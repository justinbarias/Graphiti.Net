// Ported from tests/ingestion/test_resolution_trace.py (port/RULES.md R25).
// Tests for the resolution refactors: the new parameters keep the behavior of today
// at their defaults, and `ResolutionTrace` records what the resolution saw and changed.
//
// PORT: open item O10. The upstream tests replace module functions with monkeypatch.setattr. A test has a form when a
// fake at the network boundary gives the result of the patch and no assertion reads the arguments of the patched
// function. Such a test runs the real function:
// - `edge_ops.search` (two results in order: the duplicate search, then the graph-wide search) is the real search with
//   EdgeTestSearchDriver, which answers the search with the endpoint filter and the search with no filter.
// - `edge_ops.create_entity_edge_embeddings` (a mock that does nothing) is the real function with an embedder that
//   gives each fact the vector that the edges hold, [0.1, 0.2].
// - `edge_ops.semaphore_gather` (a gather that runs the calls one after the other) is the real gather.
// - `edge_ops._extract_edge_timestamps` (a mock that does nothing) is the real function. The resolution calls it only
//   for an edge with no duplicate, and the extracted edge has a duplicate, so the model gets one call.
// - `node_ops._collect_candidate_nodes` (the result [[stored, other], []]) is the real function with a driver whose name
//   lookup returns no node and whose similarity search returns [stored, other] for the first node and no node for the
//   second.
// The Python oracle ran each scenario with these fakes and no patch, and each assertion of the upstream test held.
//
// TODO(port): O10. test_semantic_search_reuses_name_embeddings_only_when_requested reads the arguments of the patched
// `node_ops.node_similarity_search` (`await_args_list[0].args[1]`), so it has no form. The recorded cases of
// collect_candidate_nodes with reuse_name_embeddings in values/node_operations.json cover the same behavior: each one
// records each embedder batch and the query text of each similarity search.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.NodeOperationsTestSupport;

public class TestResolutionTrace
{
    private static readonly PyDateTime T0 = PyDateTime.FromIsoFormat("2026-01-01T00:00:00+00:00");

    private static readonly PyDateTime T1 = PyDateTime.FromIsoFormat("2026-01-11T00:00:00+00:00");

    private static EntityEdge MakeEdge(string uuid, string source, string target, string fact, PyDateTime? validAt = null, List<string>? episodes = null) =>
        new()
        {
            Uuid = uuid,
            GroupId = "g",
            SourceNodeUuid = source,
            TargetNodeUuid = target,
            Name = "RELATES",
            Fact = fact,
            CreatedAt = T0,
            FactEmbedding = [0.1, 0.2],
            ValidAt = validAt,
            Episodes = episodes ?? [],
        };

    private static EpisodicNode MakeEpisode() => new EpisodicNode
    {
        Uuid = "ep1",
        Name = "episode",
        GroupId = "g",
        Source = EpisodeType.Text,
        SourceDescription = "test",
        Content = "content",
        ValidAt = T1,
        CreatedAt = T1,
    }.Validate();

    // One extracted edge. The model marks candidate 0 as a duplicate and candidate 1 as contradicted.
    private static async Task<(List<EntityEdge> Resolved, List<EntityEdge> Invalidated, List<EntityEdge> New, EdgeTestLlm Llm)> RunEdgeResolutionAsync(ResolutionTrace? trace, CancellationToken cancellationToken)
    {
        var duplicate = MakeEdge("dup", "a", "b", "Ana is employed by Acme", validAt: T1, episodes: ["ep0"]);
        var contradicted = MakeEdge("old", "a", "c", "Ana works at Bolt", validAt: T0, episodes: ["ep0"]);
        var extracted = MakeEdge("new", "a", "b", "Ana works at Acme", validAt: T1);

        // The first search is the duplicate search. The second search is graph-wide.
        var driver = new EdgeTestSearchDriver((_, _, filter) => filter.EdgeSourceNodeUuids is not null ? [duplicate] : [duplicate, contradicted]);
        var llmClient = new EdgeTestLlm(new()
        {
            ["duplicate_facts"] = new List<object?> { 0L },
            ["contradicted_facts"] = new List<object?> { 1L },
        });
        var clients = new GraphitiClients
        {
            Driver = driver,
            LlmClient = llmClient,
            Embedder = new EdgeTestEmbedder(_ => [0.1, 0.2]),
            CrossEncoder = new EdgeOperationsRun.FakeCrossEncoder(),
            Tracer = new NoOpTracer(),
        };
        List<string> uuids = ["a", "b", "c"];
        List<EntityNode> nodes = [.. uuids.Select(uuid => new EntityNode { Uuid = uuid, Name = uuid, GroupId = "g" }.Validate())];

        var (resolved, invalidated, @new) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgesAsync(
            clients,
            [extracted],
            MakeEpisode(),
            nodes,
            new(),
            new() { [("Entity", "Entity")] = [] },
            trace: trace,
            cancellationToken: cancellationToken));
        return (resolved, invalidated, @new, llmClient);
    }

    // `expired_at` is the time of the call, so the comparison uses only if it is set.
    private static string DumpEdges(List<EntityEdge> edges) => PyJson.Dumps(edges.Select(edge =>
    {
        var dump = edge.ModelDump(json: true);
        dump.Remove("expired_at");
        dump["expired"] = edge.ExpiredAt is not null;
        return (object?)dump;
    }).ToList());

    [Fact(Timeout = 5000)]
    public async Task test_edge_resolution_returns_same_values_with_and_without_trace()
    {
        var plain = await RunEdgeResolutionAsync(null, TestContext.Current.CancellationToken);
        var traced = await RunEdgeResolutionAsync(new ResolutionTrace(), TestContext.Current.CancellationToken);

        Assert.Equal(DumpEdges(plain.Resolved), DumpEdges(traced.Resolved));
        Assert.Equal(DumpEdges(plain.Invalidated), DumpEdges(traced.Invalidated));
        Assert.Equal(DumpEdges(plain.New), DumpEdges(traced.New));
        Assert.Single(plain.Llm.Calls);
        Assert.Single(traced.Llm.Calls);
        Assert.Equal(["dup"], plain.Resolved.Select(edge => edge.Uuid));
        Assert.Equal(["old"], plain.Invalidated.Select(edge => edge.Uuid));
        Assert.Empty(plain.New);
    }

    [Fact(Timeout = 5000)]
    public async Task test_edge_resolution_trace_records_candidates_and_operations()
    {
        var trace = new ResolutionTrace();
        var (_, invalidated, _, _) = await RunEdgeResolutionAsync(trace, TestContext.Current.CancellationToken);

        Assert.Equal("""{"new": ["dup"]}""", PyJson.Dumps(trace.SeenDuplicateCandidates));
        Assert.Equal("""{"new": ["old"]}""", PyJson.Dumps(trace.SeenInvalidationCandidates));
        List<Operation> expected =
        [
            new AddEpisodesToEdge { EdgeUuid = "dup", EpisodeUuids = ["ep1"] },
            new EndEdge { EdgeUuid = "old", InvalidAt = T1, ExpiredAt = invalidated[0].ExpiredAt ?? throw new InvalidOperationException("no expired_at"), OnlyIfOpen = false },
        ];
        Assert.Equal(Dump(expected), Dump(trace.Operations));
    }

    [Fact]
    public void test_serial_date_rule_ends_a_fact_at_the_earliest_later_contradiction()
    {
        // The date rule of serial ingestion, for a fact with two contradictions that start later.
        var fact = MakeEdge("new", "a", "b", "Ana works at Acme", validAt: T0);
        var late = MakeEdge("late", "a", "c", "Ana works at Bolt", validAt: PyDateTime.FromIsoFormat("2026-01-21T00:00:00+00:00"));
        var early = MakeEdge("early", "a", "d", "Ana works at Core", validAt: T1);

        EdgeOperations.ApplyTemporalInvalidation(fact, [late, early]);

        Assert.Equal(T1, fact.InvalidAt);
        Assert.NotNull(fact.ExpiredAt);
    }

    [Fact]
    public void test_find_exact_duplicate_edge_rules()
    {
        var seen = new Dictionary<(string, string, string), EntityEdge>();
        var first = MakeEdge("e1", "a", "b", "Ana works at Acme");
        Assert.Null(EdgeOperations.FindExactDuplicateEdge(first, seen));
        var same = MakeEdge("e2", "a", "b", " ana works at  ACME ");
        Assert.Same(first, EdgeOperations.FindExactDuplicateEdge(same, seen));
        var reverseSameName = MakeEdge("e3", "b", "a", "Ana works at Acme");
        Assert.Same(first, EdgeOperations.FindExactDuplicateEdge(reverseSameName, seen));
        var reverseOtherName = MakeEdge("e4", "b", "a", "Ana works at Acme");
        reverseOtherName.Name = "EMPLOYS";
        Assert.Null(EdgeOperations.FindExactDuplicateEdge(reverseOtherName, seen));
        var otherFact = MakeEdge("e5", "a", "b", "Ana left Acme");
        Assert.Null(EdgeOperations.FindExactDuplicateEdge(otherFact, seen));
    }

    private static async Task<(List<EntityNode> Nodes, OrderedDictionary<string, string> UuidMap, List<(EntityNode, EntityNode)> Pairs, MockLlm Llm)> RunNodeResolutionAsync(ResolutionTrace? trace, CancellationToken cancellationToken)
    {
        var stored = new EntityNode { Uuid = "stored", Name = "Ana Lopez", GroupId = "g", Labels = ["Entity"] }.Validate();
        var other = new EntityNode { Uuid = "other", Name = "Bolt", GroupId = "g", Labels = ["Entity"] }.Validate();
        List<EntityNode> extracted =
        [
            new EntityNode
            {
                Uuid = "x1",
                Name = "Ana Lopez",
                GroupId = "g",
                Labels = ["Entity", "Person"],
                Attributes = new() { ["email"] = "ana@example.com" },
            }.Validate(),
            new EntityNode { Uuid = "x2", Name = "Acme", GroupId = "g", Labels = ["Entity"] }.Validate(),
        ];
        var driver = new MockGraphDriver(search: new MockSearch([[stored, other], []]), ops: new MockOps { GetByNames = (_, _) => [] });
        var llmClient = new MockLlm();
        var clients = MakeClients(llm: llmClient, driver: driver);
        var (nodes, uuidMap, pairs) = await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(
            clients,
            extracted,
            MakeEpisode(),
            [],
            null,
            trace: trace,
            cancellationToken: cancellationToken));
        return (nodes, uuidMap, pairs, llmClient);
    }

    [Fact(Timeout = 5000)]
    public async Task test_node_resolution_returns_same_values_with_and_without_trace()
    {
        var plain = await RunNodeResolutionAsync(null, TestContext.Current.CancellationToken);
        var traced = await RunNodeResolutionAsync(new ResolutionTrace(), TestContext.Current.CancellationToken);

        // `created_at` has a default of the current time, so the comparison leaves it out.
        Assert.Equal(DumpNodes(plain.Nodes), DumpNodes(traced.Nodes));
        Assert.Equal("""{"x1": "stored", "x2": "x2"}""", PyJson.Dumps(plain.UuidMap));
        Assert.Equal("""{"x1": "stored", "x2": "x2"}""", PyJson.Dumps(traced.UuidMap));
        Assert.Empty(plain.Llm.Calls);
        Assert.Empty(traced.Llm.Calls);

        // The stored node gets the label and the attribute of the extracted node.
        Assert.Equal(["Entity", "Person"], plain.Nodes[0].Labels);
        Assert.Equal("""{"email": "ana@example.com"}""", PyJson.Dumps(plain.Nodes[0].Attributes));
    }

    [Fact(Timeout = 5000)]
    public async Task test_node_resolution_trace_records_candidates_and_patch()
    {
        var trace = new ResolutionTrace();
        await RunNodeResolutionAsync(trace, TestContext.Current.CancellationToken);

        Assert.Equal("""{"x1": ["stored", "other"], "x2": []}""", PyJson.Dumps(trace.SeenNodeCandidates));
        Assert.Equal(["stored"], trace.StoredNodeUuids);

        // The trace keeps the labels of each candidate at the time of the analysis.
        Assert.Equal("""{"stored": ["Entity"], "other": ["Entity"]}""", PyJson.Dumps(trace.SeenNodeLabels));
        List<Operation> expected =
        [
            new PatchNode { NodeUuid = "stored", Labels = ["Entity", "Person"], Attributes = new() { ["email"] = "ana@example.com" } },
        ];
        Assert.Equal(Dump(expected), Dump(trace.Operations));
    }

    private static string DumpNodes(List<EntityNode> nodes) => PyJson.Dumps(nodes.Select(node =>
    {
        var dump = node.ModelDump(json: true);
        dump.Remove("created_at");
        return (object?)dump;
    }).ToList());

    // PORT: `==` on two lists of pydantic operations compares the fields of each model.
    private static string Dump(List<Operation> operations) =>
        PyJson.Dumps(operations.Select(operation => (object?)((IPydanticModel)operation).ModelDump(true)).ToList());
}
