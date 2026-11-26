namespace Graphiti.PyCompat;

using System.Globalization;
using System.Numerics;
using System.Text;

/// <summary>
/// <c>ast.literal_eval(text)</c> of Python 3.12 for a <c>str</c> (RULES.md R11, Part 2, GAPS F5). Upstream calls it
/// in two places, <c>decision_prompts.parse_list</c> and <c>gliner2_client</c>, on the text of an LLM reply and on the
/// <c>repr</c> of a list of dicts. Both catch <c>SyntaxError</c> and <c>ValueError</c> and go on. The result is a value of
/// the model of RULES.md R6: <c>string</c>, <c>long</c> (<see cref="BigInteger"/> beyond 64 bits), <c>double</c>,
/// <c>bool</c>, null, <see cref="List{T}"/> of object, <see cref="PyTuple"/>, <see cref="OrderedDictionary{TKey, TValue}"/>
/// of string and object (<see cref="PyDict"/> for a dict with other keys), <c>PySet</c>, and <c>byte[]</c>.
/// The port reads the Python expression grammar and the Python tokenizer, so it tells text that is not Python from
/// Python that is not a literal. The messages of <c>SyntaxError</c> and of the <c>ValueError</c> for a node are
/// close to Python's, not equal. No upstream site shows them.
/// </summary>
/// <remarks>
/// Differences from Python, none of which an upstream site can tell apart:
/// <list type="bullet">
/// <item>A literal that the value model cannot hold, a complex number (<c>1+2j</c>) or <c>Ellipsis</c> (<c>...</c>), throws
/// <see cref="ArgumentException"/> (<c>ValueError</c>) with a message that names it. Python returns a value. Both upstream
/// callers catch <c>ValueError</c> and take their fallback path, and a different exception would stop the call.</item>
/// <item>A <c>\N{name}</c> escape resolves only the names in <see cref="PyUnicodeNames"/>, a generated table of a few hundred
/// common characters. A name that is valid in Python and not in the table throws <see cref="FormatException"/>, as an
/// unknown name does in Python. A malformed escape throws <see cref="FormatException"/> in both.</item>
/// <item>An identifier is checked with the Unicode tables of .NET and no NFKC step, so a few texts give <see cref="ArgumentException"/>
/// where Python gives <see cref="FormatException"/>, or the other way round. Both upstream callers catch both.</item>
/// <item>The inside of an f-string is checked for its braces only.</item>
/// <item>Python raises <c>MemoryError</c> or <c>RecursionError</c> for a chain of about 6000 unary operators or 10000 additions, and the
/// port does not: it reads operator chains with a loop. A nesting of 1000 ternary or lambda expressions throws
/// <see cref="InvalidOperationException"/>.</item>
/// <item>A string whose escapes make a high and a low surrogate in a row is one code point in a .NET string and two in Python.</item>
/// </list>
/// </remarks>
internal static class PyAst
{
    private const int MaxBracketLevel = 200;
    private const int MaxExpressionDepth = 1000;
    private const int MaxIntDigits = 4300;

    private static readonly HashSet<string> Keywords =
    [
        "False", "None", "True", "and", "as", "assert", "async", "await", "break", "class", "continue", "def", "del", "elif",
        "else", "except", "finally", "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal", "not", "or",
        "pass", "raise", "return", "try", "while", "with", "yield",
    ];

    /// <summary>
    /// <c>ast.literal_eval(text)</c>. Text that is not a Python expression throws <see cref="FormatException"/>
    /// (<c>SyntaxError</c>). An expression that is not a literal throws <see cref="ArgumentException"/>
    /// (<c>ValueError: malformed node or string</c>). A dict key or set item that cannot be hashed throws
    /// <see cref="InvalidCastException"/> (<c>TypeError: unhashable type</c>). Text with a lone surrogate throws
    /// <see cref="EncoderFallbackException"/> (<c>UnicodeEncodeError</c>), because Python encodes the text to UTF-8 first.
    /// </summary>
    /// <param name="text">The Python source of one expression.</param>
    /// <returns>The value.</returns>
    internal static object? LiteralEval(string text)
    {
        // ast.literal_eval strips leading spaces and tabs, then the parser encodes the text to UTF-8 (the position
        // of a lone surrogate counts from the stripped text), and it reads the text with universal newlines.
        var stripped = text.TrimStart(' ', '\t');
        ThrowForLoneSurrogate(stripped);
        var source = stripped.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (source.Contains('\0', StringComparison.Ordinal))
        {
            throw new FormatException("source code string cannot contain null bytes");
        }

        var tokens = new Lexer(source).Run();
        var node = new Parser(tokens).ParseEval();
        var result = Convert(node);

        // Python returns a complex number or Ellipsis where it finds one, and it raises for a later malformed
        // node first. So the port keeps a marker and reports it when the whole text has been read.
        if (FindMarker(result) is { } marker)
        {
            throw OutsideModel(marker);
        }

        return result;
    }

    /// <summary>The name of the first literal in a result that the value model cannot hold, or null.</summary>
    private static string? FindMarker(object? value) => value switch
    {
        ComplexLiteral => "a complex number",
        EllipsisLiteral => "Ellipsis (...)",
        List<object?> list => list.Select(FindMarker).FirstOrDefault(m => m is not null),
        PyTuple tuple => tuple.Select(FindMarker).FirstOrDefault(m => m is not null),
        IPySet set => set.Items.Select(FindMarker).FirstOrDefault(m => m is not null),
        PyDict pd => pd.SelectMany(e => new[] { FindMarker(e.Key), FindMarker(e.Value) }).FirstOrDefault(m => m is not null),
        OrderedDictionary<string, object?> dict => dict.Values.Select(FindMarker).FirstOrDefault(m => m is not null),
        _ => null,
    };

    private static void ThrowForLoneSurrogate(string text)
    {
        // str.encode('utf-8') fails on a lone surrogate. The position counts code points.
        var cp = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
                cp++;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                var start = cp;
                var end = cp;
                var last = c;
                while (i + 1 < text.Length && char.IsSurrogate(text[i + 1])
                    && !(char.IsHighSurrogate(text[i + 1]) && i + 2 < text.Length && char.IsLowSurrogate(text[i + 2])))
                {
                    i++;
                    end++;
                    last = text[i];
                }

                var message = start == end
                    ? string.Create(CultureInfo.InvariantCulture, $"'utf-8' codec can't encode character '\\u{(int)last:x4}' in position {start}: surrogates not allowed")
                    : string.Create(CultureInfo.InvariantCulture, $"'utf-8' codec can't encode characters in position {start}-{end}: surrogates not allowed");
                throw new EncoderFallbackException(message);
            }

            cp++;
        }
    }

    // ---- the value of a node: ast.literal_eval's _convert ----

    private static object? Convert(Node node)
    {
        switch (node.Kind)
        {
            case NodeKind.Constant:
                return node.Value;
            case NodeKind.Tuple:
                var tuple = new List<object?>(node.Items.Count);
                foreach (var item in node.Items)
                {
                    tuple.Add(Convert(item));
                }

                return PyTuple.From(tuple);
            case NodeKind.List:
                var list = new List<object?>(node.Items.Count);
                foreach (var item in node.Items)
                {
                    list.Add(Convert(item));
                }

                return list;
            case NodeKind.Set:
                var set = new PySet<object?>();
                foreach (var item in node.Items)
                {
                    set.Add(Convert(item));
                }

                return set;
            case NodeKind.Dict:
                return ConvertDict(node);
            case NodeKind.Call when node.IsSetCall:
                return new PySet<object?>();
            case NodeKind.Binary when node.Tag is "+" or "-":
                var left = ConvertSignedNum(node.Left);
                var right = ConvertNum(node.Right);
                if (left is not (long or BigInteger or double) || right is not ComplexLiteral)
                {
                    throw Malformed(node);
                }

                return right;
            default:
                return ConvertSignedNum(node);
        }
    }

    private static object? ConvertDict(Node node)
    {
        var entries = new List<(object? Key, object? Value)>();
        foreach (var (keyNode, valueNode) in node.Pairs)
        {
            if (keyNode is null)
            {
                throw new ArgumentException("malformed node or string: None");
            }

            var key = Convert(keyNode);
            var value = Convert(valueNode);
            PyValueEquality.Hash(key);
            entries.Add((key, value));
        }

        if (entries.All(e => e.Key is string))
        {
            var dict = new OrderedDictionary<string, object?>();
            foreach (var (key, value) in entries)
            {
                dict[(string)(key ?? string.Empty)] = value;
            }

            return dict;
        }

        var pd = new PyDict();
        foreach (var (key, value) in entries)
        {
            pd[key] = value;
        }

        return pd;
    }

    private static object? ConvertSignedNum(Node? node)
    {
        if (node is { Kind: NodeKind.Unary, Tag: "+" or "-" })
        {
            var operand = ConvertNum(node.Left);
            return node.Tag == "+" ? operand : Negate(operand);
        }

        return ConvertNum(node);
    }

    private static object? ConvertNum(Node? node)
    {
        if (node is { Kind: NodeKind.Constant, Value: long or BigInteger or double or ComplexLiteral })
        {
            return node.Value;
        }

        throw Malformed(node);
    }

    private static object? Negate(object? n) => n switch
    {
        long l => l == long.MinValue ? Box(-(BigInteger)l) : Box(-l),
        BigInteger big => Normalize(-big),
        double d => -d,
        _ => n,
    };

    /// <summary>An int of Python: a <c>long</c> if it fits, a <see cref="BigInteger"/> if not.</summary>
    private static object Normalize(BigInteger n) =>
        n >= long.MinValue && n <= long.MaxValue ? Box((long)n) : Box(n);

    private static object Box<T>(T value)
        where T : notnull => value;

    private static ArgumentException Malformed(Node? node) => new(node is null
        ? "malformed node or string: None"
        : string.Create(CultureInfo.InvariantCulture, $"malformed node or string on line {node.Line}: <ast.{node.AstName}>"));

    private static ArgumentException OutsideModel(string what) =>
        new($"malformed node or string: {what} is not in the Python value model (RULES.md R6)");

    private static FormatException Syntax(string message, int line) =>
        new(string.Create(CultureInfo.InvariantCulture, $"{message} (<unknown>, line {line})"));

    // ---- nodes ----

    private enum NodeKind
    {
        Constant,
        Name,
        List,
        Tuple,
        Set,
        Dict,
        Unary,
        Binary,
        Call,
        Starred,
        Other,
    }

    /// <summary>A complex literal such as <c>2j</c>. It exists so that <c>1+2j</c> is told from <c>1+2</c>.</summary>
    private sealed class ComplexLiteral
    {
    }

    private sealed class EllipsisLiteral
    {
    }

    private sealed class Node(NodeKind kind, int line, string tag = "")
    {
        internal NodeKind Kind => kind;

        internal int Line => line;

        /// <summary>The operator of a unary or binary node, the name of a Name node, or the AST class of an Other node.</summary>
        internal string Tag => tag;

        internal object? Value { get; init; }

        internal List<Node> Items { get; } = [];

        internal List<(Node? Key, Node Value)> Pairs { get; } = [];

        internal Node? Left { get; init; }

        internal Node? Right { get; init; }

        internal bool IsSetCall { get; set; }

        internal string AstName => Kind switch
        {
            NodeKind.Constant => "Constant",
            NodeKind.Name => "Name",
            NodeKind.List => "List",
            NodeKind.Tuple => "Tuple",
            NodeKind.Set => "Set",
            NodeKind.Dict => "Dict",
            NodeKind.Unary => "UnaryOp",
            NodeKind.Binary => "BinOp",
            NodeKind.Call => "Call",
            NodeKind.Starred => "Starred",
            _ => tag,
        };
    }

    // ---- the tokenizer ----

    private enum TokenKind
    {
        Name,
        Number,
        Str,
        Op,
        Newline,
        End,
    }

    private sealed class Token(TokenKind kind, string text, int line)
    {
        internal TokenKind Kind => kind;

        internal string Text => text;

        internal int Line => line;

        internal object? Value { get; init; }

        internal bool IsBytes { get; init; }

        internal bool IsFormat { get; init; }
    }

    private sealed class Lexer(string s)
    {
        private readonly List<Token> tokens = [];
        private readonly Stack<char> brackets = new();
        private int i;
        private int line = 1;

        internal List<Token> Run()
        {
            var atLineStart = true;
            var lineHasTokens = false;
            while (true)
            {
                if (atLineStart && brackets.Count == 0)
                {
                    var col = 0;
                    var j = i;
                    while (j < s.Length && s[j] is ' ' or '\t' or '\f')
                    {
                        col = s[j] switch { ' ' => col + 1, '\t' => ((col / 8) + 1) * 8, _ => 0 };
                        j++;
                    }

                    if (j >= s.Length)
                    {
                        // A last line of white space with no newline is an indent. With a newline it is blank.
                        if (col > 0)
                        {
                            throw Syntax("unexpected indent", line);
                        }

                        i = j;
                        break;
                    }

                    if (s[j] is '\n' or '#')
                    {
                        while (j < s.Length && s[j] != '\n')
                        {
                            j++;
                        }

                        i = Math.Min(j + 1, s.Length);
                        line++;
                        continue;
                    }

                    if (col > 0)
                    {
                        throw Syntax("unexpected indent", line);
                    }

                    i = j;
                    atLineStart = false;
                }

                if (i >= s.Length)
                {
                    break;
                }

                var c = s[i];
                switch (c)
                {
                    case ' ' or '\t' or '\f':
                        i++;
                        continue;
                    case '#':
                        while (i < s.Length && s[i] != '\n')
                        {
                            i++;
                        }

                        continue;
                    case '\n':
                        i++;
                        if (brackets.Count == 0)
                        {
                            if (lineHasTokens)
                            {
                                tokens.Add(new Token(TokenKind.Newline, "\n", line));
                            }

                            atLineStart = true;
                            lineHasTokens = false;
                        }

                        line++;
                        continue;
                    case '\\':
                        if (i + 1 >= s.Length || s[i + 1] != '\n')
                        {
                            throw Syntax(i + 1 >= s.Length ? "unexpected EOF while parsing" : "unexpected character after line continuation character", line);
                        }

                        i += 2;
                        line++;
                        if (i >= s.Length)
                        {
                            throw Syntax("unexpected EOF while parsing", line);
                        }

                        continue;
                }

                lineHasTokens = true;
                LexToken(c);
            }

            if (brackets.Count > 0)
            {
                throw Syntax($"'{brackets.Peek()}' was never closed", line);
            }

            if (lineHasTokens)
            {
                tokens.Add(new Token(TokenKind.Newline, "\n", line));
            }

            tokens.Add(new Token(TokenKind.End, string.Empty, line));
            return tokens;
        }

        private static bool IsIdentifierStart(int cp) =>
            cp is '_' or (>= 'a' and <= 'z') or (>= 'A' and <= 'Z')
            || (cp >= 128 && CharUnicodeInfo.GetUnicodeCategory(cp) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or UnicodeCategory.LetterNumber);

        private static bool IsIdentifierPart(int cp) =>
            IsIdentifierStart(cp) || cp is >= '0' and <= '9'
            || (cp >= 128 && (CharUnicodeInfo.GetUnicodeCategory(cp) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark
                or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.ConnectorPunctuation || cp == 0xB7 || cp == 0x387));

        private static bool IsDigit(char c) => c is >= '0' and <= '9';

        private int CodePointAt(int index) =>
            char.IsHighSurrogate(s[index]) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1])
                ? char.ConvertToUtf32(s[index], s[index + 1])
                : s[index];

        private void LexToken(char c)
        {
            if (IsDigit(c) || (c == '.' && i + 1 < s.Length && IsDigit(s[i + 1])))
            {
                LexNumber();
                return;
            }

            if (c is '"' or '\'')
            {
                LexString(i, false, false, false, false);
                return;
            }

            var cp = CodePointAt(i);
            if (IsIdentifierStart(cp))
            {
                if (TryLexPrefixedString())
                {
                    return;
                }

                var start = i;
                i += cp > 0xFFFF ? 2 : 1;
                while (i < s.Length && IsIdentifierPart(CodePointAt(i)))
                {
                    i += CodePointAt(i) > 0xFFFF ? 2 : 1;
                }

                tokens.Add(new Token(TokenKind.Name, s[start..i], line));
                return;
            }

            LexOperator(c);
        }

        private void LexOperator(char c)
        {
            string[] three = ["**=", "//=", ">>=", "<<=", "..."];
            string[] two = ["**", "//", ">>", "<<", "<=", ">=", "==", "!=", "<>", "->", "+=", "-=", "*=", "/=", "%=", "&=", "|=", "^=", "@=", ":="];
            foreach (var op in three)
            {
                if (string.CompareOrdinal(s, i, op, 0, 3) == 0)
                {
                    Emit(op);
                    return;
                }
            }

            foreach (var op in two)
            {
                if (string.CompareOrdinal(s, i, op, 0, 2) == 0)
                {
                    Emit(op);
                    return;
                }
            }

            switch (c)
            {
                case '(' or '[' or '{':
                    if (brackets.Count >= MaxBracketLevel)
                    {
                        throw Syntax("too many nested parentheses", line);
                    }

                    brackets.Push(c);
                    Emit(c.ToString());
                    return;
                case ')' or ']' or '}':
                    if (brackets.Count == 0)
                    {
                        throw Syntax($"unmatched '{c}'", line);
                    }

                    var open = brackets.Pop();
                    if ((open == '(' && c != ')') || (open == '[' && c != ']') || (open == '{' && c != '}'))
                    {
                        throw Syntax($"closing parenthesis '{c}' does not match opening parenthesis '{open}'", line);
                    }

                    Emit(c.ToString());
                    return;
                case '+' or '-' or '*' or '/' or '%' or '@' or '&' or '|' or '^' or '~' or '<' or '>' or '=' or '.' or ',' or ':' or ';':
                    Emit(c.ToString());
                    return;
                default:
                    throw Syntax(c < 128 && c >= ' ' ? "invalid syntax" : "invalid character", line);
            }
        }

        private void Emit(string op)
        {
            tokens.Add(new Token(TokenKind.Op, op, line));
            i += op.Length;
        }

        /// <summary>A prefix of up to two letters from b, r, u, f that is followed by a quote.</summary>
        private bool TryLexPrefixedString()
        {
            bool b = false, r = false, u = false, f = false;
            var j = i;
            while (j < s.Length && j - i < 2)
            {
                var c = s[j];
                if (!(b || u || f) && c is 'b' or 'B')
                {
                    b = true;
                }
                else if (!(b || u || r || f) && c is 'u' or 'U')
                {
                    u = true;
                }
                else if (!(r || u) && c is 'r' or 'R')
                {
                    r = true;
                }
                else if (!(f || b || u) && c is 'f' or 'F')
                {
                    f = true;
                }
                else
                {
                    break;
                }

                j++;
                if (j < s.Length && s[j] is '"' or '\'')
                {
                    LexString(j, b, r, f, true);
                    return true;
                }
            }

            return false;
        }

        private void LexString(int quoteAt, bool isBytes, bool raw, bool isFormat, bool hasPrefix)
        {
            _ = hasPrefix;
            var startLine = line;
            var quote = s[quoteAt];
            var triple = quoteAt + 2 < s.Length && s[quoteAt + 1] == quote && s[quoteAt + 2] == quote;
            int contentStart, contentEnd, k;
            if (!triple && quoteAt + 1 < s.Length && s[quoteAt + 1] == quote)
            {
                contentStart = contentEnd = quoteAt + 1;
                k = quoteAt + 2;
            }
            else
            {
                k = quoteAt + (triple ? 3 : 1);
                contentStart = k;
                while (true)
                {
                    if (k >= s.Length)
                    {
                        throw Syntax(
                            triple
                                ? $"unterminated triple-quoted string literal (detected at line {line})"
                                : $"unterminated string literal (detected at line {line})",
                            startLine);
                    }

                    var ch = s[k];
                    if (ch == '\\')
                    {
                        if (k + 1 < s.Length && s[k + 1] == '\n')
                        {
                            line++;
                        }

                        k += 2;
                        continue;
                    }

                    if (ch == quote && (!triple || (k + 2 < s.Length && s[k + 1] == quote && s[k + 2] == quote)))
                    {
                        break;
                    }

                    if (ch == '\n')
                    {
                        if (!triple)
                        {
                            throw Syntax($"unterminated string literal (detected at line {line})", startLine);
                        }

                        line++;
                    }

                    k++;
                }

                contentEnd = k;
                k += triple ? 3 : 1;
            }

            var content = s[contentStart..contentEnd];
            object value;
            if (isBytes)
            {
                value = DecodeBytes(content, raw, line);
            }
            else if (isFormat)
            {
                CheckFormatString(content, line);
                value = DecodeText(content, raw, line);
            }
            else
            {
                value = DecodeText(content, raw, line);
            }

            tokens.Add(new Token(TokenKind.Str, content, startLine) { Value = value, IsBytes = isBytes, IsFormat = isFormat });
            i = k;
        }

        private static void CheckFormatString(string content, int line)
        {
            var k = 0;
            while (k < content.Length)
            {
                var c = content[k];
                if (c == '{')
                {
                    if (k + 1 < content.Length && content[k + 1] == '{')
                    {
                        k += 2;
                        continue;
                    }

                    var depth = 1;
                    var j = k + 1;
                    while (j < content.Length && depth > 0)
                    {
                        depth += content[j] == '{' ? 1 : content[j] == '}' ? -1 : 0;
                        j++;
                    }

                    if (depth > 0)
                    {
                        throw Syntax("f-string: expecting '}'", line);
                    }

                    var field = content[(k + 1)..(j - 1)];
                    var end = field.IndexOfAny(['!', ':', '=']);
                    if ((end < 0 ? field : field[..end]).Trim().Length == 0)
                    {
                        throw Syntax("f-string: valid expression required before '}'", line);
                    }

                    k = j;
                }
                else if (c == '}')
                {
                    if (k + 1 < content.Length && content[k + 1] == '}')
                    {
                        k += 2;
                        continue;
                    }

                    throw Syntax("f-string: single '}' is not allowed", line);
                }
                else
                {
                    k++;
                }
            }
        }

        private static string DecodeText(string content, bool raw, int line)
        {
            if (raw || !content.Contains('\\', StringComparison.Ordinal))
            {
                return content;
            }

            var sb = new StringBuilder(content.Length);
            var k = 0;
            while (k < content.Length)
            {
                var c = content[k];
                if (c != '\\' || k + 1 >= content.Length)
                {
                    sb.Append(c);
                    k++;
                    continue;
                }

                var e = content[k + 1];
                k += 2;
                switch (e)
                {
                    case '\n':
                        break;
                    case '\\' or '\'' or '"':
                        sb.Append(e);
                        break;
                    case 'a':
                        sb.Append('\a');
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
                    case 'v':
                        sb.Append('\v');
                        break;
                    case >= '0' and <= '7':
                        var octal = e - '0';
                        for (var n = 0; n < 2 && k < content.Length && content[k] is >= '0' and <= '7'; n++)
                        {
                            octal = (octal * 8) + (content[k] - '0');
                            k++;
                        }

                        sb.Append((char)octal);
                        break;
                    case 'x' or 'u' or 'U':
                        var width = e == 'x' ? 2 : e == 'u' ? 4 : 8;
                        var code = 0L;
                        var digits = 0;
                        while (digits < width && k + digits < content.Length && Uri.IsHexDigit(content[k + digits]))
                        {
                            code = (code * 16) + System.Convert.ToInt32(content[k + digits].ToString(), 16);
                            digits++;
                        }

                        if (digits < width)
                        {
                            throw Syntax($"(unicode error) 'unicodeescape' codec can't decode bytes: truncated \\{(e == 'x' ? "xXX" : e == 'u' ? "uXXXX" : "UXXXXXXXX")} escape", line);
                        }

                        if (code > 0x10FFFF)
                        {
                            throw Syntax("(unicode error) 'unicodeescape' codec can't decode bytes: illegal Unicode character", line);
                        }

                        k += width;
                        if (code is >= 0xD800 and <= 0xDFFF)
                        {
                            sb.Append((char)code);
                        }
                        else
                        {
                            sb.Append(char.ConvertFromUtf32((int)code));
                        }

                        break;
                    case 'N':
                        var open = k;
                        var close = open < content.Length && content[open] == '{' ? content.IndexOf('}', open) : -1;
                        if (close < 0 || close == open + 1)
                        {
                            throw Syntax("(unicode error) 'unicodeescape' codec can't decode bytes: malformed \\N character escape", line);
                        }

                        if (!PyUnicodeNames.CodePoints.TryGetValue(content[(open + 1)..close].ToUpperInvariant(), out var named))
                        {
                            throw Syntax("(unicode error) 'unicodeescape' codec can't decode bytes: unknown Unicode character name", line);
                        }

                        sb.Append(char.ConvertFromUtf32(named));
                        k = close + 1;
                        break;
                    default:
                        sb.Append('\\').Append(e);
                        break;
                }
            }

            return sb.ToString();
        }

        private static byte[] DecodeBytes(string content, bool raw, int line)
        {
            if (content.Any(ch => ch > 127))
            {
                throw Syntax("bytes can only contain ASCII literal characters", line);
            }

            var bytes = new List<byte>(content.Length);
            var k = 0;
            while (k < content.Length)
            {
                var c = content[k];
                if (raw || c != '\\' || k + 1 >= content.Length)
                {
                    bytes.Add((byte)c);
                    k++;
                    continue;
                }

                var e = content[k + 1];
                k += 2;
                switch (e)
                {
                    case '\n':
                        break;
                    case '\\' or '\'' or '"':
                        bytes.Add((byte)e);
                        break;
                    case 'a':
                        bytes.Add(7);
                        break;
                    case 'b':
                        bytes.Add(8);
                        break;
                    case 'f':
                        bytes.Add(12);
                        break;
                    case 'n':
                        bytes.Add(10);
                        break;
                    case 'r':
                        bytes.Add(13);
                        break;
                    case 't':
                        bytes.Add(9);
                        break;
                    case 'v':
                        bytes.Add(11);
                        break;
                    case >= '0' and <= '7':
                        var octal = e - '0';
                        for (var n = 0; n < 2 && k < content.Length && content[k] is >= '0' and <= '7'; n++)
                        {
                            octal = (octal * 8) + (content[k] - '0');
                            k++;
                        }

                        bytes.Add((byte)(octal & 0xFF));
                        break;
                    case 'x':
                        if (k + 1 >= content.Length || !Uri.IsHexDigit(content[k]) || !Uri.IsHexDigit(content[k + 1]))
                        {
                            throw Syntax("(value error) invalid \\x escape", line);
                        }

                        bytes.Add(System.Convert.ToByte(content.Substring(k, 2), 16));
                        k += 2;
                        break;
                    default:
                        bytes.Add((byte)'\\');
                        bytes.Add((byte)e);
                        break;
                }
            }

            return [.. bytes];
        }

        /// <summary>
        /// The end of a number: a name character that is not the start of one of the keywords that may follow
        /// a number (<c>and else for if in is not or</c>) is an error, as in the tokenizer of CPython 3.12.
        /// </summary>
        private void VerifyEndOfNumber(string kind)
        {
            if (i >= s.Length)
            {
                return;
            }

            var c = s[i];
            var keyword = c switch
            {
                'a' => Follows("nd"),
                'e' => Follows("lse"),
                'f' => Follows("or"),
                'i' => i + 1 < s.Length && s[i + 1] is 'f' or 'n' or 's',
                'o' => Follows("r"),
                'n' => Follows("ot"),
                _ => false,
            };
            if (!keyword && c < 128 && (char.IsAsciiLetterOrDigit(c) || c == '_'))
            {
                throw Syntax($"invalid {kind} literal", line);
            }
        }

        private bool Follows(string rest) => string.CompareOrdinal(s, i + 1, rest, 0, rest.Length) == 0;

        private void LexNumber()
        {
            var start = i;
            if (s[i] == '0' && i + 1 < s.Length && s[i + 1] is 'x' or 'X' or 'o' or 'O' or 'b' or 'B')
            {
                var kindChar = char.ToLowerInvariant(s[i + 1]);
                var (radix, kind) = kindChar switch { 'x' => (16, "hexadecimal"), 'o' => (8, "octal"), _ => (2, "binary") };
                i += 2;
                var digits = new StringBuilder();
                while (true)
                {
                    if (i < s.Length && s[i] == '_')
                    {
                        i++;
                        if (i >= s.Length || !IsRadixDigit(s[i], radix))
                        {
                            throw Syntax($"invalid {kind} literal", line);
                        }
                    }

                    if (i < s.Length && IsRadixDigit(s[i], radix))
                    {
                        digits.Append(s[i]);
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (digits.Length == 0)
                {
                    throw Syntax($"invalid {kind} literal", line);
                }

                if (radix != 16 && i < s.Length && IsDigit(s[i]))
                {
                    throw Syntax($"invalid digit '{s[i]}' in {kind} literal", line);
                }

                VerifyEndOfNumber(kind);
                var value = BigInteger.Zero;
                foreach (var ch in digits.ToString())
                {
                    value = (value * radix) + System.Convert.ToInt32(ch.ToString(), 16);
                }

                tokens.Add(new Token(TokenKind.Number, s[start..i], line) { Value = Normalize(value) });
                return;
            }

            var integer = ReadDigits();
            var isFloat = false;
            if (i < s.Length && s[i] == '.')
            {
                isFloat = true;
                i++;
                if (i < s.Length && IsDigit(s[i]))
                {
                    ReadDigits();
                }
            }

            if (i < s.Length && s[i] is 'e' or 'E')
            {
                var next = i + 1 < s.Length ? s[i + 1] : '\0';
                if (next is '+' or '-')
                {
                    if (i + 2 >= s.Length || !IsDigit(s[i + 2]))
                    {
                        throw Syntax("invalid decimal literal", line);
                    }

                    i += 2;
                    ReadDigits();
                    isFloat = true;
                }
                else if (IsDigit(next))
                {
                    i++;
                    ReadDigits();
                    isFloat = true;
                }
                else
                {
                    VerifyEndOfNumber("decimal");
                }
            }

            var text = s[start..i].Replace("_", string.Empty, StringComparison.Ordinal);
            if (i < s.Length && s[i] is 'j' or 'J')
            {
                i++;
                VerifyEndOfNumber("imaginary");
                tokens.Add(new Token(TokenKind.Number, s[start..i], line) { Value = new ComplexLiteral() });
                return;
            }

            VerifyEndOfNumber("decimal");
            if (isFloat)
            {
                tokens.Add(new Token(TokenKind.Number, s[start..i], line)
                {
                    Value = double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture),
                });
                return;
            }

            var intText = integer.Replace("_", string.Empty, StringComparison.Ordinal);
            if (intText.Length > 1 && intText[0] == '0' && intText.Any(ch => ch != '0'))
            {
                throw Syntax("leading zeros in decimal integer literals are not permitted; use an 0o prefix for octal integers", line);
            }

            // A literal of zeros only is read in base 8 by CPython, where the limit of digits does not apply.
            if (intText.Length > MaxIntDigits && intText.Any(ch => ch != '0'))
            {
                throw Syntax(
                    string.Create(CultureInfo.InvariantCulture, $"Exceeds the limit ({MaxIntDigits} digits) for integer string conversion: value has {intText.Length} digits; use sys.set_int_max_str_digits() to increase the limit"),
                    line);
            }

            tokens.Add(new Token(TokenKind.Number, s[start..i], line)
            {
                Value = Normalize(BigInteger.Parse(intText, NumberStyles.None, CultureInfo.InvariantCulture)),
            });
        }

        private static bool IsRadixDigit(char c, int radix) => radix switch
        {
            16 => Uri.IsHexDigit(c),
            8 => c is >= '0' and <= '7',
            _ => c is '0' or '1',
        };

        /// <summary>Reads digits with single underscores between them. The caller has seen the first character.</summary>
        private string ReadDigits()
        {
            var start = i;
            while (i < s.Length)
            {
                if (IsDigit(s[i]))
                {
                    i++;
                }
                else if (s[i] == '_' && i > start)
                {
                    i++;
                    if (i >= s.Length || !IsDigit(s[i]))
                    {
                        throw Syntax("invalid decimal literal", line);
                    }
                }
                else
                {
                    break;
                }
            }

            return s[start..i];
        }
    }

    // ---- the parser ----

    private sealed class Parser(List<Token> tokens)
    {
        private int pos;
        private int depth;

        internal Node ParseEval()
        {
            var node = ParseExpressions();
            while (Peek.Kind == TokenKind.Newline)
            {
                pos++;
            }

            if (Peek.Kind != TokenKind.End)
            {
                throw Invalid();
            }

            return node;
        }

        private Token Peek => tokens[pos];

        private Token PeekAt(int offset) => tokens[Math.Min(pos + offset, tokens.Count - 1)];

        private bool IsOp(string op) => Peek is { Kind: TokenKind.Op } t && t.Text == op;

        private bool IsKeyword(string word) => Peek is { Kind: TokenKind.Name } t && t.Text == word;

        private static bool IsPlainName(Token t) => t.Kind == TokenKind.Name && !Keywords.Contains(t.Text);

        private FormatException Invalid() => Syntax("invalid syntax", Peek.Line);

        private void ExpectOp(string op)
        {
            if (!IsOp(op))
            {
                throw Invalid();
            }

            pos++;
        }

        private void ExpectKeyword(string word)
        {
            if (!IsKeyword(word))
            {
                throw Invalid();
            }

            pos++;
        }

        private static Node Other(string astName, int line) => new(NodeKind.Other, line, astName);

        /// <summary>The <c>eval</c> rule: <c>expression (',' expression)* [',']</c>. A comma makes a tuple.</summary>
        private Node ParseExpressions()
        {
            var line = Peek.Line;
            var first = ParseExpression();
            if (!IsOp(","))
            {
                return first;
            }

            var tuple = new Node(NodeKind.Tuple, line);
            tuple.Items.Add(first);
            while (IsOp(","))
            {
                pos++;
                if (!StartsExpression())
                {
                    break;
                }

                tuple.Items.Add(ParseExpression());
            }

            return tuple;
        }

        private bool StartsExpression()
        {
            var t = Peek;
            return t.Kind switch
            {
                TokenKind.Name => !Keywords.Contains(t.Text) || t.Text is "True" or "False" or "None" or "not" or "lambda" or "await",
                TokenKind.Number or TokenKind.Str => true,
                TokenKind.Op => t.Text is "(" or "[" or "{" or "-" or "+" or "~" or "...",
                _ => false,
            };
        }

        private Node ParseExpression()
        {
            if (++depth > MaxExpressionDepth)
            {
                throw new InvalidOperationException("maximum recursion depth exceeded during compilation");
            }

            try
            {
                var line = Peek.Line;
                if (IsKeyword("lambda"))
                {
                    return ParseLambda();
                }

                var node = ParseDisjunction();
                if (IsKeyword("if"))
                {
                    pos++;
                    ParseDisjunction();
                    ExpectKeyword("else");
                    ParseExpression();
                    return Other("IfExp", line);
                }

                return node;
            }
            finally
            {
                depth--;
            }
        }

        private Node ParseLambda()
        {
            var line = Peek.Line;
            pos++;
            while (!IsOp(":"))
            {
                if (IsOp("*") || IsOp("**"))
                {
                    pos++;
                    if (IsPlainName(Peek))
                    {
                        pos++;
                    }
                }
                else if (IsOp("/"))
                {
                    pos++;
                }
                else if (IsPlainName(Peek))
                {
                    pos++;
                    if (IsOp("="))
                    {
                        pos++;
                        ParseExpression();
                    }
                }
                else
                {
                    throw Invalid();
                }

                if (IsOp(","))
                {
                    pos++;
                }
                else if (!IsOp(":"))
                {
                    throw Invalid();
                }
            }

            pos++;
            ParseExpression();
            return Other("Lambda", line);
        }

        /// <summary><c>NAME ':=' expression</c> or an expression.</summary>
        private Node ParseNamedExpression()
        {
            if (IsPlainName(Peek) && PeekAt(1) is { Kind: TokenKind.Op, Text: ":=" })
            {
                var line = Peek.Line;
                pos += 2;
                ParseExpression();
                return Other("NamedExpr", line);
            }

            return ParseExpression();
        }

        private Node ParseDisjunction()
        {
            var node = ParseConjunction();
            if (!IsKeyword("or"))
            {
                return node;
            }

            var line = Peek.Line;
            while (IsKeyword("or"))
            {
                pos++;
                ParseConjunction();
            }

            return Other("BoolOp", line);
        }

        private Node ParseConjunction()
        {
            var node = ParseInversion();
            if (!IsKeyword("and"))
            {
                return node;
            }

            var line = Peek.Line;
            while (IsKeyword("and"))
            {
                pos++;
                ParseInversion();
            }

            return Other("BoolOp", line);
        }

        private Node ParseInversion()
        {
            var line = Peek.Line;
            var nots = 0;
            while (IsKeyword("not"))
            {
                pos++;
                nots++;
            }

            var node = ParseComparison();
            return nots > 0 ? Other("UnaryOp", line) : node;
        }

        private Node ParseComparison()
        {
            var node = ParseBinary(0);
            var line = Peek.Line;
            var compared = false;
            while (true)
            {
                if (Peek is { Kind: TokenKind.Op, Text: "==" or "!=" or "<" or "<=" or ">" or ">=" })
                {
                    pos++;
                }
                else if (IsKeyword("in"))
                {
                    pos++;
                }
                else if (IsKeyword("is"))
                {
                    pos++;
                    if (IsKeyword("not"))
                    {
                        pos++;
                    }
                }
                else if (IsKeyword("not") && PeekAt(1) is { Kind: TokenKind.Name, Text: "in" })
                {
                    pos += 2;
                }
                else
                {
                    break;
                }

                compared = true;
                ParseBinary(0);
            }

            return compared ? Other("Compare", line) : node;
        }

        /// <summary>The levels <c>|</c>, <c>^</c>, <c>&amp;</c>, shifts, sums, and products, by precedence climbing.</summary>
        private Node ParseBinary(int level)
        {
            if (level == 6)
            {
                return ParseFactor();
            }

            var node = ParseBinary(level + 1);
            while (Peek.Kind == TokenKind.Op && OperatorLevel(Peek.Text) == level)
            {
                var op = Peek.Text;
                var line = Peek.Line;
                pos++;
                var right = ParseBinary(level + 1);
                node = new Node(NodeKind.Binary, line, op) { Left = node, Right = right };
            }

            return node;
        }

        private static int OperatorLevel(string op) => op switch
        {
            "|" => 0,
            "^" => 1,
            "&" => 2,
            "<<" or ">>" => 3,
            "+" or "-" => 4,
            "*" or "/" or "//" or "%" or "@" => 5,
            _ => -1,
        };

        private Node ParseFactor()
        {
            var prefix = new List<(string Op, int Line)>();
            while (Peek is { Kind: TokenKind.Op, Text: "+" or "-" or "~" })
            {
                prefix.Add((Peek.Text, Peek.Line));
                pos++;
            }

            var node = ParseAwaitPrimary();
            if (IsOp("**"))
            {
                var line = Peek.Line;
                while (IsOp("**"))
                {
                    pos++;
                    while (Peek is { Kind: TokenKind.Op, Text: "+" or "-" or "~" })
                    {
                        pos++;
                    }

                    ParseAwaitPrimary();
                }

                node = Other("BinOp", line);
            }

            for (var k = prefix.Count - 1; k >= 0; k--)
            {
                node = new Node(NodeKind.Unary, prefix[k].Line, prefix[k].Op) { Left = node };
            }

            return node;
        }

        private Node ParseAwaitPrimary()
        {
            if (IsKeyword("await"))
            {
                var line = Peek.Line;
                pos++;
                ParsePrimary();
                return Other("Await", line);
            }

            return ParsePrimary();
        }

        private Node ParsePrimary()
        {
            var node = ParseAtom();
            while (true)
            {
                var line = Peek.Line;
                if (IsOp("."))
                {
                    pos++;
                    if (!IsPlainName(Peek))
                    {
                        throw Invalid();
                    }

                    pos++;
                    node = Other("Attribute", line);
                }
                else if (IsOp("("))
                {
                    pos++;
                    var (count, keywords) = ParseArguments();
                    node = new Node(NodeKind.Call, line)
                    {
                        IsSetCall = node is { Kind: NodeKind.Name, Tag: "set" } && count == 0 && keywords == 0,
                    };
                }
                else if (IsOp("["))
                {
                    pos++;
                    ParseSlices();
                    node = Other("Subscript", line);
                }
                else
                {
                    return node;
                }
            }
        }

        private (int Positional, int Keywords) ParseArguments()
        {
            var positional = 0;
            var keywords = 0;
            var sawKeyword = false;
            var sawDoubleStar = false;
            while (!IsOp(")"))
            {
                if (IsOp("**"))
                {
                    pos++;
                    ParseExpression();
                    keywords++;
                    sawDoubleStar = true;
                }
                else if (IsOp("*"))
                {
                    if (sawDoubleStar)
                    {
                        throw Syntax("iterable argument unpacking follows keyword argument unpacking", Peek.Line);
                    }

                    pos++;
                    ParseExpression();
                    positional++;
                }
                else if (IsPlainName(Peek) && PeekAt(1) is { Kind: TokenKind.Op, Text: "=" })
                {
                    pos += 2;
                    ParseExpression();
                    keywords++;
                    sawKeyword = true;
                }
                else
                {
                    if (sawKeyword || sawDoubleStar)
                    {
                        throw Syntax("positional argument follows keyword argument", Peek.Line);
                    }

                    ParseNamedExpression();
                    if (IsKeyword("for") || IsKeyword("async"))
                    {
                        ParseComprehensionClauses();
                        if (positional > 0 || keywords > 0 || !IsOp(")"))
                        {
                            throw Syntax("Generator expression must be parenthesized", Peek.Line);
                        }
                    }

                    positional++;
                }

                if (IsOp(","))
                {
                    pos++;
                }
                else if (!IsOp(")"))
                {
                    throw Invalid();
                }
            }

            pos++;
            return (positional, keywords);
        }

        private void ParseSlices()
        {
            while (true)
            {
                if (IsOp("*"))
                {
                    pos++;
                    ParseBinary(0);
                }
                else
                {
                    ParseSlice();
                }

                if (IsOp(","))
                {
                    pos++;
                    if (IsOp("]"))
                    {
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            ExpectOp("]");
        }

        private void ParseSlice()
        {
            if (!IsOp(":"))
            {
                var first = ParseNamedExpression();
                if (!IsOp(":"))
                {
                    _ = first;
                    return;
                }
            }

            ExpectOp(":");
            if (StartsExpression())
            {
                ParseExpression();
            }

            if (IsOp(":"))
            {
                pos++;
                if (StartsExpression())
                {
                    ParseExpression();
                }
            }
        }

        private Node ParseAtom()
        {
            var t = Peek;
            switch (t.Kind)
            {
                case TokenKind.Name:
                    if (t.Text is "True" or "False" or "None")
                    {
                        pos++;
                        return new Node(NodeKind.Constant, t.Line) { Value = t.Text == "None" ? null : t.Text == "True" };
                    }

                    if (Keywords.Contains(t.Text))
                    {
                        throw Invalid();
                    }

                    pos++;
                    return new Node(NodeKind.Name, t.Line, t.Text);
                case TokenKind.Number:
                    pos++;
                    return new Node(NodeKind.Constant, t.Line) { Value = t.Value };
                case TokenKind.Str:
                    return ParseStrings();
                case TokenKind.Op when t.Text == "...":
                    pos++;
                    return new Node(NodeKind.Constant, t.Line) { Value = new EllipsisLiteral() };
                case TokenKind.Op when t.Text == "(":
                    return ParseParenthesized();
                case TokenKind.Op when t.Text == "[":
                    return ParseListDisplay();
                case TokenKind.Op when t.Text == "{":
                    return ParseBraces();
                default:
                    throw Invalid();
            }
        }

        private Node ParseStrings()
        {
            var line = Peek.Line;
            var bytes = new List<byte>();
            var text = new StringBuilder();
            var sawBytes = false;
            var sawText = false;
            var sawFormat = false;
            while (Peek.Kind == TokenKind.Str)
            {
                var t = Peek;
                pos++;
                if (t.IsBytes)
                {
                    sawBytes = true;
                    bytes.AddRange((byte[])(t.Value ?? Array.Empty<byte>()));
                }
                else
                {
                    sawText = true;
                    sawFormat |= t.IsFormat;
                    text.Append((string)(t.Value ?? string.Empty));
                }
            }

            if (sawBytes && sawText)
            {
                throw Syntax("cannot mix bytes and nonbytes literals", line);
            }

            if (sawFormat)
            {
                return Other("JoinedStr", line);
            }

            return new Node(NodeKind.Constant, line) { Value = sawBytes ? bytes.ToArray() : text.ToString() };
        }

        /// <summary>One element of a display: <c>*bitwise_or</c> or a named expression.</summary>
        private Node ParseStarNamed()
        {
            if (IsOp("*"))
            {
                var line = Peek.Line;
                pos++;
                ParseBinary(0);
                return new Node(NodeKind.Starred, line);
            }

            return ParseNamedExpression();
        }

        private Node ParseParenthesized()
        {
            var line = Peek.Line;
            pos++;
            if (IsOp(")"))
            {
                pos++;
                return new Node(NodeKind.Tuple, line);
            }

            if (IsKeyword("yield"))
            {
                pos++;
                if (IsKeyword("from"))
                {
                    pos++;
                    ParseExpression();
                }
                else if (StartsExpression() || IsOp("*"))
                {
                    ParseStarExpressions();
                }

                ExpectOp(")");
                return Other("Yield", line);
            }

            var first = ParseStarNamed();
            if (IsKeyword("for") || IsKeyword("async"))
            {
                ParseComprehensionClauses();
                ExpectOp(")");
                return Other("GeneratorExp", line);
            }

            if (IsOp(","))
            {
                var tuple = new Node(NodeKind.Tuple, line);
                tuple.Items.Add(first);
                while (IsOp(","))
                {
                    pos++;
                    if (IsOp(")"))
                    {
                        break;
                    }

                    tuple.Items.Add(ParseStarNamed());
                }

                ExpectOp(")");
                return tuple;
            }

            ExpectOp(")");
            if (first.Kind == NodeKind.Starred)
            {
                throw Syntax("cannot use starred expression here", line);
            }

            return first;
        }

        private void ParseStarExpressions()
        {
            ParseStarNamed();
            while (IsOp(","))
            {
                pos++;
                if (IsOp(")"))
                {
                    break;
                }

                ParseStarNamed();
            }
        }

        private Node ParseListDisplay()
        {
            var line = Peek.Line;
            pos++;
            var list = new Node(NodeKind.List, line);
            if (IsOp("]"))
            {
                pos++;
                return list;
            }

            var first = ParseStarNamed();
            if (IsKeyword("for") || IsKeyword("async"))
            {
                ParseComprehensionClauses();
                ExpectOp("]");
                return Other("ListComp", line);
            }

            list.Items.Add(first);
            while (IsOp(","))
            {
                pos++;
                if (IsOp("]"))
                {
                    break;
                }

                list.Items.Add(ParseStarNamed());
            }

            ExpectOp("]");
            return list;
        }

        private Node ParseBraces()
        {
            var line = Peek.Line;
            pos++;
            if (IsOp("}"))
            {
                pos++;
                return new Node(NodeKind.Dict, line);
            }

            if (IsOp("**"))
            {
                var dict = new Node(NodeKind.Dict, line);
                ParseDictRest(dict);
                return dict;
            }

            var first = ParseStarNamed();
            if (IsOp(":"))
            {
                if (first is { Kind: NodeKind.Other, Tag: "NamedExpr" } or { Kind: NodeKind.Starred })
                {
                    throw Invalid();
                }

                pos++;
                var value = ParseExpression();
                if (IsKeyword("for") || IsKeyword("async"))
                {
                    ParseComprehensionClauses();
                    ExpectOp("}");
                    return Other("DictComp", line);
                }

                var dict = new Node(NodeKind.Dict, line);
                dict.Pairs.Add((first, value));
                if (IsOp(","))
                {
                    pos++;
                    ParseDictRest(dict);
                }
                else
                {
                    ExpectOp("}");
                }

                return dict;
            }

            if (IsKeyword("for") || IsKeyword("async"))
            {
                ParseComprehensionClauses();
                ExpectOp("}");
                return Other("SetComp", line);
            }

            var set = new Node(NodeKind.Set, line);
            set.Items.Add(first);
            while (IsOp(","))
            {
                pos++;
                if (IsOp("}"))
                {
                    break;
                }

                set.Items.Add(ParseStarNamed());
            }

            ExpectOp("}");
            if (set.Items.Any(item => item.Kind == NodeKind.Starred))
            {
                return Other("Set", line);
            }

            return set;
        }

        /// <summary>The items of a dict after the first one, up to the closing brace.</summary>
        private void ParseDictRest(Node dict)
        {
            while (!IsOp("}"))
            {
                if (IsOp("**"))
                {
                    pos++;
                    var value = ParseBinary(0);
                    dict.Pairs.Add((null, value));
                }
                else
                {
                    var key = ParseExpression();
                    ExpectOp(":");
                    dict.Pairs.Add((key, ParseExpression()));
                }

                if (IsOp(","))
                {
                    pos++;
                }
                else if (!IsOp("}"))
                {
                    throw Invalid();
                }
            }

            pos++;
        }

        private void ParseComprehensionClauses()
        {
            var any = false;
            while (IsKeyword("for") || (IsKeyword("async") && PeekAt(1) is { Kind: TokenKind.Name, Text: "for" }))
            {
                if (IsKeyword("async"))
                {
                    pos++;
                }

                pos++;
                ParseTargets();
                ExpectKeyword("in");
                ParseDisjunction();
                while (IsKeyword("if"))
                {
                    pos++;
                    ParseDisjunction();
                }

                any = true;
            }

            if (!any)
            {
                throw Invalid();
            }
        }

        private void ParseTargets()
        {
            while (true)
            {
                if (IsOp("*"))
                {
                    pos++;
                }

                var target = ParseBinary(0);
                if (!IsTarget(target))
                {
                    throw Syntax("cannot assign to expression", Peek.Line);
                }

                if (IsOp(","))
                {
                    pos++;
                    if (IsKeyword("in"))
                    {
                        return;
                    }
                }
                else
                {
                    return;
                }
            }
        }

        private static bool IsTarget(Node node) => node.Kind switch
        {
            NodeKind.Name or NodeKind.Starred => true,
            NodeKind.Other => node.Tag is "Attribute" or "Subscript",
            NodeKind.Tuple or NodeKind.List => node.Items.All(IsTarget),
            _ => false,
        };
    }
}
