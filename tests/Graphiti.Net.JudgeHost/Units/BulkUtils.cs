namespace Graphiti.JudgeHost.Units;

using Graphiti.Core;
using Graphiti.Core.Utils;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The bindings of <c>graphiti_core/utils/bulk_utils.py</c>.</summary>
internal static class BulkUtilsBindings
{
    private const string Module = "graphiti_core.utils.bulk_utils";

    internal static void Register(BindingTable table)
    {
        table.Schemas.Add(Module + ":RawEpisode", RawEpisode.ModelJsonSchema);
        table.Registry.AddModel(Module + ":RawEpisode", RawEpisodeModel);

        table.Pure.Add(
            Module + ":_build_directed_uuid_map",
            call => call.Signature("pairs").InvokeAsync(BulkUtils.BuildDirectedUuidMap));
        table.Pure.Add(
            Module + ":compress_uuid_map",
            call => call.Signature("duplicate_pairs").InvokeAsync(BulkUtils.CompressUuidMap));

        // `E = TypeVar('E', bound=Edge)`: each case passes entity edges, so the binding closes the type parameter with
        // EntityEdge (R22).
        table.Pure.Add(
            Module + ":resolve_edge_pointers",
            call => call.Signature("edges", "uuid_map").InvokeAsync(
                (Func<List<EntityEdge>, OrderedDictionary<string, string>, List<EntityEdge>>)BulkUtils.ResolveEdgePointers));
    }

    // `RawEpisode(**data)` (bulk_utils.py:105), which the flow case `flow.recorded.bulk` passes to add_episode_bulk.
    // The product record has no ModelValidate, because no function of the module validates it, so the binding reads
    // each field with the typed read of pydantic (R7) in field order. `uuid` is the one field with a default, None.
    private static RawEpisode RawEpisodeModel(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("RawEpisode", data);
        var name = f.Required("name", Pydantic.Str);
        var uuid = f.Default("uuid", v => Pydantic.Optional(v, Pydantic.Str), () => null);
        var content = f.Required("content", Pydantic.Str);
        var sourceDescription = f.Required("source_description", Pydantic.Str);
        var source = f.Required("source", Pydantic.Enum<EpisodeType>);
        var referenceTime = f.Required("reference_time", Pydantic.DateTime);
        f.ThrowIfAny();
        return new RawEpisode
        {
            Name = name.Value,
            Uuid = uuid.Value,
            Content = content.Value,
            SourceDescription = sourceDescription.Value,
            Source = source.Value,
            ReferenceTime = referenceTime.Value,
        };
    }
}
