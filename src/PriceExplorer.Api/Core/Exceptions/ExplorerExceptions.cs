namespace PriceExplorer.Api.Core.Exceptions;

/// <summary>Base for errors the API reports on purpose. The exception handler maps each one to a problem response.</summary>
public abstract class ExplorerException(string message, int statusCode, string title, string code) : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Title { get; } = title;

    /// <summary>Stable machine-readable code the UI can branch on.</summary>
    public string Code { get; } = code;
}

public sealed class BadInputException(string message, string code = "bad_input")
    : ExplorerException(message, StatusCodes.Status400BadRequest, "Invalid request", code);

public sealed class NotFoundException(string message, string code = "not_found")
    : ExplorerException(message, StatusCodes.Status404NotFound, "Not found", code);

/// <summary>A 503 the client can try again later; the exception handler sends Retry-After.</summary>
public interface IRetryLater
{
    int RetryAfterSeconds { get; }
}

/// <summary>Data hasn't finished loading yet. Returned as 503 with a Retry-After header.</summary>
public sealed class IndexNotReadyException(string message, int retryAfterSeconds = 5)
    : ExplorerException(message, StatusCodes.Status503ServiceUnavailable, "Data is still loading", "index_loading"), IRetryLater
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}

/// <summary>
/// The saved statistics a response reads aren't built yet (they're built once a snapshot's files are loaded). Returned
/// as 503 with a Retry-After header, so nobody reads "nothing changed" or "everything is new" from tables still empty.
/// </summary>
public sealed class StatisticsNotReadyException(string message, int retryAfterSeconds = 5)
    : ExplorerException(message, StatusCodes.Status503ServiceUnavailable, "Statistics are being built", "statistics_building"), IRetryLater
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
