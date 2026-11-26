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
    // TODO(port): VersionWrapper.__init__
    internal VersionWrapper(PromptFunction func)
    {
        throw new NotSupportedException("TODO(port): VersionWrapper.__init__");
    }

    internal PromptFunction Func { get; set; }

    // TODO(port): no rule for `__call__` on a class. The method is Call, and its method group is a PromptFunction.
    internal List<Message> Call(OrderedDictionary<string, object?> context)
    {
        throw new NotSupportedException("TODO(port): VersionWrapper.__call__");
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
    // PORT: R15, the form of the prompt wrappers
    // TODO(port): PromptTypeWrapper.__init__
    internal PromptTypeWrapper(OrderedDictionary<string, PromptFunction> versions)
    {
        throw new NotSupportedException("TODO(port): PromptTypeWrapper.__init__");
    }

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_nodes.extract_message
    PromptFunction ExtractNodesPrompt.ExtractMessage => throw new NotSupportedException("TODO(port): extract_nodes.extract_message");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_nodes.extract_json
    PromptFunction ExtractNodesPrompt.ExtractJson => throw new NotSupportedException("TODO(port): extract_nodes.extract_json");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_nodes.extract_text
    PromptFunction ExtractNodesPrompt.ExtractText => throw new NotSupportedException("TODO(port): extract_nodes.extract_text");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_nodes.classify_nodes
    PromptFunction ExtractNodesPrompt.ClassifyNodes => throw new NotSupportedException("TODO(port): extract_nodes.classify_nodes");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_nodes.extract_attributes
    PromptFunction ExtractNodesPrompt.ExtractAttributes => throw new NotSupportedException("TODO(port): extract_nodes.extract_attributes");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_nodes.extract_summary
    PromptFunction ExtractNodesPrompt.ExtractSummary => throw new NotSupportedException("TODO(port): extract_nodes.extract_summary");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_nodes.extract_summaries_batch
    PromptFunction ExtractNodesPrompt.ExtractSummariesBatch => throw new NotSupportedException("TODO(port): extract_nodes.extract_summaries_batch");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_nodes.extract_entity_summaries_from_episodes
    PromptFunction ExtractNodesPrompt.ExtractEntitySummariesFromEpisodes => throw new NotSupportedException("TODO(port): extract_nodes.extract_entity_summaries_from_episodes");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): dedupe_nodes.node
    PromptFunction DedupeNodesPrompt.Node => throw new NotSupportedException("TODO(port): dedupe_nodes.node");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): dedupe_nodes.node_list
    PromptFunction DedupeNodesPrompt.NodeList => throw new NotSupportedException("TODO(port): dedupe_nodes.node_list");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): dedupe_nodes.nodes
    PromptFunction DedupeNodesPrompt.Nodes => throw new NotSupportedException("TODO(port): dedupe_nodes.nodes");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_edges.edge
    PromptFunction ExtractEdgesPrompt.Edge => throw new NotSupportedException("TODO(port): extract_edges.edge");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_edges.extract_attributes
    PromptFunction ExtractEdgesPrompt.ExtractAttributes => throw new NotSupportedException("TODO(port): extract_edges.extract_attributes");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_edges.extract_timestamps
    PromptFunction ExtractEdgesPrompt.ExtractTimestamps => throw new NotSupportedException("TODO(port): extract_edges.extract_timestamps");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_edges.extract_timestamps_batch
    PromptFunction ExtractEdgesPrompt.ExtractTimestampsBatch => throw new NotSupportedException("TODO(port): extract_edges.extract_timestamps_batch");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_nodes_and_edges.extract_message
    PromptFunction ExtractNodesAndEdgesPrompt.ExtractMessage => throw new NotSupportedException("TODO(port): extract_nodes_and_edges.extract_message");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): dedupe_edges.resolve_edge
    PromptFunction DedupeEdgesPrompt.ResolveEdge => throw new NotSupportedException("TODO(port): dedupe_edges.resolve_edge");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): summarize_nodes.summarize_pair
    PromptFunction SummarizeNodesPrompt.SummarizePair => throw new NotSupportedException("TODO(port): summarize_nodes.summarize_pair");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): summarize_nodes.summarize_context
    PromptFunction SummarizeNodesPrompt.SummarizeContext => throw new NotSupportedException("TODO(port): summarize_nodes.summarize_context");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): summarize_nodes.summary_description
    PromptFunction SummarizeNodesPrompt.SummaryDescription => throw new NotSupportedException("TODO(port): summarize_nodes.summary_description");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): summarize_sagas.summarize_saga
    PromptFunction SummarizeSagasPrompt.SummarizeSaga => throw new NotSupportedException("TODO(port): summarize_sagas.summarize_saga");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): eval.qa_prompt
    PromptFunction EvalPrompt.QaPrompt => throw new NotSupportedException("TODO(port): eval.qa_prompt");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): eval.eval_prompt
    PromptFunction EvalPrompt.EvalPromptFunction => throw new NotSupportedException("TODO(port): eval.eval_prompt");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): eval.query_expansion
    PromptFunction EvalPrompt.QueryExpansion => throw new NotSupportedException("TODO(port): eval.query_expansion");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): eval.eval_add_episode_results
    PromptFunction EvalPrompt.EvalAddEpisodeResults => throw new NotSupportedException("TODO(port): eval.eval_add_episode_results");
}

// PORT: R15, the form of the prompt wrappers. PromptLibraryWrapper sets one attribute for each group with
// `setattr`. This class implements the interface PromptLibrary.
internal sealed class PromptLibraryWrapper : PromptLibrary
{
    // PORT: R15, the form of the prompt wrappers
    // TODO(port): PromptLibraryWrapper.__init__
    internal PromptLibraryWrapper(OrderedDictionary<string, OrderedDictionary<string, PromptFunction>> library)
    {
        throw new NotSupportedException("TODO(port): PromptLibraryWrapper.__init__");
    }

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_nodes
    ExtractNodesPrompt PromptLibrary.ExtractNodes => throw new NotSupportedException("TODO(port): extract_nodes");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): dedupe_nodes
    DedupeNodesPrompt PromptLibrary.DedupeNodes => throw new NotSupportedException("TODO(port): dedupe_nodes");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_edges
    ExtractEdgesPrompt PromptLibrary.ExtractEdges => throw new NotSupportedException("TODO(port): extract_edges");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): extract_nodes_and_edges
    ExtractNodesAndEdgesPrompt PromptLibrary.ExtractNodesAndEdges => throw new NotSupportedException("TODO(port): extract_nodes_and_edges");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): dedupe_edges
    DedupeEdgesPrompt PromptLibrary.DedupeEdges => throw new NotSupportedException("TODO(port): dedupe_edges");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): summarize_nodes
    SummarizeNodesPrompt PromptLibrary.SummarizeNodes => throw new NotSupportedException("TODO(port): summarize_nodes");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): summarize_sagas
    SummarizeSagasPrompt PromptLibrary.SummarizeSagas => throw new NotSupportedException("TODO(port): summarize_sagas");

    // PORT: R15, the form of the prompt wrappers
    // TODO(port): eval
    EvalPrompt PromptLibrary.Eval => throw new NotSupportedException("TODO(port): eval");
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

    // TODO(port): _builtin_specs
    private static OrderedDictionary<string, PromptSpec> BuiltinSpecs()
    {
        throw new NotSupportedException("TODO(port): _builtin_specs");
    }

    // PORT: `MappingProxyType(_builtin_specs())` is a read-only view (R14). The value needs a call to a skeleton
    // member, so it is a property that throws until the unit ports it.
    // TODO(port): BUILTIN_PROMPT_SPECS
    internal static IReadOnlyDictionary<string, PromptSpec> BuiltinPromptSpecs =>
        throw new NotSupportedException("TODO(port): BUILTIN_PROMPT_SPECS");

    // TODO(port): resolve_response_model
    internal static IResponseModel? ResolveResponseModel(string promptName, IResponseModel? responseModel = null)
    {
        throw new NotSupportedException("TODO(port): resolve_response_model");
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

// PORT: the private attributes `_extract_nodes` to `_specs` are not declared. The unit adds them with the
// constructor body.
internal sealed class DefaultChatPromptLibrary : ChatPromptLibrary
{
    // TODO(port): DefaultChatPromptLibrary.__init__
    internal DefaultChatPromptLibrary()
    {
        throw new NotSupportedException("TODO(port): DefaultChatPromptLibrary.__init__");
    }

    // TODO(port): DefaultChatPromptLibrary.extract_nodes
    internal override ExtractNodesPrompts ExtractNodes => throw new NotSupportedException("TODO(port): DefaultChatPromptLibrary.extract_nodes");

    // TODO(port): DefaultChatPromptLibrary.dedupe_nodes
    internal override DedupeNodesPrompts DedupeNodes => throw new NotSupportedException("TODO(port): DefaultChatPromptLibrary.dedupe_nodes");

    // TODO(port): DefaultChatPromptLibrary.extract_edges
    internal override ExtractEdgesPrompts ExtractEdges => throw new NotSupportedException("TODO(port): DefaultChatPromptLibrary.extract_edges");

    // TODO(port): DefaultChatPromptLibrary.extract_nodes_and_edges
    internal override ExtractNodesAndEdgesPrompts ExtractNodesAndEdges => throw new NotSupportedException("TODO(port): DefaultChatPromptLibrary.extract_nodes_and_edges");

    // TODO(port): DefaultChatPromptLibrary.dedupe_edges
    internal override DedupeEdgesPrompts DedupeEdges => throw new NotSupportedException("TODO(port): DefaultChatPromptLibrary.dedupe_edges");

    // TODO(port): DefaultChatPromptLibrary.summarize_nodes
    internal override SummarizeNodesPrompts SummarizeNodes => throw new NotSupportedException("TODO(port): DefaultChatPromptLibrary.summarize_nodes");

    // TODO(port): DefaultChatPromptLibrary.summarize_sagas
    internal override SummarizeSagasPrompts SummarizeSagas => throw new NotSupportedException("TODO(port): DefaultChatPromptLibrary.summarize_sagas");

    // TODO(port): DefaultChatPromptLibrary.eval
    internal override EvalPrompts Eval => throw new NotSupportedException("TODO(port): DefaultChatPromptLibrary.eval");

    // TODO(port): DefaultChatPromptLibrary.specs
    internal override IReadOnlyDictionary<string, PromptSpec> Specs =>
        throw new NotSupportedException("TODO(port): DefaultChatPromptLibrary.specs");
}

internal static partial class Lib
{
    // PORT: a module-level object that needs a call to a skeleton member, so it is a property that throws until the
    // unit ports it. The unit makes it a static readonly field: one object, as in Python (R17).
    // TODO(port): default_chat_prompt_library
    internal static DefaultChatPromptLibrary DefaultChatPromptLibrary =>
        throw new NotSupportedException("TODO(port): default_chat_prompt_library");

    // TODO(port): no rule for a module-level type alias. `PromptOverrides = dict[str, dict[str, ChatPromptFunction]]`
    // has no C# type. Each annotation `PromptOverrides` is
    // OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>>.
}

// PORT: a class with `__getattr__` implements IPromptGroup and forwards in TryGetFunction (R15). `base: Any` is the
// group of the base library, an IPromptGroup. The private attributes `_base` and `_overrides` are not declared.
internal sealed class OverrideGroup : IPromptGroup
{
    // TODO(port): _OverrideGroup.__init__
    internal OverrideGroup(IPromptGroup @base, OrderedDictionary<string, ChatPromptFunction> overrides)
    {
        throw new NotSupportedException("TODO(port): _OverrideGroup.__init__");
    }

    // TODO(port): _OverrideGroup.__getattr__
    public bool TryGetFunction(string name, out ChatPromptFunction? function)
    {
        throw new NotSupportedException("TODO(port): _OverrideGroup.__getattr__");
    }
}

// PORT: a class with `__getattr__` implements IPromptLibrary and forwards in TryGetGroup and Specs (R15). The private
// attributes `_base` and `_groups` are not declared.
internal sealed class ComposedPromptLibrary : IPromptLibrary
{
    // TODO(port): _ComposedPromptLibrary.__init__
    internal ComposedPromptLibrary(
        ChatPromptLibrary @base,
        OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>> overrides)
    {
        throw new NotSupportedException("TODO(port): _ComposedPromptLibrary.__init__");
    }

    // TODO(port): _ComposedPromptLibrary.__getattr__, the name `specs`
    public IReadOnlyDictionary<string, PromptSpec>? Specs =>
        throw new NotSupportedException("TODO(port): _ComposedPromptLibrary.__getattr__");

    // TODO(port): _ComposedPromptLibrary.__getattr__, the group names
    public bool TryGetGroup(string name, out IPromptGroup? group)
    {
        throw new NotSupportedException("TODO(port): _ComposedPromptLibrary.__getattr__");
    }
}

internal static partial class Lib
{
    // PORT: `result: Any`. Judge cases pass a ChatPrompt, a list, and a string.
    // TODO(port): ensure_chat_prompt
    internal static ChatPrompt EnsureChatPrompt(object? result, string label)
    {
        throw new NotSupportedException("TODO(port): ensure_chat_prompt");
    }

    // TODO(port): no rule for `_ensure_chat_prompt`, whose name without the underscore is the name of
    // `ensure_chat_prompt` in the same class. The name takes the prefix Private.
    internal static ChatPrompt PrivateEnsureChatPrompt(object? result, string label)
    {
        throw new NotSupportedException("TODO(port): _ensure_chat_prompt");
    }

    // PORT: `library: Any` is read with getattr by name, so it is an IPromptLibrary (R15).
    // TODO(port): get_prompt_builder
    internal static ChatPromptFunction GetPromptBuilder(IPromptLibrary library, string promptName)
    {
        throw new NotSupportedException("TODO(port): get_prompt_builder");
    }

    // PORT: a module-level object that needs a call to a skeleton member, so it is a property that throws until the
    // unit ports it.
    // PORT: R15, the form of the prompt wrappers
    // TODO(port): prompt_library
    internal static PromptLibrary PromptLibrary =>
        throw new NotSupportedException("TODO(port): prompt_library");

    // PORT: the annotation of the result is ChatPromptLibrary, but the override path returns a _ComposedPromptLibrary
    // with `# type: ignore[return-value]`. The C# result is the type that both share, IPromptLibrary.
    // TODO(port): create_prompt_library
    internal static IPromptLibrary CreatePromptLibrary(
        OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>>? overrides = null)
    {
        throw new NotSupportedException("TODO(port): create_prompt_library");
    }
}

// PORT: a class with `__getattr__` implements IPromptLibrary and forwards in TryGetGroup (R15). `library: Any` is an
// IPromptLibrary. The private attributes `_library` and `_specs` are not declared.
internal sealed class LibraryWithBuiltinSpecs : IPromptLibrary
{
    // TODO(port): _LibraryWithBuiltinSpecs.__init__
    internal LibraryWithBuiltinSpecs(IPromptLibrary library)
    {
        throw new NotSupportedException("TODO(port): _LibraryWithBuiltinSpecs.__init__");
    }

    IReadOnlyDictionary<string, PromptSpec>? IPromptLibrary.Specs => Specs;

    // TODO(port): _LibraryWithBuiltinSpecs.specs
    internal IReadOnlyDictionary<string, PromptSpec> Specs =>
        throw new NotSupportedException("TODO(port): _LibraryWithBuiltinSpecs.specs");

    // TODO(port): _LibraryWithBuiltinSpecs.__getattr__
    public bool TryGetGroup(string name, out IPromptGroup? group)
    {
        throw new NotSupportedException("TODO(port): _LibraryWithBuiltinSpecs.__getattr__");
    }
}

internal static partial class Lib
{
    // PORT: `library: Any` is read with hasattr and getattr by name, so it is an IPromptLibrary (R15).
    // TODO(port): validate_prompt_library
    internal static void ValidatePromptLibrary(IPromptLibrary library)
    {
        throw new NotSupportedException("TODO(port): validate_prompt_library");
    }

    // PORT: the parameter annotation is ChatPromptLibrary and the result is Any. llm_runtime.py passes the result of
    // create_prompt_library, which can be a _ComposedPromptLibrary, so both are IPromptLibrary.
    // TODO(port): ensure_prompt_library_wrapped
    internal static IPromptLibrary EnsurePromptLibraryWrapped(IPromptLibrary library)
    {
        throw new NotSupportedException("TODO(port): ensure_prompt_library_wrapped");
    }
}
