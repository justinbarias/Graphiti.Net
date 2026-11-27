namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Search;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/search/search_utils.py</c>.</summary>
/// <remarks>
/// The 19 cases of <c>fulltext_query</c> pass a <c>types:SimpleNamespace</c> as the driver. The binding wraps the
/// namespace in <see cref="TypesBindings.NamespaceDriver"/>, which sets the attributes <c>provider</c> and
/// <c>fulltext_syntax</c> that the namespace has.
/// </remarks>
internal static class SearchUtilsBindings
{
    private const string Module = "graphiti_core.search.search_utils:";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + "calculate_cosine_similarity", call => call.Signature("vector1", "vector2").InvokeAsync(SearchUtils.CalculateCosineSimilarity));
        table.Pure.Add(Module + "fulltext_query", call =>
        {
            var arguments = call.Signature("query", "group_ids", "driver");
            var query = arguments.String("query");
            var groupIds = arguments.NullableList<string>("group_ids");
            if (arguments.Value("driver") is not TypesBindings.Namespace source)
            {
                return arguments.InvokeAsync(SearchUtils.FulltextQuery);
            }

            return Codec.Result(SearchUtils.FulltextQuery(query, groupIds, new TypesBindings.NamespaceDriver(source)));
        });
        table.Pure.Add(Module + "rrf", call => call.Signature("results", "rank_const", "min_score").InvokeAsync(SearchUtils.Rrf));
        table.Pure.Add(
            Module + "maximal_marginal_relevance",
            call => call.Signature("query_vector", "candidates", "mmr_lambda", "min_score").InvokeAsync(SearchUtils.MaximalMarginalRelevance));
    }
}
