// Ported from tests/utils/search/test_episode_mentions_reranker.py (port/RULES.md, R25).
// Unit tests for graphiti_core.search.search_utils.episode_mentions_reranker.
//
// Regression coverage for #1342: the reranker sorted ascending by mention
// count, so the least-mentioned nodes ranked first - the opposite of what the
// function name and every other count-based reranker implies.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Driver;
using Graphiti.Core.Search;

public class TestEpisodeMentionsReranker
{
    // PORT: AsyncMock with execute_query returning (records, None, None) is a fake driver at the network boundary.
    private static MockDriver Driver(params (string Uuid, long Score)[] records) =>
        new([.. records.Select(r => new OrderedDictionary<string, object?> { ["uuid"] = r.Uuid, ["score"] = r.Score })]);

    [Fact]
    public async Task test_episode_mentions_reranker_orders_by_descending_mentions()
    {
        var driver = Driver(("low", 1), ("high", 10), ("mid", 5));

        var (sortedUuids, scores) = await SearchUtils.EpisodeMentionsRerankerAsync(driver, [["low", "high", "mid"]], cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["high", "mid", "low"], sortedUuids);
        Assert.Equal([10.0, 5.0, 1.0], scores);
    }

    [Fact]
    public async Task test_episode_mentions_reranker_unmentioned_nodes_rank_last()
    {
        var driver = Driver(("mentioned", 3));

        var (sortedUuids, scores) = await SearchUtils.EpisodeMentionsRerankerAsync(
            driver, [["mentioned", "unmentioned"]], minScore: 0, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["mentioned", "unmentioned"], sortedUuids);
        Assert.Equal([3.0, 0.0], scores);
    }

    [Fact]
    public async Task test_episode_mentions_reranker_min_score_filters_unmentioned_nodes()
    {
        var driver = Driver(("mentioned", 3));

        // A positive min_score should filter out nodes with zero mentions -
        // under the old float('inf') sentinel this could never happen, since
        // infinity always satisfied `score >= min_score`.
        var (sortedUuids, scores) = await SearchUtils.EpisodeMentionsRerankerAsync(
            driver, [["mentioned", "unmentioned"]], minScore: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["mentioned"], sortedUuids);
        Assert.Equal([3.0], scores);
    }

    [Fact]
    public async Task test_episode_mentions_reranker_all_unmentioned_returns_empty_when_filtered()
    {
        var driver = Driver();

        var (sortedUuids, scores) = await SearchUtils.EpisodeMentionsRerankerAsync(driver, [["a", "b"]], minScore: 1, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(sortedUuids);
        Assert.Empty(scores);
    }

    private sealed class MockDriver(List<OrderedDictionary<string, object?>> records) : GraphDriver
    {
        internal override string PythonName => "AsyncMock";

        internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<(List<OrderedDictionary<string, object?>>, object?, object?)>((records, null, null));

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
