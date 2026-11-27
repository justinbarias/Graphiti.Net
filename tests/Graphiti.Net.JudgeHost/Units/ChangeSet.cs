namespace Graphiti.JudgeHost.Units;

using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The bindings of <c>graphiti_core/ingestion/change_set.py</c>.</summary>
internal static class ChangeSetBindings
{
    private const string Module = "graphiti_core.ingestion.change_set";

    internal static void Register(BindingTable table)
    {
        table.Registry
            .AddModel(Module + ":AddEpisodesToEdge", data => AddEpisodesToEdge.ModelValidate(data))
            .AddModel(Module + ":EndEdge", data => EndEdge.ModelValidate(data))
            .AddModel(Module + ":SetEdgeAttributes", data => SetEdgeAttributes.ModelValidate(data))
            .AddModel(Module + ":PatchNode", data => PatchNode.ModelValidate(data))
            .AddModel(Module + ":SagaLink", data => SagaLink.ModelValidate(data))
            .AddModel(Module + ":ChangeSet", data => ChangeSet.ModelValidate(data))

            // `_EdgeState` is a dataclass: `_EdgeState(**data)` stores each value as it is.
            .AddModel(Module + ":_EdgeState", data => new EdgeState
            {
                Edge = data["edge"] as EntityEdge ?? throw new JudgeTypeRejectedException("_EdgeState.edge must be an EntityEdge"),
                Episodes = [.. Py.Iter(data["episodes"]).Select(Py.AsString)],
                Attributes = Py.AsDict(data["attributes"]),
                InvalidAt = data["invalid_at"] is null ? null : Py.AsDateTime(data["invalid_at"]),
                ExpiredAt = data["expired_at"] is null ? null : Py.AsDateTime(data["expired_at"]),
                EndedAsCandidate = data.TryGetValue("ended_as_candidate", out var ended) && Py.AsBool(ended),
            })
            .AddType<AddEpisodesToEdge>("AddEpisodesToEdge", m =>
            [
                new("op", m.Op),
                new("edge_uuid", m.EdgeUuid),
                new("episode_uuids", m.EpisodeUuids),
            ])
            .AddType<EndEdge>("EndEdge", m =>
            [
                new("op", m.Op),
                new("edge_uuid", m.EdgeUuid),
                new("invalid_at", m.InvalidAt),
                new("expired_at", m.ExpiredAt),
                new("only_if_open", m.OnlyIfOpen),
            ])
            .AddType<SetEdgeAttributes>("SetEdgeAttributes", m =>
            [
                new("op", m.Op),
                new("edge_uuid", m.EdgeUuid),
                new("base_attributes", m.BaseAttributes),
                new("attributes", m.Attributes),
            ])
            .AddType<PatchNode>("PatchNode", m =>
            [
                new("op", m.Op),
                new("node_uuid", m.NodeUuid),
                new("labels", m.Labels),
                new("attributes", m.Attributes),
            ])
            .AddType<SagaLink>("SagaLink", m =>
            [
                new("saga_uuid", m.SagaUuid),
                new("episode_uuid", m.EpisodeUuid),
                new("previous_episode_uuid", m.PreviousEpisodeUuid),
                new("has_episode_edge_uuid", m.HasEpisodeEdgeUuid),
                new("next_episode_edge_uuid", m.NextEpisodeEdgeUuid),
                new("created_at", m.CreatedAt),
            ])
            .AddType<ChangeSet>("ChangeSet", m =>
            [
                new("group_id", m.GroupId),
                new("unit_id", m.UnitId),
                new("seq", m.Seq),
                new("snapshot_seq", m.SnapshotSeq),
                new("analysis_started_at", m.AnalysisStartedAt),
                new("episodes", m.Episodes),
                new("create_nodes", m.CreateNodes),
                new("create_edges", m.CreateEdges),
                new("episodic_edges", m.EpisodicEdges),
                new("operations", m.Operations),
                new("node_uuids", m.NodeUuids),
                new("node_first_episode", m.NodeFirstEpisode),
                new("seen_node_candidates", m.SeenNodeCandidates),
                new("seen_node_labels", m.SeenNodeLabels),
                new("seen_duplicate_candidates", m.SeenDuplicateCandidates),
                new("seen_invalidation_candidates", m.SeenInvalidationCandidates),
                new("edges_ended_by_candidate", m.EdgesEndedByCandidate),
                new("uuid_map", m.UuidMap),
                new("saga_links", m.SagaLinks),
                new("saga_node", m.SagaNode),
                new("create_saga", m.CreateSaga),
            ]);

        table.Pure.Add(Module + ":AddEpisodesToEdge", call => Construct(call, AddEpisodesToEdge.ModelValidate));
        table.Pure.Add(Module + ":EndEdge", call => Construct(call, EndEdge.ModelValidate));
        table.Pure.Add(Module + ":SetEdgeAttributes", call => Construct(call, SetEdgeAttributes.ModelValidate));
        table.Pure.Add(Module + ":PatchNode", call => Construct(call, PatchNode.ModelValidate));
        table.Pure.Add(Module + ":SagaLink", call => Construct(call, SagaLink.ModelValidate));
        table.Pure.Add(Module + ":ChangeSet", call => Construct(call, ChangeSet.ModelValidate));
        table.Pure.Add(Module + ":ChangeSet.from_bytes", call => call.Signature("payload").InvokeAsync(ChangeSet.FromBytes));
        table.Pure.Add(Module + ":ChangeSet.patches_by_node", call =>
        {
            var p = call.Signature("self");
            return Codec.Result(p.Model<ChangeSet>("self").PatchesByNode());
        });
        table.Pure.Add(Module + ":ChangeSet.edge_operations", call =>
        {
            var p = call.Signature("self");
            return Codec.Result(p.Model<ChangeSet>("self").EdgeOperations());
        });
        table.Pure.Add(Module + ":_diff_edge_state", call => call.Signature("state").InvokeAsync(ChangeSetModule.DiffEdgeState));
        table.Pure.Add(Module + ":merge_edge_attributes", call => call.Signature("base", "new", "current").InvokeAsync(ChangeSetModule.MergeEdgeAttributes));
        table.Pure.Add(Module + ":apply_edge_operation", call => call.Signature("edge", "operation").InvokeAsync(ChangeSetModule.ApplyEdgeOperation));
    }

    // M(**kwargs) is M.ModelValidate(data) (R7). A pydantic model takes no positional argument, which is a TypeError.
    private static ValueTask<JsonNode?> Construct<T>(JudgeArguments call, Func<OrderedDictionary<string, object?>, T> modelValidate) =>
        call.Positional.Count == 0
            ? Codec.Result(modelValidate(call.Keywords))
            : throw new JudgeTypeRejectedException($"__init__() takes 1 positional argument but {call.Positional.Count + 1} were given");
}
