// Ported from tests/utils/maintenance/test_bulk_utils.py (port/RULES.md, R25). The code under test is
// graphiti_core/utils/bulk_utils.py.
//
// PORT: `_make_clients()` with MagicMock members is the clients of BulkUtilsRun, whose fakes record each call. A
// list `==` of edges tests identity first (R18), so the tests compare the items with Assert.Same.
//
// Decision of open item O10, for the tests that patch a function that bulk_utils imports. A test has a form in two cases:
// a fake at the network boundary gives the result that the patched function returns, and no assertion reads the arguments
// of the patched function; or the arguments pass unchanged into one call that the fake sees, and the test asserts that call.
// Such a test runs the real function. The Python oracle checked each fake against the result of the patch.
// Ported (four): test_dedupe_nodes_bulk_single_episode, and the three extraction tests that assert the custom
// instructions: _passes_custom_instructions_to_combined_extractor, _passes_custom_instructions_to_extract_nodes, and
// _custom_instructions_multiple_episodes.
//
// TODO(port): O10. These nine tests have no form. Each line names the recorded case of values/bulk_utils.json that covers
// the same behavior (the index is the position in `async_cases`).
// - test_dedupe_nodes_bulk_reuses_canonical_nodes (`resolve_extracted_nodes`): the patch branches on and asserts its
//   arguments. Covered by async case 32 (dedupe_nodes_bulk, nodes of three episodes with exact name matches).
// - test_dedupe_nodes_bulk_merges_identity_matches_before_name_comparison (`resolve_extracted_nodes`): the patch asserts its
//   keyword arguments. Covered by async cases 34 (identity match) and 59 (the email of the second node has spaces).
// - test_dedupe_nodes_bulk_uuid_map_respects_direction (`resolve_extracted_nodes`): the patch branches on and asserts its
//   arguments. Covered by async case 36 (an alias that resolves to a stored node, then the directed map).
// - test_dedupe_nodes_bulk_missing_canonical_falls_back (`resolve_extracted_nodes`): the patch returns a map onto a uuid that
//   no resolved node has, and the real function cannot return it. Covered by async case 58 (the two ERROR lines).
// - test_dedupe_edges_bulk_deduplicates_within_episode (`create_entity_edge_embeddings`, `resolve_extracted_edge`): the patch
//   records its `related_edges` argument, and the test asserts it. Covered by async case 61 (two edges of one episode).
// - test_dedupe_edges_bulk_merges_episode_provenance_for_duplicates (`create_entity_edge_embeddings`, `resolve_extracted_edge`):
//   the oracle shows that the real function returns no duplicate on its exact-match path, where the patch returns the related
//   edge, so no fake gives the patched result. Covered by async cases 39 and 63 (an LLM duplicate for an edge of a second
//   episode).
// - test_extract_nodes_and_edges_bulk_passes_edge_ontology_to_combined_extractor (`combined_extraction.extract_nodes_and_edges`):
//   the ontology and the strict flag reach the prompt as text, not unchanged. Covered by async case 71.
// - test_extract_nodes_and_edges_bulk_passes_custom_instructions_to_extract_edges (`extract_nodes`, `extract_edges`): the patch
//   asserts the nodes, the type map, and the types, which reach the prompt as text. Covered by async case 28.
// - test_extract_nodes_and_edges_bulk_defaults_to_combined_extraction (`combined_extraction.extract_nodes_and_edges`): the
//   patch asserts that an argument is `None`, which no call shows. Covered by async case 67 (all optional arguments left out).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.PyCompat;
using Graphiti.Core.LlmClient;

public class TestBulkUtils
{
    [Fact(Timeout = 5000)]
    public async Task test_dedupe_nodes_bulk_handles_empty_batch()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new BulkUtilsRun(new() { ["fn"] = "dedupe_nodes_bulk" }, source, cancelAt: -1);

        var (nodesByEpisode, compressedMap) = await LoopGuard.RunAsync(() => BulkUtils.DedupeNodesBulkAsync(
            run.Clients,
            [],
            [],
            cancellationToken: source.Token));

        Assert.Empty(nodesByEpisode);
        Assert.Empty(compressedMap);
        Assert.Equal(0, run.Count);
    }

    [Fact]
    public void test_build_directed_uuid_map_empty()
    {
        Assert.Empty(BulkUtils.BuildDirectedUuidMap([]));
    }

    [Fact]
    public void test_build_directed_uuid_map_chain()
    {
        var mapping = BulkUtils.BuildDirectedUuidMap(
        [
            ("a", "b"),
            ("b", "c"),
        ]);

        Assert.Equal("c", mapping["a"]);
        Assert.Equal("c", mapping["b"]);
        Assert.Equal("c", mapping["c"]);
    }

    [Fact]
    public void test_build_directed_uuid_map_preserves_direction()
    {
        var mapping = BulkUtils.BuildDirectedUuidMap(
        [
            ("alias", "canonical"),
        ]);

        Assert.Equal("canonical", mapping["alias"]);
        Assert.Equal("canonical", mapping["canonical"]);
    }

    [Fact]
    public void test_resolve_edge_pointers_updates_sources()
    {
        var createdAt = DatetimeUtils.UtcNow();
        var edge = new EntityEdge
        {
            Name = "knows",
            Fact = "fact",
            GroupId = "group",
            SourceNodeUuid = "alias",
            TargetNodeUuid = "target",
            CreatedAt = createdAt,
        }.Validate();

        var surviving = BulkUtils.ResolveEdgePointers([edge], new OrderedDictionary<string, string> { ["alias"] = "canonical" });

        Assert.Equal("canonical", edge.SourceNodeUuid);
        Assert.Equal("target", edge.TargetNodeUuid);
        Assert.Collection(surviving, e => Assert.Same(edge, e));
    }

    /// <summary>Dedup can map an endpoint onto the edge's other endpoint; that edge must go.</summary>
    [Fact]
    public void test_resolve_edge_pointers_drops_edges_collapsed_onto_one_node()
    {
        var createdAt = DatetimeUtils.UtcNow();
        var collapsing = new EntityEdge
        {
            Name = "pinned_at",
            Fact = "Postgres is pinned at Postgres 16.",
            GroupId = "group",
            SourceNodeUuid = "postgres",
            TargetNodeUuid = "postgres-16",
            CreatedAt = createdAt,
        }.Validate();
        var survivingEdge = new EntityEdge
        {
            Name = "knows",
            Fact = "fact",
            GroupId = "group",
            SourceNodeUuid = "alias",
            TargetNodeUuid = "target",
            CreatedAt = createdAt,
        }.Validate();

        var surviving = BulkUtils.ResolveEdgePointers(
            [collapsing, survivingEdge],
            new OrderedDictionary<string, string> { ["postgres-16"] = "postgres", ["alias"] = "canonical" });

        Assert.Collection(surviving, e => Assert.Same(survivingEdge, e));
    }

    /// <summary>Extracted self-referencing edges stay after pointer resolution.</summary>
    [Fact]
    public void test_resolve_edge_pointers_keeps_original_self_edges()
    {
        var createdAt = DatetimeUtils.UtcNow();
        var selfEdge = new EntityEdge
        {
            Name = "feels_happy",
            Fact = "Alice feels happy",
            GroupId = "group",
            SourceNodeUuid = "alice",
            TargetNodeUuid = "alice",
            CreatedAt = createdAt,
        }.Validate();
        var remappedSelfEdge = new EntityEdge
        {
            Name = "jogs",
            Fact = "Alice goes jogging every morning",
            GroupId = "group",
            SourceNodeUuid = "alice-alias",
            TargetNodeUuid = "alice-alias",
            CreatedAt = createdAt,
        }.Validate();

        var surviving = BulkUtils.ResolveEdgePointers(
            [selfEdge, remappedSelfEdge],
            new OrderedDictionary<string, string> { ["alice-alias"] = "alice" });

        Assert.Collection(surviving, e => Assert.Same(selfEdge, e), e => Assert.Same(remappedSelfEdge, e));
        Assert.Equal("alice", remappedSelfEdge.SourceNodeUuid);
        Assert.Equal("alice", remappedSelfEdge.TargetNodeUuid);
    }

    // Ported from tests/ingestion/test_update_queries.py:100. The code under test is execute_write_with_timeout.
    // PORT: the FakeSession of upstream is the session of BulkUtilsRun, which records the timeout parameter of the
    // session write in place of `wrapped.timeout` (decision D52). The work function closes over `'x'` (R9), so the
    // test reads the argument from the query that the work function runs.
    // PORT: `== 'result'` has no C# form. The work function returns no value, so the call returns no value (R9).
    [Fact(Timeout = 5000)]
    public async Task test_neo4j_write_gets_the_limit_as_transaction_timeout()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new BulkUtilsRun(new() { ["provider"] = "neo4j", ["ops"] = false }, source, cancelAt: -1);

        Task Work(Graphiti.Core.Driver.Transaction tx, string value, CancellationToken token) =>
            tx.RunAsync("RETURN $value", new() { ["value"] = value }, token);

        await LoopGuard.RunAsync(() => BulkUtils.ExecuteWriteWithTimeoutAsync(run.Driver, 7.5, (tx, token) => Work(tx, "x", token), source.Token));

        var write = Assert.Single(run.Events, e => (string?)((OrderedDictionary<string, object?>)e!)["kind"] == "execute_write");
        Assert.Equal(7.5, ((OrderedDictionary<string, object?>)write!)["timeout"]);
        var query = Assert.Single(run.Events, e => (string?)((OrderedDictionary<string, object?>)e!)["kind"] == "tx_run");
        Assert.Equal("[\"x\"]", PyJson.Dumps(((OrderedDictionary<string, object?>)query!)["values"]));
        Assert.Single(run.Events, e => (string?)((OrderedDictionary<string, object?>)e!)["kind"] == "close");
    }

    private static EpisodicNode MakeEpisode(string suffix) =>
        new EpisodicNode
        {
            Name = $"episode-{suffix}",
            GroupId = "group",
            Labels = [],
            Source = EpisodeType.Message,
            Content = "content",
            SourceDescription = "test",
            CreatedAt = DatetimeUtils.UtcNow(),
            ValidAt = DatetimeUtils.UtcNow(),
        }.Validate();

    private static int CountOf(BulkUtilsRun run, string kind) =>
        run.Events.Count(e => (string?)((OrderedDictionary<string, object?>)e!)["kind"] == kind);

    private static OrderedDictionary<string, object?> EmptyReply(string prompt, params string[] keys)
    {
        var reply = new OrderedDictionary<string, object?>();
        foreach (var key in keys)
        {
            reply[key] = new List<object?>();
        }

        return new() { ["prompt"] = prompt, ["needle"] = string.Empty, ["reply"] = reply };
    }

    // Ported from tests/utils/maintenance/test_bulk_utils.py:144.
    // PORT: upstream patches `resolve_extracted_nodes` with an AsyncMock that returns ([extracted], {uuid: uuid}, []). The port
    // runs the real function. The fake graph holds no stored node and no candidate, so the function resolves the node to
    // itself, which is the patched result (the oracle: one node_get_by_names call, no LLM call). `assert_awaited_once` is the
    // one node_get_by_names call that the fake graph sees.
    [Fact(Timeout = 5000)]
    public async Task test_dedupe_nodes_bulk_single_episode()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new BulkUtilsRun(new() { ["fn"] = "dedupe_nodes_bulk" }, source, cancelAt: -1);
        var episode = MakeEpisode("solo");
        var extracted = new EntityNode { Name = "Solo", GroupId = "group", Labels = ["Entity"] }.Validate();

        var (nodesByEpisode, compressedMap) = await LoopGuard.RunAsync(() => BulkUtils.DedupeNodesBulkAsync(
            run.Clients,
            [[extracted]],
            [(episode, [])],
            cancellationToken: source.Token));

        var only = Assert.Single(nodesByEpisode);
        Assert.Equal(episode.Uuid, only.Key);
        Assert.Collection(only.Value, n => Assert.Equal(extracted, n));
        var entry = Assert.Single(compressedMap);
        Assert.Equal(extracted.Uuid, entry.Key);
        Assert.Equal(extracted.Uuid, entry.Value);
        Assert.Equal(1, CountOf(run, "node_get_by_names"));
        Assert.Equal(0, CountOf(run, "llm"));
    }

    // Ported from tests/utils/maintenance/test_bulk_utils.py:490.
    // PORT: upstream patches `combined_extraction.extract_nodes_and_edges` and reads its keyword argument. The port runs the
    // real function. The fake LLM answers with no entity and no edge, which is the patched result ([], [], {}), and the
    // instructions pass unchanged into the one prompt that the fake sees (the oracle: one call of
    // extract_nodes_and_edges.extract_message, and the user message holds the text).
    [Fact(Timeout = 5000)]
    public async Task test_extract_nodes_and_edges_bulk_passes_custom_instructions_to_combined_extractor()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new BulkUtilsRun(
            new() { ["fn"] = "extract_nodes_and_edges_bulk", ["replies"] = new List<object?> { EmptyReply("extract_nodes_and_edges.extract_message", "extracted_entities", "edges") } },
            source,
            cancelAt: -1);
        var customInstructions = "Focus on extracting person entities and their relationships.";

        var (nodes, edges) = await LoopGuard.RunAsync(() => BulkUtils.ExtractNodesAndEdgesBulkAsync(
            run.Clients,
            [(MakeEpisode("1"), [])],
            [],
            customExtractionInstructions: customInstructions,
            useCombinedExtraction: true,
            cancellationToken: source.Token));

        Assert.Collection(nodes, n => Assert.Empty(n));
        Assert.Collection(edges, e => Assert.Empty(e));
        var (promptName, messages) = Assert.Single(run.Prompts);
        Assert.Equal("extract_nodes_and_edges.extract_message", promptName);
        Assert.Contains(messages, m => m.Content.Contains(customInstructions, StringComparison.Ordinal));
    }

    // Ported from tests/utils/maintenance/test_bulk_utils.py:595.
    // PORT: upstream patches `extract_nodes` (it records the instructions) and `extract_edges`. The port runs both real
    // functions. The fake LLM answers with no entity and no edge, which are the patched results ([], {}) and ([], []) (the
    // oracle). The instructions pass unchanged into the one prompt of extract_nodes.extract_message.
    [Fact(Timeout = 5000)]
    public async Task test_extract_nodes_and_edges_bulk_passes_custom_instructions_to_extract_nodes()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new BulkUtilsRun(new() { ["fn"] = "extract_nodes_and_edges_bulk" }, source, cancelAt: -1);
        var customInstructions = "Focus on extracting person entities and their relationships.";

        await LoopGuard.RunAsync(() => BulkUtils.ExtractNodesAndEdgesBulkAsync(
            run.Clients,
            [(MakeEpisode("1"), [])],
            [],
            customExtractionInstructions: customInstructions,
            useCombinedExtraction: false,
            cancellationToken: source.Token));

        var extractNodes = run.Prompts.Where(p => p.PromptName == "extract_nodes.extract_message").ToList();
        var (_, messages) = Assert.Single(extractNodes);
        Assert.Contains(messages, m => m.Content.Contains(customInstructions, StringComparison.Ordinal));
    }

    // Ported from tests/utils/maintenance/test_bulk_utils.py:733.
    // PORT: as the test of the combined extractor above, with three episodes: the fake LLM sees one prompt for each episode,
    // and each one holds the instructions.
    [Fact(Timeout = 5000)]
    public async Task test_extract_nodes_and_edges_bulk_custom_instructions_multiple_episodes()
    {
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new BulkUtilsRun(
            new() { ["fn"] = "extract_nodes_and_edges_bulk", ["replies"] = new List<object?> { EmptyReply("extract_nodes_and_edges.extract_message", "extracted_entities", "edges") } },
            source,
            cancelAt: -1);
        var customInstructions = "Extract entities related to financial transactions.";

        await LoopGuard.RunAsync(() => BulkUtils.ExtractNodesAndEdgesBulkAsync(
            run.Clients,
            [(MakeEpisode("1"), []), (MakeEpisode("2"), []), (MakeEpisode("3"), [])],
            [],
            customExtractionInstructions: customInstructions,
            useCombinedExtraction: true,
            cancellationToken: source.Token));

        Assert.Equal(3, run.Prompts.Count);
        foreach (var (promptName, messages) in run.Prompts)
        {
            Assert.Equal("extract_nodes_and_edges.extract_message", promptName);
            Assert.Contains(messages, m => m.Content.Contains(customInstructions, StringComparison.Ordinal));
        }
    }
}
