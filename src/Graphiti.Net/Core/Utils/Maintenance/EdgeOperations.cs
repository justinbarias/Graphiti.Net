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
using Graphiti.Core.Prompts;
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;
using ExtractedEdge = Graphiti.Core.Prompts.Edge;

// PORT: each private name of this module is internal, because judge cases and the recording of the unit read them
// (R2). `edge_types: dict[str, type[BaseModel]]` is an OrderedDictionary of custom type descriptors (R15).
// PORT: a dict key `tuple[str, str]` of typed data is a value tuple (Part 3). `edge_type_map: dict[tuple[str, str], list[str]]`
// is OrderedDictionary<(string, string), List<string>>: code iterates it with `.items()` (R14), and a value tuple has
// value equality. A signature that reaches the prompt context is a PyTuple there (R6).
internal static partial class EdgeOperations
{
    private static ILogger Logger => PortServices.Logger(typeof(EdgeOperations));

    internal const string DefaultEdgeName = "RELATES_TO";

    // PORT: a union of a value and a list that the function makes into a list on its first line (R2).
    // `episode_uuid: str | list[str]` is two overloads. Judge cases pass both shapes. The overload for one uuid makes
    // the list and calls the overload for a list, which holds the body.

    /// <summary>
    /// Build episodic (MENTIONED_IN) edges between entity nodes and episodes.
    ///
    /// Parameters
    /// ----------
    /// entity_nodes : list[EntityNode]
    ///     Nodes to connect to episodes.
    /// episode_uuid : str | list[str]
    ///     A single episode UUID or a list of episode UUIDs.
    /// created_at : datetime
    ///     Timestamp for the edges.
    /// node_episode_index_map : dict[str, list[int]] | None
    ///     Optional mapping from node UUID to 0-indexed episode positions.
    ///     When provided with a list of episode_uuids, each node is connected
    ///     only to its attributed episodes. When None, every node is connected
    ///     to all episodes.
    /// </summary>
    internal static List<EpisodicEdge> BuildEpisodicEdges(
        List<EntityNode> entityNodes,
        string episodeUuid,
        PyDateTime createdAt,
        OrderedDictionary<string, List<long>>? nodeEpisodeIndexMap = null)
    {
        return BuildEpisodicEdges(entityNodes, [episodeUuid], createdAt, nodeEpisodeIndexMap);
    }

    /// <summary>
    /// Build episodic (MENTIONED_IN) edges between entity nodes and episodes.
    ///
    /// Parameters
    /// ----------
    /// entity_nodes : list[EntityNode]
    ///     Nodes to connect to episodes.
    /// episode_uuid : str | list[str]
    ///     A single episode UUID or a list of episode UUIDs.
    /// created_at : datetime
    ///     Timestamp for the edges.
    /// node_episode_index_map : dict[str, list[int]] | None
    ///     Optional mapping from node UUID to 0-indexed episode positions.
    ///     When provided with a list of episode_uuids, each node is connected
    ///     only to its attributed episodes. When None, every node is connected
    ///     to all episodes.
    /// </summary>
    internal static List<EpisodicEdge> BuildEpisodicEdges(
        List<EntityNode> entityNodes,
        List<string> episodeUuid,
        PyDateTime createdAt,
        OrderedDictionary<string, List<long>>? nodeEpisodeIndexMap = null)
    {
        var episodeUuids = episodeUuid;

        List<EpisodicEdge> episodicEdges = [];
        foreach (var node in entityNodes)
        {
            List<long> indices;
            if (nodeEpisodeIndexMap is { Count: > 0 } && nodeEpisodeIndexMap.TryGetValue(node.Uuid, out var mapped))
            {
                indices = mapped;
            }
            else
            {
                indices = [.. Enumerable.Range(0, episodeUuids.Count).Select(i => (long)i)];
            }

            foreach (var idx in indices)
            {
                if (0 <= idx && idx < episodeUuids.Count)
                {
                    episodicEdges.Add(
                        new EpisodicEdge
                        {
                            SourceNodeUuid = episodeUuids[(int)idx],
                            TargetNodeUuid = node.Uuid,
                            CreatedAt = createdAt,
                            GroupId = node.GroupId,
                        }.Validate());
                }
            }
        }

        Log.BuiltEpisodicEdges(Logger, episodicEdges.Count);

        return episodicEdges;
    }

    internal static List<CommunityEdge> BuildCommunityEdges(
        List<EntityNode> entityNodes,
        CommunityNode communityNode,
        PyDateTime createdAt)
    {
        List<CommunityEdge> edges =
        [
            .. entityNodes.Select(node => new CommunityEdge
            {
                SourceNodeUuid = communityNode.Uuid,
                TargetNodeUuid = node.Uuid,
                CreatedAt = createdAt,
                GroupId = communityNode.GroupId,
            }.Validate()),
        ];

        return edges;
    }

    // PORT: `episode: EpisodicNode | list[EpisodicNode]` is EpisodeOrEpisodes (node_operations).

    /// <summary>
    /// Extract edges from one or more episodes.
    ///
    /// Parameters
    /// ----------
    /// episode : EpisodicNode | list[EpisodicNode]
    ///     A single episode or a list of episodes to extract edges from.
    ///     When a list is provided, their contents are concatenated for extraction
    ///     and edges are linked to all episode UUIDs.
    ///
    /// Returns
    /// -------
    /// tuple[list[EntityEdge], list[EntityNode]]
    ///     Extracted edges and any endpoint nodes materialized onto ``nodes`` for
    ///     dangling source/target names that survived edge filters.
    /// </summary>
    internal static async Task<(List<EntityEdge>, List<EntityNode>)> ExtractEdgesAsync(
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
        List<EpisodicNode> episodes = episode switch
        {
            EpisodeOrEpisodes.Many many => many.Episodes,
            EpisodeOrEpisodes.One one => [one.Episode],
            _ => throw new InvalidOperationException("unreachable"),
        };
        var primaryEpisode = episodes[0];

        var start = Py.TimeSeconds();
        var initialNodeUuids = nodes.Select(node => node.Uuid).ToHashSet(StringComparer.Ordinal);

        const long extractEdgesMaxTokens = 16384;
        var llmClient = clients.LlmClient;

        // Build mapping from edge type name to list of valid signatures
        var edgeTypeSignaturesMap = new OrderedDictionary<string, List<object?>>();
        foreach (var (signature, edgeTypeNames) in edgeTypeMap)
        {
            foreach (var edgeType in edgeTypeNames)
            {
                // PORT: CA1854 asks for one lookup in place of `if edge_type not in map` and the read that follows.
                if (!edgeTypeSignaturesMap.TryGetValue(edgeType, out var edgeTypeSignatures))
                {
                    edgeTypeSignatures = [];
                    edgeTypeSignaturesMap[edgeType] = edgeTypeSignatures;
                }

                edgeTypeSignatures.Add(new PyTuple(signature.Item1, signature.Item2));
            }
        }

        List<object?> edgeTypesContext = [];
        if (edgeTypes is not null)
        {
            foreach (var (typeName, typeModel) in edgeTypes)
            {
                edgeTypesContext.Add(new OrderedDictionary<string, object?>
                {
                    ["fact_type_name"] = typeName,
                    ["fact_type_signatures"] = edgeTypeSignaturesMap.TryGetValue(typeName, out var signatures)
                        ? signatures
                        : new List<object?> { new PyTuple("Entity", "Entity") },
                    ["fact_type_description"] = typeModel.Doc,
                });
            }
        }

        if (strictEdgeTypes && edgeTypesContext.Count == 0)
        {
            Log.NoEdgeTypesForStrictExtraction(Logger);
            return ([], []);
        }

        var allowedEdgeTypeNames = strictEdgeTypes
            ? (edgeTypes ?? new OrderedDictionary<string, GraphTypeDescriptor>()).Keys.ToHashSet(StringComparer.Ordinal)
            : null;

        // Build normalized name-to-node mapping for validation / materialization
        var nameToNode = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            nameToNode[DedupHelpers.NormalizeStringExact(node.Name)] = node;
        }

        // Build episode attribution instructions for multi-episode extraction
        var episodeAttribution = string.Empty;
        if (episodes.Count > 1)
        {
            episodeAttribution =
                "\n8. **Episode Attribution**: The CURRENT_MESSAGE contains multiple episodes labeled "
                + "[Episode 0], [Episode 1], etc. Each episode header includes a timestamp indicating "
                + "when that episode occurred. Use the per-episode timestamp to resolve relative time "
                + "mentions within each episode rather than relying solely on REFERENCE_TIME. "
                + "For each extracted fact, set `episode_indices` "
                + "to the 0-based list of episode numbers that the fact was derived from. "
                + "A fact sourced from Episodes 0 and 1 should have `episode_indices: [0, 1]`.";
        }

        // Prepare context for LLM
        // Use the latest episode's timestamp as the primary reference time
        // PORT: max(..., key=...) returns the first of equal items, as MaxBy does (R14). A naive and an aware valid_at
        // throw InvalidCastException in the comparison, as Python raises TypeError.
        var latestEpisode = episodes.MaxBy(ep => ep.ValidAt)!;
        var context = new OrderedDictionary<string, object?>
        {
            ["episode_content"] = TextUtils.ConcatenateEpisodes(episodes),
            ["nodes"] = nodes.Select(node => (object?)new OrderedDictionary<string, object?> { ["name"] = node.Name, ["entity_types"] = node.Labels }).ToList(),
            ["previous_episodes"] = previousEpisodes.Select(ep => (object?)new OrderedDictionary<string, object?>
            {
                ["content"] = ep.Content,
                ["timestamp"] = ep.ValidAt.IsoFormat(),
            }).ToList(),
            ["reference_time"] = latestEpisode.ValidAt,
            ["edge_types"] = edgeTypesContext,
            ["strict_edge_types"] = strictEdgeTypes,
            ["custom_extraction_instructions"] = (customExtractionInstructions ?? string.Empty) + episodeAttribution,
        };

        var llmResponse = await GraphitiTypes.GeneratePromptResponseAsync(
            llmClient,
            "extract_edges.edge",
            Lib.PromptLibrary.ExtractEdges.Edge,
            context,
            clients: clients,
            responseModel: ExtractedEdges.ResponseModel,
            maxTokens: extractEdgesMaxTokens,
            groupId: !string.IsNullOrEmpty(groupId) ? groupId : primaryEpisode.GroupId,
            cancellationToken: cancellationToken);
        var allEdgesData = ExtractedEdges.ModelValidate(llmResponse).Edges;

        var edgeGroupId = !string.IsNullOrEmpty(groupId) ? groupId : primaryEpisode.GroupId;

        // Validate entity names; materialize dangling endpoints only for edges we keep
        List<ExtractedEdge> edgesData = [];
        foreach (var edgeData in allEdgesData)
        {
            var sourceName = PyText.Strip(edgeData.SourceEntityName);
            var targetName = PyText.Strip(edgeData.TargetEntityName);
            if (sourceName.Length == 0 || targetName.Length == 0)
            {
                Log.SkippingEdgeWithEmptyName(Logger);
                continue;
            }

            if (PyText.Strip(edgeData.Fact).Length == 0)
            {
                continue;
            }

            if (allowedEdgeTypeNames is not null
                && !allowedEdgeTypeNames.Contains(edgeData.RelationType))
            {
                Log.SkippingEdgeNotInStrictOntology(Logger, edgeData.RelationType);
                continue;
            }

            var sourceNode = nameToNode.GetValueOrDefault(DedupHelpers.NormalizeStringExact(sourceName))
                ?? DanglingEndpoints.MaterializeDanglingEndpointNode(
                    sourceName,
                    groupId: edgeGroupId,
                    nameToNode: nameToNode,
                    nodes: nodes);

            var targetNode = nameToNode.GetValueOrDefault(DedupHelpers.NormalizeStringExact(targetName))
                ?? DanglingEndpoints.MaterializeDanglingEndpointNode(
                    targetName,
                    groupId: edgeGroupId,
                    nameToNode: nameToNode,
                    nodes: nodes);

            // Keep canonical names on the edge payload for the conversion loop below
            edgeData.SourceEntityName = sourceNode.Name;
            edgeData.TargetEntityName = targetNode.Name;
            edgesData.Add(edgeData);
        }

        var end = Py.TimeSeconds();
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            var elapsed = Py.Format((end - start) * 1000, ".0f");
            Log.ExtractedNewEdges(Logger, edgesData.Count, elapsed);
        }

        if (edgesData.Count == 0)
        {
            var noEdgeMaterializedNodes = DanglingEndpoints.PruneUnreferencedMaterializedNodes(
                nodes,
                initialUuids: initialNodeUuids,
                referencedUuids: new HashSet<string>(StringComparer.Ordinal));
            return ([], noEdgeMaterializedNodes);
        }

        // Convert the extracted data into EntityEdge objects
        List<EntityEdge> edges = [];
        foreach (var edgeData in edgesData)
        {
            // Validate Edge Date information
            var validAt = edgeData.ValidAt;
            var invalidAt = edgeData.InvalidAt;
            PyDateTime? validAtDatetime = null;
            PyDateTime? invalidAtDatetime = null;

            // Names already validated / rewritten to canonical node names above
            var sourceNode = nameToNode.GetValueOrDefault(DedupHelpers.NormalizeStringExact(edgeData.SourceEntityName));
            var targetNode = nameToNode.GetValueOrDefault(DedupHelpers.NormalizeStringExact(edgeData.TargetEntityName));

            if (sourceNode is null || targetNode is null)
            {
                Log.CouldNotFindSourceOrTarget(Logger);
                continue;
            }

            var sourceNodeUuid = sourceNode.Uuid;
            var targetNodeUuid = targetNode.Uuid;

            if (!string.IsNullOrEmpty(validAt))
            {
                try
                {
                    validAtDatetime = DatetimeUtils.EnsureUtc(
                        PyDateTime.FromIsoFormat(validAt.Replace("Z", "+00:00", StringComparison.Ordinal)));
                }
                catch (ArgumentException)
                {
                    Log.ErrorParsingValidAt(Logger);
                }
            }

            if (!string.IsNullOrEmpty(invalidAt))
            {
                try
                {
                    invalidAtDatetime = DatetimeUtils.EnsureUtc(
                        PyDateTime.FromIsoFormat(invalidAt.Replace("Z", "+00:00", StringComparison.Ordinal)));
                }
                catch (ArgumentException e)
                {
                    // PORT: the analyzer LOGGEN008 refuses the level word at the start of a message text, so the
                    // Python text `WARNING: ` is the value of the first placeholder.
                    Log.ErrorParsingInvalidAt(Logger, "WARNING: ", e.Message, invalidAt);
                }
            }

            // Map episode_indices (0-indexed) to episode UUIDs.
            // Clamp indices to valid range and fall back to all episodes if empty.
            List<string> edgeEpisodeUuids = [];
            foreach (var idx in edgeData.EpisodeIndices)
            {
                if (0 <= idx && idx < episodes.Count)
                {
                    edgeEpisodeUuids.Add(episodes[(int)idx].Uuid);
                }
            }

            if (edgeEpisodeUuids.Count == 0)
            {
                edgeEpisodeUuids = [.. episodes.Select(ep => ep.Uuid)];
            }

            var edgeReferenceTime =
                edgeData.EpisodeIndices.Count > 0 && 0 <= edgeData.EpisodeIndices[0] && edgeData.EpisodeIndices[0] < episodes.Count
                    ? episodes[(int)edgeData.EpisodeIndices[0]].ValidAt
                    : primaryEpisode.ValidAt;
            (validAtDatetime, invalidAtDatetime) = TemporalEdgeUtils.NormalizeFutureTemporalBounds(
                edgeData.Fact,
                edgeReferenceTime,
                validAtDatetime,
                invalidAtDatetime);

            var edge = new EntityEdge
            {
                SourceNodeUuid = sourceNodeUuid,
                TargetNodeUuid = targetNodeUuid,
                Name = edgeData.RelationType,
                GroupId = !string.IsNullOrEmpty(groupId) ? groupId : primaryEpisode.GroupId,
                Fact = edgeData.Fact,
                Episodes = edgeEpisodeUuids,
                CreatedAt = DatetimeUtils.UtcNow(),
                ValidAt = validAtDatetime,
                InvalidAt = invalidAtDatetime,
                ReferenceTime = edgeReferenceTime,
            }.Validate();
            edges.Add(edge);
            Log.CreatedNewEdge(Logger, edge.Uuid, edge.SourceNodeUuid, edge.TargetNodeUuid);
        }

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            var uuids = Py.Repr(edges.Select(e => (object?)e.Uuid).ToList());
            Log.ExtractedEdges(Logger, uuids);
        }

        var referencedUuids = edges.Select(edge => edge.SourceNodeUuid).ToHashSet(StringComparer.Ordinal);
        referencedUuids.UnionWith(edges.Select(edge => edge.TargetNodeUuid));
        var materializedNodes = DanglingEndpoints.PruneUnreferencedMaterializedNodes(
            nodes,
            initialUuids: initialNodeUuids,
            referencedUuids: referencedUuids);
        if (materializedNodes.Count > 0)
        {
            Log.MaterializedDanglingEndpoints(Logger, materializedNodes.Count, edgeGroupId);
        }

        return (edges, materializedNodes);
    }

    /// <summary>
    /// Filter restricting an edge search to the extracted edge's endpoints.
    ///
    /// src IN {a, b} AND tgt IN {a, b} covers both orientations in one query.
    /// The filter can reduce the candidate set before ranking. It also
    /// admits self-loops on either endpoint when a != b -- callers drop those
    /// with _same_pair_edges.
    /// </summary>
    internal static SearchFilters PairSearchFilter(EntityEdge edge)
    {
        List<string> endpoints = [edge.SourceNodeUuid, edge.TargetNodeUuid];
        return new SearchFilters
        {
            EdgeSourceNodeUuids = endpoints,
            EdgeTargetNodeUuids = endpoints,
        }.Validate();
    }

    /// <summary>
    /// Keep only edges between the extracted edge's endpoints, either
    /// orientation (drops the self-loops a {a,b}x{a,b} endpoint filter admits).
    /// </summary>
    internal static List<EntityEdge> SamePairEdges(EntityEdge extractedEdge, List<EntityEdge> candidates)
    {
        var pair = new HashSet<(string, string)>
        {
            (extractedEdge.SourceNodeUuid, extractedEdge.TargetNodeUuid),
            (extractedEdge.TargetNodeUuid, extractedEdge.SourceNodeUuid),
        };
        return [.. candidates.Where(edge => pair.Contains((edge.SourceNodeUuid, edge.TargetNodeUuid)))];
    }

    // PORT: `referenced_node_uuids` is a PySet<string> in the order of the edges (R14).

    /// <summary>
    /// Resolve extracted edges against existing graph context.
    ///
    /// An edge whose name is a custom edge type must join the entity types that the
    /// type's signature declares in ``edge_type_map``. A mismatched edge is dropped
    /// when ``strict_edge_types`` is set and renamed to ``DEFAULT_EDGE_NAME`` otherwise.
    ///
    /// When `invalidated_by` is given, it is filled with the uuid of every
    /// invalidated edge mapped to the uuids of the resolved edges that invalidated it.
    ///
    /// When `trace` is given, it records the candidates that each extracted edge saw and
    /// one operation for each change that the resolution makes to a stored edge.
    ///
    /// Returns
    /// -------
    /// tuple[list[EntityEdge], list[EntityEdge], list[EntityEdge]]
    ///     A tuple of (resolved_edges, invalidated_edges, new_edges) where:
    ///     - resolved_edges: All edges after resolution (may include existing edges if duplicates found)
    ///     - invalidated_edges: Edges that were invalidated/contradicted by new information
    ///     - new_edges: Only edges that are new to the graph (not duplicates of existing edges)
    /// </summary>
    internal static async Task<(List<EntityEdge>, List<EntityEdge>, List<EntityEdge>)> ResolveExtractedEdgesAsync(
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
        var driver = clients.Driver;
        var llmClient = clients.LlmClient;
        var embedder = clients.Embedder;

        // Build entity hash table
        var uuidEntityMap = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        foreach (var entity in entities)
        {
            uuidEntityMap[entity.Uuid] = entity;
        }

        // Collect all node UUIDs referenced by edges that are not in the entities list
        var referencedNodeUuids = new PySet<string>();
        foreach (var extractedEdge in extractedEdges)
        {
            if (!uuidEntityMap.ContainsKey(extractedEdge.SourceNodeUuid))
            {
                referencedNodeUuids.Add(extractedEdge.SourceNodeUuid);
            }

            if (!uuidEntityMap.ContainsKey(extractedEdge.TargetNodeUuid))
            {
                referencedNodeUuids.Add(extractedEdge.TargetNodeUuid);
            }
        }

        // Fetch missing nodes from the database
        if (referencedNodeUuids.Count > 0)
        {
            // Limit the lookup to the edge group.
            var edgeGroupId = extractedEdges[0].GroupId;
            var missingNodes = await EntityNode.GetByUuidsAsync(
                driver, [.. referencedNodeUuids], groupId: edgeGroupId, cancellationToken: cancellationToken);
            foreach (var node in missingNodes)
            {
                uuidEntityMap[node.Uuid] = node;
            }
        }

        // Determine which edge types are relevant for each edge based on node signatures.
        // `edge_types_lst` stores the subset of custom edge definitions whose
        // node signature matches each extracted edge.
        List<OrderedDictionary<string, GraphTypeDescriptor>> edgeTypesLst = [];
        foreach (var extractedEdge in extractedEdges)
        {
            edgeTypesLst.Add(
                EdgeTypesForEndpoints(extractedEdge, uuidEntityMap, edgeTypes, edgeTypeMap));
        }

        (extractedEdges, edgeTypesLst) = EnforceEdgeTypeSignatures(
            extractedEdges, edgeTypesLst, edgeTypeMap, strictEdgeTypes);

        // Fast path: deduplicate same-direction and same-type reverse-direction matches
        var seen = new Dictionary<(string, string, string), EntityEdge>();
        List<EntityEdge> deduplicatedEdges = [];
        List<OrderedDictionary<string, GraphTypeDescriptor>> deduplicatedEdgeTypes = [];

        foreach (var (edge, matchingEdgeTypes) in Py.ZipStrict(extractedEdges, edgeTypesLst))
        {
            if (FindExactDuplicateEdge(edge, seen) is not null)
            {
                continue;
            }

            deduplicatedEdges.Add(edge);
            deduplicatedEdgeTypes.Add(matchingEdgeTypes);
        }

        extractedEdges = deduplicatedEdges;
        edgeTypesLst = deduplicatedEdgeTypes;

        await Edges.CreateEntityEdgeEmbeddingsAsync(embedder, extractedEdges, cancellationToken);

        // Duplicate candidates have the same endpoints in either direction.
        // Prefiltering the search by endpoints limits the search work. The post-filter
        // removes self-loops and protects against indexes that return unrelated edges.
        // Reuse each computed fact embedding to avoid another embedder request.
        var relatedEdgesResults = await Helpers.SemaphoreGatherAsync(
            [
                .. extractedEdges.Select(extractedEdge => (Func<Task<SearchResults>>)(() => SearchModule.SearchAsync(
                    clients,
                    extractedEdge.Fact,
                    groupIds: [extractedEdge.GroupId],
                    config: SearchConfigRecipes.EdgeHybridSearchRrf,
                    searchFilter: PairSearchFilter(extractedEdge),
                    queryVector: extractedEdge.FactEmbedding,
                    cancellationToken: cancellationToken))),
            ],
            clients.MaxCoroutines,
            cancellationToken);
        List<List<EntityEdge>> relatedEdgesLists =
        [
            .. Py.ZipStrict(extractedEdges, relatedEdgesResults)
                .Select(pair => SamePairEdges(pair.First, pair.Second.Edges).ToList()),
        ];
        if (existingEdgesOverride is { Count: > 0 })
        {
            // PORT: `override_by_pair` is read by key only (R14).
            var overrideByPair = new Dictionary<(string, string), List<EntityEdge>>();
            foreach (var edge in existingEdgesOverride)
            {
                var pair = (edge.SourceNodeUuid, edge.TargetNodeUuid);
                Py.SetDefault(overrideByPair, pair, []).Add(edge);
            }

            foreach (var (extractedEdge, relatedEdges) in Py.ZipStrict(extractedEdges, relatedEdgesLists))
            {
                var pair = (extractedEdge.SourceNodeUuid, extractedEdge.TargetNodeUuid);
                var existingUuids = relatedEdges.Select(edge => edge.Uuid).ToHashSet(StringComparer.Ordinal);
                foreach (var edge in overrideByPair.GetValueOrDefault(pair, []))
                {
                    if (edge.GroupId == extractedEdge.GroupId && !existingUuids.Contains(edge.Uuid))
                    {
                        relatedEdges.Add(edge);
                        existingUuids.Add(edge.Uuid);
                    }
                }
            }
        }

        var edgeInvalidationCandidateResults = await Helpers.SemaphoreGatherAsync(
            [
                .. extractedEdges.Select(extractedEdge => (Func<Task<SearchResults>>)(() => SearchModule.SearchAsync(
                    clients,
                    extractedEdge.Fact,
                    groupIds: [extractedEdge.GroupId],
                    config: SearchConfigRecipes.EdgeHybridSearchRrf,
                    searchFilter: new SearchFilters().Validate(),
                    queryVector: extractedEdge.FactEmbedding,
                    cancellationToken: cancellationToken))),
            ],
            clients.MaxCoroutines,
            cancellationToken);

        // PORT: `**resolver_kwargs` passes `clients` only with prompt routing. The C# call passes null in its place,
        // which is the default of the parameter.
        var resolverClients = GraphitiTypes.UsesPromptRouting(clients) ? clients : null;
        List<List<EntityEdge>> edgeInvalidationCandidates = [];

        // PORT: zip(..., strict=True) of three or four lists is nested Py.ZipStrict calls. The lists have one length
        // by construction.
        foreach (var ((_, relatedEdges), invalidationResult) in Py.ZipStrict(
            Py.ZipStrict(extractedEdges, relatedEdgesLists),
            edgeInvalidationCandidateResults))
        {
            var relatedUuids = relatedEdges.Select(edge => edge.Uuid).ToHashSet(StringComparer.Ordinal);
            List<EntityEdge> deduplicated = [.. invalidationResult.Edges.Where(edge => !relatedUuids.Contains(edge.Uuid))];
            edgeInvalidationCandidates.Add(deduplicated);
        }

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            var uuids = Py.Repr(relatedEdgesLists.SelectMany(edgesLst => edgesLst).Select(e => (object?)e.Uuid).ToList());
            Log.RelatedEdges(Logger, uuids);
        }

        List<EntityEdge> watchedEdges = [];
        if (trace is not null)
        {
            var extractedUuids = extractedEdges.Select(edge => edge.Uuid).ToHashSet(StringComparer.Ordinal);
            foreach (var ((extractedEdge, relatedEdges), existingEdges) in Py.ZipStrict(
                Py.ZipStrict(extractedEdges, relatedEdgesLists),
                edgeInvalidationCandidates))
            {
                trace.RecordEdgeCandidates(extractedEdge.Uuid, relatedEdges, existingEdges);

                // An edge is a stored record when the call did not extract it.
                watchedEdges.AddRange(
                    new List<EntityEdge>([.. relatedEdges, .. existingEdges])
                        .Where(edge => !extractedUuids.Contains(edge.Uuid)));
            }

            trace.WatchStoredEdges(watchedEdges);
        }

        List<(EntityEdge, List<EntityEdge>, List<EntityEdge>?)> dedupeResults =
        [
            .. await Helpers.SemaphoreGatherAsync(
                [
                    .. Py.ZipStrict(
                        Py.ZipStrict(Py.ZipStrict(extractedEdges, relatedEdgesLists), edgeInvalidationCandidates),
                        edgeTypesLst)
                        .Select(item =>
                        {
                            var (((extractedEdge, relatedEdges), existingEdges), extractedEdgeTypes) = item;
                            return (Func<Task<(EntityEdge, List<EntityEdge>, List<EntityEdge>?)>>)(() => DedupeExtractedEdgeAsync(
                                llmClient,
                                extractedEdge,
                                relatedEdges,
                                existingEdges,
                                episode,
                                extractedEdgeTypes,
                                clients: resolverClients,
                                cancellationToken: cancellationToken));
                        }),
                ],
                clients.MaxCoroutines,
                cancellationToken),
        ];

        List<EntityEdge> resolvedEdges = [];
        List<EntityEdge> invalidatedEdges = [];
        List<EntityEdge> newEdges = [];
        foreach (var (extractedEdge, (resolvedEdge, _, _)) in Py.ZipStrict(extractedEdges, dedupeResults))
        {
            resolvedEdges.Add(resolvedEdge);

            // Track edges that are new (not duplicates of existing edges)
            // An edge is new if the resolved edge UUID matches the extracted edge UUID
            if (resolvedEdge.Uuid == extractedEdge.Uuid)
            {
                newEdges.Add(resolvedEdge);
            }
        }

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            var resolvedUuids = Py.Repr(resolvedEdges.Select(e => (object?)e.Uuid).ToList());
            Log.ResolvedEdges(Logger, resolvedUuids);
            var newUuids = Py.Repr(newEdges.Select(e => (object?)e.Uuid).ToList());
            Log.NewEdges(Logger, newUuids);
        }

        foreach (var (resolvedEdge, _, candidates) in dedupeResults)
        {
            if (candidates is null)
            {
                continue;
            }

            var hadEndDate = resolvedEdge.InvalidAt is not null;
            var invalidated = ApplyTemporalInvalidation(resolvedEdge, candidates);
            invalidatedEdges.AddRange(invalidated);
            if (trace is not null)
            {
                trace.MarkEndedAsCandidate(invalidated);
                if (!hadEndDate && resolvedEdge.InvalidAt is not null)
                {
                    trace.EndedByCandidate.Add(resolvedEdge.Uuid);
                }
            }

            if (invalidatedBy is not null)
            {
                foreach (var edge in invalidated)
                {
                    Py.SetDefault(invalidatedBy, edge.Uuid, []).Add(resolvedEdge.Uuid);
                }
            }
        }

        await Helpers.SemaphoreGatherAsync(
            [
                () => Edges.CreateEntityEdgeEmbeddingsAsync(embedder, resolvedEdges, cancellationToken),
                () => Edges.CreateEntityEdgeEmbeddingsAsync(embedder, invalidatedEdges, cancellationToken),
            ],
            clients.MaxCoroutines,
            cancellationToken);

        trace?.CollectEdgeOperations(watchedEdges);

        return (resolvedEdges, invalidatedEdges, newEdges);
    }

    // PORT: `seen` is read and written by key and never iterated, so it is a Dictionary with a value tuple key (R14).

    /// <summary>
    /// Return the earlier edge that `edge` duplicates, or add `edge` to `seen`.
    ///
    /// An edge is a duplicate of an earlier edge when the two have the same source, the
    /// same target, and the same normalized fact. It is also a duplicate when the two
    /// have opposite directions, the same normalized fact, and the same name.
    /// </summary>
    internal static EntityEdge? FindExactDuplicateEdge(
        EntityEdge edge,
        Dictionary<(string, string, string), EntityEdge> seen)
    {
        var key = (
            edge.SourceNodeUuid,
            edge.TargetNodeUuid,
            DedupHelpers.NormalizeStringExact(edge.Fact));
        if (seen.TryGetValue(key, out var found))
        {
            return found;
        }

        var reverseKey = (
            edge.TargetNodeUuid,
            edge.SourceNodeUuid,
            DedupHelpers.NormalizeStringExact(edge.Fact));
        var reverseEdge = seen.GetValueOrDefault(reverseKey);
        if (reverseEdge is not null && reverseEdge.Name == edge.Name)
        {
            return reverseEdge;
        }

        seen[key] = edge;
        return null;
    }

    // PORT: `uuid_entity_map` is read by key only (R14).

    /// <summary>Return the custom edge types whose signature matches the edge's endpoint labels.</summary>
    internal static OrderedDictionary<string, GraphTypeDescriptor> EdgeTypesForEndpoints(
        EntityEdge extractedEdge,
        Dictionary<string, EntityNode> uuidEntityMap,
        OrderedDictionary<string, GraphTypeDescriptor> edgeTypes,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap)
    {
        var sourceNode = uuidEntityMap.GetValueOrDefault(extractedEdge.SourceNodeUuid);
        var targetNode = uuidEntityMap.GetValueOrDefault(extractedEdge.TargetNodeUuid);
        List<string> sourceNodeLabels = sourceNode is not null ? [.. sourceNode.Labels, "Entity"] : ["Entity"];
        List<string> targetNodeLabels = targetNode is not null ? [.. targetNode.Labels, "Entity"] : ["Entity"];

        var matchingEdgeTypes = new OrderedDictionary<string, GraphTypeDescriptor>();
        foreach (var sourceLabel in sourceNodeLabels)
        {
            foreach (var targetLabel in targetNodeLabels)
            {
                foreach (var typeName in edgeTypeMap.GetValueOrDefault((sourceLabel, targetLabel), []))
                {
                    var typeModel = edgeTypes.GetValueOrDefault(typeName);
                    if (typeModel is not null)
                    {
                        matchingEdgeTypes[typeName] = typeModel;
                    }
                }
            }
        }

        return matchingEdgeTypes;
    }

    /// <summary>
    /// Apply the custom edge type signatures to the extracted edges.
    ///
    /// ``edge_types_lst[i]`` holds the custom edge types whose signature matches the
    /// endpoint labels of ``extracted_edges[i]``. An edge named after an edge type
    /// that declares at least one signature in ``edge_type_map``, but is not in its
    /// matching set, violates the signature. Such an edge is dropped when
    /// ``strict_edge_types`` is set, and renamed to ``DEFAULT_EDGE_NAME`` otherwise.
    /// Edges named after a type with no declared signature pass through unchanged,
    /// because such a type applies between any entity types.
    /// </summary>
    internal static (List<EntityEdge>, List<OrderedDictionary<string, GraphTypeDescriptor>>) EnforceEdgeTypeSignatures(
        List<EntityEdge> extractedEdges,
        List<OrderedDictionary<string, GraphTypeDescriptor>> edgeTypesLst,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        bool strictEdgeTypes)
    {
        var signedEdgeTypes = edgeTypeMap.Values.SelectMany(names => names).ToHashSet(StringComparer.Ordinal);
        List<EntityEdge> keptEdges = [];
        List<OrderedDictionary<string, GraphTypeDescriptor>> keptEdgeTypes = [];
        foreach (var (extractedEdge, matchingEdgeTypes) in Py.ZipStrict(extractedEdges, edgeTypesLst))
        {
            if (signedEdgeTypes.Contains(extractedEdge.Name)
                && !matchingEdgeTypes.ContainsKey(extractedEdge.Name))
            {
                if (strictEdgeTypes)
                {
                    Log.DroppingEdge(Logger, extractedEdge.Uuid, extractedEdge.Name);
                    continue;
                }

                Log.RenamingEdge(Logger, extractedEdge.Uuid, extractedEdge.Name, DefaultEdgeName);
                extractedEdge.Name = DefaultEdgeName;
                extractedEdge.Attributes = new();
            }

            keptEdges.Add(extractedEdge);
            keptEdgeTypes.Add(matchingEdgeTypes);
        }

        return (keptEdges, keptEdgeTypes);
    }

    // PORT: `invalidation_candidates.sort(key=...)` sorts the list of the caller in place with a tuple key that holds a
    // datetime (R14, Py.SortInPlace).

    /// <summary>
    /// Expire the resolved edge and any candidate it contradicts.
    ///
    /// Every check here compares dates, so the caller must have dated the edge first.
    /// Returns the candidate edges this edge invalidated.
    /// </summary>
    internal static List<EntityEdge> ApplyTemporalInvalidation(
        EntityEdge resolvedEdge,
        List<EntityEdge> invalidationCandidates)
    {
        var now = DatetimeUtils.UtcNow();

        // The edge arrived with an end date, so it is already historical.
        // PORT: upstream defect F22. A datetime is always true, so the truth test is a null test.
        if (resolvedEdge.InvalidAt is not null && resolvedEdge.ExpiredAt is null)
        {
            resolvedEdge.ExpiredAt = now;
        }

        // A candidate that starts later supersedes this edge, so expire this one instead.
        if (resolvedEdge.ExpiredAt is null)
        {
            Py.SortInPlace(invalidationCandidates, c => new PyTuple(c.ValidAt is null, DatetimeUtils.EnsureUtc(c.ValidAt)));
            foreach (var candidate in invalidationCandidates)
            {
                var candidateValidAtUtc = DatetimeUtils.EnsureUtc(candidate.ValidAt);
                var resolvedEdgeValidAtUtc = DatetimeUtils.EnsureUtc(resolvedEdge.ValidAt);
                if (candidateValidAtUtc is not null
                    && resolvedEdgeValidAtUtc is not null
                    && candidateValidAtUtc > resolvedEdgeValidAtUtc)
                {
                    resolvedEdge.InvalidAt = candidate.ValidAt;
                    resolvedEdge.ExpiredAt = now;
                    break;
                }
            }
        }

        return ResolveEdgeContradictions(resolvedEdge, invalidationCandidates);
    }

    internal static List<EntityEdge> ResolveEdgeContradictions(
        EntityEdge resolvedEdge,
        List<EntityEdge> invalidationCandidates)
    {
        if (invalidationCandidates.Count == 0)
        {
            return [];
        }

        // Determine which contradictory edges need to be expired
        List<EntityEdge> invalidatedEdges = [];
        foreach (var edge in invalidationCandidates)
        {
            // (Edge invalid before new edge becomes valid) or (new edge invalid before edge becomes valid)
            var edgeInvalidAtUtc = DatetimeUtils.EnsureUtc(edge.InvalidAt);
            var resolvedEdgeValidAtUtc = DatetimeUtils.EnsureUtc(resolvedEdge.ValidAt);
            var edgeValidAtUtc = DatetimeUtils.EnsureUtc(edge.ValidAt);
            var resolvedEdgeInvalidAtUtc = DatetimeUtils.EnsureUtc(resolvedEdge.InvalidAt);

            if ((edgeInvalidAtUtc is not null
                    && resolvedEdgeValidAtUtc is not null
                    && edgeInvalidAtUtc <= resolvedEdgeValidAtUtc)
                || (edgeValidAtUtc is not null
                    && resolvedEdgeInvalidAtUtc is not null
                    && resolvedEdgeInvalidAtUtc <= edgeValidAtUtc))
            {
                continue;
            }

            // New edge invalidates edge
            else if (edgeValidAtUtc is not null
                && resolvedEdgeValidAtUtc is not null
                && edgeValidAtUtc < resolvedEdgeValidAtUtc)
            {
                edge.InvalidAt = resolvedEdge.ValidAt;
                edge.ExpiredAt = edge.ExpiredAt is not null ? edge.ExpiredAt : DatetimeUtils.UtcNow();
                invalidatedEdges.Add(edge);
            }
        }

        return invalidatedEdges;
    }

    // PORT: `clients` is keyword-only in Python.

    /// <summary>
    /// Extract valid_at / invalid_at timestamps for an edge via a lightweight LLM call.
    ///
    /// Modifies the edge in place. Skips if the edge already has timestamps set
    /// (e.g., from the extraction prompt in the separate-extraction path) or if
    /// no reference time is available.
    /// clients: optional bundle that selects prompt overrides and model routes.
    /// </summary>
    internal static async Task ExtractEdgeTimestampsAsync(
        LLMClient llmClient,
        EntityEdge edge,
        EpisodicNode? episode,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        if (edge.ValidAt is not null || edge.InvalidAt is not null)
        {
            return;
        }

        // PORT: a datetime is always true, so `or` is `??`.
        var referenceTime = DatetimeUtils.EnsureUtc(
            edge.ReferenceTime ?? episode?.ValidAt);
        if (referenceTime is not { } reference)
        {
            return;
        }

        var context = new OrderedDictionary<string, object?>
        {
            ["fact"] = edge.Fact,
            ["reference_time"] = reference.IsoFormat(),
        };
        try
        {
            var llmResponse = await GraphitiTypes.GeneratePromptResponseAsync(
                llmClient,
                "extract_edges.extract_timestamps",
                Lib.PromptLibrary.ExtractEdges.ExtractTimestamps,
                context,
                clients: clients,
                responseModel: EdgeTimestamps.ResponseModel,
                modelSize: ModelSize.Small,
                cancellationToken: cancellationToken);
            var timestamps = EdgeTimestamps.ModelValidate(llmResponse);
            TemporalEdgeUtils.ApplyExtractedTimestamps(
                edge,
                timestamps.ValidAt,
                timestamps.InvalidAt,
                referenceTime);
        }
        catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
        {
            Log.FailedToExtractTimestamps(Logger, e, edge.Uuid);
        }
    }

    // PORT: `clients` is keyword-only in Python. `**resolver_kwargs` passes `clients` only when it is not None, and the
    // C# call passes `clients` as it is, which is the same default.

    /// <summary>
    /// Fully resolve one edge on its own: dedupe it, date it, then apply contradictions.
    ///
    /// This wrapper resolves one edge and applies its temporal contradictions.
    /// </summary>
    internal static async Task<(EntityEdge, List<EntityEdge>, List<EntityEdge>)> ResolveExtractedEdgeAsync(
        LLMClient llmClient,
        EntityEdge extractedEdge,
        List<EntityEdge> relatedEdges,
        List<EntityEdge> existingEdges,
        EpisodicNode episode,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypeCandidates = null,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        var (resolvedEdge, duplicateEdges, candidates) = await DedupeExtractedEdgeAsync(
            llmClient,
            extractedEdge,
            relatedEdges,
            existingEdges,
            episode,
            edgeTypeCandidates,
            clients: clients,
            cancellationToken: cancellationToken);
        if (candidates is null)
        {
            return (resolvedEdge, [], duplicateEdges);
        }

        var invalidatedEdges = ApplyTemporalInvalidation(resolvedEdge, candidates);
        return (resolvedEdge, invalidatedEdges, duplicateEdges);
    }
}

// PORT: a @dataclass is a sealed record with { get; set; } properties (R7). `field(default_factory=time)` is
// Py.TimeSeconds() (R8).

/// <summary>
/// The decision for one edge against its candidates.
///
/// `exact_match` is a duplicate candidate with the same endpoints and the same
/// normalized fact. When it has a value, no model call was made and the other fields
/// are empty. `duplicates` are the duplicate candidates that the model selected, and
/// the first one is the edge that the new edge resolves to. `contradicted` are the
/// candidates that the new edge contradicts.
/// </summary>
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

    /// <summary>
    /// Decide if an edge is a duplicate of a candidate and which candidates it contradicts.
    ///
    /// `related_edges` are the duplicate candidates. `existing_edges` are the invalidation
    /// candidates. The function extracts no attributes and no timestamps, and it changes
    /// no edge.
    /// </summary>
    internal static async Task<EdgeDecision> DecideEdgeAgainstCandidatesAsync(
        LLMClient llmClient,
        EntityEdge extractedEdge,
        List<EntityEdge> relatedEdges,
        List<EntityEdge> existingEdges,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        // Fast path: if the fact text and endpoints already exist verbatim, reuse the matching edge.
        var normalizedFact = DedupHelpers.NormalizeStringExact(extractedEdge.Fact);
        foreach (var edge in relatedEdges)
        {
            if (edge.SourceNodeUuid == extractedEdge.SourceNodeUuid
                && edge.TargetNodeUuid == extractedEdge.TargetNodeUuid
                && DedupHelpers.NormalizeStringExact(edge.Fact) == normalizedFact)
            {
                return new EdgeDecision { ExactMatch = edge };
            }
        }

        var start = Py.TimeSeconds();

        // Prepare context for LLM with continuous indexing
        List<object?> relatedEdgesContext =
        [
            .. relatedEdges.Index().Select(item => (object?)new OrderedDictionary<string, object?>
            {
                ["idx"] = (long)item.Index,
                ["fact"] = item.Item.Fact,
            }),
        ];

        // Invalidation candidates start where duplicate candidates end
        var invalidationIdxOffset = relatedEdges.Count;
        List<object?> invalidationEdgeCandidatesContext =
        [
            .. existingEdges.Index().Select(item => (object?)new OrderedDictionary<string, object?>
            {
                ["idx"] = (long)(invalidationIdxOffset + item.Index),
                ["fact"] = item.Item.Fact,
            }),
        ];

        var context = new OrderedDictionary<string, object?>
        {
            ["existing_edges"] = relatedEdgesContext,
            ["new_edge"] = extractedEdge.Fact,
            ["edge_invalidation_candidates"] = invalidationEdgeCandidatesContext,
        };

        if (relatedEdges.Count > 0 || existingEdges.Count > 0)
        {
            Log.ResolvingEdge(
                Logger,
                relatedEdges.Count,
                relatedEdges.Count > 0 ? " (idx 0-" + Py.Str((long)(relatedEdges.Count - 1)) + ")" : string.Empty,
                existingEdges.Count,
                existingEdges.Count > 0
                    ? " (idx " + Py.Str((long)invalidationIdxOffset) + "-" + Py.Str((long)(invalidationIdxOffset + existingEdges.Count - 1)) + ")"
                    : string.Empty);
        }

        var llmResponse = await GraphitiTypes.GeneratePromptResponseAsync(
            llmClient,
            "dedupe_edges.resolve_edge",
            Lib.PromptLibrary.DedupeEdges.ResolveEdge,
            context,
            clients: clients,
            responseModel: EdgeDuplicate.ResponseModel,
            modelSize: ModelSize.Small,
            cancellationToken: cancellationToken);
        var responseObject = EdgeDuplicate.ModelValidate(llmResponse);
        var duplicateFacts = responseObject.DuplicateFacts;

        // Validate duplicate_facts are in valid range for EXISTING FACTS
        List<long> invalidDuplicates = [.. duplicateFacts.Where(i => i < 0 || i >= relatedEdges.Count)];
        if (invalidDuplicates.Count > 0)
        {
            Log.InvalidDuplicateFacts(Logger, Py.Repr(invalidDuplicates.Select(i => (object?)i).ToList()), relatedEdges.Count - 1);
        }

        List<long> duplicateFactIds = [.. duplicateFacts.Where(i => 0 <= i && i < relatedEdges.Count)];

        // Process contradicted facts (continuous indexing across both lists)
        var contradictedFacts = responseObject.ContradictedFacts;
        List<EntityEdge> invalidationCandidates = [];

        // Only process contradictions if there are edges to check against
        if (relatedEdges.Count > 0 || existingEdges.Count > 0)
        {
            long maxValidIdx = relatedEdges.Count + existingEdges.Count - 1;
            List<long> invalidContradictions = [.. contradictedFacts.Where(i => i < 0 || i > maxValidIdx)];
            if (invalidContradictions.Count > 0)
            {
                Log.InvalidContradictedFacts(Logger, Py.Repr(invalidContradictions.Select(i => (object?)i).ToList()), maxValidIdx);
            }

            // Split contradicted facts into those from related_edges vs existing_edges based on offset
            foreach (var idx in contradictedFacts)
            {
                if (0 <= idx && idx < relatedEdges.Count)
                {
                    // From EXISTING FACTS (duplicate candidates)
                    invalidationCandidates.Add(relatedEdges[(int)idx]);
                }
                else if (invalidationIdxOffset <= idx && idx <= maxValidIdx)
                {
                    // From FACT INVALIDATION CANDIDATES (adjust index by offset)
                    invalidationCandidates.Add(existingEdges[(int)(idx - invalidationIdxOffset)]);
                }
            }
        }

        return new EdgeDecision
        {
            Duplicates = [.. duplicateFactIds.Select(idx => relatedEdges[(int)idx])],
            Contradicted = invalidationCandidates,
            StartedAt = start,
        };
    }

    // PORT: `clients` is keyword-only in Python. The nested function append_episode_provenance is a local function of
    // the body. The third item of the result is null where Python returns None.

    /// <summary>
    /// Deduplicate an extracted edge and gather the edges it might contradict.
    ///
    /// Stops before contradiction so the caller can apply temporal invalidation after
    /// the edge is dated.
    /// Returns (resolved_edge, duplicate_edges, invalidation_candidates). Candidates is
    /// None when the edge took an early return and must skip contradiction entirely;
    /// an empty list would instead mean "no candidates, but still run the checks".
    ///
    /// Parameters
    /// ----------
    /// llm_client : LLMClient
    ///     Client used to invoke the LLM for deduplication and attribute extraction.
    /// extracted_edge : EntityEdge
    ///     Newly extracted edge whose canonical representation is being resolved.
    /// related_edges : list[EntityEdge]
    ///     Candidate edges with identical endpoints used for duplicate detection.
    /// existing_edges : list[EntityEdge]
    ///     Broader set of edges evaluated for contradiction / invalidation.
    /// episode : EpisodicNode
    ///     Episode providing content context when extracting edge attributes.
    /// edge_type_candidates : dict[str, type[BaseModel]] | None
    ///     Custom edge types permitted for the current source/target signature.
    /// clients: optional bundle that selects prompt overrides and model routes.
    ///
    /// Returns
    /// -------
    /// tuple[EntityEdge, list[EntityEdge], list[EntityEdge]]
    ///     The resolved edge, any duplicates, and edges to invalidate.
    /// </summary>
    internal static async Task<(EntityEdge, List<EntityEdge>, List<EntityEdge>?)> DedupeExtractedEdgeAsync(
        LLMClient llmClient,
        EntityEdge extractedEdge,
        List<EntityEdge> relatedEdges,
        List<EntityEdge> existingEdges,
        EpisodicNode episode,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypeCandidates = null,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        void AppendEpisodeProvenance(EntityEdge resolvedEdge)
        {
            foreach (var episodeUuid in new List<string>([.. extractedEdge.Episodes, episode.Uuid]))
            {
                if (!resolvedEdge.Episodes.Contains(episodeUuid))
                {
                    resolvedEdge.Episodes.Add(episodeUuid);
                }
            }
        }

        // Nothing to compare against, so there is no duplicate and nothing to contradict.
        if (relatedEdges.Count == 0 && existingEdges.Count == 0)
        {
            // Still extract custom attributes and timestamps even when no dedup needed
            var noCandidateEdgeModel = edgeTypeCandidates is { Count: > 0 } ? edgeTypeCandidates.GetValueOrDefault(extractedEdge.Name) : null;
            if (noCandidateEdgeModel is not null && noCandidateEdgeModel.ModelFields.Count != 0)
            {
                var noCandidateAttributesContext = new OrderedDictionary<string, object?>
                {
                    ["fact"] = extractedEdge.Fact,
                    ["reference_time"] = episode is not null ? episode.ValidAt : null,
                    ["existing_attributes"] = extractedEdge.Attributes,
                };
                var noCandidateAttributesResponse = await GraphitiTypes.GeneratePromptResponseAsync(
                    llmClient,
                    "extract_edges.extract_attributes",
                    Lib.PromptLibrary.ExtractEdges.ExtractAttributes,
                    noCandidateAttributesContext,
                    clients: clients,
                    responseModel: noCandidateEdgeModel,
                    modelSize: ModelSize.Small,
                    attributeExtraction: true,
                    cancellationToken: cancellationToken);
                var (noCandidateMerged, _) = AttributeUtils.ApplyCappedAttributes(
                    noCandidateAttributesResponse,
                    noCandidateEdgeModel,
                    extractedEdge.Attributes,
                    mergeMode: "replace",
                    promptName: "extract_edges.extract_attributes",
                    entityUuid: extractedEdge.Uuid,
                    groupId: extractedEdge.GroupId);
                extractedEdge.Attributes = noCandidateMerged;
            }

            await ExtractEdgeTimestampsAsync(llmClient, extractedEdge, episode, clients, cancellationToken);

            return (extractedEdge, [], null);
        }

        var decision = await DecideEdgeAgainstCandidatesAsync(
            llmClient, extractedEdge, relatedEdges, existingEdges, clients: clients, cancellationToken: cancellationToken);

        // Fast path: the fact text and endpoints already exist verbatim, so reuse the matching edge.
        if (decision.ExactMatch is not null)
        {
            var resolved = decision.ExactMatch;
            AppendEpisodeProvenance(resolved);
            return (resolved, [], null);
        }

        var resolvedEdge = extractedEdge;
        foreach (var duplicateEdge in decision.Duplicates)
        {
            resolvedEdge = duplicateEdge;
            break;
        }

        if (decision.Duplicates.Count > 0)
        {
            AppendEpisodeProvenance(resolvedEdge);
        }

        var invalidationCandidates = decision.Contradicted;

        // Only extract structured attributes if the edge's relation_type matches an allowed custom type
        // AND the edge model exists for this node pair signature
        var edgeModel = edgeTypeCandidates is { Count: > 0 } ? edgeTypeCandidates.GetValueOrDefault(resolvedEdge.Name) : null;
        if (edgeModel is not null && edgeModel.ModelFields.Count != 0)
        {
            var edgeAttributesContext = new OrderedDictionary<string, object?>
            {
                ["fact"] = resolvedEdge.Fact,
                ["reference_time"] = episode is not null ? episode.ValidAt : null,
                ["existing_attributes"] = resolvedEdge.Attributes,
            };

            var edgeAttributesResponse = await GraphitiTypes.GeneratePromptResponseAsync(
                llmClient,
                "extract_edges.extract_attributes",
                Lib.PromptLibrary.ExtractEdges.ExtractAttributes,
                edgeAttributesContext,
                clients: clients,
                responseModel: edgeModel,
                modelSize: ModelSize.Small,
                attributeExtraction: true,
                cancellationToken: cancellationToken);
            var (merged, _) = AttributeUtils.ApplyCappedAttributes(
                edgeAttributesResponse,
                edgeModel,
                resolvedEdge.Attributes,
                mergeMode: "replace",
                promptName: "extract_edges.extract_attributes",
                entityUuid: resolvedEdge.Uuid,
                groupId: resolvedEdge.GroupId);
            resolvedEdge.Attributes = merged;
        }
        else if (edgeTypeCandidates is not null)
        {
            // Schema map was supplied but this relation has no model. Clear
            // attributes that belong to a prior schema. Do not wipe when the
            // caller omitted the map: add_triplet puts caller attributes on the
            // edge and never passes edge_type_candidates.
            resolvedEdge.Attributes = new();
        }

        // Extract timestamps for new edges (duplicated edges retain their existing timestamps)
        if (resolvedEdge.Uuid == extractedEdge.Uuid)
        {
            await ExtractEdgeTimestampsAsync(llmClient, resolvedEdge, episode, clients, cancellationToken);
        }

        var end = Py.TimeSeconds();
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            var elapsed = Py.Str((end - decision.StartedAt) * 1000);
            Log.ResolvedEdge(Logger, extractedEdge.Uuid, resolvedEdge.Uuid, elapsed);
        }

        return (resolvedEdge, decision.Duplicates, invalidationCandidates);
    }

    // PORT: `duplicate_nodes_map` is keyed by a pair of UUIDs and iterated by `.values()` (R14).
    internal static async Task<List<(EntityNode, EntityNode)>> FilterExistingDuplicateOfEdgesAsync(
        GraphDriver driver,
        List<(EntityNode, EntityNode)> duplicatesNodeTuples,
        CancellationToken cancellationToken = default)
    {
        if (duplicatesNodeTuples.Count == 0)
        {
            return [];
        }

        var duplicateNodesMap = new OrderedDictionary<(string, string), (EntityNode, EntityNode)>();
        foreach (var (source, target) in duplicatesNodeTuples)
        {
            duplicateNodesMap[(source.Uuid, target.Uuid)] = (source, target);
        }

        List<OrderedDictionary<string, object?>> records;
        if (driver.Provider == GraphProvider.Neptune)
        {
            var query = "\n" + """
            UNWIND $duplicate_node_uuids AS duplicate_tuple
            MATCH (n:Entity {uuid: duplicate_tuple.source})-[r:RELATES_TO {name: 'IS_DUPLICATE_OF'}]->(m:Entity {uuid: duplicate_tuple.target})
            RETURN DISTINCT
                n.uuid AS source_uuid,
                m.uuid AS target_uuid
""" + "\n        ";

            List<object?> duplicateNodes =
            [
                .. duplicatesNodeTuples.Select(pair => (object?)new OrderedDictionary<string, object?>
                {
                    ["source"] = pair.Item1.Uuid,
                    ["target"] = pair.Item2.Uuid,
                }),
            ];

            (records, _, _) = await driver.ExecuteQueryAsync(
                query,
                new OrderedDictionary<string, object?>
                {
                    ["duplicate_node_uuids"] = duplicateNodes,
                    ["routing_"] = "r",
                },
                cancellationToken);
        }
        else
        {
            var query = "\n" + """
            UNWIND $duplicate_node_uuids AS duplicate_tuple
            MATCH (n:Entity {uuid: duplicate_tuple[0]})-[r:RELATES_TO {name: 'IS_DUPLICATE_OF'}]->(m:Entity {uuid: duplicate_tuple[1]})
            RETURN DISTINCT
                n.uuid AS source_uuid,
                m.uuid AS target_uuid
""" + "\n        ";

            // PORT: `list(duplicate_nodes_map.keys())` is a list of Python tuples, which the value model holds as PyTuple
            // items (R6).
            List<object?> duplicateNodeUuids = [.. duplicateNodesMap.Keys.Select(key => (object?)new PyTuple(key.Item1, key.Item2))];

            (records, _, _) = await driver.ExecuteQueryAsync(
                query,
                new OrderedDictionary<string, object?>
                {
                    ["duplicate_node_uuids"] = duplicateNodeUuids,
                    ["routing_"] = "r",
                },
                cancellationToken);
        }

        // Remove duplicates that already have the IS_DUPLICATE_OF edge
        foreach (var record in records)
        {
            // PORT: the record values are open data. A key of the map holds two strings, so a pair with a value of
            // another type finds no entry, as in Python. A found value is a pair of nodes, which is always true.
            if (Py.Get(record, "source_uuid") is string sourceUuid
                && Py.Get(record, "target_uuid") is string targetUuid
                && duplicateNodesMap.ContainsKey((sourceUuid, targetUuid)))
            {
                duplicateNodesMap.Remove((sourceUuid, targetUuid));
            }
        }

        return [.. duplicateNodesMap.Values];
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Built {Count} episodic edges")]
        internal static partial void BuiltEpisodicEdges(ILogger logger, long count);

        [LoggerMessage(Level = LogLevel.Debug, Message = "No edge types available for strict edge extraction")]
        internal static partial void NoEdgeTypesForStrictExtraction(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping edge with empty source or target entity name")]
        internal static partial void SkippingEdgeWithEmptyName(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Skipping edge with relation type \"{RelationType}\" not in strict edge ontology")]
        internal static partial void SkippingEdgeNotInStrictOntology(ILogger logger, string relationType);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Extracted {Count} new edges in {Elapsed} ms")]
        internal static partial void ExtractedNewEdges(ILogger logger, long count, string elapsed);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Could not find source or target node for extracted edge")]
        internal static partial void CouldNotFindSourceOrTarget(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Error parsing valid_at date, skipping")]
        internal static partial void ErrorParsingValidAt(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Prefix}Error parsing invalid_at date: {Error}. Input: {InvalidAt}")]
        internal static partial void ErrorParsingInvalidAt(ILogger logger, string prefix, string error, string invalidAt);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Created new edge {Uuid} from {SourceNodeUuid} to {TargetNodeUuid}")]
        internal static partial void CreatedNewEdge(ILogger logger, string uuid, string sourceNodeUuid, string targetNodeUuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Extracted edges: {Uuids}")]
        internal static partial void ExtractedEdges(ILogger logger, string uuids);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Materialized {Count} dangling edge endpoint node(s) for group_id={GroupId}")]
        internal static partial void MaterializedDanglingEndpoints(ILogger logger, long count, string groupId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Related edges: {Uuids}")]
        internal static partial void RelatedEdges(ILogger logger, string uuids);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Resolved edges: {Uuids}")]
        internal static partial void ResolvedEdges(ILogger logger, string uuids);

        [LoggerMessage(Level = LogLevel.Debug, Message = "New edges (non-duplicates): {Uuids}")]
        internal static partial void NewEdges(ILogger logger, string uuids);

        [LoggerMessage(Level = LogLevel.Information, Message = "Dropping edge {Uuid}: name {Name} does not match the endpoint signature")]
        internal static partial void DroppingEdge(ILogger logger, string uuid, string name);

        [LoggerMessage(Level = LogLevel.Information, Message = "Renaming edge {Uuid} from {Name} to {DefaultName}: name does not match the endpoint signature")]
        internal static partial void RenamingEdge(ILogger logger, string uuid, string name, string defaultName);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to extract timestamps for edge {Uuid}")]
        internal static partial void FailedToExtractTimestamps(ILogger logger, Exception exception, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Resolving edge: sent {RelatedCount} EXISTING FACTS{RelatedRange} and {ExistingCount} INVALIDATION CANDIDATES{ExistingRange}")]
        internal static partial void ResolvingEdge(ILogger logger, long relatedCount, string relatedRange, long existingCount, string existingRange);

        [LoggerMessage(Level = LogLevel.Warning, Message = "LLM returned invalid duplicate_facts idx values {Values} (valid range: 0-{Max} for EXISTING FACTS)")]
        internal static partial void InvalidDuplicateFacts(ILogger logger, string values, long max);

        [LoggerMessage(Level = LogLevel.Warning, Message = "LLM returned invalid contradicted_facts idx values {Values} (valid range: 0-{Max})")]
        internal static partial void InvalidContradictedFacts(ILogger logger, string values, long max);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Resolved Edge: {ExtractedUuid} -> {ResolvedUuid}, in {Elapsed} ms")]
        internal static partial void ResolvedEdge(ILogger logger, string extractedUuid, string resolvedUuid, string elapsed);
    }
}
