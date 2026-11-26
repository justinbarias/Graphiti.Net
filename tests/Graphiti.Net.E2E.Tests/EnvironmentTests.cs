namespace Graphiti.E2E.Tests;

using global::Neo4j.Driver;

/// <summary>
/// Guards the test environment itself. Graphiti needs vector and full-text indexes, so a sidecar
/// that cannot create both makes every later end-to-end failure misleading.
/// This class has no product code to mutate and is exempt from mutation pruning (docs/TESTING.md).
/// </summary>
[Collection(Neo4jFixtureGroup.Name)]
public sealed class EnvironmentTests(Neo4jSidecar neo4j)
{
    [Fact]
    public async Task Sidecar_supports_the_vector_and_fulltext_search_that_Graphiti_needs()
    {
        var ct = TestContext.Current.CancellationToken;
        var group = Neo4jSidecar.NewGroupId();
        var label = $"Probe_{group}";
        var index = $"probe_ft_{group}";

        await using var session = neo4j.Driver.AsyncSession();
        try
        {
            await session.RunAsync(
                $"CREATE FULLTEXT INDEX {index} IF NOT EXISTS FOR (n:{label}) ON EACH [n.name]");
            await session.RunAsync("CALL db.awaitIndexes(30)");
            await session.RunAsync(
                $"CREATE (:{label} {{name: 'Ada Lovelace', v: [1.0, 0.0]}}), (:{label} {{name: 'Alan Turing', v: [0.0, 1.0]}})");

            var fulltext = await session.RunAsync(
                $"CALL db.index.fulltext.queryNodes('{index}', 'lovelace') YIELD node RETURN node.name AS name");
            var names = (await fulltext.ToListAsync(ct)).Select(r => r["name"].As<string>());
            Assert.Equal(["Ada Lovelace"], names);

            var vector = await session.RunAsync(
                $"MATCH (n:{label}) RETURN n.name AS name, vector.similarity.cosine(n.v, [1.0, 0.0]) AS score ORDER BY score DESC");
            var ranked = (await vector.ToListAsync(ct))
                .Select(r => (Name: r["name"].As<string>(), Score: r["score"].As<double>()))
                .ToList();
            Assert.Equal("Ada Lovelace", ranked[0].Name);
            Assert.Equal(1.0, ranked[0].Score, 6);
            Assert.True(ranked[1].Score < ranked[0].Score);
        }
        finally
        {
            await session.RunAsync($"MATCH (n:{label}) DETACH DELETE n");
            await session.RunAsync($"DROP INDEX {index} IF EXISTS");
        }
    }
}
