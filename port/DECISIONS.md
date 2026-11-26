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
| D20 | 2026-11-25. A judge case that passes an argument of a type that the C# parameter cannot hold is a type rejection. The .NET host reports `JudgeTypeRejected`, and the runner accepts it only where the golden result is `TypeError` or `AttributeError`. The runner change goes in its own pull request. This answers GAPS F11. |
| D21 | 2026-11-25. Open data (`Any`, `dict[str, Any]`) is the Python value model: `object?` values from a closed set of types, handled only by `PyCompat`. Models stay records with an explicit `Validate()`. `JsonSerializer` does not bind models. This amends the `pydantic` row of the library table. |
| D22 | 2026-11-25. All ported code runs on one process-wide exclusive `TaskScheduler`, so it interleaves only at an `await`, as asyncio does. Ported code adds no lock that Python does not have. Port phase 9 can replace it. |
| D23 | 2026-11-25. Ported modules reach the logger, the clock, and the random source through one internal static holder, `PortServices`. Method signatures stay equal to Python. Port phase 9 can replace it. |
| D24 | 2026-11-25. The public facade is the interface `IGraphiti`, with an internal implementation that DI resolves. The ported class of `graphiti.py` is the internal `GraphitiCore`. Amended on 2026-11-26: `IGraphiti` has no dispose member, and the container closes the implementation. |
| D25 | 2026-11-25. Public data types are separate immutable records with `DateTimeOffset` and read-only collections. The facade maps them to and from the ported classes. |
| D26 | 2026-11-25. A custom entity or edge type is a `GraphTypeDefinition` that a host builds in code from a closed set of field kinds. This answers GAPS G6. |
| D27 | 2026-11-25. The M1 public surface is the 13 public members of the upstream class, the node and edge namespaces, prompt overrides and model routing, the token usage tracker, and the options of concurrent ingestion. The driver contract and the two ingestion write hooks stay internal. |
| D28 | 2026-11-25. Graphiti resolves the unkeyed `IChatClient` and `IEmbeddingGenerator` of the host from DI. The builder can replace each one. |
| D29 | 2026-11-25. One `IChatClient` serves both model sizes. The options `Llm.Model` and `Llm.SmallModel` set `ChatOptions.ModelId`. |
| D30 | 2026-11-25. The cross-encoder is the public contract `ICrossEncoder`. The default is the upstream OpenAI reranker logic on `IChatClient`, and it reads token log probabilities through the public hook `IChatLogProbabilities`. The documents give the OpenAI implementation of the hook. Packages stay at two. This amends lead choice 2. |
| D31 | 2026-11-25. A public delegate option classifies a provider exception as a rate limit or a server error. The default knows `HttpRequestException` only. The core references no provider package and not `System.ClientModel`. |
| D32 | 2026-11-25. `llm_client.openai_decisions_client` is ported as it is onto `HttpClient` and stays internal in M1. A public route target on the OpenAI SDK's `DecisionsClient` can come after M1. This replaces lead choice 3. |
| D33 | 2026-11-25. The adapters call host services (`IChatClient`, `IEmbeddingGenerator`, a host `ICrossEncoder`) on the thread pool and return to the exclusive scheduler. Short host delegates run in line on the scheduler. This answers what D22 left open for host code. |
| D34 | 2026-11-25. `MaxCoroutines` and `Embedding.Dimensions` are options. The other ten import-time settings stay environment variables in M1. The question to the maintainer said nine, and the source has ten. This amends the answer of GAPS G10. |
| D35 | 2026-11-25. Ported code uses the namespace `Graphiti.Core` and the folder `src/Graphiti.Net/Core/`. Public types use `Graphiti`. The exception classes of the two error files are public in `Graphiti`. |
| D36 | 2026-11-26. The public surface reads a naive stored date as UTC, and a save through a public call writes a zoned UTC value. This changes the stored shape of a record that Python wrote with a naive date and that .NET saves again. Ported code keeps a naive value naive. |
| D37 | 2026-11-26. The pilot ports for real the members of modules outside the pilot that RULES.md R24 names. `entity_node_ops` stays in the pilot, and its acceptance is build and format only, because it has no pure judge case. |
| D38 | 2026-11-26. A session can edit `port/judge/expected-failures.txt`, and only to remove the id of a case that passes. The removal goes in the pull request of the unit, and the pull request lists each id. The maintainer narrows the deny rule in `.claude/settings.json` for that one file. Each other change under `port/judge/` is a patch file that the lead writes and the maintainer applies. The runner change of D20 is the first. |
| D39 | 2026-11-26. The maintainer told the lead to continue without them through plan 0004, layer 6, and its end-to-end test. For that work, the lead has standing approval for each commit, push, pull request, and squash merge, after CI passes and the adversarial review is resolved. The lead starts each subagent that a plan row names with no question. The review of each pull request layer is an Opus 5.5 high subagent. The lead passes gate 3 after the pilot review and records the reasons in the gate log. An ask-first item that changes a public interface or a stored data shape blocks its unit, and the other units continue. |
| D40 | 2026-11-26, by the lead under D39. A case in `port/judge/expected-failures.txt` can fail with `JudgeTypeRejected`, and not only with `JudgeNotBound`. This extends D20 to the wrong-type cases with a golden value or another error (RULES.md, open item O4): each one stays in the list by name until the maintainer decides it at gate 4. The change is in `port/patches/judge-type-rejected.patch` and takes effect when the maintainer applies it. |
| D41 | 2026-11-26, by the lead under D39. The public exception classes of R4 take Python's constructor defaults: `GraphitiException(string message = "")` and `ChangeSetRejectedException(string reason = "change set rejected")`. Each class has a second constructor with no default and a last parameter `Exception? innerException`. Python accepts `GraphitiError()`, and a judge case calls it. A constructor with a required message has no form for that call, and a binding must not write a default. Both reviews of the `errors` unit accepted the form. |
| D42 | 2026-11-26, by the lead under D39. `PyCompat` gets `PyPatch<T>`, a holder for a module-level value that an upstream test replaces with `monkeypatch.setattr`. The value has an async-local replacement for the flow of one test. Code reads the value with `.Value`, and the type has no implicit conversion: the review of the pull request showed that a conversion lets a comparison with `null`, `Equals`, and `var` read the holder in silence. Without `PyPatch<T>`, 10 tests of `test_content_chunking.py` have no C# form. It is used only for a name that a ported test replaces (RULES.md R17). A test that replaces a function has no form yet (open item O10, for the maintainer). |
| D43 | 2026-11-26, by the lead under D39. `generate_covering_chunks` with a chunk size of 0 or 1 returns the pair chunks in insertion order. CPython returns them in the order of its hash table, which is fixed for a frozenset of ints: for 4 items it gives `[[2,3],[1,2],[0,3],[0,1],[0,2],[1,3]]`, and the port gives `[[0,1],[0,2],[0,3],[1,2],[1,3],[2,3]]`. The chunks are the same. Both reviewers of the unit called this important. The lead accepts the difference, because the order of a set is not a contract of the language, no upstream caller passes a chunk size below 2, and an exact copy needs a port of the CPython hash table. The maintainer accepts or rejects it by name at gate 4. |
| D44 | 2026-11-26, by the lead under D39. The product projects compile with `CheckForOverflowUnderflow`, so an overflow of a `long` throws `OverflowException` and never wraps (the review of `content_chunking` showed `_tokens_to_chars(2**61)` with a negative result). `Py.Comb` returns a `BigInteger`, because `comb(100, 20)` is a real input and does not fit in a `long`. This narrows debt T25. |
| D45 | 2026-11-26, by the lead under D39. The ported tracer takes the body of a span: `Tracer.StartSpanAsync(name, body)`. A span is not an `IDisposable`. The reason is upstream defect U19: with an OpenTelemetry tracer, an exception in a span leaves as `RuntimeError`, and a `Dispose` call cannot see the exception of the body. With the body form, the port reproduces the defect, as rule R21 asks. It also lets the maintainer choose the repaired behavior later with a change in one class and no change at the 14 span sites. |
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
| `pydantic` | records with explicit `Validate()` methods, and the Python value model in `PyCompat` for open data | in box | Amended by D21. `System.Text.Json` reads and writes JSON tokens inside `PyCompat`, and does not bind models. [RULES.md](RULES.md) R7 covers each validator. |
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
| The `Graphiti(...)` constructor | `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options` | 11.0.0 | Added on 2026-11-25 for `AddGraphiti` and `GraphitiOptions` (lead choice 4, D24). |
| `pytest`, `pytest-asyncio` | `xunit.v3.mtp-v2` | 4.0.3 | On Microsoft.Testing.Platform. |
| `hypothesis` (not used upstream) | `CsCheck` | 5.0.1 | For the property layer. |
| mutation testing (none upstream) | `dotnet-stryker` | 5.1.0 | Local tool. Uses the `mtp` test runner. |
| `sentence-transformers`, `gliner2`, `falkordb`, `boto3`, `opensearch-py`, `langchain-aws`, `relate-db` | none in the MVP | | D10. |

## Lead choices accepted at gate 1

The lead made these choices alone. The maintainer accepted all seven at gate 1.

1. `Microsoft.Extensions.*` packages use the 11.x line on a .NET 10 target. The alternative is to pin the 10.x line.
2. The cross-encoder in the MVP is the upstream OpenAI reranker logic, moved onto `IChatClient`. Upstream reads token log probabilities. If `IChatClient` cannot expose them for a provider, the reranker fails with a clear error for that provider. Amended on 2026-11-25 by D30: `IChatClient` exposes them for no provider, so a public hook reads them.
3. The upstream OpenAI decisions client (node and edge dedupe) is ported onto `IChatClient`. Replaced on 2026-11-25 by D32: the client posts to its own `/decisions` endpoint, so it is ported onto `HttpClient`.
4. The public surface uses `IServiceCollection.AddGraphiti(...)`, options classes, and `Async` method names with a `CancellationToken`. Python names stay on internal members.
5. The library does not load `.env` files.
6. The upstream source is fetched by `scripts/fetch-upstream.sh` into the git-ignored `.upstream/` folder. It is not a git submodule and it is not copied into this repository.
7. The judge recorder runs Python with `uv` from the upstream lock file. Only the recorder needs Python.

## Gate log

| Gate | Date | Result |
| --- | --- | --- |
| 1. Decisions | 2026-11-24 | Approved by the maintainer, with all seven lead choices |
| Phase 4 decisions | 2026-11-25 | The maintainer chose D20, D21, D22, and D23 while the rulebook was written. [The rulebook design](../docs/design-docs/rulebook-design.md) gives the options. |
| Public surface | 2026-11-25 | The maintainer chose D24 to D35 and approved [The public surface design](../docs/design-docs/public-surface-design.md) with its 12 lead choices. On 2026-11-26, after one adversarial review, the maintainer chose D36, amended D24, and accepted lead choice L13. |
| 2. Judge | 2026-11-25 | Approved by the maintainer, with D17, D18, and D19. The maintainer asked for the real-model scenarios first, read the final results, and closed the gate. `port/judge/` is read-only from commit `c052323`. |
| Keep-going instruction | 2026-11-26 | The maintainer chose D37, D38, and D39. Each gate decision that the lead makes under D39 gets a row in this log. |
| Runner change (P8) | 2026-11-26 | The lead chose D40 under D39 and wrote the patch. The maintainer applied it, and it merged as pull request 21. The maintainer also narrowed the deny list for D38 (pull request 22). |
| Pilot, unit `errors` | 2026-11-26 | The lead chose D41 under D39. It sets constructor defaults on public exception classes. The maintainer reads it at the end of the run. |
| Pilot, three more units | 2026-11-26 | The lead chose D42, D43, and D44 under D39, after two reviews of each unit. D43 accepts one difference in behavior. The maintainer reads the three at the end of the run. |
| 3. Rulebook | 2026-11-26 | Passed by the lead under D39. Five units of different kinds were ported from the rulebook alone, and each one had two reviews with no contact. Each finding that showed a rule gap changed the rulebook, and the change table of [RULES.md](RULES.md) has one row for each round. The merged output follows the changed rules (plan 0003, task 8). The pilot did not test open item O7, because no pilot unit has a library callback: the lead checks it with the unit of `driver/neo4j_driver.py` in layer 5. D43 and the open items O4, O6, O9, and O10 wait for the maintainer. The maintainer reads the rulebook at the end of the run. |
| Skeleton contract (plan 0004, layer 0) | 2026-11-26 | The lead chose D45 under D39, after the adversarial review of the first skeleton packet. The same review changed three shapes with no decision row, and RULES.md holds them: the work function of `ExecuteWriteAsync` receives a `Transaction`, `semaphore_gather` has a four-lambda form, and a base class method with a `...` body has no C# member. |
