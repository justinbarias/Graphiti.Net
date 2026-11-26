// Ported from graphiti_core/graph_queries.py

namespace Graphiti.Core;

using Graphiti.Core.Driver;

internal static class GraphQueries
{
    // Mapping from Neo4j fulltext index names to FalkorDB node labels
    internal static readonly OrderedDictionary<string, string> Neo4jToFalkordbMapping = new()
    {
        ["node_name_and_summary"] = "Entity",
        ["community_name"] = "Community",
        ["episode_content"] = "Episodic",
        ["edge_name_and_fact"] = "RELATES_TO",
    };

    // PORT: LiteralString is str (Part 3).
    // TODO(port): get_range_indices
    internal static List<string> GetRangeIndices(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_range_indices");
    }

    // PORT: the FalkorDB branch imports STOPWORDS from driver/falkordb, a module that is out of the MVP (decision D10).
    // TODO(port): get_fulltext_indices
    internal static List<string> GetFulltextIndices(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_fulltext_indices");
    }

    // TODO(port): get_nodes_query
    internal static string GetNodesQuery(string name, string query, long limit, GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_nodes_query");
    }

    // PORT: TYPE_HINTS.md gives Unknown for vec1 and vec2. Each caller passes Cypher text, so they are strings (R20).
    // TODO(port): get_vector_cosine_func_query
    internal static string GetVectorCosineFuncQuery(string vec1, string vec2, GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_vector_cosine_func_query");
    }

    // TODO(port): get_relationships_query
    internal static string GetRelationshipsQuery(string name, long limit, GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_relationships_query");
    }
}
