namespace Graphiti.PyCompat.Tests;

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CsCheck;
using Graphiti.PyCompat;

/// <summary>
/// The values that real Python gave for the value model helpers, read from
/// <c>python-values.valuemodel.json</c>. <c>port/tools/generate_valuemodel_values.py</c> writes the file
/// and explains the tagged form of each value.
/// </summary>
internal static class ValueModelValues
{
    private static readonly JsonDocument Document = JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "python-values.valuemodel.json")));

    private static readonly Dictionary<string, Type> ErrorTypes = new()
    {
        ["ValueError"] = typeof(ArgumentException),
        ["TypeError"] = typeof(InvalidCastException),
        ["OverflowError"] = typeof(OverflowException),
        ["KeyError"] = typeof(KeyNotFoundException),
        ["AttributeError"] = typeof(MissingMemberException),
    };

    private static readonly Dictionary<string, Enum> EnumMembers = new()
    {
        ["Color.red"] = Color.Red,
        ["Color.green"] = Color.Green,
        ["Color.blue"] = Color.Blue,
        ["EpisodeType.message"] = EpisodeType.Message,
        ["EpisodeType.json"] = EpisodeType.Json,
        ["EpisodeType.text"] = EpisodeType.Text,
        ["ComparisonOperator.equals"] = ComparisonOperator.Equals,
        ["ComparisonOperator.not_equals"] = ComparisonOperator.NotEquals,
        ["ComparisonOperator.greater_than"] = ComparisonOperator.GreaterThan,
        ["ComparisonOperator.less_than"] = ComparisonOperator.LessThan,
        ["ComparisonOperator.greater_than_equal"] = ComparisonOperator.GreaterThanEqual,
        ["ComparisonOperator.less_than_equal"] = ComparisonOperator.LessThanEqual,
        ["ComparisonOperator.is_null"] = ComparisonOperator.IsNull,
        ["ComparisonOperator.is_not_null"] = ComparisonOperator.IsNotNull,
        ["Priority.low"] = Priority.Low,
        ["Priority.high"] = Priority.High,
        ["Priority.huge"] = Priority.Huge,
    };

    internal static List<JsonElement> Cases(string name) =>
        [.. Document.RootElement.GetProperty("cases").GetProperty(name).EnumerateArray()];

    /// <summary>A text of the file: a JSON string, or a list of code points for a text with a surrogate.</summary>
    internal static string Text(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : PythonValues.FromCodePoints(e);

    internal static string Text(JsonElement c, string name) => Text(c.GetProperty(name));

    /// <summary>Parses the signed hexadecimal text of a Python int.</summary>
    internal static BigInteger Hex(string text)
    {
        var negative = text.StartsWith('-');
        var value = BigInteger.Parse("0" + (negative ? text[1..] : text), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    /// <summary>
    /// Checks each case and then CsCheck draws cases at random from the list. A case holds the Python
    /// error ("e" and "msg") or a result that <paramref name="verify"/> compares. Each case gets a new
    /// <see cref="Decoder"/>, so the identity of shared objects holds inside one case only.
    /// </summary>
    internal static void Check<T>(string name, Func<JsonElement, Decoder, T> call, Action<JsonElement, T> verify)
    {
        var cases = Cases(name);
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            Run(c, call, verify);
        }

        Gen.Int[0, cases.Count - 1].Sample(i => Run(cases[i], call, verify), iter: 200);
    }

    internal static void CheckText(string name, Func<JsonElement, Decoder, string> call) =>
        Check(name, call, (c, actual) => Assert.Equal(Text(c, "r"), actual));

    /// <summary>Asserts that <paramref name="call"/> throws the C# type for a Python error, with Python's message.</summary>
    internal static void AssertError(JsonElement error, Action call)
    {
        var expected = ErrorTypes[error.GetProperty("e").GetString() ?? string.Empty];
        var ex = Record.Exception(call);
        Assert.True(ex is not null && ex.GetType() == expected, $"expected {expected.Name} for {error}, got {ex?.GetType().Name ?? "no exception"}: {ex?.Message}");
        Assert.Equal(Text(error, "msg"), ex?.Message);
    }

    private static void Run<T>(JsonElement c, Func<JsonElement, Decoder, T> call, Action<JsonElement, T> verify)
    {
        if (c.TryGetProperty("e", out _))
        {
            AssertError(c, () => call(c, new Decoder()));
        }
        else
        {
            verify(c, call(c, new Decoder()));
        }
    }

    /// <summary>Builds C# values from the tagged JSON form. Objects with the same id are the same object.</summary>
    internal sealed class Decoder
    {
        private readonly Dictionary<int, object?> ids = [];

        /// <summary>
        /// Gets or sets a value indicating whether a set whose items all have one type of long, double,
        /// string, or bool becomes a <c>PySet</c> of that type, not a <c>PySet&lt;object?&gt;</c>.
        /// </summary>
        internal bool TypedSets { get; set; }

        internal object? Decode(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Null:
                    return null;
                case JsonValueKind.True:
                    return true;
                case JsonValueKind.False:
                    return false;
            }

            if (e.TryGetProperty("ref", out var r))
            {
                return ids[r.GetInt32()];
            }

            var id = e.TryGetProperty("id", out var idElement) ? idElement.GetInt32() : -1;
            object? Keep(object? value)
            {
                if (id >= 0)
                {
                    ids[id] = value;
                }

                return value;
            }

            if (e.TryGetProperty("i", out var i))
            {
                var n = Hex(i.GetString() ?? string.Empty);
                return n >= long.MinValue && n <= long.MaxValue ? (object)(long)n : n;
            }

            if (e.TryGetProperty("f", out var f))
            {
                return Keep(BitConverter.Int64BitsToDouble(f.GetInt64()));
            }

            if (e.TryGetProperty("s", out var s))
            {
                return Text(s);
            }

            if (e.TryGetProperty("l", out var l))
            {
                var list = new List<object?>();
                Keep(list);
                list.AddRange(l.EnumerateArray().Select(Decode));
                return list;
            }

            if (e.TryGetProperty("t", out var t))
            {
                return Keep(new PyTuple([.. t.EnumerateArray().Select(Decode)]));
            }

            if (e.TryGetProperty("d", out var d))
            {
                var entries = d.EnumerateArray().ToList();
                if (entries.All(entry => entry[0].ValueKind == JsonValueKind.Object && entry[0].TryGetProperty("s", out _)))
                {
                    var dict = new OrderedDictionary<string, object?>();
                    Keep(dict);
                    foreach (var entry in entries)
                    {
                        dict[Text(entry[0].GetProperty("s"))] = Decode(entry[1]);
                    }

                    return dict;
                }

                var pd = new PyDict();
                Keep(pd);
                foreach (var entry in entries)
                {
                    pd[Decode(entry[0])] = Decode(entry[1]);
                }

                return pd;
            }

            if (e.TryGetProperty("set", out var set))
            {
                var items = set.EnumerateArray().Select(Decode).ToList();
                var frozen = e.GetProperty("frozen").GetBoolean();
                if (TypedSets && items.Count > 0)
                {
                    if (items.All(x => x is long))
                    {
                        return new PySet<long>(items.Cast<long>()) { Frozen = frozen };
                    }

                    if (items.All(x => x is double))
                    {
                        return new PySet<double>(items.Cast<double>()) { Frozen = frozen };
                    }

                    if (items.All(x => x is string))
                    {
                        return new PySet<string>(items.Cast<string>()) { Frozen = frozen };
                    }

                    if (items.All(x => x is bool))
                    {
                        return new PySet<bool>(items.Cast<bool>()) { Frozen = frozen };
                    }
                }

                return new PySet<object?>(items) { Frozen = frozen };
            }

            if (e.TryGetProperty("date", out var date))
            {
                return new DateOnly(date[0].GetInt32(), date[1].GetInt32(), date[2].GetInt32());
            }

            if (e.TryGetProperty("b", out var b))
            {
                return b.EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray();
            }

            if (e.TryGetProperty("enum", out var member))
            {
                return EnumMembers[member.GetString() ?? string.Empty];
            }

            if (e.TryGetProperty("node", out var node))
            {
                return Keep(new TestNode(node.GetString() ?? string.Empty));
            }

            if (e.TryGetProperty("edge", out var edge))
            {
                return Keep(new TestEdge(edge.GetString() ?? string.Empty));
            }

            throw new InvalidOperationException($"unknown tagged value {e}");
        }
    }
}

/// <summary>The test enum <c>Color</c> of the generator, with an int value in the middle.</summary>
internal enum Color
{
    [PyEnumMember("red", "red")]
    Red,
    [PyEnumMember("green", 2)]
    Green,
    [PyEnumMember("blue", "bl\u00e9")]
    Blue,
}

/// <summary>A copy of the upstream enum <c>EpisodeType</c>.</summary>
internal enum EpisodeType
{
    [PyEnumMember("message", "message")]
    Message,
    [PyEnumMember("json", "json")]
    Json,
    [PyEnumMember("text", "text")]
    Text,
}

/// <summary>A copy of the upstream enum <c>ComparisonOperator</c>.</summary>
internal enum ComparisonOperator
{
    [PyEnumMember("equals", "=")]
    Equals,
    [PyEnumMember("not_equals", "<>")]
    NotEquals,
    [PyEnumMember("greater_than", ">")]
    GreaterThan,
    [PyEnumMember("less_than", "<")]
    LessThan,
    [PyEnumMember("greater_than_equal", ">=")]
    GreaterThanEqual,
    [PyEnumMember("less_than_equal", "<=")]
    LessThanEqual,
    [PyEnumMember("is_null", "IS NULL")]
    IsNull,
    [PyEnumMember("is_not_null", "IS NOT NULL")]
    IsNotNull,
}

/// <summary>An enum with int values.</summary>
internal enum Priority
{
    [PyEnumMember("low", 1)]
    Low,
    [PyEnumMember("high", 2)]
    High,
    [PyEnumMember("huge", 4611686018427387904)]
    Huge,
}

/// <summary>An enum whose second member has no attribute.</summary>
internal enum Unmarked
{
    [PyEnumMember("a", "a")]
    A,
    B,
}

/// <summary>The equality of upstream <c>Node</c> (nodes.py:149): equal to each <c>Node</c> with the same UUID.</summary>
internal sealed class TestNode(string uuid)
{
    internal string Uuid => uuid;

    public override bool Equals(object? obj) => obj is TestNode other && other.Uuid == Uuid;

    public override int GetHashCode() => Uuid.GetHashCode(StringComparison.Ordinal);
}

/// <summary>The equality of upstream <c>Edge</c> (edges.py:96): equal only to a <c>Node</c> with the same UUID (defect U12).</summary>
internal sealed class TestEdge(string uuid)
{
    internal string Uuid => uuid;

    public override bool Equals(object? obj) => obj is TestNode other && other.Uuid == Uuid;

    public override int GetHashCode() => Uuid.GetHashCode(StringComparison.Ordinal);
}
