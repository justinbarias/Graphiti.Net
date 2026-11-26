namespace Graphiti.PyCompat;

using System.Collections;

/// <summary>
/// A Python <c>dict</c> whose keys are not all strings (RULES.md R6). It keeps insertion order, and it
/// compares keys as Python does: the identity test first, then <c>==</c>. So <c>1</c>, <c>1.0</c>, and
/// <c>True</c> are one key, a <c>nan</c> key is found only with the same object, and a tuple is a key by
/// its items. A key that Python cannot hash (a list, a dict, a set) throws <see cref="InvalidCastException"/>.
/// When a key is set again, the dict keeps the first key object and takes the new value, as Python does.
/// A nan key held in a <c>double</c> variable is boxed again at each call, so after <c>d[x] = 1</c> the
/// call <c>ContainsKey(x)</c> is false. .NET cannot keep the identity of an unboxed double. A nan that
/// stays in one <c>object</c> variable keeps its identity, as a Python float object does.
/// </summary>
internal sealed class PyDict : IEnumerable<KeyValuePair<object?, object?>>
{
    private readonly OrderedDictionary<Slot, object?> entries = new(SlotComparer.Instance);

    /// <summary>Gets <c>len(d)</c>.</summary>
    internal int Count => entries.Count;

    /// <summary>Gets the keys in order: <c>list(d)</c>.</summary>
    internal IEnumerable<object?> Keys => entries.Keys.Select(s => s.Value);

    /// <summary>Gets the values in order: <c>list(d.values())</c>.</summary>
    internal IEnumerable<object?> Values => entries.Values;

    /// <summary>
    /// <c>d[key]</c> and <c>d[key] = value</c>. A missing key throws <see cref="KeyNotFoundException"/>
    /// (<c>KeyError</c>) with the message <c>repr(key)</c>, which is <c>str()</c> of Python's <c>KeyError</c>.
    /// </summary>
    internal object? this[object? key]
    {
        get => entries.TryGetValue(new Slot(key), out var value) ? value : throw new KeyNotFoundException(Py.Repr(key));
        set => entries[new Slot(key)] = value;
    }

    /// <summary><c>key in d</c>.</summary>
    internal bool ContainsKey(object? key) => entries.ContainsKey(new Slot(key));

    /// <summary>A lookup that does not throw for a missing key.</summary>
    internal bool TryGetValue(object? key, out object? value) => entries.TryGetValue(new Slot(key), out value);

    /// <summary><c>del d[key]</c> without the <c>KeyError</c>: true if the key was there.</summary>
    internal bool Remove(object? key) => entries.Remove(new Slot(key));

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<object?, object?>> GetEnumerator() =>
        entries.Select(e => new KeyValuePair<object?, object?>(e.Key.Value, e.Value)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary><c>repr(d)</c>.</summary>
    public override string ToString() => Py.Repr(this);

    /// <summary>A key, boxed so that <c>None</c> can be a key.</summary>
    private readonly record struct Slot(object? Value);

    private sealed class SlotComparer : IEqualityComparer<Slot>
    {
        internal static readonly SlotComparer Instance = new();

        public bool Equals(Slot x, Slot y) => PyValueEquality.Equal(x.Value, y.Value);

        public int GetHashCode(Slot obj) => PyValueEquality.Hash(obj.Value);
    }
}
