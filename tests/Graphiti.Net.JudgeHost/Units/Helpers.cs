namespace Graphiti.JudgeHost.Units;

using Graphiti.Core;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/helpers.py</c>.</summary>
internal static class HelpersBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("graphiti_core.helpers:validate_node_labels", call => call.Signature("node_labels").InvokeAsync(Helpers.ValidateNodeLabels));
    }
}
