// Evaluation test for graphiti_core/driver/neo4j/operations/entity_node_ops.py (port/RULES.md, R23, R25).
// The recording is values/neo4j_entity_node_ops.json, which port/tools/record_entity_node_ops.py wrote from Python. A recorded run holds the
// calls and the log lines in one list in the order of their time. The last theory has no Python original: it tests the token rule of R8.
// No method of the class awaits more than one call, so no case cancels the token after call k: the theory cancels the token before the call
// and expects no call.
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

    // One case for each method, and for each method that takes a transaction, one case with and one without it.
    public static TheoryData<int> CancellationIndexes()
    {
        var data = new TheoryData<int>();
        var seen = new HashSet<string>();
        for (var i = 0; i < Cases.Count; i++)
        {
            var input = Cases[i].GetProperty("input");
            var output = Cases[i].GetProperty("output");
            var key = input.GetProperty("method").GetString() + "." + (input.TryGetProperty("use_tx", out var useTx) ? useTx.GetBoolean().ToString() : "none");
            if (output.GetProperty("error").ValueKind == JsonValueKind.Null
                && output.GetProperty("events").EnumerateArray().Any(e => e.GetProperty("kind").GetString() == "call")
                && seen.Add(key))
            {
                data.Add(i);
            }
        }

        return data;
    }

    [Fact]
    public void The_recording_holds_each_method_with_and_without_a_transaction()
    {
        var methods = Cases.Select(c => c.GetProperty("input").GetProperty("method").GetString()).Distinct().Order().ToList();
        Assert.Equal(
            ["delete", "delete_by_group_id", "delete_by_uuids", "get_by_group_ids", "get_by_uuid", "get_by_uuids", "load_embeddings", "load_embeddings_bulk", "save", "save_bulk"],
            methods);
        Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("events").EnumerateArray().Any(x => x.TryGetProperty("via", out var via) && via.GetString() == "transaction"));
        Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("events").EnumerateArray().Any(x => x.TryGetProperty("via", out var via) && via.GetString() == "executor"));
        Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("error").ValueKind == JsonValueKind.Object);
        Assert.Equal(
            methods,
            CancellationIndexes().Select(i => Cases[i.Data].GetProperty("input").GetProperty("method").GetString()).Distinct().Order().ToList());
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(CaseIndexes))]
    public async Task Each_call_the_result_and_the_log_match_Python(int index)
    {
        var input = Cases[index].GetProperty("input");
        var output = Cases[index].GetProperty("output");
        var events = new List<object>();
        var executor = new FakeExecutor(Records(input), events);
        var transaction = input.TryGetProperty("use_tx", out var useTx) && useTx.GetBoolean() ? new FakeTransaction(events) : null;

        var capture = new CaptureProvider(events);
        using var factory = new CaptureFactory(capture);
        using var scope = PortServices.Use(loggerFactory: factory);

        Exception? error = null;
        object? result = null;
        try
        {
            result = await RunAsync(input, executor, transaction, TestContext.Current.CancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
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
                    "ValueError" => typeof(ArgumentException),
                    "AttributeError" => typeof(MissingMemberException),
                    "TypeError" => typeof(InvalidCastException),
                    _ => throw new InvalidOperationException(type),
                },
                error.GetType());

            // The text of a KeyError is the Python repr of the key, and a .NET message has other words.
            // PORT: the text of the TypeError of `dynamic_label in labels` for a labels value of None differs: Py.AsList in the record
            // parser writes "expected list as List<object?>, got NoneType as nu...". The record parser is not a file of this unit, so
            // the test checks the type of this error and not its text. The report names the difference.
            if (type is not ("KeyError" or "TypeError"))
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

        var expectedEvents = output.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(expectedEvents.Count, events.Count);
        for (var i = 0; i < events.Count; i++)
        {
            switch (events[i])
            {
                case Call call:
                    Assert.Equal("call", expectedEvents[i].GetProperty("kind").GetString());
                    Assert.Equal(expectedEvents[i].GetProperty("via").GetString(), call.Via);
                    Assert.Equal(expectedEvents[i].GetProperty("query").GetString(), call.Query);
                    Assert.Equal(expectedEvents[i].GetProperty("keys").EnumerateArray().Select(k => k.GetString()), call.Keys);
                    var expectedValues = expectedEvents[i].GetProperty("values").EnumerateArray().ToList();
                    Assert.Equal(expectedValues.Count, call.Values.Count);
                    for (var j = 0; j < expectedValues.Count; j++)
                    {
                        AssertSame(expectedValues[j], call.Values[j]);
                    }

                    break;
                case LogLine line:
                    Assert.Equal("log", expectedEvents[i].GetProperty("kind").GetString());
                    Assert.Equal(expectedEvents[i].GetProperty("level").GetString(), line.Level);
                    Assert.Equal(expectedEvents[i].GetProperty("message").GetString(), line.Message);
                    break;
                default:
                    throw new InvalidOperationException();
            }
        }

        if (output.TryGetProperty("result", out var expectedResult))
        {
            AssertSame(expectedResult, Encode(result));
        }
        else
        {
            Assert.Null(result);
        }
    }

    // No Python original. The fake throws OperationCanceledException for a canceled token, as a driver does.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(CancellationIndexes))]
    public async Task A_canceled_token_stops_each_async_method(int index)
    {
        var input = Cases[index].GetProperty("input");
        var events = new List<object>();
        var executor = new FakeExecutor(Records(input), events);
        var transaction = input.TryGetProperty("use_tx", out var useTx) && useTx.GetBoolean() ? new FakeTransaction(events) : null;

        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => RunAsync(input, executor, transaction, source.Token));
        Assert.Empty(events);
    }

    private static List<OrderedDictionary<string, object?>> Records(JsonElement input) =>
        input.TryGetProperty("records", out var records)
            ? records.EnumerateArray().Select(r => (OrderedDictionary<string, object?>)Read(r)!).ToList()
            : [];

    private static async Task<object?> RunAsync(JsonElement input, FakeExecutor executor, FakeTransaction? tx, CancellationToken cancellationToken)
    {
        var method = input.GetProperty("method").GetString();
        var defaults = input.TryGetProperty("defaults", out var d) && d.GetBoolean();
        long BatchSize() => input.GetProperty("batch_size").GetInt64();
        string Text(string name) => input.GetProperty(name).GetString()!;
        List<string> Texts(string name) => input.GetProperty(name).EnumerateArray().Select(u => u.GetString()!).ToList();
        string? NullableText(string name) => input.GetProperty(name).GetString();
        long? Limit() => input.GetProperty("limit").ValueKind == JsonValueKind.Null ? null : input.GetProperty("limit").GetInt64();
        List<EntityNode> NodeList() =>
            input.GetProperty("nodes").EnumerateArray().Select(n => EntityNode.ModelValidate((OrderedDictionary<string, object?>)Read(n)!)).ToList();
        EntityNode OneNode() => EntityNode.ModelValidate((OrderedDictionary<string, object?>)Read(input.GetProperty("node"))!);

        var ops = new Neo4jEntityNodeOperations();
        switch (method)
        {
            case "save":
                if (defaults)
                {
                    await ops.SaveAsync(executor, OneNode(), cancellationToken: cancellationToken);
                }
                else
                {
                    await ops.SaveAsync(executor, OneNode(), tx, cancellationToken);
                }

                return null;
            case "save_bulk":
                if (defaults)
                {
                    await ops.SaveBulkAsync(executor, NodeList(), cancellationToken: cancellationToken);
                }
                else
                {
                    await ops.SaveBulkAsync(executor, NodeList(), tx, BatchSize(), cancellationToken);
                }

                return null;
            case "delete":
                if (defaults)
                {
                    await ops.DeleteAsync(executor, OneNode(), cancellationToken: cancellationToken);
                }
                else
                {
                    await ops.DeleteAsync(executor, OneNode(), tx, cancellationToken);
                }

                return null;
            case "delete_by_group_id":
                if (defaults)
                {
                    await ops.DeleteByGroupIdAsync(executor, Text("group_id"), cancellationToken: cancellationToken);
                }
                else
                {
                    await ops.DeleteByGroupIdAsync(executor, Text("group_id"), tx, BatchSize(), cancellationToken);
                }

                return null;
            case "delete_by_uuids":
                if (defaults)
                {
                    await ops.DeleteByUuidsAsync(executor, Texts("uuids"), cancellationToken: cancellationToken);
                }
                else
                {
                    await ops.DeleteByUuidsAsync(executor, Texts("uuids"), tx, BatchSize(), cancellationToken);
                }

                return null;
            case "get_by_uuid":
                return await ops.GetByUuidAsync(executor, Text("uuid"), cancellationToken);
            case "get_by_uuids":
                return await ops.GetByUuidsAsync(executor, Texts("uuids"), cancellationToken);
            case "get_by_group_ids":
                return defaults
                    ? await ops.GetByGroupIdsAsync(executor, Texts("group_ids"), cancellationToken: cancellationToken)
                    : await ops.GetByGroupIdsAsync(executor, Texts("group_ids"), Limit(), NullableText("uuid_cursor"), cancellationToken);
            case "load_embeddings":
                {
                    var node = OneNode();
                    await ops.LoadEmbeddingsAsync(executor, node, cancellationToken);
                    return node.NameEmbedding;
                }

            case "load_embeddings_bulk":
                {
                    var nodes = NodeList();
                    await ops.LoadEmbeddingsBulkAsync(executor, nodes, cancellationToken: cancellationToken);
                    return nodes.Select(n => n.NameEmbedding).ToList();
                }

            default:
                throw new InvalidOperationException(method);
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
        PyDateTime dt => new JsonObject { ["type"] = "datetime", ["value"] = dt.IsoFormat() },
        EpisodeType e => new JsonObject { ["type"] = "enum", ["value"] = Py.Str(Py.EnumValue(e)) },
        Node node => new JsonObject
        {
            ["type"] = node.PythonName,
            ["items"] = new JsonArray(((IPydanticModel)node).ModelDump(false).Select(kv => (JsonNode?)new JsonArray(kv.Key, Encode(kv.Value))).ToArray()),
        },
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

    private static object? ReadObject(JsonElement e)
    {
        // A case writes a datetime as {"$dt": "<isoformat>"}.
        if (e.TryGetProperty("$dt", out var text) && e.EnumerateObject().Count() == 1)
        {
            return PyDateTime.FromIsoFormat(text.GetString()!);
        }

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

    private sealed record LogLine(string Level, string Message);

    private static Call Note(string via, string query, OrderedDictionary<string, object?> parameters) =>
        new(via, query, [.. parameters.Keys], [.. parameters.Values.Select(Encode)]);

    private sealed class FakeExecutor(List<OrderedDictionary<string, object?>> records, List<object> events) : QueryExecutor
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

    private sealed class FakeTransaction(List<object> events) : Transaction
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

    private sealed class CaptureProvider(List<object> events) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(events);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<object> events) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                events.Add(new LogLine(
                    logLevel switch
                    {
                        LogLevel.Debug => "DEBUG",
                        LogLevel.Information => "INFO",
                        LogLevel.Warning => "WARNING",
                        LogLevel.Error => "ERROR",
                        _ => logLevel.ToString(),
                    },
                    formatter(state, exception)));
        }
    }
}
