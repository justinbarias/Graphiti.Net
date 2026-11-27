// Tests of "The cross-encoder" in docs/design-docs/public-surface-design.md: the seam ChatClientLogProbabilityChat
// under the ported OpenAIRerankerClient, end to end with a scripted chat client and a scripted log probability hook,
// and the bridge that gives a host ICrossEncoder to ported code. The expected values come from the design text.
namespace Graphiti.Port.Evaluation;

using System.Net;
using Graphiti;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.LlmClient;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.AI;

public class ModelAdapterCrossEncoderTests
{
    private static readonly string[] Names = ["alpha", "bravo", "charlie", "delta", "echo", "foxtrot"];

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // The reply text names the passage of the request, and the hook reads the scripted tokens of that passage.
    private static ScriptedChatClient EchoPassage() => new((_, messages) =>
        ScriptedChatClient.Reply(Names.First(n => messages[^1].Text.Contains(n, StringComparison.Ordinal))));

    private static readonly Dictionary<string, IReadOnlyList<TokenLogProbability>?> Tokens = new()
    {
        // Only the first token counts: the second one of "alpha" would give 1 - 0.05.
        ["alpha"] = [new("True", Math.Log(0.9)), new("False", Math.Log(0.05))],
        ["bravo"] = [new(" true", Math.Log(0.6))],
        ["charlie"] = [new("False", Math.Log(0.7))],
        ["delta"] = [new("false positive", Math.Log(0.2))],
        ["echo"] = null,
        ["foxtrot"] = [],
    };

    private static ScriptedLogProbabilities Hook() => new(text => Tokens[text]);

    private static Task<List<(string, double)>> Rank(ScriptedChatClient chat, IChatLogProbabilities? hook, string? model, params string[] passages)
    {
        var reranker = new OpenAIRerankerClient(new LLMConfig(model: model), new ChatClientLogProbabilityChat(chat, hook));
        return Asyncio.Run(() => reranker.RankAsync("query", [.. passages], Token));
    }

    [Fact]
    public async Task The_score_rule_and_the_order_of_the_result_are_upstreams()
    {
        var result = await Rank(EchoPassage(), Hook(), "rr", "alpha", "bravo", "charlie", "delta");

        // `True` and ` true` score exp(logprob). `False` and `false positive` score 1 - exp(logprob). Descending order.
        Assert.Equal(["alpha", "delta", "bravo", "charlie"], result.Select(r => r.Item1));
        Assert.Equal([0.9, 0.8, 0.6, 0.3], result.Select(r => Math.Round(r.Item2, 12)));
    }

    [Theory]
    [InlineData("echo")]
    [InlineData("foxtrot")]
    public async Task A_reply_with_no_log_probabilities_gives_a_passage_with_no_score_and_the_zip_fails(string passage)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Rank(EchoPassage(), Hook(), "rr", "alpha", passage));
        Assert.Equal("zip() argument 2 is shorter than argument 1", error.Message);
    }

    [Fact]
    public async Task The_options_are_one_token_temperature_0_and_the_reranker_model()
    {
        var chat = EchoPassage();
        var hook = Hook();

        await Rank(chat, hook, "rr", "alpha", "bravo");

        Assert.Equal(2, chat.Calls.Count);
        Assert.All(chat.Calls, c =>
        {
            Assert.Equal(1, c.Options?.MaxOutputTokens);
            Assert.Equal(0f, c.Options?.Temperature);
            Assert.Equal("rr", c.Options?.ModelId);
        });
    }

    [Fact]
    public async Task With_no_reranker_model_the_model_id_stays_unset()
    {
        var chat = EchoPassage();
        await Rank(chat, Hook(), null, "alpha");
        Assert.Null(chat.Calls[0].Options?.ModelId);
    }

    [Fact]
    public async Task The_hook_is_asked_for_2_top_tokens_with_the_bias_of_upstream_on_the_thread_pool()
    {
        var chat = EchoPassage();
        var hook = Hook();

        await Rank(chat, hook, "rr", "alpha", "bravo");

        Assert.Equal(2, hook.Requests.Count);
        Assert.Equal(2, hook.Reads);
        Assert.All(hook.Requests, r =>
        {
            Assert.Equal(2, r.TopCount);
            Assert.Equal(new Dictionary<int, int> { [6432] = 1, [7983] = 1 }, r.Bias);
            Assert.False(r.OnLoop);
        });
        Assert.All(chat.Calls, c => Assert.False(c.OnLoop));
    }

    [Fact]
    public async Task With_no_hook_the_seam_throws_InvalidOperationException_before_the_first_model_call()
    {
        var chat = EchoPassage();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Rank(chat, null, "rr", "alpha"));

        Assert.Empty(chat.Calls);
        Assert.Contains("UseChatLogProbabilities", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rate_limit_of_the_default_classifier_becomes_RateLimitException()
    {
        var provider = new HttpRequestException("429", null, HttpStatusCode.TooManyRequests);
        var chat = new ScriptedChatClient((_, _) => throw provider);

        var error = await Assert.ThrowsAsync<RateLimitException>(() => Rank(chat, Hook(), "rr", "alpha"));

        Assert.Same(provider, error.InnerException);
    }

    private sealed class ProviderException() : Exception("busy");

    [Fact]
    public async Task A_rate_limit_of_the_host_classifier_becomes_RateLimitException_and_another_error_stays_as_it_is()
    {
        using var scope = PortServices.Use(
            modelFailureClassifier: e => e is ProviderException ? ModelCallFailure.RateLimit : ModelCallFailure.None);

        await Assert.ThrowsAsync<RateLimitException>(
            () => Rank(new ScriptedChatClient((_, _) => throw new ProviderException()), Hook(), "rr", "alpha"));
        await Assert.ThrowsAsync<IOException>(
            () => Rank(new ScriptedChatClient((_, _) => throw new IOException("x")), Hook(), "rr", "alpha"));
    }

    [Fact]
    public async Task Inside_a_rerank_call_Current_names_the_operation_with_no_model_and_outside_it_is_null()
    {
        var chat = EchoPassage();

        var after = await Asyncio.Run(async () =>
        {
            var reranker = new OpenAIRerankerClient(new LLMConfig(model: "rr"), new ChatClientLogProbabilityChat(chat, Hook()));
            await reranker.RankAsync("query", ["alpha"], Token);
            return GraphitiModelCall.Current;
        });

        Assert.Null(after);
        Assert.Null(GraphitiModelCall.Current);
        Assert.Equal(new ModelCallInfo("rerank", null, null, null, null, null), chat.Calls[0].Info);
        Assert.Equal("rr", chat.Calls[0].Options?.ModelId);
    }

    // ---- the bridge of a host ICrossEncoder ----

    [Fact]
    public async Task The_bridge_gives_the_host_result_in_its_order_and_calls_the_host_on_the_thread_pool()
    {
        var host = new ScriptedCrossEncoder([("b", 0.7), ("a", 0.2)]);
        CrossEncoderClient bridge = new CrossEncoderBridge(host);

        var (result, after) = await Asyncio.Run(async () =>
        {
            var ranked = await bridge.RankAsync("q", ["a", "b"], Token);
            return (ranked, GraphitiModelCall.Current);
        });

        Assert.Equal([("b", 0.7), ("a", 0.2)], result);
        Assert.Equal("q", host.Calls[0].Query);
        Assert.Equal(["a", "b"], host.Calls[0].Passages);
        Assert.Equal(new ModelCallInfo("rerank", null, null, null, null, null), host.Calls[0].Info);
        Assert.False(host.Calls[0].OnLoop);
        Assert.Null(after);
        Assert.Null(GraphitiModelCall.Current);
    }
}
