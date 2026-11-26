// Evaluation test for graphiti_core/tracer.py (port/RULES.md, R23 and R25).
// values/tracer.json holds 600 spans that port/tools/record_tracer_values.py ran in real Python against the
// OpenTelemetry SDK with an in-memory exporter. The test runs the same spans through System.Diagnostics.Activity.
namespace Graphiti.Port.Evaluation;

using System.Diagnostics;
using Graphiti.Core;
using Graphiti.PyCompat;

public class TracerValuesTests
{
    [Fact]
    public async Task The_port_gives_each_recorded_result_of_python()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "tracer.json");
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        var cases = (List<object?>)document["cases"]!;
        Assert.True(cases.Count >= 600);

        List<string> failures = [];
        var index = 0;
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            var input = (OrderedDictionary<string, object?>)recorded["c"]!;
            var expected = (OrderedDictionary<string, object?>)recorded["r"]!;
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

    // The exception classes of the recording, as the port throws them.
    private static Exception ErrorOf(string pythonName, string message, CancellationToken token) => pythonName switch
    {
        "ValueError" => new ArgumentException(message),
        "RuntimeError" => new InvalidOperationException(message),
        "CancelledError" => new OperationCanceledException(token),
        _ => throw new InvalidOperationException($"No exception for {pythonName}"),
    };

    private static string PythonNameOf(Exception error) => error switch
    {
        ArgumentException => "ValueError",
        InvalidOperationException => "RuntimeError",
        OperationCanceledException => "CancelledError",
        _ => throw new InvalidOperationException($"No Python name for {error.GetType().Name}"),
    };

    private static async Task<OrderedDictionary<string, object?>> Run(OrderedDictionary<string, object?> input)
    {
        var kind = (string)input["tracer"]!;
        var prefix = (string)input["prefix"]!;
        var body = (string)input["body"]!;
        var message = (string)input["msg"]!;
        var statuses = (List<object?>)input["status"]!;
        var attrs = (OrderedDictionary<string, object?>?)input["attrs"];
        var record = (bool)input["record"]!;

        var stopped = new List<Activity>();
        using var source = new ActivitySource("tracer-values-" + Guid.NewGuid().ToString("N"));
        using var listener = new ActivityListener
        {
            ShouldListenTo = candidate => candidate == source,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => kind == "otel_fail_start"
                ? throw new InvalidOperationException("cannot start")
                : ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
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

                    if (record)
                    {
                        span.RecordException(new ArgumentException(message));
                    }

                    return body switch
                    {
                        "returns" => Task.FromResult(input["ret"]),
                        "value_error" => throw ErrorOf("ValueError", message, cancellation.Token),
                        "runtime_error" => throw ErrorOf("RuntimeError", message, cancellation.Token),
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
            outcome["ended"] = "raised";
            outcome["type"] = PythonNameOf(error);
            outcome["message"] = error is OperationCanceledException ? string.Empty : error.Message;
            outcome["context"] = error.InnerException is { } inner ? new List<object?> { PythonNameOf(inner), inner.Message } : null;
        }

        outcome["span_class"] = spanClass;
        outcome["spans"] = stopped.Select(Describe).ToList<object?>();
        return outcome;
    }

    private static OrderedDictionary<string, object?> Describe(Activity activity)
    {
        var attributes = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in activity.TagObjects)
        {
            attributes[key] = value;
        }

        var description = activity.StatusDescription;
        foreach (var (pythonName, className) in new[] { ("ValueError", nameof(ArgumentException)), ("RuntimeError", nameof(InvalidOperationException)) })
        {
            if (description is not null && description.StartsWith(className + ": ", StringComparison.Ordinal))
            {
                description = pythonName + description[className.Length..];
            }
        }

        return new OrderedDictionary<string, object?>
        {
            ["name"] = activity.DisplayName,
            ["attributes"] = attributes,
            ["status"] = activity.Status.ToString().ToUpperInvariant(),
            ["description"] = description,
            ["events"] = activity.Events
                .Select(e => (object?)new List<object?> { e.Name, e.Tags.FirstOrDefault(tag => tag.Key == "exception.message").Value })
                .ToList(),
        };
    }
}
