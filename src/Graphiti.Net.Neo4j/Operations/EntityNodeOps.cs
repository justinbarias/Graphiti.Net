// Ported from graphiti_core/driver/neo4j/operations/entity_node_ops.py
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
using Graphiti.Core.Models.Nodes;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: the module class of R2 and R5. The module has the logger and no function.
internal static partial class EntityNodeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(EntityNodeOps));

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Saved Node to Graph: {Uuid}")]
        internal static partial void SavedNodeToGraph(ILogger logger, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted Node: {Uuid}")]
        internal static partial void DeletedNode(ILogger logger, string uuid);
    }
}

internal sealed class Neo4jEntityNodeOperations : EntityNodeOperations
{
    internal override async Task SaveAsync(
        QueryExecutor executor,
        EntityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var entityData = new OrderedDictionary<string, object?>
        {
            ["uuid"] = node.Uuid,
            ["name"] = node.Name,
            ["name_embedding"] = node.NameEmbedding,
            ["group_id"] = node.GroupId,
            ["summary"] = node.Summary,
            ["created_at"] = node.CreatedAt,
        };
        foreach (var (key, value) in node.Attributes)
        {
            entityData[key] = value;
        }

        // PORT: R14. Entity first, then the other labels in their first order, each one time.
        var labels = string.Join(':', new PySet<string>(["Entity", .. node.Labels]));

        var query = NodeDbQueries.GetEntityNodeSaveQuery(GraphProvider.Neo4j, labels);

        if (tx is not null)
        {
            await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["entity_data"] = entityData }, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["entity_data"] = entityData }, cancellationToken);
        }

        EntityNodeOps.Log.SavedNodeToGraph(EntityNodeOps.Logger, node.Uuid);
    }

    internal override async Task SaveBulkAsync(
        QueryExecutor executor,
        List<EntityNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var prepared = new List<OrderedDictionary<string, object?>>();
        foreach (var node in nodes)
        {
            var entityData = new OrderedDictionary<string, object?>
            {
                ["uuid"] = node.Uuid,
                ["name"] = node.Name,
                ["group_id"] = node.GroupId,
                ["summary"] = node.Summary,
                ["created_at"] = node.CreatedAt,
                ["name_embedding"] = node.NameEmbedding,
                // PORT: R14. Entity first, then the other labels in their first order, each one time.
                ["labels"] = new PySet<string>(["Entity", .. node.Labels]),
            };
            foreach (var (key, value) in node.Attributes)
            {
                entityData[key] = value;
            }

            prepared.Add(entityData);
        }

        var query = Py.AsString(NodeDbQueries.GetEntityNodeSaveBulkQuery(GraphProvider.Neo4j, prepared));

        if (tx is not null)
        {
            await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["nodes"] = prepared }, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["nodes"] = prepared }, cancellationToken);
        }
    }

    internal override async Task DeleteAsync(
        QueryExecutor executor,
        EntityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n {uuid: $uuid})
            WHERE n:Entity OR n:Episodic OR n:Community
            OPTIONAL MATCH (n)-[r]-()
            WITH collect(r.uuid) AS edge_uuids, n
            DETACH DELETE n
            RETURN edge_uuids
""" + "\n        ";
        if (tx is not null)
        {
            await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = node.Uuid }, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = node.Uuid }, cancellationToken);
        }

        EntityNodeOps.Log.DeletedNode(EntityNodeOps.Logger, node.Uuid);
    }

    // PORT: upstream defect U1 (R21, GAPS U1). The batched delete fails in a transaction.
    internal override async Task DeleteByGroupIdAsync(
        QueryExecutor executor,
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Entity {group_id: $group_id})
            CALL (n) {
                DETACH DELETE n
            } IN TRANSACTIONS OF $batch_size ROWS
""" + "\n        ";
        if (tx is not null)
        {
            await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["group_id"] = groupId, ["batch_size"] = batchSize }, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["group_id"] = groupId, ["batch_size"] = batchSize }, cancellationToken);
        }
    }

    // PORT: upstream defect U1 (R21, GAPS U1). The batched delete fails in a transaction.
    internal override async Task DeleteByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Entity)
            WHERE n.uuid IN $uuids
            CALL (n) {
                DETACH DELETE n
            } IN TRANSACTIONS OF $batch_size ROWS
""" + "\n        ";
        if (tx is not null)
        {
            await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["batch_size"] = batchSize }, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["batch_size"] = batchSize }, cancellationToken);
        }
    }

    internal override async Task<EntityNode> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Entity {uuid: $uuid})
            RETURN
""" + "\n            " + NodeDbQueries.GetEntityNodeReturnQuery(GraphProvider.Neo4j);
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["routing_"] = "r" }, cancellationToken);
        var nodes = records.Select(RecordParsers.EntityNodeFromRecord).ToList();
        if (nodes.Count == 0)
        {
            throw new NodeNotFoundException(uuid);
        }

        return nodes[0];
    }

    internal override async Task<List<EntityNode>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Entity)
            WHERE n.uuid IN $uuids
            RETURN
""" + "\n            " + NodeDbQueries.GetEntityNodeReturnQuery(GraphProvider.Neo4j);
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["routing_"] = "r" }, cancellationToken);
        return records.Select(RecordParsers.EntityNodeFromRecord).ToList();
    }

    internal override async Task<List<EntityNode>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        var cursorClause = !string.IsNullOrEmpty(uuidCursor) ? "AND n.uuid < $uuid" : string.Empty;
        var limitClause = limit is not null ? "LIMIT $limit" : string.Empty;
        var query = "\n" + """
            MATCH (n:Entity)
            WHERE n.group_id IN $group_ids
""" + "\n            "
            + cursorClause
            + "\n" + """
            RETURN
""" + "\n            "
            + NodeDbQueries.GetEntityNodeReturnQuery(GraphProvider.Neo4j)
            + "\n" + """
            ORDER BY n.uuid DESC
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
        return records.Select(RecordParsers.EntityNodeFromRecord).ToList();
    }

    internal override async Task LoadEmbeddingsAsync(
        QueryExecutor executor,
        EntityNode node,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Entity {uuid: $uuid})
            RETURN n.name_embedding AS name_embedding
""" + "\n        ";
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = node.Uuid, ["routing_"] = "r" }, cancellationToken);
        if (records.Count == 0)
        {
            throw new NodeNotFoundException(node.Uuid);
        }

        // PORT: validate_assignment checks the type of the field at the assignment (R7).
        var f = new PydanticFields("EntityNode", new() { ["name_embedding"] = records[0]["name_embedding"] });
        var nameEmbedding = f.Required("name_embedding", v => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Float)));
        f.ThrowIfAny();
        node.NameEmbedding = nameEmbedding.Value;
    }

    internal override async Task LoadEmbeddingsBulkAsync(
        QueryExecutor executor,
        List<EntityNode> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var uuids = nodes.Select(n => n.Uuid).ToList();
        var query = "\n" + """
            MATCH (n:Entity)
            WHERE n.uuid IN $uuids
            RETURN DISTINCT n.uuid AS uuid, n.name_embedding AS name_embedding
""" + "\n        ";
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["routing_"] = "r" }, cancellationToken);
        var embeddingMap = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var r in records)
        {
            embeddingMap[Py.AsString(r["uuid"])] = r["name_embedding"];
        }

        foreach (var node in nodes)
        {
            if (embeddingMap.TryGetValue(node.Uuid, out var embedding))
            {
                // PORT: validate_assignment checks the type of the field at the assignment (R7).
                var f = new PydanticFields("EntityNode", new() { ["name_embedding"] = embedding });
                var nameEmbedding = f.Required("name_embedding", v => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Float)));
                f.ThrowIfAny();
                node.NameEmbedding = nameEmbedding.Value;
            }
        }
    }
}
