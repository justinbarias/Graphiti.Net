// Ported from tests/utils/maintenance/test_configurable_prompts.py (port/RULES.md, R25): the tests whose code under test
// is graphiti_core/utils/maintenance/edge_operations.py. The node tests of the same file belong to the unit of
// node_operations (packet P7b), and the other tests to the units of combined_extraction, community_operations,
// bulk_utils, and graphiti. The class is partial, so that each unit adds its tests to the class of the Python file.
//
// TODO(port): O10. test_resolve_extracted_edges_forwards_clients_only_for_prompt_routing patches
// `edge_ops._dedupe_extracted_edge` and asserts that its keyword `clients` is absent with no prompt library, which has no
// C# form. Covered by async case 16 of values/edge_operations.json (resolve_extracted_edges with prompt routing, scenario
// resolve_extracted_edges_override_routing of EdgeOperationsValuesTests) and async case 13 (the same function with no
// routing): each records the calls of the model client in order.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Prompts;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;

public partial class TestConfigurablePrompts
{
    // Employment edge attributes.
    private static readonly GraphTypeDescriptor EmploymentEdge = new(
        "_EmploymentEdge",
        "Employment edge attributes.",
        [new GraphFieldDescriptor("role", GraphFieldKind.String, false, null, false, null)]);

    [Fact(Timeout = 5000)]
    public async Task test_extract_edges_uses_clients_prompt_library()
    {
        await ExtractEdgesUsesClientsPromptLibrary(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task test_extract_edges_custom_prompt_uses_existing_response_model()
    {
        await ExtractEdgesUsesClientsPromptLibrary(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task test_extract_edges_preserves_prompt_name_with_custom_prompt()
    {
        await ExtractEdgesUsesClientsPromptLibrary(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task test_resolve_edges_uses_injected_prompt_library()
    {
        await ResolveEdgesUsesInjectedPromptLibrary(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task test_dedupe_edges_preserves_prompt_name_with_custom_prompt()
    {
        await ResolveEdgesUsesInjectedPromptLibrary(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public async Task test_extract_edge_timestamps_use_injected_prompt_library()
    {
        const string marker = "CUSTOM_TIMESTAMPS";
        var lib = Lib.CreatePromptLibrary(new() { ["extract_edges"] = new() { ["extract_timestamps"] = EdgeMarkerPrompt(marker) } });
        var llmClient = new EdgeTestLlm(new() { ["valid_at"] = null, ["invalid_at"] = null });
        var edge = new EntityEdge
        {
            SourceNodeUuid = "a",
            TargetNodeUuid = "b",
            Name = "WORKS_AT",
            Fact = "Alice works at Acme",
            GroupId = "group",
            Episodes = [],
            CreatedAt = DatetimeUtils.UtcNow(),
            ValidAt = null,
            InvalidAt = null,
        }.Validate();
        var clients = EdgeMakeClients(lib);
        clients.LlmClient = llmClient;
        await LoopGuard.RunAsync(() => EdgeOperations.ExtractEdgeTimestampsAsync(
            clients.LlmClient, edge, EdgeMakeEpisode(), clients: clients, cancellationToken: TestContext.Current.CancellationToken));

        var (messages, promptName, _) = llmClient.Calls[^1];
        Assert.StartsWith(marker, messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("extract_edges.extract_timestamps", promptName);
    }

    [Fact(Timeout = 5000)]
    public async Task test_extract_edge_attributes_use_injected_prompt_library()
    {
        const string marker = "CUSTOM_EDGE_ATTRS";
        var lib = Lib.CreatePromptLibrary(new() { ["extract_edges"] = new() { ["extract_attributes"] = EdgeMarkerPrompt(marker) } });
        var clients = EdgeMakeClients(lib);
        var llmClient = new EdgeTestLlm(new() { ["role"] = "engineer" });
        clients.LlmClient = llmClient;
        var extracted = new EntityEdge
        {
            SourceNodeUuid = "a",
            TargetNodeUuid = "b",
            Name = "EMPLOYMENT",
            Fact = "Alice works at Acme",
            GroupId = "group",
            Episodes = [],
            CreatedAt = DatetimeUtils.UtcNow(),
            ValidAt = PyDateTime.UtcNow(TimeProvider.System),
        }.Validate();
        await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgeAsync(
            clients.LlmClient,
            extracted,
            relatedEdges: [],
            existingEdges: [],
            episode: EdgeMakeEpisode(),
            edgeTypeCandidates: new() { ["EMPLOYMENT"] = EmploymentEdge },
            clients: clients,
            cancellationToken: TestContext.Current.CancellationToken));

        var (messages, promptName, _) = llmClient.Calls[^1];
        Assert.StartsWith(marker, messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("extract_edges.extract_attributes", promptName);
    }

    private static ChatPromptFunction EdgeMarkerPrompt(string marker) => _ => new ChatPrompt
    {
        System = new SystemMessage { Content = marker }.Validate(),
        User = new UserMessage { Content = marker }.Validate(),
    }.Validate();

    private static GraphitiClients EdgeMakeClients(IPromptLibrary? customLibrary = null) => new()
    {
        Driver = new EdgeTestSearchDriver(),
        Embedder = new EdgeTestEmbedder(),
        CrossEncoder = new EdgeOperationsRun.FakeCrossEncoder(),
        LlmClient = new EdgeTestLlm(new()
        {
            ["extracted_entities"] = new List<object?>(),
            ["edges"] = new List<object?>(),
            ["entity_resolutions"] = new List<object?>(),
        }),
        Tracer = new NoOpTracer(),
        PromptLibrary = customLibrary,
    };

    private static EpisodicNode EdgeMakeEpisode(string content = "Alice works at Acme") => new EpisodicNode
    {
        Name = "ep",
        GroupId = "group",
        Source = EpisodeType.Message,
        SourceDescription = "test",
        Content = content,
        ValidAt = DatetimeUtils.UtcNow(),
    }.Validate();

    private static async Task ExtractEdgesUsesClientsPromptLibrary(CancellationToken cancellationToken)
    {
        const string marker = "CUSTOM_EXTRACT_EDGES";
        var lib = Lib.CreatePromptLibrary(new() { ["extract_edges"] = new() { ["edge"] = EdgeMarkerPrompt(marker) } });
        var clients = EdgeMakeClients(lib);
        var llmClient = new EdgeTestLlm(new() { ["edges"] = new List<object?>() });
        clients.LlmClient = llmClient;
        List<EntityNode> nodes = [new EntityNode { Name = "Alice", GroupId = "group", Labels = ["Entity"] }.Validate()];

        await LoopGuard.RunAsync(() => EdgeOperations.ExtractEdgesAsync(
            clients,
            new EpisodeOrEpisodes.One { Episode = EdgeMakeEpisode() },
            nodes,
            previousEpisodes: [],
            edgeTypeMap: new() { [("Entity", "Entity")] = [] },
            cancellationToken: cancellationToken));

        var (messages, promptName, responseModel) = llmClient.Calls[^1];
        Assert.StartsWith(marker, messages[0].Content, StringComparison.Ordinal);
        Assert.Equal("extract_edges.edge", promptName);
        Assert.Same(ExtractedEdges.ResponseModel, responseModel);
    }

    private static async Task ResolveEdgesUsesInjectedPromptLibrary(CancellationToken cancellationToken)
    {
        const string marker = "CUSTOM_RESOLVE_EDGE";
        var lib = Lib.CreatePromptLibrary(new() { ["dedupe_edges"] = new() { ["resolve_edge"] = EdgeMarkerPrompt(marker) } });
        var clients = EdgeMakeClients(lib);
        var llmClient = new EdgeTestLlm(new() { ["duplicate_facts"] = new List<object?>(), ["contradicted_facts"] = new List<object?>() });
        clients.LlmClient = llmClient;
        var extracted = new EntityEdge
        {
            SourceNodeUuid = "a",
            TargetNodeUuid = "b",
            Name = "WORKS_AT",
            Fact = "Alice works at Acme",
            GroupId = "group",
            Episodes = [],
            CreatedAt = DatetimeUtils.UtcNow(),
        }.Validate();
        List<EntityEdge> related =
        [
            new EntityEdge
            {
                SourceNodeUuid = "a",
                TargetNodeUuid = "b",
                Name = "WORKS_AT",
                Fact = "Alice is employed by Acme",
                GroupId = "group",
                Episodes = [],
                CreatedAt = DatetimeUtils.UtcNow(),
            }.Validate(),
        ];

        // Pre-set timestamps so resolve does not make a follow-up timestamp LLM call.
        extracted.ValidAt = DatetimeUtils.UtcNow();
        await LoopGuard.RunAsync(() => EdgeOperations.ResolveExtractedEdgeAsync(
            clients.LlmClient, extracted, related, [], EdgeMakeEpisode(), clients: clients, cancellationToken: cancellationToken));

        var resolveCalls = llmClient.Calls.Where(c => c.PromptName == "dedupe_edges.resolve_edge").ToList();
        Assert.NotEmpty(resolveCalls);
        Assert.StartsWith(marker, resolveCalls[0].Messages[0].Content, StringComparison.Ordinal);
    }
}
