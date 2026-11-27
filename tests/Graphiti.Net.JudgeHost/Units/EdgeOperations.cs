namespace Graphiti.JudgeHost.Units;

using Graphiti.Core;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The bindings of <c>graphiti_core/utils/maintenance/edge_operations.py</c>.</summary>
internal static class EdgeOperationsBindings
{
    private const string Module = "graphiti_core.utils.maintenance.edge_operations";

    internal static void Register(BindingTable table)
    {
        table.Registry
            .AddType<EdgeDecision>("EdgeDecision", m =>
            [
                new("exact_match", m.ExactMatch),
                new("duplicates", m.Duplicates),
                new("contradicted", m.Contradicted),
                new("started_at", m.StartedAt),
            ]);

        // `episode_uuid: str | list[str]` is two overloads. The binding picks one by the runtime type of the argument
        // (R22).
        table.Pure.Add(Module + ":build_episodic_edges", call =>
        {
            var p = call.Signature("entity_nodes", "episode_uuid", "created_at", "node_episode_index_map");
            return p.Value("episode_uuid") is string
                ? p.InvokeAsync((Func<List<EntityNode>, string, PyDateTime, OrderedDictionary<string, List<long>>?, List<EpisodicEdge>>)EdgeOperations.BuildEpisodicEdges)
                : p.InvokeAsync((Func<List<EntityNode>, List<string>, PyDateTime, OrderedDictionary<string, List<long>>?, List<EpisodicEdge>>)EdgeOperations.BuildEpisodicEdges);
        });
        table.Pure.Add(
            Module + ":build_community_edges",
            call => call.Signature("entity_nodes", "community_node", "created_at").InvokeAsync(EdgeOperations.BuildCommunityEdges));
        table.Pure.Add(
            Module + ":_pair_search_filter",
            call => call.Signature("edge").InvokeAsync(EdgeOperations.PairSearchFilter));
        table.Pure.Add(
            Module + ":_same_pair_edges",
            call => call.Signature("extracted_edge", "candidates").InvokeAsync(EdgeOperations.SamePairEdges));
        table.Pure.Add(
            Module + ":find_exact_duplicate_edge",
            call => call.Signature("edge", "seen").InvokeAsync(EdgeOperations.FindExactDuplicateEdge));
        table.Pure.Add(
            Module + ":_edge_types_for_endpoints",
            call => call.Signature("extracted_edge", "uuid_entity_map", "edge_types", "edge_type_map").InvokeAsync(EdgeOperations.EdgeTypesForEndpoints));
        table.Pure.Add(
            Module + ":enforce_edge_type_signatures",
            call => call.Signature("extracted_edges", "edge_types_lst", "edge_type_map", "strict_edge_types").InvokeAsync(EdgeOperations.EnforceEdgeTypeSignatures));
        table.Pure.Add(
            Module + ":_apply_temporal_invalidation",
            call => call.Signature("resolved_edge", "invalidation_candidates").InvokeAsync(EdgeOperations.ApplyTemporalInvalidation));
        table.Pure.Add(
            Module + ":resolve_edge_contradictions",
            call => call.Signature("resolved_edge", "invalidation_candidates").InvokeAsync(EdgeOperations.ResolveEdgeContradictions));

        // A dataclass constructor: each field that the call gives replaces its default.
        table.Pure.Add(Module + ":EdgeDecision", call =>
        {
            var p = call.Signature("exact_match", "duplicates", "contradicted", "started_at");
            var decision = new EdgeDecision();
            if (p.Has("exact_match"))
            {
                decision.ExactMatch = p.GetOrNone<EntityEdge>("exact_match");
            }

            if (p.Has("duplicates"))
            {
                decision.Duplicates = p.List<EntityEdge>("duplicates");
            }

            if (p.Has("contradicted"))
            {
                decision.Contradicted = p.List<EntityEdge>("contradicted");
            }

            if (p.Has("started_at"))
            {
                decision.StartedAt = p.Double("started_at");
            }

            return Codec.Result(decision);
        });
    }
}
