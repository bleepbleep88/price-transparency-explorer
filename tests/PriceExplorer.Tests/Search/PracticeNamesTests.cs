using Dapper;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PriceExplorer.Api.Configuration;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Handlers.Sources;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Models.Sources;
using PriceExplorer.Api.Core.Queries.Sources;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Tests.Search;

/// <summary>
/// The practice name rules on their own (PracticeNames.sql): provider groups and registry entries written row by row
/// into an empty database, then practice_name() for each. PracticeNameTests covers the API end to end.
/// </summary>
public sealed class PracticeNamesTests : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("pte-names-").FullName;
    private DuckDbConnectionFactory _connections = null!;

    public async ValueTask InitializeAsync()
    {
        _connections = new DuckDbConnectionFactory(new DataPaths(
            Options.Create(new IngestionOptions { DataDirectory = _directory }), new TestHost(_directory)));
        await ExecuteAsync(SqlFile.For<DuckDbConnectionFactory>("Schema.sql"));
        await ExecuteAsync("CREATE TABLE provider_directory (npi BIGINT, entity_type VARCHAR, name VARCHAR, credential VARCHAR, deactivated BOOLEAN)");
        await ExecuteAsync(
            """
            INSERT INTO snapshots (snapshot_id, index_url, index_date, status) VALUES
                (1, 'https://fixtures.test/2026-09-28_index.json', DATE '2026-09-28', 'ready'),
                (2, 'https://fixtures.test/2026-08-31_index.json', DATE '2026-08-31', 'ready'),
                (3, 'https://fixtures.test/2026-07-27_index.json', DATE '2026-07-27', 'ready')
            """);
        await using var connection = await _connections.OpenAsync(TestContext.Current.CancellationToken);
        await PracticeNames.EnsureAsync(connection, TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _connections.Dispose();
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task A_shared_name_gives_way_to_the_practices_own_from_the_files_then_the_registry_then_a_lone_clinician()
    {
        await FileAsync(1, snapshot: 1, "ambetter");
        await FileAsync(2, snapshot: 1, "essential");
        await GroupAsync(1, "k0", "Big IPA", 9001);
        await GroupAsync(1, "k0", "Big IPA", 9002);
        await GroupAsync(1, "k1", "Big IPA", 1001);
        await GroupAsync(2, "k1", "K1 Practice PC", 1001);
        await GroupAsync(1, "k2", "Big IPA", 2001, 2002);
        await GroupAsync(1, "k3", "big  ipa ", 3001);
        await GroupAsync(1, "k4", "Big IPA", 4001, 4002);
        await GroupAsync(1, "k5", "Big IPA", 5001);
        await RegisteredAsync(2001, "organization", "K2 Clinic Llc");
        await RegisteredAsync(2002, "individual", "Ann Lee", "MD");
        await RegisteredAsync(3001, "individual", "Jane Roe", "DO");
        await RegisteredAsync(4001, "individual", "Bo Kim", "MD");
        await RegisteredAsync(4002, "individual", "Cy Ng", "NP");
        await RegisteredAsync(5001, "individual", "Di Ma", "MD", deactivated: true);

        await RefreshAsync();

        // k0 uses the name most (two groups): it's the name's home and keeps it. Spaces and case don't make a new name.
        (await ShownAsync(1, "k0", "Big IPA")).Should().Be("Big IPA");
        (await ShownAsync(1, "k1", "Big IPA")).Should().Be("K1 Practice PC");
        (await ShownAsync(2, "k1", "K1 Practice PC")).Should().Be("K1 Practice PC");
        (await ShownAsync(1, "k2", "Big IPA")).Should().Be("K2 Clinic Llc");
        (await ShownAsync(1, "k3", "big  ipa ")).Should().Be("Jane Roe, DO");
        (await ShownAsync(1, "k4", "Big IPA")).Should().Be("Big IPA", "two clinicians and no organization: no name of its own");
        (await ShownAsync(1, "k5", "Big IPA")).Should().Be("Big IPA", "a deactivated NPI doesn't name the practice");
        (await QueryAsync<(string, string)>("SELECT provider_key, source FROM practice_names ORDER BY 1")).Should().Equal(
            ("k1", "plan"), ("k2", "organization"), ("k3", "clinician"));
    }

    [Fact]
    public async Task A_name_on_four_tax_ids_is_one_practices_and_stays()
    {
        await FileAsync(1, snapshot: 1, "ambetter");
        await FileAsync(2, snapshot: 1, "essential");
        foreach (var key in new[] { "k1", "k2", "k3", "k4" })
        {
            await GroupAsync(1, key, "Four Doctors Group", 1000);
        }

        await GroupAsync(2, "k1", "K1 Practice PC", 1000);
        await RefreshAsync();

        (await ShownAsync(1, "k1", "Four Doctors Group")).Should().Be("Four Doctors Group");
    }

    [Fact]
    public async Task A_practices_own_name_comes_from_its_own_month_first_then_the_nearest()
    {
        await FileAsync(1, snapshot: 1, "ambetter");
        await FileAsync(2, snapshot: 2, "ambetter");
        await FileAsync(3, snapshot: 3, "ambetter");
        foreach (var key in new[] { "k0", "k0", "k1", "k2", "k3", "k4" })
        {
            await GroupAsync(1, key, "Big IPA", 1000);
        }

        await GroupAsync(2, "k1", "K1 August Name", 1000);
        await GroupAsync(3, "k1", "K1 July Name", 1000);
        await RefreshAsync();

        (await ShownAsync(1, "k1", "Big IPA")).Should().Be("K1 August Name");
    }

    [Fact]
    public async Task A_file_whose_names_no_other_file_shares_takes_each_tax_IDs_name_from_the_others()
    {
        await FileAsync(1, snapshot: 1, "ambetter");
        await FileAsync(2, snapshot: 1, "essential");
        await FileAsync(3, snapshot: 2, "ambetter");
        await ManyAsync(1, "'Practice ' || i");
        await ManyAsync(3, "'Practice ' || i");
        await ManyAsync(2, "'Shuffled ' || ((i + 7) % 120)");
        await RefreshAsync();

        (await ShownAsync(2, "t5", "Shuffled 12")).Should().Be("Practice 5");
        (await ShownAsync(1, "t5", "Practice 5")).Should().Be("Practice 5");
        (await QueryAsync<int>("SELECT DISTINCT file_id FROM practice_names")).Should().Equal(2);
    }

    [Fact]
    public async Task Two_files_that_disagree_only_with_each_other_both_keep_their_names()
    {
        await FileAsync(1, snapshot: 1, "ambetter");
        await FileAsync(2, snapshot: 1, "essential");
        await ManyAsync(1, "'Practice ' || i");
        await ManyAsync(2, "'Shuffled ' || ((i + 7) % 120)");
        await RefreshAsync();

        (await QueryAsync<long>("SELECT count(*) FROM practice_names")).Should().Equal(0);
    }

    [Fact]
    public async Task A_month_with_one_plan_takes_names_from_the_nearest_month_then_the_registry_then_keeps_the_plans()
    {
        // Sep 28 has only the Essential Plan file, and its names match neither other month's.
        await FileAsync(1, snapshot: 1, "essential");
        await FileAsync(2, snapshot: 2, "essential");
        await FileAsync(3, snapshot: 3, "essential");
        await ManyAsync(2, "'Practice ' || i");
        await ManyAsync(3, "'Old Practice ' || i");
        await ManyAsync(1, "'Shuffled ' || ((i + 7) % 120)");
        await ManyAsync(3, "'Practice ' || i", firstGroup: 1000);
        // Tax IDs only Sep 28 lists: the registry names them when it can.
        await GroupAsync(1, "x1", "Shuffled A", 7001, 7002);
        await GroupAsync(1, "x2", "Shuffled B", 7003);
        await GroupAsync(1, "x3", "Shuffled C", 7004, 7005);
        await RegisteredAsync(7001, "organization", "X1 Health Center Inc");
        await RegisteredAsync(7002, "individual", "Al Bo", "MD");
        await RegisteredAsync(7003, "individual", "Cy Do", "NP");
        await RegisteredAsync(7004, "individual", "Ed Fu", "MD");
        await RegisteredAsync(7005, "individual", "Gu Ha", "MD");
        await RefreshAsync();

        (await ShownAsync(1, "t5", "Shuffled 12")).Should().Be("Practice 5", "Aug 31 is nearer than Jul 27");
        (await QueryAsync<int>("SELECT source_file_id FROM practice_names WHERE file_id = 1 AND provider_key = 't5'")).Should().Equal(2);
        (await ShownAsync(1, "x1", "Shuffled A")).Should().Be("X1 Health Center Inc");
        (await ShownAsync(1, "x2", "Shuffled B")).Should().Be("Cy Do, NP");
        (await ShownAsync(1, "x3", "Shuffled C")).Should().Be("Shuffled C");

        // The sources page says so, and cites the file the names came from.
        var file = (await new ListSourcesHandler(_connections).Handle(new ListSourcesQuery(), TestContext.Current.CancellationToken))
            .SelectMany(snapshot => snapshot.Files).Single(candidate => candidate.FileId == 1);
        file.ProviderNames.Should().BeEquivalentTo(new SourceFileNames(0, 100, [new SourceFileRef("essential", new DateOnly(2026, 8, 31))]));
    }

    [Fact]
    public async Task The_oldest_month_takes_names_from_the_next_one()
    {
        await FileAsync(1, snapshot: 1, "essential");
        await FileAsync(2, snapshot: 2, "essential");
        await FileAsync(3, snapshot: 3, "essential");
        await ManyAsync(1, "'Practice ' || i");
        await ManyAsync(2, "'Practice ' || i");
        await ManyAsync(3, "'Shuffled ' || ((i + 7) % 120)");
        await RefreshAsync();

        (await ShownAsync(3, "t5", "Shuffled 12")).Should().Be("Practice 5");
        (await QueryAsync<int>("SELECT DISTINCT source_file_id FROM practice_names")).Should().Equal(2);
    }

    [Fact]
    public async Task Another_month_lends_its_name_from_the_same_plan_first()
    {
        await FileAsync(1, snapshot: 1, "ambetter");
        await FileAsync(2, snapshot: 2, "essential");
        await FileAsync(3, snapshot: 2, "ambetter");
        foreach (var key in new[] { "k0", "k0", "k1", "k2", "k3", "k4" })
        {
            await GroupAsync(1, key, "Big IPA", 1000);
        }

        await GroupAsync(2, "k1", "K1 Essential Name", 1000);
        await GroupAsync(3, "k1", "K1 Ambetter Name", 1000);
        await RefreshAsync();

        (await ShownAsync(1, "k1", "Big IPA")).Should().Be("K1 Ambetter Name");
    }

    [Fact]
    public async Task Statistics_go_stale_when_a_snapshots_shown_names_change()
    {
        await FileAsync(1, snapshot: 1, "ambetter");
        await FileAsync(2, snapshot: 1, "essential");
        foreach (var key in new[] { "k0", "k0", "k1", "k2", "k3", "k4" })
        {
            await GroupAsync(1, key, "Big IPA", 1000);
        }

        await RefreshAsync();
        var before = await RateFilesAsync();
        before.Should().NotContain(" names:", "no name is replaced yet");

        await GroupAsync(2, "k1", "K1 Practice PC", 1000);
        await RefreshAsync();
        (await RateFilesAsync()).Should().StartWith(before + " names:");
    }

    private async Task RefreshAsync() =>
        await new PracticeNames(_connections, NullLogger<PracticeNames>.Instance).RefreshAsync(TestContext.Current.CancellationToken);

    private async Task<string> ShownAsync(int file, string key, string name) =>
        (await QueryAsync<string>("SELECT practice_name($file, $key, $name)", new { file, key, name })).Single();

    private async Task<string> RateFilesAsync()
    {
        await using var connection = await _connections.OpenAsync(TestContext.Current.CancellationToken);
        return (await SavedStatistics.StateAsync(connection, 1)).Single().RateFiles;
    }

    private Task FileAsync(int file, int snapshot, string product) => ExecuteAsync(
        """
        INSERT INTO source_files (file_id, snapshot_id, kind, structure_index, product_key, product_label, url, status, loaded_at)
        VALUES ($file, $snapshot, 'in_network', 0, $product, $product, concat('https://fixtures.test/', $file, '.json'), 'loaded',
                TIMESTAMP '2026-10-01 12:00:00')
        """, new { file, snapshot, product });

    /// <summary>A provider group (its own provider reference) for <paramref name="key"/> listing these NPIs.</summary>
    private async Task GroupAsync(int file, string key, string name, params long[] npis)
    {
        await ExecuteAsync(
            """
            INSERT INTO provider_groups (file_id, provider_ref_id, group_idx, tin_type, tin_value, ein, business_name, npi_count,
                                         ein_level, ein_unverified, network_names, provider_key)
            SELECT $file, coalesce(max(provider_ref_id), 0) + 1, 0, 'ein', $key, $key, $name, $npis, false, false, NULL, $key
            FROM provider_groups WHERE file_id = $file
            """, new { file, key, name, npis = npis.Length });
        foreach (var npi in npis)
        {
            await ExecuteAsync(
                """
                INSERT INTO group_npis (file_id, provider_ref_id, group_idx, npi, npi_valid)
                SELECT $file, max(provider_ref_id), 0, $npi, true FROM provider_groups WHERE file_id = $file
                """, new { file, npi });
        }
    }

    /// <summary>
    /// Enough tax IDs to compare files by (t0, t1, …), one group each from <paramref name="firstGroup"/>, named by
    /// <paramref name="nameSql"/> (a SQL expression of i).
    /// </summary>
    private Task ManyAsync(int file, string nameSql, int firstGroup = 0) => ExecuteAsync(
        $"""
        INSERT INTO provider_groups (file_id, provider_ref_id, group_idx, tin_type, tin_value, ein, business_name, npi_count,
                                     ein_level, ein_unverified, network_names, provider_key)
        SELECT $file, $firstGroup + i, 0, 'ein', 't' || i, 't' || i, {nameSql}, 1, false, false, NULL, 't' || i
        FROM range($count) r(i)
        """, new { file, firstGroup, count = PracticeNames.ComparableTaxIds + 20 });

    private Task RegisteredAsync(long npi, string type, string name, string? credential = null, bool deactivated = false) => ExecuteAsync(
        "INSERT INTO provider_directory VALUES ($npi, $type, $name, $credential, $deactivated)", new { npi, type, name, credential, deactivated });

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object? args = null)
    {
        await using var connection = await _connections.OpenAsync(TestContext.Current.CancellationToken);
        return (await connection.QueryAsync<T>(sql, args)).ToList();
    }

    private async Task ExecuteAsync(string sql, object? args = null)
    {
        await using var connection = await _connections.OpenAsync(TestContext.Current.CancellationToken);
        await connection.ExecuteAsync(sql, args);
    }

    private sealed class TestHost(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "PriceExplorer.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
