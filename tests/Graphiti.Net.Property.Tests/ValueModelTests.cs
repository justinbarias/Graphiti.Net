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
            }
            else
            {
                Py.SortInPlace(order, i => keys[i], reverse);
                Assert.Equal(c.GetProperty("r").EnumerateArray().Select(x => x.GetInt32()), order);
            }
        }
    }

    // Laws that hold for any input. They do not use the Python values.

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
}
