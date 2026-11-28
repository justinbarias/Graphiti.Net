namespace Graphiti.JudgeHost.Units;

/// <summary>
/// The bindings of <c>graphiti_core/ingestion/graph_reader.py</c>. Each method of the module reads the graph, so the
/// module has no pure judge case and binds nothing. The flow layer and the recording of the unit cover it.
/// </summary>
internal static class GraphReaderBindings
{
    internal static void Register(BindingTable table)
    {
        _ = table;
    }
}
