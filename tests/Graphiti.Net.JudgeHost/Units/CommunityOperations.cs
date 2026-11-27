namespace Graphiti.JudgeHost.Units;

using System.Text.Json.Nodes;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/maintenance/community_operations.py</c>.</summary>
internal static class CommunityOperationsBindings
{
    private const string Module = "graphiti_core.utils.maintenance.community_operations";

    internal static void Register(BindingTable table)
    {
        // The Neighbor of this module is another class than the Neighbor of driver/operations/graph_utils.py.
        table.Registry
            .AddModel(Module + ":Neighbor", Neighbor.ModelValidate)
            .AddType<Neighbor>("Neighbor", m => [new("node_uuid", m.NodeUuid), new("edge_count", m.EdgeCount)]);
        table.Schemas.Add(Module + ":Neighbor", Neighbor.ModelJsonSchema);

        table.Pure.Add(Module + ":label_propagation", call => call.Signature("projection").InvokeAsync(CommunityOperations.LabelPropagation));
        table.Pure.Add(Module + ":Neighbor", call => Construct(call, Neighbor.ModelValidate));
    }

    // M(**kwargs) is M.ModelValidate(data) (R7). A pydantic model takes no positional argument, which is a TypeError.
    private static ValueTask<JsonNode?> Construct<T>(JudgeArguments call, Func<OrderedDictionary<string, object?>, T> modelValidate) =>
        call.Positional.Count == 0
            ? Codec.Result(modelValidate(call.Keywords))
            : throw new JudgeTypeRejectedException($"__init__() takes 1 positional argument but {call.Positional.Count + 1} were given");
}
