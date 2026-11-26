namespace Port.Evaluation.JudgeHost;

using System.Numerics;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>
/// The typed reads of a binding (decision D20, RULES.md R22). Each row is a decoded value, a C# parameter type, and
/// whether the binding can call the product code. Python's own treatment of the pair decides each row.
/// </summary>
public sealed class TypedReadTests
{
    private static readonly PyDateTime Moment = PyDateTime.FromIsoFormat("2026-03-01T00:00:00+00:00");

    public static TheoryData<string, object?, Type, bool> Rows() => new()
    {
        // None: only a nullable parameter, or open data, takes it.
        { "None for str", null, typeof(string), false },
        { "None for int", null, typeof(long), false },
        { "None for int | None", null, typeof(long?), true },
        { "None for float | None", null, typeof(double?), true },
        { "None for Any", null, typeof(object), true },
        { "None for list", null, typeof(List<string>), false },

        // Numbers. Python takes an int where a float is expected, and the conversion is float(x).
        { "int for int", 3L, typeof(long), true },
        { "int for float", 3L, typeof(double), true },
        { "big int for float", BigInteger.Pow(2, 70), typeof(double), true },
        { "big int for int", BigInteger.Pow(2, 70), typeof(long), false },
        { "float for float", 1.5, typeof(double), true },
        { "whole float for int", 1.0, typeof(long), false },
        { "bool for int", true, typeof(long), false },
        { "bool for float", false, typeof(double), false },
        { "int for bool", 1L, typeof(bool), false },
        { "int for C# int", 7L, typeof(int), true },
        { "large int for C# int", 1L << 40, typeof(int), false },

        // Text and enums.
        { "str for str", "x", typeof(string), true },
        { "int for str", 42L, typeof(string), false },
        { "str for enum", "red", typeof(Color), false },
        { "int for enum", 1L, typeof(Color), false },
        { "member for enum", Color.Red, typeof(Color), true },
        { "member for enum | None", Color.One, typeof(Color?), true },
        { "member for str", Color.Red, typeof(string), false },

        // Dates.
        { "datetime for datetime", Moment, typeof(PyDateTime), true },
        { "datetime for datetime | None", Moment, typeof(PyDateTime?), true },
        { "str for datetime", "2026-03-01", typeof(PyDateTime), false },
        { "date for datetime", new DateOnly(2026, 3, 1), typeof(PyDateTime), false },
        { "datetime for date", Moment, typeof(DateOnly), false },

        // Containers.
        { "list of str for list[str]", new List<object?> { "a", "b" }, typeof(List<string>), true },
        { "list with None for list[str]", new List<object?> { "a", null }, typeof(List<string>), false },
        { "list of int for list[float]", new List<object?> { 1L, 2.5 }, typeof(List<double>), true },
        { "list for list[Any]", new List<object?> { 1L, "a", null }, typeof(List<object>), true },
        { "tuple for list[str]", PyTuple.From(["a"]), typeof(List<string>), false },
        { "str for list[str]", "ab", typeof(List<string>), false },
        { "tuple for tuple", PyTuple.From(["a"]), typeof(PyTuple), true },
        { "dict for dict[str, int]", new OrderedDictionary<string, object?> { ["a"] = 1L }, typeof(OrderedDictionary<string, long>), true },
        { "dict with a str value for dict[str, int]", new OrderedDictionary<string, object?> { ["a"] = "1" }, typeof(OrderedDictionary<string, long>), false },
        { "dict with other keys for dict[str, Any]", new PyDict(), typeof(OrderedDictionary<string, object>), false },
        { "list for dict", new List<object?>(), typeof(OrderedDictionary<string, object>), false },
        { "bytes for bytes", new byte[] { 1 }, typeof(byte[]), true },

        // A model or a ref is the object that the registry built.
        { "model for its type", new TestModel("M", []), typeof(TestModel), true },
        { "dict for a model", new OrderedDictionary<string, object?>(), typeof(TestModel), false },
        { "delegate for a ref", (Func<string, string>)(s => s), typeof(Func<string, string>), true },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void ATypedReadAcceptsOrRejects(string row, object? value, Type target, bool accepted)
    {
        _ = row;
        var parameters = new JudgeArguments([value], []).Signature("p");
        var read = typeof(JudgeParameters).GetMethod(nameof(JudgeParameters.Get), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .MakeGenericMethod(target);
        Exception? error = null;
        try
        {
            read.Invoke(parameters, ["p"]);
        }
        catch (System.Reflection.TargetInvocationException e)
        {
            error = e.InnerException;
        }

        if (accepted)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.IsType<JudgeTypeRejectedException>(error);
        }
    }

    [Fact]
    public void AnIntForAFloatIsConvertedAsFloatConvertsIt()
    {
        var p = new JudgeArguments([(1L << 53) + 1, BigInteger.Pow(10, 400)], []).Signature("a", "b");
        Assert.Equal(9007199254740992.0, p.Double("a"));
        Assert.Throws<OverflowException>(() => p.Double("b"));
    }

    [Fact]
    public void ANoneForAReferenceTypeWithNoneIsNull()
    {
        var p = new JudgeArguments([null], []).Signature("text");
        Assert.Null(p.NullableString("text"));
        Assert.Null(p.NullableList<string>("text"));
        Assert.Throws<JudgeTypeRejectedException>(() => p.String("text"));
    }

    [Fact]
    public void TheListOfTheCaseIsSharedForOpenData()
    {
        var list = new List<object?> { 1L };
        Assert.Same(list, new JudgeArguments([list], []).Signature("xs").List<object>("xs"));
    }

    [Fact]
    public void ASignatureBindsAsAPythonCallDoes()
    {
        var p = new JudgeArguments(["a"], new() { ["limit"] = 3L }).Signature("query", "limit", "group");
        Assert.Equal("a", p.String("query"));
        Assert.Equal(3L, p.Long("limit"));
        Assert.False(p.Has("group"));
        Assert.Throws<JudgeTypeRejectedException>(() => p.Value("group"));

        Assert.Throws<JudgeTypeRejectedException>(() => new JudgeArguments(["a", "b"], []).Signature("query"));
        Assert.Throws<JudgeTypeRejectedException>(() => new JudgeArguments([], new() { ["other"] = 1L }).Signature("query"));
        Assert.Throws<JudgeTypeRejectedException>(() => new JudgeArguments(["a"], new() { ["query"] = "b" }).Signature("query"));
        Assert.Throws<InvalidOperationException>(() => p.Value("unnamed"));
    }
}
