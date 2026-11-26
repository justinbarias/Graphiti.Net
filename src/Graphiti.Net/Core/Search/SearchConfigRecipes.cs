// Ported from graphiti_core/search/search_config_recipes.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core.Search;

// PORT: each recipe is a module-level mutable object: a static readonly field that holds the same single object
// (R17). graphiti.py writes `limit` into a shared recipe (upstream defect U8, R21). The search config models have no
// constraint and no validator, so each recipe is built with `new M { ... }.Validate()` (R7).
internal static class SearchConfigRecipes
{
    // Performs a hybrid search with rrf reranking over edges, nodes, and communities
    internal static readonly SearchConfig CombinedHybridSearchRrf = new SearchConfig
    {
        EdgeConfig = new EdgeSearchConfig
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity],
            Reranker = EdgeReranker.Rrf,
        }.Validate(),
        NodeConfig = new NodeSearchConfig
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity],
            Reranker = NodeReranker.Rrf,
        }.Validate(),
        EpisodeConfig = new EpisodeSearchConfig
        {
            SearchMethods = [EpisodeSearchMethod.Bm25],
            Reranker = EpisodeReranker.Rrf,
        }.Validate(),
        CommunityConfig = new CommunitySearchConfig
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.Rrf,
        }.Validate(),
    }.Validate();

    // Performs a hybrid search with mmr reranking over edges, nodes, and communities
    internal static readonly SearchConfig CombinedHybridSearchMmr = new SearchConfig
    {
        EdgeConfig = new EdgeSearchConfig
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity],
            Reranker = EdgeReranker.Mmr,
            MmrLambda = 1,
        }.Validate(),
        NodeConfig = new NodeSearchConfig
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity],
            Reranker = NodeReranker.Mmr,
            MmrLambda = 1,
        }.Validate(),
        EpisodeConfig = new EpisodeSearchConfig
        {
            SearchMethods = [EpisodeSearchMethod.Bm25],
            Reranker = EpisodeReranker.Rrf,
        }.Validate(),
        CommunityConfig = new CommunitySearchConfig
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.Mmr,
            MmrLambda = 1,
        }.Validate(),
    }.Validate();

    // Performs a full-text search, similarity search, and bfs with cross_encoder reranking over edges, nodes, and communities
    internal static readonly SearchConfig CombinedHybridSearchCrossEncoder = new SearchConfig
    {
        EdgeConfig = new EdgeSearchConfig
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity, EdgeSearchMethod.Bfs],
            Reranker = EdgeReranker.CrossEncoder,
        }.Validate(),
        NodeConfig = new NodeSearchConfig
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity, NodeSearchMethod.Bfs],
            Reranker = NodeReranker.CrossEncoder,
        }.Validate(),
        EpisodeConfig = new EpisodeSearchConfig
        {
            SearchMethods = [EpisodeSearchMethod.Bm25],
            Reranker = EpisodeReranker.CrossEncoder,
        }.Validate(),
        CommunityConfig = new CommunitySearchConfig
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.CrossEncoder,
        }.Validate(),
    }.Validate();

    // performs a hybrid search over edges with rrf reranking
    internal static readonly SearchConfig EdgeHybridSearchRrf = new SearchConfig
    {
        EdgeConfig = new EdgeSearchConfig
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity],
            Reranker = EdgeReranker.Rrf,
        }.Validate(),
    }.Validate();

    // performs a hybrid search over edges with mmr reranking
    internal static readonly SearchConfig EdgeHybridSearchMmr = new SearchConfig
    {
        EdgeConfig = new EdgeSearchConfig
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity],
            Reranker = EdgeReranker.Mmr,
        }.Validate(),
    }.Validate();

    // performs a hybrid search over edges with node distance reranking
    internal static readonly SearchConfig EdgeHybridSearchNodeDistance = new SearchConfig
    {
        EdgeConfig = new EdgeSearchConfig
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity],
            Reranker = EdgeReranker.NodeDistance,
        }.Validate(),
    }.Validate();

    // performs a hybrid search over edges with episode mention reranking
    internal static readonly SearchConfig EdgeHybridSearchEpisodeMentions = new SearchConfig
    {
        EdgeConfig = new EdgeSearchConfig
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity],
            Reranker = EdgeReranker.EpisodeMentions,
        }.Validate(),
    }.Validate();

    // performs a hybrid search over edges with cross encoder reranking
    internal static readonly SearchConfig EdgeHybridSearchCrossEncoder = new SearchConfig
    {
        EdgeConfig = new EdgeSearchConfig
        {
            SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity, EdgeSearchMethod.Bfs],
            Reranker = EdgeReranker.CrossEncoder,
        }.Validate(),
        Limit = 10,
    }.Validate();

    // performs a hybrid search over nodes with rrf reranking
    internal static readonly SearchConfig NodeHybridSearchRrf = new SearchConfig
    {
        NodeConfig = new NodeSearchConfig
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity],
            Reranker = NodeReranker.Rrf,
        }.Validate(),
    }.Validate();

    // performs a hybrid search over nodes with mmr reranking
    internal static readonly SearchConfig NodeHybridSearchMmr = new SearchConfig
    {
        NodeConfig = new NodeSearchConfig
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity],
            Reranker = NodeReranker.Mmr,
        }.Validate(),
    }.Validate();

    // performs a hybrid search over nodes with node distance reranking
    internal static readonly SearchConfig NodeHybridSearchNodeDistance = new SearchConfig
    {
        NodeConfig = new NodeSearchConfig
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity],
            Reranker = NodeReranker.NodeDistance,
        }.Validate(),
    }.Validate();

    // performs a hybrid search over nodes with episode mentions reranking
    internal static readonly SearchConfig NodeHybridSearchEpisodeMentions = new SearchConfig
    {
        NodeConfig = new NodeSearchConfig
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity],
            Reranker = NodeReranker.EpisodeMentions,
        }.Validate(),
    }.Validate();

    // performs a hybrid search over nodes with episode mentions reranking
    internal static readonly SearchConfig NodeHybridSearchCrossEncoder = new SearchConfig
    {
        NodeConfig = new NodeSearchConfig
        {
            SearchMethods = [NodeSearchMethod.Bm25, NodeSearchMethod.CosineSimilarity, NodeSearchMethod.Bfs],
            Reranker = NodeReranker.CrossEncoder,
        }.Validate(),
        Limit = 10,
    }.Validate();

    // performs a hybrid search over communities with rrf reranking
    internal static readonly SearchConfig CommunityHybridSearchRrf = new SearchConfig
    {
        CommunityConfig = new CommunitySearchConfig
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.Rrf,
        }.Validate(),
    }.Validate();

    // performs a hybrid search over communities with mmr reranking
    internal static readonly SearchConfig CommunityHybridSearchMmr = new SearchConfig
    {
        CommunityConfig = new CommunitySearchConfig
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.Mmr,
        }.Validate(),
    }.Validate();

    // performs a hybrid search over communities with mmr reranking
    internal static readonly SearchConfig CommunityHybridSearchCrossEncoder = new SearchConfig
    {
        CommunityConfig = new CommunitySearchConfig
        {
            SearchMethods = [CommunitySearchMethod.Bm25, CommunitySearchMethod.CosineSimilarity],
            Reranker = CommunityReranker.CrossEncoder,
        }.Validate(),
        Limit = 3,
    }.Validate();
}
