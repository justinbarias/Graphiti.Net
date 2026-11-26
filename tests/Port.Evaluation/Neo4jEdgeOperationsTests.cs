// Evaluation test for the five edge operations modules of graphiti_core/driver/neo4j/operations/: entity_edge_ops.py,
// episodic_edge_ops.py, community_edge_ops.py, has_episode_edge_ops.py, and next_episode_edge_ops.py (port/RULES.md, R23, R25).
// The recording is values/neo4j_edge_ops.json, which port/tools/record_neo4j_edge_ops.py wrote from Python.
// It holds the calls and the log lines of each case in one ordered list.
namespace Graphiti.Port.Evaluation;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Internal;
using Graphiti.Neo4j.Operations;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

public class Neo4jEdgeOperationsTests
{
    private static readonly List<JsonElement> Cases = Load();

    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    // One case for each method of each class, with and without a transaction, that reaches the executor or the transaction.
    private static readonly Dictionary<string, int> CancelIndexes = SelectCancelCases();

    public static TheoryData<string> CancelNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in CancelIndexes.Keys)
        {
            data.Add(name);
        }

        return data;
    }

    [Fact]
    public void The_recording_holds_each_method_of_each_class_with_and_without_a_transaction()
    {
        var methods = Cases
            .GroupBy(c => c.GetProperty("input").GetProperty("cls").GetString()!)
            .ToDictionary(g => g.Key, g => g.Select(c => c.GetProperty("input").GetProperty("method").GetString()!).Distinct().Order().ToList());
        var common = new[] { "delete", "delete_by_uuids", "get_by_group_ids", "get_by_uuid", "get_by_uuids", "save" };
        Assert.Equal(["CommunityEdge", "EntityEdge", "EpisodicEdge", "HasEpisodeEdge", "NextEpisodeEdge"], methods.Keys.Order());
        Assert.Equal(common, methods["CommunityEdge"]);
        Assert.Equal(common.Append("save_bulk").Order(), methods["EpisodicEdge"]);
        Assert.Equal(common.Append("save_bulk").Order(), methods["HasEpisodeEdge"]);
        Assert.Equal(common.Append("save_bulk").Order(), methods["NextEpisodeEdge"]);
        Assert.Equal(
            common.Concat(["get_between_nodes", "get_by_node_uuid", "load_embeddings", "load_embeddings_bulk", "save_bulk"]).Order(),
            methods["EntityEdge"]);
        foreach (var (cls, names) in methods)
        {
            foreach (var name in names)
            {
                Assert.Contains($"{cls}.{name}", CancelIndexes.Keys.Select(k => k.Split('/')[0]));
                var takesTransaction = name is "save" or "delete" or "delete_by_uuids" or "save_bulk";
                Assert.Equal(takesTransaction, CancelIndexes.ContainsKey($"{cls}.{name}/transaction"));
            }
        }

        Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("events").EnumerateArray().Any(x => x.TryGetProperty("via", out var via) && via.GetString() == "transaction"));
        Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("events").EnumerateArray().Any(x => x.GetProperty("kind").GetString() == "log"));
        Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("error").ValueKind == JsonValueKind.Object);
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public async Task Each_call_the_result_and_the_log_match_Python(int index)
    {
        var input = Cases[index].GetProperty("input");
        var output = Cases[index].GetProperty("output");
        var events = new List<JsonNode>();
        var capture = new CaptureProvider(events);
        using var factory = new CaptureFactory(capture);
        using var scope = PortServices.Use(loggerFactory: factory);

        var (error, result, edge, edgeList) = await Run(input, events, TestContext.Current.CancellationToken);

        var expectedError = output.GetProperty("error");
        if (expectedError.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.NotNull(error);
            var type = expectedError.GetProperty("type").GetString();
            Assert.Equal(
                type switch
                {
                    "EdgeNotFoundError" => typeof(EdgeNotFoundException),
                    "KeyError" => typeof(KeyNotFoundException),
                    "ValidationError" => typeof(ValidationException),
                    "ValueError" => typeof(ArgumentException),
                    "AttributeError" => typeof(MissingMemberException),
                    _ => throw new InvalidOperationException(type),
                },
                error.GetType());

            // The text of a KeyError is the Python repr of the key, and a .NET message has other words.
            if (type != "KeyError")
            {
                // PORT: PyCompat writes the text of pydantic up to the type of each error. It leaves out the input value, the
                // input type, and the link of each error, so the test removes them from the Python text.
                var python = System.Text.RegularExpressions.Regex.Replace(
                    expectedError.GetProperty("text").GetString()!,
                    @", input_value=.*?, input_type=\w+\]\n    For further information visit \S+",
                    "]",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant | System.Text.RegularExpressions.RegexOptions.Singleline);
                Assert.Equal(python, error.Message);
            }
        }

        AssertSame(output.GetProperty("events"), new JsonArray([.. events]));
        AssertSame(output.GetProperty("result"), Encode(result));

        if (output.TryGetProperty("embeddings", out var embeddings))
        {
            List<Edge> actual = edgeList ?? (edge is null ? [] : [edge]);
            var expected = embeddings.EnumerateArray().ToList();
            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < actual.Count; i++)
            {
                AssertSame(expected[i], Encode(((EntityEdge)actual[i]).FactEmbedding));
            }
        }
    }

    [Theory]
    [MemberData(nameof(CancelNames))]
    public async Task Each_async_method_throws_for_a_canceled_token(string name)
    {
        var input = Cases[CancelIndexes[name]].GetProperty("input");
        var events = new List<JsonNode>();
        var (error, _, _, _) = await Run(input, events, new CancellationToken(true));
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Empty(events);
    }

    private static Dictionary<string, int> SelectCancelCases()
    {
        var selected = new Dictionary<string, int>();
        for (var i = 0; i < Cases.Count; i++)
        {
            var input = Cases[i].GetProperty("input");
            var calls = Cases[i].GetProperty("output").GetProperty("events").EnumerateArray().Where(x => x.GetProperty("kind").GetString() == "call").ToList();
            if (calls.Count == 0)
            {
                continue;
            }

            var name = input.GetProperty("cls").GetString() + "." + input.GetProperty("method").GetString() + "/" + calls[0].GetProperty("via").GetString();
            selected.TryAdd(name, i);
        }

        return selected;
    }

    private static async Task<(Exception? Error, object? Result, Edge? Edge, List<Edge>? EdgeList)> Run(
        JsonElement input,
        List<JsonNode> events,
        CancellationToken cancellationToken)
    {
        var cls = input.GetProperty("cls").GetString()!;
        var method = input.GetProperty("method").GetString()!;
        var records = input.TryGetProperty("records", out var recordsElement)
            ? recordsElement.EnumerateArray().Select(r => (OrderedDictionary<string, object?>)Read(r)!).ToList()
            : [];
        var executor = new FakeExecutor(records, events);
        var useTx = input.TryGetProperty("use_tx", out var useTxElement) && useTxElement.GetBoolean();
        var transaction = useTx ? new FakeTransaction(events) : null;
        var ops = OpsFor(cls);
        var edge = input.TryGetProperty("edge", out var edgeElement) ? Construct(cls, (OrderedDictionary<string, object?>)Read(edgeElement)!) : null;
        long? batchSize = input.TryGetProperty("batch_size", out var batchElement) ? batchElement.GetInt64() : null;
        List<string> Uuids() => input.GetProperty("uuids").EnumerateArray().Select(u => u.GetString()!).ToList();

        Exception? error = null;
        object? result = null;
        List<Edge>? edgeList = null;
        try
        {
            switch (method)
            {
                case "save":
                    await ops.Save(executor, edge!, transaction, cancellationToken);
                    break;
                case "delete":
                    await ops.Delete(executor, edge!, transaction, cancellationToken);
                    break;
                case "save_bulk":
                    var edges = input.GetProperty("edges").EnumerateArray()
                        .Select(e => Construct(cls, (OrderedDictionary<string, object?>)Read(e)!))
                        .ToList();
                    await ops.SaveBulk!(executor, edges, transaction, batchSize, cancellationToken);
                    break;
                case "delete_by_uuids":
                    await ops.DeleteByUuids(executor, Uuids(), transaction, cancellationToken);
                    break;
                case "get_by_uuid":
                    result = await ops.GetByUuid(executor, input.GetProperty("uuid").GetString()!, cancellationToken);
                    break;
                case "get_by_uuids":
                    result = await ops.GetByUuids(executor, Uuids(), cancellationToken);
                    break;
                case "get_by_group_ids":
                    var groupIds = input.GetProperty("group_ids").EnumerateArray().Select(g => g.GetString()!).ToList();
                    result = input.TryGetProperty("defaults", out _)
                        ? await ops.GetByGroupIds(executor, groupIds, null, null, cancellationToken)
                        : await ops.GetByGroupIds(
                            executor,
                            groupIds,
                            input.GetProperty("limit").ValueKind == JsonValueKind.Null ? null : input.GetProperty("limit").GetInt64(),
                            input.GetProperty("uuid_cursor").GetString(),
                            cancellationToken);
                    break;
                case "get_between_nodes":
                    result = await new Neo4jEntityEdgeOperations().GetBetweenNodesAsync(
                        executor,
                        input.GetProperty("source").GetString()!,
                        input.GetProperty("target").GetString()!,
                        cancellationToken);
                    break;
                case "get_by_node_uuid":
                    result = await new Neo4jEntityEdgeOperations().GetByNodeUuidAsync(executor, input.GetProperty("node_uuid").GetString()!, cancellationToken);
                    break;
                case "load_embeddings":
                    await new Neo4jEntityEdgeOperations().LoadEmbeddingsAsync(executor, (EntityEdge)edge!, cancellationToken);
                    break;
                default:
                    var template = (OrderedDictionary<string, object?>)Read(input.GetProperty("edge"))!;
                    var loaded = Uuids()
                        .Select(u => EntityEdge.ModelValidate(new OrderedDictionary<string, object?>(template) { ["uuid"] = u }))
                        .ToList();
                    edgeList = [.. loaded];
                    var operations = new Neo4jEntityEdgeOperations();
                    if (batchSize is null)
                    {
                        await operations.LoadEmbeddingsBulkAsync(executor, loaded, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await operations.LoadEmbeddingsBulkAsync(executor, loaded, batchSize.Value, cancellationToken);
                    }

                    break;
            }
        }
        catch (Exception e)
        {
            error = e;
        }

        return (error, result, edge, edgeList);
    }

    private sealed record Ops(
        Func<QueryExecutor, Edge, Transaction?, CancellationToken, Task> Save,
        Func<QueryExecutor, List<Edge>, Transaction?, long?, CancellationToken, Task>? SaveBulk,
        Func<QueryExecutor, Edge, Transaction?, CancellationToken, Task> Delete,
        Func<QueryExecutor, List<string>, Transaction?, CancellationToken, Task> DeleteByUuids,
        Func<QueryExecutor, string, CancellationToken, Task<object?>> GetByUuid,
        Func<QueryExecutor, List<string>, CancellationToken, Task<object?>> GetByUuids,
        Func<QueryExecutor, List<string>, long?, string?, CancellationToken, Task<object?>> GetByGroupIds);

    // The batch size is null for a call that leaves it out, so that the default of the method runs.
    private static Ops OpsFor(string cls)
    {
        switch (cls)
        {
            case "EntityEdge":
                var entity = new Neo4jEntityEdgeOperations();
                return new Ops(
                    (ex, e, tx, ct) => entity.SaveAsync(ex, (EntityEdge)e, tx, ct),
                    (ex, e, tx, size, ct) => size is null
                        ? entity.SaveBulkAsync(ex, [.. e.Cast<EntityEdge>()], tx, cancellationToken: ct)
                        : entity.SaveBulkAsync(ex, [.. e.Cast<EntityEdge>()], tx, size.Value, ct),
                    (ex, e, tx, ct) => entity.DeleteAsync(ex, (EntityEdge)e, tx, ct),
                    (ex, u, tx, ct) => entity.DeleteByUuidsAsync(ex, u, tx, ct),
                    async (ex, u, ct) => await entity.GetByUuidAsync(ex, u, ct),
                    async (ex, u, ct) => await entity.GetByUuidsAsync(ex, u, ct),
                    async (ex, g, limit, cursor, ct) => limit is null && cursor is null
                        ? await entity.GetByGroupIdsAsync(ex, g, cancellationToken: ct)
                        : await entity.GetByGroupIdsAsync(ex, g, limit, cursor, ct));
            case "EpisodicEdge":
                var episodic = new Neo4jEpisodicEdgeOperations();
                return new Ops(
                    (ex, e, tx, ct) => episodic.SaveAsync(ex, (EpisodicEdge)e, tx, ct),
                    (ex, e, tx, size, ct) => size is null
                        ? episodic.SaveBulkAsync(ex, [.. e.Cast<EpisodicEdge>()], tx, cancellationToken: ct)
                        : episodic.SaveBulkAsync(ex, [.. e.Cast<EpisodicEdge>()], tx, size.Value, ct),
                    (ex, e, tx, ct) => episodic.DeleteAsync(ex, (EpisodicEdge)e, tx, ct),
                    (ex, u, tx, ct) => episodic.DeleteByUuidsAsync(ex, u, tx, ct),
                    async (ex, u, ct) => await episodic.GetByUuidAsync(ex, u, ct),
                    async (ex, u, ct) => await episodic.GetByUuidsAsync(ex, u, ct),
                    async (ex, g, limit, cursor, ct) => limit is null && cursor is null
                        ? await episodic.GetByGroupIdsAsync(ex, g, cancellationToken: ct)
                        : await episodic.GetByGroupIdsAsync(ex, g, limit, cursor, ct));
            case "CommunityEdge":
                var community = new Neo4jCommunityEdgeOperations();
                return new Ops(
                    (ex, e, tx, ct) => community.SaveAsync(ex, (CommunityEdge)e, tx, ct),
                    null,
                    (ex, e, tx, ct) => community.DeleteAsync(ex, (CommunityEdge)e, tx, ct),
                    (ex, u, tx, ct) => community.DeleteByUuidsAsync(ex, u, tx, ct),
                    async (ex, u, ct) => await community.GetByUuidAsync(ex, u, ct),
                    async (ex, u, ct) => await community.GetByUuidsAsync(ex, u, ct),
                    async (ex, g, limit, cursor, ct) => limit is null && cursor is null
                        ? await community.GetByGroupIdsAsync(ex, g, cancellationToken: ct)
                        : await community.GetByGroupIdsAsync(ex, g, limit, cursor, ct));
            case "HasEpisodeEdge":
                var hasEpisode = new Neo4jHasEpisodeEdgeOperations();
                return new Ops(
                    (ex, e, tx, ct) => hasEpisode.SaveAsync(ex, (HasEpisodeEdge)e, tx, ct),
                    (ex, e, tx, size, ct) => size is null
                        ? hasEpisode.SaveBulkAsync(ex, [.. e.Cast<HasEpisodeEdge>()], tx, cancellationToken: ct)
                        : hasEpisode.SaveBulkAsync(ex, [.. e.Cast<HasEpisodeEdge>()], tx, size.Value, ct),
                    (ex, e, tx, ct) => hasEpisode.DeleteAsync(ex, (HasEpisodeEdge)e, tx, ct),
                    (ex, u, tx, ct) => hasEpisode.DeleteByUuidsAsync(ex, u, tx, ct),
                    async (ex, u, ct) => await hasEpisode.GetByUuidAsync(ex, u, ct),
                    async (ex, u, ct) => await hasEpisode.GetByUuidsAsync(ex, u, ct),
                    async (ex, g, limit, cursor, ct) => limit is null && cursor is null
                        ? await hasEpisode.GetByGroupIdsAsync(ex, g, cancellationToken: ct)
                        : await hasEpisode.GetByGroupIdsAsync(ex, g, limit, cursor, ct));
            default:
                var next = new Neo4jNextEpisodeEdgeOperations();
                return new Ops(
                    (ex, e, tx, ct) => next.SaveAsync(ex, (NextEpisodeEdge)e, tx, ct),
                    (ex, e, tx, size, ct) => size is null
                        ? next.SaveBulkAsync(ex, [.. e.Cast<NextEpisodeEdge>()], tx, cancellationToken: ct)
                        : next.SaveBulkAsync(ex, [.. e.Cast<NextEpisodeEdge>()], tx, size.Value, ct),
                    (ex, e, tx, ct) => next.DeleteAsync(ex, (NextEpisodeEdge)e, tx, ct),
                    (ex, u, tx, ct) => next.DeleteByUuidsAsync(ex, u, tx, ct),
                    async (ex, u, ct) => await next.GetByUuidAsync(ex, u, ct),
                    async (ex, u, ct) => await next.GetByUuidsAsync(ex, u, ct),
                    async (ex, g, limit, cursor, ct) => limit is null && cursor is null
                        ? await next.GetByGroupIdsAsync(ex, g, cancellationToken: ct)
                        : await next.GetByGroupIdsAsync(ex, g, limit, cursor, ct));
        }
    }

    private static Edge Construct(string cls, OrderedDictionary<string, object?> data) => cls switch
    {
        "EpisodicEdge" => EpisodicEdge.ModelValidate(data),
        "CommunityEdge" => CommunityEdge.ModelValidate(data),
        "HasEpisodeEdge" => HasEpisodeEdge.ModelValidate(data),
        "NextEpisodeEdge" => NextEpisodeEdge.ModelValidate(data),
        _ => EntityEdge.ModelValidate(data),
    };

    private static OrderedDictionary<string, object?> Dump(Edge edge) => edge switch
    {
        EpisodicEdge e => e.ModelDump(),
        EntityEdge e => e.ModelDump(),
        CommunityEdge e => e.ModelDump(),
        HasEpisodeEdge e => e.ModelDump(),
        NextEpisodeEdge e => e.ModelDump(),
        _ => throw new InvalidOperationException(edge.GetType().Name),
    };

    private static void AssertSame(JsonElement expected, JsonNode? actual)
    {
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expected.GetRawText()), actual),
            $"expected {expected.GetRawText()} but got {actual?.ToJsonString()}");
    }

    private static JsonNode Encode(object? value) => value switch
    {
        null => new JsonObject { ["type"] = "none" },
        bool b => new JsonObject { ["type"] = "bool", ["value"] = b },
        int or long => new JsonObject { ["type"] = "int", ["value"] = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) },
        double d => new JsonObject { ["type"] = "float", ["value"] = d },
        string s => new JsonObject { ["type"] = "str", ["value"] = s },
        PyDateTime dt => new JsonObject { ["type"] = "datetime", ["value"] = dt.IsoFormat() },
        Edge e => new JsonObject { ["type"] = "edge", ["class"] = e.GetType().Name, ["dump"] = Encode(Dump(e)) },
        OrderedDictionary<string, object?> dict => new JsonObject
        {
            ["type"] = "dict",
            ["items"] = new JsonArray(dict.Select(kv => (JsonNode?)new JsonArray(kv.Key, Encode(kv.Value))).ToArray()),
        },
        System.Collections.IEnumerable items => new JsonObject
        {
            ["type"] = "list",
            ["items"] = new JsonArray(items.Cast<object?>().Select(Encode).ToArray()),
        },
        _ => new JsonObject { ["type"] = value.GetType().Name },
    };

    private static object? Read(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => e.GetString(),
        // A cast to object keeps an int a long: the ternary of a double and a long is a double.
        JsonValueKind.Number => e.GetRawText().Any(c => c is '.' or 'e' or 'E') ? e.GetDouble() : (object)e.GetInt64(),
        JsonValueKind.Array => e.EnumerateArray().Select(Read).ToList(),
        JsonValueKind.Object => ReadObject(e),
        _ => throw new InvalidOperationException(e.ValueKind.ToString()),
    };

    private static OrderedDictionary<string, object?> ReadObject(JsonElement e)
    {
        var dict = new OrderedDictionary<string, object?>();
        foreach (var p in e.EnumerateObject())
        {
            dict[p.Name] = Read(p.Value);
        }

        return dict;
    }

    private static List<JsonElement> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "neo4j_edge_ops.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToList();
    }

    private static JsonObject Note(string via, string query, OrderedDictionary<string, object?> parameters) =>
        new JsonObject
        {
            ["kind"] = "call",
            ["via"] = via,
            ["query"] = query,
            ["keys"] = new JsonArray([.. parameters.Keys.Select(k => (JsonNode?)JsonValue.Create(k))]),
            ["values"] = new JsonArray([.. parameters.Values.Select(v => (JsonNode?)Encode(v))]),
        };

    private sealed class FakeExecutor(List<OrderedDictionary<string, object?>> records, List<JsonNode> events) : QueryExecutor
    {
        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            events.Add(Note("executor", cypherQuery, parameters));
            return (records, null, null);
        }
    }

    private sealed class FakeTransaction(List<JsonNode> events) : Transaction
    {
        internal override async Task RunAsync(
            string query,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            events.Add(Note("transaction", query, parameters));
        }
    }

    private sealed class CaptureFactory(CaptureProvider provider) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider1)
        {
        }

        public ILogger CreateLogger(string categoryName) => provider.CreateLogger(categoryName);

        public void Dispose()
        {
        }
    }

    private sealed class CaptureProvider(List<JsonNode> events) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(events);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<JsonNode> events) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                events.Add(new JsonObject
                {
                    ["kind"] = "log",
                    ["level"] = logLevel == LogLevel.Debug ? "DEBUG" : logLevel.ToString(),
                    ["message"] = formatter(state, exception),
                });
        }
    }
}
