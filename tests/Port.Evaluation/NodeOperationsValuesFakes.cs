// The fakes of NodeOperationsValuesTests: the same fakes as build_world() of port/tools/record_node_operations.py.
namespace Graphiti.Port.Evaluation;

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
using static Graphiti.Port.Evaluation.MaintenanceHelpersValuesTests;

public partial class NodeOperationsValuesTests
{
    // One run of one case: the clients, the event list, and the counts of the calls.
    private sealed class Run
    {
        private readonly CancellationTokenSource source;

        private readonly long cancelAt;

        internal Run(OrderedDictionary<string, object?> input, CancellationTokenSource source, long cancelAt)
        {
            Input = input;
            this.source = source;
            this.cancelAt = cancelAt;
            var spec = Dict(input.GetValueOrDefault("clients") ?? new OrderedDictionary<string, object?>());
            Nodes = MakeNodes(Dict(input["args"]).GetValueOrDefault("nodes") ?? new List<object?>());
            Clients = new GraphitiClients
            {
                Driver = new FakeDriver(this, PyEnum.FromValue<GraphProvider>(spec.GetValueOrDefault("provider") ?? "neo4j")),
                LlmClient = new FakeLlm(this, new LLMConfig(maxTokens: (long?)spec.GetValueOrDefault("max_tokens") ?? 8192)),
                Embedder = new FakeEmbedder(this, (string?)spec.GetValueOrDefault("embedder") ?? "batch"),
                CrossEncoder = new UnusedCrossEncoder(),
                Tracer = new NoOpTracer(),
                MaxCoroutines = (long?)spec.GetValueOrDefault("max_coroutines"),
            };
        }

        internal OrderedDictionary<string, object?> Input { get; }

        internal GraphitiClients Clients { get; }

        internal List<EntityNode> Nodes { get; }

        internal List<object?> Events { get; } = [];

        internal List<string> Texts { get; } = [];

        internal Dictionary<string, long> MaxInFlight { get; } = new(StringComparer.Ordinal);

        internal Dictionary<string, int> LlmIndex { get; } = new(StringComparer.Ordinal);

        internal List<CancellationToken> Tokens { get; } = [];

        // The count of the calls that take the token and record themselves.
        internal long Count { get; private set; }

        // The count of the calls that start, before a fake reads its token (R25).
        internal long Attempts { get; private set; }

        // The count of the attempts and the length of the event list at the moment that a fake cancels the token source.
        internal long AttemptsAtCancel { get; private set; } = -1;

        internal int EventsAtCancel { get; private set; } = -1;

        private Dictionary<string, long> InFlight { get; } = new(StringComparer.Ordinal);

        // A call that takes the token: it counts an attempt and itself in flight, yields, reads the token, and records itself.
        internal async Task EnterAsync(string kind, OrderedDictionary<string, object?> record, CancellationToken token)
        {
            Attempts++;
            InFlight[kind] = InFlight.GetValueOrDefault(kind) + 1;
            MaxInFlight[kind] = Math.Max(MaxInFlight.GetValueOrDefault(kind), InFlight[kind]);
            try
            {
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                Tokens.Add(token);
                Events.Add(record);
                Count++;
                if (Count - 1 == cancelAt)
                {
                    AttemptsAtCancel = Attempts;
                    EventsAtCancel = Events.Count;
                    await source.CancelAsync();
                }
            }
            finally
            {
                InFlight[kind]--;
            }
        }
    }

    private sealed class FakeLlm(Run run, LLMConfig config) : LLMClient(config)
    {
        internal override string PythonName => "FakeLLM";

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
                    ["kind"] = "llm",
                    ["prompt_name"] = promptName,
                    ["messages"] = messages.Select(m => (object?)new List<object?> { m.Role, H(m.Content) }).ToList(),
                    ["response_model"] = responseModel?.PythonName,
                    ["max_tokens"] = maxTokens,
                    ["model_size"] = Py.EnumValue(modelSize),
                    ["group_id"] = groupId,
                    ["attribute_extraction"] = attributeExtraction,
                },
                cancellationToken);
            var llm = Dict(run.Input["llm"]);
            var replies = llm.GetValueOrDefault(promptName ?? string.Empty) is List<object?> { Count: > 0 } list ? list : [new OrderedDictionary<string, object?>()];
            var index = run.LlmIndex.GetValueOrDefault(promptName ?? string.Empty);
            run.LlmIndex[promptName ?? string.Empty] = index + 1;
            var reply = Dict(replies[Math.Min(index, replies.Count - 1)]);
            if (reply.Count == 1 && reply.GetValueOrDefault("$raise") is "RuntimeError")
            {
                throw new InvalidOperationException("llm down");
            }

            return Dict(Py.DeepCopy(reply));
        }
    }

    private sealed class FakeEmbedder(Run run, string mode) : EmbedderClient
    {
        internal override async Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("embed", new OrderedDictionary<string, object?> { ["kind"] = "embed_create", ["input"] = inputData.Select(t => (object?)t).ToList() }, cancellationToken);
            return Vector(inputData[0]);
        }

        internal override async Task<List<List<double>>> CreateBatchAsync(List<string> inputDataList, CancellationToken cancellationToken = default)
        {
            if (mode == "single")
            {
                throw new NotImplementedException(string.Empty);
            }

            await run.EnterAsync("embed", new OrderedDictionary<string, object?> { ["kind"] = "embed_batch", ["input"] = inputDataList.Select(t => (object?)t).ToList() }, cancellationToken);
            var vectors = inputDataList.Select(Vector).ToList();
            if (mode == "short")
            {
                vectors.RemoveAt(vectors.Count - 1);
            }

            return vectors;
        }

        private List<double> Vector(string text)
        {
            run.Texts.Add(text);
            return [run.Texts.Count - 1, 0.5];
        }
    }

    private sealed record FakeOps(Run Run) : GraphOperationsInterface
    {
        internal override async Task<List<EntityNode>> NodeGetByNamesAsync(Type? cls, GraphDriver driver, List<string> names, List<string> groupIds, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(
                "names",
                new OrderedDictionary<string, object?>
                {
                    ["kind"] = "get_by_names",
                    ["names"] = names.Select(n => (object?)n).ToList(),
                    ["group_ids"] = groupIds.Select(g => (object?)g).ToList(),
                },
                cancellationToken);
            var answer = Run.Input.GetValueOrDefault("exact") ?? new List<object?>();
            return answer switch
            {
                "$raise" => throw new InvalidOperationException("names down"),
                "$not_implemented" => throw new NotImplementedException(string.Empty),
                _ => MakeNodes(answer),
            };
        }

        internal override async Task<List<EntityNode>> NodeGetByAttributeValuesAsync(
            Type? cls,
            GraphDriver driver,
            string groupId,
            OrderedDictionary<string, object?> attributes,
            List<string>? nodeLabels = null,
            long limit = 100,
            CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(
                "identity",
                new OrderedDictionary<string, object?>
                {
                    ["kind"] = "get_by_attribute_values",
                    ["group_id"] = groupId,
                    ["attributes"] = new OrderedDictionary<string, object?>(attributes),
                    ["labels"] = nodeLabels?.Select(l => (object?)l).ToList(),
                    ["limit"] = limit,
                },
                cancellationToken);
            switch ((string?)Run.Input.GetValueOrDefault("identity_mode") ?? "returns")
            {
                case "raise":
                    throw new InvalidOperationException("identity down");
                case "not_implemented":
                    throw new NotImplementedException(string.Empty);
            }

            var identity = Dict(Run.Input.GetValueOrDefault("identity") ?? new OrderedDictionary<string, object?>());
            return MakeNodes(identity.GetValueOrDefault(nodeLabels![0]) ?? new List<object?>());
        }
    }

    private sealed record FakeSearch(Run Run) : SearchInterface
    {
        internal override async Task<List<EntityNode>> NodeSimilaritySearchAsync(
            GraphDriver driver,
            List<double> searchVector,
            SearchFilters searchFilter,
            List<string>? groupIds = null,
            long limit = 100,
            double minScore = 0.7,
            CancellationToken cancellationToken = default)
        {
            var key = (int)searchVector[0];
            var text = key < 1000 ? Run.Texts[key] : $"vec{key}";
            await Run.EnterAsync(
                "search",
                new OrderedDictionary<string, object?>
                {
                    ["kind"] = "node_similarity_search",
                    ["query"] = text,
                    ["group_ids"] = groupIds?.Select(g => (object?)g).ToList(),
                    ["limit"] = limit,
                    ["min_score"] = minScore,
                },
                cancellationToken);
            var search = Dict(Run.Input.GetValueOrDefault("search") ?? new OrderedDictionary<string, object?>());
            return MakeNodes(search.GetValueOrDefault(text) ?? new List<object?>());
        }
    }

    private sealed class FakeDriver : GraphDriver
    {
        private readonly Run run;

        internal FakeDriver(Run run, GraphProvider provider)
        {
            this.run = run;
            Provider = provider;
            GraphOperationsInterface = new FakeOps(run);
            SearchInterface = new FakeSearch(run);
        }

        internal override string PythonName => "FakeDriver";

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("query", new OrderedDictionary<string, object?> { ["kind"] = "execute_query", ["keys"] = parameters.Keys.Select(k => (object?)k).ToList() }, cancellationToken);
            return ([], null, null);
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class UnusedCrossEncoder : CrossEncoderClient
    {
        internal override Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    // A log line is one more event, in the order of the calls, with the keys of the scope (`extra`) and the Python name
    // of the exception (`exc_info`).
    private sealed class EventCapture(List<object?> events) : ILoggerFactory
    {
        private readonly MaintenanceLogCapture inner = new();

        // The scopes are shared by each logger of the factory, as the scope provider of Microsoft.Extensions.Logging
        // shares them: a module reads its logger again for each call.
        private readonly Stack<OrderedDictionary<string, object?>> scopes = new();

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new Logger(inner.CreateLogger(categoryName), inner, events, scopes);

        public void Dispose()
        {
        }

        private sealed class Logger(ILogger logger, MaintenanceLogCapture capture, List<object?> events, Stack<OrderedDictionary<string, object?>> scopes) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                var scope = state as OrderedDictionary<string, object?> ?? [];
                scopes.Push(scope);
                return new Pop(scopes);
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                logger.Log(logLevel, eventId, state, exception, formatter);
                var (level, message) = capture.Lines[^1];
                var entry = new OrderedDictionary<string, object?> { ["kind"] = "log", ["level"] = level, ["message"] = Ms.Replace(message, " in <ms> ms") };
                var extra = new OrderedDictionary<string, object?>();
                foreach (var key in ExtraKeys)
                {
                    foreach (var scope in scopes)
                    {
                        if (scope.TryGetValue(key, out var value))
                        {
                            extra[key] = value;
                            break;
                        }
                    }
                }

                if (extra.Count > 0)
                {
                    entry["extra"] = extra;
                }

                if (exception is not null)
                {
                    entry["exc"] = PythonName(exception);
                }

                events.Add(entry);
            }

            private sealed class Pop(Stack<OrderedDictionary<string, object?>> scopes) : IDisposable
            {
                public void Dispose() => scopes.Pop();
            }
        }
    }
}
