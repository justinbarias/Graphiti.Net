// Ported from graphiti_core/driver/operations/episodic_edge_ops.py
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
internal abstract class EpisodicEdgeOperations
{
    internal abstract Task SaveAsync(
        QueryExecutor executor,
        EpisodicEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default);

    internal abstract Task SaveBulkAsync(
        QueryExecutor executor,
        List<EpisodicEdge> edges,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default);

    internal abstract Task DeleteAsync(
        QueryExecutor executor,
        EpisodicEdge edge,
        Transaction? tx = null,
        CancellationToken cancellationToken = default);

    internal abstract Task DeleteByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        Transaction? tx = null,
        CancellationToken cancellationToken = default);

    internal abstract Task<EpisodicEdge> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EpisodicEdge>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<EpisodicEdge>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default);
}
