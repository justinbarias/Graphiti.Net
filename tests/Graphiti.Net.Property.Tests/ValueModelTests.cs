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
            Assert.Equal(Text(c, "s"), Py.Str(x));
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
            ExpectRead(c, "bytes", () => Py.AsBytes(v), r => Assert.Same(v, r));
            ExpectRead(c, "enum", () => Py.AsEnum<TestColor>(v), r => Assert.Equal(v, r));

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
                TestColor color => Py.EnumValue(color),
                TestEpisodeType type => Py.EnumValue(type),
                _ => throw new InvalidOperationException($"{member}"),
            };
            Assert.True(PyEqualityComparer<object?>.Instance.Equals(d.Decode(c.GetProperty("value")), value));
            Assert.Equal(Text(c, "repr"), Py.Repr(member));
            Assert.Equal(Text(c, "str"), Py.Str(member));
        }

        Assert.Equal("message", Py.EnumValue("message"));
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
                PyDict pd => pd.Values.ElementAt(i),
                OrderedDictionary<string, object?> dict => dict.GetAt(i).Value,
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
