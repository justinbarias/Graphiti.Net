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
using Graphiti.Internal;
using Microsoft.Extensions.Logging;

// PORT: each provider branch (FalkorDB, Neptune) stays in its function (R1). A function that tries the driver's
// search_interface or graph_operations_interface first keeps that call and its `except NotImplementedError` (R4).
internal static class SearchUtils
{
    private static ILogger Logger => PortServices.Logger(typeof(SearchUtils));

    internal const long RelevantSchemaLimit = 10;

    internal const double DefaultMinScore = 0.6;

    internal const double DefaultMmrLambda = 0.5;

    internal const long MaxSearchDepth = 3;

    internal const long MaxQueryLength = 128;

    // PORT: the zero branch returns the integer 0 in Python and a float in the other branch, so the result is double
    // (R18, GAPS F18).
    // TODO(port): calculate_cosine_similarity
    internal static double CalculateCosineSimilarity(List<double> vector1, List<double> vector2)
    {
        throw new NotSupportedException("TODO(port): calculate_cosine_similarity");
    }

    // PORT: no return annotation. TYPE_HINTS.md gives str, and the FalkorDB branch returns
    // GraphDriver.BuildFulltextQuery, which is a string.
    // TODO(port): fulltext_query
    internal static string FulltextQuery(string query, List<string>? groupIds, GraphDriver driver)
    {
        throw new NotSupportedException("TODO(port): fulltext_query");
    }

    // TODO(port): get_episodes_by_mentions
    internal static Task<List<EpisodicNode>> GetEpisodesByMentionsAsync(
        GraphDriver driver,
        List<EntityNode> nodes,
        List<EntityEdge> edges,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): get_episodes_by_mentions");
    }

    // TODO(port): get_mentioned_nodes
    internal static Task<List<EntityNode>> GetMentionedNodesAsync(
        GraphDriver driver,
        List<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): get_mentioned_nodes");
    }

    // TODO(port): get_communities_by_nodes
    internal static Task<List<CommunityNode>> GetCommunitiesByNodesAsync(
        GraphDriver driver,
        List<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): get_communities_by_nodes");
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    // TODO(port): edge_fulltext_search
    internal static Task<List<EntityEdge>> EdgeFulltextSearchAsync(
        GraphDriver driver,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): edge_fulltext_search");
    }

    // TODO(port): edge_similarity_search
    internal static Task<List<EntityEdge>> EdgeSimilaritySearchAsync(
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
        throw new NotSupportedException("TODO(port): edge_similarity_search");
    }

    // TODO(port): edge_bfs_search
    internal static Task<List<EntityEdge>> EdgeBfsSearchAsync(
        GraphDriver driver,
        List<string>? bfsOriginNodeUuids,
        long bfsMaxDepth,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): edge_bfs_search");
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    // TODO(port): node_fulltext_search
    internal static Task<List<EntityNode>> NodeFulltextSearchAsync(
        GraphDriver driver,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): node_fulltext_search");
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    // TODO(port): node_similarity_search
    internal static Task<List<EntityNode>> NodeSimilaritySearchAsync(
        GraphDriver driver,
        List<double> searchVector,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        double minScore = DefaultMinScore,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): node_similarity_search");
    }

    // TODO(port): node_bfs_search
    internal static Task<List<EntityNode>> NodeBfsSearchAsync(
        GraphDriver driver,
        List<string>? bfsOriginNodeUuids,
        SearchFilters searchFilter,
        long bfsMaxDepth,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): node_bfs_search");
    }

    // PORT: the parameter `_search_filter` is `searchFilter` (R2). `limit` has no annotation. TYPE_HINTS.md gives int.
    // TODO(port): episode_fulltext_search
    internal static Task<List<EpisodicNode>> EpisodeFulltextSearchAsync(
        GraphDriver driver,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): episode_fulltext_search");
    }

    // PORT: `limit` has no annotation. TYPE_HINTS.md gives int.
    // TODO(port): community_fulltext_search
    internal static Task<List<CommunityNode>> CommunityFulltextSearchAsync(
        GraphDriver driver,
        string query,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): community_fulltext_search");
    }

    // PORT: `limit` and `min_score` have no annotation. TYPE_HINTS.md gives int and float.
    // TODO(port): community_similarity_search
    internal static Task<List<CommunityNode>> CommunitySimilaritySearchAsync(
        GraphDriver driver,
        List<double> searchVector,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        double minScore = DefaultMinScore,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): community_similarity_search");
    }

    // TODO(port): hybrid_node_search
    internal static Task<List<EntityNode>> HybridNodeSearchAsync(
        List<string> queries,
        List<List<double>> embeddings,
        GraphDriver driver,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): hybrid_node_search");
    }

    // TODO(port): get_relevant_nodes
    internal static Task<List<List<EntityNode>>> GetRelevantNodesAsync(
        GraphDriver driver,
        List<EntityNode> nodes,
        SearchFilters searchFilter,
        double minScore = DefaultMinScore,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): get_relevant_nodes");
    }

    // TODO(port): get_relevant_edges
    internal static Task<List<List<EntityEdge>>> GetRelevantEdgesAsync(
        GraphDriver driver,
        List<EntityEdge> edges,
        SearchFilters searchFilter,
        double minScore = DefaultMinScore,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): get_relevant_edges");
    }

    // TODO(port): get_edge_invalidation_candidates
    internal static Task<List<List<EntityEdge>>> GetEdgeInvalidationCandidatesAsync(
        GraphDriver driver,
        List<EntityEdge> edges,
        SearchFilters searchFilter,
        double minScore = DefaultMinScore,
        long limit = RelevantSchemaLimit,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): get_edge_invalidation_candidates");
    }

    // takes in a list of rankings of uuids
    // PORT: `rank_const` has no annotation. TYPE_HINTS.md gives int.
    // TODO(port): rrf
    internal static (List<string>, List<double>) Rrf(List<List<string>> results, long rankConst = 1, double minScore = 0)
    {
        throw new NotSupportedException("TODO(port): rrf");
    }

    // TODO(port): node_distance_reranker
    internal static Task<(List<string>, List<double>)> NodeDistanceRerankerAsync(
        GraphDriver driver,
        List<string> nodeUuids,
        string centerNodeUuid,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): node_distance_reranker");
    }

    // TODO(port): episode_mentions_reranker
    internal static Task<(List<string>, List<double>)> EpisodeMentionsRerankerAsync(
        GraphDriver driver,
        List<List<string>> nodeUuids,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): episode_mentions_reranker");
    }

    // PORT: `candidates` is iterated with `.items()`, so it is an OrderedDictionary (R14), the result type of the
    // get_embeddings_for_* functions.
    // TODO(port): maximal_marginal_relevance
    internal static (List<string>, List<double>) MaximalMarginalRelevance(
        List<double> queryVector,
        OrderedDictionary<string, List<double>> candidates,
        double mmrLambda = DefaultMmrLambda,
        double minScore = -2.0)
    {
        throw new NotSupportedException("TODO(port): maximal_marginal_relevance");
    }

    // TODO(port): get_embeddings_for_nodes
    internal static Task<OrderedDictionary<string, List<double>>> GetEmbeddingsForNodesAsync(
        GraphDriver driver,
        List<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): get_embeddings_for_nodes");
    }

    // TODO(port): get_embeddings_for_communities
    internal static Task<OrderedDictionary<string, List<double>>> GetEmbeddingsForCommunitiesAsync(
        GraphDriver driver,
        List<CommunityNode> communities,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): get_embeddings_for_communities");
    }

    // TODO(port): get_embeddings_for_edges
    internal static Task<OrderedDictionary<string, List<double>>> GetEmbeddingsForEdgesAsync(
        GraphDriver driver,
        List<EntityEdge> edges,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): get_embeddings_for_edges");
    }
}
