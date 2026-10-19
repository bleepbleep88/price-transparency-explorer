using Serilog;

namespace PriceExplorer.Api.Hosting;

internal static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddExplorerObservability(this WebApplicationBuilder builder)
    {
        builder.Services.AddSerilog(
            (services, logger) => logger
                .ReadFrom.Configuration(builder.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .WriteTo.Console(),
            preserveStaticLogger: true);

        return builder;
    }
}
