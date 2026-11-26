namespace Graphiti.PyCompat;

using System.Collections;
using System.Numerics;

/// <summary>
/// The key comparison of a Python <c>dict</c> or <c>set</c>, and of <c>x in list</c>: the identity test
/// first, then <c>==</c>. For a value of the value model (RULES.md R6) <c>==</c> is Python's: <c>1</c>,
/// <c>1.0</c>, and <c>True</c> are equal, <c>nan</c> equals only the same object, text compares by code
/// point, and a tuple compares item by item. For each other type <c>==</c> is <see cref="object.Equals(object)"/>,
/// called on the first argument, which is the stored key, as CPython calls <c>__eq__</c> of the stored key.
/// </summary>
/// <typeparam name="T">The item type.</typeparam>
internal sealed class PyEqualityComparer<T> : IEqualityComparer<T>
{
    /// <summary>The one instance.</summary>
    internal static readonly PyEqualityComparer<T> Instance = new();

    private PyEqualityComparer()
    {
    }

    /// <summary>Python <c>x is y or x == y</c>.</summary>
    public bool Equals(T? x, T? y) => PyValueEquality.Equal(x, y);

    /// <summary>Python <c>hash(x)</c> in the sense that equal values give equal codes. A list, a dict, or a set throws <see cref="InvalidCastException"/> (<c>TypeError: unhashable type</c>).</summary>
    public int GetHashCode(T obj) => PyValueEquality.Hash(obj);
}

/// <summary>Python <c>==</c> and <c>hash()</c> over the value model.</summary>
internal static class PyValueEquality
{
    /// <summary><c>PyObject_RichCompareBool(a, b, Py_EQ)</c>: the identity test, then <c>a == b</c>.</summary>
    internal static bool Equal(object? a, object? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (IsNumber(a))
        {
            return IsNumber(b) && NumberEqual(a, b);
        }

        switch (a)
        {
            case string sa:
                return b is string sb && string.Equals(sa, sb, StringComparison.Ordinal);
            case PyTuple ta:
                return b is PyTuple tb && SequenceEqual(ta, tb);
            case byte[] ba:
                return b is byte[] bb && ba.AsSpan().SequenceEqual(bb);
            case IPySet sa2:
                return b is IPySet sb2 && sa2.Count == sb2.Count && sa2.Items.All(sb2.ContainsItem);
            case PyDict or IDictionary:
                return IsDict(b) && DictEqual(a, b);
            case IList la:
                return b is IList lb && b is not byte[] && SequenceEqual(la.Cast<object?>().ToList(), lb.Cast<object?>().ToList());
            default:
                return !IsNumber(b) && a.Equals(b);
        }
    }

    /// <summary>A hash code that is equal for values that <see cref="Equal"/> calls equal.</summary>
    internal static int Hash(object? x)
    {
        switch (x)
        {
            case null:
                return 0;
            case bool or long or BigInteger or double:
                return NumberHash(x);
            case string s:
                return System.StringComparer.Ordinal.GetHashCode(s);
            case PyTuple t:
                var code = new HashCode();
                foreach (var item in t)
                {
                    code.Add(Hash(item));
                }

                return code.ToHashCode();
            case byte[] bytes:
                var bytesCode = new HashCode();
                bytesCode.AddBytes(bytes);
                return bytesCode.ToHashCode();
            case IPySet { Frozen: true } frozen:
                var sum = 0;
                foreach (var item in frozen.Items)
                {
                    sum = unchecked(sum + Hash(item));
                }

                return sum;
            case IPySet:
            case PyDict or IDictionary:
            case IList:
                throw new InvalidCastException($"unhashable type: '{Py.TypeName(x)}'");
            default:
                return x.GetHashCode();
        }
    }

    private static bool IsNumber(object x) => x is bool or long or BigInteger or double;

    private static bool IsDict(object x) => x is PyDict or IDictionary;

    private static bool SequenceEqual(IReadOnlyList<object?> a, IReadOnlyList<object?> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!Equal(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DictEqual(object a, object b)
    {
        var entries = Py.DictEntries(a).ToList();
        if (entries.Count != Py.Len(b))
        {
            return false;
        }

        foreach (var (key, value) in entries)
        {
            if (!TryLookup(b, key, out var other) || !Equal(value, other))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>A lookup by Python key equality in either kind of dict.</summary>
    internal static bool TryLookup(object dict, object? key, out object? value)
    {
        if (dict is PyDict pd)
        {
            return pd.TryGetValue(key, out value);
        }

        foreach (var (k, v) in Py.DictEntries(dict))
        {
            if (Equal(k, key))
            {
                value = v;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static BigInteger ToBigInteger(object x) => x switch
    {
        bool b => b ? BigInteger.One : BigInteger.Zero,
        long l => l,
        _ => (BigInteger)x,
    };

    private static bool NumberEqual(object a, object b)
    {
        if (a is double da && b is double db)
        {
            return da == db;
        }

        if (a is double || b is double)
        {
            var d = a is double x ? x : (double)b;
            var i = ToBigInteger(a is double ? b : a);
            return double.IsFinite(d) && Math.Floor(d) == d && new BigInteger(d) == i;
        }

        return ToBigInteger(a) == ToBigInteger(b);
    }

    private static int NumberHash(object x)
    {
        BigInteger value;
        if (x is double d)
        {
            if (double.IsNaN(d))
            {
                return 0;
            }

            if (!double.IsFinite(d) || Math.Floor(d) != d)
            {
                return d.GetHashCode();
            }

            value = new BigInteger(d);
        }
        else
        {
            value = ToBigInteger(x);
        }

        return value >= long.MinValue && value <= long.MaxValue ? ((long)value).GetHashCode() : value.GetHashCode();
    }
}
