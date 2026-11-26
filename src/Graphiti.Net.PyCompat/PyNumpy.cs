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
}
