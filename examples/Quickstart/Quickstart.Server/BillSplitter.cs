namespace Quickstart;

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>The result of the split of an amending Bill: the episodes, and the items that the split leaves out.</summary>
/// <param name="Episodes">The episodes in the order of the Bill.</param>
/// <param name="Skipped">One line for each item that the split leaves out, with the reason.</param>
internal sealed record BillSplit(IReadOnlyList<ActEpisode> Episodes, IReadOnlyList<string> Skipped);

/// <summary>
/// Reads the Word file of a Bill that amends the Act, and splits it into episodes. A Bill of this kind is a list of
/// items, and each item changes the Act. The split makes three kinds of episode, so that the graph shows how the law
/// changes over time:
/// a new section that an item inserts, a section of the Act that an item repeals, and a change to the words of a
/// section. The reference time of each episode is the day on which its Schedule commences. The ingestion sends a new
/// section and a change to the model. It sends no repeal to the model: it ends the facts of the repealed section itself.
/// </summary>
internal static partial class BillSplitter
{
    /// <summary>The name of the Bill.</summary>
    public const string BillName = "Online Safety Amendment (Digital Duty of Care) Bill 2026";

    /// <summary>The short name of the Bill, for the section id of an episode.</summary>
    public const string BillShortName = "Digital Duty of Care Bill 2026";

    private const string AmendedAct = "Online Safety Act 2021";
    private const int OldTextLimit = 2000;

    // The words of a cross-reference. An amendment that has only these words and numbers changes no rule of the domain.
    private static readonly HashSet<string> ReferenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "section", "sections", "subsection", "subsections", "paragraph", "paragraphs", "subparagraph", "subparagraphs",
        "part", "division", "schedule", "of", "or", "and", "to", "the", "this", "given", "under",
    };

    private static readonly HashSet<string> ProvisionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "section", "sections", "subsection", "subsections", "paragraph", "paragraphs", "subparagraph", "subparagraphs",
        "part", "division", "schedule",
    };

    private sealed record Item(
        string Schedule, string? BillPart, string? BillDivision, string Act, string Number, string Target, List<(string Style, string Text)> Body);

    /// <summary>Splits the Bill.</summary>
    /// <param name="billPath">The path of the .docx file of the Bill.</param>
    /// <param name="act">The episodes of the Act that the Bill amends. The split reads the heading and the text of each target from them.</param>
    /// <param name="assent">The day of the Royal Assent of the Bill.</param>
    /// <returns>The episodes and the items that are left out.</returns>
    public static BillSplit Split(string billPath, IReadOnlyList<ActEpisode> act, DateOnly assent)
    {
        var episodes = new List<ActEpisode>();
        var skipped = new List<string>();
        var (items, transitional) = ReadItems(billPath);
        skipped.AddRange(transitional.Select(t => $"{t}: an application or transitional provision."));
        foreach (var item in items)
        {
            var label = $"{ScheduleLabel(item.Schedule)} item {item.Number} ({item.Target})";
            if (item.Act != AmendedAct)
            {
                skipped.Add($"{label}: it amends another Act ({item.Act}).");
                continue;
            }

            if (item.Schedule.Contains("Technical corrections", StringComparison.Ordinal))
            {
                skipped.Add($"{label}: a technical correction.");
                continue;
            }

            if (item.BillPart?.Contains("Updating references", StringComparison.Ordinal) == true)
            {
                skipped.Add($"{label}: it updates a reference to another Act.");
                continue;
            }

            if (item.Target.Contains("(heading)", StringComparison.Ordinal) || item.Target.Contains("heading)", StringComparison.Ordinal)
                || item.Target.Contains("(note", StringComparison.Ordinal))
            {
                skipped.Add($"{label}: it changes a heading or a note.");
                continue;
            }

            var date = Commencement(item.Schedule, assent);
            var made = MakeEpisodes(item, act, date).ToList();
            if (made.Count == 0)
            {
                skipped.Add($"{label}: {SkipReason(item, act)}");
            }

            episodes.AddRange(made);
        }

        return new BillSplit(episodes, skipped);
    }

    /// <summary>
    /// Gives the day on which a Schedule commences (section 2 of the Bill). Schedules 2 and 3 commence on the day after
    /// the end of the period of 12 months that starts on the day of assent. The other Schedules commence on the day
    /// after the day of assent.
    /// </summary>
    /// <param name="schedule">The heading of the Schedule.</param>
    /// <param name="assent">The day of the Royal Assent.</param>
    /// <returns>The day, at midnight UTC.</returns>
    public static DateTimeOffset Commencement(string schedule, DateOnly assent)
    {
        var label = ScheduleLabel(schedule);
        var day = assent.AddDays(1);
        if (label is "Sch 2" or "Sch 3")
        {
            // The period of 12 months ends on the day before the same day of the month, one year later. So the day after
            // its end is that same day. An assent on 29 February has no such day in the next year: the period then ends
            // on 28 February, and the day after is 1 March.
            day = assent.AddMonths(12);
            if (day.Day != assent.Day)
            {
                day = day.AddDays(1);
            }
        }

        return new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    }

    private static IEnumerable<ActEpisode> MakeEpisodes(Item item, IReadOnlyList<ActEpisode> act, DateTimeOffset date)
    {
        var instruction = item.Body.FirstOrDefault(p => p.Style == "Item").Text ?? string.Empty;
        var newSections = InsertedSections(item);

        // A whole Part, Division, or section that the item repeals: one episode for each section of the Act that ends.
        if (RepealWhole().IsMatch(instruction))
        {
            // A new section takes the place of a repealed section. A new Part takes the place of a repealed Part.
            var replaced = newSections.Count == 0 ? "Nothing replaces it."
                : item.Target.StartsWith("Section", StringComparison.Ordinal) ? $"A new rule takes its place: {newSections[0].Heading}."
                : $"New rules take its place: {newSections[0].Topic.Split(", ")[0]}.";
            foreach (var old in Targets(item.Target, act))
            {
                yield return Repealed(item, old, date, replaced);
            }
        }

        foreach (var section in newSections)
        {
            yield return Inserted(item, section, date);
        }

        if (newSections.Count == 0 && !RepealWhole().IsMatch(instruction) && !IsTrivial(item))
        {
            yield return Amended(item, act, date);
        }
    }

    // The reason for an item that gives no episode.
    private static string SkipReason(Item item, IReadOnlyList<ActEpisode> act)
    {
        var instruction = item.Body.FirstOrDefault(p => p.Style == "Item").Text ?? string.Empty;
        if (RepealWhole().IsMatch(instruction) && !Targets(item.Target, act).Any())
        {
            return "it repeals a provision that the Act as made in 2021 does not have.";
        }

        return RepealPart().IsMatch(instruction)
            ? "it repeals a paragraph or a subsection, and the Bill does not give the words that end."
            : "it changes only a cross-reference, the punctuation, or a list of provisions.";
    }

    private static ActEpisode Inserted(Item item, (string Number, string Heading, string Topic, List<string> Text) section, DateTimeOffset date)
    {
        var text = new StringBuilder();
        text.Append("Status: new law. This rule starts on ").Append(Day(date)).Append(". Before this date it does not apply.\n");
        text.Append("Topic: ").Append(section.Topic).Append('\n');
        text.Append(section.Heading).Append("\n\n");
        text.Append(string.Join('\n', section.Text));
        return Episode(item, section.Number, section.Heading, "inserted by", text.ToString(), date);
    }

    private static ActEpisode Repealed(Item item, ActEpisode old, DateTimeOffset date, string replaced)
    {
        // The content of an episode of the Act: the topic line, the heading, an empty line, and the text.
        var lines = old.Content.Split('\n');
        var body = string.Join('\n', lines.Skip(3));
        if (body.Length > OldTextLimit)
        {
            body = body[..OldTextLimit] + " [text cut]";
        }

        var text = new StringBuilder();
        text.Append("Status: repealed. From ").Append(Day(date)).Append(", the rule below is repealed. It is no longer law from that date. ").Append(replaced).Append('\n');
        text.Append(lines[0]).Append('\n');
        text.Append(old.Ref.Heading).Append("\n\n");
        text.Append("The text of the repealed rule, which is law only until ").Append(Day(date)).Append(":\n");
        text.Append(body);
        var name = old.Ref.Name.Replace(old.Ref.SectionId, string.Empty, StringComparison.Ordinal).Trim();
        return Episode(item, old.Ref.Section, old.Ref.Heading, "repealed by", text.ToString(), date, name) with { RepealedSection = old.Ref.Section };
    }

    private static ActEpisode Amended(Item item, IReadOnlyList<ActEpisode> act, DateTimeOffset date)
    {
        var number = SectionNumber().Match(item.Target) is { Success: true } m ? m.Groups[1].Value : null;
        var old = number is null ? null : act.FirstOrDefault(e => e.Ref.Section == number);
        var text = new StringBuilder();
        text.Append("Status: changed. From ").Append(Day(date)).Append(", the rule below changes. The old words are no longer law from that date.\n");
        if (old is not null)
        {
            text.Append(old.Content.Split('\n')[0]).Append('\n');
            text.Append(old.Ref.Heading).Append("\n\n");
        }
        else
        {
            text.Append("Topic: ").Append(BillTopic(item)).Append("\n\n");
        }

        text.Append("The change (").Append(item.Target).Append("):\n");
        text.Append(string.Join('\n', item.Body.Select(p => p.Text)));
        return Episode(item, old?.Ref.Section, old?.Ref.Heading ?? item.Target, "amended by", text.ToString(), date);
    }

    private static ActEpisode Episode(
        Item item, string? section, string heading, string verb, string content, DateTimeOffset date, string? suffix = null)
    {
        var schedule = ScheduleLabel(item.Schedule);
        var source = $"{verb} the {BillShortName}, {schedule} item {item.Number}";
        var id = section is null
            ? $"{BillShortName}, {schedule} item {item.Number}"
            : $"{AmendedAct} s {section} ({source})";
        var name = string.IsNullOrEmpty(suffix) ? id : $"{id} {suffix}";
        var chain = string.Join(" > ", new[]
        {
            BillName, item.Schedule.Split('—')[0], item.BillPart?.Split('—')[0], item.BillDivision?.Split('—')[0], $"Item {item.Number}",
            section is null ? null : $"{AmendedAct} section {section}",
        }.Where(l => l is not null));
        var reference = new ProvisionRef(BillName, null, item.BillPart, item.BillDivision, null, section ?? string.Empty, heading, name, null)
        {
            CitationId = id,
            ChainText = chain,
            Item = $"{schedule.Replace("Sch ", string.Empty, StringComparison.Ordinal)}:{item.Number}",
        };
        return new ActEpisode(reference, content, chain, content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, date);
    }

    // The sections that an item inserts: each paragraph of the style ActHead5 starts one. The topic is the heading text of
    // the new Part and Division around it, or the heading text of the Schedule and its Part if the item has none.
    private static List<(string Number, string Heading, string Topic, List<string> Text)> InsertedSections(Item item)
    {
        var result = new List<(string Number, string Heading, string Topic, List<string> Text)>();
        string? part = null, division = null;
        foreach (var (style, text) in item.Body)
        {
            switch (style)
            {
                case "ActHead2":
                    part = Title(text);
                    division = null;
                    break;
                case "ActHead3":
                    division = Title(text);
                    break;
                case "ActHead5" when Heading().Match(text) is { Success: true } m:
                    var topic = part is null && division is null
                        ? BillTopic(item)
                        : string.Join(", ", new[] { part, division }.Where(t => t is not null));
                    result.Add((m.Groups[1].Value, m.Groups[2].Value, topic, []));
                    break;
                case "Item":
                    break;
                default:
                    if (result.Count > 0)
                    {
                        result[^1].Text.Add(text);
                    }

                    break;
            }
        }

        return result;
    }

    // The episodes of the Act that a target names: "Part 4", "Division 7 of Part 9", or "Section 40".
    private static IEnumerable<ActEpisode> Targets(string target, IReadOnlyList<ActEpisode> act)
    {
        if (DivisionOfPart().Match(target) is { Success: true } d)
        {
            return act.Where(e => Label(e.Ref.Part) == d.Groups[2].Value && Label(e.Ref.Division) == d.Groups[1].Value);
        }

        if (PartOnly().Match(target) is { Success: true } p)
        {
            return act.Where(e => Label(e.Ref.Part) == p.Groups[1].Value);
        }

        return SectionOnly().Match(target) is { Success: true } s ? act.Where(e => e.Ref.Section == s.Groups[1].Value) : [];
    }

    private static bool IsTrivial(Item item)
    {
        var instructions = item.Body.Where(p => p.Style == "Item").Select(p => p.Text).ToList();
        var body = item.Body.Where(p => p.Style != "Item").Select(p => p.Text).ToList();

        // A list of provisions, for example the list of provisions that an infringement notice can enforce.
        if (body.Count > 0)
        {
            return body.All(IsReference);
        }

        // An item with no body is trivial only if each of its instructions is trivial: a change of a cross-reference or of
        // the punctuation, or the repeal of paragraphs or subsections whose words the Bill does not give.
        return instructions.Count > 0 && instructions.All(instruction =>
            OmitSubstitute().Match(instruction) is { Success: true } m
                ? (IsReference(m.Groups[1].Value) && IsReference(m.Groups[2].Value)) || Bare(m.Groups[1].Value) == Bare(m.Groups[2].Value)
                : AfterInsert().Match(instruction) is { Success: true } a
                    ? IsReference(a.Groups[1].Value)
                    : RepealPart().IsMatch(instruction));
    }

    // True for a text that is only a cross-reference: it names a kind of provision, and each other word is a number or a
    // joining word. A bare number, for example "500", is not a cross-reference.
    private static bool IsReference(string text)
    {
        var words = Labels().Replace(text, " ").Split([' ', ',', ';', '.', ':', '(', ')', '“', '”'], StringSplitOptions.RemoveEmptyEntries);
        return words.Any(ProvisionWords.Contains) && words.All(w => ReferenceWords.Contains(w) || w.Any(char.IsDigit));
    }

    // The words of a text without the punctuation. A change of "and" to "or" changes the rule, so it is not removed.
    private static string Bare(string text) =>
        string.Join(' ', text.Split([' ', ',', ';', '.', ':', '(', ')'], StringSplitOptions.RemoveEmptyEntries));

    private static (List<Item> Items, List<string> Transitional) ReadItems(string path)
    {
        var items = new List<Item>();
        var transitional = new List<string>();
        string? schedule = null, billPart = null, billDivision = null, amended = null, number = null, target = null;
        var body = new List<(string Style, string Text)>();

        void Flush()
        {
            if (schedule is not null && amended is not null && number is not null && target is not null)
            {
                items.Add(new Item(schedule, billPart, billDivision, amended, number, target, [.. body]));
            }

            number = null;
            target = null;
            body.Clear();
        }

        foreach (var (style, text) in ActSplitter.ReadParagraphs(path))
        {
            if (style.StartsWith("TOC", StringComparison.Ordinal) || text.Length == 0)
            {
                continue;
            }

            switch (style)
            {
                case "ActHead6":
                    Flush();
                    (schedule, billPart, billDivision, amended) = (text, null, null, null);
                    break;
                case "ActHead7":
                    Flush();
                    (billPart, billDivision, amended) = (text, null, null);
                    break;
                case "ActHead8":
                    Flush();
                    (billDivision, amended) = (text, null);
                    break;
                case "ActHead9":
                    Flush();
                    amended = text;
                    break;
                case "ItemHead" when Heading().Match(text) is { Success: true } m:
                    Flush();
                    (number, target) = (m.Groups[1].Value, m.Groups[2].Value);
                    break;
                case "Transitional":
                    // An application or transitional provision. It is not an amendment of the Act.
                    Flush();
                    if (schedule is not null)
                    {
                        transitional.Add($"{ScheduleLabel(schedule)} item {text}");
                    }

                    break;
                default:
                    if (number is not null)
                    {
                        body.Add((style, text));
                    }

                    break;
            }
        }

        Flush();
        return (items, transitional);
    }

    private static string BillTopic(Item item) =>
        string.Join(", ", new[] { item.Schedule, item.BillPart, item.BillDivision }.Where(t => t is not null).Select(t => Title(t!)));

    private static string Title(string heading)
    {
        var dash = heading.IndexOf('—', StringComparison.Ordinal);
        return dash < 0 ? heading : heading[(dash + 1)..];
    }

    private static string? Label(string? heading) => heading?.Split('—')[0];

    // "Schedule 2—Digital duty of care" has the label "Sch 2".
    private static string ScheduleLabel(string schedule) => schedule.Split('—')[0].Replace("Schedule", "Sch", StringComparison.Ordinal);

    private static string Day(DateTimeOffset date) => date.ToString("d MMMM yyyy", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\d+[A-Z]*)\s+(.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^Repeal the (Part|Division|section)\b")]
    private static partial Regex RepealWhole();

    [GeneratedRegex(@"^Repeal the (paragraphs?|subsections?)\.$")]
    private static partial Regex RepealPart();

    [GeneratedRegex(@"^(Division \d+[A-Z]*) of (Part \d+[A-Z]*)$")]
    private static partial Regex DivisionOfPart();

    [GeneratedRegex(@"^(Part \d+[A-Z]*)$")]
    private static partial Regex PartOnly();

    [GeneratedRegex(@"^Section (\d+[A-Z]*)$")]
    private static partial Regex SectionOnly();

    [GeneratedRegex(@"(?:[Ss]ection|[Ss]ubsection|[Pp]aragraph|[Ss]ubparagraph)s? (\d+[A-Z]*)")]
    private static partial Regex SectionNumber();

    [GeneratedRegex(@"^Omit “(.+)”, substitute “(.+)”\.$")]
    private static partial Regex OmitSubstitute();

    [GeneratedRegex(@"^After “.+”, insert “(.+)”\.$")]
    private static partial Regex AfterInsert();

    [GeneratedRegex(@"\([a-z0-9]+\)")]
    private static partial Regex Labels();
}
