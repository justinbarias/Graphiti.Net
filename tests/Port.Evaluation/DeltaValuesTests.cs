// Evaluation test for graphiti_core/ingestion/delta.py (port/RULES.md, R18, R23, and R25).
// values/delta.json holds the calls that port/tools/record_delta.py recorded from Python: each function and each method
// of Delta, with the result or the type and the text of the exception. A float32 value of a matrix is compared exactly,
// and each list of UUIDs is compared exactly. The recorder docstring names the inputs.
namespace Graphiti.Port.Evaluation;

using System.Globalization;
using System.Numerics;
using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.Core.Search;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.PyCompat;
using static Graphiti.Port.Evaluation.MaintenanceHelpersValuesTests;

// The digest cache of delta.py is module state with no lock, as in Python: product code calls LoadDeltaDigests only on
// the scheduler (decision D22). So each test here that calls it runs its body in one turn of the scheduler
// (LoopGuard.RunAsync, a body with no await): no other ported code, for example a commit of the phases tests, runs
// between its calls or changes the cache at the same time. The classes that read the state of the cache are in this
// collection, and xunit runs the classes of one collection one after the other.
[Collection(DigestCacheGroup)]
public class DeltaValuesTests
{
    internal const string DigestCacheGroup = "DeltaDigestCache";

    internal static readonly OrderedDictionary<string, object?> Values =
        (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "values", "delta.json")))!;

    private static readonly List<OrderedDictionary<string, object?>> Cases = [.. List(Values["cases"]).Select(Dict)];

    [Fact]
    public void Each_module_constant_has_the_value_of_python()
    {
        var constants = Dict(Values["constants"]);
        Assert.Equal(DeltaModule.DigestCacheSize, (long)constants["DIGEST_CACHE_SIZE"]!);
        Assert.Equal(SearchUtils.DefaultMinScore, (double)constants["DEFAULT_MIN_SCORE"]!);
        Assert.Equal(SearchConfigModule.DefaultSearchLimit, (long)constants["DEFAULT_SEARCH_LIMIT"]!);
        Assert.Equal(Graphiti.Core.Nodes.NodeDedupCandidateLimit, (long)constants["NODE_DEDUP_CANDIDATE_LIMIT"]!);
        Assert.Equal(NodeOperations.NodeDedupCosineMinScore, (double)constants["NODE_DEDUP_COSINE_MIN_SCORE"]!);
    }

    [Fact]
    public void The_recording_reaches_each_function_and_edge_case()
    {
        foreach (var name in new[] { "_unit_matrix", "_fuzzy_query", "_fuzzy_match", "_stack", "_stack_uuids", "_top_matches", "patch_as_node", "delta" })
        {
            Assert.Contains(Cases, c => Str(c["f"]) == name);
        }

        var top = Cases.Where(c => Str(c["f"]) == "_top_matches" && Dict(c["r"]).ContainsKey("ok")).ToList();

        // A score exactly on DEFAULT_MIN_SCORE, and 0, 1, and more matches than the limit.
        Assert.Contains(top, c => (double)List(c["a"])[3]! == 0.6 && List(List(Dict(c["r"])["ok"])[0]).Contains("c2") && (long)List(c["a"])[4]! == 10);
        Assert.Contains(top, c => (double)List(c["a"])[3]! == 0.6000000238418579 && List(List(Dict(c["r"])["ok"])[0]).Contains("c2"));
        Assert.Contains(top, c => (double)List(c["a"])[3]! == 0.5999999 && List(List(Dict(c["r"])["ok"])[0]).Contains("c2"));

        // NumPy 2 compares the float32 score with the limit in float32 (NEP 50): 0.60000003 rounds to the score 0.6.
        Assert.Contains(top, c => (double)List(c["a"])[3]! == 0.60000003 && List(List(Dict(c["r"])["ok"])[0]).Contains("c2"));

        // GAPS G17: BLAS sums a score in another order than the loop of the port. The recorder gives the ranks of the
        // loop where the two differ, and it checked that each score differs by less than 1e-6.
        Assert.True(Cases.Count(c => c.ContainsKey("r_loop")) <= 2);
        Assert.Contains(top, c => List(Dict(c["r"])["ok"]).Any(r => List(r).Count == 0));
        Assert.Contains(top, c => List(Dict(c["r"])["ok"]).Any(r => List(r).Count == 1));
        Assert.Contains(top, c => List(Dict(c["r"])["ok"]).Any(r => List(r).Count == 15));

        // numpy's quicksort orders equal scores of 17 or more candidates in another order than their columns.
        Assert.Contains(top, c => List(List(c["a"])[1]).Count == 17 && Str(List(List(Dict(c["r"])["ok"])[0])[1]) == "c14");
        Assert.Contains(top, c => List(List(c["a"])[1]).Count == 22);

        // The fuzzy rule finds a match, and a load of the delta raises.
        Assert.Contains(Cases, c => Str(c["f"]) == "_fuzzy_match" && Dict(c["r"])["ok"] is string);
        Assert.Contains(Cases, c => Str(c["f"]) == "delta" && Dict(c["r"]).ContainsKey("err"));
    }

    // Each case runs in one turn of the scheduler (see the comment of the class). One turn for each case, and not one for
    // the whole loop, keeps the scheduler free for the tests that run beside it.
    [Fact]
    public async Task The_port_gives_each_recorded_result()
    {
        List<string> failures = [];
        foreach (var (index, item) in Cases.Index())
        {
            var name = Str(item["f"]);
            var args = List(item["a"]);
            var got = await LoopGuard.RunAsync(() => Task.FromResult(Got(name, args)), seconds: 30);

            // GAPS G17: where BLAS gave other ranks than the plain loop, the recorder gives the ranks of the loop.
            var want = PyJson.Dumps(item.GetValueOrDefault("r_loop") ?? item["r"]);
            var text = PyJson.Dumps(got);
            if (want != text)
            {
                failures.Add($"case {index} {name}({Short(PyJson.Dumps(args))}):\n  expected {Short(want)}\n  got      {Short(text)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(10)) + $"\n{failures.Count} failures");
    }

    // The digest cache: the same units give the same digest objects, and DIGEST_CACHE_SIZE + 1 other units drop them.
    // The calls of LoadDeltaDigests run in one turn of the scheduler. The test makes the payloads before it.
    [Fact]
    public async Task The_digest_cache_keeps_and_drops_digests_as_python_does()
    {
        var cache = Dict(Values["cache"]);
        var payloads = List(cache["payloads"]).Select(p => Convert.FromHexString(Str(p))).ToList();
        List<DeltaUnit> first = [Unit(1, "committed", payloads[0]), Unit(2, "committed", payloads[1])];
        var fillerChangeSet = ChangeSet.FromBytes(payloads[0]);
        List<DeltaUnit> filler = [];
        for (var i = 0; i < (long)cache["filler_count"]!; i++)
        {
            fillerChangeSet.Seq = 1000 + i;
            filler.Add(Unit(1000 + i, "committed", fillerChangeSet.ToBytes()));
        }

        var (one, two, otherStatus, otherGroup, three) = await LoopGuard.RunAsync(
            () =>
            {
                var one = DeltaModule.LoadDeltaDigests("cache", first).Digests;
                var two = DeltaModule.LoadDeltaDigests("cache", [.. Enumerable.Reverse(first)]).Digests;
                var otherStatus = DeltaModule.LoadDeltaDigests("cache", [Unit(1, "applying", payloads[0])]).Digests;
                var otherGroup = DeltaModule.LoadDeltaDigests("cache2", first[..1]).Digests;
                DeltaModule.LoadDeltaDigests("cache", filler);
                var three = DeltaModule.LoadDeltaDigests("cache", first).Digests;
                return Task.FromResult((one, two, otherStatus, otherGroup, three));
            },
            seconds: 30);

        Assert.Equal(PyJson.Dumps(cache["again"]), PyJson.Dumps(one.Zip(two).Select(p => (object?)ReferenceEquals(p.First, p.Second)).ToList()));
        Assert.Equal((bool)cache["other_status"]!, ReferenceEquals(otherStatus[0], one[0]));
        Assert.Equal((bool)cache["other_group"]!, ReferenceEquals(otherGroup[0], one[0]));
        Assert.Equal(PyJson.Dumps(cache["after_eviction"]), PyJson.Dumps(one.Zip(three).Select(p => (object?)ReferenceEquals(p.First, p.Second)).ToList()));
    }

    internal static DeltaUnit Unit(long seq, string status, byte[] payload) => new() { Seq = seq, Status = status, Payload = payload };

    private static OrderedDictionary<string, object?> Got(string name, List<object?> args)
    {
        try
        {
            return new OrderedDictionary<string, object?> { ["ok"] = Invoke(name, args) };
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException)
        {
            return new OrderedDictionary<string, object?> { ["err"] = ErrorView(error) };
        }
    }

    private static object? Invoke(string name, List<object?> a)
    {
        switch (name)
        {
            case "_unit_matrix":
                return MatrixView(DeltaModule.UnitMatrix(Vectors(a[0])));
            case "_fuzzy_query":
                {
                    var query = DeltaModule.FuzzyQuery(Str(a[0]));
                    return query is null
                        ? null
                        : new List<object?>
                        {
                            query.Value.Shingles.Order(Py.StringComparer).Cast<object?>().ToList(),
                            query.Value.Bands.Select(band => (object?)band.Select(item => (object?)IntText(item)).ToList()).ToList(),
                        };
                }

            case "_fuzzy_match":
                return DeltaModule.FuzzyMatch(DeltaModule.FuzzyQuery(Str(a[0])), DedupHelpers.BuildCandidateIndexes(Nodes(a[1])));
            case "_stack":
                return MatrixView(DeltaModule.Stack([.. List(a[0]).Select(m => DeltaModule.UnitMatrix(Vectors(m)))]));
            case "_stack_uuids":
                return DeltaModule.StackUuids(
                    [.. List(a[0]).Select(u => List(u).Select(Str).ToList())],
                    [.. List(a[1]).Select(m => DeltaModule.UnitMatrix(Vectors(m)))]);
            case "_top_matches":
                {
                    var rows = Vectors(a[2]);
                    var matrix = (bool)a[5]! ? Raw(rows) : DeltaModule.UnitMatrix(rows);
                    return DeltaModule.TopMatches(
                        [.. List(a[0]).Select(v => v is null ? null : Vector(v))],
                        [.. List(a[1]).Select(Str)],
                        matrix,
                        Number(a[3]),
                        (long)a[4]!);
                }

            case "patch_as_node":
                {
                    var view = NodeView(DeltaModule.PatchAsNode(Node(a[0]), PatchNode.ModelValidate(Dict(Py.DeepCopy(a[1])))));
                    view.Remove("created_at");
                    return view;
                }

            case "delta":
                return DeltaCase(a);
            default:
                throw new InvalidOperationException(name);
        }
    }

    private static OrderedDictionary<string, object?> DeltaCase(List<object?> a)
    {
        List<DeltaUnit> units = [.. List(a[1]).Select(Dict).Select(u => Unit((long)u["seq"]!, Str(u["status"]), Convert.FromHexString(Str(u["payload"]))))];
        var delta = DeltaModule.LoadDeltaDigests(Str(a[0]), units);
        var nodes = Nodes(a[2]);
        List<EntityEdge> edges = [.. List(a[3]).Select(e => EntityEdge.ModelValidate(Dict(Py.DeepCopy(e))))];
        var identity = new OrderedDictionary<string, List<string>>();
        foreach (var (label, properties) in Dict(a[4]))
        {
            identity[label] = [.. List(properties).Select(Str)];
        }

        var output = new OrderedDictionary<string, object?> { ["delta"] = DeltaView(delta), ["match_nodes"] = delta.MatchNodes(nodes) };
        foreach (var (key, call) in new (string, Func<object?>)[]
        {
            ("match_nodes_identity", () => delta.MatchNodes(nodes, identity)),
            ("patched_identity_matches", () => delta.PatchedIdentityMatches(nodes, identity)),
            ("patched_identity_matches_none", () => delta.PatchedIdentityMatches(nodes, null)),
            ("edges_between", () => delta.EdgesBetween(edges)),
            ("similar_edges", () => delta.SimilarEdges(edges)),
        })
        {
            try
            {
                output[key] = new OrderedDictionary<string, object?> { ["ok"] = call() };
            }
            catch (ArgumentException error)
            {
                output[key] = new OrderedDictionary<string, object?> { ["err"] = ErrorView(error) };
            }
        }

        return output;
    }

    private static OrderedDictionary<string, object?> DeltaView(Delta delta)
    {
        var patches = new OrderedDictionary<string, object?>();
        foreach (var (uuid, list) in delta.Patches)
        {
            patches[uuid] = list.Select(p => (object?)p.ModelDump(json: true)).ToList();
        }

        return new()
        {
            ["unit_count"] = delta.UnitCount,
            ["digests"] = delta.Digests.Select(d => (object?)DigestView(d)).ToList(),
            ["patches"] = patches,
            ["created_node_uuids"] = delta.CreatedNodeUuids.Order(Py.StringComparer).Cast<object?>().ToList(),
        };
    }

    private static OrderedDictionary<string, object?> DigestView(UnitDigest digest)
    {
        var indexes = digest.NodeIndexes;
        var normalized = new OrderedDictionary<string, object?>();
        foreach (var (key, list) in indexes.NormalizedExisting)
        {
            normalized[key] = list.Select(n => (object?)n.Uuid).ToList();
        }

        var shingles = new OrderedDictionary<string, object?>();
        foreach (var (key, set) in indexes.ShinglesByCandidate)
        {
            shingles[key] = set.Order(Py.StringComparer).Cast<object?>().ToList();
        }

        return new()
        {
            ["seq"] = digest.Seq,
            ["status"] = digest.Status,
            ["nodes"] = digest.Nodes.Select(n => (object?)NodeView(n)).ToList(),
            ["normalized_existing"] = normalized,
            ["nodes_by_uuid"] = indexes.NodesByUuid.Keys.Cast<object?>().ToList(),
            ["shingles_by_candidate"] = shingles,
            ["lsh_bucket_count"] = (long)indexes.LshBuckets.Count,
            ["node_vector_uuids"] = digest.NodeVectorUuids,
            ["node_vectors"] = MatrixView(digest.NodeVectors),
            ["edges"] = digest.Edges.Select(e => (object?)new List<object?> { e.Item1, e.Item2, e.Item3 }).ToList(),
            ["edge_vector_uuids"] = digest.EdgeVectorUuids,
            ["edge_vectors"] = MatrixView(digest.EdgeVectors),
            ["patches"] = digest.Patches.Select(p => (object?)p.ModelDump(json: true)).ToList(),
        };
    }

    private static OrderedDictionary<string, object?> NodeView(EntityNode node) => new()
    {
        ["uuid"] = node.Uuid,
        ["name"] = node.Name,
        ["group_id"] = node.GroupId,
        ["labels"] = node.Labels,
        ["created_at"] = node.CreatedAt.IsoFormat(),
        ["name_embedding"] = node.NameEmbedding,
        ["summary"] = node.Summary,
        ["attributes"] = node.Attributes,
    };

    // {'shape': [rows, columns], 'rows': matrix.tolist()}, with a value that is not finite as {'$f': repr(value)}.
    private static OrderedDictionary<string, object?> MatrixView(float[,] matrix) => new()
    {
        ["shape"] = new List<object?> { (long)matrix.GetLength(0), (long)matrix.GetLength(1) },
        ["rows"] = Enumerable.Range(0, matrix.GetLength(0))
            .Select(row => (object?)Enumerable.Range(0, matrix.GetLength(1)).Select(column => Num(matrix[row, column])).ToList())
            .ToList(),
    };

    private static object? Num(float value) =>
        float.IsFinite(value) ? (double)value : new OrderedDictionary<string, object?> { ["$f"] = Py.Repr((double)value) };

    // `np.asarray(rows, dtype=np.float32)`, and the empty matrix of the recorder for no rows.
    private static float[,] Raw(List<List<double>> rows)
    {
        var matrix = new float[rows.Count, rows.Count == 0 ? 0 : rows[0].Count];
        for (var row = 0; row < matrix.GetLength(0); row++)
        {
            for (var column = 0; column < matrix.GetLength(1); column++)
            {
                matrix[row, column] = (float)rows[row][column];
            }
        }

        return matrix;
    }

    private static List<object?> ErrorView(Exception error) =>
    [
        error switch
        {
            InvalidDataException => "error",
            _ => "ValueError",
        },
        error.Message,
    ];

    private static string IntText(object? value) => value switch
    {
        BigInteger big => big.ToString(CultureInfo.InvariantCulture),
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException("not an int"),
    };

    private static double Number(object? value) => value is long l ? l : (double)value!;

    private static List<double> Vector(object? value) => [.. List(value).Select(Number)];

    private static List<List<double>> Vectors(object? value) => [.. List(value).Select(Vector)];

    private static List<EntityNode> Nodes(object? value) => [.. List(value).Select(Node)];

    private static EntityNode Node(object? value) => EntityNode.ModelValidate(Dict(Py.DeepCopy(value)));

    private static string Short(string text) => text.Length > 600 ? text[..600] + "..." : text;
}

// The sequences of review B of packet P8b on the digest cache. Each one uses its own group, so its result does not
// depend on what the cache held before it. Each body runs in one turn of the scheduler, and the class is in the
// collection of DeltaValuesTests (see there).
[Collection(DeltaValuesTests.DigestCacheGroup)]
public class DeltaDigestCacheTests
{
    private static readonly OrderedDictionary<string, object?> Cache = Dict(DeltaValuesTests.Values["cache"]);

    private static readonly List<byte[]> Payloads = [.. List(Cache["payloads"]).Select(p => Convert.FromHexString(Str(p)))];

    // A, then DIGEST_CACHE_SIZE - 1 other units, then A: A is the oldest entry of a full cache, and it stays.
    [Fact]
    public async Task The_digest_cache_keeps_an_entry_on_the_size_limit()
    {
        var others = Others();
        await OneTurn(() =>
        {
            var a = Load("limit", 1, Payloads[0]);
            DeltaModule.LoadDeltaDigests("limit", others);
            Assert.Equal((bool)Cache["limit_kept"]!, ReferenceEquals(a, Load("limit", 1, Payloads[0])));
        });
    }

    // A, B, A again, then DIGEST_CACHE_SIZE - 1 other units: the hit moved A to the end, so B leaves and A stays.
    [Fact]
    public async Task A_hit_moves_the_entry_to_the_end_of_the_digest_cache()
    {
        var others = Others();
        await OneTurn(() =>
        {
            var a = Load("move", 1, Payloads[0]);
            var b = Load("move", 2, Payloads[1]);
            Load("move", 1, Payloads[0]);
            DeltaModule.LoadDeltaDigests("move", others);
            List<object?> move = [ReferenceEquals(a, Load("move", 1, Payloads[0])), ReferenceEquals(b, Load("move", 2, Payloads[1]))];
            Assert.Equal(PyJson.Dumps(Cache["move"]), PyJson.Dumps(move));
        });
    }

    // Two payloads with the same group, seq, status, and length: the hash of the payload keeps their keys apart.
    [Fact]
    public async Task Two_payloads_of_one_length_give_two_digests()
    {
        await OneTurn(() =>
        {
            var twins = List(Cache["twins"]).Select(p => Convert.FromHexString(Str(p))).ToList();
            Assert.Equal(twins[0].Length, twins[1].Length);
            var x = Load("twins", 7, twins[0]);
            var y = Load("twins", 7, twins[1]);
            Assert.Equal((bool)Cache["twins_same"]!, ReferenceEquals(x, y));
            Assert.Equal(PyJson.Dumps(Cache["twins_names"]), PyJson.Dumps(new List<object?> { x.Nodes[0].Name, y.Nodes[0].Name }));
        });
    }

    // The body runs in one turn of the scheduler: it has no await, so no other ported code runs between its calls.
    private static Task OneTurn(Action body) =>
        LoopGuard.RunAsync(
            () =>
            {
                body();
                return Task.CompletedTask;
            },
            seconds: 30);

    private static UnitDigest Load(string groupId, long seq, byte[] payload) =>
        DeltaModule.LoadDeltaDigests(groupId, [DeltaValuesTests.Unit(seq, "committed", payload)]).Digests[0];

    // DIGEST_CACHE_SIZE - 1 other units with the seqs 5000 and up. Only their keys matter, so the port writes them.
    private static List<DeltaUnit> Others()
    {
        var changeSet = ChangeSet.FromBytes(Payloads[0]);
        List<DeltaUnit> units = [];
        for (var i = 0L; i < (long)Cache["other_count"]!; i++)
        {
            changeSet.Seq = 5000 + i;
            units.Add(DeltaValuesTests.Unit(5000 + i, "committed", changeSet.ToBytes()));
        }

        return units;
    }
}
