// Evaluation test for graphiti_core/errors.py (port/RULES.md, R23, gap J1).
namespace Graphiti.Port.Evaluation;

using System.Reflection;
using System.Text.Json;

public class ErrorsTests
{
    // Recorded cases that have no C# form under R4. A class with no __init__ of its own takes
    // `string message = ""`, so the call with two arguments (Python makes a tuple text) has no C# form.
    private static readonly HashSet<string> NoCSharpForm =
    [
        "GraphitiError/2",
    ];

    private static readonly List<RecordedCase> Cases = Load();

    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Count; i++)
        {
            if (!NoCSharpForm.Contains(Cases[i].Key))
            {
                data.Add(i);
            }
        }

        return data;
    }

    public static TheoryData<int> CaseIndexesWithArguments()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Count; i++)
        {
            if (!NoCSharpForm.Contains(Cases[i].Key) && Cases[i].Args.Count > 0)
            {
                data.Add(i);
            }
        }

        return data;
    }

    [Fact]
    public void The_skipped_cases_are_in_the_recording()
    {
        foreach (var key in NoCSharpForm)
        {
            Assert.Contains(Cases, c => c.Key == key);
        }

        Assert.Equal(220, Cases.Count);
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void The_message_and_the_base_class_match_Python(int index)
    {
        var recorded = Cases[index];
        var type = ClassOf(recorded.Class);

        var error = Construct(type, recorded.Args, null);

        Assert.Equal(recorded.Str, error.Message);
        if (recorded.MessageAttribute is not null)
        {
            Assert.Equal(recorded.MessageAttribute, error.Message);
        }

        // R4: GraphitiError is the root, and each other class has GraphitiException as base,
        // also NodeLabelValidationError, which Python derives from ValueError too.
        if (recorded.Class == "GraphitiError")
        {
            Assert.Equal(typeof(Exception), type.BaseType);
            Assert.Equal(["Exception"], recorded.Bases);
        }
        else
        {
            Assert.Equal(typeof(GraphitiException), type.BaseType);
            Assert.Equal("GraphitiError", recorded.Bases[0]);
        }
    }

    [Theory]
    [MemberData(nameof(CaseIndexesWithArguments))]
    public void The_constructor_with_an_inner_exception_keeps_the_message_and_the_cause(int index)
    {
        var recorded = Cases[index];
        var cause = new InvalidOperationException("cause");

        var error = Construct(ClassOf(recorded.Class), recorded.Args, cause);

        Assert.Equal(recorded.Str, error.Message);
        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public void No_class_has_a_constructor_with_only_a_cause()
    {
        // So a recorded call with no argument has no form with a cause.
        foreach (var type in Cases.Select(c => ClassOf(c.Class)).Distinct())
        {
            Assert.DoesNotContain(
                type.GetConstructors(),
                c => c.GetParameters() is [{ ParameterType: var only }] && only == typeof(Exception));
        }
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Each_other_attribute_of_Python_is_a_property_with_the_same_value(int index)
    {
        var recorded = Cases[index];
        var type = ClassOf(recorded.Class);
        var error = Construct(type, recorded.Args, null);

        foreach (var (name, expected) in recorded.Attributes)
        {
            // R2: group_id is the property GroupId.
            var property = type.GetProperty(string.Concat(name.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..])));

            Assert.NotNull(property);
            Assert.False(property.CanWrite);
            Assert.Equal(expected, property.GetValue(error));
        }
    }

    [Fact]
    public void The_recording_holds_the_attributes_of_UnitInProgressError()
    {
        // Without this test, a recording with no attribute passes the test above.
        Assert.Equal(28, Cases.Count(c => c.Class == "UnitInProgressError" && c.Attributes.Count == 3));
        Assert.Equal(28, Cases.Count(c => c.Attributes.Count > 0));
    }

    private static Type ClassOf(string pythonName)
    {
        var name = pythonName == "GraphitiError"
            ? "GraphitiException"
            : pythonName.EndsWith("Error", StringComparison.Ordinal)
                ? pythonName[..^"Error".Length] + "Exception"
                : pythonName + "Exception";
        return typeof(GraphitiException).Assembly.GetType("Graphiti." + name, throwOnError: true)!;
    }

    private static Exception Construct(Type type, List<object> args, Exception? inner)
    {
        foreach (var constructor in type.GetConstructors())
        {
            var parameters = constructor.GetParameters();
            var hasInner = parameters.Length > 0 && parameters[^1].ParameterType == typeof(Exception);
            // A constructor whose last parameter is the cause serves a call with no cause only if that parameter is optional.
            if ((inner is not null && !hasInner) || (inner is null && hasInner && !parameters[^1].HasDefaultValue))
            {
                continue;
            }

            var own = hasInner ? parameters[..^1] : parameters;
            if (own.Length < args.Count || own.Skip(args.Count).Any(p => !p.HasDefaultValue))
            {
                continue;
            }

            var values = new List<object?>(args);
            values.AddRange(own.Skip(args.Count).Select(p => p.DefaultValue));
            if (hasInner)
            {
                values.Add(inner);
            }

            return (Exception)constructor.Invoke(values.ToArray());
        }

        throw new InvalidOperationException($"{type.Name} has no constructor for {args.Count} argument(s)");
    }

    private static List<RecordedCase> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "errors.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cases = new List<RecordedCase>();
        foreach (var item in document.RootElement.GetProperty("cases").EnumerateArray())
        {
            var message = item.GetProperty("message");
            cases.Add(new RecordedCase(
                item.GetProperty("class").GetString()!,
                item.GetProperty("args").EnumerateArray().Select(Convert).ToList(),
                item.GetProperty("str").GetString()!,
                message.ValueKind == JsonValueKind.Null ? null : message.GetString(),
                item.GetProperty("attrs").EnumerateObject().ToDictionary(a => a.Name, a => Convert(a.Value)),
                item.GetProperty("bases").EnumerateArray().Select(b => b.GetString()!).ToList()));
        }

        return cases;
    }

    private static object Convert(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Number => element.GetInt64(),
        JsonValueKind.Array => element.EnumerateArray().Select(e => e.GetString()!).ToList(),
        _ => throw new InvalidOperationException($"unexpected argument {element.GetRawText()}"),
    };

    private sealed record RecordedCase(string Class, List<object> Args, string Str, string? MessageAttribute, Dictionary<string, object> Attributes, List<string> Bases)
    {
        public string Key => $"{Class}/{Args.Count}";
    }
}
