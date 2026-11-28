// One end-to-end test for each member of IGraphiti other than the ones of test E9, through a real service collection
// on the real Neo4j (plan 0004, layer 9, packet P9d). A query of the test reads the graph as the proof.
namespace Graphiti.E2E.Tests;

using global::Neo4j.Driver;
using Graphiti.PyCompat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

public sealed class GraphitiMembersTests
{
    private const int TimeLimit = 180_000;
    private const string Fact = "Ana works at Bo Corp.";

    private static readonly DateTimeOffset January = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact(Timeout = TimeLimit)]
    public async Task AddEpisodeBulkAsync_saves_each_episode()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        var result = await scope.Graphiti.AddEpisodeBulkAsync(
            [Episode("b1", January), Episode("b2", January.AddDays(1))],
            new AddEpisodeBulkOptions { GroupId = scope.Group },
            ct);

        Assert.Equal(["b1", "b2"], result.Episodes.Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Equal(2, await scope.CountAsync("MATCH (n:Episodic) WHERE n.group_id = $g RETURN count(n) AS c"));
        Assert.Equal(2, await scope.CountAsync("MATCH (n:Entity) WHERE n.group_id = $g RETURN count(n) AS c"));
        Assert.True(await scope.CountAsync("MATCH ()-[r:RELATES_TO]->() WHERE r.group_id = $g AND r.fact = $f RETURN count(r) AS c", new { f = Fact }) >= 1);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task AddTripletAsync_saves_the_nodes_and_the_edge()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        var source = new EntityNode { Name = "Ana", GroupId = scope.Group };
        var target = new EntityNode { Name = "Bo Corp", GroupId = scope.Group };
        var edge = new EntityEdge
        {
            GroupId = scope.Group,
            SourceNodeUuid = source.Uuid,
            TargetNodeUuid = target.Uuid,
            CreatedAt = January,
            Name = "WORKS_AT",
            Fact = Fact,
        };

        var result = await scope.Graphiti.AddTripletAsync(source, edge, target, cancellationToken: ct);

        Assert.Equal(["Ana", "Bo Corp"], result.Nodes.Select(n => n.Name).Order(StringComparer.Ordinal));
        var saved = Assert.Single(result.Edges, e => e.Fact == Fact);
        Assert.NotNull(saved.FactEmbedding);
        Assert.Null(edge.FactEmbedding);
        Assert.Equal(1, await scope.CountAsync(
            "MATCH (a:Entity {group_id: $g, name: 'Ana'})-[r:RELATES_TO {fact: $f}]->(b:Entity {name: 'Bo Corp'}) RETURN count(r) AS c",
            new { f = Fact }));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task AdvancedSearchAsync_returns_nodes_and_edges_with_their_scores()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        await scope.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);
        await AddAnaAsync(scope, ct);

        var results = await scope.Graphiti.AdvancedSearchAsync(
            "Ana Bo Corp", SearchRecipes.CombinedHybridSearchRrf, new AdvancedSearchOptions { GroupIds = [scope.Group] }, ct);

        Assert.Contains(results.Edges, e => e.Fact == Fact);
        Assert.Contains(results.Nodes, n => n.Name == "Ana");
        Assert.Equal(results.Edges.Count, results.EdgeRerankerScores.Count);
        Assert.Equal(results.Nodes.Count, results.NodeRerankerScores.Count);
        Assert.Equal(results.Episodes.Count, results.EpisodeRerankerScores.Count);
        Assert.All(results.Edges.Concat<object>(results.Nodes), item => Assert.Equal(scope.Group, item is Edge e ? e.GroupId : ((Node)item).GroupId));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task RetrieveEpisodesAsync_returns_the_last_episodes_of_the_group()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        var added = await AddAnaAsync(scope, ct);

        var episodes = await scope.Graphiti.RetrieveEpisodesAsync(
            January.AddDays(1), new RetrieveEpisodesOptions { GroupIds = [scope.Group], Source = EpisodeType.Text }, ct);

        var episode = Assert.Single(episodes);
        Assert.Equal(added.Episode.Uuid, episode.Uuid);
        Assert.Equal(Fact, episode.Content);
        Assert.Equal(January, episode.ValidAt);
        Assert.Empty(await scope.Graphiti.RetrieveEpisodesAsync(
            January.AddDays(1), new RetrieveEpisodesOptions { GroupIds = [scope.Group], Source = EpisodeType.Json }, ct));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task GetNodesAndEdgesByEpisodeAsync_returns_what_the_episode_references()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        var added = await AddAnaAsync(scope, ct);

        var results = await scope.Graphiti.GetNodesAndEdgesByEpisodeAsync([added.Episode.Uuid], scope.Group, ct);

        Assert.Contains(results.Edges, e => e.Fact == Fact);
        Assert.Equal(["Ana", "Bo Corp"], results.Nodes.Select(n => n.Name).Order(StringComparer.Ordinal));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task RemoveEpisodeAsync_removes_the_episode_and_what_only_it_created()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        var added = await AddAnaAsync(scope, ct);

        await scope.Graphiti.RemoveEpisodeAsync(added.Episode.Uuid, scope.Group, ct);

        Assert.Equal(0, await scope.CountAsync("MATCH (n:Episodic {uuid: $u}) RETURN count(n) AS c", new { u = added.Episode.Uuid }));
        Assert.Equal(0, await scope.CountAsync("MATCH ()-[r:RELATES_TO]->() WHERE r.group_id = $g RETURN count(r) AS c"));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task BuildCommunitiesAsync_builds_the_communities_of_the_group()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        await AddAnaAsync(scope, ct);

        var result = await scope.Graphiti.BuildCommunitiesAsync([scope.Group], ct);

        var community = Assert.Single(result.Communities);
        Assert.Equal(scope.Group, community.GroupId);
        Assert.Equal(2, result.CommunityEdges.Count);
        Assert.Equal(1, await scope.CountAsync("MATCH (c:Community) WHERE c.group_id = $g RETURN count(c) AS c"));
        Assert.Equal(2, await scope.CountAsync("MATCH (:Community {group_id: $g})-[r:HAS_MEMBER]->(:Entity) RETURN count(r) AS c"));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task SummarizeSagaAsync_saves_the_summary_of_the_saga()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        var added = await AddAnaAsync(scope, ct);
        var saga = NewUuid();
        await scope.QueryAsync(
            "MATCH (e:Episodic {uuid: $e}) "
            + "CREATE (s:Saga {uuid: $s, name: 'Ana saga', group_id: $g, summary: '', created_at: datetime($t)})-[:HAS_EPISODE {uuid: $h, group_id: $g, created_at: datetime($t)}]->(e)",
            new { e = added.Episode.Uuid, s = saga, h = NewUuid(), t = "2026-01-01T00:00:00Z" });

        var summarized = await scope.Graphiti.SummarizeSagaAsync(saga, scope.Group, ct);

        Assert.Equal(saga, summarized.Uuid);
        Assert.Equal(Fact, summarized.Summary);
        Assert.Contains(scope.Chat.Calls, c => c.Prompt == "summarize_sagas.summarize_saga");
        Assert.Equal(1, await scope.CountAsync("MATCH (s:Saga {uuid: $s}) WHERE s.summary = $f RETURN count(s) AS c", new { s = saga, f = Fact }));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task BuildIndicesAndConstraintsAsync_builds_the_full_text_indexes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);

        await scope.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);

        var names = (await scope.QueryAsync("SHOW FULLTEXT INDEXES YIELD name RETURN name")).Select(r => r["name"].As<string>()).ToList();
        Assert.Contains("node_name_and_summary", names);
        Assert.Contains("edge_name_and_fact", names);
        Assert.Contains("episode_content", names);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task TokenUsage_counts_each_model_call_by_prompt_name()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        scope.Graphiti.TokenUsage.Reset();
        await AddAnaAsync(scope, ct);

        var usage = scope.Graphiti.TokenUsage.GetUsage();
        var prompts = scope.Chat.Calls.GroupBy(c => c.Prompt).ToDictionary(g => g.Key, g => (long)g.Count());
        Assert.Equal(prompts.Keys.Order(StringComparer.Ordinal), usage.Keys.Order(StringComparer.Ordinal));
        foreach (var (name, count) in prompts)
        {
            Assert.Equal(new PromptTokenUsage { PromptName = name, CallCount = count, TotalInputTokens = 10 * count, TotalOutputTokens = 4 * count }, usage[name]);
        }

        var calls = scope.Chat.Calls.Count;
        Assert.Equal(new TokenUsage { InputTokens = 10 * calls, OutputTokens = 4 * calls }, scope.Graphiti.TokenUsage.GetTotalUsage());
        scope.Graphiti.TokenUsage.Reset();
        Assert.Empty(scope.Graphiti.TokenUsage.GetUsage());
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_canceled_token_gives_OperationCanceledException()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.Graphiti.SearchAsync("Ana", cancellationToken: canceled.Token));

        // A cancel in the middle of a call: the chat client cancels the token at its first call.
        using var during = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await using var cancelling = new GraphitiScope((prompt, user) =>
        {
            during.Cancel();
            return Ana(prompt, user);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AddAnaAsync(cancelling, during.Token));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_call_from_a_synchronization_context_completes_off_the_scheduler()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        await AddAnaAsync(scope, ct);
        using var context = new PumpContext();

        var (sameContext, onScheduler, taskOnScheduler) = await context.RunAsync(async () =>
        {
            var task = scope.Graphiti.RetrieveEpisodesAsync(January.AddDays(1), new RetrieveEpisodesOptions { GroupIds = [scope.Group] }, ct);
            var completedOnScheduler = task.ContinueWith(_ => Asyncio.IsOnLoop, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var episodes = await task;
            Assert.Single(episodes);
            return (SynchronizationContext.Current == context, Asyncio.IsOnLoop, await completedOnScheduler);
        });

        Assert.True(sameContext);
        Assert.False(onScheduler);
        Assert.False(taskOnScheduler);
        Assert.All(scope.Chat.Places, place => Assert.False(place.OnScheduler));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_blocking_wait_on_a_synchronization_context_does_not_deadlock()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(Ana);
        await AddAnaAsync(scope, ct);
        using var context = new PumpContext();

        // The thread of the context blocks on the task. The task completes off the context (ConfigureAwait(false)), so
        // the wait ends. A continuation that waits for the context would never run.
        var completed = await context.RunAsync(() => Task.FromResult(
            scope.Graphiti.RetrieveEpisodesAsync(January.AddDays(1), new RetrieveEpisodesOptions { GroupIds = [scope.Group] }, ct)
                .Wait(TimeSpan.FromSeconds(20), ct)));

        Assert.True(completed);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_prompt_override_changes_the_prompt_that_the_chat_client_receives()
    {
        var ct = TestContext.Current.CancellationToken;
        List<string> keys = [];
        await using var scope = new GraphitiScope(Ana, o => o.Prompts.Overrides["extract_nodes.extract_text"] = context =>
        {
            keys.AddRange(context.Values.Keys);
            var prompt = context.BuildDefault();
            return prompt with { User = prompt.User + "\nOVERRIDE " + context.Name + " " + context.Values["episode_content"] };
        });

        await AddAnaAsync(scope, ct);

        var call = Assert.Single(scope.Chat.Calls, c => c.Prompt == "extract_nodes.extract_text");
        Assert.Contains("OVERRIDE extract_nodes.extract_text " + Fact, call.User, StringComparison.Ordinal);
        Assert.Contains("episode_content", keys);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_route_sends_its_prompts_to_its_keyed_client_and_model()
    {
        var ct = TestContext.Current.CancellationToken;
        var edges = new ScriptedChatClient(Ana);
        await using var scope = new GraphitiScope(
            Ana,
            o =>
            {
                o.Llm.Model = "medium";
                o.Llm.Routes["extract_edges"] = new ModelRoute { ModelId = "edge-model", ChatClientKey = "edges" };
            },
            register: services => services.AddKeyedSingleton<IChatClient>("edges", edges));

        await AddAnaAsync(scope, ct);

        Assert.All(edges.Calls, c => Assert.StartsWith("extract_edges.", c.Prompt, StringComparison.Ordinal));
        Assert.Contains(edges.Calls, c => c.Prompt == "extract_edges.edge" && c.ModelId == "edge-model");
        Assert.DoesNotContain(scope.Chat.Calls, c => c.Prompt.StartsWith("extract_edges.", StringComparison.Ordinal));
        Assert.All(scope.Chat.Calls, c => Assert.Equal("medium", c.ModelId));

        // Lead choice L13: the routed calls count in the one usage tracker.
        Assert.Equal(edges.Calls.Count + scope.Chat.Calls.Count, scope.Graphiti.TokenUsage.GetUsage().Values.Sum(u => u.CallCount));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task The_embedding_generator_of_32_bit_vectors_comes_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var wide = new HashDoubleEmbeddingGenerator();
        await using var scope = new GraphitiScope(Ana, register: services => services.AddSingleton<IEmbeddingGenerator<string, Embedding<double>>>(wide));

        await AddAnaAsync(scope, ct);

        Assert.True(scope.Embedder.Calls > 0);
        Assert.Equal(0, wide.Calls);
    }

    internal static EpisodeInput Episode(string name, DateTimeOffset at) =>
        new() { Name = name, Content = Fact, SourceDescription = "chat", ReferenceTime = at, Source = EpisodeType.Text };

    internal static Task<AddEpisodeResult> AddAnaAsync(GraphitiScope scope, CancellationToken ct) =>
        scope.Graphiti.AddEpisodeAsync(Episode("a1", January), new AddEpisodeOptions { GroupId = scope.Group }, ct);

    /// <summary>The replies of one fact, "Ana works at Bo Corp.", by prompt name.</summary>
    internal static string? Ana(string prompt, string user) => prompt switch
    {
        "extract_nodes.extract_text" or "extract_nodes.extract_message" or "extract_nodes.extract_json" =>
            """{"extracted_entities": [{"name": "Ana", "entity_type_id": 0}, {"name": "Bo Corp", "entity_type_id": 0}]}""",
        "extract_edges.edge" => """
            {"edges": [{"source_entity_name": "Ana", "target_entity_name": "Bo Corp", "relation_type": "WORKS_AT",
              "fact": "Ana works at Bo Corp.", "valid_at": "2026-01-01T00:00:00Z", "invalid_at": null}]}
            """,
        "extract_nodes_and_edges.extract_message" => """
            {"extracted_entities": [{"name": "Ana", "entity_type_id": 0}, {"name": "Bo Corp", "entity_type_id": 0}],
             "edges": [{"source_entity_name": "Ana", "target_entity_name": "Bo Corp", "relation_type": "WORKS_AT",
              "fact": "Ana works at Bo Corp.", "episode_indices": [0]}]}
            """,
        "extract_edges.extract_timestamps_batch" =>
            """{"timestamps": [{"valid_at": "2026-01-01T00:00:00Z", "invalid_at": null}]}""",
        "dedupe_nodes.nodes" or "dedupe_nodes.node_list" => GraphitiE9Tests.NewNodes(user),
        "dedupe_edges.resolve_edge" => """{"duplicate_facts": [], "contradicted_facts": []}""",
        "summarize_nodes.summarize_pair" or "summarize_nodes.summarize_context" => """{"summary": "Ana works at Bo Corp."}""",
        "summarize_nodes.summary_description" => """{"description": "Ana and Bo Corp"}""",
        "extract_nodes.extract_summaries_batch" => """{"summaries": []}""",
        "summarize_sagas.summarize_saga" => """{"summary": "Ana works at Bo Corp."}""",
        _ => null,
    };

    private static string NewUuid() => Guid.NewGuid().ToString();

    /// <summary>A synchronization context with one thread that runs each posted callback in order.</summary>
    private sealed class PumpContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> queue = [];
        private readonly Thread thread;

        internal PumpContext()
        {
            thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            })
            { IsBackground = true };
            thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException();

        internal Task<T> RunAsync<T>(Func<Task<T>> body)
        {
            var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(
                async _ =>
                {
                    try
                    {
                        done.SetResult(await body());
                    }
                    catch (Exception e)
                    {
                        done.SetException(e);
                    }
                },
                null);
            return done.Task;
        }

        public void Dispose()
        {
            queue.CompleteAdding();
            thread.Join();
            queue.Dispose();
        }
    }
}
