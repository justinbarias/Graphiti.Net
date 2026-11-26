// Ported from graphiti_core/driver/neo4j/operations/community_edge_ops.py
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
internal static partial class CommunityEdgeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(CommunityEdgeOps));

    internal static CommunityEdge CommunityEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
        return CommunityEdge.ModelValidate(new OrderedDictionary<string, object?>
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

internal sealed class Neo4jCommunityEdgeOperations : CommunityEdgeOperations
{
    internal override async Task SaveAsync(
        QueryExecutor executor,
        CommunityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var query = EdgeDbQueries.GetCommunityEdgeSaveQuery(GraphProvider.Neo4j);
        var parameters = new OrderedDictionary<string, object?>
        {
            ["community_uuid"] = edge.SourceNodeUuid,
            ["entity_uuid"] = edge.TargetNodeUuid,
            ["uuid"] = edge.Uuid,
            ["group_id"] = edge.GroupId,
            ["created_at"] = edge.CreatedAt,
        };
        if (tx is not null)
        {
            await tx.RunAsync(query, parameters, cancellationToken);
        }
        else
        {
            await executor.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        CommunityEdgeOps.Log.SavedEdgeToGraph(CommunityEdgeOps.Logger, edge.Uuid);
    }

    internal override async Task DeleteAsync(
        QueryExecutor executor,
        CommunityEdge edge,
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

        CommunityEdgeOps.Log.DeletedEdge(CommunityEdgeOps.Logger, edge.Uuid);
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

    internal override async Task<CommunityEdge> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Community)-[e:HAS_MEMBER {uuid: $uuid}]->(m)
            RETURN
""" + "\n            " + EdgeDbQueries.CommunityEdgeReturn;
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["routing_"] = "r" }, cancellationToken);
        var edges = records.Select(CommunityEdgeOps.CommunityEdgeFromRecord).ToList();
        if (edges.Count == 0)
        {
            throw new EdgeNotFoundException(uuid);
        }

        return edges[0];
    }

    internal override async Task<List<CommunityEdge>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        var query = "\n" + """
            MATCH (n:Community)-[e:HAS_MEMBER]->(m)
            WHERE e.uuid IN $uuids
            RETURN
""" + "\n            " + EdgeDbQueries.CommunityEdgeReturn;
        var (records, _, _) = await executor.ExecuteQueryAsync(query, new OrderedDictionary<string, object?> { ["uuids"] = uuids, ["routing_"] = "r" }, cancellationToken);
        return records.Select(CommunityEdgeOps.CommunityEdgeFromRecord).ToList();
    }

    internal override async Task<List<CommunityEdge>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        var cursorClause = !string.IsNullOrEmpty(uuidCursor) ? "AND e.uuid < $uuid" : string.Empty;
        var limitClause = limit is not null ? "LIMIT $limit" : string.Empty;
        var query = "\n" + """
            MATCH (n:Community)-[e:HAS_MEMBER]->(m)
            WHERE e.group_id IN $group_ids
""" + "\n            "
            + cursorClause
            + "\n" + """
            RETURN
""" + "\n            "
            + EdgeDbQueries.CommunityEdgeReturn
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
        return records.Select(CommunityEdgeOps.CommunityEdgeFromRecord).ToList();
    }
}
