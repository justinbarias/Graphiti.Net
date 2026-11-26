namespace Graphiti;

/// <summary>One token of a chat reply and its log probability.</summary>
/// <param name="Token">The text of the token.</param>
/// <param name="LogProbability">The natural logarithm of the probability of the token.</param>
public readonly record struct TokenLogProbability(string Token, double LogProbability);
