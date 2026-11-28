// Ported from tests/ingestion/test_failure_rules.py (port/RULES.md, R25). The code under test is
// graphiti_core/ingestion/phases.py. Packet P8c of plan 0004, layer 8, and packet P9g of layer 9.
//
// Tests for the failure rules and for the Enrich phase of concurrent ingestion.
//
// The Harness runs the real write functions on HarnessDriver (IngestionFixtures.cs). A test that replaces
// `phases.add_nodes_and_edges_bulk` with a function that calls the write of FakeGraph sets FakeGraph.BulkWrite. A test
// that replaces a method of the store sets the hook of LoggingStore. Each form that differs from the upstream patch has a
// `// PORT:` comment, and p9g/oracle_plugin.py with p9g/test_p9g_variants.py (packet P9g report) shows each form on the
// Python side.
//
// These tests keep a marker (open item O10), each with the recorded case that covers the same behavior
// (values/phases.json, keys `async_cases` and `graphiti_cases`):
// TODO(port): O10 test_second_analysis_of_a_unit_does_not_fail_the_unit. It replaces phases.extract_nodes,
//   phases.resolve_extracted_nodes, and graphiti._extract_and_resolve_edges. Covered: the graphiti case
//   analyze_episode_stale.
// TODO(port): O10 test_enrichment_of_one_node_runs_in_sequence_order. It calls Harness.enrich: the replaced
//   extract_attributes_from_nodes writes `+<episode uuid>` into each summary. Covered: the graphiti case
//   enrich_waits_for_an_earlier_unit.
// TODO(port): O10 test_enrichment_applies_the_patch_to_the_current_node. It calls Harness.enrich. Covered: the graphiti
//   case commit_and_enrich.
// TODO(port): O10 test_enrichment_failure_keeps_the_node_values_and_does_not_raise. It gives the replaced function an
//   error. Covered: _hydrate_nodes failure, and the graphiti case enrich_node_with_no_embedding.
// TODO(port): O10 test_enrichment_hook_selects_the_nodes_to_write. It calls Harness.enrich. Covered: the graphiti case
//   enrich_hook_drops_the_nodes.
// TODO(port): O10 test_enrichment_does_not_create_again_a_node_that_a_writer_deleted. The replaced function deletes a
//   node. Covered: none.
// TODO(port): O10 test_enrichment_write_error_releases_the_enrichment_order. It calls Harness.enrich. Covered: the
//   graphiti cases enrich_write_fails and enrich_release_fails.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.IngestionFixtures;

public class TestFailureRules
{
    // `GROUP`. The record Group of the in-memory store has the same name.
    private const string Group = IngestionFixtures.Group;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // Each test runs on one loop (R25), and it ends when xUnit cancels its token.
    private static Task Loop(Func<Task> body, CancellationToken cancellationToken) => LoopGuard.RunAsync(body).WaitAsync(cancellationToken);

    private static string Status(Harness h, long seq) => h.Store.Groups[Group].Units[seq].Status;

    // --- failures before the final change set is stored -------------------------------

    [Fact(Timeout = 10000)]
    public async Task test_analysis_failure_releases_the_position()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            // PORT: the test replaces phases.extract_nodes with `AsyncMock(side_effect=RuntimeError('model'))`. The model
            // of the Harness raises that error at its first call, in extract_nodes (test_p9g_variants.py).
            h.Llm.Error = new InvalidOperationException("model");
            var unit1 = await h.AdmitAsync("u1");

            await Assert.ThrowsAsync<InvalidOperationException>(() => Phases.AnalyzeEpisodeAsync(h.Graphiti, h.Store, h.Config, unit1, cancellationToken: Token));

            Assert.Equal(StateStore.StatusFailed, Status(h, unit1.Seq));
            Assert.Empty(h.Graph.Nodes);
            // A later unit does not wait for the failed unit.
            h.Llm.Error = null;
            var (_, result2) = await h.RunAsync("u2", createNodes: [MakeNode("a2", "Acme")]);
            Assert.True(result2.Report["turn_wait_ms"] < 1000);
            TestCommitRepair.AssertSet(h.Graph.Nodes.Keys, "a2");
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_admission_lease_expiry_fails_the_holder()
    {
        await Loop(async () =>
        {
            var clock = new FakeClock();
            var h = new Harness(clock);
            // Unit 1 gets the admit lease and stops.
            var stopped = await h.Store.BeginAdmitAsync(Group, "u1", h.Config.LeaseSeconds, Token);
            Assert.Null(await h.Store.BeginAdmitAsync(Group, "u2", h.Config.LeaseSeconds, Token));
            clock.Advance(h.Config.LeaseSeconds + 1);

            var unit2 = await AdmitTaskAsync(h, "u2");

            Assert.Equal(StateStore.StatusFailed, Status(h, stopped!.Seq));
            Assert.Equal(stopped.Seq + 1, unit2.Seq);
            // The episode node is visible with an empty `entity_edges` list.
            Assert.Empty(h.Graph.Episodes[unit2.Episodes[0].Uuid].EntityEdges);
            var span = Assert.Single(h.Tracer.Named("ingest.admit"));
            Assert.Equal(unit2.Seq, span.Attributes["ingest.seq"]);
        }, TestContext.Current.CancellationToken);
    }

    private static Task<AdmittedUnit> AdmitTaskAsync(Harness h, string unitId, CancellationToken? cancellationToken = null) =>
        Phases.AdmitEpisodeAsync(
            h.Graphiti,
            h.Store,
            h.Config,
            unitId: unitId,
            groupId: Group,
            name: "episode",
            episodeBody: "content",
            sourceDescription: "test",
            referenceTime: T0,
            cancellationToken: cancellationToken ?? Token);

    private static List<RawEpisode> BulkRawEpisodes() =>
    [
        .. Enumerable.Range(0, 2).Select(index => new RawEpisode
        {
            Name = $"episode-{index}",
            Content = $"content-{index}",
            SourceDescription = "test",
            Source = EpisodeType.Text,
            ReferenceTime = T0,
        }),
    ];

    private static Task<AdmittedUnit> AdmitBulkTaskAsync(Harness h, string unitId, CancellationToken? cancellationToken = null) =>
        Phases.AdmitBulkAsync(h.Graphiti, h.Store, h.Config, unitId: unitId, groupId: Group, bulkEpisodes: BulkRawEpisodes(), cancellationToken: cancellationToken ?? Token);

    // PORT: `patch_bulk_context` replaces phases.retrieve_previous_episodes_bulk with a function that gives each episode
    // no previous episode. The real function reads the previous episodes of each episode from HarnessDriver, which
    // returns no record for a read with `reference_time`: the same result (test_p9g_variants.py).

    /// <summary>Make each store method that changes state fail, except `begin_admit`.</summary>
    private static void ForbidStoreChanges(Harness h)
    {
        foreach (var method in new[] { "end_admit", "heartbeat", "fail", "start_analysis", "save_change_set" })
        {
            h.Store.Forbidden.Add(method);
        }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task test_admission_of_a_unit_in_progress_raises_and_writes_nothing(bool bulk)
    {
        await Loop(async () =>
        {
            var h = new Harness();
            Func<Harness, string, CancellationToken?, Task<AdmittedUnit>> admit = bulk ? AdmitBulkTaskAsync : AdmitTaskAsync;
            var first = await admit(h, "task-1", null);
            var writes = h.Graph.Log.Count(entry => entry == "write:bulk");
            ForbidStoreChanges(h);

            // The host runs the admit task again with the same `unit_id` while the unit is live.
            var error = await Assert.ThrowsAsync<UnitInProgressException>(() => admit(h, "task-1", null));

            Assert.Equal((Group, "task-1", first.Seq), (error.GroupId, error.UnitId, error.Seq));
            Assert.Equal(writes, h.Graph.Log.Count(entry => entry == "write:bulk"));
            Assert.Equal(first.Episodes.Select(episode => episode.Uuid), h.Graph.Episodes.Keys);
            Assert.Equal(StateStore.StatusAdmitted, Status(h, first.Seq));
        }, TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task test_admission_that_overlaps_a_first_attempt_does_not_end_its_admission(bool bulk)
    {
        await Loop(async () =>
        {
            var h = new Harness();
            Func<Harness, string, CancellationToken?, Task<AdmittedUnit>> admit = bulk ? AdmitBulkTaskAsync : AdmitTaskAsync;
            var firstInWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            List<int> writes = [];

            // PORT: the replaced add_nodes_and_edges_bulk waits before the write of FakeGraph. FakeGraph.BulkWrite waits
            // before the same write.
            h.Graph.BulkWrite = async write =>
            {
                writes.Add(1);
                firstInWrite.TrySetResult();
                await releaseFirst.Task;
                await write();
            };
            var firstAttempt = Asyncio.CreateTask(token => admit(h, "task-1", token), out _);
            await firstInWrite.Task;

            await Assert.ThrowsAsync<UnitInProgressException>(() => admit(h, "task-1", null));

            // The first attempt has the admit lease, and the retry wrote no episode node.
            Assert.Equal(StateStore.StatusAdmitting, Status(h, 1));
            Assert.Single(writes);
            Assert.Null(await h.Store.BeginAdmitAsync(Group, "task-2", h.Config.LeaseSeconds, Token));
            releaseFirst.SetResult();
            var unit = await firstAttempt;
            Assert.Equal(StateStore.StatusAdmitted, Status(h, unit.Seq));
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_admission_retry_after_a_failed_unit_gets_a_new_position()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            var first = await AdmitTaskAsync(h, "task-1");
            await h.Store.FailAsync(Group, first.Seq, "analysis error", cancellationToken: Token);

            var second = await AdmitTaskAsync(h, "task-1");

            Assert.Equal(StateStore.StatusFailed, Status(h, first.Seq));
            Assert.Equal(first.Seq + 1, second.Seq);
            Assert.Equal(StateStore.StatusAdmitted, Status(h, second.Seq));
            // The retry uses the episode node of the failed unit, and it has the content in memory.
            Assert.Equal([first.Episodes[0].Uuid], h.Graph.Episodes.Keys);
            Assert.Equal(first.Episodes[0].Uuid, second.Episodes[0].Uuid);
            Assert.Equal("content", second.Episodes[0].Content);
            Assert.Equal(first.CreatedAt, second.CreatedAt);
            Assert.Single(second.PreviousEpisodes);
            Assert.Empty(second.PreviousEpisodes[0]);
            Assert.Equal(1, h.Graph.Log.Count(entry => entry == "write:bulk"));
        }, TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task test_admission_does_not_change_an_episode_node_that_is_in_the_graph(bool bulk)
    {
        await Loop(async () =>
        {
            var h = new Harness();
            Func<Harness, string, CancellationToken?, Task<AdmittedUnit>> admit = bulk ? AdmitBulkTaskAsync : AdmitTaskAsync;
            var first = await admit(h, "task-1", null);
            // The unit committed, and the commit recorded the edges on each episode node.
            foreach (var episode in h.Graph.Episodes.Values)
            {
                episode.EntityEdges = ["e1"];
            }

            var stored = Dumps(h);
            var writes = h.Graph.Log.Count(entry => entry == "write:bulk");

            // The store has no record of the unit: the store deleted the unit after the
            // retention time. The host then runs the admit task again with the same `unit_id`.
            // PORT: `h.store = InMemoryIngestionStateStore()`. The new store logs to the graph, and an admission makes no
            // call that the log records.
            h.Store = new LoggingStore(h.Graph);
            var second = await admit(h, "task-1", null);

            Assert.Equal(first.Episodes.Select(episode => episode.Uuid), second.Episodes.Select(episode => episode.Uuid));
            // A node compares equal by UUID only, so the test compares each field.
            Assert.Equal(stored, Dumps(h));
            Assert.Equal(writes, h.Graph.Log.Count(entry => entry == "write:bulk"));
        }, TestContext.Current.CancellationToken);
    }

    // `{uuid: episode.model_dump() for uuid, episode in h.graph.episodes.items()}`.
    private static List<(string, string)> Dumps(Harness h) =>
        [.. h.Graph.Episodes.Select(pair => (pair.Key, PyJson.Dumps(pair.Value.ModelDump(json: true))))];

    [Fact(Timeout = 10000)]
    public async Task test_commit_keeps_the_links_that_the_stored_episode_node_has()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            var episode = MakeEpisode("ep-u1");
            var stored = episode.ModelCopy(deep: true);
            stored.EntityEdges = ["e-old", "e-new"];
            h.Graph.Episodes[episode.Uuid] = stored;
            h.Graph.Nodes["a"] = MakeNode("a", "Alice");
            h.Graph.Nodes["b"] = MakeNode("b", "Bob");

            var unit = await h.AdmitAsync("u1", episode);
            var changeSet = await h.AnalyzeAsync(
                unit,
                createEdges:
                [
                    MakeEdge("e-new", "a", "b", "Alice knows Bob"),
                    MakeEdge("e-2", "a", "b", "Alice likes Bob", Y),
                ]);
            await h.CommitAsync(unit, changeSet);

            // The link list is the stored list and then the new links of the change set.
            Assert.Equal(["e-old", "e-new", "e-2"], h.Graph.Episodes[episode.Uuid].EntityEdges);
        }, TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = 10000)]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task test_cancellation_in_begin_admit_releases_the_admit_lease(string storeKind)
    {
        // PORT: `tmp_path` is a new folder under the temporary folder of the system, which the test deletes.
        var tmpPath = Directory.CreateTempSubdirectory("p9g-").FullName;
        try
        {
            await Loop(async () =>
            {
                var h = new Harness();
                if (storeKind == "sqlite")
                {
                    h.Store = new LoggingStore(h.Graph, inner: new SQLiteIngestionStateStore(Path.Combine(tmpPath, "state.db")));
                }

                var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

                // The store call completes after the cancellation of the caller, as a call
                // in a worker thread does.
                // PORT: the hook of LoggingStore takes the place of `monkeypatch.setattr(h.store, 'begin_admit', ...)`. It
                // reads no token, as the replaced method gets none.
                h.Store.BeginAdmit = async (realBeginAdmit, groupId, unitId, ttl) =>
                {
                    var admission = await realBeginAdmit();
                    started.TrySetResult();
                    await Asyncio.SleepAsync(0.05, PortServices.Time);
                    return admission;
                };
                var task = Asyncio.CreateTask(token => AdmitTaskAsync(h, "task-1", token), out var cancel);
                await started.Task;
                await cancel.CancelAsync();
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
                h.Store.BeginAdmit = null;

                // The cancelled unit does not hold the admit lease, and it wrote no episode node.
                var second = await h.Store.BeginAdmitAsync(Group, "task-2", h.Config.LeaseSeconds, Token);
                Assert.NotNull(second);
                Assert.Equal(2, second.Seq);
                await Assert.ThrowsAsync<UnitExpiredException>(() => h.Store.EndAdmitAsync(Group, 1, Token));
                Assert.Empty(h.Graph.Episodes);
                await h.Store.CloseAsync(Token);
            }, TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(tmpPath, recursive: true);
        }
    }

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task test_admission_renews_its_lease_while_it_writes(bool bulk)
    {
        await Loop(async () =>
        {
            var h = new Harness(leaseSeconds: 0.15, writeMarginSeconds: 0.01);

            // PORT: the replaced add_nodes_and_edges_bulk sleeps before the write of FakeGraph. FakeGraph.BulkWrite sleeps
            // before the same write. The test also replaces phases.retrieve_previous_episodes_bulk (see
            // `patch_bulk_context` above).
            h.Graph.BulkWrite = async write =>
            {
                // The write of the episode nodes takes more time than the lease.
                await Asyncio.SleepAsync(0.4, PortServices.Time);
                await write();
            };
            AdmittedUnit unit;
            if (bulk)
            {
                unit = await Phases.AdmitBulkAsync(
                    h.Graphiti,
                    h.Store,
                    h.Config,
                    unitId: "task-1",
                    groupId: Group,
                    bulkEpisodes:
                    [
                        new RawEpisode { Name = "episode", Content = "content", SourceDescription = "test", Source = EpisodeType.Text, ReferenceTime = T0 },
                    ],
                    cancellationToken: Token);
            }
            else
            {
                unit = await AdmitTaskAsync(h, "task-1");
            }

            Assert.Equal(StateStore.StatusAdmitted, Status(h, unit.Seq));
            Assert.Single(h.Graph.Episodes);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_commit_waits_for_the_earlier_unit()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            var unit1 = await h.AdmitAsync("u1");
            var unit2 = await h.AdmitAsync("u2");
            var changeSet1 = await h.AnalyzeAsync(unit1, createNodes: [MakeNode("a1", "Acme")]);
            var changeSet2 = await h.AnalyzeAsync(unit2, createNodes: [MakeNode("a2", "Acme")]);
            Assert.IsType<CommitWait>(await h.Store.BeginCommitAsync(Group, unit2.Seq, 60.0, Token));

            var commit2 = Asyncio.CreateTask(token => h.CommitAsync(unit2, changeSet2, cancellationToken: token), out _);
            await Asyncio.SleepAsync(0.02, PortServices.Time, Token);
            Assert.False(commit2.IsCompleted);
            Assert.Empty(h.Graph.Nodes);

            await h.CommitAsync(unit1, changeSet1);
            var result2 = await commit2;

            // The commits are in sequence order, so unit 2 finds the node of unit 1.
            TestCommitRepair.AssertMap(result2.ChangeSet.UuidMap, ("a2", "a1"));
            Assert.True(result2.Report["turn_wait_ms"] > 0);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_cancellation_before_the_final_change_set_releases_the_position()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            var unit1 = await h.AdmitAsync("u1");
            var unit2 = await h.AdmitAsync("u2");
            var unit3 = await h.AdmitAsync("u3");
            var changeSet1 = await h.AnalyzeAsync(unit1, createNodes: [MakeNode("a1", "Acme")]);
            var changeSet2 = await h.AnalyzeAsync(unit2, createNodes: [MakeNode("a2", "Bolt", Y)]);
            var changeSet3 = await h.AnalyzeAsync(unit3);

            var commit2 = Asyncio.CreateTask(token => h.CommitAsync(unit2, changeSet2, cancellationToken: token), out var cancel);
            await Asyncio.SleepAsync(0.02, PortServices.Time, Token);
            await cancel.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => commit2);

            Assert.Equal(StateStore.StatusFailed, Status(h, unit2.Seq));
            await h.CommitAsync(unit1, changeSet1);
            await h.CommitAsync(unit3, changeSet3);
            TestCommitRepair.AssertSet(h.Graph.Nodes.Keys, "a1");
        }, TestContext.Current.CancellationToken);
    }

    // --- failures after the final change set is stored --------------------------------

    [Fact(Timeout = 10000)]
    public async Task test_failed_write_is_applied_again_with_no_model_call()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            h.Graph.FailBulkWrites = 2;
            h.Graph.PartialBulkWrite = true;
            var (unit, result) = await h.RunAsync("u1", createNodes: [MakeNode("a1", "Acme")]);

            Assert.Equal(3, result.Report["apply.attempts"]);
            Assert.Equal(1, h.Graph.Log.Count(entry => entry == "store:save_final"));
            Assert.Equal(3, h.Graph.Log.Count(entry => entry == "write:bulk"));
            Assert.Equal(StateStore.StatusCommitted, Status(h, unit.Seq));
            TestCommitRepair.AssertSet(h.Graph.Nodes.Keys, "a1");
            Assert.Equal(0, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_all_apply_attempts_fail()
    {
        await Loop(async () =>
        {
            var h = new Harness(maxApplyAttempts: 3);
            h.Graph.FailBulkWrites = 3;
            var unit1 = await h.AdmitAsync("u1");
            var changeSet1 = await h.AnalyzeAsync(unit1, createNodes: [MakeNode("a1", "Acme")]);

            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => h.CommitAsync(unit1, changeSet1));
            Assert.Matches("bulk write failed", error.Message);

            Assert.Equal(3, h.Graph.Log.Count(entry => entry == "write:bulk"));
            var stored = h.Store.Groups[Group].Units[unit1.Seq];
            Assert.Equal(StateStore.StatusApplyFailed, stored.Status);
            // The final change set stays for the delta of later units.
            Assert.NotNull(stored.Payload);
            Assert.Equal(unit1.Seq, h.Store.Groups[Group].Watermark);
            // No attempt writes the change set again, and the next unit commits.
            var (_, result2) = await h.RunAsync("u2", createNodes: [MakeNode("b2", "Bo", Y)]);
            TestCommitRepair.AssertSet(h.Graph.Nodes.Keys, "b2");
            Assert.Equal(0, result2.Report["recovered.count"]);
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>Make a unit that stored its final change set and then stopped.</summary>
    private static async Task<(AdmittedUnit Unit, CommitTurn Turn)> StopAfterFinalAsync(Harness h, string unitId, List<EntityNode> createNodes)
    {
        var unit = await h.AdmitAsync(unitId);
        var changeSet = await h.AnalyzeAsync(unit, createNodes);
        var turn = (CommitTurn)await h.Store.BeginCommitAsync(Group, unit.Seq, h.Config.LeaseSeconds, Token);
        await h.Store.SaveFinalAsync(Group, unit.Seq, turn.Fence, changeSet.ToBytes(), Token);
        return (unit, turn);
    }

    [Fact(Timeout = 10000)]
    public async Task test_next_unit_recovers_a_unit_that_stopped_after_the_final_change_set()
    {
        await Loop(async () =>
        {
            var clock = new FakeClock();
            var h = new Harness(clock);
            var (unit1, _) = await StopAfterFinalAsync(h, "u1", [MakeNode("a1", "Acme")]);
            var unit2 = await h.AdmitAsync("u2");
            var changeSet2 = await h.AnalyzeAsync(unit2, createNodes: [MakeNode("a2", "Acme")]);
            var lease2 = new UnitLease(h.Store, h.Config, Group, unit2.Seq, clock: clock.Call);

            // Before the deadline of unit 1, unit 2 must wait.
            Assert.IsType<CommitWait>(await h.Store.BeginCommitAsync(Group, unit2.Seq, 60.0, Token));
            // Unit 2 renews its deadline. The deadline of unit 1 then expires.
            clock.Advance(h.Config.LeaseSeconds - 1);
            await lease2.HeartbeatAsync(Token);
            clock.Advance(2);

            var result2 = await h.CommitAsync(unit2, changeSet2, lease: lease2);

            Assert.Equal(StateStore.StatusCommitted, Status(h, unit1.Seq));
            Assert.Equal(1, result2.Report["recovered.count"]);
            // Unit 2 commits after the recovery, so it finds the node of unit 1.
            TestCommitRepair.AssertMap(result2.ChangeSet.UuidMap, ("a2", "a1"));
            TestCommitRepair.AssertSet(h.Graph.Nodes.Keys, "a1");
            // The nodes of the recovered unit get no enrichment from that unit.
            Assert.Equal(["a1"], await h.Store.ReadyEnrichAsync(Group, unit2.Seq, Token));
            Assert.False(h.Store.Groups[Group].Enrich.ContainsKey(unit1.Seq));
            Assert.Equal(0, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_recovery_that_fails_marks_the_stopped_unit_apply_failed()
    {
        await Loop(async () =>
        {
            var clock = new FakeClock();
            var h = new Harness(clock, maxApplyAttempts: 2);
            var (unit1, _) = await StopAfterFinalAsync(h, "u1", [MakeNode("a1", "Acme")]);
            var unit2 = await h.AdmitAsync("u2");
            var changeSet2 = await h.AnalyzeAsync(unit2, createNodes: [MakeNode("b2", "Bo", Y)]);
            var lease2 = new UnitLease(h.Store, h.Config, Group, unit2.Seq, clock: clock.Call);
            // Unit 2 renews its deadline. The deadline of unit 1 then expires.
            clock.Advance(h.Config.LeaseSeconds - 1);
            await lease2.HeartbeatAsync(Token);
            clock.Advance(2);
            h.Graph.FailBulkWrites = 2;

            var result2 = await h.CommitAsync(unit2, changeSet2, lease: lease2);

            Assert.Equal(StateStore.StatusApplyFailed, Status(h, unit1.Seq));
            Assert.Equal(StateStore.StatusCommitted, Status(h, unit2.Seq));
            Assert.Equal(0, result2.Report["recovered.count"]);
            TestCommitRepair.AssertSet(h.Graph.Nodes.Keys, "b2");
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_stale_attempt_cannot_record_the_commit()
    {
        await Loop(async () =>
        {
            var clock = new FakeClock();
            var h = new Harness(clock);
            var (unit, staleTurn) = await StopAfterFinalAsync(h, "u1", [MakeNode("a1", "Acme")]);
            clock.Advance(h.Config.LeaseSeconds + 1);

            // A second attempt of the same unit gets the turn and the stored final change set.
            var lease = new UnitLease(h.Store, h.Config, Group, unit.Seq, clock: clock.Call);
            var final = ChangeSet.FromBytes(h.Store.Groups[Group].Units[unit.Seq].Payload!);
            var result = await h.CommitAsync(unit, final, lease: lease);
            Assert.Equal(1, result.Report["retry"]);

            await Assert.ThrowsAsync<StaleCommitException>(() => h.Store.FinishCommitAsync(Group, unit.Seq, staleTurn.Fence, ["a1"], 0.0, Token));
            await Assert.ThrowsAsync<StaleCommitException>(() => h.Store.SaveFinalAsync(Group, unit.Seq, staleTurn.Fence, "x"u8.ToArray(), Token));
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_second_attempt_waits_for_the_deadline_of_the_applying_attempt()
    {
        await Loop(async () =>
        {
            var clock = new FakeClock();
            var h = new Harness(clock);
            var (unit, firstTurn) = await StopAfterFinalAsync(h, "u1", [MakeNode("a1", "Acme")]);
            var final = ChangeSet.FromBytes(h.Store.Groups[Group].Units[unit.Seq].Payload!);
            var firstDeadline = h.Store.Groups[Group].Units[unit.Seq].Deadline;
            List<double> waits = [];

            Task Wait(CancellationToken cancellationToken)
            {
                // The second attempt writes nothing while the first attempt has the turn. Its
                // heartbeat does not move the deadline of the unit.
                Assert.DoesNotContain("write:bulk", h.Graph.Log);
                Assert.Equal(firstDeadline, h.Store.Groups[Group].Units[unit.Seq].Deadline);
                Assert.True(waits.Count < 10);
                waits.Add(clock.Call());
                clock.Advance(h.Config.LeaseSeconds / 2);
                return Task.CompletedTask;
            }

            var result = await h.CommitAsync(unit, final, wait: Wait);

            Assert.Equal(2, waits.Count);
            Assert.Equal(1, result.Report["retry"]);
            TestCommitRepair.AssertSet(h.Graph.Nodes.Keys, "a1");
            await Assert.ThrowsAsync<StaleCommitException>(() => h.Store.HeartbeatAsync(Group, unit.Seq, h.Config.LeaseSeconds, firstTurn.Fence, Token));
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task test_lease_heartbeat_is_stale_after_a_different_attempt_gets_the_turn()
    {
        await LoopGuard.RunAsync(async () =>
        {
            var clock = new FakeClock();
            var h = new Harness(clock);
            var (unit, firstTurn) = await StopAfterFinalAsync(h, "u1", [MakeNode("a1", "Acme")]);
            var firstLease = new UnitLease(h.Store, h.Config, Group, unit.Seq, clock: clock.Call) { Fence = firstTurn.Fence };
            await firstLease.HeartbeatAsync(TestContext.Current.CancellationToken);
            var budget = firstLease.WriteBudget();

            clock.Advance(h.Config.LeaseSeconds + 1);
            var retry = (CommitTurn)await h.Store.BeginCommitAsync(Group, unit.Seq, h.Config.LeaseSeconds, TestContext.Current.CancellationToken);
            Assert.Equal(firstTurn.Fence + 1, retry.Fence);

            // The first attempt cannot renew its local deadline, and it cannot write.
            await Assert.ThrowsAsync<StaleCommitException>(() => firstLease.HeartbeatAsync(TestContext.Current.CancellationToken));
            Assert.True(budget > 0);
            Assert.Throws<UnitExpiredException>(() => firstLease.WriteBudget());
        });
    }

    /// <summary>
    /// The clock of the first attempt stops, for example on a machine that was suspended.
    ///
    /// Its local deadline is thus in the future after a later unit recovered it. The check of
    /// the fence before each graph write stops the write of the old record.
    /// </summary>
    [Fact(Timeout = 10000)]
    public async Task test_attempt_checks_its_turn_before_each_graph_write()
    {
        await Loop(async () =>
        {
            var storeClock = new FakeClock();
            var localClock = new FakeClock();
            var h = new Harness(storeClock);
            h.Graph.Add(MakeNode("a", "A"), MakeNode("b", "B", Y));
            h.Graph.Add(MakeEdge("S", "a", "b", "A works at B", validAt: T0));
            var unit1 = await h.AdmitAsync("u1");
            var changeSet1 = await h.AnalyzeAsync(unit1, operations: [new AddEpisodesToEdge { EdgeUuid = "S", EpisodeUuids = ["ep-u1"] }]);
            var unit2 = await h.AdmitAsync("u2");
            var changeSet2 = await h.AnalyzeAsync(
                unit2,
                operations: [new EndEdge { EdgeUuid = "S", InvalidAt = T1, ExpiredAt = T2, OnlyIfOpen = false }]);
            var lease1 = new UnitLease(h.Store, h.Config, Group, unit1.Seq, clock: localClock.Call);
            var lease2 = new UnitLease(h.Store, h.Config, Group, unit2.Seq, clock: storeClock.Call);
            var paused = false;

            // PORT: the replaced add_nodes_and_edges_bulk calls the write of FakeGraph and then runs the commit of unit 2.
            // FakeGraph.BulkWrite runs the same steps around the same write.
            h.Graph.BulkWrite = async write =>
            {
                await write();
                if (paused)
                {
                    return;
                }

                paused = true;
                // Unit 1 stops after the first write of step C9 for longer than `lease_seconds`.
                storeClock.Advance(h.Config.LeaseSeconds / 2);
                await lease2.HeartbeatAsync(Token);
                storeClock.Advance((h.Config.LeaseSeconds / 2) + 1);
                // Unit 2 recovers unit 1 and then commits its own change set.
                var result2 = await h.CommitAsync(unit2, changeSet2, lease: lease2);
                Assert.Equal(1, result2.Report["recovered.count"]);
                Assert.Equal(T1, h.Graph.Edges["S"].InvalidAt);
            };
            Assert.Null(lease1.Fence);
            await Assert.ThrowsAsync<StaleCommitException>(() => h.CommitAsync(unit1, changeSet1, lease: lease1));

            // The local deadline of unit 1 did not stop the write. The fence did.
            Assert.True(lease1.WriteBudget() > 0);
            var edge = h.Graph.Edges["S"];
            Assert.Equal((T1, T2), (edge.InvalidAt, edge.ExpiredAt));
            Assert.Equal(["ep-u1"], edge.Episodes);
            Assert.Equal(2, h.Graph.Log.Count(entry => entry == "write:edge_update"));
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_expired_unit_stops_at_the_next_store_call()
    {
        await Loop(async () =>
        {
            var clock = new FakeClock();
            var h = new Harness(clock);
            var unit = await h.AdmitAsync("u1");
            var changeSet = await h.AnalyzeAsync(unit, createNodes: [MakeNode("a1", "Acme")]);
            clock.Advance(h.Config.LeaseSeconds + 1);

            await Assert.ThrowsAsync<UnitExpiredException>(() => h.CommitAsync(unit, changeSet));

            Assert.Empty(h.Graph.Nodes);
            Assert.DoesNotContain("write:bulk", h.Graph.Log);
        }, TestContext.Current.CancellationToken);
    }
}
