using Dapper;
using DuckDB.NET.Data;
using Microsoft.Extensions.Options;
using PriceExplorer.Api.Configuration;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Reference;

/// <summary>
/// Loads the provider directory (NPI → name, specialty, location; see reference/README.md) into DuckDB at every startup,
/// then the macro that decides which NPIs match a specialty or location filter, then each practice's own name where the
/// plan's is a network's or unreliable (<see cref="PracticeNames"/>), then CMS's HCPCS names and RBCS categories. A missing or damaged file leaves an empty table: the provider filters then match nothing and rows carry
/// no profile, codes keep only the plan's own names, and categories fall back to the code ranges. Search keeps working.
/// </summary>
internal sealed class ReferenceDataLoader(
    DuckDbConnectionFactory connections,
    ProviderDirectoryInfo directoryInfo,
    CodeReference codeReference,
    PracticeNames practiceNames,
    IOptions<ReferenceOptions> options,
    ILogger<ReferenceDataLoader> logger)
{
    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        await using var connection = await connections.OpenAsync(cancellationToken);
        directoryInfo.Set(null);

        var path = ResolvePath(options.Value.ProviderDirectoryPath);
        if (!File.Exists(path))
        {
            logger.LogWarning("Reference: provider directory not found at {Path}; specialty and location features are unavailable", path);
            await CreateEmptyDirectoryAsync(connection);
        }
        else
        {
            try
            {
                // The path comes from configuration, not from users; single quotes are escaped for the SQL literal.
                await connection.ExecuteAsync($"""
                    CREATE OR REPLACE TABLE provider_directory AS
                    SELECT * FROM read_csv('{path.Replace("'", "''", StringComparison.Ordinal)}', header = true, columns = {DirectoryColumns})
                    """);
                var count = await connection.ExecuteScalarAsync<long>("SELECT count(*) FROM provider_directory");
                // The locations list can't change until the next startup, so it's read once here.
                var locations = (await connection.QueryAsync<LocationOption>(SqlFile.For<ReferenceDataLoader>("Locations.sql"))).ToList();
                directoryInfo.Set(ProviderDirectoryInfo.Describe(SourcePathFor(path), count), locations);
                logger.LogInformation("Reference: provider directory loaded ({Count} NPIs) from {Path}", count, path);
            }
            catch (DuckDBException ex)
            {
                logger.LogWarning(ex, "Reference: couldn't read the provider directory at {Path}; specialty and location features are unavailable", path);
                await CreateEmptyDirectoryAsync(connection);
            }
        }

        await connection.ExecuteAsync(SqlFile.For<ReferenceDataLoader>("DirectoryMatches.sql"));
        await PracticeNames.EnsureAsync(connection, cancellationToken);
        await practiceNames.RefreshAsync(connection, cancellationToken);
        await LoadCodeSetsAsync(connection);
    }

    /// <summary>CMS HCPCS Level II names (a table search reads) and RBCS categories (a table, and in memory for <see cref="CodeReference"/>).</summary>
    private async Task LoadCodeSetsAsync(DuckDBConnection connection)
    {
        await LoadCsvAsync(connection, "hcpcs_names", options.Value.HcpcsNamesPath, HcpcsColumns, "code VARCHAR, description VARCHAR",
            "CMS HCPCS names", "codes keep only the plan's own names");
        await LoadCsvAsync(connection, "rbcs", options.Value.RbcsPath, RbcsColumns,
            "code VARCHAR, rbcs_id VARCHAR, category_code VARCHAR, category VARCHAR, subcategory_code VARCHAR, subcategory VARCHAR, family_code VARCHAR, family VARCHAR",
            "CMS RBCS categories", "categories fall back to code ranges");

        var classes = await connection.QueryAsync<RbcsRow>(
            "SELECT code, category_code, category, subcategory_code, subcategory, family_code, family FROM rbcs WHERE coalesce(code, '') <> ''");
        codeReference.Set(classes.Select(row => (row.Code, new RbcsClass(
            row.CategoryCode, row.Category, row.SubcategoryCode, row.Subcategory, NullIfEmpty(row.FamilyCode), NullIfEmpty(row.Family)))));
    }

    /// <summary>Loads one gzipped CSV into <paramref name="table"/>; a missing or unreadable file leaves the table empty.</summary>
    private async Task LoadCsvAsync(DuckDBConnection connection, string table, string configuredPath, string columns, string emptyColumns, string what, string fallback)
    {
        var path = ResolvePath(configuredPath);
        if (File.Exists(path))
        {
            try
            {
                // The path comes from configuration, not from users; single quotes are escaped for the SQL literal.
                await connection.ExecuteAsync($"""
                    CREATE OR REPLACE TABLE {table} AS
                    SELECT * FROM read_csv('{path.Replace("'", "''", StringComparison.Ordinal)}', header = true, columns = {columns})
                    """);
                var count = await connection.ExecuteScalarAsync<long>($"SELECT count(*) FROM {table}");
                logger.LogInformation("Reference: {What} loaded ({Count} codes) from {Path}", what, count, path);
                return;
            }
            catch (DuckDBException ex)
            {
                logger.LogWarning(ex, "Reference: couldn't read {What} at {Path}; {Fallback}", what, path, fallback);
            }
        }
        else
        {
            logger.LogWarning("Reference: {What} not found at {Path}; {Fallback}", what, path, fallback);
        }

        await connection.ExecuteAsync($"CREATE OR REPLACE TABLE {table} ({emptyColumns})");
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static Task CreateEmptyDirectoryAsync(DuckDBConnection connection) =>
        connection.ExecuteAsync($"CREATE OR REPLACE TABLE provider_directory ({EmptyDirectoryColumns})");

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    /// <summary>provider_directory.csv.gz → provider_directory.source.json next to it.</summary>
    private static string SourcePathFor(string directoryPath)
    {
        var name = Path.GetFileName(directoryPath);
        var stem = name.EndsWith(".csv.gz", StringComparison.OrdinalIgnoreCase) ? name[..^".csv.gz".Length] : Path.GetFileNameWithoutExtension(name);
        return Path.Combine(Path.GetDirectoryName(directoryPath) ?? "", $"{stem}.source.json");
    }

    private const string DirectoryColumns = """
        {'npi': 'BIGINT', 'entity_type': 'VARCHAR', 'name': 'VARCHAR', 'credential': 'VARCHAR', 'taxonomy_code': 'VARCHAR',
         'specialty': 'VARCHAR', 'specialty_group': 'VARCHAR', 'city': 'VARCHAR', 'state': 'VARCHAR', 'zip5': 'VARCHAR',
         'county_fips': 'VARCHAR', 'county': 'VARCHAR', 'borough': 'VARCHAR', 'deactivated': 'BOOLEAN', 'nppes_last_updated': 'VARCHAR'}
        """;

    private const string HcpcsColumns = "{'code': 'VARCHAR', 'description': 'VARCHAR'}";

    private const string RbcsColumns = """
        {'code': 'VARCHAR', 'rbcs_id': 'VARCHAR', 'category_code': 'VARCHAR', 'category': 'VARCHAR', 'subcategory_code': 'VARCHAR',
         'subcategory': 'VARCHAR', 'family_code': 'VARCHAR', 'family': 'VARCHAR'}
        """;

    private sealed record RbcsRow(
        string Code, string CategoryCode, string Category, string SubcategoryCode, string Subcategory, string? FamilyCode, string? Family);

    private const string EmptyDirectoryColumns = """
        npi BIGINT, entity_type VARCHAR, name VARCHAR, credential VARCHAR, taxonomy_code VARCHAR, specialty VARCHAR,
        specialty_group VARCHAR, city VARCHAR, state VARCHAR, zip5 VARCHAR, county_fips VARCHAR, county VARCHAR,
        borough VARCHAR, deactivated BOOLEAN, nppes_last_updated VARCHAR
        """;
}
