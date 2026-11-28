// Ported from tests/ingestion/ingestion_fixtures.py (port/RULES.md, R25): the test doubles of concurrent ingestion.
// Packet P8c of plan 0004, layer 8, and packet P9g of layer 9.
//
// The upstream Harness replaces `phases.add_nodes_and_edges_bulk`, `phases.execute_write_with_timeout`,
// `phases.extract_attributes_from_nodes`, and `EpisodicNode.get_by_uuids` with `monkeypatch.setattr` (open item O10).
// The port runs the real functions on HarnessDriver, a fake at the network boundary whose writes apply to FakeGraph
// (Part 3, the row of `monkeypatch.setattr`): the result of the first two replacements and of `get_by_uuids`. No fake
// at the network boundary gives the result of the replaced `extract_attributes_from_nodes` (it appends `+<episode
// uuid>` to each summary with no model call), so the Harness of the port has no `enrich`, and each upstream test that
// calls `Harness.enrich` keeps a `TODO(port): O10` marker in its test file.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
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

    // PORT: `**values` are the optional parameters of the fields that a test gives. `labels` defaults to ['Entity'].
    internal static EntityNode MakeNode(
        string uuid,
        string name,
        List<double>? embedding = null,
        List<string>? labels = null,
        OrderedDictionary<string, object?>? attributes = null,
        string summary = "") =>
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
            Summary = summary,
        }.Validate();

    // PORT: `**values` are the optional parameters of the fields that a test gives. `name` defaults to 'RELATES' and
    // `episodes` to [].
    internal static EntityEdge MakeEdge(
        string uuid,
        string source,
        string target,
        string fact,
        List<double>? embedding = null,
        List<string>? episodes = null,
        OrderedDictionary<string, object?>? attributes = null,
        PyDateTime? validAt = null,
        PyDateTime? invalidAt = null,
        PyDateTime? expiredAt = null,
        string name = "RELATES") =>
        new EntityEdge
        {
            Uuid = uuid,
            GroupId = Group,
            SourceNodeUuid = source,
            TargetNodeUuid = target,
            Fact = fact,
            Name = name,
            Episodes = episodes ?? [],
            CreatedAt = T0,
            // PORT: `embedding or X`: an empty list is false, so it gives X too.
            FactEmbedding = embedding is { Count: > 0 } ? embedding : X,
            Attributes = attributes ?? new(),
            ValidAt = validAt,
            InvalidAt = invalidAt,
            ExpiredAt = expiredAt,
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
/// An in-memory graph with the reads of `GraphReader` and the writes of the commit.
///
/// A search returns a record when it is not in `hidden`. A read by UUID returns each
/// record that exists. Each read returns a copy.
/// </summary>
// PORT: the reads of FakeGraph derive from GraphReader, as the upstream fake replaces the reader. Each read is an
// `async def` with no await in Python, so it ends with no yield: the port returns a completed task (R8), and it does not
// read the token. The writes are the functions that the upstream Harness puts in place of
// `phases.add_nodes_and_edges_bulk` and `phases.execute_write_with_timeout`. In the port, HarnessDriver gives them the
// queries of one write function (ApplyAsync), and they rebuild each record from its query parameters.
internal sealed class FakeGraph : GraphReader
{
    private static readonly HashSet<string> NodeFields = new(StringComparer.Ordinal) { "uuid", "name", "group_id", "summary", "created_at", "name_embedding", "labels" };

    private static readonly HashSet<string> EdgeFields = new(StringComparer.Ordinal)
    {
        "uuid", "source_node_uuid", "target_node_uuid", "name", "fact", "group_id", "episodes", "created_at", "expired_at",
        "valid_at", "invalid_at", "reference_time", "fact_embedding",
    };

    // PORT: the upstream FakeGraph has no clients. The Harness sets the clients of its GraphitiCore.
    internal FakeGraph()
        : base(null!)
    {
    }

    internal OrderedDictionary<string, EntityNode> Nodes { get; } = new();

    internal OrderedDictionary<string, EntityEdge> Edges { get; } = new();

    internal OrderedDictionary<string, EpisodicNode> Episodes { get; } = new();

    internal OrderedDictionary<(string, string), EpisodicEdge> EpisodicEdges { get; } = new();

    internal HashSet<string> Hidden { get; } = new(StringComparer.Ordinal);

    internal List<string> Log { get; } = [];

    internal List<double?> WriteTimeouts { get; } = [];

    // The number of next writes of each kind that raise an error.
    internal int FailBulkWrites { get; set; }

    internal int FailEdgeUpdates { get; set; }

    internal int FailNodeUpdates { get; set; }

    // When True, a bulk write that raises writes the nodes first.
    internal bool PartialBulkWrite { get; set; }

    internal Action? BeforeEdgeUpdate { get; set; }

    // PORT: a test that replaces `phases.add_nodes_and_edges_bulk` with a function that calls the write of FakeGraph
    // sets this function. It gets the write of FakeGraph as its argument.
    internal Func<Func<Task>, Task>? BulkWrite { get; set; }

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

    // --- reads -------------------------------------------------------------------

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

    // --- writes ------------------------------------------------------------------

    /// <summary>Apply the queries of one write function: a bulk save, an edge update, or a node update.</summary>
    internal async Task ApplyAsync(List<(string Query, OrderedDictionary<string, object?> Parameters)> runs)
    {
        var parameters = new OrderedDictionary<string, object?>();
        foreach (var (_, values) in runs)
        {
            foreach (var (key, value) in values)
            {
                parameters[key] = value;
            }
        }

        if (parameters.TryGetValue("episodes", out var episodes))
        {
            await (BulkWrite ?? (write => write()))(() =>
            {
                AddNodesAndEdgesBulk(
                    Records(episodes).Select(EpisodeFromRecord).ToList(),
                    Records(parameters.GetValueOrDefault("episodic_edges")).Select(EpisodicEdge.ModelValidate).ToList(),
                    Records(parameters.GetValueOrDefault("nodes")).Select(NodeFromRecord).ToList(),
                    Records(parameters.GetValueOrDefault("entity_edges")).Select(EdgeFromRecord).ToList());
                return Task.CompletedTask;
            });
        }
        else if (parameters.TryGetValue("entity_edges", out var edges))
        {
            UpdateEdges(Records(edges).Select(EdgeFromRecord).ToList());
        }
        else if (parameters.TryGetValue("nodes", out var nodes))
        {
            UpdateNodes(Records(nodes).Select(NodeFromRecord).ToList());
        }
        else if (runs.Count > 0)
        {
            throw new Xunit.Sdk.XunitException("unexpected write function");
        }
    }

    private void AddNodesAndEdgesBulk(List<EpisodicNode> episodicNodes, List<EpisodicEdge> episodicEdges, List<EntityNode> entityNodes, List<EntityEdge> entityEdges)
    {
        Log.Add("write:bulk");
        if (FailBulkWrites > 0)
        {
            FailBulkWrites -= 1;
            if (PartialBulkWrite)
            {
                foreach (var node in entityNodes)
                {
                    Nodes[node.Uuid] = node;
                }
            }

            throw new InvalidOperationException("bulk write failed");
        }

        foreach (var episode in episodicNodes)
        {
            Episodes[episode.Uuid] = episode;
        }

        foreach (var node in entityNodes)
        {
            Nodes[node.Uuid] = node;
        }

        foreach (var edge in episodicEdges)
        {
            EpisodicEdges[(edge.SourceNodeUuid, edge.TargetNodeUuid)] = edge;
        }

        foreach (var edge in entityEdges)
        {
            // The save query matches the two endpoint nodes.
            if (Nodes.ContainsKey(edge.SourceNodeUuid) && Nodes.ContainsKey(edge.TargetNodeUuid))
            {
                Edges[edge.Uuid] = edge;
            }
        }
    }

    private void UpdateEdges(List<EntityEdge> records)
    {
        Log.Add("write:edge_update");
        BeforeEdgeUpdate?.Invoke();
        if (FailEdgeUpdates > 0)
        {
            FailEdgeUpdates -= 1;
            throw new InvalidOperationException("edge update failed");
        }

        // The update query creates nothing.
        foreach (var edge in records.Where(edge => Edges.ContainsKey(edge.Uuid)))
        {
            Edges[edge.Uuid] = edge;
        }
    }

    private void UpdateNodes(List<EntityNode> records)
    {
        Log.Add("write:node_update");
        if (FailNodeUpdates > 0)
        {
            FailNodeUpdates -= 1;
            throw new InvalidOperationException("node update failed");
        }

        foreach (var node in records.Where(node => Nodes.ContainsKey(node.Uuid)))
        {
            Nodes[node.Uuid] = node;
        }
    }

    private static List<OrderedDictionary<string, object?>> Records(object? value) =>
        value is null ? [] : [.. ((System.Collections.IEnumerable)value).Cast<OrderedDictionary<string, object?>>()];

    // A copy of a list of the record, so that no later change of the written object changes the graph (`deepcopy`).
    private static List<string> Strings(object? value) => [.. ((System.Collections.IEnumerable)value!).Cast<object?>().Select(item => (string)item!)];

    private static List<double>? Doubles(object? value) =>
        value is null ? null : [.. ((System.Collections.IEnumerable)value).Cast<object?>().Select(item => Convert.ToDouble(item, System.Globalization.CultureInfo.InvariantCulture))];

    // PORT: the record of `dict(episode)` with `source` as its text and no `labels`.
    private static EpisodicNode EpisodeFromRecord(OrderedDictionary<string, object?> record)
    {
        var data = new OrderedDictionary<string, object?>(record) { ["source"] = EpisodeTypeExtensions.FromStr(record["source"]) };
        data["entity_edges"] = Strings(record["entity_edges"]);
        return EpisodicNode.ModelValidate(data);
    }

    // PORT: the record of `_entity_node_record`. A key that is not a field of the node is an attribute. The labels are
    // the labels of the record (`list(set(node.labels + ['Entity']))`).
    private static EntityNode NodeFromRecord(OrderedDictionary<string, object?> record)
    {
        var attributes = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in record)
        {
            if (!NodeFields.Contains(key))
            {
                attributes[key] = value;
            }
        }

        return new EntityNode
        {
            Uuid = (string)record["uuid"]!,
            Name = (string)record["name"]!,
            GroupId = (string)record["group_id"]!,
            Summary = (string)record["summary"]!,
            CreatedAt = (PyDateTime)record["created_at"]!,
            NameEmbedding = Doubles(record["name_embedding"]),
            Labels = Strings(record["labels"]),
            Attributes = attributes,
        }.Validate();
    }

    // PORT: the record of `_entity_edge_record`. A key that is not a field of the edge is an attribute.
    private static EntityEdge EdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        var attributes = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in record)
        {
            if (!EdgeFields.Contains(key))
            {
                attributes[key] = value;
            }
        }

        return new EntityEdge
        {
            Uuid = (string)record["uuid"]!,
            SourceNodeUuid = (string)record["source_node_uuid"]!,
            TargetNodeUuid = (string)record["target_node_uuid"]!,
            Name = (string)record["name"]!,
            Fact = (string)record["fact"]!,
            GroupId = (string)record["group_id"]!,
            Episodes = Strings(record["episodes"]),
            CreatedAt = (PyDateTime)record["created_at"]!,
            ExpiredAt = (PyDateTime?)record["expired_at"],
            ValidAt = (PyDateTime?)record["valid_at"],
            InvalidAt = (PyDateTime?)record["invalid_at"],
            ReferenceTime = (PyDateTime?)record["reference_time"],
            FactEmbedding = Doubles(record["fact_embedding"]),
            Attributes = attributes,
        }.Validate();
    }
}

/// <summary>An in-memory store that adds each call to the log of the graph.</summary>
// PORT: InMemoryIngestionStateStore is sealed in the port, so the logging store wraps one and forwards each call. A test
// that replaces `h.store` with a different store gives that store as `inner`. A test that replaces a method of the store
// with `monkeypatch.setattr` sets the hook of that method: the state store is the storage boundary of the host (R25).
internal sealed class LoggingStore(FakeGraph graph, Func<double>? clock = null, IngestionStateStore? inner = null) : IngestionStateStore
{
    internal IngestionStateStore Inner { get; } = inner ?? new InMemoryIngestionStateStore(clock);

    /// <summary>`_groups` of the in-memory store.</summary>
    internal Dictionary<string, Group> Groups => ((InMemoryIngestionStateStore)Inner).groups;

    /// <summary>The methods that a test replaced with `AsyncMock(side_effect=AssertionError(f'{method} was called'))`.</summary>
    internal HashSet<string> Forbidden { get; } = new(StringComparer.Ordinal);

    /// <summary>The replacement of `begin_admit`, with the method of the inner store as its first argument.</summary>
    internal Func<Func<Task<Admission?>>, string, string, double, Task<Admission?>>? BeginAdmit { get; set; }

    /// <summary>The replacement of `heartbeat`: `AsyncMock(side_effect=...)`.</summary>
    internal Exception? HeartbeatError { get; set; }

    /// <summary>The replacement of `save_final`: a function that runs before the method of the inner store.</summary>
    internal Action? BeforeSaveFinal { get; set; }

    /// <summary>The number of calls of `begin_admit` (`AsyncMock(wraps=store.begin_admit)`).</summary>
    internal int BeginAdmitCount { get; private set; }

    internal override Task<Admission?> BeginAdmitAsync(string groupId, string unitId, double ttl, CancellationToken cancellationToken = default)
    {
        BeginAdmitCount++;
        return BeginAdmit is null
            ? Inner.BeginAdmitAsync(groupId, unitId, ttl, cancellationToken)
            : BeginAdmit(() => Inner.BeginAdmitAsync(groupId, unitId, ttl, cancellationToken), groupId, unitId, ttl);
    }

    internal override Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default) =>
        Check("end_admit") ?? Inner.EndAdmitAsync(groupId, seq, cancellationToken);

    internal override Task<AnalysisStart> StartAnalysisAsync(string groupId, long seq, CancellationToken cancellationToken = default) =>
        Forbidden.Contains("start_analysis")
            ? Task.FromException<AnalysisStart>(new Xunit.Sdk.XunitException("start_analysis was called"))
            : Inner.StartAnalysisAsync(groupId, seq, cancellationToken);

    internal override Task SaveChangeSetAsync(string groupId, long seq, byte[] payload, CancellationToken cancellationToken = default) =>
        Check("save_change_set") ?? Inner.SaveChangeSetAsync(groupId, seq, payload, cancellationToken);

    internal override Task HeartbeatAsync(string groupId, long seq, double ttl, long? fence = null, CancellationToken cancellationToken = default)
    {
        if (HeartbeatError is not null)
        {
            return Task.FromException(HeartbeatError);
        }

        return Check("heartbeat") ?? Inner.HeartbeatAsync(groupId, seq, ttl, fence, cancellationToken);
    }

    internal override Task<BeginCommitResult> BeginCommitAsync(string groupId, long seq, double ttl, CancellationToken cancellationToken = default) =>
        Inner.BeginCommitAsync(groupId, seq, ttl, cancellationToken);

    internal override Task<List<DeltaUnit>> LoadDeltaAsync(string groupId, long afterSeq, long beforeSeq, double since, CancellationToken cancellationToken = default) =>
        Inner.LoadDeltaAsync(groupId, afterSeq, beforeSeq, since, cancellationToken);

    internal override Task SaveFinalAsync(string groupId, long seq, long fence, byte[] payload, CancellationToken cancellationToken = default)
    {
        graph.Log.Add("store:save_final");
        BeforeSaveFinal?.Invoke();
        return Inner.SaveFinalAsync(groupId, seq, fence, payload, cancellationToken);
    }

    internal override Task FinishCommitAsync(string groupId, long seq, long fence, List<string> nodeUuids, double retainSeconds, CancellationToken cancellationToken = default)
    {
        graph.Log.Add("store:finish_commit");
        return Inner.FinishCommitAsync(groupId, seq, fence, nodeUuids, retainSeconds, cancellationToken);
    }

    internal override Task FailAsync(string groupId, long seq, string reason, long? fence = null, CancellationToken cancellationToken = default) =>
        Check("fail") ?? Inner.FailAsync(groupId, seq, reason, fence, cancellationToken);

    internal override Task<List<string>> ReadyEnrichAsync(string groupId, long seq, CancellationToken cancellationToken = default) =>
        Inner.ReadyEnrichAsync(groupId, seq, cancellationToken);

    internal override Task FinishEnrichAsync(string groupId, long seq, List<string> nodeUuids, CancellationToken cancellationToken = default) =>
        Inner.FinishEnrichAsync(groupId, seq, nodeUuids, cancellationToken);

    internal override Task CloseAsync(CancellationToken cancellationToken = default) => Inner.CloseAsync(cancellationToken);

    // A replaced method raises AssertionError at its await (R4 has no row: Xunit.Sdk.XunitException, as HarnessLlm).
    private Task? Check(string method) =>
        Forbidden.Contains(method) ? Task.FromException(new Xunit.Sdk.XunitException($"{method} was called")) : null;
}

/// <summary>A tracer that keeps each span.</summary>
// PORT: RecordingTracer of the fixtures. LlmClientDecisionSupport.cs has a RecordingTracer and a RecordingSpan of its
// own, so the port names this one IngestionTracer.
internal sealed class IngestionTracer : Tracer
{
    internal List<RecordingSpan> Spans { get; } = [];

    internal override async Task<T> StartSpanAsync<T>(string name, Func<TracerSpan, Task<T>> body, CancellationToken cancellationToken = default)
    {
        var span = new RecordingSpan(name);
        Spans.Add(span);
        return await body(span);
    }

    internal List<RecordingSpan> Named(string name) => [.. Spans.Where(span => span.Name == name)];

    /// <summary>A recording span of the fixtures.</summary>
    internal sealed class RecordingSpan(string name) : TracerSpan
    {
        internal string Name { get; } = name;

        internal OrderedDictionary<string, object?> Attributes { get; } = new();

        internal string? Status { get; private set; }

        internal override void AddAttributes(OrderedDictionary<string, object?> attributes)
        {
            foreach (var (key, value) in attributes)
            {
                Attributes[key] = value;
            }
        }

        internal override void SetStatus(string status, string? description = null) => Status = status;

        internal override void RecordException(Exception exception) => Status = "error";
    }
}

/// <summary>
/// The driver of the Harness: the network boundary of the graph writes and of the episode reads.
/// </summary>
// PORT: the upstream Harness replaces `phases.add_nodes_and_edges_bulk`, `phases.execute_write_with_timeout`, and
// `EpisodicNode.get_by_uuids` with the methods of FakeGraph, and `graphiti.retrieve_episodes` with
// `AsyncMock(return_value=[])` (open item O10). The port runs the real functions on this driver (Part 3, the row of
// `monkeypatch.setattr`). Its session gives the queries of one write function to FakeGraph, which applies them as the
// replaced function applies its records: the result of the replaced functions. A read of episodes by UUID returns the
// episodes of FakeGraph, and a read of the previous episodes (the parameter `reference_time`) returns no record. Each
// other query raises the error of an await of a MagicMock.
internal sealed class HarnessDriver : GraphDriver
{
    private readonly FakeGraph graph;

    internal HarnessDriver(FakeGraph graph)
    {
        this.graph = graph;
        Provider = GraphProvider.Neo4j;
        // PORT: `_resolve_request_scope` of the Harness returns its driver for each group. GraphitiCore returns its own
        // driver for the group of its database.
        Database = IngestionFixtures.Group;
    }

    internal override string PythonName => "MagicMock";

    internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
        string cypherQuery,
        OrderedDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        List<OrderedDictionary<string, object?>> records;
        if (cypherQuery.Contains("(e:Episodic)", StringComparison.Ordinal) && parameters.GetValueOrDefault("uuids") is List<string> uuids)
        {
            var groupId = parameters.GetValueOrDefault("group_id") as string;
            records =
            [
                .. uuids.Where(graph.Episodes.ContainsKey)
                    .Select(uuid => graph.Episodes[uuid])
                    .Where(episode => groupId is null || episode.GroupId == groupId)
                    .Select(EpisodeRecord),
            ];
        }
        else if (parameters.ContainsKey("reference_time"))
        {
            records = [];
        }
        else
        {
            throw new InvalidCastException("object MagicMock can't be used in 'await' expression");
        }

        return Task.FromResult<(List<OrderedDictionary<string, object?>>, object?, object?)>((records, null, null));
    }

    internal override GraphDriverSession Session(string? database = null) => new HarnessSession(graph);

    internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidCastException("object MagicMock can't be used in 'await' expression");

    internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
        throw new InvalidCastException("object MagicMock can't be used in 'await' expression");

    internal override GraphDriver Clone(string database) =>
        throw new InvalidOperationException("the Harness has one driver");

    private static OrderedDictionary<string, object?> EpisodeRecord(EpisodicNode episode) => new()
    {
        ["content"] = episode.Content,
        ["created_at"] = episode.CreatedAt,
        ["valid_at"] = episode.ValidAt,
        ["uuid"] = episode.Uuid,
        ["group_id"] = episode.GroupId,
        ["source"] = Py.Str(Py.EnumValue(episode.Source)),
        ["name"] = episode.Name,
        ["source_description"] = episode.SourceDescription,
        ["entity_edges"] = new List<string>(episode.EntityEdges),
    };

    private sealed class HarnessSession(FakeGraph graph) : GraphDriverSession
    {
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            throw new InvalidCastException("object MagicMock can't be used in 'await' expression");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, CancellationToken cancellationToken = default) =>
            WriteAsync(func, null);

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, double timeout, CancellationToken cancellationToken = default) =>
            WriteAsync(func, timeout);

        private async Task WriteAsync(Func<Transaction, Task> func, double? timeout)
        {
            graph.WriteTimeouts.Add(timeout);
            var tx = new HarnessTx();
            await func(tx);
            await graph.ApplyAsync(tx.Runs);
        }
    }

    private sealed class HarnessTx : Transaction
    {
        internal List<(string Query, OrderedDictionary<string, object?> Parameters)> Runs { get; } = [];

        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            Runs.Add((query, parameters));
            return Task.CompletedTask;
        }
    }
}

/// <summary>A fake graph, a store, a fake `Graphiti`, and functions that make units.</summary>
internal sealed class Harness
{
    internal Harness(
        FakeClock? clock = null,
        double leaseSeconds = 60.0,
        double writeMarginSeconds = 5.0,
        long maxApplyAttempts = 5,
        double indexLagSeconds = 300.0,
        Func<ChangeSet, CancellationToken, Task<ChangeSet>>? beforeCommitWrite = null,
        Func<List<EntityNode>, AdmittedUnit, CancellationToken, Task<List<EntityNode>>>? beforeEnrichmentWrite = null)
    {
        Graph = new FakeGraph();
        Clock = clock;
        Store = new LoggingStore(Graph, clock is null ? null : clock.Call);
        Config = new ConcurrentIngestionConfig
        {
            Window = 4,
            StateStore = Store,
            PollIntervalSeconds = 0.001,
            LeaseSeconds = leaseSeconds,
            WriteMarginSeconds = writeMarginSeconds,
            MaxApplyAttempts = maxApplyAttempts,
            IndexLagSeconds = indexLagSeconds,
            BeforeCommitWrite = beforeCommitWrite,
            BeforeEnrichmentWrite = beforeEnrichmentWrite,
        }.Validate();
        Tracer = new IngestionTracer();
        Llm = new HarnessLlm();
        Driver = new HarnessDriver(Graph);

        // PORT: the fake `Graphiti` of the Harness is a SimpleNamespace with `_resolve_request_scope`, `tracer`,
        // `store_raw_episode_content`, `max_coroutines`, `clients`, and `retrieve_episodes`. GraphitiCore is sealed, so
        // the port builds a real one on the clients of the Harness and gives it the recording tracer. Its
        // `_resolve_request_scope` returns these clients for the group of the Harness, and its `retrieve_episodes` reads
        // no record from the driver of the Harness: the result of `AsyncMock(return_value=[])`.
        Graphiti = new GraphitiCore(
            llmClient: Llm,
            embedder: new HarnessEmbedder(),
            crossEncoder: new HarnessCrossEncoder(),
            storeRawEpisodeContent: true,
            graphDriver: Driver,
            maxCoroutines: null);
        Graphiti.Tracer = Tracer;
        Graphiti.Clients.Tracer = Tracer;
        Clients = Graphiti.Clients;
        Graph.Clients = Clients;
    }

    internal GraphitiClients Clients { get; }

    // PORT: the functions of the Harness have no token parameter, as in Python. They pass the token of the test.
    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    internal FakeGraph Graph { get; }

    internal FakeClock? Clock { get; }

    internal LoggingStore Store { get; set; }

    internal ConcurrentIngestionConfig Config { get; }

    internal IngestionTracer Tracer { get; }

    internal HarnessLlm Llm { get; }

    internal HarnessDriver Driver { get; }

    internal GraphitiCore Graphiti { get; }

    /// <summary>Admit a unit with store calls only.</summary>
    internal async Task<AdmittedUnit> AdmitAsync(string unitId, EpisodicNode? episode = null)
    {
        var admission = await Store.BeginAdmitAsync(IngestionFixtures.Group, unitId, Config.LeaseSeconds, Token);
        Assert.NotNull(admission);
        await Store.EndAdmitAsync(IngestionFixtures.Group, admission.Seq, Token);
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
    // PORT: `**fields` are the optional parameters, one for each field of ChangeSet that a test gives. A field that a
    // test does not give is the default of ChangeSet, and `setdefault` gives node_uuids, node_first_episode, and
    // episodic_edges.
    internal async Task<ChangeSet> AnalyzeAsync(
        AdmittedUnit unit,
        List<EntityNode>? createNodes = null,
        List<EntityEdge>? createEdges = null,
        List<Operation>? operations = null,
        List<string>? nodeUuids = null,
        OrderedDictionary<string, string>? nodeFirstEpisode = null,
        List<EpisodicEdge>? episodicEdges = null,
        OrderedDictionary<string, List<string>>? seenNodeCandidates = null,
        OrderedDictionary<string, List<string>>? seenNodeLabels = null,
        OrderedDictionary<string, List<string>>? seenDuplicateCandidates = null,
        OrderedDictionary<string, List<string>>? seenInvalidationCandidates = null,
        List<string>? edgesEndedByCandidate = null)
    {
        var analysis = await Store.StartAnalysisAsync(IngestionFixtures.Group, unit.Seq, Token);
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

        nodeUuids ??= [.. createNodes.Select(node => node.Uuid)];
        if (nodeFirstEpisode is null)
        {
            nodeFirstEpisode = new OrderedDictionary<string, string>();
            foreach (var uuid in nodeUuids)
            {
                nodeFirstEpisode[uuid] = episode.Uuid;
            }
        }

        episodicEdges ??=
        [
            .. nodeUuids.Select(uuid => new EpisodicEdge
            {
                SourceNodeUuid = episode.Uuid,
                TargetNodeUuid = uuid,
                GroupId = IngestionFixtures.Group,
                CreatedAt = IngestionFixtures.T0,
            }.Validate()),
        ];
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
            EpisodicEdges = episodicEdges,
            Operations = operations ?? [],
            NodeUuids = nodeUuids,
            NodeFirstEpisode = nodeFirstEpisode,
            SeenNodeCandidates = seenNodeCandidates ?? new(),
            SeenNodeLabels = seenNodeLabels ?? new(),
            SeenDuplicateCandidates = seenDuplicateCandidates ?? new(),
            SeenInvalidationCandidates = seenInvalidationCandidates ?? new(),
            EdgesEndedByCandidate = edgesEndedByCandidate ?? [],
        };
        await Store.SaveChangeSetAsync(IngestionFixtures.Group, unit.Seq, changeSet.ToBytes(), Token);
        return changeSet;
    }

    internal Task<CommitResult> CommitAsync(
        AdmittedUnit unit,
        ChangeSet changeSet,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        Func<CancellationToken, Task>? wait = null,
        UnitLease? lease = null,
        GraphReader? reader = null,
        CancellationToken? cancellationToken = null) =>
        Phases.CommitUnitAsync(
            Graphiti,
            Store,
            Config,
            unit,
            changeSet,
            entityTypes: entityTypes,
            identityProperties: identityProperties,
            wait: wait,
            lease: lease,
            reader: reader ?? Graph,
            cancellationToken: cancellationToken ?? Token);

    /// <summary>Admit, analyze, and commit one unit.</summary>
    internal async Task<(AdmittedUnit Unit, CommitResult Result)> RunAsync(
        string unitId,
        List<EntityNode>? createNodes = null,
        List<EntityEdge>? createEdges = null,
        List<Operation>? operations = null,
        List<string>? nodeUuids = null)
    {
        var unit = await AdmitAsync(unitId);
        var changeSet = await AnalyzeAsync(unit, createNodes: createNodes, createEdges: createEdges, operations: operations, nodeUuids: nodeUuids);
        var result = await CommitAsync(unit, changeSet);
        return (unit, result);
    }

    /// <summary>`h.llm.side_effect = None` and `h.llm.return_value = response`.</summary>
    internal void UseLlm(string response) => Llm.Response = (OrderedDictionary<string, object?>)PyJson.Loads(response)!;
}

/// <summary>
/// The model of the Harness: `AsyncMock(side_effect=AssertionError('unexpected model call'))` with `max_tokens = 32768`.
/// </summary>
// PORT: R4 has no row for AssertionError. The fake throws Xunit.Sdk.XunitException, the failure of a test assert, which
// derives from Exception as AssertionError does, so an `except Exception` of the code under test catches both. A test
// that gives `return_value` sets Response. AwaitCount is `await_count` of the mock.
internal sealed class HarnessLlm : LLMClient
{
    internal HarnessLlm()
        : base(new LLMConfig(maxTokens: 32768), false)
    {
    }

    internal override string PythonName => "MagicMock";

    internal OrderedDictionary<string, object?>? Response { get; set; }

    internal Exception? Error { get; set; }

    internal int AwaitCount { get; private set; }

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
        CancellationToken cancellationToken = default)
    {
        AwaitCount++;
        if (Error is not null)
        {
            return Task.FromException<OrderedDictionary<string, object?>>(Error);
        }

        return Response is null
            ? Task.FromException<OrderedDictionary<string, object?>>(new Xunit.Sdk.XunitException("unexpected model call"))
            : Task.FromResult(Response);
    }
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

/// <summary>The cross-encoder of the Harness: a MagicMock, whose await raises TypeError (R4: InvalidCastException).</summary>
internal sealed class HarnessCrossEncoder : CrossEncoderClient
{
    internal override Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default) =>
        throw new InvalidCastException("object MagicMock can't be used in 'await' expression");
}
