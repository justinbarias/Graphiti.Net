// Evaluation test for graphiti_core/edges.py (port/RULES.md, R23, R25).
// The recording is values/edges.json, which port/tools/record_edges.py wrote from Python. A fake driver and a fake
// embedder stand at the network boundary. The graph operations interface is null, the base class (each operation
// throws NotImplementedException), or a subclass that records each call and returns fixed edges.
namespace Graphiti.Port.Evaluation;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

public class EdgesRecordedTests
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
    public void The_recording_holds_each_method_each_mode_and_each_error()
    {
        var methods = Cases.Select(c => c.GetProperty("input").GetProperty("cls").GetString() + "." + c.GetProperty("input").GetProperty("method").GetString()).ToHashSet();
        foreach (var cls in new[] { "EpisodicEdge", "CommunityEdge", "HasEpisodeEdge", "NextEpisodeEdge", "EntityEdge" })
        {
            foreach (var method in new[] { "save", "delete", "get_by_uuid", "get_by_uuids", "get_by_group_ids" })
            {
                Assert.Contains(cls + "." + method, methods);
            }
        }

        Assert.Contains("EntityEdge.get_between_nodes", methods);
        Assert.Contains("EntityEdge.get_by_node_uuid", methods);
        Assert.Contains("EntityEdge.load_fact_embedding", methods);
        Assert.Contains("EntityEdge.generate_embedding", methods);
        Assert.Contains("Edge.delete_by_uuids", methods);
        Assert.Contains("Edge.create_entity_edge_embeddings", methods);
        Assert.Equal(
            ["EdgeNotFoundError", "GroupsEdgesNotFoundError", "IndexError", "KeyError", "ValueError"],
            Cases.Select(c => c.GetProperty("output").GetProperty("error")).Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => e.GetProperty("type").GetString()).Distinct().Order(StringComparer.Ordinal).ToList());
        Assert.Equal(["none", "raise", "return"], Cases.Select(c => c.GetProperty("input").GetProperty("mode").GetString()).Distinct().Order(StringComparer.Ordinal).ToList());
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public async Task Each_query_operation_result_and_log_match_Python(int index)
    {
        var token = TestContext.Current.CancellationToken;
        var input = Cases[index].GetProperty("input");
        var output = Cases[index].GetProperty("output");
        var clsName = input.GetProperty("cls").GetString()!;
        var method = input.GetProperty("method").GetString()!;
        var mode = input.GetProperty("mode").GetString()!;
        var operationCalls = new List<JsonNode>();
        GraphOperationsInterface? operations = mode switch
        {
            "raise" => new RaisingOperations(),
            "return" => new ReturningOperations(operationCalls, clsName),
            _ => null,
        };
        var records = input.TryGetProperty("load", out var load)
            ? load.EnumerateArray().Select(r => Py.AsDict(Read(r))).ToList()
            : RecordsFor(clsName, input.TryGetProperty("records", out var count) ? count.GetInt32() : 0);
        var driver = new FakeDriver(records)
        {
            Provider = PyEnum.FromValue<GraphProvider>(input.GetProperty("provider").GetString()),
            GraphOperationsInterface = operations,
        };
        var embedder = new FakeEmbedder(input.TryGetProperty("delta", out var delta) ? delta.GetInt32() : 0);
        var edge = input.TryGetProperty("edge", out var edgeData) ? Construct(clsName, Py.AsDict(Read(edgeData))) : null;
        var entityEdges = input.TryGetProperty("facts", out var facts)
            ? facts.EnumerateArray().Select((f, i) => EntityEdge.ModelValidate(With(EntityData(), ("uuid", $"f-{i}"), ("fact", f.GetString())))).ToList()
            : [];
        var record = input.TryGetProperty("record", out var recordData) ? Py.AsDict(Read(recordData)) : null;

        var capture = new CaptureProvider();
        using var factory = new CaptureFactory(capture);
        using var scope = PortServices.Use(loggerFactory: factory);

        object? value = null;
        Exception? error = null;
        try
        {
            value = await RunAsync(clsName, method, input, driver, edge, embedder, entityEdges, record, token);
        }
        catch (Exception e) when (e is GraphitiException or ArgumentException or KeyNotFoundException)
        {
            error = e;
        }

        var expectedError = output.GetProperty("error");
        if (expectedError.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(error);

            // PORT: save returns the result of execute_query in Python and a Task in C# (the skeleton review keeps it).
            if (method != "save")
            {
                AssertSame(output.GetProperty("result"), Encode(value));
            }
        }
        else
        {
            Assert.NotNull(error);
            var type = expectedError.GetProperty("type").GetString();
            Assert.Equal(
                type switch
                {
                    "EdgeNotFoundError" => typeof(EdgeNotFoundException),
                    "GroupsEdgesNotFoundError" => typeof(GroupsEdgesNotFoundException),
                    "IndexError" => typeof(ArgumentOutOfRangeException),
                    "KeyError" => typeof(KeyNotFoundException),
                    "ValueError" => typeof(ArgumentException),
                    _ => throw new InvalidOperationException(type),
                },
                error.GetType());

            // The text of a KeyError and of an IndexError is Python's own, and a .NET message has other words.
            if (type is not "KeyError" and not "IndexError")
            {
                Assert.Equal(expectedError.GetProperty("text").GetString(), error.Message);
            }
        }

        var calls = driver.Calls;
        var expectedCalls = output.GetProperty("calls").EnumerateArray().ToList();
        Assert.Equal(expectedCalls.Count, calls.Count);
        for (var i = 0; i < calls.Count; i++)
        {
            Assert.Equal(expectedCalls[i].GetProperty("query").GetString(), calls[i].Query);
            Assert.Equal(expectedCalls[i].GetProperty("keys").EnumerateArray().Select(k => k.GetString()), calls[i].Keys);
            var expectedValues = expectedCalls[i].GetProperty("values").EnumerateArray().ToList();
            Assert.Equal(expectedValues.Count, calls[i].Values.Count);
            for (var j = 0; j < expectedValues.Count; j++)
            {
                AssertSame(expectedValues[j], calls[i].Values[j]);
            }
        }

        AssertSame(output.GetProperty("operations"), new JsonArray([.. operationCalls]));
        AssertSame(output.GetProperty("embedder"), new JsonArray([.. embedder.Calls]));

        var expectedLogs = output.GetProperty("logs").EnumerateArray()
            .Select(l => (l.GetProperty("level").GetString(), Untimed(l.GetProperty("message").GetString()!))).ToList();
        Assert.Equal(expectedLogs, capture.Lines.Select(l => (l.Level, Untimed(l.Message))).ToList());

        if (output.TryGetProperty("edge", out var edgeAfter))
        {
            AssertSame(edgeAfter, Encode(Dump(edge!)));
        }

        // PORT: zip(strict=True) in Python assigns the pairs before the short side ends, and then raises. Part 3 of the
        // rulebook compares the counts first, so the port assigns none. The embeddings are compared only with no error.
        if (output.TryGetProperty("edges", out var edgesAfter) && expectedError.ValueKind == JsonValueKind.Null)
        {
            AssertSame(edgesAfter, new JsonArray([.. entityEdges.Select(e => Encode(e.FactEmbedding))]));
        }

        if (output.TryGetProperty("record_attributes", out var attributesAfter))
        {
            AssertSame(attributesAfter, Encode(record!["attributes"]));
        }
    }

    private static async Task<object?> RunAsync(
        string cls,
        string method,
        JsonElement input,
        FakeDriver driver,
        Edge? edge,
        FakeEmbedder embedder,
        List<EntityEdge> entityEdges,
        OrderedDictionary<string, object?>? record,
        CancellationToken token)
    {
        string S(string name) => input.GetProperty(name).GetString()!;
        string? NS(string name) => input.GetProperty(name).ValueKind == JsonValueKind.Null ? null : input.GetProperty(name).GetString();
        List<string> L(string name) => input.GetProperty(name).EnumerateArray().Select(u => u.GetString()!).ToList();
        long? Limit() => input.GetProperty("limit").ValueKind == JsonValueKind.Null ? null : input.GetProperty("limit").GetInt64();

        switch (method)
        {
            case "save":
                await edge!.SaveAsync(driver, token);
                return null;
            case "delete":
                await edge!.DeleteAsync(driver, token);
                return null;
            case "delete_by_uuids":
                await Edge.DeleteByUuidsAsync(driver, L("uuids"), token);
                return null;
            case "load_fact_embedding":
                await ((EntityEdge)edge!).LoadFactEmbeddingAsync(driver, token);
                return null;
            case "generate_embedding":
                return await ((EntityEdge)edge!).GenerateEmbeddingAsync(embedder, token);
            case "create_entity_edge_embeddings":
                await Edges.CreateEntityEdgeEmbeddingsAsync(embedder, entityEdges, token);
                return null;
            case "get_entity_edge_from_record":
                return Edges.GetEntityEdgeFromRecord(record!, driver.Provider);
            case "get_between_nodes":
                return await EntityEdge.GetBetweenNodesAsync(driver, S("source"), S("target"), token);
            case "get_by_node_uuid":
                return await EntityEdge.GetByNodeUuidAsync(driver, S("node_uuid"), token);
        }

        return (cls, method) switch
        {
            ("EpisodicEdge", "get_by_uuid") => await EpisodicEdge.GetByUuidAsync(driver, S("uuid"), token),
            ("EpisodicEdge", "get_by_uuids") => await EpisodicEdge.GetByUuidsAsync(driver, L("uuids"), token),
            ("EpisodicEdge", "get_by_group_ids") => await EpisodicEdge.GetByGroupIdsAsync(driver, L("group_ids"), Limit(), NS("uuid_cursor"), token),
            ("EntityEdge", "get_by_uuid") => await EntityEdge.GetByUuidAsync(driver, S("uuid"), NS("group_id"), token),
            ("EntityEdge", "get_by_uuids") => await EntityEdge.GetByUuidsAsync(driver, L("uuids"), NS("group_id"), token),
            ("EntityEdge", "get_by_group_ids") => await EntityEdge.GetByGroupIdsAsync(
                driver,
                L("group_ids"),
                Limit(),
                NS("uuid_cursor"),
                input.GetProperty("with_embeddings").GetBoolean(),
                token),
            ("CommunityEdge", "get_by_uuid") => await CommunityEdge.GetByUuidAsync(driver, S("uuid"), token),
            ("CommunityEdge", "get_by_uuids") => await CommunityEdge.GetByUuidsAsync(driver, L("uuids"), token),
            ("CommunityEdge", "get_by_group_ids") => await CommunityEdge.GetByGroupIdsAsync(driver, L("group_ids"), Limit(), NS("uuid_cursor"), token),
            ("HasEpisodeEdge", "get_by_uuid") => await HasEpisodeEdge.GetByUuidAsync(driver, S("uuid"), token),
            ("HasEpisodeEdge", "get_by_uuids") => await HasEpisodeEdge.GetByUuidsAsync(driver, L("uuids"), token),
            ("HasEpisodeEdge", "get_by_group_ids") => await HasEpisodeEdge.GetByGroupIdsAsync(driver, L("group_ids"), Limit(), NS("uuid_cursor"), token),
            ("NextEpisodeEdge", "get_by_uuid") => await NextEpisodeEdge.GetByUuidAsync(driver, S("uuid"), token),
            ("NextEpisodeEdge", "get_by_uuids") => await NextEpisodeEdge.GetByUuidsAsync(driver, L("uuids"), token),
            ("NextEpisodeEdge", "get_by_group_ids") => await NextEpisodeEdge.GetByGroupIdsAsync(driver, L("group_ids"), Limit(), NS("uuid_cursor"), token),
            _ => throw new InvalidOperationException(cls + "." + method),
        };
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

    private static OrderedDictionary<string, object?> EntityData() => Py.AsDict(Read(Document.GetProperty("entity_edge")));

    private static OrderedDictionary<string, object?> BaseData() => Py.AsDict(Read(Document.GetProperty("base_edge")));

    private static OrderedDictionary<string, object?> With(OrderedDictionary<string, object?> data, params (string Key, object? Value)[] changes)
    {
        foreach (var (key, value) in changes)
        {
            data[key] = value;
        }

        return data;
    }

    private static List<OrderedDictionary<string, object?>> RecordsFor(string cls, int count)
    {
        OrderedDictionary<string, object?> Base(string uuid, string groupId) => new()
        {
            ["uuid"] = uuid,
            ["group_id"] = groupId,
            ["source_node_uuid"] = "src",
            ["target_node_uuid"] = "tgt",
            ["created_at"] = "2026-01-01T00:00:00+00:00",
        };

        OrderedDictionary<string, object?> Make(string uuid, string groupId) => cls != "EntityEdge"
            ? Base(uuid, groupId)
            : With(
                Base(uuid, groupId),
                ("name", "R"),
                ("fact", "f"),
                ("fact_embedding", new List<object?> { 0.5 }),
                ("episodes", new List<object?> { "ep-1" }),
                ("expired_at", null),
                ("valid_at", "2026-01-01T00:00:00+00:00"),
                ("invalid_at", null),
                ("reference_time", null),
                ("attributes", new OrderedDictionary<string, object?> { ["uuid"] = "x", ["fact"] = "x", ["created_at"] = "x", ["custom"] = 1L }));

        return new List<OrderedDictionary<string, object?>> { Make("r-1", "g"), Make("r-2", "other") }.Take(count).ToList();
    }

    // The clock of edges.py is a constant in the recording and real here, so both texts lose the time.
    private static string Untimed(string message) =>
        Regex.Replace(message, @" in \S+ ms$", " in <t> ms", RegexOptions.CultureInvariant);

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
        Type t => new JsonObject { ["type"] = "class", ["value"] = t.Name },
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

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "edges.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private sealed record Call(string Query, List<string> Keys, List<JsonNode> Values);

    private sealed class FakeDriver(List<OrderedDictionary<string, object?>> records) : GraphDriver
    {
        internal List<Call> Calls { get; } = [];

        internal override string PythonName => "FakeDriver";

        internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            Calls.Add(new(cypherQuery, [.. parameters.Keys], [.. parameters.Values.Select(Encode)]));
            return Task.FromResult<(List<OrderedDictionary<string, object?>>, object?, object?)>((records, null, null));
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // The base class: each operation throws NotImplementedException, as each Python method raises NotImplementedError.
    private sealed record RaisingOperations : GraphOperationsInterface;

    // Records each call with the arguments after the driver, as the Python recorder does, and returns fixed edges.
    private sealed record ReturningOperations : GraphOperationsInterface
    {
        private readonly List<JsonNode> calls;
        private readonly Edge one;
        private readonly Edge two;

        internal ReturningOperations(List<JsonNode> calls, string cls)
        {
            this.calls = calls;
            var data = cls is "EpisodicEdge" or "CommunityEdge" or "HasEpisodeEdge" or "NextEpisodeEdge" ? BaseData() : EntityData();
            var name = cls == "Edge" ? "EntityEdge" : cls;
            one = Construct(name, With(new(data), ("uuid", "ops-1")));
            two = Construct(name, With(new(data), ("uuid", "ops-2"), ("group_id", "other")));
        }

        internal override Task EdgeSaveAsync(EntityEdge edge, GraphDriver driver, CancellationToken cancellationToken = default) => None("edge_save", edge);

        internal override Task EdgeDeleteAsync(Edge edge, GraphDriver driver, CancellationToken cancellationToken = default) => None("edge_delete", edge);

        internal override Task EdgeDeleteByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, string? groupId = null, CancellationToken cancellationToken = default) =>
            groupId is null ? None("edge_delete_by_uuids", cls, uuids) : None("edge_delete_by_uuids", cls, uuids, groupId);

        internal override Task<EntityEdge> EdgeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
            One<EntityEdge>("edge_get_by_uuid", cls, uuid);

        internal override Task<List<EntityEdge>> EdgeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default) =>
            Two<EntityEdge>("edge_get_by_uuids", cls, uuids);

        internal override Task<List<EntityEdge>> EdgeGetByGroupIdsAsync(Type? cls, GraphDriver driver, List<string> groupIds, long? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default) =>
            Two<EntityEdge>("edge_get_by_group_ids", cls, groupIds, limit, uuidCursor);

        internal override Task EdgeLoadEmbeddingsAsync(EntityEdge edge, GraphDriver driver, CancellationToken cancellationToken = default) => None("edge_load_embeddings", edge);

        internal override Task<List<EntityEdge>> EdgeGetBetweenNodesAsync(Type? cls, GraphDriver driver, string sourceNodeUuid, string targetNodeUuid, CancellationToken cancellationToken = default) =>
            Two<EntityEdge>("edge_get_between_nodes", cls, sourceNodeUuid, targetNodeUuid);

        internal override Task<List<EntityEdge>> EdgeGetByNodeUuidAsync(Type? cls, GraphDriver driver, string nodeUuid, CancellationToken cancellationToken = default) =>
            Two<EntityEdge>("edge_get_by_node_uuid", cls, nodeUuid);

        internal override Task EpisodicEdgeSaveAsync(EpisodicEdge edge, GraphDriver driver, CancellationToken cancellationToken = default) => None("episodic_edge_save", edge);

        internal override Task<EpisodicEdge> EpisodicEdgeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
            One<EpisodicEdge>("episodic_edge_get_by_uuid", cls, uuid);

        internal override Task<List<EpisodicEdge>> EpisodicEdgeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default) =>
            Two<EpisodicEdge>("episodic_edge_get_by_uuids", cls, uuids);

        internal override Task<List<EpisodicEdge>> EpisodicEdgeGetByGroupIdsAsync(Type? cls, GraphDriver driver, List<string> groupIds, long? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default) =>
            Two<EpisodicEdge>("episodic_edge_get_by_group_ids", cls, groupIds, limit, uuidCursor);

        internal override Task CommunityEdgeSaveAsync(CommunityEdge edge, GraphDriver driver, CancellationToken cancellationToken = default) => None("community_edge_save", edge);

        internal override Task<CommunityEdge> CommunityEdgeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
            One<CommunityEdge>("community_edge_get_by_uuid", cls, uuid);

        internal override Task<List<CommunityEdge>> CommunityEdgeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default) =>
            Two<CommunityEdge>("community_edge_get_by_uuids", cls, uuids);

        internal override Task<List<CommunityEdge>> CommunityEdgeGetByGroupIdsAsync(Type? cls, GraphDriver driver, List<string> groupIds, long? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default) =>
            Two<CommunityEdge>("community_edge_get_by_group_ids", cls, groupIds, limit, uuidCursor);

        internal override Task HasEpisodeEdgeSaveAsync(HasEpisodeEdge edge, GraphDriver driver, CancellationToken cancellationToken = default) => None("has_episode_edge_save", edge);

        internal override Task HasEpisodeEdgeDeleteAsync(HasEpisodeEdge edge, GraphDriver driver, CancellationToken cancellationToken = default) => None("has_episode_edge_delete", edge);

        internal override Task<HasEpisodeEdge> HasEpisodeEdgeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
            One<HasEpisodeEdge>("has_episode_edge_get_by_uuid", cls, uuid);

        internal override Task<List<HasEpisodeEdge>> HasEpisodeEdgeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default) =>
            Two<HasEpisodeEdge>("has_episode_edge_get_by_uuids", cls, uuids);

        internal override Task<List<HasEpisodeEdge>> HasEpisodeEdgeGetByGroupIdsAsync(Type? cls, GraphDriver driver, List<string> groupIds, long? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default) =>
            Two<HasEpisodeEdge>("has_episode_edge_get_by_group_ids", cls, groupIds, limit, uuidCursor);

        internal override Task NextEpisodeEdgeSaveAsync(NextEpisodeEdge edge, GraphDriver driver, CancellationToken cancellationToken = default) => None("next_episode_edge_save", edge);

        internal override Task NextEpisodeEdgeDeleteAsync(NextEpisodeEdge edge, GraphDriver driver, CancellationToken cancellationToken = default) => None("next_episode_edge_delete", edge);

        internal override Task<NextEpisodeEdge> NextEpisodeEdgeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
            One<NextEpisodeEdge>("next_episode_edge_get_by_uuid", cls, uuid);

        internal override Task<List<NextEpisodeEdge>> NextEpisodeEdgeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default) =>
            Two<NextEpisodeEdge>("next_episode_edge_get_by_uuids", cls, uuids);

        internal override Task<List<NextEpisodeEdge>> NextEpisodeEdgeGetByGroupIdsAsync(Type? cls, GraphDriver driver, List<string> groupIds, long? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default) =>
            Two<NextEpisodeEdge>("next_episode_edge_get_by_group_ids", cls, groupIds, limit, uuidCursor);

        private void Note(string op, object?[] args) =>
            calls.Add(new JsonObject { ["op"] = op, ["args"] = new JsonArray([.. args.Select(Encode)]) });

        private Task None(string op, params object?[] args)
        {
            Note(op, args);
            return Task.CompletedTask;
        }

        private Task<T> One<T>(string op, params object?[] args)
            where T : Edge
        {
            Note(op, args);
            return Task.FromResult((T)one);
        }

        private Task<List<T>> Two<T>(string op, params object?[] args)
            where T : Edge
        {
            Note(op, args);
            return Task.FromResult(new List<T> { (T)one, (T)two });
        }
    }

    private sealed class FakeEmbedder(int delta) : EmbedderClient
    {
        internal List<JsonNode> Calls { get; } = [];

        internal override Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            Calls.Add(new JsonObject { ["op"] = "create", ["input"] = new JsonArray([.. inputData.Select(s => (JsonNode)s)]) });
            return Task.FromResult(new List<double> { Py.Len(inputData[0]), 0.5 });
        }

        internal override Task<List<List<double>>> CreateBatchAsync(List<string> inputDataList, CancellationToken cancellationToken = default)
        {
            Calls.Add(new JsonObject { ["op"] = "create_batch", ["input"] = new JsonArray([.. inputDataList.Select(s => (JsonNode)s)]) });
            var result = inputDataList.Select((text, i) => new List<double> { i, Py.Len(text) }).ToList();
            if (delta < 0)
            {
                result = result.Take(result.Count + delta).ToList();
            }

            for (var i = 0; i < delta; i++)
            {
                result.Add([9.0]);
            }

            return Task.FromResult(result);
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
        internal List<(string? Level, string Message)> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(Lines);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<(string? Level, string Message)> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Add((logLevel == LogLevel.Debug ? "DEBUG" : logLevel.ToString(), formatter(state, exception)));
        }
    }
}
