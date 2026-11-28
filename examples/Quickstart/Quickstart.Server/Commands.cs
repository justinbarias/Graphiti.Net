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
    public static void Split(string file)
    {
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
            Console.WriteLine($"ReferenceTime: {ActSplitter.AssentDate:O}");
            Console.WriteLine("Content:");
            Console.WriteLine(e.Content);
            Console.WriteLine();
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
    /// <returns>A task that ends when the ingestion ends.</returns>
    public static async Task IngestAsync(
        Settings settings, string file, int? limit, bool resume, int window = DefaultWindow, IReadOnlyList<string>? sections = null)
    {
        var fullList = ActSplitter.Split(file);
        var all = fullList;
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
            var keep = all.Select(e => e.Ref.Section).Distinct().Take(n).ToHashSet(StringComparer.Ordinal);
            all = [.. all.Where(e => keep.Contains(e.Ref.Section))];
        }

        await using var provider = BuildProvider(settings, window > 1 ? window : null);
        var graphiti = provider.GetRequiredService<IGraphiti>();
        await graphiti.BuildIndicesAndConstraintsAsync();

        var present = await graphiti.Nodes.Episode.GetByGroupIdsAsync([settings.GroupId]);
        if (!resume && present.Count > 0)
        {
            throw new QuickstartException(
                $"The group '{settings.GroupId}' already has {present.Count} episodes. Use --resume to continue, or run the command reset first.");
        }

        var refs = all.Select(e => e.Ref).ToList();
        var existing = new Dictionary<string, string>(StringComparer.Ordinal);
        var unfinished = new Dictionary<string, string>(StringComparer.Ordinal);
        if (resume)
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

        var todo = all.Where(e => !existing.ContainsKey(e.Ref.Name)).ToList();
        Console.WriteLine($"Episodes to ingest: {todo.Count} of {all.Count} ({existing.Count} finished in the graph, {unfinished.Count} unfinished). Window: {Math.Max(window, 1)}.");
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
                            ReferenceTime = ActSplitter.AssentDate,
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
                            $"[{done}/{todo.Count}] {episode.Ref.Name}: {result.Nodes.Count} entities, {result.Edges.Count} facts, " +
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

        Console.WriteLine();
        Console.WriteLine($"Episodes ingested: {done}. Entities returned: {nodes}. Facts returned: {edges}.");
        Console.WriteLine($"Time: {stopwatch.Elapsed:hh\\:mm\\:ss}.");
        PrintUsage(graphiti);
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
