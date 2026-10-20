using System.Diagnostics;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace PriceExplorer.Api.Hosting;

/// <summary>
/// Writes problem responses as JSON even when the request's Accept header rules JSON out, where the framework's writers
/// step aside (and a 404 would become plain text without a code). Results ignore Accept too, so every client gets one
/// error shape. Registered after the framework's writers, so it only writes what they skip.
/// </summary>
internal sealed class JsonProblemDetailsWriter(IOptions<ProblemDetailsOptions> options) : IProblemDetailsWriter
{
    public bool CanWrite(ProblemDetailsContext context) => true;

    public ValueTask WriteAsync(ProblemDetailsContext context)
    {
        var http = context.HttpContext;
        var problem = context.ProblemDetails;
        problem.Status ??= http.Response.StatusCode;
        problem.Title ??= ReasonPhrases.GetReasonPhrase(problem.Status.Value);
        problem.Extensions.TryAdd("traceId", Activity.Current?.Id ?? http.TraceIdentifier);
        options.Value.CustomizeProblemDetails?.Invoke(context);
        return new ValueTask(http.Response.WriteAsJsonAsync(problem, problem.GetType(), options: null, contentType: "application/problem+json"));
    }
}
