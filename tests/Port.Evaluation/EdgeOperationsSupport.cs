// Fakes and value helpers for the evaluation tests of graphiti_core/utils/maintenance/edge_operations.py (plan 0004,
// layer 7, packet P7c). The fakes replace the LLM client, the embedder, the cross-encoder, and the graph driver with its
// search interface and graph operations interface, as port/tools/record_edge_operations.py does in Python. Each fake
// yields once and then records its call (R25).
namespace Graphiti.Port.Evaluation;

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

/// <summary>The values of the recording and the views that the recorder writes.</summary>
internal static partial class EdgeOperationsValues
{
    internal static readonly OrderedDictionary<string, object?> Document = Load();

    internal static readonly PyDateTime FixedNow = PyDateTime.FromIsoFormat((string)Document["fixed_now"]!);

    internal static readonly OrderedDictionary<string, GraphTypeDescriptor> Models = BuildModels();

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

    internal static EntityEdge Edge(object? data) => EntityEdge.ModelValidate(DecodeDict(Dict(data)));

    internal static EntityNode Node(object? data) => EntityNode.ModelValidate(DecodeDict(Dict(data)));

    internal static EpisodicNode Episode(object? data) => EpisodicNode.ModelValidate(DecodeDict(Dict(data)));

    internal static object? Dt(PyDateTime? value) =>
        value is { } v ? new OrderedDictionary<string, object?> { ["$dt"] = v.IsoFormat() } : null;

    // `encode` of the recorder: a datetime becomes {"$dt": ...}, a tuple becomes a list.
    internal static object? Encode(object? value) => value switch
    {
        PyDateTime d => Dt(d),
        OrderedDictionary<string, object?> d => d.Aggregate(
            new OrderedDictionary<string, object?>(),
            (acc, e) =>
            {
                acc[e.Key] = Encode(e.Value);
                return acc;
            }),
        PyTuple t => t.Select(Encode).ToList(),
        string s => s,
        System.Collections.IEnumerable e => e.Cast<object?>().Select(Encode).ToList(),
        _ => value,
    };

    internal static OrderedDictionary<string, object?> EdgeView(EntityEdge edge) => new()
    {
        ["uuid"] = edge.Uuid,
        ["group_id"] = edge.GroupId,
        ["source_node_uuid"] = edge.SourceNodeUuid,
        ["target_node_uuid"] = edge.TargetNodeUuid,
        ["created_at"] = Dt(edge.CreatedAt),
        ["name"] = edge.Name,
        ["fact"] = edge.Fact,
        ["fact_embedding"] = edge.FactEmbedding?.Select(x => (object?)x).ToList(),
        ["episodes"] = edge.Episodes.Select(x => (object?)x).ToList(),
        ["expired_at"] = Dt(edge.ExpiredAt),
        ["valid_at"] = Dt(edge.ValidAt),
        ["invalid_at"] = Dt(edge.InvalidAt),
        ["reference_time"] = Dt(edge.ReferenceTime),
        ["attributes"] = Encode(edge.Attributes),
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
    };

    internal static OrderedDictionary<string, object?> SimpleEdgeView(string type, Graphiti.Core.Edge edge) => new()
    {
        ["type"] = type,
        ["uuid"] = edge.Uuid,
        ["group_id"] = edge.GroupId,
        ["source_node_uuid"] = edge.SourceNodeUuid,
        ["target_node_uuid"] = edge.TargetNodeUuid,
        ["created_at"] = Dt(edge.CreatedAt),
    };

    internal static List<object?> Uuids(IEnumerable<EntityEdge> edges) => edges.Select(e => (object?)e.Uuid).ToList();

    // `placeholders` of the recorder: each uuid4 that the code made becomes <new:N> in the order of its first
    // appearance, and the milliseconds of a timed log line become <ms>.
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
        string s => MsPattern().Replace(
            Uuid4Pattern().Replace(s, m =>
            {
                if (!known.TryGetValue(m.Value, out var name))
                {
                    name = $"<new:{known.Count}>";
                    known[m.Value] = name;
                }

                return name;
            }),
            "in <ms> ms"),
        _ => value,
    };

    internal static OrderedDictionary<string, object?> LogEvent(string level, string message) => new()
    {
        ["kind"] = "log",
        ["level"] = level,
        ["message"] = message,
    };

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex Uuid4Pattern();

    [GeneratedRegex("in [0-9.e+-]+ ms", RegexOptions.CultureInvariant)]
    private static partial Regex MsPattern();

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "edge_operations.json");
        return (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
    }

    private static OrderedDictionary<string, GraphTypeDescriptor> BuildModels()
    {
        var models = new OrderedDictionary<string, GraphTypeDescriptor>();
        foreach (var (name, spec) in Dict(Document["types"]))
        {
            models[name] = new GraphTypeDescriptor(
                name,
                (string?)Dict(spec)["doc"],
                List(Dict(spec)["fields"]).Select(List).Select(f => new GraphFieldDescriptor((string)f[0]!, GraphFieldKind.String, false, null, (bool)f[1]!, (long?)f[2])));
        }

        return models;
    }
}

/// <summary>A logger factory that adds each log line, with its Python level name, to the event list of a run.</summary>
internal sealed class EdgeOperationsLogCapture(List<object?> events) : ILoggerFactory, ILoggerProvider
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
            events.Add(EdgeOperationsValues.LogEvent(
                logLevel switch
                {
                    LogLevel.Debug => "DEBUG",
                    LogLevel.Information => "INFO",
                    LogLevel.Warning => "WARNING",
                    LogLevel.Error => "ERROR",
                    _ => logLevel.ToString().ToUpperInvariant(),
                },
                formatter(state, exception)));
    }
}

/// <summary>
/// One run of a recorded case: the fakes, the event list, the count of calls in flight, the tokens that the fakes got,
/// and the call at which a fake cancels the token source.
/// </summary>
internal sealed class EdgeOperationsRun
{
    internal EdgeOperationsRun(OrderedDictionary<string, object?> input, CancellationTokenSource source, int cancelAt)
    {
        Input = input;
        Source = source;
        CancelAt = cancelAt;
        List<object?> replies = [.. EdgeOperationsValues.List(input.GetValueOrDefault("replies") ?? new List<object?>())];
        replies.Add(Reply("extract_edges.extract_attributes", string.Empty, new OrderedDictionary<string, object?>
        {
            ["role"] = "developer of tools",
            ["city"] = "Lyon",
            ["how"] = "school",
            ["x"] = 1L,
        }));
        replies.Add(Reply("extract_edges.extract_timestamps", string.Empty, new OrderedDictionary<string, object?>
        {
            ["valid_at"] = null,
            ["invalid_at"] = null,
        }));
        Replies = replies;
        Llm = new FakeLlm(this);
        var routing = input.GetValueOrDefault("routing") is true;
        Driver = new FakeDriver(this)
        {
            Provider = PyEnum.FromValue<GraphProvider>((string?)input.GetValueOrDefault("provider") ?? "neo4j"),
        };
        Clients = new GraphitiClients
        {
            Driver = Driver,
            LlmClient = Llm,
            Embedder = new FakeEmbedder(this),
            CrossEncoder = new FakeCrossEncoder(),
            Tracer = new NoOpTracer(),
            MaxCoroutines = (long?)input.GetValueOrDefault("max_coroutines"),
            PromptLibrary = routing ? Lib.DefaultChatPromptLibrary : null,
        };
    }

    internal OrderedDictionary<string, object?> Input { get; }

    internal CancellationTokenSource Source { get; }

    internal int CancelAt { get; }

    internal List<object?> Replies { get; }

    internal List<object?> Events { get; } = [];

    internal List<CancellationToken> Tokens { get; } = [];

    internal List<string> Texts { get; } = [];

    internal int Count { get; private set; }

    // The count of the calls that start, before a fake reads its token (R25).
    internal long Attempts { get; private set; }

    // The length of the event list at the moment that a fake cancels the token source.
    internal int EventsAtCancel { get; private set; } = -1;

    internal int InFlight { get; private set; }

    internal int MaxInFlight { get; private set; }

    internal FakeLlm Llm { get; }

    internal FakeDriver Driver { get; }

    internal GraphitiClients Clients { get; }

    private static OrderedDictionary<string, object?> Reply(string prompt, string needle, object? value) => new()
    {
        ["prompt"] = prompt,
        ["needle"] = needle,
        ["reply"] = value,
    };

    // The common part of each fake call: an attempt, the token check, a yield, a second token check, and the record
    // (R25). A call that is canceled still counts as an attempt, as in the fake of the recorder.
    internal async Task EnterAsync(string kind, OrderedDictionary<string, object?> record, CancellationToken cancellationToken)
    {
        Attempts++;
        cancellationToken.ThrowIfCancellationRequested();
        InFlight++;
        MaxInFlight = Math.Max(MaxInFlight, InFlight);
        try
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var item = new OrderedDictionary<string, object?> { ["kind"] = kind };
            foreach (var (key, value) in record)
            {
                item[key] = value;
            }

            Events.Add(item);
            Tokens.Add(cancellationToken);
            if (Count++ == CancelAt)
            {
                EventsAtCancel = Events.Count;

                // The cancel runs the callbacks of the token on the scheduler, before this call returns, as
                // `task.cancel()` of the recorder acts at one point of the loop. CancelAsync runs them on the thread
                // pool, where they race with the scheduler (R25).
                Source.Cancel();
            }
        }
        finally
        {
            InFlight--;
        }
    }

    internal List<EntityEdge> Answer(string fact, SearchFilters searchFilter, string kind)
    {
        var scope = searchFilter.EdgeSourceNodeUuids is not null ? "pair" : "all";
        var search = Input.GetValueOrDefault("search") as OrderedDictionary<string, object?>;
        if (search?.GetValueOrDefault(fact) is not OrderedDictionary<string, object?> byScope
            || byScope.GetValueOrDefault(scope) is not OrderedDictionary<string, object?> byKind
            || byKind.GetValueOrDefault(kind) is not List<object?> table)
        {
            return [];
        }

        return [.. table.Select(EdgeOperationsValues.Edge)];
    }

    internal sealed class FakeLlm(EdgeOperationsRun run) : LLMClient(new LLMConfig(), false)
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
            var last = messages[^1].Content;
            foreach (var item in run.Replies.Select(EdgeOperationsValues.Dict))
            {
                if ((string?)item["prompt"] == promptName && last.Contains((string)item["needle"]!, StringComparison.Ordinal))
                {
                    var value = item["reply"];
                    if (value is OrderedDictionary<string, object?> raise && raise.TryGetValue("$raise", out var text))
                    {
                        throw new InvalidOperationException((string)text!);
                    }

                    return (OrderedDictionary<string, object?>)PyJson.Loads(PyJson.Dumps(value))!;
                }
            }

            throw new KeyNotFoundException("no reply for " + promptName);
        }
    }

    internal sealed class FakeEmbedder(EdgeOperationsRun run) : EmbedderClient
    {
        internal override Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("create is not used");

        internal override async Task<List<List<double>>> CreateBatchAsync(List<string> inputDataList, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("embed", new() { ["input"] = inputDataList.Select(x => (object?)x).ToList() }, cancellationToken);
            List<List<double>> output = [];
            foreach (var text in inputDataList)
            {
                if (!run.Texts.Contains(text))
                {
                    run.Texts.Add(text);
                }

                output.Add([run.Texts.IndexOf(text), 1.0]);
            }

            return output;
        }
    }

    internal sealed class FakeCrossEncoder : CrossEncoderClient
    {
        internal override Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("rank is not used");
    }

    internal sealed record FakeSearch(EdgeOperationsRun Run) : SearchInterface
    {
        internal override async Task<List<EntityEdge>> EdgeFulltextSearchAsync(GraphDriver driver, string query, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(
                "fulltext",
                new()
                {
                    ["query"] = query,
                    ["group_ids"] = groupIds?.Select(x => (object?)x).ToList(),
                    ["limit"] = limit,
                    ["source"] = searchFilter.EdgeSourceNodeUuids?.Select(x => (object?)x).ToList(),
                    ["target"] = searchFilter.EdgeTargetNodeUuids?.Select(x => (object?)x).ToList(),
                },
                cancellationToken);
            return Run.Answer(query, searchFilter, "bm25");
        }

        internal override async Task<List<EntityEdge>> EdgeSimilaritySearchAsync(GraphDriver driver, List<double> searchVector, string? sourceNodeUuid, string? targetNodeUuid, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, double minScore = 0.7, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(
                "similarity",
                new()
                {
                    ["vector"] = searchVector.Select(x => (object?)x).ToList(),
                    ["source_node_uuid"] = sourceNodeUuid,
                    ["target_node_uuid"] = targetNodeUuid,
                    ["group_ids"] = groupIds?.Select(x => (object?)x).ToList(),
                    ["limit"] = limit,
                    ["min_score"] = minScore,
                    ["source"] = searchFilter.EdgeSourceNodeUuids?.Select(x => (object?)x).ToList(),
                    ["target"] = searchFilter.EdgeTargetNodeUuids?.Select(x => (object?)x).ToList(),
                },
                cancellationToken);
            return Run.Answer(Run.Texts[(int)searchVector[0]], searchFilter, "cos");
        }
    }

    internal sealed record FakeGraphOps(EdgeOperationsRun Run) : GraphOperationsInterface
    {
        internal override async Task<List<EntityNode>> NodeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, string? groupId = null, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync("get_nodes", new() { ["uuids"] = uuids.Select(x => (object?)x).ToList(), ["group_id"] = groupId }, cancellationToken);
            return
            [
                .. EdgeOperationsValues.List(Run.Input.GetValueOrDefault("stored_nodes") ?? new List<object?>())
                    .Select(EdgeOperationsValues.Dict)
                    .Where(n => uuids.Contains((string)n["uuid"]!) && (groupId is null || (string)n["group_id"]! == groupId))
                    .Select(n => EdgeOperationsValues.Node(n)),
            ];
        }
    }

    internal sealed class FakeDriver : GraphDriver
    {
        private readonly EdgeOperationsRun run;

        internal FakeDriver(EdgeOperationsRun run)
        {
            this.run = run;
            SearchInterface = new FakeSearch(run);
            GraphOperationsInterface = new FakeGraphOps(run);
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
                    ["values"] = EdgeOperationsValues.Encode(parameters.Values.ToList()),
                },
                cancellationToken);
            return (
                [.. EdgeOperationsValues.List(run.Input.GetValueOrDefault("records") ?? new List<object?>())
                    .Select(r => new OrderedDictionary<string, object?>(EdgeOperationsValues.Dict(r)))],
                null,
                null);
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

/// <summary>
/// The fake model client of the ported upstream tests, in place of <c>MagicMock()</c> with an <c>AsyncMock</c>
/// <c>generate_response</c>. It records each call and answers with <see cref="Respond"/>.
/// </summary>
internal sealed class EdgeTestLlm : LLMClient
{
    internal EdgeTestLlm(OrderedDictionary<string, object?>? returnValue = null)
        : base(new LLMConfig(), false)
    {
        Respond = (_, _) => returnValue is null ? new OrderedDictionary<string, object?>() : (OrderedDictionary<string, object?>)PyJson.Loads(PyJson.Dumps(returnValue))!;
    }

    internal Func<List<Message>, string?, OrderedDictionary<string, object?>> Respond { get; set; }

    internal List<(List<Message> Messages, string? PromptName, IResponseModel? ResponseModel)> Calls { get; } = [];

    internal override string PythonName => "MagicMock";

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
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add((messages, promptName, responseModel));
        return Respond(messages, promptName);
    }
}

/// <summary>
/// The fake embedder of the ported upstream tests. It records each batch and gives each text the vector of
/// <see cref="Vector"/>.
/// </summary>
internal sealed class EdgeTestEmbedder(Func<string, List<double>>? vector = null) : EmbedderClient
{
    internal List<List<string>> Batches { get; } = [];

    internal Func<string, List<double>> Vector { get; } = vector ?? (_ => [1.0, 0.0]);

    internal override Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("create is not used");

    internal override async Task<List<List<double>>> CreateBatchAsync(List<string> inputDataList, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        Batches.Add([.. inputDataList]);
        return [.. inputDataList.Select(Vector)];
    }
}

/// <summary>
/// The fake driver of the ported upstream tests that patch <c>edge_operations.search</c> (open item O10). The real
/// search reads the search interface of the driver for each edge search, so the fake answers there: the full-text
/// search and the similarity search of one search both answer with <see cref="Results"/>. <see cref="Searches"/> holds
/// the query and the filter of each search, and <see cref="Vectors"/> its query vector.
/// </summary>
internal sealed class EdgeTestSearchDriver : GraphDriver
{
    internal EdgeTestSearchDriver(Func<string?, List<double>?, SearchFilters, List<EntityEdge>>? results = null)
    {
        Results = results ?? ((_, _, _) => []);
        SearchInterface = new Interface(this);
    }

    // The query of a full-text search, or the vector of a similarity search, and the filter.
    internal Func<string?, List<double>?, SearchFilters, List<EntityEdge>> Results { get; }

    internal List<(string Query, SearchFilters Filter)> Searches { get; } = [];

    internal List<List<double>> Vectors { get; } = [];

    internal override string PythonName => "MagicMock";

    internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
        string cypherQuery,
        OrderedDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

    internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

    internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;

    private sealed record Interface(EdgeTestSearchDriver Driver) : SearchInterface
    {
        internal override async Task<List<EntityEdge>> EdgeFulltextSearchAsync(GraphDriver driver, string query, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            Driver.Searches.Add((query, searchFilter));
            return Driver.Results(query, null, searchFilter);
        }

        internal override async Task<List<EntityEdge>> EdgeSimilaritySearchAsync(GraphDriver driver, List<double> searchVector, string? sourceNodeUuid, string? targetNodeUuid, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, double minScore = 0.7, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            Driver.Vectors.Add(searchVector);
            return Driver.Results(null, searchVector, searchFilter);
        }
    }
}
