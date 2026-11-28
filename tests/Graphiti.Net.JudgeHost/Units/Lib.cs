namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/lib.py</c>.</summary>
internal static class LibBindings
{
    private const string Module = "graphiti_core.prompts.lib";

    internal static void Register(BindingTable table)
    {
        // `X.copy()` on a dict and on a mappingproxy: a new dict with the same entries.
        table.Pure.Add(Module + ":PROMPT_GROUPS.copy", call =>
        {
            call.Signature();
            return Codec.Result(new OrderedDictionary<string, IReadOnlyList<string>>(Lib.PromptGroups));
        });
        table.Pure.Add(Module + ":BUILTIN_PROMPT_SPECS.copy", call =>
        {
            call.Signature();
            return Codec.Result(new OrderedDictionary<string, PromptSpec>(Lib.BuiltinPromptSpecs));
        });

        table.Pure.Add(Module + ":resolve_response_model", call => call.Signature("prompt_name", "response_model").InvokeAsync(Lib.ResolveResponseModel));
        table.Pure.Add(Module + ":ensure_chat_prompt", call => call.Signature("result", "label").InvokeAsync(Lib.EnsureChatPrompt));
        table.Pure.Add(Module + ":get_prompt_builder", call => call.Signature("library", "prompt_name").InvokeAsync(Lib.GetPromptBuilder));
        table.Pure.Add(Module + ":create_prompt_library", call => call.Signature("overrides").InvokeAsync(Lib.CreatePromptLibrary));
        // PORT: a dict is a library with no attribute (TypesBindings.AsPromptLibrary).
        table.Pure.Add(Module + ":validate_prompt_library", call =>
            call.Signature("library").InvokeAsync((Action<object?>)(library => Lib.ValidatePromptLibrary(TypesBindings.AsPromptLibrary(library)))));

        // `prompt_library.<group>.<function>(context)`: the legacy wrapper, which returns list[Message].
        Legacy(table, "extract_nodes.extract_message", Lib.PromptLibrary.ExtractNodes.ExtractMessage);
        Legacy(table, "extract_nodes.extract_json", Lib.PromptLibrary.ExtractNodes.ExtractJson);
        Legacy(table, "extract_nodes.extract_text", Lib.PromptLibrary.ExtractNodes.ExtractText);
        Legacy(table, "extract_nodes.classify_nodes", Lib.PromptLibrary.ExtractNodes.ClassifyNodes);
        Legacy(table, "extract_nodes.extract_attributes", Lib.PromptLibrary.ExtractNodes.ExtractAttributes);
        Legacy(table, "extract_nodes.extract_summary", Lib.PromptLibrary.ExtractNodes.ExtractSummary);
        Legacy(table, "extract_nodes.extract_summaries_batch", Lib.PromptLibrary.ExtractNodes.ExtractSummariesBatch);
        Legacy(table, "extract_nodes.extract_entity_summaries_from_episodes", Lib.PromptLibrary.ExtractNodes.ExtractEntitySummariesFromEpisodes);
        Legacy(table, "dedupe_nodes.node", Lib.PromptLibrary.DedupeNodes.Node);
        Legacy(table, "dedupe_nodes.node_list", Lib.PromptLibrary.DedupeNodes.NodeList);
        Legacy(table, "dedupe_nodes.nodes", Lib.PromptLibrary.DedupeNodes.Nodes);
        Legacy(table, "extract_edges.edge", Lib.PromptLibrary.ExtractEdges.Edge);
        Legacy(table, "extract_edges.extract_attributes", Lib.PromptLibrary.ExtractEdges.ExtractAttributes);
        Legacy(table, "extract_edges.extract_timestamps", Lib.PromptLibrary.ExtractEdges.ExtractTimestamps);
        Legacy(table, "extract_edges.extract_timestamps_batch", Lib.PromptLibrary.ExtractEdges.ExtractTimestampsBatch);
        Legacy(table, "extract_nodes_and_edges.extract_message", Lib.PromptLibrary.ExtractNodesAndEdges.ExtractMessage);
        Legacy(table, "dedupe_edges.resolve_edge", Lib.PromptLibrary.DedupeEdges.ResolveEdge);
        Legacy(table, "summarize_nodes.summarize_pair", Lib.PromptLibrary.SummarizeNodes.SummarizePair);
        Legacy(table, "summarize_nodes.summarize_context", Lib.PromptLibrary.SummarizeNodes.SummarizeContext);
        Legacy(table, "summarize_nodes.summary_description", Lib.PromptLibrary.SummarizeNodes.SummaryDescription);
        Legacy(table, "summarize_sagas.summarize_saga", Lib.PromptLibrary.SummarizeSagas.SummarizeSaga);
        Legacy(table, "eval.query_expansion", Lib.PromptLibrary.Eval.QueryExpansion);
        Legacy(table, "eval.qa_prompt", Lib.PromptLibrary.Eval.QaPrompt);
        Legacy(table, "eval.eval_prompt", Lib.PromptLibrary.Eval.EvalPromptFunction);
        Legacy(table, "eval.eval_add_episode_results", Lib.PromptLibrary.Eval.EvalAddEpisodeResults);

        // `default_chat_prompt_library.<group>.<function>(context)`: the chat library, which returns a ChatPrompt.
        Chat(table, "extract_nodes.extract_message", Lib.DefaultChatPromptLibrary.ExtractNodes.ExtractMessage);
        Chat(table, "extract_nodes.extract_json", Lib.DefaultChatPromptLibrary.ExtractNodes.ExtractJson);
        Chat(table, "extract_nodes.extract_text", Lib.DefaultChatPromptLibrary.ExtractNodes.ExtractText);
        Chat(table, "extract_nodes.classify_nodes", Lib.DefaultChatPromptLibrary.ExtractNodes.ClassifyNodes);
        Chat(table, "extract_nodes.extract_attributes", Lib.DefaultChatPromptLibrary.ExtractNodes.ExtractAttributes);
        Chat(table, "extract_nodes.extract_summary", Lib.DefaultChatPromptLibrary.ExtractNodes.ExtractSummary);
        Chat(table, "extract_nodes.extract_summaries_batch", Lib.DefaultChatPromptLibrary.ExtractNodes.ExtractSummariesBatch);
        Chat(table, "extract_nodes.extract_entity_summaries_from_episodes", Lib.DefaultChatPromptLibrary.ExtractNodes.ExtractEntitySummariesFromEpisodes);
        Chat(table, "dedupe_nodes.node", Lib.DefaultChatPromptLibrary.DedupeNodes.Node);
        Chat(table, "dedupe_nodes.node_list", Lib.DefaultChatPromptLibrary.DedupeNodes.NodeList);
        Chat(table, "dedupe_nodes.nodes", Lib.DefaultChatPromptLibrary.DedupeNodes.Nodes);
        Chat(table, "extract_edges.edge", Lib.DefaultChatPromptLibrary.ExtractEdges.Edge);
        Chat(table, "extract_edges.extract_attributes", Lib.DefaultChatPromptLibrary.ExtractEdges.ExtractAttributes);
        Chat(table, "extract_edges.extract_timestamps", Lib.DefaultChatPromptLibrary.ExtractEdges.ExtractTimestamps);
        Chat(table, "extract_edges.extract_timestamps_batch", Lib.DefaultChatPromptLibrary.ExtractEdges.ExtractTimestampsBatch);
        Chat(table, "extract_nodes_and_edges.extract_message", Lib.DefaultChatPromptLibrary.ExtractNodesAndEdges.ExtractMessage);
        Chat(table, "dedupe_edges.resolve_edge", Lib.DefaultChatPromptLibrary.DedupeEdges.ResolveEdge);
        Chat(table, "summarize_nodes.summarize_pair", Lib.DefaultChatPromptLibrary.SummarizeNodes.SummarizePair);
        Chat(table, "summarize_nodes.summarize_context", Lib.DefaultChatPromptLibrary.SummarizeNodes.SummarizeContext);
        Chat(table, "summarize_nodes.summary_description", Lib.DefaultChatPromptLibrary.SummarizeNodes.SummaryDescription);
        Chat(table, "summarize_sagas.summarize_saga", Lib.DefaultChatPromptLibrary.SummarizeSagas.SummarizeSaga);
        Chat(table, "eval.query_expansion", Lib.DefaultChatPromptLibrary.Eval.QueryExpansion);
        Chat(table, "eval.qa_prompt", Lib.DefaultChatPromptLibrary.Eval.QaPrompt);
        Chat(table, "eval.eval_prompt", Lib.DefaultChatPromptLibrary.Eval.EvalPrompt);
        Chat(table, "eval.eval_add_episode_results", Lib.DefaultChatPromptLibrary.Eval.EvalAddEpisodeResults);

        table.Registry.AddRef(Module + ":default_chat_prompt_library", Lib.DefaultChatPromptLibrary);
    }

    private static void Legacy(BindingTable table, string name, PromptFunction function) =>
        table.Pure.Add(Module + ":prompt_library." + name, call => call.Signature("context").InvokeAsync(function));

    private static void Chat(BindingTable table, string name, ChatPromptFunction function) =>
        table.Pure.Add(Module + ":default_chat_prompt_library." + name, call => call.Signature("context").InvokeAsync(function));
}
