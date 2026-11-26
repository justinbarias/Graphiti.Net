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

/// <summary>Namespace for entity edge operations. Accessed as <c>graphiti.edges.entity</c>.</summary>
internal sealed class EntityEdgeNamespace
{
    private readonly GraphDriver driver;
    private readonly EntityEdgeOperations ops;
    private readonly EmbedderClient embedder;

    internal EntityEdgeNamespace(GraphDriver driver, EntityEdgeOperations ops, EmbedderClient embedder)
    {
        this.driver = driver;
        this.ops = ops;
        this.embedder = embedder;
    }

    internal async Task<EntityEdge> SaveAsync(
        EntityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await edge.GenerateEmbeddingAsync(embedder, cancellationToken);
        await ops.SaveAsync(driver, edge, tx, cancellationToken);
        return edge;
    }

    internal async Task SaveBulkAsync(
        List<EntityEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveBulkAsync(driver, edges, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        EntityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteAsync(driver, edge, tx, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByUuidsAsync(driver, uuids, tx, cancellationToken);
    }

    internal async Task<EntityEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidAsync(driver, uuid, cancellationToken);
    }

    internal async Task<List<EntityEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidsAsync(driver, uuids, cancellationToken);
    }

    internal async Task<List<EntityEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByGroupIdsAsync(driver, groupIds, limit, uuidCursor, cancellationToken);
    }

    internal async Task<List<EntityEdge>> GetBetweenNodesAsync(
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetBetweenNodesAsync(driver, sourceNodeUuid, targetNodeUuid, cancellationToken);
    }

    internal async Task<List<EntityEdge>> GetByNodeUuidAsync(
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByNodeUuidAsync(driver, nodeUuid, cancellationToken);
    }

    internal async Task LoadEmbeddingsAsync(
        EntityEdge edge,
        CancellationToken cancellationToken = default)
    {
        await ops.LoadEmbeddingsAsync(driver, edge, cancellationToken);
    }

    internal async Task LoadEmbeddingsBulkAsync(
        List<EntityEdge> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.LoadEmbeddingsBulkAsync(driver, edges, batchSize, cancellationToken);
    }
}

/// <summary>Namespace for episodic edge operations. Accessed as <c>graphiti.edges.episodic</c>.</summary>
internal sealed class EpisodicEdgeNamespace
{
    private readonly GraphDriver driver;
    private readonly EpisodicEdgeOperations ops;

    internal EpisodicEdgeNamespace(GraphDriver driver, EpisodicEdgeOperations ops)
    {
        this.driver = driver;
        this.ops = ops;
    }

    internal async Task<EpisodicEdge> SaveAsync(
        EpisodicEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveAsync(driver, edge, tx, cancellationToken);
        return edge;
    }

    internal async Task SaveBulkAsync(
        List<EpisodicEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveBulkAsync(driver, edges, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        EpisodicEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteAsync(driver, edge, tx, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByUuidsAsync(driver, uuids, tx, cancellationToken);
    }

    internal async Task<EpisodicEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidAsync(driver, uuid, cancellationToken);
    }

    internal async Task<List<EpisodicEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidsAsync(driver, uuids, cancellationToken);
    }

    internal async Task<List<EpisodicEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByGroupIdsAsync(driver, groupIds, limit, uuidCursor, cancellationToken);
    }
}

/// <summary>Namespace for community edge operations. Accessed as <c>graphiti.edges.community</c>.</summary>
internal sealed class CommunityEdgeNamespace
{
    private readonly GraphDriver driver;
    private readonly CommunityEdgeOperations ops;

    internal CommunityEdgeNamespace(GraphDriver driver, CommunityEdgeOperations ops)
    {
        this.driver = driver;
        this.ops = ops;
    }

    internal async Task<CommunityEdge> SaveAsync(
        CommunityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveAsync(driver, edge, tx, cancellationToken);
        return edge;
    }

    internal async Task DeleteAsync(
        CommunityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteAsync(driver, edge, tx, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByUuidsAsync(driver, uuids, tx, cancellationToken);
    }

    internal async Task<CommunityEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidAsync(driver, uuid, cancellationToken);
    }

    internal async Task<List<CommunityEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidsAsync(driver, uuids, cancellationToken);
    }

    internal async Task<List<CommunityEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByGroupIdsAsync(driver, groupIds, limit, uuidCursor, cancellationToken);
    }
}

/// <summary>Namespace for has_episode edge operations. Accessed as <c>graphiti.edges.has_episode</c>.</summary>
internal sealed class HasEpisodeEdgeNamespace
{
    private readonly GraphDriver driver;
    private readonly HasEpisodeEdgeOperations ops;

    internal HasEpisodeEdgeNamespace(GraphDriver driver, HasEpisodeEdgeOperations ops)
    {
        this.driver = driver;
        this.ops = ops;
    }

    internal async Task<HasEpisodeEdge> SaveAsync(
        HasEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveAsync(driver, edge, tx, cancellationToken);
        return edge;
    }

    internal async Task SaveBulkAsync(
        List<HasEpisodeEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveBulkAsync(driver, edges, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        HasEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteAsync(driver, edge, tx, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByUuidsAsync(driver, uuids, tx, cancellationToken);
    }

    internal async Task<HasEpisodeEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidAsync(driver, uuid, cancellationToken);
    }

    internal async Task<List<HasEpisodeEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidsAsync(driver, uuids, cancellationToken);
    }

    internal async Task<List<HasEpisodeEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByGroupIdsAsync(driver, groupIds, limit, uuidCursor, cancellationToken);
    }
}

/// <summary>Namespace for next_episode edge operations. Accessed as <c>graphiti.edges.next_episode</c>.</summary>
internal sealed class NextEpisodeEdgeNamespace
{
    private readonly GraphDriver driver;
    private readonly NextEpisodeEdgeOperations ops;

    internal NextEpisodeEdgeNamespace(GraphDriver driver, NextEpisodeEdgeOperations ops)
    {
        this.driver = driver;
        this.ops = ops;
    }

    internal async Task<NextEpisodeEdge> SaveAsync(
        NextEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveAsync(driver, edge, tx, cancellationToken);
        return edge;
    }

    internal async Task SaveBulkAsync(
        List<NextEpisodeEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        await ops.SaveBulkAsync(driver, edges, tx, batchSize, cancellationToken);
    }

    internal async Task DeleteAsync(
        NextEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteAsync(driver, edge, tx, cancellationToken);
    }

    internal async Task DeleteByUuidsAsync(
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        await ops.DeleteByUuidsAsync(driver, uuids, tx, cancellationToken);
    }

    internal async Task<NextEpisodeEdge> GetByUuidAsync(
        string uuid,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidAsync(driver, uuid, cancellationToken);
    }

    internal async Task<List<NextEpisodeEdge>> GetByUuidsAsync(
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByUuidsAsync(driver, uuids, cancellationToken);
    }

    internal async Task<List<NextEpisodeEdge>> GetByGroupIdsAsync(
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        return await ops.GetByGroupIdsAsync(driver, groupIds, limit, uuidCursor, cancellationToken);
    }
}

/// <summary>
/// Namespace for all edge operations. Accessed as <c>graphiti.edges</c>.
///
/// Sub-namespaces are set only when the driver provides the corresponding
/// operations implementation.  Accessing an unset attribute raises
/// <c>NotImplementedError</c> with a clear message.
/// </summary>
internal sealed class EdgeNamespace
{
    private readonly EntityEdgeNamespace? entity;
    private readonly EpisodicEdgeNamespace? episodic;
    private readonly CommunityEdgeNamespace? community;
    private readonly HasEpisodeEdgeNamespace? hasEpisode;
    private readonly NextEpisodeEdgeNamespace? nextEpisode;

    private readonly string driverName;

    // PORT: _driver_name is type(driver).__name__, which is PythonName (R15).
    internal EdgeNamespace(GraphDriver driver, EmbedderClient embedder)
    {
        driverName = driver.PythonName;

        var entityEdgeOps = driver.EntityEdgeOps;
        if (entityEdgeOps is not null)
        {
            entity = new EntityEdgeNamespace(driver, entityEdgeOps, embedder);
        }

        var episodicEdgeOps = driver.EpisodicEdgeOps;
        if (episodicEdgeOps is not null)
        {
            episodic = new EpisodicEdgeNamespace(driver, episodicEdgeOps);
        }

        var communityEdgeOps = driver.CommunityEdgeOps;
        if (communityEdgeOps is not null)
        {
            community = new CommunityEdgeNamespace(driver, communityEdgeOps);
        }

        var hasEpisodeEdgeOps = driver.HasEpisodeEdgeOps;
        if (hasEpisodeEdgeOps is not null)
        {
            hasEpisode = new HasEpisodeEdgeNamespace(driver, hasEpisodeEdgeOps);
        }

        var nextEpisodeEdgeOps = driver.NextEpisodeEdgeOps;
        if (nextEpisodeEdgeOps is not null)
        {
            nextEpisode = new NextEpisodeEdgeNamespace(driver, nextEpisodeEdgeOps);
        }
    }

    // PORT: the five properties stand for `__getattr__`, which Python defines after `__init__`. Python sets the attribute
    // only when the driver has the operations, and `__getattr__` raises NotImplementedError for it otherwise. Each getter
    // throws NotImplementedException in that case (R15).
    internal EntityEdgeNamespace Entity => entity ?? throw new NotImplementedException(driverName + " does not implement entity_edge_ops");

    internal EpisodicEdgeNamespace Episodic => episodic ?? throw new NotImplementedException(driverName + " does not implement episodic_edge_ops");

    internal CommunityEdgeNamespace Community => community ?? throw new NotImplementedException(driverName + " does not implement community_edge_ops");

    internal HasEpisodeEdgeNamespace HasEpisode => hasEpisode ?? throw new NotImplementedException(driverName + " does not implement has_episode_edge_ops");

    internal NextEpisodeEdgeNamespace NextEpisode => nextEpisode ?? throw new NotImplementedException(driverName + " does not implement next_episode_edge_ops");
}
