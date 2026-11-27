namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Utils.Maintenance;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/maintenance/dangling_endpoints.py</c>.</summary>
internal static class DanglingEndpointsBindings
{
    private const string Module = "graphiti_core.utils.maintenance.dangling_endpoints";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(
            Module + ":materialize_dangling_endpoint_node",
            call => call.Signature("name", "group_id", "name_to_node", "nodes").InvokeAsync(DanglingEndpoints.MaterializeDanglingEndpointNode));
        table.Pure.Add(
            Module + ":prune_unreferenced_materialized_nodes",
            call => call.Signature("nodes", "initial_uuids", "referenced_uuids").InvokeAsync(DanglingEndpoints.PruneUnreferencedMaterializedNodes));
    }
}
