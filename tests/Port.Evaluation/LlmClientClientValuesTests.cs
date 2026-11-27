// Evaluation test for graphiti_core/llm_client/client.py (port/RULES.md, R23 and R25). values/llm_client.json holds what
// port/tools/record_llm_client.py recorded from Python for generated inputs: the module-level members, _clean_input,
// _apply_attribute_extraction_preamble, _get_cache_key (J4), _get_provider_type, _get_failed_generation_log,
// generate_response with the cache off and with the cache on (a miss, a hit, the key with model=, and a reply that JSON
// cannot hold, with the rows of the cache file), generate_response when the task is canceled, the attributes that the
// constructor sets, and _generate_response_with_retry (J3).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using static Graphiti.Port.Evaluation.LlmClientClientSupport;

public class LlmClientClientValuesTests
{
    private static List<OrderedDictionary<string, object?>> Cases(string name) => Items(Document[name]).Select(Dict).ToList();

    [Fact]
    public void The_constants_and_the_language_instruction_match_Python()
    {
        var constants = Dict(Document["constants"]);
        Assert.Equal(Client.DefaultCacheDir, (string)constants["DEFAULT_CACHE_DIR"]!);
        foreach (var item in Items(constants["language_instruction"]))
        {
            var pair = Items(item);
            Assert.Equal((string)pair[1]!, Client.GetExtractionLanguageInstruction((string?)pair[0]));
        }

        Assert.Equal((string)constants["language_instruction_default"]!, Client.GetExtractionLanguageInstruction());
    }

    [Fact]
    public void is_server_or_retry_error_gives_the_answer_of_Python_for_each_exception_type()
    {
        var cases = Cases("is_server_or_retry_error");
        Assert.True(cases.Count >= 30);
        foreach (var recorded in cases)
        {
            var spec = Dict(recorded["spec"]);
            var label = PyJson.Dumps(spec);
            Assert.True((bool)recorded["result"]! == Client.IsServerOrRetryError(MakeError(spec)), label);
        }
    }

    [Fact]
    public void clean_input_gives_the_text_of_Python()
    {
        var cases = Cases("clean_input");
        Assert.True(cases.Count >= 300);
        List<string> failures = [];
        foreach (var recorded in cases)
        {
            var input = (string)recorded["input"]!;
            var actual = LLMClient.CleanInput(input);
            if (actual != (string)recorded["output"]!)
            {
                failures.Add($"{PyJson.Dumps(input)}: {PyJson.Dumps(actual)} instead of {PyJson.Dumps(recorded["output"])}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} texts differ. The first ones:\n{string.Join("\n", failures.Take(5))}");
    }

    [Fact]
    public void The_attribute_extraction_preamble_gives_the_messages_of_Python_and_is_idempotent()
    {
        var cases = Cases("preamble");
        Assert.True(cases.Count >= 100);
        foreach (var recorded in cases)
        {
            var messages = Messages(recorded["messages"]);
            var attributeExtraction = (bool)recorded["attribute_extraction"]!;
            var label = PyJson.Dumps(recorded["messages"]) + " " + attributeExtraction;

            LLMClient.ApplyAttributeExtractionPreamble(messages, attributeExtraction);
            Assert.True(PyJson.Dumps(Pairs(messages)) == PyJson.Dumps(recorded["once"]), label);
            LLMClient.ApplyAttributeExtractionPreamble(messages, attributeExtraction);
            Assert.True(PyJson.Dumps(Pairs(messages)) == PyJson.Dumps(recorded["twice"]), label);
        }
    }

    [Fact]
    public void The_cache_key_is_the_md5_of_Python()
    {
        var cases = Cases("cache_key");
        Assert.True(cases.Count >= 80);
        foreach (var recorded in cases)
        {
            var client = new ScriptedClient("MockLLMClient", new LLMConfig(model: (string?)recorded["config_model"]));
            var key = client.GetCacheKey(Messages(recorded["messages"]), (string?)recorded["model"]);
            Assert.True((string)recorded["key"]! == key, PyJson.Dumps(recorded));
        }

        // The call with no model argument takes the model of the config.
        var last = cases[^1];
        Assert.Equal((string)last["key"]!, new ScriptedClient("MockLLMClient", new LLMConfig(model: "m1")).GetCacheKey(Messages(last["messages"])));
    }

    [Fact]
    public void The_provider_type_comes_from_the_class_name()
    {
        var cases = Cases("provider_type");
        Assert.True(cases.Count >= 20);
        foreach (var recorded in cases)
        {
            var client = new ScriptedClient((string)recorded["class_name"]!, new LLMConfig());
            Assert.True((string)recorded["provider"]! == client.GetProviderType(), PyJson.Dumps(recorded["class_name"]));
        }
    }

    [Fact]
    public void The_failed_generation_log_gives_the_text_of_Python()
    {
        var cases = Cases("failed_generation_log");
        Assert.True(cases.Count >= 40);
        foreach (var recorded in cases)
        {
            var messages = Items(recorded["roles"]).Select(r => new Message { Role = (string)r!, Content = "c" }).ToList();
            var log = LLMClient.GetFailedGenerationLog(messages, (string?)recorded["output"]);
            Assert.True((string)recorded["log"]! == log, PyJson.Dumps(recorded));
        }
    }

    [Fact]
    public async Task generate_response_gives_Python_the_messages_the_arguments_and_the_span_events()
    {
        var cases = Cases("generate_response");
        Assert.True(cases.Count >= 100);
        List<string> failures = [];
        foreach (var recorded in cases)
        {
            var events = new List<object?>();
            var client = new ScriptedClient(
                (string)recorded["class_name"]!,
                new LLMConfig(model: (string?)recorded["config_model"], maxTokens: Py.AsLong(recorded["config_max_tokens"])));
            client.SetTracer(new RecordingTracer(events));
            var reply = new OrderedDictionary<string, object?> { ["content"] = "ok" };
            client.Script = _ => reply;
            var behavior = recorded["behavior"] as OrderedDictionary<string, object?>;
            if (behavior is not null)
            {
                var error = MakeError(behavior);
                client.Script = _ => throw error;
            }

            var messages = Messages(recorded["messages"]);
            var responseModel = (string?)recorded["response_model"] switch
            {
                null => null,
                "NodeDuplicate" => NodeDuplicate.ResponseModel,
                "NodeResolutions" => NodeResolutions.ResponseModel,
                var other => throw new InvalidOperationException(other),
            };
            var modelSize = PyEnum.FromValue<ModelSize>((string)recorded["model_size"]!);
            var clock = new InstantTimeProvider();
            using var scope = PortServices.Use(time: clock, random: new PyRandom(3), loggerFactory: new CaptureFactory());

            var outcome = Dict(recorded["outcome"]);
            string label = PyJson.Dumps(new OrderedDictionary<string, object?> { ["messages"] = recorded["messages"], ["response_model"] = recorded["response_model"], ["behavior"] = recorded["behavior"] });
            Exception? thrown = null;
            OrderedDictionary<string, object?>? result = null;
            try
            {
                result = await client.GenerateResponseAsync(
                    messages,
                    responseModel,
                    (long?)(recorded["max_tokens"] is null ? null : Py.AsLong(recorded["max_tokens"])),
                    modelSize,
                    (string?)recorded["group_id"],
                    (string?)recorded["prompt_name"],
                    (bool)recorded["attribute_extraction"]!,
                    (string?)recorded["model"],
                    TestContext.Current.CancellationToken);
            }
            catch (Exception e)
            {
                thrown = e;
            }

            // What reached _generate_response, and what the caller's list holds afterwards.
            var received = Items(recorded["received"]).Select(Dict).ToList();
            if (received.Count != client.Calls.Count)
            {
                failures.Add($"{label}: {client.Calls.Count} calls instead of {received.Count}");
                continue;
            }

            for (var i = 0; i < received.Count; i++)
            {
                var call = client.Calls[i];
                var kwargs = Dict(received[i]["kwargs"]);
                var same = PyJson.Dumps(call.Messages) == PyJson.Dumps(received[i]["messages"])
                    && call.ResponseModel == (string?)received[i]["response_model"]
                    && call.MaxTokens == Py.AsLong(received[i]["max_tokens"])
                    && call.ModelSize == (string)received[i]["model_size"]!
                    && call.Model == (kwargs.TryGetValue("model", out var m) ? (string?)m : null);
                if (!same)
                {
                    failures.Add($"{label}: the call {i} differs from {PyJson.Dumps(received[i])}");
                }
            }

            if (PyJson.Dumps(Pairs(messages)) != PyJson.Dumps(recorded["caller_messages_after"]) && received.Count > 0)
            {
                failures.Add($"{label}: the list of the caller differs");
            }

            if (PyJson.Dumps(events) != PyJson.Dumps(recorded["events"]))
            {
                failures.Add($"{label}: the span events are {PyJson.Dumps(events)} instead of {PyJson.Dumps(recorded["events"])}");
            }

            if (outcome.TryGetValue("is_same_object", out var isSameObject))
            {
                if (thrown is not null || !ReferenceEquals(result, reply) || !(bool)isSameObject!)
                {
                    failures.Add($"{label}: the result is not the object of the call ({thrown})");
                }
            }
            else
            {
                var errorName = (string)outcome["error"]!;
                var matches = errorName switch
                {
                    "IndexError" => thrown is ArgumentOutOfRangeException,
                    "ValueError" => thrown is ArgumentException { Message: var message } && message == (string)outcome["message"]!,
                    "RefusalError" => thrown is RefusalException { Message: var message } && message == (string)outcome["message"]!,
                    "RuntimeError" => thrown is InvalidOperationException { Message: var message } && message == (string)outcome["message"]!,
                    "RateLimitError" => thrown is RateLimitException { Message: var message } && message == (string)outcome["message"]!,
                    _ => throw new InvalidOperationException(errorName),
                };
                if (!matches)
                {
                    failures.Add($"{label}: {errorName} expected, and the call threw {thrown}");
                }
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} calls differ. The first ones:\n{string.Join("\n", failures.Take(3))}");
    }

    private static string LevelName(LogLevel level) => level switch
    {
        LogLevel.Warning => "WARNING",
        LogLevel.Debug => "DEBUG",
        LogLevel.Error => "ERROR",
        LogLevel.Information => "INFO",
        _ => level.ToString(),
    };

    private static List<object?> LogLines(CaptureFactory log) =>
        log.Lines.Select(l => (object?)new List<object?> { LevelName(l.Level), l.Message }).ToList();

    // The cache path of generate_response: the scenarios run on one client with a cache file in a temporary directory.
    // What is compared: the call count, the result, the span events, the log lines, and the rows of the file after each call.
    [Fact]
    public async Task generate_response_with_the_cache_on_gives_Python_the_events_the_result_the_rows_and_the_log()
    {
        var scenarios = Cases("cache_path");
        Assert.True(scenarios.Count >= 3);
        List<string> failures = [];
        foreach (var scenario in scenarios)
        {
            var dir = Path.Combine(Path.GetTempPath(), "graphiti-client-cache-" + Guid.NewGuid().ToString("N"));
            var cache = new LLMCache(dir);
            try
            {
                var calls = Items(scenario["calls"]).Select(Dict).ToList();
                var index = 0;
                OrderedDictionary<string, object?>? lastReply = null;
                var client = new ScriptedClient("MockLLMClient", new LLMConfig(model: "m")) { CacheEnabled = true, CacheDir = cache };
                client.Script = n =>
                {
                    var unserializable = (string)Dict(calls[index]["spec"])["reply"]! == "unserializable";
                    lastReply = new OrderedDictionary<string, object?> { ["content"] = unserializable ? new object() : $"reply {n + 1}" };
                    return lastReply;
                };

                foreach (var recorded in calls)
                {
                    var spec = Dict(recorded["spec"]);
                    var label = $"{scenario["name"]} call {index}";
                    var events = new List<object?>();
                    client.SetTracer(new RecordingTracer(events));
                    var log = new CaptureFactory();
                    using var scope = PortServices.Use(time: new InstantTimeProvider(), random: new PyRandom(3), loggerFactory: log);
                    var before = client.Calls.Count;
                    var result = await client.GenerateResponseAsync(
                        Messages(spec["messages"]),
                        promptName: (string?)spec["prompt_name"],
                        model: (string?)spec["model"],
                        cancellationToken: TestContext.Current.CancellationToken);

                    if (client.Calls.Count - before != Py.AsLong(recorded["called"]))
                    {
                        failures.Add($"{label}: {client.Calls.Count - before} calls of _generate_response instead of {recorded["called"]}");
                    }

                    if (recorded["result"] is string recordedResult)
                    {
                        if (PyJson.Dumps(result) != recordedResult)
                        {
                            failures.Add($"{label}: the result is {PyJson.Dumps(result)} instead of {recordedResult}");
                        }
                    }
                    else if (!ReferenceEquals(result, lastReply))
                    {
                        failures.Add($"{label}: the reply that JSON cannot hold is not returned as it is");
                    }

                    if (PyJson.Dumps(events) != PyJson.Dumps(recorded["events"]))
                    {
                        failures.Add($"{label}: the span events are {PyJson.Dumps(events)} instead of {PyJson.Dumps(recorded["events"])}");
                    }

                    if (PyJson.Dumps(LogLines(log)) != PyJson.Dumps(recorded["log"]))
                    {
                        failures.Add($"{label}: the log is {PyJson.Dumps(LogLines(log))} instead of {PyJson.Dumps(recorded["log"])}");
                    }

                    var rows = StoredRows(Path.Combine(dir, "cache.db"));
                    if (PyJson.Dumps(rows) != PyJson.Dumps(recorded["rows"]))
                    {
                        failures.Add($"{label}: the rows are {PyJson.Dumps(rows)} instead of {PyJson.Dumps(recorded["rows"])}");
                    }

                    index++;
                }

                if (client.Calls.Count != Py.AsLong(scenario["received_total"]))
                {
                    failures.Add($"{scenario["name"]}: {client.Calls.Count} calls in all instead of {scenario["received_total"]}");
                }
            }
            finally
            {
                cache.Close();
                Directory.Delete(dir, recursive: true);
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} calls differ. The first ones:\n{string.Join("\n", failures.Take(3))}");
    }

    private static List<object?> StoredRows(string dbPath)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT key, value FROM cache ORDER BY key";
        using var reader = command.ExecuteReader();
        var rows = new List<object?>();
        while (reader.Read())
        {
            rows.Add(new List<object?> { reader.GetString(0), reader.GetString(1) });
        }

        return rows;
    }

    // A cancel while an attempt fails with a retried error lands in the sleep of tenacity, so the call ends with the
    // cancellation, with the log lines of the attempts that failed before, and with no status on the span. After the last
    // attempt tenacity does not sleep, and the retried error leaves. A task that raises the cancellation itself leaves as it is.
    [Fact]
    public async Task generate_response_ends_as_Python_does_when_the_task_is_canceled()
    {
        var cases = Cases("cancel");
        Assert.True(cases.Count >= 7);
        foreach (var recorded in cases)
        {
            var spec = Dict(recorded["spec"]);
            var mode = (string)spec["mode"]!;
            var attempt = (int)Py.AsLong(spec["attempt"]);
            var label = PyJson.Dumps(spec);
            using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var client = new ScriptedClient("MockLLMClient", new LLMConfig());
            client.Script = n =>
            {
                if (n + 1 < attempt)
                {
                    throw new RateLimitException("rate");
                }

                cancel.Cancel();
                if (mode == "cancel_then_rate_limit")
                {
                    throw new RateLimitException("rate");
                }

                throw new OperationCanceledException(cancel.Token);
            };
            var events = new List<object?>();
            client.SetTracer(new RecordingTracer(events));
            var log = new CaptureFactory();
            using var scope = PortServices.Use(time: new InstantTimeProvider(), random: new PyRandom(3), loggerFactory: log);

            Exception? thrown = null;
            try
            {
                await client.GenerateResponseAsync([new() { Role = "user", Content = "hi" }], cancellationToken: cancel.Token);
            }
            catch (Exception e)
            {
                thrown = e;
            }

            Assert.True(client.Calls.Count == Py.AsLong(recorded["attempts"]), $"{label}: {client.Calls.Count} attempts");
            var outcome = Dict(recorded["outcome"]);
            switch ((string)outcome["type"]!)
            {
                case "CancelledError":
                    Assert.True(thrown is OperationCanceledException, $"{label}: {thrown}");
                    break;
                case "RateLimitError":
                    Assert.True(thrown is RateLimitException { Message: var message } && message == (string)outcome["message"]!, $"{label}: {thrown}");
                    break;
                default:
                    throw new InvalidOperationException(PyJson.Dumps(outcome));
            }

            Assert.True(PyJson.Dumps(LogLines(log)) == PyJson.Dumps(recorded["log"]), $"{label}: the log is {PyJson.Dumps(LogLines(log))}");
            Assert.True(PyJson.Dumps(events) == PyJson.Dumps(recorded["events"]), $"{label}: the span events are {PyJson.Dumps(events)}");
        }
    }

    // The attributes that LLMClient.__init__ sets, for each LLMConfig variant and for no config.
    [Fact]
    public void The_constructor_of_LLMClient_sets_the_attributes_of_Python()
    {
        var init = Dict(Document["client_init"]);
        var cases = Items(init["cases"]).Select(Dict).ToList();
        Assert.True(cases.Count >= 61);
        foreach (var recorded in cases)
        {
            var kwargs = Dict(recorded["kwargs"]);
            var config = MakeConfig(kwargs);
            var client = new ScriptedClient("MockLLMClient", config);
            Assert.Same(config, client.Config);
            AssertAttributes(client, Dict(recorded["attrs"]), PyJson.Dumps(kwargs));
        }

        var none = new ScriptedClient("MockLLMClient", null);
        AssertAttributes(none, Dict(init["no_config"]), "no config");
    }

    // cache=True creates ./llm_cache/cache.db in the working directory.
    [Fact]
    public void The_constructor_of_LLMClient_with_cache_true_makes_the_cache_file_of_Python()
    {
        var withCache = Dict(Dict(Document["client_init"])["with_cache"]);
        var directory = Path.Combine(Directory.GetCurrentDirectory(), "llm_cache");
        var existed = Directory.Exists(directory);
        var client = new ScriptedClient("MockLLMClient", new LLMConfig(model: "m"), cache: true);
        try
        {
            AssertAttributes(client, Dict(withCache["attrs"]), "cache=True");
            Assert.Equal((bool)withCache["db_exists"]!, File.Exists(Path.Combine(directory, "cache.db")));
        }
        finally
        {
            client.CacheDir?.Close();
            if (!existed)
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void AssertAttributes(LLMClient client, OrderedDictionary<string, object?> attrs, string label)
    {
        Assert.True((string?)attrs["model"] == client.Model, label);
        Assert.True((string?)attrs["small_model"] == client.SmallModel, label);
        Assert.True(Py.AsDouble(attrs["temperature"]) == client.Temperature, label);
        Assert.True(Py.AsLong(attrs["max_tokens"]) == client.MaxTokens, label);
        Assert.True((bool)attrs["cache_enabled"]! == client.CacheEnabled, label);
        Assert.True((string?)attrs["cache_dir"] == (client.CacheDir is null ? null : "LLMCache"), label);
        Assert.True((string)attrs["tracer"]! == client.Tracer.GetType().Name, label);
        Assert.True((string)attrs["token_tracker"]! == client.TokenTracker.GetType().Name, label);

        // The config keeps its own values.
        var config = Dict(attrs["config"]);
        Assert.True((string?)config["base_url"] == client.Config.BaseUrl, label);
        Assert.True((string?)config["api_key"] == client.Config.ApiKey, label);
        Assert.True((string?)config["model"] == client.Config.Model, label);
        Assert.True((string?)config["small_model"] == client.Config.SmallModel, label);
        Assert.True(Py.AsDouble(config["temperature"]) == client.Config.Temperature, label);
        Assert.True(Py.AsLong(config["max_tokens"]) == client.Config.MaxTokens, label);
    }

    // R23, J3. Each scenario is the list of outcomes of the attempts. The expected count of attempts, the log lines,
    // the result or the exception that leaves, and the highs of the waits come from Python. The fake clock keeps each
    // delay. The port draws each wait from PyRandom, whose sequence is not Python's (GAPS F9): the test seeds one source,
    // computes the expected wait as random.uniform(5, high) from a second source with the same seed, and checks that the
    // wait is also in [5, high]. The delay of Polly has the resolution of a millisecond, so the test allows that difference.
    [Fact]
    public async Task _generate_response_with_retry_gives_the_attempts_the_waits_the_log_and_the_exception_of_Python()
    {
        var retry = Dict(Document["retry"]);
        var cases = Items(retry["cases"]).Select(Dict).ToList();
        Assert.True(cases.Count >= 40);
        List<string> failures = [];
        var seed = 100;
        foreach (var recorded in cases)
        {
            seed++;
            var script = Items(recorded["script"]).Select(Dict).ToList();
            var errors = script.Select(o => o.ContainsKey("kind") ? MakeError(o) : new InvalidOperationException("an attempt that returns")).ToList();
            var client = new ScriptedClient("RetryClient")
            {
                Script = n => script[n].TryGetValue("return", out var value)
                    ? new OrderedDictionary<string, object?> { ["content"] = value }
                    : throw errors[n],
            };
            var clock = new InstantTimeProvider();
            var log = new CaptureFactory();
            using var scope = PortServices.Use(time: clock, random: new PyRandom(seed), loggerFactory: log);
            var label = PyJson.Dumps(recorded["script"]);

            Exception? thrown = null;
            OrderedDictionary<string, object?>? result = null;
            try
            {
                result = await client.GenerateResponseWithRetryAsync([new() { Role = "user", Content = "hi" }], cancellationToken: TestContext.Current.CancellationToken);
            }
            catch (Exception e)
            {
                thrown = e;
            }

            if (client.Calls.Count != Py.AsLong(recorded["attempts"]))
            {
                failures.Add($"{label}: {client.Calls.Count} attempts instead of {recorded["attempts"]}");
            }

            var sleeps = Items(recorded["sleeps"]).Select(Py.AsDouble).ToList();
            var uniforms = Items(recorded["uniform_args"]).Select(u => Items(u).Select(Py.AsDouble).ToList()).ToList();
            if (clock.Delays.Count != sleeps.Count)
            {
                failures.Add($"{label}: {clock.Delays.Count} waits instead of {sleeps.Count}");
            }
            else
            {
                var source = new PyRandom(seed);
                for (var i = 0; i < sleeps.Count; i++)
                {
                    var expected = source.Uniform(uniforms[i][0], uniforms[i][1]);
                    var seconds = clock.Delays[i].TotalSeconds;
                    if (Math.Abs(seconds - expected) > 1e-3 || seconds < uniforms[i][0] || seconds > uniforms[i][1])
                    {
                        failures.Add($"{label}: the wait {i} is {seconds} ({clock.Delays[i].Ticks} ticks, expected {expected}), and Python's window is {PyJson.Dumps(uniforms[i])}");
                    }
                }
            }

            var recordedLog = Items(recorded["log"]).Select(l => Items(l)).ToList();
            if (log.Lines.Count != recordedLog.Count
                || !log.Lines.Zip(recordedLog).All(p => p.First.Level == LogLevel.Warning && (string)p.Second[0]! == "WARNING" && p.First.Message == (string)p.Second[1]!))
            {
                failures.Add($"{label}: the log is {PyJson.Dumps(log.Lines.Select(l => l.Message).ToList())} instead of {PyJson.Dumps(recorded["log"])}");
            }

            var outcome = Dict(recorded["outcome"]);
            if (outcome.TryGetValue("result", out var recordedResult))
            {
                var content = Dict(recordedResult)["content"];
                if (thrown is not null || result is null || (string?)result["content"] != (string?)content)
                {
                    failures.Add($"{label}: the result is {result?["content"]} ({thrown}) instead of {content}");
                }
            }
            else if (thrown is null || !ReferenceEquals(thrown, errors[(int)Py.AsLong(outcome["raised_attempt"]) - 1]))
            {
                failures.Add($"{label}: the exception of attempt {outcome["raised_attempt"]} did not leave ({thrown})");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} scenarios differ. The first ones:\n{string.Join("\n", failures.Take(3))}");
    }
}
