namespace Graphiti;

/// <summary>
/// The 16 search recipes of upstream's <c>search_config_recipes.py</c>. Each read gives a new
/// <see cref="SearchConfig"/>. This differs from Python on purpose: there, a <c>search</c> call changes the shared recipe
/// object that a later <c>search_</c> call receives (GAPS U8).
/// </summary>
public static class SearchRecipes
{
    /// <summary>Gets a hybrid search with RRF reranking over edges, nodes, episodes, and communities.</summary>
    public static SearchConfig CombinedHybridSearchRrf => new()
    {
        EdgeConfig = new() { SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity], Reranker = EdgeReranker.Rrf },
        NodeConfig = new() { SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity], Reranker = NodeReranker.Rrf },
        EpisodeConfig = new() { SearchMethods = [EpisodeSearchMethod.Bm25], Reranker = EpisodeReranker.Rrf },
        CommunityConfig = new()
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.Rrf,
        },
    };

    /// <summary>Gets a hybrid search with MMR reranking over edges, nodes, and communities, and RRF over episodes.</summary>
    public static SearchConfig CombinedHybridSearchMmr => new()
    {
        EdgeConfig = new()
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity],
            Reranker = EdgeReranker.Mmr,
            MmrLambda = 1,
        },
        NodeConfig = new()
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity],
            Reranker = NodeReranker.Mmr,
            MmrLambda = 1,
        },
        EpisodeConfig = new() { SearchMethods = [EpisodeSearchMethod.Bm25], Reranker = EpisodeReranker.Rrf },
        CommunityConfig = new()
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.Mmr,
            MmrLambda = 1,
        },
    };

    /// <summary>
    /// Gets a full-text search, a similarity search, and a breadth-first search with cross-encoder reranking over edges,
    /// nodes, episodes, and communities. It needs a cross-encoder: the default one needs
    /// <c>UseChatLogProbabilities</c>.
    /// </summary>
    public static SearchConfig CombinedHybridSearchCrossEncoder => new()
    {
        EdgeConfig = new()
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity, EdgeSearchMethod.Bfs],
            Reranker = EdgeReranker.CrossEncoder,
        },
        NodeConfig = new()
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity, NodeSearchMethod.Bfs],
            Reranker = NodeReranker.CrossEncoder,
        },
        EpisodeConfig = new() { SearchMethods = [EpisodeSearchMethod.Bm25], Reranker = EpisodeReranker.CrossEncoder },
        CommunityConfig = new()
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.CrossEncoder,
        },
    };

    /// <summary>Gets a hybrid search over edges with RRF reranking.</summary>
    public static SearchConfig EdgeHybridSearchRrf => new()
    {
        EdgeConfig = new() { SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity], Reranker = EdgeReranker.Rrf },
    };

    /// <summary>Gets a hybrid search over edges with MMR reranking.</summary>
    public static SearchConfig EdgeHybridSearchMmr => new()
    {
        EdgeConfig = new() { SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity], Reranker = EdgeReranker.Mmr },
    };

    /// <summary>Gets a hybrid search over edges with node distance reranking. It needs a center node.</summary>
    public static SearchConfig EdgeHybridSearchNodeDistance => new()
    {
        EdgeConfig = new()
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity],
            Reranker = EdgeReranker.NodeDistance,
        },
    };

    /// <summary>Gets a hybrid search over edges with episode mention reranking.</summary>
    public static SearchConfig EdgeHybridSearchEpisodeMentions => new()
    {
        EdgeConfig = new()
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity],
            Reranker = EdgeReranker.EpisodeMentions,
        },
    };

    /// <summary>Gets a hybrid search over edges with cross-encoder reranking.</summary>
    public static SearchConfig EdgeHybridSearchCrossEncoder => new()
    {
        EdgeConfig = new()
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity, EdgeSearchMethod.Bfs],
            Reranker = EdgeReranker.CrossEncoder,
        },
        Limit = 10,
    };

    /// <summary>Gets a hybrid search over nodes with RRF reranking.</summary>
    public static SearchConfig NodeHybridSearchRrf => new()
    {
        NodeConfig = new() { SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity], Reranker = NodeReranker.Rrf },
    };

    /// <summary>Gets a hybrid search over nodes with MMR reranking.</summary>
    public static SearchConfig NodeHybridSearchMmr => new()
    {
        NodeConfig = new() { SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity], Reranker = NodeReranker.Mmr },
    };

    /// <summary>Gets a hybrid search over nodes with node distance reranking. It needs a center node.</summary>
    public static SearchConfig NodeHybridSearchNodeDistance => new()
    {
        NodeConfig = new()
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity],
            Reranker = NodeReranker.NodeDistance,
        },
    };

    /// <summary>Gets a hybrid search over nodes with episode mention reranking.</summary>
    public static SearchConfig NodeHybridSearchEpisodeMentions => new()
    {
        NodeConfig = new()
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity],
            Reranker = NodeReranker.EpisodeMentions,
        },
    };

    /// <summary>Gets a hybrid search over nodes with cross-encoder reranking.</summary>
    public static SearchConfig NodeHybridSearchCrossEncoder => new()
    {
        NodeConfig = new()
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity, NodeSearchMethod.Bfs],
            Reranker = NodeReranker.CrossEncoder,
        },
        Limit = 10,
    };

    /// <summary>Gets a hybrid search over communities with RRF reranking.</summary>
    public static SearchConfig CommunityHybridSearchRrf => new()
    {
        CommunityConfig = new()
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.Rrf,
        },
    };

    /// <summary>Gets a hybrid search over communities with MMR reranking.</summary>
    public static SearchConfig CommunityHybridSearchMmr => new()
    {
        CommunityConfig = new()
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.Mmr,
        },
    };

    /// <summary>Gets a hybrid search over communities with cross-encoder reranking.</summary>
    public static SearchConfig CommunityHybridSearchCrossEncoder => new()
    {
        CommunityConfig = new()
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.CrossEncoder,
        },
        Limit = 3,
    };
}
