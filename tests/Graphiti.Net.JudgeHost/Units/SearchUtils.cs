namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Search;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/search/search_utils.py</c>.</summary>
/// <remarks>
/// The 19 cases of <c>fulltext_query</c> pass a <c>types:SimpleNamespace</c> as the driver. The lead owns the decode
/// of that path (rule R22), so the binding is here and the path is not registered. The cases read the attributes
/// <c>provider</c> and <c>fulltext_syntax</c>, and <c>build_fulltext_query</c> for a FalkorDB provider.
/// </remarks>
internal static class SearchUtilsBindings
{
    private const string Module = "graphiti_core.search.search_utils:";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + "calculate_cosine_similarity", call => call.Signature("vector1", "vector2").InvokeAsync(SearchUtils.CalculateCosineSimilarity));
        table.Pure.Add(Module + "fulltext_query", call => call.Signature("query", "group_ids", "driver").InvokeAsync(SearchUtils.FulltextQuery));
        table.Pure.Add(Module + "rrf", call => call.Signature("results", "rank_const", "min_score").InvokeAsync(SearchUtils.Rrf));
        table.Pure.Add(
            Module + "maximal_marginal_relevance",
            call => call.Signature("query_vector", "candidates", "mmr_lambda", "min_score").InvokeAsync(SearchUtils.MaximalMarginalRelevance));
    }
}
