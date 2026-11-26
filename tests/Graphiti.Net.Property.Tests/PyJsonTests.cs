namespace Graphiti.PyCompat.Tests;

using System.Numerics;
using System.Text.Json;
using CsCheck;
using Graphiti.PyCompat;
using static Graphiti.PyCompat.Tests.PyJsonValues;

public class PyJsonTests
{
    private static readonly bool[] Booleans = [true, false];
    private static readonly int?[] Indents = [null, 0, 2];

    [Fact]
    public void Dumps_matches_Python()
    {
        var cases = Cases("dumps");
        Assert.True(cases.Count > 8000);
        foreach (var c in cases)
        {
            RunDumps(c, c.TryGetProperty("vi", out var vi) ? PoolValue(vi.GetInt32()) : c.GetProperty("v"));
        }
    }

    [Fact]
    public void Dumps_of_deep_values_matches_Python()
    {
        foreach (var c in Cases("dumps_deep"))
        {
            RunDumps(c, c.GetProperty("v"));
        }
    }

    [Fact]
    public void Loads_matches_Python()
    {
        var cases = Cases("loads");
        Assert.True(cases.Count > 8000);
        foreach (var c in cases)
        {
            RunLoads(c);
        }
    }

    [Fact]
    public void Loads_of_long_and_deep_texts_matches_Python()
    {
        foreach (var c in Cases("loads_big"))
        {
            RunLoads(c);
        }
    }

    [Fact]
    public void Loads_reads_back_what_Dumps_writes_with_any_options()
    {
        Gen.Int.Sample(
            seed =>
            {
                var x = RandomJson(new Random(seed), 0);
                foreach (var ensureAscii in Booleans)
                {
                    foreach (var indent in Indents)
                    {
                        foreach (var sortKeys in Booleans)
                        {
                            var text = PyJson.Dumps(x, ensureAscii, indent, sortKeys, compact: indent is null && ensureAscii);
                            Assert.True(StrictEqual(SortedCopy(x, sortKeys), PyJson.Loads(text)), text);
                        }
                    }
                }
            },
            iter: 1500);
    }

    [Fact]
    public void Dumps_with_sortKeys_orders_keys_by_code_point()
    {
        Gen.Int.Sample(
            seed =>
            {
                var rng = new Random(seed);
                var dict = new OrderedDictionary<string, object?>();
                for (var i = 0; i < rng.Next(0, 30); i++)
                {
                    dict[RandomText(rng)] = (long)i;
                }

                var keys = ((OrderedDictionary<string, object?>)(PyJson.Loads(PyJson.Dumps(dict, sortKeys: true)) ?? new object())).Keys.ToList();
                for (var i = 1; i < keys.Count; i++)
                {
                    Assert.True(Py.CompareStrings(keys[i - 1], keys[i]) < 0);
                }
            },
            iter: 500);
    }

    [Fact]
    public void Typed_data_is_written_like_open_data()
    {
        var typed = new Dictionary<string, List<string>> { ["b"] = ["x", "\u00e9"], ["a"] = [] };
        var open = new OrderedDictionary<string, object?>
        {
            ["b"] = new List<object?> { "x", "\u00e9" },
            ["a"] = new List<object?>(),
        };
        foreach (var ensureAscii in Booleans)
        {
            Assert.Equal(PyJson.Dumps(open, ensureAscii), PyJson.Dumps(typed, ensureAscii));
            Assert.Equal(PyJson.Dumps(open, ensureAscii, 2, true), PyJson.Dumps(typed, ensureAscii, 2, true));
        }

        Assert.Equal("{\"b\": [\"x\", \"\\u00e9\"], \"a\": []}", PyJson.Dumps(typed));
        Assert.Equal("[1, 2]", PyJson.Dumps(new long[] { 1, 2 }));
        Assert.Equal("[1, 2]", PyJson.Dumps(new PyTuple(1L, 2L)));
    }

    [Fact]
    public void Dumps_of_a_dict_with_keys_of_any_type_matches_the_dict_of_their_text()
    {
        var pd = new PyDict();
        pd[1L] = "a";
        pd[2.5] = "b";
        pd[false] = "c";
        pd[null] = "d";
        pd["s"] = "e";
        Assert.Equal("{\"1\": \"a\", \"2.5\": \"b\", \"false\": \"c\", \"null\": \"d\", \"s\": \"e\"}", PyJson.Dumps(pd));
    }

    private static void RunDumps(JsonElement c, JsonElement valueTag)
    {
        var value = new Decoder().Decode(valueTag);
        var indent = c.GetProperty("ind").ValueKind == JsonValueKind.Null ? (int?)null : c.GetProperty("ind").GetInt32();
        var @default = DefaultFor(c.GetProperty("d").ValueKind == JsonValueKind.Null ? null : c.GetProperty("d").GetString());
        string Call() => PyJson.Dumps(
            value,
            ensureAscii: c.GetProperty("ia").GetBoolean(),
            indent: indent,
            sortKeys: c.GetProperty("sk").GetBoolean(),
            @default: @default,
            compact: c.GetProperty("cp").GetBoolean(),
            allowNan: c.GetProperty("nan").GetBoolean());
        if (c.TryGetProperty("e", out _))
        {
            AssertError(c, () => Call());
        }
        else
        {
            AssertText(c, Call());
        }
    }

    private static void RunLoads(JsonElement c)
    {
        var text = InputText(c.GetProperty("t"));
        if (c.TryGetProperty("e", out _))
        {
            AssertError(c, () => PyJson.Loads(text));
        }
        else if (c.TryGetProperty("rsha", out _))
        {
            var actual = PyJson.Loads(text);
            AssertText(c, Assert.IsType<string>(actual));
        }
        else if (c.TryGetProperty("rchain", out var chain))
        {
            AssertChain(chain[0].GetString() ?? string.Empty, new Decoder().Decode(chain[1]), PyJson.Loads(text));
        }
        else
        {
            var actual = PyJson.Loads(text);
            var expected = new Decoder().Decode(c.GetProperty("r"));
            if (!StrictEqual(expected, actual))
            {
                Assert.Fail($"for {Describe(c)}: expected {Py.Repr(expected)}, got {Py.Repr(actual)}");
            }
        }
    }

    /// <summary>Walks a chain of one-item lists ('l') and dicts with the key "a" ('d') without recursion.</summary>
    private static void AssertChain(string shape, object? leaf, object? actual)
    {
        var x = actual;
        foreach (var kind in shape)
        {
            if (kind == 'l')
            {
                var list = Assert.IsType<List<object?>>(x);
                Assert.Single(list);
                x = list[0];
            }
            else
            {
                var dict = Assert.IsType<OrderedDictionary<string, object?>>(x);
                Assert.Single(dict);
                x = dict["a"];
            }
        }

        Assert.True(StrictEqual(leaf, x));
    }

    private static string RandomText(Random rng)
    {
        string[] pieces = ["a", "b", "\"", "\\", "\n", "\t", "\u0000", "\u001f", "\u007f", "\u00e9", "\u65e5", "\u2028", "\uffff", "\U0001F600", "\uD800", "\uDC00", "z", " ", "/"];
        return string.Concat(Enumerable.Range(0, rng.Next(0, 6)).Select(_ => pieces[rng.Next(pieces.Length)]));
    }

    private static object? RandomJson(Random rng, int depth)
    {
        var kind = rng.Next(depth < 3 ? 9 : 6);
        switch (kind)
        {
            case 0:
                return null;
            case 1:
                return rng.Next(2) == 0;
            case 2:
                return rng.Next(4) == 0 ? rng.NextInt64(long.MinValue, long.MaxValue) : rng.Next(-1000, 1000);
            case 3:
                return rng.Next(6) switch
                {
                    0 => double.PositiveInfinity,
                    1 => double.NegativeInfinity,
                    2 => double.NaN,
                    3 => -0.0,
                    4 => BitConverter.Int64BitsToDouble(rng.NextInt64()),
                    _ => Math.Round((rng.NextDouble() - 0.5) * 1e6, rng.Next(0, 7)),
                };
            case 4:
                return rng.Next(8) == 0 ? new BigInteger(rng.NextInt64()) * rng.NextInt64() : RandomText(rng);
            case 5:
                return RandomText(rng);
            case 6:
            case 7:
                return Enumerable.Range(0, rng.Next(0, 5)).Select(_ => RandomJson(rng, depth + 1)).ToList();
            default:
                var dict = new OrderedDictionary<string, object?>();
                for (var i = rng.Next(0, 5); i > 0; i--)
                {
                    dict[RandomText(rng)] = RandomJson(rng, depth + 1);
                }

                return dict;
        }
    }

    /// <summary>The value that Loads gives when keys are sorted: a copy whose dicts are in code point order.</summary>
    private static object? SortedCopy(object? x, bool sortKeys)
    {
        if (!sortKeys)
        {
            return x;
        }

        switch (x)
        {
            case List<object?> list:
                return list.Select(i => SortedCopy(i, true)).ToList();
            case OrderedDictionary<string, object?> dict:
                var copy = new OrderedDictionary<string, object?>();
                foreach (var key in dict.Keys.OrderBy(k => k, Py.StringComparer))
                {
                    copy[key] = SortedCopy(dict[key], true);
                }

                return copy;
            default:
                return x;
        }
    }
}
