// Evaluation test for graphiti_core/search/search.py (port/RULES.md, R23, R25).
// The recording is values/search.json, which port/tools/record_search.py wrote from Python. Fakes replace the graph
// driver, the embedder, the cross-encoder, and the tracer on both sides. The test asserts on the ranked uuids and the
// scores, and on one ordered list of the queries, the embedder and cross-encoder calls, the spans with their
// attributes and status, and the log lines. It also compares the largest count of calls that ran at the same time.
// The cancel grid is the second recording, values/search_cancel.json: it cancels the search while the k-th query, the
// k-th embedder call, or the k-th cross-encoder call returns. Python has no token, so the test also asserts that each
// call of a fake got the token that the test gave to the search.
namespace Graphiti.Port.Evaluation;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Driver;
using Graphiti.Core.Embedder;
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

public class SearchValuesTests
{
    private static readonly JsonElement Document = Load();

    private static readonly JsonElement CancelDocument = Load("search_cancel.json");

    private static readonly List<JsonElement> Cases = Document.GetProperty("cases").EnumerateArray().ToList();

    private static readonly List<JsonElement> CancelCases = CancelDocument.GetProperty("cases").EnumerateArray().ToList();

    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    public static TheoryData<int> CancelIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < CancelCases.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Fact]
    public void The_recording_holds_each_recipe_each_reranker_and_each_scope_function()
    {
        var inputs = Cases.Select(c => c.GetProperty("input")).ToList();
        Assert.Equal(16, inputs.Where(i => i.TryGetProperty("recipe", out _)).Select(i => i.GetProperty("recipe").GetString()).Distinct().Count());
        Assert.Equal(
            ["community_search", "edge_search", "episode_search", "node_search", "search"],
            inputs.Select(i => i.GetProperty("fn").GetString()).Distinct().Order(StringComparer.Ordinal).ToList());
        Assert.Equal(
            ["GroupIdValidationError", "SearchRerankerError"],
            Cases.Select(c => c.GetProperty("output").GetProperty("error")).Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => e.GetProperty("type").GetString()).Distinct().Order(StringComparer.Ordinal).ToList());

        // Each of the 32 span names of search.py appears in the recording.
        var spans = Cases.SelectMany(c => c.GetProperty("output").GetProperty("events").EnumerateArray())
            .Where(e => e.GetProperty("kind").GetString() == "span").Select(e => e.GetProperty("name").GetString()).ToHashSet();
        Assert.Equal(32, spans.Count);
    }

    // PORT: each recipe in Python is the same object as in the port. The recording holds its JSON dump, so that a
    // difference of a recipe shows here and not as a different search.
    [Fact]
    public void Each_recipe_matches_its_recorded_dump()
    {
        foreach (var recipe in Document.GetProperty("recipes").EnumerateObject())
        {
            Assert.Equal(
                PyJson.Dumps(Read(recipe.Value)),
                PyJson.Dumps(Recipe(recipe.Name).ModelDump(json: true)));
        }
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(CaseIndexes))]
    public async Task Each_result_call_span_and_log_line_matches_Python(int index)
    {
        var input = Cases[index].GetProperty("input");
        var output = Cases[index].GetProperty("output");

        var run = await ExecuteAsync(input, TestContext.Current.CancellationToken);

        var expectedError = output.GetProperty("error");
        if (expectedError.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(run.Error);
            AssertSame(JsonNode.Parse(output.GetProperty("result").GetRawText()), run.Result);
        }
        else
        {
            Assert.NotNull(run.Error);
            Assert.Equal(expectedError.GetProperty("type").GetString(), PythonName(run.Error));

            // The text of a CancelledError is empty in Python, and the text of an OperationCanceledException is not.
            if (run.Error is not OperationCanceledException)
            {
                Assert.Equal(expectedError.GetProperty("text").GetString(), run.Error.Message);
            }
        }

        var expectedEvents = output.GetProperty("events").EnumerateArray().ToList();
        Assert.True(
            expectedEvents.Count == run.Events.Count,
            $"expected {expectedEvents.Count} events but got {run.Events.Count}: {string.Join(" | ", run.Events.Select(Short))}");
        for (var i = 0; i < run.Events.Count; i++)
        {
            AssertSame(Untimed(JsonNode.Parse(expectedEvents[i].GetRawText())!), Untimed(run.Events[i]));
        }

        Assert.Equal(output.GetProperty("max_in_flight").GetInt32(), run.MaxInFlight);
    }

    [Fact]
    public void The_cancel_grid_holds_each_recipe_each_limit_and_each_kind_of_call()
    {
        // The two files share the fixture of the fakes.
        foreach (var key in new[] { "tables", "embeddings", "connected", "mentions" })
        {
            Assert.True(
                JsonNode.DeepEquals(JsonNode.Parse(Document.GetProperty(key).GetRawText()), JsonNode.Parse(CancelDocument.GetProperty(key).GetRawText())),
                $"{key} differs between search.json and search_cancel.json");
        }

        var points = CancelCases.Select(c => c.GetProperty("input")).ToList();
        var recipes = points.Select(p => p.GetProperty("recipe").GetString()).Distinct().ToList();
        Assert.Equal(8, recipes.Count);
        foreach (var recipe in recipes)
        {
            Assert.Equal(
                [null, 1L, 2L],
                points.Where(p => p.GetProperty("recipe").GetString() == recipe).Select(MaxCoroutines).Distinct().OrderBy(m => m ?? 0).ToList());
        }

        foreach (var call in CallKinds)
        {
            Assert.Contains(points, p => p.TryGetProperty($"cancel_at_{call}", out _));
        }

        // Python ends each canceled search with CancelledError, whatever the point.
        Assert.All(CancelCases, c => Assert.Equal("CancelledError", c.GetProperty("output").GetProperty("error").GetProperty("type").GetString()));
        Assert.Equal(CancelCases.Count, points.Select(CancelKey).Distinct().Count());
    }

    // Each entry of LateCancel names one point of the grid.
    [Fact]
    public void Each_late_cancel_entry_names_one_point_of_the_grid()
    {
        var keys = CancelCases.Select(c => CancelKey(c.GetProperty("input"))).ToHashSet();
        Assert.Equal(LateCancel.Length, LateCancel.Select(e => CancelKey(e.Recipe, e.MaxCoroutines, e.Call, e.K)).Distinct().Count());
        Assert.All(LateCancel, e => Assert.Contains(CancelKey(e.Recipe, e.MaxCoroutines, e.Call, e.K), keys));
    }

    // The grid of search_cancel.json cancels the search while the k-th call of one kind returns. Python cancels the task of
    // the search, and the port cancels the token source. A point that is not in LateCancel must give the exception type,
    // the ordered spans, queries, calls, and log lines, and the count of calls in flight that Python gave. A point in
    // LateCancel must differ in the way that its kind says, so a listed point that agrees fails the test, and the list
    // cannot go stale. The cause of each listed point is GAPS F33 (late cancel, R8): the cancel arrives while a task waits
    // to resume after a call that already returned. Python raises CancelledError where that task resumes. The port reads
    // the token at its next call, so it can run more spans, return a full result, or end the spans in another order.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(CancelIndexes))]
    public async Task Each_cancel_point_matches_Python_or_is_a_listed_late_cancel(int index)
    {
        var input = CancelCases[index].GetProperty("input");
        var run = await ExecuteAsync(input, TestContext.Current.CancellationToken);

        var kind = Compare(CancelCases[index].GetProperty("output"), run);
        var key = CancelKey(input);
        var listed = LateCancel.Where(e => CancelKey(e.Recipe, e.MaxCoroutines, e.Call, e.K) == key).Select(e => e.Kind).ToList();
        if (listed.Count == 0)
        {
            Assert.True(kind == "same", $"{key}: the port differs from Python in kind {kind}: {string.Join(" | ", run.Events.Select(Short))}");
        }
        else
        {
            Assert.True(kind == listed.Single(), $"{key}: listed as {listed.Single()}, but the port is {kind}");
        }
    }

    // PORT: no Python original. Python has no cancellation token, so this test checks rule R8 by itself. Each async
    // function passes its token on and awaits the call. A fake that is canceled throws OperationCanceledException after
    // a yield, so a dropped token or a missing await does not give the exception.
    [Theory(Timeout = 5000)]
    [InlineData("search")]
    [InlineData("edge_search")]
    [InlineData("node_search")]
    [InlineData("episode_search")]
    [InlineData("community_search")]
    public async Task Each_async_function_passes_its_token_and_awaits_the_call(string fn)
    {
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await canceled.CancelAsync();
        var input = Cases.Select(c => c.GetProperty("input")).First(i =>
            i.GetProperty("fn").GetString() == fn
            && (fn == "search" || (i.GetProperty("config").ValueKind == JsonValueKind.String && !i.TryGetProperty("defaults", out _))));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LoopGuard.RunAsync(() => RunAsync(input, [], new Probe(canceled, null, 0), canceled.Token)));
    }

    // The points of the grid where the port differs from Python by the late cancel of GAPS F33. The grid of
    // search_cancel.json holds 153 points: 114 cancel while a query returns, 24 while the embedder returns, and 15 while
    // the cross-encoder returns. 73 differ, and 80 agree. A point has the kind of its difference (see Compare):
    //   event_order    51   the same exception, and the events differ by more than extra events (extra spans, and an
    //                       end event that comes earlier or later)
    //   full_result    18   the port returns a result, and Python ends with CancelledError
    //   extra_events    2   the same exception, and only extra events
    //   other_error     2   the port ends with another exception (the reranker error, which it reaches before it reads
    //                       the token)
    // Each point that is not in this list must agree with Python. A listed point that agrees fails the test.
    private static readonly (string Recipe, long? MaxCoroutines, string Call, int K, string Kind)[] LateCancel =
    [
        ("COMBINED_HYBRID_SEARCH_RRF", null, "query", 2, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", null, "query", 3, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", null, "query", 4, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", null, "query", 5, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", null, "query", 6, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", null, "query", 7, "full_result"),
        ("COMBINED_HYBRID_SEARCH_RRF", 1L, "query", 2, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", 1L, "query", 4, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", 1L, "query", 5, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", 1L, "query", 7, "full_result"),
        ("COMBINED_HYBRID_SEARCH_RRF", 2L, "query", 2, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", 2L, "query", 3, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", 2L, "query", 4, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", 2L, "query", 5, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", 2L, "query", 6, "event_order"),
        ("COMBINED_HYBRID_SEARCH_RRF", 2L, "query", 7, "full_result"),
        ("COMBINED_HYBRID_SEARCH_MMR", null, "query", 2, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", null, "query", 3, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", null, "query", 4, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", null, "query", 5, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", null, "query", 6, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", null, "query", 7, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", null, "query", 10, "full_result"),
        ("COMBINED_HYBRID_SEARCH_MMR", 1L, "query", 2, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", 1L, "query", 5, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", 1L, "query", 7, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", 1L, "query", 9, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", 1L, "query", 10, "full_result"),
        ("COMBINED_HYBRID_SEARCH_MMR", 2L, "query", 2, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", 2L, "query", 3, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", 2L, "query", 4, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", 2L, "query", 6, "extra_events"),
        ("COMBINED_HYBRID_SEARCH_MMR", 2L, "query", 7, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", 2L, "query", 8, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", 2L, "query", 9, "event_order"),
        ("COMBINED_HYBRID_SEARCH_MMR", 2L, "query", 10, "full_result"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", null, "query", 2, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", null, "query", 3, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", null, "query", 4, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", null, "query", 5, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", null, "query", 6, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", null, "query", 7, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", null, "rank", 4, "full_result"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", 1L, "query", 7, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", 1L, "query", 9, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", 1L, "rank", 4, "full_result"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", 2L, "query", 2, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", 2L, "query", 3, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", 2L, "query", 4, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", 2L, "query", 7, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", 2L, "query", 8, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", 2L, "query", 9, "event_order"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", 2L, "rank", 2, "extra_events"),
        ("COMBINED_HYBRID_SEARCH_CROSS_ENCODER", 2L, "rank", 4, "full_result"),
        ("NODE_HYBRID_SEARCH_CROSS_ENCODER", null, "query", 2, "event_order"),
        ("NODE_HYBRID_SEARCH_CROSS_ENCODER", null, "rank", 1, "full_result"),
        ("NODE_HYBRID_SEARCH_CROSS_ENCODER", 2L, "query", 2, "event_order"),
        ("NODE_HYBRID_SEARCH_CROSS_ENCODER", 2L, "rank", 1, "full_result"),
        ("COMMUNITY_HYBRID_SEARCH_MMR", null, "query", 2, "event_order"),
        ("COMMUNITY_HYBRID_SEARCH_MMR", null, "query", 3, "full_result"),
        ("COMMUNITY_HYBRID_SEARCH_MMR", 1L, "query", 2, "event_order"),
        ("COMMUNITY_HYBRID_SEARCH_MMR", 1L, "query", 3, "full_result"),
        ("COMMUNITY_HYBRID_SEARCH_MMR", 2L, "query", 2, "event_order"),
        ("COMMUNITY_HYBRID_SEARCH_MMR", 2L, "query", 3, "full_result"),
        ("EDGE_HYBRID_SEARCH_RRF", null, "query", 2, "full_result"),
        ("EDGE_HYBRID_SEARCH_RRF", 1L, "query", 2, "event_order"),
        ("EDGE_HYBRID_SEARCH_RRF", 2L, "query", 2, "full_result"),
        ("NODE_HYBRID_SEARCH_RRF", null, "query", 2, "full_result"),
        ("NODE_HYBRID_SEARCH_RRF", 1L, "query", 2, "event_order"),
        ("NODE_HYBRID_SEARCH_RRF", 2L, "query", 2, "full_result"),
        ("EDGE_HYBRID_SEARCH_NODE_DISTANCE", null, "query", 2, "other_error"),
        ("EDGE_HYBRID_SEARCH_NODE_DISTANCE", 1L, "query", 2, "event_order"),
        ("EDGE_HYBRID_SEARCH_NODE_DISTANCE", 2L, "query", 2, "other_error"),
    ];

    private static readonly string[] CallKinds = ["query", "embed", "rank"];

    // What one run of a case gave: the result or the error, the ordered events, and the largest count of calls in flight.
    private sealed record Run(JsonNode? Result, Exception? Error, List<JsonNode> Events, int MaxInFlight);

    private static async Task<Run> ExecuteAsync(JsonElement input, CancellationToken testToken)
    {
        var events = new List<JsonNode>();

        using var factory = new CaptureFactory(events);
        using var scope = PortServices.Use(loggerFactory: factory);

        // PORT: a live token source. Python has no token, so the test checks that each fake got this token (R8, R25).
        using var source = CancellationTokenSource.CreateLinkedTokenSource(testToken);
        string? cancelCall = null;
        var cancelAt = 0;
        foreach (var call in CallKinds)
        {
            if (input.TryGetProperty($"cancel_at_{call}", out var k))
            {
                cancelCall = call;
                cancelAt = k.GetInt32();
            }
        }

        var probe = new Probe(source, cancelCall, cancelAt);

        JsonNode? result = null;
        Exception? error = null;
        try
        {
            result = await LoopGuard.RunAsync(() => RunAsync(input, events, probe, source.Token));
        }
        catch (Exception e) when (e is GraphitiException or OperationCanceledException)
        {
            error = e;
        }

        // asyncio.run ends the tasks that a failed search leaves, and a span of such a task ends in this time. The
        // port runs the same tasks on its scheduler, so the test lets the scheduler run before it reads the events.
        await LoopGuard.RunAsync(async () =>
        {
            for (var i = 0; i < 50; i++)
            {
                await Task.Yield();
            }
        });

        // Each call of a fake got the token of the test. A call that a cancel ended before its record has a token too.
        Assert.All(probe.Tokens, token => Assert.Equal(source.Token, token));
        var recordedCalls = events.Count(e => e["kind"]!.GetValue<string>() is "query" or "embed" or "rank");
        if (error is null)
        {
            Assert.Equal(recordedCalls, probe.Tokens.Count);
        }
        else
        {
            Assert.True(probe.Tokens.Count >= recordedCalls);
        }

        return new Run(result, error, events, probe.MaxInFlight);
    }

    // "same" when the port gave what Python gave. Else the kind of the difference, in this order:
    //   full_result    the port returns a result, and Python ends with an exception.
    //   other_error    the port ends with an exception of another type.
    //   extra_events   the exception is the same, and the events of Python are in the events of the port in order, with
    //                  more events between them.
    //   event_order    the exception is the same, and the events differ in another way.
    //   in_flight      the events are the same, and the largest count of calls in flight is not.
    private static string Compare(JsonElement output, Run run)
    {
        var expectedError = output.GetProperty("error");
        if (expectedError.ValueKind == JsonValueKind.Null || run.Error is null)
        {
            return expectedError.ValueKind == JsonValueKind.Null && run.Error is null ? "same" : run.Error is null ? "full_result" : "other_error";
        }

        if (expectedError.GetProperty("type").GetString() != PythonName(run.Error))
        {
            return "other_error";
        }

        var expected = output.GetProperty("events").EnumerateArray().Select(e => Untimed(JsonNode.Parse(e.GetRawText())!)).ToList();
        var actual = run.Events.Select(Untimed).ToList();
        if (expected.Count == actual.Count && expected.Zip(actual).All(p => Same(p.First, p.Second)))
        {
            return output.GetProperty("max_in_flight").GetInt32() == run.MaxInFlight ? "same" : "in_flight";
        }

        var next = 0;
        foreach (var node in actual)
        {
            if (next < expected.Count && Same(expected[next], node))
            {
                next++;
            }
        }

        return next == expected.Count ? "extra_events" : "event_order";
    }

    private static long? MaxCoroutines(JsonElement input) => input.TryGetProperty("max_coroutines", out var m) ? m.GetInt64() : null;

    private static string CancelKey(JsonElement input)
    {
        var call = CallKinds.Single(c => input.TryGetProperty($"cancel_at_{c}", out _));
        return CancelKey(input.GetProperty("recipe").GetString()!, MaxCoroutines(input), call, input.GetProperty($"cancel_at_{call}").GetInt32());
    }

    private static string CancelKey(string recipe, long? maxCoroutines, string call, int k) =>
        $"{recipe} max_coroutines={(maxCoroutines is { } m ? m.ToString(System.Globalization.CultureInfo.InvariantCulture) : "None")} {call} {k}";

    private static async Task<JsonNode?> RunAsync(JsonElement input, List<JsonNode> events, Probe probe, CancellationToken token)
    {
        var driver = new FakeDriver(events, probe);
        Tracer tracer = input.TryGetProperty("no_tracer", out var nt) && nt.GetBoolean() ? new NoOpTracer() : new RecordingTracer(events);
        var fn = input.GetProperty("fn").GetString()!;
        if (fn == "search")
        {
            var clients = new GraphitiClients
            {
                Driver = driver,
                LlmClient = null!,
                Embedder = new FakeEmbedder(events, probe),
                CrossEncoder = new FakeCrossEncoder(events, probe),
                Tracer = tracer,
                MaxCoroutines = input.TryGetProperty("max_coroutines", out var mc) ? mc.GetInt64() : null,
            };
            var results = await SearchModule.SearchAsync(
                clients,
                input.TryGetProperty("query", out var q) ? q.GetString()! : "who works where",
                input.TryGetProperty("group_ids", out var g) ? NullableStrings(g) : null,
                ConfigFor(input),
                new SearchFilters(),
                centerNodeUuid: input.TryGetProperty("center", out var c) ? c.GetString() : null,
                bfsOriginNodeUuids: input.TryGetProperty("bfs", out var b) ? NullableStrings(b) : null,
                queryVector: input.TryGetProperty("query_vector", out var qv) ? Doubles(qv) : null,
                cancellationToken: token);
            return new JsonObject
            {
                ["edges"] = Uuids(results.Edges.Select(e => e.Uuid)),
                ["edge_reranker_scores"] = Encode(results.EdgeRerankerScores),
                ["nodes"] = Uuids(results.Nodes.Select(n => n.Uuid)),
                ["node_reranker_scores"] = Encode(results.NodeRerankerScores),
                ["episodes"] = Uuids(results.Episodes.Select(e => e.Uuid)),
                ["episode_reranker_scores"] = Encode(results.EpisodeRerankerScores),
                ["communities"] = Uuids(results.Communities.Select(c => c.Uuid)),
                ["community_reranker_scores"] = Encode(results.CommunityRerankerScores),
            };
        }

        var config = input.GetProperty("config").ValueKind == JsonValueKind.Null ? null : ConfigFor(input);
        var crossEncoder = new FakeCrossEncoder(events, probe);
        const string query = "who works where";
        List<double> queryVector = [1.0, 0.0];
        List<string> groups = ["g"];
        var defaults = input.TryGetProperty("defaults", out var d) && d.GetBoolean();
        Tracer? tracerArg = input.TryGetProperty("no_tracer", out _) ? null : tracer;
        (IEnumerable<string> Uuids, List<double> Scores) pair;
        switch (fn)
        {
            case "edge_search":
                var (edges, edgeScores) = defaults
                    ? await SearchModule.EdgeSearchAsync(driver, crossEncoder, query, queryVector, groups, config?.EdgeConfig, new SearchFilters(), cancellationToken: token)
                    : await SearchModule.EdgeSearchAsync(driver, crossEncoder, query, queryVector, groups, config?.EdgeConfig, new SearchFilters(), "n-1", null, 3, 0.5, tracerArg, maxCoroutines: 2, cancellationToken: token);
                pair = (edges.Select(e => e.Uuid), edgeScores);
                break;
            case "node_search":
                var (nodes, nodeScores) = defaults
                    ? await SearchModule.NodeSearchAsync(driver, crossEncoder, query, queryVector, groups, config?.NodeConfig, new SearchFilters(), cancellationToken: token)
                    : await SearchModule.NodeSearchAsync(driver, crossEncoder, query, queryVector, groups, config?.NodeConfig, new SearchFilters(), "n-1", null, 3, 0.5, tracerArg, maxCoroutines: 2, cancellationToken: token);
                pair = (nodes.Select(n => n.Uuid), nodeScores);
                break;
            case "episode_search":
                var (episodes, episodeScores) = defaults
                    ? await SearchModule.EpisodeSearchAsync(driver, crossEncoder, query, queryVector, groups, config?.EpisodeConfig, new SearchFilters(), cancellationToken: token)
                    : await SearchModule.EpisodeSearchAsync(driver, crossEncoder, query, queryVector, groups, config?.EpisodeConfig, new SearchFilters(), 3, 0.5, tracerArg, maxCoroutines: 2, cancellationToken: token);
                pair = (episodes.Select(e => e.Uuid), episodeScores);
                break;
            case "community_search":
                var (communities, communityScores) = defaults
                    ? await SearchModule.CommunitySearchAsync(driver, crossEncoder, query, queryVector, groups, config?.CommunityConfig, cancellationToken: token)
                    : await SearchModule.CommunitySearchAsync(driver, crossEncoder, query, queryVector, groups, config?.CommunityConfig, 3, 0.5, tracerArg, maxCoroutines: 2, cancellationToken: token);
                pair = (communities.Select(c => c.Uuid), communityScores);
                break;
            default:
                throw new InvalidOperationException(fn);
        }

        return new JsonObject { ["items"] = Uuids(pair.Uuids), ["scores"] = Encode(pair.Scores) };
    }

    private static SearchConfig ConfigFor(JsonElement input)
    {
        if (input.TryGetProperty("recipe", out var recipe))
        {
            var config = Recipe(recipe.GetString()!);
            if (input.TryGetProperty("overrides", out var overrides))
            {
                var data = config.ModelDump(json: true);
                foreach (var (key, value) in Py.AsDict(Read(overrides)))
                {
                    data[key] = value;
                }

                return SearchConfig.ModelValidate(data);
            }

            return config;
        }

        return SearchConfig.ModelValidate(Py.AsDict(Read(Document.GetProperty("configs").GetProperty(input.GetProperty("config").GetString()!))));
    }

    private static SearchConfig Recipe(string name) => name switch
    {
        "COMBINED_HYBRID_SEARCH_RRF" => SearchConfigRecipes.CombinedHybridSearchRrf,
        "COMBINED_HYBRID_SEARCH_MMR" => SearchConfigRecipes.CombinedHybridSearchMmr,
        "COMBINED_HYBRID_SEARCH_CROSS_ENCODER" => SearchConfigRecipes.CombinedHybridSearchCrossEncoder,
        "EDGE_HYBRID_SEARCH_RRF" => SearchConfigRecipes.EdgeHybridSearchRrf,
        "EDGE_HYBRID_SEARCH_MMR" => SearchConfigRecipes.EdgeHybridSearchMmr,
        "EDGE_HYBRID_SEARCH_NODE_DISTANCE" => SearchConfigRecipes.EdgeHybridSearchNodeDistance,
        "EDGE_HYBRID_SEARCH_EPISODE_MENTIONS" => SearchConfigRecipes.EdgeHybridSearchEpisodeMentions,
        "EDGE_HYBRID_SEARCH_CROSS_ENCODER" => SearchConfigRecipes.EdgeHybridSearchCrossEncoder,
        "NODE_HYBRID_SEARCH_RRF" => SearchConfigRecipes.NodeHybridSearchRrf,
        "NODE_HYBRID_SEARCH_MMR" => SearchConfigRecipes.NodeHybridSearchMmr,
        "NODE_HYBRID_SEARCH_NODE_DISTANCE" => SearchConfigRecipes.NodeHybridSearchNodeDistance,
        "NODE_HYBRID_SEARCH_EPISODE_MENTIONS" => SearchConfigRecipes.NodeHybridSearchEpisodeMentions,
        "NODE_HYBRID_SEARCH_CROSS_ENCODER" => SearchConfigRecipes.NodeHybridSearchCrossEncoder,
        "COMMUNITY_HYBRID_SEARCH_RRF" => SearchConfigRecipes.CommunityHybridSearchRrf,
        "COMMUNITY_HYBRID_SEARCH_MMR" => SearchConfigRecipes.CommunityHybridSearchMmr,
        "COMMUNITY_HYBRID_SEARCH_CROSS_ENCODER" => SearchConfigRecipes.CommunityHybridSearchCrossEncoder,
        _ => throw new InvalidOperationException(name),
    };

    // The rows for one query, by the rule of `respond` in port/tools/record_search.py.
    private static List<OrderedDictionary<string, object?>> Respond(string query, OrderedDictionary<string, object?> parameters)
    {
        List<OrderedDictionary<string, object?>> Table(string name) =>
            [.. Document.GetProperty("tables").GetProperty(name).EnumerateArray().Select(x => Py.AsDict(Read(x)))];
        List<string> Param(string name) => [.. Py.Iter(parameters[name]).Select(Py.AsString)];
        var embeddings = Py.AsDict(Read(Document.GetProperty("embeddings")));
        var mentions = Py.AsDict(Read(Document.GetProperty("mentions")));
        var connected = Py.AsList(Read(Document.GetProperty("connected")));
        List<OrderedDictionary<string, object?>> Rows(string listName, Func<string, bool> keep, Func<string, OrderedDictionary<string, object?>> row) =>
            [.. Param(listName).Where(keep).Select(row)];

        if (query.Contains("queryRelationships(\"edge_name_and_fact\"", StringComparison.Ordinal))
        {
            return Table("edge_fulltext");
        }

        if (query.Contains("cosine(e.fact_embedding", StringComparison.Ordinal))
        {
            return Table("edge_similarity");
        }

        if (query.Contains("queryNodes(\"node_name_and_summary\"", StringComparison.Ordinal))
        {
            return Table("node_fulltext");
        }

        if (query.Contains("cosine(n.name_embedding", StringComparison.Ordinal))
        {
            return Table("node_similarity");
        }

        if (query.Contains("queryNodes(\"episode_content\"", StringComparison.Ordinal))
        {
            return Table("episode_fulltext");
        }

        if (query.Contains("queryNodes(\"community_name\"", StringComparison.Ordinal))
        {
            return Table("community_fulltext");
        }

        if (query.Contains("cosine(c.name_embedding", StringComparison.Ordinal))
        {
            return Table("community_similarity");
        }

        if (parameters.ContainsKey("bfs_origin_node_uuids"))
        {
            return Table(query.Contains("e.fact AS fact", StringComparison.Ordinal) ? "edge_bfs" : "node_bfs");
        }

        if (parameters.ContainsKey("edge_uuids"))
        {
            return Rows("edge_uuids", embeddings.ContainsKey, u => Row(("uuid", u), ("fact_embedding", embeddings[u])));
        }

        if (parameters.ContainsKey("community_uuids"))
        {
            return Rows("community_uuids", embeddings.ContainsKey, u => Row(("uuid", u), ("name_embedding", embeddings[u])));
        }

        if (parameters.ContainsKey("center_uuid"))
        {
            return Rows("node_uuids", u => connected.Contains(u), u => Row(("score", 1L), ("uuid", u)));
        }

        if (query.Contains("n.name_embedding AS name_embedding", StringComparison.Ordinal))
        {
            return Rows("node_uuids", embeddings.ContainsKey, u => Row(("uuid", u), ("name_embedding", embeddings[u])));
        }

        if (query.Contains("count(*) AS score", StringComparison.Ordinal))
        {
            return Rows("node_uuids", mentions.ContainsKey, u => Row(("score", mentions[u]), ("uuid", u)));
        }

        throw new InvalidOperationException("no rows for the query: " + query);
    }

    private static OrderedDictionary<string, object?> Row(params (string Key, object? Value)[] items)
    {
        var row = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in items)
        {
            row[key] = value;
        }

        return row;
    }

    private static string PythonName(Exception e) => e switch
    {
        SearchRerankerException => "SearchRerankerError",
        OperationCanceledException => "CancelledError",
        GroupIdValidationException => "GroupIdValidationError",
        _ => e.GetType().Name,
    };

    private static string Short(JsonNode e) => e["kind"]!.GetValue<string>() + (e["name"] is { } n ? ":" + n.GetValue<string>() : string.Empty);

    private static JsonArray Uuids(IEnumerable<string> uuids) => new([.. uuids.Select(u => (JsonNode)u)]);

    private static List<double> Doubles(JsonElement e) => [.. e.EnumerateArray().Select(x => x.GetDouble())];

    private static List<string>? NullableStrings(JsonElement e) =>
        e.ValueKind == JsonValueKind.Null ? null : [.. e.EnumerateArray().Select(x => x.GetString()!)];

    // The clock of the recorder is a constant and the clock here is real, so a timed log line loses the time.
    private static JsonNode Untimed(JsonNode node)
    {
        if (node is JsonObject o && o["kind"]?.GetValue<string>() == "log")
        {
            o["message"] = Regex.Replace(o["message"]!.GetValue<string>(), @" in \S+ ms$", " in <t> ms", RegexOptions.CultureInvariant);
        }

        return node;
    }

    private static void AssertSame(JsonNode? expected, JsonNode? actual) =>
        Assert.True(Same(expected, actual), $"expected {expected?.ToJsonString()} but got {actual?.ToJsonString()}");

    // JSON equality, with a tolerance of 1e-9 for a float.
    private static bool Same(JsonNode? expected, JsonNode? actual)
    {
        if (expected is JsonObject eo && actual is JsonObject ao)
        {
            if (eo["type"]?.GetValue<string>() == "float" && ao["type"]?.GetValue<string>() == "float"
                && eo["value"]!.GetValueKind() == JsonValueKind.Number && ao["value"]!.GetValueKind() == JsonValueKind.Number)
            {
                var x = eo["value"]!.GetValue<double>();
                var y = ao["value"]!.GetValue<double>();
                return Math.Abs(x - y) <= 1e-9 * Math.Max(1.0, Math.Abs(x));
            }

            return eo.Count == ao.Count && eo.All(p => ao.TryGetPropertyValue(p.Key, out var other) && Same(p.Value, other));
        }

        if (expected is JsonArray ea && actual is JsonArray aa)
        {
            return ea.Count == aa.Count && ea.Zip(aa).All(p => Same(p.First, p.Second));
        }

        return JsonNode.DeepEquals(expected, actual);
    }

    // A list of more than 16 floats is its length and its sum, as `compact` in the recorder gives it.
    private static JsonNode Encode(object? value) => value switch
    {
        null => new JsonObject { ["type"] = "none" },
        bool b => new JsonObject { ["type"] = "bool", ["value"] = b },
        long l => new JsonObject { ["type"] = "int", ["value"] = l },
        double x when double.IsNaN(x) => new JsonObject { ["type"] = "float", ["value"] = "nan" },
        double x when double.IsInfinity(x) => new JsonObject { ["type"] = "float", ["value"] = x > 0 ? "inf" : "-inf" },
        double x => new JsonObject { ["type"] = "float", ["value"] = x },
        string s => new JsonObject { ["type"] = "str", ["value"] = s },
        List<double> v when v.Count > 16 => new JsonObject { ["type"] = "vector", ["len"] = v.Count, ["sum"] = v.Sum() },
        System.Collections.IDictionary dict => new JsonObject
        {
            ["type"] = "dict",
            ["items"] = new JsonArray([.. dict.Keys.Cast<string>().Select(key => (JsonNode?)new JsonArray(key, Encode(dict[key])))]),
        },
        System.Collections.IEnumerable items => new JsonObject
        {
            ["type"] = "list",
            ["items"] = new JsonArray([.. items.Cast<object?>().Select(Encode)]),
        },
        _ => new JsonObject { ["type"] = value.GetType().Name },
    };

    private static object? Read(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => e.GetString(),
        // A cast to object keeps an int a long: the ternary of a double and a long is a double.
        JsonValueKind.Number => e.GetRawText().Any(c => c is '.' or 'e' or 'E') ? e.GetDouble() : (object)e.GetInt64(),
        JsonValueKind.Array => e.EnumerateArray().Select(Read).ToList(),
        JsonValueKind.Object => ReadObject(e),
        _ => throw new InvalidOperationException(e.ValueKind.ToString()),
    };

    private static OrderedDictionary<string, object?> ReadObject(JsonElement e)
    {
        var dict = new OrderedDictionary<string, object?>();
        foreach (var p in e.EnumerateObject())
        {
            dict[p.Name] = Read(p.Value);
        }

        return dict;
    }

    private static JsonElement Load(string file = "search.json")
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", file);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    // The calls in flight, the token of each call, and the cancel at the k-th call of one kind. A call is in flight from
    // its entry to its return. Python counts the same way in port/tools/record_search.py.
    private sealed class Probe(CancellationTokenSource source, string? cancelCall, int cancelAt)
    {
        internal List<CancellationToken> Tokens { get; } = [];

        internal int MaxInFlight { get; private set; }

        private int inFlight;
        private int queries;
        private int embeds;
        private int ranks;

        internal void Enter(CancellationToken token)
        {
            Tokens.Add(token);
            inFlight++;
            MaxInFlight = Math.Max(MaxInFlight, inFlight);
        }

        internal void Exit() => inFlight--;

        // Python: `main_task.cancel()` after the call is recorded.
        internal void AfterQuery()
        {
            queries++;
            CancelAt("query", queries);
        }

        internal void AfterEmbed()
        {
            embeds++;
            CancelAt("embed", embeds);
        }

        internal void AfterRank()
        {
            ranks++;
            CancelAt("rank", ranks);
        }

        private void CancelAt(string call, int count)
        {
            if (call == cancelCall && count == cancelAt)
            {
                source.Cancel();
            }
        }
    }

    private sealed class FakeDriver(List<JsonNode> events, Probe probe) : GraphDriver
    {
        internal override string PythonName => "FakeDriver";

        internal override async Task<(List<OrderedDictionary<string, object?>> Records, object? Summary, object? Keys)> ExecuteQueryAsync(
            string cypherQuery,
            OrderedDictionary<string, object?> parameters,
            CancellationToken cancellationToken = default)
        {
            probe.Enter(cancellationToken);
            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                events.Add(new JsonObject
                {
                    ["kind"] = "query",
                    ["query"] = cypherQuery,
                    ["keys"] = new JsonArray([.. parameters.Keys.Select(k => (JsonNode)k)]),
                    ["values"] = new JsonArray([.. parameters.Values.Select(Encode)]),
                });
                probe.AfterQuery();
                return (Respond(cypherQuery, parameters), null, null);
            }
            finally
            {
                probe.Exit();
            }
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeEmbedder(List<JsonNode> events, Probe probe) : EmbedderClient
    {
        internal override async Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            probe.Enter(cancellationToken);
            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                events.Add(new JsonObject { ["kind"] = "embed", ["input"] = Encode(inputData) });
                probe.AfterEmbed();
                return [1.0, 0.0];
            }
            finally
            {
                probe.Exit();
            }
        }
    }

    // Returns the passages in reverse order with the scores 1.0, 0.75, 0.5, and so on.
    private sealed class FakeCrossEncoder(List<JsonNode> events, Probe probe) : CrossEncoderClient
    {
        internal override async Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default)
        {
            probe.Enter(cancellationToken);
            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                events.Add(new JsonObject
                {
                    ["kind"] = "rank",
                    ["query"] = query,
                    ["passages"] = new JsonArray([.. passages.Select(p => (JsonNode)p)]),
                });
                probe.AfterRank();
                return [.. Enumerable.Reverse(passages).Select((passage, i) => (passage, 1.0 - (0.25 * i)))];
            }
            finally
            {
                probe.Exit();
            }
        }
    }

    private sealed class RecordingSpan : TracerSpan
    {
        internal OrderedDictionary<string, object?> Attributes { get; } = [];

        internal JsonArray? Status { get; private set; }

        internal string? ExceptionName { get; private set; }

        internal override void AddAttributes(OrderedDictionary<string, object?> attributes)
        {
            foreach (var (key, value) in attributes)
            {
                Attributes[key] = value;
            }
        }

        internal override void SetStatus(string status, string? description = null) => Status = new JsonArray(status, description);

        internal override void RecordException(Exception exception) => ExceptionName = PythonName(exception);
    }

    // The start of each span, and at its end the name, the attributes, the status, and the recorded exception.
    private sealed class RecordingTracer(List<JsonNode> events) : Tracer
    {
        internal override async Task<T> StartSpanAsync<T>(string name, Func<TracerSpan, Task<T>> body, CancellationToken cancellationToken = default)
        {
            var span = new RecordingSpan();
            events.Add(new JsonObject { ["kind"] = "span", ["name"] = name });
            try
            {
                return await body(span);
            }
            finally
            {
                events.Add(new JsonObject
                {
                    ["kind"] = "end",
                    ["name"] = name,
                    ["attributes"] = Encode(span.Attributes),
                    ["status"] = span.Status,
                    ["exception"] = span.ExceptionName,
                });
            }
        }
    }

    private sealed class CaptureFactory(List<JsonNode> events) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider)
        {
        }

        public ILogger CreateLogger(string categoryName) => new CaptureLogger(events);

        public void Dispose()
        {
        }

        private sealed class CaptureLogger(List<JsonNode> events) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                events.Add(new JsonObject
                {
                    ["kind"] = "log",
                    ["level"] = logLevel == LogLevel.Debug ? "DEBUG" : logLevel.ToString(),
                    ["message"] = formatter(state, exception),
                });
        }
    }
}
