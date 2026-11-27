# Architecture

This document separates what exists in the repository from what the port will add.
Update it in the same change that makes it stale.

## Implemented today

| Path | Holds | State |
| --- | --- | --- |
| `src/Graphiti.Net` | The core package. Root namespace `Graphiti`. | Holds each ported unit of the core. [port/queue.json](port/queue.json) gives the status of each module. |
| `src/Graphiti.Net.Neo4j` | The Neo4j driver package. References `Neo4j.Driver` and the core. | `Neo4jDriver` and its 11 operations classes are ported (plan 0004, layer 5). |
| `src/Graphiti.Net.PyCompat` | The helpers that give Python behavior: numbers, text, slicing, string order, `itertools`, UUIDs. Namespace `Graphiti.PyCompat`. Members are `internal`. The Unicode tables in `PyUnicodeData.g.cs` come from Python. | Built for the scalar and text helpers that the queue needs. The value model, JSON, dates, `re.split`, pydantic, and asyncio helpers are not built. See debt T23. |
| `tests/Graphiti.Net.Property.Tests` | CsCheck property tests that compare each PyCompat helper with values that real Python recorded in `python-values.json`. | 55 tests pass. |
| `tests/Graphiti.Net.E2E.Tests` | End-to-end tests on xUnit v3 and Microsoft.Testing.Platform. | The environment test, the tests of the Neo4j driver, and the test that saves and loads each node and edge type on Neo4j and compares the result with Python. |
| `docker-compose.yml` | Neo4j 5.26.2 sidecar on `localhost:7687`. | Works on a local machine. |
| `tests/Graphiti.Net.JudgeHost` | The .NET judge host: protocol loop, codec, judge embedder, and the binding table. | Works. `Units/` holds one binding file for each ported module that has a judge case. |
| `port/` | Port state, decisions, the module queue, the upstream pin, the gap list, the rulebook, and the judge. | Phase 4. The judge is read-only. The rulebook waits for gate 3. |
| `scripts/` | Setup, the gate, the mutation run, the upstream fetch, the docs check. | Works on a local machine. |
| `.devcontainer/`, `.claude/` | Devcontainer definition, Claude Code configuration, the `verify` skill. | Written, not started. See [docs/RELIABILITY.md](docs/RELIABILITY.md). |

Build configuration lives in `Directory.Build.props`, `Directory.Packages.props`, `global.json`, and `.editorconfig`.
Warnings are errors. Package versions are central.

## Planned system flow

This section describes upstream Graphiti at the pinned commit. The port reproduces it. None of it exists in C# yet.

```text
episode (text, message, JSON)
  -> ingestion phases: extract nodes -> resolve duplicates -> extract edges
     -> resolve and invalidate edges -> summarize -> write a change set
  -> graph driver -> Neo4j

query
  -> search: cosine similarity + BM25 full-text + breadth-first traversal
  -> rerank: RRF, MMR, node distance, episode mentions, or cross-encoder
  -> nodes, edges, episodes, communities
```

## Planned module map

Ported code goes to `src/Graphiti.Net/Core`, namespace `Graphiti.Core`, with one folder for each Python subpackage. [port/queue.json](port/queue.json) is the full list.
The public surface goes to the root of `src/Graphiti.Net`, namespace `Graphiti`: the interface `IGraphiti`, the public records, the options, and `AddGraphiti`.
The facade and the three model adapters go to `src/Graphiti.Net/Internal`. They map public records to ported types and call host services on the thread pool.
[The public surface design](docs/design-docs/public-surface-design.md) is the contract for all three parts.

| Upstream `graphiti_core` path | Responsibility | Target |
| --- | --- | --- |
| `graphiti.py` | The facade: `add_episode`, `search`, `build_communities`, and the rest of the public calls | `Graphiti.Net`, as the internal class `GraphitiCore` behind the public `IGraphiti` |
| `nodes.py`, `edges.py`, `models/`, `namespaces/` | Graph data types and their persistence queries | `Graphiti.Net` |
| `ingestion/` | Phases, change sets, the scheduler for concurrent ingestion, the SQLite state store | `Graphiti.Net` |
| `utils/maintenance/` | Node, edge, community, and attribute operations, dedupe helpers | `Graphiti.Net` |
| `search/` | Search configuration, recipes, filters, and ranking | `Graphiti.Net` |
| `prompts/` | The prompt library and configurable prompts | `Graphiti.Net` |
| `llm_client/`, `embedder/`, `cross_encoder/` | Contracts, retry, token tracking, caching. Provider clients become one adapter for chat, one for embeddings, and one reranker on `IChatClient`. | `Graphiti.Net` |
| `driver/driver.py`, `driver/operations/`, `driver/graph_operations/`, `driver/search_interface/` | The driver contract | `Graphiti.Net` |
| `driver/neo4j_driver.py`, `driver/neo4j/` | The Neo4j implementation | `Graphiti.Net.Neo4j` |
| `tracer.py` | OpenTelemetry spans | `Graphiti.Net` |

## Boundaries

| Boundary | Rule |
| --- | --- |
| Core to database | The core never references `Neo4j.Driver`. It talks to the driver contract. |
| Core to models | The core talks to `IChatClient` and `IEmbeddingGenerator` from `Microsoft.Extensions.AI`. It references no provider package. Provider detail enters through two public hooks: `IChatLogProbabilities` and the failure classifier. |
| Core to host code | Host services run on the thread pool, never on the exclusive scheduler of ported code. |
| Public to ported | A public type is never a ported type, except the exception classes. The facade maps between the two. |
| Product to Python | Product code never calls Python. Only the judge recorder runs Python. |
| Product to telemetry services | None. Tracing goes to the collector that the host application configures. |
| Tests to product | Tests enter through the public surface. Fakes exist only at the LLM network boundary. |

## External services

| Service | Used by | Local form |
| --- | --- | --- |
| Neo4j 5.26 | Driver package, end-to-end tests | `docker compose up -d --wait neo4j` |
| An LLM and an embedding provider | Ingestion and search | Scripted replies at the HTTP boundary in tests. A real provider only for the M1 quickstart run. |

## Exceptions

None recorded.
