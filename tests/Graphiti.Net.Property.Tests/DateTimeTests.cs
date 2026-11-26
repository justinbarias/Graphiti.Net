namespace Graphiti.PyCompat.Tests;

using System.Text.Json;
using CsCheck;
using Graphiti.PyCompat;

/// <summary>
/// The values that real Python gave for <see cref="PyDateTime"/>, read from <c>python-values.datetime.json</c>.
/// <c>port/tools/generate_datetime_values.py</c> writes the file. A datetime is [year, month, day, hour, minute, second,
/// microsecond, offset] with the offset in microseconds or null.
/// </summary>
internal static class DateTimeValues
{
    private static readonly JsonDocument Document = JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "python-values.datetime.json")));

    private static readonly Dictionary<string, Type> ErrorTypes = new()
    {
        ["ValueError"] = typeof(ArgumentException),
        ["OverflowError"] = typeof(OverflowException),
        ["TypeError"] = typeof(InvalidCastException),
    };

    internal static List<JsonElement> Cases(string name)
    {
        var cases = Document.RootElement.GetProperty("cases").GetProperty(name).EnumerateArray().ToList();
        Assert.NotEmpty(cases);
        return cases;
    }

    /// <summary>A text: a plain JSON string, or a list of code points where the text holds a lone surrogate.</summary>
    internal static string Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : PyText.FromCodePoints([.. value.EnumerateArray().Select(e => e.GetInt32())]);

    internal static string Text(JsonElement c, string name) => Text(c.GetProperty(name));

    internal static PyDateTime Dt(JsonElement parts)
    {
        var p = parts.EnumerateArray().ToArray();
        return new PyDateTime(
            p[0].GetInt32(), p[1].GetInt32(), p[2].GetInt32(), p[3].GetInt32(), p[4].GetInt32(), p[5].GetInt32(), p[6].GetInt32(),
            p[7].ValueKind == JsonValueKind.Null ? null : p[7].GetInt64());
    }

    internal static PyDateTime Dt(JsonElement c, string name) => Dt(c.GetProperty(name));

    /// <summary>Asserts that a value has the fields of a recorded datetime, field by field, and the same offset.</summary>
    internal static void AssertSame(JsonElement expected, PyDateTime actual, string context)
    {
        var p = expected.EnumerateArray().ToArray();
        long? offset = p[7].ValueKind == JsonValueKind.Null ? null : p[7].GetInt64();
        Assert.True(
            new[] { p[0].GetInt32(), p[1].GetInt32(), p[2].GetInt32(), p[3].GetInt32(), p[4].GetInt32(), p[5].GetInt32(), p[6].GetInt32() }
                .SequenceEqual([actual.Year, actual.Month, actual.Day, actual.Hour, actual.Minute, actual.Second, actual.Microsecond])
                && offset == actual.OffsetMicroseconds
                && actual.IsNaive == (offset is null),
            $"{context}: expected {expected}, got {actual.Repr()}");
    }

    /// <summary>
    /// Runs a helper on each recorded case. A case holds a result or the Python error. <paramref name="call"/> runs the helper
    /// and <paramref name="verify"/> compares its result with the recorded one. For an error the test asserts the C# exception
    /// type of R4 and the message of Python.
    /// </summary>
    internal static void Check<T>(string name, Func<JsonElement, T> call, Action<JsonElement, T> verify)
    {
        foreach (var c in Cases(name))
        {
            if (c.TryGetProperty("e", out var error))
            {
                var expected = ErrorTypes[error.GetString()!];
                var ex = Record.Exception(() => call(c));
                Assert.True(ex is not null && ex.GetType() == expected, $"{name} {c}: expected {expected.Name}, got {ex?.GetType().Name ?? "no exception"}");
                Assert.Equal(Text(c, "msg"), ex!.Message);
            }
            else
            {
                verify(c, call(c));
            }
        }
    }
}

public class DateTimeTests
{
    [Fact]
    public void IsoFormat_matches_Python() =>
        DateTimeValues.Check("iso_format", c => DateTimeValues.Dt(c, "dt").IsoFormat(), (c, v) => Assert.Equal(DateTimeValues.Text(c, "r"), v));

    [Fact]
    public void Str_matches_Python() =>
        DateTimeValues.Check("str", c => DateTimeValues.Dt(c, "dt").Str(), (c, v) => Assert.Equal(DateTimeValues.Text(c, "r"), v));

    [Fact]
    public void Repr_matches_Python() =>
        DateTimeValues.Check("repr", c => DateTimeValues.Dt(c, "dt").Repr(), (c, v) => Assert.Equal(DateTimeValues.Text(c, "r"), v));

    [Fact]
    public void FromIsoFormat_matches_Python() =>
        DateTimeValues.Check(
            "from_iso_format",
            c => PyDateTime.FromIsoFormat(DateTimeValues.Text(c, "s")),
            (c, v) => DateTimeValues.AssertSame(c.GetProperty("r"), v, DateTimeValues.Text(c, "s")));

    [Fact]
    public void Comparison_and_equality_match_Python()
    {
        foreach (var c in DateTimeValues.Cases("compare"))
        {
            var a = DateTimeValues.Dt(c, "a");
            var b = DateTimeValues.Dt(c, "b");
            var context = $"{a.Repr()} vs {b.Repr()}";
            Assert.True(c.GetProperty("eq").GetBoolean() == (a == b), context);
            Assert.True(c.GetProperty("ne").GetBoolean() == (a != b), context);
            Assert.True(a.Equals(b) == (a == b), context);
            Assert.True(c.GetProperty("eq").GetBoolean() == a.Equals((object)b), context);
            Assert.True(c.GetProperty("eq").GetBoolean() == ((object)a).Equals(b), context);
            Assert.True(c.GetProperty("eq").GetBoolean() == object.Equals(a, b), context);
            if (a == b)
            {
                Assert.Equal(a.GetHashCode(), b.GetHashCode());
            }

            if (c.TryGetProperty("ord", out var ord))
            {
                var expected = ord.EnumerateArray().Select(e => e.GetBoolean()).ToArray();
                Assert.True(expected.SequenceEqual([a < b, a <= b, a > b, a >= b]), context);
            }
            else
            {
                var message = DateTimeValues.Text(c, "ord_err");
                Assert.Equal(message, Assert.Throws<InvalidCastException>(() => a < b).Message);
                Assert.Equal(message, Assert.Throws<InvalidCastException>(() => a <= b).Message);
                Assert.Equal(message, Assert.Throws<InvalidCastException>(() => a > b).Message);
                Assert.Equal(message, Assert.Throws<InvalidCastException>(() => a >= b).Message);
                Assert.Equal(message, Assert.Throws<InvalidCastException>(() => a.CompareTo(b)).Message);
            }
        }
    }

    [Fact]
    public void ToUtc_matches_Python() =>
        DateTimeValues.Check(
            "to_utc",
            c => DateTimeValues.Dt(c, "dt").ToUtc(),
            (c, v) => DateTimeValues.AssertSame(c.GetProperty("r"), v, DateTimeValues.Dt(c, "dt").Repr()));

    [Fact]
    public void ReplaceUtc_matches_Python() =>
        DateTimeValues.Check(
            "replace_utc",
            c => DateTimeValues.Dt(c, "dt").ReplaceUtc(),
            (c, v) => DateTimeValues.AssertSame(c.GetProperty("r"), v, DateTimeValues.Dt(c, "dt").Repr()));

    [Fact]
    public void IsNaive_and_IsUtc_match_Python()
    {
        foreach (var c in DateTimeValues.Cases("flags"))
        {
            var dt = DateTimeValues.Dt(c, "dt");
            Assert.Equal(c.GetProperty("naive").GetBoolean(), dt.IsNaive);
            Assert.Equal(c.GetProperty("utc").GetBoolean(), dt.IsUtc);
        }
    }

    [Fact]
    public void FromDateTimeOffset_cuts_ticks_as_the_Neo4j_driver_does()
    {
        foreach (var c in DateTimeValues.Cases("from_dto"))
        {
            var f = c.GetProperty("f").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var value = new DateTimeOffset(f[0], f[1], f[2], f[3], f[4], f[5], TimeSpan.FromMinutes(f[7])).AddTicks(f[6]);
            DateTimeValues.AssertSame(c.GetProperty("r"), PyDateTime.FromDateTimeOffset(value), c.ToString());
        }
    }

    /// <summary>
    /// The expected values are not recorded from Python, which has no DateTimeOffset. They follow decision D36 (a naive value
    /// is read as UTC) and the rule that an offset that DateTimeOffset cannot hold becomes UTC at the same instant. Python gives
    /// only the UTC instant and the OverflowError at the ends of the range.
    /// </summary>
    [Fact]
    public void ToDateTimeOffset_by_decision_D36_reads_a_naive_value_as_UTC_and_keeps_the_instant()
    {
        var epochTicks = DateTimeOffset.UnixEpoch.UtcTicks;
        foreach (var c in DateTimeValues.Cases("to_dto"))
        {
            var dt = DateTimeValues.Dt(c, "dt");
            if (c.TryGetProperty("e", out _))
            {
                var ex = Assert.Throws<OverflowException>(() => dt.ToDateTimeOffset());
                Assert.Equal(DateTimeValues.Text(c, "msg"), ex.Message);
                continue;
            }

            var actual = dt.ToDateTimeOffset();
            Assert.Equal(c.GetProperty("utc_micros").GetInt64(), (actual.UtcTicks - epochTicks) / 10);
            Assert.Equal(c.GetProperty("off_min").GetInt64(), (long)actual.Offset.TotalMinutes);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public void UtcNow_gives_UTC_from_any_offset_and_cuts_ticks_as_the_Neo4j_driver_does()
    {
        foreach (var c in DateTimeValues.Cases("utc_now"))
        {
            var f = c.GetProperty("f").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var now = new DateTimeOffset(f[0], f[1], f[2], f[3], f[4], f[5], TimeSpan.FromMinutes(f[7])).AddTicks(f[6]);
            var actual = PyDateTime.UtcNow(new FixedClock(now));
            DateTimeValues.AssertSame(c.GetProperty("r"), actual, c.ToString());
            Assert.True(actual.IsUtc);
        }

        var before = DateTimeOffset.UtcNow;
        var live = PyDateTime.UtcNow(TimeProvider.System);
        var after = DateTimeOffset.UtcNow;
        Assert.True(live.IsUtc);
        Assert.InRange(live.ToDateTimeOffset(), before.AddTicks(-10), after);
    }

    // The members that the rulebook leaves open or that Python does not have. The values are the documented behavior.

    [Fact]
    public void ToUtc_of_a_naive_value_is_not_built()
    {
        Assert.Throws<InvalidOperationException>(() => new PyDateTime(2025, 1, 3).ToUtc());
    }

    [Fact]
    public void A_naive_value_stays_naive()
    {
        var naive = PyDateTime.FromIsoFormat("2025-01-03T10:30:45.123456");
        Assert.True(naive.IsNaive);
        Assert.False(naive.IsUtc);
        Assert.Equal("2025-01-03T10:30:45.123456", naive.IsoFormat());
        Assert.True(default(PyDateTime).IsNaive);
        Assert.Equal("0001-01-01T00:00:00", default(PyDateTime).IsoFormat());
        Assert.True(naive.ReplaceUtc().IsUtc);
        Assert.True(naive.IsNaive, "ReplaceUtc changes a copy and never the value");
    }

    [Theory]
    [InlineData(840, 840)]
    [InlineData(841, 0)]
    [InlineData(-840, -840)]
    [InlineData(-841, 0)]
    public void ToDateTimeOffset_keeps_an_offset_up_to_14_hours_and_uses_UTC_beyond(int minutes, int expectedMinutes)
    {
        var dt = new PyDateTime(2025, 1, 3, 10, 0, 0, 0, minutes * 60_000_000L);
        var actual = dt.ToDateTimeOffset();
        Assert.Equal(expectedMinutes, (int)actual.Offset.TotalMinutes);
        Assert.Equal(new DateTime(2025, 1, 3, 10, 0, 0, DateTimeKind.Utc).AddMinutes(-minutes).Ticks, actual.UtcTicks);
    }

    [Fact]
    public void Equals_of_an_object_is_false_for_null_and_for_another_type()
    {
        var dt = new PyDateTime(2025, 1, 3, 10, 0, 0, 0, 0);
        Assert.False(dt.Equals((object?)null));
        Assert.False(dt.Equals((object)"2025-01-03T10:00:00+00:00"));
        Assert.False(dt.Equals((object)new DateTimeOffset(2025, 1, 3, 10, 0, 0, TimeSpan.Zero)));
        Assert.False(dt.Equals((object)638_000_000_000_000_000L));
        Assert.False(object.Equals(dt, null));
        Assert.False(object.Equals(null, dt));
        Assert.True(object.Equals(dt, new PyDateTime(2025, 1, 3, 11, 0, 0, 0, 3_600_000_000)));
        Assert.Equal(dt.GetHashCode(), ((object)new PyDateTime(2025, 1, 3, 11, 0, 0, 0, 3_600_000_000)).GetHashCode());
    }

    [Fact]
    public void Constructor_throws_the_ValueError_messages()
    {
        Assert.Equal("year 0 is out of range", Assert.Throws<ArgumentException>(() => new PyDateTime(0, 1, 1)).Message);
        Assert.Equal("year 10000 is out of range", Assert.Throws<ArgumentException>(() => new PyDateTime(10000, 1, 1)).Message);
        Assert.Equal("month must be in 1..12", Assert.Throws<ArgumentException>(() => new PyDateTime(2025, 13, 1)).Message);
        Assert.Equal("day is out of range for month", Assert.Throws<ArgumentException>(() => new PyDateTime(2025, 2, 29)).Message);
        Assert.Equal("hour must be in 0..23", Assert.Throws<ArgumentException>(() => new PyDateTime(2025, 1, 1, 24)).Message);
        Assert.Equal("minute must be in 0..59", Assert.Throws<ArgumentException>(() => new PyDateTime(2025, 1, 1, 0, 60)).Message);
        Assert.Equal("second must be in 0..59", Assert.Throws<ArgumentException>(() => new PyDateTime(2025, 1, 1, 0, 0, 60)).Message);
        Assert.Equal("microsecond must be in 0..999999", Assert.Throws<ArgumentException>(() => new PyDateTime(2025, 1, 1, 0, 0, 0, 1_000_000)).Message);
        Assert.Throws<ArgumentException>(() => new PyDateTime(2025, 1, 1, offsetMicroseconds: 86_400_000_000));
        Assert.Throws<ArgumentException>(() => new PyDateTime(2025, 1, 1, offsetMicroseconds: -86_400_000_000));
        Assert.Equal(86_399_999_999, new PyDateTime(2025, 1, 1, offsetMicroseconds: 86_399_999_999).OffsetMicroseconds);
    }

    // Laws that hold for any value. They do not use the Python values.

    /// <summary>An offset: none, zero, whole seconds, or seconds and microseconds with the same sign (Python drops a fraction of 0 seconds).</summary>
    private static readonly Gen<long?> AnyOffset = Gen.Int[0, 3].SelectMany(kind => kind switch
    {
        0 => Gen.Const(() => (long?)null),
        1 => Gen.Const(() => (long?)0L),
        2 => Gen.Int[-86_399, 86_399].Select(s => (long?)(s * 1_000_000L)),
        _ => Gen.Int[-86_399, 86_399].Where(s => s != 0).SelectMany(s => Gen.Int[0, 999_999].Select(us => (long?)((s * 1_000_000L) + (s < 0 ? -us : us)))),
    });

    private static readonly Gen<int> AnyMicrosecond = Gen.Int[0, 3].SelectMany(kind => kind switch
    {
        0 => Gen.Const(() => 0),
        1 => Gen.Const(() => 1),
        2 => Gen.Const(() => 999_999),
        _ => Gen.Int[0, 999_999],
    });

    private static readonly Gen<PyDateTime> AnyDateTime =
        Gen.Select(Gen.Int[1, 9999], Gen.Int[1, 12], Gen.Int[1, 28], Gen.Int[0, 23], Gen.Int[0, 59], Gen.Int[0, 59], AnyMicrosecond)
            .SelectMany((y, mo, d, h, mi, s, us) => AnyOffset.Select(off => new PyDateTime(y, mo, d, h, mi, s, us, off)));

    [Fact]
    public void FromIsoFormat_reads_what_IsoFormat_and_Str_write() =>
        AnyDateTime.Sample(dt =>
        {
            var viaIso = PyDateTime.FromIsoFormat(dt.IsoFormat());
            var viaStr = PyDateTime.FromIsoFormat(dt.Str());
            return viaIso.Equals(dt) && viaStr.Equals(dt) && viaIso.IsNaive == dt.IsNaive && viaIso.OffsetMicroseconds == dt.OffsetMicroseconds
                && viaIso.Microsecond == dt.Microsecond;
        });

    [Fact]
    public void Order_and_equality_agree_for_values_of_the_same_kind() =>
        Gen.Select(AnyDateTime, AnyDateTime).Where((a, b) => a.IsNaive == b.IsNaive).Sample((a, b) =>
        {
            var c = a.CompareTo(b);
            return Math.Sign(c) == -Math.Sign(b.CompareTo(a))
                && (a < b) == (c < 0) && (a <= b) == (c <= 0) && (a > b) == (c > 0) && (a >= b) == (c >= 0)
                && (a == b) == (c == 0) && (a != b) == (c != 0)
                && (c != 0 || a.GetHashCode() == b.GetHashCode());
        });

    [Fact]
    public void A_naive_value_and_an_aware_value_are_never_equal_and_never_ordered() =>
        Gen.Select(AnyDateTime, AnyDateTime).Where((a, b) => a.IsNaive != b.IsNaive).Sample((a, b) =>
        {
            Assert.False(a == b);
            Assert.True(a != b);
            Assert.Throws<InvalidCastException>(() => a < b);
            Assert.Throws<InvalidCastException>(() => b >= a);
        });

    [Fact]
    public void ToUtc_keeps_the_instant_and_ReplaceUtc_keeps_the_clock() =>
        AnyDateTime.Where(dt => !dt.IsNaive).Sample(dt =>
        {
            var replaced = dt.ReplaceUtc();
            if (replaced.Microsecond != dt.Microsecond || !replaced.IsUtc || replaced.Hour != dt.Hour || replaced.Day != dt.Day)
            {
                return false;
            }

            try
            {
                var utc = dt.ToUtc();
                return utc.IsUtc && utc == dt && utc.ToUtc().Equals(utc);
            }
            catch (OverflowException)
            {
                // Only at the ends of the range.
                return dt.Year is 1 or 9999;
            }
        });

    [Fact]
    public void DateTimeOffset_round_trip_keeps_the_instant_and_whole_minute_offsets() =>
        Gen.Select(Gen.Int[2, 9998], Gen.Int[1, 12], Gen.Int[1, 28], Gen.Int[0, 23], Gen.Int[0, 59], Gen.Int[0, 59], Gen.Int[0, 9_999_999], Gen.Int[-840, 840]).Sample((y, mo, d, h, mi, s, ticks, minutes) =>
        {
            var source = new DateTimeOffset(y, mo, d, h, mi, s, TimeSpan.FromMinutes(minutes)).AddTicks(ticks);
            var back = PyDateTime.FromDateTimeOffset(source).ToDateTimeOffset();
            return back.Offset == source.Offset && back.UtcTicks == source.UtcTicks - (ticks % 10);
        });
}
