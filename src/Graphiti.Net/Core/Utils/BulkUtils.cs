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

using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.Ingestion;
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
    // TODO(port): _build_directed_uuid_map
    internal static OrderedDictionary<string, string> BuildDirectedUuidMap(List<(string, string)> pairs)
    {
        throw new NotSupportedException("TODO(port): _build_directed_uuid_map");
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
}

internal static partial class BulkUtils
{
    // TODO(port): retrieve_previous_episodes_bulk
    internal static Task<List<(EpisodicNode, List<EpisodicNode>)>> RetrievePreviousEpisodesBulkAsync(
        GraphDriver driver,
        List<EpisodicNode> episodes,
        long? maxCoroutines = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): retrieve_previous_episodes_bulk");
    }

    // PORT: no return annotation. The function returns None (TYPE_HINTS.md, R20).
    // TODO(port): add_nodes_and_edges_bulk
    internal static Task AddNodesAndEdgesBulkAsync(
        GraphDriver driver,
        List<EpisodicNode> episodicNodes,
        List<EpisodicEdge> episodicEdges,
        List<EntityNode> entityNodes,
        List<EntityEdge> entityEdges,
        EmbedderClient embedder,
        double? writeTimeout = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): add_nodes_and_edges_bulk");
    }

    // PORT: `func, /, *args, **kwargs` is one work function that closes over its arguments (R9), and the result is the
    // result of execute_write, a Task with no result (R9). The Neo4j branch calls `neo4j.unit_of_work` from core code.
    // Open item O6 (R19): the driver session contract has no write with a timeout yet. Open item O9 (R8): the budget
    // of asyncio.wait_for does not hold for a session call that takes no token.
    // TODO(port): execute_write_with_timeout
    internal static Task ExecuteWriteWithTimeoutAsync(
        GraphDriver graphDriver,
        double timeout,
        Func<Transaction, Task> func,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): execute_write_with_timeout");
    }

    // PORT: `list(set(node.labels + ['Entity']))` is `Entity` first, then the other labels in their first order (R14).
    // TODO(port): _entity_node_record
    internal static Task<OrderedDictionary<string, object?>> EntityNodeRecordAsync(
        EntityNode node,
        EmbedderClient embedder,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _entity_node_record");
    }

    // TODO(port): _entity_edge_record
    internal static Task<OrderedDictionary<string, object?>> EntityEdgeRecordAsync(
        EntityEdge edge,
        EmbedderClient embedder,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _entity_edge_record");
    }

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
    // operation names.
    // TODO(port): missing_ingestion_interface_operations
    internal static List<string> MissingIngestionInterfaceOperations(GraphDriver driver)
    {
        throw new NotSupportedException("TODO(port): missing_ingestion_interface_operations");
    }

    // PORT: no return annotation. The function returns None (TYPE_HINTS.md, R20).
    // TODO(port): update_entity_edges_tx
    internal static Task UpdateEntityEdgesTxAsync(
        Transaction tx,
        List<EntityEdge> entityEdges,
        EmbedderClient embedder,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): update_entity_edges_tx");
    }

    // TODO(port): update_entity_nodes_tx
    internal static Task UpdateEntityNodesTxAsync(
        Transaction tx,
        List<EntityNode> entityNodes,
        EmbedderClient embedder,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): update_entity_nodes_tx");
    }

    // PORT: `dict(episode)` of a pydantic model is a dict of its fields, and `edge.model_dump()` needs
    // EpisodicEdge.ModelDump (R7). The units of nodes and edges add those members.
    // TODO(port): add_nodes_and_edges_bulk_tx
    internal static Task AddNodesAndEdgesBulkTxAsync(
        Transaction tx,
        List<EpisodicNode> episodicNodes,
        List<EpisodicEdge> episodicEdges,
        List<EntityNode> entityNodes,
        List<EntityEdge> entityEdges,
        EmbedderClient embedder,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): add_nodes_and_edges_bulk_tx");
    }

    // TODO(port): extract_nodes_and_edges_bulk
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
        throw new NotSupportedException("TODO(port): extract_nodes_and_edges_bulk");
    }

    // PORT: the lazy import of combined_extraction is a normal reference (R19).
    // TODO(port): _extract_nodes_and_edges_bulk_combined
    internal static Task<(List<List<EntityNode>>, List<List<EntityEdge>>)> ExtractNodesAndEdgesBulkCombinedAsync(
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
        throw new NotSupportedException("TODO(port): _extract_nodes_and_edges_bulk_combined");
    }

    // TODO(port): _extract_nodes_and_edges_bulk_separate
    internal static Task<(List<List<EntityNode>>, List<List<EntityEdge>>)> ExtractNodesAndEdgesBulkSeparateAsync(
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
        throw new NotSupportedException("TODO(port): _extract_nodes_and_edges_bulk_separate");
    }

    // PORT: the nested coroutine resolve_nodes is a local function of the body, and each call is a lambda for
    // Helpers.SemaphoreGatherAsync (R8).
    // TODO(port): dedupe_nodes_bulk
    internal static Task<(OrderedDictionary<string, List<EntityNode>>, OrderedDictionary<string, string>)>
        DedupeNodesBulkAsync(
            GraphitiClients clients,
            List<List<EntityNode>> extractedNodes,
            List<(EpisodicNode, List<EpisodicNode>)> episodeTuples,
            OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
            OrderedDictionary<string, List<string>>? identityProperties = null,
            ResolutionTrace? trace = null,
            CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): dedupe_nodes_bulk");
    }

    // PORT: `_entities` and `_edge_type_map` lose the underscore (R2). np.dot is a plain loop in index order (R18).
    // TODO(port): dedupe_edges_bulk
    internal static Task<OrderedDictionary<string, List<EntityEdge>>> DedupeEdgesBulkAsync(
        GraphitiClients clients,
        List<List<EntityEdge>> extractedEdges,
        List<(EpisodicNode, List<EpisodicNode>)> episodeTuples,
        List<EntityNode> entities,
        OrderedDictionary<string, GraphTypeDescriptor> edgeTypes,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): dedupe_edges_bulk");
    }
}

// PORT: the parameters have no annotation (TYPE_HINTS.md: Unknown, R20). The one caller, compress_uuid_map, passes a
// set of UUID strings, which is a PySet<string> in the order of the pairs (R14). `self.parent` is read and written by
// key only (R14).
internal sealed class UnionFind
{
    // TODO(port): UnionFind.__init__
    internal UnionFind(PySet<string> elements)
    {
        throw new NotSupportedException("TODO(port): UnionFind.__init__");
    }

    internal Dictionary<string, string> Parent { get; set; }

    // TODO(port): UnionFind.find
    internal string Find(string x)
    {
        throw new NotSupportedException("TODO(port): UnionFind.find");
    }

    // PORT: upstream defect U18 (R21): the root with the smaller UUID wins, compared with Py.CompareStrings.
    // TODO(port): UnionFind.union
    internal void Union(string a, string b)
    {
        throw new NotSupportedException("TODO(port): UnionFind.union");
    }
}

internal static partial class BulkUtils
{
    // TODO(port): compress_uuid_map
    internal static OrderedDictionary<string, string> CompressUuidMap(List<(string, string)> duplicatePairs)
    {
        throw new NotSupportedException("TODO(port): compress_uuid_map");
    }

    // PORT: `E = TypeVar('E', bound=Edge)` is the type parameter of the method, with the constraint `where E : Edge`.
    // TODO(port): resolve_edge_pointers
    internal static List<E> ResolveEdgePointers<E>(List<E> edges, OrderedDictionary<string, string> uuidMap)
        where E : Edge
    {
        throw new NotSupportedException("TODO(port): resolve_edge_pointers");
    }
}
