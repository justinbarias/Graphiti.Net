namespace Graphiti.PyCompat.Tests;

using CsCheck;
using Graphiti.PyCompat;
using static Graphiti.PyCompat.Tests.PythonValues;

public class SequenceAndUuidTests
{
    [Fact]
    public void Combinations_match_Python() => Check("combinations",
        c => Py.Combinations(Ints(c.GetProperty("xs")), c.GetProperty("k").GetInt32()).ToArray(),
        (c, v) => Assert.Equal(c.GetProperty("r").EnumerateArray().Select(Ints).ToArray(), v));

    [Fact]
    public void ZipLongest_matches_Python() => Check("zip_longest",
        c => Py.ZipLongest(Ints(c.GetProperty("a")).Select(x => (int?)x), Ints(c.GetProperty("b")).Select(x => (int?)x)).ToArray(),
        (c, v) => Assert.Equal(
            c.GetProperty("r").EnumerateArray()
                .Select(p => (First: Nullable(p[0]), Second: Nullable(p[1]))).ToArray(),
            v));

    private static int? Nullable(System.Text.Json.JsonElement e) =>
        e.ValueKind == System.Text.Json.JsonValueKind.Null ? null : e.GetInt32();

    [Fact]
    public void Uuid5_of_the_url_namespace_matches_Python() => Check("uuid5_url",
        c => Py.Uuid5(Py.NamespaceUrl, Text(c, "name")),
        (c, v) => Assert.Equal(c.GetProperty("r").GetString(), v));

    [Fact]
    public void Combinations_reads_the_list_when_it_is_called()
    {
        var xs = new List<int> { 1, 2 };
        var combinations = Py.Combinations(xs, 2);
        xs[0] = 9;
        Assert.Equal([[1, 2]], combinations.ToArray());
    }

    [Fact]
    public void Combinations_count_is_n_choose_k() =>
        Gen.Select(Gen.Int[0, 8], Gen.Int[0, 9]).Sample((n, k) =>
        {
            var expected = k > n ? 0L : Choose(n, k);
            return Py.Combinations(Enumerable.Range(0, n).ToArray(), k).LongCount() == expected;
        });

    private static long Choose(int n, int k)
    {
        long result = 1;
        for (var i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i;
        }

        return result;
    }

    [Fact]
    public void ZipLongest_is_as_long_as_the_longer_input() =>
        Gen.Select(Gen.Int.List[0, 6], Gen.Int.List[0, 6]).Sample((a, b) =>
            Py.ZipLongest(a, b).Count() == Math.Max(a.Count, b.Count));

    [Fact]
    public void Uuid4_has_version_4_and_the_form_that_str_gives()
    {
        Gen.Const(0).Sample(_ =>
        {
            var s = Py.Uuid4();
            var hex = Py.Uuid4Hex();
            return s.Length == 36 && s[14] == '4' && s[19] is '8' or '9' or 'a' or 'b'
                && !s.Any(char.IsUpper) && hex.Length == 32 && hex[12] == '4' && !hex.Any(char.IsUpper);
        }, iter: 200);
    }
}
