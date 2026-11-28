// Fakes of the package Neo4j.Driver for the tests of graphiti_core/driver/neo4j_driver.py (plan 0004, layer 5, P5a).
// A fake exists only at the network boundary (port/RULES.md, R25): IDriver, IAsyncSession, IAsyncTransaction, and the
// executable query. Each fake method that returns a task runs `await Task.Yield();` first and then records its name,
// so that a call that the port does not await lands late and the comparison with Python fails.
namespace Graphiti.E2E.Tests;

using System.Reflection;
using global::Neo4j.Driver;
using Microsoft.Extensions.Logging;

/// <summary>
/// A slow call yields and then waits a few milliseconds. A call that the port does not await then lands after the
/// fast calls that follow it (the close), and the order of the recording shows it.
/// </summary>
internal static class Slow
{
    internal static async Task Pause()
    {
        await Task.Yield();
        await Task.Delay(5);
    }
}

/// <summary>One ordered list of the calls of the fakes. The names are the names that the Python recording uses.</summary>
internal sealed class FakeCalls
{
    internal List<string> Names { get; } = [];
}

internal sealed record ExecutedQuery(string Query, Dictionary<string, object?> Parameters, QueryConfig Config);

internal sealed class FakeClient : IDriver
{
    private readonly FakeCalls calls;

    // A real driver that never connects. It turns the Action of AsyncSession into a SessionConfig.
    private readonly IDriver configReader = GraphDatabase.Driver("bolt://localhost:1");

    internal FakeClient(FakeCalls? calls = null)
    {
        this.calls = calls ?? new FakeCalls();
    }

    internal FakeCalls Calls => calls;

    internal List<ExecutedQuery> Executed { get; } = [];

    /// <summary>An error for each query that starts with the key.</summary>
    internal Dictionary<string, Exception> Fail { get; } = [];

    internal Exception? VerifyError { get; set; }

    /// <summary>An error that the close throws after it records its name.</summary>
    internal Exception? CloseError { get; set; }

    /// <summary>Runs inside ExecuteAsync after the first yield, with the token of the call.</summary>
    internal Func<CancellationToken, Task>? InFlight { get; set; }

    internal List<FakeSession> Sessions { get; } = [];

    /// <summary>Makes the session that the next call of AsyncSession returns.</summary>
    internal Func<FakeSession>? SessionFactory { get; set; }

    public Config Config => throw new NotSupportedException();

    public bool Encrypted => false;

    public IAsyncSession AsyncSession() => throw new NotSupportedException();

    public IAsyncSession AsyncSession(Action<SessionConfigBuilder> action)
    {
        var database = configReader.AsyncSession(action).SessionConfig.Database;
        calls.Names.Add($"session(database={database})");
        var session = SessionFactory?.Invoke() ?? new FakeSession(calls);
        Sessions.Add(session);
        return session;
    }

    public Task<IServerInfo> GetServerInfoAsync() => throw new NotSupportedException();

    public Task<bool> TryVerifyConnectivityAsync() => throw new NotSupportedException();

    public async Task VerifyConnectivityAsync()
    {
        await Task.Yield();
        if (VerifyError is not null)
        {
            throw VerifyError;
        }
    }

    public Task<bool> SupportsMultiDbAsync() => throw new NotSupportedException();

    public Task<bool> SupportsSessionAuthAsync() => throw new NotSupportedException();

    public IExecutableQuery<IRecord, IRecord> ExecutableQuery(string cypher) => new FakeQuery(this, cypher);

    public Task<bool> VerifyAuthenticationAsync(IAuthToken authToken) => throw new NotSupportedException();

    public IBookmarkManager GetExecutableQueryBookmarkManager() => throw new NotSupportedException();

    public void Dispose()
    {
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        calls.Names.Add("client.close");
        await configReader.DisposeAsync();
        if (CloseError is not null)
        {
            throw CloseError;
        }
    }

    private sealed class FakeQuery(FakeClient client, string cypher) : IExecutableQuery<IRecord, IRecord>
    {
        private Dictionary<string, object?> parameters = [];
        private QueryConfig config = new();

        public IExecutableQuery<IRecord, IRecord> WithConfig(QueryConfig value)
        {
            config = value;
            return this;
        }

        public IExecutableQuery<IRecord, IRecord> WithParameters(Dictionary<string, object> value)
        {
            parameters = value.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
            return this;
        }

        public IExecutableQuery<IRecord, IRecord> WithParameters(object value) => throw new NotSupportedException();

        public IConfiguredQuery<IRecord, TNext> WithMap<TNext>(Func<IRecord, TNext> map) => throw new NotSupportedException();

        public IConfiguredQuery<IRecord, IRecord> WithFilter(Func<IRecord, bool> filter) => throw new NotSupportedException();

        public IReducedExecutableQuery<TResult> WithReduce<TAccumulate, TResult>(Func<TAccumulate> seed, Func<TAccumulate, IRecord, TAccumulate> reduce, Func<TAccumulate, TResult> finish) =>
            throw new NotSupportedException();

        public IReducedExecutableQuery<TResult> WithReduce<TResult>(Func<TResult> seed, Func<TResult, IRecord, TResult> reduce) =>
            throw new NotSupportedException();

        public IReducedExecutableQuery<TResult> WithStreamProcessor<TResult>(Func<IAsyncEnumerable<IRecord>, Task<TResult>> processor) =>
            throw new NotSupportedException();

        public async Task<EagerResult<IReadOnlyList<IRecord>>> ExecuteAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            client.Executed.Add(new ExecutedQuery(cypher, parameters, config));
            if (client.InFlight is not null)
            {
                await client.InFlight(cancellationToken);
            }

            foreach (var (prefix, error) in client.Fail)
            {
                if (cypher.StartsWith(prefix, StringComparison.Ordinal))
                {
                    throw error;
                }
            }

            // The constructor of EagerResult is internal.
            var result = Activator.CreateInstance(
                typeof(EagerResult<IReadOnlyList<IRecord>>),
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                [new List<IRecord>(), null, Array.Empty<string>()],
                null);
            return (EagerResult<IReadOnlyList<IRecord>>)result!;
        }
    }
}

internal sealed class FakeSession(FakeCalls calls) : IAsyncSession
{
    /// <summary>An error that CloseAsync and DisposeAsync throw after they record "close".</summary>
    internal Exception? CloseError { get; set; }

    public Bookmarks LastBookmarks => throw new NotSupportedException();

    public SessionConfig SessionConfig => throw new NotSupportedException();

    public async Task<IAsyncTransaction> BeginTransactionAsync()
    {
        await Slow.Pause();
        calls.Names.Add("begin_transaction");
        return new FakeTransaction(calls);
    }

    public Task<IAsyncTransaction> BeginTransactionAsync(Action<TransactionConfigBuilder> action) => throw new NotSupportedException();

    public Task<IAsyncTransaction> BeginTransactionAsync(AccessMode mode) => throw new NotSupportedException();

    public Task<IAsyncTransaction> BeginTransactionAsync(AccessMode mode, Action<TransactionConfigBuilder> action) => throw new NotSupportedException();

    public Task<T> ExecuteReadAsync<T>(Func<IAsyncQueryRunner, Task<T>> work, Action<TransactionConfigBuilder>? action = null) => throw new NotSupportedException();

    public Task<IResultCursor> ExecuteReadAsync(Func<IAsyncQueryRunner, Task<IResultCursor>> work, Action<TransactionConfigBuilder>? action = null) => throw new NotSupportedException();

    public Task ExecuteReadAsync(Func<IAsyncQueryRunner, Task> work, Action<TransactionConfigBuilder>? action = null) => throw new NotSupportedException();

    public Task<T> ExecuteWriteAsync<T>(Func<IAsyncQueryRunner, Task<T>> work, Action<TransactionConfigBuilder>? action = null) => throw new NotSupportedException();

    public Task<IResultCursor> ExecuteWriteAsync(Func<IAsyncQueryRunner, Task<IResultCursor>> work, Action<TransactionConfigBuilder>? action = null) => throw new NotSupportedException();

    public async Task ExecuteWriteAsync(Func<IAsyncQueryRunner, Task> work, Action<TransactionConfigBuilder>? action = null)
    {
        await Slow.Pause();
        calls.Names.Add("execute_write");
        await work(new FakeTransaction(calls));
    }

    public async Task CloseAsync()
    {
        await Task.Yield();
        calls.Names.Add("close");
        if (CloseError is not null)
        {
            throw CloseError;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        calls.Names.Add("close");
        if (CloseError is not null)
        {
            throw CloseError;
        }
    }

    public async Task<IResultCursor> RunAsync(string query, Action<TransactionConfigBuilder>? action = null)
    {
        await Slow.Pause();
        calls.Names.Add("run");
        return new FakeCursor();
    }

    public async Task<IResultCursor> RunAsync(string query, object parameters, Action<TransactionConfigBuilder>? action = null)
    {
        await Slow.Pause();
        calls.Names.Add("run");
        return new FakeCursor();
    }

    public async Task<IResultCursor> RunAsync(string query, IDictionary<string, object> parameters, Action<TransactionConfigBuilder>? action = null)
    {
        await Slow.Pause();
        calls.Names.Add("run");
        return new FakeCursor();
    }

    public Task<IResultCursor> RunAsync(Query query, Action<TransactionConfigBuilder>? action = null) => throw new NotSupportedException();

    public Task<IResultCursor> RunAsync(string query) => throw new NotSupportedException();

    public Task<IResultCursor> RunAsync(string query, object parameters) => throw new NotSupportedException();

    public Task<IResultCursor> RunAsync(string query, IDictionary<string, object> parameters) => throw new NotSupportedException();

    public Task<IResultCursor> RunAsync(Query query) => throw new NotSupportedException();

    public void Dispose()
    {
    }
}

internal sealed class FakeTransaction(FakeCalls calls) : IAsyncTransaction
{
    public TransactionConfig TransactionConfig => throw new NotSupportedException();

    public async Task CommitAsync()
    {
        await Slow.Pause();
        calls.Names.Add("commit");
    }

    public async Task RollbackAsync()
    {
        await Slow.Pause();
        calls.Names.Add("rollback");
    }

    public async Task<IResultCursor> RunAsync(string query)
    {
        await Slow.Pause();
        calls.Names.Add("run");
        return new FakeCursor();
    }

    public async Task<IResultCursor> RunAsync(string query, object parameters)
    {
        await Slow.Pause();
        calls.Names.Add("run");
        return new FakeCursor();
    }

    public async Task<IResultCursor> RunAsync(string query, IDictionary<string, object> parameters)
    {
        await Slow.Pause();
        calls.Names.Add("run");
        return new FakeCursor();
    }

    public Task<IResultCursor> RunAsync(Query query) => throw new NotSupportedException();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose()
    {
    }
}

/// <summary>
/// The result of a fake run: no keys and no rows, as the answer to a statement with no RETURN. The Python fake run
/// returns None, so the reads of the port record no name.
/// </summary>
internal sealed class FakeCursor : IResultCursor
{
    public IRecord Current => throw new NotSupportedException();

    public bool IsOpen => true;

    public Task<string[]> KeysAsync() => Task.FromResult(Array.Empty<string>());

    public Task<IResultSummary> ConsumeAsync() => Task.FromResult<IResultSummary>(null!);

    public Task<IRecord> PeekAsync() => throw new NotSupportedException();

    public Task<bool> FetchAsync() => throw new NotSupportedException();

    public IAsyncEnumerator<IRecord> GetAsyncEnumerator(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>A logger factory that keeps the lines of the module of the driver, with their level.</summary>
internal sealed class CaptureFactory(List<(LogLevel, string)> lines) : ILoggerFactory
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
