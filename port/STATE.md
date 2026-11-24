# Port state

Read this file and [queue.json](queue.json) before any port work. Update both before you stop.
The porting skill is `port-python-to-dotnet`. Its standing rules apply in every phase.

## Now

| Item | Value |
| --- | --- |
| Phase | 2 and 3. The judge and the gap list are built. Gate 2 is open. |
| Gate | Gate 1 passed on 2026-11-24. Gate 2 waits for the maintainer: review the judge, the break tests, and the three design changes below. |
| Last action | 2026-11-24: built the judge, recorded 1,992 cases, ran the break tests, wrote the gap list. |
| Next action | The adversarial review by an Opus 5.5 subagent, then gate 2. The 10 scenarios in `port/judge/pending/` stay pending by the maintainer's decision, and are recorded later under the rule for cases added after the gate. |

## Queue counts

182 units. All have status `todo`. 114 are in MVP scope. See the scope table in [DECISIONS.md](DECISIONS.md).

## Judge

The runner, both hosts, and the noise rules work. [The judge README](judge/README.md) is the contract, and [judge-design.md](../docs/design-docs/judge-design.md) gives the reasons.

| Layer | Cases | Source |
| --- | --- | --- |
| Pure | 1,971 | Four Sonnet 5.5 agents, checked by the lead |
| Flow, hand-written replies | 8 | The lead |
| Flow, no model call | 13 | One Sonnet 5.5 agent, checked by the lead |
| Flow, real-model replies | 0 of 10 | Waiting in `port/judge/pending/`. The OpenAI account has no credits. |

Results on 2026-11-24:

- `python3 port/judge/run.py --build python`: 1,992 cases, 1,992 passed, three runs in a row, 18 seconds each.
- `python3 port/judge/run.py --build dotnet`: 1 passed (`judge:embed`), 1,991 expected failures.
- The .NET judge embedder reproduces the 9 Python vectors bit for bit.

## Break tests

`python3 port/tools/break_tests.py` changes one piece of text in a copy of upstream and runs the judge. The pinned checkout is never changed.
The first run missed breaks 7 and 10. Those misses showed two gaps, and new scenarios closed them. The table is the final run.

| # | Break | Judge result | First failing case |
| --- | --- | --- | --- |
| Control | An unchanged copy of upstream | Passed | |
| 1 | One word changed in the text extraction prompt (`prompts/extract_nodes.py`) | Caught: 7 flow, 4 pure | `flow.hand.kendra_adidas` |
| 2 | RRF rank constant off by one (`search/search_utils.py`) | Caught: 2 flow, 21 pure | `flow.direct.isolation_search_recipes` |
| 3 | The `~` escape removed from the full-text sanitizer (`helpers.py`) | Caught: 5 flow, 4 pure | `flow.direct.ops_search_nodes` |
| 4 | Fuzzy dedupe threshold lowered from 0.9 to 0.5 (`utils/maintenance/dedup_helpers.py`) | Caught: 1 pure | `pure.dedupe.Delta.match_nodes.fuzzy_typo` |
| 5 | A node query returns the name in place of the summary (`models/nodes/node_db_queries.py`) | Caught: 12 flow, 2 pure | `flow.direct.namespaces_entity_nodes` |
| 6 | A naive datetime is no longer set to UTC (`utils/datetime_utils.py`) | Caught: 12 pure | `pure.datetime.ensure_utc.naive` |
| 7 | The group filter dropped from one edge search query (`driver/neo4j/operations/search_ops.py`) | Caught: 2 flow | `flow.direct.ops_search_edges` |
| 8 | A contradicted fact keeps an open validity window (`utils/maintenance/edge_operations.py`) | Caught: 1 flow, 4 pure | `flow.hand.contradiction` |
| 9 | Similarity search needs a score of 0.99 (`search/search_utils.py`) | Caught: 7 flow, 30 pure | `flow.direct.isolation_search_recipes` |
| 10 | An invalidated fact gets no expiry time (`utils/maintenance/edge_operations.py`) | Caught: 1 flow | `flow.hand.historical_fact` |
| 11 | The group filter dropped from the inline edge full-text search (`search/search_utils.py`) | Caught: 7 flow | `flow.direct.isolation_search_recipes` |
| 12 | Control for the script: an import error must also fail the judge (`nodes.py`) | Caught: 21 flow, 207 pure | `flow.direct.namespaces_entity_nodes` |

## Upstream test baseline

`port/tools/upstream-tests.sh`, 2026-11-24: 1026 passed and 14 skipped without a database, 40 passed on Neo4j, and `tests/test_add_triplet.py` has 11 failures on Neo4j that upstream CI does not see.

## Blocked units

None.

## Rule changes

None. `port/RULES.md` does not exist yet. Phase 4 writes it.
[GAPS.md](GAPS.md) holds 22 language gaps and 11 surfaces that the judge cannot reach. [TYPE_HINTS.md](TYPE_HINTS.md) holds the 127 signatures that upstream leaves without hints.

## Deviations from the porting skill

| Skill text | What this port does | Why |
| --- | --- | --- |
| Scaffold in phase 4 | The solution, the Neo4j sidecar test, and the tooling exist from the bootstrap. `PyCompat` still waits for phase 4. | The maintainer asked for the development tooling first. |
| Port the unit tests with each module | pytest ports live in `tests/Port.Evaluation` and are temporary. | Decision D6. |
| Lead model is Opus | The lead session ran on Fable 5.2. | The maintainer's global rules name Fable as orchestrator. |
| Phase 3 completes type hints in the Python source | The source is not changed. `port/tools/infer_type_hints.py` writes the signatures that pyright infers into `TYPE_HINTS.md`. | The oracle stays identical to the pinned commit. |
| The .NET host is in `port/judge/` | It is `tests/Graphiti.Net.JudgeHost`. | Each ported unit adds a binding, and `port/judge/` is read-only after gate 2. |

## Open questions

For the maintainer at gate 2:

1. Accept the three changes to the approved judge design: UUID names from content, the .NET host outside `port/judge/`, and a fresh host process for each flow case.
2. Decide on one more noise rule: sort `labels` lists. Upstream builds node labels from a set, so their order changes between processes (finding F13 in GAPS.md). Until then, no scenario stores a node with a second label.
3. Decided on 2026-11-25: the 10 pending scenarios stay pending. They are recorded after the OpenAI account has credits.

## Checks not run

- No judge run, because the judge does not exist.
- No mutation score. Stryker loaded its configuration and found 0 mutants, because the product projects contain no code yet.
- The devcontainer and the Claude cloud session hook were not started. See [docs/RELIABILITY.md](../docs/RELIABILITY.md).
