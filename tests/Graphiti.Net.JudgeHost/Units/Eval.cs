namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/eval.py</c>.</summary>
internal static class EvalBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("graphiti_core.prompts.eval:query_expansion", call => call.Signature("context").InvokeAsync(Eval.QueryExpansion));
        table.Pure.Add("graphiti_core.prompts.eval:qa_prompt", call => call.Signature("context").InvokeAsync(Eval.QaPrompt));
        table.Pure.Add("graphiti_core.prompts.eval:eval_prompt", call => call.Signature("context").InvokeAsync(Eval.EvalPrompt));
        table.Pure.Add("graphiti_core.prompts.eval:eval_add_episode_results", call => call.Signature("context").InvokeAsync(Eval.EvalAddEpisodeResults));

        table.Schemas.Add("graphiti_core.prompts.eval:QueryExpansion", QueryExpansion.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.eval:QAResponse", QAResponse.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.eval:EvalResponse", EvalResponse.ModelJsonSchema);
        table.Schemas.Add("graphiti_core.prompts.eval:EvalAddEpisodeResults", EvalAddEpisodeResults.ModelJsonSchema);
    }
}
