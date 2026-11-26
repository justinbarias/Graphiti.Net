namespace Graphiti.PyCompat.Tests;

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using CsCheck;
using Graphiti.PyCompat;
using static Graphiti.PyCompat.Tests.PythonValues;

/// <summary>
/// The values that real Python gave for <c>PyRegex</c> and for <c>PyText.Replace</c> and <c>Count</c>, read from
/// <c>python-values.regex.json</c>. <c>port/tools/generate_regex_values.py</c> writes the file.
/// A text is a JSON string, or a list of code points if it holds a lone surrogate.
/// </summary>
internal static class RegexValues
{
    internal const int CodePointCount = 0x110000;

    private static readonly JsonDocument Document = JsonDocument.Parse(
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "python-values.regex.json")));

    internal static List<JsonElement> RegexCases(string name) =>
        [.. Document.RootElement.GetProperty("cases").GetProperty(name).EnumerateArray()];

    internal static string Txt(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : FromCodePoints(e);

    internal static string T(JsonElement c, string name) => Txt(c.GetProperty(name));

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

    // The constants must not change under IgnoreCase, so each class test runs with and without it.
    private static readonly RegexOptions[] OptionSets = [RegexOptions.None, RegexOptions.IgnoreCase];

    private static Regex Rx(string pattern, RegexOptions options = RegexOptions.None) =>
        new(pattern, options | RegexOptions.CultureInvariant);

    // ---- the classes, for each code point ----

    private static void AssertClassMatchesPython(string name, string netPattern)
    {
        var expected = RegexValues.ClassSet(name);
        Assert.True(expected.Any(e => e) && expected.Any(e => !e), "the recorded set must hold both kinds");
        foreach (var options in OptionSets)
        {
            var regex = new Regex(@"\A" + netPattern + @"\z", Compiled | options);
            var wrong = new List<string>();
            var text = new char[2];
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

                if (regex.IsMatch(text.AsSpan(0, length)) != expected[cp])
                {
                    wrong.Add($"U+{cp:X4}");
                }
            }

            Assert.True(wrong.Count == 0, $"{name} with {options} differs from Python at {wrong.Count} code points, first {string.Join(" ", wrong.Take(10))}");
        }
    }

    [Fact]
    public void Space_matches_Python_for_each_code_point() => AssertClassMatchesPython("space", PyRegex.Space);

    [Fact]
    public void Word_matches_Python_for_each_code_point() => AssertClassMatchesPython("word", PyRegex.Word);

    [Fact]
    public void Digit_matches_Python_for_each_code_point() => AssertClassMatchesPython("digit", PyRegex.Digit);

    [Fact]
    public void SpaceClassBody_inside_a_class_matches_Python_for_each_BMP_code_point()
    {
        var expected = RegexValues.ClassSet("space");
        foreach (var options in OptionSets)
        {
            var regex = new Regex("\\A[" + PyRegex.SpaceClassBody + "]\\z", Compiled | options);
            var wrong = new List<string>();
            for (var cp = 0; cp < 0x10000; cp++)
            {
                if (regex.IsMatch([(char)cp]) != expected[cp])
                {
                    wrong.Add($"U+{cp:X4}");
                }
            }

            Assert.True(wrong.Count == 0, $"with {options} differs at {string.Join(" ", wrong.Take(10))}");
        }
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
        foreach (var options in OptionSets)
        {
            var regex = new Regex(PyRegex.Boundary, Compiled | options);
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
            Assert.True(wrong.Count == 0, $"\\b with {options} differs from Python in {wrong.Count} cases, first {string.Join(", ", wrong.Take(10))}");
        }
    }

    // ---- the IGNORECASE patterns of temporal_edge_utils.py ----

    private const RegexOptions IgnoreCase = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    // R13: each s becomes [sſ] and each i becomes [iİı]. This writes them out in a literal of the patterns below.
    private static string Fold(string literal) => literal.Replace("s", "[sſ]", StringComparison.Ordinal).Replace("i", "[iİı]", StringComparison.Ordinal);

    private static readonly Dictionary<string, Regex> TemporalRegexes = new()
    {
        // _FUTURE_ASSERTION_RE: r'\b(will|shall|...|committed to)\b|[a-z]+\'ll\b'
        ["future"] = Rx(
            PyRegex.Boundary + "(" + Fold(
                "will|shall|is going to|are going to|going to|plans? to|planned to|planning to|intends? to|intended to|"
                + "expects? to|expected to|promises? to|promised to|scheduled to|is scheduled to|are scheduled to|"
                + "set to|aims? to|aimed to|committed to")
            + ")" + PyRegex.Boundary + "|[a-zſİı]+'ll" + PyRegex.Boundary,
            IgnoreCase),

        // _DEADLINE_RE: r'\b(by|before|no later than|deadline|due by|due on)\b'
        ["deadline"] = Rx(PyRegex.Boundary + "(" + Fold("by|before|no later than|deadline|due by|due on") + ")" + PyRegex.Boundary, IgnoreCase),

        // _ONGOING_STATE_END_RE: r'\b(until|through|thru|expires?|expired|ends?|ended|ending|stops?|stopped|ceases?|ceased|no longer)\b'
        ["ongoing"] = Rx(
            PyRegex.Boundary + "(" + Fold("until|through|thru|expires?|expired|ends?|ended|ending|stops?|stopped|ceases?|ceased|no longer") + ")" + PyRegex.Boundary,
            IgnoreCase),
    };

    [Fact]
    public void The_IGNORECASE_patterns_of_temporal_edge_utils_match_Python()
    {
        var cases = RegexValues.RegexCases("temporal");
        foreach (var name in TemporalRegexes.Keys)
        {
            Assert.True(cases.Count(c => c.GetProperty("p").GetString() == name) >= 500, name);
        }

        Assert.Contains(cases, c => RegexValues.T(c, "s").Contains('Ɤ', StringComparison.Ordinal));
        Assert.Contains(cases, c => RegexValues.T(c, "s").Contains('Ƛ', StringComparison.Ordinal));
        Assert.Contains(cases, c => c.GetProperty("r").ValueKind == JsonValueKind.Array);
        Assert.Contains(cases, c => c.GetProperty("r").ValueKind == JsonValueKind.Null);
        foreach (var c in cases)
        {
            var name = c.GetProperty("p").GetString()!;
            var text = RegexValues.T(c, "s");
            var m = TemporalRegexes[name].Match(text);
            int[]? actual = m.Success ? [CodePointIndex(text, m.Index), CodePointIndex(text, m.Index + m.Length)] : null;
            int[]? expected = c.GetProperty("r").ValueKind == JsonValueKind.Null ? null : Ints(c.GetProperty("r"));
            Assert.True(
                (expected is null && actual is null) || (expected is not null && actual is not null && expected.SequenceEqual(actual)),
                $"{name} on {Describe([text])}: expected {(expected is null ? "None" : string.Join(",", expected))}, got {(actual is null ? "None" : string.Join(",", actual))}");
        }
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
        // r'(?<!\w)(?!\w)'
        ["word_gap"] = Rx("(?<!" + PyRegex.Word + ")(?!" + PyRegex.Word + ")"),
        // r'(?!\w)'
        ["no_word_ahead"] = Rx("(?!" + PyRegex.Word + ")"),
        // r'(?=\U0001F600)'
        ["before_emoji"] = Rx("(?=😀)"),
        // r'(?<=\U0001F600)'
        ["after_emoji"] = Rx("(?<=😀)"),
    };

    private static string Describe(IEnumerable<string?> items) =>
        "[" + string.Join(", ", items.Select(i => i is null ? "None" : "'" + string.Join(" ", PyText.ToCodePoints(i).Select(c => c.ToString("X", CultureInfo.InvariantCulture))) + "'")) + "]";

    [Fact]
    public void Split_matches_Python()
    {
        var cases = RegexValues.RegexCases("split");
        Assert.True(cases.Count > 4000);
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
            var text = RegexValues.T(c, "s");
            var maxsplit = c.GetProperty("n").GetInt64();
            var expected = c.GetProperty("r").EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Null ? null : RegexValues.Txt(e)).ToList();
            var message = $"{name} maxsplit {maxsplit} on {Describe([text])}";
            if (maxsplit == 0)
            {
                // Each match consumes text or sits at its own position, so no split makes more cuts than this.
                // A defect that never advances stops here with a wrong result and not after a run that never ends.
                var bound = (2 * (text.Length + 1)) + 2;
                var guarded = PyRegex.Split(SplitRegexes[name], text, bound);
                Assert.True(expected.SequenceEqual(guarded), $"{message} (bounded): expected {Describe(expected)}, got {Describe(guarded)}");
            }

            var actual = PyRegex.Split(SplitRegexes[name], text, maxsplit);
            Assert.True(expected.SequenceEqual(actual), $"{message}: expected {Describe(expected)}, got {Describe(actual)}");
        }
    }

    [Fact]
    public void Split_with_a_pattern_that_does_not_match_returns_the_text()
    {
        Assert.Equal(["abc"], PyRegex.Split(Rx("z"), "abc"));
        Assert.Equal([string.Empty], PyRegex.Split(Rx("z"), string.Empty));
    }

    [Fact]
    public void Split_does_not_cut_between_the_units_of_a_pair()
    {
        // Python: re.split(r'(?<!\w)(?!\w)', 'a😀') gives ['a😀', ''], and the same for each text that holds an emoji.
        var gap = SplitRegexes["word_gap"];
        Assert.Equal(["a\U0001F600", string.Empty], PyRegex.Split(gap, "a\U0001F600"));
    }

    [Fact]
    public void Split_throws_ArgumentException_for_what_it_cannot_do()
    {
        var inline = Assert.Throws<ArgumentException>(() => PyRegex.Split(Rx("(?x) a # comment"), "a"));
        Assert.Contains("inline x flag", inline.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => PyRegex.Split(Rx("(?ix:a # comment\n)"), "a"));
        var nonBacktracking = Assert.Throws<ArgumentException>(() => PyRegex.Split(Rx("a", RegexOptions.NonBacktracking), "a"));
        Assert.Contains("NonBacktracking", nonBacktracking.Message, StringComparison.Ordinal);
        var rightToLeft = Assert.Throws<ArgumentException>(() => PyRegex.Split(Rx("a", RegexOptions.RightToLeft), "a"));
        Assert.Contains("RightToLeft", rightToLeft.Message, StringComparison.Ordinal);

        // A pattern that only turns the flag off, or that escapes the text, is fine.
        Assert.Equal(["", ""], PyRegex.Split(Rx("(?-x)a"), "a"));
        Assert.Equal(["", ""], PyRegex.Split(Rx(@"\(\?x\)"), "(?x)"));
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
            var text = RegexValues.T(c, "s");
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

    // ---- PyText.Replace and Count ----

    [Fact]
    public void Replace_with_a_count_matches_Python()
    {
        var cases = RegexValues.RegexCases("replace");
        Assert.True(cases.Count > 500);
        foreach (var c in cases)
        {
            var actual = PyText.Replace(RegexValues.T(c, "s"), RegexValues.T(c, "old"), RegexValues.T(c, "new"), c.GetProperty("n").GetInt64());
            var expected = RegexValues.T(c, "r");
            Assert.True(expected == actual, $"replace {c}: expected {Describe([expected])}, got {Describe([actual])}");
        }
    }

    [Fact]
    public void Count_matches_Python()
    {
        var cases = RegexValues.RegexCases("count");
        Assert.NotEmpty(cases);
        foreach (var c in cases)
        {
            Assert.True(
                c.GetProperty("r").GetInt64() == PyText.Count(RegexValues.T(c, "s"), RegexValues.T(c, "sub")),
                $"count of {Describe([RegexValues.T(c, "sub")])} in {Describe([RegexValues.T(c, "s")])}");
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
        var patterns = new[] { Rx("(x*)"), Rx("(" + PyRegex.Space + "*)"), Rx("(" + PyRegex.Word + "+)"), Rx("(" + PyRegex.Boundary + ")") };
        Gen.Select(Text8, Gen.Int[0, patterns.Length - 1], Gen.Int[0, 4]).Sample((s, p, n) =>
            string.Concat(PyRegex.Split(patterns[p], s, n)) == s);
    }

    [Fact]
    public void Count_is_one_less_than_the_items_of_Split_on_the_same_text() =>
        Gen.Select(Text8, Short).Sample((s, sub) => sub.Length == 0 || PyText.Count(s, sub) == PyText.Split(s, sub).Count - 1);

    [Fact]
    public void Replace_with_a_count_joins_the_cuts_of_Split_with_the_new_text() =>
        Gen.Select(Text8, Short, Short, Gen.Int[0, 4]).Sample((s, old, @new, n) =>
            old.Length == 0 || PyText.Replace(s, old, @new, n) == string.Join(@new, PyText.Split(s, old, n)));
}
