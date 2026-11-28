# Port state

Read this file and [queue.json](queue.json) before any port work. Update both before you stop.
The porting skill is `port-python-to-dotnet`. Its standing rules apply in every phase.

## Now

| Item | Value |
| --- | --- |
| Phase | 6. Plan 0003 is done except task 5 (the pruning rule), which runs now. The pilot ported five units from the rulebook alone: `errors`, `search.search_filters`, `prompts.dedupe_nodes`, `utils.content_chunking`, and `driver.neo4j.operations.entity_node_ops`. Plan 0004 is active. Layer 0 is done for layers 1 to 6: the skeleton has 571 members that throw `NotSupportedException` with a `TODO(port)` marker. Layer 1 is done: 11 modules, each with two reviews and one fix round. Layers 2 to 6 are done, with the end-to-end tests after layers 5 and 6. |
| Gate | Gate 1 passed on 2026-11-24. Gate 2 passed on 2026-11-25. `port/judge/` is read-only, and `.claude/settings.json` denies edits to each file in it except `expected-failures.txt` (decision D38). Gate 3 passed on 2026-11-26: the lead passed it under decision D39, and the maintainer reads `port/RULES.md` at the end of the run. Gate 4 is next, after plan 0004. |
| Last action | 2026-11-28: layer 9 of plan 0004 is complete. Pull request 50 merged the core: `graphiti`, and the 34 flow cases of the judge on the .NET build. The second pull request holds the public .NET surface: `IGraphiti`, the options, the registration, 99 public types, and end-to-end tests E8 and E9. A public call gives the host the error of the call, where upstream defect U19 wrapped it. Pull request 54 merged it. Plan 0005, task 2 then closed 21 listed judge cases: `expected-failures.txt` holds 45 pure ids. |
| Next action | Plan 0005, tasks 1 to 4: the build, the 45 listed judge cases that wait for gate 4, the scanner as a merge check, and the interactive quickstart. Plan 0003, task 5: one mutation run, and the pruning rule. Gate 4 stays open for the maintainer. The gate log rows of layers 1 to 9 name each difference and each decision that the maintainer accepts or rejects there. |

## Queue counts

182 units. 105 have the status `reviewed`: the 8 units of the pilot, 6 units of layer 1 (`helpers`, `graph_queries`, the two query modules of `models`, `tracer`, `utils.text_utils`), `driver.falkordb` (its `__init__` holds only `STOPWORDS`), 7 units of layer 2 (`nodes`, `edges`, `driver.driver`, `driver.record_parsers`, `driver.operations.graph_utils`, `driver.operations.graph_ops`, `utils.ontology_utils.entity_types_utils`), 11 modules of the driver contract whose members have no logic (9 have abstract members only, and each member of `graph_operations` and `search_interface` raises `NotImplementedError`), the 10 prompt modules of layer 4, `llm_client.decision_prompts` of layer 3, `driver.neo4j.operations.search_ops` of layer 5, `search.search_config_recipes` and `search.search_helpers` of layer 6, 9 units of layer 7 (each module of `utils.maintenance` except `graph_data_operations`, and `utils.bulk_utils`), 3 units of layer 8 (`ingestion.change_set`, `ingestion.config`, and `ingestion.delta`), 18 package modules that need no file, and 27 units that layer 9 closed. The 27 are `graphiti`, `ingestion.phases`, `ingestion.scheduler`, `cross_encoder.client` (an abstract class with no executable line), and 23 more units with no pure case that the flow cases of the judge run. The queue note of each one gives the lines that the flow cases ran. 10 have the status `tests_pass`. Three have a listed pure case: `llm_client.prompt_config`, `search.search_config`, and `search.search_utils`. `utils.datetime_utils` has the status `reviewed` since plan 0005, task 2, because its last listed case passes. Seven have no pure case, and no flow case runs a line of them: `cross_encoder.openai_reranker_client`, `ingestion.sqlite_state_store`, `llm_client.cache`, `llm_client.errors`, `llm_client.llm_runtime`, `llm_client.openai_decisions_client`, and `llm_client.utils`. Their tests are their only evidence, and the maintainer reads them at gate 4. 67 have the status `todo`, and each one is out of the MVP. 114 are in MVP scope, and each of them has the status `reviewed` or `tests_pass`. `driver.falkordb` is the one unit with a status outside the MVP. See the scope table in [DECISIONS.md](DECISIONS.md).

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

Seven `TODO(port)` markers stay in ported units. Five are calls of a member that only the Neptune driver has, and that driver is out of the MVP (decision D10). Two are in `bulk_utils`: on FalkorDB and Neptune the entity-node bulk query is a list, which the port does not pass to `tx.run`. Each branch throws `NotSupportedException` at the call (rule R24). The maintainer accepts or rejects each one at gate 4. Layers 8 and 9 added no marker to a ported unit. Fourteen upstream tests of layer 7, four of layer 8 (`TestScheduler.cs`), and 20 of layer 9 keep a `TODO(port): O10` comment in the test projects, and the queue names them. The 20 of layer 9 are 18 tests of the ingestion files and 2 tests of `TestAddTriplet.cs`.

| File | Site | Member |
| --- | --- | --- |
| `src/Graphiti.Net/Core/Nodes.cs` | `CommunityNode.save` | `driver.save_to_aoss` |
| `src/Graphiti.Net/Core/Search/SearchUtils.cs` | `edge_fulltext_search`, the Neptune branch | `driver.run_aoss_query` |
| `src/Graphiti.Net/Core/Search/SearchUtils.cs` | `node_fulltext_search`, the Neptune branch | `driver.run_aoss_query` |
| `src/Graphiti.Net/Core/Search/SearchUtils.cs` | `episode_fulltext_search`, the Neptune branch | `driver.run_aoss_query` |
| `src/Graphiti.Net/Core/Search/SearchUtils.cs` | `community_fulltext_search`, the Neptune branch | `driver.run_aoss_query` |
| `src/Graphiti.Net/Core/Utils/BulkUtils.cs` | `add_nodes_and_edges_bulk_tx`, the entity-node save query | A list query of FalkorDB and Neptune |
| `src/Graphiti.Net/Core/Utils/BulkUtils.cs` | `update_entity_nodes_tx`, the entity-node update query | A list query of FalkorDB and Neptune |

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

[GAPS.md](GAPS.md) holds 22 language gaps, 15 surfaces that the judge cannot reach, and 24 upstream defects. [TYPE_HINTS.md](TYPE_HINTS.md) holds the 127 signatures that upstream leaves without hints.

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
