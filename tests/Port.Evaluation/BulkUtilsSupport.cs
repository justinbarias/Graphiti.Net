// Fakes and value helpers for the evaluation tests of graphiti_core/utils/bulk_utils.py (plan 0004, layer 7, packet
// P7e). The fakes replace the LLM client, the embedder, and the graph driver with its session, its transaction, its
// graph operations interface, and its search interface, as port/tools/record_bulk_utils.py does in Python. Each fake
// yields once and then records its call (R25). The value helpers of EdgeOperationsValues read the shared forms.
namespace Graphiti.Port.Evaluation;

using System.Security.Cryptography;
using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.EdgeOperationsValues;

/// <summary>The values of the recording of bulk_utils.</summary>
internal static class BulkUtilsValues
{
    internal static readonly OrderedDictionary<string, object?> Document = Load();

    internal static readonly PyDateTime FixedNow = PyDateTime.FromIsoFormat((string)Document["fixed_now"]!);

    internal static readonly OrderedDictionary<string, GraphTypeDescriptor> Models = BuildModels();

    // The view of an entity node in the recording: the view of edge_operations with the name embedding.
    internal static OrderedDictionary<string, object?> NodeViewWithEmbedding(EntityNode node)
    {
        var view = NodeView(node);
        view["name_embedding"] = node.NameEmbedding?.Select(x => (object?)x).ToList();
        return view;
    }

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "bulk_utils.json");
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

/// <summary>
/// One run of a recorded case of bulk_utils: the fakes, the event list, the count of calls in flight, the tokens that
/// the fakes got, and the call at which a fake cancels the token source.
/// </summary>
internal sealed class BulkUtilsRun
{
    private static readonly List<object?> FallbackReplies =
    [
        Reply("extract_nodes.extract_message", new OrderedDictionary<string, object?> { ["extracted_entities"] = new List<object?>() }),
        Reply("extract_nodes.extract_text", new OrderedDictionary<string, object?> { ["extracted_entities"] = new List<object?>() }),
        Reply("extract_nodes.extract_json", new OrderedDictionary<string, object?> { ["extracted_entities"] = new List<object?>() }),
        Reply("extract_edges.edge", new OrderedDictionary<string, object?> { ["edges"] = new List<object?>() }),
        Reply("dedupe_nodes.nodes", new OrderedDictionary<string, object?> { ["entity_resolutions"] = new List<object?>() }),
        Reply("dedupe_edges.resolve_edge", new OrderedDictionary<string, object?> { ["duplicate_facts"] = new List<object?>(), ["contradicted_facts"] = new List<object?>() }),
        Reply("extract_edges.extract_attributes", new OrderedDictionary<string, object?> { ["role"] = "engineer", ["x"] = 1L }),
        Reply("extract_edges.extract_timestamps", new OrderedDictionary<string, object?> { ["valid_at"] = null, ["invalid_at"] = null }),
    ];

    internal BulkUtilsRun(OrderedDictionary<string, object?> input, CancellationTokenSource source, int cancelAt)
    {
        Input = input;
        Source = source;
        CancelAt = cancelAt;
        Replies = [.. List(input.GetValueOrDefault("replies") ?? new List<object?>()), .. FallbackReplies];
        Driver = new FakeDriver(this);
        Embedder = new FakeEmbedder(this);
        Clients = new GraphitiClients
        {
            Driver = Driver,
            LlmClient = new FakeLlm(this),
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

    internal List<object?> Replies { get; }

    internal List<object?> Events { get; } = [];

    internal List<(string Kind, CancellationToken Token)> Tokens { get; } = [];

    // The prompt name and the messages of each LLM call, for a test that reads the text that the port sends.
    internal List<(string? PromptName, List<Message> Messages)> Prompts { get; } = [];

    internal List<string> Texts { get; } = [];

    internal List<(List<double> Vector, string Text)> Vectors { get; } = [];

    internal int Count { get; private set; }

    internal int InFlight { get; private set; }

    internal int MaxInFlight { get; private set; }

    internal FakeDriver Driver { get; }

    internal FakeEmbedder Embedder { get; }

    internal GraphitiClients Clients { get; }

    private static OrderedDictionary<string, object?> Reply(string prompt, object? value) => new()
    {
        ["prompt"] = prompt,
        ["needle"] = string.Empty,
        ["reply"] = value,
    };

    // The common part of each fake call: the token check, a yield, a second token check, and the record (R25). A call
    // that is not counted (close) runs in a finally block, gets no token, and is not a call of the cancellation tests.
    internal async Task EnterAsync(string kind, OrderedDictionary<string, object?> record, CancellationToken cancellationToken, bool counted = true)
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
            if (counted)
            {
                Tokens.Add((kind, cancellationToken));
                if (Count++ == CancelAt)
                {
                    await Source.CancelAsync();
                }
            }
        }
        finally
        {
            InFlight--;
        }
    }

    internal List<double> Vector(string text)
    {
        List<double> value;
        if (Input.GetValueOrDefault("vectors") is OrderedDictionary<string, object?> table && table.TryGetValue(text, out var fixedVector))
        {
            value = [.. List(fixedVector).Select(x => (double)x!)];
        }
        else
        {
            if (!Texts.Contains(text))
            {
                Texts.Add(text);
            }

            value = [Texts.IndexOf(text), 1.0];
        }

        Vectors.Add((value, text));
        return value;
    }

    internal static List<EntityNode> NodesOf(object? items) => [.. List(items ?? new List<object?>()).Select(Node)];

    internal sealed class FakeLlm(BulkUtilsRun run) : LLMClient(new LLMConfig(), false)
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
            run.Prompts.Add((promptName, messages));
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
            foreach (var item in run.Replies.Select(Dict))
            {
                if ((string?)item["prompt"] == promptName && last.Contains((string)item["needle"]!, StringComparison.Ordinal))
                {
                    return (OrderedDictionary<string, object?>)PyJson.Loads(PyJson.Dumps(item["reply"]))!;
                }
            }

            throw new KeyNotFoundException("no reply for " + promptName);
        }
    }

    internal sealed class FakeEmbedder(BulkUtilsRun run) : EmbedderClient
    {
        internal override async Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            if (run.Input.GetValueOrDefault("slow_embed") is true)
            {
                // A call that sleeps past the limit of the write and gives up when the token cancels it.
                await run.EnterAsync("embed_start", new() { ["input"] = inputData.Select(x => (object?)x).ToList() }, cancellationToken);
                await Task.Delay(30_000, cancellationToken);
            }

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

    internal sealed record FakeSearch(BulkUtilsRun Run) : SearchInterface
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
            string? text = null;
            for (var i = Run.Vectors.Count - 1; i >= 0; i--)
            {
                if (Run.Vectors[i].Vector.SequenceEqual(searchVector))
                {
                    text = Run.Vectors[i].Text;
                    break;
                }
            }

            await Run.EnterAsync(
                "node_similarity_search",
                new()
                {
                    ["query"] = text,
                    ["group_ids"] = groupIds?.Select(x => (object?)x).ToList(),
                    ["limit"] = limit,
                    ["min_score"] = minScore,
                },
                cancellationToken);
            var search = Run.Input.GetValueOrDefault("search") as OrderedDictionary<string, object?>;
            return BulkUtilsRun.NodesOf(text is null ? null : search?.GetValueOrDefault(text));
        }
    }

    internal sealed record FakeOps(BulkUtilsRun Run) : GraphOperationsInterface
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
                "retrieve_episodes",
                new()
                {
                    ["reference_time"] = Dt(referenceTime),
                    ["last_n"] = lastN,
                    ["group_ids"] = groupIds?.Select(x => (object?)x).ToList(),
                    ["source"] = source is null ? null : Py.EnumValue(source.Value),
                    ["saga"] = saga,
                },
                cancellationToken);
            var stored = Run.Input.GetValueOrDefault("stored") as OrderedDictionary<string, object?>;
            List<EpisodicNode> found = [];
            foreach (var groupId in groupIds ?? [])
            {
                found.AddRange(List(stored?.GetValueOrDefault(groupId) ?? new List<object?>()).Select(Episode));
            }

            return found;
        }

        internal override async Task<List<EntityNode>> NodeGetByNamesAsync(Type? cls, GraphDriver driver, List<string> names, List<string> groupIds, CancellationToken cancellationToken = default)
        {
            await Run.EnterAsync(
                "node_get_by_names",
                new()
                {
                    ["names"] = names.Select(x => (object?)x).ToList(),
                    ["group_ids"] = groupIds.Select(x => (object?)x).ToList(),
                },
                cancellationToken);
            return [.. BulkUtilsRun.NodesOf(Run.Input.GetValueOrDefault("exact")).Where(n => names.Contains(n.Name))];
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
                "node_get_by_attribute_values",
                new()
                {
                    ["group_id"] = groupId,
                    ["attributes"] = Encode(attributes),
                    ["labels"] = nodeLabels?.Select(x => (object?)x).ToList(),
                    ["limit"] = limit,
                },
                cancellationToken);
            var identity = Run.Input.GetValueOrDefault("identity") as OrderedDictionary<string, object?>;
            return BulkUtilsRun.NodesOf(nodeLabels is null ? null : identity?.GetValueOrDefault(nodeLabels[0]));
        }

        internal override Task EpisodicNodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) =>
            BulkAsync("episodic_node_save_bulk", transaction, nodes, batchSize, cancellationToken);

        internal override Task NodeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) =>
            BulkAsync("node_save_bulk", transaction, nodes, batchSize, cancellationToken);

        internal override Task EpisodicEdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> episodicEdges, long batchSize = 100, CancellationToken cancellationToken = default) =>
            BulkAsync("episodic_edge_save_bulk", transaction, episodicEdges, batchSize, cancellationToken);

        internal override Task EdgeSaveBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default) =>
            BulkAsync("edge_save_bulk", transaction, edges, batchSize, cancellationToken);

        internal override Task NodeUpdateBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> nodes, long batchSize = 100, CancellationToken cancellationToken = default) =>
            BulkAsync("node_update_bulk", transaction, nodes, batchSize, cancellationToken);

        internal override Task EdgeUpdateBulkAsync(Type? cls, GraphDriver driver, Transaction transaction, List<OrderedDictionary<string, object?>> edges, long batchSize = 100, CancellationToken cancellationToken = default) =>
            BulkAsync("edge_update_bulk", transaction, edges, batchSize, cancellationToken);

        private Task BulkAsync(string kind, Transaction transaction, List<OrderedDictionary<string, object?>> items, long batchSize, CancellationToken cancellationToken) =>
            Run.EnterAsync(
                kind,
                new()
                {
                    ["tx"] = transaction is FakeTx ? FakeTx.Name : null,
                    ["records"] = Encode(items),
                    ["batch_size"] = batchSize,
                },
                cancellationToken);
    }

    internal sealed class FakeTx(BulkUtilsRun run) : Transaction
    {
        internal const string Name = "tx";

        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            run.EnterAsync(
                "tx_run",
                new()
                {
                    ["query"] = query,
                    ["keys"] = parameters.Keys.Select(x => (object?)x).ToList(),
                    ["values"] = Encode(parameters.Values.ToList()),
                },
                cancellationToken);
    }

    internal class FakeSession(BulkUtilsRun run) : GraphDriverSession
    {
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) =>
            run.EnterAsync("close", new() { ["query_timeout_ms"] = (this as IQueryTimeoutSession)?.QueryTimeoutMs }, cancellationToken, counted: false);

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, CancellationToken cancellationToken = default) =>
            ExecuteWriteAsync(func, null, cancellationToken);

        // The Python fake records `func.timeout`, which `unit_of_work(timeout=...)` sets (decision D52).
        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, double timeout, CancellationToken cancellationToken = default) =>
            ExecuteWriteAsync(func, (double?)timeout, cancellationToken);

        private async Task ExecuteWriteAsync(Func<Transaction, Task> func, double? timeout, CancellationToken cancellationToken)
        {
            OrderedDictionary<string, object?> record = new() { ["query_timeout_ms"] = (this as IQueryTimeoutSession)?.QueryTimeoutMs, ["timeout"] = timeout };
            if (run.Input.GetValueOrDefault("block_write") is true)
            {
                // The Python fake records at once and then waits for the limit to cancel it, so that a limit below one
                // millisecond cannot race the record.
                run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "execute_write", ["query_timeout_ms"] = record["query_timeout_ms"], ["timeout"] = record["timeout"] });
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return;
            }

            await run.EnterAsync("execute_write", record, cancellationToken);
            try
            {
                await func(new FakeTx(run));
            }
            catch (Exception e)
            {
                // R4: the Python class of the exception that the session rolls back on.
                var name = e switch
                {
                    OperationCanceledException => "CancelledError",
                    InvalidOperationException => "RuntimeError",
                    _ => e.GetType().Name,
                };
                run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "rollback", ["error"] = name });
                throw;
            }
        }
    }

    internal sealed class TimeoutSession(BulkUtilsRun run) : FakeSession(run), IQueryTimeoutSession
    {
        public long? QueryTimeoutMs { get; set; }
    }

    internal sealed class FakeDriver : GraphDriver
    {
        private readonly BulkUtilsRun run;

        internal FakeDriver(BulkUtilsRun run)
        {
            this.run = run;
            Provider = PyEnum.FromValue<GraphProvider>((string?)run.Input.GetValueOrDefault("provider") ?? "neo4j");
            SearchInterface = new FakeSearch(run);
            GraphOperationsInterface = run.Input.GetValueOrDefault("ops") is false ? null : new FakeOps(run);
        }

        internal override string PythonName => "FakeDriver";

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await run.EnterAsync("execute_query", new() { ["query"] = cypherQuery, ["keys"] = parameters.Keys.Select(x => (object?)x).ToList() }, cancellationToken);
            return ([], null, null);
        }

        internal override GraphDriverSession Session(string? database = null)
        {
            run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "session" });
            return run.Input.GetValueOrDefault("timeout_session") is true ? new TimeoutSession(run) : new FakeSession(run);
        }

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
