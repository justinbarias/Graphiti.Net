// Ported from graphiti_core/search/search_utils.py
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

namespace Graphiti.Core.Search;

using Graphiti.Core.Driver;
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Models.Nodes;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: each provider branch (FalkorDB, Neptune) stays in its function (R1). A function that tries the driver's
// search_interface or graph_operations_interface first keeps that call and its `except NotImplementedError` (R4).
// PORT: `**filter_params` after the keyword arguments of a call is a loop that adds each entry after the other keys,
// with `Add`, which throws for a key that is already there, as Python raises TypeError for a repeated keyword.
internal static partial class SearchUtils
{
    private static ILogger Logger => PortServices.Logger(typeof(SearchUtils));

    internal const long RelevantSchemaLimit = 10;

    internal const double DefaultMinScore = 0.6;

    internal const double DefaultMmrLambda = 0.5;

    internal const long MaxSearchDepth = 3;

    internal const long MaxQueryLength = 128;

    // PORT: the zero branch returns the integer 0 in Python and a float in the other branch, so the result is double
    // (R18, GAPS F18).
    // PORT: np.dot and np.linalg.norm(v) go through BLAS, so each is a plain loop in index order (R18, GAPS G17).
    // np.dot raises ValueError for two lengths, and Py.ZipStrict throws ArgumentException (ValueError) there.
    /// <summary>
    /// Calculates the cosine similarity between two vectors using NumPy.
    /// </summary>
    internal static double CalculateCosineSimilarity(List<double> vector1, List<double> vector2)
    {
        var dotProduct = 0.0;
        foreach (var (a, b) in Py.ZipStrict(vector1, vector2))
        {
            dotProduct += a * b;
        }

        var squares1 = 0.0;
        foreach (var x in vector1)
        {
            squares1 += x * x;
        }

        var normVector1 = Math.Sqrt(squares1);
        var squares2 = 0.0;
        foreach (var x in vector2)
        {
            squares2 += x * x;
        }

        var normVector2 = Math.Sqrt(squares2);

        if (normVector1 == 0 || normVector2 == 0)
        {
            return 0; // Handle cases where one or both vectors are zero vectors
        }

        // PORT: a numpy float64 division gives inf or nan for a zero divisor, so this is the C# division, not
        // Py.TrueDiv. The product of two norms can be zero when neither norm is zero.
        return dotProduct / (normVector1 * normVector2);
    }

    // PORT: no return annotation. TYPE_HINTS.md gives str, and the FalkorDB branch returns
    // GraphDriver.BuildFulltextQuery, which is a string.
    internal static string FulltextQuery(string query, List<string>? groupIds, GraphDriver driver)
    {
        Helpers.ValidateGroupIds(groupIds);

        if (driver.Provider == GraphProvider.Falkordb)
        {
            return driver.BuildFulltextQuery(query, groupIds, MaxQueryLength);
        }

        var groupIdsFilterList = groupIds is not null
            ? groupIds.Select(g => driver.FulltextSyntax + "group_id:\"" + g + "\"").ToList()
            : [];
        var groupIdsFilter = string.Empty;
        foreach (var f in groupIdsFilterList)
        {
            // PORT: upstream defect U4. Two or more groups give `a OR b AND (q)` with no parentheses (R21).
            groupIdsFilter += groupIdsFilter.Length == 0 ? f : " OR " + f;
        }

        groupIdsFilter += groupIdsFilter.Length != 0 ? " AND " : string.Empty;

        var luceneQuery = Helpers.LuceneSanitize(query);
        // If the lucene query is too long return no query
        if (PyText.Split(luceneQuery, " ").Count + (groupIds?.Count ?? 0) >= MaxQueryLength)
        {
            return string.Empty;
        }

        var fullQuery = groupIdsFilter + "(" + luceneQuery + ")";

        return fullQuery;
    }

    internal static async Task<List<EpisodicNode>> GetEpisodesByMentionsAsync(
        GraphDriver driver,
        List<EntityNode> nodes,
        List<EntityEdge> edges,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        List<string> episodeUuids = [];
        foreach (var edge in edges)
        {
            episodeUuids.AddRange(edge.Episodes);
        }

        var episodes = await EpisodicNode.GetByUuidsAsync(driver, Py.Slice(episodeUuids, null, limit), cancellationToken: cancellationToken);

        return episodes;
    }

    // PORT: upstream defect U6. The query has no ORDER BY (R21). The lookup applies no group filter (R21).
    internal static async Task<List<EntityNode>> GetMentionedNodesAsync(
        GraphDriver driver,
        List<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.GetMentionedNodesAsync(driver, episodes, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var episodeUuids = episodes.Select(episode => episode.Uuid).ToList();

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
        MATCH (episode:Episodic)-[:MENTIONS]->(n:Entity)
        WHERE episode.uuid IN $uuids
        RETURN DISTINCT
""" + "\n        "
            + NodeDbQueries.GetEntityNodeReturnQuery(driver.Provider),
            new OrderedDictionary<string, object?> { ["uuids"] = episodeUuids, ["routing_"] = "r" },
            cancellationToken);

        var nodes = records.Select(record => Nodes.GetEntityNodeFromRecord(record, driver.Provider)).ToList();

        return nodes;
    }

    // PORT: the lookup applies no group filter (R21).
    internal static async Task<List<CommunityNode>> GetCommunitiesByNodesAsync(
        GraphDriver driver,
        List<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.GetCommunitiesByNodesAsync(driver, nodes, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var nodeUuids = nodes.Select(node => node.Uuid).ToList();

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
        MATCH (c:Community)-[:HAS_MEMBER]->(m:Entity)
        WHERE m.uuid IN $uuids
        RETURN DISTINCT
""" + "\n        "
            + NodeDbQueries.CommunityNodeReturn,
            new OrderedDictionary<string, object?> { ["uuids"] = nodeUuids, ["routing_"] = "r" },
            cancellationToken);

        var communities = records.Select(Nodes.GetCommunityNodeFromRecord).ToList();

        return communities;
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    internal static async Task<List<EntityEdge>> EdgeFulltextSearchAsync(
        GraphDriver driver,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            return await driver.SearchInterface.EdgeFulltextSearchAsync(driver, query, searchFilter, groupIds, limit, cancellationToken);
        }

        // fulltext search over facts
        var fuzzyQuery = FulltextQuery(query, groupIds, driver);

        if (fuzzyQuery == string.Empty)
        {
            return [];
        }

        var matchQuery = "\n" + """
    YIELD relationship AS rel, score
    MATCH (n:Entity)-[e:RELATES_TO {uuid: rel.uuid}]->(m:Entity)
""" + "\n    ";
        if (driver.Provider == GraphProvider.Falkordb)
        {
            // FalkorDB plans the MATCH above as a full :Entity label scan for EVERY row the
            // fulltext index yields, so the cost is O(hits x entities). The relationship the
            // index already returned knows its own endpoints, so read them directly. The label
            // predicate keeps the result set identical to the pattern's.
            matchQuery = "\n" + """
        YIELD relationship AS rel, score
        WITH rel AS e, score, startNode(rel) AS n, endNode(rel) AS m
        WHERE n:Entity AND m:Entity
        WITH e, score, n, m
""" + "\n        ";
        }

        var (filterQueries, filterParams) = SearchFiltersModule.EdgeSearchFilterQueryConstructor(searchFilter, driver.Provider);

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

        List<OrderedDictionary<string, object?>> records;
        if (driver.Provider == GraphProvider.Neptune)
        {
            // TODO(port): no rule for driver.run_aoss_query. Only the Neptune driver has the method, and GraphDriver has no member for it (D10).
            throw new NotSupportedException("TODO(port): no rule for driver.run_aoss_query");
        }
        else
        {
            query = GraphQueries.GetRelationshipsQuery("edge_name_and_fact", limit: limit, provider: driver.Provider)
                + matchQuery
                + filterQuery
                + "\n" + """
            WITH e, score, n, m
            RETURN
""" + "\n            "
                + EdgeDbQueries.GetEntityEdgeReturnQuery(driver.Provider)
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
                parameters.Add(key, value);
            }

            (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        var edges = records.Select(record => Edges.GetEntityEdgeFromRecord(record, driver.Provider)).ToList();

        return edges;
    }

    internal static async Task<List<EntityEdge>> EdgeSimilaritySearchAsync(
        GraphDriver driver,
        List<double> searchVector,
        string? sourceNodeUuid,
        string? targetNodeUuid,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        double minScore = DefaultMinScore,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            return await driver.SearchInterface.EdgeSimilaritySearchAsync(
                driver,
                searchVector,
                sourceNodeUuid,
                targetNodeUuid,
                searchFilter,
                groupIds,
                limit,
                minScore,
                cancellationToken);
        }

        var matchQuery = "\n" + """
        MATCH (n:Entity)-[e:RELATES_TO]->(m:Entity)
""" + "\n    ";
        var (filterQueries, filterParams) = SearchFiltersModule.EdgeSearchFilterQueryConstructor(searchFilter, driver.Provider);

        if (groupIds is not null)
        {
            filterQueries.Add("e.group_id IN $group_ids");
            filterParams["group_ids"] = groupIds;

            // PORT: upstream defect U9. The source and target filters apply only when group_ids is not None (R21).
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

        var searchVectorVar = "$search_vector";

        List<OrderedDictionary<string, object?>> records;
        string query;
        if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + """
                            MATCH (n:Entity)-[e:RELATES_TO]->(m:Entity)
""" + "\n                            "
                + filterQuery
                + "\n" + """
            RETURN DISTINCT id(e) as id, e.fact_embedding as embedding
""" + "\n            ";
            var firstParameters = new OrderedDictionary<string, object?>
            {
                ["search_vector"] = searchVector,
                ["limit"] = limit,
                ["min_score"] = minScore,
                ["routing_"] = "r",
            };
            foreach (var (key, value) in filterParams)
            {
                firstParameters.Add(key, value);
            }

            var (resp, _, _) = await driver.ExecuteQueryAsync(query, firstParameters, cancellationToken);

            if (resp.Count > 0)
            {
                // Calculate Cosine similarity then return the edge ids
                List<OrderedDictionary<string, object?>> inputIds = [];
                foreach (var r in resp)
                {
                    if (Py.Truthy(r["embedding"]))
                    {
                        var score = CalculateCosineSimilarity(
                            searchVector, PyText.Split(Py.AsString(r["embedding"]), ",").Select(Py.Float).ToList());
                        if (score > minScore)
                        {
                            inputIds.Add(new OrderedDictionary<string, object?> { ["id"] = r["id"], ["score"] = score });
                        }
                    }
                }

                // Match the edge ides and return the values
                query = "\n" + """
                UNWIND $ids as i
                MATCH ()-[r]->()
                WHERE id(r) = i.id
                RETURN
                    r.uuid AS uuid,
                    r.group_id AS group_id,
                    startNode(r).uuid AS source_node_uuid,
                    endNode(r).uuid AS target_node_uuid,
                    r.created_at AS created_at,
                    r.name AS name,
                    r.fact AS fact,
                    split(r.episodes, ",") AS episodes,
                    r.expired_at AS expired_at,
                    r.valid_at AS valid_at,
                    r.invalid_at AS invalid_at,
                    properties(r) AS attributes
                ORDER BY i.score DESC
                LIMIT $limit
""" + "\n                    ";
                var parameters = new OrderedDictionary<string, object?>
                {
                    ["ids"] = inputIds,
                    ["search_vector"] = searchVector,
                    ["limit"] = limit,
                    ["min_score"] = minScore,
                    ["routing_"] = "r",
                };
                foreach (var (key, value) in filterParams)
                {
                    parameters.Add(key, value);
                }

                (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
            }
            else
            {
                return [];
            }
        }
        else
        {
            query = matchQuery
                + filterQuery
                + "\n            WITH DISTINCT e, n, m, "
                + GraphQueries.GetVectorCosineFuncQuery("e.fact_embedding", searchVectorVar, driver.Provider)
                + """
 AS score
            WHERE score > $min_score
            RETURN
""" + "\n            "
                + EdgeDbQueries.GetEntityEdgeReturnQuery(driver.Provider)
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
                parameters.Add(key, value);
            }

            (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        var edges = records.Select(record => Edges.GetEntityEdgeFromRecord(record, driver.Provider)).ToList();

        return edges;
    }

    // PORT: upstream defect U6. The query has no ORDER BY (R21).
    internal static async Task<List<EntityEdge>> EdgeBfsSearchAsync(
        GraphDriver driver,
        List<string>? bfsOriginNodeUuids,
        long bfsMaxDepth,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            try
            {
                return await driver.SearchInterface.EdgeBfsSearchAsync(
                    driver, bfsOriginNodeUuids, bfsMaxDepth, searchFilter, groupIds, limit, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        // vector similarity search over embedded facts
        if (bfsOriginNodeUuids is null || bfsOriginNodeUuids.Count == 0)
        {
            return [];
        }

        var (filterQueries, filterParams) = SearchFiltersModule.EdgeSearchFilterQueryConstructor(searchFilter, driver.Provider);

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

        string query;
        if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + $$"""
            UNWIND $bfs_origin_node_uuids AS origin_uuid
            MATCH path = (origin {uuid: origin_uuid})-[:RELATES_TO|MENTIONS *1..{{Py.Str(bfsMaxDepth)}}]->(n:Entity)
            WHERE origin:Entity OR origin:Episodic
            UNWIND relationships(path) AS rel
            MATCH (n:Entity)-[e:RELATES_TO {uuid: rel.uuid}]-(m:Entity)
""" + "\n            "
                + filterQuery
                + "\n" + """
            RETURN DISTINCT
""" + "\n            "
                + EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Neptune)
                + "\n" + """
            LIMIT $limit
""" + "\n            ";
        }
        else
        {
            query = "\n" + $$"""
            UNWIND $bfs_origin_node_uuids AS origin_uuid
            MATCH path = (origin {uuid: origin_uuid})-[:RELATES_TO|MENTIONS*1..{{Py.Str(bfsMaxDepth)}}]->(:Entity)
            UNWIND relationships(path) AS rel
            WITH rel AS e, startNode(rel) AS n, endNode(rel) AS m
            WHERE type(e) = 'RELATES_TO'
            WITH e, n, m
""" + "\n            "
                + filterQuery
                + "\n" + """
            RETURN DISTINCT
""" + "\n            "
                + EdgeDbQueries.GetEntityEdgeReturnQuery(driver.Provider)
                + "\n" + """
            LIMIT $limit
""" + "\n            ";
        }

        var parameters = new OrderedDictionary<string, object?>
        {
            ["bfs_origin_node_uuids"] = bfsOriginNodeUuids,
            ["depth"] = bfsMaxDepth,
            ["limit"] = limit,
            ["routing_"] = "r",
        };
        foreach (var (key, value) in filterParams)
        {
            parameters.Add(key, value);
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);

        var edges = records.Select(record => Edges.GetEntityEdgeFromRecord(record, driver.Provider)).ToList();

        return edges;
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    internal static async Task<List<EntityNode>> NodeFulltextSearchAsync(
        GraphDriver driver,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            return await driver.SearchInterface.NodeFulltextSearchAsync(driver, query, searchFilter, groupIds, limit, cancellationToken);
        }

        // BM25 search to get top nodes
        var fuzzyQuery = FulltextQuery(query, groupIds, driver);
        if (fuzzyQuery == string.Empty)
        {
            return [];
        }

        var (filterQueries, filterParams) = SearchFiltersModule.NodeSearchFilterQueryConstructor(searchFilter, driver.Provider);

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

        var yieldQuery = "YIELD node AS n, score";
        List<OrderedDictionary<string, object?>> records;
        if (driver.Provider == GraphProvider.Neptune)
        {
            // TODO(port): no rule for driver.run_aoss_query. Only the Neptune driver has the method, and GraphDriver has no member for it (D10).
            throw new NotSupportedException("TODO(port): no rule for driver.run_aoss_query");
        }
        else
        {
            query = GraphQueries.GetNodesQuery("node_name_and_summary", "$query", limit: limit, provider: driver.Provider)
                + yieldQuery
                + filterQuery
                + "\n" + """
            WITH n, score
            ORDER BY score DESC
            LIMIT $limit
            RETURN
""" + "\n            "
                + NodeDbQueries.GetEntityNodeReturnQuery(driver.Provider);

            var parameters = new OrderedDictionary<string, object?>
            {
                ["query"] = fuzzyQuery,
                ["limit"] = limit,
                ["routing_"] = "r",
            };
            foreach (var (key, value) in filterParams)
            {
                parameters.Add(key, value);
            }

            (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        var nodes = records.Select(record => Nodes.GetEntityNodeFromRecord(record, driver.Provider)).ToList();

        return nodes;
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    internal static async Task<List<EntityNode>> NodeSimilaritySearchAsync(
        GraphDriver driver,
        List<double> searchVector,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        double minScore = DefaultMinScore,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            return await driver.SearchInterface.NodeSimilaritySearchAsync(
                driver, searchVector, searchFilter, groupIds, limit, minScore, cancellationToken);
        }

        var (filterQueries, filterParams) = SearchFiltersModule.NodeSearchFilterQueryConstructor(searchFilter, driver.Provider);

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

        var searchVectorVar = "$search_vector";

        List<OrderedDictionary<string, object?>> records;
        string query;
        if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + """
                                                                                                                                    MATCH (n:Entity)
""" + "\n                                                                                                                                    "
                + filterQuery
                + "\n" + """
            RETURN DISTINCT id(n) as id, n.name_embedding as embedding
""" + "\n            ";
            var (resp, _, _) = await driver.ExecuteQueryAsync(
                query,
                new OrderedDictionary<string, object?>
                {
                    ["params"] = filterParams,
                    ["search_vector"] = searchVector,
                    ["limit"] = limit,
                    ["min_score"] = minScore,
                    ["routing_"] = "r",
                },
                cancellationToken);

            if (resp.Count > 0)
            {
                // Calculate Cosine similarity then return the edge ids
                List<OrderedDictionary<string, object?>> inputIds = [];
                foreach (var r in resp)
                {
                    if (Py.Truthy(r["embedding"]))
                    {
                        var score = CalculateCosineSimilarity(
                            searchVector, PyText.Split(Py.AsString(r["embedding"]), ",").Select(Py.Float).ToList());
                        if (score > minScore)
                        {
                            inputIds.Add(new OrderedDictionary<string, object?> { ["id"] = r["id"], ["score"] = score });
                        }
                    }
                }

                // Match the edge ides and return the values
                query = "\n" + """
                                                                                                                                                                UNWIND $ids as i
                                                                                                                                                                MATCH (n:Entity)
                                                                                                                                                                WHERE id(n)=i.id
""" + "\n                                                                                                                                                                RETURN \n                                                                                                                                                                "
                    + NodeDbQueries.GetEntityNodeReturnQuery(driver.Provider)
                    + "\n" + """
                    ORDER BY i.score DESC
                    LIMIT $limit
""" + "\n                ";
                var parameters = new OrderedDictionary<string, object?>
                {
                    ["ids"] = inputIds,
                    ["search_vector"] = searchVector,
                    ["limit"] = limit,
                    ["min_score"] = minScore,
                    ["routing_"] = "r",
                };
                foreach (var (key, value) in filterParams)
                {
                    parameters.Add(key, value);
                }

                (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
            }
            else
            {
                return [];
            }
        }
        else
        {
            query = "\n" + """
                                                                                                                                    MATCH (n:Entity)
""" + "\n                                                                                                                                    "
                + filterQuery
                + "\n            WITH n, "
                + GraphQueries.GetVectorCosineFuncQuery("n.name_embedding", searchVectorVar, driver.Provider)
                + """
 AS score
            WHERE score > $min_score
            RETURN
""" + "\n            "
                + NodeDbQueries.GetEntityNodeReturnQuery(driver.Provider)
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
                parameters.Add(key, value);
            }

            (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        var nodes = records.Select(record => Nodes.GetEntityNodeFromRecord(record, driver.Provider)).ToList();

        return nodes;
    }

    // PORT: upstream defect U6. The query has no ORDER BY (R21).
    internal static async Task<List<EntityNode>> NodeBfsSearchAsync(
        GraphDriver driver,
        List<string>? bfsOriginNodeUuids,
        SearchFilters searchFilter,
        long bfsMaxDepth,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            try
            {
                return await driver.SearchInterface.NodeBfsSearchAsync(
                    driver, bfsOriginNodeUuids, searchFilter, bfsMaxDepth, groupIds, limit, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        if (bfsOriginNodeUuids is null || bfsOriginNodeUuids.Count == 0 || bfsMaxDepth < 1)
        {
            return [];
        }

        var (filterQueries, filterParams) = SearchFiltersModule.NodeSearchFilterQueryConstructor(searchFilter, driver.Provider);

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

        List<string> matchQueries =
        [
            "\n" + $$"""
        UNWIND $bfs_origin_node_uuids AS origin_uuid
        MATCH (origin {uuid: origin_uuid})-[:RELATES_TO|MENTIONS*1..{{Py.Str(bfsMaxDepth)}}]->(n:Entity)
        WHERE n.group_id = origin.group_id
""" + "\n        ",
        ];

        if (driver.Provider == GraphProvider.Neptune)
        {
            matchQueries =
            [
                "\n" + $$"""
            UNWIND $bfs_origin_node_uuids AS origin_uuid
            MATCH (origin {uuid: origin_uuid})-[e:RELATES_TO|MENTIONS*1..{{Py.Str(bfsMaxDepth)}}]->(n:Entity)
            WHERE origin:Entity OR origin.Episode
            AND n.group_id = origin.group_id
""" + "\n            ",
            ];
        }

        List<OrderedDictionary<string, object?>> records = [];
        foreach (var matchQuery in matchQueries)
        {
            var parameters = new OrderedDictionary<string, object?>
            {
                ["bfs_origin_node_uuids"] = bfsOriginNodeUuids,
                ["limit"] = limit,
                ["routing_"] = "r",
            };
            foreach (var (key, value) in filterParams)
            {
                parameters.Add(key, value);
            }

            var (subRecords, _, _) = await driver.ExecuteQueryAsync(
                matchQuery
                + filterQuery
                + "\n" + """
            RETURN
""" + "\n            "
                + NodeDbQueries.GetEntityNodeReturnQuery(driver.Provider)
                + "\n" + """
            LIMIT $limit
""" + "\n            ",
                parameters,
                cancellationToken);
            records.AddRange(subRecords);
        }

        var nodes = records.Select(record => Nodes.GetEntityNodeFromRecord(record, driver.Provider)).ToList();

        return nodes;
    }

    // PORT: the parameter `_search_filter` is `searchFilter` (R2). `limit` has no annotation. TYPE_HINTS.md gives int.
    internal static async Task<List<EpisodicNode>> EpisodeFulltextSearchAsync(
        GraphDriver driver,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            return await driver.SearchInterface.EpisodeFulltextSearchAsync(driver, query, searchFilter, groupIds, limit, cancellationToken);
        }

        // BM25 search to get top episodes
        var fuzzyQuery = FulltextQuery(query, groupIds, driver);
        if (fuzzyQuery == string.Empty)
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

        List<OrderedDictionary<string, object?>> records;
        if (driver.Provider == GraphProvider.Neptune)
        {
            // TODO(port): no rule for driver.run_aoss_query. Only the Neptune driver has the method, and GraphDriver has no member for it (D10).
            throw new NotSupportedException("TODO(port): no rule for driver.run_aoss_query");
        }
        else
        {
            query = GraphQueries.GetNodesQuery("episode_content", "$query", limit: limit, provider: driver.Provider)
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
                parameters.Add(key, value);
            }

            (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        var episodes = records.Select(Nodes.GetEpisodicNodeFromRecord).ToList();

        return episodes;
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    internal static async Task<List<CommunityNode>> CommunityFulltextSearchAsync(
        GraphDriver driver,
        string query,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            try
            {
                return await driver.SearchInterface.CommunityFulltextSearchAsync(driver, query, groupIds, limit, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        // BM25 search to get top communities
        var fuzzyQuery = FulltextQuery(query, groupIds, driver);
        if (fuzzyQuery == string.Empty)
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

        var yieldQuery = "YIELD node AS c, score";

        List<OrderedDictionary<string, object?>> records;
        if (driver.Provider == GraphProvider.Neptune)
        {
            // TODO(port): no rule for driver.run_aoss_query. Only the Neptune driver has the method, and GraphDriver has no member for it (D10).
            throw new NotSupportedException("TODO(port): no rule for driver.run_aoss_query");
        }
        else
        {
            query = GraphQueries.GetNodesQuery("community_name", "$query", limit: limit, provider: driver.Provider)
                + yieldQuery
                + "\n" + """
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
                parameters.Add(key, value);
            }

            (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        var communities = records.Select(Nodes.GetCommunityNodeFromRecord).ToList();

        return communities;
    }

    // PORT: `limit` and `min_score` have no annotation. TYPE_HINTS.md gives int and float.
    internal static async Task<List<CommunityNode>> CommunitySimilaritySearchAsync(
        GraphDriver driver,
        List<double> searchVector,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        double minScore = DefaultMinScore,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            try
            {
                return await driver.SearchInterface.CommunitySimilaritySearchAsync(driver, searchVector, groupIds, limit, minScore, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        // vector similarity search over entity names
        var queryParams = new OrderedDictionary<string, object?>();

        var groupFilterQuery = string.Empty;
        if (groupIds is not null)
        {
            groupFilterQuery += " WHERE c.group_id IN $group_ids";
            queryParams["group_ids"] = groupIds;
        }

        List<OrderedDictionary<string, object?>> records;
        string query;
        if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + """
                                                                                                                                    MATCH (n:Community)
""" + "\n                                                                                                                                    "
                + groupFilterQuery
                + "\n" + """
            RETURN DISTINCT id(n) as id, n.name_embedding as embedding
""" + "\n            ";
            var firstParameters = new OrderedDictionary<string, object?>
            {
                ["search_vector"] = searchVector,
                ["limit"] = limit,
                ["min_score"] = minScore,
                ["routing_"] = "r",
            };
            foreach (var (key, value) in queryParams)
            {
                firstParameters.Add(key, value);
            }

            var (resp, _, _) = await driver.ExecuteQueryAsync(query, firstParameters, cancellationToken);

            if (resp.Count > 0)
            {
                // Calculate Cosine similarity then return the edge ids
                List<OrderedDictionary<string, object?>> inputIds = [];
                foreach (var r in resp)
                {
                    if (Py.Truthy(r["embedding"]))
                    {
                        var score = CalculateCosineSimilarity(
                            searchVector, PyText.Split(Py.AsString(r["embedding"]), ",").Select(Py.Float).ToList());
                        if (score > minScore)
                        {
                            inputIds.Add(new OrderedDictionary<string, object?> { ["id"] = r["id"], ["score"] = score });
                        }
                    }
                }

                // Match the edge ides and return the values
                query = "\n" + """
                    UNWIND $ids as i
                    MATCH (comm:Community)
                    WHERE id(comm)=i.id
                    RETURN
                        comm.uuid As uuid,
                        comm.group_id AS group_id,
                        comm.name AS name,
                        comm.created_at AS created_at,
                        comm.summary AS summary,
                        comm.name_embedding AS name_embedding
                    ORDER BY i.score DESC
                    LIMIT $limit
""" + "\n                ";
                var parameters = new OrderedDictionary<string, object?>
                {
                    ["ids"] = inputIds,
                    ["search_vector"] = searchVector,
                    ["limit"] = limit,
                    ["min_score"] = minScore,
                    ["routing_"] = "r",
                };
                foreach (var (key, value) in queryParams)
                {
                    parameters.Add(key, value);
                }

                (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
            }
            else
            {
                return [];
            }
        }
        else
        {
            var searchVectorVar = "$search_vector";

            query = "\n" + """
                                                                                                                                    MATCH (c:Community)
""" + "\n                                                                                                                                    "
                + groupFilterQuery
                + "\n" + """
            WITH c,
""" + "\n            "
                + GraphQueries.GetVectorCosineFuncQuery("c.name_embedding", searchVectorVar, driver.Provider)
                + """
 AS score
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
                parameters.Add(key, value);
            }

            (records, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        var communities = records.Select(Nodes.GetCommunityNodeFromRecord).ToList();

        return communities;
    }

    /// <summary>
    /// Perform a hybrid search for nodes using both text queries and embeddings.
    ///
    /// This method combines fulltext search and vector similarity search to find
    /// relevant nodes in the graph database. It uses a rrf reranker.
    ///
    /// Parameters
    /// ----------
    /// queries : list[str]
    ///     A list of text queries to search for.
    /// embeddings : list[list[float]]
    ///     A list of embedding vectors corresponding to the queries. If empty only fulltext search is performed.
    /// driver : GraphDriver
    ///     The Neo4j driver instance for database operations.
    /// group_ids : list[str] | None, optional
    ///     The list of group ids to retrieve nodes from.
    /// limit : int | None, optional
    ///     The maximum number of results to return per search method. If None, a default limit will be applied.
    ///
    /// Returns
    /// -------
    /// list[EntityNode]
    ///     A list of unique EntityNode objects that match the search criteria.
    ///
    /// Notes
    /// -----
    /// This method performs the following steps:
    /// 1. Executes fulltext searches for each query.
    /// 2. Executes vector similarity searches for each embedding.
    /// 3. Combines and deduplicates the results from both search types.
    /// 4. Logs the performance metrics of the search operation.
    ///
    /// The search results are deduplicated based on the node UUIDs to ensure
    /// uniqueness in the returned list. The 'limit' parameter is applied to each
    /// individual search method before deduplication. If not specified, a default
    /// limit (defined in the individual search functions) will be used.
    /// </summary>
    internal static async Task<List<EntityNode>> HybridNodeSearchAsync(
        List<string> queries,
        List<List<double>> embeddings,
        GraphDriver driver,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        var start = Py.TimeSeconds();
        List<List<EntityNode>> results =
        [
            .. await Helpers.SemaphoreGatherAsync(
                [
                    .. queries.Select(q => (Func<Task<List<EntityNode>>>)(() => NodeFulltextSearchAsync(driver, q, searchFilter, groupIds, 2 * limit, cancellationToken))),
                    .. embeddings.Select(e => (Func<Task<List<EntityNode>>>)(() => NodeSimilaritySearchAsync(driver, e, searchFilter, groupIds, 2 * limit, cancellationToken: cancellationToken))),
                ],
                cancellationToken: cancellationToken),
        ];

        var nodeUuidMap = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            foreach (var node in result)
            {
                nodeUuidMap[node.Uuid] = node;
            }
        }

        var resultUuids = results.Select(result => result.Select(node => node.Uuid).ToList()).ToList();

        var (rankedUuids, _) = Rrf(resultUuids);

        var relevantNodes = rankedUuids.Select(uuid => nodeUuidMap[uuid]).ToList();

        var end = Py.TimeSeconds();
        var logger = Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var uuids = Py.Repr(rankedUuids);
            var ms = Py.Str((end - start) * 1000);
            Log.FoundRelevantNodes(logger, uuids, ms);
        }

        return relevantNodes;
    }

    internal static async Task<List<List<EntityNode>>> GetRelevantNodesAsync(
        GraphDriver driver,
        List<EntityNode> nodes,
        SearchFilters searchFilter,
        double minScore = DefaultMinScore,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0)
        {
            return [];
        }

        var groupId = nodes[0].GroupId;
        var queryNodes = nodes.Select(node => new OrderedDictionary<string, object?>
        {
            ["uuid"] = node.Uuid,
            ["name"] = node.Name,
            ["name_embedding"] = node.NameEmbedding,
            ["fulltext_query"] = FulltextQuery(node.Name, [node.GroupId], driver),
        }).ToList();

        var (filterQueries, filterParams) = SearchFiltersModule.NodeSearchFilterQueryConstructor(searchFilter, driver.Provider);

        var filterQuery = string.Empty;
        if (filterQueries.Count > 0)
        {
            filterQuery = "WHERE " + string.Join(" AND ", filterQueries);
        }

        var query = "\n" + """
                                                                                                                                    UNWIND $nodes AS node
                                                                                                                                    MATCH (n:Entity {group_id: $group_id})
""" + "\n                                                                                                                                    "
            + filterQuery
            + "\n        WITH node, n, "
            + GraphQueries.GetVectorCosineFuncQuery("n.name_embedding", "node.name_embedding", driver.Provider)
            + """
 AS score
        WHERE score > $min_score
        WITH node, collect(n)[..$limit] AS top_vector_nodes, collect(n.uuid) AS vector_node_uuids
""" + "\n        "
            + GraphQueries.GetNodesQuery(
                "node_name_and_summary",
                "node.fulltext_query",
                limit: limit,
                provider: driver.Provider)
            + "\n" + """
        YIELD node AS m
        WHERE m.group_id = $group_id
        WITH node, top_vector_nodes, vector_node_uuids, collect(m) AS fulltext_nodes

        WITH node,
            top_vector_nodes,
            [m IN fulltext_nodes WHERE NOT m.uuid IN vector_node_uuids] AS filtered_fulltext_nodes

        WITH node, top_vector_nodes + filtered_fulltext_nodes AS combined_nodes

        UNWIND combined_nodes AS combined_node
        WITH node, collect(DISTINCT combined_node) AS deduped_nodes

        RETURN
        node.uuid AS search_node_uuid,
        [x IN deduped_nodes | {
            uuid: x.uuid,
            name: x.name,
            name_embedding: x.name_embedding,
            group_id: x.group_id,
            created_at: x.created_at,
            summary: x.summary,
            labels: labels(x),
            attributes: properties(x)
        }] AS matches
""" + "\n        ";

        var parameters = new OrderedDictionary<string, object?>
        {
            ["nodes"] = queryNodes,
            ["group_id"] = groupId,
            ["limit"] = limit,
            ["min_score"] = minScore,
            ["routing_"] = "r",
        };
        foreach (var (key, value) in filterParams)
        {
            parameters.Add(key, value);
        }

        var (results, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);

        // PORT: a dict that code reads only by key is a Dictionary (R14). A record key is read as str, the type of the
        // key of the Python annotation.
        var relevantNodesDict = new Dictionary<string, List<EntityNode>>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            relevantNodesDict[Py.AsString(result["search_node_uuid"])] =
                Py.Iter(result["matches"]).Select(record => Nodes.GetEntityNodeFromRecord(Py.AsDict(record), driver.Provider)).ToList();
        }

        var relevantNodes = nodes.Select(node => Py.Get(relevantNodesDict, node.Uuid, [])).ToList();

        return relevantNodes;
    }

    internal static async Task<List<List<EntityEdge>>> GetRelevantEdgesAsync(
        GraphDriver driver,
        List<EntityEdge> edges,
        SearchFilters searchFilter,
        double minScore = DefaultMinScore,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        if (edges.Count == 0)
        {
            return [];
        }

        var (filterQueries, filterParams) = SearchFiltersModule.EdgeSearchFilterQueryConstructor(searchFilter, driver.Provider);

        var filterQuery = string.Empty;
        if (filterQueries.Count > 0)
        {
            filterQuery = " WHERE " + string.Join(" AND ", filterQueries);
        }

        List<OrderedDictionary<string, object?>> results;
        string query;
        if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + """
                                                                                                                                    UNWIND $edges AS edge
                                                                                                                                    MATCH (n:Entity {uuid: edge.source_node_uuid})-[e:RELATES_TO {group_id: edge.group_id}]-(m:Entity {uuid: edge.target_node_uuid})
""" + "\n                                                                                                                                    "
                + filterQuery
                + "\n" + """
            WITH e, edge
            RETURN DISTINCT id(e) as id, e.fact_embedding as source_embedding, edge.uuid as search_edge_uuid,
            edge.fact_embedding as target_embedding
""" + "\n            ";
            var firstParameters = new OrderedDictionary<string, object?>
            {
                ["edges"] = edges.Select(edge => edge.ModelDump()).ToList(),
                ["limit"] = limit,
                ["min_score"] = minScore,
                ["routing_"] = "r",
            };
            foreach (var (key, value) in filterParams)
            {
                firstParameters.Add(key, value);
            }

            var (resp, _, _) = await driver.ExecuteQueryAsync(query, firstParameters, cancellationToken);

            // Calculate Cosine similarity then return the edge ids
            List<OrderedDictionary<string, object?>> inputIds = [];
            foreach (var r in resp)
            {
                // PORT: np.dot takes the open list of the record as it is. The port reads each item as a float (R6).
                var score = CalculateCosineSimilarity(
                    PyText.Split(Py.AsString(r["source_embedding"]), ",").Select(Py.Float).ToList(),
                    Py.Iter(r["target_embedding"]).Select(Py.AsDouble).ToList());
                if (score > minScore)
                {
                    inputIds.Add(new OrderedDictionary<string, object?> { ["id"] = r["id"], ["score"] = score, ["uuid"] = r["search_edge_uuid"] });
                }
            }

            // Match the edge ides and return the values
            query = "\n" + """
        UNWIND $ids AS edge
        MATCH ()-[e]->()
        WHERE id(e) = edge.id
        WITH edge, e
        ORDER BY edge.score DESC
        RETURN edge.uuid AS search_edge_uuid,
            collect({
                uuid: e.uuid,
                source_node_uuid: startNode(e).uuid,
                target_node_uuid: endNode(e).uuid,
                created_at: e.created_at,
                name: e.name,
                group_id: e.group_id,
                fact: e.fact,
                fact_embedding: [x IN split(e.fact_embedding, ",") | toFloat(x)],
                episodes: split(e.episodes, ","),
                expired_at: e.expired_at,
                valid_at: e.valid_at,
                invalid_at: e.invalid_at,
                attributes: properties(e)
            })[..$limit] AS matches
""" + "\n                ";

            var parameters = new OrderedDictionary<string, object?>
            {
                ["ids"] = inputIds,
                ["edges"] = edges.Select(edge => edge.ModelDump()).ToList(),
                ["limit"] = limit,
                ["min_score"] = minScore,
                ["routing_"] = "r",
            };
            foreach (var (key, value) in filterParams)
            {
                parameters.Add(key, value);
            }

            (results, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        }
        else
        {
            query = "\n" + """
                                                                                                                                        UNWIND $edges AS edge
                                                                                                                                        MATCH (n:Entity {uuid: edge.source_node_uuid})-[e:RELATES_TO {group_id: edge.group_id}]-(m:Entity {uuid: edge.target_node_uuid})
""" + "\n                                                                                                                                        "
                + filterQuery
                + "\n            WITH e, edge, "
                + GraphQueries.GetVectorCosineFuncQuery(
                    "e.fact_embedding", "edge.fact_embedding", driver.Provider)
                + """
 AS score
            WHERE score > $min_score
            WITH edge, e, score
            ORDER BY score DESC
            RETURN
                edge.uuid AS search_edge_uuid,
                collect({
                    uuid: e.uuid,
                    source_node_uuid: startNode(e).uuid,
                    target_node_uuid: endNode(e).uuid,
                    created_at: e.created_at,
                    name: e.name,
                    group_id: e.group_id,
                    fact: e.fact,
                    fact_embedding: e.fact_embedding,
                    episodes: e.episodes,
                    expired_at: e.expired_at,
                    valid_at: e.valid_at,
                    invalid_at: e.invalid_at,
                    attributes: properties(e)
                })[..$limit] AS matches
""" + "\n            ";

            var parameters = new OrderedDictionary<string, object?>
            {
                ["edges"] = edges.Select(edge => edge.ModelDump()).ToList(),
                ["limit"] = limit,
                ["min_score"] = minScore,
                ["routing_"] = "r",
            };
            foreach (var (key, value) in filterParams)
            {
                parameters.Add(key, value);
            }

            (results, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        var relevantEdgesDict = new Dictionary<string, List<EntityEdge>>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            relevantEdgesDict[Py.AsString(result["search_edge_uuid"])] =
                Py.Iter(result["matches"]).Select(record => Edges.GetEntityEdgeFromRecord(Py.AsDict(record), driver.Provider)).ToList();
        }

        var relevantEdges = edges.Select(edge => Py.Get(relevantEdgesDict, edge.Uuid, [])).ToList();

        return relevantEdges;
    }

    internal static async Task<List<List<EntityEdge>>> GetEdgeInvalidationCandidatesAsync(
        GraphDriver driver,
        List<EntityEdge> edges,
        SearchFilters searchFilter,
        double minScore = DefaultMinScore,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        if (edges.Count == 0)
        {
            return [];
        }

        var (filterQueries, filterParams) = SearchFiltersModule.EdgeSearchFilterQueryConstructor(searchFilter, driver.Provider);

        var filterQuery = string.Empty;
        if (filterQueries.Count > 0)
        {
            filterQuery = " AND " + string.Join(" AND ", filterQueries);
        }

        List<OrderedDictionary<string, object?>> results;
        string query;
        if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + """
                                                                                                                                    UNWIND $edges AS edge
                                                                                                                                    MATCH (n:Entity)-[e:RELATES_TO {group_id: edge.group_id}]->(m:Entity)
                                                                                                                                    WHERE n.uuid IN [edge.source_node_uuid, edge.target_node_uuid] OR m.uuid IN [edge.target_node_uuid, edge.source_node_uuid]
""" + "\n                                                                                                                                    "
                + filterQuery
                + "\n" + """
            WITH e, edge
            RETURN DISTINCT id(e) as id, e.fact_embedding as source_embedding,
            edge.fact_embedding as target_embedding,
            edge.uuid as search_edge_uuid
""" + "\n            ";
            var firstParameters = new OrderedDictionary<string, object?>
            {
                ["edges"] = edges.Select(edge => edge.ModelDump()).ToList(),
                ["limit"] = limit,
                ["min_score"] = minScore,
                ["routing_"] = "r",
            };
            foreach (var (key, value) in filterParams)
            {
                firstParameters.Add(key, value);
            }

            var (resp, _, _) = await driver.ExecuteQueryAsync(query, firstParameters, cancellationToken);

            // Calculate Cosine similarity then return the edge ids
            List<OrderedDictionary<string, object?>> inputIds = [];
            foreach (var r in resp)
            {
                // PORT: np.dot takes the open list of the record as it is. The port reads each item as a float (R6).
                var score = CalculateCosineSimilarity(
                    PyText.Split(Py.AsString(r["source_embedding"]), ",").Select(Py.Float).ToList(),
                    Py.Iter(r["target_embedding"]).Select(Py.AsDouble).ToList());
                if (score > minScore)
                {
                    inputIds.Add(new OrderedDictionary<string, object?> { ["id"] = r["id"], ["score"] = score, ["uuid"] = r["search_edge_uuid"] });
                }
            }

            // Match the edge ides and return the values
            query = "\n" + """
        UNWIND $ids AS edge
        MATCH ()-[e]->()
        WHERE id(e) = edge.id
        WITH edge, e
        ORDER BY edge.score DESC
        RETURN edge.uuid AS search_edge_uuid,
            collect({
                uuid: e.uuid,
                source_node_uuid: startNode(e).uuid,
                target_node_uuid: endNode(e).uuid,
                created_at: e.created_at,
                name: e.name,
                group_id: e.group_id,
                fact: e.fact,
                fact_embedding: [x IN split(e.fact_embedding, ",") | toFloat(x)],
                episodes: split(e.episodes, ","),
                expired_at: e.expired_at,
                valid_at: e.valid_at,
                invalid_at: e.invalid_at,
                attributes: properties(e)
            })[..$limit] AS matches
""" + "\n                ";
            var parameters = new OrderedDictionary<string, object?>
            {
                ["ids"] = inputIds,
                ["edges"] = edges.Select(edge => edge.ModelDump()).ToList(),
                ["limit"] = limit,
                ["min_score"] = minScore,
                ["routing_"] = "r",
            };
            foreach (var (key, value) in filterParams)
            {
                parameters.Add(key, value);
            }

            (results, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        }
        else
        {
            query = "\n" + """
                                                                                                                                        UNWIND $edges AS edge
                                                                                                                                        MATCH (n:Entity)-[e:RELATES_TO {group_id: edge.group_id}]->(m:Entity)
                                                                                                                                        WHERE n.uuid IN [edge.source_node_uuid, edge.target_node_uuid] OR m.uuid IN [edge.target_node_uuid, edge.source_node_uuid]
""" + "\n                                                                                                                                        "
                + filterQuery
                + "\n            WITH edge, e, "
                + GraphQueries.GetVectorCosineFuncQuery(
                    "e.fact_embedding", "edge.fact_embedding", driver.Provider)
                + """
 AS score
            WHERE score > $min_score
            WITH edge, e, score
            ORDER BY score DESC
            RETURN
                edge.uuid AS search_edge_uuid,
                collect({
                    uuid: e.uuid,
                    source_node_uuid: startNode(e).uuid,
                    target_node_uuid: endNode(e).uuid,
                    created_at: e.created_at,
                    name: e.name,
                    group_id: e.group_id,
                    fact: e.fact,
                    fact_embedding: e.fact_embedding,
                    episodes: e.episodes,
                    expired_at: e.expired_at,
                    valid_at: e.valid_at,
                    invalid_at: e.invalid_at,
                    attributes: properties(e)
                })[..$limit] AS matches
""" + "\n            ";

            var parameters = new OrderedDictionary<string, object?>
            {
                ["edges"] = edges.Select(edge => edge.ModelDump()).ToList(),
                ["limit"] = limit,
                ["min_score"] = minScore,
                ["routing_"] = "r",
            };
            foreach (var (key, value) in filterParams)
            {
                parameters.Add(key, value);
            }

            (results, _, _) = await driver.ExecuteQueryAsync(query, parameters, cancellationToken);
        }

        var invalidationEdgesDict = new Dictionary<string, List<EntityEdge>>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            invalidationEdgesDict[Py.AsString(result["search_edge_uuid"])] =
                Py.Iter(result["matches"]).Select(record => Edges.GetEntityEdgeFromRecord(Py.AsDict(record), driver.Provider)).ToList();
        }

        var invalidationEdges = edges.Select(edge => Py.Get(invalidationEdgesDict, edge.Uuid, [])).ToList();

        return invalidationEdges;
    }

    // takes in a list of rankings of uuids
    // PORT: `rank_const` has no annotation. TYPE_HINTS.md gives int.
    // PORT: defaultdict(float) is an OrderedDictionary with Py.Get and a default of 0.0 (R14). The sort has a float key,
    // so it is Py.SortInPlace (R14, GAPS F19).
    internal static (List<string>, List<double>) Rrf(List<List<string>> results, long rankConst = 1, double minScore = 0)
    {
        var scores = new OrderedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            foreach (var (i, uuid) in result.Index())
            {
                scores[uuid] = Py.Get(scores, uuid, 0.0) + Py.TrueDiv(1, i + rankConst);
            }
        }

        var scoredUuids = scores.ToList();
        Py.SortInPlace(scoredUuids, term => term.Value, reverse: true);

        var sortedUuids = scoredUuids.Select(term => term.Key).ToList();

        return (
            sortedUuids.Where(uuid => scores[uuid] >= minScore).ToList(),
            sortedUuids.Where(uuid => scores[uuid] >= minScore).Select(uuid => scores[uuid]).ToList());
    }

    // PORT: the lookup applies no group filter (R21).
    internal static async Task<(List<string>, List<double>)> NodeDistanceRerankerAsync(
        GraphDriver driver,
        List<string> nodeUuids,
        string centerNodeUuid,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            try
            {
                return await driver.SearchInterface.NodeDistanceRerankerAsync(driver, nodeUuids, centerNodeUuid, minScore, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        // filter out node_uuid center node node uuid
        var filteredUuids = nodeUuids.Where(nodeUuid => nodeUuid != centerNodeUuid).ToList();
        // PORT: a score is the record value as it is, as in Python, so a value that is not a number fails only where
        // Python fails: at the sort or the division.
        var scores = new Dictionary<string, object?>(StringComparer.Ordinal) { [centerNodeUuid] = 0.0 };

        var query = "\n" + """
    UNWIND $node_uuids AS node_uuid
    MATCH (center:Entity {uuid: $center_uuid})-[:RELATES_TO]-(n:Entity {uuid: node_uuid})
    RETURN 1 AS score, node_uuid AS uuid
""" + "\n    ";
        // Find the shortest path to center node
        var (results, header, _) = await driver.ExecuteQueryAsync(
            query,
            new OrderedDictionary<string, object?>
            {
                ["node_uuids"] = filteredUuids,
                ["center_uuid"] = centerNodeUuid,
                ["routing_"] = "r",
            },
            cancellationToken);
        if (driver.Provider == GraphProvider.Falkordb)
        {
            // PORT: the driver contract gives each row as a dict, so `zip` over a row takes its keys, as Python does for a
            // dict. A header item is read as str, the key type of a record.
            results = results.Select(row =>
            {
                var item = new OrderedDictionary<string, object?>();
                foreach (var (name, value) in Py.ZipStrict(Py.Iter(header), Py.Iter(row)))
                {
                    item[Py.AsString(name)] = value;
                }

                return item;
            }).ToList();
        }

        foreach (var result in results)
        {
            var uuid = Py.AsString(result["uuid"]);
            var score = result["score"];
            scores[uuid] = score;
        }

        foreach (var uuid in filteredUuids)
        {
            if (!scores.ContainsKey(uuid))
            {
                scores[uuid] = double.PositiveInfinity;
            }
        }

        // rerank on shortest distance
        Py.SortInPlace(filteredUuids, curUuid => scores[curUuid]);

        // add back in filtered center uuid if it was filtered out
        if (nodeUuids.Contains(centerNodeUuid))
        {
            scores[centerNodeUuid] = 0.1;
            filteredUuids = [centerNodeUuid, .. filteredUuids];
        }

        return (
            filteredUuids.Where(uuid => Py.TrueDiv(1, Py.AsDouble(scores[uuid])) >= minScore).ToList(),
            filteredUuids.Where(uuid => Py.TrueDiv(1, Py.AsDouble(scores[uuid])) >= minScore).Select(uuid => Py.TrueDiv(1, Py.AsDouble(scores[uuid]))).ToList());
    }

    // PORT: the lookup applies no group filter (R21). A mention count is an int in Python, and the skeleton result type
    // holds it as a double.
    internal static async Task<(List<string>, List<double>)> EpisodeMentionsRerankerAsync(
        GraphDriver driver,
        List<List<string>> nodeUuids,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            try
            {
                return await driver.SearchInterface.EpisodeMentionsRerankerAsync(driver, nodeUuids, minScore, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        // use rrf as a preliminary ranker
        var (sortedUuids, _) = Rrf(nodeUuids);
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);

        // Find the shortest path to center node
        var (results, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
        UNWIND $node_uuids AS node_uuid
        MATCH (episode:Episodic)-[r:MENTIONS]->(n:Entity {uuid: node_uuid})
        RETURN count(*) AS score, n.uuid AS uuid
""" + "\n        ",
            new OrderedDictionary<string, object?> { ["node_uuids"] = sortedUuids, ["routing_"] = "r" },
            cancellationToken);

        foreach (var result in results)
        {
            scores[Py.AsString(result["uuid"])] = Py.AsDouble(result["score"]);
        }

        foreach (var uuid in sortedUuids)
        {
            if (!scores.ContainsKey(uuid))
            {
                // Node has no MENTIONS edges at all - treat as zero mentions so it
                // ranks last (descending sort below) and is excluded by any
                // min_score > 0, instead of being (incorrectly) unfilterable.
                scores[uuid] = 0;
            }
        }

        // rerank by descending mention count - nodes mentioned in the most
        // episodes rank first, matching the reranker's name and the convention
        // used by the other count-based rerankers.
        Py.SortInPlace(sortedUuids, curUuid => scores[curUuid], reverse: true);

        return (
            sortedUuids.Where(uuid => scores[uuid] >= minScore).ToList(),
            sortedUuids.Where(uuid => scores[uuid] >= minScore).Select(uuid => scores[uuid]).ToList());
    }

    // PORT: `candidates` is iterated with `.items()`, so it is an OrderedDictionary (R14), the result type of the
    // get_embeddings_for_* functions.
    // PORT: np.dot is a plain loop in index order (R18, GAPS G17). np.max of a row gives nan if the row holds a nan.
    internal static (List<string>, List<double>) MaximalMarginalRelevance(
        List<double> queryVector,
        OrderedDictionary<string, List<double>> candidates,
        double mmrLambda = DefaultMmrLambda,
        double minScore = -2.0)
    {
        var start = Py.TimeSeconds();
        var queryArray = queryVector;
        var candidateArrays = new OrderedDictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var (uuid, embedding) in candidates)
        {
            candidateArrays[uuid] = Helpers.NormalizeL2(embedding);
        }

        var uuids = candidateArrays.Keys.ToList();

        var similarityMatrix = new double[uuids.Count, uuids.Count];

        foreach (var (i, uuid1) in uuids.Index())
        {
            foreach (var (j, uuid2) in uuids.Take(i).Index())
            {
                var u = candidateArrays[uuid1];
                var v = candidateArrays[uuid2];
                var similarity = 0.0;
                foreach (var (a, b) in Py.ZipStrict(u, v))
                {
                    similarity += a * b;
                }

                similarityMatrix[i, j] = similarity;
                similarityMatrix[j, i] = similarity;
            }
        }

        var mmrScores = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (i, uuid) in uuids.Index())
        {
            var maxSim = double.NegativeInfinity;
            for (var k = 0; k < uuids.Count; k++)
            {
                var value = similarityMatrix[i, k];
                if (double.IsNaN(value))
                {
                    maxSim = double.NaN;
                    break;
                }

                if (value > maxSim)
                {
                    maxSim = value;
                }
            }

            var relevance = 0.0;
            foreach (var (a, b) in Py.ZipStrict(queryArray, candidateArrays[uuid]))
            {
                relevance += a * b;
            }

            var mmr = (mmrLambda * relevance) + ((mmrLambda - 1) * maxSim);
            mmrScores[uuid] = mmr;
        }

        Py.SortInPlace(uuids, c => mmrScores[c], reverse: true);

        var end = Py.TimeSeconds();
        var logger = Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var ms = Py.Str((end - start) * 1000);
            Log.CompletedMmrReranking(logger, ms);
        }

        return (
            uuids.Where(uuid => mmrScores[uuid] >= minScore).ToList(),
            uuids.Where(uuid => mmrScores[uuid] >= minScore).Select(uuid => mmrScores[uuid]).ToList());
    }

    // PORT: the result is a typed dict of float lists. A record value is read with Py.AsString for the key and as a list
    // of floats for the embedding. Python stores the value as it is, so the strings of the Neptune `split` stay strings
    // there, and the port throws InvalidCastException for them.
    internal static async Task<OrderedDictionary<string, List<double>>> GetEmbeddingsForNodesAsync(
        GraphDriver driver,
        List<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        string query;
        if (driver.GraphOperationsInterface is not null)
        {
            return await driver.GraphOperationsInterface.NodeLoadEmbeddingsBulkAsync(driver, nodes, cancellationToken: cancellationToken);
        }
        else if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + """
        MATCH (n:Entity)
        WHERE n.uuid IN $node_uuids
        RETURN DISTINCT
            n.uuid AS uuid,
            split(n.name_embedding, ",") AS name_embedding
""" + "\n        ";
        }
        else
        {
            query = "\n" + """
        MATCH (n:Entity)
        WHERE n.uuid IN $node_uuids
        RETURN DISTINCT
            n.uuid AS uuid,
            n.name_embedding AS name_embedding
""" + "\n        ";
        }

        var (results, _, _) = await driver.ExecuteQueryAsync(
            query,
            new OrderedDictionary<string, object?>
            {
                ["node_uuids"] = nodes.Select(node => node.Uuid).ToList(),
                ["routing_"] = "r",
            },
            cancellationToken);

        var embeddingsDict = new OrderedDictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            var uuid = Py.Get(result, "uuid");
            var embedding = Py.Get(result, "name_embedding");
            if (uuid is not null && embedding is not null)
            {
                embeddingsDict[Py.AsString(uuid)] = Py.Iter(embedding).Select(Py.AsDouble).ToList();
            }
        }

        return embeddingsDict;
    }

    // PORT: the reads of the result follow GetEmbeddingsForNodesAsync.
    internal static async Task<OrderedDictionary<string, List<double>>> GetEmbeddingsForCommunitiesAsync(
        GraphDriver driver,
        List<CommunityNode> communities,
        CancellationToken cancellationToken = default)
    {
        if (driver.SearchInterface is not null)
        {
            try
            {
                return await driver.SearchInterface.GetEmbeddingsForCommunitiesAsync(driver, communities, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        string query;
        if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + """
        MATCH (c:Community)
        WHERE c.uuid IN $community_uuids
        RETURN DISTINCT
            c.uuid AS uuid,
            split(c.name_embedding, ",") AS name_embedding
""" + "\n        ";
        }
        else
        {
            query = "\n" + """
        MATCH (c:Community)
        WHERE c.uuid IN $community_uuids
        RETURN DISTINCT
            c.uuid AS uuid,
            c.name_embedding AS name_embedding
""" + "\n        ";
        }

        var (results, _, _) = await driver.ExecuteQueryAsync(
            query,
            new OrderedDictionary<string, object?>
            {
                ["community_uuids"] = communities.Select(community => community.Uuid).ToList(),
                ["routing_"] = "r",
            },
            cancellationToken);

        var embeddingsDict = new OrderedDictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            var uuid = Py.Get(result, "uuid");
            var embedding = Py.Get(result, "name_embedding");
            if (uuid is not null && embedding is not null)
            {
                embeddingsDict[Py.AsString(uuid)] = Py.Iter(embedding).Select(Py.AsDouble).ToList();
            }
        }

        return embeddingsDict;
    }

    // PORT: the reads of the result follow GetEmbeddingsForNodesAsync.
    internal static async Task<OrderedDictionary<string, List<double>>> GetEmbeddingsForEdgesAsync(
        GraphDriver driver,
        List<EntityEdge> edges,
        CancellationToken cancellationToken = default)
    {
        string query;
        if (driver.GraphOperationsInterface is not null)
        {
            return await driver.GraphOperationsInterface.EdgeLoadEmbeddingsBulkAsync(driver, edges, cancellationToken: cancellationToken);
        }
        else if (driver.Provider == GraphProvider.Neptune)
        {
            query = "\n" + """
        MATCH (n:Entity)-[e:RELATES_TO]-(m:Entity)
        WHERE e.uuid IN $edge_uuids
        RETURN DISTINCT
            e.uuid AS uuid,
            split(e.fact_embedding, ",") AS fact_embedding
""" + "\n        ";
        }
        else
        {
            query = "\n" + """
            MATCH (n:Entity)-[e:RELATES_TO]-(m:Entity)
        WHERE e.uuid IN $edge_uuids
        RETURN DISTINCT
            e.uuid AS uuid,
            e.fact_embedding AS fact_embedding
""" + "\n        ";
        }

        var (results, _, _) = await driver.ExecuteQueryAsync(
            query,
            new OrderedDictionary<string, object?>
            {
                ["edge_uuids"] = edges.Select(edge => edge.Uuid).ToList(),
                ["routing_"] = "r",
            },
            cancellationToken);

        var embeddingsDict = new OrderedDictionary<string, List<double>>(StringComparer.Ordinal);
        foreach (var result in results)
        {
            var uuid = Py.Get(result, "uuid");
            var embedding = Py.Get(result, "fact_embedding");
            if (uuid is not null && embedding is not null)
            {
                embeddingsDict[Py.AsString(uuid)] = Py.Iter(embedding).Select(Py.AsDouble).ToList();
            }
        }

        return embeddingsDict;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Found relevant nodes: {Uuids} in {Ms} ms")]
        internal static partial void FoundRelevantNodes(ILogger logger, string uuids, string ms);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Completed MMR reranking in {Ms} ms")]
        internal static partial void CompletedMmrReranking(ILogger logger, string ms);
    }
}
