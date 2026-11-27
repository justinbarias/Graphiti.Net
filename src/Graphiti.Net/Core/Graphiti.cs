// Ported from graphiti_core/graphiti.py
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

namespace Graphiti.Core;

using System.Diagnostics;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.Ingestion;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Namespaces;
using Graphiti.Core.Prompts;
using Graphiti.Core.Search;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: the module-level names of graphiti.py go to GraphitiModule (R2, decision D24).
// PORT: `load_dotenv()` is not ported (R17). The telemetry of `graphiti_core.telemetry` is not ported (decision D9).
// PORT: `dict[str, type[BaseModel]]` is an OrderedDictionary<string, GraphTypeDescriptor> (R15). `edge_type_map:
// dict[tuple[str, str], list[str]]` is an OrderedDictionary<(string, string), List<string>>. A `uuid_map:
// dict[str, str]` is an OrderedDictionary<string, string>, the type of `ChangeSet.uuid_map`.
// PORT: a union that Python does not name (Part 3).
// `saga: str | SagaNode | None` has two shapes: the name of a saga, or the saga node. Part 3 gives the form: an abstract
// record with one derived record for each case. graphiti.py, ingestion/phases.py, and ingestion/scheduler.py pass the
// value through. `_validate_saga_group` of this module is the first function that reads it. `| None` is `SagaOrName?`.
internal abstract record SagaOrName
{
    private SagaOrName()
    {
    }

    internal sealed record Name : SagaOrName
    {
        internal required string Value { get; init; }
    }

    internal sealed record Node : SagaOrName
    {
        internal required SagaNode Saga { get; init; }
    }
}

internal static class GraphitiModule
{
    // PORT: internal, because GraphitiCore logs through it (R5).
    internal static ILogger Logger => PortServices.Logger(typeof(GraphitiModule));

    // Episode text is packed under 100k characters. The assembled prompt adds the
    // existing summary (up to SAGA_SUMMARY_MAX_CHARS), template, and schema on top
    // of that, then shrinks until the full prompt is under 128 KiB. Request 8192
    // output tokens. Persist at most SAGA_SUMMARY_MAX_CHARS.
    internal const long SagaSummaryMaxEpisodeChars = 100_000;
    internal const long SagaSummaryMaxEpisodeBytes = SagaSummaryMaxEpisodeChars;
    internal const long SagaSummaryMaxPromptBytes = 128 * 1024;
    internal const long MaxSagaEpisodesForSummary = 20;
    internal const long SummaryRebuildBatchSize = 10;
    internal const long SagaSummaryMaxTokens = 8192;

    // PORT: internal, because GraphitiCore reads it (R2).
    internal const string SagaSummarySchemaWrapper = "\n\nRespond with a JSON object in the following format:\n\n";

    // PORT: `saga: str | SagaNode | None` is a `SagaOrName?` (Part 3, a union of different shapes).
    // TODO(port): _validate_saga_group
    internal static void ValidateSagaGroup(SagaOrName? saga, string groupId)
    {
        throw new NotSupportedException("TODO(port): _validate_saga_group");
    }

    // TODO(port): _validate_saga_scope
    internal static Task ValidateSagaScopeAsync(
        SagaOrName? saga,
        string groupId,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _validate_saga_scope");
    }

    // PORT: `messages: list`. Each caller passes the List<Message> of a prompt (R20).
    // TODO(port): _saga_summary_messages_utf8_bytes
    internal static long SagaSummaryMessagesUtf8Bytes(List<Message> messages)
    {
        throw new NotSupportedException("TODO(port): _saga_summary_messages_utf8_bytes");
    }

    // TODO(port): select_saga_summary_episodes
    internal static (List<(string, PyDateTime?, PyDateTime)>, long, bool) SelectSagaSummaryEpisodes(
        List<(string, PyDateTime?, PyDateTime)> episodesData,
        long maxEpisodeChars = SagaSummaryMaxEpisodeChars)
    {
        throw new NotSupportedException("TODO(port): select_saga_summary_episodes");
    }
}

internal sealed record AddEpisodeResults
{
    internal required EpisodicNode Episode { get; set; }

    internal required List<EpisodicEdge> EpisodicEdges { get; set; }

    internal required List<EntityNode> Nodes { get; set; }

    internal required List<EntityEdge> Edges { get; set; }

    internal required List<CommunityNode> Communities { get; set; }

    internal required List<CommunityEdge> CommunityEdges { get; set; }

    // TODO(port): AddEpisodeResults.Validate (R7)
    internal AddEpisodeResults Validate()
    {
        throw new NotSupportedException("TODO(port): AddEpisodeResults.Validate");
    }
}

internal sealed record AddBulkEpisodeResults
{
    internal required List<EpisodicNode> Episodes { get; set; }

    internal required List<EpisodicEdge> EpisodicEdges { get; set; }

    internal required List<EntityNode> Nodes { get; set; }

    internal required List<EntityEdge> Edges { get; set; }

    internal required List<CommunityNode> Communities { get; set; }

    internal required List<CommunityEdge> CommunityEdges { get; set; }

    // TODO(port): AddBulkEpisodeResults.Validate (R7)
    internal AddBulkEpisodeResults Validate()
    {
        throw new NotSupportedException("TODO(port): AddBulkEpisodeResults.Validate");
    }
}

internal sealed record AddTripletResults
{
    internal required List<EntityNode> Nodes { get; set; }

    internal required List<EntityEdge> Edges { get; set; }

    // TODO(port): AddTripletResults.Validate (R7)
    internal AddTripletResults Validate()
    {
        throw new NotSupportedException("TODO(port): AddTripletResults.Validate");
    }
}

// PORT: the class `Graphiti` of graphiti.py is GraphitiCore (R2, decision D24). It is internal (R3), and the public
// facade IGraphiti calls it.
// PORT: the private attribute `_concurrent_ingestor` (a ConcurrentIngestor?) is a field of the unit. A skeleton field
// that no member reads fails the build (CS0169). The attributes `nodes` and `edges` are the properties Nodes and Edges.
// Inside this class those names bind to the properties, so the static classes Graphiti.Core.Nodes and
// Graphiti.Core.Edges need a qualified name.
internal sealed class GraphitiCore
{
    // PORT: the four branches that build a default object (Neo4jDriver, OpenAIClient, OpenAIEmbedder,
    // OpenAIRerankerClient) throw ArgumentException (R1). `tracer: Tracer | None` is the OpenTelemetry tracer that
    // create_tracer takes, an ActivitySource. `prompt_library: ChatPromptLibrary | None` is an IPromptLibrary, the type
    // that validate_prompt_library and ensure_prompt_library_wrapped take, because a caller can pass the result of
    // create_prompt_library. The call of `_capture_initialization_telemetry` is not ported (decision D9).
    // TODO(port): Graphiti.__init__
    internal GraphitiCore(
        string? uri = null,
        string? user = null,
        string? password = null,
        LLMClient? llmClient = null,
        EmbedderClient? embedder = null,
        CrossEncoderClient? crossEncoder = null,
        bool storeRawEpisodeContent = true,
        GraphDriver? graphDriver = null,
        long? maxCoroutines = null,
        ActivitySource? tracer = null,
        string traceSpanPrefix = "graphiti",
        IPromptLibrary? promptLibrary = null,
        LLMRuntime? llmRuntime = null,
        ConcurrentIngestionConfig? concurrentIngestion = null)
    {
        throw new NotSupportedException("TODO(port): Graphiti.__init__");
    }

    internal GraphDriver Driver { get; set; }

    internal bool StoreRawEpisodeContent { get; set; }

    internal long? MaxCoroutines { get; set; }

    internal LLMRuntime? LlmRuntime { get; set; }

    internal LLMClient LlmClient { get; set; }

    internal IPromptLibrary? PromptLibrary { get; set; }

    internal EmbedderClient Embedder { get; set; }

    internal CrossEncoderClient CrossEncoder { get; set; }

    internal Tracer Tracer { get; set; }

    internal GraphitiClients Clients { get; set; }

    // PORT: `getattr(self, 'concurrent_ingestion', None)` reads this nullable property (R15).
    internal ConcurrentIngestionConfig? ConcurrentIngestion { get; set; }

    internal NodeNamespace Nodes { get; set; }

    internal EdgeNamespace Edges { get; set; }

    // PORT: `_capture_initialization_telemetry` calls the telemetry of decision D9, which is not ported, so it has no
    // member.

    // TODO(port): Graphiti.token_tracker
    internal TokenUsageTracker TokenTracker => throw new NotSupportedException("TODO(port): Graphiti.token_tracker");

    // PORT: `_get_provider_type` is a part of the telemetry of decision D9, which is not ported, so it has no member.
    // Its one caller is `_capture_initialization_telemetry`.

    // TODO(port): Graphiti.close
    internal Task CloseAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.close");
    }

    // TODO(port): Graphiti._concurrent_ingestion_enabled
    private bool ConcurrentIngestionEnabled(string groupId)
    {
        throw new NotSupportedException("TODO(port): Graphiti._concurrent_ingestion_enabled");
    }

    // TODO(port): Graphiti._get_concurrent_ingestor
    private ConcurrentIngestor GetConcurrentIngestor()
    {
        throw new NotSupportedException("TODO(port): Graphiti._get_concurrent_ingestor");
    }

    // PORT: internal, because an upstream test calls it (R2).
    // TODO(port): Graphiti._get_or_create_saga
    internal Task<SagaNode> GetOrCreateSagaAsync(
        string sagaName,
        string groupId,
        PyDateTime createdAt,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti._get_or_create_saga");
    }

    // PORT: internal, because ingestion/phases.py calls it (R2).
    // TODO(port): Graphiti._find_saga
    internal Task<SagaNode?> FindSagaAsync(
        string sagaName,
        string groupId,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti._find_saga");
    }

    // PORT: internal, because ingestion/phases.py calls it (R2).
    // TODO(port): Graphiti._saga_get_previous_episode_uuid
    internal Task<string?> SagaGetPreviousEpisodeUuidAsync(
        string sagaUuid,
        string currentEpisodeUuid,
        string groupId,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti._saga_get_previous_episode_uuid");
    }

    // TODO(port): Graphiti._saga_get_episode_contents
    private Task<List<(string, PyDateTime?, PyDateTime)>?> SagaGetEpisodeContentsAsync(
        string sagaUuid,
        PyDateTime? since = null,
        long limit = 200,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti._saga_get_episode_contents");
    }

    // TODO(port): Graphiti.summarize_saga
    internal Task<SagaNode> SummarizeSagaAsync(
        string sagaId,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.summarize_saga");
    }

    // TODO(port): Graphiti.build_indices_and_constraints
    internal Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.build_indices_and_constraints");
    }

    // PORT: `episode: EpisodicNode | list[EpisodicNode]` is the `EpisodeOrEpisodes` of node_operations (Part 3).
    // TODO(port): Graphiti._extract_and_resolve_nodes
    private Task<(List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>, OrderedDictionary<string, List<long>>)> ExtractAndResolveNodesAsync(
        EpisodeOrEpisodes episode,
        List<EpisodicNode> previousEpisodes,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        List<string>? excludedEntityTypes,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti._extract_and_resolve_nodes");
    }

    // TODO(port): Graphiti._merge_materialized_endpoint_nodes
    private Task MergeMaterializedEndpointNodesAsync(
        List<EntityNode> materializedNodes,
        EpisodicNode episode,
        List<EpisodicNode> previousEpisodes,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        List<EntityNode> nodes,
        OrderedDictionary<string, string> uuidMap,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        GraphitiClients? clients = null,
        ResolutionTrace? trace = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti._merge_materialized_endpoint_nodes");
    }

    // PORT: internal, because ingestion/phases.py calls it (R2). `episode` is the
    // `EpisodeOrEpisodes` of node_operations.
    // TODO(port): Graphiti._extract_and_resolve_edges
    internal Task<(List<EntityEdge>, List<EntityEdge>, List<EntityEdge>)> ExtractAndResolveEdgesAsync(
        EpisodeOrEpisodes episode,
        List<EntityNode> extractedNodes,
        List<EpisodicNode> previousEpisodes,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        string groupId,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes,
        List<EntityNode> nodes,
        OrderedDictionary<string, string> uuidMap,
        bool strictEdgeTypes = false,
        string? customExtractionInstructions = null,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        GraphitiClients? clients = null,
        ResolutionTrace? trace = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti._extract_and_resolve_edges");
    }

    // PORT: `episode` is an `EpisodeOrEpisodes`, and `saga` is a `SagaOrName?` (Part 3).
    // TODO(port): Graphiti._process_episode_data
    private Task<(List<EpisodicEdge>, EpisodicNode)> ProcessEpisodeDataAsync(
        EpisodeOrEpisodes episode,
        List<EntityNode> nodes,
        List<EntityEdge> entityEdges,
        PyDateTime now,
        string groupId,
        SagaOrName? saga = null,
        string? sagaPreviousEpisodeUuid = null,
        OrderedDictionary<string, List<long>>? nodeEpisodeIndexMap = null,
        GraphitiClients? clients = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti._process_episode_data");
    }

    // PORT: internal, because ingestion/phases.py calls it (R2).
    // TODO(port): Graphiti._extract_and_dedupe_nodes_bulk
    internal Task<(OrderedDictionary<string, List<EntityNode>>, OrderedDictionary<string, string>, List<List<EntityEdge>>)> ExtractAndDedupeNodesBulkAsync(
        List<(EpisodicNode, List<EpisodicNode>)> episodeContext,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        List<string>? excludedEntityTypes,
        bool strictEdgeTypes = false,
        string? customExtractionInstructions = null,
        bool useCombinedExtraction = true,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        GraphitiClients? clients = null,
        ResolutionTrace? trace = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti._extract_and_dedupe_nodes_bulk");
    }

    // PORT: internal, because ingestion/phases.py calls it (R2).
    // TODO(port): Graphiti._resolve_nodes_and_edges_bulk
    internal Task<(List<EntityNode>, List<EntityEdge>, List<EntityEdge>, OrderedDictionary<string, string>)> ResolveNodesAndEdgesBulkAsync(
        OrderedDictionary<string, List<EntityNode>> nodesByEpisode,
        OrderedDictionary<string, List<EntityEdge>> edgesByEpisode,
        List<(EpisodicNode, List<EpisodicNode>)> episodeContext,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes,
        OrderedDictionary<(string, string), List<string>> edgeTypeMap,
        List<EpisodicNode> episodes,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        GraphitiClients? clients = null,
        bool hydrate = true,
        ResolutionTrace? trace = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti._resolve_nodes_and_edges_bulk");
    }

    // PORT: @handle_multiple_group_ids (R16). The body is RetrieveEpisodesCoreAsync, and this method calls
    // Decorators.HandleMultipleGroupIdsAsync with it. The default of `last_n` is EPISODE_WINDOW_LEN of
    // utils/maintenance/graph_data_operations.py.
    // TODO(port): Graphiti.retrieve_episodes
    internal Task<List<EpisodicNode>> RetrieveEpisodesAsync(
        PyDateTime referenceTime,
        long lastN = GraphDataOperations.EpisodeWindowLen,
        List<string>? groupIds = null,
        EpisodeType? source = null,
        GraphDriver? driver = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.retrieve_episodes");
    }

    // PORT: the body of the decorated retrieve_episodes (R16).
    // TODO(port): Graphiti.retrieve_episodes
    private Task<List<EpisodicNode>> RetrieveEpisodesCoreAsync(
        PyDateTime referenceTime,
        long lastN = GraphDataOperations.EpisodeWindowLen,
        List<string>? groupIds = null,
        EpisodeType? source = null,
        GraphDriver? driver = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.retrieve_episodes");
    }

    // PORT: internal, because ingestion/phases.py calls it (R2).
    // TODO(port): Graphiti._resolve_request_scope
    internal (string, GraphDriver, GraphitiClients) ResolveRequestScope(string? groupId)
    {
        throw new NotSupportedException("TODO(port): Graphiti._resolve_request_scope");
    }

    // PORT: `saga` is a `SagaOrName?` (Part 3).
    // TODO(port): Graphiti.add_episode
    internal Task<AddEpisodeResults> AddEpisodeAsync(
        string name,
        string episodeBody,
        string sourceDescription,
        PyDateTime referenceTime,
        EpisodeType source = EpisodeType.Message,
        string? groupId = null,
        string? uuid = null,
        bool updateCommunities = false,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        List<string>? excludedEntityTypes = null,
        List<string>? previousEpisodeUuids = null,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes = null,
        OrderedDictionary<(string, string), List<string>>? edgeTypeMap = null,
        string? customExtractionInstructions = null,
        SagaOrName? saga = null,
        string? sagaPreviousEpisodeUuid = null,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.add_episode");
    }

    // TODO(port): Graphiti.add_episode_bulk
    internal Task<AddBulkEpisodeResults> AddEpisodeBulkAsync(
        List<RawEpisode> bulkEpisodes,
        string? groupId = null,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        List<string>? excludedEntityTypes = null,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes = null,
        OrderedDictionary<(string, string), List<string>>? edgeTypeMap = null,
        bool strictEdgeTypes = false,
        string? customExtractionInstructions = null,
        SagaOrName? saga = null,
        bool useCombinedExtraction = true,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.add_episode_bulk");
    }

    // PORT: @handle_multiple_group_ids (R16). The tuple overload of the wrapper merges the two lists.
    // TODO(port): Graphiti.build_communities
    internal Task<(List<CommunityNode> First, List<CommunityEdge> Second)> BuildCommunitiesAsync(
        List<string>? groupIds = null,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.build_communities");
    }

    // PORT: the body of the decorated build_communities (R16).
    // TODO(port): Graphiti.build_communities
    private Task<(List<CommunityNode> First, List<CommunityEdge> Second)> BuildCommunitiesCoreAsync(
        List<string>? groupIds = null,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.build_communities");
    }

    // PORT: @handle_multiple_group_ids (R16). `num_results` has no annotation. TYPE_HINTS.md gives int.
    // TODO(port): Graphiti.search
    internal Task<List<EntityEdge>> SearchAsync(
        string query,
        string? centerNodeUuid = null,
        List<string>? groupIds = null,
        long numResults = SearchConfigModule.DefaultSearchLimit,
        SearchFilters? searchFilter = null,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.search");
    }

    // PORT: the body of the decorated search (R16).
    // TODO(port): Graphiti.search
    private Task<List<EntityEdge>> SearchCoreAsync(
        string query,
        string? centerNodeUuid = null,
        List<string>? groupIds = null,
        long numResults = SearchConfigModule.DefaultSearchLimit,
        SearchFilters? searchFilter = null,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.search");
    }

    // PORT: `_search` and `search` give the same C# name, so the private one has the prefix Private (R2).
    // TODO(port): Graphiti._search
    private Task<SearchResults> PrivateSearchAsync(
        string query,
        SearchConfig config,
        List<string>? groupIds = null,
        string? centerNodeUuid = null,
        List<string>? bfsOriginNodeUuids = null,
        SearchFilters? searchFilter = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti._search");
    }

    // PORT: @handle_multiple_group_ids (R16). `search_` gives the C# name of `search`, so it has the
    // name of the public facade (R2, and the public surface design, L10). The default of `config` is
    // the shared recipe object COMBINED_HYBRID_SEARCH_CROSS_ENCODER, which cannot be a C# default value: null stands
    // for SearchConfigRecipes.CombinedHybridSearchCrossEncoder, the same object (R17, R21 U8).
    // TODO(port): Graphiti.search_
    internal Task<SearchResults> AdvancedSearchAsync(
        string query,
        SearchConfig? config = null,
        List<string>? groupIds = null,
        string? centerNodeUuid = null,
        List<string>? bfsOriginNodeUuids = null,
        SearchFilters? searchFilter = null,
        GraphDriver? driver = null,
        List<double>? queryVector = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.search_");
    }

    // PORT: the body of the decorated search_ (R16).
    // TODO(port): Graphiti.search_
    private Task<SearchResults> AdvancedSearchCoreAsync(
        string query,
        SearchConfig? config = null,
        List<string>? groupIds = null,
        string? centerNodeUuid = null,
        List<string>? bfsOriginNodeUuids = null,
        SearchFilters? searchFilter = null,
        GraphDriver? driver = null,
        List<double>? queryVector = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.search_");
    }

    // TODO(port): Graphiti.get_nodes_and_edges_by_episode
    internal Task<SearchResults> GetNodesAndEdgesByEpisodeAsync(
        List<string> episodeUuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.get_nodes_and_edges_by_episode");
    }

    // TODO(port): Graphiti.add_triplet
    internal Task<AddTripletResults> AddTripletAsync(
        EntityNode sourceNode,
        EntityEdge edge,
        EntityNode targetNode,
        EpisodicNode? episode = null,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.add_triplet");
    }

    // TODO(port): Graphiti.remove_episode
    internal Task RemoveEpisodeAsync(
        string episodeUuid,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): Graphiti.remove_episode");
    }
}
