// Ported from graphiti_core/driver/neo4j/operations/entity_edge_ops.py
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

namespace Graphiti.Neo4j.Operations;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Operations;
using Graphiti.Core.Models.Edges;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: the module class of R2 and R5. The module has the logger and no function.
internal static partial class EntityEdgeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(EntityEdgeOps));

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Saved Edge to Graph: {Uuid}")]
        internal static partial void SavedEdgeToGraph(ILogger logger, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted Edge: {Uuid}")]
        internal static partial void DeletedEdge(ILogger logger, string uuid);
    }
}

internal sealed class Neo4jEntityEdgeOperations : EntityEdgeOperations
{
    internal override async Task SaveAsync(
        QueryExecutor executor,
        EntityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var edgeData = new OrderedDictionary<string, object?>
        {
            ["uuid"] = edge.Uuid,
            ["source_uuid"] = edge.SourceNodeUuid,
            ["target_uuid"] = edge.TargetNodeUuid,
            ["name"] = edge.Name,
            ["fact"] = edge.Fact,
            ["fact_embedding"] = edge.FactEmbedding,
            ["group_id"] = edge.GroupId,
            ["episodes"] = edge.Episodes,
            ["created_at"] = edge.CreatedAt,
            ["expired_at"] = edge.ExpiredAt,
            ["valid_at"] = edge.ValidAt,
            ["invalid_at"] = edge.InvalidAt,
        };
        foreach (var (key, value) in edge.Attributes)
        {
            edgeData[key] = value;
        }

        var query = EdgeDbQueries.GetEntityEdgeSaveQuery(GraphProvider.Neo4j);
        if (tx is not null)
        {
            await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["edge_data"] = edgeData }, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["edge_data"] = edgeData }, cancellationToken);
        }

        EntityEdgeOps.Log.SavedEdgeToGraph(EntityEdgeOps.Logger, edge.Uuid);
    }

    internal override async Task SaveBulkAsync(
        QueryExecutor executor,
        List<EntityEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var prepared = new List<OrderedDictionary<string, object?>>();
        foreach (var edge in edges)
        {
            var edgeData = new OrderedDictionary<string, object?>
            {
                ["uuid"] = edge.Uuid,
                ["source_node_uuid"] = edge.SourceNodeUuid,
                ["target_node_uuid"] = edge.TargetNodeUuid,
                ["name"] = edge.Name,
                ["fact"] = edge.Fact,
                ["fact_embedding"] = edge.FactEmbedding,
                ["group_id"] = edge.GroupId,
                ["episodes"] = edge.Episodes,
                ["created_at"] = edge.CreatedAt,
                ["expired_at"] = edge.ExpiredAt,
                ["valid_at"] = edge.ValidAt,
                ["invalid_at"] = edge.InvalidAt,
            };
            foreach (var (key, value) in edge.Attributes)
            {
                edgeData[key] = value;
            }

            prepared.Add(edgeData);
        }

        var query = EdgeDbQueries.GetEntityEdgeSaveBulkQuery(GraphProvider.Neo4j);
        if (tx is not null)
        {
            await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["entity_edges"] = prepared }, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["entity_edges"] = prepared }, cancellationToken);
        }
    }

    internal override async Task DeleteAsync(
        QueryExecutor executor,
        EntityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n)-[e:MENTIONS|RELATES_TO|HAS_MEMBER {uuid: $uuid}]->(m)
            DELETE e
""" + "\n        ";
        if (tx is not null)
        {
            await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = edge.Uuid }, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = edge.Uuid }, cancellationToken);
        }

        EntityEdgeOps.Log.DeletedEdge(EntityEdgeOps.Logger, edge.Uuid);
    }

    internal override async Task DeleteByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n)-[e:MENTIONS|RELATES_TO|HAS_MEMBER]->(m)
            WHERE e.uuid IN $uuids
            DELETE e
""" + "\n        ";
        if (tx is not null)
        {
            await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids }, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids }, cancellationToken);
        }
    }

    internal override async Task<EntityEdge> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Entity)-[e:RELATES_TO {uuid: $uuid}]->(m:Entity)
            RETURN
""" + "\n            " + EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Neo4j);
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["routing_"] = "r" }, cancellationToken);
        var edges = records.Select(RecordParsers.EntityEdgeFromRecord).ToList();
        if (edges.Count == 0)
        {
            throw new EdgeNotFoundException(uuid);
        }

        return edges[0];
    }

    internal override async Task<List<EntityEdge>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        if (uuids.Count == 0)
        {
            return [];
        }

        var query = "\n" + """
            MATCH (n:Entity)-[e:RELATES_TO]->(m:Entity)
            WHERE e.uuid IN $uuids
            RETURN
""" + "\n            " + EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Neo4j);
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["routing_"] = "r" }, cancellationToken);
        return records.Select(RecordParsers.EntityEdgeFromRecord).ToList();
    }

    internal override async Task<List<EntityEdge>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        var cursorClause = !string.IsNullOrEmpty(uuidCursor) ? "AND e.uuid < $uuid" : string.Empty;
        var limitClause = limit is not null ? "LIMIT $limit" : string.Empty;
        var query = "\n" + """
            MATCH (n:Entity)-[e:RELATES_TO]->(m:Entity)
            WHERE e.group_id IN $group_ids
""" + "\n            "
            + cursorClause
            + "\n" + """
            RETURN
""" + "\n            "
            + EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Neo4j)
            + "\n" + """
            ORDER BY e.uuid DESC
""" + "\n            "
            + limitClause;
        var (records, _, _) = await executor.ExecuteQueryAsync(
            query,
            new OrderedDictionary<string, object?>
            {
                ["group_ids"] = groupIds,
                ["uuid"] = uuidCursor,
                ["limit"] = limit,
                ["routing_"] = "r",
            },
            cancellationToken);
        return records.Select(RecordParsers.EntityEdgeFromRecord).ToList();
    }

    internal override async Task<List<EntityEdge>> GetBetweenNodesAsync(
        QueryExecutor executor,
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Entity {uuid: $source_node_uuid})-[e:RELATES_TO]->(m:Entity {uuid: $target_node_uuid})
            RETURN
""" + "\n            " + EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Neo4j);
        var (records, _, _) = await executor.ExecuteQueryAsync(
            query,
            new OrderedDictionary<string, object?>
            {
                ["source_node_uuid"] = sourceNodeUuid,
                ["target_node_uuid"] = targetNodeUuid,
                ["routing_"] = "r",
            },
            cancellationToken);
        return records.Select(RecordParsers.EntityEdgeFromRecord).ToList();
    }

    internal override async Task<List<EntityEdge>> GetByNodeUuidAsync(
        QueryExecutor executor,
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Entity {uuid: $node_uuid})-[e:RELATES_TO]-(m:Entity)
            RETURN
""" + "\n            " + EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Neo4j);
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["node_uuid"] = nodeUuid, ["routing_"] = "r" }, cancellationToken);
        return records.Select(RecordParsers.EntityEdgeFromRecord).ToList();
    }

    internal override async Task LoadEmbeddingsAsync(
        QueryExecutor executor,
        EntityEdge edge,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Entity)-[e:RELATES_TO {uuid: $uuid}]->(m:Entity)
            RETURN e.fact_embedding AS fact_embedding
""" + "\n        ";
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = edge.Uuid, ["routing_"] = "r" }, cancellationToken);
        if (records.Count == 0)
        {
            throw new EdgeNotFoundException(edge.Uuid);
        }

        // PORT: Edge has no validate_assignment, so Python stores the record value as it is. The property is a
        // List<double>?, so the value is read as list[float] | None (R7).
        edge.FactEmbedding = Pydantic.Optional(records[0]["fact_embedding"], x => Pydantic.List(x, Pydantic.Float));
    }

    internal override async Task LoadEmbeddingsBulkAsync(
        QueryExecutor executor,
        List<EntityEdge> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var uuids = edges.Select(e => e.Uuid).ToList();
        var query = "\n" + """
            MATCH (n:Entity)-[e:RELATES_TO]-(m:Entity)
            WHERE e.uuid IN $edge_uuids
            RETURN DISTINCT e.uuid AS uuid, e.fact_embedding AS fact_embedding
""" + "\n        ";
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["edge_uuids"] = uuids, ["routing_"] = "r" }, cancellationToken);
        var embeddingMap = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var r in records)
        {
            embeddingMap[Py.AsString(r["uuid"])] = r["fact_embedding"];
        }

        foreach (var edge in edges)
        {
            if (embeddingMap.TryGetValue(edge.Uuid, out var embedding))
            {
                // PORT: the reader converts or rejects a value that Python stores as it is (R7).
                edge.FactEmbedding = Pydantic.Optional(embedding, x => Pydantic.List(x, Pydantic.Float));
            }
        }
    }
}
