namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/extract_nodes_and_edges.py</c>.</summary>
internal static class ExtractNodesAndEdgesBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("graphiti_core.prompts.extract_nodes_and_edges:extract_message", call => call.Signature("context").InvokeAsync(ExtractNodesAndEdges.ExtractMessage));
        table.Pure.Add("graphiti_core.prompts.extract_nodes_and_edges:_build_edge_types_section", call => call.Signature("edge_types", "strict_edge_types").InvokeAsync(ExtractNodesAndEdges.BuildEdgeTypesSection));
        table.Pure.Add("graphiti_core.prompts.extract_nodes_and_edges:_build_custom_instructions_section", call => call.Signature("custom_extraction_instructions").InvokeAsync(ExtractNodesAndEdges.BuildCustomInstructionsSection));

        table.Registry
            .AddRef("graphiti_core.prompts.extract_nodes_and_edges:_SYSTEM_PROMPT", ExtractNodesAndEdges.SystemPrompt)
            .AddModel("graphiti_core.prompts.extract_nodes_and_edges:CombinedFact", data => CombinedFact.ModelValidate(data));

        table.Schemas.Add("graphiti_core.prompts.extract_nodes_and_edges:CombinedEntity", CombinedEntity.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.extract_nodes_and_edges:CombinedFact", CombinedFact.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.extract_nodes_and_edges:CombinedExtraction", CombinedExtraction.ModelJsonSchema);
    }
}
