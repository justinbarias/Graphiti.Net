// Evaluation test for graphiti_core/utils/maintenance/node_operations.py (port/RULES.md, R23 and R25).
// values/node_operations.json holds the calls that port/tools/record_node_operations.py recorded from Python: the sync
// helpers with plain arguments, and the async functions with fakes at the network boundary on both sides (an LLM
// client, an embedder, and a driver with a graph operations interface and a search interface). Each fake yields once
// and then records its call. The calls and the log lines are one ordered list. The recorder docstring gives the
// values that both sides fix: the clock, the text of the time of the extraction, the uuid4 texts, and long texts.
namespace Graphiti.Port.Evaluation;

using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.LlmClient;
using Graphiti.Core.Prompts;
using Graphiti.Core.Search;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;
using static Graphiti.Port.Evaluation.MaintenanceHelpersValuesTests;

public partial class NodeOperationsValuesTests
{
    private static readonly OrderedDictionary<string, object?> Values = Load();

    private static readonly List<OrderedDictionary<string, object?>> AsyncCases = List(Values["async_cases"]).Select(Dict).ToList();

    private static readonly Regex Uuid4 = new("[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}", RegexOptions.CultureInvariant);

    private static readonly Regex Ms = new(@" in \d+ ms\z", RegexOptions.CultureInvariant);

    // PORT: PyCompat writes the text of pydantic up to the type of each error. It leaves out the input value, the input
    // type, and the link of each error, so the test removes them from the Python text (R25).
    private static readonly Regex PydanticInput = new(@", input_value=.*?, input_type=\w+\]\n    For further information visit \S+", RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly string[] ExtraKeys = ["node_uuid", "group_id", "identity_type", "extracted_node_count", "group_count"];

    private static MaintenanceFixedClock Clock => new(PyDateTime.FromIsoFormat(Str(Values["fixed_now"])).ToDateTimeOffset());

    [Fact]
    public void Each_module_constant_has_the_value_of_python()
    {
        var constants = Dict(Values["constants"]);
        Assert.Equal(NodeOperations.MaxNodes, (long)constants["MAX_NODES"]!);
        Assert.Equal(NodeOperations.NodeDedupCosineMinScore, (double)constants["NODE_DEDUP_COSINE_MIN_SCORE"]!);
    }

    [Fact]
    public void The_recording_reaches_each_function_and_branch()
    {
        var functions = AsyncCases.Select(c => Str(Dict(c["input"])["fn"])).ToHashSet();
        foreach (var fn in new[] { "extract_nodes", "resolve_extracted_nodes", "resolve_nodes_with_candidates", "collect_candidate_nodes", "extract_attributes_from_nodes", "extract_attributes_only_from_nodes" })
        {
            Assert.Contains(fn, functions);
        }

        var messages = AsyncCases
            .SelectMany(c => List(Dict(c["output"])["events"]).Select(Dict))
            .Where(e => Str(e["kind"]) == "log")
            .Select(e => Str(e["message"]))
            .ToList();
        foreach (var start in new[]
        {
            "No entity types available", "Excluding entity of type", "Identity-property node candidate lookup failed",
            "Exact-name node candidate lookup failed", "LLM did not return resolutions", "LLM returned invalid IDs",
            "Skipping invalid LLM dedupe id", "Duplicate LLM dedupe id", "Invalid duplicate_candidate_id",
            "No semantic dedup candidates", "LLM returned summary for unknown entity", "Last 3 entity IDs",
        })
        {
            Assert.Contains(messages, m => m.StartsWith(start, StringComparison.Ordinal));
        }

        // The exact-name lookup that is not implemented falls back to the query of EntityNode.get_by_names.
        Assert.Contains(
            AsyncCases,
            c => Dict(c["input"]).GetValueOrDefault("exact") is "$not_implemented"
                && List(Dict(c["output"])["events"]).Any(e => Str(Dict(e)["kind"]) == "execute_query"));

        // Each function with a concurrency limit has a case with more calls than the limit (R25).
        var limited = AsyncCases.Where(c => Dict(Dict(c["input"])["clients"]).GetValueOrDefault("max_coroutines") is long).ToList();
        foreach (var kind in new[] { "search", "embed", "identity", "llm" })
        {
            Assert.Contains(limited, c => (long?)Dict(Dict(c["output"])["max_in_flight"]).GetValueOrDefault(kind) is >= 1
                && List(Dict(c["output"])["events"]).Count(e => KindOf(Str(Dict(e)["kind"])) == kind) > (long)Dict(Dict(c["input"])["clients"])["max_coroutines"]!);
        }
    }

    [Fact]
    public void The_port_gives_each_recorded_result_of_the_sync_helpers()
    {
        var types = Dict(Values["types"]);
        List<string> failures = [];
        using var scope = PortServices.Use(time: Clock);
        foreach (var item in List(Values["cases"]).Select(Dict))
        {
            var name = Str(item["f"]);
            var args = List(item["a"]);
            var result = Dict(item["r"]);
            string got;
            try
            {
                got = PyJson.Dumps(new OrderedDictionary<string, object?> { ["ok"] = Normalize(InvokeSync(name, args, types)) });
            }
            catch (Exception error) when (error is not InvalidOperationException)
            {
                // The type of each error, and the message of a ValidationError (R25). The text of another error is not a contract.
                got = PyJson.Dumps(new OrderedDictionary<string, object?> { ["err"] = new List<object?> { PythonName(error), error is ValidationException ? error.Message : null } });
                if (result.TryGetValue("err", out var err))
                {
                    var type = Str(List(err)[0]);
                    result = new() { ["err"] = new List<object?> { type, type == "ValidationError" ? PydanticInput.Replace(Str(List(err)[1]), "]") : null } };
                }
            }

            var want = PyJson.Dumps(result);
            if (want != got)
            {
                failures.Add($"{name}({Short(PyJson.Dumps(args))}): expected {Short(want)} but got {Short(got)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
    }

    public static TheoryData<int> AsyncCaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < AsyncCases.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(AsyncCaseIndexes))]
    public async Task The_port_makes_each_recorded_call_and_gives_each_result(int index)
    {
        var input = (OrderedDictionary<string, object?>)Expand(Dict(AsyncCases[index]["input"]))!;
        var expected = Dict(AsyncCases[index]["output"]);
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new Run(input, source, cancelAt: -1);

        var output = await RunCaseAsync(run, source.Token);

        Assert.Equal(Comparable(expected), Comparable(output));

        // Each fake got the token of the call (R25).
        Assert.All(run.Tokens, token => Assert.Equal(source.Token, token));
    }

    // The scenarios of the cancellation tests: the cases that the recorder ran again with a cancel at each call k but
    // the last (cancel_scenarios in port/tools/record_node_operations.py).
    private static readonly List<int> Scenarios = AsyncCases
        .Index()
        .Where(c => c.Item.ContainsKey("cancel"))
        .Select(c => c.Index)
        .ToList();

    public static TheoryData<int> ScenarioIndexes() => [.. Scenarios];

    public static TheoryData<int, int> LaterCalls()
    {
        var data = new TheoryData<int, int>();
        foreach (var index in Scenarios)
        {
            foreach (var recorded in List(AsyncCases[index]["cancel"]).Select(Dict).Where(r => (long)r["k"]! >= 0))
            {
                data.Add(index, (int)(long)recorded["k"]!);
            }
        }

        return data;
    }

    [Fact]
    public void Each_async_function_has_a_cancellation_scenario() =>
        Assert.Equal(6, Scenarios.Select(i => Str(Dict(AsyncCases[i]["input"])["fn"])).Distinct().Count());

    // Each async method with a canceled token throws OperationCanceledException (R8). The recorder canceled the task of
    // the call before its first await (k = -1), so the test compares the attempts, the recorded calls, and the events
    // that the method writes before it reads the token. A fake that is canceled throws after a yield, so a dropped token
    // or a caught cancel gives other counts.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(ScenarioIndexes))]
    public async Task Each_async_method_with_a_canceled_token_throws_OperationCanceledException(int index)
    {
        var input = (OrderedDictionary<string, object?>)Expand(Dict(AsyncCases[index]["input"]))!;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();
        var run = new Run(input, source, cancelAt: -1);

        using (PortServices.Use(loggerFactory: new EventCapture(run.Events), time: Clock))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));
            await LoopGuard.RunAsync(Turns);
        }

        AssertCancelPoint(index, -1, run, 0);
    }

    // The token is canceled while call k runs. Call k is the last call that a fake records. Python lets a CancelledError
    // through each `except Exception` (it is a BaseException). The recorder cancelled the task of the call at the same
    // point, so the test compares the count of the attempts (each fake counts an attempt before it reads its token),
    // the count of the calls that a fake recorded, and the events after call k (the log lines and the later calls). The
    // calls that a failed gather leaves run on, so the test gives the scheduler 50 turns before it counts (R25).
    // A point in LateCancel is the late cancel of GAPS F33: the call that cancels is the last call of a gather, and Python
    // raises CancelledError where the task of the caller resumes, so it makes no more attempt and writes no more line.
    // The port reads the token at the next call of a fake, so it makes one more attempt, and that attempt throws. At the
    // last search call it also writes the two debug lines that come before the LLM call ("attempts+after"). A point that
    // agrees with Python must not be in the list, and a point in the list must differ in the way that its kind says.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(LaterCalls))]
    public async Task Each_later_awaited_call_gets_the_token_and_no_call_follows_a_cancel(int index, int k)
    {
        var input = (OrderedDictionary<string, object?>)Expand(Dict(AsyncCases[index]["input"]))!;
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var run = new Run(input, source, cancelAt: k);

        using (PortServices.Use(loggerFactory: new EventCapture(run.Events), time: Clock))
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => CallAsync(run, source.Token)));
            await LoopGuard.RunAsync(Turns);
        }

        AssertCancelPoint(index, k, run, run.EventsAtCancel);
    }

    private static void AssertCancelPoint(int index, int k, Run run, int eventsAtCancel)
    {
        var recorded = List(AsyncCases[index]["cancel"]).Select(Dict).Single(r => (long)r["k"]! == k);
        var got = Normalize(new OrderedDictionary<string, object?>
        {
            ["k"] = (long)k,
            ["error"] = "CancelledError",
            ["attempts"] = run.Attempts,
            ["count"] = run.Count,
            ["after"] = run.Events.Skip(eventsAtCancel).ToList(),
        });
        var differences = string.Join(
            "+",
            CancelFields.Where(key => PyJson.Dumps(recorded[key]) != PyJson.Dumps(got[key])));
        var listed = LateCancel.Where(e => e.Case == index && e.K == k).Select(e => e.Kind).ToList();
        if (listed.Count == 0)
        {
            Assert.True(differences.Length == 0, $"case {index}, call {k}: the port differs from Python in {differences}: {PyJson.Dumps(recorded)} and {PyJson.Dumps(got)}");
        }
        else
        {
            Assert.Equal(listed.Single(), differences);
        }
    }

    [Fact]
    public void Each_late_cancel_entry_names_one_point_of_the_recording()
    {
        Assert.Equal(LateCancel.Length, LateCancel.Select(e => (e.Case, e.K)).Distinct().Count());
        Assert.All(LateCancel, e => Assert.Contains(e.K, List(AsyncCases[e.Case]["cancel"]).Select(r => (int)(long)Dict(r)["k"]!)));
    }

    private static readonly string[] CancelFields = ["attempts", "count", "after"];

    // The points where the port makes one more attempt than Python (GAPS F33).
    private static readonly (int Case, int K, string Kind)[] LateCancel =
    [
        (75, 6, "attempts+after"),
        (76, 6, "attempts+after"),
        (179, 0, "attempts"),
        (180, 0, "attempts"),
        (245, 2, "attempts"),
        (245, 7, "attempts+after"),
    ];

    private static async Task Turns()
    {
        for (var i = 0; i < 50; i++)
        {
            await Task.Yield();
        }
    }

    // ------------------------------------------------------------ one async case

    private static async Task<OrderedDictionary<string, object?>> RunCaseAsync(Run run, CancellationToken token)
    {
        var capture = new EventCapture(run.Events);
        object? result = null;
        object? error = null;
        using (PortServices.Use(loggerFactory: capture, time: Clock))
        {
            try
            {
                result = await LoopGuard.RunAsync(() => CallAsync(run, token));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                error = new OrderedDictionary<string, object?> { ["type"] = PythonName(e), ["text"] = e.Message };
            }
        }

        var maxInFlight = new OrderedDictionary<string, object?>();
        foreach (var (kind, count) in run.MaxInFlight.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            maxInFlight[kind] = count;
        }

        return Normalize(new OrderedDictionary<string, object?>
        {
            ["result"] = result,
            ["error"] = error,
            ["events"] = run.Events,
            ["nodes_after"] = run.Nodes.Select(n => (object?)NodeView(n)).ToList(),
            ["max_in_flight"] = maxInFlight,
        })!;
    }

    private static async Task<object?> CallAsync(Run run, CancellationToken token)
    {
        var a = Dict(run.Input["args"]);
        var clients = run.Clients;
        var nodes = run.Nodes;
        var entityTypes = a.TryGetValue("entity_types", out var t) ? Types(t) : null;
        var previous = a.TryGetValue("previous", out var p) && p is not null ? Episodes(p) : null;
        switch (Str(run.Input["fn"]))
        {
            case "extract_nodes":
                {
                    var (extracted, indexMap) = await NodeOperations.ExtractNodesAsync(
                        clients,
                        EpisodeArg(a["episode"])!,
                        previous!,
                        entityTypes,
                        a.TryGetValue("excluded_entity_types", out var excluded) ? Strings(excluded) : null,
                        a.TryGetValue("custom_extraction_instructions", out var custom) ? (string?)custom : null,
                        token);
                    var map = new OrderedDictionary<string, object?>();
                    foreach (var (uuid, indices) in indexMap)
                    {
                        map[uuid] = indices.Select(i => (object?)i).ToList();
                    }

                    return new List<object?> { extracted.Select(n => (object?)NodeView(n)).ToList(), map };
                }

            case "resolve_extracted_nodes":
            case "resolve_nodes_with_candidates":
                {
                    var episode = a.TryGetValue("episode", out var e) ? ((EpisodeOrEpisodes.One?)EpisodeArg(e))?.Episode : null;
                    var identity = a.TryGetValue("identity_properties", out var ip) ? IdentityProperties(ip) : null;
                    (List<EntityNode>, OrderedDictionary<string, string>, List<(EntityNode, EntityNode)>) resolution;
                    if (Str(run.Input["fn"]) == "resolve_extracted_nodes")
                    {
                        resolution = await NodeOperations.ResolveExtractedNodesAsync(
                            clients,
                            nodes,
                            episode,
                            previous,
                            entityTypes,
                            a.TryGetValue("override", out var o) ? MakeNodes(o) : null,
                            identity,
                            cancellationToken: token);
                    }
                    else
                    {
                        resolution = await NodeOperations.ResolveNodesWithCandidatesAsync(
                            clients,
                            nodes,
                            List(a["candidates"]).Select(MakeNodes).ToList(),
                            episode,
                            previous,
                            entityTypes,
                            identity,
                            cancellationToken: token);
                    }

                    var (resolved, uuidMap, pairs) = resolution;
                    var map = new OrderedDictionary<string, object?>();
                    foreach (var (key, value) in uuidMap)
                    {
                        map[key] = value;
                    }

                    return new List<object?>
                {
                    resolved.Select(n => (object?)NodeView(n)).ToList(),
                    map,
                    pairs.Select(pair => (object?)new List<object?> { pair.Item1.Uuid, pair.Item2.Uuid }).ToList(),
                };
                }

            case "collect_candidate_nodes":
                {
                    var result = await NodeOperations.CollectCandidateNodesAsync(
                        clients,
                        nodes,
                        a.TryGetValue("override", out var o) ? MakeNodes(o) : null,
                        a.TryGetValue("identity_properties", out var ip) ? IdentityProperties(ip) : null,
                        a.TryGetValue("reuse_name_embeddings", out var reuse) && (bool)reuse!,
                        token);
                    return result.Select(g => (object?)g.Select(n => (object?)NodeView(n)).ToList()).ToList();
                }

            case "extract_attributes_from_nodes":
                {
                    Func<EntityNode, Task<bool>>? shouldSummarize = null;
                    if (a.TryGetValue("summarize", out var allowedNames))
                    {
                        var allowed = Strings(allowedNames)!;
                        shouldSummarize = async node =>
                        {
                            await Task.Yield();
                            run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "should_summarize", ["name"] = node.Name });
                            return allowed.Contains(node.Name);
                        };
                    }

                    var result = await NodeOperations.ExtractAttributesFromNodesAsync(
                        clients,
                        nodes,
                        a.TryGetValue("episode", out var e) ? EpisodeArg(e) : null,
                        previous,
                        entityTypes,
                        shouldSummarize,
                        a.TryGetValue("edges", out var edges) ? List(edges).Select(x => EntityEdge.ModelValidate(Dict(Py.DeepCopy(x)))).ToList() : null,
                        a.TryGetValue("skip_fact_appending", out var s1) && (bool)s1!,
                        a.TryGetValue("include_type_descriptions", out var s2) && (bool)s2!,
                        a.TryGetValue("skip_attribute_extraction", out var s3) && (bool)s3!,
                        token);
                    return new OrderedDictionary<string, object?> { ["same"] = ReferenceEquals(result, nodes) };
                }

            case "extract_attributes_only_from_nodes":
                {
                    var result = await NodeOperations.ExtractAttributesOnlyFromNodesAsync(
                        clients,
                        nodes,
                        a.TryGetValue("episode", out var e) ? EpisodeArg(e) : null,
                        previous,
                        entityTypes,
                        validateShape: false,
                        cancellationToken: token);
                    return new OrderedDictionary<string, object?> { ["same"] = ReferenceEquals(result, nodes) };
                }

            default:
                throw new InvalidOperationException(Str(run.Input["fn"]));
        }
    }

    // ------------------------------------------------------------ the sync helpers

    private static object? InvokeSync(string name, List<object?> args, OrderedDictionary<string, object?> allTypes)
    {
        switch (name)
        {
            case "_find_sentence_end":
                return NodeOperations.FindSentenceEnd(Str(args[0]));
            case "_truncate_type_description":
                return NodeOperations.TruncateTypeDescription(Str(args[0]));
            case "_build_entity_types_context":
                return Plain(NodeOperations.BuildEntityTypesContext(Types(args[0])));
            case "_filter_entity_types_context":
                return Plain(NodeOperations.FilterEntityTypesContext(Contexts(args[0]), Strings(args[1])));
            case "_get_entity_type_description":
                return NodeOperations.GetEntityTypeDescription(Strings(args[0])!, Types(args[1]));
            case "_entity_type_for_node":
                {
                    var node = EntityNode.ModelValidate(new OrderedDictionary<string, object?> { ["uuid"] = "n", ["name"] = "n", ["group_id"] = "g", ["labels"] = args[0] });
                    return NodeOperations.EntityTypeForNode(node, Types(args[1]))?.PythonName;
                }

            case "_create_entity_nodes":
                {
                    var (created, map) = NodeOperations.CreateEntityNodes(
                        List(args[0]).Select(e => ExtractedEntity.ModelValidate(Dict(Py.DeepCopy(e)))).ToList(),
                        Contexts(args[1]),
                        Strings(args[2]),
                        Episodes(args[3]));
                    return new List<object?> { created.Select(n => (object?)NodeView(n)).ToList(), Plain(map) };
                }

            case "_collapse_exact_duplicate_extracted_nodes":
                {
                    OrderedDictionary<string, List<long>>? map = null;
                    if (args[1] is not null)
                    {
                        map = [];
                        foreach (var (uuid, indices) in Dict(args[1]))
                        {
                            map[uuid] = List(indices).Select(i => (long)i!).ToList();
                        }
                    }

                    var result = NodeOperations.CollapseExactDuplicateExtractedNodes(MakeNodes(args[0]), map);
                    return new List<object?> { result.Select(n => (object?)n.Uuid).ToList(), Plain(map) };
                }

            case "_merge_candidate_nodes":
                return NodeOperations.MergeCandidateNodes(MakeNodes(args[0]), args[1] is null ? null : MakeNodes(args[1]))
                    .Select(n => (object?)(n.Uuid + "/" + n.Name))
                    .ToList();
            case "_build_edges_by_node":
                {
                    var edges = args[0] is null ? null : List(args[0]).Select(x => EntityEdge.ModelValidate(Dict(Py.DeepCopy(x)))).ToList();
                    var result = new OrderedDictionary<string, object?>();
                    foreach (var (uuid, list) in NodeOperations.BuildEdgesByNode(edges))
                    {
                        result[uuid] = list.Select(e => (object?)e.Uuid).ToList();
                    }

                    return result;
                }

            case "_build_episode_context":
                return Plain(NodeOperations.BuildEpisodeContext(Dict(Py.DeepCopy(args[0])), EpisodeArg(args[1]), args[2] is null ? null : Episodes(args[2])));
            default:
                throw new InvalidOperationException(name);
        }
    }

    // ------------------------------------------------------------ values

    private static OrderedDictionary<string, object?> NodeView(EntityNode node) => new()
    {
        ["uuid"] = node.Uuid,
        ["name"] = node.Name,
        ["group_id"] = node.GroupId,
        ["labels"] = node.Labels.Select(l => (object?)l).ToList(),
        ["created_at"] = node.CreatedAt.IsoFormat(),
        ["name_embedding"] = node.NameEmbedding?.Select(v => (object?)v).ToList(),
        ["summary"] = H(node.Summary),
        ["attributes"] = node.Attributes,
    };

    // A text above 400 code points is its SHA-256 and its length, as h() of the recorder writes it.
    private static object H(string text) =>
        Py.Len(text) <= 400
            ? text
            : new OrderedDictionary<string, object?>
            {
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(PyText.EncodeUtf8(text))),
                ["len"] = Py.Len(text),
            };

    // The value as JSON data: a typed list or dict becomes the open form, a model node becomes its view.
    private static object? Plain(object? value) => value switch
    {
        null or bool or long or double or string => value,
        int i => (long)i,
        EntityNode node => NodeView(node),
        PyDateTime dt => dt.IsoFormat(),
        System.Collections.IDictionary dict => PlainDict(dict),
        System.Collections.IEnumerable items => items.Cast<object?>().Select(Plain).ToList(),
        _ => throw new InvalidOperationException($"No plain form for {value.GetType().Name}"),
    };

    private static OrderedDictionary<string, object?> PlainDict(System.Collections.IDictionary dict)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (System.Collections.DictionaryEntry entry in dict)
        {
            result[(string)entry.Key] = Plain(entry.Value);
        }

        return result;
    }

    // Replaces each uuid4 text with <uuid:N>, N in the order of first appearance, as normalize_uuids() of the recorder.
    private static T Normalize<T>(T value)
    {
        var table = new Dictionary<string, string>(StringComparer.Ordinal);
        string Text(string s) => Uuid4.Replace(s, m =>
        {
            if (!table.TryGetValue(m.Value, out var name))
            {
                name = $"<uuid:{table.Count}>";
                table[m.Value] = name;
            }

            return name;
        });

        object? Walk(object? v) => v switch
        {
            string s => Text(s),
            OrderedDictionary<string, object?> d => WalkDict(d),
            List<object?> l => l.Select(Walk).ToList(),
            _ => v is null or bool or long or double ? v : Walk(Plain(v)),
        };

        OrderedDictionary<string, object?> WalkDict(OrderedDictionary<string, object?> d)
        {
            var result = new OrderedDictionary<string, object?>();
            foreach (var (key, entry) in d)
            {
                var k = Text(key);
                result[k] = Walk(entry);
            }

            return result;
        }

        return (T)Walk(value)!;
    }

    // The text that the test compares. A pydantic message is compared without the parts that hold the input (R25). The text
    // of an IndexError is not a contract.
    private static string Comparable(OrderedDictionary<string, object?> output)
    {
        var copy = (OrderedDictionary<string, object?>)Py.DeepCopy(output)!;
        if (copy["error"] is OrderedDictionary<string, object?> error)
        {
            switch (Str(error["type"]))
            {
                case "ValidationError":
                    error["text"] = PydanticInput.Replace(Str(error["text"]), "]");
                    break;
                case "IndexError":
                    error["text"] = null;
                    break;
            }
        }

        return PyJson.Dumps(copy, indent: 1);
    }

    private static string PythonName(Exception error) => error switch
    {
        ValidationException => "ValidationError",
        ArgumentOutOfRangeException => "IndexError",
        ArgumentException => "ValueError",
        NotImplementedException => "NotImplementedError",
        InvalidOperationException => "RuntimeError",
        _ => error.GetType().Name,
    };

    // The kind of a call that takes the token, as the run counts it, or null for a log line or a callback.
    private static string? KindOf(string eventKind) => eventKind switch
    {
        "llm" => "llm",
        "embed_create" or "embed_batch" => "embed",
        "get_by_names" => "names",
        "get_by_attribute_values" => "identity",
        "node_similarity_search" => "search",
        "execute_query" => "query",
        _ => null,
    };

    private static object? Expand(object? value)
    {
        if (value is OrderedDictionary<string, object?> dict)
        {
            if (dict.Count == 1 && dict.TryGetValue("$rep", out var rep))
            {
                var pair = List(rep);
                return string.Concat(Enumerable.Repeat(Str(pair[0]), (int)(long)pair[1]!));
            }

            var result = new OrderedDictionary<string, object?>();
            foreach (var (key, entry) in dict)
            {
                result[key] = Expand(entry);
            }

            return result;
        }

        return value is List<object?> list ? list.Select(Expand).ToList() : value;
    }

    private static List<string>? Strings(object? value) => value is null ? null : List(value).Select(Str).ToList();

    private static List<OrderedDictionary<string, object?>> Contexts(object? value) => List(Py.DeepCopy(value)).Select(Dict).ToList();

    private static List<EntityNode> MakeNodes(object? value) => List(value).Select(d => EntityNode.ModelValidate(Dict(Py.DeepCopy(d)))).ToList();

    private static List<EpisodicNode> Episodes(object? value) => List(value).Select(d => EpisodicNode.ModelValidate(Dict(Py.DeepCopy(d)))).ToList();

    private static EpisodeOrEpisodes? EpisodeArg(object? value) => value switch
    {
        null => null,
        List<object?> list => new EpisodeOrEpisodes.Many { Episodes = Episodes(list) },
        _ => new EpisodeOrEpisodes.One { Episode = EpisodicNode.ModelValidate(Dict(Py.DeepCopy(value))) },
    };

    private static OrderedDictionary<string, List<string>>? IdentityProperties(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var result = new OrderedDictionary<string, List<string>>();
        foreach (var (label, names) in Dict(value))
        {
            result[label] = Strings(names)!;
        }

        return result;
    }

    // A custom entity type from the spec of the recorder (RULES.md R15, decision D26).
    private static OrderedDictionary<string, GraphTypeDescriptor>? Types(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var result = new OrderedDictionary<string, GraphTypeDescriptor>();
        foreach (var (name, spec) in Dict(value))
        {
            var s = Dict(spec);
            result[name] = new GraphTypeDescriptor(
                Str(s["name"]),
                (string?)s["doc"],
                List(s["fields"]).Select(Dict).Select(f => new GraphFieldDescriptor(
                    Str(f["name"]),
                    Str(f["kind"]) switch
                    {
                        "str" => GraphFieldKind.String,
                        "int" => GraphFieldKind.Integer,
                        "float" => GraphFieldKind.Number,
                        _ => GraphFieldKind.Boolean,
                    },
                    (bool)f["list"]!,
                    (string?)f["description"],
                    (bool)f["required"]!,
                    (long?)f["max_length"])));
        }

        return result;
    }

    private static string Short(string text) => text.Length > 300 ? text[..300] + "..." : text;

    private static OrderedDictionary<string, object?> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "node_operations.json");
        return (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
    }
}
