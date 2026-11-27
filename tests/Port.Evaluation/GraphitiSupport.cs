// Fakes and value helpers for the evaluation tests of graphiti_core/graphiti.py (plan 0004, layer 9, packet P9a).
// The fakes replace the graph driver with its session and transaction, the graph operations interface, the LLM client,
// the embedder, the cross-encoder, the tracer, and the state store, as port/tools/record_graphiti.py does in Python.
// Each fake yields once and then records its call (R25).
namespace Graphiti.Port.Evaluation;

using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.Ingestion;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;
using static Graphiti.Port.Evaluation.PhasesValues;

/// <summary>The values of the recording and the views that the recorder writes.</summary>
internal static partial class GraphitiValues
{
    internal static readonly OrderedDictionary<string, object?> Document = Load();

    internal static readonly PyDateTime FixedNow = PyDateTime.FromIsoFormat((string)Document["fixed_now"]!);

    // T2 of the recorder: the created_at that the recorder passes to _get_or_create_saga.
    internal static readonly PyDateTime T2 = PyDateTime.FromIsoFormat("2026-01-21T00:00:00+00:00");

    // The readers of PhasesValues, under the names that the test reads.
    internal static List<object?> List(object? value) => PhasesValues.List(value);

    internal static OrderedDictionary<string, object?> Dict(object? value) => PhasesValues.Dict(value);

    internal static object? Decode(object? value) => PhasesValues.Decode(value);

    internal static OrderedDictionary<string, object?> DecodeDict(OrderedDictionary<string, object?> value) => PhasesValues.DecodeDict(value);

    internal static object? Dt(PyDateTime? value) => PhasesValues.Dt(value);

    // `placeholders` of the recorder: each uuid4 that the code made becomes <new:N> in the order of its first appearance.
    // A key of a dict gets its placeholder too, before its value.
    internal static object? Placeholders(object? value, Dictionary<string, string> known)
    {
        if (value is OrderedDictionary<string, object?> d)
        {
            var output = new OrderedDictionary<string, object?>();
            foreach (var (key, item) in d)
            {
                var name = (string)PhasesValues.Placeholders(key, known)!;
                output[name] = Placeholders(item, known);
            }

            return output;
        }

        return value is List<object?> l ? l.Select(v => Placeholders(v, known)).ToList() : PhasesValues.Placeholders(value, known);
    }

    internal static EntityNode Node(object? data) => PhasesValues.Node(data);

    internal static EntityEdge Edge(object? data) => PhasesValues.Edge(data);

    internal static EpisodicNode Episode(object? data) => PhasesValues.Episode(data);

    // `encode` of the recorder: a datetime becomes {"$dt": ...}, an enum member {"$enum": value}, a dict a dict with
    // string keys, and a tuple or a list a list.
    internal static object? Encode(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b,
        int i => (long)i,
        long l => l,
        float f => (double)f,
        double d => d,
        PyDateTime d => Dt(d),
        Enum e => new OrderedDictionary<string, object?> { ["$enum"] = PyEnum.Value(e) },
        PyTuple t => t.Select(Encode).ToList(),
        System.Collections.IDictionary d => EncodeDictionary(d),
        System.Collections.IEnumerable e => e.Cast<object?>().Select(Encode).ToList(),
        _ => value,
    };

    // `query_view` of the recorder: the SHA-256 of the UTF-8 text, the length in code points, and the first 80
    // characters with the white space joined.
    internal static OrderedDictionary<string, object?> QueryView(string query) => new()
    {
        ["sha"] = Convert.ToHexStringLower(SHA256.HashData(PyText.EncodeUtf8(query))),
        ["len"] = Py.Len(query),
        ["head"] = Py.Slice(Normalize(query), null, 80),
    };

    internal static string Normalize(string query) => string.Join(" ", PyText.Split(query));

    internal static string MaskMs(string message) => MsPattern().Replace(message, "in <ms> ms");

    internal static OrderedDictionary<string, object?> EpisodeView(EpisodicNode episode) => new()
    {
        ["uuid"] = episode.Uuid,
        ["name"] = episode.Name,
        ["group_id"] = episode.GroupId,
        ["content"] = episode.Content,
        ["source"] = Py.EnumValue(episode.Source),
        ["source_description"] = episode.SourceDescription,
        ["valid_at"] = Dt(episode.ValidAt),
        ["created_at"] = Dt(episode.CreatedAt),
        ["entity_edges"] = episode.EntityEdges.Select(x => (object?)x).ToList(),
        ["labels"] = episode.Labels.Select(x => (object?)x).ToList(),
    };

    internal static OrderedDictionary<string, object?> NodeView(EntityNode node) => new()
    {
        ["uuid"] = node.Uuid,
        ["name"] = node.Name,
        ["group_id"] = node.GroupId,
        ["labels"] = node.Labels.Select(x => (object?)x).ToList(),
        ["created_at"] = Dt(node.CreatedAt),
        ["summary"] = node.Summary,
        ["attributes"] = Encode(node.Attributes),
        ["name_embedding"] = node.NameEmbedding?.Select(x => (object?)x).ToList(),
    };

    internal static OrderedDictionary<string, object?> EdgeView(EntityEdge edge) => new()
    {
        ["uuid"] = edge.Uuid,
        ["group_id"] = edge.GroupId,
        ["source_node_uuid"] = edge.SourceNodeUuid,
        ["target_node_uuid"] = edge.TargetNodeUuid,
        ["name"] = edge.Name,
        ["fact"] = edge.Fact,
        ["fact_embedding"] = edge.FactEmbedding?.Select(x => (object?)x).ToList(),
        ["episodes"] = edge.Episodes.Select(x => (object?)x).ToList(),
        ["expired_at"] = Dt(edge.ExpiredAt),
        ["valid_at"] = Dt(edge.ValidAt),
        ["invalid_at"] = Dt(edge.InvalidAt),
        ["attributes"] = Encode(edge.Attributes),
        ["created_at"] = Dt(edge.CreatedAt),
    };

    internal static OrderedDictionary<string, object?> EpisodicEdgeView(EpisodicEdge edge) => new()
    {
        ["uuid"] = edge.Uuid,
        ["group_id"] = edge.GroupId,
        ["source_node_uuid"] = edge.SourceNodeUuid,
        ["target_node_uuid"] = edge.TargetNodeUuid,
        ["created_at"] = Dt(edge.CreatedAt),
    };

    internal static OrderedDictionary<string, object?> SagaView(SagaNode saga) => new()
    {
        ["uuid"] = saga.Uuid,
        ["name"] = saga.Name,
        ["group_id"] = saga.GroupId,
        ["summary"] = saga.Summary,
        ["created_at"] = Dt(saga.CreatedAt),
        ["first_episode_uuid"] = saga.FirstEpisodeUuid,
        ["last_episode_uuid"] = saga.LastEpisodeUuid,
        ["last_summarized_at"] = Dt(saga.LastSummarizedAt),
        ["last_summarized_episode_valid_at"] = Dt(saga.LastSummarizedEpisodeValidAt),
        ["private"] = new List<object?>
        {
            saga.SummaryEpisodesFetched,
            saga.SummaryEpisodesSelected,
            saga.SummaryEpisodesSkipped,
            saga.SummarySelectedEpisodeBytes,
        },
    };

    internal static OrderedDictionary<string, object?> SearchView(SearchResults results) => new()
    {
        ["edges"] = results.Edges.Select(e => (object?)EdgeView(e)).ToList(),
        ["edge_scores"] = results.EdgeRerankerScores.Select(x => (object?)x).ToList(),
        ["nodes"] = results.Nodes.Select(n => (object?)NodeView(n)).ToList(),
        ["node_scores"] = results.NodeRerankerScores.Select(x => (object?)x).ToList(),
        ["episodes"] = results.Episodes.Select(e => (object?)e.Uuid).ToList(),
        ["communities"] = results.Communities.Select(c => (object?)c.Uuid).ToList(),
    };

    // `types_of` of the recorder: the models of the table `types`, and `Bad` with the field `summary` of EntityNode.
    internal static OrderedDictionary<string, GraphTypeDescriptor>? Types(object? names)
    {
        if (names is not List<object?> list)
        {
            return null;
        }

        var table = Dict(Document["types"]);
        var output = new OrderedDictionary<string, GraphTypeDescriptor>();
        foreach (var name in list.Cast<string>())
        {
            if (name == "Bad")
            {
                output[name] = new GraphTypeDescriptor("Bad", null, [new GraphFieldDescriptor("summary", GraphFieldKind.String, false, null, false, null)]);
                continue;
            }

            var spec = Dict(table[name]);
            output[name] = new GraphTypeDescriptor(
                name,
                (string?)spec["doc"],
                List(spec["fields"]).Select(List).Select(f => new GraphFieldDescriptor((string)f[0]!, GraphFieldKind.String, false, null, (bool)f[1]!, (long?)f[2])));
        }

        return output;
    }

    internal static OrderedDictionary<(string, string), List<string>>? TypeMap(object? items)
    {
        if (items is not List<object?> list)
        {
            return null;
        }

        var output = new OrderedDictionary<(string, string), List<string>>();
        foreach (var item in list.Select(List))
        {
            var key = List(item[0]);
            output[((string)key[0]!, (string)key[1]!)] = [.. List(item[1]).Cast<string>()];
        }

        return output;
    }

    internal static OrderedDictionary<string, List<string>>? Identity(object? value) =>
        value is OrderedDictionary<string, object?> table
            ? new OrderedDictionary<string, List<string>>(table.Select(e => KeyValuePair.Create(e.Key, List(e.Value).Cast<string>().ToList())))
            : null;

    internal static List<string>? Strings(object? value) => value is List<object?> list ? [.. list.Cast<string>()] : null;

    // `saga_arg` of the recorder: None, a name, or a saga node.
    internal static SagaOrName? SagaArg(object? value)
    {
        if (value is not OrderedDictionary<string, object?> d)
        {
            return null;
        }

        if (d.TryGetValue("name", out var name))
        {
            return new SagaOrName.Name { Value = (string)name! };
        }

        return new SagaOrName.Node { Saga = SagaNode.ModelValidate(DecodeDict(Dict(d["node"]))) };
    }

    internal static SearchConfig Recipe(string name) => name switch
    {
        "EDGE_HYBRID_SEARCH_RRF" => SearchConfigRecipes.EdgeHybridSearchRrf,
        "NODE_HYBRID_SEARCH_RRF" => SearchConfigRecipes.NodeHybridSearchRrf,
        _ => throw new InvalidOperationException("unknown recipe " + name),
    };

    private static OrderedDictionary<string, object?> EncodeDictionary(System.Collections.IDictionary value)
    {
        var output = new OrderedDictionary<string, object?>();
        foreach (System.Collections.DictionaryEntry entry in value)
        {
            output[Py.Str(entry.Key)] = Encode(entry.Value);
        }

        return output;
    }

    [GeneratedRegex("in [0-9.e+-]+ ms", RegexOptions.CultureInvariant)]
    private static partial Regex MsPattern();

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "graphiti.json");
        return (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
    }
}

/// <summary>A logger factory that adds each log line, with its Python level name and its time masked, to the events.</summary>
internal sealed class GraphitiLogCapture(List<object?> events) : ILoggerFactory, ILoggerProvider
{
    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new Logger(events);

    public void Dispose()
    {
    }

    private sealed class Logger(List<object?> events) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            events.Add(new OrderedDictionary<string, object?>
            {
                ["kind"] = "log",
                ["level"] = logLevel switch
                {
                    LogLevel.Debug => "DEBUG",
                    LogLevel.Information => "INFO",
                    LogLevel.Warning => "WARNING",
                    LogLevel.Error => "ERROR",
                    _ => logLevel.ToString().ToUpperInvariant(),
                },
                ["message"] = GraphitiValues.MaskMs(formatter(state, exception)),
            });
    }
}

/// <summary>
/// One run of a recorded case: the fakes, the event list, the count of calls in flight, the tokens that the fakes got,
/// and the call at which a fake cancels the token source.
/// </summary>
internal sealed class GraphitiRun
{
    private static readonly ActivitySource OtelSource = new("graphiti-recorder");

    private readonly OrderedDictionary<string, List<object?>> replies = new();

    private readonly List<string> texts = [];

    internal GraphitiRun(OrderedDictionary<string, object?> input, CancellationTokenSource source, int cancelAt)
    {
        Input = input;
        Source = source;
        CancelAt = cancelAt;
        if (input.GetValueOrDefault("replies") is OrderedDictionary<string, object?> table)
        {
            foreach (var (prompt, items) in table)
            {
                replies[prompt] = [.. List(PyJson.Loads(PyJson.Dumps(items)))];
            }
        }

        foreach (var (kind, count) in input.GetValueOrDefault("fail") as OrderedDictionary<string, object?> ?? new())
        {
            Fails[kind] = (long)count!;
        }

        Driver = new FakeDriver(this, (string?)input.GetValueOrDefault("database") ?? "default_db");
        Llm = new FakeLlm(this);
        Embedder = new FakeEmbedder(this);
        CrossEncoder = new FakeCrossEncoder(this);
    }

    internal OrderedDictionary<string, object?> Input { get; }

    internal CancellationTokenSource Source { get; }

    internal int CancelAt { get; }

    internal List<object?> Events { get; } = [];

    internal List<(string Kind, CancellationToken Token)> Tokens { get; } = [];

    internal Dictionary<string, long> Fails { get; } = [];

    internal int Count { get; private set; }

    internal int Attempts { get; private set; }

    internal int InFlight { get; private set; }

    internal int MaxInFlight { get; private set; }

    internal FakeDriver Driver { get; }

    internal FakeLlm Llm { get; }

    internal FakeEmbedder Embedder { get; }

    internal FakeCrossEncoder CrossEncoder { get; }

    // The common part of each fake call: an attempt, the token check, a yield, a second token check, the record, the
    // cancel at call k, and the failure that the case asks for (R25). The fake cancels the source on the scheduler
    // thread, as task.cancel() of the recorder does.
    internal async Task EnterAsync(string kind, OrderedDictionary<string, object?> record, CancellationToken cancellationToken, bool counted = true)
    {
        Attempts++;
        InFlight++;
        MaxInFlight = Math.Max(MaxInFlight, InFlight);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var item = new OrderedDictionary<string, object?> { ["kind"] = kind };
            foreach (var (key, value) in record)
            {
                item[key] = value;
            }

            Events.Add(item);
            if (!counted)
            {
                return;
            }

            Tokens.Add((kind, cancellationToken));
            Count++;
            if (Count - 1 == CancelAt)
            {
                Source.Cancel();
            }

            if (Fails.TryGetValue(kind, out var fails) && fails > 0)
            {
                Fails[kind] = fails - 1;
                throw new InvalidOperationException(kind + " failed");
            }
        }
        finally
        {
            InFlight--;
        }
    }

    internal List<double> Vector(string text)
    {
        if (Input.GetValueOrDefault("vectors") is OrderedDictionary<string, object?> vectors && vectors.TryGetValue(text, out var fixedVector))
        {
            return [.. List(fixedVector).Select(x => Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture))];
        }

        if (!texts.Contains(text))
        {
            texts.Add(text);
        }

        return [texts.IndexOf(text), 1.0, 0.0];
    }

    // The first reply of the case for the prompt whose needle (if any) is in the last message, the fallback reply of
    // the prompt, or LookupError. A reply of {"$raise": text} raises LookupError.
    internal OrderedDictionary<string, object?> Reply(string? promptName, string last)
    {
        var queue = promptName is not null && replies.TryGetValue(promptName, out var items) ? items : [];
        for (var i = 0; i < queue.Count; i++)
        {
            var item = Dict(queue[i]);
            if (item.GetValueOrDefault("needle") is not string needle || last.Contains(needle, StringComparison.Ordinal))
            {
                queue.RemoveAt(i);
                var reply = Dict(item["reply"]);
                if (reply.TryGetValue("$raise", out var text))
                {
                    throw new KeyNotFoundException((string)text!);
                }

                return Dict(PyJson.Loads(PyJson.Dumps(reply)));
            }
        }

        var fallback = Dict(GraphitiValues.Document["fallback"]);
        if (promptName is not null && fallback.TryGetValue(promptName, out var value))
        {
            return Dict(PyJson.Loads(PyJson.Dumps(value)));
        }

        throw new KeyNotFoundException("no reply for " + promptName);
    }

    internal GraphOperationsInterface? MakeOps() =>
        Input.GetValueOrDefault("ops") is OrderedDictionary<string, object?> spec ? new FakeOps(this, spec) : null;

    // `make_graphiti` of the recorder.
    internal GraphitiCore MakeGraphiti(OrderedDictionary<string, object?> init)
    {
        bool Flag(string key, bool otherwise) => init.TryGetValue(key, out var value) ? value is true : otherwise;

        LLMClient? llmClient = null;
        LLMRuntime? llmRuntime = null;
        if (Flag("llm_runtime", false))
        {
            var transport = new LLMTransport(Llm);
            llmRuntime = new LLMRuntime(transport.Model("model-1"));
            if (Flag("llm_client", false))
            {
                llmClient = Llm;
            }
        }
        else if (Flag("llm_client", true))
        {
            llmClient = Llm;
        }

        ConcurrentIngestionConfig? concurrentIngestion = null;
        if (init.GetValueOrDefault("concurrent_ingestion") is OrderedDictionary<string, object?> options)
        {
            var config = new OrderedDictionary<string, object?> { ["window"] = 2L };
            foreach (var (key, value) in options)
            {
                if (key is not ("enabled_for" or "store"))
                {
                    config[key] = value;
                }
            }

            concurrentIngestion = ConcurrentIngestionConfig.ModelValidate(config);
            if (options.GetValueOrDefault("enabled_for") is List<object?> names)
            {
                concurrentIngestion.EnabledForGroup = groupId => names.Contains(groupId);
            }

            if (options.GetValueOrDefault("store") is true)
            {
                concurrentIngestion.StateStore = new RecordingStore(this);
            }
        }

        return new GraphitiCore(
            uri: (string?)init.GetValueOrDefault("uri"),
            llmClient: llmClient,
            embedder: Flag("embedder", true) ? Embedder : null,
            crossEncoder: Flag("cross_encoder", true) ? CrossEncoder : null,
            storeRawEpisodeContent: Flag("store_raw_episode_content", true),
            graphDriver: Flag("graph_driver", true) ? Driver : null,
            maxCoroutines: (long?)init.GetValueOrDefault("max_coroutines"),
            tracer: (string?)init.GetValueOrDefault("tracer") == "otel" ? OtelSource : null,
            traceSpanPrefix: (string?)init.GetValueOrDefault("trace_span_prefix") ?? "graphiti",
            promptLibrary: Flag("prompt_library", false) ? Lib.CreatePromptLibrary(new()) : null,
            llmRuntime: llmRuntime,
            concurrentIngestion: concurrentIngestion);
    }

    internal sealed class FakeLlm(GraphitiRun run) : LLMClient(null, false)
    {
        internal override string PythonName => "FakeLlm";

        internal override Task<OrderedDictionary<string, object?>> PrivateGenerateResponseAsync(
            List<Message> messages,
            IResponseModel? responseModel = null,
            long maxTokens = global::Graphiti.Core.LlmClient.Config.DefaultMaxTokens,
            ModelSize modelSize = ModelSize.Medium,
            string? model = null,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override async Task<OrderedDictionary<string, object?>> GenerateResponseAsync(
            List<Message> messages,
            IResponseModel? responseModel = null,
            long? maxTokens = null,
            ModelSize modelSize = ModelSize.Medium,
            string? groupId = null,
            string? promptName = null,
            bool attributeExtraction = false,
            string? model = null,
            CancellationToken cancellationToken = default)
        {
            await run.EnterAsync(
                "llm",
                new OrderedDictionary<string, object?>
                {
                    ["prompt_name"] = promptName,
                    ["response_model"] = responseModel?.PythonName,
                    ["max_tokens"] = maxTokens,
                    ["model_size"] = Py.EnumValue(modelSize),
                    ["group_id"] = groupId,
                    ["attribute_extraction"] = attributeExtraction,
                    ["messages"] = messages.Select(m => (object?)new List<object?>
                    {
                        m.Role,
                        Convert.ToHexStringLower(SHA256.HashData(PyText.EncodeUtf8(m.Content))),
                        Py.Len(m.Content),
                    }).ToList(),
                },
                cancellationToken);
            return run.Reply(promptName, messages.Count > 0 ? messages[^1].Content : string.Empty);
        }
    }

    internal sealed class FakeEmbedder(GraphitiRun run) : EmbedderClient
    {
        internal override async Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("embed", new() { ["input"] = inputData.Select(x => (object?)x).ToList() }, cancellationToken);
            return run.Vector(inputData[0]);
        }

        internal override async Task<List<List<double>>> CreateBatchAsync(List<string> inputDataList, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("embed_batch", new() { ["input"] = inputDataList.Select(x => (object?)x).ToList() }, cancellationToken);
            return [.. inputDataList.Select(run.Vector)];
        }
    }

    internal sealed class FakeCrossEncoder(GraphitiRun run) : CrossEncoderClient
    {
        internal override async Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("rank", new() { ["query"] = query, ["passages"] = passages.Select(x => (object?)x).ToList() }, cancellationToken);
            return [.. passages.Select((p, i) => (p, 1.0 - (0.1 * i)))];
        }
    }

    internal sealed class FakeTx(GraphitiRun run) : Transaction
    {
        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            run.EnterAsync(
                "tx_run",
                new()
                {
                    ["query"] = GraphitiValues.QueryView(query),
                    ["keys"] = parameters.Keys.Select(x => (object?)x).ToList(),
                    ["values"] = GraphitiValues.Encode(parameters.Values.ToList()),
                },
                cancellationToken);
    }

    internal sealed class FakeSession(GraphitiRun run) : GraphDriverSession
    {
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            run.EnterAsync(
                "session_run",
                new()
                {
                    ["query"] = GraphitiValues.QueryView(query),
                    ["keys"] = parameters.Keys.Select(x => (object?)x).ToList(),
                    ["values"] = GraphitiValues.Encode(parameters.Values.ToList()),
                },
                cancellationToken);

        internal override Task CloseAsync(CancellationToken cancellationToken = default) =>
            run.EnterAsync("session_close", new(), cancellationToken, counted: false);

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, CancellationToken cancellationToken = default) =>
            ExecuteWriteAsync(func, null, cancellationToken);

        // The Python fake records `func.timeout`, which `unit_of_work(timeout=...)` sets (decision D52).
        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, double timeout, CancellationToken cancellationToken = default) =>
            ExecuteWriteAsync(func, (double?)timeout, cancellationToken);

        private async Task ExecuteWriteAsync(Func<Transaction, Task> func, double? timeout, CancellationToken cancellationToken)
        {
            await run.EnterAsync("execute_write", new() { ["timeout"] = timeout }, cancellationToken);
            await func(new FakeTx(run));
        }
    }

    internal sealed record FakeOps(GraphitiRun Run, OrderedDictionary<string, object?> Spec) : GraphOperationsInterface
    {
        internal override async Task<List<EpisodicNode>> RetrieveEpisodesAsync(
            GraphDriver driver,
            PyDateTime referenceTime,
            long lastN = 3,
            List<string>? groupIds = null,
            EpisodeType? source = null,
            string? saga = null,
            CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(
                "ops:retrieve_episodes",
                new()
                {
                    ["reference_time"] = Dt(referenceTime),
                    ["last_n"] = lastN,
                    ["group_ids"] = groupIds?.Select(x => (object?)x).ToList(),
                    ["source"] = GraphitiValues.Encode(source),
                    ["saga"] = saga,
                    ["database"] = driver.Database,
                },
                cancellationToken);
            if (!Spec.TryGetValue("retrieve_episodes", out var episodes))
            {
                throw new NotImplementedException(string.Empty);
            }

            return [.. List(episodes).Select(Episode)];
        }

        internal override async Task<string?> SagaGetPreviousEpisodeUuidAsync(
            GraphDriver driver,
            string sagaUuid,
            string currentEpisodeUuid,
            CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(
                "ops:saga_get_previous_episode_uuid",
                new() { ["saga_uuid"] = sagaUuid, ["current_episode_uuid"] = currentEpisodeUuid },
                cancellationToken);
            if (!Spec.TryGetValue("previous", out var previous))
            {
                throw new NotImplementedException(string.Empty);
            }

            return (string?)previous;
        }

        internal override async Task<List<(string Content, PyDateTime? ValidAt, PyDateTime CreatedAt)>> SagaGetEpisodeContentsAsync(
            GraphDriver driver,
            string sagaUuid,
            PyDateTime? since = null,
            long limit = 200,
            CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(
                "ops:saga_get_episode_contents",
                new() { ["saga_uuid"] = sagaUuid, ["since"] = Dt(since), ["limit"] = limit },
                cancellationToken);
            if (!Spec.TryGetValue("contents", out var contents))
            {
                throw new NotImplementedException(string.Empty);
            }

            return [.. List(contents).Select(List).Select(c => ((string)c[0]!, (PyDateTime?)Decode(c[1]), (PyDateTime)Decode(c[2])!))];
        }
    }

    internal sealed class FakeDriver : GraphDriver
    {
        private readonly GraphitiRun run;

        internal FakeDriver(GraphitiRun run, string database)
        {
            this.run = run;
            Database = database;
            Provider = PyEnum.FromValue<GraphProvider>((string?)run.Input.GetValueOrDefault("provider") ?? "neo4j");
            GraphOperationsInterface = run.MakeOps();
        }

        internal override string PythonName => "FakeDriver";

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await run.EnterAsync(
                "query",
                new()
                {
                    ["query"] = GraphitiValues.QueryView(cypherQuery),
                    ["keys"] = parameters.Keys.Select(x => (object?)x).ToList(),
                    ["values"] = GraphitiValues.Encode(parameters.Values.ToList()),
                },
                cancellationToken);

            // The needle is found in the query with its white space joined.
            var normalized = GraphitiValues.Normalize(cypherQuery);
            foreach (var entry in run.Input.GetValueOrDefault("rows") as List<object?> ?? [])
            {
                var row = List(entry);
                if (normalized.Contains((string)row[0]!, StringComparison.Ordinal))
                {
                    return ([.. List(row[1]).Select(r => DecodeDict(Dict(r)))], null, null);
                }
            }

            return ([], null, null);
        }

        internal override GraphDriverSession Session(string? database = null)
        {
            run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "session" });
            return new FakeSession(run);
        }

        internal override Task CloseAsync(CancellationToken cancellationToken = default) =>
            run.EnterAsync("driver_close", new() { ["database"] = Database }, cancellationToken);

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            run.EnterAsync("build_indices", new() { ["delete_existing"] = deleteExisting, ["database"] = Database }, cancellationToken);

        internal override GraphDriver Clone(string database)
        {
            run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "clone", ["database"] = database });
            return new FakeDriver(run, database);
        }
    }

    // The tracer of the recording: each span, its attributes (a key with `duration` or `_ms` has the value "<ms>"), its
    // status, its exception type, and its end.
    internal sealed class RecTracer(GraphitiRun run) : Tracer
    {
        internal override async Task<T> StartSpanAsync<T>(string name, Func<TracerSpan, Task<T>> body, CancellationToken cancellationToken = default)
        {
            run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "span", ["name"] = name });
            try
            {
                return await body(new RecSpan(run));
            }
            finally
            {
                run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "span_end", ["name"] = name });
            }
        }
    }

    internal sealed class RecSpan(GraphitiRun run) : TracerSpan
    {
        internal override void AddAttributes(OrderedDictionary<string, object?> attributes) =>
            run.Events.Add(new OrderedDictionary<string, object?>
            {
                ["kind"] = "span_attrs",
                ["attrs"] = attributes.Select(e => (object?)new List<object?>
                {
                    e.Key,
                    e.Key.Contains("duration", StringComparison.Ordinal) || e.Key.EndsWith("_ms", StringComparison.Ordinal) ? "<ms>" : GraphitiValues.Encode(e.Value),
                }).ToList(),
            });

        internal override void SetStatus(string status, string? description = null) =>
            run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "span_status", ["status"] = status, ["description"] = description });

        internal override void RecordException(Exception exception) =>
            run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "span_exception", ["type"] = GraphitiValuesTests.PythonName(exception) });
    }

    // RecordingStore of the recorder: an in-memory state store whose close is recorded and does nothing else.
    internal sealed class RecordingStore(GraphitiRun run) : IngestionStateStore
    {
        private readonly InMemoryIngestionStateStore inner = new();

        internal override Task<Admission?> BeginAdmitAsync(string groupId, string unitId, double ttl, CancellationToken cancellationToken = default) =>
            inner.BeginAdmitAsync(groupId, unitId, ttl, cancellationToken);

        internal override Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default) =>
            inner.EndAdmitAsync(groupId, seq, cancellationToken);

        internal override Task<AnalysisStart> StartAnalysisAsync(string groupId, long seq, CancellationToken cancellationToken = default) =>
            inner.StartAnalysisAsync(groupId, seq, cancellationToken);

        internal override Task SaveChangeSetAsync(string groupId, long seq, byte[] payload, CancellationToken cancellationToken = default) =>
            inner.SaveChangeSetAsync(groupId, seq, payload, cancellationToken);

        internal override Task HeartbeatAsync(string groupId, long seq, double ttl, long? fence = null, CancellationToken cancellationToken = default) =>
            inner.HeartbeatAsync(groupId, seq, ttl, fence, cancellationToken);

        internal override Task<BeginCommitResult> BeginCommitAsync(string groupId, long seq, double ttl, CancellationToken cancellationToken = default) =>
            inner.BeginCommitAsync(groupId, seq, ttl, cancellationToken);

        internal override Task<List<DeltaUnit>> LoadDeltaAsync(string groupId, long afterSeq, long beforeSeq, double since, CancellationToken cancellationToken = default) =>
            inner.LoadDeltaAsync(groupId, afterSeq, beforeSeq, since, cancellationToken);

        internal override Task SaveFinalAsync(string groupId, long seq, long fence, byte[] payload, CancellationToken cancellationToken = default) =>
            inner.SaveFinalAsync(groupId, seq, fence, payload, cancellationToken);

        internal override Task FinishCommitAsync(string groupId, long seq, long fence, List<string> nodeUuids, double retainSeconds, CancellationToken cancellationToken = default) =>
            inner.FinishCommitAsync(groupId, seq, fence, nodeUuids, retainSeconds, cancellationToken);

        internal override Task FailAsync(string groupId, long seq, string reason, long? fence = null, CancellationToken cancellationToken = default) =>
            inner.FailAsync(groupId, seq, reason, fence, cancellationToken);

        internal override Task<List<string>> ReadyEnrichAsync(string groupId, long seq, CancellationToken cancellationToken = default) =>
            inner.ReadyEnrichAsync(groupId, seq, cancellationToken);

        internal override Task FinishEnrichAsync(string groupId, long seq, List<string> nodeUuids, CancellationToken cancellationToken = default) =>
            inner.FinishEnrichAsync(groupId, seq, nodeUuids, cancellationToken);

        internal override Task CloseAsync(CancellationToken cancellationToken = default) =>
            run.EnterAsync("store_close", new(), cancellationToken);
    }

    // The span prefix of an OpenTelemetryTracer: Python's `tracer._span_prefix`, a private field in the port.
    internal static string? SpanPrefix(Tracer tracer) =>
        tracer is OpenTelemetryTracer
            ? (string?)typeof(OpenTelemetryTracer).GetField("spanPrefix", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tracer)
            : null;
}
