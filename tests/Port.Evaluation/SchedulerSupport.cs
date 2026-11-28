// Fakes for the evaluation tests of graphiti_core/ingestion/scheduler.py (plan 0004, layer 8, packet P8d). They are the
// C# form of the fakes of port/tools/record_scheduler.py: a TimeProvider with a virtual clock that moves only when a
// case moves it (the virtual loop clock of the recorder), a state store that yields once and then records each call, a
// tracer that records each span, and the count of fake calls with the cancel at call k (R25).
namespace Graphiti.Port.Evaluation;

using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Graphiti.Core;
using Graphiti.Core.Ingestion;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;
using static Graphiti.Port.Evaluation.PhasesValues;

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

/// <summary>
/// One run of a case of the graphiti part of the recorders (packet P9b of plan 0004, layer 9): a real GraphitiCore with
/// the fakes of <see cref="GraphitiRun"/>, the real in-memory state store with a fake clock inside a wrapper that records
/// each call, and the steps of the case on the virtual clock. Each fake call and each store call is a counted call of the
/// GraphitiRun: an attempt, the token checks, a yield, the record, the cancel at call k, and the failure by kind. The
/// GraphitiRun cancels <see cref="MainSource"/>, which cancels the main call of the case.
/// </summary>
internal sealed partial class IngestRun
{
    private const double HoldSeconds = 100.0;

    private readonly Dictionary<string, long> storeCalls = new(StringComparer.Ordinal);

    internal IngestRun(OrderedDictionary<string, object?> input, int cancelAt)
    {
        Input = Dict(PyJson.Loads(PyJson.Dumps(input)));
        Run = new GraphitiRun(Input, MainSource, cancelAt);
    }

    internal OrderedDictionary<string, object?> Input { get; }

    internal CancellationTokenSource MainSource { get; } = new();

    internal GraphitiRun Run { get; }

    internal PhasesClock Clock { get; } = new();

    internal OrderedDictionary<string, (Task<object?> Task, CancellationTokenSource Cancel)> Calls { get; } = new();

    // The final change sets that the hook before_commit_write got, in order.
    internal List<ChangeSet> Finals { get; } = [];

    // `any_placeholders` of the recorder: each UUID (each version) becomes <new:N> in the order of its first appearance.
    internal static object? Placeholders(object? value, Dictionary<string, string> known)
    {
        switch (value)
        {
            case OrderedDictionary<string, object?> d:
                {
                    var output = new OrderedDictionary<string, object?>();
                    foreach (var (key, item) in d)
                    {
                        var name = (string)Placeholders(key, known)!;
                        output[name] = Placeholders(item, known);
                    }

                    return output;
                }

            case List<object?> l:
                return l.Select(v => Placeholders(v, known)).ToList();
            case string s:
                return AnyUuid().Replace(s, m =>
                {
                    if (!known.TryGetValue(m.Value, out var name))
                    {
                        name = $"<new:{known.Count}>";
                        known[m.Value] = name;
                    }

                    return name;
                });
            default:
                return value;
        }
    }

    internal async Task StoreAsync(string method, List<object?> args, CancellationToken cancellationToken)
    {
        await Run.EnterAsync("store", new() { ["method"] = method, ["args"] = PhasesValues.Encode(args) }, cancellationToken);
        var number = storeCalls[method] = storeCalls.GetValueOrDefault(method) + 1;
        foreach (var row in Input.GetValueOrDefault("store_advance") as List<object?> ?? [])
        {
            var values = List(row);
            if ((string)values[0]! == method && (long)values[1]! == number)
            {
                Clock.Now += Convert.ToDouble(values[2], System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        var holds = (Input.GetValueOrDefault("store_hold") as OrderedDictionary<string, object?>)?.GetValueOrDefault(method) as List<object?> ?? [];
        if (holds.Contains(number))
        {
            try
            {
                await Asyncio.SleepAsync(HoldSeconds, PortServices.Time, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "store_cleanup", ["method"] = method });
                throw;
            }
        }

        var fails = (Input.GetValueOrDefault("store_fail") as OrderedDictionary<string, object?>)?.GetValueOrDefault(method) as List<object?> ?? [];
        if (fails.Contains(number))
        {
            throw new InvalidOperationException("store " + method + " failed");
        }
    }

    [GeneratedRegex("[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}")]
    private static partial Regex AnyUuid();

    /// <summary>The store of the graphiti part: the real in-memory store inside a wrapper that records each call.</summary>
    internal sealed class IngestStore(IngestRun owner, InMemoryIngestionStateStore inner) : IngestionStateStore
    {
        internal override async Task<Admission?> BeginAdmitAsync(string groupId, string unitId, double ttl, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("begin_admit", [groupId, unitId, ttl], cancellationToken);
            return await inner.BeginAdmitAsync(groupId, unitId, ttl, cancellationToken);
        }

        internal override async Task EndAdmitAsync(string groupId, long seq, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("end_admit", [groupId, seq], cancellationToken);
            await inner.EndAdmitAsync(groupId, seq, cancellationToken);
        }

        internal override async Task<AnalysisStart> StartAnalysisAsync(string groupId, long seq, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("start_analysis", [groupId, seq], cancellationToken);
            return await inner.StartAnalysisAsync(groupId, seq, cancellationToken);
        }

        internal override async Task SaveChangeSetAsync(string groupId, long seq, byte[] payload, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("save_change_set", [groupId, seq], cancellationToken);
            await inner.SaveChangeSetAsync(groupId, seq, payload, cancellationToken);
        }

        internal override async Task HeartbeatAsync(string groupId, long seq, double ttl, long? fence = null, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("heartbeat", [groupId, seq, ttl, fence], cancellationToken);
            await inner.HeartbeatAsync(groupId, seq, ttl, fence, cancellationToken);
        }

        internal override async Task<BeginCommitResult> BeginCommitAsync(string groupId, long seq, double ttl, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("begin_commit", [groupId, seq, ttl], cancellationToken);
            return await inner.BeginCommitAsync(groupId, seq, ttl, cancellationToken);
        }

        internal override async Task<List<DeltaUnit>> LoadDeltaAsync(string groupId, long afterSeq, long beforeSeq, double since, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("load_delta", [groupId, afterSeq, beforeSeq, since], cancellationToken);
            return await inner.LoadDeltaAsync(groupId, afterSeq, beforeSeq, since, cancellationToken);
        }

        internal override async Task SaveFinalAsync(string groupId, long seq, long fence, byte[] payload, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("save_final", [groupId, seq, fence], cancellationToken);
            await inner.SaveFinalAsync(groupId, seq, fence, payload, cancellationToken);
        }

        internal override async Task FinishCommitAsync(string groupId, long seq, long fence, List<string> nodeUuids, double retainSeconds, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("finish_commit", [groupId, seq, fence, nodeUuids, retainSeconds], cancellationToken);
            await inner.FinishCommitAsync(groupId, seq, fence, nodeUuids, retainSeconds, cancellationToken);
        }

        internal override async Task FailAsync(string groupId, long seq, string reason, long? fence = null, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("fail", [groupId, seq, SchedulerRun.PythonReason(reason), fence], cancellationToken);
            await inner.FailAsync(groupId, seq, reason, fence, cancellationToken);
        }

        internal override async Task<List<string>> ReadyEnrichAsync(string groupId, long seq, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("ready_enrich", [groupId, seq], cancellationToken);
            return await inner.ReadyEnrichAsync(groupId, seq, cancellationToken);
        }

        internal override async Task FinishEnrichAsync(string groupId, long seq, List<string> nodeUuids, CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("finish_enrich", [groupId, seq, nodeUuids], cancellationToken);
            await inner.FinishEnrichAsync(groupId, seq, nodeUuids, cancellationToken);
        }

        internal override async Task CloseAsync(CancellationToken cancellationToken = default)
        {
            await owner.StoreAsync("close", [], cancellationToken);
            await inner.CloseAsync(cancellationToken);
        }
    }
}

/// <summary>
/// The steps of a case of the graphiti part (`run_graphiti_case` of port/tools/record_scheduler.py), the views of the
/// results, and the checks that the test classes of `phases` and of the scheduler share.
/// </summary>
internal static class IngestCases
{
    private const int Drain = 100;

    // The kinds of events that are not a counted fake call (G_COUNTED_SKIP of the recorder).
    private static readonly HashSet<string> NotCounted = new(StringComparer.Ordinal)
    {
        "log", "session", "session_close", "span", "span_attrs", "span_status", "span_exception", "span_end", "clone",
        "store_cleanup", "store_state", "hook", "final_after_mutation",
    };

    internal static List<OrderedDictionary<string, object?>> Load(string file, string key)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "values", file);
        var document = (OrderedDictionary<string, object?>)PyJson.Loads(File.ReadAllText(path))!;
        return List(document[key]).Select(Dict).ToList();
    }

    internal static async Task<(IngestRun Run, OrderedDictionary<string, object?> Output)> RunAsync(
        OrderedDictionary<string, object?> input,
        int cancelAt,
        CancellationToken driver)
    {
        var run = new IngestRun(input, cancelAt);
        var time = new VirtualTime();
        OrderedDictionary<string, object?> output;
        using (PortServices.Use(time: time, loggerFactory: new IngestLogCapture(run.Run.Events)))
        {
            output = await LoopGuard.RunAsync(() => RunCaseAsync(run, time, driver), 30);
        }

        output["events"] = run.Run.Events;
        output["max_in_flight"] = (long)run.Run.MaxInFlight;
        output["count"] = (long)run.Run.Count;
        output["attempts"] = (long)run.Run.Attempts;
        return (run, output);
    }

    // The comparison of a recorded case: each event in order, then the whole output, then the token of each counted
    // fake call against the task that the recorder names for it (R25).
    internal static async Task CompareAsync(OrderedDictionary<string, object?> recorded, CancellationToken driver)
    {
        var input = Dict(recorded["in"]);
        var (run, output) = await RunAsync(input, -1, driver);
        var expected = new OrderedDictionary<string, object?>(Dict(recorded["out"]));
        expected.Remove("cancel");
        expected.Remove("main_first");
        var tasks = List(expected["tasks"]).Select(t => (string)t!).ToList();
        expected.Remove("tasks");
        var actual = Dict(IngestRun.Placeholders(output, []));
        var expectedEvents = List(expected["events"]);
        var actualEvents = List(actual["events"]);
        for (var i = 0; i < Math.Min(expectedEvents.Count, actualEvents.Count); i++)
        {
            Assert.True(PyJson.Dumps(expectedEvents[i]) == PyJson.Dumps(actualEvents[i]), $"event {i}\n{PyJson.Dumps(expectedEvents[i])}\n{PyJson.Dumps(actualEvents[i])}");
        }

        Assert.Equal(PyJson.Dumps(expected), PyJson.Dumps(actual));

        // A call of the case gets the token of that call. After a cancel of the call, a store call of a cleanup gets
        // CancellationToken.None (R8). A task that a phase function or the scheduler starts on its own (`<inner>`) has its
        // own token.
        Assert.Equal(tasks.Count, run.Run.Tokens.Count);
        var callTokens = run.Calls.Values.Select(c => c.Cancel.Token).ToList();
        foreach (var (i, task) in tasks.Index())
        {
            var (kind, token) = run.Run.Tokens[i];
            if (task == "<driver>")
            {
                Assert.Equal(driver, token);
            }
            else if (task == "<inner>")
            {
                Assert.True(token.CanBeCanceled && !callTokens.Contains(token), $"call {i} ({kind}): the token of a task of its own");
            }
            else
            {
                // A graph call inside a write with a time limit gets the linked token of Asyncio.WaitForAsync (R8). The
                // source of that token is disposed after the call, so the cancel points prove its link to the caller (R25).
                var call = run.Calls[task].Cancel;
                var limited = kind is "query" or "session_run" or "execute_write" or "tx_run" && token.CanBeCanceled && !callTokens.Contains(token);
                Assert.True(
                    call.Token == token || limited || (kind == "store" && token == CancellationToken.None && call.IsCancellationRequested),
                    $"call {i} ({kind}): the token of {task}\n{PyJson.Dumps(actualEvents.Select(Dict).Where(e => !NotCounted.Contains((string)e["kind"]!)).ElementAt(i))}");
            }
        }
    }

    // The point at call k: the count of attempts, the end of the main call, and the counted calls and the log lines after
    // call k (R25), with the uuids numbered from the first event after k.
    internal static async Task<(OrderedDictionary<string, object?> Expected, OrderedDictionary<string, object?> Actual)> CancelPointAsync(
        OrderedDictionary<string, object?> recorded,
        int k,
        CancellationToken driver)
    {
        var input = Dict(recorded["in"]);
        var expected = Dict(List(Dict(recorded["out"])["cancel"]).Select(Dict).Single(p => (long)p["k"]! == k));
        var (run, output) = await RunAsync(input, k, driver);
        var main = List(List(output["calls"]).Select(List).Single(c => (string)c[0]! == (string)input["main"]!)[1]);
        var actual = new OrderedDictionary<string, object?>
        {
            ["k"] = (long)k,
            ["attempts"] = (long)run.Run.Attempts,
            ["count"] = (long)run.Run.Count,
            ["error"] = (string)main[0]! == "ok" ? null : main[0],
            ["after"] = IngestRun.Placeholders(EventsAfter(run.Run.Events, k), []),
        };
        return (expected, actual);
    }

    // The kind of the difference at a cancel point, as GraphitiValuesTests names it. "same": the port equals Python.
    // "result": the cancel came at a late call, and the port returns where Python raises CancelledError. "logs": the port
    // writes more log lines. "attempts+N": the port makes N more attempts. "other": any other difference.
    internal static string CancelDifference(OrderedDictionary<string, object?> expected, OrderedDictionary<string, object?> actual)
    {
        if (PyJson.Dumps(expected) == PyJson.Dumps(actual))
        {
            return "same";
        }

        var expectedAfter = List(expected["after"]).Select(x => PyJson.Dumps(x)).ToList();
        var actualAfter = List(actual["after"]).Select(x => PyJson.Dumps(x)).ToList();

        // "cleanup": Python delivers the cancel in a cleanup await that the port runs with no token of the caller (the
        // gather in the `finally` of _admit_heartbeat, phases.py:321, and the close of the session in the `finally` of
        // execute_write_with_timeout). Python then runs the cleanup store call of the `except` branch (fail or
        // finish_enrich) with the cancel consumed. The port reaches that call with the canceled token, and it throws before
        // the record. Each other event of Python after call k is an event of the port, in its order.
        var cleanup = List(expected["after"]).Select(Dict)
            .Where(e => (string)e["kind"]! == "store" && (string)e["method"]! is "fail" or "finish_enrich").Select(e => PyJson.Dumps(e)).ToList();
        if (cleanup.Count > 0
            && (long)expected["count"]! - (long)actual["count"]! == cleanup.Count
            && !actualAfter.Intersect(cleanup).Any()
            && IsSubsequence([.. expectedAfter.Except(cleanup)], actualAfter))
        {
            if (expected["error"] is "CancelledError" && actual["error"] is "CancelledError" or null)
            {
                return "cleanup";
            }

            // "failure": the cancel came at the fake call that fails, inside a gather. In Python the cancel of the
            // caller cancels the gather, whose child fails in the same step, so the caller gets the error of that call
            // and runs the cleanup with the cancel consumed. The token of the port stays canceled.
            if (expected["error"] is string error && error != "CancelledError" && actual["error"] is "CancelledError")
            {
                return "failure";
            }
        }
        var next = 0;
        var extraKinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (item, i) in actualAfter.Select((x, i) => (x, i)))
        {
            if (next < expectedAfter.Count && item == expectedAfter[next])
            {
                next++;
            }
            else
            {
                extraKinds.Add((string)Dict(List(actual["after"])[i])["kind"]!);
            }
        }

        if (next != expectedAfter.Count || !Equals(expected["count"], actual["count"]))
        {
            return "other";
        }

        var extraAttempts = (long)actual["attempts"]! - (long)expected["attempts"]!;
        if (Equals(expected["error"], actual["error"]))
        {
            return extraAttempts > 0 ? $"attempts+{extraAttempts}"
                : extraAttempts == 0 && extraKinds.SetEquals(["log"]) ? "logs" : "other";
        }

        return expected["error"] is "CancelledError" && extraAttempts == 0 && extraKinds.Count == 0 ? "result" : "other";
    }

    private static bool IsSubsequence(List<string> part, List<string> whole)
    {
        var next = 0;
        foreach (var item in whole)
        {
            if (next < part.Count && item == part[next])
            {
                next++;
            }
        }

        return next == part.Count;
    }

    internal static TheoryData<int> Indexes(int count)
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < count; i++)
        {
            data.Add(i);
        }

        return data;
    }

    internal static TheoryData<int, int> CancelPoints(List<OrderedDictionary<string, object?>> cases)
    {
        var data = new TheoryData<int, int>();
        for (var i = 0; i < cases.Count; i++)
        {
            foreach (var point in Dict(cases[i]["out"]).GetValueOrDefault("cancel") as List<object?> ?? [])
            {
                data.Add(i, (int)(long)Dict(point)["k"]!);
            }
        }

        return data;
    }

    private static List<object?> EventsAfter(List<object?> events, int k)
    {
        var seen = 0;
        var after = new List<object?>();
        foreach (var item in events.Select(Dict))
        {
            var kind = (string)item["kind"]!;
            var counted = !NotCounted.Contains(kind);
            if (seen > k && (counted || kind == "log"))
            {
                after.Add(item);
            }

            if (counted)
            {
                seen++;
            }
        }

        return after;
    }

    private static OrderedDictionary<string, object?> UnitView(AdmittedUnit unit) => new()
    {
        ["group_id"] = unit.GroupId,
        ["unit_id"] = unit.UnitId,
        ["seq"] = unit.Seq,
        ["created_at"] = Dt(unit.CreatedAt),
        ["bulk"] = unit.Bulk,
        ["episodes"] = unit.Episodes.Select(e => (object?)GraphitiValues.EpisodeView(e)).ToList(),
        ["previous_episodes"] = unit.PreviousEpisodes.Select(p => (object?)p.Select(e => (object?)e.Uuid).ToList()).ToList(),
    };

    private static object? ChangeSetView(ChangeSet changeSet) => PyJson.Loads(Pydantic.DumpJson(changeSet.ModelDump(json: true)));

    private static OrderedDictionary<string, object?> ResultView(object? value) => value switch
    {
        AdmittedUnit unit => new() { ["unit"] = UnitView(unit) },
        ChangeSet changeSet => new() { ["change_set"] = ChangeSetView(changeSet) },
        CommitResult commit => new()
        {
            ["commit"] = new OrderedDictionary<string, object?>
            {
                ["change_set"] = ChangeSetView(commit.ChangeSet),
                ["edges"] = commit.Edges.Select(e => (object?)GraphitiValues.EdgeView(e)).ToList(),
                ["report"] = commit.Report.Select(e => (object?)new List<object?> { e.Key, e.Key.EndsWith("_ms", StringComparison.Ordinal) ? "<ms>" : e.Value }).ToList(),
            },
        },
        AddEpisodeResults result => new()
        {
            ["add_episode"] = new OrderedDictionary<string, object?>
            {
                ["episode"] = GraphitiValues.EpisodeView(result.Episode),
                ["episodic_edges"] = result.EpisodicEdges.Select(e => (object?)GraphitiValues.EpisodicEdgeView(e)).ToList(),
                ["nodes"] = result.Nodes.Select(n => (object?)GraphitiValues.NodeView(n)).ToList(),
                ["edges"] = result.Edges.Select(e => (object?)GraphitiValues.EdgeView(e)).ToList(),
                ["communities"] = (long)result.Communities.Count,
                ["community_edges"] = (long)result.CommunityEdges.Count,
            },
        },
        AddBulkEpisodeResults result => new()
        {
            ["add_episode_bulk"] = new OrderedDictionary<string, object?>
            {
                ["episodes"] = result.Episodes.Select(e => (object?)GraphitiValues.EpisodeView(e)).ToList(),
                ["episodic_edges"] = result.EpisodicEdges.Select(e => (object?)GraphitiValues.EpisodicEdgeView(e)).ToList(),
                ["nodes"] = result.Nodes.Select(n => (object?)GraphitiValues.NodeView(n)).ToList(),
                ["edges"] = result.Edges.Select(e => (object?)GraphitiValues.EdgeView(e)).ToList(),
                ["communities"] = (long)result.Communities.Count,
                ["community_edges"] = (long)result.CommunityEdges.Count,
            },
        },
        List<EntityNode> nodes => new() { ["nodes"] = nodes.Select(n => (object?)GraphitiValues.NodeView(n)).ToList() },
        _ => throw new ArgumentException("no view for " + value),
    };

    private static List<object?> CallView(Task<object?> task)
    {
        if (!task.IsCompleted)
        {
            return ["pending"];
        }

        if (task.IsCanceled)
        {
            return ["CancelledError", string.Empty];
        }

        if (task.Exception is { } faulted)
        {
            var error = faulted.InnerException!;
            return error is OperationCanceledException ? ["CancelledError", string.Empty] : [GraphitiValuesTests.PythonName(error), error.Message];
        }

        return ["ok", ResultView(task.Result)];
    }

    private static List<object?> CallsView(IngestRun run) =>
        [.. run.Calls.Select(e => (object?)new List<object?> { e.Key, CallView(e.Value.Task) })];

    private static List<object?> StoreView(InMemoryIngestionStateStore store)
    {
        var view = PhasesValues.StoreView(store);
        foreach (var group in view.Select(Dict))
        {
            foreach (var unit in List(group["units"]).Select(List))
            {
                unit[8] = SchedulerRun.PythonReason((string?)unit[8]);
            }
        }

        return view;
    }

    private static AdmittedUnit UnitOf(object? data)
    {
        var d = Dict(data);
        return new AdmittedUnit
        {
            GroupId = (string?)d.GetValueOrDefault("group_id") ?? "g",
            UnitId = (string)d["unit_id"]!,
            Seq = (long)d["seq"]!,
            CreatedAt = (PyDateTime)Decode(d["created_at"])!,
            Bulk = d.GetValueOrDefault("bulk") is true,
            Episodes = [.. List(d["episodes"]).Select(Episode)],
            PreviousEpisodes = [.. List(d["previous_episodes"]).Select(p => List(p).Select(Episode).ToList())],
        };
    }

    private static List<RawEpisode> RawEpisodes(object? value) =>
    [
        .. List(value).Select(Dict).Select(e => new RawEpisode
        {
            Name = (string)e["name"]!,
            Content = (string)e["content"]!,
            SourceDescription = (string)e["source_description"]!,
            Source = PyEnum.FromValue<EpisodeType>((string)e["source"]!),
            ReferenceTime = (PyDateTime)Decode(e["reference_time"])!,
            Uuid = (string?)e["uuid"],
        }),
    ];

    private static EpisodeType SourceOf(OrderedDictionary<string, object?> a) =>
        a.GetValueOrDefault("source") is string name ? PyEnum.FromValue<EpisodeType>(name) : EpisodeType.Message;

    private static async Task<OrderedDictionary<string, object?>> RunCaseAsync(IngestRun run, VirtualTime time, CancellationToken driver)
    {
        var input = run.Input;
        var caseConfig = Dict(input["config"]);
        var leaseSeconds = caseConfig.TryGetValue("lease_seconds", out var lease) ? Convert.ToDouble(lease, System.Globalization.CultureInfo.InvariantCulture) : 60.0;
        var inner = new InMemoryIngestionStateStore(run.Clock.Read);
        var store = new IngestRun.IngestStore(run, inner);
        var results = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var step in (input.GetValueOrDefault("setup") as List<object?> ?? []).Select(List))
        {
            switch ((string)step[0]!)
            {
                case "admit":
                    {
                        var groupId = step.Count > 2 ? (string)step[2]! : "g";
                        var admission = Py.NotNone(await inner.BeginAdmitAsync(groupId, (string)step[1]!, leaseSeconds, driver), "seq");
                        await inner.EndAdmitAsync(groupId, admission.Seq, driver);
                        break;
                    }

                case "begin_admit":
                    await inner.BeginAdmitAsync("g", (string)step[1]!, leaseSeconds, driver);
                    break;
                case "advance":
                    run.Clock.Now += (double)step[1]!;
                    break;
                case "fail":
                    await inner.FailAsync("g", (long)step[1]!, (string)step[2]!, cancellationToken: driver);
                    break;
                case "begin_commit":
                    await inner.BeginCommitAsync("g", (long)step[1]!, leaseSeconds, driver);
                    break;
                case "final":
                    {
                        var turn = (CommitTurn)await inner.BeginCommitAsync("g", (long)step[1]!, leaseSeconds, driver);
                        var final = new ChangeSet
                        {
                            GroupId = "g",
                            UnitId = (string)step[2]!,
                            Seq = (long)step[1]!,
                            SnapshotSeq = 0,
                            AnalysisStartedAt = 1000.0,
                            NodeUuids = [],
                        };
                        await inner.SaveFinalAsync("g", (long)step[1]!, turn.Fence, final.ToBytes(), driver);
                        break;
                    }

                case "committed":
                    {
                        // An earlier unit that committed its nodes and did not enrich them yet.
                        var unitId = (string)step[2]!;
                        List<string> nodeUuids = [.. List(step[3]).Cast<string>()];
                        var admission = Py.NotNone(await inner.BeginAdmitAsync("g", unitId, leaseSeconds, driver), "seq");
                        await inner.EndAdmitAsync("g", admission.Seq, driver);
                        var analysis = await inner.StartAnalysisAsync("g", admission.Seq, driver);
                        var final = new ChangeSet
                        {
                            GroupId = "g",
                            UnitId = unitId,
                            Seq = admission.Seq,
                            SnapshotSeq = analysis.SnapshotSeq,
                            AnalysisStartedAt = analysis.StartedAt,
                            NodeUuids = nodeUuids,
                        };
                        await inner.SaveChangeSetAsync("g", admission.Seq, final.ToBytes(), driver);
                        var turn = (CommitTurn)await inner.BeginCommitAsync("g", admission.Seq, leaseSeconds, driver);
                        await inner.SaveFinalAsync("g", admission.Seq, turn.Fence, final.ToBytes(), driver);
                        await inner.FinishCommitAsync("g", admission.Seq, turn.Fence, [.. nodeUuids], 300.0, driver);
                        break;
                    }

                case "analyzed":
                    {
                        // A different attempt stored the change set of the unit.
                        var seq = (long)step[1]!;
                        var analysis = await inner.StartAnalysisAsync("g", seq, driver);
                        var final = new ChangeSet
                        {
                            GroupId = "g",
                            UnitId = (string)step[2]!,
                            Seq = seq,
                            SnapshotSeq = analysis.SnapshotSeq,
                            AnalysisStartedAt = analysis.StartedAt,
                            NodeUuids = [],
                        };
                        await inner.SaveChangeSetAsync("g", seq, final.ToBytes(), driver);
                        break;
                    }

                case "change_set":
                    results[(string)step[1]!] = new ChangeSet
                    {
                        GroupId = "g",
                        UnitId = (string)step[3]!,
                        Seq = (long)step[2]!,
                        SnapshotSeq = 0,
                        AnalysisStartedAt = 1000.0,
                        NodeUuids = [],
                    };
                    break;
                case "unit":
                    results[(string)step[1]!] = UnitOf(step[2]);
                    break;
                default:
                    throw new ArgumentException((string)step[0]!);
            }
        }

        var init = new OrderedDictionary<string, object?>(input.GetValueOrDefault("init") as OrderedDictionary<string, object?> ?? new());
        var configValues = new OrderedDictionary<string, object?>(caseConfig);
        var enabledFor = configValues.GetValueOrDefault("enabled_for") as List<object?>;
        configValues.Remove("enabled_for");
        init["concurrent_ingestion"] = configValues;
        var graphiti = run.Run.MakeGraphiti(init);
        var config = graphiti.ConcurrentIngestion!;
        if (enabledFor is not null)
        {
            config.EnabledForGroup = groupId => enabledFor.Contains(groupId);
        }

        config.StateStore = store;
        var tracer = new GraphitiRun.RecTracer(run.Run);
        graphiti.Tracer = tracer;
        graphiti.Clients.Tracer = tracer;
        var hooks = input.GetValueOrDefault("hooks") as OrderedDictionary<string, object?> ?? new();
        if (hooks.GetValueOrDefault("before_commit_write") is string commitHook)
        {
            config.BeforeCommitWrite = (final, _) =>
            {
                run.Run.Events.Add(new OrderedDictionary<string, object?>
                {
                    ["kind"] = "hook",
                    ["name"] = "before_commit_write",
                    ["seq"] = final.Seq,
                    ["nodes"] = final.CreateNodes.Select(n => (object?)n.Uuid).ToList(),
                });
                run.Finals.Add(final);
                return commitHook == "reject" ? Task.FromException<ChangeSet>(new ArgumentException("rejected by the hook")) : Task.FromResult(final);
            };
        }

        if (hooks.GetValueOrDefault("before_enrichment_write") is string enrichHook)
        {
            config.BeforeEnrichmentWrite = (nodes, unit, _) =>
            {
                run.Run.Events.Add(new OrderedDictionary<string, object?>
                {
                    ["kind"] = "hook",
                    ["name"] = "before_enrichment_write",
                    ["seq"] = unit.Seq,
                    ["nodes"] = nodes.Select(n => (object?)n.Uuid).ToList(),
                });
                return Task.FromResult(enrichHook == "drop" ? [] : nodes);
            };
        }

        string Progress() => PyJson.Dumps(new List<object?> { run.Calls.Select(c => (object?)new List<object?> { c.Key, c.Value.Task.IsCompleted }).ToList(), (long)run.Run.Events.Count });

        async Task DrainAsync()
        {
            var last = string.Empty;
            var same = 0;
            while (same < 3)
            {
                for (var i = 0; i < Drain; i++)
                {
                    await Task.Yield();
                }

                var now = Progress();
                same = now == last ? same + 1 : 0;
                last = now;
            }
        }

        object? ResultOf(object? key)
        {
            var name = (string)key!;
            return results.TryGetValue(name, out var value) ? value : run.Calls[name].Task.Result;
        }

        // A call that reads the result of an earlier call starts only when that call returned (a cancel point can end the
        // earlier call).
        bool Ready(params object?[] keys) =>
            keys.All(k => results.ContainsKey((string)k!) || run.Calls[(string)k!].Task.IsCompletedSuccessfully);

        void Start(string key, Func<CancellationToken, Task<object?>> body)
        {
            var task = Asyncio.CreateTask(body, out var cancel);
            if (key == (string)input["main"]!)
            {
                // The GraphitiRun cancels MainSource at call k, as the recorder cancels the task of the main call.
                run.MainSource.Token.Register(cancel.Cancel);
            }

            run.Calls[key] = (task, cancel);
        }

        foreach (var action in List(input["actions"]).Select(List))
        {
            var op = (string)action[0]!;
            var a = action.Count > 2 && action[2] is OrderedDictionary<string, object?> args ? DecodeDict(args) : new OrderedDictionary<string, object?>();
            switch (op)
            {
                case "drain":
                    await DrainAsync();
                    break;
                case "loop":
                    time.Advance((double)action[1]!);
                    break;
                case "clock":
                    run.Clock.Now += (double)action[1]!;
                    break;
                case "store":
                    run.Run.Events.Add(new OrderedDictionary<string, object?> { ["kind"] = "store_state", ["store"] = StoreView(inner) });
                    break;
                case "cancel":
                    run.Calls[(string)action[1]!].Cancel.Cancel();
                    break;
                case "fail_unit":
                    // An earlier unit of the setup fails (no record).
                    await inner.FailAsync("g", (long)action[1]!, (string)action[2]!, cancellationToken: driver);
                    break;
                case "finish_enrich":
                    // The earlier unit of the setup ends its enrichment (no record).
                    await inner.FinishEnrichAsync("g", (long)action[1]!, [.. List(action[2]).Cast<string>()], driver);
                    break;
                case "arm":
                    run.Run.Fails[(string)action[1]!] = (long)action[2]!;
                    break;
                case "final_after_mutation":
                    {
                        // A change of the lists of a result does not change the final change set (scheduler.py:259, :335).
                        switch (run.Calls[(string)action[1]!].Task.Result)
                        {
                            case AddEpisodeResults one:
                                one.EpisodicEdges.Clear();
                                one.Nodes.Clear();
                                one.Edges.Clear();
                                break;
                            case AddBulkEpisodeResults many:
                                many.EpisodicEdges.Clear();
                                many.Nodes.Clear();
                                many.Edges.Clear();
                                many.Episodes.Clear();
                                break;
                        }

                        run.Run.Events.Add(new OrderedDictionary<string, object?>
                        {
                            ["kind"] = "final_after_mutation",
                            ["finals"] = run.Finals.Select(f => (object?)new List<object?> { (long)f.Episodes.Count, (long)f.EpisodicEdges.Count, (long)f.CreateEdges.Count }).ToList(),
                        });
                        break;
                    }

                case "add":
                    Start((string)action[1]!, async token => await graphiti.AddEpisodeAsync(
                        (string)a["name"]!,
                        (string)a["episode_body"]!,
                        (string)a["source_description"]!,
                        (PyDateTime)a["reference_time"]!,
                        SourceOf(a),
                        (string?)a.GetValueOrDefault("group_id"),
                        (string?)a.GetValueOrDefault("uuid"),
                        a.GetValueOrDefault("update_communities") is true,
                        GraphitiValues.Types(a.GetValueOrDefault("entity_types")),
                        GraphitiValues.Strings(a.GetValueOrDefault("excluded_entity_types")),
                        GraphitiValues.Strings(a.GetValueOrDefault("previous_episode_uuids")),
                        GraphitiValues.Types(a.GetValueOrDefault("edge_types")),
                        GraphitiValues.TypeMap(a.GetValueOrDefault("edge_type_map")),
                        (string?)a.GetValueOrDefault("custom_extraction_instructions"),
                        GraphitiValues.SagaArg(a.GetValueOrDefault("saga")),
                        (string?)a.GetValueOrDefault("saga_previous_episode_uuid"),
                        GraphitiValues.Identity(a.GetValueOrDefault("identity_properties")),
                        token));
                    break;
                case "add_bulk":
                    Start((string)action[1]!, async token => await graphiti.AddEpisodeBulkAsync(
                        RawEpisodes(a["bulk_episodes"]),
                        (string?)a.GetValueOrDefault("group_id"),
                        GraphitiValues.Types(a.GetValueOrDefault("entity_types")),
                        GraphitiValues.Strings(a.GetValueOrDefault("excluded_entity_types")),
                        GraphitiValues.Types(a.GetValueOrDefault("edge_types")),
                        GraphitiValues.TypeMap(a.GetValueOrDefault("edge_type_map")),
                        a.GetValueOrDefault("strict_edge_types") is true,
                        (string?)a.GetValueOrDefault("custom_extraction_instructions"),
                        GraphitiValues.SagaArg(a.GetValueOrDefault("saga")),
                        a.GetValueOrDefault("use_combined_extraction") is not false,
                        GraphitiValues.Identity(a.GetValueOrDefault("identity_properties")),
                        token));
                    break;
                case "admit":
                    Start((string)action[1]!, async token => await Phases.AdmitEpisodeAsync(
                        graphiti,
                        store,
                        config,
                        unitId: (string)a["unit_id"]!,
                        groupId: (string?)a.GetValueOrDefault("group_id"),
                        name: (string)a["name"]!,
                        episodeBody: (string)a["episode_body"]!,
                        sourceDescription: (string)a["source_description"]!,
                        referenceTime: (PyDateTime)a["reference_time"]!,
                        source: SourceOf(a),
                        uuid: (string?)a.GetValueOrDefault("uuid"),
                        previousEpisodeUuids: GraphitiValues.Strings(a.GetValueOrDefault("previous_episode_uuids")),
                        cancellationToken: token));
                    break;
                case "admit_bulk":
                    Start((string)action[1]!, async token => await Phases.AdmitBulkAsync(
                        graphiti,
                        store,
                        config,
                        unitId: (string)a["unit_id"]!,
                        groupId: (string?)a.GetValueOrDefault("group_id"),
                        bulkEpisodes: RawEpisodes(a["bulk_episodes"]),
                        cancellationToken: token));
                    break;
                case "analyze" or "analyze_bulk" or "commit" or "enrich" when !Ready([.. action.Skip(2).Take(op.StartsWith("analyze", StringComparison.Ordinal) ? 1 : 2)]):
                    break;
                case "analyze":
                case "analyze_bulk":
                    {
                        var unit = (AdmittedUnit)ResultOf(action[2])!;
                        var b = action[3] is OrderedDictionary<string, object?> more ? DecodeDict(more) : new();
                        Start((string)action[1]!, async token => op == "analyze"
                            ? await Phases.AnalyzeEpisodeAsync(
                                graphiti,
                                store,
                                config,
                                unit,
                                GraphitiValues.Types(b.GetValueOrDefault("entity_types")),
                                GraphitiValues.Strings(b.GetValueOrDefault("excluded_entity_types")),
                                GraphitiValues.Types(b.GetValueOrDefault("edge_types")),
                                GraphitiValues.TypeMap(b.GetValueOrDefault("edge_type_map")),
                                (string?)b.GetValueOrDefault("custom_extraction_instructions"),
                                GraphitiValues.Identity(b.GetValueOrDefault("identity_properties")),
                                token)
                            : await Phases.AnalyzeBulkAsync(
                                graphiti,
                                store,
                                config,
                                unit,
                                GraphitiValues.Types(b.GetValueOrDefault("entity_types")),
                                GraphitiValues.Strings(b.GetValueOrDefault("excluded_entity_types")),
                                GraphitiValues.Types(b.GetValueOrDefault("edge_types")),
                                GraphitiValues.TypeMap(b.GetValueOrDefault("edge_type_map")),
                                (string?)b.GetValueOrDefault("custom_extraction_instructions"),
                                GraphitiValues.Identity(b.GetValueOrDefault("identity_properties")),
                                b.GetValueOrDefault("strict_edge_types") is true,
                                b.GetValueOrDefault("use_combined_extraction") is not false,
                                token));
                        break;
                    }

                case "commit":
                    {
                        var unit = (AdmittedUnit)ResultOf(action[2])!;
                        var changeSet = (ChangeSet)ResultOf(action[3])!;
                        var b = action[4] is OrderedDictionary<string, object?> more ? DecodeDict(more) : new();
                        Start((string)action[1]!, async token => await Phases.CommitUnitAsync(
                            graphiti,
                            store,
                            config,
                            unit,
                            changeSet,
                            GraphitiValues.Types(b.GetValueOrDefault("entity_types")),
                            GraphitiValues.Identity(b.GetValueOrDefault("identity_properties")),
                            GraphitiValues.SagaArg(b.GetValueOrDefault("saga")),
                            (string?)b.GetValueOrDefault("saga_previous_episode_uuid"),
                            cancellationToken: token));
                        break;
                    }

                case "enrich":
                    {
                        var unit = (AdmittedUnit)ResultOf(action[2])!;
                        var commit = (CommitResult)ResultOf(action[3])!;
                        var b = action[4] is OrderedDictionary<string, object?> more ? DecodeDict(more) : new();
                        Start((string)action[1]!, async token => await Phases.EnrichUnitAsync(
                            graphiti,
                            store,
                            config,
                            unit,
                            commit,
                            GraphitiValues.Types(b.GetValueOrDefault("entity_types")),
                            cancellationToken: token));
                        break;
                    }

                default:
                    throw new ArgumentException(op);
            }
        }

        var pending = run.Calls.Where(e => !e.Value.Task.IsCompleted).Select(e => e.Key).ToList();
        foreach (var key in pending)
        {
            run.Calls[key].Cancel.Cancel();
        }

        await DrainAsync();
        return new OrderedDictionary<string, object?>
        {
            ["pending_at_end"] = pending.Select(k => (object?)k).ToList(),
            ["calls"] = CallsView(run),
            ["store"] = StoreView(inner),
        };
    }
}

/// <summary>
/// The log capture of the graphiti part: the level name, the text with its time masked, the `extra` fields of Python
/// (the values of the open scopes), and `exc: true` for a line with an exception. A `_ms` key of `ingest_report` reads the
/// system clock, so its value is "&lt;ms&gt;", as the recorder writes it.
/// </summary>
internal sealed class IngestLogCapture(List<object?> events) : ILoggerFactory, ILoggerProvider
{
    private readonly List<object?> events = events;

    private readonly AsyncLocal<ImmutableStack<object>> scopes = new();

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new Logger(this);

    public void Dispose()
    {
    }

    // The log line of a failed apply attempt names the type of the error: the .NET name in the port (decision D50). The
    // capture writes the Python name of that type (SchedulerRun.PythonReason), as the recorder writes it.
    private static string PythonTypeName(string message)
    {
        const string marker = " failed: ";
        var at = message.LastIndexOf(marker, StringComparison.Ordinal);
        if (!message.StartsWith("apply attempt ", StringComparison.Ordinal) || at < 0)
        {
            return message;
        }

        return string.Concat(message.AsSpan(0, at + marker.Length), SchedulerRun.PythonReason(message[(at + marker.Length)..]));
    }

    private sealed class Scope(IngestLogCapture owner, ImmutableStack<object> before) : IDisposable
    {
        public void Dispose() => owner.scopes.Value = before;
    }

    private sealed class Logger(IngestLogCapture owner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            var before = owner.scopes.Value ?? ImmutableStack<object>.Empty;
            owner.scopes.Value = before.Push(state);
            return new Scope(owner, before);
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var item = new OrderedDictionary<string, object?>
            {
                ["kind"] = "log",
                ["level"] = logLevel switch
                {
                    LogLevel.Debug => "DEBUG",
                    LogLevel.Information => "INFO",
                    LogLevel.Warning => "WARNING",
                    LogLevel.Error => "ERROR",
                    _ => logLevel.ToString().ToUpperInvariant(),
                },
                ["message"] = PythonTypeName(GraphitiValues.MaskMs(formatter(state, exception))),
            };
            var extra = new OrderedDictionary<string, object?>();
            foreach (var scope in (owner.scopes.Value ?? ImmutableStack<object>.Empty).Reverse())
            {
                foreach (System.Collections.DictionaryEntry entry in (System.Collections.IDictionary)scope)
                {
                    var value = GraphitiValues.Encode(entry.Value);
                    if ((string)entry.Key == "ingest_report" && value is OrderedDictionary<string, object?> report)
                    {
                        var masked = new OrderedDictionary<string, object?>();
                        foreach (var (key, item2) in report)
                        {
                            masked[key] = key.EndsWith("_ms", StringComparison.Ordinal) ? "<ms>" : item2;
                        }

                        value = masked;
                    }

                    extra[(string)entry.Key] = value;
                }
            }

            if (extra.Count > 0)
            {
                item["extra"] = extra;
            }

            if (exception is not null)
            {
                item["exc"] = true;
            }

            owner.events.Add(item);
        }
    }
}
