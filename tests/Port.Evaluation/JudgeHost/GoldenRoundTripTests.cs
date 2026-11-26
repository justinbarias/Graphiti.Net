namespace Port.Evaluation.JudgeHost;

using System.Text.Json;
using System.Text.Json.Nodes;
using Graphiti.JudgeHost.Core;

/// <summary>
/// Reads the real judge files under port/judge/ at test time. Each golden result is decoded and encoded again, and
/// the text must equal the golden line. Each case argument must decode with known tags only.
/// </summary>
public sealed class GoldenRoundTripTests(ITestOutputHelper output)
{
    private static readonly HashSet<string> ArgumentTags = new(StringComparer.Ordinal)
    {
        "$dt", "$ref", "$enum", "$model", "$tuple", "$set", "$dict", "$bytes", "$float",
    };

    private static readonly HashSet<string> FlowTags = new(ValueDecoder.FlowTags, StringComparer.Ordinal);

    [Fact]
    public void EachGoldenResultEncodesToTheSameLine()
    {
        int total = 0, compared = 0, skipped = 0;
        var failures = new List<string>();
        foreach (var file in Directory.GetFiles(Repository.Path("port", "judge", "golden"), "*.jsonl", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                total++;

                // The runner writes $bag and $vec. A host never does, so such a line cannot come from the codec.
                if (line.Contains("\"$bag\":", StringComparison.Ordinal) || line.Contains("\"$vec\":", StringComparison.Ordinal))
                {
                    skipped++;
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                var id = document.RootElement.GetProperty("id").GetString()!;
                var result = document.RootElement.GetProperty("result");
                JsonObject encoded;
                if (result.TryGetProperty("ok", out var ok))
                {
                    var decoder = new TestDecoder();
                    encoded = new JsonObject { ["ok"] = Codec.Encode(decoder.Decode(ok), decoder.Registry) };
                }
                else
                {
                    encoded = JsonObject.Create(result.Clone())!;
                }

                var text = JudgeJson.Dumps(SortSets(new JsonObject { ["id"] = id, ["result"] = encoded }), sortKeys: true);
                compared++;
                if (text != line)
                {
                    failures.Add($"{id}\n  golden: {Cut(line)}\n  dotnet: {Cut(text)}");
                }
            }
        }

        output.WriteLine($"golden lines: {total}, compared: {compared}, skipped for $bag or $vec: {skipped}, different: {failures.Count}");
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
        Assert.Equal(1999, total);
    }

    [Fact]
    public void EachCaseArgumentDecodesWithKnownTags()
    {
        int cases = 0, decoded = 0, decodeErrors = 0;
        var tagCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<string>();
        var golden = GoldenResults();
        foreach (var file in Directory.GetFiles(Repository.Path("port", "judge", "cases"), "*.jsonl", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                cases++;
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var id = root.GetProperty("id").GetString()!;
                var flow = root.TryGetProperty("steps", out var steps);
                var arguments = new List<JsonElement>();
                if (flow)
                {
                    foreach (var step in steps.EnumerateArray())
                    {
                        var calls = step.TryGetProperty("parallel", out var parallel) ? [.. parallel.EnumerateArray()] : new List<JsonElement> { step };
                        foreach (var call in calls)
                        {
                            Add(arguments, call, "args");
                            Add(arguments, call, "kwargs");
                        }
                    }

                    Add(arguments, root, "graphiti");
                }
                else
                {
                    Add(arguments, root, "args");
                    Add(arguments, root, "kwargs");
                }

                foreach (var argument in arguments)
                {
                    CountTags(argument, flow, tagCounts, failures, id);
                }

                try
                {
                    var decoder = new TestDecoder(anyPath: true);
                    foreach (var argument in arguments)
                    {
                        decoder.Decode(argument);
                    }

                    decoded++;
                }
                catch (Exception error) when (error is not JudgeNotBoundException)
                {
                    // host.py decodes inside its try, so the golden result is the Python error of the decoder.
                    decodeErrors++;
                    var expected = golden.TryGetValue(id, out var g) ? g : "(none)";
                    var actual = Codec.PythonExceptionName(error);
                    if (expected != actual)
                    {
                        failures.Add($"{id}: decoding throws {error.GetType().Name} ({actual}), golden {expected}: {error.Message}");
                    }
                }
            }
        }

        output.WriteLine($"cases: {cases}, decoded: {decoded}, decode errors that match the golden error: {decodeErrors - failures.Count(f => f.Contains("decoding throws", StringComparison.Ordinal))}");
        output.WriteLine("tags: " + string.Join(", ", tagCounts.Select(t => $"{t.Key} {t.Value}")));
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
    }

    private static void Add(List<JsonElement> list, JsonElement parent, string name)
    {
        if (parent.TryGetProperty(name, out var value))
        {
            list.Add(value);
        }
    }

    // Every key that starts with $ in an object with a tag must be a tag of the README. A plain dict with such a key
    // would also decode, as in Python, but no case has one, so each such key is flagged.
    private static void CountTags(JsonElement value, bool flow, SortedDictionary<string, int> counts, List<string> failures, string id)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                CountTags(item, flow, counts, failures, id);
            }
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
            {
                if (property.Name.StartsWith('$'))
                {
                    if (ArgumentTags.Contains(property.Name) || (flow && FlowTags.Contains(property.Name)))
                    {
                        counts[property.Name] = counts.GetValueOrDefault(property.Name) + 1;
                    }
                    else
                    {
                        failures.Add($"{id}: unknown tag {property.Name}");
                    }
                }

                CountTags(property.Value, flow, counts, failures, id);
            }
        }
    }

    private static Dictionary<string, string> GoldenResults()
    {
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Repository.Path("port", "judge", "golden"), "*.jsonl", SearchOption.AllDirectories))
        {
            foreach (var line in File.ReadLines(file).Where(l => l.Length > 0))
            {
                using var document = JsonDocument.Parse(line);
                var result = document.RootElement.GetProperty("result");
                if (result.TryGetProperty("error", out var error))
                {
                    results[document.RootElement.GetProperty("id").GetString()!] = error.GetString()!;
                }
            }
        }

        return results;
    }

    // The runner sorts each $set by json.dumps(v, sort_keys=True, ensure_ascii=False) before it records, and the codec
    // sorts with ensure_ascii=True. The two orders differ for text outside ASCII. Sort as the runner does.
    private static JsonNode? SortSets(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    var child = SortSets(obj[key]);
                    if (key == "$set" && child is JsonArray items)
                    {
                        var sorted = items.Select(i => i?.DeepClone())
                            .OrderBy(i => JudgeJson.Dumps(i, sortKeys: true, ensureAscii: false), Comparer<string>.Create(JudgeJson.CompareCodePoints))
                            .ToList();
                        child = new JsonArray([.. sorted]);
                    }

                    obj[key] = child is null ? null : child.Parent is null ? child : child.DeepClone();
                }

                return obj;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    var child = SortSets(array[index]);
                    array[index] = child is null ? null : child.Parent is null ? child : child.DeepClone();
                }

                return array;
            default:
                return node;
        }
    }

    private static string Cut(string text) => text.Length > 600 ? text[..600] + "..." : text;
}
