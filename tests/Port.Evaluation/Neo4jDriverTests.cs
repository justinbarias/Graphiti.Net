// Tests of graphiti_core/driver/neo4j_driver.py against the local Neo4j (plan 0004, layer 5, packet P5a).
// The expected values come from the Python Neo4jDriver at the pinned commit, run against the same database on
// 2026-11-27 (neo4j 6 Python driver). Each test uses its own group id and touches only the data of that group.
// PORT: the packet brief puts these tests in tests/Graphiti.Net.E2E.Tests, but that project cannot see the internal
// types of the product projects (port/RULES.md, R3). Only the lead changes the InternalsVisibleTo lists.
namespace Graphiti.Port.Evaluation;

using System.Numerics;
using global::Neo4j.Driver;
using Graphiti.Core.Driver;
using Graphiti.Internal;
using Graphiti.Neo4j;
using Graphiti.Neo4j.Operations;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

public sealed class Neo4jDriverTests : IAsyncLifetime
{
    private static readonly string Uri = Environment.GetEnvironmentVariable("NEO4J_URI") ?? "bolt://localhost:7687";
    private static readonly string User = Environment.GetEnvironmentVariable("NEO4J_USER") ?? "neo4j";
    private static readonly string Password = Environment.GetEnvironmentVariable("NEO4J_PASSWORD") ?? "password";

    private readonly string group = $"e2e_{Guid.NewGuid():N}";
    private readonly Neo4jDriver driver = new(Uri, User, Password);

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await Asyncio.Run(() => driver.ExecuteQueryAsync(
            "MATCH (n:DriverProbe {group_id: $g}) DETACH DELETE n",
            new() { ["g"] = group },
            CancellationToken.None));
        await driver.CloseAsync(CancellationToken.None);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<object?> RoundTrip(object? value)
    {
        var (records, _, _) = await Asyncio.Run(() => driver.ExecuteQueryAsync("RETURN $v AS v", new() { ["v"] = value }, Ct));
        return Assert.Single(records)["v"];
    }

    private async Task<long> CountProbes()
    {
        var (records, _, _) = await Asyncio.Run(() => driver.ExecuteQueryAsync(
            "MATCH (n:DriverProbe {group_id: $g}) RETURN count(n) AS c", new() { ["g"] = group }, Ct));
        return Assert.IsType<long>(records[0]["c"]);
    }

    [Fact]
    public async Task Each_scalar_value_type_makes_the_round_trip_with_its_type()
    {
        Assert.Null(await RoundTrip(null));
        Assert.True(Assert.IsType<bool>(await RoundTrip(true)));
        Assert.False(Assert.IsType<bool>(await RoundTrip(false)));
        Assert.Equal(-7L, Assert.IsType<long>(await RoundTrip(-7L)));
        Assert.Equal(2.5, Assert.IsType<double>(await RoundTrip(2.5)));
        Assert.Equal("é𝄞", Assert.IsType<string>(await RoundTrip("é𝄞")));
        Assert.Equal(new byte[] { 1, 0, 255 }, Assert.IsType<byte[]>(await RoundTrip(new byte[] { 1, 0, 255 })));
        Assert.Equal(new DateOnly(2024, 2, 29), Assert.IsType<DateOnly>(await RoundTrip(new DateOnly(2024, 2, 29))));

        // Python: 2024-05-06 07:08:09.123456+05:30 comes back with its offset.
        var aware = Assert.IsType<PyDateTime>(await RoundTrip(new PyDateTime(2024, 5, 6, 7, 8, 9, 123456, 19_800_000_000L)));
        Assert.Equal("2024-05-06T07:08:09.123456+05:30", aware.IsoFormat());

        // Python: a naive datetime comes back naive.
        var naive = Assert.IsType<PyDateTime>(await RoundTrip(new PyDateTime(2024, 1, 2, 3, 4, 5, 6)));
        Assert.True(naive.IsNaive);
        Assert.Equal("2024-01-02T03:04:05.000006", naive.IsoFormat());

        // A UTC value keeps the offset 0.
        var utc = Assert.IsType<PyDateTime>(await RoundTrip(new PyDateTime(2025, 12, 31, 23, 59, 59, 999999, 0)));
        Assert.Equal("2025-12-31T23:59:59.999999+00:00", utc.IsoFormat());

        // A BigInteger or a ulong that a long holds becomes a long.
        Assert.Equal(long.MaxValue, Assert.IsType<long>(await RoundTrip(new BigInteger(long.MaxValue))));
        Assert.Equal(5L, Assert.IsType<long>(await RoundTrip(5UL)));
    }

    [Fact]
    public async Task Values_inside_a_list_and_a_dict_make_the_round_trip_at_each_depth()
    {
        // Python: {'k': [1, 2.5, 'é', None, True, {'n': b'\x01'}]} comes back equal.
        var value = new OrderedDictionary<string, object?>
        {
            ["k"] = new List<object?> { 1L, 2.5, "é", null, true, new OrderedDictionary<string, object?> { ["n"] = new byte[] { 1 } } },
            ["t"] = new PyTuple([1L, "a"]),
            ["s"] = new PySet<string>(["x", "y"]),
            ["d"] = new List<object?> { new PyDateTime(2024, 5, 6, 7, 8, 9, 0, 0), new DateOnly(2020, 1, 1) },
        };
        var pyDict = new PyDict();
        pyDict["inner"] = new List<double> { 0.5, 1.5 };
        value["p"] = pyDict;

        var back = Assert.IsType<OrderedDictionary<string, object?>>(await RoundTrip(value));
        var k = Assert.IsType<List<object?>>(back["k"]);
        Assert.Equal(1L, Assert.IsType<long>(k[0]));
        Assert.Equal(2.5, Assert.IsType<double>(k[1]));
        Assert.Equal("é", k[2]);
        Assert.Null(k[3]);
        Assert.Equal(true, k[4]);
        var inner = Assert.IsType<OrderedDictionary<string, object?>>(k[5]);
        Assert.Equal(new byte[] { 1 }, Assert.IsType<byte[]>(inner["n"]));
        Assert.Equal(new List<object?> { 1L, "a" }, Assert.IsType<List<object?>>(back["t"]));
        Assert.Equal(new List<object?> { "x", "y" }, Assert.IsType<List<object?>>(back["s"]));
        var dates = Assert.IsType<List<object?>>(back["d"]);
        Assert.Equal("2024-05-06T07:08:09+00:00", Assert.IsType<PyDateTime>(dates[0]).IsoFormat());
        Assert.Equal(new DateOnly(2020, 1, 1), Assert.IsType<DateOnly>(dates[1]));
        var p = Assert.IsType<OrderedDictionary<string, object?>>(back["p"]);
        Assert.Equal(new List<object?> { 0.5, 1.5 }, Assert.IsType<List<object?>>(p["inner"]));
    }

    [Fact]
    public async Task Driver_temporal_values_convert_as_to_native_does()
    {
        var (records, _, _) = await Asyncio.Run(() => driver.ExecuteQueryAsync(
            "RETURN localdatetime('2024-01-02T03:04:05.123456789') AS l, " +
            "datetime({year: 2024, month: 1, day: 15, hour: 12, timezone: 'Europe/Berlin'}) AS z, " +
            "date('2024-03-01') AS d",
            new(),
            Ct));

        // Nanoseconds are cut, not rounded.
        var local = Assert.IsType<PyDateTime>(records[0]["l"]);
        Assert.Equal("2024-01-02T03:04:05.123456", local.IsoFormat());
        Assert.True(local.IsNaive);

        // A named zone keeps only its offset.
        Assert.Equal("2024-01-15T12:00:00+01:00", Assert.IsType<PyDateTime>(records[0]["z"]).IsoFormat());
        Assert.Equal(new DateOnly(2024, 3, 1), Assert.IsType<DateOnly>(records[0]["d"]));
    }

    [Theory]
    [InlineData("RETURN duration('P1D') AS v")]
    [InlineData("RETURN point({x: 1.0, y: 2.0}) AS v")]
    [InlineData("RETURN localtime('12:00') AS v")]
    public async Task A_driver_type_outside_the_value_model_throws(string query)
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => Asyncio.Run(() => driver.ExecuteQueryAsync(query, new(), Ct)));
    }

    [Fact]
    public async Task A_value_that_the_driver_cannot_hold_throws_as_the_Python_packer_does()
    {
        // Python: OverflowError, TypeError, and ValueError.
        await Assert.ThrowsAsync<OverflowException>(() => RoundTrip(BigInteger.Pow(2, 70)));
        await Assert.ThrowsAsync<OverflowException>(() => RoundTrip(ulong.MaxValue));
        var pyDict = new PyDict();
        pyDict[1L] = "a";
        await Assert.ThrowsAsync<InvalidCastException>(() => RoundTrip(pyDict));
        await Assert.ThrowsAsync<ArgumentException>(() => RoundTrip(GraphProvider.Neo4j));
    }

    [Fact]
    public async Task Execute_query_merges_params_with_the_other_entries_and_reads_the_driver_options()
    {
        // Python: execute_query('RETURN $a AS a, $b AS b, $c AS c', params={'a': 1, 'b': 2}, b=3, c=None)
        // gives [{'a': 1, 'b': 3, 'c': None}] with the keys ['a', 'b', 'c'].
        var parameters = new OrderedDictionary<string, object?>
        {
            ["params"] = new OrderedDictionary<string, object?> { ["a"] = 1L, ["b"] = 2L },
            ["b"] = 3L,
            ["c"] = null,
            ["routing_"] = "r",
            ["database_"] = null,
        };
        var (records, summary, keys) = await Asyncio.Run(() => driver.ExecuteQueryAsync("RETURN $a AS a, $b AS b, $c AS c", parameters, Ct));

        var record = Assert.Single(records);
        Assert.Equal(["a", "b", "c"], record.Keys);
        Assert.Equal(1L, record["a"]);
        Assert.Equal(3L, record["b"]);
        Assert.Null(record["c"]);
        Assert.Equal(["a", "b", "c"], Assert.IsType<List<string>>(keys));
        Assert.IsAssignableFrom<IResultSummary>(summary);

        // The caller's dictionary stays as it is.
        Assert.Equal(["params", "b", "c", "routing_", "database_"], parameters.Keys);
    }

    [Fact]
    public async Task Execute_query_with_an_empty_result_gives_no_record_and_the_keys()
    {
        // Python: [] ['uuid']
        var (records, _, keys) = await Asyncio.Run(() => driver.ExecuteQueryAsync(
            "MATCH (n:DriverProbe {group_id: $g}) RETURN n.uuid AS uuid", new() { ["g"] = group }, Ct));
        Assert.Empty(records);
        Assert.Equal(["uuid"], Assert.IsType<List<string>>(keys));
    }

    [Fact]
    public async Task Execute_query_logs_a_query_error_with_the_sorted_parameter_keys_and_throws_the_driver_error()
    {
        var lines = new List<(LogLevel, string)>();
        using var factory = new CaptureFactory(lines);
        using var scope = PortServices.Use(loggerFactory: factory);

        var parameters = new OrderedDictionary<string, object?>
        {
            ["params"] = new OrderedDictionary<string, object?> { ["a"] = 1L },
            ["zeta"] = 2L,
            ["routing_"] = "r",
        };
        var error = await Assert.ThrowsAsync<ClientException>(() => Asyncio.Run(() => driver.ExecuteQueryAsync("RETURN $a +", parameters, Ct)));
        Assert.Equal("Neo.ClientError.Statement.SyntaxError", error.Code);

        // Python: the second and third lines are "RETURN $a +" and "parameter keys: ['a', 'routing_', 'zeta']".
        var (level, text) = Assert.Single(lines);
        Assert.Equal(LogLevel.Error, level);
        Assert.Equal("Error executing Neo4j query: " + error.Message + "\nRETURN $a +\nparameter keys: ['a', 'routing_', 'zeta']", text);
    }

    [Fact]
    public async Task Execute_query_with_an_unknown_routing_value_logs_and_throws_a_ValueError()
    {
        var lines = new List<(LogLevel, string)>();
        using var factory = new CaptureFactory(lines);
        using var scope = PortServices.Use(loggerFactory: factory);

        // Python: ValueError "Invalid routing control value: 'x'", logged with "parameter keys: ['routing_']".
        var error = await Assert.ThrowsAsync<ArgumentException>(() => Asyncio.Run(() => driver.ExecuteQueryAsync(
            "RETURN 1", new() { ["routing_"] = "x" }, Ct)));
        Assert.Equal("Invalid routing control value: 'x'", error.Message);
        Assert.Equal((LogLevel.Error, "Error executing Neo4j query: Invalid routing control value: 'x'\nRETURN 1\nparameter keys: ['routing_']"), Assert.Single(lines));
    }

    [Fact]
    public async Task Execute_query_with_a_canceled_token_throws_OperationCanceledException()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Asyncio.Run(() => driver.ExecuteQueryAsync("RETURN 1", new(), source.Token)));
    }

    [Fact]
    public async Task A_transaction_commits_when_its_body_ends()
    {
        await Asyncio.Run(() => driver.TransactionAsync(
            async tx =>
            {
                await tx.RunAsync("CREATE (:DriverProbe {group_id: $g, created_at: $t})", new() { ["g"] = group, ["t"] = new PyDateTime(2024, 1, 1, 0, 0, 0, 0, 0) }, Ct);
                await tx.RunAsync("CREATE (:DriverProbe {group_id: $g})", new() { ["g"] = group }, Ct);
            },
            Ct));

        Assert.Equal(2L, await CountProbes());
    }

    [Fact]
    public async Task A_transaction_rolls_back_when_its_body_throws()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Asyncio.Run(() => driver.TransactionAsync(
            async tx =>
            {
                await tx.RunAsync("CREATE (:DriverProbe {group_id: $g})", new() { ["g"] = group }, Ct);
                throw new InvalidOperationException("body failed");
            },
            Ct)));
        Assert.Equal("body failed", error.Message);
        Assert.Equal(0L, await CountProbes());
    }

    [Fact]
    public async Task A_transaction_rolls_back_when_its_body_is_canceled()
    {
        using var source = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Asyncio.Run(() => driver.TransactionAsync(
            async tx =>
            {
                await tx.RunAsync("CREATE (:DriverProbe {group_id: $g})", new() { ["g"] = group }, Ct);
                await source.CancelAsync();
                source.Token.ThrowIfCancellationRequested();
            },
            source.Token)));
        Assert.Equal(0L, await CountProbes());
    }

    [Fact]
    public async Task A_session_runs_a_query_and_a_write_function_and_closes()
    {
        await Asyncio.Run(async () =>
        {
            var session = driver.Session();
            await session.RunAsync("CREATE (:DriverProbe {group_id: $g, kind: 'run'})", new() { ["g"] = group }, Ct);
            await session.ExecuteWriteAsync(
                async tx =>
                {
                    Assert.True(Asyncio.IsOnLoop);
                    await tx.RunAsync("CREATE (:DriverProbe {group_id: $g, kind: $k})", new() { ["g"] = group, ["k"] = "write" }, Ct);
                },
                Ct);
            await session.CloseAsync(Ct);
        });

        await using (var second = driver.Session(string.Empty))
        {
            await Asyncio.Run(() => second.RunAsync("CREATE (:DriverProbe {group_id: $g, kind: 'dispose'})", new() { ["g"] = group }, Ct));
        }

        var (records, _, _) = await Asyncio.Run(() => driver.ExecuteQueryAsync(
            "MATCH (n:DriverProbe {group_id: $g}) RETURN n.kind AS kind ORDER BY kind", new() { ["g"] = group }, Ct));
        Assert.Equal(["dispose", "run", "write"], records.Select(r => r["kind"]));
    }

    [Fact]
    public async Task Build_indices_and_constraints_two_times_gives_the_same_indexes()
    {
        const string show = "SHOW INDEXES YIELD name, type, entityType, labelsOrTypes, properties RETURN name, type, entityType, labelsOrTypes, properties ORDER BY name";
        var expected = new[] { "entity_uuid", "episode_uuid", "relation_uuid", "node_name_and_summary", "edge_name_and_fact", "episode_content", "community_name" };

        await Asyncio.Run(() => driver.BuildIndicesAndConstraintsAsync(cancellationToken: Ct));
        var (first, _, _) = await Asyncio.Run(() => driver.ExecuteQueryAsync(show, new(), Ct));
        await Asyncio.Run(() => driver.BuildIndicesAndConstraintsAsync(false, Ct));
        var (second, _, _) = await Asyncio.Run(() => driver.ExecuteQueryAsync(show, new(), Ct));

        // Other tests can add their own indexes at the same time, so compare the indexes of the build.
        var names = new HashSet<string>(GraphitiIndexNames());
        Assert.Superset(new HashSet<string>(expected), names);
        string Row(OrderedDictionary<string, object?> r) => Py.Repr(r);
        var a = first.Where(r => names.Contains((string)r["name"]!)).Select(Row).ToList();
        var b = second.Where(r => names.Contains((string)r["name"]!)).Select(Row).ToList();
        Assert.Equal(names.Count, a.Count);
        Assert.Equal(a, b);
    }

    private static IEnumerable<string> GraphitiIndexNames() =>
        Core.GraphQueries.GetRangeIndices(GraphProvider.Neo4j)
            .Concat(Core.GraphQueries.GetFulltextIndices(GraphProvider.Neo4j))
            .Select(q => q.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Select(words => words[Array.IndexOf(words, "INDEX") + 1]);

    [Fact]
    public async Task Health_check_passes_for_the_database_and_prints_the_failure_for_a_closed_port()
    {
        await Asyncio.Run(() => driver.HealthCheckAsync(Ct));

        var broken = new Neo4jDriver("bolt://localhost:1", User, Password);
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);
        try
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() => Asyncio.Run(() => broken.HealthCheckAsync(Ct)));
            Assert.Equal("Neo4j health check failed: " + error.Message + Environment.NewLine, output.ToString());
        }
        finally
        {
            Console.SetOut(original);
            await broken.CloseAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void The_operations_properties_give_the_Neo4j_operations_one_object_each()
    {
        Assert.Equal(GraphProvider.Neo4j, driver.Provider);
        Assert.Equal("neo4j", driver.Database);
        Assert.Equal(string.Empty, driver.DefaultGroupId);
        Assert.IsType<Neo4jEntityNodeOperations>(driver.EntityNodeOps);
        Assert.IsType<Neo4jEpisodeNodeOperations>(driver.EpisodeNodeOps);
        Assert.IsType<Neo4jCommunityNodeOperations>(driver.CommunityNodeOps);
        Assert.IsType<Neo4jSagaNodeOperations>(driver.SagaNodeOps);
        Assert.IsType<Neo4jEntityEdgeOperations>(driver.EntityEdgeOps);
        Assert.IsType<Neo4jEpisodicEdgeOperations>(driver.EpisodicEdgeOps);
        Assert.IsType<Neo4jCommunityEdgeOperations>(driver.CommunityEdgeOps);
        Assert.IsType<Neo4jHasEpisodeEdgeOperations>(driver.HasEpisodeEdgeOps);
        Assert.IsType<Neo4jNextEpisodeEdgeOperations>(driver.NextEpisodeEdgeOps);
        Assert.IsType<Neo4jSearchOperations>(driver.SearchOps);
        Assert.IsType<Neo4jGraphMaintenanceOperations>(driver.GraphOps);
        Assert.Same(driver.EntityNodeOps, driver.EntityNodeOps);
        Assert.Same(driver.SearchOps, driver.SearchOps);
    }

    [Fact]
    public async Task Delete_all_indexes_sends_the_query_text_of_Python()
    {
        // A fake client, so that no index of the shared database is dropped.
        var client = new RecordingClient();
        var fake = new Neo4jDriver(client, "graphiti");
        var lines = new List<(LogLevel, string)>();
        using var factory = new CaptureFactory(lines);
        using var scope = PortServices.Use(loggerFactory: factory);

        await Assert.ThrowsAsync<ClientException>(() => Asyncio.Run(() => fake.DeleteAllIndexesAsync(Ct)));

        // Python: neo4j_driver.py:208.
        Assert.Equal(["CALL db.indexes() YIELD name DROP INDEX name"], client.Queries);
        Assert.Equal((LogLevel.Error, "Error executing Neo4j query: recorded\nCALL db.indexes() YIELD name DROP INDEX name\nparameter keys: []"), Assert.Single(lines));
        Assert.Equal("graphiti", fake.Database);
    }

    private sealed class RecordingClient : IDriver
    {
        internal List<string> Queries { get; } = [];

        public Config Config => throw new NotSupportedException();

        public bool Encrypted => false;

        public IAsyncSession AsyncSession() => throw new NotSupportedException();

        public IAsyncSession AsyncSession(Action<SessionConfigBuilder> action) => throw new NotSupportedException();

        public Task<IServerInfo> GetServerInfoAsync() => throw new NotSupportedException();

        public Task<bool> TryVerifyConnectivityAsync() => throw new NotSupportedException();

        public Task VerifyConnectivityAsync() => throw new NotSupportedException();

        public Task<bool> SupportsMultiDbAsync() => throw new NotSupportedException();

        public Task<bool> SupportsSessionAuthAsync() => throw new NotSupportedException();

        public IExecutableQuery<IRecord, IRecord> ExecutableQuery(string cypher)
        {
            Queries.Add(cypher);
            throw new ClientException("Neo.ClientError.Test", "recorded");
        }

        public Task<bool> VerifyAuthenticationAsync(IAuthToken authToken) => throw new NotSupportedException();

        public IBookmarkManager GetExecutableQueryBookmarkManager() => throw new NotSupportedException();

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class CaptureFactory(List<(LogLevel, string)> lines) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) =>
            categoryName.EndsWith("Neo4jDriverModule", StringComparison.Ordinal) ? new CaptureLogger(lines) : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<(LogLevel, string)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Add((logLevel, formatter(state, exception)));
        }
    }
}
