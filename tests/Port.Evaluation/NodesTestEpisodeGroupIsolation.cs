// Ported from tests/test_episode_group_isolation.py (port/RULES.md, R25). Only the tests whose code under test is in
// graphiti_core/nodes.py: test_episode_uuid_query_binds_requested_group, test_predecessor_list_rejects_missing_scoped_uuid,
// test_custom_episode_lookup_rejects_foreign_result, and test_unscoped_episode_lookup_remains_supported. The other tests
// of the file wait for graphiti.py, edges.py, the saga queries, and the scheduler.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.PyCompat;

public partial class TestEpisodeGroupIsolation
{
    [Fact]
    public async Task test_episode_uuid_query_binds_requested_group()
    {
        var driver = new StubDriver([EpisodeRecord()]);

        var episode = await EpisodicNode.GetByUuidAsync(driver, "episode-a", groupId: "group-a", cancellationToken: TestContext.Current.CancellationToken);

        var (query, @params) = driver.Calls[0];
        Assert.Contains("WHERE e.group_id = $group_id", query, StringComparison.Ordinal);
        Assert.Equal("group-a", @params["group_id"]);
        Assert.Equal("group-a", episode.GroupId);
    }

    [Fact]
    public async Task test_predecessor_list_rejects_missing_scoped_uuid()
    {
        var driver = new StubDriver([EpisodeRecord(), EpisodeRecord(uuid: "episode-b", groupId: "group-b")]);

        var error = await Assert.ThrowsAsync<NodeNotFoundException>(() => EpisodicNode.GetByUuidsAsync(
            driver,
            ["episode-a", "episode-b"],
            groupId: "group-a",
            requireAll: true,
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("episode-b", error.Message, StringComparison.Ordinal);

        var (query, @params) = driver.Calls[0];
        Assert.Contains("AND e.group_id = $group_id", query, StringComparison.Ordinal);
        Assert.Equal("group-a", @params["group_id"]);
    }

    [Fact]
    public async Task test_custom_episode_lookup_rejects_foreign_result()
    {
        var driver = new StubDriver();
        driver.GraphOperationsInterface = new ForeignEpisodeOperations();

        var error = await Assert.ThrowsAsync<NodeNotFoundException>(
            () => EpisodicNode.GetByUuidAsync(driver, "episode-b", groupId: "group-a", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("episode-b", error.Message, StringComparison.Ordinal);

        Assert.Empty(driver.Calls);
    }

    [Fact]
    public async Task test_unscoped_episode_lookup_remains_supported()
    {
        var driver = new StubDriver([EpisodeRecord()]);

        var episode = await EpisodicNode.GetByUuidAsync(driver, "episode-a", cancellationToken: TestContext.Current.CancellationToken);

        var (query, _) = driver.Calls[0];
        Assert.DoesNotContain("WHERE e.group_id = $group_id", query, StringComparison.Ordinal);
        Assert.Equal("episode-a", episode.Uuid);
    }

    private static OrderedDictionary<string, object?> EpisodeRecord(string uuid = "episode-a", string groupId = "group-a")
    {
        var now = PyDateTime.FromDateTimeOffset(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return new OrderedDictionary<string, object?>
        {
            ["uuid"] = uuid,
            ["name"] = "episode",
            ["group_id"] = groupId,
            ["created_at"] = now,
            ["source"] = Py.EnumValue(EpisodeType.Text),
            ["source_description"] = "test",
            ["content"] = "safe content",
            ["valid_at"] = now,
            ["entity_edges"] = new List<object?>(),
        };
    }

    // The stub at the network boundary: a driver that records each query and returns the given records.
    private sealed class StubDriver : GraphDriver
    {
        private readonly List<OrderedDictionary<string, object?>> records;

        internal StubDriver(List<OrderedDictionary<string, object?>>? records = null)
        {
            Provider = GraphProvider.Neo4j;
            GraphOperationsInterface = null;
            this.records = records ?? [];
        }

        internal List<(string Query, OrderedDictionary<string, object?> Params)> Calls { get; } = [];

        internal override string PythonName => "StubDriver";

        internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((cypherQuery, parameters));
            return Task.FromResult<(List<OrderedDictionary<string, object?>>, object?, object?)>((records, null, null));
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    private sealed record ForeignEpisodeOperations : GraphOperationsInterface
    {
        internal override Task<EpisodicNode> EpisodicNodeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(new EpisodicNode
            {
                Uuid = uuid,
                Name = "foreign",
                GroupId = "group-b",
                Source = EpisodeType.Text,
                SourceDescription = "test",
                Content = "foreign content",
                ValidAt = PyDateTime.FromDateTimeOffset(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            }.Validate());
    }
}
