namespace Graphiti.JudgeHost.Units;

using Graphiti.Core.Utils.Maintenance;
using Graphiti.JudgeHost.Core;

/// <summary>The bindings of <c>graphiti_core/utils/maintenance/temporal_edge_utils.py</c>.</summary>
internal static class TemporalEdgeUtilsBindings
{
    private const string Module = "graphiti_core.utils.maintenance.temporal_edge_utils";

    internal static void Register(BindingTable table)
    {
        table.Pure.Add(
            Module + ":normalize_future_temporal_bounds",
            call => call.Signature("fact", "reference_time", "valid_at", "invalid_at").InvokeAsync(TemporalEdgeUtils.NormalizeFutureTemporalBounds));
    }
}
