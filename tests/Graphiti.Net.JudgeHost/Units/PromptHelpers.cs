namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/prompt_helpers.py</c>.</summary>
internal static class PromptHelpersBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("graphiti_core.prompts.prompt_helpers:to_prompt_json", call => call.Signature("data", "ensure_ascii", "indent").InvokeAsync(PromptHelpers.ToPromptJson));

        // builtins:str, which the case calls on the constant, is bound in Units/Builtins.cs.
        table.Registry.AddRef("graphiti_core.prompts.prompt_helpers:DO_NOT_ESCAPE_UNICODE", PromptHelpers.DoNotEscapeUnicode);
    }
}
