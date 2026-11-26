// Evaluation test for graphiti_core/llm_client/decision_prompts.py and openai_decisions_client.py (port/RULES.md,
// R23 and R25). values/llm_client_decisions.json holds what port/tools/record_decisions_client.py recorded from
// Python: the constants, about 650 calls of the pure functions, 20 calls of the two prompt builders, 150 calls of
// _map_answers, the constructor for 57 argument sets, and 108 scripted exchanges with the client. The scripted
// replies come from the recording, and the fake HttpMessageHandler gives them to the client one after the other. No
// test here sends a request.
namespace Graphiti.Port.Evaluation;

using System.Net;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.DecisionSupport;

public class LlmClientDecisionValuesTests
{
    private static readonly OrderedDictionary<string, object?> Document = Load();

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "llm_client_decisions.json");
        return (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
    }

    private static OrderedDictionary<string, object?> D(object? value) => Py.AsDict(value);

    private static List<object?> L(object? value) => Py.AsList(value);

    private static string S(object? value) => Py.AsString(value);

    private static List<OrderedDictionary<string, object?>> Dicts(object? value) => L(value).Select(Py.AsDict).ToList();

    private static OrderedDictionary<string, OrderedDictionary<string, object?>> TypedQuestions(object? value)
    {
        var questions = new OrderedDictionary<string, OrderedDictionary<string, object?>>();
        foreach (var (name, question) in D(value))
        {
            questions[name] = D(question);
        }

        return questions;
    }

    private static void AssertSameError(string label, Exception actual, List<object?> recorded)
    {
        var name = S(recorded[0]);
        Assert.True(PythonName(actual) == name, $"{label}: expected {name}, got {actual.GetType().Name}: {actual.Message}");

        // A KeyError, a TypeError, and an AttributeError have no text that the port reproduces (R25).
        if (name is "ValueError" or "JSONDecodeError" or "HTTPStatusError" or "RateLimitError" or "RuntimeError" or "OpenAIDecisionsInputTooLongError" or "ReadTimeout" or "ConnectError")
        {
            Assert.True(S(recorded[1]) == actual.Message, $"{label}: expected the text '{recorded[1]}', got '{actual.Message}'");
        }
    }

    [Fact]
    public void The_constants_are_the_ones_of_Python()
    {
        var constants = D(Document["constants"]);
        Assert.Equal(5, constants.Count);
        Assert.True(S(constants["RULES_HEADER"]) == DecisionPrompts.RulesHeader);
        Assert.True(S(constants["NODE_RULES"]) == DecisionPrompts.NodeRules);
        Assert.True(S(constants["NODE_NONE"]) == DecisionPrompts.NodeNone);
        Assert.True(S(constants["DUP_RULES"]) == DecisionPrompts.DupRules);
        Assert.True(S(constants["CON_RULES"]) == DecisionPrompts.ConRules);
    }

    private static List<object?> Tuple<T1, T2, T3, T4>((T1, T2, T3, T4) t) => [t.Item1, t.Item2, t.Item3, t.Item4];

    private static object? Invoke(string f, List<object?> a) => f switch
    {
        "tag" => DecisionPrompts.Tag(S(a[0]), S(a[1])),
        "parse_list" => DecisionPrompts.ParseList(a[0] as string),
        "shrink_state" => DecisionPrompts.ShrinkState(S(a[0])),
        "node_n6" => Tuple(DecisionPrompts.NodeN6(S(a[0]))),
        "_contra_strict" => DecisionPrompts.ContraStrict(S(a[0]), D(a[1])),
        "edge_e2" => Tuple(DecisionPrompts.EdgeE2(S(a[0]))),
        "_user_text" => DecisionPrompts.UserText(L(a[0]).Select(m => new Message { Role = S(D(m)["role"]), Content = S(D(m)["content"]) }.Validate()).ToList()),
        "_encode_payload" => DecisionPrompts.EncodePayload(S(a[0]), TypedQuestions(a[1])),
        "assemble_node_answers" => DecisionPrompts.AssembleNodeAnswers(D(a[0]), Dicts(a[1])),
        "assemble_edge_answers" => DecisionPrompts.AssembleEdgeAnswers(
            D(a[0]),
            L(a[1]).Select(Py.AsLong).ToList(),
            L(a[2]).Select(Py.AsLong).ToList(),
            Py.AsDouble(a[3]),
            Py.AsDouble(a[4])),
        _ => throw new InvalidOperationException(f),
    };

    [Fact]
    public void The_pure_functions_give_each_recorded_result_of_Python()
    {
        var constants = D(Document["constants"]);
        var cases = L(Document["pure"]);
        Assert.True(cases.Count >= 600);
        List<string> failures = [];
        foreach (var item in cases)
        {
            var recorded = D(item);
            var f = S(recorded["f"]);
            var args = L(recorded["a"]);
            var result = D(recorded["r"]);
            var label = $"{f}({PyJson.Dumps(args)})";
            try
            {
                if (result.TryGetValue("err", out var err))
                {
                    var thrown = Assert.ThrowsAny<Exception>(() => Invoke(f, args));
                    AssertSameError(label, thrown, L(err));
                }
                else if (result.ContainsKey("untyped"))
                {
                    // PORT: the result type of parse_list is list[dict[str, Any]] (R20). Python returned another shape.
                    Assert.Throws<InvalidCastException>(() => Invoke(f, args));
                }
                else
                {
                    var expected = result["ok"];
                    if (result.TryGetValue("suffix", out var suffix))
                    {
                        var rules = S(suffix) == "node"
                            ? S(constants["RULES_HEADER"]) + S(constants["NODE_RULES"])
                            : S(constants["RULES_HEADER"]) + "DUPLICATE: " + S(constants["DUP_RULES"]) + "\nCONTRADICTION: " + S(constants["CON_RULES"]);
                        var parts = L(expected);
                        parts[0] = S(parts[0]) + rules;
                    }

                    var want = PyJson.Dumps(expected);
                    var got = PyJson.Dumps(Invoke(f, args));
                    if (want != got)
                    {
                        var at = 0;
                        while (at < want.Length && at < got.Length && want[at] == got[at])
                        {
                            at++;
                        }

                        failures.Add($"{label}: the results differ at character {at}: expected ...{want[Math.Max(0, at - 40)..Math.Min(want.Length, at + 60)]} but got ...{got[Math.Max(0, at - 40)..Math.Min(got.Length, at + 60)]}");
                    }
                }
            }
            catch (Exception error)
            {
                failures.Add($"{label}: {error.GetType().Name}: {error.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} recorded results differ. The first ones:\n{string.Join("\n", failures.Take(5))}");
    }

    [Fact]
    public void The_two_prompt_builders_give_each_recorded_prompt_of_Python()
    {
        var cases = L(Document["builders"]);
        Assert.True(cases.Count >= 20);
        foreach (var item in cases)
        {
            var recorded = D(item);
            var context = D(recorded["context"]);

            // The default builder is a function of the test, as in the recording: it gives the two texts of the context.
            ChatPrompt DefaultBuilder(OrderedDictionary<string, object?> c) => new()
            {
                System = new SystemMessage { Content = S(c["system"]) }.Validate(),
                User = new UserMessage { Content = S(c["user"]) }.Validate(),
            };

            var builder = S(recorded["kind"]) == "node"
                ? DecisionPrompts.JevNodeBuilder(DefaultBuilder)
                : DecisionPrompts.JevEdgeBuilder(DefaultBuilder);
            var expected = D(D(recorded["r"])["ok"]);

            var prompt = builder(context);

            Assert.True(S(expected["system"]) == prompt.System.Content, "the system text of " + S(recorded["kind"]));
            Assert.True(S(expected["user"]) == prompt.User.Content, "the user text of " + S(recorded["kind"]));
        }
    }

    [Fact]
    public void MapAnswers_gives_each_recorded_result_or_error_of_Python()
    {
        var cases = L(Document["map_answers"]);
        Assert.True(cases.Count >= 150);
        foreach (var item in cases)
        {
            var recorded = D(item);
            var questions = TypedQuestions(recorded["questions"]);
            var label = PyJson.Dumps(recorded["raw"]);
            var result = D(recorded["r"]);
            if (result.TryGetValue("err", out var err))
            {
                var thrown = Assert.ThrowsAny<Exception>(() => OpenAIDecisionsClient.MapAnswers(questions, recorded["raw"]));
                AssertSameError(label, thrown, L(err));
            }
            else
            {
                Assert.Equal(PyJson.Dumps(result["ok"]), PyJson.Dumps(OpenAIDecisionsClient.MapAnswers(questions, recorded["raw"])));
            }
        }
    }

    [Fact]
    public void The_constructor_gives_each_recorded_attribute_or_error_of_Python()
    {
        var cases = L(Document["constructor"]);
        Assert.True(cases.Count >= 30);
        foreach (var item in cases)
        {
            var recorded = D(item);
            var configKey = recorded["config_key"] as string;
            var environmentKey = recorded["env_key"] as string;
            var environmentUrl = recorded["env_base_url"] as string;
            using var environment = PyEnv.Use(name => name switch
            {
                "OPENAI_API_KEY" => environmentKey,
                "OPENAI_BASE_URL" => environmentUrl,
                _ => null,
            });
            var config = new LLMConfig(
                apiKey: configKey,
                model: recorded["model"] as string,
                baseUrl: recorded["config_base_url"] as string,
                temperature: 0.5,
                maxTokens: 77,
                smallModel: "s");
            var result = D(recorded["r"]);
            var label = PyJson.Dumps(recorded);

            if (result.TryGetValue("err", out var err))
            {
                var thrown = Assert.ThrowsAny<Exception>(() => new OpenAIDecisionsClient(config: config, baseUrl: recorded["argument_base_url"] as string, httpClient: new HttpClient()));
                AssertSameError(label, thrown, L(err));
                continue;
            }

            var client = new OpenAIDecisionsClient(config: config, baseUrl: recorded["argument_base_url"] as string, httpClient: new HttpClient());
            var expected = D(result["ok"]);
            Assert.True(S(expected["base_url"]) == client.BaseUrl, label);
            Assert.True(S(expected["model"]) == client.Model, label);
            Assert.True(S(expected["small_model"]) == client.SmallModel, label);
            Assert.True(Py.AsDouble(expected["temperature"]) == client.Temperature, label);
            Assert.True(Py.AsLong(expected["max_tokens"]) == client.MaxTokens, label);
            Assert.True(expected["config_base_url"] as string == client.Config.BaseUrl, label);

            // `client.api_key == (config_key or env_key)`, as the recording tests it.
            var key = string.IsNullOrEmpty(configKey) ? environmentKey : configKey;
            Assert.Equal(Py.AsBool(expected["key_is_config_key"]), client.ApiKey == key);
            Assert.Equal(Py.AsBool(expected["key_in_config"]), client.Config.ApiKey == key);
            Assert.Equal(PyJson.Dumps(expected["thresholds"]), PyJson.Dumps(new List<object?> { client.DuplicateThreshold, client.ContradictionThreshold }));
            Assert.True(Py.AsDouble(expected["timeout_seconds"]) == client.TimeoutSeconds, label);
            Assert.True(Py.AsLong(expected["max_retries"]) == client.MaxRetries, label);
            Assert.Equal(Py.AsBool(expected["cache_enabled"]), client.CacheEnabled);
            client.HttpClient.Dispose();
        }
    }

    [Fact]
    public async Task The_client_gives_each_recorded_request_result_delay_usage_and_span_of_Python()
    {
        var scenarios = L(Document["scenarios"]);
        Assert.True(scenarios.Count >= 100);
        List<string> failures = [];
        var number = 0;
        foreach (var item in scenarios)
        {
            var label = $"scenario {number++}";
            try
            {
                await RunScenario(D(item), label, failures);
            }
            catch (Exception error)
            {
                failures.Add($"{label}: {error.GetType().Name}: {error.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} scenarios differ. The first ones:\n{string.Join("\n", failures.Take(5))}");
    }

    private static async Task RunScenario(OrderedDictionary<string, object?> recorded, string label, List<string> failures)
    {
        var scenario = D(recorded["scenario"]);
        var cfg = D(scenario["config"]);
        var call = D(scenario["call"]);
        var exchanges = L(recorded["exchanges"]);
        var cursor = 0;
        List<double> delays = [];
        List<string> problems = [];

        HttpResponseMessage Respond(DecisionRequest request)
        {
            if (cursor >= exchanges.Count)
            {
                problems.Add("the client sent more requests than Python did");
                return Reply(500, string.Empty);
            }

            var exchange = D(exchanges[cursor++]);
            var expected = D(exchange["request"]);
            if (S(expected["method"]) != request.Method || S(expected["url"]) != request.Url)
            {
                problems.Add($"request {cursor}: expected {expected["method"]} {expected["url"]}, got {request.Method} {request.Url}");
            }

            if (Py.AsBool(expected["authorization_ok"]) != (request.Authorization == "Bearer " + Key))
            {
                problems.Add($"request {cursor}: the Authorization header differs");
            }

            if (expected["content_type"] as string != request.ContentType)
            {
                problems.Add($"request {cursor}: the content type is {request.ContentType}, not {expected["content_type"]}");
            }

            if (S(expected["body_text"]) != request.BodyText)
            {
                problems.Add($"request {cursor}: the body is {request.BodyText}, not {expected["body_text"]}");
            }

            var reply = D(exchange["reply"]);
            if (reply.TryGetValue("raise", out var raise))
            {
                throw S(raise) == "ReadTimeout"
                    ? new TimeoutException(S(reply["message"]))
                    : new HttpRequestException(S(reply["message"]));
            }

            return Reply((int)Py.AsLong(reply["status"]), S(reply["body"]), S(reply["content_type"]));
        }

        var tracer = new RecordingTracer();
        using var patch = OpenaiDecisionsClientModule.Sleep.Use((delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });
        var client = new OpenAIDecisionsClient(
            config: new LLMConfig(apiKey: Key, model: cfg["model"] as string),
            baseUrl: cfg["base_url"] as string,
            duplicateThreshold: Py.AsDouble(cfg["duplicate_threshold"]),
            contradictionThreshold: Py.AsDouble(cfg["contradiction_threshold"]),
            maxRetries: Py.AsLong(cfg["max_retries"]),
            httpClient: HttpClientOf(new ScriptedHandler(Respond)));
        client.Tracer = tracer;
        var payload = Dict(("instructions", scenario["instructions"]), ("state", scenario["state"]), ("questions", scenario["questions"]));
        List<Message> messages = [new Message { Role = "system", Content = "system text" }.Validate()];
        if (!scenario.ContainsKey("no_user"))
        {
            messages.Add(new Message { Role = "user", Content = PyJson.Dumps(payload) }.Validate());
        }

        var result = await Asyncio.Run(async () =>
        {
            try
            {
                return (Result: (object?)await client.GenerateResponseAsync(
                    messages,
                    modelSize: PyEnum.FromValue<ModelSize>(call["model_size"]),
                    maxTokens: call["max_tokens"] is null ? null : Py.AsLong(call["max_tokens"]),
                    promptName: scenario["prompt_name"] as string,
                    model: call["model"] as string,
                    cancellationToken: TestContext.Current.CancellationToken), Error: (Exception?)null);
            }
            catch (Exception error)
            {
                return (Result: null, Error: error);
            }
        });
        client.HttpClient.Dispose();

        foreach (var problem in problems)
        {
            failures.Add($"{label}: {problem}");
        }

        var outcome = D(recorded["outcome"]);
        if (outcome.TryGetValue("err", out var err))
        {
            if (result.Error is null)
            {
                failures.Add($"{label}: Python raised {PyJson.Dumps(err)} and the port returned {PyJson.Dumps(result.Result)}");
            }
            else
            {
                AssertSameError(label, result.Error, L(err));
            }
        }
        else if (result.Error is not null)
        {
            failures.Add($"{label}: the port raised {result.Error.GetType().Name}: {result.Error.Message}");
        }
        else if (PyJson.Dumps(outcome["ok"]) != PyJson.Dumps(result.Result))
        {
            failures.Add($"{label}: the results differ: expected {PyJson.Dumps(outcome["ok"])} but got {PyJson.Dumps(result.Result)}");
        }

        if (cursor != exchanges.Count)
        {
            failures.Add($"{label}: the client sent {cursor} requests, and Python sent {exchanges.Count}");
        }

        var expectedDelays = L(recorded["delays"]).Select(Py.AsDouble).ToList();
        if (!expectedDelays.SequenceEqual(delays))
        {
            failures.Add($"{label}: the delays are [{string.Join(", ", delays)}], not [{string.Join(", ", expectedDelays)}]");
        }

        var usage = D(recorded["usage"]);
        var total = client.TokenTracker.GetTotalUsage();
        if (PyJson.Dumps(usage["total"]) != PyJson.Dumps(new List<object?> { total.InputTokens, total.OutputTokens }))
        {
            failures.Add($"{label}: the total usage is [{total.InputTokens}, {total.OutputTokens}], not {PyJson.Dumps(usage["total"])}");
        }

        var byPrompt = client.TokenTracker.GetUsage().Select(kv => (object?)new List<object?> { kv.Key, kv.Value.CallCount, kv.Value.TotalInputTokens, kv.Value.TotalOutputTokens }).ToList();
        if (PyJson.Dumps(usage["by_prompt"]) != PyJson.Dumps(byPrompt))
        {
            failures.Add($"{label}: the usage by prompt is {PyJson.Dumps(byPrompt)}, not {PyJson.Dumps(usage["by_prompt"])}");
        }

        var spans = Dicts(recorded["spans"]);
        if (spans.Count != tracer.Spans.Count)
        {
            failures.Add($"{label}: {tracer.Spans.Count} spans, not {spans.Count}");
            return;
        }

        for (var i = 0; i < spans.Count; i++)
        {
            var expectedSpan = spans[i];
            var span = tracer.Spans[i];
            if (S(expectedSpan["name"]) != span.Name)
            {
                failures.Add($"{label}: the span is named {span.Name}, not {expectedSpan["name"]}");
            }

            if (PyJson.Dumps(expectedSpan["attributes"], sortKeys: true) != PyJson.Dumps(span.Attributes, sortKeys: true))
            {
                failures.Add($"{label}: the span attributes are {PyJson.Dumps(span.Attributes)}, not {PyJson.Dumps(expectedSpan["attributes"])}");
            }

            var status = span.Status is null ? null : new List<object?> { span.Status[0], span.Status[1] };
            if (PyJson.Dumps(expectedSpan["status"]) != PyJson.Dumps(status))
            {
                failures.Add($"{label}: the span status is {PyJson.Dumps(status)}, not {PyJson.Dumps(expectedSpan["status"])}");
            }

            var exceptions = span.Exceptions.Select(e => (object?)new List<object?> { PythonName(e.Error), e.Message }).ToList();
            if (PyJson.Dumps(expectedSpan["exceptions"]) != PyJson.Dumps(exceptions))
            {
                failures.Add($"{label}: the span exceptions are {PyJson.Dumps(exceptions)}, not {PyJson.Dumps(expectedSpan["exceptions"])}");
            }
        }
    }
}
