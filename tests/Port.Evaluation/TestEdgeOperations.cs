// Ported from tests/utils/maintenance/test_edge_operations.py (port/RULES.md, R25). The code under test is
// graphiti_core/utils/maintenance/edge_operations.py.
//
// PORT: `MagicMock()` with an AsyncMock `generate_response` is EdgeTestLlm. A test that patches `edge_ops.search` gets
// EdgeTestSearchDriver: the real search reads the search interface of the driver for its two edge searches, so the fake
// answers there (Part 3, the row of O10). A patch of `create_entity_edge_embeddings` with a mock that does nothing is
// the real function with the fake embedder EdgeTestEmbedder, and a patch of `semaphore_gather` with a gather that runs
// the calls one after the other is the real gather. `EntityEdge.get_between_nodes` has no caller in the module.
//
// PORT: a test that patches `_extract_edge_timestamps` with `AsyncMock(return_value=None)` runs the real function, and the
// fake model client answers its prompt with no date (`EdgeTimestamps(valid_at=None, invalid_at=None)`), so that the edge
// keeps its dates, as with the patch. The tests have a form under two conditions: a fake at the network boundary gives
// the result that the patched function returns, and no assertion reads the arguments of the patched function. Each of
// them was run against Python with the patch removed and the same fake reply (the oracle gave the same result).
// test_extract_edge_timestamps_prefers_edge_reference_time patches the prompt function and `apply_extracted_timestamps`;
// it reads the reference time in the prompt that the fake model client gets, and the dates of the edge after the real
// `apply_extracted_timestamps`.
//
// TODO(port): O10. These tests patch a function of the module itself and assert on the arguments of the patch, which has
// no C# form:
// - test_resolve_extracted_edges_fast_path_deduplication (`_dedupe_extracted_edge`)
// - test_resolve_extracted_edges_fast_path_deduplicates_reverse_direction_by_fact (`_dedupe_extracted_edge`)
// - test_resolve_extracted_edges_merges_override_candidates (`_dedupe_extracted_edge`)
// The recording of the unit (EdgeOperationsValuesTests) has cases of the paths that these three tests reach, but it does
// not read the arguments of the patched function.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Prompts;
using Graphiti.Core.Search;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class TestEdgeOperations
{
    private static readonly GraphTypeDescriptor OccurredAtEdge = new("OccurredAtEdge", "Edge model stub for OCCURRED_AT.", []);

    private static readonly GraphTypeDescriptor WorksInEdge = new("WorksInEdge", "A person works in a domain.", []);

    private static readonly GraphTypeDescriptor MeansEdge = new("MeansEdge", "A term means something.", []);

    private static readonly GraphTypeDescriptor MentionsEdge = new("MentionsEdge", "Any entity mentions any other entity. No signature is declared.", []);

    private static readonly OrderedDictionary<string, GraphTypeDescriptor> SignatureEdgeTypes = new()
    {
        ["WORKS_IN"] = WorksInEdge,
        ["MEANS"] = MeansEdge,
        ["MENTIONS"] = MentionsEdge,
    };

    private static readonly OrderedDictionary<(string, string), List<string>> SignatureEdgeTypeMap = new()
    {
        [("Person", "Domain")] = ["WORKS_IN"],
        [("User", "Domain")] = ["WORKS_IN"],
        [("Term", "Rule")] = ["MEANS"],
        [("Term", "Domain")] = ["MEANS"],
    };

    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edge_exact_fact_short_circuit()
    {
        var mockLlmClient = new EdgeTestLlm();
        var extracted = Edge("source_uuid", "target_uuid", "test_edge", "Related fact", ["episode_1"]);
        List<EntityEdge> relatedEdges =
        [
            Edge("source_uuid", "target_uuid", "related_edge", " related FACT  ", ["episode_2"], createdAt: DaysAgo(1)),
        ];

        var (resolvedEdge, duplicateEdges, invalidated) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgeAsync(
            mockLlmClient,
            extracted,
            relatedEdges,
            MockExistingEdges(),
            MockCurrentEpisode(),
            edgeTypeCandidates: null,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Same(relatedEdges[0], resolvedEdge);
        Assert.Single(resolvedEdge.Episodes, "episode_1");
        Assert.Empty(duplicateEdges);
        Assert.Empty(invalidated);
        Assert.Empty(mockLlmClient.Calls);
    }

    [Theory(Timeout = 5000)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task test_resolve_extracted_edge_merges_episode_provenance(bool exactMatch)
    {
        var mockLlmClient = new EdgeTestLlm();
        var episode = Episode("episode_2", "Episode", "test", "Episode content");
        var extractedEdge = Edge("source_uuid", "target_uuid", "test_edge", exactMatch ? "Related fact" : "Extracted fact", ["episode_1", "episode_2"], uuid: "extracted-edge");
        var existingEdge = Edge("source_uuid", "target_uuid", "test_edge", "Related fact", ["episode_0"], uuid: "existing-edge", createdAt: DaysAgo(1));

        if (!exactMatch)
        {
            mockLlmClient = new EdgeTestLlm(new() { ["duplicate_facts"] = new List<object?> { 0L }, ["contradicted_facts"] = new List<object?>() });
        }

        var (resolvedEdge, _, _) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgeAsync(
            mockLlmClient, extractedEdge, [existingEdge], [], episode, edgeTypeCandidates: null, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Same(existingEdge, resolvedEdge);
        Assert.Equal(["episode_0", "episode_1", "episode_2"], resolvedEdge.Episodes);

        (resolvedEdge, _, _) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgeAsync(
            mockLlmClient, extractedEdge, [existingEdge], [], episode, edgeTypeCandidates: null, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(["episode_0", "episode_1", "episode_2"], resolvedEdge.Episodes);
    }

    // PORT: the Python test patches `_extract_edge_timestamps`. The real function runs (see the header).
    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edge_keeps_caller_attributes_without_schema()
    {
        var mockLlmClient = LlmWithoutTimestamps(NoDuplicates());

        var extracted = Edge("source_uuid", "target_uuid", "VIEWED", "Customer viewed listing 42", ["episode_1"]);
        extracted.Attributes = new() { ["event_name"] = "car_viewed", ["event_id"] = "42" };

        var (resolvedEdge, duplicateEdges, invalidated) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgeAsync(
            mockLlmClient,
            extracted,
            [],
            MockExistingEdges(),
            MockCurrentEpisode(),
            edgeTypeCandidates: null,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(new OrderedDictionary<string, object?> { ["event_name"] = "car_viewed", ["event_id"] = "42" }, resolvedEdge.Attributes);
        Assert.Empty(duplicateEdges);
        Assert.Empty(invalidated);
    }

    // PORT: the Python test patches `_extract_edge_timestamps`. The real function runs (see the header).
    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edge_clears_attributes_when_schema_omits_type()
    {
        var mockLlmClient = LlmWithoutTimestamps(NoDuplicates());

        var extracted = Edge("source_uuid", "target_uuid", "VIEWED", "Customer viewed listing 42", ["episode_1"]);
        extracted.Attributes = new() { ["event_name"] = "car_viewed" };

        var (resolvedEdge, _, _) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgeAsync(
            mockLlmClient,
            extracted,
            [],
            MockExistingEdges(),
            MockCurrentEpisode(),
            edgeTypeCandidates: new(),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(resolvedEdge.Attributes);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edges_keeps_unknown_names()
    {
        var llmClient = new EdgeTestLlm(NoDuplicates());
        var clients = Clients(llmClient);
        var sourceNode = Node("source_uuid", "User Node", ["User"]);
        var targetNode = Node("target_uuid", "Topic Node", ["Topic"]);
        var extractedEdge = Edge(sourceNode.Uuid, targetNode.Uuid, "INTERACTED_WITH", "User interacted with topic", []);
        var episode = Episode("episode_uuid", "Episode", "desc", "Episode content");
        var edgeTypes = new OrderedDictionary<string, GraphTypeDescriptor> { ["OCCURRED_AT"] = OccurredAtEdge };
        var edgeTypeMap = new OrderedDictionary<(string, string), List<string>> { [("Event", "Entity")] = ["OCCURRED_AT"] };

        var (resolvedEdges, invalidatedEdges, newEdges) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgesAsync(
            clients, [extractedEdge], episode, [sourceNode, targetNode], edgeTypes, edgeTypeMap, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("INTERACTED_WITH", resolvedEdges[0].Name);
        Assert.Empty(invalidatedEdges);
        Assert.Equal(resolvedEdges, newEdges); // No duplicates, so all edges are new
    }

    [Fact]
    public void test_enforce_edge_type_signatures_keeps_matching_custom_edge()
    {
        var person = SignatureNode("person", "Person");
        var domain = SignatureNode("domain", "Domain");
        var edge = SignatureEdge("WORKS_IN", person, domain);

        foreach (var strict in new[] { true, false })
        {
            var (kept, keptTypes) = Enforce([edge], [person, domain], strict);
            Assert.Same(edge, Assert.Single(kept));
            Assert.Equal("WORKS_IN", edge.Name);
            Assert.Equal("x", Assert.Single(edge.Attributes, a => a.Key == "note").Value);
            var types = Assert.Single(keptTypes);
            Assert.Same(WorksInEdge, Assert.Single(types, t => t.Key == "WORKS_IN").Value);
        }
    }

    [Fact]
    public void test_enforce_edge_type_signatures_accepts_any_declared_signature()
    {
        var user = SignatureNode("user", "User");
        var term = SignatureNode("term", "Term");
        var domain = SignatureNode("domain", "Domain");
        List<EntityEdge> edges = [SignatureEdge("WORKS_IN", user, domain), SignatureEdge("MEANS", term, domain)];

        var (kept, _) = Enforce(edges, [user, term, domain], strict: true);

        Assert.Equal(["WORKS_IN", "MEANS"], kept.Select(e => e.Name));
    }

    [Fact]
    public void test_enforce_edge_type_signatures_strict_drops_mismatched_custom_edge()
    {
        var person = SignatureNode("person", "Person");
        var org = SignatureNode("org", "Organization");
        var validDomain = SignatureNode("domain", "Domain");
        var wrong = SignatureEdge("WORKS_IN", person, org);
        var valid = SignatureEdge("WORKS_IN", person, validDomain);

        var (kept, keptTypes) = Enforce([wrong, valid], [person, org, validDomain], strict: true);

        Assert.Same(valid, Assert.Single(kept));
        Assert.Same(WorksInEdge, Assert.Single(Assert.Single(keptTypes), t => t.Key == "WORKS_IN").Value);
    }

    [Fact]
    public void test_enforce_edge_type_signatures_non_strict_renames_mismatched_custom_edge()
    {
        var person = SignatureNode("person", "Person");
        var org = SignatureNode("org", "Organization");
        var wrong = SignatureEdge("WORKS_IN", person, org);

        var (kept, keptTypes) = Enforce([wrong], [person, org], strict: false);

        Assert.Same(wrong, Assert.Single(kept));
        Assert.Equal(EdgeOperations.DefaultEdgeName, wrong.Name);
        Assert.Empty(wrong.Attributes);
        Assert.Empty(Assert.Single(keptTypes));
    }

    [Fact]
    public void test_enforce_edge_type_signatures_ignores_non_custom_names()
    {
        var person = SignatureNode("person", "Person");
        var org = SignatureNode("org", "Organization");
        var edge = SignatureEdge("EMPLOYED_BY", person, org);

        foreach (var strict in new[] { true, false })
        {
            var (kept, _) = Enforce([edge], [person, org], strict);
            Assert.Same(edge, Assert.Single(kept));
            Assert.Equal("EMPLOYED_BY", edge.Name);
        }
    }

    [Fact]
    public void test_enforce_edge_type_signatures_ignores_custom_type_without_signature()
    {
        var person = SignatureNode("person", "Person");
        var org = SignatureNode("org", "Organization");
        var edge = SignatureEdge("MENTIONS", person, org);

        foreach (var strict in new[] { true, false })
        {
            var (kept, keptTypes) = Enforce([edge], [person, org], strict);
            Assert.Same(edge, Assert.Single(kept));
            Assert.Equal("MENTIONS", edge.Name);
            Assert.Equal("x", Assert.Single(edge.Attributes, a => a.Key == "note").Value);
            Assert.Empty(Assert.Single(keptTypes));
        }
    }

    [Fact]
    public void test_enforce_edge_type_signatures_treats_unknown_endpoint_as_generic_entity()
    {
        var person = SignatureNode("person", "Person");
        var missing = SignatureNode("missing", "Domain");
        var edge = SignatureEdge("WORKS_IN", person, missing);

        var (kept, _) = Enforce([edge], [person], strict: true);

        Assert.Empty(kept);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edges_applies_signature_before_dedupe()
    {
        var llmClient = new EdgeTestLlm(NoDuplicates());
        var embed = new EdgeTestEmbedder();
        var clients = Clients(llmClient, embed);

        var person = SignatureNode("person", "Person");
        var org = SignatureNode("org", "Organization");
        var domain = SignatureNode("domain", "Domain");
        var wrong = SignatureEdge("WORKS_IN", person, org);
        var valid = SignatureEdge("WORKS_IN", person, domain);
        var episode = Episode("episode_uuid", "Episode", "desc", "Episode content");

        var (resolved, _, newEdges) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgesAsync(
            clients, [wrong, valid], episode, [person, org, domain], SignatureEdgeTypes, SignatureEdgeTypeMap, strictEdgeTypes: true, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([valid.Uuid], resolved.Select(e => e.Uuid));
        Assert.Equal(resolved, newEdges);
        Assert.Equal([valid.Fact], embed.Batches[0]);

        var renamed = SignatureEdge("WORKS_IN", person, org);
        (resolved, _, _) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgesAsync(
            clients, [renamed], episode, [person, org], SignatureEdgeTypes, SignatureEdgeTypeMap, strictEdgeTypes: false, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([EdgeOperations.DefaultEdgeName], resolved.Select(e => e.Name));

        // Two edges with the same endpoints and fact but different names: the
        // mismatched name is dropped first, so the exact-fact dedupe keeps the valid one.
        var term = SignatureNode("term", "Term");
        var sameFactWrong = SignatureEdge("WORKS_IN", term, domain);
        var sameFactValid = SignatureEdge("MEANS", term, domain);
        sameFactValid.Fact = sameFactWrong.Fact;
        (resolved, _, _) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgesAsync(
            clients, [sameFactWrong, sameFactValid], episode, [term, domain], SignatureEdgeTypes, SignatureEdgeTypeMap, strictEdgeTypes: true, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal([(sameFactValid.Uuid, "MEANS")], resolved.Select(e => (e.Uuid, e.Name)));
    }

    // Test that resolve_extracted_edge correctly uses integer indices for LLM duplicate detection.
    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edge_uses_integer_indices_for_duplicates()
    {
        // Mock LLM to return duplicate_facts with integer indices
        var mockLlmClient = new EdgeTestLlm(new()
        {
            ["duplicate_facts"] = new List<object?> { 0L, 1L }, // LLM identifies first two related edges as duplicates
            ["contradicted_facts"] = new List<object?>(),
        });

        var extractedEdge = Edge("source_uuid", "target_uuid", "test_edge", "User likes yoga", []);
        var episode = Episode("episode_uuid", "Episode", "desc", "Episode content");

        // Create multiple related edges - LLM should receive these with integer indices
        var relatedEdge0 = Edge("source_uuid", "target_uuid", "test_edge", "User enjoys yoga", ["episode_1"], createdAt: DaysAgo(1));
        var relatedEdge1 = Edge("source_uuid", "target_uuid", "test_edge", "User practices yoga", ["episode_2"], createdAt: DaysAgo(2));
        var relatedEdge2 = Edge("source_uuid", "target_uuid", "test_edge", "User loves swimming", ["episode_3"], createdAt: DaysAgo(3));
        List<EntityEdge> relatedEdges = [relatedEdge0, relatedEdge1, relatedEdge2];

        var (resolvedEdge, invalidated, duplicates) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgeAsync(
            mockLlmClient, extractedEdge, relatedEdges, [], episode, edgeTypeCandidates: null, cancellationToken: TestContext.Current.CancellationToken));

        // Verify LLM was called
        Assert.Single(mockLlmClient.Calls);

        // Verify the system correctly identified duplicates using integer indices
        // The LLM returned [0, 1], so related_edge_0 and related_edge_1 should be marked as duplicates
        Assert.Equal(2, duplicates.Count);
        Assert.True(Py.Contains(duplicates, relatedEdge0));
        Assert.True(Py.Contains(duplicates, relatedEdge1));
        Assert.Empty(invalidated);

        // Verify that the resolved edge is one of the duplicates (the first one found)
        // Check UUID since the episode list gets modified
        Assert.Equal(relatedEdge0.Uuid, resolvedEdge.Uuid);
        Assert.Contains(episode.Uuid, resolvedEdge.Episodes);
    }

    // Test that edge types used across multiple node type pairs preserve all signatures.
    //
    // This tests the fix for the bug where dict comprehension would overwrite
    // previous signatures when the same edge type appeared in multiple node pairs.
    [Fact]
    public void test_edge_type_signatures_map_preserves_multiple_signatures()
    {
        // Edge type map where the same edge type is used for multiple node pair signatures
        // This is the scenario that was broken before the fix
        var edgeTypeMap = new OrderedDictionary<(string, string), List<string>>
        {
            [("Person", "Person")] = ["InterpersonalRelationship"],
            [("Person", "Entity")] = ["InterpersonalRelationship"], // Same type, different signature
            [("Person", "City")] = ["LocatedIn"],
            [("Entity", "City")] = ["LocatedIn"], // Same type, different signature
        };

        var edgeTypes = new OrderedDictionary<string, GraphTypeDescriptor>
        {
            ["InterpersonalRelationship"] = new("InterpersonalRelationship", "A relationship between two people.", []),
            ["LocatedIn"] = new("LocatedIn", "A relationship indicating something is located in a place.", []),
        };

        // Build the mapping the same way as in extract_edges (the fixed implementation)
        var edgeTypeSignaturesMap = SignaturesMap(edgeTypeMap);

        // Verify InterpersonalRelationship has BOTH signatures preserved
        Assert.True(edgeTypeSignaturesMap.ContainsKey("InterpersonalRelationship"));
        var interpersonalSignatures = edgeTypeSignaturesMap["InterpersonalRelationship"];
        Assert.Equal(2, interpersonalSignatures.Count);
        Assert.Contains(("Person", "Person"), interpersonalSignatures);
        Assert.Contains(("Person", "Entity"), interpersonalSignatures);

        // Verify LocatedIn has BOTH signatures preserved
        Assert.True(edgeTypeSignaturesMap.ContainsKey("LocatedIn"));
        var locatedSignatures = edgeTypeSignaturesMap["LocatedIn"];
        Assert.Equal(2, locatedSignatures.Count);
        Assert.Contains(("Person", "City"), locatedSignatures);
        Assert.Contains(("Entity", "City"), locatedSignatures);

        // Verify the context has the correct structure with plural 'fact_type_signatures'
        foreach (var ctx in TypesContext(edgeTypes, edgeTypeSignaturesMap))
        {
            Assert.True(ctx.ContainsKey("fact_type_signatures"));
            Assert.Equal(2, Assert.IsType<List<(string, string)>>(ctx["fact_type_signatures"]).Count); // Each type has 2 signatures
        }
    }

    // Test that edge types with a single signature still work correctly.
    [Fact]
    public void test_edge_type_signatures_map_single_signature_still_works()
    {
        var edgeTypeMap = new OrderedDictionary<(string, string), List<string>>
        {
            [("Person", "Organization")] = ["WorksAt"],
            [("Person", "City")] = ["LivesIn"],
        };

        var edgeTypes = new OrderedDictionary<string, GraphTypeDescriptor>
        {
            ["WorksAt"] = new("BaseModel", null, []),
            ["LivesIn"] = new("BaseModel", null, []),
        };

        // Build the mapping
        var edgeTypeSignaturesMap = SignaturesMap(edgeTypeMap);

        // Verify each edge type has exactly one signature
        Assert.Single(edgeTypeSignaturesMap["WorksAt"]);
        Assert.Contains(("Person", "Organization"), edgeTypeSignaturesMap["WorksAt"]);

        Assert.Single(edgeTypeSignaturesMap["LivesIn"]);
        Assert.Contains(("Person", "City"), edgeTypeSignaturesMap["LivesIn"]);

        // Verify the context structure
        foreach (var ctx in TypesContext(edgeTypes, edgeTypeSignaturesMap))
        {
            Assert.True(ctx.ContainsKey("fact_type_signatures"));
            Assert.Single(Assert.IsType<List<(string, string)>>(ctx["fact_type_signatures"]));
        }
    }

    // Self-edges (source == target) are kept during extraction.
    [Fact(Timeout = 5000)]
    public async Task test_extract_edges_keeps_self_edges()
    {
        var alice = Node("alice_uuid", "Alice", ["Person"]);
        var bob = Node("bob_uuid", "Bob", ["Person"]);
        var mockLlm = new EdgeTestLlm(ExtractedEdgesReply(
            ExtractedEdge("Alice", "Bob", "CONGRATULATED", "Alice congratulated Bob"),
            ExtractedEdge("Alice", "Alice", "FEELS_HAPPY", "Alice feels happy")));
        var episode = Episode("ep_uuid", "Episode", "desc", "Alice congratulated Bob. Alice feels happy.");

        var (edges, _) = await LoopGuard.RunAsync(() => EdgeOperations.ExtractEdgesAsync(
            Clients(mockLlm), new EpisodeOrEpisodes.One { Episode = episode }, [alice, bob], [], new(), groupId: "group_1", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(
            new HashSet<(string, string, string)> { ("alice_uuid", "bob_uuid", "CONGRATULATED"), ("alice_uuid", "alice_uuid", "FEELS_HAPPY") },
            edges.Select(e => (e.SourceNodeUuid, e.TargetNodeUuid, e.Name)).ToHashSet());
    }

    // Edges between different nodes that happen to share a name are NOT self-edges.
    [Fact(Timeout = 5000)]
    public async Task test_extract_edges_keeps_valid_edges_with_same_name_different_nodes()
    {
        var alice = Node("alice_uuid", "Alice", ["Person"]);
        var paris = Node("paris_uuid", "Paris", ["City"]);
        var mockLlm = new EdgeTestLlm(ExtractedEdgesReply(ExtractedEdge("Alice", "Paris", "LIVES_IN", "Alice lives in Paris")));
        var episode = Episode("ep_uuid", "Episode", "desc", "Alice lives in Paris.");

        var (edges, _) = await LoopGuard.RunAsync(() => EdgeOperations.ExtractEdgesAsync(
            Clients(mockLlm), new EpisodeOrEpisodes.One { Episode = episode }, [alice, paris], [], new(), groupId: "group_1", cancellationToken: TestContext.Current.CancellationToken));

        var edge = Assert.Single(edges);
        Assert.Equal("alice_uuid", edge.SourceNodeUuid);
        Assert.Equal("paris_uuid", edge.TargetNodeUuid);
    }

    // Dangling target names are materialized onto the nodes list; the edge is kept.
    [Fact(Timeout = 5000)]
    public async Task test_extract_edges_materializes_missing_target_endpoint()
    {
        var ami = Node("ami_uuid", "AMI", ["Entity"]);
        List<EntityNode> nodes = [ami];
        var mockLlm = new EdgeTestLlm(ExtractedEdgesReply(ExtractedEdge("AMI", "version 2.18.1", "PINNED_AT", "AMI is pinned at version 2.18.1.")));
        var episode = Episode("ep_uuid", "Episode", "desc", "AMI is pinned at version 2.18.1.");

        var (edges, materialized) = await LoopGuard.RunAsync(() => EdgeOperations.ExtractEdgesAsync(
            Clients(mockLlm), new EpisodeOrEpisodes.One { Episode = episode }, nodes, [], new(), groupId: "group_1", cancellationToken: TestContext.Current.CancellationToken));

        var edge = Assert.Single(edges);
        Assert.Equal("PINNED_AT", edge.Name);
        Assert.Equal("ami_uuid", edge.SourceNodeUuid);
        Assert.Equal(new HashSet<string> { "AMI", "version 2.18.1" }, nodes.Select(n => n.Name).ToHashSet());
        var node = Assert.Single(materialized);
        Assert.Equal("version 2.18.1", node.Name);
        Assert.Equal(node.Uuid, edge.TargetNodeUuid);
        Assert.NotEqual(edge.SourceNodeUuid, edge.TargetNodeUuid);
    }

    // Materialized endpoints are removed when the edge is later dropped.
    [Fact(Timeout = 5000)]
    public async Task test_extract_edges_does_not_keep_orphans_when_edge_filtered()
    {
        var pinnedAt = new GraphTypeDescriptor("PINNED_AT", "Software is pinned at a version.", []);
        var ami = Node("ami_uuid", "AMI", ["Entity"]);
        List<EntityNode> nodes = [ami];
        var mockLlm = new EdgeTestLlm(ExtractedEdgesReply(ExtractedEdge("AMI", "version 2.18.1", "NOT_IN_ONTOLOGY", "AMI is pinned at version 2.18.1.")));
        var episode = Episode("ep_uuid", "Episode", "desc", "AMI is pinned at version 2.18.1.");

        var (edges, materialized) = await LoopGuard.RunAsync(() => EdgeOperations.ExtractEdgesAsync(
            Clients(mockLlm),
            new EpisodeOrEpisodes.One { Episode = episode },
            nodes,
            [],
            new() { [("Entity", "Entity")] = ["PINNED_AT"] },
            groupId: "group_1",
            edgeTypes: new() { ["PINNED_AT"] = pinnedAt },
            strictEdgeTypes: true,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(edges);
        Assert.Empty(materialized);
        Assert.Equal(["AMI"], nodes.Select(n => n.Name));
    }

    // Strict edge extraction should only keep configured edge ontology types.
    [Fact(Timeout = 5000)]
    public async Task test_extract_edges_strict_edge_types_filters_derived_relations()
    {
        var integratesWith = new GraphTypeDescriptor("INTEGRATES_WITH", "One product integrates with another product.", []);
        var atlas = Node("atlas_uuid", "Atlas Home Robot", ["Entity", "Product"]);
        var hub = Node("hub_uuid", "Acme Smart Hub", ["Entity", "Product"]);
        var mockLlm = new EdgeTestLlm(ExtractedEdgesReply(
            ExtractedEdge("Atlas Home Robot", "Acme Smart Hub", "INTEGRATES_WITH", "Atlas Home Robot integrates with Acme Smart Hub."),
            ExtractedEdge("Atlas Home Robot", "Acme Smart Hub", "SOLD_WITH", "Atlas Home Robot is sold with Acme Smart Hub.")));
        var episode = Episode("ep_uuid", "Episode", "desc", "Atlas Home Robot integrates with Acme Smart Hub.");

        var (edges, _) = await LoopGuard.RunAsync(() => EdgeOperations.ExtractEdgesAsync(
            Clients(mockLlm),
            new EpisodeOrEpisodes.One { Episode = episode },
            [atlas, hub],
            [],
            new() { [("Product", "Product")] = ["INTEGRATES_WITH"] },
            groupId: "group_1",
            edgeTypes: new() { ["INTEGRATES_WITH"] = integratesWith },
            strictEdgeTypes: true,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(["INTEGRATES_WITH"], edges.Select(e => e.Name));
        var promptText = string.Join("\n", mockLlm.Calls[^1].Messages.Select(m => m.Content));
        Assert.Contains("If a relationship does not match any FACT_TYPE, skip it", promptText, StringComparison.Ordinal);
        Assert.Contains("Do not derive a new relation_type", promptText, StringComparison.Ordinal);
    }

    private static readonly GraphTypeDescriptor EmploymentEdge = new(
        "_EmploymentEdge",
        "Edge attribute schema mirroring a customer's EMPLOYMENT relation.",
        [
            new GraphFieldDescriptor("title", GraphFieldKind.String, false, null, false, null),
            new GraphFieldDescriptor("is_current", GraphFieldKind.String, false, null, false, null),
        ]);

    // End-to-end: when the LLM bleeds meta-reasoning into one attribute, the
    // cap drops that field and the merge logic falls back to the prior on-edge
    // value, without affecting fields the LLM legitimately updated.
    //
    // PORT: the Python test patches `_extract_edge_timestamps`. The real function runs (see the header).
    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edge_overcap_attribute_preserves_prior()
    {
        // The LLM returns a clean update for `title` but bleeds 9 KB of meta-reasoning into
        // `is_current`: the kind of failure the customer reported.
        var bleed = string.Concat(Enumerable.Repeat(
            "true (implied by context, but no new information explicitly stated. "
            + "I should ideally remove it or set it to null. However, the instruction "
            + "is to preserve existing values...) ",
            30));
        var llmClient = LlmWithoutTimestamps(new() { ["title"] = "Senior Engineer", ["is_current"] = bleed });

        var extractedEdge = Edge("person_uuid", "company_uuid", "EMPLOYMENT", "Sam was promoted to Senior Engineer at Northwind", []);
        extractedEdge.Attributes = new() { ["title"] = "Engineer", ["is_current"] = "true" };
        var episode = Episode("episode_uuid", "Episode", "desc", "Sam was promoted to Senior Engineer.");

        var (resolved, dupes, invalidated) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgeAsync(
            llmClient,
            extractedEdge,
            [],
            [],
            episode,
            edgeTypeCandidates: new() { ["EMPLOYMENT"] = EmploymentEdge },
            cancellationToken: TestContext.Current.CancellationToken));

        // Title should reflect the legitimate LLM update.
        Assert.Equal("Senior Engineer", resolved.Attributes["title"]);

        // is_current was bleed-dropped, so the prior value must be preserved (not the
        // 9 KB rant, not None, not absent).
        Assert.Equal("true", resolved.Attributes["is_current"]);
        Assert.Empty(dupes);
        Assert.Empty(invalidated);
    }

    // PORT: the Python test patches `_extract_edge_timestamps`. The real function runs (see the header).
    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edge_keeps_initial_null_attributes()
    {
        var llmClient = LlmWithoutTimestamps(new() { ["title"] = "Senior Engineer", ["is_current"] = null });

        var extractedEdge = Edge("person_uuid", "company_uuid", "EMPLOYMENT", "Sam is a Senior Engineer at Northwind", []);
        var episode = Episode("episode_uuid", "Episode", "desc", "Sam is a Senior Engineer at Northwind.");

        var (resolved, dupes, invalidated) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgeAsync(
            llmClient,
            extractedEdge,
            [],
            [],
            episode,
            edgeTypeCandidates: new() { ["EMPLOYMENT"] = EmploymentEdge },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(new OrderedDictionary<string, object?> { ["title"] = "Senior Engineer", ["is_current"] = null }, resolved.Attributes);
        Assert.Empty(dupes);
        Assert.Empty(invalidated);
    }

    // An extracted (A -> B) edge must see an existing (B -> A) edge as a
    // duplicate candidate; the directed exact-fact fast path can't claim it, so
    // the LLM judges and the extracted edge resolves onto the reverse edge.
    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edges_dedupes_against_reverse_direction()
    {
        var reverseEdge = Edge("target_uuid", "source_uuid", "liked_by", "Yoga is liked by the user", [], uuid: "reverse_0", createdAt: DaysAgo(1));

        // The pair-scoped duplicate search returns the reverse-orientation
        // edge (its endpoints are within the {a,b} filter sets).
        var driver = new EdgeTestSearchDriver((_, _, filter) => filter.EdgeSourceNodeUuids is { Count: > 0 } ? [reverseEdge] : []);
        var llmClient = new EdgeTestLlm(new() { ["duplicate_facts"] = new List<object?> { 0L }, ["contradicted_facts"] = new List<object?>() });
        var extracted = Edge("source_uuid", "target_uuid", "likes", "User likes yoga", []);
        var episode = Episode("episode_uuid", "Episode", "desc", "Episode content");
        List<EntityNode> nodes = [Node("source_uuid", "User", ["Entity"]), Node("target_uuid", "Yoga", ["Entity"])];

        var (resolvedEdges, invalidatedEdges, newEdges) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgesAsync(
            clients: Clients(llmClient, driver: driver),
            extractedEdges: [extracted],
            episode: episode,
            entities: nodes,
            edgeTypes: new(),
            edgeTypeMap: new(),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(reverseEdge.Uuid, resolvedEdges[0].Uuid);
        Assert.Contains(episode.Uuid, resolvedEdges[0].Episodes);
        Assert.Empty(invalidatedEdges);
        Assert.Empty(newEdges);
    }

    [Fact]
    public void test_same_pair_edges_keeps_both_orientations_drops_strays()
    {
        var extracted = CandidateEdge(0, "fact", null);
        extracted.SourceNodeUuid = "a";
        extracted.TargetNodeUuid = "b";

        var forward = CandidateEdge(1, "forward", null);
        (forward.SourceNodeUuid, forward.TargetNodeUuid) = ("a", "b");
        var reverse = CandidateEdge(2, "reverse", null);
        (reverse.SourceNodeUuid, reverse.TargetNodeUuid) = ("b", "a");
        var selfLoop = CandidateEdge(3, "self loop", null);
        (selfLoop.SourceNodeUuid, selfLoop.TargetNodeUuid) = ("a", "a");
        var stray = CandidateEdge(4, "stray", null);
        (stray.SourceNodeUuid, stray.TargetNodeUuid) = ("a", "c");

        var result = EdgeOperations.SamePairEdges(extracted, [forward, reverse, selfLoop, stray]);

        Assert.Equal(["candidate_1", "candidate_2"], result.Select(e => e.Uuid));
    }

    [Fact]
    public void test_same_pair_edges_self_loop_pair()
    {
        var extracted = CandidateEdge(0, "fact", null);
        extracted.SourceNodeUuid = "a";
        extracted.TargetNodeUuid = "a";
        var selfLoop = CandidateEdge(1, "self loop", null);
        (selfLoop.SourceNodeUuid, selfLoop.TargetNodeUuid) = ("a", "a");
        var other = CandidateEdge(2, "other", null);
        (other.SourceNodeUuid, other.TargetNodeUuid) = ("a", "b");

        var result = EdgeOperations.SamePairEdges(extracted, [selfLoop, other]);

        Assert.Equal(["candidate_1"], result.Select(e => e.Uuid));
    }

    [Fact]
    public void test_pair_search_filter_sets_both_endpoint_lists()
    {
        var searchFilter = EdgeOperations.PairSearchFilter(MockExtractedEdge());

        Assert.Equal(["source_uuid", "target_uuid"], searchFilter.EdgeSourceNodeUuids!);
        Assert.Equal(["source_uuid", "target_uuid"], searchFilter.EdgeTargetNodeUuids!);
        Assert.Null(searchFilter.EdgeUuids);
    }

    // The duplicate-candidate search must be endpoint-pre-filtered (never a
    // graph-wide retrieval); the invalidation search stays unfiltered.
    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edges_pair_scopes_duplicate_search()
    {
        var existing = CandidateEdge(0, "User likes yoga", null);
        existing.SourceNodeUuid = "source_uuid";
        existing.TargetNodeUuid = "target_uuid";

        var driver = new EdgeTestSearchDriver((_, _, filter) => filter.EdgeSourceNodeUuids is { Count: > 0 } ? [existing] : []);
        var extracted = Edge("source_uuid", "target_uuid", "likes", "  user LIKES yoga ", []); // normalizes to the existing fact
        var episode = Episode("episode_uuid", "Episode", "desc", "Episode content");
        List<EntityNode> nodes = [Node("source_uuid", "User", ["Entity"]), Node("target_uuid", "Yoga", ["Entity"])];

        var (resolvedEdges, invalidatedEdges, _) = await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgesAsync(
            clients: Clients(new EdgeTestLlm(), driver: driver),
            extractedEdges: [extracted],
            episode: episode,
            entities: nodes,
            edgeTypes: new(),
            edgeTypeMap: new(),
            cancellationToken: TestContext.Current.CancellationToken));

        // Exactly two searches: the pair-scoped duplicate search, then the
        // graph-wide invalidation search.
        Assert.Equal(2, driver.Searches.Count);
        var (pairFilter, invalidationFilter) = (driver.Searches[0].Filter, driver.Searches[1].Filter);
        Assert.Equal(["source_uuid", "target_uuid"], pairFilter.EdgeSourceNodeUuids!);
        Assert.Equal(["source_uuid", "target_uuid"], pairFilter.EdgeTargetNodeUuids!);
        Assert.Null(invalidationFilter.EdgeSourceNodeUuids);
        Assert.Null(invalidationFilter.EdgeUuids);

        // The pair edge resolved via the exact-fact fast path.
        Assert.Equal(existing.Uuid, resolvedEdges[0].Uuid);
        Assert.Empty(invalidatedEdges);
    }

    // Both per-edge searches must receive the precomputed fact embedding as
    // query_vector, so the search pipeline never re-embeds the same text.
    //
    // PORT: the Python test patches create_entity_edge_embeddings with a mock that does nothing, so the precomputed
    // embedding is the one of the edge. Here the real function runs, and the fake embedder gives the sentinel.
    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edges_reuses_fact_embedding_for_searches()
    {
        List<double> sentinelEmbedding = [0.1, 0.2, 0.3];
        var driver = new EdgeTestSearchDriver();
        var extracted = Edge("source_uuid", "target_uuid", "likes", "User likes yoga", []);
        extracted.FactEmbedding = sentinelEmbedding;
        var episode = Episode("episode_uuid", "Episode", "desc", "Episode content");
        List<EntityNode> nodes = [Node("source_uuid", "User", ["Entity"]), Node("target_uuid", "Yoga", ["Entity"])];
        var llmClient = new EdgeTestLlm(NoDuplicates());

        await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgesAsync(
            clients: Clients(llmClient, new EdgeTestEmbedder(_ => [.. sentinelEmbedding]), driver),
            extractedEdges: [extracted],
            episode: episode,
            entities: nodes,
            edgeTypes: new(),
            edgeTypeMap: new(),
            cancellationToken: TestContext.Current.CancellationToken));

        // Duplicate search + invalidation search, both with the precomputed vector.
        Assert.Equal(2, driver.Vectors.Count);
        Assert.All(driver.Vectors, v => Assert.Equal(sentinelEmbedding, v));
    }

    // PORT: the Python test patches `_extract_edge_timestamps` and asserts that it was awaited once with the model client,
    // the edge, and the episode. The real function runs, and its one call of the fake model client is the call of the
    // timestamp prompt, with the fact of the edge and the time of the episode (see the header). The assertion of the
    // arguments of the patched function has no other form.
    [Fact(Timeout = 5000)]
    public async Task test_resolve_extracted_edge_retries_timestamps_for_untagged_pairwise()
    {
        var llmClient = LlmWithoutTimestamps(new());
        var extractedEdge = Edge("source_uuid", "target_uuid", "KNOWS", "Alice knows Bob.", ["episode_1"]);
        var episode = Episode("episode_uuid", "Episode", "desc", "Alice knows Bob.");

        await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgeAsync(
            llmClient,
            extractedEdge,
            [],
            [],
            episode,
            cancellationToken: TestContext.Current.CancellationToken));

        var call = Assert.Single(llmClient.Calls);
        Assert.Equal("extract_edges.extract_timestamps", call.PromptName);
        var promptText = string.Join("\n", call.Messages.Select(m => m.Content));
        Assert.Contains("Alice knows Bob.", promptText, StringComparison.Ordinal);
        Assert.Contains(episode.ValidAt.IsoFormat(), promptText, StringComparison.Ordinal);
    }

    // PORT: the Python test patches the prompt function `extract_timestamps` and `apply_extracted_timestamps`, and asserts
    // their arguments. The real functions run. The prompt that the fake model client gets holds the reference time of the
    // edge, and the real `apply_extracted_timestamps` gives the date of the reply to the edge.
    [Fact(Timeout = 5000)]
    public async Task test_extract_edge_timestamps_prefers_edge_reference_time()
    {
        var edgeReferenceTime = PyDateTime.FromIsoFormat("2024-01-01T00:00:00+00:00");
        var episodeReferenceTime = PyDateTime.FromIsoFormat("2024-02-01T00:00:00+00:00");
        var edge = Edge("source_uuid", "target_uuid", "KNOWS", "Alice knows Bob.", ["episode_1"]);
        edge.ReferenceTime = edgeReferenceTime;
        var episode = Episode("episode_uuid", "Episode", "desc", edge.Fact);
        episode.ValidAt = episodeReferenceTime;
        var llmClient = new EdgeTestLlm(new() { ["valid_at"] = "2024-01-02T00:00:00Z", ["invalid_at"] = null });

        await LoopGuard.RunAsync(() => EdgeOperations.ExtractEdgeTimestampsAsync(
            llmClient, edge, episode, cancellationToken: TestContext.Current.CancellationToken));

        var call = Assert.Single(llmClient.Calls);
        Assert.Equal("extract_edges.extract_timestamps", call.PromptName);
        var promptText = string.Join("\n", call.Messages.Select(m => m.Content));
        Assert.Contains(edge.Fact, promptText, StringComparison.Ordinal);
        Assert.Contains(edgeReferenceTime.IsoFormat(), promptText, StringComparison.Ordinal);
        Assert.DoesNotContain(episodeReferenceTime.IsoFormat(), promptText, StringComparison.Ordinal);
        Assert.Equal(PyDateTime.FromIsoFormat("2024-01-02T00:00:00+00:00"), edge.ValidAt);
        Assert.Null(edge.InvalidAt);
    }

    private static PyDateTime DaysAgo(int days) => PyDateTime.FromDateTimeOffset(TimeProvider.System.GetUtcNow().AddDays(-days));

    private static PyDateTime NowUtc() => PyDateTime.UtcNow(TimeProvider.System);

    // A fake model client that answers each prompt with `returnValue` and the timestamp prompt with no date.
    private static EdgeTestLlm LlmWithoutTimestamps(OrderedDictionary<string, object?> returnValue)
    {
        var llm = new EdgeTestLlm(returnValue);
        var other = llm.Respond;
        llm.Respond = (messages, promptName) => promptName == "extract_edges.extract_timestamps"
            ? new OrderedDictionary<string, object?> { ["valid_at"] = null, ["invalid_at"] = null }
            : other(messages, promptName);
        return llm;
    }

    private static OrderedDictionary<string, object?> NoDuplicates() => new()
    {
        ["duplicate_facts"] = new List<object?>(),
        ["contradicted_facts"] = new List<object?>(),
    };

    private static GraphitiClients Clients(EdgeTestLlm llm, EdgeTestEmbedder? embedder = null, EdgeTestSearchDriver? driver = null) => new()
    {
        Driver = driver ?? new EdgeTestSearchDriver(),
        LlmClient = llm,
        Embedder = embedder ?? new EdgeTestEmbedder(),
        CrossEncoder = new EdgeOperationsRun.FakeCrossEncoder(),
        Tracer = new NoOpTracer(),
    };

    private static EntityEdge Edge(string source, string target, string name, string fact, List<string> episodes, string? uuid = null, PyDateTime? createdAt = null)
    {
        var edge = new EntityEdge
        {
            SourceNodeUuid = source,
            TargetNodeUuid = target,
            Name = name,
            GroupId = "group_1",
            Fact = fact,
            Episodes = episodes,
            CreatedAt = createdAt ?? NowUtc(),
        };
        if (uuid is not null)
        {
            edge.Uuid = uuid;
        }

        return edge.Validate();
    }

    private static EntityNode Node(string uuid, string name, List<string> labels) =>
        new EntityNode { Uuid = uuid, Name = name, GroupId = "group_1", Labels = labels }.Validate();

    private static EpisodicNode Episode(string uuid, string name, string sourceDescription, string content) => new EpisodicNode
    {
        Uuid = uuid,
        Name = name,
        GroupId = "group_1",
        Source = EpisodeType.Message,
        SourceDescription = sourceDescription,
        Content = content,
        ValidAt = NowUtc(),
    }.Validate();

    private static EntityEdge MockExtractedEdge() => Edge("source_uuid", "target_uuid", "test_edge", "Test fact", ["episode_1"]);

    private static List<EntityEdge> MockExistingEdges()
    {
        var edge = Edge("source_uuid_3", "target_uuid_3", "existing_edge", "Existing fact", ["episode_3"], createdAt: DaysAgo(2));
        edge.ValidAt = DaysAgo(2);
        return [edge];
    }

    private static EpisodicNode MockCurrentEpisode() => Episode("episode_1", "Current Episode", "Test source description", "Current episode content");

    private static EntityNode SignatureNode(string uuid, string label) => Node(uuid, uuid, [label]);

    private static EntityEdge SignatureEdge(string name, EntityNode source, EntityNode target)
    {
        var edge = Edge(source.Uuid, target.Uuid, name, $"{source.Name} {name} {target.Name}", []);
        edge.Attributes = new() { ["note"] = "x" };
        return edge;
    }

    private static (List<EntityEdge>, List<OrderedDictionary<string, GraphTypeDescriptor>>) Enforce(List<EntityEdge> edges, List<EntityNode> nodes, bool strict)
    {
        var uuidEntityMap = nodes.ToDictionary(node => node.Uuid);
        List<OrderedDictionary<string, GraphTypeDescriptor>> edgeTypesLst =
        [
            .. edges.Select(edge => EdgeOperations.EdgeTypesForEndpoints(edge, uuidEntityMap, SignatureEdgeTypes, SignatureEdgeTypeMap)),
        ];
        return EdgeOperations.EnforceEdgeTypeSignatures(edges, edgeTypesLst, SignatureEdgeTypeMap, strict);
    }

    private static EntityEdge CandidateEdge(int idx, string fact, List<double>? embedding)
    {
        var edge = Edge("source_uuid", "target_uuid", "relates_to", fact, [], uuid: $"candidate_{idx}");
        edge.FactEmbedding = embedding;
        return edge;
    }

    private static OrderedDictionary<string, List<(string, string)>> SignaturesMap(OrderedDictionary<(string, string), List<string>> edgeTypeMap)
    {
        var edgeTypeSignaturesMap = new OrderedDictionary<string, List<(string, string)>>();
        foreach (var (signature, edgeTypeNames) in edgeTypeMap)
        {
            foreach (var edgeType in edgeTypeNames)
            {
                if (!edgeTypeSignaturesMap.TryGetValue(edgeType, out var signatures))
                {
                    signatures = [];
                    edgeTypeSignaturesMap[edgeType] = signatures;
                }

                signatures.Add(signature);
            }
        }

        return edgeTypeSignaturesMap;
    }

    private static List<OrderedDictionary<string, object?>> TypesContext(
        OrderedDictionary<string, GraphTypeDescriptor> edgeTypes,
        OrderedDictionary<string, List<(string, string)>> edgeTypeSignaturesMap) =>
    [
        .. edgeTypes.Select(t => new OrderedDictionary<string, object?>
        {
            ["fact_type_name"] = t.Key,
            ["fact_type_signatures"] = edgeTypeSignaturesMap.GetValueOrDefault(t.Key, [("Entity", "Entity")]),
            ["fact_type_description"] = t.Value.Doc,
        }),
    ];

    private static Graphiti.Core.Prompts.Edge ExtractedEdge(string source, string target, string relation, string fact) => new Graphiti.Core.Prompts.Edge
    {
        SourceEntityName = source,
        TargetEntityName = target,
        RelationType = relation,
        Fact = fact,
        ValidAt = null,
        InvalidAt = null,
    }.Validate();

    private static OrderedDictionary<string, object?> ExtractedEdgesReply(params Graphiti.Core.Prompts.Edge[] edges) =>
        new ExtractedEdges { Edges = [.. edges] }.Validate().ModelDump();
}
