// Ported from graphiti_core/driver/neo4j/operations/next_episode_edge_ops.py
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
using Microsoft.Extensions.Logging;

// PORT: the module class of R2 and R5, with the logger and the record function.
internal static partial class NextEpisodeEdgeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(NextEpisodeEdgeOps));

    internal static NextEpisodeEdge NextEpisodeEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
        return NextEpisodeEdge.ModelValidate(new OrderedDictionary<string, object?>
        {
            ["uuid"] = record["uuid"],
            ["group_id"] = record["group_id"],
            ["source_node_uuid"] = record["source_node_uuid"],
            ["target_node_uuid"] = record["target_node_uuid"],
            ["created_at"] = Helpers.ParseDbDate(record["created_at"]), // type: ignore[arg-type]
        });
    }

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Saved Edge to Graph: {Uuid}")]
        internal static partial void SavedEdgeToGraph(ILogger logger, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Deleted Edge: {Uuid}")]
        internal static partial void DeletedEdge(ILogger logger, string uuid);
    }
}

internal sealed class Neo4jNextEpisodeEdgeOperations : NextEpisodeEdgeOperations
{
    internal override async Task SaveAsync(
        QueryExecutor executor,
        NextEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new OrderedDictionary<string, object?>
        {
            ["source_episode_uuid"] = edge.SourceNodeUuid,
            ["target_episode_uuid"] = edge.TargetNodeUuid,
            ["uuid"] = edge.Uuid,
            ["group_id"] = edge.GroupId,
            ["created_at"] = edge.CreatedAt,
        };
        if (tx is not null)
        {
            await tx.RunAsync(EdgeDbQueries.NextEpisodeEdgeSave, parameters, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(EdgeDbQueries.NextEpisodeEdgeSave, parameters, cancellationToken);
        }

        NextEpisodeEdgeOps.Log.SavedEdgeToGraph(NextEpisodeEdgeOps.Logger, edge.Uuid);
    }

    internal override async Task SaveBulkAsync(
        QueryExecutor executor,
        List<NextEpisodeEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        foreach (var edge in edges)
        {
            await SaveAsync(executor, edge, tx, cancellationToken);
        }
    }

    internal override async Task DeleteAsync(
        QueryExecutor executor,
        NextEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Episodic)-[e:NEXT_EPISODE {uuid: $uuid}]->(m:Episodic)
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

        NextEpisodeEdgeOps.Log.DeletedEdge(NextEpisodeEdgeOps.Logger, edge.Uuid);
    }

    internal override async Task DeleteByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Episodic)-[e:NEXT_EPISODE]->(m:Episodic)
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

    internal override async Task<NextEpisodeEdge> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Episodic)-[e:NEXT_EPISODE {uuid: $uuid}]->(m:Episodic)
            RETURN
""" + "\n            " + EdgeDbQueries.NextEpisodeEdgeReturn;
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["routing_"] = "r" }, cancellationToken);
        var edges = records.Select(NextEpisodeEdgeOps.NextEpisodeEdgeFromRecord).ToList();
        if (edges.Count == 0)
        {
            throw new EdgeNotFoundException(uuid);
        }

        return edges[0];
    }

    internal override async Task<List<NextEpisodeEdge>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Episodic)-[e:NEXT_EPISODE]->(m:Episodic)
            WHERE e.uuid IN $uuids
            RETURN
""" + "\n            " + EdgeDbQueries.NextEpisodeEdgeReturn;
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["routing_"] = "r" }, cancellationToken);
        return records.Select(NextEpisodeEdgeOps.NextEpisodeEdgeFromRecord).ToList();
    }

    internal override async Task<List<NextEpisodeEdge>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        var cursorClause = !string.IsNullOrEmpty(uuidCursor) ? "AND e.uuid < $uuid" : string.Empty;
        var limitClause = limit is not null ? "LIMIT $limit" : string.Empty;
        var query = "\n" + """
            MATCH (n:Episodic)-[e:NEXT_EPISODE]->(m:Episodic)
            WHERE e.group_id IN $group_ids
""" + "\n            "
            + cursorClause
            + "\n" + """
            RETURN
""" + "\n            "
            + EdgeDbQueries.NextEpisodeEdgeReturn
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
        return records.Select(NextEpisodeEdgeOps.NextEpisodeEdgeFromRecord).ToList();
    }
}
