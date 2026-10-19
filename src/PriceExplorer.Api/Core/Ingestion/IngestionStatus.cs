using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace PriceExplorer.Api.Core.Ingestion;

public enum IngestionPhase
{
    Starting,
    Downloading,
    Parsing,
    Saving,
    Ready,
    Failed,
}

public sealed record IngestionIssue(string Severity, string Message, string? Source);

/// <summary>What the loader is doing right now, in plain language, plus whether any data can be queried yet.</summary>
public sealed record IngestionStatusSnapshot(
    IngestionPhase Phase,
    string Message,
    bool DataAvailable,
    string? CurrentFile,
    double? CurrentFileProgress,
    int FilesDone,
    int FilesTotal,
    IReadOnlyList<IngestionIssue> Issues,
    DateTimeOffset UpdatedAt,
    long Version);

/// <summary>
/// Holds the current loading status and pushes every change to subscribers (the SSE endpoint). Each subscriber gets a
/// one-slot channel that always holds the newest status, so a slow client skips stale updates instead of piling them up.
/// </summary>
public sealed class IngestionStatus
{
    private readonly Lock _gate = new();
    private readonly ConcurrentDictionary<long, ChannelWriter<IngestionStatusSnapshot>> _subscribers = new();
    private readonly TaskCompletionSource _dataAvailable = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _time;
    private IngestionStatusSnapshot _current;
    private long _nextSubscriberId;

    public IngestionStatus(TimeProvider time)
    {
        _time = time;
        _current = new IngestionStatusSnapshot(
            IngestionPhase.Starting, "Starting up…", DataAvailable: false, CurrentFile: null, CurrentFileProgress: null,
            FilesDone: 0, FilesTotal: 0, Issues: [], UpdatedAt: time.GetUtcNow(), Version: 0);
    }

    public IngestionStatusSnapshot Current => Volatile.Read(ref _current);

    /// <summary>Completes the first time any snapshot becomes queryable. Tests await this instead of sleeping.</summary>
    public Task WhenDataAvailable => _dataAvailable.Task;

    public void Update(Func<IngestionStatusSnapshot, IngestionStatusSnapshot> change)
    {
        lock (_gate)
        {
            var next = change(_current) with { UpdatedAt = _time.GetUtcNow(), Version = _current.Version + 1 };
            Volatile.Write(ref _current, next);
            foreach (var subscriber in _subscribers.Values)
            {
                subscriber.TryWrite(next);
            }

            if (next.DataAvailable)
            {
                _dataAvailable.TrySetResult();
            }
        }
    }

    public void AddIssue(string severity, string message, string? source) =>
        Update(status => status with { Issues = [.. status.Issues, new IngestionIssue(severity, message, source)] });

    /// <summary>Yields the current status, then every change, until the caller cancels (client disconnects).</summary>
    public async IAsyncEnumerable<IngestionStatusSnapshot> WatchAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<IngestionStatusSnapshot>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
        });
        var id = Interlocked.Increment(ref _nextSubscriberId);

        lock (_gate)
        {
            _subscribers[id] = channel.Writer;
            channel.Writer.TryWrite(_current);
        }

        try
        {
            while (await WaitToReadAsync(channel.Reader, cancellationToken))
            {
                while (channel.Reader.TryRead(out var status))
                {
                    yield return status;
                }
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }

    /// <summary>False once the watcher goes away (client disconnected), so the stream ends quietly instead of erroring.</summary>
    private static async ValueTask<bool> WaitToReadAsync(ChannelReader<IngestionStatusSnapshot> reader, CancellationToken cancellationToken)
    {
        try
        {
            return await reader.WaitToReadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
