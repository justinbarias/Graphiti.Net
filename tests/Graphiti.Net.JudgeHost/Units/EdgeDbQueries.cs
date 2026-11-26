namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Models.Edges;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/models/edges/edge_db_queries.py</c>.</summary>
internal static class EdgeDbQueriesBindings
{
    private const string Module = "graphiti_core.models.edges.edge_db_queries";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":get_episodic_edge_save_bulk_query", call => call.Signature("provider").InvokeAsync(EdgeDbQueries.GetEpisodicEdgeSaveBulkQuery));
        table.Pure.Add(Module + ":get_entity_edge_save_query", call => call.Signature("provider", "has_aoss").InvokeAsync(EdgeDbQueries.GetEntityEdgeSaveQuery));
        table.Pure.Add(Module + ":get_entity_edge_save_bulk_query", call => call.Signature("provider", "has_aoss").InvokeAsync(EdgeDbQueries.GetEntityEdgeSaveBulkQuery));
        table.Pure.Add(Module + ":get_entity_edge_update_bulk_query", call => call.Signature("provider", "has_aoss").InvokeAsync(EdgeDbQueries.GetEntityEdgeUpdateBulkQuery));
        table.Pure.Add(Module + ":get_entity_edge_return_query", call => call.Signature("provider").InvokeAsync(EdgeDbQueries.GetEntityEdgeReturnQuery));
        table.Pure.Add(Module + ":get_community_edge_save_query", call => call.Signature("provider").InvokeAsync(EdgeDbQueries.GetCommunityEdgeSaveQuery));
    }
}
