namespace Graphiti.PyCompat.Tests;

using System.Text.Json;
using System.Text.RegularExpressions;
using CsCheck;
using Graphiti.PyCompat;
using static Graphiti.PyCompat.Tests.PythonValues;

/// <summary>
/// The values that real Python gave for <c>PyRegex</c> and for <c>PyText.Replace</c>, <c>LStrip(s, chars)</c>, and
/// <c>Count</c>, read from <c>python-values.regex.json</c>. <c>port/tools/generate_regex_values.py</c> writes the file.
/// </summary>
internal static class RegexValues
{
    internal const int CodePointCount = 0x110000;

    private static readonly JsonDocument Document = JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "python-values.regex.json")));

    internal static List<JsonElement> RegexCases(string name) =>
        [.. Document.RootElement.GetProperty("cases").GetProperty(name).EnumerateArray()];

    /// <summary>One flag for each code point: does Python's class match that code point on its own.</summary>
    internal static bool[] ClassSet(string name)
    {
        var flags = new bool[CodePointCount];
        var ranges = Ints(Document.RootElement.GetProperty("classes").GetProperty(name));
        for (var i = 0; i < ranges.Length; i += 2)
        {
            for (var cp = ranges[i]; cp <= ranges[i + 1]; cp++)
            {
                flags[cp] = true;
            }
        }

        return flags;
    }

    internal static JsonElement Boundary => Document.RootElement.GetProperty("boundary");
}

public class RegexTests
{
    private const RegexOptions Compiled = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static Regex Rx(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.CultureInvariant);

    // ---- the classes, for each code point ----

    private static void AssertClassMatchesPython(string name, string netPattern)
    {
        var expected = RegexValues.ClassSet(name);
        var regex = new Regex(@"\A" + netPattern + @"\z", Compiled);
        var wrong = new List<string>();
        Span<char> text = stackalloc char[2];
        for (var cp = 0; cp < RegexValues.CodePointCount; cp++)
        {
            var length = 1;
            if (cp >= 0x10000)
            {
                text[0] = (char)(0xD800 + ((cp - 0x10000) >> 10));
                text[1] = (char)(0xDC00 + ((cp - 0x10000) & 0x3FF));
                length = 2;
            }
            else
            {
                text[0] = (char)cp;
            }

            if (regex.IsMatch(text[..length]) != expected[cp])
            {
                wrong.Add($"U+{cp:X4}");
            }
        }

        Assert.True(wrong.Count == 0, $"{name} differs from Python at {wrong.Count} code points, first {string.Join(" ", wrong.Take(10))}");
        Assert.True(expected.Any(e => e) && expected.Any(e => !e), "the recorded set must hold both kinds");
    }

    [Fact]
    public void Space_matches_Python_for_each_code_point() => AssertClassMatchesPython("space", PyRegex.Space);

    [Fact]
    public void NotSpace_matches_Python_for_each_code_point() => AssertClassMatchesPython("not_space", PyRegex.NotSpace);

    [Fact]
    public void Word_matches_Python_for_each_code_point() => AssertClassMatchesPython("word", PyRegex.Word);

    [Fact]
    public void NotWord_matches_Python_for_each_code_point() => AssertClassMatchesPython("not_word", PyRegex.NotWord);

    [Fact]
    public void Digit_matches_Python_for_each_code_point() => AssertClassMatchesPython("digit", PyRegex.Digit);

    [Fact]
    public void SpaceClassBody_inside_a_class_matches_Python_for_each_BMP_code_point()
    {
        var expected = RegexValues.ClassSet("space");
        var regex = new Regex("\\A[" + PyRegex.SpaceClassBody + "]\\z", Compiled);
        var wrong = new List<string>();
        for (var cp = 0; cp < 0x10000; cp++)
        {
            if (regex.IsMatch([(char)cp]) != expected[cp])
            {
                wrong.Add($"U+{cp:X4}");
            }
        }

        Assert.True(wrong.Count == 0, $"differs at {string.Join(" ", wrong.Take(10))}");
    }

    private static int CodePointIndex(ReadOnlySpan<char> text, int index)
    {
        var count = 0;
        for (var i = 0; i < index; i++)
        {
            if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                i++;
            }

            count++;
        }

        return count;
    }

    [Fact]
    public void Boundary_matches_Python_in_probe_strings_for_each_code_point()
    {
        var boundary = RegexValues.Boundary;
        var probes = boundary.GetProperty("probes").EnumerateArray()
            .Select(p => p.EnumerateArray().Select(t => t.GetString()!).ToArray()).ToArray();
        var regex = new Regex(PyRegex.Boundary, Compiled);
        var wrong = new List<string>();
        var checkedCodePoints = 0;
        var positions = new List<int>();
        foreach (var run in boundary.GetProperty("runs").EnumerateArray())
        {
            var first = run[0].GetInt32();
            var last = run[1].GetInt32();
            var masks = Ints(run[2]);
            for (var cp = first; cp <= last; cp++)
            {
                checkedCodePoints++;
                var c = PyText.FromCodePoints([cp]);
                for (var k = 0; k < probes.Length; k++)
                {
                    var text = string.Concat(probes[k].Select(t => t == "C" ? c : t));
                    positions.Clear();
                    foreach (var m in regex.EnumerateMatches(text))
                    {
                        positions.Add(CodePointIndex(text, m.Index));
                    }

                    var actual = positions.Aggregate(0, (mask, p) => mask | (1 << p));
                    if (actual != masks[k])
                    {
                        wrong.Add($"U+{cp:X4} in probe {k}");
                    }
                }
            }
        }

        Assert.Equal(RegexValues.CodePointCount, checkedCodePoints);
        Assert.True(wrong.Count == 0, $"\\b differs from Python in {wrong.Count} cases, first {string.Join(", ", wrong.Take(10))}");
    }

    // ---- re.split ----

    private static readonly string SpeakerStart = "^[A-Za-z_][A-Za-z0-9_" + PyRegex.SpaceClassBody + "]*:";

    // Each entry is the hand translation of the Python pattern that has the same name in the generator.
    private static readonly Dictionary<string, Regex> SplitRegexes = new()
    {
        // r'\n\s*\n'
        ["paragraph"] = Rx("\n" + PyRegex.Space + "*\n"),
        // r'(?<=[.!?])\s+'
        ["sentence"] = Rx("(?<=[.!?])" + PyRegex.Space + "+"),
        // r'^([A-Za-z_][A-Za-z0-9_\s]*):(.+?)(?=^[A-Za-z_][A-Za-z0-9_\s]*:|$)' with re.MULTILINE | re.DOTALL
        ["speaker_search"] = Rx(
            "^([A-Za-z_][A-Za-z0-9_" + PyRegex.SpaceClassBody + "]*):(.+?)(?=" + SpeakerStart + "|$)",
            RegexOptions.Multiline | RegexOptions.Singleline),
        // r'(?=^[A-Za-z_][A-Za-z0-9_\s]*:)' with re.MULTILINE
        ["speaker_split"] = Rx("(?=" + SpeakerStart + ")", RegexOptions.Multiline),
        ["comma_group"] = Rx("(,)"),
        ["optional_groups"] = Rx("(a)|(b)"),
        ["two_groups"] = Rx("(a)(b)?"),
        ["star_x"] = Rx("x*"),
        ["empty"] = Rx(string.Empty),
        ["lazy_a"] = Rx("a*?"),
        // r'x*  # a comment at the end' with re.VERBOSE
        ["verbose"] = Rx("x*  # a comment at the end", RegexOptions.IgnorePatternWhitespace),
        ["empty_first"] = Rx("|a"),
        // r'\b'
        ["boundary"] = Rx(PyRegex.Boundary),
        // r'(?<=,)\s*'
        ["lookbehind_comma"] = Rx("(?<=,)" + PyRegex.Space + "*"),
        // r'(\d+)'
        ["digits"] = Rx("(" + PyRegex.Digit + "+)"),
        // r'(\w+)'
        ["word_runs"] = Rx("(" + PyRegex.Word + "+)"),
        // r'(\S+)'
        ["non_space_runs"] = Rx("(" + PyRegex.NotSpace + "+)"),
        // r'(\W)'
        ["non_word"] = Rx("(" + PyRegex.NotWord + ")"),
    };

    private static string Describe(IEnumerable<string?> items) =>
        "[" + string.Join(", ", items.Select(i => i is null ? "None" : "'" + string.Join(" ", PyText.ToCodePoints(i).Select(c => c.ToString("X", System.Globalization.CultureInfo.InvariantCulture))) + "'")) + "]";

    [Fact]
    public void Split_matches_Python()
    {
        var cases = RegexValues.RegexCases("split");
        Assert.True(cases.Count > 5000);
        foreach (var name in SplitRegexes.Keys)
        {
            Assert.True(cases.Count(c => c.GetProperty("p").GetString() == name) >= 120, name);
        }

        foreach (var name in new[] { "paragraph", "sentence", "speaker_search", "speaker_split" })
        {
            Assert.True(cases.Count(c => c.GetProperty("p").GetString() == name && c.GetProperty("n").GetInt64() == 0) >= 200, name);
        }

        foreach (var c in cases)
        {
            var name = c.GetProperty("p").GetString()!;
            var text = Text(c, "s");
            var actual = PyRegex.Split(SplitRegexes[name], text, c.GetProperty("n").GetInt64());
            var expected = c.GetProperty("r").EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Null ? null : FromCodePoints(e)).ToList();
            Assert.True(
                expected.SequenceEqual(actual),
                $"{name} maxsplit {c.GetProperty("n")} on {Describe([text])}: expected {Describe(expected)}, got {Describe(actual)}");
        }
    }

    [Fact]
    public void Split_with_a_pattern_that_does_not_match_returns_the_text()
    {
        Assert.Equal(["abc"], PyRegex.Split(Rx("z"), "abc"));
        Assert.Equal([string.Empty], PyRegex.Split(Rx("z"), string.Empty));
    }

    // ---- $ of GAPS F7 ----

    [Fact]
    public void Dollar_before_a_final_line_break_matches_Python()
    {
        var label = Rx("^[A-Za-z_][A-Za-z0-9_]*$");
        var groupId = Rx("\\A[a-zA-Z0-9_-]+$");
        var dollar = Rx("$");
        var dollarMultiline = Rx("$", RegexOptions.Multiline);
        var sawFinalLineBreak = false;
        foreach (var c in RegexValues.RegexCases("dollar"))
        {
            var text = Text(c, "s");
            var shown = Describe([text]);
            Assert.True(c.GetProperty("label").GetBoolean() == label.IsMatch(text), "label " + shown);
            Assert.True(c.GetProperty("group_id").GetBoolean() == groupId.IsMatch(text), "group id " + shown);
            Assert.Equal(Ints(c.GetProperty("dollar")), dollar.Matches(text).Select(m => PyText.ToCodePoints(text[..m.Index]).Length).ToArray());
            Assert.Equal(
                Ints(c.GetProperty("dollar_multiline")),
                dollarMultiline.Matches(text).Select(m => PyText.ToCodePoints(text[..m.Index]).Length).ToArray());
            sawFinalLineBreak |= text == "abc\n" && c.GetProperty("label").GetBoolean();
        }

        Assert.True(sawFinalLineBreak, "F7: Python accepts the label with a final line break");
    }

    // ---- PyText.Replace, LStrip(s, chars), Count ----

    [Fact]
    public void Replace_with_a_count_matches_Python() =>
        CheckRegexCases("replace", c => PyText.Replace(Text(c, "s"), Text(c, "old"), Text(c, "new"), c.GetProperty("n").GetInt64()));

    [Fact]
    public void LStrip_with_chars_matches_Python() =>
        CheckRegexCases("lstrip_chars", c => PyText.LStrip(Text(c, "s"), Text(c, "chars")));

    [Fact]
    public void Count_matches_Python()
    {
        var cases = RegexValues.RegexCases("count");
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            Assert.True(
                c.GetProperty("r").GetInt64() == PyText.Count(Text(c, "s"), Text(c, "sub")),
                $"count of {Describe([Text(c, "sub")])} in {Describe([Text(c, "s")])}");
        }
    }

    private static void CheckRegexCases(string name, Func<JsonElement, string> call)
    {
        var cases = RegexValues.RegexCases(name);
        Assert.True(cases.Count > 500, name);
        foreach (var c in cases)
        {
            Assert.True(
                Ints(c.GetProperty("r")).SequenceEqual(PyText.ToCodePoints(call(c))),
                $"{name} {c}: expected {Describe([FromCodePoints(c.GetProperty("r"))])}, got {Describe([call(c)])}");
        }
    }

    // Laws that hold for any input. They do not use the Python values.

    private static readonly Gen<string> Piece = Gen.OneOf(
        Gen.Const("a"), Gen.Const("b"), Gen.Const("x"), Gen.Const(" "), Gen.Const("\n"), Gen.Const(" "),
        Gen.Const("\U0001F600"), Gen.Const("́"), Gen.Const("\ud800"), Gen.Const("1"), Gen.Const("\U0001D7D8"), Gen.Const("\U0001D400"));

    private static readonly Gen<string> Text8 = Piece.List[0, 12].Select(l => string.Concat(l));

    private static readonly Gen<string> Short = Piece.List[0, 3].Select(l => string.Concat(l));

    [Fact]
    public void Split_with_one_group_around_the_whole_pattern_loses_no_text()
    {
        // The items between the matches and the matches themselves, put back in order, are the text.
        var patterns = new[] { Rx("(x*)"), Rx("(" + PyRegex.Space + "*)"), Rx("(" + PyRegex.Word + "+)"), Rx("(" + PyRegex.NotWord + ")"), Rx("(" + PyRegex.Boundary + ")") };
        Gen.Select(Text8, Gen.Int[0, patterns.Length - 1], Gen.Int[0, 4]).Sample((s, p, n) =>
            string.Concat(PyRegex.Split(patterns[p], s, n)) == s);
    }

    [Fact]
    public void Split_at_most_maxsplit_times()
    {
        var regex = Rx("(a*?)");
        Gen.Select(Text8, Gen.Int[1, 5]).Sample((s, n) => PyRegex.Split(regex, s, n).Count <= (2 * n) + 1);
    }

    [Fact]
    public void Count_is_one_less_than_the_items_of_Split_on_the_same_text() =>
        Gen.Select(Text8, Short).Sample((s, sub) => sub.Length == 0 || PyText.Count(s, sub) == PyText.Split(s, sub).Count - 1);

    [Fact]
    public void Replace_with_a_count_joins_the_cuts_of_Split_with_the_new_text() =>
        Gen.Select(Text8, Short, Short, Gen.Int[0, 4]).Sample((s, old, @new, n) =>
            old.Length == 0 || PyText.Replace(s, old, @new, n) == string.Join(@new, PyText.Split(s, old, n)));

    [Fact]
    public void LStrip_with_chars_removes_only_leading_code_points_of_chars() =>
        Gen.Select(Text8, Short).Sample((s, chars) =>
        {
            var result = PyText.LStrip(s, chars);
            var set = PyText.ToCodePoints(chars).ToHashSet();
            var first = PyText.ToCodePoints(result).Cast<int?>().FirstOrDefault();
            var removed = PyText.ToCodePoints(s[..^result.Length]);
            return s.EndsWith(result, StringComparison.Ordinal) && removed.All(set.Contains) && (first is null || !set.Contains(first.Value));
        });
}
