namespace Graphiti.JudgeHost.Units;

using Graphiti.Core;
using Graphiti.Core.Utils;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/bulk_utils.py</c>.</summary>
internal static class BulkUtilsBindings
{
    private const string Module = "graphiti_core.utils.bulk_utils";

    internal static void Register(BindingTable table)
    {
        table.Schemas.Add(Module + ":RawEpisode", RawEpisode.ModelJsonSchema);

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
}
