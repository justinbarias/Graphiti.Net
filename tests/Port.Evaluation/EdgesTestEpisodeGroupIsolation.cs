// Ported from tests/test_episode_group_isolation.py (port/RULES.md, R25): the six tests whose code under test is in
// graphiti_core/edges.py. The other tests of the file wait for the units of graphiti.py, nodes.py, ingestion, and
// utils/maintenance. Python has no test class here, so the class has the name of the file with the prefix of the unit.
namespace Graphiti.Port.Evaluation;

using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.PyCompat;

public class EdgesTestEpisodeGroupIsolation
{
    private static readonly PyDateTime Now = PyDateTime.FromIsoFormat("2026-01-01T00:00:00+00:00");

    [Fact]
    public async Task test_entity_edge_uuid_lookup_filters_requested_group()
    {
        var driver = new StubDriver([EntityEdgeRecord()]);

        var edge = await EntityEdge.GetByUuidAsync(driver, "edge-a", groupId: "group-a", cancellationToken: TestContext.Current.CancellationToken);

        var (query, parameters) = driver.Calls[0];
        Assert.Contains("WHERE e.group_id = $group_id", query, StringComparison.Ordinal);
        Assert.Equal("group-a", parameters["group_id"]);
        Assert.Equal("group-a", edge.GroupId);
    }

    [Fact]
    public async Task test_entity_edge_uuid_lookup_rejects_foreign_query_result()
    {
        var driver = new StubDriver([EntityEdgeRecord(groupId: "group-b")]);

        var error = await Assert.ThrowsAsync<EdgeNotFoundException>(
            () => EntityEdge.GetByUuidAsync(driver, "edge-a", groupId: "group-a", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Matches(new Regex("edge-a", RegexOptions.CultureInvariant), error.Message);
    }

    [Fact]
    public async Task test_entity_edge_uuid_lookup_keeps_unscoped_behavior()
    {
        var driver = new StubDriver([EntityEdgeRecord(groupId: "group-b")]);

        var edge = await EntityEdge.GetByUuidAsync(driver, "edge-a", cancellationToken: TestContext.Current.CancellationToken);

        var (query, parameters) = driver.Calls[0];
        Assert.DoesNotContain("WHERE e.group_id = $group_id", query, StringComparison.Ordinal);
        Assert.Null(parameters["group_id"]);
        Assert.Equal("group-b", edge.GroupId);
    }

    [Fact]
    public async Task test_entity_edge_bulk_lookup_filters_foreign_edges()
    {
        var driver = new StubDriver([EntityEdgeRecord(), EntityEdgeRecord(uuid: "edge-b", groupId: "group-b")]);

        var edges = await EntityEdge.GetByUuidsAsync(driver, ["edge-a", "edge-b"], groupId: "group-a", cancellationToken: TestContext.Current.CancellationToken);

        var (query, parameters) = driver.Calls[0];
        Assert.Contains("AND e.group_id = $group_id", query, StringComparison.Ordinal);
        Assert.Equal("group-a", parameters["group_id"]);
        Assert.Equal(["edge-a"], edges.Select(edge => edge.Uuid));
    }

    [Fact]
    public async Task test_entity_edge_uuid_lookup_filters_graph_operations_result()
    {
        var edge = new EntityEdge
        {
            Uuid = "edge-b",
            GroupId = "group-b",
            SourceNodeUuid = "source-b",
            TargetNodeUuid = "target-b",
            CreatedAt = Now,
            Name = "RELATED_TO",
            Fact = "source relates to target",
        }.Validate();
        var driver = new StubDriver([]) { GraphOperationsInterface = new EntityEdgeOperations(edge) };

        var error = await Assert.ThrowsAsync<EdgeNotFoundException>(
            () => EntityEdge.GetByUuidAsync(driver, "edge-b", groupId: "group-a", cancellationToken: TestContext.Current.CancellationToken));
        Assert.Matches(new Regex("edge-b", RegexOptions.CultureInvariant), error.Message);

        Assert.Empty(driver.Calls);
    }

    [Fact]
    public async Task test_entity_edge_bulk_lookup_filters_graph_operations_result()
    {
        List<EntityEdge> edges =
        [
            new EntityEdge
            {
                Uuid = "edge-a",
                GroupId = "group-a",
                SourceNodeUuid = "source-a",
                TargetNodeUuid = "target-a",
                CreatedAt = Now,
                Name = "RELATED_TO",
                Fact = "source relates to target",
            }.Validate(),
            new EntityEdge
            {
                Uuid = "edge-b",
                GroupId = "group-b",
                SourceNodeUuid = "source-b",
                TargetNodeUuid = "target-b",
                CreatedAt = Now,
                Name = "RELATED_TO",
                Fact = "source relates to target",
            }.Validate(),
        ];
        var driver = new StubDriver([]) { GraphOperationsInterface = new EntityEdgeOperations(edges[0], edges) };

        var result = await EntityEdge.GetByUuidsAsync(driver, ["edge-a", "edge-b"], groupId: "group-a", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["edge-a"], result.Select(edge => edge.Uuid));
        Assert.Empty(driver.Calls);
    }

    private static OrderedDictionary<string, object?> EntityEdgeRecord(string uuid = "edge-a", string groupId = "group-a") => new()
    {
        ["uuid"] = uuid,
        ["source_node_uuid"] = "source-a",
        ["target_node_uuid"] = "target-a",
        ["fact"] = "source relates to target",
        ["fact_embedding"] = null,
        ["name"] = "RELATED_TO",
        ["group_id"] = groupId,
        ["episodes"] = new List<object?>(),
        ["created_at"] = Now,
        ["expired_at"] = null,
        ["valid_at"] = null,
        ["invalid_at"] = null,
        ["reference_time"] = null,
        ["attributes"] = new OrderedDictionary<string, object?>(),
    };

    private sealed class StubDriver(List<OrderedDictionary<string, object?>> records) : GraphDriver
    {
        internal List<(string Query, OrderedDictionary<string, object?> Parameters)> Calls { get; } = [];

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

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed record EntityEdgeOperations : GraphOperationsInterface
    {
        private readonly EntityEdge edge;
        private readonly List<EntityEdge> edges;

        internal EntityEdgeOperations(EntityEdge edge, List<EntityEdge>? edges = null)
        {
            this.edge = edge;
            this.edges = edges ?? [edge];
        }

        internal override Task<EntityEdge> EdgeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
            Task.FromResult(edge);

        internal override Task<List<EntityEdge>> EdgeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default) =>
            Task.FromResult(edges);
    }
}
