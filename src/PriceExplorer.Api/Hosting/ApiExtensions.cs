using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using PriceExplorer.Api.Core.Models.Rates;
using PriceExplorer.Api.Core.Behaviors;
using PriceExplorer.Api.Core.Reference;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Events;

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

        // Problems the framework writes itself (no route matched, a malformed {rateId:long}, 405) get a code too.
        // TryAdd keeps the codes ExplorerExceptionHandler and the binding-error factory already set.
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions.TryAdd("code", context.ProblemDetails.Status switch
            {
                StatusCodes.Status404NotFound => "not_found",
                StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
                StatusCodes.Status500InternalServerError => "server_error",
                var status => $"http_{status}",
            }));
        services.AddSingleton<IProblemDetailsWriter, JsonProblemDetailsWriter>();
        services.AddExceptionHandler<ExplorerExceptionHandler>();

        // Binding errors (e.g. page=abc) get the same shape as validator errors: camelCase keys + code.
        services.Configure<ApiBehaviorOptions>(options => options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .ToDictionary(
                    entry => JsonNamingPolicy.CamelCase.ConvertName(entry.Key),
                    entry => entry.Value!.Errors.Select(error => string.IsNullOrEmpty(error.ErrorMessage) ? "Invalid value." : error.ErrorMessage).ToArray());
            var problem = new ValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Some search inputs need fixing",
                Extensions =
                {
                    ["code"] = "validation_failed",
                    ["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier,
                },
            };
            return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
        });
        services.AddOpenApi(options =>
        {
            // MVC binds query names case-insensitively; document them in camelCase, the names hint links use.
            options.AddOperationTransformer((operation, _, _) =>
            {
                foreach (var parameter in (operation.Parameters ?? []).OfType<OpenApiParameter>().Where(p => p.In == ParameterLocation.Query))
                {
                    parameter.Name = JsonNamingPolicy.CamelCase.ConvertName(parameter.Name!);

                    // Validators enforce these; the document should say so too.
                    if (parameter.Description?.StartsWith("Required.", StringComparison.Ordinal) == true)
                    {
                        parameter.Required = true;
                    }
                }

                return Task.CompletedTask;
            });

            // A hint link's null value removes that filter, so the values are nullable.
            options.AddSchemaTransformer((schema, context, _) =>
            {
                if (context.JsonTypeInfo.Type == typeof(HintLink) && schema.Properties?.TryGetValue("query", out var query) == true
                    && query is OpenApiSchema map)
                {
                    map.AdditionalProperties = new OpenApiSchema { Type = JsonSchemaType.String | JsonSchemaType.Null };
                }

                return Task.CompletedTask;
            });
        });

        // Order matters: bad input is a 400 even while data is still loading.
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssemblyContaining<Program>();
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(RequireDataBehavior<,>));
        });
        services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);
        services.AddSingleton<ReferenceCatalog>();

        services.AddHealthChecks();

        return services;
    }

    public static WebApplication UseExplorerApi(this WebApplication app)
    {
        // Outermost, so each request is logged with the status the client actually got (a handled 404 is a 404, not a
        // 500), using the host's configured logger. Expected 503s (data still loading) aren't errors.
        app.UseSerilogRequestLogging(options =>
        {
            options.Logger = app.Services.GetRequiredService<Serilog.ILogger>();
            options.GetLevel = (context, _, exception) =>
                exception is not null || (context.Response.StatusCode >= 500 && context.Response.StatusCode != StatusCodes.Status503ServiceUnavailable)
                    ? LogEventLevel.Error
                    : LogEventLevel.Information;
        });
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.MapControllers();
        app.MapOpenApi();
        app.MapScalarApiReference();

        // Live: the process is up. Ready: checks tagged "ready" pass (data loaded).
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

        return app;
    }
}
