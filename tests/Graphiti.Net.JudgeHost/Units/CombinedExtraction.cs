namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Utils.Maintenance;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/maintenance/combined_extraction.py</c>.</summary>
internal static class CombinedExtractionBindings
{
    private const string Module = "graphiti_core.utils.maintenance.combined_extraction";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(
            Module + ":_episode_uuids_for_fact",
            call => call.Signature("edge_data", "episodes").InvokeAsync(CombinedExtractionModule.EpisodeUuidsForFact));
    }
}
