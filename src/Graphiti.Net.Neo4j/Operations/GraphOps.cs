// Ported from graphiti_core/driver/neo4j/operations/graph_ops.py
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

// PORT: the module logger has no log call, and the module has no function or constant, so it has no
// module class (R2, R5).
internal sealed class Neo4jGraphMaintenanceOperations : GraphMaintenanceOperations
{
    // TODO(port): Neo4jGraphMaintenanceOperations.clear_data
    internal override Task ClearDataAsync(
        QueryExecutor executor,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jGraphMaintenanceOperations.clear_data");
    }

    // TODO(port): Neo4jGraphMaintenanceOperations.build_indices_and_constraints
    internal override Task BuildIndicesAndConstraintsAsync(
        QueryExecutor executor,
        bool deleteExisting = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jGraphMaintenanceOperations.build_indices_and_constraints");
    }

    // TODO(port): Neo4jGraphMaintenanceOperations.delete_all_indexes
    internal override Task DeleteAllIndexesAsync(
        QueryExecutor executor,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jGraphMaintenanceOperations.delete_all_indexes");
    }

    // TODO(port): Neo4jGraphMaintenanceOperations.get_community_clusters
    internal override Task<List<List<EntityNode>>> GetCommunityClustersAsync(
        QueryExecutor executor,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jGraphMaintenanceOperations.get_community_clusters");
    }

    // TODO(port): Neo4jGraphMaintenanceOperations.determine_entity_community
    internal override Task DetermineEntityCommunityAsync(
        QueryExecutor executor,
        EntityNode entity,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jGraphMaintenanceOperations.determine_entity_community");
    }

    // TODO(port): Neo4jGraphMaintenanceOperations.get_mentioned_nodes
    internal override Task<List<EntityNode>> GetMentionedNodesAsync(
        QueryExecutor executor,
        List<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jGraphMaintenanceOperations.get_mentioned_nodes");
    }

    // TODO(port): Neo4jGraphMaintenanceOperations.get_communities_by_nodes
    internal override Task<List<CommunityNode>> GetCommunitiesByNodesAsync(
        QueryExecutor executor,
        List<EntityNode> nodes,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Neo4jGraphMaintenanceOperations.get_communities_by_nodes");
    }
}
