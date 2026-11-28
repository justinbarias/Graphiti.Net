// Port of .upstream/graphiti/tests/test_saga_refetch_episode_uuids.py (port/RULES.md, R25).
// PORT: the AsyncMock driver is a GraphDriver at the network boundary whose execute_query gives one fixed answer and
// counts its awaits. `Graphiti.__new__(Graphiti)` is RuntimeHelpers.GetUninitializedObject.
namespace Graphiti.Port.Evaluation;

using System.Runtime.CompilerServices;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.PyCompat;

/// <summary>A driver whose execute_query returns the same records each time, as `return_value` of AsyncMock.</summary>
internal sealed class FixedAnswerDriver(List<OrderedDictionary<string, object?>> records) : GraphDriver
{
    internal int ExecuteQueryAwaits { get; private set; }

    internal override string PythonName => "AsyncMock";

    internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
        string cypherQuery, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        ExecuteQueryAwaits++;
        return ([.. records.Select(r => new OrderedDictionary<string, object?>(r))], null, null);
    }

    internal override GraphDriverSession Session(string? database = null) => throw new NotImplementedException();

    internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}

public class TestSagaRefetchEpisodeUuids
{
    /// <summary>
    /// Re-fetching an existing saga must round-trip first/last_episode_uuid.
    ///
    /// The re-fetch query used to select only uuid/name/group_id/created_at, so
    /// the returned node always carried first/last_episode_uuid as None and the
    /// caller's first-episode guard fired on every episode (#1755).
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task test_get_or_create_saga_refetch_preserves_episode_uuids()
    {
        var driver = new FixedAnswerDriver(
        [
            new()
            {
                ["uuid"] = "saga-1",
                ["name"] = "my-saga",
                ["group_id"] = "g1",
                ["created_at"] = "2026-08-13T00:00:00+00:00",
                ["first_episode_uuid"] = "ep-1",
                ["last_episode_uuid"] = "ep-2",
            },
        ]);

        var graphiti = (GraphitiCore)RuntimeHelpers.GetUninitializedObject(typeof(GraphitiCore));
        graphiti.Driver = driver;

        var saga = await LoopGuard.RunAsync(() => graphiti.GetOrCreateSagaAsync("my-saga", "g1", new PyDateTime(2026, 8, 13), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("ep-1", saga.FirstEpisodeUuid);
        Assert.Equal("ep-2", saga.LastEpisodeUuid);

        // The existing-saga branch must not write anything back.
        Assert.Equal(1, driver.ExecuteQueryAwaits);
    }

    /// <summary>No matching saga row: a fresh node is minted and saved.</summary>
    [Fact(Timeout = 5000)]
    public async Task test_get_or_create_saga_creates_when_absent()
    {
        var driver = new FixedAnswerDriver([]);

        var graphiti = (GraphitiCore)RuntimeHelpers.GetUninitializedObject(typeof(GraphitiCore));
        graphiti.Driver = driver;

        var saga = await LoopGuard.RunAsync(() => graphiti.GetOrCreateSagaAsync("new-saga", "g1", new PyDateTime(2026, 8, 13), cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("new-saga", saga.Name);
        Assert.Null(saga.FirstEpisodeUuid);
        Assert.Null(saga.LastEpisodeUuid);

        // PORT: `assert saga.save is not None` holds for each SagaNode, because `save` is a method. The port reads the
        // save in its place: the find query and the save query are the two awaits of execute_query.
        // The Python test takes another path. Its AsyncMock driver has a `graph_operations_interface` that is true, so
        // SagaNode.save saves through the mocked operations interface, and Python awaits execute_query one time (the
        // find query). The fake driver of this test has no operations interface, so the save is a query. The value 2
        // is the one that the recorded Python case `saga_absent` of values/graphiti.json gives for a driver with no
        // operations interface: the find query and the save query.
        Assert.Equal(2, driver.ExecuteQueryAwaits);
    }
}
