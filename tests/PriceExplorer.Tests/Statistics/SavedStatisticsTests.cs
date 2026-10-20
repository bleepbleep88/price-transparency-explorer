using Dapper;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Queries.Statistics;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Statistics;

/// <summary>
/// The saved statistics (group_stats, provider_values) over the real Fidelis slices, read through the internal query
/// later features use. They must be the search's own numbers: every group and every row GET /api/v1/rates returns.
/// </summary>
public sealed class SavedStatisticsTests(ExplorerApiFactory factory) : IClassFixture<ExplorerApiFactory>
{
    [Theory(Timeout = 60_000)]
    [InlineData("45378")]
    [InlineData("29881")]
    [InlineData("99214")]
    [InlineData("00812")]
    public async Task Saved_groups_and_provider_values_equal_what_search_returns(string code)
    {
        await SearchComparison.SavedEqualsSearchAsync(factory, code, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 60_000)]
    public async Task Each_group_and_each_provider_in_it_is_saved_once_and_the_build_counts_them()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.WaitForIngestionAsync(ct);

        var saved = await factory.SendAsync(new GetGroupStatsQuery(), ct);

        saved.Groups.Select(group => group.Key).Should().OnlyHaveUniqueItems();
        saved.Groups.Should().OnlyContain(group =>
            group.Providers.Count == group.ProviderCount && group.Providers.Select(value => value.ProviderKey).Distinct().Count() == group.ProviderCount);

        await using var connection = await factory.Services.GetRequiredService<DuckDbConnectionFactory>().OpenAsync(ct);
        var build = await connection.QuerySingleAsync<BuildCounts>(
            "SELECT group_count AS Groups, value_count AS ProviderValues, code_count AS Codes FROM stats_builds WHERE snapshot_id = $snapshotId",
            new { snapshotId = saved.Snapshot.SnapshotId });
        build.Should().Be(new BuildCounts(
            saved.Groups.Count, saved.Groups.Sum(group => group.ProviderCount), saved.Groups.Select(group => group.Code).Distinct().Count()));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_provider_filter_returns_only_the_groups_that_provider_is_in_and_only_its_values()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.WaitForIngestionAsync(ct);
        var everyone = await factory.SendAsync(new GetGroupStatsQuery(Codes: ["45378"]), ct);

        var one = await factory.SendAsync(new GetGroupStatsQuery(Codes: ["45378"], ProviderKey: "46-0857425"), ct);

        one.Groups.Select(group => group.Key).Should().BeEquivalentTo(
            everyone.Groups.Where(group => group.Providers.Any(value => value.ProviderKey == "460857425")).Select(group => group.Key));
        one.Groups.Should().NotBeEmpty().And.OnlyContain(group => group.Providers.Single().ProviderKey == "460857425");
        one.Groups.Should().OnlyContain(group => group.Stats == everyone.Groups.Single(all => all.Key == group.Key).Stats,
            "the statistics stay market-wide");
    }

    [Fact(Timeout = 60_000)]
    public async Task Statistics_can_be_read_without_provider_values()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.WaitForIngestionAsync(ct);

        var withValues = await factory.SendAsync(new GetGroupStatsQuery(), ct);
        var withoutValues = await factory.SendAsync(new GetGroupStatsQuery(IncludeProviders: false), ct);

        withoutValues.Groups.Should().OnlyContain(group => group.Providers.Count == 0);
        withoutValues.Groups.Select(group => (group.Key, group.ProviderCount, group.Stats))
            .Should().Equal(withValues.Groups.Select(group => (group.Key, group.ProviderCount, group.Stats)));
    }

    [Fact(Timeout = 60_000)]
    public async Task Bad_input_is_rejected_before_the_query_runs()
    {
        var ct = TestContext.Current.CancellationToken;
        await factory.WaitForIngestionAsync(ct);

        var badCode = () => factory.SendAsync(new GetGroupStatsQuery(Codes: ["45378'; --"]), ct);
        var badProvider = () => factory.SendAsync(new GetGroupStatsQuery(ProviderKey: "not a key"), ct);

        await badCode.Should().ThrowAsync<ValidationException>();
        await badProvider.Should().ThrowAsync<ValidationException>();
    }

    private sealed record BuildCounts(int Groups, int ProviderValues, int Codes);
}
