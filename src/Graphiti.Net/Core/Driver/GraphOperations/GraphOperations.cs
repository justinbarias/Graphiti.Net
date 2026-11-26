// Ported from graphiti_core/driver/graph_operations/graph_operations.py
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
using Graphiti.PyCompat;

// PORT: a pydantic model with subclasses and no field is an abstract record (R7). A method whose
// Python body only raises NotImplementedError is virtual and throws NotImplementedException (R4, R15). The
// package holds one module with its own name, so the class is in the namespace of the parent package.
/// <summary>
/// Interface for updating graph mutation behavior.
///
/// All methods use `Any` type hints to avoid circular imports. See docstrings
/// for expected concrete types.
///
/// Type reference:
///     - driver: GraphDriver
///     - EntityNode, EpisodicNode, CommunityNode, SagaNode from graphiti_core.nodes
///     - EntityEdge, EpisodicEdge, CommunityEdge from graphiti_core.edges
///     - EpisodeType from graphiti_core.nodes
/// </summary>
internal abstract record GraphOperationsInterface
{
    // -----------------
    // Node: Save/Delete
    // -----------------

    /// <summary>
    /// Persist (create or update) a single node.
    /// </summary>
    internal virtual Task NodeSaveAsync(
        EntityNode node,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task NodeDeleteAsync(
        Node node,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Persist (create or update) many nodes in batches.
    /// </summary>
    internal virtual Task NodeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        Transaction transaction,
        List<OrderedDictionary<string, object?>> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Update many nodes that exist. A node that does not exist is not created.
    ///
    /// `nodes` has the same records as `node_save_bulk`. Concurrent ingestion uses this
    /// operation.
    /// </summary>
    internal virtual Task NodeUpdateBulkAsync(
        Type? cls,
        GraphDriver driver,
        Transaction transaction,
        List<OrderedDictionary<string, object?>> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task NodeDeleteByGroupIdAsync(
        Type? cls,
        GraphDriver driver,
        string groupId,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task NodeDeleteByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------
    // Node: Read
    // -----------------

    /// <summary>
    /// Retrieve a single node by UUID.
    /// </summary>
    internal virtual Task<EntityNode> NodeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve multiple nodes by UUIDs.
    /// </summary>
    internal virtual Task<List<EntityNode>> NodeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve nodes by group IDs with optional pagination.
    /// </summary>
    internal virtual Task<List<EntityNode>> NodeGetByGroupIdsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve nodes by exact names scoped to group IDs.
    /// </summary>
    internal virtual Task<List<EntityNode>> NodeGetByNamesAsync(
        Type? cls,
        GraphDriver driver,
        List<string> names,
        List<string> groupIds,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve nodes matching all provided attributes within a group.
    /// </summary>
    internal virtual Task<List<EntityNode>> NodeGetByAttributeValuesAsync(
        Type? cls,
        GraphDriver driver,
        string groupId,
        OrderedDictionary<string, object?> attributes,
        List<string>? nodeLabels = null,
        long limit = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // --------------------------
    // Node: Embeddings (load)
    // --------------------------

    /// <summary>
    /// Load embedding vectors for a single node into the instance (e.g., set node.embedding or similar).
    /// </summary>
    internal virtual Task NodeLoadEmbeddingsAsync(
        EntityNode node,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Load embedding vectors for many nodes in batches.
    /// </summary>
    internal virtual Task<OrderedDictionary<string, List<double>>> NodeLoadEmbeddingsBulkAsync(
        GraphDriver driver,
        List<EntityNode> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // --------------------------
    // EpisodicNode: Save/Delete
    // --------------------------

    /// <summary>
    /// Persist (create or update) a single episodic node.
    /// </summary>
    internal virtual Task EpisodicNodeSaveAsync(
        EpisodicNode node,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task EpisodicNodeDeleteAsync(
        EpisodicNode node,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Persist (create or update) many episodic nodes in batches.
    /// </summary>
    internal virtual Task EpisodicNodeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        Transaction transaction,
        List<OrderedDictionary<string, object?>> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Persist (create or update) many episodic edges in batches.
    /// </summary>
    internal virtual Task EpisodicEdgeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        Transaction transaction,
        List<OrderedDictionary<string, object?>> episodicEdges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task EpisodicNodeDeleteByGroupIdAsync(
        Type? cls,
        GraphDriver driver,
        string groupId,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task EpisodicNodeDeleteByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------------
    // EpisodicNode: Read
    // -----------------------

    /// <summary>
    /// Retrieve a single episodic node by UUID.
    /// </summary>
    internal virtual Task<EpisodicNode> EpisodicNodeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve multiple episodic nodes by UUIDs.
    /// </summary>
    internal virtual Task<List<EpisodicNode>> EpisodicNodeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve episodic nodes by group IDs with optional pagination.
    /// </summary>
    internal virtual Task<List<EpisodicNode>> EpisodicNodeGetByGroupIdsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve the last n episodic nodes from the graph.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     reference_time: datetime object. Only episodes with valid_at &lt;= reference_time
    ///         are returned, allowing point-in-time queries.
    ///     last_n: Number of most recent episodes to retrieve (default: 3)
    ///     group_ids: Optional list of group IDs to filter by
    ///     source: Optional EpisodeType to filter by source type
    ///     saga: Optional saga name. If provided, only retrieves episodes
    ///         belonging to that saga.
    ///
    /// Returns:
    ///     list[EpisodicNode]: List of EpisodicNode objects in chronological order
    ///         (oldest first)
    /// </summary>
    internal virtual Task<List<EpisodicNode>> RetrieveEpisodesAsync(
        GraphDriver driver,
        PyDateTime referenceTime,
        long lastN = 3,
        List<string>? groupIds = null,
        EpisodeType? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------------
    // CommunityNode: Save/Delete
    // -----------------------

    /// <summary>
    /// Persist (create or update) a single community node.
    /// </summary>
    internal virtual Task CommunityNodeSaveAsync(
        CommunityNode node,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task CommunityNodeDeleteAsync(
        CommunityNode node,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Persist (create or update) many community nodes in batches.
    /// </summary>
    internal virtual Task CommunityNodeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        Transaction transaction,
        List<OrderedDictionary<string, object?>> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task CommunityNodeDeleteByGroupIdAsync(
        Type? cls,
        GraphDriver driver,
        string groupId,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task CommunityNodeDeleteByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------------
    // CommunityNode: Read
    // -----------------------

    /// <summary>
    /// Retrieve a single community node by UUID.
    /// </summary>
    internal virtual Task<CommunityNode> CommunityNodeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve multiple community nodes by UUIDs.
    /// </summary>
    internal virtual Task<List<CommunityNode>> CommunityNodeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve community nodes by group IDs with optional pagination.
    /// </summary>
    internal virtual Task<List<CommunityNode>> CommunityNodeGetByGroupIdsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------------
    // SagaNode: Save/Delete
    // -----------------------

    /// <summary>
    /// Persist (create or update) a single saga node.
    /// </summary>
    internal virtual Task SagaNodeSaveAsync(
        SagaNode node,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task SagaNodeDeleteAsync(
        SagaNode node,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Persist (create or update) many saga nodes in batches.
    /// </summary>
    internal virtual Task SagaNodeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        Transaction transaction,
        List<OrderedDictionary<string, object?>> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task SagaNodeDeleteByGroupIdAsync(
        Type? cls,
        GraphDriver driver,
        string groupId,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task SagaNodeDeleteByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------------
    // SagaNode: Read
    // -----------------------

    /// <summary>
    /// Retrieve a single saga node by UUID.
    /// </summary>
    internal virtual Task<SagaNode> SagaNodeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve multiple saga nodes by UUIDs.
    /// </summary>
    internal virtual Task<List<SagaNode>> SagaNodeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve saga nodes by group IDs with optional pagination.
    /// </summary>
    internal virtual Task<List<SagaNode>> SagaNodeGetByGroupIdsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------------
    // Saga: Queries
    // -----------------------

    /// <summary>
    /// Find the most recent episode UUID in a saga, excluding the current one.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     saga_uuid: UUID of the saga
    ///     current_episode_uuid: UUID of the current episode to exclude
    ///
    /// Returns:
    ///     UUID of the previous episode, or None if the saga has no other episodes
    /// </summary>
    internal virtual Task<string?> SagaGetPreviousEpisodeUuidAsync(
        GraphDriver driver,
        string sagaUuid,
        string currentEpisodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve episode content + timestamps from a saga for summarization.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     saga_uuid: UUID of the saga
    ///     since: Optional datetime compared against episode ``created_at``
    ///         (ingestion time). If provided, only returns episodes added
    ///         after this timestamp; if None, returns all episodes. Filtering
    ///         by ingestion time (not ``valid_at``) keeps backfilled episodes
    ///         with historical reference times reachable on subsequent runs.
    ///     limit: Maximum number of episodes to return
    ///
    /// Returns:
    ///     list[tuple[str, datetime | None, datetime]]: (content, valid_at,
    ///     created_at) tuples in ingestion order by ``created_at``. The
    ///     caller may reorder selected episodes by ``valid_at`` for the
    ///     prompt, while ``created_at`` advances the ingestion-time filter
    ///     watermark only through episodes actually included in that prompt.
    /// </summary>
    internal virtual Task<List<(string Content, PyDateTime? ValidAt, PyDateTime CreatedAt)>> SagaGetEpisodeContentsAsync(
        GraphDriver driver,
        string sagaUuid,
        PyDateTime? since = null,
        long limit = 200,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------
    // Edge: Save/Delete
    // -----------------

    /// <summary>
    /// Persist (create or update) a single edge.
    /// </summary>
    internal virtual Task EdgeSaveAsync(
        EntityEdge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task EdgeDeleteAsync(
        Edge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Persist (create or update) many edges in batches.
    /// </summary>
    internal virtual Task EdgeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        Transaction transaction,
        List<OrderedDictionary<string, object?>> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Update many edges that exist. An edge that does not exist is not created.
    ///
    /// `edges` has the same records as `edge_save_bulk`. Concurrent ingestion uses this
    /// operation.
    /// </summary>
    internal virtual Task EdgeUpdateBulkAsync(
        Type? cls,
        GraphDriver driver,
        Transaction transaction,
        List<OrderedDictionary<string, object?>> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task EdgeDeleteByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------
    // Edge: Read
    // -----------------

    /// <summary>
    /// Retrieve a single edge by UUID.
    /// </summary>
    internal virtual Task<EntityEdge> EdgeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve multiple edges by UUIDs.
    /// </summary>
    internal virtual Task<List<EntityEdge>> EdgeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve edges by group IDs with optional pagination.
    /// </summary>
    internal virtual Task<List<EntityEdge>> EdgeGetByGroupIdsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------
    // Edge: Embeddings (load)
    // -----------------

    /// <summary>
    /// Load embedding vectors for a single edge into the instance (e.g., set edge.embedding or similar).
    /// </summary>
    internal virtual Task EdgeLoadEmbeddingsAsync(
        EntityEdge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Load embedding vectors for many edges in batches
    /// </summary>
    internal virtual Task<OrderedDictionary<string, List<double>>> EdgeLoadEmbeddingsBulkAsync(
        GraphDriver driver,
        List<EntityEdge> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // ---------------------------
    // EpisodicEdge: Save/Delete
    // ---------------------------

    /// <summary>
    /// Persist (create or update) a single episodic edge (MENTIONS).
    /// </summary>
    internal virtual Task EpisodicEdgeSaveAsync(
        EpisodicEdge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task EpisodicEdgeDeleteAsync(
        EpisodicEdge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task EpisodicEdgeDeleteByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // ---------------------------
    // EpisodicEdge: Read
    // ---------------------------

    /// <summary>
    /// Retrieve a single episodic edge by UUID.
    /// </summary>
    internal virtual Task<EpisodicEdge> EpisodicEdgeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve multiple episodic edges by UUIDs.
    /// </summary>
    internal virtual Task<List<EpisodicEdge>> EpisodicEdgeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve episodic edges by group IDs with optional pagination.
    /// </summary>
    internal virtual Task<List<EpisodicEdge>> EpisodicEdgeGetByGroupIdsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // ---------------------------
    // CommunityEdge: Save/Delete
    // ---------------------------

    /// <summary>
    /// Persist (create or update) a single community edge (HAS_MEMBER).
    /// </summary>
    internal virtual Task CommunityEdgeSaveAsync(
        CommunityEdge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task CommunityEdgeDeleteAsync(
        CommunityEdge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task CommunityEdgeDeleteByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // ---------------------------
    // CommunityEdge: Read
    // ---------------------------

    /// <summary>
    /// Retrieve a single community edge by UUID.
    /// </summary>
    internal virtual Task<CommunityEdge> CommunityEdgeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve multiple community edges by UUIDs.
    /// </summary>
    internal virtual Task<List<CommunityEdge>> CommunityEdgeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve community edges by group IDs with optional pagination.
    /// </summary>
    internal virtual Task<List<CommunityEdge>> CommunityEdgeGetByGroupIdsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // ---------------------------
    // HasEpisodeEdge: Save/Delete
    // ---------------------------

    /// <summary>
    /// Persist (create or update) a single has_episode edge.
    /// </summary>
    internal virtual Task HasEpisodeEdgeSaveAsync(
        HasEpisodeEdge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task HasEpisodeEdgeDeleteAsync(
        HasEpisodeEdge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Persist (create or update) many has_episode edges in batches.
    /// </summary>
    internal virtual Task HasEpisodeEdgeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        Transaction transaction,
        List<OrderedDictionary<string, object?>> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task HasEpisodeEdgeDeleteByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // ---------------------------
    // HasEpisodeEdge: Read
    // ---------------------------

    /// <summary>
    /// Retrieve a single has_episode edge by UUID.
    /// </summary>
    internal virtual Task<HasEpisodeEdge> HasEpisodeEdgeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve multiple has_episode edges by UUIDs.
    /// </summary>
    internal virtual Task<List<HasEpisodeEdge>> HasEpisodeEdgeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve has_episode edges by group IDs with optional pagination.
    /// </summary>
    internal virtual Task<List<HasEpisodeEdge>> HasEpisodeEdgeGetByGroupIdsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // ----------------------------
    // NextEpisodeEdge: Save/Delete
    // ----------------------------

    /// <summary>
    /// Persist (create or update) a single next_episode edge.
    /// </summary>
    internal virtual Task NextEpisodeEdgeSaveAsync(
        NextEpisodeEdge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task NextEpisodeEdgeDeleteAsync(
        NextEpisodeEdge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Persist (create or update) many next_episode edges in batches.
    /// </summary>
    internal virtual Task NextEpisodeEdgeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        Transaction transaction,
        List<OrderedDictionary<string, object?>> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task NextEpisodeEdgeDeleteByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // ----------------------------
    // NextEpisodeEdge: Read
    // ----------------------------

    /// <summary>
    /// Retrieve a single next_episode edge by UUID.
    /// </summary>
    internal virtual Task<NextEpisodeEdge> NextEpisodeEdgeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve multiple next_episode edges by UUIDs.
    /// </summary>
    internal virtual Task<List<NextEpisodeEdge>> NextEpisodeEdgeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve next_episode edges by group IDs with optional pagination.
    /// </summary>
    internal virtual Task<List<NextEpisodeEdge>> NextEpisodeEdgeGetByGroupIdsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------
    // Search
    // -----------------

    /// <summary>
    /// Retrieve entity nodes mentioned by the given episodic nodes.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     episodes: List of EpisodicNode objects
    ///
    /// Returns:
    ///     list[EntityNode]: List of EntityNode objects that are mentioned
    ///         by the given episodes via MENTIONS relationships
    /// </summary>
    internal virtual Task<List<EntityNode>> GetMentionedNodesAsync(
        GraphDriver driver,
        List<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve community nodes that contain the given entity nodes as members.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     nodes: List of EntityNode objects
    ///
    /// Returns:
    ///     list[CommunityNode]: List of CommunityNode objects that have
    ///         HAS_MEMBER relationships to the given entity nodes
    /// </summary>
    internal virtual Task<List<CommunityNode>> GetCommunitiesByNodesAsync(
        GraphDriver driver,
        List<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------
    // Maintenance
    // -----------------

    /// <summary>
    /// Clear all data or group-specific data from the graph.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     group_ids: If provided, only delete data in these groups.
    ///         If None, deletes ALL data in the graph.
    /// </summary>
    internal virtual Task ClearDataAsync(
        GraphDriver driver,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Retrieve all entity node clusters for community detection.
    ///
    /// Uses label propagation algorithm internally to identify clusters
    /// of related entities based on their edge connections.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     group_ids: List of group IDs to process. If None, processes
    ///         all groups found in the graph.
    ///
    /// Returns:
    ///     list[list[EntityNode]]: List of clusters, where each cluster
    ///         is a list of EntityNode objects that belong together
    /// </summary>
    internal virtual Task<List<List<EntityNode>>> GetCommunityClustersAsync(
        GraphDriver driver,
        List<string>? groupIds,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Delete all community nodes from the graph.
    ///
    /// This removes all Community-labeled nodes and their relationships.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    /// </summary>
    internal virtual Task RemoveCommunitiesAsync(
        GraphDriver driver,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Determine which community an entity belongs to.
    ///
    /// First checks if the entity is already a member of a community.
    /// If not, finds the most common community among neighboring entities.
    ///
    /// Args:
    ///     driver: GraphDriver instance
    ///     entity: EntityNode object to find community for
    ///
    /// Returns:
    ///     tuple[CommunityNode | None, bool]: Tuple of (community, is_new) where:
    ///         - community: The CommunityNode the entity belongs to, or None
    ///         - is_new: True if this is a new membership (entity wasn't already
    ///           in this community), False if entity was already a member
    /// </summary>
    internal virtual Task<(CommunityNode?, bool)> DetermineEntityCommunityAsync(
        GraphDriver driver,
        EntityNode entity,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------
    // Additional Node Operations
    // -----------------

    /// <summary>
    /// Retrieve all episodes mentioning a specific entity.
    ///
    /// Args:
    ///     _cls: The EpisodicNode class (for interface consistency)
    ///     driver: GraphDriver instance
    ///     entity_node_uuid: UUID of the EntityNode to find episodes for
    ///
    /// Returns:
    ///     list[EpisodicNode]: List of EpisodicNode objects that have
    ///         MENTIONS relationships to the specified entity
    /// </summary>
    internal virtual Task<List<EpisodicNode>> EpisodicNodeGetByEntityNodeUuidAsync(
        Type? cls,
        GraphDriver driver,
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Load the name embedding for a community node.
    ///
    /// Populates the node.name_embedding field in-place.
    ///
    /// Args:
    ///     node: CommunityNode object to load embedding for
    ///     driver: GraphDriver instance
    /// </summary>
    internal virtual Task CommunityNodeLoadNameEmbeddingAsync(
        CommunityNode node,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // -----------------
    // Additional Edge Operations
    // -----------------

    /// <summary>
    /// Get edges connecting two specific entity nodes.
    ///
    /// Args:
    ///     _cls: The EntityEdge class (for interface consistency)
    ///     driver: GraphDriver instance
    ///     source_node_uuid: UUID of the source EntityNode
    ///     target_node_uuid: UUID of the target EntityNode
    ///
    /// Returns:
    ///     list[EntityEdge]: List of EntityEdge objects connecting the two nodes.
    ///         Note: Only returns edges in the source-&gt;target direction.
    /// </summary>
    internal virtual Task<List<EntityEdge>> EdgeGetBetweenNodesAsync(
        Type? cls,
        GraphDriver driver,
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    /// <summary>
    /// Get all edges connected to a specific node.
    ///
    /// Args:
    ///     _cls: The EntityEdge class (for interface consistency)
    ///     driver: GraphDriver instance
    ///     node_uuid: UUID of the EntityNode to find edges for
    ///
    /// Returns:
    ///     list[EntityEdge]: List of EntityEdge objects where the node
    ///         is either the source or target
    /// </summary>
    internal virtual Task<List<EntityEdge>> EdgeGetByNodeUuidAsync(
        Type? cls,
        GraphDriver driver,
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }
}
