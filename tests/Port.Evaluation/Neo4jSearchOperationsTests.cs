// Evaluation test for graphiti_core/driver/neo4j/operations/search_ops.py (port/RULES.md, R23, R25, GAPS F21).
// The recording is values/neo4j_search_ops.json, which port/tools/record_search_ops.py wrote from Python.
namespace Graphiti.Port.Evaluation;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.Neo4j.Operations;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

public class Neo4jSearchOperationsTests
{
    private static readonly JsonElement Document = Load();
    private static readonly List<JsonElement> Cases = Document.GetProperty("cases").EnumerateArray().ToList();

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
    public void The_recording_reaches_each_method_and_each_branch()
    {
        var methods = Cases.Select(c => c.GetProperty("input").GetProperty("method").GetString()).Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            [
                "_build_neo4j_fulltext_query", "build_edge_search_filters", "build_fulltext_query", "build_node_search_filters",
                "community_fulltext_search", "community_similarity_search", "edge_bfs_search", "edge_fulltext_search",
                "edge_similarity_search", "episode_fulltext_search", "episode_mentions_reranker", "node_bfs_search",
                "node_distance_reranker", "node_fulltext_search", "node_similarity_search",
            ],
            methods);
        Assert.Equal(
            ["GroupIdValidationError", "KeyError", "ValueError", "ZeroDivisionError"],
            Cases.Select(c => c.GetProperty("output").GetProperty("error"))
                .Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => e.GetProperty("type").GetString())
                .Distinct()
                .Order(StringComparer.Ordinal));
        Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("events").GetArrayLength() == 0);
        Assert.Contains(Cases, c => c.GetProperty("output").GetProperty("events").GetArrayLength() == 2);
    }

    [Fact]
    public void The_module_constants_match_Python()
    {
        long actual = SearchOps.MaxQueryLength;
        Assert.Equal(Document.GetProperty("constants").GetProperty("MAX_QUERY_LENGTH").GetInt64(), actual);
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public async Task Each_call_the_result_and_the_log_match_Python(int index)
    {
        var input = Cases[index].GetProperty("input");
        var output = Cases[index].GetProperty("output");
        var method = input.GetProperty("method").GetString();
        var args = input.GetProperty("args");
        var responses = input.TryGetProperty("responses", out var responsesElement)
            ? responsesElement.EnumerateArray().Select(r => r.EnumerateArray().Select(x => (OrderedDictionary<string, object?>)Read(x)!).ToList()).ToList()
            : [];

        var events = new List<JsonNode>();
        var executor = new FakeExecutor(responses, events);
        using var factory = new CaptureFactory(events);
        using var scope = PortServices.Use(loggerFactory: factory);
        var operations = new Neo4jSearchOperations();
        var token = TestContext.Current.CancellationToken;

        bool Has(string name) => args.TryGetProperty(name, out _);
        string Str(string name) => args.GetProperty(name).GetString()!;
        string? OptStr(string name) => args.GetProperty(name).ValueKind == JsonValueKind.Null ? null : Str(name);
        long Long(string name) => args.GetProperty(name).GetInt64();
        double Double(string name) => args.GetProperty(name).GetDouble();
        List<string> Strs(string name) => args.GetProperty(name).EnumerateArray().Select(x => x.GetString()!).ToList();
        List<double> Vector() => args.GetProperty("search_vector").EnumerateArray().Select(x => x.GetDouble()).ToList();
        List<string>? Groups() => Has("group_ids") && args.GetProperty("group_ids").ValueKind != JsonValueKind.Null ? Strs("group_ids") : null;
        SearchFilters Filter() => Has("filter")
            ? SearchFilters.ModelValidate((OrderedDictionary<string, object?>)Read(Document.GetProperty("filters").GetProperty(Str("filter")))!)
            : SearchFilters.ModelValidate(new());

        // A case with only the required arguments calls the method with its defaults, so that a wrong C# default fails.
        var required = method switch
        {
            "node_fulltext_search" or "edge_fulltext_search" or "episode_fulltext_search" or "community_fulltext_search" => new[] { "query" },
            "node_similarity_search" or "community_similarity_search" => ["search_vector"],
            "edge_similarity_search" => ["search_vector", "source_node_uuid", "target_node_uuid"],
            "node_bfs_search" or "edge_bfs_search" => ["origin_uuids", "max_depth"],
            "node_distance_reranker" => ["node_uuids", "center_node_uuid"],
            "episode_mentions_reranker" => ["node_uuids"],
            _ => [],
        };
        var defaults = args.EnumerateObject().All(p => required.Contains(p.Name));

        Exception? error = null;
        JsonNode? result = null;
        try
        {
            switch (method)
            {
                case "node_fulltext_search":
                    result = Items(defaults
                        ? await operations.NodeFulltextSearchAsync(executor, Str("query"), Filter(), cancellationToken: token)
                        : await operations.NodeFulltextSearchAsync(executor, Str("query"), Filter(), Groups(), Has("limit") ? Long("limit") : 10, token));
                    break;
                case "node_similarity_search":
                    result = Items(defaults
                        ? await operations.NodeSimilaritySearchAsync(executor, Vector(), Filter(), cancellationToken: token)
                        : await operations.NodeSimilaritySearchAsync(executor, Vector(), Filter(), Groups(), Has("limit") ? Long("limit") : 10, Has("min_score") ? Double("min_score") : 0.6, token));
                    break;
                case "node_bfs_search":
                    result = Items(defaults
                        ? await operations.NodeBfsSearchAsync(executor, Strs("origin_uuids"), Filter(), Long("max_depth"), cancellationToken: token)
                        : await operations.NodeBfsSearchAsync(executor, Strs("origin_uuids"), Filter(), Long("max_depth"), Groups(), Has("limit") ? Long("limit") : 10, token));
                    break;
                case "edge_fulltext_search":
                    result = Items(defaults
                        ? await operations.EdgeFulltextSearchAsync(executor, Str("query"), Filter(), cancellationToken: token)
                        : await operations.EdgeFulltextSearchAsync(executor, Str("query"), Filter(), Groups(), Has("limit") ? Long("limit") : 10, token));
                    break;
                case "edge_similarity_search":
                    result = Items(defaults
                        ? await operations.EdgeSimilaritySearchAsync(executor, Vector(), OptStr("source_node_uuid"), OptStr("target_node_uuid"), Filter(), cancellationToken: token)
                        : await operations.EdgeSimilaritySearchAsync(executor, Vector(), OptStr("source_node_uuid"), OptStr("target_node_uuid"), Filter(), Groups(), Has("limit") ? Long("limit") : 10, Has("min_score") ? Double("min_score") : 0.6, token));
                    break;
                case "edge_bfs_search":
                    result = Items(defaults
                        ? await operations.EdgeBfsSearchAsync(executor, Strs("origin_uuids"), Long("max_depth"), Filter(), cancellationToken: token)
                        : await operations.EdgeBfsSearchAsync(executor, Strs("origin_uuids"), Long("max_depth"), Filter(), Groups(), Has("limit") ? Long("limit") : 10, token));
                    break;
                case "episode_fulltext_search":
                    result = Items(defaults
                        ? await operations.EpisodeFulltextSearchAsync(executor, Str("query"), Filter(), cancellationToken: token)
                        : await operations.EpisodeFulltextSearchAsync(executor, Str("query"), Filter(), Groups(), Has("limit") ? Long("limit") : 10, token));
                    break;
                case "community_fulltext_search":
                    result = Items(defaults
                        ? await operations.CommunityFulltextSearchAsync(executor, Str("query"), cancellationToken: token)
                        : await operations.CommunityFulltextSearchAsync(executor, Str("query"), Groups(), Has("limit") ? Long("limit") : 10, token));
                    break;
                case "community_similarity_search":
                    result = Items(defaults
                        ? await operations.CommunitySimilaritySearchAsync(executor, Vector(), cancellationToken: token)
                        : await operations.CommunitySimilaritySearchAsync(executor, Vector(), Groups(), Has("limit") ? Long("limit") : 10, Has("min_score") ? Double("min_score") : 0.6, token));
                    break;
                case "node_distance_reranker":
                    result = Items(defaults
                        ? await operations.NodeDistanceRerankerAsync(executor, Strs("node_uuids"), Str("center_node_uuid"), cancellationToken: token)
                        : await operations.NodeDistanceRerankerAsync(executor, Strs("node_uuids"), Str("center_node_uuid"), Double("min_score"), token));
                    break;
                case "episode_mentions_reranker":
                    result = Items(defaults
                        ? await operations.EpisodeMentionsRerankerAsync(executor, Strs("node_uuids"), cancellationToken: token)
                        : await operations.EpisodeMentionsRerankerAsync(executor, Strs("node_uuids"), Double("min_score"), token));
                    break;
                case "build_node_search_filters":
                    result = Encode(operations.BuildNodeSearchFilters(Filter()));
                    break;
                case "build_edge_search_filters":
                    result = Encode(operations.BuildEdgeSearchFilters(Filter()));
                    break;
                case "build_fulltext_query":
                    result = Encode(Has("max_query_length")
                        ? operations.BuildFulltextQuery(Str("query"), Groups(), Long("max_query_length"))
                        : operations.BuildFulltextQuery(Str("query"), Groups()));
                    break;
                default:
                    result = Encode(Has("max_query_length")
                        ? SearchOps.BuildNeo4jFulltextQuery(Str("query"), Groups(), Long("max_query_length"))
                        : Has("group_ids") ? SearchOps.BuildNeo4jFulltextQuery(Str("query"), Groups()) : SearchOps.BuildNeo4jFulltextQuery(Str("query")));
                    break;
            }
        }
        catch (Exception e) when (e is GroupIdValidationException or KeyNotFoundException or ArgumentException or DivideByZeroException)
        {
            error = e;
        }

        var expectedError = output.GetProperty("error");
        if (expectedError.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(error);
            AssertSame(output.GetProperty("result"), result);
        }
        else
        {
            Assert.NotNull(error);
            var type = expectedError.GetProperty("type").GetString();
            Assert.Equal(
                type switch
                {
                    "GroupIdValidationError" => typeof(GroupIdValidationException),
                    "KeyError" => typeof(KeyNotFoundException),
                    "ValueError" => typeof(ArgumentException),
                    "ZeroDivisionError" => typeof(DivideByZeroException),
                    _ => throw new InvalidOperationException(type),
                },
                error.GetType());

            // The text of a KeyError is the Python repr of the key, and a .NET message has other words.
            // A ZeroDivisionError of an int score says "division by zero", and the port reads each score as a float
            // by the annotation dict[str, float], so its text is "float division by zero".
            if (type is not "KeyError" and not "ZeroDivisionError")
            {
                Assert.Equal(expectedError.GetProperty("text").GetString(), error.Message);
            }
        }

        var expectedEvents = output.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(expectedEvents.Count, events.Count);
        for (var i = 0; i < events.Count; i++)
        {
            AssertSame(expectedEvents[i], events[i]);
        }
    }

    [Theory]
    [InlineData("node_fulltext_search")]
    [InlineData("node_similarity_search")]
    [InlineData("node_bfs_search")]
    [InlineData("edge_fulltext_search")]
    [InlineData("edge_similarity_search")]
    [InlineData("edge_bfs_search")]
    [InlineData("episode_fulltext_search")]
    [InlineData("community_fulltext_search")]
    [InlineData("community_similarity_search")]
    [InlineData("node_distance_reranker")]
    [InlineData("episode_mentions_reranker")]
    public async Task Each_async_method_throws_for_a_canceled_token(string method)
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        var token = source.Token;
        var executor = new FakeExecutor([], []);
        var operations = new Neo4jSearchOperations();
        var filters = SearchFilters.ModelValidate(new());
        Func<Task> call = method switch
        {
            "node_fulltext_search" => () => operations.NodeFulltextSearchAsync(executor, "q", filters, cancellationToken: token),
            "node_similarity_search" => () => operations.NodeSimilaritySearchAsync(executor, [0.5], filters, cancellationToken: token),
            "node_bfs_search" => () => operations.NodeBfsSearchAsync(executor, ["n"], filters, 1, cancellationToken: token),
            "edge_fulltext_search" => () => operations.EdgeFulltextSearchAsync(executor, "q", filters, cancellationToken: token),
            "edge_similarity_search" => () => operations.EdgeSimilaritySearchAsync(executor, [0.5], null, null, filters, cancellationToken: token),
            "edge_bfs_search" => () => operations.EdgeBfsSearchAsync(executor, ["n"], 1, filters, cancellationToken: token),
            "episode_fulltext_search" => () => operations.EpisodeFulltextSearchAsync(executor, "q", filters, cancellationToken: token),
            "community_fulltext_search" => () => operations.CommunityFulltextSearchAsync(executor, "q", cancellationToken: token),
            "community_similarity_search" => () => operations.CommunitySimilaritySearchAsync(executor, [0.5], cancellationToken: token),
            "node_distance_reranker" => () => operations.NodeDistanceRerankerAsync(executor, ["a"], "c", cancellationToken: token),
            _ => () => operations.EpisodeMentionsRerankerAsync(executor, ["a"], cancellationToken: token),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(call);
    }

    private static JsonArray Items<T>(List<T> items)
        where T : Node =>
        new JsonArray(items.Select(item =>
        {
            var node = new JsonObject
            {
                ["class"] = item.GetType().Name,
                ["uuid"] = item.Uuid,
                ["name"] = item.Name,
                ["group_id"] = item.GroupId,
            };
            if (item is EntityNode entity)
            {
                node["labels"] = new JsonArray(entity.Labels.Select(l => (JsonNode?)l).ToArray());
            }

            return (JsonNode?)node;
        }).ToArray());

    private static JsonArray Items(List<EntityEdge> items) =>
        new JsonArray(items.Select(item => (JsonNode?)new JsonObject
        {
            ["class"] = "EntityEdge",
            ["uuid"] = item.Uuid,
            ["name"] = item.Name,
            ["group_id"] = item.GroupId,
        }).ToArray());

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

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "neo4j_search_ops.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private sealed class FakeExecutor(List<List<OrderedDictionary<string, object?>>> responses, List<JsonNode> events) : QueryExecutor
    {
        private int calls;

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            events.Add(new JsonObject
            {
                ["kind"] = "call",
                ["query"] = cypherQuery,
                ["keys"] = new JsonArray(parameters.Keys.Select(k => (JsonNode?)k).ToArray()),
                ["values"] = new JsonArray(parameters.Values.Select(Encode).ToArray()),
            });
            var records = calls < responses.Count ? responses[calls] : [];
            calls++;
            return (records, null, null);
        }
    }

    private sealed class CaptureFactory(List<JsonNode> events) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

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
                    ["level"] = logLevel switch
                    {
                        LogLevel.Debug => "DEBUG",
                        LogLevel.Information => "INFO",
                        LogLevel.Warning => "WARNING",
                        LogLevel.Error => "ERROR",
                        _ => logLevel.ToString(),
                    },
                    ["message"] = formatter(state, exception),
                });
        }
    }
}
