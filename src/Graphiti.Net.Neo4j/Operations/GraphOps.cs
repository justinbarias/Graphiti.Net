// Ported from graphiti_core/driver/neo4j/operations/graph_ops.py
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
using Graphiti.PyCompat;

// PORT: the module logger has no log call, and the module has no function or constant, so it has no
// module class (R2, R5).
internal sealed class Neo4jGraphMaintenanceOperations : GraphMaintenanceOperations
{
    internal override async Task ClearDataAsync(
        QueryExecutor executor,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        if (groupIds is null)
        {
            await executor.ExecuteQueryAsync("MATCH (n) DETACH DELETE n", new(), cancellationToken);
        }
        else
        {
            foreach (var label in new[] { "Entity", "Episodic", "Community", "Saga" })
            {
                await executor.ExecuteQueryAsync(
                    "\n" + $$"""
                    MATCH (n:{{label}})
                    WHERE n.group_id IN $group_ids
                    DETACH DELETE n
""" + "\n                    ",
                    new OrderedDictionary<string, object?> { ["group_ids"] = groupIds },
                    cancellationToken);
            }
        }
    }

    internal override async Task BuildIndicesAndConstraintsAsync(
        QueryExecutor executor,
        bool deleteExisting = false,
        CancellationToken cancellationToken = default)
    {
        if (deleteExisting)
        {
            await DeleteAllIndexesAsync(executor, cancellationToken);
        }

        var rangeIndices = GraphQueries.GetRangeIndices(GraphProvider.Neo4j);
        var fulltextIndices = GraphQueries.GetFulltextIndices(GraphProvider.Neo4j);
        var indexQueries = rangeIndices.Concat(fulltextIndices).ToList();

        // PORT: R8. One lambda for each call, so that the limit of the helper applies.
        await Helpers.SemaphoreGatherAsync(
            [.. indexQueries.Select(q => (Func<Task>)(() => executor.ExecuteQueryAsync(q, new(), cancellationToken)))],
            cancellationToken: cancellationToken);
    }

    internal override async Task DeleteAllIndexesAsync(
        QueryExecutor executor,
        CancellationToken cancellationToken = default)
    {
        await executor.ExecuteQueryAsync("CALL db.indexes() YIELD name DROP INDEX name", new(), cancellationToken);
    }

    internal override async Task<List<List<EntityNode>>> GetCommunityClustersAsync(
        QueryExecutor executor,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        var communityClusters = new List<List<EntityNode>>();
        object? groupIdsValue = groupIds;

        if (groupIds is null)
        {
            var (groupIdValues, _, _) = await executor.ExecuteQueryAsync(
                "\n" + """
                MATCH (n:Entity)
                WHERE n.group_id IS NOT NULL
                RETURN
                    collect(DISTINCT n.group_id) AS group_ids
""" + "\n                ",
                new(),
                cancellationToken);
            // PORT: R6. Python binds group_ids again to a value of a record, which is open data. The local holds it,
            // and the loop below reads it with Py.Iter and passes each item on as it is.
            groupIdsValue = groupIdValues.Count > 0 ? groupIdValues[0]["group_ids"] : new List<object?>();
        }

        var resolvedGroupIds = Py.Truthy(groupIdsValue) ? groupIdsValue : new List<object?>();
        foreach (var groupId in Py.Iter(resolvedGroupIds))
        {
            var projection = new OrderedDictionary<string, List<Neighbor>>();

            // Get all entity nodes for this group
            var (nodeRecords, _, _) = await executor.ExecuteQueryAsync(
                "\n" + """
                MATCH (n:Entity)
                WHERE n.group_id IN $group_ids
                RETURN
""" + "\n                " + NodeDbQueries.GetEntityNodeReturnQuery(GraphProvider.Neo4j),
                new OrderedDictionary<string, object?> { ["group_ids"] = new List<object?> { groupId }, ["routing_"] = "r" },
                cancellationToken);
            var nodes = nodeRecords.Select(RecordParsers.EntityNodeFromRecord).ToList();

            foreach (var node in nodes)
            {
                var (records, _, _) = await executor.ExecuteQueryAsync(
                    "\n" + """
                    MATCH (n:Entity {group_id: $group_id, uuid: $uuid})-[e:RELATES_TO]-(m: Entity {group_id: $group_id})
                    WITH count(e) AS count, m.uuid AS uuid
                    RETURN
                        uuid,
                        count
""" + "\n                    ",
                    new OrderedDictionary<string, object?> { ["uuid"] = node.Uuid, ["group_id"] = groupId },
                    cancellationToken);

                // PORT: the keyword arguments hold open data, so the model reads them with ModelValidate (R7).
                projection[node.Uuid] =
                [
                    .. records.Select(record => Neighbor.ModelValidate(new OrderedDictionary<string, object?>
                    {
                        ["node_uuid"] = record["uuid"],
                        ["edge_count"] = record["count"],
                    })),
                ];
            }

            var clusterUuids = GraphUtils.LabelPropagation(projection);

            // Fetch full node objects for each cluster
            foreach (var cluster in clusterUuids)
            {
                if (cluster.Count == 0)
                {
                    continue;
                }

                var (clusterRecords, _, _) = await executor.ExecuteQueryAsync(
                    "\n" + """
                    MATCH (n:Entity)
                    WHERE n.uuid IN $uuids
                    RETURN
""" + "\n                    " + NodeDbQueries.GetEntityNodeReturnQuery(GraphProvider.Neo4j),
                    new OrderedDictionary<string, object?> { ["uuids"] = cluster, ["routing_"] = "r" },
                    cancellationToken);
                communityClusters.Add(clusterRecords.Select(RecordParsers.EntityNodeFromRecord).ToList());
            }
        }

        return communityClusters;
    }

    internal override async Task DetermineEntityCommunityAsync(
        QueryExecutor executor,
        EntityNode entity,
        CancellationToken cancellationToken = default)
    {
        // Check if the node is already part of a community
        var (records, _, _) = await executor.ExecuteQueryAsync(
            "\n" + """
            MATCH (c:Community)-[:HAS_MEMBER]->(n:Entity {uuid: $entity_uuid})
            RETURN
""" + "\n            " + NodeDbQueries.CommunityNodeReturn,
            new OrderedDictionary<string, object?> { ["entity_uuid"] = entity.Uuid },
            cancellationToken);

        if (records.Count > 0)
        {
            return;
        }

        // If the node has no community, find the mode community of surrounding entities
        // PORT: Python assigns the records and never reads them.
        _ = await executor.ExecuteQueryAsync(
            "\n" + """
            MATCH (c:Community)-[:HAS_MEMBER]->(m:Entity)-[:RELATES_TO]-(n:Entity {uuid: $entity_uuid})
            RETURN
""" + "\n            " + NodeDbQueries.CommunityNodeReturn,
            new OrderedDictionary<string, object?> { ["entity_uuid"] = entity.Uuid },
            cancellationToken);
    }

    internal override async Task<List<EntityNode>> GetMentionedNodesAsync(
        QueryExecutor executor,
        List<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        var episodeUuids = episodes.Select(episode => episode.Uuid).ToList();

        var (records, _, _) = await executor.ExecuteQueryAsync(
            "\n" + """
            MATCH (episode:Episodic)-[:MENTIONS]->(n:Entity)
            WHERE episode.uuid IN $uuids
            RETURN DISTINCT
""" + "\n            " + NodeDbQueries.GetEntityNodeReturnQuery(GraphProvider.Neo4j),
            new OrderedDictionary<string, object?> { ["uuids"] = episodeUuids, ["routing_"] = "r" },
            cancellationToken);

        return records.Select(RecordParsers.EntityNodeFromRecord).ToList();
    }

    internal override async Task<List<CommunityNode>> GetCommunitiesByNodesAsync(
        QueryExecutor executor,
        List<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        var nodeUuids = nodes.Select(node => node.Uuid).ToList();

        var (records, _, _) = await executor.ExecuteQueryAsync(
            "\n" + """
            MATCH (c:Community)-[:HAS_MEMBER]->(m:Entity)
            WHERE m.uuid IN $uuids
            RETURN DISTINCT
""" + "\n            " + NodeDbQueries.CommunityNodeReturn,
            new OrderedDictionary<string, object?> { ["uuids"] = nodeUuids, ["routing_"] = "r" },
            cancellationToken);

        return records.Select(RecordParsers.CommunityNodeFromRecord).ToList();
    }
}
