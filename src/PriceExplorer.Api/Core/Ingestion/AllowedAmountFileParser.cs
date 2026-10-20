using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using PriceExplorer.Api.Core.Ingestion.Tic;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>
/// Streams one out-of-network allowed-amount file from disk into an <see cref="AllowedAmountFileBuilder"/>, the same way
/// <see cref="InNetworkFileParser"/> reads rate files: element by element, keeping what was read before any damage.
/// </summary>
internal static class AllowedAmountFileParser
{
    public static async Task<ParseOutcome> ParseAsync(
        string path, AllowedAmountFileBuilder builder, Action<long>? onBytesRead, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var counted = new ReadProgressStream(file, onBytesRead);
        await using var input = TicJson.OpenForReading(counted);

        try
        {
            var root = await JsonSerializer.DeserializeAsync<AllowedAmountFileRoot>(input, StreamingOptions(builder.AddItem), cancellationToken);
            builder.Complete(root);
            return root is null
                ? ParseOutcome.Partial("The file is empty or not a JSON object.")
                : ParseOutcome.Complete();
        }
        catch (JsonException ex)
        {
            builder.Complete(null);
            var where = ex.LineNumber is { } line ? $" (line {line + 1})" : "";
            return ParseOutcome.Partial($"Stopped reading after {builder.ItemsParsed:N0} items: {ex.Message}{where}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            builder.Complete(null);
            return ParseOutcome.Partial($"Stopped reading after {builder.ItemsParsed:N0} items: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>TiC options whose out_of_network array streams element by element into <paramref name="onItem"/>.</summary>
    private static JsonSerializerOptions StreamingOptions(Action<OutOfNetworkItem?> onItem)
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type == typeof(OutOfNetworkItemSink))
            {
                info.CreateObject = () => new OutOfNetworkItemSink(onItem);
            }
        });
        return TicJson.CreateOptions(resolver);
    }
}
