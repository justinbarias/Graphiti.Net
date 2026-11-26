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

using global::Neo4j.Driver;
using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Operations;
using Graphiti.Internal;
using Microsoft.Extensions.Logging;

// PORT: the module class of R2 and R5. The module name equals the class name, so it has the suffix Module. The unit
// adds the Log class with the log calls.
internal static partial class Neo4jDriverModule
{
    internal static ILogger Logger => PortServices.Logger(typeof(Neo4jDriverModule));
}

// PORT: the class attributes provider = GraphProvider.NEO4J and default_group_id = '' are instance properties of
// GraphDriver. The constructor sets Provider. DefaultGroupId already has the value ''.
internal sealed class Neo4jDriver : GraphDriver
{
    // PORT: type(driver).__name__ (R15).
    internal override string PythonName => "Neo4jDriver";

    // PORT: the index build that __init__ starts as a background task is not ported (R17, G20). The unit writes
    // `// PORT: G20` at that place. aoss_client is set to None and only the Neptune driver reads it.
    // TODO(port): no rule for aoss_client, an attribute that the Neo4j driver sets to None
    // TODO(port): Neo4jDriver.__init__
    internal Neo4jDriver(string uri, string? user, string? password, string database = "neo4j")
    {
        throw new NotSupportedException("TODO(port): Neo4jDriver.__init__");
    }

    // PORT: the public surface design lets a host give its own IDriver (AddNeo4j with a factory). Python has one
    // constructor, which builds the client from uri, user, and password.
    // TODO(port): no rule for a constructor that takes the host's IDriver
    internal Neo4jDriver(IDriver client, string database = "neo4j")
    {
        throw new NotSupportedException("TODO(port): Neo4jDriver.__init__ with a host IDriver");
    }

    internal IDriver Client { get; set; }

    // --- Operations properties ---

    // TODO(port): Neo4jDriver.entity_node_ops
    internal override EntityNodeOperations EntityNodeOps => throw new NotSupportedException("TODO(port): Neo4jDriver.entity_node_ops");

    // TODO(port): Neo4jDriver.episode_node_ops
    internal override EpisodeNodeOperations EpisodeNodeOps => throw new NotSupportedException("TODO(port): Neo4jDriver.episode_node_ops");

    // TODO(port): Neo4jDriver.community_node_ops
    internal override CommunityNodeOperations CommunityNodeOps => throw new NotSupportedException("TODO(port): Neo4jDriver.community_node_ops");

    // TODO(port): Neo4jDriver.saga_node_ops
    internal override SagaNodeOperations SagaNodeOps => throw new NotSupportedException("TODO(port): Neo4jDriver.saga_node_ops");

    // TODO(port): Neo4jDriver.entity_edge_ops
    internal override EntityEdgeOperations EntityEdgeOps => throw new NotSupportedException("TODO(port): Neo4jDriver.entity_edge_ops");

    // TODO(port): Neo4jDriver.episodic_edge_ops
    internal override EpisodicEdgeOperations EpisodicEdgeOps => throw new NotSupportedException("TODO(port): Neo4jDriver.episodic_edge_ops");

    // TODO(port): Neo4jDriver.community_edge_ops
    internal override CommunityEdgeOperations CommunityEdgeOps => throw new NotSupportedException("TODO(port): Neo4jDriver.community_edge_ops");

    // TODO(port): Neo4jDriver.has_episode_edge_ops
    internal override HasEpisodeEdgeOperations HasEpisodeEdgeOps => throw new NotSupportedException("TODO(port): Neo4jDriver.has_episode_edge_ops");

    // TODO(port): Neo4jDriver.next_episode_edge_ops
    internal override NextEpisodeEdgeOperations NextEpisodeEdgeOps => throw new NotSupportedException("TODO(port): Neo4jDriver.next_episode_edge_ops");

    // TODO(port): Neo4jDriver.search_ops
    internal override SearchOperations SearchOps => throw new NotSupportedException("TODO(port): Neo4jDriver.search_ops");

    // TODO(port): Neo4jDriver.graph_ops
    internal override GraphMaintenanceOperations GraphOps => throw new NotSupportedException("TODO(port): Neo4jDriver.graph_ops");

    // PORT: an async context manager that commits or rolls back takes the body as a lambda (R16).
    // TODO(port): Neo4jDriver.transaction
    internal override Task TransactionAsync(
        Func<Transaction, Task> body,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jDriver.transaction");
    }

    // PORT: the keys params, database_, and routing_ of the parameters are driver options (R2). The parameter values
    // and the records are converted at the driver boundary (R9). The summary and the keys of the EagerResult are the
    // second and the third items.
    // TODO(port): Neo4jDriver.execute_query
    internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
        string cypherQuery,
        OrderedDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jDriver.execute_query");
    }

    // PORT: Python returns the session of the neo4j package, which has the shape of GraphDriverSession. The port wraps
    // it in Neo4jSession.
    // TODO(port): Neo4jDriver.session
    internal override GraphDriverSession Session(string? database = null)
    {
        throw new NotSupportedException("TODO(port): Neo4jDriver.session");
    }

    // TODO(port): Neo4jDriver.close
    internal override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jDriver.close");
    }

    // TODO(port): Neo4jDriver.delete_all_indexes
    internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jDriver.delete_all_indexes");
    }

    // PORT: EagerResult | None is the result tuple of ExecuteQueryAsync, or null.
    // TODO(port): Neo4jDriver._execute_index_query
    private Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)?> ExecuteIndexQueryAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jDriver._execute_index_query");
    }

    // TODO(port): Neo4jDriver.build_indices_and_constraints
    internal override Task BuildIndicesAndConstraintsAsync(
        bool deleteExisting = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jDriver.build_indices_and_constraints");
    }

    // PORT: GraphDriver has no health_check, so this is not an override.
    // PORT: print(x) is Console.Out.WriteLine(x) (R4).
    // TODO(port): Neo4jDriver.health_check
    internal Task HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jDriver.health_check");
    }

    // PORT: R9. The four members below have no Python name. ExecuteQueryAsync, Neo4jTransaction, and Neo4jSession
    // convert each parameter value before the driver call and each record after it, so the members are internal.
    // TODO(port): R9, the conversion of the parameters
    internal static Dictionary<string, object?> ToDriverParameters(OrderedDictionary<string, object?> parameters)
    {
        throw new NotSupportedException("TODO(port): R9 parameter conversion");
    }

    // TODO(port): R9, the conversion of one parameter value, at each depth
    internal static object? ToDriverValue(object? value)
    {
        throw new NotSupportedException("TODO(port): R9 parameter value conversion");
    }

    // TODO(port): R9, the conversion of one record
    internal static OrderedDictionary<string, object?> FromDriverRecord(IRecord record)
    {
        throw new NotSupportedException("TODO(port): R9 record conversion");
    }

    // TODO(port): R9, the conversion of one record value, at each depth
    internal static object? FromDriverValue(object? value)
    {
        throw new NotSupportedException("TODO(port): R9 record value conversion");
    }
}

internal sealed class Neo4jTransaction : Transaction
{
    // TODO(port): _Neo4jTransaction.__init__
    internal Neo4jTransaction(IAsyncQueryRunner tx)
    {
        throw new NotSupportedException("TODO(port): _Neo4jTransaction.__init__");
    }

    // PORT: Python returns the result of the driver. No upstream site reads it, so it is a Task (R9).
    // TODO(port): _Neo4jTransaction.run
    internal override Task RunAsync(
        string query,
        OrderedDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _Neo4jTransaction.run");
    }
}

// PORT: Python has no such class. Neo4jDriver.session returns the AsyncSession of the neo4j package, which has the
// members of GraphDriverSession. A C# session must derive from GraphDriverSession, so this class wraps the
// IAsyncSession of Neo4j.Driver.
// PORT: R9 names this adapter.
internal sealed class Neo4jSession : GraphDriverSession
{
    // TODO(port): Neo4jSession constructor
    internal Neo4jSession(IAsyncSession session)
    {
        throw new NotSupportedException("TODO(port): Neo4jSession constructor");
    }

    // TODO(port): AsyncSession.__aexit__
    public override ValueTask DisposeAsync()
    {
        throw new NotSupportedException("TODO(port): AsyncSession.__aexit__");
    }

    // TODO(port): AsyncSession.run
    internal override Task RunAsync(
        string query,
        OrderedDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): AsyncSession.run");
    }

    // TODO(port): AsyncSession.close
    internal override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): AsyncSession.close");
    }

    // TODO(port): AsyncSession.execute_write
    internal override Task ExecuteWriteAsync(
        Func<Transaction, Task> func,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): AsyncSession.execute_write");
    }
}
