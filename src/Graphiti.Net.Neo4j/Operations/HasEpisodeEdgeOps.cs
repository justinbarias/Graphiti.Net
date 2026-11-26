// Ported from graphiti_core/driver/neo4j/operations/has_episode_edge_ops.py
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
internal static partial class HasEpisodeEdgeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(HasEpisodeEdgeOps));

    // TODO(port): _has_episode_edge_from_record
    internal static HasEpisodeEdge HasEpisodeEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): _has_episode_edge_from_record");
    }
}

internal sealed class Neo4jHasEpisodeEdgeOperations : HasEpisodeEdgeOperations
{
    // TODO(port): Neo4jHasEpisodeEdgeOperations.save
    internal override Task SaveAsync(
        QueryExecutor executor,
        HasEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jHasEpisodeEdgeOperations.save");
    }

    // TODO(port): Neo4jHasEpisodeEdgeOperations.save_bulk
    internal override Task SaveBulkAsync(
        QueryExecutor executor,
        List<HasEpisodeEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jHasEpisodeEdgeOperations.save_bulk");
    }

    // TODO(port): Neo4jHasEpisodeEdgeOperations.delete
    internal override Task DeleteAsync(
        QueryExecutor executor,
        HasEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jHasEpisodeEdgeOperations.delete");
    }

    // TODO(port): Neo4jHasEpisodeEdgeOperations.delete_by_uuids
    internal override Task DeleteByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jHasEpisodeEdgeOperations.delete_by_uuids");
    }

    // TODO(port): Neo4jHasEpisodeEdgeOperations.get_by_uuid
    internal override Task<HasEpisodeEdge> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jHasEpisodeEdgeOperations.get_by_uuid");
    }

    // TODO(port): Neo4jHasEpisodeEdgeOperations.get_by_uuids
    internal override Task<List<HasEpisodeEdge>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jHasEpisodeEdgeOperations.get_by_uuids");
    }

    // TODO(port): Neo4jHasEpisodeEdgeOperations.get_by_group_ids
    internal override Task<List<HasEpisodeEdge>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jHasEpisodeEdgeOperations.get_by_group_ids");
    }
}
