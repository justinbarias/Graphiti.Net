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

    // TODO(port): get_episodic_edge_save_bulk_query
    internal static string GetEpisodicEdgeSaveBulkQuery(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_episodic_edge_save_bulk_query");
    }

    internal const string EpisodicEdgeReturn = "\n" + """
    e.uuid AS uuid,
    e.group_id AS group_id,
    n.uuid AS source_node_uuid,
    m.uuid AS target_node_uuid,
    e.created_at AS created_at
""" + "\n";

    // TODO(port): get_entity_edge_save_query
    internal static string GetEntityEdgeSaveQuery(GraphProvider provider, bool hasAoss = false)
    {
        throw new NotSupportedException("TODO(port): get_entity_edge_save_query");
    }

    // TODO(port): get_entity_edge_save_bulk_query
    internal static string GetEntityEdgeSaveBulkQuery(GraphProvider provider, bool hasAoss = false)
    {
        throw new NotSupportedException("TODO(port): get_entity_edge_save_bulk_query");
    }

    // TODO(port): get_entity_edge_update_bulk_query
    internal static string GetEntityEdgeUpdateBulkQuery(GraphProvider provider, bool hasAoss = false)
    {
        throw new NotSupportedException("TODO(port): get_entity_edge_update_bulk_query");
    }

    // TODO(port): get_entity_edge_return_query
    internal static string GetEntityEdgeReturnQuery(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_entity_edge_return_query");
    }

    // TODO(port): get_community_edge_save_query
    internal static string GetCommunityEdgeSaveQuery(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_community_edge_save_query");
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
