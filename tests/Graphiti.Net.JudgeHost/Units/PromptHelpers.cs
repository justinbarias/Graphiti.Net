namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The bindings of <c>graphiti_core/prompts/prompt_helpers.py</c>.</summary>
internal static class PromptHelpersBindings
{
    internal static void Register(BindingTable table)
    {
        table.Pure.Add("graphiti_core.prompts.prompt_helpers:to_prompt_json", call => call.Signature("data", "ensure_ascii", "indent").InvokeAsync(PromptHelpers.ToPromptJson));

        // The case pure.prompts.const.do_not_escape_unicode calls str() on the constant.
        table.Registry.AddRef("graphiti_core.prompts.prompt_helpers:DO_NOT_ESCAPE_UNICODE", PromptHelpers.DoNotEscapeUnicode);
        table.Pure.Add("builtins:str", call => call.Signature("object").InvokeAsync((Func<object?, string>)Py.Str));
    }
}
