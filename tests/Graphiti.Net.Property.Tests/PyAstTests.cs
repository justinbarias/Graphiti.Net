namespace Graphiti.PyCompat.Tests;

using System.Text.Json;
using CsCheck;
using Graphiti.PyCompat;
using static Graphiti.PyCompat.Tests.PyJsonValues;

public class PyAstTests
{
    [Fact]
    public void LiteralEval_matches_Python()
    {
        var cases = Cases("literal_eval");
        Assert.True(cases.Count > 8000);
        OnLargeStack(() =>
        {
            foreach (var c in cases)
            {
                Run(c);
            }
        });
    }

    [Fact]
    public void LiteralEval_of_long_and_deeply_nested_texts_matches_Python()
    {
        OnLargeStack(() =>
        {
            foreach (var c in Cases("literal_eval_big"))
            {
                Run(c);
            }
        });
    }

    [Fact]
    public void LiteralEval_of_the_deepest_text_that_the_limits_accept_returns_on_a_default_stack()
    {
        // The other deep tests run on a large stack, so this one holds the default stack. A stack overflow ends the
        // process, and no caller can catch it. An instrumented build uses more stack, so the test does not run there.
        Assert.SkipWhen(
            typeof(PyAst).Assembly.GetTypes().Any(t => t.Name == "MutantControl"),
            "Stryker instruments this build, and the instrumented parser uses more stack.");
        var brackets = new string('(', 200);
        var close = new string(')', 200);
        OnThread(
            () =>
            {
                Assert.Equal(1L, PyAst.LiteralEval(brackets + "1" + close));

                // 200 brackets and 799 lambdas is the deepest nesting that the two limits of the parser let through.
                // Python raises MemoryError for this text, and the port raises another error (the remarks of PyAst).
                Assert.NotNull(Record.Exception(() => PyAst.LiteralEval(brackets + string.Concat(Enumerable.Repeat("lambda: ", 799)) + "1" + close)));
            },
            0);
    }

    /// <summary>
    /// Runs the body on a thread with a 256 MB stack. The parser recurses to the depth that Python allows. A build
    /// that Stryker instruments uses more stack, and on a default stack the deep cases overflow there and stop the
    /// test host during the coverage capture, so that no mutant gets a result.
    /// </summary>
    private static void OnLargeStack(Action body) => OnThread(body, 256 * 1024 * 1024);

    /// <summary>Runs the body on a new thread and throws its error again here. A stack size of 0 is the default size.</summary>
    private static void OnThread(Action body, int maxStackSize)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? error = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    body();
                }
                catch (Exception e)
                {
                    error = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e);
                }
            },
            maxStackSize);
        thread.Start();
        thread.Join();
        error?.Throw();
    }

    [Fact]
    public void LiteralEval_reads_back_the_repr_of_a_value()
    {
        Gen.Int.Sample(
            seed =>
            {
                var x = RandomLiteral(new Random(seed), 0);
                var text = Py.Repr(x);
                var actual = PyAst.LiteralEval(text);
                if (!StrictEqual(x, actual))
                {
                    Assert.Fail($"{text} came back as {Py.Repr(actual)}");
                }
            },
            iter: 2000);
    }

    [Fact]
    public void LiteralEval_reads_what_Dumps_writes_for_a_value_that_JSON_and_Python_share()
    {
        // JSON that has no true, false, null, or NaN is also Python.
        Assert.True(StrictEqual(
            new List<object?> { 1L, "aé\n", 2.5, new OrderedDictionary<string, object?> { ["k"] = new List<object?>() } },
            PyAst.LiteralEval(PyJson.Dumps(new List<object?> { 1L, "aé\n", 2.5, new OrderedDictionary<string, object?> { ["k"] = new List<object?>() } }))));
    }

    private static void Run(JsonElement c)
    {
        var text = InputText(c.GetProperty("t"));
        if (c.TryGetProperty("e", out _))
        {
            AssertError(c, () => PyAst.LiteralEval(text));
        }
        else if (c.TryGetProperty("unsupported", out var what))
        {
            // Python returns a complex number or Ellipsis. The port throws ValueError, so that a caller takes its fallback path.
            var ex = Record.Exception(() => PyAst.LiteralEval(text));
            Assert.True(ex is not null && ex.GetType() == typeof(ArgumentException), $"expected ArgumentException for {Describe(c)}, got {ex?.GetType().Name ?? "no exception"}");
            var kind = what.GetString();
            var message = ex?.Message ?? string.Empty;
            Assert.True(
                kind == "nested"
                    ? message.Contains("complex", StringComparison.Ordinal) || message.Contains("Ellipsis", StringComparison.Ordinal)
                    : message.Contains(kind == "Ellipsis" ? "Ellipsis" : "complex", StringComparison.Ordinal),
                message);
        }
        else if (c.TryGetProperty("rsha", out _))
        {
            AssertText(c, Assert.IsType<string>(PyAst.LiteralEval(text)));
        }
        else
        {
            var actual = PyAst.LiteralEval(text);
            var expected = new Decoder().Decode(c.GetProperty("r"));
            if (!StrictEqual(expected, actual))
            {
                Assert.Fail($"for {Describe(c)}: expected {Py.Repr(expected)}, got {Py.Repr(actual)}");
            }
        }
    }

    private static object? RandomLiteral(Random rng, int depth)
    {
        switch (rng.Next(depth < 3 ? 10 : 6))
        {
            case 0:
                return null;
            case 1:
                return rng.Next(2) == 0;
            case 2:
                return rng.Next(4) == 0 ? rng.NextInt64(long.MinValue, long.MaxValue) : rng.Next(-1000, 1000);
            case 3:
                return rng.Next(5) switch
                {
                    0 => -0.0,
                    1 => BitConverter.Int64BitsToDouble(rng.NextInt64() & 0x7FEFFFFFFFFFFFFF),
                    _ => Math.Round((rng.NextDouble() - 0.5) * 1e6, rng.Next(0, 7)),
                };
            case 4:
            case 5:
                string[] pieces = ["a", "'", "\"", "\\", "\n", "\t", "\u0000", "\u007f", "é", "日", "\U0001F600", " ", "\r"];
                return string.Concat(Enumerable.Range(0, rng.Next(0, 6)).Select(_ => pieces[rng.Next(pieces.Length)]));
            case 6:
                return Enumerable.Range(0, rng.Next(0, 4)).Select(_ => RandomLiteral(rng, depth + 1)).ToList();
            case 7:
                return PyTuple.From(Enumerable.Range(0, rng.Next(0, 4)).Select(_ => RandomLiteral(rng, depth + 1)));
            case 8:
                var dict = new OrderedDictionary<string, object?>();
                for (var i = rng.Next(0, 4); i > 0; i--)
                {
                    dict["k" + rng.Next(5)] = RandomLiteral(rng, depth + 1);
                }

                return dict;
            default:
                var pd = new PyDict();
                for (var i = rng.Next(1, 4); i > 0; i--)
                {
                    pd[rng.Next(2) == 0 ? (long)rng.Next(-5, 5) : (object)PyTuple.From([(long)rng.Next(3)])] = RandomLiteral(rng, depth + 1);
                }

                return pd;
        }
    }
}
