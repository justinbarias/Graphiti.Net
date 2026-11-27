// Ported from graphiti_core/ingestion/state_store.py
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

internal static partial class StateStore
{
    internal const string StatusAdmitting = "admitting";
    internal const string StatusAdmitted = "admitted";
    internal const string StatusAnalyzed = "analyzed";
    internal const string StatusApplying = "applying";
    internal const string StatusCommitted = "committed";
    internal const string StatusFailed = "failed";
    internal const string StatusApplyFailed = "apply_failed";

    // For the order of commits, these statuses release the position of a unit.
    // PORT: a frozenset that code tests with `in` and passes to sorted() (R14).
    internal static readonly HashSet<string> TerminalStatuses =
        new(StringComparer.Ordinal) { StatusCommitted, StatusFailed, StatusApplyFailed };

    internal static readonly HashSet<string> FailedStatuses = new(StringComparer.Ordinal) { StatusFailed, StatusApplyFailed };

    internal const string ReasonApplyFailed = "apply_failed";
    internal const string ReasonExpired = "expired";
}

internal sealed record Admission
{
    internal required long Seq { get; init; }

    internal bool Existing { get; init; }

    internal long? PreviousSeq { get; init; }
}

internal sealed record AnalysisStart
{
    internal required long SnapshotSeq { get; init; }

    internal required double StartedAt { get; init; }
}

// PORT: `begin_commit` returns `CommitTurn | CommitWait | CommitRecover`, a union of three shapes (Part 3). It is an
// abstract record with one derived record for each case. Python has no name for the union.
// TODO(port): no rule for the name of a union that Python does not name
internal abstract record BeginCommitResult;

// PORT: a frozen dataclass is a record with `{ get; init; }` (R7). A record compares a `byte[]` by reference, where
// Python compares `bytes` by value. No upstream site compares two of these values.
internal sealed record CommitTurn : BeginCommitResult
{
    internal required long Fence { get; init; }

    internal byte[]? FinalPayload { get; init; }
}

internal sealed record CommitWait : BeginCommitResult;

internal sealed record CommitRecover : BeginCommitResult
{
    internal required long Seq { get; init; }

    internal required long Fence { get; init; }

    internal required byte[] FinalPayload { get; init; }
}

internal sealed record DeltaUnit
{
    internal required long Seq { get; init; }

    internal required string Status { get; init; }

    internal required byte[] Payload { get; init; }
}

internal static partial class StateStore
{
    // TODO(port): is_released
    internal static bool IsReleased(string status, double deadline, double now)
    {
        throw new NotSupportedException("TODO(port): is_released");
    }

    // TODO(port): is_stale_heartbeat
    internal static bool IsStaleHeartbeat(string status, long unitFence, long? fence)
    {
        throw new NotSupportedException("TODO(port): is_stale_heartbeat");
    }
}

internal abstract class IngestionStateStore
{
    internal abstract Task<Admission?> BeginAdmitAsync(
        string groupId,
        string unitId,
        double ttl,
        CancellationToken cancellationToken = default);

    internal abstract Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default);

    internal abstract Task<AnalysisStart> StartAnalysisAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default);

    internal abstract Task SaveChangeSetAsync(
        string groupId,
        long seq,
        byte[] payload,
        CancellationToken cancellationToken = default);

    internal abstract Task HeartbeatAsync(
        string groupId,
        long seq,
        double ttl,
        long? fence = null,
        CancellationToken cancellationToken = default);

    internal abstract Task<BeginCommitResult> BeginCommitAsync(
        string groupId,
        long seq,
        double ttl,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<DeltaUnit>> LoadDeltaAsync(
        string groupId,
        long afterSeq,
        long beforeSeq,
        double since,
        CancellationToken cancellationToken = default);

    internal abstract Task SaveFinalAsync(
        string groupId,
        long seq,
        long fence,
        byte[] payload,
        CancellationToken cancellationToken = default);

    internal abstract Task FinishCommitAsync(
        string groupId,
        long seq,
        long fence,
        List<string> nodeUuids,
        double retainSeconds,
        CancellationToken cancellationToken = default);

    internal abstract Task FailAsync(
        string groupId,
        long seq,
        string reason,
        long? fence = null,
        CancellationToken cancellationToken = default);

    internal abstract Task<List<string>> ReadyEnrichAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default);

    internal abstract Task FinishEnrichAsync(
        string groupId,
        long seq,
        List<string> nodeUuids,
        CancellationToken cancellationToken = default);

    internal abstract Task CloseAsync(CancellationToken cancellationToken = default);
}

// PORT: the private dataclass `_Unit`. internal, because a C# type at namespace level cannot be private (R2).
internal sealed record Unit
{
    internal required long Seq { get; set; }

    internal required string UnitId { get; set; }

    internal required string Status { get; set; }

    internal required double Deadline { get; set; }

    internal long? SnapshotSeq { get; set; }

    internal double? StartedAt { get; set; }

    internal double? EndedAt { get; set; }

    internal long Fence { get; set; }

    internal string? FailReason { get; set; }

    internal byte[]? Payload { get; set; }
}

// PORT: the private dataclass `_Group`.
internal sealed record Group
{
    internal long NextSeq { get; set; } = 1;

    internal long Watermark { get; set; }

    internal long? AdmitSeq { get; set; }

    // PORT: code iterates the units in key order and by value (R14).
    internal OrderedDictionary<long, Unit> Units { get; set; } = new();

    // PORT: code reads this dict only by key (R14).
    internal Dictionary<string, long> UnitIds { get; set; } = new(StringComparer.Ordinal);

    // seq -> node UUIDs with an enrichment entry, in registration order
    internal OrderedDictionary<long, List<string>> Enrich { get; set; } = new();
}

internal sealed class InMemoryIngestionStateStore : IngestionStateStore
{
    // PORT: `clock: Callable[[], float] = time.time`. A delegate cannot be a default value, so null stands for
    // `time.time`, which is Py.TimeSeconds (R8).
    // TODO(port): InMemoryIngestionStateStore.__init__
    internal InMemoryIngestionStateStore(Func<double>? clock = null)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.__init__");
    }

    // PORT: the private attributes `_clock` (a Func<double>) and `_groups` (a Dictionary<string, Group>, read only by
    // key, R14) are fields of the unit. A skeleton field that no member reads fails the build (CS0169).

    // TODO(port): InMemoryIngestionStateStore._group
    private Group Group(string groupId)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore._group");
    }

    // TODO(port): InMemoryIngestionStateStore._live_unit
    private Unit LiveUnit(string groupId, long seq, bool applyingExempt = false)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore._live_unit");
    }

    // TODO(port): InMemoryIngestionStateStore._advance_watermark
    private void AdvanceWatermark(Group group)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore._advance_watermark");
    }

    // PORT: `keep_payload` is keyword-only in Python, with no default.
    // TODO(port): InMemoryIngestionStateStore._fail_unit
    private void FailUnit(Group group, Unit unit, string reason, bool keepPayload)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore._fail_unit");
    }

    // TODO(port): InMemoryIngestionStateStore._delete_old_units
    private void DeleteOldUnits(Group group, double retainSeconds)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore._delete_old_units");
    }

    // TODO(port): InMemoryIngestionStateStore.begin_admit
    internal override Task<Admission?> BeginAdmitAsync(
        string groupId,
        string unitId,
        double ttl,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.begin_admit");
    }

    // TODO(port): InMemoryIngestionStateStore.end_admit
    internal override Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.end_admit");
    }

    // TODO(port): InMemoryIngestionStateStore.start_analysis
    internal override Task<AnalysisStart> StartAnalysisAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.start_analysis");
    }

    // TODO(port): InMemoryIngestionStateStore.save_change_set
    internal override Task SaveChangeSetAsync(
        string groupId,
        long seq,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.save_change_set");
    }

    // TODO(port): InMemoryIngestionStateStore.heartbeat
    internal override Task HeartbeatAsync(
        string groupId,
        long seq,
        double ttl,
        long? fence = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.heartbeat");
    }

    // TODO(port): InMemoryIngestionStateStore.begin_commit
    internal override Task<BeginCommitResult> BeginCommitAsync(
        string groupId,
        long seq,
        double ttl,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.begin_commit");
    }

    // TODO(port): InMemoryIngestionStateStore.load_delta
    internal override Task<List<DeltaUnit>> LoadDeltaAsync(
        string groupId,
        long afterSeq,
        long beforeSeq,
        double since,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.load_delta");
    }

    // TODO(port): InMemoryIngestionStateStore._commit_unit
    private Unit CommitUnit(string groupId, long seq, long fence)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore._commit_unit");
    }

    // TODO(port): InMemoryIngestionStateStore.save_final
    internal override Task SaveFinalAsync(
        string groupId,
        long seq,
        long fence,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.save_final");
    }

    // TODO(port): InMemoryIngestionStateStore.finish_commit
    internal override Task FinishCommitAsync(
        string groupId,
        long seq,
        long fence,
        List<string> nodeUuids,
        double retainSeconds,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.finish_commit");
    }

    // TODO(port): InMemoryIngestionStateStore.fail
    internal override Task FailAsync(
        string groupId,
        long seq,
        string reason,
        long? fence = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.fail");
    }

    // TODO(port): InMemoryIngestionStateStore.ready_enrich
    internal override Task<List<string>> ReadyEnrichAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.ready_enrich");
    }

    // TODO(port): InMemoryIngestionStateStore.finish_enrich
    internal override Task FinishEnrichAsync(
        string groupId,
        long seq,
        List<string> nodeUuids,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.finish_enrich");
    }

    // TODO(port): InMemoryIngestionStateStore.close
    internal override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): InMemoryIngestionStateStore.close");
    }
}
