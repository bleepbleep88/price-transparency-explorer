namespace PriceExplorer.Api.Core.Data;

/// <summary>
/// Every TIMESTAMP column holds UTC (the writer passes UTC values, never DuckDB's local now()). DuckDB hands them back
/// without a kind; this marks them as UTC so JSON always carries the offset.
/// </summary>
internal static class UtcTimestamps
{
    public static DateTimeOffset? From(DateTime? value) =>
        value is { } timestamp ? new DateTimeOffset(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)) : null;
}
