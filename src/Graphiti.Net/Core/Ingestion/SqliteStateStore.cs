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

using Microsoft.Data.Sqlite;

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

    // PORT: a value that Python computes at import from TERMINAL_STATUSES (R17).
    // TODO(port): _TERMINAL_SQL
    internal static string TerminalSql => throw new NotSupportedException("TODO(port): _TERMINAL_SQL");
}

// PORT: `sqlite3` is Microsoft.Data.Sqlite with raw SQLitePCL calls on the handle (R16). `self._lock` is a
// System.Threading.Lock (R8), and `self._conn` is a SqliteConnection. A `sqlite3.Row` is an
// OrderedDictionary<string, object?> with the column names as keys, in column order.
// PORT: the private attributes `_clock`, `_lock`, and `_conn` are fields of the unit. A skeleton field that no member
// reads fails the build (CS0169).
internal sealed class SQLiteIngestionStateStore : IngestionStateStore
{
    // PORT: `clock: Callable[[], float] = time.time`. A delegate cannot be a default value, so null stands for
    // `time.time`, which is Py.TimeSeconds (R8).
    // TODO(port): SQLiteIngestionStateStore.__init__
    internal SQLiteIngestionStateStore(string path, Func<double>? clock = null)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.__init__");
    }

    // TODO(port): SQLiteIngestionStateStore._run_sync
    private T RunSync<T>(Func<SqliteConnection, T> func)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore._run_sync");
    }

    // PORT: `asyncio.to_thread` is Asyncio.ToThreadAsync (R8).
    // TODO(port): SQLiteIngestionStateStore._run
    private Task<T> RunAsync<T>(Func<SqliteConnection, T> func, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore._run");
    }

    // --- helpers that run inside a transaction ---

    // PORT: a `sqlite3.Row` is an OrderedDictionary keyed by column name (R16).
    // TODO(port): SQLiteIngestionStateStore._group
    private static OrderedDictionary<string, object?> Group(SqliteConnection conn, string groupId)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore._group");
    }

    // TODO(port): SQLiteIngestionStateStore._unit
    private static OrderedDictionary<string, object?>? Unit(SqliteConnection conn, string groupId, long seq)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore._unit");
    }

    // TODO(port): SQLiteIngestionStateStore._live_unit
    private OrderedDictionary<string, object?> LiveUnit(
        SqliteConnection conn,
        string groupId,
        long seq,
        bool applyingExempt = false)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore._live_unit");
    }

    // PORT: `**values: Any` holds the column values in call order (R2).
    // TODO(port): SQLiteIngestionStateStore._update_unit
    private static void UpdateUnit(
        SqliteConnection conn,
        string groupId,
        long seq,
        OrderedDictionary<string, object?> values)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore._update_unit");
    }

    // TODO(port): SQLiteIngestionStateStore._advance_watermark
    private void AdvanceWatermark(SqliteConnection conn, string groupId)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore._advance_watermark");
    }

    // PORT: `keep_payload` is keyword-only in Python, with no default.
    // TODO(port): SQLiteIngestionStateStore._fail_unit
    private void FailUnit(SqliteConnection conn, string groupId, long seq, string reason, bool keepPayload)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore._fail_unit");
    }

    // TODO(port): SQLiteIngestionStateStore._delete_old_units
    private void DeleteOldUnits(SqliteConnection conn, string groupId, double retainSeconds)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore._delete_old_units");
    }

    // TODO(port): SQLiteIngestionStateStore._commit_unit
    private OrderedDictionary<string, object?> CommitUnit(SqliteConnection conn, string groupId, long seq, long fence)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore._commit_unit");
    }

    // --- interface ---

    // PORT: each method passes its nested `work` function to RunAsync as a lambda.
    // TODO(port): SQLiteIngestionStateStore.begin_admit
    internal override Task<Admission?> BeginAdmitAsync(
        string groupId,
        string unitId,
        double ttl,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.begin_admit");
    }

    // TODO(port): SQLiteIngestionStateStore.end_admit
    internal override Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.end_admit");
    }

    // TODO(port): SQLiteIngestionStateStore.start_analysis
    internal override Task<AnalysisStart> StartAnalysisAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.start_analysis");
    }

    // TODO(port): SQLiteIngestionStateStore.save_change_set
    internal override Task SaveChangeSetAsync(
        string groupId,
        long seq,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.save_change_set");
    }

    // TODO(port): SQLiteIngestionStateStore.heartbeat
    internal override Task HeartbeatAsync(
        string groupId,
        long seq,
        double ttl,
        long? fence = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.heartbeat");
    }

    // TODO(port): SQLiteIngestionStateStore.begin_commit
    internal override Task<BeginCommitResult> BeginCommitAsync(
        string groupId,
        long seq,
        double ttl,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.begin_commit");
    }

    // TODO(port): SQLiteIngestionStateStore.load_delta
    internal override Task<List<DeltaUnit>> LoadDeltaAsync(
        string groupId,
        long afterSeq,
        long beforeSeq,
        double since,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.load_delta");
    }

    // TODO(port): SQLiteIngestionStateStore.save_final
    internal override Task SaveFinalAsync(
        string groupId,
        long seq,
        long fence,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.save_final");
    }

    // TODO(port): SQLiteIngestionStateStore.finish_commit
    internal override Task FinishCommitAsync(
        string groupId,
        long seq,
        long fence,
        List<string> nodeUuids,
        double retainSeconds,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.finish_commit");
    }

    // TODO(port): SQLiteIngestionStateStore.fail
    internal override Task FailAsync(
        string groupId,
        long seq,
        string reason,
        long? fence = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.fail");
    }

    // TODO(port): SQLiteIngestionStateStore.ready_enrich
    internal override Task<List<string>> ReadyEnrichAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.ready_enrich");
    }

    // TODO(port): SQLiteIngestionStateStore.finish_enrich
    internal override Task FinishEnrichAsync(
        string groupId,
        long seq,
        List<string> nodeUuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.finish_enrich");
    }

    // TODO(port): SQLiteIngestionStateStore.close
    internal override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): SQLiteIngestionStateStore.close");
    }
}
