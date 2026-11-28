// Ported from tests/ingestion/test_update_queries.py (port/RULES.md, R25). Packet P8c of plan 0004, layer 8, and packet
// P9g of layer 9. The code under test is graphiti_core/utils/bulk_utils.py, and graphiti_core/ingestion/phases.py for the
// three admission tests.
//
// Ported elsewhere: test_edge_update_query_has_no_merge and test_node_update_query_has_no_merge
// (TestUpdateQueriesQueryBuilders.cs), and test_neo4j_write_gets_the_limit_as_transaction_timeout (TestBulkUtils.cs).
// Not ported: test_falkordb_write_gets_the_limit_as_query_timeout needs FalkorDriverSession, and the FalkorDB driver is
// out of the MVP (decision D10).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Ingestion;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.IngestionFixtures;

public class TestUpdateQueries
{
    // `GROUP`. The record Group of the in-memory store has the same name.
    private const string Group = IngestionFixtures.Group;

    /// <summary>An interface that has the bulk saves of today and no update operation.</summary>
    private record SaveOnlyInterface : GraphOperationsInterface
    {
        internal override Task EpisodicNodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        internal override Task NodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        internal override Task EpisodicEdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> episodicEdges, long batchSize = 100, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        internal override Task EdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>An interface that has the bulk saves of today and the two update operations.</summary>
    private sealed record FullInterface : SaveOnlyInterface
    {
        internal List<(string, List<string>)> Calls { get; } = [];

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

    // A session whose write sleeps for five seconds, as SlowSession of upstream does (`asyncio.sleep(5)`).
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
            Asyncio.SleepAsync(5, PortServices.Time, cancellationToken);

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, double timeout, CancellationToken cancellationToken = default) =>
            Asyncio.SleepAsync(5, PortServices.Time, cancellationToken);
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

    // PORT: the limit of 0.05 seconds and the sleep of the session run on the virtual clock (VirtualLoop in
    // IngestionFixtures.cs), so a busy scheduler thread in a parallel run does not change which one ends first.
    [Fact(Timeout = 10000)]
    public async Task test_write_is_cancelled_at_the_limit_on_a_driver_with_no_server_limit()
    {
        var session = new SlowSession();
        var driver = new SlowDriver(session) { Provider = GraphProvider.Neptune };

        await VirtualLoop.RunAsync(async _ =>
            await Assert.ThrowsAsync<TimeoutException>(
                () => BulkUtils.ExecuteWriteWithTimeoutAsync(driver, 0.05, (tx, token) => Task.CompletedTask, TestContext.Current.CancellationToken)))
            .WaitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, session.Closed);
    }

    // PORT: `GraphOperationsInterface()`. The class is abstract in the port, so the test makes a subclass that overrides
    // no operation.
    private sealed record EmptyInterface : GraphOperationsInterface;

    private static (Harness H, Func<Task<AdmittedUnit>> Admission) AdmitWithInterface(GraphOperationsInterface @interface, bool bulk)
    {
        var h = new Harness();
        h.Driver.GraphOperationsInterface = @interface;
        if (bulk)
        {
            return (h, () => Phases.AdmitBulkAsync(
                h.Graphiti,
                h.Store,
                h.Config,
                unitId: "u1",
                groupId: Group,
                bulkEpisodes:
                [
                    new RawEpisode { Name = "episode", Content = "content", SourceDescription = "test", Source = EpisodeType.Text, ReferenceTime = T0 },
                ],
                cancellationToken: TestContext.Current.CancellationToken));
        }

        return (h, () => Phases.AdmitEpisodeAsync(
            h.Graphiti,
            h.Store,
            h.Config,
            unitId: "u1",
            groupId: Group,
            name: "episode",
            episodeBody: "content",
            sourceDescription: "test",
            referenceTime: T0,
            cancellationToken: TestContext.Current.CancellationToken));
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task test_admission_rejects_an_interface_with_no_update_operation(bool bulk)
    {
        var (h, admission) = AdmitWithInterface(new SaveOnlyInterface(), bulk);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => LoopGuard.RunAsync(admission).WaitAsync(TestContext.Current.CancellationToken));
        Assert.Matches("edge_update_bulk, node_update_bulk", error.Message);

        // The error is before the admission: no unit and no graph write.
        Assert.Empty(h.Store.Groups);
        Assert.Empty(h.Graph.Log);
        Assert.Empty(h.Graph.Episodes);
    }

    [Fact(Timeout = 10000)]
    public async Task test_admission_names_each_operation_that_an_interface_does_not_have()
    {
        var (_, admission) = AdmitWithInterface(new EmptyInterface(), false);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => LoopGuard.RunAsync(admission).WaitAsync(TestContext.Current.CancellationToken));

        foreach (var name in new[] { "edge_save_bulk", "edge_update_bulk", "episodic_edge_save_bulk", "episodic_node_save_bulk", "node_save_bulk", "node_update_bulk" })
        {
            Assert.Contains(name, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task test_admission_accepts_an_interface_with_each_operation()
    {
        var (_, admission) = AdmitWithInterface(new FullInterface(), false);

        var unit = await LoopGuard.RunAsync(admission).WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, unit.Seq);
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
