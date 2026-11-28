namespace Graphiti.JudgeHost.Flow;

using System.Collections;
using System.Text.Json.Nodes;
using Graphiti.JudgeHost.Core;
using Graphiti.PyCompat;
using global::Neo4j.Driver;

/// <summary>
/// The <c>queries</c> seam: <c>RecordingNeo4jClient</c> of <c>judge_clients.py</c>, as a decorator of the
/// <see cref="IDriver"/> that <c>Neo4jDriver.Client</c> holds. One entry is one executable query, or one session with
/// its operations in order: a run on the session, a run in a transaction that the session began, and a run in a work
/// function of <c>execute_write</c> or <c>execute_read</c>.
/// </summary>
internal sealed class RecordingDriver(IDriver inner, FlowRecorder recorder) : IDriver
{
    public Config Config => inner.Config;

    public bool Encrypted => inner.Encrypted;

    public IAsyncSession AsyncSession() => new RecordingSession(inner.AsyncSession(), recorder);

    public IAsyncSession AsyncSession(Action<SessionConfigBuilder> action) => new RecordingSession(inner.AsyncSession(action), recorder);

    public IExecutableQuery<IRecord, IRecord> ExecutableQuery(string cypher) => new RecordingQuery(inner.ExecutableQuery(cypher), cypher, null, null, recorder);

    public IBookmarkManager GetExecutableQueryBookmarkManager() => inner.GetExecutableQueryBookmarkManager();

    public Task<IServerInfo> GetServerInfoAsync() => inner.GetServerInfoAsync();

    public Task<bool> SupportsMultiDbAsync() => inner.SupportsMultiDbAsync();

    public Task<bool> SupportsSessionAuthAsync() => inner.SupportsSessionAuthAsync();

    public Task<bool> TryVerifyConnectivityAsync() => inner.TryVerifyConnectivityAsync();

    public Task<bool> VerifyAuthenticationAsync(IAuthToken authToken) => inner.VerifyAuthenticationAsync(authToken);

    public Task VerifyConnectivityAsync() => inner.VerifyConnectivityAsync();

    public void Dispose() => inner.Dispose();

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    /// <summary>
    /// <c>_query_params</c>: the parameters as the codec encodes them. The driver values of
    /// <c>Neo4jDriver.ToDriverParameters</c> go back to the value model first: a <see cref="DateTimeOffset"/> and a
    /// <see cref="LocalDateTime"/> become a <c>$dt</c>.
    /// </summary>
    internal static JsonNode Parameters(object? parameters) => parameters switch
    {
        null => new JsonObject(),
        IDictionary or IReadOnlyDictionary<string, object?> => Value(parameters)!,
        _ => new JsonObject { ["$unknown"] = parameters.GetType().Name },
    };

    internal static JsonObject Op(string query, object? parameters) => new() { ["q"] = query, ["params"] = Parameters(parameters) };

    private static JsonNode? Value(object? value)
    {
        switch (value)
        {
            case null or string or bool or byte[] or DateOnly:
                return Codec.Encode(value);
            case DateTimeOffset offset:
                return Codec.DateTime(offset);
            case LocalDateTime local:
                return new JsonObject
                {
                    ["$dt"] = new PyDateTime(local.Year, local.Month, local.Day, local.Hour, local.Minute, local.Second, local.Nanosecond / 1000).IsoFormat(),
                };
            case IDictionary dictionary:
                {
                    var result = new JsonObject();
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        result[(string)entry.Key] = Value(entry.Value);
                    }

                    return result;
                }

            case IEnumerable<KeyValuePair<string, object?>> pairs:
                {
                    var result = new JsonObject();
                    foreach (var (key, item) in pairs)
                    {
                        result[key] = Value(item);
                    }

                    return result;
                }

            case IEnumerable items:
                return new JsonArray([.. items.Cast<object?>().Select(Value)]);
            default:
                return Codec.Encode(value);
        }
    }
}

/// <summary>An executable query that records its text, its parameters, and the routing when it runs.</summary>
internal sealed class RecordingQuery(
    IExecutableQuery<IRecord, IRecord> inner, string cypher, object? parameters, QueryConfig? config, FlowRecorder recorder)
    : IExecutableQuery<IRecord, IRecord>
{
    public IExecutableQuery<IRecord, IRecord> WithConfig(QueryConfig config) =>
        new RecordingQuery(inner.WithConfig(config), cypher, parameters, config, recorder);

    public IExecutableQuery<IRecord, IRecord> WithParameters(Dictionary<string, object> parameters) =>
        new RecordingQuery(inner.WithParameters(parameters), cypher, parameters, config, recorder);

    public IExecutableQuery<IRecord, IRecord> WithParameters(object parameters) =>
        new RecordingQuery(inner.WithParameters(parameters), cypher, parameters, config, recorder);

    public IReducedExecutableQuery<TResult> WithStreamProcessor<TResult>(Func<IAsyncEnumerable<IRecord>, Task<TResult>> streamProcessor) =>
        throw new NotSupportedException("The recording driver records only ExecuteAsync of an executable query.");

    public IConfiguredQuery<IRecord, TNext> WithMap<TNext>(Func<IRecord, TNext> map) =>
        throw new NotSupportedException("The recording driver records only ExecuteAsync of an executable query.");

    public IConfiguredQuery<IRecord, IRecord> WithFilter(Func<IRecord, bool> filter) =>
        throw new NotSupportedException("The recording driver records only ExecuteAsync of an executable query.");

    public IReducedExecutableQuery<TResult> WithReduce<TResult>(Func<TResult> seed, Func<TResult, IRecord, TResult> accumulate) =>
        throw new NotSupportedException("The recording driver records only ExecuteAsync of an executable query.");

    public IReducedExecutableQuery<TResult> WithReduce<TAccumulate, TResult>(
        Func<TAccumulate> seed, Func<TAccumulate, IRecord, TAccumulate> accumulate, Func<TAccumulate, TResult> selectResult) =>
        throw new NotSupportedException("The recording driver records only ExecuteAsync of an executable query.");

    public Task<EagerResult<IReadOnlyList<IRecord>>> ExecuteAsync(CancellationToken token = default)
    {
        // database_ is not an option of the record. Upstream passes routing_ only as 'r'.
        var options = config?.Routing == RoutingControl.Readers ? new JsonObject { ["routing_"] = "r" } : null;
        recorder.AddQuery(QueryUnit.ExecuteQuery(cypher, RecordingDriver.Parameters(parameters), options));
        return inner.ExecuteAsync(token);
    }
}

/// <summary>A session that records each operation into its own entry.</summary>
internal sealed class RecordingSession : IAsyncSession
{
    private readonly IAsyncSession inner;
    private readonly FlowRecorder recorder;
    private readonly QueryUnit unit;

    internal RecordingSession(IAsyncSession inner, FlowRecorder recorder)
    {
        this.inner = inner;
        this.recorder = recorder;
        unit = recorder.AddQuery(QueryUnit.Session());
    }

    public Bookmarks LastBookmarks => inner.LastBookmarks;

    public SessionConfig SessionConfig => inner.SessionConfig;

    public async Task<IAsyncTransaction> BeginTransactionAsync() =>
        new RecordingTransaction(await inner.BeginTransactionAsync(), recorder, unit);

    public async Task<IAsyncTransaction> BeginTransactionAsync(AccessMode mode) =>
        new RecordingTransaction(await inner.BeginTransactionAsync(mode), recorder, unit);

    public async Task<IAsyncTransaction> BeginTransactionAsync(AccessMode mode, Action<TransactionConfigBuilder> action) =>
        new RecordingTransaction(await inner.BeginTransactionAsync(mode, action), recorder, unit);

    public async Task<IAsyncTransaction> BeginTransactionAsync(Action<TransactionConfigBuilder> action) =>
        new RecordingTransaction(await inner.BeginTransactionAsync(action), recorder, unit);

#pragma warning disable CS0618 // The interface still has the two members that return a cursor.
    public Task<IResultCursor> ExecuteReadAsync(Func<IAsyncQueryRunner, Task<IResultCursor>> work, Action<TransactionConfigBuilder>? action = null) =>
        inner.ExecuteReadAsync(tx => work(Runner(tx)), action);

#pragma warning restore CS0618

    public Task ExecuteReadAsync(Func<IAsyncQueryRunner, Task> work, Action<TransactionConfigBuilder>? action = null) =>
        inner.ExecuteReadAsync(tx => work(Runner(tx)), action);

    public Task<T> ExecuteReadAsync<T>(Func<IAsyncQueryRunner, Task<T>> work, Action<TransactionConfigBuilder>? action = null) =>
        inner.ExecuteReadAsync(tx => work(Runner(tx)), action);

#pragma warning disable CS0618 // The interface still has the two members that return a cursor.
    public Task<IResultCursor> ExecuteWriteAsync(Func<IAsyncQueryRunner, Task<IResultCursor>> work, Action<TransactionConfigBuilder>? action = null) =>
        inner.ExecuteWriteAsync(tx => work(Runner(tx)), action);

#pragma warning restore CS0618

    public Task ExecuteWriteAsync(Func<IAsyncQueryRunner, Task> work, Action<TransactionConfigBuilder>? action = null) =>
        inner.ExecuteWriteAsync(tx => work(Runner(tx)), action);

    public Task<T> ExecuteWriteAsync<T>(Func<IAsyncQueryRunner, Task<T>> work, Action<TransactionConfigBuilder>? action = null) =>
        inner.ExecuteWriteAsync(tx => work(Runner(tx)), action);

    public Task<IResultCursor> RunAsync(Query query, Action<TransactionConfigBuilder> action)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query.Text, query.Parameters));
        return inner.RunAsync(query, action);
    }

    public Task<IResultCursor> RunAsync(string query, Action<TransactionConfigBuilder> action)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, null));
        return inner.RunAsync(query, action);
    }

    public Task<IResultCursor> RunAsync(string query, IDictionary<string, object> parameters, Action<TransactionConfigBuilder> action)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, parameters));
        return inner.RunAsync(query, parameters, action);
    }

    public Task<IResultCursor> RunAsync(string query, object parameters, Action<TransactionConfigBuilder> action)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, parameters));
        return inner.RunAsync(query, parameters, action);
    }

    public Task<IResultCursor> RunAsync(string query)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, null));
        return inner.RunAsync(query);
    }

    public Task<IResultCursor> RunAsync(string query, object parameters)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, parameters));
        return inner.RunAsync(query, parameters);
    }

    public Task<IResultCursor> RunAsync(string query, IDictionary<string, object> parameters)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, parameters));
        return inner.RunAsync(query, parameters);
    }

    public Task<IResultCursor> RunAsync(Query query)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query.Text, query.Parameters));
        return inner.RunAsync(query);
    }

    public Task CloseAsync() => inner.CloseAsync();

    public void Dispose() => inner.Dispose();

    public ValueTask DisposeAsync() => inner.DisposeAsync();

    private RecordingRunner Runner(IAsyncQueryRunner tx) => new(tx, recorder, unit);
}

/// <summary>A transaction that a recording session began. Each run goes into the entry of the session.</summary>
internal sealed class RecordingTransaction(IAsyncTransaction inner, FlowRecorder recorder, QueryUnit unit) : IAsyncTransaction
{
    public TransactionConfig TransactionConfig => inner.TransactionConfig;

    public Task CommitAsync() => inner.CommitAsync();

    public Task RollbackAsync() => inner.RollbackAsync();

    public Task<IResultCursor> RunAsync(string query)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, null));
        return inner.RunAsync(query);
    }

    public Task<IResultCursor> RunAsync(string query, object parameters)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, parameters));
        return inner.RunAsync(query, parameters);
    }

    public Task<IResultCursor> RunAsync(string query, IDictionary<string, object> parameters)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, parameters));
        return inner.RunAsync(query, parameters);
    }

    public Task<IResultCursor> RunAsync(Query query)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query.Text, query.Parameters));
        return inner.RunAsync(query);
    }

    public void Dispose() => inner.Dispose();

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

/// <summary>The managed transaction of a work function. Each run goes into the entry of the session.</summary>
internal sealed class RecordingRunner(IAsyncQueryRunner inner, FlowRecorder recorder, QueryUnit unit) : IAsyncQueryRunner
{
    public Task<IResultCursor> RunAsync(string query)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, null));
        return inner.RunAsync(query);
    }

    public Task<IResultCursor> RunAsync(string query, object parameters)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, parameters));
        return inner.RunAsync(query, parameters);
    }

    public Task<IResultCursor> RunAsync(string query, IDictionary<string, object> parameters)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query, parameters));
        return inner.RunAsync(query, parameters);
    }

    public Task<IResultCursor> RunAsync(Query query)
    {
        recorder.AddOp(unit, RecordingDriver.Op(query.Text, query.Parameters));
        return inner.RunAsync(query);
    }

    public void Dispose() => inner.Dispose();

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
