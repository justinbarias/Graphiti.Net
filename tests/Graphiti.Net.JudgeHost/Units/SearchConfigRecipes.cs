namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Search;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/search/search_config_recipes.py</c>.</summary>
internal static class SearchConfigRecipesBindings
{
    private const string Module = "graphiti_core.search.search_config_recipes";

    internal static void Register(BindingTable table)
    {
        // Each recipe is one shared object (R17), so a $ref gives that object.
        table.Registry
            .AddRef(Module + ":COMBINED_HYBRID_SEARCH_RRF", SearchConfigRecipes.CombinedHybridSearchRrf)
            .AddRef(Module + ":COMBINED_HYBRID_SEARCH_MMR", SearchConfigRecipes.CombinedHybridSearchMmr)
            .AddRef(Module + ":COMBINED_HYBRID_SEARCH_CROSS_ENCODER", SearchConfigRecipes.CombinedHybridSearchCrossEncoder)
            .AddRef(Module + ":EDGE_HYBRID_SEARCH_RRF", SearchConfigRecipes.EdgeHybridSearchRrf)
            .AddRef(Module + ":EDGE_HYBRID_SEARCH_MMR", SearchConfigRecipes.EdgeHybridSearchMmr)
            .AddRef(Module + ":EDGE_HYBRID_SEARCH_NODE_DISTANCE", SearchConfigRecipes.EdgeHybridSearchNodeDistance)
            .AddRef(Module + ":EDGE_HYBRID_SEARCH_EPISODE_MENTIONS", SearchConfigRecipes.EdgeHybridSearchEpisodeMentions)
            .AddRef(Module + ":EDGE_HYBRID_SEARCH_CROSS_ENCODER", SearchConfigRecipes.EdgeHybridSearchCrossEncoder)
            .AddRef(Module + ":NODE_HYBRID_SEARCH_RRF", SearchConfigRecipes.NodeHybridSearchRrf)
            .AddRef(Module + ":NODE_HYBRID_SEARCH_MMR", SearchConfigRecipes.NodeHybridSearchMmr)
            .AddRef(Module + ":NODE_HYBRID_SEARCH_NODE_DISTANCE", SearchConfigRecipes.NodeHybridSearchNodeDistance)
            .AddRef(Module + ":NODE_HYBRID_SEARCH_EPISODE_MENTIONS", SearchConfigRecipes.NodeHybridSearchEpisodeMentions)
            .AddRef(Module + ":NODE_HYBRID_SEARCH_CROSS_ENCODER", SearchConfigRecipes.NodeHybridSearchCrossEncoder)
            .AddRef(Module + ":COMMUNITY_HYBRID_SEARCH_RRF", SearchConfigRecipes.CommunityHybridSearchRrf)
            .AddRef(Module + ":COMMUNITY_HYBRID_SEARCH_MMR", SearchConfigRecipes.CommunityHybridSearchMmr)
            .AddRef(Module + ":COMMUNITY_HYBRID_SEARCH_CROSS_ENCODER", SearchConfigRecipes.CommunityHybridSearchCrossEncoder);
    }
}
