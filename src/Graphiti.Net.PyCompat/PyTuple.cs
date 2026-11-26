namespace Graphiti.PyCompat;

using System.Collections;

/// <summary>
/// A Python <c>tuple</c> in the value model (RULES.md R6): a sequence of values that cannot change.
/// <see cref="Equals(object)"/> and <see cref="GetHashCode"/> follow Python <c>==</c> and <c>hash()</c>,
/// so a tuple can be a key of a <see cref="PyDict"/> or an item of a <see cref="PySet{T}"/>.
/// </summary>
internal sealed class PyTuple : IReadOnlyList<object?>
{
    private readonly object?[] items;

    /// <summary>The tuple <c>(items[0], items[1], ...)</c>. The array is copied.</summary>
    internal PyTuple(params object?[] items)
    {
        this.items = [.. items];
    }

    /// <summary>
    /// <c>tuple(items)</c>. A static method and not a constructor, so that <c>new PyTuple(t)</c> is always
    /// the one-item tuple <c>(t,)</c>, also when <c>t</c> is a sequence.
    /// </summary>
    internal static PyTuple From(IEnumerable<object?> items) => new([.. items]);

    /// <summary>Gets <c>len(t)</c>.</summary>
    public int Count => items.Length;

    /// <summary>Gets <c>t[index]</c> for an index from 0.</summary>
    public object? this[int index] => items[index];

    /// <summary>Python <c>==</c> on two tuples.</summary>
    public override bool Equals(object? obj) => PyValueEquality.Equal(this, obj);

    /// <summary>Python <c>hash(t)</c>. A list, a dict, or a set inside throws <see cref="InvalidCastException"/>.</summary>
    public override int GetHashCode() => PyValueEquality.Hash(this);

    /// <summary><c>repr(t)</c>.</summary>
    public override string ToString() => Py.Repr(this);

    /// <inheritdoc/>
    public IEnumerator<object?> GetEnumerator() => ((IEnumerable<object?>)items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
}
