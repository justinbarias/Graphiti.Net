// Evaluation test for graphiti_core/graphiti.py (port/RULES.md, R23, R25).
// values/graphiti.json holds the calls that port/tools/record_graphiti.py recorded from Python: the module functions, the
// members of `Graphiti` that do not await, and each other method of `Graphiti` with concurrent ingestion off. The fakes
// replace the graph driver with its session and transaction, the graph operations interface, the LLM client, the
// embedder, the cross-encoder, the tracer, and the state store. The calls, the spans, and the log lines of each case are
// one ordered list.
//
// The paths that enter ConcurrentIngestor wait for packet P9b: _get_concurrent_ingestor, and the calls of
// ConcurrentIngestor.add_episode and ConcurrentIngestor.add_episode_bulk at the end of add_episode and add_episode_bulk.
// The checks before those calls are recorded. The four branches of __init__ that build a default object are not ported
// (R1, decisions D3 and D28): their cases hold the error of the port.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Prompts;
using Graphiti.Core.Search;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.GraphitiValues;

// search writes num_results into a shared recipe object (upstream defect U8), and the case search_center writes 3. The
// class runs alone, and each run gives the recipes back their limits, so the write reaches no other test class.
[CollectionDefinition(nameof(GraphitiValuesTests), DisableParallelization = true)]
public class GraphitiRecipesIsolation
{
}

[Collection(nameof(GraphitiValuesTests))]
public class GraphitiValuesTests
{
    private static readonly List<OrderedDictionary<string, object?>> SyncCases =
        List(GraphitiValues.Document["cases"]).Select(Dict).ToList();

    private static readonly List<OrderedDictionary<string, object?>> AsyncCases =
        List(GraphitiValues.Document["async_cases"]).Select(Dict).ToList();

    // The cancel points where the port differs from Python by the late cancel of GAPS F33. The key is the function, the
    // name of the case, and k. The value is the kind of the difference (CancelDifference). A listed point that agrees
    // with Python fails the test.
    private static readonly Dictionary<string, string> LateCancel = new(StringComparer.Ordinal)
    {
        ["add_episode:contradiction:5"] = "attempts+1",
        ["add_episode:contradiction:9"] = "logs",
        ["add_episode:contradiction:11"] = "logs",
        ["add_episode:contradiction:13"] = "logs",
        ["add_episode:contradiction:15"] = "attempts+1",
        ["add_episode:contradiction:21"] = "result",
        ["add_episode:entities_and_edges:5"] = "attempts+1",
        ["add_episode:entities_and_edges:9"] = "logs",
        ["add_episode:entities_and_edges:11"] = "logs",
        ["add_episode:entities_and_edges:12"] = "logs",
        ["add_episode:entities_and_edges:13"] = "attempts+1",
        ["add_episode:entities_and_edges:19"] = "result",
        ["add_episode:no_entity:7"] = "result",
        ["add_episode_bulk:one:7"] = "logs",
        ["add_episode_bulk:one:11"] = "logs",
        ["add_episode_bulk:one:17"] = "logs",
        ["add_episode_bulk:one:18"] = "attempts+1",
        ["add_episode_bulk:one:22"] = "logs",
        ["add_episode_bulk:one:24"] = "logs",
        ["add_episode_bulk:one:25"] = "logs",
        ["add_episode_bulk:one:26"] = "attempts+2",
        ["add_episode_bulk:one:31"] = "result",
        ["add_episode_bulk:several:10"] = "logs",
        ["add_episode_bulk:several:11"] = "logs",
        ["add_episode_bulk:several:18"] = "logs",
        ["add_episode_bulk:several:19"] = "logs",
        ["add_episode_bulk:several:20"] = "logs",
        ["add_episode_bulk:several:30"] = "logs",
        ["add_episode_bulk:several:31"] = "logs",
        ["add_episode_bulk:several:32"] = "attempts+1",
        ["add_episode_bulk:several:33"] = "attempts+2",
        ["add_episode_bulk:several:39"] = "logs",
        ["add_episode_bulk:several:40"] = "logs",
        ["add_episode_bulk:several:41"] = "logs",
        ["add_episode_bulk:several:43"] = "logs",
        ["add_episode_bulk:several:44"] = "logs",
        ["add_episode_bulk:several:45"] = "logs",
        ["add_episode_bulk:several:46"] = "logs",
        ["add_episode_bulk:several:47"] = "logs",
        ["add_episode_bulk:several:54"] = "result",
        ["add_triplet:new_nodes:6"] = "attempts+1",
        ["add_triplet:new_nodes:10"] = "attempts+1",
        ["add_triplet:new_nodes:13"] = "logs",
        ["add_triplet:new_nodes:15"] = "attempts+1",
        ["add_triplet:new_nodes:23"] = "result",
        ["remove_episode:remove_with_group:11"] = "result",
        ["search:search_default:2"] = "result",
        ["summarize_saga:query_first:3"] = "result",
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

    // R4: the Python name of the class of an exception that the port throws. An exception of Errors.cs keeps the Python
    // class name with `Error` in place of `Exception`.
    internal static string PythonName(Exception e) => e switch
    {
        OperationCanceledException => "CancelledError",
        GraphitiException => e.GetType().Name[..^"Exception".Length] + "Error",
        InvalidOperationException => "RuntimeError",
        KeyNotFoundException => "LookupError",
        NotImplementedException => "NotImplementedError",
        ValidationException => "ValidationError",
        ArgumentException => "ValueError",
        _ => e.GetType().Name,
    };

    [Fact]
    public void The_module_constants_are_the_constants_of_Python()
    {
        var constants = Dict(GraphitiValues.Document["constants"]);
        Assert.Equal((long)constants["SAGA_SUMMARY_MAX_EPISODE_CHARS"]!, GraphitiModule.SagaSummaryMaxEpisodeChars.Value);
        Assert.Equal(GraphitiModule.SagaSummaryMaxEpisodeBytes, (long)constants["SAGA_SUMMARY_MAX_EPISODE_BYTES"]!);
        Assert.Equal((long)constants["SAGA_SUMMARY_MAX_PROMPT_BYTES"]!, GraphitiModule.SagaSummaryMaxPromptBytes.Value);
        Assert.Equal(GraphitiModule.MaxSagaEpisodesForSummary, (long)constants["MAX_SAGA_EPISODES_FOR_SUMMARY"]!);
        Assert.Equal((long)constants["SUMMARY_REBUILD_BATCH_SIZE"]!, GraphitiModule.SummaryRebuildBatchSize.Value);
        Assert.Equal(GraphitiModule.SagaSummaryMaxTokens, (long)constants["SAGA_SUMMARY_MAX_TOKENS"]!);
        Assert.Equal(GraphitiModule.SagaSummarySchemaWrapper, (string)constants["_SAGA_SUMMARY_SCHEMA_WRAPPER"]!);
    }

    [Fact]
    public void The_recording_reaches_each_member_that_the_port_can_reach()
    {
        var names = SyncCases.Concat(AsyncCases).Select(c => (string)Dict(c["in"])["fn"]!).ToHashSet(StringComparer.Ordinal);
        foreach (var name in new[]
        {
            "_validate_saga_group", "_saga_summary_messages_utf8_bytes", "select_saga_summary_episodes", "__init__",
            "_resolve_request_scope", "_concurrent_ingestion_enabled", "close", "build_indices_and_constraints",
            "retrieve_episodes", "_validate_saga_scope", "_get_or_create_saga", "_find_saga",
            "_saga_get_previous_episode_uuid", "_saga_get_episode_contents", "summarize_saga", "_extract_and_resolve_nodes",
            "add_episode", "add_episode_bulk", "_resolve_nodes_and_edges_bulk", "build_communities", "search", "_search",
            "search_", "get_nodes_and_edges_by_episode", "add_triplet", "remove_episode",
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
        using var source = new CancellationTokenSource();
        using (PortServices.Use(loggerFactory: new GraphitiLogCapture(events), time: new MaintenanceFixedClock(GraphitiValues.FixedNow.ToDateTimeOffset())))
        {
            try
            {
                output["ok"] = RunSync(input, events, source);
            }
            catch (Exception e)
            {
                output["error"] = new OrderedDictionary<string, object?> { ["type"] = PythonName(e), ["text"] = e.Message };
            }
        }

        output["events"] = events;
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

        // Each fake call got the token of the call (R25).
        foreach (var (_, token) in run.Tokens)
        {
            Assert.Equal(source.Token, token);
        }
    }

    // The theory that cancels at call k, the last call included: the fake that makes call k records it and cancels the
    // token source. Python ran the same cancel. The test compares the count of attempts, the error, and the counted calls
    // and the log lines after call k.
    [Theory(Timeout = 10000)]
    [MemberData(nameof(CancelPoints))]
    public async Task Each_later_awaited_call_gets_the_token_and_no_call_follows_a_cancel(int index, int k)
    {
        var input = Dict(AsyncCases[index]["in"]);
        var expected = Dict(List(Dict(AsyncCases[index]["out"])["cancel"]).Select(Dict).Single(p => (long)p["k"]! == k));
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var (run, output) = await RunAsync(input, source, k);

        // The events of the run before placeholders: the recorder numbers the new uuids of the events after k alone.
        var actual = new OrderedDictionary<string, object?>
        {
            ["k"] = (long)k,
            ["attempts"] = (long)run.Attempts,
            ["count"] = (long)run.Count,
            ["error"] = output.GetValueOrDefault("error") is OrderedDictionary<string, object?> error ? error["type"] : null,
            ["after"] = Placeholders(EventsAfter(run.Events, k), []),
        };
        var key = $"{(string)input["fn"]!}:{(string)input["name"]!}:{k}";
        var kind = CancelDifference(expected, actual);
        var listed = LateCancel.GetValueOrDefault(key, "same");
        Assert.True(kind == listed, $"{key}: listed as {listed}, but the port is {kind}\n{PyJson.Dumps(expected)}\n{PyJson.Dumps(actual)}");
    }

    // Each entry of LateCancel names a recorded cancel point.
    [Fact]
    public void Each_late_cancel_entry_names_a_recorded_cancel_point()
    {
        var points = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in AsyncCases.Select(Dict))
        {
            var input = Dict(item["in"]);
            foreach (var point in Dict(item["out"]).GetValueOrDefault("cancel") as List<object?> ?? [])
            {
                points.Add($"{(string)input["fn"]!}:{(string)input["name"]!}:{(long)Dict(point)["k"]!}");
            }
        }

        Assert.All(LateCancel.Keys, key => Assert.Contains(key, points));
    }

    // The kind of the difference at a cancel point. "same": the port equals Python. Each other kind is the late cancel
    // of GAPS F33, where the events of Python after call k are a part of the events of the port, in their order, and the
    // count of counted calls is the same. "result": the cancel came at the last call, and the port returns where Python
    // raises CancelledError. "logs": the port writes more log lines. "attempts+N": the port makes N more attempts.
    // "other": any other difference, which no entry can name.
    private static string CancelDifference(OrderedDictionary<string, object?> expected, OrderedDictionary<string, object?> actual)
    {
        if (PyJson.Dumps(expected) == PyJson.Dumps(actual))
        {
            return "same";
        }

        var expectedAfter = List(expected["after"]).Select(x => PyJson.Dumps(x)).ToList();
        var actualAfter = List(actual["after"]).Select(x => PyJson.Dumps(x)).ToList();
        var next = 0;
        var extraKinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (item, i) in actualAfter.Select((x, i) => (x, i)))
        {
            if (next < expectedAfter.Count && item == expectedAfter[next])
            {
                next++;
            }
            else
            {
                extraKinds.Add((string)Dict(List(actual["after"])[i])["kind"]!);
            }
        }

        if (next != expectedAfter.Count || !Equals(expected["count"], actual["count"]))
        {
            return "other";
        }

        var extraAttempts = (long)actual["attempts"]! - (long)expected["attempts"]!;
        if (Equals(expected["error"], actual["error"]))
        {
            return extraAttempts > 0 ? $"attempts+{extraAttempts}"
                : extraAttempts == 0 && extraKinds.SetEquals(["log"]) ? "logs" : "other";
        }

        return expected["error"] is "CancelledError" && actual["error"] is null && extraAttempts == 0 ? "result" : "other";
    }

    // A test with no Python original: each async method with a canceled token throws OperationCanceledException (R8)
    // and makes no counted fake call.
    [Theory(Timeout = 10000)]
    [MemberData(nameof(CancelableCaseIndexes))]
    public async Task Each_async_method_stops_at_a_canceled_token(int index)
    {
        var input = Dict(AsyncCases[index]["in"]);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var (run, output) = await RunAsync(input, source, -1);
        Assert.Equal("CancelledError", Dict(output["error"])["type"]);
        Assert.Equal(0, run.Count);
    }

    // `events_after` of the recorder: the counted calls and the log lines after counted call k.
    private static List<object?> EventsAfter(List<object?> events, int k)
    {
        var seen = 0;
        var after = new List<object?>();
        foreach (var item in events.Select(Dict))
        {
            var kind = (string)item["kind"]!;
            var counted = kind is not ("log" or "session" or "session_close" or "span" or "span_attrs" or "span_status" or "span_exception" or "span_end" or "clone");
            if (seen > k && (counted || kind == "log"))
            {
                after.Add(item);
            }

            if (counted)
            {
                seen++;
            }
        }

        return after;
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

    private static async Task<(GraphitiRun Run, OrderedDictionary<string, object?> Output)> RunAsync(
        OrderedDictionary<string, object?> input,
        CancellationTokenSource source,
        int cancelAt)
    {
        var run = new GraphitiRun(input, source, cancelAt);
        var limits = (SearchConfigRecipes.EdgeHybridSearchRrf.Limit, SearchConfigRecipes.EdgeHybridSearchNodeDistance.Limit);
        var output = new OrderedDictionary<string, object?>();
        var patch = input.GetValueOrDefault("patch") as OrderedDictionary<string, object?> ?? new();
        using var episodeChars = patch.TryGetValue("SAGA_SUMMARY_MAX_EPISODE_CHARS", out var chars)
            ? GraphitiModule.SagaSummaryMaxEpisodeChars.Use((long)chars!)
            : null;
        using var promptBytes = patch.TryGetValue("SAGA_SUMMARY_MAX_PROMPT_BYTES", out var bytes)
            ? GraphitiModule.SagaSummaryMaxPromptBytes.Use((long)bytes!)
            : null;
        using (PortServices.Use(loggerFactory: new GraphitiLogCapture(run.Events), time: new MaintenanceFixedClock(GraphitiValues.FixedNow.ToDateTimeOffset())))
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
        (SearchConfigRecipes.EdgeHybridSearchRrf.Limit, SearchConfigRecipes.EdgeHybridSearchNodeDistance.Limit) = limits;
        return (run, Dict(Placeholders(output, [])));
    }

    private static OrderedDictionary<string, object?> InitView(GraphitiCore graphiti, GraphitiRun run)
    {
        var clients = graphiti.Clients;
        var tracer = graphiti.Tracer;
        return new()
        {
            ["store_raw_episode_content"] = graphiti.StoreRawEpisodeContent,
            ["max_coroutines"] = graphiti.MaxCoroutines,
            ["same_driver"] = ReferenceEquals(graphiti.Driver, run.Driver),
            ["same_llm"] = ReferenceEquals(graphiti.LlmClient, run.Llm),
            ["llm_is_runtime_client"] = graphiti.LlmRuntime is not null && ReferenceEquals(graphiti.LlmClient, graphiti.LlmRuntime.Client),
            ["has_runtime"] = graphiti.LlmRuntime is not null,
            ["prompt_library"] = graphiti.PromptLibrary is null
                ? null
                : graphiti.LlmRuntime is not null && ReferenceEquals(graphiti.PromptLibrary, graphiti.LlmRuntime.Library) ? "runtime" : "wrapped",
            ["same_embedder"] = ReferenceEquals(graphiti.Embedder, run.Embedder),
            ["same_cross_encoder"] = ReferenceEquals(graphiti.CrossEncoder, run.CrossEncoder),
            ["tracer"] = tracer.GetType().Name,
            ["span_prefix"] = GraphitiRun.SpanPrefix(tracer),
            ["llm_tracer"] = ReferenceEquals(graphiti.LlmRuntime is not null ? graphiti.LlmRuntime.Client.Tracer : graphiti.LlmClient.Tracer, tracer),
            ["clients"] = new List<object?>
            {
                ReferenceEquals(clients.Driver, graphiti.Driver),
                ReferenceEquals(clients.LlmClient, graphiti.LlmClient),
                ReferenceEquals(clients.Embedder, graphiti.Embedder),
                ReferenceEquals(clients.CrossEncoder, graphiti.CrossEncoder),
                ReferenceEquals(clients.Tracer, tracer),
                ReferenceEquals(clients.PromptLibrary, graphiti.PromptLibrary),
                ReferenceEquals(clients.LlmRuntime, graphiti.LlmRuntime),
                clients.MaxCoroutines,
            },
            ["concurrent_ingestion"] = graphiti.ConcurrentIngestion?.Window,
            ["namespaces"] = new List<object?> { graphiti.Nodes.GetType().Name, graphiti.Edges.GetType().Name },
            ["token_tracker"] = ReferenceEquals(graphiti.TokenTracker, graphiti.LlmClient.TokenTracker),
        };
    }

    private static OrderedDictionary<string, object?> RunSync(OrderedDictionary<string, object?> input, List<object?> events, CancellationTokenSource source)
    {
        var fn = (string)input["fn"]!;
        switch (fn)
        {
            case "_validate_saga_group":
                GraphitiModule.ValidateSagaGroup(SagaArg(input["saga"]), (string)input["group_id"]!);
                return new();

            case "_saga_summary_messages_utf8_bytes":
                return new()
                {
                    ["bytes"] = GraphitiModule.SagaSummaryMessagesUtf8Bytes(
                        [.. List(input["contents"]).Cast<string>().Select(c => new Message { Role = "user", Content = c })]),
                };

            case "select_saga_summary_episodes":
                {
                    List<(string, PyDateTime?, PyDateTime)> data =
                        [.. List(input["data"]).Select(List).Select(d => ((string)d[0]!, (PyDateTime?)Decode(d[1]), (PyDateTime)Decode(d[2])!))];
                    var (selected, size, dropped) = input.TryGetValue("max_episode_chars", out var maxChars)
                        ? GraphitiModule.SelectSagaSummaryEpisodes(data, (long)maxChars!)
                        : GraphitiModule.SelectSagaSummaryEpisodes(data);
                    return new()
                    {
                        ["selected"] = selected.Select(s => (object?)new List<object?> { s.Item1, Dt(s.Item2), Dt(s.Item3) }).ToList(),
                        ["bytes"] = size,
                        ["dropped"] = dropped,
                    };
                }

            case "__init__":
                {
                    var run = new GraphitiRun(input, source, -1);
                    return InitView(run.MakeGraphiti(Dict(input["init"])), run);
                }

            case "_resolve_request_scope":
                {
                    var run = new GraphitiRun(
                        new() { ["database"] = input["database"], ["provider"] = input.GetValueOrDefault("provider") ?? "neo4j" }, source, -1);
                    var graphiti = run.MakeGraphiti(new());
                    var (groupId, driver, clients) = graphiti.ResolveRequestScope((string?)input["group_id"]);
                    events.AddRange(run.Events);
                    return new()
                    {
                        ["group_id"] = groupId,
                        ["database"] = driver.Database,
                        ["same_driver"] = ReferenceEquals(driver, graphiti.Driver),
                        ["same_clients"] = ReferenceEquals(clients, graphiti.Clients),
                        ["clients_driver"] = ReferenceEquals(clients.Driver, driver),
                        ["shared_database"] = graphiti.Driver.Database,
                        ["shared_clients_driver"] = ReferenceEquals(graphiti.Clients.Driver, graphiti.Driver),
                        ["same_llm"] = ReferenceEquals(clients.LlmClient, graphiti.LlmClient),
                        ["same_embedder"] = ReferenceEquals(clients.Embedder, graphiti.Embedder),
                        ["events"] = run.Events.ToList(),
                    };
                }

            case "_concurrent_ingestion_enabled":
                {
                    var run = new GraphitiRun(new(), source, -1);
                    var init = input["config"] is null ? new OrderedDictionary<string, object?>() : new() { ["concurrent_ingestion"] = input["config"] };
                    return new() { ["enabled"] = run.MakeGraphiti(init).ConcurrentIngestionEnabled((string)input["group_id"]!) };
                }

            default:
                throw new InvalidOperationException("unknown function " + fn);
        }
    }

    private static GraphDriver? DriverArg(object? value, GraphitiRun run) => value is "other" ? run.Driver.Clone("other_db") : null;

    private static async Task<OrderedDictionary<string, object?>> RunCaseAsync(GraphitiRun run, OrderedDictionary<string, object?> input, CancellationToken token)
    {
        var init = new OrderedDictionary<string, object?>(input.GetValueOrDefault("init") as OrderedDictionary<string, object?> ?? new());
        var padded = init.GetValueOrDefault("prompt_library") is "padded";
        init["prompt_library"] = false;
        var graphiti = run.MakeGraphiti(init);
        if (padded)
        {
            var library = Lib.CreatePromptLibrary(new()
            {
                ["summarize_sagas"] = new()
                {
                    ["summarize_saga"] = context => new ChatPrompt
                    {
                        System = new SystemMessage { Content = new string('P', 14_000) },
                        User = new UserMessage { Content = string.Join("\n---\n", List(context["episodes"]).Cast<string>()) },
                    },
                },
            });
            graphiti.PromptLibrary = library;
            graphiti.Clients.PromptLibrary = library;
        }

        var tracer = new GraphitiRun.RecTracer(run);
        graphiti.Tracer = tracer;
        graphiti.Clients.Tracer = tracer;
        var a = input.GetValueOrDefault("args") is OrderedDictionary<string, object?> args ? DecodeDict(args) : new();
        var fn = (string)input["fn"]!;
        switch (fn)
        {
            case "close":
                await graphiti.CloseAsync(token);
                return new();

            case "build_indices_and_constraints":
                await graphiti.BuildIndicesAndConstraintsAsync(a.GetValueOrDefault("delete_existing") is true, token);
                return new();

            case "retrieve_episodes":
                {
                    var result = await graphiti.RetrieveEpisodesAsync(
                        (PyDateTime)a["reference_time"]!,
                        a.TryGetValue("last_n", out var lastN) ? (long)lastN! : 3,
                        Strings(a.GetValueOrDefault("group_ids")),
                        a.GetValueOrDefault("source") is string sourceName ? PyEnum.FromValue<EpisodeType>(sourceName) : null,
                        DriverArg(a.GetValueOrDefault("driver"), run),
                        (string?)a.GetValueOrDefault("saga"),
                        token);
                    return new() { ["episodes"] = result.Select(e => (object?)EpisodeView(e)).ToList() };
                }

            case "_validate_saga_scope":
                await GraphitiModule.ValidateSagaScopeAsync(SagaArg(a["saga"]), "g", graphiti.Driver, token);
                return new();

            case "_get_or_create_saga":
                {
                    var result = await graphiti.GetOrCreateSagaAsync(
                        (string)a["saga_name"]!, (string)a["group_id"]!, GraphitiValues.T2, DriverArg(a.GetValueOrDefault("driver"), run), token);
                    return new() { ["saga"] = SagaView(result) };
                }

            case "_find_saga":
                {
                    var result = await graphiti.FindSagaAsync((string)a["saga_name"]!, (string)a["group_id"]!, cancellationToken: token);
                    return new() { ["saga"] = result is null ? null : SagaView(result) };
                }

            case "_saga_get_previous_episode_uuid":
                return new()
                {
                    ["uuid"] = await graphiti.SagaGetPreviousEpisodeUuidAsync(
                        (string)a["saga_uuid"]!,
                        (string)a["current_episode_uuid"]!,
                        (string)a["group_id"]!,
                        DriverArg(a.GetValueOrDefault("driver"), run),
                        token),
                };

            case "_saga_get_episode_contents":
                {
                    var result = await graphiti.SagaGetEpisodeContentsAsync(
                        (string)a["saga_uuid"]!,
                        (PyDateTime?)a.GetValueOrDefault("since"),
                        a.TryGetValue("limit", out var limit) ? (long)limit! : 200,
                        cancellationToken: token);
                    return new()
                    {
                        ["contents"] = result?.Select(c => (object?)new List<object?> { c.Item1, Dt(c.Item2), Dt(c.Item3) }).ToList(),
                    };
                }

            case "summarize_saga":
                {
                    var result = await graphiti.SummarizeSagaAsync((string)a["saga_id"]!, (string?)a.GetValueOrDefault("group_id"), token);
                    return new() { ["saga"] = SagaView(result) };
                }

            case "_extract_and_resolve_nodes":
                {
                    EpisodeOrEpisodes episode = a["episode"] is List<object?> many
                        ? new EpisodeOrEpisodes.Many { Episodes = [.. many.Select(Episode)] }
                        : new EpisodeOrEpisodes.One { Episode = Episode(a["episode"]) };
                    var (nodes, uuidMap, duplicates, indexMap) = await graphiti.ExtractAndResolveNodesAsync(
                        episode,
                        [.. List(a["previous_episodes"]).Select(Episode)],
                        Types(a["entity_types"]),
                        Strings(a["excluded_entity_types"]),
                        Identity(a.GetValueOrDefault("identity_properties")),
                        token);
                    return new()
                    {
                        ["nodes"] = nodes.Select(n => (object?)NodeView(n)).ToList(),
                        ["uuid_map"] = Encode(uuidMap),
                        ["duplicates"] = duplicates.Select(d => (object?)new List<object?> { d.Item1.Uuid, d.Item2.Uuid }).ToList(),
                        ["index_map"] = Encode(indexMap),
                    };
                }

            case "_resolve_nodes_and_edges_bulk":
                {
                    // The same uuid in two episodes is one object, as dedupe_nodes_bulk gives it.
                    var shared = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
                    EntityNode NodeOf(object? data)
                    {
                        var uuid = (string)Dict(data)["uuid"]!;
                        if (!shared.TryGetValue(uuid, out var value))
                        {
                            value = Node(data);
                            shared[uuid] = value;
                        }

                        return value;
                    }

                    var nodesByEpisode = new OrderedDictionary<string, List<EntityNode>>();
                    foreach (var (key, value) in Dict(a["nodes_by_episode"]))
                    {
                        nodesByEpisode[key] = [.. List(value).Select(NodeOf)];
                    }

                    var edgesByEpisode = new OrderedDictionary<string, List<EntityEdge>>();
                    foreach (var (key, value) in Dict(a["edges_by_episode"]))
                    {
                        edgesByEpisode[key] = [.. List(value).Select(Edge)];
                    }

                    var (nodes, resolved, invalidated, uuidMap) = await graphiti.ResolveNodesAndEdgesBulkAsync(
                        nodesByEpisode,
                        edgesByEpisode,
                        [.. List(a["episode_context"]).Select(List).Select(c => (Episode(c[0]), List(c[1]).Select(Episode).ToList()))],
                        Types(a["entity_types"]),
                        Types(a["edge_types"]),
                        TypeMap(a["edge_type_map"])!,
                        [.. List(a["episodes"]).Select(Episode)],
                        identityProperties: Identity(a["identity_properties"]),
                        hydrate: (bool)a["hydrate"]!,
                        cancellationToken: token);
                    return new()
                    {
                        ["nodes"] = nodes.Select(n => (object?)NodeView(n)).ToList(),
                        ["resolved"] = resolved.Select(e => (object?)EdgeView(e)).ToList(),
                        ["invalidated"] = invalidated.Select(e => (object?)EdgeView(e)).ToList(),
                        ["uuid_map"] = Encode(uuidMap),
                    };
                }

            case "add_episode":
                {
                    var result = await graphiti.AddEpisodeAsync(
                        (string)a["name"]!,
                        (string)a["episode_body"]!,
                        (string)a["source_description"]!,
                        (PyDateTime)a["reference_time"]!,
                        a.GetValueOrDefault("source") is string sourceName ? PyEnum.FromValue<EpisodeType>(sourceName) : EpisodeType.Message,
                        (string?)a.GetValueOrDefault("group_id"),
                        (string?)a.GetValueOrDefault("uuid"),
                        a.GetValueOrDefault("update_communities") is true,
                        Types(a.GetValueOrDefault("entity_types")),
                        Strings(a.GetValueOrDefault("excluded_entity_types")),
                        Strings(a.GetValueOrDefault("previous_episode_uuids")),
                        Types(a.GetValueOrDefault("edge_types")),
                        TypeMap(a.GetValueOrDefault("edge_type_map")),
                        (string?)a.GetValueOrDefault("custom_extraction_instructions"),
                        SagaArg(a.GetValueOrDefault("saga")),
                        (string?)a.GetValueOrDefault("saga_previous_episode_uuid"),
                        Identity(a.GetValueOrDefault("identity_properties")),
                        token);
                    return new()
                    {
                        ["episode"] = EpisodeView(result.Episode),
                        ["episodic_edges"] = result.EpisodicEdges.Select(e => (object?)EpisodicEdgeView(e)).ToList(),
                        ["nodes"] = result.Nodes.Select(n => (object?)NodeView(n)).ToList(),
                        ["edges"] = result.Edges.Select(e => (object?)EdgeView(e)).ToList(),
                        ["communities"] = result.Communities.Select(c => (object?)c.Uuid).ToList(),
                        ["community_edges"] = result.CommunityEdges.Select(e => (object?)e.Uuid).ToList(),
                    };
                }

            case "add_episode_bulk":
                {
                    List<RawEpisode> episodes =
                    [
                        .. List(a["bulk_episodes"]).Select(Dict).Select(e => new RawEpisode
                        {
                            Name = (string)e["name"]!,
                            Content = (string)e["content"]!,
                            SourceDescription = (string)e["source_description"]!,
                            Source = PyEnum.FromValue<EpisodeType>((string)e["source"]!),
                            ReferenceTime = (PyDateTime)e["reference_time"]!,
                            Uuid = (string?)e["uuid"],
                        }),
                    ];
                    var result = await graphiti.AddEpisodeBulkAsync(
                        episodes,
                        (string?)a.GetValueOrDefault("group_id"),
                        Types(a.GetValueOrDefault("entity_types")),
                        Strings(a.GetValueOrDefault("excluded_entity_types")),
                        Types(a.GetValueOrDefault("edge_types")),
                        TypeMap(a.GetValueOrDefault("edge_type_map")),
                        a.GetValueOrDefault("strict_edge_types") is true,
                        (string?)a.GetValueOrDefault("custom_extraction_instructions"),
                        SagaArg(a.GetValueOrDefault("saga")),
                        a.GetValueOrDefault("use_combined_extraction") is not false,
                        Identity(a.GetValueOrDefault("identity_properties")),
                        token);
                    return new()
                    {
                        ["episodes"] = result.Episodes.Select(e => (object?)EpisodeView(e)).ToList(),
                        ["episodic_edges"] = result.EpisodicEdges.Select(e => (object?)EpisodicEdgeView(e)).ToList(),
                        ["nodes"] = result.Nodes.Select(n => (object?)NodeView(n)).ToList(),
                        ["edges"] = result.Edges.Select(e => (object?)EdgeView(e)).ToList(),
                        ["communities"] = (long)result.Communities.Count,
                        ["community_edges"] = (long)result.CommunityEdges.Count,
                    };
                }

            case "build_communities":
                {
                    var (nodes, edges) = await graphiti.BuildCommunitiesAsync(
                        Strings(a.GetValueOrDefault("group_ids")), DriverArg(a.GetValueOrDefault("driver"), run), token);
                    return new()
                    {
                        ["nodes"] = nodes.Select(n => (object?)n.Uuid).ToList(),
                        ["edges"] = edges.Select(e => (object?)e.Uuid).ToList(),
                    };
                }

            case "search" or "_search" or "search_":
                {
                    var filter = a.GetValueOrDefault("search_filter") is OrderedDictionary<string, object?> f ? SearchFilters.ModelValidate(f) : null;
                    var config = a.GetValueOrDefault("config") is string recipe ? Recipe(recipe) : null;
                    var groupIds = Strings(a.GetValueOrDefault("group_ids"));
                    var query = (string)a["query"]!;
                    if (fn == "search")
                    {
                        var edges = await graphiti.SearchAsync(
                            query,
                            (string?)a.GetValueOrDefault("center_node_uuid"),
                            groupIds,
                            a.TryGetValue("num_results", out var numResults) ? (long)numResults! : SearchConfigModule.DefaultSearchLimit,
                            filter,
                            cancellationToken: token);
                        return new() { ["edges"] = edges.Select(e => (object?)EdgeView(e)).ToList() };
                    }

                    var result = fn == "_search"
                        ? await graphiti.PrivateSearchAsync(
                            query, config!, groupIds, (string?)a.GetValueOrDefault("center_node_uuid"), searchFilter: filter, cancellationToken: token)
                        : await graphiti.AdvancedSearchAsync(
                            query,
                            config,
                            groupIds,
                            searchFilter: filter,
                            queryVector: a.GetValueOrDefault("query_vector") is List<object?> vector ? [.. vector.Select(x => (double)x!)] : null,
                            cancellationToken: token);
                    return SearchView(result);
                }

            case "get_nodes_and_edges_by_episode":
                return SearchView(await graphiti.GetNodesAndEdgesByEpisodeAsync(
                    [.. List(a["episode_uuids"]).Cast<string>()], (string?)a.GetValueOrDefault("group_id"), token));

            case "add_triplet":
                {
                    var sourceNode = Node(a["source_node"]);
                    var targetNode = Node(a["target_node"]);
                    var edge = Edge(a["edge"]);
                    var result = await graphiti.AddTripletAsync(
                        sourceNode,
                        edge,
                        targetNode,
                        a.TryGetValue("episode", out var episode) ? Episode(episode) : null,
                        Identity(a.GetValueOrDefault("identity_properties")),
                        token);
                    return new()
                    {
                        ["nodes"] = result.Nodes.Select(n => (object?)NodeView(n)).ToList(),
                        ["edges"] = result.Edges.Select(e => (object?)EdgeView(e)).ToList(),
                        ["source"] = NodeView(sourceNode),
                        ["target"] = NodeView(targetNode),
                        ["edge"] = EdgeView(edge),
                    };
                }

            case "remove_episode":
                await graphiti.RemoveEpisodeAsync((string)a["episode_uuid"]!, (string?)a.GetValueOrDefault("group_id"), token);
                return new();

            default:
                throw new InvalidOperationException("unknown function " + fn);
        }
    }
}
