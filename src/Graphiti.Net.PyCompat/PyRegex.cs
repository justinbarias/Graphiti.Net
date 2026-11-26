namespace Graphiti.PyCompat;

using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

/// <summary>
/// The character classes of Python's <c>re</c> for <c>str</c> patterns, as pattern text for .NET, and <c>re.split</c>.
/// A .NET pattern works on UTF-16 units and Python works on code points. <see cref="Word"/>, <see cref="Digit"/>, and
/// <see cref="Boundary"/> treat a character above U+FFFF (a surrogate pair) as one code point.
/// Each constant is one atom, so a quantifier after it applies to the whole constant.
/// Each constant is wrapped in <c>(?-i:...)</c>, so <c>RegexOptions.IgnoreCase</c> cannot change its set. Without the wrapper,
/// .NET 10 folds the explicit ranges with Unicode 16, and U+A7CB (not assigned in Python's Unicode 15) matches
/// <see cref="Word"/> through its lower-case partner U+0264.
/// Put a constant into a pattern in place of the Python escape, as R13 of the rulebook says:
/// <c>\s*</c> becomes <c>Space + "*"</c>, and <c>\b(\w+)\b</c> becomes <c>Boundary + "(" + Word + "+)" + Boundary</c>.
/// Do not set <c>RegexOptions.ECMAScript</c>. For <c>re.IGNORECASE</c> follow R13 and write the three extra letters out.
/// The sets come from the pinned Python build (<see cref="PyRegexData"/>), not from .NET categories.
/// </summary>
internal static class PyRegex
{
    /// <summary>
    /// The inside of a character class for <c>\s</c>. Use it where Python writes <c>\s</c> inside a positive class:
    /// <c>[A-Za-z0-9_\s]</c> becomes <c>"[A-Za-z0-9_" + SpaceClassBody + "]"</c>. A class works on UTF-16 units, so it is wrong
    /// for a negated class: <c>"[^" + SpaceClassBody + ",]"</c> matches each half of an emoji, and Python's <c>[^\s,]</c>
    /// matches the emoji once. A negated class needs an alternation that takes a surrogate pair as one item. The same
    /// holds for <see cref="Word"/> and <see cref="Digit"/>, which have no class body because their sets reach above U+FFFF.
    /// For a positive class, write an alternation: <c>[\w.-]</c> becomes <c>"(?:" + Word + "|[.-])"</c>.
    /// </summary>
    internal const string SpaceClassBody = PyRegexData.SpaceBody;

    /// <summary>Python <c>\s</c>. It also matches U+001C to U+001F (GAPS F17).</summary>
    internal const string Space = "(?-i:[" + SpaceClassBody + "])";

    /// <summary>Python <c>\w</c>: a code point for which <c>str.isalnum()</c> is true, and the underscore.</summary>
    internal const string Word = "(?-i:(?:[" + PyRegexData.WordBmpBody + "]|" + PyRegexData.WordAstral + "))";

    /// <summary>Python <c>\d</c>: each code point for which <c>str.isdecimal()</c> is true, also above U+FFFF.</summary>
    internal const string Digit = "(?-i:(?:[" + PyRegexData.DigitBmpBody + "]|" + PyRegexData.DigitAstral + "))";

    /// <summary>Python <c>\b</c>: a position with a <see cref="Word"/> code point on one side only. Empty text has none.</summary>
    internal const string Boundary = "(?-i:(?<=" + Word + ")(?!" + Word + ")|(?<!" + Word + ")(?=" + Word + "))";

    /// <summary>A position between the two units of a surrogate pair. Python has no such position.</summary>
    private const string InsidePair = @"(?!(?<=[\uD800-\uDBFF])[\uDC00-\uDFFF])";

    // An inline x flag, as in (?x) or (?ix:...). It makes the rest of the group a comment-aware pattern.
    private static readonly Regex InlineVerbose = new(@"\(\?[imnsx]*x[imnsx]*(?:-[imnsx]*)?[):]", RegexOptions.CultureInvariant);

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
            throw new ArgumentException("Split does not support RegexOptions.RightToLeft: Python has no right-to-left matching");
        }

        if ((regex.Options & RegexOptions.NonBacktracking) != 0)
        {
            throw new ArgumentException("Split does not support RegexOptions.NonBacktracking: it has no lookbehind and no \\G");
        }

        if (InlineVerbose.IsMatch(regex.ToString()))
        {
            throw new ArgumentException("Split does not support an inline x flag such as (?x): use RegexOptions.IgnorePatternWhitespace");
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
    /// Limits, each of which throws <see cref="ArgumentException"/>: <c>RegexOptions.RightToLeft</c>,
    /// <c>RegexOptions.NonBacktracking</c> (the helper needs lookbehind and <c>\G</c>), and an inline <c>x</c> flag such as
    /// <c>(?x)</c> (a comment would swallow the text that the helper appends). Use <c>RegexOptions.IgnorePatternWhitespace</c>.
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
