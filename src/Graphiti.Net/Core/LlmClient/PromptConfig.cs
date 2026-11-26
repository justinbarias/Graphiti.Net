// Ported from graphiti_core/llm_client/prompt_config.py
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

namespace Graphiti.Core.LlmClient;

using Graphiti.Core.Prompts;

// PORT: `M = TypeVar('M', bound=str)` has no C# form. A model id is a `string`, so LLMTransport is not generic.

// PORT: a frozen dataclass is a sealed record with init accessors (R7).
// PORT: `ExtractNodes` is both a nested dataclass and, as `extract_nodes`, a field, and C# does not allow two members
// with one name (error CS0102). Each nested class takes the suffix Group (R2). Its Python name, for `__qualname__` in
// a message, is `LLMPromptOverrides.ExtractNodes` (R15). The same holds for PromptRoutes.
internal sealed record LLMPromptOverrides
{
    internal sealed record ExtractNodesGroup
    {
        internal const string PythonName = "ExtractNodes";

        internal ChatPromptFunction? ExtractMessage { get; init; }

        internal ChatPromptFunction? ExtractJson { get; init; }

        internal ChatPromptFunction? ExtractText { get; init; }

        internal ChatPromptFunction? ClassifyNodes { get; init; }

        internal ChatPromptFunction? ExtractAttributes { get; init; }

        internal ChatPromptFunction? ExtractSummary { get; init; }

        internal ChatPromptFunction? ExtractSummariesBatch { get; init; }

        internal ChatPromptFunction? ExtractEntitySummariesFromEpisodes { get; init; }
    }

    internal sealed record DedupeNodesGroup
    {
        internal const string PythonName = "DedupeNodes";

        internal ChatPromptFunction? Node { get; init; }

        internal ChatPromptFunction? NodeList { get; init; }

        internal ChatPromptFunction? Nodes { get; init; }
    }

    internal sealed record ExtractEdgesGroup
    {
        internal const string PythonName = "ExtractEdges";

        internal ChatPromptFunction? Edge { get; init; }

        internal ChatPromptFunction? ExtractAttributes { get; init; }

        internal ChatPromptFunction? ExtractTimestamps { get; init; }

        internal ChatPromptFunction? ExtractTimestampsBatch { get; init; }
    }

    internal sealed record ExtractNodesAndEdgesGroup
    {
        internal const string PythonName = "ExtractNodesAndEdges";

        internal ChatPromptFunction? ExtractMessage { get; init; }
    }

    internal sealed record DedupeEdgesGroup
    {
        internal const string PythonName = "DedupeEdges";

        internal ChatPromptFunction? ResolveEdge { get; init; }
    }

    internal sealed record SummarizeNodesGroup
    {
        internal const string PythonName = "SummarizeNodes";

        internal ChatPromptFunction? SummarizePair { get; init; }

        internal ChatPromptFunction? SummarizeContext { get; init; }

        internal ChatPromptFunction? SummaryDescription { get; init; }
    }

    internal sealed record SummarizeSagasGroup
    {
        internal const string PythonName = "SummarizeSagas";

        internal ChatPromptFunction? SummarizeSaga { get; init; }
    }

    internal sealed record EvalGroup
    {
        internal const string PythonName = "Eval";

        internal ChatPromptFunction? QueryExpansion { get; init; }

        internal ChatPromptFunction? QaPrompt { get; init; }

        internal ChatPromptFunction? EvalPrompt { get; init; }

        internal ChatPromptFunction? EvalAddEpisodeResults { get; init; }
    }

    internal ExtractNodesGroup? ExtractNodes { get; init; }

    internal DedupeNodesGroup? DedupeNodes { get; init; }

    internal ExtractEdgesGroup? ExtractEdges { get; init; }

    internal ExtractNodesAndEdgesGroup? ExtractNodesAndEdges { get; init; }

    internal DedupeEdgesGroup? DedupeEdges { get; init; }

    internal SummarizeNodesGroup? SummarizeNodes { get; init; }

    internal SummarizeSagasGroup? SummarizeSagas { get; init; }

    internal EvalGroup? Eval { get; init; }
}

internal static partial class PromptConfig
{
    // PORT: R15 gives a static table from the group name to the nested type for this site. The unit decides the body.
    // Internal, because judge cases call it (R2).
    // TODO(port): _nested_group_class
    internal static Type NestedGroupClass(Type parent, string groupName)
    {
        throw new NotSupportedException("TODO(port): _nested_group_class");
    }

    // TODO(port): flatten_overrides
    internal static OrderedDictionary<string, ChatPromptFunction> FlattenOverrides(LLMPromptOverrides? overrides)
    {
        throw new NotSupportedException("TODO(port): flatten_overrides");
    }
}

// PORT: `models` is keyword-only in Python. A `frozenset` that code tests with `in` and sorts is a HashSet (R14).
internal sealed class LLMTransport
{
    // TODO(port): LLMTransport.__init__
    internal LLMTransport(LLMClient client, IReadOnlyList<string>? models = null)
    {
        throw new NotSupportedException("TODO(port): LLMTransport.__init__");
    }

    internal HashSet<string>? Models { get; set; }

    internal LLMClient Client { get; set; }

    // TODO(port): LLMTransport.supports
    internal bool Supports(string modelId)
    {
        throw new NotSupportedException("TODO(port): LLMTransport.supports");
    }

    // PORT: `prompt_overrides` and `max_tokens` are keyword-only in Python.
    // TODO(port): LLMTransport.model
    internal LLMModel Model(string id, LLMPromptOverrides? promptOverrides = null, long? maxTokens = null)
    {
        throw new NotSupportedException("TODO(port): LLMTransport.model");
    }

    // PORT: Repr() for __repr__ (R2). LLMModel.__post_init__ formats the transport with `!r`.
    internal string Repr()
    {
        throw new NotSupportedException("TODO(port): LLMTransport.__repr__");
    }
}

// PORT: a frozen dataclass. Python compares and hashes it by `id`, `transport`, and `max_tokens` only
// (`compare=False` on the other two fields). A record compares each property. No upstream site compares two models.
// The field `_flat_overrides` (`init=False`) is not declared. The unit adds it with __post_init__.
internal sealed record LLMModel
{
    internal required string Id { get; init; }

    internal required LLMTransport Transport { get; init; }

    internal LLMPromptOverrides? PromptOverrides { get; init; }

    internal long? MaxTokens { get; init; }

    // PORT: Validate() for __post_init__ (R7). Each construction path calls it.
    internal LLMModel Validate()
    {
        throw new NotSupportedException("TODO(port): LLMModel.__post_init__");
    }

    // TODO(port): LLMModel.flat_overrides
    internal IReadOnlyDictionary<string, ChatPromptFunction> FlatOverrides =>
        throw new NotSupportedException("TODO(port): LLMModel.flat_overrides");
}

// PORT: a frozen dataclass, as LLMPromptOverrides, with the suffix Group on each nested class.
internal sealed record PromptRoutes
{
    internal sealed record ExtractNodesGroup
    {
        internal const string PythonName = "ExtractNodes";

        internal LLMModel? Default { get; init; }

        internal LLMModel? ExtractMessage { get; init; }

        internal LLMModel? ExtractJson { get; init; }

        internal LLMModel? ExtractText { get; init; }

        internal LLMModel? ClassifyNodes { get; init; }

        internal LLMModel? ExtractAttributes { get; init; }

        internal LLMModel? ExtractSummary { get; init; }

        internal LLMModel? ExtractSummariesBatch { get; init; }

        internal LLMModel? ExtractEntitySummariesFromEpisodes { get; init; }
    }

    internal sealed record DedupeNodesGroup
    {
        internal const string PythonName = "DedupeNodes";

        internal LLMModel? Default { get; init; }

        internal LLMModel? Node { get; init; }

        internal LLMModel? NodeList { get; init; }

        internal LLMModel? Nodes { get; init; }
    }

    internal sealed record ExtractEdgesGroup
    {
        internal const string PythonName = "ExtractEdges";

        internal LLMModel? Default { get; init; }

        internal LLMModel? Edge { get; init; }

        internal LLMModel? ExtractAttributes { get; init; }

        internal LLMModel? ExtractTimestamps { get; init; }

        internal LLMModel? ExtractTimestampsBatch { get; init; }
    }

    internal sealed record ExtractNodesAndEdgesGroup
    {
        internal const string PythonName = "ExtractNodesAndEdges";

        internal LLMModel? Default { get; init; }

        internal LLMModel? ExtractMessage { get; init; }
    }

    internal sealed record DedupeEdgesGroup
    {
        internal const string PythonName = "DedupeEdges";

        internal LLMModel? Default { get; init; }

        internal LLMModel? ResolveEdge { get; init; }
    }

    internal sealed record SummarizeNodesGroup
    {
        internal const string PythonName = "SummarizeNodes";

        internal LLMModel? Default { get; init; }

        internal LLMModel? SummarizePair { get; init; }

        internal LLMModel? SummarizeContext { get; init; }

        internal LLMModel? SummaryDescription { get; init; }
    }

    internal sealed record SummarizeSagasGroup
    {
        internal const string PythonName = "SummarizeSagas";

        internal LLMModel? Default { get; init; }

        internal LLMModel? SummarizeSaga { get; init; }
    }

    internal sealed record EvalGroup
    {
        internal const string PythonName = "Eval";

        internal LLMModel? Default { get; init; }

        internal LLMModel? QueryExpansion { get; init; }

        internal LLMModel? QaPrompt { get; init; }

        internal LLMModel? EvalPrompt { get; init; }

        internal LLMModel? EvalAddEpisodeResults { get; init; }
    }

    // PORT: R22 allows object? here. The field holds an LLMModel or the nested record, and flatten_routes tests it with is.
    internal object? ExtractNodes { get; init; }

    internal object? DedupeNodes { get; init; }

    internal object? ExtractEdges { get; init; }

    internal object? ExtractNodesAndEdges { get; init; }

    internal object? DedupeEdges { get; init; }

    internal object? SummarizeNodes { get; init; }

    internal object? SummarizeSagas { get; init; }

    internal object? Eval { get; init; }
}

internal static partial class PromptConfig
{
    // TODO(port): flatten_routes
    internal static OrderedDictionary<string, LLMModel> FlattenRoutes(PromptRoutes? routes)
    {
        throw new NotSupportedException("TODO(port): flatten_routes");
    }
}
