namespace Graphiti.PyCompat;

using System.Globalization;
using System.Numerics;

/// <summary>
/// The parsers of pydantic-core and its date library <c>speedate</c> that the field readers use: int, float, and
/// bool text, and the datetime of a text, a number, or <c>bytes</c>. Each rule comes from the values that pydantic
/// recorded (port/tools/generate_pydantic_values.py).
/// </summary>
internal static partial class Pydantic
{
    private const string DatetimeType = "Input should be a valid datetime";
    private const long MsWatershed = 20_000_000_000;
    private const long Unix0000 = -62_167_219_200;
    private const long Unix9999 = 253_402_300_799;
    private const int MaxIntTextLength = 4300;
    private const string TooSmall = "dates before 0000 are not supported as unix timestamps";
    private const string TooLarge = "dates after 9999 are not supported as unix timestamps";

    private static readonly string[] TrueWords = ["1", "on", "t", "true", "y", "yes"];
    private static readonly string[] FalseWords = ["0", "off", "f", "false", "n", "no"];

    /// <summary>
    /// A <c>datetime</c> field: a datetime as it is, a date at midnight, an ISO 8601 text or <c>bytes</c> by the
    /// rules of <c>speedate</c>, or a Unix time as an int, a float, or a text. A Unix time above 2e10 in absolute
    /// value is in milliseconds. A Unix time gives a UTC datetime. The rules differ from <c>datetime.fromisoformat</c>.
    /// </summary>
    /// <remarks>
    /// A known difference: speedate reads a digit text of 20 digits or more with wrapping 64-bit arithmetic, so
    /// <c>"18446744073709551617"</c> gives <c>1970-01-01T00:00:01Z</c> in Python. The port reads such a text as no
    /// number and throws <see cref="ValidationException"/>. No upstream reply field is a <c>datetime</c>.
    /// </remarks>
    internal static PyDateTime DateTime(object? value) => value switch
    {
        PyDateTime dt => dt,
        DateOnly d => new PyDateTime(d.Year, d.Month, d.Day),
        bool => throw Error("datetime_type", DatetimeType),
        long l => FromTimestamp(l, 0, "datetime_parsing"),
        BigInteger big => throw DatetimeParsing(big.Sign > 0 ? TooLarge : TooSmall),
        double d => FromFloat(d),
        string s => DateTimeOfBytes(EncodeStrict(s) ?? throw Error("string_unicode", StringUnicode)),
        byte[] b => DateTimeOfBytes(b),
        _ => throw Error("datetime_type", DatetimeType),
    };

    private static long IntOfFloat(double d)
    {
        if (!double.IsFinite(d))
        {
            throw Error("finite_number", "Input should be a finite number");
        }

        if (Math.Truncate(d) != d)
        {
            throw Error("int_from_float", "Input should be a valid integer, got a number with a fractional part");
        }

        return Math.Abs(d) >= TwoPow63 ? throw Error("int_parsing_size", IntParsingSize) : (long)d;
    }

    /// <summary>
    /// pydantic-core's int text: the text without Unicode white space around it and without a fraction of zeros,
    /// an optional sign, and ASCII digits with single <c>_</c> between digits. More than 4300 characters is too big.
    /// </summary>
    private static long IntOfText(string text)
    {
        var t = text.Trim();
        var dot = t.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0)
        {
            var fraction = t[(dot + 1)..];
            if (fraction.Length == 0 || fraction.Any(c => c != '0'))
            {
                throw Error("int_parsing", IntParsing);
            }

            t = t[..dot];
        }

        var start = t.Length > 0 && t[0] is '+' or '-' ? 1 : 0;
        if (!IsDigitsWithUnderscores(t.AsSpan(start)))
        {
            throw Error("int_parsing", IntParsing);
        }

        if (t.Length > MaxIntTextLength)
        {
            throw Error("int_parsing_size", IntParsingSize);
        }

        var n = BigInteger.Parse(t.Replace("_", string.Empty, StringComparison.Ordinal), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        return n >= long.MinValue && n <= long.MaxValue ? (long)n : throw TooBig();
    }

    private static bool IsDigitsWithUnderscores(ReadOnlySpan<char> s)
    {
        if (s.Length == 0 || !char.IsAsciiDigit(s[0]) || !char.IsAsciiDigit(s[^1]))
        {
            return false;
        }

        for (var i = 1; i < s.Length; i++)
        {
            if (!char.IsAsciiDigit(s[i]) && !(s[i] == '_' && char.IsAsciiDigit(s[i - 1])))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// pydantic-core's float text: the text without Unicode white space around it, with <c>_</c> removed if it does
    /// not start or end the text or stand twice, then Rust's <c>f64</c> grammar: <c>inf</c>, <c>infinity</c>, and
    /// <c>nan</c> in any case, or a decimal with an optional exponent.
    /// </summary>
    private static double FloatOfText(string text)
    {
        var t = text.Trim();
        if (t.StartsWith('_') || t.EndsWith('_') || t.Contains("__", StringComparison.Ordinal))
        {
            throw Error("float_parsing", FloatParsing);
        }

        t = t.Replace("_", string.Empty, StringComparison.Ordinal);
        return RustFloat(t) ?? throw Error("float_parsing", FloatParsing);
    }

    /// <summary>Rust's <c>str::parse::&lt;f64&gt;</c>: null for a text that is not a float.</summary>
    private static double? RustFloat(string t)
    {
        var sign = t.Length > 0 && t[0] is '+' or '-' ? 1 : 0;
        var body = t[sign..];
        var negative = sign == 1 && t[0] == '-';
        if (body.Equals("inf", StringComparison.OrdinalIgnoreCase) || body.Equals("infinity", StringComparison.OrdinalIgnoreCase))
        {
            return negative ? double.NegativeInfinity : double.PositiveInfinity;
        }

        if (body.Equals("nan", StringComparison.OrdinalIgnoreCase))
        {
            return negative ? -double.NaN : double.NaN;
        }

        var i = 0;
        var digits = 0;
        while (i < body.Length && char.IsAsciiDigit(body[i]))
        {
            i++;
            digits++;
        }

        if (i < body.Length && body[i] == '.')
        {
            i++;
            while (i < body.Length && char.IsAsciiDigit(body[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return null;
        }

        if (i < body.Length && body[i] is 'e' or 'E')
        {
            i++;
            if (i < body.Length && body[i] is '+' or '-')
            {
                i++;
            }

            var exponentDigits = 0;
            while (i < body.Length && char.IsAsciiDigit(body[i]))
            {
                i++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
            {
                return null;
            }
        }

        return i == body.Length ? double.Parse(t, NumberStyles.Float, CultureInfo.InvariantCulture) : null;
    }

    private static bool BoolOfText(string s)
    {
        foreach (var word in TrueWords)
        {
            if (AsciiEqualsIgnoreCase(s, word))
            {
                return true;
            }
        }

        foreach (var word in FalseWords)
        {
            if (AsciiEqualsIgnoreCase(s, word))
            {
                return false;
            }
        }

        throw Error("bool_parsing", BoolParsing);
    }

    private static bool AsciiEqualsIgnoreCase(string s, string lowerWord)
    {
        if (s.Length != lowerWord.Length)
        {
            return false;
        }

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i] is >= 'A' and <= 'Z' ? (char)(s[i] + 32) : s[i];
            if (c != lowerWord[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// pydantic-core's <c>float_as_datetime</c>: the floor of the value is the Unix time, and the fraction, in
    /// milliseconds above the watershed, gives the microseconds, rounded half away from zero.
    /// </summary>
    private static PyDateTime FromFloat(double d)
    {
        if (double.IsNaN(d))
        {
            throw DatetimeParsing("NaN values not permitted");
        }

        var micros = Math.Abs(d - Math.Truncate(d)) * 1_000_000.0;
        if (Math.Abs(d) > MsWatershed)
        {
            micros /= 1000.0;
        }

        return FromTimestamp(SaturatingLong(Math.Floor(d)), SaturatingUInt(Math.Round(micros, MidpointRounding.AwayFromZero)), "datetime_parsing");
    }

    /// <summary>
    /// speedate's <c>DateTime::from_timestamp</c>: a value above 2e10 in absolute value is in milliseconds. The
    /// result is UTC. Year 0 passes speedate and fails in Python's <c>datetime</c>.
    /// </summary>
    private static PyDateTime FromTimestamp(long timestamp, uint microsecond, string errorType)
    {
        var (seconds, micros) = Seconds(timestamp, microsecond, errorType);
        return MakeDateTime(seconds, micros, 0);
    }

    private static (long Seconds, long Micros) Seconds(long timestamp, uint microsecond, string errorType)
    {
        if (timestamp == long.MinValue)
        {
            throw Error(errorType, Prefix(errorType) + TooSmall);
        }

        long seconds = timestamp;
        long micros = microsecond;
        if (Math.Abs(timestamp) > MsWatershed)
        {
            seconds = timestamp / 1000;
            var extra = timestamp % 1000 * 1000;
            if (extra < 0)
            {
                seconds--;
                extra += 1_000_000;
            }

            micros += extra;
        }

        if (micros >= 1_000_000)
        {
            seconds += micros / 1_000_000;
            micros %= 1_000_000;
        }

        if (seconds < Unix0000)
        {
            throw Error(errorType, Prefix(errorType) + TooSmall);
        }

        return seconds > Unix9999 ? throw Error(errorType, Prefix(errorType) + TooLarge) : (seconds, micros);
    }

    /// <summary>The Python <c>datetime</c> of a Unix time in seconds and microseconds, with a UTC offset or naive.</summary>
    private static PyDateTime MakeDateTime(long seconds, long micros, long? offsetMicros)
    {
        var days = Math.DivRem(seconds, 86_400L, out var secondOfDay);
        if (secondOfDay < 0)
        {
            days--;
            secondOfDay += 86_400;
        }

        var (year, month, day) = CivilFromDays(days);
        return Make(year, month, day, (int)(secondOfDay / 3600), (int)(secondOfDay / 60 % 60), (int)(secondOfDay % 60), (int)micros, offsetMicros);
    }

    /// <summary>The proleptic Gregorian date of a day count since 1970-01-01 (Howard Hinnant's algorithm).</summary>
    private static (int Year, int Month, int Day) CivilFromDays(long z)
    {
        z += 719_468;
        var era = (z >= 0 ? z : z - 146_096) / 146_097;
        var doe = z - (era * 146_097);
        var yoe = (doe - (doe / 1460) + (doe / 36_524) - (doe / 146_096)) / 365;
        var y = yoe + (era * 400);
        var doy = doe - ((365 * yoe) + (yoe / 4) - (yoe / 100));
        var mp = ((5 * doy) + 2) / 153;
        var d = doy - (((153 * mp) + 2) / 5) + 1;
        var m = mp < 10 ? mp + 3 : mp - 9;
        return ((int)(m <= 2 ? y + 1 : y), (int)m, (int)d);
    }

    /// <summary>Python's <c>datetime(...)</c>: an error such as <c>year 0 is out of range</c> is <c>datetime_parsing</c>.</summary>
    private static PyDateTime Make(int year, int month, int day, int hour, int minute, int second, int micros, long? offsetMicros)
    {
        try
        {
            return new PyDateTime(year, month, day, hour, minute, second, micros, offsetMicros);
        }
        catch (ArgumentException e)
        {
            throw DatetimeParsing(e.Message);
        }
    }

    /// <summary>
    /// pydantic-core's datetime of a text: speedate's RFC 3339 datetime, else a Unix time if the text is a number.
    /// If both fail, the text is parsed as a date, and the error of the date is reported.
    /// </summary>
    private static PyDateTime DateTimeOfBytes(byte[] b)
    {
        if (ParseRfc3339(b) is { } parts)
        {
            return Make(parts.Year, parts.Month, parts.Day, parts.Hour, parts.Minute, parts.Second, parts.Micros, parts.Offset);
        }

        const string FromDate = "datetime_from_date_parsing";
        var number = NumberOfBytes(b);
        (long Seconds, long Micros)? time = null;
        try
        {
            time = number switch
            {
                long l => Seconds(l, 0, FromDate),
                double f => TextFloatSeconds(f, FromDate),
                _ => null,
            };
        }
        catch (ValidationException)
        {
            // PORT: speedate parses the text again as a date, which reports the error below.
        }

        if (time is { } t)
        {
            return MakeDateTime(t.Seconds, t.Micros, 0);
        }

        var date = ParseDate(b, out var error);
        if (date is { } d)
        {
            return Make(d.Year, d.Month, d.Day, 0, 0, 0, 0, null);
        }

        // speedate's Date::parse_bytes tries an int after the RFC 3339 date. Its range error replaces the date error.
        // A float keeps the date error (recorded: "1.7976931348623157e308" gives "invalid character in year").
        if (number is long n)
        {
            Seconds(n, 0, FromDate);
        }

        throw Error(FromDate, Prefix(FromDate) + error);
    }

    /// <summary>
    /// speedate's float Unix time of a text: in milliseconds above the watershed, divided by 1000 first, then the
    /// floor and the rounded microseconds.
    /// </summary>
    private static (long Seconds, long Micros) TextFloatSeconds(double f, string errorType)
    {
        var normalized = Math.Abs(f) > MsWatershed ? f / 1000.0 : f;
        var seconds = SaturatingLong(Math.Floor(normalized));
        var micros = SaturatingUInt(Math.Round((normalized - seconds) * 1_000_000.0, MidpointRounding.AwayFromZero));
        return Seconds(seconds, micros, errorType);
    }

    /// <summary>speedate's <c>float_parse_bytes</c>: a text with a <c>.</c> is a Rust float, else a signed 64-bit int, else neither.</summary>
    private static object? NumberOfBytes(byte[] b)
    {
        if (Array.IndexOf(b, (byte)'.') >= 0)
        {
            var text = DecodeStrict(b);
            return text is null ? null : RustFloat(text);
        }

        var i = b.Length > 0 && b[0] is (byte)'+' or (byte)'-' ? 1 : 0;
        if (i == b.Length)
        {
            return null;
        }

        BigInteger n = 0;
        for (var j = i; j < b.Length; j++)
        {
            if (b[j] is < (byte)'0' or > (byte)'9')
            {
                return null;
            }

            n = (n * 10) + (b[j] - '0');
            if (n > long.MaxValue)
            {
                // speedate parses the digits as a positive i64, so -9223372036854775808 is not a number either.
                return null;
            }
        }

        return (long)(b[0] == '-' ? -n : n);
    }

    /// <summary>speedate's RFC 3339 date of exactly 10 bytes, with the error text of speedate.</summary>
    private static (int Year, int Month, int Day)? ParseDate(byte[] b, out string error)
    {
        var date = ParseDatePart(b, out error);
        if (date is null)
        {
            return null;
        }

        if (b.Length > 10)
        {
            error = "unexpected extra characters at the end of the input";
            return null;
        }

        return date;
    }

    private static (int Year, int Month, int Day)? ParseDatePart(byte[] b, out string error)
    {
        error = string.Empty;
        if (b.Length < 10)
        {
            error = "input is too short";
            return null;
        }

        if (Digits(b, 0, 4) is not { } year)
        {
            error = "invalid character in year";
            return null;
        }

        if (b[4] != '-')
        {
            error = "invalid date separator, expected `-`";
            return null;
        }

        if (Digits(b, 5, 2) is not { } month)
        {
            error = "invalid character in month";
            return null;
        }

        if (b[7] != '-')
        {
            error = "invalid date separator, expected `-`";
            return null;
        }

        if (Digits(b, 8, 2) is not { } day)
        {
            error = "invalid character in day";
            return null;
        }

        if (month is < 1 or > 12)
        {
            error = "month value is outside expected range of 1-12";
            return null;
        }

        if (day < 1 || day > DaysInMonth(year, month))
        {
            error = "day value is outside expected range";
            return null;
        }

        return (year, month, day);
    }

    private static int DaysInMonth(int year, int month) => month switch
    {
        2 => (year % 4 == 0 && year % 100 != 0) || year % 400 == 0 ? 29 : 28,
        4 or 6 or 9 or 11 => 30,
        _ => 31,
    };

    /// <summary>
    /// speedate's RFC 3339 datetime: a date, a separator <c>T</c>, <c>t</c>, a space, or <c>_</c>, then
    /// <c>HH:MM</c>, optional <c>:SS</c> with an optional fraction after <c>.</c> or <c>,</c> (cut to 6 digits),
    /// and an optional <c>Z</c>, <c>z</c>, or offset <c>±HH:MM</c> or <c>±HHMM</c>. Null if the text is not one.
    /// </summary>
    private static (int Year, int Month, int Day, int Hour, int Minute, int Second, int Micros, long? Offset)? ParseRfc3339(byte[] b)
    {
        if (ParseDatePart(b, out _) is not { } date || b.Length < 11 || b[10] is not ((byte)'T' or (byte)'t' or (byte)' ' or (byte)'_'))
        {
            return null;
        }

        var p = 11;
        if (Digits(b, p, 2) is not { } hour || hour > 23 || At(b, p + 2) != ':' || Digits(b, p + 3, 2) is not { } minute || minute > 59)
        {
            return null;
        }

        p += 5;
        var second = 0;
        var micros = 0;
        if (At(b, p) == ':')
        {
            if (Digits(b, p + 1, 2) is not { } s || s > 59)
            {
                return null;
            }

            second = s;
            p += 3;
            if (At(b, p) is '.' or ',')
            {
                p++;
                var start = p;
                while (p < b.Length && b[p] is >= (byte)'0' and <= (byte)'9')
                {
                    if (p - start < 6)
                    {
                        micros = (micros * 10) + (b[p] - '0');
                    }

                    p++;
                }

                if (p == start)
                {
                    return null;
                }

                for (var n = p - start; n < 6; n++)
                {
                    micros *= 10;
                }
            }
        }

        long? offset = null;
        if (At(b, p) is 'Z' or 'z')
        {
            offset = 0;
            p++;
        }
        else if (At(b, p) is '+' or '-')
        {
            var sign = b[p] == '-' ? -1 : 1;
            if (Digits(b, p + 1, 2) is not { } oh)
            {
                return null;
            }

            p += 3;
            if (At(b, p) == ':')
            {
                p++;
            }

            if (Digits(b, p, 2) is not { } om || oh > 23 || om > 59)
            {
                return null;
            }

            p += 2;
            offset = sign * ((oh * 3600L) + (om * 60L)) * 1_000_000L;
        }

        return p == b.Length ? (date.Year, date.Month, date.Day, hour, minute, second, micros, offset) : null;
    }

    private static char At(byte[] b, int i) => i < b.Length ? (char)b[i] : '\0';

    private static int? Digits(byte[] b, int start, int count)
    {
        if (start + count > b.Length)
        {
            return null;
        }

        var n = 0;
        for (var i = start; i < start + count; i++)
        {
            if (b[i] is < (byte)'0' or > (byte)'9')
            {
                return null;
            }

            n = (n * 10) + (b[i] - '0');
        }

        return n;
    }

    private static long SaturatingLong(double d) =>
        double.IsNaN(d) ? 0 : d >= TwoPow63 ? long.MaxValue : d <= -TwoPow63 ? long.MinValue : (long)d;

    private static uint SaturatingUInt(double d) =>
        double.IsNaN(d) || d <= 0 ? 0 : d >= uint.MaxValue ? uint.MaxValue : (uint)d;

    private static string Prefix(string errorType) =>
        errorType == "datetime_parsing" ? "Input should be a valid datetime, " : "Input should be a valid datetime or date, ";

    private static ValidationException DatetimeParsing(string reason) => Error("datetime_parsing", Prefix("datetime_parsing") + reason);
}
