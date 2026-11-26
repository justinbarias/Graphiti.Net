// Ported from graphiti_core/namespaces/edges.py
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

internal sealed class EntityEdgeNamespace
{
    // TODO(port): EntityEdgeNamespace.__init__
    internal EntityEdgeNamespace(GraphDriver driver, EntityEdgeOperations ops, EmbedderClient embedder)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.__init__");
    }

    // TODO(port): EntityEdgeNamespace.save
    internal Task<EntityEdge> SaveAsync(
        EntityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.save");
    }

    // TODO(port): EntityEdgeNamespace.save_bulk
    internal Task SaveBulkAsync(
        List<EntityEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.save_bulk");
    }

    // TODO(port): EntityEdgeNamespace.delete
    internal Task DeleteAsync(
        EntityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.delete");
    }

    // TODO(port): EntityEdgeNamespace.delete_by_uuids
    internal Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.delete_by_uuids");
    }

    // TODO(port): EntityEdgeNamespace.get_by_uuid
    internal Task<EntityEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.get_by_uuid");
    }

    // TODO(port): EntityEdgeNamespace.get_by_uuids
    internal Task<List<EntityEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.get_by_uuids");
    }

    // TODO(port): EntityEdgeNamespace.get_by_group_ids
    internal Task<List<EntityEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.get_by_group_ids");
    }

    // TODO(port): EntityEdgeNamespace.get_between_nodes
    internal Task<List<EntityEdge>> GetBetweenNodesAsync(
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.get_between_nodes");
    }

    // TODO(port): EntityEdgeNamespace.get_by_node_uuid
    internal Task<List<EntityEdge>> GetByNodeUuidAsync(
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.get_by_node_uuid");
    }

    // TODO(port): EntityEdgeNamespace.load_embeddings
    internal Task LoadEmbeddingsAsync(
        EntityEdge edge,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.load_embeddings");
    }

    // TODO(port): EntityEdgeNamespace.load_embeddings_bulk
    internal Task LoadEmbeddingsBulkAsync(
        List<EntityEdge> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EntityEdgeNamespace.load_embeddings_bulk");
    }
}

internal sealed class EpisodicEdgeNamespace
{
    // TODO(port): EpisodicEdgeNamespace.__init__
    internal EpisodicEdgeNamespace(GraphDriver driver, EpisodicEdgeOperations ops)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdgeNamespace.__init__");
    }

    // TODO(port): EpisodicEdgeNamespace.save
    internal Task<EpisodicEdge> SaveAsync(
        EpisodicEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdgeNamespace.save");
    }

    // TODO(port): EpisodicEdgeNamespace.save_bulk
    internal Task SaveBulkAsync(
        List<EpisodicEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdgeNamespace.save_bulk");
    }

    // TODO(port): EpisodicEdgeNamespace.delete
    internal Task DeleteAsync(
        EpisodicEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdgeNamespace.delete");
    }

    // TODO(port): EpisodicEdgeNamespace.delete_by_uuids
    internal Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdgeNamespace.delete_by_uuids");
    }

    // TODO(port): EpisodicEdgeNamespace.get_by_uuid
    internal Task<EpisodicEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdgeNamespace.get_by_uuid");
    }

    // TODO(port): EpisodicEdgeNamespace.get_by_uuids
    internal Task<List<EpisodicEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdgeNamespace.get_by_uuids");
    }

    // TODO(port): EpisodicEdgeNamespace.get_by_group_ids
    internal Task<List<EpisodicEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): EpisodicEdgeNamespace.get_by_group_ids");
    }
}

internal sealed class CommunityEdgeNamespace
{
    // TODO(port): CommunityEdgeNamespace.__init__
    internal CommunityEdgeNamespace(GraphDriver driver, CommunityEdgeOperations ops)
    {
        throw new NotSupportedException("TODO(port): CommunityEdgeNamespace.__init__");
    }

    // TODO(port): CommunityEdgeNamespace.save
    internal Task<CommunityEdge> SaveAsync(
        CommunityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityEdgeNamespace.save");
    }

    // TODO(port): CommunityEdgeNamespace.delete
    internal Task DeleteAsync(
        CommunityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityEdgeNamespace.delete");
    }

    // TODO(port): CommunityEdgeNamespace.delete_by_uuids
    internal Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityEdgeNamespace.delete_by_uuids");
    }

    // TODO(port): CommunityEdgeNamespace.get_by_uuid
    internal Task<CommunityEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityEdgeNamespace.get_by_uuid");
    }

    // TODO(port): CommunityEdgeNamespace.get_by_uuids
    internal Task<List<CommunityEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityEdgeNamespace.get_by_uuids");
    }

    // TODO(port): CommunityEdgeNamespace.get_by_group_ids
    internal Task<List<CommunityEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): CommunityEdgeNamespace.get_by_group_ids");
    }
}

internal sealed class HasEpisodeEdgeNamespace
{
    // TODO(port): HasEpisodeEdgeNamespace.__init__
    internal HasEpisodeEdgeNamespace(GraphDriver driver, HasEpisodeEdgeOperations ops)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdgeNamespace.__init__");
    }

    // TODO(port): HasEpisodeEdgeNamespace.save
    internal Task<HasEpisodeEdge> SaveAsync(
        HasEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdgeNamespace.save");
    }

    // TODO(port): HasEpisodeEdgeNamespace.save_bulk
    internal Task SaveBulkAsync(
        List<HasEpisodeEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdgeNamespace.save_bulk");
    }

    // TODO(port): HasEpisodeEdgeNamespace.delete
    internal Task DeleteAsync(
        HasEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdgeNamespace.delete");
    }

    // TODO(port): HasEpisodeEdgeNamespace.delete_by_uuids
    internal Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdgeNamespace.delete_by_uuids");
    }

    // TODO(port): HasEpisodeEdgeNamespace.get_by_uuid
    internal Task<HasEpisodeEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdgeNamespace.get_by_uuid");
    }

    // TODO(port): HasEpisodeEdgeNamespace.get_by_uuids
    internal Task<List<HasEpisodeEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdgeNamespace.get_by_uuids");
    }

    // TODO(port): HasEpisodeEdgeNamespace.get_by_group_ids
    internal Task<List<HasEpisodeEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): HasEpisodeEdgeNamespace.get_by_group_ids");
    }
}

internal sealed class NextEpisodeEdgeNamespace
{
    // TODO(port): NextEpisodeEdgeNamespace.__init__
    internal NextEpisodeEdgeNamespace(GraphDriver driver, NextEpisodeEdgeOperations ops)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdgeNamespace.__init__");
    }

    // TODO(port): NextEpisodeEdgeNamespace.save
    internal Task<NextEpisodeEdge> SaveAsync(
        NextEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdgeNamespace.save");
    }

    // TODO(port): NextEpisodeEdgeNamespace.save_bulk
    internal Task SaveBulkAsync(
        List<NextEpisodeEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdgeNamespace.save_bulk");
    }

    // TODO(port): NextEpisodeEdgeNamespace.delete
    internal Task DeleteAsync(
        NextEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdgeNamespace.delete");
    }

    // TODO(port): NextEpisodeEdgeNamespace.delete_by_uuids
    internal Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdgeNamespace.delete_by_uuids");
    }

    // TODO(port): NextEpisodeEdgeNamespace.get_by_uuid
    internal Task<NextEpisodeEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdgeNamespace.get_by_uuid");
    }

    // TODO(port): NextEpisodeEdgeNamespace.get_by_uuids
    internal Task<List<NextEpisodeEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdgeNamespace.get_by_uuids");
    }

    // TODO(port): NextEpisodeEdgeNamespace.get_by_group_ids
    internal Task<List<NextEpisodeEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): NextEpisodeEdgeNamespace.get_by_group_ids");
    }
}

internal sealed class EdgeNamespace
{
    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    // TODO(port): EdgeNamespace.entity
    internal EntityEdgeNamespace Entity => throw new NotSupportedException("TODO(port): EdgeNamespace.entity");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    // TODO(port): EdgeNamespace.episodic
    internal EpisodicEdgeNamespace Episodic => throw new NotSupportedException("TODO(port): EdgeNamespace.episodic");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    // TODO(port): EdgeNamespace.community
    internal CommunityEdgeNamespace Community => throw new NotSupportedException("TODO(port): EdgeNamespace.community");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    // TODO(port): EdgeNamespace.has_episode
    internal HasEpisodeEdgeNamespace HasEpisode => throw new NotSupportedException("TODO(port): EdgeNamespace.has_episode");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    // TODO(port): EdgeNamespace.next_episode
    internal NextEpisodeEdgeNamespace NextEpisode => throw new NotSupportedException("TODO(port): EdgeNamespace.next_episode");

    // PORT: the unit adds _driver_name with the constructor body.
    // TODO(port): EdgeNamespace.__init__
    internal EdgeNamespace(GraphDriver driver, EmbedderClient embedder)
    {
        throw new NotSupportedException("TODO(port): EdgeNamespace.__init__");
    }
}
