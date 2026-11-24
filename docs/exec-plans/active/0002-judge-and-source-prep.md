# 0002 Judge and source preparation

Port phases 2 and 3. Starts after gate 1. Ends at gate 2.

## Goal

A differential judge that runs from outside both builds, proved to catch breakage, plus the gap list.

## Tasks

| # | Task | Owner, model, effort | Acceptance evidence |
| --- | --- | --- | --- |
| 1 | Write the [judge design document](../../design-docs/judge-design.md): layers, the noise list, the hosts, the reply lookup. Done 2026-11-24. | Lead, Fable 5.2, high | The maintainer accepted all six recommendations on 2026-11-24. |
| 2 | Build the Python environment for upstream with `uv sync --frozen` in `.upstream/graphiti`. Run the upstream non-integration tests. Done 2026-11-24 by the lead, inline. | Lead, Fable 5.2 | `port/tools/upstream-tests.sh` prints the three summary lines in Results. |
| 3 | Build the Python host, the .NET host project, the reply lookup, and the judge embedder in both languages. The .NET host has an empty function table at this point. | Workhorse, Sonnet 5.5, medium | Both embedders reproduce `embedder-vectors.json`. A request with no stored reply fails with a text diff of the closest request. |
| 4 | Layer 3, model boundary. Cover the calls that the design document lists. For each step, record the LLM requests, the embedding requests, the queries with parameters, and the return value. Dump the graph after the last step. | Workhorse, Opus 5.5, medium | Golden files in `port/judge/golden/`. Two recorder runs produce identical files. |
| 5 | Layer 2, pure functions. A thin Python command reads a case from standard input and prints normalized JSON. Cover content chunking, dedupe helpers, search filters, the query builders, prompt rendering, datetime helpers, and RRF and MMR ranking. | Workhorse, Sonnet 5.5, medium | Golden files. The command adds no defaults and no type checks. |
| 6 | Mine the upstream pytest tests for inputs. Add each distinct input as a judge case. | Workhorse, Sonnet 5.5, medium | A table of pytest file to case count in the judge README. |
| 7 | Add hand-written guard cases: empty, zero, negative, very large, null, missing optional fields, an emoji, a combining mark, malformed input. | Workhorse, Sonnet 5.5, medium | Each layer has every guard class. |
| 8 | Write the runner. It takes the build to test as an argument and prints the case count. If the count is under `port/judge/count.txt`, it fails. Against .NET it reads `expected-failures.txt`. | Workhorse, Sonnet 5.5, high | All cases pass against Python. |
| 9 | Break tests. Break the Python code in at least three places for each layer, one at a time. Make sure that the judge fails each time. Undo each break. | Lead, Fable 5.2, medium | A table in `port/STATE.md`: what was broken, what the judge reported. |
| 10 | Phase 3. Complete missing type hints in the upstream checkout as a patch file in `port/patches/`. Rerun the upstream tests and the judge. | Workhorse, Sonnet 5.5, medium | The same pytest counts as task 2. The judge still passes. |
| 11 | Write `port/GAPS.md`: duck-typed calls, decorators, module state, the 21 MVP modules in import cycles, pydantic validators, and the public surface that the judge cannot reach. Record expected values for that surface with scripts in `port/tools/`. | Lead, Fable 5.2, high | Each gap has a chosen answer. |
| 12 | Wire the judge into `scripts/check.sh` and CI. At gate 2, put the `port/judge/**` deny rules back into `.claude/settings.json`. | Lead, Fable 5.2, medium | `scripts/check.sh --test` runs the judge. |

## Constraints

- Golden files come only from the recorder. Never write expected output by hand.
- The noise list is fixed in the judge README. Typical noise: UUIDs, timestamps, durations.
- The recorder needs a Neo4j sidecar. Replies are recorded one time from a real model (choice 3 in the design document), so one recording run needs the maintainer's LLM key.
- The deny rules for `port/judge/` are out of `.claude/settings.json` during this plan, with the maintainer's approval. Task 12 puts them back at gate 2.

## Gate 2

The maintainer reviews the judge and the break test table. After the gate, `port/judge/` is read-only.

## Review

One adversarial review of the judge diff before the gate. The maintainer picks Codex gpt-6-astra high or an Opus 5.5 high subagent.

## Results

- 2026-11-24, task 1: the design document is written. The lead read the upstream seams in `llm_client/client.py`, `driver/neo4j_driver.py`, and `helpers.py`. The maintainer accepted all six recommendations.
- 2026-11-24, task 2: `port/tools/upstream-tests.sh` on Python 3.12.13, macOS arm64, Neo4j 5.26.2. These numbers are the baseline for task 10.

| Configuration | Result |
| --- | --- |
| A. Unit tests, no database (upstream CI job 1) | 1026 passed, 14 skipped, 5 deselected |
| B. Database tests on Neo4j only | 40 passed |
| C. `tests/test_add_triplet.py` on Neo4j only | 11 failed, 2 passed |

- Finding from task 2: upstream CI never runs `tests/test_add_triplet.py` with a database, and 11 of its 13 tests fail on Neo4j at the pinned commit. The first error is `ValueError: embedder returned 0 vectors for 1 queries`, which points at the test's mock embedder. Task 6 must not treat this file as a passing oracle. The judge covers `add_triplet` through layer 3 scenarios.
