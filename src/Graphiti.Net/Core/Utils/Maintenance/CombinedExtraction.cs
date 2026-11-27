// Ported from graphiti_core/utils/maintenance/combined_extraction.py
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

using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Microsoft.Extensions.Logging;

// PORT: the module class has the name of the module (R2). The response model of the prompt has the same name in
// Graphiti.Core.Prompts, so the body names it as Prompts.CombinedExtraction.
internal static class CombinedExtraction
{
    private static ILogger Logger => PortServices.Logger(typeof(CombinedExtraction));

    internal const long TimestampBatchSize = 20;

    internal const long TimestampBatchAttempts = 2;

    // PORT: internal, because judge cases read it (R2).
    // TODO(port): _episode_uuids_for_fact
    internal static List<string> EpisodeUuidsForFact(CombinedFact edgeData, List<EpisodicNode> episodes)
    {
        throw new NotSupportedException("TODO(port): _episode_uuids_for_fact");
    }

    // PORT: `episode: EpisodicNode | list[EpisodicNode]` is EpisodeOrEpisodes (node_operations). The nested coroutine
    // extract_group_timestamps is a lambda for Helpers.SemaphoreGatherAsync (R8). `list({'Entity', ...})` follows
    // R14 (decision D18).
    // TODO(port): extract_nodes_and_edges
    internal static Task<(List<EntityNode>, List<EntityEdge>, OrderedDictionary<string, List<long>>)>
        ExtractNodesAndEdgesAsync(
            GraphitiClients clients,
            EpisodeOrEpisodes episode,
            List<EpisodicNode> previousEpisodes,
            OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
            List<string>? excludedEntityTypes = null,
            OrderedDictionary<(string, string), List<string>>? edgeTypeMap = null,
            OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes = null,
            bool strictEdgeTypes = false,
            string? customExtractionInstructions = null,
            List<EpisodicNode>? timeline = null,
            CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): extract_nodes_and_edges");
    }
}
