// Port of .upstream/graphiti/tests/test_graphiti_mock.py (port/RULES.md, R25) on the real Neo4j.
//
// Each test of the file uses the graph_driver fixture, which runs for each driver. Only Neo4j is ported (decision D10), so
// the tests that skip FalkorDB run here. The model, the embedder, and the cross-encoder are the mocks of the upstream file
// (GraphitiClassTestSupport.cs). As upstream, the model mock replaces generate_response, which is above the network call.
//
// PORT: the upstream test clears the groups `graphiti_test_group` and `graphiti_test_group_2` first and reads the whole
// graph in two places. The shared Neo4j holds the data of other tests and is never cleared. Each C# test works in two
// groups of its own (GraphFixture) and filters each read by them. Where a call reads the whole graph, a PORT comment names
// the change.
//
// The upstream tests `test_remove_episode_rebuilds_summary_in_batches` and `test_remove_episode_keeps_old_summary_when_rebuild_fails`
// replace the module value SUMMARY_REBUILD_BATCH_SIZE. Each C# test replaces GraphitiModule.SummaryRebuildBatchSize, a PyPatch (R17).
namespace Graphiti.E2E.Tests;

using Graphiti.Core;
using Graphiti.Core.Search;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Port.Evaluation;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

public sealed class TestGraphitiMock
{
    private const int TimeLimit = 180_000;

    // The limit of the loop guard for a body that calls Neo4j. It is under the time limit of the test.
    private const double GuardSeconds = 150;

    private static EntityNode Entity(
        string group, string name, string[]? labels = null, string? summary = null, PyDateTime? createdAt = null, params (string Key, object Value)[] attributes)
    {
        var node = new EntityNode { Name = name, GroupId = group, Labels = [.. labels ?? []], CreatedAt = createdAt ?? PyClock.Now() };
        if (summary is not null)
        {
            node.Summary = summary;
        }

        foreach (var (key, value) in attributes)
        {
            node.Attributes[key] = value;
        }

        return node;
    }

    private static EpisodicNode Episode(
        string group, string name, string content, PyDateTime validAt, PyDateTime? createdAt = null, string sourceDescription = "conversation message") => new()
        {
            Name = name,
            GroupId = group,
            Labels = [],
            CreatedAt = createdAt ?? PyClock.Now(),
            Source = EpisodeType.Message,
            SourceDescription = sourceDescription,
            Content = content,
            ValidAt = validAt,
        };

    private static EntityEdge Edge(string group, EntityNode source, EntityNode target, string name, string fact, PyDateTime createdAt) => new()
    {
        SourceNodeUuid = source.Uuid,
        TargetNodeUuid = target.Uuid,
        Name = name,
        Fact = fact,
        CreatedAt = createdAt,
        GroupId = group,
    };

    private static EpisodicEdge Mention(string group, EpisodicNode episode, EntityNode node, PyDateTime createdAt) => new()
    {
        SourceNodeUuid = episode.Uuid,
        TargetNodeUuid = node.Uuid,
        CreatedAt = createdAt,
        GroupId = group,
    };

    private static OrderedDictionary<string, object?> Summaries(params (string Name, string Summary)[] items) => new()
    {
        ["summaries"] = items.Select(item => (object?)new OrderedDictionary<string, object?> { ["name"] = item.Name, ["summary"] = item.Summary }).ToList(),
    };

    /// <summary>The filter of the four search tests: each field of the date filters and the edge type, as upstream.</summary>
    private static SearchFilters DateFilters(PyDateTime now, PyDateTime createdAt) => new()
    {
        NodeLabels = ["Entity"],
        EdgeTypes = ["RELATES_TO"],
        CreatedAt = [[new DateFilter { Date = createdAt, ComparisonOperator = ComparisonOperator.Equals }]],
        ExpiredAt = [[new DateFilter { Date = now, ComparisonOperator = ComparisonOperator.NotEquals }]],
        ValidAt =
        [
            [new DateFilter { Date = PyClock.Plus(now, 1), ComparisonOperator = ComparisonOperator.GreaterThanEqual }],
            [new DateFilter { Date = PyClock.Plus(now, 3), ComparisonOperator = ComparisonOperator.LessThanEqual }],
        ],
        InvalidAt =
        [
            [new DateFilter { Date = PyClock.Plus(now, 3), ComparisonOperator = ComparisonOperator.GreaterThan }],
            [new DateFilter { Date = PyClock.Plus(now, 5), ComparisonOperator = ComparisonOperator.LessThan }],
        ],
    };

    private static async Task Guard(Func<Task> body) => await LoopGuard.RunAsync(body, GuardSeconds);

    [Fact(Timeout = TimeLimit)]
    public async Task test_add_bulk()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();
            var graphiti = g.Graphiti(new MockLlmClient(), embedder);

            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();
            var group = g.GroupId;

            // Create episodic nodes
            var episodeNode1 = Episode(group, "test_episode", "Alice likes Bob", now, now);
            var episodeNode2 = Episode(group, "test_episode_2", "Bob adores Alice", now, now);

            // Create entity nodes
            var entityNode1 = Entity(group, "test_entity_1", ["Entity", "Person"], "test_entity_1 summary", now, ("age", 30L), ("location", "New York"));
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(group, "test_entity_2", ["Entity", "Person2"], "test_entity_2 summary", now, ("age", 25L), ("location", "Los Angeles"));
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode3 = Entity(group, "test_entity_3", ["Entity", "City", "Location"], "test_entity_3 summary", now, ("age", 25L), ("location", "Los Angeles"));
            await entityNode3.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode4 = Entity(group, "test_entity_4", ["Entity"], "test_entity_4 summary", now, ("age", 25L), ("location", "Los Angeles"));
            await entityNode4.GenerateNameEmbeddingAsync(embedder, ct);

            // Create entity edges
            var entityEdge1 = Edge(group, entityNode1, entityNode2, "likes", "test_entity_1 relates to test_entity_2", now);
            entityEdge1.ExpiredAt = now;
            entityEdge1.ValidAt = now;
            entityEdge1.InvalidAt = now;
            await entityEdge1.GenerateEmbeddingAsync(embedder, ct);
            var entityEdge2 = Edge(group, entityNode3, entityNode4, "relates_to", "test_entity_3 relates to test_entity_4", now);
            entityEdge2.ExpiredAt = now;
            entityEdge2.ValidAt = now;
            entityEdge2.InvalidAt = now;
            await entityEdge2.GenerateEmbeddingAsync(embedder, ct);

            // Create episodic to entity edges
            var episodicEdge1 = Mention(group, episodeNode1, entityNode1, now);
            var episodicEdge2 = Mention(group, episodeNode1, entityNode2, now);
            var episodicEdge3 = Mention(group, episodeNode2, entityNode3, now);
            var episodicEdge4 = Mention(group, episodeNode2, entityNode4, now);

            // Cross reference the ids
            episodeNode1.EntityEdges = [entityEdge1.Uuid];
            episodeNode2.EntityEdges = [entityEdge2.Uuid];
            entityEdge1.Episodes = [episodeNode1.Uuid, episodeNode2.Uuid];
            entityEdge2.Episodes = [episodeNode2.Uuid];

            // Test add bulk
            await BulkUtils.AddNodesAndEdgesBulkAsync(
                g.Driver,
                [episodeNode1, episodeNode2],
                [episodicEdge1, episodicEdge2, episodicEdge3, episodicEdge4],
                [entityNode1, entityNode2, entityNode3, entityNode4],
                [entityEdge1, entityEdge2],
                embedder,
                cancellationToken: ct);

            List<string> nodeIds = [episodeNode1.Uuid, episodeNode2.Uuid, entityNode1.Uuid, entityNode2.Uuid, entityNode3.Uuid, entityNode4.Uuid];
            List<string> edgeIds = [episodicEdge1.Uuid, episodicEdge2.Uuid, episodicEdge3.Uuid, episodicEdge4.Uuid, entityEdge1.Uuid, entityEdge2.Uuid];
            Assert.Equal(nodeIds.Count, await g.GetNodeCountAsync(nodeIds));
            Assert.Equal(edgeIds.Count, await g.GetEdgeCountAsync(edgeIds));

            // Test episodic nodes
            g.AssertEpisodicNodeEquals(await EpisodicNode.GetByUuidAsync(g.Driver, episodeNode1.Uuid, cancellationToken: ct), episodeNode1);
            g.AssertEpisodicNodeEquals(await EpisodicNode.GetByUuidAsync(g.Driver, episodeNode2.Uuid, cancellationToken: ct), episodeNode2);

            // Test entity nodes
            foreach (var node in new[] { entityNode1, entityNode2, entityNode3, entityNode4 })
            {
                await g.AssertEntityNodeEqualsAsync(await EntityNode.GetByUuidAsync(g.Driver, node.Uuid, ct), node);
            }

            // Test episodic edges
            foreach (var edge in new[] { episodicEdge1, episodicEdge2, episodicEdge3, episodicEdge4 })
            {
                GraphFixture.AssertEpisodicEdgeEquals(await EpisodicEdge.GetByUuidAsync(g.Driver, edge.Uuid, ct), edge);
            }

            // Test entity edges
            await g.AssertEntityEdgeEqualsAsync(await EntityEdge.GetByUuidAsync(g.Driver, entityEdge1.Uuid, cancellationToken: ct), entityEdge1);
            await g.AssertEntityEdgeEqualsAsync(await EntityEdge.GetByUuidAsync(g.Driver, entityEdge2.Uuid, cancellationToken: ct), entityEdge2);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_remove_episode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();
            var graphiti = g.Graphiti(new MockLlmClient(), embedder);

            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();
            var group = g.GroupId;

            // Create episodic nodes
            var episodeNode = Episode(group, "test_episode", "Alice likes Bob", now, now);

            // Create entity nodes
            var aliceNode = Entity(group, "Alice", ["Entity", "Person"], "Alice summary", now, ("age", 30L), ("location", "New York"));
            await aliceNode.GenerateNameEmbeddingAsync(embedder, ct);
            var bobNode = Entity(group, "Bob", ["Entity", "Person2"], "Bob summary", now, ("age", 25L), ("location", "Los Angeles"));
            await bobNode.GenerateNameEmbeddingAsync(embedder, ct);

            // Create entity to entity edge
            var entityEdge = Edge(group, aliceNode, bobNode, "likes", "Alice likes Bob", now);
            entityEdge.ExpiredAt = now;
            entityEdge.ValidAt = now;
            entityEdge.InvalidAt = now;
            await entityEdge.GenerateEmbeddingAsync(embedder, ct);

            // Create episodic to entity edges
            var episodicAliceEdge = Mention(group, episodeNode, aliceNode, now);
            var episodicBobEdge = Mention(group, episodeNode, bobNode, now);

            // Cross reference the ids
            episodeNode.EntityEdges = [entityEdge.Uuid];
            entityEdge.Episodes = [episodeNode.Uuid];

            // Test add bulk
            await BulkUtils.AddNodesAndEdgesBulkAsync(g.Driver, [episodeNode], [episodicAliceEdge, episodicBobEdge], [aliceNode, bobNode], [entityEdge], embedder, cancellationToken: ct);

            List<string> nodeIds = [episodeNode.Uuid, aliceNode.Uuid, bobNode.Uuid];
            List<string> edgeIds = [episodicAliceEdge.Uuid, episodicBobEdge.Uuid, entityEdge.Uuid];
            Assert.Equal(3, await g.GetNodeCountAsync(nodeIds));
            Assert.Equal(3, await g.GetEdgeCountAsync(edgeIds));

            // Test remove episode
            await graphiti.RemoveEpisodeAsync(episodeNode.Uuid, cancellationToken: ct);
            Assert.Equal(0, await g.GetNodeCountAsync(nodeIds));
            Assert.Equal(0, await g.GetEdgeCountAsync(edgeIds));

            // Test add bulk again
            await BulkUtils.AddNodesAndEdgesBulkAsync(g.Driver, [episodeNode], [episodicAliceEdge, episodicBobEdge], [aliceNode, bobNode], [entityEdge], embedder, cancellationToken: ct);
            Assert.Equal(3, await g.GetNodeCountAsync(nodeIds));
            Assert.Equal(3, await g.GetEdgeCountAsync(edgeIds));
        });
    }

    [Theory(Timeout = TimeLimit)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task test_remove_episode_keeps_restated_fact(bool removeFirstEpisodeFirst)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();
            var llm = new MockLlmClient(Summaries(("Alice", "Alice summary"), ("Bob", "Bob summary")));
            var graphiti = g.Graphiti(llm, embedder);
            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();
            var group = g.GroupId;
            var episode1 = Episode(group, "episode1", "Alice likes Bob", now, now);
            var episode2 = Episode(group, "episode2", "Alice likes Bob", now, now);
            var alice = Entity(group, "Alice", ["Entity", "Person"], "Alice summary", now);
            var bob = Entity(group, "Bob", ["Entity", "Person"], "Bob summary", now);
            await alice.GenerateNameEmbeddingAsync(embedder, ct);
            await bob.GenerateNameEmbeddingAsync(embedder, ct);

            var edge = Edge(group, alice, bob, "likes", "Alice likes Bob", now);
            edge.Episodes = [episode1.Uuid, episode2.Uuid];
            edge.ValidAt = now;
            await edge.GenerateEmbeddingAsync(embedder, ct);
            var factEmbedding = edge.FactEmbedding;

            episode1.EntityEdges = [edge.Uuid];
            episode2.EntityEdges = [edge.Uuid];
            List<EpisodicEdge> mentionEdges = [.. new[] { episode1, episode2 }.SelectMany(episode => new[] { alice, bob }.Select(node => Mention(group, episode, node, now)))];

            await BulkUtils.AddNodesAndEdgesBulkAsync(g.Driver, [episode1, episode2], mentionEdges, [alice, bob], [edge], embedder, cancellationToken: ct);

            var episodeToRemoveFirst = removeFirstEpisodeFirst ? episode1 : episode2;
            var episodeToRemoveSecond = removeFirstEpisodeFirst ? episode2 : episode1;

            await graphiti.RemoveEpisodeAsync(episodeToRemoveFirst.Uuid, cancellationToken: ct);

            var survivingEdge = await EntityEdge.GetByUuidAsync(g.Driver, edge.Uuid, cancellationToken: ct);
            await survivingEdge.LoadFactEmbeddingAsync(g.Driver, ct);
            Assert.Equal([episodeToRemoveSecond.Uuid], survivingEdge.Episodes);
            GraphFixture.AssertAllClose(survivingEdge.FactEmbedding!, factEmbedding!);
            Assert.Equal(2, await g.GetNodeCountAsync([episodeToRemoveFirst.Uuid, alice.Uuid, bob.Uuid]));

            await graphiti.RemoveEpisodeAsync(episodeToRemoveSecond.Uuid, cancellationToken: ct);

            Assert.Equal(0, await g.GetEdgeCountAsync([edge.Uuid]));
            Assert.Equal(0, await g.GetNodeCountAsync([episodeToRemoveSecond.Uuid, alice.Uuid, bob.Uuid]));
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_remove_episode_rebuilds_summary_from_remaining_episodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();
            var llm = new MockLlmClient(Summaries(("Alice", "Alice works on the pilot project.")));
            var graphiti = g.Graphiti(llm, embedder);
            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();
            var group = g.GroupId;
            var episode1 = Episode(group, "episode1", "Alice mentioned OLD_MARKER and spoke with Bob.", PyClock.Plus(now, -1), PyClock.Plus(now, -1));
            var episode2 = Episode(group, "episode2", "Alice works on the pilot project with Carol.", now, now);
            var alice = Entity(group, "Alice", ["Entity", "Person"], "Alice has marker OLD_MARKER\nAlice works on the pilot project.", now);
            var bob = Entity(group, "Bob", ["Entity", "Person"], "Bob summary", now);
            var carol = Entity(group, "Carol", ["Entity", "Person"], "Carol summary", now);
            await alice.GenerateNameEmbeddingAsync(embedder, ct);
            await bob.GenerateNameEmbeddingAsync(embedder, ct);
            carol.NameEmbedding = [.. Enumerable.Repeat(0.3, 384)];

            var edge1 = Edge(group, alice, bob, "has_marker", "Alice has marker OLD_MARKER", now);
            edge1.Episodes = [episode1.Uuid];
            edge1.ValidAt = PyClock.Plus(now, -1);
            var edge2 = Edge(group, alice, carol, "works_on", "Alice works on the pilot project", now);
            edge2.Episodes = [episode2.Uuid];
            edge2.ValidAt = now;
            edge1.FactEmbedding = [.. Enumerable.Repeat(0.1, 384)];
            edge2.FactEmbedding = [.. Enumerable.Repeat(0.2, 384)];
            episode1.EntityEdges = [edge1.Uuid];
            episode2.EntityEdges = [edge2.Uuid];
            List<EpisodicEdge> mentionEdges =
            [
                .. new[] { alice, bob }.Select(node => Mention(group, episode1, node, now)),
                .. new[] { alice, carol }.Select(node => Mention(group, episode2, node, now)),
            ];

            await BulkUtils.AddNodesAndEdgesBulkAsync(g.Driver, [episode1, episode2], mentionEdges, [alice, bob, carol], [edge1, edge2], embedder, cancellationToken: ct);
            await alice.LoadNameEmbeddingAsync(g.Driver, ct);
            var aliceNameEmbedding = alice.NameEmbedding;

            await graphiti.RemoveEpisodeAsync(episode1.Uuid, cancellationToken: ct);

            List<string> promptTexts = [.. llm.Calls.Select(MockLlmClient.Text)];
            Assert.Single(promptTexts);
            Assert.Contains(episode2.Content, promptTexts[0], StringComparison.Ordinal);
            Assert.All(promptTexts, text => Assert.DoesNotContain("OLD_MARKER", text, StringComparison.Ordinal));

            var retrievedAlice = await EntityNode.GetByUuidAsync(g.Driver, alice.Uuid, ct);
            await retrievedAlice.LoadNameEmbeddingAsync(g.Driver, ct);
            Assert.Equal("Alice works on the pilot project.", retrievedAlice.Summary);
            Assert.Equal(aliceNameEmbedding, retrievedAlice.NameEmbedding);
            Assert.Equal(0, await g.GetNodeCountAsync([bob.Uuid]));
            Assert.Equal(0, await g.GetEdgeCountAsync([edge1.Uuid]));
            Assert.Equal(1, await g.GetNodeCountAsync([carol.Uuid]));
            Assert.Equal(1, await g.GetEdgeCountAsync([edge2.Uuid]));
        });
    }

    // The batch size is 2, as upstream. Three episodes remain: the two oldest ones are in the first prompt, and the newest
    // one is in the second prompt, which also holds the summary of the first reply.
    [Fact(Timeout = TimeLimit)]
    public async Task test_remove_episode_rebuilds_summary_in_batches()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            const int batchSize = 2;
            using var patch = GraphitiModule.SummaryRebuildBatchSize.Use(batchSize);
            var embedder = new MockEmbedder();
            var llm = new MockLlmClient(Summaries(("Alice", "summary-1")), Summaries(("Alice", "summary-2")));
            var graphiti = g.Graphiti(llm, embedder);
            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();
            var group = g.GroupId;
            var episodeToRemove = Episode(group, "episode_to_remove", "Alice mentioned OLD_MARKER and spoke with Bob.", PyClock.Plus(now, -40), PyClock.Plus(now, -40));

            // The remaining episodes, oldest first: batchSize in the first batch and one in the second batch.
            List<EpisodicNode> remaining =
            [
                .. Enumerable.Range(0, batchSize + 1).Select(i => Episode(
                    group,
                    $"episode_{i:D2}",
                    $"Alice finished the remaining project number {i:D2}.",
                    PyClock.Plus(now, -30 + i),
                    PyClock.Plus(now, -30 + i))),
            ];
            var alice = Entity(group, "Alice", ["Entity", "Person"], "Alice original summary", now);
            var bob = Entity(group, "Bob", ["Entity", "Person"], "Bob summary", now);
            await alice.GenerateNameEmbeddingAsync(embedder, ct);
            await bob.GenerateNameEmbeddingAsync(embedder, ct);
            var edge = Edge(group, alice, bob, "has_marker", "Alice has marker OLD_MARKER", now);
            edge.Episodes = [episodeToRemove.Uuid];
            edge.ValidAt = PyClock.Plus(now, -40);
            edge.FactEmbedding = [.. Enumerable.Repeat(0.1, 384)];
            episodeToRemove.EntityEdges = [edge.Uuid];
            List<EpisodicEdge> mentionEdges =
            [
                .. new[] { alice, bob }.Select(node => Mention(group, episodeToRemove, node, now)),
                .. remaining.Select(episode => Mention(group, episode, alice, now)),
            ];
            await BulkUtils.AddNodesAndEdgesBulkAsync(g.Driver, [episodeToRemove, .. remaining], mentionEdges, [alice, bob], [edge], embedder, cancellationToken: ct);

            await graphiti.RemoveEpisodeAsync(episodeToRemove.Uuid, cancellationToken: ct);

            var retrievedAlice = await EntityNode.GetByUuidAsync(g.Driver, alice.Uuid, ct);
            await retrievedAlice.LoadNameEmbeddingAsync(g.Driver, ct);
            Assert.Equal("summary-2", retrievedAlice.Summary);
            Assert.Equal(2, llm.AwaitCount);

            List<string> promptTexts = [.. llm.Calls.Select(MockLlmClient.Text)];
            foreach (var episode in remaining[..batchSize])
            {
                Assert.Contains(episode.Content, promptTexts[0], StringComparison.Ordinal);
            }

            foreach (var episode in remaining[batchSize..])
            {
                Assert.DoesNotContain(episode.Content, promptTexts[0], StringComparison.Ordinal);
                Assert.Contains(episode.Content, promptTexts[1], StringComparison.Ordinal);
            }

            Assert.Contains("summary-1", promptTexts[1], StringComparison.Ordinal);
            Assert.Equal(0, await g.GetNodeCountAsync([episodeToRemove.Uuid, bob.Uuid]));
            Assert.Equal(0, await g.GetEdgeCountAsync([edge.Uuid]));
        });
    }

    // The batch size is 1, as upstream. Two episodes remain: the first batch has one episode and the second batch has one
    // episode, and the second reply is a RuntimeError. `caplog` is a log factory (AllLogsFactory) that
    // PortServices.Use gives to the ported code. The ERROR record of Python names the uuid of the entity in its message.
    [Fact(Timeout = TimeLimit)]
    public async Task test_remove_episode_keeps_old_summary_when_rebuild_fails()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Message)> lines = new();
        using var logs = Graphiti.Internal.PortServices.Use(loggerFactory: new AllLogsFactory(lines));
        await Guard(async () =>
        {
            const int batchSize = 1;
            using var patch = GraphitiModule.SummaryRebuildBatchSize.Use(batchSize);
            var embedder = new MockEmbedder();
            var llm = new MockLlmClient(Summaries(("Alice", "summary-1")), new InvalidOperationException("summary failure"));
            var graphiti = g.Graphiti(llm, embedder);
            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();
            var group = g.GroupId;
            var episodeToRemove = Episode(group, "episode_to_remove", "Alice mentioned OLD_MARKER and spoke with Bob.", PyClock.Plus(now, -40), PyClock.Plus(now, -40));
            List<EpisodicNode> remaining =
            [
                .. Enumerable.Range(0, batchSize + 1).Select(i => Episode(
                    group,
                    $"episode_{i:D2}",
                    $"Alice finished the remaining project number {i:D2}.",
                    PyClock.Plus(now, -30 + i),
                    PyClock.Plus(now, -30 + i))),
            ];
            var alice = Entity(group, "Alice", ["Entity", "Person"], "Alice has marker OLD_MARKER", now);
            var bob = Entity(group, "Bob", ["Entity", "Person"], "Bob summary", now);
            await alice.GenerateNameEmbeddingAsync(embedder, ct);
            await bob.GenerateNameEmbeddingAsync(embedder, ct);
            var edge = Edge(group, alice, bob, "has_marker", "Alice has marker OLD_MARKER", now);
            edge.Episodes = [episodeToRemove.Uuid];
            edge.ValidAt = PyClock.Plus(now, -40);
            edge.FactEmbedding = [.. Enumerable.Repeat(0.1, 384)];
            episodeToRemove.EntityEdges = [edge.Uuid];
            List<EpisodicEdge> mentionEdges =
            [
                .. new[] { alice, bob }.Select(node => Mention(group, episodeToRemove, node, now)),
                .. remaining.Select(episode => Mention(group, episode, alice, now)),
            ];
            await BulkUtils.AddNodesAndEdgesBulkAsync(g.Driver, [episodeToRemove, .. remaining], mentionEdges, [alice, bob], [edge], embedder, cancellationToken: ct);

            await graphiti.RemoveEpisodeAsync(episodeToRemove.Uuid, cancellationToken: ct);

            Assert.Equal(2, llm.AwaitCount);
            Assert.Equal(0, await g.GetNodeCountAsync([episodeToRemove.Uuid, bob.Uuid]));
            Assert.Equal(0, await g.GetEdgeCountAsync([edge.Uuid]));
            var retrievedAlice = await EntityNode.GetByUuidAsync(g.Driver, alice.Uuid, ct);
            Assert.Equal("Alice has marker OLD_MARKER", retrievedAlice.Summary);
            Assert.Contains(lines, line => line.Level == LogLevel.Error && line.Message.Contains(alice.Uuid, StringComparison.Ordinal));
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_graphiti_retrieve_episodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var graphiti = g.Graphiti(new MockLlmClient(), new MockEmbedder());

            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();
            var validAt1 = PyClock.Plus(now, -2);
            var validAt2 = PyClock.Plus(now, -4);
            var validAt3 = PyClock.Plus(now, -6);

            // Create episodic nodes
            var episodeNode1 = Episode(g.GroupId, "test_episode_1", "Test message 1", validAt1, now);
            var episodeNode2 = Episode(g.GroupId, "test_episode_2", "Test message 2", validAt2, now);
            var episodeNode3 = Episode(g.GroupId, "test_episode_3", "Test message 3", validAt3, now);

            // Save the nodes
            await episodeNode1.SaveAsync(g.Driver, ct);
            await episodeNode2.SaveAsync(g.Driver, ct);
            await episodeNode3.SaveAsync(g.Driver, ct);

            List<string> nodeIds = [episodeNode1.Uuid, episodeNode2.Uuid, episodeNode3.Uuid];
            Assert.Equal(3, await g.GetNodeCountAsync(nodeIds));

            // Retrieve episodes
            var queryTime = PyClock.Plus(now, -3);
            var episodes = await graphiti.RetrieveEpisodesAsync(queryTime, lastN: 5, groupIds: [g.GroupId], source: EpisodeType.Message, cancellationToken: ct);
            Assert.Equal(2, episodes.Count);
            Assert.Equal(episodeNode3.Name, episodes[0].Name);
            Assert.Equal(episodeNode2.Name, episodes[1].Name);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_filter_existing_duplicate_of_edges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode3 = Entity(g.GroupId, "test_entity_3");
            await entityNode3.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode4 = Entity(g.GroupId, "test_entity_4");
            await entityNode4.GenerateNameEmbeddingAsync(embedder, ct);

            // Save the nodes
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await entityNode3.SaveAsync(g.Driver, ct);
            await entityNode4.SaveAsync(g.Driver, ct);

            List<string> nodeIds = [entityNode1.Uuid, entityNode2.Uuid, entityNode3.Uuid, entityNode4.Uuid];
            Assert.Equal(4, await g.GetNodeCountAsync(nodeIds));

            // Create duplicate entity edge
            var entityEdge = Edge(g.GroupId, entityNode1, entityNode2, "IS_DUPLICATE_OF", "test_entity_1 is a duplicate of test_entity_2", PyClock.Now());
            await entityEdge.GenerateEmbeddingAsync(embedder, ct);
            await entityEdge.SaveAsync(g.Driver, ct);

            // Filter duplicate entity edges
            List<(EntityNode, EntityNode)> duplicateNodeTuples = [(entityNode1, entityNode2), (entityNode3, entityNode4)];
            var nodeTuples = await EdgeOperations.FilterExistingDuplicateOfEdgesAsync(g.Driver, duplicateNodeTuples, ct);
            Assert.Single(nodeTuples);
            Assert.Equal([entityNode3.Name, entityNode4.Name], [nodeTuples[0].Item1.Name, nodeTuples[0].Item2.Name]);
        });
    }

    // PORT: the upstream test ends with `remove_communities(graph_driver)`, which deletes each Community node of the whole
    // graph when `group_ids` is empty. The shared Neo4j holds the communities of other tests, so the C# call passes the group
    // of the test: `remove_communities(driver, group_ids=[group_id])`. The check that follows reads the two community uuids.
    [Fact(Timeout = TimeLimit)]
    public async Task test_determine_entity_community()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode3 = Entity(g.GroupId, "test_entity_3");
            await entityNode3.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode4 = Entity(g.GroupId, "test_entity_4");
            await entityNode4.GenerateNameEmbeddingAsync(embedder, ct);

            // Create entity edges
            var entityEdge1 = Edge(g.GroupId, entityNode1, entityNode4, "RELATES_TO", "test_entity_1 relates to test_entity_4", PyClock.Now());
            await entityEdge1.GenerateEmbeddingAsync(embedder, ct);
            var entityEdge2 = Edge(g.GroupId, entityNode2, entityNode4, "RELATES_TO", "test_entity_2 relates to test_entity_4", PyClock.Now());
            await entityEdge2.GenerateEmbeddingAsync(embedder, ct);
            var entityEdge3 = Edge(g.GroupId, entityNode3, entityNode4, "RELATES_TO", "test_entity_3 relates to test_entity_4", PyClock.Now());
            await entityEdge3.GenerateEmbeddingAsync(embedder, ct);

            // Create community nodes
            var communityNode1 = new CommunityNode { Name = "test_community_1", Labels = [], CreatedAt = PyClock.Now(), GroupId = g.GroupId };
            await communityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var communityNode2 = new CommunityNode { Name = "test_community_2", Labels = [], CreatedAt = PyClock.Now(), GroupId = g.GroupId };
            await communityNode2.GenerateNameEmbeddingAsync(embedder, ct);

            // Create community to entity edges
            var communityEdge1 = new CommunityEdge { SourceNodeUuid = communityNode1.Uuid, TargetNodeUuid = entityNode1.Uuid, CreatedAt = PyClock.Now(), GroupId = g.GroupId };
            var communityEdge2 = new CommunityEdge { SourceNodeUuid = communityNode1.Uuid, TargetNodeUuid = entityNode2.Uuid, CreatedAt = PyClock.Now(), GroupId = g.GroupId };
            var communityEdge3 = new CommunityEdge { SourceNodeUuid = communityNode2.Uuid, TargetNodeUuid = entityNode3.Uuid, CreatedAt = PyClock.Now(), GroupId = g.GroupId };

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await entityNode3.SaveAsync(g.Driver, ct);
            await entityNode4.SaveAsync(g.Driver, ct);
            await communityNode1.SaveAsync(g.Driver, ct);
            await communityNode2.SaveAsync(g.Driver, ct);

            await entityEdge1.SaveAsync(g.Driver, ct);
            await entityEdge2.SaveAsync(g.Driver, ct);
            await entityEdge3.SaveAsync(g.Driver, ct);
            await communityEdge1.SaveAsync(g.Driver, ct);
            await communityEdge2.SaveAsync(g.Driver, ct);
            await communityEdge3.SaveAsync(g.Driver, ct);

            List<string> nodeIds =
            [
                entityNode1.Uuid, entityNode2.Uuid, entityNode3.Uuid, entityNode4.Uuid, communityNode1.Uuid, communityNode2.Uuid,
            ];
            List<string> edgeIds =
            [
                entityEdge1.Uuid, entityEdge2.Uuid, entityEdge3.Uuid, communityEdge1.Uuid, communityEdge2.Uuid, communityEdge3.Uuid,
            ];
            Assert.Equal(6, await g.GetNodeCountAsync(nodeIds));
            Assert.Equal(6, await g.GetEdgeCountAsync(edgeIds));

            // Determine entity community
            var (community, isNew) = await CommunityOperations.DetermineEntityCommunityAsync(g.Driver, entityNode4, ct);
            Assert.Equal(communityNode1.Name, community!.Name);
            Assert.True(isNew);

            // Add entity to community edge
            var communityEdge4 = new CommunityEdge { SourceNodeUuid = communityNode1.Uuid, TargetNodeUuid = entityNode4.Uuid, CreatedAt = PyClock.Now(), GroupId = g.GroupId };
            await communityEdge4.SaveAsync(g.Driver, ct);

            // Determine entity community again
            (community, isNew) = await CommunityOperations.DetermineEntityCommunityAsync(g.Driver, entityNode4, ct);
            Assert.Equal(communityNode1.Name, community!.Name);
            Assert.False(isNew);

            await CommunityOperations.RemoveCommunitiesAsync(g.Driver, [g.GroupId], ct);
            Assert.Equal(0, await g.GetNodeCountAsync([communityNode1.Uuid, communityNode2.Uuid]));
        });
    }

    // PORT: the upstream call `get_community_clusters(graph_driver, group_ids=None)` reads the whole graph, and the test
    // asserts exactly two clusters. On a shared Neo4j the call reads the entities of other tests (on Python the same test
    // fails against a database with other data: 85 clusters). The C# call passes the two groups of the test.
    [Fact(Timeout = TimeLimit)]
    public async Task test_get_community_clusters()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode3 = Entity(g.GroupId2, "test_entity_3");
            await entityNode3.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode4 = Entity(g.GroupId2, "test_entity_4");
            await entityNode4.GenerateNameEmbeddingAsync(embedder, ct);

            // Create entity edges
            var entityEdge1 = Edge(g.GroupId, entityNode1, entityNode2, "RELATES_TO", "test_entity_1 relates to test_entity_2", PyClock.Now());
            await entityEdge1.GenerateEmbeddingAsync(embedder, ct);
            var entityEdge2 = Edge(g.GroupId2, entityNode3, entityNode4, "RELATES_TO", "test_entity_3 relates to test_entity_4", PyClock.Now());
            await entityEdge2.GenerateEmbeddingAsync(embedder, ct);

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await entityNode3.SaveAsync(g.Driver, ct);
            await entityNode4.SaveAsync(g.Driver, ct);
            await entityEdge1.SaveAsync(g.Driver, ct);
            await entityEdge2.SaveAsync(g.Driver, ct);

            List<string> nodeIds = [entityNode1.Uuid, entityNode2.Uuid, entityNode3.Uuid, entityNode4.Uuid];
            List<string> edgeIds = [entityEdge1.Uuid, entityEdge2.Uuid];
            Assert.Equal(4, await g.GetNodeCountAsync(nodeIds));
            Assert.Equal(2, await g.GetEdgeCountAsync(edgeIds));

            // Get community clusters
            var clusters = await CommunityOperations.GetCommunityClustersAsync(g.Driver, g.Groups, cancellationToken: ct);
            Assert.Equal(2, clusters.Count);
            Assert.Equal(2, clusters[0].Count);
            Assert.Equal(2, clusters[1].Count);
            HashSet<string> entities1 = [.. clusters[0].Select(node => node.Name)];
            HashSet<string> entities2 = [.. clusters[1].Select(node => node.Name)];
            Assert.True(entities1.SetEquals(["test_entity_1", "test_entity_2"]) || entities2.SetEquals(["test_entity_1", "test_entity_2"]));
            Assert.True(entities1.SetEquals(["test_entity_3", "test_entity_4"]) || entities2.SetEquals(["test_entity_3", "test_entity_4"]));
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_get_mentioned_nodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create episodic nodes
            var episodicNode1 = new EpisodicNode
            {
                Name = "test_episodic_1",
                Labels = [],
                CreatedAt = PyClock.Now(),
                GroupId = g.GroupId,
                Source = EpisodeType.Message,
                SourceDescription = "test_source_description",
                Content = "test_content",
                ValidAt = PyClock.Now(),
            };

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);

            // Create episodic to entity edges
            var episodicEdge1 = Mention(g.GroupId, episodicNode1, entityNode1, PyClock.Now());

            // Save the graph
            await episodicNode1.SaveAsync(g.Driver, ct);
            await entityNode1.SaveAsync(g.Driver, ct);
            await episodicEdge1.SaveAsync(g.Driver, ct);

            // Get mentioned nodes
            var mentionedNodes = await SearchUtils.GetMentionedNodesAsync(g.Driver, [episodicNode1], ct);
            Assert.Single(mentionedNodes);
            Assert.Equal(entityNode1.Name, mentionedNodes[0].Name);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_get_communities_by_nodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);

            // Create community nodes
            var communityNode1 = new CommunityNode { Name = "test_community_1", Labels = [], CreatedAt = PyClock.Now(), GroupId = g.GroupId };
            await communityNode1.GenerateNameEmbeddingAsync(embedder, ct);

            // Create community to entity edges
            var communityEdge1 = new CommunityEdge { SourceNodeUuid = communityNode1.Uuid, TargetNodeUuid = entityNode1.Uuid, CreatedAt = PyClock.Now(), GroupId = g.GroupId };

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await communityNode1.SaveAsync(g.Driver, ct);
            await communityEdge1.SaveAsync(g.Driver, ct);

            // Get communities by nodes
            var communities = await SearchUtils.GetCommunitiesByNodesAsync(g.Driver, [entityNode1], ct);
            Assert.Single(communities);
            Assert.Equal(communityNode1.Name, communities[0].Name);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_edge_fulltext_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();
            var graphiti = g.Graphiti(new MockLlmClient(), embedder);
            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);

            var now = PyClock.Now();
            var createdAt = now;
            var expiredAt = PyClock.Plus(now, 6);
            var validAt = PyClock.Plus(now, 2);
            var invalidAt = PyClock.Plus(now, 4);

            // Create entity edges
            var entityEdge1 = Edge(g.GroupId, entityNode1, entityNode2, "RELATES_TO", "test_entity_1 relates to test_entity_2", createdAt);
            entityEdge1.ValidAt = validAt;
            entityEdge1.InvalidAt = invalidAt;
            entityEdge1.ExpiredAt = expiredAt;
            await entityEdge1.GenerateEmbeddingAsync(embedder, ct);

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await entityEdge1.SaveAsync(g.Driver, ct);

            // Search for entity edges
            var searchFilters = DateFilters(now, createdAt);
            var edges = await SearchUtils.EdgeFulltextSearchAsync(
                g.Driver, "test_entity_1 relates to test_entity_2", searchFilters, groupIds: [g.GroupId], cancellationToken: ct);
            Assert.Single(edges);
            Assert.Equal(entityEdge1.Name, edges[0].Name);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_edge_similarity_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);

            var now = PyClock.Now();
            var createdAt = now;
            var expiredAt = PyClock.Plus(now, 6);
            var validAt = PyClock.Plus(now, 2);
            var invalidAt = PyClock.Plus(now, 4);

            // Create entity edges
            var entityEdge1 = Edge(g.GroupId, entityNode1, entityNode2, "RELATES_TO", "test_entity_1 relates to test_entity_2", createdAt);
            entityEdge1.ValidAt = validAt;
            entityEdge1.InvalidAt = invalidAt;
            entityEdge1.ExpiredAt = expiredAt;
            await entityEdge1.GenerateEmbeddingAsync(embedder, ct);

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await entityEdge1.SaveAsync(g.Driver, ct);

            // Search for entity edges
            var searchFilters = DateFilters(now, createdAt);
            var edges = await SearchUtils.EdgeSimilaritySearchAsync(
                g.Driver, entityEdge1.FactEmbedding!, entityNode1.Uuid, entityNode2.Uuid, searchFilters, groupIds: [g.GroupId], cancellationToken: ct);
            Assert.Single(edges);
            Assert.Equal(entityEdge1.Name, edges[0].Name);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_edge_bfs_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create episodic nodes
            var episodicNode1 = new EpisodicNode
            {
                Name = "test_episodic_1",
                Labels = [],
                CreatedAt = PyClock.Now(),
                GroupId = g.GroupId,
                Source = EpisodeType.Message,
                SourceDescription = "test_source_description",
                Content = "test_content",
                ValidAt = PyClock.Now(),
            };

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode3 = Entity(g.GroupId, "test_entity_3");
            await entityNode3.GenerateNameEmbeddingAsync(embedder, ct);

            var now = PyClock.Now();
            var createdAt = now;
            var expiredAt = PyClock.Plus(now, 6);
            var validAt = PyClock.Plus(now, 2);
            var invalidAt = PyClock.Plus(now, 4);

            // Create entity edges
            var entityEdge1 = Edge(g.GroupId, entityNode1, entityNode2, "RELATES_TO", "test_entity_1 relates to test_entity_2", createdAt);
            entityEdge1.ValidAt = validAt;
            entityEdge1.InvalidAt = invalidAt;
            entityEdge1.ExpiredAt = expiredAt;
            await entityEdge1.GenerateEmbeddingAsync(embedder, ct);
            var entityEdge2 = Edge(g.GroupId, entityNode2, entityNode3, "RELATES_TO", "test_entity_2 relates to test_entity_3", createdAt);
            entityEdge2.ValidAt = validAt;
            entityEdge2.InvalidAt = invalidAt;
            entityEdge2.ExpiredAt = expiredAt;
            await entityEdge2.GenerateEmbeddingAsync(embedder, ct);

            // Create episodic to entity edges
            var episodicEdge1 = Mention(g.GroupId, episodicNode1, entityNode1, PyClock.Now());

            // Save the graph
            await episodicNode1.SaveAsync(g.Driver, ct);
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await entityNode3.SaveAsync(g.Driver, ct);
            await entityEdge1.SaveAsync(g.Driver, ct);
            await entityEdge2.SaveAsync(g.Driver, ct);
            await episodicEdge1.SaveAsync(g.Driver, ct);

            // Search for entity edges
            var searchFilters = DateFilters(now, createdAt);

            // Test bfs from episodic node
            var edges = await SearchUtils.EdgeBfsSearchAsync(g.Driver, [episodicNode1.Uuid], 1, searchFilters, groupIds: [g.GroupId], cancellationToken: ct);
            Assert.Empty(edges);

            edges = await SearchUtils.EdgeBfsSearchAsync(g.Driver, [episodicNode1.Uuid], 2, searchFilters, groupIds: [g.GroupId], cancellationToken: ct);
            var edgesDeduplicated = DeduplicatedFacts(edges);
            Assert.Single(edgesDeduplicated);
            Assert.Equal(["test_entity_1 relates to test_entity_2"], edgesDeduplicated);

            edges = await SearchUtils.EdgeBfsSearchAsync(g.Driver, [episodicNode1.Uuid], 3, searchFilters, groupIds: [g.GroupId], cancellationToken: ct);
            edgesDeduplicated = DeduplicatedFacts(edges);
            Assert.Equal(2, edgesDeduplicated.Count);
            Assert.True(edgesDeduplicated.SetEquals(["test_entity_1 relates to test_entity_2", "test_entity_2 relates to test_entity_3"]));

            // Test bfs from entity node
            edges = await SearchUtils.EdgeBfsSearchAsync(g.Driver, [entityNode1.Uuid], 1, searchFilters, groupIds: [g.GroupId], cancellationToken: ct);
            edgesDeduplicated = DeduplicatedFacts(edges);
            Assert.Single(edgesDeduplicated);
            Assert.Equal(["test_entity_1 relates to test_entity_2"], edgesDeduplicated);

            edges = await SearchUtils.EdgeBfsSearchAsync(g.Driver, [entityNode1.Uuid], 2, searchFilters, groupIds: [g.GroupId], cancellationToken: ct);
            edgesDeduplicated = DeduplicatedFacts(edges);
            Assert.Equal(2, edgesDeduplicated.Count);
            Assert.True(edgesDeduplicated.SetEquals(["test_entity_1 relates to test_entity_2", "test_entity_2 relates to test_entity_3"]));
        });
    }

    /// <summary><c>set({edge.uuid: edge.fact for edge in edges}.values())</c>.</summary>
    private static HashSet<string> DeduplicatedFacts(List<EntityEdge> edges)
    {
        Dictionary<string, string> byUuid = [];
        foreach (var edge in edges)
        {
            byUuid[edge.Uuid] = edge.Fact;
        }

        return [.. byUuid.Values];
    }

    /// <summary><c>set({node.uuid: node.name for node in nodes}.values())</c>.</summary>
    private static HashSet<string> DeduplicatedNames(List<EntityNode> nodes)
    {
        Dictionary<string, string> byUuid = [];
        foreach (var node in nodes)
        {
            byUuid[node.Uuid] = node.Name;
        }

        return [.. byUuid.Values];
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_node_fulltext_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();
            var graphiti = g.Graphiti(new MockLlmClient(), embedder);
            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1", summary: "Summary about Alice");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2", summary: "Summary about Bob");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);

            // Search for entity edges
            var searchFilters = new SearchFilters { NodeLabels = ["Entity"] };
            var nodes = await SearchUtils.NodeFulltextSearchAsync(g.Driver, "Alice", searchFilters, groupIds: [g.GroupId], cancellationToken: ct);
            Assert.Single(nodes);
            Assert.Equal(entityNode1.Name, nodes[0].Name);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_node_similarity_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_alice", summary: "Summary about Alice");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_bob", summary: "Summary about Bob");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);

            // Search for entity edges
            var searchFilters = new SearchFilters { NodeLabels = ["Entity"] };
            var nodes = await SearchUtils.NodeSimilaritySearchAsync(
                g.Driver, entityNode1.NameEmbedding!, searchFilters, groupIds: [g.GroupId], minScore: 0.9, cancellationToken: ct);
            Assert.Single(nodes);
            Assert.Equal(entityNode1.Name, nodes[0].Name);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_node_bfs_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create episodic nodes
            var episodicNode1 = new EpisodicNode
            {
                Name = "test_episodic_1",
                Labels = [],
                CreatedAt = PyClock.Now(),
                GroupId = g.GroupId,
                Source = EpisodeType.Message,
                SourceDescription = "test_source_description",
                Content = "test_content",
                ValidAt = PyClock.Now(),
            };

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode3 = Entity(g.GroupId, "test_entity_3");
            await entityNode3.GenerateNameEmbeddingAsync(embedder, ct);

            // Create entity edges
            var entityEdge1 = Edge(g.GroupId, entityNode1, entityNode2, "RELATES_TO", "test_entity_1 relates to test_entity_2", PyClock.Now());
            await entityEdge1.GenerateEmbeddingAsync(embedder, ct);
            var entityEdge2 = Edge(g.GroupId, entityNode2, entityNode3, "RELATES_TO", "test_entity_2 relates to test_entity_3", PyClock.Now());
            await entityEdge2.GenerateEmbeddingAsync(embedder, ct);

            // Create episodic to entity edges
            var episodicEdge1 = Mention(g.GroupId, episodicNode1, entityNode1, PyClock.Now());

            // Save the graph
            await episodicNode1.SaveAsync(g.Driver, ct);
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await entityNode3.SaveAsync(g.Driver, ct);
            await entityEdge1.SaveAsync(g.Driver, ct);
            await entityEdge2.SaveAsync(g.Driver, ct);
            await episodicEdge1.SaveAsync(g.Driver, ct);

            // Search for entity nodes
            var searchFilters = new SearchFilters { NodeLabels = ["Entity"] };

            // Test bfs from episodic node
            var nodes = await SearchUtils.NodeBfsSearchAsync(g.Driver, [episodicNode1.Uuid], searchFilters, 1, groupIds: [g.GroupId], cancellationToken: ct);
            var nodesDeduplicated = DeduplicatedNames(nodes);
            Assert.Single(nodesDeduplicated);
            Assert.Equal(["test_entity_1"], nodesDeduplicated);

            nodes = await SearchUtils.NodeBfsSearchAsync(g.Driver, [episodicNode1.Uuid], searchFilters, 2, groupIds: [g.GroupId], cancellationToken: ct);
            nodesDeduplicated = DeduplicatedNames(nodes);
            Assert.Equal(2, nodesDeduplicated.Count);
            Assert.True(nodesDeduplicated.SetEquals(["test_entity_1", "test_entity_2"]));

            // Test bfs from entity node
            nodes = await SearchUtils.NodeBfsSearchAsync(g.Driver, [entityNode1.Uuid], searchFilters, 1, groupIds: [g.GroupId], cancellationToken: ct);
            nodesDeduplicated = DeduplicatedNames(nodes);
            Assert.Single(nodesDeduplicated);
            Assert.Equal(["test_entity_2"], nodesDeduplicated);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_episode_fulltext_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var graphiti = g.Graphiti(new MockLlmClient(), new MockEmbedder());
            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            // Create episodic nodes
            var episodicNode1 = Episode(g.GroupId, "test_episodic_1", "test_content", PyClock.Now(), PyClock.Now(), "Description about Alice");
            var episodicNode2 = Episode(g.GroupId, "test_episodic_2", "test_content_2", PyClock.Now(), PyClock.Now(), "Description about Bob");

            // Save the graph
            await episodicNode1.SaveAsync(g.Driver, ct);
            await episodicNode2.SaveAsync(g.Driver, ct);

            // Search for episodic nodes
            var searchFilters = new SearchFilters { NodeLabels = ["Episodic"] };
            var nodes = await SearchUtils.EpisodeFulltextSearchAsync(g.Driver, "Alice", searchFilters, groupIds: [g.GroupId], cancellationToken: ct);
            Assert.Single(nodes);
            Assert.Equal(episodicNode1.Name, nodes[0].Name);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_community_fulltext_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();
            var graphiti = g.Graphiti(new MockLlmClient(), embedder);
            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            // Create community nodes
            var communityNode1 = new CommunityNode { Name = "Alice", CreatedAt = PyClock.Now(), GroupId = g.GroupId };
            await communityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var communityNode2 = new CommunityNode { Name = "Bob", CreatedAt = PyClock.Now(), GroupId = g.GroupId };
            await communityNode2.GenerateNameEmbeddingAsync(embedder, ct);

            // Save the graph
            await communityNode1.SaveAsync(g.Driver, ct);
            await communityNode2.SaveAsync(g.Driver, ct);

            // Search for community nodes
            var nodes = await SearchUtils.CommunityFulltextSearchAsync(g.Driver, "Alice", groupIds: [g.GroupId], cancellationToken: ct);
            Assert.Single(nodes);
            Assert.Equal(communityNode1.Name, nodes[0].Name);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_community_similarity_search()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();
            var graphiti = g.Graphiti(new MockLlmClient(), embedder);
            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            // Create community nodes
            var communityNode1 = new CommunityNode { Name = "Alice", CreatedAt = PyClock.Now(), GroupId = g.GroupId };
            await communityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var communityNode2 = new CommunityNode { Name = "Bob", CreatedAt = PyClock.Now(), GroupId = g.GroupId };
            await communityNode2.GenerateNameEmbeddingAsync(embedder, ct);

            // Save the graph
            await communityNode1.SaveAsync(g.Driver, ct);
            await communityNode2.SaveAsync(g.Driver, ct);

            // Search for community nodes
            var nodes = await SearchUtils.CommunitySimilaritySearchAsync(g.Driver, communityNode1.NameEmbedding!, groupIds: [g.GroupId], minScore: 0.9, cancellationToken: ct);
            Assert.Single(nodes);
            Assert.Equal(communityNode1.Name, nodes[0].Name);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_get_relevant_nodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();
            var graphiti = g.Graphiti(new MockLlmClient(), embedder);
            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "Alice", summary: "Alice");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "Bob", summary: "Bob");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode3 = Entity(g.GroupId, "Alice Smith", summary: "Alice Smith");
            await entityNode3.GenerateNameEmbeddingAsync(embedder, ct);

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await entityNode3.SaveAsync(g.Driver, ct);

            // Search for entity nodes
            var searchFilters = new SearchFilters { NodeLabels = ["Entity"] };
            var nodes = (await SearchUtils.GetRelevantNodesAsync(g.Driver, [entityNode1], searchFilters, minScore: 0.9, cancellationToken: ct))[0];
            Assert.Equal(2, nodes.Count);
            Assert.True(nodes.Select(node => node.Name).ToHashSet().SetEquals([entityNode1.Name, entityNode3.Name]));
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_get_relevant_edges_and_invalidation_candidates()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();
            var graphiti = g.Graphiti(new MockLlmClient(), embedder);
            await graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1", summary: "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2", summary: "test_entity_2");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode3 = Entity(g.GroupId, "test_entity_3", summary: "test_entity_3");
            await entityNode3.GenerateNameEmbeddingAsync(embedder, ct);

            var now = PyClock.Now();
            var createdAt = now;
            var expiredAt = PyClock.Plus(now, 6);
            var validAt = PyClock.Plus(now, 2);
            var invalidAt = PyClock.Plus(now, 4);

            // Create entity edges
            EntityEdge TimedEdge(EntityNode source, EntityNode target, string fact)
            {
                var edge = Edge(g.GroupId, source, target, "RELATES_TO", fact, createdAt);
                edge.ExpiredAt = expiredAt;
                edge.ValidAt = validAt;
                edge.InvalidAt = invalidAt;
                return edge;
            }

            var entityEdge1 = TimedEdge(entityNode1, entityNode2, "Alice");
            await entityEdge1.GenerateEmbeddingAsync(embedder, ct);
            var entityEdge2 = TimedEdge(entityNode2, entityNode3, "Bob");
            await entityEdge2.GenerateEmbeddingAsync(embedder, ct);
            var entityEdge3 = TimedEdge(entityNode1, entityNode3, "Alice");
            await entityEdge3.GenerateEmbeddingAsync(embedder, ct);

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await entityNode3.SaveAsync(g.Driver, ct);
            await entityEdge1.SaveAsync(g.Driver, ct);
            await entityEdge2.SaveAsync(g.Driver, ct);
            await entityEdge3.SaveAsync(g.Driver, ct);

            // Search for entity nodes
            var searchFilters = DateFilters(now, createdAt);
            var edges = (await SearchUtils.GetRelevantEdgesAsync(g.Driver, [entityEdge1], searchFilters, minScore: 0.9, cancellationToken: ct))[0];
            Assert.Single(edges);
            Assert.True(edges.Select(edge => edge.Name).ToHashSet().SetEquals([entityEdge1.Name]));

            edges = (await SearchUtils.GetEdgeInvalidationCandidatesAsync(g.Driver, [entityEdge1], searchFilters, minScore: 0.9, cancellationToken: ct))[0];
            Assert.Equal(2, edges.Count);
            Assert.True(edges.Select(edge => edge.Name).ToHashSet().SetEquals([entityEdge1.Name, entityEdge3.Name]));
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_node_distance_reranker()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode3 = Entity(g.GroupId, "test_entity_3");
            await entityNode3.GenerateNameEmbeddingAsync(embedder, ct);

            // Create entity edges
            var entityEdge1 = Edge(g.GroupId, entityNode1, entityNode2, "RELATES_TO", "test_entity_1 relates to test_entity_2", PyClock.Now());
            await entityEdge1.GenerateEmbeddingAsync(embedder, ct);

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await entityNode3.SaveAsync(g.Driver, ct);
            await entityEdge1.SaveAsync(g.Driver, ct);

            // Test reranker
            var (rerankedUuids, rerankedScores) = await SearchUtils.NodeDistanceRerankerAsync(
                g.Driver, [entityNode2.Uuid, entityNode3.Uuid], entityNode1.Uuid, cancellationToken: ct);
            Dictionary<string, string> uuidToName = new()
            {
                [entityNode1.Uuid] = entityNode1.Name,
                [entityNode2.Uuid] = entityNode2.Name,
                [entityNode3.Uuid] = entityNode3.Name,
            };
            var names = rerankedUuids.Select(uuid => uuidToName[uuid]).ToList();
            Assert.Equal([entityNode2.Name, entityNode3.Name], names);
            GraphFixture.AssertAllClose(rerankedScores, [1.0, 0.0]);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_episode_mentions_reranker()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create episodic nodes
            var episodicNode1 = Episode(g.GroupId, "test_episodic_1", "test_content", PyClock.Now(), PyClock.Now(), "Description about Alice");

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);

            // Create entity edges
            var episodicEdge1 = Mention(g.GroupId, episodicNode1, entityNode1, PyClock.Now());

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await episodicNode1.SaveAsync(g.Driver, ct);
            await episodicEdge1.SaveAsync(g.Driver, ct);

            // Test reranker
            var (rerankedUuids, rerankedScores) = await SearchUtils.EpisodeMentionsRerankerAsync(
                g.Driver, [[entityNode1.Uuid, entityNode2.Uuid]], cancellationToken: ct);
            Dictionary<string, string> uuidToName = new() { [entityNode1.Uuid] = entityNode1.Name, [entityNode2.Uuid] = entityNode2.Name };
            var names = rerankedUuids.Select(uuid => uuidToName[uuid]).ToList();
            Assert.Equal([entityNode1.Name, entityNode2.Name], names);
            GraphFixture.AssertAllClose(rerankedScores, [1.0, 0.0]);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_get_embeddings_for_edges()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);
            var entityNode2 = Entity(g.GroupId, "test_entity_2");
            await entityNode2.GenerateNameEmbeddingAsync(embedder, ct);

            // Create entity edges
            var entityEdge1 = Edge(g.GroupId, entityNode1, entityNode2, "RELATES_TO", "test_entity_1 relates to test_entity_2", PyClock.Now());
            await entityEdge1.GenerateEmbeddingAsync(embedder, ct);

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);
            await entityNode2.SaveAsync(g.Driver, ct);
            await entityEdge1.SaveAsync(g.Driver, ct);

            // Get embeddings for edges
            var embeddings = await SearchUtils.GetEmbeddingsForEdgesAsync(g.Driver, [entityEdge1], ct);
            Assert.Single(embeddings);
            Assert.True(embeddings.ContainsKey(entityEdge1.Uuid));
            GraphFixture.AssertAllClose(embeddings[entityEdge1.Uuid], entityEdge1.FactEmbedding!);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_get_embeddings_for_nodes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create entity nodes
            var entityNode1 = Entity(g.GroupId, "test_entity_1");
            await entityNode1.GenerateNameEmbeddingAsync(embedder, ct);

            // Save the graph
            await entityNode1.SaveAsync(g.Driver, ct);

            // Get embeddings for edges
            var embeddings = await SearchUtils.GetEmbeddingsForNodesAsync(g.Driver, [entityNode1], ct);
            Assert.Single(embeddings);
            Assert.True(embeddings.ContainsKey(entityNode1.Uuid));
            GraphFixture.AssertAllClose(embeddings[entityNode1.Uuid], entityNode1.NameEmbedding!);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_get_embeddings_for_communities()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var g = new GraphFixture();
        await Guard(async () =>
        {
            var embedder = new MockEmbedder();

            // Create community nodes
            var communityNode1 = new CommunityNode { Name = "test_community_1", Labels = [], CreatedAt = PyClock.Now(), GroupId = g.GroupId };
            await communityNode1.GenerateNameEmbeddingAsync(embedder, ct);

            // Save the graph
            await communityNode1.SaveAsync(g.Driver, ct);

            // Get embeddings for communities
            var embeddings = await SearchUtils.GetEmbeddingsForCommunitiesAsync(g.Driver, [communityNode1], ct);
            Assert.Single(embeddings);
            Assert.True(embeddings.ContainsKey(communityNode1.Uuid));
            GraphFixture.AssertAllClose(embeddings[communityNode1.Uuid], communityNode1.NameEmbedding!);
        });
    }
}
