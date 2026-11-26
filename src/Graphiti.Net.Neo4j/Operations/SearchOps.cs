// Ported from graphiti_core/driver/neo4j/operations/search_ops.py
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
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Search;
using Graphiti.PyCompat;

// PORT: the module class of R2. The module logger has no log call, so it has no Logger (R5).
internal static class SearchOps
{
    internal const long MaxQueryLength = 128;

    internal static string BuildNeo4jFulltextQuery(
        string query,
        List<string>? groupIds = null,
        long maxQueryLength = MaxQueryLength)
    {
        Helpers.ValidateGroupIds(groupIds);

        var groupIdsFilterList = groupIds is not null ? groupIds.Select(g => $"group_id:\"{g}\"").ToList() : [];
        var groupIdsFilter = string.Empty;
        foreach (var f in groupIdsFilterList)
        {
            // PORT: upstream defect U4. The group terms are joined with OR and the query with AND, with no parentheses.
            groupIdsFilter += groupIdsFilter.Length == 0 ? f : $" OR {f}";
        }

        groupIdsFilter += groupIdsFilter.Length != 0 ? " AND " : string.Empty;

        var luceneQuery = Helpers.LuceneSanitize(query);
        // PORT: len(group_ids or '') is 0 for None and for an empty list, and the count of the list for each other list.
        if (luceneQuery.Split(' ').Length + (groupIds is { Count: > 0 } ? groupIds.Count : 0) >= maxQueryLength)
        {
            return string.Empty;
        }

        var fullQuery = groupIdsFilter + "(" + luceneQuery + ")";
        return fullQuery;
    }
}

internal sealed class Neo4jSearchOperations : SearchOperations
{
    // --- Node search ---

    internal override async Task<List<EntityNode>> NodeFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        var fuzzyQuery = SearchOps.BuildNeo4jFulltextQuery(query, groupIds);
        if (fuzzyQuery.Length == 0)
        {
            return [];
        }

        var (filterQueries, filterParams) = SearchFiltersModule.NodeSearchFilterQueryConstructor(
            searchFilter, GraphProvider.Neo4j);

        if (groupIds is not null)
        {
            filterQueries.Add("n.group_id IN $group_ids");
            filterParams["group_ids"] = groupIds;
        }

        var filterQuery = string.Empty;
        if (filterQueries.Count > 0)
        {
            filterQuery = " WHERE " + string.Join(" AND ", filterQueries);
        }

        var cypher =
            GraphQueries.GetNodesQuery("node_name_and_summary", "$query", limit: limit, provider: GraphProvider.Neo4j)
            + "YIELD node AS n, score"
            + filterQuery
            + "\n" + """
            WITH n, score
            ORDER BY score DESC
            LIMIT $limit
            RETURN
""" + "\n            "
            + NodeDbQueries.GetEntityNodeReturnQuery(GraphProvider.Neo4j);

        var parameters = new OrderedDictionary<string, object?>
        {
            ["query"] = fuzzyQuery,
            ["limit"] = limit,
            ["routing_"] = "r",
        };
        foreach (var (key, value) in filterParams)
        {
            parameters[key] = value;
        }

        var (records, _, _) = await executor.ExecuteQueryAsync(cypher, parameters, cancellationToken);

        return records.Select(RecordParsers.EntityNodeFromRecord).ToList();
    }

    internal override async Task<List<EntityNode>> NodeSimilaritySearchAsync(
        QueryExecutor executor,
        List<double> searchVector,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        double minScore = 0.6,
        CancellationToken cancellationToken = default)
    {
        var (filterQueries, filterParams) = SearchFiltersModule.NodeSearchFilterQueryConstructor(
            searchFilter, GraphProvider.Neo4j);

        if (groupIds is not null)
        {
            filterQueries.Add("n.group_id IN $group_ids");
            filterParams["group_ids"] = groupIds;
        }

        var filterQuery = string.Empty;
        if (filterQueries.Count > 0)
        {
            filterQuery = " WHERE " + string.Join(" AND ", filterQueries);
        }

        var cypher =
            "MATCH (n:Entity)"
            + filterQuery
            + "\n            WITH n, "
            + GraphQueries.GetVectorCosineFuncQuery(
                "n.name_embedding", "$search_vector", GraphProvider.Neo4j)
            + " AS score" + "\n" + """
            WHERE score > $min_score
            RETURN
""" + "\n            "
            + NodeDbQueries.GetEntityNodeReturnQuery(GraphProvider.Neo4j)
            + "\n" + """
            ORDER BY score DESC
            LIMIT $limit
""" + "\n            ";

        var parameters = new OrderedDictionary<string, object?>
        {
            ["search_vector"] = searchVector,
            ["limit"] = limit,
            ["min_score"] = minScore,
            ["routing_"] = "r",
        };
        foreach (var (key, value) in filterParams)
        {
            parameters[key] = value;
        }

        var (records, _, _) = await executor.ExecuteQueryAsync(cypher, parameters, cancellationToken);

        return records.Select(RecordParsers.EntityNodeFromRecord).ToList();
    }

    internal override async Task<List<EntityNode>> NodeBfsSearchAsync(
        QueryExecutor executor,
        List<string> originUuids,
        SearchFilters searchFilter,
        long maxDepth,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (originUuids.Count == 0 || maxDepth < 1)
        {
            return [];
        }

        var (filterQueries, filterParams) = SearchFiltersModule.NodeSearchFilterQueryConstructor(
            searchFilter, GraphProvider.Neo4j);

        if (groupIds is not null)
        {
            filterQueries.Add("n.group_id IN $group_ids");
            filterQueries.Add("origin.group_id IN $group_ids");
            filterParams["group_ids"] = groupIds;
        }

        var filterQuery = string.Empty;
        if (filterQueries.Count > 0)
        {
            filterQuery = " AND " + string.Join(" AND ", filterQueries);
        }

        // PORT: upstream defect U6. The query has no ORDER BY.
        var cypher =
            "\n" + $$"""
            UNWIND $bfs_origin_node_uuids AS origin_uuid
            MATCH (origin {uuid: origin_uuid})-[:RELATES_TO|MENTIONS*1..{{Py.Str(maxDepth)}}]->(n:Entity)
            WHERE n.group_id = origin.group_id
""" + "\n            "
            + filterQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + NodeDbQueries.GetEntityNodeReturnQuery(GraphProvider.Neo4j)
            + "\n" + """
            LIMIT $limit
""" + "\n            ";

        var parameters = new OrderedDictionary<string, object?>
        {
            ["bfs_origin_node_uuids"] = originUuids,
            ["limit"] = limit,
            ["routing_"] = "r",
        };
        foreach (var (key, value) in filterParams)
        {
            parameters[key] = value;
        }

        var (records, _, _) = await executor.ExecuteQueryAsync(cypher, parameters, cancellationToken);

        return records.Select(RecordParsers.EntityNodeFromRecord).ToList();
    }

    // --- Edge search ---

    internal override async Task<List<EntityEdge>> EdgeFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        var fuzzyQuery = SearchOps.BuildNeo4jFulltextQuery(query, groupIds);
        if (fuzzyQuery.Length == 0)
        {
            return [];
        }

        var (filterQueries, filterParams) = SearchFiltersModule.EdgeSearchFilterQueryConstructor(
            searchFilter, GraphProvider.Neo4j);

        if (groupIds is not null)
        {
            filterQueries.Add("e.group_id IN $group_ids");
            filterParams["group_ids"] = groupIds;
        }

        var filterQuery = string.Empty;
        if (filterQueries.Count > 0)
        {
            filterQuery = " WHERE " + string.Join(" AND ", filterQueries);
        }

        var cypher =
            GraphQueries.GetRelationshipsQuery("edge_name_and_fact", limit: limit, provider: GraphProvider.Neo4j)
            + "\n" + """
            YIELD relationship AS rel, score
            MATCH (n:Entity)-[e:RELATES_TO {uuid: rel.uuid}]->(m:Entity)
""" + "\n            "
            + filterQuery
            + "\n" + """
            WITH e, score, n, m
            RETURN
""" + "\n            "
            + EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Neo4j)
            + "\n" + """
            ORDER BY score DESC
            LIMIT $limit
""" + "\n            ";

        var parameters = new OrderedDictionary<string, object?>
        {
            ["query"] = fuzzyQuery,
            ["limit"] = limit,
            ["routing_"] = "r",
        };
        foreach (var (key, value) in filterParams)
        {
            parameters[key] = value;
        }

        var (records, _, _) = await executor.ExecuteQueryAsync(cypher, parameters, cancellationToken);

        return records.Select(RecordParsers.EntityEdgeFromRecord).ToList();
    }

    internal override async Task<List<EntityEdge>> EdgeSimilaritySearchAsync(
        QueryExecutor executor,
        List<double> searchVector,
        string? sourceNodeUuid,
        string? targetNodeUuid,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        double minScore = 0.6,
        CancellationToken cancellationToken = default)
    {
        var (filterQueries, filterParams) = SearchFiltersModule.EdgeSearchFilterQueryConstructor(
            searchFilter, GraphProvider.Neo4j);

        if (groupIds is not null)
        {
            filterQueries.Add("e.group_id IN $group_ids");
            filterParams["group_ids"] = groupIds;

            // PORT: upstream defect U9. The source and target filters apply only when group_ids is not None.
            if (sourceNodeUuid is not null)
            {
                filterParams["source_uuid"] = sourceNodeUuid;
                filterQueries.Add("n.uuid = $source_uuid");
            }

            if (targetNodeUuid is not null)
            {
                filterParams["target_uuid"] = targetNodeUuid;
                filterQueries.Add("m.uuid = $target_uuid");
            }
        }

        var filterQuery = string.Empty;
        if (filterQueries.Count > 0)
        {
            filterQuery = " WHERE " + string.Join(" AND ", filterQueries);
        }

        var cypher =
            "MATCH (n:Entity)-[e:RELATES_TO]->(m:Entity)"
            + filterQuery
            + "\n            WITH DISTINCT e, n, m, "
            + GraphQueries.GetVectorCosineFuncQuery(
                "e.fact_embedding", "$search_vector", GraphProvider.Neo4j)
            + " AS score" + "\n" + """
            WHERE score > $min_score
            RETURN
""" + "\n            "
            + EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Neo4j)
            + "\n" + """
            ORDER BY score DESC
            LIMIT $limit
""" + "\n            ";

        var parameters = new OrderedDictionary<string, object?>
        {
            ["search_vector"] = searchVector,
            ["limit"] = limit,
            ["min_score"] = minScore,
            ["routing_"] = "r",
        };
        foreach (var (key, value) in filterParams)
        {
            parameters[key] = value;
        }

        var (records, _, _) = await executor.ExecuteQueryAsync(cypher, parameters, cancellationToken);

        return records.Select(RecordParsers.EntityEdgeFromRecord).ToList();
    }

    internal override async Task<List<EntityEdge>> EdgeBfsSearchAsync(
        QueryExecutor executor,
        List<string> originUuids,
        long maxDepth,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        if (originUuids.Count == 0)
        {
            return [];
        }

        var (filterQueries, filterParams) = SearchFiltersModule.EdgeSearchFilterQueryConstructor(
            searchFilter, GraphProvider.Neo4j);

        if (groupIds is not null)
        {
            filterQueries.Add("e.group_id IN $group_ids");
            filterParams["group_ids"] = groupIds;
        }

        var filterQuery = string.Empty;
        if (filterQueries.Count > 0)
        {
            filterQuery = " WHERE " + string.Join(" AND ", filterQueries);
        }

        // PORT: upstream defect U6. The query has no ORDER BY.
        var cypher =
            "\n" + $$"""
            UNWIND $bfs_origin_node_uuids AS origin_uuid
            MATCH path = (origin {uuid: origin_uuid})-[:RELATES_TO|MENTIONS*1..{{Py.Str(maxDepth)}}]->(:Entity)
            UNWIND relationships(path) AS rel
            MATCH (n:Entity)-[e:RELATES_TO {uuid: rel.uuid}]-(m:Entity)
""" + "\n            "
            + filterQuery
            + "\n" + """
            RETURN DISTINCT
""" + "\n            "
            + EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Neo4j)
            + "\n" + """
            LIMIT $limit
""" + "\n            ";

        var parameters = new OrderedDictionary<string, object?>
        {
            ["bfs_origin_node_uuids"] = originUuids,
            ["depth"] = maxDepth,
            ["limit"] = limit,
            ["routing_"] = "r",
        };
        foreach (var (key, value) in filterParams)
        {
            parameters[key] = value;
        }

        var (records, _, _) = await executor.ExecuteQueryAsync(cypher, parameters, cancellationToken);

        return records.Select(RecordParsers.EntityEdgeFromRecord).ToList();
    }

    // --- Episode search ---

    internal override async Task<List<EpisodicNode>> EpisodeFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        SearchFilters searchFilter, // noqa: ARG002
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        var fuzzyQuery = SearchOps.BuildNeo4jFulltextQuery(query, groupIds);
        if (fuzzyQuery.Length == 0)
        {
            return [];
        }

        var filterParams = new OrderedDictionary<string, object?>();
        var groupFilterQuery = string.Empty;
        if (groupIds is not null)
        {
            groupFilterQuery += "\nAND e.group_id IN $group_ids";
            filterParams["group_ids"] = groupIds;
        }

        var cypher =
            GraphQueries.GetNodesQuery("episode_content", "$query", limit: limit, provider: GraphProvider.Neo4j)
            + "\n" + """
            YIELD node AS episode, score
            MATCH (e:Episodic)
            WHERE e.uuid = episode.uuid
""" + "\n            "
            + groupFilterQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + NodeDbQueries.EpisodicNodeReturn
            + "\n" + """
            ORDER BY score DESC
            LIMIT $limit
""" + "\n            ";

        var parameters = new OrderedDictionary<string, object?>
        {
            ["query"] = fuzzyQuery,
            ["limit"] = limit,
            ["routing_"] = "r",
        };
        foreach (var (key, value) in filterParams)
        {
            parameters[key] = value;
        }

        var (records, _, _) = await executor.ExecuteQueryAsync(cypher, parameters, cancellationToken);

        return records.Select(RecordParsers.EpisodicNodeFromRecord).ToList();
    }

    // --- Community search ---

    internal override async Task<List<CommunityNode>> CommunityFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        var fuzzyQuery = SearchOps.BuildNeo4jFulltextQuery(query, groupIds);
        if (fuzzyQuery.Length == 0)
        {
            return [];
        }

        var filterParams = new OrderedDictionary<string, object?>();
        var groupFilterQuery = string.Empty;
        if (groupIds is not null)
        {
            groupFilterQuery = "WHERE c.group_id IN $group_ids";
            filterParams["group_ids"] = groupIds;
        }

        var cypher =
            GraphQueries.GetNodesQuery("community_name", "$query", limit: limit, provider: GraphProvider.Neo4j)
            + "\n" + """
            YIELD node AS c, score
            WITH c, score
""" + "\n            "
            + groupFilterQuery
            + "\n" + """
            RETURN
""" + "\n            "
            + NodeDbQueries.CommunityNodeReturn
            + "\n" + """
            ORDER BY score DESC
            LIMIT $limit
""" + "\n            ";

        var parameters = new OrderedDictionary<string, object?>
        {
            ["query"] = fuzzyQuery,
            ["limit"] = limit,
            ["routing_"] = "r",
        };
        foreach (var (key, value) in filterParams)
        {
            parameters[key] = value;
        }

        var (records, _, _) = await executor.ExecuteQueryAsync(cypher, parameters, cancellationToken);

        return records.Select(RecordParsers.CommunityNodeFromRecord).ToList();
    }

    internal override async Task<List<CommunityNode>> CommunitySimilaritySearchAsync(
        QueryExecutor executor,
        List<double> searchVector,
        List<string>? groupIds = null,
        long limit = 10,
        double minScore = 0.6,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new OrderedDictionary<string, object?>();

        var groupFilterQuery = string.Empty;
        if (groupIds is not null)
        {
            groupFilterQuery += " WHERE c.group_id IN $group_ids";
            queryParams["group_ids"] = groupIds;
        }

        var cypher =
            "MATCH (c:Community)"
            + groupFilterQuery
            + "\n" + """
            WITH c,
""" + "\n            "
            + GraphQueries.GetVectorCosineFuncQuery(
                "c.name_embedding", "$search_vector", GraphProvider.Neo4j)
            + " AS score" + "\n" + """
            WHERE score > $min_score
            RETURN
""" + "\n            "
            + NodeDbQueries.CommunityNodeReturn
            + "\n" + """
            ORDER BY score DESC
            LIMIT $limit
""" + "\n            ";

        var parameters = new OrderedDictionary<string, object?>
        {
            ["search_vector"] = searchVector,
            ["limit"] = limit,
            ["min_score"] = minScore,
            ["routing_"] = "r",
        };
        foreach (var (key, value) in queryParams)
        {
            parameters[key] = value;
        }

        var (records, _, _) = await executor.ExecuteQueryAsync(cypher, parameters, cancellationToken);

        return records.Select(RecordParsers.CommunityNodeFromRecord).ToList();
    }

    // --- Rerankers ---

    // PORT: R21. The reranker applies no group filter, as upstream.
    internal override async Task<List<EntityNode>> NodeDistanceRerankerAsync(
        QueryExecutor executor,
        List<string> nodeUuids,
        string centerNodeUuid,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        var filteredUuids = nodeUuids.Where(u => u != centerNodeUuid).ToList();
        var scores = new Dictionary<string, double>(StringComparer.Ordinal) { [centerNodeUuid] = 0.0 };

        var cypher = "\n" + """
        UNWIND $node_uuids AS node_uuid
        MATCH (center:Entity {uuid: $center_uuid})-[:RELATES_TO]-(n:Entity {uuid: node_uuid})
        RETURN 1 AS score, node_uuid AS uuid
""" + "\n        ";

        var (results, _, _) = await executor.ExecuteQueryAsync(
            cypher,
            new OrderedDictionary<string, object?>
            {
                ["node_uuids"] = filteredUuids,
                ["center_uuid"] = centerNodeUuid,
                ["routing_"] = "r",
            },
            cancellationToken);

        foreach (var result in results)
        {
            // PORT: the annotation of scores is dict[str, float] (R20), so the record score is read as a float.
            scores[Py.AsString(result["uuid"])] = Py.AsDouble(result["score"]);
        }

        foreach (var uuid in filteredUuids)
        {
            if (!scores.ContainsKey(uuid))
            {
                scores[uuid] = double.PositiveInfinity;
            }
        }

        // PORT: R14. A sort with a float key sorts the list object itself.
        Py.SortInPlace(filteredUuids, curUuid => scores[curUuid]);

        if (nodeUuids.Contains(centerNodeUuid, StringComparer.Ordinal))
        {
            scores[centerNodeUuid] = 0.1;
            filteredUuids = [centerNodeUuid, .. filteredUuids];
        }

        var rerankedUuids = filteredUuids.Where(u => Py.TrueDiv(1.0, scores[u]) >= minScore).ToList();

        if (rerankedUuids.Count == 0)
        {
            return [];
        }

        // Fetch the actual EntityNode objects
        var getQuery = "\n" + """
            MATCH (n:Entity)
            WHERE n.uuid IN $uuids
            RETURN
""" + "\n            " + NodeDbQueries.GetEntityNodeReturnQuery(GraphProvider.Neo4j);

        var (records, _, _) = await executor.ExecuteQueryAsync(
            getQuery,
            new OrderedDictionary<string, object?> { ["uuids"] = rerankedUuids, ["routing_"] = "r" },
            cancellationToken);

        var nodeMap = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        foreach (var r in records)
        {
            nodeMap[Py.AsString(r["uuid"])] = RecordParsers.EntityNodeFromRecord(r);
        }

        return rerankedUuids.Where(nodeMap.ContainsKey).Select(u => nodeMap[u]).ToList();
    }

    // PORT: R21. The reranker applies no group filter, as upstream.
    internal override async Task<List<EntityNode>> EpisodeMentionsRerankerAsync(
        QueryExecutor executor,
        List<string> nodeUuids,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        if (nodeUuids.Count == 0)
        {
            return [];
        }

        var scores = new Dictionary<string, double>(StringComparer.Ordinal);

        var (results, _, _) = await executor.ExecuteQueryAsync(
            "\n" + """
            UNWIND $node_uuids AS node_uuid
            MATCH (episode:Episodic)-[r:MENTIONS]->(n:Entity {uuid: node_uuid})
            RETURN count(*) AS score, n.uuid AS uuid
""" + "\n            ",
            new OrderedDictionary<string, object?>
            {
                ["node_uuids"] = nodeUuids,
                ["routing_"] = "r",
            },
            cancellationToken);

        foreach (var result in results)
        {
            // PORT: the annotation of scores is dict[str, float] (R20), so the record score is read as a float.
            scores[Py.AsString(result["uuid"])] = Py.AsDouble(result["score"]);
        }

        foreach (var uuid in nodeUuids)
        {
            if (!scores.ContainsKey(uuid))
            {
                scores[uuid] = double.PositiveInfinity;
            }
        }

        var sortedUuids = nodeUuids.ToList();
        // PORT: R14. A sort with a float key sorts the list object itself.
        Py.SortInPlace(sortedUuids, curUuid => scores[curUuid]);

        var rerankedUuids = sortedUuids.Where(u => scores[u] >= minScore).ToList();

        if (rerankedUuids.Count == 0)
        {
            return [];
        }

        // Fetch the actual EntityNode objects
        var getQuery = "\n" + """
            MATCH (n:Entity)
            WHERE n.uuid IN $uuids
            RETURN
""" + "\n            " + NodeDbQueries.GetEntityNodeReturnQuery(GraphProvider.Neo4j);

        var (records, _, _) = await executor.ExecuteQueryAsync(
            getQuery,
            new OrderedDictionary<string, object?> { ["uuids"] = rerankedUuids, ["routing_"] = "r" },
            cancellationToken);

        var nodeMap = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        foreach (var r in records)
        {
            nodeMap[Py.AsString(r["uuid"])] = RecordParsers.EntityNodeFromRecord(r);
        }

        return rerankedUuids.Where(nodeMap.ContainsKey).Select(u => nodeMap[u]).ToList();
    }

    // --- Filter builders ---

    internal override object? BuildNodeSearchFilters(SearchFilters searchFilters)
    {
        var (filterQueries, filterParams) = SearchFiltersModule.NodeSearchFilterQueryConstructor(
            searchFilters, GraphProvider.Neo4j);
        return new OrderedDictionary<string, object?> { ["filter_queries"] = filterQueries, ["filter_params"] = filterParams };
    }

    internal override object? BuildEdgeSearchFilters(SearchFilters searchFilters)
    {
        var (filterQueries, filterParams) = SearchFiltersModule.EdgeSearchFilterQueryConstructor(
            searchFilters, GraphProvider.Neo4j);
        return new OrderedDictionary<string, object?> { ["filter_queries"] = filterQueries, ["filter_params"] = filterParams };
    }

    // --- Fulltext query builder ---

    internal override string BuildFulltextQuery(
        string query,
        List<string>? groupIds = null,
        long maxQueryLength = 8000)
    {
        return SearchOps.BuildNeo4jFulltextQuery(query, groupIds, maxQueryLength);
    }
}
