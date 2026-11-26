// Evaluation test for graphiti_core/nodes.py (port/RULES.md, R7, R23, R25).
// The recording is values/nodes.json, which port/tools/record_nodes.py wrote from Python.
// The fakes replace the graph driver, its session, the graph operations interface, and the embedder. They are not the network
// boundary: the judge has no case for an async method of this module, so this recording is the evidence for those methods.
// The calls and the log lines are one list in the order of their time. Each fake method yields before it records its call,
// so a call that the code does not await lands after the method returned and the test sees a missing call.
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

public partial class NodesValuesTests
{
    private static readonly JsonElement Root = Load();

    private static readonly List<string> Queries = [.. Root.GetProperty("queries").EnumerateArray().Select(q => q.GetString()!)];

    public static TheoryData<int> ConstructIndexes() => Indexes("construct");

    public static TheoryData<int> RecordIndexes() => Indexes("records");

    public static TheoryData<int> MethodIndexes() => Indexes("methods");

    [Fact]
    public void The_recording_holds_each_method_of_each_node_class_with_and_without_graph_operations()
    {
        var methods = Root.GetProperty("methods").EnumerateArray().Select(m => m.GetProperty("input")).ToList();
        string Key(JsonElement i) => i.GetProperty("class").GetString() + "." + i.GetProperty("method").GetString();
        var names = methods.Select(Key).Distinct().Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            [
                "CommunityNode.delete", "CommunityNode.generate_name_embedding", "CommunityNode.get_by_group_ids", "CommunityNode.get_by_uuid",
                "CommunityNode.get_by_uuids", "CommunityNode.load_name_embedding", "CommunityNode.save",
                "EntityNode.delete", "EntityNode.generate_name_embedding", "EntityNode.get_by_attribute_values", "EntityNode.get_by_group_ids",
                "EntityNode.get_by_names", "EntityNode.get_by_uuid", "EntityNode.get_by_uuids", "EntityNode.load_name_embedding", "EntityNode.save",
                "EpisodicNode.delete", "EpisodicNode.get_by_entity_node_uuid", "EpisodicNode.get_by_group_ids", "EpisodicNode.get_by_uuid",
                "EpisodicNode.get_by_uuids", "EpisodicNode.save",
                "Node.delete_by_group_id", "Node.delete_by_uuids",
                "SagaNode.delete", "SagaNode.get_by_group_ids", "SagaNode.get_by_uuid", "SagaNode.get_by_uuids", "SagaNode.save",
                "module.create_entity_node_embeddings",
            ],
            names);
        foreach (var name in names.Where(n => !n.StartsWith("module.", StringComparison.Ordinal)))
        {
            var modes = methods.Where(m => Key(m) == name).Select(m => m.GetProperty("mode").GetString()).Distinct().Order(StringComparer.Ordinal);
            Assert.Equal(["none", "raise", "return"], modes);
        }

        Assert.Contains(Root.GetProperty("methods").EnumerateArray(), m => m.GetProperty("output").GetProperty("error") is { ValueKind: JsonValueKind.Object } e && e.GetProperty("type").GetString() == "NodeNotFoundError");
    }

    [Theory]
    [MemberData(nameof(ConstructIndexes))]
    public void Each_construction_matches_Python(int index)
    {
        var entry = Root.GetProperty("construct")[index];
        var input = entry.GetProperty("input");
        var kwargs = DecodeKwargs(input.GetProperty("kwargs"));
        var output = entry.GetProperty("output");
        object? result = null;
        var error = Catch(() => result = DumpNode(ModelValidate(input.GetProperty("class").GetString()!, kwargs), kwargs));
        AssertOutcome(output.GetProperty("error"), error);
        if (error is null)
        {
            AssertSame(output.GetProperty("result"), (JsonNode?)result);
        }
    }

    [Theory]
    [MemberData(nameof(RecordIndexes))]
    public void Each_record_helper_matches_Python(int index)
    {
        var entry = Root.GetProperty("records")[index];
        var input = entry.GetProperty("input");
        var record = DecodeKwargs(input.GetProperty("record"));
        var output = entry.GetProperty("output");
        object? result = null;
        var error = Catch(() => result = input.GetProperty("function").GetString() switch
        {
            "get_episodic_node_from_record" => DumpNode(Nodes.GetEpisodicNodeFromRecord(record)),
            "get_entity_node_from_record" => DumpNode(Nodes.GetEntityNodeFromRecord(record, GraphProvider.Neo4j)),
            "get_community_node_from_record" => DumpNode(Nodes.GetCommunityNodeFromRecord(record)),
            _ => DumpNode(Nodes.GetSagaNodeFromRecord(record)),
        });
        if (input.GetProperty("function").GetString() == "get_entity_node_from_record" && DifferenceByDesign(record) is { } by)
        {
            Assert.NotNull(error);
            Assert.Equal(by.Type, error.GetType());
            if (by.Text is not null)
            {
                Assert.Equal(by.Text, error.Message);
            }

            return;
        }

        AssertOutcome(output.GetProperty("error"), error);
        if (error is null)
        {
            AssertSame(output.GetProperty("result"), (JsonNode?)result);
        }
    }

    // Two outcomes differ from Python by design, and each one has a reason:
    // - GAPS F24: the `in` test of labels keeps the typed read, so a labels value that is not a list throws InvalidCastException.
    //   Python takes str (a substring test) and None or int (TypeError) there.
    // - R4: Py.Receiver<OrderedDictionary> refuses a list as attributes with the AttributeError of Python for a missing method, and a Python list has pop.
    //   Python then raises TypeError: pop expected at most 1 argument, got 2.
    private static (Type Type, string? Text)? DifferenceByDesign(OrderedDictionary<string, object?> record)
    {
        if (!record.TryGetValue("attributes", out var attributes))
        {
            return null;
        }

        if (attributes is List<object?>)
        {
            return (typeof(MissingMemberException), "'list' object has no attribute 'pop'");
        }

        if (attributes is OrderedDictionary<string, object?> && record.TryGetValue("group_id", out var groupId) && groupId is string
            && record.TryGetValue("labels", out var labels) && labels is not List<object?>)
        {
            return (typeof(InvalidCastException), null);
        }

        return null;
    }

    [Fact]
    public void Equality_and_hash_match_Python()
    {
        var t0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var t1 = new DateTimeOffset(2026, 2, 1, 12, 30, 15, TimeSpan.Zero).AddTicks(1234560);
        OrderedDictionary<string, object?> K(string uuid, string name, string groupId, DateTimeOffset? createdAt = null)
        {
            var d = new OrderedDictionary<string, object?> { ["uuid"] = uuid, ["name"] = name, ["group_id"] = groupId };
            if (createdAt is { } c)
            {
                d["created_at"] = PyDateTime.FromDateTimeOffset(c);
            }

            return d;
        }

        var a = EntityNode.ModelValidate(K("u1", "A", "g", t0));
        var episodic = K("u1", "E", "g", t0);
        episodic["source"] = "text";
        episodic["source_description"] = "d";
        episodic["content"] = "c";
        episodic["valid_at"] = PyDateTime.FromDateTimeOffset(t0);
        var pairs = new Dictionary<string, (object Left, object? Right)>(StringComparer.Ordinal)
        {
            ["same_uuid_other_fields"] = (a, EntityNode.ModelValidate(K("u1", "B", "h", t1))),
            ["other_uuid_same_fields"] = (a, EntityNode.ModelValidate(K("u2", "A", "g", t0))),
            ["same_object"] = (a, a),
            ["entity_community"] = (a, CommunityNode.ModelValidate(K("u1", "C", "g", t0))),
            ["entity_episodic"] = (a, EpisodicNode.ModelValidate(episodic)),
            ["saga_entity"] = (SagaNode.ModelValidate(K("u1", "S", "g", t0)), a),
            ["node_str"] = (a, "u1"),
            ["node_none"] = (a, null),
            ["unicode_uuid"] = (EntityNode.ModelValidate(K("Zoë 😀", "A", "g")), EntityNode.ModelValidate(K("Zoë 😀", "B", "g"))),
        };
        var expected = Root.GetProperty("equality").EnumerateArray().ToList();
        Assert.Equal(expected.Count, pairs.Count);
        foreach (var e in expected)
        {
            var (left, right) = pairs[e.GetProperty("name").GetString()!];
            Assert.Equal(e.GetProperty("eq").GetBoolean(), left.Equals(right));
            Assert.Equal(e.GetProperty("hash_eq").GetBoolean(), right is Node && left.GetHashCode() == right.GetHashCode());
        }
    }

    [Fact]
    public void Copies_of_list_and_dict_values_match_Python()
    {
        foreach (var e in Root.GetProperty("copies").EnumerateArray())
        {
            var className = e.GetProperty("class").GetString()!;
            var field = e.GetProperty("field").GetString()!;
            var op = e.GetProperty("op").GetString()!;
            var kwargs = BaseKwargs(className);
            var label = $"{className}.{field} {op}";
            switch (op)
            {
                case "construct":
                    {
                        var value = kwargs[field];
                        var node = ModelValidate(className, kwargs);
                        Assert.True(e.GetProperty("same").GetBoolean() == ReferenceEquals(Field(node, field), value), label);
                        break;
                    }

                case "assign":
                    {
                        var node = ModelValidate(className, kwargs);
                        var replacement = Assign(node, field);
                        Assert.True(e.GetProperty("same").GetBoolean() == ReferenceEquals(Field(node, field), replacement), label);
                        break;
                    }

                default:
                    {
                        var node = ModelValidate(className, kwargs);
                        if (node is SagaNode saga)
                        {
                            saga.SummaryEpisodesFetched = 3;
                            saga.SummaryEpisodesSelected = 2;
                            saga.SummaryEpisodesSkipped = 1;
                            saga.SummarySelectedEpisodeBytes = 99;
                        }

                        var deep = op == "model_copy_deep";
                        Node copy = node switch
                        {
                            EpisodicNode n => n.ModelCopy(deep),
                            EntityNode n => n.ModelCopy(deep),
                            CommunityNode n => n.ModelCopy(deep),
                            SagaNode n => n.ModelCopy(deep),
                            _ => throw new InvalidOperationException(className),
                        };
                        if (field == "private")
                        {
                            var s = (SagaNode)copy;
                            Assert.Equal(
                                e.GetProperty("values").EnumerateArray().Select(v => v.GetInt64()),
                                [s.SummaryEpisodesFetched, s.SummaryEpisodesSelected, s.SummaryEpisodesSkipped, s.SummarySelectedEpisodeBytes]);
                        }
                        else if (field == "dump")
                        {
                            Assert.True(e.GetProperty("same").GetBoolean() == JsonNode.DeepEquals(DumpNode(copy), DumpNode(node)), label);
                        }
                        else if (field == "attributes.nested")
                        {
                            Assert.True(e.GetProperty("same").GetBoolean() == ReferenceEquals(((EntityNode)copy).Attributes["nested"], ((EntityNode)node).Attributes["nested"]), label);
                        }
                        else if (field == "episode_metadata.nested")
                        {
                            Assert.True(e.GetProperty("same").GetBoolean() == ReferenceEquals(((EpisodicNode)copy).EpisodeMetadata!["n"], ((EpisodicNode)node).EpisodeMetadata!["n"]), label);
                        }
                        else
                        {
                            Assert.True(e.GetProperty("same").GetBoolean() == ReferenceEquals(Field(copy, field), Field(node, field)), label);
                        }

                        break;
                    }
            }
        }
    }

    [Theory]
    [MemberData(nameof(MethodIndexes))]
    public async Task Each_method_call_the_result_and_the_log_match_Python(int index)
    {
        var entry = Root.GetProperty("methods")[index];
        var input = entry.GetProperty("input");
        var output = entry.GetProperty("output");
        var calls = new List<JsonObject>();
        var capture = new CaptureProvider(calls);
        using var factory = new CaptureFactory(capture);
        using var scope = PortServices.Use(loggerFactory: factory);
        var token = TestContext.Current.CancellationToken;
        var className = input.GetProperty("class").GetString()!;
        var method = input.GetProperty("method").GetString()!;

        if (className == "module")
        {
            var names = input.GetProperty("names").EnumerateArray().Select(n => n.GetString()!).ToList();
            var t0 = PyDateTime.FromDateTimeOffset(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var built = names.Select((name, i) => new EntityNode { Uuid = "n" + Py.Str((long)i), Name = name, GroupId = "g", CreatedAt = t0 }.Validate()).ToList();
            var moduleError = await CatchAsync(() => Nodes.CreateEntityNodeEmbeddingsAsync(new FakeEmbedder(calls, input.GetProperty("embeddings").GetString()!), built, token));
            AssertOutcome(output.GetProperty("error"), moduleError);
            AssertCalls(output.GetProperty("calls"), calls);

            // zip(strict=True) assigns each pair before it finds the short or long side, so the embeddings are compared after an error too.
            Assert.Equal(output.GetProperty("embeddings").EnumerateArray().Count(), built.Count);
            foreach (var (expected, entity) in output.GetProperty("embeddings").EnumerateArray().Zip(built))
            {
                AssertSame(expected, Encode(entity.NameEmbedding));
            }

            return;
        }

        var driver = new FakeDriver(calls, PyEnum.FromValue<GraphProvider>(input.GetProperty("provider").GetString()!), [.. Py.AsList(Decode(input.GetProperty("records"))).Select(Py.AsDict)]);
        var mode = input.GetProperty("mode").GetString();
        if (mode == "raise")
        {
            driver.GraphOperationsInterface = new RaisingOps();
        }
        else if (mode == "return")
        {
            var ops = new ReturningOps(calls);
            if (input.TryGetProperty("ops_result", out var opsResult))
            {
                var built = opsResult.EnumerateArray().Select(k => (object)ModelValidate(className, DecodeKwargs(k))).ToList();
                ops.Value = input.GetProperty("ops_list").GetBoolean() ? ListOf(className, built) : built[0];
            }

            driver.GraphOperationsInterface = ops;
        }

        var args = DecodeKwargs(input.GetProperty("args"));
        object? Arg(string name) => args[name];
        string Str(string name) => Py.AsString(args[name]);
        string? OptStr(string name) => (string?)args[name];
        List<string> Strs(string name) => [.. Py.AsList(args[name]).Select(Py.AsString)];
        long? OptLong(string name) => args[name] is long l ? l : null;
        bool Bool(string name) => Py.AsBool(args[name]);

        var node = input.TryGetProperty("node", out var nodeKwargs) ? ModelValidate(className, DecodeKwargs(nodeKwargs)) : null;
        object? result = null;
        var error = await CatchAsync(async () =>
        {
            switch (className, method)
            {
                case (_, "delete"):
                    await node!.DeleteAsync(driver, token);
                    break;
                case (_, "save"):
                    await node!.SaveAsync(driver, token);
                    break;
                case ("Node", "delete_by_group_id") when !args.ContainsKey("batch_size"):
                    await Node.DeleteByGroupIdAsync(driver, Str("group_id"), cancellationToken: token);
                    break;
                case ("Node", "delete_by_group_id"):
                    await Node.DeleteByGroupIdAsync(driver, Str("group_id"), Py.AsLong(Arg("batch_size")), token);
                    break;
                case ("Node", "delete_by_uuids") when !args.ContainsKey("batch_size"):
                    await Node.DeleteByUuidsAsync(driver, Strs("uuids"), cancellationToken: token);
                    break;
                case ("Node", "delete_by_uuids"):
                    await Node.DeleteByUuidsAsync(driver, Strs("uuids"), Py.AsLong(Arg("batch_size")), token);
                    break;
                case ("EpisodicNode", "get_by_uuid"):
                    result = DumpResult(await EpisodicNode.GetByUuidAsync(driver, Str("uuid"), OptStr("group_id"), token));
                    break;
                case ("EpisodicNode", "get_by_uuids"):
                    result = DumpResult(await EpisodicNode.GetByUuidsAsync(driver, Strs("uuids"), OptStr("group_id"), Bool("require_all"), token));
                    break;
                case ("EpisodicNode", "get_by_group_ids"):
                    result = DumpNodes(await EpisodicNode.GetByGroupIdsAsync(driver, Strs("group_ids"), OptLong("limit"), OptStr("uuid_cursor"), token));
                    break;
                case ("EpisodicNode", "get_by_entity_node_uuid"):
                    result = DumpNodes(await EpisodicNode.GetByEntityNodeUuidAsync(driver, Str("entity_node_uuid"), token));
                    break;
                case ("EntityNode", "generate_name_embedding"):
                    result = Encode(await ((EntityNode)node!).GenerateNameEmbeddingAsync(new FakeEmbedder(calls, "echo"), token));
                    break;
                case ("CommunityNode", "generate_name_embedding"):
                    result = Encode(await ((CommunityNode)node!).GenerateNameEmbeddingAsync(new FakeEmbedder(calls, "echo"), token));
                    break;
                case ("EntityNode", "load_name_embedding"):
                    await ((EntityNode)node!).LoadNameEmbeddingAsync(driver, token);
                    break;
                case ("CommunityNode", "load_name_embedding"):
                    await ((CommunityNode)node!).LoadNameEmbeddingAsync(driver, token);
                    break;
                case ("EntityNode", "get_by_uuid"):
                    result = DumpNode(await EntityNode.GetByUuidAsync(driver, Str("uuid"), token));
                    break;
                case ("EntityNode", "get_by_uuids"):
                    result = DumpNodes(await EntityNode.GetByUuidsAsync(driver, Strs("uuids"), OptStr("group_id"), token));
                    break;
                case ("EntityNode", "get_by_names"):
                    result = DumpNodes(await EntityNode.GetByNamesAsync(driver, Strs("names"), Strs("group_ids"), token));
                    break;
                case ("EntityNode", "get_by_attribute_values") when !args.ContainsKey("limit"):
                    result = DumpNodes(await EntityNode.GetByAttributeValuesAsync(
                        driver,
                        Str("group_id"),
                        Py.AsDict(Arg("attributes")),
                        Arg("labels") is null ? null : Strs("labels"),
                        cancellationToken: token));
                    break;
                case ("EntityNode", "get_by_attribute_values"):
                    result = DumpNodes(await EntityNode.GetByAttributeValuesAsync(
                        driver,
                        Str("group_id"),
                        Py.AsDict(Arg("attributes")),
                        Arg("labels") is null ? null : Strs("labels"),
                        Py.AsLong(Arg("limit")),
                        token));
                    break;
                case ("EntityNode", "get_by_group_ids"):
                    result = DumpNodes(await EntityNode.GetByGroupIdsAsync(driver, Strs("group_ids"), OptLong("limit"), OptStr("uuid_cursor"), Bool("with_embeddings"), token));
                    break;
                case ("CommunityNode", "get_by_uuid"):
                    result = DumpNode(await CommunityNode.GetByUuidAsync(driver, Str("uuid"), token));
                    break;
                case ("CommunityNode", "get_by_uuids"):
                    result = DumpNodes(await CommunityNode.GetByUuidsAsync(driver, Strs("uuids"), token));
                    break;
                case ("CommunityNode", "get_by_group_ids"):
                    result = DumpNodes(await CommunityNode.GetByGroupIdsAsync(driver, Strs("group_ids"), OptLong("limit"), OptStr("uuid_cursor"), token));
                    break;
                case ("SagaNode", "get_by_uuid"):
                    result = DumpNode(await SagaNode.GetByUuidAsync(driver, Str("uuid"), token));
                    break;
                case ("SagaNode", "get_by_uuids"):
                    result = DumpNodes(await SagaNode.GetByUuidsAsync(driver, Strs("uuids"), token));
                    break;
                case ("SagaNode", "get_by_group_ids"):
                    result = DumpNodes(await SagaNode.GetByGroupIdsAsync(driver, Strs("group_ids"), OptLong("limit"), OptStr("uuid_cursor"), token));
                    break;
                default:
                    throw new InvalidOperationException($"{className}.{method}");
            }
        });

        AssertOutcome(output.GetProperty("error"), error);
        AssertCalls(output.GetProperty("calls"), calls);
        if (output.TryGetProperty("result", out var expectedResult))
        {
            AssertSame(expectedResult, (JsonNode?)result);
        }

        if (output.TryGetProperty("node", out var expectedNode))
        {
            AssertSame(expectedNode, DumpNode(node!));
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

    private static object ListOf(string className, List<object> nodes) => className switch
    {
        "EpisodicNode" => nodes.Cast<EpisodicNode>().ToList(),
        "EntityNode" => nodes.Cast<EntityNode>().ToList(),
        "CommunityNode" => nodes.Cast<CommunityNode>().ToList(),
        _ => nodes.Cast<SagaNode>().ToList(),
    };

    private static Node ModelValidate(string className, OrderedDictionary<string, object?> kwargs) => className switch
    {
        "EpisodicNode" => EpisodicNode.ModelValidate(kwargs),
        "EntityNode" => EntityNode.ModelValidate(kwargs),
        "CommunityNode" => CommunityNode.ModelValidate(kwargs),
        "SagaNode" => SagaNode.ModelValidate(kwargs),
        _ => throw new InvalidOperationException(className),
    };

    // The keyword arguments of base_kwargs in the recorder, with the attributes and the episode metadata of its copies section.
    private static OrderedDictionary<string, object?> BaseKwargs(string className)
    {
        var t0 = PyDateTime.FromDateTimeOffset(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var t1 = PyDateTime.FromDateTimeOffset(new DateTimeOffset(2026, 2, 1, 12, 30, 15, TimeSpan.Zero).AddTicks(1234560));
        var d = new OrderedDictionary<string, object?>
        {
            ["uuid"] = "u-1",
            ["name"] = "Alice",
            ["group_id"] = "g-1",
            ["labels"] = new List<object?> { "Person" },
            ["created_at"] = t0,
        };
        switch (className)
        {
            case "EpisodicNode":
                d["source"] = "text";
                d["source_description"] = "desc";
                d["content"] = "user: 你好 😀";
                d["valid_at"] = t0;
                d["entity_edges"] = new List<object?> { "e1" };
                d["episode_metadata"] = new OrderedDictionary<string, object?>
                {
                    ["n"] = new OrderedDictionary<string, object?> { ["k"] = new List<object?> { 1L } },
                };
                break;
            case "EntityNode":
                d["name_embedding"] = new List<object?> { 0.5, 1.0 };
                d["summary"] = "sum";
                d["attributes"] = new OrderedDictionary<string, object?>
                {
                    ["nested"] = new OrderedDictionary<string, object?> { ["k"] = new List<object?> { 1L } },
                    ["n"] = 1L,
                };
                break;
            case "CommunityNode":
                d["name_embedding"] = new List<object?> { 0.5 };
                d["summary"] = "sum";
                break;
            default:
                d["summary"] = "sum";
                d["first_episode_uuid"] = "e1";
                d["last_episode_uuid"] = "e2";
                d["last_summarized_at"] = t0;
                d["last_summarized_episode_valid_at"] = t1;
                break;
        }

        return d;
    }

    private static object? Field(Node node, string field) => (node, field) switch
    {
        (_, "labels") => node.Labels,
        (EpisodicNode n, "entity_edges") => n.EntityEdges,
        (EpisodicNode n, "episode_metadata") => n.EpisodeMetadata,
        (EntityNode n, "name_embedding") => n.NameEmbedding,
        (EntityNode n, "attributes") => n.Attributes,
        (CommunityNode n, "name_embedding") => n.NameEmbedding,
        _ => throw new InvalidOperationException(field),
    };

    // setattr(node, field, type(value)(value)): a new value of the same type, assigned with validate_assignment.
    private static object Assign(Node node, string field)
    {
        switch (node, field)
        {
            case (_, "labels"):
                List<string> labels = [.. node.Labels];
                node.Labels = labels;
                return labels;
            case (EpisodicNode n, "entity_edges"):
                List<string> edges = [.. n.EntityEdges];
                n.EntityEdges = edges;
                return edges;
            case (EpisodicNode n, "episode_metadata"):
                var metadata = new OrderedDictionary<string, object?>(n.EpisodeMetadata!);
                n.EpisodeMetadata = metadata;
                return metadata;
            case (EntityNode n, "name_embedding"):
                List<double> embedding = [.. n.NameEmbedding!];
                n.NameEmbedding = embedding;
                return embedding;
            case (EntityNode n, "attributes"):
                var attributes = new OrderedDictionary<string, object?>(n.Attributes);
                n.Attributes = attributes;
                return attributes;
            case (CommunityNode n, "name_embedding"):
                List<double> communityEmbedding = [.. n.NameEmbedding!];
                n.NameEmbedding = communityEmbedding;
                return communityEmbedding;
            default:
                throw new InvalidOperationException(field);
        }
    }

    private static JsonObject DumpNode(Node node, OrderedDictionary<string, object?>? kwargs = null)
    {
        var data = node switch
        {
            EpisodicNode n => n.ModelDump(),
            EntityNode n => n.ModelDump(),
            CommunityNode n => n.ModelDump(),
            SagaNode n => n.ModelDump(),
            _ => throw new InvalidOperationException(node.PythonName),
        };
        if (kwargs is not null && !kwargs.ContainsKey("uuid"))
        {
            data["uuid"] = "<random>";
        }

        if (kwargs is not null && !kwargs.ContainsKey("created_at"))
        {
            data["created_at"] = "<now>";
        }

        return new JsonObject { ["class"] = node.PythonName, ["dump"] = Encode(data) };
    }

    // A graph operations interface can return None, and Python returns it as it is (the else branch of get_by_uuid and get_by_uuids).
    private static JsonNode DumpResult(object? result) => result switch
    {
        null => Encode(null),
        Node node => DumpNode(node),
        System.Collections.IEnumerable nodes => new JsonArray([.. nodes.Cast<Node>().Select(n => (JsonNode?)DumpNode(n))]),
        _ => throw new InvalidOperationException(result.GetType().Name),
    };

    private static JsonArray DumpNodes<T>(List<T> nodes)
        where T : Node => [.. nodes.Select(n => (JsonNode?)DumpNode(n))];

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
        var expectedType = type switch
        {
            "ValidationError" => typeof(ValidationException),
            "KeyError" => typeof(KeyNotFoundException),
            "ValueError" => typeof(ArgumentException),
            "NodeNotFoundError" => typeof(NodeNotFoundException),
            "NodeLabelValidationError" => typeof(NodeLabelValidationException),
            "NotImplementedError" => typeof(NotImplementedException),
            "AttributeError" => typeof(MissingMemberException),
            "TypeError" => typeof(InvalidCastException),
            _ => throw new InvalidOperationException(type),
        };
        Assert.True(expectedType == error.GetType(), $"expected {type} but got {error}");

        // The text of a KeyError is the Python repr of the key, and a .NET message has other words. A TypeError of a typed
        // read names the C# type.
        if (type is not ("KeyError" or "TypeError"))
        {
            // PORT: PyCompat writes the text of pydantic up to the type of each error. It leaves out the input value, the
            // input type, and the link of each error, so the test removes them from the Python text.
            var python = PydanticInput().Replace(expected.GetProperty("text").GetString()!, "]");
            Assert.Equal(python, error.Message);
        }
    }

    private static void AssertCalls(JsonElement expected, List<JsonObject> actual)
    {
        var list = expected.EnumerateArray().ToList();
        Assert.True(list.Count == actual.Count, $"expected {list.Count} calls but got {actual.Count}: {string.Join(" | ", actual.Select(a => a.ToJsonString()))}");
        for (var i = 0; i < list.Count; i++)
        {
            var e = JsonNode.Parse(list[i].GetRawText())!.AsObject();
            if (e["query"] is JsonValue q && q.TryGetValue<int>(out var id))
            {
                e["query"] = Queries[id];
            }

            Assert.True(JsonNode.DeepEquals(e, actual[i]), $"call {i}: expected {e.ToJsonString()} but got {actual[i].ToJsonString()}");
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
        Node n => new JsonObject { ["type"] = "node", ["class"] = n.PythonName, ["uuid"] = n.Uuid },
        Type t => new JsonObject { ["type"] = "class", ["value"] = t.Name },
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
        "int" => e.GetProperty("value").GetInt64(),
        "float" => e.GetProperty("value").GetDouble(),
        "str" => e.GetProperty("value").GetString(),
        "datetime" => PyDateTime.FromIsoFormat(e.GetProperty("value").GetString()!),
        "enum" => PyEnum.FromValue<EpisodeType>(e.GetProperty("value").GetString()!),
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
        var path = Path.Combine(AppContext.BaseDirectory, "values", "nodes.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    [GeneratedRegex(@", input_value=.*?, input_type=\w+\]\n    For further information visit \S+", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex PydanticInput();

    [GeneratedRegex(@" in [-0-9.e+]+ ms$", RegexOptions.CultureInvariant)]
    private static partial Regex Elapsed();

    private static JsonObject Call(string via, string query, OrderedDictionary<string, object?> parameters) => new()
    {
        ["via"] = via,
        ["query"] = query,
        ["keys"] = new JsonArray([.. parameters.Keys.Select(k => (JsonNode?)k)]),
        ["values"] = new JsonArray([.. parameters.Values.Select(Encode)]),
    };

    private static JsonObject Named(string via, string name, params object?[] args) => new()
    {
        ["via"] = via,
        ["name"] = name,
        ["args"] = new JsonArray([.. args.Select(Encode)]),
    };

    private sealed class FakeDriver : GraphDriver
    {
        private readonly List<JsonObject> calls;
        private readonly List<OrderedDictionary<string, object?>> records;

        internal FakeDriver(List<JsonObject> calls, GraphProvider provider, List<OrderedDictionary<string, object?>> records)
        {
            this.calls = calls;
            this.records = records;
            Provider = provider;
        }

        internal override string PythonName => "FakeDriver";

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(Call("executor", cypherQuery, parameters));
            return (records, null, null);
        }

        internal override GraphDriverSession Session(string? database = null)
        {
            calls.Add(new JsonObject { ["via"] = "session_open" });
            return new FakeSession(calls);
        }

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("not used");

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");

        internal override GraphDriver Clone(string database)
        {
            calls.Add(new JsonObject { ["via"] = "clone" });
            return this;
        }
    }

    private sealed class FakeSession(List<JsonObject> calls) : GraphDriverSession
    {
        public override async ValueTask DisposeAsync()
        {
            await Task.Yield();
            calls.Add(new JsonObject { ["via"] = "session_exit" });
        }

        internal override async Task RunAsync(string query, OrderedDictionary<string, object?> parameters, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(Call("session", query, parameters));
        }

        internal override async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(new JsonObject { ["via"] = "session_close" });
        }

        internal override Task ExecuteWriteAsync(Func<Transaction, Task> func, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("not used");
    }

    private sealed class FakeEmbedder(List<JsonObject> calls, string mode) : EmbedderClient
    {
        internal override async Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(Named("embedder", "create", inputData));
            return [(double)Py.Len(inputData[0]), 0.5];
        }

        internal override async Task<List<List<double>>> CreateBatchAsync(List<string> inputDataList, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add(Named("embedder", "create_batch", inputDataList));
            List<List<double>> result = [.. inputDataList.Select(text => new List<double> { Py.Len(text), 1.0 })];
            return mode switch
            {
                "short" => result[..^1],
                "long" => [.. result, [9.0]],
                _ => result,
            };
        }
    }

    private sealed record RaisingOps : GraphOperationsInterface;

    private sealed record ReturningOps(List<JsonObject> Calls) : GraphOperationsInterface
    {
        internal object? Value { get; set; }

        internal override Task NodeDeleteAsync(Node node, GraphDriver driver, CancellationToken cancellationToken = default) => Done("node_delete", node);

        internal override Task NodeDeleteByGroupIdAsync(Type? cls, GraphDriver driver, string groupId, long batchSize = 100, CancellationToken cancellationToken = default) =>
            Done("node_delete_by_group_id", cls, groupId, batchSize);

        internal override Task NodeDeleteByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, string? groupId = null, long batchSize = 100, CancellationToken cancellationToken = default) =>
            Done("node_delete_by_uuids", cls, uuids, groupId, batchSize);

        internal override Task EpisodicNodeSaveAsync(EpisodicNode node, GraphDriver driver, CancellationToken cancellationToken = default) => Done("episodic_node_save", node);

        internal override Task<EpisodicNode> EpisodicNodeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
            Get<EpisodicNode>("episodic_node_get_by_uuid", cls, uuid);

        internal override Task<List<EpisodicNode>> EpisodicNodeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default) =>
            Get<List<EpisodicNode>>("episodic_node_get_by_uuids", cls, uuids);

        internal override Task<List<EpisodicNode>> EpisodicNodeGetByGroupIdsAsync(Type? cls, GraphDriver driver, List<string> groupIds, long? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default) =>
            Get<List<EpisodicNode>>("episodic_node_get_by_group_ids", cls, groupIds, limit, uuidCursor);

        internal override Task<List<EpisodicNode>> EpisodicNodeGetByEntityNodeUuidAsync(Type? cls, GraphDriver driver, string entityNodeUuid, CancellationToken cancellationToken = default) =>
            Get<List<EpisodicNode>>("episodic_node_get_by_entity_node_uuid", cls, entityNodeUuid);

        internal override Task NodeLoadEmbeddingsAsync(EntityNode node, GraphDriver driver, CancellationToken cancellationToken = default) => Done("node_load_embeddings", node);

        internal override Task NodeSaveAsync(EntityNode node, GraphDriver driver, CancellationToken cancellationToken = default) => Done("node_save", node);

        internal override Task<EntityNode> NodeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
            Get<EntityNode>("node_get_by_uuid", cls, uuid);

        internal override Task<List<EntityNode>> NodeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, string? groupId = null, CancellationToken cancellationToken = default) =>
            Get<List<EntityNode>>("node_get_by_uuids", cls, uuids, groupId);

        internal override Task<List<EntityNode>> NodeGetByNamesAsync(Type? cls, GraphDriver driver, List<string> names, List<string> groupIds, CancellationToken cancellationToken = default) =>
            Get<List<EntityNode>>("node_get_by_names", cls, names, groupIds);

        internal override Task<List<EntityNode>> NodeGetByAttributeValuesAsync(Type? cls, GraphDriver driver, string groupId, OrderedDictionary<string, object?> attributes, List<string>? nodeLabels = null, long limit = 100, CancellationToken cancellationToken = default) =>
            Get<List<EntityNode>>("node_get_by_attribute_values", cls, groupId, attributes, nodeLabels, limit);

        internal override Task<List<EntityNode>> NodeGetByGroupIdsAsync(Type? cls, GraphDriver driver, List<string> groupIds, long? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default) =>
            Get<List<EntityNode>>("node_get_by_group_ids", cls, groupIds, limit, uuidCursor);

        internal override Task CommunityNodeSaveAsync(CommunityNode node, GraphDriver driver, CancellationToken cancellationToken = default) => Done("community_node_save", node);

        internal override Task CommunityNodeLoadNameEmbeddingAsync(CommunityNode node, GraphDriver driver, CancellationToken cancellationToken = default) =>
            Done("community_node_load_name_embedding", node);

        internal override Task<CommunityNode> CommunityNodeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
            Get<CommunityNode>("community_node_get_by_uuid", cls, uuid);

        internal override Task<List<CommunityNode>> CommunityNodeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default) =>
            Get<List<CommunityNode>>("community_node_get_by_uuids", cls, uuids);

        internal override Task<List<CommunityNode>> CommunityNodeGetByGroupIdsAsync(Type? cls, GraphDriver driver, List<string> groupIds, long? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default) =>
            Get<List<CommunityNode>>("community_node_get_by_group_ids", cls, groupIds, limit, uuidCursor);

        internal override Task SagaNodeSaveAsync(SagaNode node, GraphDriver driver, CancellationToken cancellationToken = default) => Done("saga_node_save", node);

        internal override Task SagaNodeDeleteAsync(SagaNode node, GraphDriver driver, CancellationToken cancellationToken = default) => Done("saga_node_delete", node);

        internal override Task<SagaNode> SagaNodeGetByUuidAsync(Type? cls, GraphDriver driver, string uuid, CancellationToken cancellationToken = default) =>
            Get<SagaNode>("saga_node_get_by_uuid", cls, uuid);

        internal override Task<List<SagaNode>> SagaNodeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default) =>
            Get<List<SagaNode>>("saga_node_get_by_uuids", cls, uuids);

        internal override Task<List<SagaNode>> SagaNodeGetByGroupIdsAsync(Type? cls, GraphDriver driver, List<string> groupIds, long? limit = null, string? uuidCursor = null, CancellationToken cancellationToken = default) =>
            Get<List<SagaNode>>("saga_node_get_by_group_ids", cls, groupIds, limit, uuidCursor);

        private async Task Done(string name, params object?[] args)
        {
            await Task.Yield();
            Calls.Add(Named("ops", name, args));
        }

        private async Task<T> Get<T>(string name, params object?[] args)
        {
            await Task.Yield();
            Calls.Add(Named("ops", name, args));
            return (T)Value!;
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

    // A log line of the Nodes logger goes into the list of the calls, so the order of a log line and a call is checked.
    private sealed class CaptureProvider(List<JsonObject> calls) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(categoryName == typeof(Nodes).FullName ? calls : []);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<JsonObject> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                lines.Add(new JsonObject
                {
                    ["via"] = "log",
                    ["level"] = logLevel == LogLevel.Debug ? "DEBUG" : logLevel.ToString().ToUpperInvariant(),
                    ["message"] = Elapsed().Replace(formatter(state, exception), " in <elapsed> ms"),
                });
        }
    }
}
