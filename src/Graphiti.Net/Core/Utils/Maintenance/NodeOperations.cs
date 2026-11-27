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
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: a union that Python does not name (Part 3).
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
internal static partial class NodeOperations
{
    private static ILogger Logger => PortServices.Logger(typeof(NodeOperations));

    // Maximum number of nodes to summarize in a single LLM call
    internal const long MaxNodes = 30;

    internal const double NodeDedupCosineMinScore = 0.6;

    // PORT: the type alias NodeSummaryFilter has no C# type (R2). Each use is Func<EntityNode, Task<bool>>.

    /// <summary>
    /// Extract entity nodes from one or more episodes.
    ///
    /// Parameters
    /// ----------
    /// episode : EpisodicNode | list[EpisodicNode]
    ///     A single episode or a list of episodes to extract entities from.
    ///     When a list is provided, their contents are concatenated for extraction
    ///     and the first episode is used for metadata (source type, group_id, etc.).
    ///
    /// Returns
    /// -------
    /// tuple[list[EntityNode], dict[str, list[int]]]
    ///     A tuple of (extracted_nodes, node_episode_index_map) where
    ///     node_episode_index_map maps node UUID to a list of 0-indexed episode
    ///     positions that the node was extracted from.
    /// </summary>
    internal static async Task<(List<EntityNode>, OrderedDictionary<string, List<long>>)> ExtractNodesAsync(
        GraphitiClients clients,
        EpisodeOrEpisodes episode,
        List<EpisodicNode> previousEpisodes,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        List<string>? excludedEntityTypes = null,
        string? customExtractionInstructions = null,
        CancellationToken cancellationToken = default)
    {
        var episodes = episode is EpisodeOrEpisodes.Many many ? many.Episodes : [((EpisodeOrEpisodes.One)episode).Episode];
        var primaryEpisode = episodes[0];

        var start = Py.TimeSeconds();
        var llmClient = clients.LlmClient;

        // Build entity types context
        var entityTypesContext = FilterEntityTypesContext(
            BuildEntityTypesContext(entityTypes), excludedEntityTypes);
        if (entityTypesContext.Count == 0)
        {
            Log.NoEntityTypesAvailable(Logger);
            return ([], new OrderedDictionary<string, List<long>>());
        }

        // Build episode attribution instructions for multi-episode extraction
        var episodeAttribution = string.Empty;
        if (episodes.Count > 1)
        {
            episodeAttribution =
                "\n7. **Episode Attribution**: The content contains multiple episodes labeled "
                + "[Episode 0], [Episode 1], etc. Each episode header includes a timestamp indicating "
                + "when that episode occurred. For each extracted entity, set `episode_indices` "
                + "to the 0-based list of episode numbers where that entity is mentioned. "
                + "An entity appearing in Episodes 0 and 2 should have `episode_indices: [0, 2]`.";
        }

        // Build base context
        // PORT: `ep.valid_at.isoformat() if ep.valid_at else None`. valid_at is a required datetime, and a PyDateTime
        // cannot be null, so the `None` branch has no form (the same reason as in TextUtils.ConcatenateEpisodes).
        var context = new OrderedDictionary<string, object?>
        {
            ["episode_content"] = TextUtils.ConcatenateEpisodes(episodes),
            ["episode_timestamp"] = primaryEpisode.ValidAt.IsoFormat(),
            ["previous_episodes"] = previousEpisodes
                .Select(ep => (object?)new OrderedDictionary<string, object?>
                {
                    ["content"] = ep.Content,
                    ["timestamp"] = ep.ValidAt.IsoFormat(),
                })
                .ToList(),
            ["custom_extraction_instructions"] = (customExtractionInstructions ?? string.Empty)
                + episodeAttribution,
            ["entity_types"] = entityTypesContext,
            ["source_description"] = primaryEpisode.SourceDescription,
        };

        // Extract entities
        var extractedEntities = await ExtractNodesSingleAsync(
            llmClient,
            primaryEpisode,
            context,
            clients: clients,
            cancellationToken: cancellationToken);

        // Filter empty names
        var filteredEntities = extractedEntities.Where(e => PyText.Strip(e.Name).Length > 0).ToList();

        var end = Py.TimeSeconds();
        var logger = Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var count = Py.Str((long)filteredEntities.Count);
            var ms = Py.Format((end - start) * 1000, ".0f");
            Log.ExtractedEntities(logger, count, ms);
        }

        // Convert to EntityNode objects with episode attribution
        var (extractedNodes, nodeEpisodeIndexMap) = CreateEntityNodes(
            filteredEntities, entityTypesContext, excludedEntityTypes, episodes);
        extractedNodes = CollapseExactDuplicateExtractedNodes(
            extractedNodes, nodeEpisodeIndexMap);

        if (logger.IsEnabled(LogLevel.Debug))
        {
            var uuids = Py.Repr(extractedNodes.Select(n => n.Uuid).ToList());
            Log.ExtractedNodes(logger, uuids);
        }

        return (extractedNodes, nodeEpisodeIndexMap);
    }

    /// <summary>Build entity types context with ID mappings.</summary>
    // PORT: `list[dict]` is a list of open dicts that reach the prompt context (R6).
    internal static List<OrderedDictionary<string, object?>> BuildEntityTypesContext(
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)
    {
        List<OrderedDictionary<string, object?>> entityTypesContext =
        [
            new OrderedDictionary<string, object?>
            {
                ["entity_type_id"] = 0L,
                ["entity_type_name"] = "Entity",
                ["entity_type_description"] =
                    "A specific, identifiable entity that does not fit any of the other listed "
                    + "types. Must still be a concrete, meaningful thing — specific enough to be "
                    + "uniquely identifiable. GOOD: a named entity not covered by the other types. "
                    + "BAD: \"luck\", \"ideas\", \"tomorrow\", \"things\", \"them\", \"everybody\", "
                    + "\"a sense of wonder\", \"great times\". "
                    + "When in doubt, do not extract the entity.",
            },
        ];

        if (entityTypes is not null)
        {
            entityTypesContext.AddRange(
                entityTypes.Index().Select(item => new OrderedDictionary<string, object?>
                {
                    ["entity_type_id"] = (long)item.Index + 1,
                    ["entity_type_name"] = item.Item.Key,
                    ["entity_type_description"] = item.Item.Value.Doc,
                }));
        }

        return entityTypesContext;
    }

    /// <summary>Remove excluded entity types from prompt context and keep IDs contiguous.</summary>
    internal static List<OrderedDictionary<string, object?>> FilterEntityTypesContext(
        List<OrderedDictionary<string, object?>> entityTypesContext,
        List<string>? excludedEntityTypes)
    {
        if (excludedEntityTypes is null || excludedEntityTypes.Count == 0)
        {
            return entityTypesContext;
        }

        // PORT: `x not in set(...)` with `x` from open data compares by Python equality and hashes `x`, so the set
        // uses PyEqualityComparer (R14, R18).
        var excludedTypeNames = new HashSet<object?>(excludedEntityTypes, PyEqualityComparer<object?>.Instance);
        var filteredContext = entityTypesContext
            .Where(entityType => !excludedTypeNames.Contains(Py.Get(entityType, "entity_type_name")))
            .ToList();

        return filteredContext
            .Index()
            .Select(item =>
            {
                var entityType = new OrderedDictionary<string, object?>(item.Item)
                {
                    ["entity_type_id"] = (long)item.Index,
                };
                return entityType;
            })
            .ToList();
    }

    internal static string GetEntityTypeDescription(
        List<string> labels,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)
    {
        var typeName = labels.FirstOrDefault(label => entityTypes is not null && entityTypes.ContainsKey(label));
        var typeModel =
            entityTypes is not null && typeName is not null ? Py.Get(entityTypes, typeName) : null;
        var doc = typeModel is not null ? typeModel.Doc : null;
        return string.IsNullOrEmpty(doc) ? "Default Entity Type" : doc;
    }

    /// <summary>
    /// Extract a concise type description from a docstring for summary prompts.
    ///
    /// Returns the first paragraph (up to the first blank line), capped at 3
    /// sentences.  This strips GOOD/BAD examples, trigger patterns, and other
    /// extraction-specific guidance that is irrelevant to summarization.
    /// </summary>
    internal static string TruncateTypeDescription(string docstring)
    {
        // Take only the first paragraph.
        List<string> paragraphLines = [];
        foreach (var line in PyText.SplitLines(docstring))
        {
            if (PyText.Strip(line).Length == 0)
            {
                if (paragraphLines.Count > 0)
                {
                    break;
                }

                continue; // skip leading blank lines
            }

            paragraphLines.Add(line);
        }

        var text = string.Join(" ", paragraphLines.Select(line => PyText.Strip(line)));

        // Cap at 3 sentences.
        List<string> sentences = [];
        var remaining = text;
        for (var i = 0; i < 3; i++)
        {
            var idx = FindSentenceEnd(remaining);
            if (idx == -1)
            {
                sentences.Add(remaining);
                remaining = string.Empty;
                break;
            }

            sentences.Add(Py.Slice(remaining, null, idx + 1));
            remaining = PyText.LStrip(Py.Slice(remaining, idx + 1, null));
        }

        return PyText.Strip(string.Join(" ", sentences));
    }

    /// <summary>
    /// Return the index of the first sentence boundary.
    ///
    /// A sentence ends at `.`, `!`, or `?` when followed by end-of-string or
    /// a space then an uppercase letter.  This avoids splitting on abbreviations
    /// like "e.g.", "Dr.", or decimals like "2.0".
    /// </summary>
    // PORT: the index counts code points (R12).
    internal static long FindSentenceEnd(string text)
    {
        var chars = PyText.CodePoints(text).ToList();
        var n = chars.Count;
        foreach (var (i, ch) in chars.Index())
        {
            if (!".!?".Contains(ch, StringComparison.Ordinal))
            {
                continue;
            }

            // End of string counts as a sentence boundary.
            if (i + 1 >= n)
            {
                return i;
            }

            // Space followed by an uppercase letter is a sentence boundary.
            if (chars[i + 1] == " " && i + 2 < n && PyText.IsUpper(chars[i + 2]))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Extract entities using a single LLM call.</summary>
    // PORT: `clients` is keyword-only in Python. `context: dict` is open data (R6).
    internal static async Task<List<ExtractedEntity>> ExtractNodesSingleAsync(
        LLMClient llmClient,
        EpisodicNode episode,
        OrderedDictionary<string, object?> context,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        var llmResponse = await CallExtractionLlmAsync(llmClient, episode, context, clients: clients, cancellationToken: cancellationToken);
        var responseObject = ExtractedEntities.ModelValidate(llmResponse);
        return responseObject.ExtractedEntitiesValue;
    }

    /// <summary>Call the appropriate extraction prompt based on episode type.</summary>
    internal static async Task<OrderedDictionary<string, object?>> CallExtractionLlmAsync(
        LLMClient llmClient,
        EpisodicNode episode,
        OrderedDictionary<string, object?> context,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        PromptFunction legacyPrompt;
        string promptName;
        if (episode.Source == EpisodeType.Message)
        {
            legacyPrompt = Lib.PromptLibrary.ExtractNodes.ExtractMessage;
            promptName = "extract_nodes.extract_message";
        }
        else if (episode.Source == EpisodeType.Text)
        {
            legacyPrompt = Lib.PromptLibrary.ExtractNodes.ExtractText;
            promptName = "extract_nodes.extract_text";
        }
        else if (episode.Source == EpisodeType.Json)
        {
            legacyPrompt = Lib.PromptLibrary.ExtractNodes.ExtractJson;
            promptName = "extract_nodes.extract_json";
        }
        else
        {
            // Fallback to text extraction
            legacyPrompt = Lib.PromptLibrary.ExtractNodes.ExtractText;
            promptName = "extract_nodes.extract_text";
        }

        return await GraphitiTypes.GeneratePromptResponseAsync(
            llmClient,
            promptName,
            legacyPrompt,
            context,
            clients: clients,
            responseModel: ExtractedEntities.ResponseModel,
            groupId: episode.GroupId,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Convert ExtractedEntity objects to EntityNode objects.
    ///
    /// Returns
    /// -------
    /// tuple[list[EntityNode], dict[str, list[int]]]
    ///     A tuple of (nodes, node_episode_index_map) where node_episode_index_map
    ///     maps each node UUID to 0-indexed episode positions the node was extracted from.
    /// </summary>
    // PORT: `list({'Entity', str(entity_type_name)})` is `Entity` first, then the type name if it differs (R14,
    // decision D18).
    internal static (List<EntityNode>, OrderedDictionary<string, List<long>>) CreateEntityNodes(
        List<ExtractedEntity> extractedEntities,
        List<OrderedDictionary<string, object?>> entityTypesContext,
        List<string>? excludedEntityTypes,
        List<EpisodicNode> episodes)
    {
        var primaryEpisode = episodes[0];
        List<EntityNode> extractedNodes = [];
        var nodeEpisodeIndexMap = new OrderedDictionary<string, List<long>>();

        foreach (var extractedEntity in extractedEntities)
        {
            var typeId = extractedEntity.EntityTypeId;
            object? entityTypeName;
            if (0 <= typeId && typeId < entityTypesContext.Count)
            {
                entityTypeName = Py.Get(entityTypesContext[(int)typeId], "entity_type_name");
            }
            else
            {
                entityTypeName = "Entity";
            }

            // Check if this entity type should be excluded
            // PORT: `x in list` compares by Python equality, and an item of the list is a str (R18).
            if (excludedEntityTypes is { Count: > 0 } && excludedEntityTypes.Any(t => PyValueEquality.Equal(t, entityTypeName)))
            {
                var logger = Logger;
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
                Name = extractedEntity.Name,
                GroupId = primaryEpisode.GroupId,
                Labels = labels,
                Summary = string.Empty,
                CreatedAt = DatetimeUtils.UtcNow(),
            }.Validate();
            extractedNodes.Add(newNode);

            // Map node to 0-indexed episode positions (LLM returns 0-indexed).
            // Clamp to valid range; fall back to all episodes if empty.
            var indices = extractedEntity.EpisodeIndices.Where(i => 0 <= i && i < episodes.Count).ToList();
            if (indices.Count == 0)
            {
                indices = [.. Enumerable.Range(0, episodes.Count).Select(i => (long)i)];
            }

            nodeEpisodeIndexMap[newNode.Uuid] = indices;

            Log.CreatedNewNode(Logger, newNode.Uuid);
        }

        return (extractedNodes, nodeEpisodeIndexMap);
    }

    /// <summary>
    /// Collapse same-message duplicates with the same normalized name.
    ///
    /// This is intentionally narrow: it only merges exact normalized-name duplicates that the
    /// extraction prompt should already have emitted once. When duplicates disagree on specificity,
    /// keep the more specific node (for example, `Person` over bare `Entity`).
    ///
    /// When node_episode_index_map is provided, episode indices from discarded nodes are merged
    /// into the canonical node's entry so attribution is preserved.
    /// </summary>
    internal static List<EntityNode> CollapseExactDuplicateExtractedNodes(
        List<EntityNode> extractedNodes,
        OrderedDictionary<string, List<long>>? nodeEpisodeIndexMap = null)
    {
        if (extractedNodes.Count < 2)
        {
            return extractedNodes;
        }

        var canonicalByName = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        List<string> orderedNames = [];

        foreach (var node in extractedNodes)
        {
            var normalizedName = DedupHelpers.NormalizeStringExact(node.Name);
            var existing = Py.Get(canonicalByName, normalizedName);
            if (existing is null)
            {
                canonicalByName[normalizedName] = node;
                orderedNames.Add(normalizedName);
                continue;
            }

            var existingSpecificLabels = existing.Labels.Where(label => label != "Entity").ToHashSet(StringComparer.Ordinal);
            var nodeSpecificLabels = node.Labels.Where(label => label != "Entity").ToHashSet(StringComparer.Ordinal);
            if (nodeSpecificLabels.Count > existingSpecificLabels.Count || (
                nodeSpecificLabels.Count == existingSpecificLabels.Count
                && Py.Len(PyText.Strip(node.Name)) > Py.Len(PyText.Strip(existing.Name))))
            {
                var oldCanonical = existing;
                canonicalByName[normalizedName] = node;

                // Merge episode indices: old canonical -> new canonical
                if (nodeEpisodeIndexMap is not null)
                {
                    var oldIndices = nodeEpisodeIndexMap.Remove(oldCanonical.Uuid, out var popped) ? popped : [];
                    var newIndices = Py.Get(nodeEpisodeIndexMap, node.Uuid, []);
                    nodeEpisodeIndexMap[node.Uuid] = newIndices.Concat(oldIndices).ToHashSet().Order().ToList();
                }
            }
            else if (nodeEpisodeIndexMap is not null)
            {
                // Discard this node; merge its indices into the existing canonical
                var discardedIndices = nodeEpisodeIndexMap.Remove(node.Uuid, out var popped) ? popped : [];
                var canonicalIndices = Py.Get(nodeEpisodeIndexMap, existing.Uuid, []);
                nodeEpisodeIndexMap[existing.Uuid] = canonicalIndices.Concat(discardedIndices).ToHashSet().Order().ToList();
            }
        }

        return orderedNames.Select(name => canonicalByName[name]).ToList();
    }

    /// <summary>Deduplicate candidate nodes while preserving search order and overrides.</summary>
    internal static List<EntityNode> MergeCandidateNodes(
        List<EntityNode> candidateNodes,
        List<EntityNode>? existingNodesOverride = null)
    {
        var mergedCandidates = new List<EntityNode>(candidateNodes);
        if (existingNodesOverride is not null)
        {
            mergedCandidates.AddRange(existingNodesOverride);
        }

        var seenCandidateUuids = new HashSet<string>(StringComparer.Ordinal);
        List<EntityNode> orderedCandidates = [];
        foreach (var candidate in mergedCandidates)
        {
            // PORT: `if x in s: continue` and `s.add(x)` are one Add call (analyzer CA1868).
            if (!seenCandidateUuids.Add(candidate.Uuid))
            {
                continue;
            }

            orderedCandidates.Add(candidate);
        }

        return orderedCandidates;
    }

    /// <summary>
    /// Search per extracted name and return ordered candidates for each extracted node.
    ///
    /// With `reuse_name_embeddings`, a node that has a `name_embedding` uses it for the
    /// semantic search, and the embedder is called only for the other nodes.
    /// </summary>
    internal static async Task<List<List<EntityNode>>> CollectCandidateNodesAsync(
        GraphitiClients clients,
        List<EntityNode> extractedNodes,
        List<EntityNode>? existingNodesOverride = null,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        bool reuseNameEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        var identityResults = await CollectIdentityPropertyCandidateNodesAsync(
            clients,
            extractedNodes,
            identityProperties,
            cancellationToken);
        var exactNameResults = await CollectExactNameCandidateNodesAsync(clients, extractedNodes, cancellationToken);
        var searchResults = await SemanticCandidateSearchAsync(
            clients, extractedNodes, reuseNameEmbeddings: reuseNameEmbeddings, cancellationToken: cancellationToken);

        List<List<EntityNode>> candidatesByNode = [];

        // PORT: zip(a, b, c, strict=True) is two nested Py.ZipStrict calls. The pairs and the exception type are the
        // ones of Python. The text of the exception for a short third list names argument 2 and not argument 3.
        foreach (var ((identityResult, exactResults), searchResult) in Py.ZipStrict(
            Py.ZipStrict(identityResults, exactNameResults),
            searchResults))
        {
            candidatesByNode.Add(
                MergeCandidateNodes(
                    [.. identityResult, .. exactResults, .. searchResult],
                    existingNodesOverride));
        }

        return candidatesByNode;
    }

    // PORT: the nested coroutine _lookup_node is a lambda for Helpers.SemaphoreGatherAsync (R8).
    internal static async Task<List<List<EntityNode>>> CollectIdentityPropertyCandidateNodesAsync(
        GraphitiClients clients,
        List<EntityNode> extractedNodes,
        OrderedDictionary<string, List<string>>? identityProperties,
        CancellationToken cancellationToken = default)
    {
        if (extractedNodes.Count == 0)
        {
            return [];
        }

        if (identityProperties is null || identityProperties.Count == 0)
        {
            return extractedNodes.Select(_ => new List<EntityNode>()).ToList();
        }

        async Task<List<EntityNode>> LookupNodeAsync(EntityNode node)
        {
            var identityType = DedupHelpers.IdentityTypeForNode(node, identityProperties);
            var identityKey = DedupHelpers.IdentityKeyForNode(node, identityProperties);
            if (identityType is null || identityKey is null)
            {
                return [];
            }

            var identityValues = Py.AsTuple(identityKey[1]);
            var attributes = new OrderedDictionary<string, object?>();
            foreach (var pair in identityValues)
            {
                var item = Py.AsTuple(pair);
                attributes[Py.AsString(item[0])] = item[1];
            }

            try
            {
                return await EntityNode.GetByAttributeValuesAsync(
                    clients.Driver,
                    node.GroupId,
                    attributes,
                    [identityType],
                    Nodes.NodeDedupCandidateLimit,
                    cancellationToken);
            }
            catch (NotImplementedException)
            {
                return [];
            }
            catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
            {
                using (Logger.BeginScope(new OrderedDictionary<string, object?>
                {
                    ["node_uuid"] = node.Uuid,
                    ["group_id"] = node.GroupId,
                    ["identity_type"] = identityType,
                }))
                {
                    Log.IdentityLookupFailed(Logger, e);
                }

                return [];
            }
        }

        return
        [
            .. await Helpers.SemaphoreGatherAsync(
                extractedNodes.Select(node => (Func<Task<List<EntityNode>>>)(() => LookupNodeAsync(node))).ToList(),
                maxCoroutines: clients.MaxCoroutines,
                cancellationToken: cancellationToken),
        ];
    }

    /// <summary>Fetch durable exact-name candidates before embedding-based search.</summary>
    internal static async Task<List<List<EntityNode>>> CollectExactNameCandidateNodesAsync(
        GraphitiClients clients,
        List<EntityNode> extractedNodes,
        CancellationToken cancellationToken = default)
    {
        if (extractedNodes.Count == 0)
        {
            return [];
        }

        var names = extractedNodes.Where(node => node.Name.Length > 0).Select(node => node.Name).ToList();
        var groupIds = extractedNodes.Where(node => node.GroupId.Length > 0).Select(node => node.GroupId).ToList();
        if (names.Count == 0 || groupIds.Count == 0)
        {
            return extractedNodes.Select(_ => new List<EntityNode>()).ToList();
        }

        List<EntityNode> candidateNodes;
        try
        {
            candidateNodes = await EntityNode.GetByNamesAsync(clients.Driver, names, groupIds, cancellationToken);
        }
        catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
        {
            using (Logger.BeginScope(new OrderedDictionary<string, object?>
            {
                ["extracted_node_count"] = (long)extractedNodes.Count,
                ["group_count"] = (long)groupIds.ToHashSet(StringComparer.Ordinal).Count,
            }))
            {
                Log.ExactNameLookupFailed(Logger, e);
            }

            return extractedNodes.Select(_ => new List<EntityNode>()).ToList();
        }

        // PORT: a dict keyed by a tuple that code reads only by key is a Dictionary (R14).
        var candidatesByGroupAndName = new Dictionary<(string, string), List<EntityNode>>();
        foreach (var candidate in candidateNodes)
        {
            var nameKey = DedupHelpers.NormalizeStringExact(candidate.Name);
            if (nameKey.Length == 0)
            {
                continue;
            }

            Py.SetDefault(candidatesByGroupAndName, (candidate.GroupId, nameKey), []).Add(
                candidate);
        }

        return extractedNodes
            .Select(node => new List<EntityNode>(
                Py.Get(
                    candidatesByGroupAndName,
                    (node.GroupId, DedupHelpers.NormalizeStringExact(node.Name)),
                    [])))
            .ToList();
    }

    /// <summary>Run direct cosine similarity search per extracted node without reranking.</summary>
    internal static async Task<List<List<EntityNode>>> SemanticCandidateSearchAsync(
        GraphitiClients clients,
        List<EntityNode> extractedNodes,
        bool reuseNameEmbeddings = false,
        CancellationToken cancellationToken = default)
    {
        if (extractedNodes.Count == 0)
        {
            return [];
        }

        var queryIndices = extractedNodes
            .Index()
            .Where(item => PyText.Strip(item.Item.Name.Replace("\n", " ", StringComparison.Ordinal)).Length > 0)
            .Select(item => item.Index)
            .ToList();
        var queries = queryIndices.Select(index => extractedNodes[index].Name.Replace("\n", " ", StringComparison.Ordinal)).ToList();
        if (queries.Count == 0)
        {
            return extractedNodes.Select(_ => new List<EntityNode>()).ToList();
        }

        List<List<double>> queryVectors;
        if (reuseNameEmbeddings)
        {
            queryVectors = await NameVectorsWithReuseAsync(
                clients, queryIndices.Select(index => extractedNodes[index]).ToList(), queries, cancellationToken);
        }
        else
        {
            queryVectors = await EmbedQueriesAsync(clients, queries, cancellationToken);
        }

        var searchResults = await Helpers.SemaphoreGatherAsync(
            Py.ZipStrict(queryIndices, queryVectors)
                .Select(pair => (Func<Task<List<EntityNode>>>)(() => SearchUtils.NodeSimilaritySearchAsync(
                    clients.Driver,
                    pair.Second,
                    new SearchFilters().Validate(),
                    [extractedNodes[pair.First].GroupId],
                    Nodes.NodeDedupCandidateLimit,
                    NodeDedupCosineMinScore,
                    cancellationToken)))
                .ToList(),
            maxCoroutines: clients.MaxCoroutines,
            cancellationToken: cancellationToken);
        var results = extractedNodes.Select(_ => new List<EntityNode>()).ToList();
        foreach (var (index, searchResult) in Py.ZipStrict(queryIndices, searchResults))
        {
            results[index] = searchResult;
        }

        return results;
    }

    internal static async Task<List<List<double>>> EmbedQueriesAsync(
        GraphitiClients clients,
        List<string> queries,
        CancellationToken cancellationToken = default)
    {
        List<List<double>> queryVectors;
        try
        {
            queryVectors = await clients.Embedder.CreateBatchAsync(queries, cancellationToken);
        }
        catch (NotImplementedException)
        {
            queryVectors =
            [
                .. await Helpers.SemaphoreGatherAsync(
                    queries.Select(query => (Func<Task<List<double>>>)(() => clients.Embedder.CreateAsync(inputData: [query], cancellationToken))).ToList(),
                    maxCoroutines: clients.MaxCoroutines,
                    cancellationToken: cancellationToken),
            ];
        }

        if (queryVectors.Count != queries.Count)
        {
            throw new ArgumentException(
                "embedder returned " + Py.Str((long)queryVectors.Count) + " vectors for " + Py.Str((long)queries.Count) + " queries");
        }

        return queryVectors;
    }

    /// <summary>Return the stored name embedding of each node, and embed only the other names.</summary>
    internal static async Task<List<List<double>>> NameVectorsWithReuseAsync(
        GraphitiClients clients,
        List<EntityNode> nodes,
        List<string> queries,
        CancellationToken cancellationToken = default)
    {
        var vectors = nodes.Select(node => node.NameEmbedding).ToList();
        var missing = vectors.Index().Where(item => item.Item is null).Select(item => item.Index).ToList();
        if (missing.Count > 0)
        {
            var embedded = await EmbedQueriesAsync(clients, missing.Select(index => queries[index]).ToList(), cancellationToken);
            foreach (var (index, vector) in Py.ZipStrict(missing, embedded))
            {
                vectors[index] = vector;
            }
        }

        return vectors.OfType<List<double>>().ToList();
    }

    /// <summary>Commit a single-node resolution result into the batch-level state.</summary>
    internal static void CommitResolution(
        DedupResolutionState state,
        EntityNode? resolvedNode,
        OrderedDictionary<string, string> uuidMap,
        List<(EntityNode, EntityNode)> duplicatePairs,
        long index)
    {
        if (resolvedNode is not null)
        {
            state.ResolvedNodes[(int)index] = resolvedNode;
        }

        foreach (var (key, value) in uuidMap)
        {
            state.UuidMap[key] = value;
        }

        state.DuplicatePairs.AddRange(duplicatePairs);
    }

    /// <summary>
    /// Escalate unresolved nodes to the dedupe prompt so the LLM can select or reject duplicates.
    ///
    /// The guardrails below defensively ignore malformed or duplicate LLM responses so the
    /// ingestion workflow remains deterministic even when the model misbehaves.
    /// </summary>
    // PORT: `clients` is keyword-only in Python.
    internal static async Task ResolveWithLlmAsync(
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
        if (state.UnresolvedIndices.Count == 0)
        {
            return;
        }

        var entityTypesDict = entityTypes is not null ? entityTypes : new OrderedDictionary<string, GraphTypeDescriptor>();

        var llmExtractedNodes = state.UnresolvedIndices.Select(i => extractedNodes[(int)i]).ToList();

        var extractedNodesContext = llmExtractedNodes
            .Index()
            .Select(item => new OrderedDictionary<string, object?>
            {
                ["id"] = (long)item.Index,
                ["name"] = item.Item.Name,
                ["entity_type"] = item.Item.Labels,
                ["entity_type_description"] = GetEntityTypeDescription(item.Item.Labels, entityTypesDict),
            })
            .ToList();

        var sentIds = extractedNodesContext.Select(ctx => ctx["id"]).ToList();
        var logger = Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var sent = sentIds.Count < 20
                ? Py.Repr(sentIds)
                : Py.Repr(Py.Slice(sentIds, null, 10)) + "..." + Py.Repr(Py.Slice(sentIds, -10, null));
            Log.SendingEntities(logger, llmExtractedNodes.Count, (long)llmExtractedNodes.Count - 1, sent);
        }

        if (llmExtractedNodes.Count > 0)
        {
            var sampleSize = Math.Min(3, extractedNodesContext.Count);
            if (logger.IsEnabled(LogLevel.Debug))
            {
                var ids = Py.Repr(Py.Slice(extractedNodesContext, null, sampleSize).Select(ctx => ctx["id"]).ToList());
                Log.FirstEntityIds(logger, sampleSize, ids);
            }

            if (extractedNodesContext.Count > 3)
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    var ids = Py.Repr(Py.Slice(extractedNodesContext, -sampleSize, null).Select(ctx => ctx["id"]).ToList());
                    Log.LastEntityIds(logger, sampleSize, ids);
                }
            }
        }

        var existingNodesContext = indexes.ExistingNodes
            .Index()
            .Select(item => new OrderedDictionary<string, object?>(item.Item.Attributes)
            {
                ["candidate_id"] = (long)item.Index,
                ["name"] = item.Item.Name,
                ["entity_types"] = item.Item.Labels,
                ["summary"] = item.Item.Summary.Length > 0 ? Py.Slice(item.Item.Summary, null, 120) : string.Empty,
            })
            .ToList();

        // Build candidate_id -> node mapping for resolving duplicates by ID
        var candidatesById = new Dictionary<long, EntityNode>();
        foreach (var (i, node) in indexes.ExistingNodes.Index())
        {
            candidatesById[i] = node;
        }

        var context = new OrderedDictionary<string, object?>
        {
            ["extracted_nodes"] = extractedNodesContext,
            ["existing_nodes"] = existingNodesContext,
            ["episode_content"] = episode is not null ? episode.Content : string.Empty,
            ["previous_episodes"] = previousEpisodes is not null
                ? previousEpisodes
                    .Select(ep => (object?)new OrderedDictionary<string, object?>
                    {
                        ["content"] = ep.Content,
                        ["timestamp"] = ep.ValidAt.IsoFormat(),
                    })
                    .ToList()
                : new List<object?>(),
        };

        var dedupeNodesMaxTokens = Math.Min(llmClient.MaxTokens != 0 ? llmClient.MaxTokens : 16384, 16384);
        var llmResponse = await GraphitiTypes.GeneratePromptResponseAsync(
            llmClient,
            "dedupe_nodes.nodes",
            Lib.PromptLibrary.DedupeNodes.Nodes,
            context,
            clients: clients,
            responseModel: NodeResolutions.ResponseModel,
            maxTokens: dedupeNodesMaxTokens,
            cancellationToken: cancellationToken);

        var nodeResolutions = NodeResolutions.ModelValidate(llmResponse).EntityResolutions;

        var validRelativeRange = (long)state.UnresolvedIndices.Count;
        var processedRelativeIds = new HashSet<long>();

        var receivedIds = nodeResolutions.Select(r => r.Id).ToHashSet();
        var expectedIds = Enumerable.Range(0, state.UnresolvedIndices.Count).Select(i => (long)i).ToHashSet();
        var missingIds = expectedIds.Where(id => !receivedIds.Contains(id)).ToHashSet();
        var extraIds = receivedIds.Where(id => !expectedIds.Contains(id)).ToHashSet();

        Log.ReceivedResolutions(Logger, (long)nodeResolutions.Count, (long)state.UnresolvedIndices.Count);

        if (missingIds.Count > 0)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                Log.MissingResolutions(logger, Py.Repr(missingIds.Order().ToList()));
            }
        }

        if (extraIds.Count > 0)
        {
            if (logger.IsEnabled(LogLevel.Warning))
            {
                Log.InvalidResolutionIds(
                    logger,
                    (long)state.UnresolvedIndices.Count - 1,
                    Py.Repr(extraIds.Order().ToList()),
                    Py.Repr(receivedIds.Order().ToList()));
            }
        }

        foreach (var resolution in nodeResolutions)
        {
            var relativeId = resolution.Id;
            var duplicateCandidateId = resolution.DuplicateCandidateId;

            if (!(0 <= relativeId && relativeId < validRelativeRange))
            {
                Log.SkippingInvalidDedupeId(
                    Logger,
                    relativeId,
                    (long)state.UnresolvedIndices.Count - 1,
                    (long)nodeResolutions.Count);
                continue;
            }

            // PORT: `if x in s: ...; continue` and `s.add(x)` are one Add call (analyzer CA1868).
            if (!processedRelativeIds.Add(relativeId))
            {
                Log.DuplicateDedupeId(Logger, Py.Str(relativeId));
                continue;
            }

            var originalIndex = state.UnresolvedIndices[(int)relativeId];
            var extractedNode = extractedNodes[(int)originalIndex];

            EntityNode resolvedNode;
            if (duplicateCandidateId < 0)
            {
                resolvedNode = extractedNode;
            }
            else if (candidatesById.TryGetValue(duplicateCandidateId, out var candidate))
            {
                resolvedNode = DedupHelpers.PromoteResolvedNode(
                    extractedNode, candidate);
            }
            else
            {
                Log.InvalidDuplicateCandidateId(
                    Logger,
                    duplicateCandidateId,
                    extractedNode.Uuid);
                resolvedNode = extractedNode;
            }

            state.ResolvedNodes[(int)originalIndex] = resolvedNode;
            state.UuidMap[extractedNode.Uuid] = resolvedNode.Uuid;
            if (resolvedNode.Uuid != extractedNode.Uuid)
            {
                state.DuplicatePairs.Add((extractedNode, resolvedNode));
            }
        }
    }

    /// <summary>
    /// Resolve nodes with semantic retrieval first, then deterministic and LLM dedup.
    ///
    /// When `trace` is given, it records the candidates that each extracted node saw and
    /// one `PatchNode` operation for each extracted node that resolved to a candidate.
    /// </summary>
    internal static async Task<(List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>)>
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
        var candidateNodesByExtracted = await CollectCandidateNodesAsync(
            clients,
            extractedNodes,
            existingNodesOverride: existingNodesOverride,
            identityProperties: identityProperties,
            cancellationToken: cancellationToken);

        // The record is before the resolution, which promotes labels of candidates in memory.
        if (trace is not null)
        {
            foreach (var (node, candidates) in Py.ZipStrict(extractedNodes, candidateNodesByExtracted))
            {
                trace.RecordNodeCandidates(node.Uuid, candidates);
            }
        }

        return await ResolveNodesWithCandidatesAsync(
            clients,
            extractedNodes,
            candidateNodesByExtracted,
            episode,
            previousEpisodes,
            entityTypes,
            identityProperties,
            trace: trace,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Resolve nodes against given candidates: identity rule, similarity rule, then LLM.
    ///
    /// `candidate_nodes_by_extracted` has one candidate list for each extracted node.
    /// </summary>
    // PORT: `trace` is keyword-only in Python.
    internal static async Task<(List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>)>
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
        var llmClient = clients.LlmClient;

        var state = new DedupResolutionState
        {
            ResolvedNodes = [.. Enumerable.Repeat<EntityNode?>(null, extractedNodes.Count)],
            UuidMap = new OrderedDictionary<string, string>(),
            UnresolvedIndices = [],
        };

        foreach (var (idx, (node, candidates)) in Py.ZipStrict(extractedNodes, candidateNodesByExtracted).Index())
        {
            if (candidates.Count == 0)
            {
                continue;
            }

            var indexes = DedupHelpers.BuildCandidateIndexes(candidates);
            var localState = new DedupResolutionState
            {
                ResolvedNodes = [null],
                UuidMap = new OrderedDictionary<string, string>(),
                UnresolvedIndices = [],
            };
            DedupHelpers.ResolveWithIdentityProperties([node], indexes, localState, identityProperties);
            if (localState.ResolvedNodes[0] is not null)
            {
                CommitResolution(
                    state,
                    localState.ResolvedNodes[0],
                    localState.UuidMap,
                    localState.DuplicatePairs,
                    idx);
                continue;
            }

            DedupHelpers.ResolveWithSimilarity([node], indexes, localState);
            if (localState.ResolvedNodes[0] is not null)
            {
                CommitResolution(
                    state,
                    localState.ResolvedNodes[0],
                    localState.UuidMap,
                    localState.DuplicatePairs,
                    idx);
                continue;
            }

            state.UnresolvedIndices.Add(idx);
        }

        if (state.UnresolvedIndices.Count > 0)
        {
            var llmCandidateNodes = MergeCandidateNodes(
                [
                    .. state.UnresolvedIndices
                        .SelectMany(idx => candidateNodesByExtracted[(int)idx]),
                ]);
            if (trace is not null)
            {
                // The dedupe prompt shows the merged candidates to each unresolved node.
                foreach (var idx in state.UnresolvedIndices)
                {
                    trace.RecordNodeCandidates(extractedNodes[(int)idx].Uuid, llmCandidateNodes);
                }

                trace.NodeLlmCalls += 1;
            }

            await ResolveWithLlmAsync(
                llmClient,
                extractedNodes,
                DedupHelpers.BuildCandidateIndexes(llmCandidateNodes),
                state,
                episode,
                previousEpisodes,
                entityTypes,
                clients: clients,
                cancellationToken: cancellationToken);
        }

        if (state.UnresolvedIndices.Count == 0 && !candidateNodesByExtracted.Any(candidates => candidates.Count > 0))
        {
            Log.NoSemanticDedupCandidates(Logger);
        }

        foreach (var (idx, node) in extractedNodes.Index())
        {
            if (state.ResolvedNodes[idx] is null)
            {
                state.ResolvedNodes[idx] = node;
                state.UuidMap[node.Uuid] = node.Uuid;
            }
        }

        var logger = Logger;
        if (logger.IsEnabled(LogLevel.Debug))
        {
            var uuids = Py.Repr(state.ResolvedNodes.OfType<EntityNode>().Select(node => node.Uuid).ToList());
            Log.ResolvedNodes(logger, uuids);
        }

        if (trace is not null)
        {
            foreach (var node in extractedNodes)
            {
                trace.RecordNodeResolution(node, Py.Get(state.UuidMap, node.Uuid, node.Uuid));
            }
        }

        return (
            state.ResolvedNodes.OfType<EntityNode>().ToList(),
            state.UuidMap,
            state.DuplicatePairs);
    }

    /// <summary>Build a dictionary mapping node UUIDs to their connected edges.</summary>
    internal static OrderedDictionary<string, List<EntityEdge>> BuildEdgesByNode(List<EntityEdge>? edges)
    {
        var edgesByNode = new OrderedDictionary<string, List<EntityEdge>>();
        if (edges is null || edges.Count == 0)
        {
            return edgesByNode;
        }

        foreach (var edge in edges)
        {
            // PORT: `if k not in d: d[k] = []` is one TryAdd call (analyzer CA1854).
            edgesByNode.TryAdd(edge.SourceNodeUuid, []);
            edgesByNode.TryAdd(edge.TargetNodeUuid, []);

            edgesByNode[edge.SourceNodeUuid].Add(edge);
            edgesByNode[edge.TargetNodeUuid].Add(edge);
        }

        return edgesByNode;
    }

    internal static GraphTypeDescriptor? EntityTypeForNode(
        EntityNode node,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes)
    {
        if (entityTypes is null)
        {
            return null;
        }

        var typeName = node.Labels.FirstOrDefault(label => entityTypes.ContainsKey(label));
        return typeName is not null ? Py.Get(entityTypes, typeName) : null;
    }

    internal static async Task<List<EntityNode>> ExtractAttributesOnlyFromNodesAsync(
        GraphitiClients clients,
        List<EntityNode> nodes,
        EpisodeOrEpisodes? episode = null,
        List<EpisodicNode>? previousEpisodes = null,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        bool validateShape = true,
        CancellationToken cancellationToken = default)
    {
        if (nodes.Count == 0 || entityTypes is null || entityTypes.Count == 0)
        {
            return nodes;
        }

        List<(EntityNode, GraphTypeDescriptor)> extractionInputs = [];
        foreach (var node in nodes)
        {
            var entityType = EntityTypeForNode(node, entityTypes);
            if (entityType is null || entityType.ModelFields.Count == 0)
            {
                continue;
            }

            extractionInputs.Add((node, entityType));
        }

        if (extractionInputs.Count == 0)
        {
            return nodes;
        }

        var attributeResults = await Helpers.SemaphoreGatherAsync(
            extractionInputs
                .Select(input => (Func<Task<OrderedDictionary<string, object?>>>)(() => ExtractEntityAttributesAsync(
                    clients.LlmClient,
                    input.Item1,
                    episode,
                    previousEpisodes,
                    input.Item2,
                    clients: clients,
                    validateShape: validateShape,
                    cancellationToken: cancellationToken)))
                .ToList(),
            maxCoroutines: clients.MaxCoroutines,
            cancellationToken: cancellationToken);

        // _extract_entity_attributes returns the already-merged attribute dict
        // (overlay of prior + cap-kept fields), so direct assignment is the merge.
        foreach (var ((node, _), attributes) in Py.ZipStrict(
            extractionInputs,
            attributeResults))
        {
            node.Attributes = attributes;
        }

        return nodes;
    }

    // PORT: `should_summarize_node: NodeSummaryFilter | None` is Func<EntityNode, Task<bool>>? (R2, type alias).
    internal static async Task<List<EntityNode>> ExtractAttributesFromNodesAsync(
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
        var llmClient = clients.LlmClient;
        var embedder = clients.Embedder;

        // Pre-build edges lookup for O(E + N) instead of O(N * E)
        var edgesByNode = BuildEdgesByNode(edges);

        if (!skipAttributeExtraction)
        {
            await ExtractAttributesOnlyFromNodesAsync(
                clients,
                nodes,
                episode,
                previousEpisodes,
                entityTypes,
                cancellationToken: cancellationToken);
        }

        // Extract summaries in batch
        await ExtractEntitySummariesBatchAsync(
            llmClient,
            nodes,
            episode,
            previousEpisodes,
            shouldSummarizeNode,
            edgesByNode,
            skipFactAppending: skipFactAppending,
            entityTypes: includeTypeDescriptions ? entityTypes : null,
            clients: clients,
            maxCoroutines: clients.MaxCoroutines,
            cancellationToken: cancellationToken);

        await Nodes.CreateEntityNodeEmbeddingsAsync(embedder, nodes, cancellationToken);

        return nodes;
    }

    // PORT: the arguments after `entity_type` are keyword-only in Python. `entity_type` is the response model of the
    // prompt, and a custom type descriptor is an IResponseModel (R7, R15).
    internal static async Task<OrderedDictionary<string, object?>> ExtractEntityAttributesAsync(
        LLMClient llmClient,
        EntityNode node,
        EpisodeOrEpisodes? episode,
        List<EpisodicNode>? previousEpisodes,
        GraphTypeDescriptor? entityType,
        GraphitiClients? clients = null,
        bool validateShape = true,
        CancellationToken cancellationToken = default)
    {
        if (entityType is null || entityType.ModelFields.Count == 0)
        {
            // No applicable type means nothing to extract, not "extracted nothing": return the
            // node's prior attributes so the caller's assignment leaves them untouched. Returning
            // {} here would clear attributes a previous typed pass stored on a deduplicated node.
            // PORT: `node.attributes or {}`. Attributes is never null, and an empty dict gives a new empty dict either way.
            return new OrderedDictionary<string, object?>(node.Attributes);
        }

        var attributesContext = BuildEpisodeContext(
            // should not include summary
            nodeData: new OrderedDictionary<string, object?>
            {
                ["name"] = node.Name,
                ["entity_types"] = node.Labels,
                ["attributes"] = node.Attributes,
            },
            episode: episode,
            previousEpisodes: previousEpisodes);

        var llmResponse = await GraphitiTypes.GeneratePromptResponseAsync(
            llmClient,
            "extract_nodes.extract_attributes",
            Lib.PromptLibrary.ExtractNodes.ExtractAttributes,
            attributesContext,
            clients: clients,
            responseModel: entityType,
            modelSize: ModelSize.Small,
            groupId: node.GroupId,
            attributeExtraction: true,
            cancellationToken: cancellationToken);

        // Overlay merge: cap-dropped or LLM-omitted fields keep prior values.
        // See attribute_utils for the merge_mode contract; the edge path uses 'replace'.
        var (merged, _) = AttributeUtils.ApplyCappedAttributes(
            llmResponse,
            entityType,
            node.Attributes,
            mergeMode: "overlay",
            promptName: "extract_nodes.extract_attributes",
            entityUuid: node.Uuid,
            groupId: node.GroupId);

        // Shape validation only — we discard the validated instance because returning
        // `model_dump()` would expand defaults across all fields and clobber prior
        // values that the merge above just preserved.
        if (validateShape)
        {
            entityType.ModelValidate(merged);
        }

        return merged;
    }

    /// <summary>
    /// Extract summaries for multiple entities in batched LLM calls.
    ///
    /// When skip_fact_appending is False (default), nodes with short summaries get edge
    /// facts appended directly without an LLM call.  Nodes needing summarization are
    /// partitioned into flights of MAX_NODES and processed with separate LLM calls.
    ///
    /// When skip_fact_appending is True, the raw fact-append shortcut is bypassed and all
    /// nodes are routed through LLM summarization using an episode-based prompt that
    /// matches the async graph summary worker.
    /// </summary>
    // PORT: the arguments after `edges_by_node` are keyword-only in Python.
    internal static async Task ExtractEntitySummariesBatchAsync(
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
        // Determine which nodes need LLM summarization vs direct edge fact appending
        List<EntityNode> nodesNeedingLlm = [];

        foreach (var node in nodes)
        {
            // Check if node should be summarized at all
            if (shouldSummarizeNode is not null && !await shouldSummarizeNode(node))
            {
                continue;
            }

            if (skipFactAppending)
            {
                // Always route through LLM — no raw fact concatenation.
                if (episode is not null || node.Summary.Length > 0)
                {
                    nodesNeedingLlm.Add(node);
                }

                continue;
            }

            var nodeEdges = Py.Get(edgesByNode, node.Uuid, []);

            // Build summary with edge facts appended
            var summaryWithEdges = node.Summary;
            if (nodeEdges.Count > 0)
            {
                var edgeFacts = string.Join("\n", nodeEdges.Where(edge => edge.Fact.Length > 0).Select(edge => edge.Fact));
                summaryWithEdges = PyText.Strip(summaryWithEdges + "\n" + edgeFacts);
            }

            // If summary is close to the persisted limit, use it directly (append edge facts, no LLM call)
            if (summaryWithEdges.Length > 0 && Py.Len(summaryWithEdges) <= TextUtils.MaxSummaryChars * 2)
            {
                node.Summary = summaryWithEdges;
                continue;
            }

            // Skip if no summary content and no episode to generate from
            if (summaryWithEdges.Length == 0 && episode is null)
            {
                continue;
            }

            // This node needs LLM summarization
            nodesNeedingLlm.Add(node);
        }

        // If no nodes need LLM summarization, return early
        if (nodesNeedingLlm.Count == 0)
        {
            return;
        }

        // Partition nodes into flights of MAX_NODES
        List<List<EntityNode>> nodeFlights = [];
        for (var i = 0L; i < nodesNeedingLlm.Count; i += MaxNodes)
        {
            nodeFlights.Add(Py.Slice(nodesNeedingLlm, i, i + MaxNodes));
        }

        // Process flights in parallel
        await Helpers.SemaphoreGatherAsync(
            nodeFlights
                .Select(flight => (Func<Task>)(() => ProcessSummaryFlightAsync(
                    llmClient,
                    flight,
                    episode,
                    previousEpisodes,
                    useEpisodePrompt: skipFactAppending,
                    entityTypes: entityTypes,
                    clients: clients,
                    cancellationToken: cancellationToken)))
                .ToList(),
            maxCoroutines: maxCoroutines,
            cancellationToken: cancellationToken);
    }

    /// <summary>Process a single flight of nodes for batch summarization.</summary>
    // PORT: the arguments after `previous_episodes` are keyword-only in Python.
    internal static async Task ProcessSummaryFlightAsync(
        LLMClient llmClient,
        List<EntityNode> nodes,
        EpisodeOrEpisodes? episode,
        List<EpisodicNode>? previousEpisodes,
        bool useEpisodePrompt = false,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        // Build entity type descriptions from docstrings, stripping GOOD/BAD
        // few-shot examples that are intended for extraction prompts only.
        var entityTypeDescriptions = new OrderedDictionary<string, string>();
        if (entityTypes is not null)
        {
            foreach (var (typeName, typeModel) in entityTypes)
            {
                if (!string.IsNullOrEmpty(typeModel.Doc))
                {
                    entityTypeDescriptions[typeName] = TruncateTypeDescription(typeModel.Doc);
                }
            }
        }

        // Build context for batch summarization
        var entitiesContext = nodes
            .Select(node => new OrderedDictionary<string, object?>
            {
                ["name"] = node.Name,
                ["summary"] = node.Summary,
                ["entity_types"] = node.Labels,
                ["attributes"] = node.Attributes,
            })
            .ToList();

        string episodeContent;
        if (episode is null)
        {
            episodeContent = string.Empty;
        }
        else if (episode is EpisodeOrEpisodes.Many many)
        {
            episodeContent = TextUtils.ConcatenateEpisodes(many.Episodes);
        }
        else
        {
            episodeContent = ((EpisodeOrEpisodes.One)episode).Episode.Content;
        }

        var batchContext = new OrderedDictionary<string, object?>
        {
            ["entities"] = entitiesContext,
            ["episode_content"] = episodeContent,
            ["previous_episodes"] = previousEpisodes is not null
                ? previousEpisodes
                    .Select(ep => (object?)new OrderedDictionary<string, object?>
                    {
                        ["content"] = ep.Content,
                        ["timestamp"] = ep.ValidAt.IsoFormat(),
                    })
                    .ToList()
                : new List<object?>(),
            ["entity_type_descriptions"] = entityTypeDescriptions,
        };

        // Get group_id from the first node (all nodes in a batch should have same group_id)
        var groupId = nodes.Count > 0 ? nodes[0].GroupId : null;

        PromptFunction legacyPrompt;
        string promptName;
        if (useEpisodePrompt)
        {
            legacyPrompt = Lib.PromptLibrary.ExtractNodes.ExtractEntitySummariesFromEpisodes;
            promptName = "extract_nodes.extract_entity_summaries_from_episodes";
        }
        else
        {
            legacyPrompt = Lib.PromptLibrary.ExtractNodes.ExtractSummariesBatch;
            promptName = "extract_nodes.extract_summaries_batch";
        }

        var llmResponse = await GraphitiTypes.GeneratePromptResponseAsync(
            llmClient,
            promptName,
            legacyPrompt,
            batchContext,
            clients: clients,
            responseModel: SummarizedEntities.ResponseModel,
            modelSize: ModelSize.Small,
            groupId: groupId,
            cancellationToken: cancellationToken);

        // Build case-insensitive name -> nodes mapping (handles duplicates)
        var nameToNodes = new Dictionary<string, List<EntityNode>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var key = PyText.Lower(node.Name);
            // PORT: `if k not in d: d[k] = []` is one TryAdd call (analyzer CA1854).
            nameToNodes.TryAdd(key, []);

            nameToNodes[key].Add(node);
        }

        // Apply summaries from LLM response
        var summariesResponse = SummarizedEntities.ModelValidate(llmResponse);
        foreach (var summarizedEntity in summariesResponse.Summaries)
        {
            var matchingNodes = Py.Get(nameToNodes, PyText.Lower(summarizedEntity.Name), []);
            if (matchingNodes.Count > 0)
            {
                var truncatedSummary = TextUtils.TruncateAtSentence(summarizedEntity.Summary, TextUtils.MaxSummaryChars);
                foreach (var node in matchingNodes)
                {
                    node.Summary = truncatedSummary;
                }
            }
            else
            {
                var logger = Logger;
                if (logger.IsEnabled(LogLevel.Warning))
                {
                    Log.UnknownEntitySummary(logger, Py.Format(summarizedEntity.Name, ".30"));
                }
            }
        }
    }

    internal static OrderedDictionary<string, object?> BuildEpisodeContext(
        OrderedDictionary<string, object?> nodeData,
        EpisodeOrEpisodes? episode,
        List<EpisodicNode>? previousEpisodes)
    {
        string episodeContent;
        if (episode is null)
        {
            episodeContent = string.Empty;
        }
        else if (episode is EpisodeOrEpisodes.Many many)
        {
            episodeContent = TextUtils.ConcatenateEpisodes(many.Episodes);
        }
        else
        {
            episodeContent = ((EpisodeOrEpisodes.One)episode).Episode.Content;
        }

        return new OrderedDictionary<string, object?>
        {
            ["node"] = nodeData,
            ["episode_content"] = episodeContent,
            ["previous_episodes"] = previousEpisodes is not null
                ? previousEpisodes
                    .Select(ep => (object?)new OrderedDictionary<string, object?>
                    {
                        ["content"] = ep.Content,
                        ["timestamp"] = ep.ValidAt.IsoFormat(),
                    })
                    .ToList()
                : new List<object?>(),
        };
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "No entity types available for extraction after exclusions")]
        internal static partial void NoEntityTypesAvailable(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Extracted {Count} entities in {Ms} ms")]
        internal static partial void ExtractedEntities(ILogger logger, string count, string ms);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Extracted nodes: {Uuids}")]
        internal static partial void ExtractedNodes(ILogger logger, string uuids);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Excluding entity of type \"{EntityTypeName}\"")]
        internal static partial void ExcludingEntityOfType(ILogger logger, string entityTypeName);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Created new node: {Uuid}")]
        internal static partial void CreatedNewNode(ILogger logger, string uuid);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Identity-property node candidate lookup failed; falling back")]
        internal static partial void IdentityLookupFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Exact-name node candidate lookup failed; falling back to semantic search")]
        internal static partial void ExactNameLookupFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Sending {Count} entities to LLM for deduplication with IDs 0-{Last} (actual IDs sent: {SentIds})")]
        internal static partial void SendingEntities(ILogger logger, long count, long last, string sentIds);

        [LoggerMessage(Level = LogLevel.Debug, Message = "First {SampleSize} entity IDs: {Ids}")]
        internal static partial void FirstEntityIds(ILogger logger, long sampleSize, string ids);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Last {SampleSize} entity IDs: {Ids}")]
        internal static partial void LastEntityIds(ILogger logger, long sampleSize, string ids);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Received {Resolutions} resolutions for {Entities} entities")]
        internal static partial void ReceivedResolutions(ILogger logger, long resolutions, long entities);

        [LoggerMessage(Level = LogLevel.Warning, Message = "LLM did not return resolutions for IDs: {Ids}")]
        internal static partial void MissingResolutions(ILogger logger, string ids);

        [LoggerMessage(Level = LogLevel.Warning, Message = "LLM returned invalid IDs outside valid range 0-{Last}: {ExtraIds} (all returned IDs: {ReceivedIds})")]
        internal static partial void InvalidResolutionIds(ILogger logger, long last, string extraIds, string receivedIds);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping invalid LLM dedupe id {RelativeId} (valid range: 0-{Last}, received {Count} resolutions)")]
        internal static partial void SkippingInvalidDedupeId(ILogger logger, long relativeId, long last, long count);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Duplicate LLM dedupe id {RelativeId} received; ignoring.")]
        internal static partial void DuplicateDedupeId(ILogger logger, string relativeId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Invalid duplicate_candidate_id {DuplicateCandidateId} for extracted node {Uuid}; treating as no duplicate.")]
        internal static partial void InvalidDuplicateCandidateId(ILogger logger, long duplicateCandidateId, string uuid);

        [LoggerMessage(Level = LogLevel.Debug, Message = "No semantic dedup candidates found; keeping all extracted nodes as new")]
        internal static partial void NoSemanticDedupCandidates(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Resolved nodes: {Uuids}")]
        internal static partial void ResolvedNodes(ILogger logger, string uuids);

        [LoggerMessage(Level = LogLevel.Warning, Message = "LLM returned summary for unknown entity (first 30 chars): {Name}")]
        internal static partial void UnknownEntitySummary(ILogger logger, string name);
    }
}
