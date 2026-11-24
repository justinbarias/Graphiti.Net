# Port state

Read this file and [queue.json](queue.json) before any port work. Update both before you stop.
The porting skill is `port-python-to-dotnet`. Its standing rules apply in every phase.

## Now

| Item | Value |
| --- | --- |
| Phase | 2 and 3. The judge and the gap list are built. Gate 2 is open. |
| Gate | Gate 1 passed on 2026-11-24. Gate 2 waits for the maintainer: review the judge, the break tests, the review result, and the open questions below. |
| Last action | 2026-11-25: an Opus 5.5 subagent reviewed the judge with a brief to refute it. The lead fixed findings I1 to I4, removed 6 defective cases, added 1 scenario and 4 break tests, and recorded all golden files again. |
| Next action | Gate 2. After approval: put the `port/judge/**` deny rules back into `.claude/settings.json`, then start plan 0003. The 10 scenarios in `port/judge/pending/` stay pending by the maintainer's decision. |

## Queue counts

182 units. All have status `todo`. 114 are in MVP scope. See the scope table in [DECISIONS.md](DECISIONS.md).

## Judge

The runner, both hosts, and the noise rules work. [The judge README](judge/README.md) is the contract, and [judge-design.md](../docs/design-docs/judge-design.md) gives the reasons.

| Layer | Cases | Source |
| --- | --- | --- |
| Pure | 1,965 | Four Sonnet 5.5 agents, checked by the lead. The review removed 6 cases. |
| Flow, hand-written replies | 8 | The lead |
| Flow, no model call | 14 | One Sonnet 5.5 agent wrote 13. The lead wrote 1 seed case. |
| Flow, real-model replies | 0 of 10 | Waiting in `port/judge/pending/`. The OpenAI account has no credits. |

Results on 2026-11-25, after the review changes:

- `python3 port/judge/run.py --build python`: 1,987 cases, 1,987 passed, in three full runs with `PYTHONHASHSEED` 1, 2, and 3. The flow layer passed 8 more runs in a row.
- `python3 port/judge/run.py --build dotnet`: 1 passed (`judge:embed`), 1,986 expected failures, 0 unexpected, 0 bound and wrong.
- The .NET judge embedder reproduces the 9 Python vectors bit for bit.

## Adversarial review

One Opus 5.5 subagent at high effort, 2026-11-25. It reported 6 important findings and 14 smaller ones.
[judge-design.md](../docs/design-docs/judge-design.md) lists each important finding and its answer.

| Finding | State |
| --- | --- |
| I1. Sorted lists hid the order of results | Fixed. Break tests 12 and 13. |
| I2. `<now>` hid the source and the format of a timestamp | Fixed. Break tests 14 and 15. |
| I3. An integer matched a near float | Fixed. |
| I4. A listed case was free to fail for any reason | Fixed. The runner reports `bound_wrong`. |
| I5. The capture seam is not the path of the default client | Open. Question 3 below. |
| I6. Public calls that no scenario reaches | Open in part. GAPS.md J13 and J14, debt T9 and T12. |

## Break tests

`python3 port/tools/break_tests.py` changes one piece of text in a copy of upstream and runs the judge. The pinned checkout is never changed.
The first run missed breaks 7 and 10. Those misses showed two gaps, and new scenarios closed them.
Breaks 12 to 15 come from the adversarial review. Break 16 is the control for the script. Before the review changes, the judge missed all four. The table is the run on 2026-11-25.

| # | Break | Judge result | First failing case |
| --- | --- | --- | --- |
| Control | An unchanged copy of upstream | Passed | |
| 1 | One word changed in the text extraction prompt (`prompts/extract_nodes.py`) | Caught: 8 flow, 4 pure | `flow.hand.kendra_adidas` |
| 2 | RRF rank constant off by one (`search/search_utils.py`) | Caught: 3 flow, 21 pure | `flow.direct.ops_search_nodes` |
| 3 | The `~` escape removed from the full-text sanitizer (`helpers.py`) | Caught: 5 flow, 4 pure | `flow.direct.ops_search_nodes` |
| 4 | Fuzzy dedupe threshold lowered from 0.9 to 0.5 (`utils/maintenance/dedup_helpers.py`) | Caught: 1 flow, 1 pure | `flow.direct.ops_search_nodes` |
| 5 | A node query returns the name in place of the summary (`models/nodes/node_db_queries.py`) | Caught: 13 flow, 2 pure | `flow.direct.namespaces_entity_nodes` |
| 6 | A naive datetime is no longer set to UTC (`utils/datetime_utils.py`) | Caught: 1 flow, 12 pure | `flow.direct.ops_search_nodes` |
| 7 | The group filter dropped from one edge search query (`driver/neo4j/operations/search_ops.py`) | Caught: 2 flow | `flow.direct.ops_search_edges` |
| 8 | A contradicted fact keeps an open validity window (`utils/maintenance/edge_operations.py`) | Caught: 1 flow, 4 pure | `flow.hand.contradiction` |
| 9 | Similarity search needs a score of 0.99 (`search/search_utils.py`) | Caught: 8 flow, 30 pure | `flow.direct.isolation_search_recipes` |
| 10 | An invalidated fact gets no expiry time (`utils/maintenance/edge_operations.py`) | Caught: 1 flow | `flow.hand.historical_fact` |
| 11 | The group filter dropped from the inline edge full-text search (`search/search_utils.py`) | Caught: 8 flow | `flow.direct.isolation_search_recipes` |
| 12 | Search results return their nodes in reverse order (`search/search.py`) | Caught: 2 flow | `flow.direct.isolation_search_recipes` |
| 13 | add_episode returns its nodes in reverse order (`graphiti.py`) | Caught: 5 flow | `flow.hand.kendra_adidas` |
| 14 | An expiry time copies the creation time in place of a new clock reading (`utils/maintenance/edge_operations.py`) | Caught: 1 flow | `flow.hand.historical_fact` |
| 15 | A contradicted fact's expiry time copies its creation time (`utils/maintenance/edge_operations.py`) | Caught: 1 flow | `flow.hand.contradiction` |
| 16 | Control for the script: an import error must also fail the judge (`nodes.py`) | Caught: 22 flow, 207 pure | `flow.direct.namespaces_entity_nodes` |

## Upstream test baseline

`port/tools/upstream-tests.sh`, 2026-11-24: 1026 passed and 14 skipped without a database, 40 passed on Neo4j, and `tests/test_add_triplet.py` has 11 failures on Neo4j that upstream CI does not see.

## Blocked units

None.

## Rule changes

None. `port/RULES.md` does not exist yet. Phase 4 writes it.
[GAPS.md](GAPS.md) holds 22 language gaps and 15 surfaces that the judge cannot reach. [TYPE_HINTS.md](TYPE_HINTS.md) holds the 127 signatures that upstream leaves without hints.

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

1. Accept the changes to the approved judge design. Three are from the build: UUID names from content, the .NET host outside `port/judge/`, and a fresh host process for each flow case. Four are from the review: bags in place of sorted names, clock rank and shape, the number rule, and the `bound_wrong` rule.
2. Decide on one more noise rule: sort `labels` lists. Upstream builds node labels from a set, so their order changes between processes (finding F13 in GAPS.md). Until then, no scenario stores a node with a second label.
3. Decide which upstream LLM path is the contract for the `Microsoft.Extensions.AI` adapter. The judge pins the base path: the schema text is appended to the prompt, and the adapter implements `_generate_response`. The default upstream client, `OpenAIClient`, sends the schema through `responses.parse` and adds no schema text. The lead recommends the base path, because it works with every provider behind `IChatClient`. With that choice, a prompt from Graphiti.Net differs from a prompt of upstream's default client by the schema text. Record the choice in DECISIONS.md.
4. Accept or reject the open surfaces by name: GAPS.md J13 and J14.
5. Decided on 2026-11-25: the 10 pending scenarios stay pending. They are recorded after the OpenAI account has credits.

## Checks not run

- The judge did not run against upstream's default `OpenAIClient` path. See question 3.
- CI does not run the judge against the Python build. Debt T13.
- No mutation score. Stryker loaded its configuration and found 0 mutants, because the product projects contain no code yet.
- The devcontainer and the Claude cloud session hook were not started. See [docs/RELIABILITY.md](../docs/RELIABILITY.md).
