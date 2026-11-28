namespace Quickstart;

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Graphiti;

/// <summary>
/// The tool of the agent: the graph search of Graphiti.Net over the group of the example. For each fact it returns the
/// fact text, the validity dates, and the section id and the chain of the episodes that hold the fact.
/// </summary>
internal sealed class SearchTool(IGraphiti graphiti, Settings settings)
{
    private const int FactCount = 10;
    private const int SectionsPerFact = 3;
    private const int SectionCount = 6;
    private const int SectionTextLimit = 2000;

    private readonly Dictionary<string, ProvisionRef> _sections = Commands.ReadSections(settings)
        .Where(r => r.EpisodeUuid is not null)
        .GroupBy(r => r.EpisodeUuid!, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    /// <summary>Searches the graph of the Online Safety Act 2021 for facts and for the text of the sections that state them.</summary>
    /// <param name="query">What to look for, in plain words.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A JSON text with the facts.</returns>
    [Description("Search the knowledge graph of the Online Safety Act 2021 (Cth). Returns facts from the Act, each with its validity dates and the section id and chain where the Act states it. The result also has the text of the sections that state these facts.")]
    public async Task<string> SearchLegislationAsync(
        [Description("What to look for, in plain words. For example: 'powers of the eSafety Commissioner to issue a removal notice'.")] string query,
        CancellationToken cancellationToken = default)
    {
        var edges = await graphiti.SearchAsync(
            query,
            new SearchOptions { GroupIds = [settings.GroupId], NumResults = FactCount },
            cancellationToken);

        var facts = new List<object>();
        var episodeOrder = new List<string>();
        foreach (var edge in edges)
        {
            var sources = new List<object>();
            foreach (var uuid in edge.Episodes.Take(SectionsPerFact))
            {
                if (!episodeOrder.Contains(uuid))
                {
                    episodeOrder.Add(uuid);
                }

                sources.Add(await DescribeAsync(uuid, cancellationToken));
            }

            facts.Add(new
            {
                fact = edge.Fact,
                valid_at = Date(edge.ValidAt),
                invalid_at = Date(edge.InvalidAt),
                sources,
            });
        }

        var sections = await ReadSectionsAsync(episodeOrder.Take(SectionCount).ToList(), cancellationToken);
        return JsonSerializer.Serialize(new { query, count = facts.Count, facts, sections });
    }

    // The graph stores the content of each episode, so the text of a section is read from the graph.
    private async Task<List<object>> ReadSectionsAsync(List<string> uuids, CancellationToken cancellationToken)
    {
        var sections = new List<object>();
        if (uuids.Count == 0)
        {
            return sections;
        }

        var episodes = (await graphiti.Nodes.Episode.GetByUuidsAsync(uuids, cancellationToken)).ToDictionary(e => e.Uuid, StringComparer.Ordinal);
        foreach (var uuid in uuids)
        {
            if (!episodes.TryGetValue(uuid, out var episode))
            {
                continue;
            }

            var (sectionId, chain) = _sections.TryGetValue(uuid, out var reference)
                ? (reference.SectionId, reference.Chain)
                : (episode.Name, episode.SourceDescription);
            sections.Add(new { section_id = sectionId, chain, text = Cut(episode.Content) });
        }

        return sections;
    }

    private static string Cut(string text)
    {
        if (text.Length <= SectionTextLimit)
        {
            return text;
        }

        var end = text.LastIndexOf(' ', SectionTextLimit);
        return text[..(end > 0 ? end : SectionTextLimit)] + " [text cut]";
    }

    private async Task<object> DescribeAsync(string episodeUuid, CancellationToken cancellationToken)
    {
        if (_sections.TryGetValue(episodeUuid, out var reference))
        {
            return new { section_id = reference.SectionId, chain = reference.Chain };
        }

        // sections.json has no record of this episode. The graph holds the name and the chain of each episode.
        var episodes = await graphiti.Nodes.Episode.GetByUuidsAsync([episodeUuid], cancellationToken);
        return episodes.Count > 0
            ? new { section_id = episodes[0].Name, chain = episodes[0].SourceDescription }
            : new { section_id = "unknown", chain = "unknown" };
    }

    private static string? Date(DateTimeOffset? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
