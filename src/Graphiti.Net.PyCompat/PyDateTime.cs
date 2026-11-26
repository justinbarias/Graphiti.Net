namespace Graphiti.PyCompat;

using System.Globalization;
using System.Text;

/// <summary>
/// A <c>datetime</c> of Python 3.12, aware or naive, to the microsecond (R11). The value holds the date
/// and time as written and an offset from UTC. The offset is null for a naive value. A value always lies
/// between year 1 and year 9999. A result outside that range throws <see cref="OverflowException"/>
/// (<c>OverflowError</c>), and a bad field or a bad text throws <see cref="ArgumentException"/>
/// (<c>ValueError</c>) with the message of Python. The default value is the naive
/// <c>datetime(1, 1, 1)</c>.
/// </summary>
internal readonly struct PyDateTime : IEquatable<PyDateTime>, IComparable<PyDateTime>
{
    private const long MicrosPerSecond = 1_000_000L;
    private const long MicrosPerMinute = 60 * MicrosPerSecond;
    private const long MicrosPerHour = 60 * MicrosPerMinute;
    private const long MicrosPerDay = 24 * MicrosPerHour;
    private const int MaxOrdinal = 3_652_059;

    /// <summary>The microseconds of 9999-12-31T23:59:59.999999 after 0001-01-01T00:00:00.</summary>
    private const long MaxMicros = (MaxOrdinal * MicrosPerDay) - 1;

    /// <summary>The field values of <c>datetime.min</c> and the offset limit of <c>timezone</c> in microseconds.</summary>
    private const long OffsetLimit = MicrosPerDay;

    private static readonly int[] DaysBeforeMonth = [-1, 0, 31, 59, 90, 120, 151, 181, 212, 243, 273, 304, 334];
    private static readonly int[] DaysInMonthTable = [-1, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    /// <summary>The microseconds from 0001-01-01T00:00:00 to the clock fields. The offset is not applied.</summary>
    private readonly long _micros;

    private readonly long _offset;
    private readonly bool _aware;

    private PyDateTime(long micros, bool aware, long offset)
    {
        _micros = micros;
        _aware = aware;
        _offset = aware ? offset : 0;
    }

    /// <summary>
    /// <c>datetime(year, month, day, hour, minute, second, microsecond, tzinfo)</c>. The offset is the
    /// <c>utcoffset</c> in microseconds, and null for a naive value. A field out of range throws
    /// <see cref="ArgumentException"/> with the message of Python.
    /// </summary>
    internal PyDateTime(int year, int month, int day, int hour = 0, int minute = 0, int second = 0, int microsecond = 0, long? offsetMicroseconds = null)
    {
        _aware = offsetMicroseconds is not null;
        _offset = offsetMicroseconds ?? 0;
        if (_aware)
        {
            CheckOffset(_offset);
        }

        _micros = CheckedMicros(year, month, day, hour, minute, second, microsecond);
    }

    /// <summary>The year, 1 to 9999.</summary>
    internal int Year => Fields().Year;

    /// <summary>The month, 1 to 12.</summary>
    internal int Month => Fields().Month;

    /// <summary>The day of the month.</summary>
    internal int Day => Fields().Day;

    /// <summary>The hour, 0 to 23.</summary>
    internal int Hour => (int)(_micros % MicrosPerDay / MicrosPerHour);

    /// <summary>The minute, 0 to 59.</summary>
    internal int Minute => (int)(_micros % MicrosPerHour / MicrosPerMinute);

    /// <summary>The second, 0 to 59.</summary>
    internal int Second => (int)(_micros % MicrosPerMinute / MicrosPerSecond);

    /// <summary>The microsecond, 0 to 999999.</summary>
    internal int Microsecond => (int)(_micros % MicrosPerSecond);

    /// <summary><c>dt.utcoffset()</c> in microseconds, or null for a naive value.</summary>
    internal long? OffsetMicroseconds => _aware ? _offset : null;

    /// <summary><c>dt.tzinfo is None</c>.</summary>
    internal bool IsNaive => !_aware;

    /// <summary><c>dt.tzinfo == timezone.utc</c>: an aware value with an offset of zero. A naive value gives false.</summary>
    internal bool IsUtc => _aware && _offset == 0;

    /// <summary>The instant of an aware value, or the clock fields of a naive value, in microseconds. Never overflows.</summary>
    private long Key => _micros - _offset;

    // ---- calendar arithmetic ----

    private static bool IsLeap(int year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);

    private static int DaysInMonth(int year, int month) =>
        month == 2 && IsLeap(year) ? 29 : DaysInMonthTable[month];

    private static int DaysBeforeYear(int year)
    {
        var y = year - 1;
        return (y * 365) + (y / 4) - (y / 100) + (y / 400);
    }

    private static int DaysBeforeMonthOf(int year, int month) =>
        DaysBeforeMonth[month] + (month > 2 && IsLeap(year) ? 1 : 0);

    /// <summary><c>date.toordinal()</c>: January 1 of year 1 is 1.</summary>
    private static int YmdToOrdinal(int year, int month, int day) =>
        DaysBeforeYear(year) + DaysBeforeMonthOf(year, month) + day;

    /// <summary>The inverse of <see cref="YmdToOrdinal"/>. The year can be 10000 for an ordinal past the limit.</summary>
    private static (int Year, int Month, int Day) OrdinalToYmd(int ordinal)
    {
        var n = ordinal - 1;
        var n400 = Math.DivRem(n, 146097, out n);
        var year = (n400 * 400) + 1;
        var n100 = Math.DivRem(n, 36524, out n);
        var n4 = Math.DivRem(n, 1461, out n);
        var n1 = Math.DivRem(n, 365, out n);
        year += (n100 * 100) + (n4 * 4) + n1;
        if (n1 == 4 || n100 == 4)
        {
            return (year - 1, 12, 31);
        }

        var leap = n1 == 3 && (n4 != 24 || n100 == 3);
        var month = (n + 50) >> 5;
        var preceding = DaysBeforeMonth[month] + (month > 2 && leap ? 1 : 0);
        if (preceding > n)
        {
            month--;
            preceding -= DaysInMonthTable[month] + (month == 2 && leap ? 1 : 0);
        }

        return (year, month, n - preceding + 1);
    }

    /// <summary>The weekday of an ordinal, Monday is 0 as in <c>date.weekday()</c>.</summary>
    private static int WeekdayOf(int ordinal) => (ordinal + 6) % 7;

    private (int Year, int Month, int Day) Fields()
    {
        var ordinal = (int)(_micros / MicrosPerDay) + 1;
        return OrdinalToYmd(ordinal);
    }

    private static long CheckedMicros(int year, int month, int day, int hour, int minute, int second, int microsecond)
    {
        if (year is < 1 or > 9999)
        {
            throw new ArgumentException($"year {year.ToString(CultureInfo.InvariantCulture)} is out of range");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentException("month must be in 1..12");
        }

        if (day < 1 || day > DaysInMonth(year, month))
        {
            throw new ArgumentException("day is out of range for month");
        }

        if (hour is < 0 or > 23)
        {
            throw new ArgumentException("hour must be in 0..23");
        }

        if (minute is < 0 or > 59)
        {
            throw new ArgumentException("minute must be in 0..59");
        }

        if (second is < 0 or > 59)
        {
            throw new ArgumentException("second must be in 0..59");
        }

        if (microsecond is < 0 or > 999_999)
        {
            throw new ArgumentException("microsecond must be in 0..999999");
        }

        return ((YmdToOrdinal(year, month, day) - 1) * MicrosPerDay) + (hour * MicrosPerHour) + (minute * MicrosPerMinute)
            + (second * MicrosPerSecond) + microsecond;
    }

    /// <summary>Throws the <c>ValueError</c> of <c>timezone(timedelta(...))</c> for an offset of one day or more.</summary>
    private static void CheckOffset(long offsetMicros)
    {
        if (offsetMicros <= -OffsetLimit || offsetMicros >= OffsetLimit)
        {
            throw new ArgumentException(
                "offset must be a timedelta strictly between -timedelta(hours=24) and timedelta(hours=24), not "
                + TimedeltaRepr(offsetMicros) + ".");
        }
    }

    /// <summary><c>repr(timedelta)</c> for a duration in microseconds.</summary>
    private static string TimedeltaRepr(long micros)
    {
        var days = Math.DivRem(micros, MicrosPerDay, out var rest);
        if (rest < 0)
        {
            rest += MicrosPerDay;
            days--;
        }

        var seconds = rest / MicrosPerSecond;
        var microseconds = rest % MicrosPerSecond;
        var parts = new List<string>();
        if (days != 0)
        {
            parts.Add($"days={days.ToString(CultureInfo.InvariantCulture)}");
        }

        if (seconds != 0)
        {
            parts.Add($"seconds={seconds.ToString(CultureInfo.InvariantCulture)}");
        }

        if (microseconds != 0)
        {
            parts.Add($"microseconds={microseconds.ToString(CultureInfo.InvariantCulture)}");
        }

        return "datetime.timedelta(" + (parts.Count == 0 ? "0" : string.Join(", ", parts)) + ")";
    }

    // ---- text output ----

    private static string Pad(long value, int width) =>
        value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

    private static void AppendOffset(StringBuilder sb, long offsetMicros, string separator)
    {
        var sign = offsetMicros < 0 ? '-' : '+';
        var abs = Math.Abs(offsetMicros);
        var hours = abs / MicrosPerHour;
        var minutes = abs % MicrosPerHour / MicrosPerMinute;
        var seconds = abs % MicrosPerMinute / MicrosPerSecond;
        var micros = abs % MicrosPerSecond;
        sb.Append(sign).Append(Pad(hours, 2)).Append(separator).Append(Pad(minutes, 2));
        if (seconds != 0 || micros != 0)
        {
            sb.Append(separator).Append(Pad(seconds, 2));
            if (micros != 0)
            {
                sb.Append('.').Append(Pad(micros, 6));
            }
        }
    }

    private string Format(char separator)
    {
        var (year, month, day) = Fields();
        var sb = new StringBuilder(32);
        sb.Append(Pad(year, 4)).Append('-').Append(Pad(month, 2)).Append('-').Append(Pad(day, 2)).Append(separator)
            .Append(Pad(Hour, 2)).Append(':').Append(Pad(Minute, 2)).Append(':').Append(Pad(Second, 2));
        if (Microsecond != 0)
        {
            sb.Append('.').Append(Pad(Microsecond, 6));
        }

        if (_aware)
        {
            AppendOffset(sb, _offset, ":");
        }

        return sb.ToString();
    }

    /// <summary>
    /// <c>dt.isoformat()</c>. A fraction of 6 digits only if the microseconds are not zero. An aware value ends with
    /// <c>+00:00</c>, never <c>Z</c>. Seconds in the offset appear only if the offset has them.
    /// </summary>
    internal string IsoFormat() => Format('T');

    /// <summary><c>str(dt)</c> and <c>f'{dt}'</c>: <c>isoformat(sep=' ')</c>.</summary>
    internal string Str() => Format(' ');

    /// <summary>
    /// <c>repr(dt)</c>, for example <c>datetime.datetime(2025, 1, 3, 0, 0, tzinfo=datetime.timezone.utc)</c>.
    /// Python writes the second only if it or the microsecond is not zero, and the microsecond only if it is not zero.
    /// </summary>
    internal string Repr()
    {
        var (year, month, day) = Fields();
        var sb = new StringBuilder("datetime.datetime(");
        sb.Append(CultureInfo.InvariantCulture, $"{year}, {month}, {day}, {Hour}, {Minute}");
        if (Microsecond != 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $", {Second}, {Microsecond}");
        }
        else if (Second != 0)
        {
            sb.Append(CultureInfo.InvariantCulture, $", {Second}");
        }

        if (_aware)
        {
            sb.Append(", tzinfo=").Append(_offset == 0 ? "datetime.timezone.utc" : "datetime.timezone(" + TimedeltaRepr(_offset) + ")");
        }

        return sb.Append(')').ToString();
    }

    // ---- UTC ----

    /// <summary><c>dt.replace(tzinfo=timezone.utc)</c>: the same fields with an offset of zero. A naive value becomes aware.</summary>
    internal PyDateTime ReplaceUtc() => new(_micros, true, 0);

    /// <summary>
    /// <c>dt.astimezone(timezone.utc)</c> for an aware value. A result outside year 1 to 9999 throws
    /// <see cref="OverflowException"/> with the message of Python. For a naive value Python reads the clock
    /// fields as local time of the system, and neither R11 nor GAPS G4 say what the port does there. The
    /// port throws <see cref="InvalidOperationException"/> and needs a decision (upstream
    /// <c>ensure_utc</c> never calls <c>astimezone</c> on a naive value).
    /// </summary>
    internal PyDateTime ToUtc()
    {
        if (!_aware)
        {
            throw new InvalidOperationException("ToUtc of a naive value needs the system time zone, and the rulebook does not say what the port does");
        }

        var instant = Key;
        if (instant is < 0 or > MaxMicros)
        {
            throw new OverflowException("date value out of range");
        }

        return new PyDateTime(instant, true, 0);
    }

    // ---- DateTimeOffset and the clock ----

    /// <summary>
    /// A value at the driver boundary or from a <see cref="TimeProvider"/>. The clock fields and the offset are kept. The
    /// 100 ns ticks are cut to microseconds, as the conversion of <c>neo4j.time.DateTime.to_native()</c> cuts nanoseconds.
    /// </summary>
    internal static PyDateTime FromDateTimeOffset(DateTimeOffset value) =>
        new(value.Ticks / 10, true, value.Offset.Ticks / 10);

    /// <summary>
    /// The value as a <see cref="DateTimeOffset"/>. A naive value is read as UTC (decision D36). An offset that
    /// <see cref="DateTimeOffset"/> cannot hold (seconds, or more than 14 hours) is replaced by UTC at the same instant.
    /// A result outside year 1 to 9999 throws <see cref="OverflowException"/>.
    /// </summary>
    internal DateTimeOffset ToDateTimeOffset()
    {
        var value = this;
        if (_aware && (_offset % MicrosPerMinute != 0 || Math.Abs(_offset) > 14 * MicrosPerHour))
        {
            value = ToUtc();
        }

        try
        {
            return new DateTimeOffset(value._micros * 10, TimeSpan.FromTicks(value._offset * 10));
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new OverflowException("date value out of range", e);
        }
    }

    /// <summary><c>datetime.now(timezone.utc)</c>, read from the provider and cut to microseconds.</summary>
    internal static PyDateTime UtcNow(TimeProvider provider) => FromDateTimeOffset(provider.GetUtcNow().ToUniversalTime());

    // ---- comparison ----

    /// <summary>
    /// <c>a == b</c>. Two aware values are equal if they are the same instant, and two naive values if the fields are the
    /// same. A naive value and an aware value are never equal.
    /// </summary>
    public bool Equals(PyDateTime other) => _aware == other._aware && Key == other.Key;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PyDateTime other && Equals(other);

    /// <summary>A hash that agrees with <see cref="Equals(PyDateTime)"/>.</summary>
    public override int GetHashCode() => HashCode.Combine(_aware, Key);

    /// <summary>The order of Python. A naive value and an aware value throw <see cref="InvalidCastException"/> (<c>TypeError</c>).</summary>
    public int CompareTo(PyDateTime other)
    {
        if (_aware != other._aware)
        {
            throw new InvalidCastException("can't compare offset-naive and offset-aware datetimes");
        }

        return Key.CompareTo(other.Key);
    }

    /// <summary><c>a == b</c>.</summary>
    public static bool operator ==(PyDateTime a, PyDateTime b) => a.Equals(b);

    /// <summary><c>a != b</c>.</summary>
    public static bool operator !=(PyDateTime a, PyDateTime b) => !a.Equals(b);

    /// <summary><c>a &lt; b</c>.</summary>
    public static bool operator <(PyDateTime a, PyDateTime b) => a.CompareTo(b) < 0;

    /// <summary><c>a &gt; b</c>.</summary>
    public static bool operator >(PyDateTime a, PyDateTime b) => a.CompareTo(b) > 0;

    /// <summary><c>a &lt;= b</c>.</summary>
    public static bool operator <=(PyDateTime a, PyDateTime b) => a.CompareTo(b) <= 0;

    /// <summary><c>a &gt;= b</c>.</summary>
    public static bool operator >=(PyDateTime a, PyDateTime b) => a.CompareTo(b) >= 0;

    // ---- fromisoformat ----

    /// <summary>The text that Python shows for a bad ISO string.</summary>
    private static ArgumentException InvalidIso(string text) =>
        new($"Invalid isoformat string: {Py.ReprStr(text)}");

    private static bool IsAsciiDigit(int b) => b is >= '0' and <= '9';

    /// <summary>
    /// <c>datetime.fromisoformat(text)</c> with the rules of Python 3.12. The date is <c>YYYY-MM-DD</c>, <c>YYYYMMDD</c>,
    /// <c>YYYY-Www</c>, <c>YYYY-Www-D</c>, <c>YYYYWww</c>, or <c>YYYYWwwD</c>. Any one character separates the date and the time.
    /// The time is <c>HH[:MM[:SS[.f]]]</c> with a comma or a point, 1 digit or more in the fraction (6 are used), and the
    /// colons are all there or all absent. A zone is <c>Z</c> or a sign and a time of the same form. Only ASCII digits count.
    /// Python reads the UTF-8 bytes of the text, and so does this method. Text that does not parse throws
    /// <see cref="ArgumentException"/>.
    /// </summary>
    internal static PyDateTime FromIsoFormat(string text)
    {
        // Python tests the length in code points. A lone surrogate is allowed in one place, the separator, at the
        // position 7, 8, or 10. Python replaces the first one that it finds with T. Any other lone surrogate fails the
        // conversion to UTF-8.
        var cps = PyText.ToCodePoints(text);
        if (cps.Length < 7)
        {
            throw InvalidIso(text);
        }

        foreach (var position in new[] { 7, 8, 10 })
        {
            if (position > cps.Length)
            {
                break;
            }

            if (position < cps.Length && cps[position] is >= 0xD800 and <= 0xDFFF)
            {
                cps[position] = 'T';
                break;
            }
        }

        byte[] bytes;
        try
        {
            bytes = [.. StrictUtf8.GetBytes(PyText.FromCodePoints(cps)), 0];
        }
        catch (EncoderFallbackException)
        {
            throw InvalidIso(text);
        }

        var reader = new IsoReader(bytes, bytes.Length - 1);
        var separator = reader.FindSeparator();
        if (separator < 0)
        {
            throw InvalidIso(text);
        }

        var rv = reader.ParseDate(separator, out var year, out var month, out var day);
        int hour = 0, minute = 0, second = 0, microsecond = 0, tzSeconds = 0, tzMicros = 0;
        if (rv == 0 && reader.Length > separator)
        {
            var start = separator + reader.CharWidthAt(separator);
            var remaining = reader.Length - start;
            rv = remaining < 0
                ? -3
                : reader.ParseTime(start, remaining, out hour, out minute, out second, out microsecond, out tzSeconds, out tzMicros);
        }

        if (rv < 0)
        {
            throw InvalidIso(text);
        }

        long? offset = null;
        if (rv == 1)
        {
            offset = tzSeconds == 0 ? 0 : (tzSeconds * MicrosPerSecond) + tzMicros;
            CheckOffset(offset.Value);
        }

        return new PyDateTime(year, month, day, hour, minute, second, microsecond, offset);
    }

    /// <summary>
    /// The C code of <c>datetime.fromisoformat</c> in Python 3.12, on the UTF-8 bytes of the text. A read past the end gives the
    /// zero byte that ends a C string. The functions return the same codes as the C functions: negative for an error.
    /// </summary>
    private readonly struct IsoReader(byte[] bytes, int length)
    {
        internal int Length { get; } = length;

        private int At(int i) => i >= 0 && i < bytes.Length ? bytes[i] : 0;

        /// <summary>The width of the UTF-8 character that starts at a byte, read from the high bits as the C code does.</summary>
        internal int CharWidthAt(int i)
        {
            var b = At(i);
            if ((b & 0x80) == 0)
            {
                return 1;
            }

            return (b & 0xF0) switch { 0xE0 => 3, 0xF0 => 4, _ => 2 };
        }

        /// <summary>The place of the character that separates the date from the time, or -1.</summary>
        internal int FindSeparator()
        {
            if (Length == 7)
            {
                return 7;
            }

            if (At(4) == '-')
            {
                if (At(5) == 'W')
                {
                    if (Length < 8)
                    {
                        return -1;
                    }

                    if (Length > 8 && At(8) == '-')
                    {
                        if (Length == 9)
                        {
                            return -1;
                        }

                        return Length > 10 && IsAsciiDigit(At(10)) ? 8 : 10;
                    }

                    return 8;
                }

                return 10;
            }

            if (At(4) == 'W')
            {
                var index = 7;
                while (index < Length && IsAsciiDigit(At(index)))
                {
                    index++;
                }

                if (index < 9)
                {
                    return index;
                }

                return index % 2 == 0 ? 7 : 8;
            }

            return 8;
        }

        private int ParseDigits(int p, int count, ref int value)
        {
            for (var i = 0; i < count; i++)
            {
                var digit = At(p++) - '0';
                if (digit is < 0 or > 9)
                {
                    return -1;
                }

                value = (value * 10) + digit;
            }

            return p;
        }

        internal int ParseDate(int length, out int year, out int month, out int day)
        {
            year = month = day = 0;
            var p = ParseDigits(0, 4, ref year);
            if (p < 0)
            {
                return -1;
            }

            var usesSeparator = At(p) == '-';
            if (usesSeparator)
            {
                p++;
            }

            if (At(p) == 'W')
            {
                p++;
                int week = 0, weekday = 0;
                p = ParseDigits(p, 2, ref week);
                if (p < 0)
                {
                    return -3;
                }

                if (p < length)
                {
                    if (usesSeparator && At(p++) != '-')
                    {
                        return -2;
                    }

                    p = ParseDigits(p, 1, ref weekday);
                    if (p < 0)
                    {
                        return -4;
                    }
                }
                else
                {
                    weekday = 1;
                }

                return IsoToYmd(year, week, weekday, out year, out month, out day) == 0 ? 0 : -3;
            }

            p = ParseDigits(p, 2, ref month);
            if (p < 0)
            {
                return -1;
            }

            if (usesSeparator && At(p++) != '-')
            {
                return -2;
            }

            p = ParseDigits(p, 2, ref day);
            return p < 0 ? -4 : 0;
        }

        private static int IsoToYmd(int isoYear, int isoWeek, int isoDay, out int year, out int month, out int day)
        {
            year = month = day = 0;
            if (isoYear is < 1 or > 9999)
            {
                return -4;
            }

            if (isoWeek <= 0 || isoWeek >= 53)
            {
                var outOfRange = true;
                if (isoWeek == 53)
                {
                    var firstWeekday = WeekdayOf(YmdToOrdinal(isoYear, 1, 1));
                    if (firstWeekday == 3 || (firstWeekday == 2 && IsLeap(isoYear)))
                    {
                        outOfRange = false;
                    }
                }

                if (outOfRange)
                {
                    return -2;
                }
            }

            if (isoDay <= 0 || isoDay >= 8)
            {
                return -3;
            }

            var firstDay = YmdToOrdinal(isoYear, 1, 1);
            var firstWeekdayOfYear = (firstDay + 6) % 7;
            var week1Monday = firstDay - firstWeekdayOfYear;
            if (firstWeekdayOfYear > 3)
            {
                week1Monday += 7;
            }

            (year, month, day) = OrdinalToYmd(week1Monday + ((isoWeek - 1) * 7) + isoDay - 1);
            return 0;
        }

        private int ParseHhMmSsFf(int start, int end, out int hour, out int minute, out int second, out int microsecond)
        {
            hour = minute = second = microsecond = 0;
            var p = start;
            var hasSeparator = true;
            for (var i = 0; i < 3; i++)
            {
                var value = 0;
                p = ParseDigits(p, 2, ref value);
                if (p < 0)
                {
                    return -3;
                }

                switch (i)
                {
                    case 0:
                        hour = value;
                        break;
                    case 1:
                        minute = value;
                        break;
                    default:
                        second = value;
                        break;
                }

                var c = At(p++);
                if (i == 0)
                {
                    hasSeparator = c == ':';
                }

                if (p >= end)
                {
                    return c != 0 ? 1 : 0;
                }

                if (hasSeparator && c == ':')
                {
                    continue;
                }

                if (c is '.' or ',')
                {
                    break;
                }

                if (!hasSeparator)
                {
                    p--;
                }
                else
                {
                    return -4;
                }
            }

            var toParse = Math.Min(end - p, 6);
            if (toParse < 1)
            {
                return -3;
            }

            p = ParseDigits(p, toParse, ref microsecond);
            if (p < 0)
            {
                return -3;
            }

            if (toParse < 6)
            {
                microsecond *= toParse switch { 1 => 100000, 2 => 10000, 3 => 1000, 4 => 100, _ => 10 };
            }

            while (IsAsciiDigit(At(p)))
            {
                p++;
            }

            return At(p) != 0 ? 1 : 0;
        }

        internal int ParseTime(int start, int length, out int hour, out int minute, out int second, out int microsecond, out int tzSeconds, out int tzMicros)
        {
            tzSeconds = tzMicros = 0;
            var end = start + length;
            var tzPos = start;
            do
            {
                if (At(tzPos) is 'Z' or '+' or '-')
                {
                    break;
                }
            }
            while (++tzPos < end);

            var rv = ParseHhMmSsFf(start, tzPos, out hour, out minute, out second, out microsecond);
            if (rv < 0)
            {
                return rv;
            }

            if (tzPos == end)
            {
                return rv == 1 ? -5 : 0;
            }

            if (At(tzPos) == 'Z')
            {
                return At(tzPos + 1) != 0 ? -5 : 1;
            }

            var sign = At(tzPos) == '-' ? -1 : 1;
            rv = ParseHhMmSsFf(tzPos + 1, end, out var tzHour, out var tzMinute, out var tzSecond, out tzMicros);
            tzSeconds = sign * ((tzHour * 3600) + (tzMinute * 60) + tzSecond);
            tzMicros *= sign;
            return rv != 0 ? -5 : 1;
        }
    }
}
