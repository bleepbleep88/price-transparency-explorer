using Dapper;
using Microsoft.Extensions.DependencyInjection;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Queries.Statistics;
using PriceExplorer.Tests.Support;
using static PriceExplorer.Tests.Support.TicFileBuilder;

namespace PriceExplorer.Tests.Statistics;

/// <summary>
/// Saved values follow the search's rules in cases the real slices don't have. The Essential Plan file is swapped for a
/// small synthetic one; each test compares what's saved with what search returns, then checks the case itself.
/// </summary>
public sealed class SavedValuesTests
{
    [Fact(Timeout = 60_000)]
    public async Task A_provider_with_two_rates_in_a_group_is_saved_at_its_weighted_median_with_its_range()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ServingEssential(File(
            [Minkowitz],
            [Item("CPT", "88305", "Tissue exam by pathologist", "[1]",
                Price(rate: "41.50", serviceCode: "[\"11\"]", billingClass: "professional"),
                Price(rate: "55.00", serviceCode: "[\"11\"]", billingClass: "professional"))]));

        var saved = await SearchComparison.SavedEqualsSearchAsync(factory, "88305", ct);

        var pathologists = saved.Groups.Should().ContainSingle().Subject.Providers.Should().ContainSingle().Subject;
        (pathologists.Value, pathologists.Low, pathologists.High, pathologists.RateCount).Should().Be((41.50m, 41.50m, 55.00m, 2),
            "two rates with one NPI each: the lower median, never the $48.25 midpoint");
        pathologists.NpiWeight.Should().Be(2);
    }

    [Fact(Timeout = 60_000)]
    public async Task A_masked_tax_id_stays_masked_in_the_saved_values()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ServingEssential(File(
            [Minkowitz, CountyClinicWholeTin],
            [Item("CPT", "88305", "Tissue exam by pathologist", "[1, 2]", Price(rate: "41.50", serviceCode: "[\"11\"]", billingClass: "professional"))]));

        var saved = await SearchComparison.SavedEqualsSearchAsync(factory, "88305", ct);

        var clinic = saved.Groups.Single().Providers.Should().ContainSingle(value => value.EinMasked).Subject;
        clinic.Ein.Should().BeNull();
        clinic.EinDisplay.Should().Be("••-•••4567");
        clinic.ProviderKey.Should().MatchRegex("^u[0-9a-f]{12}$");
        clinic.EinLevel.Should().BeTrue("the clinic's contract covers its whole tax ID (npi [0])");
        await using var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct);
        (await connection.ExecuteScalarAsync<long>(
            "SELECT count(*) FROM provider_values WHERE ein = '071234567' OR contains(ein_display, '1234567')"))
            .Should().Be(0, "the full number is never copied into the saved values");
    }

    [Fact(Timeout = 60_000)]
    public async Task Expired_rates_are_left_out_and_the_next_end_date_rebuilds_the_statistics_once_it_passes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = ServingEssential(File(
            [Minkowitz, CountyClinicWholeTin],
            [
                Item("CPT", "88305", "Tissue exam by pathologist", "[1]",
                    Price(rate: "41.50", serviceCode: "[\"11\"]", billingClass: "professional", expiration: "2098-06-30")),
                Item("CPT", "88305", "Tissue exam by pathologist", "[2]",
                    Price(rate: "55.00", serviceCode: "[\"11\"]", billingClass: "professional", expiration: "2099-12-31")),
                Item("CPT", "88305", "Tissue exam by pathologist", "[1]",
                    Price(rate: "39.00", serviceCode: "[\"11\"]", billingClass: "professional", expiration: "2020-01-01")),
            ]));

        var saved = await SearchComparison.SavedEqualsSearchAsync(factory, "88305", ct);

        saved.Groups.Single().Providers.Select(value => value.Value).Should().BeEquivalentTo([41.50m, 55.00m],
            "the rate that ended in 2020 is in neither search nor the saved statistics");
        saved.ExpiresOn.Should().Be(new DateOnly(2098, 6, 30), "the earliest end date among the rates still shown");

        // When that day passes, search stops showing the rate: readers are told at once, and the next check rebuilds.
        await using (var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct))
        {
            await connection.ExecuteAsync("UPDATE stats_builds SET expires_on = DATE '2020-01-02'");
        }

        var expired = await factory.SendAsync(new GetGroupStatsQuery(Codes: ["88305"]), ct);
        await factory.Services.GetRequiredService<IngestionCoordinator>().BuildStatisticsAsync(ct);
        var rebuilt = await factory.SendAsync(new GetGroupStatsQuery(Codes: ["88305"]), ct);

        (expired.Current, expired.ExpiresOn).Should().Be((false, new DateOnly(2020, 1, 2)));
        (rebuilt.Current, rebuilt.ExpiresOn).Should().Be((true, new DateOnly(2098, 6, 30)));
        rebuilt.BuiltAt.Should().BeAfter(saved.BuiltAt!.Value);
    }

    private static ExplorerApiFactory ServingEssential(string json) =>
        ExplorerApiFactory.With(http => http.ServeJson(FixtureHttpHandler.EssentialUrl, json));
}
