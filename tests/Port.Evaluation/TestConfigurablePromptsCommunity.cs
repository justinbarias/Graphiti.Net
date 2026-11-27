// Ported from tests/utils/maintenance/test_configurable_prompts.py (port/RULES.md, R25). The tests whose code under test is
// graphiti_core/utils/maintenance/combined_extraction.py or community_operations.py (packet P7d). The node tests are in
// TestConfigurablePromptsNodeOperations, the edge tests in TestConfigurablePromptsEdges, the bulk tests wait for packet
// P7e, and the tests of add_episode_bulk and of sagas wait for graphiti.py (layer 9).
//
// `MagicMock()` with an AsyncMock `generate_response` is the MockLlm of NodeOperationsTestSupport: it records each call
// and gives the reply. The driver and the embedder are fakes at the network boundary.
//
// TODO(port): O10, test_build_communities_uses_configured_prompt_library. The test replaces get_community_clusters and
// build_community with `monkeypatch.setattr` (`AsyncMock`), and then it reads `build_community.await_args`: the first
// argument must be `clients.llm_client`, and the keyword `clients` must be `clients`. Neither argument reaches a call of a
// fake at the network boundary unchanged: the real build_community turns `clients` into the prompt of a pair call, and the
// first patched result, `[[]]` (an empty cluster), makes the real build_community raise IndexError. So the test has no
// form under the reading of O10.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Prompts;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using static Graphiti.Port.Evaluation.NodeOperationsTestSupport;

public class TestConfigurablePromptsCommunity
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

    [Fact(Timeout = 5000)]
    public async Task test_combined_extraction_uses_clients_prompt_library()
    {
        var marker = "CUSTOM_COMBINED";
        var lib = Library("extract_nodes_and_edges", "extract_message", marker);
        var llm = Llm();
        llm.ReturnValue = new OrderedDictionary<string, object?>
        {
            ["extracted_entities"] = new List<object?>(),
            ["edges"] = new List<object?>(),
        };
        var clients = MakeClients(llm, promptLibrary: lib);

        await LoopGuard.RunAsync(() => CombinedExtractionModule.ExtractNodesAndEdgesAsync(
            clients,
            new EpisodeOrEpisodes.One { Episode = MakeEpisode() },
            [],
            cancellationToken: TestContext.Current.CancellationToken));

        var call = llm.Calls[^1];
        Assert.StartsWith(marker, call.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("extract_nodes_and_edges.extract_message", call.PromptName);
    }

    [Fact(Timeout = 5000)]
    public async Task test_combined_batch_timestamps_use_clients_prompt_library()
    {
        var marker = "CUSTOM_BATCH_TS";
        var lib = Lib.CreatePromptLibrary(new OrderedDictionary<string, OrderedDictionary<string, ChatPromptFunction>>
        {
            ["extract_nodes_and_edges"] = new() { ["extract_message"] = MarkerPrompt("x") },
            ["extract_edges"] = new() { ["extract_timestamps_batch"] = MarkerPrompt(marker) },
        });
        var llm = Llm();

        // PORT: `fake_generate` reads the keyword prompt_name of the call. The handler of MockLlm reads the messages, and
        // the system message of the main call starts with the marker 'x'.
        llm.Handler = messages => messages[0].Content.StartsWith('x')
            ? new OrderedDictionary<string, object?>
            {
                ["extracted_entities"] = new List<object?> { Item(("name", "Alice"), ("entity_type_id", 0L)) },
                ["edges"] = new List<object?>
                {
                    Item(
                        ("source_entity_name", "Alice"),
                        ("target_entity_name", "Alice"),
                        ("relation_type", "KNOWS"),
                        ("fact", "Alice knows Alice"),
                        ("episode_indices", new List<object?> { 0L })),
                },
            }
            : new OrderedDictionary<string, object?>
            {
                ["timestamps"] = new List<object?> { Item(("valid_at", null), ("invalid_at", null)) },
            };
        var clients = MakeClients(llm, promptLibrary: lib);

        await LoopGuard.RunAsync(() => CombinedExtractionModule.ExtractNodesAndEdgesAsync(
            clients,
            new EpisodeOrEpisodes.One { Episode = MakeEpisode() },
            [],
            cancellationToken: TestContext.Current.CancellationToken));

        var tsCalls = llm.Calls.Where(c => c.PromptName == "extract_edges.extract_timestamps_batch").ToList();
        Assert.NotEmpty(tsCalls);
        Assert.StartsWith(marker, tsCalls[0].Messages[0].Content, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task test_community_summarize_pair_uses_configured_prompt_library()
    {
        var marker = "CUSTOM_SUMMARIZE_PAIR";
        var lib = Library("summarize_nodes", "summarize_pair", marker);
        var llm = Llm();
        llm.ReturnValue = new OrderedDictionary<string, object?> { ["summary"] = "combined" };
        var clients = MakeClients(llm, promptLibrary: lib);

        await LoopGuard.RunAsync(() => CommunityOperations.SummarizePairAsync(
            llm,
            ("a", "b"),
            clients: clients,
            cancellationToken: TestContext.Current.CancellationToken));

        var call = llm.Calls[^1];
        Assert.StartsWith(marker, call.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("summarize_nodes.summarize_pair", call.PromptName);
    }

    [Fact(Timeout = 5000)]
    public async Task test_community_summary_description_uses_configured_prompt_library()
    {
        var marker = "CUSTOM_SUMMARY_DESC";
        var lib = Library("summarize_nodes", "summary_description", marker);
        var llm = Llm();
        llm.ReturnValue = new OrderedDictionary<string, object?> { ["description"] = "name" };
        var clients = MakeClients(llm, promptLibrary: lib);

        await LoopGuard.RunAsync(() => CommunityOperations.GenerateSummaryDescriptionAsync(
            llm,
            "summary",
            clients: clients,
            cancellationToken: TestContext.Current.CancellationToken));

        var call = llm.Calls[^1];
        Assert.StartsWith(marker, call.Messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("summarize_nodes.summary_description", call.PromptName);
    }

    [Fact(Timeout = 5000)]
    public async Task test_update_community_uses_configured_prompt_library()
    {
        // PORT: monkeypatch replaces determine_entity_community with an AsyncMock that returns (community, False), and
        // generate_summary_description with an AsyncMock that returns 'new-name' (O10). The real functions run. The
        // graph operations interface of the driver gives the result of the first patch: its determine_entity_community
        // returns the community. The LLM fake gives the result of the second patch: its reply holds 'new-name' as the
        // description, and the summary 'new' for the pair call. The test reads no argument of a patch. Upstream reads
        // the only LLM call, the pair call, and the real run makes a second call for the description, so the test reads
        // the pair call by its prompt name.
        var marker = "CUSTOM_UPDATE_COMMUNITY";
        var lib = Library("summarize_nodes", "summarize_pair", marker);
        var community = new CommunityNode
        {
            Name = "old",
            GroupId = "group",
            Labels = ["Community"],
            Summary = "old",
        }.Validate();
        var driver = new MockGraphDriver(ops: new CommunityOps(community));
        var llm = Llm();
        llm.ReturnValue = new OrderedDictionary<string, object?> { ["summary"] = "new", ["description"] = "new-name" };
        var clients = MakeClients(llm, driver: driver, promptLibrary: lib);

        var entity = Node("Alice", summary: "entity");
        await LoopGuard.RunAsync(() => CommunityOperations.UpdateCommunityAsync(
            clients.Driver,
            clients.LlmClient,
            clients.Embedder,
            entity,
            clients: clients,
            cancellationToken: TestContext.Current.CancellationToken));

        var call = llm.Calls.Single(c => c.PromptName == "summarize_nodes.summarize_pair");
        Assert.StartsWith(marker, call.Messages[0].Content, StringComparison.Ordinal);
    }

    [Fact(Timeout = 5000)]
    public async Task test_community_operations_use_configured_prompt_library()
    {
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        await test_community_summarize_pair_uses_configured_prompt_library();
    }

    // The graph operations interface of the fake driver: determine_entity_community gives the community, and each other
    // method is not implemented, so the node save falls back to the query of the driver.
    private sealed record CommunityOps(CommunityNode Community) : GraphOperationsInterface
    {
        internal override async Task<(CommunityNode?, bool)> DetermineEntityCommunityAsync(GraphDriver driver, EntityNode entity, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return (Community, false);
        }
    }
}
