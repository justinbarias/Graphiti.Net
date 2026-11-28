// Ported from tests/ingestion/ingestion_fixtures.py (port/RULES.md, R25): the test doubles of concurrent ingestion. Packet
// P8c of plan 0004, layer 8.
//
// The parts that a test of the port can run today: the constants, make_node, make_edge, make_episode, FakeClock, the
// reads of FakeGraph (a GraphReader, as the upstream fake replaces the reader), LoggingStore, and the store part of
// Harness (`admit` and `analyze`).
//
// Waits for layer 9: `Harness.commit`, `Harness.enrich`, `Harness.run`, the fake `Graphiti` (a SimpleNamespace with
// `_resolve_request_scope`), RecordingTracer, and the writes of FakeGraph. commit_unit and enrich_unit call
// graphiti._resolve_request_scope first, and GraphitiCore is a skeleton until layer 9. The writes of FakeGraph replace
// `phases.add_nodes_and_edges_bulk`, `phases.execute_write_with_timeout`, and `phases.extract_attributes_from_nodes` with
// `monkeypatch.setattr` (open item O10). Their form in the port is a fake driver whose session applies the recorded
// queries of the commit to the in-memory graph. It comes with layer 9, when a test can call commit_unit.
//
// The tests of tests/ingestion/test_commit_repair.py all call Harness.commit, Harness.run, or Harness.enrich, so each one
// waits for layer 9. Each line names the case of values/phases.json (port/tools/record_phases.py) that covers the same
// behavior of the code under test with no `Graphiti` object.
// - test_c1_retry_applies_the_stored_final_change_set: none (commit_unit reads the stored final change set).
// - test_c3_merges_a_node_that_an_earlier_unit_of_the_window_created (untyped, typed): rebase_change_set c3_exact.
// - test_c3_asks_the_model_when_the_rules_do_not_resolve_a_collision: rebase_change_set c3_model_merge.
// - test_c3_keeps_a_node_when_the_model_finds_no_duplicate: rebase_change_set c3_model_keep.
// - test_c3_finds_a_delta_node_that_the_search_does_not_return: rebase_change_set c3_delta_node.
// - test_c3_index_lag_covers_a_unit_that_committed_before_the_snapshot: none (the delta query of commit_unit).
// - test_c3_with_no_index_lag_the_delta_has_no_unit_before_the_snapshot: none (the delta query of commit_unit).
// - test_c3_does_not_decide_again_a_candidate_that_the_analysis_saw: rebase_change_set c3_seen.
// - test_c3_decides_a_stored_candidate_that_the_analysis_did_not_see: rebase_change_set c3_exact.
// - test_c3_identity_key_of_a_delta_patch_merges_a_seen_candidate: rebase_change_set c3_identity_patch.
// - test_c3_a_delta_patch_with_no_new_data_does_not_make_a_collision: rebase_change_set c3_seen_patch_no_data.
// - test_c3_a_delta_patch_that_promotes_a_label_keeps_a_seen_candidate: rebase_change_set c3_seen_patch_label.
// - test_c3_a_label_promotion_that_the_enrichment_wrote_keeps_a_seen_candidate: rebase_change_set c3_seen_label_written.
// - test_c3_a_label_that_the_analysis_saw_does_not_make_a_collision: rebase_change_set c3_seen_label_written.
// - test_c2a_apply_failed_unit_gives_only_the_records_that_exist (complete, partial): rebase_change_set
//   c3_delta_node_missing and c6_delta_edge_stored_False.
// - test_c4_drops_an_edge_that_a_node_merge_makes_a_self_edge: rebase_change_set c4_self_edge_and_duplicate.
// - test_c4a_removes_a_duplicate_between_created_edges_with_no_model_call: c4_self_edge_and_duplicate, and the sync
//   cases of _remap_edges.
// - test_c5_exact_fact_of_an_earlier_unit_is_a_duplicate_with_no_model_call: rebase_change_set c5_exact.
// - test_c5_model_decides_a_duplicate_with_a_different_fact_text: rebase_change_set c5_model.
// - test_c6_new_fact_ends_a_contradicted_fact_of_an_earlier_unit: rebase_change_set c6_ends_old_bulk_False.
// - test_c7_a_candidate_that_starts_later_ends_the_new_fact: rebase_change_set c7_later_candidate.
// - test_c7_an_earlier_contradiction_of_the_delta_moves_the_end_date_of_the_new_fact: rebase_change_set
//   c7_move_end_date, and the sync cases of _move_end_date_to_earliest_contradiction.
// - test_c6_finds_a_delta_edge_that_the_search_does_not_return: rebase_change_set c6_delta_edge_stored_True.
// - test_c7_an_edge_with_no_new_candidate_makes_no_model_call: rebase_change_set c7_no_new_candidate.
// - test_c7a_saga_links_are_in_the_final_change_set: _build_saga_links (the saga object with a previous episode).
// - test_c7a_new_saga_is_written_in_the_apply_and_not_before: apply_change_set saga_new.
// - test_c7a_rejected_unit_leaves_no_saga_node: none (the hook of commit_unit).
// - test_c7a_saga_object_that_is_not_in_the_graph_is_saved_as_in_serial_ingestion: _build_saga_links, and
//   apply_change_set saga_new_not_created.
// - test_c7a_append_keeps_the_stored_fields_of_the_saga_node (name, object): apply_change_set saga_stored.
// - test_c7b_hook_output_is_the_stored_final_change_set: none (the hook of commit_unit).
// - test_c7b_hook_can_reject_the_change_set: none (the hook of commit_unit).
// - test_c7c_final_change_set_is_stored_before_the_first_graph_write: none (save_final of commit_unit).
// - test_c8_operations_apply_to_the_current_state_of_a_stored_edge: apply_change_set operations.
// - test_c8_only_if_open_does_not_end_an_edge_that_is_ended: apply_change_set operations (edge e2).
// - test_c8_drops_an_operation_on_an_edge_that_does_not_exist: apply_change_set operations (edge e-gone).
// - test_c9_each_write_has_a_time_limit_below_the_lease: apply_change_set operations_turn_ok, and _recover_unit.
// - test_c9_update_does_not_create_again_an_edge_that_a_writer_deleted: apply_change_set operations.
// - test_c9_check_of_the_turn_renews_the_deadline_before_a_write: _recover_unit recovered, below_limit.
// - test_c9_no_write_starts_after_the_local_deadline: _recover_unit expired_turn, expired_on_limit.
// - test_c10_commit_moves_the_watermark_and_registers_the_nodes: _recover_unit recovered (finish_commit).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Embedder;
using Graphiti.Core.Ingestion;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;

/// <summary>Test doubles for concurrent ingestion: an in-memory graph, and functions that make units and change sets.</summary>
internal static class IngestionFixtures
{
    internal const string Group = "g";

    internal static readonly PyDateTime T0 = PyDateTime.FromIsoFormat("2026-01-01T00:00:00+00:00");

    internal static readonly PyDateTime T1 = PyDateTime.FromIsoFormat("2026-01-11T00:00:00+00:00");

    internal static readonly PyDateTime T2 = PyDateTime.FromIsoFormat("2026-01-21T00:00:00+00:00");

    // Embeddings with three dimensions. Two vectors on different axes have no similarity.
    internal static List<double> X => [1.0, 0.0, 0.0];

    internal static List<double> Y => [0.0, 1.0, 0.0];

    internal static List<double> Z => [0.0, 0.0, 1.0];

    internal static List<double> NearX => [0.9, 0.1, 0.0];

    internal static double Cosine(List<double>? a, List<double>? b)
    {
        if (a is null || a.Count == 0 || b is null || b.Count == 0)
        {
            return 0.0;
        }

        double dot = 0, left = 0, right = 0;
        for (var i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
            left += a[i] * a[i];
            right += b[i] * b[i];
        }

        return dot / (Math.Sqrt(left) * Math.Sqrt(right));
    }

    internal static EntityNode MakeNode(string uuid, string name, List<double>? embedding = null, List<string>? labels = null, OrderedDictionary<string, object?>? attributes = null) =>
        new EntityNode
        {
            Uuid = uuid,
            Name = name,
            GroupId = Group,
            CreatedAt = T0,
            // PORT: `embedding or X`: an empty list is false, so it gives X too.
            NameEmbedding = embedding is { Count: > 0 } ? embedding : X,
            Labels = labels ?? ["Entity"],
            Attributes = attributes ?? new(),
        }.Validate();

    internal static EntityEdge MakeEdge(string uuid, string source, string target, string fact, List<double>? embedding = null) =>
        new EntityEdge
        {
            Uuid = uuid,
            GroupId = Group,
            SourceNodeUuid = source,
            TargetNodeUuid = target,
            Fact = fact,
            Name = "RELATES",
            Episodes = [],
            CreatedAt = T0,
            // PORT: `embedding or X`: an empty list is false, so it gives X too.
            FactEmbedding = embedding is { Count: > 0 } ? embedding : X,
        }.Validate();

    internal static EpisodicNode MakeEpisode(string uuid = "ep1", PyDateTime? validAt = null, string content = "content") =>
        new EpisodicNode
        {
            Uuid = uuid,
            Name = uuid,
            GroupId = Group,
            Source = EpisodeType.Text,
            SourceDescription = "test",
            Content = content,
            ValidAt = validAt ?? T0,
            CreatedAt = validAt ?? T0,
        }.Validate();
}

/// <summary>The fake clock of the fixtures.</summary>
internal sealed class FakeClock(double now = 1000.0)
{
    internal double Now { get; private set; } = now;

    internal double Call() => Now;

    internal void Advance(double seconds) => Now += seconds;
}

/// <summary>
/// An in-memory graph with the reads of `GraphReader`.
///
/// A search returns a record when it is not in `hidden`. A read by UUID returns each
/// record that exists. Each read returns a copy.
/// </summary>
// PORT: the reads of FakeGraph derive from GraphReader, as the upstream fake replaces the reader. The writes wait for
// layer 9 (see the header of this file). Each read is an `async def` with no await in Python, so it ends with no yield:
// the port returns a completed task (R8), and it does not read the token.
internal sealed class FakeGraph(GraphitiClients clients) : GraphReader(clients)
{
    internal OrderedDictionary<string, EntityNode> Nodes { get; } = new();

    internal OrderedDictionary<string, EntityEdge> Edges { get; } = new();

    internal OrderedDictionary<string, EpisodicNode> Episodes { get; } = new();

    internal HashSet<string> Hidden { get; } = new(StringComparer.Ordinal);

    internal List<string> Log { get; } = [];

    internal void Add(params EntityNode[] records)
    {
        foreach (var record in records)
        {
            Nodes[record.Uuid] = record.ModelCopy(deep: true);
        }
    }

    internal void Add(params EntityEdge[] records)
    {
        foreach (var record in records)
        {
            Edges[record.Uuid] = record.ModelCopy(deep: true);
        }
    }

    internal override Task<List<List<EntityNode>>> NodeCandidatesAsync(
        List<EntityNode> nodes,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        Log.Add("read:node_candidates");
        List<List<EntityNode>> results = [];
        foreach (var node in nodes)
        {
            var key = DedupHelpers.IdentityKeyForNode(node, identityProperties);
            List<EntityNode> candidates = [];
            foreach (var stored in Nodes.Values)
            {
                if (Hidden.Contains(stored.Uuid) || stored.Uuid == node.Uuid)
                {
                    continue;
                }

                var sameName = DedupHelpers.NormalizeStringExact(stored.Name) == DedupHelpers.NormalizeStringExact(node.Name);
                var sameKey = key is not null && Equals(DedupHelpers.IdentityKeyForNode(stored, identityProperties), key);
                var similar = IngestionFixtures.Cosine(stored.NameEmbedding, node.NameEmbedding) >= 0.6;
                if (sameName || sameKey || similar)
                {
                    // A search does not return the name embedding.
                    var copy = stored.ModelCopy(deep: true);
                    copy.NameEmbedding = null;
                    candidates.Add(copy);
                }
            }

            results.Add(candidates);
        }

        return Task.FromResult(results);
    }

    internal override Task<List<List<EntityEdge>>> EdgeDuplicateCandidatesAsync(List<EntityEdge> edges, CancellationToken cancellationToken = default)
    {
        Log.Add("read:edge_duplicate_candidates");
        List<List<EntityEdge>> results = [];
        foreach (var edge in edges)
        {
            var pair = new HashSet<string>(StringComparer.Ordinal) { edge.SourceNodeUuid, edge.TargetNodeUuid };
            results.Add([
                .. Edges.Values
                    .Where(stored => !Hidden.Contains(stored.Uuid) && stored.Uuid != edge.Uuid && pair.SetEquals([stored.SourceNodeUuid, stored.TargetNodeUuid]))
                    .Select(stored => stored.ModelCopy(deep: true)),
            ]);
        }

        return Task.FromResult(results);
    }

    internal override Task<List<List<EntityEdge>>> EdgeInvalidationCandidatesAsync(List<EntityEdge> edges, CancellationToken cancellationToken = default)
    {
        Log.Add("read:edge_invalidation_candidates");
        List<List<EntityEdge>> results =
        [
            .. edges.Select(edge => Edges.Values
                .Where(stored => !Hidden.Contains(stored.Uuid) && stored.Uuid != edge.Uuid && IngestionFixtures.Cosine(stored.FactEmbedding, edge.FactEmbedding) >= 0.6)
                .Select(stored => stored.ModelCopy(deep: true))
                .ToList()),
        ];
        return Task.FromResult(results);
    }

    internal override Task<List<EntityNode>> NodesByUuidsAsync(List<string> uuids, string groupId, CancellationToken cancellationToken = default)
    {
        List<EntityNode> nodes =
        [
            .. uuids.Where(Nodes.ContainsKey).Select(uuid =>
            {
                var copy = Nodes[uuid].ModelCopy(deep: true);
                copy.NameEmbedding = null;
                return copy;
            }),
        ];
        return Task.FromResult(nodes);
    }

    internal override Task<List<EpisodicNode>> EpisodesByUuidsAsync(List<string> uuids, CancellationToken cancellationToken = default)
    {
        List<EpisodicNode> episodes = [.. uuids.Where(Episodes.ContainsKey).Select(uuid => Episodes[uuid].ModelCopy(deep: true))];
        return Task.FromResult(episodes);
    }

    internal override Task<List<EntityEdge>> EdgesByUuidsAsync(List<string> uuids, CancellationToken cancellationToken = default)
    {
        List<EntityEdge> edges = [.. uuids.Where(Edges.ContainsKey).Select(uuid => Edges[uuid].ModelCopy(deep: true))];
        return Task.FromResult(edges);
    }
}

/// <summary>An in-memory store that adds each call to the log of the graph.</summary>
// PORT: InMemoryIngestionStateStore is sealed in the port, so the logging store wraps one and forwards each call.
internal sealed class LoggingStore(FakeGraph graph, Func<double>? clock = null) : IngestionStateStore
{
    internal InMemoryIngestionStateStore Inner { get; } = new(clock);

    internal override Task<Admission?> BeginAdmitAsync(string groupId, string unitId, double ttl, CancellationToken cancellationToken = default) =>
        Inner.BeginAdmitAsync(groupId, unitId, ttl, cancellationToken);

    internal override Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default) =>
        Inner.EndAdmitAsync(groupId, seq, cancellationToken);

    internal override Task<AnalysisStart> StartAnalysisAsync(string groupId, long seq, CancellationToken cancellationToken = default) =>
        Inner.StartAnalysisAsync(groupId, seq, cancellationToken);

    internal override Task SaveChangeSetAsync(string groupId, long seq, byte[] payload, CancellationToken cancellationToken = default) =>
        Inner.SaveChangeSetAsync(groupId, seq, payload, cancellationToken);

    internal override Task HeartbeatAsync(string groupId, long seq, double ttl, long? fence = null, CancellationToken cancellationToken = default) =>
        Inner.HeartbeatAsync(groupId, seq, ttl, fence, cancellationToken);

    internal override Task<BeginCommitResult> BeginCommitAsync(string groupId, long seq, double ttl, CancellationToken cancellationToken = default) =>
        Inner.BeginCommitAsync(groupId, seq, ttl, cancellationToken);

    internal override Task<List<DeltaUnit>> LoadDeltaAsync(string groupId, long afterSeq, long beforeSeq, double since, CancellationToken cancellationToken = default) =>
        Inner.LoadDeltaAsync(groupId, afterSeq, beforeSeq, since, cancellationToken);

    internal override Task SaveFinalAsync(string groupId, long seq, long fence, byte[] payload, CancellationToken cancellationToken = default)
    {
        graph.Log.Add("store:save_final");
        return Inner.SaveFinalAsync(groupId, seq, fence, payload, cancellationToken);
    }

    internal override Task FinishCommitAsync(string groupId, long seq, long fence, List<string> nodeUuids, double retainSeconds, CancellationToken cancellationToken = default)
    {
        graph.Log.Add("store:finish_commit");
        return Inner.FinishCommitAsync(groupId, seq, fence, nodeUuids, retainSeconds, cancellationToken);
    }

    internal override Task FailAsync(string groupId, long seq, string reason, long? fence = null, CancellationToken cancellationToken = default) =>
        Inner.FailAsync(groupId, seq, reason, fence, cancellationToken);

    internal override Task<List<string>> ReadyEnrichAsync(string groupId, long seq, CancellationToken cancellationToken = default) =>
        Inner.ReadyEnrichAsync(groupId, seq, cancellationToken);

    internal override Task FinishEnrichAsync(string groupId, long seq, List<string> nodeUuids, CancellationToken cancellationToken = default) =>
        Inner.FinishEnrichAsync(groupId, seq, nodeUuids, cancellationToken);

    internal override Task CloseAsync(CancellationToken cancellationToken = default) => Inner.CloseAsync(cancellationToken);
}

/// <summary>A fake graph, a store, and functions that make units. The fake `Graphiti` waits for layer 9.</summary>
internal sealed class Harness
{
    internal Harness(FakeClock? clock = null, double leaseSeconds = 60.0, double writeMarginSeconds = 5.0)
    {
        using var source = new CancellationTokenSource();

        // PORT: the driver of the upstream Harness is a MagicMock with `graph_operations_interface = None`, and the
        // cross-encoder a MagicMock. A MagicMock accepts each call and returns another MagicMock, which C# has no form
        // for. The port uses the fake driver of the phases recording (no operations interface, an empty result for each
        // query) and its cross-encoder, which throws at a call. The commit writes go through the patched functions of
        // FakeGraph, which wait for layer 9, so no test of the port reaches the driver of the Harness today.
        var recording = new PhasesRun(new() { ["fn"] = "harness" }, source, -1).Clients;
        Clients = new GraphitiClients
        {
            Driver = recording.Driver,
            LlmClient = new HarnessLlm(),
            Embedder = new HarnessEmbedder(),
            CrossEncoder = recording.CrossEncoder,
            Tracer = recording.Tracer,
            MaxCoroutines = null,
        };
        Graph = new FakeGraph(Clients);
        Clock = clock;
        Store = new LoggingStore(Graph, clock is null ? null : clock.Call);
        Config = new ConcurrentIngestionConfig
        {
            Window = 4,
            StateStore = Store,
            PollIntervalSeconds = 0.001,
            LeaseSeconds = leaseSeconds,
            WriteMarginSeconds = writeMarginSeconds,
        }.Validate();
    }

    internal GraphitiClients Clients { get; }

    internal FakeGraph Graph { get; }

    internal FakeClock? Clock { get; }

    internal LoggingStore Store { get; }

    internal ConcurrentIngestionConfig Config { get; }

    /// <summary>Admit a unit with store calls only.</summary>
    internal async Task<AdmittedUnit> AdmitAsync(string unitId, EpisodicNode? episode = null)
    {
        var admission = await Store.BeginAdmitAsync(IngestionFixtures.Group, unitId, Config.LeaseSeconds);
        Assert.NotNull(admission);
        await Store.EndAdmitAsync(IngestionFixtures.Group, admission.Seq);
        episode ??= IngestionFixtures.MakeEpisode($"ep-{unitId}");
        return new AdmittedUnit
        {
            GroupId = IngestionFixtures.Group,
            UnitId = unitId,
            Seq = admission.Seq,
            CreatedAt = IngestionFixtures.T0,
            Episodes = [episode],
            PreviousEpisodes = [[]],
        };
    }

    /// <summary>Record the snapshot of a unit and store a change set with the given fields.</summary>
    internal async Task<ChangeSet> AnalyzeAsync(AdmittedUnit unit, List<EntityNode>? createNodes = null, List<EntityEdge>? createEdges = null)
    {
        var analysis = await Store.StartAnalysisAsync(IngestionFixtures.Group, unit.Seq);
        createNodes ??= [];
        createEdges ??= [];
        var episode = unit.Episodes[0];
        foreach (var edge in createEdges)
        {
            if (!edge.Episodes.Contains(episode.Uuid))
            {
                edge.Episodes.Add(episode.Uuid);
            }
        }

        List<string> nodeUuids = [.. createNodes.Select(node => node.Uuid)];
        var nodeFirstEpisode = new OrderedDictionary<string, string>();
        foreach (var uuid in nodeUuids)
        {
            nodeFirstEpisode[uuid] = episode.Uuid;
        }

        var written = episode.ModelCopy(deep: true);
        written.EntityEdges = [.. createEdges.Select(edge => edge.Uuid)];
        var changeSet = new ChangeSet
        {
            GroupId = IngestionFixtures.Group,
            UnitId = unit.UnitId,
            Seq = unit.Seq,
            SnapshotSeq = analysis.SnapshotSeq,
            AnalysisStartedAt = analysis.StartedAt,
            Episodes = [written],
            CreateNodes = createNodes,
            CreateEdges = createEdges,
            NodeUuids = nodeUuids,
            NodeFirstEpisode = nodeFirstEpisode,
            EpisodicEdges =
            [
                .. nodeUuids.Select(uuid => new EpisodicEdge
                {
                    SourceNodeUuid = episode.Uuid,
                    TargetNodeUuid = uuid,
                    GroupId = IngestionFixtures.Group,
                    CreatedAt = IngestionFixtures.T0,
                }.Validate()),
            ],
        };
        await Store.SaveChangeSetAsync(IngestionFixtures.Group, unit.Seq, changeSet.ToBytes());
        return changeSet;
    }
}

/// <summary>
/// The model of the Harness: `AsyncMock(side_effect=AssertionError('unexpected model call'))` with `max_tokens = 32768`.
/// </summary>
// PORT: R4 has no row for AssertionError. The fake throws Xunit.Sdk.XunitException, the failure of a test assert, which
// derives from Exception as AssertionError does, so an `except Exception` of the code under test catches both.
internal sealed class HarnessLlm : LLMClient
{
    internal HarnessLlm()
        : base(new LLMConfig(maxTokens: 32768), false)
    {
    }

    internal override string PythonName => "MagicMock";

    internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default) => throw new Xunit.Sdk.XunitException("unexpected model call");

    internal override Task<OrderedDictionary<string, object?>> GenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        string? groupId = null,
        string? promptName = null,
        bool attributeExtraction = false,
        string? model = null,
        CancellationToken cancellationToken = default) => throw new Xunit.Sdk.XunitException("unexpected model call");
}

/// <summary>The embedder of the Harness: `create_batch` gives X for each name.</summary>
// PORT: `embedder.create` is a MagicMock in Python, and its await raises TypeError. R4 gives InvalidCastException.
internal sealed class HarnessEmbedder : EmbedderClient
{
    internal override Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default) =>
        throw new InvalidCastException("object MagicMock can't be used in 'await' expression");

    internal override Task<List<List<double>>> CreateBatchAsync(List<string> inputDataList, CancellationToken cancellationToken = default)
    {
        List<List<double>> vectors = [.. inputDataList.Select(_ => IngestionFixtures.X)];
        return Task.FromResult(vectors);
    }
}
