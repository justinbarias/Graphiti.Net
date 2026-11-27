namespace Graphiti.JudgeHost.Units;

using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/ingestion/delta.py</c>.</summary>
internal static class DeltaBindings
{
    private const string Module = "graphiti_core.ingestion.delta";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":_unit_matrix", call => call.Signature("vectors").InvokeAsync(DeltaModule.UnitMatrix));
        table.Pure.Add(Module + ":_fuzzy_query", call => call.Signature("name").InvokeAsync(DeltaModule.FuzzyQuery));
        table.Pure.Add(Module + ":_top_matches", call => call
            .Signature("vectors", "candidate_uuids", "candidate_vectors", "min_score", "limit")
            .InvokeAsync(DeltaModule.TopMatches));
        table.Pure.Add(Module + ":load_delta_digests", call => call.Signature("group_id", "units").InvokeAsync(DeltaModule.LoadDeltaDigests));
        table.Pure.Add(Module + ":Delta.match_nodes", call =>
        {
            var p = call.Signature("self", "nodes", "identity_properties");
            var self = p.Model<Delta>("self");
            var nodes = p.List<EntityNode>("nodes");
            return Codec.Result(p.Has("identity_properties")
                ? self.MatchNodes(nodes, p.GetOrNone<OrderedDictionary<string, List<string>>>("identity_properties"))
                : self.MatchNodes(nodes));
        });
        table.Pure.Add(Module + ":Delta.patched_identity_matches", call =>
        {
            var p = call.Signature("self", "nodes", "identity_properties");
            return Codec.Result(p.Model<Delta>("self").PatchedIdentityMatches(
                p.List<EntityNode>("nodes"),
                p.GetOrNone<OrderedDictionary<string, List<string>>>("identity_properties")));
        });
        table.Pure.Add(Module + ":Delta.edges_between", call =>
        {
            var p = call.Signature("self", "edges");
            return Codec.Result(p.Model<Delta>("self").EdgesBetween(p.List<EntityEdge>("edges")));
        });
        table.Pure.Add(Module + ":Delta.similar_edges", call =>
        {
            var p = call.Signature("self", "edges");
            return Codec.Result(p.Model<Delta>("self").SimilarEdges(p.List<EntityEdge>("edges")));
        });

        // The cases build a Delta with `load_delta_digests(**data)`.
        table.Registry.AddModel(Module + ":load_delta_digests", data => DeltaModule.LoadDeltaDigests(
            (string)JudgeTypes.Convert(typeof(string), data["group_id"], "argument 'group_id'")!,
            (List<DeltaUnit>)JudgeTypes.Convert(typeof(List<DeltaUnit>), data["units"], "argument 'units'")!));

        // A float32 matrix is `matrix.tolist()`: a list of rows.
        table.Registry.AddEncoder<float[,]>(matrix => new JsonArray(
        [
            .. Enumerable.Range(0, matrix.GetLength(0)).Select(row =>
                (JsonNode)Codec.Vector(Enumerable.Range(0, matrix.GetLength(1)).Select(column => (double)matrix[row, column]))),
        ]));

        // A dataclass in a result is a $type with its Python field names (R22).
        table.Registry.AddType<UnitDigest>("_UnitDigest", m =>
        [
            new("seq", m.Seq),
            new("status", m.Status),
            new("nodes", m.Nodes),
            new("node_indexes", m.NodeIndexes),
            new("node_vector_uuids", m.NodeVectorUuids),
            new("node_vectors", m.NodeVectors),
            new("edges", m.Edges),
            new("edge_vector_uuids", m.EdgeVectorUuids),
            new("edge_vectors", m.EdgeVectors),
            new("patches", m.Patches),
        ]);
        table.Registry.AddType<Delta>("Delta", m =>
        [
            new("digests", m.Digests),
            new("patches", m.Patches),
            new("created_node_uuids", m.CreatedNodeUuids),
        ]);
    }
}
