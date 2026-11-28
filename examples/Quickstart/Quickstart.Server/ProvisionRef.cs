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
    public string Chain => string.Join(" > ", Levels());

    /// <summary>
    /// Gets the topic of the section: the heading text of the Part, the Division, and the Subdivision, without the label
    /// of each level. For example "Cyber-abuse material targeted at an Australian adult". It says what the section is
    /// about, and it holds no number.
    /// </summary>
    public string Topic => string.Join(", ", new[] { Part, Division, Subdivision }.Where(l => l is not null).Select(l => Title(l!)));

    private static string ActShortName(string act)
    {
        var bracket = act.IndexOf(" (", StringComparison.Ordinal);
        return bracket < 0 ? act : act[..bracket];
    }

    private static string Title(string heading)
    {
        // "Part 3—Complaints, objections and investigations" has the title "Complaints, objections and investigations".
        var dash = heading.IndexOf('—', StringComparison.Ordinal);
        return dash < 0 ? heading : heading[(dash + 1)..];
    }

    private static string Label(string heading)
    {
        // "Part 3—Complaints, objections and investigations" has the label "Part 3".
        var dash = heading.IndexOf('—', StringComparison.Ordinal);
        return dash < 0 ? heading : heading[..dash];
    }

    private IEnumerable<string> Levels()
    {
        yield return Act;
        foreach (var level in new[] { Chapter, Part, Division, Subdivision })
        {
            if (level is not null)
            {
                yield return Label(level);
            }
        }

        yield return $"Section {Section}";
    }
}
