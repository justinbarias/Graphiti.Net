// Plan 0004, test E6: seed a graph, then run each search recipe on a real Neo4j and compare the ranked results with
// Python.
//
// The expected values come from Python. port/tools/record_search_e2e.py seeded the same graph through the Python
// models, ran graphiti_core.search.search.search with each of the 16 recipes of search_config_recipes.py and 3
// queries, and wrote values/search_e2e.json. The recording holds the ranked results of each kind as logical keys (the
// name of a node, the fact of an edge, the name of an episode, the name of a community) and the reranker scores.
//
// These tests seed the graph through the ported models and the ported Neo4jDriver in a new group, with a noise group
// that holds the same invented query words, and run the ported search. The fakes are at the network boundary only:
// the embedder and the cross-encoder, each a pure function of its text (SHA-256). The fixture is built here from its
// own code, and one test asserts that it equals the fixture of the recording, so the two cannot drift.
//
// The recorder designed the fixture so that the result does not depend on other data of the database (BM25 reads the
// whole index): see the docstring of the recorder. Each uuid is the first 8 hex characters of SHA-256 of the group id,
// then -0000-4000-8000- and an index, so the uuid order is the index order on both sides.
namespace Graphiti.E2E.Tests;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Graphiti.Core;
using Graphiti.Core.CrossEncoder;
using Graphiti.Core.Embedder;
using Graphiti.Core.Search;
using Graphiti.Neo4j;
using Graphiti.Port.Evaluation;
using Graphiti.PyCompat;

public sealed class SearchRecipesOnNeo4jTests(SearchRecipesOnNeo4jTests.Graph graph) : IClassFixture<SearchRecipesOnNeo4jTests.Graph>
{
    private const int TimeLimit = 180_000;

    // The limit of the loop guard for a body that calls Neo4j. It is under the time limit of the test.
    private const double GuardSeconds = 150;
    private const double Tolerance = 1e-6;

    private static readonly string[] Words = ["quorbix7k", "zandeltu9q", "vexmorin4w"];
    private const string Filler = "lorem";

    private static readonly string[] EntityNames =
    [
        "Arden Vale", "Brisk Harbor", "Cobalt Mesa", "Dunmore Reach", "Ember Quay", "Fallow Crest", "Gilded Ford",
        "Hollow Pine", "Ivory Sound", "Juniper Gate", "Kestrel Moor", "Lantern Rise", "Marrow Field", "Nettle Bay",
        "Oaken Spur", "Pallid Cove", "Quarry Dell", "Russet Knoll", "Sable Wharf", "Tamarind Hill", "Umber Glen",
        "Verdant Shoal", "Willow Bend",
    ];

    private const int Hubs = 3;
    private static readonly string[] Labels = ["Person", "Place", "Thing"];
    private static readonly string[] EdgeTypes = ["KNOWS", "LIKES", "OWNS"];
    private const int EdgeCount = 18;
    private const int EpisodeCount = 8;
    private const int CommunityCount = 5;

    private const int EntityBase = 0x100;
    private const int EdgeBase = 0x200;
    private const int EpisodeBase = 0x300;
    private const int CommunityBase = 0x400;
    private const int MentionBase = 0x500;
    private const int MemberBase = 0x600;

    private static readonly DateTime BaseTime = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly Dictionary<string, SearchConfig> Recipes = new()
    {
        ["COMBINED_HYBRID_SEARCH_RRF"] = SearchConfigRecipes.CombinedHybridSearchRrf,
        ["COMBINED_HYBRID_SEARCH_MMR"] = SearchConfigRecipes.CombinedHybridSearchMmr,
        ["COMBINED_HYBRID_SEARCH_CROSS_ENCODER"] = SearchConfigRecipes.CombinedHybridSearchCrossEncoder,
        ["EDGE_HYBRID_SEARCH_RRF"] = SearchConfigRecipes.EdgeHybridSearchRrf,
        ["EDGE_HYBRID_SEARCH_MMR"] = SearchConfigRecipes.EdgeHybridSearchMmr,
        ["EDGE_HYBRID_SEARCH_NODE_DISTANCE"] = SearchConfigRecipes.EdgeHybridSearchNodeDistance,
        ["EDGE_HYBRID_SEARCH_EPISODE_MENTIONS"] = SearchConfigRecipes.EdgeHybridSearchEpisodeMentions,
        ["EDGE_HYBRID_SEARCH_CROSS_ENCODER"] = SearchConfigRecipes.EdgeHybridSearchCrossEncoder,
        ["NODE_HYBRID_SEARCH_RRF"] = SearchConfigRecipes.NodeHybridSearchRrf,
        ["NODE_HYBRID_SEARCH_MMR"] = SearchConfigRecipes.NodeHybridSearchMmr,
        ["NODE_HYBRID_SEARCH_NODE_DISTANCE"] = SearchConfigRecipes.NodeHybridSearchNodeDistance,
        ["NODE_HYBRID_SEARCH_EPISODE_MENTIONS"] = SearchConfigRecipes.NodeHybridSearchEpisodeMentions,
        ["NODE_HYBRID_SEARCH_CROSS_ENCODER"] = SearchConfigRecipes.NodeHybridSearchCrossEncoder,
        ["COMMUNITY_HYBRID_SEARCH_RRF"] = SearchConfigRecipes.CommunityHybridSearchRrf,
        ["COMMUNITY_HYBRID_SEARCH_MMR"] = SearchConfigRecipes.CommunityHybridSearchMmr,
        ["COMMUNITY_HYBRID_SEARCH_CROSS_ENCODER"] = SearchConfigRecipes.CommunityHybridSearchCrossEncoder,
    };

    private static readonly string[] GroupLabels = ["Entity", "Episodic", "Community"];

    private static readonly HashSet<string> CenterRecipes = ["EDGE_HYBRID_SEARCH_NODE_DISTANCE", "NODE_HYBRID_SEARCH_NODE_DISTANCE"];

    // The center node of the node distance recipes: entity 0, a hub.
    private const int Center = 0;

    private static readonly (string Kind, string Scores)[] ResultKinds =
    [
        ("edges", "edge_scores"), ("nodes", "node_scores"), ("episodes", "episode_scores"), ("communities", "community_scores"),
    ];

    private static readonly Lazy<JsonNode> Recording = new(() => JsonNode.Parse(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "values", "search_e2e.json"), Encoding.UTF8))!);

    public static TheoryData<string> RecipeNames => [.. Recipes.Keys];

    // ---------------------------------------------------------------------------------------------------------
    // The tests
    // ---------------------------------------------------------------------------------------------------------

    [Fact(Timeout = TimeLimit)]
    public async Task The_fixture_and_the_queries_equal_the_recording()
    {
        await Task.Yield();
        TestContext.Current.CancellationToken.ThrowIfCancellationRequested();
        var meta = Recording.Value["meta"]!;
        AssertSame(meta["words"]!, Json(Words), "meta.words");
        AssertSame(meta["queries"]!, QueriesJson(), "meta.queries");
        AssertSame(meta["recipes"]!, Json(Recipes.Keys), "meta.recipes");
        AssertSame(meta["center_recipes"]!, Json(CenterRecipes.Order(StringComparer.Ordinal)), "meta.center_recipes");
        AssertSame(meta["center"]!, new JsonArray("entity", Center), "meta.center");
        AssertSame(Recording.Value["inputs"]!["fixture"]!, FixtureSpec(), "inputs.fixture");
        AssertSame(Recording.Value["inputs"]!["noise"]!, NoiseSpec(), "inputs.noise");
    }

    [Theory(Timeout = TimeLimit)]
    [MemberData(nameof(RecipeNames))]
    public async Task Each_recipe_ranks_the_results_as_Python_does(string recipe)
    {
        var ct = TestContext.Current.CancellationToken;
        await graph.Ready(ct);
        var expected = Recording.Value["runs"]!.AsArray().Where(r => Str(r!["recipe"]) == recipe).ToList();
        Assert.Equal(Queries.Length, expected.Count);

        var failures = new List<string>();
        await LoopGuard.RunAsync(async () =>
        {
            for (var index = 0; index < Queries.Length; index++)
            {
                var actual = await graph.RunAsync(recipe, index, ct);
                var recorded = expected.Single(r => (int)r!["query"]! == index)!;
                Compare(recorded, actual, $"{recipe} query {index} ({Queries[index].Text})", failures);
            }
        }, GuardSeconds);

        Assert.True(failures.Count == 0, string.Join('\n', failures));
    }

    // ---------------------------------------------------------------------------------------------------------
    // The comparison
    // ---------------------------------------------------------------------------------------------------------

    private static void Compare(JsonNode recorded, JsonObject actual, string where, List<string> failures)
    {
        foreach (var (kind, scores) in ResultKinds)
        {
            var expectedKeys = recorded[kind]!.AsArray().Select(Str).ToList();
            var actualKeys = actual[kind]!.AsArray().Select(Str).ToList();
            foreach (var key in actualKeys.Where(k => k.StartsWith("noise", StringComparison.Ordinal)))
            {
                failures.Add($"{where}: a key of the noise group is in the {kind}: {key}");
            }

            if (!expectedKeys.SequenceEqual(actualKeys, StringComparer.Ordinal))
            {
                failures.Add($"{where}: the {kind} differ.\n  python: [{string.Join(" | ", expectedKeys.Select(Short))}]\n  port:   [{string.Join(" | ", actualKeys.Select(Short))}]");
            }

            var expectedScores = recorded[scores]!.AsArray().Select(n => (double)n!).ToList();
            var actualScores = actual[scores]!.AsArray().Select(n => (double)n!).ToList();
            if (expectedScores.Count != actualScores.Count
                || expectedScores.Zip(actualScores).Any(p => Math.Abs(p.First - p.Second) > Tolerance))
            {
                failures.Add($"{where}: the {scores} differ.\n  python: [{string.Join(", ", expectedScores.Select(s => s.ToString("R")))}]\n  port:   [{string.Join(", ", actualScores.Select(s => s.ToString("R")))}]");
            }
        }
    }

    // A fact or a name is up to 60 words. The first word and the counts of the query words name it.
    private static string Short(string key)
    {
        var words = key.Split(' ');
        var counts = Words.Select(w => words.Count(x => x == w)).ToList();
        return words.Length > 4 ? $"{words[0]}({string.Join(',', counts)})" : key;
    }

    private static void AssertSame(JsonNode expected, JsonNode actual, string path)
    {
        var sink = new List<string>();
        Diff(expected, actual, path, sink);
        Assert.True(sink.Count == 0, string.Join('\n', sink.Take(20)));
    }

    // Numbers compare by value, so 1 and 1.0 are equal. The embeddings are k / 32768, so they compare exactly.
    private static void Diff(JsonNode? expected, JsonNode? actual, string path, List<string> sink)
    {
        switch (expected)
        {
            case null:
                if (actual is not null)
                {
                    sink.Add($"{path}: expected null, got {actual.ToJsonString()}");
                }

                return;
            case JsonObject e:
                if (actual is not JsonObject ao)
                {
                    sink.Add($"{path}: expected an object, got {actual?.ToJsonString() ?? "null"}");
                    return;
                }

                if (!e.Select(p => p.Key).SequenceEqual(ao.Select(p => p.Key)))
                {
                    sink.Add($"{path}: keys [{string.Join(", ", e.Select(p => p.Key))}] != [{string.Join(", ", ao.Select(p => p.Key))}]");
                    return;
                }

                foreach (var (key, value) in e)
                {
                    Diff(value, ao[key], $"{path}.{key}", sink);
                }

                return;
            case JsonArray e:
                if (actual is not JsonArray aa || aa.Count != e.Count)
                {
                    sink.Add($"{path}: expected {e.ToJsonString()}, got {actual?.ToJsonString() ?? "null"}");
                    return;
                }

                for (var i = 0; i < e.Count; i++)
                {
                    Diff(e[i], aa[i], $"{path}[{i}]", sink);
                }

                return;
            default:
                var ev = expected.AsValue();
                if (actual is not JsonValue av)
                {
                    sink.Add($"{path}: expected {ev.ToJsonString()}, got {actual?.ToJsonString() ?? "null"}");
                }
                else if (ev.TryGetValue<double>(out var ed) && av.TryGetValue<double>(out var ad))
                {
                    if (ed != ad)
                    {
                        sink.Add($"{path}: expected {ed:R}, got {ad:R}");
                    }
                }
                else if (ev.ToJsonString() != av.ToJsonString())
                {
                    sink.Add($"{path}: expected {ev.ToJsonString()}, got {av.ToJsonString()}");
                }

                return;
        }
    }

    private static string Str(JsonNode? node) => node!.GetValue<string>();

    private static JsonArray Json(IEnumerable<string> items) => new([.. items.Select(i => (JsonNode?)i)]);

    // ---------------------------------------------------------------------------------------------------------
    // The fakes at the network boundary
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>Number k is the big-endian 16-bit integer at bytes 2k and 2k+1 of SHA-256 of the text, minus 32768, over 32768.</summary>
    private static List<double> Embed(string text)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return [.. Enumerable.Range(0, 8).Select(k => (((digest[2 * k] << 8) | digest[(2 * k) + 1]) - 32768) / 32768.0)];
    }

    /// <summary>The big-endian 32-bit integer at bytes 0 to 3 of SHA-256 of the query, a zero byte, and the passage, over 2**32.</summary>
    private static double CrossScore(string query, string passage)
    {
        var bytes = Encoding.UTF8.GetBytes(query).Concat(new byte[] { 0 }).Concat(Encoding.UTF8.GetBytes(passage)).ToArray();
        var digest = SHA256.HashData(bytes);
        var value = ((uint)digest[0] << 24) | ((uint)digest[1] << 16) | ((uint)digest[2] << 8) | digest[3];
        return value / 4294967296.0;
    }

    private sealed class HashEmbedder : EmbedderClient
    {
        internal override async Task<List<double>> CreateAsync(List<string> inputData, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            return Embed(inputData[0]);
        }
    }

    private sealed class HashCrossEncoder : CrossEncoderClient
    {
        internal override async Task<List<(string, double)>> RankAsync(string query, List<string> passages, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();

            // A stable sort, highest score first, as sorted(..., reverse=True) in Python.
            return [.. passages.Select(p => (p, CrossScore(query, p))).OrderByDescending(pair => pair.Item2)];
        }
    }

    // ---------------------------------------------------------------------------------------------------------
    // The fixture, built here as the recorder builds it
    // ---------------------------------------------------------------------------------------------------------

    private static string Text(IEnumerable<string> prefix, int[] counts, int length)
    {
        var words = new List<string>(prefix);
        for (var q = 0; q < Words.Length; q++)
        {
            words.AddRange(Enumerable.Repeat(Words[q], counts[q]));
        }

        Assert.True(words.Count <= length, $"a text has {words.Count} words, more than {length}");
        words.AddRange(Enumerable.Repeat(Filler, length - words.Count));
        return string.Join(' ', words);
    }

    private static int[] Counts(int i, int modulus) => [.. Enumerable.Range(0, 3).Select(q => (((i + 1) * (q + 2)) + q) % modulus)];

    private static string Iso(DateTime utc) => Utc(utc).IsoFormat();

    private static PyDateTime Utc(DateTime t) =>
        new(t.Year, t.Month, t.Day, t.Hour, t.Minute, t.Second, (int)(t.Ticks % TimeSpan.TicksPerSecond / 10), offsetMicroseconds: 0);

    private static PyDateTime? When(JsonNode? iso) => iso is null ? null : PyDateTime.FromIsoFormat(Str(iso));

    private static PyDateTime At(JsonNode? iso) => When(iso) ?? throw new InvalidOperationException("a date is missing");

    private static JsonArray Numbers(List<double> values) => new([.. values.Select(v => (JsonNode?)v)]);

    private static JsonArray Ints(IEnumerable<int> values) => new([.. values.Select(v => (JsonNode?)v)]);

    private static JsonObject FixtureSpec()
    {
        var leaves = EntityNames.Length - Hubs;
        var entities = new JsonArray();
        for (var i = 0; i < EntityNames.Length; i++)
        {
            var counts = i < Hubs ? [0, 0, 0] : Counts(i - Hubs, 23);
            var label = i < Hubs ? (i == 2 ? "Place" : "Person") : Labels[i % 3];
            entities.Add(new JsonObject
            {
                ["index"] = i,
                ["name"] = EntityNames[i],
                ["labels"] = Json(["Entity", label]),
                ["summary"] = Text([], counts, 70),
                ["created_at"] = Iso(BaseTime.AddMinutes(i)),
                ["name_embedding"] = Numbers(Embed(EntityNames[i])),
            });
        }

        var edges = new JsonArray();
        for (var k = 0; k < EdgeCount; k++)
        {
            var fact = Text([$"fact{k:00}"], Counts(k, 19), 60);
            edges.Add(new JsonObject
            {
                ["index"] = k,
                ["source"] = k % Hubs,
                ["target"] = Hubs + (k * 7 % leaves),
                ["name"] = EdgeTypes[k / Hubs % 3],
                ["fact"] = fact,
                ["created_at"] = Iso(BaseTime.AddHours(k)),
                ["valid_at"] = k % 4 == 0 ? null : Iso(new DateTime(2025, 1 + (k % 12), 1, 0, 0, 0, DateTimeKind.Utc)),
                ["invalid_at"] = k % 5 == 1 ? Iso(new DateTime(2026, 1 + (k % 12), 2, 0, 0, 0, DateTimeKind.Utc)) : null,
                ["expired_at"] = k % 7 == 3 ? Iso(new DateTime(2026, 6, 1 + k, 0, 0, 0, DateTimeKind.Utc)) : null,
                ["episodes"] = Ints(Enumerable.Range(0, k % 4)),
                ["fact_embedding"] = Numbers(Embed(fact)),
            });
        }

        var episodes = new JsonArray();
        for (var e = 0; e < EpisodeCount; e++)
        {
            episodes.Add(new JsonObject
            {
                ["index"] = e,
                ["name"] = $"episode {e:00}",
                ["content"] = Text([$"ep{e:00}"], Counts(e, 11), 40),
                ["source"] = "message",
                ["source_description"] = "fixture",
                ["created_at"] = Iso(BaseTime.AddDays(e)),
                ["valid_at"] = Iso(BaseTime.AddDays(e).AddHours(1)),
            });
        }

        var mentions = new JsonArray();
        for (var e = 0; e < EpisodeCount; e++)
        {
            var targets = Enumerable.Range(0, 1 + (e % 4)).Select(t => Hubs + (((e * 3) + t) % leaves)).ToList();
            if (e % 3 == 2)
            {
                targets.Add(0);
            }

            foreach (var entity in targets)
            {
                mentions.Add(new JsonObject
                {
                    ["index"] = mentions.Count,
                    ["episode"] = e,
                    ["entity"] = entity,
                    ["created_at"] = Iso(BaseTime.AddDays(e).AddMinutes(mentions.Count)),
                });
            }
        }

        var communities = new JsonArray();
        for (var c = 0; c < CommunityCount; c++)
        {
            var name = Text([$"community{c}"], Counts(c, 7), 20);
            communities.Add(new JsonObject
            {
                ["index"] = c,
                ["name"] = name,
                ["summary"] = "fixture community",
                ["created_at"] = Iso(BaseTime.AddDays(30 + c)),
                ["name_embedding"] = Numbers(Embed(name)),
            });
        }

        var members = new JsonArray();
        for (var c = 0; c < CommunityCount; c++)
        {
            for (var t = 0; t < 2 + (c % 3); t++)
            {
                members.Add(new JsonObject
                {
                    ["index"] = members.Count,
                    ["community"] = c,
                    ["entity"] = Hubs + (((c * 2) + t) % leaves),
                    ["created_at"] = Iso(BaseTime.AddDays(40).AddMinutes(members.Count)),
                });
            }
        }

        return new JsonObject
        {
            ["entities"] = entities,
            ["edges"] = edges,
            ["episodes"] = episodes,
            ["mentions"] = mentions,
            ["communities"] = communities,
            ["members"] = members,
        };
    }

    private static JsonObject NoiseSpec()
    {
        var entities = new JsonArray();
        for (var i = 0; i < 5; i++)
        {
            var name = $"noise entity {i}";
            entities.Add(new JsonObject
            {
                ["index"] = i,
                ["name"] = name,
                ["labels"] = Json(["Entity", Labels[i % 3]]),
                ["summary"] = Text([], [i + 2, 7 - i, 3], 25),
                ["created_at"] = Iso(BaseTime.AddMinutes(i)),
                ["name_embedding"] = Numbers(Embed(name)),
            });
        }

        var edges = new JsonArray();
        for (var k = 0; k < 4; k++)
        {
            var fact = Text([$"noisefact{k}"], [k + 1, 9 - k, 2 + k], 30);
            edges.Add(new JsonObject
            {
                ["index"] = k,
                ["source"] = k,
                ["target"] = k + 1,
                ["name"] = "KNOWS",
                ["fact"] = fact,
                ["created_at"] = Iso(BaseTime.AddHours(k)),
                ["valid_at"] = null,
                ["invalid_at"] = null,
                ["expired_at"] = null,
                ["episodes"] = new JsonArray(),
                ["fact_embedding"] = Numbers(Embed(fact)),
            });
        }

        var episodes = new JsonArray();
        for (var e = 0; e < 3; e++)
        {
            episodes.Add(new JsonObject
            {
                ["index"] = e,
                ["name"] = $"noise episode {e}",
                ["content"] = Text([$"noiseep{e}"], [e + 3, 2, 5 - e], 22),
                ["source"] = "message",
                ["source_description"] = "noise",
                ["created_at"] = Iso(BaseTime.AddDays(e)),
                ["valid_at"] = Iso(BaseTime.AddDays(e)),
            });
        }

        var mentions = new JsonArray();
        for (var e = 0; e < 3; e++)
        {
            mentions.Add(new JsonObject { ["index"] = e, ["episode"] = e, ["entity"] = e, ["created_at"] = Iso(BaseTime) });
        }

        var communities = new JsonArray();
        for (var c = 0; c < 2; c++)
        {
            var name = Text([$"noisecommunity{c}"], [c + 4, 1, 3 + c], 12);
            communities.Add(new JsonObject
            {
                ["index"] = c,
                ["name"] = name,
                ["summary"] = "noise community",
                ["created_at"] = Iso(BaseTime),
                ["name_embedding"] = Numbers(Embed(name)),
            });
        }

        var members = new JsonArray();
        for (var c = 0; c < 2; c++)
        {
            members.Add(new JsonObject { ["index"] = c, ["community"] = c, ["entity"] = c, ["created_at"] = Iso(BaseTime) });
        }

        return new JsonObject
        {
            ["entities"] = entities,
            ["edges"] = edges,
            ["episodes"] = episodes,
            ["mentions"] = mentions,
            ["communities"] = communities,
            ["members"] = members,
        };
    }

    // ---------------------------------------------------------------------------------------------------------
    // The queries. Query 1 passes bfs_origin_node_uuids. Query 2 passes a SearchFilters value.
    // ---------------------------------------------------------------------------------------------------------

    private sealed record Query(string Text, (string Kind, int Index)[]? BfsOrigins = null, bool Filtered = false);

    private static readonly Query[] Queries =
    [
        new(Words[0]),
        new(Words[1], BfsOrigins: [("episode", 0), ("entity", 1)]),
        new(Words[2], Filtered: true),
    ];

    private const string FilterDate = "2025-09-01T00:00:00+00:00";

    private static SearchFilters FilterOf(Query query) => !query.Filtered
        ? new SearchFilters()
        : new SearchFilters
        {
            NodeLabels = ["Person"],
            EdgeTypes = ["KNOWS"],
            ValidAt =
            [
                [new DateFilter { Date = PyDateTime.FromIsoFormat(FilterDate), ComparisonOperator = ComparisonOperator.LessThanEqual }],
                [new DateFilter { Date = null, ComparisonOperator = ComparisonOperator.IsNull }],
            ],
        };

    private static JsonArray QueriesJson()
    {
        var queries = new JsonArray();
        foreach (var query in Queries)
        {
            var item = new JsonObject { ["text"] = query.Text };
            if (query.BfsOrigins is not null)
            {
                item["bfs_origins"] = new JsonArray([.. query.BfsOrigins.Select(o => (JsonNode?)new JsonArray(o.Kind, o.Index))]);
            }

            if (query.Filtered)
            {
                item["filter"] = new JsonObject
                {
                    ["node_labels"] = Json(["Person"]),
                    ["edge_types"] = Json(["KNOWS"]),
                    ["valid_at"] = new JsonArray(
                        new JsonArray(new JsonObject { ["date"] = FilterDate, ["comparison_operator"] = "less_than_equal" }),
                        new JsonArray(new JsonObject { ["date"] = null, ["comparison_operator"] = "is_null" })),
                };
            }

            queries.Add(item);
        }

        return queries;
    }

    // ---------------------------------------------------------------------------------------------------------
    // The graph: one fixture group and one noise group, seeded once for the class
    // ---------------------------------------------------------------------------------------------------------

    public sealed class Graph : IAsyncDisposable
    {
        private readonly SemaphoreSlim gate = new(1, 1);
        private bool seeded;

        internal string Group { get; } = Neo4jSidecar.NewGroupId();

        internal string NoiseGroup { get; } = Neo4jSidecar.NewGroupId();

        internal Neo4jDriver Driver { get; } = new(Neo4jSidecar.Uri, Neo4jSidecar.User, Neo4jSidecar.Password);

        internal static string Uid(string group, int number) =>
            $"{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(group)))[..8]}-0000-4000-8000-{number:x12}";

        internal async Task Ready(CancellationToken ct)
        {
            await gate.WaitAsync(ct);
            try
            {
                if (!seeded)
                {
                    await LoopGuard.RunAsync(async () =>
                    {
                        await Driver.BuildIndicesAndConstraintsAsync(cancellationToken: ct);
                        await Driver.ExecuteQueryAsync("CALL db.awaitIndexes(120)", new(), ct);
                        await SeedAsync(Group, FixtureSpec(), ct);
                        await SeedAsync(NoiseGroup, NoiseSpec(), ct);
                    }, GuardSeconds);
                    seeded = true;
                }
            }
            finally
            {
                gate.Release();
            }
        }

        internal async Task<JsonObject> RunAsync(string recipe, int index, CancellationToken ct)
        {
            var query = Queries[index];
            var clients = new GraphitiClients
            {
                Driver = Driver,
                LlmClient = null!, // The search reads no LLM client.
                Embedder = new HashEmbedder(),
                CrossEncoder = new HashCrossEncoder(),
                Tracer = new NoOpTracer(),
            };
            var center = CenterRecipes.Contains(recipe) ? Uid(Group, EntityBase + Center) : null;
            var origins = query.BfsOrigins?.Select(o => Uid(Group, (o.Kind == "entity" ? EntityBase : EpisodeBase) + o.Index)).ToList();

            var results = await SearchModule.SearchAsync(
                clients,
                query.Text,
                [Group],
                Recipes[recipe],
                FilterOf(query),
                centerNodeUuid: center,
                bfsOriginNodeUuids: origins,
                cancellationToken: ct);

            return new JsonObject
            {
                ["edges"] = Json(results.Edges.Select(e => e.Fact)),
                ["edge_scores"] = Numbers(results.EdgeRerankerScores),
                ["nodes"] = Json(results.Nodes.Select(n => n.Name)),
                ["node_scores"] = Numbers(results.NodeRerankerScores),
                ["episodes"] = Json(results.Episodes.Select(e => e.Name)),
                ["episode_scores"] = Numbers(results.EpisodeRerankerScores),
                ["communities"] = Json(results.Communities.Select(c => c.Name)),
                ["community_scores"] = Numbers(results.CommunityRerankerScores),
            };
        }

        public async ValueTask DisposeAsync()
        {
            await LoopGuard.RunAsync(async () =>
            {
                foreach (var label in GroupLabels)
                {
                    await Driver.ExecuteQueryAsync(
                        $"MATCH (n:{label}) WHERE n.group_id IN $groups DETACH DELETE n",
                        new() { ["groups"] = new List<string> { Group, NoiseGroup } },
                        CancellationToken.None);
                }

                await Driver.CloseAsync(CancellationToken.None);
            }, GuardSeconds);
            gate.Dispose();
        }

        private async Task SeedAsync(string group, JsonObject spec, CancellationToken ct)
        {
            foreach (var item in spec["entities"]!.AsArray())
            {
                await new EntityNode
                {
                    Uuid = Uid(group, EntityBase + (int)item!["index"]!),
                    Name = Str(item["name"]),
                    GroupId = group,
                    Labels = [.. item["labels"]!.AsArray().Select(Str).Where(l => l != "Entity")],
                    CreatedAt = At(item["created_at"]),
                    NameEmbedding = [.. item["name_embedding"]!.AsArray().Select(n => (double)n!)],
                    Summary = Str(item["summary"]),
                }.SaveAsync(Driver, ct);
            }

            foreach (var item in spec["episodes"]!.AsArray())
            {
                await new EpisodicNode
                {
                    Uuid = Uid(group, EpisodeBase + (int)item!["index"]!),
                    Name = Str(item["name"]),
                    GroupId = group,
                    CreatedAt = At(item["created_at"]),
                    Source = Str(item["source"]) == "message" ? EpisodeType.Message : throw new InvalidOperationException("source"),
                    SourceDescription = Str(item["source_description"]),
                    Content = Str(item["content"]),
                    ValidAt = At(item["valid_at"]),
                }.SaveAsync(Driver, ct);
            }

            foreach (var item in spec["communities"]!.AsArray())
            {
                await new CommunityNode
                {
                    Uuid = Uid(group, CommunityBase + (int)item!["index"]!),
                    Name = Str(item["name"]),
                    GroupId = group,
                    CreatedAt = At(item["created_at"]),
                    NameEmbedding = [.. item["name_embedding"]!.AsArray().Select(n => (double)n!)],
                    Summary = Str(item["summary"]),
                }.SaveAsync(Driver, ct);
            }

            foreach (var item in spec["edges"]!.AsArray())
            {
                await new EntityEdge
                {
                    Uuid = Uid(group, EdgeBase + (int)item!["index"]!),
                    GroupId = group,
                    SourceNodeUuid = Uid(group, EntityBase + (int)item["source"]!),
                    TargetNodeUuid = Uid(group, EntityBase + (int)item["target"]!),
                    CreatedAt = At(item["created_at"]),
                    Name = Str(item["name"]),
                    Fact = Str(item["fact"]),
                    FactEmbedding = [.. item["fact_embedding"]!.AsArray().Select(n => (double)n!)],
                    Episodes = [.. item["episodes"]!.AsArray().Select(n => Uid(group, EpisodeBase + (int)n!))],
                    ValidAt = When(item["valid_at"]),
                    InvalidAt = When(item["invalid_at"]),
                    ExpiredAt = When(item["expired_at"]),
                }.SaveAsync(Driver, ct);
            }

            foreach (var item in spec["mentions"]!.AsArray())
            {
                await new EpisodicEdge
                {
                    Uuid = Uid(group, MentionBase + (int)item!["index"]!),
                    GroupId = group,
                    SourceNodeUuid = Uid(group, EpisodeBase + (int)item["episode"]!),
                    TargetNodeUuid = Uid(group, EntityBase + (int)item["entity"]!),
                    CreatedAt = At(item["created_at"]),
                }.SaveAsync(Driver, ct);
            }

            foreach (var item in spec["members"]!.AsArray())
            {
                await new CommunityEdge
                {
                    Uuid = Uid(group, MemberBase + (int)item!["index"]!),
                    GroupId = group,
                    SourceNodeUuid = Uid(group, CommunityBase + (int)item["community"]!),
                    TargetNodeUuid = Uid(group, EntityBase + (int)item["entity"]!),
                    CreatedAt = At(item["created_at"]),
                }.SaveAsync(Driver, ct);
            }
        }
    }
}
