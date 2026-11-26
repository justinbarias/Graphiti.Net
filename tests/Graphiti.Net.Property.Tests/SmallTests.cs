namespace Graphiti.PyCompat.Tests;

using System.Text.Json;
using CsCheck;
using Graphiti.PyCompat;

/// <summary>
/// Tests for PyEnv, PyHash, PyLruCache, and PyRandom. The recorded values come from
/// <c>python-values.small.json</c>, which <c>port/tools/generate_small_values.py</c> writes.
/// No test here changes a real environment variable: PyEnv reads through its own seam.
/// </summary>
public class SmallTests
{
    private static readonly JsonElement Root = JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "python-values.small.json"))).RootElement.GetProperty("cases");

    private static List<JsonElement> Cases(string name) => [.. Root.GetProperty(name).EnumerateArray()];

    private static string Text(JsonElement e) =>
        PyText.FromCodePoints([.. e.EnumerateArray().Select(x => x.GetInt32())]);

    private static string? OptText(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : Text(e);

    // ---- PyEnv ----

    private static void WithEnv(JsonElement c, Action<string?> body)
    {
        var value = OptText(c.GetProperty("value"));
        using var _ = PyEnv.Use(name => name == "PYENV_TEST" ? value : null);
        body(value);
    }

    [Fact]
    public void Env_Str_matches_Python() => Assert.All(Cases("env"), c => WithEnv(c, _ =>
    {
        Assert.Equal(Text(c.GetProperty("str_default").GetProperty("r")), PyEnv.Str("PYENV_TEST", "default"));
        Assert.Equal(OptText(c.GetProperty("str_none").GetProperty("r")), PyEnv.Str("PYENV_TEST"));
    }));

    [Fact]
    public void Env_Truthy_matches_Python() => Assert.All(Cases("env"), c => WithEnv(c, _ =>
        Assert.Equal(c.GetProperty("truthy").GetProperty("r").GetBoolean(), PyEnv.Truthy("PYENV_TEST"))));

    [Fact]
    public void Env_Int_matches_Python() => Assert.All(Cases("env"), c => WithEnv(c, value =>
    {
        var expected = c.GetProperty("int");
        if (expected.TryGetProperty("big", out _))
        {
            Assert.Throws<OverflowException>(() => PyEnv.Int("PYENV_TEST", 20));
        }
        else if (expected.TryGetProperty("e", out _))
        {
            var ex = Assert.Throws<ArgumentException>(() => PyEnv.Int("PYENV_TEST", 20));
            Assert.Equal(Text(expected.GetProperty("msg")), ex.Message);
        }
        else
        {
            Assert.Equal(expected.GetProperty("r").GetInt64(), PyEnv.Int("PYENV_TEST", 20));
        }
    }));

    [Fact]
    public void Env_Float_matches_Python() => Assert.All(Cases("env"), c => WithEnv(c, value =>
    {
        var expected = c.GetProperty("float");
        if (expected.TryGetProperty("e", out _))
        {
            var ex = Assert.Throws<ArgumentException>(() => PyEnv.Float("PYENV_TEST", 0.15));
            Assert.Equal(Text(expected.GetProperty("msg")), ex.Message);
        }
        else
        {
            PythonValues.AssertSameFloat(expected, PyEnv.Float("PYENV_TEST", 0.15));
        }
    }));

    [Fact]
    public void Env_reader_is_restored_after_Use()
    {
        using (PyEnv.Use(_ => "from the test"))
        {
            Assert.Equal("from the test", PyEnv.Str("ANY_NAME"));
        }

        Assert.Null(PyEnv.Str("GRAPHITI_NET_NAME_THAT_IS_NOT_SET"));
    }

    // ---- PyHash ----

    private static byte[] Input(JsonElement c) =>
        c.TryGetProperty("pattern", out var n)
            ? [.. Enumerable.Range(0, n.GetInt32()).Select(i => unchecked((byte)((i * 31) + 7)))]
            : Convert.FromHexString(c.GetProperty("hex").GetString() ?? string.Empty);

    [Fact]
    public void Blake2b_matches_Python() => Assert.All(Cases("blake2b"), c =>
        Assert.Equal(c.GetProperty("r").GetString(), Convert.ToHexStringLower(PyHash.Blake2b(Input(c), c.GetProperty("size").GetInt32()))));

    [Fact]
    public void Blake2b_rejects_a_bad_digest_size_as_Python_does() => Assert.All(Cases("blake2b_error"), c =>
    {
        var ex = Assert.Throws<ArgumentException>(() => PyHash.Blake2b(Input(c), c.GetProperty("size").GetInt32()));
        Assert.Equal(Text(c.GetProperty("msg")), ex.Message);
    });

    // ---- PyLruCache ----

    [Fact]
    public void LruCache_matches_the_traces_of_Python() => Assert.All(Cases("lru"), trace =>
    {
        int? maxSize = trace.GetProperty("maxsize") is { ValueKind: not JsonValueKind.Null } m ? m.GetInt32() : null;
        var cache = new PyLruCache<int, int>(maxSize);
        var ran = false;
        foreach (var step in trace.GetProperty("steps").EnumerateArray())
        {
            ran = false;
            var key = step.GetProperty("key").GetInt32();
            Func<int, int> compute = k =>
            {
                ran = true;
                return k < 0 ? throw new ArgumentException($"negative key {k}") : (k * k) + 1;
            };
            if (step.TryGetProperty("e", out _))
            {
                Assert.Throws<ArgumentException>(() => cache.GetOrAdd(key, compute));
            }
            else
            {
                Assert.Equal(step.GetProperty("r").GetInt32(), cache.GetOrAdd(key, compute));
            }

            Assert.Equal(step.GetProperty("ran").GetBoolean(), ran);
            Assert.Equal(step.GetProperty("hits").GetInt64(), cache.Hits);
            Assert.Equal(step.GetProperty("misses").GetInt64(), cache.Misses);
            Assert.Equal(step.GetProperty("size").GetInt32(), cache.Count);
        }
    });

    [Fact]
    public void LruCache_matches_Python_when_the_compute_function_calls_the_cache() => Assert.All(Cases("lru_reentrant"), trace =>
    {
        int? maxSize = trace.GetProperty("maxsize") is { ValueKind: not JsonValueKind.Null } m ? m.GetInt32() : null;
        var outer = trace.GetProperty("outer").GetInt32();
        var inner = trace.GetProperty("inner").GetInt32();
        var cache = new PyLruCache<int, int>(maxSize);
        var entered = false;
        int Compute(int k)
        {
            if (k == outer && !entered)
            {
                entered = true;
                return 1000 + cache.GetOrAdd(inner, Compute);
            }

            return (k * k) + 1;
        }

        foreach (var step in trace.GetProperty("steps").EnumerateArray())
        {
            Assert.Equal(step.GetProperty("r").GetInt32(), cache.GetOrAdd(step.GetProperty("key").GetInt32(), Compute));
            Assert.Equal(step.GetProperty("hits").GetInt64(), cache.Hits);
            Assert.Equal(step.GetProperty("misses").GetInt64(), cache.Misses);
            Assert.Equal(step.GetProperty("size").GetInt32(), cache.Count);
        }
    });

    // ---- PyRandom ----

    [Fact]
    public void Random_sequence_is_pinned_for_each_seed() => Assert.All(Cases("random_sequence"), c =>
    {
        var seed = c.GetProperty("seed").GetInt64();
        var ulongs = new PyRandom(seed);
        foreach (var expected in c.GetProperty("ulongs").EnumerateArray())
        {
            Assert.Equal(ulong.Parse(expected.GetString() ?? string.Empty, System.Globalization.CultureInfo.InvariantCulture), ulongs.NextUInt64());
        }

        var floats = new PyRandom(seed);
        foreach (var expected in c.GetProperty("floats").EnumerateArray())
        {
            Assert.Equal(expected.GetInt64(), BitConverter.DoubleToInt64Bits(floats.Random()));
        }

        var ranges = new (double, double)[] { (0.0, 1.0), (5.0, 120.0), (-3.5, 2.25), (7.0, 7.0), (10.0, -10.0) };
        var uniform = c.GetProperty("uniform").EnumerateArray().ToList();
        Assert.Equal(ranges.Length, uniform.Count);
        for (var r = 0; r < ranges.Length; r++)
        {
            var source = new PyRandom(seed);
            foreach (var expected in uniform[r].EnumerateArray())
            {
                Assert.Equal(expected.GetInt64(), BitConverter.DoubleToInt64Bits(source.Uniform(ranges[r].Item1, ranges[r].Item2)));
            }
        }
    });

    [Fact]
    public void Sample_errors_match_Python() => Assert.All(Cases("random_error"), c =>
    {
        var ex = Assert.Throws<ArgumentException>(() => new PyRandom(1).SampleRange(c.GetProperty("n").GetInt64(), c.GetProperty("k").GetInt32()));
        Assert.Equal(Text(c.GetProperty("msg")), ex.Message);
    });

    [Fact]
    public void Sample_of_a_negative_or_empty_range_with_k_0_is_empty() => Assert.All(Cases("random_empty"), c =>
    {
        Assert.Empty(c.GetProperty("r").EnumerateArray());
        Assert.Empty(new PyRandom(1).SampleRange(c.GetProperty("n").GetInt64(), c.GetProperty("k").GetInt32()));
    });

    [Fact]
    public void Sample_gives_k_distinct_items_in_range() =>
        Gen.Select(Gen.Long, Gen.Int[1, 60]).SelectMany(t => Gen.Int[0, t.Item2].Select(k => (t.Item1, t.Item2, k)))
            .Sample(t =>
            {
                var result = new PyRandom(t.Item1).SampleRange(t.Item2, t.k);
                return result.Length == t.k && result.Distinct().Count() == t.k && result.All(x => x >= 0 && x < t.Item2);
            });

    [Fact]
    public void Sample_of_the_whole_range_is_a_permutation()
    {
        var result = new PyRandom(5).SampleRange(40, 40);
        Assert.Equal(Enumerable.Range(0, 40).Select(i => (long)i), result.Order());
        Assert.NotEqual(Enumerable.Range(0, 40).Select(i => (long)i), result);
    }

    [Fact]
    public void Sample_works_for_a_huge_range()
    {
        var result = new PyRandom(3).SampleRange(long.MaxValue, 5);
        Assert.Equal(5, result.Distinct().Count());
        Assert.All(result, x => Assert.InRange(x, 0, long.MaxValue - 1));
    }

    [Fact]
    public void Sample_gives_each_item_about_the_same_frequency()
    {
        const int Runs = 30000;
        var counts = new int[10];
        var rng = new PyRandom(2026);
        for (var i = 0; i < Runs; i++)
        {
            foreach (var x in rng.SampleRange(10, 3))
            {
                counts[x]++;
            }
        }

        // Each item is drawn with chance 0.3. The standard deviation of a count is about 79.
        Assert.All(counts, c => Assert.InRange(c, 9000 - 450, 9000 + 450));
    }

    [Fact]
    public void Sample_gives_each_position_about_the_same_frequency()
    {
        const int Runs = 30000;
        var counts = new int[4, 4];
        var rng = new PyRandom(77);
        for (var i = 0; i < Runs; i++)
        {
            var s = rng.SampleRange(4, 4);
            for (var p = 0; p < 4; p++)
            {
                counts[p, s[p]]++;
            }
        }

        // Chance 0.25. The standard deviation of a count is about 75.
        foreach (var c in counts)
        {
            Assert.InRange(c, 7500 - 400, 7500 + 400);
        }
    }

    [Fact]
    public void Same_seed_gives_the_same_sequence_and_another_seed_does_not()
    {
        long[] Draw(long seed) => new PyRandom(seed).SampleRange(1000, 20);
        Assert.Equal(Draw(8), Draw(8));
        Assert.NotEqual(Draw(8), Draw(9));
        Assert.NotEqual(new PyRandom().NextUInt64(), new PyRandom().NextUInt64());
    }

    [Fact]
    public void Random_and_Uniform_stay_in_range()
    {
        var rng = new PyRandom(21);
        for (var i = 0; i < 2000; i++)
        {
            Assert.InRange(rng.Random(), 0.0, 0.9999999999999999);
            Assert.InRange(rng.Uniform(5.0, 120.0), 5.0, 120.0);
            Assert.InRange(rng.Uniform(-4.0, -2.0), -4.0, -2.0);
            Assert.Equal(3.25, rng.Uniform(3.25, 3.25));
        }
    }
}
