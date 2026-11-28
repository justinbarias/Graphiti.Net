namespace Graphiti.PyCompat;

/// <summary>
/// The numpy kernels that ported code needs with the same bits as numpy (RULES.md R18). A plain loop in index order
/// gives other last bits, and a score that differs in its last bits can change a rank.
/// </summary>
internal static class PyNumpy
{
    /// <summary>
    /// <c>np.add.reduce(a)</c> and <c>np.sum(a)</c> for a contiguous one-dimensional float64 array. numpy sums in
    /// pairs (<c>DOUBLE_pairwise_sum</c>): a plain loop below 8 items, eight running sums up to 128 items, and a
    /// split in two parts above that. The reduction starts from the identity 0.0, so a sum of negative zeros is 0.0.
    /// </summary>
    internal static double AddReduce(IReadOnlyList<double> values) => 0.0 + PairwiseSum(values, 0, values.Count);

    private static double PairwiseSum(IReadOnlyList<double> a, int start, int n)
    {
        if (n < 8)
        {
            var sum = 0.0;
            for (var i = 0; i < n; i++)
            {
                sum += a[start + i];
            }

            return sum;
        }

        if (n <= 128)
        {
            Span<double> r = stackalloc double[8];
            for (var j = 0; j < 8; j++)
            {
                r[j] = a[start + j];
            }

            int k;
            for (k = 8; k < n - (n % 8); k += 8)
            {
                for (var j = 0; j < 8; j++)
                {
                    r[j] += a[start + k + j];
                }
            }

            var res = ((r[0] + r[1]) + (r[2] + r[3])) + ((r[4] + r[5]) + (r[6] + r[7]));
            for (; k < n; k++)
            {
                res += a[start + k];
            }

            return res;
        }

        var half = n / 2;
        half -= half % 8;
        return PairwiseSum(a, start, half) + PairwiseSum(a, start + half, n - half);
    }

    /// <summary>
    /// <c>np.add.reduce(a)</c> for a contiguous one-dimensional float32 array, in float32 (<c>FLOAT_pairwise_sum</c>):
    /// the same pairs as the float64 form, with each partial sum rounded to float32. The reduction starts from the
    /// identity 0.0, so a sum of negative zeros is 0.0.
    /// </summary>
    internal static float AddReduce(IReadOnlyList<float> values) => 0.0f + PairwiseSum(values, 0, values.Count);

    private static float PairwiseSum(IReadOnlyList<float> a, int start, int n)
    {
        if (n < 8)
        {
            var sum = 0.0f;
            for (var i = 0; i < n; i++)
            {
                sum += a[start + i];
            }

            return sum;
        }

        if (n <= 128)
        {
            Span<float> r = stackalloc float[8];
            for (var j = 0; j < 8; j++)
            {
                r[j] = a[start + j];
            }

            int k;
            for (k = 8; k < n - (n % 8); k += 8)
            {
                for (var j = 0; j < 8; j++)
                {
                    r[j] += a[start + k + j];
                }
            }

            var res = ((r[0] + r[1]) + (r[2] + r[3])) + ((r[4] + r[5]) + (r[6] + r[7]));
            for (; k < n; k++)
            {
                res += a[start + k];
            }

            return res;
        }

        var half = n / 2;
        half -= half % 8;
        return PairwiseSum(a, start, half) + PairwiseSum(a, start + half, n - half);
    }

    // numpy's SMALL_QUICKSORT: a part of 16 items or fewer is sorted by insertion.
    private const int SmallQuicksort = 15;

    // PORT: numpy 2.4.1 of the oracle (macOS arm64) runs this generic sort for float32, because numpy has a SIMD
    // argsort only for x86 (x86-simd-sort with AVX-512 or AVX2). On such an x86 machine np.argsort can give equal
    // values in another order. The port follows the generic sort.

    /// <summary>
    /// <c>np.argsort(a)</c> with the default <c>kind='quicksort'</c> for a contiguous one-dimensional float32 array:
    /// the generic <c>aquicksort_&lt;npy::float_tag&gt;</c> of <c>npysort/quicksort.cpp</c>. It is an introsort: a
    /// partition about the median of three, an insertion sort for a part of 16 items or fewer, and a heapsort
    /// (<c>aheapsort_</c>) for a part that the partitions reach below the depth limit <c>2 * msb(n)</c>. It is not
    /// stable: equal values can come out in another order than their indexes. nan sorts after each other value.
    /// </summary>
    internal static int[] ArgSort(ReadOnlySpan<float> values)
    {
        var tosort = new int[values.Length];
        for (var i = 0; i < tosort.Length; i++)
        {
            tosort[i] = i;
        }

        var pl = 0;
        var pr = values.Length - 1;
        var stack = new Stack<(int Left, int Right, int Depth)>();
        var cdepth = Msb(values.Length) * 2;
        while (true)
        {
            if (cdepth < 0)
            {
                HeapSort(values, tosort, pl, pr - pl + 1);
            }
            else
            {
                while (pr - pl > SmallQuicksort)
                {
                    // The quicksort partition.
                    var pm = pl + ((pr - pl) >> 1);
                    if (Less(values[tosort[pm]], values[tosort[pl]]))
                    {
                        (tosort[pm], tosort[pl]) = (tosort[pl], tosort[pm]);
                    }

                    if (Less(values[tosort[pr]], values[tosort[pm]]))
                    {
                        (tosort[pr], tosort[pm]) = (tosort[pm], tosort[pr]);
                    }

                    if (Less(values[tosort[pm]], values[tosort[pl]]))
                    {
                        (tosort[pm], tosort[pl]) = (tosort[pl], tosort[pm]);
                    }

                    var vp = values[tosort[pm]];
                    var pi = pl;
                    var pj = pr - 1;
                    (tosort[pm], tosort[pj]) = (tosort[pj], tosort[pm]);
                    while (true)
                    {
                        do
                        {
                            ++pi;
                        }
                        while (Less(values[tosort[pi]], vp));
                        do
                        {
                            --pj;
                        }
                        while (Less(vp, values[tosort[pj]]));
                        if (pi >= pj)
                        {
                            break;
                        }

                        (tosort[pi], tosort[pj]) = (tosort[pj], tosort[pi]);
                    }

                    var pk = pr - 1;
                    (tosort[pi], tosort[pk]) = (tosort[pk], tosort[pi]);

                    // Push the larger part on the stack.
                    if (pi - pl < pr - pi)
                    {
                        stack.Push((pi + 1, pr, --cdepth));
                        pr = pi - 1;
                    }
                    else
                    {
                        stack.Push((pl, pi - 1, --cdepth));
                        pl = pi + 1;
                    }
                }

                // The insertion sort.
                for (var pi = pl + 1; pi <= pr; ++pi)
                {
                    var vi = tosort[pi];
                    var vp = values[vi];
                    var pj = pi;
                    var pk = pi - 1;
                    while (pj > pl && Less(vp, values[tosort[pk]]))
                    {
                        tosort[pj--] = tosort[pk--];
                    }

                    tosort[pj] = vi;
                }
            }

            if (stack.Count == 0)
            {
                break;
            }

            (pl, pr, cdepth) = stack.Pop();
        }

        return tosort;
    }

    // `Tag::less` of npy::float_tag: `a < b || (b != b && a == a)`, so nan is greater than each other value.
    private static bool Less(float a, float b) => a < b || (float.IsNaN(b) && !float.IsNaN(a));

    // npy_get_msb: the index of the highest set bit, and 0 for 0 and 1.
    private static int Msb(int n)
    {
        var depth = 0;
        while ((n >>= 1) != 0)
        {
            depth++;
        }

        return depth;
    }

    // aheapsort_ on the n indexes of tosort from start. The heap counts from 1, as in numpy (`a = tosort - 1`).
    private static void HeapSort(ReadOnlySpan<float> values, int[] tosort, int start, int n)
    {
        var a = start - 1;
        int i, j, tmp;
        for (var l = n >> 1; l > 0; --l)
        {
            tmp = tosort[a + l];
            for (i = l, j = l << 1; j <= n;)
            {
                if (j < n && Less(values[tosort[a + j]], values[tosort[a + j + 1]]))
                {
                    j += 1;
                }

                if (Less(values[tmp], values[tosort[a + j]]))
                {
                    tosort[a + i] = tosort[a + j];
                    i = j;
                    j += j;
                }
                else
                {
                    break;
                }
            }

            tosort[a + i] = tmp;
        }

        for (; n > 1;)
        {
            tmp = tosort[a + n];
            tosort[a + n] = tosort[a + 1];
            n -= 1;
            for (i = 1, j = 2; j <= n;)
            {
                if (j < n && Less(values[tosort[a + j]], values[tosort[a + j + 1]]))
                {
                    j++;
                }

                if (Less(values[tmp], values[tosort[a + j]]))
                {
                    tosort[a + i] = tosort[a + j];
                    i = j;
                    j += j;
                }
                else
                {
                    break;
                }
            }

            tosort[a + i] = tmp;
        }
    }
}
