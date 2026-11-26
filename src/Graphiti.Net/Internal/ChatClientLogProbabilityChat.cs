namespace Graphiti.Internal;

using System.Globalization;
using Graphiti.Core.Prompts;
using Microsoft.Extensions.AI;

/// <summary>
/// The seam <see cref="LogProbabilityChat"/> of the ported reranker on a host <see cref="IChatClient"/> and a host
/// <see cref="IChatLogProbabilities"/> hook (decision D30, rule R19).
/// </summary>
internal sealed class ChatClientLogProbabilityChat : LogProbabilityChat
{
    // DESIGN: the design says that the message names "the page of the documents", and no such page exists yet. The
    // message names the page by its subject, and the lead fixes the link when the page exists.

    /// <summary>The text of the error for a rerank call with no hook.</summary>
    internal const string NoHookMessage =
        "The reranker reads token log probabilities, and no IChatLogProbabilities is registered. "
        + "Register one with UseChatLogProbabilities. The page \"Reranking with log probabilities\" of the Graphiti.Net "
        + "documents gives the OpenAI implementation.";

    private readonly IChatClient chatClient;
    private readonly IChatLogProbabilities? logProbabilities;

    /// <summary>Initializes a new instance of the <see cref="ChatClientLogProbabilityChat"/> class.</summary>
    /// <param name="chatClient">The host chat client.</param>
    /// <param name="logProbabilities">The host hook, or null if the host registered none.</param>
    internal ChatClientLogProbabilityChat(IChatClient chatClient, IChatLogProbabilities? logProbabilities)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        this.chatClient = chatClient;
        this.logProbabilities = logProbabilities;
    }

    // `logprobs` has no form in the hook: the ported reranker always passes true, and the hook always asks.

    /// <inheritdoc/>
    internal override async Task<LogProbabilityChatResponse> CreateAsync(
        string? model,
        List<Message> messages,
        double temperature,
        long maxTokens,
        OrderedDictionary<string, long> logitBias,
        bool logprobs,
        long topLogprobs,
        CancellationToken cancellationToken = default)
    {
        if (logProbabilities is null)
        {
            throw new InvalidOperationException(NoHookMessage);
        }

        var hook = logProbabilities;
        var chatMessages = ChatClientLlmClient.ToChatMessages(messages);
        var options = new ChatOptions
        {
            MaxOutputTokens = checked((int)maxTokens),
            Temperature = (float)temperature,
            ModelId = model,
        };
        var bias = new Dictionary<int, int>();
        foreach (var (token, value) in logitBias)
        {
            bias[int.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture)] = checked((int)value);
        }

        var topCount = checked((int)topLogprobs);
        var info = new ModelCallInfo("rerank", null, null, null, model, null);

        // The hook runs on the thread pool with the chat call (decision D33).
        var first = await GraphitiModelCall.CallHostAsync(
            info,
            async token =>
            {
                hook.Request(options, topCount, bias);
                var response = await chatClient.GetResponseAsync(chatMessages, options, token);
                return hook.ReadFirstToken(response);
            },
            cancellationToken);

        // A null or empty read gives a choice with no log probabilities, so the passage gets no score.
        if (first is null || first.Count == 0)
        {
            return new LogProbabilityChatResponse([new LogProbabilityChoice(null)]);
        }

        var top = first.Select(t => new LogProbabilityTopLogprob(t.Token, t.LogProbability)).ToList();
        return new LogProbabilityChatResponse(
            [new LogProbabilityChoice(new LogProbabilityChoiceLogprobs([new LogProbabilityContent(top)]))]);
    }
}
