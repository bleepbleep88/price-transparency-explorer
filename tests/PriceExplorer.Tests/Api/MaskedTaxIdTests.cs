using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DuckDB.NET.Data;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.TicFileBuilder;

namespace PriceExplorer.Tests.Api;

/// <summary>
/// A tax ID with a prefix the IRS never issues may be a Social Security number, so results identify it by an opaque key.
/// The Essential Plan file is swapped for a small synthetic one with such a tax ID (07-1234567).
/// </summary>
public sealed class MaskedTaxIdTests
{
    private const string MaskedEin = "071234567";

    [Fact(Timeout = 120_000)]
    public async Task A_masked_key_cant_be_recomputed_from_the_number_and_stays_the_same_after_a_rebuild()
    {
        var ct = TestContext.Current.CancellationToken;
        var directory = Directory.CreateTempSubdirectory("pte-keys-").FullName;
        try
        {
            string key;
            await using (var first = ExplorerApiFactory.With(ServeMaskedClinic, directory))
            {
                key = await KeyForAsync(first, ct);
            }

            key.Should().MatchRegex("^u[0-9a-f]{12}$");
            key.Should().NotBe("u" + Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(MaskedEin)))[..12],
                "a plain hash of a 9-digit number is reversed by trying every number");

            // An older schema version makes the next start rebuild every table from the download cache.
            await using (var connection = new DuckDBConnection($"Data Source={Path.Combine(directory, "explorer.duckdb")}"))
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE meta SET value = '2' WHERE key = 'schema_version'";
                await command.ExecuteNonQueryAsync(ct);
            }

            await using var second = ExplorerApiFactory.With(ServeMaskedClinic, directory);
            (await KeyForAsync(second, ct)).Should().Be(key, "links that carry the key keep working after a rebuild");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void ServeMaskedClinic(FixtureHttpHandler http) =>
        http.ServeJson(FixtureHttpHandler.EssentialUrl, File(
            [Minkowitz, CountyClinicWholeTin],
            [Item("CPT", "88305", "Tissue exam by pathologist", "[1, 2]", Price(rate: "41.50", serviceCode: "[\"11\"]", billingClass: "professional"))]));

    /// <summary>The clinic's key, found by its full number and found again by the key itself.</summary>
    private static async Task<string> KeyForAsync(ExplorerApiFactory factory, CancellationToken ct)
    {
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        var byNumber = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rates?code=88305&ein={MaskedEin}", ct);
        var row = byNumber.GetProperty("rows").GetProperty("items").EnumerateArray().Single();
        row.GetProperty("einMasked").GetBoolean().Should().BeTrue();
        row.GetProperty("ein").ValueKind.Should().Be(JsonValueKind.Null);
        row.GetProperty("einDisplay").GetString().Should().Be("••-•••4567");
        var key = row.GetProperty("providerKey").GetString()!;

        var byKey = await client.GetFromJsonAsync<JsonElement>($"/api/v1/rates?code=88305&ein={key}", ct);
        byKey.GetProperty("rows").GetProperty("items").EnumerateArray().Single().GetProperty("providerKey").GetString().Should().Be(key);
        return key;
    }
}
