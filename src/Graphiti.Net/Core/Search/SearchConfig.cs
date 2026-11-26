// Ported from graphiti_core/search/search_config.py
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

using Graphiti.PyCompat;

// PORT: the module name equals the class SearchConfig, so the module class has the suffix Module (R2).
internal static class SearchConfigModule
{
    internal const long DefaultSearchLimit = 10;
}

internal enum EdgeSearchMethod
{
    [PyEnumMember("cosine_similarity", "cosine_similarity")]
    CosineSimilarity,
    [PyEnumMember("bm25", "bm25")]
    Bm25,
    [PyEnumMember("bfs", "breadth_first_search")]
    Bfs,
}

internal enum NodeSearchMethod
{
    [PyEnumMember("cosine_similarity", "cosine_similarity")]
    CosineSimilarity,
    [PyEnumMember("bm25", "bm25")]
    Bm25,
    [PyEnumMember("bfs", "breadth_first_search")]
    Bfs,
}

internal enum EpisodeSearchMethod
{
    [PyEnumMember("bm25", "bm25")]
    Bm25,
}

internal enum CommunitySearchMethod
{
    [PyEnumMember("cosine_similarity", "cosine_similarity")]
    CosineSimilarity,
    [PyEnumMember("bm25", "bm25")]
    Bm25,
}

internal enum EdgeReranker
{
    [PyEnumMember("rrf", "reciprocal_rank_fusion")]
    Rrf,
    [PyEnumMember("node_distance", "node_distance")]
    NodeDistance,
    [PyEnumMember("episode_mentions", "episode_mentions")]
    EpisodeMentions,
    [PyEnumMember("mmr", "mmr")]
    Mmr,
    [PyEnumMember("cross_encoder", "cross_encoder")]
    CrossEncoder,
}

internal enum NodeReranker
{
    [PyEnumMember("rrf", "reciprocal_rank_fusion")]
    Rrf,
    [PyEnumMember("node_distance", "node_distance")]
    NodeDistance,
    [PyEnumMember("episode_mentions", "episode_mentions")]
    EpisodeMentions,
    [PyEnumMember("mmr", "mmr")]
    Mmr,
    [PyEnumMember("cross_encoder", "cross_encoder")]
    CrossEncoder,
}

internal enum EpisodeReranker
{
    [PyEnumMember("rrf", "reciprocal_rank_fusion")]
    Rrf,
    [PyEnumMember("cross_encoder", "cross_encoder")]
    CrossEncoder,
}

internal enum CommunityReranker
{
    [PyEnumMember("rrf", "reciprocal_rank_fusion")]
    Rrf,
    [PyEnumMember("mmr", "mmr")]
    Mmr,
    [PyEnumMember("cross_encoder", "cross_encoder")]
    CrossEncoder,
}

// PORT: the search config models have no constraint and no validator, so Validate() returns the model (R7). The
// recipes of search_config_recipes.py construct them with typed arguments: `new M { ... }.Validate()`.
internal sealed record EdgeSearchConfig
{
    internal required List<EdgeSearchMethod> SearchMethods { get; set; }

    internal EdgeReranker Reranker { get; set; } = EdgeReranker.Rrf;

    internal double SimMinScore { get; set; } = SearchUtils.DefaultMinScore;

    internal double MmrLambda { get; set; } = SearchUtils.DefaultMmrLambda;

    internal long BfsMaxDepth { get; set; } = SearchUtils.MaxSearchDepth;

    internal EdgeSearchConfig Validate() => this;
}

internal sealed record NodeSearchConfig
{
    internal required List<NodeSearchMethod> SearchMethods { get; set; }

    internal NodeReranker Reranker { get; set; } = NodeReranker.Rrf;

    internal double SimMinScore { get; set; } = SearchUtils.DefaultMinScore;

    internal double MmrLambda { get; set; } = SearchUtils.DefaultMmrLambda;

    internal long BfsMaxDepth { get; set; } = SearchUtils.MaxSearchDepth;

    internal NodeSearchConfig Validate() => this;
}

internal sealed record EpisodeSearchConfig
{
    internal required List<EpisodeSearchMethod> SearchMethods { get; set; }

    internal EpisodeReranker Reranker { get; set; } = EpisodeReranker.Rrf;

    internal double SimMinScore { get; set; } = SearchUtils.DefaultMinScore;

    internal double MmrLambda { get; set; } = SearchUtils.DefaultMmrLambda;

    internal long BfsMaxDepth { get; set; } = SearchUtils.MaxSearchDepth;

    internal EpisodeSearchConfig Validate() => this;
}

internal sealed record CommunitySearchConfig
{
    internal required List<CommunitySearchMethod> SearchMethods { get; set; }

    internal CommunityReranker Reranker { get; set; } = CommunityReranker.Rrf;

    internal double SimMinScore { get; set; } = SearchUtils.DefaultMinScore;

    internal double MmrLambda { get; set; } = SearchUtils.DefaultMmrLambda;

    internal long BfsMaxDepth { get; set; } = SearchUtils.MaxSearchDepth;

    internal CommunitySearchConfig Validate() => this;
}

internal sealed record SearchConfig
{
    internal EdgeSearchConfig? EdgeConfig { get; set; }

    internal NodeSearchConfig? NodeConfig { get; set; }

    internal EpisodeSearchConfig? EpisodeConfig { get; set; }

    internal CommunitySearchConfig? CommunityConfig { get; set; }

    internal long Limit { get; set; } = SearchConfigModule.DefaultSearchLimit;

    // PORT: `Field(default=0)` on a float field. Pydantic keeps the int 0 until a value is assigned. The property is a
    // double (the known difference in Pydantic.DumpFields).
    internal double RerankerMinScore { get; set; }

    internal SearchConfig Validate() => this;
}

internal sealed record SearchResults
{
    internal List<EntityEdge> Edges { get; set; } = [];

    internal List<double> EdgeRerankerScores { get; set; } = [];

    internal List<EntityNode> Nodes { get; set; } = [];

    internal List<double> NodeRerankerScores { get; set; } = [];

    internal List<EpisodicNode> Episodes { get; set; } = [];

    internal List<double> EpisodeRerankerScores { get; set; } = [];

    internal List<CommunityNode> Communities { get; set; } = [];

    internal List<double> CommunityRerankerScores { get; set; } = [];

    internal SearchResults Validate() => this;

    // PORT: a classmethod that builds an instance is a static factory (R16). `cls()` is `new SearchResults()`.
    // TODO(port): SearchResults.merge
    internal static SearchResults Merge(List<SearchResults> resultsList)
    {
        throw new NotSupportedException("TODO(port): SearchResults.merge");
    }
}
