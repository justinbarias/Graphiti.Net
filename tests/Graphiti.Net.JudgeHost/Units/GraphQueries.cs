namespace Graphiti.JudgeHost.Units;

using Graphiti.Core;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/graph_queries.py</c>.</summary>
internal static class GraphQueriesBindings
{
    private const string Module = "graphiti_core.graph_queries";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":get_range_indices", call => call.Signature("provider").InvokeAsync(GraphQueries.GetRangeIndices));
        table.Pure.Add(Module + ":get_fulltext_indices", call => call.Signature("provider").InvokeAsync(GraphQueries.GetFulltextIndices));
        table.Pure.Add(Module + ":get_nodes_query", call => call.Signature("name", "query", "limit", "provider").InvokeAsync(GraphQueries.GetNodesQuery));
        table.Pure.Add(Module + ":get_vector_cosine_func_query", call => call.Signature("vec1", "vec2", "provider").InvokeAsync(GraphQueries.GetVectorCosineFuncQuery));
        table.Pure.Add(Module + ":get_relationships_query", call => call.Signature("name", "limit", "provider").InvokeAsync(GraphQueries.GetRelationshipsQuery));
    }
}
