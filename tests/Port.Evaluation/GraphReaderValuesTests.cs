// Evaluation test for graphiti_core/ingestion/graph_reader.py (port/RULES.md, R23 and R25).
// values/graph_reader.json holds the calls that port/tools/record_graph_reader.py recorded from Python: each method of
// GraphReader with each argument at its default and with a value, with fakes at the network boundary on both sides (a
// driver with a graph operations interface and a search interface, and an embedder). Each fake yields once and then
// records its call. The recorder docstring gives the answers of the fakes.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.Ingestion;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.MaintenanceHelpersValuesTests;

public class GraphReaderValuesTests
{
    private static readonly List<OrderedDictionary<string, object?>> Cases =
    [
        .. List(((OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "values", "graph_reader.json")))!)["cases"]).Select(Dict),
    ];

    public static TheoryData<int> CaseIndexes() => [.. Enumerable.Range(0, Cases.Count)];

    // The cases that make at least one call that takes the token, for the cancellation tests.
    public static TheoryData<int> CallingCases() =>
        [.. Enumerable.Range(0, Cases.Count).Where(i => List(Dict(Cases[i]["out"])["events"]).Count > 0 && !Dict(Cases[i]["out"]).ContainsKey("error"))];

    public static TheoryData<int, int> LaterCalls()
    {
        var data = new TheoryData<int, int>();
        foreach (var index in Enumerable.Range(0, Cases.Count).Where(i => !Dict(Cases[i]["out"]).ContainsKey("error")))
        {
            var calls = List(Dict(Cases[index]["out"])["events"]).Count;
            for (var k = 0; k < calls - 1; k++)
            {
                data.Add(index, k);
            }
        }

        return data;
    }

    [Fact]
    public void The_recording_reaches_each_method_and_branch()
    {
        foreach (var fn in new[] { "node_candidates", "edge_duplicate_candidates", "edge_invalidation_candidates", "nodes_by_uuids", "episodes_by_uuids", "edges_by_uuids" })
        {
            Assert.Contains(Cases, c => Str(Dict(c["in"])["fn"]) == fn && List(Dict(c["out"])["events"]).Count == 0);
            Assert.Contains(Cases, c => Str(Dict(c["in"])["fn"]) == fn && List(Dict(c["out"])["events"]).Count > 0);
        }

        // identity_properties at its default and with a value, each caught lookup error, and a limit below the count.
        Assert.Contains(Cases, c => Dict(c["in"]).ContainsKey("identity_properties"));
        foreach (var mode in new[] { "node_not_found", "edge_not_found", "edges_not_found", "runtime" })
        {
            Assert.Contains(Cases, c => (string?)Dict(c["in"])["mode"] == mode);
        }

        Assert.Contains(Cases, c => Dict(c["in"])["max_coroutines"] is 1L && (long)Dict(c["out"])["max_in_flight"]! == 1 && List(Dict(c["out"])["events"]).Count > 2);
        Assert.Contains(Cases, c => Dict(c["in"])["max_coroutines"] is 2L && (long)Dict(c["out"])["max_in_flight"]! == 4);
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(CaseIndexes))]
    public async Task The_port_makes_each_recorded_call_and_gives_each_result(int index)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new Run(Dict(Cases[index]["in"]), source, cancelAt: -1);
        var output = new OrderedDictionary<string, object?>();
        try
        {
            output["ok"] = await LoopGuard.RunAsync(() => CallAsync(run, source.Token));
        }
        catch (Exception error) when (error is InvalidOperationException or NodeNotFoundException)
        {
            output["error"] = new OrderedDictionary<string, object?>
            {
                ["type"] = error is NodeNotFoundException ? "NodeNotFoundError" : "RuntimeError",
                ["text"] = error.Message,
            };
        }

        output["events"] = run.Events;
        output["max_in_flight"] = (long)run.MaxInFlight;
        var expected = Dict(Cases[index]["out"]);
        Assert.Equal(PyJson.Dumps(expected["events"], indent: 1), PyJson.Dumps(run.Events, indent: 1));
        Assert.Equal(PyJson.Dumps(expected, indent: 1), PyJson.Dumps(output, indent: 1));

        // Each fake got the token of the call (R25).
        Assert.All(run.Tokens, token => Assert.Equal(source.Token, token));
    }

    // A test with no Python original: each async method with a canceled token throws OperationCanceledException (R8)
    // and makes no fake call.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(CallingCases))]
    public async Task Each_async_method_with_a_canceled_token_throws_OperationCanceledException(int index)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var run = new Run(Dict(Cases[index]["in"]), source, cancelAt: -1);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));
        await LoopGuard.RunAsync(Turns);
        Assert.Equal(0, run.Count);
    }

    // A test with no Python original: the token is canceled at fake call k. Call k is the last call that a fake
    // records, and the method throws OperationCanceledException (R8).
    [Theory(Timeout = 5000)]
    [MemberData(nameof(LaterCalls))]
    public async Task Each_later_awaited_call_gets_the_token_and_no_call_follows_a_cancel(int index, int k)
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new Run(Dict(Cases[index]["in"]), source, cancelAt: k);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));

        // The calls that a failed gather leaves run on. Give them turns before the count (R25).
        await LoopGuard.RunAsync(Turns);
        Assert.Equal(k + 1, run.Count);
    }

    private static async Task Turns()
    {
        for (var i = 0; i < 50; i++)
        {
            await Task.Yield();
        }
    }

    private static async Task<object?> CallAsync(Run run, CancellationToken cancellationToken)
    {
        var input = run.Input;
        var reader = new GraphReader(run.Clients);
        switch (Str(input["fn"]))
        {
            case "node_candidates":
                {
                    List<EntityNode> nodes = [.. List(input["nodes"]).Select(Node)];
                    List<List<EntityNode>> result;
                    if (input.TryGetValue("identity_properties", out var value))
                    {
                        OrderedDictionary<string, List<string>>? identity = null;
                        if (value is not null)
                        {
                            identity = [];
                            foreach (var (label, names) in Dict(value))
                            {
                                identity[label] = [.. List(names).Select(Str)];
                            }
                        }

                        result = await reader.NodeCandidatesAsync(nodes, identity, cancellationToken);
                    }
                    else
                    {
                        result = await reader.NodeCandidatesAsync(nodes, cancellationToken: cancellationToken);
                    }

                    return result.Select(group => (object?)group.Select(n => (object?)NodeView(n)).ToList()).ToList();
                }

            case "edge_duplicate_candidates":
            case "edge_invalidation_candidates":
                {
                    List<EntityEdge> edges = [.. List(input["edges"]).Select(Edge)];
                    var result = Str(input["fn"]) == "edge_duplicate_candidates"
                        ? await reader.EdgeDuplicateCandidatesAsync(edges, cancellationToken)
                        : await reader.EdgeInvalidationCandidatesAsync(edges, cancellationToken);
                    return result.Select(group => (object?)group.Select(e => (object?)EdgeView(e)).ToList()).ToList();
                }

            case "nodes_by_uuids":
                return (await reader.NodesByUuidsAsync([.. List(input["uuids"]).Select(Str)], Str(input["group_id"]), cancellationToken))
                    .Select(n => (object?)NodeView(n)).ToList();
            case "episodes_by_uuids":
                return (await reader.EpisodesByUuidsAsync([.. List(input["uuids"]).Select(Str)], cancellationToken))
                    .Select(e => (object?)new List<object?> { e.Uuid, e.GroupId }).ToList();
            case "edges_by_uuids":
                return (await reader.EdgesByUuidsAsync([.. List(input["uuids"]).Select(Str)], cancellationToken))
                    .Select(e => (object?)EdgeView(e)).ToList();
            default:
                throw new ArgumentException(Str(input["fn"]));
        }
    }

    private static OrderedDictionary<string, object?> NodeView(EntityNode node) => new()
    {
        ["uuid"] = node.Uuid,
        ["name"] = node.Name,
        ["group_id"] = node.GroupId,
        ["labels"] = node.Labels,
        ["name_embedding"] = node.NameEmbedding,
        ["attributes"] = node.Attributes,
    };

    private static OrderedDictionary<string, object?> EdgeView(EntityEdge edge) => new()
    {
        ["uuid"] = edge.Uuid,
        ["group_id"] = edge.GroupId,
        ["source_node_uuid"] = edge.SourceNodeUuid,
        ["target_node_uuid"] = edge.TargetNodeUuid,
        ["fact"] = edge.Fact,
        ["fact_embedding"] = edge.FactEmbedding,
    };

    private static EntityNode Node(object? data) => EntityNode.ModelValidate(Dict(Py.DeepCopy(data)));

    private static EntityEdge Edge(object? data) => EntityEdge.ModelValidate(Dict(Py.DeepCopy(data)));

    private static EpisodicNode Episode(object? data) => EpisodicNode.ModelValidate(Dict(Py.DeepCopy(data)));

    private static List<object?>? Strings(List<string>? values) => values?.Select(v => (object?)v).ToList();

    // One run of one case: the clients, the event list, and the counts of the calls.
    private sealed class Run
    {
        internal Run(OrderedDictionary<string, object?> input, CancellationTokenSource source, int cancelAt)
        {
            Input = input;
            Source = source;
            CancelAt = cancelAt;
            Clients = new GraphitiClients
            {
                Driver = new FakeDriver(this),
                LlmClient = new UnusedLlm(),
                Embedder = new FakeEmbedder(this),
                CrossEncoder = new UnusedCrossEncoder(),
                Tracer = new NoOpTracer(),
                MaxCoroutines = (long?)input["max_coroutines"],
            };
        }

        internal OrderedDictionary<string, object?> Input { get; }

        internal CancellationTokenSource Source { get; }

        internal int CancelAt { get; }

        internal GraphitiClients Clients { get; }

        internal List<object?> Events { get; } = [];

        internal List<CancellationToken> Tokens { get; } = [];

        internal List<string> Texts { get; } = [];

        internal int Count { get; private set; }

        internal int MaxInFlight { get; private set; }

        private int InFlight { get; set; }

        // The common part of each fake call: a token check, a yield, a second token check, and the record (R25).
        internal async Task EnterAsync(string kind, OrderedDictionary<string, object?> record, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InFlight++;
            MaxInFlight = Math.Max(MaxInFlight, InFlight);
            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                var item = new OrderedDictionary<string, object?> { ["kind"] = kind };
                foreach (var (key, value) in record)
                {
                    item[key] = value;
                }

                Events.Add(item);
                Tokens.Add(cancellationToken);
                if (Count++ == CancelAt)
                {
                    await Source.CancelAsync();
                }
            }
            finally
            {
                InFlight--;
            }
        }

        internal void Fail(string kind)
        {
            switch ((string?)Input["mode"])
            {
                case "node_not_found" when kind == "node":
                    throw new NodeNotFoundException("s-ana");
                case "edge_not_found" when kind == "edge":
                    throw new EdgeNotFoundException("s-work");
                case "edges_not_found" when kind == "edge":
                    throw new EdgesNotFoundException(["s-work", "s-x"]);
                case "runtime":
                    throw new InvalidOperationException("lookup down");
            }
        }

        internal string TextOf(List<double> vector)
        {
            var key = (int)vector[0];
            return key >= 1000 ? Texts[key - 1000] : $"vec{key}";
        }

        internal List<EntityEdge> EdgeAnswer(string fact, SearchFilters searchFilter, string kind)
        {
            var scope = searchFilter.EdgeSourceNodeUuids is not null ? "pair" : "all";
            if (Dict(Input["search"]).GetValueOrDefault(fact) is not OrderedDictionary<string, object?> byScope
                || byScope.GetValueOrDefault(scope) is not OrderedDictionary<string, object?> byKind
                || byKind.GetValueOrDefault(kind) is not List<object?> table)
            {
                return [];
            }

            return [.. table.Select(Edge)];
        }
    }

    private sealed record FakeOps(Run Run) : GraphOperationsInterface
    {
        internal override async Task<List<EntityNode>> NodeGetByNamesAsync(Type? cls, GraphDriver driver, List<string> names, List<string> groupIds, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync("node_get_by_names", new() { ["names"] = Strings(names), ["group_ids"] = Strings(groupIds) }, cancellationToken);
            return [.. List(Run.Input["exact"]).Where(n => names.Contains(Str(Dict(n)["name"]))).Select(Node)];
        }

        internal override async Task<List<EntityNode>> NodeGetByAttributeValuesAsync(
            Type? cls,
            GraphDriver driver,
            string groupId,
            OrderedDictionary<string, object?> attributes,
            List<string>? nodeLabels = null,
            long limit = 100,
            CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(
                "node_get_by_attribute_values",
                new() { ["group_id"] = groupId, ["attributes"] = new OrderedDictionary<string, object?>(attributes), ["labels"] = Strings(nodeLabels), ["limit"] = limit },
                cancellationToken);
            return [.. List(Dict(Run.Input["identity"]).GetValueOrDefault(nodeLabels?[0] ?? string.Empty) ?? new List<object?>()).Select(Node)];
        }

        internal override async Task<List<EntityNode>> NodeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, string? groupId = null, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync("node_get_by_uuids", new() { ["uuids"] = Strings(uuids), ["group_id"] = groupId }, cancellationToken);
            Run.Fail("node");
            return [.. List(Run.Input["stored_nodes"]).Select(Dict)
                .Where(n => uuids.Contains(Str(n["uuid"])) && (groupId is null || Str(n["group_id"]) == groupId))
                .Select(n => Node(n))];
        }

        internal override async Task<List<EpisodicNode>> EpisodicNodeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync("episodic_node_get_by_uuids", new() { ["uuids"] = Strings(uuids) }, cancellationToken);
            Run.Fail("node");
            return [.. List(Run.Input["stored_episodes"]).Where(e => uuids.Contains(Str(Dict(e)["uuid"]))).Select(Episode)];
        }

        internal override async Task<List<EntityEdge>> EdgeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync("edge_get_by_uuids", new() { ["uuids"] = Strings(uuids) }, cancellationToken);
            Run.Fail("edge");
            return [.. List(Run.Input["stored_edges"]).Where(e => uuids.Contains(Str(Dict(e)["uuid"]))).Select(Edge)];
        }
    }

    private sealed record FakeSearch(Run Run) : SearchInterface
    {
        internal override async Task<List<EntityNode>> NodeSimilaritySearchAsync(
            GraphDriver driver,
            List<double> searchVector,
            SearchFilters searchFilter,
            List<string>? groupIds = null,
            long limit = 100,
            double minScore = 0.7,
            CancellationToken cancellationToken = default)
        {
            var text = Run.TextOf(searchVector);
            await Run.EnterAsync(
                "node_similarity_search",
                new() { ["query"] = text, ["group_ids"] = Strings(groupIds), ["limit"] = limit, ["min_score"] = minScore },
                cancellationToken);
            return [.. List(Dict(Run.Input["search"]).GetValueOrDefault(text) ?? new List<object?>()).Select(Node)];
        }

        internal override async Task<List<EntityEdge>> EdgeFulltextSearchAsync(GraphDriver driver, string query, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(
                "edge_fulltext_search",
                new()
                {
                    ["query"] = query,
                    ["group_ids"] = Strings(groupIds),
                    ["limit"] = limit,
                    ["source"] = Strings(searchFilter.EdgeSourceNodeUuids),
                    ["target"] = Strings(searchFilter.EdgeTargetNodeUuids),
                },
                cancellationToken);
            return Run.EdgeAnswer(query, searchFilter, "bm25");
        }

        internal override async Task<List<EntityEdge>> EdgeSimilaritySearchAsync(GraphDriver driver, List<double> searchVector, string? sourceNodeUuid, string? targetNodeUuid, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, double minScore = 0.7, CancellationToken cancellationToken = default)
        {
            var text = Run.TextOf(searchVector);
            await Run.EnterAsync(
                "edge_similarity_search",
                new()
                {
                    ["query"] = text,
                    ["source_node_uuid"] = sourceNodeUuid,
                    ["target_node_uuid"] = targetNodeUuid,
                    ["group_ids"] = Strings(groupIds),
                    ["limit"] = limit,
                    ["min_score"] = minScore,
                    ["source"] = Strings(searchFilter.EdgeSourceNodeUuids),
                    ["target"] = Strings(searchFilter.EdgeTargetNodeUuids),
                },
                cancellationToken);
            var fact = List(Run.Input.GetValueOrDefault("edges") ?? new List<object?>()).Select(Dict)
                .Where(e => e.GetValueOrDefault("fact_embedding") is List<object?> v && v.Select(x => (double)x!).SequenceEqual(searchVector))
                .Select(e => Str(e["fact"]))
                .FirstOrDefault() ?? text;
            return Run.EdgeAnswer(fact, searchFilter, "cos");
        }
    }

    private sealed class FakeEmbedder(Run run) : EmbedderClient
    {
        internal override async Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("embed_create", new() { ["input"] = Strings(inputData) }, cancellationToken);
            return Vector(inputData[0]);
        }

        internal override async Task<List<List<double>>> CreateBatchAsync(List<string> inputDataList, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("embed_batch", new() { ["input"] = Strings(inputDataList) }, cancellationToken);
            return [.. inputDataList.Select(Vector)];
        }

        private List<double> Vector(string text)
        {
            if (!run.Texts.Contains(text))
            {
                run.Texts.Add(text);
            }

            return [1000 + run.Texts.IndexOf(text), 0.5];
        }
    }

    private sealed class FakeDriver : GraphDriver
    {
        internal FakeDriver(Run run)
        {
            Provider = GraphProvider.Neo4j;
            GraphOperationsInterface = new FakeOps(run);
            SearchInterface = new FakeSearch(run);
        }

        internal override string PythonName => "FakeDriver";

        internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("execute_query is not used");

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class UnusedLlm() : LLMClient(new LLMConfig(), false)
    {
        internal override string PythonName => "Unused";

        internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
            List<Message> messages,
            IResponseModel? responseModel = null,
            long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
            ModelSize modelSize = ModelSize.Medium,
            string? model = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");
    }

    private sealed class UnusedCrossEncoder : CrossEncoderClient
    {
        internal override Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }
}
