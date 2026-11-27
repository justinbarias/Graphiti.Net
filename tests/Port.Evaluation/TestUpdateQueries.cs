// Ported from tests/ingestion/test_update_queries.py (port/RULES.md, R25). Packet P8c of plan 0004, layer 8. The code
// under test of these three tests is graphiti_core/utils/bulk_utils.py.
//
// Ported elsewhere: test_edge_update_query_has_no_merge and test_node_update_query_has_no_merge
// (TestUpdateQueriesQueryBuilders.cs), and test_neo4j_write_gets_the_limit_as_transaction_timeout (TestBulkUtils.cs).
// Not ported: test_falkordb_write_gets_the_limit_as_query_timeout needs FalkorDriverSession, and the FalkorDB driver is
// out of the MVP (decision D10).
// Waits for layer 9: test_admission_rejects_an_interface_with_no_update_operation (episode, bulk),
// test_admission_names_each_operation_that_an_interface_does_not_have, and
// test_admission_accepts_an_interface_with_each_operation call admit_episode or admit_bulk, which call
// graphiti._resolve_request_scope first. The sync cases of _check_driver in values/phases.json cover the error text of
// each interface (none, partial, all, one missing).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Utils;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.IngestionFixtures;

public class TestUpdateQueries
{
    /// <summary>An interface that has the bulk saves of today and the two update operations.</summary>
    private sealed record FullInterface : GraphOperationsInterface
    {
        internal List<(string, List<string>)> Calls { get; } = [];

        internal override Task EpisodicNodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        internal override Task NodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        internal override Task EpisodicEdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> episodicEdges, long batchSize = 100, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        internal override Task EdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        internal override Task NodeUpdateBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default)
        {
            Calls.Add(("node_update_bulk", [.. nodes.Select(node => (string)node["uuid"]!)]));
            return Task.CompletedTask;
        }

        internal override Task EdgeUpdateBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default)
        {
            Calls.Add(("edge_update_bulk", [.. edges.Select(edge => (string)edge["uuid"]!)]));
            return Task.CompletedTask;
        }
    }

    // A session whose write sleeps for five seconds, as SlowSession of upstream does.
    private sealed class SlowSession : GraphDriverSession
    {
        internal int Closed { get; private set; }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        internal override Task CloseAsync(CancellationToken cancellationToken = default)
        {
            Closed++;
            return Task.CompletedTask;
        }

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, CancellationToken cancellationToken = default) =>
            Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, double timeout, CancellationToken cancellationToken = default) =>
            Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
    }

    private sealed class SlowDriver(SlowSession session) : GraphDriver
    {
        internal override string PythonName => "SimpleNamespace";

        internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override GraphDriverSession Session(string? database = null) => session;

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // PORT: upstream replaces bulk_utils.execute_write_with_timeout with an AsyncMock and asserts that it was not awaited
    // (open item O10). The fake session of the recording replaces it: a write through execute_write_with_timeout on
    // Neo4j would reach the session write with a timeout, and the test asserts the session write with no timeout.
    // `args[0] is add_nodes_and_edges_bulk_tx` has no form: a C# work function is a lambda (R9).
    [Fact(Timeout = 5000)]
    public async Task test_bulk_write_with_no_timeout_uses_the_session_call_of_today()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new PhasesRun(new() { ["fn"] = "add_nodes_and_edges_bulk" }, source, -1);

        await LoopGuard.RunAsync(() => BulkUtils.AddNodesAndEdgesBulkAsync(run.Driver, [], [], [], [], run.Clients.Embedder, cancellationToken: source.Token));

        var writes = run.Events.Select(PhasesValues.Dict).Where(e => (string)e["kind"]! == "execute_write").ToList();
        Assert.Single(writes);
        Assert.Null(writes[0]["timeout"]);
        Assert.Single(run.Events.Select(PhasesValues.Dict), e => (string)e["kind"]! == "close");
    }

    [Fact(Timeout = 10000)]
    public async Task test_write_is_cancelled_at_the_limit_on_a_driver_with_no_server_limit()
    {
        var session = new SlowSession();
        var driver = new SlowDriver(session) { Provider = GraphProvider.Neptune };

        await Assert.ThrowsAsync<TimeoutException>(() => LoopGuard.RunAsync(
            () => BulkUtils.ExecuteWriteWithTimeoutAsync(driver, 0.05, (tx, token) => Task.CompletedTask, TestContext.Current.CancellationToken)));
        Assert.Equal(1, session.Closed);
    }

    [Fact(Timeout = 5000)]
    public async Task test_update_functions_call_the_interface_of_the_driver()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new PhasesRun(new() { ["fn"] = "update" }, source, -1);
        var @interface = new FullInterface();
        run.Driver.GraphOperationsInterface = @interface;
        var node = new EntityNode { Uuid = "n1", Name = "Acme", GroupId = Group, NameEmbedding = [0.1, 0.2] }.Validate();
        var edge = new EntityEdge
        {
            Uuid = "e1",
            GroupId = Group,
            SourceNodeUuid = "n1",
            TargetNodeUuid = "n2",
            Name = "RELATES",
            Fact = "fact",
            CreatedAt = T0,
            FactEmbedding = [0.1, 0.2],
        }.Validate();
        var tx = new PhasesRun.FakeTx(run);

        await LoopGuard.RunAsync(async () =>
        {
            await BulkUtils.UpdateEntityNodesTxAsync(tx, [node], run.Clients.Embedder, run.Driver, source.Token);
            await BulkUtils.UpdateEntityEdgesTxAsync(tx, [edge], run.Clients.Embedder, run.Driver, source.Token);
        });

        Assert.Equal(2, @interface.Calls.Count);
        Assert.Equal("node_update_bulk", @interface.Calls[0].Item1);
        Assert.Equal(["n1"], @interface.Calls[0].Item2);
        Assert.Equal("edge_update_bulk", @interface.Calls[1].Item1);
        Assert.Equal(["e1"], @interface.Calls[1].Item2);
        Assert.DoesNotContain(run.Events.Select(PhasesValues.Dict), e => (string)e["kind"]! == "tx_run");
    }
}
