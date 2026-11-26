namespace Graphiti.PyCompat.Tests;

using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Graphiti.PyCompat;

/// <summary>
/// The values that real Python gave for <c>json.dumps</c>, <c>json.loads</c>, and <c>ast.literal_eval</c>, read
/// from <c>python-values.pyjson.json</c>. <c>port/tools/generate_pyjson_values.py</c> writes the file and
/// explains the tagged form of each value.
/// </summary>
internal static class PyJsonValues
{
    private static readonly JsonDocument Document = JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "python-values.pyjson.json")),
        new JsonDocumentOptions { MaxDepth = 4096 });

    private static readonly Dictionary<string, Type> ErrorTypes = new()
    {
        ["ValueError"] = typeof(ArgumentException),
        ["TypeError"] = typeof(InvalidCastException),
        ["RecursionError"] = typeof(InvalidOperationException),
        ["JSONDecodeError"] = typeof(JsonException),
        ["UnicodeEncodeError"] = typeof(EncoderFallbackException),
        ["SyntaxError"] = typeof(FormatException),
    };

    private static readonly Dictionary<string, Enum> EnumMembers = new()
    {
        ["Color.red"] = Color.Red,
        ["Color.green"] = Color.Green,
        ["Color.blue"] = Color.Blue,
        ["Priority.low"] = Priority.Low,
        ["Priority.high"] = Priority.High,
        ["Priority.huge"] = Priority.Huge,
        ["Mode.fast"] = Mode.Fast,
        ["Mode.slow"] = Mode.Slow,
    };

    internal static List<JsonElement> Cases(string name) =>
        [.. Document.RootElement.GetProperty("cases").GetProperty(name).EnumerateArray()];

    internal static JsonElement PoolValue(int index) =>
        Document.RootElement.GetProperty("cases").GetProperty("dumps_pool")[index];

    /// <summary>A text of the file: a JSON string, or a list of code points for a text with a surrogate.</summary>
    internal static string Text(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : PythonValues.FromCodePoints(e);

    /// <summary>An input text: a plain text, or {"parts": [[text, n], ...]}.</summary>
    internal static string InputText(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
        {
            var sb = new StringBuilder();
            foreach (var part in e.GetProperty("parts").EnumerateArray())
            {
                var piece = Text(part[0]);
                var n = part[1].GetInt32();
                sb.EnsureCapacity(sb.Length + (piece.Length * n));
                for (var i = 0; i < n; i++)
                {
                    sb.Append(piece);
                }
            }

            return sb.ToString();
        }

        return Text(e);
    }

    /// <summary>Asserts that <paramref name="call"/> throws the C# type for a Python error, with Python's message.</summary>
    internal static void AssertError(JsonElement c, Action call)
    {
        var name = c.GetProperty("e").GetString() ?? string.Empty;
        var expected = ErrorTypes[name];
        var ex = Record.Exception(call);
        Assert.True(ex is not null && ex.GetType() == expected, $"expected {expected.Name} for {Describe(c)}, got {ex?.GetType().Name ?? "no exception"}: {ex?.Message}");
        if (c.TryGetProperty("msg", out var msg))
        {
            Assert.Equal(Text(msg), ex?.Message);
        }
    }

    internal static string Describe(JsonElement c)
    {
        var text = c.ToString();
        return text.Length > 300 ? text[..300] + "..." : text;
    }

    /// <summary>Asserts the text result of a case: "r", or "rlen" and "rsha" for a long text.</summary>
    internal static void AssertText(JsonElement c, string actual)
    {
        if (c.TryGetProperty("rsha", out var sha))
        {
            Assert.Equal(c.GetProperty("rlen").GetInt32(), PyText.ToCodePoints(actual).Length);
            Assert.Equal(sha.GetString(), Convert.ToHexStringLower(SHA256.HashData(SurrogatePassBytes(actual))));
        }
        else
        {
            Assert.Equal(Text(c.GetProperty("r")), actual);
        }
    }

    /// <summary><c>s.encode('utf-8', 'surrogatepass')</c>: a lone surrogate is written as three bytes.</summary>
    internal static byte[] SurrogatePassBytes(string s)
    {
        var bytes = new List<byte>(s.Length);
        foreach (var cp in PyText.ToCodePoints(s))
        {
            if (cp is >= 0xD800 and <= 0xDFFF)
            {
                bytes.Add((byte)(0xE0 | (cp >> 12)));
                bytes.Add((byte)(0x80 | ((cp >> 6) & 0x3F)));
                bytes.Add((byte)(0x80 | (cp & 0x3F)));
            }
            else
            {
                bytes.AddRange(PyText.EncodeUtf8(PyText.FromCodePoints([cp])));
            }
        }

        return [.. bytes];
    }

    /// <summary>Python equality that also tells apart 1, 1.0, and True, and -0.0 from 0.0. A nan equals a nan.</summary>
    internal static bool StrictEqual(object? a, object? b)
    {
        switch (a, b)
        {
            case (null, null):
                return true;
            case (bool x, bool y):
                return x == y;
            case (long x, long y):
                return x == y;
            case (BigInteger x, BigInteger y):
                return x == y;
            case (double x, double y):
                return double.IsNaN(x) ? double.IsNaN(y) : BitConverter.DoubleToInt64Bits(x) == BitConverter.DoubleToInt64Bits(y);
            case (string x, string y):
                return string.Equals(x, y, StringComparison.Ordinal);
            case (byte[] x, byte[] y):
                return x.AsSpan().SequenceEqual(y);
            case (List<object?> x, List<object?> y):
                return x.Count == y.Count && x.Zip(y).All(p => StrictEqual(p.First, p.Second));
            case (PyTuple x, PyTuple y):
                return x.Count == y.Count && x.Zip(y).All(p => StrictEqual(p.First, p.Second));
            case (OrderedDictionary<string, object?> x, OrderedDictionary<string, object?> y):
                return x.Count == y.Count && x.Zip(y).All(p => p.First.Key == p.Second.Key && StrictEqual(p.First.Value, p.Second.Value));
            case (PyDict x, PyDict y):
                return x.Count == y.Count && x.Zip(y).All(p => StrictEqual(p.First.Key, p.Second.Key) && StrictEqual(p.First.Value, p.Second.Value));
            case (IPySet x, IPySet y):
                return x.Count == y.Count && x.Frozen == y.Frozen && x.Items.All(i => y.Items.Any(j => StrictEqual(i, j)));
            default:
                return false;
        }
    }

    /// <summary>The default function that the generator calls <paramref name="name"/>.</summary>
    internal static Func<object?, object?>? DefaultFor(string? name) => name switch
    {
        null => null,
        "str" => Py.Str,
        "repr" => Py.Repr,
        "isoformat" => o => o switch
        {
            DateOnly d => string.Create(CultureInfo.InvariantCulture, $"{d.Year:D4}-{d.Month:D2}-{d.Day:D2}"),
            PyDateTime dt => dt.IsoFormat(),
            _ => throw NotSerializable(o),
        },
        "value" => o => o is Enum e ? PyEnum.Value(e) : throw NotSerializable(o),
        "tolist" => o => o switch
        {
            IPySet s => s.Items.ToList(),
            byte[] b => b.Select(x => (object?)(long)x).ToList(),
            _ => throw NotSerializable(o),
        },
        "wrap" => o => new OrderedDictionary<string, object?> { ["repr"] = Py.Repr(o) },
        "self" => o => o,
        "boom" => _ => throw new ArgumentException("boom"),
        "none" => _ => null,
        "chain" => o => o is ChainNode c ? c.Next : throw NotSerializable(o),
        "listchain" => o => o is ChainNode c ? new List<object?> { c.Next } : throw NotSerializable(o),
        "pair" => o => new PyTuple(Py.Repr(o), 1L),
        _ => throw new InvalidOperationException($"unknown default {name}"),
    };

    private static InvalidCastException NotSerializable(object? o) =>
        new($"Object of type {o switch { PyDateTime => "datetime", DateOnly => "date", _ => Py.TypeName(o) }} is not JSON serializable");

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
                var n = ValueModelValues.Hex(i.GetString() ?? string.Empty);
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

            if (e.TryGetProperty("srep", out var srep))
            {
                var unit = Text(srep[0]);
                var count = srep[1].GetInt32();
                return string.Concat(Enumerable.Repeat(unit, count));
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
                return new PySet<object?>(set.EnumerateArray().Select(Decode).ToList()) { Frozen = e.GetProperty("frozen").GetBoolean() };
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

            if (e.TryGetProperty("strenum", out var strMember))
            {
                return EnumMembers[strMember.GetString() ?? string.Empty];
            }

            if (e.TryGetProperty("deep", out var deep))
            {
                return Deep(deep[0].GetString() ?? string.Empty, deep[1].GetInt32(), Decode(deep[2]));
            }

            if (e.TryGetProperty("chain", out var chain))
            {
                object? x = Decode(chain[1]);
                for (var k = 0; k < chain[0].GetInt32(); k++)
                {
                    x = new ChainNode(x);
                }

                return Keep(x);
            }

            throw new InvalidOperationException($"unknown tagged value {e}");
        }

        /// <summary>n lists ("l"), tuples ("t"), or dicts with the key "a" ("d"), one inside the other, around the leaf.</summary>
        internal static object? Deep(string kind, int n, object? leaf)
        {
            var x = leaf;
            for (var k = 0; k < n; k++)
            {
                x = kind switch
                {
                    "l" => new List<object?> { x },
                    "t" => new PyTuple(x),
                    _ => new OrderedDictionary<string, object?> { ["a"] = x },
                };
            }

            return x;
        }
    }
}

/// <summary>An object that JSON cannot hold, with the next one inside. The test default function unwraps it.</summary>
internal sealed class ChainNode(object? next)
{
    internal object? Next => next;
}

/// <summary>An enum for a Python class <c>Mode(str, Enum)</c>: JSON writes a member as its text.</summary>
[PyStrEnum]
internal enum Mode
{
    [PyEnumMember("fast", "fast")]
    Fast,
    [PyEnumMember("slow", "sé")]
    Slow,
}
