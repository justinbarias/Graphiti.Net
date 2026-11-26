// Ported from graphiti_core/driver/driver.py
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

namespace Graphiti.Core.Driver;

using Graphiti.Core.Driver.Operations;
using Graphiti.PyCompat;

// PORT: the module class of R2. The module logger has no log call, so it has no Logger (R5). load_dotenv() is not ported (R17).
// PORT: os.environ.get('X', 'default') is PyEnv.Str("X") ?? "default".
internal static class DriverModule
{
    internal const long DefaultSize = 10;

    internal static readonly string EntityIndexName = PyEnv.Str("ENTITY_INDEX_NAME") ?? "entities";

    internal static readonly string EpisodeIndexName = PyEnv.Str("EPISODE_INDEX_NAME") ?? "episodes";

    internal static readonly string CommunityIndexName = PyEnv.Str("COMMUNITY_INDEX_NAME") ?? "communities";

    internal static readonly string EntityEdgeIndexName = PyEnv.Str("ENTITY_EDGE_INDEX_NAME") ?? "entity_edges";
}

// PORT: the pilot (plan 0003, task 6) ported this enum for real (RULES.md R24).
internal enum GraphProvider
{
    [PyEnumMember("NEO4J", "neo4j")]
    Neo4j,
    [PyEnumMember("FALKORDB", "falkordb")]
    Falkordb,
    [PyEnumMember("NEPTUNE", "neptune")]
    Neptune,
    [PyEnumMember("RELATE", "relate")]
    Relate,
}

// PORT: an async context manager is an IAsyncDisposable (R16). `async with driver.session() as session` is
// `await using var session = driver.Session()`. __aenter__ returns the session itself, so it has no member, and
// DisposeAsync is __aexit__, which reads no exception.
internal abstract class GraphDriverSession : IAsyncDisposable
{
    internal GraphProvider Provider { get; set; }

    // No cleanup needed for Falkor, but method must exist
    public abstract ValueTask DisposeAsync();

    // PORT: **kwargs holds the query parameters (R2). No upstream site reads the result, so it is a Task (R9).
    internal abstract Task RunAsync(
        string query,
        OrderedDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);

    internal abstract Task CloseAsync(CancellationToken cancellationToken = default);

    // PORT: the extra arguments of Python are closed over by the lambda (R9). The work function receives the
    // managed write transaction. The upstream callers annotate it as GraphDriverSession, and each one calls only
    // tx.run on it, so the type is Transaction. Each upstream work function returns None, so the result is a Task.
    internal abstract Task ExecuteWriteAsync(
        Func<Transaction, Task> func,
        CancellationToken cancellationToken = default);
}

// PORT: R15. `hasattr(session, 'query_timeout_ms')` in utils/bulk_utils.py:195 is a test for this interface. It has no
// Python class. A session of a provider that takes a query timeout implements it.
internal interface IQueryTimeoutSession
{
    long? QueryTimeoutMs { get; set; }
}

internal abstract class GraphDriver : QueryExecutor
{
    // PORT: type(driver).__name__ (R15).
    internal abstract string PythonName { get; }

    internal const GraphProvider Relate = GraphProvider.Relate;

    internal GraphProvider Provider { get; set; }

    internal string FulltextSyntax { get; set; } = string.Empty; // Neo4j (default) syntax does not require a prefix for fulltext queries

    // PORT: Python declares _database with no value. The driver constructor sets it.
    internal string Database { get; set; } = string.Empty;

    internal string DefaultGroupId { get; set; } = string.Empty;

    // Legacy interfaces (kept for backwards compatibility during Phase 1)
    internal SearchInterface? SearchInterface { get; set; }

    internal GraphOperationsInterface? GraphOperationsInterface { get; set; }

    internal abstract override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
        string cypherQuery,
        OrderedDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default);

    internal abstract GraphDriverSession Session(string? database = null);

    // PORT: Python declares close as a sync method, and each driver overrides it with an async one that callers await.
    internal abstract Task CloseAsync(CancellationToken cancellationToken = default);

    // PORT: Python declares a sync method that returns a coroutine, and callers await it.
    internal abstract Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default);

    // TODO(port): with_database
    internal GraphDriver WithDatabase(string database)
    {
        throw new NotSupportedException("TODO(port): with_database");
    }

    internal abstract Task BuildIndicesAndConstraintsAsync(
        bool deleteExisting = false,
        CancellationToken cancellationToken = default);

    // TODO(port): clone
    internal virtual GraphDriver Clone(string database)
    {
        throw new NotSupportedException("TODO(port): clone");
    }

    internal virtual string BuildFulltextQuery(string query, List<string>? groupIds = null, long maxQueryLength = 128)
    {
        throw new NotImplementedException("build_fulltext_query not implemented for " + Py.Str(Provider));
    }

    // --- New operations interfaces ---

    // PORT: an async context manager that reads the exception (commit or roll back) takes the body as a lambda (R16).
    // TODO(port): transaction
    internal virtual Task TransactionAsync(
        Func<Transaction, Task> body,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): transaction");
    }

    internal virtual EntityNodeOperations? EntityNodeOps => null;

    internal virtual EpisodeNodeOperations? EpisodeNodeOps => null;

    internal virtual CommunityNodeOperations? CommunityNodeOps => null;

    internal virtual SagaNodeOperations? SagaNodeOps => null;

    internal virtual EntityEdgeOperations? EntityEdgeOps => null;

    internal virtual EpisodicEdgeOperations? EpisodicEdgeOps => null;

    internal virtual CommunityEdgeOperations? CommunityEdgeOps => null;

    internal virtual HasEpisodeEdgeOperations? HasEpisodeEdgeOps => null;

    internal virtual NextEpisodeEdgeOperations? NextEpisodeEdgeOps => null;

    internal virtual SearchOperations? SearchOps => null;

    internal virtual GraphMaintenanceOperations? GraphOps => null;
}

internal sealed class SessionTransaction : Transaction
{
    // TODO(port): _SessionTransaction.__init__
    internal SessionTransaction(GraphDriverSession session)
    {
        throw new NotSupportedException("TODO(port): _SessionTransaction.__init__");
    }

    // TODO(port): _SessionTransaction.run
    internal override Task RunAsync(
        string query,
        OrderedDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _SessionTransaction.run");
    }
}
