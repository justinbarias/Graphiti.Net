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
using Graphiti.PyCompat;

internal static class NodeDbQueries
{
    // PORT: the parameter is `str | list[str]`, a union of two shapes, so it is object (RULES.md Part 3).
    private static List<string> ValidateEntityLabels(object? labels)
    {
        var resolvedLabels = labels is string text ? PyText.Split(text, ":").Cast<object?>() : Iterate(labels);
        var filteredLabels = resolvedLabels.Where(Py.Truthy).Select(Py.AsString).ToList();
        Helpers.ValidateNodeLabels(filteredLabels);
        return filteredLabels;
    }

    // PORT: `for x in value` and `':'.join(value)` on an open value have no rule. A str gives its code points.
    // A value that Python cannot iterate is a TypeError (R4).
    private static IEnumerable<object?> Iterate(object? value) => value switch
    {
        string text => PyText.CodePoints(text),
        PyTuple tuple => tuple,
        System.Collections.IDictionary dict => dict.Keys.Cast<object?>(),
        System.Collections.IEnumerable items and not byte[] => items.Cast<object?>(),
        _ => throw new InvalidCastException($"'{Py.TypeName(value)}' object is not iterable"),
    };

    private static string JoinLabels(object? labels) => string.Join(":", Iterate(labels).Select(Py.AsString));

    internal static string GetEpisodeNodeSaveQuery(GraphProvider provider)
    {
        switch (provider)
        {
            case GraphProvider.Neptune:
                return "\n" + """
                MERGE (n:Episodic {uuid: $uuid})
                SET n = {uuid: $uuid, name: $name, group_id: $group_id, source_description: $source_description, source: $source, content: $content,
                entity_edges: join([x IN coalesce($entity_edges, []) | toString(x) ], '|'), created_at: $created_at, valid_at: $valid_at}
                RETURN n.uuid AS uuid
""" + "\n            ";
            case GraphProvider.Falkordb:
                return "\n" + """
                MERGE (n:Episodic {uuid: $uuid})
                SET n = {uuid: $uuid, name: $name, group_id: $group_id, source_description: $source_description, source: $source, content: $content,
                entity_edges: $entity_edges, created_at: $created_at, valid_at: $valid_at}
                RETURN n.uuid AS uuid
""" + "\n            ";
            default: // Neo4j
                return "\n" + """
                MERGE (n:Episodic {uuid: $uuid})
                SET n = {uuid: $uuid, name: $name, group_id: $group_id, source_description: $source_description, source: $source, content: $content,
                entity_edges: $entity_edges, created_at: $created_at, valid_at: $valid_at}
                RETURN n.uuid AS uuid
""" + "\n            ";
        }
    }

    internal static string GetEpisodeNodeSaveBulkQuery(GraphProvider provider)
    {
        switch (provider)
        {
            case GraphProvider.Neptune:
                return "\n" + """
                UNWIND $episodes AS episode
                MERGE (n:Episodic {uuid: episode.uuid})
                SET n = {uuid: episode.uuid, name: episode.name, group_id: episode.group_id, source_description: episode.source_description,
                    source: episode.source, content: episode.content,
                entity_edges: join([x IN coalesce(episode.entity_edges, []) | toString(x) ], '|'), created_at: episode.created_at, valid_at: episode.valid_at}
                RETURN n.uuid AS uuid
""" + "\n            ";
            case GraphProvider.Falkordb:
                return "\n" + """
                UNWIND $episodes AS episode
                MERGE (n:Episodic {uuid: episode.uuid})
""" + "\n                SET n = {uuid: episode.uuid, name: episode.name, group_id: episode.group_id, source_description: episode.source_description, source: episode.source, content: episode.content, \n" + """
                entity_edges: episode.entity_edges, created_at: episode.created_at, valid_at: episode.valid_at}
                RETURN n.uuid AS uuid
""" + "\n            ";
            default: // Neo4j
                return "\n" + """
                UNWIND $episodes AS episode
                MERGE (n:Episodic {uuid: episode.uuid})
""" + "\n                SET n = {uuid: episode.uuid, name: episode.name, group_id: episode.group_id, source_description: episode.source_description, source: episode.source, content: episode.content, \n" + """
                entity_edges: episode.entity_edges, created_at: episode.created_at, valid_at: episode.valid_at}
                RETURN n.uuid AS uuid
""" + "\n            ";
        }
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

    internal static string GetEntityNodeSaveQuery(GraphProvider provider, string labels, bool hasAoss = false)
    {
        var validatedLabels = ValidateEntityLabels(labels);
        labels = string.Join(":", validatedLabels);

        switch (provider)
        {
            case GraphProvider.Falkordb:
                return "\n" + $$"""
                MERGE (n:Entity {uuid: $entity_data.uuid})
                SET n:{{labels}}
                SET n = $entity_data
                SET n.name_embedding = vecf32($entity_data.name_embedding)
                RETURN n.uuid AS uuid
""" + "\n            ";
            case GraphProvider.Neptune:
                {
                    var labelSubquery = string.Empty;
                    foreach (var label in validatedLabels)
                    {
                        labelSubquery += $" SET n:{label}\n";
                    }

                    return "\n" + $$"""
                MERGE (n:Entity {uuid: $entity_data.uuid})
                {{labelSubquery}}
                SET n = removeKeyFromMap(removeKeyFromMap($entity_data, "labels"), "name_embedding")
                SET n.name_embedding = join([x IN coalesce($entity_data.name_embedding, []) | toString(x) ], ",")
                RETURN n.uuid AS uuid
""" + "\n            ";
                }

            case GraphProvider.Relate:
                return "\n" + $$"""
                MERGE (n:Entity {uuid: $entity_data.uuid})
                SET n:{{labels}}
                SET n = $entity_data
                SET n.labels = null
                RETURN n.uuid AS uuid
""" + "\n            ";
            default:
                {
                    var saveEmbeddingQuery = !hasAoss
                        ? """
WITH n CALL db.create.setNodeVectorProperty(n, "name_embedding", $entity_data.name_embedding)
"""
                        : string.Empty;
                    return "\n" + $$"""
                MERGE (n:Entity {uuid: $entity_data.uuid})
                SET n:{{labels}}
                SET n = $entity_data
""" + "\n                "
                        + saveEmbeddingQuery
                        + "\n" + """
                RETURN n.uuid AS uuid
""" + "\n            ";
                }
        }
    }

    // PORT: the Python return type is `str | Any`, so it is object? (R6). The Neo4j branch returns a string.
    internal static object? GetEntityNodeSaveBulkQuery(GraphProvider provider, List<OrderedDictionary<string, object?>> nodes, bool hasAoss = false)
    {
        foreach (var node in nodes)
        {
            ValidateEntityLabels(Py.Get<string, object?>(node, "labels", new List<object?>()));
        }

        switch (provider)
        {
            case GraphProvider.Falkordb:
                {
                    List<object?> queries = [];
                    foreach (var node in nodes)
                    {
                        // Exclude the labels list so it is not persisted as a node property
                        // by `SET n = node`.
                        var nodeData = new OrderedDictionary<string, object?>();
                        foreach (var (k, v) in node)
                        {
                            if (k != "labels")
                            {
                                nodeData[k] = v;
                            }
                        }

                        // Apply all labels in a single query instead of one query per label.
                        var labelExpr = Py.Truthy(node["labels"]) ? JoinLabels(node["labels"]) : "Entity";
                        queries.Add(
                            new PyTuple(
                                "\n" + $$"""
                        UNWIND $nodes AS node
                        MERGE (n:Entity {uuid: node.uuid})
                        SET n:{{labelExpr}}
                        SET n = node
                        WITH n, node
                        SET n.name_embedding = vecf32(node.name_embedding)
                        RETURN n.uuid AS uuid
""" + "\n                        ",
                                new OrderedDictionary<string, object?> { ["nodes"] = new List<object?> { nodeData } }));
                    }

                    return queries;
                }

            case GraphProvider.Neptune:
                {
                    List<object?> queries = [];
                    foreach (var node in nodes)
                    {
                        var labels = string.Empty;
                        foreach (var label in Iterate(node["labels"]))
                        {
                            labels += $" SET n:{Py.Str(label)}\n";
                        }

                        queries.Add("\n" + $$"""
                        UNWIND $nodes AS node
                        MERGE (n:Entity {uuid: node.uuid})
                        {{labels}}
                        SET n = removeKeyFromMap(removeKeyFromMap(node, "labels"), "name_embedding")
                        SET n.name_embedding = join([x IN coalesce(node.name_embedding, []) | toString(x) ], ",")
                        RETURN n.uuid AS uuid
""" + "\n                    ");
                    }

                    return queries;
                }

            case GraphProvider.Relate:
                return "\n" + """
                UNWIND $nodes AS node
                MERGE (n:Entity {uuid: node.uuid})
                SET n:$(node.labels)
                SET n = node
                SET n.labels = null
                RETURN n.uuid AS uuid
""" + "\n            ";
            default: // Neo4j
                {
                    var saveEmbeddingQuery = !hasAoss
                        ? """
WITH n, node CALL db.create.setNodeVectorProperty(n, "name_embedding", node.name_embedding)
"""
                        : string.Empty;
                    return "\n" + """
                    UNWIND $nodes AS node
                    MERGE (n:Entity {uuid: node.uuid})
                    SET n:$(node.labels)
                    SET n = node
""" + "\n                    "
                        + saveEmbeddingQuery
                        + "\n" + """
                RETURN n.uuid AS uuid
""" + "\n            ";
                }
        }
    }

    /// <summary>
    /// Return the bulk save query of the provider, with <c>MATCH</c> in the place of <c>MERGE</c>.
    /// The query changes a node that exists and creates nothing. Concurrent ingestion
    /// uses it to update a stored node, so that a node that a different writer deleted
    /// is not created again. The result has the form of <c>get_entity_node_save_bulk_query</c>.
    /// </summary>
    // PORT: the Python return type is `str | Any`, so it is object? (R6), as for get_entity_node_save_bulk_query.
    internal static object? GetEntityNodeUpdateBulkQuery(GraphProvider provider, List<OrderedDictionary<string, object?>> nodes, bool hasAoss = false)
    {
        string ToUpdate(string query)
        {
            if (PyText.Count(query, "MERGE (n:Entity {uuid: ") != 1)
            {
                throw new ArgumentException($"no update query for the entity node save query of {Py.Str(provider)}");
            }

            return PyText.Replace(query, "MERGE (n:Entity {uuid: ", "MATCH (n:Entity {uuid: ");
        }

        var queries = GetEntityNodeSaveBulkQuery(provider, nodes, hasAoss);
        if (queries is string text)
        {
            return ToUpdate(text);
        }

        List<object?> result = [];
        foreach (var query in Iterate(queries))
        {
            result.Add(query is PyTuple tuple ? new PyTuple(ToUpdate(Py.AsString(tuple[0])), tuple[1]) : ToUpdate(Py.AsString(query)));
        }

        return result;
    }

    internal static string GetEntityNodeReturnQuery(GraphProvider provider)
    {
        // `name_embedding` is not returned by default and must be loaded manually using `load_name_embedding()`.
        return "\n" + """
        n.uuid AS uuid,
        n.name AS name,
        n.group_id AS group_id,
        n.created_at AS created_at,
        n.summary AS summary,
        labels(n) AS labels,
        properties(n) AS attributes
""" + "\n    ";
    }

    internal static string GetCommunityNodeSaveQuery(GraphProvider provider)
    {
        switch (provider)
        {
            case GraphProvider.Falkordb:
                return "\n" + """
                MERGE (n:Community {uuid: $uuid})
                SET n = {uuid: $uuid, name: $name, group_id: $group_id, summary: $summary, created_at: $created_at, name_embedding: vecf32($name_embedding)}
                RETURN n.uuid AS uuid
""" + "\n            ";
            case GraphProvider.Neptune:
                return "\n" + """
                MERGE (n:Community {uuid: $uuid})
                SET n = {uuid: $uuid, name: $name, group_id: $group_id, summary: $summary, created_at: $created_at}
                SET n.name_embedding = join([x IN coalesce($name_embedding, []) | toString(x) ], ",")
                RETURN n.uuid AS uuid
""" + "\n            ";
            case GraphProvider.Relate:
                return "\n" + """
                MERGE (n:Community {uuid: $uuid})
                SET n = {uuid: $uuid, name: $name, group_id: $group_id, summary: $summary, created_at: $created_at, name_embedding: $name_embedding}
                RETURN n.uuid AS uuid
""" + "\n            ";
            default: // Neo4j
                return "\n" + """
                MERGE (n:Community {uuid: $uuid})
                SET n = {uuid: $uuid, name: $name, group_id: $group_id, summary: $summary, created_at: $created_at}
                WITH n CALL db.create.setNodeVectorProperty(n, "name_embedding", $name_embedding)
                RETURN n.uuid AS uuid
""" + "\n            ";
        }
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

    internal static string GetSagaNodeSaveQuery(GraphProvider provider)
    {
        switch (provider)
        {
            default: // Neo4j, FalkorDB, Neptune
                return "\n" + """
                MERGE (n:Saga {uuid: $uuid})
                SET n = {uuid: $uuid, name: $name, group_id: $group_id, created_at: $created_at, summary: $summary, first_episode_uuid: $first_episode_uuid, last_episode_uuid: $last_episode_uuid, last_summarized_at: $last_summarized_at, last_summarized_episode_valid_at: $last_summarized_episode_valid_at}
                RETURN n.uuid AS uuid
""" + "\n            ";
        }
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
