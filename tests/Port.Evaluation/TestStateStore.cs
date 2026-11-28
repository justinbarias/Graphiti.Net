// tests/ingestion/test_state_store.py (port/RULES.md, R25). Conformance tests for `IngestionStateStore`.
//
// `StoreConformance` takes a store factory. A host application can run the same class
// for its store: make a subclass and override the `make_store` fixture.
//
// PORT: each test body runs on the scheduler through LoopGuard (R25). The fixture `store` is the constructor and
// DisposeAsync. test_second_process_on_one_file starts a second Python process with multiprocessing; a .NET test has
// no such child, so the child work runs with a second store object on the same file, with the real clock as in
// Python. The theory with a canceled token has no Python original (R25).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Ingestion;

public abstract class StoreConformance : IAsyncDisposable
{
    protected const string G = "group-1";
    protected const double TTL = 60.0;

    protected StoreConformance()
    {
        Clock = new FakeClock();
        TmpPath = Path.Combine(Path.GetTempPath(), "graphiti-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TmpPath);
        Store = MakeStore();
    }

    private protected FakeClock Clock { get; }

    protected string TmpPath { get; }

    private protected IngestionStateStore Store { get; }

    public async ValueTask DisposeAsync()
    {
        await Store.CloseAsync(CancellationToken.None);
        if (Directory.Exists(TmpPath))
        {
            Directory.Delete(TmpPath, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact(Timeout = 5000)]
    public Task test_sequence_numbers_start_at_one_and_have_no_gap() => Loop(async () =>
    {
        List<long> seqs = [];
        foreach (var i in Enumerable.Range(0, 4))
        {
            seqs.Add(await Admit(Store, $"u{i}"));
        }

        Assert.Equal([1, 2, 3, 4], seqs);
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_same_unit_id_gets_same_seq() => Loop(async () =>
    {
        var first = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        var second = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        Assert.Equal((1L, false), (first.Seq, first.Existing));
        Assert.Equal((1L, true), (second.Seq, second.Existing));
        await Store.EndAdmitAsync(G, first.Seq, Token);
        Assert.Equal(first.Seq, (await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token))).Seq);
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_groups_have_separate_sequences() => Loop(async () =>
    {
        Assert.Equal(1, await Admit(Store, "u1", "a"));
        Assert.Equal(1, await Admit(Store, "u1", "b"));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_no_admit_lease_before_end_admit() => Loop(async () =>
    {
        var first = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        Assert.Null(await Store.BeginAdmitAsync(G, "u2", TTL, Token));
        await Store.EndAdmitAsync(G, first.Seq, Token);
        Assert.Equal(2, (await NotNone(Store.BeginAdmitAsync(G, "u2", TTL, Token))).Seq);
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_expired_admission_releases_lease_and_fails() => Loop(async () =>
    {
        var first = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        Clock.Advance(TTL + 1);
        var second = await NotNone(Store.BeginAdmitAsync(G, "u2", TTL, Token));
        Assert.Equal(2, second.Seq);
        await Assert.ThrowsAsync<UnitExpiredException>(() => Store.EndAdmitAsync(G, first.Seq, Token));
        await Store.EndAdmitAsync(G, second.Seq, Token);

        // The failed unit does not hold the commit order.
        Assert.IsType<CommitTurn>(await Store.BeginCommitAsync(G, second.Seq, TTL, Token));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_admission_of_a_live_unit_id_returns_the_same_unit() => Loop(async () =>
    {
        var first = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        Assert.Equal((1L, false, (long?)null), (first.Seq, first.Existing, first.PreviousSeq));

        // The unit has the admit lease. A second call with its `unit_id` gets the unit.
        var again = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        Assert.Equal((1L, true), (again.Seq, again.Existing));
        await Store.EndAdmitAsync(G, first.Seq, Token);
        await Store.SaveChangeSetAsync(G, first.Seq, "analysis"u8.ToArray(), Token);
        var turn = (CommitTurn)await Store.BeginCommitAsync(G, first.Seq, TTL, Token);
        await Store.SaveFinalAsync(G, first.Seq, turn.Fence, "final"u8.ToArray(), Token);

        // A unit with a stored final change set keeps its `seq` after its deadline.
        Clock.Advance(TTL + 1);
        again = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        Assert.Equal((1L, true), (again.Seq, again.Existing));
        var retry = (CommitTurn)await Store.BeginCommitAsync(G, first.Seq, TTL, Token);
        await Store.FinishCommitAsync(G, first.Seq, retry.Fence, [], 0.0, Token);
        again = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        Assert.Equal((1L, true), (again.Seq, again.Existing));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_admission_of_a_failed_unit_id_gives_a_new_seq() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        await Store.FailAsync(G, seq1, "error", cancellationToken: Token);
        var second = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        Assert.Equal((2L, false, (long?)seq1), (second.Seq, second.Existing, second.PreviousSeq));
        await Store.EndAdmitAsync(G, second.Seq, Token);

        // An expired unit with no stored final change set fails in the next admission.
        Clock.Advance(TTL + 1);
        var third = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        Assert.Equal((3L, false, (long?)second.Seq), (third.Seq, third.Existing, third.PreviousSeq));
        await Assert.ThrowsAsync<UnitExpiredException>(() => Store.StartAnalysisAsync(G, second.Seq, Token));
        await Store.EndAdmitAsync(G, third.Seq, Token);

        // A unit whose apply attempts failed also gives a new `seq`.
        var turn = (CommitTurn)await Store.BeginCommitAsync(G, third.Seq, TTL, Token);
        await Store.SaveFinalAsync(G, third.Seq, turn.Fence, "final"u8.ToArray(), Token);
        await Store.FailAsync(G, third.Seq, "apply_failed", turn.Fence, Token);
        var fourth = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        Assert.Equal((4L, false, (long?)third.Seq), (fourth.Seq, fourth.Existing, fourth.PreviousSeq));

        // The payload of the failed unit stays in the delta.
        await Store.EndAdmitAsync(G, fourth.Seq, Token);
        Assert.Equal([third.Seq], (await Store.LoadDeltaAsync(G, 0, fourth.Seq, 0.0, Token)).Select(unit => unit.Seq));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_start_analysis_returns_watermark_and_time() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        var seq2 = await Admit(Store, "u2");
        Assert.Equal(0, (await Store.StartAnalysisAsync(G, seq2, Token)).SnapshotSeq);
        await Commit(Store, seq1);
        var seq3 = await Admit(Store, "u3");
        var start = await Store.StartAnalysisAsync(G, seq3, Token);
        Assert.Equal(1, start.SnapshotSeq);
        Assert.Equal(Clock.Now(), start.StartedAt);
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_commit_wait_then_turn() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        var seq2 = await Admit(Store, "u2");
        Assert.IsType<CommitWait>(await Store.BeginCommitAsync(G, seq2, TTL, Token));
        await Commit(Store, seq1);
        Assert.IsType<CommitTurn>(await Store.BeginCommitAsync(G, seq2, TTL, Token));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_commit_turn_after_earlier_unit_failed() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        var seq2 = await Admit(Store, "u2");
        await Store.FailAsync(G, seq1, "error", cancellationToken: Token);
        var turn = await Store.BeginCommitAsync(G, seq2, TTL, Token);
        Assert.IsType<CommitTurn>(turn);
        Assert.Null(((CommitTurn)turn).FinalPayload);
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_expired_earlier_unit_with_no_final_becomes_failed() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        await Store.SaveChangeSetAsync(G, seq1, "analysis"u8.ToArray(), Token);
        Clock.Advance(TTL - 1);
        var seq2 = await Admit(Store, "u2");
        Clock.Advance(2);
        Assert.IsType<CommitTurn>(await Store.BeginCommitAsync(G, seq2, TTL, Token));
        await Assert.ThrowsAsync<UnitExpiredException>(() => Store.HeartbeatAsync(G, seq1, TTL, cancellationToken: Token));
        Assert.Empty(await Store.LoadDeltaAsync(G, 0, 10, 0.0, Token));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_expired_applying_unit_gives_commit_recover() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        var turn1 = (CommitTurn)await Store.BeginCommitAsync(G, seq1, TTL, Token);
        await Store.SaveFinalAsync(G, seq1, turn1.Fence, "final-1"u8.ToArray(), Token);
        Clock.Advance(TTL - 1);
        var seq2 = await Admit(Store, "u2");
        Assert.IsType<CommitWait>(await Store.BeginCommitAsync(G, seq2, TTL, Token));
        Clock.Advance(2);
        var recover = Assert.IsType<CommitRecover>(await Store.BeginCommitAsync(G, seq2, TTL, Token));
        Assert.Equal(seq1, recover.Seq);
        Assert.Equal("final-1"u8.ToArray(), recover.FinalPayload);
        Assert.Equal(turn1.Fence + 1, recover.Fence);

        // The old attempt is stale.
        await Assert.ThrowsAsync<StaleCommitException>(() => Store.SaveFinalAsync(G, seq1, turn1.Fence, "other"u8.ToArray(), Token));
        await Assert.ThrowsAsync<StaleCommitException>(() => Store.FinishCommitAsync(G, seq1, turn1.Fence, [], 0.0, Token));
        await Assert.ThrowsAsync<StaleCommitException>(() => Store.FailAsync(G, seq1, "apply_failed", turn1.Fence, Token));

        await Store.FinishCommitAsync(G, seq1, recover.Fence, [], 0.0, Token);
        Assert.IsType<CommitTurn>(await Store.BeginCommitAsync(G, seq2, TTL, Token));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_own_retry_gets_stored_final_payload() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        var turn = (CommitTurn)await Store.BeginCommitAsync(G, seq1, TTL, Token);
        await Store.SaveFinalAsync(G, seq1, turn.Fence, "final-1"u8.ToArray(), Token);
        Clock.Advance(TTL + 1);
        var retry = Assert.IsType<CommitTurn>(await Store.BeginCommitAsync(G, seq1, TTL, Token));
        Assert.Equal("final-1"u8.ToArray(), retry.FinalPayload);
        Assert.Equal(turn.Fence + 1, retry.Fence);
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_second_attempt_waits_while_the_applying_attempt_has_a_live_deadline() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        var turn = (CommitTurn)await Store.BeginCommitAsync(G, seq1, TTL, Token);
        await Store.SaveFinalAsync(G, seq1, turn.Fence, "final-1"u8.ToArray(), Token);

        // A second attempt of the same unit gets no turn and no new fence.
        Assert.IsType<CommitWait>(await Store.BeginCommitAsync(G, seq1, TTL, Token));

        // The second attempt has no fence. It cannot set the deadline of the unit.
        await Assert.ThrowsAsync<StaleCommitException>(() => Store.HeartbeatAsync(G, seq1, TTL, cancellationToken: Token));

        // The attempt with the turn keeps the deadline in the future.
        Clock.Advance(TTL - 1);
        await Store.HeartbeatAsync(G, seq1, TTL, turn.Fence, Token);
        Clock.Advance(TTL - 1);
        Assert.IsType<CommitWait>(await Store.BeginCommitAsync(G, seq1, TTL, Token));
        await Store.FinishCommitAsync(G, seq1, turn.Fence, [], 0.0, Token);
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_heartbeat_with_an_old_fence_is_stale() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        var turn = (CommitTurn)await Store.BeginCommitAsync(G, seq1, TTL, Token);
        await Store.SaveFinalAsync(G, seq1, turn.Fence, "final-1"u8.ToArray(), Token);
        Clock.Advance(TTL + 1);
        var retry = Assert.IsType<CommitTurn>(await Store.BeginCommitAsync(G, seq1, TTL, Token));

        // The old attempt cannot set the deadline after a different attempt has the turn.
        await Assert.ThrowsAsync<StaleCommitException>(() => Store.HeartbeatAsync(G, seq1, TTL, turn.Fence, Token));
        await Store.HeartbeatAsync(G, seq1, TTL, retry.Fence, Token);
        await Store.FinishCommitAsync(G, seq1, retry.Fence, [], 0.0, Token);

        // The enrichment of the unit sets the deadline with the same fence.
        await Store.HeartbeatAsync(G, seq1, TTL, retry.Fence, Token);
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_save_change_set_is_permitted_only_for_an_admitted_unit() => Loop(async () =>
    {
        var admission = await NotNone(Store.BeginAdmitAsync(G, "u1", TTL, Token));
        await Assert.ThrowsAsync<StaleCommitException>(() => Store.SaveChangeSetAsync(G, admission.Seq, "analysis-0"u8.ToArray(), Token));
        await Store.EndAdmitAsync(G, admission.Seq, Token);
        var seq1 = admission.Seq;
        await Store.SaveChangeSetAsync(G, seq1, "analysis-1"u8.ToArray(), Token);

        // A second analysis of the unit does not replace the stored payload.
        await Assert.ThrowsAsync<StaleCommitException>(() => Store.SaveChangeSetAsync(G, seq1, "analysis-2"u8.ToArray(), Token));
        var turn = (CommitTurn)await Store.BeginCommitAsync(G, seq1, TTL, Token);
        await Store.SaveFinalAsync(G, seq1, turn.Fence, "final-1"u8.ToArray(), Token);
        await Assert.ThrowsAsync<StaleCommitException>(() => Store.SaveChangeSetAsync(G, seq1, "analysis-3"u8.ToArray(), Token));
        Clock.Advance(TTL + 1);
        await Assert.ThrowsAsync<StaleCommitException>(() => Store.SaveChangeSetAsync(G, seq1, "analysis-4"u8.ToArray(), Token));
        var retry = Assert.IsType<CommitTurn>(await Store.BeginCommitAsync(G, seq1, TTL, Token));
        Assert.Equal("final-1"u8.ToArray(), retry.FinalPayload);

        await Store.FinishCommitAsync(G, seq1, retry.Fence, [], 0.0, Token);
        await Assert.ThrowsAsync<StaleCommitException>(() => Store.SaveChangeSetAsync(G, seq1, "analysis-5"u8.ToArray(), Token));
        var seq2 = await Admit(Store, "u2");
        var delta = await Store.LoadDeltaAsync(G, 0, seq2, 0.0, Token);
        Assert.Equal([(seq1, "final-1")], delta.Select(unit => (unit.Seq, System.Text.Encoding.UTF8.GetString(unit.Payload))));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_fail_is_not_permitted_for_applying() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        var turn = (CommitTurn)await Store.BeginCommitAsync(G, seq1, TTL, Token);
        await Store.SaveFinalAsync(G, seq1, turn.Fence, "final-1"u8.ToArray(), Token);
        await Assert.ThrowsAsync<ArgumentException>(() => Store.FailAsync(G, seq1, "error", cancellationToken: Token));
        await Assert.ThrowsAsync<StaleCommitException>(() => Store.FailAsync(G, seq1, "apply_failed", cancellationToken: Token));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_apply_failed_keeps_payload_and_releases_position() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        var turn = (CommitTurn)await Store.BeginCommitAsync(G, seq1, TTL, Token);
        await Store.SaveFinalAsync(G, seq1, turn.Fence, "final-1"u8.ToArray(), Token);
        var seq2 = await Admit(Store, "u2");
        await Store.FailAsync(G, seq1, "apply_failed", turn.Fence, Token);

        Assert.IsType<CommitTurn>(await Store.BeginCommitAsync(G, seq2, TTL, Token));
        var delta = await Store.LoadDeltaAsync(G, 0, seq2, Clock.Now(), Token);
        Assert.Equal(
            [(seq1, "apply_failed", "final-1")],
            delta.Select(unit => (unit.Seq, unit.Status, System.Text.Encoding.UTF8.GetString(unit.Payload))));
        await Assert.ThrowsAsync<UnitExpiredException>(() => Store.BeginCommitAsync(G, seq1, TTL, Token));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_load_delta_range_and_since() => Loop(async () =>
    {
        List<long> seqs = [];
        foreach (var i in Enumerable.Range(0, 4))
        {
            seqs.Add(await Admit(Store, $"u{i}"));
        }

        await Commit(Store, seqs[0], "p1"u8.ToArray(), retainSeconds: 1_000);
        Clock.Advance(30);
        await Commit(Store, seqs[1], "p2"u8.ToArray(), retainSeconds: 1_000);
        await Commit(Store, seqs[2], "p3"u8.ToArray(), retainSeconds: 1_000);

        var delta = await Store.LoadDeltaAsync(G, 2, 4, Clock.Now() + 1, Token);
        Assert.Equal([(3L, "committed", "p3")], delta.Select(unit => (unit.Seq, unit.Status, System.Text.Encoding.UTF8.GetString(unit.Payload))));

        // Units at or below after_seq are in the result when they ended at or after `since`.
        delta = await Store.LoadDeltaAsync(G, 2, 4, Clock.Now() - 10, Token);
        Assert.Equal([2, 3], delta.Select(unit => unit.Seq));
        delta = await Store.LoadDeltaAsync(G, 2, 4, 0.0, Token);
        Assert.Equal([1, 2, 3], delta.Select(unit => unit.Seq));

        // before_seq is exclusive.
        Assert.Equal([1, 2], (await Store.LoadDeltaAsync(G, 0, 3, Clock.Now() + 1, Token)).Select(unit => unit.Seq));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_analysis_payload_is_not_in_delta() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        await Store.SaveChangeSetAsync(G, seq1, "analysis"u8.ToArray(), Token);
        var seq2 = await Admit(Store, "u2");
        Assert.Empty(await Store.LoadDeltaAsync(G, 0, seq2, 0.0, Token));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_ready_enrich_in_seq_order_for_each_node() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        var seq2 = await Admit(Store, "u2");
        await Commit(Store, seq1, nodeUuids: ["a", "b"]);
        await Commit(Store, seq2, nodeUuids: ["b", "c"]);

        Assert.Equal(["a", "b"], await Store.ReadyEnrichAsync(G, seq1, Token));
        Assert.Equal(["c"], await Store.ReadyEnrichAsync(G, seq2, Token));
        await Store.FinishEnrichAsync(G, seq1, ["b"], Token);
        Assert.Equal(["b", "c"], await Store.ReadyEnrichAsync(G, seq2, Token));
        await Store.FinishEnrichAsync(G, seq2, ["b", "c"], Token);
        Assert.Empty(await Store.ReadyEnrichAsync(G, seq2, Token));
        Assert.Equal(["a"], await Store.ReadyEnrichAsync(G, seq1, Token));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_entries_of_expired_unit_do_not_stop_later_unit() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        var seq2 = await Admit(Store, "u2");
        await Commit(Store, seq1, nodeUuids: ["a"]);
        Clock.Advance(TTL - 1);
        await Store.HeartbeatAsync(G, seq2, TTL, cancellationToken: Token);
        await Commit(Store, seq2, nodeUuids: ["a"]);
        Assert.Empty(await Store.ReadyEnrichAsync(G, seq2, Token));
        Clock.Advance(2);
        Assert.Equal(["a"], await Store.ReadyEnrichAsync(G, seq2, Token));
        await Assert.ThrowsAsync<UnitExpiredException>(() => Store.ReadyEnrichAsync(G, seq1, Token));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_fail_deletes_enrichment_entries_only_before_commit() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        await Commit(Store, seq1, nodeUuids: ["a"]);

        // `fail` for a committed unit changes nothing.
        await Store.FailAsync(G, seq1, "error", cancellationToken: Token);
        Assert.Equal(["a"], await Store.ReadyEnrichAsync(G, seq1, Token));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_expired_unit_raises() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        Clock.Advance(TTL + 1);
        foreach (var call in new Func<Task>[]
        {
            () => Store.StartAnalysisAsync(G, seq1, Token),
            () => Store.SaveChangeSetAsync(G, seq1, "x"u8.ToArray(), Token),
            () => Store.HeartbeatAsync(G, seq1, TTL, cancellationToken: Token),
            () => Store.BeginCommitAsync(G, seq1, TTL, Token),
            () => Store.ReadyEnrichAsync(G, seq1, Token),
        })
        {
            await Assert.ThrowsAsync<UnitExpiredException>(call);
        }
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_failed_unit_raises() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        await Store.FailAsync(G, seq1, "error", cancellationToken: Token);
        await Assert.ThrowsAsync<UnitExpiredException>(() => Store.StartAnalysisAsync(G, seq1, Token));
        await Assert.ThrowsAsync<UnitExpiredException>(() => Store.SaveFinalAsync(G, seq1, 1, "x"u8.ToArray(), Token));
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_heartbeat_extends_deadline() => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        Clock.Advance(TTL - 1);
        await Store.HeartbeatAsync(G, seq1, TTL, cancellationToken: Token);
        Clock.Advance(TTL - 1);
        await Store.SaveChangeSetAsync(G, seq1, "x"u8.ToArray(), Token);
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 5000)]
    public Task test_old_units_are_deleted_only_with_three_conditions() => Loop(async () =>
    {
        var retain = 300.0;
        var seq1 = await Admit(Store, "u1");
        var seq2 = await Admit(Store, "u2");
        var seq3 = await Admit(Store, "u3");
        foreach (var seq in new[] { seq1, seq2, seq3 })
        {
            await Store.HeartbeatAsync(G, seq, retain * 10, cancellationToken: Token);
        }

        // Unit 3 is in progress with snapshot 0.
        await Store.StartAnalysisAsync(G, seq3, Token);
        await Commit(Store, seq1, "p1"u8.ToArray(), nodeUuids: ["a"], retainSeconds: retain);

        async Task<List<long>> DeltaSeqs() => [.. (await Store.LoadDeltaAsync(G, 0, 100, 0.0, Token)).Select(unit => unit.Seq)];

        Clock.Advance(retain + 1);
        await Commit(Store, seq2, "p2"u8.ToArray(), retainSeconds: retain);

        // Unit 1 is old, but it has an enrichment entry and a snapshot is below it.
        Assert.Equal([1, 2], await DeltaSeqs());

        await Store.FinishEnrichAsync(G, seq1, ["a"], Token);
        await Commit(Store, seq3, "p3"u8.ToArray(), retainSeconds: retain);

        // No unit in progress and no entry: unit 1 is deleted. Units 2 and 3 are not old.
        Assert.Equal([2, 3], await DeltaSeqs());

        Clock.Advance(retain + 1);
        var seq4 = await Admit(Store, "u4");
        await Commit(Store, seq4, "p4"u8.ToArray(), retainSeconds: retain);
        Assert.Equal([4], await DeltaSeqs());
        Assert.Equal(4, seq4);
    }, TestContext.Current.CancellationToken);

    protected static CancellationToken Token => TestContext.Current.CancellationToken;

    // The token is the token of the test, which Token reads in the body.
    protected static Task Loop(Func<Task> body, CancellationToken token) => token.IsCancellationRequested ? Task.FromCanceled(token) : LoopGuard.RunAsync(body);

    private protected static async Task<long> Admit(IngestionStateStore store, string unitId, string groupId = G)
    {
        var admission = await store.BeginAdmitAsync(groupId, unitId, TTL, Token);
        Assert.NotNull(admission);
        await store.EndAdmitAsync(groupId, admission.Seq, Token);
        return admission.Seq;
    }

    private protected static async Task<long> Commit(
        IngestionStateStore store,
        long seq,
        byte[]? payload = null,
        List<string>? nodeUuids = null,
        double retainSeconds = 0.0)
    {
        var turn = Assert.IsType<CommitTurn>(await store.BeginCommitAsync(G, seq, TTL, Token));
        await store.SaveFinalAsync(G, seq, turn.Fence, payload ?? "final"u8.ToArray(), Token);
        await store.FinishCommitAsync(G, seq, turn.Fence, nodeUuids ?? [], retainSeconds, Token);
        return turn.Fence;
    }

    private protected abstract IngestionStateStore MakeStore();

    private static async Task<Admission> NotNone(Task<Admission?> call)
    {
        var admission = await call;
        Assert.NotNull(admission);
        return admission;
    }

    private protected sealed class FakeClock
    {
        private double now = 1_000.0;

        internal double Now() => now;

        internal void Advance(double seconds) => now += seconds;
    }
}

public sealed class TestInMemoryStore : StoreConformance
{
    // PORT: each method of the in-memory store has no `await`. A Python coroutine with no suspension point runs to
    // its end in a canceled task, so the port runs the call with a canceled token too, and adds no token check.
    [Fact(Timeout = 5000)]
    public Task A_canceled_token_does_not_stop_a_call_with_no_await() => Loop(async () =>
    {
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var admission = await Store.BeginAdmitAsync(G, "u1", TTL, canceled.Token);
        Assert.Equal(1, admission?.Seq);
    }, TestContext.Current.CancellationToken);

    private protected override IngestionStateStore MakeStore() => new InMemoryIngestionStateStore(Clock.Now);
}

public sealed class TestSQLiteStore : StoreConformance
{
    public static TheoryData<string> Methods() =>
    [
        "begin_admit", "end_admit", "start_analysis", "save_change_set", "heartbeat", "begin_commit", "load_delta",
        "save_final", "finish_commit", "fail", "ready_enrich", "finish_enrich",
    ];

    [Fact(Timeout = 10000)]
    public Task test_two_store_objects_on_one_file() => Loop(async () =>
    {
        var (first, second) = (MakeStore(), MakeStore());
        try
        {
            var seq1 = await Admit(first, "u1");
            var seq2 = await Admit(second, "u2");
            Assert.Equal((1L, 2L), (seq1, seq2));
            Assert.IsType<CommitWait>(await second.BeginCommitAsync(G, seq2, TTL, Token));
            await Commit(first, seq1, "p1"u8.ToArray(), retainSeconds: 1_000);
            Assert.IsType<CommitTurn>(await second.BeginCommitAsync(G, seq2, TTL, Token));
            Assert.Equal(["p1"], (await second.LoadDeltaAsync(G, 0, seq2, 0.0, Token)).Select(unit => System.Text.Encoding.UTF8.GetString(unit.Payload)));
        }
        finally
        {
            await first.CloseAsync(CancellationToken.None);
            await second.CloseAsync(CancellationToken.None);
        }
    }, TestContext.Current.CancellationToken);

    [Fact(Timeout = 10000)]
    public Task test_second_process_on_one_file() => Loop(async () =>
    {
        var path = Path.Combine(TmpPath, "shared.db");
        var store = new SQLiteIngestionStateStore(path);
        try
        {
            var seq1 = await Admit(store, "parent-unit");
            await Commit(store, seq1, "parent"u8.ToArray(), retainSeconds: 1_000);
            var (childSeq, childDelta) = await ChildProcessWork(path);
            Assert.Equal(2, childSeq);
            Assert.Equal(["parent"], childDelta);
            Assert.Equal(
                ["parent", "child"],
                (await store.LoadDeltaAsync(G, 0, 10, 0.0, Token)).Select(unit => System.Text.Encoding.UTF8.GetString(unit.Payload)));
        }
        finally
        {
            await store.CloseAsync(CancellationToken.None);
        }
    }, TestContext.Current.CancellationToken);

    // The theory with no Python original (R25): each method with a canceled token throws OperationCanceledException,
    // and the work of the method does not run.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(Methods))]
    public Task Each_method_with_a_canceled_token_throws_and_changes_nothing(string method) => Loop(async () =>
    {
        var seq1 = await Admit(Store, "u1");
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var token = canceled.Token;
        Func<Task> call = method switch
        {
            "begin_admit" => () => Store.BeginAdmitAsync(G, "u2", TTL, token),
            "end_admit" => () => Store.EndAdmitAsync(G, seq1, token),
            "start_analysis" => () => Store.StartAnalysisAsync(G, seq1, token),
            "save_change_set" => () => Store.SaveChangeSetAsync(G, seq1, "x"u8.ToArray(), token),
            "heartbeat" => () => Store.HeartbeatAsync(G, seq1, TTL, cancellationToken: token),
            "begin_commit" => () => Store.BeginCommitAsync(G, seq1, TTL, token),
            "load_delta" => () => Store.LoadDeltaAsync(G, 0, 10, 0.0, token),
            "save_final" => () => Store.SaveFinalAsync(G, seq1, 1, "x"u8.ToArray(), token),
            "finish_commit" => () => Store.FinishCommitAsync(G, seq1, 1, ["a"], 0.0, token),
            "fail" => () => Store.FailAsync(G, seq1, "error", cancellationToken: token),
            "ready_enrich" => () => Store.ReadyEnrichAsync(G, seq1, token),
            _ => () => Store.FinishEnrichAsync(G, seq1, ["a"], token),
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(call);

        // The unit is still admitted with no payload: the canceled call changed nothing.
        await Store.SaveChangeSetAsync(G, seq1, "analysis"u8.ToArray(), Token);
        Assert.Equal(2, await Admit(Store, "u2"));
    }, TestContext.Current.CancellationToken);

    private protected override IngestionStateStore MakeStore() => new SQLiteIngestionStateStore(Path.Combine(TmpPath, "ingest.db"), Clock.Now);

    private static async Task<(long Seq, List<string> Delta)> ChildProcessWork(string path)
    {
        var store = new SQLiteIngestionStateStore(path);
        try
        {
            var seq = await Admit(store, "child-unit");
            List<string> delta = [.. (await store.LoadDeltaAsync(G, 0, seq, 0.0, Token)).Select(unit => System.Text.Encoding.UTF8.GetString(unit.Payload))];
            await Commit(store, seq, "child"u8.ToArray(), retainSeconds: 1_000);
            return (seq, delta);
        }
        finally
        {
            await store.CloseAsync(CancellationToken.None);
        }
    }
}
