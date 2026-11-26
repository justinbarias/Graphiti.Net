namespace Graphiti.PyCompat;

using System.Collections;

/// <summary>
/// A Python <c>set</c> or <c>frozenset</c> that code iterates with no sort (RULES.md R14). It iterates in
/// insertion order. Python gives no fixed order, and insertion order is one of the orders that Python
/// can give. Items compare as keys of a Python set: by default with <see cref="PyEqualityComparer{T}"/>,
/// which tests identity first. Adding an item that already equals an item keeps the first item.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal sealed class PySet<T> : IReadOnlyCollection<T>, IPySet
{
    private readonly OrderedDictionary<Slot, T> items;
    private readonly IEqualityComparer<T> comparer;

    /// <summary><c>set()</c>, or <c>set(items)</c> with the items in their order.</summary>
    internal PySet(IEnumerable<T>? items = null, IEqualityComparer<T>? comparer = null)
    {
        this.comparer = comparer ?? PyEqualityComparer<T>.Instance;
        this.items = new OrderedDictionary<Slot, T>(new SlotComparer(this.comparer));
        foreach (var item in items ?? [])
        {
            AddCore(item);
        }
    }

    /// <summary>
    /// Gets a value indicating whether this is a <c>frozenset</c>. A frozenset can be hashed, its
    /// <c>repr</c> is <c>frozenset({...})</c>, and <see cref="Add"/>, <see cref="Remove"/>, and
    /// <see cref="Discard"/> throw <see cref="MissingMemberException"/> (<c>AttributeError</c>).
    /// </summary>
    internal bool Frozen { get; init; }

    /// <summary>Gets <c>len(s)</c>.</summary>
    public int Count => items.Count;

    bool IPySet.Frozen => Frozen;

    IEnumerable<object?> IPySet.Items => items.Values.Cast<object?>();

    /// <summary><c>s.add(item)</c>. Returns true if the set did not hold an equal item.</summary>
    internal bool Add(T item)
    {
        ThrowIfFrozen("add");
        return AddCore(item);
    }

    /// <summary><c>item in s</c>.</summary>
    internal bool Contains(T item) => items.ContainsKey(new Slot(item));

    /// <summary><c>s.remove(item)</c>. A missing item throws <see cref="KeyNotFoundException"/> (<c>KeyError</c>) with the message <c>repr(item)</c>.</summary>
    internal void Remove(T item)
    {
        ThrowIfFrozen("remove");
        if (!items.Remove(new Slot(item)))
        {
            throw new KeyNotFoundException(Py.Repr(item));
        }
    }

    /// <summary><c>s.discard(item)</c>.</summary>
    internal void Discard(T item)
    {
        ThrowIfFrozen("discard");
        items.Remove(new Slot(item));
    }

    bool IPySet.ContainsItem(object? item) => item is T t ? Contains(t) : item is null && default(T) is null && items.ContainsKey(new Slot(default));

    IPySet IPySet.DeepCopy(Func<object?, object?> copy)
    {
        var result = new PySet<T>(comparer: comparer) { Frozen = Frozen };
        foreach (var item in items.Values)
        {
            // The copy of an item of type T is of type T. Only a null item gives a null copy.
            result.AddCore(copy(item) is T t ? t : item);
        }

        return result;
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => items.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary><c>repr(s)</c>.</summary>
    public override string ToString() => Py.Repr(this);

    private bool AddCore(T item) => items.TryAdd(new Slot(item), item);

    private void ThrowIfFrozen(string method)
    {
        if (Frozen)
        {
            throw new MissingMemberException($"'frozenset' object has no attribute '{method}'");
        }
    }

    private readonly record struct Slot(T? Value);

    private sealed class SlotComparer(IEqualityComparer<T> comparer) : IEqualityComparer<Slot>
    {
        public bool Equals(Slot x, Slot y) => comparer.Equals(x.Value, y.Value);

        public int GetHashCode(Slot obj) => obj.Value is null ? 0 : comparer.GetHashCode(obj.Value);
    }
}

/// <summary>The view of a <see cref="PySet{T}"/> that <c>repr</c>, <c>==</c>, <c>len</c>, and <c>deepcopy</c> use for any item type.</summary>
internal interface IPySet
{
    /// <summary>Gets <c>len(s)</c>.</summary>
    int Count { get; }

    /// <summary>Gets a value indicating whether this is a <c>frozenset</c>.</summary>
    bool Frozen { get; }

    /// <summary>Gets the items in iteration order.</summary>
    IEnumerable<object?> Items { get; }

    /// <summary><c>item in s</c> for an item of any type.</summary>
    bool ContainsItem(object? item);

    /// <summary>A new set of the same kind with each item passed through <paramref name="copy"/>.</summary>
    IPySet DeepCopy(Func<object?, object?> copy);
}
