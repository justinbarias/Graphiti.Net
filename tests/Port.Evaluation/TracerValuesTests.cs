// Evaluation test for graphiti_core/tracer.py (port/RULES.md, R23 and R25).
// values/tracer.json holds 900 spans that port/tools/record_tracer_values.py ran in real Python against the
// OpenTelemetry SDK with an in-memory exporter. The test runs the same spans through System.Diagnostics.Activity.
// Decision D46 is the one place where the port differs on purpose: the telemetry of an exception keeps its .NET
// identity. The test shows it in the table D46Names and in the fixed list NetEventKeys, and rewrites nothing else.
namespace Graphiti.Port.Evaluation;

using System.Diagnostics;
using Graphiti.Core;
using Graphiti.PyCompat;

public class TracerValuesTests
{
    // D46: the telemetry of an exception keeps its .NET identity. A .NET exception has a .NET message, so a Python class
    // name in front of it would describe an error that never existed. This table maps the Python class name of the
    // recording to the .NET type that the port throws for it. The expected status description (`<type name>: <message>`)
    // and the expected `exception.type` (the full type name) come from it, and from nothing else.
    private static readonly Dictionary<string, Type> D46Names = new()
    {
        ["ValueError"] = typeof(ArgumentException),
        ["RuntimeError"] = typeof(InvalidOperationException),
        ["KeyError"] = typeof(KeyNotFoundException),
        ["NodeNotFoundError"] = typeof(NodeNotFoundException),
    };

    // Python records four keys for an exception event, and .NET (Activity.AddException) records these three. Python has one
    // more key, `exception.escaped`. This is part of D46.
    private static readonly string[] PythonEventKeys = ["exception.escaped", "exception.message", "exception.stacktrace", "exception.type"];

    private static readonly string[] NetEventKeys = ["exception.message", "exception.stacktrace", "exception.type"];

    [Fact]
    public async Task The_port_gives_each_recorded_result_of_python()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "tracer.json");
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        var cases = (List<object?>)document["cases"]!;
        Assert.True(cases.Count >= 900);

        List<string> failures = [];
        var index = 0;
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            var input = (OrderedDictionary<string, object?>)recorded["c"]!;
            var expected = ExpectedOf((OrderedDictionary<string, object?>)recorded["r"]!);
            var label = $"case {index++} {PyJson.Dumps(input)}";
            try
            {
                var actual = await Run(input);
                if (PyJson.Dumps(actual) != PyJson.Dumps(expected))
                {
                    failures.Add($"{label}\n  expected {PyJson.Dumps(expected)}\n  but got  {PyJson.Dumps(actual)}");
                }
            }
            catch (Exception error)
            {
                failures.Add($"{label}: {error.GetType().Name}: {error.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} recorded results differ. The first ones:\n{string.Join("\n", failures.Take(3))}");
    }

    [Fact]
    public void The_recording_holds_each_exception_class_and_each_kind_of_tracer()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "tracer.json");
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        var cases = ((List<object?>)document["cases"]!).Cast<OrderedDictionary<string, object?>>().ToList();
        var inputs = cases.Select(c => (OrderedDictionary<string, object?>)c["c"]!).ToList();

        Assert.Equal(
            ["noop", "otel", "otel_fail_end", "otel_fail_start"],
            inputs.Select(i => (string)i["tracer"]!).Distinct().Order().ToList());
        Assert.Equal(
            ["cancelled", "key_error", "node_not_found", "returns", "runtime_error", "value_error"],
            inputs.Select(i => (string)i["body"]!).Distinct().Order().ToList());

        // Each class of an exception event that the recording holds is in the table of D46, and its keys are the four of Python.
        var eventTypes = new HashSet<string>();
        foreach (var span in cases.SelectMany(c => (List<object?>)((OrderedDictionary<string, object?>)c["r"]!)["spans"]!))
        {
            foreach (var recordedEvent in (List<object?>)((OrderedDictionary<string, object?>)span!)["events"]!)
            {
                var fields = (List<object?>)recordedEvent!;
                Assert.Equal(PythonEventKeys, ((List<object?>)fields[1]!).Cast<string>());
                eventTypes.Add((string)fields[2]!);
            }
        }

        Assert.Equal(["KeyError", "NodeNotFoundError", "RuntimeError", "ValueError"], eventTypes.Select(PythonClassName).Order().ToList());
        Assert.Contains("graphiti_core.errors.NodeNotFoundError", eventTypes);
    }

    // `exception.type` of Python is `ValueError` for a built-in class and `graphiti_core.errors.NodeNotFoundError` for a class of the library.
    private static string PythonClassName(string exceptionType) => exceptionType.Split('.')[^1];

    // The port controls the message of an exception that is a ported class (NodeNotFoundException) or that the test body
    // throws with a fixed message (ArgumentException and InvalidOperationException). Python str(KeyError(k)) is repr(k),
    // and a .NET KeyNotFoundException has no such rule, so the message of a KeyError is not compared.
    private static bool MessageIsControlled(string pythonClassName) => pythonClassName != "KeyError";

    private static string MaskMessage(string pythonClassName, string message) => MessageIsControlled(pythonClassName) ? message : string.Empty;

    // The description of an exception that is not controlled keeps its `<type name>: ` prefix, and the message is dropped.
    private static string? MaskDescription(string? description)
    {
        foreach (var type in D46Names.Where(pair => !MessageIsControlled(pair.Key)).Select(pair => pair.Value))
        {
            var prefix = type.Name + ": ";
            if (description is not null && description.StartsWith(prefix, StringComparison.Ordinal))
            {
                return prefix;
            }
        }

        return description;
    }

    // What the port gives for the recorded result of Python, by D46. Everything except the exception names is as recorded.
    private static OrderedDictionary<string, object?> ExpectedOf(OrderedDictionary<string, object?> recorded)
    {
        var expected = new OrderedDictionary<string, object?>(recorded);
        if (recorded.TryGetValue("type", out var typeName) && typeName is string type)
        {
            expected["message"] = MaskMessage(type, (string)recorded["message"]!);
        }

        if (recorded.TryGetValue("context", out var contextValue) && contextValue is List<object?> context)
        {
            expected["context"] = new List<object?> { context[0], MaskMessage((string)context[0]!, (string)context[1]!) };
        }

        expected["spans"] = ((List<object?>)recorded["spans"]!).Select(s => (object?)ExpectedSpanOf((OrderedDictionary<string, object?>)s!)).ToList();
        return expected;
    }

    private static OrderedDictionary<string, object?> ExpectedSpanOf(OrderedDictionary<string, object?> recorded)
    {
        var expected = new OrderedDictionary<string, object?>(recorded);

        // `<Python class name>: <message>` becomes `<.NET type name>: <message>`. A description that a body sets does not have this form.
        var description = (string?)recorded["description"];
        var separator = description?.IndexOf(": ", StringComparison.Ordinal) ?? -1;
        if (description is not null && separator > 0 && D46Names.TryGetValue(description[..separator], out var type))
        {
            description = type.Name + description[separator..];
        }

        expected["description"] = MaskDescription(description);
        expected["events"] = ((List<object?>)recorded["events"]!).Select(e => (object?)ExpectedEventOf((List<object?>)e!)).ToList();
        return expected;
    }

    private static List<object?> ExpectedEventOf(List<object?> recorded)
    {
        var pythonClassName = PythonClassName((string)recorded[2]!);
        return
        [
            recorded[0],
            NetEventKeys.Cast<object?>().ToList(),
            D46Names[pythonClassName].FullName,
            MessageIsControlled(pythonClassName) ? recorded[3] : null,
        ];
    }

    // The exception classes of the recording, as the port throws them.
    private static Exception ErrorOf(string pythonName, string message, CancellationToken token) =>
        pythonName == "CancelledError"
            ? new OperationCanceledException(token)
            : (Exception)Activator.CreateInstance(D46Names[pythonName], message)!;

    private static string PythonNameOf(Exception error) =>
        error is OperationCanceledException
            ? "CancelledError"
            : D46Names.Single(pair => pair.Value == error.GetType()).Key;

    private static async Task<OrderedDictionary<string, object?>> Run(OrderedDictionary<string, object?> input)
    {
        var kind = (string)input["tracer"]!;
        var prefix = (string)input["prefix"]!;
        var body = (string)input["body"]!;
        var message = (string)input["msg"]!;
        var statuses = (List<object?>)input["status"]!;
        var attrs = (OrderedDictionary<string, object?>?)Decode(input["attrs"]);
        var record = (string?)input["record"];

        var stopped = new List<Activity>();
        using var source = new ActivitySource("tracer-values-" + Guid.NewGuid().ToString("N"));
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => kind == "otel_fail_start"
                ? throw new InvalidOperationException("cannot start")
                : ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (kind == "otel_fail_end")
                {
                    throw new ArgumentException("end failed");
                }

                stopped.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);

        Tracer tracer = kind == "noop" ? new NoOpTracer() : TracerModule.CreateTracer(source, prefix);
        using var cancellation = new CancellationTokenSource();
        if (body == "cancelled")
        {
            await cancellation.CancelAsync();
        }

        var spanClass = string.Empty;
        var outcome = new OrderedDictionary<string, object?>();
        try
        {
            var value = await tracer.StartSpanAsync<object?>(
                (string)input["name"]!,
                span =>
                {
                    spanClass = span.GetType().Name;
                    if (attrs is not null)
                    {
                        span.AddAttributes(attrs);
                    }

                    foreach (var call in statuses.Cast<List<object?>>())
                    {
                        span.SetStatus((string)call[0]!, (string?)call[1]);
                    }

                    if (record is not null)
                    {
                        span.RecordException(ErrorOf(record, message, cancellation.Token));
                    }

                    return body switch
                    {
                        "returns" => Task.FromResult(input["ret"]),
                        "value_error" => throw ErrorOf("ValueError", message, cancellation.Token),
                        "runtime_error" => throw ErrorOf("RuntimeError", message, cancellation.Token),
                        "key_error" => throw ErrorOf("KeyError", message, cancellation.Token),
                        "node_not_found" => throw ErrorOf("NodeNotFoundError", message, cancellation.Token),
                        "cancelled" => throw ErrorOf("CancelledError", message, cancellation.Token),
                        _ => throw new InvalidOperationException($"No body {body}"),
                    };
                },
                cancellation.Token);
            outcome["ended"] = "returned";
            outcome["value"] = value;
        }
        catch (Exception error)
        {
            var name = PythonNameOf(error);
            outcome["ended"] = "raised";
            outcome["type"] = name;
            outcome["message"] = error is OperationCanceledException ? string.Empty : MaskMessage(name, error.Message);
            outcome["context"] = error.InnerException is { } inner
                ? new List<object?> { PythonNameOf(inner), inner is OperationCanceledException ? string.Empty : MaskMessage(PythonNameOf(inner), inner.Message) }
                : null;
        }

        outcome["span_class"] = spanClass;
        outcome["spans"] = stopped.Select(Describe).ToList<object?>();
        return outcome;
    }

    // A value of the recording that JSON cannot hold is a dictionary with the key `$`. This is the value of the value model (R6).
    private static object? Decode(object? value)
    {
        switch (value)
        {
            case List<object?> list:
                return list.Select(Decode).ToList();
            case OrderedDictionary<string, object?> dictionary when dictionary.TryGetValue("$", out var kind):
                int Number(string key) => (int)(long)dictionary[key]!;
                return (string)kind! switch
                {
                    "tuple" => new PyTuple(((List<object?>)dictionary["items"]!).Select(Decode).ToArray()),
                    "bytes" => Convert.FromHexString((string)dictionary["hex"]!),
                    "date" => new DateOnly(Number("y"), Number("mo"), Number("d")),
                    "datetime" => new PyDateTime(
                        Number("y"),
                        Number("mo"),
                        Number("d"),
                        Number("h"),
                        Number("mi"),
                        Number("s"),
                        Number("us"),
                        dictionary["off"] is long offset ? offset * 60_000_000L : null),
                    _ => throw new InvalidOperationException($"No value kind {kind}"),
                };
            case OrderedDictionary<string, object?> dictionary:
                var decoded = new OrderedDictionary<string, object?>();
                foreach (var (key, item) in dictionary)
                {
                    decoded[key] = Decode(item);
                }

                return decoded;
            default:
                return value;
        }
    }

    private static OrderedDictionary<string, object?> Describe(Activity activity)
    {
        var attributes = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in activity.TagObjects)
        {
            attributes[key] = value;
        }

        return new OrderedDictionary<string, object?>
        {
            ["name"] = activity.DisplayName,
            ["attributes"] = attributes,
            ["status"] = activity.Status.ToString().ToUpperInvariant(),
            ["description"] = MaskDescription(activity.StatusDescription),
            ["events"] = activity.Events.Select(e => (object?)DescribeEvent(e)).ToList(),
        };
    }

    private static List<object?> DescribeEvent(ActivityEvent recorded)
    {
        var tags = recorded.Tags.ToDictionary(tag => tag.Key, tag => tag.Value);
        var type = (string)tags["exception.type"]!;
        var pythonName = D46Names.Single(pair => pair.Value.FullName == type).Key;
        return
        [
            recorded.Name,
            tags.Keys.Order(StringComparer.Ordinal).Cast<object?>().ToList(),
            type,
            MessageIsControlled(pythonName) ? tags["exception.message"] : null,
        ];
    }
}
