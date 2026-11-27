namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/extract_nodes.py</c>.</summary>
internal static class ExtractNodesBindings
{
    private const string Module = "graphiti_core.prompts.extract_nodes";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(Module + ":extract_message", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractMessage));
        table.Pure.Add(Module + ":extract_json", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractJson));
        table.Pure.Add(Module + ":extract_text", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractText));
        table.Pure.Add(Module + ":classify_nodes", call => call.Signature("context").InvokeAsync(ExtractNodes.ClassifyNodes));
        table.Pure.Add(Module + ":extract_attributes", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractAttributes));
        table.Pure.Add(Module + ":extract_summary", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractSummary));
        table.Pure.Add(Module + ":_entity_type_descriptions_section", call => call.Signature("context").InvokeAsync(ExtractNodes.EntityTypeDescriptionsSection));
        table.Pure.Add(Module + ":extract_summaries_batch", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractSummariesBatch));
        table.Pure.Add(Module + ":extract_entity_summaries_from_episodes", call => call.Signature("context").InvokeAsync(ExtractNodes.ExtractEntitySummariesFromEpisodes));

        table.Registry
            .AddRef(Module + ":extract_message", (PromptFunction)ExtractNodes.ExtractMessage)
            .AddRef(Module + ":ExtractedEntity", ExtractedEntity.ResponseModel)
            .AddRef(Module + ":ExtractedEntities", ExtractedEntities.ResponseModel)
            .AddModel(Module + ":ExtractedEntity", data => ExtractedEntity.ModelValidate(data));

        table.Schemas.Add(Module + ":ExtractedEntity", ExtractedEntity.ModelJsonSchema);
        table.Schemas.Add(Module + ":ExtractedEntities", ExtractedEntities.ModelJsonSchema);
        table.Schemas.Add(Module + ":EntitySummary", EntitySummary.ModelJsonSchema);
        table.Schemas.Add(Module + ":SummarizedEntity", SummarizedEntity.ModelJsonSchema);
        table.Schemas.Add(Module + ":SummarizedEntities", SummarizedEntities.ModelJsonSchema);
    }
}
