// Ported from tests/ingestion/test_update_queries.py (port/RULES.md, R25): the two tests whose code under test is in
// models/edges/edge_db_queries.py and models/nodes/node_db_queries.py. The other tests of that file wait for
// utils/bulk_utils.py, ingestion/phases.py, and driver/falkordb_driver.py.
// Not ported here: test_bulk_save_does_not_persist_labels_property (tests/driver/test_falkordb_driver.py) and
// test_bulk_save_update_and_label_hygiene (tests/driver/test_relate_driver.py) need a FalkorDB or a Relate server,
// and the FalkorDB and Relate drivers are out of the MVP (decision D10).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Driver;
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Models.Nodes;
using Graphiti.PyCompat;

public class TestUpdateQueriesQueryBuilders
{
    private static List<OrderedDictionary<string, object?>> Nodes() =>
    [
        new() { ["uuid"] = "n1", ["name"] = "Ana", ["labels"] = new List<object?> { "Entity", "Person" } },
    ];

    private static string QueryText(object? queries)
    {
        if (queries is string text)
        {
            return text;
        }

        return string.Join("\n", ((List<object?>)queries!).Select(q => q is PyTuple tuple ? (string)tuple[0]! : (string)q!));
    }

    [Fact]
    public void test_edge_update_query_has_no_merge()
    {
        foreach (var provider in Enum.GetValues<GraphProvider>())
        {
            var save = EdgeDbQueries.GetEntityEdgeSaveBulkQuery(provider);
            var update = EdgeDbQueries.GetEntityEdgeUpdateBulkQuery(provider);
            Assert.Contains("MERGE", save, StringComparison.Ordinal);
            Assert.DoesNotContain("MERGE", update, StringComparison.Ordinal);

            // The update query is the save query with one word changed.
            Assert.Equal(save.Replace("MERGE (source)-[", "MATCH (source)-[", StringComparison.Ordinal), update);
        }
    }

    [Fact]
    public void test_node_update_query_has_no_merge()
    {
        foreach (var provider in Enum.GetValues<GraphProvider>())
        {
            var save = NodeDbQueries.GetEntityNodeSaveBulkQuery(provider, Nodes());
            var update = NodeDbQueries.GetEntityNodeUpdateBulkQuery(provider, Nodes());
            Assert.Equal(save!.GetType(), update!.GetType());
            Assert.Contains("MERGE", QueryText(save), StringComparison.Ordinal);
            Assert.DoesNotContain("MERGE", QueryText(update), StringComparison.Ordinal);
            Assert.Equal(
                QueryText(save).Replace("MERGE (n:Entity {uuid: ", "MATCH (n:Entity {uuid: ", StringComparison.Ordinal),
                QueryText(update));
        }
    }
}
