namespace Graphiti;

/// <summary>The result of <c>IGraphiti.AddEpisodeAsync</c>. The public form of upstream's <c>AddEpisodeResults</c>.</summary>
public sealed record AddEpisodeResult
{
    /// <summary>Gets the episode that the call saved.</summary>
    public required EpisodicNode Episode { get; init; }

    /// <summary>Gets the edges from the episode to the entities that it mentions.</summary>
    public required IReadOnlyList<EpisodicEdge> EpisodicEdges { get; init; }

    /// <summary>Gets the entities of the episode, after resolution.</summary>
    public required IReadOnlyList<EntityNode> Nodes { get; init; }

    /// <summary>Gets the facts of the episode, with the facts that it invalidated.</summary>
    public required IReadOnlyList<EntityEdge> Edges { get; init; }

    /// <summary>Gets the communities that the call updated.</summary>
    public required IReadOnlyList<CommunityNode> Communities { get; init; }

    /// <summary>Gets the community edges that the call updated.</summary>
    public required IReadOnlyList<CommunityEdge> CommunityEdges { get; init; }
}

/// <summary>The result of <c>IGraphiti.AddEpisodeBulkAsync</c>. The public form of upstream's <c>AddBulkEpisodeResults</c>.</summary>
public sealed record AddEpisodeBulkResult
{
    /// <summary>Gets the episodes that the call saved.</summary>
    public required IReadOnlyList<EpisodicNode> Episodes { get; init; }

    /// <summary>Gets the edges from the episodes to the entities that they mention.</summary>
    public required IReadOnlyList<EpisodicEdge> EpisodicEdges { get; init; }

    /// <summary>Gets the entities of the episodes, after resolution.</summary>
    public required IReadOnlyList<EntityNode> Nodes { get; init; }

    /// <summary>Gets the facts of the episodes.</summary>
    public required IReadOnlyList<EntityEdge> Edges { get; init; }

    /// <summary>Gets the communities that the call updated.</summary>
    public required IReadOnlyList<CommunityNode> Communities { get; init; }

    /// <summary>Gets the community edges that the call updated.</summary>
    public required IReadOnlyList<CommunityEdge> CommunityEdges { get; init; }
}

/// <summary>
/// The result of <c>IGraphiti.AddTripletAsync</c>. The public form of upstream's <c>AddTripletResults</c>. Upstream
/// changes the nodes and the edge that the caller passes. The public records do not change, so the changed values are
/// here.
/// </summary>
public sealed record AddTripletResult
{
    /// <summary>Gets the nodes of the triplet, after resolution.</summary>
    public required IReadOnlyList<EntityNode> Nodes { get; init; }

    /// <summary>Gets the edges of the triplet, with the edges that it invalidated.</summary>
    public required IReadOnlyList<EntityEdge> Edges { get; init; }
}

/// <summary>The result of <c>IGraphiti.BuildCommunitiesAsync</c>: the two lists of upstream's result tuple.</summary>
public sealed record BuildCommunitiesResult
{
    /// <summary>Gets the communities that the call built. It is the first item of upstream's tuple.</summary>
    public required IReadOnlyList<CommunityNode> Communities { get; init; }

    /// <summary>Gets the edges from each community to its members. It is the second item of upstream's tuple.</summary>
    public required IReadOnlyList<CommunityEdge> CommunityEdges { get; init; }
}

/// <summary>
/// The result of an advanced search. The public form of upstream's <c>SearchResults</c>. Each score list is next to its
/// item list, in the same order.
/// </summary>
public sealed record SearchResults
{
    /// <summary>Gets the edges that the search found.</summary>
    public IReadOnlyList<EntityEdge> Edges { get; init; } = [];

    /// <summary>Gets the reranker score of each edge, in the order of <see cref="Edges"/>.</summary>
    public IReadOnlyList<double> EdgeRerankerScores { get; init; } = [];

    /// <summary>Gets the nodes that the search found.</summary>
    public IReadOnlyList<EntityNode> Nodes { get; init; } = [];

    /// <summary>Gets the reranker score of each node, in the order of <see cref="Nodes"/>.</summary>
    public IReadOnlyList<double> NodeRerankerScores { get; init; } = [];

    /// <summary>Gets the episodes that the search found.</summary>
    public IReadOnlyList<EpisodicNode> Episodes { get; init; } = [];

    /// <summary>Gets the reranker score of each episode, in the order of <see cref="Episodes"/>.</summary>
    public IReadOnlyList<double> EpisodeRerankerScores { get; init; } = [];

    /// <summary>Gets the communities that the search found.</summary>
    public IReadOnlyList<CommunityNode> Communities { get; init; } = [];

    /// <summary>Gets the reranker score of each community, in the order of <see cref="Communities"/>.</summary>
    public IReadOnlyList<double> CommunityRerankerScores { get; init; } = [];
}
