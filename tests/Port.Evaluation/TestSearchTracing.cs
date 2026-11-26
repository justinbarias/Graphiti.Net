// Ported from tests/utils/search/test_search_tracing.py (port/RULES.md, R25).
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class TestSearchTracing
{
    [Fact]
    public async Task test_search_emits_trace_spans_for_edge_similarity()
    {
        var tracer = new RecordingTracer();
        var clients = new GraphitiClients
        {
            Driver = new FakeDriver(),
            LlmClient = null!,
            Embedder = new FakeEmbedder(),
            CrossEncoder = null!,
            Tracer = tracer,
        };

        var results = await Asyncio.Run(() => SearchModule.SearchAsync(
            clients,
            query: "where is the edge",
            groupIds: null,
            config: new SearchConfig
            {
                EdgeConfig = new EdgeSearchConfig { SearchMethods = [EdgeSearchMethod.CosineSimilarity] },
            },
            searchFilter: new SearchFilters(),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(results.Edges);
        Assert.Empty(results.Nodes);
        Assert.Empty(results.Episodes);
        Assert.Empty(results.Communities);

        var spanNames = tracer.Spans.Select(span => span.Name).ToList();
        Assert.Contains("search.embed_query_vector", spanNames);
        Assert.Contains("search.execute_scopes", spanNames);
        Assert.Contains("search.edge_search", spanNames);
        Assert.Contains("search.edge_search.execute_methods", spanNames);
        Assert.Contains("search.edge_search.rerank", spanNames);

        var executeScopesSpan = tracer.Spans.First(span => span.Name == "search.execute_scopes");
        Assert.Equal(true, executeScopesSpan.Attributes["scope.edges"]);
        Assert.Equal(0L, executeScopesSpan.Attributes["result.edges"]);

        var embedSpan = tracer.Spans.First(span => span.Name == "search.embed_query_vector");
        Assert.Equal(Py.Len("where is the edge"), embedSpan.Attributes["query.length"]);
        Assert.Equal(3L, embedSpan.Attributes["query_vector.dimension"]);
    }

    [Fact]
    public async Task test_search_uses_noop_tracer_when_client_has_no_tracer()
    {
        // PORT: `tracer` is a required field of GraphitiClients, and a SimpleNamespace with no `tracer` reads as None
        // through getattr. A null value is that None.
        var clients = new GraphitiClients
        {
            Driver = new FakeDriver(),
            LlmClient = null!,
            Embedder = new FakeEmbedder(),
            CrossEncoder = null!,
            Tracer = null!,
        };

        var results = await Asyncio.Run(() => SearchModule.SearchAsync(
            clients,
            query: "where is the edge",
            groupIds: null,
            config: new SearchConfig
            {
                EdgeConfig = new EdgeSearchConfig { SearchMethods = [EdgeSearchMethod.CosineSimilarity] },
            },
            searchFilter: new SearchFilters(),
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(results.Edges);
    }

    [Fact]
    public async Task test_edge_search_uses_noop_tracer_when_none_is_passed()
    {
        var (edges, scores) = await Asyncio.Run(() => SearchModule.EdgeSearchAsync(
            driver: new FakeDriver(),
            crossEncoder: null!,
            query: "where is the edge",
            queryVector: [0.1, 0.2, 0.3],
            groupIds: null,
            config: new EdgeSearchConfig { SearchMethods = [EdgeSearchMethod.CosineSimilarity] },
            searchFilter: new SearchFilters(),
            searchTracer: null,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(edges);
        Assert.Empty(scores);
    }

    // PORT: monkeypatch.setattr on `graphiti_core.search.search.edge_similarity_search` has no C# form (O10). The
    // function reads the search interface of the driver first, so a driver whose interface returns [] gives the same
    // answer as the patched function. The driver itself is never called.
    private sealed record EmptySearchInterface : SearchInterface
    {
        internal override Task<List<EntityEdge>> EdgeSimilaritySearchAsync(GraphDriver driver, List<double> searchVector, string? sourceNodeUuid, string? targetNodeUuid, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, double minScore = 0.7, CancellationToken cancellationToken = default) =>
            Task.FromResult<List<EntityEdge>>([]);
    }

    private sealed class FakeDriver : GraphDriver
    {
        internal FakeDriver()
        {
            SearchInterface = new EmptySearchInterface();
        }

        internal override string PythonName => "SimpleNamespace";

        internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeEmbedder : EmbedderClient
    {
        internal override Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default) =>
            Task.FromResult<List<double>>([0.1, 0.2, 0.3]);
    }

    private sealed class RecordingSpan(string name) : TracerSpan
    {
        internal string Name { get; } = name;

        internal OrderedDictionary<string, object?> Attributes { get; } = [];

        internal (string Status, string? Description)? Status { get; private set; }

        internal Exception? Exception { get; private set; }

        internal override void AddAttributes(OrderedDictionary<string, object?> attributes)
        {
            foreach (var (key, value) in attributes)
            {
                Attributes[key] = value;
            }
        }

        internal override void SetStatus(string status, string? description = null) => Status = (status, description);

        internal override void RecordException(Exception exception) => Exception = exception;
    }

    private sealed class RecordingTracer : Tracer
    {
        internal List<RecordingSpan> Spans { get; } = [];

        internal override async Task<T> StartSpanAsync<T>(string name, Func<TracerSpan, Task<T>> body, CancellationToken cancellationToken = default)
        {
            var span = new RecordingSpan(name);
            Spans.Add(span);
            return await body(span);
        }
    }
}
