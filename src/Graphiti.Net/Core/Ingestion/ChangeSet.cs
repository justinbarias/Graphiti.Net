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

using Graphiti.PyCompat;

// PORT: `op: Literal[...]` is `string`, with a test in Validate() (R7).
internal sealed record AddEpisodesToEdge : EdgeOperation
{
    internal string Op { get; set; } = "add_episodes_to_edge";

    internal required string EdgeUuid { get; set; }

    internal required List<string> EpisodeUuids { get; set; }

    // TODO(port): AddEpisodesToEdge (the Literal test of op, R7)
    internal AddEpisodesToEdge Validate()
    {
        throw new NotSupportedException("TODO(port): AddEpisodesToEdge.Validate");
    }
}

internal sealed record EndEdge : EdgeOperation
{
    internal string Op { get; set; } = "end_edge";

    internal required string EdgeUuid { get; set; }

    internal PyDateTime? InvalidAt { get; set; }

    internal required PyDateTime ExpiredAt { get; set; }

    internal bool OnlyIfOpen { get; set; }

    // TODO(port): EndEdge (the Literal test of op, R7)
    internal EndEdge Validate()
    {
        throw new NotSupportedException("TODO(port): EndEdge.Validate");
    }
}

internal sealed record SetEdgeAttributes : EdgeOperation
{
    internal string Op { get; set; } = "set_edge_attributes";

    internal required string EdgeUuid { get; set; }

    internal OrderedDictionary<string, object?> BaseAttributes { get; set; } = new();

    internal OrderedDictionary<string, object?> Attributes { get; set; } = new();

    // TODO(port): SetEdgeAttributes (the Literal test of op, R7)
    internal SetEdgeAttributes Validate()
    {
        throw new NotSupportedException("TODO(port): SetEdgeAttributes.Validate");
    }
}

internal sealed record PatchNode : Operation
{
    internal string Op { get; set; } = "patch_node";

    internal required string NodeUuid { get; set; }

    internal List<string> Labels { get; set; } = [];

    internal OrderedDictionary<string, object?> Attributes { get; set; } = new();

    // TODO(port): PatchNode (the Literal test of op, R7)
    internal PatchNode Validate()
    {
        throw new NotSupportedException("TODO(port): PatchNode.Validate");
    }
}

// PORT: `EdgeOperation = AddEpisodesToEdge | EndEdge | SetEdgeAttributes` is a union of three models (Part 3). It is an
// abstract record with one derived record for each case. Each case of EdgeOperation is also a case of Operation, so
// EdgeOperation derives from Operation.
// TODO(port): no rule for a type alias of a union that is also a part of a second union
internal abstract record EdgeOperation : Operation;

// PORT: `Operation = Annotated[... | PatchNode, Field(discriminator='op')]` is an abstract record with one derived
// record for each case (R7). The unit of the module adds ModelValidate, which reads the key `op`.
internal abstract record Operation;

internal sealed record SagaLink
{
    internal required string SagaUuid { get; set; }

    internal required string EpisodeUuid { get; set; }

    internal string? PreviousEpisodeUuid { get; set; }

    internal required string HasEpisodeEdgeUuid { get; set; }

    internal string? NextEpisodeEdgeUuid { get; set; }

    internal required PyDateTime CreatedAt { get; set; }

    // TODO(port): SagaLink.Validate (R7)
    internal SagaLink Validate()
    {
        throw new NotSupportedException("TODO(port): SagaLink.Validate");
    }
}

internal sealed record ChangeSet
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

    // TODO(port): ChangeSet.Validate (R7)
    internal ChangeSet Validate()
    {
        throw new NotSupportedException("TODO(port): ChangeSet.Validate");
    }

    // PORT: from_bytes calls model_validate_json (R7).
    // TODO(port): ChangeSet.model_validate
    internal static ChangeSet ModelValidate(OrderedDictionary<string, object?> data)
    {
        throw new NotSupportedException("TODO(port): ChangeSet.model_validate");
    }

    // PORT: to_bytes calls model_dump_json (R7).
    // TODO(port): ChangeSet.model_dump
    internal OrderedDictionary<string, object?> ModelDump(bool json = false)
    {
        throw new NotSupportedException("TODO(port): ChangeSet.model_dump");
    }

    // PORT: rebase_change_set calls model_copy(deep=True) (ingestion/phases.py:1221, R7).
    // TODO(port): ChangeSet.model_copy
    internal ChangeSet ModelCopy(bool deep = false)
    {
        throw new NotSupportedException("TODO(port): ChangeSet.model_copy");
    }

    // PORT: zlib.compress is ZLibStream (R18).
    // TODO(port): ChangeSet.to_bytes
    internal byte[] ToBytes()
    {
        throw new NotSupportedException("TODO(port): ChangeSet.to_bytes");
    }

    // TODO(port): ChangeSet.from_bytes
    internal static ChangeSet FromBytes(byte[] payload)
    {
        throw new NotSupportedException("TODO(port): ChangeSet.from_bytes");
    }

    // TODO(port): ChangeSet.patches_by_node
    internal OrderedDictionary<string, List<PatchNode>> PatchesByNode()
    {
        throw new NotSupportedException("TODO(port): ChangeSet.patches_by_node");
    }

    // TODO(port): ChangeSet.edge_operations
    internal List<EdgeOperation> EdgeOperations()
    {
        throw new NotSupportedException("TODO(port): ChangeSet.edge_operations");
    }
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
    // TODO(port): no rule for a dict keyed by id()
    private OrderedDictionary<EntityEdge, EdgeState> edgeStates = new(ReferenceEqualityComparer.Instance);

    // TODO(port): ResolutionTrace.record_node_candidates
    internal void RecordNodeCandidates(string nodeUuid, List<EntityNode> candidates)
    {
        throw new NotSupportedException("TODO(port): ResolutionTrace.record_node_candidates");
    }

    // TODO(port): ResolutionTrace.record_node_resolution
    internal void RecordNodeResolution(EntityNode extractedNode, string resolvedUuid)
    {
        throw new NotSupportedException("TODO(port): ResolutionTrace.record_node_resolution");
    }

    // TODO(port): ResolutionTrace.record_edge_candidates
    internal void RecordEdgeCandidates(
        string edgeUuid,
        List<EntityEdge> duplicateCandidates,
        List<EntityEdge> invalidationCandidates)
    {
        throw new NotSupportedException("TODO(port): ResolutionTrace.record_edge_candidates");
    }

    // TODO(port): ResolutionTrace.watch_stored_edges
    internal void WatchStoredEdges(List<EntityEdge> edges)
    {
        throw new NotSupportedException("TODO(port): ResolutionTrace.watch_stored_edges");
    }

    // TODO(port): ResolutionTrace.mark_ended_as_candidate
    internal void MarkEndedAsCandidate(List<EntityEdge> edges)
    {
        throw new NotSupportedException("TODO(port): ResolutionTrace.mark_ended_as_candidate");
    }

    // TODO(port): ResolutionTrace.collect_edge_operations
    internal List<EdgeOperation> CollectEdgeOperations(List<EntityEdge>? edges = null)
    {
        throw new NotSupportedException("TODO(port): ResolutionTrace.collect_edge_operations");
    }
}

// PORT: the module name equals the class name ChangeSet, so the module class has the suffix Module (R2).
internal static class ChangeSetModule
{
    // PORT: internal, because ResolutionTrace calls it (R2).
    // TODO(port): _diff_edge_state
    internal static List<EdgeOperation> DiffEdgeState(EdgeState state)
    {
        throw new NotSupportedException("TODO(port): _diff_edge_state");
    }

    // TODO(port): merge_edge_attributes
    internal static OrderedDictionary<string, object?> MergeEdgeAttributes(
        OrderedDictionary<string, object?> @base,
        OrderedDictionary<string, object?> @new,
        OrderedDictionary<string, object?> current)
    {
        throw new NotSupportedException("TODO(port): merge_edge_attributes");
    }

    // TODO(port): apply_edge_operation
    internal static bool ApplyEdgeOperation(EntityEdge edge, EdgeOperation operation)
    {
        throw new NotSupportedException("TODO(port): apply_edge_operation");
    }
}
