// Ported from tests/utils/maintenance/test_identity_properties.py (port/RULES.md, R25). Only the tests whose code under
// test is in graphiti_core/nodes.py: the five tests of EntityNode.get_by_attribute_values. The other tests of the file
// wait for utils/ontology_utils/entity_types_utils.py, utils/maintenance/node_operations.py, and graphiti.py.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.PyCompat;

public partial class TestIdentityProperties
{
    [Theory]
    [InlineData("neo4j")]
    [InlineData("falkordb")]
    public async Task test_get_by_attribute_values_builds_safe_query(string provider)
    {
        // PORT: GraphProvider is internal, so the parameter is the value of the member.
        var driver = new MockDriver(PyEnum.FromValue<GraphProvider>(provider)) { Database = "group" };

        var result = await EntityNode.GetByAttributeValuesAsync(
            driver,
            "group",
            new OrderedDictionary<string, object?> { ["email"] = "alex@example.com", ["employee_id"] = "42" },
            labels: ["Person"],
            limit: 7,
            cancellationToken: TestContext.Current.CancellationToken);

        var (query, parameters) = Assert.Single(driver.ExecuteQueryCalls);
        Assert.Empty(result);
        Assert.Contains("MATCH (n:Entity:Person)", query, StringComparison.Ordinal);
        Assert.Contains("n.group_id = $group_id", query, StringComparison.Ordinal);
        Assert.Contains("n.`email` = $attribute_0", query, StringComparison.Ordinal);
        Assert.Contains("n.`employee_id` = $attribute_1", query, StringComparison.Ordinal);
        Assert.DoesNotContain("alex@example.com", query, StringComparison.Ordinal);
        Assert.Equal("alex@example.com", parameters["attribute_0"]);
        Assert.Equal("42", parameters["attribute_1"]);
        Assert.Equal("group", parameters["group_id"]);
        Assert.Equal(7L, parameters["limit"]);
    }

    [Fact]
    public async Task test_get_by_attribute_values_returns_empty_for_empty_attributes()
    {
        var driver = new MockDriver(GraphProvider.Neo4j);

        Assert.Empty(await EntityNode.GetByAttributeValuesAsync(driver, "group", [], cancellationToken: TestContext.Current.CancellationToken));
        Assert.Empty(driver.ExecuteQueryCalls);
    }

    [Fact]
    public async Task test_get_by_attribute_values_uses_supplied_falkordb_driver()
    {
        var driver = new MockDriver(GraphProvider.Falkordb) { Database = "default_db" };

        await EntityNode.GetByAttributeValuesAsync(
            driver,
            "group",
            new OrderedDictionary<string, object?> { ["email"] = "alex@example.com" },
            labels: ["Person"],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, driver.CloneCalls);
        Assert.Single(driver.ExecuteQueryCalls);
    }

    [Fact]
    public async Task test_get_by_attribute_values_rejects_unsafe_attribute_name()
    {
        var driver = new MockDriver(GraphProvider.Neo4j);

        var error = await Assert.ThrowsAsync<ArgumentException>(() => EntityNode.GetByAttributeValuesAsync(
            driver,
            "group",
            new OrderedDictionary<string, object?> { ["email` OR true //"] = "alex@example.com" },
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("Invalid attribute name", error.Message, StringComparison.Ordinal);

        Assert.Empty(driver.ExecuteQueryCalls);
    }

    [Fact]
    public async Task test_get_by_attribute_values_rejects_unsafe_label()
    {
        var driver = new MockDriver(GraphProvider.Neo4j);

        // PORT: NodeLabelValidationError is a ValueError in Python. Its C# class derives from GraphitiException (R4).
        var error = await Assert.ThrowsAsync<NodeLabelValidationException>(() => EntityNode.GetByAttributeValuesAsync(
            driver,
            "group",
            new OrderedDictionary<string, object?> { ["email"] = "alex@example.com" },
            labels: ["Person` MATCH (n)"],
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("node_labels", error.Message, StringComparison.Ordinal);

        Assert.Empty(driver.ExecuteQueryCalls);
    }

    // The MagicMock driver of the Python tests, at the network boundary: execute_query returns ([], [], None).
    private sealed class MockDriver : GraphDriver
    {
        internal MockDriver(GraphProvider provider)
        {
            Provider = provider;
            GraphOperationsInterface = null;
        }

        internal List<(string Query, OrderedDictionary<string, object?> Parameters)> ExecuteQueryCalls { get; } = [];

        internal int CloneCalls { get; private set; }

        internal override string PythonName => "MagicMock";

        // The call is recorded after a yield, so a call that the code does not await is not seen by the assertion.
        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            ExecuteQueryCalls.Add((cypherQuery, parameters));
            return ([], new List<object?>(), null);
        }

        internal override GraphDriver Clone(string database)
        {
            CloneCalls++;
            return this;
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }
}
