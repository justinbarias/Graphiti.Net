// The model options of Graphiti on the requests that the fake chat client receives (plan 0004, layer 9, packet P9d, fix
// round): the model ids by model size, the max tokens, the temperature, the routes, the prompt overrides, the default
// reranker over IChatLogProbabilities, and the builder calls that replace a part of the container.
namespace Graphiti.E2E.Tests;

using System.Collections.Concurrent;
using global::Neo4j.Driver;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

public sealed class GraphitiModelOptionsTests
{
    private const int TimeLimit = 180_000;

    // Upstream passes its own max_tokens for two prompts: extract_edges.edge always asks for 16384
    // (edge_operations.py:171), and dedupe_nodes asks for min(llm_client.max_tokens, 16384) (node_operations.py:764).
    private const int ExtractEdgesMaxTokens = 16384;

    [Fact(Timeout = TimeLimit)]
    public async Task Model_SmallModel_MaxTokens_and_Temperature_reach_each_request()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(AnaLeads, o =>
        {
            o.Llm.Model = "big";
            o.Llm.SmallModel = "small";
            o.Llm.MaxTokens = 1234;
            o.Llm.Temperature = 0.25f;
        });

        // The second episode has a second fact of the same two entities, so dedupe_edges.resolve_edge runs with the
        // small model.
        await TwoAnaEpisodesAsync(scope, ct);

        var calls = scope.Chat.Calls.ToList();
        Assert.Contains(calls, c => c.ModelSize == "small");
        Assert.Contains(calls, c => c.ModelSize == "medium");
        Assert.All(calls, c =>
        {
            Assert.Equal(c.ModelSize == "small" ? "small" : "big", c.ModelId);
            Assert.Equal(c.Prompt == "extract_edges.edge" ? ExtractEdgesMaxTokens : 1234, c.Options!.MaxOutputTokens);
            Assert.Equal(0.25f, c.Options.Temperature);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task With_the_default_options_a_request_has_no_model_and_no_temperature()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana);

        await GraphitiMembersTests.AddAnaAsync(scope, ct);

        Assert.NotEmpty(scope.Chat.Calls);
        Assert.All(scope.Chat.Calls, c =>
        {
            Assert.Null(c.ModelId);
            Assert.Null(c.Options!.Temperature);
            Assert.Equal(16384, c.Options.MaxOutputTokens);
        });
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Each_route_sends_its_prompts_to_its_client_and_model_with_its_max_tokens()
    {
        var ct = TestContext.Current.CancellationToken;
        var nodes = new ScriptedChatClient(AnaLeads);
        await using var scope = new GraphitiScope(
            AnaLeads,
            o =>
            {
                o.Llm.Model = "medium";
                o.Llm.Routes["extract_nodes.extract_text"] = new ModelRoute { ModelId = "node-model", ChatClientKey = "nodes", MaxTokens = 7 };
                o.Llm.Routes["extract_edges"] = new ModelRoute { ModelId = "edge-group", MaxTokens = 9 };
                o.Llm.Routes["extract_edges.extract_attributes"] = new ModelRoute { ModelId = "attribute-model" };
                o.Llm.Routes["dedupe_edges.resolve_edge"] = new ModelRoute { ModelId = "resolve-model", MaxTokens = 11 };
            },
            register: services => services.AddKeyedSingleton<IChatClient>("nodes", nodes));

        await TwoAnaEpisodesAsync(scope, ct);

        Assert.All(nodes.Calls, c => Assert.Equal(("extract_nodes.extract_text", "node-model", 7), (c.Prompt, c.ModelId, c.Options!.MaxOutputTokens)));
        Assert.Equal(2, nodes.Calls.Count);

        // A group route stays the model of a prompt of the group with no route of its own. extract_edges.edge passes
        // its own max_tokens, which wins over the max tokens of the route (llm_runtime.py, complete).
        Assert.All(
            scope.Chat.Calls.Where(c => c.Prompt == "extract_edges.edge"),
            c => Assert.Equal(("edge-group", ExtractEdgesMaxTokens), (c.ModelId, c.Options!.MaxOutputTokens)));
        Assert.All(
            scope.Chat.Calls.Where(c => c.Prompt == "dedupe_edges.resolve_edge"),
            c => Assert.Equal(("resolve-model", 11), (c.ModelId, c.Options!.MaxOutputTokens)));
        Assert.Contains(scope.Chat.Calls, c => c.Prompt == "dedupe_edges.resolve_edge");
        Assert.Equal(2, scope.Chat.Calls.Count(c => c.Prompt == "extract_edges.edge"));
        Assert.All(
            scope.Chat.Calls.Where(c => !c.Prompt.StartsWith("extract_edges.", StringComparison.Ordinal) && c.Prompt != "dedupe_edges.resolve_edge"),
            c => Assert.Equal("medium", c.ModelId));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task Prompt_overrides_with_routes_reach_the_prompts_of_the_runtime()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana, o =>
        {
            o.Llm.Model = "medium";
            o.Prompts.Overrides["extract_edges.edge"] = Mark("GLOBAL");
            o.Llm.Routes["extract_edges.edge"] = new ModelRoute { ModelId = "edge-model" };
            o.Llm.Routes["extract_nodes"] = new ModelRoute
            {
                ModelId = "node-model",
                PromptOverrides = { ["extract_nodes.extract_text"] = Mark("ROUTE") },
            };
        });

        await GraphitiMembersTests.AddAnaAsync(scope, ct);

        var edge = Assert.Single(scope.Chat.Calls, c => c.Prompt == "extract_edges.edge");
        Assert.Equal("edge-model", edge.ModelId);
        Assert.Contains("\nGLOBAL extract_edges.edge", edge.User, StringComparison.Ordinal);
        var text = Assert.Single(scope.Chat.Calls, c => c.Prompt == "extract_nodes.extract_text");
        Assert.Equal("node-model", text.ModelId);
        Assert.Contains("\nROUTE extract_nodes.extract_text", text.User, StringComparison.Ordinal);
        Assert.DoesNotContain("GLOBAL", text.User, StringComparison.Ordinal);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task A_prompt_override_reads_each_context_value_in_its_public_form()
    {
        var ct = TestContext.Current.CancellationToken;
        var values = new ConcurrentDictionary<string, object?>();
        var tried = new ConcurrentDictionary<string, object?>();
        await using var scope = new GraphitiScope(GraphitiArgumentsTests.Typed, o => o.Prompts.Overrides["extract_edges.edge"] = context =>
        {
            foreach (var key in context.Values.Keys)
            {
                values[key] = context.Values[key];
                Assert.True(context.Values.TryGetValue(key, out var value));
                tried[key] = value;
            }

            return Mark("EDGE")(context);
        });

        var person = GraphTypeDefinition.Create("Person", "A human being.");
        var worksAt = GraphTypeDefinition.Create("WorksAt", "Employment of a person.");
        await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a1", new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)),
            new AddEpisodeOptions
            {
                GroupId = scope.Group,
                EntityTypes = [person],
                EdgeTypes = [worksAt],
                EdgeTypeMap = [new EdgeTypeRule { SourceType = "Person", TargetType = "Entity", EdgeTypes = ["WorksAt"] }],
            },
            ct);

        Assert.Contains("\nEDGE extract_edges.edge", Assert.Single(scope.Chat.Calls, c => c.Prompt == "extract_edges.edge").User, StringComparison.Ordinal);

        // The indexer and TryGetValue give the same public form.
        AssertPublicForm(values);
        AssertPublicForm(tried);
    }

    private static void AssertPublicForm(ConcurrentDictionary<string, object?> values)
    {
        // edge_operations.py builds fact_type_signatures as a list of (source, target) tuples. A tuple reads as a list.
        var edgeTypes = Assert.IsAssignableFrom<IReadOnlyList<object?>>(values["edge_types"]);
        var workType = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(Assert.Single(edgeTypes));
        Assert.Equal("WorksAt", workType["fact_type_name"]);
        var signature = Assert.IsAssignableFrom<IReadOnlyList<object?>>(Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<object?>>(workType["fact_type_signatures"])));
        Assert.Equal(["Person", "Entity"], signature);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), Assert.IsType<DateTimeOffset>(values["reference_time"]));
        var nodes = Assert.IsAssignableFrom<IReadOnlyList<object?>>(values["nodes"]);
        Assert.Equal(
            ["Ana", "Bo Corp"],
            nodes.Select(n => (string)Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(n)["name"]!).Order(StringComparer.Ordinal));
    }

    [Fact(Timeout = TimeLimit)]
    public async Task The_default_reranker_scores_each_passage_by_the_log_probability_hook()
    {
        var ct = TestContext.Current.CancellationToken;
        var hook = new ScriptedLogProbabilities();
        await using var scope = new GraphitiScope(
            GraphitiMembersTests.Ana,
            o => o.Reranker.Model = "rerank-model",
            build: b => b.UseChatLogProbabilities(_ => hook));
        scope.Chat.Rerank = user => user.Contains("Lisbon", StringComparison.Ordinal) ? "0.8" : "0.3";
        await scope.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);
        await TwoFactsAsync(scope, ct);

        var results = await scope.Graphiti.AdvancedSearchAsync(
            "Ana", SearchRecipes.EdgeHybridSearchCrossEncoder, new AdvancedSearchOptions { GroupIds = [scope.Group] }, ct);

        Assert.Equal(["Ana lives in Lisbon.", "Ana works at Bo Corp."], results.Edges.Select(e => e.Fact));
        Assert.Equal(2, results.EdgeRerankerScores.Count);
        Assert.Equal(0.8, results.EdgeRerankerScores[0], 12);
        Assert.Equal(0.3, results.EdgeRerankerScores[1], 12);

        // openai_reranker_client.py, rank: one call for each passage, with temperature 0, max_tokens 1, top_logprobs 2,
        // and the logit bias of the tokens True and False.
        Assert.Equal(2, scope.Chat.Reranks.Count);
        Assert.All(scope.Chat.Reranks, r => Assert.Equal(("rerank-model", 0f, 1), (r.Options!.ModelId, r.Options.Temperature, r.Options.MaxOutputTokens)));
        Assert.All(hook.Requests, r =>
        {
            Assert.Equal(2, r.Top);
            Assert.Equal(new Dictionary<int, int> { [6432] = 1, [7983] = 1 }, r.Bias);
        });
        Assert.Equal(2, hook.Requests.Count);
    }

    [Fact(Timeout = TimeLimit)]
    public async Task UseCrossEncoder_ranks_the_passages_in_place_of_the_default_reranker()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = new GraphitiScope(GraphitiMembersTests.Ana, build: b => b.UseCrossEncoder(_ => new WordCrossEncoder()));
        await scope.Graphiti.BuildIndicesAndConstraintsAsync(cancellationToken: ct);
        await TwoFactsAsync(scope, ct);

        var results = await scope.Graphiti.AdvancedSearchAsync(
            "Ana lives", SearchRecipes.EdgeHybridSearchCrossEncoder, new AdvancedSearchOptions { GroupIds = [scope.Group] }, ct);

        // WordCrossEncoder gives the share of the query words in the fact.
        Assert.Equal(["Ana lives in Lisbon.", "Ana works at Bo Corp."], results.Edges.Select(e => e.Fact));
        Assert.Equal([1.0, 0.5], results.EdgeRerankerScores);
        Assert.Empty(scope.Chat.Reranks);
    }

    [Theory(Timeout = TimeLimit)]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UseKeyedChatClient_and_UseEmbeddingGenerator_replace_the_parts_of_the_container(bool wideVectors)
    {
        var ct = TestContext.Current.CancellationToken;
        var keyed = new ScriptedChatClient(GraphitiMembersTests.Ana);
        var wide = new HashDoubleEmbeddingGenerator();
        var narrow = new HashEmbeddingGenerator();
        await using var scope = new GraphitiScope(
            GraphitiMembersTests.Ana,
            build: b =>
            {
                b.UseKeyedChatClient("graphiti");
                _ = wideVectors ? b.UseEmbeddingGenerator(_ => wide) : b.UseEmbeddingGenerator(_ => narrow);
            },
            register: services => services.AddKeyedSingleton<IChatClient>("graphiti", keyed));

        var added = await GraphitiMembersTests.AddAnaAsync(scope, ct);

        Assert.NotEmpty(keyed.Calls);
        Assert.Empty(scope.Chat.Calls);
        Assert.True((wideVectors ? wide.Calls : narrow.Calls) > 0);
        Assert.Equal(0, scope.Embedder.Calls);
        var stored = (await scope.QueryAsync("MATCH (n:Entity {uuid: $u}) RETURN n.name_embedding AS v", new { u = added.Nodes[0].Uuid }))
            .Single()["v"].As<List<double>>();
        Assert.Equal(HashEmbeddingGenerator.Vector(added.Nodes[0].Name, 1024).Select(v => (double)v), stored);
    }

    internal static async Task TwoFactsAsync(GraphitiScope scope, CancellationToken ct)
    {
        var ana = new EntityNode { Name = "Ana", GroupId = scope.Group };
        foreach (var (target, name, fact) in new[] { ("Bo Corp", "WORKS_AT", "Ana works at Bo Corp."), ("Lisbon", "LIVES_IN", "Ana lives in Lisbon.") })
        {
            var node = new EntityNode { Name = target, GroupId = scope.Group };
            await scope.Graphiti.AddTripletAsync(
                ana,
                new EntityEdge
                {
                    GroupId = scope.Group,
                    SourceNodeUuid = ana.Uuid,
                    TargetNodeUuid = node.Uuid,
                    CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    Name = name,
                    Fact = fact,
                },
                node,
                cancellationToken: ct);
        }

        Assert.Equal(2, await scope.CountAsync("MATCH ()-[r:RELATES_TO]->() WHERE r.group_id = $g RETURN count(r) AS c"));
    }

    /// <summary>The replies of <see cref="GraphitiMembersTests.Ana"/>, and a second fact for an episode that says "leads".</summary>
    internal static string? AnaLeads(string prompt, string user) =>
        prompt == "extract_edges.edge" && user.Contains("Ana leads Bo Corp.", StringComparison.Ordinal)
            ? """
              {"edges": [{"source_entity_name": "Ana", "target_entity_name": "Bo Corp", "relation_type": "LEADS",
                "fact": "Ana leads Bo Corp.", "valid_at": "2026-01-02T00:00:00Z", "invalid_at": null}]}
              """
            : GraphitiMembersTests.Ana(prompt, user);

    internal static async Task TwoAnaEpisodesAsync(GraphitiScope scope, CancellationToken ct)
    {
        await GraphitiMembersTests.AddAnaAsync(scope, ct);
        await scope.Graphiti.AddEpisodeAsync(
            GraphitiMembersTests.Episode("a2", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)) with { Content = "Ana leads Bo Corp." },
            new AddEpisodeOptions { GroupId = scope.Group },
            ct);
    }

    private static PromptBuilder Mark(string mark) => context =>
    {
        var prompt = context.BuildDefault();
        return prompt with { User = prompt.User + "\n" + mark + " " + context.Name };
    };

    /// <summary>A hook that records each request and reads the probability of True from the text of the reply.</summary>
    private sealed class ScriptedLogProbabilities : IChatLogProbabilities
    {
        internal ConcurrentQueue<(int Top, Dictionary<int, int> Bias)> Requests { get; } = new();

        public void Request(ChatOptions options, int topLogProbabilityCount, IReadOnlyDictionary<int, int> logitBias) =>
            Requests.Enqueue((topLogProbabilityCount, logitBias.ToDictionary()));

        public IReadOnlyList<TokenLogProbability>? ReadFirstToken(ChatResponse response) =>
            [new("True", Math.Log(double.Parse(response.Text, System.Globalization.CultureInfo.InvariantCulture)))];
    }
}
