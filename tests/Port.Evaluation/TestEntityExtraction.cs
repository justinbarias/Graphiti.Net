// Ported from tests/utils/maintenance/test_entity_extraction.py (port/RULES.md, R25). The code under test is
// graphiti_core/utils/maintenance/node_operations.py. A pydantic class of a test is the custom type descriptor that the
// port receives (R15).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Prompts;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.NodeOperationsTestSupport;

public class TestEntityExtraction
{
    private static GraphTypeDescriptor Type(string name, string doc) => new(name, doc, []);

    private static OrderedDictionary<string, object?> Entity(string name, long typeId) =>
        Item(("name", name), ("entity_type_id", typeId));

    private static EpisodicNode MakeTestEpisode(string content = "Test content", EpisodeType source = EpisodeType.Text) =>
        new EpisodicNode
        {
            Name = "test_episode",
            GroupId = "group",
            Source = source,
            SourceDescription = "test",
            Content = content,
            ValidAt = DatetimeUtils.UtcNow(),
        }.Validate();

    private static Task<(List<EntityNode>, OrderedDictionary<string, List<long>>)> ExtractAsync(
        GraphitiClients clients,
        EpisodicNode episode,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        List<string>? excludedEntityTypes = null,
        CancellationToken token = default) =>
        LoopGuard.RunAsync(() => NodeOperations.ExtractNodesAsync(
            clients,
            new EpisodeOrEpisodes.One { Episode = episode },
            [],
            entityTypes,
            excludedEntityTypes,
            cancellationToken: token));

    private static string PromptText(MockLlm llm) => string.Join("\n", llm.Calls[^1].Messages.Select(message => message.Content));

    /// <summary>Small inputs should use a single LLM call without chunking.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_small_input_single_llm_call()
    {
        var llm = new MockLlm { ReturnValue = Reply("extracted_entities", Entity("Alice", 0), Entity("Bob", 0)) };
        var clients = MakeClients(llm);

        var (nodes, _) = await ExtractAsync(clients, MakeTestEpisode("Alice talked to Bob."), token: TestContext.Current.CancellationToken);

        Assert.Equal(2, nodes.Count);
        Assert.Equal(new HashSet<string> { "Alice", "Bob" }, nodes.Select(n => n.Name).ToHashSet());
        Assert.Single(llm.Calls);
    }

    /// <summary>Entity type classification should work correctly.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_extracts_entity_types()
    {
        var llm = new MockLlm { ReturnValue = Reply("extracted_entities", Entity("Alice", 1), Entity("Acme Corp", 0)) };
        var clients = MakeClients(llm);

        var (nodes, _) = await ExtractAsync(
            clients,
            MakeTestEpisode("Alice works at Acme Corp."),
            new OrderedDictionary<string, GraphTypeDescriptor> { ["Person"] = Type("Person", "A human person.") },
            token: TestContext.Current.CancellationToken);

        Assert.Contains("Person", nodes.First(n => n.Name == "Alice").Labels);
        Assert.Contains("Entity", nodes.First(n => n.Name == "Acme Corp").Labels);
    }

    /// <summary>Excluded entity types should not be included in the extraction prompt.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_excludes_entity_types()
    {
        var llm = new MockLlm { ReturnValue = Reply("extracted_entities", Entity("Project X", 0)) };
        var clients = MakeClients(llm);

        var (nodes, _) = await ExtractAsync(
            clients,
            MakeTestEpisode("Alice created Project X."),
            new OrderedDictionary<string, GraphTypeDescriptor> { ["User"] = Type("User", "A user of the system.") },
            ["User"],
            TestContext.Current.CancellationToken);

        Assert.Single(nodes);
        Assert.Equal("Project X", nodes[0].Name);
        Assert.DoesNotContain("'entity_type_name': 'User'", PromptText(llm), StringComparison.Ordinal);
    }

    /// <summary>Strict ontology should let custom types use id 0 after Entity is removed.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_excluding_default_entity_remaps_custom_type_to_zero()
    {
        var llm = new MockLlm { ReturnValue = Reply("extracted_entities", Entity("Acme Robotics", 0)) };
        var clients = MakeClients(llm);

        var (nodes, _) = await ExtractAsync(
            clients,
            MakeTestEpisode("Acme Robotics announced the Atlas launch."),
            new OrderedDictionary<string, GraphTypeDescriptor> { ["Company"] = Type("Company", "A named business or organization.") },
            ["Entity"],
            TestContext.Current.CancellationToken);

        Assert.Single(nodes);
        Assert.Equal("Acme Robotics", nodes[0].Name);
        Assert.Equal(new HashSet<string> { "Entity", "Company" }, nodes[0].Labels.ToHashSet());
        var promptText = PromptText(llm);
        Assert.DoesNotContain("'entity_type_name': 'Entity'", promptText, StringComparison.Ordinal);
        Assert.Contains("'entity_type_id': 0", promptText, StringComparison.Ordinal);
        Assert.Contains("'entity_type_name': 'Company'", promptText, StringComparison.Ordinal);
    }

    /// <summary>No allowed entity types means there is nothing to extract.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_excluding_all_entity_types_skips_llm()
    {
        var llm = new MockLlm();
        var clients = MakeClients(llm);

        var (nodes, nodeEpisodeIndexMap) = await ExtractAsync(
            clients,
            MakeTestEpisode("Acme Robotics announced the Atlas launch."),
            excludedEntityTypes: ["Entity"],
            token: TestContext.Current.CancellationToken);

        Assert.Empty(nodes);
        Assert.Empty(nodeEpisodeIndexMap);
        Assert.Empty(llm.Calls);
    }

    /// <summary>Entities with empty names should be filtered out.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_filters_empty_names()
    {
        var llm = new MockLlm { ReturnValue = Reply("extracted_entities", Entity("Alice", 0), Entity(string.Empty, 0), Entity("   ", 0)) };
        var clients = MakeClients(llm);

        var (nodes, _) = await ExtractAsync(clients, MakeTestEpisode("Alice is here."), token: TestContext.Current.CancellationToken);

        Assert.Single(nodes);
        Assert.Equal("Alice", nodes[0].Name);
    }

    /// <summary>Exact same-name duplicates from one message should collapse deterministically.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_collapses_exact_duplicate_names_preferring_specific_type()
    {
        var llm = new MockLlm { ReturnValue = Reply("extracted_entities", Entity("Caroline", 0), Entity("Caroline", 1), Entity("Melanie", 1)) };
        var clients = MakeClients(llm);

        var (nodes, _) = await ExtractAsync(
            clients,
            MakeTestEpisode(
                "Caroline: Hey Mel! Good to see you! How have you been?\n"
                + "Melanie: Hey Caroline! Good to see you!",
                EpisodeType.Message),
            new OrderedDictionary<string, GraphTypeDescriptor> { ["Person"] = Type("Person", "A human person.") },
            token: TestContext.Current.CancellationToken);

        Assert.Equal(["Caroline", "Melanie"], nodes.Select(node => node.Name));
        Assert.Contains("Person", nodes.First(node => node.Name == "Caroline").Labels);
    }

    /// <summary>Text episodes should use extract_text prompt.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_uses_text_prompt_for_text_episodes()
    {
        var llm = new MockLlm { ReturnValue = Reply("extracted_entities") };
        await ExtractAsync(MakeClients(llm), MakeTestEpisode(source: EpisodeType.Text), token: TestContext.Current.CancellationToken);

        Assert.Equal("extract_nodes.extract_text", llm.Calls[^1].PromptName);
    }

    /// <summary>JSON episodes should use extract_json prompt.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_uses_json_prompt_for_json_episodes()
    {
        var llm = new MockLlm { ReturnValue = Reply("extracted_entities") };
        await ExtractAsync(MakeClients(llm), MakeTestEpisode("{}", EpisodeType.Json), token: TestContext.Current.CancellationToken);

        Assert.Equal("extract_nodes.extract_json", llm.Calls[^1].PromptName);
    }

    /// <summary>Message episodes should use extract_message prompt.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_uses_message_prompt_for_message_episodes()
    {
        var llm = new MockLlm { ReturnValue = Reply("extracted_entities") };
        await ExtractAsync(MakeClients(llm), MakeTestEpisode(source: EpisodeType.Message), token: TestContext.Current.CancellationToken);

        Assert.Equal("extract_nodes.extract_message", llm.Calls[^1].PromptName);
    }

    /// <summary>Default Entity type should always be at index 0.</summary>
    [Fact]
    public void test_default_entity_type_always_included()
    {
        var context = NodeOperations.BuildEntityTypesContext(null);

        Assert.Single(context);
        Assert.Equal(0L, context[0]["entity_type_id"]);
        Assert.Equal("Entity", context[0]["entity_type_name"]);
    }

    /// <summary>Custom entity types should be added with sequential IDs.</summary>
    [Fact]
    public void test_custom_types_added_after_default()
    {
        var context = NodeOperations.BuildEntityTypesContext(new OrderedDictionary<string, GraphTypeDescriptor>
        {
            ["Person"] = Type("Person", "A human person."),
            ["Organization"] = Type("Organization", "A business or organization."),
        });

        Assert.Equal(3, context.Count);
        Assert.Equal("Entity", context[0]["entity_type_name"]);
        Assert.Equal("Person", context[1]["entity_type_name"]);
        Assert.Equal(1L, context[1]["entity_type_id"]);
        Assert.Equal("Organization", context[2]["entity_type_name"]);
        Assert.Equal(2L, context[2]["entity_type_id"]);
    }

    /// <summary>Filtered type contexts should expose contiguous IDs to the LLM.</summary>
    [Fact]
    public void test_filter_excluded_entity_types_remaps_ids()
    {
        var context = NodeOperations.BuildEntityTypesContext(new OrderedDictionary<string, GraphTypeDescriptor>
        {
            ["Person"] = Type("Person", "A human person."),
            ["Organization"] = Type("Organization", "A business or organization."),
        });

        var filteredContext = NodeOperations.FilterEntityTypesContext(context, ["Entity", "Person"]);

        Assert.Equal(
            PyJson.Dumps(new List<object?>
            {
                new OrderedDictionary<string, object?>
                {
                    ["entity_type_id"] = 0L,
                    ["entity_type_name"] = "Organization",
                    ["entity_type_description"] = "A business or organization.",
                },
            }),
            PyJson.Dumps(filteredContext));
    }

    /// <summary>Create a test entity node.</summary>
    private static EntityNode MakeEntityNode(string name, string summary = "", string? uuid = null)
    {
        var node = Node(name, summary: summary);
        node.CreatedAt = DatetimeUtils.UtcNow();
        if (uuid is not null)
        {
            node.Uuid = uuid;
        }

        return node;
    }

    /// <summary>Create a test entity edge.</summary>
    private static EntityEdge MakeEntityEdge(string sourceUuid, string targetUuid, string fact) => new()
    {
        SourceNodeUuid = sourceUuid,
        TargetNodeUuid = targetUuid,
        Name = "TEST_RELATION",
        Fact = fact,
        GroupId = "group",
        CreatedAt = DatetimeUtils.UtcNow(),
    };

    private static Task SummariesAsync(MockLlm llm, List<EntityNode> nodes, EpisodicNode? episode, OrderedDictionary<string, List<EntityEdge>>? edgesByNode = null, Func<EntityNode, Task<bool>>? shouldSummarizeNode = null, CancellationToken token = default) =>
        LoopGuard.RunAsync(() => NodeOperations.ExtractEntitySummariesBatchAsync(
            llm,
            nodes,
            episode is null ? null : new EpisodeOrEpisodes.One { Episode = episode },
            episode is null ? null : [],
            shouldSummarizeNode,
            edgesByNode ?? [],
            cancellationToken: token));

    /// <summary>When no nodes need summarization, no LLM call should be made.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_no_nodes_needing_summarization()
    {
        var llm = new MockLlm();

        // Node with short summary that doesn't need LLM
        List<EntityNode> nodes = [MakeEntityNode("Alice", "Alice is a person.")];

        await SummariesAsync(llm, nodes, null, token: TestContext.Current.CancellationToken);

        // LLM should not be called
        Assert.Empty(llm.Calls);

        // Summary should remain unchanged
        Assert.Equal("Alice is a person.", nodes[0].Summary);
    }

    /// <summary>Nodes with short summaries should have edge facts appended without LLM.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_short_summary_with_edge_facts()
    {
        var llm = new MockLlm();
        var node = MakeEntityNode("Alice", "Alice is a person.", "alice-uuid");
        var edge = MakeEntityEdge("alice-uuid", "bob-uuid", "Alice works with Bob.");

        await SummariesAsync(llm, [node], null, new OrderedDictionary<string, List<EntityEdge>> { ["alice-uuid"] = [edge] }, token: TestContext.Current.CancellationToken);

        // LLM should not be called
        Assert.Empty(llm.Calls);

        // Summary should include edge fact
        Assert.Contains("Alice is a person.", node.Summary, StringComparison.Ordinal);
        Assert.Contains("Alice works with Bob.", node.Summary, StringComparison.Ordinal);
    }

    /// <summary>Nodes with long summaries should trigger LLM summarization.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_long_summary_needs_llm()
    {
        var llm = new MockLlm { ReturnValue = Reply("summaries", Item(("name", "Alice"), ("summary", "Alice is a software engineer at Acme Corp."))) };

        // Create a node with a very long summary (over MAX_SUMMARY_CHARS * 4)
        var longSummary = string.Concat(Enumerable.Repeat("Alice is a person. ", 200)); // ~3800 chars
        var node = MakeEntityNode("Alice", longSummary);

        await SummariesAsync(llm, [node], MakeTestEpisode(), token: TestContext.Current.CancellationToken);

        // LLM should be called
        Assert.Single(llm.Calls);

        // Summary should be updated from LLM response
        Assert.Equal("Alice is a software engineer at Acme Corp.", node.Summary);
    }

    /// <summary>Nodes filtered by should_summarize_node should be skipped.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_should_summarize_filter()
    {
        var llm = new MockLlm();
        var node = MakeEntityNode("Alice", string.Empty);

        // Filter that rejects all nodes
        await SummariesAsync(llm, [node], MakeTestEpisode(), shouldSummarizeNode: _ => Task.FromResult(false), token: TestContext.Current.CancellationToken);

        // LLM should not be called
        Assert.Empty(llm.Calls);
    }

    /// <summary>Multiple nodes needing summarization should be batched into one call.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_batch_multiple_nodes()
    {
        var llm = new MockLlm
        {
            ReturnValue = Reply("summaries", Item(("name", "Alice"), ("summary", "Alice summary.")), Item(("name", "Bob"), ("summary", "Bob summary."))),
        };

        // Create nodes with long summaries
        var longSummary = string.Concat(Enumerable.Repeat("X ", 1500)); // Long enough to need LLM
        var alice = MakeEntityNode("Alice", longSummary);
        var bob = MakeEntityNode("Bob", longSummary);

        await SummariesAsync(llm, [alice, bob], MakeTestEpisode(), token: TestContext.Current.CancellationToken);

        // LLM should be called exactly once (batch call)
        Assert.Single(llm.Calls);

        // Both nodes should have updated summaries
        Assert.Equal("Alice summary.", alice.Summary);
        Assert.Equal("Bob summary.", bob.Summary);
    }

    /// <summary>LLM returning unknown entity names should be logged but not crash.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_unknown_entity_in_response()
    {
        var llm = new MockLlm
        {
            ReturnValue = Reply("summaries", Item(("name", "UnknownEntity"), ("summary", "Should be ignored.")), Item(("name", "Alice"), ("summary", "Alice summary."))),
        };
        var alice = MakeEntityNode("Alice", string.Concat(Enumerable.Repeat("X ", 1500)));

        await SummariesAsync(llm, [alice], MakeTestEpisode(), token: TestContext.Current.CancellationToken);

        // Alice should have updated summary
        Assert.Equal("Alice summary.", alice.Summary);
    }

    /// <summary>Nodes with no summary and no episode should be skipped.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_no_episode_and_no_summary()
    {
        var llm = new MockLlm();
        var node = MakeEntityNode("Alice", string.Empty);

        await SummariesAsync(llm, [node], null, token: TestContext.Current.CancellationToken);

        // LLM should not be called - no content to summarize
        Assert.Empty(llm.Calls);
        Assert.Equal(string.Empty, node.Summary);
    }

    /// <summary>Nodes should be partitioned into flights of MAX_NODES.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_flight_partitioning()
    {
        // PORT: the test sets MAX_NODES to 2 with monkeypatch.setattr, which has no C# form: MaxNodes is a const, and R17
        // gives PyPatch<T> only to the names that the lead lists. The test keeps MAX_NODES and passes 2 * MAX_NODES + 1
        // nodes, which gives the same 3 flights (30 + 30 + 1) as 5 nodes with MAX_NODES = 2 (2 + 2 + 1).
        var llm = new MockLlm { ReturnValue = Reply("summaries") };

        // Create nodes with long summaries (need LLM)
        var longSummary = string.Concat(Enumerable.Repeat("X ", 1500));
        var nodes = Enumerable.Range(0, (int)(2 * NodeOperations.MaxNodes) + 1).Select(i => MakeEntityNode($"Entity{i}", longSummary)).ToList();

        await SummariesAsync(llm, nodes, MakeTestEpisode(), token: TestContext.Current.CancellationToken);

        // With MAX_NODES=2 and 5 nodes, we should have 3 flights (2+2+1)
        Assert.Equal(3, llm.Calls.Count);
    }

    /// <summary>LLM response names should match case-insensitively.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_case_insensitive_name_matching()
    {
        // LLM returns name with different casing
        var llm = new MockLlm { ReturnValue = Reply("summaries", Item(("name", "ALICE"), ("summary", "Alice summary from LLM."))) };

        // Node has lowercase name
        var node = MakeEntityNode("alice", string.Concat(Enumerable.Repeat("X ", 1500)));

        await SummariesAsync(llm, [node], MakeTestEpisode(), token: TestContext.Current.CancellationToken);

        // Should match despite case difference
        Assert.Equal("Alice summary from LLM.", node.Summary);
    }

    [Fact]
    public void test_single_sentence_passes_through() =>
        Assert.Equal(
            "A human person who interacts with the system.",
            NodeOperations.TruncateTypeDescription("A human person who interacts with the system."));

    [Fact]
    public void test_stops_at_first_blank_line() =>
        Assert.Equal("A human person.", NodeOperations.TruncateTypeDescription("A human person.\n\nGOOD: Alice, Bob\nBAD: happiness"));

    [Fact]
    public void test_caps_at_3_sentences() =>
        Assert.Equal("First. Second. Third.", NodeOperations.TruncateTypeDescription("First. Second. Third. Fourth should be dropped."));

    [Fact]
    public void test_first_paragraph_with_multiple_sentences() =>
        Assert.Equal(
            "A specific thing. It has properties. Use it wisely.",
            NodeOperations.TruncateTypeDescription("A specific thing. It has properties. Use it wisely.\n\nGOOD: example"));

    [Fact]
    public void test_skips_leading_blank_lines() =>
        Assert.Equal("A location.", NodeOperations.TruncateTypeDescription("\n\nA location.\n\nGOOD: New York"));

    [Fact]
    public void test_joins_multiline_first_paragraph() =>
        Assert.Equal(
            "A specific, named subject or knowledge domain.",
            NodeOperations.TruncateTypeDescription("A specific, named subject\nor knowledge domain.\n\nGOOD: cooking"));

    [Fact]
    public void test_empty_string() => Assert.Equal(string.Empty, NodeOperations.TruncateTypeDescription(string.Empty));

    [Fact]
    public void test_no_sentence_ending_punctuation() =>
        Assert.Equal(
            "A human person who interacts with the system",
            NodeOperations.TruncateTypeDescription("A human person who interacts with the system"));

    [Fact]
    public void test_realistic_preference_docstring()
    {
        var doc =
            "A specific thing that a speaker likes, dislikes, wants, "
            + "or has expressed an opinion about.\n\n"
            + "GOOD: \"sushi\"\nBAD: do not use\n\n"
            + "Trigger patterns: \"I love X\"";
        Assert.Equal(
            "A specific thing that a speaker likes, dislikes, wants, "
            + "or has expressed an opinion about.",
            NodeOperations.TruncateTypeDescription(doc));
    }

    [Fact]
    public void test_preserves_abbreviations()
    {
        var doc = "An entity used in e.g. medical contexts. It tracks Dr. Smith's patients.";
        Assert.Equal(doc, NodeOperations.TruncateTypeDescription(doc));
    }

    [Fact]
    public void test_preserves_decimals()
    {
        var doc = "Requires version 2.0 or higher. Must be certified.";
        Assert.Equal(doc, NodeOperations.TruncateTypeDescription(doc));
    }
}
