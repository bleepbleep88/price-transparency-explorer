using Dapper;
using DuckDB.NET.Data;

namespace PriceExplorer.Tests.Api;

/// <summary>Checks the shipped reference/provider_directory.csv.gz itself, for places the ZIP-to-county file gets wrong.</summary>
public sealed class BundledDirectoryTests
{
    [Theory]
    // Roswell Park's ZIP 14263 is a unique ZIP the Census file lacks; Buffalo's other NPIs are in Erie County.
    [InlineData(1548315401L, "Erie County")]
    // Valhalla, NY with a Virginia ZIP (22033) takes Valhalla's county, not Fairfax's.
    [InlineData(1255652772L, "Westchester County")]
    // Rota, Spain: its postal code 11530 is also a Nassau County ZIP.
    [InlineData(1962821579L, null)]
    public async Task A_practice_gets_its_citys_county_when_its_zip_cannot_place_it(long npi, string? county)
    {
        await using var connection = await OpenAsync();
        var found = await connection.QuerySingleAsync<string?>(
            $"SELECT nullif(county, '') FROM {DirectoryCsv} WHERE npi = $npi", new { npi = npi.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        found.Should().Be(county);
    }

    [Fact]
    public async Task Almost_every_new_york_practice_has_a_county()
    {
        await using var connection = await OpenAsync();
        var missing = await connection.ExecuteScalarAsync<long>($"SELECT count(*) FROM {DirectoryCsv} WHERE state = 'NY' AND coalesce(county, '') = ''");

        missing.Should().BeLessThan(10, "only addresses no ZIP or city can place are left without a county");
    }

    private static string DirectoryCsv =>
        $"read_csv('{Path.Combine(AppContext.BaseDirectory, "reference", "provider_directory.csv.gz").Replace("'", "''", StringComparison.Ordinal)}', header = true, all_varchar = true)";

    private static async Task<DuckDBConnection> OpenAsync()
    {
        var connection = new DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}

/// <summary>Checks the shipped CMS code-set files: names and categories, and nothing licensed.</summary>
public sealed class BundledCodeSetsTests
{
    [Fact]
    public async Task Hcpcs_names_cover_level_two_codes_only_never_cpt_or_dental()
    {
        await using var connection = await OpenAsync();
        var propofol = await connection.QuerySingleAsync<string>($"SELECT description FROM {Csv("hcpcs_names")} WHERE code = 'J2704'");
        var licensed = await connection.ExecuteScalarAsync<long>(
            $"SELECT count(*) FROM {Csv("hcpcs_names")} WHERE NOT regexp_matches(code, '^[A-CE-V][0-9]{{4}}$')");

        propofol.Should().Be("Injection, propofol, 10 mg");
        licensed.Should().Be(0, "CPT descriptors are the AMA's and dental D codes the ADA's");
    }

    [Fact]
    public async Task Rbcs_classes_every_code_once_with_no_descriptors()
    {
        await using var connection = await OpenAsync();
        var columns = (await connection.QueryAsync<string>($"SELECT column_name FROM (DESCRIBE SELECT * FROM {Csv("rbcs")})")).ToList();
        var duplicates = await connection.ExecuteScalarAsync<long>($"SELECT count(*) - count(DISTINCT code) FROM {Csv("rbcs")}");
        var colonoscopy = await connection.QuerySingleAsync<string>($"SELECT subcategory_code || family_code FROM {Csv("rbcs")} WHERE code = '45378'");

        columns.Should().Equal("code", "rbcs_id", "category_code", "category", "subcategory_code", "subcategory", "family_code", "family");
        duplicates.Should().Be(0);
        colonoscopy.Should().Be("PG004");
    }

    private static string Csv(string name) =>
        $"read_csv('{Path.Combine(AppContext.BaseDirectory, "reference", $"{name}.csv.gz").Replace("'", "''", StringComparison.Ordinal)}', header = true, all_varchar = true)";

    private static async Task<DuckDBConnection> OpenAsync()
    {
        var connection = new DuckDBConnection("Data Source=:memory:");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        return connection;
    }
}
