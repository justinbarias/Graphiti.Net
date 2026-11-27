// Tests of the token rule of R8 for the four model adapters (port/RULES.md, R25). They have no Python original: the
// adapters replace the provider clients of upstream (decision D3). Each host service is a scripted fake at the
// network boundary (ModelAdapterFakes.cs).
namespace Graphiti.Port.Evaluation;

using System.Net;
using Graphiti;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class ModelAdapterCancellationTests
{
    private static readonly IReadOnlyList<TokenLogProbability> True = [new("True", Math.Log(0.9))];

    private static List<Message> Prompt() =>
    [
        new Message { Role = "system", Content = "s" },
        new Message { Role = "user", Content = "u" },
    ];

    private static ScriptedChatClient Chat() => new((_, _) => ScriptedChatClient.Reply("""{"a": 1}"""));

    // A call that the caller cancels inside the host call. The call ends with the result or with the cancel, and the
    // test reads only what the host saw.
    private static async Task RunAsync(Func<CancellationToken, Task> call, CancellationToken token)
    {
        try
        {
            await Asyncio.Run(() => call(token));
        }
        catch (OperationCanceledException)
        {
        }
    }

    [Fact(Timeout = 10000)]
    public async Task The_chat_adapter_gives_the_host_call_a_token_that_the_callers_cancel_reaches()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var chat = Chat();
        chat.DuringCall = source.Cancel;
        var client = new ChatClientLlmClient(chat, new LLMConfig());

        await RunAsync(t => client.PrivateGenerateResponseAsync(Prompt(), null, 100, ModelSize.Medium, null, t), source.Token);

        Assert.Equal([true], chat.CanceledDuringCall);
    }

    [Fact(Timeout = 10000)]
    public async Task The_embedding_adapter_gives_the_host_call_a_token_that_the_callers_cancel_reaches()
    {
        foreach (var batch in new[] { false, true })
        {
            using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            var generator = new ScriptedEmbeddingGenerator<float>(text => [text.Length, 2]);
            generator.DuringCall = source.Cancel;
            var embedder = new EmbeddingGeneratorEmbedder(generator, new EmbedderConfig { EmbeddingDim = 2 });

            await RunAsync(
                t => batch ? embedder.CreateBatchAsync(["a", "bb"], t) : embedder.CreateAsync(["a"], t), source.Token);

            Assert.Equal([true], generator.CanceledDuringCall);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task The_log_probability_seam_gives_the_host_call_a_token_that_the_callers_cancel_reaches()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var chat = new ScriptedChatClient((_, _) => ScriptedChatClient.Reply("True"));
        chat.DuringCall = source.Cancel;
        var reranker = new OpenAIRerankerClient(
            new LLMConfig(model: "rr"), new ChatClientLogProbabilityChat(chat, new ScriptedLogProbabilities(_ => True)));

        await RunAsync(t => reranker.RankAsync("query", ["alpha"], t), source.Token);

        Assert.Equal([true], chat.CanceledDuringCall);
    }

    [Fact(Timeout = 10000)]
    public async Task The_bridge_gives_the_host_call_a_token_that_the_callers_cancel_reaches()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var host = new ScriptedCrossEncoder([("a", 0.5)]);
        host.DuringCall = source.Cancel;
        CrossEncoderClient bridge = new CrossEncoderBridge(host);

        await RunAsync(t => bridge.RankAsync("q", ["a"], t), source.Token);

        Assert.Equal([true], host.CanceledDuringCall);
    }

    [Fact(Timeout = 10000)]
    public async Task Each_adapter_throws_OperationCanceledException_for_a_canceled_token()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var canceled = source.Token;
        var client = new ChatClientLlmClient(Chat(), new LLMConfig());
        var embedder = new EmbeddingGeneratorEmbedder(
            new ScriptedEmbeddingGenerator<float>(text => [text.Length, 2]), new EmbedderConfig { EmbeddingDim = 2 });
        var reranker = new OpenAIRerankerClient(
            new LLMConfig(model: "rr"),
            new ChatClientLogProbabilityChat(
                new ScriptedChatClient((_, _) => ScriptedChatClient.Reply("True")), new ScriptedLogProbabilities(_ => True)));
        CrossEncoderClient bridge = new CrossEncoderBridge(new ScriptedCrossEncoder([("a", 0.5)]));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Asyncio.Run(
            () => client.PrivateGenerateResponseAsync(Prompt(), null, 100, ModelSize.Medium, null, canceled)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Asyncio.Run(() => embedder.CreateAsync(["a"], canceled)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Asyncio.Run(() => embedder.CreateBatchAsync(["a"], canceled)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Asyncio.Run(() => reranker.RankAsync("query", ["alpha"], canceled)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Asyncio.Run(() => bridge.RankAsync("q", ["a"], canceled)));
    }

    // ---- the classifier of a model failure: the host classifier first, then the default (decision D31) ----

    private sealed class ProviderException() : Exception("busy");

    [Fact]
    public void A_host_classifier_that_says_None_leaves_the_decision_to_the_default_classifier()
    {
        var limited = new HttpRequestException("429", null, HttpStatusCode.TooManyRequests);

        using (PortServices.Use(modelFailureClassifier: _ => ModelCallFailure.None))
        {
            Assert.Equal(ModelCallFailure.RateLimit, PortServices.ClassifyModelFailure(limited));
        }
    }

    [Fact(Timeout = 10000)]
    public async Task The_reranker_and_the_chat_adapter_agree_on_a_rate_limit_for_each_place_of_the_host_classifier()
    {
        var limited = new HttpRequestException("429", null, HttpStatusCode.TooManyRequests);
        var token = TestContext.Current.CancellationToken;

        Task Rerank(Exception thrown) => Asyncio.Run(() => new OpenAIRerankerClient(
                new LLMConfig(model: "rr"),
                new ChatClientLogProbabilityChat(new ScriptedChatClient((_, _) => throw thrown), new ScriptedLogProbabilities(_ => True)))
            .RankAsync("query", ["alpha"], token));
        Task Complete(Exception thrown) => Asyncio.Run(() => new ChatClientLlmClient(
                new ScriptedChatClient((_, _) => throw thrown), new LLMConfig())
            .PrivateGenerateResponseAsync(Prompt(), null, 100, ModelSize.Medium, null, token));

        // A host classifier that says None: the default classifier names status 429 a rate limit.
        using (PortServices.Use(modelFailureClassifier: _ => ModelCallFailure.None))
        {
            await Assert.ThrowsAsync<RateLimitException>(() => Rerank(limited));
            await Assert.ThrowsAsync<RateLimitException>(() => Complete(limited));
        }

        // A host classifier of the scope names its own exception a rate limit, for both paths.
        using (PortServices.Use(
                   modelFailureClassifier: e => e is ProviderException ? ModelCallFailure.RateLimit : ModelCallFailure.None))
        {
            await Assert.ThrowsAsync<RateLimitException>(() => Rerank(new ProviderException()));
            await Assert.ThrowsAsync<RateLimitException>(() => Complete(new ProviderException()));
        }
    }
}
