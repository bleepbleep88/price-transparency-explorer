using MediatR;
using PriceExplorer.Api.Core.Behaviors;
using PriceExplorer.Api.Core.Exceptions;
using PriceExplorer.Api.Core.Ingestion;

namespace PriceExplorer.Tests.Ingestion;

public sealed class IngestionStatusTests
{
    [Fact]
    public async Task Watchers_get_the_current_status_and_then_every_change()
    {
        var status = new IngestionStatus(TimeProvider.System);
        var ct = TestContext.Current.CancellationToken;
        await using var updates = status.WatchAsync(ct).GetAsyncEnumerator(ct);

        (await updates.MoveNextAsync()).Should().BeTrue();
        updates.Current.Phase.Should().Be(IngestionPhase.Starting);

        status.Update(s => s with { Phase = IngestionPhase.Ready, DataAvailable = true, Message = "Data is ready." });

        (await updates.MoveNextAsync()).Should().BeTrue();
        updates.Current.Phase.Should().Be(IngestionPhase.Ready);
        status.WhenDataAvailable.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task Requests_that_need_data_fail_with_503_until_data_is_available()
    {
        var status = new IngestionStatus(TimeProvider.System);
        var behavior = new RequireDataBehavior<NeedsData, string>(status);
        RequestHandlerDelegate<string> next = _ => Task.FromResult("ran");

        var whileLoading = () => behavior.Handle(new NeedsData(), next, TestContext.Current.CancellationToken);
        await whileLoading.Should().ThrowAsync<IndexNotReadyException>();

        status.Update(s => s with { DataAvailable = true });
        (await behavior.Handle(new NeedsData(), next, TestContext.Current.CancellationToken)).Should().Be("ran");
    }

    private sealed record NeedsData : IRequest<string>, IRequiresData;
}
