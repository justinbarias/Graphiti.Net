// Ported from graphiti_core/utils/maintenance/node_operations.py
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

using Graphiti.Core.Ingestion;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Microsoft.Extensions.Logging;

// TODO(port): no rule for the place and the name of a union type that Python does not name.
// `episode: EpisodicNode | list[EpisodicNode]` has two shapes, and the body branches on `isinstance(episode, list)`
// (`episode.content` for one episode, `concatenate_episodes(episode)` for a list). Part 3 gives the form: an abstract
// record with one derived record for each case. node_operations, edge_operations, combined_extraction, and graphiti.py
// pass the value through, so the type is in the first module that has the annotation. `| None` is `EpisodeOrEpisodes?`.
internal abstract record EpisodeOrEpisodes
{
    private EpisodeOrEpisodes()
    {
    }

    internal sealed record One : EpisodeOrEpisodes
    {
        internal required EpisodicNode Episode { get; init; }
    }

    internal sealed record Many : EpisodeOrEpisodes
    {
        internal required List<EpisodicNode> Episodes { get; init; }
    }
}

// PORT: each private name of this module is internal (R2): combined_extraction imports three of them, and judge cases
// and the recording of the unit read the others. `entity_types: dict[str, type[BaseModel]]` is an
// OrderedDictionary of custom type descriptors (R15). `dict[str, list[int]]` is
// OrderedDictionary<string, List<long>> (R6, R14).
internal static class NodeOperations
{
    private static ILogger Logger => PortServices.Logger(typeof(NodeOperations));

    internal const long MaxNodes = 30;

    internal const double NodeDedupCosineMinScore = 0.6;

    // PORT: the type alias NodeSummaryFilter has no C# type (R2). Each use is Func<EntityNode, Task<bool>>.

    // TODO(port): extract_nodes
    internal static Task<(List<EntityNode>, OrderedDictionary<string, List<long>>)> ExtractNodesAsync(
        GraphitiClients clients,
        EpisodeOrEpisodes episode,
        List<EpisodicNode> previousEpisodes,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        List<string>? excludedEntityTypes = null,
        string? customExtractionInstructions = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): extract_nodes");
    }

    // PORT: `list[dict]` is a list of open dicts that reach the prompt context (R6).
    // TODO(port): _build_entity_types_context
    internal static List<OrderedDictionary<string, object?>> BuildEntityTypesContext(
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)
    {
        throw new NotSupportedException("TODO(port): _build_entity_types_context");
    }

    // TODO(port): _filter_entity_types_context
    internal static List<OrderedDictionary<string, object?>> FilterEntityTypesContext(
        List<OrderedDictionary<string, object?>> entityTypesContext,
        List<string>? excludedEntityTypes)
    {
        throw new NotSupportedException("TODO(port): _filter_entity_types_context");
    }

    // TODO(port): _get_entity_type_description
    internal static string GetEntityTypeDescription(
        List<string> labels,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)
    {
        throw new NotSupportedException("TODO(port): _get_entity_type_description");
    }

    // TODO(port): _truncate_type_description
    internal static string TruncateTypeDescription(string docstring)
    {
        throw new NotSupportedException("TODO(port): _truncate_type_description");
    }

    // PORT: the index counts code points (R12).
    // TODO(port): _find_sentence_end
    internal static long FindSentenceEnd(string text)
    {
        throw new NotSupportedException("TODO(port): _find_sentence_end");
    }

    // PORT: `clients` is keyword-only in Python. `context: dict` is open data (R6).
    // TODO(port): _extract_nodes_single
    internal static Task<List<ExtractedEntity>> ExtractNodesSingleAsync(
        LLMClient llmClient,
        EpisodicNode episode,
        OrderedDictionary<string, object?> context,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _extract_nodes_single");
    }

    // TODO(port): _call_extraction_llm
    internal static Task<OrderedDictionary<string, object?>> CallExtractionLlmAsync(
        LLMClient llmClient,
        EpisodicNode episode,
        OrderedDictionary<string, object?> context,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _call_extraction_llm");
    }

    // PORT: `list({'Entity', str(entity_type_name)})` is `Entity` first, then the type name if it differs (R14,
    // decision D18).
    // TODO(port): _create_entity_nodes
    internal static (List<EntityNode>, OrderedDictionary<string, List<long>>) CreateEntityNodes(
        List<ExtractedEntity> extractedEntities,
        List<OrderedDictionary<string, object?>> entityTypesContext,
        List<string>? excludedEntityTypes,
        List<EpisodicNode> episodes)
    {
        throw new NotSupportedException("TODO(port): _create_entity_nodes");
    }

    // TODO(port): _collapse_exact_duplicate_extracted_nodes
    internal static List<EntityNode> CollapseExactDuplicateExtractedNodes(
        List<EntityNode> extractedNodes,
        OrderedDictionary<string, List<long>>? nodeEpisodeIndexMap = null)
    {
        throw new NotSupportedException("TODO(port): _collapse_exact_duplicate_extracted_nodes");
    }

    // TODO(port): _merge_candidate_nodes
    internal static List<EntityNode> MergeCandidateNodes(
        List<EntityNode> candidateNodes,
        List<EntityNode>? existingNodesOverride = null)
    {
        throw new NotSupportedException("TODO(port): _merge_candidate_nodes");
    }

    // TODO(port): _collect_candidate_nodes
    internal static Task<List<List<EntityNode>>> CollectCandidateNodesAsync(
        GraphitiClients clients,
        List<EntityNode> extractedNodes,
        List<EntityNode>? existingNodesOverride = null,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        bool reuseNameEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _collect_candidate_nodes");
    }

    // PORT: the nested coroutine _lookup_node is a lambda for Helpers.SemaphoreGatherAsync (R8).
    // TODO(port): _collect_identity_property_candidate_nodes
    internal static Task<List<List<EntityNode>>> CollectIdentityPropertyCandidateNodesAsync(
        GraphitiClients clients,
        List<EntityNode> extractedNodes,
        OrderedDictionary<string, List<string>>? identityProperties,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _collect_identity_property_candidate_nodes");
    }

    // TODO(port): _collect_exact_name_candidate_nodes
    internal static Task<List<List<EntityNode>>> CollectExactNameCandidateNodesAsync(
        GraphitiClients clients,
        List<EntityNode> extractedNodes,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _collect_exact_name_candidate_nodes");
    }

    // TODO(port): _semantic_candidate_search
    internal static Task<List<List<EntityNode>>> SemanticCandidateSearchAsync(
        GraphitiClients clients,
        List<EntityNode> extractedNodes,
        bool reuseNameEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _semantic_candidate_search");
    }

    // TODO(port): _embed_queries
    internal static Task<List<List<double>>> EmbedQueriesAsync(
        GraphitiClients clients,
        List<string> queries,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _embed_queries");
    }

    // TODO(port): _name_vectors_with_reuse
    internal static Task<List<List<double>>> NameVectorsWithReuseAsync(
        GraphitiClients clients,
        List<EntityNode> nodes,
        List<string> queries,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _name_vectors_with_reuse");
    }

    // TODO(port): _commit_resolution
    internal static void CommitResolution(
        DedupResolutionState state,
        EntityNode? resolvedNode,
        OrderedDictionary<string, string> uuidMap,
        List<(EntityNode, EntityNode)> duplicatePairs,
        long index)
    {
        throw new NotSupportedException("TODO(port): _commit_resolution");
    }

    // PORT: `clients` is keyword-only in Python.
    // TODO(port): _resolve_with_llm
    internal static Task ResolveWithLlmAsync(
        LLMClient llmClient,
        List<EntityNode> extractedNodes,
        DedupCandidateIndexes indexes,
        DedupResolutionState state,
        EpisodicNode? episode,
        List<EpisodicNode>? previousEpisodes,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _resolve_with_llm");
    }

    // TODO(port): resolve_extracted_nodes
    internal static Task<(List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>)>
        ResolveExtractedNodesAsync(
            GraphitiClients clients,
            List<EntityNode> extractedNodes,
            EpisodicNode? episode = null,
            List<EpisodicNode>? previousEpisodes = null,
            OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
            List<EntityNode>? existingNodesOverride = null,
            OrderedDictionary<string, List<string>>? identityProperties = null,
            ResolutionTrace? trace = null,
            CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): resolve_extracted_nodes");
    }

    // PORT: `trace` is keyword-only in Python.
    // TODO(port): resolve_nodes_with_candidates
    internal static Task<(List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>)>
        ResolveNodesWithCandidatesAsync(
            GraphitiClients clients,
            List<EntityNode> extractedNodes,
            List<List<EntityNode>> candidateNodesByExtracted,
            EpisodicNode? episode = null,
            List<EpisodicNode>? previousEpisodes = null,
            OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
            OrderedDictionary<string, List<string>>? identityProperties = null,
            ResolutionTrace? trace = null,
            CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): resolve_nodes_with_candidates");
    }

    // TODO(port): _build_edges_by_node
    internal static OrderedDictionary<string, List<EntityEdge>> BuildEdgesByNode(List<EntityEdge>? edges)
    {
        throw new NotSupportedException("TODO(port): _build_edges_by_node");
    }

    // TODO(port): _entity_type_for_node
    internal static GraphTypeDescriptor? EntityTypeForNode(
        EntityNode node,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)
    {
        throw new NotSupportedException("TODO(port): _entity_type_for_node");
    }

    // TODO(port): extract_attributes_only_from_nodes
    internal static Task<List<EntityNode>> ExtractAttributesOnlyFromNodesAsync(
        GraphitiClients clients,
        List<EntityNode> nodes,
        EpisodeOrEpisodes? episode = null,
        List<EpisodicNode>? previousEpisodes = null,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        bool validateShape = true,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): extract_attributes_only_from_nodes");
    }

    // PORT: `should_summarize_node: NodeSummaryFilter | None` is Func<EntityNode, Task<bool>>? (R2, type alias).
    // TODO(port): extract_attributes_from_nodes
    internal static Task<List<EntityNode>> ExtractAttributesFromNodesAsync(
        GraphitiClients clients,
        List<EntityNode> nodes,
        EpisodeOrEpisodes? episode = null,
        List<EpisodicNode>? previousEpisodes = null,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        Func<EntityNode, Task<bool>>? shouldSummarizeNode = null,
        List<EntityEdge>? edges = null,
        bool skipFactAppending = false,
        bool includeTypeDescriptions = false,
        bool skipAttributeExtraction = false,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): extract_attributes_from_nodes");
    }

    // PORT: the arguments after `entity_type` are keyword-only in Python. `entity_type` is the response model of the
    // prompt, and a custom type descriptor is an IResponseModel (R7, R15).
    // TODO(port): _extract_entity_attributes
    internal static Task<OrderedDictionary<string, object?>> ExtractEntityAttributesAsync(
        LLMClient llmClient,
        EntityNode node,
        EpisodeOrEpisodes? episode,
        List<EpisodicNode>? previousEpisodes,
        GraphTypeDescriptor? entityType,
        GraphitiClients? clients = null,
        bool validateShape = true,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _extract_entity_attributes");
    }

    // PORT: the arguments after `edges_by_node` are keyword-only in Python.
    // TODO(port): _extract_entity_summaries_batch
    internal static Task ExtractEntitySummariesBatchAsync(
        LLMClient llmClient,
        List<EntityNode> nodes,
        EpisodeOrEpisodes? episode,
        List<EpisodicNode>? previousEpisodes,
        Func<EntityNode, Task<bool>>? shouldSummarizeNode,
        OrderedDictionary<string, List<EntityEdge>> edgesByNode,
        bool skipFactAppending = false,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        GraphitiClients? clients = null,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _extract_entity_summaries_batch");
    }

    // PORT: the arguments after `previous_episodes` are keyword-only in Python.
    // TODO(port): _process_summary_flight
    internal static Task ProcessSummaryFlightAsync(
        LLMClient llmClient,
        List<EntityNode> nodes,
        EpisodeOrEpisodes? episode,
        List<EpisodicNode>? previousEpisodes,
        bool useEpisodePrompt = false,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _process_summary_flight");
    }

    // TODO(port): _build_episode_context
    internal static OrderedDictionary<string, object?> BuildEpisodeContext(
        OrderedDictionary<string, object?> nodeData,
        EpisodeOrEpisodes? episode,
        List<EpisodicNode>? previousEpisodes)
    {
        throw new NotSupportedException("TODO(port): _build_episode_context");
    }
}
