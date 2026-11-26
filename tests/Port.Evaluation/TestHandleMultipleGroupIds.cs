// Port of .upstream/graphiti/tests/test_handle_multiple_group_ids.py (port/RULES.md, R25).
// PORT: the fake driver is a GraphDriver at the network boundary. It overrides Clone, which is virtual.
// PORT: Python's `search('q', ['a', 'b'], driver)` and `search('q', ['a', 'b'], driver=driver)` differ in how the wrapper
// finds group_ids. The C# wrapper takes group_ids and driver as parameters (R16), so the two calls have one form.
namespace Graphiti.Port.Evaluation;

using System.Collections.Generic;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.PyCompat;

internal sealed class FakeGroupDriver : GraphDriver
{
    internal FakeGroupDriver(string database, GraphProvider provider = GraphProvider.Falkordb)
    {
        Database = database;
        Provider = provider;
    }

    internal List<string> CloneCalls { get; } = [];

    internal override string PythonName => "_FakeDriver";

    internal override GraphDriver Clone(string database)
    {
        CloneCalls.Add(database);
        return new FakeGroupDriver(database, Provider);
    }

    internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
        string cypherQuery, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    internal override GraphDriverSession Session(string? database = null) => throw new NotSupportedException();

    internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

    internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

internal sealed class GroupIdsHost(GraphDriver? clientsDriver)
{
    internal GraphDriver? ClientsDriver { get; } = clientsDriver;

    internal long? MaxCoroutines { get; }

    internal List<string?> SeenDrivers { get; } = [];

    internal Task<List<string>> SearchAsync(string query, List<string>? groupIds = null, GraphDriver? driver = null) =>
        Decorators.HandleMultipleGroupIdsAsync(
            ClientsDriver, MaxCoroutines, groupIds, driver, (ids, d) => SearchCoreAsync(query, ids, d));

    private async Task<List<string>> SearchCoreAsync(string query, List<string>? groupIds, GraphDriver? driver)
    {
        // A fake that returns a task yields first, then records the call (R25).
        await Task.Yield();
        var db = driver?.Database;
        SeenDrivers.Add(db);
        return [$"{query}:{db ?? "None"}:{GroupIdsText(groupIds)}"];
    }

    internal static string GroupIdsText(List<string>? ids) => ids is null ? "None" : "[" + string.Join(", ", ids.Select(i => $"'{i}'")) + "]";
}

public class TestHandleMultipleGroupIds
{
    [Fact]
    public async Task test_falkor_single_group_id_clones_driver_when_database_differs()
    {
        // Single group_id must route to that graph, not the driver's default (#1659).
        await Asyncio.Run(async () =>
        {
            var driver = new FakeGroupDriver("reggraph");
            var host = new GroupIdsHost(driver);

            var result = await host.SearchAsync("q", ["acptprobe"]);

            Assert.Equal(["acptprobe"], driver.CloneCalls);
            // Shared driver must not be reassigned (call-scoped clone only)
            Assert.Same(driver, host.ClientsDriver);
            Assert.Equal("reggraph", host.ClientsDriver!.Database);
            Assert.Equal(["acptprobe"], host.SeenDrivers);
            Assert.Equal(["q:acptprobe:['acptprobe']"], result);
        });
    }

    [Fact]
    public async Task test_falkor_single_group_id_skips_clone_when_already_on_graph()
    {
        await Asyncio.Run(async () =>
        {
            var driver = new FakeGroupDriver("acptprobe");
            var host = new GroupIdsHost(driver);

            var result = await host.SearchAsync("q", ["acptprobe"]);

            Assert.Empty(driver.CloneCalls);
            Assert.Equal([null], host.SeenDrivers); // uses default path without injecting driver
            Assert.Equal(["q:None:['acptprobe']"], result);
        });
    }

    [Fact]
    public async Task test_falkor_multi_group_ids_still_clones_each()
    {
        await Asyncio.Run(async () =>
        {
            var driver = new FakeGroupDriver("reggraph");
            var host = new GroupIdsHost(driver);

            var result = await host.SearchAsync("q", ["a", "b"]);

            Assert.Equal(["a", "b"], driver.CloneCalls);
            Assert.Same(driver, host.ClientsDriver);
            Assert.Equal(new HashSet<string?> { "a", "b" }, host.SeenDrivers.ToHashSet());
            Assert.Equal(["q:a:['a']", "q:b:['b']"], result.Order(StringComparer.Ordinal).ToList());
        });
    }

    [Fact]
    public async Task test_non_falkor_single_group_id_is_passthrough()
    {
        await Asyncio.Run(async () =>
        {
            var driver = new FakeGroupDriver("neo4j", GraphProvider.Neo4j);
            var host = new GroupIdsHost(driver);

            var result = await host.SearchAsync("q", ["tenant"]);

            Assert.Empty(driver.CloneCalls);
            Assert.Equal([null], host.SeenDrivers);
            Assert.Equal(["q:None:['tenant']"], result);
        });
    }

    [Fact]
    public async Task test_falkor_multi_group_ids_with_positional_driver()
    {
        // A positional `driver` must not collide with the injected keyword (#1758).
        await Asyncio.Run(async () =>
        {
            var driver = new FakeGroupDriver("reggraph");
            var host = new GroupIdsHost(driver);

            var result = await host.SearchAsync("q", ["a", "b"], driver);

            Assert.Equal(["a", "b"], driver.CloneCalls);
            Assert.Same(driver, host.ClientsDriver);
            Assert.Equal(new HashSet<string?> { "a", "b" }, host.SeenDrivers.ToHashSet());
            Assert.Equal(["q:a:['a']", "q:b:['b']"], result.Order(StringComparer.Ordinal).ToList());
        });
    }

    [Fact]
    public async Task test_falkor_multi_group_ids_with_positional_group_ids_and_kwarg_driver()
    {
        // `group_ids` positional with `driver` as a keyword.
        await Asyncio.Run(async () =>
        {
            var driver = new FakeGroupDriver("reggraph");
            var host = new GroupIdsHost(driver);

            var result = await host.SearchAsync("q", ["a", "b"], driver: driver);

            Assert.Equal(["a", "b"], driver.CloneCalls);
            Assert.Same(driver, host.ClientsDriver);
            Assert.Equal(new HashSet<string?> { "a", "b" }, host.SeenDrivers.ToHashSet());
            Assert.Equal(["q:a:['a']", "q:b:['b']"], result.Order(StringComparer.Ordinal).ToList());
        });
    }
}
