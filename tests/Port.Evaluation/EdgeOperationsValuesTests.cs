// Evaluation test for graphiti_core/utils/maintenance/edge_operations.py (port/RULES.md, R23, R25).
// values/edge_operations.json holds the calls that port/tools/record_edge_operations.py recorded from Python: the sync
// functions with generated inputs, and the async functions with fakes for the LLM client, the embedder, and the graph
// driver. The calls and the log lines of each case are one ordered list.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.EdgeOperationsValues;

public class EdgeOperationsValuesTests
{
    private static readonly List<OrderedDictionary<string, object?>> AsyncCases =
        List(Document["async_cases"]).Select(Dict).ToList();

    public static TheoryData<int> AsyncCaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < AsyncCases.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Fact]
    public void Each_module_constant_has_the_value_of_python()
    {
        Assert.Equal(EdgeOperations.DefaultEdgeName, (string)Dict(Document["constants"])["DEFAULT_EDGE_NAME"]!);
    }

    [Fact]
    public void The_recording_reaches_each_function_and_branch()
    {
        var names = List(Document["cases"]).Select(c => (string)Dict(c)["f"]!).ToHashSet();
        foreach (var name in new[]
        {
            "build_episodic_edges", "build_community_edges", "_pair_search_filter", "_same_pair_edges",
            "find_exact_duplicate_edge", "edge_types", "enforce_length_mismatch", "_apply_temporal_invalidation",
            "resolve_edge_contradictions",
        })
        {
            Assert.Contains(name, names);
        }

        var fns = AsyncCases.Select(c => (string)Dict(c["in"])["fn"]!).ToHashSet();
        foreach (var fn in new[]
        {
            "extract_edges", "resolve_extracted_edges", "resolve_extracted_edge", "decide_edge_against_candidates",
            "_dedupe_extracted_edge", "_extract_edge_timestamps", "filter_existing_duplicate_of_edges",
        })
        {
            Assert.Contains(fn, fns);
        }

        // The limit holds at two levels: the gather of the searches, and the gather of the two edge searches inside
        // each search. A case has more searches than its limit of 2 (2 x 2 calls in flight), one has the limit 1, and
        // one has no limit (8 calls in flight).
        Assert.Contains(AsyncCases, c => Dict(c["in"]).GetValueOrDefault("max_coroutines") is 2L && List(Dict(c["in"])["edges"]).Count > 2 && (long)Dict(c["out"])["max_in_flight"]! == 4);
        Assert.Contains(AsyncCases, c => Dict(c["in"]).GetValueOrDefault("max_coroutines") is 1L && (long)Dict(c["out"])["max_in_flight"]! == 1);
        Assert.Contains(AsyncCases, c => Dict(c["in"]).GetValueOrDefault("max_coroutines") is null && (long)Dict(c["out"])["max_in_flight"]! == 8);

        // The last gather embeds the resolved edges and the invalidated edges, so it has two calls. With the limit 1 it has
        // one call in flight, and with no limit it has two.
        bool ResolvesAndInvalidates(OrderedDictionary<string, object?> c) =>
            (string)Dict(c["in"])["fn"]! == "resolve_extracted_edges"
            && Dict(c["out"]).GetValueOrDefault("ok") is OrderedDictionary<string, object?> ok
            && List(ok["invalidated"]).Count > 0;
        Assert.Contains(AsyncCases, c => ResolvesAndInvalidates(c) && Dict(c["in"]).GetValueOrDefault("max_coroutines") is 1L && (long)Dict(c["out"])["max_in_flight"]! == 1);
        Assert.Contains(AsyncCases, c => ResolvesAndInvalidates(c) && Dict(c["in"]).GetValueOrDefault("max_coroutines") is null && (long)Dict(c["out"])["max_in_flight"]! == 2);
    }

    [Fact]
    public void The_port_gives_each_recorded_sync_result_and_log_line_of_python()
    {
        var cases = List(Document["cases"]).Select(Dict).ToList();
        Assert.True(cases.Count >= 500);
        List<string> failures = [];
        foreach (var (index, item) in cases.Index())
        {
            var events = new List<object?>();
            object? actual;
            using (PortServices.Use(loggerFactory: new EdgeOperationsLogCapture(events), time: new MaintenanceFixedClock(FixedNow.ToDateTimeOffset())))
            {
                actual = RunSync((string)item["f"]!, Dict(item["in"]), events);
            }

            var expected = PyJson.Dumps(item["out"]);
            var got = PyJson.Dumps(Placeholders(actual, []));
            if (expected != got)
            {
                failures.Add($"case {index} ({item["f"]}):\n  expected {expected}\n  got      {got}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(5)) + $"\n{failures.Count} failures");
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(AsyncCaseIndexes))]
    public async Task The_port_makes_each_recorded_call_and_gives_each_result(int index)
    {
        var input = Dict(AsyncCases[index]["in"]);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new EdgeOperationsRun(input, source, cancelAt: -1);
        var output = new OrderedDictionary<string, object?>();
        using (PortServices.Use(loggerFactory: new EdgeOperationsLogCapture(run.Events), time: new MaintenanceFixedClock(FixedNow.ToDateTimeOffset())))
        {
            try
            {
                output["ok"] = await LoopGuard.RunAsync(() => CallAsync(run, source.Token));
            }
            catch (Exception e) when (e is ValidationException or KeyNotFoundException or ArgumentException or InvalidOperationException or InvalidCastException)
            {
                output["error"] = ErrorView(e);
            }
        }

        output["events"] = run.Events;
        output["max_in_flight"] = (long)run.MaxInFlight;
        var expected = Dict(AsyncCases[index]["out"]);
        var actual = Dict(Placeholders(output, []));
        Assert.Equal(PyJson.Dumps(expected.GetValueOrDefault("error")), PyJson.Dumps(actual.GetValueOrDefault("error")));
        var expectedEvents = List(expected["events"]);
        var actualEvents = List(actual["events"]);
        for (var i = 0; i < Math.Min(expectedEvents.Count, actualEvents.Count); i++)
        {
            Assert.Equal(PyJson.Dumps(expectedEvents[i]), PyJson.Dumps(actualEvents[i]));
        }

        Assert.Equal(expectedEvents.Count, actualEvents.Count);
        Assert.Equal(PyJson.Dumps(expected.GetValueOrDefault("ok")), PyJson.Dumps(actual.GetValueOrDefault("ok")));
        Assert.Equal(PyJson.Dumps(expected["max_in_flight"]), PyJson.Dumps(actual["max_in_flight"]));

        // Each fake got the token of the call (R25).
        Assert.All(run.Tokens, token => Assert.Equal(source.Token, token));
    }

    // The scenarios of the cancellation tests: one recorded case of each async function, with the count of its fake
    // calls with no cancel.
    private static readonly Dictionary<string, (int Case, int Calls)> Scenarios = new()
    {
        ["extract_edges"] = (0, 1),
        ["resolve_extracted_edges"] = (13, 25),
        ["resolve_extracted_edges_strict"] = (14, 14),
        ["resolve_extracted_edges_no_candidates"] = (15, 20),
        ["resolve_extracted_edges_override_routing"] = (16, 13),
        ["resolve_extracted_edges_limit_two"] = (17, 24),
        ["resolve_extracted_edge"] = (21, 2),
        ["decide_edge_against_candidates"] = (26, 1),
        ["_dedupe_extracted_edge"] = (29, 2),
        ["_extract_edge_timestamps"] = (31, 1),
        ["filter_existing_duplicate_of_edges"] = (36, 1),
    };

    public static TheoryData<string> ScenarioNames() => [.. Scenarios.Keys];

    public static TheoryData<string, int> LaterCalls()
    {
        var data = new TheoryData<string, int>();
        foreach (var (name, (_, calls)) in Scenarios)
        {
            for (var k = 0; k < calls - 1; k++)
            {
                data.Add(name, k);
            }
        }

        return data;
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(ScenarioNames))]
    public async Task The_declared_count_of_calls_is_the_count_of_the_method(string name)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new EdgeOperationsRun(Dict(AsyncCases[Scenarios[name].Case]["in"]), source, cancelAt: -1);
        using (PortServices.Use(loggerFactory: new EdgeOperationsLogCapture(run.Events), time: new MaintenanceFixedClock(FixedNow.ToDateTimeOffset())))
        {
            await LoopGuard.RunAsync(() => CallAsync(run, source.Token));
        }

        Assert.Equal(Scenarios[name].Calls, run.Count);
    }

    // The cases that the recorder ran again with a cancel are the cases of Scenarios (CANCEL_CASES in
    // port/tools/record_edge_operations.py).
    [Fact]
    public void Each_scenario_has_a_recorded_cancel_run_for_each_point()
    {
        var recorded = AsyncCases.Index().Where(c => c.Item.ContainsKey("cancel")).Select(c => c.Index).Order().ToList();
        Assert.Equal(Scenarios.Values.Select(v => v.Case).Order().ToList(), recorded);
        foreach (var (_, (index, calls)) in Scenarios)
        {
            Assert.Equal(
                Enumerable.Range(-1, calls).Select(k => (long)k).ToList(),
                List(AsyncCases[index]["cancel"]).Select(r => (long)Dict(r)["k"]!).ToList());
        }
    }

    // Each async method with a canceled token throws OperationCanceledException (R8). The recorder canceled the task of
    // the call before its first await (k = -1), so the test compares the attempts, the recorded calls, and the events
    // that the method writes before it reads the token. A fake that is canceled throws, so a dropped token or a caught
    // cancel gives other counts.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(ScenarioNames))]
    public async Task Each_async_method_with_a_canceled_token_throws_OperationCanceledException(string name)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var run = new EdgeOperationsRun(Dict(AsyncCases[Scenarios[name].Case]["in"]), source, cancelAt: -1);
        using (PortServices.Use(loggerFactory: new EdgeOperationsLogCapture(run.Events), time: new MaintenanceFixedClock(FixedNow.ToDateTimeOffset())))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));
            await LoopGuard.RunAsync(Turns);
        }

        AssertCancelPoint(Scenarios[name].Case, -1, run, 0);
    }

    // The token is canceled while call k runs. Call k is the last call that a fake records. Python lets a CancelledError
    // through each `except Exception` (it is a BaseException), so it writes no `Failed to extract timestamps` line. The
    // recorder canceled the task of the call at the same point, so the test compares the count of the attempts (each
    // fake counts an attempt before it reads its token), the count of the calls that a fake recorded, and the events
    // after call k (the log lines and the later calls). The calls that a failed gather leaves run on, so the test gives
    // the scheduler 50 turns before it counts (R25).
    [Theory(Timeout = 5000)]
    [MemberData(nameof(LaterCalls))]
    public async Task Each_later_awaited_call_gets_the_token_and_no_call_follows_a_cancel(string name, int k)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new EdgeOperationsRun(Dict(AsyncCases[Scenarios[name].Case]["in"]), source, cancelAt: k);
        using (PortServices.Use(loggerFactory: new EdgeOperationsLogCapture(run.Events), time: new MaintenanceFixedClock(FixedNow.ToDateTimeOffset())))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));

            // The calls that a failed gather leaves run on. Give them turns before the count (R25).
            await LoopGuard.RunAsync(Turns);
        }

        AssertCancelPoint(Scenarios[name].Case, k, run, run.EventsAtCancel);
    }

    [Fact]
    public void Each_late_cancel_entry_names_one_point_of_the_recording()
    {
        Assert.Equal(LateCancel.Length, LateCancel.Select(e => (e.Case, e.K)).Distinct().Count());
        Assert.All(LateCancel, e => Assert.Contains(e.K, List(AsyncCases[e.Case]["cancel"]).Select(r => (int)(long)Dict(r)["k"]!)));
    }

    private static readonly string[] CancelFields = ["attempts", "count", "after"];

    // The late cancel of GAPS F33. Python cancels each task of the gathers at once. The port reads the token at the next
    // call of a fake, so a search whose last fake call has returned goes on, and writes its debug line `search returned
    // context` ("after"). At the last calls of a case the port also writes the debug lines that come before the next
    // call ("after"). Each point keeps the events of Python in their order. A point that agrees with Python must not be in
    // the list, and a point in the list must differ in the way that its kind says.
    private static readonly (int Case, int K, string Kind)[] LateCancel =
    [
        (13, 3, "after"),
        (13, 4, "after"),
        (13, 5, "after"),
        (13, 6, "after"),
        (13, 7, "after"),
        (13, 8, "after"),
        (13, 9, "after"),
        (13, 11, "after"),
        (13, 12, "after"),
        (13, 13, "after"),
        (13, 14, "after"),
        (13, 15, "after"),
        (13, 16, "after"),
        (13, 17, "after"),
        (13, 22, "after"),
        (14, 3, "after"),
        (14, 4, "after"),
        (14, 5, "after"),
        (14, 7, "after"),
        (14, 8, "after"),
        (14, 9, "after"),
        (14, 12, "after"),
        (15, 3, "after"),
        (15, 4, "after"),
        (15, 5, "after"),
        (15, 6, "after"),
        (15, 7, "after"),
        (15, 9, "after"),
        (15, 10, "after"),
        (15, 11, "after"),
        (15, 12, "after"),
        (15, 13, "after"),
        (15, 18, "after"),
        (16, 3, "after"),
        (16, 4, "after"),
        (16, 5, "after"),
        (16, 7, "after"),
        (16, 8, "after"),
        (16, 9, "after"),
        (16, 11, "after"),
        (17, 2, "after"),
        (17, 3, "after"),
        (17, 4, "after"),
        (17, 6, "after"),
        (17, 7, "after"),
        (17, 8, "after"),
        (17, 10, "after"),
        (17, 11, "after"),
        (17, 12, "after"),
        (17, 14, "after"),
        (17, 15, "after"),
        (17, 16, "after"),
        (17, 22, "after"),
    ];

    // The point k of a recorded cancel scenario (k = -1: the task is canceled before its first await).
    private static void AssertCancelPoint(int index, int k, EdgeOperationsRun run, int eventsAtCancel)
    {
        var recorded = List(AsyncCases[index]["cancel"]).Select(Dict).Single(r => (long)r["k"]! == k);
        var got = Dict(Placeholders(
            new OrderedDictionary<string, object?>
            {
                ["k"] = (long)k,
                ["error"] = "CancelledError",
                ["attempts"] = run.Attempts,
                ["count"] = (long)run.Count,
                ["after"] = run.Events.Skip(eventsAtCancel).ToList(),
            },
            []));
        var differences = string.Join(
            "+",
            CancelFields.Where(key => PyJson.Dumps(recorded[key]) != PyJson.Dumps(got[key])));
        var listed = LateCancel.Where(e => e.Case == index && e.K == k).Select(e => e.Kind).ToList();
        if (listed.Count == 0)
        {
            Assert.True(differences.Length == 0, $"case {index}, call {k}: the port differs from Python in {differences}: {PyJson.Dumps(recorded)} and {PyJson.Dumps(got)}");
        }
        else
        {
            Assert.Equal(listed.Single(), differences);

            // The port writes more lines, never other lines: the events of Python come in the same order.
            var port = List(got["after"]).Select(x => PyJson.Dumps(x)).ToList();
            var next = 0;
            foreach (var item in List(recorded["after"]).Select(x => PyJson.Dumps(x)))
            {
                next = port.IndexOf(item, next) + 1;
                Assert.True(next > 0, $"case {index}, call {k}: the port lacks the event {item} of Python");
            }
        }
    }

    private static async Task Turns()
    {
        for (var i = 0; i < 50; i++)
        {
            await Task.Yield();
        }
    }

    private static OrderedDictionary<string, object?> ErrorView(Exception e) => new()
    {
        ["type"] = e switch
        {
            ValidationException => "ValidationError",
            KeyNotFoundException => "LookupError",
            InvalidCastException => "TypeError",
            ArgumentException => "ValueError",
            _ => "RuntimeError",
        },
        ["text"] = e is ValidationException ? e.Message.Split('\n')[0] : e.Message,
    };

    private static OrderedDictionary<string, GraphTypeDescriptor>? TypesOf(object? names) =>
        names is null
            ? null
            : List(names).Select(n => (string)n!).Aggregate(
                new OrderedDictionary<string, GraphTypeDescriptor>(),
                (acc, n) =>
                {
                    acc[n] = Models[n];
                    return acc;
                });

    private static OrderedDictionary<(string, string), List<string>> TypeMap(object? value)
    {
        var map = new OrderedDictionary<(string, string), List<string>>();
        foreach (var entry in List(value ?? new List<object?>()).Select(List))
        {
            var key = List(entry[0]);
            map[((string)key[0]!, (string)key[1]!)] = List(entry[1]).Select(x => (string)x!).ToList();
        }

        return map;
    }

    private static async Task<object?> CallAsync(EdgeOperationsRun run, CancellationToken cancellationToken)
    {
        var input = run.Input;
        var route = input.GetValueOrDefault("routing") is true ? run.Clients : null;
        switch ((string)input["fn"]!)
        {
            case "extract_edges":
                {
                    EpisodeOrEpisodes episode = input["episode"] is List<object?> many
                        ? new EpisodeOrEpisodes.Many { Episodes = [.. many.Select(Episode)] }
                        : new EpisodeOrEpisodes.One { Episode = Episode(input["episode"]) };
                    List<EntityNode> nodes = [.. List(input["nodes"]).Select(Node)];
                    var (edges, materialized) = await EdgeOperations.ExtractEdgesAsync(
                        run.Clients,
                        episode,
                        nodes,
                        [.. List(input["previous"]).Select(Episode)],
                        TypeMap(input["type_map"]),
                        groupId: (string)input["group_id"]!,
                        edgeTypes: TypesOf(input["types"]),
                        strictEdgeTypes: (bool)input["strict"]!,
                        customExtractionInstructions: (string?)input["instructions"],
                        cancellationToken: cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["edges"] = edges.Select(e => (object?)EdgeView(e)).ToList(),
                        ["materialized"] = materialized.Select(n => (object?)NodeView(n)).ToList(),
                        ["nodes"] = nodes.Select(n => (object?)NodeView(n)).ToList(),
                    };
                }

            case "resolve_extracted_edges":
                {
                    List<EntityEdge> extracted = [.. List(input["edges"]).Select(Edge)];
                    var invalidatedBy = (bool)input["invalidated_by"]! ? new OrderedDictionary<string, List<string>>() : null;
                    var overrideEdges = input["override"] is List<object?> o ? o.Select(Edge).ToList() : null;
                    var (resolved, invalidated, @new) = await EdgeOperations.ResolveExtractedEdgesAsync(
                        run.Clients,
                        extracted,
                        Episode(input["episode"]),
                        [.. List(input["entities"]).Select(Node)],
                        TypesOf(input["types"])!,
                        TypeMap(input["type_map"]),
                        existingEdgesOverride: overrideEdges,
                        strictEdgeTypes: (bool)input["strict"]!,
                        invalidatedBy: invalidatedBy,
                        trace: null,
                        cancellationToken: cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["resolved"] = resolved.Select(e => (object?)EdgeView(e)).ToList(),
                        ["invalidated"] = invalidated.Select(e => (object?)EdgeView(e)).ToList(),
                        ["new"] = Uuids(@new),
                        ["invalidated_by"] = Encode(invalidatedBy?.Aggregate(
                            new OrderedDictionary<string, object?>(),
                            (acc, e) =>
                            {
                                acc[e.Key] = e.Value;
                                return acc;
                            })),
                        ["extracted"] = extracted.Select(e => (object?)EdgeView(e)).ToList(),
                    };
                }

            case "resolve_extracted_edge":
            case "_dedupe_extracted_edge":
                {
                    var extracted = Edge(input["edge"]);
                    List<EntityEdge> related = [.. List(input["related"]).Select(Edge)];
                    List<EntityEdge> existing = [.. List(input["existing"]).Select(Edge)];
                    var candidates = input["candidates"] is OrderedDictionary<string, object?>
                        ? new OrderedDictionary<string, GraphTypeDescriptor>()
                        : TypesOf(input["candidates"]);
                    EntityEdge first;
                    List<EntityEdge> second;
                    List<EntityEdge>? third;
                    if ((string)input["fn"]! == "resolve_extracted_edge")
                    {
                        (first, second, third) = await EdgeOperations.ResolveExtractedEdgeAsync(
                            run.Llm, extracted, related, existing, Episode(input["episode"]), candidates, clients: route, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        (first, second, third) = await EdgeOperations.DedupeExtractedEdgeAsync(
                            run.Llm, extracted, related, existing, Episode(input["episode"]), candidates, clients: route, cancellationToken: cancellationToken);
                    }

                    return new OrderedDictionary<string, object?>
                    {
                        ["result"] = new List<object?>
                    {
                        EdgeView(first),
                        second.Select(e => (object?)EdgeView(e)).ToList(),
                        third?.Select(e => (object?)EdgeView(e)).ToList(),
                    },
                        ["extracted"] = EdgeView(extracted),
                        ["related"] = related.Select(e => (object?)EdgeView(e)).ToList(),
                        ["existing"] = existing.Select(e => (object?)EdgeView(e)).ToList(),
                    };
                }

            case "decide_edge_against_candidates":
                {
                    var decision = await EdgeOperations.DecideEdgeAgainstCandidatesAsync(
                        run.Llm,
                        Edge(input["edge"]),
                        [.. List(input["related"]).Select(Edge)],
                        [.. List(input["existing"]).Select(Edge)],
                        clients: route,
                        cancellationToken: cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["exact_match"] = decision.ExactMatch?.Uuid,
                        ["duplicates"] = Uuids(decision.Duplicates),
                        ["contradicted"] = Uuids(decision.Contradicted),
                    };
                }

            case "_extract_edge_timestamps":
                {
                    var extracted = Edge(input["edge"]);
                    var episode = input["episode"] is null ? null : Episode(input["episode"]);
                    await EdgeOperations.ExtractEdgeTimestampsAsync(run.Llm, extracted, episode, clients: route, cancellationToken: cancellationToken);
                    return new OrderedDictionary<string, object?> { ["edge"] = EdgeView(extracted) };
                }

            case "filter_existing_duplicate_of_edges":
                {
                    var stored = List(input["stored_nodes"]).Select(Node).ToDictionary(n => n.Uuid);
                    List<(EntityNode, EntityNode)> pairs = [.. List(input["pairs"]).Select(List).Select(p => (stored[(string)p[0]!], stored[(string)p[1]!]))];
                    var result = await EdgeOperations.FilterExistingDuplicateOfEdgesAsync(run.Driver, pairs, cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["pairs"] = result.Select(p => (object?)new List<object?> { p.Item1.Uuid, p.Item2.Uuid }).ToList(),
                    };
                }

            default:
                throw new InvalidOperationException((string)input["fn"]!);
        }
    }

    private static OrderedDictionary<string, object?> RunSync(string name, OrderedDictionary<string, object?> input, List<object?> events)
    {
        var output = new OrderedDictionary<string, object?>();
        object? after = null;
        try
        {
            output["ok"] = name switch
            {
                "build_episodic_edges" => BuildEpisodicEdges(input),
                "build_community_edges" => EdgeOperations.BuildCommunityEdges(
                        [.. List(input["nodes"]).Select(Node)],
                        CommunityNode.ModelValidate(DecodeDict(Dict(input["community"]))),
                        (PyDateTime)Decode(input["created_at"])!)
                    .Select(e => (object?)SimpleEdgeView("CommunityEdge", e)).ToList(),
                "_pair_search_filter" => PairSearchFilter(input),
                "_same_pair_edges" => Uuids(EdgeOperations.SamePairEdges(Edge(input["edge"]), [.. List(input["candidates"]).Select(Edge)])),
                "find_exact_duplicate_edge" => FindExactDuplicates(input),
                "edge_types" => EdgeTypes(input),
                "enforce_length_mismatch" => EnforceLengthMismatch(input),
                "_apply_temporal_invalidation" or "resolve_edge_contradictions" => Temporal(name, input, out after),
                _ => throw new InvalidOperationException(name),
            };
        }
        catch (ArgumentException e)
        {
            output.Remove("ok");
            output["error"] = new OrderedDictionary<string, object?> { ["type"] = "ValueError", ["text"] = e.Message };
        }

        output["log"] = events.ToList();
        output["after"] = after;
        return output;
    }

    private static List<object?> BuildEpisodicEdges(OrderedDictionary<string, object?> input)
    {
        List<EntityNode> nodes = [.. List(input["nodes"]).Select(Node)];
        var createdAt = (PyDateTime)Decode(input["created_at"])!;
        OrderedDictionary<string, List<long>>? map = null;
        if (input["map"] is OrderedDictionary<string, object?> m)
        {
            map = new OrderedDictionary<string, List<long>>();
            foreach (var (key, value) in m)
            {
                map[key] = List(value).Select(x => (long)x!).ToList();
            }
        }

        var edges = input["episode_uuid"] is string one
            ? EdgeOperations.BuildEpisodicEdges(nodes, one, createdAt, map)
            : EdgeOperations.BuildEpisodicEdges(nodes, List(input["episode_uuid"]).Select(x => (string)x!).ToList(), createdAt, map);
        return edges.Select(e => (object?)SimpleEdgeView("EpisodicEdge", e)).ToList();
    }

    private static OrderedDictionary<string, object?> PairSearchFilter(OrderedDictionary<string, object?> input)
    {
        var filter = EdgeOperations.PairSearchFilter(Edge(input["edge"]));
        return new OrderedDictionary<string, object?>
        {
            ["edge_source_node_uuids"] = Encode(filter.EdgeSourceNodeUuids),
            ["edge_target_node_uuids"] = Encode(filter.EdgeTargetNodeUuids),
            ["dump"] = Encode(filter.ModelDump(json: true)),
        };
    }

    private static OrderedDictionary<string, object?> FindExactDuplicates(OrderedDictionary<string, object?> input)
    {
        var seen = new Dictionary<(string, string, string), EntityEdge>();
        List<object?> found = [];
        foreach (var data in List(input["edges"]))
        {
            found.Add(EdgeOperations.FindExactDuplicateEdge(Edge(data), seen)?.Uuid);
        }

        return new OrderedDictionary<string, object?> { ["found"] = found, ["seen"] = (long)seen.Count };
    }

    private static OrderedDictionary<string, object?> EdgeTypes(OrderedDictionary<string, object?> input)
    {
        var edgeTypeMap = TypeMap(input["type_map"]);
        var edgeTypes = TypesOf(input["type_names"])!;
        var uuidEntityMap = new Dictionary<string, EntityNode>();
        foreach (var (uuid, data) in Dict(input["nodes"]))
        {
            uuidEntityMap[uuid] = Node(data);
        }

        List<EntityEdge> edges = [.. List(input["edges"]).Select(Edge)];
        List<OrderedDictionary<string, GraphTypeDescriptor>> lst = [.. edges.Select(e => EdgeOperations.EdgeTypesForEndpoints(e, uuidEntityMap, edgeTypes, edgeTypeMap))];
        var matched = lst.Select(m => (object?)m.Keys.Select(k => (object?)k).ToList()).ToList();
        var (kept, keptTypes) = EdgeOperations.EnforceEdgeTypeSignatures(edges, lst, edgeTypeMap, (bool)input["strict"]!);
        return new OrderedDictionary<string, object?>
        {
            ["matched"] = matched,
            ["kept"] = kept.Select(e => (object?)EdgeView(e)).ToList(),
            ["kept_types"] = keptTypes.Select(m => (object?)m.Keys.Select(k => (object?)k).ToList()).ToList(),
        };
    }

    private static object? EnforceLengthMismatch(OrderedDictionary<string, object?> input)
    {
        EdgeOperations.EnforceEdgeTypeSignatures(
            [Edge(List(input["edges"])[0])],
            [],
            new OrderedDictionary<(string, string), List<string>> { [("Person", "Company")] = ["WORKS_AT"] },
            true);
        return null;
    }

    private static List<object?> Temporal(string name, OrderedDictionary<string, object?> input, out object? after)
    {
        var resolved = Edge(input["resolved"]);
        List<EntityEdge> candidates = [.. List(input["candidates"]).Select(Edge)];
        var result = name == "_apply_temporal_invalidation"
            ? EdgeOperations.ApplyTemporalInvalidation(resolved, candidates)
            : EdgeOperations.ResolveEdgeContradictions(resolved, candidates);
        after = new OrderedDictionary<string, object?>
        {
            ["resolved"] = EdgeView(resolved),
            ["candidates"] = candidates.Select(c => (object?)EdgeView(c)).ToList(),
        };
        return Uuids(result);
    }
}
