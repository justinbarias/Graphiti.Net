// Tests of the chat adapter ChatClientLlmClient by the request table, the reply table, and the retry text of
// "The chat adapter" in docs/design-docs/public-surface-design.md. The expected values come from the design text.
// The chat client is a scripted fake at the network boundary (ModelAdapterFakes.cs).
namespace Graphiti.Port.Evaluation;

using System.Net;
using System.Text.Json;
using Graphiti;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.AI;
using static Graphiti.Port.Evaluation.LlmClientClientSupport;

public class ModelAdapterChatTests
{
    private static readonly IResponseModel Thing = new BuiltinResponseModel("Thing", Message.ModelJsonSchema);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static List<Message> Prompt() =>
    [
        new Message { Role = "system", Content = "s" },
        new Message { Role = "user", Content = "u" },
    ];

    private static ScriptedChatClient Replying(string text) => new((_, _) => ScriptedChatClient.Reply(text));

    private static Task<OrderedDictionary<string, object?>> Direct(
        ChatClientLlmClient client,
        List<Message>? messages = null,
        IResponseModel? responseModel = null,
        long maxTokens = 100,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null) =>
        Asyncio.Run(() => client.PrivateGenerateResponseAsync(messages ?? Prompt(), responseModel, maxTokens, modelSize, model, Token));

    // ---- request table ----

    [Fact]
    public async Task Messages_map_to_one_chat_message_each_in_order_with_the_role_of_the_same_name()
    {
        var chat = Replying("{}");
        var client = new ChatClientLlmClient(chat, new LLMConfig());
        List<Message> messages =
        [
            new Message { Role = "system", Content = "a" },
            new Message { Role = "user", Content = "b" },
            new Message { Role = "assistant", Content = "c" },
            new Message { Role = "developer", Content = "d" },
        ];

        await Direct(client, messages);

        Assert.Equal([("system", "a"), ("user", "b"), ("assistant", "c"), ("developer", "d")], chat.Calls[0].Messages);
    }

    [Fact]
    public void A_known_role_is_the_ChatRole_of_the_same_name_and_another_role_is_a_new_ChatRole()
    {
        List<Message> messages =
        [
            new Message { Role = "system", Content = "a" },
            new Message { Role = "user", Content = "b" },
            new Message { Role = "assistant", Content = "c" },
            new Message { Role = "tool", Content = "d" },
        ];

        var roles = ChatClientLlmClient.ToChatMessages(messages).Select(m => m.Role).ToList();

        Assert.Equal([ChatRole.System, ChatRole.User, ChatRole.Assistant, new ChatRole("tool")], roles);
    }

    [Fact]
    public async Task Max_tokens_is_MaxOutputTokens()
    {
        var chat = Replying("{}");
        await Direct(new ChatClientLlmClient(chat, new LLMConfig()), maxTokens: 1234);
        Assert.Equal(1234, chat.Calls[0].Options?.MaxOutputTokens);
    }

    [Theory]
    [InlineData("route", "small", "big", "small", "route")]
    [InlineData("route", "medium", "big", "small", "route")]
    [InlineData(null, "small", "big", "small", "small")]
    [InlineData(null, "small", "big", null, "big")]
    [InlineData(null, "medium", "big", "small", "big")]
    [InlineData(null, "medium", null, "small", null)]
    [InlineData(null, "small", null, null, null)]
    public async Task ModelId_is_the_model_argument_then_the_sized_model(
        string? model, string size, string? configModel, string? configSmallModel, string? expected)
    {
        var chat = Replying("{}");
        var client = new ChatClientLlmClient(chat, new LLMConfig(model: configModel, smallModel: configSmallModel));

        await Direct(client, modelSize: size == "small" ? ModelSize.Small : ModelSize.Medium, model: model);

        Assert.Equal(expected, chat.Calls[0].Options?.ModelId);
    }

    [Fact]
    public async Task Temperature_is_sent_only_if_the_option_has_a_value()
    {
        var with = Replying("{}");
        var without = Replying("{}");

        await Direct(new ChatClientLlmClient(with, new LLMConfig(), temperature: 0.25));
        await Direct(new ChatClientLlmClient(without, new LLMConfig(temperature: 0.7)));

        Assert.Equal(0.25f, with.Calls[0].Options?.Temperature);
        Assert.Null(without.Calls[0].Options?.Temperature);
    }

    [Fact]
    public async Task Each_call_asks_for_a_JSON_reply_with_no_schema_in_the_format()
    {
        var chat = Replying("{}");
        var client = new ChatClientLlmClient(chat, new LLMConfig());

        await Direct(client);
        await Direct(client, responseModel: Thing);

        Assert.All(chat.Calls, c => Assert.True(c.Options?.ResponseFormat is ChatResponseFormatJson { Schema: null }));
    }

    // ---- reply table ----

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task An_empty_reply_throws_EmptyResponseException(string? text)
    {
        var client = new ChatClientLlmClient(new ScriptedChatClient((_, _) => ScriptedChatClient.Reply(text)), new LLMConfig());
        await Assert.ThrowsAsync<EmptyResponseException>(() => Direct(client));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{\"a\": 1}")]
    public async Task A_content_filter_finish_throws_RefusalException(string? text)
    {
        var client = new ChatClientLlmClient(
            new ScriptedChatClient((_, _) => ScriptedChatClient.Reply(text, ChatFinishReason.ContentFilter)), new LLMConfig());
        await Assert.ThrowsAsync<RefusalException>(() => Direct(client));
    }

    [Fact]
    public async Task A_JSON_object_reply_is_the_result()
    {
        var client = new ChatClientLlmClient(Replying("{\"name\": \"Alice\", \"n\": [1, 2.5, null]}"), new LLMConfig());

        var result = await Direct(client);

        Assert.Equal("{\"name\": \"Alice\", \"n\": [1, 2.5, null]}", PyJson.Dumps(result));
    }

    [Fact]
    public async Task A_reply_that_is_not_JSON_throws_JsonException()
    {
        var client = new ChatClientLlmClient(Replying("not json"), new LLMConfig());
        await Assert.ThrowsAsync<JsonException>(() => Direct(client));
    }

    [Theory]
    [InlineData("[1, 2]", "array")]
    [InlineData("\"text\"", "string")]
    [InlineData("3", "number")]
    [InlineData("true", "boolean")]
    [InlineData("null", "null")]
    public async Task JSON_that_is_not_an_object_throws_JsonException_that_names_the_kind(string text, string kind)
    {
        var client = new ChatClientLlmClient(Replying(text), new LLMConfig());
        var error = await Assert.ThrowsAsync<JsonException>(() => Direct(client));
        Assert.Contains(kind, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Usage_is_recorded_once_for_each_reply_with_the_prompt_name()
    {
        var chat = new ScriptedChatClient((_, _) => ScriptedChatClient.Reply(
            "{}", usage: new UsageDetails { InputTokenCount = 10, OutputTokenCount = 4 }));
        var client = new ChatClientLlmClient(chat, new LLMConfig());

        await Asyncio.Run(() => client.GenerateResponseAsync(Prompt(), promptName: "p.one", cancellationToken: Token));
        await Asyncio.Run(() => client.GenerateResponseAsync(Prompt(), promptName: "p.one", cancellationToken: Token));

        var usage = client.TokenTracker.GetUsage()["p.one"];
        Assert.Equal((2L, 20L, 8L), (usage.CallCount, usage.TotalInputTokens, usage.TotalOutputTokens));
    }

    [Fact]
    public async Task A_reply_with_no_usage_records_zeros()
    {
        var client = new ChatClientLlmClient(Replying("{}"), new LLMConfig());

        await Asyncio.Run(() => client.GenerateResponseAsync(Prompt(), promptName: "p.two", cancellationToken: Token));

        var usage = client.TokenTracker.GetUsage()["p.two"];
        Assert.Equal((1L, 0L, 0L), (usage.CallCount, usage.TotalInputTokens, usage.TotalOutputTokens));
    }

    private sealed class ProviderException(string message) : Exception(message);

    [Fact]
    public async Task A_rate_limit_of_the_host_classifier_throws_RateLimitException_with_the_provider_exception_inside()
    {
        var provider = new ProviderException("busy");
        var client = new ChatClientLlmClient(
            new ScriptedChatClient((_, _) => throw provider),
            new LLMConfig(),
            classifyFailure: e => e is ProviderException ? ModelCallFailure.RateLimit : ModelCallFailure.None);

        var error = await Assert.ThrowsAsync<RateLimitException>(() => Direct(client));

        Assert.Same(provider, error.InnerException);
    }

    [Fact]
    public async Task A_server_error_of_the_host_classifier_throws_HttpRequestException_500_with_the_provider_exception_inside()
    {
        var provider = new ProviderException("down");
        var client = new ChatClientLlmClient(
            new ScriptedChatClient((_, _) => throw provider),
            new LLMConfig(),
            classifyFailure: _ => ModelCallFailure.ServerError);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => Direct(client));

        Assert.Equal(HttpStatusCode.InternalServerError, error.StatusCode);
        Assert.Same(provider, error.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, typeof(RateLimitException))]
    [InlineData(HttpStatusCode.ServiceUnavailable, typeof(HttpRequestException))]
    public async Task The_default_classifier_applies_when_the_host_classifier_says_none(HttpStatusCode status, Type expected)
    {
        var provider = new HttpRequestException("x", null, status);
        var client = new ChatClientLlmClient(
            new ScriptedChatClient((_, _) => throw provider), new LLMConfig(), classifyFailure: _ => ModelCallFailure.None);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => Direct(client));

        Assert.IsType(expected, error);
        Assert.Same(provider, error.InnerException);
        if (error is HttpRequestException wrapped)
        {
            Assert.Equal(HttpStatusCode.InternalServerError, wrapped.StatusCode);
        }
    }

    [Fact]
    public async Task Another_exception_goes_to_the_caller_as_it_is()
    {
        var provider = new ProviderException("bad request");
        var client = new ChatClientLlmClient(new ScriptedChatClient((_, _) => throw provider), new LLMConfig());

        var error = await Assert.ThrowsAsync<ProviderException>(() => Direct(client));

        Assert.Same(provider, error);
    }

    [Fact]
    public async Task An_exception_of_the_classifier_goes_to_the_caller_as_it_is()
    {
        var thrown = new InvalidCastException("classifier");
        var client = new ChatClientLlmClient(
            new ScriptedChatClient((_, _) => throw new ProviderException("x")), new LLMConfig(), classifyFailure: _ => throw thrown);

        var error = await Assert.ThrowsAsync<InvalidCastException>(() => Direct(client));

        Assert.Same(thrown, error);
    }

    // ---- retry ----

    private static async Task<(Exception? Error, int Calls, int Waits)> RunWithRetry(
        Func<int, ChatResponse> script, Func<Exception, ModelCallFailure>? classify = null)
    {
        var clock = new InstantTimeProvider();
        using var scope = PortServices.Use(time: clock, random: new PyRandom(5));
        var chat = new ScriptedChatClient((i, _) => script(i));
        var client = new ChatClientLlmClient(chat, new LLMConfig(), classifyFailure: classify);
        Exception? error = null;
        try
        {
            await Asyncio.Run(() => client.GenerateResponseAsync(Prompt(), promptName: "p", cancellationToken: Token));
        }
        catch (Exception e)
        {
            error = e;
        }

        return (error, chat.Calls.Count, clock.Delays.Count);
    }

    public static TheoryData<string, Type> RetriedFailures => new()
    {
        { "rate_limit", typeof(RateLimitException) },
        { "empty", typeof(EmptyResponseException) },
        { "not_json", typeof(JsonException) },
        { "not_object", typeof(JsonException) },
        { "server_error", typeof(HttpRequestException) },
    };

    private static ChatResponse Failing(string kind) => kind switch
    {
        "rate_limit" => throw new HttpRequestException("429", null, HttpStatusCode.TooManyRequests),
        "server_error" => throw new HttpRequestException("502", null, HttpStatusCode.BadGateway),
        "empty" => ScriptedChatClient.Reply(string.Empty),
        "not_json" => ScriptedChatClient.Reply("{"),
        "not_object" => ScriptedChatClient.Reply("[]"),
        "refusal" => ScriptedChatClient.Reply(null, ChatFinishReason.ContentFilter),
        "provider" => throw new ProviderException("bad request"),
        _ => throw new ArgumentException(kind),
    };

    [Theory]
    [MemberData(nameof(RetriedFailures))]
    public async Task A_retried_failure_runs_4_attempts_and_the_caller_gets_the_last_exception(string kind, Type expected)
    {
        var (error, calls, waits) = await RunWithRetry(_ => Failing(kind));

        Assert.Equal(4, calls);
        Assert.Equal(3, waits);
        Assert.IsType(expected, error);
        if (error is HttpRequestException wrapped)
        {
            Assert.Equal(HttpStatusCode.InternalServerError, wrapped.StatusCode);
            Assert.IsType<HttpRequestException>(wrapped.InnerException);
        }
    }

    [Theory]
    [InlineData("refusal", typeof(RefusalException))]
    [InlineData("provider", typeof(ProviderException))]
    public async Task A_failure_that_is_not_retried_runs_1_attempt(string kind, Type expected)
    {
        var (error, calls, waits) = await RunWithRetry(_ => Failing(kind));

        Assert.Equal(1, calls);
        Assert.Equal(0, waits);
        Assert.IsType(expected, error);
    }

    [Fact]
    public async Task A_reply_after_retried_failures_is_the_result()
    {
        var (error, calls, waits) = await RunWithRetry(i => i < 2 ? Failing("empty") : ScriptedChatClient.Reply("{}"));

        Assert.Null(error);
        Assert.Equal(3, calls);
        Assert.Equal(2, waits);
    }

    [Fact]
    public async Task A_rate_limit_of_the_host_classifier_is_retried()
    {
        var (error, calls, _) = await RunWithRetry(
            _ => throw new ProviderException("busy"),
            e => e is ProviderException ? ModelCallFailure.RateLimit : ModelCallFailure.None);

        Assert.Equal(4, calls);
        Assert.IsType<RateLimitException>(error);
    }

    // ---- provider name and the ambient call value ----

    [Fact]
    public void The_provider_type_is_the_provider_name_of_the_metadata_or_unknown()
    {
        Assert.Equal("acme", new ChatClientLlmClient(new ScriptedChatClient((_, _) => ScriptedChatClient.Reply("{}"), "acme")).GetProviderType());
        Assert.Equal("unknown", new ChatClientLlmClient(Replying("{}")).GetProviderType());
    }

    [Fact]
    public async Task Inside_a_chat_call_Current_holds_the_facts_of_the_call_and_outside_it_is_null()
    {
        var chat = Replying("{}");
        var client = new ChatClientLlmClient(chat, new LLMConfig(model: "big", smallModel: "small"));

        Assert.Null(GraphitiModelCall.Current);
        var after = await Asyncio.Run(async () =>
        {
            await client.GenerateResponseAsync(
                Prompt(), Thing, modelSize: ModelSize.Small, promptName: "extract_nodes.extract_message", model: "routed", cancellationToken: Token);
            return GraphitiModelCall.Current;
        });

        Assert.Null(after);
        Assert.Null(GraphitiModelCall.Current);
        var info = chat.Calls[0].Info;
        Assert.Equal(
            new ModelCallInfo("chat", "extract_nodes.extract_message", "Thing", "small", "routed", PyJson.Dumps(Message.ModelJsonSchema.ToDict())),
            info);
    }

    [Fact]
    public async Task With_no_route_and_no_response_model_Current_holds_nulls()
    {
        var chat = Replying("{}");
        var client = new ChatClientLlmClient(chat, new LLMConfig(model: "big"));

        await Asyncio.Run(() => client.GenerateResponseAsync(Prompt(), cancellationToken: Token));

        Assert.Equal(new ModelCallInfo("chat", null, null, "medium", null, null), chat.Calls[0].Info);
    }

    [Fact]
    public async Task The_classifier_runs_in_line_after_the_host_call_where_Current_is_null()
    {
        (ModelCallInfo? Info, bool OnLoop)? seen = null;
        var client = new ChatClientLlmClient(
            new ScriptedChatClient((_, _) => throw new ProviderException("x")),
            new LLMConfig(),
            classifyFailure: _ =>
            {
                seen = (GraphitiModelCall.Current, Asyncio.IsOnLoop);
                return ModelCallFailure.None;
            });

        await Assert.ThrowsAsync<ProviderException>(() => Direct(client));

        Assert.Equal((null, true), seen);
    }

    [Fact]
    public async Task The_chat_client_runs_on_the_thread_pool()
    {
        var chat = Replying("{}");
        await Direct(new ChatClientLlmClient(chat, new LLMConfig()));
        Assert.False(chat.Calls[0].OnLoop);
    }
}
