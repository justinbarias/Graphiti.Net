// Port of .upstream/graphiti/tests/test_episode_group_isolation.py (port/RULES.md, R25): the tests whose code under test is
// the Graphiti class. The other tests of the file are in tests/Port.Evaluation, one class TestEpisodeGroupIsolation in three
// files: test_episode_uuid_query_binds_requested_group, test_predecessor_list_rejects_missing_scoped_uuid,
// test_custom_episode_lookup_rejects_foreign_result, and test_unscoped_episode_lookup_remains_supported
// (NodesTestEpisodeGroupIsolation.cs), test_saga_edge_queries_bind_both_endpoints (TestEpisodeGroupIsolation.cs), and the six
// tests test_entity_edge_* (EdgesTestEpisodeGroupIsolation.cs). They are not ported a second time.
//
// PORT: `Graphiti.__new__(Graphiti)` is RuntimeHelpers.GetUninitializedObject, as in TestSagaRefetchEpisodeUuids. The tests
// replace the instance attribute `_resolve_request_scope` with a lambda, and they set `driver`, `clients`, `max_coroutines`
// with plain objects. A C# method has no replacement. The real ResolveRequestScope runs on a driver at the network boundary
// (StubDriver), whose `clone` gives back the driver that the test names, so the scope is the same as the lambda gives. Each
// `clients` is a GraphitiClients record of fakes. The tests that patch module functions and class methods (O10) run the real
// functions on the stub driver, and they assert the queries that the stub sees (the first case of the reading of O10 in
// Part 3 of port/RULES.md): the patched function returns what the stub answers for its query.
namespace Graphiti.E2E.Tests;

using System.Runtime.CompilerServices;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Port.Evaluation;
using Graphiti.PyCompat;

public sealed class TestEpisodeGroupIsolation
{
    private const int TimeLimit = 180_000;
    private const double GuardSeconds = 150;

    private static async Task Guard(Func<Task> body) => await LoopGuard.RunAsync(body, GuardSeconds);

    private static GraphitiCore Bare() => (GraphitiCore)RuntimeHelpers.GetUninitializedObject(typeof(GraphitiCore));

    private static GraphitiClients ClientsOf(GraphDriver driver, MockLlmClient? llm = null) => new()
    {
        Driver = driver,
        LlmClient = llm ?? new MockLlmClient(),
        Embedder = new MockEmbedder(),
        CrossEncoder = new MockCrossEncoder(),
        Tracer = new NoOpTracer(),
    };

    private static OrderedDictionary<string, object?> EpisodeRecord(string uuid = "episode-a", string groupId = "group-a", params string[] entityEdges) => new()
    {
        ["uuid"] = uuid,
        ["name"] = "episode",
        ["group_id"] = groupId,
        ["created_at"] = PyClock.Utc2026,
        ["source"] = Py.EnumValue(EpisodeType.Text),
        ["source_description"] = "test",
        ["content"] = "safe content",
        ["valid_at"] = PyClock.Utc2026,
        ["entity_edges"] = entityEdges.Select(uuid => (object?)uuid).ToList(),
    };

    private static OrderedDictionary<string, object?> EntityNodeRecord(EntityNode node) => new()
    {
        ["uuid"] = node.Uuid,
        ["name"] = node.Name,
        ["group_id"] = node.GroupId,
        ["created_at"] = PyClock.Utc2026,
        ["summary"] = string.Empty,
        ["labels"] = new List<object?> { "Entity" },
        ["attributes"] = new OrderedDictionary<string, object?>(),
    };

    private static OrderedDictionary<string, object?> EntityEdgeRecord(string uuid, string groupId, string source, string target, string fact) => new()
    {
        ["uuid"] = uuid,
        ["source_node_uuid"] = source,
        ["target_node_uuid"] = target,
        ["fact"] = fact,
        ["fact_embedding"] = null,
        ["name"] = "RELATED_TO",
        ["group_id"] = groupId,
        ["episodes"] = new List<object?>(),
        ["created_at"] = PyClock.Utc2026,
        ["expired_at"] = null,
        ["valid_at"] = null,
        ["invalid_at"] = null,
        ["reference_time"] = null,
        ["attributes"] = new OrderedDictionary<string, object?>(),
    };

    // test_episode_uuid_query_binds_requested_group, test_predecessor_list_rejects_missing_scoped_uuid,
    // test_custom_episode_lookup_rejects_foreign_result, and test_unscoped_episode_lookup_remains_supported: see the header.

    [Fact(Timeout = TimeLimit)]
    public async Task test_saga_predecessor_query_binds_saga_and_episode_group()
    {
        var ct = TestContext.Current.CancellationToken;
        await Guard(async () =>
        {
            var driver = new StubDriver();
            var graphiti = Bare();

            var result = await graphiti.SagaGetPreviousEpisodeUuidAsync("saga-a", "episode-a", "group-a", driver: driver, cancellationToken: ct);

            var (query, @params) = driver.Calls[0];
            Assert.Contains("Saga {uuid: $saga_uuid, group_id: $group_id}", query, StringComparison.Ordinal);
            Assert.Contains("Episodic {group_id: $group_id}", query, StringComparison.Ordinal);
            Assert.Equal("group-a", @params["group_id"]);
            Assert.Null(result);
        });
    }

    // PORT: `_concurrent_ingestion_enabled = lambda group_id: True` is a configuration with `enabled_for_group` true.
    // `_get_concurrent_ingestor = Mock(side_effect=AssertionError('scheduler must not run'))` and `get_ingestor.assert_not_called()`
    // have no replacement. If the call reached the scheduler, add_episode would run the ingestion on the stub driver and the
    // mock model, and it would not raise NodeNotFoundException for the foreign uuid. The assertion of the error type shows that
    // the call stopped before the scheduler.
    [Fact(Timeout = TimeLimit)]
    public async Task test_concurrent_ingestion_rejects_foreign_uuid_before_scheduler()
    {
        var ct = TestContext.Current.CancellationToken;
        await Guard(async () =>
        {
            var driver = new StubDriver([EpisodeRecord(uuid: "episode-b", groupId: "group-b")]);
            var graphiti = Bare();
            graphiti.Driver = driver;
            graphiti.Clients = ClientsOf(driver);
            graphiti.Tracer = new NoOpTracer();
            graphiti.ConcurrentIngestion = new Graphiti.Core.Ingestion.ConcurrentIngestionConfig { Window = 2, EnabledForGroup = _ => true };

            var error = await Assert.ThrowsAsync<NodeNotFoundException>(() => graphiti.AddEpisodeAsync(
                name: "episode",
                episodeBody: "caller content",
                sourceDescription: "test",
                referenceTime: PyClock.Utc2026,
                groupId: "group-a",
                uuid: "episode-b",
                cancellationToken: ct));
            Assert.Contains("episode-b", error.Message, StringComparison.Ordinal);
        });
    }

    // test_saga_edge_queries_bind_both_endpoints: see the header.

    [Fact(Timeout = TimeLimit)]
    public async Task test_triplet_rejects_mismatched_group_before_processing()
    {
        var ct = TestContext.Current.CancellationToken;
        await Guard(async () =>
        {
            var now = PyClock.Utc2026;
            var source = new EntityNode { Name = "source", GroupId = "group-b" };
            var target = new EntityNode { Name = "target", GroupId = "group-a" };
            var edge = new EntityEdge
            {
                SourceNodeUuid = source.Uuid,
                TargetNodeUuid = target.Uuid,
                Name = "RELATES_TO",
                Fact = "source relates to target",
                GroupId = "group-a",
                CreatedAt = now,
            };

            var error = await Assert.ThrowsAsync<ArgumentException>(() => Bare().AddTripletAsync(source, edge, target, cancellationToken: ct));
            Assert.Contains("does not belong to group group-a", error.Message, StringComparison.Ordinal);
        });
    }

    // test_entity_edge_uuid_lookup_filters_requested_group through test_entity_edge_bulk_lookup_filters_graph_operations_result:
    // see the header.

    // O10: the upstream test patches EpisodicNode.get_by_uuids, EntityEdge.get_by_uuids, and graphiti_module.get_mentioned_nodes
    // and asserts the group that reaches the edge lookup. The stub driver answers the three queries (the episode with an
    // edge uuid, no edge, no mentioned node), and the assertion reads the group parameter of the edge query.
    [Fact(Timeout = TimeLimit)]
    public async Task test_episode_provenance_forwards_group_to_entity_edge_lookup()
    {
        var ct = TestContext.Current.CancellationToken;
        await Guard(async () =>
        {
            var driver = new StubDriver(route: (query, _) =>
                query.Contains("RELATES_TO", StringComparison.Ordinal) || query.Contains("MENTIONS", StringComparison.Ordinal)
                    ? []
                    : [EpisodeRecord("episode-a", "group-a", "edge-a")]);
            var graphiti = Bare();
            graphiti.Driver = driver;
            graphiti.MaxCoroutines = 1;
            graphiti.Clients = ClientsOf(driver);

            await graphiti.GetNodesAndEdgesByEpisodeAsync(["episode-a"], groupId: "group-a", cancellationToken: ct);

            var edgeLookups = driver.Calls.Where(call => call.Query.Contains("RELATES_TO", StringComparison.Ordinal) && call.Params.ContainsKey("uuids")).ToList();
            var lookup = Assert.Single(edgeLookups);
            Assert.Equal("group-a", lookup.Params["group_id"]);
            Assert.Equal(new List<string> { "edge-a" }, lookup.Params["uuids"]);
        });
    }

    // O10: the upstream test also patches EpisodicNode.delete and Node.delete_by_uuids with AsyncMocks. The real calls run
    // on the stub driver and delete nothing (the stub holds no data). The assertion is the group of the edge lookup.
    [Fact(Timeout = TimeLimit)]
    public async Task test_remove_episode_forwards_group_to_entity_edge_lookup()
    {
        var ct = TestContext.Current.CancellationToken;
        await Guard(async () =>
        {
            var driver = new StubDriver(route: (query, _) =>
                query.Contains("RELATES_TO", StringComparison.Ordinal) || query.Contains("MENTIONS", StringComparison.Ordinal) || query.Contains("DELETE", StringComparison.Ordinal)
                    ? []
                    : [EpisodeRecord("episode-a", "group-a", "edge-a")]);
            var graphiti = Bare();
            graphiti.Driver = driver;
            graphiti.Clients = ClientsOf(driver);
            graphiti.MaxCoroutines = 1;

            await graphiti.RemoveEpisodeAsync("episode-a", groupId: "group-a", cancellationToken: ct);

            var edgeLookups = driver.Calls.Where(call => call.Query.Contains("RELATES_TO", StringComparison.Ordinal) && call.Params.ContainsKey("uuids") && !call.Query.Contains("DELETE", StringComparison.Ordinal)).ToList();
            var lookup = Assert.Single(edgeLookups);
            Assert.Equal("group-a", lookup.Params["group_id"]);
        });
    }

    // O10: the upstream test patches SagaNode.get_by_uuid, generate_prompt_response, SagaNode.save, and the method
    // _saga_get_episode_contents. The stub driver of the scope answers the saga query with a saga of another group. The
    // assertions: the saga lookup went to the driver of the scope (`saga_lookup_drivers == [driver]`: the shared driver of the
    // object gets no query), the model has no call, and the scope driver gets no other query (no episode fetch, no write).
    [Fact(Timeout = TimeLimit)]
    public async Task test_summarize_saga_rejects_foreign_group_before_llm_or_write()
    {
        var ct = TestContext.Current.CancellationToken;
        await Guard(async () =>
        {
            var scoped = new StubDriver(route: (query, _) =>
                query.Contains("Saga", StringComparison.Ordinal)
                    ? [new OrderedDictionary<string, object?>
                    {
                        ["uuid"] = "saga-b",
                        ["name"] = "saga",
                        ["group_id"] = "group-b",
                        ["created_at"] = PyClock.Utc2026,
                    }]
                    : []);
            var shared = new StubDriver { CloneTo = scoped };
            var llm = new MockLlmClient();
            var graphiti = Bare();
            graphiti.Driver = shared;
            graphiti.Clients = ClientsOf(shared, llm);

            var error = await Assert.ThrowsAsync<NodeNotFoundException>(() => graphiti.SummarizeSagaAsync("saga-b", groupId: "group-a", cancellationToken: ct));
            Assert.Contains("saga-b", error.Message, StringComparison.Ordinal);

            Assert.Empty(shared.Calls);
            var only = Assert.Single(scoped.Calls);
            Assert.Contains("Saga", only.Query, StringComparison.Ordinal);
            Assert.DoesNotContain("HAS_EPISODE", only.Query, StringComparison.Ordinal);
            Assert.Equal(0, llm.AwaitCount);
        });
    }

    // PORT: the upstream test sets `graphiti.driver`, `graphiti.llm_client`, and the lambda `_resolve_request_scope`, and it
    // builds no `clients`. The C# class is built with its constructor on the real Neo4j driver (the Neo4j driver's clone returns
    // the driver itself, so the real scope is the driver of the test). The model is the mock that captures the messages and
    // answers {'summary': 'updated summary'}. The test works in the two groups of the fixture, and it deletes only them.
    [Fact(Timeout = TimeLimit)]
    public async Task test_summarize_saga_excludes_legacy_foreign_group_episode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var groupA = g.GroupId;
            var groupB = g.GroupId2;
            var createdAt = PyClock.Now();
            var saga = new SagaNode { Uuid = Guid.NewGuid().ToString(), Name = "saga summary group test", GroupId = groupA, CreatedAt = createdAt };
            var episodeA = new EpisodicNode
            {
                Uuid = Guid.NewGuid().ToString(),
                Name = "group A episode",
                GroupId = groupA,
                Source = EpisodeType.Text,
                SourceDescription = "test",
                Content = "alphamarker group A episode content",
                ValidAt = createdAt,
                CreatedAt = createdAt,
            };
            var episodeB = new EpisodicNode
            {
                Uuid = Guid.NewGuid().ToString(),
                Name = "group B episode",
                GroupId = groupB,
                Source = EpisodeType.Text,
                SourceDescription = "test",
                Content = "betamarker group B episode content",
                ValidAt = createdAt,
                CreatedAt = createdAt,
            };

            var llm = new MockLlmClient
            {
                Script = (_, _) => new OrderedDictionary<string, object?> { ["summary"] = "updated summary" },
            };
            var graphiti = g.Graphiti(llm, new MockEmbedder());

            await saga.SaveAsync(g.Driver, ct);
            await episodeA.SaveAsync(g.Driver, ct);
            await episodeB.SaveAsync(g.Driver, ct);
            await new HasEpisodeEdge
            {
                SourceNodeUuid = saga.Uuid,
                TargetNodeUuid = episodeA.Uuid,
                GroupId = groupA,
                CreatedAt = createdAt,
            }.SaveAsync(g.Driver, ct);
            await g.Driver.ExecuteQueryAsync(
                """
                MATCH (s:Saga {uuid: $saga_uuid}), (e:Episodic {uuid: $episode_uuid})
                CREATE (s)-[link:HAS_EPISODE]->(e)
                SET link.uuid = randomUUID(),
                    link.group_id = s.group_id,
                    link.created_at = datetime()
                """,
                new() { ["saga_uuid"] = saga.Uuid, ["episode_uuid"] = episodeB.Uuid },
                ct);

            await graphiti.SummarizeSagaAsync(saga.Uuid, groupId: groupA, cancellationToken: ct);
            var scopedPrompt = MockLlmClient.Text(llm.Calls.Last());
            Assert.Contains("alphamarker group A episode content", scopedPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("betamarker", scopedPrompt, StringComparison.Ordinal);

            await g.Driver.ExecuteQueryAsync(
                """
                MATCH (s:Saga {uuid: $saga_uuid})
                SET s.last_summarized_at = null,
                    s.last_summarized_episode_valid_at = null,
                    s.summary = ''
                """,
                new() { ["saga_uuid"] = saga.Uuid },
                ct);
            await graphiti.SummarizeSagaAsync(saga.Uuid, cancellationToken: ct);
            var unscopedPrompt = MockLlmClient.Text(llm.Calls.Last());
            Assert.Contains("alphamarker group A episode content", unscopedPrompt, StringComparison.Ordinal);
            Assert.DoesNotContain("betamarker", unscopedPrompt, StringComparison.Ordinal);
        });
    }

    // O10: the upstream test patches EntityNode.get_by_uuid, EntityEdge.get_by_uuid (and records its arguments), and
    // add_nodes_and_edges_bulk. The stub driver answers the node queries with the two nodes and the edge query with the
    // foreign edge. The assertions: the edge lookup is one query with no group (`('edge-b', {})`), and nothing is written (the
    // write needs a session of the driver, and the stub counts the requests of one).
    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_rejects_foreign_edge_uuid_without_saving()
    {
        var ct = TestContext.Current.CancellationToken;
        await Guard(async () =>
        {
            var source = new EntityNode { Name = "source", GroupId = "group-a", NameEmbedding = [0.1] };
            var target = new EntityNode { Name = "target", GroupId = "group-a", NameEmbedding = [0.1] };
            var edge = new EntityEdge
            {
                Uuid = "edge-b",
                SourceNodeUuid = source.Uuid,
                TargetNodeUuid = target.Uuid,
                Name = "RELATED_TO",
                Fact = "source relates to target",
                FactEmbedding = [0.1],
                GroupId = "group-a",
                CreatedAt = PyClock.Utc2026,
            };
            var driver = new StubDriver(route: (query, @params) =>
            {
                if (query.Contains("RELATES_TO", StringComparison.Ordinal))
                {
                    return [EntityEdgeRecord("edge-b", "group-b", "foreign-source", "foreign-target", "foreign fact")];
                }

                return [EntityNodeRecord((string)@params["uuid"]! == source.Uuid ? source : target)];
            });
            var graphiti = Bare();
            graphiti.Driver = driver;
            graphiti.Clients = ClientsOf(driver);
            graphiti.Embedder = new MockEmbedder();

            var error = await Assert.ThrowsAsync<EdgeNotFoundException>(() => graphiti.AddTripletAsync(source, edge, target, cancellationToken: ct));
            Assert.Contains("edge-b", error.Message, StringComparison.Ordinal);

            var edgeLookups = driver.Calls.Where(call => call.Query.Contains("RELATES_TO", StringComparison.Ordinal)).ToList();
            var lookup = Assert.Single(edgeLookups);
            Assert.Equal("edge-b", lookup.Params["uuid"]);
            Assert.Null(lookup.Params["group_id"]);
            Assert.Equal(0, driver.SessionRequests);
        });
    }
}
