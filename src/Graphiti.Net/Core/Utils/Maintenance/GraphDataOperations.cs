// Ported from graphiti_core/utils/maintenance/graph_data_operations.py
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

namespace Graphiti.Core.Utils.Maintenance;

using Graphiti.Core.Driver;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

internal static class GraphDataOperations
{
    internal const long EpisodeWindowLen = 3;

    private static ILogger Logger => PortServices.Logger(typeof(GraphDataOperations));

    // PORT: no return annotation. The function returns the result of GraphOperationsInterface.clear_data, which is a
    // Task with no result (TYPE_HINTS.md, R20). The nested work functions delete_all and delete_group_ids are lambdas
    // that the unit passes to ExecuteWriteAsync (R9).
    // TODO(port): clear_data
    internal static Task ClearDataAsync(
        GraphDriver driver,
        List<string>? groupIds = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): clear_data");
    }

    // TODO(port): retrieve_episodes
    internal static Task<List<EpisodicNode>> RetrieveEpisodesAsync(
        GraphDriver driver,
        PyDateTime referenceTime,
        long lastN = EpisodeWindowLen,
        List<string>? groupIds = null,
        EpisodeType? source = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): retrieve_episodes");
    }
}
