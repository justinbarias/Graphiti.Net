# Port decisions

Gate 1 of the port. The maintainer approved this file on 2026-11-24. A later change needs the maintainer's agreement.
Decisions D1 to D15 come from the bootstrap interview on 2026-11-24.
The section "Lead choices accepted at gate 1" lists the choices that the lead made alone.

## Source and target

| Item | Value |
| --- | --- |
| Source | `getzep/graphiti`, version 0.32.0, commit `a61333edfa14cc483365ed7dd1e287ae6096db6c` |
| Source package | `graphiti_core`: 182 modules, 47,677 lines |
| Target framework | .NET 10, C# 14 |
| Packages | `Graphiti.Net` (core, root namespace `Graphiti`), `Graphiti.Net.Neo4j` (driver) |
| License | Apache-2.0, with a NOTICE file that credits Zep |

## Decisions from the interview

| Id | Decision |
| --- | --- |
| D1 | MVP scope is the full `graphiti_core` package for one driver. |
| D2 | The MVP driver is Neo4j. Azure Cosmos DB Gremlin is a later milestone that starts with a spike. |
| D3 | One adapter over `Microsoft.Extensions.AI` replaces every provider LLM client and embedder client. |
| D4 | Internal modules are a strict structural port. The public surface is designed for .NET. |
| D5 | The test suite has four layers: differential judge, end-to-end, property, mutation. See [docs/TESTING.md](../docs/TESTING.md). |
| D6 | Upstream pytest tests are ported to xUnit under `tests/Port.Evaluation` only to judge the port. They are deleted after parity (M3). |
| D7 | Mutation testing starts in observe mode. An enforced threshold comes after the M1 baseline. |
| D8 | The roadmap follows the last published upstream roadmap plus an upstream parity milestone. |
| D9 | PostHog telemetry is not ported. OpenTelemetry tracing is ported. |
| D10 | Deferred to M5: FalkorDB, Neptune, and Relate drivers, BGE and Gemini rerankers, GLiNER2 and Jev clients. |
| D11 | The judge compares against golden files that a recorder script produces from Python at the pinned commit. The golden files are committed. |
| D12 | Review follows the porting skill: two fresh Opus reviewers for each unit. If they disagree, a third reviewer decides. |
| D13 | End-to-end tests use one Neo4j sidecar from `docker-compose.yml`. Each test isolates its data with a unique group id. |
| D14 | One setup script serves the devcontainer, Claude cloud sessions, and CI. |
| D16 | The judge follows [the judge design](../docs/design-docs/judge-design.md): capture at the provider contract, exact schema and Cypher text, replies recorded from a real model, clock values normalized, concurrent requests compared as a multiset. Approved 2026-11-24. |
| D17 | Gate 2, 2026-11-25. The LLM contract of the port is upstream's base client path: the adapter implements `_generate_response`, and the shared code appends the response schema text to the prompt. The port does not follow the `responses.parse` path of upstream's default `OpenAIClient`. Native structured output can come later as a .NET extension, covered by end-to-end tests only. |
| D18 | Gate 2, 2026-11-25. The order of node labels is noise. The judge compares label lists without their order in flow results, in prompt text, in Cypher text, and in reply keys. The port writes `Entity` first. |
| D19 | Gate 2, 2026-11-25. The maintainer accepted seven changes to the judge design. Three are from the build: UUID names from content, the .NET host outside `port/judge/`, and a fresh host process for each flow case. Four are from the review: bags in place of sorted names, clock rank and shape, the number rule, and the `bound_wrong` rule. |
| D15 | M1 includes publishing `0.1.0-preview` to nuget.org. The publish step itself still needs the maintainer's approval at that time. |

## Scope by module

[queue.json](queue.json) holds every module. The `notes` field marks the modules that the MVP does not port.

| Group | Modules | Python lines | Queue note prefix |
| --- | --- | --- | --- |
| Ported in the MVP | 114 | 32,596 | (empty) |
| Replaced by the `Microsoft.Extensions.AI` adapter | 11 | 2,297 | `replaced:` |
| Deferred to M5 | 55 | 12,656 | `out-of-mvp:` |
| Not ported (telemetry) | 2 | 128 | `not-ported:` |

27 modules are in import cycles. 21 of them are in MVP scope. Port each cycle group together.

## Library choices

Versions are the latest stable releases on nuget.org on 2026-11-24.
When code first uses a package, the package enters `Directory.Packages.props`.

| Python | .NET | Version | Notes |
| --- | --- | --- | --- |
| `pydantic` | records with `System.Text.Json` and explicit `Validate()` methods | in box | The serializer does not validate. The rulebook must cover each validator. |
| `neo4j` | `Neo4j.Driver` | 6.5.1 | Official driver. |
| `openai`, `anthropic`, `groq`, `google-genai`, `voyageai` | `Microsoft.Extensions.AI.Abstractions` | 11.0.1 | D3. The user supplies the provider package. |
| `httpx` | `HttpClient` from `IHttpClientFactory` | in box | Only where the core calls HTTP directly. |
| `tenacity` | `Microsoft.Extensions.Resilience` (Polly) | 11.0.1 | Retry counts and wait times must match upstream. |
| `numpy` | `System.Numerics.Tensors` | 11.0.0 | Upstream uses it for vector normalization, dot products, and MMR. |
| `sqlite3` | `Microsoft.Data.Sqlite` | 11.0.0 | For the ingestion state store. |
| `opentelemetry` | `System.Diagnostics.ActivitySource`, `OpenTelemetry.Api` | 1.21.0 | |
| `python-dotenv` | none | | The library does not read `.env` files. The host application owns configuration. |
| `posthog` | none | | D9. |
| `logging` | `Microsoft.Extensions.Logging.Abstractions` | 11.0.0 | Source-generated log methods. |
| `pytest`, `pytest-asyncio` | `xunit.v3.mtp-v2` | 4.0.3 | On Microsoft.Testing.Platform. |
| `hypothesis` (not used upstream) | `CsCheck` | 5.0.1 | For the property layer. |
| mutation testing (none upstream) | `dotnet-stryker` | 5.1.0 | Local tool. Uses the `mtp` test runner. |
| `sentence-transformers`, `gliner2`, `falkordb`, `boto3`, `opensearch-py`, `langchain-aws`, `relate-db` | none in the MVP | | D10. |

## Lead choices accepted at gate 1

The lead made these choices alone. The maintainer accepted all seven at gate 1.

1. `Microsoft.Extensions.*` packages use the 11.x line on a .NET 10 target. The alternative is to pin the 10.x line.
2. The cross-encoder in the MVP is the upstream OpenAI reranker logic, moved onto `IChatClient`. Upstream reads token log probabilities. If `IChatClient` cannot expose them for a provider, the reranker fails with a clear error for that provider.
3. The upstream OpenAI decisions client (node and edge dedupe) is ported onto `IChatClient`.
4. The public surface uses `IServiceCollection.AddGraphiti(...)`, options classes, and `Async` method names with a `CancellationToken`. Python names stay on internal members.
5. The library does not load `.env` files.
6. The upstream source is fetched by `scripts/fetch-upstream.sh` into the git-ignored `.upstream/` folder. It is not a git submodule and it is not copied into this repository.
7. The judge recorder runs Python with `uv` from the upstream lock file. Only the recorder needs Python.

## Gate log

| Gate | Date | Result |
| --- | --- | --- |
| 1. Decisions | 2026-11-24 | Approved by the maintainer, with all seven lead choices |
| 2. Judge | 2026-11-25 | The maintainer approved D17, D18, and D19, and asked for the real-model scenarios before the gate closes. The scenarios are recorded. The gate closes after the maintainer reads the final results in `port/STATE.md`. |
