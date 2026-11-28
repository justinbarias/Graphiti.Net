// Ported from graphiti_core/ingestion/scheduler.py
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

using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: the module-level `logger`. ConcurrentIngestor logs, so the Logger property and the class Log are internal (R5).
internal static partial class Scheduler
{
    internal static ILogger Logger => PortServices.Logger(typeof(Scheduler));

    internal static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "heartbeat of ingestion unit {Seq} of group {GroupId} failed")]
        internal static partial void HeartbeatFailed(ILogger logger, Exception exception, long seq, string groupId);
    }
}

/// <summary>The window, the admit lock, and the wake-up event of one graph.</summary>
// PORT: the private dataclass `_GroupState`. `asyncio.Semaphore(n)` is AsyncSemaphore, `asyncio.Lock` is AsyncLock, and
// `asyncio.Event` is AsyncEvent (R8).
internal sealed record GroupState
{
    internal required AsyncSemaphore Window { get; set; }

    internal AsyncLock AdmitLock { get; set; } = new();

    internal AsyncEvent Changed { get; set; } = new();

    /// <summary>Wake up each unit of the graph that waits for a different unit.</summary>
    internal void Notify()
    {
        Changed.Set();
        Changed = new AsyncEvent();
    }
}

/// <summary>
/// The in-process scheduler of concurrent ingestion.
///
/// For each graph, at most `window` units run at one time. A unit keeps its window
/// slot from admission to the end of enrichment. The window slot and the admit lock
/// are the first two awaits of a call. Calls that one task starts in order thus get
/// their sequence numbers in that order.
/// </summary>
// PORT: the private attribute `_groups` (a Dictionary<string, GroupState>, read only by key, R14) is a field of the
// unit. It is internal, as are `_group`, `_wait_local`, `_heartbeat`, and `_run_unit`, because the recording test of the
// unit reads them (R2).
internal sealed class ConcurrentIngestor
{
    internal Dictionary<string, GroupState> groups;

    internal ConcurrentIngestor(GraphitiCore graphiti, ConcurrentIngestionConfig config)
    {
        Graphiti = graphiti;
        Config = config;
        Store = config.GetStateStore();
        groups = new(StringComparer.Ordinal);
    }

    // PORT: the attribute `graphiti`. Inside this class the name Graphiti binds to the property, so a qualified name
    // of the namespace Graphiti needs `global::`.
    internal GraphitiCore Graphiti { get; set; }

    internal ConcurrentIngestionConfig Config { get; set; }

    internal IngestionStateStore Store { get; set; }

    internal GroupState Group(string groupId)
    {
        var state = groups.GetValueOrDefault(groupId);
        if (state is null)
        {
            state = new GroupState { Window = new AsyncSemaphore(Config.Window) };
            groups[groupId] = state;
        }

        return state;
    }

    // PORT: the result is the nested async function `wait` (TYPE_HINTS.md), an async callable (R8).
    internal Func<CancellationToken, Task> WaitLocal(GroupState state)
    {
        // PORT: the nested coroutine is a local async function (R8).
        async Task WaitAsync(CancellationToken cancellationToken)
        {
            // A unit of this process wakes up the waiters. The poll interval applies
            // to a change that a different process made.
            try
            {
                await Asyncio.WaitForAsync(
                    token => state.Changed.WaitAsync(token),
                    Config.PollIntervalSeconds,
                    PortServices.Time,
                    cancellationToken);
            }
            catch (TimeoutException)
            {
                // PORT: contextlib.suppress(asyncio.TimeoutError)
            }
        }

        return WaitAsync;
    }

    internal async Task HeartbeatAsync(UnitLease lease, CancellationToken cancellationToken = default)
    {
        var interval = Py.TrueDiv(Config.LeaseSeconds, 3);
        while (true)
        {
            await Asyncio.SleepAsync(interval, PortServices.Time, cancellationToken);
            try
            {
                await lease.HeartbeatAsync(cancellationToken);
            }
            catch (Exception error) when (error is UnitExpiredException or StaleCommitException)
            {
                // The unit is expired or failed, or a different attempt has its turn.
                // The next store call of the unit raises the error.
                return;
            }
            catch (Exception error) when (!Asyncio.IsCancellation(error, cancellationToken))
            {
                Scheduler.Log.HeartbeatFailed(Scheduler.Logger, error, lease.Seq, lease.GroupId);
            }
        }
    }

    /// <summary>Run the Analyze, Commit, and Enrich phases of an admitted unit.</summary>
    // PORT: `span: Any` is the TracerSpan of `start_span` (R16). `analyze: Any` is the nested async function `analyze`
    // of add_episode and add_episode_bulk (R8).
    // PORT: `commit_kwargs: dict[str, Any]` is one typed parameter for each key that a caller writes: `saga`,
    // `saga_previous_episode_uuid`, and `identity_properties` (R2). The bulk caller writes no
    // `saga_previous_episode_uuid` and passes null, the default of commit_unit.
    internal async Task<(CommitResult, List<EntityNode>)> RunUnitAsync(
        GroupState state,
        AdmittedUnit unit,
        TracerSpan span,
        Func<AdmittedUnit, CancellationToken, Task<ChangeSet>> analyze,
        SagaOrName? saga,
        string? sagaPreviousEpisodeUuid,
        OrderedDictionary<string, List<string>>? identityProperties,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        CancellationToken cancellationToken = default)
    {
        var (graphiti, store, config) = (Graphiti, Store, Config);
        span.AddAttributes(new OrderedDictionary<string, object?>
        {
            ["ingest.mode"] = "concurrent",
            ["ingest.seq"] = unit.Seq,
            ["ingest.window"] = config.Window,
            ["ingest.unit_id"] = unit.UnitId,
            ["group_id"] = unit.GroupId,
        });
        var wait = WaitLocal(state);
        var lease = new UnitLease(store, config, unit.GroupId, unit.Seq);
        var heartbeat = Asyncio.CreateTask(token => HeartbeatAsync(lease, token), out var cancelHeartbeat);
        CommitResult? commitResult = null;
        try
        {
            await lease.HeartbeatAsync(cancellationToken);
            var changeSet = await analyze(unit, cancellationToken);
            commitResult = await Phases.CommitUnitAsync(
                graphiti,
                store,
                config,
                unit,
                changeSet,
                entityTypes: entityTypes,
                wait: wait,
                lease: lease,
                saga: saga,
                sagaPreviousEpisodeUuid: sagaPreviousEpisodeUuid,
                identityProperties: identityProperties,
                cancellationToken: cancellationToken);
            state.Notify();
            var nodes = await Phases.EnrichUnitAsync(
                graphiti,
                store,
                config,
                unit,
                commitResult,
                entityTypes: entityTypes,
                wait: wait,
                lease: lease,
                cancellationToken: cancellationToken);
            return (commitResult, nodes);
        }
        catch (Exception error)
        {
            if (commitResult is null)
            {
                // The call has no effect on a unit that has a stored final change set.
                // PORT: after a caught cancellation the call passes CancellationToken.None (R8). After a different
                // error, Python lets a new cancellation into the call, so it passes the token of the caller.
                await Phases.FailUnitAsync(
                    store,
                    unit.GroupId,
                    unit.Seq,
                    error,
                    Asyncio.IsCancellation(error, cancellationToken) ? CancellationToken.None : cancellationToken);
            }

            throw;
        }
        finally
        {
            cancelHeartbeat.Cancel();
            await Asyncio.GatherAsync([heartbeat], returnExceptions: true);

            // PORT: the source of the token of the task (R8), after the last use of the task.
            cancelHeartbeat.Dispose();
            state.Notify();
        }
    }

    // PORT: each parameter is keyword-only in Python, and only `identity_properties` has a default. `saga` is the
    // `SagaOrName?` of graphiti.py (Part 3).
    // PORT: `async with state.window` and `async with state.admit_lock` are AcquireAsync, and Release in `finally` (R8).
    // `with graphiti.tracer.start_span(...)` is StartSpanAsync with the body as a lambda (R16).
    internal async Task<AddEpisodeResults> AddEpisodeAsync(
        string name,
        string episodeBody,
        string sourceDescription,
        PyDateTime referenceTime,
        EpisodeType source,
        string groupId,
        string? uuid,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        List<string>? excludedEntityTypes,
        List<string>? previousEpisodeUuids,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes,
        OrderedDictionary<(string, string), List<string>>? edgeTypeMap,
        string? customExtractionInstructions,
        SagaOrName? saga,
        string? sagaPreviousEpisodeUuid,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        var (graphiti, store, config) = (Graphiti, Store, Config);
        var state = Group(groupId);

        await state.Window.AcquireAsync(cancellationToken);
        try
        {
            return await graphiti.Tracer.StartSpanAsync(
                "add_episode",
                async span =>
                {
                    CommitResult commitResult;
                    List<EntityNode> nodes;
                    try
                    {
                        AdmittedUnit unit;
                        await state.AdmitLock.AcquireAsync(cancellationToken);
                        try
                        {
                            unit = await Phases.AdmitEpisodeAsync(
                                graphiti,
                                store,
                                config,
                                unitId: Py.Uuid4(),
                                groupId: groupId,
                                name: name,
                                episodeBody: episodeBody,
                                sourceDescription: sourceDescription,
                                referenceTime: referenceTime,
                                source: source,
                                uuid: uuid,
                                previousEpisodeUuids: previousEpisodeUuids,
                                cancellationToken: cancellationToken);
                        }
                        finally
                        {
                            state.AdmitLock.Release();
                        }

                        // PORT: the nested coroutine is a local async function (R8).
                        async Task<ChangeSet> AnalyzeAsync(AdmittedUnit unit, CancellationToken cancellationToken)
                        {
                            return await Phases.AnalyzeEpisodeAsync(
                                graphiti,
                                store,
                                config,
                                unit,
                                entityTypes: entityTypes,
                                excludedEntityTypes: excludedEntityTypes,
                                edgeTypes: edgeTypes,
                                edgeTypeMap: edgeTypeMap,
                                customExtractionInstructions: customExtractionInstructions,
                                identityProperties: identityProperties,
                                cancellationToken: cancellationToken);
                        }

                        (commitResult, nodes) = await RunUnitAsync(
                            state,
                            unit,
                            span,
                            AnalyzeAsync,
                            saga,
                            sagaPreviousEpisodeUuid,
                            identityProperties,
                            entityTypes,
                            cancellationToken);
                    }
                    catch (Exception error) when (!Asyncio.IsCancellation(error, cancellationToken))
                    {
                        span.SetStatus("error", error.Message);
                        span.RecordException(error);
                        throw;
                    }

                    var final = commitResult.ChangeSet;
                    span.AddAttributes(new OrderedDictionary<string, object?>
                    {
                        ["episode.uuid"] = final.Episodes[0].Uuid,
                        ["node.count"] = (long)nodes.Count,
                        ["edge.count"] = (long)commitResult.Edges.Count,
                    });
                    // PORT: pydantic makes a new list for each list field, so each list is a copy (R7).
                    return new AddEpisodeResults
                    {
                        Episode = final.Episodes[0],
                        EpisodicEdges = [.. final.EpisodicEdges],
                        Nodes = [.. nodes],
                        Edges = [.. commitResult.Edges],
                        Communities = [],
                        CommunityEdges = [],
                    }.Validate();
                },
                cancellationToken);
        }
        finally
        {
            state.Window.Release();
        }
    }

    // PORT: each parameter is keyword-only in Python, and only `identity_properties` has a default.
    internal async Task<AddBulkEpisodeResults> AddEpisodeBulkAsync(
        List<RawEpisode> bulkEpisodes,
        string groupId,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        List<string>? excludedEntityTypes,
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes,
        OrderedDictionary<(string, string), List<string>>? edgeTypeMap,
        bool strictEdgeTypes,
        string? customExtractionInstructions,
        SagaOrName? saga,
        bool useCombinedExtraction,
        OrderedDictionary<string, List<string>>? identityProperties = null,
        CancellationToken cancellationToken = default)
    {
        var (graphiti, store, config) = (Graphiti, Store, Config);
        var state = Group(groupId);

        await state.Window.AcquireAsync(cancellationToken);
        try
        {
            return await graphiti.Tracer.StartSpanAsync(
                "add_episode_bulk",
                async span =>
                {
                    span.AddAttributes(new OrderedDictionary<string, object?> { ["episode.count"] = (long)bulkEpisodes.Count });
                    CommitResult commitResult;
                    List<EntityNode> nodes;
                    try
                    {
                        AdmittedUnit unit;
                        await state.AdmitLock.AcquireAsync(cancellationToken);
                        try
                        {
                            unit = await Phases.AdmitBulkAsync(
                                graphiti,
                                store,
                                config,
                                unitId: Py.Uuid4(),
                                groupId: groupId,
                                bulkEpisodes: bulkEpisodes,
                                cancellationToken: cancellationToken);
                        }
                        finally
                        {
                            state.AdmitLock.Release();
                        }

                        // PORT: the nested coroutine is a local async function (R8).
                        async Task<ChangeSet> AnalyzeAsync(AdmittedUnit unit, CancellationToken cancellationToken)
                        {
                            return await Phases.AnalyzeBulkAsync(
                                graphiti,
                                store,
                                config,
                                unit,
                                entityTypes: entityTypes,
                                excludedEntityTypes: excludedEntityTypes,
                                edgeTypes: edgeTypes,
                                edgeTypeMap: edgeTypeMap,
                                customExtractionInstructions: customExtractionInstructions,
                                identityProperties: identityProperties,
                                strictEdgeTypes: strictEdgeTypes,
                                useCombinedExtraction: useCombinedExtraction,
                                cancellationToken: cancellationToken);
                        }

                        (commitResult, nodes) = await RunUnitAsync(
                            state,
                            unit,
                            span,
                            AnalyzeAsync,
                            saga,
                            null,
                            identityProperties,
                            entityTypes,
                            cancellationToken);
                    }
                    catch (Exception error) when (!Asyncio.IsCancellation(error, cancellationToken))
                    {
                        span.SetStatus("error", error.Message);
                        span.RecordException(error);
                        throw;
                    }

                    var final = commitResult.ChangeSet;
                    span.AddAttributes(new OrderedDictionary<string, object?>
                    {
                        ["node.count"] = (long)nodes.Count,
                        ["edge.count"] = (long)commitResult.Edges.Count,
                    });
                    // PORT: pydantic makes a new list for each list field, so each list is a copy (R7).
                    return new AddBulkEpisodeResults
                    {
                        Episodes = [.. final.Episodes],
                        EpisodicEdges = [.. final.EpisodicEdges],
                        Nodes = [.. nodes],
                        Edges = [.. commitResult.Edges],
                        Communities = [],
                        CommunityEdges = [],
                    }.Validate();
                },
                cancellationToken);
        }
        finally
        {
            state.Window.Release();
        }
    }

    /// <summary>Close the state store.</summary>
    internal async Task CloseAsync(CancellationToken cancellationToken = default)
    {
        await Store.CloseAsync(cancellationToken);
    }
}
