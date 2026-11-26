namespace Graphiti.PyCompat;

using System.Collections;
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
    /// <c>xs.sort(key=key, reverse=reverse)</c> on the list object itself, as CPython 3.12 does it
    /// (<c>list_sort_impl</c> in <c>Objects/listobject.c</c>). It makes the same comparisons in the same
    /// order, so the result is Python's also for keys that do not compare consistently, such as <c>nan</c>,
    /// and the pair in a <c>TypeError</c> is Python's. Keys compare with <see cref="LessThan"/>.
    /// <list type="bullet">
    /// <item>The list is empty while the sort runs. The key function runs one time for each item, in list order, before any comparison.</item>
    /// <item>An exception from the key function leaves the list as it was and arrives as itself.</item>
    /// <item>An exception from a comparison arrives as itself, and the list holds the items in the partly sorted order of that moment.</item>
    /// <item>If the list is not empty at the end, its items are dropped, the sorted items are written back, and the sort throws
    /// <see cref="ArgumentException"/> (<c>ValueError: list modified during sort</c>). Python also detects a change that leaves
    /// the list empty again. This helper cannot.</item>
    /// </list>
    /// </summary>
    internal static void SortInPlace<T>(List<T> xs, Func<T, object?>? key = null, bool reverse = false)
    {
        var values = xs.ToArray();
        var n = values.Length;
        xs.Clear();
        var keys = new object?[n];
        try
        {
            for (var i = 0; i < n; i++)
            {
                keys[i] = key is null ? values[i] : key(values[i]);
            }
        }
        catch (Exception)
        {
            // keyfunc_fail: the items go back in their first order, with no reverse.
            xs.Clear();
            xs.AddRange(values);
            throw;
        }

        try
        {
            if (n > 1)
            {
                if (reverse)
                {
                    // Stability of a reverse sort: reverse, sort forward, reverse again.
                    Array.Reverse(keys);
                    Array.Reverse(values);
                }

                new ListSort<T>(keys, values).Sort();
            }
        }
        catch (Exception)
        {
            WriteBack(xs, values, reverse);
            throw;
        }

        var modified = xs.Count != 0;
        WriteBack(xs, values, reverse);
        if (modified)
        {
            throw new ArgumentException("list modified during sort");
        }
    }

    /// <summary>
    /// Python <c>a &lt; b</c> over the value model: numbers by exact value across <c>bool</c>, <c>long</c>,
    /// <c>BigInteger</c>, and <c>double</c> (false for each comparison with nan), text by code point, tuples
    /// item by item, lists (<c>List&lt;object?&gt;</c> or a typed <see cref="IList"/>) item by item, bytes by
    /// byte, dates, datetimes by the operators of <see cref="PyDateTime"/>, and sets by proper subset.
    /// A datetime against a date throws <see cref="InvalidCastException"/> with
    /// <c>can't compare datetime.datetime to datetime.date</c>. Each other pair of the value model throws
    /// <see cref="InvalidCastException"/> (<c>TypeError</c>) with Python's message. A value of a type outside
    /// the value model, for example a <c>ValueTuple</c>, an <c>int</c>, or a <c>decimal</c>, throws
    /// <see cref="InvalidOperationException"/>.
    /// </summary>
    internal static bool LessThan(object? a, object? b)
    {
        RequireOrderable(a);
        RequireOrderable(b);
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
            case byte[] ba when b is byte[] bb:
                return ba.AsSpan().SequenceCompareTo(bb) < 0;
            case PyDateTime ta2 when b is PyDateTime tb2:
                // A naive value against an aware value throws InvalidCastException (TypeError) as itself.
                return ta2 < tb2;
            case PyDateTime when b is DateOnly:
            case DateOnly when b is PyDateTime:
                // datetime is a subclass of date, and CPython names the datetime first in both orders.
                throw new InvalidCastException("can't compare datetime.datetime to datetime.date");
            case DateOnly da when b is DateOnly db:
                return da < db;
            case IPySet sa2 when b is IPySet sb2:
                return sa2.Count < sb2.Count && sa2.Items.All(sb2.ContainsItem);
            case IList la when IsList(a) && b is IList lb && IsList(b):
                return SequenceLess(la.Cast<object?>().ToList(), lb.Cast<object?>().ToList());
        }

        throw new InvalidCastException($"'<' not supported between instances of '{TypeName(a)}' and '{TypeName(b)}'");
    }

    private static bool IsList(object? x) => x is IList and not (byte[] or IDictionary);

    private static void RequireOrderable(object? x)
    {
        if (x is not (null or bool or long or BigInteger or double or string or byte[] or PyTuple or IPySet or PyDict
            or IDictionary or IList or DateOnly or PyDateTime or Enum))
        {
            throw new InvalidOperationException(
                $"Py.LessThan: the type {x.GetType().FullName} is not in the Python value model (RULES.md R6). Use a PyTuple, a long, or a double.");
        }
    }

    private static void WriteBack<T>(List<T> xs, T[] values, bool reverse)
    {
        if (reverse && values.Length > 1)
        {
            Array.Reverse(values);
        }

        xs.Clear();
        xs.AddRange(values);
    }

    /// <summary>
    /// The merge state of CPython's list sort (<c>MergeState</c>): natural runs found by <c>count_run</c>,
    /// short runs extended to <c>minrun</c> by binary insertion, the powersort merge policy of CPython 3.11
    /// and later, and <c>merge_lo</c> and <c>merge_hi</c> with galloping and an adaptive <c>min_gallop</c>.
    /// Keys and values move together. After an exception from a comparison, each item is in the array again.
    /// </summary>
    private sealed class ListSort<T>(object?[] keys, T[] values)
    {
        private const int MinGallopStart = 7;
        private const int MaxMergePending = 85;

        private readonly Run[] pending = new Run[MaxMergePending];
        private int count;
        private int minGallop = MinGallopStart;
        private object?[] tempKeys = [];
        private T[] tempValues = [];

        internal void Sort()
        {
            var n = keys.Length;
            var minRun = ComputeMinRun(n);
            var lo = 0;
            var remaining = n;
            do
            {
                var run = CountRun(lo, lo + remaining, out var descending);
                if (descending)
                {
                    Array.Reverse(keys, lo, run);
                    Array.Reverse(values, lo, run);
                }

                if (run < minRun)
                {
                    var force = remaining <= minRun ? remaining : minRun;
                    BinarySort(lo, lo + force, lo + run);
                    run = force;
                }

                FoundNewRun(run);
                pending[count] = new Run(lo, run, 0);
                count++;
                lo += run;
                remaining -= run;
            }
            while (remaining > 0);

            // merge_force_collapse
            while (count > 1)
            {
                var i = count - 2;
                if (i > 0 && pending[i - 1].Length < pending[i + 1].Length)
                {
                    i--;
                }

                MergeAt(i);
            }
        }

        /// <summary><c>merge_compute_minrun</c>: n itself below 64, else a value from 32 to 64.</summary>
        internal static int ComputeMinRun(int n)
        {
            var r = 0;
            while (n >= 64)
            {
                r |= n & 1;
                n >>= 1;
            }

            return n + r;
        }

        /// <summary><c>powerloop</c>: the depth of the boundary between two runs in the merge tree.</summary>
        private static int PowerLoop(long s1, long n1, long n2, long n)
        {
            var result = 0;
            var a = (2 * s1) + n1;
            var b = a + n1 + n2;
            while (true)
            {
                result++;
                if (a >= n)
                {
                    a -= n;
                    b -= n;
                }
                else if (b >= n)
                {
                    break;
                }

                a <<= 1;
                b <<= 1;
            }

            return result;
        }

        private static bool Less(object? a, object? b) => LessThan(a, b);

        /// <summary><c>count_run</c>: a run that is strictly descending, or one that never descends.</summary>
        private int CountRun(int lo, int hi, out bool descending)
        {
            descending = false;
            var k = lo + 1;
            if (k == hi)
            {
                return 1;
            }

            var n = 2;
            if (Less(keys[k], keys[k - 1]))
            {
                descending = true;
                for (k++; k < hi; k++, n++)
                {
                    if (!Less(keys[k], keys[k - 1]))
                    {
                        break;
                    }
                }
            }
            else
            {
                for (k++; k < hi; k++, n++)
                {
                    if (Less(keys[k], keys[k - 1]))
                    {
                        break;
                    }
                }
            }

            return n;
        }

        /// <summary><c>binarysort</c>: [lo, start) is sorted. Insert each item of [start, hi) after its equals.</summary>
        private void BinarySort(int lo, int hi, int start)
        {
            if (lo == start)
            {
                start++;
            }

            for (; start < hi; start++)
            {
                var l = lo;
                var r = start;
                var pivot = keys[r];
                do
                {
                    var p = l + ((r - l) >> 1);
                    if (Less(pivot, keys[p]))
                    {
                        r = p;
                    }
                    else
                    {
                        l = p + 1;
                    }
                }
                while (l < r);

                var pivotValue = values[start];
                Array.Copy(keys, l, keys, l + 1, start - l);
                Array.Copy(values, l, values, l + 1, start - l);
                keys[l] = pivot;
                values[l] = pivotValue;
            }
        }

        /// <summary><c>found_new_run</c>: merge the runs on the stack whose power is above the new boundary's.</summary>
        private void FoundNewRun(int n2)
        {
            if (count == 0)
            {
                return;
            }

            var top = pending[count - 1];
            var power = PowerLoop(top.Start, top.Length, n2, keys.Length);
            while (count > 1 && pending[count - 2].Power > power)
            {
                MergeAt(count - 2);
            }

            pending[count - 1] = pending[count - 1] with { Power = power };
        }

        /// <summary><c>merge_at</c>: merge the runs i and i + 1 of the stack.</summary>
        private void MergeAt(int i)
        {
            var pa = pending[i].Start;
            var na = pending[i].Length;
            var pb = pending[i + 1].Start;
            var nb = pending[i + 1].Length;
            pending[i] = pending[i] with { Length = na + nb };
            if (i == count - 3)
            {
                pending[i + 1] = pending[i + 2];
            }

            count--;

            // Where does b start in a? Items of a before that are in place.
            var k = GallopRight(keys[pb], keys, pa, na, 0);
            pa += k;
            na -= k;
            if (na == 0)
            {
                return;
            }

            // Where does a end in b? Items of b after that are in place.
            nb = GallopLeft(keys[pa + na - 1], keys, pb, nb, nb - 1);
            if (nb <= 0)
            {
                return;
            }

            if (na <= nb)
            {
                MergeLo(pa, na, pb, nb);
            }
            else
            {
                MergeHi(pa, na, pb, nb);
            }
        }

        /// <summary><c>gallop_left</c>: the first position in a[0, n) where key belongs, before its equals.</summary>
        private static int GallopLeft(object? key, object?[] a, int start, int n, int hint)
        {
            var lastOfs = 0;
            var ofs = 1;
            if (Less(a[start + hint], key))
            {
                // a[hint] < key: gallop right until a[hint + lastOfs] < key <= a[hint + ofs].
                var maxOfs = n - hint;
                while (ofs < maxOfs)
                {
                    if (!Less(a[start + hint + ofs], key))
                    {
                        break;
                    }

                    lastOfs = ofs;
                    ofs = (ofs << 1) + 1;
                }

                if (ofs > maxOfs)
                {
                    ofs = maxOfs;
                }

                lastOfs += hint;
                ofs += hint;
            }
            else
            {
                // key <= a[hint]: gallop left until a[hint - ofs] < key <= a[hint - lastOfs].
                var maxOfs = hint + 1;
                while (ofs < maxOfs)
                {
                    if (Less(a[start + hint - ofs], key))
                    {
                        break;
                    }

                    lastOfs = ofs;
                    ofs = (ofs << 1) + 1;
                }

                if (ofs > maxOfs)
                {
                    ofs = maxOfs;
                }

                var k = lastOfs;
                lastOfs = hint - ofs;
                ofs = hint - k;
            }

            lastOfs++;
            while (lastOfs < ofs)
            {
                var m = lastOfs + ((ofs - lastOfs) >> 1);
                if (Less(a[start + m], key))
                {
                    lastOfs = m + 1;
                }
                else
                {
                    ofs = m;
                }
            }

            return ofs;
        }

        /// <summary><c>gallop_right</c>: the last position in a[0, n) where key belongs, after its equals.</summary>
        private static int GallopRight(object? key, object?[] a, int start, int n, int hint)
        {
            var lastOfs = 0;
            var ofs = 1;
            if (Less(key, a[start + hint]))
            {
                // key < a[hint]: gallop left until a[hint - ofs] <= key < a[hint - lastOfs].
                var maxOfs = hint + 1;
                while (ofs < maxOfs)
                {
                    if (!Less(key, a[start + hint - ofs]))
                    {
                        break;
                    }

                    lastOfs = ofs;
                    ofs = (ofs << 1) + 1;
                }

                if (ofs > maxOfs)
                {
                    ofs = maxOfs;
                }

                var k = lastOfs;
                lastOfs = hint - ofs;
                ofs = hint - k;
            }
            else
            {
                // a[hint] <= key: gallop right until a[hint + lastOfs] <= key < a[hint + ofs].
                var maxOfs = n - hint;
                while (ofs < maxOfs)
                {
                    if (Less(key, a[start + hint + ofs]))
                    {
                        break;
                    }

                    lastOfs = ofs;
                    ofs = (ofs << 1) + 1;
                }

                if (ofs > maxOfs)
                {
                    ofs = maxOfs;
                }

                lastOfs += hint;
                ofs += hint;
            }

            lastOfs++;
            while (lastOfs < ofs)
            {
                var m = lastOfs + ((ofs - lastOfs) >> 1);
                if (Less(key, a[start + m]))
                {
                    ofs = m;
                }
                else
                {
                    lastOfs = m + 1;
                }
            }

            return ofs;
        }

        private void EnsureTemp(int need)
        {
            if (tempKeys.Length < need)
            {
                tempKeys = new object?[need];
                tempValues = new T[need];
            }
        }

        /// <summary><c>merge_lo</c>: run a at pa is copied out, and the merge fills from the left.</summary>
        private void MergeLo(int pa, int na, int pb, int nb)
        {
            EnsureTemp(na);
            Array.Copy(keys, pa, tempKeys, 0, na);
            Array.Copy(values, pa, tempValues, 0, na);
            var dest = pa;
            var a = 0;
            var b = pb;
            try
            {
                keys[dest] = keys[b];
                values[dest++] = values[b++];
                nb--;
                if (nb == 0)
                {
                    goto Succeed;
                }

                if (na == 1)
                {
                    goto CopyB;
                }

                var mg = minGallop;
                while (true)
                {
                    var aCount = 0;
                    var bCount = 0;

                    // One pair at a time, until one run wins min_gallop times in a row.
                    while (true)
                    {
                        if (Less(keys[b], tempKeys[a]))
                        {
                            keys[dest] = keys[b];
                            values[dest++] = values[b++];
                            bCount++;
                            aCount = 0;
                            nb--;
                            if (nb == 0)
                            {
                                goto Succeed;
                            }

                            if (bCount >= mg)
                            {
                                break;
                            }
                        }
                        else
                        {
                            keys[dest] = tempKeys[a];
                            values[dest++] = tempValues[a++];
                            aCount++;
                            bCount = 0;
                            na--;
                            if (na == 1)
                            {
                                goto CopyB;
                            }

                            if (aCount >= mg)
                            {
                                break;
                            }
                        }
                    }

                    // Gallop until neither run wins MIN_GALLOP times.
                    mg++;
                    do
                    {
                        mg -= mg > 1 ? 1 : 0;
                        minGallop = mg;
                        var k = GallopRight(keys[b], tempKeys, a, na, 0);
                        aCount = k;
                        if (k != 0)
                        {
                            Array.Copy(tempKeys, a, keys, dest, k);
                            Array.Copy(tempValues, a, values, dest, k);
                            dest += k;
                            a += k;
                            na -= k;
                            if (na == 1)
                            {
                                goto CopyB;
                            }

                            // Not possible for a consistent order, but nan keys are not consistent.
                            if (na == 0)
                            {
                                goto Succeed;
                            }
                        }

                        keys[dest] = keys[b];
                        values[dest++] = values[b++];
                        nb--;
                        if (nb == 0)
                        {
                            goto Succeed;
                        }

                        k = GallopLeft(tempKeys[a], keys, b, nb, 0);
                        bCount = k;
                        if (k != 0)
                        {
                            Array.Copy(keys, b, keys, dest, k);
                            Array.Copy(values, b, values, dest, k);
                            dest += k;
                            b += k;
                            nb -= k;
                            if (nb == 0)
                            {
                                goto Succeed;
                            }
                        }

                        keys[dest] = tempKeys[a];
                        values[dest++] = tempValues[a++];
                        na--;
                        if (na == 1)
                        {
                            goto CopyB;
                        }
                    }
                    while (aCount >= MinGallopStart || bCount >= MinGallopStart);

                    mg++;
                    minGallop = mg;
                }
            }
            catch (Exception)
            {
                // Fail: the rest of run a goes back into the array.
                Array.Copy(tempKeys, a, keys, dest, na);
                Array.Copy(tempValues, a, values, dest, na);
                throw;
            }

        Succeed:
            Array.Copy(tempKeys, a, keys, dest, na);
            Array.Copy(tempValues, a, values, dest, na);
            return;

        CopyB:
            // The last item of run a belongs at the end of the merge.
            Array.Copy(keys, b, keys, dest, nb);
            Array.Copy(values, b, values, dest, nb);
            keys[dest + nb] = tempKeys[a];
            values[dest + nb] = tempValues[a];
        }

        /// <summary><c>merge_hi</c>: run b at pb is copied out, and the merge fills from the right.</summary>
        private void MergeHi(int pa, int na, int pb, int nb)
        {
            EnsureTemp(nb);
            Array.Copy(keys, pb, tempKeys, 0, nb);
            Array.Copy(values, pb, tempValues, 0, nb);
            var dest = pb + nb - 1;
            var a = pa + na - 1;
            var b = nb - 1;
            try
            {
                keys[dest] = keys[a];
                values[dest--] = values[a--];
                na--;
                if (na == 0)
                {
                    goto Succeed;
                }

                if (nb == 1)
                {
                    goto CopyA;
                }

                var mg = minGallop;
                while (true)
                {
                    var aCount = 0;
                    var bCount = 0;
                    while (true)
                    {
                        if (Less(tempKeys[b], keys[a]))
                        {
                            keys[dest] = keys[a];
                            values[dest--] = values[a--];
                            aCount++;
                            bCount = 0;
                            na--;
                            if (na == 0)
                            {
                                goto Succeed;
                            }

                            if (aCount >= mg)
                            {
                                break;
                            }
                        }
                        else
                        {
                            keys[dest] = tempKeys[b];
                            values[dest--] = tempValues[b--];
                            bCount++;
                            aCount = 0;
                            nb--;
                            if (nb == 1)
                            {
                                goto CopyA;
                            }

                            if (bCount >= mg)
                            {
                                break;
                            }
                        }
                    }

                    mg++;
                    do
                    {
                        mg -= mg > 1 ? 1 : 0;
                        minGallop = mg;
                        var k = na - GallopRight(tempKeys[b], keys, pa, na, na - 1);
                        aCount = k;
                        if (k != 0)
                        {
                            dest -= k;
                            a -= k;
                            Array.Copy(keys, a + 1, keys, dest + 1, k);
                            Array.Copy(values, a + 1, values, dest + 1, k);
                            na -= k;
                            if (na == 0)
                            {
                                goto Succeed;
                            }
                        }

                        keys[dest] = tempKeys[b];
                        values[dest--] = tempValues[b--];
                        nb--;
                        if (nb == 1)
                        {
                            goto CopyA;
                        }

                        k = nb - GallopLeft(keys[a], tempKeys, 0, nb, nb - 1);
                        bCount = k;
                        if (k != 0)
                        {
                            dest -= k;
                            b -= k;
                            Array.Copy(tempKeys, b + 1, keys, dest + 1, k);
                            Array.Copy(tempValues, b + 1, values, dest + 1, k);
                            nb -= k;
                            if (nb == 1)
                            {
                                goto CopyA;
                            }

                            // Not possible for a consistent order, but nan keys are not consistent.
                            if (nb == 0)
                            {
                                goto Succeed;
                            }
                        }

                        keys[dest] = keys[a];
                        values[dest--] = values[a--];
                        na--;
                        if (na == 0)
                        {
                            goto Succeed;
                        }
                    }
                    while (aCount >= MinGallopStart || bCount >= MinGallopStart);

                    mg++;
                    minGallop = mg;
                }
            }
            catch (Exception)
            {
                // Fail: the rest of run b goes back into the array.
                Array.Copy(tempKeys, 0, keys, dest - (nb - 1), nb);
                Array.Copy(tempValues, 0, values, dest - (nb - 1), nb);
                throw;
            }

        Succeed:
            Array.Copy(tempKeys, 0, keys, dest - (nb - 1), nb);
            Array.Copy(tempValues, 0, values, dest - (nb - 1), nb);
            return;

        CopyA:
            // The first item of run b belongs at the front of the merge.
            Array.Copy(keys, a - na + 1, keys, dest - na + 1, na);
            Array.Copy(values, a - na + 1, values, dest - na + 1, na);
            dest -= na;
            keys[dest] = tempKeys[b];
            values[dest] = tempValues[b];
        }

        /// <summary>A run on the stack: where it starts, how long it is, and its power.</summary>
        private readonly record struct Run(int Start, int Length, int Power);
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
