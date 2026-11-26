namespace Graphiti.JudgeHost.Units;

using Graphiti.Core;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;

/// <summary>The bindings of <c>EpisodeType</c> in <c>graphiti_core/nodes.py</c>. No other name of that module is bound here.</summary>
internal static class NodesBindings
{
    internal static void Register(BindingTable table)
    {
        // EpisodeType(value) is the lookup by value.
        table.Pure.Add("graphiti_core.nodes:EpisodeType", call => Codec.Result(PyEnum.FromValue<EpisodeType>(call.Signature("value").Value("value"))));
        table.Pure.Add("graphiti_core.nodes:EpisodeType.from_str", call => call.Signature("episode_type").InvokeAsync(EpisodeTypeExtensions.FromStr));
        table.Registry.AddEnum<EpisodeType>("graphiti_core.nodes:EpisodeType");
    }
}
