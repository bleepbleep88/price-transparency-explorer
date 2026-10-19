using System.Collections;

namespace PriceExplorer.Api.Core.Ingestion.Tic;

/// <summary>
/// A write-only collection. System.Text.Json calls <see cref="Add"/> once per array element while it streams,
/// so the element can be processed and dropped instead of kept in memory.
/// </summary>
public abstract class StreamingSink<T>(Action<T> onItem) : ICollection<T>
{
    public int Count { get; private set; }

    public bool IsReadOnly => false;

    public void Add(T item)
    {
        Count++;
        onItem(item);
    }

    public void Clear() { }

    public bool Contains(T item) => false;

    public void CopyTo(T[] array, int arrayIndex) { }

    public bool Remove(T item) => false;

    public IEnumerator<T> GetEnumerator() => Enumerable.Empty<T>().GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class ProviderReferenceSink(Action<ProviderReference?> onItem) : StreamingSink<ProviderReference?>(onItem);

public sealed class InNetworkItemSink(Action<InNetworkItem?> onItem) : StreamingSink<InNetworkItem?>(onItem);
