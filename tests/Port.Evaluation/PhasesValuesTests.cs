// Evaluation test for graphiti_core/ingestion/phases.py (port/RULES.md, R23, R25).
// values/phases.json holds the calls that port/tools/record_phases.py recorded from Python: the sync functions, and the
// async functions that the port can reach with no `Graphiti` method, with fakes for the LLM client, the embedder, the
// graph driver with its session and its transaction, and the graph reader. The state store is the real in-memory store
// in a recording wrapper. The calls, the store calls, and the log lines of each case are one ordered list.
//
// admit_episode, admit_bulk, analyze_episode, analyze_bulk, commit_unit, enrich_unit, and _save_analysis call a method
// of `Graphiti` first (graphiti._resolve_request_scope), and GraphitiCore is a skeleton until layer 9. They wait for
// layer 9, and so do the paths of _build_saga_links that call graphiti._find_saga or
// graphiti._saga_get_previous_episode_uuid. The other paths of _build_saga_links and _written_episode read no member of
// `Graphiti` but its `store_raw_episode_content`, so the test gives them an object that no constructor made.
namespace Graphiti.Port.Evaluation;

using System.Runtime.CompilerServices;
using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.PhasesValues;

public class PhasesValuesTests
{
    private static readonly List<OrderedDictionary<string, object?>> SyncCases =
        List(PhasesValues.Document["cases"]).Select(Dict).ToList();

    private static readonly List<OrderedDictionary<string, object?>> AsyncCases =
        List(PhasesValues.Document["async_cases"]).Select(Dict).ToList();

    // The cancel points where the port makes more attempts than Python: the late cancel of GAPS F33. The key is the name
    // of the case and k, and the value is the count of the extra attempts. Python raises CancelledError where the caller
    // resumes after call k (the end of a gather, or the return of the transaction function and the `close` in `finally`,
    // whose own sleep then raises). The port reads the token only at its next fake call, which throws at its token check
    // and records nothing. The error and the recorded calls after call k agree. A listed point that agrees with Python
    // fails the test.
    private static readonly Dictionary<string, long> LateCancel = new(StringComparer.Ordinal)
    {
        ["c3_exact:3"] = 1,
        ["c5_exact:1"] = 1,
        ["operations:7"] = 2,
        ["first_attempt_fails:4"] = 1,
        ["first_attempt_fails:13"] = 1,
        ["recovered:6"] = 2,
        ["groups_bulk_False:1"] = 1,
        ["groups_bulk_False:2"] = 2,
        ["groups_bulk_False:3"] = 1,
    };

    public static TheoryData<int> SyncCaseIndexes() => Indexes(SyncCases.Count);

    public static TheoryData<int> AsyncCaseIndexes() => Indexes(AsyncCases.Count);

    // The async cases with at least one counted fake call: a canceled token must stop them (R8, R25).
    public static TheoryData<int> CancelableCaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < AsyncCases.Count; i++)
        {
            if ((long)Dict(AsyncCases[i]["out"])["count"]! > 0)
            {
                data.Add(i);
            }
        }

        return data;
    }

    public static TheoryData<int, int> CancelPoints()
    {
        var data = new TheoryData<int, int>();
        for (var i = 0; i < AsyncCases.Count; i++)
        {
            foreach (var point in Dict(AsyncCases[i]["out"]).GetValueOrDefault("cancel") as List<object?> ?? [])
            {
                data.Add(i, (int)(long)Dict(point)["k"]!);
            }
        }

        return data;
    }

    // R4: the Python name of the class of an exception that the port throws.
    internal static string PythonName(Exception e) => e switch
    {
        OperationCanceledException => "CancelledError",
        InvalidOperationException => "RuntimeError",
        KeyNotFoundException => "LookupError",
        ValidationException => "ValidationError",
        ArgumentException => "ValueError",
        UnitExpiredException => "UnitExpiredError",
        StaleCommitException => "StaleCommitError",
        UnitInProgressException => "UnitInProgressError",
        NodeNotFoundException => "NodeNotFoundError",
        _ => e.GetType().Name,
    };

    [Fact]
    public void The_report_keys_are_the_constant_of_Python()
    {
        var expected = List(Dict(PhasesValues.Document["constants"])["REPORT_KEYS"]);
        Assert.Equal(expected.Cast<string>(), Phases.ReportKeys);
    }

    [Fact]
    public void The_recording_reaches_each_function_that_the_port_can_reach()
    {
        var names = SyncCases.Concat(AsyncCases).Select(c => (string)Dict(c["in"])["fn"]!).ToHashSet(StringComparer.Ordinal);
        foreach (var name in new[]
        {
            "new_report", "_default_edge_type_map", "_written_episode", "_episode_uuid", "_unique_by_uuid_nodes",
            "_unique_by_uuid_edges", "_unique_episodic_edges", "_replace_entity_edges", "_episode_context", "_remap_edges",
            "_move_end_date_to_earliest_contradiction", "_check_driver", "UnitLease", "UnitLease.heartbeat", "_begin_admit",
            "_fail_unit", "_fail_admission", "_written_episodes", "_episodes_with_stored_links", "rebase_change_set",
            "apply_change_set", "_write_saga_links", "_apply_with_retries", "_mark_apply_failed", "_recover_unit",
            "_build_saga_links", "_hydrate_nodes",
        })
        {
            Assert.Contains(name, names);
        }
    }

    [Theory]
    [MemberData(nameof(SyncCaseIndexes))]
    public void The_port_gives_each_recorded_sync_result(int index)
    {
        var input = Dict(SyncCases[index]["in"]);
        var expected = Dict(SyncCases[index]["out"]);
        var events = new List<object?>();
        var output = new OrderedDictionary<string, object?>();
        using (PortServices.Use(loggerFactory: new PhasesLogCapture(events), time: new MaintenanceFixedClock(FixedNow.ToDateTimeOffset())))
        {
            try
            {
                output["ok"] = RunSync(input);
            }
            catch (Exception e)
            {
                output["error"] = new OrderedDictionary<string, object?> { ["type"] = PythonName(e), ["text"] = e.Message };
            }
        }

        output["events"] = events;
        if ((string)input["fn"]! == "new_report")
        {
            // PORT: Python stores the int 0, and the port the double 0 (the report is a dict[str, float]). The test
            // compares the keys, their order, and the values.
            var rows = List(Dict(expected["ok"])["report"]).Select(List).ToList();
            var report = Phases.NewReport();
            Assert.Equal(rows.Select(r => (string)r[0]!), report.Keys);
            Assert.All(rows, r => Assert.Equal((long)r[1]!, report[(string)r[0]!]));
            return;
        }

        Assert.Equal(PyJson.Dumps(expected), PyJson.Dumps(output));
    }

    [Theory(Timeout = 10000)]
    [MemberData(nameof(AsyncCaseIndexes))]
    public async Task The_port_makes_each_recorded_call_and_gives_each_result(int index)
    {
        var input = Dict(AsyncCases[index]["in"]);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var (run, output) = await RunAsync(input, source, -1);
        var expected = new OrderedDictionary<string, object?>(Dict(AsyncCases[index]["out"]));
        expected.Remove("cancel");
        var expectedEvents = List(expected["events"]);
        var actualEvents = List(output["events"]);
        for (var i = 0; i < Math.Min(expectedEvents.Count, actualEvents.Count); i++)
        {
            Assert.Equal(PyJson.Dumps(expectedEvents[i]), PyJson.Dumps(actualEvents[i]));
        }

        Assert.Equal(PyJson.Dumps(expected), PyJson.Dumps(output));

        // Each fake call got the token of the call (R25). A saga save runs in asyncio.wait_for with the write budget
        // (R8), so its query gets the token of the limit, which the token of the call also cancels.
        foreach (var (kind, token) in run.Tokens)
        {
            if (token != source.Token)
            {
                Assert.Equal("query", kind);
                Assert.True(token.CanBeCanceled);
            }
        }
    }

    // The theory that cancels at call k: the fake that makes call k records it and cancels the token source. Python ran
    // the same cancel. The test compares the count of attempts, the error, and the fake calls after call k.
    [Theory(Timeout = 10000)]
    [MemberData(nameof(CancelPoints))]
    public async Task Each_later_awaited_call_gets_the_token_and_no_call_follows_a_cancel(int index, int k)
    {
        var input = Dict(AsyncCases[index]["in"]);
        var expected = Dict(List(Dict(AsyncCases[index]["out"])["cancel"]).Select(Dict).Single(p => (long)p["k"]! == k));
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var (run, output) = await RunAsync(input, source, k);
        var counted = List(output["events"]).Select(Dict)
            .Where(e => (string)e["kind"]! is not ("store" or "log" or "session" or "close" or "rollback" or "budget" or "cancelled"))
            .Select(e => (object?)e)
            .ToList();
        var actual = new OrderedDictionary<string, object?>
        {
            ["k"] = (long)k,
            ["attempts"] = (long)run.Attempts,
            ["count"] = (long)run.Count,
            ["error"] = output.GetValueOrDefault("error") is OrderedDictionary<string, object?> error ? error["type"] : null,
            ["after"] = counted.Skip(k + 1).ToList(),
        };
        var key = $"{(string?)input.GetValueOrDefault("name") ?? (string)input["fn"]!}:{k}";
        if (LateCancel.TryGetValue(key, out var extra))
        {
            Assert.Equal((long)expected["attempts"]! + extra, actual["attempts"]);
            actual["attempts"] = expected["attempts"];
        }

        Assert.True(PyJson.Dumps(expected) == PyJson.Dumps(actual), PyJson.Dumps(expected) + "\n" + PyJson.Dumps(actual) + "\n" + PyJson.Dumps(output["events"]));
    }

    // A test with no Python original: each async function with a canceled token throws OperationCanceledException (R8)
    // and makes no counted fake call.
    [Theory(Timeout = 10000)]
    [MemberData(nameof(CancelableCaseIndexes))]
    public async Task Each_async_function_stops_at_a_canceled_token(int index)
    {
        var input = Dict(AsyncCases[index]["in"]);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var (run, output) = await RunAsync(input, source, -1);
        Assert.Equal("CancelledError", Dict(output["error"])["type"]);
        Assert.Equal(0, run.Count);
    }

    private static TheoryData<int> Indexes(int count)
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    private static async Task<(PhasesRun Run, OrderedDictionary<string, object?> Output)> RunAsync(
        OrderedDictionary<string, object?> input,
        CancellationTokenSource source,
        int cancelAt)
    {
        var run = new PhasesRun(input, source, cancelAt);
        var output = new OrderedDictionary<string, object?>();
        using (PortServices.Use(loggerFactory: new PhasesLogCapture(run.Events), time: new MaintenanceFixedClock(FixedNow.ToDateTimeOffset())))
        {
            try
            {
                output["ok"] = await LoopGuard.RunAsync(() => RunCaseAsync(run, input, source.Token));
            }
            catch (Exception e)
            {
                output["error"] = new OrderedDictionary<string, object?>
                {
                    ["type"] = PythonName(e),
                    ["text"] = e is OperationCanceledException ? string.Empty : e.Message,
                };
            }

            // asyncio.run runs the tasks that a failed call leaves to their end. The port gives them turns (R25).
            await LoopGuard.RunAsync(async () =>
            {
                for (var i = 0; i < 50; i++)
                {
                    await Task.Yield();
                }
            });
        }

        output["events"] = run.Events;
        output["max_in_flight"] = (long)run.MaxInFlight;
        output["count"] = (long)run.Count;
        output["attempts"] = (long)run.Attempts;
        return (run, Dict(Placeholders(output, [])));
    }

    private static GraphitiCore Graphiti(bool storeRaw)
    {
        // PORT: the Python test passes a SimpleNamespace with the one attribute that the function reads.
        var graphiti = (GraphitiCore)RuntimeHelpers.GetUninitializedObject(typeof(GraphitiCore));
        graphiti.StoreRawEpisodeContent = storeRaw;
        return graphiti;
    }

    private static OrderedDictionary<string, object?> RunSync(OrderedDictionary<string, object?> input)
    {
        var fn = (string)input["fn"]!;
        switch (fn)
        {
            case "_default_edge_type_map":
                {
                    OrderedDictionary<string, GraphTypeDescriptor>? types = null;
                    if (input["edge_types"] is List<object?> names)
                    {
                        types = new();
                        foreach (var name in names.Cast<string>())
                        {
                            types[name] = new GraphTypeDescriptor(name, null, []);
                        }
                    }

                    var result = Phases.DefaultEdgeTypeMap(types);
                    return new()
                    {
                        ["map"] = result.Select(e => (object?)new List<object?>
                    {
                        new List<object?> { e.Key.Item1, e.Key.Item2 },
                        e.Value.Select(x => (object?)x).ToList(),
                    }).ToList(),
                    };
                }

            case "_written_episode":
                {
                    var episode = Episode(input["episode"]);
                    var written = Phases.WrittenEpisode(Graphiti((bool)input["store_raw"]!), episode, [.. List(input["entity_edges"]).Cast<string>()]);
                    return new() { ["written"] = EpisodeView(written), ["original"] = EpisodeView(episode), ["same"] = ReferenceEquals(written, episode) };
                }

            case "_episode_uuid":
                {
                    var args = List(input["args"]);
                    var uuid = args.Count == 2
                        ? Phases.EpisodeUuid((string)args[0]!, (string)args[1]!)
                        : Phases.EpisodeUuid((string)args[0]!, (string)args[1]!, (long)args[2]!);
                    return new() { ["uuid"] = uuid };
                }

            case "_unique_by_uuid_nodes":
                {
                    List<EntityNode> records = [.. List(input["records"]).Select(Node)];
                    return new() { ["result"] = Phases.UniqueByUuid(records).Select(r => (object?)new List<object?> { Py.IndexOf(records, r), r.Uuid }).ToList() };
                }

            case "_unique_by_uuid_edges":
                {
                    List<EntityEdge> records = [.. List(input["records"]).Select(Edge)];
                    return new() { ["result"] = Phases.UniqueByUuid(records).Select(r => (object?)new List<object?> { Py.IndexOf(records, r), r.Uuid }).ToList() };
                }

            case "_unique_episodic_edges":
                return new() { ["result"] = Phases.UniqueEpisodicEdges([.. List(input["edges"]).Select(EpisodicEdgeOf)]).Select(e => (object?)e.Uuid).ToList() };

            case "_replace_entity_edges":
                {
                    var changeSet = new ChangeSet
                    {
                        GroupId = "g",
                        UnitId = "u1",
                        Seq = 1,
                        SnapshotSeq = 0,
                        AnalysisStartedAt = 1000.0,
                        Episodes = [.. List(input["episodes"]).Select(Episode)],
                    };
                    var replaced = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var (key, value) in Dict(input["replaced"]))
                    {
                        replaced[key] = (string)value!;
                    }

                    HashSet<string>? dropped = input.GetValueOrDefault("dropped") is List<object?> d ? d.Cast<string>().ToHashSet(StringComparer.Ordinal) : null;
                    List<string>? added = input.GetValueOrDefault("added") is List<object?> a ? [.. a.Cast<string>()] : null;
                    if (input.ContainsKey("dropped") && input.ContainsKey("added"))
                    {
                        Phases.ReplaceEntityEdges(changeSet, replaced, dropped, added);
                    }
                    else if (input.ContainsKey("dropped"))
                    {
                        Phases.ReplaceEntityEdges(changeSet, replaced, dropped);
                    }
                    else if (input.ContainsKey("added"))
                    {
                        Phases.ReplaceEntityEdges(changeSet, replaced, added: added);
                    }
                    else
                    {
                        Phases.ReplaceEntityEdges(changeSet, replaced);
                    }

                    return new() { ["episodes"] = changeSet.Episodes.Select(e => (object?)e.EntityEdges.Select(x => (object?)x).ToList()).ToList() };
                }

            case "_episode_context":
                {
                    var context = Phases.EpisodeContext(Admitted(input["unit"]));
                    return new()
                    {
                        ["context"] = context.Select(e => (object?)new List<object?>
                    {
                        e.Key,
                        e.Value.Item1.Uuid,
                        e.Value.Item1.Content,
                        e.Value.Item2.Select(p => (object?)p.Uuid).ToList(),
                    }).ToList(),
                    };
                }

            case "_remap_edges":
                {
                    var changeSet = ChangeSetOf(input["change_set"]);
                    var report = Phases.NewReport();
                    Phases.RemapEdges(changeSet, report);
                    return new() { ["change_set"] = changeSet.ModelDump(json: true), ["report"] = ReportView(report) };
                }

            case "_move_end_date_to_earliest_contradiction":
                {
                    var edge = Edge(input["edge"]);
                    Phases.MoveEndDateToEarliestContradiction(edge, [.. List(input["contradicted"]).Select(Edge)]);
                    return new() { ["invalid_at"] = Dt(edge.InvalidAt) };
                }

            case "_check_driver":
                {
                    using var source = new CancellationTokenSource();
                    var run = new PhasesRun(input, source, -1);
                    Phases.CheckDriver(run.Driver);
                    return new();
                }

            case "UnitLease":
                {
                    var clock = new PhasesClock();
                    var store = new InMemoryIngestionStateStore(clock.Read);
                    var config = ConcurrentIngestionConfig.ModelValidate(new() { ["window"] = 4L });
                    var lease = input.GetValueOrDefault("default_clock") is true
                        ? new UnitLease(store, config, "g", 7)
                        : new UnitLease(store, config, "g", 7, clock.Read);
                    List<object?> steps = [new OrderedDictionary<string, object?> { ["fence"] = lease.Fence, ["group_id"] = lease.GroupId, ["seq"] = lease.Seq }];
                    foreach (var step in List(input["steps"]).Select(List))
                    {
                        switch ((string)step[0]!)
                        {
                            case "renewed":
                                lease.Renewed((double)step[1]!);
                                steps.Add(new OrderedDictionary<string, object?> { ["renewed"] = step[1] });
                                break;
                            case "clock":
                                clock.Now = (double)step[1]!;
                                break;
                            default:
                                try
                                {
                                    steps.Add(new OrderedDictionary<string, object?> { ["budget"] = lease.WriteBudget() });
                                }
                                catch (Exception e)
                                {
                                    steps.Add(new OrderedDictionary<string, object?> { ["error"] = new OrderedDictionary<string, object?> { ["type"] = PythonName(e), ["text"] = e.Message } });
                                }

                                break;
                        }
                    }

                    return new() { ["steps"] = steps };
                }

            default:
                throw new InvalidOperationException("unknown function " + fn);
        }
    }

    private static async Task<List<BeginCommitResult>> SetupStoreAsync(PhasesRun run, OrderedDictionary<string, object?> input)
    {
        List<BeginCommitResult> results = [];
        var store = run.Inner;
        foreach (var step in input.GetValueOrDefault("setup") as List<object?> ?? [])
        {
            var s = List(step);
            switch ((string)s[0]!)
            {
                case "admit":
                    {
                        var admission = await store.BeginAdmitAsync("g", (string)s[1]!, run.Config.LeaseSeconds);
                        await store.EndAdmitAsync("g", Py.NotNone(admission, "seq").Seq);
                        break;
                    }

                case "admitting":
                    await store.BeginAdmitAsync("g", (string)s[1]!, run.Config.LeaseSeconds);
                    break;
                case "analyze":
                    await store.StartAnalysisAsync("g", (long)s[1]!);
                    await store.SaveChangeSetAsync("g", (long)s[1]!, ChangeSetOf(s[2]).ToBytes());
                    break;
                case "begin_commit":
                    results.Add(await store.BeginCommitAsync("g", (long)s[1]!, run.Config.LeaseSeconds));
                    break;
                case "save_final":
                    await store.SaveFinalAsync("g", (long)s[1]!, (long)s[2]!, ChangeSetOf(s[3]).ToBytes());
                    break;
                case "finish_commit":
                    await store.FinishCommitAsync("g", (long)s[1]!, (long)s[2]!, [.. List(s[3]).Cast<string>()], run.Config.IndexLagSeconds);
                    break;
                case "fail":
                    await store.FailAsync("g", (long)s[1]!, (string)s[2]!, (long?)s[3]);
                    break;
                case "advance":
                    run.Clock.Now += (double)s[1]!;
                    break;
                default:
                    throw new InvalidOperationException((string)s[0]!);
            }
        }

        return results;
    }

    private static OrderedDictionary<string, object?> CommitView(BeginCommitResult result) => result switch
    {
        CommitTurn turn => new() { ["type"] = "CommitTurn", ["fence"] = turn.Fence, ["final"] = turn.FinalPayload is not null },
        CommitRecover recover => new() { ["type"] = "CommitRecover", ["seq"] = recover.Seq, ["fence"] = recover.Fence },
        _ => new() { ["type"] = "CommitWait" },
    };

    private static async Task<OrderedDictionary<string, object?>> RunCaseAsync(PhasesRun run, OrderedDictionary<string, object?> input, CancellationToken token)
    {
        var results = await SetupStoreAsync(run, input);
        var fn = (string)input["fn"]!;
        var store = run.Store;
        switch (fn)
        {
            case "UnitLease.heartbeat":
                {
                    var lease = new UnitLease(store, run.Config, "g", 1, run.Clock.Read) { Fence = (long?)input["fence"] };
                    List<object?> steps = [];
                    foreach (var step in List(input["steps"]).Cast<string>())
                    {
                        try
                        {
                            if (step == "heartbeat")
                            {
                                await lease.HeartbeatAsync(token);
                                steps.Add(new OrderedDictionary<string, object?> { ["heartbeat"] = true });
                            }
                            else
                            {
                                steps.Add(new OrderedDictionary<string, object?> { ["budget"] = lease.WriteBudget() });
                            }
                        }
                        catch (Exception e) when (e is not OperationCanceledException)
                        {
                            steps.Add(new OrderedDictionary<string, object?> { ["error"] = new OrderedDictionary<string, object?> { ["type"] = PythonName(e), ["text"] = e.Message } });
                        }
                    }

                    return new() { ["steps"] = steps, ["store"] = StoreView(run.Inner) };
                }

            case "_begin_admit":
                {
                    var waitAdvance = input.GetValueOrDefault("wait_advance") is double advance ? advance : 0.0;
                    async Task WaitAsync(CancellationToken cancellationToken)
                    {
                        await run.EnterAsync("wait", new(), cancellationToken);
                        run.Clock.Now += waitAdvance;
                    }

                    if (input.GetValueOrDefault("cancel_in_shield") is true)
                    {
                        using var shieldSource = new CancellationTokenSource();
                        var task = Phases.BeginAdmitAsync(store, run.Config, "g", (string)input["unit_id"]!, WaitAsync, shieldSource.Token);
                        await shieldSource.CancelAsync();
                        try
                        {
                            await task;
                        }
                        catch (OperationCanceledException)
                        {
                            run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "cancelled" });
                        }

                        return new() { ["store"] = StoreView(run.Inner) };
                    }

                    var admission = await Phases.BeginAdmitAsync(store, run.Config, "g", (string)input["unit_id"]!, WaitAsync, token);
                    return new()
                    {
                        ["admission"] = new List<object?> { admission.Seq, admission.Existing, admission.PreviousSeq },
                        ["store"] = StoreView(run.Inner),
                    };
                }

            case "_fail_unit":
                await Phases.FailUnitAsync(store, "g", (long)input["seq"]!, new InvalidOperationException("model"), token);
                return new() { ["store"] = StoreView(run.Inner) };

            case "_fail_admission":
                await Phases.FailAdmissionAsync(
                    store, "g", new Admission { Seq = (long)input["seq"]!, Existing = (bool)input["existing"]! }, new KeyNotFoundException("x"), token);
                return new() { ["store"] = StoreView(run.Inner) };

            case "_written_episodes":
                {
                    var result = await Phases.WrittenEpisodesAsync(
                        run.Driver, [.. List(input["uuids"]).Cast<string>()], (string?)input.GetValueOrDefault("group_id") ?? "g", token);
                    return new() { ["episodes"] = result.Select(e => (object?)new List<object?> { e.Key, EpisodeView(e.Value) }).ToList() };
                }

            case "_episodes_with_stored_links":
                {
                    List<EpisodicNode> episodes = [.. List(input["episodes"]).Select(Episode)];
                    var result = await Phases.EpisodesWithStoredLinksAsync(run.Reader, episodes, token);
                    return new()
                    {
                        ["episodes"] = result.Select(e => (object?)new List<object?> { EpisodeView(e), episodes.Any(x => ReferenceEquals(x, e)) }).ToList(),
                        ["inputs"] = episodes.Select(e => (object?)EpisodeView(e)).ToList(),
                    };
                }

            case "rebase_change_set":
                {
                    var changeSet = ChangeSetOf(input["change_set"]);
                    List<DeltaUnit> delta =
                    [
                        .. List(input["delta"]).Select(List).Select(d => new DeltaUnit
                    {
                        Seq = (long)d[0]!,
                        Status = (string)d[1]!,
                        Payload = ChangeSetOf(d[2]).ToBytes(),
                    }),
                ];
                    var unit = Admitted(input["unit"]);
                    ChangeSet final;
                    OrderedDictionary<string, double>? report = null;
                    if (input.GetValueOrDefault("defaults") is true)
                    {
                        final = await Phases.RebaseChangeSetAsync(run.Clients, changeSet, delta, unit, cancellationToken: token);
                    }
                    else
                    {
                        report = input.GetValueOrDefault("report") is true ? Phases.NewReport() : null;
                        var identity = input["identity_properties"] is OrderedDictionary<string, object?> table
                            ? new OrderedDictionary<string, List<string>>(table.Select(e => KeyValuePair.Create(e.Key, List(e.Value).Cast<string>().ToList())))
                            : null;
                        final = await Phases.RebaseChangeSetAsync(
                            run.Clients, changeSet, delta, unit, reader: run.Reader, entityTypes: null, identityProperties: identity, report: report, cancellationToken: token);
                    }

                    return new()
                    {
                        ["final"] = final.ModelDump(json: true),
                        ["input"] = PyJson.Dumps(changeSet.ModelDump(json: true)) == PyJson.Dumps(input["change_set"]),
                        ["report"] = report is null ? null : ReportView(report),
                    };
                }

            case "apply_change_set" or "_apply_with_retries" or "_write_saga_links":
                {
                    List<object?> budgets = [.. List(input["budgets"])];
                    List<object?> turns = [.. input.GetValueOrDefault("turns") as List<object?> ?? []];
                    double WriteBudget()
                    {
                        var value = budgets[0];
                        if (budgets.Count > 1)
                        {
                            budgets.RemoveAt(0);
                        }

                        run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "budget", ["value"] = value });
                        if (value is "expired")
                        {
                            throw new UnitExpiredException("g", 1);
                        }

                        return (double)value!;
                    }

                    async Task CheckTurnAsync(CancellationToken cancellationToken)
                    {
                        var outcome = turns.Count > 0 ? (string)turns[0]! : "ok";
                        if (turns.Count > 0)
                        {
                            turns.RemoveAt(0);
                        }

                        await run.EnterAsync("check_turn", new() { ["outcome"] = outcome }, cancellationToken);
                        switch (outcome)
                        {
                            case "stale":
                                throw new StaleCommitException("g", 1);
                            case "expired":
                                throw new UnitExpiredException("g", 1);
                            case "error":
                                throw new InvalidOperationException("no answer");
                        }
                    }

                    var changeSet = ChangeSetOf(input["change_set"]);
                    if (fn == "_write_saga_links")
                    {
                        await Phases.WriteSagaLinksAsync(
                            run.Clients,
                            changeSet,
                            _ =>
                            {
                                try
                                {
                                    return Task.FromResult(WriteBudget());
                                }
                                catch (Exception e)
                                {
                                    return Task.FromException<double>(e);
                                }
                            },
                            token);
                        return new() { ["change_set"] = changeSet.ModelDump(json: true) };
                    }

                    if (fn == "_apply_with_retries")
                    {
                        var report = Phases.NewReport();
                        var changed = await Phases.ApplyWithRetriesAsync(
                            run.Clients, changeSet, WriteBudget, (long)input["max_attempts"]!, report, run.Reader, CheckTurnAsync, token);
                        return new() { ["changed"] = changed.Select(e => (object?)EdgeView(e)).ToList(), ["report"] = ReportView(report) };
                    }

                    var result = input.GetValueOrDefault("defaults") is true
                        ? await Phases.ApplyChangeSetAsync(run.Clients, changeSet, WriteBudget, cancellationToken: token)
                        : await Phases.ApplyChangeSetAsync(
                            run.Clients, changeSet, WriteBudget, run.Reader, input.GetValueOrDefault("turns") is not null ? CheckTurnAsync : null, token);
                    return new()
                    {
                        ["changed"] = result.ChangedEdges.Select(e => (object?)EdgeView(e)).ToList(),
                        ["applied"] = result.Applied,
                        ["dropped"] = result.Dropped,
                        ["change_set"] = changeSet.ModelDump(json: true),
                    };
                }

            case "_mark_apply_failed":
                await Phases.MarkApplyFailedAsync(store, "g", (long)input["seq"]!, (long)input["fence"]!, token);
                return new() { ["store"] = StoreView(run.Inner) };

            case "_recover_unit":
                {
                    var recover = (CommitRecover)results[^1];
                    if (input.GetValueOrDefault("bad_payload") is true)
                    {
                        recover = new CommitRecover { Seq = recover.Seq, Fence = recover.Fence, FinalPayload = "not zlib"u8.ToArray() };
                    }

                    if (input.GetValueOrDefault("fence") is long fence)
                    {
                        recover = new CommitRecover { Seq = recover.Seq, Fence = fence, FinalPayload = recover.FinalPayload };
                    }

                    var turns = new Dictionary<long, long>();
                    foreach (var (key, value) in input.GetValueOrDefault("turns") as OrderedDictionary<string, object?> ?? new())
                    {
                        turns[long.Parse(key, System.Globalization.CultureInfo.InvariantCulture)] = (long)value!;
                    }

                    var report = Phases.NewReport();
                    await Phases.RecoverUnitAsync(
                        run.Clients, store, run.Config, "g", recover, run.Clock.Now, run.Clock.Read, turns, report, run.Reader, token);
                    return new()
                    {
                        ["recover"] = CommitView(recover),
                        ["turns"] = turns.Select(e => (object?)new List<object?> { e.Key, e.Value }).ToList(),
                        ["report"] = ReportView(report),
                        ["store"] = StoreView(run.Inner),
                    };
                }

            case "_build_saga_links":
                {
                    var sagaObject = input["saga"] is null ? null : SagaNode.ModelValidate(DecodeDict(Dict(input["saga"])));
                    SagaOrName? saga = sagaObject is null ? null : new SagaOrName.Node { Saga = sagaObject };
                    var (links, sagaNode, create) = await Phases.BuildSagaLinksAsync(
                        Graphiti(true), run.Clients, Admitted(input["unit"]), saga, (string?)input.GetValueOrDefault("previous"), token);
                    return new()
                    {
                        ["links"] = links.Select(l => (object?)l.ModelDump(json: true)).ToList(),
                        ["saga"] = sagaNode?.ModelDump(json: true),
                        ["same"] = ReferenceEquals(sagaNode, sagaObject),
                        ["create"] = create,
                    };
                }

            case "_hydrate_nodes":
                {
                    var changeSet = ChangeSetOf(input["change_set"]);
                    List<EntityNode> nodes = [.. List(input["nodes"]).Select(Node)];
                    var failed = await Phases.HydrateNodesAsync(run.Clients, Admitted(input["unit"]), changeSet, nodes, null, token);
                    return new() { ["failed"] = failed, ["nodes"] = nodes.Select(n => (object?)NodeView(n)).ToList() };
                }

            default:
                throw new InvalidOperationException("unknown function " + fn);
        }
    }
}
