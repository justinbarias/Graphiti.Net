namespace Quickstart;

using System.Diagnostics;
using System.Text.Json;
using Graphiti;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>The commands <c>split</c>, <c>ingest</c>, and <c>reset</c>.</summary>
internal static class Commands
{
    private const int DefaultWindow = 3;

    // The spend limit of the full run. The ingestion stops when the usage tracker passes one of these.
    private const long MaxInputTokens = 8_000_000;
    private const long MaxOutputTokens = 1_000_000;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Splits the Act and prints the count, the five largest episodes, and three samples. It calls no model.</summary>
    /// <param name="file">The path of the .docx file.</param>
    /// <param name="bill">The path of the .docx file of the Bill, or null. With a Bill, the command prints the split of the Bill.</param>
    /// <param name="assent">The day of the Royal Assent of the Bill.</param>
    public static void Split(string file, string? bill = null, DateOnly assent = default)
    {
        if (bill is not null)
        {
            SplitBill(file, bill, assent);
            return;
        }

        var episodes = ActSplitter.Split(file);
        var sections = episodes.Select(e => e.Ref.Section).Distinct().Count();
        Console.WriteLine($"Episodes: {episodes.Count}. Sections: {sections}.");
        Console.WriteLine($"Parts: {episodes.Select(e => e.Ref.Part).Distinct().Count()}. " +
            $"Divisions: {episodes.Where(e => e.Ref.Division is not null).Select(e => (e.Ref.Part, e.Ref.Division)).Distinct().Count()}. " +
            $"Subdivisions: {episodes.Where(e => e.Ref.Subdivision is not null).Select(e => (e.Ref.Part, e.Ref.Division, e.Ref.Subdivision)).Distinct().Count()}.");
        Console.WriteLine($"Words in all episodes: {episodes.Sum(e => e.WordCount)}.");
        foreach (var word in new[] { "cyber-abuse", "end-user", "cyberabuse", "enduser" })
        {
            Console.WriteLine($"Count of '{word}' in the text of the episodes: {episodes.Sum(e => CountOf(e.Content, word))}.");
        }

        Console.WriteLine();
        Console.WriteLine("The five largest episodes (words):");
        foreach (var e in episodes.OrderByDescending(e => e.WordCount).Take(5))
        {
            Console.WriteLine($"  {e.WordCount,5}  {e.Ref.Name}");
        }

        Console.WriteLine();
        foreach (var index in new[] { 0, episodes.Count / 3, episodes.Count * 2 / 3 })
        {
            var e = episodes[index];
            Console.WriteLine($"--- Sample {index + 1}. Name: {e.Ref.Name}");
            Console.WriteLine($"SourceDescription: {e.SourceDescription}");
            Console.WriteLine($"ReferenceTime: {e.ReferenceTime:O}");
            Console.WriteLine("Content:");
            Console.WriteLine(e.Content);
            Console.WriteLine();
        }
    }

    private static void SplitBill(string file, string bill, DateOnly assent)
    {
        var split = BillSplitter.Split(bill, ActSplitter.Split(file), assent);
        var episodes = split.Episodes;
        Console.WriteLine($"Episodes of the Bill: {episodes.Count}. Items left out: {split.Skipped.Count}.");
        foreach (var kind in new[] { "inserted by", "repealed by", "amended by" })
        {
            Console.WriteLine($"  {kind,-12} {episodes.Count(e => e.Ref.Name.Contains($"({kind} ", StringComparison.Ordinal))}");
        }

        Console.WriteLine($"  {"other",-12} {episodes.Count(e => !e.Ref.Name.Contains(" by the ", StringComparison.Ordinal))}");
        foreach (var group in episodes.GroupBy(e => e.ReferenceTime).OrderBy(g => g.Key))
        {
            Console.WriteLine($"Reference time {group.Key:yyyy-MM-dd}: {group.Count()} episodes.");
        }

        Console.WriteLine($"Words in all episodes: {episodes.Sum(e => e.WordCount)}.");
        Console.WriteLine();
        Console.WriteLine("The episodes (item, words, name):");
        foreach (var e in episodes)
        {
            Console.WriteLine($"  {e.Ref.Item,-7} {e.WordCount,5}  {e.Ref.Name}");
        }

        Console.WriteLine();
        Console.WriteLine("The items that are left out:");
        foreach (var line in split.Skipped)
        {
            Console.WriteLine($"  {line}");
        }

        Console.WriteLine();
        foreach (var kind in new[] { "(inserted by", "(repealed by", "(amended by" })
        {
            if (episodes.FirstOrDefault(e => e.Ref.Name.Contains(kind, StringComparison.Ordinal)) is { } e)
            {
                Console.WriteLine($"--- Sample. Name: {e.Ref.Name}");
                Console.WriteLine($"SourceDescription: {e.SourceDescription}");
                Console.WriteLine($"ReferenceTime: {e.ReferenceTime:O}");
                Console.WriteLine("Content:");
                Console.WriteLine(e.Content.Length > 1200 ? e.Content[..1200] + " ..." : e.Content);
                Console.WriteLine();
            }
        }
    }

    private static int CountOf(string text, string word)
    {
        var count = 0;
        for (var i = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(word, i + word.Length, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }

    /// <summary>Ingests the first sections of the Act into the graph.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="file">The path of the .docx file.</param>
    /// <param name="limit">The count of sections to ingest, or null for all.</param>
    /// <param name="resume">True to skip each section whose episode exists in the group.</param>
    /// <param name="window">The count of episodes that Graphiti works on at the same time. 0 or 1 gives serial ingestion.</param>
    /// <param name="sections">The section numbers to ingest, or null for all.</param>
    /// <param name="bill">The path of the .docx file of a Bill that amends the Act, or null.</param>
    /// <param name="items">The items of the Bill to ingest, each as Schedule:item, or null for all.</param>
    /// <returns>A task that ends when the ingestion ends.</returns>
    public static async Task IngestAsync(
        Settings settings, string file, int? limit, bool resume, int window = DefaultWindow, IReadOnlyList<string>? sections = null,
        string? bill = null, IReadOnlyList<string>? items = null)
    {
        // With a Bill, the command ingests the episodes of the Bill only. The Act gives the heading and the text of each
        // provision that the Bill changes.
        var fullList = bill is null
            ? ActSplitter.Split(file)
            : BillSplitter.Split(bill, ActSplitter.Split(file), settings.BillAssentDate).Episodes;
        var all = fullList;
        if (items is not null)
        {
            if (items.FirstOrDefault(i => !all.Any(e => e.Ref.Item == i)) is { } none)
            {
                throw new QuickstartException($"The Bill has no episode for the item '{none}'. Give an item as Schedule:item, for example 2:13.");
            }

            all = [.. all.Where(e => items.Contains(e.Ref.Item, StringComparer.Ordinal))];
        }

        var sectionNumbers = all.Select(e => e.Ref.Section).Distinct().ToList();
        if (sections is not null)
        {
            if (sections.FirstOrDefault(s => !sectionNumbers.Contains(s, StringComparer.Ordinal)) is { } missing)
            {
                throw new QuickstartException($"The Act has no section '{missing}'.");
            }

            all = [.. all.Where(e => sections.Contains(e.Ref.Section, StringComparer.Ordinal))];
        }

        if (limit is { } n)
        {
            // For the Act, the limit counts sections. For a Bill, it counts episodes.
            var keep = all.Select(e => e.Ref.Section).Distinct().Take(n).ToHashSet(StringComparer.Ordinal);
            all = bill is null ? [.. all.Where(e => keep.Contains(e.Ref.Section))] : [.. all.Take(n)];
        }

        await using var provider = BuildProvider(settings, window > 1 ? window : null);
        var graphiti = provider.GetRequiredService<IGraphiti>();
        await graphiti.BuildIndicesAndConstraintsAsync();

        var present = await graphiti.Nodes.Episode.GetByGroupIdsAsync([settings.GroupId]);
        if (!resume && bill is null && present.Count > 0)
        {
            throw new QuickstartException(
                $"The group '{settings.GroupId}' already has {present.Count} episodes. Use --resume to continue, or run the command reset first.");
        }

        var refs = all.Select(e => e.Ref).ToList();
        var existing = new Dictionary<string, string>(StringComparer.Ordinal);
        var unfinished = new Dictionary<string, string>(StringComparer.Ordinal);
        if (resume || bill is not null)
        {
            // Graphiti saves an episode when it starts the work, so an episode in the graph can be unfinished. A
            // finished episode has a record with its uuid in the sections file, which the command writes only after the
            // call returns, or it has at least one entity that it mentions. A section with no entity of the ontology
            // (for example the short title) is finished by its record. Graphiti processes an unfinished episode again
            // if the call gives its uuid.
            var recorded = ReadSections(settings).Where(r => r.EpisodeUuid is not null).Select(r => r.EpisodeUuid!).ToHashSet(StringComparer.Ordinal);
            var selected = all.Select(e => e.Ref.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var episode in (await graphiti.Nodes.Episode.GetByGroupIdsAsync([settings.GroupId])).Where(e => selected.Contains(e.Name)))
            {
                if (recorded.Contains(episode.Uuid)
                    || (await graphiti.GetNodesAndEdgesByEpisodeAsync([episode.Uuid], settings.GroupId)).Nodes.Count > 0)
                {
                    existing[episode.Name] = episode.Uuid;
                }
                else
                {
                    unfinished[episode.Name] = episode.Uuid;
                }
            }
        }

        // A repeal goes to no model. The step after the model episodes ends the facts of each repealed section.
        var repeals = all.Where(e => e.RepealedSection is not null).ToList();
        var todo = all.Where(e => e.RepealedSection is null && !existing.ContainsKey(e.Ref.Name)).ToList();
        var total = todo.Count + repeals.Count;
        Console.WriteLine($"Episodes to ingest: {todo.Count} of {all.Count - repeals.Count} ({all.Count(e => e.RepealedSection is null && existing.ContainsKey(e.Ref.Name))} finished in the graph, {unfinished.Count} unfinished). Repeals to apply: {repeals.Count}. Window: {Math.Max(window, 1)}.");
        for (var i = 0; i < refs.Count; i++)
        {
            if (existing.TryGetValue(refs[i].Name, out var uuid))
            {
                refs[i] = refs[i] with { EpisodeUuid = uuid };
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var gate = new SemaphoreSlim(Math.Max(window, 1));
        var writeLock = new object();
        var done = 0;
        var nodes = 0;
        var edges = 0;
        var tasks = new List<Task>();
        Exception? firstError = null;
        using var cancel = new CancellationTokenSource();
        foreach (var episode in todo)
        {
            try
            {
                await gate.WaitAsync(cancel.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var started = Stopwatch.GetTimestamp();
                    var result = await graphiti.AddEpisodeAsync(
                        new EpisodeInput
                        {
                            Name = episode.Ref.Name,
                            Content = episode.Content,
                            SourceDescription = episode.SourceDescription,
                            ReferenceTime = episode.ReferenceTime,
                            Source = EpisodeType.Text,
                            Uuid = unfinished.GetValueOrDefault(episode.Ref.Name),
                        },
                        Ontology.Options(settings.GroupId),
                        cancel.Token);
                    lock (writeLock)
                    {
                        done++;
                        if (graphiti.TokenUsage.GetTotalUsage() is { } used
                            && (used.InputTokens > MaxInputTokens || used.OutputTokens > MaxOutputTokens))
                        {
                            Interlocked.CompareExchange(
                                ref firstError, new QuickstartException("The token use passed the spend limit. The ingestion stopped."), null);
                            cancel.Cancel();
                        }

                        nodes += result.Nodes.Count;
                        edges += result.Edges.Count;
                        var index = refs.FindIndex(r => r.Name == episode.Ref.Name);
                        refs[index] = refs[index] with { EpisodeUuid = result.Episode.Uuid };
                        WriteSections(settings, fullList, refs);
                        Console.WriteLine(
                            $"[{done}/{total}] {episode.Ref.Name}: {result.Nodes.Count} entities, {result.Edges.Count} facts, " +
                            $"{Stopwatch.GetElapsedTime(started).TotalSeconds:F1}s");
                    }
                }
                catch (Exception error)
                {
                    // Keep the first error. The cancel stops the other episodes, whose errors are only a result of it.
                    Interlocked.CompareExchange(ref firstError, error, null);
                    await cancel.CancelAsync();
                }
                finally
                {
                    gate.Release();
                }
            }));
        }

        await Task.WhenAll(tasks);
        stopwatch.Stop();
        WriteSections(settings, fullList, refs);
        if (firstError is not null)
        {
            PrintUsage(graphiti);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(firstError);
        }

        var ended = repeals.Count > 0 ? await ApplyRepealsAsync(graphiti, settings, fullList, refs, repeals, done, total) : 0;
        stopwatch.Stop();
        Console.WriteLine();
        Console.WriteLine($"Episodes ingested: {done}. Entities returned: {nodes}. Facts returned: {edges}.");
        if (repeals.Count > 0)
        {
            Console.WriteLine($"Repeals applied: {repeals.Count}. Facts ended: {ended}.");
        }

        Console.WriteLine($"Time: {stopwatch.Elapsed:hh\\:mm\\:ss}.");
        PrintUsage(graphiti);
    }

    // Applies the repeals of a Bill with no call to the chat model. Each fact that ends costs one call to the embedding
    // model, because the save of a fact embeds its text again. A repeal is a fact about the text: the section stops being law
    // on a date. So the command does not ask a model which facts end. It saves one episode for each repealed section, as
    // the source of the change, and it ends each fact whose source episodes are all repealed: InvalidAt becomes the day
    // of the repeal. A fact that another section also states stays open. The step can run two times: it skips a fact
    // that has an end date.
    private static async Task<int> ApplyRepealsAsync(
        IGraphiti graphiti, Settings settings, IReadOnlyList<ActEpisode> fullList, List<ProvisionRef> refs,
        List<ActEpisode> repeals, int done, int total)
    {
        var episodes = (await graphiti.Nodes.Episode.GetByGroupIdsAsync([settings.GroupId])).ToList();
        var byName = episodes.GroupBy(e => e.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        // The source episodes that each repeal ends: the episodes of the section that are older than the repeal. They
        // are the episode of the Act, and each episode of a Bill that changed the section before the repeal.
        var endedSources = new Dictionary<string, (DateTimeOffset Date, string RepealUuid)>(StringComparer.Ordinal);
        var sourcesOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        // A fact can have sources in two repealed sections. So the step also reads each repeal that an earlier run applied,
        // and not only the repeals of this run.
        var selected = repeals.Select(r => r.Ref.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var repeal in fullList.Where(e => e.RepealedSection is not null))
        {
            if (!selected.Contains(repeal.Ref.Name) && !byName.ContainsKey(repeal.Ref.Name))
            {
                continue;
            }

            if (!byName.TryGetValue(repeal.Ref.Name, out var node))
            {
                node = await graphiti.Nodes.Episode.SaveAsync(new EpisodicNode
                {
                    Name = repeal.Ref.Name,
                    GroupId = settings.GroupId,
                    Source = EpisodeType.Text,
                    SourceDescription = repeal.SourceDescription,
                    Content = repeal.Content,
                    ValidAt = repeal.ReferenceTime,
                });
                byName[node.Name] = node;
            }

            if (refs.FindIndex(r => r.Name == repeal.Ref.Name) is var index and >= 0)
            {
                refs[index] = refs[index] with { EpisodeUuid = node.Uuid };
            }

            var prefix = $"{ActSplitter.ActShortName} s {repeal.RepealedSection}";
            var sources = episodes
                .Where(e => (e.Name == prefix || e.Name.StartsWith(prefix + " (", StringComparison.Ordinal))
                    && !e.Name.Contains("(repealed by", StringComparison.Ordinal) && e.ValidAt < repeal.ReferenceTime)
                .Select(e => e.Uuid).ToList();
            sourcesOf[repeal.Ref.Name] = sources;
            foreach (var uuid in sources)
            {
                endedSources[uuid] = (repeal.ReferenceTime, node.Uuid);
            }
        }

        var repealUuids = endedSources.Values.Select(s => s.RepealUuid).ToHashSet(StringComparer.Ordinal);
        var facts = await graphiti.Edges.Entity.GetByGroupIdsAsync([settings.GroupId]);
        var endedBy = repeals.ToDictionary(r => r.Ref.Name, _ => 0, StringComparer.Ordinal);
        var now = DateTimeOffset.UtcNow;
        var count = 0;
        foreach (var fact in facts)
        {
            // A source that is a repeal episode is not a statement of the fact, so it does not count.
            var stated = fact.Episodes.Where(u => !repealUuids.Contains(u)).ToList();
            if (stated.Count == 0 || !stated.All(endedSources.ContainsKey))
            {
                continue;
            }

            // The fact ends when its last source ends. A fact that has this end date or an earlier one stays as it is.
            var last = stated.Select(u => endedSources[u]).MaxBy(s => s.Date);
            if (fact.InvalidAt is { } end && end <= last.Date)
            {
                continue;
            }

            await graphiti.Edges.Entity.SaveAsync(fact with
            {
                InvalidAt = last.Date,
                ExpiredAt = now,
                Episodes = [.. fact.Episodes, .. stated.Select(u => endedSources[u].RepealUuid).Distinct(StringComparer.Ordinal).Where(u => !fact.Episodes.Contains(u))],
            });
            count++;
            var name = byName.Values.First(e => e.Uuid == last.RepealUuid).Name;
            if (endedBy.TryGetValue(name, out var soFar))
            {
                endedBy[name] = soFar + 1;
            }
        }

        foreach (var repeal in repeals)
        {
            done++;
            Console.WriteLine(
                $"[{done}/{total}] {repeal.Ref.Name}: repeal, {sourcesOf[repeal.Ref.Name].Count} source episodes, {endedBy[repeal.Ref.Name]} facts ended");
        }

        WriteSections(settings, fullList, refs);
        return count;
    }

    /// <summary>Deletes the data of the group of the example, and nothing else. It asks for the word yes.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>A task that ends when the data is deleted.</returns>
    public static async Task ResetAsync(Settings settings)
    {
        Console.Write($"This deletes all episodes, entities, and facts of the group '{settings.GroupId}'. Type yes to continue: ");
        if (!string.Equals(Console.ReadLine()?.Trim(), "yes", StringComparison.Ordinal))
        {
            Console.WriteLine("Nothing was deleted.");
            return;
        }

        await using var provider = BuildProvider(settings, null);
        var graphiti = provider.GetRequiredService<IGraphiti>();
        // The batch delete of a group (DeleteByGroupIdAsync) fails on Neo4j (GAPS U1). RemoveEpisodeAsync works, but it
        // asks the model to rebuild the summaries of the entities that stay, and it took about 5 minutes for one episode of
        // the full graph. So the command deletes each node with DeleteAsync, which also deletes the edges of the node.
        // It makes no model call and touches only nodes that it read for this group id.
        var stopwatch = Stopwatch.StartNew();
        var count = 0;
        var episodes = await graphiti.Nodes.Episode.GetByGroupIdsAsync([settings.GroupId]);
        foreach (var episode in episodes)
        {
            await graphiti.Nodes.Episode.DeleteAsync(episode);
            count++;
        }

        var entityCount = 0;
        while (true)
        {
            var page = await graphiti.Nodes.Entity.GetByGroupIdsAsync([settings.GroupId], 100);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var entity in page)
            {
                await graphiti.Nodes.Entity.DeleteAsync(entity);
                entityCount++;
            }
        }

        if (File.Exists(settings.SectionsFile))
        {
            File.Delete(settings.SectionsFile);
        }

        Console.WriteLine($"Done. {count} episodes and {entityCount} entities removed in {stopwatch.Elapsed:hh\\:mm\\:ss}.");
        PrintUsage(graphiti);
    }

    /// <summary>Reads sections.json.</summary>
    /// <param name="settings">The settings.</param>
    /// <returns>The records, or an empty list if the file does not exist.</returns>
    public static IReadOnlyList<ProvisionRef> ReadSections(Settings settings) =>
        File.Exists(settings.SectionsFile)
            ? JsonSerializer.Deserialize<List<ProvisionRef>>(File.ReadAllText(settings.SectionsFile)) ?? []
            : [];

    /// <summary>Builds the service provider of the example, with Graphiti and quiet logging.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="ingestWindow">The window of concurrent ingestion, or null.</param>
    /// <returns>The service provider.</returns>
    public static ServiceProvider BuildProvider(Settings settings, int? ingestWindow)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning).AddSimpleConsole());
        services.AddQuickstartGraphiti(settings, ingestWindow);
        return services.BuildServiceProvider();
    }

    // The file of the group keeps the record of each episode that an earlier run made. A run with --limit or --resume
    // changes only the records of the sections that it processed. It never drops another record.
    private static void WriteSections(Settings settings, IReadOnlyList<ActEpisode> fullList, List<ProvisionRef> processed)
    {
        var known = ReadSections(settings).ToDictionary(r => r.Name, StringComparer.Ordinal);
        foreach (var record in processed)
        {
            if (record.EpisodeUuid is not null || !known.ContainsKey(record.Name))
            {
                known[record.Name] = record;
            }
        }

        var ordered = new List<ProvisionRef>();
        foreach (var episode in fullList)
        {
            if (known.Remove(episode.Ref.Name, out var record))
            {
                ordered.Add(record);
            }
        }

        ordered.AddRange(known.Values);
        Directory.CreateDirectory(settings.DataDirectory);
        File.WriteAllText(settings.SectionsFile, JsonSerializer.Serialize(ordered, JsonOptions));
    }

    private static void PrintUsage(IGraphiti graphiti)
    {
        Console.WriteLine();
        Console.WriteLine("Token use by prompt:");
        Console.WriteLine($"  {"prompt",-42} {"calls",6} {"input",10} {"output",9}");
        foreach (var (name, usage) in graphiti.TokenUsage.GetUsage().OrderByDescending(p => p.Value.TotalTokens))
        {
            Console.WriteLine($"  {name,-42} {usage.CallCount,6} {usage.TotalInputTokens,10} {usage.TotalOutputTokens,9}");
        }

        var total = graphiti.TokenUsage.GetTotalUsage();
        Console.WriteLine($"  {"total",-42} {"",6} {total.InputTokens,10} {total.OutputTokens,9}");
        Console.WriteLine();
        Console.WriteLine(
            $"Model calls with a schema: {StructuredOutputChatClient.SchemaCalls}. Calls with no schema: {StructuredOutputChatClient.PlainCalls}.");
        foreach (var (name, error) in StructuredOutputChatClient.RejectedSchemas)
        {
            Console.WriteLine($"Schema rejected by the provider: {name}: {error}");
        }
    }
}
