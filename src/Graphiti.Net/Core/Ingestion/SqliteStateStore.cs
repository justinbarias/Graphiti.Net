// Ported from graphiti_core/ingestion/sqlite_state_store.py
/*
Copyright 2024, Zep Software, Inc.

Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
*/

namespace Graphiti.Core.Ingestion;

using Graphiti.PyCompat;
using Microsoft.Data.Sqlite;
using SQLitePCL;

// PORT: `T = TypeVar('T')` is the type parameter of RunSync and RunAsync.
internal static class SqliteStateStore
{
    internal const double BusyTimeoutSeconds = 5.0;

    // PORT: internal, because SQLiteIngestionStateStore reads it (R2). The text follows the string rules of R9.
    internal const string Schema = "\n" + """
CREATE TABLE IF NOT EXISTS graphiti_ingest_group (
  group_id      TEXT PRIMARY KEY,
  next_seq      INTEGER NOT NULL,
  watermark     INTEGER NOT NULL,
  admit_seq     INTEGER
);
CREATE TABLE IF NOT EXISTS graphiti_ingest_unit (
  group_id      TEXT NOT NULL,
  seq           INTEGER NOT NULL,
  unit_id       TEXT NOT NULL,
  status        TEXT NOT NULL,
  snapshot_seq  INTEGER,
  started_at    REAL,
  ended_at      REAL,
  fence         INTEGER NOT NULL DEFAULT 0,
  deadline      REAL NOT NULL,
  fail_reason   TEXT,
  payload       BLOB,
  PRIMARY KEY (group_id, seq)
);
CREATE INDEX IF NOT EXISTS graphiti_ingest_unit_by_unit_id
  ON graphiti_ingest_unit (group_id, unit_id, seq);
CREATE TABLE IF NOT EXISTS graphiti_ingest_enrich (
  group_id      TEXT NOT NULL,
  node_uuid     TEXT NOT NULL,
  seq           INTEGER NOT NULL,
  PRIMARY KEY (group_id, node_uuid, seq)
);
""" + "\n";

    // PORT: a value that Python computes at import from TERMINAL_STATUSES (R17). sorted() of text is code point order (R12).
    internal static string TerminalSql { get; } =
        string.Join(", ", StateStore.TerminalStatuses.Order(Py.StringComparer).Select(status => $"'{status}'"));
}

/// <summary>
/// A state store in one SQLite file, with `sqlite3` from the standard library.
///
/// More than one process on one host can use the same file. Each method is one
/// `BEGIN IMMEDIATE` transaction that runs in a worker thread.
/// </summary>
// PORT: `sqlite3` is Microsoft.Data.Sqlite with raw SQLitePCL calls on the handle (R16). `self._lock` is a
// System.Threading.Lock (R8), and `self._conn` is a SqliteConnection. A `sqlite3.Row` is an
// OrderedDictionary<string, object?> with the column names as keys, in column order.
// PORT: the class owns a SqliteConnection, and analyzer CA1001 asks for IDisposable. Dispose does what close() does.
internal sealed class SQLiteIngestionStateStore : IngestionStateStore, IDisposable
{
    private readonly Func<double> clock;

    private readonly Lock lockValue;

    private readonly SqliteConnection conn;

    // PORT: `clock: Callable[[], float] = time.time`. A delegate cannot be a default value, so null stands for
    // `time.time`, which is Py.TimeSeconds (R8).
    internal SQLiteIngestionStateStore(string path, Func<double>? clock = null)
    {
        this.clock = clock ?? Py.TimeSeconds;
        lockValue = new Lock();

        // PORT: sqlite3.connect(path, timeout=BUSY_TIMEOUT_SECONDS, isolation_level=None, check_same_thread=False).
        // Pooling is off, so that close() releases the file. isolation_level=None is autocommit, which is the mode of
        // the raw handle. The timeout is sqlite3_busy_timeout on the handle (R16).
        conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        conn.Open();
        raw.sqlite3_busy_timeout(Handle(conn), (int)(SqliteStateStore.BusyTimeoutSeconds * 1000));

        // PORT: `self._conn.row_factory = sqlite3.Row` is the row form of Execute (R16).
        Execute(conn, "PRAGMA journal_mode=WAL");
        Execute(conn, $"PRAGMA busy_timeout={Py.Str(Py.Int(SqliteStateStore.BusyTimeoutSeconds * 1000))}");
        lock (lockValue)
        {
            ExecuteScript(conn, SqliteStateStore.Schema);
        }
    }

    /// <summary>Releases the connection, as close() does.</summary>
    public void Dispose()
    {
        lock (lockValue)
        {
            conn.Close();
        }
    }

    private T RunSync<T>(Func<SqliteConnection, T> func)
    {
        lock (lockValue)
        {
            var conn = this.conn;
            Execute(conn, "BEGIN IMMEDIATE");
            T result;
            try
            {
                result = func(conn);
            }
            catch (Exception)
            {
                Execute(conn, "ROLLBACK");
                throw;
            }

            Execute(conn, "COMMIT");
            return result;
        }
    }

    // PORT: `asyncio.to_thread` is Asyncio.ToThreadAsync (R8).
    private Task<T> RunAsync<T>(Func<SqliteConnection, T> func, CancellationToken cancellationToken = default) =>
        Asyncio.ToThreadAsync(() => RunSync(func), cancellationToken);

    // --- helpers that run inside a transaction ---

    // PORT: a `sqlite3.Row` is an OrderedDictionary keyed by column name (R16).
    private static OrderedDictionary<string, object?> Group(SqliteConnection conn, string groupId)
    {
        Execute(
            conn,
            "INSERT OR IGNORE INTO graphiti_ingest_group (group_id, next_seq, watermark) "
            + "VALUES (?, 1, 0)",
            groupId);
        return Execute(
            conn, "SELECT * FROM graphiti_ingest_group WHERE group_id = ?", groupId)[0];
    }

    private static OrderedDictionary<string, object?>? Unit(SqliteConnection conn, string groupId, long seq) =>
        Execute(
            conn, "SELECT * FROM graphiti_ingest_unit WHERE group_id = ? AND seq = ?", groupId, seq).FirstOrDefault();

    private OrderedDictionary<string, object?> LiveUnit(
        SqliteConnection conn,
        string groupId,
        long seq,
        bool applyingExempt = false)
    {
        var unit = Unit(conn, groupId, seq);
        if (unit is null || StateStore.FailedStatuses.Contains(Py.AsString(unit["status"])))
        {
            throw new UnitExpiredException(groupId, seq);
        }

        if (Py.AsDouble(unit["deadline"]) <= clock() && !(
            applyingExempt && Py.AsString(unit["status"]) == StateStore.StatusApplying))
        {
            throw new UnitExpiredException(groupId, seq);
        }

        return unit;
    }

    // PORT: `**values: Any` holds the column values in call order (R2).
    private static void UpdateUnit(
        SqliteConnection conn,
        string groupId,
        long seq,
        OrderedDictionary<string, object?> values)
    {
        var assignments = string.Join(", ", values.Keys.Select(column => $"{column} = ?"));
        Execute(
            conn,
            $"UPDATE graphiti_ingest_unit SET {assignments} WHERE group_id = ? AND seq = ?",
            [.. values.Values, groupId, seq]);
    }

    // PORT: static, because it reads no field (analyzer CA1822).
    private static void AdvanceWatermark(SqliteConnection conn, string groupId)
    {
        var group = Group(conn, groupId);
        var watermark = Py.AsLong(group["watermark"]);
        while (watermark + 1 < Py.AsLong(group["next_seq"]))
        {
            var unit = Unit(conn, groupId, watermark + 1);
            if (unit is not null && !StateStore.TerminalStatuses.Contains(Py.AsString(unit["status"])))
            {
                break;
            }

            watermark += 1;
        }

        if (watermark != Py.AsLong(group["watermark"]))
        {
            Execute(
                conn,
                "UPDATE graphiti_ingest_group SET watermark = ? WHERE group_id = ?",
                watermark,
                groupId);
        }
    }

    // PORT: `keep_payload` is keyword-only in Python, with no default.
    private void FailUnit(SqliteConnection conn, string groupId, long seq, string reason, bool keepPayload)
    {
        if (keepPayload)
        {
            UpdateUnit(
                conn,
                groupId,
                seq,
                new()
                {
                    ["status"] = StateStore.StatusApplyFailed,
                    ["fail_reason"] = reason,
                    ["ended_at"] = clock(),
                });
        }
        else
        {
            UpdateUnit(
                conn,
                groupId,
                seq,
                new()
                {
                    ["status"] = StateStore.StatusFailed,
                    ["fail_reason"] = reason,
                    ["ended_at"] = clock(),
                    ["payload"] = null,
                });
        }

        Execute(
            conn, "DELETE FROM graphiti_ingest_enrich WHERE group_id = ? AND seq = ?", groupId, seq);
        Execute(
            conn,
            "UPDATE graphiti_ingest_group SET admit_seq = NULL WHERE group_id = ? AND admit_seq = ?",
            groupId,
            seq);
        AdvanceWatermark(conn, groupId);
    }

    private void DeleteOldUnits(SqliteConnection conn, string groupId, double retainSeconds)
    {
        var group = Group(conn, groupId);
        var row = Execute(
            conn,
            "SELECT MIN(snapshot_seq) AS snapshot FROM graphiti_ingest_unit "
            + $"WHERE group_id = ? AND status NOT IN ({SqliteStateStore.TerminalSql}) AND snapshot_seq IS NOT NULL",
            groupId)[0];
        var limit = Py.AsLong(group["watermark"]);
        if (row["snapshot"] is not null)
        {
            limit = Math.Min(limit, Py.AsLong(row["snapshot"]));
        }

        Execute(
            conn,
            "DELETE FROM graphiti_ingest_unit "
            + $"WHERE group_id = ? AND seq <= ? AND status IN ({SqliteStateStore.TerminalSql}) "
            + "AND ended_at IS NOT NULL AND ended_at < ? "
            + "AND NOT EXISTS (SELECT 1 FROM graphiti_ingest_enrich e "
            + "WHERE e.group_id = graphiti_ingest_unit.group_id "
            + "AND e.seq = graphiti_ingest_unit.seq)",
            groupId,
            limit,
            clock() - retainSeconds);
    }

    private OrderedDictionary<string, object?> CommitUnit(SqliteConnection conn, string groupId, long seq, long fence)
    {
        var unit = Unit(conn, groupId, seq);
        if (unit is null || StateStore.FailedStatuses.Contains(Py.AsString(unit["status"])))
        {
            throw new UnitExpiredException(groupId, seq);
        }

        if (Py.AsString(unit["status"]) == StateStore.StatusCommitted || Py.AsLong(unit["fence"]) != fence)
        {
            throw new StaleCommitException(groupId, seq);
        }

        if (Py.AsDouble(unit["deadline"]) <= clock())
        {
            throw new UnitExpiredException(groupId, seq);
        }

        return unit;
    }

    // --- interface ---

    // PORT: each method passes its nested `work` function to RunAsync as a local function.
    internal override Task<Admission?> BeginAdmitAsync(
        string groupId,
        string unitId,
        double ttl,
        CancellationToken cancellationToken = default)
    {
        Admission? Work(SqliteConnection conn)
        {
            var group = Group(conn, groupId);
            var now = clock();

            // A `unit_id` can have more than one unit. Only the last one can be live.
            var previous = Execute(
                conn,
                "SELECT seq, status, deadline FROM graphiti_ingest_unit "
                + "WHERE group_id = ? AND unit_id = ? ORDER BY seq DESC LIMIT 1",
                groupId,
                unitId).FirstOrDefault();
            if (previous is not null)
            {
                if (!StateStore.IsReleased(Py.AsString(previous["status"]), Py.AsDouble(previous["deadline"]), now))
                {
                    return new Admission { Seq = Py.AsLong(previous["seq"]), Existing = true };
                }

                if (!StateStore.TerminalStatuses.Contains(Py.AsString(previous["status"])))
                {
                    FailUnit(
                        conn, groupId, Py.AsLong(previous["seq"]), StateStore.ReasonExpired, keepPayload: false);
                }

                group = Group(conn, groupId);
            }

            if (group["admit_seq"] is not null)
            {
                var holder = Unit(conn, groupId, Py.AsLong(group["admit_seq"]));
                if (holder is not null && Py.AsDouble(holder["deadline"]) > now)
                {
                    return null;
                }

                if (holder is not null)
                {
                    FailUnit(
                        conn, groupId, Py.AsLong(holder["seq"]), StateStore.ReasonExpired, keepPayload: false);
                }
            }

            var seq = Py.AsLong(group["next_seq"]);
            Execute(
                conn,
                "INSERT INTO graphiti_ingest_unit (group_id, seq, unit_id, status, deadline) "
                + "VALUES (?, ?, ?, ?, ?)",
                groupId,
                seq,
                unitId,
                StateStore.StatusAdmitting,
                now + ttl);
            Execute(
                conn,
                "UPDATE graphiti_ingest_group SET next_seq = ?, admit_seq = ? WHERE group_id = ?",
                seq + 1,
                seq,
                groupId);
            return new Admission
            {
                Seq = seq,
                PreviousSeq = previous is not null ? Py.AsLong(previous["seq"]) : null,
            };
        }

        return RunAsync(Work, cancellationToken);
    }

    internal override Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default)
    {
        object? Work(SqliteConnection conn)
        {
            var unit = LiveUnit(conn, groupId, seq);
            if (Py.AsString(unit["status"]) == StateStore.StatusAdmitting)
            {
                UpdateUnit(conn, groupId, seq, new() { ["status"] = StateStore.StatusAdmitted });
            }

            Execute(
                conn,
                "UPDATE graphiti_ingest_group SET admit_seq = NULL "
                + "WHERE group_id = ? AND admit_seq = ?",
                groupId,
                seq);
            return null;
        }

        return RunAsync(Work, cancellationToken);
    }

    internal override Task<AnalysisStart> StartAnalysisAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default)
    {
        AnalysisStart Work(SqliteConnection conn)
        {
            var group = Group(conn, groupId);
            LiveUnit(conn, groupId, seq);
            var startedAt = clock();
            UpdateUnit(
                conn, groupId, seq, new() { ["snapshot_seq"] = group["watermark"], ["started_at"] = startedAt });
            return new AnalysisStart { SnapshotSeq = Py.AsLong(group["watermark"]), StartedAt = startedAt };
        }

        return RunAsync(Work, cancellationToken);
    }

    internal override Task SaveChangeSetAsync(
        string groupId,
        long seq,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        object? Work(SqliteConnection conn)
        {
            var unit = LiveUnit(conn, groupId, seq, applyingExempt: true);
            if (Py.AsString(unit["status"]) != StateStore.StatusAdmitted)
            {
                // A second analysis does not replace a stored payload.
                throw new StaleCommitException(groupId, seq);
            }

            UpdateUnit(conn, groupId, seq, new() { ["payload"] = payload, ["status"] = StateStore.StatusAnalyzed });
            return null;
        }

        return RunAsync(Work, cancellationToken);
    }

    internal override Task HeartbeatAsync(
        string groupId,
        long seq,
        double ttl,
        long? fence = null,
        CancellationToken cancellationToken = default)
    {
        object? Work(SqliteConnection conn)
        {
            var unit = LiveUnit(conn, groupId, seq, applyingExempt: true);
            if (StateStore.IsStaleHeartbeat(Py.AsString(unit["status"]), Py.AsLong(unit["fence"]), fence))
            {
                throw new StaleCommitException(groupId, seq);
            }

            UpdateUnit(conn, groupId, seq, new() { ["deadline"] = clock() + ttl });
            return null;
        }

        return RunAsync(Work, cancellationToken);
    }

    internal override Task<BeginCommitResult> BeginCommitAsync(
        string groupId,
        long seq,
        double ttl,
        CancellationToken cancellationToken = default)
    {
        BeginCommitResult Work(SqliteConnection conn)
        {
            var group = Group(conn, groupId);
            var unit = LiveUnit(conn, groupId, seq, applyingExempt: true);
            if (Py.AsString(unit["status"]) == StateStore.StatusCommitted)
            {
                throw new StaleCommitException(groupId, seq);
            }

            var now = clock();
            var earlierUnits = Execute(
                conn,
                "SELECT * FROM graphiti_ingest_unit "
                + $"WHERE group_id = ? AND seq > ? AND seq < ? AND status NOT IN ({SqliteStateStore.TerminalSql}) "
                + "ORDER BY seq",
                groupId,
                group["watermark"],
                seq);
            foreach (var earlier in earlierUnits)
            {
                if (Py.AsDouble(earlier["deadline"]) > now)
                {
                    return new CommitWait();
                }

                if (Py.AsString(earlier["status"]) == StateStore.StatusApplying && earlier["payload"] is not null)
                {
                    var earlierFence = Py.AsLong(earlier["fence"]) + 1;
                    UpdateUnit(
                        conn,
                        groupId,
                        Py.AsLong(earlier["seq"]),
                        new() { ["fence"] = earlierFence, ["deadline"] = now + ttl });
                    return new CommitRecover
                    {
                        Seq = Py.AsLong(earlier["seq"]),
                        Fence = earlierFence,
                        FinalPayload = Py.AsBytes(earlier["payload"]),
                    };
                }

                FailUnit(conn, groupId, Py.AsLong(earlier["seq"]), StateStore.ReasonExpired, keepPayload: false);
            }

            if (Py.AsString(unit["status"]) == StateStore.StatusApplying && Py.AsDouble(unit["deadline"]) > now)
            {
                // A different attempt applies the final change set of the unit.
                return new CommitWait();
            }

            var fence = Py.AsLong(unit["fence"]) + 1;
            UpdateUnit(conn, groupId, seq, new() { ["fence"] = fence, ["deadline"] = now + ttl });
            var finalPayload =
                Py.AsString(unit["status"]) == StateStore.StatusApplying && unit["payload"] is not null
                ? Py.AsBytes(unit["payload"])
                : null;
            return new CommitTurn { Fence = fence, FinalPayload = finalPayload };
        }

        return RunAsync(Work, cancellationToken);
    }

    internal override Task<List<DeltaUnit>> LoadDeltaAsync(
        string groupId,
        long afterSeq,
        long beforeSeq,
        double since,
        CancellationToken cancellationToken = default)
    {
        List<DeltaUnit> Work(SqliteConnection conn)
        {
            var rows = Execute(
                conn,
                "SELECT seq, status, payload FROM graphiti_ingest_unit "
                + "WHERE group_id = ? AND seq < ? AND status IN (?, ?) AND payload IS NOT NULL "
                + "AND (seq > ? OR (ended_at IS NOT NULL AND ended_at >= ?)) ORDER BY seq",
                groupId,
                beforeSeq,
                StateStore.StatusCommitted,
                StateStore.StatusApplyFailed,
                afterSeq,
                since);
            return
            [
                .. rows.Select(row => new DeltaUnit
                {
                    Seq = Py.AsLong(row["seq"]),
                    Status = Py.AsString(row["status"]),
                    Payload = Py.AsBytes(row["payload"]),
                }),
            ];
        }

        return RunAsync(Work, cancellationToken);
    }

    internal override Task SaveFinalAsync(
        string groupId,
        long seq,
        long fence,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        object? Work(SqliteConnection conn)
        {
            CommitUnit(conn, groupId, seq, fence);
            UpdateUnit(conn, groupId, seq, new() { ["payload"] = payload, ["status"] = StateStore.StatusApplying });
            return null;
        }

        return RunAsync(Work, cancellationToken);
    }

    internal override Task FinishCommitAsync(
        string groupId,
        long seq,
        long fence,
        List<string> nodeUuids,
        double retainSeconds,
        CancellationToken cancellationToken = default)
    {
        object? Work(SqliteConnection conn)
        {
            CommitUnit(conn, groupId, seq, fence);
            UpdateUnit(conn, groupId, seq, new() { ["status"] = StateStore.StatusCommitted, ["ended_at"] = clock() });

            // PORT: `executemany` runs the statement for each parameter row, in order. dict.fromkeys keeps the first of
            // each UUID in order (R14).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var nodeUuid in nodeUuids.Where(seen.Add))
            {
                Execute(
                    conn,
                    "INSERT OR IGNORE INTO graphiti_ingest_enrich (group_id, node_uuid, seq) "
                    + "VALUES (?, ?, ?)",
                    groupId,
                    nodeUuid,
                    seq);
            }

            AdvanceWatermark(conn, groupId);
            DeleteOldUnits(conn, groupId, retainSeconds);
            return null;
        }

        return RunAsync(Work, cancellationToken);
    }

    internal override Task FailAsync(
        string groupId,
        long seq,
        string reason,
        long? fence = null,
        CancellationToken cancellationToken = default)
    {
        object? Work(SqliteConnection conn)
        {
            var unit = Unit(conn, groupId, seq);
            if (unit is null || StateStore.TerminalStatuses.Contains(Py.AsString(unit["status"])))
            {
                return null;
            }

            if (Py.AsString(unit["status"]) == StateStore.StatusApplying)
            {
                if (reason != StateStore.ReasonApplyFailed)
                {
                    throw new ArgumentException(
                        "fail is not permitted for a unit with a stored final change set, "
                        + $"with the exception of the reason {Py.Repr(StateStore.ReasonApplyFailed)}");
                }

                if (fence is null || fence != Py.AsLong(unit["fence"]))
                {
                    throw new StaleCommitException(groupId, seq);
                }

                FailUnit(conn, groupId, seq, reason, keepPayload: true);
                return null;
            }

            FailUnit(conn, groupId, seq, reason, keepPayload: false);
            return null;
        }

        return RunAsync(Work, cancellationToken);
    }

    internal override Task<List<string>> ReadyEnrichAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default)
    {
        List<string> Work(SqliteConnection conn)
        {
            LiveUnit(conn, groupId, seq);
            Execute(
                conn,
                "DELETE FROM graphiti_ingest_enrich WHERE group_id = ? AND NOT EXISTS ("
                + "SELECT 1 FROM graphiti_ingest_unit u WHERE u.group_id = graphiti_ingest_enrich.group_id "
                + "AND u.seq = graphiti_ingest_enrich.seq AND u.deadline > ?)",
                groupId,
                clock());
            var rows = Execute(
                conn,
                "SELECT e.node_uuid FROM graphiti_ingest_enrich e "
                + "WHERE e.group_id = ? AND e.seq = ? AND NOT EXISTS ("
                + "SELECT 1 FROM graphiti_ingest_enrich b WHERE b.group_id = e.group_id "
                + "AND b.node_uuid = e.node_uuid AND b.seq < e.seq) ORDER BY e.rowid",
                groupId,
                seq);
            return [.. rows.Select(row => Py.AsString(row["node_uuid"]))];
        }

        return RunAsync(Work, cancellationToken);
    }

    internal override Task FinishEnrichAsync(
        string groupId,
        long seq,
        List<string> nodeUuids,
        CancellationToken cancellationToken = default)
    {
        object? Work(SqliteConnection conn)
        {
            // PORT: `executemany` runs the statement for each parameter row, in order.
            foreach (var nodeUuid in nodeUuids)
            {
                Execute(
                    conn,
                    "DELETE FROM graphiti_ingest_enrich WHERE group_id = ? AND seq = ? AND node_uuid = ?",
                    groupId,
                    seq,
                    nodeUuid);
            }

            return null;
        }

        return RunAsync(Work, cancellationToken);
    }

    // PORT: `async def close` has no `await`, so the body runs at the call and the task is complete.
    internal override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        lock (lockValue)
        {
            conn.Close();
        }

        return Task.CompletedTask;
    }

    // PORT: the C# form of `conn.execute(sql, parameters)` of sqlite3, with each row read in full: `.fetchone()` is the
    // first row or null, and `.fetchall()` is the list. No rule names this form. Microsoft.Data.Sqlite refuses the `?`
    // parameters of the upstream SQL text, so the statement runs as raw SQLitePCL calls on the handle (R16). A
    // parameter binds as sqlite3 binds it: an int as INTEGER, a float as REAL, a text as TEXT after the strict UTF-8
    // encoding of R12, bytes as BLOB, and None as NULL. A column value reads as sqlite3 reads it (R16).
    private static List<OrderedDictionary<string, object?>> Execute(SqliteConnection conn, string sql, params object?[] parameters)
    {
        var db = Handle(conn);
        SqliteException.ThrowExceptionForRC(raw.sqlite3_prepare_v2(db, sql, out var stmt), db);
        try
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                int bound;
                switch (parameters[i])
                {
                    case null:
                        bound = raw.sqlite3_bind_null(stmt, i + 1);
                        break;
                    case long l:
                        bound = raw.sqlite3_bind_int64(stmt, i + 1, l);
                        break;
                    case double d:
                        bound = raw.sqlite3_bind_double(stmt, i + 1, d);
                        break;
                    case string text:
                        _ = PyText.EncodeUtf8(text);
                        bound = raw.sqlite3_bind_text(stmt, i + 1, text);
                        break;
                    case byte[] bytes:
                        bound = raw.sqlite3_bind_blob(stmt, i + 1, bytes);
                        break;
                    default:
                        throw new InvalidCastException($"Error binding parameter {i + 1}: type '{Py.TypeName(parameters[i])}' is not supported");
                }

                SqliteException.ThrowExceptionForRC(bound, db);
            }

            List<OrderedDictionary<string, object?>> rows = [];
            while (true)
            {
                var rc = raw.sqlite3_step(stmt);
                if (rc == raw.SQLITE_DONE)
                {
                    return rows;
                }

                if (rc != raw.SQLITE_ROW)
                {
                    SqliteException.ThrowExceptionForRC(rc, db);
                }

                var row = new OrderedDictionary<string, object?>();
                var count = raw.sqlite3_column_count(stmt);
                for (var column = 0; column < count; column++)
                {
                    row[raw.sqlite3_column_name(stmt, column).utf8_to_string()] = raw.sqlite3_column_type(stmt, column) switch
                    {
                        raw.SQLITE_INTEGER => raw.sqlite3_column_int64(stmt, column),
                        raw.SQLITE_FLOAT => raw.sqlite3_column_double(stmt, column),
                        raw.SQLITE_TEXT => raw.sqlite3_column_text(stmt, column).utf8_to_string(),
                        raw.SQLITE_BLOB => raw.sqlite3_column_blob(stmt, column).ToArray(),
                        _ => null,
                    };
                }

                rows.Add(row);
            }
        }
        finally
        {
            raw.sqlite3_finalize(stmt);
        }
    }

    // PORT: the C# form of `conn.executescript(sql)` of sqlite3: each statement of the text, in order.
    private static void ExecuteScript(SqliteConnection conn, string sql)
    {
        var db = Handle(conn);
        SqliteException.ThrowExceptionForRC(raw.sqlite3_exec(db, sql), db);
    }

    // PORT: sqlite3 raises ProgrammingError for a call on a closed connection.
    private static sqlite3 Handle(SqliteConnection conn) =>
        conn.Handle ?? throw new InvalidOperationException("Cannot operate on a closed database.");
}
