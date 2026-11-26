// Ported from graphiti_core/driver/operations/graph_ops.py
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

namespace Graphiti.Core.Driver.Operations;

// PORT: an abstract method has no body, as in Python.
internal abstract class GraphMaintenanceOperations
{
    internal abstract Task ClearDataAsync(
        QueryExecutor executor,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default);

    internal abstract Task BuildIndicesAndConstraintsAsync(
        QueryExecutor executor,
        bool deleteExisting = false,
        CancellationToken cancellationToken = default);

    internal abstract Task DeleteAllIndexesAsync(
        QueryExecutor executor,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<List<EntityNode>>> GetCommunityClustersAsync(
        QueryExecutor executor,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default);

    // TODO(port): remove_communities
    internal virtual Task RemoveCommunitiesAsync(
        QueryExecutor executor,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): remove_communities");
    }

    internal abstract Task DetermineEntityCommunityAsync(
        QueryExecutor executor,
        EntityNode entity,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EntityNode>> GetMentionedNodesAsync(
        QueryExecutor executor,
        List<EpisodicNode> episodes,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<CommunityNode>> GetCommunitiesByNodesAsync(
        QueryExecutor executor,
        List<EntityNode> nodes,
        CancellationToken cancellationToken = default);
}
