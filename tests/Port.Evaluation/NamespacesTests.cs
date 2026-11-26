// Evaluation test for graphiti_core/namespaces/nodes.py and edges.py (port/RULES.md, R23, R25).
// The recording is values/namespaces.json, which port/tools/record_namespaces.py wrote from Python.
// The fake driver is the network boundary: it records each query, and its operations are the real Neo4j operations.
// save and the calls that need an operations class or a function that is not ported yet are not recorded.
namespace Graphiti.Port.Evaluation;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Operations;
using Graphiti.Core.Embedder;
using Graphiti.Core.Namespaces;
using Graphiti.Neo4j.Operations;
using Graphiti.PyCompat;

public class NamespacesTests
{
    private static readonly List<JsonElement> Cases = Load();

    public static TheoryData<int> NodeNamespaceCases() => Indexes("node_namespace");

    public static TheoryData<int> EdgeNamespaceCases() => Indexes("edge_namespace");

    public static TheoryData<int> CallCases() => Indexes(null);

    private static TheoryData<int> Indexes(string? what)
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Count; i++)
        {
            if (What(Cases[i]) == what)
            {
                data.Add(i);
            }
        }

        return data;
    }

    private static string? What(JsonElement c) =>
        c.GetProperty("input").TryGetProperty("what", out var w) ? w.GetString() : null;

    [Fact]
    public void The_recording_holds_each_kind_of_driver_and_each_recorded_call()
    {
        Assert.Equal(8, Cases.Count(c => What(c) == "node_namespace"));
        Assert.Equal(8, Cases.Count(c => What(c) == "edge_namespace"));
        var methods = Cases.Where(c => What(c) is null).Select(c => c.GetProperty("input").GetProperty("method").GetString()).Distinct().Order().ToList();
        Assert.Equal(["delete", "delete_by_group_id", "delete_by_uuids", "load_embeddings", "load_embeddings_bulk"], methods);
    }

    [Theory]
    [MemberData(nameof(NodeNamespaceCases))]
    public void NodeNamespace_sets_a_sub_namespace_for_each_operations_class_of_the_driver(int index)
    {
        var input = Cases[index].GetProperty("input");
        var present = input.GetProperty("kind").GetString() switch
        {
            "none" => Array.Empty<string>(),
            "entity_only" => ["entity"],
            "episode_saga" => ["episode", "saga"],
            _ => ["entity", "episode", "community", "saga"],
        };
        var driver = new FakeDriver(input.GetProperty("driver").GetString()!, []) { Nodes = present };
        var namespaces = new NodeNamespace(driver, new FakeEmbedder());
        var output = Cases[index].GetProperty("output");
        Check(output, "entity", () => namespaces.Entity);
        Check(output, "episode", () => namespaces.Episode);
        Check(output, "community", () => namespaces.Community);
        Check(output, "saga", () => namespaces.Saga);
    }

    [Theory]
    [MemberData(nameof(EdgeNamespaceCases))]
    public void EdgeNamespace_sets_a_sub_namespace_for_each_operations_class_of_the_driver(int index)
    {
        var input = Cases[index].GetProperty("input");
        var present = input.GetProperty("kind").GetString() switch
        {
            "none" => Array.Empty<string>(),
            "episodic_only" => ["episodic"],
            "entity_next" => ["entity", "next_episode"],
            _ => ["entity", "episodic", "community", "has_episode", "next_episode"],
        };
        var driver = new FakeDriver(input.GetProperty("driver").GetString()!, []) { Edges = present };
        var namespaces = new EdgeNamespace(driver, new FakeEmbedder());
        var output = Cases[index].GetProperty("output");
        Check(output, "entity", () => namespaces.Entity);
        Check(output, "episodic", () => namespaces.Episodic);
        Check(output, "community", () => namespaces.Community);
        Check(output, "has_episode", () => namespaces.HasEpisode);
        Check(output, "next_episode", () => namespaces.NextEpisode);
    }

    [Theory]
    [MemberData(nameof(CallCases))]
    public async Task EntityNodeNamespace_each_call_reaches_the_executor_as_in_Python(int index)
    {
        var input = Cases[index].GetProperty("input");
        var output = Cases[index].GetProperty("output");
        var method = input.GetProperty("method").GetString();
        var records = input.TryGetProperty("records", out var recordsElement)
            ? recordsElement.EnumerateArray().Select(r => (OrderedDictionary<string, object?>)Read(r)!).ToList()
            : [];
        var driver = new FakeDriver("FakeDriver", records) { Nodes = ["entity"] };
        var entity = new NodeNamespace(driver, new FakeEmbedder()).Entity;
        var transaction = input.TryGetProperty("use_tx", out var useTx) && useTx.GetBoolean() ? new FakeTransaction() : null;
        var uuids = input.TryGetProperty("uuids", out var uuidsElement) ? uuidsElement.EnumerateArray().Select(u => u.GetString()!).ToList() : [];
        var nodes = uuids.Select(u => new EntityNode { Uuid = u, Name = "n", GroupId = "g" }).ToList();
        var node = method is not "delete_by_uuids" && input.TryGetProperty("uuid", out var uuid)
            ? new EntityNode { Uuid = uuid.GetString()!, Name = "n", GroupId = "g" }
            : null;
        var token = TestContext.Current.CancellationToken;

        Exception? error = null;
        try
        {
            switch (method)
            {
                case "delete":
                    await entity.DeleteAsync(node!, transaction, token);
                    break;
                case "delete_by_group_id":
                    await entity.DeleteByGroupIdAsync(input.GetProperty("group_id").GetString()!, transaction, input.GetProperty("batch_size").GetInt64(), token);
                    break;
                case "delete_by_uuids":
                    await entity.DeleteByUuidsAsync(uuids, transaction, input.GetProperty("batch_size").GetInt64(), token);
                    break;
                case "load_embeddings":
                    await entity.LoadEmbeddingsAsync(node!, token);
                    break;
                default:
                    await entity.LoadEmbeddingsBulkAsync(nodes, cancellationToken: token);
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
            Assert.Equal(
                expectedError.GetProperty("type").GetString() switch
                {
                    "NodeNotFoundError" => typeof(NodeNotFoundException),
                    "KeyError" => typeof(KeyNotFoundException),
                    "ValidationError" => typeof(ValidationException),
                    var other => throw new InvalidOperationException(other),
                },
                error.GetType());
        }

        var calls = driver.Calls.Concat(transaction?.Calls ?? []).ToList();
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

    private static void Check(JsonElement output, string name, Func<object> read)
    {
        var expected = output.GetProperty(name);
        if (expected.GetProperty("present").GetBoolean())
        {
            Assert.Equal(expected.GetProperty("type").GetString(), read().GetType().Name);
        }
        else
        {
            Assert.Equal("NotImplementedError", expected.GetProperty("error").GetString());
            var thrown = Assert.Throws<NotImplementedException>(() => read());
            Assert.Equal(expected.GetProperty("text").GetString(), thrown.Message);
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
        JsonValueKind.Number => e.GetRawText().Any(c => c is '.' or 'e' or 'E') ? e.GetDouble() : e.GetInt64(),
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
        var path = Path.Combine(AppContext.BaseDirectory, "values", "namespaces.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToList();
    }

    private sealed record Call(string Via, string Query, List<string> Keys, List<JsonNode> Values);

    private static Call Note(string via, string query, OrderedDictionary<string, object?> parameters) =>
        new(via, query, [.. parameters.Keys], [.. parameters.Values.Select(Encode)]);

    private sealed class FakeEmbedder : EmbedderClient
    {
        internal override Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default) =>
            Task.FromResult<List<double>>([0.0]);
    }

    private sealed class FakeDriver(string name, List<OrderedDictionary<string, object?>> records) : GraphDriver
    {
        internal List<Call> Calls { get; } = [];

        internal string[] Nodes { get; set; } = [];

        internal string[] Edges { get; set; } = [];

        internal override string PythonName => name;

        internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(Note("executor", cypherQuery, parameters));
            return Task.FromResult<(List<OrderedDictionary<string, object?>>, object?, object?)>((records, null, null));
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException();

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        internal override EntityNodeOperations? EntityNodeOps => Nodes.Contains("entity") ? new Neo4jEntityNodeOperations() : null;

        internal override EpisodeNodeOperations? EpisodeNodeOps => Nodes.Contains("episode") ? new Neo4jEpisodeNodeOperations() : null;

        internal override CommunityNodeOperations? CommunityNodeOps => Nodes.Contains("community") ? new Neo4jCommunityNodeOperations() : null;

        internal override SagaNodeOperations? SagaNodeOps => Nodes.Contains("saga") ? new Neo4jSagaNodeOperations() : null;

        internal override EntityEdgeOperations? EntityEdgeOps => Edges.Contains("entity") ? new Neo4jEntityEdgeOperations() : null;

        internal override EpisodicEdgeOperations? EpisodicEdgeOps => Edges.Contains("episodic") ? new Neo4jEpisodicEdgeOperations() : null;

        internal override CommunityEdgeOperations? CommunityEdgeOps => Edges.Contains("community") ? new Neo4jCommunityEdgeOperations() : null;

        internal override HasEpisodeEdgeOperations? HasEpisodeEdgeOps => Edges.Contains("has_episode") ? new Neo4jHasEpisodeEdgeOperations() : null;

        internal override NextEpisodeEdgeOperations? NextEpisodeEdgeOps => Edges.Contains("next_episode") ? new Neo4jNextEpisodeEdgeOperations() : null;
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
}
