namespace Graphiti.PyCompat;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

/// <summary>
/// The character classes of Python's <c>re</c> for <c>str</c> patterns, as pattern text for .NET, and <c>re.split</c>.
/// A .NET pattern works on UTF-16 units and Python works on code points. Each constant matches one whole code point,
/// so a character above U+FFFF (a surrogate pair) is one item, and a lone surrogate is one item.
/// Each constant is one atom, so a quantifier after it applies to the whole constant.
/// Put a constant into a pattern in place of the Python escape, as R13 of the rulebook says:
/// <c>\s*</c> becomes <c>Space + "*"</c>, and <c>\b(\w+)\b</c> becomes <c>Boundary + "(" + Word + "+)" + Boundary</c>.
/// Do not set <c>RegexOptions.ECMAScript</c>, and do not use <c>RegexOptions.IgnoreCase</c> without R13's three extra letters.
/// The sets come from the pinned Python build (<see cref="PyRegexData"/>), not from .NET categories.
/// </summary>
internal static class PyRegex
{
    /// <summary>
    /// The inside of a character class for <c>\s</c>. Use it where Python writes <c>\s</c> inside a class:
    /// <c>[A-Za-z0-9_\s]</c> becomes <c>"[A-Za-z0-9_" + SpaceClassBody + "]"</c>. It is exact, because each white space
    /// character is in the BMP. Word and Digit have no such member, because their sets reach above U+FFFF and a class cannot
    /// hold a surrogate pair. Write an alternation instead: <c>[\w.-]</c> becomes <c>"(?:" + Word + "|[.-])"</c>.
    /// </summary>
    internal const string SpaceClassBody = PyRegexData.SpaceBody;

    /// <summary>Python <c>\s</c>. It also matches U+001C to U+001F (GAPS F17).</summary>
    internal const string Space = "[" + SpaceClassBody + "]";

    /// <summary>Python <c>\S</c>.</summary>
    internal const string NotSpace = "(?:[^" + SpaceClassBody + @"\uD800-\uDFFF]|" + AnyPair + "|" + LoneSurrogate + ")";

    /// <summary>Python <c>\w</c>: a code point for which <c>str.isalnum()</c> is true, and the underscore.</summary>
    internal const string Word = "(?:[" + PyRegexData.WordBmpBody + "]|" + PyRegexData.WordAstral + ")";

    /// <summary>Python <c>\W</c>. It matches a lone surrogate.</summary>
    internal const string NotWord =
        "(?:[^" + PyRegexData.WordBmpBody + @"\uD800-\uDFFF]|(?!" + PyRegexData.WordAstral + ")" + AnyPair + "|" + LoneSurrogate + ")";

    /// <summary>Python <c>\b</c>: a position with a <see cref="Word"/> code point on one side only. Empty text has none.</summary>
    internal const string Boundary = "(?:(?<=" + Word + ")(?!" + Word + ")|(?<!" + Word + ")(?=" + Word + "))";

    /// <summary>Python <c>\d</c>: each code point for which <c>str.isdecimal()</c> is true, also above U+FFFF.</summary>
    internal const string Digit = "(?:[" + PyRegexData.DigitBmpBody + "]|" + PyRegexData.DigitAstral + ")";

    private const string AnyPair = @"[\uD800-\uDBFF][\uDC00-\uDFFF]";

    private const string LoneSurrogate = @"[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]";

    /// <summary>A position between the two units of a surrogate pair. Python has no such position.</summary>
    private const string InsidePair = @"(?!(?<=[\uD800-\uDBFF])[\uDC00-\uDFFF])";

    private sealed class SplitForms(Regex plain, Regex advance)
    {
        /// <summary>Finds the leftmost match at a code point boundary.</summary>
        internal Regex Plain { get; } = plain;

        /// <summary>The same, but a match that is empty and ends where the search started does not count.</summary>
        internal Regex Advance { get; } = advance;
    }

    private static readonly ConditionalWeakTable<Regex, SplitForms> Forms = new();

    private static SplitForms BuildForms(Regex regex)
    {
        if ((regex.Options & RegexOptions.RightToLeft) != 0)
        {
            throw new ArgumentException("Python has no right-to-left matching");
        }

        // A comment of the pattern ends at the line break, so the suffix is safe.
        var end = (regex.Options & RegexOptions.IgnorePatternWhitespace) != 0 ? "\n)" : ")";
        var core = "(?:" + regex.ToString() + end;
        return new SplitForms(
            new Regex(InsidePair + core + InsidePair, regex.Options, regex.MatchTimeout),
            new Regex(InsidePair + core + @"(?<!\G)" + InsidePair, regex.Options, regex.MatchTimeout));
    }

    /// <summary>
    /// <c>re.split(pattern, text, maxsplit)</c> for a pattern that was translated by hand. The flags are the options of
    /// <paramref name="regex"/>. The result holds the text before each match, then the value of each capture group of the
    /// match (<c>null</c> for a group that did not take part), and at the end the text after the last match.
    /// An empty match splits, and a match that is empty and directly after another match is found, as in Python 3.7 and later.
    /// A position between the two units of a surrogate pair is not a position, so no match starts or ends there.
    /// <paramref name="maxsplit"/> is the number of cuts. Zero means all, and a negative value means none, as in Python 3.12.
    /// The groups are in the order of <see cref="Regex.GetGroupNumbers"/>: .NET numbers the unnamed groups first and the
    /// named groups after them. Python numbers them by position, so use one kind in a pattern that you split with.
    /// </summary>
    internal static List<string?> Split(Regex regex, string text, long maxsplit = 0)
    {
        var forms = Forms.GetValue(regex, BuildForms);
        var groups = regex.GetGroupNumbers().Where(n => n != 0).ToArray();
        var result = new List<string?>();
        var last = 0;
        var cuts = 0L;
        var mustAdvance = false;
        while (maxsplit == 0 || cuts < maxsplit)
        {
            var m = (mustAdvance ? forms.Advance : forms.Plain).Match(text, last);
            if (!m.Success)
            {
                break;
            }

            result.Add(text.Substring(last, m.Index - last));
            foreach (var number in groups)
            {
                var group = m.Groups[number];
                result.Add(group.Success ? group.Value : null);
            }

            cuts++;
            mustAdvance = m.Length == 0;
            last = m.Index + m.Length;
        }

        result.Add(text.Substring(last));
        return result;
    }
}
