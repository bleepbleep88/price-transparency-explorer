using System.Globalization;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PriceExplorer.Api.Core.Exceptions;

namespace PriceExplorer.Api.Hosting;

/// <summary>Turns known exceptions into RFC 9457 problem responses. Anything else falls through to a generic 500.</summary>
internal sealed class ExplorerExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<ExplorerExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => ToValidationProblem(validation),
            ExplorerException known => ToProblem(known),
            _ => null,
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
            return false;
        }

        if (exception is IndexNotReadyException notReady)
        {
            httpContext.Response.Headers.RetryAfter = notReady.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ValidationProblemDetails ToValidationProblem(ValidationException exception)
    {
        // camelCase keys so the Vue form can put each message under the matching field (billingCode, npi, ein...).
        var errors = exception.Errors
            .GroupBy(failure => JsonNamingPolicy.CamelCase.ConvertName(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray());

        return new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Some search inputs need fixing",
            Extensions = { ["code"] = "validation_failed" },
        };
    }

    private static ProblemDetails ToProblem(ExplorerException exception) => new()
    {
        Status = exception.StatusCode,
        Title = exception.Title,
        Detail = exception.Message,
        Extensions = { ["code"] = exception.Code },
    };
}
