namespace Graphiti.JudgeHost.Units;

using System.Text.Json.Nodes;
using Graphiti.Core.Search;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The bindings of <c>graphiti_core/search/search_config.py</c>.</summary>
internal static class SearchConfigBindings
{
    private const string Module = "graphiti_core.search.search_config";

    internal static void Register(BindingTable table)
    {
        table.Registry
            .AddEnum<EdgeSearchMethod>(Module + ":EdgeSearchMethod")
            .AddEnum<NodeSearchMethod>(Module + ":NodeSearchMethod")
            .AddEnum<EpisodeSearchMethod>(Module + ":EpisodeSearchMethod")
            .AddEnum<CommunitySearchMethod>(Module + ":CommunitySearchMethod")
            .AddEnum<EdgeReranker>(Module + ":EdgeReranker")
            .AddEnum<NodeReranker>(Module + ":NodeReranker")
            .AddEnum<EpisodeReranker>(Module + ":EpisodeReranker")
            .AddEnum<CommunityReranker>(Module + ":CommunityReranker")
            .AddModel(Module + ":EdgeSearchConfig", EdgeSearchConfig.ModelValidate)
            .AddModel(Module + ":NodeSearchConfig", NodeSearchConfig.ModelValidate)
            .AddModel(Module + ":EpisodeSearchConfig", EpisodeSearchConfig.ModelValidate)
            .AddModel(Module + ":CommunitySearchConfig", CommunitySearchConfig.ModelValidate)
            .AddModel(Module + ":SearchConfig", SearchConfig.ModelValidate)
            .AddModel(Module + ":SearchResults", SearchResults.ModelValidate)
            .AddType<EdgeSearchConfig>("EdgeSearchConfig", m => ConfigFields(m.SearchMethods, m.Reranker, m.SimMinScore, m.MmrLambda, m.BfsMaxDepth))
            .AddType<NodeSearchConfig>("NodeSearchConfig", m => ConfigFields(m.SearchMethods, m.Reranker, m.SimMinScore, m.MmrLambda, m.BfsMaxDepth))
            .AddType<EpisodeSearchConfig>("EpisodeSearchConfig", m => ConfigFields(m.SearchMethods, m.Reranker, m.SimMinScore, m.MmrLambda, m.BfsMaxDepth))
            .AddType<CommunitySearchConfig>("CommunitySearchConfig", m => ConfigFields(m.SearchMethods, m.Reranker, m.SimMinScore, m.MmrLambda, m.BfsMaxDepth))
            .AddType<SearchConfig>("SearchConfig", m =>
            [
                new("edge_config", m.EdgeConfig),
                new("node_config", m.NodeConfig),
                new("episode_config", m.EpisodeConfig),
                new("community_config", m.CommunityConfig),
                new("limit", m.Limit),
                new("reranker_min_score", m.RerankerMinScore),
            ])
            .AddType<SearchResults>("SearchResults", m =>
            [
                new("edges", m.Edges),
                new("edge_reranker_scores", m.EdgeRerankerScores),
                new("nodes", m.Nodes),
                new("node_reranker_scores", m.NodeRerankerScores),
                new("episodes", m.Episodes),
                new("episode_reranker_scores", m.EpisodeRerankerScores),
                new("communities", m.Communities),
                new("community_reranker_scores", m.CommunityRerankerScores),
            ]);

        table.Schemas.Add(Module + ":EdgeSearchConfig", EdgeSearchConfig.ModelJsonSchema);
        table.Schemas.Add(Module + ":NodeSearchConfig", NodeSearchConfig.ModelJsonSchema);
        table.Schemas.Add(Module + ":EpisodeSearchConfig", EpisodeSearchConfig.ModelJsonSchema);
        table.Schemas.Add(Module + ":CommunitySearchConfig", CommunitySearchConfig.ModelJsonSchema);
        table.Schemas.Add(Module + ":SearchConfig", SearchConfig.ModelJsonSchema);
        table.Schemas.Add(Module + ":SearchResults", SearchResults.ModelJsonSchema);

        // E(value) (R7).
        table.Pure.Add(Module + ":EdgeSearchMethod", call => call.Signature("value").InvokeAsync((Func<object?, EdgeSearchMethod>)PyEnum.FromValue<EdgeSearchMethod>));
        table.Pure.Add(Module + ":NodeSearchMethod", call => call.Signature("value").InvokeAsync((Func<object?, NodeSearchMethod>)PyEnum.FromValue<NodeSearchMethod>));
        table.Pure.Add(Module + ":EpisodeSearchMethod", call => call.Signature("value").InvokeAsync((Func<object?, EpisodeSearchMethod>)PyEnum.FromValue<EpisodeSearchMethod>));
        table.Pure.Add(Module + ":CommunitySearchMethod", call => call.Signature("value").InvokeAsync((Func<object?, CommunitySearchMethod>)PyEnum.FromValue<CommunitySearchMethod>));
        table.Pure.Add(Module + ":EdgeReranker", call => call.Signature("value").InvokeAsync((Func<object?, EdgeReranker>)PyEnum.FromValue<EdgeReranker>));
        table.Pure.Add(Module + ":NodeReranker", call => call.Signature("value").InvokeAsync((Func<object?, NodeReranker>)PyEnum.FromValue<NodeReranker>));
        table.Pure.Add(Module + ":EpisodeReranker", call => call.Signature("value").InvokeAsync((Func<object?, EpisodeReranker>)PyEnum.FromValue<EpisodeReranker>));
        table.Pure.Add(Module + ":CommunityReranker", call => call.Signature("value").InvokeAsync((Func<object?, CommunityReranker>)PyEnum.FromValue<CommunityReranker>));

        table.Pure.Add(Module + ":EdgeSearchConfig", call => Construct(call, EdgeSearchConfig.ModelValidate));
        table.Pure.Add(Module + ":NodeSearchConfig", call => Construct(call, NodeSearchConfig.ModelValidate));
        table.Pure.Add(Module + ":EpisodeSearchConfig", call => Construct(call, EpisodeSearchConfig.ModelValidate));
        table.Pure.Add(Module + ":CommunitySearchConfig", call => Construct(call, CommunitySearchConfig.ModelValidate));
        table.Pure.Add(Module + ":SearchConfig", call => Construct(call, SearchConfig.ModelValidate));
        table.Pure.Add(Module + ":SearchResults", call => Construct(call, SearchResults.ModelValidate));

        table.Pure.Add(Module + ":SearchConfig.model_copy", ModelCopy);
        table.Pure.Add(Module + ":SearchConfig.model_dump", call => Codec.Result(call.Signature("self").Get<SearchConfig>("self").ModelDump()));
        table.Pure.Add(Module + ":SearchResults.merge", call => call.Signature("results_list").InvokeAsync(SearchResults.Merge));
    }

    private static IEnumerable<KeyValuePair<string, object?>> ConfigFields(object searchMethods, object reranker, double simMinScore, double mmrLambda, long bfsMaxDepth) =>
    [
        new("search_methods", searchMethods),
        new("reranker", reranker),
        new("sim_min_score", simMinScore),
        new("mmr_lambda", mmrLambda),
        new("bfs_max_depth", bfsMaxDepth),
    ];

    // m.model_copy() is `m with { }`, and model_copy(update={...}) assigns each key after the copy with no
    // validation (R7). A value that the typed property cannot hold is a type rejection (R22).
    private static ValueTask<JsonNode?> ModelCopy(JudgeArguments call)
    {
        var p = call.Signature("self", "update");
        var copy = p.Get<SearchConfig>("self") with { };
        if (p.Has("update") && p.Value("update") is { } update)
        {
            foreach (var (key, value) in Py.AsDict(update))
            {
                switch (key)
                {
                    case "edge_config":
                        copy.EdgeConfig = (EdgeSearchConfig?)JudgeTypes.Convert(typeof(EdgeSearchConfig), value, "update['edge_config']");
                        break;
                    case "node_config":
                        copy.NodeConfig = (NodeSearchConfig?)JudgeTypes.Convert(typeof(NodeSearchConfig), value, "update['node_config']");
                        break;
                    case "episode_config":
                        copy.EpisodeConfig = (EpisodeSearchConfig?)JudgeTypes.Convert(typeof(EpisodeSearchConfig), value, "update['episode_config']");
                        break;
                    case "community_config":
                        copy.CommunityConfig = (CommunitySearchConfig?)JudgeTypes.Convert(typeof(CommunitySearchConfig), value, "update['community_config']");
                        break;
                    case "limit":
                        copy.Limit = (long)JudgeTypes.Convert(typeof(long), value, "update['limit']")!;
                        break;
                    case "reranker_min_score":
                        copy.RerankerMinScore = (double)JudgeTypes.Convert(typeof(double), value, "update['reranker_min_score']")!;
                        break;
                    default:
                        throw new JudgeTypeRejectedException($"SearchConfig has no field {key}, which the C# record cannot hold");
                }
            }
        }

        return Codec.Result(copy);
    }

    // M(**kwargs) is M.ModelValidate(data) (R7). A pydantic model takes no positional argument, which is a TypeError.
    private static ValueTask<JsonNode?> Construct<T>(JudgeArguments call, Func<OrderedDictionary<string, object?>, T> modelValidate) =>
        call.Positional.Count == 0
            ? Codec.Result(modelValidate(call.Keywords))
            : throw new JudgeTypeRejectedException($"__init__() takes 1 positional argument but {call.Positional.Count + 1} were given");
}
