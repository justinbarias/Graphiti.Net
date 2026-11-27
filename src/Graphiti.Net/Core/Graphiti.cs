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
using Graphiti.Core.Utils.OntologyUtils;
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

internal static partial class GraphitiModule
{
    // PORT: internal, because GraphitiCore logs through it (R5).
    internal static ILogger Logger => PortServices.Logger(typeof(GraphitiModule));

    // Episode text is packed under 100k characters. The assembled prompt adds the
    // existing summary (up to SAGA_SUMMARY_MAX_CHARS), template, and schema on top
    // of that, then shrinks until the full prompt is under 128 KiB. Request 8192
    // output tokens. Persist at most SAGA_SUMMARY_MAX_CHARS.
    // PORT: tests/test_saga_summary_budget.py replaces SAGA_SUMMARY_MAX_EPISODE_CHARS and SAGA_SUMMARY_MAX_PROMPT_BYTES
    // with monkeypatch.setattr, and summarize_saga reads the module names at each call, so both are a PyPatch (R17).
    internal static readonly PyPatch<long> SagaSummaryMaxEpisodeChars = new(100_000);

    // PORT: Python binds the value of SAGA_SUMMARY_MAX_EPISODE_CHARS at import, so this name keeps 100_000.
    internal const long SagaSummaryMaxEpisodeBytes = 100_000;
    internal static readonly PyPatch<long> SagaSummaryMaxPromptBytes = new(128 * 1024);
    internal const long MaxSagaEpisodesForSummary = 20;
    internal const long SummaryRebuildBatchSize = 10;
    internal const long SagaSummaryMaxTokens = 8192;

    // PORT: internal, because GraphitiCore reads it (R2).
    internal const string SagaSummarySchemaWrapper = "\n\nRespond with a JSON object in the following format:\n\n";

    // PORT: `saga: str | SagaNode | None` is a `SagaOrName?` (Part 3, a union of different shapes).
    internal static void ValidateSagaGroup(SagaOrName? saga, string groupId)
    {
        if (saga is SagaOrName.Node node && node.Saga.GroupId != groupId)
        {
            throw new ArgumentException("saga " + node.Saga.Uuid + " does not belong to group " + groupId);
        }
    }

    internal static async Task ValidateSagaScopeAsync(
        SagaOrName? saga,
        string groupId,
        GraphDriver driver,
        CancellationToken cancellationToken = default)
    {
        ValidateSagaGroup(saga, groupId);
        if (saga is not SagaOrName.Node node)
        {
            return;
        }

        SagaNode storedSaga;
        try
        {
            storedSaga = await SagaNode.GetByUuidAsync(driver, node.Saga.Uuid, cancellationToken);
        }
        catch (NodeNotFoundException)
        {
            return;
        }

        if (storedSaga.GroupId != groupId)
        {
            throw new NodeNotFoundException(node.Saga.Uuid);
        }
    }

    // PORT: `messages: list`. Each caller passes the List<Message> of a prompt (R20).
    internal static long SagaSummaryMessagesUtf8Bytes(List<Message> messages)
    {
        long total = 0;
        foreach (var message in messages)
        {
            total += PyText.EncodeUtf8(message.Content).Length;
        }

        return total;
    }

    /// <summary>
    /// Select a contiguous ingestion-time episode batch using a character budget.
    ///
    /// Packs oldest-first so last_summarized_at can advance through a backlog.
    /// When the oldest episode alone exceeds the budget, truncate it rather than
    /// skip, matching Go entity-summary behavior for a single oversized episode.
    /// </summary>
    // PORT: Python binds the default when it defines the function: the value of SAGA_SUMMARY_MAX_EPISODE_CHARS at
    // import, 100_000. A replaced value of the PyPatch does not change it, as in Python.
    internal static (List<(string, PyDateTime?, PyDateTime)>, long, bool) SelectSagaSummaryEpisodes(
        List<(string, PyDateTime?, PyDateTime)> episodesData,
        long maxEpisodeChars = 100_000)
    {
        List<(string, PyDateTime?, PyDateTime)> selected = [];
        long selectedChars = 0;
        var dropped = false;

        foreach (var (content, validAt, createdAt) in episodesData)
        {
            var contentChars = Py.Len(content);
            if (selectedChars + contentChars <= maxEpisodeChars)
            {
                selected.Add((content, validAt, createdAt));
                selectedChars += contentChars;
                continue;
            }

            if (selected.Count == 0)
            {
                var truncated = Py.Slice(content, null, maxEpisodeChars);
                if (truncated.Length > 0)
                {
                    selected.Add((truncated, validAt, createdAt));
                    selectedChars = Py.Len(truncated);
                }

                dropped = true;
                break;
            }

            // Preserve chronological coherence for incremental summaries. Once the
            // next episode does not fit, later episodes are deferred to a future run.
            dropped = true;
            break;
        }

        long selectedBytes = 0;
        foreach (var (content, _, _) in selected)
        {
            selectedBytes += PyText.EncodeUtf8(content).Length;
        }

        return (selected, selectedBytes, dropped);
    }

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "No new episodes found for saga {SagaId}, skipping summary")]
        internal static partial void NoNewEpisodesForSaga(ILogger logger, string sagaId);

        [LoggerMessage(Level = LogLevel.Information, Message = "No saga episode content fit the summary byte budget for saga {SagaId}")]
        internal static partial void NoSagaEpisodeContentFit(ILogger logger, string sagaId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Saga summary episode payload constrained by byte budget")]
        internal static partial void SagaSummaryPayloadConstrained(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Updated summary for saga {SagaId}")]
        internal static partial void UpdatedSummaryForSaga(ILogger logger, string sagaId);

        [LoggerMessage(Level = LogLevel.Information, Message = "Completed add_episode in {Milliseconds} ms")]
        internal static partial void CompletedAddEpisode(ILogger logger, string milliseconds);

        [LoggerMessage(Level = LogLevel.Information, Message = "Completed add_episode_bulk in {Milliseconds} ms")]
        internal static partial void CompletedAddEpisodeBulk(ILogger logger, string milliseconds);

        [LoggerMessage(
            Level = LogLevel.Information,
            Message = "Edge UUID {OldUuid} already exists with different source/target nodes. Generated new UUID {NewUuid} to avoid overwriting.")]
        internal static partial void EdgeUuidAlreadyExists(ILogger logger, string oldUuid, string newUuid);

        [LoggerMessage(
            Level = LogLevel.Error,
            Message = "Failed to rebuild the summary of entity {NodeUuid} after removal of episode {EpisodeUuid}")]
        internal static partial void FailedToRebuildSummary(ILogger logger, Exception exception, string nodeUuid, string episodeUuid);
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

    internal AddEpisodeResults Validate() => this;

    // PORT: add_episode builds the model from `communities` and `community_edges`, which hold a tuple after the
    // two-name unpacking of upstream defect U14. A value of another shape is open data, so the model has the reader of
    // R7, and pydantic's error comes from it.
    internal static AddEpisodeResults ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("AddEpisodeResults", data);
        var episode = f.Required("episode", v => Pydantic.Model(v, "EpisodicNode", d => EpisodicNode.ModelValidate(d)));
        var episodicEdges = f.Required(
            "episodic_edges",
            v => Pydantic.List(v, x => Pydantic.Model(x, "EpisodicEdge", d => EpisodicEdge.ModelValidate(d))));
        var nodes = f.Required("nodes", v => Pydantic.List(v, x => Pydantic.Model(x, "EntityNode", d => EntityNode.ModelValidate(d))));
        var edges = f.Required("edges", v => Pydantic.List(v, x => Pydantic.Model(x, "EntityEdge", d => EntityEdge.ModelValidate(d))));
        var communities = f.Required(
            "communities",
            v => Pydantic.List(v, x => Pydantic.Model(x, "CommunityNode", d => CommunityNode.ModelValidate(d))));
        var communityEdges = f.Required(
            "community_edges",
            v => Pydantic.List(v, x => Pydantic.Model(x, "CommunityEdge", d => CommunityEdge.ModelValidate(d))));
        f.ThrowIfAny();
        return new AddEpisodeResults
        {
            Episode = episode.Value,
            EpisodicEdges = episodicEdges.Value,
            Nodes = nodes.Value,
            Edges = edges.Value,
            Communities = communities.Value,
            CommunityEdges = communityEdges.Value,
        }.Validate();
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

    internal AddBulkEpisodeResults Validate() => this;
}

internal sealed record AddTripletResults
{
    internal required List<EntityNode> Nodes { get; set; }

    internal required List<EntityEdge> Edges { get; set; }

    internal AddTripletResults Validate() => this;
}

// PORT: the class `Graphiti` of graphiti.py is GraphitiCore (R2, decision D24). It is internal (R3), and the public
// facade IGraphiti calls it.
// PORT: the attributes `nodes` and `edges` are the properties Nodes and Edges. Inside this class those names bind to the
// properties, so the static classes Graphiti.Core.Nodes and Graphiti.Core.Edges need a qualified name.
internal sealed class GraphitiCore
{
    // PORT: the private attribute `_concurrent_ingestor`.
    private ConcurrentIngestor? concurrentIngestor;

    /// <summary>
    /// Initialize a Graphiti instance.
    ///
    /// This constructor sets up a connection to a graph database and initializes
    /// the LLM client for natural language processing tasks.
    ///
    /// Parameters
    /// ----------
    /// uri : str
    ///     The URI of the Neo4j database.
    /// user : str
    ///     The username for authenticating with the Neo4j database.
    /// password : str
    ///     The password for authenticating with the Neo4j database.
    /// llm_client : LLMClient | None, optional
    ///     An instance of LLMClient for natural language processing tasks.
    ///     If not provided, a default OpenAIClient will be initialized.
    ///     Cannot be combined with ``llm_runtime``.
    /// embedder : EmbedderClient | None, optional
    ///     An instance of EmbedderClient for embedding tasks.
    ///     If not provided, a default OpenAIEmbedder will be initialized.
    /// cross_encoder : CrossEncoderClient | None, optional
    ///     An instance of CrossEncoderClient for reranking tasks.
    ///     If not provided, a default OpenAIRerankerClient will be initialized.
    /// store_raw_episode_content : bool, optional
    ///     Whether to store the raw content of episodes. Defaults to True.
    /// graph_driver : GraphDriver | None, optional
    ///     An instance of GraphDriver for database operations.
    ///     If not provided, a default Neo4jDriver will be initialized.
    /// max_coroutines : int | None, optional
    ///     The maximum number of concurrent operations allowed at each
    ///     `semaphore_gather` call site across the Graphiti ingestion and search
    ///     paths. When set, this value is propagated to all internal
    ///     `semaphore_gather` calls via `GraphitiClients.max_coroutines`, making
    ///     this the recommended way to tune concurrency on a per-instance basis.
    ///     If not set, the value falls back to the `SEMAPHORE_LIMIT` environment
    ///     variable (if defined) or the built-in default
    ///     (`DEFAULT_SEMAPHORE_LIMIT`).
    ///
    ///     Note: this is a per-`semaphore_gather` cap, not a global cap on
    ///     in-flight coroutines. Each `semaphore_gather` call allocates its own
    ///     `asyncio.Semaphore`, so when a parent gather awaits child coroutines
    ///     that themselves contain a gather, the inner gathers each bound at
    ///     `max_coroutines` independently. For the search path that means the
    ///     inner per-scope dispatches (`edge_search`, `node_search`,
    ///     `community_search`) can together admit up to ~`scopes × max_coroutines`
    ///     in-flight tasks, where `scopes` is the number of search scopes
    ///     configured. Tune `max_coroutines` with that in mind when sizing for
    ///     tight LLM rate-limits.
    ///
    ///     Carve-out: when a `GraphDriver` implements
    ///     `graph_operations_interface.get_community_clusters`, that path is
    ///     serial inside the driver and does not honour `max_coroutines`.
    ///     Community-build concurrency outside the cluster lookup (LLM
    ///     summarization fan-out) is bounded as documented above.
    /// tracer : Tracer | None, optional
    ///     An OpenTelemetry tracer instance for distributed tracing. If not provided, tracing is disabled (no-op).
    /// trace_span_prefix : str, optional
    ///     Prefix to prepend to all span names. Defaults to 'graphiti'.
    /// prompt_library : ChatPromptLibrary | None, optional
    ///     An instance-scoped prompt library used for all LLM prompt construction.
    ///     If not provided, Graphiti uses its legacy module-level prompt functions.
    ///     ``self.prompt_library`` and ``self.clients.prompt_library`` remain ``None`` by default.
    ///     For partial customization, compose overrides with
    ///     ``create_prompt_library(overrides)`` and pass the result here.
    ///     Override callables must return ``ChatPrompt``.
    ///     Cannot be combined with ``llm_runtime``.
    /// llm_runtime : LLMRuntime | None, optional
    ///     An opt-in runtime that routes prompts across one or more
    ///     ``LLMTransport`` instances. It requires a default ``LLMModel``.
    ///     The runtime owns prompt selection and model routing. It has no
    ///     ``small_id`` field. ``model_size`` does not select a smaller model
    ///     on the runtime path. Route lightweight prompts to a smaller model.
    ///     GLiNER2 does not support per-prompt routing. Cannot be combined
    ///     with ``llm_client`` or ``prompt_library``.
    /// concurrent_ingestion : ConcurrentIngestionConfig | None, optional
    ///     The configuration of concurrent ingestion for one graph. With a value,
    ///     ``add_episode`` and ``add_episode_bulk`` run the extraction and the
    ///     resolution of many units of one ``group_id`` at the same time, and they
    ///     commit the units in sequence order. With the default ``None``, the two
    ///     methods run serial ingestion.
    ///
    /// Returns
    /// -------
    /// None
    ///
    /// Notes
    /// -----
    /// This method establishes a connection to a graph database (Neo4j by default) using the provided
    /// credentials. It also sets up the LLM client, either using the provided client
    /// or by creating a default OpenAIClient.
    ///
    /// The default database name is defined during the driver’s construction. If a different database name
    /// is required, it should be specified in the URI or set separately after
    /// initialization.
    ///
    /// The OpenAI API key is expected to be set in the environment variables.
    /// Make sure to set the OPENAI_API_KEY environment variable before initializing
    /// Graphiti if you're using the default OpenAIClient.
    /// </summary>
    // PORT: the four branches that build a default object (Neo4jDriver, OpenAIClient, OpenAIEmbedder,
    // OpenAIRerankerClient) throw ArgumentException (R1). `tracer: Tracer | None` is the OpenTelemetry tracer that
    // create_tracer takes, an ActivitySource. `prompt_library: ChatPromptLibrary | None` is an IPromptLibrary, the type
    // that validate_prompt_library and ensure_prompt_library_wrapped take, because a caller can pass the result of
    // create_prompt_library. The call of `_capture_initialization_telemetry` is not ported (decision D9).
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
        // PORT: `user` and `password` go only to the Neo4jDriver of the default branch, which is not ported (R1).
        _ = user;
        _ = password;

        if (llmRuntime is not null)
        {
            List<string> extras = [];
            foreach (var (name, value) in new (string, object?)[] { ("llm_client", llmClient), ("prompt_library", promptLibrary) })
            {
                if (value is not null)
                {
                    extras.Add(name);
                }
            }

            if (extras.Count > 0)
            {
                throw new ArgumentException("llm_runtime cannot be combined with: " + string.Join(", ", extras) + ".");
            }
        }

        // PORT: `if graph_driver:`. A GraphDriver has no __bool__ or __len__, so only None is false. The same holds for
        // the LLM client, the embedder, and the cross-encoder below.
        if (graphDriver is not null)
        {
            Driver = graphDriver;
        }
        else
        {
            if (uri is null)
            {
                throw new ArgumentException("uri must be provided when graph_driver is None");
            }

            throw new ArgumentException("graph_driver must be provided"); // PORT: D3, D28
        }

        StoreRawEpisodeContent = storeRawEpisodeContent;
        MaxCoroutines = maxCoroutines;

        LlmRuntime = llmRuntime;
        if (llmRuntime is not null)
        {
            LlmClient = llmRuntime.Client;
            PromptLibrary = llmRuntime.Library;
        }
        else
        {
            if (llmClient is not null)
            {
                LlmClient = llmClient;
            }
            else
            {
                throw new ArgumentException("llm_client must be provided"); // PORT: D3, D28
            }

            if (promptLibrary is not null)
            {
                Lib.ValidatePromptLibrary(promptLibrary);
                PromptLibrary = Lib.EnsurePromptLibraryWrapped(promptLibrary);
            }
            else
            {
                PromptLibrary = null;
            }
        }

        if (embedder is not null)
        {
            Embedder = embedder;
        }
        else
        {
            throw new ArgumentException("embedder must be provided"); // PORT: D3, D28
        }

        if (crossEncoder is not null)
        {
            CrossEncoder = crossEncoder;
        }
        else
        {
            throw new ArgumentException("cross_encoder must be provided"); // PORT: D3, D28
        }

        // Initialize tracer
        Tracer = TracerModule.CreateTracer(tracer, traceSpanPrefix);

        // Set tracer on clients
        if (LlmRuntime is not null)
        {
            LlmRuntime.SetTracer(Tracer);
        }
        else
        {
            LlmClient.SetTracer(Tracer);
        }

        Clients = new GraphitiClients
        {
            Driver = Driver,
            LlmClient = LlmClient,
            Embedder = Embedder,
            CrossEncoder = CrossEncoder,
            Tracer = Tracer,
            PromptLibrary = PromptLibrary,
            LlmRuntime = LlmRuntime,
            MaxCoroutines = MaxCoroutines,
        };

        // Concurrent ingestion is off when there is no configuration. The scheduler
        // and the state store are made on the first call that uses the mode.
        ConcurrentIngestion = concurrentIngestion;
        concurrentIngestor = null;

        // Initialize namespace API (graphiti.nodes.entity.save(), etc.)
        Nodes = new NodeNamespace(Driver, Embedder);
        Edges = new EdgeNamespace(Driver, Embedder);

        // Capture telemetry event
        // PORT: `self._capture_initialization_telemetry()` is not ported (decision D9).
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

    /// <summary>
    /// Access the LLM client's token usage tracker.
    ///
    /// Returns the TokenUsageTracker from the LLM client, which can be used to:
    /// - Get token usage by prompt type: tracker.get_usage()
    /// - Get total usage: tracker.get_total_usage()
    /// - Print a formatted summary: tracker.print_summary()
    /// - Reset tracking: tracker.reset()
    /// </summary>
    internal TokenUsageTracker TokenTracker => LlmClient.TokenTracker;

    // PORT: `_get_provider_type` is a part of the telemetry of decision D9, which is not ported, so it has no member.
    // Its one caller is `_capture_initialization_telemetry`.

    /// <summary>
    /// Close the connection to the Neo4j database.
    ///
    /// This method safely closes the driver connection to the Neo4j database.
    /// It should be called when the Graphiti instance is no longer needed or
    /// when the application is shutting down.
    ///
    /// Parameters
    /// ----------
    /// self
    ///
    /// Returns
    /// -------
    /// None
    ///
    /// Notes
    /// -----
    /// It's important to close the driver connection to release system resources
    /// and ensure that all pending transactions are completed or rolled back.
    /// This method should be called as part of a cleanup process, potentially
    /// in a context manager or a shutdown hook.
    ///
    /// Example:
    ///     graphiti = Graphiti(uri, user, password)
    ///     try:
    ///         # Use graphiti...
    ///     finally:
    ///         graphiti.close()
    /// </summary>
    internal async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        var concurrentIngestion = ConcurrentIngestion;
        if (concurrentIngestion is not null && concurrentIngestion.StateStore is not null)
        {
            await concurrentIngestion.StateStore.CloseAsync(cancellationToken);
        }

        await Driver.CloseAsync(cancellationToken);
    }

    /// <summary>Return True when a call for the graph must use concurrent ingestion.</summary>
    // PORT: internal, because the recording test calls it (R2).
    internal bool ConcurrentIngestionEnabled(string groupId)
    {
        // An object that a test made with no call of `__init__` has no configuration.
        var config = ConcurrentIngestion;
        if (config is null)
        {
            return false;
        }

        return config.EnabledForGroup is null || config.EnabledForGroup(groupId);
    }

    private ConcurrentIngestor GetConcurrentIngestor()
    {
        var ingestor = concurrentIngestor;
        if (ingestor is null)
        {
            Debug.Assert(ConcurrentIngestion is not null, "concurrent_ingestion is not None");
            ingestor = new ConcurrentIngestor(this, ConcurrentIngestion);
            concurrentIngestor = ingestor;
        }

        return ingestor;
    }

    /// <summary>
    /// Get an existing saga by name or create a new one.
    ///
    /// Parameters
    /// ----------
    /// saga_name : str
    ///     The name of the saga.
    /// group_id : str
    ///     The group id for the saga.
    /// created_at : datetime
    ///     Timestamp to stamp on a newly created saga. Callers should pass the
    ///     originating episode's reference time (``valid_at``) rather than the
    ///     current wall-clock time so the saga's ``created_at`` reflects the
    ///     episode it was minted from.
    ///
    /// Returns
    /// -------
    /// SagaNode
    ///     The existing or newly created saga node.
    /// </summary>
    // PORT: internal, because an upstream test calls it (R2).
    internal async Task<SagaNode> GetOrCreateSagaAsync(
        string sagaName,
        string groupId,
        PyDateTime createdAt,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        // PORT: `driver or self.driver`. A GraphDriver has no __bool__ or __len__, so only None is false.
        driver ??= Driver;

        var existing = await FindSagaAsync(sagaName, groupId, driver: driver, cancellationToken: cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var saga = new SagaNode { Name = sagaName, GroupId = groupId, CreatedAt = createdAt }.Validate();
        await saga.SaveAsync(driver, cancellationToken);
        return saga;
    }

    /// <summary>Return the saga with the name, or None. The method writes nothing.</summary>
    // PORT: internal, because ingestion/phases.py calls it (R2).
    internal async Task<SagaNode?> FindSagaAsync(
        string sagaName,
        string groupId,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        driver ??= Driver;

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (s:Saga {name: $name, group_id: $group_id})
            RETURN s.uuid AS uuid, s.name AS name, s.group_id AS group_id, s.created_at AS created_at,
                   s.first_episode_uuid AS first_episode_uuid, s.last_episode_uuid AS last_episode_uuid
""" + "\n            ",
            new OrderedDictionary<string, object?>
            {
                ["name"] = sagaName,
                ["group_id"] = groupId,
                ["routing_"] = "r",
            },
            cancellationToken);

        if (records.Count > 0)
        {
            var record = records[0];

            // PORT: the values are open data, so the model reads them with ModelValidate (R7).
            return SagaNode.ModelValidate(new OrderedDictionary<string, object?>
            {
                ["uuid"] = record["uuid"],
                ["name"] = record["name"],
                ["group_id"] = record["group_id"],
                ["created_at"] = Helpers.ParseDbDate(record["created_at"]),
                ["first_episode_uuid"] = record["first_episode_uuid"],
                ["last_episode_uuid"] = record["last_episode_uuid"],
            });
        }

        return null;
    }

    /// <summary>Find the most recent episode UUID in a saga, excluding the current one.</summary>
    // PORT: internal, because ingestion/phases.py calls it (R2).
    internal async Task<string?> SagaGetPreviousEpisodeUuidAsync(
        string sagaUuid,
        string currentEpisodeUuid,
        string groupId,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        driver ??= Driver;

        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                var previousEpisodeUuid = await driver.GraphOperationsInterface.SagaGetPreviousEpisodeUuidAsync(
                    driver, sagaUuid, currentEpisodeUuid, cancellationToken);
                if (previousEpisodeUuid is not null)
                {
                    await EpisodicNode.GetByUuidAsync(driver, previousEpisodeUuid, groupId: groupId, cancellationToken: cancellationToken);
                }

                return previousEpisodeUuid;
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        var (records, _, _) = await driver.ExecuteQueryAsync(
            "\n" + """
            MATCH (s:Saga {uuid: $saga_uuid, group_id: $group_id})
                  -[:HAS_EPISODE]->(e:Episodic {group_id: $group_id})
            WHERE e.uuid <> $current_episode_uuid
            RETURN e.uuid AS uuid
            ORDER BY e.valid_at DESC, e.created_at DESC
            LIMIT 1
""" + "\n            ",
            new OrderedDictionary<string, object?>
            {
                ["saga_uuid"] = sagaUuid,
                ["current_episode_uuid"] = currentEpisodeUuid,
                ["group_id"] = groupId,
                ["routing_"] = "r",
            },
            cancellationToken);
        if (records.Count > 0)
        {
            // PORT: the record value is open data, and the result is a `str | None`. A value of another type throws
            // InvalidCastException here, where Python returns it.
            return (string?)records[0]["uuid"];
        }

        return null;
    }

    /// <summary>Retrieve (content, valid_at, created_at) episodes, using IoC if available.</summary>
    // PORT: internal, because the recording test calls it (R2).
    internal async Task<List<(string, PyDateTime?, PyDateTime)>?> SagaGetEpisodeContentsAsync(
        string sagaUuid,
        PyDateTime? since = null,
        long limit = 200,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        driver ??= Driver;
        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.SagaGetEpisodeContentsAsync(
                    driver, sagaUuid, since: since, limit: limit, cancellationToken: cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        return null;
    }

    /// <summary>
    /// Incrementally summarize a saga using only new episodes since the last summary.
    ///
    /// Two watermarks are maintained on the saga node, with deliberately
    /// different semantics:
    ///
    /// - ``last_summarized_at`` is the ingestion-time *filter* watermark:
    ///   the next run picks up any episode whose ``created_at`` is greater
    ///   than this value. It advances only to the latest episode actually
    ///   included in the summary prompt, so byte-budgeted episodes remain
    ///   reachable on subsequent runs.
    /// - ``last_summarized_episode_valid_at`` is the *temporal* watermark:
    ///   the maximum ``valid_at`` (episode reference time) across the
    ///   episodes covered by the current summary. Consumers asking "how
    ///   recent is the content of this summary in event-time?" should use
    ///   this field, not ``last_summarized_at``.
    ///
    /// If the saga has been summarized before, only episodes added after
    /// ``last_summarized_at`` are fetched. The existing summary is provided
    /// to the LLM as context so no information is lost.
    ///
    /// On the first call (no prior summary), all episodes are included.
    ///
    /// Parameters
    /// ----------
    /// saga_id : str
    ///     The UUID of the saga to summarize.
    /// group_id : str | None
    ///     If set, the saga must belong to this group or NodeNotFoundError is raised.
    ///     If None, the lookup does not check the group.
    ///
    /// Returns
    /// -------
    /// SagaNode
    ///     The updated saga node with the new summary.
    ///
    /// Raises
    /// ------
    /// NodeNotFoundError
    ///     If the saga is not found in the selected group.
    /// </summary>
    internal async Task<SagaNode> SummarizeSagaAsync(
        string sagaId,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        var driver = Driver;

        // PORT: `getattr(self, 'clients', None)` reads the property (R15). An object that a test made with no call of
        // the constructor has a null value there.
        GraphitiClients? sagaClients = Clients;
        if (groupId is not null)
        {
            (groupId, driver, sagaClients) = ResolveRequestScope(groupId);
        }

        var saga = await SagaNode.GetByUuidAsync(driver, sagaId, cancellationToken);
        if (groupId is not null && saga.GroupId != groupId)
        {
            throw new NodeNotFoundException(sagaId);
        }

        // Fetch only episodes added since the last summary. Cap the window to the same episode
        // count node summaries use so a long thread or document cannot fill
        // the model context window in one pass. Deferred episodes remain
        // eligible because last_summarized_at advances only through this batch.
        var maxEpisodes = GraphitiModule.MaxSagaEpisodesForSummary;
        var since = saga.LastSummarizedAt;

        // PORT: `saga.summary or ''` gives the same text as the summary for each str value.
        var existingSummary = TextUtils.TruncateAtSentence(saga.Summary ?? string.Empty, TextUtils.SagaSummaryMaxChars);

        // Try IoC interface first, fall back to raw Cypher
        var episodesData = await SagaGetEpisodeContentsAsync(
            sagaId, since: since, limit: maxEpisodes, driver: driver, cancellationToken: cancellationToken);
        if (episodesData is null)
        {
            List<OrderedDictionary<string, object?>> records;
            if (since is not null)
            {
                (records, _, _) = await driver.ExecuteQueryAsync(
                    "\n" + """
                    MATCH (s:Saga {uuid: $saga_uuid})-[:HAS_EPISODE]->(e:Episodic)
                    WHERE e.group_id = s.group_id AND e.created_at IS NOT NULL AND e.created_at > $since
                    RETURN e.content AS content, e.valid_at AS valid_at, e.created_at AS created_at
                    ORDER BY e.created_at ASC, e.valid_at ASC
                    LIMIT $limit
""" + "\n                    ",
                    new OrderedDictionary<string, object?>
                    {
                        ["saga_uuid"] = sagaId,
                        ["since"] = since,
                        ["limit"] = maxEpisodes,
                        ["routing_"] = "r",
                    },
                    cancellationToken);
            }
            else
            {
                (records, _, _) = await driver.ExecuteQueryAsync(
                    "\n" + """
                    MATCH (s:Saga {uuid: $saga_uuid})-[:HAS_EPISODE]->(e:Episodic)
                    WHERE e.group_id = s.group_id AND e.created_at IS NOT NULL
                    RETURN e.content AS content, e.valid_at AS valid_at, e.created_at AS created_at
                    ORDER BY e.created_at ASC, e.valid_at ASC
                    LIMIT $limit
""" + "\n                    ",
                    new OrderedDictionary<string, object?>
                    {
                        ["saga_uuid"] = sagaId,
                        ["limit"] = maxEpisodes,
                        ["routing_"] = "r",
                    },
                    cancellationToken);
            }

            episodesData = [];
            foreach (var r in records)
            {
                var content = Py.Get(r, "content");
                var createdAt = Helpers.ParseDbDate(Py.Get(r, "created_at"));
                if (Py.Truthy(content) && createdAt is not null)
                {
                    // PORT: the tuple holds a str and two datetimes. A record value of another type throws
                    // InvalidCastException at the typed read, where Python keeps it in the tuple.
                    var validAt = Helpers.ParseDbDate(Py.Get(r, "valid_at"));
                    episodesData.Add((Py.AsString(content), validAt is null ? null : Py.AsDateTime(validAt), Py.AsDateTime(createdAt)));
                }
            }
        }

        if (episodesData.Count == 0)
        {
            saga.SummaryEpisodesFetched = 0;
            saga.SummaryEpisodesSelected = 0;
            saga.SummaryEpisodesSkipped = 0;
            saga.SummarySelectedEpisodeBytes = 0;
            GraphitiModule.Log.NoNewEpisodesForSaga(GraphitiModule.Logger, sagaId);
            return saga;
        }

        long totalEpisodeCount = episodesData.Count;
        (episodesData, var selectedEpisodeBytes, var episodesDropped) = GraphitiModule.SelectSagaSummaryEpisodes(
            episodesData, maxEpisodeChars: GraphitiModule.SagaSummaryMaxEpisodeChars.Value);
        saga.SummaryEpisodesFetched = totalEpisodeCount;
        saga.SummaryEpisodesSelected = episodesData.Count;
        saga.SummaryEpisodesSkipped = totalEpisodeCount - episodesData.Count;
        saga.SummarySelectedEpisodeBytes = selectedEpisodeBytes;
        if (episodesData.Count == 0)
        {
            GraphitiModule.Log.NoSagaEpisodeContentFit(GraphitiModule.Logger, sagaId);
            return saga;
        }

        if (episodesDropped)
        {
            using (GraphitiModule.Logger.BeginScope(new OrderedDictionary<string, object?>
            {
                ["saga_id"] = sagaId,
                ["episodes_fetched"] = totalEpisodeCount,
                ["episodes_selected"] = (long)episodesData.Count,
                ["selected_episode_bytes"] = selectedEpisodeBytes,
                ["max_episode_chars"] = GraphitiModule.SagaSummaryMaxEpisodeChars.Value,
                ["max_prompt_bytes"] = GraphitiModule.SagaSummaryMaxPromptBytes.Value,
                ["episodes_dropped"] = episodesDropped,
            }))
            {
                GraphitiModule.Log.SagaSummaryPayloadConstrained(GraphitiModule.Logger);
            }
        }

        // PORT: a nested function is a local function (R8).
        OrderedDictionary<string, object?> PromptContext(List<(string, PyDateTime?, PyDateTime)> selected)
        {
            // PORT: a sort with a tuple key of datetimes is Py.SortInPlace on a copy (R14). A datetime is always true,
            // so `episode[1] or episode[2]` takes the second item only for None.
            var promptEpisodesData = new List<(string, PyDateTime?, PyDateTime)>(selected);
            Py.SortInPlace(promptEpisodesData, episode => new PyTuple(episode.Item2 ?? episode.Item3, episode.Item3));
            return new OrderedDictionary<string, object?>
            {
                ["saga_name"] = saga.Name,
                ["existing_summary"] = existingSummary,
                ["episodes"] = promptEpisodesData.Select(episode => (object?)episode.Item1).ToList(),
            };
        }

        long schemaReserve = PyText.EncodeUtf8(GraphitiModule.SagaSummarySchemaWrapper).Length
            + PyText.EncodeUtf8(PyJson.Dumps(SagaSummary.ModelJsonSchema.ToDict())).Length;
        while (episodesData.Count > 0)
        {
            var context = PromptContext(episodesData);
            var promptBytes =
                GraphitiModule.SagaSummaryMessagesUtf8Bytes(
                    GraphitiTypes.ResolvePromptMessages(
                        "summarize_sagas.summarize_saga",
                        Lib.PromptLibrary.SummarizeSagas.SummarizeSaga,
                        context,
                        clients: sagaClients))
                + schemaReserve;
            if (promptBytes <= GraphitiModule.SagaSummaryMaxPromptBytes.Value)
            {
                break;
            }

            if (episodesData.Count == 1)
            {
                var (content, validAt, createdAt) = episodesData[0];
                var over = promptBytes - GraphitiModule.SagaSummaryMaxPromptBytes.Value;
                var truncated = TextUtils.TruncateUtf8ToBytes(
                    content, Math.Max(0, PyText.EncodeUtf8(content).Length - over));
                episodesData = truncated.Length > 0 ? [(truncated, validAt, createdAt)] : [];
                break;
            }

            episodesData = Py.Slice(episodesData, null, -1);

            // PORT: Python sets `episodes_dropped` here and reads it no more.
            episodesDropped = true;
        }

        _ = episodesDropped;
        saga.SummaryEpisodesSelected = episodesData.Count;
        saga.SummaryEpisodesSkipped = totalEpisodeCount - episodesData.Count;
        long summarizedBytes = 0;
        foreach (var (content, _, _) in episodesData)
        {
            summarizedBytes += PyText.EncodeUtf8(content).Length;
        }

        saga.SummarySelectedEpisodeBytes = summarizedBytes;
        if (episodesData.Count == 0)
        {
            GraphitiModule.Log.NoSagaEpisodeContentFit(GraphitiModule.Logger, sagaId);
            return saga;
        }

        List<PyDateTime> validAts = [];
        foreach (var (_, validAt, _) in episodesData)
        {
            if (validAt is { } value)
            {
                validAts.Add(value);
            }
        }

        List<PyDateTime> summarizedCreatedAts = [.. episodesData.Select(e => e.Item3)];

        var promptContext = PromptContext(episodesData);

        var llmResponse = await GraphitiTypes.GeneratePromptResponseAsync(
            LlmClient,
            "summarize_sagas.summarize_saga",
            Lib.PromptLibrary.SummarizeSagas.SummarizeSaga,
            promptContext,
            clients: sagaClients,
            responseModel: SagaSummary.ResponseModel,
            maxTokens: GraphitiModule.SagaSummaryMaxTokens,
            cancellationToken: cancellationToken);

        // PORT: a reply value that the annotation gives as `str` is read with Py.AsString (R20, GAPS F44).
        var summary = Py.AsString(Py.Get(llmResponse, "summary", (object?)string.Empty));
        if (Py.Len(summary) > TextUtils.SagaSummaryMaxChars)
        {
            summary = TextUtils.TruncateAtSentence(summary, TextUtils.SagaSummaryMaxChars);
        }

        saga.Summary = summary;

        // Ingestion-time watermark for the next-run filter. Advance only
        // through the episodes that were actually sent to the summary prompt so
        // byte-budgeted later episodes remain reachable on subsequent runs.
        // PORT: `max(xs)` keeps the first of equal items and compares with `>` (R14), as the loops below do.
        PyDateTime? lastSummarizedAt = null;
        foreach (var createdAt in summarizedCreatedAts)
        {
            if (lastSummarizedAt is null || createdAt > lastSummarizedAt.Value)
            {
                lastSummarizedAt = createdAt;
            }
        }

        saga.LastSummarizedAt = summarizedCreatedAts.Count > 0 ? lastSummarizedAt : null;

        // Episode-time watermark for public/temporal consumers: advance only
        // forward to the latest reference time we just summarized. If no
        // episode in this batch carried a valid_at, leave the previous value unchanged.
        if (validAts.Count > 0)
        {
            var newEpisodeWatermark = validAts[0];
            foreach (var validAt in validAts)
            {
                if (validAt > newEpisodeWatermark)
                {
                    newEpisodeWatermark = validAt;
                }
            }

            if (
                saga.LastSummarizedEpisodeValidAt is null
                || newEpisodeWatermark > saga.LastSummarizedEpisodeValidAt.Value)
            {
                saga.LastSummarizedEpisodeValidAt = newEpisodeWatermark;
            }
        }

        await saga.SaveAsync(driver, cancellationToken);

        GraphitiModule.Log.UpdatedSummaryForSaga(GraphitiModule.Logger, sagaId);

        return saga;
    }

    /// <summary>
    /// Build indices and constraints in the Neo4j database.
    ///
    /// This method sets up the necessary indices and constraints in the Neo4j database
    /// to optimize query performance and ensure data integrity for the knowledge graph.
    ///
    /// Parameters
    /// ----------
    /// self
    /// delete_existing : bool, optional
    ///     Whether to clear existing indices before creating new ones.
    ///
    ///
    /// Returns
    /// -------
    /// None
    ///
    /// Notes
    /// -----
    /// This method should typically be called once during the initial setup of the
    /// knowledge graph or when updating the database schema. It uses the
    /// driver's `build_indices_and_constraints` method to perform
    /// the actual database operations.
    ///
    /// The specific indices and constraints created depend on the implementation
    /// of the driver's `build_indices_and_constraints` method. Refer to the specific
    /// driver documentation for details on the exact database schema modifications.
    ///
    /// Caution: Running this method on a large existing database may take some time
    /// and could impact database performance during execution.
    /// </summary>
    internal async Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default)
    {
        await Driver.BuildIndicesAndConstraintsAsync(deleteExisting, cancellationToken);
    }

    /// <summary>Extract nodes from episode(s) and resolve against existing graph.</summary>
    // PORT: `episode: EpisodicNode | list[EpisodicNode]` is the `EpisodeOrEpisodes` of node_operations (Part 3).
    // PORT: internal, because the recording test calls it: no method of the class calls it (R2).
    internal async Task<(List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>, OrderedDictionary<string, List<long>>)> ExtractAndResolveNodesAsync(
        EpisodeOrEpisodes episode,
        List<EpisodicNode> previousEpisodes,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        List<string>? excludedEntityTypes,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        List<EpisodicNode> episodes = episode is EpisodeOrEpisodes.Many many ? many.Episodes : [((EpisodeOrEpisodes.One)episode).Episode];
        var primaryEpisode = episodes[0];

        var (extractedNodes, nodeEpisodeIndexMap) = await NodeOperations.ExtractNodesAsync(
            Clients, episode, previousEpisodes, entityTypes, excludedEntityTypes, cancellationToken: cancellationToken);

        List<EntityNode> identityNodes;
        if (identityProperties is { Count: > 0 })
        {
            identityNodes = [.. extractedNodes.Where(node => DedupHelpers.IdentityTypeForNode(node, identityProperties) is not null)];
        }
        else
        {
            identityNodes = [];
        }

        if (identityNodes.Count > 0)
        {
            await NodeOperations.ExtractAttributesOnlyFromNodesAsync(
                Clients,
                identityNodes,
                episode,
                previousEpisodes,
                entityTypes,
                validateShape: false,
                cancellationToken: cancellationToken);
        }

        List<EntityNode> nodes;
        OrderedDictionary<string, string> uuidMap;
        List<(EntityNode, EntityNode)> duplicates;
        if (identityProperties is { Count: > 0 })
        {
            (nodes, uuidMap, duplicates) = await NodeOperations.ResolveExtractedNodesAsync(
                Clients,
                extractedNodes,
                primaryEpisode,
                previousEpisodes,
                entityTypes,
                identityProperties: identityProperties,
                cancellationToken: cancellationToken);
        }
        else
        {
            (nodes, uuidMap, duplicates) = await NodeOperations.ResolveExtractedNodesAsync(
                Clients,
                extractedNodes,
                primaryEpisode,
                previousEpisodes,
                entityTypes,
                cancellationToken: cancellationToken);
        }

        return (nodes, uuidMap, duplicates, nodeEpisodeIndexMap);
    }

    /// <summary>
    /// Fold endpoints materialized by ``extract_edges`` into the resolved node set.
    ///
    /// These names never went through node extraction, so they still have to be
    /// deduplicated the way ordinary extracted nodes are. Nodes resolved for this
    /// episode are not persisted yet and are therefore invisible to graph search,
    /// so match against them by name first and send only the remainder through
    /// ``resolve_extracted_nodes``. ``nodes`` and ``uuid_map`` are updated in place.
    /// </summary>
    private async Task MergeMaterializedEndpointNodesAsync(
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
        clients ??= Clients;

        // Pass `trace` only when it is set, so the serial path makes the calls of today.
        // PORT: a null `trace` is the default of resolve_extracted_nodes, so the call passes it as it is.
        // PORT: a dict that code reads only by key is a Dictionary (R14). The last node of a name wins, as in Python.
        var nodesByName = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            nodesByName[DedupHelpers.NormalizeStringExact(node.Name)] = node;
        }

        var knownUuids = nodes.Select(node => node.Uuid).ToHashSet(StringComparer.Ordinal);

        List<EntityNode> unresolved = [];
        foreach (var materializedNode in materializedNodes)
        {
            if (knownUuids.Contains(materializedNode.Uuid))
            {
                continue;
            }

            if (nodesByName.TryGetValue(DedupHelpers.NormalizeStringExact(materializedNode.Name), out var existing))
            {
                uuidMap[materializedNode.Uuid] = existing.Uuid;
                continue;
            }

            unresolved.Add(materializedNode);
        }

        if (unresolved.Count == 0)
        {
            return;
        }

        List<EntityNode> resolvedNodes;
        OrderedDictionary<string, string> materializedUuidMap;
        if (identityProperties is { Count: > 0 })
        {
            (resolvedNodes, materializedUuidMap, _) = await NodeOperations.ResolveExtractedNodesAsync(
                clients,
                unresolved,
                episode,
                previousEpisodes,
                entityTypes,
                identityProperties: identityProperties,
                trace: trace,
                cancellationToken: cancellationToken);
        }
        else
        {
            (resolvedNodes, materializedUuidMap, _) = await NodeOperations.ResolveExtractedNodesAsync(
                clients,
                unresolved,
                episode,
                previousEpisodes,
                entityTypes,
                trace: trace,
                cancellationToken: cancellationToken);
        }

        foreach (var (key, value) in materializedUuidMap)
        {
            uuidMap[key] = value;
        }

        foreach (var resolvedNode in resolvedNodes)
        {
            // PORT: `if uuid in known: continue` with `known.add(uuid)` after the append is one `if (s.Add(x))`
            // (CA1868).
            if (knownUuids.Add(resolvedNode.Uuid))
            {
                nodes.Add(resolvedNode);
            }
        }
    }

    /// <summary>
    /// Extract edges from episode(s) and resolve against existing graph.
    ///
    /// Returns
    /// -------
    /// tuple[list[EntityEdge], list[EntityEdge], list[EntityEdge]]
    ///     A tuple of (resolved_edges, invalidated_edges, new_edges) where:
    ///     - resolved_edges: All edges after resolution
    ///     - invalidated_edges: Edges invalidated by new information
    ///     - new_edges: Only edges that are new to the graph (not duplicates)
    /// </summary>
    // PORT: internal, because ingestion/phases.py calls it (R2). `episode` is the
    // `EpisodeOrEpisodes` of node_operations.
    internal async Task<(List<EntityEdge>, List<EntityEdge>, List<EntityEdge>)> ExtractAndResolveEdgesAsync(
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
        clients ??= Clients;

        // PORT: a null `trace` is the default of resolve_extracted_edges, so the call passes it as it is.
        List<EpisodicNode> episodes = episode is EpisodeOrEpisodes.Many many ? many.Episodes : [((EpisodeOrEpisodes.One)episode).Episode];
        var primaryEpisode = episodes[0];

        var (extractedEdges, materializedNodes) = await EdgeOperations.ExtractEdgesAsync(
            clients,
            episode,
            extractedNodes,
            previousEpisodes,
            edgeTypeMap,
            groupId,
            edgeTypes,
            strictEdgeTypes,
            customExtractionInstructions,
            cancellationToken);

        // Merge only nodes that extract_edges newly materialized — never the full
        // pre-resolution extracted_nodes list, which also contains deduped duplicates
        // whose uuid_map remaps must be preserved for resolve_edge_pointers.
        if (materializedNodes.Count > 0)
        {
            await MergeMaterializedEndpointNodesAsync(
                materializedNodes,
                primaryEpisode,
                previousEpisodes,
                entityTypes,
                nodes,
                uuidMap,
                identityProperties: identityProperties,
                clients: clients,
                trace: trace,
                cancellationToken: cancellationToken);
        }

        var edges = BulkUtils.ResolveEdgePointers(extractedEdges, uuidMap);

        var (resolvedEdges, invalidatedEdges, newEdges) = await EdgeOperations.ResolveExtractedEdgesAsync(
            clients,
            edges,
            primaryEpisode,
            nodes,
            edgeTypes is { Count: > 0 } ? edgeTypes : new OrderedDictionary<string, GraphTypeDescriptor>(),
            edgeTypeMap,
            trace: trace,
            cancellationToken: cancellationToken);

        return (resolvedEdges, invalidatedEdges, newEdges);
    }

    /// <summary>
    /// Process and save episode data to the graph.
    ///
    /// Parameters
    /// ----------
    /// episode : EpisodicNode | list[EpisodicNode]
    ///     The episode(s) to process.
    /// nodes : list[EntityNode]
    ///     The entity nodes extracted from the episode(s).
    /// entity_edges : list[EntityEdge]
    ///     The entity edges extracted from the episode(s).
    /// now : datetime
    ///     The current timestamp.
    /// group_id : str
    ///     The group id for the episode.
    /// saga : str | SagaNode | None
    ///     Optional. Either a saga name (str) or a SagaNode object to associate
    ///     this episode with. If a string is provided, the saga will be looked up
    ///     by name or created if it doesn't exist.
    /// saga_previous_episode_uuid : str | None
    ///     Optional. UUID of the previous episode in the saga. If provided, skips
    ///     the database query to find the most recent episode. Useful for efficiently
    ///     adding multiple episodes to the same saga in sequence.
    /// node_episode_index_map : dict[str, list[int]] | None
    ///     Optional mapping from node UUID to 0-indexed episode positions for
    ///     building episodic edges with correct attribution.
    /// clients : GraphitiClients | None
    ///     Optional request-scoped clients bundle. Defaults to ``self.clients``.
    ///     Callers pass a per-request bundle so concurrent calls for different
    ///     group_ids target the correct database (issue #1676).
    /// </summary>
    // PORT: `episode` is an `EpisodeOrEpisodes`, and `saga` is a `SagaOrName?` (Part 3).
    private async Task<(List<EpisodicEdge>, EpisodicNode)> ProcessEpisodeDataAsync(
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
        clients ??= Clients;
        var driver = clients.Driver;
        List<EpisodicNode> episodes = episode is EpisodeOrEpisodes.Many many ? many.Episodes : [((EpisodeOrEpisodes.One)episode).Episode];
        List<string> episodeUuids = [.. episodes.Select(ep => ep.Uuid)];

        var episodicEdges = EdgeOperations.BuildEpisodicEdges(nodes, episodeUuids, now, nodeEpisodeIndexMap);
        foreach (var ep in episodes)
        {
            ep.EntityEdges = [.. entityEdges.Select(edge => edge.Uuid)];
            if (!StoreRawEpisodeContent)
            {
                ep.Content = string.Empty;
            }
        }

        await BulkUtils.AddNodesAndEdgesBulkAsync(
            driver,
            episodes,
            episodicEdges,
            nodes,
            entityEdges,
            clients.Embedder,
            cancellationToken: cancellationToken);

        var primaryEpisode = episodes[0];

        // Handle saga association if provided
        if (saga is not null)
        {
            await GraphitiModule.ValidateSagaScopeAsync(saga, groupId, driver, cancellationToken);

            // Get or create saga node based on input type
            SagaNode sagaNode;
            if (saga is SagaOrName.Name sagaName)
            {
                // Use the originating episode's reference time (valid_at) for a
                // newly created saga so its created_at matches the episode that
                // minted it, not the wall-clock time of this run.
                // PORT: `primary_episode.valid_at or now`. A datetime is always true, and `valid_at` is required.
                var sagaCreatedAt = primaryEpisode.ValidAt;
                sagaNode = await GetOrCreateSagaAsync(
                    sagaName.Value, groupId, sagaCreatedAt, driver: driver, cancellationToken: cancellationToken);
            }
            else
            {
                sagaNode = ((SagaOrName.Node)saga).Saga;
            }

            // Use provided previous episode UUID or query for it
            var previousEpisodeUuid = sagaPreviousEpisodeUuid;
            if (previousEpisodeUuid is null)
            {
                previousEpisodeUuid = await SagaGetPreviousEpisodeUuidAsync(
                    sagaNode.Uuid, primaryEpisode.Uuid, groupId, driver: driver, cancellationToken: cancellationToken);
            }

            // Create NEXT_EPISODE edge from the previous episode to the new one
            if (previousEpisodeUuid is not null)
            {
                var nextEpisodeEdge = new NextEpisodeEdge
                {
                    SourceNodeUuid = previousEpisodeUuid,
                    TargetNodeUuid = primaryEpisode.Uuid,
                    GroupId = groupId,
                    CreatedAt = now,
                }.Validate();
                await nextEpisodeEdge.SaveAsync(driver, cancellationToken);
            }

            // Create HAS_EPISODE edge from saga to the new episode
            var hasEpisodeEdge = new HasEpisodeEdge
            {
                SourceNodeUuid = sagaNode.Uuid,
                TargetNodeUuid = primaryEpisode.Uuid,
                GroupId = groupId,
                CreatedAt = now,
            }.Validate();
            await hasEpisodeEdge.SaveAsync(driver, cancellationToken);

            // Track first and last episode on the saga node
            if (sagaNode.FirstEpisodeUuid is null)
            {
                sagaNode.FirstEpisodeUuid = primaryEpisode.Uuid;
            }

            sagaNode.LastEpisodeUuid = primaryEpisode.Uuid;
            await sagaNode.SaveAsync(driver, cancellationToken);
        }

        return (episodicEdges, primaryEpisode);
    }

    /// <summary>Extract nodes and edges from all episodes and deduplicate.</summary>
    // PORT: internal, because ingestion/phases.py calls it (R2).
    internal async Task<(OrderedDictionary<string, List<EntityNode>>, OrderedDictionary<string, string>, List<List<EntityEdge>>)> ExtractAndDedupeNodesBulkAsync(
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
        clients ??= Clients;

        // Extract all nodes and edges for each episode
        var (extractedNodesBulk, extractedEdgesBulk) = await BulkUtils.ExtractNodesAndEdgesBulkAsync(
            clients,
            episodeContext,
            edgeTypeMap: edgeTypeMap,
            edgeTypes: edgeTypes,
            entityTypes: entityTypes,
            excludedEntityTypes: excludedEntityTypes,
            strictEdgeTypes: strictEdgeTypes,
            customExtractionInstructions: customExtractionInstructions,
            useCombinedExtraction: useCombinedExtraction,
            cancellationToken: cancellationToken);

        if (identityProperties is { Count: > 0 })
        {
            List<Func<Task<List<EntityNode>>>> extractionCalls = [];
            foreach (var (index, (episode, previousEpisodes)) in episodeContext.Index())
            {
                List<EntityNode> identityNodes =
                [
                    .. extractedNodesBulk[index].Where(node => DedupHelpers.IdentityTypeForNode(node, identityProperties) is not null),
                ];
                if (identityNodes.Count > 0)
                {
                    extractionCalls.Add(() => NodeOperations.ExtractAttributesOnlyFromNodesAsync(
                        clients,
                        identityNodes,
                        new EpisodeOrEpisodes.One { Episode = episode },
                        previousEpisodes,
                        entityTypes,
                        validateShape: false,
                        cancellationToken: cancellationToken));
                }
            }

            if (extractionCalls.Count > 0)
            {
                await Helpers.SemaphoreGatherAsync(
                    extractionCalls,
                    maxCoroutines: MaxCoroutines,
                    cancellationToken: cancellationToken);
            }
        }

        // Dedupe extracted nodes in memory
        // PORT: a null `trace` is the default of dedupe_nodes_bulk, so the call passes it as it is.
        var (nodesByEpisode, uuidMap) = await BulkUtils.DedupeNodesBulkAsync(
            clients,
            extractedNodesBulk,
            episodeContext,
            entityTypes,
            identityProperties: identityProperties,
            trace: trace,
            cancellationToken: cancellationToken);

        return (nodesByEpisode, uuidMap, extractedEdgesBulk);
    }

    /// <summary>
    /// Resolve nodes and edges against the existing graph.
    ///
    /// With ``hydrate=False``, the method does not extract node attributes and summaries.
    /// </summary>
    // PORT: internal, because ingestion/phases.py calls it (R2).
    internal async Task<(List<EntityNode>, List<EntityEdge>, List<EntityEdge>, OrderedDictionary<string, string>)> ResolveNodesAndEdgesBulkAsync(
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
        clients ??= Clients;

        // PORT: a null `trace` is the default of each called function, so the calls pass it as it is. A dict that code
        // reads only by key is a Dictionary (R14).
        var nodesByUuid = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        foreach (var nodes in nodesByEpisode.Values)
        {
            foreach (var node in nodes)
            {
                nodesByUuid[node.Uuid] = node;
            }
        }

        // Get unique nodes per episode
        var nodesByEpisodeUnique = new OrderedDictionary<string, List<EntityNode>>(StringComparer.Ordinal);
        var nodesUuidSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (episode, _) in episodeContext)
        {
            nodesByEpisodeUnique[episode.Uuid] = [];
            List<EntityNode> nodes = [.. nodesByEpisode[episode.Uuid].Select(node => nodesByUuid[node.Uuid])];
            foreach (var node in nodes)
            {
                // PORT: `if uuid not in s: ...append(node); s.add(uuid)` is one `if (s.Add(x))` (CA1868).
                if (nodesUuidSet.Add(node.Uuid))
                {
                    nodesByEpisodeUnique[episode.Uuid].Add(node);
                }
            }
        }

        // Resolve nodes
        // PORT: a nested coroutine is a local async function (R8).
        async Task<(List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>)> ResolveNodesAsync(
            EpisodicNode episode,
            List<EpisodicNode> previousEpisodes)
        {
            var nodes = nodesByEpisodeUnique[episode.Uuid];
            if (identityProperties is { Count: > 0 })
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

        var nodeResults = await Helpers.SemaphoreGatherAsync(
            [
                .. episodeContext.Select<(EpisodicNode, List<EpisodicNode>), Func<Task<(List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>)>>>(
                    context => () => ResolveNodesAsync(context.Item1, context.Item2)),
            ],
            maxCoroutines: MaxCoroutines,
            cancellationToken: cancellationToken);

        List<EntityNode> resolvedNodes = [];
        var uuidMap = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var result in nodeResults)
        {
            resolvedNodes.AddRange(result.Item1);
            foreach (var (key, value) in result.Item2)
            {
                uuidMap[key] = value;
            }
        }

        // Update nodes_by_uuid with resolved nodes
        foreach (var resolvedNode in resolvedNodes)
        {
            nodesByUuid[resolvedNode.Uuid] = resolvedNode;
        }

        // Update nodes_by_episode_unique with resolved pointers
        // PORT: Python assigns the value of each key while it iterates the items. The loop iterates a copy of the keys,
        // because a C# collection refuses a change during its enumeration.
        foreach (var episodeUuid in nodesByEpisodeUnique.Keys.ToList())
        {
            var nodes = nodesByEpisodeUnique[episodeUuid];
            List<EntityNode> updatedNodes = [];
            foreach (var node in nodes)
            {
                var updatedNodeUuid = Py.Get(uuidMap, node.Uuid, node.Uuid);
                var updatedNode = nodesByUuid[updatedNodeUuid];
                updatedNodes.Add(updatedNode);
            }

            nodesByEpisodeUnique[episodeUuid] = updatedNodes;
        }

        // PORT: a tuple of episode UUIDs is a PyTuple key, which compares by value (R14).
        var identityNodesByEpisodeUuids = new OrderedDictionary<PyTuple, List<EntityNode>>();
        if (identityProperties is { Count: > 0 })
        {
            // PORT: Python builds `episodes_by_uuid`, `episode_context_indices`, and `previous_episodes_by_uuid` here
            // and reads none of them before it builds them again below. The port builds them at the place where code
            // reads them.
            var mentioningEpisodesByNodeUuid = new OrderedDictionary<string, List<EpisodicNode>>(StringComparer.Ordinal);
            foreach (var (episode, _) in episodeContext)
            {
                var episodeNodeUuids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var node in nodesByEpisode[episode.Uuid])
                {
                    var nodeUuid = Py.Get(uuidMap, node.Uuid, node.Uuid);
                    if (!episodeNodeUuids.Add(nodeUuid))
                    {
                        continue;
                    }

                    Py.SetDefault(mentioningEpisodesByNodeUuid, nodeUuid, []).Add(episode);
                }
            }

            var multiMentionIdentityNodeUuids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (nodeUuid, mentioningEpisodes) in mentioningEpisodesByNodeUuid)
            {
                var node = nodesByUuid[nodeUuid];
                if (
                    mentioningEpisodes.Count > 1
                    && DedupHelpers.IdentityTypeForNode(node, identityProperties) is not null)
                {
                    var episodeUuids = new PyTuple([.. mentioningEpisodes.Select(episode => (object?)episode.Uuid)]);
                    Py.SetDefault(identityNodesByEpisodeUuids, episodeUuids, []).Add(node);
                    multiMentionIdentityNodeUuids.Add(nodeUuid);
                }
            }

            if (multiMentionIdentityNodeUuids.Count > 0)
            {
                foreach (var episodeUuid in nodesByEpisodeUnique.Keys.ToList())
                {
                    var nodes = nodesByEpisodeUnique[episodeUuid];
                    nodesByEpisodeUnique[episodeUuid] = [.. nodes.Where(node => !multiMentionIdentityNodeUuids.Contains(node.Uuid))];
                }
            }
        }

        // Extract attributes for resolved nodes
        List<List<EntityNode>> hydratedNodesResults;
        if (hydrate)
        {
            // PORT: Python builds each coroutine, and so reads each argument, before the gather (R8).
            List<Func<Task<List<EntityNode>>>> episodeHydrationCalls = [];
            foreach (var (episode, previousEpisodes) in episodeContext)
            {
                var episodeNodes = nodesByEpisodeUnique[episode.Uuid];
                episodeHydrationCalls.Add(() => NodeOperations.ExtractAttributesFromNodesAsync(
                    clients,
                    episodeNodes,
                    new EpisodeOrEpisodes.One { Episode = episode },
                    previousEpisodes,
                    entityTypes,
                    cancellationToken: cancellationToken));
            }

            if (identityProperties is { Count: > 0 })
            {
                // PORT: dicts that code reads only by key are Dictionaries (R14). The last value of a key wins.
                var episodesByUuid = new Dictionary<string, EpisodicNode>(StringComparer.Ordinal);
                var episodeContextIndices = new Dictionary<string, long>(StringComparer.Ordinal);
                var previousEpisodesByUuid = new Dictionary<string, List<EpisodicNode>>(StringComparer.Ordinal);
                foreach (var (index, (episode, previousEpisodes)) in episodeContext.Index())
                {
                    episodesByUuid[episode.Uuid] = episode;
                    episodeContextIndices[episode.Uuid] = index;
                    previousEpisodesByUuid[episode.Uuid] = previousEpisodes;
                }

                List<Func<Task<List<EntityNode>>>> identityHydrationCalls = [];
                foreach (var (episodeUuids, nodes) in identityNodesByEpisodeUuids)
                {
                    // PORT: a sort with a tuple key that holds a datetime is Py.SortInPlace on a new list (R14).
                    List<EpisodicNode> mentioningEpisodes = [.. episodeUuids.Select(episodeUuid => episodesByUuid[(string)episodeUuid!])];
                    Py.SortInPlace(
                        mentioningEpisodes,
                        episode => new PyTuple(DatetimeUtils.EnsureUtc(episode.ValidAt), episodeContextIndices[episode.Uuid]));
                    var previous = previousEpisodesByUuid[mentioningEpisodes[0].Uuid];
                    identityHydrationCalls.Add(() => NodeOperations.ExtractAttributesFromNodesAsync(
                        clients,
                        nodes,
                        new EpisodeOrEpisodes.Many { Episodes = mentioningEpisodes },
                        previous,
                        entityTypes,
                        cancellationToken: cancellationToken));
                }

                hydratedNodesResults = await Helpers.SemaphoreGatherAsync(
                    [.. episodeHydrationCalls, .. identityHydrationCalls],
                    maxCoroutines: MaxCoroutines,
                    cancellationToken: cancellationToken);
            }
            else
            {
                hydratedNodesResults = await Helpers.SemaphoreGatherAsync(
                    episodeHydrationCalls,
                    maxCoroutines: MaxCoroutines,
                    cancellationToken: cancellationToken);
            }
        }
        else
        {
            hydratedNodesResults = [.. episodeContext.Select(context => nodesByEpisodeUnique[context.Item1.Uuid])];
            if (identityProperties is { Count: > 0 })
            {
                hydratedNodesResults.AddRange(identityNodesByEpisodeUuids.Values);
            }
        }

        List<EntityNode> finalHydratedNodes = [.. hydratedNodesResults.SelectMany(nodes => nodes)];
        if (identityProperties is { Count: > 0 })
        {
            var uniqueHydratedNodes = new OrderedDictionary<string, EntityNode>(StringComparer.Ordinal);
            foreach (var node in finalHydratedNodes)
            {
                uniqueHydratedNodes.TryAdd(node.Uuid, node);
            }

            finalHydratedNodes = [.. uniqueHydratedNodes.Values];
        }

        // Resolve edges with updated pointers
        var edgesByEpisodeUnique = new OrderedDictionary<string, List<EntityEdge>>(StringComparer.Ordinal);
        var edgesUuidSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (episodeUuid, edges) in edgesByEpisode)
        {
            var edgesWithUpdatedPointers = BulkUtils.ResolveEdgePointers(edges, uuidMap);
            edgesByEpisodeUnique[episodeUuid] = [];

            foreach (var edge in edgesWithUpdatedPointers)
            {
                if (edgesUuidSet.Add(edge.Uuid))
                {
                    edgesByEpisodeUnique[episodeUuid].Add(edge);
                }
            }
        }

        // PORT: Python builds each coroutine, and so reads each argument, before the gather (R8).
        List<Func<Task<(List<EntityEdge>, List<EntityEdge>, List<EntityEdge>)>>> edgeCalls = [];
        foreach (var episode in episodes)
        {
            var episodeEdges = edgesByEpisodeUnique[episode.Uuid];
            var callEdgeTypes = edgeTypes is { Count: > 0 } ? edgeTypes : new OrderedDictionary<string, GraphTypeDescriptor>();
            edgeCalls.Add(() => EdgeOperations.ResolveExtractedEdgesAsync(
                clients,
                episodeEdges,
                episode,
                finalHydratedNodes,
                callEdgeTypes,
                edgeTypeMap,
                trace: trace,
                cancellationToken: cancellationToken));
        }

        var edgeResults = await Helpers.SemaphoreGatherAsync(
            edgeCalls,
            maxCoroutines: MaxCoroutines,
            cancellationToken: cancellationToken);

        List<EntityEdge> resolvedEdges = [];
        List<EntityEdge> invalidatedEdges = [];
        foreach (var result in edgeResults)
        {
            resolvedEdges.AddRange(result.Item1);
            invalidatedEdges.AddRange(result.Item2);

            // result[2] is new_edges - not used in bulk flow since attributes
            // are extracted before edge resolution
        }

        return (finalHydratedNodes, resolvedEdges, invalidatedEdges, uuidMap);
    }

    /// <summary>
    /// Retrieve the last n episodic nodes from the graph.
    ///
    /// This method fetches a specified number of the most recent episodic nodes
    /// from the graph, relative to the given reference time.
    ///
    /// Parameters
    /// ----------
    /// reference_time : datetime
    ///     The reference time to retrieve episodes before.
    /// last_n : int, optional
    ///     The number of episodes to retrieve. Defaults to EPISODE_WINDOW_LEN.
    /// group_ids : list[str | None], optional
    ///     The group ids to return data from.
    /// source : EpisodeType | None, optional
    ///     Filter episodes by source type.
    /// driver : GraphDriver | None, optional
    ///     The graph driver to use. If not provided, uses the default driver.
    /// saga : str | None, optional
    ///     If provided, only retrieve episodes that belong to the saga with this name.
    ///
    /// Returns
    /// -------
    /// list[EpisodicNode]
    ///     A list of the most recent EpisodicNode objects.
    ///
    /// Notes
    /// -----
    /// The actual retrieval is performed by the `retrieve_episodes` function
    /// from the `graphiti_core.utils` module, unless a saga is specified.
    /// </summary>
    // PORT: @handle_multiple_group_ids (R16). The body is RetrieveEpisodesCoreAsync, and this method calls
    // Decorators.HandleMultipleGroupIdsAsync with it. The default of `last_n` is EPISODE_WINDOW_LEN of
    // utils/maintenance/graph_data_operations.py. `hasattr(self, 'clients')` is false for an object that a test made
    // with no call of the constructor, so the wrapper gets a null driver of the clients there (R15).
    internal Task<List<EpisodicNode>> RetrieveEpisodesAsync(
        PyDateTime referenceTime,
        long lastN = GraphDataOperations.EpisodeWindowLen,
        List<string>? groupIds = null,
        EpisodeType? source = null,
        GraphDriver? driver = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        return Decorators.HandleMultipleGroupIdsAsync(
            Clients?.Driver,
            MaxCoroutines,
            groupIds,
            driver,
            (ids, d) => RetrieveEpisodesCoreAsync(referenceTime, lastN, ids, source, d, saga, cancellationToken),
            cancellationToken);
    }

    // PORT: the body of the decorated retrieve_episodes (R16).
    private async Task<List<EpisodicNode>> RetrieveEpisodesCoreAsync(
        PyDateTime referenceTime,
        long lastN = GraphDataOperations.EpisodeWindowLen,
        List<string>? groupIds = null,
        EpisodeType? source = null,
        GraphDriver? driver = null,
        string? saga = null,
        CancellationToken cancellationToken = default)
    {
        if (driver is null)
        {
            driver = Clients.Driver;
        }

        if (driver.GraphOperationsInterface is not null)
        {
            try
            {
                return await driver.GraphOperationsInterface.RetrieveEpisodesAsync(
                    driver, referenceTime, lastN, groupIds, source, saga, cancellationToken);
            }
            catch (NotImplementedException)
            {
                // PORT: Python passes
            }
        }

        return await GraphDataOperations.RetrieveEpisodesAsync(driver, referenceTime, lastN, groupIds, source, saga, cancellationToken);
    }

    /// <summary>
    /// Resolve the effective ``group_id`` and a request-scoped driver/clients bundle.
    ///
    /// Historically ``add_episode`` / ``add_episode_bulk`` reassigned the shared
    /// ``self.driver`` (and ``self.clients.driver``) when a ``group_id`` mapped to a
    /// different database. Because those coroutines contain many ``await`` points
    /// (LLM calls, embeddings, DB writes), a concurrent call for a *different*
    /// ``group_id`` could reassign ``self.driver`` mid-execution, causing the first
    /// call's remaining operations to target the wrong database and silently persist
    /// episodes under the wrong graph (issue #1676).
    ///
    /// Instead of mutating shared instance state, this returns a per-call driver and a
    /// matching ``GraphitiClients`` copy that callers thread through the request. The
    /// default-database case reuses the shared instances unchanged (no copy, no clone),
    /// preserving existing behaviour for the common single-database deployment.
    /// </summary>
    // PORT: internal, because ingestion/phases.py calls it (R2).
    internal (string, GraphDriver, GraphitiClients) ResolveRequestScope(string? groupId)
    {
        if (groupId is null)
        {
            // Use the provider's default group id and the preset database name.
            return (Helpers.GetDefaultGroupId(Driver.Provider), Driver, Clients);
        }

        Helpers.ValidateGroupId(groupId);
        if (groupId == Driver.Database)
        {
            return (groupId, Driver, Clients);
        }

        var driver = Driver.Clone(database: groupId);

        // PORT: `model_copy(update={...})` of a record is `with` (R7).
        var clients = Clients with { Driver = driver };
        return (groupId, driver, clients);
    }

    /// <summary>
    /// Process an episode and update the graph.
    ///
    /// This method extracts information from the episode, creates nodes and edges,
    /// and updates the graph database accordingly.
    ///
    /// Parameters
    /// ----------
    /// name : str
    ///     The name of the episode.
    /// episode_body : str
    ///     The content of the episode.
    /// source_description : str
    ///     A description of the episode's source.
    /// reference_time : datetime
    ///     The reference time for the episode.
    /// source : EpisodeType, optional
    ///     The type of the episode. Defaults to EpisodeType.message.
    /// group_id : str | None
    ///     An id for the graph partition the episode is a part of.
    /// uuid : str | None
    ///     Optional uuid of the episode.
    /// update_communities : bool
    ///     Optional. Whether to update communities with new node information
    /// entity_types : dict[str, BaseModel] | None
    ///     Optional. Dictionary mapping entity type names to their Pydantic model definitions.
    /// excluded_entity_types : list[str] | None
    ///     Optional. List of entity type names to exclude from the graph. Entities classified
    ///     into these types will not be added to the graph. Can include 'Entity' to exclude
    ///     the default entity type.
    /// previous_episode_uuids : list[str] | None
    ///     Optional.  list of episode uuids to use as the previous episodes. If this is not provided,
    ///     the most recent episodes by created_at date will be used.
    /// custom_extraction_instructions : str | None
    ///     Optional. Custom extraction instructions string to be included in the extract entities and extract edges prompts.
    ///     This allows for additional instructions or context to guide the extraction process.
    /// saga : str | SagaNode | None
    ///     Optional. Either a saga name (str) or a SagaNode object to associate this episode with.
    ///     If a string is provided and a saga with this name already exists in the group, the episode
    ///     will be added to it. Otherwise, a new saga will be created. Sagas are connected to episodes
    ///     via HAS_EPISODE edges, and consecutive episodes are linked via NEXT_EPISODE edges.
    /// saga_previous_episode_uuid : str | None
    ///     Optional. UUID of the previous episode in the saga. If provided, skips the database
    ///     query to find the most recent episode. Useful for efficiently adding multiple episodes
    ///     to the same saga in sequence. The returned AddEpisodeResults.episode.uuid can be passed
    ///     as this parameter for the next episode.
    /// identity_properties : dict[str, list[str]] | None
    ///     Optional. Map entity type names to attribute names that identify entities.
    ///     Graphiti extracts these attributes before node resolution.
    ///
    /// Returns
    /// -------
    /// None
    ///
    /// Notes
    /// -----
    /// This method performs several steps including node extraction, edge extraction,
    /// deduplication, and database updates. It also handles embedding generation
    /// and edge invalidation.
    ///
    /// It is recommended to run this method as a background process, such as in a queue.
    /// It's important that each episode is added sequentially and awaited before adding
    /// the next one. For web applications, consider using FastAPI's background tasks
    /// or a dedicated task queue like Celery for this purpose.
    ///
    /// Example using FastAPI background tasks:
    ///     @app.post("/add_episode")
    ///     async def add_episode_endpoint(episode_data: EpisodeData):
    ///         background_tasks.add_task(graphiti.add_episode, **episode_data.dict())
    ///         return {"message": "Episode processing started"}
    /// </summary>
    // PORT: `saga` is a `SagaOrName?` (Part 3).
    internal async Task<AddEpisodeResults> AddEpisodeAsync(
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
        var start = Py.TimeSeconds();
        var now = DatetimeUtils.UtcNow();

        EntityTypesUtils.ValidateEntityTypes(entityTypes);
        EntityTypesUtils.ValidateIdentityProperties(identityProperties, entityTypes);
        Helpers.ValidateExcludedEntityTypes(excludedEntityTypes, entityTypes);

        // Resolve a request-scoped driver/clients bundle rather than mutating the
        // shared ``self.driver``, so concurrent calls for different group_ids cannot
        // clobber each other's database target mid-execution (issue #1676).
        (groupId, var driver, var clients) = ResolveRequestScope(groupId);
        var scopedGroupId = groupId;

        await GraphitiModule.ValidateSagaScopeAsync(saga, scopedGroupId, driver, cancellationToken);
        if (saga is not null && sagaPreviousEpisodeUuid is not null)
        {
            await EpisodicNode.GetByUuidAsync(driver, sagaPreviousEpisodeUuid, groupId: scopedGroupId, cancellationToken: cancellationToken);
        }

        if (ConcurrentIngestionEnabled(scopedGroupId))
        {
            if (updateCommunities)
            {
                throw new ArgumentException(
                    "update_communities=True has no support in concurrent ingestion. "
                    + "Call build_communities after the ingestion.");
            }

            if (uuid is not null)
            {
                await EpisodicNode.GetByUuidAsync(driver, uuid, groupId: scopedGroupId, cancellationToken: cancellationToken);
            }

            if (previousEpisodeUuids is not null)
            {
                await EpisodicNode.GetByUuidsAsync(
                    driver,
                    previousEpisodeUuids,
                    groupId: scopedGroupId,
                    requireAll: true,
                    cancellationToken: cancellationToken);
            }

            return await GetConcurrentIngestor().AddEpisodeAsync(
                name: name,
                episodeBody: episodeBody,
                sourceDescription: sourceDescription,
                referenceTime: referenceTime,
                source: source,
                groupId: scopedGroupId,
                uuid: uuid,
                entityTypes: entityTypes,
                excludedEntityTypes: excludedEntityTypes,
                previousEpisodeUuids: previousEpisodeUuids,
                edgeTypes: edgeTypes,
                edgeTypeMap: edgeTypeMap,
                customExtractionInstructions: customExtractionInstructions,
                saga: saga,
                sagaPreviousEpisodeUuid: sagaPreviousEpisodeUuid,
                identityProperties: identityProperties,
                cancellationToken: cancellationToken);
        }

        return await Tracer.StartSpanAsync(
            "add_episode",
            async span =>
            {
                try
                {
                    // Retrieve previous episodes for context
                    var previousEpisodes =
                        previousEpisodeUuids is null
                            ? await RetrieveEpisodesAsync(
                                referenceTime,
                                lastN: SearchUtils.RelevantSchemaLimit,
                                groupIds: [scopedGroupId],
                                source: source,
                                driver: driver,
                                cancellationToken: cancellationToken)
                            : await EpisodicNode.GetByUuidsAsync(
                                driver,
                                previousEpisodeUuids,
                                groupId: scopedGroupId,
                                requireAll: true,
                                cancellationToken: cancellationToken);

                    // Get or create episode
                    var episode =
                        uuid is not null
                            ? await EpisodicNode.GetByUuidAsync(driver, uuid, groupId: scopedGroupId, cancellationToken: cancellationToken)
                            : new EpisodicNode
                            {
                                Name = name,
                                GroupId = scopedGroupId,
                                Labels = [],
                                Source = source,
                                Content = episodeBody,
                                SourceDescription = sourceDescription,
                                CreatedAt = now,
                                ValidAt = referenceTime,
                            }.Validate();

                    // Create default edge type map
                    var edgeTypeMapDefault =
                        edgeTypes is not null
                            ? new OrderedDictionary<(string, string), List<string>> { [("Entity", "Entity")] = [.. edgeTypes.Keys] }
                            : new OrderedDictionary<(string, string), List<string>> { [("Entity", "Entity")] = [] };

                    // Extract and resolve nodes
                    var (extractedNodes, nodeEpisodeIndexMap) = await NodeOperations.ExtractNodesAsync(
                        clients,
                        new EpisodeOrEpisodes.One { Episode = episode },
                        previousEpisodes,
                        entityTypes,
                        excludedEntityTypes,
                        customExtractionInstructions,
                        cancellationToken);

                    List<EntityNode> identityNodes;
                    if (identityProperties is { Count: > 0 })
                    {
                        identityNodes = [.. extractedNodes.Where(node => DedupHelpers.IdentityTypeForNode(node, identityProperties) is not null)];
                    }
                    else
                    {
                        identityNodes = [];
                    }

                    if (identityNodes.Count > 0)
                    {
                        await NodeOperations.ExtractAttributesOnlyFromNodesAsync(
                            clients,
                            identityNodes,
                            new EpisodeOrEpisodes.One { Episode = episode },
                            previousEpisodes,
                            entityTypes,
                            validateShape: false,
                            cancellationToken: cancellationToken);
                    }

                    var (nodes, uuidMap, _) = await NodeOperations.ResolveExtractedNodesAsync(
                        clients,
                        extractedNodes,
                        episode,
                        previousEpisodes,
                        entityTypes,
                        identityProperties: identityProperties,
                        cancellationToken: cancellationToken);

                    // Extract and resolve edges in parallel with attribute extraction
                    var (resolvedEdges, invalidatedEdges, newEdges) = await ExtractAndResolveEdgesAsync(
                        new EpisodeOrEpisodes.One { Episode = episode },
                        extractedNodes,
                        previousEpisodes,
                        edgeTypeMap is { Count: > 0 } ? edgeTypeMap : edgeTypeMapDefault,
                        scopedGroupId,
                        edgeTypes,
                        nodes,
                        uuidMap,
                        customExtractionInstructions: customExtractionInstructions,
                        entityTypes: entityTypes,
                        identityProperties: identityProperties,
                        clients: clients,
                        cancellationToken: cancellationToken);

                    List<EntityEdge> entityEdges = [.. resolvedEdges, .. invalidatedEdges];

                    // Extract node attributes - only pass new edges for summary generation
                    // to avoid duplicating facts that already exist in the graph
                    var hydratedNodes = await NodeOperations.ExtractAttributesFromNodesAsync(
                        clients,
                        nodes,
                        new EpisodeOrEpisodes.One { Episode = episode },
                        previousEpisodes,
                        entityTypes,
                        edges: newEdges,
                        cancellationToken: cancellationToken);

                    // Process and save episode data (including saga association if provided)
                    (var episodicEdges, episode) = await ProcessEpisodeDataAsync(
                        new EpisodeOrEpisodes.One { Episode = episode },
                        hydratedNodes,
                        entityEdges,
                        now,
                        scopedGroupId,
                        saga,
                        sagaPreviousEpisodeUuid,
                        nodeEpisodeIndexMap,
                        clients: clients,
                        cancellationToken: cancellationToken);

                    // Update communities if requested
                    // PORT: after the two-name unpacking of upstream defect U14, the two names hold the two results of
                    // update_community, each a tuple. The names hold open data, and the model reads them (R7).
                    object? communities = new List<CommunityNode>();
                    object? communityEdges = new List<CommunityEdge>();
                    if (updateCommunities)
                    {
                        var communityResults = await Helpers.SemaphoreGatherAsync(
                            [
                                .. nodes.Select<EntityNode, Func<Task<(List<CommunityNode>, List<CommunityEdge>)>>>(
                                    node => () => CommunityOperations.UpdateCommunityAsync(
                                        driver,
                                        clients.LlmClient,
                                        clients.Embedder,
                                        node,
                                        clients: clients,
                                        cancellationToken: cancellationToken)),
                            ],
                            maxCoroutines: MaxCoroutines,
                            cancellationToken: cancellationToken);

                        // PORT: upstream defect U14
                        var (first, second) = Py.Unpack2(communityResults);
                        communities = new PyTuple(first.Item1, first.Item2);
                        communityEdges = new PyTuple(second.Item1, second.Item2);
                    }

                    var end = Py.TimeSeconds();

                    // Add span attributes
                    span.AddAttributes(new OrderedDictionary<string, object?>
                    {
                        ["episode.uuid"] = episode.Uuid,
                        ["episode.source"] = Py.EnumValue(source),
                        ["episode.reference_time"] = referenceTime.IsoFormat(),
                        ["group_id"] = scopedGroupId,
                        ["node.count"] = (long)hydratedNodes.Count,
                        ["edge.count"] = (long)entityEdges.Count,
                        ["edge.invalidated_count"] = (long)invalidatedEdges.Count,
                        ["previous_episodes.count"] = (long)previousEpisodes.Count,
                        ["entity_types.count"] = entityTypes is { Count: > 0 } ? (long)entityTypes.Count : 0L,
                        ["edge_types.count"] = edgeTypes is { Count: > 0 } ? (long)edgeTypes.Count : 0L,
                        ["update_communities"] = updateCommunities,
                        ["communities.count"] = updateCommunities ? Py.Len(communities) : 0L,
                        ["duration_ms"] = (end - start) * 1000,
                    });

                    // PORT: a logger local with IsEnabled for a costly log argument (R5).
                    var logger = GraphitiModule.Logger;
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        var milliseconds = Py.Str((end - start) * 1000);
                        GraphitiModule.Log.CompletedAddEpisode(logger, milliseconds);
                    }

                    return AddEpisodeResults.ModelValidate(new OrderedDictionary<string, object?>
                    {
                        ["episode"] = episode,
                        ["episodic_edges"] = episodicEdges,
                        ["nodes"] = hydratedNodes,
                        ["edges"] = entityEdges,
                        ["communities"] = communities,
                        ["community_edges"] = communityEdges,
                    });
                }
                catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
                {
                    span.SetStatus("error", e.Message);
                    span.RecordException(e);
                    throw;
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Process multiple episodes in bulk and update the graph.
    ///
    /// This method extracts information from multiple episodes, creates nodes and edges,
    /// and updates the graph database accordingly, all in a single batch operation.
    ///
    /// Parameters
    /// ----------
    /// bulk_episodes : list[RawEpisode]
    ///     A list of RawEpisode objects to be processed and added to the graph.
    /// group_id : str | None
    ///     An id for the graph partition the episode is a part of.
    /// entity_types : dict[str, type[BaseModel]] | None
    ///     Optional. A dictionary mapping entity type names to Pydantic models.
    /// excluded_entity_types : list[str] | None
    ///     Optional. A list of entity type names to exclude from extraction.
    /// edge_types : dict[str, type[BaseModel]] | None
    ///     Optional. A dictionary mapping edge type names to Pydantic models.
    /// edge_type_map : dict[tuple[str, str], list[str]] | None
    ///     Optional. A mapping of (source_type, target_type) to allowed edge types.
    /// strict_edge_types : bool
    ///     Optional. Whether to discard extracted edges that do not use one of the
    ///     provided edge_types.
    /// custom_extraction_instructions : str | None
    ///     Optional. Custom extraction instructions string to be included in the
    ///     extract entities and extract edges prompts. This allows for additional
    ///     instructions or context to guide the extraction process.
    /// saga : str | SagaNode | None
    ///     Optional. Either a saga name (str) or a SagaNode object to associate all episodes with.
    ///     If a string is provided and a saga with this name already exists in the group, the episodes
    ///     will be added to it. Otherwise, a new saga will be created. Sagas are connected to episodes
    ///     via HAS_EPISODE edges, and consecutive episodes are linked via NEXT_EPISODE edges.
    /// use_combined_extraction : bool
    ///     If True, use one combined node-and-edge LLM call per episode. If False, use separate
    ///     node and edge extraction.
    /// identity_properties : dict[str, list[str]] | None
    ///     Optional. Map entity type names to attribute names that identify entities.
    ///     Graphiti extracts these attributes before node resolution.
    ///
    /// Returns
    /// -------
    /// AddBulkEpisodeResults
    ///
    /// Notes
    /// -----
    /// This method performs several steps including:
    /// - Saving all episodes to the database
    /// - Retrieving previous episode context for each new episode
    /// - Extracting nodes and edges from all episodes
    /// - Generating embeddings for nodes and edges
    /// - Deduplicating nodes and edges
    /// - Saving nodes, episodic edges, and entity edges to the knowledge graph
    ///
    /// This bulk operation is designed for efficiency when processing multiple episodes
    /// at once. However, it's important to ensure that the bulk operation doesn't
    /// overwhelm system resources. Consider implementing rate limiting or chunking for
    /// very large batches of episodes.
    ///
    /// Edge invalidation and date extraction (``valid_at`` / ``invalid_at``) are
    /// performed in the bulk path as well: edges flow through ``extract_edges`` and
    /// ``resolve_extracted_edges`` just like in ``add_episode``, and any invalidated
    /// edges are persisted alongside the newly resolved ones.
    /// </summary>
    internal async Task<AddBulkEpisodeResults> AddEpisodeBulkAsync(
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
        if (ConcurrentIngestion is not null)
        {
            var (concurrentGroupId, concurrentDriver, _) = ResolveRequestScope(groupId);
            if (ConcurrentIngestionEnabled(concurrentGroupId))
            {
                await GraphitiModule.ValidateSagaScopeAsync(saga, concurrentGroupId, concurrentDriver, cancellationToken);
                EntityTypesUtils.ValidateEntityTypes(entityTypes);
                EntityTypesUtils.ValidateIdentityProperties(identityProperties, entityTypes);
                List<string> episodeUuids = [];
                foreach (var episode in bulkEpisodes)
                {
                    if (episode.Uuid is not null)
                    {
                        episodeUuids.Add(episode.Uuid);
                    }
                }

                if (episodeUuids.Count > 0)
                {
                    await EpisodicNode.GetByUuidsAsync(
                        concurrentDriver,
                        episodeUuids,
                        groupId: concurrentGroupId,
                        requireAll: true,
                        cancellationToken: cancellationToken);
                }

                return await GetConcurrentIngestor().AddEpisodeBulkAsync(
                    bulkEpisodes: bulkEpisodes,
                    groupId: concurrentGroupId,
                    entityTypes: entityTypes,
                    excludedEntityTypes: excludedEntityTypes,
                    edgeTypes: edgeTypes,
                    edgeTypeMap: edgeTypeMap,
                    strictEdgeTypes: strictEdgeTypes,
                    customExtractionInstructions: customExtractionInstructions,
                    saga: saga,
                    useCombinedExtraction: useCombinedExtraction,
                    identityProperties: identityProperties,
                    cancellationToken: cancellationToken);
            }
        }

        return await Tracer.StartSpanAsync(
            "add_episode_bulk",
            async bulkSpan =>
            {
                bulkSpan.AddAttributes(new OrderedDictionary<string, object?> { ["episode.count"] = (long)bulkEpisodes.Count });

                try
                {
                    var start = Py.TimeSeconds();
                    var now = DatetimeUtils.UtcNow();
                    EntityTypesUtils.ValidateEntityTypes(entityTypes);
                    EntityTypesUtils.ValidateIdentityProperties(identityProperties, entityTypes);

                    // Resolve a request-scoped driver/clients bundle rather than mutating
                    // the shared ``self.driver``, so concurrent calls for different
                    // group_ids cannot clobber each other's database target (issue #1676).
                    var (scopedGroupId, driver, clients) = ResolveRequestScope(groupId);
                    await GraphitiModule.ValidateSagaScopeAsync(saga, scopedGroupId, driver, cancellationToken);

                    // Create default edge type map
                    var edgeTypeMapDefault =
                        edgeTypes is not null
                            ? new OrderedDictionary<(string, string), List<string>> { [("Entity", "Entity")] = [.. edgeTypes.Keys] }
                            : new OrderedDictionary<(string, string), List<string>> { [("Entity", "Entity")] = [] };

                    List<EpisodicNode> episodes = [];
                    foreach (var episode in bulkEpisodes)
                    {
                        episodes.Add(
                            episode.Uuid is not null
                                ? await EpisodicNode.GetByUuidAsync(driver, episode.Uuid, groupId: scopedGroupId, cancellationToken: cancellationToken)
                                : new EpisodicNode
                                {
                                    Name = episode.Name,
                                    Labels = [],
                                    Source = episode.Source,
                                    Content = episode.Content,
                                    SourceDescription = episode.SourceDescription,
                                    GroupId = scopedGroupId,
                                    CreatedAt = now,
                                    ValidAt = episode.ReferenceTime,
                                }.Validate());
                    }

                    // Save all episodes
                    await BulkUtils.AddNodesAndEdgesBulkAsync(
                        driver: driver,
                        episodicNodes: episodes,
                        episodicEdges: [],
                        entityNodes: [],
                        entityEdges: [],
                        embedder: clients.Embedder,
                        cancellationToken: cancellationToken);

                    // Get previous episode context for each episode
                    var episodeContext = await BulkUtils.RetrievePreviousEpisodesBulkAsync(
                        driver, episodes, maxCoroutines: MaxCoroutines, cancellationToken: cancellationToken);

                    // Extract and dedupe nodes and edges
                    var (nodesByEpisode, uuidMap, extractedEdgesBulk) = await ExtractAndDedupeNodesBulkAsync(
                        episodeContext,
                        edgeTypeMap is { Count: > 0 } ? edgeTypeMap : edgeTypeMapDefault,
                        edgeTypes,
                        entityTypes,
                        excludedEntityTypes,
                        strictEdgeTypes: strictEdgeTypes,
                        customExtractionInstructions: customExtractionInstructions,
                        useCombinedExtraction: useCombinedExtraction,
                        identityProperties: identityProperties,
                        clients: clients,
                        cancellationToken: cancellationToken);

                    // Create Episodic Edges
                    List<EpisodicEdge> episodicEdges = [];
                    foreach (var (episodeUuid, nodes) in nodesByEpisode)
                    {
                        episodicEdges.AddRange(EdgeOperations.BuildEpisodicEdges(nodes, episodeUuid, now));
                    }

                    // Re-map edge pointers and dedupe edges
                    List<List<EntityEdge>> extractedEdgesBulkUpdated =
                    [
                        .. extractedEdgesBulk.Select(edges => BulkUtils.ResolveEdgePointers(edges, uuidMap)),
                    ];

                    var edgesByEpisode = await BulkUtils.DedupeEdgesBulkAsync(
                        clients,
                        extractedEdgesBulkUpdated,
                        episodeContext,
                        [],
                        edgeTypes is { Count: > 0 } ? edgeTypes : new OrderedDictionary<string, GraphTypeDescriptor>(),
                        edgeTypeMap is { Count: > 0 } ? edgeTypeMap : edgeTypeMapDefault,
                        cancellationToken);

                    // Resolve nodes and edges against the existing graph
                    var (finalHydratedNodes, resolvedEdges, invalidatedEdges, finalUuidMap) = await ResolveNodesAndEdgesBulkAsync(
                        nodesByEpisode,
                        edgesByEpisode,
                        episodeContext,
                        entityTypes,
                        edgeTypes,
                        edgeTypeMap is { Count: > 0 } ? edgeTypeMap : edgeTypeMapDefault,
                        episodes,
                        identityProperties: identityProperties,
                        clients: clients,
                        cancellationToken: cancellationToken);

                    // Resolved pointers for episodic edges
                    var resolvedEpisodicEdges = BulkUtils.ResolveEdgePointers(episodicEdges, finalUuidMap);

                    List<EntityEdge> edgesToSave = [.. resolvedEdges, .. invalidatedEdges];
                    foreach (var episode in episodes)
                    {
                        // PORT: `list(dict.fromkeys(...))` keeps the first of each UUID in order (R14).
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        List<string> entityEdgeUuids = [];
                        foreach (var edge in edgesToSave)
                        {
                            if (edge.Episodes.Contains(episode.Uuid) && seen.Add(edge.Uuid))
                            {
                                entityEdgeUuids.Add(edge.Uuid);
                            }
                        }

                        episode.EntityEdges = entityEdgeUuids;
                    }

                    // save data to KG
                    await BulkUtils.AddNodesAndEdgesBulkAsync(
                        driver,
                        episodes,
                        resolvedEpisodicEdges,
                        finalHydratedNodes,
                        edgesToSave,
                        clients.Embedder,
                        cancellationToken: cancellationToken);

                    // Handle saga association if provided
                    if (saga is not null)
                    {
                        // Get or create saga node based on input type
                        SagaNode sagaNode;
                        if (saga is SagaOrName.Name sagaName)
                        {
                            // Anchor a newly minted saga to the earliest episode
                            // reference time in the bulk, so created_at reflects the
                            // episode window rather than the time this run started.
                            // PORT: `valid_at` is required, so the filter `is not None` keeps each episode. `min(xs)`
                            // keeps the first of equal items and compares with `<` (R14).
                            List<PyDateTime> validAts = [.. episodes.Select(ep => ep.ValidAt)];
                            var sagaCreatedAt = now;
                            if (validAts.Count > 0)
                            {
                                sagaCreatedAt = validAts[0];
                                foreach (var validAt in validAts)
                                {
                                    if (validAt < sagaCreatedAt)
                                    {
                                        sagaCreatedAt = validAt;
                                    }
                                }
                            }

                            sagaNode = await GetOrCreateSagaAsync(
                                sagaName.Value, scopedGroupId, sagaCreatedAt, driver: driver, cancellationToken: cancellationToken);
                        }
                        else
                        {
                            sagaNode = ((SagaOrName.Node)saga).Saga;
                        }

                        // Sort episodes by valid_at to create NEXT_EPISODE chain in correct order
                        // PORT: a sort with a datetime key is Py.SortInPlace on a copy (R14).
                        var sortedEpisodes = new List<EpisodicNode>(episodes);
                        Py.SortInPlace(sortedEpisodes, e => e.ValidAt);

                        // Find the most recent episode already in the saga
                        var previousEpisodeUuid = await SagaGetPreviousEpisodeUuidAsync(
                            sagaNode.Uuid, string.Empty, scopedGroupId, driver: driver, cancellationToken: cancellationToken);

                        foreach (var episode in sortedEpisodes)
                        {
                            // Create NEXT_EPISODE edge from the previous episode
                            if (previousEpisodeUuid is not null)
                            {
                                var nextEpisodeEdge = new NextEpisodeEdge
                                {
                                    SourceNodeUuid = previousEpisodeUuid,
                                    TargetNodeUuid = episode.Uuid,
                                    GroupId = scopedGroupId,
                                    CreatedAt = now,
                                }.Validate();
                                await nextEpisodeEdge.SaveAsync(driver, cancellationToken);
                            }

                            // Create HAS_EPISODE edge from saga to episode
                            var hasEpisodeEdge = new HasEpisodeEdge
                            {
                                SourceNodeUuid = sagaNode.Uuid,
                                TargetNodeUuid = episode.Uuid,
                                GroupId = scopedGroupId,
                                CreatedAt = now,
                            }.Validate();
                            await hasEpisodeEdge.SaveAsync(driver, cancellationToken);

                            // Update previous_episode_uuid for the next iteration
                            previousEpisodeUuid = episode.Uuid;
                        }

                        // Track first and last episode on the saga node
                        if (sortedEpisodes.Count > 0)
                        {
                            if (sagaNode.FirstEpisodeUuid is null)
                            {
                                sagaNode.FirstEpisodeUuid = sortedEpisodes[0].Uuid;
                            }

                            sagaNode.LastEpisodeUuid = sortedEpisodes[^1].Uuid;
                            await sagaNode.SaveAsync(driver, cancellationToken);
                        }
                    }

                    var end = Py.TimeSeconds();

                    // Add span attributes
                    bulkSpan.AddAttributes(new OrderedDictionary<string, object?>
                    {
                        ["group_id"] = scopedGroupId,
                        ["node.count"] = (long)finalHydratedNodes.Count,
                        ["edge.count"] = (long)(resolvedEdges.Count + invalidatedEdges.Count),
                        ["duration_ms"] = (end - start) * 1000,
                    });

                    // PORT: a logger local with IsEnabled for a costly log argument (R5).
                    var logger = GraphitiModule.Logger;
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        var milliseconds = Py.Str((end - start) * 1000);
                        GraphitiModule.Log.CompletedAddEpisodeBulk(logger, milliseconds);
                    }

                    return new AddBulkEpisodeResults
                    {
                        Episodes = episodes,
                        EpisodicEdges = resolvedEpisodicEdges,
                        Nodes = finalHydratedNodes,
                        Edges = [.. resolvedEdges, .. invalidatedEdges],
                        Communities = [],
                        CommunityEdges = [],
                    }.Validate();
                }
                catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
                {
                    bulkSpan.SetStatus("error", e.Message);
                    bulkSpan.RecordException(e);
                    throw;
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Use a community clustering algorithm to find communities of nodes. Create community nodes summarising
    /// the content of these communities.
    /// ----------
    /// group_ids : list[str] | None
    ///     Optional. Create communities only for the listed group_ids. If blank the entire graph will be used.
    /// </summary>
    // PORT: @handle_multiple_group_ids (R16). The tuple overload of the wrapper merges the two lists.
    internal Task<(List<CommunityNode> First, List<CommunityEdge> Second)> BuildCommunitiesAsync(
        List<string>? groupIds = null,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        return Decorators.HandleMultipleGroupIdsAsync(
            Clients?.Driver,
            MaxCoroutines,
            groupIds,
            driver,
            (ids, d) => BuildCommunitiesCoreAsync(ids, d, cancellationToken),
            cancellationToken);
    }

    // PORT: the body of the decorated build_communities (R16).
    private async Task<(List<CommunityNode> First, List<CommunityEdge> Second)> BuildCommunitiesCoreAsync(
        List<string>? groupIds = null,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        if (driver is null)
        {
            driver = Clients.Driver;
        }

        // Clear existing communities (scoped: only the groups being rebuilt)
        await CommunityOperations.RemoveCommunitiesAsync(driver, groupIds: groupIds, cancellationToken: cancellationToken);

        // Prefer the resolved clients bundle (llm_runtime / prompt_library),
        // but honor an explicit driver override when provided.
        var clients = Clients;
        // PORT: `driver is not None` is always true here, after the test above. The port keeps only the identity test,
        // so that the compiler keeps the driver known as not null.
        if (!ReferenceEquals(driver, Driver))
        {
            clients = Clients with { Driver = driver };
        }

        var (communityNodes, communityEdges) = await CommunityOperations.BuildCommunitiesAsync(
            clients.Driver,
            clients.LlmClient,
            groupIds,
            clients: clients,
            maxCoroutines: MaxCoroutines,
            cancellationToken: cancellationToken);

        await Helpers.SemaphoreGatherAsync(
            [.. communityNodes.Select<CommunityNode, Func<Task<List<double>>>>(node => () => node.GenerateNameEmbeddingAsync(Embedder, cancellationToken))],
            maxCoroutines: MaxCoroutines,
            cancellationToken: cancellationToken);

        await Helpers.SemaphoreGatherAsync(
            [.. communityNodes.Select<CommunityNode, Func<Task>>(node => () => node.SaveAsync(driver, cancellationToken))],
            maxCoroutines: MaxCoroutines,
            cancellationToken: cancellationToken);
        await Helpers.SemaphoreGatherAsync(
            [.. communityEdges.Select<CommunityEdge, Func<Task>>(edge => () => edge.SaveAsync(driver, cancellationToken))],
            maxCoroutines: MaxCoroutines,
            cancellationToken: cancellationToken);

        return (communityNodes, communityEdges);
    }

    /// <summary>
    /// Perform a hybrid search on the knowledge graph.
    ///
    /// This method executes a search query on the graph, combining vector and
    /// text-based search techniques to retrieve relevant facts, returning the edges as a string.
    ///
    /// This is our basic out-of-the-box search, for more robust results we recommend using our more advanced
    /// search method graphiti.search_().
    ///
    /// Parameters
    /// ----------
    /// query : str
    ///     The search query string.
    /// center_node_uuid: str, optional
    ///     Facts will be reranked based on proximity to this node
    /// group_ids : list[str | None] | None, optional
    ///     The graph partitions to return data from.
    /// num_results : int, optional
    ///     The maximum number of results to return. Defaults to 10.
    ///
    /// Returns
    /// -------
    /// list
    ///     A list of EntityEdge objects that are relevant to the search query.
    ///
    /// Notes
    /// -----
    /// This method uses a SearchConfig with num_episodes set to 0 and
    /// num_results set to the provided num_results parameter.
    ///
    /// The search is performed using the current date and time as the reference
    /// point for temporal relevance.
    /// </summary>
    // PORT: @handle_multiple_group_ids (R16). `num_results` has no annotation. TYPE_HINTS.md gives int.
    internal Task<List<EntityEdge>> SearchAsync(
        string query,
        string? centerNodeUuid = null,
        List<string>? groupIds = null,
        long numResults = SearchConfigModule.DefaultSearchLimit,
        SearchFilters? searchFilter = null,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        return Decorators.HandleMultipleGroupIdsAsync(
            Clients?.Driver,
            MaxCoroutines,
            groupIds,
            driver,
            (ids, d) => SearchCoreAsync(query, centerNodeUuid, ids, numResults, searchFilter, d, cancellationToken),
            cancellationToken);
    }

    // PORT: the body of the decorated search (R16).
    private async Task<List<EntityEdge>> SearchCoreAsync(
        string query,
        string? centerNodeUuid = null,
        List<string>? groupIds = null,
        long numResults = SearchConfigModule.DefaultSearchLimit,
        SearchFilters? searchFilter = null,
        GraphDriver? driver = null,
        CancellationToken cancellationToken = default)
    {
        var searchConfig =
            centerNodeUuid is null ? SearchConfigRecipes.EdgeHybridSearchRrf : SearchConfigRecipes.EdgeHybridSearchNodeDistance;

        // PORT: upstream defect U8. The call writes the limit into the shared recipe object (R17).
        searchConfig.Limit = numResults;

        var edges = (
            await SearchModule.SearchAsync(
                Clients,
                query,
                groupIds,
                searchConfig,
                searchFilter ?? new SearchFilters().Validate(),
                driver: driver,
                centerNodeUuid: centerNodeUuid,
                cancellationToken: cancellationToken)).Edges;

        return edges;
    }

    /// <summary>DEPRECATED</summary>
    // PORT: `_search` and `search` give the same C# name, so the private one has the prefix Private (R2). It is
    // internal, because the recording test calls it (R2).
    internal async Task<SearchResults> PrivateSearchAsync(
        string query,
        SearchConfig config,
        List<string>? groupIds = null,
        string? centerNodeUuid = null,
        List<string>? bfsOriginNodeUuids = null,
        SearchFilters? searchFilter = null,
        CancellationToken cancellationToken = default)
    {
        return await AdvancedSearchAsync(
            query, config, groupIds, centerNodeUuid, bfsOriginNodeUuids, searchFilter, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// search_ (replaces _search) is our advanced search method that returns Graph objects (nodes and edges) rather
    /// than a list of facts. This endpoint allows the end user to utilize more advanced features such as filters and
    /// different search and reranker methodologies across different layers in the graph.
    ///
    /// For different config recipes refer to search/search_config_recipes.
    /// </summary>
    // PORT: @handle_multiple_group_ids (R16). `search_` gives the C# name of `search`, so it has the
    // name of the public facade (R2, and the public surface design, L10). The default of `config` is
    // the shared recipe object COMBINED_HYBRID_SEARCH_CROSS_ENCODER, which cannot be a C# default value: null stands
    // for SearchConfigRecipes.CombinedHybridSearchCrossEncoder, the same object (R17, R21 U8).
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
        return Decorators.HandleMultipleGroupIdsAsync(
            Clients?.Driver,
            MaxCoroutines,
            groupIds,
            driver,
            (ids, d) => AdvancedSearchCoreAsync(query, config, ids, centerNodeUuid, bfsOriginNodeUuids, searchFilter, d, queryVector, cancellationToken),
            cancellationToken);
    }

    // PORT: the body of the decorated search_ (R16).
    private async Task<SearchResults> AdvancedSearchCoreAsync(
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
        return await SearchModule.SearchAsync(
            Clients,
            query,
            groupIds,
            config ?? SearchConfigRecipes.CombinedHybridSearchCrossEncoder,
            searchFilter ?? new SearchFilters().Validate(),
            centerNodeUuid,
            bfsOriginNodeUuids,
            queryVector: queryVector,
            driver: driver,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get nodes and edges that episodes reference.
    ///
    /// Parameters
    /// ----------
    /// episode_uuids : list[str]
    ///     The episode UUIDs.
    /// group_id : str | None
    ///     If set, episodes must belong to this group or NodeNotFoundError is raised. Only nodes
    ///     and edges in this group are returned. If None, the method does not check the group of
    ///     the episode UUIDs. Multi-tenant callers must pass group_id.
    ///
    /// Raises
    /// ------
    /// NodeNotFoundError
    ///     If an episode is not found in the selected group.
    /// </summary>
    internal async Task<SearchResults> GetNodesAndEdgesByEpisodeAsync(
        List<string> episodeUuids,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        var driver = Driver;
        if (groupId is not null)
        {
            (groupId, driver, _) = ResolveRequestScope(groupId);
        }

        var episodes = await EpisodicNode.GetByUuidsAsync(
            driver,
            episodeUuids,
            groupId: groupId,
            requireAll: groupId is not null,
            cancellationToken: cancellationToken);

        var edgesList = await Helpers.SemaphoreGatherAsync(
            [
                .. episodes.Select<EpisodicNode, Func<Task<List<EntityEdge>>>>(
                    episode => () => EntityEdge.GetByUuidsAsync(driver, episode.EntityEdges, groupId: groupId, cancellationToken: cancellationToken)),
            ],
            maxCoroutines: MaxCoroutines,
            cancellationToken: cancellationToken);

        var allowedGroupIds = episodes.Select(episode => episode.GroupId).ToHashSet(StringComparer.Ordinal);
        List<EntityEdge> edges = [.. edgesList.SelectMany(lst => lst).Where(edge => allowedGroupIds.Contains(edge.GroupId))];

        List<EntityNode> nodes =
        [
            .. (await SearchUtils.GetMentionedNodesAsync(driver, episodes, cancellationToken))
                .Where(node => allowedGroupIds.Contains(node.GroupId)),
        ];

        return new SearchResults { Edges = edges, Nodes = nodes }.Validate();
    }

    /// <summary>
    /// Add a node-edge-node triplet and resolve nodes by configured identity properties.
    ///
    /// Pass ``identity_properties`` to match caller-provided node attributes against
    /// existing entities. The caller must supply these attributes on the nodes.
    /// </summary>
    internal async Task<AddTripletResults> AddTripletAsync(
        EntityNode sourceNode,
        EntityEdge edge,
        EntityNode targetNode,
        EpisodicNode? episode = null,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var (valueName, value) in new (string, Node?)[] { ("source node", sourceNode), ("target node", targetNode), ("episode", episode) })
        {
            if (value is not null && value.GroupId != edge.GroupId)
            {
                throw new ArgumentException(valueName + " " + value.Uuid + " does not belong to group " + edge.GroupId);
            }
        }

        if (edge.Episodes.Count > 0)
        {
            await EpisodicNode.GetByUuidsAsync(
                Driver,
                edge.Episodes,
                groupId: edge.GroupId,
                requireAll: true,
                cancellationToken: cancellationToken);
        }

        if (sourceNode.NameEmbedding is null)
        {
            await sourceNode.GenerateNameEmbeddingAsync(Embedder, cancellationToken);
        }

        if (targetNode.NameEmbedding is null)
        {
            await targetNode.GenerateNameEmbeddingAsync(Embedder, cancellationToken);
        }

        if (edge.FactEmbedding is null)
        {
            await edge.GenerateEmbeddingAsync(Embedder, cancellationToken);
        }

        EntityNode resolvedSource;
        try
        {
            resolvedSource = await EntityNode.GetByUuidAsync(Driver, sourceNode.Uuid, cancellationToken);
        }
        catch (NodeNotFoundException)
        {
            List<EntityNode> resolvedSourceNodes;
            if (identityProperties is { Count: > 0 })
            {
                (resolvedSourceNodes, _, _) = await NodeOperations.ResolveExtractedNodesAsync(
                    Clients,
                    [sourceNode],
                    identityProperties: identityProperties,
                    cancellationToken: cancellationToken);
            }
            else
            {
                (resolvedSourceNodes, _, _) = await NodeOperations.ResolveExtractedNodesAsync(
                    Clients,
                    [sourceNode],
                    cancellationToken: cancellationToken);
            }

            resolvedSource = resolvedSourceNodes[0];
        }

        if (resolvedSource.GroupId != edge.GroupId)
        {
            throw new NodeNotFoundException(sourceNode.Uuid);
        }

        EntityNode resolvedTarget;
        try
        {
            resolvedTarget = await EntityNode.GetByUuidAsync(Driver, targetNode.Uuid, cancellationToken);
        }
        catch (NodeNotFoundException)
        {
            List<EntityNode> resolvedTargetNodes;
            if (identityProperties is { Count: > 0 })
            {
                (resolvedTargetNodes, _, _) = await NodeOperations.ResolveExtractedNodesAsync(
                    Clients,
                    [targetNode],
                    identityProperties: identityProperties,
                    cancellationToken: cancellationToken);
            }
            else
            {
                (resolvedTargetNodes, _, _) = await NodeOperations.ResolveExtractedNodesAsync(
                    Clients,
                    [targetNode],
                    cancellationToken: cancellationToken);
            }

            resolvedTarget = resolvedTargetNodes[0];
        }

        if (resolvedTarget.GroupId != edge.GroupId)
        {
            throw new NodeNotFoundException(targetNode.Uuid);
        }

        List<EntityNode> nodes = [resolvedSource, resolvedTarget];

        // Merge user-provided properties from original nodes into resolved nodes (excluding uuid)
        // Update attributes dictionary (merge rather than replace)
        // PORT: the resolved node can be the given node itself, so the loop reads a copy of the items: a C# collection
        // refuses a change during its enumeration, where `d.update(d)` changes nothing.
        if (sourceNode.Attributes.Count > 0)
        {
            foreach (var (key, value) in sourceNode.Attributes.ToList())
            {
                resolvedSource.Attributes[key] = value;
            }
        }

        if (targetNode.Attributes.Count > 0)
        {
            foreach (var (key, value) in targetNode.Attributes.ToList())
            {
                resolvedTarget.Attributes[key] = value;
            }
        }

        // Update summary if provided by user (non-empty string)
        if (!string.IsNullOrEmpty(sourceNode.Summary))
        {
            resolvedSource.Summary = sourceNode.Summary;
        }

        if (!string.IsNullOrEmpty(targetNode.Summary))
        {
            resolvedTarget.Summary = targetNode.Summary;
        }

        // Update labels (merge with existing)
        // PORT: `list(set(a) | set(b))` gives `Entity` first if present, then the labels of `a` in order, then the new
        // labels of `b` in order (R14).
        if (sourceNode.Labels.Count > 0)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> labels = [];
            if (resolvedSource.Labels.Contains("Entity") || sourceNode.Labels.Contains("Entity"))
            {
                seen.Add("Entity");
                labels.Add("Entity");
            }

            foreach (var label in resolvedSource.Labels.Concat(sourceNode.Labels))
            {
                if (seen.Add(label))
                {
                    labels.Add(label);
                }
            }

            resolvedSource.Labels = labels;
        }

        if (targetNode.Labels.Count > 0)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> labels = [];
            if (resolvedTarget.Labels.Contains("Entity") || targetNode.Labels.Contains("Entity"))
            {
                seen.Add("Entity");
                labels.Add("Entity");
            }

            foreach (var label in resolvedTarget.Labels.Concat(targetNode.Labels))
            {
                if (seen.Add(label))
                {
                    labels.Add(label);
                }
            }

            resolvedTarget.Labels = labels;
        }

        edge.SourceNodeUuid = resolvedSource.Uuid;
        edge.TargetNodeUuid = resolvedTarget.Uuid;

        // Check if an edge with this UUID already exists with different source/target nodes.
        // If so, generate a new UUID to create a new edge instead of overwriting.
        // PORT: try, except, else (Part 3). The else body follows the call inside the try.
        var returned = false;
        try
        {
            // Keep this lookup unscoped so the group check catches a foreign edge before MERGE overwrites it.
            var existingEdge = await EntityEdge.GetByUuidAsync(Driver, edge.Uuid, cancellationToken: cancellationToken);
            returned = true;
            if (existingEdge.GroupId != edge.GroupId)
            {
                throw new EdgeNotFoundException(edge.Uuid);
            }

            // Edge exists - check if source/target nodes match
            if (
                existingEdge.SourceNodeUuid != edge.SourceNodeUuid
                || existingEdge.TargetNodeUuid != edge.TargetNodeUuid)
            {
                // Source/target mismatch - generate new UUID to create a new edge
                var oldUuid = edge.Uuid;
                edge.Uuid = Py.Uuid4();
                GraphitiModule.Log.EdgeUuidAlreadyExists(GraphitiModule.Logger, oldUuid, edge.Uuid);
            }
        }
        catch (EdgeNotFoundException) when (!returned)
        {
            // Edge doesn't exist yet, proceed normally
            // PORT: Python passes
        }

        // Duplicate candidates: a hybrid search pre-filtered to edges between
        // the pair's endpoints (either orientation) -- the backend ranks over
        // the pair's edges only, never a graph-wide scan. The post-filter
        // drops self-loops the {a,b}x{a,b} filter admits. Mirrors
        // resolve_extracted_edges.
        var relatedSearch = await SearchModule.SearchAsync(
            Clients,
            edge.Fact,
            groupIds: [edge.GroupId],
            config: SearchConfigRecipes.EdgeHybridSearchRrf,
            searchFilter: EdgeOperations.PairSearchFilter(edge),
            queryVector: edge.FactEmbedding,
            cancellationToken: cancellationToken);
        List<EntityEdge> relatedEdges =
        [
            .. EdgeOperations.SamePairEdges(edge, relatedSearch.Edges).Where(candidate => candidate.GroupId == edge.GroupId),
        ];
        var existingSearch = await SearchModule.SearchAsync(
            Clients,
            edge.Fact,
            groupIds: [edge.GroupId],
            config: SearchConfigRecipes.EdgeHybridSearchRrf,
            searchFilter: new SearchFilters().Validate(),
            queryVector: edge.FactEmbedding,
            cancellationToken: cancellationToken);
        List<EntityEdge> existingEdges = [.. existingSearch.Edges.Where(candidate => candidate.GroupId == edge.GroupId)];

        // Keep edges that are already duplicate candidates out of the
        // invalidation list so the LLM is not shown the same fact under two
        // indices. Mirrors resolve_extracted_edges' overlap dedup.
        var relatedUuids = relatedEdges.Select(relatedEdge => relatedEdge.Uuid).ToHashSet(StringComparer.Ordinal);
        existingEdges = [.. existingEdges.Where(e => !relatedUuids.Contains(e.Uuid))];

        // PORT: `edge.valid_at or utc_now()`. A datetime is always true, so only None takes the clock.
        var dedupeEpisode =
            episode
            ?? new EpisodicNode
            {
                Name = string.Empty,
                Source = EpisodeType.Text,
                SourceDescription = string.Empty,
                Content = string.Empty,
                ValidAt = edge.ValidAt ?? DatetimeUtils.UtcNow(),
                EntityEdges = [],
                GroupId = edge.GroupId,
            }.Validate();
        var (resolvedEdge, invalidatedEdges, _) = await EdgeOperations.ResolveExtractedEdgeAsync(
            LlmClient,
            edge,
            relatedEdges,
            existingEdges,
            dedupeEpisode,
            null,
            clients: Clients,
            cancellationToken: cancellationToken);

        List<EntityEdge> edges = [resolvedEdge, .. invalidatedEdges];
        if (edges.Any(candidate => candidate.GroupId != edge.GroupId))
        {
            throw new ArgumentException("triplet resolution crossed the requested group boundary");
        }

        await global::Graphiti.Core.Edges.CreateEntityEdgeEmbeddingsAsync(Embedder, edges, cancellationToken);
        await global::Graphiti.Core.Nodes.CreateEntityNodeEmbeddingsAsync(Embedder, nodes, cancellationToken);

        await BulkUtils.AddNodesAndEdgesBulkAsync(Driver, [], [], nodes, edges, Embedder, cancellationToken: cancellationToken);
        return new AddTripletResults { Edges = edges, Nodes = nodes }.Validate();
    }

    /// <summary>
    /// Remove an episode and its related nodes and edges.
    ///
    /// Parameters
    /// ----------
    /// episode_uuid : str
    ///     The episode UUID.
    /// group_id : str | None
    ///     If set, the episode must belong to this group or NodeNotFoundError is raised. Only nodes
    ///     and edges in this group are changed or deleted. If None, the method does not check the
    ///     group of the episode UUID. Multi-tenant callers must pass group_id.
    ///
    /// Raises
    /// ------
    /// NodeNotFoundError
    ///     If the episode is not found in the selected group.
    /// </summary>
    internal async Task RemoveEpisodeAsync(
        string episodeUuid,
        string? groupId = null,
        CancellationToken cancellationToken = default)
    {
        var driver = Driver;
        var clients = Clients;
        if (groupId is not null)
        {
            (groupId, driver, clients) = ResolveRequestScope(groupId);
        }

        // Find the episode to be deleted
        var episode = await EpisodicNode.GetByUuidAsync(driver, episodeUuid, groupId: groupId, cancellationToken: cancellationToken);
        groupId = episode.GroupId;

        // Find edges mentioned by the episode
        List<EntityEdge> edges =
        [
            .. (await EntityEdge.GetByUuidsAsync(driver, episode.EntityEdges, groupId: groupId, cancellationToken: cancellationToken))
                .Where(edge => edge.GroupId == groupId),
        ];

        // Keep edges that other episodes also support.
        List<EntityEdge> edgesToDelete = [];
        List<EntityEdge> edgesToUpdate = [];
        foreach (var edge in edges)
        {
            if (edge.Episodes.Contains(episode.Uuid))
            {
                edge.Episodes = [.. edge.Episodes.Where(uuid => uuid != episode.Uuid)];
                if (edge.Episodes.Count > 0)
                {
                    edgesToUpdate.Add(edge);
                }
                else
                {
                    edgesToDelete.Add(edge);
                }
            }
        }

        // Find nodes mentioned by the episode
        List<EntityNode> nodes =
        [
            .. (await SearchUtils.GetMentionedNodesAsync(driver, [episode], cancellationToken))
                .Where(node => node.GroupId == groupId),
        ];

        // We should delete all nodes that are only mentioned in the deleted episode
        List<EntityNode> nodesToDelete = [];
        foreach (var node in nodes)
        {
            var query =
                "MATCH (e:Episodic {group_id: $group_id})-[:MENTIONS]->"
                + "(n:Entity {uuid: $uuid, group_id: $group_id}) "
                + "RETURN count(*) AS episode_count";
            var (records, _, _) = await driver.ExecuteQueryAsync(
                query,
                new OrderedDictionary<string, object?>
                {
                    ["uuid"] = node.Uuid,
                    ["group_id"] = groupId,
                    ["routing_"] = "r",
                },
                cancellationToken);

            foreach (var record in records)
            {
                // PORT: `==` on a value of open data is PlainEqual (R18).
                if (PyValueEquality.PlainEqual(record["episode_count"], 1L))
                {
                    nodesToDelete.Add(node);
                }
            }
        }

        var deletedNodeUuids = nodesToDelete.Select(node => node.Uuid).ToHashSet(StringComparer.Ordinal);
        List<EntityNode> survivingNodes = [.. nodes.Where(node => !deletedNodeUuids.Contains(node.Uuid))];

        foreach (var edge in edgesToUpdate)
        {
            await edge.LoadFactEmbeddingAsync(driver, cancellationToken);
            await edge.SaveAsync(driver, cancellationToken);
        }

        await Edge.DeleteByUuidsAsync(driver, [.. edgesToDelete.Select(edge => edge.Uuid)], cancellationToken);
        await Node.DeleteByUuidsAsync(driver, [.. nodesToDelete.Select(node => node.Uuid)], cancellationToken: cancellationToken);
        await episode.DeleteAsync(driver, cancellationToken);

        // PORT: a nested coroutine is a local async function (R8).
        async Task RebuildNodeSummaryAsync(EntityNode node)
        {
            var oldSummary = node.Summary;
            try
            {
                var episodes = await EpisodicNode.GetByEntityNodeUuidAsync(driver, node.Uuid, cancellationToken);

                // PORT: a sort with a datetime key is Py.SortInPlace on a new list (R14). `valid_at` is required and a
                // datetime is always true, so `remaining.valid_at or remaining.created_at` is the valid_at.
                List<EpisodicNode> remainingEpisodes =
                [
                    .. episodes.Where(remaining => remaining.Uuid != episode.Uuid && remaining.GroupId == groupId),
                ];
                Py.SortInPlace(remainingEpisodes, remaining => remaining.ValidAt);
                node.Summary = string.Empty;
                for (long batchStart = 0; batchStart < remainingEpisodes.Count; batchStart += GraphitiModule.SummaryRebuildBatchSize)
                {
                    var episodeBatch = Py.Slice(remainingEpisodes, batchStart, batchStart + GraphitiModule.SummaryRebuildBatchSize);
                    await NodeOperations.ExtractEntitySummariesBatchAsync(
                        clients.LlmClient,
                        [node],
                        new EpisodeOrEpisodes.Many { Episodes = episodeBatch },
                        null,
                        null,
                        new OrderedDictionary<string, List<EntityEdge>>(),
                        skipFactAppending: true,
                        clients: clients,
                        cancellationToken: cancellationToken);
                }

                await node.LoadNameEmbeddingAsync(driver, cancellationToken);
                await node.SaveAsync(driver, cancellationToken);
            }
            catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
            {
                node.Summary = oldSummary;
                GraphitiModule.Log.FailedToRebuildSummary(GraphitiModule.Logger, e, node.Uuid, episode.Uuid);
            }
        }

        await Helpers.SemaphoreGatherAsync(
            [.. survivingNodes.Select<EntityNode, Func<Task>>(node => () => RebuildNodeSummaryAsync(node))],
            maxCoroutines: MaxCoroutines,
            cancellationToken: cancellationToken);
    }
}
