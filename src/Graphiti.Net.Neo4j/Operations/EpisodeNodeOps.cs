// Ported from graphiti_core/driver/neo4j/operations/episode_node_ops.py
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
internal static partial class EpisodeNodeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(EpisodeNodeOps));

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Saved Episode to Graph: {Uuid}")]
        internal static partial void SavedEpisodeToGraph(ILogger logger, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted Node: {Uuid}")]
        internal static partial void DeletedNode(ILogger logger, string uuid);
    }
}

internal sealed class Neo4jEpisodeNodeOperations : EpisodeNodeOperations
{
    internal override async Task SaveAsync(
        QueryExecutor executor,
        EpisodicNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var query = NodeDbQueries.GetEpisodeNodeSaveQuery(GraphProvider.Neo4j);
        var parameters = new OrderedDictionary<string, object?>
        {
            ["uuid"] = node.Uuid,
            ["name"] = node.Name,
            ["group_id"] = node.GroupId,
            ["source_description"] = node.SourceDescription,
            ["content"] = node.Content,
            ["entity_edges"] = node.EntityEdges,
            ["created_at"] = node.CreatedAt,
            ["valid_at"] = node.ValidAt,
            ["source"] = Py.EnumValue(node.Source),
        };
        if (tx is not null)
        {
            await tx.RunAsync(query, parameters, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        EpisodeNodeOps.Log.SavedEpisodeToGraph(EpisodeNodeOps.Logger, node.Uuid);
    }

    internal override async Task SaveBulkAsync(
        QueryExecutor executor,
        List<EpisodicNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var episodes = new List<OrderedDictionary<string, object?>>();
        foreach (var node in nodes)
        {
            // PORT: dict(node) is a new dict with the fields of the model in field order, each value as it is (R6).
            var ep = new OrderedDictionary<string, object?>
            {
                ["uuid"] = node.Uuid,
                ["name"] = node.Name,
                ["group_id"] = node.GroupId,
                ["labels"] = node.Labels,
                ["created_at"] = node.CreatedAt,
                ["source"] = node.Source,
                ["source_description"] = node.SourceDescription,
                ["content"] = node.Content,
                ["valid_at"] = node.ValidAt,
                ["entity_edges"] = node.EntityEdges,
                ["episode_metadata"] = node.EpisodeMetadata,
            };
            ep["source"] = Py.Str(Py.EnumValue(node.Source));
            ep.Remove("labels");
            episodes.Add(ep);
        }

        var query = NodeDbQueries.GetEpisodeNodeSaveBulkQuery(GraphProvider.Neo4j);
        if (tx is not null)
        {
            await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["episodes"] = episodes }, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["episodes"] = episodes }, cancellationToken);
        }
    }

    internal override async Task DeleteAsync(
        QueryExecutor executor,
        EpisodicNode node,
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

        EpisodeNodeOps.Log.DeletedNode(EpisodeNodeOps.Logger, node.Uuid);
    }

    internal override async Task DeleteByGroupIdAsync(
        QueryExecutor executor,
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Episodic {group_id: $group_id})
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
            MATCH (n:Episodic)
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

    internal override async Task<EpisodicNode> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (e:Episodic {uuid: $uuid})
            RETURN
""" + "\n            " + NodeDbQueries.EpisodicNodeReturn;
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["routing_"] = "r" }, cancellationToken);
        var episodes = records.Select(RecordParsers.EpisodicNodeFromRecord).ToList();
        if (episodes.Count == 0)
        {
            throw new NodeNotFoundException(uuid);
        }

        return episodes[0];
    }

    internal override async Task<List<EpisodicNode>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (e:Episodic)
            WHERE e.uuid IN $uuids
            RETURN DISTINCT
""" + "\n            " + NodeDbQueries.EpisodicNodeReturn;
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["routing_"] = "r" }, cancellationToken);
        return records.Select(RecordParsers.EpisodicNodeFromRecord).ToList();
    }

    internal override async Task<List<EpisodicNode>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        var cursorClause = !string.IsNullOrEmpty(uuidCursor) ? "AND e.uuid < $uuid" : string.Empty;
        var limitClause = limit is not null ? "LIMIT $limit" : string.Empty;
        var query = "\n" + """
            MATCH (e:Episodic)
            WHERE e.group_id IN $group_ids
""" + "\n            "
            + cursorClause
            + "\n" + """
            RETURN DISTINCT
""" + "\n            "
            + NodeDbQueries.EpisodicNodeReturn
            + "\n" + """
            ORDER BY uuid DESC
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
        return records.Select(RecordParsers.EpisodicNodeFromRecord).ToList();
    }

    internal override async Task<List<EpisodicNode>> GetByEntityNodeUuidAsync(
        QueryExecutor executor,
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (e:Episodic)-[r:MENTIONS]->(n:Entity {uuid: $entity_node_uuid})
            RETURN DISTINCT
""" + "\n            " + NodeDbQueries.EpisodicNodeReturn;
        var (records, _, _) = await executor.ExecuteQueryAsync(
            query,
            new OrderedDictionary<string, object?> { ["entity_node_uuid"] = entityNodeUuid, ["routing_"] = "r" },
            cancellationToken);
        return records.Select(RecordParsers.EpisodicNodeFromRecord).ToList();
    }

    internal override async Task<List<EpisodicNode>> RetrieveEpisodesAsync(
        QueryExecutor executor,
        PyDateTime referenceTime,
        long lastN = 3,
        List<string>? groupIds = null,
        string? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        List<OrderedDictionary<string, object?>> records;
        if (saga is not null && groupIds is not null && groupIds.Count > 0)
        {
            var sourceClause = !string.IsNullOrEmpty(source) ? "AND e.source = $source" : string.Empty;
            var query = "\n" + """
                MATCH (s:Saga {name: $saga_name, group_id: $group_id})-[:HAS_EPISODE]->(e:Episodic)
                WHERE e.valid_at <= $reference_time
""" + "\n                "
                + sourceClause
                + "\n" + """
                RETURN
""" + "\n                "
                + NodeDbQueries.EpisodicNodeReturn
                + "\n" + """
                ORDER BY e.valid_at DESC
                LIMIT $num_episodes
""" + "\n                ";
            (records, _, _) = await executor.ExecuteQueryAsync(
                query,
                new OrderedDictionary<string, object?>
                {
                    ["saga_name"] = saga,
                    ["group_id"] = groupIds[0],
                    ["reference_time"] = referenceTime,
                    ["source"] = source,
                    ["num_episodes"] = lastN,
                    ["routing_"] = "r",
                },
                cancellationToken);
        }
        else
        {
            var sourceClause = !string.IsNullOrEmpty(source) ? "AND e.source = $source" : string.Empty;
            var groupClause = groupIds is { Count: > 0 } ? "AND e.group_id IN $group_ids" : string.Empty;
            var query = "\n" + """
                MATCH (e:Episodic)
                WHERE e.valid_at <= $reference_time
""" + "\n                "
                + groupClause
                + sourceClause
                + "\n" + """
                RETURN
""" + "\n                "
                + NodeDbQueries.EpisodicNodeReturn
                + "\n" + """
                ORDER BY e.valid_at DESC
                LIMIT $num_episodes
""" + "\n                ";
            (records, _, _) = await executor.ExecuteQueryAsync(
                query,
                new OrderedDictionary<string, object?>
                {
                    ["reference_time"] = referenceTime,
                    ["group_ids"] = groupIds,
                    ["source"] = source,
                    ["num_episodes"] = lastN,
                    ["routing_"] = "r",
                },
                cancellationToken);
        }

        return records.Select(RecordParsers.EpisodicNodeFromRecord).ToList();
    }
}
