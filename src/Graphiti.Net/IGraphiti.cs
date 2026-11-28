namespace Graphiti;

/// <summary>
/// Graphiti, the temporal context graph: the public form of upstream's class <c>Graphiti</c> (decision D24). Resolve it
/// from the container after <c>AddGraphiti</c>. The container owns the one instance and closes it, so this interface has
/// no dispose member. A host must not change a collection that it passed to a call before the task of the call
/// completes, because the facade reads the arguments on its scheduler.
/// </summary>
/// <remarks>
/// A call works from any thread and with any <see cref="SynchronizationContext"/>. The task that it returns completes off
/// the Graphiti scheduler. The <see cref="CancellationToken"/> of a call reaches each host service call. A canceled token
/// gives <see cref="OperationCanceledException"/>. Graphiti reproduces the upstream defects that the documents list.
/// </remarks>
public interface IGraphiti
{
    /// <summary>Gets the token usage of the model calls, by prompt name. The public form of upstream's <c>token_tracker</c>.</summary>
    ITokenUsageTracker TokenUsage { get; }

    /// <summary>Gets the node namespaces: upstream's <c>graphiti.nodes</c>.</summary>
    INodeNamespaces Nodes { get; }

    /// <summary>Gets the edge namespaces: upstream's <c>graphiti.edges</c>.</summary>
    IEdgeNamespaces Edges { get; }

    /// <summary>
    /// Processes an episode and updates the graph: it extracts the entities and the facts, resolves them against the
    /// graph, invalidates contradicted facts, and saves the result. Upstream's <c>add_episode</c>. Add the episodes of one
    /// group one at a time, and await each call before the next one.
    /// </summary>
    /// <param name="episode">The episode.</param>
    /// <param name="options">The other parameters, or null for the upstream defaults.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The episode, its edges, the resolved nodes, the edges, and the communities.</returns>
    Task<AddEpisodeResult> AddEpisodeAsync(
        EpisodeInput episode, AddEpisodeOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Processes many episodes in one batch and updates the graph. Upstream's <c>add_episode_bulk</c>.</summary>
    /// <param name="episodes">The episodes.</param>
    /// <param name="options">The other parameters, or null for the upstream defaults.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The episodes, their edges, the resolved nodes, the edges, and the communities.</returns>
    Task<AddEpisodeBulkResult> AddEpisodeBulkAsync(
        IReadOnlyList<EpisodeInput> episodes, AddEpisodeBulkOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a node-edge-node triplet, and resolves the nodes by the configured identity properties. Upstream's
    /// <c>add_triplet</c>. Upstream changes the nodes and the edge that the caller passes, so the changed values come back
    /// in the result.
    /// </summary>
    /// <param name="source">The source node.</param>
    /// <param name="edge">The edge.</param>
    /// <param name="target">The target node.</param>
    /// <param name="options">The other parameters, or null for the upstream defaults.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The nodes and the edges of the triplet.</returns>
    Task<AddTripletResult> AddTripletAsync(
        EntityNode source,
        EntityEdge edge,
        EntityNode target,
        AddTripletOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A hybrid search of the facts of the graph, with the full-text search and the similarity search. Upstream's
    /// <c>search</c>.
    /// </summary>
    /// <param name="query">The query text.</param>
    /// <param name="options">The other parameters, or null for the upstream defaults.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The edges that hold the facts, in rank order.</returns>
    Task<IReadOnlyList<EntityEdge>> SearchAsync(
        string query, SearchOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// The advanced search with a search configuration. Upstream's <c>search_</c>. It has no default configuration:
    /// name a recipe of <see cref="SearchRecipes"/>.
    /// </summary>
    /// <param name="query">The query text.</param>
    /// <param name="config">The search configuration.</param>
    /// <param name="options">The other parameters, or null for the upstream defaults.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The nodes, the edges, the episodes, and the communities, each with its reranker scores.</returns>
    Task<SearchResults> AdvancedSearchAsync(
        string query, SearchConfig config, AdvancedSearchOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Returns the last episodes before a reference time. Upstream's <c>retrieve_episodes</c>.</summary>
    /// <param name="referenceTime">The reference time.</param>
    /// <param name="options">The other parameters, or null for the upstream defaults.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The episodes.</returns>
    Task<IReadOnlyList<EpisodicNode>> RetrieveEpisodesAsync(
        DateTimeOffset referenceTime, RetrieveEpisodesOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Returns the nodes and the edges that the episodes reference. Upstream's <c>get_nodes_and_edges_by_episode</c>.</summary>
    /// <param name="episodeUuids">The UUIDs of the episodes.</param>
    /// <param name="groupId">The group of the episodes, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The nodes and the edges, with no scores.</returns>
    Task<SearchResults> GetNodesAndEdgesByEpisodeAsync(
        IReadOnlyList<string> episodeUuids, string? groupId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an episode, and the nodes and the edges that only this episode created. Upstream's <c>remove_episode</c>.
    /// </summary>
    /// <param name="episodeUuid">The UUID of the episode.</param>
    /// <param name="groupId">The group of the episode, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the episode is removed.</returns>
    Task RemoveEpisodeAsync(string episodeUuid, string? groupId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds communities of nodes with a clustering algorithm and summarizes each one. Upstream's
    /// <c>build_communities</c>. It first removes the communities of the groups, or of the whole graph for null.
    /// </summary>
    /// <param name="groupIds">The groups to build communities for, or null for the whole graph.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The communities and their edges.</returns>
    Task<BuildCommunitiesResult> BuildCommunitiesAsync(
        IReadOnlyList<string>? groupIds = null, CancellationToken cancellationToken = default);

    /// <summary>Summarizes the episodes of a saga and saves the summary. Upstream's <c>summarize_saga</c>.</summary>
    /// <param name="sagaId">The UUID of the saga.</param>
    /// <param name="groupId">The group of the saga, or null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The saga with its summary.</returns>
    Task<SagaNode> SummarizeSagaAsync(string sagaId, string? groupId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the indexes and the constraints of the database. Upstream's <c>build_indices_and_constraints</c>. The library
    /// builds no index at start, so a host calls this method.
    /// </summary>
    /// <param name="deleteExisting">True to drop each index of the database first.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A task that ends when the indexes exist.</returns>
    Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default);
}

/// <summary>The token usage of the model calls of one Graphiti. The public form of upstream's <c>TokenUsageTracker</c>.</summary>
public interface ITokenUsageTracker
{
    /// <summary>Gets a copy of the token usage, by prompt name.</summary>
    /// <returns>The usage of each prompt, in the order of the first call.</returns>
    IReadOnlyDictionary<string, PromptTokenUsage> GetUsage();

    /// <summary>Gets the total token usage across all prompts.</summary>
    /// <returns>The total usage.</returns>
    TokenUsage GetTotalUsage();

    /// <summary>Clears the usage.</summary>
    void Reset();
}

/// <summary>A count of input tokens and output tokens. The public form of upstream's <c>TokenUsage</c>.</summary>
public sealed record TokenUsage
{
    /// <summary>Gets the count of input tokens.</summary>
    public long InputTokens { get; init; }

    /// <summary>Gets the count of output tokens.</summary>
    public long OutputTokens { get; init; }

    /// <summary>Gets the sum of the input tokens and the output tokens.</summary>
    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>The token usage of one prompt. The public form of upstream's <c>PromptTokenUsage</c>.</summary>
public sealed record PromptTokenUsage
{
    /// <summary>Gets the prompt name, or <c>unknown</c> for a call with no prompt name.</summary>
    public required string PromptName { get; init; }

    /// <summary>Gets the count of calls. A reply with no usage values counts as one call with zero tokens.</summary>
    public long CallCount { get; init; }

    /// <summary>Gets the count of input tokens of all calls.</summary>
    public long TotalInputTokens { get; init; }

    /// <summary>Gets the count of output tokens of all calls.</summary>
    public long TotalOutputTokens { get; init; }

    /// <summary>Gets the sum of the input tokens and the output tokens of all calls.</summary>
    public long TotalTokens => TotalInputTokens + TotalOutputTokens;

    /// <summary>Gets the mean count of input tokens of a call, or 0 with no call.</summary>
    public double AvgInputTokens => CallCount > 0 ? (double)TotalInputTokens / CallCount : 0;

    /// <summary>Gets the mean count of output tokens of a call, or 0 with no call.</summary>
    public double AvgOutputTokens => CallCount > 0 ? (double)TotalOutputTokens / CallCount : 0;
}
