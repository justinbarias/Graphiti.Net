// Ported from graphiti_core/driver/neo4j_driver.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Neo4j;

using System.Collections;
using System.Numerics;
using global::Neo4j.Driver;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Operations;
using Graphiti.Internal;
using Graphiti.Neo4j.Operations;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: the module class of R2 and R5. The module name equals the class name, so it has the suffix Module.
internal static partial class Neo4jDriverModule
{
    internal static ILogger Logger => PortServices.Logger(typeof(Neo4jDriverModule));

    internal static partial class Log
    {
        // PORT: {e} is str(e) of the exception (Neo4jDriver.DriverErrorText).
        [LoggerMessage(Level = LogLevel.Error, Message = "Error executing Neo4j query: {E}\n{CypherQuery}\nparameter keys: {ParameterKeys}")]
        internal static partial void ErrorExecutingNeo4jQuery(ILogger logger, string e, string cypherQuery, string parameterKeys);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Index already exists (concurrent creation): {Query}...")]
        internal static partial void IndexAlreadyExists(ILogger logger, string query);
    }
}

// PORT: the class attributes provider = GraphProvider.NEO4J and default_group_id = '' are instance properties of
// GraphDriver. The constructor sets Provider. DefaultGroupId already has the value ''.
internal sealed class Neo4jDriver : GraphDriver
{
    private readonly EntityNodeOperations entityNodeOps;
    private readonly EpisodeNodeOperations episodeNodeOps;
    private readonly CommunityNodeOperations communityNodeOps;
    private readonly SagaNodeOperations sagaNodeOps;
    private readonly EntityEdgeOperations entityEdgeOps;
    private readonly EpisodicEdgeOperations episodicEdgeOps;
    private readonly CommunityEdgeOperations communityEdgeOps;
    private readonly HasEpisodeEdgeOperations hasEpisodeEdgeOps;
    private readonly NextEpisodeEdgeOperations nextEpisodeEdgeOps;
    private readonly SearchOperations searchOps;
    private readonly GraphMaintenanceOperations graphOps;

    // PORT: type(driver).__name__ (R15).
    internal override string PythonName => "Neo4jDriver";

    // PORT: R9. The first constructor builds the client and chains to the second one, which holds the rest of
    // __init__.
    internal Neo4jDriver(string uri, string? user, string? password, string database = "neo4j")
        : this(GraphDatabase.Driver(uri, AuthTokens.Basic(user ?? string.Empty, password ?? string.Empty)), database)
    {
    }

    // PORT: the public surface design lets a host give its own IDriver (AddNeo4j with a factory). Python has one
    // constructor, which builds the client from uri, user, and password.
    internal Neo4jDriver(IDriver client, string database = "neo4j")
    {
        Provider = GraphProvider.Neo4j;
        Client = client;
        Database = database;

        // Instantiate Neo4j operations
        entityNodeOps = new Neo4jEntityNodeOperations();
        episodeNodeOps = new Neo4jEpisodeNodeOperations();
        communityNodeOps = new Neo4jCommunityNodeOperations();
        sagaNodeOps = new Neo4jSagaNodeOperations();
        entityEdgeOps = new Neo4jEntityEdgeOperations();
        episodicEdgeOps = new Neo4jEpisodicEdgeOperations();
        communityEdgeOps = new Neo4jCommunityEdgeOperations();
        hasEpisodeEdgeOps = new Neo4jHasEpisodeEdgeOperations();
        nextEpisodeEdgeOps = new Neo4jNextEpisodeEdgeOperations();
        searchOps = new Neo4jSearchOperations();
        graphOps = new Neo4jGraphMaintenanceOperations();

        // Schedule the indices and constraints to be built
        // PORT: G20. The index build that __init__ starts as a background task is not ported (R17). The .NET driver
        // builds indexes only in BuildIndicesAndConstraintsAsync, so there is no _init_task.

        // PORT: aoss_client has no member (R9). Only the Neptune driver reads it.
    }

    internal IDriver Client { get; set; }

    // --- Operations properties ---

    internal override EntityNodeOperations EntityNodeOps => entityNodeOps;

    internal override EpisodeNodeOperations EpisodeNodeOps => episodeNodeOps;

    internal override CommunityNodeOperations CommunityNodeOps => communityNodeOps;

    internal override SagaNodeOperations SagaNodeOps => sagaNodeOps;

    internal override EntityEdgeOperations EntityEdgeOps => entityEdgeOps;

    internal override EpisodicEdgeOperations EpisodicEdgeOps => episodicEdgeOps;

    internal override CommunityEdgeOperations CommunityEdgeOps => communityEdgeOps;

    internal override HasEpisodeEdgeOperations HasEpisodeEdgeOps => hasEpisodeEdgeOps;

    internal override NextEpisodeEdgeOperations NextEpisodeEdgeOps => nextEpisodeEdgeOps;

    internal override SearchOperations SearchOps => searchOps;

    internal override GraphMaintenanceOperations GraphOps => graphOps;

    // PORT: an async context manager that commits or rolls back takes the body as a lambda (R16).
    // PORT: the session and transaction calls of Neo4j.Driver take no CancellationToken (R9). The port checks the token
    // before each driver call, so that a cancellation arrives at the await where Python raises CancelledError (R8,
    // open item O9). The rollback and the close run after a cancellation as well, as R8 asks.
    /// <summary>Neo4j transaction with real commit/rollback semantics.</summary>
    internal override async Task TransactionAsync(
        Func<Transaction, Task> body,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var session = Client.AsyncSession(b => b.WithDatabase(Database));
        var tx = await session.BeginTransactionAsync();
        try
        {
            await body(new Neo4jTransaction(tx));
            await tx.CommitAsync();
        }
        catch (Exception)
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    // PORT: the keys params, database_, and routing_ of the parameters are driver options (R2). The parameter values
    // and the records are converted at the driver boundary (R9). The summary and the keys of the EagerResult are the
    // second and the third items.
    internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
        string cypherQuery,
        OrderedDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        // PORT: **kwargs is a new dict for each call, so the method reads a copy (R2).
        var kwargs = new OrderedDictionary<string, object?>(parameters);
        kwargs.Remove("params", out var paramsValue);
        var @params = paramsValue is null ? new OrderedDictionary<string, object?>() : Py.AsDict(paramsValue);

        // Route via Neo4j's database_ kwarg; an explicit override wins (including None,
        // which lets the server resolve the home database), else the driver default.
        var database = kwargs.Remove("database_", out var databaseValue)
            ? (databaseValue is null ? null : Py.AsString(databaseValue))
            : Database;

        EagerResult<IReadOnlyList<IRecord>> result;
        try
        {
            // PORT: the body of AsyncDriver.execute_query of the neo4j package. routing_ is a driver option, and
            // the other entries join the entries of params: dict(parameters_ or {}, **kwargs) (R2).
            var routing = RoutingControl.Writers;
            var merged = new OrderedDictionary<string, object?>(@params);
            foreach (var (key, value) in kwargs)
            {
                if (key == "routing_")
                {
                    routing = value switch
                    {
                        "w" => RoutingControl.Writers,
                        "r" => RoutingControl.Readers,
                        _ => throw new ArgumentException("Invalid routing control value: " + Py.Repr(value)),
                    };
                }
                else
                {
                    merged[key] = value;
                }
            }

            result = await Client
                .ExecutableQuery(cypherQuery)
                .WithParameters(ToDriverParameters(merged))
                .WithConfig(new QueryConfig(routing, database))
                .ExecuteAsync(cancellationToken);
        }
        catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
        {
            // Log parameter names only; values may contain sensitive data.
            var logger = Neo4jDriverModule.Logger;
            if (logger.IsEnabled(LogLevel.Error))
            {
                List<string> keys = [.. @params.Keys, .. kwargs.Keys];
                Py.SortInPlace(keys);
                var text = Py.Repr(keys);
                Neo4jDriverModule.Log.ErrorExecutingNeo4jQuery(logger, DriverErrorText(e), cypherQuery, text);
            }

            throw;
        }

        return ([.. result.Result.Select(FromDriverRecord)], result.Summary, new List<string>(result.Keys));
    }

    // PORT: Python returns the session of the neo4j package, which has the shape of GraphDriverSession. The port wraps
    // it in Neo4jSession.
    internal override GraphDriverSession Session(string? database = null)
    {
        // PORT: database or self._database. An empty string also gives the default (R9).
        var database_ = string.IsNullOrEmpty(database) ? Database : database;
        return new Neo4jSession(Client.AsyncSession(b => b.WithDatabase(database_)));
    }

    // PORT: G20. Python cancels and awaits _init_task first. The port has no such task. IDriver has no CloseAsync, so
    // client.close() is DisposeAsync (R9).
    internal override async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await Client.DisposeAsync();
    }

    internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteQueryAsync("CALL db.indexes() YIELD name DROP INDEX name", new(), cancellationToken);
    }

    // PORT: EagerResult | None is the result tuple of ExecuteQueryAsync, or null.
    /// <summary>
    /// Execute an index creation query, ignoring 'index already exists' errors.
    ///
    /// Neo4j can raise EquivalentSchemaRuleAlreadyExists when concurrent CREATE INDEX
    /// IF NOT EXISTS queries race, even though the index exists. This is safe to ignore.
    /// </summary>
    private async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)?> ExecuteIndexQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await ExecuteQueryAsync(query, new(), cancellationToken);
        }
        catch (ClientException e)
        {
            // Ignore "equivalent index already exists" error (race condition with IF NOT EXISTS)
            // PORT: str(e) holds the code and the message of the error (R4).
            if ((e.Code is not null && e.Code.Contains("EquivalentSchemaRuleAlreadyExists", StringComparison.Ordinal))
                || e.Message.Contains("EquivalentSchemaRuleAlreadyExists", StringComparison.Ordinal))
            {
                var logger = Neo4jDriverModule.Logger;
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    var text = Py.Slice(query, null, 50);
                    Neo4jDriverModule.Log.IndexAlreadyExists(logger, text);
                }

                return null;
            }

            throw;
        }
    }

    internal override async Task BuildIndicesAndConstraintsAsync(
        bool deleteExisting = false,
        CancellationToken cancellationToken = default)
    {
        if (deleteExisting)
        {
            await DeleteAllIndexesAsync(cancellationToken);
        }

        var rangeIndices = GraphQueries.GetRangeIndices(Provider);

        var fulltextIndices = GraphQueries.GetFulltextIndices(Provider);

        List<string> indexQueries = [.. rangeIndices, .. fulltextIndices];

        await Helpers.SemaphoreGatherAsync(
            [.. indexQueries.Select(query =>
                (Func<Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)?>>)(() => ExecuteIndexQueryAsync(query, cancellationToken)))],
            cancellationToken: cancellationToken);
    }

    // PORT: GraphDriver has no health_check, so this is not an override.
    // PORT: VerifyConnectivityAsync takes no CancellationToken (R9).
    /// <summary>Check Neo4j connectivity by running the driver's verify_connectivity method.</summary>
    internal async Task HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Client.VerifyConnectivityAsync();
            return;
        }
        catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
        {
            // PORT: print(x) is Console.Out.WriteLine(x) (R4). {e} is str(e) of the exception.
            Console.Out.WriteLine("Neo4j health check failed: " + DriverErrorText(e));
            throw;
        }
    }

    // PORT: R9. str(e) of an exception. The Python driver prints an error that the server sent as
    // {neo4j_code: ...} {message: ...} {gql_status: ...} {gql_status_description: ...}. Each other exception prints its
    // message. A .NET exception from the server is a Neo4jException with a code. No Python name.
    private static string DriverErrorText(Exception e)
    {
        return e is Neo4jException { Code: not null } serverError
            ? "{neo4j_code: " + serverError.Code + "} {message: " + serverError.Message + "} {gql_status: " + serverError.GqlStatus
                + "} {gql_status_description: " + serverError.GqlStatusDescription + "}"
            : e.Message;
    }

    // PORT: R9. The four members below have no Python name. ExecuteQueryAsync, Neo4jTransaction, and Neo4jSession
    // convert each parameter value before the driver call and each record after it, so the members are internal.
    internal static Dictionary<string, object?> ToDriverParameters(OrderedDictionary<string, object?> parameters)
    {
        var converted = new Dictionary<string, object?>(parameters.Count);
        foreach (var (key, value) in parameters)
        {
            _ = PyText.EncodeUtf8(key);
            converted[key] = ToDriverValue(value);
        }

        return converted;
    }

    // PORT: R9. The errors follow the packer of the Python driver: OverflowError for an int out of range, TypeError
    // for a dict key that is not a str, ValueError for each other type, and UnicodeEncodeError for a str that holds a
    // lone surrogate. PyText.EncodeUtf8 is the strict encode that the packer does (R12).
    internal static object? ToDriverValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case bool or long or double or byte[] or DateOnly:
                return value;
            case string text:
                _ = PyText.EncodeUtf8(text);
                return text;
            case PyDateTime dateTime:
                return dateTime.IsNaive
                    ? new LocalDateTime(
                        dateTime.Year,
                        dateTime.Month,
                        dateTime.Day,
                        dateTime.Hour,
                        dateTime.Minute,
                        dateTime.Second,
                        dateTime.Microsecond * 1000)
                    : dateTime.ToDateTimeOffset();
            case BigInteger big:
                return big >= long.MinValue && big <= long.MaxValue
                    ? (long)big
                    : throw new OverflowException("Integer " + big.ToString(System.Globalization.CultureInfo.InvariantCulture) + " out of range");
            case ulong unsigned:
                return unsigned <= long.MaxValue
                    ? (long)unsigned
                    : throw new OverflowException("Integer " + unsigned.ToString(System.Globalization.CultureInfo.InvariantCulture) + " out of range");
            case PyDict pyDict:
                {
                    var map = new Dictionary<string, object?>();
                    foreach (var (key, item) in pyDict)
                    {
                        map[CheckedKey(key)] = ToDriverValue(item);
                    }

                    return map;
                }

            case IDictionary dictionary:
                {
                    var map = new Dictionary<string, object?>(dictionary.Count);
                    foreach (DictionaryEntry entry in dictionary)
                    {
                        map[CheckedKey(entry.Key)] = ToDriverValue(entry.Value);
                    }

                    return map;
                }

            case IEnumerable items:
                {
                    var list = new List<object?>();
                    foreach (var item in items)
                    {
                        list.Add(ToDriverValue(item));
                    }

                    return list;
                }

            default:
                throw new ArgumentException("Values of type " + PythonTypeText(value) + " are not supported");
        }
    }

    // PORT: R9. A map key of the packer: a str that the strict encode accepts, else TypeError.
    private static string CheckedKey(object? key)
    {
        var text = key as string ?? throw new InvalidCastException("Map keys must be strings, not " + PythonTypeText(key));
        _ = PyText.EncodeUtf8(text);
        return text;
    }

    // PORT: R9. {type(value)} in the message of the packer: <class 'int'>, or <enum 'GraphProvider'> for an enum.
    private static string PythonTypeText(object? value)
    {
        return value is Enum ? "<enum '" + Py.TypeName(value) + "'>" : "<class '" + Py.TypeName(value) + "'>";
    }

    internal static OrderedDictionary<string, object?> FromDriverRecord(IRecord record)
    {
        var converted = new OrderedDictionary<string, object?>();
        foreach (var key in record.Keys)
        {
            converted[key] = FromDriverValue(record[key]);
        }

        return converted;
    }

    // PORT: R9. Nanoseconds are cut to microseconds, as to_native() does. A named zone keeps only its offset.
    internal static object? FromDriverValue(object? value)
    {
        switch (value)
        {
            case null:
                return null;
            case bool or long or double or string or byte[]:
                return value;
            case IReadOnlyDictionary<string, object?> map:
                {
                    var converted = new OrderedDictionary<string, object?>();
                    foreach (var (key, item) in map)
                    {
                        converted[key] = FromDriverValue(item);
                    }

                    return converted;
                }

            case IList list:
                {
                    var converted = new List<object?>(list.Count);
                    foreach (var item in list)
                    {
                        converted.Add(FromDriverValue(item));
                    }

                    return converted;
                }

            case ZonedDateTime zoned:
                return new PyDateTime(
                    zoned.Year,
                    zoned.Month,
                    zoned.Day,
                    zoned.Hour,
                    zoned.Minute,
                    zoned.Second,
                    zoned.Nanosecond / 1000,
                    zoned.OffsetSeconds * 1_000_000L);
            case LocalDateTime local:
                return new PyDateTime(
                    local.Year,
                    local.Month,
                    local.Day,
                    local.Hour,
                    local.Minute,
                    local.Second,
                    local.Nanosecond / 1000);
            case LocalDate date:
                return new DateOnly(date.Year, date.Month, date.Day);
            default:
                throw new NotSupportedException("The driver type " + value.GetType().FullName + " has no place in the value model");
        }
    }
}

/// <summary>Wraps a Neo4j AsyncTransaction for the Transaction ABC.</summary>
internal sealed class Neo4jTransaction : Transaction
{
    private readonly IAsyncQueryRunner tx;

    internal Neo4jTransaction(IAsyncQueryRunner tx)
    {
        this.tx = tx;
    }

    // PORT: Python returns the result of the driver. No upstream site reads it, so it is a Task (R9).
    // PORT: IAsyncQueryRunner.RunAsync takes no CancellationToken (R9, open item O9). The port checks the token
    // before the driver call.
    internal override async Task RunAsync(
        string query,
        OrderedDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await tx.RunAsync(query, Neo4jDriver.ToDriverParameters(parameters));
    }
}

// PORT: Python has no such class. Neo4jDriver.session returns the AsyncSession of the neo4j package, which has the
// members of GraphDriverSession. A C# session must derive from GraphDriverSession, so this class wraps the
// IAsyncSession of Neo4j.Driver.
// PORT: R9 names this adapter. The calls of IAsyncSession take no CancellationToken (R9, open item O9).
internal sealed class Neo4jSession : GraphDriverSession
{
    private readonly IAsyncSession session;

    internal Neo4jSession(IAsyncSession session)
    {
        this.session = session;
    }

    // PORT: AsyncSession.__aexit__ closes the session. AsyncSession.close drops the error of the result that the
    // session did not consume.
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await session.DisposeAsync();
        }
        catch (Neo4jException)
        {
        }
    }

    // PORT: AsyncSession.run. A run call does not consume its result cursor, as in Python (R9).
    internal override async Task RunAsync(
        string query,
        OrderedDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await session.RunAsync(query, Neo4jDriver.ToDriverParameters(parameters));
    }

    // PORT: AsyncSession.close. It drops the error of the result that the session did not consume. No token check,
    // because close is a cleanup call (R8).
    internal override async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await session.CloseAsync();
        }
        catch (Neo4jException)
        {
        }
    }

    // PORT: AsyncSession.execute_write. The work function of the driver runs on the thread pool, so it starts with
    // Asyncio.OnLoopAsync (R8). It receives the managed transaction as a Neo4jTransaction (R9).
    internal override async Task ExecuteWriteAsync(
        Func<Transaction, Task> func,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await session.ExecuteWriteAsync(tx => Asyncio.OnLoopAsync(() => func(new Neo4jTransaction(tx))));
    }

    // PORT: AsyncSession.execute_write of a work function that `unit_of_work(timeout=timeout)` marks (decision D52,
    // open item O6). The timeout goes in the transaction configuration of Neo4j.Driver 6.5.1 (R9). The work function
    // runs as in the member above.
    internal override async Task ExecuteWriteAsync(
        Func<Transaction, Task> func,
        double timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var txTimeout = TxTimeout(timeout);
        await session.ExecuteWriteAsync(
            tx => Asyncio.OnLoopAsync(() => func(new Neo4jTransaction(tx))),
            b => b.WithTimeout(txTimeout));
    }

    // PORT: tx_timeout_as_ms of the Python driver (neo4j 6.1.0, neo4j/_async/io/_bolt.py:1052), which sends the
    // timeout to the server in whole milliseconds. Python rounds half to even and sends 1 for a value in (0, 0.5 ms).
    // Neo4j.Driver 6.5.1 rounds a TimeSpan up to the next millisecond, so the conversion makes a TimeSpan of whole
    // milliseconds, and the driver sends the same number as Python. A negative value and nan raise ValueError
    // (ArgumentException, R4), and an infinity raises OverflowError (OverflowException). Python sends a value up to
    // 2**63 - 1 ms. A TimeSpan holds at most 922337203685477 ms (about 29,000 years): above that, this throws
    // OverflowException, where Python sends the value.
    private static TimeSpan TxTimeout(double timeout)
    {
        if (timeout < 0)
        {
            throw new ArgumentException("Timeout must be a positive number or 0.");
        }

        var ms = Py.Int(Math.Round(1000 * timeout, MidpointRounding.ToEven));
        if (ms == 0 && timeout > 0)
        {
            // Special case for 0 < timeout < 0.5 ms.
            // This would be rounded to 0 ms, but the server interprets this as
            // infinite timeout. So we round to the smallest possible timeout: 1 ms.
            ms = 1;
        }

        return TimeSpan.FromTicks(checked(ms * TimeSpan.TicksPerMillisecond));
    }
}
