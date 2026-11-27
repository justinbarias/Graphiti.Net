// Ported from tests/utils/search/test_edge_bfs_query_shape.py (port/RULES.md, R25).
// Query-shape regression tests for edge_bfs_search (no live database). A recording driver captures the emitted Cypher,
// so no database connection is required.
// test_neptune_operations_bfs_search_returns_reference_time is not here: its code under test is
// driver/neptune/operations/search_ops.py, which is out of the MVP.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Driver;
using Graphiti.Core.Search;

public class TestEdgeBfsQueryShape
{
    [Fact(Timeout = 5000)]
    public async Task test_edge_bfs_search_consumes_path_relationships_directly()
    {
        var driver = new RecordingDriver();

        await SearchUtils.EdgeBfsSearchAsync(driver, ["origin-uuid"], 2, new SearchFilters(), groupIds: ["group-a"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains("WITH rel AS e, startNode(rel) AS n, endNode(rel) AS m", driver.CypherQuery, StringComparison.Ordinal);
        // The path traverses RELATES_TO|MENTIONS; the old re-MATCH filtered MENTIONS
        // implicitly, so the rewrite must keep an explicit type guard.
        Assert.Contains("WHERE type(e) = 'RELATES_TO'", driver.CypherQuery, StringComparison.Ordinal);
        // The expensive per-row re-MATCH must be gone.
        Assert.DoesNotContain("uuid: rel.uuid", driver.CypherQuery, StringComparison.Ordinal);
        // Downstream filters still see the relationship. The return helper derives
        // direction from it so undirected matching cannot swap edge endpoints.
        Assert.Contains("e.group_id IN $group_ids", driver.CypherQuery, StringComparison.Ordinal);
        Assert.Contains("startNode(e).uuid AS source_node_uuid", driver.CypherQuery, StringComparison.Ordinal);
        Assert.Contains("endNode(e).uuid AS target_node_uuid", driver.CypherQuery, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fulltext variant of this rewrite belongs to #1500; this change is
    /// scoped to the BFS query and must leave the fulltext query alone.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task test_edge_fulltext_search_not_rewritten_by_bfs_fix()
    {
        var driver = new RecordingDriver();

        await SearchUtils.EdgeFulltextSearchAsync(driver, "api test system", new SearchFilters(), groupIds: ["group-a"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("WITH rel AS e, startNode(rel) AS n, endNode(rel) AS m", driver.CypherQuery, StringComparison.Ordinal);
        Assert.DoesNotContain("type(e) = 'RELATES_TO'", driver.CypherQuery, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task test_neptune_generic_edge_searches_return_reference_time()
    {
        var driver = new RecordingDriver { Provider = GraphProvider.Neptune };

        await SearchUtils.EdgeBfsSearchAsync(driver, ["origin-uuid"], 2, new SearchFilters(), groupIds: ["group-a"], cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("e.reference_time AS reference_time", driver.CypherQuery, StringComparison.Ordinal);

        // TODO(port): the second half calls edge_fulltext_search on Neptune, which reaches driver.run_aoss_query. The
        // port has no rule for that member and throws there (report of plan 0004 packet P6b), so the test asserts the
        // throw in place of the query text.
        await Assert.ThrowsAsync<NotSupportedException>(
            () => SearchUtils.EdgeFulltextSearchAsync(driver, "api test system", new SearchFilters(), groupIds: ["group-a"], cancellationToken: TestContext.Current.CancellationToken));
    }

    /// <summary>Captures the Cypher and params a search function emits, returning no rows.</summary>
    private sealed class RecordingDriver : GraphDriver
    {
        internal RecordingDriver()
        {
            Provider = GraphProvider.Neo4j;
            SearchInterface = null;
            FulltextSyntax = string.Empty;
        }

        internal string CypherQuery { get; private set; } = string.Empty;

        internal OrderedDictionary<string, object?> Params { get; private set; } = [];

        internal override string PythonName => "RecordingDriver";

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            // A fake that returns a task yields first, then records the call (R25).
            await Task.Yield();
            CypherQuery = cypherQuery;
            Params = parameters;
            return ([], null, null);
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
