// The node and edge namespaces of IGraphiti on the real Neo4j (plan 0004, layer 9, packet P9d): a save, a read, and a
// delete for each namespace, with a query of the test as the proof. The round trips of the mapping rules that can lose
// data: a date with an offset and more than 6 digits of a second, an embedding, each kind of attribute value, a null
// episode_metadata, and each member of EpisodeType.
namespace Graphiti.E2E.Tests;

using global::Neo4j.Driver;

public sealed class GraphitiNamespacesTests
{
    private const int TimeLimit = 180_000;

    // 10:20:30.1234567 at +05:30. A save keeps the offset and cuts the value to microseconds.
    private static readonly DateTimeOffset Precise = new DateTimeOffset(2024, 3, 5, 10, 20, 30, TimeSpan.FromMinutes(330)).AddTicks(1_234_567);
    private static readonly DateTimeOffset PreciseCut = new DateTimeOffset(2024, 3, 5, 10, 20, 30, TimeSpan.FromMinutes(330)).AddTicks(1_234_560);
    private static readonly DateTimeOffset Created = new DateTimeOffset(2023, 12, 31, 23, 59, 59, TimeSpan.FromMinutes(-210)).AddTicks(9_999_999);
    private static readonly DateTimeOffset CreatedCut = new DateTimeOffset(2023, 12, 31, 23, 59, 59, TimeSpan.FromMinutes(-210)).AddTicks(9_999_990);

    public static TheoryData<EpisodeType> EpisodeTypes => [.. Enum.GetValues<EpisodeType>()];

    // ---------------------------------------------------------------------------------------------------------------
    // Nodes
    // ---------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = TimeLimit)]
    public async Task Nodes_Entity_saves_reads_and_deletes_a_node()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var nodes = scope.Graphiti.Nodes.Entity;
        var node = new EntityNode { Name = "Ana", GroupId = scope.Group, Labels = ["Person"], Summary = "A pilot.", CreatedAt = Created };

        var saved = await nodes.SaveAsync(node, ct);
        Assert.NotNull(saved.NameEmbedding);
        Assert.Null(node.NameEmbedding);

        var read = await nodes.GetByUuidAsync(node.Uuid, ct);
        Assert.Equal((node.Uuid, "Ana", scope.Group, "A pilot.", CreatedCut), (read.Uuid, read.Name, read.GroupId, read.Summary, read.CreatedAt));
        Assert.Equal(TimeSpan.FromMinutes(-210), read.CreatedAt.Offset);
        Assert.Contains("Person", read.Labels);
        Assert.Single(await nodes.GetByGroupIdsAsync([scope.Group], cancellationToken: ct));

        await nodes.DeleteAsync(read, ct);
        Assert.Equal(0, await scope.CountAsync("MATCH (n:Entity {uuid: $u}) RETURN count(n) AS c", new { u = node.Uuid }));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Nodes_Episode_saves_reads_and_deletes_an_episode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var episodes = scope.Graphiti.Nodes.Episode;
        var episode = Episode(scope.Group, EpisodeType.Json);

        await episodes.SaveAsync(episode, ct);
        var read = await episodes.GetByUuidAsync(episode.Uuid, ct);
        Assert.Equal(
            (episode.Uuid, episode.Name, episode.Content, episode.SourceDescription, EpisodeType.Json, PreciseCut),
            (read.Uuid, read.Name, read.Content, read.SourceDescription, read.Source, read.ValidAt));
        Assert.Equal(TimeSpan.FromMinutes(330), read.ValidAt.Offset);
        Assert.Equal(["e1", "e2"], read.EntityEdges);
        Assert.Null(read.EpisodeMetadata);

        await episodes.DeleteAsync(read, ct);
        Assert.Equal(0, await scope.CountAsync("MATCH (n:Episodic {uuid: $u}) RETURN count(n) AS c", new { u = episode.Uuid }));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Nodes_Community_saves_reads_and_deletes_a_community()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var communities = scope.Graphiti.Nodes.Community;
        var community = new CommunityNode { Name = "Pilots", GroupId = scope.Group, Summary = "The pilots." };

        var saved = await communities.SaveAsync(community, ct);
        var read = await communities.GetByUuidAsync(community.Uuid, ct);
        Assert.Equal(("Pilots", "The pilots."), (read.Name, read.Summary));
        var loaded = await communities.LoadNameEmbeddingAsync(read, ct);
        Assert.Equal(saved.NameEmbedding!.Value.ToArray(), loaded.NameEmbedding!.Value.ToArray());

        await communities.DeleteAsync(read, ct);
        Assert.Equal(0, await scope.CountAsync("MATCH (n:Community {uuid: $u}) RETURN count(n) AS c", new { u = community.Uuid }));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Nodes_Saga_reads_and_deletes_a_saga_and_its_save_fails_as_upstream()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var sagas = scope.Graphiti.Nodes.Saga;
        var saga = new SagaNode { Name = "Trip", GroupId = scope.Group, Summary = "A trip." };

        // GAPS U2: each saga save misses five query parameters, so Neo4j rejects it.
        await Assert.ThrowsAsync<ClientException>(() => sagas.SaveAsync(saga, ct));

        await scope.QueryAsync(
            "CREATE (:Saga {uuid: $u, name: 'Trip', group_id: $g, summary: 'A trip.', created_at: datetime('2026-01-01T00:00:00+02:00')})",
            new { u = saga.Uuid });
        var read = await sagas.GetByUuidAsync(saga.Uuid, ct);

        // Upstream's _saga_node_from_record of the Neo4j driver reads only uuid, name, group_id, and created_at, so the
        // summary of the stored saga is not read back.
        Assert.Equal(("Trip", string.Empty, new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.FromHours(2))), (read.Name, read.Summary, read.CreatedAt));
        Assert.Single(await sagas.GetByUuidsAsync([saga.Uuid], ct));

        await sagas.DeleteAsync(read, ct);
        Assert.Equal(0, await scope.CountAsync("MATCH (n:Saga {uuid: $u}) RETURN count(n) AS c", new { u = saga.Uuid }));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Edges
    // ---------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = TimeLimit)]
    public async Task Edges_Entity_saves_reads_and_deletes_an_edge()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var a = await scope.Graphiti.Nodes.Entity.SaveAsync(new EntityNode { Name = "Ana", GroupId = scope.Group }, ct);
        var b = await scope.Graphiti.Nodes.Entity.SaveAsync(new EntityNode { Name = "Bo Corp", GroupId = scope.Group }, ct);
        var edge = new EntityEdge
        {
            GroupId = scope.Group,
            SourceNodeUuid = a.Uuid,
            TargetNodeUuid = b.Uuid,
            CreatedAt = Created,
            Name = "WORKS_AT",
            Fact = "Ana works at Bo Corp.",
            Episodes = ["ep"],
            ValidAt = Precise,
            InvalidAt = null,
            ExpiredAt = Created,
        };

        var saved = await scope.Graphiti.Edges.Entity.SaveAsync(edge, ct);
        Assert.NotNull(saved.FactEmbedding);
        var read = await scope.Graphiti.Edges.Entity.GetByUuidAsync(edge.Uuid, ct);
        Assert.Equal(
            (edge.Fact, edge.Name, a.Uuid, b.Uuid, (DateTimeOffset?)PreciseCut, (DateTimeOffset?)CreatedCut, (DateTimeOffset?)null),
            (read.Fact, read.Name, read.SourceNodeUuid, read.TargetNodeUuid, read.ValidAt, read.ExpiredAt, read.InvalidAt));
        Assert.Equal(["ep"], read.Episodes);
        Assert.Single(await scope.Graphiti.Edges.Entity.GetBetweenNodesAsync(a.Uuid, b.Uuid, ct));

        await scope.Graphiti.Edges.Entity.DeleteAsync(read, ct);
        Assert.Equal(0, await scope.CountAsync("MATCH ()-[r:RELATES_TO {uuid: $u}]->() RETURN count(r) AS c", new { u = edge.Uuid }));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Edges_Episodic_saves_reads_and_deletes_an_edge()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var episode = await scope.Graphiti.Nodes.Episode.SaveAsync(Episode(scope.Group, EpisodeType.Text), ct);
        var entity = await scope.Graphiti.Nodes.Entity.SaveAsync(new EntityNode { Name = "Ana", GroupId = scope.Group }, ct);
        var edge = new EpisodicEdge { GroupId = scope.Group, SourceNodeUuid = episode.Uuid, TargetNodeUuid = entity.Uuid, CreatedAt = Created };

        await scope.Graphiti.Edges.Episodic.SaveAsync(edge, ct);
        var read = await scope.Graphiti.Edges.Episodic.GetByUuidAsync(edge.Uuid, ct);
        Assert.Equal(edge with { CreatedAt = CreatedCut }, read);

        await scope.Graphiti.Edges.Episodic.DeleteAsync(read, ct);
        Assert.Equal(0, await scope.CountAsync("MATCH ()-[r:MENTIONS {uuid: $u}]->() RETURN count(r) AS c", new { u = edge.Uuid }));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Edges_Community_saves_reads_and_deletes_an_edge()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var community = await scope.Graphiti.Nodes.Community.SaveAsync(new CommunityNode { Name = "Pilots", GroupId = scope.Group }, ct);
        var entity = await scope.Graphiti.Nodes.Entity.SaveAsync(new EntityNode { Name = "Ana", GroupId = scope.Group }, ct);
        var edge = new CommunityEdge { GroupId = scope.Group, SourceNodeUuid = community.Uuid, TargetNodeUuid = entity.Uuid, CreatedAt = Created };

        await scope.Graphiti.Edges.Community.SaveAsync(edge, ct);
        var read = await scope.Graphiti.Edges.Community.GetByUuidAsync(edge.Uuid, ct);
        Assert.Equal(edge with { CreatedAt = CreatedCut }, read);

        await scope.Graphiti.Edges.Community.DeleteAsync(read, ct);
        Assert.Equal(0, await scope.CountAsync("MATCH ()-[r:HAS_MEMBER {uuid: $u}]->() RETURN count(r) AS c", new { u = edge.Uuid }));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Edges_HasEpisode_saves_reads_and_deletes_an_edge()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var episode = await scope.Graphiti.Nodes.Episode.SaveAsync(Episode(scope.Group, EpisodeType.Text), ct);
        var saga = Guid.NewGuid().ToString();
        await scope.QueryAsync(
            "CREATE (:Saga {uuid: $u, name: 'Trip', group_id: $g, summary: '', created_at: datetime('2026-01-01T00:00:00Z')})", new { u = saga });
        var edge = new HasEpisodeEdge { GroupId = scope.Group, SourceNodeUuid = saga, TargetNodeUuid = episode.Uuid, CreatedAt = Created };

        await scope.Graphiti.Edges.HasEpisode.SaveAsync(edge, ct);
        var read = await scope.Graphiti.Edges.HasEpisode.GetByUuidAsync(edge.Uuid, ct);
        Assert.Equal(edge with { CreatedAt = CreatedCut }, read);

        await scope.Graphiti.Edges.HasEpisode.DeleteAsync(read, ct);
        Assert.Equal(0, await scope.CountAsync("MATCH ()-[r:HAS_EPISODE {uuid: $u}]->() RETURN count(r) AS c", new { u = edge.Uuid }));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Edges_NextEpisode_saves_reads_and_deletes_an_edge()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var first = await scope.Graphiti.Nodes.Episode.SaveAsync(Episode(scope.Group, EpisodeType.Text), ct);
        var second = await scope.Graphiti.Nodes.Episode.SaveAsync(Episode(scope.Group, EpisodeType.Text), ct);
        var edge = new NextEpisodeEdge { GroupId = scope.Group, SourceNodeUuid = first.Uuid, TargetNodeUuid = second.Uuid, CreatedAt = Created };

        await scope.Graphiti.Edges.NextEpisode.SaveAsync(edge, ct);
        var read = await scope.Graphiti.Edges.NextEpisode.GetByUuidAsync(edge.Uuid, ct);
        Assert.Equal(edge with { CreatedAt = CreatedCut }, read);

        await scope.Graphiti.Edges.NextEpisode.DeleteAsync(read, ct);
        Assert.Equal(0, await scope.CountAsync("MATCH ()-[r:NEXT_EPISODE {uuid: $u}]->() RETURN count(r) AS c", new { u = edge.Uuid }));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Round trips of the mapping rules that can lose data
    // ---------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = TimeLimit)]
    public async Task Each_kind_of_attribute_value_round_trips_through_Neo4j()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var node = new EntityNode
        {
            Name = "Ana",
            GroupId = scope.Group,
            Attributes = new Dictionary<string, object?>
            {
                ["flag"] = true,
                ["count"] = 9_007_199_254_740_993L,
                ["ratio"] = 0.1 + 0.2,
                ["text"] = "Zoë 🚀",
                ["when"] = Precise,
                ["day"] = new DateOnly(2024, 2, 29),
                ["tags"] = new object?[] { "a", "b" },
                ["none"] = null,
            },
        };

        await scope.Graphiti.Nodes.Entity.SaveAsync(node, ct);
        var read = (await scope.Graphiti.Nodes.Entity.GetByUuidAsync(node.Uuid, ct)).Attributes;

        Assert.Equal(true, read["flag"]);
        Assert.Equal(9_007_199_254_740_993L, read["count"]);
        Assert.Equal(0.1 + 0.2, read["ratio"]);
        Assert.Equal("Zoë 🚀", read["text"]);
        var when = Assert.IsType<DateTimeOffset>(read["when"]);
        Assert.Equal((PreciseCut, TimeSpan.FromMinutes(330)), (when, when.Offset));
        Assert.Equal(new DateOnly(2024, 2, 29), read["day"]);
        Assert.Equal(["a", "b"], Assert.IsAssignableFrom<IReadOnlyList<object?>>(read["tags"]));

        // Neo4j stores no null property, so the key of a null value is not read back, as in Python.
        Assert.False(read.ContainsKey("none"));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_dictionary_attribute_fails_at_Neo4j_and_a_value_outside_the_model_fails_at_the_facade()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var nested = new EntityNode
        {
            Name = "Ana",
            GroupId = scope.Group,
            Attributes = new Dictionary<string, object?> { ["map"] = new Dictionary<string, object?> { ["k"] = 1L } },
        };
        await Assert.ThrowsAnyAsync<Neo4jException>(() => scope.Graphiti.Nodes.Entity.SaveAsync(nested, ct));

        var other = nested with { Attributes = new Dictionary<string, object?> { ["int"] = 1 } };
        await Assert.ThrowsAsync<InvalidCastException>(() => scope.Graphiti.Nodes.Entity.SaveAsync(other, ct));
        Assert.Equal(0, await scope.CountAsync("MATCH (n:Entity) WHERE n.group_id = $g RETURN count(n) AS c"));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task An_embedding_round_trips_through_Neo4j()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var vector = Enumerable.Range(1, 1024).Select(i => Math.PI / i).ToArray();
        var node = new EntityNode { Name = "Ana", GroupId = scope.Group, NameEmbedding = vector };

        await scope.Graphiti.Nodes.Entity.SaveBulkAsync([node], cancellationToken: ct);
        var loaded = await scope.Graphiti.Nodes.Entity.LoadEmbeddingsAsync(node with { NameEmbedding = null }, ct);

        // The save query stores the vector with db.create.setNodeVectorProperty, which keeps 32-bit values, as for
        // Python. The facade loses nothing: a vector of 32-bit values round trips with no change.
        var stored = vector.Select(v => (double)(float)v).ToArray();
        Assert.Equal(stored, loaded.NameEmbedding!.Value.ToArray());
        await scope.Graphiti.Nodes.Entity.SaveBulkAsync([loaded], cancellationToken: ct);
        var again = await scope.Graphiti.Nodes.Entity.LoadEmbeddingsBulkAsync([node with { NameEmbedding = null }], cancellationToken: ct);
        Assert.Equal(stored, Assert.Single(again).NameEmbedding!.Value.ToArray());
    }

    [Theory(Timeout = TimeLimit)]
    [MemberData(nameof(EpisodeTypes))]
    public async Task Each_episode_type_round_trips_through_Neo4j(EpisodeType type)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var episode = Episode(scope.Group, type);

        await scope.Graphiti.Nodes.Episode.SaveAsync(episode, ct);

        Assert.Equal(type, (await scope.Graphiti.Nodes.Episode.GetByUuidAsync(episode.Uuid, ct)).Source);
    }

    private static EpisodicNode Episode(string group, EpisodeType source) => new()
    {
        Name = "e",
        GroupId = group,
        Source = source,
        SourceDescription = "chat",
        Content = "Ana works at Bo Corp.",
        ValidAt = Precise,
        CreatedAt = Created,
        EntityEdges = ["e1", "e2"],
        EpisodeMetadata = null,
    };
}
