namespace Graphiti.JudgeHost.Units;

using Graphiti.JudgeHost.Core;

/// <summary>
/// The bindings of <c>graphiti_core/graphiti_types.py</c>. No judge case names this module, because each of its
/// functions needs a client object or an LLM call. So nothing is bound.
/// </summary>
internal static class GraphitiTypesBindings
{
    internal static void Register(BindingTable table)
    {
        _ = table;
    }
}
