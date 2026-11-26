# 0004 Translate the core

Port phase 6. Starts after gate 3. 114 units, 32,596 Python lines.

## Shape

A stack of pull requests, built with `gh stack`. Each layer depends on the layers under it and passes the gate alone.
`port/queue.json` gives the exact order inside each layer. Port every module of a cycle group together.

| Layer | Branch | Upstream packages | Units | Lines | Implementer |
| --- | --- | --- | --- | --- | --- |
| 0 | `port/skeleton` | Types and signatures for all 114 units, with no bodies | 114 | | Opus 5.5, medium |
| 1 | `port/foundations` | `errors`, `helpers`, `graphiti_types`, `tracer`, `decorators`, `graph_queries`, `utils` (6 modules), `models`, `namespaces` | 20 | 4,277 | Sonnet 5.5, medium |
| 2 | `port/graph-types` | `nodes`, `edges`, and the driver contract: `driver.driver`, `driver.operations`, `driver.graph_operations`, `driver.search_interface`, `driver.query_executor`, `driver.record_parsers`, the `driver` package root | 21 | 5,147 | Opus 5.5, medium. One cycle group holds most of this layer. |
| 3 | `port/models-boundary` | `llm_client` (11 modules), `embedder`, `cross_encoder`, plus the new `Microsoft.Extensions.AI` adapter | 16 | 2,228 | Sonnet 5.5, high |
| 4 | `port/prompts` | `prompts` | 14 | 3,052 | Sonnet 5.5, medium |
| 5 | `port/neo4j` | `driver.neo4j`, `driver.neo4j_driver` | 14 | 3,032 | Sonnet 5.5, high. Opus 5.5 medium for `search_ops`. |
| 6 | `port/search` | `search` | 7 | 3,456 | Opus 5.5, medium |
| 7 | `port/maintenance` | `utils.maintenance` | 10 | 4,458 | Opus 5.5, medium |
| 8 | `port/ingestion` | `ingestion` | 9 | 4,159 | Opus 5.5, medium |
| 9 | `port/facade` | `graphiti` (2,782 lines), the package root, `migrations`, and the public .NET surface | 3 | 2,787 | Opus 5.5, high |

Layer 0 is the skeleton that the porting skill requires, so every later layer compiles against fixed shapes.
A skeleton member throws `NotSupportedException` with a `TODO(port)` marker, and the scanner counts the markers down to zero.
It never throws `NotImplementedException`, because ported code catches that type to fall back from a driver operation (`port/RULES.md`, R24).

## Loop for each unit

1. Set the status to `in_progress` and fill in `target`.
2. Port the module, and the upstream pytest file that covers it into `tests/Port.Evaluation`, by `port/RULES.md`.
3. `dotnet build -warnaserror`. Then set `built`.
4. `dotnet format --verify-no-changes`, then the unit's evaluation tests. Then set `tests_pass`.
5. The judge cases that cover the unit. Remove their lines from `expected-failures.txt`. Then set `parity_pass`.
6. Review. Then set `reviewed`.

Run the checkable steps at low effort first, and rerun only the failures at high effort.
If a unit still fails after three fix rounds, set it to `blocked` and bring it to the maintainer.

## Brief for an implementer

Give each implementer one unit and its Python file. Add `port/RULES.md`, `port/GAPS.md`, and the list of files that the implementer owns. Add the standing rules in AGENTS.md and the commands above.
An implementer cannot change `port/RULES.md`, `port/judge/`, or files outside its list.

## Review

- For each unit: two fresh Opus 5.5 high reviewers with [REVIEW.md](../../../REVIEW.md), the Python file, the C# file, `RULES.md`, and `GAPS.md`. A third reviewer settles a disagreement. This is decision D12.
- For each layer: one adversarial pass over the layer diff before its pull request.
- For the stack: one integrated pass over `git diff main...port/facade` before the merge.

Each subagent start needs the maintainer's approval first.

## End-to-end tests added in this plan

| After layer | Flow |
| --- | --- |
| 5 | Save and load each node and edge type on Neo4j. Build indexes twice and get the same result. |
| 6 | Seed a graph, then run each search recipe and compare the ranked ids with the golden result. |
| 8 | Ingest episodes in parallel into one group, and find no lost update. |
| 9 | `AddEpisodeAsync`, then `SearchAsync`. A contradicting episode closes the old fact's validity window and deletes nothing. |

After layers 5, 7, and 9, run `scripts/mutate.sh` and apply the pruning rule.

## Also in this plan

A script that writes [db-schema.md](../../generated/db-schema.md) from the ported query definitions (after layer 5).

## Lead decisions at the start, 2026-11-26

The lead made these decisions under decision D39. The maintainer reads them at the end of the run.

- Each layer is one branch and one pull request, as the table says. Inside a layer, an implementer ports a packet of one to four modules in its own worktree, and packets with no shared file run at the same time. Two reviewers read each packet, so each unit has two reviewers. The lead updates the queue, the state file, and `expected-failures.txt` one time for each layer.
- Layer 0 has seven packets. Five cover layers 1 to 6, and two cover layers 7 to 9. It merges as two pull requests, so that layer 1 starts after the first one.
- The skeleton ports in full each member that has no logic to defer: an enum, an abstract class, a constant, and a body that is only `return None` or `raise NotImplementedError` (rule R24).
- `utils.bulk_utils` moves from layer 1 to layer 7. It imports five modules of `utils.maintenance` and `ingestion.change_set`, and no unit of layers 1 to 6 imports it.
- Two more units leave layer 1 (2026-11-27). `decorators` moves to layer 6, because it imports `search.search_config` and four of its tests need `SearchResults.merge`. `utils.ontology_utils.entity_types_utils` moves to layer 2, because it reads the fields of `EntityNode`.
- A unit with logic delivers a recording of Python results beside the upstream tests, and each Neo4j operations unit delivers a recording through a fake executor (rule R25). The pilot showed that the upstream tests alone find few defects.
- An implementer and a reviewer write the full report to a file, and the lead reads the file. The reply to the lead is a short summary.

## Results

### Layer 0, the skeleton for layers 1 to 6, 2026-11-27

Five packets declared the types and the signatures of the modules of layers 1 to 6. One Opus 5.5 high reviewer read each packet and tried to refute it.

| Packet | Modules | Review findings |
| --- | --- | --- |
| S1 | The driver contract, `helpers`, `tracer`, and the query modules | 4 contract problems |
| S2 | `nodes`, `edges`, and `namespaces` | 7 minor |
| S3 | `llm_client`, `embedder`, `cross_encoder`, and `prompts` | 5 important, 9 minor |
| S4 | `driver.neo4j_driver` and `driver.neo4j.operations` | 1 important, 9 minor |
| S5 | `search` and `decorators` | 1 important, 3 minor |

What the reviews changed:

- The work function of `ExecuteWriteAsync` receives a `Transaction`, and `Neo4jTransaction` wraps an `IAsyncQueryRunner`.
- The tracer takes the body of a span (decision D45), because the end of the Python context manager depends on the exception of the body. This showed upstream defect U19.
- `GraphDriver` and `LLMClient` have a `PythonName`, for the messages that Python builds from a class name.
- The wrapper of `handle_multiple_group_ids` takes the two values that it reads, and has one overload for each result shape.
- The reranker seam takes the ported `Message` and a nullable model id.
- The rulebook has one row in its change table for each round. Open item O12 is closed, and O4 has 25 more cases.

The lead did not apply two findings: the docstrings and the section comments of the Neo4j files. Each unit copies them with its body (rule R24).

Commands at the tip, after the rebase on `main`:

- `scripts/check.sh --test`: `check: passed`. 2,590 tests, 0 failed, 0 skipped.
- `dotnet test --project tests/Port.Evaluation`: 2,214 tests, 0 failed.
- `python3 -B port/judge/run.py --build dotnet`: 1,999 cases, 403 passed, 1,596 expected failures, 0 unexpected, 0 fixed, 0 bound and wrong.
- `scan_port.py src --allow-todo`: 1,136 `TODO(port)` markers in 115 files. The other 105 findings are `NotImplementedException` sites that port a Python `raise NotImplementedError` (rule R4).

Not in this pull request: the two skeleton packets for layers 7 to 9. They are outside the goal of decision D39, which ends at layer 6.

Found beside the plan: `scripts/mutate.sh` gave no result on 2026-11-26. Two tests, `PyAstTests.LiteralEval_matches_Python` and `PyAstTests.LiteralEval_of_long_and_deeply_nested_texts_matches_Python`, stop the test host in a mutated build. Stryker then runs them against each mutant, and each mutant gets the status `RuntimeError`. A probe on `PyHash.cs` with 56 mutants took 159 seconds and gave 56 errors. With the two tests skipped it took 8 seconds and killed 56. The probable cause is a stack overflow in the mutated parser. The lead fixes the two tests before the mutation run of layer 5.
