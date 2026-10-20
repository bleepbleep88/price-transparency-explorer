using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PriceExplorer.Api.Configuration;
using PriceExplorer.Api.Core.Ingestion;

namespace PriceExplorer.Tests.Support;

/// <summary>
/// The real API, wired to a fake payer server (<see cref="FixtureHttpHandler"/>) and a throwaway data folder.
/// Ingestion runs for real: download → parse → DuckDB.
/// </summary>
public class ExplorerApiFactory : WebApplicationFactory<Program>
{
    private readonly bool _ownsDirectory;
    private readonly IReadOnlyDictionary<string, string?> _settings;
    private readonly IReadOnlyList<string> _snapshots;

    public ExplorerApiFactory()
        : this(configureHttp: null, dataDirectory: null, settings: null)
    {
    }

    protected ExplorerApiFactory(
        Action<FixtureHttpHandler>? configureHttp, string? dataDirectory, IReadOnlyDictionary<string, string?>? settings,
        IReadOnlyList<string>? snapshots = null)
    {
        Http = new FixtureHttpHandler();
        configureHttp?.Invoke(Http);
        _ownsDirectory = dataDirectory is null;
        _settings = settings ?? new Dictionary<string, string?>();
        _snapshots = snapshots ?? [FixtureHttpHandler.IndexUrl];
        DataDirectory = dataDirectory ?? Directory.CreateTempSubdirectory("pte-api-").FullName;
    }

    /// <summary>
    /// A factory with a customized fake payer server, a shared data folder (e.g. to simulate a restart), extra
    /// configuration settings, and/or other index files to load (by default only the 2026-08-31 fixture index).
    /// </summary>
    public static ExplorerApiFactory With(
        Action<FixtureHttpHandler>? configureHttp = null, string? dataDirectory = null, IReadOnlyDictionary<string, string?>? settings = null,
        IReadOnlyList<string>? snapshots = null) =>
        new Configured(configureHttp, dataDirectory, settings, snapshots);

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

    /// <summary>Sends a request through MediatR in its own scope, as a controller would (for internal queries with no endpoint).</summary>
    public async Task<TResponse> SendAsync<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken)
    {
        using var scope = Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request, cancellationToken);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ingestion:DataDirectory"] = DataDirectory,
            ["Ingestion:MaxDownloadAttempts"] = "2",
            ["Ingestion:StallTimeoutSeconds"] = "10",
            ["Serilog:MinimumLevel:Default"] = "Warning",
        }).AddInMemoryCollection(_settings));

        builder.ConfigureTestServices(services =>
        {
            // Exactly the test's indexes: configuration can override appsettings.json's list entries but not remove them.
            services.PostConfigure<IngestionOptions>(options =>
                options.Snapshots = _snapshots.Select(url => new SnapshotSource { IndexUrl = url }).ToList());
            services
                .AddHttpClient(FileDownloader.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => Http)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
        });
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

    private sealed class Configured(
        Action<FixtureHttpHandler>? configureHttp, string? dataDirectory, IReadOnlyDictionary<string, string?>? settings,
        IReadOnlyList<string>? snapshots)
        : ExplorerApiFactory(configureHttp, dataDirectory, settings, snapshots);
}
