// Evaluation test for graphiti_core/search/search_utils.py (port/RULES.md, R18, R23, R25).
// The recording is values/search_utils.json, which port/tools/record_search_utils.py wrote from Python. Fakes replace
// the graph driver, its search interface, and its graph operations interface, and the test asserts on one ordered list
// of their calls and the log lines. A float compares with a tolerance of 1e-6, because np.dot and np.linalg.norm go
// through BLAS (R18, GAPS G17). Each fake yields before it records, counts the calls in flight, and records its token,
// so that the test compares the concurrency of the recording and shows the token of each call (R25).
namespace Graphiti.Port.Evaluation;

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.Driver;
using Graphiti.Core.Search;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

public class SearchUtilsValuesTests
{
    private static readonly JsonElement Document = Load();

    private static readonly List<JsonElement> Cases = Document.GetProperty("cases").EnumerateArray().ToList();

    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Cases.Count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    [Fact]
    public void Each_module_constant_matches_Python()
    {
        var constants = Document.GetProperty("constants");
        Assert.Equal(SearchUtils.RelevantSchemaLimit, constants.GetProperty("RELEVANT_SCHEMA_LIMIT").GetInt64());
        Assert.Equal(SearchUtils.DefaultMinScore, constants.GetProperty("DEFAULT_MIN_SCORE").GetDouble());
        Assert.Equal(SearchUtils.DefaultMmrLambda, constants.GetProperty("DEFAULT_MMR_LAMBDA").GetDouble());
        Assert.Equal(SearchUtils.MaxSearchDepth, constants.GetProperty("MAX_SEARCH_DEPTH").GetInt64());
        Assert.Equal(SearchUtils.MaxQueryLength, constants.GetProperty("MAX_QUERY_LENGTH").GetInt64());
    }

    [Fact]
    public void The_recording_holds_each_function_each_provider_and_each_mode()
    {
        var inputs = Cases.Select(c => c.GetProperty("input")).ToList();
        var functions = inputs.Select(i => i.GetProperty("fn").GetString()).ToHashSet();
        Assert.Equal(25, functions.Count);
        foreach (var fn in new[] { "edge_similarity_search", "edge_bfs_search", "node_similarity_search", "node_bfs_search", "community_similarity_search" })
        {
            Assert.Equal(
                ["falkordb", "neo4j", "neptune", "relate"],
                inputs.Where(i => i.GetProperty("fn").GetString() == fn).Select(i => i.GetProperty("provider").GetString()).Distinct().Order(StringComparer.Ordinal).ToList());
        }

        Assert.Equal(
            ["none", "raise", "return"],
            inputs.Where(i => i.TryGetProperty("search_interface", out _)).Select(i => i.GetProperty("search_interface").GetString()).Append("none").Distinct().Order(StringComparer.Ordinal).ToList());
        Assert.Equal(
            ["none", "raise", "return"],
            inputs.Where(i => i.TryGetProperty("operations", out _)).Select(i => i.GetProperty("operations").GetString()).Distinct().Order(StringComparer.Ordinal).ToList());
        Assert.Contains(inputs, i => i.TryGetProperty("limit", out var limit) && limit.ValueKind == JsonValueKind.Number && limit.GetInt64() == 0);

        // The limit of semaphore_gather shows: a hybrid search of 22 searches ran 20 at one time (SEMAPHORE_LIMIT).
        Assert.Contains(
            Cases,
            c => c.GetProperty("input").GetProperty("fn").GetString() == "hybrid_node_search"
                && c.GetProperty("input").GetProperty("queries").GetArrayLength() + c.GetProperty("input").GetProperty("embeddings").GetArrayLength() > Helpers.SemaphoreLimit
                && c.GetProperty("output").GetProperty("max_in_flight").GetInt64() == Helpers.SemaphoreLimit);
        Assert.Equal(
            ["GroupIdValidationError", "NotImplementedError", "TypeError", "ValueError", "ZeroDivisionError"],
            Cases.Select(c => c.GetProperty("output").GetProperty("error")).Where(e => e.ValueKind == JsonValueKind.Object)
                .Select(e => e.GetProperty("type").GetString()).Distinct().Order(StringComparer.Ordinal).ToList());
    }

    [Theory(Timeout = 5000)]
    [MemberData(nameof(CaseIndexes))]
    public async Task Each_result_call_and_log_line_matches_Python(int index)
    {
        var input = Cases[index].GetProperty("input");
        var output = Cases[index].GetProperty("output");
        var fn = input.GetProperty("fn").GetString()!;
        var events = new List<JsonNode>();
        var probe = new Probe();
        var driver = MakeDriver(input, events, probe);

        // A live token: each fake records the token that it gets, and it is this one for each call. It is linked to the
        // token of the test, so that the time limit of the test reaches the call.
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var token = source.Token;

        using var factory = new CaptureFactory(events);
        using var scope = PortServices.Use(loggerFactory: factory);

        object? value = null;
        Exception? error = null;
        try
        {
            value = await LoopGuard.RunAsync(() => RunAsync(fn, input, driver, token));
        }
        catch (Exception e) when (e is GraphitiException or ArgumentException or InvalidCastException or NotImplementedException
            or DivideByZeroException or KeyNotFoundException)
        {
            error = e;
        }

        // A mention count is an int in Python and a double in the port, a reranker passes an int min_score of the
        // recording on as a double, and calculate_cosine_similarity returns the int 0 for a zero vector (GAPS F18).
        var lenient = fn is "episode_mentions_reranker" or "node_distance_reranker" or "calculate_cosine_similarity";
        var expectedError = output.GetProperty("error");
        if (expectedError.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(error);
            AssertSame(output.GetProperty("result"), Encode(value), lenient);
        }
        else
        {
            Assert.NotNull(error);
            var type = expectedError.GetProperty("type").GetString();
            Assert.IsAssignableFrom(
                type switch
                {
                    "GroupIdValidationError" => typeof(GroupIdValidationException),
                    "NotImplementedError" => typeof(NotImplementedException),
                    "ValueError" => typeof(ArgumentException),
                    "TypeError" => typeof(InvalidCastException),
                    "ZeroDivisionError" => typeof(DivideByZeroException),
                    _ => throw new InvalidOperationException(type),
                },
                error);

            // numpy, zip, and a TypeError of the interpreter have their own words. ZeroDivisionError is Python's own.
            if (type is "GroupIdValidationError" or "NotImplementedError")
            {
                Assert.Equal(expectedError.GetProperty("text").GetString(), error.Message);
            }
        }

        var expectedEvents = output.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(expectedEvents.Count, events.Count);
        for (var i = 0; i < events.Count; i++)
        {
            AssertSame(Untimed(expectedEvents[i]), Untimed(events[i]), lenient);
        }

        // The largest count of calls that ran at the same time: the limit of semaphore_gather shows in it.
        Assert.Equal(output.GetProperty("max_in_flight").GetInt32(), probe.Max);

        // Each call that reached a fake carried the token of the caller.
        Assert.Equal(expectedEvents.Count(IsCall), probe.Tokens.Count);
        Assert.All(probe.Tokens, recorded => Assert.Equal(token, recorded));
    }

    // A call of a fake: a query of the driver, or an operation of an interface. build_fulltext_query is a sync method.
    private static bool IsCall(JsonElement e) =>
        e.GetProperty("kind").GetString() == "query"
        || (e.GetProperty("kind").GetString() == "op" && e.GetProperty("op").GetString() != "build_fulltext_query");

    private static bool IsCall(JsonNode e) => IsCall(JsonSerializer.SerializeToElement(e));

    // PORT: no Python original. Python has no cancellation token, so this test checks rule R8 by itself. Each async
    // function passes its token to the driver and to the interfaces, and awaits the call. A fake that is canceled throws
    // OperationCanceledException after a yield, so a dropped token or a missing await does not give the exception. The
    // fake records no call. The theory runs the first case of each function, provider, and interface mode, and of each
    // shape of a hybrid search (so that the similarity call is the first call in one case). The test of the recording
    // checks the token of each later call.
    [Theory(Timeout = 5000)]
    [MemberData(nameof(CanceledCalls))]
    public async Task Each_async_function_passes_its_token_and_awaits_the_call(int index)
    {
        using var canceled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await canceled.CancelAsync();
        var input = Cases[index].GetProperty("input");
        var events = new List<JsonNode>();
        var driver = MakeDriver(input, events, new Probe());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LoopGuard.RunAsync(() => RunAsync(input.GetProperty("fn").GetString()!, input, driver, canceled.Token)));

        Assert.DoesNotContain(events, IsCall);
    }

    public static TheoryData<int> CanceledCalls()
    {
        // The first case of each async function, provider, and mode that reaches the driver or an interface with no error.
        var data = new TheoryData<int>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Cases.Count; i++)
        {
            var input = Cases[i].GetProperty("input");
            var output = Cases[i].GetProperty("output");
            var fn = input.GetProperty("fn").GetString()!;
            var mode = (input.TryGetProperty("search_interface", out var si) ? si.GetString() : "none")
                + "/" + (input.TryGetProperty("operations", out var op) ? op.GetString() : "none");
            var provider = input.TryGetProperty("provider", out var p) ? p.GetString() : "none";
            var shape = fn == "hybrid_node_search"
                ? (input.GetProperty("queries").GetArrayLength() > 0 ? "q" : "-") + (input.GetProperty("embeddings").GetArrayLength() > 0 ? "e" : "-")
                : string.Empty;
            if (input.TryGetProperty("args", out _) || output.GetProperty("error").ValueKind != JsonValueKind.Null
                || !output.GetProperty("events").EnumerateArray().Any(IsCall) || !seen.Add($"{fn}/{provider}/{mode}/{shape}"))
            {
                continue;
            }

            data.Add(i);
        }

        return data;
    }

    private static FakeDriver MakeDriver(JsonElement input, List<JsonNode> events, Probe probe)
    {
        var provider = input.TryGetProperty("provider", out var p) ? PyEnum.FromValue<GraphProvider>(p.GetString()) : GraphProvider.Neo4j;
        return new FakeDriver(events, probe, RowsFor(input), input.TryGetProperty("header", out var header) ? Read(header) : null)
        {
            Provider = provider,
            FulltextSyntax = input.TryGetProperty("syntax", out var syntax) ? syntax.GetString()! : string.Empty,
            SearchInterface = (input.TryGetProperty("search_interface", out var si) ? si.GetString() : "none") switch
            {
                "raise" => new RaisingSearchInterface(),
                "return" => new ReturningSearchInterface(events, probe),
                _ => null,
            },
            GraphOperationsInterface = (input.TryGetProperty("operations", out var ops) ? ops.GetString() : "none") switch
            {
                "raise" => new RaisingOperations(),
                "return" => new ReturningOperations(events, probe),
                _ => null,
            },
        };
    }

    private static async Task<object?> RunAsync(string fn, JsonElement input, FakeDriver driver, CancellationToken token)
    {
        if (input.TryGetProperty("args", out var args))
        {
            var arguments = args.EnumerateArray().ToList();
            var kwargs = input.TryGetProperty("kwargs", out var k) ? k : default;
            bool Has(string name) => kwargs.ValueKind == JsonValueKind.Object && kwargs.TryGetProperty(name, out _);
            double D(string name) => kwargs.GetProperty(name).GetDouble();
            switch (fn)
            {
                case "calculate_cosine_similarity":
                    return SearchUtils.CalculateCosineSimilarity(Doubles(arguments[0]), Doubles(arguments[1]));
                case "fulltext_query":
                    return SearchUtils.FulltextQuery(arguments[0].GetString()!, NullableStrings(arguments[1]), driver);
                case "rrf":
                    var results = arguments[0].EnumerateArray().Select(Strings).ToList();
                    return Has("rank_const")
                        ? SearchUtils.Rrf(results, kwargs.GetProperty("rank_const").GetInt64(), D("min_score"))
                        : SearchUtils.Rrf(results);
                case "maximal_marginal_relevance":
                    var candidates = new OrderedDictionary<string, List<double>>();
                    foreach (var property in arguments[1].EnumerateObject())
                    {
                        candidates[property.Name] = Doubles(property.Value);
                    }

                    return (Has("mmr_lambda"), Has("min_score")) switch
                    {
                        (true, true) => SearchUtils.MaximalMarginalRelevance(Doubles(arguments[0]), candidates, D("mmr_lambda"), D("min_score")),
                        (true, false) => SearchUtils.MaximalMarginalRelevance(Doubles(arguments[0]), candidates, D("mmr_lambda")),
                        _ => SearchUtils.MaximalMarginalRelevance(Doubles(arguments[0]), candidates),
                    };
                default:
                    throw new InvalidOperationException(fn);
            }
        }

        var filters = SearchFilters.ModelValidate(Py.AsDict(Read(Document.GetProperty("filters").GetProperty(input.TryGetProperty("filters", out var f) ? f.GetString()! : "none"))));
        var groups = input.TryGetProperty("group_ids", out var g) ? NullableStrings(g) : null;
        var defaults = input.TryGetProperty("defaults", out var d) && d.GetBoolean();
        var limit = input.TryGetProperty("limit", out var l) ? l.GetInt64() : 5;
        var minScore = input.TryGetProperty("min_score", out var m) ? m.GetDouble() : 0.5;
        var query = input.TryGetProperty("query", out var q) ? q.GetString()! : "alpha b-c Zoë";
        var origins = input.TryGetProperty("origins", out var o) ? NullableStrings(o) : ["o-1"];
        var depth = input.TryGetProperty("depth", out var dp) ? dp.GetInt64() : 2;
        List<double> vector = [1.0, 0.0];

        switch (fn)
        {
            case "edge_fulltext_search":
                return defaults
                    ? await SearchUtils.EdgeFulltextSearchAsync(driver, query, filters, cancellationToken: token)
                    : await SearchUtils.EdgeFulltextSearchAsync(driver, query, filters, groups, limit, token);
            case "node_fulltext_search":
                return defaults
                    ? await SearchUtils.NodeFulltextSearchAsync(driver, query, filters, cancellationToken: token)
                    : await SearchUtils.NodeFulltextSearchAsync(driver, query, filters, groups, limit, token);
            case "episode_fulltext_search":
                return defaults
                    ? await SearchUtils.EpisodeFulltextSearchAsync(driver, query, filters, cancellationToken: token)
                    : await SearchUtils.EpisodeFulltextSearchAsync(driver, query, filters, groups, limit, token);
            case "community_fulltext_search":
                return defaults
                    ? await SearchUtils.CommunityFulltextSearchAsync(driver, query, cancellationToken: token)
                    : await SearchUtils.CommunityFulltextSearchAsync(driver, query, groups, limit, token);
            case "edge_similarity_search":
                var source = input.TryGetProperty("source", out var s) ? s.GetString() : "s-1";
                var target = input.TryGetProperty("target", out var t) ? t.GetString() : null;
                return defaults
                    ? await SearchUtils.EdgeSimilaritySearchAsync(driver, vector, null, null, filters, cancellationToken: token)
                    : await SearchUtils.EdgeSimilaritySearchAsync(driver, vector, source, target, filters, groups, limit, minScore, token);
            case "node_similarity_search":
                return defaults
                    ? await SearchUtils.NodeSimilaritySearchAsync(driver, vector, filters, cancellationToken: token)
                    : await SearchUtils.NodeSimilaritySearchAsync(driver, vector, filters, groups, limit, minScore, token);
            case "community_similarity_search":
                return defaults
                    ? await SearchUtils.CommunitySimilaritySearchAsync(driver, vector, cancellationToken: token)
                    : await SearchUtils.CommunitySimilaritySearchAsync(driver, vector, groups, limit, minScore, token);
            case "edge_bfs_search":
                return defaults
                    ? await SearchUtils.EdgeBfsSearchAsync(driver, origins, depth, filters, cancellationToken: token)
                    : await SearchUtils.EdgeBfsSearchAsync(driver, origins, depth, filters, groups, limit, token);
            case "node_bfs_search":
                return defaults
                    ? await SearchUtils.NodeBfsSearchAsync(driver, origins, filters, depth, cancellationToken: token)
                    : await SearchUtils.NodeBfsSearchAsync(driver, origins, filters, depth, groups, limit, token);
            case "hybrid_node_search":
                var queries = Strings(input.GetProperty("queries"));
                var embeddings = input.GetProperty("embeddings").EnumerateArray().Select(Doubles).ToList();
                return defaults
                    ? await SearchUtils.HybridNodeSearchAsync(queries, embeddings, driver, filters, cancellationToken: token)
                    : await SearchUtils.HybridNodeSearchAsync(queries, embeddings, driver, filters, groups, limit, token);
            case "get_episodes_by_mentions":
                List<EntityNode> mentionNodes = [EntityNode.ModelValidate(Data("entity_node"))];
                List<EntityEdge> mentionEdges =
                [
                    EntityEdge.ModelValidate(With(Data("entity_edge"), ("episodes", new List<object?> { "ep-1", "ep-2" }))),
                    EntityEdge.ModelValidate(With(Data("entity_edge"), ("uuid", "e-2"), ("episodes", new List<object?> { "ep-3" }))),
                ];
                return defaults
                    ? await SearchUtils.GetEpisodesByMentionsAsync(driver, mentionNodes, mentionEdges, cancellationToken: token)
                    : await SearchUtils.GetEpisodesByMentionsAsync(driver, mentionNodes, mentionEdges, input.TryGetProperty("limit", out var ml) ? ml.GetInt64() : 10, token);
            case "get_mentioned_nodes":
                return await SearchUtils.GetMentionedNodesAsync(
                    driver,
                    [EpisodicNode.ModelValidate(Data("episode")), EpisodicNode.ModelValidate(With(Data("episode"), ("uuid", "ep-2")))],
                    token);
            case "get_communities_by_nodes":
                return await SearchUtils.GetCommunitiesByNodesAsync(driver, TwoNodes(), token);
            case "get_embeddings_for_nodes":
                return await SearchUtils.GetEmbeddingsForNodesAsync(driver, TwoNodes(), token);
            case "get_embeddings_for_communities":
                return await SearchUtils.GetEmbeddingsForCommunitiesAsync(
                    driver,
                    [CommunityNode.ModelValidate(Data("community")), CommunityNode.ModelValidate(With(Data("community"), ("uuid", "c-2")))],
                    token);
            case "get_embeddings_for_edges":
                return await SearchUtils.GetEmbeddingsForEdgesAsync(
                    driver,
                    [EntityEdge.ModelValidate(Data("entity_edge")), EntityEdge.ModelValidate(With(Data("entity_edge"), ("uuid", "e-2")))],
                    token);
            case "get_relevant_nodes":
                var nodes = new List<EntityNode>
                {
                    EntityNode.ModelValidate(Data("entity_node")),
                    EntityNode.ModelValidate(With(Data("entity_node"), ("uuid", "n-2"), ("name", "Zoë 東京"), ("group_id", "grp-2"))),
                }.Take(input.GetProperty("count").GetInt32()).ToList();
                return defaults
                    ? await SearchUtils.GetRelevantNodesAsync(driver, nodes, filters, cancellationToken: token)
                    : await SearchUtils.GetRelevantNodesAsync(driver, nodes, filters, minScore, limit, token);
            case "get_relevant_edges":
            case "get_edge_invalidation_candidates":
                var edges = new List<EntityEdge>
                {
                    EntityEdge.ModelValidate(Data("entity_edge")),
                    EntityEdge.ModelValidate(With(Data("entity_edge"), ("uuid", "e-2"), ("group_id", "grp-2"))),
                }.Take(input.GetProperty("count").GetInt32()).ToList();
                if (fn == "get_relevant_edges")
                {
                    return defaults
                        ? await SearchUtils.GetRelevantEdgesAsync(driver, edges, filters, cancellationToken: token)
                        : await SearchUtils.GetRelevantEdgesAsync(driver, edges, filters, minScore, limit, token);
                }

                return defaults
                    ? await SearchUtils.GetEdgeInvalidationCandidatesAsync(driver, edges, filters, cancellationToken: token)
                    : await SearchUtils.GetEdgeInvalidationCandidatesAsync(driver, edges, filters, minScore, limit, token);
            case "node_distance_reranker":
                return defaults
                    ? await SearchUtils.NodeDistanceRerankerAsync(driver, Strings(input.GetProperty("node_uuids")), input.GetProperty("center").GetString()!, cancellationToken: token)
                    : await SearchUtils.NodeDistanceRerankerAsync(driver, Strings(input.GetProperty("node_uuids")), input.GetProperty("center").GetString()!, minScore, token);
            case "episode_mentions_reranker":
                var lists = input.GetProperty("node_uuids").EnumerateArray().Select(Strings).ToList();
                return defaults
                    ? await SearchUtils.EpisodeMentionsRerankerAsync(driver, lists, cancellationToken: token)
                    : await SearchUtils.EpisodeMentionsRerankerAsync(driver, lists, minScore, token);
            default:
                throw new InvalidOperationException(fn);
        }
    }

    private static List<EntityNode> TwoNodes() =>
        [EntityNode.ModelValidate(Data("entity_node")), EntityNode.ModelValidate(With(Data("entity_node"), ("uuid", "n-2")))];

    // The rows of each execute_query call, built as port/tools/record_search_utils.py builds them.
    private static List<List<OrderedDictionary<string, object?>>> RowsFor(JsonElement input)
    {
        if (input.TryGetProperty("args", out _))
        {
            return [];
        }

        var fn = input.GetProperty("fn").GetString()!;
        var provider = input.GetProperty("provider").GetString();
        var rowsElement = input.TryGetProperty("rows", out var r) ? r : default;
        if (rowsElement.ValueKind == JsonValueKind.Array)
        {
            return [rowsElement.EnumerateArray().Select(x => Py.AsDict(Read(x))).ToList()];
        }

        if (rowsElement.ValueKind == JsonValueKind.String)
        {
            return rowsElement.GetString() == "distance"
                ? [[Row(("uuid", "b"), ("score", 1L)), Row(("uuid", "c"), ("score", 1L))]]
                : [[Row(("uuid", "b"), ("score", 3L)), Row(("uuid", "a"), ("score", 3L)), Row(("uuid", "c"), ("score", 1L)), Row(("uuid", "y"), ("score", 2L))]];
        }

        var rows = rowsElement.ValueKind == JsonValueKind.Number ? rowsElement.GetInt32() : 0;
        switch (fn)
        {
            case "hybrid_node_search":
                return [[NodeRecord("h-1"), NodeRecord("h-2")], [NodeRecord("h-2"), NodeRecord("h-3")], [NodeRecord("h-3")], [NodeRecord("h-1")]];
            case "get_mentioned_nodes":
                return [new List<OrderedDictionary<string, object?>> { NodeRecord("r-1"), NodeRecord("r-2") }.Take(rows).ToList()];
            case "get_communities_by_nodes":
                return [new List<OrderedDictionary<string, object?>> { CommunityRecord("r-1"), CommunityRecord("r-2") }.Take(rows).ToList()];
            case "get_episodes_by_mentions":
                return [new List<OrderedDictionary<string, object?>> { EpisodeRecord("r-1"), EpisodeRecord("r-2") }.Take(rows).ToList()];
            case "get_embeddings_for_nodes":
            case "get_embeddings_for_communities":
            case "get_embeddings_for_edges":
                var key = fn == "get_embeddings_for_edges" ? "fact_embedding" : "name_embedding";
                var records = new List<OrderedDictionary<string, object?>>
                {
                    Row(("uuid", "r-1"), (key, new List<object?> { 0.5, 1.0 })),
                    Row(("uuid", null), (key, new List<object?> { 1.0 })),
                    Row(("uuid", "r-2"), (key, null)),
                    Row((key, new List<object?> { 2.0 })),
                    Row(("uuid", "r-3"), (key, new List<object?>())),
                    Row(("uuid", "r-1"), (key, new List<object?> { 3.0, 4.0 })),
                };
                return [records.Take(rows * 3).ToList()];
            case "get_relevant_nodes":
                return rows == 0
                    ? [[]]
                    : [[
                        Row(("search_node_uuid", "n-1"), ("matches", new List<object?> { NodeRecord("m-1"), NodeRecord("m-2") })),
                        Row(("search_node_uuid", "zz"), ("matches", new List<object?> { NodeRecord("m-3") })),
                    ]];
            case "get_relevant_edges":
            case "get_edge_invalidation_candidates":
                if (rows == 0)
                {
                    return [[], []];
                }

                List<OrderedDictionary<string, object?>> matches =
                [
                    Row(("search_edge_uuid", "e-1"), ("matches", new List<object?> { EdgeRecord("m-1"), EdgeRecord("m-2") })),
                    Row(("search_edge_uuid", "zz"), ("matches", new List<object?>())),
                ];
                if (provider == "neptune")
                {
                    return
                    [
                        [
                            Row(("id", 1L), ("source_embedding", "1,0"), ("target_embedding", new List<object?> { 1.0, 0.0 }), ("search_edge_uuid", "e-1")),
                            Row(("id", 2L), ("source_embedding", "0,1"), ("target_embedding", new List<object?> { 1.0, 0.0 }), ("search_edge_uuid", "e-2")),
                            Row(("id", 3L), ("source_embedding", "0.6,0.8"), ("target_embedding", new List<object?> { 1L, 0L }), ("search_edge_uuid", "e-1")),
                        ],
                        matches,
                    ];
                }

                return [matches];
        }

        if (rows == 0)
        {
            return [[], []];
        }

        List<OrderedDictionary<string, object?>> SimilarityRows() =>
        [
            Row(("id", 1L), ("embedding", "1,0")),
            Row(("id", 2L), ("embedding", "0,1")),
            Row(("id", 3L), ("embedding", string.Empty)),
            Row(("id", 4L), ("embedding", null)),
            Row(("id", 5L), ("embedding", "0.8, 0.6")),
        ];

        return fn switch
        {
            "edge_fulltext_search" or "edge_bfs_search" => [[EdgeRecord("r-1"), EdgeRecord("r-2", "grp-2")]],
            "edge_similarity_search" => provider == "neptune"
                ? [SimilarityRows(), [EdgeRecord("r-1"), EdgeRecord("r-2", "grp-2")]]
                : [[EdgeRecord("r-1"), EdgeRecord("r-2", "grp-2")]],
            "node_fulltext_search" or "node_bfs_search" => [[NodeRecord("r-1"), NodeRecord("r-2", "grp-2")]],
            "node_similarity_search" => provider == "neptune"
                ? [SimilarityRows(), [NodeRecord("r-1"), NodeRecord("r-2", "grp-2")]]
                : [[NodeRecord("r-1"), NodeRecord("r-2", "grp-2")]],
            "episode_fulltext_search" => [[EpisodeRecord("r-1"), EpisodeRecord("r-2", "grp-2")]],
            "community_fulltext_search" => [[CommunityRecord("r-1"), CommunityRecord("r-2", "grp-2")]],
            "community_similarity_search" => provider == "neptune"
                ? [SimilarityRows(), [CommunityRecord("r-1"), CommunityRecord("r-2", "grp-2")]]
                : [[CommunityRecord("r-1"), CommunityRecord("r-2", "grp-2")]],
            _ => throw new InvalidOperationException(fn),
        };
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

    private static OrderedDictionary<string, object?> NodeRecord(string uuid, string groupId = "g") => Row(
        ("uuid", uuid),
        ("name", "Name " + uuid),
        ("group_id", groupId),
        ("labels", new List<object?> { "Entity", "Person" }),
        ("created_at", "2026-01-01T00:00:00+00:00"),
        ("summary", "summary of " + uuid),
        ("name_embedding", new List<object?> { 0.5, 0.25 }),
        ("attributes", Row(("uuid", "x"), ("name", "x"), ("age", 3L))));

    private static OrderedDictionary<string, object?> EdgeRecord(string uuid, string groupId = "g") => Row(
        ("uuid", uuid),
        ("group_id", groupId),
        ("source_node_uuid", "src"),
        ("target_node_uuid", "tgt"),
        ("created_at", "2026-01-01T00:00:00+00:00"),
        ("name", "WORKS_AT"),
        ("fact", "fact of " + uuid),
        ("fact_embedding", new List<object?> { 0.5 }),
        ("episodes", new List<object?> { "ep-1" }),
        ("expired_at", null),
        ("valid_at", "2026-01-02T00:00:00+00:00"),
        ("invalid_at", null),
        ("reference_time", null),
        ("attributes", Row(("uuid", "x"), ("custom", 1L))));

    private static OrderedDictionary<string, object?> EpisodeRecord(string uuid, string groupId = "g") => Row(
        ("content", "content " + uuid),
        ("created_at", "2026-01-01T00:00:00+00:00"),
        ("valid_at", "2026-01-01T00:00:00+00:00"),
        ("uuid", uuid),
        ("name", "episode " + uuid),
        ("group_id", groupId),
        ("source", "message"),
        ("source_description", "chat"),
        ("entity_edges", new List<object?> { "e-1" }));

    private static OrderedDictionary<string, object?> CommunityRecord(string uuid, string groupId = "g") => Row(
        ("uuid", uuid),
        ("name", "community " + uuid),
        ("group_id", groupId),
        ("name_embedding", new List<object?> { 1.0, 0.0 }),
        ("created_at", "2026-01-01T00:00:00+00:00"),
        ("summary", "summary " + uuid));

    private static OrderedDictionary<string, object?> Data(string name) => Py.AsDict(Read(Document.GetProperty(name)));

    private static OrderedDictionary<string, object?> With(OrderedDictionary<string, object?> data, params (string Key, object? Value)[] changes)
    {
        foreach (var (key, value) in changes)
        {
            data[key] = value;
        }

        return data;
    }

    private static List<double> Doubles(JsonElement e) => e.EnumerateArray().Select(x => Py.AsDouble(Read(x))).ToList();

    private static List<string> Strings(JsonElement e) => e.EnumerateArray().Select(x => x.GetString()!).ToList();

    private static List<string>? NullableStrings(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : Strings(e);

    // The clock of search_utils.py is real on both sides, so the two timed log lines lose the time.
    private static JsonNode Untimed(JsonElement e) => Untimed(JsonNode.Parse(e.GetRawText())!);

    private static JsonNode Untimed(JsonNode node)
    {
        if (node is JsonObject o && o["kind"]?.GetValue<string>() == "log")
        {
            o["message"] = Regex.Replace(o["message"]!.GetValue<string>(), @" in \S+ ms$", " in <t> ms", RegexOptions.CultureInvariant);
        }

        return node;
    }

    private static void AssertSame(JsonElement expected, JsonNode? actual, bool lenient) =>
        AssertSame(JsonNode.Parse(expected.GetRawText()), actual, lenient);

    private static void AssertSame(JsonNode? expected, JsonNode? actual, bool lenient)
    {
        Assert.True(Same(expected, actual, lenient), $"expected {expected?.ToJsonString()} but got {actual?.ToJsonString()}");
    }

    // JSON equality, with a tolerance of 1e-6 for a float. With `lenient`, an int and a float of the same value are
    // equal.
    private static bool Same(JsonNode? expected, JsonNode? actual, bool lenient)
    {
        if (expected is JsonObject eo && actual is JsonObject ao)
        {
            var et = eo["type"]?.GetValue<string>();
            var at = ao["type"]?.GetValue<string>();
            if (et is "int" or "float" && at is "int" or "float" && eo.Count == 2 && ao.Count == 2)
            {
                if (et != at && !lenient)
                {
                    return false;
                }

                var ev = eo["value"]!;
                var av = ao["value"]!;
                if (ev.GetValueKind() == JsonValueKind.String || av.GetValueKind() == JsonValueKind.String)
                {
                    return ev.GetValueKind() == av.GetValueKind() && ev.GetValue<string>() == av.GetValue<string>();
                }

                var x = double.Parse(ev.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
                var y = double.Parse(av.ToJsonString(), System.Globalization.CultureInfo.InvariantCulture);
                return Math.Abs(x - y) <= 1e-6 * Math.Max(1.0, Math.Abs(x));
            }

            if (eo.Count != ao.Count)
            {
                return false;
            }

            foreach (var (key, value) in eo)
            {
                if (!ao.TryGetPropertyValue(key, out var other) || !Same(value, other, lenient))
                {
                    return false;
                }
            }

            return true;
        }

        if (expected is JsonArray ea && actual is JsonArray aa)
        {
            return ea.Count == aa.Count && ea.Zip(aa).All(pair => Same(pair.First, pair.Second, lenient));
        }

        return JsonNode.DeepEquals(expected, actual);
    }

    private static JsonNode Encode(object? value) => value switch
    {
        null => new JsonObject { ["type"] = "none" },
        bool b => new JsonObject { ["type"] = "bool", ["value"] = b },
        int or long => new JsonObject { ["type"] = "int", ["value"] = Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) },
        double d when double.IsNaN(d) => new JsonObject { ["type"] = "float", ["value"] = "nan" },
        double d when double.IsInfinity(d) => new JsonObject { ["type"] = "float", ["value"] = d > 0 ? "inf" : "-inf" },
        double d => new JsonObject { ["type"] = "float", ["value"] = d },
        string s => new JsonObject { ["type"] = "str", ["value"] = s },
        PyDateTime dt => new JsonObject { ["type"] = "datetime", ["value"] = dt.IsoFormat() },
        Type t => new JsonObject { ["type"] = "class", ["value"] = t.Name },
        Enum e => new JsonObject { ["type"] = e.GetType().Name },
        IPydanticModel m => new JsonObject { ["type"] = "model", ["class"] = m.GetType().Name, ["dump"] = Encode(m.ModelDump(false)) },
        ITuple tuple => new JsonObject
        {
            ["type"] = "list",
            ["items"] = new JsonArray(Enumerable.Range(0, tuple.Length).Select(i => Encode(tuple[i])).ToArray()),
        },
        System.Collections.IDictionary dict => new JsonObject
        {
            ["type"] = "dict",
            ["items"] = new JsonArray(dict.Keys.Cast<string>().Select(key => (JsonNode?)new JsonArray(key, Encode(dict[key]))).ToArray()),
        },
        System.Collections.IEnumerable items => new JsonObject
        {
            ["type"] = "list",
            ["items"] = new JsonArray(items.Cast<object?>().Select(Encode).ToArray()),
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
        JsonValueKind.Object when e.TryGetProperty("$float", out var f) && e.EnumerateObject().Count() == 1 => Py.Float(f.GetString()!),
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

    private static JsonElement Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "search_utils.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.Clone();
    }

    private static JsonObject Op(string name, params object?[] args) =>
        new() { ["kind"] = "op", ["op"] = name, ["args"] = new JsonArray([.. args.Select(Encode)]) };

    // The calls in flight of one case, and the token of each call. A call is in flight from the entry of a fake method to its
    // return. The scheduler runs one thread, so the counters need no lock.
    private sealed class Probe
    {
        private int now;

        internal int Max { get; private set; }

        internal List<CancellationToken> Tokens { get; } = [];

        internal void Enter(CancellationToken token)
        {
            Tokens.Add(token);
            Max = Math.Max(Max, ++now);
        }

        internal void Leave() => now--;
    }

    private sealed class FakeDriver(List<JsonNode> events, Probe probe, List<List<OrderedDictionary<string, object?>>> responses, object? header) : GraphDriver
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
                List<OrderedDictionary<string, object?>> records = [];
                if (responses.Count > 0)
                {
                    records = responses[0];
                    responses.RemoveAt(0);
                }

                return (records, header, null);
            }
            finally
            {
                probe.Leave();
            }
        }

        internal override string BuildFulltextQuery(string query, List<string>? groupIds = null, long maxQueryLength = 128)
        {
            events.Add(Op("build_fulltext_query", query, groupIds, maxQueryLength));
            return "falkor(" + query + ")";
        }

        internal override GraphDriverSession Session(string? database = null) => throw new InvalidOperationException("not used");

        internal override Task CloseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task DeleteAllIndexesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        internal override Task BuildIndicesAndConstraintsAsync(bool deleteExisting = false, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    // The base class: each method throws NotImplementedException, as each Python method raises NotImplementedError.
    private sealed record RaisingSearchInterface : SearchInterface;

    private sealed record RaisingOperations : GraphOperationsInterface;

    // Records each call with the arguments after the driver, as the Python recorder does, and returns fixed values.
    private sealed record ReturningSearchInterface(List<JsonNode> Events, Probe Probe) : SearchInterface
    {
        private static EntityEdge Edge() => EntityEdge.ModelValidate(With(Data("entity_edge"), ("uuid", "si-1")));

        private static EntityNode Node() => EntityNode.ModelValidate(With(Data("entity_node"), ("uuid", "si-1")));

        internal override async Task<List<EntityEdge>> EdgeFulltextSearchAsync(GraphDriver driver, string query, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, CancellationToken cancellationToken = default)
        {
            await Note("edge_fulltext_search", cancellationToken, query, searchFilter, groupIds, limit);
            return [Edge()];
        }

        internal override async Task<List<EntityEdge>> EdgeSimilaritySearchAsync(GraphDriver driver, List<double> searchVector, string? sourceNodeUuid, string? targetNodeUuid, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, double minScore = 0.7, CancellationToken cancellationToken = default)
        {
            await Note("edge_similarity_search", cancellationToken, searchVector, sourceNodeUuid, targetNodeUuid, searchFilter, groupIds, limit, minScore);
            return [Edge()];
        }

        internal override async Task<List<EntityNode>> NodeFulltextSearchAsync(GraphDriver driver, string query, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, CancellationToken cancellationToken = default)
        {
            await Note("node_fulltext_search", cancellationToken, query, searchFilter, groupIds, limit);
            return [Node()];
        }

        internal override async Task<List<EntityNode>> NodeSimilaritySearchAsync(GraphDriver driver, List<double> searchVector, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, double minScore = 0.7, CancellationToken cancellationToken = default)
        {
            await Note("node_similarity_search", cancellationToken, searchVector, searchFilter, groupIds, limit, minScore);
            return [Node()];
        }

        internal override async Task<List<EpisodicNode>> EpisodeFulltextSearchAsync(GraphDriver driver, string query, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, CancellationToken cancellationToken = default)
        {
            await Note("episode_fulltext_search", cancellationToken, query, searchFilter, groupIds, limit);
            return [EpisodicNode.ModelValidate(With(Data("episode"), ("uuid", "si-1")))];
        }

        internal override async Task<List<EntityEdge>> EdgeBfsSearchAsync(GraphDriver driver, List<string>? bfsOriginNodeUuids, long bfsMaxDepth, SearchFilters searchFilter, List<string>? groupIds = null, long limit = 100, CancellationToken cancellationToken = default)
        {
            await Note("edge_bfs_search", cancellationToken, bfsOriginNodeUuids, bfsMaxDepth, searchFilter, groupIds, limit);
            return [Edge()];
        }

        internal override async Task<List<EntityNode>> NodeBfsSearchAsync(GraphDriver driver, List<string>? bfsOriginNodeUuids, SearchFilters searchFilter, long bfsMaxDepth, List<string>? groupIds = null, long limit = 100, CancellationToken cancellationToken = default)
        {
            await Note("node_bfs_search", cancellationToken, bfsOriginNodeUuids, searchFilter, bfsMaxDepth, groupIds, limit);
            return [Node()];
        }

        internal override async Task<List<CommunityNode>> CommunityFulltextSearchAsync(GraphDriver driver, string query, List<string>? groupIds = null, long limit = 100, CancellationToken cancellationToken = default)
        {
            await Note("community_fulltext_search", cancellationToken, query, groupIds, limit);
            return [CommunityNode.ModelValidate(With(Data("community"), ("uuid", "si-1")))];
        }

        internal override async Task<List<CommunityNode>> CommunitySimilaritySearchAsync(GraphDriver driver, List<double> searchVector, List<string>? groupIds = null, long limit = 100, double minScore = 0.6, CancellationToken cancellationToken = default)
        {
            await Note("community_similarity_search", cancellationToken, searchVector, groupIds, limit, minScore);
            return [CommunityNode.ModelValidate(With(Data("community"), ("uuid", "si-1")))];
        }

        internal override async Task<OrderedDictionary<string, List<double>>> GetEmbeddingsForCommunitiesAsync(GraphDriver driver, List<CommunityNode> communities, CancellationToken cancellationToken = default)
        {
            await Note("get_embeddings_for_communities", cancellationToken, communities);
            return new() { ["si-1"] = [1.0, 0.5] };
        }

        internal override async Task<(List<string>, List<double>)> NodeDistanceRerankerAsync(GraphDriver driver, List<string> nodeUuids, string centerNodeUuid, double minScore = 0, CancellationToken cancellationToken = default)
        {
            await Note("node_distance_reranker", cancellationToken, nodeUuids, centerNodeUuid, minScore);
            return (["si-1"], [1.0]);
        }

        internal override async Task<(List<string>, List<double>)> EpisodeMentionsRerankerAsync(GraphDriver driver, List<List<string>> nodeUuids, double minScore = 0, CancellationToken cancellationToken = default)
        {
            await Note("episode_mentions_reranker", cancellationToken, nodeUuids, minScore);
            return (["si-1"], [2.0]);
        }

        private async Task Note(string name, CancellationToken cancellationToken, params object?[] args)
        {
            Probe.Enter(cancellationToken);
            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                Events.Add(Op(name, args));
            }
            finally
            {
                Probe.Leave();
            }
        }
    }

    private sealed record ReturningOperations(List<JsonNode> Events, Probe Probe) : GraphOperationsInterface
    {
        internal override async Task<List<EntityNode>> GetMentionedNodesAsync(GraphDriver driver, List<EpisodicNode> episodes, CancellationToken cancellationToken = default)
        {
            await Note("get_mentioned_nodes", cancellationToken, episodes);
            return [EntityNode.ModelValidate(With(Data("entity_node"), ("uuid", "ops-1")))];
        }

        internal override async Task<List<CommunityNode>> GetCommunitiesByNodesAsync(GraphDriver driver, List<EntityNode> nodes, CancellationToken cancellationToken = default)
        {
            await Note("get_communities_by_nodes", cancellationToken, nodes);
            return [CommunityNode.ModelValidate(With(Data("community"), ("uuid", "ops-1")))];
        }

        // Python passes no batch size, so the recording holds the nodes only.
        internal override async Task<OrderedDictionary<string, List<double>>> NodeLoadEmbeddingsBulkAsync(GraphDriver driver, List<EntityNode> nodes, long batchSize = 100, CancellationToken cancellationToken = default)
        {
            await Note("node_load_embeddings_bulk", cancellationToken, nodes);
            return new() { ["ops-1"] = [0.5, 0.5] };
        }

        internal override async Task<OrderedDictionary<string, List<double>>> EdgeLoadEmbeddingsBulkAsync(GraphDriver driver, List<EntityEdge> edges, long batchSize = 100, CancellationToken cancellationToken = default)
        {
            await Note("edge_load_embeddings_bulk", cancellationToken, edges);
            return new() { ["ops-1"] = [0.25] };
        }

        internal override async Task<List<EpisodicNode>> EpisodicNodeGetByUuidsAsync(Type? cls, GraphDriver driver, List<string> uuids, CancellationToken cancellationToken = default)
        {
            await Note("episodic_node_get_by_uuids", cancellationToken, cls, uuids);
            return [EpisodicNode.ModelValidate(With(Data("episode"), ("uuid", "ops-1")))];
        }

        private async Task Note(string name, CancellationToken cancellationToken, params object?[] args)
        {
            Probe.Enter(cancellationToken);
            try
            {
                await Task.Yield();
                cancellationToken.ThrowIfCancellationRequested();
                Events.Add(Op(name, args));
            }
            finally
            {
                Probe.Leave();
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
