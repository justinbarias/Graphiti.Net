namespace Graphiti;

/// <summary>
/// The node namespaces of upstream's <c>graphiti.nodes</c>. Each method has the parameters and the defaults of upstream,
/// without the <c>tx</c> parameter. A method that changes its argument upstream returns the changed record. The methods
/// reproduce upstream defects: the batched deletes and the saga save fail as upstream fails (GAPS U1, U2). A call applies
/// no group filter beyond what upstream applies, so a host with many tenants makes sure that each UUID belongs to its
/// tenant.
/// </summary>
public interface INodeNamespaces
{
    /// <summary>Gets the namespace of the entity nodes: upstream's <c>nodes.entity</c>.</summary>
    IEntityNodeNamespace Entity { get; }

    /// <summary>Gets the namespace of the episodes: upstream's <c>nodes.episode</c>.</summary>
    IEpisodeNodeNamespace Episode { get; }

    /// <summary>Gets the namespace of the community nodes: upstream's <c>nodes.community</c>.</summary>
    ICommunityNodeNamespace Community { get; }

    /// <summary>Gets the namespace of the saga nodes: upstream's <c>nodes.saga</c>.</summary>
    ISagaNodeNamespace Saga { get; }
}

/// <summary>The entity nodes: upstream's <c>EntityNodeNamespace</c>.</summary>
public interface IEntityNodeNamespace
{
    /// <summary>Embeds the name if the node has no name embedding, and saves the node. Upstream's <c>save</c>.</summary>
    /// <param name="node">The node.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The node with its name embedding.</returns>
    Task<EntityNode> SaveAsync(EntityNode node, CancellationToken cancellationToken = default);

    /// <summary>Saves the nodes in batches. Upstream's <c>save_bulk</c>. A node with no name embedding fails (GAPS U7).</summary>
    /// <param name="nodes">The nodes.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the nodes are saved.</returns>
    Task SaveBulkAsync(IReadOnlyList<EntityNode> nodes, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the node and its edges. Upstream's <c>delete</c>.</summary>
    /// <param name="node">The node.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the node is deleted.</returns>
    Task DeleteAsync(EntityNode node, CancellationToken cancellationToken = default);

    /// <summary>Deletes the nodes of a group. Upstream's <c>delete_by_group_id</c>. It fails as upstream fails (GAPS U1).</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the nodes are deleted.</returns>
    Task DeleteByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the nodes with these UUIDs. Upstream's <c>delete_by_uuids</c>. It fails as upstream fails (GAPS U1).</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the nodes are deleted.</returns>
    Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Reads a node. Upstream's <c>get_by_uuid</c>. A missing node throws <see cref="NodeNotFoundException"/>.</summary>
    /// <param name="uuid">The UUID.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The node.</returns>
    Task<EntityNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>Reads the nodes with these UUIDs. Upstream's <c>get_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The nodes that exist.</returns>
    Task<IReadOnlyList<EntityNode>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads the nodes of the groups, a page at a time. Upstream's <c>get_by_group_ids</c>.</summary>
    /// <param name="groupIds">The groups.</param>
    /// <param name="limit">The size of the page, or null for no limit.</param>
    /// <param name="uuidCursor">The UUID after which the page starts, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The nodes.</returns>
    Task<IReadOnlyList<EntityNode>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default);

    /// <summary>Reads the name embedding of the node. Upstream's <c>load_embeddings</c>.</summary>
    /// <param name="node">The node.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The node with its name embedding.</returns>
    Task<EntityNode> LoadEmbeddingsAsync(EntityNode node, CancellationToken cancellationToken = default);

    /// <summary>Reads the name embeddings of the nodes. Upstream's <c>load_embeddings_bulk</c>.</summary>
    /// <param name="nodes">The nodes.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The nodes with their name embeddings, in the order of the argument.</returns>
    Task<IReadOnlyList<EntityNode>> LoadEmbeddingsBulkAsync(
        IReadOnlyList<EntityNode> nodes, int batchSize = 100, CancellationToken cancellationToken = default);
}

/// <summary>The episodes: upstream's <c>EpisodeNodeNamespace</c>.</summary>
public interface IEpisodeNodeNamespace
{
    /// <summary>Saves the episode. Upstream's <c>save</c>.</summary>
    /// <param name="node">The episode.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The episode.</returns>
    Task<EpisodicNode> SaveAsync(EpisodicNode node, CancellationToken cancellationToken = default);

    /// <summary>Saves the episodes in batches. Upstream's <c>save_bulk</c>.</summary>
    /// <param name="nodes">The episodes.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the episodes are saved.</returns>
    Task SaveBulkAsync(IReadOnlyList<EpisodicNode> nodes, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the episode and its edges. Upstream's <c>delete</c>.</summary>
    /// <param name="node">The episode.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the episode is deleted.</returns>
    Task DeleteAsync(EpisodicNode node, CancellationToken cancellationToken = default);

    /// <summary>Deletes the episodes of a group. Upstream's <c>delete_by_group_id</c>. It fails as upstream fails (GAPS U1).</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the episodes are deleted.</returns>
    Task DeleteByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the episodes with these UUIDs. Upstream's <c>delete_by_uuids</c>. It fails as upstream fails (GAPS U1).</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the episodes are deleted.</returns>
    Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Reads an episode. Upstream's <c>get_by_uuid</c>. A missing episode throws <see cref="NodeNotFoundException"/>.</summary>
    /// <param name="uuid">The UUID.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The episode.</returns>
    Task<EpisodicNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>Reads the episodes with these UUIDs. Upstream's <c>get_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The episodes that exist.</returns>
    Task<IReadOnlyList<EpisodicNode>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads the episodes of the groups, a page at a time. Upstream's <c>get_by_group_ids</c>.</summary>
    /// <param name="groupIds">The groups.</param>
    /// <param name="limit">The size of the page, or null for no limit.</param>
    /// <param name="uuidCursor">The UUID after which the page starts, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The episodes.</returns>
    Task<IReadOnlyList<EpisodicNode>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default);

    /// <summary>Reads the episodes that mention an entity. Upstream's <c>get_by_entity_node_uuid</c>.</summary>
    /// <param name="entityNodeUuid">The UUID of the entity node.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The episodes.</returns>
    Task<IReadOnlyList<EpisodicNode>> GetByEntityNodeUuidAsync(string entityNodeUuid, CancellationToken cancellationToken = default);

    /// <summary>Returns the last episodes before a reference time. Upstream's <c>retrieve_episodes</c>.</summary>
    /// <param name="referenceTime">The reference time.</param>
    /// <param name="lastN">The number of episodes. The default is 3.</param>
    /// <param name="groupIds">The groups, or null for each group.</param>
    /// <param name="source">The source type text, for example <c>message</c>, or null for each type.</param>
    /// <param name="saga">The name of a saga, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The episodes.</returns>
    Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
        DateTimeOffset referenceTime,
        int lastN = 3,
        IReadOnlyList<string>? groupIds = null,
        string? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The community nodes: upstream's <c>CommunityNodeNamespace</c>.</summary>
public interface ICommunityNodeNamespace
{
    /// <summary>Embeds the name if the node has no name embedding, and saves the node. Upstream's <c>save</c>.</summary>
    /// <param name="node">The node.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The node with its name embedding.</returns>
    Task<CommunityNode> SaveAsync(CommunityNode node, CancellationToken cancellationToken = default);

    /// <summary>Saves the nodes in batches. Upstream's <c>save_bulk</c>.</summary>
    /// <param name="nodes">The nodes.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the nodes are saved.</returns>
    Task SaveBulkAsync(IReadOnlyList<CommunityNode> nodes, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the node and its edges. Upstream's <c>delete</c>.</summary>
    /// <param name="node">The node.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the node is deleted.</returns>
    Task DeleteAsync(CommunityNode node, CancellationToken cancellationToken = default);

    /// <summary>Deletes the nodes of a group. Upstream's <c>delete_by_group_id</c>. It fails as upstream fails (GAPS U1).</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the nodes are deleted.</returns>
    Task DeleteByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the nodes with these UUIDs. Upstream's <c>delete_by_uuids</c>. It fails as upstream fails (GAPS U1).</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the nodes are deleted.</returns>
    Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Reads a node. Upstream's <c>get_by_uuid</c>. A missing node throws <see cref="NodeNotFoundException"/>.</summary>
    /// <param name="uuid">The UUID.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The node.</returns>
    Task<CommunityNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>Reads the nodes with these UUIDs. Upstream's <c>get_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The nodes that exist.</returns>
    Task<IReadOnlyList<CommunityNode>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads the nodes of the groups, a page at a time. Upstream's <c>get_by_group_ids</c>.</summary>
    /// <param name="groupIds">The groups.</param>
    /// <param name="limit">The size of the page, or null for no limit.</param>
    /// <param name="uuidCursor">The UUID after which the page starts, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The nodes.</returns>
    Task<IReadOnlyList<CommunityNode>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default);

    /// <summary>Reads the name embedding of the node. Upstream's <c>load_name_embedding</c>.</summary>
    /// <param name="node">The node.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The node with its name embedding.</returns>
    Task<CommunityNode> LoadNameEmbeddingAsync(CommunityNode node, CancellationToken cancellationToken = default);
}

/// <summary>The saga nodes: upstream's <c>SagaNodeNamespace</c>. Each save fails as upstream fails (GAPS U2).</summary>
public interface ISagaNodeNamespace
{
    /// <summary>Saves the saga. Upstream's <c>save</c>. It fails as upstream fails (GAPS U2).</summary>
    /// <param name="node">The saga.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The saga.</returns>
    Task<SagaNode> SaveAsync(SagaNode node, CancellationToken cancellationToken = default);

    /// <summary>Saves the sagas in batches. Upstream's <c>save_bulk</c>.</summary>
    /// <param name="nodes">The sagas.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the sagas are saved.</returns>
    Task SaveBulkAsync(IReadOnlyList<SagaNode> nodes, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the saga and its edges. Upstream's <c>delete</c>.</summary>
    /// <param name="node">The saga.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the saga is deleted.</returns>
    Task DeleteAsync(SagaNode node, CancellationToken cancellationToken = default);

    /// <summary>Deletes the sagas of a group. Upstream's <c>delete_by_group_id</c>. It fails as upstream fails (GAPS U1).</summary>
    /// <param name="groupId">The group.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the sagas are deleted.</returns>
    Task DeleteByGroupIdAsync(string groupId, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Deletes the sagas with these UUIDs. Upstream's <c>delete_by_uuids</c>. It fails as upstream fails (GAPS U1).</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="batchSize">The size of a batch. The default is 100.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the sagas are deleted.</returns>
    Task DeleteByUuidsAsync(IReadOnlyList<string> uuids, int batchSize = 100, CancellationToken cancellationToken = default);

    /// <summary>Reads a saga. Upstream's <c>get_by_uuid</c>. A missing saga throws <see cref="NodeNotFoundException"/>.</summary>
    /// <param name="uuid">The UUID.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The saga.</returns>
    Task<SagaNode> GetByUuidAsync(string uuid, CancellationToken cancellationToken = default);

    /// <summary>Reads the sagas with these UUIDs. Upstream's <c>get_by_uuids</c>.</summary>
    /// <param name="uuids">The UUIDs.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The sagas that exist.</returns>
    Task<IReadOnlyList<SagaNode>> GetByUuidsAsync(IReadOnlyList<string> uuids, CancellationToken cancellationToken = default);

    /// <summary>Reads the sagas of the groups, a page at a time. Upstream's <c>get_by_group_ids</c>.</summary>
    /// <param name="groupIds">The groups.</param>
    /// <param name="limit">The size of the page, or null for no limit.</param>
    /// <param name="uuidCursor">The UUID after which the page starts, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The sagas.</returns>
    Task<IReadOnlyList<SagaNode>> GetByGroupIdsAsync(
        IReadOnlyList<string> groupIds, int? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default);
}
