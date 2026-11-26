namespace Graphiti.PyCompat;

/// <summary>
/// <c>functools.lru_cache(maxsize=n)</c>, and <c>maxsize=None</c> when <c>maxSize</c> is null.
/// A hit moves the entry to the newest place. A full cache drops the least recently used entry.
/// A call that throws is not cached. A size of 0 or less caches nothing, as in Python.
/// The cache has no lock: it is used on the exclusive scheduler of R8.
/// </summary>
internal sealed class PyLruCache<TKey, TValue>(int? maxSize)
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _map = [];

    // The first node is the least recently used, and the last node is the newest.
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new();

    /// <summary><c>cache_info().maxsize</c>.</summary>
    internal int? MaxSize { get; } = maxSize is < 0 ? 0 : maxSize;

    /// <summary><c>cache_info().hits</c>.</summary>
    internal long Hits { get; private set; }

    /// <summary><c>cache_info().misses</c>. A call that throws counts as a miss.</summary>
    internal long Misses { get; private set; }

    /// <summary><c>cache_info().currsize</c>.</summary>
    internal int Count => _map.Count;

    /// <summary>
    /// A call of the cached function: the stored value for the key, or the result of
    /// <paramref name="compute"/>, which is stored. An exception from it passes through and stores nothing.
    /// </summary>
    internal TValue GetOrAdd(TKey key, Func<TKey, TValue> compute)
    {
        if (MaxSize == 0)
        {
            Misses++;
            return compute(key);
        }

        if (_map.TryGetValue(key, out var node))
        {
            Hits++;
            _order.Remove(node);
            _order.AddLast(node);
            return node.Value.Value;
        }

        Misses++;
        var value = compute(key);
        if (_map.TryGetValue(key, out var filled))
        {
            // The call itself filled this key. An unbounded cache overwrites the entry with the
            // outer value. A bounded cache keeps the first entry and returns the outer value.
            if (MaxSize is null)
            {
                filled.Value = new KeyValuePair<TKey, TValue>(key, value);
            }

            return value;
        }

        if (MaxSize is { } max && _map.Count >= max && _order.First is { } oldest)
        {
            _order.RemoveFirst();
            _map.Remove(oldest.Value.Key);
        }

        _map[key] = _order.AddLast(new KeyValuePair<TKey, TValue>(key, value));
        return value;
    }
}
