// Ported from graphiti_core/driver/neo4j/operations/community_node_ops.py
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
internal static partial class CommunityNodeOps
{
    internal static ILogger Logger => PortServices.Logger(typeof(CommunityNodeOps));
}

internal sealed class Neo4jCommunityNodeOperations : CommunityNodeOperations
{
    // TODO(port): Neo4jCommunityNodeOperations.save
    internal override Task SaveAsync(
        QueryExecutor executor,
        CommunityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jCommunityNodeOperations.save");
    }

    // TODO(port): Neo4jCommunityNodeOperations.save_bulk
    internal override Task SaveBulkAsync(
        QueryExecutor executor,
        List<CommunityNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jCommunityNodeOperations.save_bulk");
    }

    // TODO(port): Neo4jCommunityNodeOperations.delete
    internal override Task DeleteAsync(
        QueryExecutor executor,
        CommunityNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jCommunityNodeOperations.delete");
    }

    // TODO(port): Neo4jCommunityNodeOperations.delete_by_group_id
    internal override Task DeleteByGroupIdAsync(
        QueryExecutor executor,
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jCommunityNodeOperations.delete_by_group_id");
    }

    // TODO(port): Neo4jCommunityNodeOperations.delete_by_uuids
    internal override Task DeleteByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jCommunityNodeOperations.delete_by_uuids");
    }

    // TODO(port): Neo4jCommunityNodeOperations.get_by_uuid
    internal override Task<CommunityNode> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jCommunityNodeOperations.get_by_uuid");
    }

    // TODO(port): Neo4jCommunityNodeOperations.get_by_uuids
    internal override Task<List<CommunityNode>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jCommunityNodeOperations.get_by_uuids");
    }

    // TODO(port): Neo4jCommunityNodeOperations.get_by_group_ids
    internal override Task<List<CommunityNode>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jCommunityNodeOperations.get_by_group_ids");
    }

    // TODO(port): Neo4jCommunityNodeOperations.load_name_embedding
    internal override Task LoadNameEmbeddingAsync(
        QueryExecutor executor,
        CommunityNode node,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jCommunityNodeOperations.load_name_embedding");
    }
}
