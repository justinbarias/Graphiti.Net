// Ported from tests/utils/search/test_edge_cross_encoder_rrf_shortlist.py (port/RULES.md, R25). The code under test is
// graphiti_core/search/search.py, `edge_search`.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class TestEdgeCrossEncoderRrfShortlist
{
    private static EntityEdge Edge(string uuid, string fact) => new()
    {
        Uuid = uuid,
        SourceNodeUuid = "source",
        TargetNodeUuid = "target",
        Name = "relates_to",
        GroupId = "group_1",
        Fact = fact,
        CreatedAt = PyDateTime.UtcNow(TimeProvider.System),
    };

    // RRF + 2*limit shortlist: cosine rank-1 reaches CE even when BM25 list is long.
    [Fact(Timeout = 5000)]
    public async Task test_rrf_shortlist_includes_cosine_top_hit()
    {
        const int limit = 2;
        List<EntityEdge> bm25Hits = [.. Enumerable.Range(0, 6).Select(i => Edge($"bm25-{i}", $"bm25-fact-{i}"))];
        var cosineHit = Edge("cosine-1", "cosine-top-fact");
        var crossEncoder = new RecordingCrossEncoder(cosineHit.Fact, 0.99);

        var (edges, scores) = await LoopGuard.RunAsync(() => SearchModule.EdgeSearchAsync(
            driver: new FakeDriver(new FakeSearchInterface(bm25Hits, [cosineHit])),
            crossEncoder: crossEncoder,
            query: "find the cosine top hit",
            queryVector: [0.1, 0.2, 0.3],
            groupIds: null,
            config: new EdgeSearchConfig
            {
                SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity],
                Reranker = EdgeReranker.CrossEncoder,
            },
            searchFilter: new SearchFilters(),
            limit: limit,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(cosineHit.Fact, crossEncoder.RankedPassages);
        Assert.Equal(2 * limit, crossEncoder.RankedPassages.Count);
        Assert.True(crossEncoder.RankedPassages.Count > limit);
        Assert.Equal(cosineHit.Uuid, edges[0].Uuid);
        Assert.Equal(limit, edges.Count);
        Assert.Equal(limit, scores.Count);
    }

    // Smoke: BM25 fills weak hits; cosine has the real answer.
    //
    // Query: "Where does Alice work?"
    // BM25:  "Alice likes coffee", "Alice lives in SF"  (and more)
    // Cosine: "Alice works at Zep"
    //
    // Before the fix, CE only saw the first `limit` BM25 facts.
    // After, RRF shortlists up to `2 * limit` candidates so Zep reaches CE and can rank #1.
    [Fact(Timeout = 5000)]
    public async Task test_smoke_alice_works_at_zep_reaches_cross_encoder()
    {
        const int limit = 2;
        var coffee = Edge("bm25-1", "Alice likes coffee");
        var livesInSf = Edge("bm25-2", "Alice lives in SF");
        List<EntityEdge> extraBm25 = [.. Enumerable.Range(3, 2).Select(i => Edge($"bm25-{i}", $"Alice unrelated {i}"))];
        var worksAtZep = Edge("cosine-1", "Alice works at Zep");
        var crossEncoder = new RecordingCrossEncoder(worksAtZep.Fact, 0.99);

        var (edges, scores) = await LoopGuard.RunAsync(() => SearchModule.EdgeSearchAsync(
            driver: new FakeDriver(new FakeSearchInterface([coffee, livesInSf, .. extraBm25], [worksAtZep])),
            crossEncoder: crossEncoder,
            query: "Where does Alice work?",
            queryVector: [0.1, 0.2, 0.3],
            groupIds: null,
            config: new EdgeSearchConfig
            {
                SearchMethods = [EdgeSearchMethod.Bm25, EdgeSearchMethod.CosineSimilarity],
                Reranker = EdgeReranker.CrossEncoder,
            },
            searchFilter: new SearchFilters(),
            limit: limit,
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains(worksAtZep.Fact, crossEncoder.RankedPassages);
        Assert.True(crossEncoder.RankedPassages.Count <= 2 * limit);
        Assert.Equal(worksAtZep.Uuid, edges[0].Uuid);
        Assert.Equal("Alice works at Zep", edges[0].Fact);
        Assert.Equal(limit, edges.Count);
        Assert.Equal(limit, scores.Count);
    }

    // PORT: monkeypatch.setattr on `graphiti_core.search.search.edge_fulltext_search` and `edge_similarity_search` has no
    // C# form (O10). Each function reads the search interface of the driver first, so a driver whose interface returns
    // the two lists gives the same answer as the patched functions. The driver itself is never called.
    private sealed record FakeSearchInterface(List<EntityEdge> Bm25Hits, List<EntityEdge> CosineHits) : SearchInterface
    {
        internal override Task<List<EntityEdge>> EdgeFulltextSearchAsync(GraphDriver driver, string query, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, CancellationToken cancellationToken = default) =>
            Task.FromResult(Bm25Hits);

        internal override Task<List<EntityEdge>> EdgeSimilaritySearchAsync(GraphDriver driver, List<double> searchVector, string? sourceNodeUuid, string? targetNodeUuid, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, double minScore = 0.7, CancellationToken cancellationToken = default) =>
            Task.FromResult(CosineHits);
    }

    // Gives the score `topScore` to the passage `topPassage` and 0.1 to each other passage, and returns them by score,
    // highest first.
    private sealed class RecordingCrossEncoder(string topPassage, double topScore) : CrossEncoderClient
    {
        internal List<string> RankedPassages { get; } = [];

        internal override Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default)
        {
            RankedPassages.AddRange(passages);
            List<(string, double)> scored = [.. passages.Select(p => (p, p == topPassage ? topScore : 0.1))];
            Py.SortInPlace(scored, item => item.Item2, reverse: true);
            return Task.FromResult(scored);
        }
    }

    private sealed class FakeDriver : GraphDriver
    {
        internal FakeDriver(SearchInterface searchInterface)
        {
            SearchInterface = searchInterface;
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
}
