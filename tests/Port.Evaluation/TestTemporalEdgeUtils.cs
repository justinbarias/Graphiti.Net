// Ported from tests/utils/maintenance/test_temporal_edge_utils.py (port/RULES.md, R25).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Utils.Maintenance;
using Graphiti.PyCompat;

public class TestTemporalEdgeUtils
{
    // PORT: PyDateTime is internal, so a row holds the date as text, and null for None.
    public static TheoryData<string, string?, string?> FutureAssertions() => new()
    {
        { "Emma will relocate to Tokyo on December 25, 2022", "2022-12-25", null },
        { "David will launch the TechNova mobile app on September 1, 2026", "2026-09-01", null },
        { "The team will ship version 2.0 by the end of Q3 2025", "2025-09-30", null },
        { "Maria will pay back the loan by March 2026", null, null },
    };

    [Theory]
    [MemberData(nameof(FutureAssertions))]
    public void test_future_assertions_are_valid_at_reference_time(string fact, string? validAtText, string? invalidAtText)
    {
        PyDateTime? validAt = validAtText is null ? null : PyDateTime.FromIsoFormat(validAtText + "T00:00:00+00:00");
        PyDateTime? invalidAt = invalidAtText is null ? null : PyDateTime.FromIsoFormat(invalidAtText + "T00:00:00+00:00");
        var referenceTime = Dt(2020, 1, 1);

        var (normalizedValidAt, normalizedInvalidAt) = TemporalEdgeUtils.NormalizeFutureTemporalBounds(
            fact,
            referenceTime,
            validAt,
            invalidAt);

        Assert.Equal(referenceTime, normalizedValidAt);
        Assert.Equal(invalidAt, normalizedInvalidAt);
    }

    [Fact]
    public void test_future_soft_deadline_is_not_invalid_at()
    {
        var referenceTime = Dt(2020, 1, 1);

        var (normalizedValidAt, normalizedInvalidAt) = TemporalEdgeUtils.NormalizeFutureTemporalBounds(
            "Alice will complete the project by July 30, 2024",
            referenceTime,
            Dt(2024, 7, 30),
            Dt(2024, 7, 30));

        Assert.Equal(referenceTime, normalizedValidAt);
        Assert.Null(normalizedInvalidAt);
    }

    [Fact]
    public void test_future_ongoing_state_end_keeps_invalid_at()
    {
        var referenceTime = Dt(2020, 1, 1);
        var invalidAt = Dt(2024, 12, 25);

        var (normalizedValidAt, normalizedInvalidAt) = TemporalEdgeUtils.NormalizeFutureTemporalBounds(
            "TinyBirds styles will be unavailable until December 25, 2024",
            referenceTime,
            null,
            invalidAt);

        Assert.Equal(referenceTime, normalizedValidAt);
        Assert.Equal(invalidAt, normalizedInvalidAt);
    }

    [Fact]
    public void test_completed_event_date_is_unchanged()
    {
        var validAt = Dt(2024, 9, 1);

        var (normalizedValidAt, normalizedInvalidAt) = TemporalEdgeUtils.NormalizeFutureTemporalBounds(
            "David launched the TechNova mobile app on September 1, 2024",
            Dt(2020, 1, 1),
            validAt,
            null);

        Assert.Equal(validAt, normalizedValidAt);
        Assert.Null(normalizedInvalidAt);
    }

    private static PyDateTime Dt(int year, int month, int day) => PyDateTime.FromIsoFormat($"{year:D4}-{month:D2}-{day:D2}T00:00:00+00:00");
}
