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

// before_commit_write(change_set) -> change_set
// before_enrichment_write(nodes, unit) -> nodes
// PORT: `BeforeCommitWriteHook` and `BeforeEnrichmentWriteHook` are type aliases, so they have no C# type (R2). Each
// use is the aliased delegate type. An async callable takes a CancellationToken last (R8).

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
    // TODO(port): ConcurrentIngestionConfig._check_values
    internal ConcurrentIngestionConfig Validate()
    {
        throw new NotSupportedException("TODO(port): ConcurrentIngestionConfig._check_values");
    }

    // TODO(port): ConcurrentIngestionConfig.get_state_store
    internal IngestionStateStore GetStateStore()
    {
        throw new NotSupportedException("TODO(port): ConcurrentIngestionConfig.get_state_store");
    }
}
