// Ported from graphiti_core/ingestion/change_set.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core.Ingestion;

using Graphiti.Core.Utils;
using Graphiti.PyCompat;

/// <summary>Add episode UUIDs to the `episodes` list of a stored edge.</summary>
// PORT: `op: Literal[...]` is `string`, with a test in Validate() (R7).
internal sealed record AddEpisodesToEdge : EdgeOperation, IPydanticModel
{
    internal string Op { get; set; } = "add_episodes_to_edge";

    internal required string EdgeUuid { get; set; }

    internal required List<string> EpisodeUuids { get; set; }

    internal AddEpisodesToEdge Validate()
    {
        // PORT: the Literal test of `op` (R7). The typed property holds any text, so the reader of the field runs here.
        var f = new PydanticFields("AddEpisodesToEdge", new() { ["op"] = Op });
        f.Required("op", v => Pydantic.Literal(v, "add_episodes_to_edge"));
        f.ThrowIfAny();
        return this;
    }

    internal static AddEpisodesToEdge ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("AddEpisodesToEdge", data);
        var op = f.Default("op", v => Pydantic.Literal(v, "add_episodes_to_edge"), () => "add_episodes_to_edge");
        var edgeUuid = f.Required("edge_uuid", Pydantic.Str);
        var episodeUuids = f.Required("episode_uuids", v => Pydantic.List(v, Pydantic.Str));
        f.ThrowIfAny();
        return new AddEpisodesToEdge { Op = op.Value, EdgeUuid = edgeUuid.Value, EpisodeUuids = episodeUuids.Value }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("op", Op),
        ("edge_uuid", EdgeUuid),
        ("episode_uuids", EpisodeUuids));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Set the end dates of a stored edge.</summary>
internal sealed record EndEdge : EdgeOperation, IPydanticModel
{
    internal string Op { get; set; } = "end_edge";

    internal required string EdgeUuid { get; set; }

    internal PyDateTime? InvalidAt { get; set; }

    internal required PyDateTime ExpiredAt { get; set; }

    internal bool OnlyIfOpen { get; set; }

    internal EndEdge Validate()
    {
        // PORT: the Literal test of `op` (R7).
        var f = new PydanticFields("EndEdge", new() { ["op"] = Op });
        f.Required("op", v => Pydantic.Literal(v, "end_edge"));
        f.ThrowIfAny();
        return this;
    }

    internal static EndEdge ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("EndEdge", data);
        var op = f.Default("op", v => Pydantic.Literal(v, "end_edge"), () => "end_edge");
        var edgeUuid = f.Required("edge_uuid", Pydantic.Str);
        var invalidAt = f.Default("invalid_at", v => Pydantic.OptionalValue(v, Pydantic.DateTime), () => null);
        var expiredAt = f.Required("expired_at", Pydantic.DateTime);
        var onlyIfOpen = f.Default("only_if_open", Pydantic.Bool, () => false);
        f.ThrowIfAny();
        return new EndEdge
        {
            Op = op.Value,
            EdgeUuid = edgeUuid.Value,
            InvalidAt = invalidAt.Value,
            ExpiredAt = expiredAt.Value,
            OnlyIfOpen = onlyIfOpen.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("op", Op),
        ("edge_uuid", EdgeUuid),
        ("invalid_at", InvalidAt),
        ("expired_at", ExpiredAt),
        ("only_if_open", OnlyIfOpen));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>
/// Set the attributes of a stored edge with a merge by key.
///
/// `base_attributes` is the map that the analysis read. `attributes` is the map that
/// the analysis computed.
/// </summary>
internal sealed record SetEdgeAttributes : EdgeOperation, IPydanticModel
{
    internal string Op { get; set; } = "set_edge_attributes";

    internal required string EdgeUuid { get; set; }

    internal OrderedDictionary<string, object?> BaseAttributes { get; set; } = new();

    internal OrderedDictionary<string, object?> Attributes { get; set; } = new();

    internal SetEdgeAttributes Validate()
    {
        // PORT: the Literal test of `op` (R7).
        var f = new PydanticFields("SetEdgeAttributes", new() { ["op"] = Op });
        f.Required("op", v => Pydantic.Literal(v, "set_edge_attributes"));
        f.ThrowIfAny();
        return this;
    }

    internal static SetEdgeAttributes ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SetEdgeAttributes", data);
        var op = f.Default("op", v => Pydantic.Literal(v, "set_edge_attributes"), () => "set_edge_attributes");
        var edgeUuid = f.Required("edge_uuid", Pydantic.Str);
        var baseAttributes = f.Default("base_attributes", v => Pydantic.Dict(v, Pydantic.Any), () => new());
        var attributes = f.Default("attributes", v => Pydantic.Dict(v, Pydantic.Any), () => new());
        f.ThrowIfAny();
        return new SetEdgeAttributes
        {
            Op = op.Value,
            EdgeUuid = edgeUuid.Value,
            BaseAttributes = baseAttributes.Value,
            Attributes = attributes.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("op", Op),
        ("edge_uuid", EdgeUuid),
        ("base_attributes", BaseAttributes),
        ("attributes", Attributes));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>Overlay the labels and the attributes of an extracted node on a stored node.</summary>
internal sealed record PatchNode : Operation, IPydanticModel
{
    internal string Op { get; set; } = "patch_node";

    internal required string NodeUuid { get; set; }

    internal List<string> Labels { get; set; } = [];

    internal OrderedDictionary<string, object?> Attributes { get; set; } = new();

    internal PatchNode Validate()
    {
        // PORT: the Literal test of `op` (R7).
        var f = new PydanticFields("PatchNode", new() { ["op"] = Op });
        f.Required("op", v => Pydantic.Literal(v, "patch_node"));
        f.ThrowIfAny();
        return this;
    }

    internal static PatchNode ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("PatchNode", data);
        var op = f.Default("op", v => Pydantic.Literal(v, "patch_node"), () => "patch_node");
        var nodeUuid = f.Required("node_uuid", Pydantic.Str);
        var labels = f.Default("labels", v => Pydantic.List(v, Pydantic.Str), () => []);
        var attributes = f.Default("attributes", v => Pydantic.Dict(v, Pydantic.Any), () => new());
        f.ThrowIfAny();
        return new PatchNode
        {
            Op = op.Value,
            NodeUuid = nodeUuid.Value,
            Labels = labels.Value,
            Attributes = attributes.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("op", Op),
        ("node_uuid", NodeUuid),
        ("labels", Labels),
        ("attributes", Attributes));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

// PORT: `EdgeOperation = AddEpisodesToEdge | EndEdge | SetEdgeAttributes` is a union of three models (Part 3). It is an
// abstract record with one derived record for each case. Each case of EdgeOperation is also a case of Operation, so
// EdgeOperation derives from Operation (R2, the row for a type alias).
internal abstract record EdgeOperation : Operation;

// PORT: `Operation = Annotated[... | PatchNode, Field(discriminator='op')]` is an abstract record with one derived
// record for each case (R7).
internal abstract record Operation
{
    // PORT: the reader of the discriminated union (R7): the key `op` names the case. The reader takes the field value,
    // so that an Operation instance passes as it is, as pydantic keeps a model instance of the union.
    internal static Operation ModelValidate(object? value) => Pydantic.Tagged<Operation>(
        value,
        "op",
        ("add_episodes_to_edge", AddEpisodesToEdge.ModelValidate),
        ("end_edge", EndEdge.ModelValidate),
        ("set_edge_attributes", SetEdgeAttributes.ModelValidate),
        ("patch_node", PatchNode.ModelValidate));
}

/// <summary>The saga records that the commit writes for one episode.</summary>
internal sealed record SagaLink : IPydanticModel
{
    internal required string SagaUuid { get; set; }

    internal required string EpisodeUuid { get; set; }

    internal string? PreviousEpisodeUuid { get; set; }

    internal required string HasEpisodeEdgeUuid { get; set; }

    internal string? NextEpisodeEdgeUuid { get; set; }

    internal required PyDateTime CreatedAt { get; set; }

    internal SagaLink Validate() => this;

    internal static SagaLink ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("SagaLink", data);
        var sagaUuid = f.Required("saga_uuid", Pydantic.Str);
        var episodeUuid = f.Required("episode_uuid", Pydantic.Str);
        var previousEpisodeUuid = f.Default("previous_episode_uuid", v => Pydantic.Optional(v, Pydantic.Str), () => null);
        var hasEpisodeEdgeUuid = f.Required("has_episode_edge_uuid", Pydantic.Str);
        var nextEpisodeEdgeUuid = f.Default("next_episode_edge_uuid", v => Pydantic.Optional(v, Pydantic.Str), () => null);
        var createdAt = f.Required("created_at", Pydantic.DateTime);
        f.ThrowIfAny();
        return new SagaLink
        {
            SagaUuid = sagaUuid.Value,
            EpisodeUuid = episodeUuid.Value,
            PreviousEpisodeUuid = previousEpisodeUuid.Value,
            HasEpisodeEdgeUuid = hasEpisodeEdgeUuid.Value,
            NextEpisodeEdgeUuid = nextEpisodeEdgeUuid.Value,
            CreatedAt = createdAt.Value,
        }.Validate();
    }

    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("saga_uuid", SagaUuid),
        ("episode_uuid", EpisodeUuid),
        ("previous_episode_uuid", PreviousEpisodeUuid),
        ("has_episode_edge_uuid", HasEpisodeEdgeUuid),
        ("next_episode_edge_uuid", NextEpisodeEdgeUuid),
        ("created_at", CreatedAt));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);
}

/// <summary>
/// The records to create and the operations on stored records of one ingestion unit.
///
/// A change set contains no copy of a stored node or of a stored edge. A reference to
/// a stored record is its UUID.
/// </summary>
internal sealed record ChangeSet : IPydanticModel
{
    internal required string GroupId { get; set; }

    internal required string UnitId { get; set; }

    internal required long Seq { get; set; }

    internal required long SnapshotSeq { get; set; }

    internal required double AnalysisStartedAt { get; set; }

    internal List<EpisodicNode> Episodes { get; set; } = [];

    internal List<EntityNode> CreateNodes { get; set; } = [];

    internal List<EntityEdge> CreateEdges { get; set; } = [];

    internal List<EpisodicEdge> EpisodicEdges { get; set; } = [];

    internal List<Operation> Operations { get; set; } = [];

    internal List<string> NodeUuids { get; set; } = [];

    internal OrderedDictionary<string, string> NodeFirstEpisode { get; set; } = new();

    internal OrderedDictionary<string, List<string>> SeenNodeCandidates { get; set; } = new();

    // The labels of each node candidate at the time when the analysis read it.
    internal OrderedDictionary<string, List<string>> SeenNodeLabels { get; set; } = new();

    internal OrderedDictionary<string, List<string>> SeenDuplicateCandidates { get; set; } = new();

    internal OrderedDictionary<string, List<string>> SeenInvalidationCandidates { get; set; } = new();

    // The created edges whose end date is the start of a contradiction that the
    // analysis saw. An end date that the episode gave is not in this list.
    internal List<string> EdgesEndedByCandidate { get; set; } = [];

    internal OrderedDictionary<string, string> UuidMap { get; set; } = new();

    internal List<SagaLink> SagaLinks { get; set; } = [];

    // The saga node of `saga_links`, as the commit read it or made it. With
    // `create_saga`, the node is new and the apply of the commit writes it first.
    internal SagaNode? SagaNode { get; set; }

    internal bool CreateSaga { get; set; }

    internal ChangeSet Validate() => this;

    // PORT: from_bytes calls model_validate_json (R7).
    internal static ChangeSet ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("ChangeSet", data);
        var groupId = f.Required("group_id", Pydantic.Str);
        var unitId = f.Required("unit_id", Pydantic.Str);
        var seq = f.Required("seq", Pydantic.Int);
        var snapshotSeq = f.Required("snapshot_seq", Pydantic.Int);
        var analysisStartedAt = f.Required("analysis_started_at", Pydantic.Float);
        var episodes = f.Default("episodes", v => Pydantic.List(v, x => Pydantic.Model(x, "EpisodicNode", EpisodicNode.ModelValidate)), () => []);
        var createNodes = f.Default("create_nodes", v => Pydantic.List(v, x => Pydantic.Model(x, "EntityNode", EntityNode.ModelValidate)), () => []);
        var createEdges = f.Default("create_edges", v => Pydantic.List(v, x => Pydantic.Model(x, "EntityEdge", EntityEdge.ModelValidate)), () => []);
        var episodicEdges = f.Default("episodic_edges", v => Pydantic.List(v, x => Pydantic.Model(x, "EpisodicEdge", EpisodicEdge.ModelValidate)), () => []);
        var operations = f.Default("operations", v => Pydantic.List(v, Operation.ModelValidate), () => []);
        var nodeUuids = f.Default("node_uuids", v => Pydantic.List(v, Pydantic.Str), () => []);
        var nodeFirstEpisode = f.Default("node_first_episode", v => Pydantic.Dict(v, Pydantic.Str), () => new());
        var seenNodeCandidates = f.Default("seen_node_candidates", v => Pydantic.Dict(v, x => Pydantic.List(x, Pydantic.Str)), () => new());
        var seenNodeLabels = f.Default("seen_node_labels", v => Pydantic.Dict(v, x => Pydantic.List(x, Pydantic.Str)), () => new());
        var seenDuplicateCandidates = f.Default("seen_duplicate_candidates", v => Pydantic.Dict(v, x => Pydantic.List(x, Pydantic.Str)), () => new());
        var seenInvalidationCandidates = f.Default("seen_invalidation_candidates", v => Pydantic.Dict(v, x => Pydantic.List(x, Pydantic.Str)), () => new());
        var edgesEndedByCandidate = f.Default("edges_ended_by_candidate", v => Pydantic.List(v, Pydantic.Str), () => []);
        var uuidMap = f.Default("uuid_map", v => Pydantic.Dict(v, Pydantic.Str), () => new());
        var sagaLinks = f.Default("saga_links", v => Pydantic.List(v, x => Pydantic.Model(x, "SagaLink", SagaLink.ModelValidate)), () => []);
        var sagaNode = f.Default("saga_node", v => Pydantic.Optional(v, x => Pydantic.Model(x, "SagaNode", SagaNode.ModelValidate)), () => null);
        var createSaga = f.Default("create_saga", Pydantic.Bool, () => false);
        f.ThrowIfAny();
        return new ChangeSet
        {
            GroupId = groupId.Value,
            UnitId = unitId.Value,
            Seq = seq.Value,
            SnapshotSeq = snapshotSeq.Value,
            AnalysisStartedAt = analysisStartedAt.Value,
            Episodes = episodes.Value,
            CreateNodes = createNodes.Value,
            CreateEdges = createEdges.Value,
            EpisodicEdges = episodicEdges.Value,
            Operations = operations.Value,
            NodeUuids = nodeUuids.Value,
            NodeFirstEpisode = nodeFirstEpisode.Value,
            SeenNodeCandidates = seenNodeCandidates.Value,
            SeenNodeLabels = seenNodeLabels.Value,
            SeenDuplicateCandidates = seenDuplicateCandidates.Value,
            SeenInvalidationCandidates = seenInvalidationCandidates.Value,
            EdgesEndedByCandidate = edgesEndedByCandidate.Value,
            UuidMap = uuidMap.Value,
            SagaLinks = sagaLinks.Value,
            SagaNode = sagaNode.Value,
            CreateSaga = createSaga.Value,
        }.Validate();
    }

    // PORT: to_bytes calls model_dump_json (R7).
    internal OrderedDictionary<string, object?> ModelDump(bool json = false) => Pydantic.DumpFields(
        json,
        ("group_id", GroupId),
        ("unit_id", UnitId),
        ("seq", Seq),
        ("snapshot_seq", SnapshotSeq),
        ("analysis_started_at", AnalysisStartedAt),
        ("episodes", Episodes),
        ("create_nodes", CreateNodes),
        ("create_edges", CreateEdges),
        ("episodic_edges", EpisodicEdges),
        ("operations", Operations),
        ("node_uuids", NodeUuids),
        ("node_first_episode", NodeFirstEpisode),
        ("seen_node_candidates", SeenNodeCandidates),
        ("seen_node_labels", SeenNodeLabels),
        ("seen_duplicate_candidates", SeenDuplicateCandidates),
        ("seen_invalidation_candidates", SeenInvalidationCandidates),
        ("edges_ended_by_candidate", EdgesEndedByCandidate),
        ("uuid_map", UuidMap),
        ("saga_links", SagaLinks),
        ("saga_node", SagaNode),
        ("create_saga", CreateSaga));

    OrderedDictionary<string, object?> IPydanticModel.ModelDump(bool json) => ModelDump(json);

    // PORT: rebase_change_set calls model_copy(deep=True) (ingestion/phases.py:1221, R7). With `deep`, each list and
    // dict is a new object, and each model in it is a deep copy. A model that the change set holds two times is copied
    // one time, as the memo of copy.deepcopy does (R7).
    internal ChangeSet ModelCopy(bool deep = false)
    {
        var copy = this with { };
        if (deep)
        {
            var memo = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            T Memo<T>(T item, Func<T, T> copyItem)
                where T : class
            {
                if (memo.TryGetValue(item, out var done))
                {
                    return (T)done;
                }

                var made = copyItem(item);
                memo[item] = made;
                return made;
            }

            copy.Episodes = [.. Episodes.Select(x => Memo(x, n => n.ModelCopy(deep: true)))];
            copy.CreateNodes = [.. CreateNodes.Select(x => Memo(x, n => n.ModelCopy(deep: true)))];
            copy.CreateEdges = [.. CreateEdges.Select(x => Memo(x, e => e.ModelCopy(deep: true)))];
            copy.EpisodicEdges = [.. EpisodicEdges.Select(x => Memo(x, e => e.ModelCopy(deep: true)))];
            copy.Operations = [.. Operations.Select(x => Memo(x, o => o switch
            {
                AddEpisodesToEdge a => a with { EpisodeUuids = [.. a.EpisodeUuids] },
                EndEdge e => e with { },
                SetEdgeAttributes s => s with { BaseAttributes = Py.AsDict(Py.DeepCopy(s.BaseAttributes)), Attributes = Py.AsDict(Py.DeepCopy(s.Attributes)) },
                PatchNode p => p with { Labels = [.. p.Labels], Attributes = Py.AsDict(Py.DeepCopy(p.Attributes)) },
                _ => throw new InvalidOperationException("unknown operation"),
            }))];
            copy.NodeUuids = [.. NodeUuids];
            copy.NodeFirstEpisode = new(NodeFirstEpisode);
            copy.SeenNodeCandidates = new(SeenNodeCandidates.Select(kv => KeyValuePair.Create(kv.Key, new List<string>(kv.Value))));
            copy.SeenNodeLabels = new(SeenNodeLabels.Select(kv => KeyValuePair.Create(kv.Key, new List<string>(kv.Value))));
            copy.SeenDuplicateCandidates = new(SeenDuplicateCandidates.Select(kv => KeyValuePair.Create(kv.Key, new List<string>(kv.Value))));
            copy.SeenInvalidationCandidates = new(SeenInvalidationCandidates.Select(kv => KeyValuePair.Create(kv.Key, new List<string>(kv.Value))));
            copy.EdgesEndedByCandidate = [.. EdgesEndedByCandidate];
            copy.UuidMap = new(UuidMap);
            copy.SagaLinks = [.. SagaLinks.Select(x => Memo(x, l => l with { }))];
            copy.SagaNode = SagaNode is null ? null : Memo(SagaNode, n => n.ModelCopy(deep: true));
        }

        return copy;
    }

    /// <summary>Return the compressed JSON of the change set.</summary>
    // PORT: zlib.compress is PyZlib.Compress, with the default level 6 of Python (R18). The compressed bytes differ from the
    // bytes of CPython: .NET compresses with zlib-ng, and CPython 3.12 with zlib 1.2.12. No level of ZLibStream gives the
    // bytes of Python (measured on 2026-11-27 for levels 0 to 9). The compressed text is the same, and each side reads the
    // bytes of the other. GAPS F39 names the difference.
    internal byte[] ToBytes() => PyZlib.Compress(PyText.EncodeUtf8(Pydantic.DumpJson(ModelDump(json: true))));

    /// <summary>Return the change set of a payload that `to_bytes` made.</summary>
    // PORT: zlib.decompress is PyZlib.Decompress (R18). Bytes that are not a full zlib stream throw InvalidDataException with
    // the text of zlib.error, where Python raises zlib.error. R4 has no row for zlib.error.
    internal static ChangeSet FromBytes(byte[] payload) => ModelValidate(Pydantic.LoadJson(PyZlib.Decompress(payload)));

    /// <summary>Return the `PatchNode` operations for each node UUID, in list order.</summary>
    internal OrderedDictionary<string, List<PatchNode>> PatchesByNode()
    {
        var patches = new OrderedDictionary<string, List<PatchNode>>();
        foreach (var operation in Operations)
        {
            if (operation is PatchNode patchNode)
            {
                Py.SetDefault(patches, patchNode.NodeUuid, []).Add(patchNode);
            }
        }

        return patches;
    }

    /// <summary>Return the operations on stored edges, in list order.</summary>
    internal List<EdgeOperation> EdgeOperations() =>
        [.. Operations.Where(operation => operation is not PatchNode).Cast<EdgeOperation>()];
}

// PORT: the private dataclass `_EdgeState`. internal, because ResolutionTrace and ChangeSetModule use it (R2).
internal sealed record EdgeState
{
    internal required EntityEdge Edge { get; set; }

    internal required List<string> Episodes { get; set; }

    internal required OrderedDictionary<string, object?> Attributes { get; set; }

    internal required PyDateTime? InvalidAt { get; set; }

    internal required PyDateTime? ExpiredAt { get; set; }

    internal bool EndedAsCandidate { get; set; }
}

/// <summary>
/// Records what node resolution and edge resolution saw and changed.
///
/// The resolution functions change stored records in memory. The trace keeps the
/// candidates that each extracted item saw, and one operation for each change to a
/// stored record.
/// </summary>
internal sealed record ResolutionTrace
{
    internal OrderedDictionary<string, List<string>> SeenNodeCandidates { get; set; } = new();

    internal OrderedDictionary<string, List<string>> SeenNodeLabels { get; set; } = new();

    internal OrderedDictionary<string, List<string>> SeenDuplicateCandidates { get; set; } = new();

    internal OrderedDictionary<string, List<string>> SeenInvalidationCandidates { get; set; } = new();

    // PORT: `operations: list[Any]`. Each item is a PatchNode or an EdgeOperation (R20), so the type is Operation.
    internal List<Operation> Operations { get; set; } = [];

    // PORT: a set that code tests with `in` only (R14).
    internal HashSet<string> StoredNodeUuids { get; set; } = new(StringComparer.Ordinal);

    // The edges whose end date is the start of a contradicted candidate.
    // PORT: a set that code tests with `in` and passes to sorted() (R14).
    internal HashSet<string> EndedByCandidate { get; set; } = new(StringComparer.Ordinal);

    // The number of model calls that node resolution made for the trace.
    internal long NodeLlmCalls { get; set; }

    // PORT: `_edge_states: dict[int, _EdgeState]` is keyed by `id(edge)`. The key is the edge object with reference
    // equality, in insertion order, because collect_edge_operations iterates the keys.
    // PORT: R14 gives this form for a dict keyed by `id(obj)`.
    private OrderedDictionary<EntityEdge, EdgeState> edgeStates = new(ReferenceEqualityComparer.Instance);

    internal void RecordNodeCandidates(string nodeUuid, List<EntityNode> candidates)
    {
        var seen = Py.SetDefault(SeenNodeCandidates, nodeUuid, []);
        foreach (var candidate in candidates)
        {
            if (!seen.Contains(candidate.Uuid, StringComparer.Ordinal))
            {
                seen.Add(candidate.Uuid);
            }

            // The first read of a candidate is before a promotion of its labels in memory.
            Py.SetDefault(SeenNodeLabels, candidate.Uuid, [.. candidate.Labels]);
        }
    }

    /// <summary>Record that an extracted node resolved to a stored node.</summary>
    internal void RecordNodeResolution(EntityNode extractedNode, string resolvedUuid)
    {
        if (resolvedUuid == extractedNode.Uuid)
        {
            return;
        }

        StoredNodeUuids.Add(resolvedUuid);
        Operations.Add(
            new PatchNode
            {
                NodeUuid = resolvedUuid,
                Labels = [.. extractedNode.Labels],
                Attributes = Py.AsDict(Py.DeepCopy(extractedNode.Attributes.Count > 0 ? extractedNode.Attributes : new OrderedDictionary<string, object?>())),
            }.Validate());
    }

    internal void RecordEdgeCandidates(
        string edgeUuid,
        List<EntityEdge> duplicateCandidates,
        List<EntityEdge> invalidationCandidates)
    {
        SeenDuplicateCandidates[edgeUuid] = [.. duplicateCandidates.Select(edge => edge.Uuid)];
        SeenInvalidationCandidates[edgeUuid] = [
            .. invalidationCandidates.Select(edge => edge.Uuid)
        ];
    }

    /// <summary>Keep the state of stored edge objects before resolution changes them.</summary>
    internal void WatchStoredEdges(List<EntityEdge> edges)
    {
        foreach (var edge in edges)
        {
            if (edgeStates.ContainsKey(edge))
            {
                continue;
            }

            edgeStates[edge] = new EdgeState
            {
                Edge = edge,
                Episodes = [.. edge.Episodes],
                Attributes = Py.AsDict(Py.DeepCopy(edge.Attributes.Count > 0 ? edge.Attributes : new OrderedDictionary<string, object?>())),
                InvalidAt = edge.InvalidAt,
                ExpiredAt = edge.ExpiredAt,
            };
        }
    }

    /// <summary>Mark stored edges that a contradiction ended (`only_if_open=False`).</summary>
    internal void MarkEndedAsCandidate(List<EntityEdge> edges)
    {
        foreach (var edge in edges)
        {
            var state = edgeStates.GetValueOrDefault(edge);
            if (state is not null)
            {
                state.EndedAsCandidate = true;
            }
        }
    }

    /// <summary>
    /// Make one operation for each change to a watched edge, and stop the watch.
    ///
    /// With `edges`, only the watch of those edge objects stops. Calls that run at the
    /// same time on one trace thus do not collect the edges of each other.
    /// </summary>
    internal List<EdgeOperation> CollectEdgeOperations(List<EntityEdge>? edges = null)
    {
        var keys = edges is null ? [.. edgeStates.Keys] : edges.ToList();
        List<EdgeOperation> operations = [];
        foreach (var key in keys)
        {
            if (edgeStates.Remove(key, out var state))
            {
                operations.AddRange(ChangeSetModule.DiffEdgeState(state));
            }
        }

        Operations.AddRange(operations);
        return operations;
    }
}

// PORT: the module name equals the class name ChangeSet, so the module class has the suffix Module (R2).
internal static class ChangeSetModule
{
    // PORT: internal, because ResolutionTrace calls it (R2).
    internal static List<EdgeOperation> DiffEdgeState(EdgeState state)
    {
        var edge = state.Edge;
        List<EdgeOperation> operations = [];

        List<string> addedEpisodes = [.. edge.Episodes.Where(uuid => !state.Episodes.Contains(uuid, StringComparer.Ordinal))];
        if (addedEpisodes.Count > 0)
        {
            operations.Add(new AddEpisodesToEdge { EdgeUuid = edge.Uuid, EpisodeUuids = addedEpisodes }.Validate());
        }

        var currentAttributes = edge.Attributes.Count > 0 ? edge.Attributes : new OrderedDictionary<string, object?>();
        if (!PyEqualityComparer<object?>.Instance.Equals(currentAttributes, state.Attributes))
        {
            operations.Add(
                new SetEdgeAttributes
                {
                    EdgeUuid = edge.Uuid,
                    BaseAttributes = Py.AsDict(Py.DeepCopy(state.Attributes)),
                    Attributes = Py.AsDict(Py.DeepCopy(currentAttributes)),
                }.Validate());
        }

        var datesChanged = edge.InvalidAt != state.InvalidAt || edge.ExpiredAt != state.ExpiredAt;
        if (datesChanged && edge.ExpiredAt is { } expiredAt)
        {
            operations.Add(
                new EndEdge
                {
                    EdgeUuid = edge.Uuid,
                    InvalidAt = edge.InvalidAt != state.InvalidAt ? edge.InvalidAt : null,
                    ExpiredAt = expiredAt,
                    OnlyIfOpen = !state.EndedAsCandidate,
                }.Validate());
        }

        return operations;
    }

    /// <summary>
    /// Merge three attribute maps by key.
    ///
    /// A key with the same state in `current` and in `base` gets the state of `new`.
    /// A key with a different state in `current` keeps the state of `current`.
    /// </summary>
    internal static OrderedDictionary<string, object?> MergeEdgeAttributes(
        OrderedDictionary<string, object?> @base,
        OrderedDictionary<string, object?> @new,
        OrderedDictionary<string, object?> current)
    {
        var missing = new object();
        var merged = new OrderedDictionary<string, object?>();
        foreach (var key in (List<string>)[.. current.Keys, .. @new.Keys.Where(k => !current.ContainsKey(k))])
        {
            var currentValue = current.GetValueOrDefault(key, missing);
            // PORT: `current_value == base.get(key, missing)` is a plain `==` of two open values, with no identity test (R6).
            var unchanged = PyValueEquality.PlainEqual(currentValue, @base.GetValueOrDefault(key, missing));
            var value = unchanged ? @new.GetValueOrDefault(key, missing) : currentValue;
            if (!ReferenceEquals(value, missing))
            {
                merged[key] = value;
            }
        }

        return merged;
    }

    /// <summary>
    /// Apply one operation to the current state of a stored edge.
    ///
    /// Each rule gives the same record when it runs two times. Returns True when the
    /// operation changed the edge.
    /// </summary>
    internal static bool ApplyEdgeOperation(EntityEdge edge, EdgeOperation operation)
    {
        if (operation is AddEpisodesToEdge addEpisodesToEdge)
        {
            List<string> added = [.. addEpisodesToEdge.EpisodeUuids.Where(uuid => !edge.Episodes.Contains(uuid, StringComparer.Ordinal))];

            // PORT: dict.fromkeys(added) keeps the first of each item in order (R14).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var uuid in added)
            {
                if (seen.Add(uuid))
                {
                    edge.Episodes.Add(uuid);
                }
            }

            return added.Count > 0;
        }

        if (operation is EndEdge endEdge)
        {
            if (endEdge.OnlyIfOpen && edge.ExpiredAt is not null)
            {
                return false;
            }

            var changed = false;
            if (endEdge.InvalidAt is not null)
            {
                var currentInvalidAt = DatetimeUtils.EnsureUtc(edge.InvalidAt);
                var newInvalidAt = DatetimeUtils.EnsureUtc(endEdge.InvalidAt);
                if (currentInvalidAt is null || (
                    newInvalidAt is not null && newInvalidAt < currentInvalidAt))
                {
                    edge.InvalidAt = endEdge.InvalidAt;
                    changed = true;
                }
            }

            if (edge.ExpiredAt is null)
            {
                edge.ExpiredAt = endEdge.ExpiredAt;
                changed = true;
            }

            return changed;
        }

        var setEdgeAttributes = (SetEdgeAttributes)operation;
        var current = edge.Attributes.Count > 0 ? edge.Attributes : new OrderedDictionary<string, object?>();
        var merged = MergeEdgeAttributes(setEdgeAttributes.BaseAttributes, setEdgeAttributes.Attributes, current);
        if (PyEqualityComparer<object?>.Instance.Equals(merged, current))
        {
            return false;
        }

        edge.Attributes = merged;
        return true;
    }
}
