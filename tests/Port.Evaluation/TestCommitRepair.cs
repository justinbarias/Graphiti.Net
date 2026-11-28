// Ported from tests/ingestion/test_commit_repair.py (port/RULES.md, R25). The code under test is
// graphiti_core/ingestion/phases.py. Packet P9g of plan 0004, layer 9.
//
// Replay tests for the commit of concurrent ingestion (steps C1 to C10).
//
// Each test makes change sets with no model call, commits them in sequence order on an
// in-memory graph, and checks the repair. The model client raises an error on a call
// that a test does not expect.
//
// The Harness runs the real write functions on HarnessDriver (IngestionFixtures.cs). These tests keep a marker
// (open item O10), each with the recorded case that covers the same behavior (values/phases.json, keys `async_cases` and
// `graphiti_cases`):
// TODO(port): O10 test_c3_merges_a_node_that_an_earlier_unit_of_the_window_created (untyped, typed). It calls
//   Harness.enrich, and the replaced extract_attributes_from_nodes writes `+<episode uuid>` into each summary. Covered:
//   rebase_change_set c3_exact, and the graphiti case commit_and_enrich.
// TODO(port): O10 test_c3_a_label_promotion_that_the_enrichment_wrote_keeps_a_seen_candidate. It calls Harness.enrich.
//   Covered: rebase_change_set c3_seen_label_written.
// TODO(port): O10 test_c7a_saga_links_are_in_the_final_change_set. It replaces NextEpisodeEdge.save, HasEpisodeEdge.save,
//   SagaNode.save, SagaNode.get_by_uuid, graphiti._find_saga, and graphiti._saga_get_previous_episode_uuid. Covered: the
//   graphiti case commit_with_a_saga_name, and _build_saga_links.
// TODO(port): O10 test_c7a_new_saga_is_written_in_the_apply_and_not_before. The same replacements. Covered: the graphiti
//   case commit_bulk_with_a_saga_name, and apply_change_set saga_new.
// TODO(port): O10 test_c7a_rejected_unit_leaves_no_saga_node. It replaces graphiti._get_or_create_saga. Covered in part:
//   the graphiti case commit_hook_rejects.
// TODO(port): O10 test_c7a_saga_object_that_is_not_in_the_graph_is_saved_as_in_serial_ingestion. The same replacements.
//   Covered: _build_saga_links, and apply_change_set saga_new_not_created.
// TODO(port): O10 test_c7a_append_keeps_the_stored_fields_of_the_saga_node (name, object). The same replacements.
//   Covered: apply_change_set saga_stored.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.IngestionFixtures;

public class TestCommitRepair
{
    // Each test runs on one loop (R25), and it ends when xUnit cancels its token.
    private static Task Loop(Func<Task> body, CancellationToken cancellationToken) => LoopGuard.RunAsync(body).WaitAsync(cancellationToken);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    // `GROUP`. The record Group of the in-memory store has the same name.
    private const string Group = IngestionFixtures.Group;

    private static List<string> LabelsFor(bool typed, string label) => typed ? ["Entity", label] : ["Entity"];

    /// <summary>Analyze two units on the same snapshot, then commit them in sequence order.</summary>
    private static async Task<((AdmittedUnit Unit, CommitResult Result) First, (AdmittedUnit Unit, CommitResult Result) Second)> CommitTwoAsync(
        Harness h,
        Func<AdmittedUnit, Task<ChangeSet>> fields1,
        Func<AdmittedUnit, Task<ChangeSet>> fields2,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        OrderedDictionary<string, List<string>>? identityProperties = null)
    {
        var unit1 = await h.AdmitAsync("u1");
        var unit2 = await h.AdmitAsync("u2");
        var changeSet1 = await fields1(unit1);
        var changeSet2 = await fields2(unit2);
        Assert.Equal(0, changeSet1.SnapshotSeq);
        Assert.Equal(0, changeSet2.SnapshotSeq);
        var result1 = await h.CommitAsync(unit1, changeSet1, entityTypes, identityProperties);
        var result2 = await h.CommitAsync(unit2, changeSet2, entityTypes, identityProperties);
        return ((unit1, result1), (unit2, result2));
    }

    // --- C1 --------------------------------------------------------------------------

    [Fact(Timeout = 10000)]
    public async Task test_c1_retry_applies_the_stored_final_change_set()
    {
        await Loop(async () =>
        {
            var clock = new FakeClock();
            var h = new Harness(clock);
            var unit = await h.AdmitAsync("u1");
            var changeSet = await h.AnalyzeAsync(unit, createNodes: [MakeNode("a1", "Acme")]);
            // An earlier attempt stored the final change set and stopped before the write.
            var turn = (CommitTurn)await h.Store.BeginCommitAsync(Group, unit.Seq, h.Config.LeaseSeconds, Token);
            var final = changeSet.ModelCopy(deep: true);
            final.CreateNodes = [MakeNode("final", "Final Node")];
            final.NodeUuids = ["final"];
            await h.Store.SaveFinalAsync(Group, unit.Seq, turn.Fence, final.ToBytes(), Token);
            // The retry gets the turn only after the deadline of the earlier attempt.
            clock.Advance(h.Config.LeaseSeconds + 1);

            var result = await h.CommitAsync(unit, changeSet);

            Assert.Equal(1, result.Report["retry"]);
            AssertSet(h.Graph.Nodes.Keys, "final");
            Assert.DoesNotContain("read:node_candidates", h.Graph.Log);
            Assert.Equal(0, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    // --- C3 --------------------------------------------------------------------------

    [Fact(Timeout = 10000)]
    public async Task test_c3_asks_the_model_when_the_rules_do_not_resolve_a_collision()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            h.UseLlm("""{"entity_resolutions": [{"id": 0, "name": "Zenith GmbH", "duplicate_candidate_id": 0}]}""");
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, createNodes: [MakeNode("a1", "Zenith Corporation")]),
                unit => h.AnalyzeAsync(unit, createNodes: [MakeNode("a2", "Zenith GmbH", NearX)]));

            AssertMap(result2.ChangeSet.UuidMap, ("a2", "a1"));
            Assert.Equal(1, result2.Report["repair.llm_calls"]);
            Assert.Equal(1, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c3_keeps_a_node_when_the_model_finds_no_duplicate()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            h.UseLlm("""{"entity_resolutions": [{"id": 0, "name": "Zenith GmbH", "duplicate_candidate_id": -1}]}""");
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, createNodes: [MakeNode("a1", "Zenith Corporation")]),
                unit => h.AnalyzeAsync(unit, createNodes: [MakeNode("a2", "Zenith GmbH", NearX)]));

            Assert.Empty(result2.ChangeSet.UuidMap);
            AssertSet(h.Graph.Nodes.Keys, "a1", "a2");
            Assert.Equal(1, result2.Report["node.collided"]);
            Assert.Equal(0, result2.Report["node.merged"]);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c3_finds_a_delta_node_that_the_search_does_not_return()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            var unit1 = await h.AdmitAsync("u1");
            var unit2 = await h.AdmitAsync("u2");
            var changeSet1 = await h.AnalyzeAsync(unit1, createNodes: [MakeNode("a1", "Acme")]);
            var changeSet2 = await h.AnalyzeAsync(unit2, createNodes: [MakeNode("a2", "Acme")]);
            await h.CommitAsync(unit1, changeSet1);
            // The search index does not have the node of unit 1 at this time.
            h.Graph.Hidden.Add("a1");

            var result2 = await h.CommitAsync(unit2, changeSet2);

            AssertMap(result2.ChangeSet.UuidMap, ("a2", "a1"));
            AssertSet(h.Graph.Nodes.Keys, "a1");
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c3_index_lag_covers_a_unit_that_committed_before_the_snapshot()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            await h.RunAsync("u1", createNodes: [MakeNode("a1", "Acme")]);
            h.Graph.Hidden.Add("a1");
            // Unit 2 starts after the commit of unit 1, so its snapshot contains unit 1.
            var unit2 = await h.AdmitAsync("u2");
            var changeSet2 = await h.AnalyzeAsync(unit2, createNodes: [MakeNode("a2", "Acme")]);
            Assert.Equal(1, changeSet2.SnapshotSeq);

            var result2 = await h.CommitAsync(unit2, changeSet2);

            Assert.Equal(1, result2.Report["delta.unit_count"]);
            AssertMap(result2.ChangeSet.UuidMap, ("a2", "a1"));
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c3_with_no_index_lag_the_delta_has_no_unit_before_the_snapshot()
    {
        await Loop(async () =>
        {
            var h = new Harness(new FakeClock(), indexLagSeconds: 0.0);
            await h.RunAsync("u1", createNodes: [MakeNode("a1", "Acme")]);
            h.Graph.Hidden.Add("a1");
            h.Clock!.Advance(1);
            var (_, result2) = await h.RunAsync("u2", createNodes: [MakeNode("a2", "Acme")]);

            Assert.Equal(0, result2.Report["delta.unit_count"]);
            Assert.Empty(result2.ChangeSet.UuidMap);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c3_does_not_decide_again_a_candidate_that_the_analysis_saw()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            h.Graph.Add(MakeNode("s1", "Zenith Corporation"));
            var unit = await h.AdmitAsync("u1");
            var changeSet = await h.AnalyzeAsync(
                unit,
                createNodes: [MakeNode("a1", "Zenith GmbH", NearX)],
                seenNodeCandidates: new() { ["a1"] = ["s1"] });
            var result = await h.CommitAsync(unit, changeSet);

            Assert.Equal(0, result.Report["node.collided"]);
            AssertSet(h.Graph.Nodes.Keys, "s1", "a1");
            Assert.Equal(0, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c3_decides_a_stored_candidate_that_the_analysis_did_not_see()
    {
        await Loop(async () =>
        {
            // A writer outside the sequence created the node after the analysis.
            var h = new Harness();
            var unit = await h.AdmitAsync("u1");
            var changeSet = await h.AnalyzeAsync(unit, createNodes: [MakeNode("a1", "Acme")]);
            h.Graph.Add(MakeNode("s1", "Acme"));

            var result = await h.CommitAsync(unit, changeSet);

            AssertMap(result.ChangeSet.UuidMap, ("a1", "s1"));
            Assert.Equal(0, result.Report["delta.unit_count"]);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c3_identity_key_of_a_delta_patch_merges_a_seen_candidate()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            List<string> person = ["Entity", "Person"];
            h.Graph.Add(MakeNode("s1", "Ana Lopez", labels: person));
            var identityProperties = new OrderedDictionary<string, List<string>> { ["Person"] = ["email"] };
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(
                    unit,
                    operations: [new PatchNode { NodeUuid = "s1", Labels = person, Attributes = new() { ["email"] = "ana@example.com" } }],
                    nodeUuids: ["s1"]),
                // The analysis saw `s1` with no email and found no duplicate.
                unit => h.AnalyzeAsync(
                    unit,
                    createNodes: [MakeNode("p2", "A. Lopez", Y, labels: person, attributes: new() { ["email"] = "ana@example.com" })],
                    seenNodeCandidates: new() { ["p2"] = ["s1"] }),
                identityProperties: identityProperties,
                entityTypes: Types("Person"));

            // The Enrich phase of unit 1 did not run, so the graph node has no email.
            Assert.Empty(h.Graph.Nodes["s1"].Attributes);
            AssertMap(result2.ChangeSet.UuidMap, ("p2", "s1"));
            Assert.Equal(0, result2.Report["repair.llm_calls"]);
            Assert.Equal(0, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c3_a_delta_patch_with_no_new_data_does_not_make_a_collision()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            h.Graph.Add(MakeNode("s1", "Zenith Corporation", labels: ["Entity", "Company"]));
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, operations: [new PatchNode { NodeUuid = "s1", Labels = ["Entity", "Company"] }], nodeUuids: ["s1"]),
                unit => h.AnalyzeAsync(
                    unit,
                    createNodes: [MakeNode("a2", "Zenith GmbH", NearX)],
                    seenNodeCandidates: new() { ["a2"] = ["s1"] }));

            Assert.Equal(0, result2.Report["node.collided"]);
            Assert.Equal(0, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c3_a_delta_patch_that_promotes_a_label_keeps_a_seen_candidate()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            h.Graph.Add(MakeNode("s1", "Zenith Corporation"));
            h.UseLlm("""{"entity_resolutions": [{"id": 0, "name": "Zenith GmbH", "duplicate_candidate_id": 0}]}""");
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, operations: [new PatchNode { NodeUuid = "s1", Labels = ["Entity", "Company"] }], nodeUuids: ["s1"]),
                unit => h.AnalyzeAsync(
                    unit,
                    createNodes: [MakeNode("a2", "Zenith GmbH", NearX, labels: ["Entity", "Company"])],
                    seenNodeCandidates: new() { ["a2"] = ["s1"] }),
                entityTypes: Types("Company"));

            Assert.Equal(1, result2.Report["node.collided"]);
            AssertMap(result2.ChangeSet.UuidMap, ("a2", "s1"));
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c3_a_label_that_the_analysis_saw_does_not_make_a_collision()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            h.Graph.Add(MakeNode("s1", "Zenith Corporation"));
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, operations: [new PatchNode { NodeUuid = "s1", Labels = ["Entity", "Company"] }], nodeUuids: ["s1"]),
                unit => h.AnalyzeAsync(
                    unit,
                    createNodes: [MakeNode("a2", "Zenith GmbH", NearX)],
                    seenNodeCandidates: new() { ["a2"] = ["s1"] },
                    // The analysis of unit 2 read the node after the label was in the graph.
                    seenNodeLabels: new() { ["s1"] = ["Entity", "Company"] }));

            Assert.Equal(0, result2.Report["node.collided"]);
            Assert.Equal(0, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    // --- C2a -------------------------------------------------------------------------

    [Theory(Timeout = 10000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task test_c2a_apply_failed_unit_gives_only_the_records_that_exist(bool partial)
    {
        await Loop(async () =>
        {
            var h = new Harness(maxApplyAttempts: 2);
            var unit1 = await h.AdmitAsync("u1");
            var unit2 = await h.AdmitAsync("u2");
            var changeSet1 = await h.AnalyzeAsync(unit1, createNodes: [MakeNode("a1", "Acme")]);
            var changeSet2 = await h.AnalyzeAsync(unit2, createNodes: [MakeNode("a2", "Acme")]);
            h.Graph.FailBulkWrites = 2;
            h.Graph.PartialBulkWrite = partial;
            await Assert.ThrowsAsync<InvalidOperationException>(() => h.CommitAsync(unit1, changeSet1));
            Assert.Equal(partial, h.Graph.Nodes.ContainsKey("a1"));

            var result2 = await h.CommitAsync(unit2, changeSet2);

            Assert.Equal(1, result2.Report["delta.unit_count"]);
            if (partial)
            {
                AssertMap(result2.ChangeSet.UuidMap, ("a2", "a1"));
                AssertSet(h.Graph.Nodes.Keys, "a1");
            }
            else
            {
                // A new node is never merged into a record that is not in the graph.
                Assert.Empty(result2.ChangeSet.UuidMap);
                AssertSet(h.Graph.Nodes.Keys, "a2");
            }
        }, TestContext.Current.CancellationToken);
    }

    // --- C4 and C4a ------------------------------------------------------------------

    [Fact(Timeout = 10000)]
    public async Task test_c4_drops_an_edge_that_a_node_merge_makes_a_self_edge()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, createNodes: [MakeNode("a1", "Acme")]),
                unit => h.AnalyzeAsync(
                    unit,
                    createNodes: [MakeNode("a2", "Acme"), MakeNode("a3", "ACME")],
                    createEdges: [MakeEdge("e2", "a2", "a3", "Acme is also named ACME", Z)]));

            var final = result2.ChangeSet;
            AssertMap(final.UuidMap, ("a2", "a1"), ("a3", "a1"));
            Assert.Empty(final.CreateEdges);
            Assert.Empty(final.Episodes[0].EntityEdges);
            Assert.Equal([("ep-u2", "a1")], final.EpisodicEdges.Select(edge => (edge.SourceNodeUuid, edge.TargetNodeUuid)));
            Assert.Empty(h.Graph.Edges);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c4a_removes_a_duplicate_between_created_edges_with_no_model_call()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, createNodes: [MakeNode("a1", "Acme")]),
                unit => h.AnalyzeAsync(
                    unit,
                    createNodes: [MakeNode("a2", "Acme"), MakeNode("a3", "ACME"), MakeNode("b2", "Bo", Y)],
                    createEdges:
                    [
                        MakeEdge("e2", "a2", "b2", "Acme hired Bo", Z),
                        MakeEdge("e3", "a3", "b2", "acme  hired bo", Z, episodes: ["ep-other"]),
                        MakeEdge("e4", "a3", "b2", "Acme pays Bo", Z),
                    ]));

            var final = result2.ChangeSet;
            Assert.Equal(["e2", "e4"], final.CreateEdges.Select(edge => edge.Uuid));
            Assert.Equal(["ep-u2", "ep-other"], final.CreateEdges[0].Episodes);
            Assert.Equal(["e2", "e4"], final.Episodes[0].EntityEdges);
            Assert.Equal(1, result2.Report["repair.c4a.edge_removed"]);
            Assert.Equal(1, result2.Report["edge.deduplicated"]);
            AssertSet(h.Graph.Edges.Keys, "e2", "e4");
            Assert.Equal(0, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    // --- C5 to C7 --------------------------------------------------------------------

    private static void StoredPeople(Harness h) => h.Graph.Add(MakeNode("ana", "Ana"), MakeNode("acme", "Acme", Y), MakeNode("bolt", "Bolt", Z));

    [Fact(Timeout = 10000)]
    public async Task test_c5_exact_fact_of_an_earlier_unit_is_a_duplicate_with_no_model_call()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            StoredPeople(h);
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, createEdges: [MakeEdge("e1", "ana", "acme", "Ana works at Acme")]),
                unit => h.AnalyzeAsync(unit, createEdges: [MakeEdge("e2", "ana", "acme", "ana works at  ACME")]));

            var final = result2.ChangeSet;
            Assert.Empty(final.CreateEdges);
            Assert.Equal(["e1"], final.Episodes[0].EntityEdges);
            Assert.Contains(final.Operations, operation => Same(operation, new AddEpisodesToEdge { EdgeUuid = "e1", EpisodeUuids = ["ep-u2"] }));
            AssertSet(h.Graph.Edges.Keys, "e1");
            Assert.Equal(["ep-u1", "ep-u2"], h.Graph.Edges["e1"].Episodes);
            Assert.Equal(["e1"], result2.Edges.Select(edge => edge.Uuid));
            Assert.Equal(1, result2.Report["edge.collided"]);
            Assert.Equal(1, result2.Report["repair.c5.edge_merged"]);
            Assert.Equal(1, result2.Report["operation.applied"]);
            Assert.Equal(0, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c5_model_decides_a_duplicate_with_a_different_fact_text()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            StoredPeople(h);
            h.UseLlm("""{"duplicate_facts": [0], "contradicted_facts": []}""");
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, createEdges: [MakeEdge("e1", "ana", "acme", "Ana works at Acme")]),
                // The reverse direction is also a duplicate candidate.
                unit => h.AnalyzeAsync(unit, createEdges: [MakeEdge("e2", "acme", "ana", "Acme employs Ana")]));

            AssertSet(h.Graph.Edges.Keys, "e1");
            Assert.Equal(["ep-u1", "ep-u2"], h.Graph.Edges["e1"].Episodes);
            Assert.Equal(1, result2.Report["repair.llm_calls"]);
            Assert.Equal(1, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c6_new_fact_ends_a_contradicted_fact_of_an_earlier_unit()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            StoredPeople(h);
            h.UseLlm("""{"duplicate_facts": [], "contradicted_facts": [0]}""");
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, createEdges: [MakeEdge("e1", "ana", "acme", "Ana works at Acme", validAt: T0)]),
                // The edge has different endpoints. Only the graph-wide pass finds the candidate.
                unit => h.AnalyzeAsync(unit, createEdges: [MakeEdge("e2", "ana", "bolt", "Ana works at Bolt", NearX, validAt: T1)]));

            var ended = h.Graph.Edges["e1"];
            Assert.Equal(T1, ended.InvalidAt);
            Assert.NotNull(ended.ExpiredAt);
            Assert.Null(h.Graph.Edges["e2"].ExpiredAt);
            var final = result2.ChangeSet;
            var operation = Assert.IsType<EndEdge>(Assert.Single(final.Operations));
            Assert.False(operation.OnlyIfOpen);
            Assert.Equal(["e2", "e1"], final.Episodes[0].EntityEdges);
            AssertSet(result2.Edges.Select(edge => edge.Uuid), "e1", "e2");
            Assert.Equal(1, result2.Report["repair.c6.edge_ended"]);
            Assert.Equal(1, result2.Report["edge.ended"]);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c7_a_candidate_that_starts_later_ends_the_new_fact()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            StoredPeople(h);
            h.UseLlm("""{"duplicate_facts": [], "contradicted_facts": [0]}""");
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, createEdges: [MakeEdge("e1", "ana", "acme", "Ana works at Acme", validAt: T1)]),
                unit => h.AnalyzeAsync(unit, createEdges: [MakeEdge("e2", "ana", "bolt", "Ana works at Bolt", NearX, validAt: T0)]));

            Assert.Null(h.Graph.Edges["e1"].InvalidAt);
            Assert.Null(h.Graph.Edges["e1"].ExpiredAt);
            Assert.Equal(T1, h.Graph.Edges["e2"].InvalidAt);
            Assert.NotNull(h.Graph.Edges["e2"].ExpiredAt);
            Assert.Empty(result2.ChangeSet.Operations);
            Assert.Equal(1, result2.Report["edge.ended"]);
        }, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The analysis ended the new fact at T2, the start of a stored fact that it saw.
    ///
    /// A fact of an earlier unit of the window contradicts the new fact and starts at T1.
    /// Serial ingestion ends the new fact at the earliest contradiction that starts later.
    /// An end date that the episode gave stays.
    /// </summary>
    // PORT: the ids end-date-from-a-candidate and end-date-from-the-episode are the two rows. The expected end date is
    // T1 for the first and T2 for the second.
    [Theory(Timeout = 10000)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task test_c7_an_earlier_contradiction_of_the_delta_moves_the_end_date_of_the_new_fact(bool endedByCandidate)
    {
        await Loop(async () =>
        {
            var expectedInvalidAt = endedByCandidate ? T1 : T2;
            var h = new Harness();
            StoredPeople(h);
            h.UseLlm("""{"duplicate_facts": [], "contradicted_facts": [0]}""");
            var newFact = MakeEdge("e2", "ana", "bolt", "Ana works at Bolt", NearX, validAt: T0, invalidAt: T2, expiredAt: T2);
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, createEdges: [MakeEdge("e1", "ana", "acme", "Ana works at Acme", validAt: T1)]),
                unit => h.AnalyzeAsync(unit, createEdges: [newFact], edgesEndedByCandidate: endedByCandidate ? ["e2"] : []));

            Assert.Equal(1, result2.Report["edge.collided"]);
            Assert.Equal(expectedInvalidAt, h.Graph.Edges["e2"].InvalidAt);
            Assert.Equal(T2, h.Graph.Edges["e2"].ExpiredAt);
            // The fact of the earlier unit starts later. It stays open.
            Assert.Null(h.Graph.Edges["e1"].InvalidAt);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c6_finds_a_delta_edge_that_the_search_does_not_return()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            StoredPeople(h);
            h.UseLlm("""{"duplicate_facts": [], "contradicted_facts": [0]}""");
            var unit1 = await h.AdmitAsync("u1");
            var unit2 = await h.AdmitAsync("u2");
            var changeSet1 = await h.AnalyzeAsync(unit1, createEdges: [MakeEdge("e1", "ana", "acme", "Ana works at Acme", validAt: T0)]);
            var changeSet2 = await h.AnalyzeAsync(unit2, createEdges: [MakeEdge("e2", "ana", "bolt", "Ana works at Bolt", NearX, validAt: T1)]);
            await h.CommitAsync(unit1, changeSet1);
            h.Graph.Hidden.Add("e1");

            await h.CommitAsync(unit2, changeSet2);

            Assert.Equal(T1, h.Graph.Edges["e1"].InvalidAt);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c7_an_edge_with_no_new_candidate_makes_no_model_call()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            StoredPeople(h);
            h.Graph.Add(MakeEdge("e0", "ana", "acme", "Ana works at Acme"));
            var (_, (_, result2)) = await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(unit, createEdges: [MakeEdge("e1", "ana", "bolt", "Ana likes Bolt", Z)]),
                // The analysis saw `e0` in the two passes.
                unit => h.AnalyzeAsync(
                    unit,
                    createEdges: [MakeEdge("e2", "ana", "acme", "Ana visits Acme", NearX)],
                    seenDuplicateCandidates: new() { ["e2"] = ["e0"] },
                    seenInvalidationCandidates: new() { ["e2"] = [] }));

            Assert.Equal(1, result2.Report["edge.checked"]);
            Assert.Equal(0, result2.Report["edge.collided"]);
            AssertSet(h.Graph.Edges.Keys, "e0", "e1", "e2");
            Assert.Equal(0, h.Llm.AwaitCount);
        }, TestContext.Current.CancellationToken);
    }

    // --- C7a to C7c ------------------------------------------------------------------

    [Fact(Timeout = 10000)]
    public async Task test_c7b_hook_output_is_the_stored_final_change_set()
    {
        await Loop(async () =>
        {
            List<int> seenByHook = [];

            Task<ChangeSet> DropEdges(ChangeSet changeSet, CancellationToken cancellationToken)
            {
                seenByHook.Add(changeSet.CreateEdges.Count);
                changeSet.CreateEdges = [];
                return Task.FromResult(changeSet);
            }

            var h = new Harness(beforeCommitWrite: DropEdges);
            StoredPeople(h);
            var (unit, result) = await h.RunAsync("u1", createEdges: [MakeEdge("e1", "ana", "acme", "Ana works at Acme")]);

            Assert.Equal([1], seenByHook);
            Assert.Empty(result.ChangeSet.CreateEdges);
            Assert.Empty(h.Graph.Edges);
            var storedFinal = ChangeSet.FromBytes(h.Store.Groups[Group].Units[unit.Seq].Payload!);
            Assert.Empty(storedFinal.CreateEdges);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c7b_hook_can_reject_the_change_set()
    {
        await Loop(async () =>
        {
            static Task<ChangeSet> Reject(ChangeSet changeSet, CancellationToken cancellationToken)
            {
                if (changeSet.UnitId == "u1")
                {
                    throw new ChangeSetRejectedException("not permitted");
                }

                return Task.FromResult(changeSet);
            }

            var h = new Harness(beforeCommitWrite: Reject);
            var unit1 = await h.AdmitAsync("u1");
            var changeSet1 = await h.AnalyzeAsync(unit1, createNodes: [MakeNode("a1", "Acme")]);
            await Assert.ThrowsAsync<ChangeSetRejectedException>(() => h.CommitAsync(unit1, changeSet1));

            Assert.Equal(StateStore.StatusFailed, h.Store.Groups[Group].Units[unit1.Seq].Status);
            Assert.Empty(h.Graph.Nodes);
            Assert.DoesNotContain("write:bulk", h.Graph.Log);
            // The position of the unit is released.
            var (_, result2) = await h.RunAsync("u2", createNodes: [MakeNode("a2", "Acme")]);
            Assert.Equal(0, result2.Report["delta.unit_count"]);
            AssertSet(h.Graph.Nodes.Keys, "a2");
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c7c_final_change_set_is_stored_before_the_first_graph_write()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            h.Graph.Add(MakeNode("ana", "Ana"), MakeNode("acme", "Acme", Y));
            h.Graph.Add(MakeEdge("e0", "ana", "acme", "Ana works at Acme"));
            await h.RunAsync(
                "u1",
                createNodes: [MakeNode("a1", "Zed", Z)],
                operations: [new AddEpisodesToEdge { EdgeUuid = "e0", EpisodeUuids = ["ep-u1"] }]);

            var writes = h.Graph.Log.Where(entry => entry.StartsWith("write:", StringComparison.Ordinal) || entry.StartsWith("store:", StringComparison.Ordinal));
            Assert.Equal(["store:save_final", "write:bulk", "write:edge_update", "store:finish_commit"], writes);
        }, TestContext.Current.CancellationToken);
    }

    // --- C8 --------------------------------------------------------------------------

    [Fact(Timeout = 10000)]
    public async Task test_c8_operations_apply_to_the_current_state_of_a_stored_edge()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            StoredPeople(h);
            h.Graph.Add(MakeEdge("e0", "ana", "acme", "Ana works at Acme", episodes: ["ep0"], attributes: new() { ["role"] = "a" }));
            await CommitTwoAsync(
                h,
                unit => h.AnalyzeAsync(
                    unit,
                    operations:
                    [
                        new EndEdge { EdgeUuid = "e0", InvalidAt = T1, ExpiredAt = T1 },
                        new SetEdgeAttributes { EdgeUuid = "e0", BaseAttributes = new() { ["role"] = "a" }, Attributes = new() { ["role"] = "c" } },
                    ]),
                // The analysis of unit 2 read the edge before the commit of unit 1.
                unit => h.AnalyzeAsync(
                    unit,
                    operations:
                    [
                        new EndEdge { EdgeUuid = "e0", InvalidAt = T2, ExpiredAt = T2 },
                        new AddEpisodesToEdge { EdgeUuid = "e0", EpisodeUuids = ["ep-u2", "ep0"] },
                        new SetEdgeAttributes
                        {
                            EdgeUuid = "e0",
                            BaseAttributes = new() { ["role"] = "a" },
                            Attributes = new() { ["role"] = "b", ["level"] = 1L },
                        },
                    ]));

            var edge = h.Graph.Edges["e0"];
            // The earlier end dates of unit 1 stay.
            Assert.Equal(T1, edge.InvalidAt);
            Assert.Equal(T1, edge.ExpiredAt);
            Assert.Equal(["ep0", "ep-u2"], edge.Episodes);
            // The key that unit 1 changed keeps its value. The key that only unit 2 set applies.
            AssertDict(edge.Attributes, ("role", "c"), ("level", 1L));
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c8_only_if_open_does_not_end_an_edge_that_is_ended()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            StoredPeople(h);
            h.Graph.Add(MakeEdge("e0", "ana", "acme", "Ana works at Acme", expiredAt: T0, invalidAt: T0));
            var (_, result) = await h.RunAsync(
                "u1",
                operations: [new EndEdge { EdgeUuid = "e0", InvalidAt = T1, ExpiredAt = T1, OnlyIfOpen = true }]);

            Assert.Equal(T0, h.Graph.Edges["e0"].ExpiredAt);
            Assert.Empty(result.Edges);
            Assert.DoesNotContain("write:edge_update", h.Graph.Log);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c8_drops_an_operation_on_an_edge_that_does_not_exist()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            var (_, result) = await h.RunAsync("u1", operations: [new AddEpisodesToEdge { EdgeUuid = "deleted", EpisodeUuids = ["ep-u1"] }]);

            Assert.Equal(1, result.Report["operation.dropped"]);
            Assert.Equal(0, result.Report["operation.applied"]);
            Assert.Empty(h.Graph.Edges);
        }, TestContext.Current.CancellationToken);
    }

    // --- C9 --------------------------------------------------------------------------

    [Fact(Timeout = 10000)]
    public async Task test_c9_each_write_has_a_time_limit_below_the_lease()
    {
        await Loop(async () =>
        {
            var h = new Harness(leaseSeconds: 30.0, writeMarginSeconds: 4.0);
            StoredPeople(h);
            h.Graph.Add(MakeEdge("e0", "ana", "acme", "Ana works at Acme"));
            await h.RunAsync("u1", operations: [new AddEpisodesToEdge { EdgeUuid = "e0", EpisodeUuids = ["ep-u1"] }]);

            Assert.Equal(2, h.Graph.WriteTimeouts.Count);
            Assert.All(h.Graph.WriteTimeouts, timeout => Assert.True(timeout is not null && timeout > 0 && timeout <= 26.0));
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c9_update_does_not_create_again_an_edge_that_a_writer_deleted()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            StoredPeople(h);
            h.Graph.Add(MakeEdge("e0", "ana", "acme", "Ana works at Acme"));
            // A writer outside the sequence deletes the edge between the read and the write.
            h.Graph.BeforeEdgeUpdate = () => h.Graph.Edges.Remove("e0");
            await h.RunAsync("u1", operations: [new AddEpisodesToEdge { EdgeUuid = "e0", EpisodeUuids = ["ep-u1"] }]);

            Assert.Contains("write:edge_update", h.Graph.Log);
            Assert.Empty(h.Graph.Edges);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c9_check_of_the_turn_renews_the_deadline_before_a_write()
    {
        await Loop(async () =>
        {
            var clock = new FakeClock();
            var h = new Harness(clock, leaseSeconds: 30.0, writeMarginSeconds: 4.0);
            var unit = await h.AdmitAsync("u1");
            var changeSet = await h.AnalyzeAsync(unit, createNodes: [MakeNode("a1", "Acme")]);
            var lease = new UnitLease(h.Store, h.Config, Group, unit.Seq, clock: clock.Call);

            // The unit has the turn. The heartbeat with its fence gives the write a new deadline.
            await h.CommitAsync(unit, changeSet, lease: lease, reader: new SlowReader(h, clock));

            AssertSet(h.Graph.Nodes.Keys, "a1");
            Assert.Equal(1, lease.Fence);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 10000)]
    public async Task test_c9_no_write_starts_after_the_local_deadline()
    {
        await Loop(async () =>
        {
            var clock = new FakeClock();
            var h = new Harness(clock, leaseSeconds: 30.0, writeMarginSeconds: 4.0);
            var unit = await h.AdmitAsync("u1");
            var changeSet = await h.AnalyzeAsync(unit, createNodes: [MakeNode("a1", "Acme")]);
            var lease = new UnitLease(h.Store, h.Config, Group, unit.Seq, clock: clock.Call);

            // The store gives no answer to the check of the turn before the write. The local
            // deadline then stops the write.
            h.Store.HeartbeatError = new InvalidOperationException("no answer");
            await Assert.ThrowsAsync<UnitExpiredException>(() => h.CommitAsync(unit, changeSet, lease: lease, reader: new SlowReader(h, clock)));

            Assert.DoesNotContain("write:bulk", h.Graph.Log);
            Assert.Empty(h.Graph.Nodes);
            // The final change set is stored. A later unit applies it after the deadline.
            Assert.Equal(StateStore.StatusApplying, h.Store.Groups[Group].Units[unit.Seq].Status);
        }, TestContext.Current.CancellationToken);
    }

    // --- C10 -------------------------------------------------------------------------

    [Fact(Timeout = 10000)]
    public async Task test_c10_commit_moves_the_watermark_and_registers_the_nodes()
    {
        await Loop(async () =>
        {
            var h = new Harness();
            var (unit, result) = await h.RunAsync("u1", createNodes: [MakeNode("a1", "Acme"), MakeNode("b1", "Bo", Y)]);

            Assert.Equal(unit.Seq, h.Store.Groups[Group].Watermark);
            Assert.Equal(["a1", "b1"], await h.Store.ReadyEnrichAsync(Group, unit.Seq, Token));
            Assert.Equal("store:finish_commit", h.Graph.Log[^1]);
            Assert.Empty(h.Graph.Episodes["ep-u1"].EntityEdges);
            var span = Assert.Single(h.Tracer.Named("ingest.commit"));
            Assert.Equal(unit.Seq, span.Attributes["ingest.seq"]);
            foreach (var (key, value) in result.Report)
            {
                // PORT: the report holds doubles (packet P8c) and the span holds the Python types, so the test compares the
                // numbers, as `==` of Python compares an int and a float.
                Assert.Equal(value, Convert.ToDouble(span.Attributes[key], System.Globalization.CultureInfo.InvariantCulture));
            }
        }, TestContext.Current.CancellationToken);
    }

    // `class SlowReader` of the two C9 tests: a reader whose node search advances the clock by 27 seconds. Each other
    // read is the read of the graph (`__getattr__`).
    private sealed class SlowReader(Harness h, FakeClock clock) : GraphReader(h.Clients)
    {
        internal override Task<List<List<EntityNode>>> NodeCandidatesAsync(
            List<EntityNode> nodes,
            OrderedDictionary<string, List<string>>? identityProperties = null,
            CancellationToken cancellationToken = default)
        {
            clock.Advance(27.0);
            return h.Graph.NodeCandidatesAsync(nodes, identityProperties, cancellationToken);
        }

        internal override Task<List<List<EntityEdge>>> EdgeDuplicateCandidatesAsync(List<EntityEdge> edges, CancellationToken cancellationToken = default) =>
            h.Graph.EdgeDuplicateCandidatesAsync(edges, cancellationToken);

        internal override Task<List<List<EntityEdge>>> EdgeInvalidationCandidatesAsync(List<EntityEdge> edges, CancellationToken cancellationToken = default) =>
            h.Graph.EdgeInvalidationCandidatesAsync(edges, cancellationToken);

        internal override Task<List<EntityNode>> NodesByUuidsAsync(List<string> uuids, string groupId, CancellationToken cancellationToken = default) =>
            h.Graph.NodesByUuidsAsync(uuids, groupId, cancellationToken);

        internal override Task<List<EpisodicNode>> EpisodesByUuidsAsync(List<string> uuids, CancellationToken cancellationToken = default) =>
            h.Graph.EpisodesByUuidsAsync(uuids, cancellationToken);

        internal override Task<List<EntityEdge>> EdgesByUuidsAsync(List<string> uuids, CancellationToken cancellationToken = default) =>
            h.Graph.EdgesByUuidsAsync(uuids, cancellationToken);
    }

    // PORT: `{'Company': object}`. The class `object` has no field, so the type is a descriptor with no field.
    internal static OrderedDictionary<string, GraphTypeDescriptor> Types(params string[] names)
    {
        var types = new OrderedDictionary<string, GraphTypeDescriptor>();
        foreach (var name in names)
        {
            types[name] = new GraphTypeDescriptor(name, null, []);
        }

        return types;
    }

    // `operation in operations` compares the fields of two pydantic models.
    internal static bool Same(Operation left, Operation right) => PyJson.Dumps(Dump(left)) == PyJson.Dumps(Dump(right));

    private static OrderedDictionary<string, object?> Dump(Operation operation) => operation switch
    {
        PatchNode patch => patch.ModelDump(json: true),
        AddEpisodesToEdge add => add.ModelDump(json: true),
        EndEdge end => end.ModelDump(json: true),
        SetEdgeAttributes set => set.ModelDump(json: true),
        _ => throw new InvalidOperationException("unknown operation"),
    };

    // `set(values) == {...}`.
    internal static void AssertSet(IEnumerable<string> actual, params string[] expected) =>
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual.Order(StringComparer.Ordinal));

    // `mapping == {...}` of a dict of str.
    internal static void AssertMap(OrderedDictionary<string, string> actual, params (string, string)[] expected) =>
        Assert.Equal(expected.OrderBy(pair => pair.Item1, StringComparer.Ordinal), actual.Select(pair => (pair.Key, pair.Value)).OrderBy(pair => pair.Key, StringComparer.Ordinal));

    // `mapping == {...}` of a dict of values.
    internal static void AssertDict(OrderedDictionary<string, object?> actual, params (string, object?)[] expected) =>
        Assert.Equal(expected.OrderBy(pair => pair.Item1, StringComparer.Ordinal), actual.Select(pair => (pair.Key, pair.Value)).OrderBy(pair => pair.Key, StringComparer.Ordinal));
}
