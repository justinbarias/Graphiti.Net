# Port state

Read this file and [queue.json](queue.json) before any port work. Update both before you stop.
The porting skill is `port-python-to-dotnet`. Its standing rules apply in every phase.

## Now

| Item | Value |
| --- | --- |
| Phase | 6. Plan 0003 is done except task 5 (the pruning rule), which runs now. The pilot ported five units from the rulebook alone: `errors`, `search.search_filters`, `prompts.dedupe_nodes`, `utils.content_chunking`, and `driver.neo4j.operations.entity_node_ops`. Plan 0004 is active. Layer 0 is done for layers 1 to 6: the skeleton has 571 members that throw `NotSupportedException` with a `TODO(port)` marker. Layer 1 is done: 11 modules, each with two reviews and one fix round. Layers 2 to 6 are done, with the end-to-end tests after layers 5 and 6. |
| Gate | Gate 1 passed on 2026-11-24. Gate 2 passed on 2026-11-25. `port/judge/` is read-only, and `.claude/settings.json` denies edits to each file in it except `expected-failures.txt` (decision D38). Gate 3 passed on 2026-11-26: the lead passed it under decision D39, and the maintainer reads `port/RULES.md` at the end of the run. Gate 4 is next, after plan 0004. |
| Last action | 2026-11-27: layer 6 of plan 0004 (`search.search_config`, `search.search_config_recipes`, `search.search_helpers`, `decorators`, `search.search_utils`, and `search.search`, with the end-to-end test of the 16 search recipes on Neo4j). 146 judge cases left `expected-failures.txt`, and 624 stay. Layer 5 merged as pull request 39. 2026-11-27: layer 5 of plan 0004 (`driver.neo4j_driver` and the modules of `driver.neo4j.operations`, with the end-to-end test of the graph types on Neo4j). 23 judge cases left `expected-failures.txt`, and 770 stay. Open item O7 is closed. The recordings of `entity_node_ops` and of the namespaces now reach each method. Layer 3 merged as pull request 38. 2026-11-27: layer 3 of plan 0004 (13 modules of `llm_client`, `embedder`, and `cross_encoder`, and the `Microsoft.Extensions.AI` adapters). 56 judge cases left `expected-failures.txt`, and 793 stay. The lead chose D48. 2026-11-27: layer 4 of plan 0004 (the prompt modules `extract_nodes`, `snippets`, `extract_edges`, `dedupe_edges`, `extract_nodes_and_edges`, `eval`, `summarize_nodes`, `summarize_sagas`, `names`, and `lib`). 193 judge cases left `expected-failures.txt`, and 849 stay. Layer 2 merged as pull request 35. 2026-11-27: layer 2 of plan 0004 (`nodes`, `edges`, `driver.driver`, `driver.record_parsers`, `driver.operations.graph_utils`, `driver.operations.graph_ops`, and `utils.ontology_utils.entity_types_utils`). 276 judge cases left `expected-failures.txt`. The reviews added five `PyCompat` helpers: `Py.NotNone`, `Py.ZipStrict`, `Py.Receiver`, `Py.Int` for an open value, and the type name of a function. Layer 1 merged as pull request 34. 2026-11-27: layer 1 of plan 0004 (`helpers`, `utils.datetime_utils`, `utils.text_utils`, `graph_queries`, the two query modules of `models`, `tracer`, `graphiti_types`, and `namespaces`). 278 judge cases left `expected-failures.txt`. The reviews added decisions D46 and D47 and two `PyCompat` helpers: `Py.Iter` and `PyNumpy.AddReduce`. 2026-11-27: the skeleton for layers 1 to 6 (plan 0004, layer 0, packets S1 to S5), after one review of each packet. The reviews changed 12 rules and added decision D45, upstream defect U19, and 25 cases to open item O4. 2026-11-26 (tasks 6, 7, and 8): the five pilot units merged (pull requests 24, 25, 28, 29, and 30), each after two reviews and one fix round. The support code and the rule changes of the reviews merged as pull request 27. The lead passed gate 3. 2026-11-26 (tasks P3, P6, P7, P8): `PyJson`, `PyAst`, `Pydantic`, the judge host codec, and the runner change are on `main`. The maintainer applied the runner patch and narrowed the deny list. The rulebook changed in R1, R4, R7, and R22. 2026-11-26 (task 4): wrote `scripts/mutation_survivors.py` and ran Stryker two times on `PyCompat` (93.22% each). 2026-11-26: fixed the 14 important findings of one Opus 5.5 adversarial review of the public surface design. The maintainer decided D36. 2026-11-25: wrote the design. The maintainer decided D24 to D35 and approved the page. The rulebook changed in R1, R2, R3, R4, R7, R8, R15, R17, R19, and R22. Before that: wrote the rulebook, [RULES.md](RULES.md), with 25 repository rules, the `PyCompat` helper list, the general rules of the skill, and a map from each of the 78 entries of GAPS.md to a rule. The maintainer decided D20 to D23. One Opus 5.5 adversarial review found 16 important findings, and each one changed the rulebook. 2026-11-25 (task 3): built `src/Graphiti.Net.PyCompat` (scalar and text helpers) and `tests/Graphiti.Net.Property.Tests` (55 tests against Python values). |
| Next action | Plan 0003, task 5: apply the pruning rule to the mutation run, and move the plan to `completed`. Plan 0004: layers 1 to 6 and both end-to-end tests are done, which is the end of the goal of decision D39. The maintainer reads the gate log rows of layers 1 to 6 before layer 7 starts. The two skeleton packets for layers 7 to 9 come after that. The fix of the two `PyAst` tests that stop the test host in a mutated build is on the branch `test/pyast-large-stack`. It waits for the maintainer, and no mutation run gives a result before it merges. |

## Queue counts

182 units. 61 have the status `reviewed`: the 8 units of the pilot, 6 units of layer 1 (`helpers`, `graph_queries`, the two query modules of `models`, `tracer`, `utils.text_utils`), `driver.falkordb` (its `__init__` holds only `STOPWORDS`), 7 units of layer 2 (`nodes`, `edges`, `driver.driver`, `driver.record_parsers`, `driver.operations.graph_utils`, `driver.operations.graph_ops`, `utils.ontology_utils.entity_types_utils`), 11 modules of the driver contract whose members have no logic (9 have abstract members only, and each member of `graph_operations` and `search_interface` raises `NotImplementedError`), the 10 prompt modules of layer 4, `llm_client.decision_prompts` of layer 3, `driver.neo4j.operations.search_ops` of layer 5, `search.search_config_recipes` and `search.search_helpers` of layer 6, and 14 package modules that need no file. 31 have the status `tests_pass`: `utils.datetime_utils`, `graphiti_types`, `namespaces.nodes`, `namespaces.edges`, 12 modules of layer 3 (each ported module of `llm_client`, `embedder`, and `cross_encoder` except `decision_prompts`), `driver.neo4j_driver`, 10 modules of `driver.neo4j.operations` (each one except `search_ops`), and 4 modules of layer 6 (`search.search_config`, `search.search_utils`, `search.search`, and `decorators`). The notes of each one in the queue say what it waits for. 90 have the status `todo`. 114 are in MVP scope. See the scope table in [DECISIONS.md](DECISIONS.md).

## Judge

The runner, both hosts, and the noise rules work. [The judge README](judge/README.md) is the contract, and [judge-design.md](../docs/design-docs/judge-design.md) gives the reasons.

| Layer | Cases | Source |
| --- | --- | --- |
| Pure | 1,965 | Four Sonnet 5.5 agents, checked by the lead. The review removed 6 cases. |
| Flow, hand-written replies | 9 | The lead |
| Flow, no model call | 14 | One Sonnet 5.5 agent wrote 13. The lead wrote 1 seed case. |
| Flow, real-model replies | 11 | 10 with `gpt-4.1-mini` and 1 with `gpt-4.1`. 136 stored replies. |

The real-model scenarios reach `add_episode_bulk`, `remove_episode`, `add_triplet`, `summarize_saga`, custom entity and edge types, sagas, and ingestion with `concurrent_ingestion` on.
`flow.hand.communities` reaches `build_communities` and a community update.

Results on 2026-11-25:

- `python3 port/judge/run.py --build python`: 1,999 cases, 1,999 passed, in two full runs. The flow layer passed 10 more runs. Each of the 12 runs had a different `PYTHONHASHSEED`.
- `python3 port/judge/run.py --build dotnet`: 1 passed (`judge:embed`), 1,998 expected failures, 0 unexpected, 0 bound and wrong.
- The .NET judge embedder reproduces the 9 Python vectors bit for bit.
- `port/tools/find_score_ties.py` on all flow cases: 0 ties.

## What the recording found

| Finding | Effect |
| --- | --- |
| Label order reaches prompt text and Cypher text, not only the `labels` values. | The label rule covers all three places and the reply keys. GAPS F13, decision D18. |
| Upstream sorts full-text rows by score only. Rows with equal scores change order between runs. | One old case was unstable in about 1 run of 60. Five case texts were changed. The new tool `port/tools/find_score_ties.py` finds ties. GAPS U15. |
| A full-text score depends on the history of the index. Entries of deleted nodes stay in its statistics for some time, so the order of two rows that match different words changed between runs. | Each host now gives each flow case an empty database with new full-text indexes. Scores are then the same in each run. The judge needs the database for itself. |
| `add_episode_bulk` keeps the duplicate fact with the smallest UUID, which is random. | `flow.recorded.bulk` has one fact for each pair of entities. A break test on the merge branch was missed for that reason. GAPS U18. |
| `build_communities` builds its prompts in an order that changes between runs. | The call moved from the real-model scenario to `flow.hand.communities`, where all nodes of a community have one summary. GAPS U16. |
| Three episodes that ran at the same time on one group gave different graphs in 2 of 6 runs. | `flow.recorded.concurrent` now runs same-group episodes in sequence, and runs a parallel step on two other groups. GAPS J14. |
| `add_episode` with `update_communities=True` raises `ValueError` for any node count other than two. | An upstream defect. The cases pin it. GAPS U14. |
| With the base LLM path, `gpt-4.1-mini` answered 4 attribute requests with a copy of the JSON schema, and `add_episode` failed. | `flow.recorded.custom_types` pins the failure, and `flow.recorded.custom_types_strong` pins the success with `gpt-4.1`. This is a known cost of decision D17. GAPS F23. |

## Adversarial review

One Opus 5.5 subagent at high effort, 2026-11-25. It reported 6 important findings and 14 smaller ones.
[judge-design.md](../docs/design-docs/judge-design.md) lists each important finding and its answer.

| Finding | State |
| --- | --- |
| I1. Sorted lists hid the order of results | Fixed. Break tests 12 and 13. |
| I2. `<now>` hid the source and the format of a timestamp | Fixed. Break tests 14 and 15. |
| I3. An integer matched a near float | Fixed. |
| I4. A listed case was free to fail for any reason | Fixed. The runner reports `bound_wrong`. |
| I5. The capture seam is not the path of the default client | Decided: D17 keeps the base path. |
| I6. Public calls that no scenario reaches | Closed for six calls. Open for the surfaces in GAPS.md J13 and J14, debt T12 and T16. |

## Break tests

`python3 port/tools/break_tests.py` changes one piece of text in a copy of upstream and runs the judge. The pinned checkout is never changed.
The first run missed breaks 7 and 10. Those misses showed two gaps, and new scenarios closed them.
Breaks 12 to 15 come from the adversarial review. Before the review changes, the judge missed all four.
Breaks 16 to 19 target the calls that the real-model scenarios added. Break 20 is the control for the script. The table is the run on 2026-11-25.

| # | Break | Judge result | First failing case |
| --- | --- | --- | --- |
| Control | An unchanged copy of upstream | Passed | |
| 1 | One word changed in the text extraction prompt (`prompts/extract_nodes.py`) | Caught: 18 flow, 4 pure | `flow.hand.kendra_adidas` |
| 2 | RRF rank constant off by one (`search/search_utils.py`) | Caught: 8 flow, 21 pure | `flow.direct.isolation_search_recipes` |
| 3 | The `~` escape removed from the full-text sanitizer (`helpers.py`) | Caught: 6 flow, 4 pure | `flow.direct.ops_search_nodes` |
| 4 | Fuzzy dedupe threshold lowered from 0.9 to 0.5 (`utils/maintenance/dedup_helpers.py`) | Caught: 2 flow, 1 pure | `flow.recorded.bulk` |
| 5 | A node query returns the name in place of the summary (`models/nodes/node_db_queries.py`) | Caught: 25 flow, 2 pure | `flow.direct.namespaces_entity_nodes` |
| 6 | A naive datetime is no longer set to UTC (`utils/datetime_utils.py`) | Caught: 12 pure | `pure.datetime.ensure_utc.naive` |
| 7 | The group filter dropped from one edge search query (`driver/neo4j/operations/search_ops.py`) | Caught: 2 flow | `flow.direct.ops_search_edges` |
| 8 | A contradicted fact keeps an open validity window (`utils/maintenance/edge_operations.py`) | Caught: 4 flow, 4 pure | `flow.hand.contradiction` |
| 9 | Similarity search needs a score of 0.99 (`search/search_utils.py`) | Caught: 20 flow, 30 pure | `flow.direct.isolation_search_recipes` |
| 10 | An invalidated fact gets no expiry time (`utils/maintenance/edge_operations.py`) | Caught: 1 flow | `flow.hand.historical_fact` |
| 11 | The group filter dropped from the inline edge full-text search (`search/search_utils.py`) | Caught: 19 flow | `flow.direct.isolation_search_recipes` |
| 12 | Search results return their nodes in reverse order (`search/search.py`) | Caught: 4 flow | `flow.direct.isolation_search_recipes` |
| 13 | add_episode returns its nodes in reverse order (`graphiti.py`) | Caught: 14 flow | `flow.hand.kendra_adidas` |
| 14 | An expiry time copies the creation time in place of a new clock reading (`utils/maintenance/edge_operations.py`) | Caught: 1 flow | `flow.hand.historical_fact` |
| 15 | A contradicted fact's expiry time copies its creation time (`utils/maintenance/edge_operations.py`) | Caught: 4 flow | `flow.hand.contradiction` |
| 16 | remove_episode keeps the removed episode on the facts that it supported (`graphiti.py`) | Caught: 1 flow | `flow.recorded.lifecycle` |
| 17 | A new community gets its summary as its name (`utils/maintenance/community_operations.py`) | Caught: 1 flow | `flow.hand.communities` |
| 18 | A custom entity type is not written as a node label (`utils/maintenance/node_operations.py`) | Caught: 2 flow | `flow.recorded.custom_types` |
| 19 | Bulk ingestion leaves a fact on a node that dedupe removed (`utils/bulk_utils.py`) | Caught: 10 flow, 4 pure | `flow.hand.contradiction` |
| 20 | Control for the script: an import error must also fail the judge (`nodes.py`) | Caught: 34 flow, 207 pure | `flow.direct.namespaces_entity_nodes` |

## Upstream test baseline

`port/tools/upstream-tests.sh`, 2026-11-24: 1026 passed and 14 skipped without a database, 40 passed on Neo4j, and `tests/test_add_triplet.py` has 11 failures on Neo4j that upstream CI does not see.

## Blocked units

None.

Five `TODO(port)` markers stay in ported units. Each one is a call of a member that only the Neptune driver has, and that driver is out of the MVP (decision D10). Each branch throws `NotSupportedException` at the call (rule R24). The maintainer accepts or rejects each marker at gate 4.

| File | Site | Member |
| --- | --- | --- |
| `src/Graphiti.Net/Core/Nodes.cs` | `CommunityNode.save` | `driver.save_to_aoss` |
| `src/Graphiti.Net/Core/Search/SearchUtils.cs` | `edge_fulltext_search`, the Neptune branch | `driver.run_aoss_query` |
| `src/Graphiti.Net/Core/Search/SearchUtils.cs` | `node_fulltext_search`, the Neptune branch | `driver.run_aoss_query` |
| `src/Graphiti.Net/Core/Search/SearchUtils.cs` | `episode_fulltext_search`, the Neptune branch | `driver.run_aoss_query` |
| `src/Graphiti.Net/Core/Search/SearchUtils.cs` | `community_fulltext_search`, the Neptune branch | `driver.run_aoss_query` |

## Rule changes

| Date | Change | Units to port again |
| --- | --- | --- |
| 2026-11-25 | First version of [RULES.md](RULES.md). | None. No unit is ported. |
| 2026-11-25 | Fixes after the adversarial review. The rulebook lists them in its own change table. | None. No unit is ported. |
| 2026-11-25 | After the public surface design (D24 to D35). Ported code uses the namespace `Graphiti.Core` and the folder `src/Graphiti.Net/Core/`. The facade class is `GraphitiCore`, and the exception classes are public. New rules cover provider errors, `Asyncio.CallHostAsync`, the custom type descriptor, and `HttpClient` for the decisions client. | None. No unit is ported. |
| 2026-11-25 | After the adversarial review of the design. The rulebook lists the changes in its own change table. | None. No unit is ported. |
| 2026-11-26 | After the integrated review of the pull request stack. R12 uses the `PyText` members for `split(sep)` and `strip(chars)`. R1 names the `PyCompat` project in the `using` rule. R4 has a row for `UnicodeDecodeError` and the limit of `OverflowException`. | None. No unit is ported. |
| 2026-11-26 | After the pre-pilot scan. R24 names the members of modules outside the pilot that a pilot unit ports for real. R22 says who removes a passing case from `expected-failures.txt`. Open item O5 is decided (D38). | None. No unit is ported. |
| 2026-11-26 | After the reviews of the `PyCompat` helpers. R7 (enum attributes), R8 (cleanup tokens, `WaitForAsync`, gather of tasks), R11, R12, R13, R14 (`Py.SortInPlace` for float, datetime, and open keys), R18, and Part 2 changed. New open item O9. The rulebook lists the changes in its own change table. | None. No unit is ported. |
| 2026-11-26 | After the pilot: three rounds of rule changes from the reviews of the five units and of the support code. The rulebook lists them in its own change table. | `search_filters`: the member order of its three models, done in pull request 27. `content_chunking`: `.Value` on each `PyPatch<T>` read, done before its merge. No unit waits. |

[GAPS.md](GAPS.md) holds 22 language gaps, 15 surfaces that the judge cannot reach, and 19 upstream defects. [TYPE_HINTS.md](TYPE_HINTS.md) holds the 127 signatures that upstream leaves without hints.

## Deviations from the porting skill

| Skill text | What this port does | Why |
| --- | --- | --- |
| Scaffold in phase 4 | The solution, the Neo4j sidecar test, and the tooling exist from the bootstrap. `PyCompat` was built in phase 4 (plan 0003, task 3). | The maintainer asked for the development tooling first. |
| Port the unit tests with each module | pytest ports live in `tests/Port.Evaluation` and are temporary. | Decision D6. |
| Lead model is Opus | The lead session ran on Fable 5.2. | The maintainer's global rules name Fable as orchestrator. |
| Phase 3 completes type hints in the Python source | The source is not changed. `port/tools/infer_type_hints.py` writes the signatures that pyright infers into `TYPE_HINTS.md`. | The oracle stays identical to the pinned commit. |
| The .NET host is in `port/judge/` | It is `tests/Graphiti.Net.JudgeHost`. | Each ported unit adds a binding, and `port/judge/` is read-only after gate 2. |

## Open questions

For the maintainer, before gate 4:

1. Accept or reject the open surfaces by name: GAPS.md J13 and J14.
2. Accept or reject by name the 23 wrong-type cases that decision D20 does not settle: RULES.md, open item O4.

For the maintainer, from the public surface design (the page lists all 12 open questions):

1. Does `PyCompat` ship inside the `Graphiti.Net` package or as its own package? Needed before plan 0005.
2. A library row for `Microsoft.Extensions.Http`, or an `HttpClient` that the decisions client takes directly. Needed before the decisions client unit.
3. Does the chat adapter strip a markdown code fence from a reply? Needed before the adapter unit.

For the maintainer, from the pilot and the start of plan 0004:

1. A new member on the driver session contract for a write with a timeout (`utils/bulk_utils.py:192` uses `neo4j.unit_of_work` in core code). RULES.md, open item O6. Needed before layer 7.
2. Decision D43: accept or reject the order of the pair chunks of `generate_covering_chunks` for a chunk size of 0 or 1. Needed at gate 4.
3. About 250 upstream test sites replace a module-level function or a method. RULES.md, open item O10. Needed before layer 7.
4. Apply `port/patches/judge-readme-units.patch`, which names the `Units/` folder in the judge README. [patches/README.md](patches/README.md) gives the steps.
5. The scanner of the porting skill reports each `NotImplementedException`. Rule R4 requires that type where Python raises `NotImplementedError`. Accept those sites by name at gate 4.
6. Two pilot worktrees hold an untracked `.upstream` symbolic link that an implementer made for a recorder: `.claude/worktrees/agent-a6354ed2b2d3e0ca4` and `.claude/worktrees/agent-a289034ea91cceca3`. A delete command of one implementer was denied, and the lead did not delete the links.

Decided on 2026-11-25: D17 (the base LLM path), D18 (label order is noise), D19 (seven judge design changes). The maintainer closed gate 2 on 2026-11-25 after the final results.
Decided on 2026-11-25 for the rulebook: D20 (wrong-type cases), D21 (the Python value model), D22 (the exclusive scheduler), D23 (the ambient service holder).
Decided on 2026-11-26 for the pilot: the pilot ports the members that R24 names for real, and `entity_node_ops` only builds (D37). The lead removes passing ids from `expected-failures.txt`, and each other judge change is a patch file (D38). The lead continues through plan 0004, layer 6, with standing approvals (D39).
Decided on 2026-11-25 for the public surface: D24 to D35. Decided on 2026-11-26 after the review: D36 (naive dates) and an amendment to D24. [DECISIONS.md](DECISIONS.md) holds one row for each.

## Checks not run

- The judge did not run against upstream's default `OpenAIClient` path. Decision D17 puts that path outside the contract.
- CI does not run the judge against the Python build. Debt T13.
- No second adversarial review ran on the changes after the first review. The maintainer closed gate 2 without one.
- Five units follow the rulebook. Each one had two reviews and one fix round, and no second review ran on the fixes.
- The Neo4j driver and its operations ran on one database: Neo4j 5.26.2 of `docker-compose.yml`, as a single instance. No test ran on a cluster, on Aura, or on another version.
- No flow case of the judge ran against the .NET build. The units of layers 3, 5, and 6 with no pure case wait for layer 9 of plan 0004.
- The search ran on Neo4j with a fake embedder and a fake cross-encoder that are pure functions of their text. No test ran a search with real embeddings. The Neptune and FalkorDB branches of `search_utils` ran against a fake driver only.
- The layer 6 packets had two reviews and one fix round. The end-to-end test of the search recipes had no review pair.
- The layer 5 packets had two reviews and one fix round. One adversarial pass read the fixes and the end-to-end test of the graph types. No review ran after the fixes of that pass.
- On a database that already has the Graphiti indexes, the two index tests on Neo4j pass with no index build. The test with a fake of the driver package holds the build, and so does a run on an empty database, as in CI.
- One adversarial review ran on the public surface design and its rule changes. No second review ran after the fixes. No code follows the design yet.
- The `Microsoft.Extensions.AI.Abstractions` facts of the design come from Learn pages for version 10.11.0, not from the 11.0.1 assembly. Debt T21.
- The model adapters of layer 3 had no review pair, and no test ran them against a real model provider. The fakes implement `IChatClient` and `IEmbeddingGenerator`.
- The decisions client and the reranker ran against fakes at the HTTP boundary and at the chat seam. No test called a real endpoint.
- Decision D22 depends on a rule at each library boundary. One callback exists, `Neo4jSession.ExecuteWriteAsync`, and a test fails without `Asyncio.OnLoopAsync` there (open item O7, closed). No other library callback is ported yet.
- The site lists in the rulebook (regular expressions, sets, `getattr` calls, validators) come from `grep` over the 114 MVP modules. A site that the patterns missed is not in the lists. The rulebook tells an implementer to stop at such a site.
- Nobody applied the pruning rule. On two Stryker runs on `PyCompat`, the script listed 13 and 14 tests (plan 0003, task 5).
- The devcontainer and the Claude cloud session hook were not started. See [docs/RELIABILITY.md](../docs/RELIABILITY.md).
