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

// PORT: the module-level `logger`. ConcurrentIngestor logs, so the Logger property is internal (R5).
internal static class Scheduler
{
    internal static ILogger Logger => PortServices.Logger(typeof(Scheduler));
}

// PORT: the private dataclass `_GroupState`. `asyncio.Semaphore(n)` and `asyncio.Lock` are SemaphoreSlim, and
// `asyncio.Event` is AsyncEvent (R8).
internal sealed record GroupState
{
    internal required SemaphoreSlim Window { get; set; }

    internal SemaphoreSlim AdmitLock { get; set; } = new(1, 1);

    internal AsyncEvent Changed { get; set; } = new();

    // TODO(port): _GroupState.notify
    internal void Notify()
    {
        throw new NotSupportedException("TODO(port): _GroupState.notify");
    }
}

// PORT: the private attribute `_groups` (a Dictionary<string, GroupState>, read only by key, R14) is a field of the
// unit. A skeleton field that no member reads fails the build (CS0169).
internal sealed class ConcurrentIngestor
{
    // TODO(port): ConcurrentIngestor.__init__
    internal ConcurrentIngestor(GraphitiCore graphiti, ConcurrentIngestionConfig config)
    {
        throw new NotSupportedException("TODO(port): ConcurrentIngestor.__init__");
    }

    // PORT: the attribute `graphiti`. Inside this class the name Graphiti binds to the property, so a qualified name
    // of the namespace Graphiti needs `global::`.
    internal GraphitiCore Graphiti { get; set; }

    internal ConcurrentIngestionConfig Config { get; set; }

    internal IngestionStateStore Store { get; set; }

    // TODO(port): ConcurrentIngestor._group
    private GroupState Group(string groupId)
    {
        throw new NotSupportedException("TODO(port): ConcurrentIngestor._group");
    }

    // PORT: the result is the nested async function `wait` (TYPE_HINTS.md), an async callable (R8).
    // TODO(port): ConcurrentIngestor._wait_local
    private Func<CancellationToken, Task> WaitLocal(GroupState state)
    {
        throw new NotSupportedException("TODO(port): ConcurrentIngestor._wait_local");
    }

    // TODO(port): ConcurrentIngestor._heartbeat
    private Task HeartbeatAsync(UnitLease lease, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): ConcurrentIngestor._heartbeat");
    }

    // PORT: `span: Any` is the TracerSpan of `start_span` (R16). `analyze: Any` is the nested async function `analyze`
    // of add_episode and add_episode_bulk (R8).
    // PORT: `commit_kwargs: dict[str, Any]` holds the keyword arguments `saga`, `saga_previous_episode_uuid`, and
    // `identity_properties` that `_run_unit` passes to commit_unit with `**commit_kwargs`.
    // TODO(port): no rule for a dict of keyword arguments that is not a query parameter dict
    // TODO(port): ConcurrentIngestor._run_unit
    private Task<(CommitResult, List<EntityNode>)> RunUnitAsync(
        GroupState state,
        AdmittedUnit unit,
        TracerSpan span,
        Func<AdmittedUnit, CancellationToken, Task<ChangeSet>> analyze,
        OrderedDictionary<string, object?> commitKwargs,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): ConcurrentIngestor._run_unit");
    }

    // PORT: each parameter is keyword-only in Python, and only `identity_properties` has a default. `saga` is the
    // `SagaOrName?` of graphiti.py (Part 3).
    // TODO(port): ConcurrentIngestor.add_episode
    internal Task<AddEpisodeResults> AddEpisodeAsync(
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
        throw new NotSupportedException("TODO(port): ConcurrentIngestor.add_episode");
    }

    // PORT: each parameter is keyword-only in Python, and only `identity_properties` has a default.
    // TODO(port): ConcurrentIngestor.add_episode_bulk
    internal Task<AddBulkEpisodeResults> AddEpisodeBulkAsync(
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
        throw new NotSupportedException("TODO(port): ConcurrentIngestor.add_episode_bulk");
    }

    // TODO(port): ConcurrentIngestor.close
    internal Task CloseAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): ConcurrentIngestor.close");
    }
}
