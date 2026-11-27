// Fakes and value helpers for the evaluation tests of graphiti_core/ingestion/phases.py (plan 0004, layer 8, packet P8c).
// The fakes replace the LLM client, the embedder, the cross-encoder, the graph driver with its session and transaction,
// and the graph reader, as port/tools/record_phases.py does in Python. The state store is the real in-memory store
// inside a wrapper that records each call. Each fake yields once and then records its call (R25).
namespace Graphiti.Port.Evaluation;

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.Ingestion;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

/// <summary>The values of the recording and the views that the recorder writes.</summary>
internal static partial class PhasesValues
{
    internal static readonly OrderedDictionary<string, object?> Document = Load();

    internal static readonly PyDateTime FixedNow = PyDateTime.FromIsoFormat((string)Document["fixed_now"]!);

    internal static List<object?> List(object? value) => (List<object?>)value!;

    internal static OrderedDictionary<string, object?> Dict(object? value) => (OrderedDictionary<string, object?>)value!;

    // A dict with the one key "$dt" is a datetime, as `decode` of the recorder reads it.
    internal static object? Decode(object? value) => value switch
    {
        OrderedDictionary<string, object?> d when d.Count == 1 && d.ContainsKey("$dt") => PyDateTime.FromIsoFormat((string)d["$dt"]!),
        OrderedDictionary<string, object?> d => DecodeDict(d),
        List<object?> l => l.Select(Decode).ToList(),
        _ => value,
    };

    internal static OrderedDictionary<string, object?> DecodeDict(OrderedDictionary<string, object?> value)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, item) in value)
        {
            result[key] = Decode(item);
        }

        return result;
    }

    internal static EntityNode Node(object? data) => EntityNode.ModelValidate(DecodeDict(Dict(data)));

    internal static EntityEdge Edge(object? data) => EntityEdge.ModelValidate(DecodeDict(Dict(data)));

    internal static EpisodicNode Episode(object? data) => EpisodicNode.ModelValidate(DecodeDict(Dict(data)));

    internal static EpisodicEdge EpisodicEdgeOf(object? data) => EpisodicEdge.ModelValidate(DecodeDict(Dict(data)));

    // The recorder writes a change set as the JSON of model_dump(mode='json'). Each side reads it with model_validate.
    internal static ChangeSet ChangeSetOf(object? data) => ChangeSet.ModelValidate(Dict(PyJson.Loads(PyJson.Dumps(data))));

    internal static AdmittedUnit Admitted(object? data)
    {
        var d = Dict(data);
        return new AdmittedUnit
        {
            GroupId = (string)d["group_id"]!,
            UnitId = (string)d["unit_id"]!,
            Seq = (long)d["seq"]!,
            CreatedAt = (PyDateTime)Decode(d["created_at"])!,
            Bulk = (bool)d["bulk"]!,
            Episodes = [.. List(d["episodes"]).Select(Episode)],
            PreviousEpisodes = [.. List(d["previous_episodes"]).Select(p => List(p).Select(Episode).ToList())],
        };
    }

    internal static object? Dt(PyDateTime? value) =>
        value is { } v ? new OrderedDictionary<string, object?> { ["$dt"] = v.IsoFormat() } : null;

    // `encode` of the recorder: a datetime becomes {"$dt": ...}, an enum member {"$enum": value}, a tuple a list.
    internal static object? Encode(object? value) => value switch
    {
        null => null,
        string s => s,
        bool b => b,
        int i => (long)i,
        long l => l,
        double d => d,
        PyDateTime d => Dt(d),
        Enum e => new OrderedDictionary<string, object?> { ["$enum"] = PyEnum.Value(e) },
        OrderedDictionary<string, object?> d => d.Aggregate(
            new OrderedDictionary<string, object?>(),
            (acc, e) =>
            {
                acc[e.Key] = Encode(e.Value);
                return acc;
            }),
        OrderedDictionary<string, List<string>> d => d.Aggregate(
            new OrderedDictionary<string, object?>(),
            (acc, e) =>
            {
                acc[e.Key] = Encode(e.Value);
                return acc;
            }),
        PyTuple t => t.Select(Encode).ToList(),
        System.Collections.IEnumerable e => e.Cast<object?>().Select(Encode).ToList(),
        _ => value,
    };

    internal static OrderedDictionary<string, object?> EpisodeView(EpisodicNode episode) => new()
    {
        ["uuid"] = episode.Uuid,
        ["name"] = episode.Name,
        ["group_id"] = episode.GroupId,
        ["content"] = episode.Content,
        ["valid_at"] = Dt(episode.ValidAt),
        ["created_at"] = Dt(episode.CreatedAt),
        ["entity_edges"] = episode.EntityEdges.Select(x => (object?)x).ToList(),
        ["source"] = Py.EnumValue(episode.Source),
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
    };

    // The *_ms keys read the system clock (time.time()), so the recording leaves them out.
    internal static List<object?> ReportView(OrderedDictionary<string, double> report) =>
        [.. report.Where(e => !e.Key.EndsWith("_ms", StringComparison.Ordinal)).Select(e => (object?)new List<object?> { e.Key, e.Value })];

    // A failure reason that Python writes with type(error).__name__ is `<type>` (decision D50).
    internal static string? MaskReason(string? reason) =>
        reason is null or StateStore.ReasonExpired or StateStore.ReasonApplyFailed ? reason : "<type>";

    internal static List<object?> StoreView(InMemoryIngestionStateStore store)
    {
        List<object?> output = [];
        foreach (var (groupId, group) in store.groups)
        {
            output.Add(new OrderedDictionary<string, object?>
            {
                ["group_id"] = groupId,
                ["next_seq"] = group.NextSeq,
                ["watermark"] = group.Watermark,
                ["admit_seq"] = group.AdmitSeq,
                ["units"] = group.Units.Select(e => (object?)new List<object?>
                {
                    e.Key,
                    e.Value.UnitId,
                    e.Value.Status,
                    e.Value.Deadline,
                    e.Value.SnapshotSeq,
                    e.Value.StartedAt,
                    e.Value.EndedAt,
                    e.Value.Fence,
                    MaskReason(e.Value.FailReason),
                    e.Value.Payload is not null,
                }).ToList(),
                ["unit_ids"] = group.UnitIds.Select(e => (object?)new List<object?> { e.Key, e.Value }).ToList(),
                ["enrich"] = group.Enrich.Select(e => (object?)new List<object?> { e.Key, e.Value.Select(x => (object?)x).ToList() }).ToList(),
            });
        }

        return output;
    }

    // `placeholders` of the recorder: each uuid4 that the code made becomes <new:N> in the order of its first appearance.
    internal static object? Placeholders(object? value, Dictionary<string, string> known) => value switch
    {
        OrderedDictionary<string, object?> d => d.Aggregate(
            new OrderedDictionary<string, object?>(),
            (acc, e) =>
            {
                acc[e.Key] = Placeholders(e.Value, known);
                return acc;
            }),
        List<object?> l => l.Select(v => Placeholders(v, known)).ToList(),
        string s => Uuid4Pattern().Replace(s, m =>
        {
            if (!known.TryGetValue(m.Value, out var name))
            {
                name = $"<new:{known.Count}>";
                known[m.Value] = name;
            }

            return name;
        }),
        _ => value,
    };

    // The log line of _apply_with_retries ends with type(error).__name__ in Python and the .NET type name in the port.
    internal static string MaskTypeName(string message) => TypeNamePattern().Replace(message, "failed: <type>");

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex Uuid4Pattern();

    [GeneratedRegex("failed: [A-Za-z_]+$", RegexOptions.CultureInvariant)]
    private static partial Regex TypeNamePattern();

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "phases.json");
        return (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
    }
}

/// <summary>The fake clock of the recording: one time in seconds that the fakes can move.</summary>
internal sealed class PhasesClock(double now = 1000.0)
{
    internal double Now { get; set; } = now;

    internal double Read() => Now;
}

/// <summary>A logger factory that adds each log line, with its Python level name, to the event list of a run.</summary>
internal sealed class PhasesLogCapture(List<object?> events) : ILoggerFactory, ILoggerProvider
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
                ["message"] = PhasesValues.MaskTypeName(formatter(state, exception)),
            });
    }
}

/// <summary>
/// One run of a recorded case: the fakes, the store, the event list, the count of calls in flight, the tokens that the
/// fakes got, and the call at which a fake cancels the token source.
/// </summary>
internal sealed class PhasesRun
{
    private readonly OrderedDictionary<string, List<object?>> replies = new();

    private readonly List<string> texts = [];

    internal PhasesRun(OrderedDictionary<string, object?> input, CancellationTokenSource source, int cancelAt)
    {
        Input = input;
        Source = source;
        CancelAt = cancelAt;
        if (input.GetValueOrDefault("replies") is OrderedDictionary<string, object?> table)
        {
            foreach (var (prompt, items) in table)
            {
                replies[prompt] = [.. PhasesValues.List(PyJson.Loads(PyJson.Dumps(items)))];
            }
        }

        foreach (var (kind, count) in input.GetValueOrDefault("fail") as OrderedDictionary<string, object?> ?? new())
        {
            Fails[kind] = (long)count!;
        }

        foreach (var (kind, count) in input.GetValueOrDefault("store_fail") as OrderedDictionary<string, object?> ?? new())
        {
            StoreFails[kind] = (long)count!;
        }

        foreach (var item in input.GetValueOrDefault("advance") as List<object?> ?? [])
        {
            var row = PhasesValues.List(item);
            Advance.Add([(string)row[0]!, (long)row[1]!, Convert.ToDouble(row[2], System.Globalization.CultureInfo.InvariantCulture)]);
        }

        Clock = new PhasesClock(input.GetValueOrDefault("clock") is double start ? start : 1000.0);
        Driver = new FakeDriver(this);
        Clients = new GraphitiClients
        {
            Driver = Driver,
            LlmClient = new FakeLlm(this),
            Embedder = new FakeEmbedder(this),
            CrossEncoder = new FakeCrossEncoder(),
            Tracer = new NoOpTracer(),
            MaxCoroutines = (long?)input.GetValueOrDefault("max_coroutines"),
        };
        Reader = new FakeReader(this, Clients);
        Inner = new InMemoryIngestionStateStore(Clock.Read);
        Store = new LoggingStore(this, Inner);
        var config = new OrderedDictionary<string, object?> { ["window"] = 4L };
        foreach (var (key, value) in input.GetValueOrDefault("config") as OrderedDictionary<string, object?> ?? new())
        {
            config[key] = value;
        }

        Config = ConcurrentIngestionConfig.ModelValidate(config);
    }

    internal OrderedDictionary<string, object?> Input { get; }

    internal CancellationTokenSource Source { get; }

    internal int CancelAt { get; }

    internal List<object?> Events { get; } = [];

    internal List<(string Kind, CancellationToken Token)> Tokens { get; } = [];

    internal Dictionary<string, long> Fails { get; } = [];

    internal Dictionary<string, long> StoreFails { get; } = [];

    internal List<object?[]> Advance { get; } = [];

    internal int Count { get; private set; }

    internal int Attempts { get; private set; }

    internal int InFlight { get; private set; }

    internal int MaxInFlight { get; private set; }

    internal PhasesClock Clock { get; }

    internal FakeDriver Driver { get; }

    internal GraphitiClients Clients { get; }

    internal FakeReader Reader { get; }

    internal InMemoryIngestionStateStore Inner { get; }

    internal LoggingStore Store { get; }

    internal ConcurrentIngestionConfig Config { get; }

    // The common part of each fake call: an attempt, the token check, a yield, a second token check, the record, the
    // clock move, the cancel at call k, and the failure that the case asks for (R25).
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
            foreach (var row in Advance)
            {
                if ((string)row[0]! == kind && (long)row[1]! > 0)
                {
                    row[1] = (long)row[1]! - 1;
                    Clock.Now += (double)row[2]!;
                }
            }

            if (Count - 1 == CancelAt)
            {
                await Source.CancelAsync();
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
            return [.. PhasesValues.List(fixedVector).Select(x => Convert.ToDouble(x, System.Globalization.CultureInfo.InvariantCulture))];
        }

        if (!texts.Contains(text))
        {
            texts.Add(text);
        }

        return [texts.IndexOf(text), 1.0, 0.0];
    }

    internal OrderedDictionary<string, object?> Reply(string? promptName)
    {
        if (promptName is null || !replies.TryGetValue(promptName, out var queue) || queue.Count == 0)
        {
            throw new KeyNotFoundException("no reply for " + promptName);
        }

        var reply = queue[0];
        queue.RemoveAt(0);
        return PhasesValues.Dict(reply);
    }

    internal sealed class FakeLlm(PhasesRun run) : LLMClient(new LLMConfig(), false)
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
            return run.Reply(promptName);
        }
    }

    internal sealed class FakeEmbedder(PhasesRun run) : EmbedderClient
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

    internal sealed class FakeCrossEncoder : CrossEncoderClient
    {
        internal override Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("rank is not used");
    }

    internal sealed class FakeTx(PhasesRun run) : Transaction
    {
        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            run.EnterAsync(
                "tx_run",
                new()
                {
                    ["query"] = query,
                    ["keys"] = parameters.Keys.Select(x => (object?)x).ToList(),
                    ["values"] = PhasesValues.Encode(parameters.Values.ToList()),
                },
                cancellationToken);
    }

    internal sealed class FakeSession(PhasesRun run) : GraphDriverSession
    {
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) =>
            run.EnterAsync("close", new(), cancellationToken, counted: false);

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, CancellationToken cancellationToken = default) =>
            ExecuteWriteAsync(func, null, cancellationToken);

        // The Python fake records `func.timeout`, which `unit_of_work(timeout=...)` sets (decision D52).
        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, double timeout, CancellationToken cancellationToken = default) =>
            ExecuteWriteAsync(func, (double?)timeout, cancellationToken);

        private async Task ExecuteWriteAsync(Func<Transaction, Task> func, double? timeout, CancellationToken cancellationToken)
        {
            await run.EnterAsync("execute_write", new() { ["timeout"] = timeout }, cancellationToken);
            try
            {
                await func(new FakeTx(run));
            }
            catch (Exception e)
            {
                // R4: the Python class of the exception that the session rolls back on.
                run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "rollback", ["error"] = PhasesValuesTests.PythonName(e) });
                throw;
            }
        }
    }

    internal sealed record PartialOps : GraphOperationsInterface
    {
        internal override Task NodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task EdgeUpdateBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    // The Python class `OneMissing` derives from `AllOps` and puts back the base operation `node_update_bulk`. A C#
    // override cannot be taken back, so this record overrides the other five operations.
    internal record OneMissingOps : GraphOperationsInterface
    {
        internal override Task NodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task EdgeUpdateBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task EpisodicNodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task EpisodicEdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> episodicEdges, long batchSize = 100, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task EdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    internal sealed record AllOps : OneMissingOps
    {
        internal override Task NodeUpdateBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    internal sealed class FakeDriver : GraphDriver
    {
        private readonly PhasesRun run;

        internal FakeDriver(PhasesRun run)
        {
            this.run = run;
            Provider = PyEnum.FromValue<GraphProvider>((string?)run.Input.GetValueOrDefault("provider") ?? "neo4j");
            GraphOperationsInterface = (string?)run.Input.GetValueOrDefault("ops") switch
            {
                "partial" => new PartialOps(),
                "all" => new AllOps(),
                "one_missing" => new OneMissingOps(),
                _ => null,
            };
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
                    ["query"] = cypherQuery,
                    ["keys"] = parameters.Keys.Select(x => (object?)x).ToList(),
                    ["values"] = PhasesValues.Encode(parameters.Values.ToList()),
                },
                cancellationToken);
            foreach (var entry in run.Input.GetValueOrDefault("rows") as List<object?> ?? [])
            {
                var row = PhasesValues.List(entry);
                if (cypherQuery.Contains((string)row[0]!, StringComparison.Ordinal))
                {
                    return ([.. PhasesValues.List(row[1]).Select(r => PhasesValues.DecodeDict(PhasesValues.Dict(r)))], null, null);
                }
            }

            return ([], null, null);
        }

        internal override GraphDriverSession Session(string? database = null)
        {
            run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "session" });
            return new FakeSession(run);
        }

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // The reader of the recording derives from GraphReader, as FakeGraph of tests/ingestion/ingestion_fixtures.py replaces
    // the reader. Each read returns new objects.
    internal sealed class FakeReader(PhasesRun run, GraphitiClients clients) : GraphReader(clients)
    {
        private OrderedDictionary<string, object?> Spec => run.Input.GetValueOrDefault("reader") as OrderedDictionary<string, object?> ?? new();

        internal override async Task<List<List<EntityNode>>> NodeCandidatesAsync(
            List<EntityNode> nodes,
            OrderedDictionary<string, List<string>>? identityProperties = null,
            CancellationToken cancellationToken = default)
        {
            await run.EnterAsync(
                "read:node_candidates",
                new()
                {
                    ["nodes"] = nodes.Select(n => (object?)n.Uuid).ToList(),
                    ["identity_properties"] = PhasesValues.Encode(identityProperties),
                },
                cancellationToken);
            var table = Spec.GetValueOrDefault("node_candidates") as OrderedDictionary<string, object?> ?? new();
            return [.. nodes.Select(n => (table.GetValueOrDefault(n.Uuid) as List<object?> ?? []).Select(PhasesValues.Node).ToList())];
        }

        internal override async Task<List<List<EntityEdge>>> EdgeDuplicateCandidatesAsync(List<EntityEdge> edges, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("read:edge_duplicate_candidates", new() { ["edges"] = edges.Select(e => (object?)e.Uuid).ToList() }, cancellationToken);
            return EdgeTable("duplicates", edges);
        }

        internal override async Task<List<List<EntityEdge>>> EdgeInvalidationCandidatesAsync(List<EntityEdge> edges, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("read:edge_invalidation_candidates", new() { ["edges"] = edges.Select(e => (object?)e.Uuid).ToList() }, cancellationToken);
            return EdgeTable("invalidations", edges);
        }

        internal override async Task<List<EntityNode>> NodesByUuidsAsync(List<string> uuids, string groupId, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("read:nodes_by_uuids", new() { ["uuids"] = uuids.Select(x => (object?)x).ToList(), ["group_id"] = groupId }, cancellationToken);
            var stored = Stored("nodes");
            return [.. uuids.Where(stored.ContainsKey).Select(u => PhasesValues.Node(stored[u]))];
        }

        internal override async Task<List<EpisodicNode>> EpisodesByUuidsAsync(List<string> uuids, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("read:episodes_by_uuids", new() { ["uuids"] = uuids.Select(x => (object?)x).ToList() }, cancellationToken);
            var stored = Stored("episodes");
            return [.. uuids.Where(stored.ContainsKey).Select(u => PhasesValues.Episode(stored[u]))];
        }

        internal override async Task<List<EntityEdge>> EdgesByUuidsAsync(List<string> uuids, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("read:edges_by_uuids", new() { ["uuids"] = uuids.Select(x => (object?)x).ToList() }, cancellationToken);
            var stored = Stored("edges");
            return [.. uuids.Where(stored.ContainsKey).Select(u => PhasesValues.Edge(stored[u]))];
        }

        private List<List<EntityEdge>> EdgeTable(string name, List<EntityEdge> edges)
        {
            var table = Spec.GetValueOrDefault(name) as OrderedDictionary<string, object?> ?? new();
            return [.. edges.Select(e => (table.GetValueOrDefault(e.Uuid) as List<object?> ?? []).Select(PhasesValues.Edge).ToList())];
        }

        private Dictionary<string, object?> Stored(string name)
        {
            var stored = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var item in Spec.GetValueOrDefault(name) as List<object?> ?? [])
            {
                stored[(string)PhasesValues.Dict(item)["uuid"]!] = item;
            }

            return stored;
        }
    }

    // LoggingStore of the fixtures: each call is recorded with no yield, and the inner store runs it. A payload is left
    // out of the record, because the compressed bytes differ between the two sides (GAPS F39).
    internal sealed class LoggingStore(PhasesRun run, InMemoryIngestionStateStore inner) : IngestionStateStore
    {
        internal override Task<Admission?> BeginAdmitAsync(string groupId, string unitId, double ttl, CancellationToken cancellationToken = default)
        {
            Record("begin_admit", groupId, unitId, ttl);
            return inner.BeginAdmitAsync(groupId, unitId, ttl, cancellationToken);
        }

        internal override Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default)
        {
            Record("end_admit", groupId, seq);
            return inner.EndAdmitAsync(groupId, seq, cancellationToken);
        }

        internal override Task<AnalysisStart> StartAnalysisAsync(string groupId, long seq, CancellationToken cancellationToken = default)
        {
            Record("start_analysis", groupId, seq);
            return inner.StartAnalysisAsync(groupId, seq, cancellationToken);
        }

        internal override Task SaveChangeSetAsync(string groupId, long seq, byte[] payload, CancellationToken cancellationToken = default)
        {
            Record("save_change_set", groupId, seq);
            return inner.SaveChangeSetAsync(groupId, seq, payload, cancellationToken);
        }

        internal override Task HeartbeatAsync(string groupId, long seq, double ttl, long? fence = null, CancellationToken cancellationToken = default)
        {
            Record("heartbeat", groupId, seq, ttl, fence);
            return inner.HeartbeatAsync(groupId, seq, ttl, fence, cancellationToken);
        }

        internal override Task<BeginCommitResult> BeginCommitAsync(string groupId, long seq, double ttl, CancellationToken cancellationToken = default)
        {
            Record("begin_commit", groupId, seq, ttl);
            return inner.BeginCommitAsync(groupId, seq, ttl, cancellationToken);
        }

        internal override Task<List<DeltaUnit>> LoadDeltaAsync(string groupId, long afterSeq, long beforeSeq, double since, CancellationToken cancellationToken = default)
        {
            Record("load_delta", groupId, afterSeq, beforeSeq, since);
            return inner.LoadDeltaAsync(groupId, afterSeq, beforeSeq, since, cancellationToken);
        }

        internal override Task SaveFinalAsync(string groupId, long seq, long fence, byte[] payload, CancellationToken cancellationToken = default)
        {
            Record("save_final", groupId, seq, fence);
            return inner.SaveFinalAsync(groupId, seq, fence, payload, cancellationToken);
        }

        internal override Task FinishCommitAsync(string groupId, long seq, long fence, List<string> nodeUuids, double retainSeconds, CancellationToken cancellationToken = default)
        {
            Record("finish_commit", groupId, seq, fence, nodeUuids, retainSeconds);
            return inner.FinishCommitAsync(groupId, seq, fence, nodeUuids, retainSeconds, cancellationToken);
        }

        internal override Task FailAsync(string groupId, long seq, string reason, long? fence = null, CancellationToken cancellationToken = default)
        {
            Record("fail", groupId, seq, PhasesValues.MaskReason(reason), fence);
            return inner.FailAsync(groupId, seq, reason, fence, cancellationToken);
        }

        internal override Task<List<string>> ReadyEnrichAsync(string groupId, long seq, CancellationToken cancellationToken = default)
        {
            Record("ready_enrich", groupId, seq);
            return inner.ReadyEnrichAsync(groupId, seq, cancellationToken);
        }

        internal override Task FinishEnrichAsync(string groupId, long seq, List<string> nodeUuids, CancellationToken cancellationToken = default)
        {
            Record("finish_enrich", groupId, seq, nodeUuids);
            return inner.FinishEnrichAsync(groupId, seq, nodeUuids, cancellationToken);
        }

        internal override Task CloseAsync(CancellationToken cancellationToken = default)
        {
            Record("close");
            return inner.CloseAsync(cancellationToken);
        }

        private void Record(string method, params object?[] args)
        {
            run.Events.Add(new OrderedDictionary<string, object?>
            {
                ["kind"] = "store",
                ["method"] = method,
                ["args"] = PhasesValues.Encode(args.ToList()),
            });
            if (run.StoreFails.TryGetValue(method, out var fails) && fails > 0)
            {
                run.StoreFails[method] = fails - 1;
                throw new InvalidOperationException("store " + method + " failed");
            }
        }
    }
}
