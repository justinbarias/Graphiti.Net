# 0006 PostgreSQL driver spike

The feasibility spike of milestone M6. It starts after M1, which plan 0005 ends.
[The Azure driver exploration](../../design-docs/azure-driver-exploration.md) holds the comparison, the driver sketch, and the decisions of the maintainer.

## Goal

Show with a running slice that Graphiti.Net can use PostgreSQL with pgvector as its graph database.
The spike ends with a report. The maintainer decides the build from the report.

## Scope

- The slice covers four kinds: entity node, episodic node, entity edge, and episodic edge.
- The slice covers save, bulk save in one transaction, read by uuid, read by group, and the three searches for nodes and edges.
- Spike code lives in `spikes/postgresql/`. It is not a product project and it is not packed.
- The spike changes no file in `src/`, `port/judge/`, or `.upstream/`.

Out of scope: communities, sagas, the public `AddPostgreSql` call, the hook methods in the core, and a NuGet package.

## Ask-first items

Each of these stops for the maintainer before work continues.

| Item | Step |
| --- | --- |
| The table schema, which is a stored data shape | 2 |
| A pull of a database image, because the Docker disk was full on 2026-11-28 | 2 |
| The `postgres` service in `docker-compose.yml`, which the gate uses | 2 |
| The list of new hook methods and the new `GraphProvider` member, which change ported code | 6 |
| An Azure server, an Azure identity, and any spend | 7 |

## Tasks

| # | Task | Owner, model, effort | Acceptance evidence |
| --- | --- | --- | --- |
| 1 | Write a script that lists each call of `ExecuteQueryAsync`, `Transaction.RunAsync`, and `GraphDriverSession.RunAsync` in the core, with its method and its hook status. Compare the result with the exploration. | Workhorse, Sonnet 5.5, medium | The script and its output. The exploration counted 97 call sites, 77 methods with a hook, and 7 methods with no hook. Each difference has an explanation. |
| 2 | Write the schema for the four kinds, with the indexes and the generated `tsvector` columns. Add the container. Bring the schema to the maintainer. | Lead, Opus 5.5, medium | The schema file. `CREATE EXTENSION vector` and each `CREATE` statement run on the container. The maintainer approved the schema. |
| 3 | Write save, bulk save, the transaction forms, read by uuid, and read by group. Measure the largest episode text that a `tsvector` accepts. | Workhorse, Sonnet 5.5, medium | A test for each operation passes on the container. A failed statement inside a transaction leaves no row. The report gives the text limit in characters. |
| 4 | Write the full-text search, the vector search, and the breadth-first search for nodes and for edges, with the `SearchFilters` builder. Run one search with the default concurrency limit of the core. | Workhorse, Opus 5.5, medium | A test for each search passes. The vector score of a known pair equals the Neo4j value to 6 decimal places. The breadth-first search to depth 3 returns the same node set as Neo4j. |
| 5 | Run the same fixtures on Neo4j and on PostgreSQL. Report the differences. | Workhorse, Sonnet 5.5, medium | A table with one row for each fixture query: set equality for reads, the overlap of the top 10 results for full-text search with `simple` and with `english`, and the time of each query. |
| 6 | Write the list of hook methods that the core needs, the new `GraphProvider` member, and the `InternalsVisibleTo` entry, as a proposal. Write no code in `src/`. | Lead, Fable 5.2, medium | The proposal names each method with its signature and its upstream site. The maintainer accepted or changed it. |
| 7 | With approval, run the suite of steps 3 and 4 on an Azure flexible server with a managed identity or a user token. | Workhorse, Sonnet 5.5, medium | The run output. The report states the server tier, the PostgreSQL version, the `azure.extensions` value, and the cost of the run. |
| 8 | Write the spike report with a build recommendation. Run one adversarial review of the report and the spike code. | Lead, Fable 5.2, medium. Reviewer of the maintainer's choice. | The report answers each row of the risk table of the exploration. No open `important` finding. The maintainer recorded the build decision. |

## Expected values

- Vector score: `(2 - cosine distance) / 2`. For two equal vectors the score is 1. For two opposite vectors the score is 0.
- The default limit of the vector score is 0.6, and the default depth of the breadth-first search is 3.
- The default embedding has 1,024 dimensions.
- Step 5 sets no pass limit for the full-text overlap. It reports the number, and the maintainer decides (decision 8 of the exploration).

## Gate

The maintainer reads the report and decides one of three things: build the driver as M6, change the target, or stop.

## Results

None yet.
