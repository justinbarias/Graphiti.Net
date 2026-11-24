# Architecture

This document separates what exists in the repository from what the port will add.
Update it in the same change that makes it stale.

## Implemented today

| Path | Holds | State |
| --- | --- | --- |
| `src/Graphiti.Net` | The core package. Root namespace `Graphiti`. | Empty project. No ported code. |
| `src/Graphiti.Net.Neo4j` | The Neo4j driver package. References `Neo4j.Driver` and the core. | Empty project. No ported code. |
| `tests/Graphiti.Net.E2E.Tests` | End-to-end tests on xUnit v3 and Microsoft.Testing.Platform. | One environment test that proves the sidecar supports full-text and vector search. |
| `docker-compose.yml` | Neo4j 5.26.2 sidecar on `localhost:7687`. | Works on a local machine. |
| `port/` | Port state, decisions, the module queue, the upstream pin. | Phase 1. |
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

One folder in `src/Graphiti.Net` for each Python subpackage. [port/queue.json](port/queue.json) is the full list.

| Upstream `graphiti_core` path | Responsibility | Target |
| --- | --- | --- |
| `graphiti.py` | The facade: `add_episode`, `search`, `build_communities`, and the rest of the public calls | `Graphiti.Net`, plus the .NET public surface |
| `nodes.py`, `edges.py`, `models/`, `namespaces/` | Graph data types and their persistence queries | `Graphiti.Net` |
| `ingestion/` | Phases, change sets, the scheduler for concurrent ingestion, the SQLite state store | `Graphiti.Net` |
| `utils/maintenance/` | Node, edge, community, and attribute operations, dedupe helpers | `Graphiti.Net` |
| `search/` | Search configuration, recipes, filters, and ranking | `Graphiti.Net` |
| `prompts/` | The prompt library and configurable prompts | `Graphiti.Net` |
| `llm_client/`, `embedder/`, `cross_encoder/` | Contracts, retry, token tracking, caching. Provider clients become one adapter. | `Graphiti.Net` |
| `driver/driver.py`, `driver/operations/`, `driver/graph_operations/`, `driver/search_interface/` | The driver contract | `Graphiti.Net` |
| `driver/neo4j_driver.py`, `driver/neo4j/` | The Neo4j implementation | `Graphiti.Net.Neo4j` |
| `tracer.py` | OpenTelemetry spans | `Graphiti.Net` |

## Boundaries

| Boundary | Rule |
| --- | --- |
| Core to database | The core never references `Neo4j.Driver`. It talks to the driver contract. |
| Core to models | The core talks to `IChatClient` and `IEmbeddingGenerator` from `Microsoft.Extensions.AI`. It references no provider package. |
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
