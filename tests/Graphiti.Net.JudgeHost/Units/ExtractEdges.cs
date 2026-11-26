namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/extract_edges.py</c>.</summary>
internal static class ExtractEdgesBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("graphiti_core.prompts.extract_edges:edge", call => call.Signature("context").InvokeAsync(ExtractEdges.Edge));
        table.Pure.Add("graphiti_core.prompts.extract_edges:extract_attributes", call => call.Signature("context").InvokeAsync(ExtractEdges.ExtractAttributes));
        table.Pure.Add("graphiti_core.prompts.extract_edges:extract_timestamps", call => call.Signature("context").InvokeAsync(ExtractEdges.ExtractTimestamps));
        table.Pure.Add("graphiti_core.prompts.extract_edges:extract_timestamps_batch", call => call.Signature("context").InvokeAsync(ExtractEdges.ExtractTimestampsBatch));

        table.Schemas.Add("graphiti_core.prompts.extract_edges:Edge", Edge.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.extract_edges:ExtractedEdges", ExtractedEdges.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.extract_edges:EdgeTimestamps", EdgeTimestamps.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.extract_edges:BatchEdgeTimestamps", BatchEdgeTimestamps.ModelJsonSchema);
    }
}
