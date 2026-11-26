namespace Graphiti.JudgeHost.Units;

using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The bindings of <c>graphiti_core/helpers.py</c>.</summary>
internal static class HelpersBindings
{
    private const string Module = "graphiti_core.helpers:";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + "parse_db_date", call => call.Signature("input_date").InvokeAsync(Helpers.ParseDbDate));
        table.Pure.Add(Module + "get_default_group_id", call => call.Signature("provider").InvokeAsync(Helpers.GetDefaultGroupId));
        table.Pure.Add(Module + "lucene_sanitize", call => call.Signature("query").InvokeAsync(Helpers.LuceneSanitize));
        table.Pure.Add(Module + "normalize_l2", call => call.Signature("embedding").InvokeAsync(Helpers.NormalizeL2));
        table.Pure.Add(Module + "validate_group_id", call => call.Signature("group_id").InvokeAsync(Helpers.ValidateGroupId));
        table.Pure.Add(Module + "validate_group_ids", call => call.Signature("group_ids").InvokeAsync(Helpers.ValidateGroupIds));
        table.Pure.Add(Module + "validate_node_labels", call => call.Signature("node_labels").InvokeAsync(Helpers.ValidateNodeLabels));
        table.Pure.Add(Module + "validate_excluded_entity_types", call => call.Signature("excluded_entity_types", "entity_types").InvokeAsync(Helpers.ValidateExcludedEntityTypes));
    }
}
