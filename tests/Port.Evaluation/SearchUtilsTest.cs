// Ported from tests/utils/search/search_utils_test.py (port/RULES.md, R25).
// PORT: the Python tests replace node_fulltext_search and node_similarity_search with mock.patch, which C# cannot do
// (open item O10). Each of the two functions first hands its call to driver.search_interface with the same arguments,
// so a fake search interface at the driver boundary stands in for the two mocks. It returns the side effects in
// order and records the arguments of each call.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Search;
using Graphiti.PyCompat;

public class SearchUtilsTest
{
    private static EntityNode Node(string uuid, string name) =>
        new EntityNode { Uuid = uuid, Name = name, Labels = ["Entity"], GroupId = "1" }.Validate();

    [Fact(Timeout = 5000)]
    public async Task test_hybrid_node_search_deduplication()
    {
        // Mock the database driver
        var mockSearch = new MockSearch(
            [[Node("1", "Alice")], [Node("2", "Bob")]],
            [[Node("1", "Alice")], [Node("3", "Charlie")]]);
        var mockDriver = new MockDriver { SearchInterface = mockSearch };

        // Call the function with test data
        List<string> queries = ["Alice", "Bob"];
        List<List<double>> embeddings = [[0.1, 0.2, 0.3], [0.4, 0.5, 0.6]];
        var results = await LoopGuard.RunAsync(() => SearchUtils.HybridNodeSearchAsync(queries, embeddings, mockDriver, new SearchFilters(), cancellationToken: TestContext.Current.CancellationToken));

        // Assertions
        Assert.Equal(3, results.Count);
        Assert.Equal(new HashSet<string> { "1", "2", "3" }, results.Select(node => node.Uuid).ToHashSet());
        Assert.Equal(new HashSet<string> { "Alice", "Bob", "Charlie" }, results.Select(node => node.Name).ToHashSet());

        // Verify that the mock functions were called correctly
        Assert.Equal(2, mockSearch.FulltextCalls.Count);
        Assert.Equal(2, mockSearch.SimilarityCalls.Count);
    }

    [Fact(Timeout = 5000)]
    public async Task test_hybrid_node_search_empty_results()
    {
        var mockSearch = new MockSearch([], []);
        var mockDriver = new MockDriver { SearchInterface = mockSearch };

        List<string> queries = ["NonExistent"];
        List<List<double>> embeddings = [[0.1, 0.2, 0.3]];
        var results = await LoopGuard.RunAsync(() => SearchUtils.HybridNodeSearchAsync(queries, embeddings, mockDriver, new SearchFilters(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(results);
    }

    [Fact(Timeout = 5000)]
    public async Task test_hybrid_node_search_only_fulltext()
    {
        var mockSearch = new MockSearch([[Node("1", "Alice")]], []);
        var mockDriver = new MockDriver { SearchInterface = mockSearch };

        List<string> queries = ["Alice"];
        List<List<double>> embeddings = [];
        var results = await LoopGuard.RunAsync(() => SearchUtils.HybridNodeSearchAsync(queries, embeddings, mockDriver, new SearchFilters(), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Single(results);
        Assert.Equal("Alice", results[0].Name);
        Assert.Single(mockSearch.FulltextCalls);
        Assert.Empty(mockSearch.SimilarityCalls);
    }

    [Fact(Timeout = 5000)]
    public async Task test_hybrid_node_search_with_limit()
    {
        var mockSearch = new MockSearch(
            [[Node("1", "Alice"), Node("2", "Bob")]],
            [[Node("3", "Charlie"), Node("4", "David")]]);
        var mockDriver = new MockDriver { SearchInterface = mockSearch };

        List<string> queries = ["Test"];
        List<List<double>> embeddings = [[0.1, 0.2, 0.3]];
        long limit = 1;
        var results = await LoopGuard.RunAsync(() => SearchUtils.HybridNodeSearchAsync(queries, embeddings, mockDriver, new SearchFilters(), ["1"], limit, TestContext.Current.CancellationToken));

        // We expect 4 results because the limit is applied per search method
        // before deduplication, and we're not actually limiting the results
        // in the hybrid_node_search function itself
        Assert.Equal(4, results.Count);
        Assert.Single(mockSearch.FulltextCalls);
        Assert.Single(mockSearch.SimilarityCalls);
        // Verify that the limit was passed to the search functions
        MockSearch.AssertCalledWith(mockSearch.FulltextCalls, mockDriver, "Test", ["1"], 2);
        MockSearch.AssertCalledWith(mockSearch.SimilarityCalls, mockDriver, new List<double> { 0.1, 0.2, 0.3 }, ["1"], 2);
    }

    [Fact(Timeout = 5000)]
    public async Task test_hybrid_node_search_with_limit_and_duplicates()
    {
        var mockSearch = new MockSearch(
            [[Node("1", "Alice"), Node("2", "Bob")]],
            [[Node("1", "Alice"), Node("3", "Charlie")]]); // Duplicate
        var mockDriver = new MockDriver { SearchInterface = mockSearch };

        List<string> queries = ["Test"];
        List<List<double>> embeddings = [[0.1, 0.2, 0.3]];
        long limit = 2;
        var results = await LoopGuard.RunAsync(() => SearchUtils.HybridNodeSearchAsync(queries, embeddings, mockDriver, new SearchFilters(), ["1"], limit, TestContext.Current.CancellationToken));

        // We expect 3 results because:
        // 1. The limit of 2 is applied to each search method
        // 2. We get 2 results from fulltext and 2 from similarity
        // 3. One result is a duplicate (Alice), so it's only included once
        Assert.Equal(3, results.Count);
        Assert.Equal(new HashSet<string> { "Alice", "Bob", "Charlie" }, results.Select(node => node.Name).ToHashSet());
        Assert.Single(mockSearch.FulltextCalls);
        Assert.Single(mockSearch.SimilarityCalls);
        MockSearch.AssertCalledWith(mockSearch.FulltextCalls, mockDriver, "Test", ["1"], 4);
        MockSearch.AssertCalledWith(mockSearch.SimilarityCalls, mockDriver, new List<double> { 0.1, 0.2, 0.3 }, ["1"], 4);
    }

    private sealed record Call(GraphDriver Driver, object Query, SearchFilters SearchFilter, List<string>? GroupIds, long Limit);

    // A side effect list gives one result for each call, and a list with one item is the return value of every call.
    private sealed record MockSearch(List<List<EntityNode>> FulltextResults, List<List<EntityNode>> SimilarityResults) : SearchInterface
    {
        internal List<Call> FulltextCalls { get; } = [];

        internal List<Call> SimilarityCalls { get; } = [];

        internal override async Task<List<EntityNode>> NodeFulltextSearchAsync(GraphDriver driver, string query, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, CancellationToken cancellationToken = default)
        {
            // A fake that returns a task yields first, then records the call (R25).
            await Task.Yield();
            FulltextCalls.Add(new(driver, query, searchFilter, groupIds, limit));
            return Next(FulltextResults, FulltextCalls.Count);
        }

        internal override async Task<List<EntityNode>> NodeSimilaritySearchAsync(GraphDriver driver, List<double> searchVector, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, double minScore = 0.7, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            SimilarityCalls.Add(new(driver, searchVector, searchFilter, groupIds, limit));
            return Next(SimilarityResults, SimilarityCalls.Count);
        }

        // assert_called_with checks the last call: the driver, the query or the vector, SearchFilters(), the group ids,
        // and the limit.
        internal static void AssertCalledWith(List<Call> calls, GraphDriver driver, object query, List<string> groupIds, long limit)
        {
            var last = calls[^1];
            Assert.Same(driver, last.Driver);
            Assert.Equal(query, last.Query);
            Assert.Equivalent(new SearchFilters().ModelDump(), last.SearchFilter.ModelDump());
            Assert.Equal(groupIds, last.GroupIds);
            Assert.Equal(limit, last.Limit);
        }

        private static List<EntityNode> Next(List<List<EntityNode>> results, int count) =>
            results.Count == 0 ? [] : results.Count == 1 ? results[0] : results[count - 1];
    }

    private sealed class MockDriver : GraphDriver
    {
        internal override string PythonName => "AsyncMock";

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
