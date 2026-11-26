// Ported from graphiti_core/driver/search_interface/search_interface.py
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

namespace Graphiti.Core.Driver.SearchInterface;

using Graphiti.Core.Search;

// PORT: a pydantic model with subclasses and no field is an abstract record (R7). A method whose
// Python body only raises NotImplementedError is virtual and throws NotImplementedException (R4, R15).
// The nested class Config sets arbitrary_types_allowed, which needs nothing (R7).
internal abstract record SearchInterface
{
    internal virtual Task<List<EntityEdge>> EdgeFulltextSearchAsync(
        GraphDriver driver,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<List<EntityEdge>> EdgeSimilaritySearchAsync(
        GraphDriver driver,
        List<double> searchVector,
        string? sourceNodeUuid,
        string? targetNodeUuid,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 100,
        double minScore = 0.7,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<List<EntityNode>> NodeFulltextSearchAsync(
        GraphDriver driver,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<List<EntityNode>> NodeSimilaritySearchAsync(
        GraphDriver driver,
        List<double> searchVector,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 100,
        double minScore = 0.7,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<List<EpisodicNode>> EpisodeFulltextSearchAsync(
        GraphDriver driver,
        string query,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<List<EntityEdge>> EdgeBfsSearchAsync(
        GraphDriver driver,
        List<string>? bfsOriginNodeUuids,
        long bfsMaxDepth,
        SearchFilters searchFilter,
        List<string>? groupIds = null,
        long limit = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<List<EntityNode>> NodeBfsSearchAsync(
        GraphDriver driver,
        List<string>? bfsOriginNodeUuids,
        SearchFilters searchFilter,
        long bfsMaxDepth,
        List<string>? groupIds = null,
        long limit = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<List<CommunityNode>> CommunityFulltextSearchAsync(
        GraphDriver driver,
        string query,
        List<string>? groupIds = null,
        long limit = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<List<CommunityNode>> CommunitySimilaritySearchAsync(
        GraphDriver driver,
        List<double> searchVector,
        List<string>? groupIds = null,
        long limit = 100,
        double minScore = 0.6,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<OrderedDictionary<string, List<double>>> GetEmbeddingsForCommunitiesAsync(
        GraphDriver driver,
        List<CommunityNode> communities,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<(List<string>, List<double>)> NodeDistanceRerankerAsync(
        GraphDriver driver,
        List<string> nodeUuids,
        string centerNodeUuid,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<(List<string>, List<double>)> EpisodeMentionsRerankerAsync(
        GraphDriver driver,
        List<List<string>> nodeUuids,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // ---------- SEARCH FILTERS (sync) ----------

    internal virtual object? BuildNodeSearchFilters(SearchFilters searchFilters)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual object? BuildEdgeSearchFilters(SearchFilters searchFilters)
    {
        throw new NotImplementedException(string.Empty);
    }
}
