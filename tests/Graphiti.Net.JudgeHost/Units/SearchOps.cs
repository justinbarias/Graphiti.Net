namespace Graphiti.JudgeHost.Units;

using Graphiti.JudgeHost.Core;
using Graphiti.Neo4j.Operations;

/// <summary>The bindings of <c>graphiti_core/driver/neo4j/operations/search_ops.py</c>.</summary>
internal static class SearchOpsBindings
{
    private const string Module = "graphiti_core.driver.neo4j.operations.search_ops";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":_build_neo4j_fulltext_query", call => call.Signature("query", "group_ids", "max_query_length").InvokeAsync(SearchOps.BuildNeo4jFulltextQuery));
    }
}
