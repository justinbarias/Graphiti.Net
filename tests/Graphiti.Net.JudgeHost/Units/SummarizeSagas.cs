namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/summarize_sagas.py</c>.</summary>
internal static class SummarizeSagasBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("graphiti_core.prompts.summarize_sagas:summarize_saga", call => call.Signature("context").InvokeAsync(SummarizeSagas.SummarizeSaga));

        table.Schemas.Add("graphiti_core.prompts.summarize_sagas:SagaSummary", SagaSummary.ModelJsonSchema);
    }
}
