namespace Graphiti;

using Graphiti.Internal;
using CorePrompts = Graphiti.Core.Prompts;

/// <summary>
/// A prompt override: builds the system text and the user text of one prompt from its context. It is the public form of
/// an upstream override callable that returns <c>ChatPrompt</c>. It runs in line on the Graphiti scheduler: it must not
/// block, do I/O, or call <c>IGraphiti</c>.
/// </summary>
/// <param name="context">The name and the context of the prompt.</param>
/// <returns>The prompt.</returns>
public delegate ChatPrompt PromptBuilder(PromptContext context);

/// <summary>
/// A prompt of a system text and a user text. The public form of upstream's <c>ChatPrompt</c>. Graphiti adds the Unicode
/// note to the system text after the builder returns, as upstream does.
/// </summary>
/// <param name="System">The system text.</param>
/// <param name="User">The user text.</param>
public sealed record ChatPrompt(string System, string User);

/// <summary>The input of a <see cref="PromptBuilder"/>: the name of the prompt and the upstream context dict.</summary>
public sealed class PromptContext
{
    private readonly OrderedDictionary<string, object?> context;

    internal PromptContext(string name, OrderedDictionary<string, object?> context)
    {
        Name = name;
        this.context = context;
        Values = new PromptContextValues(context);
    }

    /// <summary>Gets the prompt name, for example <c>extract_nodes.extract_message</c>.</summary>
    public string Name { get; }

    /// <summary>
    /// Gets a read-only view of the upstream context dict. A value has the kinds of the public records: null,
    /// <see cref="bool"/>, <see cref="long"/>, <see cref="double"/>, <see cref="string"/>, <see cref="DateTimeOffset"/>,
    /// <see cref="DateOnly"/>, a read-only list, or a read-only dictionary. A read of a value of another kind throws
    /// <see cref="InvalidCastException"/>. The keys are upstream's and can change when the pinned upstream version
    /// changes.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Values { get; }

    /// <summary>Builds the built-in prompt for the same context, so that a builder can add text to it.</summary>
    /// <returns>The built-in prompt, without the Unicode note.</returns>
    public ChatPrompt BuildDefault()
    {
        var prompt = CorePrompts.Lib.GetPromptBuilder(CorePrompts.Lib.DefaultChatPromptLibrary, Name)(context);
        return new ChatPrompt(prompt.System.Content, prompt.User.Content);
    }
}

/// <summary>
/// The model of a route: a prompt name or a group name of <c>Llm.Routes</c> runs on this model. The public form of
/// upstream's <c>LLMModel</c>.
/// </summary>
public sealed class ModelRoute
{
    /// <summary>Gets the model id. It must not be empty.</summary>
    public required string ModelId { get; init; }

    /// <summary>
    /// Gets the service key of a keyed <c>IChatClient</c> of the container, or null. Null uses the chat client of
    /// Graphiti.
    /// </summary>
    public object? ChatClientKey { get; init; }

    /// <summary>Gets the maximum number of output tokens of the model, or null for the call's own limit. It must be more than zero.</summary>
    public int? MaxTokens { get; init; }

    /// <summary>
    /// Gets the prompt overrides of this model. A key is one of the 25 upstream prompt names. They win over
    /// <c>Prompts.Overrides</c>.
    /// </summary>
    public IDictionary<string, PromptBuilder> PromptOverrides { get; } = new Dictionary<string, PromptBuilder>(StringComparer.Ordinal);
}
