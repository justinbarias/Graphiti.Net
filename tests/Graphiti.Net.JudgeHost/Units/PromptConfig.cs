namespace Graphiti.JudgeHost.Units;

using System.Reflection;
using Graphiti.Core.LlmClient;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The bindings of <c>graphiti_core/llm_client/prompt_config.py</c>.</summary>
internal static class PromptConfigBindings
{
    private const string Module = "graphiti_core.llm_client.prompt_config";

    internal static void Register(BindingTable table)
    {
        table.Registry
            .AddRef(Module + ":LLMPromptOverrides", typeof(LLMPromptOverrides))
            .AddRef(Module + ":PromptRoutes", typeof(PromptRoutes))
            .AddModel(Module + ":LLMPromptOverrides", Dataclass<LLMPromptOverrides>)
            .AddModel(Module + ":LLMPromptOverrides.ExtractNodes", Dataclass<LLMPromptOverrides.ExtractNodesGroup>)
            .AddModel(Module + ":LLMPromptOverrides.DedupeNodes", Dataclass<LLMPromptOverrides.DedupeNodesGroup>)
            .AddModel(Module + ":LLMPromptOverrides.ExtractEdges", Dataclass<LLMPromptOverrides.ExtractEdgesGroup>)
            .AddModel(Module + ":LLMPromptOverrides.ExtractNodesAndEdges", Dataclass<LLMPromptOverrides.ExtractNodesAndEdgesGroup>)
            .AddModel(Module + ":LLMPromptOverrides.DedupeEdges", Dataclass<LLMPromptOverrides.DedupeEdgesGroup>)
            .AddModel(Module + ":LLMPromptOverrides.SummarizeNodes", Dataclass<LLMPromptOverrides.SummarizeNodesGroup>)
            .AddModel(Module + ":LLMPromptOverrides.SummarizeSagas", Dataclass<LLMPromptOverrides.SummarizeSagasGroup>)
            .AddModel(Module + ":LLMPromptOverrides.Eval", Dataclass<LLMPromptOverrides.EvalGroup>)
            .AddModel(Module + ":PromptRoutes", Dataclass<PromptRoutes>)
            .AddModel(Module + ":PromptRoutes.ExtractNodes", Dataclass<PromptRoutes.ExtractNodesGroup>)
            .AddModel(Module + ":PromptRoutes.DedupeNodes", Dataclass<PromptRoutes.DedupeNodesGroup>)
            .AddModel(Module + ":PromptRoutes.ExtractEdges", Dataclass<PromptRoutes.ExtractEdgesGroup>)
            .AddModel(Module + ":PromptRoutes.ExtractNodesAndEdges", Dataclass<PromptRoutes.ExtractNodesAndEdgesGroup>)
            .AddModel(Module + ":PromptRoutes.DedupeEdges", Dataclass<PromptRoutes.DedupeEdgesGroup>)
            .AddModel(Module + ":PromptRoutes.SummarizeNodes", Dataclass<PromptRoutes.SummarizeNodesGroup>)
            .AddModel(Module + ":PromptRoutes.SummarizeSagas", Dataclass<PromptRoutes.SummarizeSagasGroup>)
            .AddModel(Module + ":PromptRoutes.Eval", Dataclass<PromptRoutes.EvalGroup>);

        // `_nested_group_class` returns a class, which the result names with its Python name (R15).
        table.Pure.Add(Module + ":_nested_group_class", call =>
        {
            var p = call.Signature("parent", "group_name");
            return Codec.Result(new JudgeClass(PythonName(PromptConfig.NestedGroupClass(p.Ref<Type>("parent"), p.String("group_name")))));
        });
        table.Pure.Add(Module + ":flatten_overrides", call => call.Signature("overrides").InvokeAsync(PromptConfig.FlattenOverrides));
        table.Pure.Add(Module + ":flatten_routes", call => call.Signature("routes").InvokeAsync(PromptConfig.FlattenRoutes));

        // LLMTransport(client, *, models=None). A case passes a text for the client, which is a type rejection (R22).
        table.Pure.Add(Module + ":LLMTransport", call =>
        {
            var p = call.Signature("client", "models");
            var client = p.Model<LLMClient>("client");
            return Codec.Result(p.Has("models") ? new LLMTransport(client, p.NullableList<string>("models")) : new LLMTransport(client));
        });

        // LLMModel(id, transport, prompt_overrides=None, max_tokens=None): a dataclass with `__post_init__`.
        table.Pure.Add(Module + ":LLMModel", call =>
        {
            var p = call.Signature("id", "transport", "prompt_overrides", "max_tokens");
            var model = new LLMModel { Id = p.String("id"), Transport = p.Model<LLMTransport>("transport") };
            if (p.Has("prompt_overrides"))
            {
                model = model with { PromptOverrides = p.GetOrNone<LLMPromptOverrides>("prompt_overrides") };
            }

            if (p.Has("max_tokens"))
            {
                model = model with { MaxTokens = p.NullableLong("max_tokens") };
            }

            return Codec.Result(model.Validate());
        });
    }

    // The Python name of a nested group record: its constant `PythonName`.
    private static string PythonName(Type group) =>
        group.GetField("PythonName", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)?.GetRawConstantValue() as string
        ?? throw new JudgeHostErrorException($"The type {group.FullName} has no constant PythonName.");

    // The Python field names of each dataclass, in field order. A keyword that is not in the table is the TypeError of
    // Python. The C# property of a name has the Pascal case of it (R2), and no other spelling reaches a property.
    private static readonly Dictionary<Type, string[]> FieldNames = new()
    {
        [typeof(LLMPromptOverrides)] = ["extract_nodes", "dedupe_nodes", "extract_edges", "extract_nodes_and_edges", "dedupe_edges", "summarize_nodes", "summarize_sagas", "eval"],
        [typeof(PromptRoutes)] = ["extract_nodes", "dedupe_nodes", "extract_edges", "extract_nodes_and_edges", "dedupe_edges", "summarize_nodes", "summarize_sagas", "eval"],
        [typeof(LLMPromptOverrides.ExtractNodesGroup)] = ["extract_message", "extract_json", "extract_text", "classify_nodes", "extract_attributes", "extract_summary", "extract_summaries_batch", "extract_entity_summaries_from_episodes"],
        [typeof(LLMPromptOverrides.DedupeNodesGroup)] = ["node", "node_list", "nodes"],
        [typeof(LLMPromptOverrides.ExtractEdgesGroup)] = ["edge", "extract_attributes", "extract_timestamps", "extract_timestamps_batch"],
        [typeof(LLMPromptOverrides.ExtractNodesAndEdgesGroup)] = ["extract_message"],
        [typeof(LLMPromptOverrides.DedupeEdgesGroup)] = ["resolve_edge"],
        [typeof(LLMPromptOverrides.SummarizeNodesGroup)] = ["summarize_pair", "summarize_context", "summary_description"],
        [typeof(LLMPromptOverrides.SummarizeSagasGroup)] = ["summarize_saga"],
        [typeof(LLMPromptOverrides.EvalGroup)] = ["query_expansion", "qa_prompt", "eval_prompt", "eval_add_episode_results"],
        [typeof(PromptRoutes.ExtractNodesGroup)] = ["default", "extract_message", "extract_json", "extract_text", "classify_nodes", "extract_attributes", "extract_summary", "extract_summaries_batch", "extract_entity_summaries_from_episodes"],
        [typeof(PromptRoutes.DedupeNodesGroup)] = ["default", "node", "node_list", "nodes"],
        [typeof(PromptRoutes.ExtractEdgesGroup)] = ["default", "edge", "extract_attributes", "extract_timestamps", "extract_timestamps_batch"],
        [typeof(PromptRoutes.ExtractNodesAndEdgesGroup)] = ["default", "extract_message"],
        [typeof(PromptRoutes.DedupeEdgesGroup)] = ["default", "resolve_edge"],
        [typeof(PromptRoutes.SummarizeNodesGroup)] = ["default", "summarize_pair", "summarize_context", "summary_description"],
        [typeof(PromptRoutes.SummarizeSagasGroup)] = ["default", "summarize_saga"],
        [typeof(PromptRoutes.EvalGroup)] = ["default", "query_expansion", "qa_prompt", "eval_prompt", "eval_add_episode_results"],
    };

    // A frozen dataclass M(**kwargs): each keyword names a field, and None is the default of each field. A keyword that
    // names no field, and a value that the field cannot hold, are a TypeError in Python or a type rejection here (R22).
    private static T Dataclass<T>(OrderedDictionary<string, object?> data)
        where T : class
    {
        var instance = (T)(Activator.CreateInstance(typeof(T), nonPublic: true)
            ?? throw new JudgeHostErrorException($"The type {typeof(T).FullName} has no constructor."));
        foreach (var (key, value) in data)
        {
            if (!FieldNames[typeof(T)].Contains(key, StringComparer.Ordinal))
            {
                throw new JudgeTypeRejectedException($"__init__() got an unexpected keyword argument '{key}'");
            }

            var field = typeof(T).GetProperty(
                    string.Concat(PyText.Split(key, "_").Select(PyText.Capitalize)),
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new JudgeHostErrorException($"The type {typeof(T).FullName} has no property for the field {key}.");
            if (value is not null)
            {
                field.SetValue(instance, JudgeTypes.Convert(field.PropertyType, value, $"argument '{key}'"));
            }
        }

        return instance;
    }
}
