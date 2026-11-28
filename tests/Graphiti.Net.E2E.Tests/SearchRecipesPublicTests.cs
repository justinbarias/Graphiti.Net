// The 16 public search recipes of SearchRecipes (plan 0004, layer 9, packet P9d). Each recipe maps to the ported
// recipe of search_config_recipes.py with the same values, and each read gives a new object.
namespace Graphiti.E2E.Tests;

using Graphiti.Internal;
using Graphiti.PyCompat;
using CoreSearch = Graphiti.Core.Search;

public sealed class SearchRecipesPublicTests
{
    // GraphitiCore.search writes num_results into these two ported recipes (GAPS U8). Another test of the process can
    // run a search first, so the limit of these two is compared with the upstream default and not with the object.
    private static readonly HashSet<string> SharedBySearch = ["EdgeHybridSearchRrf", "EdgeHybridSearchNodeDistance"];

    public static TheoryData<string> Names =>
    [
        "CombinedHybridSearchRrf", "CombinedHybridSearchMmr", "CombinedHybridSearchCrossEncoder", "EdgeHybridSearchRrf",
        "EdgeHybridSearchMmr", "EdgeHybridSearchNodeDistance", "EdgeHybridSearchEpisodeMentions",
        "EdgeHybridSearchCrossEncoder", "NodeHybridSearchRrf", "NodeHybridSearchMmr", "NodeHybridSearchNodeDistance",
        "NodeHybridSearchEpisodeMentions", "NodeHybridSearchCrossEncoder", "CommunityHybridSearchRrf",
        "CommunityHybridSearchMmr", "CommunityHybridSearchCrossEncoder",
    ];

    [Fact]
    public void There_is_one_public_recipe_for_each_ported_recipe()
    {
        var ported = typeof(CoreSearch.SearchConfigRecipes)
            .GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)
            .Select(f => f.Name)
            .Order(StringComparer.Ordinal);
        var published = typeof(SearchRecipes).GetProperties().Select(p => p.Name).Order(StringComparer.Ordinal);
        Assert.Equal(ported, published);
        Assert.Equal(16, Names.Count);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_recipe_maps_to_the_values_of_the_ported_recipe(string name)
    {
        var published = (SearchConfig)typeof(SearchRecipes).GetProperty(name)!.GetValue(null)!;
        var ported = (CoreSearch.SearchConfig)typeof(CoreSearch.SearchConfigRecipes)
            .GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(null)!;

        var expected = ported.ModelDump(json: true);
        var actual = PublicMapping.ToPorted(published).ModelDump(json: true);
        if (SharedBySearch.Contains(name))
        {
            Assert.Equal(10L, actual["limit"]);
            expected.Remove("limit");
            actual.Remove("limit");
        }

        Assert.Equal(PyJson.Dumps(expected), PyJson.Dumps(actual));
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Each_read_gives_a_new_config(string name)
    {
        var property = typeof(SearchRecipes).GetProperty(name)!;
        var first = (SearchConfig)property.GetValue(null)!;
        var second = (SearchConfig)property.GetValue(null)!;
        Assert.NotSame(first, second);
        if (first.EdgeConfig is not null)
        {
            Assert.NotSame(first.EdgeConfig.SearchMethods, second.EdgeConfig!.SearchMethods);
        }

        if (first.NodeConfig is not null)
        {
            Assert.NotSame(first.NodeConfig.SearchMethods, second.NodeConfig!.SearchMethods);
        }
    }
}
