namespace Graphiti.JudgeHost.Core;

using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.PyCompat;

/// <summary>
/// JSON text as Python's <c>json.dumps</c> writes it, and JSON strings read as Python's <c>json.loads</c> reads them.
/// <c>System.Text.Json</c> refuses or replaces a lone surrogate, and Python keeps it. So this class writes and reads
/// the escapes of a string itself.
/// </summary>
internal static class JudgeJson
{
    /// <summary>
    /// <c>json.dumps(value, ensure_ascii=..., sort_keys=...)</c> with the default separators <c>", "</c> and <c>": "</c>.
    /// A whole float keeps its <c>.0</c>, and each float is written as Python's <c>repr</c> writes it.
    /// </summary>
    internal static string Dumps(JsonNode? node, bool sortKeys = false, bool ensureAscii = true)
    {
        var sb = new StringBuilder();
        Write(sb, node, sortKeys, ensureAscii);
        return sb.ToString();
    }

    /// <summary>The text of a JSON string element. A lone surrogate escape gives a lone surrogate, as in Python.</summary>
    internal static string ReadString(JsonElement element) => Unescape(element.GetRawText());

    /// <summary>The name of a JSON property. A lone surrogate escape gives a lone surrogate, as in Python.</summary>
    internal static string ReadName(JsonProperty property) =>
        Unescape(Encoding.UTF8.GetString(JsonMarshal.GetRawUtf8PropertyName(property)));

    /// <summary>
    /// Compares two texts by code point, as Python compares two <c>str</c> values. A lone surrogate counts as its
    /// own code point. Ordinal comparison of UTF-16 units differs only when a surrogate pair meets a unit above U+D7FF.
    /// </summary>
    internal static int CompareCodePoints(string? a, string? b)
    {
        if (a is null || b is null)
        {
            return a is null ? (b is null ? 0 : -1) : 1;
        }

        var i = 0;
        var j = 0;
        while (i < a.Length && j < b.Length)
        {
            var x = CodePointAt(a, ref i);
            var y = CodePointAt(b, ref j);
            if (x != y)
            {
                return x < y ? -1 : 1;
            }
        }

        return (i < a.Length ? 1 : 0) - (j < b.Length ? 1 : 0);
    }

    /// <summary>Writes a string as Python writes it, with the quotes.</summary>
    internal static void WriteString(StringBuilder sb, string text, bool ensureAscii)
    {
        sb.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                case '\t':
                    sb.Append("\\t");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
                    break;
                default:
                    // With ensure_ascii, Python escapes each character outside ' ' to '~', one UTF-16 unit at a time.
                    // Without it, Python escapes only the control characters.
                    if (c < ' ' || (ensureAscii && c > '~'))
                    {
                        sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        sb.Append('"');
    }

    private static void Write(StringBuilder sb, JsonNode? node, bool sortKeys, bool ensureAscii)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject obj:
                {
                    sb.Append('{');
                    IEnumerable<KeyValuePair<string, JsonNode?>> entries = obj;
                    if (sortKeys)
                    {
                        entries = entries.OrderBy(e => e.Key, Comparer<string>.Create(CompareCodePoints));
                    }

                    var first = true;
                    foreach (var (key, value) in entries)
                    {
                        if (!first)
                        {
                            sb.Append(", ");
                        }

                        first = false;
                        WriteString(sb, key, ensureAscii);
                        sb.Append(": ");
                        Write(sb, value, sortKeys, ensureAscii);
                    }

                    sb.Append('}');
                    break;
                }

            case JsonArray array:
                {
                    sb.Append('[');
                    for (var index = 0; index < array.Count; index++)
                    {
                        if (index > 0)
                        {
                            sb.Append(", ");
                        }

                        Write(sb, array[index], sortKeys, ensureAscii);
                    }

                    sb.Append(']');
                    break;
                }

            case JsonValue value:
                WriteValue(sb, value, ensureAscii);
                break;
            default:
                throw new InvalidOperationException($"JudgeJson: unknown node type {node.GetType().Name}");
        }
    }

    private static void WriteValue(StringBuilder sb, JsonValue value, bool ensureAscii)
    {
        var raw = value.TryGetValue<JsonElement>(out var element) ? element : value.GetValue<object>();
        switch (raw)
        {
            case JsonElement e:
                WriteElement(sb, e, ensureAscii);
                break;
            case string s:
                WriteString(sb, s, ensureAscii);
                break;
            case char ch:
                WriteString(sb, ch.ToString(), ensureAscii);
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case double d:
                sb.Append(FloatText(d));
                break;
            case float f:
                sb.Append(FloatText(f));
                break;
            case long or int or short or sbyte or byte or ulong or uint or ushort or BigInteger:
                sb.Append(Convert.ToString(raw, CultureInfo.InvariantCulture));
                break;
            default:
                throw new InvalidOperationException($"JudgeJson: a JSON value of the type {raw.GetType().Name} has no Python form");
        }
    }

    private static void WriteElement(StringBuilder sb, JsonElement e, bool ensureAscii)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                WriteString(sb, ReadString(e), ensureAscii);
                break;
            case JsonValueKind.Number:
                {
                    var text = e.GetRawText();
                    sb.Append(IsIntegerText(text)
                        ? BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
                        : FloatText(double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture)));
                    break;
                }

            case JsonValueKind.True:
                sb.Append("true");
                break;
            case JsonValueKind.False:
                sb.Append("false");
                break;
            case JsonValueKind.Null:
                sb.Append("null");
                break;
            default:
                Write(sb, e.ValueKind == JsonValueKind.Object ? JsonObject.Create(e) : JsonArray.Create(e), sortKeys: false, ensureAscii);
                break;
        }
    }

    /// <summary>True for JSON number text that Python's <c>json.loads</c> reads as an int.</summary>
    internal static bool IsIntegerText(string text) => text.AsSpan().IndexOfAny('.', 'e', 'E') < 0;

    // json.dumps writes float.__repr__, and NaN, Infinity, and -Infinity for the values that JSON lacks.
    private static string FloatText(double d) =>
        double.IsNaN(d) ? "NaN" : double.IsInfinity(d) ? (d > 0 ? "Infinity" : "-Infinity") : Py.FloatRepr(d);

    private static int CodePointAt(string s, ref int index)
    {
        var c = s[index++];
        if (char.IsHighSurrogate(c) && index < s.Length && char.IsLowSurrogate(s[index]))
        {
            return char.ConvertToUtf32(c, s[index++]);
        }

        return c;
    }

    private static string Unescape(string raw)
    {
        var start = 0;
        var end = raw.Length;
        if (end >= 2 && raw[0] == '"' && raw[end - 1] == '"')
        {
            start = 1;
            end--;
        }

        var body = raw.AsSpan(start, end - start);
        if (body.IndexOf('\\') < 0)
        {
            return body.ToString();
        }

        var sb = new StringBuilder(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            var next = body[++i];
            switch (next)
            {
                case 'n':
                    sb.Append('\n');
                    break;
                case 'r':
                    sb.Append('\r');
                    break;
                case 't':
                    sb.Append('\t');
                    break;
                case 'b':
                    sb.Append('\b');
                    break;
                case 'f':
                    sb.Append('\f');
                    break;
                case 'u':
                    // One escape is one UTF-16 unit. A pair of escapes gives a surrogate pair, and a lone one stays lone.
                    sb.Append((char)int.Parse(body.Slice(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default:
                    // \" \\ and \/
                    sb.Append(next);
                    break;
            }
        }

        return sb.ToString();
    }
}
