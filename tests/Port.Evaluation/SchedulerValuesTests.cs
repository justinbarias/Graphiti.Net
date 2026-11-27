// Evaluation test for graphiti_core/ingestion/scheduler.py (port/RULES.md, R23, R25). Packet P8d of plan 0004, layer 8.
// values/scheduler.json holds the cases that port/tools/record_scheduler.py recorded from Python: each path of
// ConcurrentIngestor that a prepared state store, a fake clock, and prepared units reach with no method of `Graphiti`.
// The test runs the steps of each case on the port with the same fakes, and compares the store calls, the calls of the
// fake analysis, the log lines, and the snapshots, in one ordered list.
//
// Paths that wait for layer 9: after the admit lock, add_episode and add_episode_bulk call admit_episode and admit_bulk,
// and an analysis that returns calls commit_unit and enrich_unit. Each one calls graphiti._resolve_request_scope first,
// and GraphitiCore is a skeleton until layer 9. The only member of `Graphiti` that the recorded paths read is `tracer`,
// so the test gives them an object that no constructor made, with a tracer.
namespace Graphiti.Port.Evaluation;

using System.Runtime.CompilerServices;
using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.PhasesValues;

public class SchedulerValuesTests
{
    private const int Drain = 100;

    private static readonly PyDateTime T0 = PyDateTime.FromIsoFormat("2026-01-01T00:00:00+00:00");

    private static readonly List<OrderedDictionary<string, object?>> Cases = LoadCases();

    // The cancel points where the port and Python differ: the late cancel of GAPS F33. Each one is a cancel that arrives
    // in a call after which the main call awaits only the cleanup of _run_unit. Python delivers task.cancel() at the next
    // suspension of the task, which is inside that cleanup: the store call of _fail_unit, or the gather of the heartbeat
    // task in `finally`. The port reads the token only at its next check, and the cleanup passes CancellationToken.None
    // (R8). So at the last call before the error (6 points) the port makes the store call of _fail_unit, which Python
    // cancels, and at the store call of _fail_unit (7 points) the port ends with the error of the analysis, where Python
    // ends with CancelledError. The count of attempts is the same at each point. The value is the result of the port at
    // that point. A listed point that agrees with Python fails the test.
    private static readonly Dictionary<string, string> LateCancel = new(StringComparer.Ordinal)
    {
        ["analysis_fails:1"] = """{"k": 1, "attempts": 3, "count": 3, "error": "RuntimeError", "after": [{"kind": "store", "method": "fail", "args": ["g", 1, "<type>", null]}]}""",
        ["analysis_fails:2"] = """{"k": 2, "attempts": 3, "count": 3, "error": "RuntimeError", "after": []}""",
        ["analysis_raises_a_value_error:2"] = """{"k": 2, "attempts": 4, "count": 4, "error": "ValueError", "after": [{"kind": "store", "method": "fail", "args": ["g", 1, "<type>", null]}]}""",
        ["analysis_raises_a_value_error:3"] = """{"k": 3, "attempts": 4, "count": 4, "error": "ValueError", "after": []}""",
        ["analysis_fails_after_heartbeats:4"] = """{"k": 4, "attempts": 6, "count": 6, "error": "RuntimeError", "after": [{"kind": "store", "method": "fail", "args": ["g", 1, "<type>", null]}]}""",
        ["analysis_fails_after_heartbeats:5"] = """{"k": 5, "attempts": 6, "count": 6, "error": "RuntimeError", "after": []}""",
        ["analysis_marks_the_unit_failed:2"] = """{"k": 2, "attempts": 4, "count": 4, "error": "RuntimeError", "after": [{"kind": "store", "method": "fail", "args": ["g", 1, "<type>", null]}]}""",
        ["analysis_marks_the_unit_failed:3"] = """{"k": 3, "attempts": 4, "count": 4, "error": "RuntimeError", "after": []}""",
        ["first_heartbeat_store_error:0"] = """{"k": 0, "attempts": 2, "count": 2, "error": "RuntimeError", "after": [{"kind": "store", "method": "fail", "args": ["g", 1, "<type>", null]}]}""",
        ["first_heartbeat_store_error:1"] = """{"k": 1, "attempts": 2, "count": 2, "error": "RuntimeError", "after": []}""",
        ["turn_of_a_different_attempt:1"] = """{"k": 1, "attempts": 3, "count": 3, "error": "RuntimeError", "after": [{"kind": "store", "method": "fail", "args": ["g", 1, "<type>", null]}]}""",
        ["turn_of_a_different_attempt:2"] = """{"k": 2, "attempts": 3, "count": 3, "error": "RuntimeError", "after": []}""",
        ["heartbeat_error_during_the_analysis:4"] = """{"k": 4, "attempts": 5, "count": 5, "error": "RuntimeError", "after": []}""",
    };

    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    public static TheoryData<int, int> CancelPoints()
    {
        var data = new TheoryData<int, int>();
        for (var i = 0; i < Cases.Count; i++)
        {
            foreach (var point in Dict(Cases[i]["out"]).GetValueOrDefault("cancel") as List<object?> ?? [])
            {
                data.Add(i, (int)(long)Dict(point)["k"]!);
            }
        }

        return data;
    }

    [Fact]
    public void The_recording_reaches_each_path_that_the_port_can_reach()
    {
        var ops = Cases.SelectMany(c => List(Dict(c["in"])["actions"]).Select(a => (string)List(a)[0]!)).ToHashSet(StringComparer.Ordinal);
        foreach (var op in new[] { "init", "group", "notify_view", "wait", "call_wait", "heartbeat", "run_unit", "add", "add_bulk", "close", "cancel" })
        {
            Assert.Contains(op, ops);
        }

        Assert.Contains(Cases, c => Dict(c["in"]).ContainsKey("cancel"));
    }

    [Theory(Timeout = 10000)]
    [MemberData(nameof(CaseIndexes))]
    public async Task The_port_makes_each_recorded_call_and_gives_each_result(int index)
    {
        var input = Dict(Cases[index]["in"]);
        var (run, output) = await RunAsync(input, -1, Dict(Cases[index]["out"]), TestContext.Current.CancellationToken);
        var expected = new OrderedDictionary<string, object?>(Dict(Cases[index]["out"]));
        expected.Remove("cancel");
        var expectedEvents = List(expected["events"]);
        var actualEvents = List(output["events"]);
        for (var i = 0; i < Math.Min(expectedEvents.Count, actualEvents.Count); i++)
        {
            Assert.True(PyJson.Dumps(expectedEvents[i]) == PyJson.Dumps(actualEvents[i]), $"event {i}\n{PyJson.Dumps(expectedEvents[i])}\n{PyJson.Dumps(actualEvents[i])}");
        }

        Assert.Equal(PyJson.Dumps(expected), PyJson.Dumps(output));

        // Each store call and each call of the fake analysis got the token of its call (R25). A store call of the
        // heartbeat task gets the token of that task, and the store call of _fail_unit gets CancellationToken.None (R8).
        var callTokens = run.Calls.Values.Select(c => c.Cancel.Token).ToList();
        foreach (var (method, token) in run.Tokens)
        {
            if (callTokens.Contains(token) || (method == "close" && token == TestContext.Current.CancellationToken))
            {
                continue;
            }

            if (method == "fail")
            {
                Assert.Equal(CancellationToken.None, token);
            }
            else
            {
                Assert.Equal("heartbeat", method);
                Assert.True(token.CanBeCanceled);
            }
        }
    }

    // The theory that cancels at call k: the fake that makes call k records it and cancels the main call. Python ran the
    // same cancel. The test compares the count of attempts, the end of the main call, and the fake calls after call k.
    [Theory(Timeout = 10000)]
    [MemberData(nameof(CancelPoints))]
    public async Task Each_later_awaited_call_gets_the_token_and_no_call_follows_a_cancel(int index, int k)
    {
        var input = Dict(Cases[index]["in"]);
        var expected = Dict(List(Dict(Cases[index]["out"])["cancel"]).Select(Dict).Single(p => (long)p["k"]! == k));
        var (_, output) = await RunAsync(input, k, null, TestContext.Current.CancellationToken);
        var counted = List(output["events"]).Select(Dict).Where(e => (string)e["kind"]! is "store" or "analyze").Select(e => (object?)e).ToList();
        var main = List(List(output["calls"]).Select(List).Single(c => (string)c[0]! == (string)input["main"]!)[1]);
        var actual = new OrderedDictionary<string, object?>
        {
            ["k"] = (long)k,
            ["attempts"] = output["attempts"],
            ["count"] = output["count"],
            ["error"] = main[0],
            ["after"] = counted.Skip(k + 1).ToList(),
        };
        var key = $"{(string)input["name"]!}:{k}";
        if (LateCancel.TryGetValue(key, out var port))
        {
            Assert.NotEqual(PyJson.Dumps(expected), PyJson.Dumps(actual));
            Assert.Equal(port, PyJson.Dumps(actual));
            return;
        }

        Assert.True(PyJson.Dumps(expected) == PyJson.Dumps(actual), PyJson.Dumps(expected) + "\n" + PyJson.Dumps(actual));
    }

    // A test with no Python original: each async method with a canceled token throws OperationCanceledException (R8)
    // and makes no fake call with that token. _run_unit then marks the unit failed with CancellationToken.None (R8).
    [Theory(Timeout = 10000)]
    [InlineData("add_episode")]
    [InlineData("add_episode_bulk")]
    [InlineData("_run_unit")]
    [InlineData("_wait_local")]
    [InlineData("_heartbeat")]
    [InlineData("close")]
    public async Task Each_async_method_stops_at_a_canceled_token(string method)
    {
        var input = new OrderedDictionary<string, object?> { ["name"] = method, ["main"] = "m" };
        var run = new SchedulerRun(input, -1);
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        using (PortServices.Use(time: new VirtualTime(), loggerFactory: new PhasesLogCapture(run.Events)))
        {
            var error = await LoopGuard.RunAsync(async () =>
            {
                var inner = new InMemoryIngestionStateStore(run.Clock.Read);
                var admission = await inner.BeginAdmitAsync("g", "u1", 0.75, TestContext.Current.CancellationToken);
                await inner.EndAdmitAsync("g", Py.NotNone(admission, "seq").Seq, TestContext.Current.CancellationToken);
                var store = new SchedulerRun.RecordingStore(run, inner);
                var config = ConcurrentIngestionConfig.ModelValidate(new() { ["window"] = 2L, ["state_store"] = store, ["lease_seconds"] = 0.75, ["write_margin_seconds"] = 0.0 });
                var ingestor = new ConcurrentIngestor(Graphiti(new SchedulerTracer()), config);
                var token = source.Token;
                Task call = method switch
                {
                    "add_episode" => ingestor.AddEpisodeAsync(
                        "episode", "content", "test", T0, EpisodeType.Text, "g", null, null, null, null, null, null, null, null, null, cancellationToken: token),
                    "add_episode_bulk" => ingestor.AddEpisodeBulkAsync([], "g", null, null, null, null, false, null, null, true, cancellationToken: token),
                    "_run_unit" => ingestor.RunUnitAsync(ingestor.Group("g"), Unit("u1", 1), new SchedulerSpan("add_episode"), (_, _) => throw new InvalidOperationException("no analysis"), null, null, null, null, token),
                    "_wait_local" => ingestor.WaitLocal(ingestor.Group("g"))(token),
                    "_heartbeat" => ingestor.HeartbeatAsync(new UnitLease(store, config, "g", 1), token),
                    _ => ingestor.CloseAsync(token),
                };
                try
                {
                    await call;
                    return null;
                }
                catch (Exception e)
                {
                    return e;
                }
            });
            Assert.IsAssignableFrom<OperationCanceledException>(error);
        }

        // The only recorded store call is the one of _fail_unit, with CancellationToken.None (R8).
        var recorded = run.Events.Select(Dict).Where(e => (string)e["kind"]! == "store").Select(e => (string)e["method"]!).ToList();
        Assert.Equal(method == "_run_unit" ? ["fail"] : [], recorded);
        Assert.Equal(recorded.Count, run.Count);
        Assert.All(run.Tokens.Where(t => t.Token != source.Token), t => Assert.Equal(("fail", CancellationToken.None), t));
    }

    private static List<OrderedDictionary<string, object?>> LoadCases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "scheduler.json");
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        return List(document["cases"]).Select(Dict).ToList();
    }

    private static GraphitiCore Graphiti(Tracer tracer)
    {
        // PORT: the recorder passes a SimpleNamespace with `tracer`, the one member of `Graphiti` that the recorded
        // paths read. The upstream test of the dispatch builds its object with Graphiti.__new__ in the same way.
        var graphiti = (GraphitiCore)RuntimeHelpers.GetUninitializedObject(typeof(GraphitiCore));
        graphiti.Tracer = tracer;
        return graphiti;
    }

    private static AdmittedUnit Unit(string unitId, long seq) => new()
    {
        GroupId = "g",
        UnitId = unitId,
        Seq = seq,
        CreatedAt = T0,
        Episodes = [],
        PreviousEpisodes = [],
    };

    private static List<object?> CallView(Task task)
    {
        if (!task.IsCompleted)
        {
            return ["pending"];
        }

        if (task.IsCanceled)
        {
            return ["CancelledError", string.Empty];
        }

        if (task.Exception is { } faulted)
        {
            var error = faulted.InnerException!;
            return error is OperationCanceledException ? ["CancelledError", string.Empty] : [PhasesValuesTests.PythonName(error), error.Message];
        }

        return ["ok", null];
    }

    private static List<object?> GroupsView(ConcurrentIngestor ingestor) =>
        [.. ingestor.groups.Select(e => (object?)new List<object?> { e.Key, (long)e.Value.Window.CurrentCount, e.Value.AdmitLock.CurrentCount == 0, e.Value.Changed.IsSet() })];

    private static List<object?> SpansView(SchedulerTracer tracer) =>
        [.. tracer.Spans.Select(s => (object?)new List<object?> { s.Name, Encode(s.Attributes), s.Status })];

    private static List<object?> CallsView(SchedulerRun run) =>
        [.. run.Calls.Select(e => (object?)new List<object?> { e.Key, CallView(e.Value.Task) })];

    private static Exception MakeError(string kind, AdmittedUnit unit) => kind switch
    {
        "runtime" => new InvalidOperationException("analysis failed"),
        "value" => new ArgumentException("bad value"),
        "expired" => new UnitExpiredException(unit.GroupId, unit.Seq),
        "stale" => new StaleCommitException(unit.GroupId, unit.Seq),
        _ => throw new ArgumentException(kind),
    };

    private static async Task<(SchedulerRun Run, OrderedDictionary<string, object?> Output)> RunAsync(
        OrderedDictionary<string, object?> input,
        int cancelAt,
        OrderedDictionary<string, object?>? expected,
        CancellationToken driver)
    {
        var run = new SchedulerRun(Dict(PyJson.Loads(PyJson.Dumps(input))), cancelAt);
        var time = new VirtualTime();
        OrderedDictionary<string, object?> output;
        using (PortServices.Use(time: time, loggerFactory: new PhasesLogCapture(run.Events)))
        {
            output = await LoopGuard.RunAsync(() => RunCaseAsync(run, time, expected, driver), 30);
        }

        output["events"] = run.Events;
        output["max_in_flight"] = (long)run.MaxInFlight;
        output["count"] = (long)run.Count;
        output["attempts"] = (long)run.Attempts;
        return (run, output);
    }

    private static async Task<OrderedDictionary<string, object?>> RunCaseAsync(
        SchedulerRun run,
        VirtualTime time,
        OrderedDictionary<string, object?>? expected,
        CancellationToken driver)
    {
        var input = run.Input;
        var caseConfig = Dict(input["config"]);
        var leaseSeconds = (double)caseConfig["lease_seconds"]!;
        var inner = new InMemoryIngestionStateStore(run.Clock.Read);
        var store = new SchedulerRun.RecordingStore(run, inner);
        foreach (var step in List(input["setup"]).Select(List))
        {
            switch ((string)step[0]!)
            {
                case "admit":
                    var admission = Py.NotNone(await inner.BeginAdmitAsync("g", (string)step[1]!, leaseSeconds, driver), "seq");
                    await inner.EndAdmitAsync("g", admission.Seq, driver);
                    break;
                case "advance":
                    run.Clock.Now += (double)step[1]!;
                    break;
                case "fail":
                    await inner.FailAsync("g", (long)step[1]!, (string)step[2]!, cancellationToken: driver);
                    break;
                case "begin_commit":
                    await inner.BeginCommitAsync("g", (long)step[1]!, leaseSeconds, driver);
                    break;
                default:
                    throw new ArgumentException((string)step[0]!);
            }
        }

        var tracer = new SchedulerTracer();
        var graphiti = Graphiti(tracer);
        var data = new OrderedDictionary<string, object?>();
        if (input.GetValueOrDefault("defaults") is true)
        {
            data["window"] = caseConfig["window"];
            data["state_store"] = store;
        }
        else
        {
            if (input.GetValueOrDefault("no_store") is not true)
            {
                data["state_store"] = store;
            }

            foreach (var (key, value) in caseConfig)
            {
                data[key] = value;
            }
        }

        var config = ConcurrentIngestionConfig.ModelValidate(data);
        var ingestor = new ConcurrentIngestor(graphiti, config);
        var gates = new Dictionary<long, AsyncEvent>();
        var seqOf = new Dictionary<string, long>(StringComparer.Ordinal);
        var waits = new Dictionary<string, Func<CancellationToken, Task>>(StringComparer.Ordinal);

        async Task<ChangeSet> AnalyzeFakeAsync(AdmittedUnit unit, List<object?> spec, CancellationToken cancellationToken)
        {
            foreach (var (index, step) in spec.Select(List).Index())
            {
                switch ((string)step[0]!)
                {
                    case "call":
                        run.Tokens.Add(("analyze", cancellationToken));
                        await run.EnterAsync("analyze", new() { ["seq"] = unit.Seq, ["step"] = (long)index }, cancellationToken);
                        break;
                    case "gate":
                        await gates[unit.Seq].WaitAsync(cancellationToken);
                        break;
                    case "sleep":
                        await Task.Delay(TimeSpan.FromSeconds((double)step[1]!), PortServices.Time, cancellationToken);
                        break;
                    case "mark_failed":
                        await ingestor.Store.FailAsync(unit.GroupId, unit.Seq, "analysis", cancellationToken: cancellationToken);
                        break;
                    case "raise":
                        throw MakeError((string)step[1]!, unit);
                    default:
                        throw new ArgumentException((string)step[0]!);
                }
            }

            throw new InvalidOperationException("an analysis that returns calls commit_unit, which waits for layer 9");
        }

        // The drain of the recorder: Python runs the ready tasks until none is left. A wake-up of a SemaphoreSlim waiter
        // with a token that can be canceled resumes on the thread pool first, and then on the scheduler (R8), so the port
        // can need a pause for it, and the length of that pause depends on the load of the machine. So the drain waits
        // until the state equals the state that Python recorded at that point: the next snapshot, or the calls and the
        // spans at the end. It waits at most 5 seconds, and the state must then stay the same for three more rounds. A
        // drain with no recorded state after it ends when the state is the same after three rounds.
        var expectedSnapshots = expected is null
            ? []
            : List(expected["events"]).Select(Dict).Where(e => (string)e["kind"]! == "snapshot").Select(e => PyJson.Dumps(e)).ToList();
        var snapshotIndex = 0;

        OrderedDictionary<string, object?> Snapshot() => new()
        {
            ["kind"] = "snapshot",
            ["calls"] = CallsView(run),
            ["groups"] = GroupsView(ingestor),
            ["spans"] = SpansView(tracer),
        };

        async Task DrainAsync(Func<string> state, string? target)
        {
            var last = string.Empty;
            var same = 0;
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (same < 3)
            {
                for (var i = 0; i < Drain; i++)
                {
                    await Task.Yield();
                }

                var now = state();
                same = now == last && (target is null || now == target || DateTime.UtcNow > deadline) ? same + 1 : 0;
                last = now;
                if (same < 3)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1), TimeProvider.System, driver);
                }
            }
        }

        string Progress() => PyJson.Dumps(new List<object?> { CallsView(run), GroupsView(ingestor), SpansView(tracer), (long)run.Events.Count });

        void Start(string id, Func<CancellationToken, Task> body)
        {
            var task = Asyncio.CreateTask(body, out var cancel);
            run.Calls[id] = (task, cancel);
        }

        List<RawEpisode> RawEpisodes(long count) =>
            [.. Enumerable.Range(0, (int)count).Select(i => new RawEpisode { Name = $"episode-{i}", Content = "content", SourceDescription = "test", Source = EpisodeType.Text, ReferenceTime = T0 })];

        var actions = List(input["actions"]).Select(List).ToList();
        foreach (var (index, action) in actions.Index())
        {
            switch ((string)action[0]!)
            {
                case "drain":
                    {
                        var next = index + 1 < actions.Count ? (string)actions[index + 1][0]! : null;
                        if (next == "snapshot" && snapshotIndex < expectedSnapshots.Count)
                        {
                            await DrainAsync(() => PyJson.Dumps(Snapshot()), expectedSnapshots[snapshotIndex]);
                        }
                        else
                        {
                            await DrainAsync(Progress, null);
                        }

                        break;
                    }

                case "loop":
                    time.Advance((double)action[1]!);
                    break;
                case "clock":
                    run.Clock.Now += (double)action[1]!;
                    break;
                case "snapshot":
                    run.Events.Add(Snapshot());
                    snapshotIndex++;
                    break;
                case "store":
                    run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "store_state", ["store"] = StoreView(inner) });
                    break;
                case "init":
                    run.Events.Add(new OrderedDictionary<string, object?>
                    {
                        ["kind"] = "init",
                        ["same_graphiti"] = ReferenceEquals(ingestor.Graphiti, graphiti),
                        ["same_config"] = ReferenceEquals(ingestor.Config, config),
                        ["store"] = ReferenceEquals(ingestor.Store, store) ? "given" : ingestor.Store.GetType().Name,
                        ["config_store"] = ReferenceEquals(ingestor.Store, config.StateStore),
                        ["groups"] = ingestor.groups.Keys.Select(k => (object?)k).ToList(),
                    });
                    break;
                case "group":
                    {
                        var groupId = (string)action[1]!;
                        var before = ingestor.groups.Keys.Select(k => (object?)k).ToList();
                        var first = ingestor.groups.GetValueOrDefault(groupId);
                        var group = ingestor.Group(groupId);
                        run.Events.Add(new OrderedDictionary<string, object?>
                        {
                            ["kind"] = "group",
                            ["group_id"] = groupId,
                            ["before"] = before,
                            ["same"] = ReferenceEquals(group, first),
                            ["window"] = (long)group.Window.CurrentCount,
                            ["admit_locked"] = group.AdmitLock.CurrentCount == 0,
                            ["changed"] = group.Changed.IsSet(),
                        });
                        break;
                    }

                case "notify_view":
                    {
                        var group = ingestor.Group((string)action[1]!);
                        var old = group.Changed;
                        group.Notify();
                        run.Events.Add(new OrderedDictionary<string, object?>
                        {
                            ["kind"] = "notify",
                            ["old_set"] = old.IsSet(),
                            ["new_set"] = group.Changed.IsSet(),
                            ["replaced"] = !ReferenceEquals(group.Changed, old),
                        });
                        break;
                    }

                case "notify":
                    ingestor.Group((string)action[1]!).Notify();
                    break;
                case "wait":
                    {
                        var wait = ingestor.WaitLocal(ingestor.Group((string)action[2]!));
                        Start((string)action[1]!, wait);
                        break;
                    }

                case "wait_fn":
                    waits[(string)action[1]!] = ingestor.WaitLocal(ingestor.Group((string)action[2]!));
                    break;
                case "call_wait":
                    Start((string)action[1]!, waits[(string)action[2]!]);
                    break;
                case "heartbeat":
                    {
                        var lease = new UnitLease(ingestor.Store, config, "g", (long)action[2]!) { Fence = (long?)action[3] };
                        Start((string)action[1]!, token => ingestor.HeartbeatAsync(lease, token));
                        break;
                    }

                case "run_unit":
                    {
                        var seq = (long)action[2]!;
                        var spec = List(action[4]);
                        gates[seq] = new AsyncEvent();
                        seqOf[(string)action[1]!] = seq;
                        var unit = Unit((string)action[3]!, seq);
                        var span = new SchedulerSpan("add_episode");
                        tracer.Spans.Add(span);
                        var state = ingestor.Group("g");
                        Start((string)action[1]!, token => ingestor.RunUnitAsync(state, unit, span, (u, t) => AnalyzeFakeAsync(u, spec, t), null, null, null, null, token));
                        break;
                    }

                case "release":
                    gates[seqOf[(string)action[1]!]].Set();
                    break;
                case "cancel":
                    run.Calls[(string)action[1]!].Cancel.Cancel();
                    break;
                case "hold_admit":
                    await ingestor.Group((string)action[1]!).AdmitLock.WaitAsync(driver);
                    break;
                case "hold_window":
                    await ingestor.Group((string)action[1]!).Window.WaitAsync(driver);
                    break;
                case "release_window":
                    ingestor.Group((string)action[1]!).Window.Release();
                    break;
                case "add":
                    {
                        var id = (string)action[1]!;
                        var groupId = (string)action[2]!;
                        if (action[3] is true)
                        {
                            // Each optional argument left out: `identity_properties` (R25).
                            Start(id, token => ingestor.AddEpisodeAsync(
                                name: $"episode-{id}",
                                episodeBody: "content",
                                sourceDescription: "test",
                                referenceTime: T0,
                                source: EpisodeType.Text,
                                groupId: groupId,
                                uuid: null,
                                entityTypes: null,
                                excludedEntityTypes: null,
                                previousEpisodeUuids: null,
                                edgeTypes: null,
                                edgeTypeMap: null,
                                customExtractionInstructions: null,
                                saga: null,
                                sagaPreviousEpisodeUuid: null,
                                cancellationToken: token));
                        }
                        else
                        {
                            Start(id, token => ingestor.AddEpisodeAsync(
                                name: $"episode-{id}",
                                episodeBody: "content",
                                sourceDescription: "test",
                                referenceTime: T0,
                                source: EpisodeType.Text,
                                groupId: groupId,
                                uuid: null,
                                entityTypes: null,
                                excludedEntityTypes: null,
                                previousEpisodeUuids: null,
                                edgeTypes: null,
                                edgeTypeMap: null,
                                customExtractionInstructions: null,
                                saga: null,
                                sagaPreviousEpisodeUuid: null,
                                identityProperties: new() { ["Person"] = ["email"] },
                                cancellationToken: token));
                        }

                        break;
                    }

                case "add_bulk":
                    {
                        var episodes = RawEpisodes((long)action[3]!);
                        var groupId = (string)action[2]!;
                        Start((string)action[1]!, token => ingestor.AddEpisodeBulkAsync(
                            bulkEpisodes: episodes,
                            groupId: groupId,
                            entityTypes: null,
                            excludedEntityTypes: null,
                            edgeTypes: null,
                            edgeTypeMap: null,
                            strictEdgeTypes: false,
                            customExtractionInstructions: null,
                            saga: null,
                            useCombinedExtraction: true,
                            cancellationToken: token));
                        break;
                    }

                case "close":
                    await ingestor.CloseAsync(driver);
                    run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "closed" });
                    break;
                default:
                    throw new ArgumentException((string)action[0]!);
            }
        }

        // The end of each case: cancel each call that has not ended, and release each admit lock of the driver.
        var pending = run.Calls.Where(e => !e.Value.Task.IsCompleted).Select(e => e.Key).ToList();
        foreach (var key in pending)
        {
            run.Calls[key].Cancel.Cancel();
        }

        if (expected is null)
        {
            await DrainAsync(Progress, null);
        }
        else
        {
            await DrainAsync(
                () => PyJson.Dumps(new List<object?> { CallsView(run), SpansView(tracer) }),
                PyJson.Dumps(new List<object?> { expected["calls"], expected["spans"] }));
        }

        foreach (var group in ingestor.groups.Values)
        {
            if (group.AdmitLock.CurrentCount == 0)
            {
                group.AdmitLock.Release();
            }
        }

        var output = new OrderedDictionary<string, object?>
        {
            ["pending_at_end"] = pending.Select(k => (object?)k).ToList(),
            ["calls"] = CallsView(run),
            ["groups"] = GroupsView(ingestor),
            ["spans"] = SpansView(tracer),
            ["max_spans"] = (long)tracer.MaxInFlight,
            ["store"] = StoreView(inner),
        };
        return output;
    }
}
