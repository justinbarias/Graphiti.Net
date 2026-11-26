// Ported from graphiti_core/driver/operations/episode_node_ops.py
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

using Graphiti.PyCompat;

// PORT: an abstract method has no body, as in Python.
internal abstract class EpisodeNodeOperations
{
    internal abstract Task SaveAsync(
        QueryExecutor executor,
        EpisodicNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default);

    internal abstract Task SaveBulkAsync(
        QueryExecutor executor,
        List<EpisodicNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default);

    internal abstract Task DeleteAsync(
        QueryExecutor executor,
        EpisodicNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default);

    internal abstract Task DeleteByGroupIdAsync(
        QueryExecutor executor,
        string groupId,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default);

    internal abstract Task DeleteByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default);

    internal abstract Task<EpisodicNode> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EpisodicNode>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EpisodicNode>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EpisodicNode>> GetByEntityNodeUuidAsync(
        QueryExecutor executor,
        string entityNodeUuid,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EpisodicNode>> RetrieveEpisodesAsync(
        QueryExecutor executor,
        PyDateTime referenceTime,
        long lastN = 3,
        List<string>? groupIds = null,
        string? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default);
}
