// Ported from tests/utils/search/test_search_security.py (port/RULES.md, R25). Only the tests whose code under test
// is in graphiti_core/search/search_filters.py, helpers.validate_node_labels,
// driver/neo4j/operations/search_ops.py, graphiti_core/search/search_utils.py, or graphiti_core/search/search.py.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Search;
using Graphiti.Neo4j.Operations;
using Graphiti.PyCompat;

public class TestSearchSecurity
{
    [Fact]
    public void test_search_filters_reject_unsafe_node_labels()
    {
        var error = Assert.Throws<ValidationException>(
            () => new SearchFilters { NodeLabels = ["Entity`) WITH n MATCH (x) DETACH DELETE x //"] }.Validate());
        Assert.Contains("node_labels must start with a letter or underscore", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_node_search_filter_constructor_keeps_valid_label_expression()
    {
        var filters = new SearchFilters { NodeLabels = ["Person", "Organization"] }.Validate();

        var (filterQueries, filterParams) = SearchFiltersModule.NodeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j);

        Assert.Equal(["n:Person|Organization"], filterQueries);
        Assert.Empty(filterParams);
    }

    [Fact]
    public void test_node_search_filter_constructor_rejects_unsafe_labels_bypassing_pydantic()
    {
        // model_construct() skips validation: the record is built without Validate().
        var filters = new SearchFilters { NodeLabels = ["Entity`) DETACH DELETE x //"] };

        var error = Assert.Throws<NodeLabelValidationException>(
            () => SearchFiltersModule.NodeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j));
        Assert.Contains("node_labels must start with a letter or underscore", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_edge_search_filter_constructor_rejects_unsafe_labels_bypassing_pydantic()
    {
        // model_construct() skips validation: the record is built without Validate().
        var filters = new SearchFilters { NodeLabels = ["Entity`) DETACH DELETE x //"] };

        var error = Assert.Throws<NodeLabelValidationException>(
            () => SearchFiltersModule.EdgeSearchFilterQueryConstructor(filters, GraphProvider.Neo4j));
        Assert.Contains("node_labels must start with a letter or underscore", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void test_build_neo4j_fulltext_query_rejects_invalid_group_ids()
    {
        var error = Assert.Throws<GroupIdValidationException>(() => SearchOps.BuildNeo4jFulltextQuery("test", ["bad\"group"]));
        Assert.Contains("must contain only alphanumeric", error.Message, StringComparison.Ordinal);
    }

    // Ported with graphiti_core/search/search_utils.py. SimpleNamespace(provider=..., fulltext_syntax='') is a driver
    // that answers no query.
    [Fact]
    public void test_fulltext_query_rejects_invalid_group_ids()
    {
        var driver = new NamespaceDriver { Provider = GraphProvider.Neo4j, FulltextSyntax = string.Empty };

        var error = Assert.Throws<GroupIdValidationException>(() => SearchUtils.FulltextQuery("test", ["bad\"group"], driver));
        Assert.Contains("must contain only alphanumeric", error.Message, StringComparison.Ordinal);
    }

    // Ported with graphiti_core/search/search.py. `search` validates the group ids before it reads any client, so the
    // SimpleNamespace clients are null here. The embedder, the cross-encoder, and the tracer are never read.
    [Fact(Timeout = 5000)]
    public async Task test_shared_search_rejects_invalid_group_ids()
    {
        var clients = new GraphitiClients
        {
            Driver = new NamespaceDriver(),
            LlmClient = null!,
            Embedder = null!,
            CrossEncoder = null!,
            Tracer = null!,
        };

        var error = await Assert.ThrowsAsync<GroupIdValidationException>(() => LoopGuard.RunAsync(() => SearchModule.SearchAsync(
            clients,
            query: "test",
            groupIds: ["bad\"group"],
            config: new SearchConfig(),
            searchFilter: new SearchFilters(),
            cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Contains("must contain only alphanumeric", error.Message, StringComparison.Ordinal);
    }

    private sealed class NamespaceDriver : GraphDriver
    {
        internal override string PythonName => "SimpleNamespace";

        internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
