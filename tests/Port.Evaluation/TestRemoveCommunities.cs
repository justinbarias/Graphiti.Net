// Ported from tests/utils/maintenance/test_remove_communities.py (port/RULES.md, R25). The code under test is
// remove_communities in graphiti_core/utils/maintenance/community_operations.py.
//
// `driver = MagicMock()` with `execute_query = AsyncMock()` is a fake driver at the network boundary: it records each
// query and its parameters, and it gives no rows. `graph_operations_interface` and `graph_ops` are None, or a fake that
// records the call of remove_communities (`AsyncMock`, `assert_awaited_once_with`). Each fake yields once and then
// records its call (R25).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Operations;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;

public class TestRemoveCommunities
{
    [Fact(Timeout = 5000)]
    public async Task test_remove_communities_unscoped_deletes_all()
    {
        var driver = new RecordingDriver();

        await LoopGuard.RunAsync(() => CommunityOperations.RemoveCommunitiesAsync(driver, cancellationToken: TestContext.Current.CancellationToken));

        var (query, kwargs) = Assert.Single(driver.Queries);
        Assert.Contains("MATCH (c:Community)", query, StringComparison.Ordinal);
        Assert.DoesNotContain("WHERE", query, StringComparison.Ordinal);
        Assert.DoesNotContain("group_ids", kwargs.Keys);
    }

    [Fact(Timeout = 5000)]
    public async Task test_remove_communities_scoped_deletes_only_selected_groups()
    {
        var driver = new RecordingDriver();

        await LoopGuard.RunAsync(() => CommunityOperations.RemoveCommunitiesAsync(driver, groupIds: ["group_a", "group_b"], cancellationToken: TestContext.Current.CancellationToken));

        var (query, kwargs) = Assert.Single(driver.Queries);
        Assert.Contains("WHERE c.group_id IN $group_ids", query, StringComparison.Ordinal);
        Assert.Equal(["group_a", "group_b"], (List<string>)kwargs["group_ids"]!);
    }

    [Fact(Timeout = 5000)]
    public async Task test_remove_communities_empty_group_ids_deletes_all()
    {
        // group_ids=[] means "no scoping requested" (matches build_communities'
        // None/blank semantics), not "delete nothing".
        var driver = new RecordingDriver();

        await LoopGuard.RunAsync(() => CommunityOperations.RemoveCommunitiesAsync(driver, groupIds: [], cancellationToken: TestContext.Current.CancellationToken));

        var (query, _) = Assert.Single(driver.Queries);
        Assert.DoesNotContain("WHERE", query, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task test_remove_communities_scoped_via_graph_operations_interface()
    {
        var ops = new RecordingOps();
        var driver = new RecordingDriver(ops: ops);

        await LoopGuard.RunAsync(() => CommunityOperations.RemoveCommunitiesAsync(driver, groupIds: ["group_a"], cancellationToken: TestContext.Current.CancellationToken));

        // assert_awaited_once_with(driver, group_ids=['group_a'])
        var (calledDriver, groupIds) = Assert.Single(ops.Calls);
        Assert.Same(driver, calledDriver);
        Assert.Equal(["group_a"], groupIds);
        Assert.Empty(driver.Queries);
    }

    [Fact(Timeout = 5000)]
    public async Task test_remove_communities_scoped_via_graph_ops()
    {
        var graphOps = new RecordingGraphOps();
        var driver = new RecordingDriver(graphOps: graphOps);

        await LoopGuard.RunAsync(() => CommunityOperations.RemoveCommunitiesAsync(driver, groupIds: ["group_a"], cancellationToken: TestContext.Current.CancellationToken));

        // assert_awaited_once_with(driver, group_ids=['group_a'])
        var (calledExecutor, groupIds) = Assert.Single(graphOps.Calls);
        Assert.Same(driver, calledExecutor);
        Assert.Equal(["group_a"], groupIds);
        Assert.Empty(driver.Queries);
    }

    // The MagicMock driver of the Python tests: `execute_query` is an AsyncMock with no rows, and the two interfaces are
    // None or a fake.
    private sealed class RecordingDriver : GraphDriver
    {
        private readonly GraphMaintenanceOperations? graphOps;

        internal RecordingDriver(GraphOperationsInterface? ops = null, GraphMaintenanceOperations? graphOps = null)
        {
            Provider = GraphProvider.Neo4j;
            GraphOperationsInterface = ops;
            this.graphOps = graphOps;
        }

        internal List<(string Query, OrderedDictionary<string, object?> Parameters)> Queries { get; } = [];

        internal override string PythonName => "MagicMock";

        internal override GraphMaintenanceOperations? GraphOps => graphOps;

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            Queries.Add((cypherQuery, parameters));
            return ([], null, null);
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    private sealed record RecordingOps : GraphOperationsInterface
    {
        internal List<(GraphDriver Driver, List<string>? GroupIds)> Calls { get; } = [];

        internal override async Task RemoveCommunitiesAsync(GraphDriver driver, List<string>? groupIds = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            Calls.Add((driver, groupIds));
        }
    }

    private sealed class RecordingGraphOps : GraphMaintenanceOperations
    {
        internal List<(QueryExecutor Executor, List<string>? GroupIds)> Calls { get; } = [];

        internal override async Task RemoveCommunitiesAsync(QueryExecutor executor, List<string>? groupIds = null, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            Calls.Add((executor, groupIds));
        }

        internal override Task ClearDataAsync(QueryExecutor executor, List<string>? groupIds = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(QueryExecutor executor, bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task DeleteAllIndexesAsync(QueryExecutor executor, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task<List<List<EntityNode>>> GetCommunityClustersAsync(QueryExecutor executor, List<string>? groupIds = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task DetermineEntityCommunityAsync(QueryExecutor executor, EntityNode entity, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task<List<EntityNode>> GetMentionedNodesAsync(QueryExecutor executor, List<EpisodicNode> episodes, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task<List<CommunityNode>> GetCommunitiesByNodesAsync(QueryExecutor executor, List<EntityNode> nodes, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }
}
