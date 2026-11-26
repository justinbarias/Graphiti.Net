namespace Graphiti.JudgeHost.Core;

using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Graphiti.PyCompat;

/// <summary>
/// Decodes a case argument from JSON to the Python value model (RULES.md R6), as <c>decode</c> in
/// <c>port/judge/host-python/codec.py</c> does. Plain JSON: an object is an
/// <c>OrderedDictionary&lt;string, object?&gt;</c> in key order, an array is a <c>List&lt;object?&gt;</c>, a whole
/// number is a <c>long</c> or a <c>BigInteger</c>, another number is a <c>double</c>. The tags of the README are
/// tested in the order of <c>codec.py</c>. An object with no known tag is a plain dict, as in Python.
/// </summary>
internal class ValueDecoder(JudgeRegistry registry)
{
    /// <summary>The tags of the flow layer. The flow host is not ported, so each one is not bound.</summary>
    internal static readonly IReadOnlyList<string> FlowTags = ["$step", "$graphiti", "$embed"];

    /// <summary>Gets the registry that resolves <c>$ref</c>, <c>$enum</c>, and <c>$model</c> paths.</summary>
    internal JudgeRegistry Registry { get; } = registry;

    /// <summary>Decodes one value.</summary>
    internal object? Decode(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => DecodeObject(value),
        JsonValueKind.Array => DecodeList(value),
        JsonValueKind.String => JudgeJson.ReadString(value),
        JsonValueKind.Number => DecodeNumber(value),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException($"ValueDecoder: no value of kind {value.ValueKind}"),
    };

    /// <summary>A JSON number as Python's <c>json.loads</c> reads it: an int with no size limit, or a float.</summary>
    internal static object DecodeNumber(JsonElement value)
    {
        var text = value.GetRawText();
        if (!JudgeJson.IsIntegerText(text))
        {
            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        return long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var small)
            ? small
            : BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <c>name in obj</c>, with the last value of a repeated key, as <c>json.loads</c> keeps it. <c>TryGetProperty</c>
    /// cannot be used: it throws when another key of the object holds a lone surrogate escape.
    /// </summary>
    protected static bool Find(JsonElement obj, string name, out JsonElement value)
    {
        var found = false;
        value = default;
        foreach (var property in obj.EnumerateObject())
        {
            if (JudgeJson.ReadName(property) == name)
            {
                value = property.Value;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// A hook for a decoder that knows more tags, for example the result tags in a test. It runs before the tags of
    /// the README. The default knows none.
    /// </summary>
    protected virtual bool TryDecodeOther(JsonElement value, out object? result)
    {
        result = null;
        return false;
    }

    /// <summary><c>{"$ref": path}</c>. The default reads the registry.</summary>
    protected virtual object? ResolveRef(string path) => Registry.ResolveRef(path);

    /// <summary><c>{"$enum": path, "value": v}</c>. The default reads the registry.</summary>
    protected virtual object? ResolveEnum(string path, object? value) => Registry.ResolveEnum(path, value);

    /// <summary><c>{"$model": path, "data": {...}}</c>. The default reads the registry.</summary>
    protected virtual object? ResolveModel(string path, OrderedDictionary<string, object?> data) => Registry.ResolveModel(path, data);

    /// <summary>A key of a <c>$dict</c> or an item of a <c>$set</c>. The default keeps the decoded value.</summary>
    protected virtual object? DecodeKey(JsonElement value) => Decode(value);

    private List<object?> DecodeList(JsonElement value)
    {
        var list = new List<object?>(value.GetArrayLength());
        foreach (var item in value.EnumerateArray())
        {
            list.Add(Decode(item));
        }

        return list;
    }

    private object? DecodeObject(JsonElement value)
    {
        if (TryDecodeOther(value, out var other))
        {
            return other;
        }

        if (Find(value, "$dt", out var dt))
        {
            // datetime.fromisoformat(value['$dt'].replace('Z', '+00:00'))
            return PyDateTime.FromIsoFormat(Text(dt, "$dt").Replace("Z", "+00:00", StringComparison.Ordinal));
        }

        if (Find(value, "$ref", out var reference))
        {
            return ResolveRef(Text(reference, "$ref"));
        }

        if (Find(value, "$enum", out var enumPath))
        {
            var member = Find(value, "value", out var raw) ? Decode(raw) : throw new KeyNotFoundException("'value'");
            return ResolveEnum(Text(enumPath, "$enum"), member);
        }

        if (Find(value, "$model", out var modelPath))
        {
            // The data is decoded first, so that an argument inside it is checked before the path.
            var data = Find(value, "data", out var rawData) ? Decode(rawData) : new OrderedDictionary<string, object?>();
            if (data is not OrderedDictionary<string, object?> keywords)
            {
                throw new InvalidCastException($"argument after ** must be a mapping, not {Py.TypeName(data)}");
            }

            return ResolveModel(Text(modelPath, "$model"), keywords);
        }

        if (Find(value, "$tuple", out var tuple))
        {
            return PyTuple.From(DecodeList(Array(tuple, "$tuple")));
        }

        if (Find(value, "$set", out var set))
        {
            var items = new PySet<object?>();
            foreach (var item in Array(set, "$set").EnumerateArray())
            {
                items.Add(DecodeKey(item));
            }

            return items;
        }

        if (Find(value, "$bytes", out var bytes))
        {
            return FromHex(Text(bytes, "$bytes"));
        }

        if (Find(value, "$float", out var number))
        {
            return Py.Float(Text(number, "$float"));
        }

        if (Find(value, "$dict", out var dict))
        {
            return DecodeDict(Array(dict, "$dict"));
        }

        foreach (var tag in FlowTags)
        {
            if (Find(value, tag, out _))
            {
                throw new JudgeNotBoundException(tag, string.Empty, $"The tag {tag} belongs to the flow layer, and the .NET flow host is not ported yet.");
            }
        }

        var plain = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            // json.loads keeps the last value of a repeated key at the position of the first.
            plain[JudgeJson.ReadName(property)] = Decode(property.Value);
        }

        return plain;
    }

    // {decode(k): decode(v) for k, v in pairs}. If every key is a string, Python holds an ordinary dict with string
    // keys, which is an OrderedDictionary<string, object?> in the value model. Otherwise it is a PyDict.
    private object DecodeDict(JsonElement pairs)
    {
        var entries = new List<(object? Key, object? Value)>();
        foreach (var pair in pairs.EnumerateArray())
        {
            if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2)
            {
                throw new ArgumentException("$dict: each pair must be a list of a key and a value");
            }

            entries.Add((DecodeKey(pair[0]), Decode(pair[1])));
        }

        if (entries.All(e => e.Key is string))
        {
            var byText = new OrderedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var (key, item) in entries)
            {
                byText[(string)key!] = item;
            }

            return byText;
        }

        var result = new PyDict();
        foreach (var (key, item) in entries)
        {
            result[key] = item;
        }

        return result;
    }

    // bytes.fromhex: two hex digits for each byte, with ASCII white space allowed before each pair.
    private static byte[] FromHex(string text)
    {
        var result = new List<byte>(text.Length / 2);
        var i = 0;
        while (true)
        {
            while (i < text.Length && text[i] is ' ' or '\t' or '\n' or '\r' or '\v' or '\f')
            {
                i++;
            }

            if (i >= text.Length)
            {
                return [.. result];
            }

            if (i + 1 >= text.Length || !char.IsAsciiHexDigit(text[i]) || !char.IsAsciiHexDigit(text[i + 1]))
            {
                throw new ArgumentException($"non-hexadecimal number found in fromhex() arg at position {(i + 1 < text.Length && char.IsAsciiHexDigit(text[i]) ? i + 1 : i)}");
            }

            result.Add(byte.Parse(text.AsSpan(i, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
            i += 2;
        }
    }

    private static string Text(JsonElement value, string tag) =>
        value.ValueKind == JsonValueKind.String
            ? JudgeJson.ReadString(value)
            : throw new InvalidCastException($"{tag}: the tag needs a text, not a JSON {value.ValueKind}");

    private static JsonElement Array(JsonElement value, string tag) =>
        value.ValueKind == JsonValueKind.Array
            ? value
            : throw new InvalidCastException($"{tag}: the tag needs a list, not a JSON {value.ValueKind}");
}
