namespace Graphiti.PyCompat;

using System.Numerics;

/// <summary>
/// The list operations that test items with <c>==</c> (RULES.md R18), and <c>list.sort</c> (R14).
/// A Python list compares each item with the identity test first and <c>==</c> of the list item second,
/// which <see cref="PyEqualityComparer{T}"/> does. Never use <c>List.Contains</c> for nodes or edges.
/// </summary>
internal static partial class Py
{
    /// <summary><c>x in xs</c>.</summary>
    internal static bool Contains<T>(IEnumerable<T> xs, T x)
    {
        foreach (var item in xs)
        {
            if (PyEqualityComparer<T>.Instance.Equals(item, x))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// <c>xs.index(x)</c>. A missing item throws <see cref="ArgumentException"/> (<c>ValueError</c>) with
    /// Python's message <c>repr(x) is not in list</c>. For a type outside the value model, which has no
    /// <c>repr</c> here, the message names the type in place of the repr.
    /// </summary>
    internal static long IndexOf<T>(IReadOnlyList<T> xs, T x)
    {
        for (var i = 0; i < xs.Count; i++)
        {
            if (PyEqualityComparer<T>.Instance.Equals(xs[i], x))
            {
                return i;
            }
        }

        string shown;
        try
        {
            shown = Repr(x);
        }
        catch (InvalidOperationException)
        {
            shown = $"<{TypeName(x)} object>";
        }

        throw new ArgumentException($"{shown} is not in list");
    }

    /// <summary><c>xs.remove(x)</c>: removes the first equal item. A missing item throws <see cref="ArgumentException"/> (<c>ValueError</c>).</summary>
    internal static void Remove<T>(List<T> xs, T x)
    {
        for (var i = 0; i < xs.Count; i++)
        {
            if (PyEqualityComparer<T>.Instance.Equals(xs[i], x))
            {
                xs.RemoveAt(i);
                return;
            }
        }

        throw new ArgumentException("list.remove(x): x not in list");
    }

    /// <summary><c>xs.count(x)</c>.</summary>
    internal static long Count<T>(IEnumerable<T> xs, T x)
    {
        long n = 0;
        foreach (var item in xs)
        {
            if (PyEqualityComparer<T>.Instance.Equals(item, x))
            {
                n++;
            }
        }

        return n;
    }

    /// <summary>
    /// <c>xs.sort(key=key, reverse=reverse)</c> on the list object itself. The sort is stable, and with
    /// <paramref name="reverse"/> equal items keep their first order, as Python does. Keys compare as Python
    /// <c>&lt;</c> compares them (<see cref="LessThan"/>). A key pair that Python cannot compare throws
    /// <see cref="InvalidCastException"/> (<c>TypeError</c>) as itself. The comparisons are the ones of
    /// CPython 3.12 for a list of fewer than 64 items (a run, then a binary insertion sort), so the pair in
    /// an error message is Python's. A longer list uses the same insertion sort. Its result is Python's for
    /// keys with a consistent order, but the pair in an error can differ, and so can the order of keys that
    /// hold a nan.
    /// </summary>
    internal static void SortInPlace<T>(List<T> xs, Func<T, object?>? key = null, bool reverse = false)
    {
        var n = xs.Count;
        if (n < 2)
        {
            return;
        }

        var keys = new object?[n];
        var values = xs.ToArray();
        for (var i = 0; i < n; i++)
        {
            keys[i] = key is null ? values[i] : key(values[i]);
        }

        if (reverse)
        {
            Array.Reverse(keys);
            Array.Reverse(values);
        }

        // count_run: the first run, strictly descending or not descending.
        var run = 2;
        if (LessThan(keys[1], keys[0]))
        {
            while (run < n && LessThan(keys[run], keys[run - 1]))
            {
                run++;
            }

            Array.Reverse(keys, 0, run);
            Array.Reverse(values, 0, run);
        }
        else
        {
            while (run < n && !LessThan(keys[run], keys[run - 1]))
            {
                run++;
            }
        }

        // binarysort: insert each later item after the last item that is not greater.
        for (var start = run; start < n; start++)
        {
            var pivot = keys[start];
            var pivotValue = values[start];
            int l = 0, r = start;
            do
            {
                var p = l + ((r - l) >> 1);
                if (LessThan(pivot, keys[p]))
                {
                    r = p;
                }
                else
                {
                    l = p + 1;
                }
            }
            while (l < r);

            Array.Copy(keys, l, keys, l + 1, start - l);
            Array.Copy(values, l, values, l + 1, start - l);
            keys[l] = pivot;
            values[l] = pivotValue;
        }

        if (reverse)
        {
            Array.Reverse(values);
        }

        for (var i = 0; i < n; i++)
        {
            xs[i] = values[i];
        }
    }

    /// <summary>
    /// Python <c>a &lt; b</c> over the value model: numbers by exact value across <c>bool</c>, <c>long</c>,
    /// <c>BigInteger</c>, and <c>double</c> (false for each comparison with nan), text by code point, tuples and
    /// lists item by item, bytes by byte, dates, datetimes by the operators of <see cref="PyDateTime"/>, sets by proper subset, and two values of one other type that
    /// is <see cref="IComparable"/> and not an enum. Each other pair throws <see cref="InvalidCastException"/>
    /// (<c>TypeError</c>) with Python's message.
    /// </summary>
    internal static bool LessThan(object? a, object? b)
    {
        if (a is bool or long or BigInteger or double && b is bool or long or BigInteger or double)
        {
            return NumberLess(a, b);
        }

        switch (a)
        {
            case string sa when b is string sb:
                return CompareStrings(sa, sb) < 0;
            case PyTuple ta when b is PyTuple tb:
                return SequenceLess(ta, tb);
            case List<object?> la when b is List<object?> lb:
                return SequenceLess(la, lb);
            case byte[] ba when b is byte[] bb:
                return ba.AsSpan().SequenceCompareTo(bb) < 0;
            case DateOnly da when b is DateOnly db:
                return da < db;
            case PyDateTime ta2 when b is PyDateTime tb2:
                // A naive value against an aware value throws InvalidCastException (TypeError) as itself.
                return ta2 < tb2;
            case IPySet sa2 when b is IPySet sb2:
                return sa2.Count < sb2.Count && sa2.Items.All(sb2.ContainsItem);
            case IComparable ca when a is not Enum && b is not null && a.GetType() == b.GetType():
                return ca.CompareTo(b) < 0;
        }

        throw new InvalidCastException($"'<' not supported between instances of '{TypeName(a)}' and '{TypeName(b)}'");
    }

    private static bool SequenceLess(IReadOnlyList<object?> a, IReadOnlyList<object?> b)
    {
        var i = 0;
        while (i < a.Count && i < b.Count && PyValueEquality.Equal(a[i], b[i]))
        {
            i++;
        }

        return i < a.Count && i < b.Count ? LessThan(a[i], b[i]) : a.Count < b.Count;
    }

    private static bool NumberLess(object a, object b)
    {
        if (a is double da && b is double db)
        {
            return da < db;
        }

        if (a is double d1)
        {
            // float < int: compare the int with the exact float.
            return !double.IsNaN(d1) && (double.IsInfinity(d1) ? d1 < 0 : IntLess(Floor(d1), d1, ToInt(b), floatFirst: true));
        }

        if (b is double d2)
        {
            return !double.IsNaN(d2) && (double.IsInfinity(d2) ? d2 > 0 : IntLess(Floor(d2), d2, ToInt(a), floatFirst: false));
        }

        return ToInt(a) < ToInt(b);
    }

    /// <summary>
    /// The float <paramref name="d"/>, whose floor is <paramref name="floor"/>, against the int <paramref name="n"/>.
    /// <c>d &lt; n</c> holds when the floor is below <c>n</c>. <c>n &lt; d</c> holds when <c>n</c> is below the
    /// floor, or equal to it with a fraction left.
    /// </summary>
    private static bool IntLess(BigInteger floor, double d, BigInteger n, bool floatFirst) =>
        floatFirst ? floor < n : n < floor || (n == floor && Math.Floor(d) != d);

    private static BigInteger Floor(double d) => new(Math.Floor(d));

    private static BigInteger ToInt(object x) => x switch
    {
        bool b => b ? BigInteger.One : BigInteger.Zero,
        long l => l,
        _ => (BigInteger)x,
    };
}
