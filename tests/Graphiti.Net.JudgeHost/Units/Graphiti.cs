namespace Graphiti.JudgeHost.Units;

using Graphiti.Core;
using Graphiti.JudgeHost.Core;

/// <summary>
/// The bindings of <c>graphiti_core/graphiti.py</c>. Only the schemas of the three result models are bound. The pure
/// cases name no other member, and the flow cases drive the class through the facade.
/// </summary>
internal static class GraphitiBindings
{
    private const string Module = "graphiti_core.graphiti";

    internal static void Register(BindingTable table)
    {
        table.Schemas.Add(Module + ":AddEpisodeResults", AddEpisodeResults.ModelJsonSchema);
        table.Schemas.Add(Module + ":AddBulkEpisodeResults", AddBulkEpisodeResults.ModelJsonSchema);
        table.Schemas.Add(Module + ":AddTripletResults", AddTripletResults.ModelJsonSchema);
    }
}
