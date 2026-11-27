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
internal sealed record EdgeSearchConfig : IPydanticModel
{
    internal required List<EdgeSearchMethod> SearchMethods { get; set; }

    internal EdgeReranker Reranker { get; set; } = EdgeReranker.Rrf;

    internal double SimMinScore { get; set; } = SearchUtils.DefaultMinScore;

    internal double MmrLambda { get; set; } = SearchUtils.DefaultMmrLambda;

    internal long BfsMaxDepth { get; set; } = SearchUtils.MaxSearchDepth;

    internal EdgeSearchConfig Validate() => this;

    internal static EdgeSearchConfig ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EdgeSearchConfig", data);
        var searchMethods = f.Required("search_methods", v => Pydantic.List(v, Pydantic.Enum<EdgeSearchMethod>));
        var reranker = f.Default("reranker", Pydantic.Enum<EdgeReranker>, () => EdgeReranker.Rrf);
        var simMinScore = f.Default("sim_min_score", Pydantic.Float, () => SearchUtils.DefaultMinScore);
        var mmrLambda = f.Default("mmr_lambda", Pydantic.Float, () => SearchUtils.DefaultMmrLambda);
        var bfsMaxDepth = f.Default("bfs_max_depth", Pydantic.Int, () => SearchUtils.MaxSearchDepth);
        f.ThrowIfAny();
        return new EdgeSearchConfig
        {
            SearchMethods = searchMethods.Value,
            Reranker = reranker.Value,
            SimMinScore = simMinScore.Value,
            MmrLambda = mmrLambda.Value,
            BfsMaxDepth = bfsMaxDepth.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("search_methods", SearchMethods),
        ("reranker", Reranker),
        ("sim_min_score", SimMinScore),
        ("mmr_lambda", MmrLambda),
        ("bfs_max_depth", BfsMaxDepth));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("EdgeSearchConfig")
        .Required("search_methods", PydanticType.List(PydanticType.Enum<EdgeSearchMethod>()))
        .Default("reranker", PydanticType.Enum<EdgeReranker>(), EdgeReranker.Rrf)
        .Default("sim_min_score", PydanticType.Float, SearchUtils.DefaultMinScore)
        .Default("mmr_lambda", PydanticType.Float, SearchUtils.DefaultMmrLambda)
        .Default("bfs_max_depth", PydanticType.Int, SearchUtils.MaxSearchDepth);
}

internal sealed record NodeSearchConfig : IPydanticModel
{
    internal required List<NodeSearchMethod> SearchMethods { get; set; }

    internal NodeReranker Reranker { get; set; } = NodeReranker.Rrf;

    internal double SimMinScore { get; set; } = SearchUtils.DefaultMinScore;

    internal double MmrLambda { get; set; } = SearchUtils.DefaultMmrLambda;

    internal long BfsMaxDepth { get; set; } = SearchUtils.MaxSearchDepth;

    internal NodeSearchConfig Validate() => this;

    internal static NodeSearchConfig ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("NodeSearchConfig", data);
        var searchMethods = f.Required("search_methods", v => Pydantic.List(v, Pydantic.Enum<NodeSearchMethod>));
        var reranker = f.Default("reranker", Pydantic.Enum<NodeReranker>, () => NodeReranker.Rrf);
        var simMinScore = f.Default("sim_min_score", Pydantic.Float, () => SearchUtils.DefaultMinScore);
        var mmrLambda = f.Default("mmr_lambda", Pydantic.Float, () => SearchUtils.DefaultMmrLambda);
        var bfsMaxDepth = f.Default("bfs_max_depth", Pydantic.Int, () => SearchUtils.MaxSearchDepth);
        f.ThrowIfAny();
        return new NodeSearchConfig
        {
            SearchMethods = searchMethods.Value,
            Reranker = reranker.Value,
            SimMinScore = simMinScore.Value,
            MmrLambda = mmrLambda.Value,
            BfsMaxDepth = bfsMaxDepth.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("search_methods", SearchMethods),
        ("reranker", Reranker),
        ("sim_min_score", SimMinScore),
        ("mmr_lambda", MmrLambda),
        ("bfs_max_depth", BfsMaxDepth));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("NodeSearchConfig")
        .Required("search_methods", PydanticType.List(PydanticType.Enum<NodeSearchMethod>()))
        .Default("reranker", PydanticType.Enum<NodeReranker>(), NodeReranker.Rrf)
        .Default("sim_min_score", PydanticType.Float, SearchUtils.DefaultMinScore)
        .Default("mmr_lambda", PydanticType.Float, SearchUtils.DefaultMmrLambda)
        .Default("bfs_max_depth", PydanticType.Int, SearchUtils.MaxSearchDepth);
}

internal sealed record EpisodeSearchConfig : IPydanticModel
{
    internal required List<EpisodeSearchMethod> SearchMethods { get; set; }

    internal EpisodeReranker Reranker { get; set; } = EpisodeReranker.Rrf;

    internal double SimMinScore { get; set; } = SearchUtils.DefaultMinScore;

    internal double MmrLambda { get; set; } = SearchUtils.DefaultMmrLambda;

    internal long BfsMaxDepth { get; set; } = SearchUtils.MaxSearchDepth;

    internal EpisodeSearchConfig Validate() => this;

    internal static EpisodeSearchConfig ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EpisodeSearchConfig", data);
        var searchMethods = f.Required("search_methods", v => Pydantic.List(v, Pydantic.Enum<EpisodeSearchMethod>));
        var reranker = f.Default("reranker", Pydantic.Enum<EpisodeReranker>, () => EpisodeReranker.Rrf);
        var simMinScore = f.Default("sim_min_score", Pydantic.Float, () => SearchUtils.DefaultMinScore);
        var mmrLambda = f.Default("mmr_lambda", Pydantic.Float, () => SearchUtils.DefaultMmrLambda);
        var bfsMaxDepth = f.Default("bfs_max_depth", Pydantic.Int, () => SearchUtils.MaxSearchDepth);
        f.ThrowIfAny();
        return new EpisodeSearchConfig
        {
            SearchMethods = searchMethods.Value,
            Reranker = reranker.Value,
            SimMinScore = simMinScore.Value,
            MmrLambda = mmrLambda.Value,
            BfsMaxDepth = bfsMaxDepth.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("search_methods", SearchMethods),
        ("reranker", Reranker),
        ("sim_min_score", SimMinScore),
        ("mmr_lambda", MmrLambda),
        ("bfs_max_depth", BfsMaxDepth));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("EpisodeSearchConfig")
        .Required("search_methods", PydanticType.List(PydanticType.Enum<EpisodeSearchMethod>()))
        .Default("reranker", PydanticType.Enum<EpisodeReranker>(), EpisodeReranker.Rrf)
        .Default("sim_min_score", PydanticType.Float, SearchUtils.DefaultMinScore)
        .Default("mmr_lambda", PydanticType.Float, SearchUtils.DefaultMmrLambda)
        .Default("bfs_max_depth", PydanticType.Int, SearchUtils.MaxSearchDepth);
}

internal sealed record CommunitySearchConfig : IPydanticModel
{
    internal required List<CommunitySearchMethod> SearchMethods { get; set; }

    internal CommunityReranker Reranker { get; set; } = CommunityReranker.Rrf;

    internal double SimMinScore { get; set; } = SearchUtils.DefaultMinScore;

    internal double MmrLambda { get; set; } = SearchUtils.DefaultMmrLambda;

    internal long BfsMaxDepth { get; set; } = SearchUtils.MaxSearchDepth;

    internal CommunitySearchConfig Validate() => this;

    internal static CommunitySearchConfig ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("CommunitySearchConfig", data);
        var searchMethods = f.Required("search_methods", v => Pydantic.List(v, Pydantic.Enum<CommunitySearchMethod>));
        var reranker = f.Default("reranker", Pydantic.Enum<CommunityReranker>, () => CommunityReranker.Rrf);
        var simMinScore = f.Default("sim_min_score", Pydantic.Float, () => SearchUtils.DefaultMinScore);
        var mmrLambda = f.Default("mmr_lambda", Pydantic.Float, () => SearchUtils.DefaultMmrLambda);
        var bfsMaxDepth = f.Default("bfs_max_depth", Pydantic.Int, () => SearchUtils.MaxSearchDepth);
        f.ThrowIfAny();
        return new CommunitySearchConfig
        {
            SearchMethods = searchMethods.Value,
            Reranker = reranker.Value,
            SimMinScore = simMinScore.Value,
            MmrLambda = mmrLambda.Value,
            BfsMaxDepth = bfsMaxDepth.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("search_methods", SearchMethods),
        ("reranker", Reranker),
        ("sim_min_score", SimMinScore),
        ("mmr_lambda", MmrLambda),
        ("bfs_max_depth", BfsMaxDepth));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("CommunitySearchConfig")
        .Required("search_methods", PydanticType.List(PydanticType.Enum<CommunitySearchMethod>()))
        .Default("reranker", PydanticType.Enum<CommunityReranker>(), CommunityReranker.Rrf)
        .Default("sim_min_score", PydanticType.Float, SearchUtils.DefaultMinScore)
        .Default("mmr_lambda", PydanticType.Float, SearchUtils.DefaultMmrLambda)
        .Default("bfs_max_depth", PydanticType.Int, SearchUtils.MaxSearchDepth);
}

internal sealed record SearchConfig : IPydanticModel
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

    internal static SearchConfig ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SearchConfig", data);
        var edgeConfig = f.Default(
            "edge_config",
            v => Pydantic.Optional(v, x => Pydantic.Model(x, "EdgeSearchConfig", EdgeSearchConfig.ModelValidate)),
            () => null);
        var nodeConfig = f.Default(
            "node_config",
            v => Pydantic.Optional(v, x => Pydantic.Model(x, "NodeSearchConfig", NodeSearchConfig.ModelValidate)),
            () => null);
        var episodeConfig = f.Default(
            "episode_config",
            v => Pydantic.Optional(v, x => Pydantic.Model(x, "EpisodeSearchConfig", EpisodeSearchConfig.ModelValidate)),
            () => null);
        var communityConfig = f.Default(
            "community_config",
            v => Pydantic.Optional(v, x => Pydantic.Model(x, "CommunitySearchConfig", CommunitySearchConfig.ModelValidate)),
            () => null);
        var limit = f.Default("limit", Pydantic.Int, () => SearchConfigModule.DefaultSearchLimit);
        var rerankerMinScore = f.Default("reranker_min_score", Pydantic.Float, () => 0);
        f.ThrowIfAny();
        return new SearchConfig
        {
            EdgeConfig = edgeConfig.Value,
            NodeConfig = nodeConfig.Value,
            EpisodeConfig = episodeConfig.Value,
            CommunityConfig = communityConfig.Value,
            Limit = limit.Value,
            RerankerMinScore = rerankerMinScore.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("edge_config", EdgeConfig),
        ("node_config", NodeConfig),
        ("episode_config", EpisodeConfig),
        ("community_config", CommunityConfig),
        ("limit", Limit),
        ("reranker_min_score", RerankerMinScore));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("SearchConfig")
        .Default("edge_config", PydanticType.Optional(PydanticType.Model(EdgeSearchConfig.ModelJsonSchema)), null)
        .Default("node_config", PydanticType.Optional(PydanticType.Model(NodeSearchConfig.ModelJsonSchema)), null)
        .Default("episode_config", PydanticType.Optional(PydanticType.Model(EpisodeSearchConfig.ModelJsonSchema)), null)
        .Default("community_config", PydanticType.Optional(PydanticType.Model(CommunitySearchConfig.ModelJsonSchema)), null)
        .Default("limit", PydanticType.Int, SearchConfigModule.DefaultSearchLimit)
        .Default("reranker_min_score", PydanticType.Float, 0L);
}

internal sealed record SearchResults : IPydanticModel
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

    internal static SearchResults ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SearchResults", data);
        var edges = f.Default(
            "edges",
            v => Pydantic.List(v, x => Pydantic.Model(x, "EntityEdge", d => EntityEdge.ModelValidate(d))),
            () => []);
        var edgeRerankerScores = f.Default("edge_reranker_scores", v => Pydantic.List(v, Pydantic.Float), () => []);
        var nodes = f.Default(
            "nodes",
            v => Pydantic.List(v, x => Pydantic.Model(x, "EntityNode", EntityNode.ModelValidate)),
            () => []);
        var nodeRerankerScores = f.Default("node_reranker_scores", v => Pydantic.List(v, Pydantic.Float), () => []);
        var episodes = f.Default(
            "episodes",
            v => Pydantic.List(v, x => Pydantic.Model(x, "EpisodicNode", EpisodicNode.ModelValidate)),
            () => []);
        var episodeRerankerScores = f.Default("episode_reranker_scores", v => Pydantic.List(v, Pydantic.Float), () => []);
        var communities = f.Default(
            "communities",
            v => Pydantic.List(v, x => Pydantic.Model(x, "CommunityNode", CommunityNode.ModelValidate)),
            () => []);
        var communityRerankerScores = f.Default("community_reranker_scores", v => Pydantic.List(v, Pydantic.Float), () => []);
        f.ThrowIfAny();
        return new SearchResults
        {
            Edges = edges.Value,
            EdgeRerankerScores = edgeRerankerScores.Value,
            Nodes = nodes.Value,
            NodeRerankerScores = nodeRerankerScores.Value,
            Episodes = episodes.Value,
            EpisodeRerankerScores = episodeRerankerScores.Value,
            Communities = communities.Value,
            CommunityRerankerScores = communityRerankerScores.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("edges", Edges),
        ("edge_reranker_scores", EdgeRerankerScores),
        ("nodes", Nodes),
        ("node_reranker_scores", NodeRerankerScores),
        ("episodes", Episodes),
        ("episode_reranker_scores", EpisodeRerankerScores),
        ("communities", Communities),
        ("community_reranker_scores", CommunityRerankerScores));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("SearchResults")
        .Factory("edges", PydanticType.List(PydanticType.Model(EntityEdge.ModelJsonSchema)))
        .Factory("edge_reranker_scores", PydanticType.List(PydanticType.Float))
        .Factory("nodes", PydanticType.List(PydanticType.Model(EntityNode.ModelJsonSchema)))
        .Factory("node_reranker_scores", PydanticType.List(PydanticType.Float))
        .Factory("episodes", PydanticType.List(PydanticType.Model(EpisodicNode.ModelJsonSchema)))
        .Factory("episode_reranker_scores", PydanticType.List(PydanticType.Float))
        .Factory("communities", PydanticType.List(PydanticType.Model(CommunityNode.ModelJsonSchema)))
        .Factory("community_reranker_scores", PydanticType.List(PydanticType.Float));

    // PORT: a classmethod that builds an instance is a static factory (R16). `cls()` is `new SearchResults()`.

    /// <summary>
    /// Merge multiple SearchResults objects into a single SearchResults object.
    ///
    /// Parameters
    /// ----------
    /// results_list : list[SearchResults]
    ///     List of SearchResults objects to merge
    ///
    /// Returns
    /// -------
    /// SearchResults
    ///     A single SearchResults object containing all results
    /// </summary>
    internal static SearchResults Merge(List<SearchResults> resultsList)
    {
        if (resultsList.Count == 0)
        {
            return new SearchResults();
        }

        var merged = new SearchResults();
        foreach (var result in resultsList)
        {
            merged.Edges.AddRange(result.Edges);
            merged.EdgeRerankerScores.AddRange(result.EdgeRerankerScores);
            merged.Nodes.AddRange(result.Nodes);
            merged.NodeRerankerScores.AddRange(result.NodeRerankerScores);
            merged.Episodes.AddRange(result.Episodes);
            merged.EpisodeRerankerScores.AddRange(result.EpisodeRerankerScores);
            merged.Communities.AddRange(result.Communities);
            merged.CommunityRerankerScores.AddRange(result.CommunityRerankerScores);
        }

        return merged;
    }
}
