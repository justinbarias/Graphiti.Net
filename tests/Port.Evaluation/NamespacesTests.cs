// Evaluation test for graphiti_core/namespaces/nodes.py and edges.py (port/RULES.md, R23, R25).
// The recording is values/namespaces.json, which port/tools/record_namespaces.py wrote from Python.
// The fake driver is the network boundary: it records each query, and its operations are the real Neo4j operations.
// Each public method of the nine namespace classes is called with each argument at its default, with each optional
// argument given, and with and without a transaction. The fake embedder is a pure function of its text.
// Each fake task method yields first and then records (rule R25).
namespace Graphiti.Port.Evaluation;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Driver.Operations;
using Graphiti.Core.Embedder;
using Graphiti.Core.Namespaces;
using Graphiti.Neo4j.Operations;
using Graphiti.PyCompat;

public class NamespacesTests
{
    private const string FixedTime = "2024-01-02T03:04:05+00:00";

    private static readonly List<JsonElement> Cases = Load();

    private static readonly Dictionary<string, Type> NamespaceTypes = new()
    {
        ["node.entity"] = typeof(EntityNodeNamespace),
        ["node.episode"] = typeof(EpisodeNodeNamespace),
        ["node.community"] = typeof(CommunityNodeNamespace),
        ["node.saga"] = typeof(SagaNodeNamespace),
        ["edge.entity"] = typeof(EntityEdgeNamespace),
        ["edge.episodic"] = typeof(EpisodicEdgeNamespace),
        ["edge.community"] = typeof(CommunityEdgeNamespace),
        ["edge.has_episode"] = typeof(HasEpisodeEdgeNamespace),
        ["edge.next_episode"] = typeof(NextEpisodeEdgeNamespace),
    };

    public static TheoryData<int> NodeNamespaceCases() => Indexes("node_namespace");

    public static TheoryData<int> EdgeNamespaceCases() => Indexes("edge_namespace");

    public static TheoryData<int> CallCases() => Indexes(null);

    // The first call of each method that reaches the operations class without an error.
    public static TheoryData<int> TokenCases()
    {
        var data = new TheoryData<int>();
        var seen = new HashSet<string>();
        for (var i = 0; i < Cases.Count; i++)
        {
            if (What(Cases[i]) is not null)
            {
                continue;
            }

            var output = Cases[i].GetProperty("output");
            if (output.GetProperty("error").ValueKind == JsonValueKind.Null
                && output.GetProperty("ops").GetArrayLength() > 0
                && seen.Add(Key(Cases[i])))
            {
                data.Add(i);
            }
        }

        return data;
    }

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

    private static string Key(JsonElement c)
    {
        var input = c.GetProperty("input");
        var ns = input.TryGetProperty("ns", out var n) ? n.GetString() : "node.entity";
        return ns + "." + input.GetProperty("method").GetString();
    }

    [Fact]
    public void The_recording_holds_each_kind_of_driver_and_each_method_of_each_namespace()
    {
        // Each one of the 16 sets of node operations and each one of the 32 sets of edge operations, for 2 driver names.
        Assert.Equal(16 * 2, Cases.Count(c => What(c) == "node_namespace"));
        Assert.Equal(32 * 2, Cases.Count(c => What(c) == "edge_namespace"));

        // Each method of each namespace class has a case. The methods come from the C# classes, so a method that is added
        // there without a case fails here.
        var recorded = Cases.Where(c => What(c) is null).Select(Key).Distinct().Order().ToList();
        var declared = new List<string>();
        foreach (var (ns, type) in NamespaceTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                declared.Add(ns + "." + Regex.Replace(method.Name[..^"Async".Length], "(?<!^)(?=[A-Z])", "_").ToLowerInvariant());
            }
        }

        Assert.Equal(declared.Order().ToList(), recorded);
        Assert.Equal(75, recorded.Count);
        Assert.Equal(75, TokenCases().Count);
    }

    [Theory]
    [MemberData(nameof(NodeNamespaceCases))]
    public void NodeNamespace_sets_a_sub_namespace_for_each_operations_class_of_the_driver(int index)
    {
        var input = Cases[index].GetProperty("input");
        var present = input.GetProperty("present").EnumerateArray().Select(name => name.GetString()!).ToArray();
        var driver = new FakeDriver(input.GetProperty("driver").GetString()!, []) { Nodes = present };
        var namespaces = new NodeNamespace(driver, new FakeEmbedder([]));
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
        var present = input.GetProperty("present").EnumerateArray().Select(name => name.GetString()!).ToArray();
        var driver = new FakeDriver(input.GetProperty("driver").GetString()!, []) { Edges = present };
        var namespaces = new EdgeNamespace(driver, new FakeEmbedder([]));
        var output = Cases[index].GetProperty("output");
        Check(output, "entity", () => namespaces.Entity);
        Check(output, "episodic", () => namespaces.Episodic);
        Check(output, "community", () => namespaces.Community);
        Check(output, "has_episode", () => namespaces.HasEpisode);
        Check(output, "next_episode", () => namespaces.NextEpisode);
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(CallCases))]
    public async Task Each_namespace_method_reaches_the_executor_as_in_Python(int index)
    {
        var input = Cases[index].GetProperty("input");
        var output = Cases[index].GetProperty("output");
        var ns = input.TryGetProperty("ns", out var nsElement) ? nsElement.GetString()! : "node.entity";
        var method = input.GetProperty("method").GetString()!;
        var token = TestContext.Current.CancellationToken;

        var run = await Run(ns, method, input, token);

        var expectedError = output.GetProperty("error");
        if (expectedError.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(run.Error);
        }
        else
        {
            Assert.NotNull(run.Error);
            Assert.Equal(
                expectedError.GetProperty("type").GetString() switch
                {
                    "NodeNotFoundError" => typeof(NodeNotFoundException),
                    "EdgeNotFoundError" => typeof(EdgeNotFoundException),
                    "KeyError" => typeof(KeyNotFoundException),
                    "ValidationError" => typeof(ValidationException),
                    "ValueError" => typeof(ArgumentException),
                    "NotImplementedError" => typeof(NotImplementedException),
                    var other => throw new InvalidOperationException(other),
                },
                run.Error.GetType());

            // The message of a ported exception class is the message of Python.
            if (run.Error is NodeNotFoundException or EdgeNotFoundException)
            {
                Assert.Equal(expectedError.GetProperty("text").GetString(), run.Error.Message);
            }
        }

        var expectedOps = output.GetProperty("ops").EnumerateArray().ToList();
        Assert.Equal(expectedOps.Count, run.Driver.OpsSeen.Count);
        for (var i = 0; i < expectedOps.Count; i++)
        {
            AssertSame(expectedOps[i], run.Driver.OpsSeen[i]);
        }

        var calls = run.Driver.Calls.Concat(run.Transaction?.Calls ?? []).ToList();
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

        AssertSame(output.GetProperty("result"), EncodeResult(run.Result));

        if (output.TryGetProperty("same", out var same))
        {
            Assert.Equal(same.GetBoolean(), ReferenceEquals(run.Result, run.Items[0]));
        }

        var embedderCalls = output.GetProperty("embedder").EnumerateArray().ToList();
        Assert.Equal(embedderCalls.Count, run.Embedder.Calls.Count);
        for (var i = 0; i < embedderCalls.Count; i++)
        {
            AssertSame(embedderCalls[i], run.Embedder.Calls[i]);
        }

        if (output.TryGetProperty("embeddings", out var embeddings))
        {
            var expected = embeddings.EnumerateArray().ToList();
            Assert.Equal(expected.Count, run.Items.Count);
            for (var i = 0; i < run.Items.Count; i++)
            {
                AssertSame(
                    expected[i],
                    Encode(run.Items[i] switch
                    {
                        EntityNode node => node.NameEmbedding,
                        CommunityNode node => node.NameEmbedding,
                        EntityEdge edge => edge.FactEmbedding,
                        _ => throw new InvalidOperationException(),
                    }));
            }
        }
    }

    // A canceled token has to reach each fake that the call reaches: the executor, the transaction, the operations
    // class, and the embedder. A fake that is reached with another token means that a namespace dropped the token.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(TokenCases))]
    public async Task Each_namespace_method_passes_its_canceled_token_to_each_fake_that_it_reaches(int index)
    {
        var input = Cases[index].GetProperty("input");
        var output = Cases[index].GetProperty("output");
        var ns = input.TryGetProperty("ns", out var nsElement) ? nsElement.GetString()! : "node.entity";
        var method = input.GetProperty("method").GetString()!;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();

        var run = await Run(ns, method, input, source.Token);

        Assert.Null(run.Error);
        var expectedCount = output.GetProperty("calls").GetArrayLength()
            + output.GetProperty("ops").GetArrayLength()
            + output.GetProperty("embedder").GetArrayLength();
        Assert.Equal(expectedCount, run.Tokens.Count);
        Assert.All(run.Tokens, seen => Assert.Equal(source.Token, seen));
    }

    private sealed record Outcome(
        FakeDriver Driver,
        FakeTransaction? Transaction,
        FakeEmbedder Embedder,
        List<object> Items,
        object? Result,
        Exception? Error,
        List<CancellationToken> Tokens);

    private static async Task<Outcome> Run(string ns, string method, JsonElement input, CancellationToken token)
    {
        var records = input.TryGetProperty("records", out var recordsElement)
            ? recordsElement.EnumerateArray().Select(r => (OrderedDictionary<string, object?>)Read(r)!).ToList()
            : [];
        var tokens = new List<CancellationToken>();
        var driver = new FakeDriver("FakeDriver", records, tokens)
        {
            Nodes = ["entity", "episode", "community", "saga"],
            Edges = ["entity", "episodic", "community", "has_episode", "next_episode"],
        };
        var embedder = new FakeEmbedder(tokens);
        var transaction = input.TryGetProperty("use_tx", out var useTx) && useTx.GetBoolean() ? new FakeTransaction(tokens) : null;
        var kind = ns switch
        {
            "node.entity" => "entity_node",
            "node.episode" => "episode_node",
            "node.community" => "community_node",
            "node.saga" => "saga_node",
            _ => ns.Replace("edge.", string.Empty, StringComparison.Ordinal) + "_edge",
        };
        var items = ItemSpecs(input, method).Select(spec => BuildItem(kind, spec)).ToList();

        object? result = null;
        Exception? error = null;
        try
        {
            result = await Dispatch(
                ns + "." + method,
                input,
                new NodeNamespace(driver, embedder),
                new EdgeNamespace(driver, embedder),
                items,
                transaction,
                token);
        }
        catch (Exception e) when (e is NodeNotFoundException or EdgeNotFoundException or KeyNotFoundException or ValidationException or ArgumentException or NotImplementedException)
        {
            error = e;
        }

        return new Outcome(driver, transaction, embedder, items, result, error, tokens);
    }

    // The specs of the models that a case gives to the call, as record_namespaces.py reads them.
    private static List<JsonElement> ItemSpecs(JsonElement input, string method)
    {
        if (input.TryGetProperty("items", out var items))
        {
            return [.. items.EnumerateArray()];
        }

        if (method is "save_bulk" or "load_embeddings_bulk")
        {
            return input.TryGetProperty("uuids", out var uuids)
                ? [.. uuids.EnumerateArray().Select(uuid => Spec(("uuid", uuid.GetString()!)))]
                : [];
        }

        if (method is "delete" or "load_embeddings" or "save" && input.TryGetProperty("uuid", out var single))
        {
            return [Spec(("uuid", single.GetString()!))];
        }

        return [];
    }

    private static JsonElement Spec((string Key, string Value) field) =>
        JsonDocument.Parse(new JsonObject { [field.Key] = field.Value }.ToJsonString()).RootElement.Clone();

    private static bool Has(JsonElement input, string key) =>
        key == "tx" ? !input.TryGetProperty("omit_tx", out _) : input.TryGetProperty(key, out _);

    private static string Str(JsonElement input, string key) => input.GetProperty(key).GetString()!;

    private static string? NullableStr(JsonElement input, string key) => input.GetProperty(key).GetString();

    private static long Long(JsonElement input, string key) => input.GetProperty(key).GetInt64();

    private static long? NullableLong(JsonElement input, string key) =>
        input.GetProperty(key).ValueKind == JsonValueKind.Null ? null : input.GetProperty(key).GetInt64();

    private static List<string> StrList(JsonElement input, string key) =>
        [.. input.GetProperty(key).EnumerateArray().Select(item => item.GetString()!)];

    private static async Task<object?> Wrap(Task task)
    {
        await task;
        return null;
    }

    private static async Task<object?> Wrap<T>(Task<T> task) => await task;

    // The call of each method of each namespace, with the arguments that the case gives and no more.
    private static Task<object?> Dispatch(
        string key,
        JsonElement input,
        NodeNamespace nodes,
        EdgeNamespace edges,
        List<object> items,
        Transaction? tx,
        CancellationToken token)
    {
        switch (key)
        {
            case "node.entity.save":
                {
                    var sub = nodes.Entity;
                    var arg0 = (EntityNode)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.entity.save_bulk":
                {
                    var sub = nodes.Entity;
                    var arg0 = items.Cast<EntityNode>().ToList();
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveBulkAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.SaveBulkAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.entity.delete":
                {
                    var sub = nodes.Entity;
                    var arg0 = (EntityNode)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.entity.delete_by_group_id":
                {
                    var sub = nodes.Entity;
                    var arg0 = Str(input, "group_id");
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByGroupIdAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByGroupIdAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.DeleteByGroupIdAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.DeleteByGroupIdAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.entity.delete_by_uuids":
                {
                    var sub = nodes.Entity;
                    var arg0 = StrList(input, "uuids");
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByUuidsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.DeleteByUuidsAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.entity.get_by_uuid":
                {
                    var sub = nodes.Entity;
                    var arg0 = Str(input, "uuid");
                    return Wrap(sub.GetByUuidAsync(arg0, cancellationToken: token));
                }

            case "node.entity.get_by_uuids":
                {
                    var sub = nodes.Entity;
                    var arg0 = StrList(input, "uuids");
                    return Wrap(sub.GetByUuidsAsync(arg0, cancellationToken: token));
                }

            case "node.entity.get_by_group_ids":
                {
                    var sub = nodes.Entity;
                    var arg0 = StrList(input, "group_ids");
                    var mask = (Has(input, "limit") ? 1 : 0) | (Has(input, "uuid_cursor") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.GetByGroupIdsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), cancellationToken: token)),
                        2 => Wrap(sub.GetByGroupIdsAsync(arg0, uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        3 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.entity.load_embeddings":
                {
                    var sub = nodes.Entity;
                    var arg0 = (EntityNode)items[0];
                    return Wrap(sub.LoadEmbeddingsAsync(arg0, cancellationToken: token));
                }

            case "node.entity.load_embeddings_bulk":
                {
                    var sub = nodes.Entity;
                    var arg0 = items.Cast<EntityNode>().ToList();
                    var mask = (Has(input, "batch_size") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.LoadEmbeddingsBulkAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.LoadEmbeddingsBulkAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.episode.save":
                {
                    var sub = nodes.Episode;
                    var arg0 = (EpisodicNode)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.episode.save_bulk":
                {
                    var sub = nodes.Episode;
                    var arg0 = items.Cast<EpisodicNode>().ToList();
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveBulkAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.SaveBulkAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.episode.delete":
                {
                    var sub = nodes.Episode;
                    var arg0 = (EpisodicNode)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.episode.delete_by_group_id":
                {
                    var sub = nodes.Episode;
                    var arg0 = Str(input, "group_id");
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByGroupIdAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByGroupIdAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.DeleteByGroupIdAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.DeleteByGroupIdAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.episode.delete_by_uuids":
                {
                    var sub = nodes.Episode;
                    var arg0 = StrList(input, "uuids");
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByUuidsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.DeleteByUuidsAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.episode.get_by_uuid":
                {
                    var sub = nodes.Episode;
                    var arg0 = Str(input, "uuid");
                    return Wrap(sub.GetByUuidAsync(arg0, cancellationToken: token));
                }

            case "node.episode.get_by_uuids":
                {
                    var sub = nodes.Episode;
                    var arg0 = StrList(input, "uuids");
                    return Wrap(sub.GetByUuidsAsync(arg0, cancellationToken: token));
                }

            case "node.episode.get_by_group_ids":
                {
                    var sub = nodes.Episode;
                    var arg0 = StrList(input, "group_ids");
                    var mask = (Has(input, "limit") ? 1 : 0) | (Has(input, "uuid_cursor") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.GetByGroupIdsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), cancellationToken: token)),
                        2 => Wrap(sub.GetByGroupIdsAsync(arg0, uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        3 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.episode.get_by_entity_node_uuid":
                {
                    var sub = nodes.Episode;
                    var arg0 = Str(input, "entity_node_uuid");
                    return Wrap(sub.GetByEntityNodeUuidAsync(arg0, cancellationToken: token));
                }

            case "node.episode.retrieve_episodes":
                {
                    var sub = nodes.Episode;
                    var arg0 = PyDateTime.FromIsoFormat(Str(input, "reference_time"));
                    var mask = (Has(input, "last_n") ? 1 : 0) | (Has(input, "group_ids") ? 2 : 0) | (Has(input, "source") ? 4 : 0) | (Has(input, "saga") ? 8 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.RetrieveEpisodesAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.RetrieveEpisodesAsync(arg0, lastN: Long(input, "last_n"), cancellationToken: token)),
                        2 => Wrap(sub.RetrieveEpisodesAsync(arg0, groupIds: StrList(input, "group_ids"), cancellationToken: token)),
                        3 => Wrap(sub.RetrieveEpisodesAsync(arg0, lastN: Long(input, "last_n"), groupIds: StrList(input, "group_ids"), cancellationToken: token)),
                        4 => Wrap(sub.RetrieveEpisodesAsync(arg0, source: Str(input, "source"), cancellationToken: token)),
                        5 => Wrap(sub.RetrieveEpisodesAsync(arg0, lastN: Long(input, "last_n"), source: Str(input, "source"), cancellationToken: token)),
                        6 => Wrap(sub.RetrieveEpisodesAsync(arg0, groupIds: StrList(input, "group_ids"), source: Str(input, "source"), cancellationToken: token)),
                        7 => Wrap(sub.RetrieveEpisodesAsync(arg0, lastN: Long(input, "last_n"), groupIds: StrList(input, "group_ids"), source: Str(input, "source"), cancellationToken: token)),
                        8 => Wrap(sub.RetrieveEpisodesAsync(arg0, saga: Str(input, "saga"), cancellationToken: token)),
                        9 => Wrap(sub.RetrieveEpisodesAsync(arg0, lastN: Long(input, "last_n"), saga: Str(input, "saga"), cancellationToken: token)),
                        10 => Wrap(sub.RetrieveEpisodesAsync(arg0, groupIds: StrList(input, "group_ids"), saga: Str(input, "saga"), cancellationToken: token)),
                        11 => Wrap(sub.RetrieveEpisodesAsync(arg0, lastN: Long(input, "last_n"), groupIds: StrList(input, "group_ids"), saga: Str(input, "saga"), cancellationToken: token)),
                        12 => Wrap(sub.RetrieveEpisodesAsync(arg0, source: Str(input, "source"), saga: Str(input, "saga"), cancellationToken: token)),
                        13 => Wrap(sub.RetrieveEpisodesAsync(arg0, lastN: Long(input, "last_n"), source: Str(input, "source"), saga: Str(input, "saga"), cancellationToken: token)),
                        14 => Wrap(sub.RetrieveEpisodesAsync(arg0, groupIds: StrList(input, "group_ids"), source: Str(input, "source"), saga: Str(input, "saga"), cancellationToken: token)),
                        15 => Wrap(sub.RetrieveEpisodesAsync(arg0, lastN: Long(input, "last_n"), groupIds: StrList(input, "group_ids"), source: Str(input, "source"), saga: Str(input, "saga"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.community.save":
                {
                    var sub = nodes.Community;
                    var arg0 = (CommunityNode)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.community.save_bulk":
                {
                    var sub = nodes.Community;
                    var arg0 = items.Cast<CommunityNode>().ToList();
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveBulkAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.SaveBulkAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.community.delete":
                {
                    var sub = nodes.Community;
                    var arg0 = (CommunityNode)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.community.delete_by_group_id":
                {
                    var sub = nodes.Community;
                    var arg0 = Str(input, "group_id");
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByGroupIdAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByGroupIdAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.DeleteByGroupIdAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.DeleteByGroupIdAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.community.delete_by_uuids":
                {
                    var sub = nodes.Community;
                    var arg0 = StrList(input, "uuids");
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByUuidsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.DeleteByUuidsAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.community.get_by_uuid":
                {
                    var sub = nodes.Community;
                    var arg0 = Str(input, "uuid");
                    return Wrap(sub.GetByUuidAsync(arg0, cancellationToken: token));
                }

            case "node.community.get_by_uuids":
                {
                    var sub = nodes.Community;
                    var arg0 = StrList(input, "uuids");
                    return Wrap(sub.GetByUuidsAsync(arg0, cancellationToken: token));
                }

            case "node.community.get_by_group_ids":
                {
                    var sub = nodes.Community;
                    var arg0 = StrList(input, "group_ids");
                    var mask = (Has(input, "limit") ? 1 : 0) | (Has(input, "uuid_cursor") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.GetByGroupIdsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), cancellationToken: token)),
                        2 => Wrap(sub.GetByGroupIdsAsync(arg0, uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        3 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.community.load_name_embedding":
                {
                    var sub = nodes.Community;
                    var arg0 = (CommunityNode)items[0];
                    return Wrap(sub.LoadNameEmbeddingAsync(arg0, cancellationToken: token));
                }

            case "node.saga.save":
                {
                    var sub = nodes.Saga;
                    var arg0 = (SagaNode)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.saga.save_bulk":
                {
                    var sub = nodes.Saga;
                    var arg0 = items.Cast<SagaNode>().ToList();
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveBulkAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.SaveBulkAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.saga.delete":
                {
                    var sub = nodes.Saga;
                    var arg0 = (SagaNode)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.saga.delete_by_group_id":
                {
                    var sub = nodes.Saga;
                    var arg0 = Str(input, "group_id");
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByGroupIdAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByGroupIdAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.DeleteByGroupIdAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.DeleteByGroupIdAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.saga.delete_by_uuids":
                {
                    var sub = nodes.Saga;
                    var arg0 = StrList(input, "uuids");
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByUuidsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.DeleteByUuidsAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "node.saga.get_by_uuid":
                {
                    var sub = nodes.Saga;
                    var arg0 = Str(input, "uuid");
                    return Wrap(sub.GetByUuidAsync(arg0, cancellationToken: token));
                }

            case "node.saga.get_by_uuids":
                {
                    var sub = nodes.Saga;
                    var arg0 = StrList(input, "uuids");
                    return Wrap(sub.GetByUuidsAsync(arg0, cancellationToken: token));
                }

            case "node.saga.get_by_group_ids":
                {
                    var sub = nodes.Saga;
                    var arg0 = StrList(input, "group_ids");
                    var mask = (Has(input, "limit") ? 1 : 0) | (Has(input, "uuid_cursor") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.GetByGroupIdsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), cancellationToken: token)),
                        2 => Wrap(sub.GetByGroupIdsAsync(arg0, uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        3 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.entity.save":
                {
                    var sub = edges.Entity;
                    var arg0 = (EntityEdge)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.entity.save_bulk":
                {
                    var sub = edges.Entity;
                    var arg0 = items.Cast<EntityEdge>().ToList();
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveBulkAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.SaveBulkAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.entity.delete":
                {
                    var sub = edges.Entity;
                    var arg0 = (EntityEdge)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.entity.delete_by_uuids":
                {
                    var sub = edges.Entity;
                    var arg0 = StrList(input, "uuids");
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByUuidsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.entity.get_by_uuid":
                {
                    var sub = edges.Entity;
                    var arg0 = Str(input, "uuid");
                    return Wrap(sub.GetByUuidAsync(arg0, cancellationToken: token));
                }

            case "edge.entity.get_by_uuids":
                {
                    var sub = edges.Entity;
                    var arg0 = StrList(input, "uuids");
                    return Wrap(sub.GetByUuidsAsync(arg0, cancellationToken: token));
                }

            case "edge.entity.get_by_group_ids":
                {
                    var sub = edges.Entity;
                    var arg0 = StrList(input, "group_ids");
                    var mask = (Has(input, "limit") ? 1 : 0) | (Has(input, "uuid_cursor") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.GetByGroupIdsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), cancellationToken: token)),
                        2 => Wrap(sub.GetByGroupIdsAsync(arg0, uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        3 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.entity.get_between_nodes":
                {
                    var sub = edges.Entity;
                    var arg0 = Str(input, "source_node_uuid");
                    var arg1 = Str(input, "target_node_uuid");
                    return Wrap(sub.GetBetweenNodesAsync(arg0, arg1, cancellationToken: token));
                }

            case "edge.entity.get_by_node_uuid":
                {
                    var sub = edges.Entity;
                    var arg0 = Str(input, "node_uuid");
                    return Wrap(sub.GetByNodeUuidAsync(arg0, cancellationToken: token));
                }

            case "edge.entity.load_embeddings":
                {
                    var sub = edges.Entity;
                    var arg0 = (EntityEdge)items[0];
                    return Wrap(sub.LoadEmbeddingsAsync(arg0, cancellationToken: token));
                }

            case "edge.entity.load_embeddings_bulk":
                {
                    var sub = edges.Entity;
                    var arg0 = items.Cast<EntityEdge>().ToList();
                    var mask = (Has(input, "batch_size") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.LoadEmbeddingsBulkAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.LoadEmbeddingsBulkAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.episodic.save":
                {
                    var sub = edges.Episodic;
                    var arg0 = (EpisodicEdge)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.episodic.save_bulk":
                {
                    var sub = edges.Episodic;
                    var arg0 = items.Cast<EpisodicEdge>().ToList();
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveBulkAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.SaveBulkAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.episodic.delete":
                {
                    var sub = edges.Episodic;
                    var arg0 = (EpisodicEdge)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.episodic.delete_by_uuids":
                {
                    var sub = edges.Episodic;
                    var arg0 = StrList(input, "uuids");
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByUuidsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.episodic.get_by_uuid":
                {
                    var sub = edges.Episodic;
                    var arg0 = Str(input, "uuid");
                    return Wrap(sub.GetByUuidAsync(arg0, cancellationToken: token));
                }

            case "edge.episodic.get_by_uuids":
                {
                    var sub = edges.Episodic;
                    var arg0 = StrList(input, "uuids");
                    return Wrap(sub.GetByUuidsAsync(arg0, cancellationToken: token));
                }

            case "edge.episodic.get_by_group_ids":
                {
                    var sub = edges.Episodic;
                    var arg0 = StrList(input, "group_ids");
                    var mask = (Has(input, "limit") ? 1 : 0) | (Has(input, "uuid_cursor") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.GetByGroupIdsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), cancellationToken: token)),
                        2 => Wrap(sub.GetByGroupIdsAsync(arg0, uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        3 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.community.save":
                {
                    var sub = edges.Community;
                    var arg0 = (CommunityEdge)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.community.delete":
                {
                    var sub = edges.Community;
                    var arg0 = (CommunityEdge)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.community.delete_by_uuids":
                {
                    var sub = edges.Community;
                    var arg0 = StrList(input, "uuids");
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByUuidsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.community.get_by_uuid":
                {
                    var sub = edges.Community;
                    var arg0 = Str(input, "uuid");
                    return Wrap(sub.GetByUuidAsync(arg0, cancellationToken: token));
                }

            case "edge.community.get_by_uuids":
                {
                    var sub = edges.Community;
                    var arg0 = StrList(input, "uuids");
                    return Wrap(sub.GetByUuidsAsync(arg0, cancellationToken: token));
                }

            case "edge.community.get_by_group_ids":
                {
                    var sub = edges.Community;
                    var arg0 = StrList(input, "group_ids");
                    var mask = (Has(input, "limit") ? 1 : 0) | (Has(input, "uuid_cursor") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.GetByGroupIdsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), cancellationToken: token)),
                        2 => Wrap(sub.GetByGroupIdsAsync(arg0, uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        3 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.has_episode.save":
                {
                    var sub = edges.HasEpisode;
                    var arg0 = (HasEpisodeEdge)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.has_episode.save_bulk":
                {
                    var sub = edges.HasEpisode;
                    var arg0 = items.Cast<HasEpisodeEdge>().ToList();
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveBulkAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.SaveBulkAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.has_episode.delete":
                {
                    var sub = edges.HasEpisode;
                    var arg0 = (HasEpisodeEdge)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.has_episode.delete_by_uuids":
                {
                    var sub = edges.HasEpisode;
                    var arg0 = StrList(input, "uuids");
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByUuidsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.has_episode.get_by_uuid":
                {
                    var sub = edges.HasEpisode;
                    var arg0 = Str(input, "uuid");
                    return Wrap(sub.GetByUuidAsync(arg0, cancellationToken: token));
                }

            case "edge.has_episode.get_by_uuids":
                {
                    var sub = edges.HasEpisode;
                    var arg0 = StrList(input, "uuids");
                    return Wrap(sub.GetByUuidsAsync(arg0, cancellationToken: token));
                }

            case "edge.has_episode.get_by_group_ids":
                {
                    var sub = edges.HasEpisode;
                    var arg0 = StrList(input, "group_ids");
                    var mask = (Has(input, "limit") ? 1 : 0) | (Has(input, "uuid_cursor") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.GetByGroupIdsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), cancellationToken: token)),
                        2 => Wrap(sub.GetByGroupIdsAsync(arg0, uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        3 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.next_episode.save":
                {
                    var sub = edges.NextEpisode;
                    var arg0 = (NextEpisodeEdge)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.next_episode.save_bulk":
                {
                    var sub = edges.NextEpisode;
                    var arg0 = items.Cast<NextEpisodeEdge>().ToList();
                    var mask = (Has(input, "tx") ? 1 : 0) | (Has(input, "batch_size") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.SaveBulkAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, cancellationToken: token)),
                        2 => Wrap(sub.SaveBulkAsync(arg0, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        3 => Wrap(sub.SaveBulkAsync(arg0, tx: tx, batchSize: Long(input, "batch_size"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.next_episode.delete":
                {
                    var sub = edges.NextEpisode;
                    var arg0 = (NextEpisodeEdge)items[0];
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.next_episode.delete_by_uuids":
                {
                    var sub = edges.NextEpisode;
                    var arg0 = StrList(input, "uuids");
                    var mask = (Has(input, "tx") ? 1 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.DeleteByUuidsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.DeleteByUuidsAsync(arg0, tx: tx, cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            case "edge.next_episode.get_by_uuid":
                {
                    var sub = edges.NextEpisode;
                    var arg0 = Str(input, "uuid");
                    return Wrap(sub.GetByUuidAsync(arg0, cancellationToken: token));
                }

            case "edge.next_episode.get_by_uuids":
                {
                    var sub = edges.NextEpisode;
                    var arg0 = StrList(input, "uuids");
                    return Wrap(sub.GetByUuidsAsync(arg0, cancellationToken: token));
                }

            case "edge.next_episode.get_by_group_ids":
                {
                    var sub = edges.NextEpisode;
                    var arg0 = StrList(input, "group_ids");
                    var mask = (Has(input, "limit") ? 1 : 0) | (Has(input, "uuid_cursor") ? 2 : 0);
                    return mask switch
                    {
                        0 => Wrap(sub.GetByGroupIdsAsync(arg0, cancellationToken: token)),
                        1 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), cancellationToken: token)),
                        2 => Wrap(sub.GetByGroupIdsAsync(arg0, uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        3 => Wrap(sub.GetByGroupIdsAsync(arg0, limit: NullableLong(input, "limit"), uuidCursor: NullableStr(input, "uuid_cursor"), cancellationToken: token)),
                        _ => throw new InvalidOperationException(),
                    };
                }

            default:
                throw new InvalidOperationException("No method " + key);
        }
    }

    private static object BuildItem(string kind, JsonElement spec)
    {
        string Text(string name, string fallback) => spec.TryGetProperty(name, out var v) ? v.GetString()! : fallback;
        PyDateTime Time(string name, string fallback) => PyDateTime.FromIsoFormat(Text(name, fallback));
        PyDateTime? OptionalTime(string name) =>
            spec.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? PyDateTime.FromIsoFormat(v.GetString()!) : null;
        List<double>? Embedding(string name) =>
            spec.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null
                ? [.. v.EnumerateArray().Select(e => e.GetDouble())]
                : null;
        List<string> Texts(string name) =>
            spec.TryGetProperty(name, out var v) ? [.. v.EnumerateArray().Select(e => e.GetString()!)] : [];
        OrderedDictionary<string, object?> Dict(string name) =>
            spec.TryGetProperty(name, out var v) ? ReadObject(v) : new OrderedDictionary<string, object?>();

        switch (kind)
        {
            case "entity_node":
                {
                    var node = new EntityNode { Uuid = Text("uuid", string.Empty), Name = Text("name", "n"), GroupId = Text("group_id", "g"), CreatedAt = Time("created_at", FixedTime) };
                    if (spec.TryGetProperty("labels", out _))
                    {
                        node.Labels = Texts("labels");
                    }

                    node.Summary = Text("summary", string.Empty);
                    node.Attributes = Dict("attributes");
                    node.NameEmbedding = Embedding("name_embedding");
                    return node;
                }

            case "episode_node":
                {
                    var node = new EpisodicNode
                    {
                        Uuid = Text("uuid", string.Empty),
                        Name = Text("name", "n"),
                        GroupId = Text("group_id", "g"),
                        CreatedAt = Time("created_at", FixedTime),
                        Source = EpisodeTypeExtensions.FromStr(Text("source", "text")),
                        SourceDescription = Text("source_description", "sd"),
                        Content = Text("content", "c"),
                        ValidAt = Time("valid_at", FixedTime),
                    };
                    node.EntityEdges = Texts("entity_edges");
                    return node;
                }

            case "community_node":
                {
                    var node = new CommunityNode { Uuid = Text("uuid", string.Empty), Name = Text("name", "n"), GroupId = Text("group_id", "g"), CreatedAt = Time("created_at", FixedTime) };
                    node.Summary = Text("summary", string.Empty);
                    node.NameEmbedding = Embedding("name_embedding");
                    return node;
                }

            case "saga_node":
                {
                    var node = new SagaNode { Uuid = Text("uuid", string.Empty), Name = Text("name", "n"), GroupId = Text("group_id", "g"), CreatedAt = Time("created_at", FixedTime) };
                    node.Summary = Text("summary", string.Empty);
                    return node;
                }

            case "entity_edge":
                {
                    var edge = new EntityEdge
                    {
                        Uuid = Text("uuid", string.Empty),
                        GroupId = Text("group_id", "g"),
                        SourceNodeUuid = Text("source_node_uuid", "s"),
                        TargetNodeUuid = Text("target_node_uuid", "t"),
                        CreatedAt = Time("created_at", FixedTime),
                        Name = Text("name", "RELATES_TO"),
                        Fact = Text("fact", "a fact"),
                    };
                    edge.FactEmbedding = Embedding("fact_embedding");
                    edge.Episodes = Texts("episodes");
                    edge.ExpiredAt = OptionalTime("expired_at");
                    edge.ValidAt = OptionalTime("valid_at");
                    edge.InvalidAt = OptionalTime("invalid_at");
                    edge.Attributes = Dict("attributes");
                    return edge;
                }

            case "episodic_edge":
                return new EpisodicEdge { Uuid = Text("uuid", string.Empty), GroupId = Text("group_id", "g"), SourceNodeUuid = Text("source_node_uuid", "s"), TargetNodeUuid = Text("target_node_uuid", "t"), CreatedAt = Time("created_at", FixedTime) };
            case "community_edge":
                return new CommunityEdge { Uuid = Text("uuid", string.Empty), GroupId = Text("group_id", "g"), SourceNodeUuid = Text("source_node_uuid", "s"), TargetNodeUuid = Text("target_node_uuid", "t"), CreatedAt = Time("created_at", FixedTime) };
            case "has_episode_edge":
                return new HasEpisodeEdge { Uuid = Text("uuid", string.Empty), GroupId = Text("group_id", "g"), SourceNodeUuid = Text("source_node_uuid", "s"), TargetNodeUuid = Text("target_node_uuid", "t"), CreatedAt = Time("created_at", FixedTime) };
            case "next_episode_edge":
                return new NextEpisodeEdge { Uuid = Text("uuid", string.Empty), GroupId = Text("group_id", "g"), SourceNodeUuid = Text("source_node_uuid", "s"), TargetNodeUuid = Text("target_node_uuid", "t"), CreatedAt = Time("created_at", FixedTime) };
            default:
                throw new InvalidOperationException(kind);
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

    // An argument that the namespace gives to an operations class, as record_namespaces.py encodes it.
    private static JsonNode EncodeArg(object? value) => value switch
    {
        Transaction => new JsonObject { ["type"] = "transaction" },
        Node node => new JsonObject { ["type"] = node.GetType().Name, ["uuid"] = node.Uuid },
        Edge edge => new JsonObject { ["type"] = edge.GetType().Name, ["uuid"] = edge.Uuid },
        string => Encode(value),
        System.Collections.IEnumerable items => new JsonObject
        {
            ["type"] = "list",
            ["items"] = new JsonArray(items.Cast<object?>().Select(item => (JsonNode?)EncodeArg(item)).ToArray()),
        },
        _ => Encode(value),
    };

    // What a method returns: a model as its fields in a fixed order, a list of models, or a plain value.
    private static JsonNode EncodeResult(object? value)
    {
        JsonNode Fields(string type, params (string Name, JsonNode Value)[] fields) => new JsonObject
        {
            ["type"] = type,
            ["fields"] = new JsonArray(fields.Select(field => (JsonNode?)new JsonArray(field.Name, field.Value)).ToArray()),
        };

        JsonNode NodeFields(Node node, params (string Name, JsonNode Value)[] more) => Fields(
            node.GetType().Name,
            [("uuid", Encode(node.Uuid)), ("name", Encode(node.Name)), ("group_id", Encode(node.GroupId)), ("labels", Encode(node.Labels)), ("created_at", Encode(node.CreatedAt)), .. more]);

        JsonNode EdgeFields(Edge edge, params (string Name, JsonNode Value)[] more) => Fields(
            edge.GetType().Name,
            [("uuid", Encode(edge.Uuid)), ("group_id", Encode(edge.GroupId)), ("source_node_uuid", Encode(edge.SourceNodeUuid)), ("target_node_uuid", Encode(edge.TargetNodeUuid)), ("created_at", Encode(edge.CreatedAt)), .. more]);

        return value switch
        {
            EntityNode n => NodeFields(n, ("name_embedding", Encode(n.NameEmbedding)), ("summary", Encode(n.Summary)), ("attributes", Encode(n.Attributes))),
            EpisodicNode n => NodeFields(
                n,
                ("source", Encode(Py.EnumValue(n.Source))),
                ("source_description", Encode(n.SourceDescription)),
                ("content", Encode(n.Content)),
                ("valid_at", Encode(n.ValidAt)),
                ("entity_edges", Encode(n.EntityEdges))),
            CommunityNode n => NodeFields(n, ("name_embedding", Encode(n.NameEmbedding)), ("summary", Encode(n.Summary))),
            SagaNode n => NodeFields(n, ("summary", Encode(n.Summary))),
            EntityEdge e => EdgeFields(
                e,
                ("name", Encode(e.Name)),
                ("fact", Encode(e.Fact)),
                ("fact_embedding", Encode(e.FactEmbedding)),
                ("episodes", Encode(e.Episodes)),
                ("expired_at", Encode(e.ExpiredAt)),
                ("valid_at", Encode(e.ValidAt)),
                ("invalid_at", Encode(e.InvalidAt)),
                ("reference_time", Encode(e.ReferenceTime)),
                ("attributes", Encode(e.Attributes))),
            Edge e => EdgeFields(e),
            string or null => Encode(value),
            System.Collections.IEnumerable items => new JsonObject
            {
                ["type"] = "list",
                ["items"] = new JsonArray(items.Cast<object?>().Select(item => (JsonNode?)EncodeResult(item)).ToArray()),
            },
            _ => Encode(value),
        };
    }

    private static JsonNode Encode(object? value) => value switch
    {
        null => new JsonObject { ["type"] = "none" },
        bool b => new JsonObject { ["type"] = "bool", ["value"] = b },
        int or long => new JsonObject { ["type"] = "int", ["value"] = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) },
        double d => new JsonObject { ["type"] = "float", ["value"] = d },
        string s => new JsonObject { ["type"] = "str", ["value"] = s },
        PyDateTime time => new JsonObject { ["type"] = "datetime", ["value"] = time.IsoFormat() },
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
        var path = Path.Combine(AppContext.BaseDirectory, "values", "namespaces.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(c => c.Clone()).ToList();
    }

    private sealed record Call(string Via, string Query, List<string> Keys, List<JsonNode> Values);

    private static Call Note(string via, string query, OrderedDictionary<string, object?> parameters) =>
        new(via, query, [.. parameters.Keys], [.. parameters.Values.Select(Encode)]);

    // The fake embedder is a pure function of its text: the length in code points, and the sum of the code points modulo 997.
    private sealed class FakeEmbedder(List<CancellationToken> tokens) : EmbedderClient
    {
        internal List<JsonNode> Calls { get; } = [];

        internal override async Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            Calls.Add(Encode(inputData));
            var runes = inputData[0].EnumerateRunes().ToList();
            return [runes.Count, runes.Sum(rune => rune.Value) % 997];
        }
    }

    private sealed class FakeDriver(string name, List<OrderedDictionary<string, object?>> records, List<CancellationToken>? tokens = null) : GraphDriver
    {
        private readonly List<CancellationToken> tokenLog = tokens ?? [];

        internal List<Call> Calls { get; } = [];

        internal string[] Nodes { get; set; } = [];

        internal string[] Edges { get; set; } = [];

        internal List<JsonNode> OpsSeen { get; } = [];

        internal override string PythonName => name;

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokenLog.Add(cancellationToken);
            Calls.Add(Note("executor", cypherQuery, parameters));
            return (records, null, null);
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException();

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException();

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        internal override EntityNodeOperations? EntityNodeOps =>
            Nodes.Contains("entity") ? new RecordingEntityNodeOperations(new Neo4jEntityNodeOperations(), OpsSeen, tokenLog) : null;

        internal override EpisodeNodeOperations? EpisodeNodeOps =>
            Nodes.Contains("episode") ? new RecordingEpisodeNodeOperations(new Neo4jEpisodeNodeOperations(), OpsSeen, tokenLog) : null;

        internal override CommunityNodeOperations? CommunityNodeOps =>
            Nodes.Contains("community") ? new RecordingCommunityNodeOperations(new Neo4jCommunityNodeOperations(), OpsSeen, tokenLog) : null;

        internal override SagaNodeOperations? SagaNodeOps =>
            Nodes.Contains("saga") ? new RecordingSagaNodeOperations(new Neo4jSagaNodeOperations(), OpsSeen, tokenLog) : null;

        internal override EntityEdgeOperations? EntityEdgeOps =>
            Edges.Contains("entity") ? new RecordingEntityEdgeOperations(new Neo4jEntityEdgeOperations(), OpsSeen, tokenLog) : null;

        internal override EpisodicEdgeOperations? EpisodicEdgeOps =>
            Edges.Contains("episodic") ? new RecordingEpisodicEdgeOperations(new Neo4jEpisodicEdgeOperations(), OpsSeen, tokenLog) : null;

        internal override CommunityEdgeOperations? CommunityEdgeOps =>
            Edges.Contains("community") ? new RecordingCommunityEdgeOperations(new Neo4jCommunityEdgeOperations(), OpsSeen, tokenLog) : null;

        internal override HasEpisodeEdgeOperations? HasEpisodeEdgeOps =>
            Edges.Contains("has_episode") ? new RecordingHasEpisodeEdgeOperations(new Neo4jHasEpisodeEdgeOperations(), OpsSeen, tokenLog) : null;

        internal override NextEpisodeEdgeOperations? NextEpisodeEdgeOps =>
            Edges.Contains("next_episode") ? new RecordingNextEpisodeEdgeOperations(new Neo4jNextEpisodeEdgeOperations(), OpsSeen, tokenLog) : null;
    }

    // Each operations class is the real Neo4j class behind a wrapper that notes the arguments of each call and then
    // delegates, as the recorder does in Python. The Neo4j class ignores batch_size in load_embeddings_bulk, so only the
    // wrapper shows it.
    private static class RecordingNote
    {
        internal static void Add(List<JsonNode> seen, string method, params (string Name, object? Value)[] arguments) =>
            seen.Add(new JsonObject
            {
                ["method"] = method,
                ["args"] = new JsonArray(arguments.Select(argument => (JsonNode?)new JsonArray(argument.Name, EncodeArg(argument.Value))).ToArray()),
            });
    }

    private sealed class RecordingEntityNodeOperations(EntityNodeOperations inner, List<JsonNode> seen, List<CancellationToken> tokens) : EntityNodeOperations
    {
        internal override async Task SaveAsync(
            QueryExecutor executor,
            EntityNode node,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save", ("node", node), ("tx", tx));
            await inner.SaveAsync(executor, node, tx, cancellationToken);
        }

        internal override async Task SaveBulkAsync(
            QueryExecutor executor,
            List<EntityNode> nodes,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save_bulk", ("nodes", nodes), ("tx", tx), ("batch_size", batchSize));
            await inner.SaveBulkAsync(executor, nodes, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteAsync(
            QueryExecutor executor,
            EntityNode node,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete", ("node", node), ("tx", tx));
            await inner.DeleteAsync(executor, node, tx, cancellationToken);
        }

        internal override async Task DeleteByGroupIdAsync(
            QueryExecutor executor,
            string groupId,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_group_id", ("group_id", groupId), ("tx", tx), ("batch_size", batchSize));
            await inner.DeleteByGroupIdAsync(executor, groupId, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_uuids", ("uuids", uuids), ("tx", tx), ("batch_size", batchSize));
            await inner.DeleteByUuidsAsync(executor, uuids, tx, batchSize, cancellationToken);
        }

        internal override async Task<EntityNode> GetByUuidAsync(
            QueryExecutor executor,
            string uuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuid", ("uuid", uuid));
            return await inner.GetByUuidAsync(executor, uuid, cancellationToken);
        }

        internal override async Task<List<EntityNode>> GetByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuids", ("uuids", uuids));
            return await inner.GetByUuidsAsync(executor, uuids, cancellationToken);
        }

        internal override async Task<List<EntityNode>> GetByGroupIdsAsync(
            QueryExecutor executor,
            List<string> groupIds,
            long? limit = null,
            string? uuidCursor = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_group_ids", ("group_ids", groupIds), ("limit", limit), ("uuid_cursor", uuidCursor));
            return await inner.GetByGroupIdsAsync(executor, groupIds, limit, uuidCursor, cancellationToken);
        }

        internal override async Task LoadEmbeddingsAsync(
            QueryExecutor executor,
            EntityNode node,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "load_embeddings", ("node", node));
            await inner.LoadEmbeddingsAsync(executor, node, cancellationToken);
        }

        internal override async Task LoadEmbeddingsBulkAsync(
            QueryExecutor executor,
            List<EntityNode> nodes,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "load_embeddings_bulk", ("nodes", nodes), ("batch_size", batchSize));
            await inner.LoadEmbeddingsBulkAsync(executor, nodes, batchSize, cancellationToken);
        }
    }

    private sealed class RecordingEpisodeNodeOperations(EpisodeNodeOperations inner, List<JsonNode> seen, List<CancellationToken> tokens) : EpisodeNodeOperations
    {
        internal override async Task SaveAsync(
            QueryExecutor executor,
            EpisodicNode node,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save", ("node", node), ("tx", tx));
            await inner.SaveAsync(executor, node, tx, cancellationToken);
        }

        internal override async Task SaveBulkAsync(
            QueryExecutor executor,
            List<EpisodicNode> nodes,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save_bulk", ("nodes", nodes), ("tx", tx), ("batch_size", batchSize));
            await inner.SaveBulkAsync(executor, nodes, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteAsync(
            QueryExecutor executor,
            EpisodicNode node,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete", ("node", node), ("tx", tx));
            await inner.DeleteAsync(executor, node, tx, cancellationToken);
        }

        internal override async Task DeleteByGroupIdAsync(
            QueryExecutor executor,
            string groupId,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_group_id", ("group_id", groupId), ("tx", tx), ("batch_size", batchSize));
            await inner.DeleteByGroupIdAsync(executor, groupId, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_uuids", ("uuids", uuids), ("tx", tx), ("batch_size", batchSize));
            await inner.DeleteByUuidsAsync(executor, uuids, tx, batchSize, cancellationToken);
        }

        internal override async Task<EpisodicNode> GetByUuidAsync(
            QueryExecutor executor,
            string uuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuid", ("uuid", uuid));
            return await inner.GetByUuidAsync(executor, uuid, cancellationToken);
        }

        internal override async Task<List<EpisodicNode>> GetByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuids", ("uuids", uuids));
            return await inner.GetByUuidsAsync(executor, uuids, cancellationToken);
        }

        internal override async Task<List<EpisodicNode>> GetByGroupIdsAsync(
            QueryExecutor executor,
            List<string> groupIds,
            long? limit = null,
            string? uuidCursor = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_group_ids", ("group_ids", groupIds), ("limit", limit), ("uuid_cursor", uuidCursor));
            return await inner.GetByGroupIdsAsync(executor, groupIds, limit, uuidCursor, cancellationToken);
        }

        internal override async Task<List<EpisodicNode>> GetByEntityNodeUuidAsync(
            QueryExecutor executor,
            string entityNodeUuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_entity_node_uuid", ("entity_node_uuid", entityNodeUuid));
            return await inner.GetByEntityNodeUuidAsync(executor, entityNodeUuid, cancellationToken);
        }

        internal override async Task<List<EpisodicNode>> RetrieveEpisodesAsync(
            QueryExecutor executor,
            PyDateTime referenceTime,
            long lastN = 3,
            List<string>? groupIds = null,
            string? source = null,
            string? saga = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "retrieve_episodes", ("reference_time", referenceTime), ("last_n", lastN), ("group_ids", groupIds), ("source", source), ("saga", saga));
            return await inner.RetrieveEpisodesAsync(executor, referenceTime, lastN, groupIds, source, saga, cancellationToken);
        }
    }

    private sealed class RecordingCommunityNodeOperations(CommunityNodeOperations inner, List<JsonNode> seen, List<CancellationToken> tokens) : CommunityNodeOperations
    {
        internal override async Task SaveAsync(
            QueryExecutor executor,
            CommunityNode node,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save", ("node", node), ("tx", tx));
            await inner.SaveAsync(executor, node, tx, cancellationToken);
        }

        internal override async Task SaveBulkAsync(
            QueryExecutor executor,
            List<CommunityNode> nodes,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save_bulk", ("nodes", nodes), ("tx", tx), ("batch_size", batchSize));
            await inner.SaveBulkAsync(executor, nodes, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteAsync(
            QueryExecutor executor,
            CommunityNode node,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete", ("node", node), ("tx", tx));
            await inner.DeleteAsync(executor, node, tx, cancellationToken);
        }

        internal override async Task DeleteByGroupIdAsync(
            QueryExecutor executor,
            string groupId,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_group_id", ("group_id", groupId), ("tx", tx), ("batch_size", batchSize));
            await inner.DeleteByGroupIdAsync(executor, groupId, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_uuids", ("uuids", uuids), ("tx", tx), ("batch_size", batchSize));
            await inner.DeleteByUuidsAsync(executor, uuids, tx, batchSize, cancellationToken);
        }

        internal override async Task<CommunityNode> GetByUuidAsync(
            QueryExecutor executor,
            string uuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuid", ("uuid", uuid));
            return await inner.GetByUuidAsync(executor, uuid, cancellationToken);
        }

        internal override async Task<List<CommunityNode>> GetByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuids", ("uuids", uuids));
            return await inner.GetByUuidsAsync(executor, uuids, cancellationToken);
        }

        internal override async Task<List<CommunityNode>> GetByGroupIdsAsync(
            QueryExecutor executor,
            List<string> groupIds,
            long? limit = null,
            string? uuidCursor = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_group_ids", ("group_ids", groupIds), ("limit", limit), ("uuid_cursor", uuidCursor));
            return await inner.GetByGroupIdsAsync(executor, groupIds, limit, uuidCursor, cancellationToken);
        }

        internal override async Task LoadNameEmbeddingAsync(
            QueryExecutor executor,
            CommunityNode node,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "load_name_embedding", ("node", node));
            await inner.LoadNameEmbeddingAsync(executor, node, cancellationToken);
        }
    }

    private sealed class RecordingSagaNodeOperations(SagaNodeOperations inner, List<JsonNode> seen, List<CancellationToken> tokens) : SagaNodeOperations
    {
        internal override async Task SaveAsync(
            QueryExecutor executor,
            SagaNode node,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save", ("node", node), ("tx", tx));
            await inner.SaveAsync(executor, node, tx, cancellationToken);
        }

        internal override async Task SaveBulkAsync(
            QueryExecutor executor,
            List<SagaNode> nodes,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save_bulk", ("nodes", nodes), ("tx", tx), ("batch_size", batchSize));
            await inner.SaveBulkAsync(executor, nodes, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteAsync(
            QueryExecutor executor,
            SagaNode node,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete", ("node", node), ("tx", tx));
            await inner.DeleteAsync(executor, node, tx, cancellationToken);
        }

        internal override async Task DeleteByGroupIdAsync(
            QueryExecutor executor,
            string groupId,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_group_id", ("group_id", groupId), ("tx", tx), ("batch_size", batchSize));
            await inner.DeleteByGroupIdAsync(executor, groupId, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_uuids", ("uuids", uuids), ("tx", tx), ("batch_size", batchSize));
            await inner.DeleteByUuidsAsync(executor, uuids, tx, batchSize, cancellationToken);
        }

        internal override async Task<SagaNode> GetByUuidAsync(
            QueryExecutor executor,
            string uuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuid", ("uuid", uuid));
            return await inner.GetByUuidAsync(executor, uuid, cancellationToken);
        }

        internal override async Task<List<SagaNode>> GetByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuids", ("uuids", uuids));
            return await inner.GetByUuidsAsync(executor, uuids, cancellationToken);
        }

        internal override async Task<List<SagaNode>> GetByGroupIdsAsync(
            QueryExecutor executor,
            List<string> groupIds,
            long? limit = null,
            string? uuidCursor = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_group_ids", ("group_ids", groupIds), ("limit", limit), ("uuid_cursor", uuidCursor));
            return await inner.GetByGroupIdsAsync(executor, groupIds, limit, uuidCursor, cancellationToken);
        }
    }

    private sealed class RecordingEntityEdgeOperations(EntityEdgeOperations inner, List<JsonNode> seen, List<CancellationToken> tokens) : EntityEdgeOperations
    {
        internal override async Task SaveAsync(
            QueryExecutor executor,
            EntityEdge edge,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save", ("edge", edge), ("tx", tx));
            await inner.SaveAsync(executor, edge, tx, cancellationToken);
        }

        internal override async Task SaveBulkAsync(
            QueryExecutor executor,
            List<EntityEdge> edges,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save_bulk", ("edges", edges), ("tx", tx), ("batch_size", batchSize));
            await inner.SaveBulkAsync(executor, edges, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteAsync(
            QueryExecutor executor,
            EntityEdge edge,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete", ("edge", edge), ("tx", tx));
            await inner.DeleteAsync(executor, edge, tx, cancellationToken);
        }

        internal override async Task DeleteByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_uuids", ("uuids", uuids), ("tx", tx));
            await inner.DeleteByUuidsAsync(executor, uuids, tx, cancellationToken);
        }

        internal override async Task<EntityEdge> GetByUuidAsync(
            QueryExecutor executor,
            string uuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuid", ("uuid", uuid));
            return await inner.GetByUuidAsync(executor, uuid, cancellationToken);
        }

        internal override async Task<List<EntityEdge>> GetByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuids", ("uuids", uuids));
            return await inner.GetByUuidsAsync(executor, uuids, cancellationToken);
        }

        internal override async Task<List<EntityEdge>> GetByGroupIdsAsync(
            QueryExecutor executor,
            List<string> groupIds,
            long? limit = null,
            string? uuidCursor = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_group_ids", ("group_ids", groupIds), ("limit", limit), ("uuid_cursor", uuidCursor));
            return await inner.GetByGroupIdsAsync(executor, groupIds, limit, uuidCursor, cancellationToken);
        }

        internal override async Task<List<EntityEdge>> GetBetweenNodesAsync(
            QueryExecutor executor,
            string sourceNodeUuid,
            string targetNodeUuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_between_nodes", ("source_node_uuid", sourceNodeUuid), ("target_node_uuid", targetNodeUuid));
            return await inner.GetBetweenNodesAsync(executor, sourceNodeUuid, targetNodeUuid, cancellationToken);
        }

        internal override async Task<List<EntityEdge>> GetByNodeUuidAsync(
            QueryExecutor executor,
            string nodeUuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_node_uuid", ("node_uuid", nodeUuid));
            return await inner.GetByNodeUuidAsync(executor, nodeUuid, cancellationToken);
        }

        internal override async Task LoadEmbeddingsAsync(
            QueryExecutor executor,
            EntityEdge edge,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "load_embeddings", ("edge", edge));
            await inner.LoadEmbeddingsAsync(executor, edge, cancellationToken);
        }

        internal override async Task LoadEmbeddingsBulkAsync(
            QueryExecutor executor,
            List<EntityEdge> edges,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "load_embeddings_bulk", ("edges", edges), ("batch_size", batchSize));
            await inner.LoadEmbeddingsBulkAsync(executor, edges, batchSize, cancellationToken);
        }
    }

    private sealed class RecordingEpisodicEdgeOperations(EpisodicEdgeOperations inner, List<JsonNode> seen, List<CancellationToken> tokens) : EpisodicEdgeOperations
    {
        internal override async Task SaveAsync(
            QueryExecutor executor,
            EpisodicEdge edge,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save", ("edge", edge), ("tx", tx));
            await inner.SaveAsync(executor, edge, tx, cancellationToken);
        }

        internal override async Task SaveBulkAsync(
            QueryExecutor executor,
            List<EpisodicEdge> edges,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save_bulk", ("edges", edges), ("tx", tx), ("batch_size", batchSize));
            await inner.SaveBulkAsync(executor, edges, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteAsync(
            QueryExecutor executor,
            EpisodicEdge edge,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete", ("edge", edge), ("tx", tx));
            await inner.DeleteAsync(executor, edge, tx, cancellationToken);
        }

        internal override async Task DeleteByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_uuids", ("uuids", uuids), ("tx", tx));
            await inner.DeleteByUuidsAsync(executor, uuids, tx, cancellationToken);
        }

        internal override async Task<EpisodicEdge> GetByUuidAsync(
            QueryExecutor executor,
            string uuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuid", ("uuid", uuid));
            return await inner.GetByUuidAsync(executor, uuid, cancellationToken);
        }

        internal override async Task<List<EpisodicEdge>> GetByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuids", ("uuids", uuids));
            return await inner.GetByUuidsAsync(executor, uuids, cancellationToken);
        }

        internal override async Task<List<EpisodicEdge>> GetByGroupIdsAsync(
            QueryExecutor executor,
            List<string> groupIds,
            long? limit = null,
            string? uuidCursor = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_group_ids", ("group_ids", groupIds), ("limit", limit), ("uuid_cursor", uuidCursor));
            return await inner.GetByGroupIdsAsync(executor, groupIds, limit, uuidCursor, cancellationToken);
        }
    }

    private sealed class RecordingCommunityEdgeOperations(CommunityEdgeOperations inner, List<JsonNode> seen, List<CancellationToken> tokens) : CommunityEdgeOperations
    {
        internal override async Task SaveAsync(
            QueryExecutor executor,
            CommunityEdge edge,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save", ("edge", edge), ("tx", tx));
            await inner.SaveAsync(executor, edge, tx, cancellationToken);
        }

        internal override async Task DeleteAsync(
            QueryExecutor executor,
            CommunityEdge edge,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete", ("edge", edge), ("tx", tx));
            await inner.DeleteAsync(executor, edge, tx, cancellationToken);
        }

        internal override async Task DeleteByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_uuids", ("uuids", uuids), ("tx", tx));
            await inner.DeleteByUuidsAsync(executor, uuids, tx, cancellationToken);
        }

        internal override async Task<CommunityEdge> GetByUuidAsync(
            QueryExecutor executor,
            string uuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuid", ("uuid", uuid));
            return await inner.GetByUuidAsync(executor, uuid, cancellationToken);
        }

        internal override async Task<List<CommunityEdge>> GetByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuids", ("uuids", uuids));
            return await inner.GetByUuidsAsync(executor, uuids, cancellationToken);
        }

        internal override async Task<List<CommunityEdge>> GetByGroupIdsAsync(
            QueryExecutor executor,
            List<string> groupIds,
            long? limit = null,
            string? uuidCursor = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_group_ids", ("group_ids", groupIds), ("limit", limit), ("uuid_cursor", uuidCursor));
            return await inner.GetByGroupIdsAsync(executor, groupIds, limit, uuidCursor, cancellationToken);
        }
    }

    private sealed class RecordingHasEpisodeEdgeOperations(HasEpisodeEdgeOperations inner, List<JsonNode> seen, List<CancellationToken> tokens) : HasEpisodeEdgeOperations
    {
        internal override async Task SaveAsync(
            QueryExecutor executor,
            HasEpisodeEdge edge,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save", ("edge", edge), ("tx", tx));
            await inner.SaveAsync(executor, edge, tx, cancellationToken);
        }

        internal override async Task SaveBulkAsync(
            QueryExecutor executor,
            List<HasEpisodeEdge> edges,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save_bulk", ("edges", edges), ("tx", tx), ("batch_size", batchSize));
            await inner.SaveBulkAsync(executor, edges, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteAsync(
            QueryExecutor executor,
            HasEpisodeEdge edge,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete", ("edge", edge), ("tx", tx));
            await inner.DeleteAsync(executor, edge, tx, cancellationToken);
        }

        internal override async Task DeleteByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_uuids", ("uuids", uuids), ("tx", tx));
            await inner.DeleteByUuidsAsync(executor, uuids, tx, cancellationToken);
        }

        internal override async Task<HasEpisodeEdge> GetByUuidAsync(
            QueryExecutor executor,
            string uuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuid", ("uuid", uuid));
            return await inner.GetByUuidAsync(executor, uuid, cancellationToken);
        }

        internal override async Task<List<HasEpisodeEdge>> GetByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuids", ("uuids", uuids));
            return await inner.GetByUuidsAsync(executor, uuids, cancellationToken);
        }

        internal override async Task<List<HasEpisodeEdge>> GetByGroupIdsAsync(
            QueryExecutor executor,
            List<string> groupIds,
            long? limit = null,
            string? uuidCursor = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_group_ids", ("group_ids", groupIds), ("limit", limit), ("uuid_cursor", uuidCursor));
            return await inner.GetByGroupIdsAsync(executor, groupIds, limit, uuidCursor, cancellationToken);
        }
    }

    private sealed class RecordingNextEpisodeEdgeOperations(NextEpisodeEdgeOperations inner, List<JsonNode> seen, List<CancellationToken> tokens) : NextEpisodeEdgeOperations
    {
        internal override async Task SaveAsync(
            QueryExecutor executor,
            NextEpisodeEdge edge,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save", ("edge", edge), ("tx", tx));
            await inner.SaveAsync(executor, edge, tx, cancellationToken);
        }

        internal override async Task SaveBulkAsync(
            QueryExecutor executor,
            List<NextEpisodeEdge> edges,
            Transaction? tx = null,
            long batchSize = 100,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "save_bulk", ("edges", edges), ("tx", tx), ("batch_size", batchSize));
            await inner.SaveBulkAsync(executor, edges, tx, batchSize, cancellationToken);
        }

        internal override async Task DeleteAsync(
            QueryExecutor executor,
            NextEpisodeEdge edge,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete", ("edge", edge), ("tx", tx));
            await inner.DeleteAsync(executor, edge, tx, cancellationToken);
        }

        internal override async Task DeleteByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            Transaction? tx = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "delete_by_uuids", ("uuids", uuids), ("tx", tx));
            await inner.DeleteByUuidsAsync(executor, uuids, tx, cancellationToken);
        }

        internal override async Task<NextEpisodeEdge> GetByUuidAsync(
            QueryExecutor executor,
            string uuid,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuid", ("uuid", uuid));
            return await inner.GetByUuidAsync(executor, uuid, cancellationToken);
        }

        internal override async Task<List<NextEpisodeEdge>> GetByUuidsAsync(
            QueryExecutor executor,
            List<string> uuids,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_uuids", ("uuids", uuids));
            return await inner.GetByUuidsAsync(executor, uuids, cancellationToken);
        }

        internal override async Task<List<NextEpisodeEdge>> GetByGroupIdsAsync(
            QueryExecutor executor,
            List<string> groupIds,
            long? limit = null,
            string? uuidCursor = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens.Add(cancellationToken);
            RecordingNote.Add(seen, "get_by_group_ids", ("group_ids", groupIds), ("limit", limit), ("uuid_cursor", uuidCursor));
            return await inner.GetByGroupIdsAsync(executor, groupIds, limit, uuidCursor, cancellationToken);
        }
    }

    private sealed class FakeTransaction(List<CancellationToken>? tokens = null) : Transaction
    {
        internal List<Call> Calls { get; } = [];

        internal override async Task RunAsync(
            string query,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            tokens?.Add(cancellationToken);
            Calls.Add(Note("transaction", query, parameters));
        }
    }
}
