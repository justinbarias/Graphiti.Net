// Evaluation test for four maintenance modules (port/RULES.md, R23 and R25): dedup_helpers.py, attribute_utils.py,
// temporal_edge_utils.py, and dangling_endpoints.py of graphiti_core/utils/maintenance/.
// values/maintenance_helpers.json holds about 4,000 calls that port/tools/record_maintenance_helpers.py recorded from
// Python, each with its log lines, and values/maintenance_helpers_review.json holds the fixed calls that came with the
// reviews. GraphDataOperationsValuesTests reads the async cases of the first file.
namespace Graphiti.Port.Evaluation;

using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Graphiti.Core;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class MaintenanceHelpersValuesTests
{
    internal static readonly OrderedDictionary<string, object?> Document = Load("maintenance_helpers.json");

    private static readonly OrderedDictionary<string, object?> ReviewDocument = Load("maintenance_helpers_review.json");

    [Fact]
    public void Each_module_constant_has_the_value_of_python()
    {
        var constants = Dict(Document["constants"]);
        var dedup = Dict(constants["dedup_helpers"]);
        Assert.Equal(DedupHelpers.NameEntropyThreshold, (double)dedup["_NAME_ENTROPY_THRESHOLD"]!);
        Assert.Equal(DedupHelpers.MinNameLength, (long)dedup["_MIN_NAME_LENGTH"]!);
        Assert.Equal(DedupHelpers.MinTokenCount, (long)dedup["_MIN_TOKEN_COUNT"]!);
        Assert.Equal(DedupHelpers.FuzzyJaccardThreshold, (double)dedup["_FUZZY_JACCARD_THRESHOLD"]!);
        Assert.Equal(DedupHelpers.MinhashPermutations, (long)dedup["_MINHASH_PERMUTATIONS"]!);
        Assert.Equal(DedupHelpers.MinhashBandSize, (long)dedup["_MINHASH_BAND_SIZE"]!);
        Assert.Equal(DedupHelpers.EntityBaseLabel, (string)dedup["ENTITY_BASE_LABEL"]!);

        var attributes = Dict(constants["attribute_utils"]);
        Assert.Equal(AttributeUtils.DefaultAttributeMaxLength, (long)attributes["DEFAULT_ATTRIBUTE_MAX_LENGTH"]!);
        Assert.Equal(AttributeUtils.ListTotalLengthMultiplier, (long)attributes["LIST_TOTAL_LENGTH_MULTIPLIER"]!);
        Assert.Equal(AttributeUtils.EnvVar, (string)attributes["_ENV_VAR"]!);

        Assert.Equal(GraphDataOperations.EpisodeWindowLen, (long)Dict(constants["graph_data_operations"])["EPISODE_WINDOW_LEN"]!);

        // The recording holds the maxsize of the lru_cache of Python. The port keeps its cache in a private static field.
        var cache = typeof(DedupHelpers).GetField("CachedShinglesCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(cache);
        Assert.Equal((int)(long)constants["lru_cache_maxsize"]!, ((PyLruCache<string, HashSet<string>>)cache.GetValue(null)!).MaxSize);
    }

    [Fact]
    public void The_port_gives_each_recorded_result_and_log_line_of_python()
    {
        var cases = List(Document["cases"]);
        Assert.True(cases.Count >= 4000);
        var descriptors = Dict(Dict(Dict(cases.Single(c => (string)Dict(c)["f"]! == "descriptors"))["r"])["ok"]);
        var models = new Dictionary<string, GraphTypeDescriptor>(StringComparer.Ordinal);
        foreach (var (name, fields) in descriptors)
        {
            models[name] = new GraphTypeDescriptor(
                name,
                null,
                List(fields).Select(f => List(f)).Select(f => new GraphFieldDescriptor((string)f[0]!, GraphFieldKind.String, false, null, (bool)f[1]!, (long?)f[2])));
        }

        // The cases of one file share the set of warned environment values, as the recorder does, so they run in the
        // recorded order. Each file starts with an empty set, and the set is private to this test.
        var clock = new MaintenanceFixedClock(PyDateTime.FromIsoFormat((string)Document["fixed_now"]!).ToDateTimeOffset());
        List<string> failures = [];
        foreach (var file in new[] { cases, List(ReviewDocument["cases"]) })
        {
            using (AttributeUtils.WarnedInvalidEnv.Use(new HashSet<string>(StringComparer.Ordinal)))
            {
                foreach (var item in file)
                {
                    Check(Dict(item), models, clock, failures);
                }
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} recorded results differ. The first ones:\n{string.Join("\n", failures.Take(8))}");
    }

    // The order of a Python set of text changes with the hash seed. R14 gives the port the insertion order, and the
    // recorder binds `set` to a class with that order. A case with "set_order": true has another result for another
    // order, so real Python can give another result for it. The recording must keep such cases for both places.
    [Fact]
    public void The_recording_holds_cases_that_depend_on_the_order_of_a_set()
    {
        var ordered = List(Document["cases"]).Concat(List(ReviewDocument["cases"]))
            .Select(Dict)
            .Where(c => c.TryGetValue("set_order", out var flag) && (bool)flag!)
            .Select(c => (string)c["f"]!)
            .ToList();
        Assert.Contains("resolve", ordered);
        Assert.Contains("apply_capped_attributes", ordered);
        Assert.All(ordered, name => Assert.True(name is "resolve" or "apply_capped_attributes", name));
    }

    private static void Check(OrderedDictionary<string, object?> recorded, Dictionary<string, GraphTypeDescriptor> models, MaintenanceFixedClock clock, List<string> failures)
    {
        var name = (string)recorded["f"]!;
        if (name == "descriptors")
        {
            return;
        }

        var args = List(recorded["a"]);
        var result = Dict(recorded["r"]);
        var expectedLogs = List(recorded["logs"]).Select(Dict).Select(l => ((string)l["level"]!, (string)l["message"]!)).ToList();
        var capture = new MaintenanceLogCapture();
        var label = $"{name}({Short(PyJson.Dumps(args))})";
        try
        {
            using (PortServices.Use(loggerFactory: capture, time: clock))
            {
                if (result.TryGetValue("err", out var err))
                {
                    var error = List(err);
                    var thrown = Assert.ThrowsAny<Exception>(() => Invoke(name, args, models));
                    switch ((string)error[0]!)
                    {
                        case "ValueError":
                            Assert.IsType<ArgumentException>(thrown);
                            Assert.Equal((string)error[1]!, thrown.Message);
                            break;
                        case "UnicodeEncodeError":
                            Assert.IsType<EncoderFallbackException>(thrown);
                            break;
                        default:
                            throw new InvalidOperationException($"No exception type for {error[0]}");
                    }
                }
                else
                {
                    var want = PyJson.Dumps(result["ok"]);
                    var got = PyJson.Dumps(Invoke(name, args, models));
                    if (want != got)
                    {
                        var at = 0;
                        while (at < want.Length && at < got.Length && want[at] == got[at])
                        {
                            at++;
                        }

                        failures.Add($"{label}: the results differ at character {at}: expected ...{Window(want, at)} but got ...{Window(got, at)}");
                        return;
                    }
                }
            }

            if (!expectedLogs.SequenceEqual(capture.Lines))
            {
                failures.Add($"{label}: the log lines differ: expected [{string.Join(" | ", expectedLogs)}] but got [{string.Join(" | ", capture.Lines)}]");
            }
        }
        catch (Exception error)
        {
            failures.Add($"{label}: {error.GetType().Name}: {error.Message}");
        }
    }

    private static object? Invoke(string name, List<object?> a, Dictionary<string, GraphTypeDescriptor> models) => name switch
    {
        "_normalize_string_exact" => DedupHelpers.NormalizeStringExact(Str(a[0])),
        "_normalize_name_for_fuzzy" => DedupHelpers.NormalizeNameForFuzzy(Str(a[0])),
        "_name_entropy" => DedupHelpers.NameEntropy(Str(a[0])),
        "_has_high_entropy" => DedupHelpers.HasHighEntropy(Str(a[0])),
        "_shingles" => Sorted(DedupHelpers.Shingles(Str(a[0]))),
        "_cached_shingles" => Sorted(DedupHelpers.CachedShingles(Str(a[0]))),
        "_minhash_signature" => Hashes(DedupHelpers.MinhashSignature(Set(a[0]))),
        "_hash_shingle" => DedupHelpers.HashShingle(Str(a[0]), (long)a[1]!).ToString(CultureInfo.InvariantCulture),
        "_lsh_bands" => DedupHelpers.LshBands(List(a[0]).Select(h => ulong.Parse(Str(h), CultureInfo.InvariantCulture)).ToList())
            .Select(band => (object?)Hashes(band.Cast<ulong>())).ToList(),
        "_jaccard_similarity" => DedupHelpers.JaccardSimilarity(Set(a[0]), Set(a[1])),
        "_normalize_identity_value" => DedupHelpers.NormalizeIdentityValue(a[0]),
        "_identity_type_for_node" => DedupHelpers.IdentityTypeForNode(Node(a[0]), IdentityProperties(a[1])),
        "_identity_key_for_node" => Plain(DedupHelpers.IdentityKeyForNode(Node(a[0]), IdentityProperties(a[1]))),
        "_promote_resolved_node" => NodeView(DedupHelpers.PromoteResolvedNode(Node(a[0]), Node(a[1]))),
        "_build_candidate_indexes" => IndexesView(DedupHelpers.BuildCandidateIndexes(List(a[0]).Select(Node).ToList())),
        "resolve" => Resolve(List(a[0]), List(a[1]), List(a[2]), IdentityProperties(a[3])),
        "_check_value_against_cap" => Plain(AttributeUtils.CheckValueAgainstCap(Decode(a[0]), (long)a[1]!)),
        "_resolve_default_max_length" => WithEnvironment(a[0], () => AttributeUtils.ResolveDefaultMaxLength((long)a[1]!)),
        "cap_string_attributes" => WithEnvironment(a[0], () => CapView(Cap(models[Str(a[1])], Dict(a[2]), Dict(a[3])))),
        "apply_capped_attributes" => WithEnvironment(null, () => CapView(Apply(models[Str(a[0])], Dict(a[1]), Dict(a[2]), Dict(a[3])))),
        "normalize_future_temporal_bounds" => Plain(TemporalEdgeUtils.NormalizeFutureTemporalBounds(Str(a[0]), Date(a[1]), Date(a[2]), Date(a[3]))),
        "apply_extracted_timestamps" => ApplyTimestamps(a),
        "materialize_dangling_endpoint_node" => Materialize(Str(a[0]), Str(a[1]), Dict(a[2]), List(a[3])),
        "prune_unreferenced_materialized_nodes" => Prune(List(a[0]), List(a[1]), List(a[2])),
        _ => throw new InvalidOperationException($"No binding for {name}"),
    };

    private static OrderedDictionary<string, object?> Resolve(List<object?> existing, List<object?> extracted, List<object?> pre, OrderedDictionary<string, List<string>>? props)
    {
        var existingNodes = existing.Select(Node).ToList();
        var extractedNodes = extracted.Select(Node).ToList();
        var indexes = DedupHelpers.BuildCandidateIndexes(existingNodes);
        var state = new DedupResolutionState
        {
            ResolvedNodes = pre.Select(p => p is null ? null : existingNodes[(int)(long)p]).ToList(),
            UuidMap = new(),
            UnresolvedIndices = [],
        };
        DedupHelpers.ResolveWithIdentityProperties(extractedNodes, indexes, state, props);
        var afterIdentity = new OrderedDictionary<string, object?>
        {
            ["resolved_nodes"] = state.ResolvedNodes.Select(n => (object?)n?.Uuid).ToList(),
            ["uuid_map"] = Plain(state.UuidMap),
            ["duplicate_pairs"] = Pairs(state.DuplicatePairs),
        };
        DedupHelpers.ResolveWithSimilarity(extractedNodes, indexes, state);
        return new OrderedDictionary<string, object?>
        {
            ["after_identity"] = afterIdentity,
            ["resolved_nodes"] = state.ResolvedNodes.Select(n => n is null ? null : (object?)NodeView(n)).ToList(),
            ["uuid_map"] = Plain(state.UuidMap),
            ["unresolved_indices"] = state.UnresolvedIndices.Cast<object?>().ToList(),
            ["duplicate_pairs"] = Pairs(state.DuplicatePairs),
            ["existing_after"] = existingNodes.Select(n => (object?)NodeView(n)).ToList(),
        };
    }

    private static List<object?> Pairs(List<(EntityNode, EntityNode)> pairs) =>
        pairs.Select(p => (object?)new List<object?> { p.Item1.Uuid, p.Item2.Uuid }).ToList();

    private static OrderedDictionary<string, object?> IndexesView(DedupCandidateIndexes indexes)
    {
        var normalized = new OrderedDictionary<string, object?>();
        foreach (var (key, nodes) in indexes.NormalizedExisting)
        {
            normalized[key] = nodes.Select(n => (object?)n.Uuid).ToList();
        }

        var shingles = new OrderedDictionary<string, object?>();
        foreach (var (key, set) in indexes.ShinglesByCandidate)
        {
            shingles[key] = Sorted(set);
        }

        var byUuid = new OrderedDictionary<string, object?>();
        foreach (var (key, node) in indexes.NodesByUuid)
        {
            byUuid[key] = node.Uuid;
        }

        return new OrderedDictionary<string, object?>
        {
            ["existing_nodes"] = indexes.ExistingNodes.Select(n => (object?)n.Uuid).ToList(),
            ["nodes_by_uuid"] = byUuid,
            ["normalized_existing"] = normalized,
            ["shingles_by_candidate"] = shingles,
            ["lsh_buckets"] = indexes.LshBuckets
                .Select(kv => (object?)new List<object?> { kv.Key[0], Hashes(((PyTuple)kv.Key[1]!).Cast<ulong>()), kv.Value.Cast<object?>().ToList() })
                .ToList(),
        };
    }

    private static (OrderedDictionary<string, object?>, PySet<string>) Cap(GraphTypeDescriptor model, OrderedDictionary<string, object?> response, OrderedDictionary<string, object?> kwargs) =>
        AttributeUtils.CapStringAttributes(
            DecodeDict(response),
            model,
            kwargs.TryGetValue("default_max_length", out var max) ? (long)max! : AttributeUtils.DefaultAttributeMaxLength,
            kwargs.TryGetValue("prompt_name", out var prompt) ? (string)prompt! : string.Empty,
            kwargs.TryGetValue("entity_uuid", out var uuid) ? (string)uuid! : string.Empty,
            kwargs.TryGetValue("group_id", out var group) ? (string)group! : string.Empty);

    private static (OrderedDictionary<string, object?>, PySet<string>) Apply(
        GraphTypeDescriptor model,
        OrderedDictionary<string, object?> response,
        OrderedDictionary<string, object?> prior,
        OrderedDictionary<string, object?> kwargs) =>
        AttributeUtils.ApplyCappedAttributes(
            DecodeDict(response),
            model,
            prior,
            (string)kwargs["merge_mode"]!,
            kwargs.TryGetValue("default_max_length", out var max) ? (long)max! : AttributeUtils.DefaultAttributeMaxLength,
            kwargs.TryGetValue("prompt_name", out var prompt) ? (string)prompt! : string.Empty,
            kwargs.TryGetValue("entity_uuid", out var uuid) ? (string)uuid! : string.Empty,
            kwargs.TryGetValue("group_id", out var group) ? (string)group! : string.Empty);

    // [kept, sorted(dropped), list(kept)]: the key order of the result is part of the comparison.
    private static List<object?> CapView((OrderedDictionary<string, object?> Kept, PySet<string> Dropped) result) =>
        [Plain(result.Kept), Sorted(result.Dropped), result.Kept.Keys.Cast<object?>().ToList()];

    private static T WithEnvironment<T>(object? value, Func<T> body)
    {
        using (PyEnv.Use(name => name == AttributeUtils.EnvVar ? (string?)value : null))
        {
            return body();
        }
    }

    private static List<object?> ApplyTimestamps(List<object?> a)
    {
        var edge = EntityEdge.ModelValidate(new OrderedDictionary<string, object?>
        {
            ["group_id"] = "g",
            ["source_node_uuid"] = "s",
            ["target_node_uuid"] = "t",
            ["created_at"] = PyDateTime.FromIsoFormat((string)Document["fixed_now"]!),
            ["name"] = "R",
            ["fact"] = Str(a[0]),
            ["valid_at"] = Date(a[4]),
            ["invalid_at"] = Date(a[5]),
        });
        TemporalEdgeUtils.ApplyExtractedTimestamps(edge, (string?)a[1], (string?)a[2], Date(a[3]));
        return [null, Plain(edge.ValidAt), Plain(edge.InvalidAt)];
    }

    private static OrderedDictionary<string, object?> Materialize(string name, string groupId, OrderedDictionary<string, object?> mapping, List<object?> nodes)
    {
        var nameToNode = new Dictionary<string, EntityNode>(StringComparer.Ordinal);
        var keys = new List<string>();
        foreach (var (key, data) in mapping)
        {
            nameToNode[key] = Node(data);
            keys.Add(key);
        }

        var before = keys.ToDictionary(k => k, k => nameToNode[k], StringComparer.Ordinal);
        var nodeList = nodes.Select(Node).ToList();
        var result = DanglingEndpoints.MaterializeDanglingEndpointNode(name, groupId, nameToNode, nodeList);
        var existing = keys.FirstOrDefault(k => ReferenceEquals(before[k], result));
        if (existing is null)
        {
            // A new node has a random uuid4: the test compares its shape.
            Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", result.Uuid);
            keys.AddRange(nameToNode.Keys.Where(k => !keys.Contains(k, StringComparer.Ordinal)));
        }

        OrderedDictionary<string, object?> View(EntityNode node)
        {
            var data = NodeView(node);
            if (ReferenceEquals(node, result) && existing is null)
            {
                data["uuid"] = "<uuid4>";
            }

            return data;
        }

        var mapped = new OrderedDictionary<string, object?>();
        foreach (var key in keys)
        {
            mapped[key] = View(nameToNode[key])["uuid"];
        }

        return new OrderedDictionary<string, object?>
        {
            ["existing_key"] = existing,
            ["result"] = View(result),
            ["nodes"] = nodeList.Select(n => (object?)View(n)).ToList(),
            ["appended"] = nodeList.Count > 0 && ReferenceEquals(nodeList[^1], result),
            ["name_to_node"] = mapped,
            ["new_key_is_result"] = keys.Where(k => ReferenceEquals(nameToNode[k], result)).Cast<object?>().ToList(),
        };
    }

    private static OrderedDictionary<string, object?> Prune(List<object?> nodes, List<object?> initial, List<object?> referenced)
    {
        var nodeList = nodes.Select(Node).ToList();
        var same = nodeList;
        var result = DanglingEndpoints.PruneUnreferencedMaterializedNodes(
            nodeList,
            initial.Select(Str).ToHashSet(StringComparer.Ordinal),
            referenced.Select(Str).ToHashSet(StringComparer.Ordinal));
        return new OrderedDictionary<string, object?>
        {
            ["result"] = result.Select(n => (object?)n.Uuid).ToList(),
            ["nodes"] = same.Select(n => (object?)n.Uuid).ToList(),
        };
    }

    internal static EntityNode Node(object? data) => EntityNode.ModelValidate(Dict(data));

    internal static OrderedDictionary<string, object?> NodeView(EntityNode node) => new()
    {
        ["uuid"] = node.Uuid,
        ["name"] = node.Name,
        ["group_id"] = node.GroupId,
        ["labels"] = node.Labels.Cast<object?>().ToList(),
        ["created_at"] = node.CreatedAt.IsoFormat(),
        ["summary"] = node.Summary,
        ["attributes"] = Plain(node.Attributes),
    };

    private static OrderedDictionary<string, List<string>>? IdentityProperties(object? value)
    {
        if (value is null)
        {
            return null;
        }

        var result = new OrderedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (key, names) in Dict(value))
        {
            result[key] = List(names).Select(Str).ToList();
        }

        return result;
    }

    // {"$tuple": [...]} in the recording is a Python tuple.
    private static object? Decode(object? value) =>
        value is OrderedDictionary<string, object?> { Count: 1 } d && d.TryGetValue("$tuple", out var items) ? PyTuple.From(List(items)) : value;

    private static OrderedDictionary<string, object?> DecodeDict(OrderedDictionary<string, object?> value)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, item) in value)
        {
            result[key] = Decode(item);
        }

        return result;
    }

    private static PyDateTime? Date(object? value) => value is null ? null : PyDateTime.FromIsoFormat(Str(Dict(value)["$dt"]));

    private static List<object?> Sorted(IEnumerable<string> items) => items.Order(Py.StringComparer).Cast<object?>().ToList();

    private static List<object?> Hashes(IEnumerable<ulong> values) => values.Select(v => (object?)v.ToString(CultureInfo.InvariantCulture)).ToList();

    private static HashSet<string> Set(object? value) => List(value).Select(Str).ToHashSet(StringComparer.Ordinal);

    // A result as a value of the model of R6, so that PyJson.Dumps writes it as the recording does.
    internal static object? Plain(object? value) => value switch
    {
        null or string or bool or long or double or BigInteger => value,
        int i => (long)i,
        PyDateTime dt => new OrderedDictionary<string, object?> { ["$dt"] = dt.IsoFormat() },
        OrderedDictionary<string, object?> dict => PlainDict(dict),
        OrderedDictionary<string, string> dict => PlainDict(dict.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value))),
        ITuple tuple => Enumerable.Range(0, tuple.Length).Select(i => Plain(tuple[i])).ToList(),
        IEnumerable items => items.Cast<object?>().Select(Plain).ToList(),
        _ => throw new InvalidOperationException($"No normal form for {value.GetType().Name}"),
    };

    private static OrderedDictionary<string, object?> PlainDict(IEnumerable<KeyValuePair<string, object?>> dict)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, entry) in dict)
        {
            result[key] = Plain(entry);
        }

        return result;
    }

    private static string Window(string text, int at) => text[Math.Max(0, at - 40)..Math.Min(text.Length, at + 60)];

    private static string Short(string text) => text.Length > 120 ? text[..120] + "..." : text;

    internal static string Str(object? value) => (string)value!;

    internal static List<object?> List(object? value) => (List<object?>)value!;

    internal static OrderedDictionary<string, object?> Dict(object? value) => (OrderedDictionary<string, object?>)value!;

    private static OrderedDictionary<string, object?> Load(string file)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", file);
        return (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
    }
}
