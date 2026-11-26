namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/dedupe_nodes.py</c>.</summary>
internal static class DedupeNodesBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("graphiti_core.prompts.dedupe_nodes:node", call => call.Signature("context").InvokeAsync(DedupeNodes.Node));
        table.Pure.Add("graphiti_core.prompts.dedupe_nodes:nodes", call => call.Signature("context").InvokeAsync(DedupeNodes.Nodes));
        table.Pure.Add("graphiti_core.prompts.dedupe_nodes:node_list", call => call.Signature("context").InvokeAsync(DedupeNodes.NodeList));

        table.Schemas.Add("graphiti_core.prompts.dedupe_nodes:NodeDuplicate", NodeDuplicate.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.dedupe_nodes:NodeResolutions", NodeResolutions.ModelJsonSchema);
    }
}
