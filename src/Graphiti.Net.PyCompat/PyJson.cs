namespace Graphiti.PyCompat;

using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

/// <summary>
/// <c>json.dumps</c> and <c>json.loads</c> of Python 3.12 (RULES.md R11, GAPS G2, G5, F3). The text that
/// <see cref="Dumps"/> writes reaches model prompts and the cache key, so it follows Python character by
/// character. Both directions work with an explicit stack and never recurse, so a document of any depth
/// cannot overflow the call stack. Python stops at a depth of 9997 containers (its C recursion limit, see
/// <see cref="RecursionLimit"/>), and the port stops at the same depth with an <see cref="InvalidOperationException"/>
/// (<c>RecursionError</c> is a <c>RuntimeError</c>, RULES.md R4).
/// </summary>
internal static class PyJson
{
    /// <summary>
    /// The nesting depth at which Python 3.12 raises <c>RecursionError</c> in the C scanner and the C encoder:
    /// 9998 containers. The call <c>json.dumps(x, indent=2)</c> recurses in Python code instead, and fails near
    /// 990 levels when it is called from the top of a script, with a limit that depends on the depth of the
    /// caller. The port does not copy that lower limit. It applies the C limit to each call.
    /// </summary>
    internal const int RecursionLimit = 9998;

    /// <summary>
    /// <c>json.dumps(value, ensure_ascii=..., indent=..., sort_keys=..., default=..., separators=..., allow_nan=...)</c>.
    /// <paramref name="compact"/> is <c>separators=(',', ':')</c>. Without it the separators are <c>, </c> and
    /// <c>: </c>, and <c>,</c> and <c>: </c> when <paramref name="indent"/> is not null. An <paramref name="indent"/>
    /// of zero or less writes a new line with no spaces. An <paramref name="indent"/> above <see cref="int.MaxValue"/>
    /// throws <see cref="OverflowException"/> for each value, where Python builds the text of one level first and
    /// raises <c>MemoryError</c>. A value outside the JSON types goes to
    /// <paramref name="default"/>, which is called as Python calls it. Without it, such a value throws
    /// <see cref="InvalidCastException"/> (<c>TypeError: Object of type X is not JSON serializable</c>). A dict key
    /// that is not text, an int, a float, a bool, or <c>None</c> throws <see cref="InvalidCastException"/> too.
    /// <c>nan</c> and <c>inf</c> with <c>allow_nan=False</c>, a circular reference, and an int of more than 4300
    /// digits throw <see cref="ArgumentException"/> (<c>ValueError</c>).
    /// </summary>
    /// <param name="value">A value of the model of RULES.md R6, or any object if <paramref name="default"/> handles it.</param>
    /// <param name="ensureAscii">Escape each character above U+007E.</param>
    /// <param name="indent">The number of spaces for a level, or null for a single line.</param>
    /// <param name="sortKeys">Sort the keys of each dict by Python <c>&lt;</c>, as <c>sorted(d.items())</c> does.</param>
    /// <param name="default">The <c>default</c> function.</param>
    /// <param name="compact">Use the separators <c>,</c> and <c>:</c>.</param>
    /// <param name="allowNan">Write <c>NaN</c>, <c>Infinity</c>, and <c>-Infinity</c>. Without it they throw.</param>
    /// <returns>The JSON text.</returns>
    internal static string Dumps(
        object? value,
        bool ensureAscii = true,
        long? indent = null,
        bool sortKeys = false,
        Func<object?, object?>? @default = null,
        bool compact = false,
        bool allowNan = true)
    {
        if (indent > int.MaxValue)
        {
            throw new OverflowException("An indent of more than 2147483647 spaces is over the limit of the port.");
        }

        var itemSeparator = compact || indent is not null ? "," : ", ";
        var keySeparator = compact ? ":" : ": ";
        return new Encoder(ensureAscii, indent, sortKeys, @default, itemSeparator, keySeparator, allowNan).Encode(value);
    }

    /// <summary>
    /// <c>json.loads(text)</c> into the value model of RULES.md R6: <see cref="OrderedDictionary{TKey, TValue}"/>
    /// of string and object, <see cref="List{T}"/> of object, <c>long</c> (<see cref="BigInteger"/> beyond 64 bits),
    /// <c>double</c>, <c>string</c>, <c>bool</c>, and null. A duplicate key keeps its first position and takes the last
    /// value. <c>NaN</c>, <c>Infinity</c>, and <c>-Infinity</c> are numbers, and a number that is too large for a double
    /// is <c>inf</c>. A text that is not JSON throws <see cref="JsonException"/> with the message of
    /// <c>JSONDecodeError</c>, for example <c>Expecting value: line 1 column 1 (char 0)</c>. Its position counts code
    /// points. An integer of more than 4300 digits throws <see cref="ArgumentException"/> (a plain <c>ValueError</c>,
    /// not a decode error). A nesting of 9998 containers throws <see cref="InvalidOperationException"/>
    /// (<c>RecursionError</c>). One difference: with 9995 to 9997 containers open and an error after them, Python raises
    /// <c>RecursionError</c> and the port raises <see cref="JsonException"/>.
    /// </summary>
    /// <param name="text">The JSON text.</param>
    /// <returns>The value.</returns>
    internal static object? Loads(string text)
    {
        if (text.StartsWith('\uFEFF'))
        {
            throw DecodeError("Unexpected UTF-8 BOM (decode using utf-8-sig)", text, 0);
        }

        return new Parser(text).Parse();
    }

    // ---- shared ----

    private static JsonException DecodeError(string message, string doc, int position)
    {
        // JSONDecodeError: the position, the line, and the column count code points.
        var codePoints = 0;
        var line = 1;
        var lastNewline = -1;
        for (var i = 0; i < position && i < doc.Length; i++)
        {
            var c = doc[i];
            if (char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(doc[i - 1]))
            {
                continue;
            }

            if (c == '\n')
            {
                line++;
                lastNewline = codePoints;
            }

            codePoints++;
        }

        var column = lastNewline >= 0 ? codePoints - lastNewline : codePoints + 1;
        return new JsonException(string.Create(CultureInfo.InvariantCulture, $"{message}: line {line} column {column} (char {codePoints})"));
    }

    private static InvalidOperationException Recursion(string where) =>
        new("maximum recursion depth exceeded " + where);

    // ---- json.dumps ----

    private enum FrameKind
    {
        List,
        Dict,
        Default,
    }

    private sealed class Frame(FrameKind kind, object container)
    {
        internal FrameKind Kind { get; } = kind;

        internal object Container { get; } = container;

        internal object?[] Items { get; init; } = [];

        internal List<(object? Key, object? Value)> Entries { get; init; } = [];

        internal int Index { get; set; }

        internal int Count => Kind == FrameKind.List ? Items.Length : Entries.Count;
    }

    private sealed class Encoder(
        bool ensureAscii,
        long? indent,
        bool sortKeys,
        Func<object?, object?>? @default,
        string itemSeparator,
        string keySeparator,
        bool allowNan)
    {
        private readonly StringBuilder sb = new();
        private readonly Stack<Frame> frames = new();
        private readonly HashSet<object> active = new(ReferenceEqualityComparer.Instance);
        private int level;

        internal string Encode(object? root)
        {
            Begin(root);
            while (frames.Count > 0)
            {
                var frame = frames.Peek();
                if (frame.Kind == FrameKind.Default)
                {
                    frames.Pop();
                    active.Remove(frame.Container);
                    continue;
                }

                if (frame.Index == frame.Count)
                {
                    CloseContainer(frame);
                    continue;
                }

                if (frame.Index > 0)
                {
                    sb.Append(itemSeparator);
                }

                NewlineIndent(level);
                if (frame.Kind == FrameKind.List)
                {
                    Begin(frame.Items[frame.Index++]);
                }
                else
                {
                    var (key, value) = frame.Entries[frame.Index++];
                    WriteString(KeyText(key));
                    sb.Append(keySeparator);
                    Begin(value);
                }
            }

            return sb.ToString();
        }

        private void CloseContainer(Frame frame)
        {
            frames.Pop();
            active.Remove(frame.Container);
            level--;
            NewlineIndent(level);
            sb.Append(frame.Kind == FrameKind.List ? ']' : '}');
        }

        private void NewlineIndent(int depth)
        {
            if (indent is { } spaces)
            {
                // An indent of 0 or less writes the line breaks and no spaces, as Python does.
                sb.Append('\n').Append(' ', checked((int)(Math.Max(spaces, 0L) * depth)));
            }
        }

        private void Begin(object? start)
        {
            // A value that default replaces is written in the same loop, so a chain of replacements does not recurse.
            var value = start;
            while (true)
            {
                switch (value)
                {
                    case null:
                        sb.Append("null");
                        return;
                    case bool b:
                        sb.Append(b ? "true" : "false");
                        return;
                    case string s:
                        WriteString(s);
                        return;
                    case long l:
                        sb.Append(l.ToString(CultureInfo.InvariantCulture));
                        return;
                    case BigInteger big:
                        sb.Append(Py.IntText(big));
                        return;
                    case double d:
                        sb.Append(FloatText(d));
                        return;
                    case byte[]:
                        break;
                    case PyTuple tuple:
                        BeginList(tuple, tuple);
                        return;
                    case PyDict or IDictionary:
                        BeginDict(value);
                        return;
                    case IList list:
                        BeginList(list, list.Cast<object?>());
                        return;
                }

                value = Replace(value);
            }
        }

        private void BeginList(object container, IEnumerable<object?> items)
        {
            if (frames.Count + 1 >= RecursionLimit)
            {
                throw Recursion("while encoding a JSON object");
            }

            var snapshot = items.ToArray();
            if (snapshot.Length == 0)
            {
                sb.Append("[]");
                return;
            }

            Push(new Frame(FrameKind.List, container) { Items = snapshot }, '[');
        }

        private void BeginDict(object dict)
        {
            if (frames.Count + 1 >= RecursionLimit)
            {
                throw Recursion("while encoding a JSON object");
            }

            var entries = Py.DictEntries(dict).ToList();
            if (entries.Count == 0)
            {
                sb.Append("{}");
                return;
            }

            if (sortKeys)
            {
                SortEntries(entries);
            }

            Push(new Frame(FrameKind.Dict, dict) { Entries = entries }, '{');
        }

        private void Push(Frame frame, char open)
        {
            if (!active.Add(frame.Container))
            {
                throw new ArgumentException("Circular reference detected");
            }

            sb.Append(open);
            frames.Push(frame);
            level++;
        }

        /// <summary>Calls <c>default</c> for a value that is not a JSON type, and keeps the value marked until its replacement is written.</summary>
        private object? Replace(object value)
        {
            if (@default is null)
            {
                throw new InvalidCastException($"Object of type {ClassName(value)} is not JSON serializable");
            }

            // CPython calls the default function first, and the call of a Python function costs two levels of
            // its C stack. The error then has no "while" part.
            if (frames.Count + 2 >= RecursionLimit)
            {
                throw new InvalidOperationException("maximum recursion depth exceeded");
            }

            if (!active.Add(value))
            {
                throw new ArgumentException("Circular reference detected");
            }

            var replacement = @default(value);
            if (frames.Count + 1 >= RecursionLimit)
            {
                throw Recursion("while encoding a JSON object");
            }

            frames.Push(new Frame(FrameKind.Default, value));
            return replacement;
        }

        private static void SortEntries(List<(object? Key, object? Value)> entries)
        {
            if (entries.All(e => e.Key is string))
            {
                // The keys of a dict are distinct, so an unstable sort gives the one order.
                entries.Sort((a, b) => Py.CompareStrings((string)(a.Key ?? string.Empty), (string)(b.Key ?? string.Empty)));
                return;
            }

            // The sort of CPython, with its comparisons in its order, so a nan key and the pair of an error are Python's.
            Py.SortInPlace(entries, e => e.Key);
        }

        private string KeyText(object? key) => key switch
        {
            string s => s,
            double d => FloatText(d),
            bool b => b ? "true" : "false",
            null => "null",
            long l => l.ToString(CultureInfo.InvariantCulture),
            BigInteger big => Py.IntText(big),
            _ => throw new InvalidCastException(
                $"keys must be str, int, float, bool or None, not {(indent is null ? Py.TypeName(key) : ClassName(key))}"),
        };

        private string FloatText(double d)
        {
            if (double.IsFinite(d))
            {
                return Py.FloatRepr(d);
            }

            if (!allowNan)
            {
                throw new ArgumentException("Out of range float values are not JSON compliant: " + Py.FloatRepr(d));
            }

            return double.IsNaN(d) ? "NaN" : d > 0 ? "Infinity" : "-Infinity";
        }

        private void WriteString(string s)
        {
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
                    case < ' ':
                        AppendEscape(c);
                        break;
                    case > '~' when ensureAscii:
                        // A character above U+FFFF is a surrogate pair in a .NET string, and Python writes
                        // the two surrogates of that pair. A lone surrogate is written as it is.
                        AppendEscape(c);
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            sb.Append('"');
        }

        private void AppendEscape(char c) =>
            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));

        /// <summary>The <c>__name__</c> of the Python class of a value, as the message of <c>default</c> shows it.</summary>
        private static string ClassName(object value) => value switch
        {
            PyDateTime => "datetime",
            DateOnly => "date",
            _ => Py.TypeName(value),
        };
    }

    // ---- json.loads ----

    private sealed class Open(bool isDict)
    {
        internal bool IsDict { get; } = isDict;

        internal List<object?> List { get; } = [];

        internal OrderedDictionary<string, object?> Dict { get; } = [];

        internal string Key { get; set; } = string.Empty;
    }

    private sealed class Parser(string s)
    {
        private const int MaxIntDigits = 4300;

        private readonly Stack<Open> stack = new();
        private int idx;

        internal object? Parse()
        {
            SkipWhitespace();
            var value = ParseValue();
            SkipWhitespace();
            if (idx != s.Length)
            {
                throw DecodeError("Extra data", s, idx);
            }

            return value;
        }

        private static bool IsDigit(char c) => c is >= '0' and <= '9';

        /// <summary>
        /// The error of <c>raise_errmsg</c>. CPython builds its exception by calling a class, which fails when the
        /// scanner is already 9997 containers deep, so it raises <c>RecursionError</c> instead.
        /// </summary>
        private Exception Fail(string message, int position) => stack.Count + 1 >= RecursionLimit
            ? Recursion("while calling a Python object")
            : DecodeError(message, s, position);

        private object? ParseValue()
        {
            while (true)
            {
                var value = ParseTerm(out var opened);
                if (opened)
                {
                    continue;
                }

                // A value is complete: add it to its container, and close each container that ends here.
                while (true)
                {
                    if (stack.Count == 0)
                    {
                        return value;
                    }

                    var top = stack.Peek();
                    if (top.IsDict)
                    {
                        top.Dict[top.Key] = value;
                    }
                    else
                    {
                        top.List.Add(value);
                    }

                    SkipWhitespace();
                    var close = top.IsDict ? '}' : ']';
                    if (idx < s.Length && s[idx] == close)
                    {
                        idx++;
                        stack.Pop();
                        value = top.IsDict ? top.Dict : top.List;
                        continue;
                    }

                    if (idx >= s.Length || s[idx] != ',')
                    {
                        throw Fail("Expecting ',' delimiter", idx);
                    }

                    idx++;
                    SkipWhitespace();
                    if (top.IsDict)
                    {
                        ReadKey(top);
                    }

                    break;
                }
            }
        }

        /// <summary>Reads one term. A container with items is only opened: <paramref name="opened"/> is true and the first item is next.</summary>
        private object? ParseTerm(out bool opened)
        {
            opened = false;
            if (idx >= s.Length)
            {
                throw Fail("Expecting value", idx);
            }

            var start = idx;
            switch (s[idx])
            {
                case '"':
                    return ScanString(idx + 1);
                case '{':
                    EnterContainer("while decoding a JSON object from a unicode string");
                    idx++;
                    SkipWhitespace();
                    if (idx < s.Length && s[idx] == '}')
                    {
                        idx++;
                        return new OrderedDictionary<string, object?>();
                    }

                    var dict = new Open(true);
                    stack.Push(dict);
                    ReadKey(dict);
                    opened = true;
                    return null;
                case '[':
                    EnterContainer("while decoding a JSON array from a unicode string");
                    idx++;
                    SkipWhitespace();
                    if (idx < s.Length && s[idx] == ']')
                    {
                        idx++;
                        return new List<object?>();
                    }

                    stack.Push(new Open(false));
                    opened = true;
                    return null;
                case 'n' when Matches("null"):
                    idx += 4;
                    return null;
                case 't' when Matches("true"):
                    idx += 4;
                    return true;
                case 'f' when Matches("false"):
                    idx += 5;
                    return false;
                case 'N' when Matches("NaN"):
                    idx += 3;
                    return BitConverter.Int64BitsToDouble(0x7FF8000000000000);
                case 'I' when Matches("Infinity"):
                    idx += 8;
                    return double.PositiveInfinity;
                case '-' when Matches("-Infinity"):
                    idx += 9;
                    return double.NegativeInfinity;
            }

            return ParseNumber(start);
        }

        private bool Matches(string literal) =>
            string.CompareOrdinal(s, idx, literal, 0, literal.Length) == 0;

        private void EnterContainer(string where)
        {
            if (stack.Count + 1 >= RecursionLimit)
            {
                throw Recursion(where);
            }
        }

        private void SkipWhitespace()
        {
            while (idx < s.Length && s[idx] is ' ' or '\t' or '\n' or '\r')
            {
                idx++;
            }
        }

        private void ReadKey(Open dict)
        {
            if (idx >= s.Length || s[idx] != '"')
            {
                throw Fail("Expecting property name enclosed in double quotes", idx);
            }

            dict.Key = ScanString(idx + 1);
            SkipWhitespace();
            if (idx >= s.Length || s[idx] != ':')
            {
                throw Fail("Expecting ':' delimiter", idx);
            }

            idx++;
            SkipWhitespace();
        }

        private object ParseNumber(int start)
        {
            var i = start;
            var end = s.Length - 1;
            if (s[i] == '-')
            {
                i++;
                if (i > end)
                {
                    throw Fail("Expecting value", start);
                }
            }

            if (s[i] is >= '1' and <= '9')
            {
                i++;
                while (i <= end && IsDigit(s[i]))
                {
                    i++;
                }
            }
            else if (s[i] == '0')
            {
                i++;
            }
            else
            {
                throw Fail("Expecting value", start);
            }

            var isFloat = false;
            if (i < end && s[i] == '.' && IsDigit(s[i + 1]))
            {
                isFloat = true;
                i += 2;
                while (i <= end && IsDigit(s[i]))
                {
                    i++;
                }
            }

            if (i < end && s[i] is 'e' or 'E')
            {
                var exponentStart = i;
                i++;
                if (i < end && s[i] is '-' or '+')
                {
                    i++;
                }

                while (i <= end && IsDigit(s[i]))
                {
                    i++;
                }

                if (IsDigit(s[i - 1]))
                {
                    isFloat = true;
                }
                else
                {
                    i = exponentStart;
                }
            }

            idx = i;
            var number = s.AsSpan(start, i - start);
            if (isFloat)
            {
                // .NET parses to the nearest double and gives infinity above the range, as Python does.
                return double.Parse(number, NumberStyles.Float, CultureInfo.InvariantCulture);
            }

            if (long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var l))
            {
                return l;
            }

            var digits = number.Length - (number[0] == '-' ? 1 : 0);
            if (digits > MaxIntDigits)
            {
                throw new ArgumentException(
                    string.Create(CultureInfo.InvariantCulture, $"Exceeds the limit ({MaxIntDigits} digits) for integer string conversion: value has {digits} digits; use sys.set_int_max_str_digits() to increase the limit"));
            }

            return BigInteger.Parse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }

        /// <summary>Scans a string whose opening quote is at <c>end - 1</c>, and leaves <see cref="idx"/> after the closing quote.</summary>
        private string ScanString(int end)
        {
            var begin = end - 1;
            StringBuilder? sb = null;
            while (true)
            {
                var next = end;
                while (next < s.Length)
                {
                    var d = s[next];
                    if (d is '"' or '\\')
                    {
                        break;
                    }

                    if (d <= 0x1F)
                    {
                        throw Fail("Invalid control character at", next);
                    }

                    next++;
                }

                if (next >= s.Length)
                {
                    throw Fail("Unterminated string starting at", begin);
                }

                if (s[next] == '"')
                {
                    idx = next + 1;
                    if (sb is null)
                    {
                        return s.Substring(end, next - end);
                    }

                    return sb.Append(s, end, next - end).ToString();
                }

                sb ??= new StringBuilder();
                sb.Append(s, end, next - end);
                next++;
                if (next == s.Length)
                {
                    throw Fail("Unterminated string starting at", begin);
                }

                var c = s[next];
                if (c != 'u')
                {
                    end = next + 1;
                    switch (c)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            sb.Append(c);
                            break;
                        case 'b':
                            sb.Append('\b');
                            break;
                        case 'f':
                            sb.Append('\f');
                            break;
                        case 'n':
                            sb.Append('\n');
                            break;
                        case 'r':
                            sb.Append('\r');
                            break;
                        case 't':
                            sb.Append('\t');
                            break;
                        default:
                            throw Fail("Invalid \\escape", end - 2);
                    }

                    continue;
                }

                next++;
                end = next + 4;
                if (end >= s.Length)
                {
                    throw Fail("Invalid \\uXXXX escape", next - 1);
                }

                // A high surrogate escape and a low surrogate escape in a row are one character in Python and two
                // UTF-16 units in a .NET string, so the loop needs no special case for a pair.
                sb.Append((char)Hex4(next, end - 5));
            }
        }

        private int Hex4(int from, int errorPosition)
        {
            var value = 0;
            for (var i = from; i < from + 4; i++)
            {
                var digit = s[i];
                value <<= 4;
                if (digit is >= '0' and <= '9')
                {
                    value |= digit - '0';
                }
                else if (digit is >= 'a' and <= 'f')
                {
                    value |= digit - 'a' + 10;
                }
                else if (digit is >= 'A' and <= 'F')
                {
                    value |= digit - 'A' + 10;
                }
                else
                {
                    throw Fail("Invalid \\uXXXX escape", errorPosition);
                }
            }

            return value;
        }
    }
}
