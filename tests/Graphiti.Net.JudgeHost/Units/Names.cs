namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Prompts;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/prompts/names.py</c>.</summary>
internal static class NamesBindings
{
    internal static void Register(BindingTable table)
    {
        // builtins:list, which the cases call on these two, is bound in Units/Builtins.cs.
        table.Registry.AddRef("graphiti_core.prompts.names:PromptName.__args__", Names.PromptName);
        table.Registry.AddRef("graphiti_core.prompts.names:PromptGroup.__args__", Names.PromptGroup);
    }
}
