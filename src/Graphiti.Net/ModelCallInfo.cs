namespace Graphiti;

/// <summary>
/// The facts of one call that Graphiti makes to a host model service. <see cref="GraphitiModelCall.Current"/> holds
/// the value while the call runs, so that host middleware, for example a <c>DelegatingChatClient</c>, can read it.
/// </summary>
/// <param name="Operation">The kind of call: <c>chat</c>, <c>embed</c>, <c>embed_batch</c>, or <c>rerank</c>.</param>
/// <param name="PromptName">The name of the prompt of a chat call, for example <c>extract_nodes.extract_message</c>, or null.</param>
/// <param name="ResponseModel">The upstream class name of the response model of a chat call, or null.</param>
/// <param name="ModelSize">The model size of a chat call, <c>small</c> or <c>medium</c>, or null.</param>
/// <param name="Model">The model that a route names for the call. It is null with no route.</param>
/// <param name="ResponseSchema">The JSON schema text of the response model of a chat call, or null.</param>
public sealed record ModelCallInfo(
    string Operation, string? PromptName, string? ResponseModel, string? ModelSize, string? Model, string? ResponseSchema);
