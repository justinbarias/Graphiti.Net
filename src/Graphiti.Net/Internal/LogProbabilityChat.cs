namespace Graphiti.Internal;

/// <summary>One message of a log probability request: <c>{'role': ..., 'content': ...}</c>.</summary>
/// <param name="Role">The role: <c>system</c> or <c>user</c>.</param>
/// <param name="Content">The text of the message.</param>
internal sealed record LogProbabilityChatMessage(string Role, string Content);

/// <summary>One item of <c>top_logprobs</c>: a token and its log probability.</summary>
/// <param name="Token">The text of the token.</param>
/// <param name="Logprob">The natural logarithm of the probability of the token.</param>
internal sealed record LogProbabilityTopLogprob(string Token, double Logprob);

/// <summary>One item of <c>logprobs.content</c>: the most likely tokens at one position of the reply.</summary>
/// <param name="TopLogprobs">The tokens, most likely first.</param>
internal sealed record LogProbabilityContent(List<LogProbabilityTopLogprob> TopLogprobs);

/// <summary><c>choice.logprobs</c>.</summary>
/// <param name="Content">The positions of the reply, or null if the service gave none.</param>
internal sealed record LogProbabilityChoiceLogprobs(List<LogProbabilityContent>? Content);

/// <summary>One item of <c>response.choices</c>.</summary>
/// <param name="Logprobs">The log probabilities, or null if the service gave none.</param>
internal sealed record LogProbabilityChoice(LogProbabilityChoiceLogprobs? Logprobs);

/// <summary>The reply of a log probability request, with the members that the ported reranker reads.</summary>
/// <param name="Choices">The choices of the reply.</param>
internal sealed record LogProbabilityChatResponse(List<LogProbabilityChoice> Choices);

/// <summary>
/// The seam of the ported reranker (decision D30, RULES.md R19): where upstream calls
/// <c>self.client.chat.completions.create(...)</c> of the OpenAI package, the port calls <see cref="CreateAsync"/>
/// with the same seven arguments. A lead adapter implements it on a host chat service, and a test implements it
/// with a fake at the network boundary.
/// </summary>
internal abstract class LogProbabilityChat
{
    /// <summary><c>chat.completions.create(model, messages, temperature, max_tokens, logit_bias, logprobs, top_logprobs)</c>.</summary>
    /// <param name="model">The model name.</param>
    /// <param name="messages">The messages of the request, in order.</param>
    /// <param name="temperature">The temperature.</param>
    /// <param name="maxTokens">The limit of reply tokens.</param>
    /// <param name="logitBias">The bias for each token id, in the order of the Python dict.</param>
    /// <param name="logprobs">True to ask for log probabilities.</param>
    /// <param name="topLogprobs">The count of most likely tokens for each position.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The reply.</returns>
    internal abstract Task<LogProbabilityChatResponse> CreateAsync(
        string model,
        List<LogProbabilityChatMessage> messages,
        double temperature,
        long maxTokens,
        OrderedDictionary<string, long> logitBias,
        bool logprobs,
        long topLogprobs,
        CancellationToken cancellationToken = default);
}
