// Port of tests/llm_client/test_openai_decisions_client.py (port/RULES.md, R25). Each test keeps its Python name.
//
// Two tests of the shared pytest files wait for another unit, and are not ported yet:
// - test_jev_client_threshold_defaults_match_decisions_client (test_openai_decisions_client.py) builds a JevClient,
//   so it waits for the unit graphiti_core.llm_client.jev_client.
// - test_jev_prompt_overrides_build_n6_and_e2_questions (test_jev_client.py) reads the constants through the module
//   jev_client, so it waits for jev_client.
// values/decision_prompt_fixture.json holds the texts of tests/llm_client/decision_prompt_fixture.json of the pinned
// upstream (port/tools/record_decision_prompt_fixture.py), for
// test_shared_prompt_renders_match_zep_fixture_for_both_clients.
// The two prompt builders themselves are compared with Python in LlmClientDecisionValuesTests, with a default builder
// that the test gives.
//
// The fake is an HttpMessageHandler (LlmClientDecisionSupport.cs), where upstream has httpx.MockTransport. The
// monkeypatch of `_sleep` is the PyPatch `OpenaiDecisionsClientModule.Sleep` (R17).
namespace Graphiti.Port.Evaluation;

using System.Net;
using System.Text.RegularExpressions;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.DecisionSupport;

public class LlmClientDecisionClientTests
{
    [Fact]
    public async Task test_request_mapping_and_response_mapping()
    {
        var handler = new ScriptedHandler(_ => JsonReply(
            200,
            Dict(
                ("model", "gpt-6-luna"),
                ("answers", AnswerSet()),
                ("usage", Dict(("input_tokens", 31L), ("output_tokens", 7L), ("total_tokens", 38L))))));
        var client = Client(handler);

        var result = await Asyncio.Run(() => Call(client));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://decisions.test/v1/decisions", request.Url);
        Assert.Equal("Bearer " + Key, request.Authorization);
        AssertEqualValues(
            Dict(
                ("model", "gpt-6-luna"),
                ("input", "caller instructions\n\ncurrent state"),
                ("questions", new List<object?>
                {
                    Dict(("name", "dup0"), ("type", "predicate"), ("instructions", "Is this a duplicate?")),
                    Dict(("name", "con0"), ("type", "predicate"), ("instructions", "Is this a contradiction?")),
                    Dict(
                        ("name", "pick"),
                        ("type", "choice"),
                        ("instructions", "Choose one."),
                        ("choices", new List<object?>
                        {
                            Dict(("value", "none"), ("description", "No match")),
                            Dict(("value", "alice"), ("description", "Alice Smith")),
                        })),
                })),
            request.Body);
        AssertEqualValues(Dict(("duplicate_facts", new List<object?>()), ("contradicted_facts", new List<object?>())), result);
        var usage = client.TokenTracker.GetTotalUsage();
        Assert.Equal(31, usage.InputTokens);
        Assert.Equal(7, usage.OutputTokens);
        client.HttpClient.Dispose();
    }

    [Theory]
    [InlineData("caller instructions", "caller instructions\n\ncurrent state")]
    [InlineData("", "current state")]
    public async Task test_input_joins_only_nonempty_instructions_and_state(string instructions, string expectedInput)
    {
        var handler = new ScriptedHandler(_ => JsonReply(200, Dict(("answers", AnswerSet()))));
        var client = Client(handler);

        await Asyncio.Run(() => Call(client, Payload(instructions: instructions)));

        var inputs = handler.Requests.Select(r => r.Body["input"]).ToList();
        Assert.Equal<object?>([expectedInput], inputs);
        client.HttpClient.Dispose();
    }

    [Fact]
    public void test_answer_mapping_supports_choice_probabilities_and_confidence()
    {
        var questions = Questions(Payload());
        var probabilities = OpenAIDecisionsClient.MapAnswers(
            questions,
            new List<object?>
            {
                Dict(("type", "predicate"), ("name", "dup0"), ("probability", 0.75)),
                Dict(("type", "predicate"), ("name", "con0"), ("probability", 0.25)),
                Dict(
                    ("type", "choice"),
                    ("name", "pick"),
                    ("probabilities", new List<object?>
                    {
                        Dict(("value", "none"), ("probability", 0.2)),
                        Dict(("value", "alice"), ("probability", 0.8)),
                    })),
            });
        var confidence = OpenAIDecisionsClient.MapAnswers(
            questions,
            new List<object?>
            {
                Dict(("type", "predicate"), ("name", "dup0"), ("probability", 0.75)),
                Dict(("type", "predicate"), ("name", "con0"), ("probability", 0.25)),
                Dict(("type", "choice"), ("name", "pick"), ("choice", "alice"), ("confidence", 0.8)),
            });

        AssertEqualValues(Dict(("probabilities", Dict(("none", 0.2), ("alice", 0.8)))), probabilities["pick"]);
        AssertEqualValues(Dict(("choice", "alice"), ("probabilities", Dict(("alice", 0.8)))), confidence["pick"]);
    }

    [Theory]
    [InlineData(0.69, 0.79, false)]
    [InlineData(0.70, 0.80, true)]
    public async Task test_default_threshold_boundaries(double duplicate, double contradiction, bool expectedFound)
    {
        var handler = new ScriptedHandler(_ => JsonReply(200, Dict(("answers", AnswerSet(duplicate: duplicate, contradiction: contradiction)))));
        var client = Client(handler);

        Assert.Equal(0.7, client.DuplicateThreshold);
        Assert.Equal(0.8, client.ContradictionThreshold);
        var result = await Asyncio.Run(() => Call(client));

        List<object?> found = expectedFound ? [0L] : [];
        AssertEqualValues(Dict(("duplicate_facts", found), ("contradicted_facts", found)), result);
        client.HttpClient.Dispose();
    }

    public static TheoryData<string, string> InvalidAnswers()
    {
        var set = AnswerSet();
        return new TheoryData<string, string>
        {
            {
                PyJson.Dumps(new List<object?> { Dict(("type", "predicate"), ("name", "unknown"), ("probability", 0.9)), set[1], set[2] }),
                "unknown answer name"
            },
            {
                PyJson.Dumps(new List<object?> { set[0], set[1], Dict(("type", "choice"), ("name", "pick"), ("choice", "other"), ("confidence", 1.0)) }),
                "not allowed"
            },
            { PyJson.Dumps(new List<object?>()), "missing answers" },
            {
                PyJson.Dumps(new List<object?> { Dict(("type", "choice"), ("name", "dup0"), ("choice", "true"), ("confidence", 0.9)), set[1], set[2] }),
                "expected.*predicate"
            },
            {
                PyJson.Dumps(new List<object?> { Dict(("type", "predicate"), ("name", "dup0")), set[1], set[2] }),
                "needs a valid probability"
            },
            {
                PyJson.Dumps(new List<object?> { set[0], set[1], Dict(("type", "choice"), ("name", "pick"), ("choice", "alice")) }),
                "needs probabilities or a choice with confidence"
            },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidAnswers))]
    public async Task test_invalid_answers_raise(string answersJson, string error)
    {
        var handler = new ScriptedHandler(_ => JsonReply(200, Dict(("answers", PyJson.Loads(answersJson)))));
        var client = Client(handler);

        var thrown = await Assert.ThrowsAsync<ArgumentException>(() => Asyncio.Run(() => Call(client)));

        Assert.Matches(new Regex(error), thrown.Message);
        client.HttpClient.Dispose();
    }

    [Fact]
    public async Task test_http_error_is_not_retried()
    {
        var handler = new ScriptedHandler(_ => JsonReply(401, Dict(("error", Dict(("message", "unauthorized"))))));
        var client = Client(handler);

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => Asyncio.Run(() => Call(client)));

        Assert.Contains("HTTP 401", thrown.Message);
        Assert.Equal(HttpStatusCode.Unauthorized, thrown.StatusCode);
        Assert.Single(handler.Requests);
        client.HttpClient.Dispose();
    }

    [Fact]
    public async Task test_retries_timeout_429_and_5xx_with_exponential_backoff()
    {
        var calls = 0;
        List<double> delays = [];

        HttpResponseMessage Handle(DecisionRequest request)
        {
            calls++;
            return calls switch
            {
                1 => throw new TimeoutException("request timed out"),
                2 => Reply(500, string.Empty),
                3 => Reply(429, string.Empty),
                _ => JsonReply(200, Dict(("answers", AnswerSet()))),
            };
        }

        using var patch = OpenaiDecisionsClientModule.Sleep.Use((delay, _) =>
        {
            delays.Add(delay);
            return Task.CompletedTask;
        });
        var client = Client(new ScriptedHandler(Handle));

        await Asyncio.Run(() => Call(client));

        Assert.Equal(4, calls);
        Assert.Equal([1.0, 2.0, 4.0], delays);
        client.HttpClient.Dispose();
    }

    [Theory]
    [InlineData("context_length_exceeded")]
    [InlineData("string_above_max_length")]
    public async Task test_too_long_input_removes_previous_then_halves_current(string errorCode)
    {
        List<string> states = [];
        List<int> questionCounts = [];
        var answersByName = AnswerSet().Select(Py.AsDict).ToDictionary(a => (string)a["name"]!);

        HttpResponseMessage Handle(DecisionRequest request)
        {
            var body = request.Body;
            states.Add((string)body["input"]!);
            var requested = Py.AsList(body["questions"]);
            questionCounts.Add(requested.Count);
            if (states.Count < 4)
            {
                return JsonReply(400, Dict(("error", Dict(("code", errorCode), ("type", "invalid_request_error")))));
            }

            return JsonReply(200, Dict(("answers", requested.Select(q => (object?)answersByName[(string)Py.AsDict(q)["name"]!]).ToList())));
        }

        var payload = Payload(
            "<PREVIOUS MESSAGES>[\"previous\"]</PREVIOUS MESSAGES>\n"
            + "<CURRENT MESSAGE>" + string.Concat(Enumerable.Repeat("current ", 400)) + "</CURRENT MESSAGE>");
        var client = Client(new ScriptedHandler(Handle));

        await Asyncio.Run(() => Call(client, payload));

        Assert.Equal([3, 1, 1, 1, 2], questionCounts);
        Assert.Contains("<PREVIOUS MESSAGES>[]</PREVIOUS MESSAGES>", states[2]);
        Assert.True(states[3].Length < states[2].Length);
        client.HttpClient.Dispose();
    }

    [Fact]
    public async Task test_too_long_single_question_raises_after_state_shrinking()
    {
        var handler = new ScriptedHandler(_ => JsonReply(400, Dict(("error", Dict(("code", "context_length_exceeded"), ("type", "invalid_request_error"))))));
        var payload = Payload(
            "<PREVIOUS MESSAGES>[\"previous\"]</PREVIOUS MESSAGES>\n"
            + "<CURRENT MESSAGE>" + string.Concat(Enumerable.Repeat("current ", 400)) + "</CURRENT MESSAGE>");
        var client = Client(handler);

        await Assert.ThrowsAsync<OpenAIDecisionsInputTooLongException>(() => Asyncio.Run(() => Call(client, payload)));

        client.HttpClient.Dispose();
    }

    [Fact]
    public async Task test_observed_token_limit_error_splits_questions_then_raises()
    {
        var observedProbeBody = Dict(
            ("error", Dict(
                ("message", "Decision input exceeds the token limit."),
                ("type", "invalid_request_error"),
                ("param", null),
                ("code", null))));
        List<int> questionCounts = [];
        List<int> inputLengths = [];

        HttpResponseMessage Handle(DecisionRequest request)
        {
            var body = request.Body;
            questionCounts.Add(Py.AsList(body["questions"]).Count);
            inputLengths.Add((int)Py.Len((string)body["input"]!));
            return JsonReply(400, observedProbeBody);
        }

        var payload = Payload(
            "<PREVIOUS MESSAGES>[\"previous\"]</PREVIOUS MESSAGES>\n"
            + "<CURRENT MESSAGE>" + string.Concat(Enumerable.Repeat("current ", 400)) + "</CURRENT MESSAGE>");
        var client = Client(new ScriptedHandler(Handle));

        await Assert.ThrowsAsync<OpenAIDecisionsInputTooLongException>(() => Asyncio.Run(() => Call(client, payload)));

        Assert.Equal(3, questionCounts[0]);
        Assert.Equal(1, questionCounts[1]);
        Assert.All(questionCounts.Skip(1), count => Assert.Equal(1, count));
        Assert.True(inputLengths.Count > 2);
        Assert.True(inputLengths[^1] < inputLengths[1]);
        client.HttpClient.Dispose();
    }

    [Fact]
    public async Task test_other_invalid_request_error_is_not_retried_or_split()
    {
        var calls = 0;
        List<int> questionCounts = [];

        HttpResponseMessage Handle(DecisionRequest request)
        {
            calls++;
            questionCounts.Add(Py.AsList(request.Body["questions"]).Count);
            return JsonReply(
                400,
                Dict(("error", Dict(
                    ("message", "The request has an invalid parameter."),
                    ("type", "invalid_request_error"),
                    ("param", null),
                    ("code", null)))));
        }

        var client = Client(new ScriptedHandler(Handle));

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => Asyncio.Run(() => Call(client)));

        Assert.Contains("HTTP 400", thrown.Message);
        Assert.Equal(1, calls);
        Assert.Equal([3], questionCounts);
        client.HttpClient.Dispose();
    }

    [Theory]
    [InlineData("https://argument.test/v1/", "https://config.test/v1/", "https://environment.test/v1/", "https://argument.test/v1")]
    [InlineData(null, "https://config.test/v1/", "https://environment.test/v1/", "https://config.test/v1")]
    [InlineData(null, null, "https://environment.test/v1/", "https://environment.test/v1")]
    [InlineData(null, null, null, "https://api.openai.com/v1")]
    public void test_base_url_precedence(string? argumentBaseUrl, string? configBaseUrl, string? environmentBaseUrl, string expectedBaseUrl)
    {
        using var environment = PyEnv.Use(name => name == "OPENAI_BASE_URL" ? environmentBaseUrl : null);

        var client = new OpenAIDecisionsClient(
            config: new LLMConfig(apiKey: Key, baseUrl: configBaseUrl),
            baseUrl: argumentBaseUrl);

        Assert.Equal(expectedBaseUrl, client.BaseUrl);
        client.HttpClient.Dispose();
    }

    [Fact]
    public void test_shared_prompt_renders_match_zep_fixture_for_both_clients()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "decision_prompt_fixture.json");
        var expected = Py.AsDict(PyJson.Loads(File.ReadAllText(path)));
        var nodeContext = new OrderedDictionary<string, object?>
        {
            ["previous_episodes"] = new List<object?> { "Alice joined Acme." },
            ["episode_content"] = "Alice joined Acme as an engineer.",
            ["extracted_nodes"] = new List<object?>
            {
                new OrderedDictionary<string, object?>
                {
                    ["id"] = 0L, ["name"] = "Alice", ["entity_type"] = new List<object?> { "Person" },
                },
                new OrderedDictionary<string, object?>
                {
                    ["id"] = 1L, ["name"] = "Acme", ["entity_type"] = new List<object?> { "Organization" },
                },
            },
            ["existing_nodes"] = new List<object?>
            {
                new OrderedDictionary<string, object?> { ["candidate_id"] = 7L, ["name"] = "Alice Smith" },
            },
        };
        var edgeContext = new OrderedDictionary<string, object?>
        {
            ["existing_edges"] = "[{\"idx\": 0, \"fact\": \"Alice works at Acme\"}]",
            ["edge_invalidation_candidates"] = "[{\"idx\": 1, \"fact\": \"Alice was a manager at Acme\"}]",
            ["new_edge"] = "Alice works at Acme as an engineer",
        };

        foreach (var overrides in new[]
                 {
                     DecisionPrompts.JevPromptOverrides(),
                     OpenaiDecisionsClientModule.OpenaiDecisionsPromptOverrides(),
                 })
        {
            var node = overrides.DedupeNodes!.Nodes!(nodeContext);
            var edge = overrides.DedupeEdges!.ResolveEdge!(edgeContext);
            Assert.Equal(Py.AsString(expected["node"]), node.User.Content);
            Assert.Equal(Py.AsString(expected["edge"]), edge.User.Content);
        }
    }
}
