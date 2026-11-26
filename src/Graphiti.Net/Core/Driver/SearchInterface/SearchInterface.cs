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

namespace Graphiti.Core.Driver;

using Graphiti.Core.Search;

// PORT: a pydantic model with subclasses and no field is an abstract record (R7). A method whose
// Python body only raises NotImplementedError is virtual and throws NotImplementedException (R4, R15). The
// package holds one module with its own name, so the class is in the namespace of the parent package.
// The nested class Config sets arbitrary_types_allowed, which needs nothing (R7).
/// <summary>
/// Interface for implementing custom search logic.
///
/// All methods use `Any` type hints to avoid circular imports. See docstrings
/// for expected concrete types.
///
/// Type reference:
///     - driver: GraphDriver
///     - search_filter: SearchFilters
///     - EntityNode, EpisodicNode, CommunityNode from graphiti_core.nodes
///     - EntityEdge from graphiti_core.edges
/// </summary>
internal abstract record SearchInterface
{
    /// <summary>
    /// Perform fulltext search over edge facts and names.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     query: Search query string
    ///     search_filter: SearchFilters instance for filtering results
    ///     group_ids: Optional list of group IDs to filter by
    ///     limit: Maximum number of results to return
    ///
    /// Returns:
    ///     list[EntityEdge]: List of matching EntityEdge objects
    /// </summary>
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

    /// <summary>
    /// Perform vector similarity search over edge fact embeddings.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     search_vector: Query embedding vector
    ///     source_node_uuid: Optional source node UUID to filter by
    ///     target_node_uuid: Optional target node UUID to filter by
    ///     search_filter: SearchFilters instance for filtering results
    ///     group_ids: Optional list of group IDs to filter by
    ///     limit: Maximum number of results to return
    ///     min_score: Minimum similarity score threshold (0.0 to 1.0)
    ///
    /// Returns:
    ///     list[EntityEdge]: List of matching EntityEdge objects
    /// </summary>
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

    /// <summary>
    /// Perform fulltext search over node names and summaries.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     query: Search query string
    ///     search_filter: SearchFilters instance for filtering results
    ///     group_ids: Optional list of group IDs to filter by
    ///     limit: Maximum number of results to return
    ///
    /// Returns:
    ///     list[EntityNode]: List of matching EntityNode objects
    /// </summary>
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

    /// <summary>
    /// Perform vector similarity search over node name embeddings.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     search_vector: Query embedding vector
    ///     search_filter: SearchFilters instance for filtering results
    ///     group_ids: Optional list of group IDs to filter by
    ///     limit: Maximum number of results to return
    ///     min_score: Minimum similarity score threshold (0.0 to 1.0)
    ///
    /// Returns:
    ///     list[EntityNode]: List of matching EntityNode objects
    /// </summary>
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

    /// <summary>
    /// Perform fulltext search over episode content.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     query: Search query string
    ///     search_filter: SearchFilters instance (kept for interface parity)
    ///     group_ids: Optional list of group IDs to filter by
    ///     limit: Maximum number of results to return
    ///
    /// Returns:
    ///     list[EpisodicNode]: List of matching EpisodicNode objects
    /// </summary>
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

    /// <summary>
    /// Perform breadth-first search for edges starting from origin nodes.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     bfs_origin_node_uuids: List of starting node UUIDs (Entity or Episodic).
    ///         Returns empty list if None or empty.
    ///     bfs_max_depth: Maximum traversal depth (must be &gt;= 1)
    ///     search_filter: SearchFilters instance for filtering results
    ///     group_ids: Optional list of group IDs to filter by
    ///     limit: Maximum number of results to return
    ///
    /// Returns:
    ///     list[EntityEdge]: List of EntityEdge objects found within the search depth
    /// </summary>
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

    /// <summary>
    /// Perform breadth-first search for nodes starting from origin nodes.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     bfs_origin_node_uuids: List of starting node UUIDs (Entity or Episodic).
    ///         Returns empty list if None or empty.
    ///     search_filter: SearchFilters instance for filtering results
    ///     bfs_max_depth: Maximum traversal depth (must be &gt;= 1, returns empty if &lt; 1)
    ///     group_ids: Optional list of group IDs to filter by
    ///     limit: Maximum number of results to return
    ///
    /// Returns:
    ///     list[EntityNode]: List of EntityNode objects found within the search depth
    /// </summary>
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

    /// <summary>
    /// Perform fulltext search over community names.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     query: Search query string
    ///     group_ids: Optional list of group IDs to filter by
    ///     limit: Maximum number of results to return
    ///
    /// Returns:
    ///     list[CommunityNode]: List of matching CommunityNode objects
    /// </summary>
    internal virtual Task<List<CommunityNode>> CommunityFulltextSearchAsync(
        GraphDriver driver,
        string query,
        List<string>? groupIds = null,
        long limit = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Perform vector similarity search over community name embeddings.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     search_vector: Query embedding vector
    ///     group_ids: Optional list of group IDs to filter by
    ///     limit: Maximum number of results to return
    ///     min_score: Minimum similarity score threshold (0.0 to 1.0)
    ///
    /// Returns:
    ///     list[CommunityNode]: List of matching CommunityNode objects
    /// </summary>
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

    /// <summary>
    /// Load name embeddings for a list of community nodes.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     communities: List of CommunityNode objects to load embeddings for
    ///
    /// Returns:
    ///     dict[str, list[float]]: Mapping of community UUID to name embedding vector
    /// </summary>
    internal virtual Task<OrderedDictionary<string, List<double>>> GetEmbeddingsForCommunitiesAsync(
        GraphDriver driver,
        List<CommunityNode> communities,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Rerank nodes by their graph distance to a center node.
    ///
    /// Nodes directly connected to the center node get score 1.0, the center node
    /// itself gets score 0.1 (if in the input list), and unconnected nodes get
    /// score approaching 0 (1/infinity).
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     node_uuids: List of node UUIDs to rerank. The center_node_uuid will be
    ///         filtered out during processing but included in results if present.
    ///     center_node_uuid: UUID of the center node to measure distances from
    ///     min_score: Minimum score threshold. Nodes with 1/distance &lt; min_score
    ///         are excluded from results.
    ///
    /// Returns:
    ///     tuple[list[str], list[float]]: Tuple of (sorted_uuids, scores) where
    ///         scores are 1/distance values, sorted by distance ascending
    /// </summary>
    internal virtual Task<(List<string>, List<double>)> NodeDistanceRerankerAsync(
        GraphDriver driver,
        List<string> nodeUuids,
        string centerNodeUuid,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Rerank nodes by their episode mention count.
    ///
    /// Uses RRF (Reciprocal Rank Fusion) as a preliminary ranker, then reranks
    /// by the number of episodes that mention each node.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     node_uuids: List of ranked UUID lists (e.g., from multiple search results)
    ///         to be merged and reranked
    ///     min_score: Minimum mention count threshold. Nodes with fewer mentions
    ///         are excluded from results.
    ///
    /// Returns:
    ///     tuple[list[str], list[float]]: Tuple of (sorted_uuids, mention_counts)
    ///         sorted by mention count descending
    /// </summary>
    internal virtual Task<(List<string>, List<double>)> EpisodeMentionsRerankerAsync(
        GraphDriver driver,
        List<List<string>> nodeUuids,
        double minScore = 0,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // ---------- SEARCH FILTERS (sync) ----------

    /// <summary>
    /// Build provider-specific node search filters.
    ///
    /// Args:
    ///     search_filters: SearchFilters instance
    ///
    /// Returns:
    ///     Provider-specific filter representation
    /// </summary>
    internal virtual object? BuildNodeSearchFilters(SearchFilters searchFilters)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Build provider-specific edge search filters.
    ///
    /// Args:
    ///     search_filters: SearchFilters instance
    ///
    /// Returns:
    ///     Provider-specific filter representation
    /// </summary>
    internal virtual object? BuildEdgeSearchFilters(SearchFilters searchFilters)
    {
        throw new NotImplementedException(string.Empty);
    }
}
