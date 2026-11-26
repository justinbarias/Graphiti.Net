namespace Graphiti.JudgeHost.Units;

using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The bindings of <c>graphiti_core/nodes.py</c>.</summary>
internal static class NodesBindings
{
    private const string Module = "graphiti_core.nodes";

    internal static void Register(BindingTable table)
    {
        // EpisodeType(value) is the lookup by value.
        table.Pure.Add("graphiti_core.nodes:EpisodeType", call => Codec.Result(PyEnum.FromValue<EpisodeType>(call.Signature("value").Value("value"))));
        table.Pure.Add("graphiti_core.nodes:EpisodeType.from_str", call => call.Signature("episode_type").InvokeAsync(EpisodeTypeExtensions.FromStr));
        table.Registry.AddEnum<EpisodeType>("graphiti_core.nodes:EpisodeType");

        table.Registry
            .AddModel(Module + ":EpisodicNode", EpisodicNode.ModelValidate)
            .AddModel(Module + ":EntityNode", EntityNode.ModelValidate)
            .AddModel(Module + ":CommunityNode", CommunityNode.ModelValidate)
            .AddModel(Module + ":SagaNode", SagaNode.ModelValidate)
            .AddType<EpisodicNode>("EpisodicNode", m =>
            [
                new("uuid", m.Uuid),
                new("name", m.Name),
                new("group_id", m.GroupId),
                new("labels", m.Labels),
                new("created_at", m.CreatedAt),
                new("source", m.Source),
                new("source_description", m.SourceDescription),
                new("content", m.Content),
                new("valid_at", m.ValidAt),
                new("entity_edges", m.EntityEdges),
                new("episode_metadata", m.EpisodeMetadata),
            ])
            .AddType<EntityNode>("EntityNode", m =>
            [
                new("uuid", m.Uuid),
                new("name", m.Name),
                new("group_id", m.GroupId),
                new("labels", m.Labels),
                new("created_at", m.CreatedAt),
                new("name_embedding", m.NameEmbedding),
                new("summary", m.Summary),
                new("attributes", m.Attributes),
            ])
            .AddType<CommunityNode>("CommunityNode", m =>
            [
                new("uuid", m.Uuid),
                new("name", m.Name),
                new("group_id", m.GroupId),
                new("labels", m.Labels),
                new("created_at", m.CreatedAt),
                new("name_embedding", m.NameEmbedding),
                new("summary", m.Summary),
            ])
            .AddType<SagaNode>("SagaNode", m =>
            [
                new("uuid", m.Uuid),
                new("name", m.Name),
                new("group_id", m.GroupId),
                new("labels", m.Labels),
                new("created_at", m.CreatedAt),
                new("summary", m.Summary),
                new("first_episode_uuid", m.FirstEpisodeUuid),
                new("last_episode_uuid", m.LastEpisodeUuid),
                new("last_summarized_at", m.LastSummarizedAt),
                new("last_summarized_episode_valid_at", m.LastSummarizedEpisodeValidAt),
            ]);

        table.Schemas.Add(Module + ":EpisodicNode", EpisodicNode.ModelJsonSchema);
        table.Schemas.Add(Module + ":EntityNode", EntityNode.ModelJsonSchema);
        table.Schemas.Add(Module + ":CommunityNode", CommunityNode.ModelJsonSchema);
        table.Schemas.Add(Module + ":SagaNode", SagaNode.ModelJsonSchema);

        table.Pure.Add(Module + ":EpisodicNode", call => Construct(call, EpisodicNode.ModelValidate));
        table.Pure.Add(Module + ":EntityNode", call => Construct(call, EntityNode.ModelValidate));
        table.Pure.Add(Module + ":CommunityNode", call => Construct(call, CommunityNode.ModelValidate));
        table.Pure.Add(Module + ":SagaNode", call => Construct(call, SagaNode.ModelValidate));

        // a.__eq__(b) is Equals (R18).
        table.Pure.Add(Module + ":EntityNode.__eq__", call => call.Signature("self", "other").InvokeAsync((Func<EntityNode, object?, bool>)((self, other) => self.Equals(other))));

        table.Pure.Add(Module + ":get_episodic_node_from_record", call => call.Signature("record").InvokeAsync(Nodes.GetEpisodicNodeFromRecord));
        table.Pure.Add(Module + ":get_entity_node_from_record", call => call.Signature("record", "provider").InvokeAsync(Nodes.GetEntityNodeFromRecord));
        table.Pure.Add(Module + ":get_community_node_from_record", call => call.Signature("record").InvokeAsync(Nodes.GetCommunityNodeFromRecord));
        table.Pure.Add(Module + ":get_saga_node_from_record", call => call.Signature("record").InvokeAsync(Nodes.GetSagaNodeFromRecord));
    }

    // M(**kwargs) is M.ModelValidate(data) (R7). A pydantic model takes no positional argument, which is a TypeError.
    private static ValueTask<JsonNode?> Construct<T>(JudgeArguments call, Func<OrderedDictionary<string, object?>, T> modelValidate) =>
        call.Positional.Count == 0
            ? Codec.Result(modelValidate(call.Keywords))
            : throw new JudgeTypeRejectedException($"__init__() takes 1 positional argument but {call.Positional.Count + 1} were given");
}
