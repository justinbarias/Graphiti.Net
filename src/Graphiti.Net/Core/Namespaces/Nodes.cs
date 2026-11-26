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

/// <summary>Namespace for entity node operations. Accessed as <c>graphiti.nodes.entity</c>.</summary>
internal sealed class EntityNodeNamespace
{
    private readonly GraphDriver driver;
    private readonly EntityNodeOperations ops;
    private readonly EmbedderClient embedder;

    internal EntityNodeNamespace(GraphDriver driver, EntityNodeOperations ops, EmbedderClient embedder)
    {
        this.driver = driver;
        this.ops = ops;
        this.embedder = embedder;
    }

    internal async Task<EntityNode> SaveAsync(
        EntityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await node.GenerateNameEmbeddingAsync(embedder, cancellationToken);
        await ops.SaveAsync(driver, node, tx, cancellationToken);
        return node;
    }

    internal async Task SaveBulkAsync(
        List<EntityNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveBulkAsync(driver, nodes, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        EntityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteAsync(driver, node, tx, cancellationToken);
    }

    internal async Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByGroupIdAsync(driver, groupId, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByUuidsAsync(driver, uuids, tx, batchSize, cancellationToken);
    }

    internal async Task<EntityNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidAsync(driver, uuid, cancellationToken);
    }

    internal async Task<List<EntityNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidsAsync(driver, uuids, cancellationToken);
    }

    internal async Task<List<EntityNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByGroupIdsAsync(driver, groupIds, limit, uuidCursor, cancellationToken);
    }

    internal async Task LoadEmbeddingsAsync(
        EntityNode node,
        CancellationToken cancellationToken = default)
    {
        await ops.LoadEmbeddingsAsync(driver, node, cancellationToken);
    }

    internal async Task LoadEmbeddingsBulkAsync(
        List<EntityNode> nodes,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.LoadEmbeddingsBulkAsync(driver, nodes, batchSize, cancellationToken);
    }
}

/// <summary>Namespace for episode node operations. Accessed as <c>graphiti.nodes.episode</c>.</summary>
internal sealed class EpisodeNodeNamespace
{
    private readonly GraphDriver driver;
    private readonly EpisodeNodeOperations ops;

    internal EpisodeNodeNamespace(GraphDriver driver, EpisodeNodeOperations ops)
    {
        this.driver = driver;
        this.ops = ops;
    }

    internal async Task<EpisodicNode> SaveAsync(
        EpisodicNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveAsync(driver, node, tx, cancellationToken);
        return node;
    }

    internal async Task SaveBulkAsync(
        List<EpisodicNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveBulkAsync(driver, nodes, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        EpisodicNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteAsync(driver, node, tx, cancellationToken);
    }

    internal async Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByGroupIdAsync(driver, groupId, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByUuidsAsync(driver, uuids, tx, batchSize, cancellationToken);
    }

    internal async Task<EpisodicNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidAsync(driver, uuid, cancellationToken);
    }

    internal async Task<List<EpisodicNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidsAsync(driver, uuids, cancellationToken);
    }

    internal async Task<List<EpisodicNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByGroupIdsAsync(driver, groupIds, limit, uuidCursor, cancellationToken);
    }

    internal async Task<List<EpisodicNode>> GetByEntityNodeUuidAsync(
        string entityNodeUuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByEntityNodeUuidAsync(driver, entityNodeUuid, cancellationToken);
    }

    internal async Task<List<EpisodicNode>> RetrieveEpisodesAsync(
        PyDateTime referenceTime,
        long lastN = 3,
        List<string>? groupIds = null,
        string? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        return await ops.RetrieveEpisodesAsync(driver, referenceTime, lastN, groupIds, source, saga, cancellationToken);
    }
}

/// <summary>Namespace for community node operations. Accessed as <c>graphiti.nodes.community</c>.</summary>
internal sealed class CommunityNodeNamespace
{
    private readonly GraphDriver driver;
    private readonly CommunityNodeOperations ops;
    private readonly EmbedderClient embedder;

    internal CommunityNodeNamespace(GraphDriver driver, CommunityNodeOperations ops, EmbedderClient embedder)
    {
        this.driver = driver;
        this.ops = ops;
        this.embedder = embedder;
    }

    internal async Task<CommunityNode> SaveAsync(
        CommunityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await node.GenerateNameEmbeddingAsync(embedder, cancellationToken);
        await ops.SaveAsync(driver, node, tx, cancellationToken);
        return node;
    }

    internal async Task SaveBulkAsync(
        List<CommunityNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveBulkAsync(driver, nodes, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        CommunityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteAsync(driver, node, tx, cancellationToken);
    }

    internal async Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByGroupIdAsync(driver, groupId, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByUuidsAsync(driver, uuids, tx, batchSize, cancellationToken);
    }

    internal async Task<CommunityNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidAsync(driver, uuid, cancellationToken);
    }

    internal async Task<List<CommunityNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidsAsync(driver, uuids, cancellationToken);
    }

    internal async Task<List<CommunityNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByGroupIdsAsync(driver, groupIds, limit, uuidCursor, cancellationToken);
    }

    internal async Task LoadNameEmbeddingAsync(
        CommunityNode node,
        CancellationToken cancellationToken = default)
    {
        await ops.LoadNameEmbeddingAsync(driver, node, cancellationToken);
    }
}

/// <summary>Namespace for saga node operations. Accessed as <c>graphiti.nodes.saga</c>.</summary>
internal sealed class SagaNodeNamespace
{
    private readonly GraphDriver driver;
    private readonly SagaNodeOperations ops;

    internal SagaNodeNamespace(GraphDriver driver, SagaNodeOperations ops)
    {
        this.driver = driver;
        this.ops = ops;
    }

    internal async Task<SagaNode> SaveAsync(
        SagaNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveAsync(driver, node, tx, cancellationToken);
        return node;
    }

    internal async Task SaveBulkAsync(
        List<SagaNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveBulkAsync(driver, nodes, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        SagaNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteAsync(driver, node, tx, cancellationToken);
    }

    internal async Task DeleteByGroupIdAsync(
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByGroupIdAsync(driver, groupId, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByUuidsAsync(driver, uuids, tx, batchSize, cancellationToken);
    }

    internal async Task<SagaNode> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidAsync(driver, uuid, cancellationToken);
    }

    internal async Task<List<SagaNode>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidsAsync(driver, uuids, cancellationToken);
    }

    internal async Task<List<SagaNode>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByGroupIdsAsync(driver, groupIds, limit, uuidCursor, cancellationToken);
    }
}

/// <summary>
/// Namespace for all node operations. Accessed as <c>graphiti.nodes</c>.
///
/// Sub-namespaces are set only when the driver provides the corresponding
/// operations implementation.  Accessing an unset attribute raises
/// <c>NotImplementedError</c> with a clear message.
/// </summary>
internal sealed class NodeNamespace
{
    private readonly EntityNodeNamespace? entity;
    private readonly EpisodeNodeNamespace? episode;
    private readonly CommunityNodeNamespace? community;
    private readonly SagaNodeNamespace? saga;

    private readonly string driverName;

    // PORT: _driver_name is type(driver).__name__, which is PythonName (R15).
    internal NodeNamespace(GraphDriver driver, EmbedderClient embedder)
    {
        driverName = driver.PythonName;

        var entityNodeOps = driver.EntityNodeOps;
        if (entityNodeOps is not null)
        {
            entity = new EntityNodeNamespace(driver, entityNodeOps, embedder);
        }

        var episodeNodeOps = driver.EpisodeNodeOps;
        if (episodeNodeOps is not null)
        {
            episode = new EpisodeNodeNamespace(driver, episodeNodeOps);
        }

        var communityNodeOps = driver.CommunityNodeOps;
        if (communityNodeOps is not null)
        {
            community = new CommunityNodeNamespace(driver, communityNodeOps, embedder);
        }

        var sagaNodeOps = driver.SagaNodeOps;
        if (sagaNodeOps is not null)
        {
            saga = new SagaNodeNamespace(driver, sagaNodeOps);
        }
    }

    // PORT: the four properties stand for `__getattr__`, which Python defines after `__init__`. Python sets the attribute
    // only when the driver has the operations, and `__getattr__` raises NotImplementedError for it otherwise. Each getter
    // throws NotImplementedException in that case (R15).
    internal EntityNodeNamespace Entity => entity ?? throw new NotImplementedException(driverName + " does not implement entity_node_ops");

    internal EpisodeNodeNamespace Episode => episode ?? throw new NotImplementedException(driverName + " does not implement episode_node_ops");

    internal CommunityNodeNamespace Community => community ?? throw new NotImplementedException(driverName + " does not implement community_node_ops");

    internal SagaNodeNamespace Saga => saga ?? throw new NotImplementedException(driverName + " does not implement saga_node_ops");
}
