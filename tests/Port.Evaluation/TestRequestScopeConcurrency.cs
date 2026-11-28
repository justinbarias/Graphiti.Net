// Port of .upstream/graphiti/tests/test_request_scope_concurrency.py (port/RULES.md, R25).
// Regression tests for concurrent multi-group_id database routing (issue #1676).
//
// These tests are database-free: they exercise ``Graphiti._resolve_request_scope``
// directly with a fake driver. The historical bug was that ``add_episode`` /
// ``add_episode_bulk`` reassigned the shared ``self.driver`` when a ``group_id``
// mapped to a different database. Because those coroutines have many ``await``
// points, a concurrent call for a different ``group_id`` could reassign
// ``self.driver`` mid-execution and silently persist episodes under the wrong
// graph. The fix returns a request-scoped driver/clients bundle instead of
// mutating shared instance state.
//
// PORT: Python's `Mock(spec=...)` clients are never called. The port gives fakes at the network boundary that throw
// when a test calls them (R25, no mocks between internal classes). `Graphiti.__new__(Graphiti)` is
// RuntimeHelpers.GetUninitializedObject.
namespace Graphiti.Port.Evaluation;

using System.Runtime.CompilerServices;
using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;

/// <summary>
/// Minimal in-memory GraphDriver whose ``clone`` records the target database.
///
/// Each ``clone`` returns a brand new instance bound to the requested database,
/// mirroring how the real drivers hand back a call-scoped copy without mutating
/// the original.
/// </summary>
internal sealed class RequestScopeFakeDriver : GraphDriver
{
    internal RequestScopeFakeDriver(string database = "default_db")
    {
        Provider = GraphProvider.Neo4j;
        Database = database;
    }

    internal List<string> CloneCalls { get; } = [];

    internal override string PythonName => "FakeDriver";

    internal override GraphDriver Clone(string database)
    {
        CloneCalls.Add(database);
        return new RequestScopeFakeDriver(database);
    }

    // --- Abstract methods: unused by these tests. ---
    internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
        string cypherQuery, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    internal override GraphDriverSession Session(string? database = null) => throw new NotImplementedException();

    internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();

    internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}

/// <summary>An LLM client that the tests never call.</summary>
internal sealed class UnusedLlmClient() : LLMClient(null, false)
{
    internal override string PythonName => "UnusedLlmClient";

    internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
        List<Message> messages,
        IResponseModel? responseModel = null,
        long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
        ModelSize modelSize = ModelSize.Medium,
        string? model = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
}

/// <summary>An embedder that the tests never call.</summary>
internal sealed class UnusedEmbedder : EmbedderClient
{
    internal override Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}

/// <summary>A cross-encoder that the tests never call.</summary>
internal sealed class UnusedCrossEncoder : CrossEncoderClient
{
    internal override Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}

public class TestRequestScopeConcurrency
{
    [Fact]
    public void test_resolve_request_scope_none_group_id_reuses_shared_driver()
    {
        var (graphiti, driver) = MakeGraphiti();

        var (groupId, scopedDriver, scopedClients) = graphiti.ResolveRequestScope(null);

        // Default group id for Neo4j is the empty string.
        Assert.Equal(string.Empty, groupId);

        // No clone occurred and the shared instances are reused as-is.
        Assert.Same(driver, scopedDriver);
        Assert.Same(graphiti.Clients, scopedClients);
        Assert.Empty(driver.CloneCalls);
    }

    [Fact]
    public void test_resolve_request_scope_matching_group_id_reuses_shared_driver()
    {
        var (graphiti, driver) = MakeGraphiti(database: "tenant_a");

        var (groupId, scopedDriver, scopedClients) = graphiti.ResolveRequestScope("tenant_a");

        Assert.Equal("tenant_a", groupId);
        Assert.Same(driver, scopedDriver);
        Assert.Same(graphiti.Clients, scopedClients);
        Assert.Empty(driver.CloneCalls);
    }

    [Fact]
    public void test_resolve_request_scope_different_group_id_does_not_mutate_shared_state()
    {
        var (graphiti, driver) = MakeGraphiti(database: "default_db");
        var originalClients = graphiti.Clients;

        var (groupId, scopedDriver, scopedClients) = graphiti.ResolveRequestScope("tenant_b");

        // A request-scoped clone targeting the requested database is returned.
        Assert.Equal("tenant_b", groupId);
        Assert.NotSame(driver, scopedDriver);
        Assert.Equal("tenant_b", scopedDriver.Database);
        Assert.Equal(["tenant_b"], driver.CloneCalls);

        // The scoped clients bundle points at the clone but preserves the other clients.
        Assert.NotSame(originalClients, scopedClients);
        Assert.Same(scopedDriver, scopedClients.Driver);
        Assert.Same(originalClients.LlmClient, scopedClients.LlmClient);
        Assert.Same(originalClients.Embedder, scopedClients.Embedder);
        Assert.Same(originalClients.CrossEncoder, scopedClients.CrossEncoder);

        // Crucially, the shared instance state is untouched (this is the #1676 fix).
        Assert.Same(driver, graphiti.Driver);
        Assert.Equal("default_db", graphiti.Driver.Database);
        Assert.Same(originalClients, graphiti.Clients);
        Assert.Same(driver, graphiti.Clients.Driver);
    }

    /// <summary>
    /// Two interleaved calls for different group_ids must not clobber each other.
    ///
    /// Each worker resolves its request scope, then yields control (simulating the
    /// many ``await`` points inside ``add_episode``) while the other worker runs.
    /// With the pre-fix code that reassigned ``self.driver``, the first worker's
    /// driver would be silently repointed at the second worker's database. The
    /// request-scoped bundle keeps them isolated.
    /// </summary>
    [Fact(Timeout = 5000)]
    public async Task test_concurrent_group_ids_keep_independent_drivers()
    {
        var (graphiti, baseDriver) = MakeGraphiti(database: "default_db");

        var observations = new OrderedDictionary<string, string>();

        async Task Worker(string gid, double hold)
        {
            var (_, scopedDriver, scopedClients) = graphiti.ResolveRequestScope(gid);

            // Yield control so a concurrent worker interleaves here.
            // PORT: asyncio.sleep(0) yields once, so a hold of 0 is Task.Yield.
            if (hold > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(hold), TestContext.Current.CancellationToken);
            }
            else
            {
                await Task.Yield();
            }

            // After the interleave, the scoped driver must still target this worker's db.
            Assert.Equal(gid, scopedDriver.Database);
            Assert.Equal(gid, scopedClients.Driver.Database);

            // And the shared driver must never have been mutated to this or any group.
            Assert.Same(baseDriver, graphiti.Driver);
            Assert.Equal("default_db", graphiti.Driver.Database);
            observations[gid] = scopedDriver.Database;
        }

        await LoopGuard.RunAsync(async () =>
        {
            await Task.WhenAll(Worker("tenant_a", hold: 0.02), Worker("tenant_b", hold: 0.0), Worker("tenant_c", hold: 0.01));
            return true;
        });

        Assert.Equal(3, observations.Count);
        Assert.Equal("tenant_a", observations["tenant_a"]);
        Assert.Equal("tenant_b", observations["tenant_b"]);
        Assert.Equal("tenant_c", observations["tenant_c"]);

        // Shared driver still points at the original default database.
        Assert.Equal("default_db", graphiti.Driver.Database);
        Assert.Equal("default_db", graphiti.Clients.Driver.Database);
    }

    /// <summary>Build a Graphiti instance around a FakeDriver, bypassing __init__ side effects.</summary>
    private static (GraphitiCore Graphiti, RequestScopeFakeDriver Driver) MakeGraphiti(string database = "default_db")
    {
        var driver = new RequestScopeFakeDriver(database: database);
        var clients = new GraphitiClients
        {
            Driver = driver,
            LlmClient = new UnusedLlmClient(),
            Embedder = new UnusedEmbedder(),
            CrossEncoder = new UnusedCrossEncoder(),
            Tracer = new NoOpTracer(),
        };
        var graphiti = (GraphitiCore)RuntimeHelpers.GetUninitializedObject(typeof(GraphitiCore));
        graphiti.Driver = driver;
        graphiti.Clients = clients;
        return (graphiti, driver);
    }
}
