// Ported from tests/utils/maintenance/test_configurable_prompts.py (port/RULES.md, R25). Only the node tests, whose code
// under test is graphiti_core/utils/maintenance/node_operations.py. The edge tests wait for edge_operations.py (packet
// P7c), the tests of combined extraction and communities for packet P7d, the bulk tests for packet P7e, and the tests of
// add_episode_bulk and of sagas for graphiti.py (layer 9).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Prompts;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using static Graphiti.Port.Evaluation.NodeOperationsTestSupport;

public class TestConfigurablePromptsNodeOperations
{
    private static ChatPromptFunction MarkerPrompt(string marker) =>
        _ => new ChatPrompt
        {
            System = new SystemMessage { Content = marker },
            User = new UserMessage { Content = marker },
        };

    private static IPromptLibrary Library(string group, string function, string marker) =>
        Lib.CreatePromptLibrary(new OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>>
        {
            [group] = new() { [function] = MarkerPrompt(marker) },
        });

    private static MockLlm Llm() => new(16384)
    {
        ReturnValue = new OrderedDictionary<string, object?>
        {
            ["extracted_entities"] = new List<object?>(),
            ["edges"] = new List<object?>(),
            ["entity_resolutions"] = new List<object?>(),
        },
    };

    private static EpisodicNode MakeEpisode(string content = "Alice works at Acme") => NodeOperationsTestSupport.MakeEpisode(content);

    [Fact(Timeout = 5000)]
    public async Task test_extract_nodes_uses_clients_prompt_library()
    {
        var marker = "CUSTOM_EXTRACT_NODES";
        var llm = Llm();
        llm.ReturnValue = Reply("extracted_entities", Item(("name", "Alice"), ("entity_type_id", 0L)));
        var clients = MakeClients(llm, promptLibrary: Library("extract_nodes", "extract_message", marker));

        await LoopGuard.RunAsync(() => NodeOperations.ExtractNodesAsync(
            clients,
            new EpisodeOrEpisodes.One { Episode = MakeEpisode() },
            [],
            cancellationToken: TestContext.Current.CancellationToken));

        var call = llm.Calls[^1];
        Assert.StartsWith(marker, call.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("extract_nodes.extract_message", call.PromptName);
        Assert.Same(ExtractedEntities.ResponseModel, call.ResponseModel);
    }

    [Fact(Timeout = 5000)]
    public async Task test_extract_nodes_custom_prompt_uses_existing_response_model()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        await test_extract_nodes_uses_clients_prompt_library();
    }

    [Fact(Timeout = 5000)]
    public async Task test_extract_nodes_preserves_prompt_name_with_custom_prompt()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        await test_extract_nodes_uses_clients_prompt_library();
    }

    [Fact(Timeout = 5000)]
    public async Task test_extract_node_attributes_use_injected_prompt_library()
    {
        // class Person(BaseModel): city: str = Field(default='')
        // PORT: a default other than None is outside the field forms of decision D26, so the test builds the descriptor
        // of a field that is not required. The test reads no default: the reply gives the field.
        var person = new GraphTypeDescriptor("Person", null, [new GraphFieldDescriptor("city", GraphFieldKind.String, false, null, false, null)]);
        var marker = "CUSTOM_NODE_ATTRS";
        var llm = Llm();
        llm.ReturnValue = new OrderedDictionary<string, object?> { ["city"] = "Seattle" };

        // PORT: monkeypatch replaces create_entity_node_embeddings with a mock that does nothing (O10). The real function
        // runs with the fake embedder.
        var clients = MakeClients(llm, promptLibrary: Library("extract_nodes", "extract_attributes", marker));

        static Task<bool> ShouldNotSummarize(EntityNode node) => Task.FromResult(false);

        await LoopGuard.RunAsync(() => NodeOperations.ExtractAttributesFromNodesAsync(
            clients,
            [Node("Alice", labels: ["Entity", "Person"])],
            episode: new EpisodeOrEpisodes.One { Episode = MakeEpisode() },
            entityTypes: new OrderedDictionary<string, GraphTypeDescriptor> { ["Person"] = person },
            shouldSummarizeNode: ShouldNotSummarize,
            cancellationToken: TestContext.Current.CancellationToken));

        var call = llm.Calls[^1];
        Assert.StartsWith(marker, call.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("extract_nodes.extract_attributes", call.PromptName);
        Assert.Same(person, call.ResponseModel);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_nodes_uses_clients_prompt_library()
    {
        // PORT: the test replaces `_collect_candidate_nodes` and `_resolve_with_similarity` with monkeypatch.setattr,
        // which has no C# form (O10). The candidates come from the search interface of the driver, which
        // `_semantic_candidate_search` reads first, and the driver gives no exact-name candidate. The candidate 'Alicia'
        // does not match 'Alice' by `_resolve_with_similarity` (a short name with no exact match), so the node stays
        // unresolved and reaches the LLM, as the patch makes it do.
        var marker = "CUSTOM_DEDUPE_NODES";
        var llm = Llm();
        llm.ReturnValue = Reply("entity_resolutions", Item(("id", 0L), ("name", "Alice"), ("duplicate_candidate_id", -1L)));
        var search = new MockSearch([[]]);
        var driver = new MockGraphDriver(search: search);
        var clients = MakeClients(llm, driver: driver, promptLibrary: Library("dedupe_nodes", "nodes", marker));

        List<EntityNode> extracted = [Node("Alice")];
        await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(clients, extracted, MakeEpisode(), cancellationToken: TestContext.Current.CancellationToken));

        // No LLM call when no candidates / all resolved without LLM; force unresolved path
        // by providing candidates that don't match via similarity
        search.Results[0] = [Node("Alicia")];

        await LoopGuard.RunAsync(() => NodeOperations.ResolveExtractedNodesAsync(clients, extracted, MakeEpisode(), cancellationToken: TestContext.Current.CancellationToken));
        var call = llm.Calls[^1];
        Assert.StartsWith(marker, call.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("dedupe_nodes.nodes", call.PromptName);
    }
}
