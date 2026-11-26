// Ported from graphiti_core/driver/operations/saga_node_ops.py
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
internal abstract class SagaNodeOperations
{
    internal abstract Task SaveAsync(
        QueryExecutor executor,
        SagaNode node,
        Transaction? tx = null,
        CancellationToken cancellationToken = default);

    internal abstract Task SaveBulkAsync(
        QueryExecutor executor,
        List<SagaNode> nodes,
        Transaction? tx = null,
        long batchSize = 100,
        CancellationToken cancellationToken = default);

    internal abstract Task DeleteAsync(
        QueryExecutor executor,
        SagaNode node,
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

    internal abstract Task<SagaNode> GetByUuidAsync(
        QueryExecutor executor,
        string uuid,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<SagaNode>> GetByUuidsAsync(
        QueryExecutor executor,
        List<string> uuids,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<SagaNode>> GetByGroupIdsAsync(
        QueryExecutor executor,
        List<string> groupIds,
        long? limit = null,
        string? uuidCursor = null,
        CancellationToken cancellationToken = default);
}
