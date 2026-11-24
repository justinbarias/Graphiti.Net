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

- 2026-11-24, tasks 3 to 9, 11, 12: done, except the 10 real-model scenarios of task 4.

| Task | Result |
| --- | --- |
| 3. Hosts | Python host and `tests/Graphiti.Net.JudgeHost`. The C# judge embedder matches 9 Python vectors bit for bit. |
| 4. Flow layer | 21 scenarios recorded: 8 with hand-written replies, 13 with no model call. 10 real-model scenarios wait in `port/judge/pending/`, because the OpenAI account has no credits. |
| 5 to 7. Pure layer | 1,971 cases from four Sonnet 5.5 agents: text and chunking 404, queries and filters 410, prompts and schemas 323, dedupe, ranking, and models 827, plus 7 seed cases. The agents wrote more cases than the brief asked for. |
| 8. Runner | `port/judge/run.py`. 1,992 cases pass on Python three times in a row. |
| 9. Break tests | `port/tools/break_tests.py`: 1 control and 12 breaks, all caught. The first run missed 2, which exposed two gaps in the flow layer. The table is in `port/STATE.md`. |
| 10. Type hints | Replaced: `port/TYPE_HINTS.md` holds the 127 signatures that pyright infers. Upstream is not changed. |
| 11. Gaps | `port/GAPS.md`: 22 language gaps, 15 surfaces outside the judge, 13 upstream defects, 22 findings. |
| 12. Gate wiring | `scripts/check.sh --test` runs the embedder self-check and the judge against the .NET build. The deny rules for `port/judge/` go back at gate 2. |

- 2026-11-25, review: one Opus 5.5 subagent at high effort reviewed the judge, with a brief to refute it. It reported 6 important findings and 14 smaller ones.

| Item | Result |
| --- | --- |
| Fixed in `run.py` | I1 order of results, I2 clock rank and shape, I3 number rule, I4 `bound_wrong`. |
| Cases | 6 defective pure cases removed. `flow.hand.guard_invisible_chars` added. 5 steps declare `unordered`. |
| Totals | 1,987 cases: 1,965 pure and 22 flow. They pass on Python in three full runs with `PYTHONHASHSEED` 1, 2, and 3. |
| .NET build | 1 passed, 1,986 expected failures, 0 unexpected, 0 bound and wrong. |
| Break tests | 1 control and 16 breaks, all caught. Breaks 12 to 15 are the reviewer's. The judge missed all four before the fixes. |
| Open | I5, the contract choice for the LLM path. I6, the calls that no scenario reaches. Both are in `port/STATE.md` and `port/GAPS.md`. |

- 2026-11-25, gate 2 decisions and the real-model recording. The maintainer approved D17 (the base LLM path), D18 (label order is noise), and D19 (seven judge design changes), and supplied a working key.

| Item | Result |
| --- | --- |
| Label rule | The runner sorts label lists in values, in prompt text, and in Cypher text. The Python host sorts them in reply keys. |
| Real-model scenarios | 11 recorded: 10 with `gpt-4.1-mini`, 1 with `gpt-4.1`. 136 stored replies. |
| Hand-written scenario | `flow.hand.communities` covers `build_communities` and a community update. |
| Database preparation | Each host gives each flow case an empty database with new full-text indexes. Without it, the order of search rows changed between runs. |
| New tools | `port/tools/find_score_ties.py` and `port/tools/prune_replies.py`. |
| Totals | 1,999 cases: 1,965 pure and 34 flow. Two full runs and 10 flow runs pass on Python, each with a different hash seed. |
| .NET build | 1 passed, 1,998 expected failures, 0 unexpected, 0 bound and wrong. |
| Break tests | 1 control and 20 breaks, all caught. One more break, on the merge of duplicate facts in a bulk call, was missed and removed, because upstream leaves that behavior open (U18). |
| Upstream defects | 5 more: U14 to U18 in `port/GAPS.md`. |

Open before gate 2 closes: the maintainer reads the results in `port/STATE.md`. No second adversarial review ran on the changes after the first review.

- Finding from task 2: upstream CI never runs `tests/test_add_triplet.py` with a database, and 11 of its 13 tests fail on Neo4j at the pinned commit. The first error is `ValueError: embedder returned 0 vectors for 1 queries`, which points at the test's mock embedder. Task 6 must not treat this file as a passing oracle. The judge covers `add_triplet` through layer 3 scenarios.
