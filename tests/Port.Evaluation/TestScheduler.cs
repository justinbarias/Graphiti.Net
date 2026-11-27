// Ported from tests/ingestion/test_scheduler.py (port/RULES.md, R25). The code under test is
// graphiti_core/ingestion/scheduler.py. Packet P8d of plan 0004, layer 8.
//
// No test of the file can run on the port today, so the file holds no test method. Each line names the case of
// values/scheduler.json (port/tools/record_scheduler.py) that covers the same behavior of the code under test with no
// `Graphiti` object, or none.
//
// The four tests of SchedulerHarness build no `Graphiti` object: they give the scheduler a SimpleNamespace. They replace
// scheduler.admit_episode, analyze_episode, commit_unit, and enrich_unit with `monkeypatch.setattr`. The rule of Part 3
// for a patched function has no form here: the real functions call graphiti._resolve_request_scope first, and
// GraphitiCore is a skeleton until layer 9, so no fake at the network boundary can serve them. They also need the fake
// `Graphiti` of the Harness, which C# cannot give: GraphitiCore is a sealed class, and its methods are not virtual.
// TODO(port): O10 test_window_limits_the_units_and_sequence_order_is_call_order. Covered in part:
//   window_more_calls_than_the_window (the window and the count of calls in flight), and the span attributes of each
//   _run_unit case (ingest.mode, ingest.seq, ingest.window, ingest.unit_id). The order of the sequence numbers, the
//   commits, and the enrichment wait for layer 9.
// TODO(port): O10 test_window_of_one_runs_one_unit_at_a_time. Covered in part: window_of_one and
//   window_of_one_with_one_call.
// TODO(port): O10 test_failed_unit_releases_its_slot_and_its_position. Covered in part: analysis_marks_the_unit_failed
//   (the analysis that marks the unit failed and raises), cancel_a_call_that_waits_for_the_window (the slot), and the
//   end of each _run_unit case, where no heartbeat follows the end of the unit. The other units of the window commit,
//   which waits for layer 9.
// TODO(port): O10 test_heartbeat_renews_the_deadline_of_a_slow_unit. Covered: analysis_fails_after_heartbeats and
//   heartbeat_renews. The analysis that returns and the commit wait for layer 9.
//
// The tests of the dispatch in `Graphiti` build a `Graphiti` object with make_graphiti and call Graphiti.add_episode,
// Graphiti.add_episode_bulk, or Graphiti.close. Each one waits for layer 9:
// - test_mode_off_runs_serial_ingestion_and_makes_no_scheduler: none.
// - test_enabled_for_group_selects_the_path: none.
// - test_update_communities_is_rejected_on_the_concurrent_path: none.
// - test_bulk_call_uses_the_scheduler_only_when_the_mode_is_on: none.
// - test_close_closes_the_state_store: init_with_a_store and init_with_defaults (ConcurrentIngestor.close).
// - test_serial_ingestion_does_not_import_the_ingestion_package: none. It tests the Python import of a module, which
//   has no C# form (R19).
namespace Graphiti.Port.Evaluation;
