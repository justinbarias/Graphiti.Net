// Evaluation test for the resolution functions of layer 7 with a ResolutionTrace (port/RULES.md, R23 and R25).
// values/resolution_trace.json holds the calls that port/tools/record_resolution_trace.py recorded from Python:
// resolve_extracted_nodes and resolve_nodes_with_candidates with the fakes of the node_operations recording,
// resolve_extracted_edges with the fakes of the edge_operations recording, and dedupe_nodes_bulk with the fakes of the
// bulk_utils recording. Each case holds the output of the call with a trace, in the form of its recorder, and the full
// content of the trace. The test runs each case with a trace and with no trace argument: both outputs equal the
// recorded output, and the trace equals the recorded trace.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.EdgeOperationsValues;

public class ResolutionTraceValuesTests
{
    internal static readonly List<OrderedDictionary<string, object?>> Cases =
    [
        .. List(((OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "values", "resolution_trace.json")))!)["cases"]).Select(Dict),
    ];

    private static readonly PyDateTime TraceNow = PyDateTime.FromIsoFormat("2026-03-04T05:06:07.891011+00:00");

    public static TheoryData<int, bool> EdgeAndBulkCases()
    {
        var data = new TheoryData<int, bool>();
        foreach (var (index, item) in Cases.Index())
        {
            if ((string)item["family"]! != "node")
            {
                data.Add(index, true);
                data.Add(index, false);
            }
        }

        return data;
    }

    [Fact]
    public void The_recording_reaches_each_function_and_each_kind_of_resolution()
    {
        var functions = Cases.Select(c => (string)Dict(c["in"])["fn"]!).ToHashSet();
        Assert.Equal(["resolve_extracted_nodes", "resolve_nodes_with_candidates", "resolve_extracted_edges", "dedupe_nodes_bulk"], functions);
        var operations = Cases.SelectMany(c => List(Dict(Dict(c["out"])["trace"])["operations"])).Select(o => (string)Dict(o)["op"]!).ToHashSet();
        Assert.Equal(["patch_node", "add_episodes_to_edge", "set_edge_attributes", "end_edge"], operations);

        // A node by the exact name, a fuzzy name, an identity property, the answer of the model, and no match.
        var nodes = Cases.Where(c => (string)c["family"]! == "node").Select(c => PyJson.Dumps(List(Dict(c["out"])["result"])[1])).ToList();
        Assert.Contains(nodes, n => n.Contains("\"x-exact\": \"s-alice\"", StringComparison.Ordinal)
            && n.Contains("\"x-fuzzy\": \"s-joe\"", StringComparison.Ordinal) && n.Contains("\"x-id\": \"s-smith\"", StringComparison.Ordinal)
            && n.Contains("\"x-llm\": \"s-bob\"", StringComparison.Ordinal) && n.Contains("\"x-new\": \"x-new\"", StringComparison.Ordinal));

        // An exact duplicate, a duplicate by the model, a contradiction that ends an old edge, a fact that a later edge
        // ends, and a new edge.
        var edge = Cases.First(c => (string)c["family"]! == "edge");
        var trace = PyJson.Dumps(Dict(Dict(edge["out"])["trace"]));
        Assert.Contains("\"edge_uuid\": \"s-work\"", trace, StringComparison.Ordinal);
        Assert.Contains("\"edge_uuid\": \"s-live\"", trace, StringComparison.Ordinal);
        Assert.Contains("\"op\": \"end_edge\", \"edge_uuid\": \"s-bobwork\"", trace, StringComparison.Ordinal);
        Assert.Contains("\"ended_by_candidate\": [\"x-old\"]", trace, StringComparison.Ordinal);
        Assert.Contains("x-new", PyJson.Dumps(Dict(Dict(edge["out"])["ok"])["new"]), StringComparison.Ordinal);
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(EdgeAndBulkCases))]
    public async Task The_port_gives_each_recorded_output_and_trace_with_and_without_a_trace(int index, bool traced)
    {
        var item = Cases[index];
        var input = Dict(item["in"]);
        var expected = Dict(item["out"]);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var trace = traced ? new ResolutionTrace() : null;
        OrderedDictionary<string, object?> output;
        if ((string)item["family"]! == "edge")
        {
            var run = new EdgeOperationsRun(input, source, cancelAt: -1);
            output = new() { ["ok"] = await RunAsync(run.Events, () => ResolveEdgesAsync(run, trace, source.Token)) };
            output["events"] = run.Events;
            output["max_in_flight"] = (long)run.MaxInFlight;
            Assert.All(run.Tokens, token => Assert.Equal(source.Token, token));
        }
        else
        {
            var run = new BulkUtilsRun(input, source, cancelAt: -1);
            output = new() { ["ok"] = await RunAsync(run.Events, () => DedupeNodesBulkAsync(run, trace, source.Token)) };
            output["events"] = run.Events;
            output["max_in_flight"] = (long)run.MaxInFlight;
            output["count"] = (long)run.Count;
            Assert.All(run.Tokens, token => Assert.Equal(source.Token, token.Token));
        }

        output["trace"] = TraceView(trace);
        var actual = Dict(Placeholders(output, []));
        var want = new OrderedDictionary<string, object?>(expected);
        if (!traced)
        {
            want["trace"] = null;
        }

        Assert.Equal(PyJson.Dumps(want, indent: 1), PyJson.Dumps(actual, indent: 1));
    }

    // The content of a trace in the form of trace_view() of the recorder, as open data.
    internal static OrderedDictionary<string, object?>? TraceView(ResolutionTrace? trace) => trace is null
        ? null
        : new()
        {
            ["seen_node_candidates"] = Open(trace.SeenNodeCandidates),
            ["seen_node_labels"] = Open(trace.SeenNodeLabels),
            ["seen_duplicate_candidates"] = Open(trace.SeenDuplicateCandidates),
            ["seen_invalidation_candidates"] = Open(trace.SeenInvalidationCandidates),
            ["operations"] = trace.Operations.Select(o => (object?)((IPydanticModel)o).ModelDump(true)).ToList(),
            ["stored_node_uuids"] = trace.StoredNodeUuids.Order(Py.StringComparer).Cast<object?>().ToList(),
            ["ended_by_candidate"] = trace.EndedByCandidate.Order(Py.StringComparer).Cast<object?>().ToList(),
            ["node_llm_calls"] = trace.NodeLlmCalls,
        };

    private static OrderedDictionary<string, object?> Open(OrderedDictionary<string, List<string>> map)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, list) in map)
        {
            result[key] = list.Cast<object?>().ToList();
        }

        return result;
    }

    private static async Task<object?> RunAsync(List<object?> events, Func<Task<object?>> call)
    {
        using (PortServices.Use(loggerFactory: new EdgeOperationsLogCapture(events), time: new MaintenanceFixedClock(TraceNow.ToDateTimeOffset())))
        {
            return await LoopGuard.RunAsync(call);
        }
    }

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

    // The branch of resolve_extracted_edges of EdgeOperationsValuesTests, with the trace.
    private static async Task<object?> ResolveEdgesAsync(EdgeOperationsRun run, ResolutionTrace? trace, CancellationToken cancellationToken)
    {
        var input = run.Input;
        List<EntityEdge> extracted = [.. List(input["edges"]).Select(Edge)];
        var invalidatedBy = (bool)input["invalidated_by"]! ? new OrderedDictionary<string, List<string>>() : null;
        var overrideEdges = input["override"] is List<object?> o ? o.Select(Edge).ToList() : null;
        var types = new OrderedDictionary<string, GraphTypeDescriptor>();
        foreach (var name in List(input["types"]).Select(n => (string)n!))
        {
            types[name] = Models[name];
        }

        var (resolved, invalidated, @new) = trace is null
            ? await EdgeOperations.ResolveExtractedEdgesAsync(
                run.Clients,
                extracted,
                Episode(input["episode"]),
                [.. List(input["entities"]).Select(Node)],
                types,
                TypeMap(input["type_map"]),
                existingEdgesOverride: overrideEdges,
                strictEdgeTypes: (bool)input["strict"]!,
                invalidatedBy: invalidatedBy,
                cancellationToken: cancellationToken)
            : await EdgeOperations.ResolveExtractedEdgesAsync(
                run.Clients,
                extracted,
                Episode(input["episode"]),
                [.. List(input["entities"]).Select(Node)],
                types,
                TypeMap(input["type_map"]),
                existingEdgesOverride: overrideEdges,
                strictEdgeTypes: (bool)input["strict"]!,
                invalidatedBy: invalidatedBy,
                trace: trace,
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

    // The branch of dedupe_nodes_bulk of BulkUtilsValuesTests, with the trace.
    private static async Task<object?> DedupeNodesBulkAsync(BulkUtilsRun run, ResolutionTrace? trace, CancellationToken cancellationToken)
    {
        var input = run.Input;
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

        var (byEpisode, uuidMap) = trace is null
            ? await BulkUtils.DedupeNodesBulkAsync(run.Clients, extracted, episodeTuples, identityProperties: identityProperties, cancellationToken: cancellationToken)
            : await BulkUtils.DedupeNodesBulkAsync(run.Clients, extracted, episodeTuples, identityProperties: identityProperties, trace: trace, cancellationToken: cancellationToken);
        return new OrderedDictionary<string, object?>
        {
            ["by_episode"] = byEpisode.Select(e => (object?)new List<object?> { e.Key, e.Value.Select(n => (object?)BulkUtilsValues.NodeViewWithEmbedding(n)).ToList() }).ToList(),
            ["uuid_map"] = uuidMap.Select(e => (object?)new List<object?> { e.Key, e.Value }).ToList(),
            ["extracted"] = extracted.Select(n => (object?)n.Select(x => (object?)BulkUtilsValues.NodeViewWithEmbedding(x)).ToList()).ToList(),
        };
    }
}

// The node cases of values/resolution_trace.json, with the fakes of the node_operations recording.
public partial class NodeOperationsValuesTests
{
    public static TheoryData<int, bool> TraceNodeCases()
    {
        var data = new TheoryData<int, bool>();
        foreach (var (index, item) in ResolutionTraceValuesTests.Cases.Index())
        {
            if ((string)item["family"]! == "node")
            {
                data.Add(index, true);
                data.Add(index, false);
            }
        }

        return data;
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(TraceNodeCases))]
    public async Task The_port_gives_each_recorded_output_and_trace_with_and_without_a_trace(int index, bool traced)
    {
        var item = ResolutionTraceValuesTests.Cases[index];
        var input = (OrderedDictionary<string, object?>)Expand(Dict(item["in"]))!;
        var expected = new OrderedDictionary<string, object?>(Dict(item["out"]));
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new Run(input, source, cancelAt: -1);
        var trace = traced ? new ResolutionTrace() : null;

        object? result = null;
        object? error = null;
        using (PortServices.Use(loggerFactory: new EventCapture(run.Events), time: Clock))
        {
            try
            {
                result = await LoopGuard.RunAsync(() => ResolveWithTraceAsync(run, trace, source.Token));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                error = new OrderedDictionary<string, object?> { ["type"] = PythonName(e), ["text"] = e.Message };
            }
        }

        var maxInFlight = new OrderedDictionary<string, object?>();
        foreach (var (kind, count) in run.MaxInFlight.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            maxInFlight[kind] = count;
        }

        var output = Normalize(new OrderedDictionary<string, object?>
        {
            ["result"] = result,
            ["error"] = error,
            ["events"] = run.Events,
            ["nodes_after"] = run.Nodes.Select(n => (object?)NodeView(n)).ToList(),
            ["max_in_flight"] = maxInFlight,
            ["trace"] = ResolutionTraceValuesTests.TraceView(trace),
        });
        if (!traced)
        {
            expected["trace"] = null;
        }

        Assert.Equal(Comparable(expected), Comparable(output));
        Assert.All(run.Tokens, token => Assert.Equal(source.Token, token));
    }

    private static async Task<object?> ResolveWithTraceAsync(Run run, ResolutionTrace? trace, CancellationToken token)
    {
        var a = Dict(run.Input["args"]);
        var episode = a.TryGetValue("episode", out var e) ? ((EpisodeOrEpisodes.One?)EpisodeArg(e))?.Episode : null;
        var previous = a.TryGetValue("previous", out var p) && p is not null ? Episodes(p) : null;
        var identity = a.TryGetValue("identity_properties", out var ip) ? IdentityProperties(ip) : null;
        (List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>) resolution;
        if ((string)run.Input["fn"]! == "resolve_extracted_nodes")
        {
            resolution = trace is null
                ? await NodeOperations.ResolveExtractedNodesAsync(run.Clients, run.Nodes, episode, previous, null, null, identity, cancellationToken: token)
                : await NodeOperations.ResolveExtractedNodesAsync(run.Clients, run.Nodes, episode, previous, null, null, identity, trace, token);
        }
        else
        {
            List<List<EntityNode>> candidates = [.. List(a["candidates"]).Select(MakeNodes)];
            resolution = trace is null
                ? await NodeOperations.ResolveNodesWithCandidatesAsync(run.Clients, run.Nodes, candidates, episode, previous, null, identity, cancellationToken: token)
                : await NodeOperations.ResolveNodesWithCandidatesAsync(run.Clients, run.Nodes, candidates, episode, previous, null, identity, trace: trace, cancellationToken: token);
        }

        var (resolved, uuidMap, pairs) = resolution;
        var map = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in uuidMap)
        {
            map[key] = value;
        }

        return new List<object?>
        {
            resolved.Select(n => (object?)NodeView(n)).ToList(),
            map,
            pairs.Select(pair => (object?)new List<object?> { pair.Item1.Uuid, pair.Item2.Uuid }).ToList(),
        };
    }
}
