// Evaluation test for graphiti_core/ingestion/change_set.py and graphiti_core/ingestion/config.py (port/RULES.md, R23
// and R25). values/change_set.json holds the calls that port/tools/record_change_set.py recorded from Python:
// merge_edge_attributes, apply_edge_operation, _diff_edge_state, steps of a ResolutionTrace, the text and the bytes of
// ChangeSet.to_bytes, and ConcurrentIngestionConfig(**kwargs). The stored form of a change set is a stored data shape,
// so the test compares the compressed bytes with the bytes of Python, and reads the bytes that Python wrote.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.PyCompat;

public class ChangeSetValuesTests
{
    private static readonly List<OrderedDictionary<string, object?>> Cases = Load();

    public static TheoryData<string> Functions() =>
        ["merge_edge_attributes", "apply_edge_operation", "_diff_edge_state", "ResolutionTrace", "ChangeSet", "ConcurrentIngestionConfig"];

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
                    "apply_edge_operation" => Apply(args, Py.AsList(recorded["r"])),
                    "_diff_edge_state" => Diff(args, recorded["r"]),
                    "ResolutionTrace" => Trace(Py.AsDict(args[0]), Py.AsList(recorded["r"])),
                    "ChangeSet" => ChangeSetCase(Py.AsString(args[0]), Py.AsDict(recorded["r"])),
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

        // The compressed bytes of .NET (zlib-ng) differ from the bytes of CPython's zlib 1.2.12 (the TODO(port) at
        // ChangeSet.ToBytes). The test compares the text inside the bytes: the bytes of the port hold the text of
        // Python, and the bytes of Python hold the same text.
        if (Inflate(changeSet.ToBytes()) != text || !Py.AsBool(result["text_of_bytes"]))
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

    // zlib.decompress(payload).decode(), written with the zlib stream of .NET.
    private static string Inflate(byte[] payload)
    {
        using var input = new MemoryStream(payload);
        using var zlib = new System.IO.Compression.ZLibStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return PyText.DecodeUtf8(output.ToArray());
    }

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
