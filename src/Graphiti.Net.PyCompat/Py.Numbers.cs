namespace Graphiti.PyCompat;

using System.Globalization;
using System.Numerics;
using System.Text;

/// <summary>Python builtins that C# lacks. Numbers, here. Sequences and UUIDs are in the other parts.</summary>
internal static partial class Py
{
    /// <summary>
    /// <c>a / b</c> on two ints. The result is the correctly rounded quotient, also for values above 2^53.
    /// A zero divisor throws <see cref="DivideByZeroException"/> (<c>ZeroDivisionError</c>).
    /// </summary>
    internal static double TrueDiv(long a, long b)
    {
        if (b == 0)
        {
            throw new DivideByZeroException("division by zero");
        }

        const long exact = 1L << 53;
        if (a is >= -exact and <= exact && b is >= -exact and <= exact)
        {
            return (double)a / b;
        }

        var negative = (a < 0) != (b < 0);
        var na = BigInteger.Abs(a);
        var nb = BigInteger.Abs(b);
        if (na.IsZero)
        {
            return negative ? -0.0 : 0.0;
        }

        // Scale so that the integer quotient has 55 or 56 bits. Two bits more than a double holds,
        // and the last bit records whether the division left a rest. The conversion of a long to a
        // double then rounds to nearest even, which is what Python does.
        var shift = 55 + (int)nb.GetBitLength() - (int)na.GetBitLength();
        BigInteger quotient;
        bool rest;
        if (shift >= 0)
        {
            quotient = BigInteger.DivRem(na << shift, nb, out var remainder);
            rest = !remainder.IsZero;
        }
        else
        {
            quotient = BigInteger.DivRem(na, nb << -shift, out var remainder);
            rest = !remainder.IsZero;
        }

        var q = (long)quotient;
        if (rest)
        {
            q |= 1;
        }

        var result = Math.ScaleB((double)q, -shift);
        return negative ? -result : result;
    }

    /// <summary>
    /// <c>a / b</c> on two floats. A zero divisor throws <see cref="DivideByZeroException"/>
    /// (<c>ZeroDivisionError</c>), also for 0.0 / 0.0.
    /// </summary>
    internal static double TrueDiv(double a, double b)
    {
        if (b == 0)
        {
            throw new DivideByZeroException("float division by zero");
        }

        return a / b;
    }

    /// <summary>
    /// <c>a // b</c>: the quotient rounded toward negative infinity. A zero divisor throws
    /// <see cref="DivideByZeroException"/>. A result that a long cannot hold (<c>long.MinValue // -1</c>)
    /// throws <see cref="OverflowException"/>.
    /// </summary>
    internal static long FloorDiv(long a, long b)
    {
        if (b == 0)
        {
            throw new DivideByZeroException("integer division or modulo by zero");
        }

        if (a == long.MinValue && b == -1)
        {
            throw new OverflowException("the result of // does not fit in a long");
        }

        var q = a / b;
        if (a % b != 0 && (a < 0) != (b < 0))
        {
            q--;
        }

        return q;
    }

    /// <summary>
    /// <c>a % b</c>: the rest takes the sign of <paramref name="b"/>. A zero divisor throws
    /// <see cref="DivideByZeroException"/>.
    /// </summary>
    internal static long Mod(long a, long b)
    {
        if (b == 0)
        {
            throw new DivideByZeroException("integer modulo by zero");
        }

        if (b == -1)
        {
            return 0;
        }

        var r = a % b;
        if (r != 0 && (r < 0) != (b < 0))
        {
            r += b;
        }

        return r;
    }

    /// <summary>
    /// <c>math.comb(n, k)</c>: the number of ways to choose <paramref name="k"/> items from
    /// <paramref name="n"/>. It is 0 when <paramref name="k"/> is above <paramref name="n"/>. A negative
    /// argument throws <see cref="ArgumentException"/> (<c>ValueError</c>). The result is a
    /// <see cref="BigInteger"/>, because a count such as <c>comb(100, 20)</c> does not fit in a long, and
    /// upstream compares it with a limit (<c>utils/content_chunking.py:752</c>).
    /// </summary>
    internal static BigInteger Comb(long n, long k)
    {
        if (n < 0)
        {
            throw new ArgumentException("n must be a non-negative integer");
        }

        if (k < 0)
        {
            throw new ArgumentException("k must be a non-negative integer");
        }

        if (k > n)
        {
            return BigInteger.Zero;
        }

        k = Math.Min(k, n - k);
        var result = BigInteger.One;
        for (long i = 1; i <= k; i++)
        {
            result = result * (n - k + i) / i;
        }

        return result;
    }

    /// <summary>
    /// <c>int(x)</c> on a float: cuts toward zero. <c>nan</c> throws <see cref="ArgumentException"/>
    /// (<c>ValueError</c>) and an infinity throws <see cref="OverflowException"/> (<c>OverflowError</c>).
    /// A value outside the range of a long also throws <see cref="OverflowException"/>.
    /// </summary>
    internal static long Int(double x)
    {
        if (double.IsNaN(x))
        {
            throw new ArgumentException("cannot convert float NaN to integer");
        }

        if (double.IsInfinity(x))
        {
            throw new OverflowException("cannot convert float infinity to integer");
        }

        var t = Math.Truncate(x);
        if (t < -9223372036854775808.0 || t >= 9223372036854775808.0)
        {
            throw new OverflowException("the value does not fit in a long");
        }

        return (long)t;
    }

    /// <summary>The value of a decimal digit code point (Unicode category Nd, Unicode 15), or -1.</summary>
    private static int DecimalDigit(int cp)
    {
        var starts = PyUnicodeData.DecimalDigitStarts;
        var index = Array.BinarySearch(starts, cp);
        if (index < 0)
        {
            index = ~index - 1;
        }

        return index >= 0 && cp - starts[index] < 10 ? cp - starts[index] : -1;
    }

    /// <summary>
    /// The whitespace that <c>int()</c> and <c>float()</c> remove. Below U+007F it is the C set (the
    /// separators U+001C to U+001F are not in it). Above, it is the whitespace set of <c>str</c>.
    /// </summary>
    private static bool IsNumberSpace(int cp) =>
        cp < 127 ? cp is (>= 9 and <= 13) or 32 : PyText.IsSpaceCodePoint(cp);

    /// <summary>
    /// The text that Python's <c>int()</c> and <c>float()</c> read: whitespace removed at both ends and
    /// each decimal digit written as an ASCII digit. Other text stays as it is.
    /// </summary>
    private static string NormalizeNumberText(string s)
    {
        var cps = PyText.ToCodePoints(s);
        var start = 0;
        var end = cps.Length;
        while (start < end && IsNumberSpace(cps[start]))
        {
            start++;
        }

        while (end > start && IsNumberSpace(cps[end - 1]))
        {
            end--;
        }

        var sb = new StringBuilder(end - start);
        for (var i = start; i < end; i++)
        {
            var digit = DecimalDigit(cps[i]);
            if (digit >= 0)
            {
                sb.Append((char)('0' + digit));
            }
            else if (cps[i] is >= 0xD800 and <= 0xDFFF or >= 0x10000)
            {
                sb.Append('￿');
            }
            else
            {
                sb.Append((char)cps[i]);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Reads digits with single underscores between two digits, from <paramref name="i"/>.
    /// Returns the digits without underscores, or null if there is none or an underscore is misplaced.
    /// </summary>
    private static string? ReadDigits(string s, ref int i)
    {
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            if (s[i] is >= '0' and <= '9')
            {
                sb.Append(s[i]);
                i++;
            }
            else if (s[i] == '_' && sb.Length > 0 && i + 1 < s.Length && s[i + 1] is >= '0' and <= '9')
            {
                i++;
            }
            else
            {
                break;
            }
        }

        return sb.Length == 0 ? null : sb.ToString();
    }

    /// <summary>
    /// <c>int(x)</c> on a value from open data: a bool, an int, a float (cut toward zero), text, or bytes with
    /// decimal text. Another type throws <see cref="InvalidCastException"/> with the text of Python's
    /// <c>TypeError</c>. An int outside the range of a long throws <see cref="OverflowException"/>, where Python
    /// returns the value (RULES.md R4).
    /// </summary>
    internal static long Int(object? x) => x switch
    {
        bool b => b ? 1 : 0,
        long l => l,
        BigInteger => throw new OverflowException("the value does not fit in a long"),
        double d => Int(d),
        string s => Int(s),
        byte[] bytes => Int(System.Text.Encoding.Latin1.GetString(bytes)),
        _ => throw new InvalidCastException($"int() argument must be a string, a bytes-like object or a real number, not '{TypeName(x)}'"),
    };

    /// <summary>
    /// <c>int(text)</c>: decimal text with an optional sign, underscores between digits, Unicode digits,
    /// and whitespace at both ends. Text that does not parse throws <see cref="ArgumentException"/>
    /// (<c>ValueError</c>). A value outside the range of a long throws <see cref="OverflowException"/>.
    /// </summary>
    internal static long Int(string text)
    {
        var s = NormalizeNumberText(text);
        var i = 0;
        var negative = false;
        if (i < s.Length && s[i] is '+' or '-')
        {
            negative = s[i] == '-';
            i++;
        }

        var digits = ReadDigits(s, ref i);
        if (digits is null || i != s.Length)
        {
            throw InvalidIntLiteral(text);
        }

        if (digits.Length > MaxIntStrDigits)
        {
            throw new ArgumentException(
                $"Exceeds the limit ({MaxIntStrDigits} digits) for integer string conversion: value has "
                + $"{digits.Length} digits; use sys.set_int_max_str_digits() to increase the limit");
        }

        if (!BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            throw InvalidIntLiteral(text);
        }

        if (negative)
        {
            value = -value;
        }

        if (value < long.MinValue || value > long.MaxValue)
        {
            throw new OverflowException("the value does not fit in a long");
        }

        return (long)value;
    }

    /// <summary>
    /// <c>float(text)</c>: a decimal number with an optional sign, a fraction, an exponent, underscores
    /// between digits, Unicode digits, or <c>inf</c>, <c>infinity</c>, <c>nan</c> in any letter case, with
    /// whitespace at both ends. Text that does not parse throws <see cref="ArgumentException"/>
    /// (<c>ValueError</c>). A value too large for a double is an infinity, as in Python.
    /// </summary>
    internal static double Float(string text)
    {
        var s = NormalizeNumberText(text);
        var i = 0;
        var negative = false;
        if (i < s.Length && s[i] is '+' or '-')
        {
            negative = s[i] == '-';
            i++;
        }

        var rest = s[i..].ToLowerInvariant();
        if (rest is "inf" or "infinity")
        {
            return negative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        if (rest == "nan")
        {
            return double.NaN;
        }

        var sb = new StringBuilder();
        var integer = ReadDigits(s, ref i);
        string? fraction = null;
        if (i < s.Length && s[i] == '.')
        {
            i++;
            fraction = ReadDigits(s, ref i) ?? string.Empty;
        }

        if (integer is null && string.IsNullOrEmpty(fraction))
        {
            throw NotAFloat(text);
        }

        sb.Append(integer ?? "0");
        if (!string.IsNullOrEmpty(fraction))
        {
            sb.Append('.').Append(fraction);
        }

        if (i < s.Length && s[i] is 'e' or 'E')
        {
            i++;
            var expSign = string.Empty;
            if (i < s.Length && s[i] is '+' or '-')
            {
                expSign = s[i].ToString();
                i++;
            }

            var exponent = ReadDigits(s, ref i) ?? throw NotAFloat(text);
            sb.Append('e').Append(expSign).Append(exponent);
        }

        if (i != s.Length)
        {
            throw NotAFloat(text);
        }

        var value = double.Parse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return negative ? -value : value;
    }

    /// <summary>
    /// CPython formats this message with <c>%.200R</c>: the repr is cut after 200 code points, with no
    /// closing quote when it is longer. <c>float()</c> has no such cut.
    /// </summary>
    private static ArgumentException InvalidIntLiteral(string text)
    {
        var shown = PyText.ToCodePoints(ReprStr(text));
        var cut = shown.Length > 200 ? shown[..200] : shown;
        return new ArgumentException("invalid literal for int() with base 10: " + PyText.FromCodePoints(cut));
    }

    private static ArgumentException NotAFloat(string text) =>
        new($"could not convert string to float: {ReprStr(text)}");

    /// <summary>The default limit of <c>sys.get_int_max_str_digits()</c> in Python 3.12.</summary>
    private const int MaxIntStrDigits = 4300;

    /// <summary>
    /// <c>repr(s)</c> for a string: single quotes unless the text holds a single quote and no double
    /// quote, escapes for the backslash, the quote, tab, line feed, carriage return, and each code
    /// point that <c>str.isprintable()</c> rejects.
    /// </summary>
    internal static string ReprStr(string s)
    {
        var cps = PyText.ToCodePoints(s);
        var quote = cps.Contains('\'') && !cps.Contains('"') ? '"' : '\'';
        var sb = new StringBuilder();
        sb.Append(quote);
        foreach (var cp in cps)
        {
            if (cp == quote || cp == '\\')
            {
                sb.Append('\\').Append((char)cp);
            }
            else if (cp == '\t')
            {
                sb.Append("\\t");
            }
            else if (cp == '\n')
            {
                sb.Append("\\n");
            }
            else if (cp == '\r')
            {
                sb.Append("\\r");
            }
            else if (cp is >= 0x20 and < 0x7F || !PyText.IsNonPrintable(cp))
            {
                sb.Append(PyText.FromCodePoints([cp]));
            }
            else if (cp < 0x100)
            {
                sb.Append(CultureInfo.InvariantCulture, $"\\x{cp:x2}");
            }
            else if (cp < 0x10000)
            {
                sb.Append(CultureInfo.InvariantCulture, $"\\u{cp:x4}");
            }
            else
            {
                sb.Append(CultureInfo.InvariantCulture, $"\\U{cp:x8}");
            }
        }

        return sb.Append(quote).ToString();
    }
}
