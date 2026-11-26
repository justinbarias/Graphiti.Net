namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Operations;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/driver/driver.py</c>, <c>record_parsers.py</c>, and <c>operations/graph_utils.py</c>.</summary>
internal static class DriverBindings
{
    internal static void Register(BindingTable table)
    {
        table.Registry.AddEnum<GraphProvider>("graphiti_core.driver.driver:GraphProvider");

        table.Pure.Add("graphiti_core.driver.record_parsers:entity_node_from_record", call => call.Signature("record").InvokeAsync(RecordParsers.EntityNodeFromRecord));
        table.Pure.Add("graphiti_core.driver.record_parsers:entity_edge_from_record", call => call.Signature("record").InvokeAsync(RecordParsers.EntityEdgeFromRecord));
        table.Pure.Add("graphiti_core.driver.record_parsers:episodic_node_from_record", call => call.Signature("record").InvokeAsync(RecordParsers.EpisodicNodeFromRecord));
        table.Pure.Add("graphiti_core.driver.record_parsers:community_node_from_record", call => call.Signature("record").InvokeAsync(RecordParsers.CommunityNodeFromRecord));

        table.Pure.Add("graphiti_core.driver.operations.graph_utils:label_propagation", call => call.Signature("projection").InvokeAsync(GraphUtils.LabelPropagation));
        table.Registry.AddModel("graphiti_core.driver.operations.graph_utils:Neighbor", Neighbor.ModelValidate);
        table.Schemas.Add("graphiti_core.driver.operations.graph_utils:Neighbor", Neighbor.ModelJsonSchema);
    }
}
