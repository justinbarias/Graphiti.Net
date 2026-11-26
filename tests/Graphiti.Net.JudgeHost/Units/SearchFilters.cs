namespace Graphiti.JudgeHost.Units;

using System.Text.Json.Nodes;
using Graphiti.Core.Search;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The bindings of <c>graphiti_core/search/search_filters.py</c>.</summary>
internal static class SearchFiltersBindings
{
    private const string Module = "graphiti_core.search.search_filters";

    internal static void Register(BindingTable table)
    {
        table.Registry
            .AddEnum<ComparisonOperator>(Module + ":ComparisonOperator")
            .AddModel(Module + ":DateFilter", DateFilter.ModelValidate)
            .AddModel(Module + ":PropertyFilter", PropertyFilter.ModelValidate)
            .AddModel(Module + ":SearchFilters", SearchFilters.ModelValidate)
            .AddType<DateFilter>("DateFilter", m => [new("date", m.Date), new("comparison_operator", m.ComparisonOperator)])
            .AddType<PropertyFilter>("PropertyFilter", m =>
            [
                new("property_name", m.PropertyName),
                new("property_value", m.PropertyValue),
                new("comparison_operator", m.ComparisonOperator),
            ])
            .AddType<SearchFilters>("SearchFilters", m =>
            [
                new("node_labels", m.NodeLabels),
                new("edge_types", m.EdgeTypes),
                new("valid_at", m.ValidAt),
                new("invalid_at", m.InvalidAt),
                new("created_at", m.CreatedAt),
                new("expired_at", m.ExpiredAt),
                new("edge_uuids", m.EdgeUuids),
                new("edge_source_node_uuids", m.EdgeSourceNodeUuids),
                new("edge_target_node_uuids", m.EdgeTargetNodeUuids),
                new("property_filters", m.PropertyFilters),
            ]);

        table.Schemas.Add(Module + ":DateFilter", DateFilter.ModelJsonSchema);
        table.Schemas.Add(Module + ":PropertyFilter", PropertyFilter.ModelJsonSchema);
        table.Schemas.Add(Module + ":SearchFilters", SearchFilters.ModelJsonSchema);

        // ComparisonOperator(value) is E(value) (R7).
        table.Pure.Add(Module + ":ComparisonOperator", call => call.Signature("value").InvokeAsync((Func<object?, ComparisonOperator>)PyEnum.FromValue<ComparisonOperator>));
        table.Pure.Add(Module + ":DateFilter", call => Construct(call, DateFilter.ModelValidate));
        table.Pure.Add(Module + ":PropertyFilter", call => Construct(call, PropertyFilter.ModelValidate));
        table.Pure.Add(Module + ":SearchFilters", call => Construct(call, SearchFilters.ModelValidate));
        table.Pure.Add(Module + ":cypher_to_opensearch_operator", call => call.Signature("op").InvokeAsync(SearchFiltersModule.CypherToOpensearchOperator));
        table.Pure.Add(Module + ":node_search_filter_query_constructor", call => call.Signature("filters", "provider").InvokeAsync(SearchFiltersModule.NodeSearchFilterQueryConstructor));
        table.Pure.Add(Module + ":date_filter_query_constructor", call => call.Signature("value_name", "param_name", "operator").InvokeAsync(SearchFiltersModule.DateFilterQueryConstructor));
        table.Pure.Add(Module + ":edge_search_filter_query_constructor", call => call.Signature("filters", "provider").InvokeAsync(SearchFiltersModule.EdgeSearchFilterQueryConstructor));
    }

    // M(**kwargs) is M.ModelValidate(data) (R7). A pydantic model takes no positional argument, which is a TypeError.
    private static ValueTask<JsonNode?> Construct<T>(JudgeArguments call, Func<OrderedDictionary<string, object?>, T> modelValidate) =>
        call.Positional.Count == 0
            ? Codec.Result(modelValidate(call.Keywords))
            : throw new JudgeTypeRejectedException($"__init__() takes 1 positional argument but {call.Positional.Count + 1} were given");
}
