namespace Graphiti.Internal;

using System.Collections;
using CoreLlm = Graphiti.Core.LlmClient;
using CorePrompts = Graphiti.Core.Prompts;

/// <summary>
/// Prompt overrides and model routing (the public surface design, "Prompt overrides and model routing"). It turns the
/// flat public dictionaries, keyed by prompt name or group name, into the ported forms: the override dict of
/// <c>create_prompt_library</c>, <c>LLMPromptOverrides</c>, and <c>PromptRoutes</c>.
/// </summary>
internal static class PromptRouting
{
    /// <summary>Gets the 8 upstream group names.</summary>
    internal static IEnumerable<string> GroupNames => CorePrompts.Lib.PromptGroups.Keys;

    /// <summary>Gets a value indicating whether <paramref name="name"/> is one of the 25 upstream prompt names.</summary>
    internal static bool IsPromptName(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        return dot > 0
            && CorePrompts.Lib.PromptGroups.TryGetValue(name[..dot], out var methods)
            && methods.Contains(name[(dot + 1)..]);
    }

    /// <summary>Gets a value indicating whether <paramref name="name"/> is one of the 8 upstream group names.</summary>
    internal static bool IsGroupName(string name) => CorePrompts.Lib.PromptGroups.ContainsKey(name);

    /// <summary>A public builder as a ported builder. The host builder runs in line on the scheduler.</summary>
    internal static CorePrompts.ChatPromptFunction ToPorted(string name, PromptBuilder builder) =>
        context => ToPorted(builder(new PromptContext(name, context)));

    /// <summary>A public prompt as the ported <c>ChatPrompt</c>.</summary>
    internal static CorePrompts.ChatPrompt ToPorted(ChatPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(prompt.System, nameof(prompt));
        ArgumentNullException.ThrowIfNull(prompt.User, nameof(prompt));
        return new CorePrompts.ChatPrompt
        {
            System = new CorePrompts.SystemMessage { Content = prompt.System }.Validate(),
            User = new CorePrompts.UserMessage { Content = prompt.User }.Validate(),
        }.Validate();
    }

    /// <summary>The overrides as upstream's <c>PromptOverrides</c> dict of <c>create_prompt_library</c>.</summary>
    internal static OrderedDictionary<string, OrderedDictionary<string, CorePrompts.ChatPromptFunction>> ToLibraryOverrides(
        IEnumerable<KeyValuePair<string, PromptBuilder>> overrides)
    {
        var result = new OrderedDictionary<string, OrderedDictionary<string, CorePrompts.ChatPromptFunction>>(StringComparer.Ordinal);
        foreach (var (name, builder) in overrides)
        {
            var dot = name.IndexOf('.', StringComparison.Ordinal);
            var group = name[..dot];
            if (!result.TryGetValue(group, out var functions))
            {
                functions = new(StringComparer.Ordinal);
                result[group] = functions;
            }

            functions[name[(dot + 1)..]] = ToPorted(name, builder);
        }

        return result;
    }

    /// <summary>The overrides as upstream's <c>LLMPromptOverrides</c>, or null for no override.</summary>
    internal static CoreLlm.LLMPromptOverrides? ToPromptOverrides(IEnumerable<KeyValuePair<string, PromptBuilder>> overrides)
    {
        CoreLlm.LLMPromptOverrides? result = null;
        foreach (var (name, builder) in overrides)
        {
            var function = ToPorted(name, builder);
            var value = result ?? new CoreLlm.LLMPromptOverrides();
            result = name switch
            {
                "extract_nodes.extract_message" => value with { ExtractNodes = (value.ExtractNodes ?? new()) with { ExtractMessage = function } },
                "extract_nodes.extract_json" => value with { ExtractNodes = (value.ExtractNodes ?? new()) with { ExtractJson = function } },
                "extract_nodes.extract_text" => value with { ExtractNodes = (value.ExtractNodes ?? new()) with { ExtractText = function } },
                "extract_nodes.classify_nodes" => value with { ExtractNodes = (value.ExtractNodes ?? new()) with { ClassifyNodes = function } },
                "extract_nodes.extract_attributes" => value with { ExtractNodes = (value.ExtractNodes ?? new()) with { ExtractAttributes = function } },
                "extract_nodes.extract_summary" => value with { ExtractNodes = (value.ExtractNodes ?? new()) with { ExtractSummary = function } },
                "extract_nodes.extract_summaries_batch" => value with { ExtractNodes = (value.ExtractNodes ?? new()) with { ExtractSummariesBatch = function } },
                "extract_nodes.extract_entity_summaries_from_episodes" => value with { ExtractNodes = (value.ExtractNodes ?? new()) with { ExtractEntitySummariesFromEpisodes = function } },
                "dedupe_nodes.node" => value with { DedupeNodes = (value.DedupeNodes ?? new()) with { Node = function } },
                "dedupe_nodes.node_list" => value with { DedupeNodes = (value.DedupeNodes ?? new()) with { NodeList = function } },
                "dedupe_nodes.nodes" => value with { DedupeNodes = (value.DedupeNodes ?? new()) with { Nodes = function } },
                "extract_edges.edge" => value with { ExtractEdges = (value.ExtractEdges ?? new()) with { Edge = function } },
                "extract_edges.extract_attributes" => value with { ExtractEdges = (value.ExtractEdges ?? new()) with { ExtractAttributes = function } },
                "extract_edges.extract_timestamps" => value with { ExtractEdges = (value.ExtractEdges ?? new()) with { ExtractTimestamps = function } },
                "extract_edges.extract_timestamps_batch" => value with { ExtractEdges = (value.ExtractEdges ?? new()) with { ExtractTimestampsBatch = function } },
                "extract_nodes_and_edges.extract_message" => value with { ExtractNodesAndEdges = (value.ExtractNodesAndEdges ?? new()) with { ExtractMessage = function } },
                "dedupe_edges.resolve_edge" => value with { DedupeEdges = (value.DedupeEdges ?? new()) with { ResolveEdge = function } },
                "summarize_nodes.summarize_pair" => value with { SummarizeNodes = (value.SummarizeNodes ?? new()) with { SummarizePair = function } },
                "summarize_nodes.summarize_context" => value with { SummarizeNodes = (value.SummarizeNodes ?? new()) with { SummarizeContext = function } },
                "summarize_nodes.summary_description" => value with { SummarizeNodes = (value.SummarizeNodes ?? new()) with { SummaryDescription = function } },
                "summarize_sagas.summarize_saga" => value with { SummarizeSagas = (value.SummarizeSagas ?? new()) with { SummarizeSaga = function } },
                "eval.query_expansion" => value with { Eval = (value.Eval ?? new()) with { QueryExpansion = function } },
                "eval.qa_prompt" => value with { Eval = (value.Eval ?? new()) with { QaPrompt = function } },
                "eval.eval_prompt" => value with { Eval = (value.Eval ?? new()) with { EvalPrompt = function } },
                "eval.eval_add_episode_results" => value with { Eval = (value.Eval ?? new()) with { EvalAddEpisodeResults = function } },
                _ => throw new ArgumentException($"Unknown prompt name: {name}"),
            };
        }

        return result;
    }

    /// <summary>
    /// The routes as upstream's <c>PromptRoutes</c>. A group with no prompt route gets its model as the field value. A
    /// group with prompt routes gets its nested group class, with the group model as <c>default</c>.
    /// </summary>
    internal static CoreLlm.PromptRoutes ToPromptRoutes(IReadOnlyDictionary<string, CoreLlm.LLMModel> routes)
    {
        var result = new CoreLlm.PromptRoutes();
        foreach (var group in GroupNames)
        {
            routes.TryGetValue(group, out var groupModel);
            var prompts = routes.Where(r => r.Key.StartsWith(group + ".", StringComparison.Ordinal)).ToList();
            if (prompts.Count == 0)
            {
                if (groupModel is not null)
                {
                    result = SetGroup(result, group, groupModel);
                }

                continue;
            }

            object nested = group switch
            {
                "extract_nodes" => new CoreLlm.PromptRoutes.ExtractNodesGroup { Default = groupModel },
                "dedupe_nodes" => new CoreLlm.PromptRoutes.DedupeNodesGroup { Default = groupModel },
                "extract_edges" => new CoreLlm.PromptRoutes.ExtractEdgesGroup { Default = groupModel },
                "extract_nodes_and_edges" => new CoreLlm.PromptRoutes.ExtractNodesAndEdgesGroup { Default = groupModel },
                "dedupe_edges" => new CoreLlm.PromptRoutes.DedupeEdgesGroup { Default = groupModel },
                "summarize_nodes" => new CoreLlm.PromptRoutes.SummarizeNodesGroup { Default = groupModel },
                "summarize_sagas" => new CoreLlm.PromptRoutes.SummarizeSagasGroup { Default = groupModel },
                "eval" => new CoreLlm.PromptRoutes.EvalGroup { Default = groupModel },
                _ => throw new ArgumentException($"Unknown prompt group: {group}"),
            };
            foreach (var (name, model) in prompts)
            {
                nested = SetPrompt(nested, name, model);
            }

            result = SetGroup(result, group, nested);
        }

        return result;
    }

    private static CoreLlm.PromptRoutes SetGroup(CoreLlm.PromptRoutes routes, string group, object value) => group switch
    {
        "extract_nodes" => routes with { ExtractNodes = value },
        "dedupe_nodes" => routes with { DedupeNodes = value },
        "extract_edges" => routes with { ExtractEdges = value },
        "extract_nodes_and_edges" => routes with { ExtractNodesAndEdges = value },
        "dedupe_edges" => routes with { DedupeEdges = value },
        "summarize_nodes" => routes with { SummarizeNodes = value },
        "summarize_sagas" => routes with { SummarizeSagas = value },
        "eval" => routes with { Eval = value },
        _ => throw new ArgumentException($"Unknown prompt group: {group}"),
    };

    private static object SetPrompt(object group, string name, CoreLlm.LLMModel model) => (group, name) switch
    {
        (CoreLlm.PromptRoutes.ExtractNodesGroup g, "extract_nodes.extract_message") => g with { ExtractMessage = model },
        (CoreLlm.PromptRoutes.ExtractNodesGroup g, "extract_nodes.extract_json") => g with { ExtractJson = model },
        (CoreLlm.PromptRoutes.ExtractNodesGroup g, "extract_nodes.extract_text") => g with { ExtractText = model },
        (CoreLlm.PromptRoutes.ExtractNodesGroup g, "extract_nodes.classify_nodes") => g with { ClassifyNodes = model },
        (CoreLlm.PromptRoutes.ExtractNodesGroup g, "extract_nodes.extract_attributes") => g with { ExtractAttributes = model },
        (CoreLlm.PromptRoutes.ExtractNodesGroup g, "extract_nodes.extract_summary") => g with { ExtractSummary = model },
        (CoreLlm.PromptRoutes.ExtractNodesGroup g, "extract_nodes.extract_summaries_batch") => g with { ExtractSummariesBatch = model },
        (CoreLlm.PromptRoutes.ExtractNodesGroup g, "extract_nodes.extract_entity_summaries_from_episodes") => g with { ExtractEntitySummariesFromEpisodes = model },
        (CoreLlm.PromptRoutes.DedupeNodesGroup g, "dedupe_nodes.node") => g with { Node = model },
        (CoreLlm.PromptRoutes.DedupeNodesGroup g, "dedupe_nodes.node_list") => g with { NodeList = model },
        (CoreLlm.PromptRoutes.DedupeNodesGroup g, "dedupe_nodes.nodes") => g with { Nodes = model },
        (CoreLlm.PromptRoutes.ExtractEdgesGroup g, "extract_edges.edge") => g with { Edge = model },
        (CoreLlm.PromptRoutes.ExtractEdgesGroup g, "extract_edges.extract_attributes") => g with { ExtractAttributes = model },
        (CoreLlm.PromptRoutes.ExtractEdgesGroup g, "extract_edges.extract_timestamps") => g with { ExtractTimestamps = model },
        (CoreLlm.PromptRoutes.ExtractEdgesGroup g, "extract_edges.extract_timestamps_batch") => g with { ExtractTimestampsBatch = model },
        (CoreLlm.PromptRoutes.ExtractNodesAndEdgesGroup g, "extract_nodes_and_edges.extract_message") => g with { ExtractMessage = model },
        (CoreLlm.PromptRoutes.DedupeEdgesGroup g, "dedupe_edges.resolve_edge") => g with { ResolveEdge = model },
        (CoreLlm.PromptRoutes.SummarizeNodesGroup g, "summarize_nodes.summarize_pair") => g with { SummarizePair = model },
        (CoreLlm.PromptRoutes.SummarizeNodesGroup g, "summarize_nodes.summarize_context") => g with { SummarizeContext = model },
        (CoreLlm.PromptRoutes.SummarizeNodesGroup g, "summarize_nodes.summary_description") => g with { SummaryDescription = model },
        (CoreLlm.PromptRoutes.SummarizeSagasGroup g, "summarize_sagas.summarize_saga") => g with { SummarizeSaga = model },
        (CoreLlm.PromptRoutes.EvalGroup g, "eval.query_expansion") => g with { QueryExpansion = model },
        (CoreLlm.PromptRoutes.EvalGroup g, "eval.qa_prompt") => g with { QaPrompt = model },
        (CoreLlm.PromptRoutes.EvalGroup g, "eval.eval_prompt") => g with { EvalPrompt = model },
        (CoreLlm.PromptRoutes.EvalGroup g, "eval.eval_add_episode_results") => g with { EvalAddEpisodeResults = model },
        _ => throw new ArgumentException($"Unknown prompt name: {name}"),
    };
}

/// <summary>
/// The read-only view of <see cref="PromptContext.Values"/>. Each read maps the upstream value to the value kinds of the
/// public records, so a value with no public form throws only when a builder reads it.
/// </summary>
internal sealed class PromptContextValues(OrderedDictionary<string, object?> context) : IReadOnlyDictionary<string, object?>
{
    public int Count => context.Count;

    public IEnumerable<string> Keys => context.Keys;

    public IEnumerable<object?> Values => context.Values.Select(PublicValues.ToPublicValue);

    public object? this[string key] => PublicValues.ToPublicValue(context[key]);

    public bool ContainsKey(string key) => context.ContainsKey(key);

    public bool TryGetValue(string key, out object? value)
    {
        if (context.TryGetValue(key, out var raw))
        {
            value = PublicValues.ToPublicValue(raw);
            return true;
        }

        value = null;
        return false;
    }

    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() =>
        context.Select(pair => new KeyValuePair<string, object?>(pair.Key, PublicValues.ToPublicValue(pair.Value))).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
