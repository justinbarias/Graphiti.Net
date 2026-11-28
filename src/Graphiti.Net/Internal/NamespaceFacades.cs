namespace Graphiti.Internal;

using static Graphiti.Internal.GraphitiFacade;
using Core = Graphiti.Core;
using M = Graphiti.Internal.PublicMapping;

// The namespace facades of the public surface design, "Namespaces". Each method maps its arguments, starts the method
// of the ported namespace on the scheduler, and maps the result. The ported namespace is read inside the call, so a
// driver with no operations fails in the call, as upstream's __getattr__ does. A method that changes its argument
// upstream returns the changed record.

/// <summary>The public form of the ported <c>NodeNamespace</c>.</summary>
internal sealed class NodeNamespacesFacade(Core.GraphitiCore core) : INodeNamespaces
{
    public IEntityNodeNamespace Entity { get; } = new EntityNodeNamespaceFacade(core);

    public IEpisodeNodeNamespace Episode { get; } = new EpisodeNodeNamespaceFacade(core);

    public ICommunityNodeNamespace Community { get; } = new CommunityNodeNamespaceFacade(core);

    public ISagaNodeNamespace Saga { get; } = new SagaNodeNamespaceFacade(core);
}

/// <summary>The public form of the ported <c>EdgeNamespace</c>.</summary>
internal sealed class EdgeNamespacesFacade(Core.GraphitiCore core) : IEdgeNamespaces
{
    public IEntityEdgeNamespace Entity { get; } = new EntityEdgeNamespaceFacade(core);

    public IEpisodicEdgeNamespace Episodic { get; } = new EpisodicEdgeNamespaceFacade(core);

    public ICommunityEdgeNamespace Community { get; } = new CommunityEdgeNamespaceFacade(core);

    public IHasEpisodeEdgeNamespace HasEpisode { get; } = new HasEpisodeEdgeNamespaceFacade(core);

    public INextEpisodeEdgeNamespace NextEpisode { get; } = new NextEpisodeEdgeNamespaceFacade(core);
}

internal sealed class EntityNodeNamespaceFacade(Core.GraphitiCore core) : IEntityNodeNamespace
{
    public Task<EntityNode> SaveAsync(EntityNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return RunAsync(async () => M.ToPublic(await core.Nodes.Entity.SaveAsync(M.ToPorted(node), cancellationToken: cancellationToken)), cancellationToken);
    }

    public Task SaveBulkAsync(IReadOnlyList<EntityNode> nodes, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return RunAsync(() => core.Nodes.Entity.SaveBulkAsync([.. nodes.Select(M.ToPorted)], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteAsync(EntityNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return RunAsync(() => core.Nodes.Entity.DeleteAsync(M.ToPorted(node), cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        return RunAsync(() => core.Nodes.Entity.DeleteByGroupIdAsync(groupId, batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync(() => core.Nodes.Entity.DeleteByUuidsAsync([.. uuids], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task<EntityNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        return RunAsync(async () => M.ToPublic(await core.Nodes.Entity.GetByUuidAsync(uuid, cancellationToken)), cancellationToken);
    }

    public Task<IReadOnlyList<EntityNode>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync<IReadOnlyList<EntityNode>>(async () => [.. (await core.Nodes.Entity.GetByUuidsAsync([.. uuids], cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<EntityNode>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        return RunAsync<IReadOnlyList<EntityNode>>(async () => [.. (await core.Nodes.Entity.GetByGroupIdsAsync([.. groupIds], limit, uuidCursor, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<EntityNode> LoadEmbeddingsAsync(EntityNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return RunAsync(
            async () =>
            {
                var ported = M.ToPorted(node);
                await core.Nodes.Entity.LoadEmbeddingsAsync(ported, cancellationToken);
                return M.ToPublic(ported);
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<EntityNode>> LoadEmbeddingsBulkAsync(
        IReadOnlyList<EntityNode> nodes, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return RunAsync<IReadOnlyList<EntityNode>>(
            async () =>
            {
                List<Core.EntityNode> ported = [.. nodes.Select(M.ToPorted)];
                await core.Nodes.Entity.LoadEmbeddingsBulkAsync(ported, batchSize, cancellationToken);
                return [.. ported.Select(M.ToPublic)];
            },
            cancellationToken);
    }
}

internal sealed class EpisodeNodeNamespaceFacade(Core.GraphitiCore core) : IEpisodeNodeNamespace
{
    public Task<EpisodicNode> SaveAsync(EpisodicNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return RunAsync(async () => M.ToPublic(await core.Nodes.Episode.SaveAsync(M.ToPorted(node), cancellationToken: cancellationToken)), cancellationToken);
    }

    public Task SaveBulkAsync(IReadOnlyList<EpisodicNode> nodes, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return RunAsync(() => core.Nodes.Episode.SaveBulkAsync([.. nodes.Select(M.ToPorted)], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteAsync(EpisodicNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return RunAsync(() => core.Nodes.Episode.DeleteAsync(M.ToPorted(node), cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        return RunAsync(() => core.Nodes.Episode.DeleteByGroupIdAsync(groupId, batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync(() => core.Nodes.Episode.DeleteByUuidsAsync([.. uuids], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task<EpisodicNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        return RunAsync(async () => M.ToPublic(await core.Nodes.Episode.GetByUuidAsync(uuid, cancellationToken)), cancellationToken);
    }

    public Task<IReadOnlyList<EpisodicNode>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync<IReadOnlyList<EpisodicNode>>(async () => [.. (await core.Nodes.Episode.GetByUuidsAsync([.. uuids], cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<EpisodicNode>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        return RunAsync<IReadOnlyList<EpisodicNode>>(async () => [.. (await core.Nodes.Episode.GetByGroupIdsAsync([.. groupIds], limit, uuidCursor, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<EpisodicNode>> GetByEntityNodeUuidAsync(string entityNodeUuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entityNodeUuid);
        return RunAsync<IReadOnlyList<EpisodicNode>>(async () => [.. (await core.Nodes.Episode.GetByEntityNodeUuidAsync(entityNodeUuid, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
        DateTimeOffset referenceTime,
        int lastN = 3,
        IReadOnlyList<string>? groupIds = null,
        string? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<EpisodicNode>>(
            async () => [.. (await core.Nodes.Episode.RetrieveEpisodesAsync(
                PublicValues.ToPortedDate(referenceTime), lastN, M.ToPortedList(groupIds), source, saga, cancellationToken)).Select(M.ToPublic)],
            cancellationToken);
}

internal sealed class CommunityNodeNamespaceFacade(Core.GraphitiCore core) : ICommunityNodeNamespace
{
    public Task<CommunityNode> SaveAsync(CommunityNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return RunAsync(async () => M.ToPublic(await core.Nodes.Community.SaveAsync(M.ToPorted(node), cancellationToken: cancellationToken)), cancellationToken);
    }

    public Task SaveBulkAsync(IReadOnlyList<CommunityNode> nodes, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return RunAsync(() => core.Nodes.Community.SaveBulkAsync([.. nodes.Select(M.ToPorted)], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteAsync(CommunityNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return RunAsync(() => core.Nodes.Community.DeleteAsync(M.ToPorted(node), cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        return RunAsync(() => core.Nodes.Community.DeleteByGroupIdAsync(groupId, batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync(() => core.Nodes.Community.DeleteByUuidsAsync([.. uuids], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task<CommunityNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        return RunAsync(async () => M.ToPublic(await core.Nodes.Community.GetByUuidAsync(uuid, cancellationToken)), cancellationToken);
    }

    public Task<IReadOnlyList<CommunityNode>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync<IReadOnlyList<CommunityNode>>(async () => [.. (await core.Nodes.Community.GetByUuidsAsync([.. uuids], cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<CommunityNode>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        return RunAsync<IReadOnlyList<CommunityNode>>(async () => [.. (await core.Nodes.Community.GetByGroupIdsAsync([.. groupIds], limit, uuidCursor, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<CommunityNode> LoadNameEmbeddingAsync(CommunityNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return RunAsync(
            async () =>
            {
                var ported = M.ToPorted(node);
                await core.Nodes.Community.LoadNameEmbeddingAsync(ported, cancellationToken);
                return M.ToPublic(ported);
            },
            cancellationToken);
    }
}

internal sealed class SagaNodeNamespaceFacade(Core.GraphitiCore core) : ISagaNodeNamespace
{
    public Task<SagaNode> SaveAsync(SagaNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return RunAsync(async () => M.ToPublic(await core.Nodes.Saga.SaveAsync(M.ToPorted(node), cancellationToken: cancellationToken)), cancellationToken);
    }

    public Task SaveBulkAsync(IReadOnlyList<SagaNode> nodes, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        return RunAsync(() => core.Nodes.Saga.SaveBulkAsync([.. nodes.Select(M.ToPorted)], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteAsync(SagaNode node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        return RunAsync(() => core.Nodes.Saga.DeleteAsync(M.ToPorted(node), cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupId);
        return RunAsync(() => core.Nodes.Saga.DeleteByGroupIdAsync(groupId, batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync(() => core.Nodes.Saga.DeleteByUuidsAsync([.. uuids], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task<SagaNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        return RunAsync(async () => M.ToPublic(await core.Nodes.Saga.GetByUuidAsync(uuid, cancellationToken)), cancellationToken);
    }

    public Task<IReadOnlyList<SagaNode>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync<IReadOnlyList<SagaNode>>(async () => [.. (await core.Nodes.Saga.GetByUuidsAsync([.. uuids], cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<SagaNode>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        return RunAsync<IReadOnlyList<SagaNode>>(async () => [.. (await core.Nodes.Saga.GetByGroupIdsAsync([.. groupIds], limit, uuidCursor, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }
}

internal sealed class EntityEdgeNamespaceFacade(Core.GraphitiCore core) : IEntityEdgeNamespace
{
    public Task<EntityEdge> SaveAsync(EntityEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return RunAsync(async () => M.ToPublic(await core.Edges.Entity.SaveAsync(M.ToPorted(edge), cancellationToken: cancellationToken)), cancellationToken);
    }

    public Task SaveBulkAsync(IReadOnlyList<EntityEdge> edges, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edges);
        return RunAsync(() => core.Edges.Entity.SaveBulkAsync([.. edges.Select(M.ToPorted)], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteAsync(EntityEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return RunAsync(() => core.Edges.Entity.DeleteAsync(M.ToPorted(edge), cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync(() => core.Edges.Entity.DeleteByUuidsAsync([.. uuids], cancellationToken: cancellationToken), cancellationToken);
    }

    public Task<EntityEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        return RunAsync(async () => M.ToPublic(await core.Edges.Entity.GetByUuidAsync(uuid, cancellationToken)), cancellationToken);
    }

    public Task<IReadOnlyList<EntityEdge>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync<IReadOnlyList<EntityEdge>>(async () => [.. (await core.Edges.Entity.GetByUuidsAsync([.. uuids], cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<EntityEdge>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        return RunAsync<IReadOnlyList<EntityEdge>>(async () => [.. (await core.Edges.Entity.GetByGroupIdsAsync([.. groupIds], limit, uuidCursor, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<EntityEdge>> GetBetweenNodesAsync(
        string sourceNodeUuid, string targetNodeUuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceNodeUuid);
        ArgumentNullException.ThrowIfNull(targetNodeUuid);
        return RunAsync<IReadOnlyList<EntityEdge>>(async () => [.. (await core.Edges.Entity.GetBetweenNodesAsync(sourceNodeUuid, targetNodeUuid, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<EntityEdge>> GetByNodeUuidAsync(string nodeUuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(nodeUuid);
        return RunAsync<IReadOnlyList<EntityEdge>>(async () => [.. (await core.Edges.Entity.GetByNodeUuidAsync(nodeUuid, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<EntityEdge> LoadEmbeddingsAsync(EntityEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return RunAsync(
            async () =>
            {
                var ported = M.ToPorted(edge);
                await core.Edges.Entity.LoadEmbeddingsAsync(ported, cancellationToken);
                return M.ToPublic(ported);
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<EntityEdge>> LoadEmbeddingsBulkAsync(
        IReadOnlyList<EntityEdge> edges, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edges);
        return RunAsync<IReadOnlyList<EntityEdge>>(
            async () =>
            {
                List<Core.EntityEdge> ported = [.. edges.Select(M.ToPorted)];
                await core.Edges.Entity.LoadEmbeddingsBulkAsync(ported, batchSize, cancellationToken);
                return [.. ported.Select(M.ToPublic)];
            },
            cancellationToken);
    }
}

internal sealed class EpisodicEdgeNamespaceFacade(Core.GraphitiCore core) : IEpisodicEdgeNamespace
{
    public Task<EpisodicEdge> SaveAsync(EpisodicEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return RunAsync(async () => M.ToPublic(await core.Edges.Episodic.SaveAsync(M.ToPorted(edge), cancellationToken: cancellationToken)), cancellationToken);
    }

    public Task SaveBulkAsync(IReadOnlyList<EpisodicEdge> edges, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edges);
        return RunAsync(() => core.Edges.Episodic.SaveBulkAsync([.. edges.Select(M.ToPorted)], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteAsync(EpisodicEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return RunAsync(() => core.Edges.Episodic.DeleteAsync(M.ToPorted(edge), cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync(() => core.Edges.Episodic.DeleteByUuidsAsync([.. uuids], cancellationToken: cancellationToken), cancellationToken);
    }

    public Task<EpisodicEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        return RunAsync(async () => M.ToPublic(await core.Edges.Episodic.GetByUuidAsync(uuid, cancellationToken)), cancellationToken);
    }

    public Task<IReadOnlyList<EpisodicEdge>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync<IReadOnlyList<EpisodicEdge>>(async () => [.. (await core.Edges.Episodic.GetByUuidsAsync([.. uuids], cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<EpisodicEdge>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        return RunAsync<IReadOnlyList<EpisodicEdge>>(async () => [.. (await core.Edges.Episodic.GetByGroupIdsAsync([.. groupIds], limit, uuidCursor, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }
}

internal sealed class CommunityEdgeNamespaceFacade(Core.GraphitiCore core) : ICommunityEdgeNamespace
{
    public Task<CommunityEdge> SaveAsync(CommunityEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return RunAsync(async () => M.ToPublic(await core.Edges.Community.SaveAsync(M.ToPorted(edge), cancellationToken: cancellationToken)), cancellationToken);
    }

    public Task DeleteAsync(CommunityEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return RunAsync(() => core.Edges.Community.DeleteAsync(M.ToPorted(edge), cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync(() => core.Edges.Community.DeleteByUuidsAsync([.. uuids], cancellationToken: cancellationToken), cancellationToken);
    }

    public Task<CommunityEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        return RunAsync(async () => M.ToPublic(await core.Edges.Community.GetByUuidAsync(uuid, cancellationToken)), cancellationToken);
    }

    public Task<IReadOnlyList<CommunityEdge>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync<IReadOnlyList<CommunityEdge>>(async () => [.. (await core.Edges.Community.GetByUuidsAsync([.. uuids], cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<CommunityEdge>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        return RunAsync<IReadOnlyList<CommunityEdge>>(async () => [.. (await core.Edges.Community.GetByGroupIdsAsync([.. groupIds], limit, uuidCursor, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }
}

internal sealed class HasEpisodeEdgeNamespaceFacade(Core.GraphitiCore core) : IHasEpisodeEdgeNamespace
{
    public Task<HasEpisodeEdge> SaveAsync(HasEpisodeEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return RunAsync(async () => M.ToPublic(await core.Edges.HasEpisode.SaveAsync(M.ToPorted(edge), cancellationToken: cancellationToken)), cancellationToken);
    }

    public Task SaveBulkAsync(IReadOnlyList<HasEpisodeEdge> edges, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edges);
        return RunAsync(() => core.Edges.HasEpisode.SaveBulkAsync([.. edges.Select(M.ToPorted)], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteAsync(HasEpisodeEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return RunAsync(() => core.Edges.HasEpisode.DeleteAsync(M.ToPorted(edge), cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync(() => core.Edges.HasEpisode.DeleteByUuidsAsync([.. uuids], cancellationToken: cancellationToken), cancellationToken);
    }

    public Task<HasEpisodeEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        return RunAsync(async () => M.ToPublic(await core.Edges.HasEpisode.GetByUuidAsync(uuid, cancellationToken)), cancellationToken);
    }

    public Task<IReadOnlyList<HasEpisodeEdge>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync<IReadOnlyList<HasEpisodeEdge>>(async () => [.. (await core.Edges.HasEpisode.GetByUuidsAsync([.. uuids], cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<HasEpisodeEdge>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        return RunAsync<IReadOnlyList<HasEpisodeEdge>>(async () => [.. (await core.Edges.HasEpisode.GetByGroupIdsAsync([.. groupIds], limit, uuidCursor, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }
}

internal sealed class NextEpisodeEdgeNamespaceFacade(Core.GraphitiCore core) : INextEpisodeEdgeNamespace
{
    public Task<NextEpisodeEdge> SaveAsync(NextEpisodeEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return RunAsync(async () => M.ToPublic(await core.Edges.NextEpisode.SaveAsync(M.ToPorted(edge), cancellationToken: cancellationToken)), cancellationToken);
    }

    public Task SaveBulkAsync(IReadOnlyList<NextEpisodeEdge> edges, int batchSize = 100, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edges);
        return RunAsync(() => core.Edges.NextEpisode.SaveBulkAsync([.. edges.Select(M.ToPorted)], batchSize: batchSize, cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteAsync(NextEpisodeEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return RunAsync(() => core.Edges.NextEpisode.DeleteAsync(M.ToPorted(edge), cancellationToken: cancellationToken), cancellationToken);
    }

    public Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync(() => core.Edges.NextEpisode.DeleteByUuidsAsync([.. uuids], cancellationToken: cancellationToken), cancellationToken);
    }

    public Task<NextEpisodeEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuid);
        return RunAsync(async () => M.ToPublic(await core.Edges.NextEpisode.GetByUuidAsync(uuid, cancellationToken)), cancellationToken);
    }

    public Task<IReadOnlyList<NextEpisodeEdge>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uuids);
        return RunAsync<IReadOnlyList<NextEpisodeEdge>>(async () => [.. (await core.Edges.NextEpisode.GetByUuidsAsync([.. uuids], cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }

    public Task<IReadOnlyList<NextEpisodeEdge>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(groupIds);
        return RunAsync<IReadOnlyList<NextEpisodeEdge>>(async () => [.. (await core.Edges.NextEpisode.GetByGroupIdsAsync([.. groupIds], limit, uuidCursor, cancellationToken)).Select(M.ToPublic)], cancellationToken);
    }
}
