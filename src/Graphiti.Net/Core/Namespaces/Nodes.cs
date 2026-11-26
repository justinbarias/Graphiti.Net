// Ported from graphiti_core/namespaces/nodes.py
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

namespace Graphiti.Core.Namespaces;

using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Operations;
using Graphiti.Core.Embedder;
using Graphiti.PyCompat;

internal sealed class EntityNodeNamespace
{
    // TODO(port): EntityNodeNamespace.__init__
    internal EntityNodeNamespace(GraphDriver driver, EntityNodeOperations ops, EmbedderClient embedder)
    {
        throw new NotSupportedException("TODO(port): EntityNodeNamespace.__init__");
    }

    // TODO(port): EntityNodeNamespace.save
    internal Task<EntityNode> SaveAsync(
        EntityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNodeNamespace.save");
    }

    // TODO(port): EntityNodeNamespace.save_bulk
    internal Task SaveBulkAsync(
        List<EntityNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNodeNamespace.save_bulk");
    }

    // TODO(port): EntityNodeNamespace.delete
    internal Task DeleteAsync(
        EntityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNodeNamespace.delete");
    }

    // TODO(port): EntityNodeNamespace.delete_by_group_id
    internal Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNodeNamespace.delete_by_group_id");
    }

    // TODO(port): EntityNodeNamespace.delete_by_uuids
    internal Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNodeNamespace.delete_by_uuids");
    }

    // TODO(port): EntityNodeNamespace.get_by_uuid
    internal Task<EntityNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNodeNamespace.get_by_uuid");
    }

    // TODO(port): EntityNodeNamespace.get_by_uuids
    internal Task<List<EntityNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNodeNamespace.get_by_uuids");
    }

    // TODO(port): EntityNodeNamespace.get_by_group_ids
    internal Task<List<EntityNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNodeNamespace.get_by_group_ids");
    }

    // TODO(port): EntityNodeNamespace.load_embeddings
    internal Task LoadEmbeddingsAsync(
        EntityNode node,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNodeNamespace.load_embeddings");
    }

    // TODO(port): EntityNodeNamespace.load_embeddings_bulk
    internal Task LoadEmbeddingsBulkAsync(
        List<EntityNode> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityNodeNamespace.load_embeddings_bulk");
    }
}

internal sealed class EpisodeNodeNamespace
{
    // TODO(port): EpisodeNodeNamespace.__init__
    internal EpisodeNodeNamespace(GraphDriver driver, EpisodeNodeOperations ops)
    {
        throw new NotSupportedException("TODO(port): EpisodeNodeNamespace.__init__");
    }

    // TODO(port): EpisodeNodeNamespace.save
    internal Task<EpisodicNode> SaveAsync(
        EpisodicNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodeNodeNamespace.save");
    }

    // TODO(port): EpisodeNodeNamespace.save_bulk
    internal Task SaveBulkAsync(
        List<EpisodicNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodeNodeNamespace.save_bulk");
    }

    // TODO(port): EpisodeNodeNamespace.delete
    internal Task DeleteAsync(
        EpisodicNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodeNodeNamespace.delete");
    }

    // TODO(port): EpisodeNodeNamespace.delete_by_group_id
    internal Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodeNodeNamespace.delete_by_group_id");
    }

    // TODO(port): EpisodeNodeNamespace.delete_by_uuids
    internal Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodeNodeNamespace.delete_by_uuids");
    }

    // TODO(port): EpisodeNodeNamespace.get_by_uuid
    internal Task<EpisodicNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodeNodeNamespace.get_by_uuid");
    }

    // TODO(port): EpisodeNodeNamespace.get_by_uuids
    internal Task<List<EpisodicNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodeNodeNamespace.get_by_uuids");
    }

    // TODO(port): EpisodeNodeNamespace.get_by_group_ids
    internal Task<List<EpisodicNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodeNodeNamespace.get_by_group_ids");
    }

    // TODO(port): EpisodeNodeNamespace.get_by_entity_node_uuid
    internal Task<List<EpisodicNode>> GetByEntityNodeUuidAsync(
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodeNodeNamespace.get_by_entity_node_uuid");
    }

    // TODO(port): EpisodeNodeNamespace.retrieve_episodes
    internal Task<List<EpisodicNode>> RetrieveEpisodesAsync(
        PyDateTime referenceTime,
        long lastN = 3,
        List<string>? groupIds = null,
        string? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodeNodeNamespace.retrieve_episodes");
    }
}

internal sealed class CommunityNodeNamespace
{
    // TODO(port): CommunityNodeNamespace.__init__
    internal CommunityNodeNamespace(GraphDriver driver, CommunityNodeOperations ops, EmbedderClient embedder)
    {
        throw new NotSupportedException("TODO(port): CommunityNodeNamespace.__init__");
    }

    // TODO(port): CommunityNodeNamespace.save
    internal Task<CommunityNode> SaveAsync(
        CommunityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNodeNamespace.save");
    }

    // TODO(port): CommunityNodeNamespace.save_bulk
    internal Task SaveBulkAsync(
        List<CommunityNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNodeNamespace.save_bulk");
    }

    // TODO(port): CommunityNodeNamespace.delete
    internal Task DeleteAsync(
        CommunityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNodeNamespace.delete");
    }

    // TODO(port): CommunityNodeNamespace.delete_by_group_id
    internal Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNodeNamespace.delete_by_group_id");
    }

    // TODO(port): CommunityNodeNamespace.delete_by_uuids
    internal Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNodeNamespace.delete_by_uuids");
    }

    // TODO(port): CommunityNodeNamespace.get_by_uuid
    internal Task<CommunityNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNodeNamespace.get_by_uuid");
    }

    // TODO(port): CommunityNodeNamespace.get_by_uuids
    internal Task<List<CommunityNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNodeNamespace.get_by_uuids");
    }

    // TODO(port): CommunityNodeNamespace.get_by_group_ids
    internal Task<List<CommunityNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNodeNamespace.get_by_group_ids");
    }

    // TODO(port): CommunityNodeNamespace.load_name_embedding
    internal Task LoadNameEmbeddingAsync(
        CommunityNode node,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityNodeNamespace.load_name_embedding");
    }
}

internal sealed class SagaNodeNamespace
{
    // TODO(port): SagaNodeNamespace.__init__
    internal SagaNodeNamespace(GraphDriver driver, SagaNodeOperations ops)
    {
        throw new NotSupportedException("TODO(port): SagaNodeNamespace.__init__");
    }

    // TODO(port): SagaNodeNamespace.save
    internal Task<SagaNode> SaveAsync(
        SagaNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNodeNamespace.save");
    }

    // TODO(port): SagaNodeNamespace.save_bulk
    internal Task SaveBulkAsync(
        List<SagaNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNodeNamespace.save_bulk");
    }

    // TODO(port): SagaNodeNamespace.delete
    internal Task DeleteAsync(
        SagaNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNodeNamespace.delete");
    }

    // TODO(port): SagaNodeNamespace.delete_by_group_id
    internal Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNodeNamespace.delete_by_group_id");
    }

    // TODO(port): SagaNodeNamespace.delete_by_uuids
    internal Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNodeNamespace.delete_by_uuids");
    }

    // TODO(port): SagaNodeNamespace.get_by_uuid
    internal Task<SagaNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNodeNamespace.get_by_uuid");
    }

    // TODO(port): SagaNodeNamespace.get_by_uuids
    internal Task<List<SagaNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNodeNamespace.get_by_uuids");
    }

    // TODO(port): SagaNodeNamespace.get_by_group_ids
    internal Task<List<SagaNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SagaNodeNamespace.get_by_group_ids");
    }
}

internal sealed class NodeNamespace
{
    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    // TODO(port): NodeNamespace.entity
    internal EntityNodeNamespace Entity => throw new NotSupportedException("TODO(port): NodeNamespace.entity");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    // TODO(port): NodeNamespace.episode
    internal EpisodeNodeNamespace Episode => throw new NotSupportedException("TODO(port): NodeNamespace.episode");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    // TODO(port): NodeNamespace.community
    internal CommunityNodeNamespace Community => throw new NotSupportedException("TODO(port): NodeNamespace.community");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    // TODO(port): NodeNamespace.saga
    internal SagaNodeNamespace Saga => throw new NotSupportedException("TODO(port): NodeNamespace.saga");

    // PORT: the unit adds _driver_name with the constructor body.
    // TODO(port): NodeNamespace.__init__
    internal NodeNamespace(GraphDriver driver, EmbedderClient embedder)
    {
        throw new NotSupportedException("TODO(port): NodeNamespace.__init__");
    }
}
