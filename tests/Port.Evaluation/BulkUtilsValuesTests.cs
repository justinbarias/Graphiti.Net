// Evaluation test for graphiti_core/utils/bulk_utils.py (port/RULES.md, R23, R25).
// values/bulk_utils.json holds the calls that port/tools/record_bulk_utils.py recorded from Python: the sync functions
// with generated inputs, and the async functions with fakes for the LLM client, the embedder, and the graph driver
// with its session, its transaction, and its interfaces. The calls and the log lines of each case are one ordered
// list.
namespace Graphiti.Port.Evaluation;

using System.Runtime.ExceptionServices;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.BulkUtilsValues;
using static Graphiti.Port.Evaluation.EdgeOperationsValues;

public class BulkUtilsValuesTests
{
    private static readonly List<OrderedDictionary<string, object?>> AsyncCases =
        List(BulkUtilsValues.Document["async_cases"]).Select(Dict).ToList();

    // The scenarios of the cancellation tests: one recorded case of each async function, with the count of its counted
    // fake calls with no cancel.
    private static readonly Dictionary<string, int> Scenarios = new()
    {
        ["_entity_node_record"] = 1,
        ["update_entity_edges_tx"] = 4,
        ["update_entity_nodes_tx"] = 5,
        ["add_nodes_and_edges_bulk_tx"] = 6,
        ["add_nodes_and_edges_bulk_tx_cypher"] = 9,
        ["add_nodes_and_edges_bulk"] = 18,
        ["add_nodes_and_edges_bulk_ops"] = 19,
        ["add_nodes_and_edges_bulk_timeout"] = 20,
        ["retrieve_previous_episodes_bulk"] = 24,
        ["extract_nodes_and_edges_bulk"] = 27,
        ["dedupe_nodes_bulk"] = 32,
        ["dedupe_nodes_bulk_llm"] = 36,
        ["dedupe_edges_bulk"] = 39,
        ["add_nodes_and_edges_bulk_neo4j_timeout"] = 44,
        ["execute_write_with_timeout_neo4j"] = 48,
        ["execute_write_with_timeout_relate"] = 53,
    };

    public static TheoryData<int> AsyncCaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < AsyncCases.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    public static TheoryData<string> ScenarioNames() => [.. Scenarios.Keys];

    public static TheoryData<string, int> LaterCalls()
    {
        var data = new TheoryData<string, int>();
        foreach (var (name, index) in Scenarios)
        {
            var calls = (long)Dict(AsyncCases[index]["out"])["count"]!;
            for (var k = 0; k < calls - 1; k++)
            {
                data.Add(name, k);
            }
        }

        return data;
    }

    [Fact]
    public void Each_module_constant_has_the_value_of_python()
    {
        var constants = Dict(BulkUtilsValues.Document["constants"]);
        Assert.Equal(BulkUtils.ChunkSize, (long)constants["CHUNK_SIZE"]!);
        Assert.Equal(List(constants["INGESTION_INTERFACE_OPERATIONS"]).Select(x => (string)x!), BulkUtils.IngestionInterfaceOperations);
    }

    [Fact]
    public void The_recording_reaches_each_function_and_branch()
    {
        var cases = List(BulkUtilsValues.Document["cases"]).Select(Dict).ToList();
        var names = cases.Select(c => (string)c["f"]!).ToHashSet();
        foreach (var name in new[] { "_build_directed_uuid_map", "compress_uuid_map", "resolve_edge_pointers", "missing_ingestion_interface_operations" })
        {
            Assert.Contains(name, names);
        }

        // A chain of three duplicates and a cycle, for both maps.
        Assert.Contains(cases, c => (string)c["f"]! == "compress_uuid_map" && PyJson.Dumps(Dict(c["in"])["pairs"]) == """[["a", "b"], ["b", "c"], ["c", "d"]]""");
        Assert.Contains(cases, c => (string)c["f"]! == "compress_uuid_map" && PyJson.Dumps(Dict(c["in"])["pairs"]) == """[["a", "b"], ["b", "c"], ["c", "a"]]""");

        // resolve_edge_pointers drops an edge and writes its log line.
        Assert.Contains(cases, c => (string)c["f"]! == "resolve_edge_pointers" && List(Dict(c["out"])["log"]).Count > 0);

        var fns = AsyncCases.Select(c => (string)Dict(c["in"])["fn"]!).ToHashSet();
        foreach (var fn in new[]
        {
            "_entity_node_record", "_entity_edge_record", "update_entity_edges_tx", "update_entity_nodes_tx",
            "add_nodes_and_edges_bulk_tx", "add_nodes_and_edges_bulk", "retrieve_previous_episodes_bulk",
            "extract_nodes_and_edges_bulk", "dedupe_nodes_bulk", "dedupe_edges_bulk", "execute_write_with_timeout",
        })
        {
            Assert.Contains(fn, fns);
        }

        // A limit of 2 with more calls than the limit, and the same calls with no limit.
        Assert.Contains(AsyncCases, c => Dict(c["in"]).GetValueOrDefault("max_coroutines") is 2L && (long)Dict(c["out"])["max_in_flight"]! == 2 && (long)Dict(c["out"])["count"]! > 2);
        Assert.Contains(AsyncCases, c => Dict(c["in"]).GetValueOrDefault("max_coroutines") is null && (long)Dict(c["out"])["max_in_flight"]! > 2);

        // The timeout of a write: a session with query_timeout_ms, and a limit that ends the write.
        Assert.Contains(AsyncCases, c => Dict(c["in"]).GetValueOrDefault("timeout_session") is true && Dict(c["out"]).ContainsKey("ok"));
        Assert.Contains(AsyncCases, c => Dict(c["out"]).GetValueOrDefault("error") is OrderedDictionary<string, object?> e && (string)e["type"]! == "TimeoutError");

        // Neo4j: the limit reaches the session as the transaction timeout (decision D52), for 7.5, 2.5, 0, and a
        // negative limit, and the session closes after a work function that raises.
        foreach (var limit in new[] { 7.5, 2.5, 0.0, -1.0 })
        {
            Assert.Contains(AsyncCases, c => (string?)Dict(c["in"]).GetValueOrDefault("provider") == "neo4j" && List(Dict(c["out"])["events"]).Any(e => Dict(e).GetValueOrDefault("timeout") is double t && t == limit));
        }

        Assert.Contains(AsyncCases, c => (string?)Dict(c["in"]).GetValueOrDefault("provider") == "neo4j" && Dict(c["out"]).GetValueOrDefault("error") is OrderedDictionary<string, object?> e && (string)e["type"]! == "RuntimeError" && (string?)Dict(List(Dict(c["out"])["events"])[^1])["kind"] == "close");

        // The fix round: a work function that the limit cancels (a rollback with CancelledError, and no write), a limit of
        // 1.0019 (1001 ms), a limit below one millisecond (1 ms), an IndexError with no call, the fallback to the canonical
        // node, the similarity limit, two groups, and a call of each method with its defaults.
        List<object?> EventsOf(OrderedDictionary<string, object?> c) => List(Dict(c["out"])["events"]);
        Assert.Contains(AsyncCases, c => Dict(c["in"]).GetValueOrDefault("slow_embed") is true && EventsOf(c).Any(e => (string?)Dict(e)["kind"] == "rollback" && (string?)Dict(e)["error"] == "CancelledError") && Dict(c["out"]).ContainsKey("error"));
        foreach (var ms in new[] { 1001L, 1L })
        {
            Assert.Contains(AsyncCases, c => EventsOf(c).Any(e => (string?)Dict(e)["kind"] == "close" && Dict(e).GetValueOrDefault("query_timeout_ms") is long v && v == ms) && Dict(c["in"]).GetValueOrDefault("write_timeout") is double and < 1.1);
        }

        Assert.Contains(AsyncCases, c => Dict(c["out"]).GetValueOrDefault("error") is OrderedDictionary<string, object?> e && (string)e["type"]! == "IndexError" && (long)Dict(c["out"])["count"]! == 0);
        Assert.Contains(AsyncCases, c => EventsOf(c).Any(e => (string?)Dict(e)["kind"] == "log" && ((string?)Dict(e)["message"])!.StartsWith("Canonical node", StringComparison.Ordinal)));
        Assert.Contains(AsyncCases, c => Dict(c["in"]).GetValueOrDefault("vectors") is OrderedDictionary<string, object?> v && v.ContainsKey("Dark night"));
        Assert.Contains(AsyncCases, c => Dict(c["in"]).GetValueOrDefault("defaults") is true && (string)Dict(c["in"])["fn"]! == "extract_nodes_and_edges_bulk" && Dict(c["in"])["combined"] is false);
    }

    // A chain of decreasing UUIDs: the pair k+1 -> k for k from n-2 down to 0. The union goes onto the smallest UUID, so
    // the chain has depth n, and the first Find of compress_uuid_map walks all of it.
    private static List<(string, string)> ChainPairs(int n)
    {
        var uuids = Enumerable.Range(0, n).Select(i => i.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)).ToList();
        return [.. Enumerable.Range(0, n - 1).Reverse().Select(k => (uuids[k + 1], uuids[k]))];
    }

    private static T OnThread<T>(int stackBytes, Func<T> body)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(
            () =>
            {
                try
                {
                    result = body();
                }
                catch (Exception e)
                {
                    error = e;
                }
            },
            stackBytes);
        thread.Start();
        Assert.True(thread.Join(30_000), "the thread did not end");
        if (error is not null)
        {
            ExceptionDispatchInfo.Capture(error).Throw();
        }

        return result!;
    }

    // PORT: Python raises RecursionError for a chain of about 1000 and more (GAPS F43). The port goes deeper and returns,
    // as long as the stack holds. The map is the one of the chain, with a literal: each key resolves to the smallest UUID.
    [Fact]
    public void compress_uuid_map_returns_for_a_chain_of_3000_where_python_raises_RecursionError()
    {
        var map = OnThread(16 << 20, () => BulkUtils.CompressUuidMap(ChainPairs(3000)));

        Assert.Equal(3000, map.Count);
        Assert.All(map.Values, value => Assert.Equal("000000", value));
        Assert.Equal("002999", map.Keys.First());
    }

    // PORT: with a chain that is deeper than the stack holds, Find throws InsufficientExecutionStackException in place of
    // a process stop (GAPS F43). The thread has a stack of 1 MiB, so the guard fires long before the chain ends, and the
    // test host lives.
    [Fact]
    public void compress_uuid_map_throws_InsufficientExecutionStackException_for_a_chain_that_the_stack_cannot_hold()
    {
        Assert.Throws<InsufficientExecutionStackException>(() => OnThread(1 << 20, () => BulkUtils.CompressUuidMap(ChainPairs(200_000))));
    }

    // A test with no Python original (R24): on FalkorDB and Neptune the query builder of the entity nodes returns a list of
    // queries. Python passes the list to tx.run. The port has no transaction that takes a list, so it marks the site with a
    // TODO(port) and throws NotSupportedException. The episode query before it still runs.
    [Theory(Timeout = 5000)]
    [InlineData("falkordb")]
    [InlineData("neptune")]
    public async Task A_list_of_queries_for_the_entity_nodes_throws_NotSupportedException_with_the_marker(string provider)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new BulkUtilsRun(new() { ["provider"] = provider, ["ops"] = false }, source, cancelAt: -1);
        var node = new EntityNode { Name = "Embedded", GroupId = "g", Labels = ["Entity"], NameEmbedding = [0.5, 0.25] }.Validate();

        var update = await Assert.ThrowsAsync<NotSupportedException>(() => LoopGuard.RunAsync(() => BulkUtils.UpdateEntityNodesTxAsync(
            new BulkUtilsRun.FakeTx(run), [node], run.Embedder, run.Driver, source.Token)));
        Assert.StartsWith("TODO(port):", update.Message, StringComparison.Ordinal);
        Assert.Empty(run.Events);

        var add = await Assert.ThrowsAsync<NotSupportedException>(() => LoopGuard.RunAsync(() => BulkUtils.AddNodesAndEdgesBulkTxAsync(
            new BulkUtilsRun.FakeTx(run), [], [], [node], [], run.Embedder, run.Driver, source.Token)));
        Assert.StartsWith("TODO(port):", add.Message, StringComparison.Ordinal);
        Assert.Equal(["tx_run"], run.Events.Select(e => (string?)Dict(e)["kind"]));
    }

    [Fact]
    public void The_port_gives_each_recorded_sync_result_and_log_line_of_python()
    {
        var cases = List(BulkUtilsValues.Document["cases"]).Select(Dict).ToList();
        Assert.True(cases.Count >= 180);
        List<string> failures = [];
        foreach (var (index, item) in cases.Index())
        {
            var events = new List<object?>();
            var output = new OrderedDictionary<string, object?>();
            object? after = null;
            using (PortServices.Use(loggerFactory: new EdgeOperationsLogCapture(events), time: new MaintenanceFixedClock(BulkUtilsValues.FixedNow.ToDateTimeOffset())))
            {
                try
                {
                    output["ok"] = RunSync((string)item["f"]!, Dict(item["in"]), out after);
                }
                catch (Exception e) when (e is ArgumentException or InvalidCastException or KeyNotFoundException)
                {
                    output["error"] = new OrderedDictionary<string, object?> { ["type"] = e.GetType().Name };
                }
            }

            output["log"] = events;
            output["after"] = after;
            var expected = PyJson.Dumps(item["out"]);
            var got = PyJson.Dumps(output);
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
        var run = new BulkUtilsRun(input, source, cancelAt: -1);
        var output = new OrderedDictionary<string, object?>();
        using (PortServices.Use(loggerFactory: new EdgeOperationsLogCapture(run.Events), time: new MaintenanceFixedClock(BulkUtilsValues.FixedNow.ToDateTimeOffset())))
        {
            try
            {
                output["ok"] = await LoopGuard.RunAsync(() => CallAsync(run, source.Token));
            }
            catch (TimeoutException e)
            {
                output["error"] = new OrderedDictionary<string, object?> { ["type"] = "TimeoutError", ["text"] = e.Message };
            }
            catch (InvalidOperationException e)
            {
                // R4: RuntimeError is InvalidOperationException. The work function of a case raises it.
                output["error"] = new OrderedDictionary<string, object?> { ["type"] = "RuntimeError", ["text"] = e.Message };
            }
            catch (ArgumentOutOfRangeException)
            {
                // R4: IndexError is ArgumentOutOfRangeException. The text is the one of Python, as a test of an IndexError
                // compares the type only.
                output["error"] = new OrderedDictionary<string, object?> { ["type"] = "IndexError", ["text"] = "list index out of range" };
            }
        }

        output["events"] = run.Events;
        output["max_in_flight"] = (long)run.MaxInFlight;
        output["count"] = (long)run.Count;
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
        Assert.Equal(PyJson.Dumps(expected["count"]), PyJson.Dumps(actual["count"]));

        // Each fake got the token of the call (R25). With a write timeout on a provider other than Neo4j,
        // asyncio.wait_for gives the session write a token that the limit or the call cancels (R8), and the work
        // function of the write passes that token to each call that it makes: the embedder, the transaction, and the
        // interface. Python cancels the task of the work at the limit. On Neo4j the session gets the token of the call
        // with the timeout, and the server holds the limit (decision D52).
        var waitFor = (string?)input.GetValueOrDefault("provider") != "neo4j"
            && (input.GetValueOrDefault("write_timeout") is not null || (string)input["fn"]! == "execute_write_with_timeout");
        foreach (var (kind, token) in run.Tokens)
        {
            if (waitFor)
            {
                Assert.True(token.CanBeCanceled);
                Assert.NotEqual(source.Token, token);
                Assert.Equal(run.Tokens[0].Token, token);
            }
            else
            {
                Assert.Equal(source.Token, token);
            }
        }
    }

    // A test with no Python original: each async method with a canceled token throws OperationCanceledException (R8)
    // and makes no counted fake call.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(ScenarioNames))]
    public async Task Each_async_method_with_a_canceled_token_throws_OperationCanceledException(string name)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var run = new BulkUtilsRun(Dict(AsyncCases[Scenarios[name]]["in"]), source, cancelAt: -1);
        using (PortServices.Use(loggerFactory: new EdgeOperationsLogCapture(run.Events), time: new MaintenanceFixedClock(BulkUtilsValues.FixedNow.ToDateTimeOffset())))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));
            await LoopGuard.RunAsync(Turns);
        }

        Assert.Equal(0, run.Count);
    }

    // A test with no Python original: the token is canceled at fake call k. Call k is the last counted call that a
    // fake records, and the method throws OperationCanceledException (R8). The close of a session still runs.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(LaterCalls))]
    public async Task Each_later_awaited_call_gets_the_token_and_no_call_follows_a_cancel(string name, int k)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new BulkUtilsRun(Dict(AsyncCases[Scenarios[name]]["in"]), source, cancelAt: k);
        using (PortServices.Use(loggerFactory: new EdgeOperationsLogCapture(run.Events), time: new MaintenanceFixedClock(BulkUtilsValues.FixedNow.ToDateTimeOffset())))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));

            // The calls that a failed gather leaves run on. Give them turns before the count (R25).
            await LoopGuard.RunAsync(Turns);
        }

        Assert.Equal(k + 1, run.Count);
        var opened = run.Events.Count(e => (string?)Dict(e)["kind"] == "session");
        Assert.Equal(opened, run.Events.Count(e => (string?)Dict(e)["kind"] == "close"));
    }

    private static async Task Turns()
    {
        for (var i = 0; i < 50; i++)
        {
            await Task.Yield();
        }
    }

    // A C# class of the interface that overrides the named operations, as the recorder builds one with type().
    private static GraphOperationsInterface? Interface(object? overridden) => overridden switch
    {
        null => null,
        List<object?> { Count: 0 } => new NoOverride(),
        List<object?> { Count: 2 } => new TwoOverrides(),
        List<object?> { Count: 3 } => new ThreeOverrides(),
        _ => new AllOverrides(),
    };

    private static List<object?> RunSync(string f, OrderedDictionary<string, object?> input, out object? after)
    {
        after = null;
        switch (f)
        {
            case "_build_directed_uuid_map":
            case "compress_uuid_map":
                {
                    List<(string, string)> pairs = [.. List(input["pairs"]).Select(List).Select(p => ((string)p[0]!, (string)p[1]!))];
                    var result = f == "compress_uuid_map" ? BulkUtils.CompressUuidMap(pairs) : BulkUtils.BuildDirectedUuidMap(pairs);
                    return result.Select(e => (object?)new List<object?> { e.Key, e.Value }).ToList();
                }

            case "resolve_edge_pointers":
                {
                    var uuidMap = new OrderedDictionary<string, string>();
                    foreach (var (key, value) in Dict(input["uuid_map"]))
                    {
                        uuidMap[key] = (string)value!;
                    }

                    List<object?> result;
                    List<Graphiti.Core.Edge> edges;
                    if (input["episodic"] is true)
                    {
                        List<EpisodicEdge> episodic = [.. List(input["edges"]).Select(e =>
                        {
                            var data = DecodeDict(Dict(e));
                            data.Remove("name");
                            data.Remove("fact");
                            return EpisodicEdge.ModelValidate(data);
                        })];
                        edges = [.. episodic];
                        result = BulkUtils.ResolveEdgePointers(episodic, uuidMap).Select(e => (object?)e.Uuid).ToList();
                    }
                    else
                    {
                        List<EntityEdge> entity = [.. List(input["edges"]).Select(Edge)];
                        edges = [.. entity];
                        result = BulkUtils.ResolveEdgePointers(entity, uuidMap).Select(e => (object?)e.Uuid).ToList();
                    }

                    after = edges.Select(e => (object?)new List<object?> { e.SourceNodeUuid, e.TargetNodeUuid }).ToList();
                    return result;
                }

            case "missing_ingestion_interface_operations":
                {
                    using var source = new CancellationTokenSource();
                    var driver = new BulkUtilsRun(new() { ["ops"] = false }, source, -1).Driver;
                    driver.GraphOperationsInterface = Interface(input["overridden"]);
                    return BulkUtils.MissingIngestionInterfaceOperations(driver).Select(x => (object?)x).ToList();
                }

            default:
                throw new InvalidOperationException(f);
        }
    }

    private static double Number(object? value) => value switch
    {
        long l => l,
        double d => d,
        var other => throw new ArgumentException(Py.Repr(other)),
    };

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

    private static OrderedDictionary<string, GraphTypeDescriptor>? TypesOf(object? names) =>
        names is null
            ? null
            : List(names).Select(n => (string)n!).Aggregate(
                new OrderedDictionary<string, GraphTypeDescriptor>(),
                (acc, n) =>
                {
                    acc[n] = BulkUtilsValues.Models[n];
                    return acc;
                });

    private static List<(EpisodicNode, List<EpisodicNode>)> TuplesOf(object? items) =>
        [.. List(items).Select(List).Select(t => (Episode(t[0]), List(t[1]).Select(Episode).ToList()))];

    private static List<object?> NodeViews(IEnumerable<EntityNode> nodes) => nodes.Select(n => (object?)NodeViewWithEmbedding(n)).ToList();

    private static List<object?> EdgeViews(IEnumerable<EntityEdge> edges) => edges.Select(e => (object?)EdgeView(e)).ToList();

    private static async Task<object?> CallAsync(BulkUtilsRun run, CancellationToken cancellationToken)
    {
        var input = run.Input;
        var driver = run.Driver;
        var embedder = run.Embedder;
        switch ((string)input["fn"]!)
        {
            case "_entity_node_record":
                {
                    var node = Node(input["node"]);
                    var result = await BulkUtils.EntityNodeRecordAsync(node, embedder, driver, cancellationToken);
                    return new OrderedDictionary<string, object?> { ["record"] = Encode(result), ["node"] = NodeViewWithEmbedding(node) };
                }

            case "_entity_edge_record":
                {
                    var edge = Edge(input["edge"]);
                    var result = await BulkUtils.EntityEdgeRecordAsync(edge, embedder, driver, cancellationToken);
                    return new OrderedDictionary<string, object?> { ["record"] = Encode(result), ["edge"] = EdgeView(edge) };
                }

            case "update_entity_edges_tx":
                {
                    List<EntityEdge> edges = [.. List(input["edges"]).Select(Edge)];
                    await BulkUtils.UpdateEntityEdgesTxAsync(new BulkUtilsRun.FakeTx(run), edges, embedder, driver, cancellationToken);
                    return new OrderedDictionary<string, object?> { ["edges"] = EdgeViews(edges) };
                }

            case "update_entity_nodes_tx":
                {
                    var nodes = BulkUtilsRun.NodesOf(input["nodes"]);
                    await BulkUtils.UpdateEntityNodesTxAsync(new BulkUtilsRun.FakeTx(run), nodes, embedder, driver, cancellationToken);
                    return new OrderedDictionary<string, object?> { ["nodes"] = NodeViews(nodes) };
                }

            case "add_nodes_and_edges_bulk_tx":
            case "add_nodes_and_edges_bulk":
                {
                    List<EpisodicNode> episodes = [.. List(input["episodes"]).Select(Episode)];
                    List<EpisodicEdge> episodicEdges = [.. List(input["episodic_edges"]).Select(e => EpisodicEdge.ModelValidate(DecodeDict(Dict(e))))];
                    var nodes = BulkUtilsRun.NodesOf(input["nodes"]);
                    List<EntityEdge> edges = [.. List(input["edges"]).Select(Edge)];
                    if ((string)input["fn"]! == "add_nodes_and_edges_bulk_tx")
                    {
                        await BulkUtils.AddNodesAndEdgesBulkTxAsync(new BulkUtilsRun.FakeTx(run), episodes, episodicEdges, nodes, edges, embedder, driver, cancellationToken);
                    }
                    else if (input.GetValueOrDefault("defaults") is true)
                    {
                        await BulkUtils.AddNodesAndEdgesBulkAsync(driver, episodes, episodicEdges, nodes, edges, embedder, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        double? writeTimeout = input["write_timeout"] is null ? null : Number(input["write_timeout"]);
                        await BulkUtils.AddNodesAndEdgesBulkAsync(driver, episodes, episodicEdges, nodes, edges, embedder, writeTimeout: writeTimeout, cancellationToken: cancellationToken);
                    }

                    return new OrderedDictionary<string, object?> { ["nodes"] = NodeViews(nodes), ["edges"] = EdgeViews(edges) };
                }

            case "execute_write_with_timeout":
                {
                    // PORT: the work function closes over its argument, and the call returns no value (R9).
                    var value = (string?)input["value"];
                    var raises = (bool)input["raises"]!;
                    await BulkUtils.ExecuteWriteWithTimeoutAsync(
                        driver,
                        Number(input["timeout"]),
                        async (tx, token) =>
                        {
                            await tx.RunAsync("RETURN $value", new() { ["value"] = value }, token);
                            if (raises)
                            {
                                throw new InvalidOperationException("work failed");
                            }
                        },
                        cancellationToken);
                    return null;
                }

            case "retrieve_previous_episodes_bulk":
                {
                    List<EpisodicNode> episodes = [.. List(input["episodes"]).Select(Episode)];
                    var result = input.GetValueOrDefault("defaults") is true
                        ? await BulkUtils.RetrievePreviousEpisodesBulkAsync(driver, episodes, cancellationToken: cancellationToken)
                        : await BulkUtils.RetrievePreviousEpisodesBulkAsync(driver, episodes, maxCoroutines: (long?)input["max_coroutines"], cancellationToken: cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["tuples"] = result.Select(t => (object?)new List<object?> { t.Item1.Uuid, t.Item2.Select(p => (object?)p.Uuid).ToList() }).ToList(),
                    };
                }

            case "extract_nodes_and_edges_bulk":
            case "extract_nodes_and_edges_bulk_combined":
            case "extract_nodes_and_edges_bulk_separate":
                {
                    (List<List<EntityNode>> nodesBulk, List<List<EntityEdge>> edgesBulk) = (input["fn"], input.GetValueOrDefault("defaults") is true) switch
                    {
                        ("extract_nodes_and_edges_bulk", true) => await BulkUtils.ExtractNodesAndEdgesBulkAsync(
                            run.Clients, TuplesOf(input["episode_tuples"]), TypeMap(input["type_map"]), cancellationToken: cancellationToken),
                        ("extract_nodes_and_edges_bulk_combined", true) => await BulkUtils.ExtractNodesAndEdgesBulkCombinedAsync(
                            run.Clients, TuplesOf(input["episode_tuples"]), TypeMap(input["type_map"]), cancellationToken: cancellationToken),
                        ("extract_nodes_and_edges_bulk_separate", true) => await BulkUtils.ExtractNodesAndEdgesBulkSeparateAsync(
                            run.Clients, TuplesOf(input["episode_tuples"]), TypeMap(input["type_map"]), cancellationToken: cancellationToken),
                        _ => await BulkUtils.ExtractNodesAndEdgesBulkAsync(
                            run.Clients,
                            TuplesOf(input["episode_tuples"]),
                            TypeMap(input["type_map"]),
                            entityTypes: TypesOf(input["entity_types"]),
                            excludedEntityTypes: input["excluded"] is List<object?> excluded ? [.. excluded.Select(x => (string)x!)] : null,
                            edgeTypes: TypesOf(input["edge_types"]),
                            customExtractionInstructions: (string?)input["instructions"],
                            useCombinedExtraction: (bool)input["combined"]!,
                            strictEdgeTypes: (bool)input["strict"]!,
                            cancellationToken: cancellationToken),
                    };
                    return new OrderedDictionary<string, object?>
                    {
                        ["nodes"] = nodesBulk.Select(n => (object?)NodeViews(n)).ToList(),
                        ["edges"] = edgesBulk.Select(e => (object?)EdgeViews(e)).ToList(),
                    };
                }

            case "dedupe_nodes_bulk":
                {
                    List<List<EntityNode>> extracted = [.. List(input["nodes"]).Select(BulkUtilsRun.NodesOf)];
                    List<(EpisodicNode, List<EpisodicNode>)> episodeTuples = [.. List(input["episodes"]).Select(e => (Episode(e), new List<EpisodicNode>()))];
                    OrderedDictionary<string, List<string>>? identityProperties = null;
                    if (input["identity_properties"] is OrderedDictionary<string, object?> properties)
                    {
                        identityProperties = new();
                        foreach (var (key, value) in properties)
                        {
                            identityProperties[key] = [.. List(value).Select(x => (string)x!)];
                        }
                    }

                    var (byEpisode, uuidMap) = input.GetValueOrDefault("defaults") is true
                        ? await BulkUtils.DedupeNodesBulkAsync(run.Clients, extracted, episodeTuples, cancellationToken: cancellationToken)
                        : await BulkUtils.DedupeNodesBulkAsync(
                            run.Clients,
                            extracted,
                            episodeTuples,
                            entityTypes: TypesOf(input["entity_types"]),
                            identityProperties: identityProperties,
                            trace: null,
                            cancellationToken: cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["by_episode"] = byEpisode.Select(e => (object?)new List<object?> { e.Key, NodeViews(e.Value) }).ToList(),
                        ["uuid_map"] = uuidMap.Select(e => (object?)new List<object?> { e.Key, e.Value }).ToList(),
                        ["extracted"] = extracted.Select(n => (object?)NodeViews(n)).ToList(),
                    };
                }

            case "dedupe_edges_bulk":
                {
                    List<List<EntityEdge>> extracted = [.. List(input["edges"]).Select(items => List(items).Select(Edge).ToList())];
                    List<(EpisodicNode, List<EpisodicNode>)> episodeTuples = [.. List(input["episodes"]).Select(e => (Episode(e), new List<EpisodicNode>()))];
                    var byEpisode = await BulkUtils.DedupeEdgesBulkAsync(
                        run.Clients,
                        extracted,
                        episodeTuples,
                        [],
                        TypesOf(input["types"]) ?? [],
                        TypeMap(input["type_map"]),
                        cancellationToken);
                    return new OrderedDictionary<string, object?>
                    {
                        ["by_episode"] = byEpisode.Select(e => (object?)new List<object?> { e.Key, EdgeViews(e.Value) }).ToList(),
                        ["extracted"] = extracted.Select(e => (object?)EdgeViews(e)).ToList(),
                    };
                }

            default:
                throw new InvalidOperationException((string)input["fn"]!);
        }
    }

    private sealed record NoOverride : GraphOperationsInterface;

    private sealed record TwoOverrides : GraphOperationsInterface
    {
        internal override Task NodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task EdgeUpdateBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record ThreeOverrides : GraphOperationsInterface
    {
        internal override Task EpisodicNodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task EdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task EpisodicEdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> episodicEdges, long batchSize = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record AllOverrides : GraphOperationsInterface
    {
        internal override Task EpisodicNodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task NodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task EpisodicEdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> episodicEdges, long batchSize = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task EdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task NodeUpdateBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task EdgeUpdateBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
