namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Utils.OntologyUtils;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/ontology_utils/entity_types_utils.py</c>.</summary>
internal static class EntityTypesUtilsBindings
{
    private const string Module = "graphiti_core.utils.ontology_utils.entity_types_utils";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":validate_entity_types", call => call.Signature("entity_types").InvokeAsync(EntityTypesUtils.ValidateEntityTypes));
        table.Pure.Add(Module + ":validate_identity_properties", call => call.Signature("identity_properties", "entity_types").InvokeAsync(EntityTypesUtils.ValidateIdentityProperties));
    }
}
