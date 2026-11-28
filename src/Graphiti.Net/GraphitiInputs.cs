namespace Graphiti;

/// <summary>
/// One episode to add: the first six parameters of upstream's <c>add_episode</c>, and the fields of upstream's
/// <c>RawEpisode</c> for <c>IGraphiti.AddEpisodeBulkAsync</c>.
/// </summary>
public sealed record EpisodeInput
{
    /// <summary>Gets the name of the episode.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the content of the episode. Upstream names it <c>episode_body</c>.</summary>
    public required string Content { get; init; }

    /// <summary>Gets a description of the source of the episode.</summary>
    public required string SourceDescription { get; init; }

    /// <summary>Gets the reference time of the episode. A call keeps the offset and cuts the value to microseconds.</summary>
    public required DateTimeOffset ReferenceTime { get; init; }

    /// <summary>Gets the type of the episode. The default is <see cref="EpisodeType.Message"/>.</summary>
    public EpisodeType Source { get; init; } = EpisodeType.Message;

    /// <summary>Gets the UUID of the episode, or null for a new random UUID.</summary>
    public string? Uuid { get; init; }
}

/// <summary>The other parameters of upstream's <c>add_episode</c>, with the upstream defaults.</summary>
public sealed record AddEpisodeOptions
{
    /// <summary>Gets the id of the partition of the graph that the episode is a part of, or null for the default.</summary>
    public string? GroupId { get; init; }

    /// <summary>
    /// Gets a value indicating whether to update the communities with the new node information. The default is false.
    /// Upstream fails for a node count other than two (GAPS U14), and Graphiti keeps that.
    /// </summary>
    public bool UpdateCommunities { get; init; }

    /// <summary>Gets the custom entity types, or null. The order of the list reaches the prompt text.</summary>
    public IReadOnlyList<GraphTypeDefinition>? EntityTypes { get; init; }

    /// <summary>
    /// Gets the names of the entity types to exclude from the graph, or null. The list can hold <c>Entity</c> to
    /// exclude the default entity type.
    /// </summary>
    public IReadOnlyList<string>? ExcludedEntityTypes { get; init; }

    /// <summary>
    /// Gets the UUIDs of the episodes to use as the previous episodes, or null. With null, Graphiti uses the most recent
    /// episodes.
    /// </summary>
    public IReadOnlyList<string>? PreviousEpisodeUuids { get; init; }

    /// <summary>Gets the custom edge types, or null. The order of the list reaches the prompt text.</summary>
    public IReadOnlyList<GraphTypeDefinition>? EdgeTypes { get; init; }

    /// <summary>Gets the allowed edge types for each pair of entity types, or null. The order reaches the prompt text.</summary>
    public IReadOnlyList<EdgeTypeRule>? EdgeTypeMap { get; init; }

    /// <summary>Gets custom extraction instructions for the prompts that extract entities and edges, or null.</summary>
    public string? CustomExtractionInstructions { get; init; }

    /// <summary>
    /// Gets the name of a saga to add the episode to, or null. If no saga of this name exists in the group, Graphiti
    /// creates one. A call that sets both <see cref="Saga"/> and <see cref="SagaNode"/> throws
    /// <see cref="ArgumentException"/>.
    /// </summary>
    public string? Saga { get; init; }

    /// <summary>
    /// Gets the saga to add the episode to, or null. A call that sets both <see cref="Saga"/> and
    /// <see cref="SagaNode"/> throws <see cref="ArgumentException"/>. The call returns no saga: read it again for the
    /// values that upstream changes.
    /// </summary>
    public SagaNode? SagaNode { get; init; }

    /// <summary>Gets the UUID of the previous episode in the saga, or null. A value skips the query for it.</summary>
    public string? SagaPreviousEpisodeUuid { get; init; }

    /// <summary>
    /// Gets the attribute names that identify the entities of each entity type, or null. Graphiti extracts these
    /// attributes before node resolution.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? IdentityProperties { get; init; }
}

/// <summary>The other parameters of upstream's <c>add_episode_bulk</c>, with the upstream defaults.</summary>
public sealed record AddEpisodeBulkOptions
{
    /// <summary>Gets the id of the partition of the graph that the episodes are a part of, or null for the default.</summary>
    public string? GroupId { get; init; }

    /// <summary>Gets the custom entity types, or null. The order of the list reaches the prompt text.</summary>
    public IReadOnlyList<GraphTypeDefinition>? EntityTypes { get; init; }

    /// <summary>Gets the names of the entity types to exclude from extraction, or null.</summary>
    public IReadOnlyList<string>? ExcludedEntityTypes { get; init; }

    /// <summary>Gets the custom edge types, or null. The order of the list reaches the prompt text.</summary>
    public IReadOnlyList<GraphTypeDefinition>? EdgeTypes { get; init; }

    /// <summary>Gets the allowed edge types for each pair of entity types, or null. The order reaches the prompt text.</summary>
    public IReadOnlyList<EdgeTypeRule>? EdgeTypeMap { get; init; }

    /// <summary>
    /// Gets a value indicating whether to discard the extracted edges that do not use one of the custom edge types. The
    /// default is false.
    /// </summary>
    public bool StrictEdgeTypes { get; init; }

    /// <summary>Gets custom extraction instructions for the prompts that extract entities and edges, or null.</summary>
    public string? CustomExtractionInstructions { get; init; }

    /// <summary>
    /// Gets the name of a saga to add each episode to, or null. A call that sets both <see cref="Saga"/> and
    /// <see cref="SagaNode"/> throws <see cref="ArgumentException"/>.
    /// </summary>
    public string? Saga { get; init; }

    /// <summary>
    /// Gets the saga to add each episode to, or null. A call that sets both <see cref="Saga"/> and
    /// <see cref="SagaNode"/> throws <see cref="ArgumentException"/>.
    /// </summary>
    public SagaNode? SagaNode { get; init; }

    /// <summary>
    /// Gets a value indicating whether to use one combined model call for the nodes and the edges of each episode. The
    /// default is true.
    /// </summary>
    public bool UseCombinedExtraction { get; init; } = true;

    /// <summary>Gets the attribute names that identify the entities of each entity type, or null.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? IdentityProperties { get; init; }
}

/// <summary>The other parameters of upstream's <c>add_triplet</c>, with the upstream defaults.</summary>
public sealed record AddTripletOptions
{
    /// <summary>Gets the episode of the triplet, or null.</summary>
    public EpisodicNode? Episode { get; init; }

    /// <summary>Gets the attribute names that identify the entities of each entity type, or null.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? IdentityProperties { get; init; }
}

/// <summary>The other parameters of upstream's <c>search</c>, with the upstream defaults.</summary>
public sealed record SearchOptions
{
    /// <summary>Gets the UUID of a node for distance reranking, or null.</summary>
    public string? CenterNodeUuid { get; init; }

    /// <summary>Gets the group ids to search, or null for each group.</summary>
    public IReadOnlyList<string>? GroupIds { get; init; }

    /// <summary>Gets the maximum number of results. The default is 10.</summary>
    public int NumResults { get; init; } = 10;

    /// <summary>Gets the search filters, or null.</summary>
    public SearchFilters? SearchFilter { get; init; }
}

/// <summary>The other parameters of upstream's <c>search_</c>, with the upstream defaults.</summary>
public sealed record AdvancedSearchOptions
{
    /// <summary>Gets the group ids to search, or null for each group.</summary>
    public IReadOnlyList<string>? GroupIds { get; init; }

    /// <summary>Gets the UUID of a node for distance reranking, or null.</summary>
    public string? CenterNodeUuid { get; init; }

    /// <summary>Gets the UUIDs of the nodes where a breadth-first search starts, or null.</summary>
    public IReadOnlyList<string>? BfsOriginNodeUuids { get; init; }

    /// <summary>Gets the search filters, or null.</summary>
    public SearchFilters? SearchFilter { get; init; }

    /// <summary>Gets the embedding of the query, or null to embed the query text.</summary>
    public ReadOnlyMemory<double>? QueryVector { get; init; }
}

/// <summary>The other parameters of upstream's <c>retrieve_episodes</c>, with the upstream defaults.</summary>
public sealed record RetrieveEpisodesOptions
{
    /// <summary>Gets the number of episodes to return. The default is 3.</summary>
    public int LastN { get; init; } = 3;

    /// <summary>Gets the group ids to read, or null for each group.</summary>
    public IReadOnlyList<string>? GroupIds { get; init; }

    /// <summary>Gets the episode type to read, or null for each type.</summary>
    public EpisodeType? Source { get; init; }

    /// <summary>Gets the name of a saga to read the episodes of, or null.</summary>
    public string? Saga { get; init; }
}

/// <summary>
/// The allowed edge types between two entity types: one entry of upstream's <c>edge_type_map</c>. A list of rules keeps
/// the order, which reaches the prompt text.
/// </summary>
public sealed record EdgeTypeRule
{
    /// <summary>Gets the entity type of the source node.</summary>
    public required string SourceType { get; init; }

    /// <summary>Gets the entity type of the target node.</summary>
    public required string TargetType { get; init; }

    /// <summary>Gets the names of the allowed edge types.</summary>
    public required IReadOnlyList<string> EdgeTypes { get; init; }
}
