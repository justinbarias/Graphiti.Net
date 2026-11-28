// Plan 0004, test E9: IGraphiti from a real service collection with AddGraphiti and AddNeo4j, on the real Neo4j. One
// episode, then a search that returns the fact. A second episode contradicts the fact. A query of the test then reads
// the graph back. The model replies are scripted by the prompt name of GraphitiModelCall.Current.
namespace Graphiti.E2E.Tests;

using System.Text.RegularExpressions;
using global::Neo4j.Driver;

public sealed partial class GraphitiE9Tests
{
    private const int TimeLimit = 180_000;

    internal static readonly DateTimeOffset March = new(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
    internal static readonly DateTimeOffset June = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact(Timeout = TimeLimit)]
    public async Task A_second_episode_invalidates_the_contradicted_fact_and_keeps_the_graph()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Kendra);
        var graphiti = scope.Graphiti;
        await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

        var first = await graphiti.AddEpisodeAsync(
            new EpisodeInput { Name = "e1", Content = "Kendra loves Adidas shoes.", SourceDescription = "chat", ReferenceTime = March, Source = EpisodeType.Text },
            new AddEpisodeOptions { GroupId = scope.Group },
            ct);
        Assert.Equal(scope.Group, first.Episode.GroupId);
        var firstEdge = Assert.Single(first.Edges);
        Assert.Equal("Kendra loves Adidas shoes.", firstEdge.Fact);

        // The result holds what the graph holds: the two entities, and one MENTIONS edge from the episode to each.
        Assert.Equal(["Adidas shoes", "Kendra"], first.Nodes.Select(n => n.Name).Order(StringComparer.Ordinal));
        Assert.Equal(
            first.Nodes.Select(n => n.Uuid).Order(StringComparer.Ordinal),
            (await scope.QueryAsync("MATCH (n:Entity) WHERE n.group_id = $g RETURN n.uuid AS u")).Select(r => r["u"].As<string>()).Order(StringComparer.Ordinal));
        Assert.Equal(2, first.EpisodicEdges.Count);
        Assert.All(first.EpisodicEdges, e => Assert.Equal(first.Episode.Uuid, e.SourceNodeUuid));
        Assert.Equal(first.Nodes.Select(n => n.Uuid).Order(StringComparer.Ordinal), first.EpisodicEdges.Select(e => e.TargetNodeUuid).Order(StringComparer.Ordinal));
        Assert.Empty(first.Communities);
        Assert.Empty(first.CommunityEdges);
        Assert.Equal(Counts(3, 1, 2, 2, 1), await CountsAsync(scope));

        var found = await graphiti.SearchAsync("Kendra Adidas shoes", new SearchOptions { GroupIds = [scope.Group] }, ct);
        Assert.Contains(found, e => e.Fact == "Kendra loves Adidas shoes." && e.GroupId == scope.Group);

        var second = await graphiti.AddEpisodeAsync(
            new EpisodeInput
            {
                Name = "e2",
                Content = "Kendra no longer loves Adidas shoes. Kendra now loves Nike shoes.",
                SourceDescription = "chat",
                ReferenceTime = June,
                Source = EpisodeType.Text,
            },
            new AddEpisodeOptions { GroupId = scope.Group },
            ct);
        Assert.Contains(second.Edges, e => e.Fact == "Kendra now loves Nike shoes." && e.ValidAt == June);

        var old = await scope.QueryAsync(
            "MATCH (:Entity {group_id: $g})-[r:RELATES_TO]->(:Entity) WHERE r.fact = $fact RETURN r.invalid_at AS invalid, r.expired_at AS expired",
            new { fact = "Kendra loves Adidas shoes." });
        var row = Assert.Single(old);
        Assert.Equal(June, row["invalid"].As<ZonedDateTime>().ToDateTimeOffset());
        Assert.NotNull(row["expired"].As<ZonedDateTime?>());

        Assert.Equal(1, await scope.CountAsync(
            "MATCH (:Entity {group_id: $g, name: 'Kendra'})-[r:RELATES_TO]->(:Entity {name: 'Nike shoes'}) WHERE r.fact = $fact RETURN count(r) AS c",
            new { fact = "Kendra now loves Nike shoes." }));

        // Episode 2 adds one episode, the entity Nike shoes, three MENTIONS edges, and two facts. It deletes nothing:
        // each node and each edge of the first episode still exists, with its uuid.
        Assert.Equal(Counts(5, 2, 3, 5, 3), await CountsAsync(scope));
        string[] firstNodes = [first.Episode.Uuid, .. first.Nodes.Select(n => n.Uuid)];
        string[] firstEdges = [.. first.Edges.Select(e => e.Uuid), .. first.EpisodicEdges.Select(e => e.Uuid)];
        Assert.Equal(firstNodes.Length, await scope.CountAsync("MATCH (n) WHERE n.uuid IN $us AND n.group_id = $g RETURN count(n) AS c", new { us = firstNodes }));
        Assert.Equal(firstEdges.Length, await scope.CountAsync("MATCH ()-[r]->() WHERE r.uuid IN $us AND r.group_id = $g RETURN count(r) AS c", new { us = firstEdges }));
        var reread = await graphiti.Edges.Entity.GetByUuidAsync(firstEdge.Uuid, ct);
        Assert.Equal((firstEdge.Fact, firstEdge.ValidAt, (DateTimeOffset?)June), (reread.Fact, reread.ValidAt, reread.InvalidAt));
        var stored = (await scope.QueryAsync(
            "MATCH ()-[r:RELATES_TO {uuid: $u}]->() RETURN r.reference_time AS t, r.expired_at AS x, r.created_at AS c", new { u = firstEdge.Uuid })).Single();
        Assert.Equal(stored["t"].As<ZonedDateTime>().ToDateTimeOffset(), reread.ReferenceTime);
        Assert.Equal(March, reread.ReferenceTime);
        Assert.Equal(stored["x"].As<ZonedDateTime>().ToDateTimeOffset(), reread.ExpiredAt);
        Assert.Equal(stored["c"].As<ZonedDateTime>().ToDateTimeOffset(), reread.CreatedAt);
        Assert.NotEqual(reread.CreatedAt, reread.ExpiredAt);
    }

    private static (long Nodes, long Episodes, long Entities, long Mentions, long Facts) Counts(
        long nodes, long episodes, long entities, long mentions, long facts) => (nodes, episodes, entities, mentions, facts);

    /// <summary>The count of the nodes, the episodes, the entities, the MENTIONS edges, and the facts of the group.</summary>
    private static async Task<(long, long, long, long, long)> CountsAsync(GraphitiScope scope) => (
        await scope.CountAsync("MATCH (n) WHERE n.group_id = $g RETURN count(n) AS c"),
        await scope.CountAsync("MATCH (n:Episodic) WHERE n.group_id = $g RETURN count(n) AS c"),
        await scope.CountAsync("MATCH (n:Entity) WHERE n.group_id = $g RETURN count(n) AS c"),
        await scope.CountAsync("MATCH ()-[r:MENTIONS]->() WHERE r.group_id = $g RETURN count(r) AS c"),
        await scope.CountAsync("MATCH ()-[r:RELATES_TO]->() WHERE r.group_id = $g RETURN count(r) AS c"));

    /// <summary>
    /// The replies of the Kendra scenario of the judge (flow.hand.contradiction), by prompt name. The second episode
    /// names Nike. The edge dedupe call marks the old fact as contradicted for the new fact that says "no longer".
    /// </summary>
    internal static string? Kendra(string prompt, string user) => prompt switch
    {
        "extract_nodes.extract_text" or "extract_nodes.extract_message" => user.Contains("Nike", StringComparison.Ordinal)
            ? """{"extracted_entities": [{"name": "Kendra", "entity_type_id": 0}, {"name": "Adidas shoes", "entity_type_id": 0}, {"name": "Nike shoes", "entity_type_id": 0}]}"""
            : """{"extracted_entities": [{"name": "Kendra", "entity_type_id": 0}, {"name": "Adidas shoes", "entity_type_id": 0}]}""",
        "extract_edges.edge" => Section(user, "CURRENT MESSAGE", "Nike") || user.Contains("Nike shoes", StringComparison.Ordinal)
            ? """
              {"edges": [
                {"source_entity_name": "Kendra", "target_entity_name": "Adidas shoes", "relation_type": "NO_LONGER_LOVES",
                 "fact": "Kendra no longer loves Adidas shoes.", "valid_at": "2026-06-01T00:00:00.000000Z", "invalid_at": null},
                {"source_entity_name": "Kendra", "target_entity_name": "Nike shoes", "relation_type": "LOVES",
                 "fact": "Kendra now loves Nike shoes.", "valid_at": "2026-06-01T00:00:00.000000Z", "invalid_at": null}]}
              """
            : """
              {"edges": [{"source_entity_name": "Kendra", "target_entity_name": "Adidas shoes", "relation_type": "LOVES",
                "fact": "Kendra loves Adidas shoes.", "valid_at": "2026-03-01T00:00:00.000000Z", "invalid_at": null}]}
              """,
        "dedupe_nodes.nodes" => NewNodes(user),
        "dedupe_edges.resolve_edge" => Contradict(user, "Kendra loves Adidas shoes."),
        _ => null,
    };

    /// <summary>A reply of <c>dedupe_nodes.nodes</c> that resolves each extracted entity as a new node.</summary>
    internal static string NewNodes(string user)
    {
        var extracted = Between(user, "<EXTRACTED ENTITIES>", "</EXTRACTED ENTITIES>") ?? user;
        var resolutions = IdAndName().Matches(extracted)
            .Select(m => $$"""{"id": {{m.Groups[1].Value}}, "name": "{{m.Groups[2].Value}}", "duplicate_candidate_id": -1}""");
        return $$"""{"entity_resolutions": [{{string.Join(", ", resolutions)}}]}""";
    }

    /// <summary>
    /// A reply of <c>dedupe_edges.resolve_edge</c>. A new fact that says "no longer" contradicts the old fact, by the id
    /// that the prompt gives the old fact.
    /// </summary>
    internal static string Contradict(string user, string oldFact)
    {
        var newFact = Between(user, "<NEW FACT>", "</NEW FACT>") ?? string.Empty;
        if (!newFact.Contains("no longer", StringComparison.Ordinal))
        {
            return """{"duplicate_facts": [], "contradicted_facts": []}""";
        }

        var match = Regex.Match(user, "['\"]idx['\"]: (\\d+), ['\"]fact['\"]: ['\"]" + Regex.Escape(oldFact) + "['\"]");
        return match.Success
            ? $$"""{"duplicate_facts": [], "contradicted_facts": [{{match.Groups[1].Value}}]}"""
            : """{"duplicate_facts": [], "contradicted_facts": []}""";
    }

    internal static string? Between(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        var to = from < 0 ? -1 : text.IndexOf(end, from, StringComparison.Ordinal);
        return from < 0 || to < 0 ? null : text[(from + start.Length)..to];
    }

    private static bool Section(string user, string name, string word) =>
        Between(user, $"<{name}>", $"</{name}>")?.Contains(word, StringComparison.Ordinal) == true;

    [GeneratedRegex("['\"]id['\"]: (\\d+), ['\"]name['\"]: ['\"]([^'\"]*)['\"]")]
    private static partial Regex IdAndName();
}
