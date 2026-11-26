namespace Graphiti;

using Microsoft.Extensions.AI;

/// <summary>
/// The hook that asks a chat provider for token log probabilities and reads them from its reply (decision D30).
/// <c>Microsoft.Extensions.AI</c> has no member for log probabilities, so the host supplies the provider code.
/// Graphiti calls it on the thread pool, together with the chat call. It must do no I/O.
/// </summary>
public interface IChatLogProbabilities
{
    /// <summary>Changes <paramref name="options"/> so that the provider returns log probabilities.</summary>
    /// <param name="options">The options of the chat call.</param>
    /// <param name="topLogProbabilityCount">The count of most likely tokens for each position of the reply.</param>
    /// <param name="logitBias">The bias for each token id. The ids belong to one OpenAI tokenizer: a hook for another provider ignores them or maps them.</param>
    void Request(ChatOptions options, int topLogProbabilityCount, IReadOnlyDictionary<int, int> logitBias);

    /// <summary>Reads the most likely tokens at the first position of the reply.</summary>
    /// <param name="response">The reply of the chat call.</param>
    /// <returns>The tokens, most likely first, or null if the reply holds none.</returns>
    IReadOnlyList<TokenLogProbability>? ReadFirstToken(ChatResponse response);
}
