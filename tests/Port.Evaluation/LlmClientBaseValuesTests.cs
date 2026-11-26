// Evaluation test for the base modules of the model client (port/RULES.md, R23 and R25): config.py, errors.py,
// token_tracker.py, and utils.py of graphiti_core/llm_client.
// values/llm_client_base.json holds what port/tools/record_llm_client_base.py recorded from Python.
namespace Graphiti.Port.Evaluation;

using System.Collections;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// print_summary writes to the console, and the test replaces the console writer. No other test of the
// assembly prints, and the collection keeps these tests apart from each other.
[Collection("LlmClientConsole")]
public class LlmClientBaseValuesTests
{
    private static readonly OrderedDictionary<string, object?> Document = Load();

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "llm_client_base.json");
        return (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
    }

    private static OrderedDictionary<string, object?> Dict(object? value) => Py.AsDict(value);

    private static List<object?> Items(object? value) => Py.AsList(value);

    [Fact]
    public void The_constants_and_the_members_of_ModelSize_match_Python()
    {
        var config = Dict(Document["config"]);
        var constants = Dict(config["constants"]);
        Assert.True(Py.AsLong(constants["DEFAULT_MAX_TOKENS"]) == Config.DefaultMaxTokens);
        Assert.True(Py.AsDouble(constants["DEFAULT_TEMPERATURE"]) == Config.DefaultTemperature);

        var members = Items(config["model_size"]).Select(m => Items(m)).ToList();
        var actual = Enum.GetValues<ModelSize>().Select(m => (PyEnum.Name(m), PyEnum.Value(m))).ToList();
        Assert.Equal(members.Select(m => ((string)m[0]!, (object)m[1]!)).ToList(), actual);
    }

    [Fact]
    public void Each_LLMConfig_keeps_the_six_attributes_of_Python()
    {
        var config = Dict(Document["config"]);
        var constants = Dict(config["constants"]);
        var cases = Items(config["cases"]);
        Assert.True(cases.Count >= 60);
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            var kwargs = Dict(recorded["kwargs"]);
            var attrs = Dict(recorded["attrs"]);

            // A key that the call leaves out takes the default of Python, which is the recorded constant.
            var actual = new LLMConfig(
                apiKey: kwargs.TryGetValue("api_key", out var apiKey) ? (string?)apiKey : null,
                model: kwargs.TryGetValue("model", out var model) ? (string?)model : null,
                baseUrl: kwargs.TryGetValue("base_url", out var baseUrl) ? (string?)baseUrl : null,
                temperature: Py.AsDouble(kwargs.TryGetValue("temperature", out var temperature) ? temperature : constants["DEFAULT_TEMPERATURE"]),
                maxTokens: Py.AsLong(kwargs.TryGetValue("max_tokens", out var maxTokens) ? maxTokens : constants["DEFAULT_MAX_TOKENS"]),
                smallModel: kwargs.TryGetValue("small_model", out var smallModel) ? (string?)smallModel : null);

            var label = PyJson.Dumps(kwargs);
            Assert.True(attrs["base_url"] as string == actual.BaseUrl, label);
            Assert.True(attrs["api_key"] as string == actual.ApiKey, label);
            Assert.True(attrs["model"] as string == actual.Model, label);
            Assert.True(attrs["small_model"] as string == actual.SmallModel, label);
            // PORT: Python keeps an int that the caller passes, and the C# property is a double. The values are equal.
            Assert.True(Py.AsDouble(attrs["temperature"]) == actual.Temperature, label);
            Assert.True(Py.AsLong(attrs["max_tokens"]) == actual.MaxTokens, label);
        }
    }

    [Fact]
    public void Each_exception_class_has_the_message_and_the_base_class_of_Python()
    {
        var cases = Items(Document["errors"]);
        Assert.True(cases.Count >= 40);
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            var name = (string)recorded["class"]!;
            if (recorded["args"] is null)
            {
                continue; // The TypeError of a missing argument has no C# form. The upstream test checks the signature.
            }

            var args = Items(recorded["args"]);
            Exception error = name switch
            {
                "RateLimitError" => args.Count == 0 ? new RateLimitException() : new RateLimitException((string)args[0]!),
                "RefusalError" => new RefusalException((string)args[0]!),
                "EmptyResponseError" => new EmptyResponseException((string)args[0]!),
                _ => throw new InvalidOperationException(name),
            };
            var label = $"{name}({PyJson.Dumps(args)})";
            Assert.True((string)recorded["message"]! == error.Message, label);
            Assert.True((string)recorded["str"]! == error.Message, label);
            Assert.Equal(typeof(Exception), error.GetType().BaseType);
            Assert.Equal(["Exception", "BaseException"], Items(recorded["base"]).Select(b => (string)b!).ToList());
        }
    }

    [Fact]
    public void The_dataclasses_of_the_tracker_give_the_totals_and_averages_of_Python()
    {
        var cases = Items(Dict(Document["token_tracker"])["dataclasses"]);
        Assert.True(cases.Count >= 40);
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            var p = new PromptTokenUsage
            {
                PromptName = "p",
                CallCount = Py.AsLong(recorded["call_count"]),
                TotalInputTokens = Py.AsLong(recorded["input"]),
                TotalOutputTokens = Py.AsLong(recorded["output"]),
            };
            var u = new TokenUsage { InputTokens = Py.AsLong(recorded["input"]), OutputTokens = Py.AsLong(recorded["output"]) };
            var label = PyJson.Dumps(recorded);
            Assert.True(Py.AsLong(recorded["prompt_total"]) == p.TotalTokens, label);
            Assert.True(Py.AsDouble(recorded["avg_in"]) == p.AvgInputTokens, label);
            Assert.True(Py.AsDouble(recorded["avg_out"]) == p.AvgOutputTokens, label);
            Assert.True(Py.AsLong(recorded["usage_total"]) == u.TotalTokens, label);
        }
    }

    [Fact]
    public void Each_series_of_record_calls_gives_the_usage_the_totals_and_the_text_of_Python()
    {
        var series = Items(Dict(Document["token_tracker"])["series"]);
        Assert.True(series.Count >= 20);
        var sortKeys = new List<string> { "total_tokens", "input_tokens", "output_tokens", "call_count", "prompt_name", "bogus", "", "TOTAL_TOKENS", "avg_input_tokens" };
        List<string> failures = [];
        var previous = Console.Out;
        try
        {
            foreach (var item in series)
            {
                var recorded = Dict(item);
                var tracker = new TokenUsageTracker();
                foreach (var call in Items(recorded["calls"]))
                {
                    var c = Items(call);
                    tracker.Record((string?)c[0], Py.AsLong(c[1]), Py.AsLong(c[2]));
                }

                CheckState(failures, "after_record", tracker, Dict(recorded["after_record"]), sortKeys);

                // A change of the copy must not reach the tracker.
                foreach (var v in tracker.GetUsage().Values)
                {
                    v.TotalInputTokens = 9999;
                    v.CallCount = 0;
                }

                CheckState(failures, "after_copy_edit", tracker, Dict(recorded["after_copy_edit"]), []);
                tracker.Reset();
                CheckState(failures, "after_reset", tracker, Dict(recorded["after_reset"]), []);
                tracker.Record("after_reset", 3, 4);
                CheckState(failures, "after_reset_and_record", tracker, Dict(recorded["after_reset_and_record"]), []);
            }
        }
        finally
        {
            Console.SetOut(previous);
        }

        Assert.True(failures.Count == 0, $"{failures.Count} recorded states differ. The first ones:\n{string.Join("\n", failures.Take(3))}");
    }

    private static void CheckState(List<string> failures, string stage, TokenUsageTracker tracker, OrderedDictionary<string, object?> expected, List<string> sortKeys)
    {
        var usage = tracker.GetUsage();
        var rows = Items(expected["usage"]).Select(r => Items(r)).ToList();
        if (rows.Count != usage.Count)
        {
            failures.Add($"{stage}: {usage.Count} prompts instead of {rows.Count}");
            return;
        }

        var index = 0;
        foreach (var (key, value) in usage)
        {
            var row = rows[index++];
            if ((string)row[0]! != key || (string)row[1]! != value.PromptName || Py.AsLong(row[2]) != value.CallCount
                || Py.AsLong(row[3]) != value.TotalInputTokens || Py.AsLong(row[4]) != value.TotalOutputTokens
                || Py.AsLong(row[5]) != value.TotalTokens || Py.AsDouble(row[6]) != value.AvgInputTokens
                || Py.AsDouble(row[7]) != value.AvgOutputTokens)
            {
                failures.Add($"{stage}: the row of '{key}' differs from {PyJson.Dumps(row)}");
            }
        }

        var total = tracker.GetTotalUsage();
        var expectedTotal = Items(expected["total"]);
        if (Py.AsLong(expectedTotal[0]) != total.InputTokens || Py.AsLong(expectedTotal[1]) != total.OutputTokens
            || Py.AsLong(expectedTotal[2]) != total.TotalTokens)
        {
            failures.Add($"{stage}: the total differs from {PyJson.Dumps(expectedTotal)}");
        }

        var summaries = Dict(expected["summaries"]);
        foreach (var key in sortKeys)
        {
            var text = Capture(() => tracker.PrintSummary(key));
            if (text != (string)summaries[key]!)
            {
                failures.Add($"{stage}: print_summary('{key}') differs:\n{text}\nexpected:\n{summaries[key]}");
            }
        }

        var defaultText = Capture(() => tracker.PrintSummary());
        if (defaultText != (string)expected["default_summary"]!)
        {
            failures.Add($"{stage}: print_summary() differs:\n{defaultText}\nexpected:\n{expected["default_summary"]}");
        }
    }

    private static string Capture(Action action)
    {
        using var writer = new StringWriter { NewLine = "\n" };
        Console.SetOut(writer);
        action();
        return writer.ToString();
    }

    [Fact]
    public async Task generate_embedding_passes_the_text_with_no_line_break_and_logs_as_Python_does()
    {
        var cases = Items(Document["utils"]);
        Assert.True(cases.Count >= 16);
        foreach (var item in cases)
        {
            var recorded = Dict(item);
            var text = (string)recorded["text"]!;
            var embedder = new FakeEmbedder();
            var capture = new CaptureProvider();
            using var factory = new CaptureFactory(capture);
            using var scope = PortServices.Use(loggerFactory: factory);

            var result = await Utils.GenerateEmbeddingAsync(embedder, text, TestContext.Current.CancellationToken);

            var label = PyJson.Dumps(text);
            var expectedInput = Items(recorded["input_data"]).Select(c => Items(c).Select(s => (string)s!).ToList()).ToList();
            Assert.True(expectedInput.Count == embedder.Calls.Count && expectedInput.Zip(embedder.Calls).All(p => p.First.SequenceEqual(p.Second)), label);
            Assert.True(Items(recorded["result"]).Select(Py.AsDouble).SequenceEqual(result), label);

            var line = Assert.Single(capture.Lines);
            Assert.Equal("DEBUG", (string)recorded["log_level"]!);
            Assert.Equal(LogLevel.Debug, line.Level);
            var head = (string)recorded["log_head"]!;
            var tail = (string)recorded["log_tail"]!;
            Assert.StartsWith(head + " in ", line.Message, StringComparison.Ordinal);
            Assert.EndsWith(tail, line.Message, StringComparison.Ordinal);
            var elapsed = line.Message[(head.Length + " in ".Length)..^tail.Length];
            Assert.True(double.TryParse(elapsed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _), $"{label}: '{elapsed}' is not a float text");
        }
    }

    private sealed class FakeEmbedder : EmbedderClient
    {
        internal List<List<string>> Calls { get; } = [];

        // The fake at the network boundary of the recorder: [float(len(input_data[0])), 0.5, -1.25].
        internal override Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            Calls.Add([.. inputData]);
            return Task.FromResult<List<double>>([Py.Len(inputData[0]), 0.5, -1.25]);
        }
    }

    private sealed class CaptureFactory(CaptureProvider provider) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider1)
        {
        }

        public ILogger CreateLogger(string categoryName) => provider.CreateLogger(categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class CaptureProvider : ILoggerProvider
    {
        internal List<(LogLevel Level, string Message)> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Lines);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<(LogLevel, string)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Add((logLevel, formatter(state, exception)));
        }
    }
}
