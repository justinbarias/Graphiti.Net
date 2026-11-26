namespace Graphiti.PyCompat.Tests.Models;

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using CsCheck;
using Graphiti.PyCompat;

/// <summary>
/// The values that pydantic gave, read from <c>python-values.pydantic.json</c>.
/// <c>port/tools/generate_pydantic_values.py</c> writes the file and explains the tagged form of each value.
/// </summary>
internal static class PydanticValues
{
    private static readonly JsonDocument Document = JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "python-values.pydantic.json")));

    private static readonly Dictionary<string, Enum> EnumMembers = BuildEnumMembers();

    /// <summary>Gets the stand-in for a value that a default factory made, which the test does not compare.</summary>
    internal static object Anything { get; } = new();

    internal static JsonElement Root => Document.RootElement;

    internal static List<JsonElement> Cases(string name) =>
        [.. Root.GetProperty("cases").GetProperty(name).EnumerateArray()];

    internal static List<JsonElement> List(string name) => [.. Root.GetProperty(name).EnumerateArray()];

    /// <summary>A text of the file: a JSON string, or a list of code points for a text with a surrogate.</summary>
    internal static string Text(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : PythonValues.FromCodePoints(e);

    /// <summary>
    /// Runs <paramref name="run"/> for each case, then for 200 cases that CsCheck draws from the list.
    /// </summary>
    internal static void ForEach(List<JsonElement> cases, Action<JsonElement> run)
    {
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            run(c);
        }

        Gen.Int[0, cases.Count - 1].Sample(i => run(cases[i]), iter: 200);
    }

    /// <summary>Asserts that the errors of <paramref name="ex"/> are the recorded <c>[loc, type, msg]</c> list.</summary>
    internal static void AssertErrors(JsonElement errors, ValidationException ex, string context)
    {
        var expected = errors.EnumerateArray().Select(ErrorText).ToList();
        var actual = ex.Errors.Select(e => $"{LocText(e.Loc)} {e.Type}: {e.Msg}").ToList();
        Assert.True(expected.SequenceEqual(actual), $"{context}\nexpected:\n  {string.Join("\n  ", expected)}\nactual:\n  {string.Join("\n  ", actual)}");
    }

    internal static string ErrorText(JsonElement error) =>
        $"{LocText([.. error[0].EnumerateArray().Select(l => l.ValueKind == JsonValueKind.Number ? (object)l.GetInt64() : Text(l))])} {error[1].GetString()}: {Text(error[2])}";

    /// <summary>
    /// Asserts that <paramref name="actual"/> is the recorded value: the same Python <c>repr</c>, which tells an int
    /// from a float and a bool, and a list from a tuple. A dict is compared key by key, so that a value from a
    /// default factory can be skipped.
    /// </summary>
    internal static void AssertValue(object? expected, object? actual, string context)
    {
        if (ReferenceEquals(expected, Anything))
        {
            return;
        }

        if (expected is OrderedDictionary<string, object?> e && actual is OrderedDictionary<string, object?> a)
        {
            Assert.True(e.Keys.SequenceEqual(a.Keys), $"{context}: keys {Py.Repr(e.Keys.ToList())} != {Py.Repr(a.Keys.ToList())}");
            foreach (var key in e.Keys)
            {
                AssertValue(e[key], a[key], $"{context}.{key}");
            }

            return;
        }

        if (expected is List<object?> el && actual is List<object?> al && el.Count == al.Count)
        {
            for (var i = 0; i < el.Count; i++)
            {
                AssertValue(el[i], al[i], $"{context}[{i}]");
            }

            return;
        }

        Assert.True(Py.Repr(expected) == Py.Repr(actual), $"{context}: expected {Py.Repr(expected)}, got {Py.Repr(actual)} ({actual?.GetType().Name})");
    }

    private static string LocText(IEnumerable<object> loc) =>
        "(" + string.Join(", ", loc.Select(l => l is string s ? Py.Repr(s) : Convert.ToInt64(l, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture))) + ")";

    private static Dictionary<string, Enum> BuildEnumMembers()
    {
        var result = new Dictionary<string, Enum>();
        void Add<T>()
            where T : struct, Enum
        {
            foreach (var m in Enum.GetValues<T>())
            {
                result[$"{typeof(T).Name}.{PyEnum.Name(m)}"] = m;
            }
        }

        Add<ComparisonOperator>();
        Add<EpisodeType>();
        Add<EdgeSearchMethod>();
        Add<EdgeReranker>();
        Add<NodeSearchMethod>();
        Add<NodeReranker>();
        return result;
    }

    /// <summary>Builds C# values from the tagged JSON form. Objects with the same id are the same object.</summary>
    internal sealed class Decoder
    {
        private readonly Dictionary<int, object?> ids = [];

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

            var value = DecodeTagged(e);
            if (e.TryGetProperty("id", out var id))
            {
                ids[id.GetInt32()] = value;
            }

            return value;
        }

        internal OrderedDictionary<string, object?> DecodeDict(JsonElement e) =>
            Decode(e) as OrderedDictionary<string, object?> ?? throw new InvalidOperationException($"not a dict with text keys: {e}");

        private object? DecodeTagged(JsonElement e)
        {
            if (e.TryGetProperty("i", out var i))
            {
                var n = ValueModelValues.Hex(i.GetString() ?? string.Empty);
                return n >= long.MinValue && n <= long.MaxValue ? (object)(long)n : n;
            }

            if (e.TryGetProperty("f", out var f))
            {
                return BitConverter.Int64BitsToDouble(f.GetInt64());
            }

            if (e.TryGetProperty("s", out var s))
            {
                return Text(s);
            }

            if (e.TryGetProperty("l", out var l))
            {
                return l.EnumerateArray().Select(Decode).ToList();
            }

            if (e.TryGetProperty("t", out var t))
            {
                return new PyTuple([.. t.EnumerateArray().Select(Decode)]);
            }

            if (e.TryGetProperty("d", out var d))
            {
                var entries = d.EnumerateArray().ToList();
                if (entries.All(entry => entry[0].ValueKind == JsonValueKind.Object && entry[0].TryGetProperty("s", out _)))
                {
                    var dict = new OrderedDictionary<string, object?>();
                    foreach (var entry in entries)
                    {
                        dict[Text(entry[0].GetProperty("s"))] = Decode(entry[1]);
                    }

                    return dict;
                }

                var pd = new PyDict();
                foreach (var entry in entries)
                {
                    pd[Decode(entry[0])] = Decode(entry[1]);
                }

                return pd;
            }

            if (e.TryGetProperty("set", out var set))
            {
                return new PySet<object?>(set.EnumerateArray().Select(Decode)) { Frozen = e.GetProperty("frozen").GetBoolean() };
            }

            if (e.TryGetProperty("date", out var date))
            {
                return new DateOnly(date[0].GetInt32(), date[1].GetInt32(), date[2].GetInt32());
            }

            if (e.TryGetProperty("dt", out var dt))
            {
                long? offset = dt[7].ValueKind == JsonValueKind.Null ? null : dt[7].GetInt64();
                return new PyDateTime(
                    dt[0].GetInt32(), dt[1].GetInt32(), dt[2].GetInt32(), dt[3].GetInt32(), dt[4].GetInt32(), dt[5].GetInt32(), dt[6].GetInt32(), offset);
            }

            if (e.TryGetProperty("b", out var b))
            {
                return b.EnumerateArray().Select(x => (byte)x.GetInt32()).ToArray();
            }

            if (e.TryGetProperty("enum", out var member))
            {
                return EnumMembers[member.GetString() ?? string.Empty];
            }

            if (e.TryGetProperty("model", out var model))
            {
                return TestModels.Validate(model.GetString() ?? string.Empty, DecodeDict(e.GetProperty("data")));
            }

            if (e.TryGetProperty("anything", out _))
            {
                return Anything;
            }

            throw new InvalidOperationException($"unknown tagged value {e}");
        }
    }
}

/// <summary>A copy of the upstream enum <c>search_filters.ComparisonOperator</c>.</summary>
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

/// <summary>A copy of the upstream enum <c>nodes.EpisodeType</c>.</summary>
internal enum EpisodeType
{
    [PyEnumMember("message", "message")]
    Message,
    [PyEnumMember("json", "json")]
    Json,
    [PyEnumMember("text", "text")]
    Text,
    [PyEnumMember("fact_triple", "fact_triple")]
    FactTriple,
}

/// <summary>A copy of the upstream enum <c>search_config.EdgeSearchMethod</c>.</summary>
internal enum EdgeSearchMethod
{
    [PyEnumMember("cosine_similarity", "cosine_similarity")]
    CosineSimilarity,
    [PyEnumMember("bm25", "bm25")]
    Bm25,
    [PyEnumMember("bfs", "breadth_first_search")]
    Bfs,
}

/// <summary>A copy of the upstream enum <c>search_config.NodeSearchMethod</c>.</summary>
internal enum NodeSearchMethod
{
    [PyEnumMember("cosine_similarity", "cosine_similarity")]
    CosineSimilarity,
    [PyEnumMember("bm25", "bm25")]
    Bm25,
    [PyEnumMember("bfs", "breadth_first_search")]
    Bfs,
}

/// <summary>A copy of the upstream enum <c>search_config.EdgeReranker</c>.</summary>
internal enum EdgeReranker
{
    [PyEnumMember("rrf", "reciprocal_rank_fusion")]
    Rrf,
    [PyEnumMember("node_distance", "node_distance")]
    NodeDistance,
    [PyEnumMember("episode_mentions", "episode_mentions")]
    EpisodeMentions,
    [PyEnumMember("mmr", "mmr")]
    Mmr,
    [PyEnumMember("cross_encoder", "cross_encoder")]
    CrossEncoder,
}

/// <summary>A copy of the upstream enum <c>search_config.NodeReranker</c>.</summary>
internal enum NodeReranker
{
    [PyEnumMember("rrf", "reciprocal_rank_fusion")]
    Rrf,
    [PyEnumMember("node_distance", "node_distance")]
    NodeDistance,
    [PyEnumMember("episode_mentions", "episode_mentions")]
    EpisodeMentions,
    [PyEnumMember("mmr", "mmr")]
    Mmr,
    [PyEnumMember("cross_encoder", "cross_encoder")]
    CrossEncoder,
}
