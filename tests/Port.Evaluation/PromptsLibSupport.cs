// What the tests of graphiti_core/prompts/lib.py share: a duck-typed library that stands for a `SimpleNamespace`
// (the upstream tests build one by hand), the typed read of the legacy `prompt_library`, and the override builders.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Prompts;
using Graphiti.Internal;

/// <summary>A hand-built group: one attribute for each function. A null value is an attribute that is not callable.</summary>
internal sealed class HandBuiltGroup(Dictionary<string, ChatPromptFunction?> functions) : IPromptGroup
{
    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        var found = functions.TryGetValue(name, out var value);
        function = value;
        return found;
    }
}

/// <summary>A hand-built library: one attribute for each group. A null group is an attribute that is not a group.</summary>
internal sealed class HandBuiltLibrary(
    Dictionary<string, IPromptGroup?> groups,
    IReadOnlyDictionary<string, PromptSpec>? specs) : IPromptLibrary
{
    public IReadOnlyDictionary<string, PromptSpec>? Specs => specs;

    public bool TryGetGroup(string name, out IPromptGroup? group)
    {
        var found = groups.TryGetValue(name, out var value);
        group = value;
        return found;
    }
}

/// <summary>
/// A <see cref="ChatPromptLibrary"/> subclass that gives the groups and the specs of the default library, except that
/// one group property returns null (the Python subclass whose property returns <c>None</c>) and the specs can be another
/// mapping.
/// </summary>
internal sealed class DelegatingChatLibrary(string? nullGroup, IReadOnlyDictionary<string, PromptSpec>? specs) : ChatPromptLibrary
{
    private static DefaultChatPromptLibrary Default => Lib.DefaultChatPromptLibrary;

    internal override ExtractNodesPrompts ExtractNodes => nullGroup == "extract_nodes" ? null! : Default.ExtractNodes;

    internal override DedupeNodesPrompts DedupeNodes => nullGroup == "dedupe_nodes" ? null! : Default.DedupeNodes;

    internal override ExtractEdgesPrompts ExtractEdges => nullGroup == "extract_edges" ? null! : Default.ExtractEdges;

    internal override ExtractNodesAndEdgesPrompts ExtractNodesAndEdges =>
        nullGroup == "extract_nodes_and_edges" ? null! : Default.ExtractNodesAndEdges;

    internal override DedupeEdgesPrompts DedupeEdges => nullGroup == "dedupe_edges" ? null! : Default.DedupeEdges;

    internal override SummarizeNodesPrompts SummarizeNodes => nullGroup == "summarize_nodes" ? null! : Default.SummarizeNodes;

    internal override SummarizeSagasPrompts SummarizeSagas => nullGroup == "summarize_sagas" ? null! : Default.SummarizeSagas;

    internal override EvalPrompts Eval => nullGroup == "eval" ? null! : Default.Eval;

    internal override IReadOnlyDictionary<string, PromptSpec> Specs => specs ?? Default.Specs;
}

internal static class PromptsLibSupport
{
    internal static ChatPrompt Chat(string system, string user) => new()
    {
        System = new SystemMessage { Content = system }.Validate(),
        User = new UserMessage { Content = user }.Validate(),
    };

    /// <summary>The override of the upstream test: a ChatPrompt whose user text ends with <c>context['x']</c>.</summary>
    internal static ChatPromptFunction Override(string system, string userPrefix) =>
        context => Chat(system, userPrefix + (string)context["x"]!);

    internal static OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>> Overrides(
        params (string Group, string Function, string System, string UserPrefix)[] items)
    {
        var result = new OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>>();
        foreach (var (group, function, system, userPrefix) in items)
        {
            if (!result.TryGetValue(group, out var functions))
            {
                functions = [];
                result[group] = functions;
            }

            functions[function] = Override(system, userPrefix);
        }

        return result;
    }

    /// <summary><c>getattr(prompt_library, group)</c>: the typed member of the legacy library for a group name.</summary>
    internal static object GroupOf(PromptLibrary library, string group) => group switch
    {
        "extract_nodes" => library.ExtractNodes,
        "dedupe_nodes" => library.DedupeNodes,
        "extract_edges" => library.ExtractEdges,
        "extract_nodes_and_edges" => library.ExtractNodesAndEdges,
        "dedupe_edges" => library.DedupeEdges,
        "summarize_nodes" => library.SummarizeNodes,
        "summarize_sagas" => library.SummarizeSagas,
        "eval" => library.Eval,
        _ => throw new MissingMemberException(group),
    };

    /// <summary><c>getattr(group, function)</c>: the typed member of a legacy group for a function name.</summary>
    internal static PromptFunction FunctionOf(object group, string name, string function) => (name, function) switch
    {
        ("extract_nodes", "extract_message") => ((ExtractNodesPrompt)group).ExtractMessage,
        ("extract_nodes", "extract_json") => ((ExtractNodesPrompt)group).ExtractJson,
        ("extract_nodes", "extract_text") => ((ExtractNodesPrompt)group).ExtractText,
        ("extract_nodes", "classify_nodes") => ((ExtractNodesPrompt)group).ClassifyNodes,
        ("extract_nodes", "extract_attributes") => ((ExtractNodesPrompt)group).ExtractAttributes,
        ("extract_nodes", "extract_summary") => ((ExtractNodesPrompt)group).ExtractSummary,
        ("extract_nodes", "extract_summaries_batch") => ((ExtractNodesPrompt)group).ExtractSummariesBatch,
        ("extract_nodes", "extract_entity_summaries_from_episodes") => ((ExtractNodesPrompt)group).ExtractEntitySummariesFromEpisodes,
        ("dedupe_nodes", "node") => ((DedupeNodesPrompt)group).Node,
        ("dedupe_nodes", "node_list") => ((DedupeNodesPrompt)group).NodeList,
        ("dedupe_nodes", "nodes") => ((DedupeNodesPrompt)group).Nodes,
        ("extract_edges", "edge") => ((ExtractEdgesPrompt)group).Edge,
        ("extract_edges", "extract_attributes") => ((ExtractEdgesPrompt)group).ExtractAttributes,
        ("extract_edges", "extract_timestamps") => ((ExtractEdgesPrompt)group).ExtractTimestamps,
        ("extract_edges", "extract_timestamps_batch") => ((ExtractEdgesPrompt)group).ExtractTimestampsBatch,
        ("extract_nodes_and_edges", "extract_message") => ((ExtractNodesAndEdgesPrompt)group).ExtractMessage,
        ("dedupe_edges", "resolve_edge") => ((DedupeEdgesPrompt)group).ResolveEdge,
        ("summarize_nodes", "summarize_pair") => ((SummarizeNodesPrompt)group).SummarizePair,
        ("summarize_nodes", "summarize_context") => ((SummarizeNodesPrompt)group).SummarizeContext,
        ("summarize_nodes", "summary_description") => ((SummarizeNodesPrompt)group).SummaryDescription,
        ("summarize_sagas", "summarize_saga") => ((SummarizeSagasPrompt)group).SummarizeSaga,
        ("eval", "query_expansion") => ((EvalPrompt)group).QueryExpansion,
        ("eval", "qa_prompt") => ((EvalPrompt)group).QaPrompt,
        ("eval", "eval_prompt") => ((EvalPrompt)group).EvalPromptFunction,
        ("eval", "eval_add_episode_results") => ((EvalPrompt)group).EvalAddEpisodeResults,
        _ => throw new MissingMemberException(function),
    };

    /// <summary>A hand-built library that returns the same ChatPrompt from each function, as the upstream test does.</summary>
    internal static HandBuiltLibrary HandBuilt(
        IReadOnlySet<string> absent,
        IReadOnlySet<string> notGroup,
        IReadOnlySet<(string Group, string Function)> drop,
        IReadOnlySet<(string Group, string Function)> notCallable,
        IReadOnlyDictionary<string, PromptSpec>? specs,
        bool returnsNone = false)
    {
        var groups = new Dictionary<string, IPromptGroup?>();
        foreach (var (group, functions) in Lib.PromptGroups)
        {
            if (absent.Contains(group))
            {
                continue;
            }

            if (notGroup.Contains(group))
            {
                groups[group] = null;
                continue;
            }

            var members = new Dictionary<string, ChatPromptFunction?>();
            foreach (var function in functions)
            {
                if (drop.Contains((group, function)))
                {
                    continue;
                }

                if (notCallable.Contains((group, function)))
                {
                    members[function] = null;
                }
                else if (returnsNone)
                {
                    // An upstream function that returns `None`: a null result of the delegate.
                    members[function] = _ => null!;
                }
                else
                {
                    members[function] = _ => Chat("hand-built system", "hand-built user");
                }
            }

            groups[group] = new HandBuiltGroup(members);
        }

        return new HandBuiltLibrary(groups, specs);
    }
}
