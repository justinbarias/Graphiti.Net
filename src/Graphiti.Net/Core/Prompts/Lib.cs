// Ported from graphiti_core/prompts/lib.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core.Prompts;

using Graphiti.Internal;
using Graphiti.PyCompat;

// PORT: the two interfaces of R15. The pilot declared them for real (R24). Each library class below implements
// IPromptLibrary, and each group class implements IPromptGroup, so that `getattr` by name has a typed form.
internal interface IPromptGroup
{
    bool TryGetFunction(string name, out ChatPromptFunction? function);
}

internal interface IPromptLibrary
{
    IReadOnlyDictionary<string, PromptSpec>? Specs { get; }

    bool TryGetGroup(string name, out IPromptGroup? group);
}

// PORT: the module class of R2, split around the classes to keep the order of the Python file.
internal static partial class Lib
{
    // Canonical group -> method names for structural validation and overrides.
    internal static readonly OrderedDictionary<string, IReadOnlyList<string>> PromptGroups = new()
    {
        ["extract_nodes"] =
        [
            "extract_message",
            "extract_json",
            "extract_text",
            "classify_nodes",
            "extract_attributes",
            "extract_summary",
            "extract_summaries_batch",
            "extract_entity_summaries_from_episodes",
        ],
        ["dedupe_nodes"] = ["node", "node_list", "nodes"],
        ["extract_edges"] =
        [
            "edge",
            "extract_attributes",
            "extract_timestamps",
            "extract_timestamps_batch",
        ],
        ["extract_nodes_and_edges"] = ["extract_message"],
        ["dedupe_edges"] = ["resolve_edge"],
        ["summarize_nodes"] = ["summarize_pair", "summarize_context", "summary_description"],
        ["summarize_sagas"] = ["summarize_saga"],
        ["eval"] =
        [
            "query_expansion",
            "qa_prompt",
            "eval_prompt",
            "eval_add_episode_results",
        ],
    };
}

// PORT: a Protocol is an interface (R15). A member has the name of its group in PascalCase.
internal interface PromptLibrary
{
    ExtractNodesPrompt ExtractNodes { get; }

    DedupeNodesPrompt DedupeNodes { get; }

    ExtractEdgesPrompt ExtractEdges { get; }

    ExtractNodesAndEdgesPrompt ExtractNodesAndEdges { get; }

    DedupeEdgesPrompt DedupeEdges { get; }

    SummarizeNodesPrompt SummarizeNodes { get; }

    SummarizeSagasPrompt SummarizeSagas { get; }

    EvalPrompt Eval { get; }
}

// PORT: `PromptLibraryImpl` is a TypedDict that PromptLibraryWrapper reads with `.items()`, so it has no C# type,
// as `Versions` has none (R2). A value of it is an OrderedDictionary<string, OrderedDictionary<string, PromptFunction>>.

internal sealed class VersionWrapper
{
    internal VersionWrapper(PromptFunction func)
    {
        Func = func;
    }

    internal PromptFunction Func { get; set; }

    // PORT: Call for __call__ (R2). Its method group is a PromptFunction.
    internal List<Message> Call(OrderedDictionary<string, object?> context)
    {
        var messages = Func(context);
        foreach (var message in messages)
        {
            message.Content += message.Role == "system" ? PromptHelpers.DoNotEscapeUnicode : string.Empty;
        }

        return messages;
    }
}

// PORT: R15, the form of the prompt wrappers. PromptTypeWrapper sets one attribute for each prompt version of its
// group with `setattr`. This class implements the 8 `<Module>Prompt` interfaces, each member with an explicit implementation, so that
// `extract_attributes` and `extract_message`, which are in two groups each, get one member for each group. A member
// of a group that the instance does not hold throws, as Python raises AttributeError.
internal sealed class PromptTypeWrapper :
    ExtractNodesPrompt,
    DedupeNodesPrompt,
    ExtractEdgesPrompt,
    ExtractNodesAndEdgesPrompt,
    DedupeEdgesPrompt,
    SummarizeNodesPrompt,
    SummarizeSagasPrompt,
    EvalPrompt
{
    // PORT: R15. The attributes that `setattr` sets, by version name.
    private readonly Dictionary<string, VersionWrapper> attributes = [];

    // PORT: R15, the form of the prompt wrappers
    internal PromptTypeWrapper(OrderedDictionary<string, PromptFunction> versions)
    {
        foreach (var (version, func) in versions)
        {
            attributes[version] = new VersionWrapper(func);
        }
    }

    PromptFunction ExtractNodesPrompt.ExtractMessage =>
        attributes.TryGetValue("extract_message", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("extract_message");

    PromptFunction ExtractNodesPrompt.ExtractJson =>
        attributes.TryGetValue("extract_json", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("extract_json");

    PromptFunction ExtractNodesPrompt.ExtractText =>
        attributes.TryGetValue("extract_text", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("extract_text");

    PromptFunction ExtractNodesPrompt.ClassifyNodes =>
        attributes.TryGetValue("classify_nodes", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("classify_nodes");

    PromptFunction ExtractNodesPrompt.ExtractAttributes =>
        attributes.TryGetValue("extract_attributes", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("extract_attributes");

    PromptFunction ExtractNodesPrompt.ExtractSummary =>
        attributes.TryGetValue("extract_summary", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("extract_summary");

    PromptFunction ExtractNodesPrompt.ExtractSummariesBatch =>
        attributes.TryGetValue("extract_summaries_batch", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("extract_summaries_batch");

    PromptFunction ExtractNodesPrompt.ExtractEntitySummariesFromEpisodes =>
        attributes.TryGetValue("extract_entity_summaries_from_episodes", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("extract_entity_summaries_from_episodes");

    PromptFunction DedupeNodesPrompt.Node =>
        attributes.TryGetValue("node", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("node");

    PromptFunction DedupeNodesPrompt.NodeList =>
        attributes.TryGetValue("node_list", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("node_list");

    PromptFunction DedupeNodesPrompt.Nodes =>
        attributes.TryGetValue("nodes", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("nodes");

    PromptFunction ExtractEdgesPrompt.Edge =>
        attributes.TryGetValue("edge", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("edge");

    PromptFunction ExtractEdgesPrompt.ExtractAttributes =>
        attributes.TryGetValue("extract_attributes", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("extract_attributes");

    PromptFunction ExtractEdgesPrompt.ExtractTimestamps =>
        attributes.TryGetValue("extract_timestamps", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("extract_timestamps");

    PromptFunction ExtractEdgesPrompt.ExtractTimestampsBatch =>
        attributes.TryGetValue("extract_timestamps_batch", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("extract_timestamps_batch");

    PromptFunction ExtractNodesAndEdgesPrompt.ExtractMessage =>
        attributes.TryGetValue("extract_message", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("extract_message");

    PromptFunction DedupeEdgesPrompt.ResolveEdge =>
        attributes.TryGetValue("resolve_edge", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("resolve_edge");

    PromptFunction SummarizeNodesPrompt.SummarizePair =>
        attributes.TryGetValue("summarize_pair", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("summarize_pair");

    PromptFunction SummarizeNodesPrompt.SummarizeContext =>
        attributes.TryGetValue("summarize_context", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("summarize_context");

    PromptFunction SummarizeNodesPrompt.SummaryDescription =>
        attributes.TryGetValue("summary_description", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("summary_description");

    PromptFunction SummarizeSagasPrompt.SummarizeSaga =>
        attributes.TryGetValue("summarize_saga", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("summarize_saga");

    PromptFunction EvalPrompt.QaPrompt =>
        attributes.TryGetValue("qa_prompt", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("qa_prompt");

    PromptFunction EvalPrompt.EvalPromptFunction =>
        attributes.TryGetValue("eval_prompt", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("eval_prompt");

    PromptFunction EvalPrompt.QueryExpansion =>
        attributes.TryGetValue("query_expansion", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("query_expansion");

    PromptFunction EvalPrompt.EvalAddEpisodeResults =>
        attributes.TryGetValue("eval_add_episode_results", out var version) ? new PromptFunction(version.Call) : throw new MissingMemberException("eval_add_episode_results");
}

// PORT: R15, the form of the prompt wrappers. PromptLibraryWrapper sets one attribute for each group with
// `setattr`. This class implements the interface PromptLibrary.
internal sealed class PromptLibraryWrapper : PromptLibrary
{
    // PORT: R15. The attributes that `setattr` sets, by group name.
    private readonly Dictionary<string, PromptTypeWrapper> attributes = [];

    // PORT: R15, the form of the prompt wrappers
    internal PromptLibraryWrapper(OrderedDictionary<string, OrderedDictionary<string, PromptFunction>> library)
    {
        foreach (var (promptType, versions) in library)
        {
            attributes[promptType] = new PromptTypeWrapper(versions);
        }
    }

    ExtractNodesPrompt PromptLibrary.ExtractNodes =>
        attributes.TryGetValue("extract_nodes", out var group) ? group : throw new MissingMemberException("extract_nodes");

    DedupeNodesPrompt PromptLibrary.DedupeNodes =>
        attributes.TryGetValue("dedupe_nodes", out var group) ? group : throw new MissingMemberException("dedupe_nodes");

    ExtractEdgesPrompt PromptLibrary.ExtractEdges =>
        attributes.TryGetValue("extract_edges", out var group) ? group : throw new MissingMemberException("extract_edges");

    ExtractNodesAndEdgesPrompt PromptLibrary.ExtractNodesAndEdges =>
        attributes.TryGetValue("extract_nodes_and_edges", out var group) ? group : throw new MissingMemberException("extract_nodes_and_edges");

    DedupeEdgesPrompt PromptLibrary.DedupeEdges =>
        attributes.TryGetValue("dedupe_edges", out var group) ? group : throw new MissingMemberException("dedupe_edges");

    SummarizeNodesPrompt PromptLibrary.SummarizeNodes =>
        attributes.TryGetValue("summarize_nodes", out var group) ? group : throw new MissingMemberException("summarize_nodes");

    SummarizeSagasPrompt PromptLibrary.SummarizeSagas =>
        attributes.TryGetValue("summarize_sagas", out var group) ? group : throw new MissingMemberException("summarize_sagas");

    EvalPrompt PromptLibrary.Eval =>
        attributes.TryGetValue("eval", out var group) ? group : throw new MissingMemberException("eval");
}

internal static partial class Lib
{
    internal static readonly OrderedDictionary<string, OrderedDictionary<string, PromptFunction>> PromptLibraryImpl = new()
    {
        ["extract_nodes"] = ExtractNodes.Versions,
        ["dedupe_nodes"] = DedupeNodes.Versions,
        ["extract_edges"] = ExtractEdges.Versions,
        ["extract_nodes_and_edges"] = ExtractNodesAndEdges.Versions,
        ["dedupe_edges"] = DedupeEdges.Versions,
        ["summarize_nodes"] = SummarizeNodes.Versions,
        ["summarize_sagas"] = SummarizeSagas.Versions,
        ["eval"] = Eval.Versions,
    };

    internal const string ChatPromptMigrationNote =
        "Prompt overrides must return ChatPrompt(system=..., user=...). "
        + "Returning list[Message] is no longer supported.";

    // Fixed prompt_name -> response_model registry. Schemas are not overridable.
    private static OrderedDictionary<string, PromptSpec> BuiltinSpecs()
    {
        return new OrderedDictionary<string, PromptSpec>
        {
            ["extract_nodes.extract_message"] = new PromptSpec
            {
                Name = "extract_nodes.extract_message",
                ResponseModel = ExtractedEntities.ResponseModel,
            }.Validate(),
            ["extract_nodes.extract_text"] = new PromptSpec
            {
                Name = "extract_nodes.extract_text",
                ResponseModel = ExtractedEntities.ResponseModel,
            }.Validate(),
            ["extract_nodes.extract_json"] = new PromptSpec
            {
                Name = "extract_nodes.extract_json",
                ResponseModel = ExtractedEntities.ResponseModel,
            }.Validate(),
            ["extract_nodes.classify_nodes"] = new PromptSpec
            {
                Name = "extract_nodes.classify_nodes",
            }.Validate(),
            ["extract_nodes.extract_attributes"] = new PromptSpec
            {
                Name = "extract_nodes.extract_attributes",
                DynamicSchema = true,
            }.Validate(),
            ["extract_nodes.extract_summary"] = new PromptSpec
            {
                Name = "extract_nodes.extract_summary",
            }.Validate(),
            ["extract_nodes.extract_summaries_batch"] = new PromptSpec
            {
                Name = "extract_nodes.extract_summaries_batch",
                ResponseModel = SummarizedEntities.ResponseModel,
            }.Validate(),
            ["extract_nodes.extract_entity_summaries_from_episodes"] = new PromptSpec
            {
                Name = "extract_nodes.extract_entity_summaries_from_episodes",
                ResponseModel = SummarizedEntities.ResponseModel,
            }.Validate(),
            ["dedupe_nodes.node"] = new PromptSpec
            {
                Name = "dedupe_nodes.node",
                ResponseModel = NodeDuplicate.ResponseModel,
            }.Validate(),
            ["dedupe_nodes.node_list"] = new PromptSpec
            {
                Name = "dedupe_nodes.node_list",
            }.Validate(),
            ["dedupe_nodes.nodes"] = new PromptSpec
            {
                Name = "dedupe_nodes.nodes",
                ResponseModel = NodeResolutions.ResponseModel,
            }.Validate(),
            ["extract_edges.edge"] = new PromptSpec
            {
                Name = "extract_edges.edge",
                ResponseModel = ExtractedEdges.ResponseModel,
            }.Validate(),
            ["extract_edges.extract_attributes"] = new PromptSpec
            {
                Name = "extract_edges.extract_attributes",
                DynamicSchema = true,
            }.Validate(),
            ["extract_edges.extract_timestamps"] = new PromptSpec
            {
                Name = "extract_edges.extract_timestamps",
                ResponseModel = EdgeTimestamps.ResponseModel,
            }.Validate(),
            ["extract_edges.extract_timestamps_batch"] = new PromptSpec
            {
                Name = "extract_edges.extract_timestamps_batch",
                ResponseModel = BatchEdgeTimestamps.ResponseModel,
            }.Validate(),
            ["extract_nodes_and_edges.extract_message"] = new PromptSpec
            {
                Name = "extract_nodes_and_edges.extract_message",
                ResponseModel = CombinedExtraction.ResponseModel,
            }.Validate(),
            ["dedupe_edges.resolve_edge"] = new PromptSpec
            {
                Name = "dedupe_edges.resolve_edge",
                ResponseModel = EdgeDuplicate.ResponseModel,
            }.Validate(),
            ["summarize_nodes.summarize_pair"] = new PromptSpec
            {
                Name = "summarize_nodes.summarize_pair",
                ResponseModel = Summary.ResponseModel,
            }.Validate(),
            ["summarize_nodes.summarize_context"] = new PromptSpec
            {
                Name = "summarize_nodes.summarize_context",
                ResponseModel = Summary.ResponseModel,
            }.Validate(),
            ["summarize_nodes.summary_description"] = new PromptSpec
            {
                Name = "summarize_nodes.summary_description",
                ResponseModel = SummaryDescription.ResponseModel,
            }.Validate(),
            ["summarize_sagas.summarize_saga"] = new PromptSpec
            {
                Name = "summarize_sagas.summarize_saga",
                ResponseModel = SagaSummary.ResponseModel,
            }.Validate(),
            ["eval.query_expansion"] = new PromptSpec
            {
                Name = "eval.query_expansion",
                ResponseModel = QueryExpansion.ResponseModel,
            }.Validate(),
            ["eval.qa_prompt"] = new PromptSpec
            {
                Name = "eval.qa_prompt",
                ResponseModel = QAResponse.ResponseModel,
            }.Validate(),
            ["eval.eval_prompt"] = new PromptSpec
            {
                Name = "eval.eval_prompt",
                ResponseModel = EvalResponse.ResponseModel,
            }.Validate(),
            ["eval.eval_add_episode_results"] = new PromptSpec
            {
                Name = "eval.eval_add_episode_results",
                ResponseModel = EvalAddEpisodeResults.ResponseModel,
            }.Validate(),
        };
    }

    // PORT: `MappingProxyType(...)` is a live read-only view (R14).
    internal static readonly IReadOnlyDictionary<string, PromptSpec> BuiltinPromptSpecs = BuiltinSpecs().AsReadOnly<string, PromptSpec>();

    /// <summary>
    /// Return the fixed schema for <c>promptName</c>, or the caller schema if dynamic.
    ///
    /// Raises <c>ValueError</c> for unknown names, missing dynamic schemas, and attempts
    /// to replace a fixed schema.
    /// </summary>
    internal static IResponseModel? ResolveResponseModel(string promptName, IResponseModel? responseModel = null)
    {
        var spec = BuiltinPromptSpecs.GetValueOrDefault(promptName);
        if (spec is null)
        {
            throw new ArgumentException($"Unknown prompt_name: {promptName}");
        }

        if (spec.DynamicSchema)
        {
            if (responseModel is null)
            {
                throw new ArgumentException(
                    $"prompt {promptName} has dynamic_schema=True; response_model is required");
            }

            return responseModel;
        }

        if (responseModel is not null && !ReferenceEquals(responseModel, spec.ResponseModel))
        {
            throw new ArgumentException(
                $"Prompt schema overrides are not allowed for {promptName}: "
                + $"got {responseModel.PythonName}, expected "
                + $"{(spec.ResponseModel is not null ? spec.ResponseModel.PythonName : "None")}");
        }

        return spec.ResponseModel;
    }
}

// PORT: an ABC whose members are abstract properties (R15). It implements IPromptLibrary: TryGetGroup gives the
// property of each group name, which is `getattr(library, group_name)`.
internal abstract class ChatPromptLibrary : IPromptLibrary
{
    IReadOnlyDictionary<string, PromptSpec>? IPromptLibrary.Specs => Specs;

    internal abstract ExtractNodesPrompts ExtractNodes { get; }

    internal abstract DedupeNodesPrompts DedupeNodes { get; }

    internal abstract ExtractEdgesPrompts ExtractEdges { get; }

    internal abstract ExtractNodesAndEdgesPrompts ExtractNodesAndEdges { get; }

    internal abstract DedupeEdgesPrompts DedupeEdges { get; }

    internal abstract SummarizeNodesPrompts SummarizeNodes { get; }

    internal abstract SummarizeSagasPrompts SummarizeSagas { get; }

    internal abstract EvalPrompts Eval { get; }

    internal abstract IReadOnlyDictionary<string, PromptSpec> Specs { get; }

    public bool TryGetGroup(string name, out IPromptGroup? group)
    {
        group = name switch
        {
            "extract_nodes" => ExtractNodes,
            "dedupe_nodes" => DedupeNodes,
            "extract_edges" => ExtractEdges,
            "extract_nodes_and_edges" => ExtractNodesAndEdges,
            "dedupe_edges" => DedupeEdges,
            "summarize_nodes" => SummarizeNodes,
            "summarize_sagas" => SummarizeSagas,
            "eval" => Eval,
            _ => null,
        };
        return group is not null;
    }
}

internal sealed class DefaultChatPromptLibrary : ChatPromptLibrary
{
    private readonly DefaultExtractNodesPrompts extractNodes;
    private readonly DefaultDedupeNodesPrompts dedupeNodes;
    private readonly DefaultExtractEdgesPrompts extractEdges;
    private readonly DefaultExtractNodesAndEdgesPrompts extractNodesAndEdges;
    private readonly DefaultDedupeEdgesPrompts dedupeEdges;
    private readonly DefaultSummarizeNodesPrompts summarizeNodes;
    private readonly DefaultSummarizeSagasPrompts summarizeSagas;
    private readonly DefaultEvalPrompts eval;
    private readonly IReadOnlyDictionary<string, PromptSpec> specs;

    internal DefaultChatPromptLibrary()
    {
        extractNodes = new DefaultExtractNodesPrompts();
        dedupeNodes = new DefaultDedupeNodesPrompts();
        extractEdges = new DefaultExtractEdgesPrompts();
        extractNodesAndEdges = new DefaultExtractNodesAndEdgesPrompts();
        dedupeEdges = new DefaultDedupeEdgesPrompts();
        summarizeNodes = new DefaultSummarizeNodesPrompts();
        summarizeSagas = new DefaultSummarizeSagasPrompts();
        eval = new DefaultEvalPrompts();
        specs = Lib.BuiltinPromptSpecs;
    }

    internal override ExtractNodesPrompts ExtractNodes => extractNodes;

    internal override DedupeNodesPrompts DedupeNodes => dedupeNodes;

    internal override ExtractEdgesPrompts ExtractEdges => extractEdges;

    internal override ExtractNodesAndEdgesPrompts ExtractNodesAndEdges => extractNodesAndEdges;

    internal override DedupeEdgesPrompts DedupeEdges => dedupeEdges;

    internal override SummarizeNodesPrompts SummarizeNodes => summarizeNodes;

    internal override SummarizeSagasPrompts SummarizeSagas => summarizeSagas;

    internal override EvalPrompts Eval => eval;

    internal override IReadOnlyDictionary<string, PromptSpec> Specs => specs;
}

internal static partial class Lib
{
    // PORT: one object, as in Python (R17).
    internal static readonly DefaultChatPromptLibrary DefaultChatPromptLibrary = new();

    // PORT: no type for a type alias (R2). `PromptOverrides = dict[str, dict[str, ChatPromptFunction]]`
    // has no C# type. Each annotation `PromptOverrides` is
    // OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>>.
}

// PORT: a class with `__getattr__` implements IPromptGroup and forwards in TryGetFunction (R15). `base: Any` is the
// group of the base library, an IPromptGroup.
internal sealed class OverrideGroup : IPromptGroup
{
    private readonly IPromptGroup @base;
    private readonly OrderedDictionary<string, ChatPromptFunction> overrides;

    internal OverrideGroup(IPromptGroup @base, OrderedDictionary<string, ChatPromptFunction> overrides)
    {
        this.@base = @base;
        this.overrides = overrides;
    }

    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        if (overrides.TryGetValue(name, out var @override))
        {
            function = context =>
            {
                var result = @override(context);
                return Lib.PrivateEnsureChatPrompt(result, $"override.{name}");
            };
            return true;
        }

        return @base.TryGetFunction(name, out function);
    }
}

// PORT: a class with `__getattr__` implements IPromptLibrary and forwards in TryGetGroup and Specs (R15).
internal sealed class ComposedPromptLibrary : IPromptLibrary
{
    private readonly ChatPromptLibrary @base;
    private readonly Dictionary<string, IPromptGroup> groups = [];

    internal ComposedPromptLibrary(
        ChatPromptLibrary @base,
        OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>> overrides)
    {
        this.@base = @base;
        foreach (var groupName in Lib.PromptGroups.Keys)
        {
            var groupOverrides = overrides.GetValueOrDefault(groupName, []);
            if (!@base.TryGetGroup(groupName, out var baseGroup) || baseGroup is null)
            {
                throw new MissingMemberException(groupName);
            }

            if (groupOverrides.Count > 0)
            {
                groups[groupName] = new OverrideGroup(baseGroup, groupOverrides);
            }
            else
            {
                groups[groupName] = baseGroup;
            }
        }
    }

    // PORT: `__getattr__` with the name `specs`.
    public IReadOnlyDictionary<string, PromptSpec>? Specs => @base.Specs;

    // PORT: `__getattr__` with a group name.
    public bool TryGetGroup(string name, out IPromptGroup? group)
    {
        if (groups.TryGetValue(name, out var found))
        {
            group = found;
            return true;
        }

        group = null;
        return false;
    }
}

internal static partial class Lib
{
    // PORT: `result: Any`. Judge cases pass a ChatPrompt, a list, and a string.
    internal static ChatPrompt EnsureChatPrompt(object? result, string label)
    {
        if (result is ChatPrompt chatPrompt)
        {
            return chatPrompt;
        }

        if (Py.TypeName(result) == "list")
        {
            throw new InvalidCastException($"{label} returned list[Message]; {ChatPromptMigrationNote}");
        }

        throw new InvalidCastException(
            $"{label} must return ChatPrompt, got {Py.TypeName(result)}. {ChatPromptMigrationNote}");
    }

    // PORT: the prefix Private (R2). `_ensure_chat_prompt` without the underscore is the name of
    // `ensure_chat_prompt` in the same class.
    internal static ChatPrompt PrivateEnsureChatPrompt(object? result, string label)
    {
        return EnsureChatPrompt(result, label);
    }

    /// <summary>Resolve <c>group.method</c> from a prompt library to a callable builder.</summary>
    // PORT: `library: Any` is read with getattr by name, so it is an IPromptLibrary (R15).
    internal static ChatPromptFunction GetPromptBuilder(IPromptLibrary library, string promptName)
    {
        if (!promptName.Contains('.', StringComparison.Ordinal))
        {
            throw new ArgumentException($"Invalid prompt_name (expected group.method): {promptName}");
        }

        var parts = PyText.Split(promptName, ".", 1);
        var groupName = parts[0];
        var methodName = parts[1];
        if (!PromptGroups.TryGetValue(groupName, out var methodNames))
        {
            throw new ArgumentException($"Unknown prompt group: {groupName}");
        }

        if (!methodNames.Contains(methodName))
        {
            throw new ArgumentException($"Unknown prompt function for group {groupName}: {methodName}");
        }

        if (!library.TryGetGroup(groupName, out var group))
        {
            throw new MissingMemberException(groupName);
        }

        // PORT: an attribute that is not a group has no such method: getattr raises AttributeError.
        if (group is null || !group.TryGetFunction(methodName, out var builder))
        {
            throw new MissingMemberException(methodName);
        }

        if (builder is null)
        {
            throw new ArgumentException($"Prompt library function must be callable: {promptName}");
        }

        return context => EnsureChatPrompt(builder(context), promptName);
    }

    // PORT: R15, the form of the prompt wrappers
    internal static readonly PromptLibrary PromptLibrary = new PromptLibraryWrapper(PromptLibraryImpl);

    /// <summary>
    /// Create a prompt library, optionally applying partial overrides to the defaults.
    ///
    /// Override callables must return <c>ChatPrompt</c>. Returning <c>list[Message]</c> raises
    /// <c>TypeError</c> with a migration note.
    /// </summary>
    // PORT: the annotation of the result is ChatPromptLibrary, but the override path returns a _ComposedPromptLibrary
    // with `# type: ignore[return-value]`. The C# result is the type that both share, IPromptLibrary.
    internal static IPromptLibrary CreatePromptLibrary(
        OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>>? overrides = null)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return new DefaultChatPromptLibrary();
        }

        foreach (var (groupName, groupOverrides) in overrides)
        {
            if (!PromptGroups.TryGetValue(groupName, out var functionNames))
            {
                throw new ArgumentException($"Unknown prompt group: {groupName}");
            }

            foreach (var (functionName, function) in groupOverrides)
            {
                if (!functionNames.Contains(functionName))
                {
                    throw new ArgumentException($"Unknown prompt function for group {groupName}: {functionName}");
                }

                if (function is null)
                {
                    throw new ArgumentException($"Prompt override must be callable: {groupName}.{functionName}");
                }
            }
        }

        return new ComposedPromptLibrary(new DefaultChatPromptLibrary(), overrides);
    }
}

// PORT: a class with `__getattr__` implements IPromptLibrary and forwards in TryGetGroup (R15). `library: Any` is an
// IPromptLibrary.
internal sealed class LibraryWithBuiltinSpecs : IPromptLibrary
{
    private readonly IPromptLibrary library;
    private readonly IReadOnlyDictionary<string, PromptSpec> specs;

    internal LibraryWithBuiltinSpecs(IPromptLibrary library)
    {
        this.library = library;
        specs = Lib.BuiltinPromptSpecs;
    }

    IReadOnlyDictionary<string, PromptSpec>? IPromptLibrary.Specs => Specs;

    internal IReadOnlyDictionary<string, PromptSpec> Specs => specs;

    public bool TryGetGroup(string name, out IPromptGroup? group)
    {
        return library.TryGetGroup(name, out group);
    }
}

internal static partial class Lib
{
    /// <summary>Structurally validate a complete prompt library (duck-typed; ABC not required).</summary>
    // PORT: `library: Any` is read with hasattr and getattr by name, so it is an IPromptLibrary (R15).
    internal static void ValidatePromptLibrary(IPromptLibrary library)
    {
        foreach (var (groupName, functionNames) in PromptGroups)
        {
            if (!library.TryGetGroup(groupName, out var group))
            {
                throw new ArgumentException($"Prompt library missing group: {groupName}");
            }

            foreach (var functionName in functionNames)
            {
                // PORT: an attribute that is not a group has no such function: hasattr is false.
                if (group is null || !group.TryGetFunction(functionName, out var function))
                {
                    throw new ArgumentException($"Prompt library missing function: {groupName}.{functionName}");
                }

                if (function is null)
                {
                    throw new ArgumentException(
                        $"Prompt library function must be callable: {groupName}.{functionName}");
                }
            }
        }

        // PORT: `Specs` is typed as a mapping of PromptSpec, so the two branches `must be a mapping` and
        // `must be PromptSpec` cannot be reached. A null `Specs` stands for `not hasattr(library, 'specs')`.
        var specs = library.Specs;
        if (specs is not null)
        {
            foreach (var (name, spec) in BuiltinPromptSpecs)
            {
                if (!specs.TryGetValue(name, out var libSpec))
                {
                    throw new ArgumentException($"Prompt library specs missing entry: {name}");
                }

                if (libSpec.DynamicSchema != spec.DynamicSchema)
                {
                    throw new ArgumentException(
                        $"Prompt schema overrides are not allowed: {name} dynamic_schema mismatch");
                }

                if (!ReferenceEquals(libSpec.ResponseModel, spec.ResponseModel))
                {
                    throw new ArgumentException(
                        $"Prompt schema overrides are not allowed: {name} response_model mismatch");
                }
            }
        }
    }

    /// <summary>
    /// Validate and return the library, attaching builtin specs when missing.
    ///
    /// Unicode post-processing is handled by <c>ChatPrompt.as_messages</c>; no VersionWrapper.
    /// </summary>
    // PORT: the parameter annotation is ChatPromptLibrary and the result is Any. llm_runtime.py passes the result of
    // create_prompt_library, which can be a _ComposedPromptLibrary, so both are IPromptLibrary.
    internal static IPromptLibrary EnsurePromptLibraryWrapped(IPromptLibrary library)
    {
        ValidatePromptLibrary(library);
        if (library.Specs is null)
        {
            return new LibraryWithBuiltinSpecs(library);
        }

        return library;
    }
}
