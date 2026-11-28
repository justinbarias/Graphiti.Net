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

using Graphiti.PyCompat;

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

/// <summary>
/// The position of a unit.
///
/// `existing` is True when the store already had a live unit for the `unit_id`. The
/// caller then has no admit lease, and it must not write the episodes again.
/// `previous_seq` is the position of an earlier unit of the `unit_id` that failed.
/// </summary>
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
// abstract record with one derived record for each case. Python names each case and not the union, so the cases are
// top-level records and the lead gave the name of the base record.
internal abstract record BeginCommitResult;

// PORT: a frozen dataclass is a record with `{ get; init; }` (R7). A record compares a `byte[]` by reference, where
// Python compares `bytes` by value. No upstream site compares two of these values.
/// <summary>The unit has the commit turn. `final_payload` has a value when a final change set is stored.</summary>
internal sealed record CommitTurn : BeginCommitResult
{
    internal required long Fence { get; init; }

    internal byte[]? FinalPayload { get; init; }
}

/// <summary>An earlier unit is in progress.</summary>
internal sealed record CommitWait : BeginCommitResult;

/// <summary>An earlier unit stopped after it stored its final change set. The caller applies it.</summary>
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
    /// <summary>Return True when a unit does not keep its `unit_id` for a retry of the admission.</summary>
    internal static bool IsReleased(string status, double deadline, double now)
    {
        if (FailedStatuses.Contains(status))
        {
            return true;
        }

        if (status is StatusApplying or StatusCommitted)
        {
            return false;
        }

        return deadline <= now;
    }

    /// <summary>Return True when a heartbeat with `fence` must not set the deadline of the unit.</summary>
    internal static bool IsStaleHeartbeat(string status, long unitFence, long? fence)
    {
        if (fence is null)
        {
            return status == StatusApplying;
        }

        return fence != unitFence;
    }
}

/// <summary>
/// The state of concurrent ingestion: the sequence, the change sets, and the enrichment order.
///
/// Each method is atomic. A store for more than one host must compare deadlines with
/// the clock of the store. The store does not read a payload.
/// </summary>
internal abstract class IngestionStateStore
{
    /// <summary>
    /// Give the admit lease and the next sequence number to a unit.
    ///
    /// For a `unit_id` that has a live unit, the call returns the `seq` of that unit
    /// with `existing=True`. It gives no lease, and it changes no state. The caller
    /// must then write nothing and call no method that changes the state of that
    /// unit: a different attempt has the unit. The Admit phase raises
    /// `UnitInProgressError` for this result. The host can call `begin_admit` again
    /// after the lease time.
    ///
    /// A unit is not live when its status is `failed` or `apply_failed`, or when its
    /// deadline is in the past and it has no stored final change set. The call marks
    /// such an expired unit `failed`. The `unit_id` then gets a new sequence number,
    /// and `previous_seq` is the `seq` of the unit that is not live.
    ///
    /// Returns None when a different unit has the lease and its deadline is in the
    /// future.
    /// </summary>
    internal abstract Task<Admission?> BeginAdmitAsync(
        string groupId,
        string unitId,
        double ttl,
        CancellationToken cancellationToken = default);

    /// <summary>Set the status `admitted` and release the admit lease.</summary>
    internal abstract Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default);

    /// <summary>Store and return the watermark and the current time.</summary>
    internal abstract Task<AnalysisStart> StartAnalysisAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Store the payload of the analysis and set the status `analyzed`.
    ///
    /// The call is permitted only for a unit with the status `admitted`. For each other
    /// status it raises `StaleCommitError`, and the stored payload stays.
    /// </summary>
    internal abstract Task SaveChangeSetAsync(
        string groupId,
        long seq,
        byte[] payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Set the deadline of a unit to the current time plus `ttl`.
    ///
    /// `fence` is the fence of the commit turn of the caller, or None for a caller
    /// with no turn. A `fence` that is not the fence of the unit raises
    /// `StaleCommitError`. For a unit with the status `applying`, only the attempt
    /// with the turn can set the deadline: a call with no `fence` also raises
    /// `StaleCommitError`.
    /// </summary>
    internal abstract Task HeartbeatAsync(
        string groupId,
        long seq,
        double ttl,
        long? fence = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Give the commit turn to a unit when each earlier unit is committed or failed.
    ///
    /// Returns `CommitWait` also when the unit has the status `applying` and a deadline
    /// in the future. A different attempt then applies its final change set.
    /// </summary>
    internal abstract Task<BeginCommitResult> BeginCommitAsync(
        string groupId,
        long seq,
        double ttl,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Return the final change sets of the `committed` and `apply_failed` units.
    ///
    /// A unit is in the result when its `seq` is below `before_seq`, and its `seq` is
    /// above `after_seq` or its end time is at or after `since`. The order is `seq` order.
    /// </summary>
    internal abstract Task<List<DeltaUnit>> LoadDeltaAsync(
        string groupId,
        long afterSeq,
        long beforeSeq,
        double since,
        CancellationToken cancellationToken = default);

    /// <summary>Replace the payload with the final change set and set the status `applying`.</summary>
    internal abstract Task SaveFinalAsync(
        string groupId,
        long seq,
        long fence,
        byte[] payload,
        CancellationToken cancellationToken = default);

    /// <summary>Set the status `committed`, register the nodes for enrichment, move the watermark.</summary>
    internal abstract Task FinishCommitAsync(
        string groupId,
        long seq,
        long fence,
        List<string> nodeUuids,
        double retainSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Release the position of a unit.
    ///
    /// The call is not permitted for the status `applying`, with one exception: the
    /// reason `apply_failed`, which needs the current fence and keeps the payload.
    /// </summary>
    internal abstract Task FailAsync(
        string groupId,
        long seq,
        string reason,
        long? fence = null,
        CancellationToken cancellationToken = default);

    /// <summary>Return the nodes of the unit for which no earlier unit has an enrichment entry.</summary>
    internal abstract Task<List<string>> ReadyEnrichAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default);

    /// <summary>Delete the enrichment entries of the unit for the node UUIDs.</summary>
    internal abstract Task FinishEnrichAsync(
        string groupId,
        long seq,
        List<string> nodeUuids,
        CancellationToken cancellationToken = default);

    /// <summary>Release the resources of the store.</summary>
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

/// <summary>
/// A state store in process memory.
///
/// No lock is necessary, because each method has no `await` between its read and its
/// write. The store is correct only when one process ingests into the graph.
/// </summary>
// PORT: each Python method is an `async def` with no `await`. The C# method runs its body when it is called and returns
// a completed task, so a canceled token does not stop it, as a Python coroutine with no suspension point runs to its end.
internal sealed class InMemoryIngestionStateStore : IngestionStateStore
{
    private readonly Func<double> clock;

    // PORT: internal, because a ported test reads `_groups` (R2). Code reads it only by key (R14).
    internal Dictionary<string, Group> groups;

    // PORT: `clock: Callable[[], float] = time.time`. A delegate cannot be a default value, so null stands for
    // `time.time`, which is Py.TimeSeconds (R8).
    internal InMemoryIngestionStateStore(Func<double>? clock = null)
    {
        this.clock = clock ?? Py.TimeSeconds;
        groups = new(StringComparer.Ordinal);
    }

    private Group Group(string groupId)
    {
        var group = groups.GetValueOrDefault(groupId);
        if (group is null)
        {
            group = new Group();
            groups[groupId] = group;
        }

        return group;
    }

    private Unit LiveUnit(string groupId, long seq, bool applyingExempt = false)
    {
        var unit = Group(groupId).Units.GetValueOrDefault(seq);
        if (unit is null || StateStore.FailedStatuses.Contains(unit.Status))
        {
            throw new UnitExpiredException(groupId, seq);
        }

        if (unit.Deadline <= clock() && !(
            applyingExempt && unit.Status == StateStore.StatusApplying))
        {
            throw new UnitExpiredException(groupId, seq);
        }

        return unit;
    }

    // PORT: static, because it reads no field (analyzer CA1822).
    private static void AdvanceWatermark(Group group)
    {
        while (group.Watermark + 1 < group.NextSeq)
        {
            var unit = group.Units.GetValueOrDefault(group.Watermark + 1);
            if (unit is not null && !StateStore.TerminalStatuses.Contains(unit.Status))
            {
                break;
            }

            group.Watermark += 1;
        }
    }

    // PORT: `keep_payload` is keyword-only in Python, with no default.
    private void FailUnit(Group group, Unit unit, string reason, bool keepPayload)
    {
        unit.Status = keepPayload ? StateStore.StatusApplyFailed : StateStore.StatusFailed;
        unit.FailReason = reason;
        unit.EndedAt = clock();
        if (!keepPayload)
        {
            unit.Payload = null;
        }

        group.Enrich.Remove(unit.Seq);
        if (group.AdmitSeq == unit.Seq)
        {
            group.AdmitSeq = null;
        }

        AdvanceWatermark(group);
    }

    private void DeleteOldUnits(Group group, double retainSeconds)
    {
        var now = clock();
        var limit = group.Watermark;
        foreach (var unit in group.Units.Values)
        {
            if (!StateStore.TerminalStatuses.Contains(unit.Status) && unit.SnapshotSeq is { } snapshotSeq)
            {
                limit = Math.Min(limit, snapshotSeq);
            }
        }

        foreach (var seq in group.Units.Keys.Where(seq => seq <= limit).ToList())
        {
            var unit = group.Units[seq];
            if (
                StateStore.TerminalStatuses.Contains(unit.Status)
                && unit.EndedAt is not null
                && unit.EndedAt < now - retainSeconds
                && !(group.Enrich.GetValueOrDefault(seq) is { Count: > 0 }))
            {
                group.Units.Remove(seq);
                if (group.UnitIds.TryGetValue(unit.UnitId, out var unitSeq) && unitSeq == seq)
                {
                    group.UnitIds.Remove(unit.UnitId);
                }

                group.Enrich.Remove(seq);
            }
        }
    }

    internal override Task<Admission?> BeginAdmitAsync(
        string groupId,
        string unitId,
        double ttl,
        CancellationToken cancellationToken = default)
    {
        var group = Group(groupId);
        var now = clock();
        long? previousSeq = group.UnitIds.TryGetValue(unitId, out var found) ? found : null;
        var previous = previousSeq is { } p ? group.Units.GetValueOrDefault(p) : null;
        if (previous is not null)
        {
            if (!StateStore.IsReleased(previous.Status, previous.Deadline, now))
            {
                return Task.FromResult<Admission?>(new Admission { Seq = previous.Seq, Existing = true });
            }

            if (!StateStore.TerminalStatuses.Contains(previous.Status))
            {
                FailUnit(group, previous, StateStore.ReasonExpired, keepPayload: false);
            }
        }

        if (group.AdmitSeq is { } admitSeq)
        {
            var holder = group.Units.GetValueOrDefault(admitSeq);
            if (holder is not null && holder.Deadline > now)
            {
                return Task.FromResult<Admission?>(null);
            }

            if (holder is not null)
            {
                FailUnit(group, holder, StateStore.ReasonExpired, keepPayload: false);
            }

            group.AdmitSeq = null;
        }

        var seq = group.NextSeq;
        group.NextSeq += 1;
        group.Units[seq] = new Unit
        {
            Seq = seq,
            UnitId = unitId,
            Status = StateStore.StatusAdmitting,
            Deadline = now + ttl,
        };
        group.UnitIds[unitId] = seq;
        group.AdmitSeq = seq;
        return Task.FromResult<Admission?>(new Admission { Seq = seq, PreviousSeq = previous?.Seq });
    }

    internal override Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default)
    {
        var group = Group(groupId);
        var unit = LiveUnit(groupId, seq);
        if (unit.Status == StateStore.StatusAdmitting)
        {
            unit.Status = StateStore.StatusAdmitted;
        }

        if (group.AdmitSeq == seq)
        {
            group.AdmitSeq = null;
        }

        return Task.CompletedTask;
    }

    internal override Task<AnalysisStart> StartAnalysisAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default)
    {
        var group = Group(groupId);
        var unit = LiveUnit(groupId, seq);
        long snapshotSeq = group.Watermark;
        double startedAt = clock();
        unit.SnapshotSeq = snapshotSeq;
        unit.StartedAt = startedAt;
        return Task.FromResult(new AnalysisStart { SnapshotSeq = snapshotSeq, StartedAt = startedAt });
    }


    internal override Task SaveChangeSetAsync(
        string groupId,
        long seq,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        var unit = LiveUnit(groupId, seq, applyingExempt: true);
        if (unit.Status != StateStore.StatusAdmitted)
        {
            // A second analysis does not replace a stored payload.
            throw new StaleCommitException(groupId, seq);
        }

        unit.Payload = payload;
        unit.Status = StateStore.StatusAnalyzed;
        return Task.CompletedTask;
    }

    internal override Task HeartbeatAsync(
        string groupId,
        long seq,
        double ttl,
        long? fence = null,
        CancellationToken cancellationToken = default)
    {
        var unit = LiveUnit(groupId, seq, applyingExempt: true);
        if (StateStore.IsStaleHeartbeat(unit.Status, unit.Fence, fence))
        {
            throw new StaleCommitException(groupId, seq);
        }

        unit.Deadline = clock() + ttl;
        return Task.CompletedTask;
    }

    internal override Task<BeginCommitResult> BeginCommitAsync(
        string groupId,
        long seq,
        double ttl,
        CancellationToken cancellationToken = default)
    {
        var group = Group(groupId);
        var unit = LiveUnit(groupId, seq, applyingExempt: true);
        if (unit.Status == StateStore.StatusCommitted)
        {
            throw new StaleCommitException(groupId, seq);
        }

        var now = clock();
        for (var earlierSeq = group.Watermark + 1; earlierSeq < seq; earlierSeq++)
        {
            var earlier = group.Units.GetValueOrDefault(earlierSeq);
            if (earlier is null || StateStore.TerminalStatuses.Contains(earlier.Status))
            {
                continue;
            }

            if (earlier.Deadline > now)
            {
                return Task.FromResult<BeginCommitResult>(new CommitWait());
            }

            if (earlier.Status == StateStore.StatusApplying && earlier.Payload is { } earlierPayload)
            {
                earlier.Fence += 1;
                earlier.Deadline = now + ttl;
                return Task.FromResult<BeginCommitResult>(new CommitRecover
                {
                    Seq = earlier.Seq,
                    Fence = earlier.Fence,
                    FinalPayload = earlierPayload,
                });
            }

            FailUnit(group, earlier, StateStore.ReasonExpired, keepPayload: false);
        }

        if (unit.Status == StateStore.StatusApplying && unit.Deadline > now)
        {
            // A different attempt applies the final change set of the unit.
            return Task.FromResult<BeginCommitResult>(new CommitWait());
        }

        unit.Fence += 1;
        unit.Deadline = now + ttl;
        var finalPayload = unit.Status == StateStore.StatusApplying ? unit.Payload : null;
        return Task.FromResult<BeginCommitResult>(new CommitTurn { Fence = unit.Fence, FinalPayload = finalPayload });
    }

    internal override Task<List<DeltaUnit>> LoadDeltaAsync(
        string groupId,
        long afterSeq,
        long beforeSeq,
        double since,
        CancellationToken cancellationToken = default)
    {
        var group = Group(groupId);
        List<DeltaUnit> result = [];
        foreach (var seq in group.Units.Keys.Order().ToList())
        {
            var unit = group.Units[seq];
            if (seq >= beforeSeq || unit.Payload is null)
            {
                continue;
            }

            if (unit.Status is not (StateStore.StatusCommitted or StateStore.StatusApplyFailed))
            {
                continue;
            }

            if (seq > afterSeq || (unit.EndedAt is not null && unit.EndedAt >= since))
            {
                result.Add(new DeltaUnit { Seq = seq, Status = unit.Status, Payload = unit.Payload });
            }
        }

        return Task.FromResult(result);
    }

    private Unit CommitUnit(string groupId, long seq, long fence)
    {
        var unit = Group(groupId).Units.GetValueOrDefault(seq);
        if (unit is null || StateStore.FailedStatuses.Contains(unit.Status))
        {
            throw new UnitExpiredException(groupId, seq);
        }

        if (unit.Status == StateStore.StatusCommitted || unit.Fence != fence)
        {
            throw new StaleCommitException(groupId, seq);
        }

        if (unit.Deadline <= clock())
        {
            throw new UnitExpiredException(groupId, seq);
        }

        return unit;
    }

    internal override Task SaveFinalAsync(
        string groupId,
        long seq,
        long fence,
        byte[] payload,
        CancellationToken cancellationToken = default)
    {
        var unit = CommitUnit(groupId, seq, fence);
        unit.Payload = payload;
        unit.Status = StateStore.StatusApplying;
        return Task.CompletedTask;
    }

    internal override Task FinishCommitAsync(
        string groupId,
        long seq,
        long fence,
        List<string> nodeUuids,
        double retainSeconds,
        CancellationToken cancellationToken = default)
    {
        var group = Group(groupId);
        var unit = CommitUnit(groupId, seq, fence);
        unit.Status = StateStore.StatusCommitted;
        unit.EndedAt = clock();
        if (nodeUuids.Count > 0)
        {
            // PORT: list(dict.fromkeys(node_uuids)) keeps the first of each item in order (R14).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            group.Enrich[seq] = [.. nodeUuids.Where(seen.Add)];
        }

        AdvanceWatermark(group);
        DeleteOldUnits(group, retainSeconds);
        return Task.CompletedTask;
    }

    internal override Task FailAsync(
        string groupId,
        long seq,
        string reason,
        long? fence = null,
        CancellationToken cancellationToken = default)
    {
        var group = Group(groupId);
        var unit = group.Units.GetValueOrDefault(seq);
        if (unit is null || StateStore.TerminalStatuses.Contains(unit.Status))
        {
            return Task.CompletedTask;
        }

        if (unit.Status == StateStore.StatusApplying)
        {
            if (reason != StateStore.ReasonApplyFailed)
            {
                throw new ArgumentException(
                    "fail is not permitted for a unit with a stored final change set, "
                    + $"with the exception of the reason {Py.Repr(StateStore.ReasonApplyFailed)}");
            }

            if (fence is null || fence != unit.Fence)
            {
                throw new StaleCommitException(groupId, seq);
            }

            FailUnit(group, unit, reason, keepPayload: true);
            return Task.CompletedTask;
        }

        FailUnit(group, unit, reason, keepPayload: false);
        return Task.CompletedTask;
    }

    internal override Task<List<string>> ReadyEnrichAsync(
        string groupId,
        long seq,
        CancellationToken cancellationToken = default)
    {
        var group = Group(groupId);
        LiveUnit(groupId, seq);
        var now = clock();
        foreach (var entrySeq in group.Enrich.Keys.ToList())
        {
            var entryUnit = group.Units.GetValueOrDefault(entrySeq);
            if (entryUnit is null || entryUnit.Deadline <= now)
            {
                group.Enrich.Remove(entrySeq);
            }
        }

        var blocked = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (entrySeq, nodeUuids) in group.Enrich)
        {
            if (entrySeq < seq)
            {
                blocked.UnionWith(nodeUuids);
            }
        }

        return Task.FromResult<List<string>>([.. group.Enrich.GetValueOrDefault(seq, new List<string>()).Where(uuid => !blocked.Contains(uuid))]);
    }

    internal override Task FinishEnrichAsync(
        string groupId,
        long seq,
        List<string> nodeUuids,
        CancellationToken cancellationToken = default)
    {
        var group = Group(groupId);
        List<string> remaining = [.. group.Enrich.GetValueOrDefault(seq, new List<string>()).Where(uuid => !new HashSet<string>(nodeUuids, StringComparer.Ordinal).Contains(uuid))];
        if (remaining.Count > 0)
        {
            group.Enrich[seq] = remaining;
        }
        else
        {
            group.Enrich.Remove(seq);
        }

        return Task.CompletedTask;
    }

    internal override Task CloseAsync(CancellationToken cancellationToken = default)
    {
        groups = new(StringComparer.Ordinal);
        return Task.CompletedTask;
    }
}
