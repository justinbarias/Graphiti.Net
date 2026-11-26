// Evaluation test for graphiti_core/search/search_config.py, search_config_recipes.py, and search_helpers.py
// (port/RULES.md, R23 and R25). values/search_config.json holds the constants, the enum members, the 16 recipes, and
// about 500 calls that port/tools/record_search_config_values.py recorded from Python. A datetime in an input is its
// isoformat() text under a key that ends in `_at`.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Search;
using Graphiti.PyCompat;

public class SearchConfigValuesTests
{
    private static readonly OrderedDictionary<string, object?> Document =
        (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "values", "search_config.json")))!;

    [Fact]
    public void The_constants_are_the_values_of_python()
    {
        var constants = (OrderedDictionary<string, object?>)Document["constants"]!;
        Assert.Equal(constants["DEFAULT_SEARCH_LIMIT"], SearchConfigModule.DefaultSearchLimit);
    }

    [Fact]
    public void Each_enum_has_the_members_of_python_in_order()
    {
        var enums = (OrderedDictionary<string, object?>)Document["enums"]!;
        Assert.Equal(8, enums.Count);
        Assert.Equal(Members(enums["EdgeSearchMethod"]), Port<EdgeSearchMethod>());
        Assert.Equal(Members(enums["NodeSearchMethod"]), Port<NodeSearchMethod>());
        Assert.Equal(Members(enums["EpisodeSearchMethod"]), Port<EpisodeSearchMethod>());
        Assert.Equal(Members(enums["CommunitySearchMethod"]), Port<CommunitySearchMethod>());
        Assert.Equal(Members(enums["EdgeReranker"]), Port<EdgeReranker>());
        Assert.Equal(Members(enums["NodeReranker"]), Port<NodeReranker>());
        Assert.Equal(Members(enums["EpisodeReranker"]), Port<EpisodeReranker>());
        Assert.Equal(Members(enums["CommunityReranker"]), Port<CommunityReranker>());
    }

    [Fact]
    public void Each_recipe_has_the_fields_of_python()
    {
        var recipes = (OrderedDictionary<string, object?>)Document["recipes"]!;
        var port = new OrderedDictionary<string, SearchConfig>
        {
            ["COMBINED_HYBRID_SEARCH_RRF"] = SearchConfigRecipes.CombinedHybridSearchRrf,
            ["COMBINED_HYBRID_SEARCH_MMR"] = SearchConfigRecipes.CombinedHybridSearchMmr,
            ["COMBINED_HYBRID_SEARCH_CROSS_ENCODER"] = SearchConfigRecipes.CombinedHybridSearchCrossEncoder,
            ["EDGE_HYBRID_SEARCH_RRF"] = SearchConfigRecipes.EdgeHybridSearchRrf,
            ["EDGE_HYBRID_SEARCH_MMR"] = SearchConfigRecipes.EdgeHybridSearchMmr,
            ["EDGE_HYBRID_SEARCH_NODE_DISTANCE"] = SearchConfigRecipes.EdgeHybridSearchNodeDistance,
            ["EDGE_HYBRID_SEARCH_EPISODE_MENTIONS"] = SearchConfigRecipes.EdgeHybridSearchEpisodeMentions,
            ["EDGE_HYBRID_SEARCH_CROSS_ENCODER"] = SearchConfigRecipes.EdgeHybridSearchCrossEncoder,
            ["NODE_HYBRID_SEARCH_RRF"] = SearchConfigRecipes.NodeHybridSearchRrf,
            ["NODE_HYBRID_SEARCH_MMR"] = SearchConfigRecipes.NodeHybridSearchMmr,
            ["NODE_HYBRID_SEARCH_NODE_DISTANCE"] = SearchConfigRecipes.NodeHybridSearchNodeDistance,
            ["NODE_HYBRID_SEARCH_EPISODE_MENTIONS"] = SearchConfigRecipes.NodeHybridSearchEpisodeMentions,
            ["NODE_HYBRID_SEARCH_CROSS_ENCODER"] = SearchConfigRecipes.NodeHybridSearchCrossEncoder,
            ["COMMUNITY_HYBRID_SEARCH_RRF"] = SearchConfigRecipes.CommunityHybridSearchRrf,
            ["COMMUNITY_HYBRID_SEARCH_MMR"] = SearchConfigRecipes.CommunityHybridSearchMmr,
            ["COMMUNITY_HYBRID_SEARCH_CROSS_ENCODER"] = SearchConfigRecipes.CommunityHybridSearchCrossEncoder,
        };
        Assert.Equal(recipes.Keys, port.Keys);
        foreach (var (name, recipe) in port)
        {
            var dump = Plain(recipe.ModelDump());
            Assert.True(Same(recipes[name], dump), $"{name}: expected {PyJson.Dumps(recipes[name])} but got {PyJson.Dumps(dump)}");
        }
    }

    [Fact]
    public void Each_model_validates_as_python_does()
    {
        var cases = (List<object?>)Document["validate"]!;
        Assert.Equal(350, cases.Count);
        List<string> failures = [];
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            var model = (string)recorded["model"]!;
            var data = (OrderedDictionary<string, object?>)recorded["data"]!;
            Func<OrderedDictionary<string, object?>, IPydanticModel> validate = model switch
            {
                "EdgeSearchConfig" => d => EdgeSearchConfig.ModelValidate(d),
                "NodeSearchConfig" => d => NodeSearchConfig.ModelValidate(d),
                "EpisodeSearchConfig" => d => EpisodeSearchConfig.ModelValidate(d),
                "CommunitySearchConfig" => d => CommunitySearchConfig.ModelValidate(d),
                "SearchConfig" => d => SearchConfig.ModelValidate(d),
                "SearchResults" => d => SearchResults.ModelValidate(d),
                _ => throw new InvalidOperationException(model),
            };
            var label = $"{model}({PyJson.Dumps(data)})";
            var expected = PyJson.Dumps(recorded.GetValueOrDefault("ok") ?? recorded.GetValueOrDefault("text"));
            try
            {
                var dump = Plain(validate(data).ModelDump(json: false));
                if (!recorded.TryGetValue("ok", out var ok) || !Same(ok, dump))
                {
                    failures.Add($"{label}: expected {expected} but got {PyJson.Dumps(dump)}");
                }
            }
            catch (ValidationException e)
            {
                if (!recorded.TryGetValue("text", out var text) || (string?)text != e.Message)
                {
                    failures.Add($"{label}: expected {expected} but got {e.Message}");
                }
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} calls differ from Python:\n" + string.Join("\n", failures.Take(10)));
    }

    [Fact]
    public void Merge_gives_each_recorded_result_of_python()
    {
        var cases = (List<object?>)Document["merge"]!;
        Assert.Equal(40, cases.Count);
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            var inputs = ((List<object?>)recorded["inputs"]!).Select(Results).ToList();
            var merged = SearchResults.Merge(inputs);
            var got = new OrderedDictionary<string, object?>
            {
                ["edges"] = merged.Edges.Select(e => (object?)e.Uuid).ToList(),
                ["edge_reranker_scores"] = merged.EdgeRerankerScores.Select(s => (object?)s).ToList(),
                ["nodes"] = merged.Nodes.Select(n => (object?)n.Uuid).ToList(),
                ["node_reranker_scores"] = merged.NodeRerankerScores.Select(s => (object?)s).ToList(),
                ["episodes"] = merged.Episodes.Select(n => (object?)n.Uuid).ToList(),
                ["episode_reranker_scores"] = merged.EpisodeRerankerScores.Select(s => (object?)s).ToList(),
                ["communities"] = merged.Communities.Select(n => (object?)n.Uuid).ToList(),
                ["community_reranker_scores"] = merged.CommunityRerankerScores.Select(s => (object?)s).ToList(),
            };
            Assert.Equal(PyJson.Dumps(recorded["result"]), PyJson.Dumps(got));
        }
    }

    [Fact]
    public void Format_edge_date_range_gives_the_text_of_python()
    {
        var cases = (List<object?>)Document["format_edge_date_range"]!;
        Assert.Equal(80, cases.Count);
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            var edge = EntityEdge.ModelValidate(Dates(recorded["edge"]));
            Assert.Equal((string)recorded["result"]!, SearchHelpers.FormatEdgeDateRange(edge));
        }
    }

    [Fact]
    public void Search_results_to_context_string_gives_the_text_of_python()
    {
        var cases = (List<object?>)Document["context_string"]!;
        Assert.Equal(40, cases.Count);
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            Assert.Equal((string)recorded["result"]!, SearchHelpers.SearchResultsToContextString(Results(recorded["results"])));
        }
    }

    private static SearchResults Results(object? value)
    {
        var d = (OrderedDictionary<string, object?>)value!;
        return new SearchResults
        {
            Edges = [.. Items(d["edges"]).Select(x => EntityEdge.ModelValidate(Dates(x)))],
            EdgeRerankerScores = [.. Items(d["edge_reranker_scores"]).Select(Py.AsDouble)],
            Nodes = [.. Items(d["nodes"]).Select(x => EntityNode.ModelValidate(Dates(x)))],
            NodeRerankerScores = [.. Items(d["node_reranker_scores"]).Select(Py.AsDouble)],
            Episodes = [.. Items(d["episodes"]).Select(x => EpisodicNode.ModelValidate(Dates(x)))],
            EpisodeRerankerScores = [.. Items(d["episode_reranker_scores"]).Select(Py.AsDouble)],
            Communities = [.. Items(d["communities"]).Select(x => CommunityNode.ModelValidate(Dates(x)))],
            CommunityRerankerScores = [.. Items(d["community_reranker_scores"]).Select(Py.AsDouble)],
        }.Validate();
    }

    private static List<object?> Items(object? value) => (List<object?>)value!;

    // The recorder writes a datetime as its isoformat() text under a key that ends in `_at`.
    private static OrderedDictionary<string, object?> Dates(object? value)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, item) in (OrderedDictionary<string, object?>)value!)
        {
            result[key] = key.EndsWith("_at", StringComparison.Ordinal) && item is string text ? PyDateTime.FromIsoFormat(text) : item;
        }

        return result;
    }

    private static List<string> Members(object? recorded) =>
        [.. ((List<object?>)recorded!).Select(m => PyJson.Dumps(m))];

    private static List<string> Port<T>()
        where T : struct, Enum =>
        [.. Enum.GetValues<T>().Select(m => PyJson.Dumps(new List<object?> { PyEnum.Name(m), PyEnum.Value(m) }))];

    // model_dump() with each enum member as its value, as the recorder writes it. The recorder does not use
    // model_dump(mode='json'), which keeps nan and inf where Pydantic.JsonValue gives null.
    private static object? Plain(object? value) => value switch
    {
        Enum e => PyEnum.Value(e),
        OrderedDictionary<string, object?> d => new OrderedDictionary<string, object?>(d.Select(p => KeyValuePair.Create(p.Key, Plain(p.Value)))),
        List<object?> l => l.Select(Plain).ToList(),
        _ => value,
    };

    // Python's equality of two dumped values. An int equals a float of the same value, as in Python: pydantic keeps the
    // int default 0 of SearchConfig.reranker_min_score, and the port holds the float 0.0 (Pydantic.DumpFields).
    private static bool Same(object? expected, object? actual) => (expected, actual) switch
    {
        (long a, double b) => a == b,
        (double a, long b) => a == b,
        (double a, double b) => a.Equals(b),
        (List<object?> a, List<object?> b) => a.Count == b.Count && a.Zip(b).All(p => Same(p.First, p.Second)),
        (OrderedDictionary<string, object?> a, OrderedDictionary<string, object?> b) =>
            a.Keys.SequenceEqual(b.Keys) && a.All(p => Same(p.Value, b[p.Key])),
        _ => PyJson.Dumps(expected) == PyJson.Dumps(actual),
    };
}
