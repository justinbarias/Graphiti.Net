// Ported from graphiti_core/driver/operations/search_ops.py
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

namespace Graphiti.Core.Driver.Operations;

using Graphiti.Core.Search;

// PORT: an abstract method has no body, as in Python.
internal abstract class SearchOperations
{
    // Node search

    internal abstract Task<List<EntityNode>> NodeFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EntityNode>> NodeSimilaritySearchAsync(
        QueryExecutor executor,
        List<double> searchVector,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        double minScore = 0.6,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EntityNode>> NodeBfsSearchAsync(
        QueryExecutor executor,
        List<string> originUuids,
        SearchFilters searchFilter,
        long maxDepth,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default);

    // Edge search

    internal abstract Task<List<EntityEdge>> EdgeFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EntityEdge>> EdgeSimilaritySearchAsync(
        QueryExecutor executor,
        List<double> searchVector,
        string? sourceNodeUuid,
        string? targetNodeUuid,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        double minScore = 0.6,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EntityEdge>> EdgeBfsSearchAsync(
        QueryExecutor executor,
        List<string> originUuids,
        long maxDepth,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default);

    // Episode search

    internal abstract Task<List<EpisodicNode>> EpisodeFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default);

    // Community search

    internal abstract Task<List<CommunityNode>> CommunityFulltextSearchAsync(
        QueryExecutor executor,
        string query,
        List<string>? groupIds = null,
        long limit = 10,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<CommunityNode>> CommunitySimilaritySearchAsync(
        QueryExecutor executor,
        List<double> searchVector,
        List<string>? groupIds = null,
        long limit = 10,
        double minScore = 0.6,
        CancellationToken cancellationToken = default);

    // Rerankers

    internal abstract Task<List<EntityNode>> NodeDistanceRerankerAsync(
        QueryExecutor executor,
        List<string> nodeUuids,
        string centerNodeUuid,
        double minScore = 0,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EntityNode>> EpisodeMentionsRerankerAsync(
        QueryExecutor executor,
        List<string> nodeUuids,
        double minScore = 0,
        CancellationToken cancellationToken = default);

    // Filter builders (sync)

    internal abstract object? BuildNodeSearchFilters(SearchFilters searchFilters);

    internal abstract object? BuildEdgeSearchFilters(SearchFilters searchFilters);

    // Fulltext query builder

    internal abstract string BuildFulltextQuery(
        string query,
        List<string>? groupIds = null,
        long maxQueryLength = 8000);
}
