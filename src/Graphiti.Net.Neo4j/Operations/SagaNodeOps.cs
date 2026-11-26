// Ported from graphiti_core/driver/neo4j/operations/saga_node_ops.py
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

namespace Graphiti.Neo4j.Operations;

using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Operations;
using Graphiti.Internal;
using Microsoft.Extensions.Logging;

// PORT: the module class of R2 and R5, with the logger and the record function. The unit adds the Log class with the log calls.
internal static partial class SagaNodeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(SagaNodeOps));

    // TODO(port): _saga_node_from_record
    internal static SagaNode SagaNodeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): _saga_node_from_record");
    }
}

internal sealed class Neo4jSagaNodeOperations : SagaNodeOperations
{
    // TODO(port): Neo4jSagaNodeOperations.save
    internal override Task SaveAsync(
        QueryExecutor executor,
        SagaNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSagaNodeOperations.save");
    }

    // TODO(port): Neo4jSagaNodeOperations.save_bulk
    internal override Task SaveBulkAsync(
        QueryExecutor executor,
        List<SagaNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSagaNodeOperations.save_bulk");
    }

    // TODO(port): Neo4jSagaNodeOperations.delete
    internal override Task DeleteAsync(
        QueryExecutor executor,
        SagaNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSagaNodeOperations.delete");
    }

    // TODO(port): Neo4jSagaNodeOperations.delete_by_group_id
    internal override Task DeleteByGroupIdAsync(
        QueryExecutor executor,
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSagaNodeOperations.delete_by_group_id");
    }

    // TODO(port): Neo4jSagaNodeOperations.delete_by_uuids
    internal override Task DeleteByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSagaNodeOperations.delete_by_uuids");
    }

    // TODO(port): Neo4jSagaNodeOperations.get_by_uuid
    internal override Task<SagaNode> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSagaNodeOperations.get_by_uuid");
    }

    // TODO(port): Neo4jSagaNodeOperations.get_by_uuids
    internal override Task<List<SagaNode>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSagaNodeOperations.get_by_uuids");
    }

    // TODO(port): Neo4jSagaNodeOperations.get_by_group_ids
    internal override Task<List<SagaNode>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jSagaNodeOperations.get_by_group_ids");
    }
}
