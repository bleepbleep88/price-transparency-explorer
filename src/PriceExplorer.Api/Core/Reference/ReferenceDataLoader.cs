using Dapper;
using DuckDB.NET.Data;
using Microsoft.Extensions.Options;
using PriceExplorer.Api.Configuration;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Models.Rates;

namespace PriceExplorer.Api.Core.Reference;

/// <summary>
/// Loads the provider directory (NPI → name, specialty, location; see reference/README.md) into DuckDB at every startup,
/// then the macro that decides which NPIs match a specialty or location filter. A missing or damaged file leaves an
/// empty table: those filters then match nothing and rows carry no profile, but search keeps working.
/// </summary>
internal sealed class ReferenceDataLoader(
    DuckDbConnectionFactory connections,
    ProviderDirectoryInfo directoryInfo,
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
    }

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

    private const string EmptyDirectoryColumns = """
        npi BIGINT, entity_type VARCHAR, name VARCHAR, credential VARCHAR, taxonomy_code VARCHAR, specialty VARCHAR,
        specialty_group VARCHAR, city VARCHAR, state VARCHAR, zip5 VARCHAR, county_fips VARCHAR, county VARCHAR,
        borough VARCHAR, deactivated BOOLEAN, nppes_last_updated VARCHAR
        """;
}
