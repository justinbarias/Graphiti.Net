// Ported from tests/utils/maintenance/test_combined_extraction.py (port/RULES.md, R25). The code under test is
// graphiti_core/utils/maintenance/combined_extraction.py. The two tests of that file whose code under test is
// graphiti_core/prompts/extract_nodes_and_edges.py are in PromptsCombinedExtractionTests.
//
// `MagicMock()` with an AsyncMock `generate_response` is EdgeTestLlm. Its `side_effect` list is a function that gives the
// replies in order. The driver, the embedder, and the cross-encoder are fakes that the function does not call.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class TestCombinedExtraction
{
    private static readonly OrderedDictionary<string, object?> NullBatch = J("""{"timestamps": [{"valid_at": null, "invalid_at": null}]}""");

    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_excluding_default_entity_remaps_custom_type_to_zero()
    {
        var company = new GraphTypeDescriptor("Company", "A named business or organization.", []);
        var (clients, llm) = MakeClients(
            J("""
            {"extracted_entities": [{"name": "Acme Robotics", "entity_type_id": 0}, {"name": "Atlas launch", "entity_type_id": 0}],
             "edges": [{"source_entity_name": "Acme Robotics", "target_entity_name": "Atlas launch", "relation_type": "ANNOUNCED",
                        "fact": "Acme Robotics announced the Atlas launch.", "episode_indices": [0]}]}
            """),
            NullBatch);

        var (nodes, edges, nodeEpisodeIndexMap) = await RunAsync(
            clients, One(MakeEpisode()), entityTypes: new() { ["Company"] = company }, excludedEntityTypes: ["Entity"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new HashSet<string> { "Acme Robotics", "Atlas launch" }, nodes.Select(n => n.Name).ToHashSet());
        Assert.All(nodes, node => Assert.Equal(new HashSet<string> { "Entity", "Company" }, node.Labels.ToHashSet()));
        Assert.Single(edges);
        Assert.Equal(nodes.Count, nodeEpisodeIndexMap.Count);
        Assert.All(nodeEpisodeIndexMap.Values, indices => Assert.Equal([0L], indices));

        var promptText = PromptText(llm, 0);
        Assert.DoesNotContain("'entity_type_name': 'Entity'", promptText, StringComparison.Ordinal);
        Assert.Contains("'entity_type_id': 0", promptText, StringComparison.Ordinal);
        Assert.Contains("'entity_type_name': 'Company'", promptText, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_strict_edge_types_filters_derived_relations()
    {
        var product = new GraphTypeDescriptor("Product", "A named commercial product.", []);
        var integratesWith = new GraphTypeDescriptor("INTEGRATES_WITH", "One product integrates with another product.", []);
        var (clients, llm) = MakeClients(
            J("""
            {"extracted_entities": [{"name": "Atlas Home Robot", "entity_type_id": 0}, {"name": "Acme Smart Hub", "entity_type_id": 0}],
             "edges": [{"source_entity_name": "Atlas Home Robot", "target_entity_name": "Acme Smart Hub", "relation_type": "INTEGRATES_WITH",
                        "fact": "Atlas Home Robot integrates with Acme Smart Hub.", "episode_indices": [0]},
                       {"source_entity_name": "Atlas Home Robot", "target_entity_name": "Acme Smart Hub", "relation_type": "SOLD_WITH",
                        "fact": "Atlas Home Robot is sold with Acme Smart Hub.", "episode_indices": [0]}]}
            """),
            NullBatch);

        var (nodes, edges, _) = await RunAsync(
            clients,
            One(MakeEpisode()),
            entityTypes: new() { ["Product"] = product },
            excludedEntityTypes: ["Entity"],
            edgeTypes: new() { ["INTEGRATES_WITH"] = integratesWith },
            edgeTypeMap: new() { [("Product", "Product")] = ["INTEGRATES_WITH"] },
            strictEdgeTypes: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new HashSet<string> { "Atlas Home Robot", "Acme Smart Hub" }, nodes.Select(n => n.Name).ToHashSet());
        Assert.Equal(["INTEGRATES_WITH"], edges.Select(e => e.Name));
        var promptText = PromptText(llm, 0);
        Assert.Contains("If a relationship does not match any FACT TYPE, skip it", promptText, StringComparison.Ordinal);
        Assert.Contains("derive a new relation_type", promptText, StringComparison.Ordinal);
    }

    /// <summary>Dangling target names become EntityNodes; the edge is kept, not dropped.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_materializes_missing_target_endpoint()
    {
        var (clients, _) = MakeClients(
            J("""
            {"extracted_entities": [{"name": "AMI", "entity_type_id": 0}],
             "edges": [{"source_entity_name": "AMI", "target_entity_name": "version 2.18.1", "relation_type": "PINNED_AT",
                        "fact": "AMI is pinned at version 2.18.1.", "episode_indices": [0]}]}
            """),
            NullBatch);

        var (nodes, edges, nodeEpisodeIndexMap) = await RunAsync(clients, One(MakeEpisode()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new HashSet<string> { "AMI", "version 2.18.1" }, nodes.Select(n => n.Name).ToHashSet());
        Assert.Single(edges);
        Assert.Equal("PINNED_AT", edges[0].Name);
        Assert.Equal("AMI is pinned at version 2.18.1.", edges[0].Fact);

        var nameToUuid = nodes.ToDictionary(n => n.Name, n => n.Uuid);
        Assert.Equal(nameToUuid["AMI"], edges[0].SourceNodeUuid);
        Assert.Equal(nameToUuid["version 2.18.1"], edges[0].TargetNodeUuid);
        Assert.NotEqual(edges[0].SourceNodeUuid, edges[0].TargetNodeUuid);
        Assert.Equal(nameToUuid.Values.ToHashSet(), nodeEpisodeIndexMap.Keys.ToHashSet());
    }

    /// <summary>Dangling source names become EntityNodes; the edge is kept, not dropped.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_materializes_missing_source_endpoint()
    {
        var (clients, _) = MakeClients(
            J("""
            {"extracted_entities": [{"name": "production cluster", "entity_type_id": 0}],
             "edges": [{"source_entity_name": "version 6.8.6", "target_entity_name": "production cluster", "relation_type": "DEPLOYED_ON",
                        "fact": "version 6.8.6 is deployed on the production cluster.", "episode_indices": [0]}]}
            """),
            NullBatch);

        var (nodes, edges, _) = await RunAsync(clients, One(MakeEpisode()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new HashSet<string> { "version 6.8.6", "production cluster" }, nodes.Select(n => n.Name).ToHashSet());
        Assert.Single(edges);
        var nameToUuid = nodes.ToDictionary(n => n.Name, n => n.Uuid);
        Assert.Equal(nameToUuid["version 6.8.6"], edges[0].SourceNodeUuid);
        Assert.Equal(nameToUuid["production cluster"], edges[0].TargetNodeUuid);
    }

    /// <summary>Happy path: both endpoints already extracted — no materialization needed.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_keeps_edge_when_both_endpoints_present()
    {
        var (clients, llm) = MakeClients(
            J("""
            {"extracted_entities": [{"name": "AMI", "entity_type_id": 0}, {"name": "version 2.18.1", "entity_type_id": 0}],
             "edges": [{"source_entity_name": "AMI", "target_entity_name": "version 2.18.1", "relation_type": "PINNED_AT",
                        "fact": "AMI is pinned at version 2.18.1.", "episode_indices": [0]}]}
            """),
            NullBatch);

        var (nodes, edges, _) = await RunAsync(clients, One(MakeEpisode()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new HashSet<string> { "AMI", "version 2.18.1" }, nodes.Select(n => n.Name).ToHashSet());
        Assert.Single(edges);
        Assert.All(nodes, node => Assert.Equal(new HashSet<string> { "Entity" }, node.Labels.ToHashSet()));

        var promptText = PromptText(llm, 0);
        Assert.Contains("source_entity_name and target_entity_name must be names from extracted_entities", promptText, StringComparison.Ordinal);
        Assert.Contains("If an endpoint is a value such as a version, add it as an entity.", promptText, StringComparison.Ordinal);
    }

    /// <summary>Case/whitespace differences still match existing nodes; no duplicate materialization.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_resolves_dangling_endpoint_case_insensitively()
    {
        var (clients, _) = MakeClients(
            J("""
            {"extracted_entities": [{"name": "AMI", "entity_type_id": 0}, {"name": "Version 2.18.1", "entity_type_id": 0}],
             "edges": [{"source_entity_name": "ami", "target_entity_name": "version 2.18.1", "relation_type": "PINNED_AT",
                        "fact": "AMI is pinned at version 2.18.1.", "episode_indices": [0]}]}
            """),
            NullBatch);

        var (nodes, edges, _) = await RunAsync(clients, One(MakeEpisode()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, nodes.Count);
        Assert.Single(edges);
        var nameToUuid = nodes.ToDictionary(n => n.Name, n => n.Uuid);
        Assert.Equal(nameToUuid["AMI"], edges[0].SourceNodeUuid);
        Assert.Equal(nameToUuid["Version 2.18.1"], edges[0].TargetNodeUuid);
    }

    /// <summary>Excluded entity types stay out of the graph even if used as edge endpoints.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_does_not_resurrect_excluded_entity_endpoint()
    {
        var person = new GraphTypeDescriptor("Person", "A person.", []);
        var (clients, _) = MakeClients(
            J("""
            {"extracted_entities": [{"name": "Alice", "entity_type_id": 0}, {"name": "secret value", "entity_type_id": 1}],
             "edges": [{"source_entity_name": "Alice", "target_entity_name": "secret value", "relation_type": "KNOWS",
                        "fact": "Alice knows secret value.", "episode_indices": [0]}]}
            """),
            NullBatch);

        var (nodes, edges, _) = await RunAsync(
            clients, One(MakeEpisode()), entityTypes: new() { ["Person"] = person }, excludedEntityTypes: ["Entity"], cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("secret value", nodes.Select(n => n.Name));
        Assert.Empty(edges);
    }

    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_pairwise_null_batch_does_not_retry()
    {
        var (clients, llm) = MakeClients(AliceKnowsBob(), NullBatch);

        var (_, edges, _) = await RunAsync(clients, One(MakeEpisode()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(edges);
        Assert.Null(edges[0].ValidAt);
        Assert.Equal(2, llm.Calls.Count);
        Assert.Equal("extract_edges.extract_timestamps_batch", llm.Calls[1].PromptName);
    }

    /// <summary>A pairwise edge keeps the extracted end time even when it precedes its start.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_pairwise_keeps_an_end_before_its_start()
    {
        const string start = "2024-02-01T00:00:00+00:00";
        const string endBeforeStart = "2024-01-01T00:00:00+00:00";
        var (clients, _) = MakeClients(
            AliceKnowsBob(),
            J($$"""{"timestamps": [{"valid_at": "{{start}}", "invalid_at": "{{endBeforeStart}}"}]}"""));

        var (_, edges, _) = await RunAsync(clients, One(MakeEpisode()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(edges);
        Assert.NotNull(edges[0].ValidAt);
        Assert.NotNull(edges[0].InvalidAt);
        Assert.Equal(start, edges[0].ValidAt!.Value.IsoFormat());
        Assert.Equal(endBeforeStart, edges[0].InvalidAt!.Value.IsoFormat());
    }

    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_renders_timeline_with_context_turns()
    {
        var (clients, llm) = MakeClients(
            J("""{"extracted_entities": [{"name": "Acme Robotics", "entity_type_id": 0}], "edges": []}"""),
            NullBatch);

        var targetA = MakeEpisode();
        targetA.Content = "user turn one";
        var contextB = MakeEpisode();
        contextB.Content = "ignored assistant turn";
        var targetC = MakeEpisode();
        targetC.Content = "user turn two";

        await RunAsync(clients, Many(targetA, targetC), timeline: [targetA, contextB, targetC], cancellationToken: TestContext.Current.CancellationToken);

        var promptText = PromptText(llm, 0);
        var episode0 = promptText.IndexOf("[Episode 0]", StringComparison.Ordinal);
        var context = promptText.IndexOf("[CONTEXT EPISODE] (timestamp:", StringComparison.Ordinal);
        var episode1 = promptText.IndexOf("[Episode 1]", StringComparison.Ordinal);
        Assert.True(episode0 >= 0 && context >= 0 && episode1 >= 0);
        Assert.True(episode0 < context && context < episode1);
        Assert.Contains("ignored assistant turn", promptText, StringComparison.Ordinal);
        Assert.Contains("[CONTEXT EPISODE] are not numbered episodes", promptText, StringComparison.Ordinal);
        Assert.Contains("PREVIOUS MESSAGES and [CONTEXT EPISODE] messages to resolve", promptText, StringComparison.Ordinal);
        Assert.DoesNotContain("CONTEXT EPISODES:", promptText, StringComparison.Ordinal);
    }

    /// <summary>Indices in the LLM response point at numbered targets, not context turns.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_timeline_indices_attribute_to_targets_only()
    {
        var (clients, _) = MakeClients(
            J("""
            {"extracted_entities": [{"name": "Acme Robotics", "entity_type_id": 0}, {"name": "Atlas launch", "entity_type_id": 0}],
             "edges": [{"source_entity_name": "Acme Robotics", "target_entity_name": "Atlas launch", "relation_type": "ANNOUNCED",
                        "fact": "Acme Robotics announced the Atlas launch.", "episode_indices": [1]}]}
            """),
            NullBatch);

        var targetA = MakeEpisode();
        targetA.Content = "user turn one";
        var contextB = MakeEpisode();
        contextB.Content = "ignored assistant turn";
        var targetC = MakeEpisode();
        targetC.Content = "user turn two";

        var (_, edges, nodeEpisodeIndexMap) = await RunAsync(clients, Many(targetA, targetC), timeline: [targetA, contextB, targetC], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Single(edges);
        Assert.Equal([targetC.Uuid], edges[0].Episodes);
        Assert.DoesNotContain(contextB.Uuid, edges[0].Episodes);
        Assert.NotEmpty(nodeEpisodeIndexMap);
        Assert.All(nodeEpisodeIndexMap.Values, indices => Assert.Equal([1L], indices));
    }

    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_renders_custom_instructions_after_fact_types()
    {
        var product = new GraphTypeDescriptor("Product", "A named product.", []);
        var sold = new GraphTypeDescriptor("SOLD", "One entity sold another entity.", []);
        var (clients, llm) = MakeClients(
            J("""{"extracted_entities": [], "edges": []}"""),
            J("""{"timestamps": []}"""));

        await RunAsync(
            clients,
            One(MakeEpisode()),
            entityTypes: new() { ["Product"] = product },
            edgeTypes: new() { ["SOLD"] = sold },
            edgeTypeMap: new() { [("Product", "Product")] = ["SOLD"] },
            customExtractionInstructions: "Only extract products launched after 2020.", cancellationToken: TestContext.Current.CancellationToken);

        var promptText = PromptText(llm, 0);
        Assert.Contains("<CUSTOM INSTRUCTIONS>", promptText, StringComparison.Ordinal);
        Assert.Contains("Only extract products launched after 2020.", promptText, StringComparison.Ordinal);
        Assert.Contains("the CUSTOM INSTRUCTION wins", promptText, StringComparison.Ordinal);
        Assert.True(promptText.IndexOf("</FACT TYPES>", StringComparison.Ordinal) < promptText.IndexOf("<CUSTOM INSTRUCTIONS>", StringComparison.Ordinal));
        Assert.True(promptText.IndexOf("<CUSTOM INSTRUCTIONS>", StringComparison.Ordinal) < promptText.IndexOf("<PREVIOUS MESSAGES>", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_omits_custom_instructions_block_when_empty()
    {
        var (clients, llm) = MakeClients(
            J("""{"extracted_entities": [], "edges": []}"""),
            J("""{"timestamps": []}"""));

        await RunAsync(clients, One(MakeEpisode()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("<CUSTOM INSTRUCTIONS>", PromptText(llm, 0), StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_dates_facts_in_groups()
    {
        var factCount = (int)CombinedExtractionModule.TimestampBatchSize + 5;
        var extractResponse = new OrderedDictionary<string, object?>
        {
            ["extracted_entities"] = Enumerable.Range(0, factCount + 1)
                .Select(i => (object?)new OrderedDictionary<string, object?> { ["name"] = $"Entity {i}", ["entity_type_id"] = 0L })
                .ToList(),
            ["edges"] = Enumerable.Range(0, factCount)
                .Select(i => (object?)new OrderedDictionary<string, object?>
                {
                    ["source_entity_name"] = $"Entity {i}",
                    ["target_entity_name"] = $"Entity {i + 1}",
                    ["relation_type"] = "LINKS_TO",
                    ["fact"] = $"Entity {i} links to Entity {i + 1}.",
                    ["episode_indices"] = new List<object?> { 0L },
                })
                .ToList(),
        };
        List<long> groupSizes = [];

        var llm = new EdgeTestLlm();
        llm.Respond = (messages, promptName) =>
        {
            if (promptName != "extract_edges.extract_timestamps_batch")
            {
                return Copy(extractResponse);
            }

            var prompt = string.Join("\n", messages.Select(m => m.Content));
            long size = Enumerable.Range(0, factCount).Count(i => prompt.Contains($"Entity {i} links to Entity", StringComparison.Ordinal));
            groupSizes.Add(size);
            if (groupSizes.Count == 1)
            {
                throw new InvalidOperationException("batch timestamps failed");
            }

            return new OrderedDictionary<string, object?>
            {
                ["timestamps"] = Enumerable.Range(0, (int)size)
                    .Select(_ => (object?)new OrderedDictionary<string, object?> { ["valid_at"] = "2024-01-01T00:00:00Z", ["invalid_at"] = null })
                    .ToList(),
            };
        };

        var (_, edges, _) = await RunAsync(Clients(llm), One(MakeEpisode()), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(factCount, edges.Count);
        Assert.Equal(new List<long> { CombinedExtractionModule.TimestampBatchSize, CombinedExtractionModule.TimestampBatchSize, 5 }.Order(), groupSizes.Order());
        Assert.All(edges, edge => Assert.NotNull(edge.ValidAt));
    }

    private static OrderedDictionary<string, object?> J(string json) => (OrderedDictionary<string, object?>)PyJson.Loads(json)!;

    private static OrderedDictionary<string, object?> Copy(OrderedDictionary<string, object?> value) => J(PyJson.Dumps(value));

    private static OrderedDictionary<string, object?> AliceKnowsBob() => J("""
        {"extracted_entities": [{"name": "Alice", "entity_type_id": 0}, {"name": "Bob", "entity_type_id": 0}],
         "edges": [{"source_entity_name": "Alice", "target_entity_name": "Bob", "relation_type": "KNOWS",
                    "fact": "Alice knows Bob.", "episode_indices": [0]}]}
        """);

    private static GraphitiClients Clients(EdgeTestLlm llm) => new()
    {
        Driver = new EdgeTestSearchDriver(),
        LlmClient = llm,
        Embedder = new EdgeTestEmbedder(),
        CrossEncoder = new EdgeOperationsRun.FakeCrossEncoder(),
        Tracer = new NoOpTracer(),
    };

    // `llm_generate.side_effect = [...]`: each call takes the next reply. A call after the last reply fails, as the
    // StopAsyncIteration of the mock does.
    private static (GraphitiClients Clients, EdgeTestLlm Llm) MakeClients(params OrderedDictionary<string, object?>[] sideEffect)
    {
        var llm = new EdgeTestLlm();
        var index = 0;
        llm.Respond = (_, _) => index < sideEffect.Length ? Copy(sideEffect[index++]) : throw new InvalidOperationException("StopAsyncIteration");
        return (Clients(llm), llm);
    }

    private static EpisodicNode MakeEpisode() => new EpisodicNode
    {
        Name = "test_episode",
        GroupId = "group",
        Source = EpisodeType.Text,
        SourceDescription = "test",
        Content = "Acme Robotics announced the Atlas launch.",
        ValidAt = DatetimeUtils.UtcNow(),
    }.Validate();

    private static EpisodeOrEpisodes.One One(EpisodicNode episode) => new EpisodeOrEpisodes.One { Episode = episode };

    private static EpisodeOrEpisodes.Many Many(params EpisodicNode[] episodes) => new EpisodeOrEpisodes.Many { Episodes = [.. episodes] };

    // '\n'.join(message.content for message in llm_generate.call_args_list[i].args[0])
    private static string PromptText(EdgeTestLlm llm, int call) => string.Join("\n", llm.Calls[call].Messages.Select(m => m.Content));

    private static Task<(List<EntityNode>, List<EntityEdge>, OrderedDictionary<string, List<long>>)> RunAsync(
        GraphitiClients clients,
        EpisodeOrEpisodes episode,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        List<string>? excludedEntityTypes = null,
        OrderedDictionary<(string, string), List<string>>? edgeTypeMap = null,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes = null,
        bool strictEdgeTypes = false,
        string? customExtractionInstructions = null,
        List<EpisodicNode>? timeline = null,
        CancellationToken cancellationToken = default) =>
        LoopGuard.RunAsync(() => CombinedExtractionModule.ExtractNodesAndEdgesAsync(
            clients,
            episode,
            previousEpisodes: [],
            entityTypes: entityTypes,
            excludedEntityTypes: excludedEntityTypes,
            edgeTypeMap: edgeTypeMap,
            edgeTypes: edgeTypes,
            strictEdgeTypes: strictEdgeTypes,
            customExtractionInstructions: customExtractionInstructions,
            timeline: timeline,
            cancellationToken: cancellationToken));
}
