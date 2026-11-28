// Port of .upstream/graphiti/tests/test_saga_summary_budget.py (port/RULES.md, R25).
// PORT: `monkeypatch.setattr('graphiti_core.graphiti.SAGA_SUMMARY_MAX_EPISODE_CHARS', ...)` and the same for
// SAGA_SUMMARY_MAX_PROMPT_BYTES are `using var _ = GraphitiModule.<name>.Use(...)` (R17). `Graphiti.__new__(Graphiti)`
// is RuntimeHelpers.GetUninitializedObject. `SimpleNamespace(graph_operations_interface=operations)` is a GraphDriver at
// the network boundary that carries the operations and throws for each other call. `GraphitiClients.model_construct`
// with SimpleNamespace clients is a GraphitiClients with clients that throw when a test calls them.
namespace Graphiti.Port.Evaluation;

using System.Runtime.CompilerServices;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;

/// <summary>A driver that only carries a graph operations interface.</summary>
internal sealed class OperationsOnlyDriver : GraphDriver
{
    internal OperationsOnlyDriver(GraphOperationsInterface operations)
    {
        GraphOperationsInterface = operations;
    }

    internal override string PythonName => "SimpleNamespace";

    internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
        string cypherQuery, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    internal override GraphDriverSession Session(string? database = null) => throw new NotImplementedException();

    internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}

internal sealed record SagaBudgetFakeGraphOperations : GraphOperationsInterface
{
    internal SagaBudgetFakeGraphOperations(List<(string Content, PyDateTime? ValidAt, PyDateTime CreatedAt)>? episodes = null)
    {
        Episodes = episodes ??
        [
            (new string('a', 40), TestSagaSummaryBudget.Dt(1), TestSagaSummaryBudget.Dt(11)),
            (new string('b', 40), TestSagaSummaryBudget.Dt(2), TestSagaSummaryBudget.Dt(12)),
            (new string('c', 40), TestSagaSummaryBudget.Dt(3), TestSagaSummaryBudget.Dt(13)),
        ];
    }

    internal List<(string Content, PyDateTime? ValidAt, PyDateTime CreatedAt)> Episodes { get; }

    internal SagaNode? SavedSaga { get; private set; }

    internal override Task<SagaNode> SagaNodeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
        Task.FromResult(new SagaNode { Uuid = uuid, Name = "test saga", GroupId = "graph-1", CreatedAt = TestSagaSummaryBudget.Dt(0) });

    internal override Task<List<(string Content, PyDateTime? ValidAt, PyDateTime CreatedAt)>> SagaGetEpisodeContentsAsync(
        GraphDriver driver,
        string sagaUuid,
        PyDateTime? since = null,
        long limit = 200,
        CancellationToken cancellationToken = default)
    {
        Assert.Null(since);
        Assert.Equal(GraphitiModule.MaxSagaEpisodesForSummary, limit);
        return Task.FromResult(Episodes);
    }

    internal override Task SagaNodeSaveAsync(SagaNode node, GraphDriver driver, CancellationToken cancellationToken = default)
    {
        SavedSaga = node;
        return Task.CompletedTask;
    }
}

internal class SagaBudgetFakeLlmClient() : LLMClient(null, false)
{
    internal List<Message>? Messages { get; private set; }

    internal long? SeenMaxTokens { get; private set; }

    internal override string PythonName => "_FakeLLMClient";

    internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();

    internal override Task<OrderedDictionary<string, object?>> GenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        string? groupId = null,
        string? promptName = null,
        bool attributeExtraction = false,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        Assert.Equal("summarize_sagas.summarize_saga", promptName);
        Messages = messages;
        SeenMaxTokens = maxTokens;
        return Task.FromResult(new OrderedDictionary<string, object?> { ["summary"] = "updated summary" });
    }
}

internal sealed class SagaBudgetLongSummaryLlmClient : SagaBudgetFakeLlmClient
{
    internal override async Task<OrderedDictionary<string, object?>> GenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long? maxTokens = null,
        ModelSize modelSize = ModelSize.Medium,
        string? groupId = null,
        string? promptName = null,
        bool attributeExtraction = false,
        string? model = null,
        CancellationToken cancellationToken = default)
    {
        await base.GenerateResponseAsync(messages, responseModel: responseModel, promptName: promptName, maxTokens: maxTokens, cancellationToken: cancellationToken);
        return new OrderedDictionary<string, object?>
        {
            ["summary"] = "Kept sentence. " + new string('x', (int)(TextUtils.SagaSummaryMaxChars + 50)) + " Dropped.",
        };
    }
}

public class TestSagaSummaryBudget
{
    [Fact]
    public void test_saga_summary_budget_constants()
    {
        Assert.Equal(20, GraphitiModule.MaxSagaEpisodesForSummary);
        Assert.Equal(100_000, GraphitiModule.SagaSummaryMaxEpisodeChars.Value);
        Assert.Equal(8192, GraphitiModule.SagaSummaryMaxTokens);
        Assert.Equal(4000, TextUtils.SagaSummaryMaxChars);
    }

    [Fact]
    public void test_saga_summary_episode_selection_respects_byte_budget()
    {
        List<(string, PyDateTime?, PyDateTime)> episodes =
        [
            (new string('a', 40), null, Dt(1)),
            (new string('b', 40), null, Dt(2)),
            (new string('c', 40), null, Dt(3)),
        ];

        var (selected, selectedBytes, dropped) = GraphitiModule.SelectSagaSummaryEpisodes(episodes, maxEpisodeChars: 90);

        Assert.Equal([new string('a', 40), new string('b', 40)], selected.Select(s => s.Item1));
        Assert.Equal([Dt(1), Dt(2)], selected.Select(s => s.Item3));
        Assert.Equal(80, selectedBytes);
        Assert.True(dropped);
    }

    [Fact]
    public void test_saga_summary_episode_selection_counts_characters_not_utf8_bytes()
    {
        List<(string, PyDateTime?, PyDateTime)> episodes =
        [
            (new string('é', 10), null, Dt(1)),
            (new string('a', 10), null, Dt(2)),
        ];

        var (selected, selectedBytes, dropped) = GraphitiModule.SelectSagaSummaryEpisodes(episodes, maxEpisodeChars: 15);

        Assert.Equal([new string('é', 10)], selected.Select(s => s.Item1));
        Assert.Equal(PyText.EncodeUtf8(new string('é', 10)).Length, selectedBytes);
        Assert.True(dropped);
    }

    [Fact]
    public void test_saga_summary_episode_selection_truncates_first_oversized_episode()
    {
        var (selected, selectedBytes, dropped) = GraphitiModule.SelectSagaSummaryEpisodes(
            [(new string('é', 10), null, Dt(1))], maxEpisodeChars: 7);

        Assert.Equal([new string('é', 7)], selected.Select(s => s.Item1));
        Assert.Equal(PyText.EncodeUtf8(new string('é', 7)).Length, selectedBytes);
        Assert.True(dropped);
    }

    [Fact(Timeout = 5000)]
    public async Task test_saga_summary_cursor_advances_only_to_latest_selected_episode()
    {
        using var patch = GraphitiModule.SagaSummaryMaxEpisodeChars.Use(90);
        var operations = new SagaBudgetFakeGraphOperations();
        var llm = new SagaBudgetFakeLlmClient();
        var graphiti = MakeGraphiti(operations, llm);

        var saga = await LoopGuard.RunAsync(() => graphiti.SummarizeSagaAsync("saga-1", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("updated summary", saga.Summary);
        Assert.Equal(Dt(12), saga.LastSummarizedAt);
        Assert.Equal(Dt(2), saga.LastSummarizedEpisodeValidAt);
        Assert.Equal(3, saga.SummaryEpisodesFetched);
        Assert.Equal(2, saga.SummaryEpisodesSelected);
        Assert.Equal(1, saga.SummaryEpisodesSkipped);
        Assert.Equal(80, saga.SummarySelectedEpisodeBytes);
        Assert.Same(saga, operations.SavedSaga);
        Assert.Equal(GraphitiModule.SagaSummaryMaxTokens, llm.SeenMaxTokens);
    }

    /// <summary>
    /// A 20-episode fetch that only fits five episodes must advance
    /// last_summarized_at to the fifth episode's created_at so the rest remain
    /// eligible on the next run.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task test_saga_summary_watermark_advances_for_partial_fetch_window()
    {
        using var patch = GraphitiModule.SagaSummaryMaxEpisodeChars.Use(5_000);
        List<(string, PyDateTime?, PyDateTime)> episodes = [.. Enumerable.Range(1, 20).Select(i => (new string('e', 1_000), (PyDateTime?)Dt(i), Dt(10 + i)))];
        var operations = new SagaBudgetFakeGraphOperations(episodes: episodes);
        var llm = new SagaBudgetFakeLlmClient();
        var graphiti = MakeGraphiti(operations, llm);

        var saga = await LoopGuard.RunAsync(() => graphiti.SummarizeSagaAsync("saga-1", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(20, saga.SummaryEpisodesFetched);
        Assert.Equal(5, saga.SummaryEpisodesSelected);
        Assert.Equal(15, saga.SummaryEpisodesSkipped);
        Assert.Equal(Dt(15), saga.LastSummarizedAt);
        Assert.Equal(Dt(5), saga.LastSummarizedEpisodeValidAt);
    }

    [Fact(Timeout = 5000)]
    public async Task test_saga_summary_truncates_stored_text_to_8k_chars()
    {
        var operations = new SagaBudgetFakeGraphOperations();
        var llm = new SagaBudgetLongSummaryLlmClient();
        var graphiti = MakeGraphiti(operations, llm);

        var saga = await LoopGuard.RunAsync(() => graphiti.SummarizeSagaAsync("saga-1", cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(Py.Len(saga.Summary) <= TextUtils.SagaSummaryMaxChars);
        Assert.StartsWith("Kept sentence.", saga.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("Dropped.", saga.Summary, StringComparison.Ordinal);
        Assert.Equal(GraphitiModule.SagaSummaryMaxTokens, llm.SeenMaxTokens);
    }

    [Fact(Timeout = 5000)]
    public async Task test_saga_summary_cursor_follows_created_at_not_valid_at()
    {
        using var patch = GraphitiModule.SagaSummaryMaxEpisodeChars.Use(50);
        var operations = new SagaBudgetFakeGraphOperations(
            episodes:
            [
                ("regular earlier ingestion", Dt(20), Dt(9)),
                ("backfill newer ingestion but older valid_at", Dt(1), Dt(10)),
            ]);
        var graphiti = MakeGraphiti(operations, new SagaBudgetFakeLlmClient());

        var saga = await LoopGuard.RunAsync(() => graphiti.SummarizeSagaAsync("saga-1", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("updated summary", saga.Summary);
        Assert.Equal(Dt(9), saga.LastSummarizedAt);
        Assert.Equal(Dt(20), saga.LastSummarizedEpisodeValidAt);
        Assert.Same(saga, operations.SavedSaga);
    }

    [Fact(Timeout = 5000)]
    public async Task test_saga_summary_prompt_orders_selected_episodes_by_valid_at()
    {
        using var patch = GraphitiModule.SagaSummaryMaxEpisodeChars.Use(200);
        var operations = new SagaBudgetFakeGraphOperations(
            episodes:
            [
                ("regular earlier ingestion", Dt(20), Dt(9)),
                ("backfill newer ingestion but older valid_at", Dt(1), Dt(10)),
            ]);
        var graphiti = MakeGraphiti(operations, new SagaBudgetFakeLlmClient());

        await LoopGuard.RunAsync(() => graphiti.SummarizeSagaAsync("saga-1", cancellationToken: TestContext.Current.CancellationToken));

        var userPrompt = ((SagaBudgetFakeLlmClient)graphiti.LlmClient).Messages![1].Content;
        Assert.True(
            userPrompt.IndexOf("backfill newer ingestion", StringComparison.Ordinal)
            < userPrompt.IndexOf("regular earlier ingestion", StringComparison.Ordinal));
    }

    [Fact(Timeout = 5000)]
    public async Task test_saga_summary_truncates_oversized_first_episode_and_advances_cursor()
    {
        using var patch = GraphitiModule.SagaSummaryMaxEpisodeChars.Use(10);
        var operations = new SagaBudgetFakeGraphOperations();
        var llm = new SagaBudgetFakeLlmClient();
        var graphiti = MakeGraphiti(operations, llm);

        var saga = await LoopGuard.RunAsync(() => graphiti.SummarizeSagaAsync("saga-1", cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("updated summary", saga.Summary);
        Assert.Equal(Dt(11), saga.LastSummarizedAt);
        Assert.Equal(3, saga.SummaryEpisodesFetched);
        Assert.Equal(1, saga.SummaryEpisodesSelected);
        Assert.Equal(2, saga.SummaryEpisodesSkipped);
        Assert.Equal(10, saga.SummarySelectedEpisodeBytes);
        Assert.Equal(GraphitiModule.SagaSummaryMaxTokens, llm.SeenMaxTokens);
        Assert.Same(saga, operations.SavedSaga);
    }

    [Fact(Timeout = 5000)]
    public async Task test_saga_summary_prompt_stays_under_context_cap()
    {
        using var promptBytes = GraphitiModule.SagaSummaryMaxPromptBytes.Use(12_000);
        using var episodeChars = GraphitiModule.SagaSummaryMaxEpisodeChars.Use(12_000);
        var operations = new SagaBudgetFakeGraphOperations(
            episodes: [.. Enumerable.Range(1, 7).Select(i => (new string('x', 4_000), (PyDateTime?)Dt(i), Dt(i)))]);
        var llm = new SagaBudgetFakeLlmClient();
        var graphiti = MakeGraphiti(operations, llm);

        var saga = await LoopGuard.RunAsync(() => graphiti.SummarizeSagaAsync("saga-1", cancellationToken: TestContext.Current.CancellationToken));

        var promptBytesSum = llm.Messages!.Sum(message => (long)PyText.EncodeUtf8(message.Content).Length);
        Assert.True(promptBytesSum <= 12_000);
        Assert.True(saga.SummaryEpisodesSelected < 7);
        Assert.True(saga.SummaryEpisodesSkipped > 0);
        Assert.Equal(Dt((int)saga.SummaryEpisodesSelected), saga.LastSummarizedAt);
        Assert.Equal(GraphitiModule.SagaSummaryMaxTokens, llm.SeenMaxTokens);
    }

    [Fact(Timeout = 5000)]
    public async Task test_saga_summary_budget_uses_routed_prompt()
    {
        using var patch = GraphitiModule.SagaSummaryMaxPromptBytes.Use(25_000);
        List<(string, PyDateTime?, PyDateTime)> episodes = [.. Enumerable.Range(1, 5).Select(index => (new string('x', 4_000), (PyDateTime?)Dt(index), Dt(index)))];

        var legacyLlm = new SagaBudgetFakeLlmClient();
        var legacyGraphiti = (GraphitiCore)RuntimeHelpers.GetUninitializedObject(typeof(GraphitiCore));
        legacyGraphiti.Driver = new OperationsOnlyDriver(new SagaBudgetFakeGraphOperations(episodes: episodes));
        legacyGraphiti.LlmClient = legacyLlm;
        var legacySaga = await LoopGuard.RunAsync(() => legacyGraphiti.SummarizeSagaAsync("saga-1", cancellationToken: TestContext.Current.CancellationToken));

        static ChatPrompt PaddedPrompt(OrderedDictionary<string, object?> context) => new()
        {
            System = new SystemMessage { Content = new string('P', 14_000) },
            User = new UserMessage { Content = string.Join("\n---\n", ((List<object?>)context["episodes"]!).Cast<string>()) },
        };

        var library = Lib.CreatePromptLibrary(new() { ["summarize_sagas"] = new() { ["summarize_saga"] = PaddedPrompt } });
        var routedLlm = new SagaBudgetFakeLlmClient();
        var routedGraphiti = (GraphitiCore)RuntimeHelpers.GetUninitializedObject(typeof(GraphitiCore));
        var routedDriver = new OperationsOnlyDriver(new SagaBudgetFakeGraphOperations(episodes: episodes));
        routedGraphiti.Driver = routedDriver;
        routedGraphiti.LlmClient = routedLlm;

        // PORT: `GraphitiClients.model_construct(llm_client=..., prompt_library=...)` leaves the other fields unset. The
        // port gives the required fields: the driver of the object, and clients that throw when a test calls them.
        routedGraphiti.Clients = new GraphitiClients
        {
            Driver = routedDriver,
            LlmClient = routedLlm,
            Embedder = new UnusedEmbedder(),
            CrossEncoder = new UnusedCrossEncoder(),
            Tracer = new NoOpTracer(),
            PromptLibrary = library,
        };
        var routedSaga = await LoopGuard.RunAsync(() => routedGraphiti.SummarizeSagaAsync("saga-1", cancellationToken: TestContext.Current.CancellationToken));

        Assert.True(routedSaga.SummaryEpisodesSelected < legacySaga.SummaryEpisodesSelected);
        foreach (var llm in new[] { legacyLlm, routedLlm })
        {
            var promptBytes = llm.Messages!.Sum(message => (long)PyText.EncodeUtf8(message.Content).Length);
            Assert.True(promptBytes <= 25_000);
        }

        Assert.StartsWith(new string('P', 100), routedLlm.Messages![0].Content, StringComparison.Ordinal);
    }

    internal static PyDateTime Dt(int minute) => new(2026, 1, 1, 0, minute, offsetMicroseconds: 0);

    private static GraphitiCore MakeGraphiti(SagaBudgetFakeGraphOperations operations, SagaBudgetFakeLlmClient llm)
    {
        var graphiti = (GraphitiCore)RuntimeHelpers.GetUninitializedObject(typeof(GraphitiCore));
        graphiti.Driver = new OperationsOnlyDriver(operations);
        graphiti.LlmClient = llm;
        graphiti.Clients = new GraphitiClients
        {
            Driver = graphiti.Driver,
            LlmClient = llm,
            Embedder = new UnusedEmbedder(),
            CrossEncoder = new UnusedCrossEncoder(),
            Tracer = new NoOpTracer(),
        };
        return graphiti;
    }
}
