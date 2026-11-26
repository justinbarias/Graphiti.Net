// Evaluation test for graphiti_core/helpers.py, utils/datetime_utils.py, and utils/text_utils.py (port/RULES.md, R23
// and R25). values/helpers_values.json holds about 1,800 calls that port/tools/record_helpers_values.py recorded from
// Python. A datetime is {"$dt": isoformat}, a tuple is {"$tuple": [...]}, and a dict with a key that is not text is
// {"$dict": [[key, value], ...]}.
namespace Graphiti.Port.Evaluation;

using System.Text;
using Graphiti.Core;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;

public class HelpersValuesTests
{
    [Fact]
    public void The_port_gives_each_recorded_result_of_python()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "helpers_values.json");
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        var cases = (List<object?>)document["cases"]!;
        Assert.True(cases.Count >= 1700);

        List<string> failures = [];
        foreach (var item in cases)
        {
            var recorded = (OrderedDictionary<string, object?>)item!;
            var name = (string)recorded["f"]!;
            var args = (List<object?>)recorded["a"]!;
            var result = (OrderedDictionary<string, object?>)recorded["r"]!;
            var label = $"{name}({Clip(PyJson.Dumps(args))})";
            try
            {
                if (result.TryGetValue("err", out var err))
                {
                    var error = (List<object?>)err!;
                    var (type, message) = ((string)error[0]!, (string)error[1]!);
                    var thrown = Assert.ThrowsAny<Exception>(() => Invoke(name, args));
                    Assert.Equal(ExpectedExceptionType(type), thrown.GetType());

                    // PORT: PyText.EncodeUtf8 throws EncoderFallbackException with the text of .NET, where Python says
                    // "'utf-8' codec can't encode character ... surrogates not allowed". Only the type is compared (R25).
                    if (type != "UnicodeEncodeError")
                    {
                        Assert.Equal(message, thrown.Message);
                    }
                }
                else
                {
                    var actual = PyJson.Dumps(Encode(Invoke(name, args)));
                    var expected = PyJson.Dumps(result["ok"]);
                    if (actual != expected)
                    {
                        var at = 0;
                        while (at < expected.Length && at < actual.Length && expected[at] == actual[at])
                        {
                            at++;
                        }

                        failures.Add($"{label}: the results differ at character {at}: expected ...{expected[Math.Max(0, at - 40)..Math.Min(expected.Length, at + 60)]} but got ...{actual[Math.Max(0, at - 40)..Math.Min(actual.Length, at + 60)]}");
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

    private static string Clip(string text) => text.Length > 160 ? text[..160] + "..." : text;

    private static Type ExpectedExceptionType(string python) => python switch
    {
        "ValueError" => typeof(ArgumentException),
        "GroupIdValidationError" => typeof(GroupIdValidationException),
        "NodeLabelValidationError" => typeof(NodeLabelValidationException),
        "OverflowError" => typeof(OverflowException),
        "UnicodeEncodeError" => typeof(EncoderFallbackException),
        _ => throw new InvalidOperationException($"No C# type for {python}"),
    };

    private static object? Invoke(string name, List<object?> a) => name switch
    {
        "lucene_sanitize" => Helpers.LuceneSanitize((string)a[0]!),
        "validate_group_id" => Helpers.ValidateGroupId((string?)a[0]),
        "validate_group_ids" => Helpers.ValidateGroupIds(Strings(a[0])),
        "validate_node_labels" => Helpers.ValidateNodeLabels(Strings(a[0])),
        "validate_excluded_entity_types" => Helpers.ValidateExcludedEntityTypes(Strings(a[0]), Types(a[1])),
        "normalize_l2" => Helpers.NormalizeL2([.. ((List<object?>)a[0]!).Select(x => (double)x!)]),
        "parse_db_date" => Helpers.ParseDbDate(Decode(a[0])),
        "ensure_utc" => DatetimeUtils.EnsureUtc((PyDateTime?)Decode(a[0])),
        "convert_datetimes_to_strings" => DatetimeUtils.ConvertDatetimesToStrings(Decode(a[0])),
        "truncate_utf8_to_bytes" => TextUtils.TruncateUtf8ToBytes((string)a[0]!, (long)a[1]!),
        "truncate_at_sentence" => TextUtils.TruncateAtSentence((string)a[0]!, (long)a[1]!),
        "concatenate_episodes" => TextUtils.ConcatenateEpisodes(Episodes(a[0])),
        "concatenate_timeline" => TextUtils.ConcatenateTimeline(Episodes(a[0]), Episodes(a[1])),
        _ => throw new InvalidOperationException($"No binding for {name}"),
    };

    private static List<string>? Strings(object? value) => value is null ? null : [.. ((List<object?>)value).Select(x => (string)x!)];

    // The recording lists the names of the custom types. Python passes pydantic classes, and the port a descriptor with no fields.
    private static OrderedDictionary<string, GraphTypeDescriptor>? Types(object? value)
    {
        if (value is null)
        {
            return null;
        }

        OrderedDictionary<string, GraphTypeDescriptor> types = new(StringComparer.Ordinal);
        foreach (var name in Strings(value)!)
        {
            types[name] = new GraphTypeDescriptor(name, null, []);
        }

        return types;
    }

    private static List<EpisodicNode> Episodes(object? value) =>
    [
        .. ((List<object?>)value!).Select(item =>
        {
            var episode = (OrderedDictionary<string, object?>)item!;
            return new EpisodicNode
            {
                Uuid = (string)episode["uuid"]!,
                Name = "n",
                GroupId = "g",
                Source = EpisodeType.Message,
                SourceDescription = "d",
                Content = (string)episode["content"]!,
                ValidAt = (PyDateTime)Decode(episode["valid_at"])!,
            };
        }),
    ];

    // A recorded argument as a value of the model of R6.
    private static object? Decode(object? value)
    {
        switch (value)
        {
            case List<object?> list:
                return list.Select(Decode).ToList();
            case OrderedDictionary<string, object?> dict:
                if (dict.Count == 1 && dict.TryGetValue("$dt", out var dt))
                {
                    return PyDateTime.FromIsoFormat((string)dt!);
                }

                if (dict.Count == 1 && dict.TryGetValue("$tuple", out var tuple))
                {
                    return PyTuple.From(((List<object?>)tuple!).Select(Decode));
                }

                if (dict.Count == 1 && dict.TryGetValue("$dict", out var pairs))
                {
                    PyDict result = new();
                    foreach (var pair in (List<object?>)pairs!)
                    {
                        var kv = (List<object?>)pair!;
                        result[Decode(kv[0])] = Decode(kv[1]);
                    }

                    return result;
                }

                OrderedDictionary<string, object?> plain = new(StringComparer.Ordinal);
                foreach (var (k, v) in dict)
                {
                    plain[k] = Decode(v);
                }

                return plain;
            default:
                return value;
        }
    }

    // A result in the form of the recording.
    private static object? Encode(object? value)
    {
        switch (value)
        {
            case PyDateTime dt:
                return new OrderedDictionary<string, object?>(StringComparer.Ordinal) { ["$dt"] = dt.IsoFormat() };
            case PyTuple tuple:
                return new OrderedDictionary<string, object?>(StringComparer.Ordinal) { ["$tuple"] = tuple.Select(Encode).ToList() };
            case PyDict dict:
                return new OrderedDictionary<string, object?>(StringComparer.Ordinal) { ["$dict"] = dict.Select(e => (object?)new List<object?> { Encode(e.Key), Encode(e.Value) }).ToList() };
            case OrderedDictionary<string, object?> plain:
                OrderedDictionary<string, object?> result = new(StringComparer.Ordinal);
                foreach (var (k, v) in plain)
                {
                    result[k] = Encode(v);
                }

                return result;
            case List<double> doubles:
                return doubles.Cast<object?>().ToList();
            case List<object?> list:
                return list.Select(Encode).ToList();
            default:
                return value;
        }
    }
}

// Behavior that the judge cannot pin: the limit and the order of semaphore_gather (R8, GAPS J10). The upstream tests do not cover it.
public class SemaphoreGatherTests
{
    private static async Task<(List<long> Results, long MaxInFlight)> Run(long count, long? limit)
    {
        long inFlight = 0;
        long maxInFlight = 0;
        List<Func<Task<long>>> calls = [];
        for (long i = 0; i < count; i++)
        {
            var index = i;
            calls.Add(async () =>
            {
                inFlight++;
                maxInFlight = Math.Max(maxInFlight, inFlight);
                for (var turn = 0; turn < 4; turn++)
                {
                    await Task.Yield();
                }

                inFlight--;
                return index;
            });
        }

        var results = await Asyncio.Run(() => Helpers.SemaphoreGatherAsync(calls, limit));
        return (results, maxInFlight);
    }

    [Fact]
    public async Task semaphore_gather_returns_results_in_input_order_and_bounds_the_calls()
    {
        var (results, maxInFlight) = await Run(10, 3);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => (long)i).ToList(), results);
        Assert.Equal(3, maxInFlight);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task semaphore_gather_uses_the_semaphore_limit_for_none_and_zero(long? limit)
    {
        var (results, maxInFlight) = await Run(Helpers.SemaphoreLimit + 5, limit);
        Assert.Equal(Helpers.SemaphoreLimit + 5, results.Count);
        Assert.Equal(Helpers.SemaphoreLimit, maxInFlight);
    }

    [Fact]
    public async Task semaphore_gather_of_four_calls_returns_a_tuple()
    {
        var (a, b, c, d) = await Asyncio.Run(() => Helpers.SemaphoreGatherAsync(
            () => Task.FromResult(1L),
            () => Task.FromResult("two"),
            () => Task.FromResult(3.5),
            () => Task.FromResult(true),
            2));
        Assert.Equal(1L, a);
        Assert.Equal("two", b);
        Assert.Equal(3.5, c);
        Assert.True(d);
    }

    [Fact]
    public async Task semaphore_gather_without_results_runs_each_call()
    {
        long count = 0;
        List<Func<Task>> calls = [];
        for (var i = 0; i < 5; i++)
        {
            calls.Add(async () =>
            {
                await Task.Yield();
                count++;
            });
        }

        await Asyncio.Run(() => Helpers.SemaphoreGatherAsync(calls, 2));
        Assert.Equal(5, count);
    }
}

// Ported from tests/helpers_test.py. Only test_lucene_sanitize has code under test in graphiti_core/helpers.py. The
// other code of the file is a fixture for the driver tests, and the unit of the driver ports it.
public class HelpersTest
{
    [Fact]
    public void test_lucene_sanitize()
    {
        (string Query, string Expected)[] queries =
        [
            (
                "This has every escape character + - && || ! ( ) { } [ ] ^ \" ~ * ? : \\ /",
                "\\This has every escape character \\+ \\- \\&\\& \\|\\| \\! \\( \\) \\{ \\} \\[ \\] \\^ \\\" \\~ \\* \\? \\: \\\\ \\/"),
            ("this has no escape characters", "this has no escape characters"),
        ];

        foreach (var (query, expected) in queries)
        {
            Assert.Equal(expected, Helpers.LuceneSanitize(query));
        }
    }
}

// Ported from tests/test_entity_exclusion_int.py. Only the two tests that need no driver. The others wait for the
// units of graphiti.py and of utils/maintenance/node_operations.py.
public class TestEntityExclusionValidation
{
    private static OrderedDictionary<string, GraphTypeDescriptor> EntityTypes() => new(StringComparer.Ordinal)
    {
        ["Person"] = new GraphTypeDescriptor("Person", null, []),
        ["Organization"] = new GraphTypeDescriptor("Organization", null, []),
    };

    [Fact]
    public void test_validation_valid_excluded_types()
    {
        var entityTypes = EntityTypes();

        // Valid exclusions
        Assert.True(Helpers.ValidateExcludedEntityTypes(["Entity"], entityTypes));
        Assert.True(Helpers.ValidateExcludedEntityTypes(["Person"], entityTypes));
        Assert.True(Helpers.ValidateExcludedEntityTypes(["Entity", "Person"], entityTypes));
        Assert.True(Helpers.ValidateExcludedEntityTypes(null, entityTypes));
        Assert.True(Helpers.ValidateExcludedEntityTypes([], entityTypes));
    }

    [Fact]
    public void test_validation_invalid_excluded_types()
    {
        var entityTypes = EntityTypes();

        // Invalid exclusions should raise ValueError
        var first = Assert.Throws<ArgumentException>(() => Helpers.ValidateExcludedEntityTypes(["InvalidType"], entityTypes));
        Assert.Contains("Invalid excluded entity types", first.Message, StringComparison.Ordinal);

        var second = Assert.Throws<ArgumentException>(() => Helpers.ValidateExcludedEntityTypes(["Person", "NonExistentType"], entityTypes));
        Assert.Contains("Invalid excluded entity types", second.Message, StringComparison.Ordinal);
    }
}

// Ported from tests/test_text_utils.py.
public class TestTextUtils
{
    [Fact]
    public void test_truncate_at_sentence_short_text()
    {
        // Test that short text is returned unchanged.
        var text = "This is a short sentence.";
        var result = TextUtils.TruncateAtSentence(text, 100);
        Assert.Equal(text, result);
    }

    [Fact]
    public void test_truncate_at_sentence_empty()
    {
        // Test that empty text is handled correctly.
        Assert.Equal(string.Empty, TextUtils.TruncateAtSentence(string.Empty, 100));

        // TODO(port): truncate_at_sentence(None, 100) is None. The parameter is string, which cannot hold null (open item O4).
    }

    [Fact]
    public void test_truncate_at_sentence_exact_length()
    {
        // Test text at exactly max_chars.
        var text = new string('A', 100);
        var result = TextUtils.TruncateAtSentence(text, 100);
        Assert.Equal(text, result);
    }

    [Fact]
    public void test_truncate_at_sentence_with_period()
    {
        // Test truncation at sentence boundary with period.
        var text = "First sentence. Second sentence. Third sentence. Fourth sentence.";
        var result = TextUtils.TruncateAtSentence(text, 40);
        Assert.Equal("First sentence. Second sentence.", result);
        Assert.True(Py.Len(result) <= 40);
    }

    [Fact]
    public void test_truncate_at_sentence_with_question()
    {
        // Test truncation at sentence boundary with question mark.
        var text = "What is this? This is a test. More text here.";
        var result = TextUtils.TruncateAtSentence(text, 30);
        Assert.Equal("What is this? This is a test.", result);
        Assert.True(Py.Len(result) <= 32);
    }

    [Fact]
    public void test_truncate_at_sentence_with_exclamation()
    {
        // Test truncation at sentence boundary with exclamation mark.
        var text = "Hello world! This is exciting. And more text.";
        var result = TextUtils.TruncateAtSentence(text, 30);
        Assert.Equal("Hello world! This is exciting.", result);
        Assert.True(Py.Len(result) <= 32);
    }

    [Fact]
    public void test_truncate_at_sentence_no_boundary()
    {
        // Test truncation when no sentence boundary exists before max_chars.
        var text = "This is a very long sentence without any punctuation marks near the beginning";
        var result = TextUtils.TruncateAtSentence(text, 30);
        Assert.True(Py.Len(result) <= 30);
        Assert.StartsWith("This is a very long sentence", result, StringComparison.Ordinal);
    }

    [Fact]
    public void test_truncate_at_sentence_multiple_periods()
    {
        // Test with multiple sentence endings.
        var text = "A. B. C. D. E. F. G. H.";
        var result = TextUtils.TruncateAtSentence(text, 10);
        Assert.Equal("A. B. C.", result);
        Assert.True(Py.Len(result) <= 10);
    }

    [Fact]
    public void test_truncate_at_sentence_strips_trailing_whitespace()
    {
        // Test that trailing whitespace is stripped.
        var text = "First sentence.   Second sentence.";
        var result = TextUtils.TruncateAtSentence(text, 20);
        Assert.Equal("First sentence.", result);
        Assert.False(result.EndsWith(' '));
    }

    [Fact]
    public void test_max_summary_chars_constant()
    {
        // Test that MAX_SUMMARY_CHARS is set to expected value.
        Assert.Equal(1000, TextUtils.MaxSummaryChars);
    }

    [Fact]
    public void test_saga_summary_char_constants()
    {
        Assert.Equal(4000, TextUtils.SagaSummaryMaxChars);
    }

    [Fact]
    public void test_truncate_at_sentence_realistic_summary()
    {
        // Test with a realistic entity summary.
        var text =
            "John is a software engineer who works at a tech company in San Francisco. " +
            "He has been programming for over 10 years and specializes in Python and distributed systems. " +
            "John enjoys hiking on weekends and is learning to play guitar. " +
            "He graduated from MIT with a degree in computer science.";
        var result = TextUtils.TruncateAtSentence(text, TextUtils.MaxSummaryChars);
        Assert.True(Py.Len(result) <= TextUtils.MaxSummaryChars);

        // Should keep complete sentences
        Assert.EndsWith(".", result, StringComparison.Ordinal);

        // Should include at least the first sentence
        Assert.Contains("John is a software engineer", result, StringComparison.Ordinal);
    }

    [Fact]
    public void test_truncate_utf8_to_bytes_preserves_runes()
    {
        var text = new string('é', 10);
        var result = TextUtils.TruncateUtf8ToBytes(text, 7);
        var encoded = Encoding.UTF8.GetBytes(result);
        Assert.True(encoded.Length <= 7);
        Assert.Equal(new string('é', 3), result);
    }

    [Fact]
    public void test_truncate_utf8_to_bytes_empty_on_non_positive_budget()
    {
        Assert.Equal(string.Empty, TextUtils.TruncateUtf8ToBytes("abc", 0));
        Assert.Equal(string.Empty, TextUtils.TruncateUtf8ToBytes("abc", -1));
    }
}

// Ported from tests/utils/test_concatenate_episodes.py. _make_episode builds a MagicMock there, and an EpisodicNode here.
public static class ConcatenateEpisodesSupport
{
    internal static EpisodicNode MakeEpisode(string content, PyDateTime? validAt = null) => new()
    {
        Name = "n",
        GroupId = "g",
        Source = EpisodeType.Message,
        SourceDescription = "d",
        Content = content,
        ValidAt = validAt ?? new PyDateTime(2025, 1, 1, 12, 0, 0, offsetMicroseconds: 0),
    };

    internal static PyDateTime Utc(int year, int month, int day, int hour, int minute, int second) =>
        new(year, month, day, hour, minute, second, offsetMicroseconds: 0);
}

public class TestConcatenateEpisodes
{
    [Fact]
    public void test_single_episode_returns_content_as_is()
    {
        var ep = ConcatenateEpisodesSupport.MakeEpisode("Hello world");
        Assert.Equal("Hello world", TextUtils.ConcatenateEpisodes([ep]));
    }

    [Fact]
    public void test_multiple_episodes_adds_headers_with_timestamps()
    {
        List<EpisodicNode> eps =
        [
            ConcatenateEpisodesSupport.MakeEpisode("First", ConcatenateEpisodesSupport.Utc(2025, 1, 1, 10, 0, 0)),
            ConcatenateEpisodesSupport.MakeEpisode("Second", ConcatenateEpisodesSupport.Utc(2025, 1, 1, 10, 5, 0)),
            ConcatenateEpisodesSupport.MakeEpisode("Third", ConcatenateEpisodesSupport.Utc(2025, 1, 1, 10, 10, 0)),
        ];
        var result = TextUtils.ConcatenateEpisodes(eps);
        Assert.Contains("[Episode 0] (timestamp: 2025-01-01T10:00:00+00:00)\nFirst", result, StringComparison.Ordinal);
        Assert.Contains("[Episode 1] (timestamp: 2025-01-01T10:05:00+00:00)\nSecond", result, StringComparison.Ordinal);
        Assert.Contains("[Episode 2] (timestamp: 2025-01-01T10:10:00+00:00)\nThird", result, StringComparison.Ordinal);
    }

    [Fact]
    public void test_multiple_episodes_separated_by_blank_line()
    {
        var ts = ConcatenateEpisodesSupport.Utc(2025, 6, 15, 8, 0, 0);
        List<EpisodicNode> eps = [ConcatenateEpisodesSupport.MakeEpisode("A", ts), ConcatenateEpisodesSupport.MakeEpisode("B", ts)];
        var result = TextUtils.ConcatenateEpisodes(eps);
        var tsStr = ts.IsoFormat();
        Assert.Equal($"[Episode 0] (timestamp: {tsStr})\nA\n\n[Episode 1] (timestamp: {tsStr})\nB", result);
    }

    [Fact]
    public void test_single_episode_no_header()
    {
        var ep = ConcatenateEpisodesSupport.MakeEpisode("No header please");
        var result = TextUtils.ConcatenateEpisodes([ep]);
        Assert.DoesNotContain("[Episode", result, StringComparison.Ordinal);
    }

    // TODO(port): test_multiple_episodes_with_none_valid_at. It sets valid_at to None, and EpisodicNode.ValidAt is a
    // PyDateTime, which cannot hold null. Without a nullable property, the branch of concatenate_episodes that writes
    // "unknown" cannot run.
}

public class TestConcatenateTimeline
{
    [Fact]
    public void test_numbers_extraction_targets_and_marks_context_turns()
    {
        var a = ConcatenateEpisodesSupport.MakeEpisode("user says hi", ConcatenateEpisodesSupport.Utc(2025, 1, 1, 10, 0, 0));
        a.Uuid = "a";
        var b = ConcatenateEpisodesSupport.MakeEpisode("assistant replies", ConcatenateEpisodesSupport.Utc(2025, 1, 1, 10, 1, 0));
        b.Uuid = "b";
        var c = ConcatenateEpisodesSupport.MakeEpisode("user follows up", ConcatenateEpisodesSupport.Utc(2025, 1, 1, 10, 2, 0));
        c.Uuid = "c";

        var result = TextUtils.ConcatenateTimeline([a, c], [a, b, c]);

        var episode0 = result.IndexOf("[Episode 0] (timestamp: 2025-01-01T10:00:00+00:00)", StringComparison.Ordinal);
        var context = result.IndexOf("[CONTEXT EPISODE] (timestamp: 2025-01-01T10:01:00+00:00)", StringComparison.Ordinal);
        var episode1 = result.IndexOf("[Episode 1] (timestamp: 2025-01-01T10:02:00+00:00)", StringComparison.Ordinal);
        Assert.True(episode0 >= 0 && context >= 0 && episode1 >= 0);
        Assert.True(episode0 < context && context < episode1);
        Assert.Contains("user says hi", result, StringComparison.Ordinal);
        Assert.Contains("assistant replies", result, StringComparison.Ordinal);
        Assert.Contains("user follows up", result, StringComparison.Ordinal);
    }
}
