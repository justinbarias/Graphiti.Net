// Ported from tests/driver/test_falkordb_driver.py (port/RULES.md, R25): the test whose code under test is graph_queries.py.
// The skip condition of Python (the falkordb package is not installed) does not apply: the test calls only
// get_fulltext_indices. The other tests of that file need FalkorDriver and its operations, which are out of the MVP (milestone M5).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;

public class TestFalkordbDriver
{
    [Fact]
    public void test_falkordb_fulltext_indices_use_ddl_form()
    {
        var queries = GraphQueries.GetFulltextIndices(GraphProvider.Falkordb);

        Assert.All(queries, query => Assert.DoesNotContain("db.idx.fulltext.createNodeIndex", query, StringComparison.Ordinal));
        Assert.All(queries, query => Assert.StartsWith("CREATE FULLTEXT INDEX", query.Trim(), StringComparison.Ordinal));
        Assert.All(queries.Take(3), query => Assert.Contains("OPTIONS {stopwords:", query, StringComparison.Ordinal));
    }
}
