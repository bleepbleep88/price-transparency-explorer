using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PriceExplorer.Api.Core.Ingestion;

namespace PriceExplorer.Tests.Support;

/// <summary>
/// The real API, wired to a fake payer server (<see cref="FixtureHttpHandler"/>) and a throwaway data folder.
/// Ingestion runs for real: download → parse → DuckDB.
/// </summary>
public class ExplorerApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _ownsDirectory;

    public ExplorerApiFactory()
        : this(configureHttp: null, dataDirectory: null)
    {
    }

    protected ExplorerApiFactory(Action<FixtureHttpHandler>? configureHttp, string? dataDirectory)
    {
        Http = new FixtureHttpHandler();
        configureHttp?.Invoke(Http);
        _ownsDirectory = dataDirectory is null;
        DataDirectory = dataDirectory ?? Directory.CreateTempSubdirectory("pte-api-").FullName;
    }

    /// <summary>A factory with a customized fake payer server and/or a shared data folder (e.g. to simulate a restart).</summary>
    public static ExplorerApiFactory With(Action<FixtureHttpHandler>? configureHttp = null, string? dataDirectory = null) =>
        new Configured(configureHttp, dataDirectory);

    public FixtureHttpHandler Http { get; }

    public string DataDirectory { get; }

    public IngestionStatus Status => Services.GetRequiredService<IngestionStatus>();

    /// <summary>Waits (event-driven, via the status stream) until loading finishes, successfully or not.</summary>
    public async Task<IngestionStatusSnapshot> WaitForIngestionAsync(CancellationToken cancellationToken)
    {
        await foreach (var status in Status.WatchAsync(cancellationToken))
        {
            if (status.Phase is IngestionPhase.Ready or IngestionPhase.Failed)
            {
                return status;
            }
        }

        throw new InvalidOperationException("The status stream ended before loading finished.");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ingestion:DataDirectory"] = DataDirectory,
            ["Ingestion:Snapshots:0:IndexUrl"] = FixtureHttpHandler.IndexUrl,
            ["Ingestion:MaxDownloadAttempts"] = "2",
            ["Ingestion:StallTimeoutSeconds"] = "10",
            ["Serilog:MinimumLevel:Default"] = "Warning",
        }));

        builder.ConfigureTestServices(services => services
            .AddHttpClient(FileDownloader.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Http)
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        GC.SuppressFinalize(this);
        if (_ownsDirectory && Directory.Exists(DataDirectory))
        {
            Directory.Delete(DataDirectory, recursive: true);
        }
    }

    private sealed class Configured(Action<FixtureHttpHandler>? configureHttp, string? dataDirectory)
        : ExplorerApiFactory(configureHttp, dataDirectory);
}
