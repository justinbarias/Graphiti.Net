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
using Graphiti.PyCompat;

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
    // PORT: R15, the Python name of each nested group record, for `__qualname__` and `type(x).__name__` in a message.
    private static readonly Dictionary<Type, string> PythonNames = new()
    {
        [typeof(LLMPromptOverrides.ExtractNodesGroup)] = LLMPromptOverrides.ExtractNodesGroup.PythonName,
        [typeof(LLMPromptOverrides.DedupeNodesGroup)] = LLMPromptOverrides.DedupeNodesGroup.PythonName,
        [typeof(LLMPromptOverrides.ExtractEdgesGroup)] = LLMPromptOverrides.ExtractEdgesGroup.PythonName,
        [typeof(LLMPromptOverrides.ExtractNodesAndEdgesGroup)] = LLMPromptOverrides.ExtractNodesAndEdgesGroup.PythonName,
        [typeof(LLMPromptOverrides.DedupeEdgesGroup)] = LLMPromptOverrides.DedupeEdgesGroup.PythonName,
        [typeof(LLMPromptOverrides.SummarizeNodesGroup)] = LLMPromptOverrides.SummarizeNodesGroup.PythonName,
        [typeof(LLMPromptOverrides.SummarizeSagasGroup)] = LLMPromptOverrides.SummarizeSagasGroup.PythonName,
        [typeof(LLMPromptOverrides.EvalGroup)] = LLMPromptOverrides.EvalGroup.PythonName,
        [typeof(PromptRoutes.ExtractNodesGroup)] = PromptRoutes.ExtractNodesGroup.PythonName,
        [typeof(PromptRoutes.DedupeNodesGroup)] = PromptRoutes.DedupeNodesGroup.PythonName,
        [typeof(PromptRoutes.ExtractEdgesGroup)] = PromptRoutes.ExtractEdgesGroup.PythonName,
        [typeof(PromptRoutes.ExtractNodesAndEdgesGroup)] = PromptRoutes.ExtractNodesAndEdgesGroup.PythonName,
        [typeof(PromptRoutes.DedupeEdgesGroup)] = PromptRoutes.DedupeEdgesGroup.PythonName,
        [typeof(PromptRoutes.SummarizeNodesGroup)] = PromptRoutes.SummarizeNodesGroup.PythonName,
        [typeof(PromptRoutes.SummarizeSagasGroup)] = PromptRoutes.SummarizeSagasGroup.PythonName,
        [typeof(PromptRoutes.EvalGroup)] = PromptRoutes.EvalGroup.PythonName,
    };

    // PORT: R15, `fields(overrides)` and `getattr(overrides, group_name)`: the fields of LLMPromptOverrides in field order.
    private static readonly (string Name, Func<LLMPromptOverrides, object?> Get)[] OverrideFields =
    [
        ("extract_nodes", o => o.ExtractNodes),
        ("dedupe_nodes", o => o.DedupeNodes),
        ("extract_edges", o => o.ExtractEdges),
        ("extract_nodes_and_edges", o => o.ExtractNodesAndEdges),
        ("dedupe_edges", o => o.DedupeEdges),
        ("summarize_nodes", o => o.SummarizeNodes),
        ("summarize_sagas", o => o.SummarizeSagas),
        ("eval", o => o.Eval),
    ];

    // PORT: R15, `getattr(group, method_name)` for a group of LLMPromptOverrides. A name that a group does not hold is
    // an AttributeError.
    private static readonly Dictionary<string, Func<object, string, ChatPromptFunction?>> OverrideBuilders = new()
    {
        ["extract_nodes"] = (group, name) =>
        {
            var g = (LLMPromptOverrides.ExtractNodesGroup)group;
            return name switch
            {
                "extract_message" => g.ExtractMessage,
                "extract_json" => g.ExtractJson,
                "extract_text" => g.ExtractText,
                "classify_nodes" => g.ClassifyNodes,
                "extract_attributes" => g.ExtractAttributes,
                "extract_summary" => g.ExtractSummary,
                "extract_summaries_batch" => g.ExtractSummariesBatch,
                "extract_entity_summaries_from_episodes" => g.ExtractEntitySummariesFromEpisodes,
                _ => throw new MissingMemberException($"'ExtractNodes' object has no attribute '{name}'"),
            };
        },
        ["dedupe_nodes"] = (group, name) =>
        {
            var g = (LLMPromptOverrides.DedupeNodesGroup)group;
            return name switch
            {
                "node" => g.Node,
                "node_list" => g.NodeList,
                "nodes" => g.Nodes,
                _ => throw new MissingMemberException($"'DedupeNodes' object has no attribute '{name}'"),
            };
        },
        ["extract_edges"] = (group, name) =>
        {
            var g = (LLMPromptOverrides.ExtractEdgesGroup)group;
            return name switch
            {
                "edge" => g.Edge,
                "extract_attributes" => g.ExtractAttributes,
                "extract_timestamps" => g.ExtractTimestamps,
                "extract_timestamps_batch" => g.ExtractTimestampsBatch,
                _ => throw new MissingMemberException($"'ExtractEdges' object has no attribute '{name}'"),
            };
        },
        ["extract_nodes_and_edges"] = (group, name) =>
        {
            var g = (LLMPromptOverrides.ExtractNodesAndEdgesGroup)group;
            return name switch
            {
                "extract_message" => g.ExtractMessage,
                _ => throw new MissingMemberException($"'ExtractNodesAndEdges' object has no attribute '{name}'"),
            };
        },
        ["dedupe_edges"] = (group, name) =>
        {
            var g = (LLMPromptOverrides.DedupeEdgesGroup)group;
            return name switch
            {
                "resolve_edge" => g.ResolveEdge,
                _ => throw new MissingMemberException($"'DedupeEdges' object has no attribute '{name}'"),
            };
        },
        ["summarize_nodes"] = (group, name) =>
        {
            var g = (LLMPromptOverrides.SummarizeNodesGroup)group;
            return name switch
            {
                "summarize_pair" => g.SummarizePair,
                "summarize_context" => g.SummarizeContext,
                "summary_description" => g.SummaryDescription,
                _ => throw new MissingMemberException($"'SummarizeNodes' object has no attribute '{name}'"),
            };
        },
        ["summarize_sagas"] = (group, name) =>
        {
            var g = (LLMPromptOverrides.SummarizeSagasGroup)group;
            return name switch
            {
                "summarize_saga" => g.SummarizeSaga,
                _ => throw new MissingMemberException($"'SummarizeSagas' object has no attribute '{name}'"),
            };
        },
        ["eval"] = (group, name) =>
        {
            var g = (LLMPromptOverrides.EvalGroup)group;
            return name switch
            {
                "query_expansion" => g.QueryExpansion,
                "qa_prompt" => g.QaPrompt,
                "eval_prompt" => g.EvalPrompt,
                "eval_add_episode_results" => g.EvalAddEpisodeResults,
                _ => throw new MissingMemberException($"'Eval' object has no attribute '{name}'"),
            };
        },
    };

    // PORT: R15 gives a static table from the group name to the nested type for this site. Internal, because judge
    // cases call it (R2).
    internal static Type NestedGroupClass(Type parent, string groupName)
    {
        var nestedName = string.Concat(PyText.Split(groupName, "_").Select(PyText.Capitalize));
        if (parent == typeof(LLMPromptOverrides))
        {
            return nestedName switch
            {
                "ExtractNodes" => typeof(LLMPromptOverrides.ExtractNodesGroup),
                "DedupeNodes" => typeof(LLMPromptOverrides.DedupeNodesGroup),
                "ExtractEdges" => typeof(LLMPromptOverrides.ExtractEdgesGroup),
                "ExtractNodesAndEdges" => typeof(LLMPromptOverrides.ExtractNodesAndEdgesGroup),
                "DedupeEdges" => typeof(LLMPromptOverrides.DedupeEdgesGroup),
                "SummarizeNodes" => typeof(LLMPromptOverrides.SummarizeNodesGroup),
                "SummarizeSagas" => typeof(LLMPromptOverrides.SummarizeSagasGroup),
                "Eval" => typeof(LLMPromptOverrides.EvalGroup),
                _ => throw new MissingMemberException($"type object 'LLMPromptOverrides' has no attribute '{nestedName}'"),
            };
        }

        if (parent == typeof(PromptRoutes))
        {
            return nestedName switch
            {
                "ExtractNodes" => typeof(PromptRoutes.ExtractNodesGroup),
                "DedupeNodes" => typeof(PromptRoutes.DedupeNodesGroup),
                "ExtractEdges" => typeof(PromptRoutes.ExtractEdgesGroup),
                "ExtractNodesAndEdges" => typeof(PromptRoutes.ExtractNodesAndEdgesGroup),
                "DedupeEdges" => typeof(PromptRoutes.DedupeEdgesGroup),
                "SummarizeNodes" => typeof(PromptRoutes.SummarizeNodesGroup),
                "SummarizeSagas" => typeof(PromptRoutes.SummarizeSagasGroup),
                "Eval" => typeof(PromptRoutes.EvalGroup),
                _ => throw new MissingMemberException($"type object 'PromptRoutes' has no attribute '{nestedName}'"),
            };
        }

        // PORT: Python reads the attribute of any class. The callers pass LLMPromptOverrides or PromptRoutes.
        throw new MissingMemberException($"type object has no attribute '{nestedName}'");
    }

    /// <summary>Turn nested override classes into <c>group.function</c> → builder.</summary>
    internal static OrderedDictionary<string, ChatPromptFunction> FlattenOverrides(LLMPromptOverrides? overrides)
    {
        if (overrides is null)
        {
            return new();
        }

        OrderedDictionary<string, ChatPromptFunction> flat = new();
        foreach (var (groupName, getGroup) in OverrideFields)
        {
            if (!Lib.PromptGroups.ContainsKey(groupName))
            {
                continue;
            }

            var group = getGroup(overrides);
            if (group is null)
            {
                continue;
            }

            var expected = NestedGroupClass(typeof(LLMPromptOverrides), groupName);
            if (!expected.IsInstanceOfType(group))
            {
                throw new InvalidCastException(
                    $"prompt_overrides.{groupName} must be LLMPromptOverrides.{PythonNames[expected]}, "
                    + $"got {(PythonNames.TryGetValue(group.GetType(), out var groupTypeName) ? groupTypeName : Py.TypeName(group))}");
            }

            foreach (var methodName in Lib.PromptGroups[groupName])
            {
                var builder = OverrideBuilders[groupName](group, methodName);
                if (builder is null)
                {
                    continue;
                }

                // PORT: `callable(builder)` is true for each ChatPromptFunction, so the TypeError of Python has no C# form.
                flat[$"{groupName}.{methodName}"] = builder;
            }
        }

        return flat;
    }
}

// PORT: `models` is keyword-only in Python. A `frozenset` that code tests with `in` and sorts is a HashSet (R14).
internal sealed class LLMTransport
{
    internal LLMTransport(LLMClient client, IReadOnlyList<string>? models = null)
    {
        // PORT: `isinstance(client, LLMClient)` is true for each LLMClient parameter, so the TypeError of Python has no C# form.
        if (models is null)
        {
            Models = null;
        }
        else
        {
            var modelIds = models.ToArray();

            // PORT: `isinstance(model_id, str)` is true for each string of the list.
            if (modelIds.Length == 0 || modelIds.Any(modelId => PyText.Strip(modelId).Length == 0))
            {
                throw new ArgumentException("models must contain non-empty model ids");
            }

            Models = new HashSet<string>(modelIds, StringComparer.Ordinal);
        }

        Client = client;
    }

    internal HashSet<string>? Models { get; set; }

    internal LLMClient Client { get; set; }

    internal bool Supports(string modelId)
    {
        return Models is null || Models.Contains(modelId);
    }

    // PORT: `prompt_overrides` and `max_tokens` are keyword-only in Python.
    internal LLMModel Model(string id, LLMPromptOverrides? promptOverrides = null, long? maxTokens = null)
    {
        return new LLMModel
        {
            Id = id,
            Transport = this,
            PromptOverrides = promptOverrides,
            MaxTokens = maxTokens,
        }.Validate();
    }

    // PORT: Repr() for __repr__ (R2). LLMModel.__post_init__ formats the transport with `!r`.
    internal string Repr()
    {
        List<object?>? models = Models is not null ? [.. Models.Order(Py.StringComparer)] : null;
        return $"LLMTransport(client={Client.PythonName}, models={Py.Repr(models)})";
    }
}

// PORT: a frozen dataclass. Python compares and hashes it by `id`, `transport`, and `max_tokens` only
// (`compare=False` on the other two fields). A record compares each property, so this record declares Equals and
// GetHashCode with the three fields. The upstream test `test_models_compare_and_hash_by_transport_identity` compares
// two models. LLMTransport has no `__eq__`, so a transport compares by identity.
/// <summary>
/// A provider model id, plus optional per-model prompt text overrides.
/// Graphiti does not assign nicknames. Bind this object to a local variable and pass that variable into
/// <see cref="PromptRoutes"/>.
/// </summary>
internal sealed record LLMModel
{
    // PORT: the field `_flat_overrides` (`init=False`, `repr=False`). __post_init__ sets it with
    // `object.__setattr__`, and Validate() sets it here. A model that has not run Validate() has no value.
    private IReadOnlyDictionary<string, ChatPromptFunction>? flatOverrides;

    internal required string Id { get; init; }

    internal required LLMTransport Transport { get; init; }

    internal LLMPromptOverrides? PromptOverrides { get; init; }

    internal long? MaxTokens { get; init; }

    // PORT: Validate() for __post_init__ (R7). Each construction path calls it.
    internal LLMModel Validate()
    {
        if (PyText.Strip(Id).Length == 0)
        {
            throw new ArgumentException("LLMModel.id must be a non-empty string");
        }

        // PORT: `isinstance(self.transport, LLMTransport)` is true for each LLMTransport property, so the TypeError of
        // Python has no C# form.
        if (!Transport.Supports(Id))
        {
            throw new ArgumentException($"{Py.Repr(Id)} is not declared on transport {Transport.Repr()}");
        }

        if (MaxTokens is not null && MaxTokens <= 0)
        {
            throw new ArgumentException("LLMModel.max_tokens must be positive");
        }

        flatOverrides = new System.Collections.ObjectModel.ReadOnlyDictionary<string, ChatPromptFunction>(
            PromptConfig.FlattenOverrides(PromptOverrides));
        return this;
    }

    // PORT: `compare=False` on `prompt_overrides`, and the transport by identity.
    public bool Equals(LLMModel? other) =>
        other is not null && Id == other.Id && ReferenceEquals(Transport, other.Transport) && MaxTokens == other.MaxTokens;

    public override int GetHashCode() =>
        HashCode.Combine(Id, System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Transport), MaxTokens);

    internal IReadOnlyDictionary<string, ChatPromptFunction> FlatOverrides =>
        flatOverrides ?? throw new MissingMemberException("'LLMModel' object has no attribute '_flat_overrides'");
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
    // PORT: R15, `fields(routes)` and `getattr(routes, group_name)`: the fields of PromptRoutes in field order.
    private static readonly (string Name, Func<PromptRoutes, object?> Get)[] RouteFields =
    [
        ("extract_nodes", r => r.ExtractNodes),
        ("dedupe_nodes", r => r.DedupeNodes),
        ("extract_edges", r => r.ExtractEdges),
        ("extract_nodes_and_edges", r => r.ExtractNodesAndEdges),
        ("dedupe_edges", r => r.DedupeEdges),
        ("summarize_nodes", r => r.SummarizeNodes),
        ("summarize_sagas", r => r.SummarizeSagas),
        ("eval", r => r.Eval),
    ];

    // PORT: R15, `getattr(value, name)` for a group of PromptRoutes. The name is `default` or a method name of
    // PROMPT_GROUPS. A name that a group does not hold is an AttributeError.
    private static readonly Dictionary<string, Func<object, string, LLMModel?>> RouteModels = new()
    {
        ["extract_nodes"] = (group, name) =>
        {
            var g = (PromptRoutes.ExtractNodesGroup)group;
            return name switch
            {
                "default" => g.Default,
                "extract_message" => g.ExtractMessage,
                "extract_json" => g.ExtractJson,
                "extract_text" => g.ExtractText,
                "classify_nodes" => g.ClassifyNodes,
                "extract_attributes" => g.ExtractAttributes,
                "extract_summary" => g.ExtractSummary,
                "extract_summaries_batch" => g.ExtractSummariesBatch,
                "extract_entity_summaries_from_episodes" => g.ExtractEntitySummariesFromEpisodes,
                _ => throw new MissingMemberException($"'ExtractNodes' object has no attribute '{name}'"),
            };
        },
        ["dedupe_nodes"] = (group, name) =>
        {
            var g = (PromptRoutes.DedupeNodesGroup)group;
            return name switch
            {
                "default" => g.Default,
                "node" => g.Node,
                "node_list" => g.NodeList,
                "nodes" => g.Nodes,
                _ => throw new MissingMemberException($"'DedupeNodes' object has no attribute '{name}'"),
            };
        },
        ["extract_edges"] = (group, name) =>
        {
            var g = (PromptRoutes.ExtractEdgesGroup)group;
            return name switch
            {
                "default" => g.Default,
                "edge" => g.Edge,
                "extract_attributes" => g.ExtractAttributes,
                "extract_timestamps" => g.ExtractTimestamps,
                "extract_timestamps_batch" => g.ExtractTimestampsBatch,
                _ => throw new MissingMemberException($"'ExtractEdges' object has no attribute '{name}'"),
            };
        },
        ["extract_nodes_and_edges"] = (group, name) =>
        {
            var g = (PromptRoutes.ExtractNodesAndEdgesGroup)group;
            return name switch
            {
                "default" => g.Default,
                "extract_message" => g.ExtractMessage,
                _ => throw new MissingMemberException($"'ExtractNodesAndEdges' object has no attribute '{name}'"),
            };
        },
        ["dedupe_edges"] = (group, name) =>
        {
            var g = (PromptRoutes.DedupeEdgesGroup)group;
            return name switch
            {
                "default" => g.Default,
                "resolve_edge" => g.ResolveEdge,
                _ => throw new MissingMemberException($"'DedupeEdges' object has no attribute '{name}'"),
            };
        },
        ["summarize_nodes"] = (group, name) =>
        {
            var g = (PromptRoutes.SummarizeNodesGroup)group;
            return name switch
            {
                "default" => g.Default,
                "summarize_pair" => g.SummarizePair,
                "summarize_context" => g.SummarizeContext,
                "summary_description" => g.SummaryDescription,
                _ => throw new MissingMemberException($"'SummarizeNodes' object has no attribute '{name}'"),
            };
        },
        ["summarize_sagas"] = (group, name) =>
        {
            var g = (PromptRoutes.SummarizeSagasGroup)group;
            return name switch
            {
                "default" => g.Default,
                "summarize_saga" => g.SummarizeSaga,
                _ => throw new MissingMemberException($"'SummarizeSagas' object has no attribute '{name}'"),
            };
        },
        ["eval"] = (group, name) =>
        {
            var g = (PromptRoutes.EvalGroup)group;
            return name switch
            {
                "default" => g.Default,
                "query_expansion" => g.QueryExpansion,
                "qa_prompt" => g.QaPrompt,
                "eval_prompt" => g.EvalPrompt,
                "eval_add_episode_results" => g.EvalAddEpisodeResults,
                _ => throw new MissingMemberException($"'Eval' object has no attribute '{name}'"),
            };
        },
    };

    /// <summary>Turn <see cref="PromptRoutes"/> into group and <c>group.function</c> → <see cref="LLMModel"/>.</summary>
    internal static OrderedDictionary<string, LLMModel> FlattenRoutes(PromptRoutes? routes)
    {
        if (routes is null)
        {
            return new();
        }

        OrderedDictionary<string, LLMModel> flat = new();
        foreach (var (groupName, getValue) in RouteFields)
        {
            if (!Lib.PromptGroups.ContainsKey(groupName))
            {
                continue;
            }

            var value = getValue(routes);
            if (value is null)
            {
                continue;
            }

            if (value is LLMModel groupModel)
            {
                flat[groupName] = groupModel;
                continue;
            }

            var expected = NestedGroupClass(typeof(PromptRoutes), groupName);
            if (!expected.IsInstanceOfType(value))
            {
                throw new InvalidCastException(
                    $"routes.{groupName} must be LLMModel or PromptRoutes.{PythonNames[expected]}, "
                    + $"got {(PythonNames.TryGetValue(value.GetType(), out var valueTypeName) ? valueTypeName : Py.TypeName(value))}");
            }

            // PORT: `value.default` is an LLMModel? property, so `isinstance(value.default, LLMModel)` and the TypeError
            // of Python have no C# form.
            var defaultModel = RouteModels[groupName](value, "default");
            if (defaultModel is not null)
            {
                flat[groupName] = defaultModel;
            }

            foreach (var methodName in Lib.PromptGroups[groupName])
            {
                var model = RouteModels[groupName](value, methodName);
                if (model is null)
                {
                    continue;
                }

                // PORT: `isinstance(model, LLMModel)` is true for each LLMModel? property, so the TypeError of Python has no C# form.
                flat[$"{groupName}.{methodName}"] = model;
            }
        }

        return flat;
    }
}
