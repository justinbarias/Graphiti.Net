# Graphiti.Net product specification

Status: approved in the bootstrap interview on 2026-11-24. Change it only with the maintainer's agreement.

## Purpose

Graphiti.Net is a .NET port of [Graphiti](https://github.com/getzep/graphiti), the temporal context graph library by Zep.
A context graph stores entities, the facts that link them, and the time window in which each fact was true.
Every fact traces back to an episode, which is the raw text, message, or JSON that produced it.

Graphiti.Net gives .NET agents the same memory layer that Python agents get from `graphiti-core`.
The port is pinned to upstream version 0.32.0, commit `a61333e` ([port/UPSTREAM_PIN](../../port/UPSTREAM_PIN)).

## Users

- A .NET developer who builds an agent and needs memory that changes over time.
- A team that already runs Graphiti in Python and wants the same graph from a .NET service.

## What the library does

| Capability | Upstream entry point | Description |
| --- | --- | --- |
| Ingest | `add_episode`, `add_episode_bulk` | Extract entities and facts from an episode with an LLM, resolve duplicates, and write them to the graph. |
| Temporal facts | edge invalidation | When a new fact contradicts an old one, close the old fact's validity window. Never delete history. |
| Hybrid search | `search`, `search_` | Combine cosine similarity, BM25 full-text, and graph traversal. Rerank with RRF, MMR, node distance, episode mentions, or a cross-encoder. |
| Custom types | entity and edge type models | The developer defines entity and edge types. Extraction fills their attributes. |
| Communities | `build_communities` | Cluster entities and summarize each cluster. |
| Manual facts | `add_triplet` | Add one fact directly, without an LLM extraction. |
| Episode lifecycle | `retrieve_episodes`, `remove_episode`, `get_nodes_and_edges_by_episode` | Read, trace, and remove episodes. |
| Sagas | `summarize_saga` | Group episodes into a saga and keep a running summary. |
| Configurable prompts | prompt library | Replace or extend the extraction and dedupe prompts. |
| Concurrent ingestion | ingestion scheduler and state store | Ingest many episodes into one graph in parallel without lost updates. |
| Tracing | OpenTelemetry | Emit spans to the user's own collector. |

## Roadmap

The upstream README carried a "Status and Roadmap" section until March 2026.
Its last published form is the spine of this roadmap.
At the pinned commit, upstream already ships the first three items.

| Upstream roadmap item | Upstream status | Graphiti.Net milestone |
| --- | --- | --- |
| Custom graph schemas: developer-defined node and edge classes | Done | M1 |
| Stronger retrieval with configurable options | Done | M1 |
| Graphiti MCP server | Done | M2 |
| Expanded test coverage for reliability and edge cases | Open | M3 |

The first upstream roadmap (August 2024) listed performance and scalability, search improvements, and node CRUD.
All three are part of `graphiti_core` at the pinned commit, so M1 covers them.

### M0. Bootstrap

Repository, agent guidance, development environment, test tooling, port decisions, and execution plans.
Evidence: `scripts/check.sh --test` passes on a clean clone.

### M1. MVP: full `graphiti_core` on Neo4j

This is the MVP goal.

Scope:

- Every `graphiti_core` module that the port queue does not mark as deferred, replaced, or not ported. That is 114 modules and 32,596 Python lines ([port/queue.json](../../port/queue.json)).
- One graph database: Neo4j 5.26, in its own package `Graphiti.Net.Neo4j`.
- One LLM and embedder boundary: an adapter over `Microsoft.Extensions.AI` (`IChatClient` and `IEmbeddingGenerator`). Any provider with an `IChatClient` works.
- An LLM-based cross-encoder reranker over `IChatClient`.
- A .NET-native public surface: dependency injection registration, options, `CancellationToken` on every async method. Internal modules follow the Python structure one to one.

Out of scope for M1:

- FalkorDB, Neptune, and Relate drivers.
- BGE and Gemini rerankers, GLiNER2 and Jev clients.
- The MCP server and the REST service.
- PostHog telemetry. Graphiti.Net never sends usage data to a telemetry service.

If all of these are true, M1 is done:

1. Every differential judge case passes, and `port/judge/expected-failures.txt` is empty.
2. The end-to-end suite passes against the Neo4j sidecar.
3. The port scanner reports no finding and no `TODO(port)` marker, or the maintainer accepted each marker by name.
4. The upstream quickstart example, written in C#, runs against Neo4j with a real LLM.
5. `Graphiti.Net` and `Graphiti.Net.Neo4j` version `0.1.0-preview` are published on nuget.org.

### M2. MCP server

A port of upstream `mcp_server` on the official `ModelContextProtocol` C# SDK.
It exposes episode ingestion, search, and graph maintenance as MCP tools over stdio and HTTP.

### M3. Reliability and test coverage

- Mutation testing moves from observe mode to an enforced threshold. The M1 baseline sets the number.
- A scheduled job runs Python and .NET side by side with random inputs. Each new difference becomes a judge case.
- The temporary pytest ports under `tests/Port.Evaluation` are deleted.

### M4. REST service

A port of upstream `server/graph_service` to ASP.NET Core minimal APIs.

### M5. Upstream parity

- The deferred drivers: FalkorDB, Neptune, Relate.
- The deferred model clients: BGE reranker, Gemini reranker, GLiNER2, Jev.
- A repeatable process to move the pin forward to newer upstream commits. The upstream `spec/` folder describes the designs that newer work builds on.

### M6. Azure Cosmos DB for Apache Gremlin driver

This driver has no upstream equivalent, so no differential oracle exists for it.
The milestone starts with a feasibility spike.
The spike must answer how the driver provides vector search and full-text search, because Graphiti needs both.
The maintainer decides whether to build the driver after the spike report.

## Non-goals

- A redesign of Graphiti's algorithms or prompts. Behavior changes come from upstream.
- Idiomatic rewrites of internal modules before M1 is done.
- Support for .NET versions older than .NET 10.

## Acceptance evidence

A claim that a milestone is done needs command output, not a description.
[docs/TESTING.md](../TESTING.md) defines the test layers.
[docs/QUALITY_SCORE.md](../QUALITY_SCORE.md) records what was run and when.
