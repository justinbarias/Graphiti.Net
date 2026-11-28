namespace Graphiti;

using Graphiti.Internal;

/// <summary>
/// A node of the graph, the public form of upstream's <c>Node</c> (decision D25). Two nodes are equal if each property
/// is equal. A collection property compares by reference, as in each C# record.
/// </summary>
public abstract record Node
{
    private protected Node()
    {
    }

    /// <summary>Gets the UUID of the node. The default is a new random version 4 UUID.</summary>
    public string Uuid { get; init; } = PublicValues.NewUuid();

    /// <summary>Gets the name of the node.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the partition of the graph that holds the node.</summary>
    public required string GroupId { get; init; }

    /// <summary>Gets the labels of the node. The default is an empty list.</summary>
    public IReadOnlyList<string> Labels { get; init; } = [];

    /// <summary>
    /// Gets the time when the node was created. The default is the current time in UTC. A save keeps the offset and
    /// cuts the value to microseconds.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = PublicValues.UtcNow();
}

/// <summary>An episode: one piece of source data that Graphiti ingested. The public form of upstream's <c>EpisodicNode</c>.</summary>
public sealed record EpisodicNode : Node
{
    /// <summary>Gets the source type of the episode.</summary>
    public required EpisodeType Source { get; init; }

    /// <summary>Gets the description of the data source.</summary>
    public required string SourceDescription { get; init; }

    /// <summary>Gets the raw episode data.</summary>
    public required string Content { get; init; }

    /// <summary>Gets the time when the original document was created.</summary>
    public required DateTimeOffset ValidAt { get; init; }

    /// <summary>Gets the UUIDs of the entity edges that this episode references. The default is an empty list.</summary>
    public IReadOnlyList<string> EntityEdges { get; init; } = [];

    /// <summary>
    /// Gets the metadata of the customer for filtering, or null. A value is null, <see cref="bool"/>, <see cref="long"/>,
    /// <see cref="double"/>, <see cref="string"/>, <see cref="DateTimeOffset"/>, <see cref="DateOnly"/>, a read-only list,
    /// or a read-only dictionary. A call throws <see cref="InvalidCastException"/> for another kind of value.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? EpisodeMetadata { get; init; }
}

/// <summary>An entity of the graph. The public form of upstream's <c>EntityNode</c>.</summary>
public sealed record EntityNode : Node
{
    /// <summary>Gets the embedding of the name, or null. A read and a save keep each 64-bit value.</summary>
    public ReadOnlyMemory<double>? NameEmbedding { get; init; }

    /// <summary>Gets the regional summary of the edges around the node. The default is an empty text.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>
    /// Gets the additional attributes of the node, for example the fields of a custom entity type. A value is null,
    /// <see cref="bool"/>, <see cref="long"/>, <see cref="double"/>, <see cref="string"/>, <see cref="DateTimeOffset"/>,
    /// <see cref="DateOnly"/>, a read-only list, or a read-only dictionary. A call throws
    /// <see cref="InvalidCastException"/> for another kind of value. The default is an empty dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = PublicValues.EmptyAttributes;
}

/// <summary>A community of entities. The public form of upstream's <c>CommunityNode</c>.</summary>
public sealed record CommunityNode : Node
{
    /// <summary>Gets the embedding of the name, or null. A read and a save keep each 64-bit value.</summary>
    public ReadOnlyMemory<double>? NameEmbedding { get; init; }

    /// <summary>Gets the summary of the member nodes of the region. The default is an empty text.</summary>
    public string Summary { get; init; } = string.Empty;
}

/// <summary>A saga: an ordered chain of episodes with a summary. The public form of upstream's <c>SagaNode</c>.</summary>
public sealed record SagaNode : Node
{
    /// <summary>Gets the summary of the saga. The default is an empty text.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Gets the UUID of the first episode of the saga, or null.</summary>
    public string? FirstEpisodeUuid { get; init; }

    /// <summary>Gets the UUID of the last episode of the saga, or null.</summary>
    public string? LastEpisodeUuid { get; init; }

    /// <summary>Gets the ingestion time of the most recent summary, the filter of the next incremental summary, or null.</summary>
    public DateTimeOffset? LastSummarizedAt { get; init; }

    /// <summary>Gets the latest episode reference time across the episodes of the most recent summary, or null.</summary>
    public DateTimeOffset? LastSummarizedEpisodeValidAt { get; init; }
}

/// <summary>
/// The types of episodes that Graphiti can process. The public form of upstream's <c>EpisodeType</c>. Graphiti maps
/// each member by name, never by number or text.
/// </summary>
public enum EpisodeType
{
    /// <summary>A message in the form <c>actor: content</c>, for example <c>user: Hello, how are you?</c>.</summary>
    Message = 0,

    /// <summary>A JSON text.</summary>
    Json = 1,

    /// <summary>A plain text.</summary>
    Text = 2,

    /// <summary>A fact triple. Upstream's text is <c>fact_triple</c>.</summary>
    FactTriple = 3,
}
