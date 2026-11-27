// Tests of the write with a transaction timeout (decision D52, open item O6): BulkUtils.ExecuteWriteWithTimeoutAsync
// (graphiti_core/utils/bulk_utils.py:178) and Neo4jSession.ExecuteWriteAsync with a timeout, against the local Neo4j.
// The expected values come from the Python driver (neo4j 6.1.0 in the oracle venv) against the same database
// (Neo4j 5.26.2) on 2026-11-27: `session.execute_write(unit_of_work(timeout=t)(work))`, with the BEGIN message that
// the driver logs at debug level. Each test uses its own group id and touches only the data of that group.
namespace Graphiti.E2E.Tests;

using System.Text.RegularExpressions;
using global::Neo4j.Driver;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.Neo4j;
using Graphiti.PyCompat;

[Collection(Neo4jFixtureGroup.Name)]
public sealed partial class WriteTimeoutOnNeo4jTests(Neo4jSidecar neo4j) : IAsyncLifetime
{
    private readonly string group = Neo4jSidecar.NewGroupId();
    private readonly BeginLog begins = new();
    private Neo4jDriver driver = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync()
    {
        var client = GraphDatabase.Driver(Neo4jSidecar.Uri, AuthTokens.Basic(Neo4jSidecar.User, Neo4jSidecar.Password), c => c.WithLogger(begins));
        driver = new Neo4jDriver(client);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await Asyncio.Run(() => driver.ExecuteQueryAsync(
            "MATCH (n:WriteTimeoutProbe {group_id: $g}) DETACH DELETE n",
            new() { ["g"] = group },
            CancellationToken.None));
        await driver.CloseAsync(CancellationToken.None);
    }

    public static TheoryData<double, long> SentTimeouts() => new()
    {
        // Python: the tx_timeout of BEGIN in milliseconds. round() rounds half to even, and a value in (0, 0.5 ms)
        // is 1. Neo4j.Driver 6.5.1 alone rounds up: 0.0025 s would be 3 and 0.0123456 s would be 13.
        { 0.0, 0 },
        { -0.0, 0 },
        { 1e-9, 1 },
        { 0.0004, 1 },
        { 0.0005, 1 },
        { 0.0006, 1 },
        { 0.0015, 2 },
        { 0.0025, 2 },
        { 0.0035, 4 },
        { 0.001, 1 },
        { 0.0123456, 12 },
        { 2.5, 2500 },
        { 7.5, 7500 },
        { 1e9, 1_000_000_000_000 },
    };

    public static TheoryData<double, string, string> RefusedTimeouts() => new()
    {
        // Python: the error of execute_write, raised before BEGIN (R4 maps the class).
        { -1.0, nameof(ArgumentException), "Timeout must be a positive number or 0." },
        { -1e-9, nameof(ArgumentException), "Timeout must be a positive number or 0." },
        { double.NegativeInfinity, nameof(ArgumentException), "Timeout must be a positive number or 0." },
        { double.NaN, nameof(ArgumentException), "cannot convert float NaN to integer" },
        { double.PositiveInfinity, nameof(OverflowException), "cannot convert float infinity to integer" },
    };

    [Theory]
    [MemberData(nameof(SentTimeouts))]
    public async Task The_session_sends_the_timeout_of_python_in_milliseconds(double timeout, long sent)
    {
        begins.Clear();
        await Asyncio.Run(async () =>
        {
            var session = driver.Session();
            try
            {
                await session.ExecuteWriteAsync(tx => tx.RunAsync("RETURN 1", new(), Ct), timeout, Ct);
            }
            catch (Neo4jException)
            {
                // The test reads the BEGIN message only. A limit of a few milliseconds can end the transaction before
                // its commit, in Python too, and the server then reports that it timed out.
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None);
            }
        });

        Assert.Equal([sent], begins.TxTimeouts());
    }

    [Theory]
    [MemberData(nameof(RefusedTimeouts))]
    public async Task The_session_refuses_the_timeout_that_python_refuses_and_sends_no_begin(double timeout, string error, string message)
    {
        begins.Clear();
        var ran = false;
        var e = await Assert.ThrowsAnyAsync<Exception>(() => Asyncio.Run(async () =>
        {
            var session = driver.Session();
            try
            {
                await session.ExecuteWriteAsync(
                    _ =>
                    {
                        ran = true;
                        return Task.CompletedTask;
                    },
                    timeout,
                    Ct);
            }
            finally
            {
                await session.CloseAsync(CancellationToken.None);
            }
        }));

        Assert.Equal(error, e.GetType().Name);
        Assert.Equal(message, e.Message);
        Assert.False(ran);
        Assert.Empty(begins.TxTimeouts());
    }

    [Fact]
    public async Task A_timeout_above_the_range_of_a_TimeSpan_throws_OverflowException()
    {
        // An open difference: Python sends 1e15 s as 1000000000000000000 ms. A TimeSpan holds at most
        // 922337203685477 ms, so the port throws OverflowException, the class of the error that Python raises above
        // 2**63 - 1 ms (1e300 s).
        begins.Clear();
        await Assert.ThrowsAsync<OverflowException>(() => Asyncio.Run(async () =>
        {
            await using var session = driver.Session();
            await session.ExecuteWriteAsync(_ => Task.CompletedTask, 1e15, Ct);
        }));
        Assert.Empty(begins.TxTimeouts());
    }

    [Fact]
    public async Task A_write_inside_the_limit_commits()
    {
        await Asyncio.Run(() => BulkUtils.ExecuteWriteWithTimeoutAsync(
            driver,
            30.0,
            tx => tx.RunAsync("CREATE (:WriteTimeoutProbe {group_id: $g, kind: 'inside'})", new() { ["g"] = group }, Ct),
            Ct));

        Assert.Equal([30_000L], begins.TxTimeouts());
        var (records, _, _) = await Asyncio.Run(() => driver.ExecuteQueryAsync(
            "MATCH (n:WriteTimeoutProbe {group_id: $g}) RETURN n.kind AS kind", new() { ["g"] = group }, Ct));
        Assert.Equal(["inside"], records.Select(r => r["kind"]));
    }

    [Fact]
    public async Task A_write_that_passes_the_limit_fails_with_the_error_of_the_server_and_leaves_no_node()
    {
        // The work waits for a lock that another transaction holds until the write has failed, so the server ends it
        // at the limit and the result does not depend on timing. Python: ClientError with this code after about one
        // second, is_retryable() False, so execute_write tries once, and no node of the write stays (the oracle,
        // 2026-11-27). Neo4j.Driver 6.5.1 throws ClientException with the same code and tries once too.
        // The lock holder lets go after the write has failed. If no limit reaches the server, it lets go after 30
        // seconds, and the write then commits, so the test fails and does not hang.
        await Asyncio.Run(() => driver.ExecuteQueryAsync(
            "CREATE (:WriteTimeoutProbe {group_id: $g, kind: 'lock'})", new() { ["g"] = group }, Ct));
        await using var holder = neo4j.Driver.AsyncSession();
        var held = await holder.BeginTransactionAsync();
        var failed = new TaskCompletionSource();
        var release = Task.Run(
            async () =>
            {
                await Task.WhenAny(failed.Task, Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None));
                await held.RollbackAsync();
            },
            CancellationToken.None);
        try
        {
            await (await held.RunAsync(
                "MATCH (n:WriteTimeoutProbe {group_id: $g, kind: 'lock'}) SET n.held = true",
                new Dictionary<string, object?> { ["g"] = group })).ConsumeAsync();

            var attempts = 0;
            begins.Clear();
            var error = await Assert.ThrowsAsync<ClientException>(() => Asyncio.Run(() => BulkUtils.ExecuteWriteWithTimeoutAsync(
                driver,
                1.0,
                async tx =>
                {
                    attempts++;
                    await tx.RunAsync("CREATE (:WriteTimeoutProbe {group_id: $g, kind: 'new'})", new() { ["g"] = group }, Ct);
                    await tx.RunAsync("MATCH (n:WriteTimeoutProbe {group_id: $g, kind: 'lock'}) SET n.x = 2", new() { ["g"] = group }, Ct);
                },
                Ct)));

            Assert.Equal("Neo.ClientError.Transaction.LockClientStopped", error.Code);
            Assert.False(error.IsRetriable);
            Assert.Equal(1, attempts);
            Assert.Equal([1000L], begins.TxTimeouts());
        }
        finally
        {
            failed.TrySetResult();
            await release;
        }

        var (records, _, _) = await Asyncio.Run(() => driver.ExecuteQueryAsync(
            "MATCH (n:WriteTimeoutProbe {group_id: $g}) RETURN n.kind AS kind", new() { ["g"] = group }, Ct));
        Assert.Equal(["lock"], records.Select(r => r["kind"]));
    }

    [Fact]
    public async Task The_write_with_a_timeout_throws_OperationCanceledException_for_a_canceled_token_and_calls_no_driver()
    {
        // A test with no Python original (R8): as the write with no timeout, the check comes before the driver call.
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var calls = new FakeCalls();
        var session = new Neo4jSession(new FakeSession(calls));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Asyncio.Run(() => session.ExecuteWriteAsync(_ => Task.CompletedTask, 7.5, source.Token)));
        Assert.Empty(calls.Names);
    }

    [GeneratedRegex(@"tx_timeout, (\d+)")]
    private static partial Regex TxTimeoutPattern();

    // The BEGIN messages that Neo4j.Driver logs at debug level. The driver logs from more than one thread.
    private sealed class BeginLog : INeo4jLogger
    {
        private readonly List<string> lines = [];

        public void Clear()
        {
            lock (lines)
            {
                lines.Clear();
            }
        }

        // The tx_timeout of each BEGIN, or -1 for a BEGIN with none.
        public List<long> TxTimeouts()
        {
            lock (lines)
            {
                return [.. lines.Select(l => TxTimeoutPattern().Match(l) is { Success: true } m ? long.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : -1)];
            }
        }

        public void Debug(string message, params object[] args) => Take(message, args);

        public void Trace(string message, params object[] args) => Take(message, args);

        public void Info(string message, params object[] args) => Take(message, args);

        public void Warn(Exception cause, string message, params object[] args)
        {
        }

        public void Error(Exception cause, string message, params object[] args)
        {
        }

        public bool IsDebugEnabled() => true;

        public bool IsTraceEnabled() => false;

        private void Take(string message, object[] args)
        {
            var text = args.Length == 0 ? message : string.Format(System.Globalization.CultureInfo.InvariantCulture, message, args);
            if (text.Contains("C: BEGIN", StringComparison.Ordinal))
            {
                lock (lines)
                {
                    lines.Add(text);
                }
            }
        }
    }
}
