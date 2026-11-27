// Ported from graphiti_core/ingestion/phases.py
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

using System.Diagnostics;
using Graphiti.Core.Driver;
using Graphiti.Core.Search;
using Graphiti.Core.Utils;
using Graphiti.Core.Utils.Maintenance;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: `WaitFn = Callable[[], Awaitable[None]]` is a type alias, so it has no C# type (R2). Each use is
// `Func<CancellationToken, Task>`: an async callable takes a CancellationToken last (R8).
// PORT: `dict[str, type[BaseModel]]` is an OrderedDictionary<string, GraphTypeDescriptor> (R15). `edge_type_map:
// dict[tuple[str, str], list[str]]` is an OrderedDictionary<(string, string), List<string>>. A commit report
// `dict[str, float]` is an OrderedDictionary<string, double>, because commit_unit spreads it into span attributes.
// PORT: `time.time()` is Py.TimeSeconds, which reads the system clock (R8). The values go only into span attributes and
// into the `*_ms` keys of a commit report.
// PORT: each private function that the recording test of the unit calls is internal (R2).
internal static partial class Phases
{
    private static ILogger Logger => PortServices.Logger(typeof(Phases));

    // PORT: a tuple of strings (Part 3).
    internal static readonly IReadOnlyList<string> ReportKeys =
    [
        "turn_wait_ms",
        "duration_ms",
        "delta.unit_count",
        "node.checked",
        "node.collided",
        "node.merged",
        "edge.checked",
        "edge.collided",
        "edge.deduplicated",
        "edge.ended",
        "repair.llm_calls",
        "repair.duration_ms",
        "repair.c3.node_merged",
        "repair.c4a.edge_removed",
        "repair.c5.edge_merged",
        "repair.c6.edge_ended",
        "operation.applied",
        "operation.dropped",
        "apply.attempts",
        "recovered.count",
        "retry",
    ];

    /// <summary>Return a commit report with each count at zero.</summary>
    // PORT: `dict.fromkeys(REPORT_KEYS, 0)`. Python stores the int 0, and the port the double 0 (R18).
    internal static OrderedDictionary<string, double> NewReport()
    {
        var report = new OrderedDictionary<string, double>();
        foreach (var key in ReportKeys)
        {
            report[key] = 0;
        }

        return report;
    }
}

/// <summary>
/// One admitted ingestion unit: its position, its episodes, and their context.
///
/// `episodes` have their content. `previous_episodes` has one list for each episode.
/// </summary>
internal sealed record AdmittedUnit
{
    internal required string GroupId { get; set; }

    internal required string UnitId { get; set; }

    internal required long Seq { get; set; }

    internal required PyDateTime CreatedAt { get; set; }

    internal bool Bulk { get; set; }

    internal required List<EpisodicNode> Episodes { get; set; }

    internal required List<List<EpisodicNode>> PreviousEpisodes { get; set; }

    internal AdmittedUnit Validate() => this;
}

/// <summary>
/// The result of a commit.
///
/// `change_set` is the final change set. `edges` are the created edges and the stored
/// edges that the commit changed. `report` has the counts of the commit.
/// </summary>
internal sealed record CommitResult
{
    internal required ChangeSet ChangeSet { get; set; }

    internal List<EntityEdge> Edges { get; set; } = [];

    internal OrderedDictionary<string, double> Report { get; set; } = new();

    internal CommitResult Validate() => this;
}

/// <summary>
/// The local deadline of a unit.
///
/// The local deadline is the time of the last successful `heartbeat` or
/// `begin_commit` call, read before the call, plus `lease_seconds`. It is thus never
/// later than the deadline in the store.
///
/// `fence` is the fence of the commit turn of this attempt. `commit_unit` sets it.
/// With a fence, a heartbeat raises `StaleCommitError` when a different attempt has
/// the turn of the unit.
/// </summary>
// PORT: the private attribute `_deadline` (a `double?`) is a field of the unit.
internal sealed class UnitLease
{
    private double? deadline;

    // PORT: `clock: Callable[[], float] = time.monotonic`. A delegate cannot be a default value, so null stands for
    // `time.monotonic`, which is the timestamp of PortServices.Time in seconds (R8).
    internal UnitLease(
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        string groupId,
        long seq,
        Func<double>? clock = null)
    {
        Store = store;
        Config = config;
        GroupId = groupId;
        Seq = seq;
        Clock = clock ?? (() => PortServices.Time.GetTimestamp() / (double)PortServices.Time.TimestampFrequency);
        Fence = null;
        deadline = null;
    }

    internal IngestionStateStore Store { get; set; }

    internal ConcurrentIngestionConfig Config { get; set; }

    internal string GroupId { get; set; }

    internal long Seq { get; set; }

    internal Func<double> Clock { get; set; }

    internal long? Fence { get; set; }

    /// <summary>Record a successful store call that set the deadline of the unit.</summary>
    internal void Renewed(double callStartedAt)
    {
        deadline = callStartedAt + Config.LeaseSeconds;
    }

    internal async Task HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        var startedAt = Clock();
        var fence = Fence;
        try
        {
            await Store.HeartbeatAsync(GroupId, Seq, Config.LeaseSeconds, fence, cancellationToken);
        }
        catch (StaleCommitException)
        {
            if (fence is not null)
            {
                throw;
            }

            // A different attempt applies the final change set of the unit. This attempt
            // waits for the turn and does not set the deadline.
            return;
        }

        Renewed(startedAt);
    }

    /// <summary>Return the time limit in seconds for a graph write that starts now.</summary>
    internal double WriteBudget()
    {
        if (deadline is null)
        {
            throw new UnitExpiredException(GroupId, Seq);
        }

        var budget = deadline.Value - Clock() - Config.WriteMarginSeconds;
        if (budget <= 0)
        {
            throw new UnitExpiredException(GroupId, Seq);
        }

        return budget;
    }
}

internal static partial class Phases
{
    private static Func<CancellationToken, Task> DefaultWait(ConcurrentIngestionConfig config)
    {
        // PORT: the nested coroutine is a local async function. `asyncio.sleep` is Asyncio.SleepAsync on PortServices.Time
        // (R8). Task.Delay refuses a delay above about 49.7 days, and asyncio.sleep has no limit.
        async Task WaitAsync(CancellationToken cancellationToken)
        {
            await Asyncio.SleepAsync(config.PollIntervalSeconds, PortServices.Time, cancellationToken);
        }

        return WaitAsync;
    }

    // PORT: internal, because ingestion/scheduler.py imports it (R2). `error: BaseException` is an Exception.

    /// <summary>Release the position of a unit that has no stored final change set.</summary>
    // PORT: `type(error).__name__` is the .NET type name of the error (decision D50, R15).
    internal static async Task FailUnitAsync(
        IngestionStateStore store,
        string groupId,
        long seq,
        Exception error,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await store.FailAsync(groupId, seq, error.GetType().Name, cancellationToken: cancellationToken);
        }
        catch (Exception e) when (!Asyncio.IsCancellation(e, cancellationToken))
        {
            // The unit has a stored final change set, or the store has no access. The
            // deadline of the unit then releases its position.
            Log.CouldNotMarkUnitFailed(Logger, seq, groupId);
        }
    }

    internal static OrderedDictionary<(string, string), List<string>> DefaultEdgeTypeMap(
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes)
    {
        if (edgeTypes is not null)
        {
            return new() { [("Entity", "Entity")] = [.. edgeTypes.Keys] };
        }

        return new() { [("Entity", "Entity")] = [] };
    }

    // PORT: no annotation for the result. TYPE_HINTS.md gives Unknown, and the value is a copy of the episode (R20).

    /// <summary>Return the copy of an episode that a graph write must contain.</summary>
    internal static EpisodicNode WrittenEpisode(GraphitiCore graphiti, EpisodicNode episode, List<string> entityEdges)
    {
        var written = episode.ModelCopy(deep: true);

        // PORT: `list(dict.fromkeys(entity_edges))` keeps the first of each UUID in order (R14).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        written.EntityEdges = [.. entityEdges.Where(seen.Add)];
        if (!graphiti.StoreRawEpisodeContent)
        {
            written.Content = string.Empty;
        }

        return written;
    }

    // PORT: `driver: Any`. Each caller passes the GraphDriver of `_resolve_request_scope` (R20).

    /// <summary>Raise an error before the admission when the driver cannot write a commit.</summary>
    internal static void CheckDriver(GraphDriver driver)
    {
        var missing = BulkUtils.MissingIngestionInterfaceOperations(driver);
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                "concurrent ingestion needs these operations of graph_operations_interface, "
                + "and the interface of the driver does not have them: " + string.Join(", ", missing));
        }
    }

    internal static async Task<Admission> BeginAdmitAsync(
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        string groupId,
        string unitId,
        Func<CancellationToken, Task> wait,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            // A store call in a worker thread completes after a cancellation of the caller.
            // The call is thus shielded, and a cancelled caller releases the unit.
            var call = Asyncio.CreateTask(
                token => store.BeginAdmitAsync(groupId, unitId, config.LeaseSeconds, token),
                out var cancelCall);
            Admission? admission;
            try
            {
                try
                {
                    admission = await Asyncio.ShieldAsync(call, cancellationToken);
                }
                catch (OperationCanceledException error)
                {
                    // PORT: `contextlib.suppress(Exception)`. The cancellation arrived, so each call here passes
                    // CancellationToken.None (R8).
                    try
                    {
                        admission = await call;
                        if (admission is not null)
                        {
                            await FailAdmissionAsync(store, groupId, admission, error, CancellationToken.None);
                        }
                    }
                    catch (Exception)
                    {
                        // PORT: contextlib.suppress(Exception)
                    }

                    throw;
                }
            }
            finally
            {
                // PORT: the source of the token of the task (R8). No code cancels it.
                cancelCall.Dispose();
            }

            if (admission is null)
            {
                await wait(cancellationToken);
                continue;
            }

            if (admission.Existing)
            {
                // A different attempt has the unit. This attempt writes nothing, and it calls
                // no store method that changes state.
                throw new UnitInProgressException(groupId, unitId, admission.Seq);
            }

            return admission;
        }
    }

    // PORT: an @asynccontextmanager is a method that returns an IAsyncDisposable, used with `await using` (R16). The
    // code before `yield` starts the heartbeat task, and the code in `finally` runs in DisposeAsync. The result type is
    // the class that implements it: analyzer CA1859 fails the build for a private method that returns the interface.

    /// <summary>
    /// Renew the deadline of a unit while its admission reads and writes the graph.
    ///
    /// The caller of the Admit phase does not have the `seq` before the phase returns, so
    /// the phase renews the deadline itself.
    /// </summary>
    private static HeartbeatScope AdmitHeartbeat(
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        string groupId,
        Admission admission)
    {
        // PORT: the nested coroutine is a local async function. It gets the token of its task (R8).
        async Task BeatAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                await Asyncio.SleepAsync(config.LeaseSeconds / 3, PortServices.Time, cancellationToken);
                try
                {
                    await store.HeartbeatAsync(groupId, admission.Seq, config.LeaseSeconds, cancellationToken: cancellationToken);
                }
                catch (Exception error) when (error is UnitExpiredException or StaleCommitException)
                {
                    // The unit is expired or failed. `end_admit` raises the error.
                    return;
                }
                catch (Exception error) when (!Asyncio.IsCancellation(error, cancellationToken))
                {
                    Log.AdmissionHeartbeatFailed(Logger, error, admission.Seq, groupId);
                }
            }
        }

        var task = Asyncio.CreateTask(BeatAsync, out var cancel);
        return new HeartbeatScope(task, cancel);
    }

    // PORT: the context manager object of `_admit_heartbeat`. R16 gives the IAsyncDisposable, and C# has no anonymous
    // implementation of an interface, so this class holds the task and the code of the `finally` block.
    private sealed class HeartbeatScope(Task task, CancellationTokenSource cancel) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            cancel.Cancel();
            await Asyncio.GatherAsync([task], returnExceptions: true);
            cancel.Dispose();
        }
    }

    /// <summary>
    /// Return the UUID of an episode that a unit writes.
    ///
    /// The UUID is a function of the `unit_id`. A retry of the admission thus writes the
    /// same episode node, and not one more node.
    /// </summary>
    internal static string EpisodeUuid(string groupId, string unitId, long index = 0)
    {
        return Py.Uuid5(Py.NamespaceUrl, "graphiti:ingest:" + groupId + ":" + unitId + ":" + Py.Str(index));
    }

    // PORT: `driver: Any` is the GraphDriver of the caller (R20). Code reads the result only by key (R14).

    /// <summary>
    /// Return the episode nodes of the `unit_id` that are in the graph.
    ///
    /// The read does not use the state store: the store can have no record of a unit that
    /// wrote its episode nodes. The admission writes only the nodes that this read does
    /// not return, so it does not change a stored node or its `entity_edges`.
    /// </summary>
    internal static async Task<Dictionary<string, EpisodicNode>> WrittenEpisodesAsync(
        GraphDriver driver,
        List<string> episodeUuids,
        string groupId,
        CancellationToken cancellationToken = default)
    {
        if (episodeUuids.Count == 0)
        {
            return new(StringComparer.Ordinal);
        }

        var episodes = await EpisodicNode.GetByUuidsAsync(driver, episodeUuids, groupId: groupId, cancellationToken: cancellationToken);
        var written = new Dictionary<string, EpisodicNode>(StringComparer.Ordinal);
        foreach (var episode in episodes)
        {
            written[episode.Uuid] = episode;
        }

        return written;
    }

    internal static async Task FailAdmissionAsync(
        IngestionStateStore store,
        string groupId,
        Admission admission,
        Exception error,
        CancellationToken cancellationToken = default)
    {
        // A unit that a different attempt admitted keeps its position.
        if (!admission.Existing)
        {
            await FailUnitAsync(store, groupId, admission.Seq, error, cancellationToken);
        }
    }

    // PORT: the parameters after `config` are keyword-only in Python.

    /// <summary>
    /// Admit one episode: get its sequence number and make the episode node visible.
    ///
    /// The store gives the sequence number with the admit lease. With the lease, the
    /// function reads the previous episodes and writes the episode node with an empty
    /// `entity_edges` list. With `uuid`, the function loads the episode and does not
    /// write it.
    ///
    /// When the `unit_id` has a live unit, the function raises `UnitInProgressError`. It
    /// then writes nothing, and it calls no store method that changes state.
    /// </summary>
    internal static async Task<AdmittedUnit> AdmitEpisodeAsync(
        GraphitiCore graphiti,
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        string unitId,
        string? groupId,
        string name,
        string episodeBody,
        string sourceDescription,
        PyDateTime referenceTime,
        EpisodeType source = EpisodeType.Message,
        string? uuid = null,
        List<string>? previousEpisodeUuids = null,
        Func<CancellationToken, Task>? wait = null,
        CancellationToken cancellationToken = default)
    {
        var (resolvedGroupId, driver, clients) = graphiti.ResolveRequestScope(groupId);
        CheckDriver(driver);
        var waitFn = wait ?? DefaultWait(config);

        return await graphiti.Tracer.StartSpanAsync(
            "ingest.admit",
            async span =>
            {
                var start = Py.TimeSeconds();
                var admission = await BeginAdmitAsync(store, config, resolvedGroupId, unitId, waitFn, cancellationToken);
                var seq = admission.Seq;
                var admittedAt = Py.TimeSeconds();
                PyDateTime now;
                EpisodicNode episode;
                List<EpisodicNode> previousEpisodes;
                try
                {
                    await using (AdmitHeartbeat(store, config, resolvedGroupId, admission))
                    {
                        now = DatetimeUtils.UtcNow();
                        previousEpisodes = previousEpisodeUuids is null
                            ? await graphiti.RetrieveEpisodesAsync(
                                referenceTime,
                                lastN: SearchUtils.RelevantSchemaLimit,
                                groupIds: [resolvedGroupId],
                                source: source,
                                driver: driver,
                                cancellationToken: cancellationToken)
                            : await EpisodicNode.GetByUuidsAsync(
                                driver,
                                previousEpisodeUuids,
                                groupId: resolvedGroupId,
                                requireAll: true,
                                cancellationToken: cancellationToken);
                        if (uuid is not null)
                        {
                            episode = await EpisodicNode.GetByUuidAsync(driver, uuid, groupId: resolvedGroupId, cancellationToken: cancellationToken);
                        }
                        else
                        {
                            var episodeUuid = EpisodeUuid(resolvedGroupId, unitId);
                            var written = await WrittenEpisodesAsync(driver, [episodeUuid], resolvedGroupId, cancellationToken);
                            if (written.TryGetValue(episodeUuid, out var stored))
                            {
                                // The node is in the graph, and the admission does not change it.
                                // The unit keeps the content in memory.
                                episode = stored;
                                episode.Content = episodeBody;
                                now = episode.CreatedAt;
                            }
                            else
                            {
                                episode = new EpisodicNode
                                {
                                    Uuid = episodeUuid,
                                    Name = name,
                                    GroupId = resolvedGroupId,
                                    Labels = [],
                                    Source = source,
                                    Content = episodeBody,
                                    SourceDescription = sourceDescription,
                                    CreatedAt = now,
                                    ValidAt = referenceTime,
                                }.Validate();
                                await BulkUtils.AddNodesAndEdgesBulkAsync(
                                    driver,
                                    [WrittenEpisode(graphiti, episode, [])],
                                    [],
                                    [],
                                    [],
                                    clients.Embedder,
                                    cancellationToken: cancellationToken);
                            }
                        }

                        // After a retry, the node of the unit is in the graph. It is not its own context.
                        var episodeUuidValue = episode.Uuid;
                        previousEpisodes = [.. previousEpisodes.Where(previous => previous.Uuid != episodeUuidValue)];
                        await store.EndAdmitAsync(resolvedGroupId, seq, cancellationToken);
                    }
                }
                catch (Exception error)
                {
                    // PORT: `except BaseException` also catches a cancellation. A Python cancel arrives one time, so after a
                    // cancel the cleanup runs to its end with CancellationToken.None. After another error, a later cancel
                    // of the caller reaches the cleanup call, so it gets the token of the caller (R8).
                    await FailAdmissionAsync(store, resolvedGroupId, admission, error, Asyncio.IsCancellation(error, cancellationToken) ? CancellationToken.None : cancellationToken);
                    throw;
                }

                span.AddAttributes(new OrderedDictionary<string, object?>
                {
                    ["ingest.seq"] = seq,
                    ["wait_ms"] = (admittedAt - start) * 1000,
                    ["duration_ms"] = (Py.TimeSeconds() - start) * 1000,
                });
                return new AdmittedUnit
                {
                    GroupId = resolvedGroupId,
                    UnitId = unitId,
                    Seq = seq,
                    CreatedAt = now,
                    Episodes = [episode],
                    PreviousEpisodes = [[.. previousEpisodes]],
                }.Validate();
            },
            cancellationToken);
    }

    // PORT: the parameters after `config` are keyword-only in Python.

    /// <summary>
    /// Admit one bulk call as one unit with one sequence number.
    ///
    /// The function writes each new episode node and then reads the previous episodes of
    /// each episode, in the order of the bulk call of today.
    ///
    /// When the `unit_id` has a live unit, the function raises `UnitInProgressError`. It
    /// then writes nothing, and it calls no store method that changes state.
    /// </summary>
    internal static async Task<AdmittedUnit> AdmitBulkAsync(
        GraphitiCore graphiti,
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        string unitId,
        string? groupId,
        List<RawEpisode> bulkEpisodes,
        Func<CancellationToken, Task>? wait = null,
        CancellationToken cancellationToken = default)
    {
        var (resolvedGroupId, driver, clients) = graphiti.ResolveRequestScope(groupId);
        CheckDriver(driver);
        var waitFn = wait ?? DefaultWait(config);

        return await graphiti.Tracer.StartSpanAsync(
            "ingest.admit",
            async span =>
            {
                var start = Py.TimeSeconds();
                var admission = await BeginAdmitAsync(store, config, resolvedGroupId, unitId, waitFn, cancellationToken);
                var seq = admission.Seq;
                var admittedAt = Py.TimeSeconds();
                PyDateTime now;
                List<EpisodicNode> episodes;
                List<(EpisodicNode, List<EpisodicNode>)> episodeContext;
                try
                {
                    await using (AdmitHeartbeat(store, config, resolvedGroupId, admission))
                    {
                        now = DatetimeUtils.UtcNow();
                        episodes = [];
                        List<EpisodicNode> newEpisodes = [];
                        var written = await WrittenEpisodesAsync(
                            driver,
                            [
                                .. bulkEpisodes
                                    .Index()
                                    .Where(item => item.Item.Uuid is null)
                                    .Select(item => EpisodeUuid(resolvedGroupId, unitId, item.Index)),
                            ],
                            resolvedGroupId,
                            cancellationToken);
                        foreach (var (index, rawEpisode) in bulkEpisodes.Index())
                        {
                            if (rawEpisode.Uuid is not null)
                            {
                                episodes.Add(
                                    await EpisodicNode.GetByUuidAsync(
                                        driver, rawEpisode.Uuid, groupId: resolvedGroupId, cancellationToken: cancellationToken));
                                continue;
                            }

                            var episodeUuid = EpisodeUuid(resolvedGroupId, unitId, index);
                            if (written.TryGetValue(episodeUuid, out var stored))
                            {
                                // The node is in the graph, and the admission does not change it.
                                stored.Content = rawEpisode.Content;
                                episodes.Add(stored);
                                continue;
                            }

                            var episode = new EpisodicNode
                            {
                                Uuid = episodeUuid,
                                Name = rawEpisode.Name,
                                Labels = [],
                                Source = rawEpisode.Source,
                                Content = rawEpisode.Content,
                                SourceDescription = rawEpisode.SourceDescription,
                                GroupId = resolvedGroupId,
                                CreatedAt = now,
                                ValidAt = rawEpisode.ReferenceTime,
                            }.Validate();
                            episodes.Add(episode);
                            newEpisodes.Add(episode);
                        }

                        if (newEpisodes.Count > 0)
                        {
                            await BulkUtils.AddNodesAndEdgesBulkAsync(
                                driver,
                                [.. newEpisodes.Select(episode => WrittenEpisode(graphiti, episode, []))],
                                [],
                                [],
                                [],
                                clients.Embedder,
                                cancellationToken: cancellationToken);
                        }

                        episodeContext = await BulkUtils.RetrievePreviousEpisodesBulkAsync(
                            driver, episodes, maxCoroutines: graphiti.MaxCoroutines, cancellationToken: cancellationToken);
                        await store.EndAdmitAsync(resolvedGroupId, seq, cancellationToken);
                    }
                }
                catch (Exception error)
                {
                    // PORT: `except BaseException` also catches a cancellation. A Python cancel arrives one time, so after a
                    // cancel the cleanup runs to its end with CancellationToken.None. After another error, a later cancel
                    // of the caller reaches the cleanup call, so it gets the token of the caller (R8).
                    await FailAdmissionAsync(store, resolvedGroupId, admission, error, Asyncio.IsCancellation(error, cancellationToken) ? CancellationToken.None : cancellationToken);
                    throw;
                }

                span.AddAttributes(new OrderedDictionary<string, object?>
                {
                    ["ingest.seq"] = seq,
                    ["episode.count"] = (long)episodes.Count,
                    ["wait_ms"] = (admittedAt - start) * 1000,
                    ["duration_ms"] = (Py.TimeSeconds() - start) * 1000,
                });
                return new AdmittedUnit
                {
                    GroupId = resolvedGroupId,
                    UnitId = unitId,
                    Seq = seq,
                    CreatedAt = now,
                    Bulk = true,
                    Episodes = [.. episodeContext.Select(context => context.Item1)],
                    PreviousEpisodes = [.. episodeContext.Select(context => new List<EpisodicNode>(context.Item2))],
                }.Validate();
            },
            cancellationToken);
    }

    // PORT: `records: list[Any]`. The callers pass the created nodes or the created edges, so there is one overload
    // for each type (R15, as for `_enum_value`). `unique.setdefault(...)` keeps the first record of each UUID.
    internal static List<EntityNode> UniqueByUuid(List<EntityNode> records)
    {
        var unique = new OrderedDictionary<string, EntityNode>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            unique.TryAdd(record.Uuid, record);
        }

        return [.. unique.Values];
    }

    internal static List<EntityEdge> UniqueByUuid(List<EntityEdge> records)
    {
        var unique = new OrderedDictionary<string, EntityEdge>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            unique.TryAdd(record.Uuid, record);
        }

        return [.. unique.Values];
    }

    // PORT: `span: Any` is the TracerSpan of `start_span` (R16). Internal, because the recording test calls it (R2).
    internal static async Task SaveAnalysisAsync(
        GraphitiCore graphiti,
        IngestionStateStore store,
        AdmittedUnit unit,
        ChangeSet changeSet,
        GraphitiClients clients,
        TracerSpan span,
        double start,
        CancellationToken cancellationToken = default)
    {
        _ = graphiti;

        // A node that the unit creates gets its name embedding here, not in the commit.
        foreach (var node in changeSet.CreateNodes)
        {
            if (node.NameEmbedding is null)
            {
                await node.GenerateNameEmbeddingAsync(clients.Embedder, cancellationToken);
            }
        }

        await store.SaveChangeSetAsync(unit.GroupId, unit.Seq, changeSet.ToBytes(), cancellationToken);
        span.AddAttributes(new OrderedDictionary<string, object?>
        {
            ["ingest.seq"] = unit.Seq,
            ["snapshot_seq"] = changeSet.SnapshotSeq,
            ["duration_ms"] = (Py.TimeSeconds() - start) * 1000,
            ["create_node.count"] = (long)changeSet.CreateNodes.Count,
            ["create_edge.count"] = (long)changeSet.CreateEdges.Count,
            ["operation.count"] = (long)changeSet.Operations.Count,
        });
    }

    // PORT: the parameters after `unit` are keyword-only in Python.

    /// <summary>
    /// Run extraction and resolution for one episode and store the change set.
    ///
    /// The function writes nothing to the graph. It records the snapshot before its first
    /// graph read. It extracts identity attributes when identity properties are configured.
    /// </summary>
    internal static async Task<ChangeSet> AnalyzeEpisodeAsync(
        GraphitiCore graphiti,
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        AdmittedUnit unit,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        List<string>? excludedEntityTypes = null,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes = null,
        OrderedDictionary<(string, string), List<string>>? edgeTypeMap = null,
        string? customExtractionInstructions = null,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        _ = config;
        var (groupId, _, clients) = graphiti.ResolveRequestScope(unit.GroupId);
        var episode = unit.Episodes[0];
        var previousEpisodes = unit.PreviousEpisodes[0];

        return await graphiti.Tracer.StartSpanAsync(
            "ingest.analyze",
            async span =>
            {
                var start = Py.TimeSeconds();
                ChangeSet changeSet;
                try
                {
                    var analysis = await store.StartAnalysisAsync(groupId, unit.Seq, cancellationToken);
                    var trace = new ResolutionTrace();

                    var (extractedNodes, nodeEpisodeIndexMap) = await NodeOperations.ExtractNodesAsync(
                        clients,
                        new EpisodeOrEpisodes.One { Episode = episode },
                        previousEpisodes,
                        entityTypes,
                        excludedEntityTypes,
                        customExtractionInstructions,
                        cancellationToken);
                    List<EntityNode> identityNodes = identityProperties is { Count: > 0 }
                        ? [.. extractedNodes.Where(node => DedupHelpers.IdentityTypeForNode(node, identityProperties) is not null)]
                        : [];
                    if (identityNodes.Count > 0)
                    {
                        await NodeOperations.ExtractAttributesOnlyFromNodesAsync(
                            clients,
                            identityNodes,
                            new EpisodeOrEpisodes.One { Episode = episode },
                            previousEpisodes,
                            entityTypes,
                            validateShape: false,
                            cancellationToken: cancellationToken);
                    }

                    var (nodes, uuidMap, _) = await NodeOperations.ResolveExtractedNodesAsync(
                        clients,
                        extractedNodes,
                        episode,
                        previousEpisodes,
                        entityTypes,
                        identityProperties: identityProperties,
                        trace: trace,
                        cancellationToken: cancellationToken);
                    var (resolvedEdges, invalidatedEdges, newEdges) = await graphiti.ExtractAndResolveEdgesAsync(
                        new EpisodeOrEpisodes.One { Episode = episode },
                        extractedNodes,
                        previousEpisodes,
                        edgeTypeMap is { Count: > 0 } ? edgeTypeMap : DefaultEdgeTypeMap(edgeTypes),
                        groupId,
                        edgeTypes,
                        nodes,
                        uuidMap,
                        customExtractionInstructions: customExtractionInstructions,
                        entityTypes: entityTypes,
                        identityProperties: identityProperties,
                        clients: clients,
                        trace: trace,
                        cancellationToken: cancellationToken);

                    nodes = UniqueByUuid(nodes);
                    var nodeFirstEpisode = new OrderedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var node in nodes)
                    {
                        nodeFirstEpisode[node.Uuid] = episode.Uuid;
                    }

                    // PORT: pydantic copies each dict value at construction (R7). The port passes the dicts of the trace,
                    // which no code uses after this call.
                    changeSet = new ChangeSet
                    {
                        GroupId = groupId,
                        UnitId = unit.UnitId,
                        Seq = unit.Seq,
                        SnapshotSeq = analysis.SnapshotSeq,
                        AnalysisStartedAt = analysis.StartedAt,
                        Episodes =
                        [
                            WrittenEpisode(
                                graphiti,
                                episode,
                                [.. resolvedEdges.Concat(invalidatedEdges).Select(edge => edge.Uuid)]),
                        ],
                        CreateNodes = [.. nodes.Where(node => !trace.StoredNodeUuids.Contains(node.Uuid))],
                        CreateEdges = UniqueByUuid(newEdges),
                        EpisodicEdges = EdgeOperations.BuildEpisodicEdges(
                            nodes, [episode.Uuid], unit.CreatedAt, nodeEpisodeIndexMap),
                        Operations = [.. trace.Operations],
                        NodeUuids = [.. nodes.Select(node => node.Uuid)],
                        NodeFirstEpisode = nodeFirstEpisode,
                        SeenNodeCandidates = trace.SeenNodeCandidates,
                        SeenNodeLabels = trace.SeenNodeLabels,
                        EdgesEndedByCandidate = [.. trace.EndedByCandidate.Order(Py.StringComparer)],
                        SeenDuplicateCandidates = trace.SeenDuplicateCandidates,
                        SeenInvalidationCandidates = trace.SeenInvalidationCandidates,
                    }.Validate();
                    await SaveAnalysisAsync(graphiti, store, unit, changeSet, clients, span, start, cancellationToken);
                }
                catch (StaleCommitException)
                {
                    // The unit has a stored change set from a different attempt. It keeps its
                    // position.
                    throw;
                }
                catch (Exception error)
                {
                    // PORT: `except BaseException` also catches a cancellation. A Python cancel arrives one time, so after a
                    // cancel the cleanup runs to its end with CancellationToken.None. After another error, a later cancel
                    // of the caller reaches the cleanup call, so it gets the token of the caller (R8).
                    await FailUnitAsync(store, groupId, unit.Seq, error, Asyncio.IsCancellation(error, cancellationToken) ? CancellationToken.None : cancellationToken);
                    throw;
                }

                return changeSet;
            },
            cancellationToken);
    }

    // PORT: the parameters after `unit` are keyword-only in Python.

    /// <summary>
    /// Run the batch pipeline for one bulk unit and store the change set.
    ///
    /// The items of the unit are compared with each other here, as today. The function
    /// writes nothing to the graph and extracts configured identity attributes.
    /// </summary>
    internal static async Task<ChangeSet> AnalyzeBulkAsync(
        GraphitiCore graphiti,
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        AdmittedUnit unit,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        List<string>? excludedEntityTypes = null,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes = null,
        OrderedDictionary<(string, string), List<string>>? edgeTypeMap = null,
        string? customExtractionInstructions = null,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        bool strictEdgeTypes = false,
        bool useCombinedExtraction = true,
        CancellationToken cancellationToken = default)
    {
        _ = config;
        var (groupId, _, clients) = graphiti.ResolveRequestScope(unit.GroupId);
        List<(EpisodicNode, List<EpisodicNode>)> episodeContext =
            [.. Py.ZipStrict(unit.Episodes, unit.PreviousEpisodes).Select(pair => (pair.First, pair.Second))];
        var resolvedEdgeTypeMap = edgeTypeMap is { Count: > 0 } ? edgeTypeMap : DefaultEdgeTypeMap(edgeTypes);

        return await graphiti.Tracer.StartSpanAsync(
            "ingest.analyze",
            async span =>
            {
                var start = Py.TimeSeconds();
                ChangeSet changeSet;
                try
                {
                    var analysis = await store.StartAnalysisAsync(groupId, unit.Seq, cancellationToken);
                    var trace = new ResolutionTrace();

                    var (nodesByEpisode, uuidMap, extractedEdgesBulk) = await graphiti.ExtractAndDedupeNodesBulkAsync(
                        episodeContext,
                        resolvedEdgeTypeMap,
                        edgeTypes,
                        entityTypes,
                        excludedEntityTypes,
                        strictEdgeTypes: strictEdgeTypes,
                        customExtractionInstructions: customExtractionInstructions,
                        useCombinedExtraction: useCombinedExtraction,
                        clients: clients,

                        // The first pass also resolves extracted nodes to stored nodes.
                        trace: trace,
                        identityProperties: identityProperties,
                        cancellationToken: cancellationToken);

                    List<EpisodicEdge> episodicEdges = [];
                    foreach (var (episodeUuid, episodeNodes) in nodesByEpisode)
                    {
                        episodicEdges.AddRange(EdgeOperations.BuildEpisodicEdges(episodeNodes, episodeUuid, unit.CreatedAt));
                    }

                    var edgesByEpisode = await BulkUtils.DedupeEdgesBulkAsync(
                        clients,
                        [.. extractedEdgesBulk.Select(edges => BulkUtils.ResolveEdgePointers(edges, uuidMap))],
                        episodeContext,
                        [],
                        edgeTypes ?? new(),
                        resolvedEdgeTypeMap,
                        cancellationToken);

                    var (nodes, resolvedEdges, invalidatedEdges, finalUuidMap) = await graphiti.ResolveNodesAndEdgesBulkAsync(
                        nodesByEpisode,
                        edgesByEpisode,
                        episodeContext,
                        entityTypes,
                        edgeTypes,
                        resolvedEdgeTypeMap,
                        unit.Episodes,
                        clients: clients,
                        hydrate: false,
                        trace: trace,
                        identityProperties: identityProperties,
                        cancellationToken: cancellationToken);

                    nodes = UniqueByUuid(nodes);
                    var finalNodeUuids = nodes.Select(node => node.Uuid).ToHashSet(StringComparer.Ordinal);

                    // PORT: the nested function is a local function.
                    string FinalUuid(string nodeUuid)
                    {
                        // The two maps of the batch pipeline can make a chain of UUIDs.
                        for (var i = 0L; i < uuidMap.Count + finalUuidMap.Count + 1; i++)
                        {
                            var nextUuid = Py.Get(finalUuidMap, nodeUuid, Py.Get(uuidMap, nodeUuid, nodeUuid));
                            if (nextUuid == nodeUuid)
                            {
                                break;
                            }

                            nodeUuid = nextUuid;
                        }

                        return nodeUuid;
                    }

                    var nodeFirstEpisode = new OrderedDictionary<string, string>(StringComparer.Ordinal);
                    foreach (var episode in unit.Episodes)
                    {
                        foreach (var node in Py.Get(nodesByEpisode, episode.Uuid, []))
                        {
                            var nodeUuid = FinalUuid(node.Uuid);
                            if (finalNodeUuids.Contains(nodeUuid))
                            {
                                nodeFirstEpisode.TryAdd(nodeUuid, episode.Uuid);
                            }
                        }
                    }

                    foreach (var node in nodes)
                    {
                        nodeFirstEpisode.TryAdd(node.Uuid, unit.Episodes[0].Uuid);
                    }

                    List<EntityEdge> edgesToSave = [.. resolvedEdges, .. invalidatedEdges];

                    // An extracted edge is a key of the candidate record of the trace.
                    var createdEdgeUuids = trace.SeenDuplicateCandidates.Keys.ToHashSet(StringComparer.Ordinal);

                    // PORT: pydantic copies each dict value at construction (R7). The port passes the dicts of the trace,
                    // which no code uses after this call.
                    changeSet = new ChangeSet
                    {
                        GroupId = groupId,
                        UnitId = unit.UnitId,
                        Seq = unit.Seq,
                        SnapshotSeq = analysis.SnapshotSeq,
                        AnalysisStartedAt = analysis.StartedAt,
                        Episodes =
                        [
                            .. unit.Episodes.Select(episode => WrittenEpisode(
                                graphiti,
                                episode,
                                [.. edgesToSave.Where(edge => edge.Episodes.Contains(episode.Uuid, StringComparer.Ordinal)).Select(edge => edge.Uuid)])),
                        ],
                        CreateNodes = [.. nodes.Where(node => !trace.StoredNodeUuids.Contains(node.Uuid))],
                        CreateEdges = UniqueByUuid([.. resolvedEdges.Where(edge => createdEdgeUuids.Contains(edge.Uuid))]),
                        EpisodicEdges = UniqueEpisodicEdges(BulkUtils.ResolveEdgePointers(episodicEdges, finalUuidMap)),
                        Operations = [.. trace.Operations],
                        NodeUuids = [.. nodes.Select(node => node.Uuid)],
                        NodeFirstEpisode = nodeFirstEpisode,
                        SeenNodeCandidates = trace.SeenNodeCandidates,
                        SeenNodeLabels = trace.SeenNodeLabels,
                        EdgesEndedByCandidate = [.. trace.EndedByCandidate.Order(Py.StringComparer)],
                        SeenDuplicateCandidates = trace.SeenDuplicateCandidates,
                        SeenInvalidationCandidates = trace.SeenInvalidationCandidates,
                    }.Validate();
                    await SaveAnalysisAsync(graphiti, store, unit, changeSet, clients, span, start, cancellationToken);
                }
                catch (StaleCommitException)
                {
                    // The unit has a stored change set from a different attempt. It keeps its
                    // position.
                    throw;
                }
                catch (Exception error)
                {
                    // PORT: `except BaseException` also catches a cancellation. A Python cancel arrives one time, so after a
                    // cancel the cleanup runs to its end with CancellationToken.None. After another error, a later cancel
                    // of the caller reaches the cleanup call, so it gets the token of the caller (R8).
                    await FailUnitAsync(store, groupId, unit.Seq, error, Asyncio.IsCancellation(error, cancellationToken) ? CancellationToken.None : cancellationToken);
                    throw;
                }

                return changeSet;
            },
            cancellationToken);
    }

    internal static List<EpisodicEdge> UniqueEpisodicEdges(List<EpisodicEdge> edges)
    {
        var unique = new OrderedDictionary<(string, string), EpisodicEdge>();
        foreach (var edge in edges)
        {
            unique.TryAdd((edge.SourceNodeUuid, edge.TargetNodeUuid), edge);
        }

        return [.. unique.Values];
    }

    // PORT: code reads `replaced` only by key, and tests `dropped` with `in` only (R14).

    /// <summary>Change the edge UUIDs in `entity_edges` of each episode of a change set.</summary>
    internal static void ReplaceEntityEdges(
        ChangeSet changeSet,
        Dictionary<string, string> replaced,
        HashSet<string>? dropped = null,
        List<string>? added = null)
    {
        // PORT: `dropped or set()`: an empty set and None give an empty set.
        dropped = dropped is { Count: > 0 } ? dropped : new HashSet<string>(StringComparer.Ordinal);
        foreach (var episode in changeSet.Episodes)
        {
            List<string> uuids = [.. episode.EntityEdges.Where(uuid => !dropped.Contains(uuid)).Select(uuid => Py.Get(replaced, uuid, uuid))];

            // PORT: `list(dict.fromkeys(...))` keeps the first of each UUID in order (R14).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            episode.EntityEdges = [.. uuids.Concat(added ?? []).Where(seen.Add)];
        }
    }

    // PORT: code reads the result only by key (R14).
    internal static Dictionary<string, (EpisodicNode, List<EpisodicNode>)> EpisodeContext(AdmittedUnit unit)
    {
        var context = new Dictionary<string, (EpisodicNode, List<EpisodicNode>)>(StringComparer.Ordinal);
        foreach (var (episode, previousEpisodes) in Py.ZipStrict(unit.Episodes, unit.PreviousEpisodes))
        {
            context[episode.Uuid] = (episode, previousEpisodes);
        }

        return context;
    }

    /// <summary>Step C3: resolve each created node that has a candidate that the analysis did not see.</summary>
    private static async Task RepairNodesAsync(
        GraphitiClients clients,
        GraphReader reader,
        ChangeSet final,
        Delta delta,
        AdmittedUnit unit,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        OrderedDictionary<string, List<string>>? identityProperties,
        OrderedDictionary<string, double> report,
        CancellationToken cancellationToken = default)
    {
        List<EntityNode> created = [.. final.CreateNodes];
        report["node.checked"] = created.Count;
        if (created.Count == 0)
        {
            return;
        }

        var ownUuids = created.Select(node => node.Uuid).ToHashSet(StringComparer.Ordinal);

        // The candidates of the graph at the time of the commit, with the stored embeddings.
        var graphCandidates = await reader.NodeCandidatesAsync(created, identityProperties, cancellationToken);
        var current = new OrderedDictionary<string, EntityNode>(StringComparer.Ordinal);
        foreach (var candidates in graphCandidates)
        {
            foreach (var candidate in candidates)
            {
                current.TryAdd(candidate.Uuid, candidate);
            }
        }

        // The nodes that the delta created or patched. A search can miss them (index lag).
        List<List<string>> deltaUuids = [];

        // PORT: `zip(a, b, c, strict=True)` is two nested calls of Py.ZipStrict.
        foreach (var ((node, matches), identityMatches) in Py.ZipStrict(
                     Py.ZipStrict(created, delta.MatchNodes(created, identityProperties)),
                     delta.PatchedIdentityMatches(created, identityProperties)))
        {
            List<string> patchedSeen =
                [.. Py.Get(final.SeenNodeCandidates, node.Uuid, []).Where(uuid => delta.Patches.ContainsKey(uuid))];

            // PORT: `list(dict.fromkeys(...))` keeps the first of each UUID in order (R14).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            deltaUuids.Add([.. matches.Concat(identityMatches).Concat(patchedSeen).Where(seen.Add)]);
        }

        // A delta record is a candidate only if it is in the graph (step C2a). The read
        // also gives the current state of the record.
        var missing = deltaUuids.SelectMany(uuids => uuids).ToHashSet(StringComparer.Ordinal);
        missing.ExceptWith(current.Keys);
        missing.ExceptWith(ownUuids);
        foreach (var node in await reader.NodesByUuidsAsync([.. missing.Order(Py.StringComparer)], final.GroupId, cancellationToken))
        {
            current[node.Uuid] = node;
        }

        // The Enrich phase of an earlier unit possibly did not write its patch at this
        // time. The comparison thus uses a copy with the patches of the delta.
        var patchChanged = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var (nodeUuid, node) in current)
        {
            var deltaPatches = Py.Get(delta.Patches, nodeUuid);
            if (deltaPatches is null || deltaPatches.Count == 0)
            {
                continue;
            }

            // The labels that the analysis saw, when the analysis read the node. The Enrich
            // phase of an earlier unit possibly wrote a promoted label after that read.
            var labelsBefore = Py.Get(final.SeenNodeLabels, nodeUuid, node.Labels).ToHashSet(StringComparer.Ordinal);
            foreach (var patch in deltaPatches)
            {
                DedupHelpers.PromoteResolvedNode(DeltaModule.PatchAsNode(node, patch), node);
            }

            patchChanged[nodeUuid] =
                deltaPatches.Any(patch => patch.Attributes.Count > 0) || !labelsBefore.SetEquals(node.Labels);
        }

        List<(EntityNode, List<EntityNode>)> collided = [];
        foreach (var ((node, graphList), extraUuids) in Py.ZipStrict(Py.ZipStrict(created, graphCandidates), deltaUuids))
        {
            var seen = Py.Get(final.SeenNodeCandidates, node.Uuid, []).ToHashSet(StringComparer.Ordinal);
            List<EntityNode> candidates = [];

            // PORT: `dict.fromkeys(...)` keeps the first of each UUID in order (R14).
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var uuid in graphList.Select(candidate => candidate.Uuid).Concat(extraUuids).Where(unique.Add))
            {
                var candidate = Py.Get(current, uuid);
                if (candidate is null || ownUuids.Contains(uuid))
                {
                    continue;
                }

                // The analysis saw the candidate. It stays a candidate only if a patch of
                // the delta gave it attributes or labels that the analysis did not see.
                if (seen.Contains(uuid) && !Py.Get(patchChanged, uuid, false))
                {
                    continue;
                }

                candidates.Add(candidate);
            }

            if (candidates.Count > 0)
            {
                collided.Add((node, candidates));
            }
        }

        report["node.collided"] = collided.Count;
        if (collided.Count == 0)
        {
            return;
        }

        // A bulk unit resolves the nodes of each episode with the context of that episode.
        var context = EpisodeContext(unit);
        var defaultEpisodeUuid = unit.Episodes[0].Uuid;
        var groups = new OrderedDictionary<string, List<(EntityNode, List<EntityNode>)>>(StringComparer.Ordinal);
        foreach (var (node, candidates) in collided)
        {
            var episodeUuid = Py.Get(final.NodeFirstEpisode, node.Uuid, defaultEpisodeUuid);
            if (!context.ContainsKey(episodeUuid))
            {
                episodeUuid = defaultEpisodeUuid;
            }

            // PORT: `groups.setdefault(k, []).append(x)` (R14).
            Py.SetDefault(groups, episodeUuid, []).Add((node, candidates));
        }

        var merged = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        List<PatchNode> patches = [];
        foreach (var (episodeUuid, items) in groups)
        {
            var (episode, previousEpisodes) = context[episodeUuid];
            var trace = new ResolutionTrace();
            var (_, uuidMap, _) = await NodeOperations.ResolveNodesWithCandidatesAsync(
                clients,
                [.. items.Select(item => item.Item1)],
                [.. items.Select(item => item.Item2)],
                episode,
                previousEpisodes,
                entityTypes,
                identityProperties,
                trace: trace,
                cancellationToken: cancellationToken);
            foreach (var (uuid, target) in uuidMap)
            {
                if (uuid != target)
                {
                    merged[uuid] = target;
                }
            }

            patches.AddRange(trace.Operations.OfType<PatchNode>());
            report["repair.llm_calls"] += trace.NodeLlmCalls;
        }

        if (merged.Count == 0)
        {
            return;
        }

        final.CreateNodes = [.. final.CreateNodes.Where(node => !merged.ContainsKey(node.Uuid))];
        foreach (var (uuid, target) in merged)
        {
            final.UuidMap[uuid] = target;
        }

        final.Operations.AddRange(patches);

        // PORT: `list(dict.fromkeys(...))` keeps the first of each UUID in order (R14).
        var seenNodeUuids = new HashSet<string>(StringComparer.Ordinal);
        final.NodeUuids = [.. final.NodeUuids.Select(uuid => Py.Get(merged, uuid, uuid)).Where(seenNodeUuids.Add)];
        var nodeFirstEpisode = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (nodeUuid, episodeUuid) in final.NodeFirstEpisode)
        {
            nodeFirstEpisode.TryAdd(Py.Get(merged, nodeUuid, nodeUuid), episodeUuid);
        }

        final.NodeFirstEpisode = nodeFirstEpisode;
        report["repair.c3.node_merged"] = merged.Count;
    }

    /// <summary>Steps C4 and C4a: apply the UUID map to all edges, then remove exact duplicates.</summary>
    internal static void RemapEdges(ChangeSet final, OrderedDictionary<string, double> report)
    {
        if (final.UuidMap.Count == 0)
        {
            return;
        }

        var uuidsBefore = final.CreateEdges.Select(edge => edge.Uuid).ToHashSet(StringComparer.Ordinal);
        final.CreateEdges = BulkUtils.ResolveEdgePointers(final.CreateEdges, final.UuidMap);
        var dropped = new HashSet<string>(uuidsBefore, StringComparer.Ordinal);
        dropped.ExceptWith(final.CreateEdges.Select(edge => edge.Uuid));
        final.EpisodicEdges = UniqueEpisodicEdges(BulkUtils.ResolveEdgePointers(final.EpisodicEdges, final.UuidMap));

        // A node merge can give two created edges the same endpoints. This is the rule of
        // serial ingestion for the edges of one episode, with no model call.
        var seen = new Dictionary<(string, string, string), EntityEdge>();
        List<EntityEdge> survivors = [];
        var replaced = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var edge in final.CreateEdges)
        {
            var earlier = EdgeOperations.FindExactDuplicateEdge(edge, seen);
            if (earlier is null)
            {
                survivors.Add(edge);
                continue;
            }

            // PORT: `extend` with a generator tests each UUID against the list as it grows.
            foreach (var uuid in edge.Episodes)
            {
                if (!earlier.Episodes.Contains(uuid, StringComparer.Ordinal))
                {
                    earlier.Episodes.Add(uuid);
                }
            }

            replaced[edge.Uuid] = earlier.Uuid;
        }

        final.CreateEdges = survivors;
        ReplaceEntityEdges(final, replaced, dropped);
        report["repair.c4a.edge_removed"] = replaced.Count;
    }

    internal static void MoveEndDateToEarliestContradiction(EntityEdge edge, List<EntityEdge> contradicted)
    {
        var validAt = DatetimeUtils.EnsureUtc(edge.ValidAt);
        var invalidAt = DatetimeUtils.EnsureUtc(edge.InvalidAt);
        if (validAt is null || invalidAt is null)
        {
            return;
        }

        foreach (var candidate in contradicted)
        {
            var candidateValidAt = DatetimeUtils.EnsureUtc(candidate.ValidAt);
            if (candidateValidAt is not null && validAt.Value < candidateValidAt.Value && candidateValidAt.Value < invalidAt.Value)
            {
                edge.InvalidAt = candidate.ValidAt;
                invalidAt = candidateValidAt;
            }
        }
    }

    /// <summary>Steps C5 to C7: find and decide the edge duplicates and the edge contradictions.</summary>
    private static async Task RepairEdgesAsync(
        GraphitiClients clients,
        GraphReader reader,
        ChangeSet final,
        Delta delta,
        AdmittedUnit unit,
        OrderedDictionary<string, double> report,
        CancellationToken cancellationToken = default)
    {
        List<EntityEdge> edges = [.. final.CreateEdges];
        report["edge.checked"] = edges.Count;
        if (edges.Count == 0)
        {
            return;
        }

        var ownUuids = edges.Select(edge => edge.Uuid).ToHashSet(StringComparer.Ordinal);

        // C5 has the endpoint filter. C6 is a separate pass with no endpoint filter.
        var (duplicateLists, invalidationLists) = await Asyncio.GatherAsync(
            () => reader.EdgeDuplicateCandidatesAsync(edges, cancellationToken),
            () => reader.EdgeInvalidationCandidatesAsync(edges, cancellationToken),
            cancellationToken);
        var deltaPairUuids = delta.EdgesBetween(edges);
        var deltaSimilarUuids = delta.SimilarEdges(edges);

        var current = new OrderedDictionary<string, EntityEdge>(StringComparer.Ordinal);
        foreach (var candidateLists in new[] { duplicateLists, invalidationLists })
        {
            foreach (var candidates in candidateLists)
            {
                foreach (var candidate in candidates)
                {
                    current.TryAdd(candidate.Uuid, candidate);
                }
            }
        }

        // A delta edge is a candidate only if it is in the graph (step C2a). The read also
        // gives its current end dates.
        var missing = deltaPairUuids.Concat(deltaSimilarUuids).SelectMany(uuids => uuids).ToHashSet(StringComparer.Ordinal);
        missing.ExceptWith(current.Keys);
        missing.ExceptWith(ownUuids);
        foreach (var edge in await reader.EdgesByUuidsAsync([.. missing.Order(Py.StringComparer)], cancellationToken))
        {
            current[edge.Uuid] = edge;
        }

        List<(EntityEdge, List<EntityEdge>, List<EntityEdge>)> collided = [];
        foreach (var (index, edge) in edges.Index())
        {
            var seenDuplicates = Py.Get(final.SeenDuplicateCandidates, edge.Uuid, []).ToHashSet(StringComparer.Ordinal);
            var seenInvalidations = Py.Get(final.SeenInvalidationCandidates, edge.Uuid, []).ToHashSet(StringComparer.Ordinal);

            // PORT: `list(dict.fromkeys(...))` keeps the first of each UUID in order (R14).
            var seenPair = new HashSet<string>(StringComparer.Ordinal);
            List<string> pairUuids =
                [.. duplicateLists[index].Select(candidate => candidate.Uuid).Concat(deltaPairUuids[index]).Where(seenPair.Add)];
            var duplicates = EdgeOperations.SamePairEdges(
                edge,
                [
                    .. pairUuids
                        .Where(uuid => current.ContainsKey(uuid) && !ownUuids.Contains(uuid) && !seenDuplicates.Contains(uuid))
                        .Select(uuid => current[uuid]),
                ]);
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            excluded.UnionWith(pairUuids);
            excluded.UnionWith(seenDuplicates);
            excluded.UnionWith(seenInvalidations);
            excluded.UnionWith(ownUuids);
            var seenInvalidation = new HashSet<string>(StringComparer.Ordinal);
            List<EntityEdge> invalidations =
            [
                .. invalidationLists[index]
                    .Select(candidate => candidate.Uuid)
                    .Concat(deltaSimilarUuids[index])
                    .Where(seenInvalidation.Add)
                    .Where(uuid => current.ContainsKey(uuid) && !excluded.Contains(uuid))
                    .Select(uuid => current[uuid]),
            ];
            if (duplicates.Count > 0 || invalidations.Count > 0)
            {
                collided.Add((edge, duplicates, invalidations));
            }
        }

        report["edge.collided"] = collided.Count;
        if (collided.Count == 0)
        {
            return;
        }

        // PORT: `resolver_kwargs` holds `clients` only with prompt routing. No key passes the default null.
        var resolverClients = GraphitiTypes.UsesPromptRouting(clients) ? clients : null;
        var decisions = await Helpers.SemaphoreGatherAsync(
            [
                .. collided.Select(item => (Func<Task<EdgeDecision>>)(() => EdgeOperations.DecideEdgeAgainstCandidatesAsync(
                    clients.LlmClient,
                    item.Item1,
                    item.Item2,
                    item.Item3,
                    clients: resolverClients,
                    cancellationToken: cancellationToken))),
            ],
            clients.MaxCoroutines,
            cancellationToken);

        // Each candidate is a stored record. The trace makes one operation for each change.
        var trace = new ResolutionTrace();
        trace.WatchStoredEdges([.. collided.SelectMany(item => item.Item2.Concat(item.Item3))]);
        var replaced = new Dictionary<string, string>(StringComparer.Ordinal);
        var endedByCandidate = final.EdgesEndedByCandidate.ToHashSet(StringComparer.Ordinal);
        List<string> endedUuids = [];
        var endedCount = 0L;
        foreach (var ((edge, _, _), decision) in Py.ZipStrict(collided, decisions))
        {
            if (decision.ExactMatch is null)
            {
                report["repair.llm_calls"] += 1;
            }

            var target = decision.ExactMatch ?? (decision.Duplicates.Count > 0 ? decision.Duplicates[0] : null);
            var resolvedEdge = edge;
            if (target is not null)
            {
                // PORT: `extend` with a generator tests each UUID against the list as it grows.
                foreach (var uuid in edge.Episodes)
                {
                    if (!target.Episodes.Contains(uuid, StringComparer.Ordinal))
                    {
                        target.Episodes.Add(uuid);
                    }
                }

                replaced[edge.Uuid] = target.Uuid;
                resolvedEdge = target;
            }

            if (decision.ExactMatch is not null)
            {
                continue;
            }

            // The date rules apply in the two directions: a candidate that starts later
            // ends the edge, and the edge ends a candidate that starts earlier.
            var wasOpen = resolvedEdge.ExpiredAt is null;
            if (ReferenceEquals(resolvedEdge, edge) && endedByCandidate.Contains(edge.Uuid))
            {
                // The analysis ended the edge at the start of a contradiction that it saw.
                // Serial ingestion ends an edge at the earliest contradiction that starts
                // later, so an earlier one of this commit moves the end date.
                MoveEndDateToEarliestContradiction(edge, decision.Contradicted);
            }

            var invalidated = EdgeOperations.ApplyTemporalInvalidation(resolvedEdge, [.. decision.Contradicted]);
            trace.MarkEndedAsCandidate(invalidated);
            endedUuids.AddRange(invalidated.Select(candidate => candidate.Uuid));
            endedCount += invalidated.Count + (wasOpen && resolvedEdge.ExpiredAt is not null ? 1 : 0);
        }

        final.Operations.AddRange(trace.CollectEdgeOperations());
        final.CreateEdges = [.. final.CreateEdges.Where(edge => !replaced.ContainsKey(edge.Uuid))];

        // Serial ingestion of one episode adds the edges that the episode ended to
        // `entity_edges`. A bulk call adds only the edges that have the episode.
        ReplaceEntityEdges(final, replaced, added: unit.Bulk ? null : endedUuids);
        report["repair.c5.edge_merged"] = replaced.Count;
        report["repair.c6.edge_ended"] = endedCount;
    }

    // PORT: the parameters after `delta_units` are keyword-only in Python. `unit` has no default.

    /// <summary>
    /// Repair a change set against the graph and the delta (steps C2a to C7).
    ///
    /// The function reads the candidates of each created record again. It decides only a
    /// record that has a candidate that the analysis did not see. It writes nothing, and
    /// it does not change `change_set`. The result is the change set to make final.
    /// </summary>
    internal static async Task<ChangeSet> RebaseChangeSetAsync(
        GraphitiClients clients,
        ChangeSet changeSet,
        List<DeltaUnit> deltaUnits,
        AdmittedUnit unit,
        GraphReader? reader = null,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        OrderedDictionary<string, double>? report = null,
        CancellationToken cancellationToken = default)
    {
        report ??= NewReport();
        reader ??= new GraphReader(clients);
        var start = Py.TimeSeconds();

        var final = changeSet.ModelCopy(deep: true);
        var delta = DeltaModule.LoadDeltaDigests(
            final.GroupId, [.. deltaUnits.Where(deltaUnit => deltaUnit.Seq != final.Seq)]);
        report["delta.unit_count"] = delta.UnitCount;

        await RepairNodesAsync(
            clients, reader, final, delta, unit, entityTypes, identityProperties, report, cancellationToken);
        RemapEdges(final, report);
        await RepairEdgesAsync(clients, reader, final, delta, unit, report, cancellationToken);

        report["node.merged"] = report["repair.c3.node_merged"];
        report["edge.deduplicated"] = report["repair.c4a.edge_removed"] + report["repair.c5.edge_merged"];
        report["edge.ended"] = report["repair.c6.edge_ended"];
        report["repair.duration_ms"] = (Py.TimeSeconds() - start) * 1000;
        return final;
    }

    /// <summary>
    /// Return copies of the episodes that keep the `entity_edges` of the stored nodes.
    ///
    /// The `entity_edges` of each copy is the stored list and then the new UUIDs of the
    /// change set. A commit thus removes no link that an earlier commit wrote, and a
    /// second apply writes the same list.
    /// </summary>
    internal static async Task<List<EpisodicNode>> EpisodesWithStoredLinksAsync(
        GraphReader reader,
        List<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        var stored = new Dictionary<string, EpisodicNode>(StringComparer.Ordinal);
        foreach (var episode in await reader.EpisodesByUuidsAsync([.. episodes.Select(episode => episode.Uuid)], cancellationToken))
        {
            stored[episode.Uuid] = episode;
        }

        List<EpisodicNode> written = [];
        foreach (var episode in episodes)
        {
            var storedEpisode = Py.Get(stored, episode.Uuid);
            if (storedEpisode is null || storedEpisode.EntityEdges.Count == 0)
            {
                written.Add(episode);
                continue;
            }

            // PORT: `list(dict.fromkeys(...))` keeps the first of each UUID in order (R14).
            var seen = new HashSet<string>(StringComparer.Ordinal);
            List<string> links = [.. storedEpisode.EntityEdges.Concat(episode.EntityEdges).Where(seen.Add)];

            // PORT: `model_copy(update={'entity_edges': links})` (R7).
            var copy = episode.ModelCopy();
            copy.EntityEdges = links;
            written.Add(copy);
        }

        return written;
    }
}

/// <summary>The result of `apply_change_set`: the stored edges that changed, and the counts.</summary>
internal sealed record ApplyResult
{
    internal List<EntityEdge> ChangedEdges { get; set; } = [];

    internal long Applied { get; set; }

    internal long Dropped { get; set; }

    internal ApplyResult Validate() => this;
}

internal static partial class Phases
{
    // PORT: `write_budget: Callable[[], Awaitable[float]]` is an async callable (R8).
    // PORT: `asyncio.wait_for(x.save(driver), await write_budget())`. Python makes the coroutine of the save first and
    // awaits the budget second, and the save starts only in wait_for. So the port awaits the budget first. Each save
    // takes the token of the limit (R8, open item O9).
    internal static async Task WriteSagaLinksAsync(
        GraphitiClients clients,
        ChangeSet changeSet,
        Func<CancellationToken, Task<double>> writeBudget,
        CancellationToken cancellationToken = default)
    {
        var links = changeSet.SagaLinks;
        var changeSetSaga = changeSet.SagaNode;
        if (links.Count == 0 || changeSetSaga is null)
        {
            return;
        }

        var driver = clients.Driver;
        SagaNode sagaNode;

        // PORT: try, except, else (Part 3). The else body follows the call inside the try, and the filter keeps its
        // exceptions out of the catch.
        var returned = false;
        try
        {
            // The saga is in the graph. The apply changes only the episode fields of the
            // stored node, so the summary and its dates stay.
            sagaNode = await SagaNode.GetByUuidAsync(driver, changeSetSaga.Uuid, cancellationToken);
            returned = true;
            if (sagaNode.GroupId != changeSet.GroupId)
            {
                throw new NodeNotFoundException(changeSetSaga.Uuid);
            }
        }
        catch (NodeNotFoundException) when (!returned)
        {
            sagaNode = changeSetSaga.ModelCopy();
            if (changeSet.CreateSaga)
            {
                // The saga is new. Its node is the first saga write. A second apply reads
                // the node that the first apply wrote.
                var budget = await writeBudget(cancellationToken);
                var newSaga = sagaNode;
                await Asyncio.WaitForAsync(token => newSaga.SaveAsync(driver, token), budget, PortServices.Time, cancellationToken);
            }
        }

        foreach (var link in links)
        {
            if (link.PreviousEpisodeUuid is not null && link.NextEpisodeEdgeUuid is not null)
            {
                var nextEpisodeEdge = new NextEpisodeEdge
                {
                    Uuid = link.NextEpisodeEdgeUuid,
                    SourceNodeUuid = link.PreviousEpisodeUuid,
                    TargetNodeUuid = link.EpisodeUuid,
                    GroupId = changeSet.GroupId,
                    CreatedAt = link.CreatedAt,
                }.Validate();
                var nextBudget = await writeBudget(cancellationToken);
                await Asyncio.WaitForAsync(token => nextEpisodeEdge.SaveAsync(driver, token), nextBudget, PortServices.Time, cancellationToken);
            }

            var hasEpisodeEdge = new HasEpisodeEdge
            {
                Uuid = link.HasEpisodeEdgeUuid,
                SourceNodeUuid = link.SagaUuid,
                TargetNodeUuid = link.EpisodeUuid,
                GroupId = changeSet.GroupId,
                CreatedAt = link.CreatedAt,
            }.Validate();
            var hasBudget = await writeBudget(cancellationToken);
            await Asyncio.WaitForAsync(token => hasEpisodeEdge.SaveAsync(driver, token), hasBudget, PortServices.Time, cancellationToken);
        }

        // Track the first and the last episode on the saga node, as serial ingestion does.
        if (sagaNode.FirstEpisodeUuid is null)
        {
            sagaNode.FirstEpisodeUuid = links[0].EpisodeUuid;
        }

        sagaNode.LastEpisodeUuid = links[^1].EpisodeUuid;
        var sagaBudget = await writeBudget(cancellationToken);
        await Asyncio.WaitForAsync(token => sagaNode.SaveAsync(driver, token), sagaBudget, PortServices.Time, cancellationToken);
    }

    // PORT: the parameters after `change_set` are keyword-only in Python. `write_budget` has no default.

    /// <summary>
    /// Apply a final change set to the graph (steps C8 and C9).
    ///
    /// Each operation applies to the current state of its stored edge. An operation on an
    /// edge that does not exist is dropped. `write_budget` returns the time limit in
    /// seconds of a write that starts now, or raises `UnitExpiredError`. `check_turn`
    /// runs before each graph write. It raises `StaleCommitError` when a different
    /// attempt has the turn of the unit. The function mints no UUID and no date, so a
    /// second call writes the same records.
    /// </summary>
    internal static async Task<ApplyResult> ApplyChangeSetAsync(
        GraphitiClients clients,
        ChangeSet changeSet,
        Func<double> writeBudget,
        GraphReader? reader = null,
        Func<CancellationToken, Task>? checkTurn = null,
        CancellationToken cancellationToken = default)
    {
        var driver = clients.Driver;
        reader ??= new GraphReader(clients);

        // PORT: the nested coroutine is a local async function.
        async Task<double> CheckedBudgetAsync(CancellationToken token)
        {
            if (checkTurn is not null)
            {
                try
                {
                    await checkTurn(token);
                }
                catch (Exception error) when (error is StaleCommitException or UnitExpiredException)
                {
                    throw;
                }
                catch (Exception error) when (!Asyncio.IsCancellation(error, token))
                {
                    // The store gave no answer. The local deadline then limits the write.
                    Log.TurnNotChecked(Logger, error, changeSet.Seq, changeSet.GroupId);
                }
            }

            return writeBudget();
        }

        var operations = changeSet.EdgeOperations();

        // PORT: `operation.edge_uuid` on the union of the three edge operations is a switch on the case: the abstract
        // record has no member for it. `list(dict.fromkeys(...))` keeps the first of each UUID in order (R14).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<string> edgeUuids =
        [
            .. operations.Select(operation => operation switch
                {
                    AddEpisodesToEdge addEpisodesToEdge => addEpisodesToEdge.EdgeUuid,
                    EndEdge endEdge => endEdge.EdgeUuid,
                    SetEdgeAttributes setEdgeAttributes => setEdgeAttributes.EdgeUuid,
                    _ => throw new MissingMemberException("edge_uuid"),
                }).Where(seen.Add),
        ];
        var current = new Dictionary<string, EntityEdge>(StringComparer.Ordinal);
        foreach (var edge in await reader.EdgesByUuidsAsync(edgeUuids, cancellationToken))
        {
            current[edge.Uuid] = edge;
        }

        var result = new ApplyResult().Validate();
        var changed = new OrderedDictionary<string, EntityEdge>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            var edgeUuid = operation switch
            {
                AddEpisodesToEdge addEpisodesToEdge => addEpisodesToEdge.EdgeUuid,
                EndEdge endEdge => endEdge.EdgeUuid,
                SetEdgeAttributes setEdgeAttributes => setEdgeAttributes.EdgeUuid,
                _ => throw new MissingMemberException("edge_uuid"),
            };
            var edge = Py.Get(current, edgeUuid);
            if (edge is null)
            {
                // A writer outside the sequence deleted the edge. It is not created again.
                result.Dropped += 1;
                continue;
            }

            result.Applied += 1;
            if (ChangeSetModule.ApplyEdgeOperation(edge, operation))
            {
                changed[edge.Uuid] = edge;
            }
        }

        foreach (var edge in changed.Values)
        {
            if (edge.FactEmbedding is null)
            {
                await edge.LoadFactEmbeddingAsync(driver, cancellationToken);
            }
        }

        result.ChangedEdges = [.. changed.Values];

        // PORT: Python evaluates the arguments in order: the read of the stored links, then the budget.
        var episodes = await EpisodesWithStoredLinksAsync(reader, changeSet.Episodes, cancellationToken);
        var bulkBudget = await CheckedBudgetAsync(cancellationToken);
        await BulkUtils.AddNodesAndEdgesBulkAsync(
            driver,
            episodes,
            changeSet.EpisodicEdges,
            changeSet.CreateNodes,
            changeSet.CreateEdges,
            clients.Embedder,
            writeTimeout: bulkBudget,
            cancellationToken: cancellationToken);
        if (changed.Count > 0)
        {
            var updateBudget = await CheckedBudgetAsync(cancellationToken);

            // PORT: the extra arguments of Python are closed over by the work function. It passes on the token that
            // execute_write_with_timeout gives it (R9).
            await BulkUtils.ExecuteWriteWithTimeoutAsync(
                driver,
                updateBudget,
                (tx, token) => BulkUtils.UpdateEntityEdgesTxAsync(tx, result.ChangedEdges, clients.Embedder, driver, token),
                cancellationToken);
        }

        await WriteSagaLinksAsync(clients, changeSet, CheckedBudgetAsync, cancellationToken);
        return result;
    }

    /// <summary>Apply the same final change set until one attempt succeeds. No model call is made.</summary>
    internal static async Task<List<EntityEdge>> ApplyWithRetriesAsync(
        GraphitiClients clients,
        ChangeSet final,
        Func<double> writeBudget,
        long maxAttempts,
        OrderedDictionary<string, double> report,
        GraphReader reader,
        Func<CancellationToken, Task> checkTurn,
        CancellationToken cancellationToken = default)
    {
        var changed = new OrderedDictionary<string, EntityEdge>(StringComparer.Ordinal);
        Exception? lastError = null;
        // PORT: `range(1, max_attempts + 1)`. `attempt <= maxAttempts` does not overflow for long.MaxValue.
        for (var attempt = 1L; attempt <= maxAttempts; attempt++)
        {
            report["apply.attempts"] = attempt;
            ApplyResult result;
            try
            {
                result = await ApplyChangeSetAsync(
                    clients, final, writeBudget: writeBudget, reader: reader, checkTurn: checkTurn, cancellationToken: cancellationToken);
            }
            catch (Exception error) when (error is UnitExpiredException or StaleCommitException)
            {
                throw;
            }
            catch (Exception error) when (!Asyncio.IsCancellation(error, cancellationToken))
            {
                lastError = error;

                // PORT: `type(error).__name__` is the .NET type name of the error (decision D50, R15).
                Log.ApplyAttemptFailed(Logger, attempt, final.Seq, final.GroupId, error.GetType().Name);
                continue;
            }

            // A retry after a partial write finds some edges already changed.
            foreach (var edge in result.ChangedEdges)
            {
                changed[edge.Uuid] = edge;
            }

            report["operation.applied"] = result.Applied;
            report["operation.dropped"] = result.Dropped;
            return [.. changed.Values];
        }

        Debug.Assert(lastError is not null, "assert last_error is not None");
        throw lastError;
    }

    internal static async Task MarkApplyFailedAsync(
        IngestionStateStore store,
        string groupId,
        long seq,
        long fence,
        CancellationToken cancellationToken = default)
    {
        Log.EachApplyAttemptFailed(Logger, seq, groupId);

        // With one of these errors, a different attempt has the turn of the unit.
        try
        {
            await store.FailAsync(groupId, seq, StateStore.ReasonApplyFailed, fence, cancellationToken);
        }
        catch (Exception error) when (error is StaleCommitException or UnitExpiredException)
        {
            // PORT: contextlib.suppress(StaleCommitError, UnitExpiredError)
        }
    }

    // PORT: code reads `turns` only by key (R14).

    /// <summary>
    /// Apply the stored final change set of an earlier unit that stopped, and record its commit.
    ///
    /// The nodes of the recovered unit get no enrichment from that unit.
    /// </summary>
    internal static async Task RecoverUnitAsync(
        GraphitiClients clients,
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        string groupId,
        CommitRecover recover,
        double callStartedAt,
        Func<double> clock,
        Dictionary<long, long> turns,
        OrderedDictionary<string, double> report,
        GraphReader reader,
        CancellationToken cancellationToken = default)
    {
        // The recovery has the turn of the earlier unit, with the fence of `recover`.
        var lease = new UnitLease(store, config, groupId, recover.Seq, clock);
        lease.Fence = recover.Fence;
        lease.Renewed(callStartedAt);

        turns[recover.Seq] = Py.Get(turns, recover.Seq, 0L) + 1;
        try
        {
            await ApplyWithRetriesAsync(
                clients,
                ChangeSet.FromBytes(recover.FinalPayload),
                lease.WriteBudget,
                config.MaxApplyAttempts,
                NewReport(),
                reader,
                lease.HeartbeatAsync,
                cancellationToken);
            await store.FinishCommitAsync(
                groupId, recover.Seq, recover.Fence, [], config.IndexLagSeconds, cancellationToken);
            report["recovered.count"] += 1;
            Log.RecoveredUnit(Logger, recover.Seq, groupId);
        }
        catch (StaleCommitException)
        {
            // A different attempt has the turn of that unit.
            return;
        }
        catch (UnitExpiredException)
        {
            // The time of this turn ended. The next `begin_commit` call gives a new turn.
            if (turns[recover.Seq] >= config.MaxApplyAttempts)
            {
                await MarkApplyFailedAsync(store, groupId, recover.Seq, recover.Fence, cancellationToken);
            }
        }
        catch (Exception error) when (!Asyncio.IsCancellation(error, cancellationToken))
        {
            await MarkApplyFailedAsync(store, groupId, recover.Seq, recover.Fence, cancellationToken);
        }
    }

    // PORT: `saga: str | SagaNode | None` is the `SagaOrName?` of graphiti.py (Part 3).

    /// <summary>
    /// Step C7a: find or make the saga node, read its last episode, and mint the edge UUIDs.
    ///
    /// The function writes nothing. It returns the links, the saga node, and True when the
    /// saga node is new and the apply of the commit must create it.
    /// </summary>
    internal static async Task<(List<SagaLink>, SagaNode?, bool)> BuildSagaLinksAsync(
        GraphitiCore graphiti,
        GraphitiClients clients,
        AdmittedUnit unit,
        SagaOrName? saga,
        string? sagaPreviousEpisodeUuid,
        CancellationToken cancellationToken = default)
    {
        if (saga is null)
        {
            return ([], null, false);
        }

        var driver = clients.Driver;
        var now = unit.CreatedAt;
        List<EpisodicNode> episodes;
        PyDateTime sagaCreatedAt;
        string currentEpisodeUuid;
        string? previousEpisodeUuid;
        if (unit.Bulk)
        {
            // PORT: `sorted(..., key=valid_at)` sorts by a datetime key, so Py.SortInPlace on a copy (R14).
            episodes = [.. unit.Episodes];
            Py.SortInPlace(episodes, episode => episode.ValidAt);

            // PORT: `valid_at` is a required datetime, so the filter `is not None` keeps each one. `min` keeps the
            // first of equal values and compares with `<` (R14).
            List<PyDateTime> validAts = [.. episodes.Select(episode => episode.ValidAt)];
            if (validAts.Count > 0)
            {
                var earliest = validAts[0];
                foreach (var validAt in validAts.Skip(1))
                {
                    if (validAt < earliest)
                    {
                        earliest = validAt;
                    }
                }

                sagaCreatedAt = earliest;
            }
            else
            {
                sagaCreatedAt = now;
            }

            currentEpisodeUuid = string.Empty;
            previousEpisodeUuid = null;
        }
        else
        {
            episodes = [unit.Episodes[0]];

            // PORT: `episodes[0].valid_at or now`. A datetime is always true, and `valid_at` is required.
            sagaCreatedAt = episodes[0].ValidAt;
            currentEpisodeUuid = episodes[0].Uuid;
            previousEpisodeUuid = sagaPreviousEpisodeUuid;
        }

        var createSaga = false;
        SagaNode sagaNode;
        if (saga is SagaOrName.Name sagaName)
        {
            var found = await graphiti.FindSagaAsync(sagaName.Value, unit.GroupId, driver: driver, cancellationToken: cancellationToken);
            createSaga = found is null;
            sagaNode = found ?? new SagaNode { Name = sagaName.Value, GroupId = unit.GroupId, CreatedAt = sagaCreatedAt }.Validate();
        }
        else
        {
            var sagaObject = ((SagaOrName.Node)saga).Saga;

            // Serial ingestion uses the object of the caller and saves it after the edges.
            if (sagaObject.GroupId != unit.GroupId)
            {
                throw new ArgumentException("saga " + sagaObject.Uuid + " does not belong to group " + unit.GroupId);
            }

            // PORT: try, except, else (Part 3). The else body follows the call inside the try.
            var returned = false;
            try
            {
                var storedSaga = await SagaNode.GetByUuidAsync(driver, sagaObject.Uuid, cancellationToken);
                returned = true;
                if (storedSaga.GroupId != unit.GroupId)
                {
                    throw new NodeNotFoundException(sagaObject.Uuid);
                }
            }
            catch (NodeNotFoundException) when (!returned)
            {
                // PORT: Python passes
            }

            sagaNode = sagaObject.ModelCopy();
        }

        if (previousEpisodeUuid is null && !createSaga)
        {
            previousEpisodeUuid = await graphiti.SagaGetPreviousEpisodeUuidAsync(
                sagaNode.Uuid, currentEpisodeUuid, unit.GroupId, driver: driver, cancellationToken: cancellationToken);
        }

        List<SagaLink> links = [];
        foreach (var episode in episodes)
        {
            links.Add(new SagaLink
            {
                SagaUuid = sagaNode.Uuid,
                EpisodeUuid = episode.Uuid,
                PreviousEpisodeUuid = previousEpisodeUuid,
                HasEpisodeEdgeUuid = Py.Uuid4(),
                NextEpisodeEdgeUuid = previousEpisodeUuid is not null ? Py.Uuid4() : null,
                CreatedAt = now,
            }.Validate());
            previousEpisodeUuid = episode.Uuid;
        }

        return (links, sagaNode, createSaga);
    }

    // PORT: the parameters after `change_set` are keyword-only in Python. `saga` is the
    // `SagaOrName?` of graphiti.py.

    /// <summary>
    /// Commit a unit in sequence order (steps C1 to C10).
    ///
    /// The function waits for the commit turn. With the turn, it repairs the change set
    /// against the graph and the delta, makes the final change set durable, applies it to
    /// the current records, and records the commit. When an earlier unit stopped after it
    /// stored its final change set, the function applies that change set first.
    ///
    /// The caller renews the deadline of the unit with `lease.heartbeat()` while the
    /// function runs. With no `lease`, the function renews the deadline while it waits.
    /// </summary>
    internal static async Task<CommitResult> CommitUnitAsync(
        GraphitiCore graphiti,
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        AdmittedUnit unit,
        ChangeSet changeSet,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        SagaOrName? saga = null,
        string? sagaPreviousEpisodeUuid = null,
        Func<CancellationToken, Task>? wait = null,
        UnitLease? lease = null,
        GraphReader? reader = null,
        CancellationToken cancellationToken = default)
    {
        var (groupId, _, clients) = graphiti.ResolveRequestScope(unit.GroupId);
        var waitFn = wait ?? DefaultWait(config);
        var commitReader = reader ?? new GraphReader(clients);
        var ownLease = lease is null;
        var commitLease = lease ?? new UnitLease(store, config, groupId, unit.Seq);
        var report = NewReport();

        return await graphiti.Tracer.StartSpanAsync(
            "ingest.commit",
            async span =>
            {
                var start = Py.TimeSeconds();
                var finalSaved = false;
                CommitTurn turn;
                ChangeSet final;
                try
                {
                    var recoverTurns = new Dictionary<long, long>();
                    while (true)
                    {
                        var callStartedAt = commitLease.Clock();
                        var begun = await store.BeginCommitAsync(groupId, unit.Seq, config.LeaseSeconds, cancellationToken);
                        if (begun is CommitTurn commitTurn)
                        {
                            commitLease.Fence = commitTurn.Fence;
                            commitLease.Renewed(callStartedAt);
                            turn = commitTurn;
                            break;
                        }

                        if (begun is CommitRecover recover)
                        {
                            await RecoverUnitAsync(
                                clients,
                                store,
                                config,
                                groupId,
                                recover,
                                callStartedAt,
                                commitLease.Clock,
                                recoverTurns,
                                report,
                                commitReader,
                                cancellationToken);
                            continue;
                        }

                        Debug.Assert(begun is CommitWait, "assert isinstance(turn, CommitWait)");
                        if (ownLease)
                        {
                            await commitLease.HeartbeatAsync(cancellationToken);
                        }

                        await waitFn(cancellationToken);
                    }

                    report["turn_wait_ms"] = (Py.TimeSeconds() - start) * 1000;

                    if (turn.FinalPayload is not null)
                    {
                        // C1: a retry applies the stored final change set and makes no model call.
                        final = ChangeSet.FromBytes(turn.FinalPayload);
                        finalSaved = true;
                        report["retry"] = 1;
                    }
                    else
                    {
                        var deltaUnits = await store.LoadDeltaAsync(
                            groupId,
                            changeSet.SnapshotSeq,
                            unit.Seq,
                            changeSet.AnalysisStartedAt - config.IndexLagSeconds,
                            cancellationToken);
                        final = await RebaseChangeSetAsync(
                            clients,
                            changeSet,
                            deltaUnits,
                            unit: unit,
                            reader: commitReader,
                            entityTypes: entityTypes,
                            identityProperties: identityProperties,
                            report: report,
                            cancellationToken: cancellationToken);
                        (final.SagaLinks, final.SagaNode, final.CreateSaga) = await BuildSagaLinksAsync(
                            graphiti, clients, unit, saga, sagaPreviousEpisodeUuid, cancellationToken);
                        if (config.BeforeCommitWrite is not null)
                        {
                            final = await config.BeforeCommitWrite(final, cancellationToken);
                        }

                        // C7c: the final change set is durable before the first graph write.
                        await store.SaveFinalAsync(groupId, unit.Seq, turn.Fence, final.ToBytes(), cancellationToken);
                        finalSaved = true;
                    }
                }
                catch (Exception error)
                {
                    if (!finalSaved)
                    {
                        // PORT: `except BaseException` also catches a cancellation. A Python cancel arrives one time, so
                        // after a cancel the cleanup runs to its end with CancellationToken.None. After another error, a
                        // later cancel of the caller reaches the cleanup call, so it gets the token of the caller (R8).
                        await FailUnitAsync(
                            store, groupId, unit.Seq, error, Asyncio.IsCancellation(error, cancellationToken) ? CancellationToken.None : cancellationToken);
                    }

                    throw;
                }

                List<EntityEdge> changedEdges;
                try
                {
                    changedEdges = await ApplyWithRetriesAsync(
                        clients,
                        final,
                        commitLease.WriteBudget,
                        config.MaxApplyAttempts,
                        report,
                        commitReader,
                        commitLease.HeartbeatAsync,
                        cancellationToken);
                }
                catch (Exception error) when (error is UnitExpiredException or StaleCommitException)
                {
                    throw;
                }
                catch (Exception error) when (!Asyncio.IsCancellation(error, cancellationToken))
                {
                    await MarkApplyFailedAsync(store, groupId, unit.Seq, turn.Fence, cancellationToken);
                    throw;
                }

                await store.FinishCommitAsync(
                    groupId, unit.Seq, turn.Fence, final.NodeUuids, config.IndexLagSeconds, cancellationToken);

                report["duration_ms"] = (Py.TimeSeconds() - start) * 1000;
                // PORT: in Python each count of the report is an int and each `*_ms` key a float. The port keeps the
                // report as a dict of double (R18), so the span and the log scope get each count as a long, as the other
                // spans of the file do. CommitResult.report stays a dict of double, as pydantic coerces it to float.
                var reportValues = new OrderedDictionary<string, object?>();
                foreach (var (key, value) in report)
                {
                    reportValues[key] = key.EndsWith("_ms", StringComparison.Ordinal) ? value : (long)value;
                }

                var attributes = new OrderedDictionary<string, object?> { ["ingest.seq"] = unit.Seq };
                foreach (var (key, value) in reportValues)
                {
                    attributes[key] = value;
                }

                span.AddAttributes(attributes);
                using (Logger.BeginScope(new OrderedDictionary<string, object?>
                {
                    ["ingest_group_id"] = groupId,
                    ["ingest_seq"] = unit.Seq,
                    ["ingest_report"] = reportValues,
                }))
                {
                    Log.CommittedUnit(Logger, unit.Seq, groupId);
                }

                return new CommitResult
                {
                    ChangeSet = final,
                    Edges = [.. final.CreateEdges, .. changedEdges],
                    Report = report,
                }.Validate();
            },
            cancellationToken);
    }

    /// <summary>Extract the attributes and the summaries of nodes. Return the number of failed nodes.</summary>
    internal static async Task<long> HydrateNodesAsync(
        GraphitiClients clients,
        AdmittedUnit unit,
        ChangeSet final,
        List<EntityNode> nodes,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        CancellationToken cancellationToken = default)
    {
        var context = EpisodeContext(unit);
        var defaultEpisodeUuid = unit.Episodes[0].Uuid;
        var groups = new OrderedDictionary<string, List<EntityNode>>(StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            var episodeUuid = Py.Get(final.NodeFirstEpisode, node.Uuid, defaultEpisodeUuid);
            if (!context.ContainsKey(episodeUuid))
            {
                episodeUuid = defaultEpisodeUuid;
            }

            // PORT: `groups.setdefault(k, []).append(x)` (R14).
            Py.SetDefault(groups, episodeUuid, []).Add(node);
        }

        // PORT: the nested coroutine is a local async function.
        async Task<long> HydrateAsync(string episodeUuid, List<EntityNode> groupNodes)
        {
            var (episode, previousEpisodes) = context[episodeUuid];
            try
            {
                await NodeOperations.ExtractAttributesFromNodesAsync(
                    clients,
                    groupNodes,
                    new EpisodeOrEpisodes.One { Episode = episode },
                    previousEpisodes,
                    entityTypes,

                    // A bulk call of today passes no edges to the summary.
                    edges: unit.Bulk ? null : final.CreateEdges,
                    cancellationToken: cancellationToken);
            }
            catch (Exception error) when (!Asyncio.IsCancellation(error, cancellationToken))
            {
                // The graph write of the commit is complete. The nodes keep the summary
                // and the attributes that they have.
                var logger = Logger;
                if (logger.IsEnabled(LogLevel.Error))
                {
                    Log.AttributeExtractionFailed(
                        logger, error, Py.Repr(groupNodes.Select(node => node.Uuid).ToList()), unit.Seq, unit.GroupId);
                }

                return groupNodes.Count;
            }

            return 0;
        }

        var failed = await Helpers.SemaphoreGatherAsync(
            [.. groups.Select(item => (Func<Task<long>>)(() => HydrateAsync(item.Key, item.Value)))],
            clients.MaxCoroutines,
            cancellationToken);
        return failed.Sum();
    }

    // PORT: the parameters after `commit_result` are keyword-only in Python.

    /// <summary>
    /// Extract the attributes and the summaries of the nodes of a committed unit.
    ///
    /// For one node, the enrichment of two units runs in sequence order and not at the
    /// same time. The function reads the current node, applies the patches of the unit,
    /// and writes the node with a query that creates nothing. It returns the nodes that
    /// it wrote. An extraction error does not raise.
    /// </summary>
    internal static async Task<List<EntityNode>> EnrichUnitAsync(
        GraphitiCore graphiti,
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        AdmittedUnit unit,
        CommitResult commitResult,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes = null,
        Func<CancellationToken, Task>? wait = null,
        UnitLease? lease = null,
        GraphReader? reader = null,
        CancellationToken cancellationToken = default)
    {
        var (groupId, driver, clients) = graphiti.ResolveRequestScope(unit.GroupId);
        var waitFn = wait ?? DefaultWait(config);
        var enrichReader = reader ?? new GraphReader(clients);
        var ownLease = lease is null;
        var enrichLease = lease ?? new UnitLease(store, config, groupId, unit.Seq);

        var final = commitResult.ChangeSet;
        var patches = final.PatchesByNode();
        var createdEmbeddings = new Dictionary<string, List<double>?>(StringComparer.Ordinal);
        foreach (var node in final.CreateNodes)
        {
            createdEmbeddings[node.Uuid] = node.NameEmbedding;
        }

        // PORT: `list(dict.fromkeys(...))` keeps the first of each UUID in order (R14).
        var seen = new HashSet<string>(StringComparer.Ordinal);
        List<string> remaining = [.. final.NodeUuids.Where(seen.Add)];
        var enriched = new Dictionary<string, EntityNode>(StringComparer.Ordinal);

        await graphiti.Tracer.StartSpanAsync(
            "ingest.enrich",
            async span =>
            {
                var start = Py.TimeSeconds();
                var waited = 0.0;
                var batchCount = 0L;
                var failedCount = 0L;
                try
                {
                    while (remaining.Count > 0)
                    {
                        if (ownLease)
                        {
                            await enrichLease.HeartbeatAsync(cancellationToken);
                        }

                        var readyUuids = (await store.ReadyEnrichAsync(groupId, unit.Seq, cancellationToken)).ToHashSet(StringComparer.Ordinal);
                        List<string> ready = [.. remaining.Where(readyUuids.Contains)];
                        if (ready.Count == 0)
                        {
                            var waitStartedAt = Py.TimeSeconds();
                            await waitFn(cancellationToken);
                            waited += Py.TimeSeconds() - waitStartedAt;
                            continue;
                        }

                        var nodes = await enrichReader.NodesByUuidsAsync(ready, groupId, cancellationToken);
                        foreach (var node in nodes)
                        {
                            foreach (var patch in Py.Get(patches, node.Uuid, []))
                            {
                                DedupHelpers.PromoteResolvedNode(DeltaModule.PatchAsNode(node, patch), node);
                            }
                        }

                        failedCount += await HydrateNodesAsync(clients, unit, final, nodes, entityTypes, cancellationToken);
                        if (config.BeforeEnrichmentWrite is not null)
                        {
                            nodes = await config.BeforeEnrichmentWrite(nodes, unit, cancellationToken);
                        }

                        if (nodes.Count > 0)
                        {
                            foreach (var node in nodes)
                            {
                                if (node.NameEmbedding is null)
                                {
                                    node.NameEmbedding = Py.Get(createdEmbeddings, node.Uuid);
                                }
                            }

                            await Nodes.CreateEntityNodeEmbeddingsAsync(
                                clients.Embedder, [.. nodes.Where(node => node.NameEmbedding is null)], cancellationToken);
                            var written = nodes;

                            // PORT: the extra arguments of Python are closed over by the work function. It passes on the
                            // token that execute_write_with_timeout gives it (R9).
                            await BulkUtils.ExecuteWriteWithTimeoutAsync(
                                driver,
                                enrichLease.WriteBudget(),
                                (tx, token) => BulkUtils.UpdateEntityNodesTxAsync(tx, written, clients.Embedder, driver, token),
                                cancellationToken);
                        }

                        await store.FinishEnrichAsync(groupId, unit.Seq, ready, cancellationToken);
                        batchCount += 1;
                        remaining = [.. remaining.Where(uuid => !readyUuids.Contains(uuid))];
                        foreach (var node in nodes)
                        {
                            enriched[node.Uuid] = node;
                        }
                    }
                }
                catch (Exception)
                {
                    // Release the enrichment order of the nodes for the later units.
                    // PORT: `except BaseException` also catches a cancellation, so the cleanup passes
                    // CancellationToken.None (R8). The call has no token, so `except Exception` is a plain catch.
                    try
                    {
                        await store.FinishEnrichAsync(groupId, unit.Seq, remaining, CancellationToken.None);
                    }
                    catch (Exception)
                    {
                        Log.CouldNotReleaseEnrichment(Logger, unit.Seq, groupId);
                    }

                    throw;
                }

                span.AddAttributes(new OrderedDictionary<string, object?>
                {
                    ["ingest.seq"] = unit.Seq,
                    ["wait_ms"] = waited * 1000,
                    ["duration_ms"] = (Py.TimeSeconds() - start) * 1000,
                    ["node.count"] = (long)enriched.Count,
                    ["batch.count"] = batchCount,
                    ["node.failed"] = failedCount,
                });
            },
            cancellationToken);
        return [.. final.NodeUuids.Where(enriched.ContainsKey).Select(uuid => enriched[uuid])];
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "could not mark ingestion unit {Seq} of group {GroupId} as failed")]
        internal static partial void CouldNotMarkUnitFailed(ILogger logger, long seq, string groupId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "heartbeat of ingestion unit {Seq} of group {GroupId} failed in the admission")]
        internal static partial void AdmissionHeartbeatFailed(ILogger logger, Exception exception, long seq, string groupId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "the turn of ingestion unit {Seq} of group {GroupId} was not checked before a write")]
        internal static partial void TurnNotChecked(ILogger logger, Exception exception, long seq, string groupId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "apply attempt {Attempt} of ingestion unit {Seq} of group {GroupId} failed: {ErrorType}")]
        internal static partial void ApplyAttemptFailed(ILogger logger, long attempt, long seq, string groupId, string errorType);

        [LoggerMessage(Level = LogLevel.Error, Message = "each apply attempt of ingestion unit {Seq} of group {GroupId} failed; a part of its change set is possibly in the graph")]
        internal static partial void EachApplyAttemptFailed(ILogger logger, long seq, string groupId);

        [LoggerMessage(Level = LogLevel.Warning, Message = "recovered ingestion unit {Seq} of group {GroupId}")]
        internal static partial void RecoveredUnit(ILogger logger, long seq, string groupId);

        [LoggerMessage(Level = LogLevel.Information, Message = "committed ingestion unit {Seq} of group {GroupId}")]
        internal static partial void CommittedUnit(ILogger logger, long seq, string groupId);

        [LoggerMessage(Level = LogLevel.Error, Message = "attribute extraction failed for nodes {NodeUuids} of ingestion unit {Seq} of group {GroupId}")]
        internal static partial void AttributeExtractionFailed(ILogger logger, Exception exception, string nodeUuids, long seq, string groupId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "could not release the enrichment entries of unit {Seq} of group {GroupId}")]
        internal static partial void CouldNotReleaseEnrichment(ILogger logger, long seq, string groupId);
    }
}
