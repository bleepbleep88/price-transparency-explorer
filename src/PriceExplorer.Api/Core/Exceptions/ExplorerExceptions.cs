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

/// <summary>Data hasn't finished loading yet. Returned as 503 with a Retry-After header.</summary>
public sealed class IndexNotReadyException(string message, int retryAfterSeconds = 5)
    : ExplorerException(message, StatusCodes.Status503ServiceUnavailable, "Data is still loading", "index_loading")
{
    public int RetryAfterSeconds { get; } = retryAfterSeconds;
}
