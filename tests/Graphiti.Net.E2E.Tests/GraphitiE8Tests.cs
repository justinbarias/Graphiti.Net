// Plan 0004, test E8: concurrent ingestion through IGraphiti on the real Neo4j (plan 0004, layer 9, packet P9d, fix
// round). Six AddEpisodeAsync calls run at the same time into one group, with ConcurrentIngestion on and a window of 3.
// Each episode names the same two entities and one fact of its own. A read from Neo4j then shows that no update is lost:
// each episode once, each entity once, each fact, and the mention edges of each episode. The test runs with the
// in-memory state store and with the SQLite state store.
namespace Graphiti.E2E.Tests;

using global::Neo4j.Driver;
using Microsoft.Data.Sqlite;

public sealed class GraphitiE8Tests
{
    private const int TimeLimit = 180_000;
    private const int Episodes = 6;

    private static readonly DateTimeOffset Start = new(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);

    [Theory(Timeout = TimeLimit)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Six_concurrent_episodes_into_one_group_lose_no_update(bool sqlite)
    {
        var ct = TestContext.Current.CancellationToken;
        var folder = Path.Combine(Path.GetTempPath(), $"graphitinet-e8-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "state.db");
        try
        {
            await using (var scope = new GraphitiScope(Script, o => o.ConcurrentIngestion = new ConcurrentIngestionOptions
            {
                Window = 3,
                StateStorePath = sqlite ? path : null,
            }))
            {
                var results = await Task.WhenAll(Enumerable.Range(0, Episodes).Select(i => scope.Graphiti.AddEpisodeAsync(
                    new EpisodeInput { Name = $"e{i}", Content = FactOf(i), SourceDescription = "chat", ReferenceTime = Start.AddHours(i), Source = EpisodeType.Text },
                    new AddEpisodeOptions { GroupId = scope.Group },
                    ct)));

                var episodes = (await scope.QueryAsync("MATCH (n:Episodic) WHERE n.group_id = $g RETURN n.uuid AS u, n.name AS n"))
                    .Select(r => (Uuid: r["u"].As<string>(), Name: r["n"].As<string>())).ToList();
                Assert.Equal(Enumerable.Range(0, Episodes).Select(i => $"e{i}"), episodes.Select(e => e.Name).Order(StringComparer.Ordinal));
                Assert.Equal(results.Select(r => r.Episode.Uuid).Order(StringComparer.Ordinal), episodes.Select(e => e.Uuid).Order(StringComparer.Ordinal));

                var entities = (await scope.QueryAsync("MATCH (n:Entity) WHERE n.group_id = $g RETURN n.name AS n")).Select(r => r["n"].As<string>()).ToList();
                Assert.Equal(["Ana", "Bo Corp"], entities.Order(StringComparer.Ordinal));

                var facts = (await scope.QueryAsync("MATCH (:Entity)-[r:RELATES_TO]->(:Entity) WHERE r.group_id = $g RETURN r.fact AS f"))
                    .Select(r => r["f"].As<string>()).ToList();
                Assert.Equal(Enumerable.Range(0, Episodes).Select(FactOf).Order(StringComparer.Ordinal), facts.Order(StringComparer.Ordinal));

                foreach (var (uuid, _) in episodes)
                {
                    var mentioned = (await scope.QueryAsync("MATCH (:Episodic {uuid: $u})-[:MENTIONS]->(n:Entity) RETURN n.name AS n", new { u = uuid }))
                        .Select(r => r["n"].As<string>()).Order(StringComparer.Ordinal);
                    Assert.Equal(["Ana", "Bo Corp"], mentioned);
                }

                if (sqlite)
                {
                    Assert.Equal(Enumerable.Repeat("committed", Episodes), await UnitStatusesAsync(path, scope.Group));
                }
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string FactOf(int i) => $"Ana met Bo Corp on day {i + 1}.";

    private static async Task<List<string>> UnitStatusesAsync(string path, string group)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT status FROM graphiti_ingest_unit WHERE group_id = $g ORDER BY seq";
        command.Parameters.AddWithValue("$g", group);
        List<string> statuses = [];
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            statuses.Add(reader.GetString(0));
        }

        return statuses;
    }

    /// <summary>The replies: each episode names Ana and Bo Corp, and its own fact is the text of the episode.</summary>
    private static string? Script(string prompt, string user) => prompt switch
    {
        "extract_edges.edge" => $$"""
            {"edges": [{"source_entity_name": "Ana", "target_entity_name": "Bo Corp", "relation_type": "MET",
              "fact": "{{CurrentFact(user)}}", "valid_at": null, "invalid_at": null}]}
            """,
        _ => GraphitiMembersTests.Ana(prompt, user),
    };

    private static string CurrentFact(string user)
    {
        var current = GraphitiE9Tests.Between(user, "<CURRENT_MESSAGE>", "</CURRENT_MESSAGE>")
            ?? GraphitiE9Tests.Between(user, "<CURRENT MESSAGE>", "</CURRENT MESSAGE>")
            ?? throw new InvalidOperationException("The edge prompt has no current message.");
        var day = System.Text.RegularExpressions.Regex.Match(current, "on day (\\d+)\\.");
        return day.Success ? $"Ana met Bo Corp on day {day.Groups[1].Value}." : throw new InvalidOperationException("No fact in the current message.");
    }
}
