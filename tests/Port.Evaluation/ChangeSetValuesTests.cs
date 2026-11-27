// Evaluation test for graphiti_core/ingestion/change_set.py and graphiti_core/ingestion/config.py (port/RULES.md, R23
// and R25). values/change_set.json holds the calls that port/tools/record_change_set.py recorded from Python:
// merge_edge_attributes, apply_edge_operation, _diff_edge_state, steps of a ResolutionTrace, the text and the bytes of
// ChangeSet.to_bytes, ChangeSet.from_bytes on cut and changed bytes, model_copy(deep=True), and
// ConcurrentIngestionConfig(**kwargs). The stored form of a change set is a stored data shape. The compressed bytes of
// the port differ from the bytes of Python (GAPS F39), so the test compares the text inside the port's bytes, and it
// reads the bytes that Python wrote.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.PyCompat;

public class ChangeSetValuesTests
{
    private static readonly List<OrderedDictionary<string, object?>> Cases = Load();

    public static TheoryData<string> Functions() =>
        [
            "merge_edge_attributes", "merge_edge_attributes_nan", "apply_edge_operation", "_diff_edge_state", "ResolutionTrace",
            "ChangeSet", "ChangeSetBytes", "ChangeSetDeepCopy", "ConcurrentIngestionConfig",
        ];

    [Theory]
    [MemberData(nameof(Functions))]
    public void The_port_gives_each_recorded_result_of_python(string function)
    {
        var cases = Cases.Where(c => (string?)c["f"] == function).ToList();
        Assert.NotEmpty(cases);
        List<string> failures = [];
        foreach (var (index, recorded) in cases.Index())
        {
            var args = Py.AsList(recorded["a"]);
            try
            {
                var problem = function switch
                {
                    "merge_edge_attributes" => Same(
                        recorded["r"],
                        ChangeSetModule.MergeEdgeAttributes(Py.AsDict(args[0]), Py.AsDict(args[1]), Py.AsDict(args[2]))),
                    "merge_edge_attributes_nan" => MergeNan(Py.AsString(args[0]), recorded["r"]),
                    "apply_edge_operation" => Apply(args, Py.AsList(recorded["r"])),
                    "_diff_edge_state" => Diff(args, recorded["r"]),
                    "ResolutionTrace" => Trace(Py.AsDict(args[0]), Py.AsList(recorded["r"])),
                    "ChangeSet" => ChangeSetCase(Py.AsString(args[0]), Py.AsDict(recorded["r"])),
                    "ChangeSetBytes" => CorruptBytes(Py.AsString(args[0]), Py.AsList(recorded["r"])),
                    "ChangeSetDeepCopy" => DeepCopy(Py.AsString(args[0]), Py.AsDict(recorded["r"])),
                    _ => Config(Py.AsDict(args[0]), Py.AsDict(recorded["r"])),
                };
                if (problem is not null)
                {
                    failures.Add($"case {index}: {problem}");
                }
            }
            catch (Exception error)
            {
                failures.Add($"case {index}: {error.GetType().Name}: {error.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} of {cases.Count} recorded results differ. The first ones:\n{string.Join("\n", failures.Take(5))}");
    }

    private static string? Apply(List<object?> args, List<object?> result)
    {
        var edge = EntityEdge.ModelValidate(Py.AsDict(args[0]));
        var operation = (EdgeOperation)Operation.ModelValidate(args[1]);
        var first = ChangeSetModule.ApplyEdgeOperation(edge, operation);
        var middle = edge.ModelDump(json: true);
        var second = ChangeSetModule.ApplyEdgeOperation(edge, operation);
        var unchanged = PyJson.Dumps(edge.ModelDump(json: true)) == PyJson.Dumps(middle);
        return Same(result, new List<object?> { first, middle, second, unchanged });
    }

    private static string? Diff(List<object?> args, object? result)
    {
        var state = new EdgeState
        {
            Edge = EntityEdge.ModelValidate(Py.AsDict(args[0])),
            Episodes = [.. Py.AsList(args[1]).Select(Py.AsString)],
            Attributes = Py.AsDict(args[2]),
            InvalidAt = Date(args[3]),
            ExpiredAt = Date(args[4]),
            EndedAsCandidate = Py.AsBool(args[5]),
        };
        return Same(result, ChangeSetModule.DiffEdgeState(state).Select(op => (object?)Dump(op)).ToList());
    }

    private static string? Trace(OrderedDictionary<string, object?> start, List<object?> steps)
    {
        var edges = Py.AsList(start["edges"]).Select(e => EntityEdge.ModelValidate(Py.AsDict(e))).ToList();
        var nodes = Py.AsList(start["nodes"]).Select(n => EntityNode.ModelValidate(Py.AsDict(n))).ToList();
        var trace = new ResolutionTrace();
        foreach (var (index, item) in steps.Index())
        {
            var recorded = Py.AsDict(item);
            var step = Py.AsDict(recorded["step"]);
            List<EntityEdge> PickEdges(string key) => [.. Py.AsList(step[key]).Select(i => edges[(int)Py.AsLong(i)])];
            object? result = null;
            switch (Py.AsString(step["m"]))
            {
                case "candidates":
                    trace.RecordNodeCandidates(Py.AsString(step["uuid"]), [.. Py.AsList(step["nodes"]).Select(i => nodes[(int)Py.AsLong(i)])]);
                    break;
                case "resolution":
                    trace.RecordNodeResolution(nodes[(int)Py.AsLong(step["node"])], Py.AsString(step["resolved"]));
                    break;
                case "edge_candidates":
                    trace.RecordEdgeCandidates(Py.AsString(step["uuid"]), PickEdges("duplicates"), PickEdges("invalidations"));
                    break;
                case "watch":
                    trace.WatchStoredEdges(PickEdges("edges"));
                    break;
                case "mutate":
                    var edge = edges[(int)Py.AsLong(step["edge"])];
                    if (step.TryGetValue("append", out var append))
                    {
                        edge.Episodes.Add(Py.AsString(append));
                    }

                    if (step.TryGetValue("attributes", out var attributes))
                    {
                        edge.Attributes = new(Py.AsDict(attributes));
                    }

                    if (step.TryGetValue("set_key", out var setKey))
                    {
                        var pair = Py.AsList(setKey);
                        edge.Attributes[Py.AsString(pair[0])] = pair[1];
                    }

                    if (step.TryGetValue("invalid_at", out var invalidAt))
                    {
                        edge.InvalidAt = invalidAt is null ? null : PyDateTime.FromIsoFormat(Py.AsString(invalidAt));
                    }

                    if (step.TryGetValue("expired_at", out var expiredAt))
                    {
                        edge.ExpiredAt = expiredAt is null ? null : PyDateTime.FromIsoFormat(Py.AsString(expiredAt));
                    }

                    break;
                case "labels":
                    var node = nodes[(int)Py.AsLong(step["node"])];
                    node.Labels = [.. node.Labels, Py.AsString(step["label"])];
                    break;
                case "mark":
                    trace.MarkEndedAsCandidate(PickEdges("edges"));
                    break;
                case "collect":
                    result = trace.CollectEdgeOperations(PickEdges("edges")).Select(op => (object?)Dump(op)).ToList();
                    break;
                default:
                    result = trace.CollectEdgeOperations().Select(op => (object?)Dump(op)).ToList();
                    break;
            }

            var state = new OrderedDictionary<string, object?>
            {
                ["seen_node_candidates"] = trace.SeenNodeCandidates,
                ["seen_node_labels"] = trace.SeenNodeLabels,
                ["seen_duplicate_candidates"] = trace.SeenDuplicateCandidates,
                ["seen_invalidation_candidates"] = trace.SeenInvalidationCandidates,
                ["operations"] = trace.Operations.Select(op => (object?)Dump(op)).ToList(),
                ["stored_node_uuids"] = trace.StoredNodeUuids.Order(Py.StringComparer).ToList(),
                ["ended_by_candidate"] = trace.EndedByCandidate.Order(Py.StringComparer).ToList(),
                ["node_llm_calls"] = trace.NodeLlmCalls,
            };
            var problem = Same(recorded["result"], result) ?? Same(recorded["state"], state);
            if (problem is not null)
            {
                return $"step {index} ({PyJson.Dumps(step)}): {problem}";
            }
        }

        return null;
    }

    private static string? ChangeSetCase(string text, OrderedDictionary<string, object?> result)
    {
        var changeSet = ChangeSet.ModelValidate(Pydantic.LoadJson(text));
        if (Pydantic.DumpJson(changeSet.ModelDump(json: true)) != text)
        {
            return "model_dump_json differs: " + Pydantic.DumpJson(changeSet.ModelDump(json: true));
        }

        // The compressed bytes of .NET (zlib-ng) differ from the bytes of CPython's zlib 1.2.12 (GAPS F39). The test compares
        // the text inside the bytes: the bytes of the port hold the text of Python, and the bytes of Python hold the same text.
        if (PyText.DecodeUtf8(PyZlib.Decompress(changeSet.ToBytes())) != text || !Py.AsBool(result["text_of_bytes"]))
        {
            return "to_bytes holds another text than the bytes of Python";
        }

        var restored = ChangeSet.FromBytes(Convert.FromHexString(Py.AsString(result["bytes"])));
        if (Py.AsBool(result["restored_equal"]) != (Pydantic.DumpJson(restored.ModelDump(json: true)) == text))
        {
            return "from_bytes of the bytes of Python gives another change set";
        }

        var copy = changeSet.ModelCopy(deep: true);
        if (Py.AsBool(result["deep_copy_equal"]) != (Pydantic.DumpJson(copy.ModelDump(json: true)) == text))
        {
            return "model_copy(deep=True) gives another change set";
        }

        var patches = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in changeSet.PatchesByNode())
        {
            patches[key] = value.Select(p => (object?)p.ModelDump(json: true)).ToList();
        }

        return Same(result["patches_by_node"], patches)
            ?? Same(result["edge_operations"], changeSet.EdgeOperations().Select(op => (object?)Dump(op)).ToList());
    }

    // `current_value == base.get(key, missing)` is a plain ==: a nan is not equal to itself, also when it is one object in
    // both maps. In a list, the comparison of the items tests identity first (R6, PyValueEquality.PlainEqual).
    private static string? MergeNan(string name, object? recorded)
    {
        // One boxed nan is one Python object. A second box is another object.
        object nan = double.NaN;
        object other = double.NaN;
        var (@base, current) = name switch
        {
            "same object, top level" => ((object?)nan, (object?)nan),
            "two objects, top level" => (other, nan),
            "same object in a list" => (new List<object?> { nan }, new List<object?> { nan }),
            "two objects in a list" => (new List<object?> { other }, new List<object?> { nan }),
            _ => throw new InvalidOperationException(name),
        };
        OrderedDictionary<string, object?> Map(object? value) => new() { ["x"] = value };
        return Same(recorded, ChangeSetModule.MergeEdgeAttributes(Map(@base), Map(1L), Map(current)));
    }

    private static byte[] Mutate(byte[] payload, List<object?> spec)
    {
        switch (Py.AsString(spec[0]))
        {
            case "keep":
                return payload[..(int)Py.AsLong(spec[1])];
            case "cut_tail":
                return payload[..(payload.Length - (int)Py.AsLong(spec[1]))];
            case "xor":
                var changed = (byte[])payload.Clone();
                changed[(int)Py.AsLong(spec[1])] ^= (byte)Py.AsLong(spec[2]);
                return changed;
            case "append":
                return [.. payload, .. Convert.FromHexString(Py.AsString(spec[1]))];
            default:
                return payload;
        }
    }

    // ChangeSet.from_bytes on bytes that Python wrote, cut or changed. Python raises zlib.error, and the port throws
    // InvalidDataException. The text is recorded for a cut stream, a wrong header, and a wrong check value. For another kind
    // of error only the class is the same.
    private static string? CorruptBytes(string hex, List<object?> results)
    {
        var payload = Convert.FromHexString(hex);
        foreach (var item in results)
        {
            var pair = Py.AsList(item);
            var spec = Py.AsList(pair[0]);
            var want = Py.AsList(pair[1]);
            string[] got;
            try
            {
                got = ["ok", Pydantic.DumpJson(ChangeSet.FromBytes(Mutate(payload, spec)).ModelDump(json: true))];
            }
            catch (InvalidDataException error)
            {
                got = ["zlib.error", error.Message];
            }

            var sameClass = got[0] == Py.AsString(want[0]);
            var sameText = (want[1] is null && got[0] == "zlib.error") || got[1] == (string?)want[1];
            if (!sameClass || !sameText)
            {
                return $"{PyJson.Dumps(spec)}: expected {Py.Repr(want[0])} {Py.Repr(want[1])} but got {Py.Repr(got[0])} {Py.Repr(got[1])}";
            }
        }

        return null;
    }

    private static readonly OrderedDictionary<string, Func<ChangeSet, ChangeSet, bool>> SharedProbes = new()
    {
        ["node_uuids"] = (a, b) => ReferenceEquals(b.NodeUuids, a.NodeUuids),
        ["node_uuids_item"] = (a, b) => ReferenceEquals(b.NodeUuids[0], a.NodeUuids[0]),
        ["episodes"] = (a, b) => ReferenceEquals(b.Episodes, a.Episodes),
        ["episode0"] = (a, b) => ReferenceEquals(b.Episodes[0], a.Episodes[0]),
        ["episode0_labels"] = (a, b) => ReferenceEquals(b.Episodes[0].Labels, a.Episodes[0].Labels),
        ["episode0_entity_edges"] = (a, b) => ReferenceEquals(b.Episodes[0].EntityEdges, a.Episodes[0].EntityEdges),
        ["episode0_metadata"] = (a, b) => ReferenceEquals(b.Episodes[0].EpisodeMetadata, a.Episodes[0].EpisodeMetadata),
        ["episode0_metadata_list"] = (a, b) => ReferenceEquals(b.Episodes[0].EpisodeMetadata!["m"], a.Episodes[0].EpisodeMetadata!["m"]),
        ["create_nodes"] = (a, b) => ReferenceEquals(b.CreateNodes, a.CreateNodes),
        ["node0"] = (a, b) => ReferenceEquals(b.CreateNodes[0], a.CreateNodes[0]),
        ["node0_labels"] = (a, b) => ReferenceEquals(b.CreateNodes[0].Labels, a.CreateNodes[0].Labels),
        ["node0_embedding"] = (a, b) => ReferenceEquals(b.CreateNodes[0].NameEmbedding, a.CreateNodes[0].NameEmbedding),
        ["node0_attributes"] = (a, b) => ReferenceEquals(b.CreateNodes[0].Attributes, a.CreateNodes[0].Attributes),
        ["node0_tags"] = (a, b) => ReferenceEquals(b.CreateNodes[0].Attributes["tags"], a.CreateNodes[0].Attributes["tags"]),
        ["node0_tags_inner"] = (a, b) => ReferenceEquals(Item(b.CreateNodes[0].Attributes["tags"], 1), Item(a.CreateNodes[0].Attributes["tags"], 1)),
        ["node0_meta_list"] = (a, b) => ReferenceEquals(Key(b.CreateNodes[0].Attributes["meta"], "k"), Key(a.CreateNodes[0].Attributes["meta"], "k")),
        ["dup_nodes"] = (a, b) => ReferenceEquals(b.CreateNodes[0], b.CreateNodes[1]),
        ["create_edges"] = (a, b) => ReferenceEquals(b.CreateEdges, a.CreateEdges),
        ["edge0"] = (a, b) => ReferenceEquals(b.CreateEdges[0], a.CreateEdges[0]),
        ["edge0_episodes"] = (a, b) => ReferenceEquals(b.CreateEdges[0].Episodes, a.CreateEdges[0].Episodes),
        ["edge0_embedding"] = (a, b) => ReferenceEquals(b.CreateEdges[0].FactEmbedding, a.CreateEdges[0].FactEmbedding),
        ["edge0_attributes"] = (a, b) => ReferenceEquals(b.CreateEdges[0].Attributes, a.CreateEdges[0].Attributes),
        ["edge0_tags"] = (a, b) => ReferenceEquals(b.CreateEdges[0].Attributes["tags"], a.CreateEdges[0].Attributes["tags"]),
        ["edge0_tags_dict"] = (a, b) => ReferenceEquals(Item(b.CreateEdges[0].Attributes["tags"], 1), Item(a.CreateEdges[0].Attributes["tags"], 1)),
        ["edge0_tags_list"] = (a, b) => ReferenceEquals(Key(Item(b.CreateEdges[0].Attributes["tags"], 1), "y"), Key(Item(a.CreateEdges[0].Attributes["tags"], 1), "y")),
        ["dup_edges"] = (a, b) => ReferenceEquals(b.CreateEdges[0], b.CreateEdges[1]),
        ["episodic_edges"] = (a, b) => ReferenceEquals(b.EpisodicEdges, a.EpisodicEdges),
        ["episodic_edge0"] = (a, b) => ReferenceEquals(b.EpisodicEdges[0], a.EpisodicEdges[0]),
        ["operations"] = (a, b) => ReferenceEquals(b.Operations, a.Operations),
        ["op_add"] = (a, b) => ReferenceEquals(b.Operations[0], a.Operations[0]),
        ["op_add_episode_uuids"] = (a, b) => ReferenceEquals(((AddEpisodesToEdge)b.Operations[0]).EpisodeUuids, ((AddEpisodesToEdge)a.Operations[0]).EpisodeUuids),
        ["op_end"] = (a, b) => ReferenceEquals(b.Operations[1], a.Operations[1]),
        ["op_set"] = (a, b) => ReferenceEquals(b.Operations[2], a.Operations[2]),
        ["op_set_base"] = (a, b) => ReferenceEquals(((SetEdgeAttributes)b.Operations[2]).BaseAttributes, ((SetEdgeAttributes)a.Operations[2]).BaseAttributes),
        ["op_set_base_list"] = (a, b) => ReferenceEquals(((SetEdgeAttributes)b.Operations[2]).BaseAttributes["t"], ((SetEdgeAttributes)a.Operations[2]).BaseAttributes["t"]),
        ["op_set_attributes"] = (a, b) => ReferenceEquals(((SetEdgeAttributes)b.Operations[2]).Attributes, ((SetEdgeAttributes)a.Operations[2]).Attributes),
        ["op_set_nested"] = (a, b) => ReferenceEquals(Key(((SetEdgeAttributes)b.Operations[2]).Attributes["u"], "v"), Key(((SetEdgeAttributes)a.Operations[2]).Attributes["u"], "v")),
        ["op_patch"] = (a, b) => ReferenceEquals(b.Operations[3], a.Operations[3]),
        ["op_patch_labels"] = (a, b) => ReferenceEquals(((PatchNode)b.Operations[3]).Labels, ((PatchNode)a.Operations[3]).Labels),
        ["op_patch_attributes"] = (a, b) => ReferenceEquals(((PatchNode)b.Operations[3]).Attributes, ((PatchNode)a.Operations[3]).Attributes),
        ["dup_ops"] = (a, b) => ReferenceEquals(b.Operations[2], b.Operations[4]),
        ["node_first_episode"] = (a, b) => ReferenceEquals(b.NodeFirstEpisode, a.NodeFirstEpisode),
        ["seen_node_candidates"] = (a, b) => ReferenceEquals(b.SeenNodeCandidates, a.SeenNodeCandidates),
        ["seen_node_candidates_list"] = (a, b) => ReferenceEquals(b.SeenNodeCandidates["n1"], a.SeenNodeCandidates["n1"]),
        ["seen_node_labels"] = (a, b) => ReferenceEquals(b.SeenNodeLabels, a.SeenNodeLabels),
        ["seen_node_labels_list"] = (a, b) => ReferenceEquals(b.SeenNodeLabels["s1"], a.SeenNodeLabels["s1"]),
        ["seen_duplicate_candidates"] = (a, b) => ReferenceEquals(b.SeenDuplicateCandidates, a.SeenDuplicateCandidates),
        ["seen_duplicate_candidates_list"] = (a, b) => ReferenceEquals(b.SeenDuplicateCandidates["e1"], a.SeenDuplicateCandidates["e1"]),
        ["seen_invalidation_candidates"] = (a, b) => ReferenceEquals(b.SeenInvalidationCandidates, a.SeenInvalidationCandidates),
        ["seen_invalidation_candidates_list"] = (a, b) => ReferenceEquals(b.SeenInvalidationCandidates["e1"], a.SeenInvalidationCandidates["e1"]),
        ["edges_ended_by_candidate"] = (a, b) => ReferenceEquals(b.EdgesEndedByCandidate, a.EdgesEndedByCandidate),
        ["uuid_map"] = (a, b) => ReferenceEquals(b.UuidMap, a.UuidMap),
        ["saga_links"] = (a, b) => ReferenceEquals(b.SagaLinks, a.SagaLinks),
        ["saga_link0"] = (a, b) => ReferenceEquals(b.SagaLinks[0], a.SagaLinks[0]),
        ["saga_node"] = (a, b) => ReferenceEquals(b.SagaNode, a.SagaNode),
        ["saga_node_labels"] = (a, b) => ReferenceEquals(b.SagaNode!.Labels, a.SagaNode!.Labels),
    };

    private static object? Item(object? list, int index) => ((List<object?>)list!)[index];

    private static object? Key(object? dict, string key) => ((OrderedDictionary<string, object?>)dict!)[key];

    // model_copy(deep=True) (phases.py:1221, rebase_change_set). Python records which objects the copy shares with the
    // original. The test then changes each list, dict, and nested value of the copy, and the original must keep its text.
    private static string? DeepCopy(string text, OrderedDictionary<string, object?> result)
    {
        var original = ChangeSet.ModelValidate(Pydantic.LoadJson(text));

        // The recording holds one node, one edge, and one operation two times. Validation made one object for each item.
        original.CreateNodes[1] = original.CreateNodes[0];
        original.CreateEdges[1] = original.CreateEdges[0];
        original.Operations[4] = original.Operations[2];
        var copy = original.ModelCopy(deep: true);

        var recorded = Py.AsDict(result["shared"]);
        List<string> problems = [];
        if (!SharedProbes.Keys.SequenceEqual(recorded.Keys))
        {
            return "the probes differ from the recorded probes";
        }

        foreach (var (name, probe) in SharedProbes)
        {
            if (probe(original, copy) != Py.AsBool(recorded[name]))
            {
                problems.Add($"{name}: Python gives {Py.Repr(recorded[name])}");
            }
        }

        if (Pydantic.DumpJson(copy.ModelDump(json: true)) != text || !Py.AsBool(result["copy_equal"]))
        {
            problems.Add("the text of the copy differs from the text of the original");
        }

        ChangeNested(copy);
        if (Pydantic.DumpJson(original.ModelDump(json: true)) != text)
        {
            problems.Add("a change of the copy reached the original");
        }

        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    // A change of each container of the copy, down to the nested values of the open attributes.
    private static void ChangeNested(ChangeSet copy)
    {
        copy.NodeUuids.Add("zz");
        copy.Episodes[0].Labels.Add("zz");
        copy.Episodes[0].EntityEdges.Add("zz");
        ((List<object?>)copy.Episodes[0].EpisodeMetadata!["m"]!).Add("zz");
        copy.Episodes[0].EpisodeMetadata!["zz"] = "zz";

        var node = copy.CreateNodes[0];
        node.Labels.Add("zz");
        node.NameEmbedding!.Add(9.0);
        node.Attributes["zz"] = "zz";
        ((List<object?>)node.Attributes["tags"]!).Add("zz");
        ((List<object?>)Item(node.Attributes["tags"], 1)!).Add("zz");
        ((List<object?>)Key(node.Attributes["meta"], "k")!).Add("zz");

        var edge = copy.CreateEdges[0];
        edge.Episodes.Add("zz");
        edge.FactEmbedding!.Add(9.0);
        edge.Attributes["zz"] = "zz";
        ((List<object?>)edge.Attributes["tags"]!).Add("zz");
        ((OrderedDictionary<string, object?>)Item(edge.Attributes["tags"], 1)!)["zz"] = "zz";
        ((List<object?>)Key(Item(edge.Attributes["tags"], 1), "y")!).Add("zz");

        ((AddEpisodesToEdge)copy.Operations[0]).EpisodeUuids.Add("zz");
        var set = (SetEdgeAttributes)copy.Operations[2];
        set.BaseAttributes["zz"] = "zz";
        ((List<object?>)set.BaseAttributes["t"]!).Add("zz");
        ((List<object?>)Key(set.Attributes["u"], "v")!).Add("zz");
        var patch = (PatchNode)copy.Operations[3];
        patch.Labels.Add("zz");
        patch.Attributes["zz"] = "zz";
        ((List<object?>)patch.Attributes["t"]!).Add("zz");

        copy.NodeFirstEpisode["zz"] = "zz";
        copy.SeenNodeCandidates["n1"].Add("zz");
        copy.SeenNodeLabels["s1"].Add("zz");
        copy.SeenDuplicateCandidates["e1"].Add("zz");
        copy.SeenInvalidationCandidates["e1"].Add("zz");
        copy.EdgesEndedByCandidate.Add("zz");
        copy.UuidMap["zz"] = "zz";
        copy.SagaNode!.Labels.Add("zz");
        copy.SagaLinks.Clear();
        copy.EpisodicEdges.Clear();
        copy.CreateEdges.Clear();
        copy.CreateNodes.Clear();
        copy.Episodes.Clear();
        copy.Operations.Clear();
    }

    private static string? Config(OrderedDictionary<string, object?> kwargs, OrderedDictionary<string, object?> result)
    {
        if (result.TryGetValue("err", out var err))
        {
            var error = Py.AsList(err);
            Assert.Equal("ValidationError", error[0]);
            var thrown = Assert.Throws<ValidationException>(() => ConcurrentIngestionConfig.ModelValidate(kwargs));
            return thrown.Message == Py.AsString(error[1]) ? null : $"message: expected {Py.Repr(error[1])} but got {Py.Repr(thrown.Message)}";
        }

        var config = ConcurrentIngestionConfig.ModelValidate(kwargs);
        return Same(result["ok"], new OrderedDictionary<string, object?>
        {
            ["window"] = config.Window,
            ["state_store"] = config.StateStore,
            ["lease_seconds"] = config.LeaseSeconds,
            ["write_margin_seconds"] = config.WriteMarginSeconds,
            ["poll_interval_seconds"] = config.PollIntervalSeconds,
            ["index_lag_seconds"] = config.IndexLagSeconds,
            ["max_apply_attempts"] = config.MaxApplyAttempts,
        });
    }

    private static OrderedDictionary<string, object?> Dump(Operation operation) => operation switch
    {
        AddEpisodesToEdge a => a.ModelDump(json: true),
        EndEdge e => e.ModelDump(json: true),
        SetEdgeAttributes s => s.ModelDump(json: true),
        PatchNode p => p.ModelDump(json: true),
        _ => throw new InvalidOperationException("unknown operation"),
    };

    private static PyDateTime? Date(object? value) => value is null ? null : PyDateTime.FromIsoFormat(Py.AsString(value));

    // The recorded value and the port's value as JSON text. A typed list or dict of the port has the same text.
    private static string? Same(object? expected, object? actual)
    {
        var want = PyJson.Dumps(expected);
        var got = PyJson.Dumps(Pydantic.Dump(actual));
        return want == got ? null : $"expected {want} but got {got}";
    }

    private static List<OrderedDictionary<string, object?>> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "change_set.json");
        var document = Py.AsDict(PyJson.Loads(File.ReadAllText(path)));
        return [.. Py.AsList(document["cases"]).Select(Py.AsDict)];
    }
}
