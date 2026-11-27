// tests/ingestion/test_change_set.py (port/RULES.md, R25). Each test keeps its Python name.
// `==` on two operation models compares the fields in Python. A C# record compares a list or a dict member by
// reference (R7), so the tests compare the model_dump of each side.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.PyCompat;

public class TestChangeSet
{
    private static readonly PyDateTime T0 = PyDateTime.FromIsoFormat("2026-01-01T00:00:00+00:00");

    [Fact]
    public void test_change_set_round_trip_keeps_embeddings_and_datetimes()
    {
        var episode = new EpisodicNode
        {
            Uuid = "ep1",
            Name = "episode",
            GroupId = "g",
            Source = EpisodeType.Text,
            SourceDescription = "test",
            Content = "Ana works at Acme",
            ValidAt = T0,
            CreatedAt = T0,
            EntityEdges = ["e1"],
        }.Validate();
        var node = new EntityNode
        {
            Uuid = "n1",
            Name = "Ana",
            GroupId = "g",
            Labels = ["Entity", "Person"],
            CreatedAt = T0,
            NameEmbedding = [0.1, 0.2, 0.3],
            Attributes = new() { ["email"] = "ana@example.com" },
        }.Validate();
        var edge = MakeEdge(e =>
        {
            e.FactEmbedding = [0.4, 0.5];
            e.ValidAt = T0;
            e.Attributes = new() { ["role"] = "engineer" };
        });
        var changeSet = new ChangeSet
        {
            GroupId = "g",
            UnitId = "u1",
            Seq = 3,
            SnapshotSeq = 1,
            AnalysisStartedAt = 12.5,
            Episodes = [episode],
            CreateNodes = [node],
            CreateEdges = [edge],
            EpisodicEdges =
            [
                new EpisodicEdge { SourceNodeUuid = "ep1", TargetNodeUuid = "n1", GroupId = "g", CreatedAt = T0 }.Validate(),
            ],
            Operations =
            [
                new AddEpisodesToEdge { EdgeUuid = "e9", EpisodeUuids = ["ep1"] }.Validate(),
                new EndEdge { EdgeUuid = "e8", InvalidAt = T0, ExpiredAt = Days(1) }.Validate(),
                new SetEdgeAttributes { EdgeUuid = "e7", BaseAttributes = new() { ["a"] = 1L }, Attributes = new() { ["a"] = 2L } }.Validate(),
                new PatchNode { NodeUuid = "n9", Labels = ["Entity", "Person"], Attributes = new() { ["email"] = "x" } }.Validate(),
            ],
            NodeUuids = ["n1", "n9"],
            NodeFirstEpisode = new() { ["n1"] = "ep1", ["n9"] = "ep1" },
            SeenNodeCandidates = new() { ["n1"] = ["n9"] },
            SeenDuplicateCandidates = new() { ["e1"] = ["e9"] },
            SeenInvalidationCandidates = new() { ["e1"] = ["e8"] },
            UuidMap = new() { ["x"] = "n9" },
            SagaLinks =
            [
                new SagaLink { SagaUuid = "s1", EpisodeUuid = "ep1", HasEpisodeEdgeUuid = "h1", CreatedAt = T0 }.Validate(),
            ],
        }.Validate();

        var payload = changeSet.ToBytes();
        var restored = ChangeSet.FromBytes(payload);

        Assert.IsType<byte[]>(payload);

        // `Edge.__eq__` does not compare edges, so the test compares the dumps.
        Assert.Equal(PyJson.Dumps(changeSet.ModelDump(json: true)), PyJson.Dumps(restored.ModelDump(json: true)));
        Assert.Equal([0.1, 0.2, 0.3], restored.CreateNodes[0].NameEmbedding);
        Assert.Equal([0.4, 0.5], restored.CreateEdges[0].FactEmbedding);
        Assert.Equal(T0, restored.CreateEdges[0].ValidAt);
        Assert.Equal(
            [typeof(AddEpisodesToEdge), typeof(EndEdge), typeof(SetEdgeAttributes), typeof(PatchNode)],
            restored.Operations.Select(operation => operation.GetType()));
        Assert.Equal(["n9"], restored.PatchesByNode().Keys);
        Assert.Equal(3, restored.EdgeOperations().Count);
    }

    [Fact]
    public void test_add_episodes_keeps_order_and_adds_no_duplicate()
    {
        var edge = MakeEdge(e => e.Episodes = ["ep0", "ep1"]);
        Assert.Equal(
            (true, false),
            ApplyTwice(edge, new AddEpisodesToEdge { EdgeUuid = "e1", EpisodeUuids = ["ep1", "ep2"] }.Validate()));
        Assert.Equal(["ep0", "ep1", "ep2"], edge.Episodes);
    }

    [Fact]
    public void test_end_edge_sets_dates_on_open_edge()
    {
        var edge = MakeEdge(_ => { });
        var operation = new EndEdge { EdgeUuid = "e1", InvalidAt = T0, ExpiredAt = Days(2) }.Validate();
        Assert.Equal((true, false), ApplyTwice(edge, operation));
        Assert.Equal((T0, Days(2)), (edge.InvalidAt, edge.ExpiredAt));
    }

    [Fact]
    public void test_end_edge_keeps_earlier_end_date()
    {
        var edge = MakeEdge(e =>
        {
            e.InvalidAt = T0;
            e.ExpiredAt = Days(1);
        });
        var later = new EndEdge { EdgeUuid = "e1", InvalidAt = Days(5), ExpiredAt = Days(6) }.Validate();
        Assert.Equal((false, false), ApplyTwice(edge, later));
        Assert.Equal((T0, Days(1)), (edge.InvalidAt, edge.ExpiredAt));
    }

    [Fact]
    public void test_end_edge_replaces_later_end_date()
    {
        var edge = MakeEdge(e =>
        {
            e.InvalidAt = Days(5);
            e.ExpiredAt = Days(6);
        });
        var earlier = new EndEdge { EdgeUuid = "e1", InvalidAt = T0, ExpiredAt = Days(9) }.Validate();
        Assert.Equal((true, false), ApplyTwice(edge, earlier));

        // `expired_at` has a value, so it stays.
        Assert.Equal((T0, Days(6)), (edge.InvalidAt, edge.ExpiredAt));
    }

    [Fact]
    public void test_end_edge_only_if_open_does_nothing_on_expired_edge()
    {
        var edge = MakeEdge(e => e.ExpiredAt = Days(1));
        var operation = new EndEdge { EdgeUuid = "e1", InvalidAt = T0, ExpiredAt = Days(2), OnlyIfOpen = true }.Validate();
        Assert.Equal((false, false), ApplyTwice(edge, operation));
        Assert.Null(edge.InvalidAt);
    }

    [Fact]
    public void test_end_edge_with_no_invalid_at_sets_expired_at_only()
    {
        var edge = MakeEdge(e => e.InvalidAt = T0);
        var operation = new EndEdge { EdgeUuid = "e1", ExpiredAt = Days(2), OnlyIfOpen = true }.Validate();
        Assert.Equal((true, false), ApplyTwice(edge, operation));
        Assert.Equal((T0, Days(2)), (edge.InvalidAt, edge.ExpiredAt));
    }

    [Fact]
    public void test_merge_edge_attributes_by_key()
    {
        var @base = Dict(("a", 1L), ("b", 1L), ("d", 1L));
        var @new = Dict(("a", 2L), ("b", 2L), ("c", 2L));

        // A different writer changed `a` and added `e` after the analysis read the edge.
        var current = Dict(("a", 9L), ("b", 1L), ("d", 1L), ("e", 9L));
        var merged = ChangeSetModule.MergeEdgeAttributes(@base, @new, current);

        // `a`: changed by the other writer, stays. `b`: not changed, gets the new value.
        // `c`: new key. `d`: removed by the unit. `e`: added by the other writer, stays.
        Assert.True(PyEqualityComparer<object?>.Instance.Equals(Dict(("a", 9L), ("b", 2L), ("c", 2L), ("e", 9L)), merged));
    }

    [Fact]
    public void test_merge_edge_attributes_is_a_replace_when_no_writer_changed_the_edge()
    {
        var @base = Dict(("a", 1L), ("b", 1L));
        var @new = Dict(("a", 2L), ("c", 3L));
        Assert.True(PyEqualityComparer<object?>.Instance.Equals(@new, ChangeSetModule.MergeEdgeAttributes(@base, @new, new(@base))));
    }

    [Fact]
    public void test_set_edge_attributes_two_times_gives_same_record()
    {
        var edge = MakeEdge(e => e.Attributes = Dict(("a", 9L), ("b", 1L)));
        var operation = new SetEdgeAttributes
        {
            EdgeUuid = "e1",
            BaseAttributes = Dict(("a", 1L), ("b", 1L)),
            Attributes = Dict(("a", 2L), ("b", 2L)),
        }.Validate();
        Assert.Equal((true, false), ApplyTwice(edge, operation));
        Assert.True(PyEqualityComparer<object?>.Instance.Equals(Dict(("a", 9L), ("b", 2L)), edge.Attributes));
    }

    [Fact]
    public void test_trace_makes_operations_from_changes_to_stored_edges()
    {
        var duplicate = MakeEdge(e =>
        {
            e.Uuid = "dup";
            e.Attributes = Dict(("role", "old"));
        });
        var contradicted = MakeEdge(e =>
        {
            e.Uuid = "old";
            e.ValidAt = T0;
        });
        var resolvedStored = MakeEdge(e =>
        {
            e.Uuid = "res";
            e.InvalidAt = Days(3);
        });
        var untouched = MakeEdge(e => e.Uuid = "same");
        var trace = new ResolutionTrace();
        trace.WatchStoredEdges([duplicate, contradicted, resolvedStored, untouched]);

        duplicate.Episodes.Add("ep1");
        duplicate.Attributes = Dict(("role", "new"));
        contradicted.InvalidAt = Days(1);
        contradicted.ExpiredAt = Days(9);
        trace.MarkEndedAsCandidate([contradicted]);
        resolvedStored.ExpiredAt = Days(9);

        var operations = trace.CollectEdgeOperations();

        Assert.Equal(
            Dumps(
            [
                new AddEpisodesToEdge { EdgeUuid = "dup", EpisodeUuids = ["ep1"] },
                new SetEdgeAttributes { EdgeUuid = "dup", BaseAttributes = Dict(("role", "old")), Attributes = Dict(("role", "new")) },
                new EndEdge { EdgeUuid = "old", InvalidAt = Days(1), ExpiredAt = Days(9), OnlyIfOpen = false },
                new EndEdge { EdgeUuid = "res", InvalidAt = null, ExpiredAt = Days(9), OnlyIfOpen = true },
            ]),
            Dumps([.. operations]));
        Assert.Equal(Dumps([.. operations]), Dumps(trace.Operations));
        Assert.Empty(trace.CollectEdgeOperations());
    }

    [Fact]
    public void test_trace_records_node_resolution()
    {
        var extracted = new EntityNode { Uuid = "new", Name = "Ana", GroupId = "g", Labels = ["Entity", "Person"], Attributes = Dict(("k", "v")) }.Validate();
        var stored = new EntityNode { Uuid = "stored", Name = "Ana", GroupId = "g", Labels = ["Entity"] }.Validate();
        var trace = new ResolutionTrace();
        trace.RecordNodeCandidates("new", [stored]);
        trace.RecordNodeResolution(extracted, "new");
        Assert.Empty(trace.Operations);
        trace.RecordNodeResolution(extracted, "stored");
        Assert.Equal(["new"], trace.SeenNodeCandidates.Keys);
        Assert.Equal(["stored"], trace.SeenNodeCandidates["new"]);
        Assert.Equal(["stored"], trace.StoredNodeUuids);
        Assert.Equal(
            Dumps([new PatchNode { NodeUuid = "stored", Labels = ["Entity", "Person"], Attributes = Dict(("k", "v")) }]),
            Dumps(trace.Operations));
    }

    [Fact]
    public void test_config_defaults_and_validation()
    {
        var config = new ConcurrentIngestionConfig { Window = 4 }.Validate();
        Assert.Equal((60.0, 5.0), (config.LeaseSeconds, config.WriteMarginSeconds));
        Assert.Equal((0.25, 300.0), (config.PollIntervalSeconds, config.IndexLagSeconds));
        Assert.Equal(5, config.MaxApplyAttempts);
        var store = config.GetStateStore();
        Assert.IsType<InMemoryIngestionStateStore>(store);
        Assert.Same(store, config.GetStateStore());

        Assert.Throws<ValidationException>(() => ConcurrentIngestionConfig.ModelValidate(new()));
        Assert.Throws<ValidationException>(() => ConcurrentIngestionConfig.ModelValidate(new() { ["window"] = 0L }));
        Assert.Throws<ValidationException>(() => ConcurrentIngestionConfig.ModelValidate(
            new() { ["window"] = 1L, ["lease_seconds"] = 15.0, ["write_margin_seconds"] = 5.0 }));
        Assert.Throws<ValidationException>(() => ConcurrentIngestionConfig.ModelValidate(
            new() { ["window"] = 1L, ["state_store"] = new object() }));
    }

    private static PyDateTime Days(int days) => PyDateTime.FromIsoFormat($"2026-01-{1 + days:D2}T00:00:00+00:00");

    private static EntityEdge MakeEdge(Action<EntityEdge> values)
    {
        var edge = new EntityEdge
        {
            Uuid = "e1",
            GroupId = "g",
            SourceNodeUuid = "a",
            TargetNodeUuid = "b",
            Name = "WORKS_AT",
            Fact = "Ana works at Acme",
            CreatedAt = T0,
            Episodes = ["ep0"],
        };
        values(edge);
        return edge.Validate();
    }

    private static (bool First, bool Second) ApplyTwice(EntityEdge edge, EdgeOperation operation)
    {
        var first = ChangeSetModule.ApplyEdgeOperation(edge, operation);
        var state = PyJson.Dumps(edge.ModelDump(json: true));
        var second = ChangeSetModule.ApplyEdgeOperation(edge, operation);
        Assert.Equal(state, PyJson.Dumps(edge.ModelDump(json: true)));
        return (first, second);
    }

    private static OrderedDictionary<string, object?> Dict(params (string Key, object? Value)[] items)
    {
        var result = new OrderedDictionary<string, object?>();
        foreach (var (key, value) in items)
        {
            result[key] = value;
        }

        return result;
    }

    private static string Dumps(List<Operation> operations) =>
        PyJson.Dumps(operations.Select(operation => (object?)((IPydanticModel)operation).ModelDump(json: true)).ToList());
}
