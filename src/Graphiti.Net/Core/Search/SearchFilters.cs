// Ported from graphiti_core/search/search_filters.py
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

using Graphiti.Core.Driver;
using Graphiti.PyCompat;

internal enum ComparisonOperator
{
    [PyEnumMember("equals", "=")]
    Equals,
    [PyEnumMember("not_equals", "<>")]
    NotEquals,
    [PyEnumMember("greater_than", ">")]
    GreaterThan,
    [PyEnumMember("less_than", "<")]
    LessThan,
    [PyEnumMember("greater_than_equal", ">=")]
    GreaterThanEqual,
    [PyEnumMember("less_than_equal", "<=")]
    LessThanEqual,
    [PyEnumMember("is_null", "IS NULL")]
    IsNull,
    [PyEnumMember("is_not_null", "IS NOT NULL")]
    IsNotNull,
}

internal sealed record DateFilter : IPydanticModel
{
    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("DateFilter")
        .Default("date", PydanticType.Optional(PydanticType.DateTime), null, "A datetime to filter on")
        .Required("comparison_operator", PydanticType.Enum<ComparisonOperator>(), "Comparison operator for date filter");

    internal PyDateTime? Date { get; set; }

    internal required ComparisonOperator ComparisonOperator { get; set; }

    internal DateFilter Validate() => this;

    internal static DateFilter ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("DateFilter", data);
        var date = f.Default("date", v => Pydantic.OptionalValue(v, Pydantic.DateTime), () => null);
        var comparisonOperator = f.Required("comparison_operator", Pydantic.Enum<ComparisonOperator>);
        f.ThrowIfAny();
        return new DateFilter { Date = date.Value, ComparisonOperator = comparisonOperator.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("date", Date), ("comparison_operator", ComparisonOperator));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

internal sealed record PropertyFilter : IPydanticModel
{
    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("PropertyFilter")
        .Required("property_name", PydanticType.Str, "Property name")
        .Default(
            "property_value",
            PydanticType.Union(PydanticType.Str, PydanticType.Int, PydanticType.Float, PydanticType.Null),
            null,
            "Value you want to match on for the property")
        .Required("comparison_operator", PydanticType.Enum<ComparisonOperator>(), "Comparison operator for the property");

    internal required string PropertyName { get; set; }

    internal object? PropertyValue { get; set; }

    internal required ComparisonOperator ComparisonOperator { get; set; }

    internal PropertyFilter Validate() => this;

    internal static PropertyFilter ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("PropertyFilter", data);
        var propertyName = f.Required("property_name", Pydantic.Str);
        var propertyValue = f.Default("property_value", v => Pydantic.Optional(v, Pydantic.StrIntFloat), () => null);
        var comparisonOperator = f.Required("comparison_operator", Pydantic.Enum<ComparisonOperator>);
        f.ThrowIfAny();
        return new PropertyFilter
        {
            PropertyName = propertyName.Value,
            PropertyValue = propertyValue.Value,
            ComparisonOperator = comparisonOperator.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) =>
        Pydantic.DumpFields(json, ("property_name", PropertyName), ("property_value", PropertyValue), ("comparison_operator", ComparisonOperator));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

internal sealed record SearchFilters : IPydanticModel
{
    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("SearchFilters")
        .Default("node_labels", PydanticType.Optional(PydanticType.List(PydanticType.Str)), null, "List of node labels to filter on")
        .Default("edge_types", PydanticType.Optional(PydanticType.List(PydanticType.Str)), null, "List of edge types to filter on")
        .Default("valid_at", PydanticType.Optional(PydanticType.List(PydanticType.List(PydanticType.Model(DateFilter.ModelJsonSchema)))), null)
        .Default("invalid_at", PydanticType.Optional(PydanticType.List(PydanticType.List(PydanticType.Model(DateFilter.ModelJsonSchema)))), null)
        .Default("created_at", PydanticType.Optional(PydanticType.List(PydanticType.List(PydanticType.Model(DateFilter.ModelJsonSchema)))), null)
        .Default("expired_at", PydanticType.Optional(PydanticType.List(PydanticType.List(PydanticType.Model(DateFilter.ModelJsonSchema)))), null)
        .Default("edge_uuids", PydanticType.Optional(PydanticType.List(PydanticType.Str)), null)
        .Default("edge_source_node_uuids", PydanticType.Optional(PydanticType.List(PydanticType.Str)), null, "Restrict edge results to these source node UUIDs")
        .Default("edge_target_node_uuids", PydanticType.Optional(PydanticType.List(PydanticType.Str)), null, "Restrict edge results to these target node UUIDs")
        .Default("property_filters", PydanticType.Optional(PydanticType.List(PydanticType.Model(PropertyFilter.ModelJsonSchema))), null);

    internal List<string>? NodeLabels { get; set; }

    internal List<string>? EdgeTypes { get; set; }

    internal List<List<DateFilter>>? ValidAt { get; set; }

    internal List<List<DateFilter>>? InvalidAt { get; set; }

    internal List<List<DateFilter>>? CreatedAt { get; set; }

    internal List<List<DateFilter>>? ExpiredAt { get; set; }

    internal List<string>? EdgeUuids { get; set; }

    internal List<string>? EdgeSourceNodeUuids { get; set; }

    internal List<string>? EdgeTargetNodeUuids { get; set; }

    internal List<PropertyFilter>? PropertyFilters { get; set; }

    // @field_validator('node_labels'): validate_node_label_filters. PydanticFields puts the field name in front of the
    // location when ModelValidate reads the field, so the error has no field of its own here.
    private static List<string>? ValidateNodeLabelFilters(List<string>? value)
    {
        Pydantic.Wrap<NodeLabelValidationException>(() => Helpers.ValidateNodeLabels(value), "SearchFilters");
        return value;
    }

    internal SearchFilters Validate()
    {
        ValidateNodeLabelFilters(NodeLabels);
        return this;
    }

    internal static SearchFilters ModelValidate(OrderedDictionary<string, object?> data)
    {
        static List<string>? Strs(object? v) => Pydantic.Optional(v, x => Pydantic.List(x, Pydantic.Str));
        static List<List<DateFilter>>? DateFilters(object? v) =>
            Pydantic.Optional(v, x => Pydantic.List(x, y => Pydantic.List(y, z => Pydantic.Model(z, "DateFilter", DateFilter.ModelValidate))));

        var f = new PydanticFields("SearchFilters", data);
        var nodeLabels = f.Default("node_labels", v => ValidateNodeLabelFilters(Strs(v)), () => null);
        var edgeTypes = f.Default("edge_types", Strs, () => null);
        var validAt = f.Default("valid_at", DateFilters, () => null);
        var invalidAt = f.Default("invalid_at", DateFilters, () => null);
        var createdAt = f.Default("created_at", DateFilters, () => null);
        var expiredAt = f.Default("expired_at", DateFilters, () => null);
        var edgeUuids = f.Default("edge_uuids", Strs, () => null);
        var edgeSourceNodeUuids = f.Default("edge_source_node_uuids", Strs, () => null);
        var edgeTargetNodeUuids = f.Default("edge_target_node_uuids", Strs, () => null);
        var propertyFilters = f.Default(
            "property_filters",
            v => Pydantic.Optional(v, x => Pydantic.List(x, y => Pydantic.Model(y, "PropertyFilter", PropertyFilter.ModelValidate))),
            () => null);
        f.ThrowIfAny();
        return new SearchFilters
        {
            NodeLabels = nodeLabels.Value,
            EdgeTypes = edgeTypes.Value,
            ValidAt = validAt.Value,
            InvalidAt = invalidAt.Value,
            CreatedAt = createdAt.Value,
            ExpiredAt = expiredAt.Value,
            EdgeUuids = edgeUuids.Value,
            EdgeSourceNodeUuids = edgeSourceNodeUuids.Value,
            EdgeTargetNodeUuids = edgeTargetNodeUuids.Value,
            PropertyFilters = propertyFilters.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("node_labels", NodeLabels),
        ("edge_types", EdgeTypes),
        ("valid_at", ValidAt),
        ("invalid_at", InvalidAt),
        ("created_at", CreatedAt),
        ("expired_at", ExpiredAt),
        ("edge_uuids", EdgeUuids),
        ("edge_source_node_uuids", EdgeSourceNodeUuids),
        ("edge_target_node_uuids", EdgeTargetNodeUuids),
        ("property_filters", PropertyFilters));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

internal static class SearchFiltersModule
{
    internal static string CypherToOpensearchOperator(ComparisonOperator op)
    {
        var mapping = new Dictionary<ComparisonOperator, string>
        {
            [ComparisonOperator.GreaterThan] = "gt",
            [ComparisonOperator.LessThan] = "lt",
            [ComparisonOperator.GreaterThanEqual] = "gte",
            [ComparisonOperator.LessThanEqual] = "lte",
        };
        return Py.Get(mapping, op, (string)Py.EnumValue(op));
    }

    internal static (List<string>, OrderedDictionary<string, object?>) NodeSearchFilterQueryConstructor(
        SearchFilters filters,
        GraphProvider provider)
    {
        List<string> filterQueries = [];
        OrderedDictionary<string, object?> filterParams = [];

        if (filters.NodeLabels is not null)
        {
            // Defense-in-depth for model_construct()/other validation bypasses.
            Helpers.ValidateNodeLabels(filters.NodeLabels);
            var nodeLabels = string.Join("|", filters.NodeLabels);
            var nodeLabelFilter = "n:" + nodeLabels;
            filterQueries.Add(nodeLabelFilter);
        }

        return (filterQueries, filterParams);
    }

    internal static string DateFilterQueryConstructor(string valueName, string paramName, ComparisonOperator @operator)
    {
        var query = "(" + valueName + " ";

        if (@operator == ComparisonOperator.IsNull || @operator == ComparisonOperator.IsNotNull)
        {
            query += (string)Py.EnumValue(@operator) + ")";
        }
        else
        {
            query += (string)Py.EnumValue(@operator) + " " + paramName + ")";
        }

        return query;
    }

    internal static (List<string>, OrderedDictionary<string, object?>) EdgeSearchFilterQueryConstructor(
        SearchFilters filters,
        GraphProvider provider)
    {
        List<string> filterQueries = [];
        OrderedDictionary<string, object?> filterParams = [];

        if (filters.EdgeTypes is not null)
        {
            var edgeTypes = filters.EdgeTypes;
            filterQueries.Add("e.name in $edge_types");
            filterParams["edge_types"] = edgeTypes;
        }

        if (filters.EdgeUuids is not null)
        {
            filterQueries.Add("e.uuid in $edge_uuids");
            filterParams["edge_uuids"] = filters.EdgeUuids;
        }

        // Edge endpoint filters: every edge query binds n as the source and m as
        // the target endpoint. Backends that can push these down pre-rank an
        // endpoint-scoped candidate set instead of scanning the graph.
        if (filters.EdgeSourceNodeUuids is not null)
        {
            filterQueries.Add("n.uuid IN $edge_source_node_uuids");
            filterParams["edge_source_node_uuids"] = filters.EdgeSourceNodeUuids;
        }

        if (filters.EdgeTargetNodeUuids is not null)
        {
            filterQueries.Add("m.uuid IN $edge_target_node_uuids");
            filterParams["edge_target_node_uuids"] = filters.EdgeTargetNodeUuids;
        }

        if (filters.NodeLabels is not null)
        {
            // Defense-in-depth for model_construct()/other validation bypasses.
            Helpers.ValidateNodeLabels(filters.NodeLabels);
            var nodeLabels = string.Join("|", filters.NodeLabels);
            var nodeLabelFilter = "n:" + nodeLabels + " AND m:" + nodeLabels;
            filterQueries.Add(nodeLabelFilter);
        }

        if (filters.ValidAt is not null)
        {
            var validAtFilter = "(";
            foreach (var (i, orList) in filters.ValidAt.Index())
            {
                foreach (var (j, dateFilter) in orList.Index())
                {
                    if (dateFilter.ComparisonOperator is not (ComparisonOperator.IsNull or ComparisonOperator.IsNotNull))
                    {
                        // PORT: upstream defect U10
                        // PORT: the cast to long makes the index of Index() a Python int of the value model (R6) for Py.Str.
                        filterParams["valid_at_" + Py.Str((long)j)] = dateFilter.Date;
                    }
                }

                var andFilters = orList.Index()
                    .Select(item => DateFilterQueryConstructor("e.valid_at", "$valid_at_" + Py.Str((long)item.Index), item.Item.ComparisonOperator))
                    .ToList();
                var andFilterQuery = string.Empty;
                foreach (var (j, andFilter) in andFilters.Index())
                {
                    andFilterQuery += andFilter;
                    if (j != andFilters.Count - 1)
                    {
                        andFilterQuery += " AND ";
                    }
                }

                validAtFilter += andFilterQuery;

                if (i == filters.ValidAt.Count - 1)
                {
                    validAtFilter += ")";
                }
                else
                {
                    validAtFilter += " OR ";
                }
            }

            filterQueries.Add(validAtFilter);
        }

        if (filters.InvalidAt is not null)
        {
            var invalidAtFilter = "(";
            foreach (var (i, orList) in filters.InvalidAt.Index())
            {
                foreach (var (j, dateFilter) in orList.Index())
                {
                    if (dateFilter.ComparisonOperator is not (ComparisonOperator.IsNull or ComparisonOperator.IsNotNull))
                    {
                        // PORT: upstream defect U10
                        filterParams["invalid_at_" + Py.Str((long)j)] = dateFilter.Date;
                    }
                }

                var andFilters = orList.Index()
                    .Select(item => DateFilterQueryConstructor("e.invalid_at", "$invalid_at_" + Py.Str((long)item.Index), item.Item.ComparisonOperator))
                    .ToList();
                var andFilterQuery = string.Empty;
                foreach (var (j, andFilter) in andFilters.Index())
                {
                    andFilterQuery += andFilter;
                    if (j != andFilters.Count - 1)
                    {
                        andFilterQuery += " AND ";
                    }
                }

                invalidAtFilter += andFilterQuery;

                if (i == filters.InvalidAt.Count - 1)
                {
                    invalidAtFilter += ")";
                }
                else
                {
                    invalidAtFilter += " OR ";
                }
            }

            filterQueries.Add(invalidAtFilter);
        }

        if (filters.CreatedAt is not null)
        {
            var createdAtFilter = "(";
            foreach (var (i, orList) in filters.CreatedAt.Index())
            {
                foreach (var (j, dateFilter) in orList.Index())
                {
                    if (dateFilter.ComparisonOperator is not (ComparisonOperator.IsNull or ComparisonOperator.IsNotNull))
                    {
                        // PORT: upstream defect U10
                        filterParams["created_at_" + Py.Str((long)j)] = dateFilter.Date;
                    }
                }

                var andFilters = orList.Index()
                    .Select(item => DateFilterQueryConstructor("e.created_at", "$created_at_" + Py.Str((long)item.Index), item.Item.ComparisonOperator))
                    .ToList();
                var andFilterQuery = string.Empty;
                foreach (var (j, andFilter) in andFilters.Index())
                {
                    andFilterQuery += andFilter;
                    if (j != andFilters.Count - 1)
                    {
                        andFilterQuery += " AND ";
                    }
                }

                createdAtFilter += andFilterQuery;

                if (i == filters.CreatedAt.Count - 1)
                {
                    createdAtFilter += ")";
                }
                else
                {
                    createdAtFilter += " OR ";
                }
            }

            filterQueries.Add(createdAtFilter);
        }

        if (filters.ExpiredAt is not null)
        {
            var expiredAtFilter = "(";
            foreach (var (i, orList) in filters.ExpiredAt.Index())
            {
                foreach (var (j, dateFilter) in orList.Index())
                {
                    if (dateFilter.ComparisonOperator is not (ComparisonOperator.IsNull or ComparisonOperator.IsNotNull))
                    {
                        // PORT: upstream defect U10
                        filterParams["expired_at_" + Py.Str((long)j)] = dateFilter.Date;
                    }
                }

                var andFilters = orList.Index()
                    .Select(item => DateFilterQueryConstructor("e.expired_at", "$expired_at_" + Py.Str((long)item.Index), item.Item.ComparisonOperator))
                    .ToList();
                var andFilterQuery = string.Empty;
                foreach (var (j, andFilter) in andFilters.Index())
                {
                    andFilterQuery += andFilter;
                    if (j != andFilters.Count - 1)
                    {
                        andFilterQuery += " AND ";
                    }
                }

                expiredAtFilter += andFilterQuery;

                if (i == filters.ExpiredAt.Count - 1)
                {
                    expiredAtFilter += ")";
                }
                else
                {
                    expiredAtFilter += " OR ";
                }
            }

            filterQueries.Add(expiredAtFilter);
        }

        return (filterQueries, filterParams);
    }
}
