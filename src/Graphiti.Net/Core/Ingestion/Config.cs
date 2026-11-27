// Ported from graphiti_core/ingestion/config.py
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

// before_commit_write(change_set) -> change_set
// before_enrichment_write(nodes, unit) -> nodes
// PORT: `BeforeCommitWriteHook` and `BeforeEnrichmentWriteHook` are type aliases, so they have no C# type (R2). Each
// use is the aliased delegate type. An async callable takes a CancellationToken last (R8).

/// <summary>
/// Settings of concurrent ingestion for one graph.
///
/// The mode is off when `Graphiti` gets no object of this class.
///
/// Parameters
/// ----------
/// window : int
///     The largest number of units of one graph that run at one time in this process.
///     Each unit also runs its model calls through `semaphore_gather`, so the largest
///     number of model calls at one time is `window` times `SEMAPHORE_LIMIT`.
/// state_store : IngestionStateStore | None
///     The store of the sequence, the change sets, and the enrichment order. The default
///     is a new `InMemoryIngestionStateStore`.
/// lease_seconds : float
///     The time after which a unit with no heartbeat is expired.
/// write_margin_seconds : float
///     The time before the local deadline at which a graph write must be complete.
/// poll_interval_seconds : float
///     The wait time between two store calls when a unit waits for a different process.
/// index_lag_seconds : float
///     The longest time between a completed graph write and the time when a search
///     returns the record.
/// max_apply_attempts : int
///     The number of attempts to write one final change set.
/// enabled_for_group : Callable[[str], bool] | None
///     When the function returns False for a group_id, that call uses serial ingestion.
/// before_commit_write : async Callable[[ChangeSet], ChangeSet] | None
///     Runs after the repair and before the final change set is stored.
/// before_enrichment_write : async Callable[[list[EntityNode], AdmittedUnit], list[EntityNode]] | None
///     Runs before each enrichment write.
/// </summary>
// PORT: `ConfigDict(arbitrary_types_allowed=True)` needs nothing (R7).
internal sealed record ConcurrentIngestionConfig
{
    internal required long Window { get; set; }

    // PORT: `state_store: Any = None`. The validator accepts only an IngestionStateStore, and get_state_store returns
    // it, so the type is IngestionStateStore (R20).
    internal IngestionStateStore? StateStore { get; set; }

    internal double LeaseSeconds { get; set; } = 60.0;

    internal double WriteMarginSeconds { get; set; } = 5.0;

    internal double PollIntervalSeconds { get; set; } = 0.25;

    internal double IndexLagSeconds { get; set; } = 300.0;

    internal long MaxApplyAttempts { get; set; } = 5;

    internal Func<string, bool>? EnabledForGroup { get; set; }

    // PORT: `BeforeCommitWriteHook = Callable[[Any], Awaitable[Any]]`. commit_unit passes the final ChangeSet and
    // stores the result as the final ChangeSet (ingestion/phases.py:1661), so the types are ChangeSet (R20).
    internal Func<ChangeSet, CancellationToken, Task<ChangeSet>>? BeforeCommitWrite { get; set; }

    // PORT: `BeforeEnrichmentWriteHook = Callable[[list[Any], Any], Awaitable[list[Any]]]`. enrich_unit passes the
    // nodes and the AdmittedUnit, and writes the nodes of the result (ingestion/phases.py:1805, R20).
    internal Func<List<EntityNode>, AdmittedUnit, CancellationToken, Task<List<EntityNode>>>? BeforeEnrichmentWrite { get; set; }

    // PORT: the constraints of the fields and `@model_validator(mode='after') _check_values` run here (R7).
    internal ConcurrentIngestionConfig Validate()
    {
        var f = new PydanticFields("ConcurrentIngestionConfig");
        f.Ge("window", Window, 1);
        f.Gt("lease_seconds", LeaseSeconds, 0);
        f.Ge("write_margin_seconds", WriteMarginSeconds, 0);
        f.Gt("poll_interval_seconds", PollIntervalSeconds, 0);
        f.Ge("index_lag_seconds", IndexLagSeconds, 0);
        f.Ge("max_apply_attempts", MaxApplyAttempts, 1);
        f.ThrowIfAny();
        Pydantic.Wrap(
            () =>
            {
                if (WriteMarginSeconds >= Py.TrueDiv(LeaseSeconds, 3))
                {
                    throw new ArgumentException("write_margin_seconds must be less than lease_seconds / 3");
                }

                // PORT: `isinstance(self.state_store, IngestionStateStore)` holds for each value of the typed property.
                // ModelValidate raises the error of this branch for a value of another type.
            },
            "ConcurrentIngestionConfig");
        return this;
    }

    internal static ConcurrentIngestionConfig ModelValidate(OrderedDictionary<string, object?> data)
    {
        var f = new PydanticFields("ConcurrentIngestionConfig", data);
        var window = f.Required("window", Pydantic.Int);
        var stateStore = f.Default("state_store", Pydantic.Any, () => null);
        var leaseSeconds = f.Default("lease_seconds", Pydantic.Float, () => 60.0);
        var writeMarginSeconds = f.Default("write_margin_seconds", Pydantic.Float, () => 5.0);
        var pollIntervalSeconds = f.Default("poll_interval_seconds", Pydantic.Float, () => 0.25);
        var indexLagSeconds = f.Default("index_lag_seconds", Pydantic.Float, () => 300.0);
        var maxApplyAttempts = f.Default("max_apply_attempts", Pydantic.Int, () => 5L);
        var enabledForGroup = f.Default("enabled_for_group", v => Pydantic.Optional(v, Pydantic.Callable<Func<string, bool>>), () => null);
        var beforeCommitWrite = f.Default(
            "before_commit_write",
            v => Pydantic.Optional(v, Pydantic.Callable<Func<ChangeSet, CancellationToken, Task<ChangeSet>>>),
            () => null);
        var beforeEnrichmentWrite = f.Default(
            "before_enrichment_write",
            v => Pydantic.Optional(v, Pydantic.Callable<Func<List<EntityNode>, AdmittedUnit, CancellationToken, Task<List<EntityNode>>>>),
            () => null);
        f.ThrowIfAny();
        var config = new ConcurrentIngestionConfig
        {
            Window = window.Value,
            StateStore = stateStore.Value as IngestionStateStore,
            LeaseSeconds = leaseSeconds.Value,
            WriteMarginSeconds = writeMarginSeconds.Value,
            PollIntervalSeconds = pollIntervalSeconds.Value,
            IndexLagSeconds = indexLagSeconds.Value,
            MaxApplyAttempts = maxApplyAttempts.Value,
            EnabledForGroup = enabledForGroup.Value,
            BeforeCommitWrite = beforeCommitWrite.Value,
            BeforeEnrichmentWrite = beforeEnrichmentWrite.Value,
        }.Validate();

        // PORT: the second test of `_check_values`, after the first one in Validate() (R7). A typed property cannot hold
        // a value of another type, so the test runs on the value of the data.
        Pydantic.Wrap(
            () =>
            {
                if (stateStore.Value is not null && stateStore.Value is not IngestionStateStore)
                {
                    throw new ArgumentException("state_store must be an IngestionStateStore");
                }
            },
            "ConcurrentIngestionConfig");
        return config;
    }

    /// <summary>Return the configured store. Create the in-memory store on the first call.</summary>
    internal IngestionStateStore GetStateStore()
    {
        if (StateStore is null)
        {
            StateStore = new InMemoryIngestionStateStore();
        }

        return StateStore;
    }
}
