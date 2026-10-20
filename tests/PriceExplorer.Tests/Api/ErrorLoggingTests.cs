using System.Net;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Queries.Rates;
using PriceExplorer.Tests.Support;

namespace PriceExplorer.Tests.Api;

/// <summary>An unexpected exception reaches the client as a 500 problem and the log exactly once.</summary>
public sealed class ErrorLoggingTests(ErrorLoggingTests.Factory factory) : IClassFixture<ErrorLoggingTests.Factory>
{
    private const string Failure = "Reference lists failed on purpose.";

    /// <summary>The real API, where reading the filter options fails unexpectedly.</summary>
    public sealed class Factory : LogCaptureApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(services =>
                services.AddTransient<IPipelineBehavior<GetReferenceListsQuery, ReferenceLists>, FailingBehavior>());
        }
    }

    private sealed class FailingBehavior : IPipelineBehavior<GetReferenceListsQuery, ReferenceLists>
    {
        public Task<ReferenceLists> Handle(
            GetReferenceListsQuery request, RequestHandlerDelegate<ReferenceLists> next, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Failure);
    }

    [Fact(Timeout = 60_000)]
    public async Task An_unexpected_exception_is_logged_once()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = factory.CreateClient();
        await factory.WaitForIngestionAsync(ct);

        using var response = await client.GetAsync("/api/v1/reference", ct);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        factory.Events.Where(logged => logged.Exception?.Message == Failure).Should().ContainSingle();
    }
}
