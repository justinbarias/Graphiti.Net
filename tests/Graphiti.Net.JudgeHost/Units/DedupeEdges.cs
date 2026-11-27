namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/dedupe_edges.py</c>.</summary>
internal static class DedupeEdgesBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("graphiti_core.prompts.dedupe_edges:resolve_edge", call => call.Signature("context").InvokeAsync(DedupeEdges.ResolveEdge));

        table.Schemas.Add("graphiti_core.prompts.dedupe_edges:EdgeDuplicate", EdgeDuplicate.ModelJsonSchema);
    }
}
