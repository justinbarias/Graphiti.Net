// Ported from graphiti_core/utils/maintenance/temporal_edge_utils.py

namespace Graphiti.Core.Utils.Maintenance;

using System.Text.RegularExpressions;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static class TemporalEdgeUtils
{
    private static ILogger Logger => PortServices.Logger(typeof(TemporalEdgeUtils));

    // PORT: a module-level re.compile is a private static readonly Regex (R13). The pattern needs the translation of
    // `\b`, `\s`, and re.IGNORECASE by R13, so the skeleton holds a property that throws, and the unit replaces it
    // with the field.
    // TODO(port): _FUTURE_ASSERTION_RE
    private static Regex FutureAssertionRe => throw new NotSupportedException("TODO(port): _FUTURE_ASSERTION_RE");

    // TODO(port): _DEADLINE_RE
    private static Regex DeadlineRe => throw new NotSupportedException("TODO(port): _DEADLINE_RE");

    // TODO(port): _ONGOING_STATE_END_RE
    private static Regex OngoingStateEndRe => throw new NotSupportedException("TODO(port): _ONGOING_STATE_END_RE");

    // TODO(port): normalize_future_temporal_bounds
    internal static (PyDateTime?, PyDateTime?) NormalizeFutureTemporalBounds(
        string fact,
        PyDateTime? referenceTime,
        PyDateTime? validAt,
        PyDateTime? invalidAt)
    {
        throw new NotSupportedException("TODO(port): normalize_future_temporal_bounds");
    }

    // TODO(port): apply_extracted_timestamps
    internal static void ApplyExtractedTimestamps(
        EntityEdge edge,
        string? validAt,
        string? invalidAt,
        PyDateTime? referenceTime)
    {
        throw new NotSupportedException("TODO(port): apply_extracted_timestamps");
    }
}
