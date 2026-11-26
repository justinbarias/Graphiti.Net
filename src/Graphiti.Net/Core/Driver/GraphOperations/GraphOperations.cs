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

namespace Graphiti.Core.Driver.GraphOperations;

using Graphiti.Core.Search;
using Graphiti.PyCompat;

// PORT: a pydantic model with subclasses and no field is an abstract record (R7). A method whose
// Python body only raises NotImplementedError is virtual and throws NotImplementedException (R4, R15).
internal abstract record GraphOperationsInterface
{
    // -----------------
    // Node: Save/Delete
    // -----------------

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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task NodeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        GraphDriverSession transaction,
        List<OrderedDictionary<string, object?>> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task NodeUpdateBulkAsync(
        Type? cls,
        GraphDriver driver,
        GraphDriverSession transaction,
        List<OrderedDictionary<string, object?>> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task NodeDeleteByGroupIdAsync(
        Type? cls,
        GraphDriver driver,
        string groupId,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<EntityNode> NodeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<EntityNode>> NodeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<EntityNode>> NodeGetByNamesAsync(
        Type? cls,
        GraphDriver driver,
        List<string> names,
        List<string> groupIds,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    internal virtual Task NodeLoadEmbeddingsAsync(
        EntityNode node,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task EpisodicNodeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        GraphDriverSession transaction,
        List<OrderedDictionary<string, object?>> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task EpisodicEdgeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        GraphDriverSession transaction,
        List<OrderedDictionary<string, object?>> episodicEdges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task EpisodicNodeDeleteByGroupIdAsync(
        Type? cls,
        GraphDriver driver,
        string groupId,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<EpisodicNode> EpisodicNodeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<EpisodicNode>> EpisodicNodeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task CommunityNodeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        GraphDriverSession transaction,
        List<OrderedDictionary<string, object?>> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task CommunityNodeDeleteByGroupIdAsync(
        Type? cls,
        GraphDriver driver,
        string groupId,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<CommunityNode> CommunityNodeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<CommunityNode>> CommunityNodeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task SagaNodeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        GraphDriverSession transaction,
        List<OrderedDictionary<string, object?>> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task SagaNodeDeleteByGroupIdAsync(
        Type? cls,
        GraphDriver driver,
        string groupId,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<SagaNode> SagaNodeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<SagaNode>> SagaNodeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    internal virtual Task<string?> SagaGetPreviousEpisodeUuidAsync(
        GraphDriver driver,
        string sagaUuid,
        string currentEpisodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task EdgeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        GraphDriverSession transaction,
        List<OrderedDictionary<string, object?>> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task EdgeUpdateBulkAsync(
        Type? cls,
        GraphDriver driver,
        GraphDriverSession transaction,
        List<OrderedDictionary<string, object?>> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<EntityEdge> EdgeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<EntityEdge>> EdgeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    internal virtual Task EdgeLoadEmbeddingsAsync(
        EntityEdge edge,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<EpisodicEdge> EpisodicEdgeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<EpisodicEdge>> EpisodicEdgeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<CommunityEdge> CommunityEdgeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<CommunityEdge>> CommunityEdgeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task HasEpisodeEdgeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        GraphDriverSession transaction,
        List<OrderedDictionary<string, object?>> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<HasEpisodeEdge> HasEpisodeEdgeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<HasEpisodeEdge>> HasEpisodeEdgeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task NextEpisodeEdgeSaveBulkAsync(
        Type? cls,
        GraphDriver driver,
        GraphDriverSession transaction,
        List<OrderedDictionary<string, object?>> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<NextEpisodeEdge> NextEpisodeEdgeGetByUuidAsync(
        Type? cls,
        GraphDriver driver,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<NextEpisodeEdge>> NextEpisodeEdgeGetByUuidsAsync(
        Type? cls,
        GraphDriver driver,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
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

    internal virtual Task<List<EntityNode>> GetMentionedNodesAsync(
        GraphDriver driver,
        List<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

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

    internal virtual Task ClearDataAsync(
        GraphDriver driver,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task<List<List<EntityNode>>> GetCommunityClustersAsync(
        GraphDriver driver,
        List<string>? groupIds,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    internal virtual Task RemoveCommunitiesAsync(
        GraphDriver driver,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<EpisodicNode>> EpisodicNodeGetByEntityNodeUuidAsync(
        Type? cls,
        GraphDriver driver,
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

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

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<EntityEdge>> EdgeGetBetweenNodesAsync(
        Type? cls,
        GraphDriver driver,
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }

    // TODO(port): no rule for the class object parameter _cls. Callers pass cls or None.
    internal virtual Task<List<EntityEdge>> EdgeGetByNodeUuidAsync(
        Type? cls,
        GraphDriver driver,
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(string.Empty);
    }
}
