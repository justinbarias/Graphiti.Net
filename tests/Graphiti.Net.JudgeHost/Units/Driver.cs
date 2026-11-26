namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Driver;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/driver/driver.py</c>.</summary>
internal static class DriverBindings
{
    internal static void Register(BindingTable table)
    {
        table.Registry.AddEnum<GraphProvider>("graphiti_core.driver.driver:GraphProvider");
    }
}
