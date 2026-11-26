namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/snippets.py</c>.</summary>
internal static class SnippetsBindings
{
    internal static void Register(BindingTable table)
    {
        table.Registry.AddRef("graphiti_core.prompts.snippets:summary_instructions", Snippets.SummaryInstructions);
    }
}
