// The test doubles of the ported pytest tests of graphiti_core/utils/maintenance/node_operations.py. The Python tests use
// MagicMock and AsyncMock for the driver, the LLM client, and the embedder, which are the network boundary. These fakes
// record each call after a yield, so a call that the code does not await is not seen by an assertion (R25).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Core.Search;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;

internal static class NodeOperationsTestSupport
{
    internal static GraphitiClients MakeClients(
        MockLlm? llm = null,
        MockEmbedder? embedder = null,
        MockGraphDriver? driver = null,
        IPromptLibrary? promptLibrary = null,
        long? maxCoroutines = null) => new()
        {
            Driver = driver ?? new MockGraphDriver(),
            LlmClient = llm ?? new MockLlm(),
            Embedder = embedder ?? new MockEmbedder(),
            CrossEncoder = new UnusedCrossEncoder(),
            Tracer = new NoOpTracer(),
            PromptLibrary = promptLibrary,
            MaxCoroutines = maxCoroutines,
        };

    internal static EpisodicNode MakeEpisode(string content = "content", EpisodeType source = EpisodeType.Message, string groupId = "group") =>
        new EpisodicNode
        {
            Name = "episode",
            GroupId = groupId,
            Source = source,
            SourceDescription = "test",
            Content = content,
            ValidAt = DatetimeUtils.UtcNow(),
        }.Validate();

    internal static EntityNode Node(string name, string groupId = "group", List<string>? labels = null, string summary = "", OrderedDictionary<string, object?>? attributes = null) =>
        new EntityNode
        {
            Name = name,
            GroupId = groupId,
            Labels = labels ?? ["Entity"],
            Summary = summary,
            Attributes = attributes ?? [],
        }.Validate();

    internal static OrderedDictionary<string, object?> Reply(string key, params OrderedDictionary<string, object?>[] items) =>
        new() { [key] = items.Select(i => (object?)i).ToList() };

    internal static OrderedDictionary<string, object?> Item(params (string Key, object? Value)[] fields)
    {
        var item = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in fields)
        {
            item[key] = value;
        }

        return item;
    }

    /// <summary>The LLM client of the tests: it records each call and returns <see cref="ReturnValue"/> or the answer of <see cref="Handler"/>.</summary>
    internal sealed class MockLlm : LLMClient
    {
        internal MockLlm(long maxTokens = 32768)
            : base(new LLMConfig(maxTokens: maxTokens))
        {
        }

        internal OrderedDictionary<string, object?> ReturnValue { get; set; } = [];

        internal Func<List<Message>, OrderedDictionary<string, object?>>? Handler { get; set; }

        internal List<(List<Message> Messages, IResponseModel? ResponseModel, long? MaxTokens, string? PromptName)> Calls { get; } = [];

        internal override string PythonName => "MagicMock";

        internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
            List<Message> messages,
            IResponseModel? responseModel = null,
            long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
            ModelSize modelSize = ModelSize.Medium,
            string? model = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

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
            await Task.Yield();
            Calls.Add((messages, responseModel, maxTokens, promptName));
            return (OrderedDictionary<string, object?>)Py.DeepCopy(Handler is not null ? Handler(messages) : ReturnValue)!;
        }
    }

    /// <summary>The embedder of the tests. A null <see cref="Batch"/> is an embedder with no create_batch.</summary>
    internal sealed class MockEmbedder : EmbedderClient
    {
        internal Func<List<string>, List<List<double>>>? Batch { get; set; } = inputs => [.. inputs.Select(_ => new List<double> { 0.1, 0.2, 0.3 })];

        internal List<string> BatchInputs { get; } = [];

        internal override async Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return [0.1, 0.2, 0.3];
        }

        internal override async Task<List<List<double>>> CreateBatchAsync(List<string> inputDataList, CancellationToken cancellationToken = default)
        {
            if (Batch is null)
            {
                throw new NotImplementedException(string.Empty);
            }

            await Task.Yield();
            BatchInputs.AddRange(inputDataList);
            return Batch(inputDataList);
        }
    }

    /// <summary>The search interface of a driver: each call of node_similarity_search returns the next list.</summary>
    internal sealed record MockSearch(List<List<EntityNode>> Results) : SearchInterface
    {
        internal int Calls { get; private set; }

        internal override async Task<List<EntityNode>> NodeSimilaritySearchAsync(
            GraphDriver driver,
            List<double> searchVector,
            SearchFilters searchFilter,
            List<string>? groupIds = null,
            long limit = 100,
            double minScore = 0.7,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var result = Results[Math.Min(Calls, Results.Count - 1)];
            Calls++;
            return result;
        }
    }

    /// <summary>The graph operations interface of a driver, with the two lookups of node_operations.</summary>
    internal sealed record MockOps : GraphOperationsInterface
    {
        internal Func<List<string>, List<string>, List<EntityNode>>? GetByNames { get; init; }

        internal Func<string, OrderedDictionary<string, object?>, List<string>?, long, List<EntityNode>>? GetByAttributeValues { get; init; }

        internal List<(GraphDriver Driver, string GroupId, OrderedDictionary<string, object?> Attributes, List<string>? Labels, long Limit)> AttributeCalls { get; } = [];

        internal int NameCalls { get; private set; }

        internal override async Task<List<EntityNode>> NodeGetByNamesAsync(Type? cls, GraphDriver driver, List<string> names, List<string> groupIds, CancellationToken cancellationToken = default)
        {
            if (GetByNames is null)
            {
                throw new NotImplementedException(string.Empty);
            }

            await Task.Yield();
            NameCalls++;
            return GetByNames(names, groupIds);
        }

        internal override async Task<List<EntityNode>> NodeGetByAttributeValuesAsync(
            Type? cls,
            GraphDriver driver,
            string groupId,
            OrderedDictionary<string, object?> attributes,
            List<string>? nodeLabels = null,
            long limit = 100,
            CancellationToken cancellationToken = default)
        {
            if (GetByAttributeValues is null)
            {
                throw new NotImplementedException(string.Empty);
            }

            await Task.Yield();
            AttributeCalls.Add((driver, groupId, attributes, nodeLabels, limit));
            return GetByAttributeValues(groupId, attributes, nodeLabels, limit);
        }
    }

    /// <summary>The MagicMock driver of the Python tests: execute_query returns ([], None, None).</summary>
    internal sealed class MockGraphDriver : GraphDriver
    {
        internal MockGraphDriver(GraphProvider provider = GraphProvider.Neo4j, SearchInterface? search = null, GraphOperationsInterface? ops = null)
        {
            Provider = provider;
            SearchInterface = search;
            GraphOperationsInterface = ops;
        }

        internal int ExecuteQueryCalls { get; private set; }

        internal override string PythonName => "MagicMock";

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            ExecuteQueryCalls++;
            return ([], null, null);
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class UnusedCrossEncoder : CrossEncoderClient
    {
        internal override Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }
}
