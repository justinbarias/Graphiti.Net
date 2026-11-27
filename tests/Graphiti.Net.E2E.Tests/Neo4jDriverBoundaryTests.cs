// Tests of graphiti_core/driver/neo4j_driver.py with a fake client at the network boundary (plan 0004, layer 5, P5a,
// review round). No database is used. The expected values come from tests/Graphiti.Net.E2E.Tests/values/neo4j_driver.json,
// which port/tools/record_neo4j_driver.py wrote by running the Python Neo4jDriver against a Python fake of the same
// boundary, or they are literals from the Python driver that the comment of the test names.
namespace Graphiti.E2E.Tests;

using System.Text;
using System.Text.Json;
using global::Neo4j.Driver;
using Graphiti.Core.Driver;
using Graphiti.Internal;
using Graphiti.Neo4j;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

[Collection(ConsoleOutputGroup.Name)]
public sealed class Neo4jDriverBoundaryTests
{
    private static readonly JsonElement Values = JsonDocument.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "values", "neo4j_driver.json"), Encoding.UTF8)).RootElement;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static (Neo4jDriver Driver, FakeClient Client) Make(string database = "neo4j")
    {
        var client = new FakeClient();
        return (new Neo4jDriver(client, database), client);
    }

    private static List<string> Strings(JsonElement array) => [.. array.EnumerateArray().Select(e => e.GetString()!)];

    private static object? Plain(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetInt64(),
        JsonValueKind.Object => value.EnumerateObject().ToDictionary(p => p.Name, p => Plain(p.Value)),
        _ => throw new NotSupportedException(value.ValueKind.ToString()),
    };

    private static LogLevel Level(string name) => name switch
    {
        "ERROR" => LogLevel.Error,
        "DEBUG" => LogLevel.Debug,
        _ => throw new NotSupportedException(name),
    };

    private static List<(LogLevel, string)> Lines(JsonElement log) =>
        [.. log.EnumerateArray().Select(e => (Level(e[0].GetString()!), e[1].GetString()!))];

    [Theory]
    [InlineData("default", false)]
    [InlineData("delete_existing", true)]
    public async Task Build_indices_and_constraints_sends_the_queries_of_Python_in_its_order(string name, bool deleteExisting)
    {
        // Python: get_range_indices(NEO4J) + get_fulltext_indices(NEO4J), and the delete query first when delete_existing.
        var (fake, client) = Make();

        await Asyncio.Run(() => fake.BuildIndicesAndConstraintsAsync(deleteExisting, Ct));

        Assert.Equal(Strings(Values.GetProperty("index_queries").GetProperty(name)), client.Executed.Select(e => e.Query));
    }

    [Fact]
    public async Task Execute_query_passes_the_parameters_the_database_and_the_routing_as_Python_does()
    {
        var inputs = new Dictionary<string, OrderedDictionary<string, object?>>
        {
            ["plain"] = new(),
            ["params"] = new() { ["params"] = new OrderedDictionary<string, object?> { ["a"] = 1L } },
            ["merge"] = new() { ["params"] = new OrderedDictionary<string, object?> { ["a"] = 1L, ["b"] = 2L }, ["b"] = 3L, ["c"] = null },
            ["database_none"] = new() { ["database_"] = null },
            ["database_named"] = new() { ["database_"] = "other" },
            ["routing_r"] = new() { ["routing_"] = "r" },
            ["routing_w"] = new() { ["routing_"] = "w" },
        };

        foreach (var recorded in Values.GetProperty("execute_calls").EnumerateArray())
        {
            var name = recorded.GetProperty("name").GetString()!;
            var (fake, client) = Make();

            await Asyncio.Run(() => fake.ExecuteQueryAsync(recorded.GetProperty("query").GetString()!, inputs[name], Ct));

            var call = Assert.Single(client.Executed);

            // The client of Python merges parameters_ with the other keyword arguments, except routing_.
            var expected = (Dictionary<string, object?>)Plain(recorded.GetProperty("parameters"))!;
            var routing = RoutingControl.Writers;
            foreach (var other in recorded.GetProperty("other").EnumerateObject())
            {
                if (other.Name == "routing_")
                {
                    routing = other.Value.GetString() == "r" ? RoutingControl.Readers : RoutingControl.Writers;
                }
                else
                {
                    expected[other.Name] = Plain(other.Value);
                }
            }

            Assert.True(call.Parameters.Count == expected.Count && expected.All(kv => call.Parameters.TryGetValue(kv.Key, out var v) && Equals(v, kv.Value)), name);
            Assert.Equal(recorded.GetProperty("database").GetString(), call.Config.Database);
            Assert.Equal(routing, call.Config.Routing);
        }
    }

    [Fact]
    public async Task An_index_race_gives_no_result_and_one_debug_line_and_another_error_passes_on()
    {
        // Python: _execute_index_query ignores a ClientError whose text holds EquivalentSchemaRuleAlreadyExists and logs
        // the first 50 characters of the query. Another ClientError passes on.
        var race = Values.GetProperty("index_race").GetProperty("equivalent_exists");
        var lines = new List<(LogLevel, string)>();
        using var factory = new CaptureFactory(lines);
        using var scope = PortServices.Use(loggerFactory: factory);

        var (fake, client) = Make();
        client.Fail["CREATE INDEX entity_uuid IF NOT EXISTS"] = new ClientException("Neo.ClientError.Schema.EquivalentSchemaRuleAlreadyExists", "An equivalent index already exists");
        await Asyncio.Run(() => fake.BuildIndicesAndConstraintsAsync(false, Ct));

        Assert.Equal(Lines(race.GetProperty("log")), lines);
        Assert.Equal(Strings(race.GetProperty("queries")), client.Executed.Select(e => e.Query).Order(StringComparer.Ordinal));

        var otherRace = Values.GetProperty("index_race").GetProperty("other_code");
        Assert.Equal("ClientError", otherRace.GetProperty("raised").GetProperty("type").GetString());
        lines.Clear();
        var (other, otherClient) = Make();
        otherClient.Fail["CREATE INDEX entity_uuid IF NOT EXISTS"] = new ClientException("Neo.ClientError.Schema.IndexWithNameAlreadyExists", "There already exists an index called entity_uuid");
        var error = await Assert.ThrowsAsync<ClientException>(() => Asyncio.Run(() => other.BuildIndicesAndConstraintsAsync(false, Ct)));
        Assert.Equal("Neo.ClientError.Schema.IndexWithNameAlreadyExists", error.Code);
        Assert.Equal(Lines(otherRace.GetProperty("log")), lines);
    }

    [Fact]
    public async Task The_error_line_of_a_failed_query_holds_str_of_the_driver_error()
    {
        var recorded = Values.GetProperty("error_texts")[0];
        var lines = new List<(LogLevel, string)>();
        using var factory = new CaptureFactory(lines);
        using var scope = PortServices.Use(loggerFactory: factory);
        var (fake, client) = Make();
        client.Fail["RETURN"] = new ClientException("Neo.ClientError.Test", "recorded");

        await Assert.ThrowsAsync<ClientException>(() => Asyncio.Run(() => fake.ExecuteQueryAsync(
            "RETURN 1",
            new() { ["params"] = new OrderedDictionary<string, object?> { ["a"] = 1L }, ["zeta"] = 2L, ["routing_"] = "r" },
            Ct)));

        Assert.Equal(Lines(recorded.GetProperty("log")), lines);
    }

    [Fact]
    public async Task The_health_check_prints_str_of_the_error_and_throws_it_again()
    {
        foreach (var recorded in Values.GetProperty("health_check").EnumerateArray())
        {
            var name = recorded.GetProperty("name").GetString()!;
            var (fake, client) = Make();
            client.VerifyError = name switch
            {
                "server_error" => new ClientException("Neo.ClientError.Security.Unauthorized", "The client is unauthorized."),
                "driver_error" => new ServiceUnavailableException("Unable to connect"),
                _ => null,
            };

            var output = new StringWriter();
            var original = Console.Out;
            Console.SetOut(output);
            try
            {
                if (client.VerifyError is null)
                {
                    await Asyncio.Run(() => fake.HealthCheckAsync(Ct));
                }
                else
                {
                    await Assert.ThrowsAsync(client.VerifyError.GetType(), () => Asyncio.Run(() => fake.HealthCheckAsync(Ct)));
                }
            }
            finally
            {
                Console.SetOut(original);
            }

            Assert.Equal(recorded.GetProperty("printed").GetString(), output.ToString().Replace(Environment.NewLine, "\n", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task A_transaction_calls_the_driver_in_the_order_of_Python_for_a_commit_a_failing_body_and_a_canceled_body()
    {
        // Python: neo4j_driver.py lines 151 to 158 (`async with self.client.session(...) as session`,
        // `begin_transaction`, `yield`, `commit`, and in `except BaseException` the `rollback`, then `__aexit__`, which is
        // `close`). Values: the oracle, and values/neo4j_driver.json (transactions).
        await Check("commit", ["session(database=neo4j)", "begin_transaction", "run", "commit", "close"], null, _ => Task.CompletedTask, Ct);
        await Check("body_throws", ["session(database=neo4j)", "begin_transaction", "run", "rollback", "close"], typeof(InvalidOperationException), _ => throw new InvalidOperationException("body failed"), Ct);
        using var source = new CancellationTokenSource();
        await Check(
            "body_canceled",
            ["session(database=neo4j)", "begin_transaction", "run", "rollback", "close"],
            typeof(OperationCanceledException),
            async _ =>
            {
                await source.CancelAsync();
                source.Token.ThrowIfCancellationRequested();
            },
            source.Token);

        static async Task Check(string name, string[] pythonOrder, Type? errorType, Func<Transaction, Task> after, CancellationToken token)
        {
            Assert.Equal(pythonOrder, Strings(Values.GetProperty("transactions").GetProperty(name).GetProperty("calls")));
            var (fake, client) = Make();
            var run = Asyncio.Run(() => fake.TransactionAsync(
                async tx =>
                {
                    await tx.RunAsync("Q", new() { ["a"] = 1L }, Ct);
                    await after(tx);
                },
                token));
            if (errorType is null)
            {
                await run;
            }
            else
            {
                var error = await Assert.ThrowsAnyAsync<Exception>(() => run);
                Assert.IsAssignableFrom(errorType, error);
            }

            Assert.Equal(pythonOrder, client.Calls.Names);
        }
    }

    [Fact]
    public async Task The_session_and_the_database_of_a_call_follow_Python()
    {
        var sessions = Values.GetProperty("sessions");

        var (defaultDriver, defaultClient) = Make();
        _ = defaultDriver.Session();
        _ = defaultDriver.Session("other");
        _ = defaultDriver.Session(string.Empty);
        _ = defaultDriver.Session(null);
        Assert.Equal(Strings(sessions.GetProperty("default_driver")), defaultClient.Calls.Names);

        var (named, namedClient) = Make("graphiti");
        _ = named.Session();
        _ = named.Session("other");
        await Asyncio.Run(() => named.TransactionAsync(_ => Task.CompletedTask, Ct));
        Assert.Equal(Strings(sessions.GetProperty("named_driver")), namedClient.Calls.Names);

        var (closing, closingClient) = Make();
        await Asyncio.Run(() => closing.CloseAsync(Ct));
        Assert.Equal(Strings(sessions.GetProperty("driver_close")), closingClient.Calls.Names);
    }

    [Fact]
    public async Task A_session_closes_and_drops_the_error_of_a_pending_result()
    {
        // Python: AsyncSession.close and __aexit__ record one close, and they drop the error of the open result
        // (neo4j/_async/work/session.py, lines 196 to 203).
        var calls = new FakeCalls();
        var client = new FakeClient(calls);
        var fake = new Neo4jDriver(client);
        var failure = new ClientException("Neo.ClientError.Statement.ArithmeticError", "/ by zero");

        client.SessionFactory = () => new FakeSession(calls);
        var closed = fake.Session();
        await Asyncio.Run(() => closed.CloseAsync(Ct));
        Assert.Equal(["session(database=neo4j)", "close"], calls.Names);

        calls.Names.Clear();
        client.SessionFactory = () => new FakeSession(calls);
        await Asyncio.Run(async () => await fake.Session().DisposeAsync());
        Assert.Equal(["session(database=neo4j)", "close"], calls.Names);

        calls.Names.Clear();
        client.SessionFactory = () => new FakeSession(calls) { CloseError = failure };
        await Asyncio.Run(() => fake.Session().CloseAsync(Ct));
        await Asyncio.Run(async () => await fake.Session().DisposeAsync());
        Assert.Equal(["session(database=neo4j)", "close", "session(database=neo4j)", "close"], calls.Names);
    }

    [Fact]
    public async Task A_session_run_waits_for_the_driver_call()
    {
        var (fake, client) = Make();
        await Asyncio.Run(async () =>
        {
            var session = fake.Session();
            await session.RunAsync("Q", new() { ["a"] = 1L }, Ct);
            Assert.Equal(["session(database=neo4j)", "run"], client.Calls.Names);
            await session.ExecuteWriteAsync(tx => tx.RunAsync("Q", new(), Ct), Ct);
            Assert.Equal(["session(database=neo4j)", "run", "execute_write", "run"], client.Calls.Names);
        });
    }

    [Fact]
    public async Task Each_async_method_throws_OperationCanceledException_for_a_canceled_token_and_calls_no_driver()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var token = source.Token;
        var (fake, client) = Make();
        var transaction = new Neo4jTransaction(new FakeTransaction(client.Calls));
        var session = new Neo4jSession(new FakeSession(client.Calls));
        var output = new StringWriter();
        var original = Console.Out;
        Console.SetOut(output);

        var cases = new Dictionary<string, Func<Task>>
        {
            ["TransactionAsync"] = () => fake.TransactionAsync(_ => Task.CompletedTask, token),
            ["ExecuteQueryAsync"] = () => fake.ExecuteQueryAsync("RETURN 1", new(), token),
            ["DeleteAllIndexesAsync"] = () => fake.DeleteAllIndexesAsync(token),
            ["BuildIndicesAndConstraintsAsync"] = () => fake.BuildIndicesAndConstraintsAsync(false, token),
            ["HealthCheckAsync"] = () => fake.HealthCheckAsync(token),
            ["Neo4jTransaction.RunAsync"] = () => transaction.RunAsync("Q", new(), token),
            ["Neo4jSession.RunAsync"] = () => session.RunAsync("Q", new(), token),
            ["Neo4jSession.ExecuteWriteAsync"] = () => session.ExecuteWriteAsync(_ => Task.CompletedTask, token),
        };
        try
        {
            foreach (var (name, call) in cases)
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Asyncio.Run(call));
                Assert.True(client.Calls.Names.Count == 0, name);
                Assert.Empty(client.Executed);
            }
        }
        finally
        {
            Console.SetOut(original);
        }

        // Python: a cancellation is not an Exception, so the health check prints nothing for it.
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task A_query_that_is_canceled_in_flight_throws_OperationCanceledException_and_logs_nothing()
    {
        // Python: `except Exception` does not catch CancelledError, so execute_query logs no line.
        var lines = new List<(LogLevel, string)>();
        using var factory = new CaptureFactory(lines);
        using var scope = PortServices.Use(loggerFactory: factory);
        var (fake, client) = Make();
        var started = new TaskCompletionSource();
        client.InFlight = async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
        };
        using var source = new CancellationTokenSource();

        var run = Asyncio.Run(() => fake.ExecuteQueryAsync("RETURN 1", new(), source.Token));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(30), Ct));
        Assert.Empty(lines);
    }

    [Fact]
    public async Task A_string_with_a_lone_surrogate_throws_at_each_depth_as_the_Python_packer_does()
    {
        // Python: execute_query('RETURN $v', params={'v': '\ud800'}) raises UnicodeEncodeError for the value, for a list
        // item, for a dict value, and for a dict key (the oracle, 2026-11-27). Nothing reaches the network.
        var lines = new List<(LogLevel, string)>();
        using var factory = new CaptureFactory(lines);
        using var scope = PortServices.Use(loggerFactory: factory);
        var (fake, client) = Make();
        var session = new Neo4jSession(new FakeSession(client.Calls));
        var transaction = new Neo4jTransaction(new FakeTransaction(client.Calls));

        object?[] values =
        [
            "\ud800",
            new List<object?> { 1L, "ok", "a\udc00b" },
            new OrderedDictionary<string, object?> { ["k"] = "\ud800" },
            new OrderedDictionary<string, object?> { ["\ud800"] = 1L },
            new Dictionary<object, object?> { ["\udfff"] = 1L },
        ];
        foreach (var value in values)
        {
            await Assert.ThrowsAsync<EncoderFallbackException>(() => Asyncio.Run(() => fake.ExecuteQueryAsync("RETURN $v", new() { ["v"] = value }, Ct)));
            await Assert.ThrowsAsync<EncoderFallbackException>(() => Asyncio.Run(() => transaction.RunAsync("RETURN $v", new() { ["v"] = value }, Ct)));
            await Assert.ThrowsAsync<EncoderFallbackException>(() => Asyncio.Run(() => session.RunAsync("RETURN $v", new() { ["v"] = value }, Ct)));
        }

        await Assert.ThrowsAsync<EncoderFallbackException>(() => Asyncio.Run(() => fake.ExecuteQueryAsync("RETURN 1", new() { ["params"] = new OrderedDictionary<string, object?> { ["\ud800"] = 1L } }, Ct)));
        Assert.Empty(client.Executed);
        Assert.Empty(client.Calls.Names);

        // Python logs the error line for execute_query, with the sorted parameter keys.
        Assert.All(lines, l => Assert.Equal(LogLevel.Error, l.Item1));
        Assert.StartsWith("Error executing Neo4j query: ", lines[0].Item2, StringComparison.Ordinal);
        Assert.EndsWith("\nRETURN $v\nparameter keys: ['v']", lines[0].Item2, StringComparison.Ordinal);
        Assert.Equal(values.Length + 1, lines.Count);

        // A pair of surrogates is a valid character.
        Assert.Equal("𝄞", Neo4jDriver.ToDriverValue("𝄞"));
    }

    [Fact]
    public void The_packer_errors_have_the_texts_of_Python()
    {
        // Python (the oracle, 2026-11-27, execute_query('RETURN $v', params={'v': ...})):
        //   {1: 'a'}              TypeError  Map keys must be strings, not <class 'int'>
        //   {b'x': 1}             TypeError  Map keys must be strings, not <class 'bytes'>
        //   GraphProvider.NEO4J   ValueError Values of type <enum 'GraphProvider'> are not supported
        //   2**70                 OverflowError Integer 1180591620717411303424 out of range
        var pyDict = new PyDict();
        pyDict[1L] = "a";
        Assert.Equal("Map keys must be strings, not <class 'int'>", Assert.Throws<InvalidCastException>(() => Neo4jDriver.ToDriverValue(pyDict)).Message);

        var bytesKey = new PyDict();
        bytesKey[new byte[] { 120 }] = 1L;
        Assert.Equal("Map keys must be strings, not <class 'bytes'>", Assert.Throws<InvalidCastException>(() => Neo4jDriver.ToDriverValue(bytesKey)).Message);

        // A dictionary that is not a PyDict gives the same type and text.
        var plain = new Dictionary<object, object?> { [1L] = "a" };
        Assert.Equal("Map keys must be strings, not <class 'int'>", Assert.Throws<InvalidCastException>(() => Neo4jDriver.ToDriverValue(plain)).Message);

        Assert.Equal("Values of type <enum 'GraphProvider'> are not supported", Assert.Throws<ArgumentException>(() => Neo4jDriver.ToDriverValue(GraphProvider.Neo4j)).Message);
        Assert.Equal("Integer 1180591620717411303424 out of range", Assert.Throws<OverflowException>(() => Neo4jDriver.ToDriverValue(System.Numerics.BigInteger.Pow(2, 70))).Message);
    }
}
