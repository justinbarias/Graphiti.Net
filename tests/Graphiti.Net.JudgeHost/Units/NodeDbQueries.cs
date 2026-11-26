namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Models.Nodes;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/models/nodes/node_db_queries.py</c>.</summary>
internal static class NodeDbQueriesBindings
{
    private const string Module = "graphiti_core.models.nodes.node_db_queries";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":get_episode_node_save_query", call => call.Signature("provider").InvokeAsync(NodeDbQueries.GetEpisodeNodeSaveQuery));
        table.Pure.Add(Module + ":get_episode_node_save_bulk_query", call => call.Signature("provider").InvokeAsync(NodeDbQueries.GetEpisodeNodeSaveBulkQuery));
        table.Pure.Add(Module + ":get_entity_node_save_query", call => call.Signature("provider", "labels", "has_aoss").InvokeAsync(NodeDbQueries.GetEntityNodeSaveQuery));
        table.Pure.Add(Module + ":get_entity_node_save_bulk_query", call => call.Signature("provider", "nodes", "has_aoss").InvokeAsync(NodeDbQueries.GetEntityNodeSaveBulkQuery));
        table.Pure.Add(Module + ":get_entity_node_update_bulk_query", call => call.Signature("provider", "nodes", "has_aoss").InvokeAsync(NodeDbQueries.GetEntityNodeUpdateBulkQuery));
        table.Pure.Add(Module + ":get_entity_node_return_query", call => call.Signature("provider").InvokeAsync(NodeDbQueries.GetEntityNodeReturnQuery));
        table.Pure.Add(Module + ":get_community_node_save_query", call => call.Signature("provider").InvokeAsync(NodeDbQueries.GetCommunityNodeSaveQuery));
        table.Pure.Add(Module + ":get_saga_node_save_query", call => call.Signature("provider").InvokeAsync(NodeDbQueries.GetSagaNodeSaveQuery));
    }
}
