namespace Graphiti;

using Graphiti.Internal;

/// <summary>
/// An edge of the graph, the public form of upstream's <c>Edge</c> (decision D25). Two edges are equal if each property
/// is equal. A collection property compares by reference, as in each C# record.
/// </summary>
public abstract record Edge
{
    private protected Edge()
    {
    }

    /// <summary>Gets the UUID of the edge. The default is a new random version 4 UUID.</summary>
    public string Uuid { get; init; } = PublicValues.NewUuid();

    /// <summary>Gets the partition of the graph that holds the edge.</summary>
    public required string GroupId { get; init; }

    /// <summary>Gets the UUID of the source node.</summary>
    public required string SourceNodeUuid { get; init; }

    /// <summary>Gets the UUID of the target node.</summary>
    public required string TargetNodeUuid { get; init; }

    /// <summary>Gets the time when the edge was created. A save keeps the offset and cuts the value to microseconds.</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>A fact between two entities. The public form of upstream's <c>EntityEdge</c>.</summary>
public sealed record EntityEdge : Edge
{
    /// <summary>Gets the name of the edge, the relation name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the fact that the edge represents, with the nodes that it connects.</summary>
    public required string Fact { get; init; }

    /// <summary>Gets the embedding of the fact, or null. A read and a save keep each 64-bit value.</summary>
    public ReadOnlyMemory<double>? FactEmbedding { get; init; }

    /// <summary>Gets the UUIDs of the episodes that reference the edge. The default is an empty list.</summary>
    public IReadOnlyList<string> Episodes { get; init; } = [];

    /// <summary>Gets the time when the edge was invalidated, or null.</summary>
    public DateTimeOffset? ExpiredAt { get; init; }

    /// <summary>Gets the time when the fact became true, or null.</summary>
    public DateTimeOffset? ValidAt { get; init; }

    /// <summary>Gets the time when the fact stopped being true, or null.</summary>
    public DateTimeOffset? InvalidAt { get; init; }

    /// <summary>Gets the reference time of the episode that created the edge, or null.</summary>
    public DateTimeOffset? ReferenceTime { get; init; }

    /// <summary>
    /// Gets the additional attributes of the edge, for example the fields of a custom edge type. A value is null,
    /// <see cref="bool"/>, <see cref="long"/>, <see cref="double"/>, <see cref="string"/>, <see cref="DateTimeOffset"/>,
    /// <see cref="DateOnly"/>, a read-only list, or a read-only dictionary. A call throws
    /// <see cref="InvalidCastException"/> for another kind of value. The default is an empty dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; init; } = PublicValues.EmptyAttributes;
}

/// <summary>An edge from an episode to an entity that it mentions. The public form of upstream's <c>EpisodicEdge</c>.</summary>
public sealed record EpisodicEdge : Edge;

/// <summary>An edge from a community to one of its members. The public form of upstream's <c>CommunityEdge</c>.</summary>
public sealed record CommunityEdge : Edge;

/// <summary>An edge from a saga to one of its episodes. The public form of upstream's <c>HasEpisodeEdge</c>.</summary>
public sealed record HasEpisodeEdge : Edge;

/// <summary>An edge from an episode of a saga to the next one. The public form of upstream's <c>NextEpisodeEdge</c>.</summary>
public sealed record NextEpisodeEdge : Edge;
