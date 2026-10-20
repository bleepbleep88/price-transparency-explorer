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
