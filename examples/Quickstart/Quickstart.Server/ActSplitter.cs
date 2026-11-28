namespace Quickstart;

using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml.Linq;

/// <summary>One episode of the split: the place in the Act, and the text to ingest.</summary>
/// <param name="Ref">The place of the episode in the Act.</param>
/// <param name="Content">The content of the episode: the topic line, the heading of the section, and the text. It has no chain and no section number.</param>
/// <param name="SourceDescription">The chain in one line, for the field of the same name.</param>
/// <param name="WordCount">The number of words of the text of the section, without the heading.</param>
internal sealed record ActEpisode(ProvisionRef Ref, string Content, string SourceDescription, int WordCount);

/// <summary>
/// Reads the Word file of the Act and splits it into episodes. A section is the unit. The definitions section is too
/// large for one episode, so the splitter cuts it at a definition. The code uses only the .NET libraries.
/// </summary>
internal static partial class ActSplitter
{
    /// <summary>The name of the Act. The date of assent is its reference time.</summary>
    public const string ActName = "Online Safety Act 2021 (Cth)";

    /// <summary>The date of assent of the Act, 23 July 2021, in UTC.</summary>
    public static readonly DateTimeOffset AssentDate = new(2021, 7, 23, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The size at which a definitions section is cut, in words.</summary>
    private const int DefinitionsEpisodeWords = 400;

    /// <summary>A section with a definition and more words than this one is cut into many episodes.</summary>
    private const int LargeSectionWords = 600;

    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    private sealed record Para(string Style, string Text);

    /// <summary>Reads a Word file and returns its episodes in the order of the Act.</summary>
    /// <param name="path">The path of the .docx file.</param>
    /// <returns>The episodes.</returns>
    public static IReadOnlyList<ActEpisode> Split(string path)
    {
        var paragraphs = ReadParagraphs(path);
        var episodes = new List<ActEpisode>();
        string? part = null, division = null, subdivision = null;
        var started = false;
        string? number = null, heading = null;
        var body = new List<Para>();

        void Flush()
        {
            if (number is null || heading is null)
            {
                return;
            }

            episodes.AddRange(MakeEpisodes(part, division, subdivision, number, heading, body));
            number = null;
            heading = null;
            body.Clear();
        }

        foreach (var paragraph in paragraphs)
        {
            var style = paragraph.Style;
            if (style.StartsWith("TOC", StringComparison.Ordinal))
            {
                continue;
            }

            switch (style)
            {
                case "ActHead1":
                    // A Chapter. This Act has none, and the record keeps the level for other Acts.
                    throw new NotSupportedException("The Act has a Chapter, and the splitter does not handle it.");
                case "ActHead2":
                    Flush();
                    started = true;
                    part = paragraph.Text;
                    division = null;
                    subdivision = null;
                    break;
                case "ActHead3":
                    Flush();
                    division = paragraph.Text;
                    subdivision = null;
                    break;
                case "ActHead4":
                    Flush();
                    subdivision = paragraph.Text;
                    break;
                case "ActHead5" when started:
                    Flush();
                    var match = SectionHeading().Match(paragraph.Text);
                    if (!match.Success)
                    {
                        throw new InvalidDataException($"A section heading has no number: '{paragraph.Text}'.");
                    }

                    number = match.Groups[1].Value;
                    heading = match.Groups[2].Value;
                    break;
                case "AssentBk" or "2ndRd":
                    // The tail of the file: the legislative history.
                    Flush();
                    started = false;
                    break;
                default:
                    if (started && number is not null && paragraph.Text.Length > 0)
                    {
                        body.Add(paragraph);
                    }

                    break;
            }
        }

        Flush();
        return episodes;
    }

    private static IEnumerable<ActEpisode> MakeEpisodes(
        string? part, string? division, string? subdivision, string number, string heading, List<Para> body)
    {
        var words = body.Sum(p => WordCount(p.Text));
        var groups = new List<List<Para>>();
        if (words > LargeSectionWords && body.Any(p => p.Style == "Definition"))
        {
            groups = CutAtDefinitions(body);
        }
        else
        {
            groups.Add([.. body]);
        }

        for (var i = 0; i < groups.Count; i++)
        {
            var name = $"{SectionIdOf(number)}{(groups.Count > 1 ? $" (part {i + 1} of {groups.Count})" : string.Empty)}";
            var reference = new ProvisionRef(ActName, null, part, division, subdivision, number, heading, name, null);
            var text = new StringBuilder();
            // The content holds the topic, the heading, and the text. The topic is the heading text of the Part and the
            // Division with no number, so the model knows the scheme of the section. The place of the section (the section
            // id and the chain) is provenance: the episode holds it in Name and in SourceDescription, and the model never
            // reads it.
            text.Append("Topic: ").Append(reference.Topic).Append('\n');
            text.Append(heading).Append("\n\n");
            text.Append(string.Join('\n', groups[i].Select(p => p.Text)));
            yield return new ActEpisode(reference, text.ToString(), reference.Chain, groups[i].Sum(p => WordCount(p.Text)));
        }
    }

    private static string SectionIdOf(string number) => $"Online Safety Act 2021 s {number}";

    /// <summary>
    /// Cuts a section at the paragraphs of the style Definition. The lead paragraphs before the first definition
    /// (for example "In this Act:") start each episode, so that each episode reads on its own.
    /// </summary>
    private static List<List<Para>> CutAtDefinitions(List<Para> body)
    {
        var firstDefinition = body.FindIndex(p => p.Style == "Definition");
        var lead = body.Take(firstDefinition).ToList();
        var blocks = new List<List<Para>>();
        foreach (var paragraph in body.Skip(firstDefinition))
        {
            if (paragraph.Style == "Definition" || blocks.Count == 0)
            {
                blocks.Add([]);
            }

            blocks[^1].Add(paragraph);
        }

        var groups = new List<List<Para>>();
        var current = new List<Para>(lead);
        var currentWords = 0;
        foreach (var block in blocks)
        {
            if (currentWords >= DefinitionsEpisodeWords)
            {
                groups.Add(current);
                current = [.. lead];
                currentWords = 0;
            }

            current.AddRange(block);
            currentWords += block.Sum(p => WordCount(p.Text));
        }

        groups.Add(current);
        return groups;
    }

    private static int WordCount(string text) => text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static List<Para> ReadParagraphs(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException("The file has no word/document.xml. It is not a .docx file.");
        using var stream = entry.Open();
        var document = XDocument.Load(stream);
        var result = new List<Para>();
        foreach (var p in document.Descendants(W + "p"))
        {
            var style = p.Element(W + "pPr")?.Element(W + "pStyle")?.Attribute(W + "val")?.Value ?? string.Empty;
            var text = new StringBuilder();
            foreach (var child in p.Descendants(W + "r").SelectMany(r => r.Elements()))
            {
                // The children of a run in this file (counted in the .docx): w:t 9,901, w:tab 5,099, w:noBreakHyphen 304,
                // w:fldChar 912, w:instrText 304, w:lastRenderedPageBreak 14, w:object 2. The file has no w:softHyphen,
                // w:br, w:cr, w:sym, or w:delText. Each one is decided here.
                if (child.Name == W + "t")
                {
                    text.Append(child.Value);
                }
                else if (child.Name == W + "tab")
                {
                    // A tab becomes one space.
                    text.Append(' ');
                }
                else if (child.Name == W + "noBreakHyphen")
                {
                    // A non-breaking hyphen is a real hyphen of the text: "end-user", "cyber-abuse". Keep it.
                    text.Append('-');
                }
                else if (child.Name == W + "softHyphen")
                {
                    // A soft hyphen is only a hint for line breaks. It is not part of the text. The file has none.
                }
                else if (child.Name == W + "br" || child.Name == W + "cr")
                {
                    // A line break becomes one space. The file has none.
                    text.Append(' ');
                }
                else if (child.Name == W + "sym")
                {
                    throw new NotSupportedException("The file has a w:sym symbol, and the splitter does not read it.");
                }

                // The other children hold no text of the Act: w:rPr (style), w:fldChar and w:instrText (the codes of a
                // field, for example a page reference in the table of contents), w:lastRenderedPageBreak, and w:object (an
                // embedded object).
            }

            result.Add(new Para(style, Normalize(text.ToString())));
        }

        return result;
    }

    /// <summary>Turns each non-breaking space into a space, collapses runs of spaces, and trims the text.</summary>
    private static string Normalize(string text) =>
        Spaces().Replace(text.Replace(' ', ' '), " ").Trim();

    [GeneratedRegex(@"^(\d+[A-Z]*)\s+(.+)$")]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@" {2,}")]
    private static partial Regex Spaces();
}
