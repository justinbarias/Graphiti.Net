namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/summarize_nodes.py</c>.</summary>
internal static class SummarizeNodesBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("graphiti_core.prompts.summarize_nodes:summarize_pair", call => call.Signature("context").InvokeAsync(SummarizeNodes.SummarizePair));
        table.Pure.Add("graphiti_core.prompts.summarize_nodes:summarize_context", call => call.Signature("context").InvokeAsync(SummarizeNodes.SummarizeContext));
        table.Pure.Add("graphiti_core.prompts.summarize_nodes:summary_description", call => call.Signature("context").InvokeAsync(SummarizeNodes.SummaryDescription));

        table.Schemas.Add("graphiti_core.prompts.summarize_nodes:Summary", Summary.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.summarize_nodes:SummaryDescription", SummaryDescription.ModelJsonSchema);

        // The cases of prompts/lib.py pass the class as a $ref (resolve_response_model).
        table.Registry.AddRef("graphiti_core.prompts.summarize_nodes:Summary", Summary.ResponseModel);
    }
}
