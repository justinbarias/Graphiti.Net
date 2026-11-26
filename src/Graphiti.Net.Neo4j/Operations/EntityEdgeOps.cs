// Ported from graphiti_core/driver/neo4j/operations/entity_edge_ops.py
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

// PORT: the module class of R2 and R5, with the logger. The unit adds the Log class with the log calls.
internal static partial class EntityEdgeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(EntityEdgeOps));
}

internal sealed class Neo4jEntityEdgeOperations : EntityEdgeOperations
{
    // TODO(port): Neo4jEntityEdgeOperations.save
    internal override Task SaveAsync(
        QueryExecutor executor,
        EntityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jEntityEdgeOperations.save");
    }

    // TODO(port): Neo4jEntityEdgeOperations.save_bulk
    internal override Task SaveBulkAsync(
        QueryExecutor executor,
        List<EntityEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jEntityEdgeOperations.save_bulk");
    }

    // TODO(port): Neo4jEntityEdgeOperations.delete
    internal override Task DeleteAsync(
        QueryExecutor executor,
        EntityEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jEntityEdgeOperations.delete");
    }

    // TODO(port): Neo4jEntityEdgeOperations.delete_by_uuids
    internal override Task DeleteByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jEntityEdgeOperations.delete_by_uuids");
    }

    // TODO(port): Neo4jEntityEdgeOperations.get_by_uuid
    internal override Task<EntityEdge> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jEntityEdgeOperations.get_by_uuid");
    }

    // TODO(port): Neo4jEntityEdgeOperations.get_by_uuids
    internal override Task<List<EntityEdge>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jEntityEdgeOperations.get_by_uuids");
    }

    // TODO(port): Neo4jEntityEdgeOperations.get_by_group_ids
    internal override Task<List<EntityEdge>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jEntityEdgeOperations.get_by_group_ids");
    }

    // TODO(port): Neo4jEntityEdgeOperations.get_between_nodes
    internal override Task<List<EntityEdge>> GetBetweenNodesAsync(
        QueryExecutor executor,
        string sourceNodeUuid,
        string targetNodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jEntityEdgeOperations.get_between_nodes");
    }

    // TODO(port): Neo4jEntityEdgeOperations.get_by_node_uuid
    internal override Task<List<EntityEdge>> GetByNodeUuidAsync(
        QueryExecutor executor,
        string nodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jEntityEdgeOperations.get_by_node_uuid");
    }

    // TODO(port): Neo4jEntityEdgeOperations.load_embeddings
    internal override Task LoadEmbeddingsAsync(
        QueryExecutor executor,
        EntityEdge edge,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jEntityEdgeOperations.load_embeddings");
    }

    // TODO(port): Neo4jEntityEdgeOperations.load_embeddings_bulk
    internal override Task LoadEmbeddingsBulkAsync(
        QueryExecutor executor,
        List<EntityEdge> edges,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jEntityEdgeOperations.load_embeddings_bulk");
    }
}
