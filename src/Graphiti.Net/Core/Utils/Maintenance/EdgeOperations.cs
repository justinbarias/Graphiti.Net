// Ported from graphiti_core/utils/maintenance/edge_operations.py
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
using Graphiti.Core.Ingestion;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: each private name of this module is internal, because judge cases and the recording of the unit read them
// (R2). `edge_types: dict[str, type[BaseModel]]` is an OrderedDictionary of custom type descriptors (R15).
// TODO(port): no rule for a dict key `tuple[str, str]` of typed data. `edge_type_map: dict[tuple[str, str], list[str]]`
// is OrderedDictionary<(string, string), List<string>>: code iterates it with `.items()` (R14), and a value tuple has
// value equality. A signature that reaches the prompt context is a PyTuple there (R6).
internal static partial class EdgeOperations
{
    private static ILogger Logger => PortServices.Logger(typeof(EdgeOperations));

    internal const string DefaultEdgeName = "RELATES_TO";

    // TODO(port): no rule for a union of a value and a list that the function makes into a list on its first line.
    // `episode_uuid: str | list[str]` is two overloads. Judge cases pass both shapes.
    // TODO(port): build_episodic_edges
    internal static List<EpisodicEdge> BuildEpisodicEdges(
        List<EntityNode> entityNodes,
        string episodeUuid,
        PyDateTime createdAt,
        OrderedDictionary<string, List<long>>? nodeEpisodeIndexMap = null)
    {
        throw new NotSupportedException("TODO(port): build_episodic_edges");
    }

    // TODO(port): build_episodic_edges
    internal static List<EpisodicEdge> BuildEpisodicEdges(
        List<EntityNode> entityNodes,
        List<string> episodeUuid,
        PyDateTime createdAt,
        OrderedDictionary<string, List<long>>? nodeEpisodeIndexMap = null)
    {
        throw new NotSupportedException("TODO(port): build_episodic_edges");
    }

    // TODO(port): build_community_edges
    internal static List<CommunityEdge> BuildCommunityEdges(
        List<EntityNode> entityNodes,
        CommunityNode communityNode,
        PyDateTime createdAt)
    {
        throw new NotSupportedException("TODO(port): build_community_edges");
    }

    // PORT: `episode: EpisodicNode | list[EpisodicNode]` is EpisodeOrEpisodes (node_operations).
    // TODO(port): extract_edges
    internal static Task<(List<EntityEdge>, List<EntityNode>)> ExtractEdgesAsync(
        GraphitiClients clients,
        EpisodeOrEpisodes episode,
        List<EntityNode> nodes,
        List<EpisodicNode> previousEpisodes,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        string groupId = "",
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes = null,
        bool strictEdgeTypes = false,
        string? customExtractionInstructions = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): extract_edges");
    }

    // TODO(port): _pair_search_filter
    internal static SearchFilters PairSearchFilter(EntityEdge edge)
    {
        throw new NotSupportedException("TODO(port): _pair_search_filter");
    }

    // TODO(port): _same_pair_edges
    internal static List<EntityEdge> SamePairEdges(EntityEdge extractedEdge, List<EntityEdge> candidates)
    {
        throw new NotSupportedException("TODO(port): _same_pair_edges");
    }

    // PORT: `referenced_node_uuids` is a PySet<string> in the order of the edges (R14).
    // TODO(port): resolve_extracted_edges
    internal static Task<(List<EntityEdge>, List<EntityEdge>, List<EntityEdge>)> ResolveExtractedEdgesAsync(
        GraphitiClients clients,
        List<EntityEdge> extractedEdges,
        EpisodicNode episode,
        List<EntityNode> entities,
        OrderedDictionary<string, GraphTypeDescriptor> edgeTypes,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        List<EntityEdge>? existingEdgesOverride = null,
        bool strictEdgeTypes = false,
        OrderedDictionary<string, List<string>>? invalidatedBy = null,
        ResolutionTrace? trace = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): resolve_extracted_edges");
    }

    // PORT: `seen` is read and written by key and never iterated, so it is a Dictionary with a value tuple key (R14).
    // TODO(port): find_exact_duplicate_edge
    internal static EntityEdge? FindExactDuplicateEdge(
        EntityEdge edge,
        Dictionary<(string, string, string), EntityEdge> seen)
    {
        throw new NotSupportedException("TODO(port): find_exact_duplicate_edge");
    }

    // PORT: `uuid_entity_map` is read by key only (R14).
    // TODO(port): _edge_types_for_endpoints
    internal static OrderedDictionary<string, GraphTypeDescriptor> EdgeTypesForEndpoints(
        EntityEdge extractedEdge,
        Dictionary<string, EntityNode> uuidEntityMap,
        OrderedDictionary<string, GraphTypeDescriptor> edgeTypes,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap)
    {
        throw new NotSupportedException("TODO(port): _edge_types_for_endpoints");
    }

    // TODO(port): enforce_edge_type_signatures
    internal static (List<EntityEdge>, List<OrderedDictionary<string, GraphTypeDescriptor>>) EnforceEdgeTypeSignatures(
        List<EntityEdge> extractedEdges,
        List<OrderedDictionary<string, GraphTypeDescriptor>> edgeTypesLst,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        bool strictEdgeTypes)
    {
        throw new NotSupportedException("TODO(port): enforce_edge_type_signatures");
    }

    // PORT: `invalidation_candidates.sort(key=...)` sorts the list of the caller in place with a tuple key that holds a
    // datetime (R14, Py.SortInPlace).
    // TODO(port): _apply_temporal_invalidation
    internal static List<EntityEdge> ApplyTemporalInvalidation(
        EntityEdge resolvedEdge,
        List<EntityEdge> invalidationCandidates)
    {
        throw new NotSupportedException("TODO(port): _apply_temporal_invalidation");
    }

    // TODO(port): resolve_edge_contradictions
    internal static List<EntityEdge> ResolveEdgeContradictions(
        EntityEdge resolvedEdge,
        List<EntityEdge> invalidationCandidates)
    {
        throw new NotSupportedException("TODO(port): resolve_edge_contradictions");
    }

    // PORT: `clients` is keyword-only in Python.
    // TODO(port): _extract_edge_timestamps
    internal static Task ExtractEdgeTimestampsAsync(
        LLMClient llmClient,
        EntityEdge edge,
        EpisodicNode? episode,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _extract_edge_timestamps");
    }

    // PORT: `clients` is keyword-only in Python. `**resolver_kwargs` passes `clients` only when it is not None, and the
    // C# call passes `clients` as it is, which is the same default.
    // TODO(port): resolve_extracted_edge
    internal static Task<(EntityEdge, List<EntityEdge>, List<EntityEdge>)> ResolveExtractedEdgeAsync(
        LLMClient llmClient,
        EntityEdge extractedEdge,
        List<EntityEdge> relatedEdges,
        List<EntityEdge> existingEdges,
        EpisodicNode episode,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypeCandidates = null,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): resolve_extracted_edge");
    }
}

// PORT: a @dataclass is a sealed record with { get; set; } properties (R7). `field(default_factory=time)` is
// Py.TimeSeconds() (R8).
internal sealed record EdgeDecision
{
    internal EntityEdge? ExactMatch { get; set; }

    internal List<EntityEdge> Duplicates { get; set; } = [];

    internal List<EntityEdge> Contradicted { get; set; } = [];

    internal double StartedAt { get; set; } = Py.TimeSeconds();
}

internal static partial class EdgeOperations
{
    // PORT: `clients` is keyword-only in Python.
    // TODO(port): decide_edge_against_candidates
    internal static Task<EdgeDecision> DecideEdgeAgainstCandidatesAsync(
        LLMClient llmClient,
        EntityEdge extractedEdge,
        List<EntityEdge> relatedEdges,
        List<EntityEdge> existingEdges,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): decide_edge_against_candidates");
    }

    // PORT: `clients` is keyword-only in Python. The nested function append_episode_provenance is a local function of
    // the body. The third item of the result is null where Python returns None.
    // TODO(port): _dedupe_extracted_edge
    internal static Task<(EntityEdge, List<EntityEdge>, List<EntityEdge>?)> DedupeExtractedEdgeAsync(
        LLMClient llmClient,
        EntityEdge extractedEdge,
        List<EntityEdge> relatedEdges,
        List<EntityEdge> existingEdges,
        EpisodicNode episode,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypeCandidates = null,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _dedupe_extracted_edge");
    }

    // PORT: `duplicate_nodes_map` is keyed by a pair of UUIDs and iterated by `.values()` (R14).
    // TODO(port): filter_existing_duplicate_of_edges
    internal static Task<List<(EntityNode, EntityNode)>> FilterExistingDuplicateOfEdgesAsync(
        GraphDriver driver,
        List<(EntityNode, EntityNode)> duplicatesNodeTuples,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): filter_existing_duplicate_of_edges");
    }
}
