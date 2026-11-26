// Evaluation test for graphiti_core/search/search_filters.py (port/RULES.md, R7): the field validator of
// node_labels reports its error with the errors of the other fields, in field order, as pydantic does.
namespace Graphiti.Port.Evaluation;

using System.Globalization;
using Graphiti.Core.Search;
using Graphiti.PyCompat;

public class SearchFiltersValidationTests
{
    private const string LabelError =
        "Value error, node_labels must start with a letter or underscore and contain only alphanumeric characters or underscores: \"a b\"";

    public static TheoryData<string, OrderedDictionary<string, object?>, (string Loc, string Type, string Msg)[]> Cases() => new()
    {
        {
            // SearchFilters(node_labels=['a b'], edge_types=[5]).errors(), pydantic at the pinned upstream commit
            "edge_types",
            new OrderedDictionary<string, object?> { ["node_labels"] = new List<object?> { "a b" }, ["edge_types"] = new List<object?> { 5L } },
            [
                ("node_labels", "value_error", LabelError),
                ("edge_types.0", "string_type", "Input should be a valid string"),
            ]
        },
        {
            // SearchFilters(node_labels=['a b'], valid_at=[[{'comparison_operator': 'LIKE'}]]).errors()
            "valid_at",
            new OrderedDictionary<string, object?>
            {
                ["node_labels"] = new List<object?> { "a b" },
                ["valid_at"] = new List<object?> { new List<object?> { new OrderedDictionary<string, object?> { ["comparison_operator"] = "LIKE" } } },
            },
            [
                ("node_labels", "value_error", LabelError),
                ("valid_at.0.0.comparison_operator", "enum", "Input should be '=', '<>', '>', '<', '>=', '<=', 'IS NULL' or 'IS NOT NULL'"),
            ]
        },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_validator_error_joins_the_field_errors_as_in_pydantic(
        string name, OrderedDictionary<string, object?> data, (string Loc, string Type, string Msg)[] expected)
    {
        _ = name;
        var error = Assert.Throws<ValidationException>(() => SearchFilters.ModelValidate(data));

        Assert.Equal("SearchFilters", error.Title);
        Assert.Equal(expected.Length, error.Errors.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            // The location as pydantic prints it in str(error), for example edge_types.0.
            Assert.Equal(expected[i].Loc, string.Join(".", error.Errors[i].Loc.Select(part => Convert.ToString(part, CultureInfo.InvariantCulture))));
            Assert.Equal(expected[i].Type, error.Errors[i].Type);
            Assert.Equal(expected[i].Msg, error.Errors[i].Msg);
        }
    }

    [Fact]
    public void A_typed_construction_reports_the_validator_error_under_the_field()
    {
        // SearchFilters(node_labels=['a b']).errors() gives one error with the location ('node_labels',).
        var error = Assert.Throws<ValidationException>(() => new SearchFilters { NodeLabels = ["a b"] }.Validate());

        var only = Assert.Single(error.Errors);
        Assert.Equal(["node_labels"], only.Loc);
        Assert.Equal("value_error", only.Type);
        Assert.Equal(LabelError, only.Msg);
    }
}
