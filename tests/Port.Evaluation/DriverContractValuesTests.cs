// Evaluation test for graphiti_core/driver/driver.py, record_parsers.py, operations/graph_utils.py, and operations/graph_ops.py
// (port/RULES.md, R7, R23, R25). The recording is values/driver_contract.json, which port/tools/record_driver_contract.py
// wrote from Python. The fakes are at the network boundary: the graph driver, its session, and the query executor.
namespace Graphiti.Port.Evaluation;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Operations;
using Graphiti.PyCompat;

public partial class DriverContractValuesTests
{
    private static readonly JsonElement Root = Load();

    public static TheoryData<int> ParserIndexes() => Indexes("parsers");

    public static TheoryData<int> NeighborIndexes() => Indexes("neighbor");

    public static TheoryData<int> LabelPropagationIndexes() => Indexes("label_propagation");

    [Fact]
    public void The_module_constants_match_Python()
    {
        var c = Root.GetProperty("constants");
        Assert.Equal(DriverModule.DefaultSize, c.GetProperty("DEFAULT_SIZE").GetInt64());
        Assert.Equal(c.GetProperty("ENTITY_INDEX_NAME").GetString(), DriverModule.EntityIndexName);
        Assert.Equal(c.GetProperty("EPISODE_INDEX_NAME").GetString(), DriverModule.EpisodeIndexName);
        Assert.Equal(c.GetProperty("COMMUNITY_INDEX_NAME").GetString(), DriverModule.CommunityIndexName);
        Assert.Equal(c.GetProperty("ENTITY_EDGE_INDEX_NAME").GetString(), DriverModule.EntityEdgeIndexName);
    }

    [Fact]
    public void The_recording_reaches_each_branch_of_the_parsers()
    {
        var parsers = Root.GetProperty("parsers").EnumerateArray().ToList();
        Assert.Contains(parsers, p => p.GetProperty("error").ValueKind == JsonValueKind.Null);
        foreach (var type in new[] { "KeyError", "ValidationError", "ValueError" })
        {
            Assert.Contains(parsers, p => p.GetProperty("error") is { ValueKind: JsonValueKind.Object } e && e.GetProperty("type").GetString() == type);
        }

        Assert.Contains(parsers, p => p.GetProperty("function").GetString() == "episodic_node_from_record" && p.GetProperty("error") is { ValueKind: JsonValueKind.Object } e && e.GetProperty("type").GetString() == "NotImplementedError");
        Assert.True(Root.GetProperty("label_propagation").GetArrayLength() > 50);
    }

    [Theory]
    [MemberData(nameof(ParserIndexes))]
    public void Each_record_parser_matches_Python(int index)
    {
        var entry = Root.GetProperty("parsers")[index];
        var record = DecodeKwargs(entry.GetProperty("record"));
        var function = entry.GetProperty("function").GetString()!;
        var difference = DifferenceByDesign(function, record);
        object? result = null;
        var error = Catch(() => result = DumpNode(function switch
        {
            "entity_node_from_record" => RecordParsers.EntityNodeFromRecord(record),
            "entity_edge_from_record" => RecordParsers.EntityEdgeFromRecord(record),
            "episodic_node_from_record" => RecordParsers.EpisodicNodeFromRecord(record),
            _ => RecordParsers.CommunityNodeFromRecord(record),
        }));
        if (difference is { } by)
        {
            Assert.NotNull(error);
            Assert.Equal(by.Type, error.GetType());
            if (by.Text is not null)
            {
                Assert.Equal(by.Text, error.Message);
            }
        }
        else
        {
            AssertOutcome(entry.GetProperty("error"), error);
            if (error is null)
            {
                AssertSame(entry.GetProperty("result"), (JsonNode?)result);
            }
        }

        // The parser changes the record in place (attributes.pop, labels.remove). A port that copies it differs (R6).
        AssertSame(entry.GetProperty("record_after"), Encode(record));
    }

    // Two outcomes differ from Python by design, and each one has a reason:
    // - GAPS F24: the `in` test of labels keeps the typed read, so a labels value that is not a list throws InvalidCastException.
    //   Python takes str (a substring test), dict (a key test), and None or int (TypeError) there. A tuple or a set that Python accepts is not recorded.
    // - R4: Py.Receiver<OrderedDictionary> refuses a list as attributes with the AttributeError of Python for a missing method, and a Python list has pop.
    //   Python then raises TypeError: pop expected at most 1 argument, got 2.
    private static (Type Type, string? Text)? DifferenceByDesign(string function, OrderedDictionary<string, object?> record)
    {
        if (function is not ("entity_node_from_record" or "entity_edge_from_record") || !record.TryGetValue("attributes", out var attributes))
        {
            return null;
        }

        if (attributes is List<object?>)
        {
            return (typeof(MissingMemberException), "'list' object has no attribute 'pop'");
        }

        if (function == "entity_node_from_record" && attributes is OrderedDictionary<string, object?> && record.TryGetValue("group_id", out var groupId) && groupId is string
            && record.TryGetValue("labels", out var labels) && labels is not List<object?>)
        {
            return (typeof(InvalidCastException), null);
        }

        return null;
    }

    [Theory]
    [MemberData(nameof(NeighborIndexes))]
    public void Each_Neighbor_construction_matches_Python(int index)
    {
        var entry = Root.GetProperty("neighbor")[index];
        var data = DecodeKwargs(entry.GetProperty("data"));
        object? result = null;
        var error = Catch(() => result = Encode(Neighbor.ModelValidate(data).ModelDump()));
        AssertOutcome(entry.GetProperty("error"), error);
        if (error is null)
        {
            AssertSame(entry.GetProperty("result"), (JsonNode?)result);
        }
    }

    [Fact]
    public void The_schema_of_Neighbor_matches_Python()
    {
        Assert.Equal(Root.GetProperty("neighbor_schema").GetString(), PyJson.Dumps(Neighbor.ModelJsonSchema.ToDict()));
    }

    [Theory]
    [MemberData(nameof(LabelPropagationIndexes))]
    public void Each_label_propagation_matches_Python(int index)
    {
        var entry = Root.GetProperty("label_propagation")[index];
        var projection = new OrderedDictionary<string, List<Neighbor>>();
        foreach (var pair in entry.GetProperty("projection").EnumerateArray())
        {
            projection[pair[0].GetString()!] = [.. pair[1].EnumerateArray().Select(n => new Neighbor { NodeUuid = n[0].GetString()!, EdgeCount = n[1].GetInt64() }.Validate())];
        }

        // PORT: upstream defect U11: label_propagation does not end for some inputs. A wrong tie order makes a recorded graph loop, so a case that does not end fails.
        List<List<string>>? result = null;
        Exception? error = null;
        var worker = new Thread(() => error = Catch(() => result = GraphUtils.LabelPropagation(projection))) { IsBackground = true };
        worker.Start();
        Assert.True(worker.Join(TimeSpan.FromSeconds(5)), "label_propagation did not end within 5 seconds");
        AssertOutcome(entry.GetProperty("error"), error);
        if (error is null)
        {
            Assert.Equal(
                entry.GetProperty("result").EnumerateArray().Select(c => c.EnumerateArray().Select(u => u.GetString()!).ToList()).ToList(),
                result);
        }
    }

    [Fact]
    public void with_database_returns_a_shallow_copy_with_another_default_database()
    {
        foreach (var e in Root.GetProperty("driver").GetProperty("with_database").EnumerateArray())
        {
            var driver = new FakeDriver(PyEnum.FromValue<GraphProvider>(e.GetProperty("provider").GetString()!), "first", []);
            var cloned = (FakeDriver)driver.WithDatabase(e.GetProperty("database").GetString()!);
            Assert.Equal(e.GetProperty("same_object").GetBoolean(), ReferenceEquals(cloned, driver));
            Assert.Equal(e.GetProperty("type").GetString(), cloned.GetType().Name);
            Assert.Equal(e.GetProperty("database_of_clone").GetString(), cloned.Database);
            Assert.Equal(e.GetProperty("database_of_original").GetString(), driver.Database);
            Assert.Equal(e.GetProperty("provider_of_clone").GetString(), (string)PyEnum.Value(cloned.Provider));
            Assert.Equal(e.GetProperty("default_group_id").GetString(), cloned.DefaultGroupId);
            Assert.Equal(e.GetProperty("same_connection").GetBoolean(), ReferenceEquals(cloned.Connection, driver.Connection));
            var again = (FakeDriver)cloned.WithDatabase("third");
            var chain = e.GetProperty("chain").EnumerateArray().ToList();
            Assert.Equal(chain[0].GetString(), cloned.Database);
            Assert.Equal(chain[1].GetString(), again.Database);
            Assert.Equal(chain[2].GetBoolean(), ReferenceEquals(again, cloned));
        }
    }

    [Fact]
    public void clone_returns_the_same_driver()
    {
        foreach (var e in Root.GetProperty("driver").GetProperty("clone").EnumerateArray())
        {
            var driver = new FakeDriver(PyEnum.FromValue<GraphProvider>(e.GetProperty("provider").GetString()!), "first", []);
            Assert.Equal(e.GetProperty("same_object").GetBoolean(), ReferenceEquals(driver.Clone(e.GetProperty("database").GetString()!), driver));
            Assert.Equal(e.GetProperty("database_after").GetString(), driver.Database);
        }
    }

    [Fact]
    public void build_fulltext_query_raises_NotImplementedError_with_the_provider()
    {
        foreach (var e in Root.GetProperty("driver").GetProperty("build_fulltext_query").EnumerateArray())
        {
            var driver = new FakeDriver(PyEnum.FromValue<GraphProvider>(e.GetProperty("provider").GetString()!), "first", []);
            var args = e.GetProperty("args").GetProperty("items").EnumerateArray().Select(Decode).ToList();
            var error = Catch(() => driver.BuildFulltextQuery((string)args[0]!, args.Count > 1 ? (args[1] as List<object?>)?.Cast<string>().ToList() : null, args.Count > 2 ? (long)args[2]! : 128));
            AssertOutcome(e.GetProperty("error"), error);
        }
    }

    [Fact]
    public async Task transaction_wraps_a_session_and_closes_it()
    {
        foreach (var e in Root.GetProperty("driver").GetProperty("transaction").EnumerateArray())
        {
            var calls = new List<JsonObject>();
            var driver = new FakeDriver(PyEnum.FromValue<GraphProvider>(e.GetProperty("provider").GetString()!), "first", calls);
            var body = e.GetProperty("body").GetString();
            var error = await CatchAsync(() => driver.TransactionAsync(async tx =>
            {
                // PORT: type(tx).__name__ is _SessionTransaction, and the port drops the underscore (R2).
                Assert.Equal(e.GetProperty("tx_type").GetString(), "_" + tx.GetType().Name);
                switch (body)
                {
                    case "run":
                        await tx.RunAsync("MATCH (n) WHERE n.uuid = $uuid RETURN n", new() { ["uuid"] = "u-1", ["names"] = new List<object?> { "a", "é" }, ["flag"] = null });
                        break;
                    case "two_runs":
                        await tx.RunAsync("A", new() { ["x"] = 1L });
                        await tx.RunAsync("B", new());
                        break;
                    case "raise":
                        throw new ArgumentException("boom");
                }
            }));
            AssertOutcome(e.GetProperty("error"), error);

            // PORT: tx.run returns the result of session.run in Python. The port returns Task, so the result is not observed.
            var expected = e.GetProperty("calls").EnumerateArray().Select(c => JsonNode.Parse(c.GetRawText())!.AsObject()).ToList();
            Assert.Equal(expected.Select(c => c.ToJsonString()), calls.Select(c => c.ToJsonString()));
        }
    }

    // No Python original: the finally of GraphDriver.transaction runs `await session.close()` to the end, also after a cancellation of the
    // body. The fake session honors its token, so a close with the canceled token never records (R8).
    [Fact]
    public async Task a_canceled_body_still_closes_the_session()
    {
        using var source = new CancellationTokenSource();
        var calls = new List<JsonObject>();
        var driver = new FakeDriver(GraphProvider.Neo4j, "first", calls);
        var error = await CatchAsync(() => driver.TransactionAsync(
            async tx =>
            {
                // A fake cancels with Cancel(), which runs the callbacks of the token at once on the scheduler thread, as
                // `task.cancel()` acts at one point of the loop in Python. CancelAsync runs them on the thread pool (RULES.md R25).
                source.Cancel();
                await tx.RunAsync("Q", new(), source.Token);
            },
            source.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(error);
        Assert.Equal(["session", "close"], calls.Select(c => (string)c["via"]!));
    }

    // R25: each async method with a body that the fakes reach throws for a canceled token.
    [Theory]
    [InlineData("transaction")]
    [InlineData("remove_communities_all")]
    [InlineData("remove_communities_groups")]
    public async Task A_canceled_token_stops_each_async_method(string method)
    {
        var token = new CancellationToken(true);
        var calls = new List<JsonObject>();
        var driver = new FakeDriver(GraphProvider.Neo4j, "first", calls);
        var executor = new FakeExecutor(calls);
        Func<Task> call = method switch
        {
            "transaction" => () => driver.TransactionAsync(tx => tx.RunAsync("Q", new(), token), token),
            "remove_communities_all" => () => new Ops().RemoveCommunitiesAsync(executor, null, token),
            _ => () => new Ops().RemoveCommunitiesAsync(executor, ["g1"], token),
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(call);
        Assert.DoesNotContain(calls, c => c["query"] is not null);
    }

    [Fact]
    public void The_defaults_of_GraphDriver_match_Python()
    {
        var d = Root.GetProperty("driver").GetProperty("defaults");
        var driver = new FakeDriver(GraphProvider.Neo4j, "first", []);
        var bare = new BareDriver();
        Assert.Equal(d.GetProperty("fulltext_syntax").GetString(), bare.FulltextSyntax);
        Assert.Equal(d.GetProperty("default_group_id").GetString(), bare.DefaultGroupId);
        Assert.Null(bare.SearchInterface);
        Assert.Null(bare.GraphOperationsInterface);
        Assert.Equal(d.GetProperty("RELATE").GetString(), (string)PyEnum.Value(GraphDriver.Relate));
        var props = d.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetProperty("type").GetString());
        Assert.All(props.Values, v => Assert.Equal("none", v));
        Assert.Null(driver.EntityNodeOps);
        Assert.Null(driver.EpisodeNodeOps);
        Assert.Null(driver.CommunityNodeOps);
        Assert.Null(driver.SagaNodeOps);
        Assert.Null(driver.EntityEdgeOps);
        Assert.Null(driver.EpisodicEdgeOps);
        Assert.Null(driver.CommunityEdgeOps);
        Assert.Null(driver.HasEpisodeEdgeOps);
        Assert.Null(driver.NextEpisodeEdgeOps);
        Assert.Null(driver.SearchOps);
        Assert.Null(driver.GraphOps);
        Assert.Equal(11, props.Count);
    }

    [Fact]
    public async Task remove_communities_runs_the_query_of_Python()
    {
        foreach (var e in Root.GetProperty("remove_communities").EnumerateArray())
        {
            var calls = new List<JsonObject>();
            var executor = new FakeExecutor(calls);
            var groupIds = e.GetProperty("group_ids");
            var ops = new Ops();
            if (groupIds.ValueKind == JsonValueKind.String)
            {
                await ops.RemoveCommunitiesAsync(executor, null, TestContext.Current.CancellationToken);
            }
            else
            {
                var decoded = Decode(groupIds);
                await ops.RemoveCommunitiesAsync(executor, (decoded as List<object?>)?.Cast<string>().ToList(), TestContext.Current.CancellationToken);
            }

            var expected = e.GetProperty("calls").EnumerateArray().Select(c => JsonNode.Parse(c.GetRawText())!.AsObject()).ToList();
            Assert.Equal(expected.Select(c => c.ToJsonString()), calls.Select(c => c.ToJsonString()));
        }
    }

    private static TheoryData<int> Indexes(string section)
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Root.GetProperty(section).GetArrayLength(); i++)
        {
            data.Add(i);
        }

        return data;
    }

    private static JsonObject DumpNode(object node)
    {
        var (name, data) = node switch
        {
            EntityNode n => ((string)n.PythonName, n.ModelDump()),
            EpisodicNode n => (n.PythonName, n.ModelDump()),
            CommunityNode n => (n.PythonName, n.ModelDump()),
            EntityEdge n => ("EntityEdge", n.ModelDump()),
            _ => throw new InvalidOperationException(node.GetType().Name),
        };
        return new JsonObject { ["class"] = name, ["dump"] = Encode(data) };
    }

    private static Exception? Catch(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private static async Task<Exception?> CatchAsync(Func<Task> action)
    {
        try
        {
            await action();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    private static void AssertOutcome(JsonElement expected, Exception? error)
    {
        if (expected.ValueKind == JsonValueKind.Null)
        {
            Assert.True(error is null, error?.ToString());
            return;
        }

        Assert.NotNull(error);
        var type = expected.GetProperty("type").GetString();

        // Each class is exact. A subclass is another Python class, and R4 maps a Python class to a subclass only where it names one.
        // ValidationException derives from ArgumentException, so a ValueError of Python is the base class here.
        var expectedType = type switch
        {
            "ValidationError" => typeof(ValidationException),
            "KeyError" => typeof(KeyNotFoundException),
            "ValueError" => typeof(ArgumentException),
            "NotImplementedError" => typeof(NotImplementedException),
            "AttributeError" => typeof(MissingMemberException),
            "TypeError" => typeof(InvalidCastException),
            _ => throw new InvalidOperationException(type),
        };
        Assert.True(error.GetType() == expectedType, $"expected {type} but got {error}");

        var text = expected.GetProperty("text").GetString()!;
        switch (type)
        {
            case "ValidationError":
                Assert.Equal(PydanticInput().Replace(text, "]"), error.Message);
                break;
            case "KeyError":
                // A KeyError is compared by type only (R25): its text is the Python repr of the key.
                break;
            case "TypeError":
                // The typed read names the C# type, so a TypeError text differs from Python by design. The text is compared where the port gives it.
                if (!text.Contains("is not iterable", StringComparison.Ordinal))
                {
                    Assert.Equal(text, error.Message);
                }

                break;
            default:
                Assert.Equal(text, error.Message);
                break;
        }
    }

    private static void AssertSame(JsonElement expected, JsonNode? actual) =>
        Assert.True(
            JsonNode.DeepEquals(JsonNode.Parse(expected.GetRawText()), actual),
            $"expected {expected.GetRawText()} but got {actual?.ToJsonString()}");

    private static JsonNode Encode(object? value) => value switch
    {
        null => new JsonObject { ["type"] = "none" },
        bool b => new JsonObject { ["type"] = "bool", ["value"] = b },
        long l => new JsonObject { ["type"] = "int", ["value"] = l },
        double d => new JsonObject { ["type"] = "float", ["value"] = d },
        string s => new JsonObject { ["type"] = "str", ["value"] = s },
        PyDateTime dt => new JsonObject { ["type"] = "datetime", ["value"] = dt.IsoFormat() },
        EpisodeType t => new JsonObject { ["type"] = "enum", ["class"] = "EpisodeType", ["value"] = (string)PyEnum.Value(t) },
        OrderedDictionary<string, object?> dict => new JsonObject
        {
            ["type"] = "dict",
            ["items"] = new JsonArray([.. dict.Select(kv => (JsonNode?)new JsonArray(kv.Key, Encode(kv.Value)))]),
        },
        System.Collections.IEnumerable items => new JsonObject
        {
            ["type"] = "list",
            ["items"] = new JsonArray([.. items.Cast<object?>().Select(Encode)]),
        },
        _ => new JsonObject { ["type"] = value.GetType().Name },
    };

    private static object? Decode(JsonElement e) => e.GetProperty("type").GetString() switch
    {
        "none" => null,
        "bool" => e.GetProperty("value").GetBoolean(),
        "int" => e.GetProperty("value").TryGetInt64(out var l) ? (object)l : System.Numerics.BigInteger.Parse(e.GetProperty("value").GetRawText()),
        "float" => e.GetProperty("value").GetDouble(),
        "str" => e.GetProperty("value").GetString(),
        "datetime" => PyDateTime.FromIsoFormat(e.GetProperty("value").GetString()!),
        "list" => e.GetProperty("items").EnumerateArray().Select(Decode).ToList(),
        "dict" => DecodeKwargs(e.GetProperty("items")),
        var t => throw new InvalidOperationException(t),
    };

    private static OrderedDictionary<string, object?> DecodeKwargs(JsonElement pairs)
    {
        var dict = new OrderedDictionary<string, object?>();
        foreach (var pair in pairs.EnumerateArray())
        {
            dict[pair[0].GetString()!] = Decode(pair[1]);
        }

        return dict;
    }

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "driver_contract.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    [GeneratedRegex(@", input_value=.*?, input_type=\w+\]\n    For further information visit \S+", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex PydanticInput();

    private static JsonObject Call(string? via, string query, OrderedDictionary<string, object?> parameters)
    {
        var call = new JsonObject();
        if (via is not null)
        {
            call["via"] = via;
        }

        call["query"] = query;
        call["keys"] = new JsonArray([.. parameters.Keys.Select(k => (JsonNode?)k)]);
        call["values"] = new JsonArray([.. parameters.Values.Select(Encode)]);
        return call;
    }

    private class BareDriver : GraphDriver
    {
        internal override string PythonName => "BareDriver";

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class FakeDriver : BareDriver
    {
        private readonly List<JsonObject> calls;

        internal FakeDriver(GraphProvider provider, string database, List<JsonObject> calls)
        {
            Provider = provider;
            Database = database;
            DefaultGroupId = "dg";
            this.calls = calls;
        }

        internal object Connection { get; } = new();

        internal override string PythonName => "FakeDriver";

        internal override GraphDriverSession Session(string? database = null)
        {
            calls.Add(new JsonObject { ["via"] = "session", ["database"] = database });
            return new FakeSession(calls);
        }
    }

    private sealed class FakeSession(List<JsonObject> calls) : GraphDriverSession
    {
        public override ValueTask DisposeAsync()
        {
            calls.Add(new JsonObject { ["via"] = "exit" });
            return ValueTask.CompletedTask;
        }

        internal override async Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(Call("run", query, parameters));
        }

        internal override async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(new JsonObject { ["via"] = "close" });
        }

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, double timeout, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class FakeExecutor(List<JsonObject> calls) : QueryExecutor
    {
        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(Call(null, cypherQuery, parameters));
            return ([], null, null);
        }
    }

    private sealed class Ops : GraphMaintenanceOperations
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
}
