using System.Collections;

namespace Graphiti.PyCompat;

internal static partial class Py
{
    /// <summary><c>len(s)</c>: the number of code points.</summary>
    internal static long Len(string s)
    {
        long count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                i++;
            }

            count++;
        }

        return count;
    }

    /// <summary>
    /// <c>s[index]</c> by code point. A negative index counts from the end. An index outside the string
    /// throws <see cref="ArgumentOutOfRangeException"/> (<c>IndexError</c>).
    /// </summary>
    internal static string At(string s, long index)
    {
        var cps = PyText.ToCodePoints(s);
        var i = index < 0 ? index + cps.Length : index;
        if (i < 0 || i >= cps.Length)
        {
            throw new ArgumentOutOfRangeException(null, "string index out of range");
        }

        return PyText.FromCodePoints(cps.AsSpan((int)i, 1));
    }

    /// <summary>
    /// The indexes that <c>range(*slice(start, stop, step).indices(length))</c> gives in Python.
    /// A zero step throws <see cref="ArgumentException"/> (<c>ValueError</c>).
    /// </summary>
    private static IEnumerable<int> SliceIndexes(int length, long? start, long? stop, long? step)
    {
        var st = step ?? 1;
        if (st == 0)
        {
            throw new ArgumentException("slice step cannot be zero");
        }

        long lower = st < 0 ? -1 : 0;
        long upper = st < 0 ? length - 1 : length;

        long Bound(long? value, long defaultValue)
        {
            if (value is null)
            {
                return defaultValue;
            }

            var v = value.Value;
            if (v < 0)
            {
                v += length;
                return v < lower ? lower : v;
            }

            return v > upper ? upper : v;
        }

        var from = Bound(start, st < 0 ? upper : lower);
        var to = Bound(stop, st < 0 ? lower : upper);
        // Python computes the length of the slice first, so a step above the length cannot overflow.
        Int128 span = st > 0 ? (Int128)to - from : (Int128)from - to;
        Int128 absStep = st > 0 ? st : -(Int128)st;
        var count = span <= 0 ? Int128.Zero : ((span - 1) / absStep) + 1;
        for (Int128 k = 0; k < count; k++)
        {
            yield return (int)(from + (k * st));
        }
    }

    /// <summary><c>s[start:stop:step]</c> by code point, with negative indexes. A null bound is an omitted one.</summary>
    internal static string Slice(string s, long? start, long? stop, long? step = null)
    {
        var cps = PyText.ToCodePoints(s);
        var picked = SliceIndexes(cps.Length, start, stop, step).Select(i => cps[i]).ToArray();
        return PyText.FromCodePoints(picked);
    }

    /// <summary><c>xs[start:stop:step]</c> on a list. The result is a new list.</summary>
    internal static List<T> Slice<T>(IReadOnlyList<T> xs, long? start, long? stop, long? step = null) =>
        [.. SliceIndexes(xs.Count, start, stop, step).Select(i => xs[i])];

    /// <summary>
    /// Compares two strings by code point, as Python does. Returns -1, 0, or 1.
    /// <c>string.CompareOrdinal</c> compares by UTF-16 unit and differs above U+FFFF.
    /// </summary>
    internal static int CompareStrings(string a, string b)
    {
        var x = PyText.ToCodePoints(a);
        var y = PyText.ToCodePoints(b);
        var n = Math.Min(x.Length, y.Length);
        for (var i = 0; i < n; i++)
        {
            if (x[i] != y[i])
            {
                return x[i] < y[i] ? -1 : 1;
            }
        }

        return x.Length.CompareTo(y.Length);
    }

    /// <summary>A comparer for <c>sorted(strings)</c>: it orders by code point.</summary>
    internal static readonly IComparer<string> StringComparer = Comparer<string>.Create(CompareStrings);

    /// <summary>
    /// <c>itertools.combinations(xs, k)</c>: the groups of <paramref name="k"/> items in the order of
    /// <paramref name="xs"/>, in lexicographic order of their positions. A negative <paramref name="k"/>
    /// throws <see cref="ArgumentException"/> (<c>ValueError</c>) when the sequence is first read.
    /// </summary>
    internal static IEnumerable<T[]> Combinations<T>(IReadOnlyList<T> xs, int k)
    {
        if (k < 0)
        {
            throw new ArgumentException("r must be non-negative");
        }

        // Python reads the input once, when the call is made. Later changes to the list do not show.
        return CombinationsCore([.. xs], k);
    }

    private static IEnumerable<T[]> CombinationsCore<T>(T[] xs, int k)
    {
        var n = xs.Length;
        if (k > n)
        {
            yield break;
        }

        var indexes = Enumerable.Range(0, k).ToArray();
        yield return indexes.Select(i => xs[i]).ToArray();
        while (true)
        {
            var i = k - 1;
            while (i >= 0 && indexes[i] == i + n - k)
            {
                i--;
            }

            if (i < 0)
            {
                yield break;
            }

            indexes[i]++;
            for (var j = i + 1; j < k; j++)
            {
                indexes[j] = indexes[j - 1] + 1;
            }

            yield return indexes.Select(x => xs[x]).ToArray();
        }
    }

    /// <summary>
    /// <c>itertools.zip_longest(a, b)</c>: pairs until the longer input ends. The missing side of a pair
    /// is <c>default</c>. That is Python's <c>None</c> for a reference type. For a value type, pass
    /// nullable items (<c>int?</c>), or the missing side reads as zero.
    /// </summary>
    internal static IEnumerable<(TA? First, TB? Second)> ZipLongest<TA, TB>(IEnumerable<TA> a, IEnumerable<TB> b)
    {
        using var ea = a.GetEnumerator();
        using var eb = b.GetEnumerator();
        while (true)
        {
            var hasA = ea.MoveNext();
            var hasB = eb.MoveNext();
            if (!hasA && !hasB)
            {
                yield break;
            }

            yield return (hasA ? ea.Current : default, hasB ? eb.Current : default);
        }
    }
}
