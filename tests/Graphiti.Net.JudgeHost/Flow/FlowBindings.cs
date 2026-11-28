namespace Graphiti.JudgeHost.Units;

using Graphiti.Core;
using Graphiti.JudgeHost.Core;

/// <summary>
/// The encoders of the three result models of <c>graphiti_core/graphiti.py</c>, which only flow steps return. No pure
/// case names <c>graphiti.py</c>, so no unit file holds them (R22: the module that defines a name registers it).
/// </summary>
internal static class FlowBindings
{
    internal static void Register(BindingTable table) =>
        table.Registry
            .AddType<AddEpisodeResults>("AddEpisodeResults", m =>
            [
                new("episode", m.Episode),
                new("episodic_edges", m.EpisodicEdges),
                new("nodes", m.Nodes),
                new("edges", m.Edges),
                new("communities", m.Communities),
                new("community_edges", m.CommunityEdges),
            ])
            .AddType<AddBulkEpisodeResults>("AddBulkEpisodeResults", m =>
            [
                new("episodes", m.Episodes),
                new("episodic_edges", m.EpisodicEdges),
                new("nodes", m.Nodes),
                new("edges", m.Edges),
                new("communities", m.Communities),
                new("community_edges", m.CommunityEdges),
            ])
            .AddType<AddTripletResults>("AddTripletResults", m =>
            [
                new("nodes", m.Nodes),
                new("edges", m.Edges),
            ]);
}
