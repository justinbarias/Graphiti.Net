// Ported from graphiti_core/driver/neo4j/operations/community_node_ops.py
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
internal static partial class CommunityNodeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(CommunityNodeOps));

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Saved Community Node to Graph: {Uuid}")]
        internal static partial void SavedCommunityNodeToGraph(ILogger logger, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted Node: {Uuid}")]
        internal static partial void DeletedNode(ILogger logger, string uuid);
    }
}

internal sealed class Neo4jCommunityNodeOperations : CommunityNodeOperations
{
    internal override async Task SaveAsync(
        QueryExecutor executor,
        CommunityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var query = NodeDbQueries.GetCommunityNodeSaveQuery(GraphProvider.Neo4j);
        var parameters = new OrderedDictionary<string, object?>
        {
            ["uuid"] = node.Uuid,
            ["name"] = node.Name,
            ["group_id"] = node.GroupId,
            ["summary"] = node.Summary,
            ["name_embedding"] = node.NameEmbedding,
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

        CommunityNodeOps.Log.SavedCommunityNodeToGraph(CommunityNodeOps.Logger, node.Uuid);
    }

    internal override async Task SaveBulkAsync(
        QueryExecutor executor,
        List<CommunityNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        // Community nodes saved individually since bulk query not in existing codebase
        foreach (var node in nodes)
        {
            await SaveAsync(executor, node, tx: tx, cancellationToken: cancellationToken);
        }
    }

    internal override async Task DeleteAsync(
        QueryExecutor executor,
        CommunityNode node,
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

        CommunityNodeOps.Log.DeletedNode(CommunityNodeOps.Logger, node.Uuid);
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
            MATCH (n:Community {group_id: $group_id})
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
            MATCH (n:Community)
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

    internal override async Task<CommunityNode> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (c:Community {uuid: $uuid})
            RETURN
""" + "\n            " + NodeDbQueries.CommunityNodeReturn;
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["routing_"] = "r" }, cancellationToken);
        var nodes = records.Select(RecordParsers.CommunityNodeFromRecord).ToList();
        if (nodes.Count == 0)
        {
            throw new NodeNotFoundException(uuid);
        }

        return nodes[0];
    }

    internal override async Task<List<CommunityNode>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (c:Community)
            WHERE c.uuid IN $uuids
            RETURN
""" + "\n            " + NodeDbQueries.CommunityNodeReturn;
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["routing_"] = "r" }, cancellationToken);
        return records.Select(RecordParsers.CommunityNodeFromRecord).ToList();
    }

    internal override async Task<List<CommunityNode>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        var cursorClause = !string.IsNullOrEmpty(uuidCursor) ? "AND c.uuid < $uuid" : string.Empty;
        var limitClause = limit is not null ? "LIMIT $limit" : string.Empty;
        var query = "\n" + """
            MATCH (c:Community)
            WHERE c.group_id IN $group_ids
""" + "\n            "
            + cursorClause
            + "\n" + """
            RETURN
""" + "\n            "
            + NodeDbQueries.CommunityNodeReturn
            + "\n" + """
            ORDER BY c.uuid DESC
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
        return records.Select(RecordParsers.CommunityNodeFromRecord).ToList();
    }

    internal override async Task LoadNameEmbeddingAsync(
        QueryExecutor executor,
        CommunityNode node,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (c:Community {uuid: $uuid})
            RETURN c.name_embedding AS name_embedding
""" + "\n        ";
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = node.Uuid, ["routing_"] = "r" }, cancellationToken);
        if (records.Count == 0)
        {
            throw new NodeNotFoundException(node.Uuid);
        }

        // PORT: validate_assignment checks the type of the field at the assignment (R7).
        var f = new PydanticFields("CommunityNode", new() { ["name_embedding"] = records[0]["name_embedding"] });
        var nameEmbedding = f.Required("name_embedding", v => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Float)));
        f.ThrowIfAny();
        node.NameEmbedding = nameEmbedding.Value;
    }
}
