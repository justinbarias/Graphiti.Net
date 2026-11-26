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
using Graphiti.Core.Search;

// PORT: the module class of R2. The module logger has no log call, so it has no Logger (R5).
internal static class SearchOps
{
    internal const long MaxQueryLength = 128;

    // TODO(port): _build_neo4j_fulltext_query
    internal static string BuildNeo4jFulltextQuery(
        string query,
        List<string>? groupIds = null,
        long maxQueryLength = MaxQueryLength)
    {
        throw new NotSupportedException("TODO(port): _build_neo4j_fulltext_query");
    }
}

internal sealed class Neo4jSearchOperations : SearchOperations
{
    // TODO(port): Neo4jSearchOperations.node_fulltext_search
    internal override Task<List<EntityNode>> NodeFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.node_fulltext_search");
    }

    // TODO(port): Neo4jSearchOperations.node_similarity_search
    internal override Task<List<EntityNode>> NodeSimilaritySearchAsync(
        QueryExecutor executor,
        List<double> searchVector,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        double minScore = 0.6,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.node_similarity_search");
    }

    // TODO(port): Neo4jSearchOperations.node_bfs_search
    internal override Task<List<EntityNode>> NodeBfsSearchAsync(
        QueryExecutor executor,
        List<string> originUuids,
        SearchFilters searchFilter,
        long maxDepth,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.node_bfs_search");
    }

    // TODO(port): Neo4jSearchOperations.edge_fulltext_search
    internal override Task<List<EntityEdge>> EdgeFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.edge_fulltext_search");
    }

    // TODO(port): Neo4jSearchOperations.edge_similarity_search
    internal override Task<List<EntityEdge>> EdgeSimilaritySearchAsync(
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
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.edge_similarity_search");
    }

    // TODO(port): Neo4jSearchOperations.edge_bfs_search
    internal override Task<List<EntityEdge>> EdgeBfsSearchAsync(
        QueryExecutor executor,
        List<string> originUuids,
        long maxDepth,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.edge_bfs_search");
    }

    // TODO(port): Neo4jSearchOperations.episode_fulltext_search
    internal override Task<List<EpisodicNode>> EpisodeFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.episode_fulltext_search");
    }

    // TODO(port): Neo4jSearchOperations.community_fulltext_search
    internal override Task<List<CommunityNode>> CommunityFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.community_fulltext_search");
    }

    // TODO(port): Neo4jSearchOperations.community_similarity_search
    internal override Task<List<CommunityNode>> CommunitySimilaritySearchAsync(
        QueryExecutor executor,
        List<double> searchVector,
        List<string>? groupIds = null,
        long limit = 10,
        double minScore = 0.6,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.community_similarity_search");
    }

    // TODO(port): Neo4jSearchOperations.node_distance_reranker
    internal override Task<List<EntityNode>> NodeDistanceRerankerAsync(
        QueryExecutor executor,
        List<string> nodeUuids,
        string centerNodeUuid,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.node_distance_reranker");
    }

    // TODO(port): Neo4jSearchOperations.episode_mentions_reranker
    internal override Task<List<EntityNode>> EpisodeMentionsRerankerAsync(
        QueryExecutor executor,
        List<string> nodeUuids,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.episode_mentions_reranker");
    }

    // TODO(port): Neo4jSearchOperations.build_node_search_filters
    internal override object? BuildNodeSearchFilters(SearchFilters searchFilters)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.build_node_search_filters");
    }

    // TODO(port): Neo4jSearchOperations.build_edge_search_filters
    internal override object? BuildEdgeSearchFilters(SearchFilters searchFilters)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.build_edge_search_filters");
    }

    // TODO(port): Neo4jSearchOperations.build_fulltext_query
    internal override string BuildFulltextQuery(
        string query,
        List<string>? groupIds = null,
        long maxQueryLength = 8000)
    {
        throw new NotSupportedException("TODO(port): Neo4jSearchOperations.build_fulltext_query");
    }
}
