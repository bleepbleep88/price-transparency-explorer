using Dapper;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PriceExplorer.Api.Configuration;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Exceptions;
using PriceExplorer.Api.Core.Handlers.History;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Models.History;
using PriceExplorer.Api.Core.Queries.History;
using PriceExplorer.Api.Core.Reference;

namespace PriceExplorer.Tests.History;

/// <summary>
/// The change rules on their own: saved provider values written row by row into an empty database (two snapshots,
/// 2026-08-31 and 2026-09-28), then the changes handler. The API tests cover the same feed end to end.
/// </summary>
public sealed class ChangeDetectionTests : IAsyncLifetime
{
    private const string Ambetter = "ambetter";
    private const string Essential = "essential";
    private const string Colonoscopy = "ambetter|CPT|45378|facility|asc||negotiated";
    private const int August = 1;
    private const int September = 2;

    private readonly string _directory = Directory.CreateTempSubdirectory("pte-changes-").FullName;
    private DuckDbConnectionFactory _connections = null!;
    private long _nextRateId = 1;

    public async ValueTask InitializeAsync()
    {
        _connections = new DuckDbConnectionFactory(new DataPaths(
            Options.Create(new IngestionOptions { DataDirectory = _directory }), new TestHost(_directory)));
        await ExecuteAsync(SqlFile.For<DuckDbConnectionFactory>("Schema.sql"));
        await ExecuteAsync(
            """
            INSERT INTO snapshots (snapshot_id, index_url, index_date, status) VALUES
                (1, 'https://fixtures.test/2026-08-31_index.json', DATE '2026-08-31', 'ready'),
                (2, 'https://fixtures.test/2026-09-28_index.json', DATE '2026-09-28', 'ready')
            """);
        await FileAsync(August, Ambetter);
        await FileAsync(September, Ambetter);
        // Both snapshots' statistics are built from these files; files a test adds later leave them built, not current.
        await MarkStatisticsCurrentAsync(August);
        await MarkStatisticsCurrentAsync(September);
    }

    public ValueTask DisposeAsync()
    {
        _connections.Dispose();
        Directory.Delete(_directory, recursive: true);
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Each_pair_of_values_is_changed_new_dropped_or_unchanged()
    {
        await ValueAsync(August, Colonoscopy, "111111111", 600m);
        await ValueAsync(September, Colonoscopy, "111111111", 660m);
        await ValueAsync(August, Colonoscopy, "222222222", 900m);
        await ValueAsync(September, Colonoscopy, "222222222", 900m);
        await ValueAsync(August, Colonoscopy, "333333333", 450m);
        await ValueAsync(September, Colonoscopy, "444444444", 750m);

        var result = await ChangesAsync(new GetChangesQuery());

        result.Counts.Should().Be(new ChangeCounts(
            Changed: 1, Increased: 1, Decreased: 0, New: 1, NewListedElsewhere: 0, Dropped: 1, DroppedListedElsewhere: 0, Unchanged: 1));
        result.Rows.Items.Select(row => (row.Kind, row.ProviderKey, row.From, row.To, row.Change, row.ChangePct)).Should().Equal(
            ("changed", "111111111", 600m, 660m, 60m, 10m),
            ("new", "444444444", null, 750m, null, null),
            ("dropped", "333333333", 450m, null, null, null));
        result.Rows.Total.Should().Be(3);
    }

    [Fact]
    public async Task A_change_from_zero_has_no_percentage_so_a_minimum_leaves_it_out()
    {
        await ValueAsync(August, Colonoscopy, "111111111", 0m);
        await ValueAsync(September, Colonoscopy, "111111111", 50m);

        var all = await ChangesAsync(new GetChangesQuery());
        var big = await ChangesAsync(new GetChangesQuery(MinChangePct: 1));

        all.Rows.Items.Should().ContainSingle().Which.Should().Match<RateChange>(row => row.Change == 50m && row.ChangePct == null);
        big.Counts.Changed.Should().Be(0);
        big.Rows.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData(5.0, 1)]
    [InlineData(5.1, 0)]
    public async Task A_minimum_applies_to_the_percentage_as_shown_rounded_to_a_tenth(double minimum, int changed)
    {
        // 100.21 → 95.21 is −4.99%, shown as −5.0%: a 5% minimum keeps it, 5.1% doesn't.
        await PairAsync(Colonoscopy, "111111111", 100.21m, 95.21m);

        var result = await ChangesAsync(new GetChangesQuery(MinChangePct: (decimal)minimum));

        result.Counts.Changed.Should().Be(changed);
        result.Rows.Items.Select(row => row.ChangePct).Should().Equal(changed == 1 ? [-5.0m] : []);
    }

    [Theory]
    [InlineData("490", new[] { "0490", "00490" })]
    [InlineData("RC 490", new[] { "0490" })]
    [InlineData("cpt 490", new[] { "00490" })]
    [InlineData("00490", new[] { "00490" })]
    public async Task A_code_is_read_as_search_reads_it(string code, string[] matched)
    {
        await PairAsync("ambetter|RC|0490|facility|asc||negotiated", "111111111", 900m, 990m);
        await PairAsync("ambetter|CPT|00490|professional|asc||negotiated", "111111111", 9m, 10m);

        var result = await ChangesAsync(new GetChangesQuery(Code: code));

        result.Rows.Items.Select(row => row.Code).Should().BeEquivalentTo(matched);
    }

    [Fact]
    public async Task A_rate_listed_under_other_modifiers_is_new_and_dropped_and_says_it_is_listed_elsewhere()
    {
        // The same telehealth rate, with GQ dropped from its modifiers, as Centene did between 2026-07-27 and 2026-08-31.
        await ValueAsync(August, "ambetter|CPT|99213|professional|telehealth-other|95,GQ,GT|negotiated", "111111111", 75m);
        await ValueAsync(September, "ambetter|CPT|99213|professional|telehealth-other|95,GT|negotiated", "111111111", 75m);
        // Another code for the same provider, and another provider for the same code: really new and dropped.
        await ValueAsync(August, "ambetter|CPT|99214|professional|telehealth-other|95,GT|negotiated", "111111111", 110m);
        await ValueAsync(September, "ambetter|CPT|99213|professional|telehealth-other|95,GT|negotiated", "222222222", 75m);

        var result = await ChangesAsync(new GetChangesQuery(Sort: "code"));

        result.Rows.Items.Select(row => (row.Kind, row.Code, row.ProviderKey, row.ListedElsewhere)).Should().BeEquivalentTo(
        [
            ("dropped", "99213", "111111111", true), ("new", "99213", "111111111", true),
            ("new", "99213", "222222222", false), ("dropped", "99214", "111111111", false),
        ]);
        result.Counts.Should().Match<ChangeCounts>(counts =>
            counts.New == 2 && counts.NewListedElsewhere == 1 && counts.Dropped == 2 && counts.DroppedListedElsewhere == 1);
        (await ChangesAsync(new GetChangesQuery(ListedElsewhere: false))).Rows.Items.Should().OnlyContain(row => !row.ListedElsewhere)
            .And.HaveCount(2);
    }

    [Fact]
    public async Task The_newer_file_names_the_provider()
    {
        await ValueAsync(August, Colonoscopy, "111111111", 600m, name: "Alpha Surgery Center");
        await ValueAsync(September, Colonoscopy, "111111111", 660m, name: "Alpha Surgery Center of Albany");

        var row = (await ChangesAsync(new GetChangesQuery())).Rows.Items.Single();

        row.BusinessName.Should().Be("Alpha Surgery Center of Albany");
    }

    [Fact]
    public async Task The_same_provider_in_another_product_is_another_comparison_group()
    {
        await FileAsync(August, Essential);
        await FileAsync(September, Essential);
        await ValueAsync(August, Colonoscopy, "111111111", 600m);
        await ValueAsync(September, "essential|CPT|45378|facility|asc||negotiated", "111111111", 600m);

        var result = await ChangesAsync(new GetChangesQuery());

        result.Rows.Items.Select(row => (row.Kind, row.ProductKey)).Should().BeEquivalentTo([("dropped", Ambetter), ("new", Essential)]);
    }

    [Fact]
    public async Task A_product_only_one_snapshot_has_is_listed_but_not_compared()
    {
        await FileAsync(September, Essential);
        await ValueAsync(September, "essential|CPT|45378|facility|asc||negotiated", "111111111", 600m);

        var result = await ChangesAsync(new GetChangesQuery(Product: Essential));

        result.Products.Should().ContainEquivalentOf(new ChangesProduct(Essential, "essential label", false, "Not in the 2026-08-31 snapshot."));
        result.Products.Should().ContainEquivalentOf(new ChangesProduct(Ambetter, "ambetter label", true, null));
        result.Counts.Should().Be(new ChangeCounts(0, 0, 0, 0, 0, 0, 0, 0), "the provider isn't new: the product wasn't in the older snapshot at all");
    }

    [Fact]
    public async Task Rate_ids_are_given_only_for_snapshots_whose_saved_statistics_are_current()
    {
        await PairAsync(Colonoscopy, "111111111", 600m, 660m);

        var current = await ChangesAsync(new GetChangesQuery());
        // As if the app was rebuilt since August's statistics: still built, no longer current.
        await ExecuteAsync("UPDATE stats_builds SET code_version = 'an-older-app' WHERE snapshot_id = 1");
        var halfCurrent = await ChangesAsync(new GetChangesQuery());

        current.Current.Should().BeTrue();
        current.Rows.Items.Single().Should().Match<RateChange>(row => row.FromRateId != null && row.ToRateId != null);
        halfCurrent.Current.Should().BeFalse("the older snapshot's statistics aren't current");
        halfCurrent.To.StatisticsCurrent.Should().BeTrue();
        halfCurrent.From.StatisticsCurrent.Should().BeFalse();
        halfCurrent.Rows.Items.Single().Should().Match<RateChange>(row => row.FromRateId == null && row.ToRateId != null);
    }

    [Fact]
    public async Task Until_both_snapshots_statistics_are_built_there_are_no_counts_only_a_503_to_try_later()
    {
        // While August's statistics are built, its saved values are empty: every September rate would read as new.
        await ValueAsync(September, Colonoscopy, "111111111", 660m);
        await ExecuteAsync("DELETE FROM stats_builds WHERE snapshot_id = 1");

        var changes = () => ChangesAsync(new GetChangesQuery());

        var problem = (await changes.Should().ThrowAsync<StatisticsNotReadyException>()).Which;
        problem.Code.Should().Be("statistics_building");
        problem.StatusCode.Should().Be(503);
        problem.Message.Should().Be("The saved statistics for 2026-08-31 aren't built yet. They're built once loading finishes.");
    }

    [Fact]
    public async Task The_biggest_moves_lead_with_procedure_codes_and_revenue_code_only_charges_follow()
    {
        // A facility charge filed under a revenue code alone states no unit, so it isn't ranked with procedure codes.
        await PairAsync("ambetter|RC|0301|facility|hospital-outpatient||negotiated", "rc-only", 20m, 1667m, name: "RC only");
        await PairAsync("ambetter|CPT|45378|facility|asc||negotiated", "cpt-small", 100m, 101m, name: "CPT small");
        await PairAsync("ambetter|CPT|99213|professional|office||negotiated", "cpt-big", 100m, 150m, name: "CPT big");

        var largest = await ChangesAsync(new GetChangesQuery());
        var byPercent = await ChangesAsync(new GetChangesQuery(Sort: "-changePct"));

        largest.Rows.Items.Select(row => row.ProviderKey).Should().Equal("cpt-big", "cpt-small", "rc-only");
        byPercent.Rows.Items.Select(row => row.ProviderKey).Should().Equal(["rc-only", "cpt-big", "cpt-small"], "only the default order sets them apart");
        largest.Counts.Changed.Should().Be(3, "revenue-code rows still count");
    }

    [Fact]
    public void A_trend_step_counts_who_stayed_who_moved_and_who_came_or_went()
    {
        var before = new Dictionary<string, decimal> { ["a"] = 100m, ["b"] = 100m, ["c"] = 50m, ["gone"] = 10m };
        var after = new Dictionary<string, decimal> { ["a"] = 110m, ["b"] = 100m, ["c"] = 40m, ["new1"] = 300m, ["new2"] = 300m };

        GetRateTrendHandler.Step(7, before, after).Should().Be(new TrendStep(7, Stayed: 3, Raised: 1, Lowered: 1, Left: 1, Joined: 2));
        GetRateTrendHandler.Step(7, before, before).Should().Be(new TrendStep(7, Stayed: 4, Raised: 0, Lowered: 0, Left: 0, Joined: 0));
    }

    [Theory]
    [InlineData("largest", "c|a|b|d|e")]
    [InlineData("-changePct", "a|b|c|d|e")]
    [InlineData("changePct", "c|b|a|d|e")]
    [InlineData("code", "a|b|c|d|e")]
    [InlineData("-code", "e|d|c|b|a")]
    [InlineData("provider", "a|b|c|d|e")]
    [InlineData("-provider", "e|d|c|b|a")]
    public async Task Every_sort_has_one_order_and_ends_on_group_and_provider(string sort, string order)
    {
        // a +10%, b −5%, c −20%; d and e are new, at the same rate.
        await PairAsync("ambetter|CPT|10001|facility|asc||negotiated", "a", 100m, 110m, name: "a");
        await PairAsync("ambetter|CPT|10002|facility|asc||negotiated", "b", 100m, 95m, name: "b");
        await PairAsync("ambetter|CPT|10003|facility|asc||negotiated", "c", 100m, 80m, name: "c");
        await ValueAsync(September, "ambetter|CPT|10004|facility|asc||negotiated", "d", 50m, name: "d");
        await ValueAsync(September, "ambetter|CPT|10005|facility|asc||negotiated", "e", 50m, name: "e");

        var result = await ChangesAsync(new GetChangesQuery(Sort: sort));

        string.Join('|', result.Rows.Items.Select(row => row.ProviderKey)).Should().Be(order);
    }

    private Task<ChangesResult> ChangesAsync(GetChangesQuery query) =>
        new GetChangesHandler(_connections, new ReferenceCatalog()).Handle(query, TestContext.Current.CancellationToken);

    private async Task PairAsync(string groupKey, string provider, decimal from, decimal to, string? name = null)
    {
        await ValueAsync(August, groupKey, provider, from, name);
        await ValueAsync(September, groupKey, provider, to, name);
    }

    /// <summary>One provider's saved value in a group, and the group's own row if it has none yet.</summary>
    private async Task ValueAsync(int snapshot, string groupKey, string provider, decimal value, string? name = null)
    {
        var parts = groupKey.Split('|');
        var args = new
        {
            snapshot, groupKey, product = parts[0], codeType = parts[1], code = parts[2], feeType = parts[3], setting = parts[4],
            modifiers = parts[5], negotiatedType = parts[6], provider, value, name = name ?? $"Provider {provider}", rateId = _nextRateId++,
        };
        await ExecuteAsync(
            """
            INSERT INTO provider_values VALUES ($snapshot, $groupKey, $product, $codeType, $code, $feeType, $setting, $modifiers,
                $negotiatedType, $provider, $provider, $provider, false, false, $name, $value, $value, $value, 1, 1, $rateId, NULL)
            """, args);
        await ExecuteAsync(
            """
            INSERT INTO group_stats
            SELECT $snapshot, $groupKey, $product, $product || ' label', $codeType, $code, $feeType, $setting, $modifiers, $negotiatedType,
                   'per procedure', false, 1, 1, 'list', $value, NULL, NULL, NULL, NULL, NULL, $value, NULL, NULL, NULL
            WHERE NOT EXISTS (SELECT 1 FROM group_stats WHERE snapshot_id = $snapshot AND group_key = $groupKey)
            """, args);
    }

    /// <summary>A saved rate file for the product, with the same counts in every snapshot (so it's complete).</summary>
    private Task FileAsync(int snapshot, string product) => ExecuteAsync(
        """
        INSERT INTO source_files (file_id, snapshot_id, kind, structure_index, product_key, product_label, url, status,
                                  rates_unique, distinct_eins, loaded_at)
        SELECT coalesce(max(file_id), 0) + 1, $snapshot, 'in_network', 0, $product, $product || ' label',
               concat('https://fixtures.test/', $snapshot, '-', $product, '.json'), 'loaded', 100, 50, TIMESTAMP '2026-10-01 12:00:00'
        FROM source_files
        """, new { snapshot, product });

    /// <summary>Records a build of the snapshot's statistics from its files as they are, by this build of the app.</summary>
    private Task MarkStatisticsCurrentAsync(int snapshot) => ExecuteAsync(
        """
        INSERT INTO stats_builds
        SELECT $snapshot,
               string_agg(concat(file_id, '@', loaded_at, '@', product_key, '@', product_label), ',' ORDER BY file_id),
               $codeVersion, NULL, TIMESTAMP '2026-10-01 12:00:00', 1, 1, 1, 1
        FROM source_files WHERE snapshot_id = $snapshot AND kind = 'in_network' AND status IN ('loaded', 'partial')
        """, new { snapshot, codeVersion = SavedStatistics.CodeVersion });

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
