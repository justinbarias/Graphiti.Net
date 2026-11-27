// Fakes for the evaluation tests of graphiti_core/utils/maintenance/combined_extraction.py and community_operations.py
// (plan 0004, layer 7, packet P7d). They follow the fakes of port/tools/record_combined_extraction.py and
// port/tools/record_community_operations.py: the LLM client, the embedder, and the graph driver with its graph operations
// interface and its maintenance operations. Each fake yields once and then records its call (R25).
namespace Graphiti.Port.Evaluation;

using System.Security.Cryptography;
using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Operations;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;
using static Graphiti.Port.Evaluation.EdgeOperationsValues;

/// <summary>Value helpers of the two recordings.</summary>
internal static class CommunityCombinedValues
{
    internal const string PairPrompt = "summarize_nodes.summarize_pair";

    internal const string DescriptionPrompt = "summarize_nodes.summary_description";

    internal const string BatchTimestampsPrompt = "extract_edges.extract_timestamps_batch";

    internal static OrderedDictionary<string, object?> Load(string name) =>
        (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "values", name)))!;

    internal static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(PyText.EncodeUtf8(text)));

    internal static OrderedDictionary<string, object?> CommunityView(CommunityNode node) => new()
    {
        ["uuid"] = node.Uuid,
        ["name"] = node.Name,
        ["group_id"] = node.GroupId,
        ["labels"] = node.Labels.Select(x => (object?)x).ToList(),
        ["created_at"] = Dt(node.CreatedAt),
        ["summary"] = node.Summary,
        ["name_embedding"] = node.NameEmbedding?.Select(x => (object?)x).ToList(),
    };

    internal static OrderedDictionary<string, object?> CommunityEdgeView(CommunityEdge edge) => new()
    {
        ["uuid"] = edge.Uuid,
        ["group_id"] = edge.GroupId,
        ["source_node_uuid"] = edge.SourceNodeUuid,
        ["target_node_uuid"] = edge.TargetNodeUuid,
        ["created_at"] = Dt(edge.CreatedAt),
    };

    // `sort_pair_runs` of the community recorder: each run of consecutive summarize_pair calls is sorted by its JSON
    // text, because the calls of one round run at the same time (GAPS G19). The order of the rounds stays.
    internal static List<object?> SortPairRuns(List<object?> items)
    {
        List<object?> output = [];
        List<object?> run = [];
        foreach (var item in items.Append(null))
        {
            if (item is OrderedDictionary<string, object?> d && d.GetValueOrDefault("kind") is "llm" && d.GetValueOrDefault("prompt_name") is PairPrompt)
            {
                run.Add(item);
                continue;
            }

            output.AddRange(run.OrderBy(x => PyJson.Dumps(x), StringComparer.Ordinal));
            run = [];
            if (item is not null)
            {
                output.Add(item);
            }
        }

        return output;
    }

    // The Python name of an exception that the recording keeps.
    internal static string PythonErrorName(Exception e) => e switch
    {
        ValidationException => "ValidationError",
        KeyNotFoundException => "LookupError",
        ArgumentOutOfRangeException => "IndexError",
        ArgumentException => "ValueError",
        _ => "RuntimeError",
    };
}

/// <summary>A logger factory that adds each log line to the event list of a run, as the Capture handler does.</summary>
internal sealed class CommunityCombinedLogCapture(List<object?> events) : ILoggerFactory
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

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var entry = LogEvent(
                logLevel switch
                {
                    LogLevel.Debug => "DEBUG",
                    LogLevel.Information => "INFO",
                    LogLevel.Warning => "WARNING",
                    LogLevel.Error => "ERROR",
                    _ => logLevel.ToString().ToUpperInvariant(),
                },
                formatter(state, exception));
            if (exception is not null)
            {
                entry["exc"] = true;
            }

            events.Add(entry);
        }
    }
}

/// <summary>
/// One run of a recorded case: the fakes, the event list, the count of calls in flight, the tokens that the fakes got,
/// and the call at which a fake cancels the token source.
/// </summary>
internal sealed class CommunityCombinedRun
{
    private readonly List<OrderedDictionary<string, object?>> replies;

    internal CommunityCombinedRun(OrderedDictionary<string, object?> input, CancellationTokenSource source, int cancelAt)
    {
        Input = input;
        Source = source;
        CancelAt = cancelAt;
        replies = [.. List(input.GetValueOrDefault("replies") ?? new List<object?>()).Select(r => new OrderedDictionary<string, object?>(Dict(r)))];
        Llm = new FakeLlm(this);
        Embedder = new FakeEmbedder(this);
        Driver = new FakeDriver(this);
        Clients = new GraphitiClients
        {
            Driver = Driver,
            LlmClient = Llm,
            Embedder = Embedder,
            CrossEncoder = new FakeCrossEncoder(),
            Tracer = new NoOpTracer(),
            MaxCoroutines = (long?)input.GetValueOrDefault("max_coroutines"),
            PromptLibrary = input.GetValueOrDefault("routing") is true ? Lib.DefaultChatPromptLibrary : null,
        };
    }

    internal OrderedDictionary<string, object?> Input { get; }

    internal CancellationTokenSource Source { get; }

    internal int CancelAt { get; }

    internal List<object?> Events { get; } = [];

    internal List<CancellationToken> Tokens { get; } = [];

    internal List<string> Texts { get; } = [];

    internal int Count { get; private set; }

    internal int InFlight { get; private set; }

    internal int MaxInFlight { get; private set; }

    internal FakeLlm Llm { get; }

    internal FakeEmbedder Embedder { get; }

    internal FakeDriver Driver { get; }

    internal GraphitiClients Clients { get; }

    // The common part of each fake call: the token check, a yield, a second token check, and the record (R25).
    internal async Task EnterAsync(string kind, OrderedDictionary<string, object?> record, CancellationToken cancellationToken)
    {
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
                await Source.CancelAsync();
            }
        }
        finally
        {
            InFlight--;
        }
    }

    internal List<EntityNode> Stored() =>
        [.. List(Input.GetValueOrDefault("stored_nodes") ?? new List<object?>()).Select(n => Node(n))];

    private bool Implements(string op) => List(Input.GetValueOrDefault("ops") ?? new List<object?>()).Contains(op);

    private OrderedDictionary<string, object?> Answer(string? promptName, string last)
    {
        foreach (var item in replies)
        {
            var times = item.GetValueOrDefault("times") as long?;
            if ((string?)item["prompt"] == promptName && last.Contains((string)item["needle"]!, StringComparison.Ordinal) && (times ?? 1) > 0)
            {
                if (times is not null)
                {
                    item["times"] = times - 1;
                }

                var value = item["reply"];
                if (value is OrderedDictionary<string, object?> raise && raise.TryGetValue("$raise", out var text))
                {
                    throw new InvalidOperationException((string)text!);
                }

                return (OrderedDictionary<string, object?>)PyJson.Loads(PyJson.Dumps(value))!;
            }
        }

        var digest = CommunityCombinedValues.Sha256(last)[..8];
        switch (promptName)
        {
            case CommunityCombinedValues.BatchTimestampsPrompt:
                {
                    var count = (last.Length - last.Replace("\"reference_time\"", string.Empty, StringComparison.Ordinal).Length) / "\"reference_time\"".Length;
                    return new OrderedDictionary<string, object?>
                    {
                        ["timestamps"] = Enumerable.Range(0, count)
                            .Select(_ => (object?)new OrderedDictionary<string, object?> { ["valid_at"] = null, ["invalid_at"] = null })
                            .ToList(),
                    };
                }

            case CommunityCombinedValues.PairPrompt:
                return new OrderedDictionary<string, object?> { ["summary"] = $"P{digest}." };
            case CommunityCombinedValues.DescriptionPrompt:
                return new OrderedDictionary<string, object?> { ["description"] = $"D{digest}" };
            default:
                throw new KeyNotFoundException("no reply for " + promptName);
        }
    }

    internal sealed class FakeLlm(CommunityCombinedRun run) : LLMClient(new LLMConfig(), false)
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
                        CommunityCombinedValues.Sha256(m.Content),
                        Py.Len(m.Content),
                    }).ToList(),
                },
                cancellationToken);
            return run.Answer(promptName, messages[^1].Content);
        }
    }

    internal sealed class FakeEmbedder(CommunityCombinedRun run) : EmbedderClient
    {
        internal override async Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("embed", new() { ["input"] = inputData.Select(x => (object?)x).ToList() }, cancellationToken);
            var text = inputData[0];
            if (!run.Texts.Contains(text))
            {
                run.Texts.Add(text);
            }

            return [run.Texts.IndexOf(text), 1.0];
        }
    }

    internal sealed class FakeCrossEncoder : CrossEncoderClient
    {
        internal override Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("rank is not used");
    }

    internal sealed record FakeOps(CommunityCombinedRun Run) : GraphOperationsInterface
    {
        internal override async Task<List<EntityNode>> NodeGetByGroupIdsAsync(Type? cls, GraphDriver driver, List<string> groupIds, long? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync("get_by_group_ids", new() { ["group_ids"] = groupIds.Select(x => (object?)x).ToList(), ["limit"] = limit, ["uuid_cursor"] = uuidCursor }, cancellationToken);
            return [.. Run.Stored().Where(n => groupIds.Contains(n.GroupId))];
        }

        internal override async Task<List<EntityNode>> NodeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, string? groupId = null, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync("get_by_uuids", new() { ["uuids"] = uuids.Select(x => (object?)x).ToList(), ["group_id"] = groupId }, cancellationToken);
            return [.. Run.Stored().Where(n => uuids.Contains(n.Uuid))];
        }

        internal override async Task<List<List<EntityNode>>> GetCommunityClustersAsync(GraphDriver driver, List<string>? groupIds, CancellationToken cancellationToken = default)
        {
            if (!Run.Implements("get_community_clusters"))
            {
                throw new NotImplementedException(string.Empty);
            }

            await Run.EnterAsync("ops_get_community_clusters", new() { ["group_ids"] = groupIds?.Select(x => (object?)x).ToList() }, cancellationToken);
            var byUuid = Run.Stored().ToDictionary(n => n.Uuid);
            return [.. List(Run.Input["clusters"]).Select(c => List(c).Select(u => byUuid[(string)u!]).ToList())];
        }

        internal override async Task RemoveCommunitiesAsync(GraphDriver driver, List<string>? groupIds = null, CancellationToken cancellationToken = default)
        {
            if (!Run.Implements("remove_communities"))
            {
                throw new NotImplementedException(string.Empty);
            }

            await Run.EnterAsync("ops_remove_communities", new() { ["group_ids"] = groupIds?.Select(x => (object?)x).ToList() }, cancellationToken);
        }

        internal override async Task<(CommunityNode?, bool)> DetermineEntityCommunityAsync(GraphDriver driver, EntityNode entity, CancellationToken cancellationToken = default)
        {
            if (!Run.Implements("determine_entity_community"))
            {
                throw new NotImplementedException(string.Empty);
            }

            await Run.EnterAsync("ops_determine_entity_community", new() { ["entity"] = entity.Uuid }, cancellationToken);
            var found = Run.Input["community"];
            return (found is null ? null : CommunityNode.ModelValidate(DecodeDict(Dict(found))), (bool)Run.Input["is_new"]!);
        }
    }

    internal sealed class FakeMaintenance : GraphMaintenanceOperations
    {
        internal override Task ClearDataAsync(QueryExecutor executor, List<string>? groupIds = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(QueryExecutor executor, bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task DeleteAllIndexesAsync(QueryExecutor executor, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task<List<List<EntityNode>>> GetCommunityClustersAsync(QueryExecutor executor, List<string>? groupIds = null, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task DetermineEntityCommunityAsync(QueryExecutor executor, EntityNode entity, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task<List<EntityNode>> GetMentionedNodesAsync(QueryExecutor executor, List<EpisodicNode> episodes, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task<List<CommunityNode>> GetCommunitiesByNodesAsync(QueryExecutor executor, List<EntityNode> nodes, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    internal sealed class FakeDriver : GraphDriver
    {
        private readonly CommunityCombinedRun run;

        private readonly FakeMaintenance? maintenance;

        internal FakeDriver(CommunityCombinedRun run)
        {
            this.run = run;
            Provider = GraphProvider.Neo4j;
            GraphOperationsInterface = new FakeOps(run);
            maintenance = run.Input.GetValueOrDefault("graph_ops") is true ? new FakeMaintenance() : null;
        }

        internal override string PythonName => "FakeDriver";

        internal override GraphMaintenanceOperations? GraphOps => maintenance;

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
                    ["values"] = Encode(parameters.Values.ToList()),
                },
                cancellationToken);
            var key = parameters.TryGetValue("uuid", out var u) ? u : parameters.GetValueOrDefault("entity_uuid");
            foreach (var item in List(run.Input.GetValueOrDefault("answers") ?? new List<object?>()).Select(Dict))
            {
                if (cypherQuery.Contains((string)item["needle"]!, StringComparison.Ordinal) && (!item.ContainsKey("uuid") || Equals(item["uuid"], key)))
                {
                    return ([.. List(item["records"]).Select(r => DecodeDict(Dict(r)))], null, null);
                }
            }

            return ([], null, null);
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
