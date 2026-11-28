namespace Quickstart;

using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using Graphiti;

/// <summary>
/// The tool of the agent: the graph search of Graphiti.Net over the group of the example. For each fact it returns the
/// fact text, the two entities and the name of the relation, the validity dates, and the section id and the chain of the
/// episodes that hold the fact.
/// </summary>
internal sealed class SearchTool(IGraphiti graphiti, Settings settings)
{
    private const int FactCount = 10;
    private const int SectionsPerFact = 3;
    private const int SectionCount = 6;
    private const int FactSections = 4;
    private const int SectionTextLimit = 2000;

    private readonly Dictionary<string, ProvisionRef> _sections = Commands.ReadSections(settings)
        .Where(r => r.EpisodeUuid is not null)
        .GroupBy(r => r.EpisodeUuid!, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

    /// <summary>Searches the graph of the Online Safety Act 2021 for facts and for the text of the sections that state them.</summary>
    /// <param name="query">What to look for, in plain words.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>A JSON text with the facts.</returns>
    [Description("Search the knowledge graph of the Online Safety Act 2021 (Cth) and of the Digital Duty of Care Bill 2026, which amends the Act. Returns facts, each with its validity dates (a fact with invalid_at is no longer law from that date) and the section id and chain of each source. The result also has the text of the sections that state these facts, and of sections whose own text matches the query.")]
    public async Task<string> SearchLegislationAsync(
        [Description("What to look for, in plain words. For example: 'powers of the eSafety Commissioner to issue a removal notice'.")] string query,
        CancellationToken cancellationToken = default)
    {
        // An advanced search with the recipe of the hybrid search over edges and episodes (full-text and similarity search
        // for the facts, full-text search for the text of the episodes). A section with no fact can be found by its text.
        var config = SearchRecipes.CombinedHybridSearchRrf with { NodeConfig = null, CommunityConfig = null, Limit = FactCount };
        var found = await graphiti.AdvancedSearchAsync(
            query, config, new AdvancedSearchOptions { GroupIds = [settings.GroupId] }, cancellationToken);
        var edges = found.Edges;
        var entities = await ReadEntitiesAsync(edges, cancellationToken);

        var facts = new List<object>();
        var episodeOrder = new List<string>();
        foreach (var edge in edges)
        {
            var sources = new List<object>();
            // The first sources state the fact. A repeal adds its episode at the end of the list, so the last source stays
            // in the result: it is the reason for the end date.
            var shown = edge.Episodes.Count <= SectionsPerFact
                ? edge.Episodes
                : [.. edge.Episodes.Take(SectionsPerFact - 1), edge.Episodes[^1]];
            foreach (var uuid in shown)
            {
                if (!episodeOrder.Contains(uuid))
                {
                    episodeOrder.Add(uuid);
                }

                sources.Add(await DescribeAsync(uuid, cancellationToken));
            }

            // The graph of the page needs the two entities of each fact and the name of the relation.
            facts.Add(new
            {
                uuid = edge.Uuid,
                name = edge.Name,
                source = Entity(entities, edge.SourceNodeUuid),
                target = Entity(entities, edge.TargetNodeUuid),
                fact = edge.Fact,
                attributes = edge.Attributes.Where(a => a.Value is not null).ToDictionary(a => a.Key, a => a.Value),
                valid_at = Date(edge.ValidAt),
                invalid_at = Date(edge.InvalidAt),
                sources,
            });
        }

        // The sections: the first episodes of the facts, then the episodes that the search found by their text, then the
        // other episodes of the facts. No section appears two times. At most 6.
        var order = episodeOrder.Take(FactSections)
            .Concat(found.Episodes.Select(e => e.Uuid))
            .Concat(episodeOrder.Skip(FactSections))
            .Distinct(StringComparer.Ordinal)
            .Take(SectionCount)
            .ToList();
        var sections = await ReadSectionsAsync(order, cancellationToken);
        return JsonSerializer.Serialize(new { query, count = facts.Count, facts, sections });
    }

    // One read of the graph for the entities at the two ends of the facts.
    private async Task<Dictionary<string, EntityNode>> ReadEntitiesAsync(IReadOnlyList<EntityEdge> edges, CancellationToken cancellationToken)
    {
        var uuids = edges.SelectMany(e => new[] { e.SourceNodeUuid, e.TargetNodeUuid }).Distinct(StringComparer.Ordinal).ToList();
        return uuids.Count == 0
            ? new Dictionary<string, EntityNode>(StringComparer.Ordinal)
            : (await graphiti.Nodes.Entity.GetByUuidsAsync(uuids, cancellationToken)).ToDictionary(n => n.Uuid, StringComparer.Ordinal);
    }

    private static object Entity(Dictionary<string, EntityNode> entities, string uuid) =>
        entities.TryGetValue(uuid, out var node)
            ? new { uuid, name = node.Name, labels = node.Labels.Where(l => l != "Entity").ToList() }
            : new { uuid, name = "unknown", labels = new List<string>() };

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
