// Evaluation test for graphiti_core/driver/neo4j/operations/entity_node_ops.py (port/RULES.md, R23, R25).
// The recording is values/neo4j_entity_node_ops.json, which port/tools/record_entity_node_ops.py wrote from Python.
// save, save_bulk, get_by_uuid, get_by_uuids, and get_by_group_ids call a skeleton member, so they are not recorded.
namespace Graphiti.Port.Evaluation;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Internal;
using Graphiti.Neo4j.Operations;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

public class Neo4jEntityNodeOperationsTests
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

    [Fact]
    public void The_recording_holds_each_recorded_method_with_and_without_a_transaction()
    {
        var methods = Cases.Select(c => c.GetProperty("input").GetProperty("method").GetString()).Distinct().Order().ToList();
        Assert.Equal(["delete", "delete_by_group_id", "delete_by_uuids", "load_embeddings", "load_embeddings_bulk"], methods);
        Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("calls").EnumerateArray().Any(x => x.GetProperty("via").GetString() == "transaction"));
        Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("calls").EnumerateArray().Any(x => x.GetProperty("via").GetString() == "executor"));
        Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("error").ValueKind == JsonValueKind.Object);
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public async Task Each_call_the_result_and_the_log_match_Python(int index)
    {
        var input = Cases[index].GetProperty("input");
        var output = Cases[index].GetProperty("output");
        var method = input.GetProperty("method").GetString();
        var records = input.TryGetProperty("records", out var recordsElement)
            ? recordsElement.EnumerateArray().Select(r => (OrderedDictionary<string, object?>)Read(r)!).ToList()
            : [];
        var executor = new FakeExecutor(records);
        var transaction = input.TryGetProperty("use_tx", out var useTx) && useTx.GetBoolean() ? new FakeTransaction() : null;
        var uuids = input.TryGetProperty("uuids", out var uuidsElement) ? uuidsElement.EnumerateArray().Select(u => u.GetString()!).ToList() : [];
        var nodes = uuids.Select(u => new EntityNode { Uuid = u, Name = "n", GroupId = "g" }).ToList();
        var node = method is not "delete_by_uuids" && input.TryGetProperty("uuid", out var uuid)
            ? new EntityNode { Uuid = uuid.GetString()!, Name = "n", GroupId = "g" }
            : null;
        long BatchSize() => input.GetProperty("batch_size").GetInt64();

        var capture = new CaptureProvider();
        using var factory = new CaptureFactory(capture);
        using var scope = PortServices.Use(loggerFactory: factory);
        var operations = new Neo4jEntityNodeOperations();

        Exception? error = null;
        try
        {
            switch (method)
            {
                case "delete":
                    await operations.DeleteAsync(executor, node!, transaction, TestContext.Current.CancellationToken);
                    break;
                case "delete_by_group_id":
                    await operations.DeleteByGroupIdAsync(executor, input.GetProperty("group_id").GetString()!, transaction, BatchSize(), TestContext.Current.CancellationToken);
                    break;
                case "delete_by_uuids":
                    await operations.DeleteByUuidsAsync(executor, uuids, transaction, BatchSize(), TestContext.Current.CancellationToken);
                    break;
                case "load_embeddings":
                    await operations.LoadEmbeddingsAsync(executor, node!, TestContext.Current.CancellationToken);
                    break;
                default:
                    await operations.LoadEmbeddingsBulkAsync(executor, nodes, cancellationToken: TestContext.Current.CancellationToken);
                    break;
            }
        }
        catch (Exception e) when (e is NodeNotFoundException or KeyNotFoundException or ValidationException)
        {
            error = e;
        }

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
                    "NodeNotFoundError" => typeof(NodeNotFoundException),
                    "KeyError" => typeof(KeyNotFoundException),
                    "ValidationError" => typeof(ValidationException),
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

        var calls = executor.Calls.Concat(transaction?.Calls ?? []).ToList();
        var expectedCalls = output.GetProperty("calls").EnumerateArray().ToList();
        Assert.Equal(expectedCalls.Count, calls.Count);
        for (var i = 0; i < calls.Count; i++)
        {
            Assert.Equal(expectedCalls[i].GetProperty("via").GetString(), calls[i].Via);
            Assert.Equal(expectedCalls[i].GetProperty("query").GetString(), calls[i].Query);
            Assert.Equal(expectedCalls[i].GetProperty("keys").EnumerateArray().Select(k => k.GetString()), calls[i].Keys);
            var expectedValues = expectedCalls[i].GetProperty("values").EnumerateArray().ToList();
            Assert.Equal(expectedValues.Count, calls[i].Values.Count);
            for (var j = 0; j < expectedValues.Count; j++)
            {
                AssertSame(expectedValues[j], calls[i].Values[j]);
            }
        }

        var expectedLogs = output.GetProperty("logs").EnumerateArray().Select(l => (l.GetProperty("level").GetString(), l.GetProperty("message").GetString())).ToList();
        Assert.Equal(expectedLogs, capture.Lines);

        if (output.TryGetProperty("embeddings", out var embeddings))
        {
            var actual = method == "load_embeddings" ? [node!] : nodes;
            var expected = embeddings.EnumerateArray().ToList();
            Assert.Equal(expected.Count, actual.Count);
            for (var i = 0; i < actual.Count; i++)
            {
                AssertSame(expected[i], Encode(actual[i].NameEmbedding));
            }
        }
    }

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
        var path = Path.Combine(AppContext.BaseDirectory, "values", "neo4j_entity_node_ops.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToList();
    }

    private sealed record Call(string Via, string Query, List<string> Keys, List<JsonNode> Values);

    private static Call Note(string via, string query, OrderedDictionary<string, object?> parameters) =>
        new(via, query, [.. parameters.Keys], [.. parameters.Values.Select(Encode)]);

    private sealed class FakeExecutor(List<OrderedDictionary<string, object?>> records) : QueryExecutor
    {
        internal List<Call> Calls { get; } = [];

        internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(Note("executor", cypherQuery, parameters));
            return Task.FromResult<(List<OrderedDictionary<string, object?>>, object?, object?)>((records, null, null));
        }
    }

    private sealed class FakeTransaction : Transaction
    {
        internal List<Call> Calls { get; } = [];

        internal override Task RunAsync(
            string query,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(Note("transaction", query, parameters));
            return Task.CompletedTask;
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

    private sealed class CaptureProvider : ILoggerProvider
    {
        internal List<(string?, string?)> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Lines);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<(string?, string?)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Add((logLevel == LogLevel.Debug ? "DEBUG" : logLevel.ToString(), formatter(state, exception)));
        }
    }
}
