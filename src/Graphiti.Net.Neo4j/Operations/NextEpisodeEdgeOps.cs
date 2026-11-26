// Ported from graphiti_core/driver/neo4j/operations/next_episode_edge_ops.py
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
internal static partial class NextEpisodeEdgeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(NextEpisodeEdgeOps));

    // TODO(port): _next_episode_edge_from_record
    internal static NextEpisodeEdge NextEpisodeEdgeFromRecord(OrderedDictionary<string, object?> record)
    {
        throw new NotSupportedException("TODO(port): _next_episode_edge_from_record");
    }
}

internal sealed class Neo4jNextEpisodeEdgeOperations : NextEpisodeEdgeOperations
{
    // TODO(port): Neo4jNextEpisodeEdgeOperations.save
    internal override Task SaveAsync(
        QueryExecutor executor,
        NextEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jNextEpisodeEdgeOperations.save");
    }

    // TODO(port): Neo4jNextEpisodeEdgeOperations.save_bulk
    internal override Task SaveBulkAsync(
        QueryExecutor executor,
        List<NextEpisodeEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jNextEpisodeEdgeOperations.save_bulk");
    }

    // TODO(port): Neo4jNextEpisodeEdgeOperations.delete
    internal override Task DeleteAsync(
        QueryExecutor executor,
        NextEpisodeEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jNextEpisodeEdgeOperations.delete");
    }

    // TODO(port): Neo4jNextEpisodeEdgeOperations.delete_by_uuids
    internal override Task DeleteByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jNextEpisodeEdgeOperations.delete_by_uuids");
    }

    // TODO(port): Neo4jNextEpisodeEdgeOperations.get_by_uuid
    internal override Task<NextEpisodeEdge> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jNextEpisodeEdgeOperations.get_by_uuid");
    }

    // TODO(port): Neo4jNextEpisodeEdgeOperations.get_by_uuids
    internal override Task<List<NextEpisodeEdge>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jNextEpisodeEdgeOperations.get_by_uuids");
    }

    // TODO(port): Neo4jNextEpisodeEdgeOperations.get_by_group_ids
    internal override Task<List<NextEpisodeEdge>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jNextEpisodeEdgeOperations.get_by_group_ids");
    }
}
