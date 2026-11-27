namespace Graphiti.JudgeHost.Units;

using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/maintenance/node_operations.py</c>.</summary>
internal static class NodeOperationsBindings
{
    private const string Module = "graphiti_core.utils.maintenance.node_operations";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":_build_entity_types_context", call => call.Signature("entity_types").InvokeAsync(NodeOperations.BuildEntityTypesContext));
        table.Pure.Add(
            Module + ":_filter_entity_types_context",
            call => call.Signature("entity_types_context", "excluded_entity_types").InvokeAsync(NodeOperations.FilterEntityTypesContext));
        table.Pure.Add(Module + ":_get_entity_type_description", call => call.Signature("labels", "entity_types").InvokeAsync(NodeOperations.GetEntityTypeDescription));
        table.Pure.Add(Module + ":_truncate_type_description", call => call.Signature("docstring").InvokeAsync(NodeOperations.TruncateTypeDescription));
        table.Pure.Add(Module + ":_find_sentence_end", call => call.Signature("text").InvokeAsync(NodeOperations.FindSentenceEnd));
        table.Pure.Add(
            Module + ":_create_entity_nodes",
            call => call.Signature("extracted_entities", "entity_types_context", "excluded_entity_types", "episodes").InvokeAsync(NodeOperations.CreateEntityNodes));
        table.Pure.Add(
            Module + ":_collapse_exact_duplicate_extracted_nodes",
            call => call.Signature("extracted_nodes", "node_episode_index_map").InvokeAsync(NodeOperations.CollapseExactDuplicateExtractedNodes));
        table.Pure.Add(Module + ":_merge_candidate_nodes", call => call.Signature("candidate_nodes", "existing_nodes_override").InvokeAsync(NodeOperations.MergeCandidateNodes));
        table.Pure.Add(Module + ":_build_edges_by_node", call => call.Signature("edges").InvokeAsync(NodeOperations.BuildEdgesByNode));
        table.Pure.Add(Module + ":_entity_type_for_node", call => call.Signature("node", "entity_types").InvokeAsync(NodeOperations.EntityTypeForNode));
        table.Pure.Add(Module + ":_build_episode_context", BuildEpisodeContext);
    }

    // The union `EpisodicNode | list[EpisodicNode] | None` is built from the decoded argument by its runtime type (R22).
    private static ValueTask<JsonNode?> BuildEpisodeContext(JudgeArguments call)
    {
        var p = call.Signature("node_data", "episode", "previous_episodes");
        EpisodeOrEpisodes? episode = p.Value("episode") switch
        {
            null => null,
            EpisodicNode one => new EpisodeOrEpisodes.One { Episode = one },
            List<object?> => new EpisodeOrEpisodes.Many { Episodes = p.List<EpisodicNode>("episode") },
            var other => throw new JudgeTypeRejectedException($"argument 'episode' cannot be {other.GetType().Name}"),
        };
        return Codec.Result(NodeOperations.BuildEpisodeContext(
            p.Dict<object?>("node_data"),
            episode,
            p.NullableList<EpisodicNode>("previous_episodes")));
    }
}
