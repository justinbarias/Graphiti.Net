// Each of the 75 members of the 9 namespaces of IGraphiti on the real Neo4j (plan 0004, layer 9, packet P9d, fix round).
// A read of the test from Neo4j is the proof of each call. A member that fails by a known upstream defect has an
// assertion of that failure: the batched node deletes (GAPS U1) and the saga save (GAPS U2).
namespace Graphiti.E2E.Tests;

using global::Neo4j.Driver;

public sealed class GraphitiNamespaceMembersTests
{
    private const int TimeLimit = 180_000;

    // A date with no digit after the microseconds, so that a read record equals the saved record.
    private static readonly DateTimeOffset At = new DateTimeOffset(2025, 6, 1, 8, 0, 0, TimeSpan.FromHours(2)).AddTicks(1_234_560);

    [Fact(Timeout = TimeLimit)]
    public async Task Each_entity_node_member_reads_or_writes_Neo4j()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var nodes = scope.Graphiti.Nodes.Entity;
        const string Stored = "MATCH (n:Entity) WHERE n.group_id = $g RETURN n.uuid AS u";

        var a = await nodes.SaveAsync(new EntityNode { Name = "Ana", GroupId = scope.Group }, ct);
        var b = new EntityNode { Name = "Bo", GroupId = scope.Group, NameEmbedding = Vector("Bo") };
        var c = new EntityNode { Name = "Cy", GroupId = scope.Group, NameEmbedding = Vector("Cy") };
        await nodes.SaveBulkAsync([b, c], cancellationToken: ct);
        Assert.Equal(Set(a.Uuid, b.Uuid, c.Uuid), await UuidsAsync(scope, Stored));

        await ReadsAsync(
            scope, Stored, [a.Uuid, b.Uuid, c.Uuid], n => n.Uuid, nodes.GetByUuidAsync, uuids => nodes.GetByUuidsAsync(uuids, ct),
            (limit, cursor) => nodes.GetByGroupIdsAsync([scope.Group], limit, cursor, ct), ct);

        var loaded = await nodes.LoadEmbeddingsAsync(b with { NameEmbedding = null }, ct);
        Assert.Equal(await StoredVectorAsync(scope, "MATCH (n:Entity {uuid: $u}) RETURN n.name_embedding AS v", b.Uuid), loaded.NameEmbedding!.Value.ToArray());
        var bulk = await nodes.LoadEmbeddingsBulkAsync([b with { NameEmbedding = null }, c with { NameEmbedding = null }], cancellationToken: ct);
        Assert.Equal(Vector("Cy"), Assert.Single(bulk, n => n.Uuid == c.Uuid).NameEmbedding!.Value.ToArray());
        Assert.Equal(Vector("Bo"), Assert.Single(bulk, n => n.Uuid == b.Uuid).NameEmbedding!.Value.ToArray());

        await nodes.DeleteAsync(a, ct);
        Assert.Equal(Set(b.Uuid, c.Uuid), await UuidsAsync(scope, Stored));
        await BatchedDeletesFailAsync(
            scope, Stored, () => nodes.DeleteByUuidsAsync([b.Uuid], cancellationToken: ct), () => nodes.DeleteByGroupIdAsync(scope.Group, cancellationToken: ct),
            () => nodes.DeleteByUuidsAsync([Guid.NewGuid().ToString()], cancellationToken: ct));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Each_episode_node_member_reads_or_writes_Neo4j()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var episodes = scope.Graphiti.Nodes.Episode;
        const string Stored = "MATCH (n:Episodic) WHERE n.group_id = $g RETURN n.uuid AS u";

        var e1 = await episodes.SaveAsync(Episode(scope.Group, "e1", EpisodeType.Text, At), ct);
        var e2 = Episode(scope.Group, "e2", EpisodeType.Text, At.AddDays(1));
        var e3 = Episode(scope.Group, "e3", EpisodeType.Message, At.AddDays(2));
        await episodes.SaveBulkAsync([e2, e3], cancellationToken: ct);
        Assert.Equal(Set(e1.Uuid, e2.Uuid, e3.Uuid), await UuidsAsync(scope, Stored));
        Assert.Equal(e2, await episodes.GetByUuidAsync(e2.Uuid, ct) with { EntityEdges = e2.EntityEdges, Labels = e2.Labels });

        await ReadsAsync(
            scope, Stored, [e1.Uuid, e2.Uuid, e3.Uuid], n => n.Uuid, episodes.GetByUuidAsync, uuids => episodes.GetByUuidsAsync(uuids, ct),
            (limit, cursor) => episodes.GetByGroupIdsAsync([scope.Group], limit, cursor, ct), ct);

        var entity = await scope.Graphiti.Nodes.Entity.SaveAsync(new EntityNode { Name = "Ana", GroupId = scope.Group }, ct);
        await scope.Graphiti.Edges.Episodic.SaveAsync(new EpisodicEdge { GroupId = scope.Group, SourceNodeUuid = e1.Uuid, TargetNodeUuid = entity.Uuid, CreatedAt = At }, ct);
        Assert.Equal([e1.Uuid], (await episodes.GetByEntityNodeUuidAsync(entity.Uuid, ct)).Select(e => e.Uuid));

        // The last episodes up to the time, newest first (episode_node_ops.py, retrieve_episodes), of the source and of the saga.
        Assert.Equal([e3.Uuid, e2.Uuid], (await episodes.RetrieveEpisodesAsync(At.AddDays(3), 2, [scope.Group], cancellationToken: ct)).Select(e => e.Uuid));
        Assert.Equal([e1.Uuid], (await episodes.RetrieveEpisodesAsync(At.AddHours(1), 3, [scope.Group], cancellationToken: ct)).Select(e => e.Uuid));

        // GAPS U3: with group ids and a source, the query holds "$group_idsAND e.source", and Neo4j rejects it.
        var u3 = await Assert.ThrowsAsync<ClientException>(() => episodes.RetrieveEpisodesAsync(At.AddDays(3), 3, [scope.Group], "text", cancellationToken: ct));
        Assert.Contains("Invalid input", u3.Message, StringComparison.Ordinal);
        await scope.QueryAsync(
            "CREATE (s:Saga {uuid: $s, name: 'Trip', group_id: $g, summary: '', created_at: datetime('2026-01-01T00:00:00Z')}) "
            + "WITH s MATCH (e:Episodic) WHERE e.uuid IN $es CREATE (s)-[:HAS_EPISODE {uuid: e.uuid + '-h', group_id: $g, created_at: datetime('2026-01-01T00:00:00Z')}]->(e)",
            new { s = Guid.NewGuid().ToString(), es = new[] { e1.Uuid, e3.Uuid } });
        Assert.Equal([e3.Uuid, e1.Uuid], (await episodes.RetrieveEpisodesAsync(At.AddDays(3), 3, [scope.Group], saga: "Trip", cancellationToken: ct)).Select(e => e.Uuid));
        Assert.Equal([e1.Uuid], (await episodes.RetrieveEpisodesAsync(At.AddDays(3), 3, [scope.Group], "text", "Trip", ct)).Select(e => e.Uuid));

        await episodes.DeleteAsync(e1, ct);
        Assert.Equal(Set(e2.Uuid, e3.Uuid), await UuidsAsync(scope, Stored));
        await BatchedDeletesFailAsync(
            scope, Stored, () => episodes.DeleteByUuidsAsync([e2.Uuid], cancellationToken: ct), () => episodes.DeleteByGroupIdAsync(scope.Group, cancellationToken: ct),
            () => episodes.DeleteByUuidsAsync([Guid.NewGuid().ToString()], cancellationToken: ct));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Each_community_node_member_reads_or_writes_Neo4j()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var communities = scope.Graphiti.Nodes.Community;
        const string Stored = "MATCH (n:Community) WHERE n.group_id = $g RETURN n.uuid AS u";

        var c1 = await communities.SaveAsync(new CommunityNode { Name = "Pilots", GroupId = scope.Group, Summary = "s1" }, ct);
        var c2 = new CommunityNode { Name = "Cooks", GroupId = scope.Group, NameEmbedding = Vector("Cooks"), CreatedAt = At };
        var c3 = new CommunityNode { Name = "Poets", GroupId = scope.Group, NameEmbedding = Vector("Poets"), CreatedAt = At };
        await communities.SaveBulkAsync([c2, c3], cancellationToken: ct);
        Assert.Equal(Set(c1.Uuid, c2.Uuid, c3.Uuid), await UuidsAsync(scope, Stored));

        await ReadsAsync(
            scope, Stored, [c1.Uuid, c2.Uuid, c3.Uuid], n => n.Uuid, communities.GetByUuidAsync, uuids => communities.GetByUuidsAsync(uuids, ct),
            (limit, cursor) => communities.GetByGroupIdsAsync([scope.Group], limit, cursor, ct), ct);

        var loaded = await communities.LoadNameEmbeddingAsync(c2 with { NameEmbedding = null }, ct);
        Assert.Equal(await StoredVectorAsync(scope, "MATCH (n:Community {uuid: $u}) RETURN n.name_embedding AS v", c2.Uuid), loaded.NameEmbedding!.Value.ToArray());
        Assert.Equal(Vector("Cooks"), loaded.NameEmbedding!.Value.ToArray());

        await communities.DeleteAsync(c1, ct);
        Assert.Equal(Set(c2.Uuid, c3.Uuid), await UuidsAsync(scope, Stored));
        await BatchedDeletesFailAsync(
            scope, Stored, () => communities.DeleteByUuidsAsync([c2.Uuid], cancellationToken: ct), () => communities.DeleteByGroupIdAsync(scope.Group, cancellationToken: ct),
            () => communities.DeleteByUuidsAsync([Guid.NewGuid().ToString()], cancellationToken: ct));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Each_saga_node_member_reads_or_writes_Neo4j()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var sagas = scope.Graphiti.Nodes.Saga;
        const string Stored = "MATCH (n:Saga) WHERE n.group_id = $g RETURN n.uuid AS u";

        // GAPS U2: each saga save misses five query parameters, so Neo4j rejects it. save_bulk saves one at a time.
        var s1 = new SagaNode { Name = "Trip", GroupId = scope.Group };
        await Assert.ThrowsAsync<ClientException>(() => sagas.SaveAsync(s1, ct));
        await Assert.ThrowsAsync<ClientException>(() => sagas.SaveBulkAsync([s1], cancellationToken: ct));
        Assert.Empty(await UuidsAsync(scope, Stored));

        string[] uuids = [s1.Uuid, Guid.NewGuid().ToString(), Guid.NewGuid().ToString()];
        await scope.QueryAsync(
            "UNWIND $us AS u CREATE (:Saga {uuid: u, name: 'Trip', group_id: $g, summary: '', created_at: datetime('2026-01-01T00:00:00Z')})",
            new { us = uuids });

        await ReadsAsync(
            scope, Stored, uuids, n => n.Uuid, sagas.GetByUuidAsync, ids => sagas.GetByUuidsAsync(ids, ct),
            (limit, cursor) => sagas.GetByGroupIdsAsync([scope.Group], limit, cursor, ct), ct);

        await sagas.DeleteAsync(await sagas.GetByUuidAsync(uuids[0], ct), ct);
        Assert.Equal(Set(uuids[1], uuids[2]), await UuidsAsync(scope, Stored));
        await BatchedDeletesFailAsync(
            scope, Stored, () => sagas.DeleteByUuidsAsync([uuids[1]], cancellationToken: ct), () => sagas.DeleteByGroupIdAsync(scope.Group, cancellationToken: ct),
            () => sagas.DeleteByUuidsAsync([Guid.NewGuid().ToString()], cancellationToken: ct));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Each_entity_edge_member_reads_or_writes_Neo4j()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var edges = scope.Graphiti.Edges.Entity;
        const string Stored = "MATCH ()-[r:RELATES_TO]->() WHERE r.group_id = $g RETURN r.uuid AS u";
        var a = await scope.Graphiti.Nodes.Entity.SaveAsync(new EntityNode { Name = "Ana", GroupId = scope.Group }, ct);
        var b = await scope.Graphiti.Nodes.Entity.SaveAsync(new EntityNode { Name = "Bo", GroupId = scope.Group }, ct);
        var c = await scope.Graphiti.Nodes.Entity.SaveAsync(new EntityNode { Name = "Cy", GroupId = scope.Group }, ct);

        var e1 = await edges.SaveAsync(Fact(scope.Group, a, b, "Ana knows Bo."), ct);
        var e2 = Fact(scope.Group, b, c, "Bo knows Cy.") with { FactEmbedding = Vector("Bo knows Cy.") };
        var e3 = Fact(scope.Group, a, c, "Ana knows Cy.") with { FactEmbedding = Vector("Ana knows Cy.") };
        await edges.SaveBulkAsync([e2, e3], cancellationToken: ct);
        Assert.Equal(Set(e1.Uuid, e2.Uuid, e3.Uuid), await UuidsAsync(scope, Stored));

        await ReadsAsync(
            scope, Stored, [e1.Uuid, e2.Uuid, e3.Uuid], e => e.Uuid, edges.GetByUuidAsync, uuids => edges.GetByUuidsAsync(uuids, ct),
            (limit, cursor) => edges.GetByGroupIdsAsync([scope.Group], limit, cursor, ct), ct);

        Assert.Equal([e1.Uuid], (await edges.GetBetweenNodesAsync(a.Uuid, b.Uuid, ct)).Select(e => e.Uuid));
        Assert.Empty(await edges.GetBetweenNodesAsync(b.Uuid, a.Uuid, ct));
        Assert.Equal(Set(e2.Uuid, e3.Uuid), (await edges.GetByNodeUuidAsync(c.Uuid, ct)).Select(e => e.Uuid).ToHashSet());

        var loaded = await edges.LoadEmbeddingsAsync(e2 with { FactEmbedding = null }, ct);
        Assert.Equal(await StoredVectorAsync(scope, "MATCH ()-[r:RELATES_TO {uuid: $u}]->() RETURN r.fact_embedding AS v", e2.Uuid), loaded.FactEmbedding!.Value.ToArray());
        Assert.Equal(Vector("Bo knows Cy."), loaded.FactEmbedding!.Value.ToArray());
        var bulk = await edges.LoadEmbeddingsBulkAsync([e2 with { FactEmbedding = null }, e3 with { FactEmbedding = null }], cancellationToken: ct);
        Assert.Equal(Vector("Ana knows Cy."), Assert.Single(bulk, e => e.Uuid == e3.Uuid).FactEmbedding!.Value.ToArray());

        await edges.DeleteAsync(e1, ct);
        Assert.Equal(Set(e2.Uuid, e3.Uuid), await UuidsAsync(scope, Stored));
        await edges.DeleteByUuidsAsync([e2.Uuid], ct);
        Assert.Equal(Set(e3.Uuid), await UuidsAsync(scope, Stored));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Each_episodic_edge_member_reads_or_writes_Neo4j()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var episode = await scope.Graphiti.Nodes.Episode.SaveAsync(Episode(scope.Group, "e", EpisodeType.Text, At), ct);
        var targets = await EntitiesAsync(scope, 3, ct);
        var ns = scope.Graphiti.Edges.Episodic;
        await EdgeMembersAsync(
            scope,
            "MENTIONS",
            [.. targets.Select(t => new EpisodicEdge { GroupId = scope.Group, SourceNodeUuid = episode.Uuid, TargetNodeUuid = t, CreatedAt = At })],
            e => ns.SaveAsync(e, ct),
            e => ns.SaveBulkAsync(e, cancellationToken: ct),
            e => ns.DeleteAsync(e, ct),
            u => ns.DeleteByUuidsAsync(u, ct),
            ns.GetByUuidAsync,
            u => ns.GetByUuidsAsync(u, ct),
            (limit, cursor) => ns.GetByGroupIdsAsync([scope.Group], limit, cursor, ct),
            ct);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Each_community_edge_member_reads_or_writes_Neo4j()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var community = await scope.Graphiti.Nodes.Community.SaveAsync(new CommunityNode { Name = "Pilots", GroupId = scope.Group }, ct);
        var targets = await EntitiesAsync(scope, 3, ct);
        var ns = scope.Graphiti.Edges.Community;
        await EdgeMembersAsync(
            scope,
            "HAS_MEMBER",
            [.. targets.Select(t => new CommunityEdge { GroupId = scope.Group, SourceNodeUuid = community.Uuid, TargetNodeUuid = t, CreatedAt = At })],
            e => ns.SaveAsync(e, ct),
            null,
            e => ns.DeleteAsync(e, ct),
            u => ns.DeleteByUuidsAsync(u, ct),
            ns.GetByUuidAsync,
            u => ns.GetByUuidsAsync(u, ct),
            (limit, cursor) => ns.GetByGroupIdsAsync([scope.Group], limit, cursor, ct),
            ct);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Each_has_episode_edge_member_reads_or_writes_Neo4j()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var saga = Guid.NewGuid().ToString();
        await scope.QueryAsync(
            "CREATE (:Saga {uuid: $u, name: 'Trip', group_id: $g, summary: '', created_at: datetime('2026-01-01T00:00:00Z')})", new { u = saga });
        var episodes = await EpisodesAsync(scope, 3, ct);
        var ns = scope.Graphiti.Edges.HasEpisode;
        await EdgeMembersAsync(
            scope,
            "HAS_EPISODE",
            [.. episodes.Select(e => new HasEpisodeEdge { GroupId = scope.Group, SourceNodeUuid = saga, TargetNodeUuid = e, CreatedAt = At })],
            e => ns.SaveAsync(e, ct),
            e => ns.SaveBulkAsync(e, cancellationToken: ct),
            e => ns.DeleteAsync(e, ct),
            u => ns.DeleteByUuidsAsync(u, ct),
            ns.GetByUuidAsync,
            u => ns.GetByUuidsAsync(u, ct),
            (limit, cursor) => ns.GetByGroupIdsAsync([scope.Group], limit, cursor, ct),
            ct);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Each_next_episode_edge_member_reads_or_writes_Neo4j()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var episodes = await EpisodesAsync(scope, 4, ct);
        var ns = scope.Graphiti.Edges.NextEpisode;
        await EdgeMembersAsync(
            scope,
            "NEXT_EPISODE",
            [.. episodes.Zip(episodes.Skip(1)).Select(p => new NextEpisodeEdge { GroupId = scope.Group, SourceNodeUuid = p.First, TargetNodeUuid = p.Second, CreatedAt = At })],
            e => ns.SaveAsync(e, ct),
            e => ns.SaveBulkAsync(e, cancellationToken: ct),
            e => ns.DeleteAsync(e, ct),
            u => ns.DeleteByUuidsAsync(u, ct),
            ns.GetByUuidAsync,
            u => ns.GetByUuidsAsync(u, ct),
            (limit, cursor) => ns.GetByGroupIdsAsync([scope.Group], limit, cursor, ct),
            ct);
    }

    internal static double[] Vector(string text) => [.. HashEmbeddingGenerator.Vector(text, 1024).Select(v => (double)v)];

    internal static HashSet<string> Set(params string[] values) => [.. values];

    internal static async Task<HashSet<string>> UuidsAsync(GraphitiScope scope, string cypher) =>
        [.. (await scope.QueryAsync(cypher)).Select(r => r["u"].As<string>())];

    internal static EpisodicNode Episode(string group, string name, EpisodeType source, DateTimeOffset validAt) => new()
    {
        Name = name,
        GroupId = group,
        Source = source,
        SourceDescription = "chat",
        Content = $"Content of {name}.",
        ValidAt = validAt,
        CreatedAt = At,
        EntityEdges = ["x"],
    };

    private static EntityEdge Fact(string group, EntityNode source, EntityNode target, string fact) => new()
    {
        GroupId = group,
        SourceNodeUuid = source.Uuid,
        TargetNodeUuid = target.Uuid,
        CreatedAt = At,
        Name = "KNOWS",
        Fact = fact,
    };

    private static async Task<double[]> StoredVectorAsync(GraphitiScope scope, string cypher, string uuid) =>
        [.. (await scope.QueryAsync(cypher, new { u = uuid })).Single()["v"].As<List<double>>()];

    /// <summary>
    /// The three reads of a namespace: by uuid, by uuids (a missing uuid gives nothing), and by group, all of them, the
    /// first by a limit of 1, and the ones before a cursor. The group read orders by uuid, highest first.
    /// </summary>
    private static async Task ReadsAsync<T>(
        GraphitiScope scope,
        string stored,
        string[] uuids,
        Func<T, string> uuidOf,
        Func<string, CancellationToken, Task<T>> getByUuid,
        Func<IReadOnlyList<string>, Task<IReadOnlyList<T>>> getByUuids,
        Func<int?, string?, Task<IReadOnlyList<T>>> getByGroupIds,
        CancellationToken ct)
    {
        var inNeo4j = (await UuidsAsync(scope, stored)).Order(StringComparer.Ordinal).Reverse().ToList();
        Assert.Equal(uuids.Order(StringComparer.Ordinal).Reverse(), inNeo4j);
        Assert.Equal(uuids[1], uuidOf(await getByUuid(uuids[1], ct)));
        Assert.Equal(Set(uuids[0], uuids[2]), (await getByUuids([uuids[0], uuids[2], Guid.NewGuid().ToString()])).Select(uuidOf).ToHashSet());
        Assert.Equal(inNeo4j, (await getByGroupIds(null, null)).Select(uuidOf));
        Assert.Equal(inNeo4j.Take(1), (await getByGroupIds(1, null)).Select(uuidOf));
        Assert.Equal(inNeo4j.Skip(1), (await getByGroupIds(null, inNeo4j[0])).Select(uuidOf));
    }

    /// <summary>
    /// GAPS U1: a batched node delete runs CALL ... IN TRANSACTIONS inside a transaction, so Neo4j rejects it when a row
    /// matches, and nothing is deleted. With no matching row the call returns.
    /// </summary>
    private static async Task BatchedDeletesFailAsync(GraphitiScope scope, string stored, Func<Task> byUuids, Func<Task> byGroup, Func<Task> noMatch)
    {
        var before = await UuidsAsync(scope, stored);
        Assert.Contains("CALL { ... } IN TRANSACTIONS", (await Assert.ThrowsAnyAsync<Neo4jException>(byUuids)).Message, StringComparison.Ordinal);
        Assert.Contains("CALL { ... } IN TRANSACTIONS", (await Assert.ThrowsAnyAsync<Neo4jException>(byGroup)).Message, StringComparison.Ordinal);
        await noMatch();
        Assert.Equal(before, await UuidsAsync(scope, stored));
    }

    private static async Task EdgeMembersAsync<T>(
        GraphitiScope scope,
        string type,
        IReadOnlyList<T> edges,
        Func<T, Task<T>> save,
        Func<IReadOnlyList<T>, Task>? saveBulk,
        Func<T, Task> delete,
        Func<IReadOnlyList<string>, Task> deleteByUuids,
        Func<string, CancellationToken, Task<T>> getByUuid,
        Func<IReadOnlyList<string>, Task<IReadOnlyList<T>>> getByUuids,
        Func<int?, string?, Task<IReadOnlyList<T>>> getByGroupIds,
        CancellationToken ct)
        where T : Edge
    {
        var stored = $"MATCH ()-[r:{type}]->() WHERE r.group_id = $g RETURN r.uuid AS u";
        await save(edges[0]);
        if (saveBulk is null)
        {
            await save(edges[1]);
            await save(edges[2]);
        }
        else
        {
            await saveBulk([edges[1], edges[2]]);
        }

        var ends = await scope.QueryAsync($"MATCH (s)-[r:{type}]->(t) WHERE r.group_id = $g RETURN r.uuid AS u, s.uuid AS s, t.uuid AS t");
        Assert.Equal(
            edges.Select(e => (e.Uuid, e.SourceNodeUuid, e.TargetNodeUuid)).Order(),
            ends.Select(r => (r["u"].As<string>(), r["s"].As<string>(), r["t"].As<string>())).Order());

        await ReadsAsync(scope, stored, [.. edges.Select(e => e.Uuid)], e => e.Uuid, getByUuid, getByUuids, getByGroupIds, ct);
        Assert.Equal(edges[1], await getByUuid(edges[1].Uuid, ct));

        await delete(edges[0]);
        Assert.Equal(Set(edges[1].Uuid, edges[2].Uuid), await UuidsAsync(scope, stored));
        await deleteByUuids([edges[1].Uuid]);
        Assert.Equal(Set(edges[2].Uuid), await UuidsAsync(scope, stored));
    }

    private static async Task<List<string>> EntitiesAsync(GraphitiScope scope, int count, CancellationToken ct)
    {
        List<string> uuids = [];
        for (var i = 0; i < count; i++)
        {
            uuids.Add((await scope.Graphiti.Nodes.Entity.SaveAsync(new EntityNode { Name = $"N{i}", GroupId = scope.Group }, ct)).Uuid);
        }

        return uuids;
    }

    private static async Task<List<string>> EpisodesAsync(GraphitiScope scope, int count, CancellationToken ct)
    {
        List<string> uuids = [];
        for (var i = 0; i < count; i++)
        {
            uuids.Add((await scope.Graphiti.Nodes.Episode.SaveAsync(Episode(scope.Group, $"e{i}", EpisodeType.Text, At.AddDays(i)), ct)).Uuid);
        }

        return uuids;
    }
}
