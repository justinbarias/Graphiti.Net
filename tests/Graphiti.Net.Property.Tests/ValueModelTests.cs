namespace Graphiti.PyCompat.Tests;

using System.Text.Json;
using CsCheck;
using Graphiti.PyCompat;
using static Graphiti.PyCompat.Tests.ValueModelValues;

public class ValueModelTests
{
    [Fact]
    public void Repr_matches_Python() => CheckText("repr", (c, d) => Py.Repr(d.Decode(c.GetProperty("v"))));

    [Fact]
    public void Str_matches_Python() => CheckText("str", (c, d) => Py.Str(d.Decode(c.GetProperty("v"))));

    [Fact]
    public void Repr_and_Str_of_a_float_match_Python()
    {
        var cases = Cases("float_repr");
        Assert.True(cases.Count > 5000);
        foreach (var c in cases)
        {
            var x = BitConverter.Int64BitsToDouble(c.GetProperty("x").GetInt64());
            Assert.Equal(Text(c, "r"), Py.Repr(x));
            Assert.Equal(Text(c, c.TryGetProperty("s", out _) ? "s" : "r"), Py.Str(x));
        }
    }

    [Fact]
    public void Truthy_matches_Python() =>
        Check("truthy", (c, d) => Py.Truthy(d.Decode(c.GetProperty("v"))), (c, v) => Assert.Equal(c.GetProperty("r").GetBoolean(), v));

    [Fact]
    public void Len_matches_Python() =>
        Check("len", (c, d) => Py.Len(d.Decode(c.GetProperty("v"))), (c, v) => Assert.Equal(c.GetProperty("r").GetInt64(), v));

    [Fact]
    public void Format_matches_Python() =>
        CheckText("format", (c, d) => Py.Format(d.Decode(c.GetProperty("v")), Text(c, "spec")));

    [Fact]
    public void Typed_reads_accept_what_isinstance_accepts()
    {
        foreach (var c in Cases("as"))
        {
            var v = new Decoder().Decode(c.GetProperty("v"));
            ExpectRead(c, "bool", () => Py.AsBool(v), r => Assert.Equal(v, r));
            ExpectRead(c, "str", () => Py.AsString(v), r => Assert.Same(v, r));
            ExpectRead(c, "list", () => Py.AsList(v), r => Assert.Same(v, r));
            ExpectRead(c, "tuple", () => Py.AsTuple(v), r => Assert.Same(v, r));
            ExpectRead(c, "dict", () => Py.AsDict(v), r => Assert.Same(v, r));
            ExpectRead(c, "pydict", () => Py.AsPyDict(v), r => Assert.Same(v, r));
            ExpectRead(c, "date", () => Py.AsDate(v), r => Assert.Equal(v, r));
            ExpectRead(c, "datetime", () => Py.AsDateTime(v), r => Assert.Equal(v, r));
            ExpectRead(c, "bytes", () => Py.AsBytes(v), r => Assert.Same(v, r));
            ExpectRead(c, "enum", () => Py.AsEnum<Color>(v), r => Assert.Equal(v, r));

            var asInt = c.GetProperty("int");
            if (asInt.ValueKind == JsonValueKind.Null)
            {
                Assert.Throws<InvalidCastException>(() => Py.AsLong(v));
            }
            else if (asInt.TryGetProperty("big", out _))
            {
                Assert.Throws<OverflowException>(() => Py.AsLong(v));
            }
            else
            {
                Assert.Equal(Hex(asInt.GetProperty("r").GetString() ?? string.Empty), Py.AsLong(v));
            }

            var asFloat = c.GetProperty("float");
            if (asFloat.ValueKind == JsonValueKind.Null)
            {
                Assert.Throws<InvalidCastException>(() => Py.AsDouble(v));
            }
            else if (asFloat.TryGetProperty("e", out _))
            {
                AssertError(asFloat, () => Py.AsDouble(v));
            }
            else
            {
                Assert.Equal(asFloat.GetProperty("r").GetInt64(), BitConverter.DoubleToInt64Bits(Py.AsDouble(v)));
            }
        }
    }

    [Fact]
    public void Key_equality_matches_Python()
    {
        var comparer = PyEqualityComparer<object?>.Instance;
        foreach (var c in Cases("equal"))
        {
            var d = new Decoder();
            var a = d.Decode(c.GetProperty("a"));
            var b = d.Decode(c.GetProperty("b"));
            var equal = c.GetProperty("r").GetBoolean();
            Assert.True(equal == comparer.Equals(a, b), $"{c}");
            if (a is PyTuple tuple)
            {
                // A tuple compares as Python compares it also through object.Equals.
                Assert.True(equal == tuple.Equals(b), $"{c}");
            }

            // A law: equal values that can be hashed have equal hash codes.
            if (equal && Hashable(a) && Hashable(b))
            {
                Assert.Equal(comparer.GetHashCode(a), comparer.GetHashCode(b));
            }
        }
    }

    [Fact]
    public void Unhashable_values_match_Python() =>
        Check("hashable", (c, d) => PyEqualityComparer<object?>.Instance.GetHashCode(d.Decode(c.GetProperty("v"))), (c, _) => Assert.True(c.GetProperty("r").GetBoolean()));

    [Fact]
    public void PyDict_operations_match_Python()
    {
        foreach (var c in Cases("dict_ops"))
        {
            var decoder = new Decoder();
            var dict = new PyDict();
            var results = c.GetProperty("results").EnumerateArray().ToList();
            foreach (var (op, i) in c.GetProperty("ops").GetProperty("l").EnumerateArray().Select((op, i) => (op.GetProperty("l"), i)))
            {
                var name = (string)(decoder.Decode(op[0]) ?? string.Empty);
                var key = decoder.Decode(op[1]);
                var value = decoder.Decode(op[2]);
                ExpectResult(results[i], name, name switch
                {
                    "set" => () => { dict[key] = value; return null; }
                    ,
                    "get" => () => Py.Get(dict, key),
                    "get_default" => () => Py.Get(dict, key, value),
                    "getitem" => () => dict[key],
                    "del" => () => dict.Remove(key) ? null : throw new KeyNotFoundException(Py.Repr(key)),
                    "in" => () => dict.ContainsKey(key),
                    "setdefault" => () => Py.SetDefault(dict, key, value),
                    _ => () => Py.SetDefault(dict, key),
                });
            }

            Assert.Equal(Text(c, "repr"), Py.Repr(dict));
            Assert.Equal(c.GetProperty("len").GetInt32(), dict.Count);
        }
    }

    [Fact]
    public void Get_and_SetDefault_on_a_dict_with_string_keys_match_Python()
    {
        foreach (var c in Cases("str_dict_ops"))
        {
            var decoder = new Decoder();
            var dict = new OrderedDictionary<string, object?>();
            var results = c.GetProperty("results").EnumerateArray().ToList();
            foreach (var (op, i) in c.GetProperty("ops").GetProperty("l").EnumerateArray().Select((op, i) => (op.GetProperty("l"), i)))
            {
                var name = (string)(decoder.Decode(op[0]) ?? string.Empty);
                var key = (string)(decoder.Decode(op[1]) ?? string.Empty);
                var value = decoder.Decode(op[2]);
                var result = results[i];
                if (name == "getitem" && result.TryGetProperty("e", out _))
                {
                    // R6: d[k] on a missing key throws KeyNotFoundException. The .NET message is not Python's.
                    Assert.Throws<KeyNotFoundException>(() => dict[key]);
                    continue;
                }

                ExpectResult(result, name, name switch
                {
                    "set" => () => { dict[key] = value; return null; }
                    ,
                    "get" => () => Py.Get(dict, key),
                    "get_default" => () => Py.Get(dict, key, value),
                    "getitem" => () => dict[key],
                    "del" => () => dict.Remove(key) ? null : throw new KeyNotFoundException(Py.Repr(key)),
                    "in" => () => dict.ContainsKey(key),
                    "setdefault" => () => Py.SetDefault(dict, key, value),
                    _ => () => Py.SetDefault(dict, key, null),
                });
            }

            Assert.Equal(Text(c, "repr"), Py.Repr(dict));
        }
    }

    [Fact]
    public void PySet_operations_match_Python()
    {
        foreach (var c in Cases("set_ops"))
        {
            var decoder = new Decoder();
            var set = new PySet<object?>();
            var results = c.GetProperty("results").EnumerateArray().ToList();
            foreach (var (op, i) in c.GetProperty("ops").GetProperty("l").EnumerateArray().Select((op, i) => (op.GetProperty("l"), i)))
            {
                var name = (string)(decoder.Decode(op[0]) ?? string.Empty);
                var key = decoder.Decode(op[1]);
                ExpectResult(results[i], name, name switch
                {
                    "add" => () => { set.Add(key); return null; }
                    ,
                    "in" => () => set.Contains(key),
                    "discard" => () => { set.Discard(key); return null; }
                    ,
                    _ => () => { set.Remove(key); return null; }
                    ,
                });
            }

            // The content as sorted repr text. The order of a Python set is not a contract (R14).
            var expected = c.GetProperty("items").EnumerateArray().Select(Text).ToList();
            Assert.Equal(expected, set.Select(Py.Repr).Order(Py.StringComparer).ToList());
            Assert.Equal(c.GetProperty("len").GetInt32(), set.Count);
        }
    }

    [Fact]
    public void A_frozenset_cannot_change()
    {
        var frozen = new PySet<object?>([1L]) { Frozen = true };
        foreach (var c in Cases("frozen_mutation"))
        {
            AssertError(c, c.GetProperty("method").GetString() switch
            {
                "add" => () => frozen.Add(1L),
                "remove" => () => frozen.Remove(1L),
                _ => () => frozen.Discard(1L),
            });
        }

        Assert.Single(frozen);
    }

    [Fact]
    public void Node_and_edge_keys_test_identity_first_then_the_stored_key()
    {
        var comparer = PyEqualityComparer<object?>.Instance;
        foreach (var c in Cases("node_keys"))
        {
            var decoder = new Decoder();
            var items = c.GetProperty("items").GetProperty("l").EnumerateArray().Select(decoder.Decode).ToList();
            var probes = c.GetProperty("probes").GetProperty("l").EnumerateArray().Select(decoder.Decode).ToList();
            var set = new PySet<object?>(items);
            var hashSet = new HashSet<object?>(items, comparer);
            var dict = new PyDict();
            foreach (var item in items)
            {
                dict[item] = null;
            }

            Assert.Equal(c.GetProperty("len").GetInt32(), set.Count);
            Assert.Equal(c.GetProperty("len").GetInt32(), hashSet.Count);
            Assert.Equal(c.GetProperty("dict_len").GetInt32(), dict.Count);
            Assert.Equal(c.GetProperty("in").EnumerateArray().Select(x => x.GetBoolean()), probes.Select(set.Contains));
            Assert.Equal(c.GetProperty("in").EnumerateArray().Select(x => x.GetBoolean()), probes.Select(hashSet.Contains));
            Assert.Equal(c.GetProperty("in_dict").EnumerateArray().Select(x => x.GetBoolean()), probes.Select(dict.ContainsKey));
        }
    }

    [Fact]
    public void Unpack2_matches_Python() =>
        Check("unpack2", (c, d) => (Decoder: d, Pair: Py.Unpack2((IReadOnlyList<object?>)(d.Decode(c.GetProperty("v")) ?? new List<object?>()))), (c, r) =>
        {
            Assert.Equal(Py.Repr(r.Decoder.Decode(c.GetProperty("a"))), Py.Repr(r.Pair.First));
            Assert.Equal(Py.Repr(r.Decoder.Decode(c.GetProperty("b"))), Py.Repr(r.Pair.Second));
        });

    [Fact]
    public void DeepCopy_matches_Python()
    {
        foreach (var c in Cases("deepcopy"))
        {
            var original = new Decoder().Decode(c.GetProperty("v"));
            var copy = Py.DeepCopy(original);
            Assert.Equal(Text(c, "repr"), Py.Repr(copy));
            var paths = c.GetProperty("paths").EnumerateArray().Select(p => p.EnumerateArray().Select(x => x.GetInt32()).ToList()).ToList();
            var same = c.GetProperty("same").EnumerateArray().Select(x => x.GetBoolean()).ToList();
            var alias = c.GetProperty("alias").EnumerateArray().Select(x => x.GetInt32()).ToList();
            for (var i = 0; i < paths.Count; i++)
            {
                var copied = At(copy, paths[i]);
                Assert.True(same[i] == ReferenceEquals(copied, At(original, paths[i])), $"same at {i} in {c}");
                Assert.Equal(alias[i], paths.FindIndex(p => ReferenceEquals(At(copy, p), copied)));
            }
        }
    }

    [Fact]
    public void Enum_members_match_Python()
    {
        foreach (var c in Cases("enum"))
        {
            var d = new Decoder();
            var member = d.Decode(c.GetProperty("v"));
            var value = member switch
            {
                Color color => Py.EnumValue(color),
                EpisodeType type => Py.EnumValue(type),
                ComparisonOperator op => Py.EnumValue(op),
                Priority priority => Py.EnumValue(priority),
                _ => throw new InvalidOperationException($"{member}"),
            };
            Assert.True(PyEqualityComparer<object?>.Instance.Equals(d.Decode(c.GetProperty("value")), value));
            Assert.Equal(Text(c, "repr"), Py.Repr(member));
            Assert.Equal(Text(c, "str"), Py.Str(member));
        }

        Assert.Equal("message", Py.EnumValue("message"));
    }

    [Fact]
    public void Format_throws_for_an_option_that_is_not_built()
    {
        // Python accepts each of these specs. Py.Format does not build them (see its XML comment).
        var cases = Cases("format_unbuilt");
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            var v = new Decoder().Decode(c.GetProperty("v"));
            var ex = Record.Exception(() => Py.Format(v, Text(c, "spec")));
            Assert.True(ex is InvalidOperationException, $"{c}: {ex?.GetType().Name ?? "no exception"}");
        }
    }

    [Fact]
    public void Sets_with_other_item_types_compare_as_Python_compares_them()
    {
        foreach (var c in Cases("typed_set_equal"))
        {
            var d = new Decoder { TypedSets = true };
            var a = d.Decode(c.GetProperty("a"));
            var b = d.Decode(c.GetProperty("b"));
            Assert.True(c.GetProperty("r").GetBoolean() == PyEqualityComparer<object?>.Instance.Equals(a, b), $"{c}");
        }
    }

    [Fact]
    public void GetNullable_on_a_dict_with_int_values_matches_Python()
    {
        var d = new OrderedDictionary<string, long> { ["a"] = 1, ["b"] = 0 };
        foreach (var c in Cases("int_dict_get"))
        {
            var expected = c.GetProperty("r");
            long? want = expected.ValueKind == JsonValueKind.Null ? null : expected.GetInt64();
            Assert.Equal(want, Py.GetNullable(d, Text(c, "k")));
        }
    }

    [Fact]
    public void Enum_FromValue_matches_Python()
    {
        foreach (var c in Cases("enum_from_value"))
        {
            var d = new Decoder();
            var value = d.Decode(c.GetProperty("value"));
            Func<object> call = c.GetProperty("cls").GetString() switch
            {
                "Color" => () => PyEnum.FromValue<Color>(value),
                "EpisodeType" => () => PyEnum.FromValue<EpisodeType>(value),
                "ComparisonOperator" => () => PyEnum.FromValue<ComparisonOperator>(value),
                _ => () => PyEnum.FromValue<Priority>(value),
            };
            if (c.TryGetProperty("e", out _))
            {
                AssertError(c, () => call());
            }
            else
            {
                Assert.Equal(d.Decode(c.GetProperty("r")), call());
            }
        }
    }

    [Fact]
    public void An_enum_member_with_no_attribute_is_named_in_the_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Py.Repr(Unmarked.A));
        Assert.Contains("Unmarked.B", ex.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidCastException>(() => Py.Len(Color.Red));
    }

    [Fact]
    public void A_typed_read_of_typed_data_names_both_types()
    {
        var ex = Assert.Throws<InvalidCastException>(() => Py.AsList(new List<string>()));
        Assert.Equal("expected list as List<object?>, got list as List<String>", ex.Message);
    }

    [Fact]
    public void List_helpers_match_Python()
    {
        foreach (var c in Cases("list_ops"))
        {
            var d = new Decoder();
            var xs = (List<object?>)(d.Decode(c.GetProperty("xs")) ?? new List<object?>());
            var x = d.Decode(c.GetProperty("x"));
            Assert.True(c.GetProperty("in").GetBoolean() == Py.Contains(xs, x), $"in {c}");
            Assert.Equal(c.GetProperty("count").GetInt64(), Py.Count(xs, x));
            if (c.TryGetProperty("index", out var index))
            {
                Assert.Equal(index.GetInt64(), Py.IndexOf(xs, x));
            }
            else if (c.GetProperty("index_error").TryGetProperty("msg", out _))
            {
                AssertError(c.GetProperty("index_error"), () => Py.IndexOf(xs, x));
            }
            else
            {
                Assert.Throws<ArgumentException>(() => Py.IndexOf(xs, x));
            }

            var after = new List<object?>(xs);
            if (c.TryGetProperty("removed", out var removed))
            {
                Py.Remove(after, x);
                var expected = new List<object?>(xs);
                expected.RemoveAt(removed.GetInt32());
                Assert.True(expected.Zip(after).All(p => ReferenceEquals(p.First, p.Second)) && expected.Count == after.Count, $"remove {c}");
            }
            else
            {
                AssertError(c.GetProperty("remove_error"), () => Py.Remove(after, x));
            }
        }
    }

    [Fact]
    public void SortInPlace_matches_Python()
    {
        foreach (var c in Cases("sort"))
        {
            var keys = (List<object?>)(new Decoder().Decode(c.GetProperty("keys")) ?? new List<object?>());
            var order = Enumerable.Range(0, keys.Count).ToList();
            var reverse = c.GetProperty("reverse").GetBoolean();
            if (c.TryGetProperty("e", out _))
            {
                AssertError(c, () => Py.SortInPlace(order, i => keys[i], reverse));

                // The list holds the partly sorted items of the moment of the error.
                Assert.Equal(c.GetProperty("after").EnumerateArray().Select(x => x.GetInt32()), order);
            }
            else
            {
                Py.SortInPlace(order, i => keys[i], reverse);
                Assert.Equal(c.GetProperty("r").EnumerateArray().Select(x => x.GetInt32()), order);
            }
        }
    }

    [Fact]
    public void SortInPlace_of_generated_keys_matches_Python()
    {
        foreach (var c in Cases("sort_lcg"))
        {
            var keys = LcgKeys(c.GetProperty("shape").GetString() ?? string.Empty, c.GetProperty("n").GetInt32(), (ulong)c.GetProperty("seed").GetInt64());
            var order = Enumerable.Range(0, keys.Count).ToList();
            Py.SortInPlace(order, i => keys[i], c.GetProperty("reverse").GetBoolean());
            if (c.TryGetProperty("r", out var r))
            {
                Assert.True(r.EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(order), $"{c.GetProperty("shape")} n={c.GetProperty("n")}");
            }
            else
            {
                var text = string.Join(',', order.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
                Assert.True(c.GetProperty("sha256").GetString() == digest, $"{c.GetProperty("shape")} n={c.GetProperty("n")} seed={c.GetProperty("seed")} reverse={c.GetProperty("reverse")}");
            }
        }
    }

    [Fact]
    public void SortInPlace_treats_the_list_as_Python_does()
    {
        foreach (var c in Cases("sort_mutation"))
        {
            var kind = c.GetProperty("kind").GetString();
            var at = c.GetProperty("at").GetInt32();
            var xs = c.GetProperty("items").EnumerateArray().Select(x => x.GetInt64()).ToList();
            var seen = new List<long>();
            object? Key(long x)
            {
                switch (kind)
                {
                    case "len":
                        seen.Add(xs.Count);
                        break;
                    case "order":
                        seen.Add(x);
                        break;
                    case "append":
                        xs.Add(99);
                        break;
                    default:
                        if (seen.Count == at)
                        {
                            throw new ArgumentException("boom");
                        }

                        seen.Add(x);
                        break;
                }

                return x;
            }

            var reverse = c.GetProperty("reverse").GetBoolean();
            if (c.TryGetProperty("e", out _))
            {
                AssertError(c, () => Py.SortInPlace(xs, Key, reverse));
            }
            else
            {
                Py.SortInPlace(xs, Key, reverse);
            }

            if (kind is "len" or "order")
            {
                Assert.Equal(c.GetProperty("seen").EnumerateArray().Select(x => x.GetInt64()), seen);
            }

            Assert.Equal(c.GetProperty("final").EnumerateArray().Select(x => x.GetInt64()), xs);
        }
    }

    [Fact]
    public void LessThan_matches_Python() =>
        Check("less_than", (c, d) => Py.LessThan(d.Decode(c.GetProperty("a")), d.Decode(c.GetProperty("b"))), (c, r) => Assert.True(c.GetProperty("r").GetBoolean() == r, $"{c}"));

    [Fact]
    public void LessThan_takes_typed_lists_and_refuses_types_outside_the_value_model()
    {
        Assert.True(Py.LessThan(new List<string> { "a" }, new List<object?> { "b" }));
        Assert.False(Py.LessThan(new List<long> { 2 }, new List<double> { 1.5 }));
        Assert.Throws<InvalidOperationException>(() => Py.LessThan((1.0, "a"), (2.0, "a")));
        Assert.Throws<InvalidOperationException>(() => Py.LessThan(1, 2L));
        Assert.Throws<InvalidOperationException>(() => Py.LessThan(1L, 2m));
    }

    // Laws that hold for any input. They do not use the Python values.

    [Fact]
    public void SortInPlace_of_100000_doubles_is_ordered_and_stable()
    {
        var random = new Random(7);
        var keys = Enumerable.Range(0, 100_000).Select(_ => (object?)Math.Round(random.NextDouble() * 1000, 1)).ToList();
        var order = Enumerable.Range(0, keys.Count).ToList();
        Py.SortInPlace(order, i => keys[i]);
        for (var i = 1; i < order.Count; i++)
        {
            var a = (double)(keys[order[i - 1]] ?? 0.0);
            var b = (double)(keys[order[i]] ?? 0.0);
            Assert.True(a < b || (a == b && order[i - 1] < order[i]), $"at {i}");
        }
    }

    [Fact]
    public void PySet_iterates_in_first_insertion_order()
    {
        // R14 chooses insertion order for a set that code iterates. The model is a list that keeps the
        // first of equal items and drops an item when it is discarded.
        Gen.Select(Gen.Bool, Gen.Long[-5, 5]).Array[0, 40].Sample(ops =>
        {
            var set = new PySet<long>();
            var model = new List<long>();
            foreach (var (add, x) in ops)
            {
                if (add)
                {
                    set.Add(x);
                    if (!model.Contains(x))
                    {
                        model.Add(x);
                    }
                }
                else
                {
                    set.Discard(x);
                    model.Remove(x);
                }
            }

            return set.SequenceEqual(model);
        });
    }

    [Fact]
    public void Repr_of_a_float_reads_back_as_the_same_float() =>
        Gen.Double.Where(double.IsFinite).Sample(x =>
            BitConverter.DoubleToInt64Bits(Py.Float(Py.Repr(x))) == BitConverter.DoubleToInt64Bits(x));

    [Fact]
    public void Values_outside_the_value_model_are_rejected()
    {
        object outside = new Uri("https://example.invalid");
        Assert.Throws<InvalidOperationException>(() => Py.Repr(outside));
        Assert.Throws<InvalidOperationException>(() => Py.Repr(new List<object?> { outside }));
        Assert.Throws<InvalidOperationException>(() => Py.Truthy(outside));
        Assert.Throws<InvalidOperationException>(() => Py.Len(outside));
        Assert.Throws<InvalidOperationException>(() => Py.DeepCopy(outside));
        Assert.Throws<InvalidOperationException>(() => Py.Format(outside, ">3"));
    }

    /// <summary>The keys of <c>lcg_keys</c> in the generator, built from the same 64-bit LCG.</summary>
    private static List<object?> LcgKeys(string shape, int n, ulong seed)
    {
        var state = seed;
        long Next()
        {
            state = unchecked((state * 6364136223846793005UL) + 1442695040888963407UL);
            return (long)(state >> 33);
        }

        var keys = new List<object?>(n);
        switch (shape)
        {
            case "sorted":
                keys.AddRange(Enumerable.Range(0, n).Select(i => (object?)(long)(i / 3)));
                return keys;
            case "reversed":
                keys.AddRange(Enumerable.Range(0, n).Select(i => (object?)(long)((n - i) / 3)));
                return keys;
            case "equal":
                keys.AddRange(Enumerable.Range(0, n).Select(_ => (object?)0L));
                return keys;
            case "sawtooth":
                keys.AddRange(Enumerable.Range(0, n).Select(i => (object?)(long)(i % 50)));
                return keys;
            case "halves":
                keys.AddRange(Enumerable.Range(0, n).Select(i => (object?)(long)(i % ((n / 2) + 1))));
                return keys;
            case "swaps":
                keys.AddRange(Enumerable.Range(0, n).Select(i => (object?)(long)(i / 2)));
                for (var j = 0; j < Math.Max(1, n / 100); j++)
                {
                    var a = (int)(Next() % n);
                    var b = (int)(Next() % n);
                    (keys[a], keys[b]) = (keys[b], keys[a]);
                }

                return keys;
        }

        for (var i = 0; i < n; i++)
        {
            var r = Next();
            if (shape.StartsWith("nan", StringComparison.Ordinal))
            {
                var p = int.Parse(shape[3..], System.Globalization.CultureInfo.InvariantCulture);
                keys.Add(r % 100 < p ? double.NaN : ((r / 100 % 2000) / 8.0) - 125.0);
            }
            else if (shape == "inf")
            {
                keys.Add(r % 10 == 0 ? double.PositiveInfinity : r % 10 == 1 ? double.NegativeInfinity : ((r / 10 % 2000) / 8.0) - 125.0);
            }
            else
            {
                keys.Add(r % 2 == 0 ? (object?)((r / 2 % 300) - 150) : ((r / 2 % 2400) / 8.0) - 150.0);
            }
        }

        return keys;
    }

    private static bool Hashable(object? x)
    {
        try
        {
            PyEqualityComparer<object?>.Instance.GetHashCode(x);
            return true;
        }
        catch (InvalidCastException)
        {
            return false;
        }
    }

    private static object? At(object? value, List<int> path)
    {
        foreach (var i in path)
        {
            value = value switch
            {
                PyDict pd => i < 0 ? pd.Keys.ElementAt(-i - 1) : pd.Values.ElementAt(i),
                OrderedDictionary<string, object?> dict => i < 0 ? dict.GetAt(-i - 1).Key : dict.GetAt(i).Value,
                PyTuple t => t[i],
                List<object?> list => list[i],
                _ => throw new InvalidOperationException($"no item {i} in {value}"),
            };
        }

        return value;
    }

    private static void ExpectRead<T>(JsonElement c, string name, Func<T> read, Action<T> verify)
    {
        if (c.GetProperty(name).GetBoolean())
        {
            verify(read());
        }
        else
        {
            Assert.Throws<InvalidCastException>(() => read());
        }
    }

    private static void ExpectResult(JsonElement expected, string op, Func<object?> call)
    {
        if (expected.TryGetProperty("e", out _))
        {
            AssertError(expected, () => call());
        }
        else
        {
            Assert.True(Text(expected, "r") == Py.Repr(call()), $"{op}: expected {Text(expected, "r")}");
        }
    }

    [Fact]
    public void Iter_gives_what_a_Python_for_loop_gives()
    {
        // Each expected value is what Python 3.12 gives for list(value), or the text of the TypeError of iter(value).
        Assert.Equal(["x", 1L, null], Py.Iter(new List<object?> { "x", 1L, null }));
        Assert.Equal(["a", "b"], Py.Iter(new List<string> { "a", "b" }));
        Assert.Equal(["x", 1L], Py.Iter(PyTuple.From(["x", 1L])));
        Assert.Equal(["a", "\U0001F600", "b"], Py.Iter("a\U0001F600b"));
        Assert.Equal([65L, 66L], Py.Iter(new byte[] { 65, 66 }));
        Assert.Empty(Py.Iter(Array.Empty<byte>()));
        Assert.Equal(["k", "j"], Py.Iter(new OrderedDictionary<string, object?> { ["k"] = 2L, ["j"] = 1L }));
        var dict = new PyDict();
        dict[1L] = "a";
        dict["k"] = 2L;
        Assert.Equal([1L, "k"], Py.Iter(dict));

        Assert.Equal("'NoneType' object is not iterable", Assert.Throws<InvalidCastException>(() => Py.Iter(null)).Message);
        Assert.Equal("'int' object is not iterable", Assert.Throws<InvalidCastException>(() => Py.Iter(5L)).Message);
        Assert.Equal("'float' object is not iterable", Assert.Throws<InvalidCastException>(() => Py.Iter(1.5)).Message);
        Assert.Equal("'bool' object is not iterable", Assert.Throws<InvalidCastException>(() => Py.Iter(true)).Message);
    }

    [Fact]
    public void NotNone_returns_the_value_or_throws_the_AttributeError_of_None()
    {
        // Python 3.12: None.replace('-', '') raises AttributeError("'NoneType' object has no attribute 'replace'").
        Assert.Equal("a-b", Py.NotNone("a-b", "replace"));
        var error = Assert.Throws<MissingMemberException>(() => Py.NotNone<string>(null, "replace"));
        Assert.Equal("'NoneType' object has no attribute 'replace'", error.Message);
    }

    // Python 3.12: x.get raises AttributeError("'<type>' object has no attribute 'get'") for each value below.
    [Theory]
    [InlineData("int")]
    [InlineData("str")]
    [InlineData("list")]
    [InlineData("tuple")]
    [InlineData("float")]
    [InlineData("bool")]
    [InlineData("NoneType")]
    public void Receiver_returns_a_value_of_the_type_or_throws_the_AttributeError_of_Python(string type)
    {
        object? value = type switch
        {
            "int" => 5L,
            "str" => "x",
            "list" => new List<object?> { 1L },
            "tuple" => new PyTuple(1L),
            "float" => 1.5,
            "bool" => true,
            _ => null,
        };
        var error = Assert.Throws<MissingMemberException>(() => Py.Receiver<OrderedDictionary<string, object?>>(value, "get"));
        Assert.Equal($"'{type}' object has no attribute 'get'", error.Message);

        var dict = new OrderedDictionary<string, object?> { ["a"] = 1L };
        Assert.Same(dict, Py.Receiver<OrderedDictionary<string, object?>>(dict, "get"));
    }

    [Fact]
    public void TypeName_of_a_delegate_is_function()
    {
        // Python 3.12: type(f).__name__ is 'function' for a def and for a lambda.
        Assert.Equal("function", Py.TypeName((Func<long>)(() => 0L)));
    }

    [Fact]
    public void Int_of_an_open_value_gives_what_int_gives_in_Python()
    {
        // Python 3.12: int(True) 1, int(7) 7, int(7.9) 7, int(-7.9) -7, int('  12 ') 12, int(b'12') 12.
        Assert.Equal(1L, Py.Int((object?)true));
        Assert.Equal(7L, Py.Int((object?)7L));
        Assert.Equal(7L, Py.Int((object?)7.9));
        Assert.Equal(-7L, Py.Int((object?)-7.9));
        Assert.Equal(12L, Py.Int((object?)"  12 "));
        Assert.Equal(12L, Py.Int((object?)"12"u8.ToArray()));

        // Python 3.12: int(b' 12 '), int(b'\t12\n\x0b\x0c\r'), int(b'1_2'), and int(b'+12') give 12.
        foreach (var good in new byte[][] { " 12 "u8.ToArray(), "\t12\n\v\f\r"u8.ToArray(), "1_2"u8.ToArray(), "+12"u8.ToArray() })
        {
            Assert.Equal(12L, Py.Int((object?)good));
        }

        // Python 3.12: for bytes, only ASCII whitespace is whitespace. Each of these raises ValueError with the
        // bytes repr: b'12\xa0', b'\xa012', b'\x8512', b'\x1c12', b'', b'12\x00', and b'1 2'.
        foreach (var (bad, repr) in new (byte[], string)[]
        {
            ([0x31, 0x32, 0xA0], @"b'12\xa0'"),
            ([0xA0, 0x31, 0x32], @"b'\xa012'"),
            ([0x85, 0x31, 0x32], @"b'\x8512'"),
            ([0x1C, 0x31, 0x32], @"b'\x1c12'"),
            ([], "b''"),
            ([0x31, 0x32, 0x00], @"b'12\x00'"),
            ([0x31, 0x20, 0x32], "b'1 2'"),
        })
        {
            var error = Assert.Throws<ArgumentException>(() => Py.Int((object?)bad));
            Assert.Equal("invalid literal for int() with base 10: " + repr, error.Message);
        }

        // Python 3.12: int(None), int([1]), and int({'a': 1}) raise TypeError with the type name at the end.
        foreach (var (value, name) in new (object?, string)[] { (null, "NoneType"), (new List<object?> { 1L }, "list"), (new OrderedDictionary<string, object?>(), "dict") })
        {
            var error = Assert.Throws<InvalidCastException>(() => Py.Int(value));
            Assert.Equal($"int() argument must be a string, a bytes-like object or a real number, not '{name}'", error.Message);
        }

        // Python returns 2**70. A long cannot hold it (R4).
        Assert.Throws<OverflowException>(() => Py.Int((object?)System.Numerics.BigInteger.Pow(2, 70)));
    }

    [Fact]
    public void PlainEqual_is_the_expression_a_eq_b_with_no_identity_test_at_the_top()
    {
        // Python: n = float('nan'). n == n is False, [n] == [n] is True, (n,) == (n,) is True, {'a': n} == {'a': n} is True.
        object nan = double.NaN;
        Assert.True(PyValueEquality.Equal(nan, nan));
        Assert.False(PyValueEquality.PlainEqual(nan, nan));
        Assert.False(PyValueEquality.PlainEqual(nan, double.NaN));
        Assert.True(PyValueEquality.PlainEqual(new List<object?> { nan }, new List<object?> { nan }));
        Assert.True(PyValueEquality.PlainEqual(PyTuple.From([nan]), PyTuple.From([nan])));
        Assert.True(PyValueEquality.PlainEqual(new OrderedDictionary<string, object?> { ["a"] = nan }, new OrderedDictionary<string, object?> { ["a"] = nan }));
        Assert.True(PyValueEquality.PlainEqual(1L, 1.0));
        Assert.True(PyValueEquality.PlainEqual(true, 1L));
        Assert.True(PyValueEquality.PlainEqual(null, null));
        Assert.True(PyValueEquality.PlainEqual("a", "a"));
    }
}
