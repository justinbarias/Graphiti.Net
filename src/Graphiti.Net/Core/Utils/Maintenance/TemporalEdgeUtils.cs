// Ported from graphiti_core/utils/maintenance/temporal_edge_utils.py

namespace Graphiti.Core.Utils.Maintenance;

using System.Text.RegularExpressions;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

/// <summary>Helpers for normalizing edge temporal bounds.</summary>
internal static partial class TemporalEdgeUtils
{
    private static ILogger Logger => PortServices.Logger(typeof(TemporalEdgeUtils));

    // PORT: re.IGNORECASE also matches U+017F for `s`, and U+0130 and U+0131 for `i`, so each `s` is `[sſ]`, each `i`
    // is `[iİı]`, and `[a-z]` is `[a-zſİı]` (R13). `\b` is PyRegex.Boundary.
    // r'\b('
    // r'will|shall|is going to|are going to|going to|'
    // r'plans? to|planned to|planning to|'
    // r'intends? to|intended to|'
    // r'expects? to|expected to|'
    // r'promises? to|promised to|'
    // r'scheduled to|is scheduled to|are scheduled to|'
    // r'set to|aims? to|aimed to|committed to'
    // r")\b|[a-z]+'ll\b"
    private static readonly Regex FutureAssertionRe = new(
        PyRegex.Boundary + "("
        + "w[iİı]ll|[sſ]hall|[iİı][sſ] go[iİı]ng to|are go[iİı]ng to|go[iİı]ng to|"
        + "plan[sſ]? to|planned to|plann[iİı]ng to|"
        + "[iİı]ntend[sſ]? to|[iİı]ntended to|"
        + "expect[sſ]? to|expected to|"
        + "prom[iİı][sſ]e[sſ]? to|prom[iİı][sſ]ed to|"
        + "[sſ]cheduled to|[iİı][sſ] [sſ]cheduled to|are [sſ]cheduled to|"
        + "[sſ]et to|a[iİı]m[sſ]? to|a[iİı]med to|comm[iİı]tted to"
        + ")" + PyRegex.Boundary + "|[a-zſİı]+'ll" + PyRegex.Boundary,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // r'\b(by|before|no later than|deadline|due by|due on)\b'
    private static readonly Regex DeadlineRe = new(
        PyRegex.Boundary + "(by|before|no later than|deadl[iİı]ne|due by|due on)" + PyRegex.Boundary,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // r'\b(until|through|thru|expires?|expired|ends?|ended|ending|'
    // r'stops?|stopped|ceases?|ceased|no longer)\b'
    private static readonly Regex OngoingStateEndRe = new(
        PyRegex.Boundary + "(unt[iİı]l|through|thru|exp[iİı]re[sſ]?|exp[iİı]red|end[sſ]?|ended|end[iİı]ng|"
        + "[sſ]top[sſ]?|[sſ]topped|cea[sſ]e[sſ]?|cea[sſ]ed|no longer)" + PyRegex.Boundary,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Normalize temporal bounds for future-tense assertions.
    ///
    /// For planned/promised/scheduled future facts, ``valid_at`` is the statement's
    /// reference time, not the future occurrence or deadline date. The future date
    /// remains part of the fact text or typed attributes. ``invalid_at`` is only for
    /// facts that stop being true; a soft deadline like "will complete by Friday"
    /// is not an invalidation time.
    /// </summary>
    internal static (PyDateTime?, PyDateTime?) NormalizeFutureTemporalBounds(
        string fact,
        PyDateTime? referenceTime,
        PyDateTime? validAt,
        PyDateTime? invalidAt)
    {
        var referenceTimeUtc = DatetimeUtils.EnsureUtc(referenceTime);
        var validAtUtc = DatetimeUtils.EnsureUtc(validAt);
        var invalidAtUtc = DatetimeUtils.EnsureUtc(invalidAt);

        if (referenceTimeUtc is not { } reference || !FutureAssertionRe.IsMatch(fact))
        {
            return (validAtUtc, invalidAtUtc);
        }

        if (validAtUtc is not { } valid || valid > reference)
        {
            validAtUtc = reference;
        }

        if (invalidAtUtc is { } invalid
            && invalid > reference
            && DeadlineRe.IsMatch(fact)
            && !OngoingStateEndRe.IsMatch(fact))
        {
            invalidAtUtc = null;
        }

        return (validAtUtc, invalidAtUtc);
    }

    /// <summary>Parse ISO timestamp strings onto an edge and normalize future bounds.</summary>
    internal static void ApplyExtractedTimestamps(
        EntityEdge edge,
        string? validAt,
        string? invalidAt,
        PyDateTime? referenceTime)
    {
        if (!string.IsNullOrEmpty(validAt))
        {
            try
            {
                edge.ValidAt = DatetimeUtils.EnsureUtc(PyDateTime.FromIsoFormat(validAt.Replace("Z", "+00:00", StringComparison.Ordinal)));
            }
            catch (ArgumentException)
            {
                Log.ErrorParsingValidAt(Logger, validAt);
            }
        }

        if (!string.IsNullOrEmpty(invalidAt))
        {
            try
            {
                edge.InvalidAt = DatetimeUtils.EnsureUtc(PyDateTime.FromIsoFormat(invalidAt.Replace("Z", "+00:00", StringComparison.Ordinal)));
            }
            catch (ArgumentException)
            {
                Log.ErrorParsingInvalidAt(Logger, invalidAt);
            }
        }

        (edge.ValidAt, edge.InvalidAt) = NormalizeFutureTemporalBounds(
            edge.Fact,
            referenceTime,
            edge.ValidAt,
            edge.InvalidAt);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Error parsing valid_at: {ValidAt}")]
        internal static partial void ErrorParsingValidAt(ILogger logger, string validAt);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Error parsing invalid_at: {InvalidAt}")]
        internal static partial void ErrorParsingInvalidAt(ILogger logger, string invalidAt);
    }
}
