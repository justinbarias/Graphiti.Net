// Ported from graphiti_core/driver/neo4j/operations/saga_node_ops.py
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

// PORT: the module class of R2 and R5, with the logger and the record function.
internal static partial class SagaNodeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(SagaNodeOps));

    internal static SagaNode SagaNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
        return SagaNode.ModelValidate(new OrderedDictionary<string, object?>
        {
            ["uuid"] = record["uuid"],
            ["name"] = record["name"],
            ["group_id"] = record["group_id"],
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]), // type: ignore[arg-type]
        });
    }

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Saved Saga Node to Graph: {Uuid}")]
        internal static partial void SavedSagaNodeToGraph(ILogger logger, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted Node: {Uuid}")]
        internal static partial void DeletedNode(ILogger logger, string uuid);
    }
}

internal sealed class Neo4jSagaNodeOperations : SagaNodeOperations
{
    internal override async Task SaveAsync(
        QueryExecutor executor,
        SagaNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var query = NodeDbQueries.GetSagaNodeSaveQuery(GraphProvider.Neo4j);
        var parameters = new OrderedDictionary<string, object?>
        {
            ["uuid"] = node.Uuid,
            ["name"] = node.Name,
            ["group_id"] = node.GroupId,
            ["created_at"] = node.CreatedAt,
        };
        if (tx is not null)
        {
            await tx.RunAsync(query, parameters, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        SagaNodeOps.Log.SavedSagaNodeToGraph(SagaNodeOps.Logger, node.Uuid);
    }

    internal override async Task SaveBulkAsync(
        QueryExecutor executor,
        List<SagaNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        foreach (var node in nodes)
        {
            await SaveAsync(executor, node, tx: tx, cancellationToken: cancellationToken);
        }
    }

    internal override async Task DeleteAsync(
        QueryExecutor executor,
        SagaNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Saga {uuid: $uuid})
            DETACH DELETE n
""" + "\n        ";
        if (tx is not null)
        {
            await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = node.Uuid }, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = node.Uuid }, cancellationToken);
        }

        SagaNodeOps.Log.DeletedNode(SagaNodeOps.Logger, node.Uuid);
    }

    internal override async Task DeleteByGroupIdAsync(
        QueryExecutor executor,
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Saga {group_id: $group_id})
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

    internal override async Task DeleteByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Saga)
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

    internal override async Task<SagaNode> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (s:Saga {uuid: $uuid})
            RETURN
""" + "\n            " + NodeDbQueries.SagaNodeReturn;
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["routing_"] = "r" }, cancellationToken);
        var nodes = records.Select(SagaNodeOps.SagaNodeFromRecord).ToList();
        if (nodes.Count == 0)
        {
            throw new NodeNotFoundException(uuid);
        }

        return nodes[0];
    }

    internal override async Task<List<SagaNode>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (s:Saga)
            WHERE s.uuid IN $uuids
            RETURN
""" + "\n            " + NodeDbQueries.SagaNodeReturn;
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["routing_"] = "r" }, cancellationToken);
        return records.Select(SagaNodeOps.SagaNodeFromRecord).ToList();
    }

    internal override async Task<List<SagaNode>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        var cursorClause = !string.IsNullOrEmpty(uuidCursor) ? "AND s.uuid < $uuid" : string.Empty;
        var limitClause = limit is not null ? "LIMIT $limit" : string.Empty;
        var query = "\n" + """
            MATCH (s:Saga)
            WHERE s.group_id IN $group_ids
""" + "\n            "
            + cursorClause
            + "\n" + """
            RETURN
""" + "\n            "
            + NodeDbQueries.SagaNodeReturn
            + "\n" + """
            ORDER BY s.uuid DESC
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
        return records.Select(SagaNodeOps.SagaNodeFromRecord).ToList();
    }
}
