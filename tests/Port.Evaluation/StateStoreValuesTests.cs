// Evaluation test for graphiti_core/ingestion/state_store.py and graphiti_core/ingestion/sqlite_state_store.py
// (port/RULES.md, R23 and R25, gap J7). values/state_store.json holds what port/tools/record_state_store.py recorded
// from Python: the constants, is_released and is_stale_heartbeat, sequences of store calls with a fake clock (the
// result of each call, the state of the in-memory store, and the rows of the SQLite file at the end), one SQLite
// file that Python wrote, and the busy timeout and the journal mode of the connection of a store. Each sequence runs on the in-memory store, and on the SQLite store with a temp file and with
// ':memory:'. The schema of the SQLite store is a stored data shape, so the test reads the file of Python with the
// port, and compares the rows and the sqlite_master text of a file that the port wrote with those of Python.
namespace Graphiti.Port.Evaluation;

using Graphiti.Core.Ingestion;
using Graphiti.PyCompat;
using Microsoft.Data.Sqlite;

public sealed class StateStoreValuesTests : IDisposable
{
    private static readonly List<OrderedDictionary<string, object?>> Cases = Load();

    private readonly string folder = Path.Combine(Path.GetTempPath(), "graphiti-state-store-" + Guid.NewGuid().ToString("N"));

    public StateStoreValuesTests() => Directory.CreateDirectory(folder);

    public static TheoryData<int> Sequences() => [.. Cases.Index().Where(c => (string?)c.Item["f"] == "sequence").Select(c => c.Index)];

    public void Dispose()
    {
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void Each_constant_has_the_value_of_python()
    {
        var constants = Cases.Where(c => (string?)c["f"] == "constant").ToList();
        Assert.Equal(14, constants.Count);
        foreach (var recorded in constants)
        {
            var name = Py.AsString(Py.AsList(recorded["a"])[1]);
            object? actual = name switch
            {
                "STATUS_ADMITTING" => StateStore.StatusAdmitting,
                "STATUS_ADMITTED" => StateStore.StatusAdmitted,
                "STATUS_ANALYZED" => StateStore.StatusAnalyzed,
                "STATUS_APPLYING" => StateStore.StatusApplying,
                "STATUS_COMMITTED" => StateStore.StatusCommitted,
                "STATUS_FAILED" => StateStore.StatusFailed,
                "STATUS_APPLY_FAILED" => StateStore.StatusApplyFailed,
                "REASON_APPLY_FAILED" => StateStore.ReasonApplyFailed,
                "REASON_EXPIRED" => StateStore.ReasonExpired,
                "TERMINAL_STATUSES" => StateStore.TerminalStatuses.Order(Py.StringComparer).ToList(),
                "FAILED_STATUSES" => StateStore.FailedStatuses.Order(Py.StringComparer).ToList(),
                "BUSY_TIMEOUT_SECONDS" => SqliteStateStore.BusyTimeoutSeconds,
                "_SCHEMA" => SqliteStateStore.Schema,
                "_TERMINAL_SQL" => SqliteStateStore.TerminalSql,
                _ => throw new InvalidOperationException(name),
            };
            Assert.Equal(PyJson.Dumps(recorded["r"]), PyJson.Dumps(Pydantic.Dump(actual)));
        }
    }

    [Fact]
    public void Is_released_and_is_stale_heartbeat_give_the_results_of_python()
    {
        var cases = Cases.Where(c => (string?)c["f"] is "is_released" or "is_stale_heartbeat").ToList();
        Assert.Equal(105, cases.Count);
        foreach (var recorded in cases)
        {
            var a = Py.AsList(recorded["a"]);
            var actual = (string?)recorded["f"] == "is_released"
                ? StateStore.IsReleased(Py.AsString(a[0]), Py.AsDouble(a[1]), Py.AsDouble(a[2]))
                : StateStore.IsStaleHeartbeat(Py.AsString(a[0]), Py.AsLong(a[1]), a[2] is null ? null : Py.AsLong(a[2]));
            Assert.True(Py.AsBool(recorded["r"]) == actual, $"{recorded["f"]}({PyJson.Dumps(a)})");
        }
    }

    [Theory(Timeout = 20000)]
    [MemberData(nameof(Sequences))]
    public async Task The_in_memory_store_gives_each_recorded_result_and_state(int index)
    {
        var recorded = Py.AsDict(Cases[index]["r"]);
        var steps = Py.AsList(Py.AsList(Cases[index]["a"])[0]);
        var clock = new FakeClock();
        var store = new InMemoryIngestionStateStore(clock.Now);
        var results = await LoopGuard.RunAsync(() => Run(store, clock, steps, TestContext.Current.CancellationToken));
        Same(recorded["memory"], results, "the results");
        Same(recorded["memory_state"], MemoryState(store), "the state of _groups");
    }

    [Theory(Timeout = 20000)]
    [MemberData(nameof(Sequences))]
    public async Task The_sqlite_store_gives_each_recorded_result_and_row_on_a_file(int index)
    {
        var recorded = Py.AsDict(Cases[index]["r"]);
        var steps = Py.AsList(Py.AsList(Cases[index]["a"])[0]);
        var path = Path.Combine(folder, "run.db");
        var clock = new FakeClock();
        var store = new SQLiteIngestionStateStore(path, clock.Now);
        var results = await LoopGuard.RunAsync(() => Run(store, clock, steps, TestContext.Current.CancellationToken));
        await store.CloseAsync(TestContext.Current.CancellationToken);
        Same(recorded["sqlite"], results, "the results");
        Same(recorded["sqlite_rows"], TableRows(path), "the rows of the file");
    }

    [Theory(Timeout = 20000)]
    [MemberData(nameof(Sequences))]
    public async Task The_sqlite_store_gives_each_recorded_result_on_memory(int index)
    {
        var recorded = Py.AsDict(Cases[index]["r"]);
        var steps = Py.AsList(Py.AsList(Cases[index]["a"])[0]);
        var clock = new FakeClock();
        var store = new SQLiteIngestionStateStore(":memory:", clock.Now);
        var results = await LoopGuard.RunAsync(() => Run(store, clock, steps, TestContext.Current.CancellationToken));
        await store.CloseAsync(TestContext.Current.CancellationToken);
        Same(recorded["sqlite"], results, "the results");
    }

    [Fact(Timeout = 20000)]
    public async Task The_port_reads_a_file_that_python_wrote_and_writes_the_same_rows()
    {
        var recorded = Cases.Single(c => (string?)c["f"] == "python_file");
        var result = Py.AsDict(recorded["r"]);
        var steps = Py.AsList(Py.AsList(recorded["a"])[0]);
        var rows = Py.AsDict(result["rows"]);

        // The file of Python opens in the port, and each table holds the rows that Python reads.
        var pythonFile = Path.Combine(folder, "python.db");
        await File.WriteAllBytesAsync(pythonFile, Convert.FromHexString(Py.AsString(result["file"])), TestContext.Current.CancellationToken);
        var clock = new FakeClock();
        var opened = new SQLiteIngestionStateStore(pythonFile, clock.Now);
        var delta = await LoopGuard.RunAsync(() => opened.LoadDeltaAsync("g1", 0, 10, 0.0, TestContext.Current.CancellationToken));
        Assert.Equal(["f1"], delta.Select(unit => Convert.ToHexStringLower(unit.Payload)));
        var admission = await LoopGuard.RunAsync(() => opened.BeginAdmitAsync("g1", "u1", 60.0, TestContext.Current.CancellationToken));
        Assert.Equal((1L, true), (admission?.Seq, admission?.Existing));
        await opened.CloseAsync(TestContext.Current.CancellationToken);
        Same(rows, TableRows(pythonFile), "the rows of the file of Python");

        // A file that the port writes with the same calls holds the rows and the schema text of the file of Python.
        var portFile = Path.Combine(folder, "port.db");
        clock = new FakeClock();
        var store = new SQLiteIngestionStateStore(portFile, clock.Now);
        var results = await LoopGuard.RunAsync(() => Run(store, clock, steps, TestContext.Current.CancellationToken));
        await store.CloseAsync(TestContext.Current.CancellationToken);
        Same(result["results"], results, "the results");
        Same(rows, TableRows(portFile), "the rows of the file of the port");
    }

    [Theory(Timeout = 20000)]
    [InlineData("file")]
    [InlineData("memory")]
    public async Task The_connection_of_the_store_has_the_busy_timeout_and_the_journal_mode_of_python(string kind)
    {
        var recorded = Cases.Single(c => (string?)c["f"] == "pragmas" && Py.AsString(Py.AsList(c["a"])[0]) == kind);
        var store = new SQLiteIngestionStateStore(kind == "file" ? Path.Combine(folder, "pragmas.db") : ":memory:", new FakeClock().Now);
        try
        {
            // The connection is private. The test reads it, because a pragma has no other way out.
            var conn = (SqliteConnection)typeof(SQLiteIngestionStateStore)
                .GetField("conn", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .GetValue(store)!;
            object? Pragma(string name)
            {
                using var command = conn.CreateCommand();
                command.CommandText = "PRAGMA " + name;
                return Encode(command.ExecuteScalar());
            }

            Same(
                recorded["r"],
                new OrderedDictionary<string, object?> { ["busy_timeout"] = Pragma("busy_timeout"), ["journal_mode"] = Pragma("journal_mode") },
                "the pragmas");
        }
        finally
        {
            await store.CloseAsync(TestContext.Current.CancellationToken);
        }
    }

    // The calls of one recorded sequence, with the result or the exception of each call as Python encodes it.
    internal static async Task<List<object?>> Run(IngestionStateStore store, FakeClock clock, List<object?> steps, CancellationToken token)
    {
        List<object?> results = [];
        foreach (var item in steps)
        {
            var step = Py.AsDict(item);
            var m = Py.AsString(step["m"]);
            if (m == "advance")
            {
                clock.Value += Py.AsDouble(step["seconds"]);
                results.Add(null);
                continue;
            }

            string G() => Py.AsString(step["group_id"]);
            long Seq() => Py.AsLong(step["seq"]);
            long? Fence() => step.GetValueOrDefault("fence") is { } fence ? Py.AsLong(fence) : null;
            List<string> Uuids() => [.. Py.AsList(step["node_uuids"]).Select(Py.AsString)];
            byte[] Payload() => Convert.FromHexString(Py.AsString(step["payload"]));
            try
            {
                object? value = m switch
                {
                    "begin_admit" => await store.BeginAdmitAsync(G(), Py.AsString(step["unit_id"]), Py.AsDouble(step["ttl"]), token),
                    "end_admit" => await Done(store.EndAdmitAsync(G(), Seq(), token)),
                    "start_analysis" => await store.StartAnalysisAsync(G(), Seq(), token),
                    "save_change_set" => await Done(store.SaveChangeSetAsync(G(), Seq(), Payload(), token)),
                    "heartbeat" => await Done(store.HeartbeatAsync(G(), Seq(), Py.AsDouble(step["ttl"]), Fence(), token)),
                    "begin_commit" => await store.BeginCommitAsync(G(), Seq(), Py.AsDouble(step["ttl"]), token),
                    "save_final" => await Done(store.SaveFinalAsync(G(), Seq(), Py.AsLong(step["fence"]), Payload(), token)),
                    "finish_commit" => await Done(store.FinishCommitAsync(G(), Seq(), Py.AsLong(step["fence"]), Uuids(), Py.AsDouble(step["retain_seconds"]), token)),
                    "fail" => await Done(store.FailAsync(G(), Seq(), Py.AsString(step["reason"]), Fence(), token)),
                    "ready_enrich" => await store.ReadyEnrichAsync(G(), Seq(), token),
                    "finish_enrich" => await Done(store.FinishEnrichAsync(G(), Seq(), Uuids(), token)),
                    "load_delta" => await store.LoadDeltaAsync(G(), Py.AsLong(step["after_seq"]), Py.AsLong(step["before_seq"]), Py.AsDouble(step["since"]), token),
                    _ => throw new InvalidOperationException(m),
                };
                results.Add(new OrderedDictionary<string, object?> { ["ok"] = Encode(value) });
            }
            catch (Exception error) when (error is UnitExpiredException or StaleCommitException or ArgumentException)
            {
                var name = error switch
                {
                    UnitExpiredException => "UnitExpiredError",
                    StaleCommitException => "StaleCommitError",
                    _ => "ValueError",
                };
                results.Add(new OrderedDictionary<string, object?> { ["err"] = new List<object?> { name, error.Message } });
            }
        }

        return results;
    }

    internal static object? Encode(object? value) => value switch
    {
        null or bool or long or double or string => value,
        byte[] bytes => new OrderedDictionary<string, object?> { ["$bytes"] = Convert.ToHexStringLower(bytes) },
        List<string> list => list.Cast<object?>().ToList(),
        List<DeltaUnit> list => list.Select(Encode).ToList(),
        Admission a => new OrderedDictionary<string, object?> { ["$type"] = "Admission", ["seq"] = a.Seq, ["existing"] = a.Existing, ["previous_seq"] = a.PreviousSeq },
        AnalysisStart a => new OrderedDictionary<string, object?> { ["$type"] = "AnalysisStart", ["snapshot_seq"] = a.SnapshotSeq, ["started_at"] = a.StartedAt },
        CommitTurn t => new OrderedDictionary<string, object?> { ["$type"] = "CommitTurn", ["fence"] = t.Fence, ["final_payload"] = Encode(t.FinalPayload) },
        CommitWait => new OrderedDictionary<string, object?> { ["$type"] = "CommitWait" },
        CommitRecover r => new OrderedDictionary<string, object?> { ["$type"] = "CommitRecover", ["seq"] = r.Seq, ["fence"] = r.Fence, ["final_payload"] = Encode(r.FinalPayload) },
        DeltaUnit d => new OrderedDictionary<string, object?> { ["$type"] = "DeltaUnit", ["seq"] = d.Seq, ["status"] = d.Status, ["payload"] = Encode(d.Payload) },
        _ => throw new InvalidOperationException($"No encoding for {value.GetType().Name}"),
    };

    // The rows of each table and the sqlite_master text, as the recorder reads them with sqlite3.
    internal static OrderedDictionary<string, object?> TableRows(string path)
    {
        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        conn.Open();
        var tables = new OrderedDictionary<string, object?>();
        foreach (var (table, order) in new[] { ("graphiti_ingest_group", "group_id"), ("graphiti_ingest_unit", "group_id, seq"), ("graphiti_ingest_enrich", "rowid") })
        {
            tables[table] = Query(conn, $"SELECT * FROM {table} ORDER BY {order}").Cast<object?>().ToList();
        }

        tables["sqlite_master"] = Query(conn, "SELECT type, name, tbl_name, sql FROM sqlite_master ORDER BY name")
            .Select(row => (object?)row.Values.ToList()).ToList();
        return tables;
    }

    private static List<OrderedDictionary<string, object?>> Query(SqliteConnection conn, string sql)
    {
        using var command = conn.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        List<OrderedDictionary<string, object?>> rows = [];
        while (reader.Read())
        {
            var row = new OrderedDictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : Encode(reader.GetValue(i));
            }

            rows.Add(row);
        }

        return rows;
    }

    private static OrderedDictionary<string, object?> MemoryState(InMemoryIngestionStateStore store)
    {
        var groups = new OrderedDictionary<string, object?>();
        foreach (var (groupId, group) in store.groups.OrderBy(kv => kv.Key, Py.StringComparer))
        {
            var unitIds = new OrderedDictionary<string, object?>();
            foreach (var (unitId, seq) in group.UnitIds.OrderBy(kv => kv.Key, Py.StringComparer))
            {
                unitIds[unitId] = seq;
            }

            groups[groupId] = new OrderedDictionary<string, object?>
            {
                ["next_seq"] = group.NextSeq,
                ["watermark"] = group.Watermark,
                ["admit_seq"] = group.AdmitSeq,
                ["units"] = group.Units.Values.Select(u => (object?)new OrderedDictionary<string, object?>
                {
                    ["seq"] = u.Seq,
                    ["unit_id"] = u.UnitId,
                    ["status"] = u.Status,
                    ["deadline"] = u.Deadline,
                    ["snapshot_seq"] = u.SnapshotSeq,
                    ["started_at"] = u.StartedAt,
                    ["ended_at"] = u.EndedAt,
                    ["fence"] = u.Fence,
                    ["fail_reason"] = u.FailReason,
                    ["payload"] = Encode(u.Payload),
                }).ToList(),
                ["unit_ids"] = unitIds,
                ["enrich"] = group.Enrich.Select(kv => (object?)new List<object?> { kv.Key, kv.Value.Cast<object?>().ToList() }).ToList(),
            };
        }

        return groups;
    }

    private static async Task<object?> Done(Task task)
    {
        await task;
        return null;
    }

    private static void Same(object? expected, object? actual, string what)
    {
        var want = PyJson.Dumps(expected);
        var got = PyJson.Dumps(actual);
        if (want == got)
        {
            return;
        }

        var at = 0;
        while (at < want.Length && at < got.Length && want[at] == got[at])
        {
            at++;
        }

        Assert.Fail($"{what} differ at character {at}: expected ...{want[Math.Max(0, at - 80)..Math.Min(want.Length, at + 80)]} but got ...{got[Math.Max(0, at - 80)..Math.Min(got.Length, at + 80)]}");
    }

    private static List<OrderedDictionary<string, object?>> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", "state_store.json");
        var document = Py.AsDict(PyJson.Loads(File.ReadAllText(path)));
        return [.. Py.AsList(document["cases"]).Select(Py.AsDict)];
    }

    // The fake clock of the recorder: it starts at 1000.0, and a step moves it.
    internal sealed class FakeClock
    {
        internal double Value { get; set; } = 1_000.0;

        internal double Now() => Value;
    }
}
