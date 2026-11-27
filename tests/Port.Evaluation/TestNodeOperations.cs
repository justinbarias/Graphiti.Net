// Ported from tests/utils/maintenance/test_node_operations.py (port/RULES.md, R25). The file tests node_operations.py
// and dedup_helpers.py, and both are ported, so each test is here.
//
// The Python tests replace `_semantic_candidate_search`, `_collect_exact_name_candidate_nodes`, `EntityNode.get_by_names`,
// and `node_similarity_search` with monkeypatch.setattr, which has no C# form (O10). Each of these functions reads an
// object of the driver first: node_similarity_search reads driver.search_interface, and EntityNode.get_by_names reads
// driver.graph_operations_interface. The test puts the replacement on that object and runs the real function, which
// Part 3 of the rulebook allows. A search interface that returns the patched candidates gives the same answer as the
// patched `_semantic_candidate_search`, and the driver gives no exact-name candidate unless the test asks for one.
// The tests of `_resolve_with_llm` replace `prompt_library.dedupe_nodes.nodes` with a function that keeps the context.
// The port reads the prompt from the static library, so these tests read the same values in the rendered prompt.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.NodeOperationsTestSupport;

public class TestNodeOperations
{
    // (clients, llm_generate) of _make_clients(): a driver with no graph operations interface whose execute_query
    // returns no record, and an LLM client with max_tokens = 32768.
    private static (GraphitiClients Clients, MockLlm Llm) MakeClients(List<List<EntityNode>>? semanticCandidates = null, MockOps? ops = null)
    {
        var llm = new MockLlm();
        var driver = new MockGraphDriver(search: semanticCandidates is null ? null : new MockSearch(semanticCandidates), ops: ops);
        return (NodeOperationsTestSupport.MakeClients(llm, driver: driver), llm);
    }

    private static OrderedDictionary<string, object?> Resolution(long id, string name, long duplicateCandidateId) =>
        Item(("id", id), ("name", name), ("duplicate_candidate_id", duplicateCandidateId));

    [Fact(Timeout = 5000)]
    public async Task test_semantic_candidate_search_skips_blank_names()
    {
        var hit = Node("Hit");
        var (clients, _) = MakeClients([[hit]]);
        var embedder = (MockEmbedder)clients.Embedder;
        List<EntityNode> nodes = [Node("Alice"), Node(string.Empty), Node("Bob")];

        var result = await LoopGuard.RunAsync(() => NodeOperations.SemanticCandidateSearchAsync(clients, nodes, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(3, result.Count);
        Assert.Empty(result[1]);
        Assert.Equal(["Alice", "Bob"], embedder.BatchInputs);
    }

    [Fact(Timeout = 5000)]
    public async Task test_semantic_candidate_search_raises_on_vector_count_mismatch()
    {
        var (clients, _) = MakeClients([[]]);
        ((MockEmbedder)clients.Embedder).Batch = _ => [[0.1, 0.2, 0.3]];
        List<EntityNode> nodes = [Node("Alice"), Node("Bob")];

        var error = await Assert.ThrowsAsync<ArgumentException>(() => LoopGuard.RunAsync(() => NodeOperations.SemanticCandidateSearchAsync(clients, nodes, cancellationToken: TestContext.Current.CancellationToken)));
        Assert.Contains("vectors", error.Message, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_nodes_exact_match_skips_llm()
    {
        var candidate = Node("Joe Michaels");
        var extracted = Node("Joe Michaels");
        var (clients, llm) = MakeClients([[candidate]]);

        var (resolved, uuidMap, _) = await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(clients, [extracted], MakeEpisode(), [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(candidate.Uuid, resolved[0].Uuid);
        Assert.Equal(candidate.Uuid, uuidMap[extracted.Uuid]);
        Assert.Empty(llm.Calls);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_nodes_exact_match_promotes_generic_candidate_type()
    {
        var candidate = Node("Audrey");
        var extracted = Node("Audrey", labels: ["Entity", "Person"]);
        var (clients, llm) = MakeClients([[candidate]]);

        var (resolved, uuidMap, _) = await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(clients, [extracted], MakeEpisode(), [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(candidate.Uuid, resolved[0].Uuid);
        Assert.Equal(new HashSet<string> { "Entity", "Person" }, resolved[0].Labels.ToHashSet());
        Assert.Equal(new HashSet<string> { "Entity", "Person" }, candidate.Labels.ToHashSet());
        Assert.Equal(candidate.Uuid, uuidMap[extracted.Uuid]);
        Assert.Empty(llm.Calls);
    }

    [Fact]
    public void test_identity_type_for_node_uses_label_with_identity_properties()
    {
        var node = Node("Acme", labels: ["Entity", "Party", "Company"]);
        var identityProperties = new OrderedDictionary<string, List<string>> { ["Party"] = ["party_id"], ["Company"] = ["company_id"] };

        Assert.Equal("Company", DedupHelpers.IdentityTypeForNode(node, identityProperties));
    }

    [Fact]
    public void test_identity_type_for_node_uses_matching_label_when_other_labels_do_not_match()
    {
        var node = Node("Acme", labels: ["Entity", "Party", "Company"]);

        Assert.Equal("Party", DedupHelpers.IdentityTypeForNode(node, new OrderedDictionary<string, List<string>> { ["Party"] = ["party_id"] }));
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_nodes_identity_properties_match_before_llm()
    {
        var candidate = Node("ACME", labels: ["Entity", "Company"], attributes: new() { ["domain"] = "acme.example", ["size"] = "enterprise" });
        var ops = new MockOps { GetByAttributeValues = (_, _, _, _) => [candidate] };
        var extracted = Node("ACME Incorporated", labels: ["Entity", "Company"], attributes: new() { ["domain"] = "acme.example", ["size"] = null, ["region"] = "us-west" });
        var (clients, llm) = MakeClients([[]], ops);

        var (resolved, uuidMap, duplicates) = await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(
            clients,
            [extracted],
            MakeEpisode(),
            [],
            identityProperties: new OrderedDictionary<string, List<string>> { ["Company"] = ["domain"] },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(candidate.Uuid, resolved[0].Uuid);
        Assert.Equal(
            PyJson.Dumps(new OrderedDictionary<string, object?> { ["domain"] = "acme.example", ["size"] = "enterprise", ["region"] = "us-west" }),
            PyJson.Dumps(resolved[0].Attributes));
        Assert.Equal(candidate.Uuid, uuidMap[extracted.Uuid]);
        var pair = Assert.Single(duplicates);
        Assert.Same(extracted, pair.Item1);
        Assert.Same(candidate, pair.Item2);
        Assert.Empty(llm.Calls);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_nodes_low_entropy_uses_llm()
    {
        var candidate = Node("Joseph");
        var extracted = Node("Joe");
        var (clients, llm) = MakeClients([[candidate]]);
        llm.ReturnValue = Reply("entity_resolutions", Resolution(0, "Joe", -1));

        var (resolved, uuidMap, _) = await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(clients, [extracted], MakeEpisode(), [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(extracted.Uuid, resolved[0].Uuid);
        Assert.Equal(extracted.Uuid, uuidMap[extracted.Uuid]);
        Assert.NotEmpty(llm.Calls);
    }

    /// <summary>Short names with a unique exact candidate match should resolve deterministically.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_resolve_nodes_short_name_exact_match_skips_llm()
    {
        var candidate = Node("Java");
        var extracted = Node("Java");
        var (clients, llm) = MakeClients([[candidate]]);

        var (resolved, uuidMap, _) = await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(clients, [extracted], MakeEpisode(), [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(candidate.Uuid, resolved[0].Uuid);
        Assert.Equal(candidate.Uuid, uuidMap[extracted.Uuid]);
        Assert.Empty(llm.Calls);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_nodes_fuzzy_match()
    {
        var candidate = Node("Joe-Michaels");
        var extracted = Node("Joe Michaels");
        var (clients, llm) = MakeClients([[candidate]]);

        var (resolved, uuidMap, _) = await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(clients, [extracted], MakeEpisode(), [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(candidate.Uuid, resolved[0].Uuid);
        Assert.Equal(candidate.Uuid, uuidMap[extracted.Uuid]);
        Assert.Empty(llm.Calls);
    }

    [Fact(Timeout = 5000)]
    public async Task test_collect_candidate_nodes_prepends_exact_name_candidates()
    {
        var exactCandidate = Node("Alice");
        var semanticCandidate = Node("Alice Smith");
        var extracted = Node("Alice");
        var ops = new MockOps { GetByNames = (_, _) => [exactCandidate] };
        var (clients, _) = MakeClients([[semanticCandidate]], ops);
        var search = (MockSearch)clients.Driver.SearchInterface!;

        var result = await LoopGuard.RunAsync(() => NodeOperations.CollectCandidateNodesAsync(clients, [extracted], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Single(result);
        Assert.Equal([exactCandidate.Uuid, semanticCandidate.Uuid], result[0].Select(node => node.Uuid));
        Assert.Equal(1, ops.NameCalls);
        Assert.Equal(1, search.Calls);
    }

    [Fact(Timeout = 5000)]
    public async Task test_collect_candidate_nodes_dedupes_and_merges_override()
    {
        var candidate = Node("Alice");
        var overrideDuplicate = Node("Alice Alt");
        overrideDuplicate.Uuid = candidate.Uuid;
        var extracted = Node("Alice");
        var (clients, _) = MakeClients([[candidate]]);
        var search = (MockSearch)clients.Driver.SearchInterface!;

        var result = await LoopGuard.RunAsync(() => NodeOperations.CollectCandidateNodesAsync(clients, [extracted], existingNodesOverride: [overrideDuplicate], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Single(result);
        Assert.Single(result[0]);
        Assert.Equal(candidate.Uuid, result[0][0].Uuid);
        Assert.True(search.Calls > 0);
    }

    [Fact(Timeout = 5000)]
    public async Task test_collect_exact_name_candidate_nodes_scopes_by_group()
    {
        var groupACandidate = Node("Alice", groupId: "group-a");
        var groupBCandidate = Node("Alice", groupId: "group-b");
        List<EntityNode> extractedNodes = [Node("Alice", groupId: "group-a"), Node("Alice", groupId: "group-b")];
        var ops = new MockOps { GetByNames = (_, _) => [groupACandidate, groupBCandidate] };
        var (clients, _) = MakeClients(ops: ops);

        var result = await LoopGuard.RunAsync(() => NodeOperations.CollectExactNameCandidateNodesAsync(clients, extractedNodes, TestContext.Current.CancellationToken));

        Assert.Equal(2, result.Count);
        Assert.Same(groupACandidate, Assert.Single(result[0]));
        Assert.Same(groupBCandidate, Assert.Single(result[1]));
        Assert.Equal(1, ops.NameCalls);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_nodes_exact_name_lookup_handles_semantic_miss()
    {
        var candidate = Node("Audrey");
        var extracted = Node("Audrey", labels: ["Entity", "Person"]);
        var ops = new MockOps { GetByNames = (_, _) => [candidate] };
        var (clients, llm) = MakeClients([[]], ops);

        var (resolved, uuidMap, _) = await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(clients, [extracted], MakeEpisode(), [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(candidate.Uuid, resolved[0].Uuid);
        Assert.Equal(new HashSet<string> { "Entity", "Person" }, candidate.Labels.ToHashSet());
        Assert.Equal(candidate.Uuid, uuidMap[extracted.Uuid]);
        Assert.Empty(llm.Calls);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_nodes_semantic_miss_keeps_node_without_llm()
    {
        var extracted = Node("Completely New Thing");
        var (clients, llm) = MakeClients([[]]);

        var (resolved, uuidMap, duplicates) = await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(clients, [extracted], MakeEpisode(), [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(extracted.Uuid, resolved[0].Uuid);
        Assert.Equal(extracted.Uuid, uuidMap[extracted.Uuid]);
        Assert.Empty(duplicates);
        Assert.Empty(llm.Calls);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_nodes_multiple_exact_matches_use_llm()
    {
        var candidate1 = Node("Java");
        var candidate2 = Node("Java");
        var extracted = Node("Java");
        var (clients, llm) = MakeClients([[candidate1, candidate2]]);
        llm.ReturnValue = Reply("entity_resolutions", Resolution(0, "Java", 0));

        var (resolved, uuidMap, _) = await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(clients, [extracted], MakeEpisode(), [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(candidate1.Uuid, resolved[0].Uuid);
        Assert.Equal(candidate1.Uuid, uuidMap[extracted.Uuid]);
        Assert.NotEmpty(llm.Calls);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_nodes_batches_unresolved_nodes_into_one_llm_call()
    {
        var lowEntropyCandidate = Node("Joseph");
        var javaCandidate1 = Node("Java");
        var javaCandidate2 = Node("Java");
        List<EntityNode> extractedNodes = [Node("Joe"), Node("Java")];
        var (clients, llm) = MakeClients([[lowEntropyCandidate], [javaCandidate1, javaCandidate2]]);
        llm.ReturnValue = Reply("entity_resolutions", Resolution(0, "Joe", -1), Resolution(1, "Java", 1));

        var (resolved, uuidMap, _) = await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(clients, extractedNodes, MakeEpisode(), [], cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(extractedNodes[0].Uuid, resolved[0].Uuid);
        Assert.Equal(javaCandidate1.Uuid, resolved[1].Uuid);
        Assert.Equal(extractedNodes[0].Uuid, uuidMap[extractedNodes[0].Uuid]);
        Assert.Equal(javaCandidate1.Uuid, uuidMap[extractedNodes[1].Uuid]);
        Assert.Single(llm.Calls);
    }

    [Fact]
    public void test_build_candidate_indexes_populates_structures()
    {
        var candidate = Node("Bob Dylan");

        var indexes = DedupHelpers.BuildCandidateIndexes([candidate]);

        var normalizedKey = PyText.Lower(candidate.Name);
        Assert.Equal(candidate.Uuid, indexes.NormalizedExisting[normalizedKey][0].Uuid);
        Assert.Same(candidate, indexes.NodesByUuid[candidate.Uuid]);
        Assert.True(indexes.ShinglesByCandidate.ContainsKey(candidate.Uuid));
        Assert.Contains(indexes.LshBuckets.Values, bucket => bucket.Contains(candidate.Uuid));
    }

    [Fact]
    public void test_normalize_helpers()
    {
        Assert.Equal("alice smith", DedupHelpers.NormalizeStringExact("  Alice   Smith "));
        Assert.Equal("alice smith", DedupHelpers.NormalizeNameForFuzzy("Alice-Smith!"));
    }

    [Fact]
    public void test_name_entropy_variants()
    {
        Assert.True(DedupHelpers.NameEntropy("alice") > DedupHelpers.NameEntropy("aaaaa"));
        Assert.Equal(0.0, DedupHelpers.NameEntropy(string.Empty));
    }

    [Fact]
    public void test_has_high_entropy_rules()
    {
        Assert.True(DedupHelpers.HasHighEntropy("meaningful name"));
        Assert.False(DedupHelpers.HasHighEntropy("aa"));
    }

    [Fact]
    public void test_shingles_and_cache()
    {
        var raw = "alice";
        var shingleSet = DedupHelpers.Shingles(raw);
        Assert.True(shingleSet.SetEquals(["ali", "lic", "ice"]));
        Assert.True(DedupHelpers.CachedShingles(raw).SetEquals(shingleSet));
        Assert.Same(DedupHelpers.CachedShingles(raw), DedupHelpers.CachedShingles(raw));
    }

    [Fact]
    public void test_hash_minhash_and_lsh()
    {
        HashSet<string> shingles = ["abc", "bcd", "cde"];
        var signature = DedupHelpers.MinhashSignature(shingles);
        Assert.Equal(32, signature.Count);
        var bands = DedupHelpers.LshBands(signature);
        Assert.All(bands, band => Assert.Equal(4, band.Count));
        var hashed = shingles.Select(s => DedupHelpers.HashShingle(s, 0)).ToHashSet();
        Assert.Equal(shingles.Count, hashed.Count);
    }

    [Fact]
    public void test_jaccard_similarity_edges()
    {
        HashSet<string> a = ["a", "b"];
        HashSet<string> b = ["a", "c"];
        Assert.Equal(1.0 / 3, DedupHelpers.JaccardSimilarity(a, b), 1e-6);
        Assert.Equal(1.0, DedupHelpers.JaccardSimilarity([], []));
        Assert.Equal(0.0, DedupHelpers.JaccardSimilarity(a, []));
    }

    private static DedupResolutionState State(params long[] unresolved) =>
        new() { ResolvedNodes = [null], UuidMap = [], UnresolvedIndices = [.. unresolved] };

    [Fact]
    public void test_resolve_with_similarity_exact_match_updates_state()
    {
        var candidate = Node("Charlie Parker");
        var extracted = Node("Charlie Parker");

        var indexes = DedupHelpers.BuildCandidateIndexes([candidate]);
        var state = State();

        DedupHelpers.ResolveWithSimilarity([extracted], indexes, state);

        Assert.Equal(candidate.Uuid, state.ResolvedNodes[0]?.Uuid);
        Assert.Equal(candidate.Uuid, state.UuidMap[extracted.Uuid]);
        Assert.Empty(state.UnresolvedIndices);
        Assert.Equal([(extracted, candidate)], state.DuplicatePairs);
    }

    /// <summary>Short names like 'Nate' should resolve via exact match without hitting the LLM.</summary>
    [Fact]
    public void test_resolve_with_similarity_short_name_exact_match_resolves_deterministically()
    {
        var candidate = Node("Nate", labels: ["Entity", "Person"]);
        var extracted = Node("Nate", labels: ["Entity", "Person"]);

        var indexes = DedupHelpers.BuildCandidateIndexes([candidate]);
        var state = State();

        DedupHelpers.ResolveWithSimilarity([extracted], indexes, state);

        Assert.Equal(candidate.Uuid, state.ResolvedNodes[0]?.Uuid);
        Assert.Equal(candidate.Uuid, state.UuidMap[extracted.Uuid]);
        Assert.Empty(state.UnresolvedIndices);
        Assert.Equal([(extracted, candidate)], state.DuplicatePairs);
    }

    /// <summary>Short names with no exact match should still reach the LLM for resolution.</summary>
    [Fact]
    public void test_resolve_with_similarity_short_name_no_candidate_defers_to_llm()
    {
        var extracted = Node("Nate", labels: ["Entity", "Person"]);

        var indexes = DedupHelpers.BuildCandidateIndexes([]);
        var state = State();

        DedupHelpers.ResolveWithSimilarity([extracted], indexes, state);

        Assert.Null(state.ResolvedNodes[0]);
        Assert.Empty(state.UuidMap);
        Assert.Equal([0L], state.UnresolvedIndices);
        Assert.Empty(state.DuplicatePairs);
    }

    /// <summary>Short names with multiple exact matches should escalate to LLM.</summary>
    [Fact]
    public void test_resolve_with_similarity_short_name_multiple_candidates_defers_to_llm()
    {
        var candidate1 = Node("Java");
        var candidate2 = Node("Java");
        var extracted = Node("Java");

        var indexes = DedupHelpers.BuildCandidateIndexes([candidate1, candidate2]);
        var state = State();

        DedupHelpers.ResolveWithSimilarity([extracted], indexes, state);

        Assert.Null(state.ResolvedNodes[0]);
        Assert.Empty(state.UuidMap);
        Assert.Equal([0L], state.UnresolvedIndices);
        Assert.Empty(state.DuplicatePairs);
    }

    [Fact]
    public void test_resolve_with_similarity_low_entropy_defers_resolution()
    {
        var extracted = Node("Bob");
        var indexes = new DedupCandidateIndexes
        {
            ExistingNodes = [],
            NodesByUuid = [],
            NormalizedExisting = [],
            ShinglesByCandidate = [],
            LshBuckets = [],
        };
        var state = State();

        DedupHelpers.ResolveWithSimilarity([extracted], indexes, state);

        Assert.Null(state.ResolvedNodes[0]);
        Assert.Equal([0L], state.UnresolvedIndices);
        Assert.Empty(state.DuplicatePairs);
    }

    [Fact]
    public void test_resolve_with_similarity_multiple_exact_matches_defers_to_llm()
    {
        var candidate1 = Node("Johnny Appleseed");
        var candidate2 = Node("Johnny Appleseed");
        var extracted = Node("Johnny Appleseed");

        var indexes = DedupHelpers.BuildCandidateIndexes([candidate1, candidate2]);
        var state = State();

        DedupHelpers.ResolveWithSimilarity([extracted], indexes, state);

        Assert.Null(state.ResolvedNodes[0]);
        Assert.Equal([0L], state.UnresolvedIndices);
        Assert.Empty(state.DuplicatePairs);
    }

    private static MockLlm DedupeLlm(long maxTokens, params OrderedDictionary<string, object?>[] resolutions) =>
        new(maxTokens) { ReturnValue = Reply("entity_resolutions", resolutions) };

    /// <summary>Ensure candidate.attributes with a 'candidate_id' key cannot corrupt the LLM context.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_resolve_with_llm_candidate_attributes_cannot_overwrite_candidate_id()
    {
        var candidate = Node("Dizzy Gillespie");
        candidate.Attributes = new() { ["candidate_id"] = 999L, ["genre"] = "jazz" };
        var extracted = Node("Dizzy");
        var indexes = DedupHelpers.BuildCandidateIndexes([candidate]);
        var state = State(0);
        var llm = DedupeLlm(32768, Resolution(0, "Dizzy Gillespie", 0));

        await LoopGuard.RunAsync(() => NodeOperations.ResolveWithLlmAsync(llm, [extracted], indexes, state, MakeEpisode(), [], null, cancellationToken: TestContext.Current.CancellationToken));

        // candidate_id must be the positional index (0), not the adversarial attribute (999)
        // PORT: the context of the prompt is read in the rendered prompt (see the file header).
        var prompt = string.Concat(Assert.Single(llm.Calls).Messages.Select(m => m.Content));
        Assert.Contains("\"candidate_id\": 0", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("\"candidate_id\": 999", prompt, StringComparison.Ordinal);

        // non-colliding attributes should still be present
        Assert.Contains("\"genre\": \"jazz\"", prompt, StringComparison.Ordinal);
        Assert.Equal(candidate.Uuid, state.ResolvedNodes[0]?.Uuid);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_with_llm_updates_unresolved()
    {
        var extracted = Node("Dizzy");
        var candidate = Node("Dizzy Gillespie");
        var indexes = DedupHelpers.BuildCandidateIndexes([candidate]);
        var state = State(0);
        var llm = DedupeLlm(32768, Resolution(0, "Dizzy Gillespie", 0));

        await LoopGuard.RunAsync(() => NodeOperations.ResolveWithLlmAsync(llm, [extracted], indexes, state, MakeEpisode(), [], null, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(candidate.Uuid, state.ResolvedNodes[0]?.Uuid);
        Assert.Equal(candidate.Uuid, state.UuidMap[extracted.Uuid]);

        // PORT: the context of the prompt is read in the rendered prompt (see the file header).
        var prompt = string.Concat(Assert.Single(llm.Calls).Messages.Select(m => m.Content));
        Assert.Contains("\"candidate_id\": 0", prompt, StringComparison.Ordinal);
        Assert.Contains("\"entity_type_description\": \"Default Entity Type\"", prompt, StringComparison.Ordinal);
        Assert.Equal([(extracted, candidate)], state.DuplicatePairs);
    }

    [Theory(Timeout = 5000)]
    [InlineData(32768L, 16384L)]
    [InlineData(4096L, 4096L)]
    [InlineData(0L, 16384L)]
    public async Task test_resolve_with_llm_caps_completion_budget(long clientMaxTokens, long expectedMaxTokens)
    {
        // PORT: `max_tokens = None` of the Python client is 0, because LLMClient.MaxTokens is a long. Both are false
        // for `or 16384`.
        var extracted = Node("Dizzy");
        var candidate = Node("Dizzy Gillespie");
        var indexes = DedupHelpers.BuildCandidateIndexes([candidate]);
        var state = State(0);
        var llm = DedupeLlm(clientMaxTokens, Resolution(0, "Dizzy Gillespie", 0));

        await LoopGuard.RunAsync(() => NodeOperations.ResolveWithLlmAsync(llm, [extracted], indexes, state, MakeEpisode(), [], null, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(expectedMaxTokens, Assert.Single(llm.Calls).MaxTokens);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_with_llm_promotes_generic_candidate_type()
    {
        var extracted = Node("Audrey", labels: ["Entity", "Person"]);
        var candidate = Node("Audrey");
        var indexes = DedupHelpers.BuildCandidateIndexes([candidate]);
        var state = State(0);
        var llm = DedupeLlm(32768, Resolution(0, "Audrey", 0));

        await LoopGuard.RunAsync(() => NodeOperations.ResolveWithLlmAsync(llm, [extracted], indexes, state, MakeEpisode(), [], null, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(candidate.Uuid, state.ResolvedNodes[0]?.Uuid);
        Assert.Equal(new HashSet<string> { "Entity", "Person" }, state.ResolvedNodes[0]?.Labels.ToHashSet());
        Assert.Equal(new HashSet<string> { "Entity", "Person" }, candidate.Labels.ToHashSet());
        Assert.Equal(candidate.Uuid, state.UuidMap[extracted.Uuid]);
        Assert.Equal([(extracted, candidate)], state.DuplicatePairs);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_with_llm_ignores_out_of_range_relative_ids()
    {
        var extracted = Node("Dexter");
        var indexes = DedupHelpers.BuildCandidateIndexes([]);
        var state = State(0);
        var llm = DedupeLlm(32768, Resolution(5, "Dexter", -1));
        var caplog = new MaintenanceLogCapture();

        using (PortServices.Use(loggerFactory: caplog))
        {
            await LoopGuard.RunAsync(() => NodeOperations.ResolveWithLlmAsync(llm, [extracted], indexes, state, MakeEpisode(), [], null, cancellationToken: TestContext.Current.CancellationToken));
        }

        Assert.Null(state.ResolvedNodes[0]);
        Assert.Contains(caplog.Lines, line => line.Level == "WARNING" && line.Message.Contains("Skipping invalid LLM dedupe id 5", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_with_llm_ignores_duplicate_relative_ids()
    {
        var extracted = Node("Dizzy");
        var candidate = Node("Dizzy Gillespie");
        var indexes = DedupHelpers.BuildCandidateIndexes([candidate]);
        var state = State(0);
        var llm = DedupeLlm(32768, Resolution(0, "Dizzy Gillespie", 0), Resolution(0, "Dizzy", -1));

        await LoopGuard.RunAsync(() => NodeOperations.ResolveWithLlmAsync(llm, [extracted], indexes, state, MakeEpisode(), [], null, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(candidate.Uuid, state.ResolvedNodes[0]?.Uuid);
        Assert.Equal(candidate.Uuid, state.UuidMap[extracted.Uuid]);
        Assert.Equal([(extracted, candidate)], state.DuplicatePairs);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_with_llm_invalid_candidate_id_defaults_to_extracted()
    {
        var extracted = Node("Dexter");
        var indexes = DedupHelpers.BuildCandidateIndexes([]);
        var state = State(0);
        var llm = DedupeLlm(32768, Resolution(0, "Dexter", 999));

        await LoopGuard.RunAsync(() => NodeOperations.ResolveWithLlmAsync(llm, [extracted], indexes, state, MakeEpisode(), [], null, cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(extracted.Equals(state.ResolvedNodes[0]));
        Assert.Equal(extracted.Uuid, state.UuidMap[extracted.Uuid]);
        Assert.Empty(state.DuplicatePairs);
    }

    private static Task ExtractSummariesAsync(MockLlm llm, List<EntityNode> nodes, OrderedDictionary<string, List<EntityEdge>>? edgesByNode = null, Func<EntityNode, Task<bool>>? shouldSummarizeNode = null, CancellationToken token = default) =>
        LoopGuard.RunAsync(() => NodeOperations.ExtractEntitySummariesBatchAsync(
            llm,
            nodes,
            new EpisodeOrEpisodes.One { Episode = MakeEpisode() },
            [],
            shouldSummarizeNode,
            edgesByNode ?? [],
            cancellationToken: token));

    /// <summary>Test that short summaries are kept as-is without LLM call (optimization).</summary>
    [Fact(Timeout = 5000)]
    public async Task test_batch_summaries_short_summary_no_llm()
    {
        var llm = new MockLlm { ReturnValue = Reply("summaries", Item(("name", "Test Node"), ("summary", "Generated summary"))) };
        var node = Node("Test Node", summary: "Old summary");

        await ExtractSummariesAsync(llm, [node], token: TestContext.Current.CancellationToken);

        // Short summary should be kept as-is without LLM call
        Assert.Equal("Old summary", node.Summary);

        // LLM should NOT have been called (summary is short enough)
        Assert.Empty(llm.Calls);
    }

    /// <summary>Test that summary is NOT regenerated when callback returns False.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_batch_summaries_callback_skip_summary()
    {
        var llm = new MockLlm { ReturnValue = Reply("summaries", Item(("name", "Test Node"), ("summary", "This should not be used"))) };
        var node = Node("Test Node", summary: "Old summary");

        // Callback that always returns False (skip summary generation)
        await ExtractSummariesAsync(llm, [node], shouldSummarizeNode: _ => Task.FromResult(false), token: TestContext.Current.CancellationToken);

        // Summary should remain unchanged
        Assert.Equal("Old summary", node.Summary);

        // LLM should NOT have been called for summary
        Assert.Empty(llm.Calls);
    }

    /// <summary>Test callback that selectively skips summaries based on node properties.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_batch_summaries_selective_callback()
    {
        var llm = new MockLlm { ReturnValue = Reply("summaries") };
        var userNode = Node("User", labels: ["Entity", "User"], summary: "Old");
        var topicNode = Node("Topic", labels: ["Entity", "Topic"], summary: "Old");

        // Callback that skips User nodes but generates for others
        await ExtractSummariesAsync(llm, [userNode, topicNode], shouldSummarizeNode: n => Task.FromResult(!n.Labels.Contains("User")), token: TestContext.Current.CancellationToken);

        // User summary should remain unchanged (callback returned False)
        Assert.Equal("Old", userNode.Summary);

        // Topic summary should also remain unchanged (short summary optimization)
        Assert.Equal("Old", topicNode.Summary);

        // LLM should NOT have been called (summaries are short enough)
        Assert.Empty(llm.Calls);
    }

    /// <summary>Test that callback is properly passed through extract_attributes_from_nodes.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_extract_attributes_from_nodes_with_callback()
    {
        var (clients, llm) = MakeClients();
        llm.ReturnValue = Reply("summaries");
        var node1 = Node("Node1", labels: ["Entity", "User"], summary: "Old1");
        var node2 = Node("Node2", labels: ["Entity", "Topic"], summary: "Old2");
        List<string> callTracker = [];

        // Callback that tracks which nodes it's called with
        Task<bool> TrackingFilter(EntityNode n)
        {
            callTracker.Add(n.Name);
            return Task.FromResult(!n.Labels.Contains("User"));
        }

        var results = await LoopGuard.RunAsync(() => NodeOperations.ExtractAttributesFromNodesAsync(
            clients,
            [node1, node2],
            new EpisodeOrEpisodes.One { Episode = MakeEpisode() },
            [],
            null,
            TrackingFilter,
            cancellationToken: TestContext.Current.CancellationToken));

        // Callback should have been called for both nodes
        Assert.Equal(2, callTracker.Count);
        Assert.Contains("Node1", callTracker);
        Assert.Contains("Node2", callTracker);

        // Both nodes should keep old summaries (short summary optimization skips LLM)
        Assert.Equal("Old1", results.First(n => n.Name == "Node1").Summary);
        Assert.Equal("Old2", results.First(n => n.Name == "Node2").Summary);
    }

    // TODO(port): test_extract_attributes_from_nodes_keeps_initial_nulls_from_response waits for
    // GraphTypeDescriptor.ModelValidate (lead, debt T18). extract_attributes_from_nodes validates the shape with
    // `entity_type(**merged)`, and the descriptor throws NotSupportedException there. The recording covers the merge with
    // validate_shape=False (NodeOperationsValuesTests).

    /// <summary>Test that LLM is called when summary exceeds character limit.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_batch_summaries_calls_llm_for_long_summary()
    {
        var llm = new MockLlm { ReturnValue = Reply("summaries", Item(("name", "Test Node"), ("summary", "Condensed summary"))) };
        var node = Node("Test Node", summary: "Short");

        // Create edges with long facts that exceed the threshold
        var longFact = new string('x', (int)(TextUtils.MaxSummaryChars * 2));
        var edge = new EntityEdge
        {
            Uuid = "edge1",
            GroupId = "group",
            SourceNodeUuid = node.Uuid,
            TargetNodeUuid = "other-uuid",
            Name = "test_edge",
            Fact = longFact,
            CreatedAt = DatetimeUtils.UtcNow(),
        };

        // Multiple long edges
        await ExtractSummariesAsync(llm, [node], new OrderedDictionary<string, List<EntityEdge>> { [node.Uuid] = [edge, edge] }, token: TestContext.Current.CancellationToken);

        // LLM should have been called to condense the long summary
        Assert.Single(llm.Calls);
        Assert.Equal("Condensed summary", node.Summary);
    }

    [Fact(Timeout = 5000)]
    public async Task test_extract_attributes_preserves_prior_attributes_when_entity_types_none()
    {
        var (clients, llm) = MakeClients();
        llm.ReturnValue = Reply("summaries");
        var node = Node("Alice", labels: ["Entity", "Person"], summary: "Existing summary", attributes: new() { ["age"] = 30L, ["city"] = "New York" });

        var results = await LoopGuard.RunAsync(() => NodeOperations.ExtractAttributesFromNodesAsync(
            clients, [node], new EpisodeOrEpisodes.One { Episode = MakeEpisode() }, [], null, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(PyJson.Dumps(new OrderedDictionary<string, object?> { ["age"] = 30L, ["city"] = "New York" }), PyJson.Dumps(results[0].Attributes));
    }

    [Fact(Timeout = 5000)]
    public async Task test_extract_attributes_preserves_prior_attributes_when_label_not_in_entity_types()
    {
        // class Organization(BaseModel): industry: str | None = None
        var organization = new GraphTypeDescriptor("Organization", null, [new GraphFieldDescriptor("industry", GraphFieldKind.String, false, null, false, null)]);
        var (clients, llm) = MakeClients();
        llm.ReturnValue = Reply("summaries");
        var node = Node("Alice", labels: ["Entity", "Person"], summary: "Existing summary", attributes: new() { ["age"] = 30L, ["city"] = "New York" });

        var results = await LoopGuard.RunAsync(() => NodeOperations.ExtractAttributesFromNodesAsync(
            clients,
            [node],
            new EpisodeOrEpisodes.One { Episode = MakeEpisode() },
            [],
            new OrderedDictionary<string, GraphTypeDescriptor> { ["Organization"] = organization },
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(PyJson.Dumps(new OrderedDictionary<string, object?> { ["age"] = 30L, ["city"] = "New York" }), PyJson.Dumps(results[0].Attributes));
    }
}
