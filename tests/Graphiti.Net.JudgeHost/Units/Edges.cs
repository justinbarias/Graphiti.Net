namespace Graphiti.JudgeHost.Units;

using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/edges.py</c>.</summary>
internal static class EdgesBindings
{
    private const string Module = "graphiti_core.edges";

    internal static void Register(BindingTable table)
    {
        table.Registry
            .AddModel(Module + ":EpisodicEdge", data => EpisodicEdge.ModelValidate(data))
            .AddModel(Module + ":EntityEdge", data => EntityEdge.ModelValidate(data))
            .AddModel(Module + ":CommunityEdge", data => CommunityEdge.ModelValidate(data))
            .AddModel(Module + ":HasEpisodeEdge", data => HasEpisodeEdge.ModelValidate(data))
            .AddModel(Module + ":NextEpisodeEdge", data => NextEpisodeEdge.ModelValidate(data))
            .AddType<EpisodicEdge>("EpisodicEdge", m => BaseFields(m))
            .AddType<EntityEdge>("EntityEdge", m =>
            [
                .. BaseFields(m),
                new("name", m.Name),
                new("fact", m.Fact),
                new("fact_embedding", m.FactEmbedding),
                new("episodes", m.Episodes),
                new("expired_at", m.ExpiredAt),
                new("valid_at", m.ValidAt),
                new("invalid_at", m.InvalidAt),
                new("reference_time", m.ReferenceTime),
                new("attributes", m.Attributes),
            ])
            .AddType<CommunityEdge>("CommunityEdge", m => BaseFields(m))
            .AddType<HasEpisodeEdge>("HasEpisodeEdge", m => BaseFields(m))
            .AddType<NextEpisodeEdge>("NextEpisodeEdge", m => BaseFields(m));

        table.Schemas.Add(Module + ":EpisodicEdge", EpisodicEdge.ModelJsonSchema);
        table.Schemas.Add(Module + ":EntityEdge", EntityEdge.ModelJsonSchema);
        table.Schemas.Add(Module + ":CommunityEdge", CommunityEdge.ModelJsonSchema);
        table.Schemas.Add(Module + ":HasEpisodeEdge", HasEpisodeEdge.ModelJsonSchema);
        table.Schemas.Add(Module + ":NextEpisodeEdge", NextEpisodeEdge.ModelJsonSchema);

        table.Pure.Add(Module + ":EpisodicEdge", call => Construct(call, EpisodicEdge.ModelValidate));
        table.Pure.Add(Module + ":EntityEdge", call => Construct(call, EntityEdge.ModelValidate));
        table.Pure.Add(Module + ":CommunityEdge", call => Construct(call, CommunityEdge.ModelValidate));
        table.Pure.Add(Module + ":HasEpisodeEdge", call => Construct(call, HasEpisodeEdge.ModelValidate));
        table.Pure.Add(Module + ":NextEpisodeEdge", call => Construct(call, NextEpisodeEdge.ModelValidate));

        // a == b is Equals (R18).
        table.Pure.Add(Module + ":EntityEdge.__eq__", call =>
        {
            var p = call.Signature("self", "other");
            return Codec.Result(p.Model<EntityEdge>("self").Equals(p.Value("other")));
        });

        table.Pure.Add(Module + ":get_episodic_edge_from_record", call => call.Signature("record").InvokeAsync(Edges.GetEpisodicEdgeFromRecord));
        table.Pure.Add(Module + ":get_entity_edge_from_record", call => call.Signature("record", "provider").InvokeAsync(Edges.GetEntityEdgeFromRecord));
        table.Pure.Add(Module + ":get_community_edge_from_record", call => call.Signature("record").InvokeAsync(Edges.GetCommunityEdgeFromRecord));
        table.Pure.Add(Module + ":get_has_episode_edge_from_record", call => call.Signature("record").InvokeAsync(Edges.GetHasEpisodeEdgeFromRecord));
        table.Pure.Add(Module + ":get_next_episode_edge_from_record", call => call.Signature("record").InvokeAsync(Edges.GetNextEpisodeEdgeFromRecord));
    }

    private static List<KeyValuePair<string, object?>> BaseFields(Edge m) =>
    [
        new("uuid", m.Uuid),
        new("group_id", m.GroupId),
        new("source_node_uuid", m.SourceNodeUuid),
        new("target_node_uuid", m.TargetNodeUuid),
        new("created_at", m.CreatedAt),
    ];

    // M(**kwargs) is M.ModelValidate(data) (R7). A pydantic model takes no positional argument, which is a TypeError.
    private static ValueTask<JsonNode?> Construct<T>(JudgeArguments call, Func<OrderedDictionary<string, object?>, T> modelValidate) =>
        call.Positional.Count == 0
            ? Codec.Result(modelValidate(call.Keywords))
            : throw new JudgeTypeRejectedException($"__init__() takes 1 positional argument but {call.Positional.Count + 1} were given");
}
