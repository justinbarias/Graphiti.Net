using System.Text;
using System.Text.Json;
using CsCheck;
using Graphiti.PyCompat;

namespace Graphiti.PyCompat.Tests;

/// <summary>
/// The values that real Python gave for each helper, read from <c>python-values.json</c>.
/// <c>port/tools/generate_pycompat_values.py</c> writes the file. An input is stored as
/// code points, and a float as the 64 bits of its value.
/// </summary>
internal static class PythonValues
{
    private static readonly JsonDocument Document = JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "python-values.json")));

    private static readonly string[] Strings =
        [.. Document.RootElement.GetProperty("strings").EnumerateArray().Select(FromCodePoints)];

    private static readonly Dictionary<string, Type> ErrorTypes = new()
    {
        ["ZeroDivisionError"] = typeof(DivideByZeroException),
        ["OverflowError"] = typeof(OverflowException),
        ["ValueError"] = typeof(ArgumentException),
        ["TypeError"] = typeof(InvalidCastException),
        ["IndexError"] = typeof(ArgumentOutOfRangeException),
        ["UnicodeEncodeError"] = typeof(EncoderFallbackException),
        ["UnicodeDecodeError"] = typeof(DecoderFallbackException),
    };

    internal static string FromCodePoints(JsonElement element) =>
        PyText.FromCodePoints([.. element.EnumerateArray().Select(e => e.GetInt32())]);

    internal static string Str(JsonElement c, string name) => Strings[c.GetProperty(name).GetInt32()];

    internal static string Text(JsonElement c, string name) => FromCodePoints(c.GetProperty(name));

    internal static long? OptLong(JsonElement c, string name) =>
        c.GetProperty(name) is { ValueKind: not JsonValueKind.Null } v ? v.GetInt64() : null;

    internal static double Bits(JsonElement c, string name) =>
        BitConverter.Int64BitsToDouble(c.GetProperty(name).GetInt64());

    internal static List<JsonElement> Cases(string name) =>
        [.. Document.RootElement.GetProperty("cases").GetProperty(name).EnumerateArray()];

    /// <summary>Asserts that two floats are the same value: the same bits, and any nan equals any nan.</summary>
    internal static void AssertSameFloat(JsonElement c, double actual)
    {
        if (c.GetProperty("nan").GetBoolean())
        {
            Assert.True(double.IsNaN(actual), $"expected nan, got {actual}");
        }
        else
        {
            Assert.Equal(c.GetProperty("r").GetInt64(), BitConverter.DoubleToInt64Bits(actual));
        }
    }

    /// <summary>
    /// Checks a helper against each recorded case. Every case runs once, and then CsCheck draws
    /// cases at random from the same list. A case holds either a result ("r") or the Python
    /// error class name ("e"). <paramref name="call"/> runs the helper. <paramref name="verify"/>
    /// compares its result with the recorded one, and runs only for a case that has a result.
    /// </summary>
    internal static void Check<T>(string name, Func<JsonElement, T> call, Action<JsonElement, T> verify)
    {
        var cases = Cases(name);
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            Run(c, call, verify);
        }

        Gen.Int[0, cases.Count - 1].Sample(i => Run(cases[i], call, verify), iter: 200);
    }

    internal static void CheckLong(string name, Func<JsonElement, long> call) =>
        Check(name, call, (c, v) => Assert.Equal(c.GetProperty("r").GetInt64(), v));

    internal static void CheckInt(string name, Func<JsonElement, int> call) =>
        Check(name, call, (c, v) => Assert.Equal(c.GetProperty("r").GetInt32(), v));

    internal static void CheckBool(string name, Func<JsonElement, bool> call) =>
        Check(name, call, (c, v) => Assert.Equal(c.GetProperty("r").GetBoolean(), v));

    internal static void CheckFloat(string name, Func<JsonElement, double> call) =>
        Check(name, call, AssertSameFloat);

    internal static void CheckText(string name, Func<JsonElement, string> call) =>
        Check(name, call, (c, v) => Assert.Equal(Ints(c.GetProperty("r")), PyText.ToCodePoints(v)));

    internal static void CheckTexts(string name, Func<JsonElement, IEnumerable<string>> call) =>
        Check(name, call, (c, v) => Assert.Equal(
            c.GetProperty("r").EnumerateArray().Select(Ints).ToArray(), v.Select(PyText.ToCodePoints).ToArray()));

    internal static int[] Ints(JsonElement e) => [.. e.EnumerateArray().Select(x => x.GetInt32())];

    private static void Run<T>(JsonElement c, Func<JsonElement, T> call, Action<JsonElement, T> verify)
    {
        if (c.TryGetProperty("big", out var big))
        {
            // Python's result is an int that a long cannot hold. The helper throws OverflowException.
            var ex = Record.Exception(() => call(c));
            Assert.True(ex is OverflowException, $"expected OverflowException for {c} (Python gave {big}), got {ex?.GetType().Name ?? "no exception"}");
        }
        else if (c.TryGetProperty("e", out var error))
        {
            var expected = ErrorTypes[error.GetString()!];
            var ex = Record.Exception(() => call(c));
            Assert.True(ex is not null && ex.GetType() == expected,
                $"expected {expected.Name} for {c}, got {ex?.GetType().Name ?? "no exception"}");
            if (!error.GetString()!.StartsWith("Unicode", StringComparison.Ordinal))
            {
                Assert.Equal(FromCodePoints(c.GetProperty("msg")), ex!.Message);
            }
        }
        else
        {
            verify(c, call(c));
        }
    }
}
