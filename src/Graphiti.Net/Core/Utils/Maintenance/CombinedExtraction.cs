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

using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: the module imports the response model `CombinedExtraction` of the prompt, which has the name of the module
// class. So the module class has the suffix `Module` (R2).
internal static partial class CombinedExtractionModule
{
    private static ILogger Logger => PortServices.Logger(typeof(CombinedExtractionModule));

    internal const long TimestampBatchSize = 20;

    internal const long TimestampBatchAttempts = 2;

    // PORT: internal, because judge cases read it (R2).
    internal static List<string> EpisodeUuidsForFact(CombinedFact edgeData, List<EpisodicNode> episodes)
    {
        List<string> episodeUuids =
        [
            .. edgeData.EpisodeIndices.Where(idx => 0 <= idx && idx < episodes.Count).Select(idx => episodes[(int)idx].Uuid),
        ];
        return episodeUuids.Count > 0 ? episodeUuids : [.. episodes.Select(episode => episode.Uuid)];
    }

    /// <summary>
    /// Extract entity nodes and relationship facts in a single LLM call.
    ///
    /// This combined extraction produces better results than separate node+edge
    /// extraction because the model can see both tasks simultaneously, ensuring
    /// every entity has at least one connecting fact and reducing orphaned nodes.
    ///
    /// Parameters
    /// ----------
    /// clients : GraphitiClients
    ///     LLM and embedder clients.
    /// episode : EpisodicNode | list[EpisodicNode]
    ///     A single episode or a list of episodes to extract from.
    /// previous_episodes : list[EpisodicNode]
    ///     Prior episodes for context (not extracted from).
    /// entity_types : dict | None
    ///     Custom entity type definitions.
    /// excluded_entity_types : list[str] | None
    ///     Entity types to exclude from extraction.
    /// edge_type_map : dict | None
    ///     Mapping of (source_type, target_type) tuples to lists of edge type names.
    /// edge_types : dict | None
    ///     Custom edge type definitions (Pydantic models keyed by type name).
    /// strict_edge_types : bool
    ///     Whether to discard edges that do not use one of the provided edge_types.
    /// custom_extraction_instructions : str | None
    ///     Additional extraction instructions.
    /// timeline : list[EpisodicNode] | None
    ///     Ordered span containing the episodes plus interleaved context-only turns.
    ///     When None, the episodes render alone.
    ///
    /// Returns
    /// -------
    /// tuple[list[EntityNode], list[EntityEdge], dict[str, list[int]]]
    ///     A tuple of (nodes, edges, node_episode_index_map) where
    ///     node_episode_index_map maps node UUID to 0-indexed episode positions.
    /// </summary>
    // PORT: `episode: EpisodicNode | list[EpisodicNode]` is EpisodeOrEpisodes (node_operations). The nested coroutine
    // extract_group_timestamps is a local async function, and each call of it is a lambda for
    // Helpers.SemaphoreGatherAsync (R8). `list({'Entity', ...})` follows R14 (decision D18).
    internal static async Task<(List<EntityNode>, List<EntityEdge>, OrderedDictionary<string, List<long>>)>
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
        List<EpisodicNode> episodes = episode switch
        {
            EpisodeOrEpisodes.Many many => many.Episodes,
            EpisodeOrEpisodes.One one => [one.Episode],
            _ => throw new InvalidOperationException("unreachable"),
        };
        var primaryEpisode = episodes[0];

        var start = Py.TimeSeconds();
        var llmClient = clients.LlmClient;

        // Build entity types context
        var entityTypesContext = NodeOperations.FilterEntityTypesContext(
            NodeOperations.BuildEntityTypesContext(entityTypes), excludedEntityTypes);
        if (entityTypesContext.Count == 0)
        {
            Log.NoEntityTypesAvailable(Logger);
            return ([], [], new OrderedDictionary<string, List<long>>());
        }

        // Build edge types context (same format as separate extraction path)
        // PORT: `list[dict]` reaches the prompt context, so each item is an open dict (R6). A signature that reaches
        // the context is a PyTuple (R6, edge_operations).
        List<object?> edgeTypesContext = [];
        if (edgeTypes is { Count: > 0 } && edgeTypeMap is { Count: > 0 })
        {
            var edgeTypeSignaturesMap = new OrderedDictionary<string, List<object?>>();
            foreach (var (signature, typeNames) in edgeTypeMap)
            {
                foreach (var typeName in typeNames)
                {
                    // PORT: CA1854 asks for one call in place of `if type_name not in map: map[type_name] = []`.
                    edgeTypeSignaturesMap.TryAdd(typeName, []);
                    edgeTypeSignaturesMap[typeName].Add(new PyTuple(signature.Item1, signature.Item2));
                }
            }

            edgeTypesContext =
            [
                .. edgeTypes.Select(item => (object?)new OrderedDictionary<string, object?>
                {
                    ["fact_type_name"] = item.Key,
                    ["fact_type_signatures"] = edgeTypeSignaturesMap.TryGetValue(item.Key, out var signatures)
                        ? signatures
                        : new List<object?> { new PyTuple("Entity", "Entity") },
                    ["fact_type_description"] = item.Value.Doc,
                }),
            ];
        }

        if (strictEdgeTypes && edgeTypesContext.Count == 0)
        {
            Log.NoEdgeTypesForStrictExtraction(Logger);
            return ([], [], new OrderedDictionary<string, List<long>>());
        }

        var allowedEdgeTypeNames = strictEdgeTypes
            ? (edgeTypes ?? new OrderedDictionary<string, GraphTypeDescriptor>()).Keys.ToHashSet(StringComparer.Ordinal)
            : null;

        // Build context for the combined prompt
        // PORT: `ep.valid_at.isoformat() if ep.valid_at else None`. valid_at is a required datetime, and a PyDateTime
        // cannot be null, so the `None` branch has no form (the same reason as in node_operations).
        var context = new OrderedDictionary<string, object?>
        {
            ["episode_content"] = timeline is { Count: > 0 }
                ? TextUtils.ConcatenateTimeline(episodes, timeline)
                : TextUtils.ConcatenateEpisodes(episodes),
            ["previous_episodes"] = previousEpisodes
                .Select(ep => (object?)new OrderedDictionary<string, object?>
                {
                    ["content"] = ep.Content,
                    ["timestamp"] = ep.ValidAt.IsoFormat(),
                })
                .ToList(),
            ["custom_extraction_instructions"] = customExtractionInstructions ?? string.Empty,
            ["entity_types"] = entityTypesContext,
            ["edge_types"] = edgeTypesContext,
            ["strict_edge_types"] = strictEdgeTypes,
        };

        // Single LLM call for combined extraction
        var llmResponse = await GraphitiTypes.GeneratePromptResponseAsync(
            llmClient,
            "extract_nodes_and_edges.extract_message",
            Lib.PromptLibrary.ExtractNodesAndEdges.ExtractMessage,
            context,
            clients: clients,
            responseModel: CombinedExtraction.ResponseModel,
            groupId: primaryEpisode.GroupId,
            cancellationToken: cancellationToken);
        var responseObject = CombinedExtraction.ModelValidate(llmResponse);

        var end = Py.TimeSeconds();
        var logger = Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var ms = Py.Format((end - start) * 1000, ".0f");
            Log.CombinedExtraction(logger, responseObject.ExtractedEntities.Count, responseObject.Edges.Count, ms);
        }

        // --- Process nodes ---

        // Filter empty names
        var filteredEntities = responseObject.ExtractedEntities.Where(e => PyText.Strip(e.Name).Length > 0).ToList();

        // Convert CombinedEntity objects to EntityNode objects (no episode attribution yet —
        // that is derived from edges below).
        List<EntityNode> extractedNodes = [];
        var excludedEntityNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entity in filteredEntities)
        {
            var typeId = entity.EntityTypeId;
            object? entityTypeName;
            if (0 <= typeId && typeId < entityTypesContext.Count)
            {
                entityTypeName = Py.Get(entityTypesContext[(int)typeId], "entity_type_name");
            }
            else
            {
                entityTypeName = "Entity";
            }

            // PORT: `x in list` compares by Python equality, and an item of the list is a str (R18).
            if (excludedEntityTypes is { Count: > 0 } && excludedEntityTypes.Any(t => PyValueEquality.Equal(t, entityTypeName)))
            {
                excludedEntityNames.Add(DedupHelpers.NormalizeStringExact(entity.Name));
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    var name = Py.Str(entityTypeName);
                    Log.ExcludingEntityOfType(logger, name);
                }

                continue;
            }

            var typeLabel = Py.Str(entityTypeName);
            List<string> labels = typeLabel == "Entity" ? ["Entity"] : ["Entity", typeLabel];
            var newNode = new EntityNode
            {
                Name = entity.Name,
                GroupId = primaryEpisode.GroupId,
                Labels = labels,
                Summary = string.Empty,
                CreatedAt = DatetimeUtils.UtcNow(),
            }.Validate();
            extractedNodes.Add(newNode);
        }

        // Collapse exact-duplicate nodes (same normalized name).
        // Temporarily use an empty map — real attribution comes from edges below.
        var nodeEpisodeIndexMap = new OrderedDictionary<string, List<long>>();
        extractedNodes = NodeOperations.CollapseExactDuplicateExtractedNodes(
            extractedNodes, nodeEpisodeIndexMap);

        // --- Process edges ---

        // Build normalized name-to-node map so case/whitespace differences don't drop edges
        var nameToNode = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        foreach (var node in extractedNodes)
        {
            nameToNode[DedupHelpers.NormalizeStringExact(node.Name)] = node;
        }

        List<EntityEdge> extractedEdges = [];
        var materializedEndpointUuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edgeData in responseObject.Edges)
        {
            if (PyText.Strip(edgeData.Fact).Length == 0)
            {
                Log.SkippingEdgeWithEmptyFact(logger);
                continue;
            }

            // Map episode_indices (0-indexed) to episode UUIDs
            var edgeEpisodeUuids = EpisodeUuidsForFact(edgeData, episodes);

            // Use the first attributed episode's timestamp as the reference time
            var edgeReferenceTime =
                edgeData.EpisodeIndices.Count > 0 && 0 <= edgeData.EpisodeIndices[0] && edgeData.EpisodeIndices[0] < episodes.Count
                    ? episodes[(int)edgeData.EpisodeIndices[0]].ValidAt
                    : primaryEpisode.ValidAt;
            var sourceName = PyText.Strip(edgeData.SourceEntityName);
            var targetName = PyText.Strip(edgeData.TargetEntityName);
            if (sourceName.Length == 0 || targetName.Length == 0)
            {
                Log.SkippingEdgeWithEmptyName(logger);
                continue;
            }

            if (allowedEdgeTypeNames is not null
                && !allowedEdgeTypeNames.Contains(edgeData.RelationType))
            {
                Log.SkippingEdgeNotInStrictOntology(logger, edgeData.RelationType);
                continue;
            }

            var sourceKey = DedupHelpers.NormalizeStringExact(sourceName);
            var targetKey = DedupHelpers.NormalizeStringExact(targetName);
            if (excludedEntityNames.Contains(sourceKey) || excludedEntityNames.Contains(targetKey))
            {
                Log.SkippingEdgeWithExcludedEndpoint(logger);
                continue;
            }

            var sourceNode = nameToNode.GetValueOrDefault(sourceKey);
            if (sourceNode is null)
            {
                var before = extractedNodes.Count;
                sourceNode = DanglingEndpoints.MaterializeDanglingEndpointNode(
                    sourceName,
                    groupId: primaryEpisode.GroupId,
                    nameToNode: nameToNode,
                    nodes: extractedNodes);
                if (extractedNodes.Count > before)
                {
                    materializedEndpointUuids.Add(sourceNode.Uuid);
                }
            }

            var targetNode = nameToNode.GetValueOrDefault(targetKey);
            if (targetNode is null)
            {
                var before = extractedNodes.Count;
                targetNode = DanglingEndpoints.MaterializeDanglingEndpointNode(
                    targetName,
                    groupId: primaryEpisode.GroupId,
                    nameToNode: nameToNode,
                    nodes: extractedNodes);
                if (extractedNodes.Count > before)
                {
                    materializedEndpointUuids.Add(targetNode.Uuid);
                }
            }

            extractedEdges.Add(
                new EntityEdge
                {
                    SourceNodeUuid = sourceNode.Uuid,
                    TargetNodeUuid = targetNode.Uuid,
                    Name = edgeData.RelationType,
                    GroupId = primaryEpisode.GroupId,
                    Fact = edgeData.Fact,
                    Episodes = edgeEpisodeUuids,
                    CreatedAt = DatetimeUtils.UtcNow(),
                    ReferenceTime = edgeReferenceTime,
                }.Validate());
        }

        if (extractedEdges.Count > 0)
        {
            async Task<BatchEdgeTimestamps?> ExtractGroupTimestampsAsync(List<EntityEdge> group)
            {
                // PORT: a datetime is always true, so `if edge.reference_time` is a null test.
                List<object?> factsWithRef =
                [
                    .. group.Select(edge => (object?)new OrderedDictionary<string, object?>
                    {
                        ["fact"] = edge.Fact,
                        ["reference_time"] = edge.ReferenceTime is { } referenceTime ? referenceTime.IsoFormat() : "unknown",
                    }),
                ];
                for (long attempt = 1; attempt < TimestampBatchAttempts + 1; attempt++)
                {
                    try
                    {
                        // Build the prompt again for each attempt because the LLM client edits messages.
                        var tsResponse = await GraphitiTypes.GeneratePromptResponseAsync(
                            llmClient,
                            "extract_edges.extract_timestamps_batch",
                            Lib.PromptLibrary.ExtractEdges.ExtractTimestampsBatch,
                            new OrderedDictionary<string, object?> { ["facts"] = factsWithRef },
                            clients: clients,
                            responseModel: BatchEdgeTimestamps.ResponseModel,
                            modelSize: ModelSize.Small,
                            cancellationToken: cancellationToken);
                        return BatchEdgeTimestamps.ModelValidate(tsResponse);
                    }
                    catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
                    {
                        Log.FailedToExtractBatchTimestamps(Logger, e, group.Count, attempt, TimestampBatchAttempts);
                    }
                }

                return null;
            }

            List<List<EntityEdge>> factGroups = [];
            for (var i = 0; i < extractedEdges.Count; i += (int)TimestampBatchSize)
            {
                factGroups.Add(extractedEdges.GetRange(i, Math.Min((int)TimestampBatchSize, extractedEdges.Count - i)));
            }

            var groupResults = await Helpers.SemaphoreGatherAsync(
                [.. factGroups.Select(group => (Func<Task<BatchEdgeTimestamps?>>)(() => ExtractGroupTimestampsAsync(group)))],
                clients.MaxCoroutines,
                cancellationToken);
            foreach (var (group, batchResult) in Py.ZipStrict(factGroups, groupResults))
            {
                if (batchResult is null)
                {
                    continue;
                }

                if (batchResult.Timestamps.Count != group.Count)
                {
                    Log.BatchTimestampCountMismatch(Logger, batchResult.Timestamps.Count, group.Count);
                }

                var batchRows = batchResult.Timestamps.Take(group.Count).ToList();
                foreach (var (edge, timestamps) in Py.ZipLongest(group, batchRows))
                {
                    if (timestamps is not null)
                    {
                        // PORT: batch_rows is a slice of at most len(group) items, so zip_longest gives an edge for
                        // each row and no input gives a row with no edge. Python reads edge.reference_time with no
                        // test, so the form of R4 is Py.NotNone.
                        var rowEdge = Py.NotNone(edge, "reference_time");
                        TemporalEdgeUtils.ApplyExtractedTimestamps(
                            rowEdge,
                            timestamps.ValidAt,
                            timestamps.InvalidAt,
                            rowEdge.ReferenceTime);
                    }
                }
            }
        }

        // --- Derive node episode attribution from edges and drop orphans ---
        // Each node inherits the episode indices of every edge it participates in.
        // Nodes with no connecting edges are dropped — they have no retrievable facts.
        var episodeUuidToIdx = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var (i, ep) in episodes.Index())
        {
            episodeUuidToIdx[ep.Uuid] = i;
        }

        var connectedNodeUuids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var edge in extractedEdges)
        {
            connectedNodeUuids.Add(edge.SourceNodeUuid);
            connectedNodeUuids.Add(edge.TargetNodeUuid);
        }

        long orphanCount = extractedNodes.Count(n => !connectedNodeUuids.Contains(n.Uuid));
        if (orphanCount != 0)
        {
            Log.DroppingOrphanNodes(logger, orphanCount);
        }

        extractedNodes = [.. extractedNodes.Where(n => connectedNodeUuids.Contains(n.Uuid))];
        long survivingMaterialized = materializedEndpointUuids.Count(connectedNodeUuids.Contains);
        if (survivingMaterialized != 0)
        {
            Log.MaterializedDanglingEndpoints(logger, survivingMaterialized, primaryEpisode.GroupId);
        }

        foreach (var edge in extractedEdges)
        {
            foreach (var nodeUuid in new[] { edge.SourceNodeUuid, edge.TargetNodeUuid })
            {
                List<long> edgeEpisodePositions =
                [
                    .. edge.Episodes.Where(episodeUuidToIdx.ContainsKey).Select(epUuid => episodeUuidToIdx[epUuid]),
                ];
                var existing = Py.Get(nodeEpisodeIndexMap, nodeUuid, []);
                var merged = existing.Concat(edgeEpisodePositions).ToHashSet().Order().ToList();
                nodeEpisodeIndexMap[nodeUuid] = merged;
            }
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            Log.CombinedExtractionFinal(logger, extractedNodes.Count, extractedEdges.Count, responseObject.Edges.Count);
        }

        return (extractedNodes, extractedEdges, nodeEpisodeIndexMap);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "No entity types available for combined extraction after exclusions")]
        internal static partial void NoEntityTypesAvailable(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "No edge types available for strict combined extraction")]
        internal static partial void NoEdgeTypesForStrictExtraction(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Combined extraction: {Entities} entities, {Edges} edges in {Ms} ms")]
        internal static partial void CombinedExtraction(ILogger logger, long entities, long edges, string ms);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Excluding entity of type \"{EntityTypeName}\"")]
        internal static partial void ExcludingEntityOfType(ILogger logger, string entityTypeName);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping edge with empty fact")]
        internal static partial void SkippingEdgeWithEmptyFact(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping edge with empty source or target entity name")]
        internal static partial void SkippingEdgeWithEmptyName(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping edge with relation type \"{RelationType}\" not in strict edge ontology")]
        internal static partial void SkippingEdgeNotInStrictOntology(ILogger logger, string relationType);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping edge with excluded entity endpoint")]
        internal static partial void SkippingEdgeWithExcludedEndpoint(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract batch timestamps for {Count} facts (attempt {Attempt} of {Attempts})")]
        internal static partial void FailedToExtractBatchTimestamps(ILogger logger, Exception exception, long count, long attempt, long attempts);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Batch timestamp count mismatch: got {Got} timestamps for {Count} facts")]
        internal static partial void BatchTimestampCountMismatch(ILogger logger, long got, long count);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Dropping {Count} orphan node(s) with no connecting edges")]
        internal static partial void DroppingOrphanNodes(ILogger logger, long count);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Materialized {Count} dangling edge endpoint node(s) for group_id={GroupId}")]
        internal static partial void MaterializedDanglingEndpoints(ILogger logger, long count, string groupId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Combined extraction final: {Nodes} nodes, {Edges} edges (from {Raw} raw)")]
        internal static partial void CombinedExtractionFinal(ILogger logger, long nodes, long edges, long raw);
    }
}
