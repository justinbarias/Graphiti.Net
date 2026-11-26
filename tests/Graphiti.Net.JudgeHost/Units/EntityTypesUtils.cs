namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Utils.OntologyUtils;
using Graphiti.Internal;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/ontology_utils/entity_types_utils.py</c>.</summary>
internal static class EntityTypesUtilsBindings
{
    private const string Module = "graphiti_core.utils.ontology_utils.entity_types_utils";

    private static readonly string[] EntityNodeFields = ["uuid", "name", "group_id", "labels", "created_at", "name_embedding", "summary", "attributes"];

    internal static void Register(BindingTable table)
    {
        // The cases pass a model class as a custom type. Only the keys of its model_fields matter to this module, so
        // each descriptor holds the field names of the Python class. The kind of a field is not read.
        // TODO(port): no rule says who registers these two $ref paths (R22 gives them to the units of extract_nodes
        // and nodes, which do not register them). Remove them when those units do.
        table.Registry.AddRef(
            "graphiti_core.prompts.extract_nodes:ExtractedEntities",
            new GraphTypeDescriptor("ExtractedEntities", null, [new GraphFieldDescriptor("extracted_entities", GraphFieldKind.String, true, null, true, null)]));
        table.Registry.AddRef(
            "graphiti_core.nodes:EntityNode",
            new GraphTypeDescriptor(
                "EntityNode",
                null,
                EntityNodeFields
                    .Select(n => new GraphFieldDescriptor(n, GraphFieldKind.String, false, null, false, null))));

        table.Pure.Add(Module + ":validate_entity_types", call => call.Signature("entity_types").InvokeAsync(EntityTypesUtils.ValidateEntityTypes));
        table.Pure.Add(Module + ":validate_identity_properties", call => call.Signature("identity_properties", "entity_types").InvokeAsync(EntityTypesUtils.ValidateIdentityProperties));
    }
}
