// Ported from graphiti_core/utils/bulk_utils.py
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

namespace Graphiti.Core.Utils;

using System.Reflection;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.Ingestion;
using Graphiti.Core.Models.Edges;
using Graphiti.Core.Models.Nodes;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: each private name of this module is internal, because judge cases and the recording of the unit read them
// (R2). A `tx: GraphDriverSession` parameter of a work function is a Transaction (R9). `edge_types` and
// `entity_types` are OrderedDictionaries of custom type descriptors (R15). `edge_type_map` has the form of
// edge_operations.
internal static partial class BulkUtils
{
    private static ILogger Logger => PortServices.Logger(typeof(BulkUtils));

    internal const long ChunkSize = 10;

    // PORT: the nested function find is a local function of the body.

    /// <summary>
    /// Collapse alias -> canonical chains while preserving direction.
    ///
    /// The incoming pairs represent directed mappings discovered during node dedupe. We use a simple
    /// union-find with iterative path compression to ensure every source UUID resolves to its ultimate
    /// canonical target, even if aliases appear lexicographically smaller than the canonical UUID.
    /// </summary>
    internal static OrderedDictionary<string, string> BuildDirectedUuidMap(List<(string, string)> pairs)
    {
        var parent = new OrderedDictionary<string, string>();

        // Directed union-find lookup using iterative path compression.
        string Find(string uuid)
        {
            // PORT: `parent.setdefault(uuid, uuid)` with the result unused is TryAdd (CA1854).
            parent.TryAdd(uuid, uuid);
            var root = uuid;
            while (parent[root] != root)
            {
                root = parent[root];
            }

            while (parent[uuid] != root)
            {
                var nextUuid = parent[uuid];
                parent[uuid] = root;
                uuid = nextUuid;
            }

            return root;
        }

        foreach (var (sourceUuid, targetUuid) in pairs)
        {
            parent.TryAdd(sourceUuid, sourceUuid);
            parent.TryAdd(targetUuid, targetUuid);

            // PORT: Python evaluates the value of an assignment before its subscript.
            var targetRoot = Find(targetUuid);
            parent[Find(sourceUuid)] = targetRoot;
        }

        // PORT: find changes the values of parent and adds no key, so the loop reads the keys in their order.
        var result = new OrderedDictionary<string, string>();
        foreach (var uuid in parent.Keys.ToList())
        {
            result[uuid] = Find(uuid);
        }

        return result;
    }
}

// PORT: a pydantic model (R7). No function of this module validates or dumps it, so its unit adds the R7 members
// that the judge case `pure.schemas.bulk_utils.RawEpisode` needs.
internal sealed record RawEpisode
{
    internal required string Name { get; set; }

    internal string? Uuid { get; set; }

    internal required string Content { get; set; }

    internal required string SourceDescription { get; set; }

    internal required EpisodeType Source { get; set; }

    internal required PyDateTime ReferenceTime { get; set; }

    internal static readonly PydanticSchema ModelJsonSchema = new PydanticSchema("RawEpisode")
        .Required("name", PydanticType.Str)
        .Default("uuid", PydanticType.Optional(PydanticType.Str), null)
        .Required("content", PydanticType.Str)
        .Required("source_description", PydanticType.Str)
        .Required("source", PydanticType.Enum<EpisodeType>(
            """
            Enumeration of different types of episodes that can be processed.

            This enum defines the various sources or formats of episodes that the system
            can handle. It's used to categorize and potentially handle different types
            of input data differently.

            Attributes:
            -----------
            message : str
                Represents a standard message-type episode. The content for this type
                should be formatted as "actor: content". For example, "user: Hello, how are you?"
                or "assistant: I'm doing well, thank you for asking."
            json : str
                Represents an episode containing a JSON string object with structured data.
            text : str
                Represents a plain text episode.
            """))
        .Required("reference_time", PydanticType.DateTime);
}

internal static partial class BulkUtils
{
    internal static async Task<List<(EpisodicNode, List<EpisodicNode>)>> RetrievePreviousEpisodesBulkAsync(
        GraphDriver driver,
        List<EpisodicNode> episodes,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        var previousEpisodesList = await Helpers.SemaphoreGatherAsync(
            [
                .. episodes.Select<EpisodicNode, Func<Task<List<EpisodicNode>>>>(episode => () => GraphDataOperations.RetrieveEpisodesAsync(
                    driver,
                    episode.ValidAt,
                    lastN: GraphDataOperations.EpisodeWindowLen,
                    groupIds: [episode.GroupId],
                    cancellationToken: cancellationToken)),
            ],
            maxCoroutines,
            cancellationToken);
        var episodeTuples = new List<(EpisodicNode, List<EpisodicNode>)>();
        foreach (var (i, episode) in episodes.Index())
        {
            episodeTuples.Add((episode, previousEpisodesList[i]));
        }

        return episodeTuples;
    }

    // PORT: no return annotation. The function returns None (TYPE_HINTS.md, R20).

    /// <summary>
    /// Write the records in one session.
    ///
    /// `write_timeout` is a time limit in seconds for the write. With the default `None`,
    /// the write has no time limit.
    /// </summary>
    internal static async Task AddNodesAndEdgesBulkAsync(
        GraphDriver driver,
        List<EpisodicNode> episodicNodes,
        List<EpisodicEdge> episodicEdges,
        List<EntityNode> entityNodes,
        List<EntityEdge> entityEdges,
        EmbedderClient embedder,
        double? writeTimeout = null,
        CancellationToken cancellationToken = default)
    {
        if (writeTimeout is not null)
        {
            // PORT: the extra arguments of Python are closed over by the work function (R9). It takes the token of
            // the call that runs it.
            await ExecuteWriteWithTimeoutAsync(
                driver,
                writeTimeout.Value,
                tx => AddNodesAndEdgesBulkTxAsync(
                    tx,
                    episodicNodes,
                    episodicEdges,
                    entityNodes,
                    entityEdges,
                    embedder,
                    driver: driver,
                    cancellationToken: cancellationToken),
                cancellationToken);
            return;
        }

        var session = driver.Session();
        try
        {
            await session.ExecuteWriteAsync(
                tx => AddNodesAndEdgesBulkTxAsync(
                    tx,
                    episodicNodes,
                    episodicEdges,
                    entityNodes,
                    entityEdges,
                    embedder,
                    driver: driver,
                    cancellationToken: cancellationToken),
                cancellationToken);
        }
        finally
        {
            // PORT: a cleanup call in finally also runs after a cancellation, so it passes no token (R8).
            await session.CloseAsync(CancellationToken.None);
        }
    }

    // PORT: `func, /, *args, **kwargs` is one work function that closes over its arguments (R9), and the result is the
    // result of execute_write, a Task with no result (R9). The Neo4j branch calls `neo4j.unit_of_work` from core code.
    // Decision D52 closes open item O6: `unit_of_work(timeout=timeout)(func)` is the session write that takes the
    // timeout in seconds as a parameter. Open item O9 (R8) still holds for the asyncio.wait_for branch: its budget does
    // not hold for a session call that takes no token.

    /// <summary>
    /// Run a write function in a new session with a time limit in seconds.
    ///
    /// On Neo4j, the limit is the transaction timeout, which the server applies. On
    /// FalkorDB, each query gets the limit as its query timeout. The server applies it to
    /// a write only when its `TIMEOUT_MAX` setting is not zero. On each provider other
    /// than Neo4j, the call is also cancelled at the limit. The database can then complete
    /// a write that it already started.
    /// </summary>
    internal static async Task ExecuteWriteWithTimeoutAsync(
        GraphDriver graphDriver,
        double timeout,
        Func<Transaction, Task> func,
        CancellationToken cancellationToken = default)
    {
        var session = graphDriver.Session();
        try
        {
            if (graphDriver.Provider == GraphProvider.Neo4j)
            {
                await session.ExecuteWriteAsync(func, timeout, cancellationToken);
                return;
            }

            if (session is IQueryTimeoutSession timeoutSession)
            {
                timeoutSession.QueryTimeoutMs = Math.Max(1, Py.Int(timeout * 1000)); // type: ignore[attr-defined]
            }

            // PORT: asyncio.wait_for (R8). The limit holds only for a session call that reads its token (open item O9).
            await Asyncio.WaitForAsync(token => session.ExecuteWriteAsync(func, token), timeout, PortServices.Time, cancellationToken);
        }
        finally
        {
            // PORT: a cleanup call in finally also runs after a cancellation, so it passes no token (R8).
            await session.CloseAsync(CancellationToken.None);
        }
    }

    // PORT: `list(set(node.labels + ['Entity']))` is `Entity` first, then the other labels in their first order (R14).

    /// <summary>Return the query parameters of one entity node for a bulk save or a bulk update.</summary>
    internal static async Task<OrderedDictionary<string, object?>> EntityNodeRecordAsync(
        EntityNode node,
        EmbedderClient embedder,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        if (node.NameEmbedding is null)
        {
            await node.GenerateNameEmbeddingAsync(embedder, cancellationToken);
        }

        var entityData = new OrderedDictionary<string, object?>
        {
            ["uuid"] = node.Uuid,
            ["name"] = node.Name,
            ["group_id"] = node.GroupId,
            ["summary"] = node.Summary,
            ["created_at"] = node.CreatedAt,
            ["name_embedding"] = node.NameEmbedding,
            ["labels"] = new PySet<string>(["Entity", .. node.Labels]).ToList(),
        };

        foreach (var (k, v) in node.Attributes)
        {
            // PORT: `if k not in entity_data: entity_data[k] = v` is TryAdd (CA1854).
            entityData.TryAdd(k, v);
        }

        return entityData;
    }

    /// <summary>Return the query parameters of one entity edge for a bulk save or a bulk update.</summary>
    internal static async Task<OrderedDictionary<string, object?>> EntityEdgeRecordAsync(
        EntityEdge edge,
        EmbedderClient embedder,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        if (edge.FactEmbedding is null)
        {
            await edge.GenerateEmbeddingAsync(embedder, cancellationToken);
        }

        var edgeData = new OrderedDictionary<string, object?>
        {
            ["uuid"] = edge.Uuid,
            ["source_node_uuid"] = edge.SourceNodeUuid,
            ["target_node_uuid"] = edge.TargetNodeUuid,
            ["name"] = edge.Name,
            ["fact"] = edge.Fact,
            ["group_id"] = edge.GroupId,
            ["episodes"] = edge.Episodes,
            ["created_at"] = edge.CreatedAt,
            ["expired_at"] = edge.ExpiredAt,
            ["valid_at"] = edge.ValidAt,
            ["invalid_at"] = edge.InvalidAt,
            ["reference_time"] = edge.ReferenceTime,
            ["fact_embedding"] = edge.FactEmbedding,
        };

        // Merge attributes without overwriting explicit edge fields.
        // Attributes may contain stale string versions of typed fields
        // (e.g. reference_time as ISO string) that would replace the
        // datetime values set above.
        foreach (var (k, v) in edge.Attributes)
        {
            // PORT: `if k not in edge_data: edge_data[k] = v` is TryAdd (CA1854).
            edgeData.TryAdd(k, v);
        }

        return edgeData;
    }

    // The operations of `graph_operations_interface` that the commit and the enrichment of
    // concurrent ingestion call. These operations have no Cypher fallback.
    // PORT: a module-level tuple of strings is a static readonly list (R17, Part 3).
    internal static readonly IReadOnlyList<string> IngestionInterfaceOperations =
    [
        "episodic_node_save_bulk",
        "node_save_bulk",
        "episodic_edge_save_bulk",
        "edge_save_bulk",
        "node_update_bulk",
        "edge_update_bulk",
    ];

    // PORT: the test of an override is the one site where reflection is allowed (R15). The result keeps the Python
    // operation names. Each Python name maps to the C# method of GraphOperationsInterface (R2). A C# subclass always
    // has the method, so `getattr(...) is None` is never true.

    /// <summary>
    /// Return the operations of concurrent ingestion that the interface of the driver lacks.
    ///
    /// An operation that the interface class does not override is missing. The result is
    /// empty for a driver with no `graph_operations_interface`.
    /// </summary>
    internal static List<string> MissingIngestionInterfaceOperations(GraphDriver driver)
    {
        var @interface = driver.GraphOperationsInterface;
        if (@interface is null)
        {
            return [];
        }

        return IngestionInterfaceOperations
            .Where(name =>
            {
                var method = @interface.GetType().GetMethod(
                    name switch
                    {
                        "episodic_node_save_bulk" => nameof(GraphOperationsInterface.EpisodicNodeSaveBulkAsync),
                        "node_save_bulk" => nameof(GraphOperationsInterface.NodeSaveBulkAsync),
                        "episodic_edge_save_bulk" => nameof(GraphOperationsInterface.EpisodicEdgeSaveBulkAsync),
                        "edge_save_bulk" => nameof(GraphOperationsInterface.EdgeSaveBulkAsync),
                        "node_update_bulk" => nameof(GraphOperationsInterface.NodeUpdateBulkAsync),
                        _ => nameof(GraphOperationsInterface.EdgeUpdateBulkAsync),
                    },
                    BindingFlags.Instance | BindingFlags.NonPublic);
                return method is null || method.DeclaringType == typeof(GraphOperationsInterface);
            })
            .Order(Py.StringComparer)
            .ToList();
    }

    // PORT: no return annotation. The function returns None (TYPE_HINTS.md, R20).

    /// <summary>Update entity edges that exist. An edge that does not exist is not created.</summary>
    internal static async Task UpdateEntityEdgesTxAsync(
        Transaction tx,
        List<EntityEdge> entityEdges,
        EmbedderClient embedder,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        if (entityEdges.Count == 0)
        {
            return;
        }

        var edges = new List<OrderedDictionary<string, object?>>();
        foreach (var edge in entityEdges)
        {
            edges.Add(await EntityEdgeRecordAsync(edge, embedder, driver, cancellationToken));
        }

        if (driver.GraphOperationsInterface is not null)
        {
            await driver.GraphOperationsInterface.EdgeUpdateBulkAsync(null, driver, tx, edges, cancellationToken: cancellationToken);
            return;
        }

        var query = EdgeDbQueries.GetEntityEdgeUpdateBulkQuery(driver.Provider);
        await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["entity_edges"] = edges }, cancellationToken);
    }

    /// <summary>Update entity nodes that exist. A node that does not exist is not created.</summary>
    internal static async Task UpdateEntityNodesTxAsync(
        Transaction tx,
        List<EntityNode> entityNodes,
        EmbedderClient embedder,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        if (entityNodes.Count == 0)
        {
            return;
        }

        var nodes = new List<OrderedDictionary<string, object?>>();
        foreach (var node in entityNodes)
        {
            nodes.Add(await EntityNodeRecordAsync(node, embedder, driver, cancellationToken));
        }

        if (driver.GraphOperationsInterface is not null)
        {
            await driver.GraphOperationsInterface.NodeUpdateBulkAsync(null, driver, tx, nodes, cancellationToken: cancellationToken);
            return;
        }

        // PORT: the query builder returns `str | Any` (object?), and tx.run takes the text, as EntityNodeOps reads it.
        var query = Py.AsString(NodeDbQueries.GetEntityNodeUpdateBulkQuery(driver.Provider, nodes));
        await tx.RunAsync(query, new OrderedDictionary<string, object?> { ["nodes"] = nodes }, cancellationToken);
    }

    // PORT: `dict(episode)` of a pydantic model is a dict of its fields, which is EpisodicNode.ModelDump: the model has
    // no nested model, and the query reads the values only. `edge.model_dump()` is EpisodicEdge.ModelDump (R7).
    internal static async Task AddNodesAndEdgesBulkTxAsync(
        Transaction tx,
        List<EpisodicNode> episodicNodes,
        List<EpisodicEdge> episodicEdges,
        List<EntityNode> entityNodes,
        List<EntityEdge> entityEdges,
        EmbedderClient embedder,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        var episodes = episodicNodes.Select(episode => episode.ModelDump()).ToList();
        foreach (var episode in episodes)
        {
            episode["source"] = Py.Str(Py.EnumValue(Py.AsEnum<EpisodeType>(episode["source"])));
            episode.Remove("labels");
        }

        var nodes = new List<OrderedDictionary<string, object?>>();
        foreach (var node in entityNodes)
        {
            nodes.Add(await EntityNodeRecordAsync(node, embedder, driver, cancellationToken));
        }

        var edges = new List<OrderedDictionary<string, object?>>();
        foreach (var edge in entityEdges)
        {
            edges.Add(await EntityEdgeRecordAsync(edge, embedder, driver, cancellationToken));
        }

        if (driver.GraphOperationsInterface is not null)
        {
            await driver.GraphOperationsInterface.EpisodicNodeSaveBulkAsync(null, driver, tx, episodes, cancellationToken: cancellationToken);
            await driver.GraphOperationsInterface.NodeSaveBulkAsync(null, driver, tx, nodes, cancellationToken: cancellationToken);
            await driver.GraphOperationsInterface.EpisodicEdgeSaveBulkAsync(
                null, driver, tx, episodicEdges.Select(edge => edge.ModelDump()).ToList(), cancellationToken: cancellationToken);
            await driver.GraphOperationsInterface.EdgeSaveBulkAsync(null, driver, tx, edges, cancellationToken: cancellationToken);
        }
        else
        {
            await tx.RunAsync(
                NodeDbQueries.GetEpisodeNodeSaveBulkQuery(driver.Provider),
                new OrderedDictionary<string, object?> { ["episodes"] = episodes },
                cancellationToken);
            await tx.RunAsync(
                Py.AsString(NodeDbQueries.GetEntityNodeSaveBulkQuery(driver.Provider, nodes)),
                new OrderedDictionary<string, object?> { ["nodes"] = nodes },
                cancellationToken);
            await tx.RunAsync(
                EdgeDbQueries.GetEpisodicEdgeSaveBulkQuery(driver.Provider),
                new OrderedDictionary<string, object?> { ["episodic_edges"] = episodicEdges.Select(edge => edge.ModelDump()).ToList() },
                cancellationToken);
            await tx.RunAsync(
                EdgeDbQueries.GetEntityEdgeSaveBulkQuery(driver.Provider),
                new OrderedDictionary<string, object?> { ["entity_edges"] = edges },
                cancellationToken);
        }
    }

    internal static Task<(List<List<EntityNode>>, List<List<EntityEdge>>)> ExtractNodesAndEdgesBulkAsync(
        GraphitiClients clients,
        List<(EpisodicNode, List<EpisodicNode>)> episodeTuples,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        List<string>? excludedEntityTypes = null,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes = null,
        string? customExtractionInstructions = null,
        bool useCombinedExtraction = true,
        bool strictEdgeTypes = false,
        CancellationToken cancellationToken = default)
    {
        if (useCombinedExtraction)
        {
            return ExtractNodesAndEdgesBulkCombinedAsync(
                clients,
                episodeTuples,
                edgeTypeMap,
                entityTypes: entityTypes,
                excludedEntityTypes: excludedEntityTypes,
                edgeTypes: edgeTypes,
                strictEdgeTypes: strictEdgeTypes,
                customExtractionInstructions: customExtractionInstructions,
                cancellationToken: cancellationToken);
        }

        return ExtractNodesAndEdgesBulkSeparateAsync(
            clients,
            episodeTuples,
            edgeTypeMap,
            entityTypes: entityTypes,
            excludedEntityTypes: excludedEntityTypes,
            edgeTypes: edgeTypes,
            strictEdgeTypes: strictEdgeTypes,
            customExtractionInstructions: customExtractionInstructions,
            cancellationToken: cancellationToken);
    }

    // PORT: the lazy import of combined_extraction is a normal reference (R19).

    /// <summary>Combined extraction: single LLM call per episode for both nodes and edges.</summary>
    internal static async Task<(List<List<EntityNode>>, List<List<EntityEdge>>)> ExtractNodesAndEdgesBulkCombinedAsync(
        GraphitiClients clients,
        List<(EpisodicNode, List<EpisodicNode>)> episodeTuples,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        List<string>? excludedEntityTypes = null,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes = null,
        bool strictEdgeTypes = false,
        string? customExtractionInstructions = null,
        CancellationToken cancellationToken = default)
    {
        var results = await Helpers.SemaphoreGatherAsync(
            [
                .. episodeTuples.Select<(EpisodicNode, List<EpisodicNode>), Func<Task<(List<EntityNode>, List<EntityEdge>, OrderedDictionary<string, List<long>>)>>>(
                    tuple => () => CombinedExtractionModule.ExtractNodesAndEdgesAsync(
                        clients,
                        new EpisodeOrEpisodes.One { Episode = tuple.Item1 },
                        tuple.Item2,
                        entityTypes: entityTypes,
                        excludedEntityTypes: excludedEntityTypes,
                        edgeTypeMap: edgeTypeMap,
                        edgeTypes: edgeTypes,
                        strictEdgeTypes: strictEdgeTypes,
                        customExtractionInstructions: customExtractionInstructions,
                        cancellationToken: cancellationToken)),
            ],
            clients.MaxCoroutines,
            cancellationToken);

        var nodesBulk = results.Select(result => result.Item1).ToList();
        var edgesBulk = results.Select(result => result.Item2).ToList();
        return (nodesBulk, edgesBulk);
    }

    internal static async Task<(List<List<EntityNode>>, List<List<EntityEdge>>)> ExtractNodesAndEdgesBulkSeparateAsync(
        GraphitiClients clients,
        List<(EpisodicNode, List<EpisodicNode>)> episodeTuples,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        List<string>? excludedEntityTypes = null,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes = null,
        bool strictEdgeTypes = false,
        string? customExtractionInstructions = null,
        CancellationToken cancellationToken = default)
    {
        var extractedResults = await Helpers.SemaphoreGatherAsync(
            [
                .. episodeTuples.Select<(EpisodicNode, List<EpisodicNode>), Func<Task<(List<EntityNode>, OrderedDictionary<string, List<long>>)>>>(
                    tuple => () => NodeOperations.ExtractNodesAsync(
                        clients,
                        new EpisodeOrEpisodes.One { Episode = tuple.Item1 },
                        tuple.Item2,
                        entityTypes: entityTypes,
                        excludedEntityTypes: excludedEntityTypes,
                        customExtractionInstructions: customExtractionInstructions,
                        cancellationToken: cancellationToken)),
            ],
            clients.MaxCoroutines,
            cancellationToken);
        var extractedNodesBulk = extractedResults.Select(result => result.Item1).ToList();

        var edgeResults = await Helpers.SemaphoreGatherAsync(
            [
                .. episodeTuples.Index().Select<(int, (EpisodicNode, List<EpisodicNode>)), Func<Task<(List<EntityEdge>, List<EntityNode>)>>>(
                    item => () => EdgeOperations.ExtractEdgesAsync(
                        clients,
                        new EpisodeOrEpisodes.One { Episode = item.Item2.Item1 },
                        extractedNodesBulk[item.Item1],
                        item.Item2.Item2,
                        edgeTypeMap: edgeTypeMap,
                        groupId: item.Item2.Item1.GroupId,
                        edgeTypes: edgeTypes,
                        strictEdgeTypes: strictEdgeTypes,
                        customExtractionInstructions: customExtractionInstructions,
                        cancellationToken: cancellationToken)),
            ],
            clients.MaxCoroutines,
            cancellationToken);

        var extractedEdgesBulk = new List<List<EntityEdge>>();
        foreach (var (i, (edges, materializedNodes)) in edgeResults.Index())
        {
            extractedEdgesBulk.Add(edges);
            extractedNodesBulk[i].AddRange(materializedNodes);
        }

        return (extractedNodesBulk, extractedEdgesBulk);
    }

    // PORT: the nested coroutine resolve_nodes is a local function of the body, and each call is a lambda for
    // Helpers.SemaphoreGatherAsync (R8). `**trace_kwargs` passes the trace only when it is not None, which is the
    // default of the parameter, so each call passes `trace`.

    /// <summary>
    /// Resolve entity duplicates across an in-memory batch using a two-pass strategy.
    ///
    /// 1. Run :func:`resolve_extracted_nodes` for every episode in parallel so each batch item is
    ///    reconciled against the live graph just like the non-batch flow.
    /// 2. Re-run the deterministic similarity heuristics across the union of resolved nodes to catch
    ///    duplicates that only co-occur inside this batch, emitting a canonical UUID map that callers
    ///    can apply to edges and persistence.
    ///
    /// When `trace` is given, the first pass records the candidates that each extracted node
    /// saw and the stored nodes that an extracted node resolved to.
    /// </summary>
    internal static async Task<(OrderedDictionary<string, List<EntityNode>>, OrderedDictionary<string, string>)>
        DedupeNodesBulkAsync(
            GraphitiClients clients,
            List<List<EntityNode>> extractedNodes,
            List<(EpisodicNode, List<EpisodicNode>)> episodeTuples,
            OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
            OrderedDictionary<string, List<string>>? identityProperties = null,
            ResolutionTrace? trace = null,
            CancellationToken cancellationToken = default)
    {
        async Task<(List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>)> ResolveNodesAsync(
            List<EntityNode> nodes,
            EpisodicNode episode,
            List<EpisodicNode> previousEpisodes)
        {
            if (identityProperties is not null && identityProperties.Count > 0)
            {
                return await NodeOperations.ResolveExtractedNodesAsync(
                    clients,
                    nodes,
                    episode,
                    previousEpisodes,
                    entityTypes,
                    identityProperties: identityProperties,
                    trace: trace,
                    cancellationToken: cancellationToken);
            }

            return await NodeOperations.ResolveExtractedNodesAsync(
                clients,
                nodes,
                episode,
                previousEpisodes,
                entityTypes,
                trace: trace,
                cancellationToken: cancellationToken);
        }

        var firstPassResults = await Helpers.SemaphoreGatherAsync(
            [
                .. extractedNodes.Index().Select<(int, List<EntityNode>), Func<Task<(List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>)>>>(
                    item => () => ResolveNodesAsync(
                        item.Item2,
                        episodeTuples[item.Item1].Item1,
                        episodeTuples[item.Item1].Item2)),
            ],
            clients.MaxCoroutines,
            cancellationToken);

        var episodeResolutions = new List<(string, List<EntityNode>)>();
        var perEpisodeUuidMaps = new List<OrderedDictionary<string, string>>();
        var duplicatePairs = new List<(string, string)>();

        foreach (var ((resolvedNodes, uuidMap, duplicates), (episode, _)) in Py.ZipStrict(firstPassResults, episodeTuples))
        {
            episodeResolutions.Add((episode.Uuid, resolvedNodes));
            perEpisodeUuidMaps.Add(uuidMap);
            duplicatePairs.AddRange(duplicates.Select(pair => (pair.Item1.Uuid, pair.Item2.Uuid)));
        }

        var canonicalNodes = new OrderedDictionary<string, EntityNode>();
        foreach (var (_, resolvedNodes) in episodeResolutions)
        {
            foreach (var node in resolvedNodes)
            {
                // NOTE: this loop is O(n^2) in the number of nodes inside the batch because we rebuild
                // the MinHash index for the accumulated canonical pool each time. The LRU-backed
                // shingle cache keeps the constant factors low for typical batch sizes (≤ CHUNK_SIZE),
                // but if batches grow significantly we should switch to an incremental index or chunked
                // processing.
                if (canonicalNodes.Count == 0)
                {
                    canonicalNodes[node.Uuid] = node;
                    continue;
                }

                var existingCandidates = canonicalNodes.Values.ToList();
                if (identityProperties is not null && identityProperties.Count > 0)
                {
                    // Attribute and summary hydration uses all episodes that mention an identity-typed
                    // node; name and similarity matches use the first episode (#2156).
                    var identityType = DedupHelpers.IdentityTypeForNode(node, identityProperties);
                    var identityKey = DedupHelpers.IdentityKeyForNode(node, identityProperties);
                    if (identityType is not null && identityKey is not null)
                    {
                        // PORT: `==` on two identity keys is the value equality of PyTuple (R14).
                        var identityMatch = existingCandidates.FirstOrDefault(
                            candidate => DedupHelpers.IdentityTypeForNode(candidate, identityProperties) == identityType
                                && Equals(DedupHelpers.IdentityKeyForNode(candidate, identityProperties), identityKey));
                        if (identityMatch is not null)
                        {
                            foreach (var (key, value) in node.Attributes)
                            {
                                Py.SetDefault(identityMatch.Attributes, key, value);
                            }

                            if (identityMatch.Uuid != node.Uuid)
                            {
                                duplicatePairs.Add((node.Uuid, identityMatch.Uuid));
                            }

                            continue;
                        }
                    }
                }

                var normalized = DedupHelpers.NormalizeStringExact(node.Name);
                var exactMatch = existingCandidates.FirstOrDefault(
                    candidate => DedupHelpers.NormalizeStringExact(candidate.Name) == normalized);
                if (exactMatch is not null)
                {
                    if (exactMatch.Uuid != node.Uuid)
                    {
                        duplicatePairs.Add((node.Uuid, exactMatch.Uuid));
                    }

                    continue;
                }

                var indexes = DedupHelpers.BuildCandidateIndexes(existingCandidates);
                var state = new DedupResolutionState
                {
                    ResolvedNodes = [null],
                    UuidMap = new(),
                    UnresolvedIndices = [],
                };
                DedupHelpers.ResolveWithSimilarity([node], indexes, state);

                var resolved = state.ResolvedNodes[0];
                if (resolved is null)
                {
                    canonicalNodes[node.Uuid] = node;
                    continue;
                }

                var canonicalUuid = resolved.Uuid;
                canonicalNodes.TryAdd(canonicalUuid, resolved);
                if (canonicalUuid != node.Uuid)
                {
                    duplicatePairs.Add((node.Uuid, canonicalUuid));
                }
            }
        }

        var unionPairs = new List<(string, string)>();
        foreach (var uuidMap in perEpisodeUuidMaps)
        {
            unionPairs.AddRange(uuidMap.Select(entry => (entry.Key, entry.Value)));
        }

        unionPairs.AddRange(duplicatePairs);

        var compressedMap = BuildDirectedUuidMap(unionPairs);

        var nodesByEpisode = new OrderedDictionary<string, List<EntityNode>>();
        foreach (var (episodeUuid, resolvedNodes) in episodeResolutions)
        {
            var dedupedNodes = new List<EntityNode>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in resolvedNodes)
            {
                var canonicalUuid = Py.Get(compressedMap, node.Uuid, node.Uuid);

                // PORT: `if x in seen: continue` and `seen.add(x)` are one Add (CA1868).
                if (!seen.Add(canonicalUuid))
                {
                    continue;
                }

                var canonicalNode = canonicalNodes.GetValueOrDefault(canonicalUuid);
                if (canonicalNode is null)
                {
                    Log.CanonicalNodeMissing(Logger, canonicalUuid, node.Uuid);
                    canonicalNode = node;
                }

                dedupedNodes.Add(canonicalNode);
            }

            nodesByEpisode[episodeUuid] = dedupedNodes;
        }

        return (nodesByEpisode, compressedMap);
    }

    // PORT: `_entities` and `_edge_type_map` lose the underscore (R2). np.dot is a plain loop in index order (R18), and
    // it raises ValueError for two lengths, where Py.ZipStrict throws ArgumentException (ValueError).
    internal static async Task<OrderedDictionary<string, List<EntityEdge>>> DedupeEdgesBulkAsync(
        GraphitiClients clients,
        List<List<EntityEdge>> extractedEdges,
        List<(EpisodicNode, List<EpisodicNode>)> episodeTuples,
        List<EntityNode> entities,
        OrderedDictionary<string, GraphTypeDescriptor> edgeTypes,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        CancellationToken cancellationToken = default)
    {
        var embedder = clients.Embedder;
        var minScore = 0.6;

        // generate embeddings
        await Helpers.SemaphoreGatherAsync(
            [.. extractedEdges.Select<List<EntityEdge>, Func<Task>>(edges => () => Edges.CreateEntityEdgeEmbeddingsAsync(embedder, edges, cancellationToken))],
            clients.MaxCoroutines,
            cancellationToken);

        // Find similar results
        var dedupeTuples = new List<(EpisodicNode, EntityEdge, List<EntityEdge>)>();
        foreach (var (i, edgesI) in extractedEdges.Index())
        {
            var existingEdges = new List<EntityEdge>();
            foreach (var edgesJ in extractedEdges)
            {
                existingEdges.AddRange(edgesJ);
            }

            foreach (var edge in edgesI)
            {
                var candidates = new List<EntityEdge>();
                foreach (var existingEdge in existingEdges)
                {
                    // Skip self-comparison
                    if (edge.Uuid == existingEdge.Uuid)
                    {
                        continue;
                    }

                    // Approximate BM25 by checking for word overlaps (this is faster than creating many in-memory indices)
                    // This approach will cast a wider net than BM25, which is ideal for this use case
                    if (edge.SourceNodeUuid != existingEdge.SourceNodeUuid
                        || edge.TargetNodeUuid != existingEdge.TargetNodeUuid)
                    {
                        continue;
                    }

                    var edgeWords = new HashSet<string>(PyText.Split(PyText.Lower(edge.Fact)), StringComparer.Ordinal);
                    var existingEdgeWords = new HashSet<string>(PyText.Split(PyText.Lower(existingEdge.Fact)), StringComparer.Ordinal);
                    var hasOverlap = edgeWords.Overlaps(existingEdgeWords);
                    if (hasOverlap)
                    {
                        candidates.Add(existingEdge);
                        continue;
                    }

                    // Check for semantic similarity even if there is no overlap
                    var similarity = 0.0;
                    foreach (var (a, b) in Py.ZipStrict(
                        Helpers.NormalizeL2(edge.FactEmbedding ?? []),
                        Helpers.NormalizeL2(existingEdge.FactEmbedding ?? [])))
                    {
                        similarity += a * b;
                    }

                    if (similarity >= minScore)
                    {
                        candidates.Add(existingEdge);
                    }
                }

                dedupeTuples.Add((episodeTuples[i].Item1, edge, candidates));
            }
        }

        var resolverClients = GraphitiTypes.UsesPromptRouting(clients) ? clients : null;
        var bulkEdgeResolutions = await Helpers.SemaphoreGatherAsync(
            [
                .. dedupeTuples.Select<(EpisodicNode, EntityEdge, List<EntityEdge>), Func<Task<(EntityEdge, List<EntityEdge>, List<EntityEdge>)>>>(
                    tuple => () => EdgeOperations.ResolveExtractedEdgeAsync(
                        clients.LlmClient,
                        tuple.Item2,
                        tuple.Item3,
                        tuple.Item3,
                        tuple.Item1,
                        edgeTypes,
                        clients: resolverClients,
                        cancellationToken: cancellationToken)),
            ],
            clients.MaxCoroutines,
            cancellationToken);

        // For now we won't track edge invalidation
        var duplicatePairs = new List<(string, string)>();
        foreach (var (i, (_, _, duplicates)) in bulkEdgeResolutions.Index())
        {
            var (episode, edge, candidates) = dedupeTuples[i];
            foreach (var duplicate in duplicates)
            {
                duplicatePairs.Add((edge.Uuid, duplicate.Uuid));
            }
        }

        // Now we compress the duplicate_map, so that 3 -> 2 and 2 -> becomes 3 -> 1 (sorted by uuid)
        var compressedMap = CompressUuidMap(duplicatePairs);

        // PORT: a dict that code reads only by key is a Dictionary (R14).
        var edgeUuidMap = new Dictionary<string, EntityEdge>(StringComparer.Ordinal);
        foreach (var edges in extractedEdges)
        {
            foreach (var edge in edges)
            {
                edgeUuidMap[edge.Uuid] = edge;
            }
        }

        foreach (var edges in extractedEdges)
        {
            foreach (var edge in edges)
            {
                var canonicalUuid = Py.Get(compressedMap, edge.Uuid, edge.Uuid);
                if (canonicalUuid != edge.Uuid)
                {
                    var canonicalEdge = edgeUuidMap[canonicalUuid];
                    foreach (var episodeUuid in edge.Episodes)
                    {
                        if (!canonicalEdge.Episodes.Contains(episodeUuid))
                        {
                            canonicalEdge.Episodes.Add(episodeUuid);
                        }
                    }
                }
            }
        }

        var edgesByEpisode = new OrderedDictionary<string, List<EntityEdge>>();
        foreach (var (i, edges) in extractedEdges.Index())
        {
            var episode = episodeTuples[i].Item1;

            edgesByEpisode[episode.Uuid] = edges.Select(edge => edgeUuidMap[Py.Get(compressedMap, edge.Uuid, edge.Uuid)]).ToList();
        }

        return edgesByEpisode;
    }
}

// PORT: the parameters have no annotation (TYPE_HINTS.md: Unknown, R20). The one caller, compress_uuid_map, passes a
// set of UUID strings, which is a PySet<string> in the order of the pairs (R14). `self.parent` is read and written by
// key only (R14).
internal sealed class UnionFind
{
    internal UnionFind(PySet<string> elements)
    {
        // start each element in its own set
        Parent = elements.ToDictionary(e => e, e => e, StringComparer.Ordinal);
    }

    internal Dictionary<string, string> Parent { get; set; }

    internal string Find(string x)
    {
        // path‐compression
        if (Parent[x] != x)
        {
            Parent[x] = Find(Parent[x]);
        }

        return Parent[x];
    }

    // PORT: upstream defect U18 (R21): the root with the smaller UUID wins, compared with Py.CompareStrings.
    internal void Union(string a, string b)
    {
        var (ra, rb) = (Find(a), Find(b));
        if (ra == rb)
        {
            return;
        }

        // attach the lexicographically larger root under the smaller
        if (Py.CompareStrings(ra, rb) < 0)
        {
            Parent[rb] = ra;
        }
        else
        {
            Parent[ra] = rb;
        }
    }
}

internal static partial class BulkUtils
{
    /// <summary>
    /// all_ids: iterable of all entity IDs (strings)
    /// duplicate_pairs: iterable of (id1, id2) pairs
    /// returns: dict mapping each id -> lexicographically smallest id in its duplicate set
    /// </summary>
    // PORT: upstream defect U18 (R21): the merge goes onto the smallest UUID.
    internal static OrderedDictionary<string, string> CompressUuidMap(List<(string, string)> duplicatePairs)
    {
        var allUuids = new PySet<string>();
        foreach (var pair in duplicatePairs)
        {
            allUuids.Add(pair.Item1);
            allUuids.Add(pair.Item2);
        }

        var uf = new UnionFind(allUuids);
        foreach (var (a, b) in duplicatePairs)
        {
            uf.Union(a, b);
        }

        // ensure full path‐compression before mapping
        var result = new OrderedDictionary<string, string>();
        foreach (var uuid in allUuids)
        {
            result[uuid] = uf.Find(uuid);
        }

        return result;
    }

    // PORT: `E = TypeVar('E', bound=Edge)` is the type parameter of the method, with the constraint `where E : Edge`.

    /// <summary>
    /// Repoint edge endpoints through ``uuid_map``, dropping edges that collapse.
    ///
    /// Endpoints are rewritten in place. The returned list omits edges whose two
    /// endpoints were distinct before remapping and resolve to the same node —
    /// dedup can map an endpoint onto the node that is already the other endpoint.
    /// Extracted self-referencing edges (source already equals target) are kept.
    /// Use the return value rather than the list passed in.
    /// </summary>
    internal static List<E> ResolveEdgePointers<E>(List<E> edges, OrderedDictionary<string, string> uuidMap)
        where E : Edge
    {
        var surviving = new List<E>();
        foreach (var edge in edges)
        {
            var sourceUuid = edge.SourceNodeUuid;
            var targetUuid = edge.TargetNodeUuid;
            edge.SourceNodeUuid = Py.Get(uuidMap, sourceUuid, sourceUuid);
            edge.TargetNodeUuid = Py.Get(uuidMap, targetUuid, targetUuid);

            if (sourceUuid != targetUuid && edge.SourceNodeUuid == edge.TargetNodeUuid)
            {
                Log.DroppingSelfEdge(Logger, edge.SourceNodeUuid);
                continue;
            }

            surviving.Add(edge);
        }

        return surviving;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Canonical node {CanonicalUuid} missing during batch dedupe; falling back to {NodeUuid}")]
        internal static partial void CanonicalNodeMissing(ILogger logger, string canonicalUuid, string nodeUuid);

        [LoggerMessage(Level = LogLevel.Information, Message = "Dropping self-edge for node {SourceNodeUuid} (endpoints resolved to the same node)")]
        internal static partial void DroppingSelfEdge(ILogger logger, string sourceNodeUuid);
    }
}
