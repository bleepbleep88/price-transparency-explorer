using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using PriceExplorer.Api.Core.Behaviors;
using Scalar.AspNetCore;
using Serilog;

namespace PriceExplorer.Api.Hosting;

internal static class ApiExtensions
{
    public static IServiceCollection AddExplorerApi(this IServiceCollection services)
    {
        services.AddControllers()
            .AddJsonOptions(options =>
                options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        // Results written outside MVC (server-sent events) use the HTTP JSON options; keep enums as strings there too.
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));

        services.AddProblemDetails();
        services.AddExceptionHandler<ExplorerExceptionHandler>();
        services.AddOpenApi();

        // Order matters: bad input is a 400 even while data is still loading.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Program>();
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(RequireDataBehavior<,>));
        });
        services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);

        services.AddHealthChecks();

        return services;
    }

    public static WebApplication UseExplorerApi(this WebApplication app)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseSerilogRequestLogging();

        app.MapControllers();
        app.MapOpenApi();
        app.MapScalarApiReference();

        // Live: the process is up. Ready: checks tagged "ready" pass (data loaded).
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

        return app;
    }
}
