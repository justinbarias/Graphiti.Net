// Ported from tests/test_edge_db_queries.py (port/RULES.md, R25). The pytest parametrization over GraphProvider is a loop.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Driver;
using Graphiti.Core.Models.Edges;

public class TestEdgeDbQueries
{
    [Fact]
    public void test_neo4j_uses_start_end_node()
    {
        var query = EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Neo4j);
        Assert.Contains("startNode(e).uuid AS source_node_uuid", query, StringComparison.Ordinal);
        Assert.Contains("endNode(e).uuid AS target_node_uuid", query, StringComparison.Ordinal);
        Assert.DoesNotContain("n.uuid AS source_node_uuid", query, StringComparison.Ordinal);
        Assert.DoesNotContain("m.uuid AS target_node_uuid", query, StringComparison.Ordinal);
    }

    [Fact]
    public void test_falkordb_uses_start_end_node()
    {
        var query = EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Falkordb);
        Assert.Contains("startNode(e).uuid AS source_node_uuid", query, StringComparison.Ordinal);
        Assert.Contains("endNode(e).uuid AS target_node_uuid", query, StringComparison.Ordinal);
    }

    [Fact]
    public void test_neptune_uses_start_end_node_with_split_episodes()
    {
        var query = EdgeDbQueries.GetEntityEdgeReturnQuery(GraphProvider.Neptune);
        Assert.Contains("startNode(e).uuid AS source_node_uuid", query, StringComparison.Ordinal);
        Assert.Contains("endNode(e).uuid AS target_node_uuid", query, StringComparison.Ordinal);
        Assert.Contains("split(e.episodes, ',') AS episodes", query, StringComparison.Ordinal);
    }

    [Fact]
    public void test_default_branch_returns_neo4j_or_falkordb_attributes()
    {
        // Both NEO4J and FALKORDB use `properties(e)`.
        foreach (var provider in new[] { GraphProvider.Neo4j, GraphProvider.Falkordb })
        {
            var query = EdgeDbQueries.GetEntityEdgeReturnQuery(provider);
            Assert.Contains("properties(e) AS attributes", query, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void test_entity_edge_return_query_selects_reference_time()
    {
        // reference_time is saved by EntityEdge.save() on every provider and the record parser reads it from the
        // top-level record. The return query must select it alongside the other temporal fields.
        foreach (var provider in Enum.GetValues<GraphProvider>())
        {
            var query = EdgeDbQueries.GetEntityEdgeReturnQuery(provider);

            Assert.Contains("reference_time AS reference_time", query, StringComparison.Ordinal);
            foreach (var field in new[]
            {
                "created_at AS created_at",
                "expired_at AS expired_at",
                "valid_at AS valid_at",
                "invalid_at AS invalid_at",
            })
            {
                Assert.Contains(field, query, StringComparison.Ordinal);
            }
        }
    }
}
