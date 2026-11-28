// Ported from tests/ingestion/test_scheduler.py (port/RULES.md, R25). The code under test is
// graphiti_core/ingestion/scheduler.py and the dispatch in graphiti_core/graphiti.py. Packets P8d and P9b of plan 0004.
//
// The four tests of SchedulerHarness replace scheduler.admit_episode, analyze_episode, commit_unit, and enrich_unit with
// `monkeypatch.setattr`, and the Harness replaces phases.add_nodes_and_edges_bulk, execute_write_with_timeout, and
// extract_attributes_from_nodes. No fake at the network boundary gives the result of these patches (Part 3, the row of
// `monkeypatch.setattr`): the fake analysis makes one node from no model call, and the fake writes apply each record to
// the in-memory graph of the Harness. Each line names the recorded case that covers the same behavior with the real
// functions and a real GraphitiCore (values/scheduler.json, key `graphiti_cases`, port/tools/record_scheduler.py).
// TODO(port): O10 test_window_limits_the_units_and_sequence_order_is_call_order. Covered: three_calls_window_of_two (the
//   sequence numbers in call order, the commits, the enrichment, and the span attributes), and
//   window_more_calls_than_the_window (the window).
// TODO(port): O10 test_window_of_one_runs_one_unit_at_a_time. Covered: three_calls_window_of_one and window_of_one.
// TODO(port): O10 test_failed_unit_releases_its_slot_and_its_position. Covered: analysis_fails, admit_fails (the next call
//   gets the admit lock and commits), and analysis_marks_the_unit_failed.
// TODO(port): O10 test_heartbeat_renews_the_deadline_of_a_slow_unit. Covered: analysis_fails_after_heartbeats,
//   heartbeat_renews, and commit_turn_wait.
//
// The tests of the dispatch in `Graphiti` patch ConcurrentIngestor.add_episode or add_episode_bulk. Here the real
// scheduler runs on the fakes of GraphitiRun, and the first store call of the scheduler (begin_admit) shows the path.
// test_serial_ingestion_does_not_import_the_ingestion_package has no C# form: it tests the Python import of a module
// (R19).
namespace Graphiti.Port.Evaluation;

using System.Reflection;
using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.PhasesValues;

public class TestScheduler
{
    private static readonly PyDateTime T0 = PyDateTime.FromIsoFormat("2026-01-01T00:00:00+00:00");

    [Fact(Timeout = 10000)]
    public async Task test_mode_off_runs_serial_ingestion_and_makes_no_scheduler()
    {
        // PORT: SerialPathRan is the RuntimeError of the fake driver at its first query: the serial path reads the previous
        // episodes first. The test of `del graphiti.concurrent_ingestion` (an object with no call of __init__) has no C#
        // form: each GraphitiCore has the field.
        var run = new IngestRun(Input(), -1);
        using (PortServices.Use(time: new VirtualTime(), loggerFactory: new IngestLogCapture(run.Run.Events)))
        {
            var graphiti = run.Run.MakeGraphiti(new());
            Assert.IsType<InvalidOperationException>(await AddEpisodeAsync(graphiti, TestContext.Current.CancellationToken));
            Assert.Null(Ingestor(graphiti));
        }

        Assert.DoesNotContain(run.Run.Events.Select(Dict), e => (string)e["kind"]! == "store");
    }

    [Fact(Timeout = 10000)]
    public async Task test_enabled_for_group_selects_the_path()
    {
        // PORT: the patched ConcurrentIngestor.add_episode returned "concurrent result". The real scheduler admits the unit
        // of the group (the store call begin_admit of group g), and the admission then reads the previous episodes.
        // The saga argument reaches commit_unit: phases case commit_with_a_saga_name covers it.
        var run = new IngestRun(Input(), -1);
        using (PortServices.Use(time: new VirtualTime(), loggerFactory: new IngestLogCapture(run.Run.Events)))
        {
            var (graphiti, config) = Concurrent(run);
            config.EnabledForGroup = groupId => groupId == "g";
            Assert.IsType<InvalidOperationException>(await AddEpisodeAsync(graphiti, TestContext.Current.CancellationToken, new SagaOrName.Name { Value = "thread" }));
            Assert.NotNull(Ingestor(graphiti));
            Assert.Equal(["g"], BeginAdmitGroups(run));

            config.EnabledForGroup = _ => false;
            var disabled = run.Run.MakeGraphiti(new());
            disabled.ConcurrentIngestion = config;
            Assert.IsType<InvalidOperationException>(await AddEpisodeAsync(disabled, TestContext.Current.CancellationToken));

            // The scheduler and the state store are not made for a graph that does not use the mode.
            Assert.Null(Ingestor(disabled));
            Assert.Equal(["g"], BeginAdmitGroups(run));
        }
    }

    [Fact(Timeout = 10000)]
    public async Task test_update_communities_is_rejected_on_the_concurrent_path()
    {
        var run = new IngestRun(Input(), -1);
        using (PortServices.Use(time: new VirtualTime(), loggerFactory: new IngestLogCapture(run.Run.Events)))
        {
            var (graphiti, _) = Concurrent(run);
            var error = Assert.IsType<ArgumentException>(await AddEpisodeAsync(graphiti, TestContext.Current.CancellationToken, updateCommunities: true));
            Assert.Contains("update_communities", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task test_bulk_call_uses_the_scheduler_only_when_the_mode_is_on()
    {
        // PORT: the patched ConcurrentIngestor.add_episode_bulk returned "concurrent result". The real scheduler admits the
        // unit of the group. `use_combined_extraction` reaches analyze_bulk: phases case analyze_bulk covers the default.
        var run = new IngestRun(Input(), -1);
        using (PortServices.Use(time: new VirtualTime(), loggerFactory: new IngestLogCapture(run.Run.Events)))
        {
            var (graphiti, _) = Concurrent(run);
            await LoopGuard.RunAsync(async () =>
            {
                try
                {
                    await graphiti.AddEpisodeBulkAsync([], "g", cancellationToken: TestContext.Current.CancellationToken);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    // The empty unit can fail after the admission. The test reads only the path.
                }
            });
            Assert.NotNull(Ingestor(graphiti));
            Assert.Equal(["g"], BeginAdmitGroups(run));
        }
    }

    [Fact(Timeout = 10000)]
    public async Task test_close_closes_the_state_store()
    {
        var run = new IngestRun(Input(), -1);
        using (PortServices.Use(time: new VirtualTime(), loggerFactory: new IngestLogCapture(run.Run.Events)))
        {
            var (graphiti, _) = Concurrent(run);
            await LoopGuard.RunAsync(() => graphiti.CloseAsync(TestContext.Current.CancellationToken));
        }

        var calls = run.Run.Events.Select(Dict).Select(e => (string)e["kind"]! == "store" ? "store:" + (string)e["method"]! : (string)e["kind"]!).ToList();
        Assert.Single(calls, c => c == "store:close");
        Assert.Single(calls, c => c == "driver_close");
    }

    // Each query of the fake driver raises RuntimeError, so the first graph read of each path fails. The concurrent path
    // calls the state store (begin_admit) before its first query.
    private static OrderedDictionary<string, object?> Input() => new() { ["fail"] = new OrderedDictionary<string, object?> { ["query"] = 99L }, ["main"] = "m" };

    private static (GraphitiCore Graphiti, ConcurrentIngestionConfig Config) Concurrent(IngestRun run)
    {
        var graphiti = run.Run.MakeGraphiti(new() { ["concurrent_ingestion"] = new OrderedDictionary<string, object?> { ["window"] = 2L } });
        var config = graphiti.ConcurrentIngestion!;
        config.StateStore = new IngestRun.IngestStore(run, new InMemoryIngestionStateStore());
        return (graphiti, config);
    }

    private static async Task<Exception?> AddEpisodeAsync(GraphitiCore graphiti, CancellationToken token, SagaOrName? saga = null, bool updateCommunities = false) =>
        await LoopGuard.RunAsync(async () =>
        {
            try
            {
                await graphiti.AddEpisodeAsync(
                    "episode",
                    "content",
                    "test",
                    T0,
                    groupId: "g",
                    updateCommunities: updateCommunities,
                    saga: saga,
                    cancellationToken: token);
                return null;
            }
            catch (Exception e)
            {
                return e;
            }
        });

    private static object? Ingestor(GraphitiCore graphiti) =>
        typeof(GraphitiCore).GetField("concurrentIngestor", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(graphiti);

    private static List<string> BeginAdmitGroups(IngestRun run) =>
        [.. run.Run.Events.Select(Dict).Where(e => (string)e["kind"]! == "store" && (string)e["method"]! == "begin_admit").Select(e => (string)List(e["args"])[0]!)];
}
