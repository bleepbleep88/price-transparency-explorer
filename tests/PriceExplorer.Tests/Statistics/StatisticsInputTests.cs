using FluentValidation.TestHelper;
using PriceExplorer.Api.Core.Ingestion;
using PriceExplorer.Api.Core.Queries.Statistics;

namespace PriceExplorer.Tests.Statistics;

public sealed class StatisticsInputTests
{
    private readonly GetGroupStatsQueryValidator _validator = new();

    [Fact]
    public void Codes_are_built_in_batches_of_about_two_thousand_rates_and_never_split()
    {
        var codes = new[] { new CodeRates("00812", 1_500), new CodeRates("29881", 400), new CodeRates("45378", 200), new CodeRates("99213", 5_000), new CodeRates("J0585", 10) };

        SnapshotStatisticsBuilder.Batches(codes).Should().BeEquivalentTo(
            new List<List<string>> { new() { "00812", "29881" }, new() { "45378" }, new() { "99213" }, new() { "J0585" } },
            options => options.WithStrictOrdering(),
            "a code with more rates than a batch holds gets a batch of its own");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(new[] { "45378", "00812", "J0585", "0490" }, null)]
    [InlineData(null, "46-0857425")]
    [InlineData(null, "u0123456789ab")]
    [InlineData(null, "npi:1234567890")]
    public void Codes_in_stored_form_and_provider_keys_from_results_are_accepted(string[]? codes, string? providerKey)
    {
        _validator.TestValidate(new GetGroupStatsQuery(Codes: codes, ProviderKey: providerKey)).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(new string[0], null, "Codes")]
    [InlineData(new[] { "45378", "4537 8" }, null, "Codes[1]")]
    [InlineData(new[] { "123456789" }, null, "Codes[0]")]
    [InlineData(null, "not a key", "ProviderKey")]
    [InlineData(null, "46-085742", "ProviderKey")]
    public void Malformed_codes_and_provider_keys_are_rejected(string[]? codes, string? providerKey, string property)
    {
        _validator.TestValidate(new GetGroupStatsQuery(Codes: codes, ProviderKey: providerKey)).ShouldHaveValidationErrorFor(property);
    }

    [Fact]
    public void A_snapshot_id_must_be_positive()
    {
        _validator.TestValidate(new GetGroupStatsQuery(Snapshot: 0)).ShouldHaveValidationErrorFor(query => query.Snapshot);
    }
}
