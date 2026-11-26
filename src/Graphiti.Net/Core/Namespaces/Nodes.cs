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
    private readonly GraphDriver _driver;
    private readonly EntityNodeOperations _ops;
    private readonly EmbedderClient _embedder;

    internal EntityNodeNamespace(GraphDriver driver, EntityNodeOperations ops, EmbedderClient embedder)
    {
        _driver = driver;
        _ops = ops;
        _embedder = embedder;
    }

    internal async Task<EntityNode> SaveAsync(
        EntityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await node.GenerateNameEmbeddingAsync(_embedder, cancellationToken);
        await _ops.SaveAsync(_driver, node, tx, cancellationToken);
        return node;
    }

    internal async Task SaveBulkAsync(
        List<EntityNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveBulkAsync(_driver, nodes, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        EntityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteAsync(_driver, node, tx, cancellationToken);
    }

    internal async Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByGroupIdAsync(_driver, groupId, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByUuidsAsync(_driver, uuids, tx, batchSize, cancellationToken);
    }

    internal async Task<EntityNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidAsync(_driver, uuid, cancellationToken);
    }

    internal async Task<List<EntityNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidsAsync(_driver, uuids, cancellationToken);
    }

    internal async Task<List<EntityNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
    }

    internal async Task LoadEmbeddingsAsync(
        EntityNode node,
        CancellationToken cancellationToken = default)
    {
        await _ops.LoadEmbeddingsAsync(_driver, node, cancellationToken);
    }

    internal async Task LoadEmbeddingsBulkAsync(
        List<EntityNode> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.LoadEmbeddingsBulkAsync(_driver, nodes, batchSize, cancellationToken);
    }
}

internal sealed class EpisodeNodeNamespace
{
    private readonly GraphDriver _driver;
    private readonly EpisodeNodeOperations _ops;

    internal EpisodeNodeNamespace(GraphDriver driver, EpisodeNodeOperations ops)
    {
        _driver = driver;
        _ops = ops;
    }

    internal async Task<EpisodicNode> SaveAsync(
        EpisodicNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveAsync(_driver, node, tx, cancellationToken);
        return node;
    }

    internal async Task SaveBulkAsync(
        List<EpisodicNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveBulkAsync(_driver, nodes, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        EpisodicNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteAsync(_driver, node, tx, cancellationToken);
    }

    internal async Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByGroupIdAsync(_driver, groupId, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByUuidsAsync(_driver, uuids, tx, batchSize, cancellationToken);
    }

    internal async Task<EpisodicNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidAsync(_driver, uuid, cancellationToken);
    }

    internal async Task<List<EpisodicNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidsAsync(_driver, uuids, cancellationToken);
    }

    internal async Task<List<EpisodicNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
    }

    internal async Task<List<EpisodicNode>> GetByEntityNodeUuidAsync(
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByEntityNodeUuidAsync(_driver, entityNodeUuid, cancellationToken);
    }

    internal async Task<List<EpisodicNode>> RetrieveEpisodesAsync(
        PyDateTime referenceTime,
        long lastN = 3,
        List<string>? groupIds = null,
        string? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        return await _ops.RetrieveEpisodesAsync(_driver, referenceTime, lastN, groupIds, source, saga, cancellationToken);
    }
}

internal sealed class CommunityNodeNamespace
{
    private readonly GraphDriver _driver;
    private readonly CommunityNodeOperations _ops;
    private readonly EmbedderClient _embedder;

    internal CommunityNodeNamespace(GraphDriver driver, CommunityNodeOperations ops, EmbedderClient embedder)
    {
        _driver = driver;
        _ops = ops;
        _embedder = embedder;
    }

    internal async Task<CommunityNode> SaveAsync(
        CommunityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await node.GenerateNameEmbeddingAsync(_embedder, cancellationToken);
        await _ops.SaveAsync(_driver, node, tx, cancellationToken);
        return node;
    }

    internal async Task SaveBulkAsync(
        List<CommunityNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveBulkAsync(_driver, nodes, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        CommunityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteAsync(_driver, node, tx, cancellationToken);
    }

    internal async Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByGroupIdAsync(_driver, groupId, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByUuidsAsync(_driver, uuids, tx, batchSize, cancellationToken);
    }

    internal async Task<CommunityNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidAsync(_driver, uuid, cancellationToken);
    }

    internal async Task<List<CommunityNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidsAsync(_driver, uuids, cancellationToken);
    }

    internal async Task<List<CommunityNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
    }

    internal async Task LoadNameEmbeddingAsync(
        CommunityNode node,
        CancellationToken cancellationToken = default)
    {
        await _ops.LoadNameEmbeddingAsync(_driver, node, cancellationToken);
    }
}

internal sealed class SagaNodeNamespace
{
    private readonly GraphDriver _driver;
    private readonly SagaNodeOperations _ops;

    internal SagaNodeNamespace(GraphDriver driver, SagaNodeOperations ops)
    {
        _driver = driver;
        _ops = ops;
    }

    internal async Task<SagaNode> SaveAsync(
        SagaNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveAsync(_driver, node, tx, cancellationToken);
        return node;
    }

    internal async Task SaveBulkAsync(
        List<SagaNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.SaveBulkAsync(_driver, nodes, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        SagaNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteAsync(_driver, node, tx, cancellationToken);
    }

    internal async Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByGroupIdAsync(_driver, groupId, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await _ops.DeleteByUuidsAsync(_driver, uuids, tx, batchSize, cancellationToken);
    }

    internal async Task<SagaNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidAsync(_driver, uuid, cancellationToken);
    }

    internal async Task<List<SagaNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByUuidsAsync(_driver, uuids, cancellationToken);
    }

    internal async Task<List<SagaNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await _ops.GetByGroupIdsAsync(_driver, groupIds, limit, uuidCursor, cancellationToken);
    }
}

internal sealed class NodeNamespace
{
    private readonly string _driverName;

    private readonly EntityNodeNamespace? _entity;
    private readonly EpisodeNodeNamespace? _episode;
    private readonly CommunityNodeNamespace? _community;
    private readonly SagaNodeNamespace? _saga;

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    internal EntityNodeNamespace Entity => _entity ?? throw new NotImplementedException(_driverName + " does not implement entity_node_ops");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    internal EpisodeNodeNamespace Episode => _episode ?? throw new NotImplementedException(_driverName + " does not implement episode_node_ops");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    internal CommunityNodeNamespace Community => _community ?? throw new NotImplementedException(_driverName + " does not implement community_node_ops");

    // PORT: Python sets the attribute only when the driver has the operations, and __getattr__ raises
    // NotImplementedError for it otherwise. The getter throws NotImplementedException in that case (R15).
    internal SagaNodeNamespace Saga => _saga ?? throw new NotImplementedException(_driverName + " does not implement saga_node_ops");

    // PORT: _driver_name is type(driver).__name__, which is PythonName (R15).
    internal NodeNamespace(GraphDriver driver, EmbedderClient embedder)
    {
        _driverName = driver.PythonName;

        var entityNodeOps = driver.EntityNodeOps;
        if (entityNodeOps is not null)
        {
            _entity = new EntityNodeNamespace(driver, entityNodeOps, embedder);
        }

        var episodeNodeOps = driver.EpisodeNodeOps;
        if (episodeNodeOps is not null)
        {
            _episode = new EpisodeNodeNamespace(driver, episodeNodeOps);
        }

        var communityNodeOps = driver.CommunityNodeOps;
        if (communityNodeOps is not null)
        {
            _community = new CommunityNodeNamespace(driver, communityNodeOps, embedder);
        }

        var sagaNodeOps = driver.SagaNodeOps;
        if (sagaNodeOps is not null)
        {
            _saga = new SagaNodeNamespace(driver, sagaNodeOps);
        }
    }
}
