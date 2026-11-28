namespace Quickstart;

/// <summary>
/// The place of one episode in the Act. The splitter makes one record for each episode. The ingestion fills in the
/// episode uuid, and the search tool reads it back from <c>sections.json</c>.
/// </summary>
/// <param name="Act">The name of the Act, for example "Online Safety Act 2021 (Cth)".</param>
/// <param name="Chapter">The heading of the Chapter, or null. This Act has no Chapter.</param>
/// <param name="Part">The heading of the Part, for example "Part 3—Complaints, objections and investigations", or null.</param>
/// <param name="Division">The heading of the Division, or null.</param>
/// <param name="Subdivision">The heading of the Subdivision, or null.</param>
/// <param name="Section">The section number, for example "36" or "13A".</param>
/// <param name="Heading">The heading of the section, without the number.</param>
/// <param name="Name">The episode name: the section id, with a part number for a section that has many episodes.</param>
/// <param name="EpisodeUuid">The uuid of the episode in the graph, or null before the ingestion.</param>
internal sealed record ProvisionRef(
    string Act,
    string? Chapter,
    string? Part,
    string? Division,
    string? Subdivision,
    string Section,
    string Heading,
    string Name,
    string? EpisodeUuid)
{
    /// <summary>Gets the section id, for example "Online Safety Act 2021 s 36".</summary>
    public string SectionId => $"{ActShortName(Act)} s {Section}";

    /// <summary>
    /// Gets the chain in one line, with the label of each level: Act, Chapter, Part, Division, Subdivision, Section. A
    /// level that does not exist is left out.
    /// </summary>
    public string Chain => string.Join(" > ", Levels(headingText: false));

    /// <summary>Gets the chain down to the Subdivision in one line, with the heading text of each level. It has no Section level.</summary>
    public string ChainWithHeadings => string.Join(" > ", Levels(headingText: true).SkipLast(1));

    private static string ActShortName(string act)
    {
        var bracket = act.IndexOf(" (", StringComparison.Ordinal);
        return bracket < 0 ? act : act[..bracket];
    }

    private static string Label(string heading)
    {
        // "Part 3—Complaints, objections and investigations" has the label "Part 3".
        var dash = heading.IndexOf('—', StringComparison.Ordinal);
        return dash < 0 ? heading : heading[..dash];
    }

    private IEnumerable<string> Levels(bool headingText)
    {
        yield return Act;
        foreach (var level in new[] { Chapter, Part, Division, Subdivision })
        {
            if (level is not null)
            {
                yield return headingText ? level : Label(level);
            }
        }

        yield return headingText ? $"Section {Section} {Heading}" : $"Section {Section}";
    }
}
