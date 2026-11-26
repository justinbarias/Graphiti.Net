// Evaluation test for graphiti_core/driver/neo4j/operations/graph_ops.py (port/RULES.md, R23, R25).
// The recording is values/neo4j_graph_ops.json, which port/tools/record_graph_ops.py wrote from Python.
// get_community_clusters runs label_propagation, which does not end for some graphs (upstream defect U11, GAPS F6).
// The recording leaves out each graph for which Python does not end, and each case here has a time limit of 5 seconds.
namespace Graphiti.Port.Evaluation;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Internal;
using Graphiti.Neo4j.Operations;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

public class Neo4jGraphOperationsTests
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

    public static TheoryData<string> Methods() =>
        [
            "clear_data",
            "clear_data_with_groups",
            "build_indices_and_constraints",
            "build_indices_and_constraints_delete_existing",
            "delete_all_indexes",
            "get_community_clusters",
            "get_community_clusters_with_groups",
            "determine_entity_community",
            "get_mentioned_nodes",
            "get_communities_by_nodes",
        ];

    [Fact]
    public void The_recording_reaches_each_method_and_each_branch()
    {
        var methods = Cases.Select(c => c.GetProperty("input").GetProperty("method").GetString()).Distinct().Order().ToList();
        Assert.Equal(
            [
                "build_indices_and_constraints",
                "clear_data",
                "delete_all_indexes",
                "determine_entity_community",
                "get_communities_by_nodes",
                "get_community_clusters",
                "get_mentioned_nodes",
            ],
            methods);

        bool Has(string method, Func<JsonElement, bool> test) =>
            Cases.Any(c => c.GetProperty("input").GetProperty("method").GetString() == method && test(c.GetProperty("input")));

        // clear_data: no group ids, an empty list, a list, and the empty text as a group id
        Assert.True(Has("clear_data", i => !i.TryGetProperty("group_ids", out _)));
        Assert.True(Has("clear_data", i => i.TryGetProperty("group_ids", out var g) && g.ValueKind == JsonValueKind.Null));
        Assert.True(Has("clear_data", i => i.TryGetProperty("group_ids", out var g) && g.ValueKind == JsonValueKind.Array && g.GetArrayLength() == 0));
        Assert.True(Has("clear_data", i => i.TryGetProperty("group_ids", out var g) && g.ValueKind == JsonValueKind.Array && g.EnumerateArray().Any(x => x.GetString() == string.Empty)));

        // build_indices_and_constraints: the default, and each value of delete_existing
        Assert.True(Has("build_indices_and_constraints", i => !i.TryGetProperty("delete_existing", out _)));
        Assert.True(Has("build_indices_and_constraints", i => i.TryGetProperty("delete_existing", out var d) && d.GetBoolean()));
        Assert.True(Has("build_indices_and_constraints", i => i.TryGetProperty("delete_existing", out var d) && !d.GetBoolean()));

        // get_community_clusters: no group ids, with and without a node, with a cluster, and with an error
        Assert.True(Has("get_community_clusters", i => i.GetProperty("group_ids").ValueKind == JsonValueKind.Null));
        Assert.True(Has("get_community_clusters", i => i.GetProperty("group_ids").ValueKind == JsonValueKind.Array && i.GetProperty("graph").GetProperty("nodes").EnumerateObject().All(n => n.Value.GetArrayLength() == 0)));
        Assert.Contains(Cases, c => c.GetProperty("input").GetProperty("method").GetString() == "get_community_clusters" && c.GetProperty("output").TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.Array && r.GetArrayLength() > 1);
        foreach (var type in new[] { "KeyError", "AttributeError", "ValidationError", "RuntimeError" })
        {
            Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("error") is { ValueKind: JsonValueKind.Object } e && e.GetProperty("type").GetString() == type);
        }

        // determine_entity_community: a community found at once, and a second query
        Assert.Contains(Cases, c => c.GetProperty("input").GetProperty("method").GetString() == "determine_entity_community" && c.GetProperty("output").GetProperty("events").GetArrayLength() == 1);
        Assert.Contains(Cases, c => c.GetProperty("input").GetProperty("method").GetString() == "determine_entity_community" && c.GetProperty("output").GetProperty("events").GetArrayLength() == 2);

        // a graph that Python does not end on is left out, and the count is in the file
        Assert.True(NonTerminatingLeftOut() > 0);
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(CaseIndexes))]
    public async Task Each_call_the_result_and_the_log_match_Python(int index)
    {
        var input = Cases[index].GetProperty("input");
        var output = Cases[index].GetProperty("output");
        var method = input.GetProperty("method").GetString();
        var expectedEvents = output.GetProperty("events").EnumerateArray().ToList();

        var events = new List<Event>();
        var capture = new CaptureProvider(events);
        using var factory = new CaptureFactory(capture);
        using var scope = PortServices.Use(loggerFactory: factory);
        var responses = expectedEvents
            .Where(e => e.GetProperty("kind").GetString() == "call")
            .Select(e => e.GetProperty("response"))
            .ToList();
        var failAt = input.TryGetProperty("fail_at", out var failAtElement) ? failAtElement.GetInt32() : -1;
        var executor = new FakeExecutor(events, responses, failAt);
        var operations = new Neo4jGraphMaintenanceOperations();
        var token = TestContext.Current.CancellationToken;

        List<string>? GroupIds() => input.GetProperty("group_ids").ValueKind == JsonValueKind.Null
            ? null
            : input.GetProperty("group_ids").EnumerateArray().Select(g => g.GetString()!).ToList();

        Exception? error = null;
        List<JsonNode>? result = null;
        try
        {
            switch (method)
            {
                case "clear_data":
                    if (input.TryGetProperty("group_ids", out _))
                    {
                        await Asyncio.Run(() => operations.ClearDataAsync(executor, GroupIds(), token));
                    }
                    else
                    {
                        await Asyncio.Run(() => operations.ClearDataAsync(executor, cancellationToken: token));
                    }

                    break;
                case "build_indices_and_constraints":
                    if (input.TryGetProperty("delete_existing", out var deleteExisting))
                    {
                        await Asyncio.Run(() => operations.BuildIndicesAndConstraintsAsync(executor, deleteExisting.GetBoolean(), token));
                    }
                    else
                    {
                        await Asyncio.Run(() => operations.BuildIndicesAndConstraintsAsync(executor, cancellationToken: token));
                    }

                    break;
                case "delete_all_indexes":
                    await Asyncio.Run(() => operations.DeleteAllIndexesAsync(executor, token));
                    break;
                case "get_community_clusters":
                    var clusters = await Asyncio.Run(() => operations.GetCommunityClustersAsync(executor, GroupIds(), token));
                    result = [.. clusters.Select(cluster => (JsonNode)new JsonArray(cluster.Select(node => (JsonNode?)Encode(node.ModelDump(true))).ToArray()))];
                    break;
                case "determine_entity_community":
                    var entity = new EntityNode { Uuid = input.GetProperty("uuid").GetString()!, Name = "n", GroupId = "g" };
                    await Asyncio.Run(() => operations.DetermineEntityCommunityAsync(executor, entity, token));
                    break;
                case "get_mentioned_nodes":
                    var episodes = input.GetProperty("episodes").EnumerateArray().Select(u => new EpisodicNode
                    {
                        Uuid = u.GetString()!,
                        Name = "e",
                        GroupId = "g",
                        Source = EpisodeType.Message,
                        SourceDescription = "d",
                        Content = "c",
                        ValidAt = PyDateTime.FromIsoFormat("2024-01-01T00:00:00+00:00"),
                    }).ToList();
                    var mentioned = await Asyncio.Run(() => operations.GetMentionedNodesAsync(executor, episodes, token));
                    result = [.. mentioned.Select(node => Encode(node.ModelDump(true)))];
                    break;
                default:
                    var nodes = input.GetProperty("nodes").EnumerateArray()
                        .Select(u => new EntityNode { Uuid = u.GetString()!, Name = "n", GroupId = "g" }).ToList();
                    var communities = await Asyncio.Run(() => operations.GetCommunitiesByNodesAsync(executor, nodes, token));
                    result = [.. communities.Select(node => Encode(node.ModelDump(true)))];
                    break;
            }
        }
        catch (Exception e) when (e is KeyNotFoundException or MissingMemberException or ValidationException or InvalidCastException or InvalidOperationException)
        {
            error = e;
        }

        var expectedError = output.GetProperty("error");
        if (expectedError.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(error);
            if (method is "get_community_clusters" or "get_mentioned_nodes" or "get_communities_by_nodes")
            {
                var expected = output.GetProperty("result").EnumerateArray().ToList();
                Assert.NotNull(result);
                Assert.Equal(expected.Count, result.Count);
                for (var i = 0; i < expected.Count; i++)
                {
                    AssertSame(expected[i], result[i]);
                }
            }
        }
        else
        {
            Assert.NotNull(error);
            var type = expectedError.GetProperty("type").GetString();
            Assert.Equal(
                type switch
                {
                    "KeyError" => typeof(KeyNotFoundException),
                    "AttributeError" => typeof(MissingMemberException),
                    "ValidationError" => typeof(ValidationException),
                    "TypeError" => typeof(InvalidCastException),
                    "RuntimeError" => typeof(InvalidOperationException),
                    _ => throw new InvalidOperationException(type),
                },
                error.GetType());

            // The text of a KeyError is the Python repr of the key, and the text of a TypeError is not a contract of the port.
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

        // The calls and the log lines are one ordered list, so that a log line that moves across a call fails.
        Assert.Equal(expectedEvents.Count, events.Count);
        for (var i = 0; i < events.Count; i++)
        {
            Assert.Equal(expectedEvents[i].GetProperty("kind").GetString(), events[i].Kind);
            if (events[i].Kind == "log")
            {
                Assert.Equal(expectedEvents[i].GetProperty("level").GetString(), events[i].Level);
                Assert.Equal(expectedEvents[i].GetProperty("message").GetString(), events[i].Message);
                continue;
            }

            Assert.Equal(expectedEvents[i].GetProperty("query").GetString(), events[i].Query);
            Assert.Equal(expectedEvents[i].GetProperty("keys").EnumerateArray().Select(k => k.GetString()), events[i].Keys);
            var expectedValues = expectedEvents[i].GetProperty("values").EnumerateArray().ToList();
            Assert.Equal(expectedValues.Count, events[i].Values!.Count);
            for (var j = 0; j < expectedValues.Count; j++)
            {
                AssertSame(expectedValues[j], events[i].Values![j]);
            }
        }
    }

    // A test with no Python original: each async method with a canceled token throws OperationCanceledException (R8).
    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Each_async_method_with_a_canceled_token_throws_OperationCanceledException(string method)
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var token = source.Token;
        var executor = new FakeExecutor([], [], -1);
        var operations = new Neo4jGraphMaintenanceOperations();
        var entity = new EntityNode { Uuid = "u", Name = "n", GroupId = "g" };
        var episode = new EpisodicNode
        {
            Uuid = "e",
            Name = "e",
            GroupId = "g",
            Source = EpisodeType.Message,
            SourceDescription = "d",
            Content = "c",
            ValidAt = PyDateTime.FromIsoFormat("2024-01-01T00:00:00+00:00"),
        };

        Func<Task> call = method switch
        {
            "clear_data" => () => Asyncio.Run(() => operations.ClearDataAsync(executor, null, token)),
            "clear_data_with_groups" => () => Asyncio.Run(() => operations.ClearDataAsync(executor, ["g"], token)),
            "build_indices_and_constraints" => () => Asyncio.Run(() => operations.BuildIndicesAndConstraintsAsync(executor, false, token)),
            "build_indices_and_constraints_delete_existing" => () => Asyncio.Run(() => operations.BuildIndicesAndConstraintsAsync(executor, true, token)),
            "delete_all_indexes" => () => Asyncio.Run(() => operations.DeleteAllIndexesAsync(executor, token)),
            "get_community_clusters" => () => Asyncio.Run(() => operations.GetCommunityClustersAsync(executor, null, token)),
            "get_community_clusters_with_groups" => () => Asyncio.Run(() => operations.GetCommunityClustersAsync(executor, ["g"], token)),
            "determine_entity_community" => () => Asyncio.Run(() => operations.DetermineEntityCommunityAsync(executor, entity, token)),
            "get_mentioned_nodes" => () => Asyncio.Run(() => operations.GetMentionedNodesAsync(executor, [episode], token)),
            _ => () => Asyncio.Run(() => operations.GetCommunitiesByNodesAsync(executor, [entity], token)),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(call);
    }

    private static int NonTerminatingLeftOut()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "neo4j_graph_ops.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("nonterminating_graphs_left_out").GetInt32();
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
        var path = Path.Combine(AppContext.BaseDirectory, "values", "neo4j_graph_ops.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToList();
    }

    private sealed record Event(string Kind, string? Query, List<string>? Keys, List<JsonNode>? Values, string? Level, string? Message);

    private sealed class FakeExecutor(List<Event> events, List<JsonElement> responses, int failAt) : QueryExecutor
    {
        private int count;

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var index = count++;
            events.Add(new Event("call", cypherQuery, [.. parameters.Keys], [.. parameters.Values.Select(Encode)], null, null));
            if (failAt == index)
            {
                throw new InvalidOperationException("boom");
            }

            // The records come from the recording, one list for each call, and each call reads them new.
            var records = index < responses.Count
                ? responses[index].EnumerateArray().Select(r => (OrderedDictionary<string, object?>)Read(r)!).ToList()
                : [];
            return (records, null, null);
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

    private sealed class CaptureProvider(List<Event> events) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(events);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<Event> events) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                events.Add(new Event("log", null, null, null, logLevel == LogLevel.Debug ? "DEBUG" : logLevel.ToString().ToUpperInvariant(), formatter(state, exception)));
        }
    }
}
