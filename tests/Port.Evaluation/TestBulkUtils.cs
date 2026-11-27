// Ported from tests/utils/maintenance/test_bulk_utils.py (port/RULES.md, R25). The code under test is
// graphiti_core/utils/bulk_utils.py.
//
// PORT: `_make_clients()` with MagicMock members is the clients of BulkUtilsRun, whose fakes record each call. A
// list `==` of edges tests identity first (R18), so the tests compare the items with Assert.Same.
//
// TODO(port): O10. These tests patch a function that bulk_utils imports, which has no C# form:
// - test_dedupe_nodes_bulk_reuses_canonical_nodes (`resolve_extracted_nodes`)
// - test_dedupe_nodes_bulk_merges_identity_matches_before_name_comparison (`resolve_extracted_nodes`)
// - test_dedupe_nodes_bulk_single_episode (`resolve_extracted_nodes`)
// - test_dedupe_nodes_bulk_uuid_map_respects_direction (`resolve_extracted_nodes`)
// - test_dedupe_nodes_bulk_missing_canonical_falls_back (`resolve_extracted_nodes`)
// - test_dedupe_edges_bulk_deduplicates_within_episode (`create_entity_edge_embeddings`, `resolve_extracted_edge`)
// - test_dedupe_edges_bulk_merges_episode_provenance_for_duplicates (`create_entity_edge_embeddings`,
//   `resolve_extracted_edge`)
// - test_extract_nodes_and_edges_bulk_passes_custom_instructions_to_combined_extractor
//   (`combined_extraction.extract_nodes_and_edges`)
// - test_extract_nodes_and_edges_bulk_passes_edge_ontology_to_combined_extractor
//   (`combined_extraction.extract_nodes_and_edges`)
// - test_extract_nodes_and_edges_bulk_passes_custom_instructions_to_extract_nodes (`extract_nodes`, `extract_edges`)
// - test_extract_nodes_and_edges_bulk_passes_custom_instructions_to_extract_edges (`extract_nodes`, `extract_edges`)
// - test_extract_nodes_and_edges_bulk_defaults_to_combined_extraction (`combined_extraction.extract_nodes_and_edges`)
// - test_extract_nodes_and_edges_bulk_custom_instructions_multiple_episodes
//   (`combined_extraction.extract_nodes_and_edges`)
// The recording of the unit (BulkUtilsValuesTests) covers the paths of dedupe_nodes_bulk, dedupe_edges_bulk, and the
// separate extraction. The combined extraction with one episode or more waits for packet P7d.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.PyCompat;

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

        Task Work(Graphiti.Core.Driver.Transaction tx, string value) =>
            tx.RunAsync("RETURN $value", new() { ["value"] = value }, source.Token);

        await LoopGuard.RunAsync(() => BulkUtils.ExecuteWriteWithTimeoutAsync(run.Driver, 7.5, tx => Work(tx, "x"), source.Token));

        var write = Assert.Single(run.Events, e => (string?)((OrderedDictionary<string, object?>)e!)["kind"] == "execute_write");
        Assert.Equal(7.5, ((OrderedDictionary<string, object?>)write!)["timeout"]);
        var query = Assert.Single(run.Events, e => (string?)((OrderedDictionary<string, object?>)e!)["kind"] == "tx_run");
        Assert.Equal("[\"x\"]", PyJson.Dumps(((OrderedDictionary<string, object?>)query!)["values"]));
        Assert.Single(run.Events, e => (string?)((OrderedDictionary<string, object?>)e!)["kind"] == "close");
    }
}
