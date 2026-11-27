// Evaluation test for graphiti_core/cross_encoder/openai_reranker_client.py (port/RULES.md, R19, R23, R25).
// values/cache_reranker.json holds what port/tools/record_cache_reranker.py recorded from Python with a scripted fake
// for `client.chat.completions.create`. The fake here is a subclass of the lead seam LogProbabilityChat: it is the
// fake at the network boundary. It answers the n-th call with the n-th scripted reply and records each call.
// test_default_reranker_model_is_gpt_4_1_mini is the one test of tests/llm_client/test_openai_client.py whose code
// under test is this module. The other tests of that file wait for openai_client.py.
namespace Graphiti.Port.Evaluation;

using Graphiti;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

public class CrossEncoderRerankerTests
{
    private static readonly OrderedDictionary<string, object?> Document = Load();

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "cache_reranker.json");
        return Py.AsDict(Py.AsDict(PyJson.Loads(File.ReadAllText(path)))["reranker"]);
    }

    private static OrderedDictionary<string, object?> Obj(object? value) => Py.AsDict(value);

    private static List<object?> Items(object? value) => Py.AsList(value);

    [Fact]
    public void test_default_reranker_model_is_gpt_4_1_mini()
    {
        Assert.Equal("gpt-4.1-mini", OpenaiRerankerClientModule.DefaultModel);
        Assert.Equal(OpenaiRerankerClientModule.DefaultModel, (string)Document["default_model"]!);
    }

    [Fact]
    public void A_null_client_is_not_ported_and_throws()
    {
        Assert.Throws<ArgumentException>(() => new OpenAIRerankerClient());
    }

    [Fact]
    public async Task Each_call_the_result_the_error_and_the_log_match_Python()
    {
        var cases = Items(Document["cases"]);
        Assert.True(cases.Count >= 100);
        var names = new HashSet<string>();
        foreach (var item in cases)
        {
            var recorded = Obj(item);
            var input = Obj(recorded["case"]);
            names.Add((string)input["name"]!);
            await Check(recorded, input);
        }

        foreach (var name in new[] { "token", "logprob", "ties", "ties_20", "cancel", "all_equal", "empty_passages", "skip_no_logprobs", "empty_content", "rate_limit", "runtime_error", "random" })
        {
            Assert.Contains(name, names);
        }
    }

    private static async Task Check(OrderedDictionary<string, object?> recorded, OrderedDictionary<string, object?> input)
    {
        var label = PyJson.Dumps(input);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fake = new FakeChat(Items(input["replies"]).Select(Obj).ToList(), cancel);
        LLMConfig? config = input.TryGetValue("model", out var model) ? new LLMConfig(model: (string?)model) : null;
        var reranker = new OpenAIRerankerClient(config, fake);
        var passages = Items(input["passages"]).Select(p => (string)p!).ToList();

        var capture = new CaptureProvider();
        using var factory = new CaptureFactory(capture);
        using var scope = PortServices.Use(loggerFactory: factory);

        List<(string, double)>? result = null;
        Exception? error = null;
        try
        {
            result = await Asyncio.Run(() => reranker.RankAsync((string)input["query"]!, passages, cancel.Token));
        }
        catch (Exception e)
        {
            error = e;
        }

        // The arguments of each call, with the messages.
        var calls = Items(recorded["calls"]).Select(Obj).ToList();
        Assert.True(calls.Count == fake.Calls.Count, label);
        for (var i = 0; i < calls.Count; i++)
        {
            var expected = calls[i];
            var actual = fake.Calls[i];
            var defaultModel = (string)Document["default_model"]!;
            var givenModel = input.TryGetValue("model", out var m) ? (string?)m : null;
            // The port passes null where Python falls back to DEFAULT_MODEL (R19).
            Assert.Equal(string.IsNullOrEmpty(givenModel) ? defaultModel : givenModel, (string)expected["model"]!);
            Assert.Equal(string.IsNullOrEmpty(givenModel) ? null : givenModel, actual.Model);
            Assert.Equal(PyJson.Dumps(expected["messages"]), PyJson.Dumps(actual.Messages.Select(x => (object?)new List<object?> { x.Role, x.Content }).ToList()));
            Assert.Equal(Py.AsDouble(expected["temperature"]), actual.Temperature);
            Assert.Equal(Py.AsLong(expected["max_tokens"]), actual.MaxTokens);
            Assert.Equal(
                PyJson.Dumps(expected["logit_bias"]),
                PyJson.Dumps(actual.LogitBias.Select(p => (object?)new List<object?> { p.Key, p.Value }).ToList()));
            Assert.Equal((bool)expected["logprobs"]!, actual.Logprobs);
            Assert.Equal(Py.AsLong(expected["top_logprobs"]), actual.TopLogprobs);
        }

        var expectedError = recorded["error"] as OrderedDictionary<string, object?>;
        var expectedLog = Items(recorded["log"]).Select(Items).ToList();
        if (expectedError is null)
        {
            Assert.True(error is null, label + ": " + error);
            var expectedResult = Items(recorded["result"]).Select(Items).ToList();
            Assert.NotNull(result);
            Assert.True(expectedResult.Count == result.Count, label);
            for (var i = 0; i < expectedResult.Count; i++)
            {
                Assert.Equal((string)expectedResult[i][0]!, result[i].Item1);
                Assert.True(Py.AsDouble(expectedResult[i][1]) == result[i].Item2, $"{label}: {expectedResult[i][1]} != {result[i].Item2:R}");
            }

            Assert.Empty(capture.Lines);
            return;
        }

        Assert.True(error is not null, label);
        var type = (string)expectedError["type"]!;
        var text = (string)expectedError["text"]!;
        switch (type)
        {
            case "CancelledError":
                // asyncio.CancelledError is not an Exception in Python, so no log line is written.
                Assert.IsAssignableFrom<OperationCanceledException>(error);
                Assert.Empty(capture.Lines);
                return;
            case "RateLimitError":
                var rate = Assert.IsType<RateLimitException>(error);
                Assert.Equal(text, rate.Message);
                Assert.NotNull(rate.InnerException);
                Assert.Equal("RateLimitError", (string)expectedError["cause"]!);
                Assert.Empty(capture.Lines);
                return;
            case "ValueError":
                Assert.Equal(text, Assert.IsType<ArgumentException>(error).Message);
                break;
            case "RuntimeError":
                Assert.Equal(text, Assert.IsType<InvalidOperationException>(error).Message);
                break;
            case "IndexError":
                // The text of a .NET index error has other words than Python's "list index out of range".
                Assert.IsType<ArgumentOutOfRangeException>(error);
                break;
            default:
                throw new InvalidOperationException(type);
        }

        var line = Assert.Single(capture.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Single(expectedLog);
        Assert.Equal("ERROR", (string)expectedLog[0][0]!);
        const string head = "Error in generating LLM response: ";
        Assert.StartsWith(head, (string)expectedLog[0][1]!, StringComparison.Ordinal);
        if (type == "IndexError")
        {
            Assert.StartsWith(head, line.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal((string)expectedLog[0][1]!, line.Message);
        }
    }

    private sealed record Call(
        string? Model,
        List<Message> Messages,
        double Temperature,
        long MaxTokens,
        OrderedDictionary<string, long> LogitBias,
        bool Logprobs,
        long TopLogprobs);

    private sealed class FakeChat(List<OrderedDictionary<string, object?>> replies, CancellationTokenSource cancel) : LogProbabilityChat
    {
        internal List<Call> Calls { get; } = [];

        internal override Task<LogProbabilityChatResponse> CreateAsync(
            string? model,
            List<Message> messages,
            double temperature,
            long maxTokens,
            OrderedDictionary<string, long> logitBias,
            bool logprobs,
            long topLogprobs,
            CancellationToken cancellationToken = default)
        {
            var n = Calls.Count;
            Calls.Add(new Call(model, messages, temperature, maxTokens, logitBias, logprobs, topLogprobs));
            var script = replies[n];
            switch ((string)script["kind"]!)
            {
                case "rate_limit":
                    throw new HttpRequestException("rate limited", null, System.Net.HttpStatusCode.TooManyRequests);
                case "runtime_error":
                    throw new InvalidOperationException((string)script["message"]!);
                case "cancel":
                    // The task is canceled: the token is canceled, and the call ends with the cancellation.
                    cancel.Cancel();
                    throw new OperationCanceledException(cancel.Token);
                case "tokens":
                    var top = Items(script["top"]).Select(Items).Select(t => new LogProbabilityTopLogprob((string)t[0]!, Py.AsDouble(t[1]))).ToList();
                    return Task.FromResult(Response(new LogProbabilityChoiceLogprobs([new LogProbabilityContent(top)])));
                case "no_logprobs":
                    return Task.FromResult(new LogProbabilityChatResponse([new LogProbabilityChoice(null)]));
                case "content_none":
                    return Task.FromResult(Response(new LogProbabilityChoiceLogprobs(null)));
                case "empty_content":
                    return Task.FromResult(Response(new LogProbabilityChoiceLogprobs([])));
                case "empty_top":
                    return Task.FromResult(Response(new LogProbabilityChoiceLogprobs([new LogProbabilityContent([])])));
                case "no_choices":
                    return Task.FromResult(new LogProbabilityChatResponse([]));
                default:
                    throw new InvalidOperationException((string)script["kind"]!);
            }
        }

        private static LogProbabilityChatResponse Response(LogProbabilityChoiceLogprobs logprobs) =>
            new([new LogProbabilityChoice(logprobs)]);
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
