// Ported from graphiti_core/models/nodes/node_db_queries.py
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

namespace Graphiti.Core.Models.Nodes;

using Graphiti.Core.Driver;

internal static class NodeDbQueries
{
    // PORT: the parameter is `str | list[str]`, a union of two shapes, so it is object (RULES.md Part 3).
    // TODO(port): _validate_entity_labels
    private static List<string> ValidateEntityLabels(object? labels)
    {
        throw new NotSupportedException("TODO(port): _validate_entity_labels");
    }

    // TODO(port): get_episode_node_save_query
    internal static string GetEpisodeNodeSaveQuery(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_episode_node_save_query");
    }

    // TODO(port): get_episode_node_save_bulk_query
    internal static string GetEpisodeNodeSaveBulkQuery(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_episode_node_save_bulk_query");
    }

    internal const string EpisodicNodeReturn = "\n" + """
    e.uuid AS uuid,
    e.name AS name,
    e.group_id AS group_id,
    e.created_at AS created_at,
    e.source AS source,
    e.source_description AS source_description,
    e.content AS content,
    e.valid_at AS valid_at,
    e.entity_edges AS entity_edges
""" + "\n";

    internal const string EpisodicNodeReturnNeptune = "\n" + """
    e.content AS content,
    e.created_at AS created_at,
    e.valid_at AS valid_at,
    e.uuid AS uuid,
    e.name AS name,
    e.group_id AS group_id,
    e.source_description AS source_description,
    e.source AS source,
    split(e.entity_edges, ",") AS entity_edges
""" + "\n";

    // TODO(port): get_entity_node_save_query
    internal static string GetEntityNodeSaveQuery(GraphProvider provider, string labels, bool hasAoss = false)
    {
        throw new NotSupportedException("TODO(port): get_entity_node_save_query");
    }

    // TODO(port): get_entity_node_save_bulk_query
    // PORT: the Python return type is `str | Any`, so it is object? (R6). The Neo4j branch returns a string.
    internal static object? GetEntityNodeSaveBulkQuery(GraphProvider provider, List<OrderedDictionary<string, object?>> nodes, bool hasAoss = false)
    {
        throw new NotSupportedException("TODO(port): get_entity_node_save_bulk_query");
    }

    // PORT: the Python return type is `str | Any`, so it is object? (R6), as for get_entity_node_save_bulk_query.
    // TODO(port): get_entity_node_update_bulk_query
    internal static object? GetEntityNodeUpdateBulkQuery(GraphProvider provider, List<OrderedDictionary<string, object?>> nodes, bool hasAoss = false)
    {
        throw new NotSupportedException("TODO(port): get_entity_node_update_bulk_query");
    }

    // TODO(port): get_entity_node_return_query
    internal static string GetEntityNodeReturnQuery(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_entity_node_return_query");
    }

    // TODO(port): get_community_node_save_query
    internal static string GetCommunityNodeSaveQuery(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_community_node_save_query");
    }

    internal const string CommunityNodeReturn = "\n" + """
    c.uuid AS uuid,
    c.name AS name,
    c.group_id AS group_id,
    c.created_at AS created_at,
    c.name_embedding AS name_embedding,
    c.summary AS summary
""" + "\n";

    internal const string CommunityNodeReturnNeptune = "\n" + """
    n.uuid AS uuid,
    n.name AS name,
    [x IN split(n.name_embedding, ",") | toFloat(x)] AS name_embedding,
    n.group_id AS group_id,
    n.summary AS summary,
    n.created_at AS created_at
""" + "\n";

    // TODO(port): get_saga_node_save_query
    internal static string GetSagaNodeSaveQuery(GraphProvider provider)
    {
        throw new NotSupportedException("TODO(port): get_saga_node_save_query");
    }

    internal const string SagaNodeReturn = "\n" + """
    s.uuid AS uuid,
    s.name AS name,
    s.group_id AS group_id,
    s.created_at AS created_at,
    s.summary AS summary,
    s.first_episode_uuid AS first_episode_uuid,
    s.last_episode_uuid AS last_episode_uuid,
    s.last_summarized_at AS last_summarized_at,
    s.last_summarized_episode_valid_at AS last_summarized_episode_valid_at
""" + "\n";

    internal const string SagaNodeReturnNeptune = "\n" + """
    s.uuid AS uuid,
    s.name AS name,
    s.group_id AS group_id,
    s.created_at AS created_at,
    s.summary AS summary,
    s.first_episode_uuid AS first_episode_uuid,
    s.last_episode_uuid AS last_episode_uuid,
    s.last_summarized_at AS last_summarized_at,
    s.last_summarized_episode_valid_at AS last_summarized_episode_valid_at
""" + "\n";
}
