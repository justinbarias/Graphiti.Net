// Evaluation test for graphiti_core/llm_client/decision_prompts.py and openai_decisions_client.py (port/RULES.md,
// R23 and R25). values/llm_client_decisions.json holds what port/tools/record_decisions_client.py recorded from
// Python: the constants, about 800 calls of the pure functions, 20 calls of the two prompt builders, 150 calls of
// _map_answers, the two prompt override functions (the default library, a passed one, and a library of texts), the
// constructor for 57 argument sets and for 8 timeouts of the default client, about 200 scripted exchanges with the client, the delay of _sleep and a cancel during it, _generate_response, and a redirect. The
// scripted replies come from the recording, and the fake HttpMessageHandler gives them to the client one after the
// other. The one test of the default client talks to a server on 127.0.0.1. No test here leaves the machine.
namespace Graphiti.Port.Evaluation;

using System.Net;
using Graphiti.Core.LlmClient;
using Graphiti.Internal;
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

        // A KeyError, a TypeError, a UnicodeEncodeError, and an OverflowError have no text that the port reproduces (R25).
        if (name is "ValueError" or "JSONDecodeError" or "HTTPStatusError" or "RateLimitError" or "RuntimeError" or "OpenAIDecisionsInputTooLongError" or "ReadTimeout" or "ConnectError"
            or "ReadError" or "AttributeError" or "UnicodeDecodeError" or "NotImplementedError")
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
                else if (result.TryGetValue("limit", out var limit))
                {
                    // An int that a long cannot hold: Python returns the value, and the port throws (the limit of R4).
                    var thrown = Assert.ThrowsAny<Exception>(() => Invoke(f, args));
                    Assert.True(thrown.GetType().Name == S(limit), $"{label}: expected {limit}, got {thrown.GetType().Name}");
                }
                else if (result.TryGetValue("repr", out var repr))
                {
                    // A value that the file cannot hold, such as a dict with an int key: the repr of the Python value.
                    var got = Py.Repr(Invoke(f, args));
                    Assert.True(S(repr) == got, $"{label}: expected {repr} but got {got}");
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

    // openai_decisions_prompt_overrides() and jev_prompt_overrides(): the prompt names that they override, and what each
    // builder renders for a context of the recording. The recording holds the SHA-256 and the length of each text of the
    // prompt that Python rendered, because a rendered prompt is long. The library of the case is the default library,
    // the default library passed in, or a library of two builders that give the texts of the context.
    [Fact]
    public void The_two_prompt_override_functions_give_the_builders_that_Python_gives()
    {
        var cases = L(Document["prompt_overrides"]);
        Assert.Equal(6, cases.Count);
        var rendered = 0;
        foreach (var item in cases)
        {
            var recorded = D(item);
            var source = S(recorded["source"]);
            var libraryName = S(recorded["library"]);
            var label = $"{source} with {libraryName}";
            IPromptLibrary? library = libraryName switch
            {
                "default" => null,
                "default_passed" => Lib.CreatePromptLibrary(),
                _ => Lib.CreatePromptLibrary(new()
                {
                    ["dedupe_nodes"] = new() { ["nodes"] = TextBuilder },
                    ["dedupe_edges"] = new() { ["resolve_edge"] = TextBuilder },
                }),
            };

            var overrides = source == "jev_prompt_overrides"
                ? DecisionPrompts.JevPromptOverrides(library)
                : OpenaiDecisionsClientModule.OpenaiDecisionsPromptOverrides(library);

            Assert.Equal(L(recorded["flat"]).Select(S).ToList(), PromptConfig.FlattenOverrides(overrides).Keys.ToList());
            rendered += AssertRendered(D(recorded)["node"], overrides.DedupeNodes!.Nodes!, label + " nodes");
            rendered += AssertRendered(D(recorded)["edge"], overrides.DedupeEdges!.ResolveEdge!, label + " resolve_edge");
        }

        Assert.True(rendered >= 40);
    }

    // The texts come from Python 3.12 with the pinned upstream:
    //   from graphiti_core.llm_client import decision_prompts as dp
    //   dp.edge_e2("<EXISTING FACTS>\n[]\n</EXISTING FACTS>\n<FACT INVALIDATION CANDIDATES>\n{}\n"
    //              "</FACT INVALIDATION CANDIDATES>\n<NEW FACT>\nAlice works at Acme\n</NEW FACT>")
    [Theory]
    [InlineData("[]", "{}", "can only concatenate list (not \"dict\") to list")]
    [InlineData("\"\"", "[]", "can only concatenate str (not \"list\") to str")]
    [InlineData("[]", "\"\"", "can only concatenate list (not \"str\") to list")]
    [InlineData("\"\"", "{}", "can only concatenate str (not \"dict\") to str")]
    [InlineData("()", "[]", "can only concatenate tuple (not \"list\") to tuple")]
    [InlineData("[]", "()", "can only concatenate list (not \"tuple\") to list")]
    [InlineData("{}", "{}", "unsupported operand type(s) for +: 'dict' and 'dict'")]
    [InlineData("[]", "None", "'NoneType' object is not iterable")]
    public void edge_e2_gives_the_TypeError_text_of_Python_for_two_bodies_that_do_not_add(
        string existing, string candidates, string expected)
    {
        var user = $"<EXISTING FACTS>\n{existing}\n</EXISTING FACTS>\n<FACT INVALIDATION CANDIDATES>\n{candidates}\n"
            + "</FACT INVALIDATION CANDIDATES>\n<NEW FACT>\nAlice works at Acme\n</NEW FACT>";

        var error = Assert.Throws<InvalidCastException>(() => DecisionPrompts.EdgeE2(user));

        Assert.Equal(expected, error.Message);
    }

    [Theory]
    [InlineData("[]", "[]")]
    [InlineData("()", "()")]
    [InlineData("\"\"", "\"\"")]
    public void edge_e2_accepts_two_empty_bodies_of_one_type(string existing, string candidates)
    {
        var user = $"<EXISTING FACTS>\n{existing}\n</EXISTING FACTS>\n<FACT INVALIDATION CANDIDATES>\n{candidates}\n"
            + "</FACT INVALIDATION CANDIDATES>\n<NEW FACT>\nAlice works at Acme\n</NEW FACT>";

        Assert.Empty(DecisionPrompts.EdgeE2(user).Questions);
    }

    private static ChatPrompt TextBuilder(OrderedDictionary<string, object?> context) => new()
    {
        System = new SystemMessage { Content = S(context["system"]) }.Validate(),
        User = new UserMessage { Content = S(context["user"]) }.Validate(),
    };

    private static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    private static int AssertRendered(object? recordedCases, ChatPromptFunction builder, string label)
    {
        var count = 0;
        foreach (var item in L(recordedCases))
        {
            var recorded = D(item);
            var context = D(recorded["context"]);
            var expected = D(recorded["r"]);
            Assert.True(expected.ContainsKey("ok"), $"{label}: Python raised {PyJson.Dumps(expected)}");
            var texts = D(expected["ok"]);

            var prompt = builder(context);

            foreach (var (key, text) in new[] { ("system", prompt.System.Content), ("user", prompt.User.Content) })
            {
                var wanted = D(texts[key]);
                Assert.True(S(wanted["sha256"]) == Sha256Hex(text), $"{label}: the {key} text of {PyJson.Dumps(context)}");
                Assert.Equal(Py.AsLong(wanted["length"]), (long)Py.Len(text));
            }

            count++;
        }

        return count;
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
        var byClock = scenario.ContainsKey("by_clock");
        var timeoutSeconds = cfg.TryGetValue("timeout_seconds", out var timeoutValue) ? Py.AsDouble(timeoutValue) : 120.0;
        using var waiting = new SemaphoreSlim(0);

        async Task<HttpResponseMessage> Respond(DecisionRequest request, CancellationToken cancellationToken)
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
                if (byClock && S(raise) == "ReadTimeout")
                {
                    // The request takes too long: the client ends it with its own timer, when the clock moves.
                    waiting.Release();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                throw S(raise) switch
                {
                    "ReadTimeout" => new TimeoutException(S(reply["message"])),
                    "ReadError" => new IOException(S(reply["message"])),
                    _ => new HttpRequestException(S(reply["message"])),
                };
            }

            return reply.TryGetValue("body_hex", out var hex)
                ? RawReply((int)Py.AsLong(reply["status"]), Convert.FromHexString(S(hex)), S(reply["content_type"]))
                : Reply((int)Py.AsLong(reply["status"]), S(reply["body"]), S(reply["content_type"]));
        }

        var tracer = new DecisionRecordingTracer();
        using var patch = OpenaiDecisionsClientModule.Sleep.Use((delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });
        var clock = new DecisionClock();
        using var scope = PortServices.Use(time: clock);
        var client = new OpenAIDecisionsClient(
            config: new LLMConfig(apiKey: cfg.TryGetValue("api_key", out var apiKey) ? S(apiKey) : Key, model: cfg["model"] as string),
            baseUrl: cfg["base_url"] as string,
            duplicateThreshold: Py.AsDouble(cfg["duplicate_threshold"]),
            contradictionThreshold: Py.AsDouble(cfg["contradiction_threshold"]),
            timeoutSeconds: timeoutSeconds,
            maxRetries: Py.AsLong(cfg["max_retries"]),
            httpClient: HttpClientOf(new ScriptedHandler(Respond)));
        client.Tracer = tracer;
        var payload = Dict(("instructions", scenario["instructions"]), ("state", scenario["state"]), ("questions", scenario["questions"]));
        foreach (var omitted in scenario.TryGetValue("omit", out var omit) ? L(omit).Select(S) : [])
        {
            payload.Remove(omitted);
        }

        List<Message> messages = [new Message { Role = "system", Content = "system text" }.Validate()];
        if (!scenario.ContainsKey("no_user"))
        {
            messages.Add(new Message { Role = "user", Content = PyJson.Dumps(payload) }.Validate());
        }

        var run = Asyncio.Run(async () =>
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
        var clockStop = DateTime.UtcNow.AddSeconds(10);
        while (byClock && !run.IsCompleted && DateTime.UtcNow < clockStop)
        {
            // Each request that waits for its timer gets it: the clock moves by the timeout of the request.
            if (await waiting.WaitAsync(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken))
            {
                clock.Advance(TimeSpan.FromSeconds(timeoutSeconds));
            }
        }

        var result = await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
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

            // The text of a UnicodeEncodeError is the message of .NET, which PyCompat does not change (R4).
            var textDiffers = outcome.TryGetValue("err", out var recordedError) && S(L(recordedError)[0]) == "UnicodeEncodeError";
            if (textDiffers)
            {
                span.Status![1] = L(expectedSpan["status"])[1] as string;
                span.Exceptions[0] = (span.Exceptions[0].Error, S(L(L(expectedSpan["exceptions"])[0])[1]));
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

    [Fact]
    public async Task The_next_request_waits_for_the_delay_and_a_cancel_ends_the_call_as_in_Python()
    {
        var cases = L(Document["async_cases"]);
        Assert.Equal(["gate", "cancel"], cases.Select(c => S(D(c)["mode"])).ToList());
        foreach (var item in cases)
        {
            var recorded = D(item);
            var mode = S(recorded["mode"]);
            var replies = L(recorded["replies"]).Select(D).ToList();
            var count = 0;
            var secondRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var sleeping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            List<double> delays = [];
            var handler = new ScriptedHandler(_ =>
            {
                var reply = replies[Math.Min(count++, replies.Count - 1)];
                if (count == 2)
                {
                    secondRequest.TrySetResult();
                }

                return Reply((int)Py.AsLong(reply["status"]), S(reply["body"]));
            });
            var tracer = new DecisionRecordingTracer();
            using var patch = OpenaiDecisionsClientModule.Sleep.Use((delay, token) =>
            {
                delays.Add(delay);
                sleeping.TrySetResult();
                return mode == "gate" ? gate.Task : Task.Delay(Timeout.InfiniteTimeSpan, token);
            });
            var client = new OpenAIDecisionsClient(config: new LLMConfig(apiKey: Key), baseUrl: BaseUrl, maxRetries: 3, httpClient: HttpClientOf(handler));
            client.Tracer = tracer;
            using var cancel = new CancellationTokenSource();

            var run = Asyncio.Run(() => Call(client, D(recorded["payload"]), cancel.Token));
            await sleeping.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await Task.WhenAny(secondRequest.Task, Task.Delay(300, TestContext.Current.CancellationToken));

            // While the delay waits, the client sends nothing, and the call is not done.
            var before = D(recorded["before"]);
            Assert.Equal(Py.AsLong(before["requests"]), handler.Requests.Count);
            Assert.Equal(L(before["delays"]).Select(Py.AsDouble), delays);
            Assert.Equal(Py.AsBool(before["done"]), run.IsCompleted);

            var outcome = D(recorded["outcome"]);
            if (mode == "gate")
            {
                gate.SetResult();
                var result = await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                Assert.Equal(PyJson.Dumps(outcome["ok"]), PyJson.Dumps(result));
            }
            else
            {
                await cancel.CancelAsync();
                var error = await Record.ExceptionAsync(() => run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
                Assert.NotNull(error);
                Assert.True(PythonName(error) == S(L(outcome["err"])[0]), $"{mode}: expected {PyJson.Dumps(outcome["err"])}, got {error.GetType().Name}: {error.Message}");
            }

            Assert.Equal(Py.AsLong(recorded["requests"]), handler.Requests.Count);
            Assert.Equal(L(recorded["delays"]).Select(Py.AsDouble), delays);
            var usage = client.TokenTracker.GetUsage().Select(kv => (object?)new List<object?> { kv.Key, kv.Value.CallCount, kv.Value.TotalInputTokens, kv.Value.TotalOutputTokens }).ToList();
            Assert.Equal(PyJson.Dumps(recorded["by_prompt"]), PyJson.Dumps(usage));
            var span = Assert.Single(tracer.Spans);
            var expectedSpan = Dicts(recorded["spans"]).Single();
            Assert.Equal(PyJson.Dumps(expectedSpan["attributes"], sortKeys: true), PyJson.Dumps(span.Attributes, sortKeys: true));
            Assert.Null(span.Status);
            Assert.Empty(span.Exceptions);
            client.HttpClient.Dispose();
        }
    }

    [Fact]
    public void The_default_client_takes_each_timeout_that_httpx_takes()
    {
        var cases = L(Document["default_client"]);
        Assert.Equal(8, cases.Count);
        foreach (var item in cases)
        {
            var recorded = D(item);
            var expected = D(D(recorded["r"])["ok"]);

            var client = new OpenAIDecisionsClient(config: new LLMConfig(apiKey: Key), timeoutSeconds: Py.AsDouble(recorded["timeout_seconds"]));

            Assert.True(Py.AsDouble(expected["timeout_seconds"]) == client.TimeoutSeconds, PyJson.Dumps(recorded));
            Assert.Equal(Timeout.InfiniteTimeSpan, client.HttpClient.Timeout);
            client.HttpClient.Dispose();
        }
    }

    [Fact]
    public async Task The_default_client_follows_no_redirect()
    {
        var recorded = D(Document["redirect"]);
        using var server = new RedirectServer();
        using var patch = OpenaiDecisionsClientModule.Sleep.Use((_, _) => Task.CompletedTask);
        var client = new OpenAIDecisionsClient(config: new LLMConfig(apiKey: Key), baseUrl: $"http://127.0.0.1:{server.Port}/v1", maxRetries: 3);
        var payload = Dict(("instructions", ""), ("state", "current state"), ("questions", Dict(("dup0", Dict(("type", "noul"), ("instructions", "is it?"))))));

        var error = await Record.ExceptionAsync(() => Asyncio.Run(() => Call(client, payload)));

        Assert.NotNull(error);
        AssertSameError("redirect", error, L(D(recorded["outcome"])["err"]));
        Assert.Equal(PyJson.Dumps(recorded["requests"]), PyJson.Dumps(server.Requests.Cast<object?>().ToList()));
        client.HttpClient.Dispose();
    }

    [Fact]
    public async Task PrivateGenerateResponse_raises_what_Python_raises()
    {
        var recorded = D(Document["private_generate"]);
        var client = new OpenAIDecisionsClient(config: new LLMConfig(apiKey: Key), httpClient: new HttpClient());

        var error = await Record.ExceptionAsync(async () => await client.PrivateGenerateResponseAsync(
            [new Message { Role = "user", Content = "x" }.Validate()],
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.NotNull(error);
        AssertSameError("_generate_response", error, L(recorded["err"]));
        client.HttpClient.Dispose();
    }
}
