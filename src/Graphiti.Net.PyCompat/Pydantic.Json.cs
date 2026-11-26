namespace Graphiti.PyCompat;

using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

/// <summary>
/// pydantic's JSON text: <c>model_dump_json()</c> and <c>model_validate_json()</c> (RULES.md R7). pydantic writes and
/// reads JSON with its own Rust code, which differs from Python's <c>json</c> module.
/// </summary>
internal static partial class Pydantic
{
    /// <summary>
    /// <c>m.model_dump_json()</c> of <c>m.ModelDump(json: true)</c>, or of any value of the value model, which passes
    /// through <see cref="JsonValue(object?)"/> first. The text has no spaces and keeps non-ASCII text as it is. A float
    /// is written in Rust's shortest form (<c>1e16</c>, <c>0.00001</c>, <c>1e-6</c>, <c>1.0</c>), and <c>nan</c> and
    /// <c>inf</c> as <c>null</c>. Only <c>"</c>, <c>\</c>, and the characters below U+0020 are escaped. A lone surrogate
    /// throws <see cref="EncoderFallbackException"/> (pydantic's <c>PydanticSerializationError</c> about a
    /// <c>UnicodeEncodeError</c>). <c>bytes</c> that are not UTF-8 throw <see cref="DecoderFallbackException"/>.
    /// </summary>
    /// <remarks>
    /// A dict whose keys become one text in JSON mode (<c>None</c> and <c>'None'</c>, or <c>nan</c> and <c>inf</c>)
    /// keeps one entry here, because <see cref="JsonValue(object?)"/> builds a dict. pydantic writes the key twice. No
    /// upstream model reaches it: a <c>dict[str, Any]</c> field has text keys.
    /// </remarks>
    internal static string DumpJson(object? value)
    {
        var sb = new StringBuilder();
        WriteJson(sb, JsonValue(value));
        return sb.ToString();
    }

    /// <summary>
    /// The JSON parser of <c>M.model_validate_json(text)</c>: <c>M.ModelValidate(Pydantic.LoadJson(text))</c>. It
    /// accepts what <c>json.loads</c> accepts, also <c>NaN</c> and <c>Infinity</c>, and it rejects a <c>\u</c> escape
    /// of a lone surrogate, which <c>json.loads</c> accepts. Text that is not JSON gives the <c>json_invalid</c> error,
    /// and JSON that is not an object gives <c>model_type</c>. The message of <c>json_invalid</c> holds the message of
    /// <c>json.loads</c>, not pydantic's. No upstream code reads it.
    /// </summary>
    internal static OrderedDictionary<string, object?> LoadJson(string text)
    {
        CheckJsonDepth(text);
        object? value;
        try
        {
            value = PyJson.Loads(text);
        }
        catch (Exception e) when (e is JsonException or ArgumentException)
        {
            throw Error("json_invalid", "Invalid JSON: " + e.Message);
        }

        if (HasLoneSurrogate(value))
        {
            throw Error("json_invalid", "Invalid JSON: lone surrogate in hex escape");
        }

        return value as OrderedDictionary<string, object?> ?? throw Error("model_type", "Input should be an object");
    }

    /// <summary>
    /// <see cref="LoadJson(string)"/> of UTF-8 <c>bytes</c>, as <c>ChangeSet.from_bytes</c> passes them
    /// (<c>ingestion/change_set.py:129</c>). Bytes that are not UTF-8 give the <c>json_invalid</c> error.
    /// </summary>
    internal static OrderedDictionary<string, object?> LoadJson(byte[] utf8) =>
        LoadJson(DecodeStrict(utf8) ?? throw Error("json_invalid", "Invalid JSON: invalid unicode code point"));

    /// <summary>
    /// pydantic's JSON parser accepts 201 nested lists and objects (the outer object and 200 levels in it) and rejects
    /// 202 with <c>json_invalid</c>, <c>recursion limit exceeded</c>. The check runs before the parse, so the deep text
    /// never reaches a recursive walk.
    /// </summary>
    private static void CheckJsonDepth(string text)
    {
        const int MaxDepth = 201;
        var depth = 0;
        var inString = false;
        var line = 1;
        var column = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            column = c == '\n' ? 0 : column + 1;
            line += c == '\n' ? 1 : 0;
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                    column++;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '[' or '{' when ++depth > MaxDepth:
                    throw Error("json_invalid", $"Invalid JSON: recursion limit exceeded at line {line} column {column}");
                case ']' or '}':
                    depth--;
                    break;
            }
        }
    }

    private static bool HasLoneSurrogate(object? value) => value switch
    {
        string s => s != PyText.DropLoneSurrogates(s),
        OrderedDictionary<string, object?> d => d.Any(kv => HasLoneSurrogate(kv.Key) || HasLoneSurrogate(kv.Value)),
        List<object?> l => l.Any(HasLoneSurrogate),
        _ => false,
    };

    private static void WriteJson(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                break;
            case bool b:
                sb.Append(b ? "true" : "false");
                break;
            case long or BigInteger:
                sb.Append(Py.Str(value));
                break;
            case double d:
                // JsonValue turned nan and inf into null.
                sb.Append(RustFloat(d));
                break;
            case string s:
                WriteJsonString(sb, s);
                break;
            case OrderedDictionary<string, object?> dict:
                sb.Append('{');
                var first = true;
                foreach (var (key, item) in dict)
                {
                    sb.Append(first ? string.Empty : ",");
                    first = false;
                    WriteJsonString(sb, key);
                    sb.Append(':');
                    WriteJson(sb, item);
                }

                sb.Append('}');
                break;
            case List<object?> list:
                sb.Append('[');
                for (var i = 0; i < list.Count; i++)
                {
                    sb.Append(i == 0 ? string.Empty : ",");
                    WriteJson(sb, list[i]);
                }

                sb.Append(']');
                break;
            default:
                throw new InvalidOperationException($"Pydantic.DumpJson: {value.GetType().FullName} is not a JSON value");
        }
    }

    private static void WriteJsonString(StringBuilder sb, string s)
    {
        var codePoint = 0;
        for (var i = 0; i < s.Length; i++, codePoint++)
        {
            if (char.IsSurrogate(s[i]) && !(char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])))
            {
                throw new EncoderFallbackException(
                    $"Error serializing to JSON: UnicodeEncodeError: 'utf-8' codec can't encode character '\\u{(int)s[i]:x4}' in position {codePoint}: surrogates not allowed");
            }

            if (char.IsHighSurrogate(s[i]))
            {
                i++;
            }
        }

        sb.Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '\b':
                    sb.Append("\\b");
                    break;
                case '\f':
                    sb.Append("\\f");
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
                case < ' ':
                    sb.Append(CultureInfo.InvariantCulture, $"\\u{(int)c:x4}");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        sb.Append('"');
    }

    /// <summary>
    /// The text of a finite float in Rust's <c>ryu</c> form, which pydantic writes: the shortest digits (the same as
    /// Python's <c>repr</c>), in fixed notation if the decimal point falls within 16 places of the first digit or the
    /// value is above 1e-5, else <c>de-6</c> or <c>d.ddde21</c>.
    /// </summary>
    private static string RustFloat(double d)
    {
        var repr = Py.FloatRepr(Math.Abs(d));
        var sign = double.IsNegative(d) ? "-" : string.Empty;
        var e = repr.IndexOf('e', StringComparison.Ordinal);
        var mantissa = e < 0 ? repr : repr[..e];
        var exponent = e < 0 ? 0 : int.Parse(repr[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var dot = mantissa.IndexOf('.', StringComparison.Ordinal);
        var digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
        var point = (dot < 0 ? mantissa.Length : dot) + exponent;
        var trimmed = digits.TrimStart('0');
        point -= digits.Length - trimmed.Length;
        digits = trimmed.TrimEnd('0');
        if (digits.Length == 0)
        {
            return sign + "0.0";
        }

        var n = digits.Length;
        var k = point - n;
        if (k >= 0 && point <= 16)
        {
            return sign + digits + new string('0', k) + ".0";
        }

        if (point > 0 && point <= 16)
        {
            return sign + digits[..point] + "." + digits[point..];
        }

        if (point > -5 && point <= 0)
        {
            return sign + "0." + new string('0', -point) + digits;
        }

        var exp = (point - 1).ToString(CultureInfo.InvariantCulture);
        return sign + (n == 1 ? digits : digits[0] + "." + digits[1..]) + "e" + exp;
    }
}
