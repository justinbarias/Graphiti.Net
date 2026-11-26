// Ported from graphiti_core/models/edges/edge_db_queries.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core.Models.Edges;

using Graphiti.Core.Driver;
using Graphiti.PyCompat;

internal static class EdgeDbQueries
{
    internal const string EpisodicEdgeSave = "\n" + """
    MATCH (episode:Episodic {uuid: $episode_uuid})
    MATCH (node:Entity {uuid: $entity_uuid})
    MERGE (episode)-[e:MENTIONS {uuid: $uuid}]->(node)
    SET
        e.group_id = $group_id,
        e.created_at = $created_at
    RETURN e.uuid AS uuid
""" + "\n";

    internal static string GetEpisodicEdgeSaveBulkQuery(GraphProvider provider)
    {
        return "\n" + """
        UNWIND $episodic_edges AS edge
        MATCH (episode:Episodic {uuid: edge.source_node_uuid})
        MATCH (node:Entity {uuid: edge.target_node_uuid})
        MERGE (episode)-[e:MENTIONS {uuid: edge.uuid}]->(node)
        SET
            e.group_id = edge.group_id,
            e.created_at = edge.created_at
        RETURN e.uuid AS uuid
""" + "\n    ";
    }

    internal const string EpisodicEdgeReturn = "\n" + """
    e.uuid AS uuid,
    e.group_id AS group_id,
    n.uuid AS source_node_uuid,
    m.uuid AS target_node_uuid,
    e.created_at AS created_at
""" + "\n";

    internal static string GetEntityEdgeSaveQuery(GraphProvider provider, bool hasAoss = false)
    {
        switch (provider)
        {
            case GraphProvider.Falkordb:
                return "\n" + """
                MATCH (source:Entity {uuid: $edge_data.source_uuid})
                MATCH (target:Entity {uuid: $edge_data.target_uuid})
                MERGE (source)-[e:RELATES_TO {uuid: $edge_data.uuid}]->(target)
                SET e = $edge_data
                SET e.fact_embedding = vecf32($edge_data.fact_embedding)
                RETURN e.uuid AS uuid
""" + "\n            ";
            case GraphProvider.Neptune:
                return "\n" + """
                MATCH (source:Entity {uuid: $edge_data.source_uuid})
                MATCH (target:Entity {uuid: $edge_data.target_uuid})
                MERGE (source)-[e:RELATES_TO {uuid: $edge_data.uuid}]->(target)
                SET e = removeKeyFromMap(removeKeyFromMap($edge_data, "fact_embedding"), "episodes")
                SET e.fact_embedding = join([x IN coalesce($edge_data.fact_embedding, []) | toString(x) ], ",")
                SET e.episodes = join($edge_data.episodes, ",")
                RETURN $edge_data.uuid AS uuid
""" + "\n            ";
            case GraphProvider.Relate:
                return "\n" + """
                MATCH (source:Entity {uuid: $edge_data.source_uuid})
                MATCH (target:Entity {uuid: $edge_data.target_uuid})
                MERGE (source)-[e:RELATES_TO {uuid: $edge_data.uuid}]->(target)
                SET e = $edge_data
                RETURN e.uuid AS uuid
""" + "\n            ";
            default: // Neo4j
                {
                    var saveEmbeddingQuery = !hasAoss
                        ? """
WITH e CALL db.create.setRelationshipVectorProperty(e, "fact_embedding", $edge_data.fact_embedding)
"""
                        : string.Empty;
                    return ("\n" + """
                        MATCH (source:Entity {uuid: $edge_data.source_uuid})
                        MATCH (target:Entity {uuid: $edge_data.target_uuid})
                        MERGE (source)-[e:RELATES_TO {uuid: $edge_data.uuid}]->(target)
                        SET e = $edge_data
""" + "\n                        "
                        + saveEmbeddingQuery)
                        + "\n" + """
                RETURN e.uuid AS uuid
""" + "\n                ";
                }
        }
    }

    internal static string GetEntityEdgeSaveBulkQuery(GraphProvider provider, bool hasAoss = false)
    {
        switch (provider)
        {
            case GraphProvider.Falkordb:
                return "\n" + """
                UNWIND $entity_edges AS edge
                MATCH (source:Entity {uuid: edge.source_node_uuid})
                MATCH (target:Entity {uuid: edge.target_node_uuid})
                MERGE (source)-[r:RELATES_TO {uuid: edge.uuid}]->(target)
                SET r = edge
                SET r.fact_embedding = vecf32(edge.fact_embedding)
                WITH r, edge
                RETURN edge.uuid AS uuid
""" + "\n            ";
            case GraphProvider.Neptune:
                return "\n" + """
                UNWIND $entity_edges AS edge
                MATCH (source:Entity {uuid: edge.source_node_uuid})
                MATCH (target:Entity {uuid: edge.target_node_uuid})
                MERGE (source)-[r:RELATES_TO {uuid: edge.uuid}]->(target)
                SET r = removeKeyFromMap(removeKeyFromMap(edge, "fact_embedding"), "episodes")
                SET r.fact_embedding = join([x IN coalesce(edge.fact_embedding, []) | toString(x) ], ",")
                SET r.episodes = join(edge.episodes, ",")
                RETURN edge.uuid AS uuid
""" + "\n            ";
            case GraphProvider.Relate:
                return "\n" + """
                UNWIND $entity_edges AS edge
                MATCH (source:Entity {uuid: edge.source_node_uuid})
                MATCH (target:Entity {uuid: edge.target_node_uuid})
                MERGE (source)-[e:RELATES_TO {uuid: edge.uuid}]->(target)
                SET e = edge
                RETURN edge.uuid AS uuid
""" + "\n            ";
            default:
                {
                    var saveEmbeddingQuery = !hasAoss
                        ? """
WITH e, edge CALL db.create.setRelationshipVectorProperty(e, "fact_embedding", edge.fact_embedding)
"""
                        : string.Empty;
                    return "\n" + """
                    UNWIND $entity_edges AS edge
                    MATCH (source:Entity {uuid: edge.source_node_uuid})
                    MATCH (target:Entity {uuid: edge.target_node_uuid})
                    MERGE (source)-[e:RELATES_TO {uuid: edge.uuid}]->(target)
                    SET e = edge
""" + "\n                    "
                        + saveEmbeddingQuery
                        + "\n" + """
                RETURN edge.uuid AS uuid
""" + "\n            ";
                }
        }
    }

    /// <summary>
    /// Return the bulk save query of the provider, with <c>MATCH</c> in the place of <c>MERGE</c>.
    /// The query changes an edge that exists and creates nothing. Concurrent ingestion
    /// uses it to update a stored edge, so that an edge that a different writer deleted
    /// is not created again.
    /// </summary>
    internal static string GetEntityEdgeUpdateBulkQuery(GraphProvider provider, bool hasAoss = false)
    {
        var query = GetEntityEdgeSaveBulkQuery(provider, hasAoss);
        if (PyText.Count(query, "MERGE (source)-[") != 1)
        {
            throw new ArgumentException($"no update query for the entity edge save query of {Py.Str(provider)}");
        }

        return query.Replace("MERGE (source)-[", "MATCH (source)-[", StringComparison.Ordinal);
    }

    internal static string GetEntityEdgeReturnQuery(GraphProvider provider)
    {
        // `fact_embedding` is not returned by default and must be manually loaded using `load_fact_embedding()`.
        // Direction semantics: NEO4J/FALKORDB/NEPTUNE read source/target from the relationship itself
        // via Cypher's built-in `startNode(e)`/`endNode(e)`, so the returned direction matches the
        // stored relationship regardless of whether the surrounding MATCH is directed or undirected.
        if (provider == GraphProvider.Neptune)
        {
            return "\n" + """
        e.uuid AS uuid,
        startNode(e).uuid AS source_node_uuid,
        endNode(e).uuid AS target_node_uuid,
        e.group_id AS group_id,
        e.name AS name,
        e.fact AS fact,
        split(e.episodes, ',') AS episodes,
        e.created_at AS created_at,
        e.expired_at AS expired_at,
        e.valid_at AS valid_at,
        e.invalid_at AS invalid_at,
        e.reference_time AS reference_time,
        properties(e) AS attributes
""" + "\n    ";
        }

        return "\n" + """
        e.uuid AS uuid,
        startNode(e).uuid AS source_node_uuid,
        endNode(e).uuid AS target_node_uuid,
        e.group_id AS group_id,
        e.created_at AS created_at,
        e.name AS name,
        e.fact AS fact,
        e.episodes AS episodes,
        e.expired_at AS expired_at,
        e.valid_at AS valid_at,
        e.invalid_at AS invalid_at,
        e.reference_time AS reference_time,
        properties(e) AS attributes
""" + "\n    ";
    }

    internal static string GetCommunityEdgeSaveQuery(GraphProvider provider)
    {
        switch (provider)
        {
            case GraphProvider.Falkordb:
                return "\n" + """
                MATCH (community:Community {uuid: $community_uuid})
                MATCH (node {uuid: $entity_uuid})
                MERGE (community)-[e:HAS_MEMBER {uuid: $uuid}]->(node)
                SET e = {uuid: $uuid, group_id: $group_id, created_at: $created_at}
                RETURN e.uuid AS uuid
""" + "\n            ";
            case GraphProvider.Neptune:
                return "\n" + """
                MATCH (community:Community {uuid: $community_uuid})
                MATCH (node {uuid: $entity_uuid})
                WHERE node:Entity OR node:Community
                MERGE (community)-[r:HAS_MEMBER {uuid: $uuid}]->(node)
                SET r.uuid= $uuid
                SET r.group_id= $group_id
                SET r.created_at= $created_at
                RETURN r.uuid AS uuid
""" + "\n            ";
            default: // Neo4j
                return "\n" + """
                MATCH (community:Community {uuid: $community_uuid})
                MATCH (node:Entity | Community {uuid: $entity_uuid})
                MERGE (community)-[e:HAS_MEMBER {uuid: $uuid}]->(node)
                SET e = {uuid: $uuid, group_id: $group_id, created_at: $created_at}
                RETURN e.uuid AS uuid
""" + "\n            ";
        }
    }

    internal const string CommunityEdgeReturn = "\n" + """
    e.uuid AS uuid,
    e.group_id AS group_id,
    n.uuid AS source_node_uuid,
    m.uuid AS target_node_uuid,
    e.created_at AS created_at
""" + "\n";

    internal const string HasEpisodeEdgeSave = "\n" + """
    MATCH (saga:Saga {uuid: $saga_uuid, group_id: $group_id})
    MATCH (episode:Episodic {uuid: $episode_uuid, group_id: $group_id})
    MERGE (saga)-[e:HAS_EPISODE {uuid: $uuid}]->(episode)
    SET
        e.group_id = $group_id,
        e.created_at = $created_at
    RETURN e.uuid AS uuid
""" + "\n";

    internal const string HasEpisodeEdgeReturn = "\n" + """
    e.uuid AS uuid,
    e.group_id AS group_id,
    n.uuid AS source_node_uuid,
    m.uuid AS target_node_uuid,
    e.created_at AS created_at
""" + "\n";

    internal const string NextEpisodeEdgeSave = "\n" + """
    MATCH (source_episode:Episodic {uuid: $source_episode_uuid, group_id: $group_id})
    MATCH (target_episode:Episodic {uuid: $target_episode_uuid, group_id: $group_id})
    MERGE (source_episode)-[e:NEXT_EPISODE {uuid: $uuid}]->(target_episode)
    SET
        e.group_id = $group_id,
        e.created_at = $created_at
    RETURN e.uuid AS uuid
""" + "\n";

    internal const string NextEpisodeEdgeReturn = "\n" + """
    e.uuid AS uuid,
    e.group_id AS group_id,
    n.uuid AS source_node_uuid,
    m.uuid AS target_node_uuid,
    e.created_at AS created_at
""" + "\n";
}
