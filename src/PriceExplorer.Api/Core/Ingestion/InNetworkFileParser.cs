using System.Text.Json;
using PriceExplorer.Api.Core.Ingestion.Tic;

namespace PriceExplorer.Api.Core.Ingestion;

/// <summary>Streams one in-network file from disk into an <see cref="InNetworkFileBuilder"/>.</summary>
internal static class InNetworkFileParser
{
    /// <summary>Parses the file at <paramref name="path"/>; <paramref name="onBytesRead"/> receives bytes read so far (for progress).</summary>
    public static async Task<ParseOutcome> ParseAsync(
        string path, InNetworkFileBuilder builder, Action<long>? onBytesRead, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1 << 16,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var counted = new ReadProgressStream(file, onBytesRead);
        await using var input = TicJson.OpenForReading(counted);

        var options = TicJson.CreateStreamingInNetworkOptions(builder.AddProviderReference, builder.AddItem);

        try
        {
            var root = await JsonSerializer.DeserializeAsync<InNetworkFileRoot>(input, options, cancellationToken);
            builder.Complete(root);
            return root is null
                ? ParseOutcome.Partial("The file is empty or not a JSON object.")
                : ParseOutcome.Complete();
        }
        catch (JsonException ex)
        {
            // Keep everything read before the bad spot (a truncated download, a malformed element) and report it.
            builder.Complete(null);
            var where = ex.LineNumber is { } line ? $" (line {line + 1})" : "";
            return ParseOutcome.Partial($"Stopped reading after {builder.ItemsParsed:N0} items: {ex.Message}{where}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Anything else (a decoder error, an input shape nobody planned for) also keeps what was read so far.
            builder.Complete(null);
            return ParseOutcome.Partial($"Stopped reading after {builder.ItemsParsed:N0} items: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

internal sealed record ParseOutcome(bool IsComplete, string? Problem)
{
    public static ParseOutcome Complete() => new(true, null);

    public static ParseOutcome Partial(string problem) => new(false, problem);
}

/// <summary>Pass-through read stream that reports its position, so parsing progress can be shown in bytes.</summary>
internal sealed class ReadProgressStream(Stream inner, Action<long>? onBytesRead) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override int Read(byte[] buffer, int offset, int count) => Track(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Track(inner.Read(buffer));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Track(await inner.ReadAsync(buffer, cancellationToken));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Track(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken));

    public override long Seek(long offset, SeekOrigin origin)
    {
        _position = inner.Seek(offset, origin);
        return _position;
    }

    public override void Flush() { }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Track(int read)
    {
        _position += read;
        onBytesRead?.Invoke(_position);
        return read;
    }
}
