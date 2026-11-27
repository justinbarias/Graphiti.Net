namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Search;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/search/search_helpers.py</c>.</summary>
internal static class SearchHelpersBindings
{
    private const string Module = "graphiti_core.search.search_helpers";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":format_edge_date_range", call => call.Signature("edge").InvokeAsync(SearchHelpers.FormatEdgeDateRange));
        table.Pure.Add(Module + ":search_results_to_context_string", call => call.Signature("search_results").InvokeAsync(SearchHelpers.SearchResultsToContextString));
    }
}
