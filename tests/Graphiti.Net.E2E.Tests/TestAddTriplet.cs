// Port of .upstream/graphiti/tests/test_add_triplet.py (port/RULES.md, R25) on the real Neo4j.
//
// Findings of the oracle (Python 0.32.0 on the same Neo4j, see reports/L9-P9f.md). The upstream test fails on Python for
// the reasons that follow, so a ported test that is a faithful copy cannot pass:
//   F1. The fixture mock_embedder is Mock(spec=EmbedderClient) with only `create` set. Its `create_batch` is an AsyncMock
//       whose result is empty, so `create_entity_edge_embeddings` raises ValueError (zip strict) and the search of
//       `resolve_extracted_nodes` raises ValueError (`embedder returned 0 vectors for 1 queries`).
//   F2. The fixture mock_llm_client answers each call with {'duplicate_facts': [], 'invalidate_facts': []}. The call that
//       resolves a new node needs `entity_resolutions`, so pydantic raises ValidationError when a node has to be resolved
//       against a candidate.
//   F3. test_add_triplet_updates_labels expects the labels to equal {'Person', 'Employee', 'Manager'}. The code merges the
//       labels with those of the stored node, and the stored node has the label 'Entity', so the set has four labels.
//   F4. The three tests of an invalid uuid expect ValueError 'Node with UUID ... not found'. add_triplet no longer raises
//       it: a node that is not found is resolved as a new node.
//   F5. The upstream answer of the model has no `contradicted_facts`. The real resolve_extracted_edge needs it when a
//       candidate edge exists (EdgeDuplicate), and Python raises the same ValidationError. The patch of the upstream test
//       hides it. The scripted model of these tests adds the key for EdgeDuplicate.
//
// PORT: the C# fixtures remove F1 and F2 so that the tests run: MockEmbedder(batchLikeCreate: true) answers create_batch
// from the same table as `create`, and the scripted model answers a NodeResolutions call with one new node for each
// extracted entity, and each other call as upstream does. The oracle shows that these seven tests pass on Python with the
// same two changes (a scratch pytest plugin, port/judge and .upstream are not changed): merges_attributes, updates_summary,
// with_new_nodes_no_uuid, preserves_existing_attributes, empty_attributes_preserved, and the two edge uuid tests. The
// four tests of F3 and F4 fail on Python with the changes. They assert the result that Python gives (a PORT comment at each).
//
// PORT: the upstream tests patch `graphiti_core.graphiti.search` (the result has no edges) and
// `graphiti_core.graphiti.resolve_extracted_edge` (the result is the edge). A C# module function has no patch (O10). The
// reading of O10 gives the same condition with the fakes of the boundary: the real search finds no edge in the group of
// the test (the group holds only the nodes and the existing edge, and the pair filter reads one pair), and the real
// resolve_extracted_edge answers from the mock model above. The tests do not assert any argument of the two patches.
namespace Graphiti.E2E.Tests;

using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.Port.Evaluation;
using Graphiti.PyCompat;

public sealed partial class TestAddTriplet
{
    private const int TimeLimit = 180_000;
    private const double GuardSeconds = 150;

    private static OrderedDictionary<string, object?> Plain() => new()
    {
        ["duplicate_facts"] = new List<object?>(),
        ["invalidate_facts"] = new List<object?>(),
    };

    /// <summary>The scripted model: one new node for each extracted entity of a NodeResolutions call, and the upstream answer for the others.</summary>
    private static OrderedDictionary<string, object?> Answer(List<Message> messages, IResponseModel? responseModel)
    {
        // PORT: the real resolve_extracted_edge reads `contradicted_facts` of EdgeDuplicate, which the upstream answer lacks
        // (the patch of the upstream test never lets this call run). Python raises the same ValidationError for that answer.
        if (responseModel?.PythonName == "EdgeDuplicate")
        {
            return new OrderedDictionary<string, object?>
            {
                ["duplicate_facts"] = new List<object?>(),
                ["contradicted_facts"] = new List<object?>(),
            };
        }

        if (responseModel?.PythonName != "NodeResolutions")
        {
            return Plain();
        }

        var text = messages[^1].Content;
        var start = text.IndexOf("<EXTRACTED ENTITIES>", StringComparison.Ordinal);
        var end = text.IndexOf("</EXTRACTED ENTITIES>", StringComparison.Ordinal);
        var extracted = start >= 0 && end >= 0 ? text[start..end] : text;
        return new OrderedDictionary<string, object?>
        {
            ["entity_resolutions"] = IdAndName().Matches(extracted)
                .Select(m => (object?)new OrderedDictionary<string, object?>
                {
                    ["id"] = long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
                    ["name"] = m.Groups[2].Value,
                    ["duplicate_candidate_id"] = -1L,
                })
                .ToList(),
        };
    }

    [GeneratedRegex("['\"]id['\"]: (\\d+), ['\"]name['\"]: ['\"]([^'\"]*)['\"]")]
    private static partial Regex IdAndName();

    private sealed class Env : IAsyncDisposable
    {
        internal Env()
        {
            Fixture = new GraphFixture();
            Embedder = new MockEmbedder(batchLikeCreate: true);
            Graphiti = Fixture.Graphiti(new MockLlmClient { Script = Answer }, Embedder);
        }

        internal GraphFixture Fixture { get; }

        internal MockEmbedder Embedder { get; }

        internal GraphitiCore Graphiti { get; }

        internal global::Graphiti.Neo4j.Neo4jDriver Driver => Fixture.Driver;

        internal string Group => Fixture.GroupId;

        public ValueTask DisposeAsync() => Fixture.DisposeAsync();
    }

    private static EntityNode Person(string group, string name, PyDateTime now, string summary, string[] labels, string? uuid = null, params (string Key, object Value)[] attributes)
    {
        var node = new EntityNode { Name = name, GroupId = group, Labels = [.. labels], CreatedAt = now, Summary = summary };
        if (uuid is not null)
        {
            node.Uuid = uuid;
        }

        foreach (var (key, value) in attributes)
        {
            node.Attributes[key] = value;
        }

        return node;
    }

    private static EntityEdge Link(string group, EntityNode source, EntityNode target, string name, string fact, PyDateTime now, string? uuid = null)
    {
        var edge = new EntityEdge
        {
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            Name = name,
            Fact = fact,
            GroupId = group,
            CreatedAt = now,
        };
        if (uuid is not null)
        {
            edge.Uuid = uuid;
        }

        return edge;
    }

    /// <summary>The Python result for an unknown node uuid, read back from Neo4j: two new nodes with the user uuids, and the edge between them.</summary>
    private static async Task AssertStoredAsync(Env env, AddTripletResults result, EntityNode userSource, EntityNode userTarget)
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal(2, result.Nodes.Count);
        Assert.Equal(userSource.Uuid, result.Nodes[0].Uuid);
        Assert.Equal(userTarget.Uuid, result.Nodes[1].Uuid);
        foreach (var (user, name, summary, age) in new[] { (userSource, "Alice", "Alice summary", 30L), (userTarget, "Bob", "Bob summary", 25L) })
        {
            var stored = await EntityNode.GetByUuidAsync(env.Driver, user.Uuid, ct);
            Assert.Equal(name, stored.Name);
            Assert.True(stored.Labels.ToHashSet().SetEquals(["Entity", "Person"]));
            Assert.Equal(summary, stored.Summary);
            Assert.Equal(age, stored.Attributes["age"]);
        }

        var storedEdge = await EntityEdge.GetByUuidAsync(env.Driver, result.Edges[0].Uuid, cancellationToken: ct);
        Assert.Equal("KNOWS", storedEdge.Name);
        Assert.Equal("Alice knows Bob", storedEdge.Fact);
        Assert.Equal(userSource.Uuid, storedEdge.SourceNodeUuid);
        Assert.Equal(userTarget.Uuid, storedEdge.TargetNodeUuid);
    }

    private static async Task Guard(Func<Task> body) => await LoopGuard.RunAsync(body, GuardSeconds);

    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_merges_attributes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var env = new Env();
        await Guard(async () =>
        {
            await env.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();

            // Create an existing node with some attributes
            var existingSource = Person(env.Group, "Alice", now, "Existing summary", ["Person"], attributes: [("age", 30L), ("city", "New York")]);
            await existingSource.GenerateNameEmbeddingAsync(env.Embedder, ct);
            await existingSource.SaveAsync(env.Driver, ct);

            // Create a user-provided node with additional attributes
            var userSource = Person(env.Group, "Alice", now, "Updated summary", ["Person", "Employee"], existingSource.Uuid, ("age", 31L), ("department", "Engineering"));

            // Create target node
            var userTarget = Person(env.Group, "Bob", now, "Bob summary", ["Person"], attributes: [("age", 25L)]);

            // Create edge
            var edge = Link(env.Group, userSource, userTarget, "WORKS_WITH", "Alice works with Bob", now);

            await env.Graphiti.AddTripletAsync(userSource, edge, userTarget, cancellationToken: ct);

            // Verify attributes were merged (not replaced)
            var retrievedSource = await EntityNode.GetByUuidAsync(env.Driver, existingSource.Uuid, ct);
            Assert.True(retrievedSource.Attributes.ContainsKey("age"));
            Assert.Equal(31L, retrievedSource.Attributes["age"]); // Updated value
            Assert.Equal("New York", retrievedSource.Attributes["city"]); // Preserved
            Assert.Equal("Engineering", retrievedSource.Attributes["department"]); // Added
            Assert.Equal("Updated summary", retrievedSource.Summary);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_updates_summary()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var env = new Env();
        await Guard(async () =>
        {
            await env.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();

            // Create an existing node with a summary
            var existingTarget = Person(env.Group, "Bob", now, "Old summary", ["Person"]);
            await existingTarget.GenerateNameEmbeddingAsync(env.Embedder, ct);
            await existingTarget.SaveAsync(env.Driver, ct);

            // Create user-provided nodes
            var userSource = Person(env.Group, "Alice", now, "Alice summary", ["Person"]);
            var userTarget = Person(env.Group, "Bob", now, "New summary for Bob", ["Person"], existingTarget.Uuid);

            var edge = Link(env.Group, userSource, userTarget, "KNOWS", "Alice knows Bob", now);

            await env.Graphiti.AddTripletAsync(userSource, edge, userTarget, cancellationToken: ct);

            // Verify summary was updated
            var retrievedTarget = await EntityNode.GetByUuidAsync(env.Driver, existingTarget.Uuid, ct);
            Assert.Equal("New summary for Bob", retrievedTarget.Summary);
        });
    }

    // PORT: the upstream test asserts that the labels of the stored node are exactly {'Person', 'Employee', 'Manager'}.
    // That test fails on Python 0.32.0 (finding F3). add_triplet merges the labels of the user node with those of the stored
    // node, and the stored node has the label 'Entity'. This test asserts the result that Python gives for the same steps. The
    // oracle (scratch script p9f/measure_triplet.py, Python 0.32.0, same fixtures as above) gave the labels
    // ['Employee', 'Entity', 'Manager', 'Person'].
    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_updates_labels()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var env = new Env();
        await Guard(async () =>
        {
            await env.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();

            // Create an existing node with labels
            var existingSource = Person(env.Group, "Alice", now, string.Empty, ["Person"]);
            await existingSource.GenerateNameEmbeddingAsync(env.Embedder, ct);
            await existingSource.SaveAsync(env.Driver, ct);

            // Create user-provided node with different labels
            var userSource = Person(env.Group, "Alice", now, string.Empty, ["Person", "Employee", "Manager"], existingSource.Uuid);
            var userTarget = Person(env.Group, "Bob", now, string.Empty, ["Person"]);

            var edge = Link(env.Group, userSource, userTarget, "MANAGES", "Alice manages Bob", now);

            await env.Graphiti.AddTripletAsync(userSource, edge, userTarget, cancellationToken: ct);

            // Verify labels were updated
            var retrievedSource = await EntityNode.GetByUuidAsync(env.Driver, existingSource.Uuid, ct);

            // The labels are merged with the stored labels, so 'Entity' stays (the Python result, not the upstream assertion).
            Assert.True(retrievedSource.Labels.ToHashSet().SetEquals(["Employee", "Entity", "Manager", "Person"]));
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_with_new_nodes_no_uuid()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var env = new Env();
        await Guard(async () =>
        {
            await env.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();

            // Create user-provided nodes without UUIDs
            var userSource = Person(env.Group, "Alice", now, "Alice summary", ["Person"], attributes: [("age", 30L)]);
            var userTarget = Person(env.Group, "Bob", now, "Bob summary", ["Person"], attributes: [("age", 25L)]);

            var edge = Link(env.Group, userSource, userTarget, "KNOWS", "Alice knows Bob", now);

            var result = await env.Graphiti.AddTripletAsync(userSource, edge, userTarget, cancellationToken: ct);

            // Verify nodes were created with user-provided attributes
            Assert.True(result.Nodes.Count >= 2);

            // Find the nodes in the result
            var sourceInResult = result.Nodes.FirstOrDefault(n => n.Name == "Alice");
            var targetInResult = result.Nodes.FirstOrDefault(n => n.Name == "Bob");

            if (sourceInResult is not null)
            {
                Assert.Equal(30L, sourceInResult.Attributes["age"]);
                Assert.Equal("Alice summary", sourceInResult.Summary);
            }

            if (targetInResult is not null)
            {
                Assert.Equal(25L, targetInResult.Attributes["age"]);
                Assert.Equal("Bob summary", targetInResult.Summary);
            }
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_preserves_existing_attributes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var env = new Env();
        await Guard(async () =>
        {
            await env.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();

            // Create an existing node with multiple attributes
            var existingSource = Person(
                env.Group, "Alice", now, "Existing summary", ["Person"], attributes: [("age", 30L), ("city", "New York"), ("country", "USA"), ("email", "alice@example.com")]);
            await existingSource.GenerateNameEmbeddingAsync(env.Embedder, ct);
            await existingSource.SaveAsync(env.Driver, ct);

            // Create user-provided node with only some attributes
            var userSource = Person(env.Group, "Alice", now, "Updated summary", ["Person"], existingSource.Uuid, ("age", 31L), ("city", "Boston"));
            var userTarget = Person(env.Group, "Bob", now, string.Empty, ["Person"]);

            var edge = Link(env.Group, userSource, userTarget, "KNOWS", "Alice knows Bob", now);

            await env.Graphiti.AddTripletAsync(userSource, edge, userTarget, cancellationToken: ct);

            // Verify all attributes are preserved/updated correctly
            var retrievedSource = await EntityNode.GetByUuidAsync(env.Driver, existingSource.Uuid, ct);
            Assert.Equal(31L, retrievedSource.Attributes["age"]); // Updated
            Assert.Equal("Boston", retrievedSource.Attributes["city"]); // Updated
            Assert.Equal("USA", retrievedSource.Attributes["country"]); // Preserved
            Assert.Equal("alice@example.com", retrievedSource.Attributes["email"]); // Preserved
            Assert.Equal("Updated summary", retrievedSource.Summary);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_empty_attributes_preserved()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var env = new Env();
        await Guard(async () =>
        {
            await env.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();

            // Create an existing node with attributes
            var existingSource = Person(env.Group, "Alice", now, "Existing summary", ["Person"], attributes: [("age", 30L), ("city", "New York")]);
            await existingSource.GenerateNameEmbeddingAsync(env.Embedder, ct);
            await existingSource.SaveAsync(env.Driver, ct);

            // Create user-provided node with empty attributes
            var userSource = Person(env.Group, "Alice", now, string.Empty, ["Person"], existingSource.Uuid); // Empty summary should not overwrite
            var userTarget = Person(env.Group, "Bob", now, string.Empty, ["Person"]);

            var edge = Link(env.Group, userSource, userTarget, "KNOWS", "Alice knows Bob", now);

            await env.Graphiti.AddTripletAsync(userSource, edge, userTarget, cancellationToken: ct);

            // Verify existing attributes are preserved when user provides empty dict
            var retrievedSource = await EntityNode.GetByUuidAsync(env.Driver, existingSource.Uuid, ct);

            // Empty attributes dict should not clear existing attributes
            Assert.True(retrievedSource.Attributes.ContainsKey("age"));
            Assert.True(retrievedSource.Attributes.ContainsKey("city"));

            // Empty summary should not overwrite existing summary
            Assert.Equal("Existing summary", retrievedSource.Summary);
        });
    }

    // F4 of the header: add_triplet resolves a node that is not found as a new node, and raises no error.
    // PORT: the upstream test asserts that add_triplet raises ValueError 'Node with UUID {uuid} not found' for an invalid source uuid. That test
    // fails on Python 0.32.0 (finding F4): add_triplet raises no error. A node that is not found is resolved as a new node
    // and saved with the uuid of the user node. This test asserts what Python gave for the same steps (scratch script
    // p9f/measure_triplet.py, case "F4 source"): no error, two stored nodes (names Alice and Bob, labels Entity and Person,
    // the summaries and the age attributes of the user nodes, the uuids of the user nodes), and one stored edge KNOWS
    // 'Alice knows Bob' from the first node to the second node.
    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_invalid_source_uuid()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var env = new Env();
        await Guard(async () =>
        {
            await env.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();

            // Create a node with a UUID that doesn't exist in the database
            var invalidUuid = Guid.NewGuid().ToString();
            var userSource = Person(env.Group, "Alice", now, "Alice summary", ["Person"], invalidUuid, ("age", 30L));
            var userTarget = Person(env.Group, "Bob", now, "Bob summary", ["Person"], attributes: [("age", 25L)]);

            var edge = Link(env.Group, userSource, userTarget, "KNOWS", "Alice knows Bob", now);

            // Python gives no error (see the PORT comment)
            var result = await env.Graphiti.AddTripletAsync(userSource, edge, userTarget, cancellationToken: ct);
            await AssertStoredAsync(env, result, userSource, userTarget);
        });
    }

    // PORT: the upstream test asserts that add_triplet raises ValueError 'Node with UUID {uuid} not found' for an invalid target uuid. That test
    // fails on Python 0.32.0 (finding F4): add_triplet raises no error. A node that is not found is resolved as a new node
    // and saved with the uuid of the user node. This test asserts what Python gave for the same steps (scratch script
    // p9f/measure_triplet.py, case "F4 target"): no error, two stored nodes (names Alice and Bob, labels Entity and Person,
    // the summaries and the age attributes of the user nodes, the uuids of the user nodes), and one stored edge KNOWS
    // 'Alice knows Bob' from the first node to the second node.
    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_invalid_target_uuid()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var env = new Env();
        await Guard(async () =>
        {
            await env.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();

            // Create an existing source node
            var existingSource = Person(env.Group, "Alice", now, "Alice summary", ["Person"], attributes: [("age", 30L)]);
            await existingSource.GenerateNameEmbeddingAsync(env.Embedder, ct);
            await existingSource.SaveAsync(env.Driver, ct);

            // Create a target node with a UUID that doesn't exist in the database
            var invalidUuid = Guid.NewGuid().ToString();
            var userSource = Person(env.Group, "Alice", now, "Alice summary", ["Person"], existingSource.Uuid, ("age", 30L));
            var userTarget = Person(env.Group, "Bob", now, "Bob summary", ["Person"], invalidUuid, ("age", 25L));

            var edge = Link(env.Group, userSource, userTarget, "KNOWS", "Alice knows Bob", now);

            // Python gives no error (see the PORT comment)
            var result = await env.Graphiti.AddTripletAsync(userSource, edge, userTarget, cancellationToken: ct);
            await AssertStoredAsync(env, result, userSource, userTarget);
        });
    }

    // PORT: the upstream test asserts that add_triplet raises ValueError 'Node with UUID {uuid} not found' for source first when both uuids are invalid. That test
    // fails on Python 0.32.0 (finding F4): add_triplet raises no error. A node that is not found is resolved as a new node
    // and saved with the uuid of the user node. This test asserts what Python gave for the same steps (scratch script
    // p9f/measure_triplet.py, case "F4 both"): no error, two stored nodes (names Alice and Bob, labels Entity and Person,
    // the summaries and the age attributes of the user nodes, the uuids of the user nodes), and one stored edge KNOWS
    // 'Alice knows Bob' from the first node to the second node.
    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_invalid_both_uuids()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var env = new Env();
        await Guard(async () =>
        {
            await env.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();

            // Create nodes with UUIDs that don't exist in the database
            var invalidSourceUuid = Guid.NewGuid().ToString();
            var invalidTargetUuid = Guid.NewGuid().ToString();

            var userSource = Person(env.Group, "Alice", now, "Alice summary", ["Person"], invalidSourceUuid, ("age", 30L));
            var userTarget = Person(env.Group, "Bob", now, "Bob summary", ["Person"], invalidTargetUuid, ("age", 25L));

            var edge = Link(env.Group, userSource, userTarget, "KNOWS", "Alice knows Bob", now);

            // Python gives no error (see the PORT comment)
            var result = await env.Graphiti.AddTripletAsync(userSource, edge, userTarget, cancellationToken: ct);
            await AssertStoredAsync(env, result, userSource, userTarget);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_edge_uuid_with_different_nodes_creates_new_edge()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var env = new Env();
        await Guard(async () =>
        {
            await env.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();

            // Create existing nodes: Alice and Bob
            var alice = Person(env.Group, "Alice", now, "Alice summary", ["Person"]);
            await alice.GenerateNameEmbeddingAsync(env.Embedder, ct);
            await alice.SaveAsync(env.Driver, ct);

            var bob = Person(env.Group, "Bob", now, "Bob summary", ["Person"]);
            await bob.GenerateNameEmbeddingAsync(env.Embedder, ct);
            await bob.SaveAsync(env.Driver, ct);

            // Create a third node: Charlie
            var charlie = Person(env.Group, "Charlie", now, "Charlie summary", ["Person"]);
            await charlie.GenerateNameEmbeddingAsync(env.Embedder, ct);
            await charlie.SaveAsync(env.Driver, ct);

            // Create an existing edge between Alice and Bob
            var existingEdge = Link(env.Group, alice, bob, "KNOWS", "Alice knows Bob", now);
            await existingEdge.GenerateEmbeddingAsync(env.Embedder, ct);
            await existingEdge.SaveAsync(env.Driver, ct);

            // Now try to add a triplet using the existing edge UUID but with different nodes (Alice -> Charlie)
            var newEdgeWithSameUuid = Link(env.Group, alice, charlie, "KNOWS", "Alice knows Charlie", now, existingEdge.Uuid);

            var result = await env.Graphiti.AddTripletAsync(alice, newEdgeWithSameUuid, charlie, cancellationToken: ct);

            // The original edge (Alice -> Bob) should still exist
            var originalEdge = await EntityEdge.GetByUuidAsync(env.Driver, existingEdge.Uuid, cancellationToken: ct);
            Assert.Equal(alice.Uuid, originalEdge.SourceNodeUuid);
            Assert.Equal(bob.Uuid, originalEdge.TargetNodeUuid);
            Assert.Equal("Alice knows Bob", originalEdge.Fact);

            // The new edge should have a different UUID
            var newEdge = result.Edges[0];
            Assert.NotEqual(existingEdge.Uuid, newEdge.Uuid);
            Assert.Equal(alice.Uuid, newEdge.SourceNodeUuid);
            Assert.Equal(charlie.Uuid, newEdge.TargetNodeUuid);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task test_add_triplet_edge_uuid_with_same_nodes_updates_edge()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var env = new Env();
        await Guard(async () =>
        {
            await env.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

            var now = PyClock.Now();

            // Create existing nodes: Alice and Bob
            var alice = Person(env.Group, "Alice", now, "Alice summary", ["Person"]);
            await alice.GenerateNameEmbeddingAsync(env.Embedder, ct);
            await alice.SaveAsync(env.Driver, ct);

            var bob = Person(env.Group, "Bob", now, "Bob summary", ["Person"]);
            await bob.GenerateNameEmbeddingAsync(env.Embedder, ct);
            await bob.SaveAsync(env.Driver, ct);

            // Create an existing edge between Alice and Bob
            var existingEdge = Link(env.Group, alice, bob, "KNOWS", "Alice knows Bob", now);
            await existingEdge.GenerateEmbeddingAsync(env.Embedder, ct);
            await existingEdge.SaveAsync(env.Driver, ct);

            // Now update the edge with the same source/target but different fact
            var updatedEdge = Link(env.Group, alice, bob, "WORKS_WITH", "Alice works with Bob", now, existingEdge.Uuid);

            var result = await env.Graphiti.AddTripletAsync(alice, updatedEdge, bob, cancellationToken: ct);

            // The edge should keep the same UUID (update allowed)
            var resultEdge = result.Edges[0];
            Assert.Equal(existingEdge.Uuid, resultEdge.Uuid);
        });
    }

    // TODO(port): O10 test_add_triplet_passes_caller_episode_to_edge_dedupe
    // The upstream test replaces five module functions and two class methods (EntityNode.get_by_uuid,
    // EntityEdge.get_by_uuid, search, resolve_extracted_edge, create_entity_edge_embeddings, create_entity_node_embeddings,
    // add_nodes_and_edges_bulk) on a mocked driver, and it asserts the object that reaches the fifth argument of
    // resolve_extracted_edge (`args[4] is shadow`). No fake at the network boundary shows that argument: with no candidate
    // edge and an edge that has valid_at, resolve_extracted_edge reads the episode in no call of the model and no query.
    // A recorded case that covers the behavior: none. The recorded cases add_triplet/new_nodes and
    // add_triplet/existing_nodes of values/graphiti.json (ported in tests/Port.Evaluation/GraphitiValuesTests.cs) run
    // add_triplet with and without an episode, and they cover the group check and the call sequence, not this argument.

    // TODO(port): O10 test_add_triplet_builds_dedupe_episode_when_caller_omits_it
    // The same patches as the test above. The assertions read the built episode (an EpisodicNode with the group of the edge,
    // valid_at equal to edge.valid_at, and a uuid) at the fifth argument of the patched resolve_extracted_edge. No fake at
    // the network boundary shows it. A recorded case that covers the behavior: none (add_triplet/new_nodes covers the
    // call with no episode, and its events do not hold the built episode).
}
