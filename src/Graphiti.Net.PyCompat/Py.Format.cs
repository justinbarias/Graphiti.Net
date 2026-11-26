namespace Graphiti.PyCompat;

using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

/// <summary>
/// <c>format(value, spec)</c>, which an f-string such as <c>f'{x:>12,}'</c> calls, and the <c>%</c> forms
/// <c>%.30s</c> and <c>%.1f</c> of a log call (RULES.md R5, R10).
/// </summary>
internal static partial class Py
{
    /// <summary>
    /// <c>format(value, spec)</c> for the parts of Python's format-spec language that upstream uses: fill and
    /// align (<c>&lt;</c>, <c>&gt;</c>, <c>^</c>, <c>=</c>), sign, <c>0</c>, width, the grouping
    /// <c>,</c> and <c>_</c>, precision, and the types <c>s</c>, <c>d</c>, <c>f</c>, and <c>F</c>. Widths and
    /// precisions of text count code points. Floats are rounded half to even on the exact binary value.
    /// A spec that Python rejects throws <see cref="ArgumentException"/> (<c>ValueError</c>) or
    /// <see cref="InvalidCastException"/> (<c>TypeError</c>) with Python's message. A spec that Python
    /// accepts and this helper does not cover throws <see cref="InvalidOperationException"/>. These are
    /// not built: the types <c>e</c>, <c>E</c>, <c>g</c>, <c>G</c>, <c>%</c>, <c>n</c>, <c>c</c>, <c>b</c>,
    /// <c>o</c>, <c>x</c>, and <c>X</c>, the options <c>#</c> and <c>z</c>, the flag <c>0</c> together with
    /// grouping, a float with a precision and no type, and a date with a spec (<c>strftime</c>).
    /// </summary>
    internal static string Format(object? value, string spec)
    {
        switch (value)
        {
            case string s:
                return FormatText(s, spec, "str");
            case Enum e:
                // Enum.__format__ of a plain Enum formats str(member).
                return FormatText(Str(e), spec, "str");
            case bool b when spec.Length == 0:
                return Str(b);
            case bool or long or BigInteger:
                return FormatInt(value is bool b2 ? (b2 ? BigInteger.One : BigInteger.Zero) : value is long l ? l : (BigInteger)value, spec, TypeName(value));
            case double d:
                return FormatFloat(d, ParseSpec(spec, "float", '\0'), spec);
        }

        if (spec.Length == 0)
        {
            return Str(value);
        }

        if (value is null or byte[] or PyTuple or IPySet or PyDict or IDictionary or IList)
        {
            // object.__format__ accepts only an empty spec.
            throw new InvalidCastException($"unsupported format string passed to {TypeName(value)}.__format__");
        }

        throw new InvalidOperationException($"Py.Format: the spec '{spec}' for {TypeName(value)} is not supported");
    }

    private static string FormatText(string s, string spec, string typeName)
    {
        var f = ParseSpec(spec, typeName, 's');
        if (f.Type != 's')
        {
            throw UnknownCode(f.Type, typeName);
        }

        if (f.Sign != '\0')
        {
            throw new ArgumentException(f.Sign == ' '
                ? "Space not allowed in string format specifier"
                : "Sign not allowed in string format specifier");
        }

        if (f.NoNegativeZero)
        {
            throw new ArgumentException("Negative zero coercion (z) not allowed in string format specifier");
        }

        if (f.Alternate)
        {
            throw new ArgumentException("Alternate form (#) not allowed in string format specifier");
        }

        if (f.Align == '=')
        {
            throw new ArgumentException("'=' alignment not allowed in string format specifier");
        }

        var cps = PyText.ToCodePoints(s);
        var text = f.Precision is long p && p < cps.Length ? PyText.FromCodePoints(cps.AsSpan(0, (int)p)) : s;
        return Pad(string.Empty, text, f with { Align = f.Align == '\0' ? '<' : f.Align });
    }

    private static string FormatInt(BigInteger n, string spec, string typeName)
    {
        var f = ParseSpec(spec, typeName, 'd');
        switch (f.Type)
        {
            case 'e' or 'E' or 'f' or 'F' or 'g' or 'G' or '%':
                return FormatFloat(IntToFloat(n), f, spec);
            case 'b' or 'c' or 'd' or 'o' or 'x' or 'X' or 'n':
                break;
            default:
                throw UnknownCode(f.Type, typeName);
        }

        if (f.Precision is not null)
        {
            throw new ArgumentException("Precision not allowed in integer format specifier");
        }

        if (f.NoNegativeZero)
        {
            throw new ArgumentException("Negative zero coercion (z) not allowed in integer format specifier");
        }

        if (f.Type == 'c' && f.Sign != '\0')
        {
            throw new ArgumentException("Sign not allowed with integer format specifier 'c'");
        }

        if (f.Type == 'c' && f.Alternate)
        {
            throw new ArgumentException("Alternate form (#) not allowed with integer format specifier 'c'");
        }

        if (f.Type == 'c' && (n < long.MinValue || n > long.MaxValue))
        {
            throw new OverflowException("Python int too large to convert to C long");
        }

        if (f.Type == 'c' && (n < 0 || n > 0x10FFFF))
        {
            throw new OverflowException("%c arg not in range(0x110000)");
        }

        if (f.Type != 'd' || f.Alternate || ZeroPadGrouped(f))
        {
            throw new InvalidOperationException($"Py.Format: the spec '{spec}' for int is not supported");
        }

        var digits = Group(IntText(BigInteger.Abs(n)), f.Grouping);
        return Pad(SignText(n.Sign < 0, f.Sign), digits, f);
    }

    private static string FormatFloat(double d, FormatSpec f, string spec)
    {
        if (f.Type is not ('f' or 'F' or '\0' or 'e' or 'E' or 'g' or 'G' or 'n' or '%'))
        {
            throw UnknownCode(f.Type, "float");
        }

        if (f.Precision > int.MaxValue)
        {
            throw new ArgumentException("precision too big");
        }

        if (f.Type is not ('f' or 'F' or '\0'))
        {
            throw new InvalidOperationException($"Py.Format: the spec '{spec}' for float is not supported");
        }

        if (f.Alternate || f.NoNegativeZero || ZeroPadGrouped(f) || (f.Type == '\0' && f.Precision is not null))
        {
            throw new InvalidOperationException($"Py.Format: the spec '{spec}' for float is not supported");
        }

        // Python writes no sign for a nan, also when its sign bit is set.
        var negative = double.IsNegative(d) && !double.IsNaN(d);
        string body;
        if (double.IsNaN(d) || double.IsInfinity(d))
        {
            body = double.IsNaN(d) ? "nan" : "inf";
            body = f.Type == 'F' ? body.ToUpperInvariant() : body;
        }
        else if (f.Type == '\0')
        {
            // No type and no precision: repr() text, with grouping in the whole part.
            body = FloatRepr(Math.Abs(d));
            if (f.Grouping != '\0' && !body.Contains('e', StringComparison.Ordinal))
            {
                var dot = body.IndexOf('.', StringComparison.Ordinal);
                body = Group(body[..dot], f.Grouping) + body[dot..];
            }
        }
        else
        {
            body = FixedText(Math.Abs(d), (int)(f.Precision ?? 6));
            var dot = body.IndexOf('.', StringComparison.Ordinal);
            body = dot < 0 ? Group(body, f.Grouping) : Group(body[..dot], f.Grouping) + body[dot..];
        }

        return Pad(SignText(negative, f.Sign), body, f);
    }

    /// <summary>
    /// The exact value of a finite, non-negative double, rounded half to even to <paramref name="precision"/>
    /// digits after the point, as <c>'%.*f'</c> gives it.
    /// </summary>
    private static string FixedText(double a, int precision)
    {
        var bits = BitConverter.DoubleToInt64Bits(a);
        var exponent = (int)((bits >> 52) & 0x7FF);
        var mantissa = bits & 0xFFFFFFFFFFFFFL;
        if (exponent == 0)
        {
            exponent = 1;
        }
        else
        {
            mantissa |= 1L << 52;
        }

        // a = mantissa * 2^shift, so a * 10^precision = mantissa * 10^precision * 2^shift.
        var shift = exponent - 1075;
        var scaled = mantissa * BigInteger.Pow(10, precision);
        BigInteger q;
        if (shift >= 0)
        {
            q = scaled << shift;
        }
        else
        {
            var denominator = BigInteger.One << -shift;
            q = BigInteger.DivRem(scaled, denominator, out var remainder);
            var twice = remainder * 2;
            if (twice > denominator || (twice == denominator && !q.IsEven))
            {
                q += 1;
            }
        }

        var text = q.ToString(CultureInfo.InvariantCulture).PadLeft(precision + 1, '0');
        return precision == 0 ? text : text[..^precision] + "." + text[^precision..];
    }

    private static string Group(string digits, char separator)
    {
        if (separator == '\0' || digits.Length <= 3)
        {
            return digits;
        }

        var sb = new StringBuilder();
        for (var i = 0; i < digits.Length; i++)
        {
            if (i > 0 && (digits.Length - i) % 3 == 0)
            {
                sb.Append(separator);
            }

            sb.Append(digits[i]);
        }

        return sb.ToString();
    }

    private static string SignText(bool negative, char sign) =>
        negative ? "-" : sign switch { '+' => "+", ' ' => " ", _ => string.Empty };

    private static string Pad(string sign, string body, FormatSpec f)
    {
        var length = (int)(Len(sign) + Len(body));
        var missing = f.Width is long w && w > length ? (int)(w - length) : 0;
        var fill = PyText.FromCodePoints([f.Fill]);
        string Fill(int n) => string.Concat(Enumerable.Repeat(fill, n));
        return (f.Align == '\0' ? '>' : f.Align) switch
        {
            '<' => sign + body + Fill(missing),
            '^' => Fill(missing / 2) + sign + body + Fill(missing - (missing / 2)),
            '=' => sign + Fill(missing) + body,
            _ => Fill(missing) + sign + body,
        };
    }

    /// <summary>
    /// True when CPython writes separators into the padding: grouping with the fill 0 and the align '='.
    /// That form is not built.
    /// </summary>
    private static bool ZeroPadGrouped(FormatSpec f) => f.Grouping != '\0' && f.Fill == '0' && f.Align == '=';

    private static ArgumentException UnknownCode(char type, string typeName) =>
        new($"Unknown format code '{type}' for object of type '{typeName}'");

    /// <summary>
    /// Reads <c>[[fill]align][sign][z][#][0][width][grouping][.precision][type]</c> as CPython's
    /// <c>parse_internal_render_format_spec</c> does.
    /// </summary>
    private static FormatSpec ParseSpec(string spec, string typeName, char defaultType)
    {
        var cps = PyText.ToCodePoints(spec);
        var pos = 0;
        var f = new FormatSpec { Type = defaultType };
        bool IsAlign(int i) => i < cps.Length && cps[i] is '<' or '>' or '=' or '^';
        var fillGiven = false;
        if (cps.Length >= 2 && IsAlign(1))
        {
            f = f with { Fill = cps[0], Align = (char)cps[1] };
            fillGiven = true;
            pos = 2;
        }
        else if (IsAlign(0))
        {
            f = f with { Align = (char)cps[0] };
            pos = 1;
        }

        if (pos < cps.Length && cps[pos] is '+' or '-' or ' ')
        {
            f = f with { Sign = (char)cps[pos++] };
        }

        if (pos < cps.Length && cps[pos] == 'z')
        {
            f = f with { NoNegativeZero = true };
            pos++;
        }

        if (pos < cps.Length && cps[pos] == '#')
        {
            f = f with { Alternate = true };
            pos++;
        }

        if (pos < cps.Length && cps[pos] == '0' && !fillGiven)
        {
            f = f with { Fill = '0', Zero = true, Align = f.Align == '\0' && defaultType != 's' ? '=' : f.Align };
            pos++;
        }

        f = f with { Width = ReadNumber(cps, ref pos) };
        if (pos < cps.Length && cps[pos] is ',' or '_')
        {
            f = f with { Grouping = (char)cps[pos++] };
            if (pos < cps.Length && cps[pos] is ',' or '_' && cps[pos] != f.Grouping)
            {
                throw new ArgumentException("Cannot specify both ',' and '_'.");
            }
        }

        if (pos < cps.Length && cps[pos] == '.')
        {
            pos++;
            f = f with { Precision = ReadNumber(cps, ref pos) ?? throw new ArgumentException("Format specifier missing precision") };
        }

        if (cps.Length - pos > 1)
        {
            throw new ArgumentException($"Invalid format specifier '{spec}' for object of type '{typeName}'");
        }

        if (pos < cps.Length)
        {
            f = f with { Type = cps[pos] < 0x10000 ? (char)cps[pos] : '�' };
        }

        if (f.Grouping != '\0' && f.Type is not ('d' or 'e' or 'f' or 'g' or 'E' or 'G' or '%' or 'F' or '\0'))
        {
            var allowedUnderscore = f.Grouping == '_' && f.Type is 'b' or 'o' or 'x' or 'X';
            if (!allowedUnderscore)
            {
                throw new ArgumentException($"Cannot specify '{f.Grouping}' with '{f.Type}'.");
            }
        }

        return f;
    }

    /// <summary>A width or a precision. Python refuses a number above <c>PY_SSIZE_T_MAX</c>.</summary>
    private static long? ReadNumber(int[] cps, ref int pos)
    {
        var start = pos;
        long n = 0;
        while (pos < cps.Length && cps[pos] is >= '0' and <= '9')
        {
            var digit = cps[pos] - '0';
            if (n > (long.MaxValue - digit) / 10)
            {
                throw new ArgumentException("Too many decimal digits in format string");
            }

            n = (n * 10) + digit;
            pos++;
        }

        return pos == start ? null : n;
    }

    private sealed record FormatSpec
    {
        public int Fill { get; init; } = ' ';

        public char Align { get; init; }

        public char Sign { get; init; }

        public bool NoNegativeZero { get; init; }

        public bool Alternate { get; init; }

        public bool Zero { get; init; }

        public long? Width { get; init; }

        public char Grouping { get; init; }

        public long? Precision { get; init; }

        public char Type { get; init; }
    }
}
