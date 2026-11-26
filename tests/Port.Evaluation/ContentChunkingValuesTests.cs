// Evaluation test for graphiti_core/utils/content_chunking.py (port/RULES.md, R23 and R25).
// values/content_chunking.json holds about 1,500 calls that port/tools/record_chunking_values.py recorded from Python.
namespace Graphiti.Port.Evaluation;

using System.Collections;
using System.Runtime.CompilerServices;
using Graphiti.Core;
using Graphiti.Core.Utils;
using Graphiti.PyCompat;

public class ContentChunkingValuesTests
{
    [Fact]
    public void The_port_gives_each_recorded_result_of_python()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "content_chunking.json");
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        var cases = (List<object?>)document["cases"]!;
        Assert.True(cases.Count >= 1500);

        List<string> failures = [];
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            var name = (string)recorded["f"]!;
            var args = (List<object?>)recorded["a"]!;
            var result = (OrderedDictionary<string, object?>)recorded["r"]!;
            var label = $"{name}({PyJson.Dumps(args)})";
            try
            {
                if (result.TryGetValue("err", out var err))
                {
                    var error = (List<object?>)err!;
                    Assert.Equal("ValueError", error[0]);
                    var thrown = Assert.Throws<ArgumentException>(() => Invoke(name, args));
                    Assert.Equal((string)error[1]!, thrown.Message);
                }
                else
                {
                    var actual = Normalize(Invoke(name, args));
                    var expected = result["ok"];
                    if (PyJson.Dumps(actual) != PyJson.Dumps(expected))
                    {
                        var want = PyJson.Dumps(expected);
                        var got = PyJson.Dumps(actual);
                        var at = 0;
                        while (at < want.Length && at < got.Length && want[at] == got[at])
                        {
                            at++;
                        }

                        failures.Add($"{name}: the results differ at character {at}: expected ...{want[Math.Max(0, at - 40)..Math.Min(want.Length, at + 60)]} but got ...{got[Math.Max(0, at - 40)..Math.Min(got.Length, at + 60)]}");
                    }
                }
            }
            catch (Exception error)
            {
                failures.Add($"{label}: {error.GetType().Name}: {error.Message}");
            }
        }

        Assert.True(failures.Count == 0, $"{failures.Count} recorded results differ. The first ones:\n{string.Join("\n", failures.Take(5))}");
    }

    private static object? Invoke(string name, List<object?> a) => name switch
    {
        "estimate_tokens" => ContentChunking.EstimateTokens(Str(a[0])),
        "_tokens_to_chars" => ContentChunking.TokensToChars(Long(a[0])),
        "should_chunk" => ContentChunking.ShouldChunk(Str(a[0]), PyEnum.FromValue<EpisodeType>(a[1])),
        "_estimate_high_density" => ContentChunking.EstimateHighDensity(Str(a[0]), PyEnum.FromValue<EpisodeType>(a[1]), Long(a[2])),
        "_json_likely_dense" => ContentChunking.JsonLikelyDense(Str(a[0]), Long(a[1])),
        "_count_json_keys" => ContentChunking.CountJsonKeys(Dict(a[0]), Long(a[1]), Long(a[2])),
        "_text_likely_dense" => ContentChunking.TextLikelyDense(Str(a[0]), Long(a[1])),
        "chunk_json_content" => ContentChunking.ChunkJsonContent(Str(a[0]), NullableLong(a[1]), NullableLong(a[2])),
        "_chunk_json_array" => ContentChunking.ChunkJsonArray(List(a[0]), Long(a[1]), Long(a[2])),
        "_get_overlap_elements" => ContentChunking.GetOverlapElements(List(a[0]), Long(a[1])),
        "_chunk_json_object" => ContentChunking.ChunkJsonObject(Dict(a[0]), Long(a[1]), Long(a[2])),
        "_get_overlap_dict" => ContentChunking.GetOverlapDict(Dict(a[0]), List(a[1]).Cast<string>().ToList(), Long(a[2])),
        "chunk_text_content" => ContentChunking.ChunkTextContent(Str(a[0]), NullableLong(a[1]), NullableLong(a[2])),
        "_chunk_by_sentences" => ContentChunking.ChunkBySentences(Str(a[0]), Long(a[1]), Long(a[2])),
        "_chunk_by_size" => ContentChunking.ChunkBySize(Str(a[0]), Long(a[1]), Long(a[2])),
        "_get_overlap_text" => ContentChunking.GetOverlapText(Str(a[0]), Long(a[1])),
        "chunk_message_content" => ContentChunking.ChunkMessageContent(Str(a[0]), NullableLong(a[1]), NullableLong(a[2])),
        "_chunk_message_array" => ContentChunking.ChunkMessageArray(List(a[0]), Long(a[1]), Long(a[2])),
        "_chunk_speaker_messages" => ContentChunking.ChunkSpeakerMessages(Str(a[0]), Long(a[1]), Long(a[2])),
        "_get_overlap_messages" => ContentChunking.GetOverlapMessages(List(a[0]).Cast<string>().ToList(), Long(a[1])),
        "_chunk_by_lines" => ContentChunking.ChunkByLines(Str(a[0]), Long(a[1]), Long(a[2])),
        "generate_covering_chunks" => ContentChunking.GenerateCoveringChunks(List(a[0]), Long(a[1])),
        _ => throw new InvalidOperationException($"No binding for {name}"),
    };

    private static string Str(object? value) => (string)value!;

    private static long Long(object? value) => (long)value!;

    private static long? NullableLong(object? value) => value is null ? null : (long)value;

    private static List<object?> List(object? value) => (List<object?>)value!;

    private static OrderedDictionary<string, object?> Dict(object? value) => (OrderedDictionary<string, object?>)value!;

    // A result as a value of the model of R6, so that PyJson.Dumps writes it: a tuple and a typed list become a list.
    private static object? Normalize(object? value) => value switch
    {
        null or string or bool or long or double => value,
        OrderedDictionary<string, object?> dict => NormalizeDict(dict),
        ITuple tuple => Enumerable.Range(0, tuple.Length).Select(i => Normalize(tuple[i])).ToList(),
        IEnumerable items => items.Cast<object?>().Select(Normalize).ToList(),
        _ => throw new InvalidOperationException($"No normal form for {value.GetType().Name}"),
    };

    private static OrderedDictionary<string, object?> NormalizeDict(OrderedDictionary<string, object?> dict)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, entry) in dict)
        {
            result[key] = Normalize(entry);
        }

        return result;
    }
}
