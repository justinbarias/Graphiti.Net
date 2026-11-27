// Ported from graphiti_core/search/search.py
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

using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: the module name equals its folder name, so the module class has the suffix Module (R2).
internal static partial class SearchModule
{
    private static ILogger Logger => PortServices.Logger(typeof(SearchModule));

    // PORT: `value.value if hasattr(value, 'value') else value` is two overloads (R15): one for text, one for an enum
    // that returns its wire value.
    private static string EnumValue(string value)
    {
        return Py.EnumValue(value);
    }

    private static object EnumValue<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return Py.EnumValue(value);
    }

    private static Tracer ResolveTracer(Tracer? searchTracer)
    {
        return searchTracer is not null ? searchTracer : new NoOpTracer();
    }

    // PORT: a @contextmanager that sets the span status from the exception of its body. It takes the body as a lambda,
    // as Tracer.StartSpanAsync does (R16, decision D45): `with _trace_phase(t, name, attrs) as span: ...` is
    // `await TracePhaseAsync(t, name, attrs, async span => { ... }, cancellationToken)`, and a `return` in the body is
    // the result of the call. `attributes` has no default, because the body follows it. Each upstream call passes it.
    private static Task<T> TracePhaseAsync<T>(
        Tracer searchTracer,
        string name,
        OrderedDictionary<string, object?>? attributes,
        Func<TracerSpan, Task<T>> body,
        CancellationToken cancellationToken = default)
    {
        return searchTracer.StartSpanAsync(
            name,
            async span =>
            {
                if (attributes is not null && attributes.Count > 0)
                {
                    span.AddAttributes(attributes);
                }

                try
                {
                    var result = await body(span);
                    span.SetStatus("ok");
                    return result;
                }
                catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
                {
                    span.SetStatus("error", e.Message);
                    span.RecordException(e);
                    throw;
                }
            },
            cancellationToken);
    }

    // PORT: the form for a body with no result, as Tracer.StartSpanAsync has. Python has one _trace_phase for both.
    private static async Task TracePhaseAsync(
        Tracer searchTracer,
        string name,
        OrderedDictionary<string, object?>? attributes,
        Func<TracerSpan, Task> body,
        CancellationToken cancellationToken = default)
    {
        await TracePhaseAsync<object?>(
            searchTracer,
            name,
            attributes,
            async span =>
            {
                await body(span);
                return null;
            },
            cancellationToken);
    }

    // PORT: the four searches run through the four-lambda form of Helpers.SemaphoreGatherAsync (R8).
    internal static async Task<SearchResults> SearchAsync(
        GraphitiClients clients,
        string query,
        List<string>? groupIds,
        SearchConfig config,
        SearchFilters searchFilter,
        string? centerNodeUuid = null,
        List<string>? bfsOriginNodeUuids = null,
        List<double>? queryVector = null,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        var start = Py.TimeSeconds();
        Helpers.ValidateGroupIds(groupIds);

        // PORT: `driver or clients.driver`. A GraphDriver has no __bool__ or __len__, so only None is false.
        driver ??= clients.Driver;
        var embedder = clients.Embedder;
        var crossEncoder = clients.CrossEncoder;
        var searchTracer = ResolveTracer(clients.Tracer);
        var maxCoroutines = clients.MaxCoroutines;

        if (PyText.Strip(query) == string.Empty)
        {
            return new SearchResults();
        }

        List<double> searchVector;
        if (
            (config.EdgeConfig is not null
                && config.EdgeConfig.SearchMethods.Contains(EdgeSearchMethod.CosineSimilarity))
            || (config.EdgeConfig is not null && EdgeReranker.Mmr == config.EdgeConfig.Reranker)
            || (config.NodeConfig is not null
                && config.NodeConfig.SearchMethods.Contains(NodeSearchMethod.CosineSimilarity))
            || (config.NodeConfig is not null && NodeReranker.Mmr == config.NodeConfig.Reranker)
            || (config.CommunityConfig is not null
                && config.CommunityConfig.SearchMethods.Contains(CommunitySearchMethod.CosineSimilarity))
            || (config.CommunityConfig is not null && CommunityReranker.Mmr == config.CommunityConfig.Reranker))
        {
            searchVector = await TracePhaseAsync(
                searchTracer,
                "search.embed_query_vector",
                new OrderedDictionary<string, object?>
                {
                    ["query.length"] = Py.Len(query),
                    ["query_vector.provided"] = queryVector is not null,
                },
                async span =>
                {
                    var searchVector = queryVector is not null
                        ? queryVector
                        : await embedder.CreateAsync([PyText.Replace(query, "\n", " ")], cancellationToken);
                    span.AddAttributes(new OrderedDictionary<string, object?> { ["query_vector.dimension"] = (long)searchVector.Count });
                    return searchVector;
                },
                cancellationToken);
        }
        else
        {
            // PORT: `[0.0] * EMBEDDING_DIM`. A count below 1 gives an empty list, as in Python.
            searchVector = [];
            for (long i = 0; i < Embedder.Client.EmbeddingDim; i++)
            {
                searchVector.Add(0.0);
            }
        }

        // if group_ids is empty, set it to None
        groupIds = groupIds is { Count: > 0 } && !(groupIds.Count == 1 && groupIds[0] == string.Empty) ? groupIds : null;
        var (
            (edges, edgeRerankerScores),
            (nodes, nodeRerankerScores),
            (episodes, episodeRerankerScores),
            (communities, communityRerankerScores)) = await TracePhaseAsync(
            searchTracer,
            "search.execute_scopes",
            new OrderedDictionary<string, object?>
            {
                ["group_id.count"] = (long)(groupIds ?? []).Count,
                ["scope.edges"] = config.EdgeConfig is not null,
                ["scope.nodes"] = config.NodeConfig is not null,
                ["scope.episodes"] = config.EpisodeConfig is not null,
                ["scope.communities"] = config.CommunityConfig is not null,
                ["limit"] = config.Limit,
            },
            async span =>
            {
                var scopes = await Helpers.SemaphoreGatherAsync(
                    () => EdgeSearchAsync(
                        driver,
                        crossEncoder,
                        query,
                        searchVector,
                        groupIds,
                        config.EdgeConfig,
                        searchFilter,
                        centerNodeUuid,
                        bfsOriginNodeUuids,
                        config.Limit,
                        config.RerankerMinScore,
                        searchTracer,
                        maxCoroutines: maxCoroutines,
                        cancellationToken: cancellationToken),
                    () => NodeSearchAsync(
                        driver,
                        crossEncoder,
                        query,
                        searchVector,
                        groupIds,
                        config.NodeConfig,
                        searchFilter,
                        centerNodeUuid,
                        bfsOriginNodeUuids,
                        config.Limit,
                        config.RerankerMinScore,
                        searchTracer,
                        maxCoroutines: maxCoroutines,
                        cancellationToken: cancellationToken),
                    () => EpisodeSearchAsync(
                        driver,
                        crossEncoder,
                        query,
                        searchVector,
                        groupIds,
                        config.EpisodeConfig,
                        searchFilter,
                        config.Limit,
                        config.RerankerMinScore,
                        searchTracer,
                        maxCoroutines: maxCoroutines,
                        cancellationToken: cancellationToken),
                    () => CommunitySearchAsync(
                        driver,
                        crossEncoder,
                        query,
                        searchVector,
                        groupIds,
                        config.CommunityConfig,
                        config.Limit,
                        config.RerankerMinScore,
                        searchTracer,
                        maxCoroutines: maxCoroutines,
                        cancellationToken: cancellationToken),
                    maxCoroutines: maxCoroutines,
                    cancellationToken: cancellationToken);
                span.AddAttributes(new OrderedDictionary<string, object?>
                {
                    ["result.edges"] = (long)scopes.Item1.Item1.Count,
                    ["result.nodes"] = (long)scopes.Item2.Item1.Count,
                    ["result.episodes"] = (long)scopes.Item3.Item1.Count,
                    ["result.communities"] = (long)scopes.Item4.Item1.Count,
                });
                return scopes;
            },
            cancellationToken);

        var results = new SearchResults
        {
            Edges = edges,
            EdgeRerankerScores = edgeRerankerScores,
            Nodes = nodes,
            NodeRerankerScores = nodeRerankerScores,
            Episodes = episodes,
            EpisodeRerankerScores = episodeRerankerScores,
            Communities = communities,
            CommunityRerankerScores = communityRerankerScores,
        };

        var latency = (Py.TimeSeconds() - start) * 1000;

        var logger = Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var ms = Py.Str(latency);
            Log.SearchReturnedContext(logger, ms);
        }

        return results;
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    internal static async Task<(List<EntityEdge>, List<double>)> EdgeSearchAsync(
        GraphDriver driver,
        CrossEncoderClient crossEncoder,
        string query,
        List<double> queryVector,
        List<string>? groupIds,
        EdgeSearchConfig? config,
        SearchFilters searchFilter,
        string? centerNodeUuid = null,
        List<string>? bfsOriginNodeUuids = null,
        long limit = SearchConfigModule.DefaultSearchLimit,
        double rerankerMinScore = 0,
        Tracer? searchTracer = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            return ([], []);
        }

        var tracer = ResolveTracer(searchTracer);

        return await TracePhaseAsync(
            tracer,
            "search.edge_search",
            new OrderedDictionary<string, object?>
            {
                ["limit"] = limit,
                ["reranker"] = EnumValue(config.Reranker),
                ["search_methods"] = config.SearchMethods.Select(method => (object?)EnumValue(method)).ToList(),
                ["bfs_origin_count"] = (long)(bfsOriginNodeUuids ?? []).Count,
                ["center_node_uuid.provided"] = centerNodeUuid is not null,
            },
            async span =>
            {
                // Build search tasks based on configured search methods
                List<Func<Task<List<EntityEdge>>>> searchTasks = [];
                if (config.SearchMethods.Contains(EdgeSearchMethod.Bm25))
                {
                    searchTasks.Add(
                        () => SearchUtils.EdgeFulltextSearchAsync(driver, query, searchFilter, groupIds, 2 * limit, cancellationToken));
                }

                if (config.SearchMethods.Contains(EdgeSearchMethod.CosineSimilarity))
                {
                    searchTasks.Add(
                        () => SearchUtils.EdgeSimilaritySearchAsync(
                            driver,
                            queryVector,
                            null,
                            null,
                            searchFilter,
                            groupIds,
                            2 * limit,
                            config.SimMinScore,
                            cancellationToken));
                }

                if (config.SearchMethods.Contains(EdgeSearchMethod.Bfs))
                {
                    searchTasks.Add(
                        () => SearchUtils.EdgeBfsSearchAsync(
                            driver,
                            bfsOriginNodeUuids,
                            config.BfsMaxDepth,
                            searchFilter,
                            groupIds,
                            2 * limit,
                            cancellationToken));
                }

                // Execute only the configured search methods
                List<List<EntityEdge>> searchResults = [];
                if (searchTasks.Count > 0)
                {
                    await TracePhaseAsync(
                        tracer,
                        "search.edge_search.execute_methods",
                        new OrderedDictionary<string, object?>
                        {
                            ["method_count"] = (long)searchTasks.Count,
                            ["candidate_limit"] = 2 * limit,
                        },
                        async methodSpan =>
                        {
                            searchResults = [.. await Helpers.SemaphoreGatherAsync(searchTasks, maxCoroutines, cancellationToken)];
                            methodSpan.AddAttributes(new OrderedDictionary<string, object?>
                            {
                                ["result_set_count"] = (long)searchResults.Count,
                                ["non_empty_result_sets"] = (long)searchResults.Count(result => result.Count > 0),
                            });
                        },
                        cancellationToken);
                }

                if (config.SearchMethods.Contains(EdgeSearchMethod.Bfs) && bfsOriginNodeUuids is null)
                {
                    List<string> sourceNodeUuids = [.. searchResults.SelectMany(result => result).Select(edge => edge.SourceNodeUuid)];
                    await TracePhaseAsync(
                        tracer,
                        "search.edge_search.expand_bfs",
                        new OrderedDictionary<string, object?>
                        {
                            ["origin_node_count"] = (long)sourceNodeUuids.Count,
                            ["candidate_limit"] = 2 * limit,
                        },
                        async _ =>
                        {
                            searchResults.Add(
                                await SearchUtils.EdgeBfsSearchAsync(
                                    driver,
                                    sourceNodeUuids,
                                    config.BfsMaxDepth,
                                    searchFilter,
                                    groupIds,
                                    2 * limit,
                                    cancellationToken));
                        },
                        cancellationToken);
                }

                var edgeUuidMap = new OrderedDictionary<string, EntityEdge>(StringComparer.Ordinal);
                foreach (var result in searchResults)
                {
                    foreach (var edge in result)
                    {
                        edgeUuidMap[edge.Uuid] = edge;
                    }
                }

                List<string> rerankedUuids = [];
                List<double> edgeScores = [];
                await TracePhaseAsync(
                    tracer,
                    "search.edge_search.rerank",
                    new OrderedDictionary<string, object?>
                    {
                        ["candidate_count"] = (long)edgeUuidMap.Count,
                        ["result_set_count"] = (long)searchResults.Count,
                        ["reranker"] = EnumValue(config.Reranker),
                    },
                    async _ =>
                    {
                        if (
                            config.Reranker == EdgeReranker.Rrf
                            || config.Reranker == EdgeReranker.EpisodeMentions)
                        {
                            List<List<string>> searchResultUuids = [.. searchResults.Select(result => result.Select(edge => edge.Uuid).ToList())];

                            (rerankedUuids, edgeScores) = SearchUtils.Rrf(searchResultUuids, minScore: rerankerMinScore);
                        }
                        else if (config.Reranker == EdgeReranker.Mmr)
                        {
                            var searchResultUuidsAndVectors = await TracePhaseAsync(
                                tracer,
                                "search.edge_search.load_embeddings",
                                new OrderedDictionary<string, object?> { ["candidate_count"] = (long)edgeUuidMap.Count },
                                async _ => await SearchUtils.GetEmbeddingsForEdgesAsync(
                                    driver, [.. edgeUuidMap.Values], cancellationToken),
                                cancellationToken);
                            await TracePhaseAsync(
                                tracer,
                                "search.edge_search.compute_mmr",
                                new OrderedDictionary<string, object?> { ["candidate_count"] = (long)searchResultUuidsAndVectors.Count },
                                _ =>
                                {
                                    (rerankedUuids, edgeScores) = SearchUtils.MaximalMarginalRelevance(
                                        queryVector,
                                        searchResultUuidsAndVectors,
                                        config.MmrLambda,
                                        rerankerMinScore);
                                    return Task.CompletedTask;
                                },
                                cancellationToken);
                        }
                        else if (config.Reranker == EdgeReranker.CrossEncoder)
                        {
                            List<List<string>> searchResultUuids = [.. searchResults.Select(result => result.Select(edge => edge.Uuid).ToList())];
                            var (rrfResultUuids, _) = SearchUtils.Rrf(searchResultUuids, minScore: rerankerMinScore);
                            var rrfEdges = Py.Slice(rrfResultUuids.Select(uuid => edgeUuidMap[uuid]).ToList(), null, 2 * limit);
                            var factToUuidMap = new OrderedDictionary<string, string>(StringComparer.Ordinal);
                            foreach (var edge in rrfEdges)
                            {
                                factToUuidMap[edge.Fact] = edge.Uuid;
                            }

                            var rerankedFacts = await TracePhaseAsync(
                                tracer,
                                "search.edge_search.cross_encoder_rank",
                                new OrderedDictionary<string, object?> { ["candidate_count"] = (long)factToUuidMap.Count },
                                async _ => await crossEncoder.RankAsync(query, [.. factToUuidMap.Keys], cancellationToken),
                                cancellationToken);
                            rerankedUuids =
                            [
                                .. rerankedFacts
                                    .Where(pair => pair.Item2 >= rerankerMinScore)
                                    .Select(pair => factToUuidMap[pair.Item1]),
                            ];
                            edgeScores = [.. rerankedFacts.Select(pair => pair.Item2).Where(score => score >= rerankerMinScore)];
                        }
                        else if (config.Reranker == EdgeReranker.NodeDistance)
                        {
                            if (centerNodeUuid is null)
                            {
                                throw new SearchRerankerException("No center node provided for Node Distance reranker");
                            }

                            // PORT: the lambdas below lose the null state of the captured parameter.
                            string centerUuid = centerNodeUuid;

                            var sortedResultUuids = await TracePhaseAsync(
                                tracer,
                                "search.edge_search.seed_rrf",
                                new OrderedDictionary<string, object?> { ["result_set_count"] = (long)searchResults.Count },
                                _ =>
                                {
                                    var (sorted, _) = SearchUtils.Rrf(
                                        [.. searchResults.Select(result => result.Select(edge => edge.Uuid).ToList())],
                                        minScore: rerankerMinScore);
                                    return Task.FromResult(sorted);
                                },
                                cancellationToken);
                            List<EntityEdge> sortedResults = [.. sortedResultUuids.Select(uuid => edgeUuidMap[uuid])];

                            // PORT: defaultdict(list) is an OrderedDictionary with Py.SetDefault (R14).
                            var sourceToEdgeUuidMap = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
                            foreach (var edge in sortedResults)
                            {
                                Py.SetDefault(sourceToEdgeUuidMap, edge.SourceNodeUuid, []).Add(edge.Uuid);
                            }

                            List<string> sourceUuids = [.. sourceToEdgeUuidMap.Keys];

                            var (rerankedNodeUuids, scores) = await TracePhaseAsync(
                                tracer,
                                "search.edge_search.node_distance_rank",
                                new OrderedDictionary<string, object?>
                                {
                                    ["source_node_count"] = (long)sourceUuids.Count,
                                    ["center_node_uuid.provided"] = centerNodeUuid is not null,
                                },
                                async _ => await SearchUtils.NodeDistanceRerankerAsync(
                                    driver, sourceUuids, centerUuid, minScore: rerankerMinScore, cancellationToken: cancellationToken),
                                cancellationToken);
                            edgeScores = scores;

                            foreach (var nodeUuid in rerankedNodeUuids)
                            {
                                // PORT: a read of a missing key of a defaultdict stores an empty list.
                                rerankedUuids.AddRange(Py.SetDefault(sourceToEdgeUuidMap, nodeUuid, []));
                            }
                        }
                    },
                    cancellationToken);

                List<EntityEdge> rerankedEdges = [.. rerankedUuids.Select(uuid => edgeUuidMap[uuid])];

                if (config.Reranker == EdgeReranker.EpisodeMentions)
                {
                    Py.SortInPlace(rerankedEdges, edge => (long)edge.Episodes.Count, reverse: true);
                }

                span.AddAttributes(new OrderedDictionary<string, object?>
                {
                    ["candidate_count"] = (long)edgeUuidMap.Count,
                    ["reranked_count"] = (long)rerankedEdges.Count,
                    ["returned_count"] = Math.Min((long)rerankedEdges.Count, limit),
                });

                return (Py.Slice(rerankedEdges, null, limit), Py.Slice(edgeScores, null, limit));
            },
            cancellationToken);
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    internal static async Task<(List<EntityNode>, List<double>)> NodeSearchAsync(
        GraphDriver driver,
        CrossEncoderClient crossEncoder,
        string query,
        List<double> queryVector,
        List<string>? groupIds,
        NodeSearchConfig? config,
        SearchFilters searchFilter,
        string? centerNodeUuid = null,
        List<string>? bfsOriginNodeUuids = null,
        long limit = SearchConfigModule.DefaultSearchLimit,
        double rerankerMinScore = 0,
        Tracer? searchTracer = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            return ([], []);
        }

        var tracer = ResolveTracer(searchTracer);

        return await TracePhaseAsync(
            tracer,
            "search.node_search",
            new OrderedDictionary<string, object?>
            {
                ["limit"] = limit,
                ["reranker"] = EnumValue(config.Reranker),
                ["search_methods"] = config.SearchMethods.Select(method => (object?)EnumValue(method)).ToList(),
                ["bfs_origin_count"] = (long)(bfsOriginNodeUuids ?? []).Count,
                ["center_node_uuid.provided"] = centerNodeUuid is not null,
            },
            async span =>
            {
                // Build search tasks based on configured search methods
                List<Func<Task<List<EntityNode>>>> searchTasks = [];
                if (config.SearchMethods.Contains(NodeSearchMethod.Bm25))
                {
                    searchTasks.Add(
                        () => SearchUtils.NodeFulltextSearchAsync(driver, query, searchFilter, groupIds, 2 * limit, cancellationToken));
                }

                if (config.SearchMethods.Contains(NodeSearchMethod.CosineSimilarity))
                {
                    searchTasks.Add(
                        () => SearchUtils.NodeSimilaritySearchAsync(
                            driver,
                            queryVector,
                            searchFilter,
                            groupIds,
                            2 * limit,
                            config.SimMinScore,
                            cancellationToken));
                }

                if (config.SearchMethods.Contains(NodeSearchMethod.Bfs))
                {
                    searchTasks.Add(
                        () => SearchUtils.NodeBfsSearchAsync(
                            driver,
                            bfsOriginNodeUuids,
                            searchFilter,
                            config.BfsMaxDepth,
                            groupIds,
                            2 * limit,
                            cancellationToken));
                }

                // Execute only the configured search methods
                List<List<EntityNode>> searchResults = [];
                if (searchTasks.Count > 0)
                {
                    await TracePhaseAsync(
                        tracer,
                        "search.node_search.execute_methods",
                        new OrderedDictionary<string, object?>
                        {
                            ["method_count"] = (long)searchTasks.Count,
                            ["candidate_limit"] = 2 * limit,
                        },
                        async methodSpan =>
                        {
                            searchResults = [.. await Helpers.SemaphoreGatherAsync(searchTasks, maxCoroutines, cancellationToken)];
                            methodSpan.AddAttributes(new OrderedDictionary<string, object?>
                            {
                                ["result_set_count"] = (long)searchResults.Count,
                                ["non_empty_result_sets"] = (long)searchResults.Count(result => result.Count > 0),
                            });
                        },
                        cancellationToken);
                }

                if (config.SearchMethods.Contains(NodeSearchMethod.Bfs) && bfsOriginNodeUuids is null)
                {
                    List<string> originNodeUuids = [.. searchResults.SelectMany(result => result).Select(node => node.Uuid)];
                    await TracePhaseAsync(
                        tracer,
                        "search.node_search.expand_bfs",
                        new OrderedDictionary<string, object?>
                        {
                            ["origin_node_count"] = (long)originNodeUuids.Count,
                            ["candidate_limit"] = 2 * limit,
                        },
                        async _ =>
                        {
                            searchResults.Add(
                                await SearchUtils.NodeBfsSearchAsync(
                                    driver,
                                    originNodeUuids,
                                    searchFilter,
                                    config.BfsMaxDepth,
                                    groupIds,
                                    2 * limit,
                                    cancellationToken));
                        },
                        cancellationToken);
                }

                List<List<string>> searchResultUuids = [.. searchResults.Select(result => result.Select(node => node.Uuid).ToList())];
                var nodeUuidMap = new OrderedDictionary<string, EntityNode>(StringComparer.Ordinal);
                foreach (var result in searchResults)
                {
                    foreach (var node in result)
                    {
                        nodeUuidMap[node.Uuid] = node;
                    }
                }

                List<string> rerankedUuids = [];
                List<double> nodeScores = [];
                await TracePhaseAsync(
                    tracer,
                    "search.node_search.rerank",
                    new OrderedDictionary<string, object?>
                    {
                        ["candidate_count"] = (long)nodeUuidMap.Count,
                        ["result_set_count"] = (long)searchResults.Count,
                        ["reranker"] = EnumValue(config.Reranker),
                    },
                    async _ =>
                    {
                        if (config.Reranker == NodeReranker.Rrf)
                        {
                            (rerankedUuids, nodeScores) = SearchUtils.Rrf(searchResultUuids, minScore: rerankerMinScore);
                        }
                        else if (config.Reranker == NodeReranker.Mmr)
                        {
                            var searchResultUuidsAndVectors = await TracePhaseAsync(
                                tracer,
                                "search.node_search.load_embeddings",
                                new OrderedDictionary<string, object?> { ["candidate_count"] = (long)nodeUuidMap.Count },
                                async _ => await SearchUtils.GetEmbeddingsForNodesAsync(
                                    driver, [.. nodeUuidMap.Values], cancellationToken),
                                cancellationToken);

                            await TracePhaseAsync(
                                tracer,
                                "search.node_search.compute_mmr",
                                new OrderedDictionary<string, object?> { ["candidate_count"] = (long)searchResultUuidsAndVectors.Count },
                                _ =>
                                {
                                    (rerankedUuids, nodeScores) = SearchUtils.MaximalMarginalRelevance(
                                        queryVector,
                                        searchResultUuidsAndVectors,
                                        config.MmrLambda,
                                        rerankerMinScore);
                                    return Task.CompletedTask;
                                },
                                cancellationToken);
                        }
                        else if (config.Reranker == NodeReranker.CrossEncoder)
                        {
                            var nameToUuidMap = new OrderedDictionary<string, string>(StringComparer.Ordinal);
                            foreach (var node in nodeUuidMap.Values.ToList())
                            {
                                nameToUuidMap[node.Name] = node.Uuid;
                            }

                            var rerankedNodeNames = await TracePhaseAsync(
                                tracer,
                                "search.node_search.cross_encoder_rank",
                                new OrderedDictionary<string, object?> { ["candidate_count"] = (long)nameToUuidMap.Count },
                                async _ => await crossEncoder.RankAsync(query, [.. nameToUuidMap.Keys], cancellationToken),
                                cancellationToken);
                            rerankedUuids =
                            [
                                .. rerankedNodeNames
                                    .Where(pair => pair.Item2 >= rerankerMinScore)
                                    .Select(pair => nameToUuidMap[pair.Item1]),
                            ];
                            nodeScores = [.. rerankedNodeNames.Select(pair => pair.Item2).Where(score => score >= rerankerMinScore)];
                        }
                        else if (config.Reranker == NodeReranker.EpisodeMentions)
                        {
                            (rerankedUuids, nodeScores) = await TracePhaseAsync(
                                tracer,
                                "search.node_search.episode_mentions_rank",
                                new OrderedDictionary<string, object?> { ["candidate_count"] = (long)nodeUuidMap.Count },
                                async _ => await SearchUtils.EpisodeMentionsRerankerAsync(
                                    driver, searchResultUuids, minScore: rerankerMinScore, cancellationToken: cancellationToken),
                                cancellationToken);
                        }
                        else if (config.Reranker == NodeReranker.NodeDistance)
                        {
                            if (centerNodeUuid is null)
                            {
                                throw new SearchRerankerException("No center node provided for Node Distance reranker");
                            }

                            // PORT: the lambdas below lose the null state of the captured parameter.
                            string centerUuid = centerNodeUuid;

                            var seededUuids = await TracePhaseAsync(
                                tracer,
                                "search.node_search.seed_rrf",
                                new OrderedDictionary<string, object?> { ["result_set_count"] = (long)searchResults.Count },
                                _ => Task.FromResult(SearchUtils.Rrf(searchResultUuids, minScore: rerankerMinScore).Item1),
                                cancellationToken);
                            (rerankedUuids, nodeScores) = await TracePhaseAsync(
                                tracer,
                                "search.node_search.node_distance_rank",
                                new OrderedDictionary<string, object?>
                                {
                                    ["source_node_count"] = (long)seededUuids.Count,
                                    ["center_node_uuid.provided"] = centerNodeUuid is not null,
                                },
                                async _ => await SearchUtils.NodeDistanceRerankerAsync(
                                    driver,
                                    seededUuids,
                                    centerUuid,
                                    minScore: rerankerMinScore,
                                    cancellationToken: cancellationToken),
                                cancellationToken);
                        }
                    },
                    cancellationToken);

                List<EntityNode> rerankedNodes = [.. rerankedUuids.Select(uuid => nodeUuidMap[uuid])];

                span.AddAttributes(new OrderedDictionary<string, object?>
                {
                    ["candidate_count"] = (long)nodeUuidMap.Count,
                    ["reranked_count"] = (long)rerankedNodes.Count,
                    ["returned_count"] = Math.Min((long)rerankedNodes.Count, limit),
                });

                return (Py.Slice(rerankedNodes, null, limit), Py.Slice(nodeScores, null, limit));
            },
            cancellationToken);
    }

    // PORT: the parameter `_query_vector` is `queryVector` (R2). `limit` has no annotation. TYPE_HINTS.md gives int.
    internal static async Task<(List<EpisodicNode>, List<double>)> EpisodeSearchAsync(
        GraphDriver driver,
        CrossEncoderClient crossEncoder,
        string query,
        List<double> queryVector,
        List<string>? groupIds,
        EpisodeSearchConfig? config,
        SearchFilters searchFilter,
        long limit = SearchConfigModule.DefaultSearchLimit,
        double rerankerMinScore = 0,
        Tracer? searchTracer = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            return ([], []);
        }

        var tracer = ResolveTracer(searchTracer);

        return await TracePhaseAsync(
            tracer,
            "search.episode_search",
            new OrderedDictionary<string, object?>
            {
                ["limit"] = limit,
                ["reranker"] = EnumValue(config.Reranker),
                ["search_methods"] = config.SearchMethods.Select(method => (object?)EnumValue(method)).ToList(),
            },
            async span =>
            {
                var searchResults = await TracePhaseAsync(
                    tracer,
                    "search.episode_search.execute_methods",
                    new OrderedDictionary<string, object?> { ["candidate_limit"] = 2 * limit },
                    async _ =>
                    {
                        List<List<EpisodicNode>> results =
                        [
                            .. await Helpers.SemaphoreGatherAsync<List<EpisodicNode>>(
                                [
                                    () => SearchUtils.EpisodeFulltextSearchAsync(driver, query, searchFilter, groupIds, 2 * limit, cancellationToken),
                                ],
                                maxCoroutines,
                                cancellationToken),
                        ];
                        return results;
                    },
                    cancellationToken);

                List<List<string>> searchResultUuids = [.. searchResults.Select(result => result.Select(episode => episode.Uuid).ToList())];
                var episodeUuidMap = new OrderedDictionary<string, EpisodicNode>(StringComparer.Ordinal);
                foreach (var result in searchResults)
                {
                    foreach (var episode in result)
                    {
                        episodeUuidMap[episode.Uuid] = episode;
                    }
                }

                List<string> rerankedUuids = [];
                List<double> episodeScores = [];
                await TracePhaseAsync(
                    tracer,
                    "search.episode_search.rerank",
                    new OrderedDictionary<string, object?>
                    {
                        ["candidate_count"] = (long)episodeUuidMap.Count,
                        ["result_set_count"] = (long)searchResults.Count,
                        ["reranker"] = EnumValue(config.Reranker),
                    },
                    async _ =>
                    {
                        if (config.Reranker == EpisodeReranker.Rrf)
                        {
                            (rerankedUuids, episodeScores) = SearchUtils.Rrf(searchResultUuids, minScore: rerankerMinScore);
                        }
                        else if (config.Reranker == EpisodeReranker.CrossEncoder)
                        {
                            List<string> rrfResultUuids = [];
                            await TracePhaseAsync(
                                tracer,
                                "search.episode_search.seed_rrf",
                                new OrderedDictionary<string, object?> { ["result_set_count"] = (long)searchResults.Count },
                                _ =>
                                {
                                    (rrfResultUuids, episodeScores) = SearchUtils.Rrf(searchResultUuids, minScore: rerankerMinScore);
                                    return Task.CompletedTask;
                                },
                                cancellationToken);
                            var rrfResults = Py.Slice(rrfResultUuids.Select(uuid => episodeUuidMap[uuid]).ToList(), null, limit);

                            var contentToUuidMap = new OrderedDictionary<string, string>(StringComparer.Ordinal);
                            foreach (var episode in rrfResults)
                            {
                                contentToUuidMap[episode.Content] = episode.Uuid;
                            }

                            var rerankedContents = await TracePhaseAsync(
                                tracer,
                                "search.episode_search.cross_encoder_rank",
                                new OrderedDictionary<string, object?> { ["candidate_count"] = (long)contentToUuidMap.Count },
                                async _ => await crossEncoder.RankAsync(query, [.. contentToUuidMap.Keys], cancellationToken),
                                cancellationToken);
                            rerankedUuids =
                            [
                                .. rerankedContents
                                    .Where(pair => pair.Item2 >= rerankerMinScore)
                                    .Select(pair => contentToUuidMap[pair.Item1]),
                            ];
                            episodeScores = [.. rerankedContents.Select(pair => pair.Item2).Where(score => score >= rerankerMinScore)];
                        }
                    },
                    cancellationToken);

                List<EpisodicNode> rerankedEpisodes = [.. rerankedUuids.Select(uuid => episodeUuidMap[uuid])];
                span.AddAttributes(new OrderedDictionary<string, object?>
                {
                    ["candidate_count"] = (long)episodeUuidMap.Count,
                    ["reranked_count"] = (long)rerankedEpisodes.Count,
                    ["returned_count"] = Math.Min((long)rerankedEpisodes.Count, limit),
                });

                return (Py.Slice(rerankedEpisodes, null, limit), Py.Slice(episodeScores, null, limit));
            },
            cancellationToken);
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    internal static async Task<(List<CommunityNode>, List<double>)> CommunitySearchAsync(
        GraphDriver driver,
        CrossEncoderClient crossEncoder,
        string query,
        List<double> queryVector,
        List<string>? groupIds,
        CommunitySearchConfig? config,
        long limit = SearchConfigModule.DefaultSearchLimit,
        double rerankerMinScore = 0,
        Tracer? searchTracer = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        if (config is null)
        {
            return ([], []);
        }

        var tracer = ResolveTracer(searchTracer);

        return await TracePhaseAsync(
            tracer,
            "search.community_search",
            new OrderedDictionary<string, object?>
            {
                ["limit"] = limit,
                ["reranker"] = EnumValue(config.Reranker),
                ["search_methods"] = config.SearchMethods.Select(method => (object?)EnumValue(method)).ToList(),
            },
            async span =>
            {
                var searchResults = await TracePhaseAsync(
                    tracer,
                    "search.community_search.execute_methods",
                    new OrderedDictionary<string, object?> { ["candidate_limit"] = 2 * limit },
                    async _ =>
                    {
                        List<List<CommunityNode>> results =
                        [
                            .. await Helpers.SemaphoreGatherAsync<List<CommunityNode>>(
                                [
                                    () => SearchUtils.CommunityFulltextSearchAsync(driver, query, groupIds, 2 * limit, cancellationToken),
                                    () => SearchUtils.CommunitySimilaritySearchAsync(
                                        driver, queryVector, groupIds, 2 * limit, config.SimMinScore, cancellationToken),
                                ],
                                maxCoroutines,
                                cancellationToken),
                        ];
                        return results;
                    },
                    cancellationToken);

                List<List<string>> searchResultUuids =
                [
                    .. searchResults.Select(result => result.Select(community => community.Uuid).ToList()),
                ];
                var communityUuidMap = new OrderedDictionary<string, CommunityNode>(StringComparer.Ordinal);
                foreach (var result in searchResults)
                {
                    foreach (var community in result)
                    {
                        communityUuidMap[community.Uuid] = community;
                    }
                }

                List<string> rerankedUuids = [];
                List<double> communityScores = [];
                await TracePhaseAsync(
                    tracer,
                    "search.community_search.rerank",
                    new OrderedDictionary<string, object?>
                    {
                        ["candidate_count"] = (long)communityUuidMap.Count,
                        ["result_set_count"] = (long)searchResults.Count,
                        ["reranker"] = EnumValue(config.Reranker),
                    },
                    async _ =>
                    {
                        if (config.Reranker == CommunityReranker.Rrf)
                        {
                            (rerankedUuids, communityScores) = SearchUtils.Rrf(searchResultUuids, minScore: rerankerMinScore);
                        }
                        else if (config.Reranker == CommunityReranker.Mmr)
                        {
                            var searchResultUuidsAndVectors = await TracePhaseAsync(
                                tracer,
                                "search.community_search.load_embeddings",
                                new OrderedDictionary<string, object?> { ["candidate_count"] = (long)communityUuidMap.Count },
                                async _ => await SearchUtils.GetEmbeddingsForCommunitiesAsync(
                                    driver, [.. communityUuidMap.Values], cancellationToken),
                                cancellationToken);

                            await TracePhaseAsync(
                                tracer,
                                "search.community_search.compute_mmr",
                                new OrderedDictionary<string, object?> { ["candidate_count"] = (long)searchResultUuidsAndVectors.Count },
                                _ =>
                                {
                                    (rerankedUuids, communityScores) = SearchUtils.MaximalMarginalRelevance(
                                        queryVector,
                                        searchResultUuidsAndVectors,
                                        config.MmrLambda,
                                        rerankerMinScore);
                                    return Task.CompletedTask;
                                },
                                cancellationToken);
                        }
                        else if (config.Reranker == CommunityReranker.CrossEncoder)
                        {
                            var nameToUuidMap = new OrderedDictionary<string, string>(StringComparer.Ordinal);
                            foreach (var result in searchResults)
                            {
                                foreach (var node in result)
                                {
                                    nameToUuidMap[node.Name] = node.Uuid;
                                }
                            }

                            var rerankedNodes = await TracePhaseAsync(
                                tracer,
                                "search.community_search.cross_encoder_rank",
                                new OrderedDictionary<string, object?> { ["candidate_count"] = (long)nameToUuidMap.Count },
                                async _ => await crossEncoder.RankAsync(query, [.. nameToUuidMap.Keys], cancellationToken),
                                cancellationToken);
                            rerankedUuids =
                            [
                                .. rerankedNodes
                                    .Where(pair => pair.Item2 >= rerankerMinScore)
                                    .Select(pair => nameToUuidMap[pair.Item1]),
                            ];
                            communityScores = [.. rerankedNodes.Select(pair => pair.Item2).Where(score => score >= rerankerMinScore)];
                        }
                    },
                    cancellationToken);

                List<CommunityNode> rerankedCommunities = [.. rerankedUuids.Select(uuid => communityUuidMap[uuid])];
                span.AddAttributes(new OrderedDictionary<string, object?>
                {
                    ["candidate_count"] = (long)communityUuidMap.Count,
                    ["reranked_count"] = (long)rerankedCommunities.Count,
                    ["returned_count"] = Math.Min((long)rerankedCommunities.Count, limit),
                });

                return (Py.Slice(rerankedCommunities, null, limit), Py.Slice(communityScores, null, limit));
            },
            cancellationToken);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "search returned context in {Ms} ms")]
        internal static partial void SearchReturnedContext(ILogger logger, string ms);
    }
}
