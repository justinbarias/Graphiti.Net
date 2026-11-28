// The effect of each argument of each IGraphiti call on the real Neo4j (plan 0004, layer 9, packet P9d, fix round). The
// proof of each argument is a read from Neo4j or a request that the fake chat client received.
namespace Graphiti.E2E.Tests;

using global::Neo4j.Driver;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

public sealed class GraphitiArgumentsTests
{
    private const int TimeLimit = 180_000;
    private const string Fact = "Ana works at Bo Corp.";

    private static readonly DateTimeOffset January = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly GraphTypeDefinition Person = GraphTypeDefinition.Create("Person", "A human being.")
        .AddString("occupation", "The job of the person");

    private static readonly GraphTypeDefinition WorksAt = GraphTypeDefinition.Create("WorksAt", "Employment of a person.")
        .AddInteger("since", "The year that the work started");

    // ---------------------------------------------------------------------------------------------------------------
    // AddEpisodeAsync
    // ---------------------------------------------------------------------------------------------------------------

    [Fact(Timeout = TimeLimit)]
    public async Task EpisodeInput_Uuid_processes_the_stored_episode_of_that_uuid()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var stored = await scope.Graphiti.Nodes.Episode.SaveAsync(
            GraphitiNamespaceMembersTests.Episode(scope.Group, "a1", EpisodeType.Text, January) with { Content = Fact }, ct);

        var added = await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January) with { Uuid = stored.Uuid }, new AddEpisodeOptions { GroupId = scope.Group }, ct);

        // add_episode reads the episode of the uuid and saves no second episode.
        Assert.Equal(stored.Uuid, added.Episode.Uuid);
        Assert.Equal(1, await scope.CountAsync("MATCH (n:Episodic) WHERE n.group_id = $g RETURN count(n) AS c"));
        Assert.Equal(2, await scope.CountAsync("MATCH (:Episodic {uuid: $u})-[r:MENTIONS]->(:Entity) RETURN count(r) AS c", new { u = stored.Uuid }));

        // A uuid with no episode raises NodeNotFoundError. GraphitiCallErrorTests shows it with and without a listener.
        var uuid = Guid.NewGuid().ToString();
        var missing = await Assert.ThrowsAsync<NodeNotFoundException>(() => scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a2", January) with { Uuid = uuid }, new AddEpisodeOptions { GroupId = scope.Group }, ct));
        Assert.Equal($"node {uuid} not found", missing.Message);
        Assert.Equal(1, await scope.CountAsync("MATCH (n:Episodic) WHERE n.group_id = $g RETURN count(n) AS c"));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Entity_types_reach_the_prompt_the_labels_and_the_attributes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Typed);

        var added = await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January), new AddEpisodeOptions { GroupId = scope.Group, EntityTypes = [Person] }, ct);

        Assert.Contains("Person", scope.Chat.Calls.First(c => c.Prompt == "extract_nodes.extract_text").User, StringComparison.Ordinal);
        var ana = (await scope.QueryAsync("MATCH (n:Entity {name: 'Ana'}) WHERE n.group_id = $g RETURN labels(n) AS l, n.occupation AS o")).Single();
        Assert.Equal(["Entity", "Person"], ana["l"].As<List<string>>().Order(StringComparer.Ordinal));
        Assert.Equal("pilot", ana["o"].As<string>());
        Assert.Equal("pilot", Assert.Single(added.Nodes, n => n.Name == "Ana").Attributes["occupation"]);
        var bo = (await scope.QueryAsync("MATCH (n:Entity {name: 'Bo Corp'}) WHERE n.group_id = $g RETURN labels(n) AS l")).Single();
        Assert.Equal(["Entity"], bo["l"].As<List<string>>());
    }

    [Fact(Timeout = TimeLimit)]
    public async Task The_last_entity_type_of_one_name_wins_as_in_a_dict_display()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Typed);

        await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January),
            new AddEpisodeOptions { GroupId = scope.Group, EntityTypes = [GraphTypeDefinition.Create("Person", "A human being."), Person] },
            ct);

        // The second Person has the field occupation, so the extraction fills it.
        Assert.Equal("pilot", (await scope.QueryAsync("MATCH (n:Entity {name: 'Ana'}) WHERE n.group_id = $g RETURN n.occupation AS o")).Single()["o"].As<string>());
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Excluded_entity_types_leave_the_prompt_and_the_ids_of_the_types()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Typed);

        await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January),
            new AddEpisodeOptions { GroupId = scope.Group, EntityTypes = [Person], ExcludedEntityTypes = ["Entity"] },
            ct);

        // _filter_entity_types_context drops Entity and numbers the rest from 0, so Person has id 0. The reply gives Bo
        // Corp id 0, which is now Person, and Ana id 1, which no type has. _create_entity_nodes reads an unknown id as
        // Entity, which is excluded, so the extraction drops Ana. The edge to Ana then saves Ana as a plain Entity.
        var prompt = scope.Chat.Calls.Single(c => c.Prompt == "extract_nodes.extract_text").User;
        Assert.DoesNotContain("does not fit any of the other listed", prompt, StringComparison.Ordinal);
        var labels = (await scope.QueryAsync("MATCH (n:Entity) WHERE n.group_id = $g RETURN n.name AS n, labels(n) AS l"))
            .ToDictionary(r => r["n"].As<string>(), r => string.Join(",", r["l"].As<List<string>>().Order(StringComparer.Ordinal)));
        Assert.Equal(new Dictionary<string, string> { ["Ana"] = "Entity", ["Bo Corp"] = "Entity,Person" }, labels);
    }

    [Theory(Timeout = TimeLimit)]
    [InlineData("Person", "Entity", 2020L)]
    [InlineData("Entity", "Person", null)]
    public async Task Edge_types_and_the_edge_type_map_set_the_type_and_the_attributes_of_an_edge(string source, string target, long? since)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Typed);

        await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January),
            new AddEpisodeOptions
            {
                GroupId = scope.Group,
                EntityTypes = [Person],
                EdgeTypes = [WorksAt],
                EdgeTypeMap = [new EdgeTypeRule { SourceType = source, TargetType = target, EdgeTypes = ["WorksAt"] }],
            },
            ct);

        var edge = (await scope.QueryAsync("MATCH ()-[r:RELATES_TO]->() WHERE r.group_id = $g RETURN r.name AS n, r.since AS s")).Single();
        Assert.Equal(since, edge["s"].As<long?>());
        var prompt = scope.Chat.Calls.First(c => c.Prompt == "extract_edges.edge").User;
        Assert.Contains("WorksAt", prompt, StringComparison.Ordinal);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Custom_extraction_instructions_reach_the_extraction_prompts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);

        await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January),
            new AddEpisodeOptions { GroupId = scope.Group, CustomExtractionInstructions = "ZEBRA RULES" },
            ct);

        Assert.Contains("ZEBRA RULES", scope.Chat.Calls.Single(c => c.Prompt == "extract_nodes.extract_text").User, StringComparison.Ordinal);
        Assert.Contains("ZEBRA RULES", scope.Chat.Calls.Single(c => c.Prompt == "extract_edges.edge").User, StringComparison.Ordinal);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Previous_episode_uuids_choose_the_previous_messages_of_the_prompt()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var older = await scope.Graphiti.Nodes.Episode.SaveAsync(GraphitiNamespaceMembersTests.Episode(scope.Group, "p1", EpisodeType.Text, January.AddDays(-2)), ct);
        await scope.Graphiti.Nodes.Episode.SaveAsync(GraphitiNamespaceMembersTests.Episode(scope.Group, "p2", EpisodeType.Text, January.AddDays(-1)), ct);

        await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January), new AddEpisodeOptions { GroupId = scope.Group, PreviousEpisodeUuids = [older.Uuid] }, ct);
        var chosen = scope.Chat.Calls.Single(c => c.Prompt == "extract_edges.edge").User;

        Assert.Contains("Content of p1.", chosen, StringComparison.Ordinal);
        Assert.DoesNotContain("Content of p2.", chosen, StringComparison.Ordinal);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_saga_name_saves_the_saga_and_its_episode_edges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);

        var first = await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January), new AddEpisodeOptions { GroupId = scope.Group, Saga = "trip" }, ct);
        var second = await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a2", January.AddDays(1)),
            new AddEpisodeOptions { GroupId = scope.Group, Saga = "trip", SagaPreviousEpisodeUuid = first.Episode.Uuid },
            ct);

        var rows = await scope.QueryAsync("MATCH (s:Saga {name: 'trip'})-[:HAS_EPISODE]->(e:Episodic) WHERE s.group_id = $g RETURN s.uuid AS s, e.uuid AS e");
        Assert.Single(rows.Select(r => r["s"].As<string>()).Distinct());
        Assert.Equal(GraphitiNamespaceMembersTests.Set(first.Episode.Uuid, second.Episode.Uuid), rows.Select(r => r["e"].As<string>()).ToHashSet());
        Assert.Equal(1, await scope.CountAsync(
            "MATCH (:Episodic {uuid: $a})-[r:NEXT_EPISODE]->(:Episodic {uuid: $b}) RETURN count(r) AS c", new { a = first.Episode.Uuid, b = second.Episode.Uuid }));

        // summarize_saga returns the whole saga node: each field equals the stored saga.
        var saga = await scope.Graphiti.SummarizeSagaAsync(rows[0]["s"].As<string>(), scope.Group, ct);
        var stored = (await scope.QueryAsync("MATCH (s:Saga {uuid: $u}) RETURN s", new { u = saga.Uuid })).Single()["s"].As<INode>().Properties;
        Assert.Equal(
            (stored["summary"].As<string>(), stored["first_episode_uuid"].As<string>(), stored["last_episode_uuid"].As<string>()),
            (saga.Summary, saga.FirstEpisodeUuid, saga.LastEpisodeUuid));
        Assert.Equal((first.Episode.Uuid, second.Episode.Uuid), (saga.FirstEpisodeUuid, saga.LastEpisodeUuid));
        Assert.Equal(stored["last_summarized_at"].As<ZonedDateTime>().ToDateTimeOffset(), saga.LastSummarizedAt);
        Assert.Equal(stored["last_summarized_episode_valid_at"].As<ZonedDateTime>().ToDateTimeOffset(), saga.LastSummarizedEpisodeValidAt);
        Assert.Equal(January.AddDays(1), saga.LastSummarizedEpisodeValidAt);
        Assert.NotEqual(saga.LastSummarizedAt, saga.LastSummarizedEpisodeValidAt);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_saga_node_links_the_episode_to_that_saga()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var uuid = Guid.NewGuid().ToString();
        await scope.QueryAsync(
            "CREATE (:Saga {uuid: $u, name: 'trip', group_id: $g, summary: '', created_at: datetime('2026-01-01T00:00:00Z')})", new { u = uuid });
        var saga = await scope.Graphiti.Nodes.Saga.GetByUuidAsync(uuid, ct) with
        {
            Summary = "An old summary.",
            FirstEpisodeUuid = "first-episode",
            LastSummarizedAt = new DateTimeOffset(2025, 3, 3, 3, 3, 3, TimeSpan.FromHours(1)),
            LastSummarizedEpisodeValidAt = new DateTimeOffset(2025, 4, 4, 4, 4, 4, TimeSpan.FromHours(-2)),
        };

        var added = await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January), new AddEpisodeOptions { GroupId = scope.Group, SagaNode = saga }, ct);

        Assert.Equal(1, await scope.CountAsync(
            "MATCH (:Saga {uuid: $s})-[r:HAS_EPISODE]->(:Episodic {uuid: $e}) RETURN count(r) AS c", new { s = uuid, e = added.Episode.Uuid }));
        Assert.Equal(1, await scope.CountAsync("MATCH (s:Saga) WHERE s.group_id = $g RETURN count(s) AS c"));

        // add_episode sets the last episode of the saga node and saves the whole node (graphiti.py:1243-1246).
        var stored = (await scope.QueryAsync("MATCH (s:Saga {uuid: $u}) RETURN s", new { u = uuid })).Single()["s"].As<INode>().Properties;
        Assert.Equal(
            ("An old summary.", "first-episode", added.Episode.Uuid),
            (stored["summary"].As<string>(), stored["first_episode_uuid"].As<string>(), stored["last_episode_uuid"].As<string>()));
        Assert.Equal(saga.LastSummarizedAt, stored["last_summarized_at"].As<ZonedDateTime>().ToDateTimeOffset());
        Assert.Equal(saga.LastSummarizedEpisodeValidAt, stored["last_summarized_episode_valid_at"].As<ZonedDateTime>().ToDateTimeOffset());
    }

    [Fact(Timeout = TimeLimit)]
    public async Task With_StoreRawEpisodeContent_off_the_episode_is_saved_with_no_content()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana, o => o.StoreRawEpisodeContent = false);

        var added = await GraphitiMembersTests.AddAnaAsync(scope, ct);

        Assert.Equal(string.Empty, (await scope.QueryAsync("MATCH (n:Episodic {uuid: $u}) RETURN n.content AS c", new { u = added.Episode.Uuid })).Single()["c"].As<string>());
        Assert.Contains(Fact, scope.Chat.Calls.Single(c => c.Prompt == "extract_nodes.extract_text").User, StringComparison.Ordinal);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Identity_properties_reach_the_validation_of_each_call()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Typed);
        var identity = new Dictionary<string, IReadOnlyList<string>> { ["Person"] = ["email"] };

        // validate_identity_properties: each property must be a field of the entity type. Person has no field email.
        await Assert.ThrowsAsync<IdentityPropertyValidationException>(() => scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", January), new AddEpisodeOptions { GroupId = scope.Group, EntityTypes = [Person], IdentityProperties = identity }, ct));
        await Assert.ThrowsAsync<IdentityPropertyValidationException>(() => scope.Graphiti.AddEpisodeBulkAsync(
            [GraphitiMembersTests.Episode("b1", January)], new AddEpisodeBulkOptions { GroupId = scope.Group, EntityTypes = [Person], IdentityProperties = identity }, ct));
        Assert.Equal(0, await scope.CountAsync("MATCH (n) WHERE n.group_id = $g RETURN count(n) AS c"));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Identity_properties_of_AddTripletAsync_resolve_a_node_by_its_identity()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var stored = await scope.Graphiti.Nodes.Entity.SaveAsync(
            new EntityNode { Name = "Ana", GroupId = scope.Group, Labels = ["Person"], Attributes = new Dictionary<string, object?> { ["email"] = "ana@example.org" } }, ct);
        var source = new EntityNode { Name = "A. Silva", GroupId = scope.Group, Labels = ["Person"], Attributes = new Dictionary<string, object?> { ["email"] = "ana@example.org" } };
        var target = new EntityNode { Name = "Bo Corp", GroupId = scope.Group };

        var result = await scope.Graphiti.AddTripletAsync(
            source,
            new EntityEdge { GroupId = scope.Group, SourceNodeUuid = source.Uuid, TargetNodeUuid = target.Uuid, CreatedAt = January, Name = "WORKS_AT", Fact = Fact },
            target,
            new AddTripletOptions { IdentityProperties = new Dictionary<string, IReadOnlyList<string>> { ["Person"] = ["email"] } },
            ct);

        // The new source node has the email of the stored node, so it resolves to the stored node.
        Assert.Equal(stored.Uuid, result.Nodes[0].Uuid);
        Assert.Equal(
            GraphitiNamespaceMembersTests.Set(stored.Uuid, result.Nodes[1].Uuid),
            await GraphitiNamespaceMembersTests.UuidsAsync(scope, "MATCH (n:Entity) WHERE n.group_id = $g RETURN n.uuid AS u"));
        Assert.Equal(1, await scope.CountAsync("MATCH (:Entity {uuid: $s})-[r:RELATES_TO]->() RETURN count(r) AS c", new { s = stored.Uuid }));
    }

    [Theory(Timeout = TimeLimit)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UseCombinedExtraction_chooses_the_prompts_of_the_bulk_call(bool combined)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);

        var result = await scope.Graphiti.AddEpisodeBulkAsync(
            [GraphitiMembersTests.Episode("b1", January) with { Source = EpisodeType.Message }],
            new AddEpisodeBulkOptions { GroupId = scope.Group, UseCombinedExtraction = combined },
            ct);

        Assert.Equal(combined, scope.Chat.Calls.Any(c => c.Prompt.StartsWith("extract_nodes_and_edges.", StringComparison.Ordinal)));
        Assert.Equal(!combined, scope.Chat.Calls.Any(c => c.Prompt == "extract_edges.edge"));

        // The result holds the stored facts and nodes.
        var edge = Assert.Single(result.Edges);
        Assert.Equal(Fact, edge.Fact);
        Assert.Equal(1, await scope.CountAsync("MATCH ()-[r:RELATES_TO {uuid: $u}]->() WHERE r.fact = $f RETURN count(r) AS c", new { u = edge.Uuid, f = Fact }));
        Assert.Equal(
            (await GraphitiNamespaceMembersTests.UuidsAsync(scope, "MATCH (n:Entity) WHERE n.group_id = $g RETURN n.uuid AS u")).Order(StringComparer.Ordinal),
            result.Nodes.Select(n => n.Uuid).Order(StringComparer.Ordinal));
        Assert.Equal(2, result.EpisodicEdges.Count);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_bulk_saga_and_entity_types_reach_the_graph()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Typed);

        var result = await scope.Graphiti.AddEpisodeBulkAsync(
            [GraphitiMembersTests.Episode("b1", January)],
            new AddEpisodeBulkOptions { GroupId = scope.Group, EntityTypes = [Person], Saga = "trip", UseCombinedExtraction = false },
            ct);

        Assert.Equal(1, await scope.CountAsync(
            "MATCH (s:Saga {name: 'trip'})-[:HAS_EPISODE]->(e:Episodic {uuid: $u}) WHERE s.group_id = $g RETURN count(e) AS c", new { u = result.Episodes[0].Uuid }));
        Assert.Contains("Person", (await scope.QueryAsync("MATCH (n:Entity {name: 'Ana'}) WHERE n.group_id = $g RETURN labels(n) AS l")).Single()["l"].As<List<string>>());
    }

    [Fact(Timeout = TimeLimit)]
    public async Task An_episode_of_AddTripletAsync_is_the_provenance_of_the_edge()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var episode = GraphitiNamespaceMembersTests.Episode(scope.Group, "t1", EpisodeType.Text, January);
        var ana = new EntityNode { Name = "Ana", GroupId = scope.Group };
        var bo = new EntityNode { Name = "Bo Corp", GroupId = scope.Group };
        EntityEdge Edge(string fact) => new() { GroupId = scope.Group, SourceNodeUuid = ana.Uuid, TargetNodeUuid = bo.Uuid, CreatedAt = January, Name = "WORKS_AT", Fact = fact };
        var first = await scope.Graphiti.AddTripletAsync(
            ana with { Attributes = new Dictionary<string, object?> { ["zeta"] = 1L, ["alpha"] = 2L } }, Edge(Fact), bo, cancellationToken: ct);

        // The attributes keep the order of the dict of the caller (a Python dict keeps the insertion order).
        Assert.Equal(["zeta", "alpha"], first.Nodes[0].Attributes.Keys);

        var result = await scope.Graphiti.AddTripletAsync(ana, Edge(Fact), bo, new AddTripletOptions { Episode = episode }, ct);

        // The second edge repeats the fact of the first edge, so _dedupe_extracted_edge reuses the stored edge and
        // appends the uuid of the episode to its episodes.
        var edge = Assert.Single(result.Edges);
        Assert.Contains(episode.Uuid, edge.Episodes);
        var stored = (await scope.QueryAsync("MATCH ()-[r:RELATES_TO]->() WHERE r.group_id = $g RETURN r.uuid AS u, r.episodes AS e")).Single();
        Assert.Equal(edge.Uuid, stored["u"].As<string>());
        Assert.Contains(episode.Uuid, stored["e"].As<List<string>>());
    }

    [Fact(Timeout = TimeLimit)]
    public async Task LastN_and_the_saga_choose_the_retrieved_episodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var episodes = new List<EpisodicNode>();
        for (var i = 0; i < 4; i++)
        {
            episodes.Add(await scope.Graphiti.Nodes.Episode.SaveAsync(
                GraphitiNamespaceMembersTests.Episode(scope.Group, $"r{i}", EpisodeType.Text, January.AddDays(i)), ct));
        }

        await scope.QueryAsync(
            "CREATE (s:Saga {uuid: $s, name: 'trip', group_id: $g, summary: '', created_at: datetime('2026-01-01T00:00:00Z')}) "
            + "WITH s MATCH (e:Episodic) WHERE e.uuid IN $es CREATE (s)-[:HAS_EPISODE {uuid: e.uuid + '-h', group_id: $g, created_at: datetime('2026-01-01T00:00:00Z')}]->(e)",
            new { s = Guid.NewGuid().ToString(), es = new[] { episodes[0].Uuid, episodes[2].Uuid } });
        var at = January.AddDays(10);

        // The last episodes, in time order.
        Assert.Equal(
            [episodes[1].Uuid, episodes[2].Uuid, episodes[3].Uuid],
            (await scope.Graphiti.RetrieveEpisodesAsync(at, new RetrieveEpisodesOptions { GroupIds = [scope.Group] }, ct)).Select(e => e.Uuid));
        Assert.Equal(
            [episodes[2].Uuid, episodes[3].Uuid],
            (await scope.Graphiti.RetrieveEpisodesAsync(at, new RetrieveEpisodesOptions { GroupIds = [scope.Group], LastN = 2 }, ct)).Select(e => e.Uuid));
        Assert.Equal(
            [episodes[0].Uuid, episodes[2].Uuid],
            (await scope.Graphiti.RetrieveEpisodesAsync(at, new RetrieveEpisodesOptions { GroupIds = [scope.Group], Saga = "trip" }, ct)).Select(e => e.Uuid));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task The_group_id_of_a_call_must_be_the_group_of_the_episode_or_the_saga()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        var added = await GraphitiMembersTests.AddAnaAsync(scope, ct);
        var saga = Guid.NewGuid().ToString();
        await scope.QueryAsync(
            "MATCH (e:Episodic {uuid: $e}) CREATE (s:Saga {uuid: $s, name: 'trip', group_id: $g, summary: '', created_at: datetime('2026-01-01T00:00:00Z')})"
            + "-[:HAS_EPISODE {uuid: $s + '-h', group_id: $g, created_at: datetime('2026-01-01T00:00:00Z')}]->(e)",
            new { e = added.Episode.Uuid, s = saga });
        var other = Neo4jSidecar.NewGroupId();

        await Assert.ThrowsAsync<NodeNotFoundException>(() => scope.Graphiti.GetNodesAndEdgesByEpisodeAsync([added.Episode.Uuid], other, ct));
        await Assert.ThrowsAsync<NodeNotFoundException>(() => scope.Graphiti.RemoveEpisodeAsync(added.Episode.Uuid, other, ct));
        await Assert.ThrowsAsync<NodeNotFoundException>(() => scope.Graphiti.SummarizeSagaAsync(saga, other, ct));
        Assert.DoesNotContain(scope.Chat.Calls, c => c.Prompt == "summarize_sagas.summarize_saga");
        Assert.Equal(1, await scope.CountAsync("MATCH (n:Episodic {uuid: $u}) RETURN count(n) AS c", new { u = added.Episode.Uuid }));

        Assert.Equal(2, (await scope.Graphiti.GetNodesAndEdgesByEpisodeAsync([added.Episode.Uuid], scope.Group, ct)).Nodes.Count);
        Assert.Equal(saga, (await scope.Graphiti.SummarizeSagaAsync(saga, scope.Group, ct)).Uuid);
        await scope.Graphiti.RemoveEpisodeAsync(added.Episode.Uuid, scope.Group, ct);
        Assert.Equal(0, await scope.CountAsync("MATCH (n:Episodic {uuid: $u}) RETURN count(n) AS c", new { u = added.Episode.Uuid }));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_canceled_token_stops_each_kind_of_call_before_it_starts()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        var token = canceled.Token;
        var node = new EntityNode { Name = "Ana", GroupId = scope.Group };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Graphiti.SearchAsync("Ana", new SearchOptions { GroupIds = [scope.Group] }, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Graphiti.AdvancedSearchAsync("Ana", SearchRecipes.EdgeHybridSearchRrf, cancellationToken: token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Graphiti.AddEpisodeBulkAsync(
            [GraphitiMembersTests.Episode("b1", January)], new AddEpisodeBulkOptions { GroupId = scope.Group }, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Graphiti.Nodes.Entity.SaveAsync(node, token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Graphiti.Edges.Entity.GetByGroupIdsAsync([scope.Group], cancellationToken: token));

        Assert.Empty(scope.Chat.Calls);
        Assert.Equal(0, scope.Embedder.Calls);
        Assert.Equal(0, await scope.CountAsync("MATCH (n) WHERE n.group_id = $g RETURN count(n) AS c"));
        Assert.Equal("Ana", (await scope.Graphiti.Nodes.Entity.SaveAsync(node, ct)).Name);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_cancel_during_a_call_reaches_the_ported_call()
    {
        var ct = TestContext.Current.CancellationToken;

        // The embedder cancels the token at its first call. The ported call passes the token to the embedder, so the
        // embedder throws.
        using (var during = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var embedder = new HashEmbeddingGenerator { OnCall = during.Cancel };
            await using var scope = new GraphitiScope(GraphitiMembersTests.Ana, register: s => s.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embedder));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Graphiti.SearchAsync("Ana", new SearchOptions { GroupIds = [scope.Group] }, during.Token));
            Assert.Equal(1, embedder.Calls);
        }

        using (var during = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var embedder = new HashEmbeddingGenerator { OnCall = during.Cancel };
            await using var scope = new GraphitiScope(GraphitiMembersTests.Ana, register: s => s.AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(embedder));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Graphiti.Nodes.Entity.SaveAsync(new EntityNode { Name = "Ana", GroupId = scope.Group }, during.Token));
            Assert.Equal(0, await scope.CountAsync("MATCH (n) WHERE n.group_id = $g RETURN count(n) AS c"));
        }

        using (var during = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            await using var scope = new GraphitiScope((prompt, user) =>
            {
                during.Cancel();
                return GraphitiMembersTests.Ana(prompt, user);
            });
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Graphiti.AddEpisodeBulkAsync(
                [GraphitiMembersTests.Episode("b1", January)], new AddEpisodeBulkOptions { GroupId = scope.Group }, during.Token));
            Assert.Equal(0, await scope.CountAsync("MATCH ()-[r:RELATES_TO]->() WHERE r.group_id = $g RETURN count(r) AS c"));
        }
    }

    /// <summary>The replies of <see cref="GraphitiMembersTests.Ana"/> with Ana as a Person, and the attribute replies.</summary>
    internal static string? Typed(string prompt, string user) => prompt switch
    {
        "extract_nodes.extract_text" =>
            """{"extracted_entities": [{"name": "Ana", "entity_type_id": 1}, {"name": "Bo Corp", "entity_type_id": 0}]}""",
        "extract_edges.edge" => """
            {"edges": [{"source_entity_name": "Ana", "target_entity_name": "Bo Corp", "relation_type": "WorksAt",
              "fact": "Ana works at Bo Corp.", "valid_at": "2026-01-01T00:00:00Z", "invalid_at": null}]}
            """,
        "extract_nodes.extract_attributes" => """{"occupation": "pilot"}""",
        "extract_edges.extract_attributes" => """{"since": 2020}""",
        _ => GraphitiMembersTests.Ana(prompt, user),
    };

    /// <summary>The replies of one entity and no fact.</summary>
    internal static string? Solo(string prompt, string user) => prompt switch
    {
        "extract_nodes.extract_text" => """{"extracted_entities": [{"name": "Ana", "entity_type_id": 0}]}""",
        "extract_edges.edge" => """{"edges": []}""",
        _ => GraphitiMembersTests.Ana(prompt, user),
    };
}
