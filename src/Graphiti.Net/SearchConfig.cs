namespace Graphiti;

/// <summary>
/// The configuration of an advanced search. The public form of upstream's <c>SearchConfig</c>. A scope with no
/// configuration is not searched. <see cref="SearchRecipes"/> holds the 16 recipes of upstream.
/// </summary>
public sealed record SearchConfig
{
    /// <summary>Gets the configuration of the edge search, or null for no edge search.</summary>
    public EdgeSearchConfig? EdgeConfig { get; init; }

    /// <summary>Gets the configuration of the node search, or null for no node search.</summary>
    public NodeSearchConfig? NodeConfig { get; init; }

    /// <summary>Gets the configuration of the episode search, or null for no episode search.</summary>
    public EpisodeSearchConfig? EpisodeConfig { get; init; }

    /// <summary>Gets the configuration of the community search, or null for no community search.</summary>
    public CommunitySearchConfig? CommunityConfig { get; init; }

    /// <summary>Gets the maximum number of results of each scope. The default is 10.</summary>
    public int Limit { get; init; } = 10;

    /// <summary>Gets the minimum reranker score of a result. The default is 0.</summary>
    public double RerankerMinScore { get; init; }
}

/// <summary>The configuration of the edge search. The public form of upstream's <c>EdgeSearchConfig</c>.</summary>
public sealed record EdgeSearchConfig
{
    /// <summary>Gets the search methods, in order.</summary>
    public required IReadOnlyList<EdgeSearchMethod> SearchMethods { get; init; }

    /// <summary>Gets the reranker. The default is <see cref="EdgeReranker.Rrf"/>.</summary>
    public EdgeReranker Reranker { get; init; } = EdgeReranker.Rrf;

    /// <summary>Gets the minimum similarity score of the similarity search. The default is 0.6.</summary>
    public double SimMinScore { get; init; } = 0.6;

    /// <summary>Gets the lambda of the MMR reranker. The default is 0.5.</summary>
    public double MmrLambda { get; init; } = 0.5;

    /// <summary>Gets the maximum depth of the breadth-first search. The default is 3.</summary>
    public int BfsMaxDepth { get; init; } = 3;
}

/// <summary>The configuration of the node search. The public form of upstream's <c>NodeSearchConfig</c>.</summary>
public sealed record NodeSearchConfig
{
    /// <summary>Gets the search methods, in order.</summary>
    public required IReadOnlyList<NodeSearchMethod> SearchMethods { get; init; }

    /// <summary>Gets the reranker. The default is <see cref="NodeReranker.Rrf"/>.</summary>
    public NodeReranker Reranker { get; init; } = NodeReranker.Rrf;

    /// <summary>Gets the minimum similarity score of the similarity search. The default is 0.6.</summary>
    public double SimMinScore { get; init; } = 0.6;

    /// <summary>Gets the lambda of the MMR reranker. The default is 0.5.</summary>
    public double MmrLambda { get; init; } = 0.5;

    /// <summary>Gets the maximum depth of the breadth-first search. The default is 3.</summary>
    public int BfsMaxDepth { get; init; } = 3;
}

/// <summary>The configuration of the episode search. The public form of upstream's <c>EpisodeSearchConfig</c>.</summary>
public sealed record EpisodeSearchConfig
{
    /// <summary>Gets the search methods, in order.</summary>
    public required IReadOnlyList<EpisodeSearchMethod> SearchMethods { get; init; }

    /// <summary>Gets the reranker. The default is <see cref="EpisodeReranker.Rrf"/>.</summary>
    public EpisodeReranker Reranker { get; init; } = EpisodeReranker.Rrf;

    /// <summary>Gets the minimum similarity score of the similarity search. The default is 0.6.</summary>
    public double SimMinScore { get; init; } = 0.6;

    /// <summary>Gets the lambda of the MMR reranker. The default is 0.5.</summary>
    public double MmrLambda { get; init; } = 0.5;

    /// <summary>Gets the maximum depth of the breadth-first search. The default is 3.</summary>
    public int BfsMaxDepth { get; init; } = 3;
}

/// <summary>The configuration of the community search. The public form of upstream's <c>CommunitySearchConfig</c>.</summary>
public sealed record CommunitySearchConfig
{
    /// <summary>Gets the search methods, in order.</summary>
    public required IReadOnlyList<CommunitySearchMethod> SearchMethods { get; init; }

    /// <summary>Gets the reranker. The default is <see cref="CommunityReranker.Rrf"/>.</summary>
    public CommunityReranker Reranker { get; init; } = CommunityReranker.Rrf;

    /// <summary>Gets the minimum similarity score of the similarity search. The default is 0.6.</summary>
    public double SimMinScore { get; init; } = 0.6;

    /// <summary>Gets the lambda of the MMR reranker. The default is 0.5.</summary>
    public double MmrLambda { get; init; } = 0.5;

    /// <summary>Gets the maximum depth of the breadth-first search. The default is 3.</summary>
    public int BfsMaxDepth { get; init; } = 3;
}

/// <summary>A search method for edges. The public form of upstream's <c>EdgeSearchMethod</c>.</summary>
public enum EdgeSearchMethod
{
    /// <summary>The cosine similarity of the fact embedding.</summary>
    CosineSimilarity = 0,

    /// <summary>The full-text search of the fact.</summary>
    Bm25 = 1,

    /// <summary>The breadth-first search from the origin nodes.</summary>
    Bfs = 2,
}

/// <summary>A search method for nodes. The public form of upstream's <c>NodeSearchMethod</c>.</summary>
public enum NodeSearchMethod
{
    /// <summary>The cosine similarity of the name embedding.</summary>
    CosineSimilarity = 0,

    /// <summary>The full-text search of the name and the summary.</summary>
    Bm25 = 1,

    /// <summary>The breadth-first search from the origin nodes.</summary>
    Bfs = 2,
}

/// <summary>A search method for episodes. The public form of upstream's <c>EpisodeSearchMethod</c>.</summary>
public enum EpisodeSearchMethod
{
    /// <summary>The full-text search of the content.</summary>
    Bm25 = 0,
}

/// <summary>A search method for communities. The public form of upstream's <c>CommunitySearchMethod</c>.</summary>
public enum CommunitySearchMethod
{
    /// <summary>The cosine similarity of the name embedding.</summary>
    CosineSimilarity = 0,

    /// <summary>The full-text search of the name.</summary>
    Bm25 = 1,
}

/// <summary>A reranker for edges. The public form of upstream's <c>EdgeReranker</c>.</summary>
public enum EdgeReranker
{
    /// <summary>Reciprocal rank fusion.</summary>
    Rrf = 0,

    /// <summary>The distance from the center node.</summary>
    NodeDistance = 1,

    /// <summary>The count of episode mentions.</summary>
    EpisodeMentions = 2,

    /// <summary>Maximal marginal relevance.</summary>
    Mmr = 3,

    /// <summary>The cross-encoder.</summary>
    CrossEncoder = 4,
}

/// <summary>A reranker for nodes. The public form of upstream's <c>NodeReranker</c>.</summary>
public enum NodeReranker
{
    /// <summary>Reciprocal rank fusion.</summary>
    Rrf = 0,

    /// <summary>The distance from the center node.</summary>
    NodeDistance = 1,

    /// <summary>The count of episode mentions.</summary>
    EpisodeMentions = 2,

    /// <summary>Maximal marginal relevance.</summary>
    Mmr = 3,

    /// <summary>The cross-encoder.</summary>
    CrossEncoder = 4,
}

/// <summary>A reranker for episodes. The public form of upstream's <c>EpisodeReranker</c>.</summary>
public enum EpisodeReranker
{
    /// <summary>Reciprocal rank fusion.</summary>
    Rrf = 0,

    /// <summary>The cross-encoder.</summary>
    CrossEncoder = 1,
}

/// <summary>A reranker for communities. The public form of upstream's <c>CommunityReranker</c>.</summary>
public enum CommunityReranker
{
    /// <summary>Reciprocal rank fusion.</summary>
    Rrf = 0,

    /// <summary>Maximal marginal relevance.</summary>
    Mmr = 1,

    /// <summary>The cross-encoder.</summary>
    CrossEncoder = 2,
}
