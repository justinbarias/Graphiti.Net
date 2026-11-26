// Ported from tests/test_episode_group_isolation.py (port/RULES.md, R25): the test whose code under test is edge_db_queries.py.
// The other tests of that file need the Graphiti class, nodes, edges, or the Neo4j operations, which are later units.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Models.Edges;

public class TestEpisodeGroupIsolation
{
    [Fact]
    public void test_saga_edge_queries_bind_both_endpoints()
    {
        Assert.Contains("Saga {uuid: $saga_uuid, group_id: $group_id}", EdgeDbQueries.HasEpisodeEdgeSave, StringComparison.Ordinal);
        Assert.Contains("Episodic {uuid: $episode_uuid, group_id: $group_id}", EdgeDbQueries.HasEpisodeEdgeSave, StringComparison.Ordinal);
        Assert.Contains("Episodic {uuid: $source_episode_uuid, group_id: $group_id}", EdgeDbQueries.NextEpisodeEdgeSave, StringComparison.Ordinal);
        Assert.Contains("Episodic {uuid: $target_episode_uuid, group_id: $group_id}", EdgeDbQueries.NextEpisodeEdgeSave, StringComparison.Ordinal);
    }
}
