using System.Net;
using Dapper;
using Microsoft.Extensions.Options;
using PriceExplorer.Api.Configuration;
using PriceExplorer.Api.Core.Data;
using PriceExplorer.Api.Core.Ingestion;

namespace PriceExplorer.Api.Hosting;

internal static class IngestionExtensions
{
    public static IServiceCollection AddExplorerIngestion(this IServiceCollection services)
    {
        services.AddOptions<IngestionOptions>()
            .BindConfiguration(IngestionOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<DataPaths>();
        services.AddSingleton<DuckDbConnectionFactory>();
        services.AddSingleton<IngestionStatus>();
        services.AddSingleton<DuckDbIngestionWriter>();
        services.AddSingleton<FileDownloader>();
        services.AddSingleton<TableOfContentsReader>();
        services.AddSingleton<IngestionCoordinator>();
        services.AddHostedService<IngestionService>();

        // Large bodies are streamed with our own stall watchdog, so the client-wide timeout is off.
        services.AddHttpClient(FileDownloader.HttpClientName, (provider, client) =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(provider.GetRequiredService<IOptions<IngestionOptions>>().Value.UserAgent);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            });

        services.AddHealthChecks().AddCheck<DataReadyHealthCheck>("data", tags: ["ready"]);

        DefaultTypeMap.MatchNamesWithUnderscores = true;

        return services;
    }
}
