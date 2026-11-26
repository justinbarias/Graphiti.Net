// Ported from graphiti_core/graph_queries.py

namespace Graphiti.Core;

using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Falkordb;
using Graphiti.PyCompat;

/// <summary>
/// Database query utilities for different graph database backends.
///
/// This module provides database-agnostic query generation for Neo4j and FalkorDB,
/// supporting index creation, fulltext search, and bulk operations.
/// </summary>
internal static class GraphQueries
{
    // Mapping from Neo4j fulltext index names to FalkorDB node labels
    internal static readonly Dictionary<string, string> Neo4jToFalkordbMapping = new()
    {
        ["node_name_and_summary"] = "Entity",
        ["community_name"] = "Community",
        ["episode_content"] = "Episodic",
        ["edge_name_and_fact"] = "RELATES_TO",
    };

    // PORT: LiteralString is str (Part 3).
    internal static List<string> GetRangeIndices(GraphProvider provider)
    {
        if (provider == GraphProvider.Falkordb)
        {
            return
            [
                // Entity node
                "CREATE INDEX FOR (n:Entity) ON (n.uuid, n.group_id, n.name, n.created_at)",
                // Episodic node
                "CREATE INDEX FOR (n:Episodic) ON (n.uuid, n.group_id, n.created_at, n.valid_at)",
                // Community node
                "CREATE INDEX FOR (n:Community) ON (n.uuid)",
                // Saga node
                "CREATE INDEX FOR (n:Saga) ON (n.uuid, n.group_id, n.name)",
                // RELATES_TO edge
                "CREATE INDEX FOR ()-[e:RELATES_TO]-() ON (e.uuid, e.group_id, e.name, e.created_at, e.expired_at, e.valid_at, e.invalid_at)",
                // MENTIONS edge
                "CREATE INDEX FOR ()-[e:MENTIONS]-() ON (e.uuid, e.group_id)",
                // HAS_MEMBER edge
                "CREATE INDEX FOR ()-[e:HAS_MEMBER]-() ON (e.uuid)",
                // HAS_EPISODE edge
                "CREATE INDEX FOR ()-[e:HAS_EPISODE]-() ON (e.uuid, e.group_id)",
                // NEXT_EPISODE edge
                "CREATE INDEX FOR ()-[e:NEXT_EPISODE]-() ON (e.uuid, e.group_id)",
            ];
        }

        return
        [
            "CREATE INDEX entity_uuid IF NOT EXISTS FOR (n:Entity) ON (n.uuid)",
            "CREATE INDEX episode_uuid IF NOT EXISTS FOR (n:Episodic) ON (n.uuid)",
            "CREATE INDEX community_uuid IF NOT EXISTS FOR (n:Community) ON (n.uuid)",
            "CREATE INDEX saga_uuid IF NOT EXISTS FOR (n:Saga) ON (n.uuid)",
            "CREATE INDEX relation_uuid IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.uuid)",
            "CREATE INDEX mention_uuid IF NOT EXISTS FOR ()-[e:MENTIONS]-() ON (e.uuid)",
            "CREATE INDEX has_member_uuid IF NOT EXISTS FOR ()-[e:HAS_MEMBER]-() ON (e.uuid)",
            "CREATE INDEX has_episode_uuid IF NOT EXISTS FOR ()-[e:HAS_EPISODE]-() ON (e.uuid)",
            "CREATE INDEX next_episode_uuid IF NOT EXISTS FOR ()-[e:NEXT_EPISODE]-() ON (e.uuid)",
            "CREATE INDEX entity_group_id IF NOT EXISTS FOR (n:Entity) ON (n.group_id)",
            "CREATE INDEX episode_group_id IF NOT EXISTS FOR (n:Episodic) ON (n.group_id)",
            "CREATE INDEX community_group_id IF NOT EXISTS FOR (n:Community) ON (n.group_id)",
            "CREATE INDEX saga_group_id IF NOT EXISTS FOR (n:Saga) ON (n.group_id)",
            "CREATE INDEX relation_group_id IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.group_id)",
            "CREATE INDEX mention_group_id IF NOT EXISTS FOR ()-[e:MENTIONS]-() ON (e.group_id)",
            "CREATE INDEX has_episode_group_id IF NOT EXISTS FOR ()-[e:HAS_EPISODE]-() ON (e.group_id)",
            "CREATE INDEX next_episode_group_id IF NOT EXISTS FOR ()-[e:NEXT_EPISODE]-() ON (e.group_id)",
            "CREATE INDEX name_entity_index IF NOT EXISTS FOR (n:Entity) ON (n.name)",
            "CREATE INDEX saga_name IF NOT EXISTS FOR (n:Saga) ON (n.name)",
            "CREATE INDEX created_at_entity_index IF NOT EXISTS FOR (n:Entity) ON (n.created_at)",
            "CREATE INDEX created_at_episodic_index IF NOT EXISTS FOR (n:Episodic) ON (n.created_at)",
            "CREATE INDEX valid_at_episodic_index IF NOT EXISTS FOR (n:Episodic) ON (n.valid_at)",
            "CREATE INDEX name_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.name)",
            "CREATE INDEX created_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.created_at)",
            "CREATE INDEX expired_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.expired_at)",
            "CREATE INDEX valid_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.valid_at)",
            "CREATE INDEX invalid_at_edge_index IF NOT EXISTS FOR ()-[e:RELATES_TO]-() ON (e.invalid_at)",
        ];
    }

    internal static List<string> GetFulltextIndices(GraphProvider provider)
    {
        if (provider == GraphProvider.Falkordb)
        {
            // Convert to string representation for embedding in queries
            var stopwordsStr = Py.Str(FalkordbPackage.Stopwords);

            // Use type: ignore to satisfy LiteralString requirement while maintaining single source of truth
            // PORT: the comment speaks of the Python `cast` to list[LiteralString], which C# does not need (LiteralString is str).
            return
            [
                $$"""
CREATE FULLTEXT INDEX FOR (n:Episodic)
                ON (n.content, n.source, n.source_description, n.group_id)
                OPTIONS {stopwords: {{stopwordsStr}}}
""",
                $$"""
CREATE FULLTEXT INDEX FOR (n:Entity)
                ON (n.name, n.summary, n.group_id)
                OPTIONS {stopwords: {{stopwordsStr}}}
""",
                $$"""
CREATE FULLTEXT INDEX FOR (n:Community)
                ON (n.name, n.group_id)
                OPTIONS {stopwords: {{stopwordsStr}}}
""",
                """
CREATE FULLTEXT INDEX FOR ()-[e:RELATES_TO]-() ON (e.name, e.fact, e.group_id)
""",
            ];
        }

        return
        [
            """
CREATE FULLTEXT INDEX episode_content IF NOT EXISTS
        FOR (e:Episodic) ON EACH [e.content, e.source, e.source_description, e.group_id]
""",
            """
CREATE FULLTEXT INDEX node_name_and_summary IF NOT EXISTS
        FOR (n:Entity) ON EACH [n.name, n.summary, n.group_id]
""",
            """
CREATE FULLTEXT INDEX community_name IF NOT EXISTS
        FOR (n:Community) ON EACH [n.name, n.group_id]
""",
            """
CREATE FULLTEXT INDEX edge_name_and_fact IF NOT EXISTS
        FOR ()-[e:RELATES_TO]-() ON EACH [e.name, e.fact, e.group_id]
""",
        ];
    }

    internal static string GetNodesQuery(string name, string query, long limit, GraphProvider provider)
    {
        if (provider == GraphProvider.Falkordb)
        {
            var label = Neo4jToFalkordbMapping[name];
            return $"CALL db.idx.fulltext.queryNodes('{label}', {query})";
        }

        if (provider == GraphProvider.Relate)
        {
            return $"CALL db.index.fulltext.queryNodes(\"{name}\", {query})\n";
        }

        return $$"""CALL db.index.fulltext.queryNodes("{{name}}", {{query}}, {limit: $limit})""";
    }

    // PORT: TYPE_HINTS.md gives Unknown for vec1 and vec2. Each caller passes Cypher text, so they are strings (R20).
    internal static string GetVectorCosineFuncQuery(string vec1, string vec2, GraphProvider provider)
    {
        if (provider == GraphProvider.Falkordb)
        {
            // FalkorDB uses a different syntax for regular cosine similarity and Neo4j uses normalized cosine similarity
            return $"(2 - vec.cosineDistance({vec1}, vecf32({vec2})))/2";
        }

        return $"vector.similarity.cosine({vec1}, {vec2})";
    }

    internal static string GetRelationshipsQuery(string name, long limit, GraphProvider provider)
    {
        if (provider == GraphProvider.Falkordb)
        {
            var label = Neo4jToFalkordbMapping[name];
            return $"CALL db.idx.fulltext.queryRelationships('{label}', $query)";
        }

        if (provider == GraphProvider.Relate)
        {
            return $"CALL db.index.fulltext.queryRelationships(\"{name}\", $query)\n";
        }

        return $$"""CALL db.index.fulltext.queryRelationships("{{name}}", $query, {limit: $limit})""";
    }
}
