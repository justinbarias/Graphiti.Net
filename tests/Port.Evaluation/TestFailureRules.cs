// Ported from tests/ingestion/test_failure_rules.py (port/RULES.md, R25). The code under test is
// graphiti_core/ingestion/phases.py. Packet P8c of plan 0004, layer 8.
//
// Ported: test_lease_heartbeat_is_stale_after_a_different_attempt_gets_the_turn, which needs only the store part of the
// Harness and UnitLease.
//
// Waits for layer 9: each other test of the file calls admit_episode, admit_bulk, analyze_episode, Harness.commit,
// Harness.run, or Harness.enrich, which call graphiti._resolve_request_scope first. GraphitiCore is a skeleton until
// layer 9. Each line names the case of values/phases.json that covers the same behavior of the code under test with no
// `Graphiti` object, or none.
// - test_analysis_failure_releases_the_position: _fail_unit (seq 1 admitted).
// - test_admission_lease_expiry_fails_the_holder (episode, bulk): _begin_admit (a different unit holds the admit lease).
// - test_admission_of_a_unit_in_progress_raises_and_writes_nothing (episode, bulk): _begin_admit (unit u1 admitted).
// - test_admission_that_overlaps_a_first_attempt_does_not_end_its_admission (episode, bulk): _fail_admission (existing).
// - test_admission_retry_after_a_failed_unit_gets_a_new_position: _begin_admit (u1 failed), and _written_episodes.
// - test_admission_does_not_change_an_episode_node_that_is_in_the_graph (episode, bulk): _written_episodes.
// - test_commit_keeps_the_links_that_the_stored_episode_node_has: _episodes_with_stored_links, and apply_change_set
//   operations.
// - test_cancellation_in_begin_admit_releases_the_admit_lease (memory, sqlite): _begin_admit (the cancel in the shield).
// - test_admission_renews_its_lease_while_it_writes (episode, bulk): none (_admit_heartbeat is not recorded).
// - test_commit_waits_for_the_earlier_unit: none (the wait loop of commit_unit).
// - test_cancellation_before_the_final_change_set_releases_the_position: none (commit_unit).
// - test_failed_write_is_applied_again_with_no_model_call: _apply_with_retries first_attempt_fails.
// - test_all_apply_attempts_fail: _apply_with_retries each_attempt_fails, and _mark_apply_failed.
// - test_next_unit_recovers_a_unit_that_stopped_after_the_final_change_set: _recover_unit recovered.
// - test_recovery_that_fails_marks_the_stopped_unit_apply_failed: _recover_unit each_apply_fails.
// - test_stale_attempt_cannot_record_the_commit: _recover_unit stale, and UnitLease.heartbeat (a fence of an earlier turn).
// - test_second_attempt_waits_for_the_deadline_of_the_applying_attempt: UnitLease.heartbeat (applying unit, no fence).
// - test_second_analysis_of_a_unit_does_not_fail_the_unit: none (analyze_episode).
// - test_attempt_checks_its_turn_before_each_graph_write: apply_change_set operations_turn_ok, turn_stale.
// - test_expired_unit_stops_at_the_next_store_call: UnitLease.heartbeat (an expired unit).
// - test_enrichment_of_one_node_runs_in_sequence_order: none (enrich_unit).
// - test_enrichment_applies_the_patch_to_the_current_node: none (enrich_unit).
// - test_enrichment_failure_keeps_the_node_values_and_does_not_raise: _hydrate_nodes failure.
// - test_enrichment_hook_selects_the_nodes_to_write: none (enrich_unit).
// - test_enrichment_does_not_create_again_a_node_that_a_writer_deleted: none (enrich_unit).
// - test_enrichment_write_error_releases_the_enrichment_order: none (enrich_unit).
//
// tests/ingestion/test_analyze_bulk.py: test_bulk_analysis_does_not_create_a_node_that_is_in_the_graph calls admit_bulk
// and analyze_bulk with the Harness, so it waits for layer 9. It also patches node_ops._collect_candidate_nodes and
// phases.dedupe_edges_bulk (open item O10).
// tests/ingestion/test_concurrent_identity_properties.py: each of its four tests builds a `Graphiti` object (through
// add_episode or add_episode_bulk) or calls admit_episode, so each one waits for layer 9:
// test_concurrent_add_episode_uses_identity_properties_for_lookup,
// test_concurrent_add_episode_resolves_same_identity_without_dedupe_model_call,
// test_concurrent_add_episode_without_identity_properties_skips_identity_calls, and
// test_invalid_identity_properties_fail_before_concurrent_writes (episode, bulk).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Ingestion;
using static Graphiti.Port.Evaluation.IngestionFixtures;

public class TestFailureRules
{
    /// <summary>Make a unit that stored its final change set and then stopped.</summary>
    private static async Task<(AdmittedUnit Unit, CommitTurn Turn)> StopAfterFinalAsync(Harness h, string unitId, List<Graphiti.Core.EntityNode> createNodes)
    {
        var unit = await h.AdmitAsync(unitId);
        var changeSet = await h.AnalyzeAsync(unit, createNodes);
        var turn = (CommitTurn)await h.Store.BeginCommitAsync(IngestionFixtures.Group, unit.Seq, h.Config.LeaseSeconds, TestContext.Current.CancellationToken);
        await h.Store.SaveFinalAsync(IngestionFixtures.Group, unit.Seq, turn.Fence, changeSet.ToBytes());
        return (unit, turn);
    }

    [Fact(Timeout = 5000)]
    public async Task test_lease_heartbeat_is_stale_after_a_different_attempt_gets_the_turn()
    {
        await LoopGuard.RunAsync(async () =>
        {
            var clock = new FakeClock();
            var h = new Harness(clock);
            var (unit, firstTurn) = await StopAfterFinalAsync(h, "u1", [MakeNode("a1", "Acme")]);
            var firstLease = new UnitLease(h.Store, h.Config, IngestionFixtures.Group, unit.Seq, clock: clock.Call) { Fence = firstTurn.Fence };
            await firstLease.HeartbeatAsync(TestContext.Current.CancellationToken);
            var budget = firstLease.WriteBudget();

            clock.Advance(h.Config.LeaseSeconds + 1);
            var retry = (CommitTurn)await h.Store.BeginCommitAsync(IngestionFixtures.Group, unit.Seq, h.Config.LeaseSeconds, TestContext.Current.CancellationToken);
            Assert.Equal(firstTurn.Fence + 1, retry.Fence);

            // The first attempt cannot renew its local deadline, and it cannot write.
            await Assert.ThrowsAsync<StaleCommitException>(() => firstLease.HeartbeatAsync(TestContext.Current.CancellationToken));
            Assert.True(budget > 0);
            Assert.Throws<UnitExpiredException>(() => firstLease.WriteBudget());
        });
    }
}
