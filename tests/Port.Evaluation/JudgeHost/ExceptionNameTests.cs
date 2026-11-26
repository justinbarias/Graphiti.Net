namespace Port.Evaluation.JudgeHost;

using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.Json;
using Graphiti.JudgeHost.Core;
using Neo4j.Driver;

/// <summary>
/// <c>Codec.PythonExceptionName</c> (RULES.md R4, open item O2). The ported exception classes and
/// <c>ValidationException</c> do not exist yet, so the test builds types with those full names at run time.
/// </summary>
public sealed class ExceptionNameTests
{
    private static readonly ModuleBuilder Module =
        AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ExceptionNameTypes"), AssemblyBuilderAccess.Run).DefineDynamicModule("ExceptionNameTypes");

    private static readonly Dictionary<string, Type> Built = new(StringComparer.Ordinal);

    public static TheoryData<string, string> BuiltinRows() => new()
    {
        { nameof(ArgumentException), "ValueError" },
        { nameof(ArgumentNullException), "TypeError" },
        { nameof(ArgumentOutOfRangeException), "IndexError" },
        { nameof(DecoderFallbackException), "UnicodeDecodeError" },
        { nameof(EncoderFallbackException), "UnicodeEncodeError" },
        { nameof(JsonException), "JSONDecodeError" },
        { nameof(FormatException), "SyntaxError" },
        { nameof(InvalidCastException), "TypeError" },
        { nameof(KeyNotFoundException), "KeyError" },
        { nameof(IndexOutOfRangeException), "IndexError" },
        { nameof(MissingMemberException), "AttributeError" },
        { nameof(MissingMethodException), "AttributeError" },
        { nameof(NotImplementedException), "NotImplementedError" },
        { nameof(InvalidOperationException), "RuntimeError" },
        { nameof(ObjectDisposedException), "RuntimeError" },
        { nameof(DivideByZeroException), "ZeroDivisionError" },
        { nameof(OverflowException), "OverflowError" },
        { nameof(OperationCanceledException), "CancelledError" },
        { nameof(TaskCanceledException), "CancelledError" },
        { nameof(TimeoutException), "TimeoutError" },
        { nameof(NullReferenceException), "NullReferenceException" },
    };

    [Theory]
    [MemberData(nameof(BuiltinRows))]
    public void ABuiltinTypeHasItsPythonName(string type, string python)
    {
        var error = (Exception)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(Find(type));
        Assert.Equal(python, Codec.PythonExceptionName(error));
        Assert.Equal(python, Codec.PythonExceptionName(error.GetType()));
    }

    [Fact]
    public void ValidationExceptionWinsOverItsBase()
    {
        var validation = Build("Graphiti.PyCompat.ValidationException", typeof(ArgumentException));
        var derived = Build("Graphiti.PyCompat.SomeValidationException", validation);
        Assert.Equal("ValidationError", Name(validation));
        Assert.Equal("ValidationError", Name(derived));

        // The same name in another namespace is an ordinary ArgumentException.
        Assert.Equal("ValueError", Name(Build("Other.ValidationException", typeof(ArgumentException))));
    }

    [Fact]
    public void EachPortedExceptionHasItsPythonName()
    {
        var root = Build("Graphiti.GraphitiException", typeof(Exception));
        Assert.Equal("GraphitiError", Name(root));
        foreach (var (type, python) in Codec.GraphitiExceptionNames)
        {
            var baseType = type is "RateLimitException" or "RefusalException" or "EmptyResponseException"
                or "OpenAIDecisionsInputTooLongException" or "OpenAIInputTooLongSignalException" ? typeof(Exception) : root;
            var ns = baseType == root ? "Graphiti" : "Graphiti.LlmClient";
            var built = type == "GraphitiException" ? root : Build($"{ns}.{type}", baseType);
            Assert.Equal(python, Name(built));
        }

        // A derived type with no row takes the name of its nearest base with a row.
        Assert.Equal("NodeNotFoundError", Name(Build("Graphiti.Nodes.SpecialNodeNotFoundException", Built["Graphiti.NodeNotFoundException"])));
        Assert.Equal("GraphitiError", Name(Build("Graphiti.Search.UnlistedException", root)));

        // A type outside the namespace Graphiti keeps its own name.
        Assert.Equal("NodeNotFoundException", Name(Build("Elsewhere.NodeNotFoundException", typeof(Exception))));
    }

    [Fact]
    public void EachPortedRowStandsAboveItsBase()
    {
        var rows = Codec.GraphitiExceptionNames.Select(r => r.Type).ToList();
        foreach (var derived in rows.Where(r => r is not "GraphitiException" and not "RateLimitException" and not "RefusalException"
                     and not "EmptyResponseException" and not "OpenAIDecisionsInputTooLongException" and not "OpenAIInputTooLongSignalException"))
        {
            Assert.True(rows.IndexOf(derived) < rows.IndexOf("GraphitiException"), derived);
        }

        var builtin = Codec.BuiltinExceptionNames.Select(r => r.Type).ToList();
        var argument = builtin.IndexOf(typeof(ArgumentException).FullName!);
        foreach (var derived in new[] { "Graphiti.PyCompat.ValidationException", typeof(DecoderFallbackException).FullName!, typeof(EncoderFallbackException).FullName!, typeof(ArgumentNullException).FullName!, typeof(ArgumentOutOfRangeException).FullName! })
        {
            Assert.True(builtin.IndexOf(derived) < argument, derived);
        }

        Assert.True(builtin.IndexOf(typeof(TaskCanceledException).FullName!) < builtin.IndexOf(typeof(OperationCanceledException).FullName!));
    }

    [Theory]
    [InlineData("Neo.ClientError.Statement.SyntaxError", "CypherSyntaxError")]
    [InlineData("Neo.ClientError.Statement.TypeError", "CypherTypeError")]
    [InlineData("Neo.ClientError.Schema.ConstraintValidationFailed", "ConstraintError")]
    [InlineData("Neo.ClientError.Statement.ParameterMissing", "ClientError")]
    [InlineData("Neo.DatabaseError.General.UnknownError", "DatabaseError")]
    [InlineData("Neo.TransientError.General.DatabaseUnavailable", "TransientError")]
    [InlineData("Neo.TransientError.Transaction.Terminated", "ClientError")]
    public void ANeo4jErrorIsNamedByItsCode(string code, string python)
    {
        Assert.Equal(python, Codec.PythonExceptionName(new ClientException(code, "message")));
        Assert.Equal(python, Codec.PythonExceptionName(new DatabaseException(code, "message")));
    }

    [Fact]
    public void ANeo4jErrorWithNoCodeIsNamedByItsType()
    {
        Assert.Equal("ServiceUnavailable", Codec.PythonExceptionName(new ServiceUnavailableException("down")));
        Assert.Equal("SessionExpired", Codec.PythonExceptionName(new SessionExpiredException("gone")));
        Assert.Equal("ClientError", Codec.PythonExceptionName(new ClientException("message")));
    }

    [Fact]
    public void AnHttpErrorIsNamedByItsStatus()
    {
        Assert.Equal("HTTPStatusError", Codec.PythonExceptionName(new HttpRequestException("x", null, System.Net.HttpStatusCode.InternalServerError)));
        Assert.Equal("TransportError", Codec.PythonExceptionName(new HttpRequestException("x")));
    }

    private static string Name(Type type) => Codec.PythonExceptionName((Exception)Activator.CreateInstance(type)!);

    private static Type Find(string name) =>
        new[] { typeof(object).Assembly, typeof(JsonException).Assembly, typeof(DecoderFallbackException).Assembly }
            .Select(a => a.GetTypes().FirstOrDefault(t => t.Name == name && t.IsPublic && typeof(Exception).IsAssignableFrom(t)))
            .First(t => t is not null)!;

    private static Type Build(string fullName, Type baseType)
    {
        lock (Built)
        {
            if (!Built.TryGetValue(fullName, out var type))
            {
                var builder = Module.DefineType(fullName, TypeAttributes.Public | TypeAttributes.Class, baseType);
                builder.DefineDefaultConstructor(MethodAttributes.Public);
                type = builder.CreateType();
                Built[fullName] = type;
            }

            return type;
        }
    }
}
