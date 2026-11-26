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
    private readonly GraphDriver _driver;
    private readonly EntityEdgeOperations _ops;
    private readonly EmbedderClient _embedder;

    internal EntityEdgeNamespace(GraphDriver driver, EntityEdgeOperations ops, EmbedderClient embedder)
    {
        _driver = driver;
        _ops = ops;
        _embedder = embedder;
    }

    internal async Task<EntityEdge> SaveAsync(
        EntityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await edge.GenerateEmbeddingAsync(_embedder, cancellationToken);
        await _ops.SaveAsync(_driver, edge, tx, cancellationToken);
        return edge;
    }

    internal async Task SaveBulkAsync(
        List<EntityEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveBulkAsync(_driver, edges, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        EntityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteAsync(_driver, edge, tx, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByUuidsAsync(_driver, uuids, tx, cancellationToken);
    }

    internal async Task<EntityEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidAsync(_driver, uuid, cancellationToken);
    }

    internal async Task<List<EntityEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidsAsync(_driver, uuids, cancellationToken);
    }

    internal async Task<List<EntityEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
    }

    internal async Task<List<EntityEdge>> GetBetweenNodesAsync(
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetBetweenNodesAsync(_driver, sourceNodeUuid, targetNodeUuid, cancellationToken);
    }

    internal async Task<List<EntityEdge>> GetByNodeUuidAsync(
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByNodeUuidAsync(_driver, nodeUuid, cancellationToken);
    }

    internal async Task LoadEmbeddingsAsync(
        EntityEdge edge,
        CancellationToken cancellationToken = default)
    {
        await _ops.LoadEmbeddingsAsync(_driver, edge, cancellationToken);
    }

    internal async Task LoadEmbeddingsBulkAsync(
        List<EntityEdge> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.LoadEmbeddingsBulkAsync(_driver, edges, batchSize, cancellationToken);
    }
}

internal sealed class EpisodicEdgeNamespace
{
    private readonly GraphDriver _driver;
    private readonly EpisodicEdgeOperations _ops;

    internal EpisodicEdgeNamespace(GraphDriver driver, EpisodicEdgeOperations ops)
    {
        _driver = driver;
        _ops = ops;
    }

    internal async Task<EpisodicEdge> SaveAsync(
        EpisodicEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveAsync(_driver, edge, tx, cancellationToken);
        return edge;
    }

    internal async Task SaveBulkAsync(
        List<EpisodicEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveBulkAsync(_driver, edges, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        EpisodicEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteAsync(_driver, edge, tx, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByUuidsAsync(_driver, uuids, tx, cancellationToken);
    }

    internal async Task<EpisodicEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidAsync(_driver, uuid, cancellationToken);
    }

    internal async Task<List<EpisodicEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidsAsync(_driver, uuids, cancellationToken);
    }

    internal async Task<List<EpisodicEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
    }
}

internal sealed class CommunityEdgeNamespace
{
    private readonly GraphDriver _driver;
    private readonly CommunityEdgeOperations _ops;

    internal CommunityEdgeNamespace(GraphDriver driver, CommunityEdgeOperations ops)
    {
        _driver = driver;
        _ops = ops;
    }

    internal async Task<CommunityEdge> SaveAsync(
        CommunityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveAsync(_driver, edge, tx, cancellationToken);
        return edge;
    }

    internal async Task DeleteAsync(
        CommunityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteAsync(_driver, edge, tx, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByUuidsAsync(_driver, uuids, tx, cancellationToken);
    }

    internal async Task<CommunityEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidAsync(_driver, uuid, cancellationToken);
    }

    internal async Task<List<CommunityEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidsAsync(_driver, uuids, cancellationToken);
    }

    internal async Task<List<CommunityEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
    }
}

internal sealed class HasEpisodeEdgeNamespace
{
    private readonly GraphDriver _driver;
    private readonly HasEpisodeEdgeOperations _ops;

    internal HasEpisodeEdgeNamespace(GraphDriver driver, HasEpisodeEdgeOperations ops)
    {
        _driver = driver;
        _ops = ops;
    }

    internal async Task<HasEpisodeEdge> SaveAsync(
        HasEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveAsync(_driver, edge, tx, cancellationToken);
        return edge;
    }

    internal async Task SaveBulkAsync(
        List<HasEpisodeEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveBulkAsync(_driver, edges, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        HasEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteAsync(_driver, edge, tx, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByUuidsAsync(_driver, uuids, tx, cancellationToken);
    }

    internal async Task<HasEpisodeEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidAsync(_driver, uuid, cancellationToken);
    }

    internal async Task<List<HasEpisodeEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidsAsync(_driver, uuids, cancellationToken);
    }

    internal async Task<List<HasEpisodeEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
    }
}

internal sealed class NextEpisodeEdgeNamespace
{
    private readonly GraphDriver _driver;
    private readonly NextEpisodeEdgeOperations _ops;

    internal NextEpisodeEdgeNamespace(GraphDriver driver, NextEpisodeEdgeOperations ops)
    {
        _driver = driver;
        _ops = ops;
    }

    internal async Task<NextEpisodeEdge> SaveAsync(
        NextEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveAsync(_driver, edge, tx, cancellationToken);
        return edge;
    }

    internal async Task SaveBulkAsync(
        List<NextEpisodeEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveBulkAsync(_driver, edges, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        NextEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteAsync(_driver, edge, tx, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByUuidsAsync(_driver, uuids, tx, cancellationToken);
    }

    internal async Task<NextEpisodeEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidAsync(_driver, uuid, cancellationToken);
    }

    internal async Task<List<NextEpisodeEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidsAsync(_driver, uuids, cancellationToken);
    }

    internal async Task<List<NextEpisodeEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
    }
}

internal sealed class EdgeNamespace
{
    private readonly string _driverName;

    private readonly EntityEdgeNamespace? _entity;
    private readonly EpisodicEdgeNamespace? _episodic;
    private readonly CommunityEdgeNamespace? _community;
    private readonly HasEpisodeEdgeNamespace? _hasEpisode;
    private readonly NextEpisodeEdgeNamespace? _nextEpisode;

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    internal EntityEdgeNamespace Entity => _entity ?? throw new NotImplementedException(_driverName + " does not implement entity_edge_ops");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    internal EpisodicEdgeNamespace Episodic => _episodic ?? throw new NotImplementedException(_driverName + " does not implement episodic_edge_ops");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    internal CommunityEdgeNamespace Community => _community ?? throw new NotImplementedException(_driverName + " does not implement community_edge_ops");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    internal HasEpisodeEdgeNamespace HasEpisode => _hasEpisode ?? throw new NotImplementedException(_driverName + " does not implement has_episode_edge_ops");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    internal NextEpisodeEdgeNamespace NextEpisode => _nextEpisode ?? throw new NotImplementedException(_driverName + " does not implement next_episode_edge_ops");

    // PORT: _driver_name is type(driver).__name__, which is PythonName (R15).
    internal EdgeNamespace(GraphDriver driver, EmbedderClient embedder)
    {
        _driverName = driver.PythonName;

        var entityEdgeOps = driver.EntityEdgeOps;
        if (entityEdgeOps is not null)
        {
            _entity = new EntityEdgeNamespace(driver, entityEdgeOps, embedder);
        }

        var episodicEdgeOps = driver.EpisodicEdgeOps;
        if (episodicEdgeOps is not null)
        {
            _episodic = new EpisodicEdgeNamespace(driver, episodicEdgeOps);
        }

        var communityEdgeOps = driver.CommunityEdgeOps;
        if (communityEdgeOps is not null)
        {
            _community = new CommunityEdgeNamespace(driver, communityEdgeOps);
        }

        var hasEpisodeEdgeOps = driver.HasEpisodeEdgeOps;
        if (hasEpisodeEdgeOps is not null)
        {
            _hasEpisode = new HasEpisodeEdgeNamespace(driver, hasEpisodeEdgeOps);
        }

        var nextEpisodeEdgeOps = driver.NextEpisodeEdgeOps;
        if (nextEpisodeEdgeOps is not null)
        {
            _nextEpisode = new NextEpisodeEdgeNamespace(driver, nextEpisodeEdgeOps);
        }
    }
}
