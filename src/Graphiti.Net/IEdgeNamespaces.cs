namespace Graphiti;

/// <summary>
/// The edge namespaces of upstream's <c>graphiti.edges</c>. Each method has the parameters and the defaults of upstream,
/// without the <c>tx</c> parameter. A method that changes its argument upstream returns the changed record. A call
/// applies no group filter beyond what upstream applies, so a host with many tenants makes sure that each UUID belongs
/// to its tenant.
/// </summary>
public interface IEdgeNamespaces
{
    /// <summary>Gets the namespace of the entity edges: upstream's <c>edges.entity</c>.</summary>
    IEntityEdgeNamespace Entity { get; }

    /// <summary>Gets the namespace of the episodic edges: upstream's <c>edges.episodic</c>.</summary>
    IEpisodicEdgeNamespace Episodic { get; }

    /// <summary>Gets the namespace of the community edges: upstream's <c>edges.community</c>.</summary>
    ICommunityEdgeNamespace Community { get; }

    /// <summary>Gets the namespace of the edges from a saga to its episodes: upstream's <c>edges.has_episode</c>.</summary>
    IHasEpisodeEdgeNamespace HasEpisode { get; }

    /// <summary>Gets the namespace of the edges between episodes of a saga: upstream's <c>edges.next_episode</c>.</summary>
    INextEpisodeEdgeNamespace NextEpisode { get; }
}

/// <summary>The entity edges: upstream's <c>EntityEdgeNamespace</c>.</summary>
public interface IEntityEdgeNamespace
{
    /// <summary>Embeds the fact if the edge has no fact embedding, and saves the edge. Upstream's <c>save</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edge with its fact embedding.</returns>
    Task<EntityEdge> SaveAsync(EntityEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Saves the edges in batches. Upstream's <c>save_bulk</c>. An edge with no fact embedding fails (GAPS U7).</summary>
    /// <param name="edges">The edges.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edges are saved.</returns>
    Task SaveBulkAsync(IReadOnlyList<EntityEdge> edges, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the edge. Upstream's <c>delete</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edge is deleted.</returns>
    Task DeleteAsync(EntityEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Deletes the edges with these UUIDs. Upstream's <c>delete_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edges are deleted.</returns>
    Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads an edge. Upstream's <c>get_by_uuid</c>. A missing edge throws <see cref="EdgeNotFoundException"/>.</summary>
    /// <param name="uuid">The UUID.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edge.</returns>
    Task<EntityEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges with these UUIDs. Upstream's <c>get_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges that exist.</returns>
    Task<IReadOnlyList<EntityEdge>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges of the groups, a page at a time. Upstream's <c>get_by_group_ids</c>.</summary>
    /// <param name="groupIds">The groups.</param>
    /// <param name="limit">The size of the page, or null for no limit.</param>
    /// <param name="uuidCursor">The UUID after which the page starts, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges.</returns>
    Task<IReadOnlyList<EntityEdge>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges from one node to another. Upstream's <c>get_between_nodes</c>.</summary>
    /// <param name="sourceNodeUuid">The UUID of the source node.</param>
    /// <param name="targetNodeUuid">The UUID of the target node.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges.</returns>
    Task<IReadOnlyList<EntityEdge>> GetBetweenNodesAsync(
        string sourceNodeUuid, string targetNodeUuid, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges of a node. Upstream's <c>get_by_node_uuid</c>.</summary>
    /// <param name="nodeUuid">The UUID of the node.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges.</returns>
    Task<IReadOnlyList<EntityEdge>> GetByNodeUuidAsync(string nodeUuid, CancellationToken cancellationToken = default);

    /// <summary>Reads the fact embedding of the edge. Upstream's <c>load_embeddings</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edge with its fact embedding.</returns>
    Task<EntityEdge> LoadEmbeddingsAsync(EntityEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Reads the fact embeddings of the edges. Upstream's <c>load_embeddings_bulk</c>.</summary>
    /// <param name="edges">The edges.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges with their fact embeddings, in the order of the argument.</returns>
    Task<IReadOnlyList<EntityEdge>> LoadEmbeddingsBulkAsync(
        IReadOnlyList<EntityEdge> edges, int batchSize = 100, CancellationToken cancellationToken = default);
}

/// <summary>The edges from an episode to the entities that it mentions: upstream's <c>EpisodicEdgeNamespace</c>.</summary>
public interface IEpisodicEdgeNamespace
{
    /// <summary>Saves the edge. Upstream's <c>save</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edge.</returns>
    Task<EpisodicEdge> SaveAsync(EpisodicEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Saves the edges in batches. Upstream's <c>save_bulk</c>.</summary>
    /// <param name="edges">The edges.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edges are saved.</returns>
    Task SaveBulkAsync(IReadOnlyList<EpisodicEdge> edges, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the edge. Upstream's <c>delete</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edge is deleted.</returns>
    Task DeleteAsync(EpisodicEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Deletes the edges with these UUIDs. Upstream's <c>delete_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edges are deleted.</returns>
    Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads an edge. Upstream's <c>get_by_uuid</c>. A missing edge throws <see cref="EdgeNotFoundException"/>.</summary>
    /// <param name="uuid">The UUID.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edge.</returns>
    Task<EpisodicEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges with these UUIDs. Upstream's <c>get_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges that exist.</returns>
    Task<IReadOnlyList<EpisodicEdge>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges of the groups, a page at a time. Upstream's <c>get_by_group_ids</c>.</summary>
    /// <param name="groupIds">The groups.</param>
    /// <param name="limit">The size of the page, or null for no limit.</param>
    /// <param name="uuidCursor">The UUID after which the page starts, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges.</returns>
    Task<IReadOnlyList<EpisodicEdge>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default);
}

/// <summary>The edges from a community to its members: upstream's <c>CommunityEdgeNamespace</c>.</summary>
public interface ICommunityEdgeNamespace
{
    /// <summary>Saves the edge. Upstream's <c>save</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edge.</returns>
    Task<CommunityEdge> SaveAsync(CommunityEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Deletes the edge. Upstream's <c>delete</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edge is deleted.</returns>
    Task DeleteAsync(CommunityEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Deletes the edges with these UUIDs. Upstream's <c>delete_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edges are deleted.</returns>
    Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads an edge. Upstream's <c>get_by_uuid</c>. A missing edge throws <see cref="EdgeNotFoundException"/>.</summary>
    /// <param name="uuid">The UUID.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edge.</returns>
    Task<CommunityEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges with these UUIDs. Upstream's <c>get_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges that exist.</returns>
    Task<IReadOnlyList<CommunityEdge>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges of the groups, a page at a time. Upstream's <c>get_by_group_ids</c>.</summary>
    /// <param name="groupIds">The groups.</param>
    /// <param name="limit">The size of the page, or null for no limit.</param>
    /// <param name="uuidCursor">The UUID after which the page starts, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges.</returns>
    Task<IReadOnlyList<CommunityEdge>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default);
}

/// <summary>The edges from a saga to its episodes: upstream's <c>HasEpisodeEdgeNamespace</c>.</summary>
public interface IHasEpisodeEdgeNamespace
{
    /// <summary>Saves the edge. Upstream's <c>save</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edge.</returns>
    Task<HasEpisodeEdge> SaveAsync(HasEpisodeEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Saves the edges in batches. Upstream's <c>save_bulk</c>.</summary>
    /// <param name="edges">The edges.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edges are saved.</returns>
    Task SaveBulkAsync(IReadOnlyList<HasEpisodeEdge> edges, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the edge. Upstream's <c>delete</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edge is deleted.</returns>
    Task DeleteAsync(HasEpisodeEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Deletes the edges with these UUIDs. Upstream's <c>delete_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edges are deleted.</returns>
    Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads an edge. Upstream's <c>get_by_uuid</c>. A missing edge throws <see cref="EdgeNotFoundException"/>.</summary>
    /// <param name="uuid">The UUID.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edge.</returns>
    Task<HasEpisodeEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges with these UUIDs. Upstream's <c>get_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges that exist.</returns>
    Task<IReadOnlyList<HasEpisodeEdge>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges of the groups, a page at a time. Upstream's <c>get_by_group_ids</c>.</summary>
    /// <param name="groupIds">The groups.</param>
    /// <param name="limit">The size of the page, or null for no limit.</param>
    /// <param name="uuidCursor">The UUID after which the page starts, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges.</returns>
    Task<IReadOnlyList<HasEpisodeEdge>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default);
}

/// <summary>The edges from an episode of a saga to the next one: upstream's <c>NextEpisodeEdgeNamespace</c>.</summary>
public interface INextEpisodeEdgeNamespace
{
    /// <summary>Saves the edge. Upstream's <c>save</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edge.</returns>
    Task<NextEpisodeEdge> SaveAsync(NextEpisodeEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Saves the edges in batches. Upstream's <c>save_bulk</c>.</summary>
    /// <param name="edges">The edges.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edges are saved.</returns>
    Task SaveBulkAsync(IReadOnlyList<NextEpisodeEdge> edges, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the edge. Upstream's <c>delete</c>.</summary>
    /// <param name="edge">The edge.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edge is deleted.</returns>
    Task DeleteAsync(NextEpisodeEdge edge, CancellationToken cancellationToken = default);

    /// <summary>Deletes the edges with these UUIDs. Upstream's <c>delete_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the edges are deleted.</returns>
    Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads an edge. Upstream's <c>get_by_uuid</c>. A missing edge throws <see cref="EdgeNotFoundException"/>.</summary>
    /// <param name="uuid">The UUID.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edge.</returns>
    Task<NextEpisodeEdge> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges with these UUIDs. Upstream's <c>get_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges that exist.</returns>
    Task<IReadOnlyList<NextEpisodeEdge>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads the edges of the groups, a page at a time. Upstream's <c>get_by_group_ids</c>.</summary>
    /// <param name="groupIds">The groups.</param>
    /// <param name="limit">The size of the page, or null for no limit.</param>
    /// <param name="uuidCursor">The UUID after which the page starts, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges.</returns>
    Task<IReadOnlyList<NextEpisodeEdge>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default);
}
