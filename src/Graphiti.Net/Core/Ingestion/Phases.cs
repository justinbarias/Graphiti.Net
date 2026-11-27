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

using Graphiti.Core.Driver;
using Graphiti.Core.Utils;
using Graphiti.Internal;
using Graphiti.PyCompat;
using Microsoft.Extensions.Logging;

// PORT: `WaitFn = Callable[[], Awaitable[None]]` is a type alias, so it has no C# type (R2). Each use is
// `Func<CancellationToken, Task>`: an async callable takes a CancellationToken last (R8).
// PORT: `dict[str, type[BaseModel]]` is an OrderedDictionary<string, GraphTypeDescriptor> (R15). `edge_type_map:
// dict[tuple[str, str], list[str]]` is an OrderedDictionary<(string, string), List<string>>. A commit report
// `dict[str, float]` is an OrderedDictionary<string, double>, because commit_unit spreads it into span attributes.
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

    // TODO(port): new_report
    internal static OrderedDictionary<string, double> NewReport()
    {
        throw new NotSupportedException("TODO(port): new_report");
    }
}

internal sealed record AdmittedUnit
{
    internal required string GroupId { get; set; }

    internal required string UnitId { get; set; }

    internal required long Seq { get; set; }

    internal required PyDateTime CreatedAt { get; set; }

    internal bool Bulk { get; set; }

    internal required List<EpisodicNode> Episodes { get; set; }

    internal required List<List<EpisodicNode>> PreviousEpisodes { get; set; }

    // TODO(port): AdmittedUnit.Validate (R7)
    internal AdmittedUnit Validate()
    {
        throw new NotSupportedException("TODO(port): AdmittedUnit.Validate");
    }
}

internal sealed record CommitResult
{
    internal required ChangeSet ChangeSet { get; set; }

    internal List<EntityEdge> Edges { get; set; } = [];

    internal OrderedDictionary<string, double> Report { get; set; } = new();

    // TODO(port): CommitResult.Validate (R7)
    internal CommitResult Validate()
    {
        throw new NotSupportedException("TODO(port): CommitResult.Validate");
    }
}

// PORT: the private attribute `_deadline` (a `double?`) is a field of the unit. A skeleton field that no member reads
// fails the build (CS0169).
internal sealed class UnitLease
{
    // PORT: `clock: Callable[[], float] = time.monotonic`. A delegate cannot be a default value, so null stands for
    // `time.monotonic`, which reads PortServices.Time (R8).
    // TODO(port): UnitLease.__init__
    internal UnitLease(
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        string groupId,
        long seq,
        Func<double>? clock = null)
    {
        throw new NotSupportedException("TODO(port): UnitLease.__init__");
    }

    internal IngestionStateStore Store { get; set; }

    internal ConcurrentIngestionConfig Config { get; set; }

    internal string GroupId { get; set; }

    internal long Seq { get; set; }

    internal Func<double> Clock { get; set; }

    internal long? Fence { get; set; }

    // TODO(port): UnitLease.renewed
    internal void Renewed(double callStartedAt)
    {
        throw new NotSupportedException("TODO(port): UnitLease.renewed");
    }

    // TODO(port): UnitLease.heartbeat
    internal Task HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): UnitLease.heartbeat");
    }

    // TODO(port): UnitLease.write_budget
    internal double WriteBudget()
    {
        throw new NotSupportedException("TODO(port): UnitLease.write_budget");
    }
}

internal static partial class Phases
{
    // TODO(port): _default_wait
    private static Func<CancellationToken, Task> DefaultWait(ConcurrentIngestionConfig config)
    {
        throw new NotSupportedException("TODO(port): _default_wait");
    }

    // PORT: internal, because ingestion/scheduler.py imports it (R2). `error: BaseException` is an Exception.
    // TODO(port): _fail_unit
    internal static Task FailUnitAsync(
        IngestionStateStore store,
        string groupId,
        long seq,
        Exception error,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _fail_unit");
    }

    // TODO(port): _default_edge_type_map
    private static OrderedDictionary<(string, string), List<string>> DefaultEdgeTypeMap(
        OrderedDictionary<string, GraphTypeDescriptor>? edgeTypes)
    {
        throw new NotSupportedException("TODO(port): _default_edge_type_map");
    }

    // PORT: no annotation for the result. TYPE_HINTS.md gives Unknown, and the value is a copy of the episode (R20).
    // TODO(port): _written_episode
    private static EpisodicNode WrittenEpisode(GraphitiCore graphiti, EpisodicNode episode, List<string> entityEdges)
    {
        throw new NotSupportedException("TODO(port): _written_episode");
    }

    // PORT: `driver: Any`. Each caller passes the GraphDriver of `_resolve_request_scope` (R20).
    // TODO(port): _check_driver
    private static void CheckDriver(GraphDriver driver)
    {
        throw new NotSupportedException("TODO(port): _check_driver");
    }

    // TODO(port): _begin_admit
    private static Task<Admission> BeginAdmitAsync(
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        string groupId,
        string unitId,
        Func<CancellationToken, Task> wait,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _begin_admit");
    }

    // PORT: an @asynccontextmanager is a method that returns an IAsyncDisposable, used with `await using` (R16). The
    // code before `yield` starts the heartbeat task, and the code in `finally` runs in DisposeAsync.
    // TODO(port): _admit_heartbeat
    private static IAsyncDisposable AdmitHeartbeat(
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        string groupId,
        Admission admission)
    {
        throw new NotSupportedException("TODO(port): _admit_heartbeat");
    }

    // TODO(port): _episode_uuid
    private static string EpisodeUuid(string groupId, string unitId, long index = 0)
    {
        throw new NotSupportedException("TODO(port): _episode_uuid");
    }

    // PORT: `driver: Any` is the GraphDriver of the caller (R20). Code reads the result only by key (R14).
    // TODO(port): _written_episodes
    private static Task<Dictionary<string, EpisodicNode>> WrittenEpisodesAsync(
        GraphDriver driver,
        List<string> episodeUuids,
        string groupId,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _written_episodes");
    }

    // TODO(port): _fail_admission
    private static Task FailAdmissionAsync(
        IngestionStateStore store,
        string groupId,
        Admission admission,
        Exception error,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _fail_admission");
    }

    // PORT: the parameters after `config` are keyword-only in Python.
    // TODO(port): admit_episode
    internal static Task<AdmittedUnit> AdmitEpisodeAsync(
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
        throw new NotSupportedException("TODO(port): admit_episode");
    }

    // PORT: the parameters after `config` are keyword-only in Python.
    // TODO(port): admit_bulk
    internal static Task<AdmittedUnit> AdmitBulkAsync(
        GraphitiCore graphiti,
        IngestionStateStore store,
        ConcurrentIngestionConfig config,
        string unitId,
        string? groupId,
        List<RawEpisode> bulkEpisodes,
        Func<CancellationToken, Task>? wait = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): admit_bulk");
    }

    // PORT: `records: list[Any]`. The callers pass the created nodes or the created edges, so there is one overload
    // for each type (R15, as for `_enum_value`).
    // TODO(port): _unique_by_uuid
    private static List<EntityNode> UniqueByUuid(List<EntityNode> records)
    {
        throw new NotSupportedException("TODO(port): _unique_by_uuid");
    }

    // TODO(port): _unique_by_uuid
    private static List<EntityEdge> UniqueByUuid(List<EntityEdge> records)
    {
        throw new NotSupportedException("TODO(port): _unique_by_uuid");
    }

    // PORT: `span: Any` is the TracerSpan of `start_span` (R16).
    // TODO(port): _save_analysis
    private static Task SaveAnalysisAsync(
        GraphitiCore graphiti,
        IngestionStateStore store,
        AdmittedUnit unit,
        ChangeSet changeSet,
        GraphitiClients clients,
        TracerSpan span,
        double start,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _save_analysis");
    }

    // PORT: the parameters after `unit` are keyword-only in Python.
    // TODO(port): analyze_episode
    internal static Task<ChangeSet> AnalyzeEpisodeAsync(
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
        throw new NotSupportedException("TODO(port): analyze_episode");
    }

    // PORT: the parameters after `unit` are keyword-only in Python.
    // TODO(port): analyze_bulk
    internal static Task<ChangeSet> AnalyzeBulkAsync(
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
        throw new NotSupportedException("TODO(port): analyze_bulk");
    }

    // TODO(port): _unique_episodic_edges
    private static List<EpisodicEdge> UniqueEpisodicEdges(List<EpisodicEdge> edges)
    {
        throw new NotSupportedException("TODO(port): _unique_episodic_edges");
    }

    // PORT: code reads `replaced` only by key, and tests `dropped` with `in` only (R14).
    // TODO(port): _replace_entity_edges
    private static void ReplaceEntityEdges(
        ChangeSet changeSet,
        Dictionary<string, string> replaced,
        HashSet<string>? dropped = null,
        List<string>? added = null)
    {
        throw new NotSupportedException("TODO(port): _replace_entity_edges");
    }

    // PORT: code reads the result only by key (R14).
    // TODO(port): _episode_context
    private static Dictionary<string, (EpisodicNode, List<EpisodicNode>)> EpisodeContext(AdmittedUnit unit)
    {
        throw new NotSupportedException("TODO(port): _episode_context");
    }

    // TODO(port): _repair_nodes
    private static Task RepairNodesAsync(
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
        throw new NotSupportedException("TODO(port): _repair_nodes");
    }

    // TODO(port): _remap_edges
    private static void RemapEdges(ChangeSet final, OrderedDictionary<string, double> report)
    {
        throw new NotSupportedException("TODO(port): _remap_edges");
    }

    // TODO(port): _move_end_date_to_earliest_contradiction
    private static void MoveEndDateToEarliestContradiction(EntityEdge edge, List<EntityEdge> contradicted)
    {
        throw new NotSupportedException("TODO(port): _move_end_date_to_earliest_contradiction");
    }

    // TODO(port): _repair_edges
    private static Task RepairEdgesAsync(
        GraphitiClients clients,
        GraphReader reader,
        ChangeSet final,
        Delta delta,
        AdmittedUnit unit,
        OrderedDictionary<string, double> report,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _repair_edges");
    }

    // PORT: the parameters after `delta_units` are keyword-only in Python. `unit` has no default.
    // TODO(port): rebase_change_set
    internal static Task<ChangeSet> RebaseChangeSetAsync(
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
        throw new NotSupportedException("TODO(port): rebase_change_set");
    }

    // TODO(port): _episodes_with_stored_links
    private static Task<List<EpisodicNode>> EpisodesWithStoredLinksAsync(
        GraphReader reader,
        List<EpisodicNode> episodes,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _episodes_with_stored_links");
    }
}

internal sealed record ApplyResult
{
    internal List<EntityEdge> ChangedEdges { get; set; } = [];

    internal long Applied { get; set; }

    internal long Dropped { get; set; }

    // TODO(port): ApplyResult.Validate (R7)
    internal ApplyResult Validate()
    {
        throw new NotSupportedException("TODO(port): ApplyResult.Validate");
    }
}

internal static partial class Phases
{
    // PORT: `write_budget: Callable[[], Awaitable[float]]` is an async callable (R8).
    // TODO(port): _write_saga_links
    private static Task WriteSagaLinksAsync(
        GraphitiClients clients,
        ChangeSet changeSet,
        Func<CancellationToken, Task<double>> writeBudget,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _write_saga_links");
    }

    // PORT: the parameters after `change_set` are keyword-only in Python. `write_budget` has no default.
    // TODO(port): apply_change_set
    internal static Task<ApplyResult> ApplyChangeSetAsync(
        GraphitiClients clients,
        ChangeSet changeSet,
        Func<double> writeBudget,
        GraphReader? reader = null,
        Func<CancellationToken, Task>? checkTurn = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): apply_change_set");
    }

    // TODO(port): _apply_with_retries
    private static Task<List<EntityEdge>> ApplyWithRetriesAsync(
        GraphitiClients clients,
        ChangeSet final,
        Func<double> writeBudget,
        long maxAttempts,
        OrderedDictionary<string, double> report,
        GraphReader reader,
        Func<CancellationToken, Task> checkTurn,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _apply_with_retries");
    }

    // TODO(port): _mark_apply_failed
    private static Task MarkApplyFailedAsync(
        IngestionStateStore store,
        string groupId,
        long seq,
        long fence,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _mark_apply_failed");
    }

    // PORT: code reads `turns` only by key (R14).
    // TODO(port): _recover_unit
    private static Task RecoverUnitAsync(
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
        throw new NotSupportedException("TODO(port): _recover_unit");
    }

    // PORT: `saga: str | SagaNode | None` is the `SagaOrName?` of graphiti.py (Part 3).
    // TODO(port): _build_saga_links
    private static Task<(List<SagaLink>, SagaNode?, bool)> BuildSagaLinksAsync(
        GraphitiCore graphiti,
        GraphitiClients clients,
        AdmittedUnit unit,
        SagaOrName? saga,
        string? sagaPreviousEpisodeUuid,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _build_saga_links");
    }

    // PORT: the parameters after `change_set` are keyword-only in Python. `saga` is the `object?` of
    // BuildSagaLinksAsync.
    // TODO(port): commit_unit
    internal static Task<CommitResult> CommitUnitAsync(
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
        throw new NotSupportedException("TODO(port): commit_unit");
    }

    // TODO(port): _hydrate_nodes
    private static Task<long> HydrateNodesAsync(
        GraphitiClients clients,
        AdmittedUnit unit,
        ChangeSet final,
        List<EntityNode> nodes,
        OrderedDictionary<string, GraphTypeDescriptor>? entityTypes,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("TODO(port): _hydrate_nodes");
    }

    // PORT: the parameters after `commit_result` are keyword-only in Python.
    // TODO(port): enrich_unit
    internal static Task<List<EntityNode>> EnrichUnitAsync(
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
        throw new NotSupportedException("TODO(port): enrich_unit");
    }
}
