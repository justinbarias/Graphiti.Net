// Layer 5 of plan 0004 (packet P5f): save and load each node type and each edge type on a real Neo4j, and build the
// indexes twice.
//
// The expected values come from Python. port/tools/record_graph_types_e2e.py ran the nine classes of graphiti_core
// (EntityNode, EpisodicNode, CommunityNode, SagaNode, EntityEdge, EpisodicEdge, CommunityEdge, HasEpisodeEdge,
// NextEpisodeEdge) against the same database, through the model methods and through the operations classes of
// Neo4jDriver, and it wrote values/graph_types_e2e.json. These tests run the same steps through the port and compare
// what the port stores and what it loads with what Python stored and loaded, field by field, with the types (an int
// stays an int, a date keeps its offset).
//
// Cross-language proof, with no Python at test time:
// - Rows_that_Python_saved_load_in_the_port_as_Python_loaded_them runs the query text and the parameters of each
//   save that Python ran (the "replay" section), then loads the rows through the port.
// - Each test that saves through the port compares the stored form (labels or type, and properties, read with
//   plain Cypher) with the stored form that Python wrote. Python loads a row as a function of its stored form, so
//   a row that equals the Python row loads in Python as the Python row does.
//
// Each test uses its own group id and its own uuids, and removes only its own group. The test replaces the group
// id of the recording and the first eight characters of each uuid. The replacement keeps the order of the uuids,
// which the ORDER BY of the queries needs. No test drops an index.
namespace Graphiti.E2E.Tests;

using System.Collections;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using global::Neo4j.Driver;
using Graphiti.Core;
using Graphiti.Neo4j;
using Graphiti.PyCompat;

public sealed class GraphTypesOnNeo4jTests
{
    private const long Plus0530 = 19_800_000_000L;
    private const long Minus0330 = -12_600_000_000L;
    private const long Plus1000 = 36_000_000_000L;

    private static readonly string[] NodeOrder = ["A", "B", "E1", "E2", "C", "S"];
    private static readonly string[] EdgeOrder = ["EE", "EP", "CE", "HE", "NE"];
    private static readonly string[] SagaEdges = ["HE", "NE"];
    private static readonly string[] NodeLabels = ["Entity", "Episodic", "Community", "Saga"];
    private static readonly string[] NodeKinds = ["EntityNode", "EpisodicNode", "CommunityNode", "SagaNode"];
    private static readonly string[] EdgeKinds = ["EntityEdge", "EpisodicEdge", "CommunityEdge", "HasEpisodeEdge", "NextEpisodeEdge"];

    private static readonly HashSet<string> Tags = ["none", "bool", "int", "float", "str", "datetime", "enum", "list", "dict"];

    private static readonly Lazy<string> RecordingText = new(() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "values", "graph_types_e2e.json"), Encoding.UTF8));

    // ---------------------------------------------------------------------------------------------------------
    // The tests
    // ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Rows_that_Python_saved_load_in_the_port_as_Python_loaded_them()
    {
        await using var run = new Run(TestContext.Current.CancellationToken);
        await Asyncio.Run(async () =>
        {
            foreach (var entry in run.Recording["replay"]!.AsArray())
            {
                var parameters = new OrderedDictionary<string, object?>();
                foreach (var pair in entry!["params"]!.AsArray())
                {
                    parameters[Str(pair![0])] = Decode(pair[1]!);
                }

                // The query text and the parameters are what Python ran. Only the driver below them is the port.
                await run.Driver.ExecuteQueryAsync(Str(entry["query"]), parameters, run.Ct);
            }

            var actual = new JsonObject();
            foreach (var kind in NodeKinds.Concat(EdgeKinds))
            {
                actual[kind] = await ReadThroughModels(run, kind, Kinds(run.Ct)[kind]);
            }

            AssertNoDifference(run.Recording["model"]!["read"]!, actual, "model.read (rows saved by Python)");
        });
    }

    [Fact]
    public async Task Each_type_saved_and_loaded_through_its_model_methods_matches_Python()
    {
        await using var run = new Run(TestContext.Current.CancellationToken);
        await Asyncio.Run(async () =>
        {
            var models = Fixtures(run);
            AssertInputsMatch(run, models);
            var kinds = Kinds(run.Ct);
            var driver = run.Driver;
            var recorded = run.Recording["model"]!;
            var actual = new JsonObject();

            foreach (var key in NodeOrder.Concat(EdgeOrder))
            {
                await kinds[KindOf(run, key)].Save(driver, models[key]);
            }

            var read = new JsonObject();
            foreach (var kind in NodeKinds.Concat(EdgeKinds))
            {
                read[kind] = await ReadThroughModels(run, kind, kinds[kind]);
            }

            actual["read"] = read;

            // Delete of each edge class.
            foreach (var key in EdgeOrder)
            {
                await kinds[KindOf(run, key)].Delete(driver, models[key]);
            }

            actual["after_edge_delete"] = await Exists(run, EdgeOrder);

            // Edge.delete_by_uuids removes MENTIONS, RELATES_TO, and HAS_MEMBER only.
            await SaveAll(run, kinds, models, EdgeOrder);
            await Edge.DeleteByUuidsAsync(driver, [.. EdgeOrder.Select(run.Id)], run.Ct);
            actual["after_edge_delete_by_uuids"] = await Exists(run, EdgeOrder);
            foreach (var key in SagaEdges)
            {
                await kinds[KindOf(run, key)].Delete(driver, models[key]);
            }

            // Delete of each node takes the edges of the node with it.
            await SaveAll(run, kinds, models, EdgeOrder);
            foreach (var key in NodeOrder)
            {
                await kinds[KindOf(run, key)].Delete(driver, models[key]);
            }

            actual["after_node_delete"] = await Exists(run, [.. NodeOrder, .. EdgeOrder]);

            // Node.delete_by_uuids and Node.delete_by_group_id remove the labels Entity, Episodic, and Community.
            await SaveAll(run, kinds, models, NodeOrder);
            await Node.DeleteByUuidsAsync(driver, [.. NodeOrder.Select(run.Id)], cancellationToken: run.Ct);
            actual["after_node_delete_by_uuids"] = await Exists(run, NodeOrder);
            await SaveAll(run, kinds, models, NodeOrder);
            await Node.DeleteByGroupIdAsync(driver, run.Group, cancellationToken: run.Ct);
            actual["after_node_delete_by_group_id"] = await Exists(run, NodeOrder);
            await kinds["SagaNode"].Delete(driver, models["S"]);
            actual["after_saga_delete"] = await Exists(run, ["S"]);

            AssertNoDifference(recorded, actual, "model");
        });
    }

    [Fact]
    public async Task Each_type_saved_and_loaded_through_the_driver_operations_matches_Python()
    {
        await using var run = new Run(TestContext.Current.CancellationToken);
        await Asyncio.Run(async () =>
        {
            var models = Fixtures(run);
            AssertInputsMatch(run, models);
            var kinds = Kinds(run.Ct);
            var driver = run.Driver;
            var actual = new JsonObject();

            async Task CleanNodes()
            {
                foreach (var key in NodeOrder)
                {
                    await kinds[KindOf(run, key)].OpsDelete(driver, models[key]);
                }
            }

            var saveErrors = new JsonArray();
            foreach (var key in NodeOrder)
            {
                saveErrors.Add(Keyed(key, "error", await Attempt(() => kinds[KindOf(run, key)].OpsSave(driver, models[key]))));
            }

            // The saga save of the operations class fails in Python (upstream defect U2), so the saga row comes from the model.
            await kinds["SagaNode"].Save(driver, models["S"]);
            foreach (var key in EdgeOrder)
            {
                saveErrors.Add(Keyed(key, "error", await Attempt(() => kinds[KindOf(run, key)].OpsSave(driver, models[key]))));
            }

            actual["save_errors"] = saveErrors;

            var read = new JsonObject();
            foreach (var kind in NodeKinds.Concat(EdgeKinds))
            {
                read[kind] = await ReadThroughOperations(run, kind, kinds[kind]);
            }

            actual["read"] = read;

            foreach (var key in EdgeOrder)
            {
                await kinds[KindOf(run, key)].OpsDelete(driver, models[key]);
            }

            actual["after_edge_delete"] = await Exists(run, EdgeOrder);

            actual["bulk_edge_errors"] = await Bulk(run, kinds, models, EdgeKinds);
            var bulkEdges = new JsonObject();
            foreach (var kind in EdgeKinds)
            {
                bulkEdges[kind] = await ReadThroughOperations(run, kind, kinds[kind]);
            }

            actual["bulk_edges"] = bulkEdges;
            actual["delete_by_uuids_edge_errors"] = await DeleteByUuids(run, kinds, EdgeKinds);
            actual["after_edge_delete_by_uuids"] = await Exists(run, EdgeOrder);

            foreach (var key in NodeOrder)
            {
                await kinds[KindOf(run, key)].OpsDelete(driver, models[key]);
            }

            actual["after_node_delete"] = await Exists(run, NodeOrder);

            actual["bulk_node_errors"] = await Bulk(run, kinds, models, NodeKinds);
            await kinds["SagaNode"].Save(driver, models["S"]);
            var bulkNodes = new JsonObject();
            foreach (var kind in NodeKinds)
            {
                bulkNodes[kind] = await ReadThroughOperations(run, kind, kinds[kind]);
            }

            actual["bulk_nodes"] = bulkNodes;
            actual["delete_by_uuids_node_errors"] = await DeleteByUuids(run, kinds, NodeKinds);
            actual["after_node_delete_by_uuids"] = await Exists(run, NodeOrder);
            await CleanNodes();

            await Bulk(run, kinds, models, NodeKinds);
            await kinds["SagaNode"].Save(driver, models["S"]);
            var byGroup = new JsonArray();
            foreach (var kind in NodeKinds)
            {
                byGroup.Add(Keyed(kind, "error", await Attempt(() => kinds[kind].OpsDeleteByGroupId!(driver, run.Group)), "kind"));
            }

            actual["delete_by_group_id_node_errors"] = byGroup;
            actual["after_node_delete_by_group_id"] = await Exists(run, NodeOrder);
            await CleanNodes();

            AssertNoDifference(run.Recording["ops"]!, actual, "ops");
        });
    }

    [Fact]
    public async Task A_save_that_Neo4j_refuses_fails_with_the_code_that_Python_saw()
    {
        await using var run = new Run(TestContext.Current.CancellationToken);
        await Asyncio.Run(async () =>
        {
            var when = new PyDateTime(2026, 1, 1, offsetMicroseconds: 0);
            var actual = new JsonObject();

            // Neo4j cannot store a map as a property value.
            actual["map_attribute"] = await Attempt(() => new EntityNode
            {
                Uuid = run.Uuid(0xF1),
                Name = "nested",
                GroupId = run.Group,
                NameEmbedding = [0.5],
                Attributes = new OrderedDictionary<string, object?> { ["inner"] = new OrderedDictionary<string, object?> { ["a"] = 1L } },
            }.SaveAsync(run.Driver, run.Ct));

            // Upstream defect U7: a save with no embedding fails, although the model allows it.
            actual["node_without_embedding"] = await Attempt(() => new EntityNode
            {
                Uuid = run.Uuid(0xF2),
                Name = "plain",
                GroupId = run.Group,
            }.SaveAsync(run.Driver, run.Ct));

            var first = new EntityNode { Uuid = run.Uuid(0xF3), Name = "one", GroupId = run.Group, NameEmbedding = [0.5] };
            var second = new EntityNode { Uuid = run.Uuid(0xF4), Name = "two", GroupId = run.Group, NameEmbedding = [0.5] };
            await first.SaveAsync(run.Driver, run.Ct);
            await second.SaveAsync(run.Driver, run.Ct);
            actual["edge_without_embedding"] = await Attempt(() => new EntityEdge
            {
                Uuid = run.Uuid(0xF5),
                GroupId = run.Group,
                SourceNodeUuid = first.Uuid,
                TargetNodeUuid = second.Uuid,
                CreatedAt = when,
                Name = "R",
                Fact = "f",
            }.SaveAsync(run.Driver, run.Ct));

            AssertNoDifference(run.Recording["save_errors"]!, actual, "save_errors");
        });
    }

    [Fact]
    public async Task Building_the_indexes_twice_gives_the_same_list_and_has_each_index_that_Python_creates()
    {
        await using var run = new Run(TestContext.Current.CancellationToken);
        await Asyncio.Run(async () =>
        {
            var recorded = run.Recording["indexes"]!;
            var expected = recorded["after_first_build"]!.AsArray();
            var names = recorded["names"]!.AsArray().Select(n => Str(n)).ToHashSet();
            Assert.Equal(names.Count, expected.Count);

            var before = await ReadIndexes(run);
            await run.Driver.BuildIndicesAndConstraintsAsync(cancellationToken: run.Ct);
            var first = await ReadIndexes(run);
            await run.Driver.BuildIndicesAndConstraintsAsync(cancellationToken: run.Ct);
            var second = await ReadIndexes(run);

            // The two lists are equal. The lists hold the indexes on the labels and the types of the Graphiti schema.
            var sink = new List<string>();
            Diff(Json(first), Json(second), "second build", sink);
            Assert.True(sink.Count == 0, "The second build changed the indexes:\n" + string.Join('\n', sink));

            // Each index that the Python driver creates is in the list, with its kind, labels or types, properties, and state.
            var expectedList = new JsonArray([.. expected.Select(e => e!.DeepClone())]);
            var actualByName = new JsonArray([.. first.Where(i => names.Contains(Str(i["name"]))).Select(i => (JsonNode?)Json(i))]);
            AssertNoDifference(expectedList, actualByName, "indexes that Python creates");

            // The build adds no index that Python does not create, and it removes none.
            var beforeNames = before.Select(i => Str(i["name"])).ToHashSet();
            var added = first.Where(i => !beforeNames.Contains(Str(i["name"]))).Select(i => Str(i["name"])).ToList();
            Assert.All(added, name => Assert.Contains(name, names));
            Assert.All(before, i => Assert.Contains(first, f => Str(f["name"]) == Str(i["name"])));
        });
    }

    // ---------------------------------------------------------------------------------------------------------
    // The run: a group, uuids, a driver, and the recording with the group and the uuids replaced
    // ---------------------------------------------------------------------------------------------------------

    private sealed class Run : IAsyncDisposable
    {
        private readonly string prefix = Guid.NewGuid().ToString("N")[..8];
        private readonly Dictionary<string, string> uuids = [];

        internal Run(CancellationToken ct)
        {
            Ct = ct;
            Group = Neo4jSidecar.NewGroupId();
            Driver = new Neo4jDriver(Neo4jSidecar.Uri, Neo4jSidecar.User, Neo4jSidecar.Password);

            var original = JsonNode.Parse(RecordingText.Value)!;
            var meta = original["meta"]!;
            var text = RecordingText.Value;
            foreach (var (key, value) in meta["uuids"]!.AsObject())
            {
                var fixture = Str(value);
                uuids[key] = Replace(fixture);
                text = text.Replace(fixture, uuids[key], StringComparison.Ordinal);
            }

            var missing = Str(meta["missing_uuid"]);
            Missing = Replace(missing);
            text = text.Replace(missing, Missing, StringComparison.Ordinal);
            text = text.Replace(Str(meta["group"]), Group, StringComparison.Ordinal);
            Recording = JsonNode.Parse(text)!;
            Kinds = meta["kinds"]!.AsObject().ToDictionary(p => p.Key, p => Str(p.Value));
        }

        internal CancellationToken Ct { get; }

        internal string Group { get; }

        internal string Missing { get; }

        internal Neo4jDriver Driver { get; }

        internal JsonNode Recording { get; }

        internal Dictionary<string, string> Kinds { get; }

        internal string Id(string key) => uuids[key];

        /// <summary>A uuid for a row that the recording does not hold. It has the same prefix as the other uuids.</summary>
        internal string Uuid(int number) => $"{prefix}-0000-4000-8000-{number:x12}";

        public async ValueTask DisposeAsync()
        {
            await Asyncio.Run(async () =>
            {
                foreach (var label in NodeLabels)
                {
                    await Driver.ExecuteQueryAsync(
                        $"MATCH (n:{label}) WHERE n.group_id = $g DETACH DELETE n",
                        new() { ["g"] = Group },
                        CancellationToken.None);
                }

                await Driver.CloseAsync(CancellationToken.None);
            });
        }

        // The recording holds uuids like 00000000-0000-4000-8000-0000000000a1. Only the first eight characters change.
        private string Replace(string fixture) => prefix + fixture[8..];
    }

    // ---------------------------------------------------------------------------------------------------------
    // The inputs
    // ---------------------------------------------------------------------------------------------------------

    private static string KindOf(Run run, string key) => run.Kinds[key];

    private static Dictionary<string, object> Fixtures(Run run)
    {
        var g = run.Group;
        return new Dictionary<string, object>
        {
            ["A"] = new EntityNode
            {
                Uuid = run.Id("A"),
                Name = "Zoë 東京 😀",
                GroupId = g,
                Labels = ["Person", "Employee"],
                CreatedAt = new PyDateTime(2026, 3, 4, 5, 6, 7, 123456, Plus0530),
                NameEmbedding = [0.1, -0.25, 0.5, 1.0],
                Summary = "Línea uno\nlínea 二 ☃",
                Attributes = new OrderedDictionary<string, object?>
                {
                    ["age"] = 42L,
                    ["height"] = 1.75,
                    ["active"] = true,
                    ["tags"] = new List<object?> { "a", "é" },
                    ["born"] = new PyDateTime(1990, 7, 8, 9, 10, 11, 500000, Minus0330),
                    ["note"] = "ünï 😀",
                    ["missing"] = null,
                    ["big"] = 1L << 40,
                    ["ratio"] = 2.0,
                },
            },
            ["B"] = new EntityNode
            {
                Uuid = run.Id("B"),
                Name = "Bob Ünïcode",
                GroupId = g,
                Labels = ["Place"],
                CreatedAt = new PyDateTime(2026, 3, 5, 6, 7, 8, 1, Plus1000),
                NameEmbedding = [-0.5, 0.75],
                Summary = "Segundo",
                Attributes = new OrderedDictionary<string, object?> { ["country"] = "Perú", ["visits"] = 3L },
            },
            ["E1"] = new EpisodicNode
            {
                Uuid = run.Id("E1"),
                Name = "épisode 1",
                GroupId = g,
                Labels = ["Custom"],
                CreatedAt = new PyDateTime(2026, 4, 5, 6, 7, 8, 999999, Minus0330),
                Source = EpisodeType.Json,
                SourceDescription = "Zoë source 😀",
                Content = "{\"quoté\": \"日本\", \"n\": 1}",
                ValidAt = new PyDateTime(2026, 1, 2, 3, 4, 5, 6, Plus0530),
                EntityEdges = ["edge-1", "édge-2"],
                EpisodeMetadata = new OrderedDictionary<string, object?> { ["k"] = "v", ["n"] = 1L },
            },
            ["E2"] = new EpisodicNode
            {
                Uuid = run.Id("E2"),
                Name = "episode 2",
                GroupId = g,
                CreatedAt = new PyDateTime(2026, 4, 6, 6, 7, 8, 0, 0),
                Source = EpisodeType.Message,
                SourceDescription = "chat",
                Content = "Hola: mundo",
                ValidAt = new PyDateTime(2026, 1, 3, 3, 4, 5, 0, 0),
            },
            ["C"] = new CommunityNode
            {
                Uuid = run.Id("C"),
                Name = "Comunidad ñ 東",
                GroupId = g,
                CreatedAt = new PyDateTime(2026, 5, 6, 7, 8, 9, 10, Plus1000),
                NameEmbedding = [0.5, 0.25, -0.75],
                Summary = "resumen 日本 ✓",
            },
            ["S"] = new SagaNode
            {
                Uuid = run.Id("S"),
                Name = "Saga ü",
                GroupId = g,
                CreatedAt = new PyDateTime(2026, 6, 7, 8, 9, 10, 11, Plus0530),
                Summary = "saga summary é",
                FirstEpisodeUuid = run.Id("E1"),
                LastEpisodeUuid = run.Id("E2"),
                LastSummarizedAt = new PyDateTime(2026, 6, 8, 9, 10, 11, 12, Minus0330),
                LastSummarizedEpisodeValidAt = new PyDateTime(2026, 6, 9, 10, 11, 12, 13, Plus1000),

                // The four counters are not stored. A loaded saga has zero in each.
                SummaryEpisodesFetched = 7,
                SummaryEpisodesSelected = 5,
                SummaryEpisodesSkipped = 2,
                SummarySelectedEpisodeBytes = 4096,
            },
            ["EE"] = new EntityEdge
            {
                Uuid = run.Id("EE"),
                GroupId = g,
                SourceNodeUuid = run.Id("A"),
                TargetNodeUuid = run.Id("B"),
                CreatedAt = new PyDateTime(2026, 7, 8, 9, 10, 11, 123456, Plus0530),
                Name = "WORKS_AT",
                Fact = "Zoë trabaja en 東京 😀",
                FactEmbedding = [0.3, -0.6, 0.9],
                Episodes = [run.Id("E1"), "ép-2"],
                ExpiredAt = new PyDateTime(2026, 8, 9, 10, 11, 12, 0, Minus0330),
                ValidAt = new PyDateTime(2026, 1, 1, 0, 0, 0, 0, 0),
                InvalidAt = new PyDateTime(2027, 1, 1, 0, 0, 0, 1, Plus1000),
                ReferenceTime = new PyDateTime(2026, 2, 3, 4, 5, 6, 7),
                Attributes = new OrderedDictionary<string, object?>
                {
                    ["weight"] = 0.5,
                    ["since"] = 2020L,
                    ["tags"] = new List<object?> { "x", "ý" },
                    ["flag"] = false,
                    ["unset"] = null,
                },
            },
            ["EP"] = new EpisodicEdge
            {
                Uuid = run.Id("EP"),
                GroupId = g,
                SourceNodeUuid = run.Id("E1"),
                TargetNodeUuid = run.Id("A"),
                CreatedAt = new PyDateTime(2026, 7, 9, 9, 10, 11, 1, Plus0530),
            },
            ["CE"] = new CommunityEdge
            {
                Uuid = run.Id("CE"),
                GroupId = g,
                SourceNodeUuid = run.Id("C"),
                TargetNodeUuid = run.Id("A"),
                CreatedAt = new PyDateTime(2026, 7, 10, 9, 10, 11, 2, Minus0330),
            },
            ["HE"] = new HasEpisodeEdge
            {
                Uuid = run.Id("HE"),
                GroupId = g,
                SourceNodeUuid = run.Id("S"),
                TargetNodeUuid = run.Id("E1"),
                CreatedAt = new PyDateTime(2026, 7, 11, 9, 10, 11, 3, Plus1000),
            },
            ["NE"] = new NextEpisodeEdge
            {
                Uuid = run.Id("NE"),
                GroupId = g,
                SourceNodeUuid = run.Id("E1"),
                TargetNodeUuid = run.Id("E2"),
                CreatedAt = new PyDateTime(2026, 7, 12, 9, 10, 11, 4, 0),
            },
        };
    }

    /// <summary>The inputs of the test are the inputs that Python saved. A drift between the two would hide a defect.</summary>
    private static void AssertInputsMatch(Run run, Dictionary<string, object> models)
    {
        var actual = new JsonObject();
        foreach (var (key, model) in models)
        {
            actual[key] = EncodeModel(model);
        }

        AssertNoDifference(run.Recording["inputs"]!, actual, "inputs");
    }

    // ---------------------------------------------------------------------------------------------------------
    // The kinds: one adapter for each of the nine classes
    // ---------------------------------------------------------------------------------------------------------

    private sealed class Kind
    {
        // The methods of the model class.
        internal required Func<Neo4jDriver, object, Task> Save { get; init; }

        internal required Func<Neo4jDriver, object, Task> Delete { get; init; }

        internal required Func<Neo4jDriver, string, Task<object>> GetByUuid { get; init; }

        internal required Func<Neo4jDriver, List<string>, Task<List<object>>> GetByUuids { get; init; }

        internal required Func<Neo4jDriver, List<string>, Task<List<object>>> GetByGroupIds { get; init; }

        internal Func<Neo4jDriver, object, Task>? LoadEmbedding { get; init; }

        internal Func<Neo4jDriver, List<string>, Task<List<object>>>? GetByGroupIdsWithEmbeddings { get; init; }

        // The methods of the operations class.
        internal required Func<Neo4jDriver, object, Task> OpsSave { get; init; }

        internal Func<Neo4jDriver, List<object>, Task>? OpsSaveBulk { get; init; }

        internal required Func<Neo4jDriver, object, Task> OpsDelete { get; init; }

        internal required Func<Neo4jDriver, List<string>, Task> OpsDeleteByUuids { get; init; }

        internal Func<Neo4jDriver, string, Task>? OpsDeleteByGroupId { get; init; }

        internal required Func<Neo4jDriver, string, Task<object>> OpsGetByUuid { get; init; }

        internal required Func<Neo4jDriver, List<string>, Task<List<object>>> OpsGetByUuids { get; init; }

        internal required Func<Neo4jDriver, List<string>, Task<List<object>>> OpsGetByGroupIds { get; init; }

        internal Func<Neo4jDriver, object, Task>? OpsLoadEmbedding { get; init; }

        internal Func<Neo4jDriver, List<object>, Task>? OpsLoadEmbeddingsBulk { get; init; }

        internal Action<object>? ResetEmbedding { get; init; }

        internal Func<object, object?>? Embedding { get; init; }
    }

    private static List<object> Boxed<T>(List<T> items) where T : notnull => [.. items.Cast<object>()];

    private static List<T> Typed<T>(List<object> items) => [.. items.Cast<T>()];

    private static Dictionary<string, Kind> Kinds(CancellationToken ct) => new()
    {
        ["EntityNode"] = new Kind
        {
            Save = (d, m) => ((EntityNode)m).SaveAsync(d, ct),
            Delete = (d, m) => ((EntityNode)m).DeleteAsync(d, ct),
            GetByUuid = async (d, u) => await EntityNode.GetByUuidAsync(d, u, ct),
            GetByUuids = async (d, u) => Boxed(await EntityNode.GetByUuidsAsync(d, u, cancellationToken: ct)),
            GetByGroupIds = async (d, g) => Boxed(await EntityNode.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            LoadEmbedding = (d, m) => ((EntityNode)m).LoadNameEmbeddingAsync(d, ct),
            GetByGroupIdsWithEmbeddings = async (d, g) => Boxed(await EntityNode.GetByGroupIdsAsync(d, g, withEmbeddings: true, cancellationToken: ct)),
            OpsSave = (d, m) => d.EntityNodeOps.SaveAsync(d, (EntityNode)m, cancellationToken: ct),
            OpsSaveBulk = (d, m) => d.EntityNodeOps.SaveBulkAsync(d, Typed<EntityNode>(m), cancellationToken: ct),
            OpsDelete = (d, m) => d.EntityNodeOps.DeleteAsync(d, (EntityNode)m, cancellationToken: ct),
            OpsDeleteByUuids = (d, u) => d.EntityNodeOps.DeleteByUuidsAsync(d, u, cancellationToken: ct),
            OpsDeleteByGroupId = (d, g) => d.EntityNodeOps.DeleteByGroupIdAsync(d, g, cancellationToken: ct),
            OpsGetByUuid = async (d, u) => await d.EntityNodeOps.GetByUuidAsync(d, u, ct),
            OpsGetByUuids = async (d, u) => Boxed(await d.EntityNodeOps.GetByUuidsAsync(d, u, ct)),
            OpsGetByGroupIds = async (d, g) => Boxed(await d.EntityNodeOps.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            OpsLoadEmbedding = (d, m) => d.EntityNodeOps.LoadEmbeddingsAsync(d, (EntityNode)m, ct),
            OpsLoadEmbeddingsBulk = (d, m) => d.EntityNodeOps.LoadEmbeddingsBulkAsync(d, Typed<EntityNode>(m), cancellationToken: ct),
            ResetEmbedding = m => ((EntityNode)m).NameEmbedding = null,
            Embedding = m => ((EntityNode)m).NameEmbedding,
        },
        ["EpisodicNode"] = new Kind
        {
            Save = (d, m) => ((EpisodicNode)m).SaveAsync(d, ct),
            Delete = (d, m) => ((EpisodicNode)m).DeleteAsync(d, ct),
            GetByUuid = async (d, u) => await EpisodicNode.GetByUuidAsync(d, u, cancellationToken: ct),
            GetByUuids = async (d, u) => Boxed(await EpisodicNode.GetByUuidsAsync(d, u, cancellationToken: ct)),
            GetByGroupIds = async (d, g) => Boxed(await EpisodicNode.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            OpsSave = (d, m) => d.EpisodeNodeOps.SaveAsync(d, (EpisodicNode)m, cancellationToken: ct),
            OpsSaveBulk = (d, m) => d.EpisodeNodeOps.SaveBulkAsync(d, Typed<EpisodicNode>(m), cancellationToken: ct),
            OpsDelete = (d, m) => d.EpisodeNodeOps.DeleteAsync(d, (EpisodicNode)m, cancellationToken: ct),
            OpsDeleteByUuids = (d, u) => d.EpisodeNodeOps.DeleteByUuidsAsync(d, u, cancellationToken: ct),
            OpsDeleteByGroupId = (d, g) => d.EpisodeNodeOps.DeleteByGroupIdAsync(d, g, cancellationToken: ct),
            OpsGetByUuid = async (d, u) => await d.EpisodeNodeOps.GetByUuidAsync(d, u, ct),
            OpsGetByUuids = async (d, u) => Boxed(await d.EpisodeNodeOps.GetByUuidsAsync(d, u, ct)),
            OpsGetByGroupIds = async (d, g) => Boxed(await d.EpisodeNodeOps.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
        },
        ["CommunityNode"] = new Kind
        {
            Save = (d, m) => ((CommunityNode)m).SaveAsync(d, ct),
            Delete = (d, m) => ((CommunityNode)m).DeleteAsync(d, ct),
            GetByUuid = async (d, u) => await CommunityNode.GetByUuidAsync(d, u, ct),
            GetByUuids = async (d, u) => Boxed(await CommunityNode.GetByUuidsAsync(d, u, cancellationToken: ct)),
            GetByGroupIds = async (d, g) => Boxed(await CommunityNode.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            LoadEmbedding = (d, m) => ((CommunityNode)m).LoadNameEmbeddingAsync(d, ct),
            OpsSave = (d, m) => d.CommunityNodeOps.SaveAsync(d, (CommunityNode)m, cancellationToken: ct),
            OpsSaveBulk = (d, m) => d.CommunityNodeOps.SaveBulkAsync(d, Typed<CommunityNode>(m), cancellationToken: ct),
            OpsDelete = (d, m) => d.CommunityNodeOps.DeleteAsync(d, (CommunityNode)m, cancellationToken: ct),
            OpsDeleteByUuids = (d, u) => d.CommunityNodeOps.DeleteByUuidsAsync(d, u, cancellationToken: ct),
            OpsDeleteByGroupId = (d, g) => d.CommunityNodeOps.DeleteByGroupIdAsync(d, g, cancellationToken: ct),
            OpsGetByUuid = async (d, u) => await d.CommunityNodeOps.GetByUuidAsync(d, u, ct),
            OpsGetByUuids = async (d, u) => Boxed(await d.CommunityNodeOps.GetByUuidsAsync(d, u, ct)),
            OpsGetByGroupIds = async (d, g) => Boxed(await d.CommunityNodeOps.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            OpsLoadEmbedding = (d, m) => d.CommunityNodeOps.LoadNameEmbeddingAsync(d, (CommunityNode)m, ct),
            ResetEmbedding = m => ((CommunityNode)m).NameEmbedding = null,
            Embedding = m => ((CommunityNode)m).NameEmbedding,
        },
        ["SagaNode"] = new Kind
        {
            Save = (d, m) => ((SagaNode)m).SaveAsync(d, ct),
            Delete = (d, m) => ((SagaNode)m).DeleteAsync(d, ct),
            GetByUuid = async (d, u) => await SagaNode.GetByUuidAsync(d, u, ct),
            GetByUuids = async (d, u) => Boxed(await SagaNode.GetByUuidsAsync(d, u, cancellationToken: ct)),
            GetByGroupIds = async (d, g) => Boxed(await SagaNode.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            OpsSave = (d, m) => d.SagaNodeOps.SaveAsync(d, (SagaNode)m, cancellationToken: ct),
            OpsSaveBulk = (d, m) => d.SagaNodeOps.SaveBulkAsync(d, Typed<SagaNode>(m), cancellationToken: ct),
            OpsDelete = (d, m) => d.SagaNodeOps.DeleteAsync(d, (SagaNode)m, cancellationToken: ct),
            OpsDeleteByUuids = (d, u) => d.SagaNodeOps.DeleteByUuidsAsync(d, u, cancellationToken: ct),
            OpsDeleteByGroupId = (d, g) => d.SagaNodeOps.DeleteByGroupIdAsync(d, g, cancellationToken: ct),
            OpsGetByUuid = async (d, u) => await d.SagaNodeOps.GetByUuidAsync(d, u, ct),
            OpsGetByUuids = async (d, u) => Boxed(await d.SagaNodeOps.GetByUuidsAsync(d, u, ct)),
            OpsGetByGroupIds = async (d, g) => Boxed(await d.SagaNodeOps.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
        },
        ["EntityEdge"] = new Kind
        {
            Save = (d, m) => ((EntityEdge)m).SaveAsync(d, ct),
            Delete = (d, m) => ((EntityEdge)m).DeleteAsync(d, ct),
            GetByUuid = async (d, u) => await EntityEdge.GetByUuidAsync(d, u, cancellationToken: ct),
            GetByUuids = async (d, u) => Boxed(await EntityEdge.GetByUuidsAsync(d, u, cancellationToken: ct)),
            GetByGroupIds = async (d, g) => Boxed(await EntityEdge.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            LoadEmbedding = (d, m) => ((EntityEdge)m).LoadFactEmbeddingAsync(d, ct),
            OpsSave = (d, m) => d.EntityEdgeOps.SaveAsync(d, (EntityEdge)m, cancellationToken: ct),
            OpsSaveBulk = (d, m) => d.EntityEdgeOps.SaveBulkAsync(d, Typed<EntityEdge>(m), cancellationToken: ct),
            OpsDelete = (d, m) => d.EntityEdgeOps.DeleteAsync(d, (EntityEdge)m, cancellationToken: ct),
            OpsDeleteByUuids = (d, u) => d.EntityEdgeOps.DeleteByUuidsAsync(d, u, cancellationToken: ct),
            OpsGetByUuid = async (d, u) => await d.EntityEdgeOps.GetByUuidAsync(d, u, ct),
            OpsGetByUuids = async (d, u) => Boxed(await d.EntityEdgeOps.GetByUuidsAsync(d, u, ct)),
            OpsGetByGroupIds = async (d, g) => Boxed(await d.EntityEdgeOps.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            OpsLoadEmbedding = (d, m) => d.EntityEdgeOps.LoadEmbeddingsAsync(d, (EntityEdge)m, ct),
            OpsLoadEmbeddingsBulk = (d, m) => d.EntityEdgeOps.LoadEmbeddingsBulkAsync(d, Typed<EntityEdge>(m), cancellationToken: ct),
            ResetEmbedding = m => ((EntityEdge)m).FactEmbedding = null,
            Embedding = m => ((EntityEdge)m).FactEmbedding,
        },
        ["EpisodicEdge"] = new Kind
        {
            Save = (d, m) => ((EpisodicEdge)m).SaveAsync(d, ct),
            Delete = (d, m) => ((EpisodicEdge)m).DeleteAsync(d, ct),
            GetByUuid = async (d, u) => await EpisodicEdge.GetByUuidAsync(d, u, ct),
            GetByUuids = async (d, u) => Boxed(await EpisodicEdge.GetByUuidsAsync(d, u, cancellationToken: ct)),
            GetByGroupIds = async (d, g) => Boxed(await EpisodicEdge.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            OpsSave = (d, m) => d.EpisodicEdgeOps.SaveAsync(d, (EpisodicEdge)m, cancellationToken: ct),
            OpsSaveBulk = (d, m) => d.EpisodicEdgeOps.SaveBulkAsync(d, Typed<EpisodicEdge>(m), cancellationToken: ct),
            OpsDelete = (d, m) => d.EpisodicEdgeOps.DeleteAsync(d, (EpisodicEdge)m, cancellationToken: ct),
            OpsDeleteByUuids = (d, u) => d.EpisodicEdgeOps.DeleteByUuidsAsync(d, u, cancellationToken: ct),
            OpsGetByUuid = async (d, u) => await d.EpisodicEdgeOps.GetByUuidAsync(d, u, ct),
            OpsGetByUuids = async (d, u) => Boxed(await d.EpisodicEdgeOps.GetByUuidsAsync(d, u, ct)),
            OpsGetByGroupIds = async (d, g) => Boxed(await d.EpisodicEdgeOps.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
        },
        ["CommunityEdge"] = new Kind
        {
            Save = (d, m) => ((CommunityEdge)m).SaveAsync(d, ct),
            Delete = (d, m) => ((CommunityEdge)m).DeleteAsync(d, ct),
            GetByUuid = async (d, u) => await CommunityEdge.GetByUuidAsync(d, u, ct),
            GetByUuids = async (d, u) => Boxed(await CommunityEdge.GetByUuidsAsync(d, u, cancellationToken: ct)),
            GetByGroupIds = async (d, g) => Boxed(await CommunityEdge.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            OpsSave = (d, m) => d.CommunityEdgeOps.SaveAsync(d, (CommunityEdge)m, cancellationToken: ct),
            OpsDelete = (d, m) => d.CommunityEdgeOps.DeleteAsync(d, (CommunityEdge)m, cancellationToken: ct),
            OpsDeleteByUuids = (d, u) => d.CommunityEdgeOps.DeleteByUuidsAsync(d, u, cancellationToken: ct),
            OpsGetByUuid = async (d, u) => await d.CommunityEdgeOps.GetByUuidAsync(d, u, ct),
            OpsGetByUuids = async (d, u) => Boxed(await d.CommunityEdgeOps.GetByUuidsAsync(d, u, ct)),
            OpsGetByGroupIds = async (d, g) => Boxed(await d.CommunityEdgeOps.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
        },
        ["HasEpisodeEdge"] = new Kind
        {
            Save = (d, m) => ((HasEpisodeEdge)m).SaveAsync(d, ct),
            Delete = (d, m) => ((HasEpisodeEdge)m).DeleteAsync(d, ct),
            GetByUuid = async (d, u) => await HasEpisodeEdge.GetByUuidAsync(d, u, ct),
            GetByUuids = async (d, u) => Boxed(await HasEpisodeEdge.GetByUuidsAsync(d, u, cancellationToken: ct)),
            GetByGroupIds = async (d, g) => Boxed(await HasEpisodeEdge.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            OpsSave = (d, m) => d.HasEpisodeEdgeOps.SaveAsync(d, (HasEpisodeEdge)m, cancellationToken: ct),
            OpsSaveBulk = (d, m) => d.HasEpisodeEdgeOps.SaveBulkAsync(d, Typed<HasEpisodeEdge>(m), cancellationToken: ct),
            OpsDelete = (d, m) => d.HasEpisodeEdgeOps.DeleteAsync(d, (HasEpisodeEdge)m, cancellationToken: ct),
            OpsDeleteByUuids = (d, u) => d.HasEpisodeEdgeOps.DeleteByUuidsAsync(d, u, cancellationToken: ct),
            OpsGetByUuid = async (d, u) => await d.HasEpisodeEdgeOps.GetByUuidAsync(d, u, ct),
            OpsGetByUuids = async (d, u) => Boxed(await d.HasEpisodeEdgeOps.GetByUuidsAsync(d, u, ct)),
            OpsGetByGroupIds = async (d, g) => Boxed(await d.HasEpisodeEdgeOps.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
        },
        ["NextEpisodeEdge"] = new Kind
        {
            Save = (d, m) => ((NextEpisodeEdge)m).SaveAsync(d, ct),
            Delete = (d, m) => ((NextEpisodeEdge)m).DeleteAsync(d, ct),
            GetByUuid = async (d, u) => await NextEpisodeEdge.GetByUuidAsync(d, u, ct),
            GetByUuids = async (d, u) => Boxed(await NextEpisodeEdge.GetByUuidsAsync(d, u, cancellationToken: ct)),
            GetByGroupIds = async (d, g) => Boxed(await NextEpisodeEdge.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
            OpsSave = (d, m) => d.NextEpisodeEdgeOps.SaveAsync(d, (NextEpisodeEdge)m, cancellationToken: ct),
            OpsSaveBulk = (d, m) => d.NextEpisodeEdgeOps.SaveBulkAsync(d, Typed<NextEpisodeEdge>(m), cancellationToken: ct),
            OpsDelete = (d, m) => d.NextEpisodeEdgeOps.DeleteAsync(d, (NextEpisodeEdge)m, cancellationToken: ct),
            OpsDeleteByUuids = (d, u) => d.NextEpisodeEdgeOps.DeleteByUuidsAsync(d, u, cancellationToken: ct),
            OpsGetByUuid = async (d, u) => await d.NextEpisodeEdgeOps.GetByUuidAsync(d, u, ct),
            OpsGetByUuids = async (d, u) => Boxed(await d.NextEpisodeEdgeOps.GetByUuidsAsync(d, u, ct)),
            OpsGetByGroupIds = async (d, g) => Boxed(await d.NextEpisodeEdgeOps.GetByGroupIdsAsync(d, g, cancellationToken: ct)),
        },
    };

    // ---------------------------------------------------------------------------------------------------------
    // The steps. Each step builds a value of the shape that the recorder wrote.
    // ---------------------------------------------------------------------------------------------------------

    private static IEnumerable<string> KeysOf(Run run, string kind) =>
        NodeOrder.Concat(EdgeOrder).Where(key => run.Kinds[key] == kind);

    private static async Task SaveAll(Run run, Dictionary<string, Kind> kinds, Dictionary<string, object> models, string[] keys)
    {
        foreach (var key in keys)
        {
            await kinds[KindOf(run, key)].Save(run.Driver, models[key]);
        }
    }

    private static async Task<JsonObject> ReadThroughModels(Run run, string kindName, Kind kind)
    {
        var d = run.Driver;
        var keys = KeysOf(run, kindName).ToList();
        var uuids = keys.Select(run.Id).ToList();
        var o = new JsonObject { ["raw"] = await RawAll(run, keys) };

        var byUuid = new JsonArray();
        foreach (var key in keys)
        {
            byUuid.Add(Keyed(key, "model", EncodeModel(await kind.GetByUuid(d, run.Id(key)))));
        }

        o["by_uuid"] = byUuid;
        o["by_uuids"] = Models(await kind.GetByUuids(d, [.. uuids, run.Missing]), sort: true);
        o["by_group_ids"] = Models(await kind.GetByGroupIds(d, [run.Group, run.Group + "_other"]));
        o["missing"] = await Attempt(() => kind.GetByUuid(d, run.Missing));

        if (kind.Embedding is not null)
        {
            var embeddings = new JsonArray();
            foreach (var key in keys)
            {
                var model = await kind.GetByUuid(d, run.Id(key));
                kind.ResetEmbedding!(model);
                await kind.LoadEmbedding!(d, model);
                embeddings.Add(Keyed(key, "embedding", Encode(kind.Embedding(model))));
            }

            o["embeddings"] = embeddings;
        }

        if (kind.GetByGroupIdsWithEmbeddings is not null)
        {
            o["by_group_ids_with_embeddings"] = Models(await kind.GetByGroupIdsWithEmbeddings(d, [run.Group]));
        }

        return o;
    }

    private static async Task<JsonObject> ReadThroughOperations(Run run, string kindName, Kind kind)
    {
        var d = run.Driver;
        var keys = KeysOf(run, kindName).ToList();
        var uuids = keys.Select(run.Id).ToList();
        var o = new JsonObject { ["raw"] = await RawAll(run, keys) };

        var byUuid = new JsonArray();
        foreach (var key in keys)
        {
            byUuid.Add(Keyed(key, "model", EncodeModel(await kind.OpsGetByUuid(d, run.Id(key)))));
        }

        o["by_uuid"] = byUuid;
        o["by_uuids"] = Models(await kind.OpsGetByUuids(d, [.. uuids, run.Missing]), sort: true);
        o["by_group_ids"] = Models(await kind.OpsGetByGroupIds(d, [run.Group, run.Group + "_other"]));
        o["missing"] = await Attempt(() => kind.OpsGetByUuid(d, run.Missing));

        if (kind.OpsLoadEmbedding is not null)
        {
            var embeddings = new JsonArray();
            foreach (var key in keys)
            {
                var model = await kind.OpsGetByUuid(d, run.Id(key));
                kind.ResetEmbedding!(model);
                await kind.OpsLoadEmbedding(d, model);
                embeddings.Add(Keyed(key, "embedding", Encode(kind.Embedding!(model))));
            }

            o["embeddings"] = embeddings;
        }

        if (kind.OpsLoadEmbeddingsBulk is not null)
        {
            var models = new List<object>();
            foreach (var key in keys)
            {
                models.Add(await kind.OpsGetByUuid(d, run.Id(key)));
            }

            await kind.OpsLoadEmbeddingsBulk(d, models);
            var embeddings = new JsonArray();
            for (var i = 0; i < keys.Count; i++)
            {
                embeddings.Add(Keyed(keys[i], "embedding", Encode(kind.Embedding!(models[i]))));
            }

            o["embeddings_bulk"] = embeddings;
        }

        return o;
    }

    /// <summary>The models in the order of their uuids. Some queries have no ORDER BY (upstream defect U6).</summary>
    private static JsonArray Models(List<object> models, bool sort = false) =>
        new([.. (sort ? models.OrderBy(Uuid, StringComparer.Ordinal).ToList() : models).Select(m => (JsonNode?)EncodeModel(m))]);

    private static string Uuid(object model) => model is Node n ? n.Uuid : ((Edge)model).Uuid;

    private static async Task<JsonArray> RawAll(Run run, List<string> keys)
    {
        var list = new JsonArray();
        foreach (var key in keys)
        {
            list.Add(Keyed(key, "raw", await ReadRaw(run, key)));
        }

        return list;
    }

    private static async Task<JsonNode> Exists(Run run, IEnumerable<string> keys)
    {
        var list = new JsonArray();
        foreach (var key in keys)
        {
            list.Add(new JsonObject { ["key"] = key, ["exists"] = await ReadRaw(run, key) is not null });
        }

        return list;
    }

    private static async Task<JsonNode> Bulk(Run run, Dictionary<string, Kind> kinds, Dictionary<string, object> models, string[] kindNames)
    {
        var list = new JsonArray();
        foreach (var name in kindNames)
        {
            var items = KeysOf(run, name).Select(k => models[k]).ToList();
            var kind = kinds[name];
            if (kind.OpsSaveBulk is null)
            {
                // The operations class of CommunityEdge has no save_bulk. The rows then save one by one.
                foreach (var item in items)
                {
                    await kind.OpsSave(run.Driver, item);
                }

                list.Add(new JsonObject { ["kind"] = name, ["error"] = null, ["no_save_bulk"] = true });
                continue;
            }

            list.Add(Keyed(name, "error", await Attempt(() => kind.OpsSaveBulk(run.Driver, items)), "kind"));
        }

        return list;
    }

    private static async Task<JsonNode> DeleteByUuids(Run run, Dictionary<string, Kind> kinds, string[] kindNames)
    {
        var list = new JsonArray();
        foreach (var name in kindNames)
        {
            var uuids = KeysOf(run, name).Select(run.Id).ToList();
            list.Add(Keyed(name, "error", await Attempt(() => kinds[name].OpsDeleteByUuids(run.Driver, uuids)), "kind"));
        }

        return list;
    }

    private static JsonObject Keyed(string key, string field, JsonNode? value, string keyName = "key") =>
        new() { [keyName] = key, [field] = value };

    /// <summary>Runs a call and gives null if it ends, or the error in the shape that the recorder uses.</summary>
    private static async Task<JsonNode?> Attempt(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return ErrorOf(e);
        }

        return null;
    }

    private static JsonObject ErrorOf(Exception e) => e switch
    {
        // A database error is its code. The class names and the texts differ between the two drivers.
        Neo4jException { Code: { } code } => new JsonObject { ["code"] = code },
        NodeNotFoundException => new JsonObject { ["type"] = "NodeNotFoundError", ["text"] = e.Message },
        EdgeNotFoundException => new JsonObject { ["type"] = "EdgeNotFoundError", ["text"] = e.Message },

        // CommunityEdge.get_by_uuid reads edges[0] of an empty list. RULES.md R4 maps IndexError to this class.
        ArgumentOutOfRangeException => new JsonObject { ["type"] = "IndexError", ["text"] = "list index out of range" },
        _ => new JsonObject { ["type"] = e.GetType().Name, ["text"] = e.Message },
    };

    // ---------------------------------------------------------------------------------------------------------
    // The stored form: labels or type, and properties, read with plain Cypher
    // ---------------------------------------------------------------------------------------------------------

    private static async Task<JsonNode?> ReadRaw(Run run, string key)
    {
        var uuid = run.Id(key);
        if (NodeKinds.Contains(run.Kinds[key]))
        {
            var (records, _, _) = await run.Driver.ExecuteQueryAsync(
                "MATCH (n {uuid: $u}) RETURN labels(n) AS labels, properties(n) AS props", new() { ["u"] = uuid }, run.Ct);
            return records.Count == 0 ? null : RawShape(records[0]["labels"], records[0]["props"], null);
        }

        var (edges, _, _) = await run.Driver.ExecuteQueryAsync(
            "MATCH (a)-[e {uuid: $u}]->(b) RETURN type(e) AS t, properties(e) AS props, a.uuid AS s, b.uuid AS d",
            new() { ["u"] = uuid },
            run.Ct);
        return edges.Count == 0 ? null : RawShape(edges[0]["t"], edges[0]["props"], new JsonArray(Str(edges[0]["s"]), Str(edges[0]["d"])));
    }

    private static JsonObject RawShape(object? labelsOrType, object? props, JsonNode? ends)
    {
        // The bulk save of entity nodes stores a labels property, built from a set, so the order of its items is open.
        var dict = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in (OrderedDictionary<string, object?>)props!)
        {
            dict[key] = key == "labels" && value is List<object?> items ? Sorted(items) : value;
        }

        var raw = new JsonObject
        {
            ["props"] = Encode(dict),
            ["kind"] = Encode(labelsOrType is List<object?> labels ? Sorted(labels) : labelsOrType),
        };
        if (ends is not null)
        {
            raw["ends"] = ends;
        }

        return raw;
    }

    private static List<object?> Sorted(List<object?> items) => [.. items.OrderBy(i => (string)i!, StringComparer.Ordinal)];

    // ---------------------------------------------------------------------------------------------------------
    // The indexes
    // ---------------------------------------------------------------------------------------------------------

    private static readonly HashSet<string> SchemaNames =
        ["Entity", "Episodic", "Community", "Saga", "RELATES_TO", "MENTIONS", "HAS_MEMBER", "HAS_EPISODE", "NEXT_EPISODE"];

    /// <summary>The indexes on the labels and the types of the Graphiti schema, after the indexes come online.</summary>
    private static async Task<List<OrderedDictionary<string, object?>>> ReadIndexes(Run run)
    {
        await run.Driver.ExecuteQueryAsync("CALL db.awaitIndexes(120)", new(), run.Ct);
        var (records, _, _) = await run.Driver.ExecuteQueryAsync(
            "SHOW INDEXES YIELD name, type, entityType, labelsOrTypes, properties, state " +
            "RETURN name, type, entityType, labelsOrTypes, properties, state ORDER BY name",
            new(),
            run.Ct);
        return [.. records.Where(r => r["labelsOrTypes"] is List<object?> items && items.Any(i => SchemaNames.Contains((string)i!)))];
    }

    private static JsonArray Json(List<OrderedDictionary<string, object?>> indexes) => new JsonArray([.. indexes.Select(i => (JsonNode?)Json(i))]);

    private static JsonObject Json(OrderedDictionary<string, object?> index) => new()
    {
        ["name"] = Str(index["name"]),
        ["type"] = Str(index["type"]),
        ["entityType"] = Str(index["entityType"]),
        ["labelsOrTypes"] = new JsonArray([.. ((List<object?>)index["labelsOrTypes"]!).Select(i => (JsonNode?)Str(i))]),
        ["properties"] = new JsonArray([.. ((List<object?>)index["properties"]!).Select(i => (JsonNode?)Str(i))]),
        ["state"] = Str(index["state"]),
    };

    // ---------------------------------------------------------------------------------------------------------
    // Encoding of a C# value in the tagged form of the recorder: an int stays an int and a float stays a float
    // ---------------------------------------------------------------------------------------------------------

    private static JsonObject Tagged(string type, JsonNode? value = null)
    {
        var o = new JsonObject { ["type"] = type };
        if (value is not null)
        {
            o["value"] = value;
        }

        return o;
    }

    private static JsonObject Encode(object? value) => value switch
    {
        null => Tagged("none"),
        bool b => Tagged("bool", b),
        int i => Tagged("int", (long)i),
        long l => Tagged("int", l),
        double d => Tagged("float", d),
        string s => Tagged("str", s),
        PyDateTime dt => Tagged("datetime", dt.IsoFormat()),
        EpisodeType e => new JsonObject { ["type"] = "enum", ["class"] = "EpisodeType", ["value"] = (string)Py.EnumValue(e) },
        IDictionary map => EncodeDict(map),
        IEnumerable items => new JsonObject
        {
            ["type"] = "list",
            ["items"] = new JsonArray([.. items.Cast<object?>().Select(i => (JsonNode?)Encode(i))]),
        },
        _ => throw new NotSupportedException("The test cannot encode " + value.GetType().FullName),
    };

    private static JsonObject EncodeDict(IDictionary map)
    {
        var entries = new List<(string Key, object? Value)>();
        foreach (DictionaryEntry entry in map)
        {
            entries.Add(((string)entry.Key, entry.Value));
        }

        return new JsonObject
        {
            ["type"] = "dict",
            ["items"] = new JsonArray([.. entries
                .OrderBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => (JsonNode?)new JsonArray(e.Key, Encode(e.Value)))]),
        };
    }

    private static JsonObject EncodeModel(object model)
    {
        var fields = new JsonObject();
        switch (model)
        {
            case Node node:
                fields["uuid"] = Encode(node.Uuid);
                fields["name"] = Encode(node.Name);
                fields["group_id"] = Encode(node.GroupId);
                fields["labels"] = Encode(node.Labels.Order(StringComparer.Ordinal).ToList());
                fields["created_at"] = Encode(node.CreatedAt);
                break;
            case Edge edge:
                fields["uuid"] = Encode(edge.Uuid);
                fields["group_id"] = Encode(edge.GroupId);
                fields["source_node_uuid"] = Encode(edge.SourceNodeUuid);
                fields["target_node_uuid"] = Encode(edge.TargetNodeUuid);
                fields["created_at"] = Encode(edge.CreatedAt);
                break;
        }

        switch (model)
        {
            case EntityNode n:
                fields["name_embedding"] = Encode(n.NameEmbedding);
                fields["summary"] = Encode(n.Summary);
                fields["attributes"] = Encode(n.Attributes);
                break;
            case EpisodicNode n:
                fields["source"] = Encode(n.Source);
                fields["source_description"] = Encode(n.SourceDescription);
                fields["content"] = Encode(n.Content);
                fields["valid_at"] = Encode(n.ValidAt);
                fields["entity_edges"] = Encode(n.EntityEdges);
                fields["episode_metadata"] = Encode(n.EpisodeMetadata);
                break;
            case CommunityNode n:
                fields["name_embedding"] = Encode(n.NameEmbedding);
                fields["summary"] = Encode(n.Summary);
                break;
            case SagaNode n:
                fields["summary"] = Encode(n.Summary);
                fields["first_episode_uuid"] = Encode(n.FirstEpisodeUuid);
                fields["last_episode_uuid"] = Encode(n.LastEpisodeUuid);
                fields["last_summarized_at"] = Encode(n.LastSummarizedAt);
                fields["last_summarized_episode_valid_at"] = Encode(n.LastSummarizedEpisodeValidAt);
                fields["_summary_episodes_fetched"] = Encode(n.SummaryEpisodesFetched);
                fields["_summary_episodes_selected"] = Encode(n.SummaryEpisodesSelected);
                fields["_summary_episodes_skipped"] = Encode(n.SummaryEpisodesSkipped);
                fields["_summary_selected_episode_bytes"] = Encode(n.SummarySelectedEpisodeBytes);
                break;
            case EntityEdge e:
                fields["name"] = Encode(e.Name);
                fields["fact"] = Encode(e.Fact);
                fields["fact_embedding"] = Encode(e.FactEmbedding);
                fields["episodes"] = Encode(e.Episodes);
                fields["expired_at"] = Encode(e.ExpiredAt);
                fields["valid_at"] = Encode(e.ValidAt);
                fields["invalid_at"] = Encode(e.InvalidAt);
                fields["reference_time"] = Encode(e.ReferenceTime);
                fields["attributes"] = Encode(e.Attributes);
                break;
        }

        return new JsonObject { ["class"] = model.GetType().Name, ["fields"] = fields };
    }

    /// <summary>The value of a parameter in the tagged form, as a value that the port driver takes.</summary>
    private static object? Decode(JsonNode node)
    {
        var type = Str(node["type"]);
        return type switch
        {
            "none" => null,
            "bool" => node["value"]!.GetValue<bool>(),
            "int" => node["value"]!.GetValue<long>(),
            "float" => node["value"]!.GetValue<double>(),
            "str" => Str(node["value"]),
            "datetime" => PyDateTime.FromIsoFormat(Str(node["value"])),
            "list" => node["items"]!.AsArray().Select(i => Decode(i!)).ToList(),
            "dict" => DecodeDict(node["items"]!.AsArray()),
            _ => throw new NotSupportedException("The recording has the tag " + type),
        };
    }

    private static OrderedDictionary<string, object?> DecodeDict(JsonArray items)
    {
        var dict = new OrderedDictionary<string, object?>();
        foreach (var pair in items)
        {
            dict[Str(pair![0])] = Decode(pair[1]!);
        }

        return dict;
    }

    // ---------------------------------------------------------------------------------------------------------
    // The comparison: field by field, with the types
    // ---------------------------------------------------------------------------------------------------------

    private static string Str(object? value) => value switch
    {
        JsonNode node => node.GetValue<string>(),
        string s => s,
        _ => throw new InvalidCastException("A string is needed, not " + (value?.GetType().FullName ?? "null")),
    };

    private static bool IsTagged(JsonObject o) =>
        o["type"] is JsonValue v && v.TryGetValue<string>(out var type) && Tags.Contains(type);

    private static void AssertNoDifference(JsonNode expected, JsonNode actual, string what)
    {
        var sink = new List<string>();
        Diff(expected, actual, what, sink);
        Assert.True(
            sink.Count == 0,
            $"{sink.Count} difference(s) between Python and the port in {what}:\n" + string.Join('\n', sink.Take(40)));
    }

    private static string Show(JsonNode? node)
    {
        var text = node?.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "null";
        return text.Length > 160 ? text[..160] + "..." : text;
    }

    private static void Diff(JsonNode? expected, JsonNode? actual, string path, List<string> sink)
    {
        switch (expected)
        {
            case null:
                if (actual is not null)
                {
                    sink.Add($"{path}: Python has nothing, the port has {Show(actual)}");
                }

                return;

            case JsonObject eo when actual is JsonObject ao:
                if (IsTagged(eo) && IsTagged(ao))
                {
                    DiffTagged(eo, ao, path, sink);
                    return;
                }

                foreach (var key in eo.Select(p => p.Key).Union(ao.Select(p => p.Key)))
                {
                    var inE = eo.TryGetPropertyValue(key, out var e);
                    var inA = ao.TryGetPropertyValue(key, out var a);
                    if (inE && inA)
                    {
                        Diff(e, a, $"{path}.{key}", sink);
                    }
                    else
                    {
                        sink.Add($"{path}.{key}: {(inE ? "Python has it, the port has not" : "the port has it, Python has not")}");
                    }
                }

                return;

            case JsonArray ea when actual is JsonArray aa:
                if (ea.Count != aa.Count)
                {
                    sink.Add($"{path}: Python has {ea.Count} item(s), the port has {aa.Count}   [python {Show(ea)}  port {Show(aa)}]");
                    return;
                }

                for (var i = 0; i < ea.Count; i++)
                {
                    var label = ea[i] is JsonObject io && io["key"] is JsonValue k ? k.GetValue<string>() : i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    Diff(ea[i], aa[i], $"{path}[{label}]", sink);
                }

                return;

            case JsonValue ev when actual is JsonValue av:
                if (Scalar(ev) != Scalar(av))
                {
                    sink.Add($"{path}: Python {Scalar(ev)}, the port {Scalar(av)}");
                }

                return;

            default:
                sink.Add($"{path}: Python has {Show(expected)}, the port has {Show(actual)}");
                return;
        }
    }

    private static string Scalar(JsonValue v) =>
        v.TryGetValue<string>(out var s) ? "\"" + s + "\"" : v.TryGetValue<bool>(out var b) ? (b ? "true" : "false") : v.ToJsonString();

    private static void DiffTagged(JsonObject expected, JsonObject actual, string path, List<string> sink)
    {
        var type = Str(expected["type"]);
        if (type != Str(actual["type"]))
        {
            sink.Add($"{path}: Python has type {type}, the port has type {Str(actual["type"])}   [python {Show(expected)}  port {Show(actual)}]");
            return;
        }

        switch (type)
        {
            case "none":
                return;
            case "bool":
                if (expected["value"]!.GetValue<bool>() != actual["value"]!.GetValue<bool>())
                {
                    sink.Add($"{path}: Python {Show(expected)}, the port {Show(actual)}");
                }

                return;
            case "int":
                if (expected["value"]!.GetValue<long>() != actual["value"]!.GetValue<long>())
                {
                    sink.Add($"{path}: Python {Show(expected)}, the port {Show(actual)}");
                }

                return;
            case "float":
                if (BitConverter.DoubleToInt64Bits(expected["value"]!.GetValue<double>()) != BitConverter.DoubleToInt64Bits(actual["value"]!.GetValue<double>()))
                {
                    sink.Add($"{path}: Python {Show(expected)}, the port {Show(actual)}");
                }

                return;
            case "str":
            case "datetime":
                if (Str(expected["value"]) != Str(actual["value"]))
                {
                    sink.Add($"{path}: Python {Show(expected)}, the port {Show(actual)}");
                }

                return;
            case "enum":
                if (Str(expected["class"]) != Str(actual["class"]) || Str(expected["value"]) != Str(actual["value"]))
                {
                    sink.Add($"{path}: Python {Show(expected)}, the port {Show(actual)}");
                }

                return;
            case "list":
                Diff(expected["items"], actual["items"], path, sink);
                return;
            case "dict":
                {
                    var e = expected["items"]!.AsArray().ToDictionary(p => Str(p![0]), p => p![1]);
                    var a = actual["items"]!.AsArray().ToDictionary(p => Str(p![0]), p => p![1]);
                    foreach (var key in e.Keys.Union(a.Keys))
                    {
                        if (e.TryGetValue(key, out var ev) && a.TryGetValue(key, out var av))
                        {
                            Diff(ev, av, $"{path}.{key}", sink);
                        }
                        else
                        {
                            sink.Add($"{path}.{key}: {(e.ContainsKey(key) ? "Python has the key, the port has not" : "the port has the key, Python has not")}");
                        }
                    }

                    return;
                }
        }
    }
}
