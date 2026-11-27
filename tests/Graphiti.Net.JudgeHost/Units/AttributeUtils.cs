namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Utils.Maintenance;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/maintenance/attribute_utils.py</c>.</summary>
internal static class AttributeUtilsBindings
{
    private const string Module = "graphiti_core.utils.maintenance.attribute_utils";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":_resolve_default_max_length", call => call.Signature("default_max_length").InvokeAsync(AttributeUtils.ResolveDefaultMaxLength));
        table.Pure.Add(Module + ":_field_max_length", call => call.Signature("model", "field_name").InvokeAsync(AttributeUtils.FieldMaxLength));
        table.Pure.Add(Module + ":_field_is_required", call => call.Signature("model", "field_name").InvokeAsync(AttributeUtils.FieldIsRequired));
        table.Pure.Add(Module + ":_check_value_against_cap", call => call.Signature("value", "max_len").InvokeAsync(AttributeUtils.CheckValueAgainstCap));
        table.Pure.Add(
            Module + ":cap_string_attributes",
            call => call.Signature("response", "model", "default_max_length", "prompt_name", "entity_uuid", "group_id").InvokeAsync(AttributeUtils.CapStringAttributes));
        table.Pure.Add(
            Module + ":apply_capped_attributes",
            call => call.Signature("response", "model", "prior_attributes", "merge_mode", "default_max_length", "prompt_name", "entity_uuid", "group_id").InvokeAsync(AttributeUtils.ApplyCappedAttributes));
    }
}
