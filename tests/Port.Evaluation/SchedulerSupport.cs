// Fakes for the evaluation tests of graphiti_core/ingestion/scheduler.py (plan 0004, layer 8, packet P8d). They are the
// C# form of the fakes of port/tools/record_scheduler.py: a TimeProvider with a virtual clock that moves only when a
// case moves it (the virtual loop clock of the recorder), a state store that yields once and then records each call, a
// tracer that records each span, and the count of fake calls with the cancel at call k (R25).
namespace Graphiti.Port.Evaluation;

using System.Runtime.CompilerServices;
using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.Internal;
using Graphiti.PyCompat;

/// <summary>
/// A clock for asyncio.sleep and asyncio.wait_for (R8) that moves only at <see cref="Advance"/>. A timer fires in the
/// call of <see cref="Advance"/>, on the thread of the caller, in the order of its time. A timer with no delay fires on
/// the next trip through the scheduler, as a call_soon of asyncio does.
/// </summary>
internal sealed class VirtualTime : TimeProvider
{
    private readonly List<VirtualTimer> timers = [];

    private long ticks = TimeSpan.FromSeconds(1000).Ticks;

    private long created;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => ticks;

    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(ticks);

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new VirtualTimer(this, callback, state, created++);
        timer.Change(dueTime, period);
        return timer;
    }

    internal void Advance(double seconds)
    {
        var target = ticks + TimeSpan.FromSeconds(seconds).Ticks;
        while (true)
        {
            var next = timers.Where(t => t.Due <= target).OrderBy(t => t.Due).ThenBy(t => t.Order).FirstOrDefault();
            if (next is null)
            {
                break;
            }

            ticks = next.Due;
            timers.Remove(next);
            if (next.Period > TimeSpan.Zero)
            {
                next.Due += next.Period.Ticks;
                timers.Add(next);
            }

            next.Callback(next.State);
        }

        ticks = target;
    }

    private sealed class VirtualTimer(VirtualTime owner, TimerCallback callback, object? state, long order) : ITimer
    {
        internal TimerCallback Callback { get; } = callback;

        internal object? State { get; } = state;

        internal long Order { get; } = order;

        internal long Due { get; set; }

        internal TimeSpan Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            owner.timers.Remove(this);
            Period = period == Timeout.InfiniteTimeSpan ? TimeSpan.Zero : period;
            if (dueTime == Timeout.InfiniteTimeSpan)
            {
                return true;
            }

            if (dueTime <= TimeSpan.Zero)
            {
                _ = Task.Factory.StartNew(() => Callback(State), CancellationToken.None, TaskCreationOptions.None, Asyncio.Scheduler);
                return true;
            }

            Due = owner.ticks + dueTime.Ticks;
            owner.timers.Add(this);
            return true;
        }

        public void Dispose() => owner.timers.Remove(this);

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>A span that keeps its attributes and its status, as RecordingSpan of the fixtures does.</summary>
internal sealed class SchedulerSpan(string name) : TracerSpan
{
    internal string Name { get; } = name;

    internal OrderedDictionary<string, object?> Attributes { get; } = new();

    internal string? Status { get; private set; }

    internal override void AddAttributes(OrderedDictionary<string, object?> attributes)
    {
        foreach (var (key, value) in attributes)
        {
            Attributes[key] = value;
        }
    }

    internal override void SetStatus(string status, string? description = null) => Status = status;

    internal override void RecordException(Exception exception) => Status = "error";
}

/// <summary>A tracer that keeps each span and counts the spans in flight.</summary>
internal sealed class SchedulerTracer : Tracer
{
    internal List<SchedulerSpan> Spans { get; } = [];

    internal int InFlight { get; private set; }

    internal int MaxInFlight { get; private set; }

    internal override async Task<T> StartSpanAsync<T>(string name, Func<TracerSpan, Task<T>> body, CancellationToken cancellationToken = default)
    {
        var span = new SchedulerSpan(name);
        Spans.Add(span);
        InFlight++;
        MaxInFlight = Math.Max(MaxInFlight, InFlight);
        try
        {
            return await body(span);
        }
        finally
        {
            InFlight--;
        }
    }
}

/// <summary>
/// One run of a recorded case: the event list, the counted fake calls, the calls that the case started, the token of
/// each store call, and the call at which a fake cancels the main call.
/// </summary>
internal sealed class SchedulerRun(OrderedDictionary<string, object?> input, int cancelAt)
{
    private const double HoldSeconds = 100.0;

    private readonly Dictionary<string, long> storeCalls = new(StringComparer.Ordinal);

    internal OrderedDictionary<string, object?> Input { get; } = input;

    internal int CancelAt { get; } = cancelAt;

    internal List<object?> Events { get; } = [];

    internal List<(string Method, CancellationToken Token)> Tokens { get; } = [];

    // The token of each counted fake call, in the order of the records: the order of `tasks` of the recorder.
    internal List<CancellationToken> EventTokens { get; } = [];

    internal OrderedDictionary<string, (Task Task, CancellationTokenSource Cancel)> Calls { get; } = new();

    internal int Count { get; private set; }

    internal int Attempts { get; private set; }

    internal int InFlight { get; private set; }

    internal int MaxInFlight { get; private set; }

    internal PhasesClock Clock { get; } = new();

    // The common part of each counted fake call: an attempt, the token check, a yield, a second token check, the record,
    // the move of the store clock, and the cancel at call k. The fake cancels on the thread of the scheduler, at the
    // place where the Python fake calls task.cancel() (R25).
    internal async Task EnterAsync(string kind, OrderedDictionary<string, object?> record, CancellationToken cancellationToken)
    {
        Attempts++;
        InFlight++;
        MaxInFlight = Math.Max(MaxInFlight, InFlight);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            var item = new OrderedDictionary<string, object?> { ["kind"] = kind };
            foreach (var (key, value) in record)
            {
                item[key] = value;
            }

            Events.Add(item);
            EventTokens.Add(cancellationToken);
            Count++;
            foreach (var row in Input.GetValueOrDefault("store_advance") as List<object?> ?? [])
            {
                var values = (List<object?>)row!;
                if (kind == "store" && (string)values[0]! == (string)record["method"]! && (long)values[1]! > 0)
                {
                    values[1] = (long)values[1]! - 1;
                    Clock.Now += (double)values[2]!;
                }
            }

            if (Count - 1 == CancelAt)
            {
                Calls[(string)Input["main"]!].Cancel.Cancel();
            }
        }
        finally
        {
            InFlight--;
        }
    }

    internal async Task StoreAsync(string method, List<object?> args, CancellationToken cancellationToken)
    {
        Tokens.Add((method, cancellationToken));
        await EnterAsync("store", new() { ["method"] = method, ["args"] = PhasesValues.Encode(args) }, cancellationToken);
        var number = storeCalls[method] = storeCalls.GetValueOrDefault(method) + 1;
        var holds = (Input.GetValueOrDefault("store_hold") as OrderedDictionary<string, object?>)?.GetValueOrDefault(method) as List<object?> ?? [];
        if (holds.Contains(number))
        {
            // A store call that waits on the loop clock. When it is canceled, it does cleanup work on the loop clock and
            // throws the cancel again, as the wrapper of the recorder does.
            try
            {
                await Asyncio.SleepAsync(HoldSeconds, PortServices.Time, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await Asyncio.SleepAsync((double)Input["hold_cleanup"]!, PortServices.Time, CancellationToken.None);
                Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "store_cleanup", ["method"] = method });
                throw;
            }
        }

        var fails = (Input.GetValueOrDefault("store_fail") as OrderedDictionary<string, object?>)?.GetValueOrDefault(method) as List<object?> ?? [];
        if (fails.Contains(number))
        {
            throw new InvalidOperationException("store " + method + " failed");
        }
    }

    /// <summary>
    /// A failure reason that the port writes as the .NET type name of the error (decision D50), as the Python name of
    /// that type (the table of <see cref="PhasesValuesTests.PythonName"/>). A reason that is not a type name stays.
    /// </summary>
    internal static string? PythonReason(string? reason)
    {
        if (reason is null or StateStore.ReasonExpired or StateStore.ReasonApplyFailed or "analysis")
        {
            return reason;
        }

        var type = new[] { typeof(OperationCanceledException).Assembly, typeof(UnitExpiredException).Assembly }
            .SelectMany(a => a.GetTypes())
            .Single(t => t.Name == reason && typeof(Exception).IsAssignableFrom(t));
        return PhasesValuesTests.PythonName((Exception)RuntimeHelpers.GetUninitializedObject(type));
    }

    /// <summary>
    /// The store of the recording: the real in-memory store inside a wrapper that yields once and then records each call,
    /// as a store that does I/O does. The failure reason of the record is <see cref="PythonReason"/>.
    /// </summary>
    internal sealed class RecordingStore(SchedulerRun run, InMemoryIngestionStateStore inner) : IngestionStateStore
    {
        internal override async Task<Admission?> BeginAdmitAsync(string groupId, string unitId, double ttl, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("begin_admit", [groupId, unitId, ttl], cancellationToken);
            return await inner.BeginAdmitAsync(groupId, unitId, ttl, cancellationToken);
        }

        internal override async Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("end_admit", [groupId, seq], cancellationToken);
            await inner.EndAdmitAsync(groupId, seq, cancellationToken);
        }

        internal override async Task<AnalysisStart> StartAnalysisAsync(string groupId, long seq, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("start_analysis", [groupId, seq], cancellationToken);
            return await inner.StartAnalysisAsync(groupId, seq, cancellationToken);
        }

        internal override async Task SaveChangeSetAsync(string groupId, long seq, byte[] payload, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("save_change_set", [groupId, seq], cancellationToken);
            await inner.SaveChangeSetAsync(groupId, seq, payload, cancellationToken);
        }

        internal override async Task HeartbeatAsync(string groupId, long seq, double ttl, long? fence = null, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("heartbeat", [groupId, seq, ttl, fence], cancellationToken);
            await inner.HeartbeatAsync(groupId, seq, ttl, fence, cancellationToken);
        }

        internal override async Task<BeginCommitResult> BeginCommitAsync(string groupId, long seq, double ttl, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("begin_commit", [groupId, seq, ttl], cancellationToken);
            return await inner.BeginCommitAsync(groupId, seq, ttl, cancellationToken);
        }

        internal override async Task<List<DeltaUnit>> LoadDeltaAsync(string groupId, long afterSeq, long beforeSeq, double since, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("load_delta", [groupId, afterSeq, beforeSeq, since], cancellationToken);
            return await inner.LoadDeltaAsync(groupId, afterSeq, beforeSeq, since, cancellationToken);
        }

        internal override async Task SaveFinalAsync(string groupId, long seq, long fence, byte[] payload, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("save_final", [groupId, seq, fence], cancellationToken);
            await inner.SaveFinalAsync(groupId, seq, fence, payload, cancellationToken);
        }

        internal override async Task FinishCommitAsync(string groupId, long seq, long fence, List<string> nodeUuids, double retainSeconds, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("finish_commit", [groupId, seq, fence, nodeUuids, retainSeconds], cancellationToken);
            await inner.FinishCommitAsync(groupId, seq, fence, nodeUuids, retainSeconds, cancellationToken);
        }

        internal override async Task FailAsync(string groupId, long seq, string reason, long? fence = null, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("fail", [groupId, seq, PythonReason(reason), fence], cancellationToken);
            await inner.FailAsync(groupId, seq, reason, fence, cancellationToken);
        }

        internal override async Task<List<string>> ReadyEnrichAsync(string groupId, long seq, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("ready_enrich", [groupId, seq], cancellationToken);
            return await inner.ReadyEnrichAsync(groupId, seq, cancellationToken);
        }

        internal override async Task FinishEnrichAsync(string groupId, long seq, List<string> nodeUuids, CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("finish_enrich", [groupId, seq, nodeUuids], cancellationToken);
            await inner.FinishEnrichAsync(groupId, seq, nodeUuids, cancellationToken);
        }

        internal override async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            await run.StoreAsync("close", [], cancellationToken);
            await inner.CloseAsync(cancellationToken);
        }
    }
}
