# 0004 Translate the core

Port phase 6. Starts after gate 3. 114 units, 32,596 Python lines.

## Shape

A stack of pull requests, built with `gh stack`. Each layer depends on the layers under it and passes the gate alone.
`port/queue.json` gives the exact order inside each layer. Port every module of a cycle group together.

| Layer | Branch | Upstream packages | Units | Lines | Implementer |
| --- | --- | --- | --- | --- | --- |
| 0 | `port/skeleton`, `port/skeleton-upper` | Done 2026-11-27. Types and signatures for all 114 units, with no bodies | 114 | | Opus 5.5, medium |
| 1 | `port/foundations` | Done 2026-11-27. `errors`, `helpers`, `graphiti_types`, `tracer`, `decorators`, `graph_queries`, `utils` (6 modules), `models`, `namespaces` | 20 | 4,277 | Sonnet 5.5, medium |
| 2 | `port/graph-types` | Done 2026-11-27. `nodes`, `edges`, and the driver contract: `driver.driver`, `driver.operations`, `driver.graph_operations`, `driver.search_interface`, `driver.query_executor`, `driver.record_parsers`, the `driver` package root | 21 | 5,147 | Opus 5.5, medium. One cycle group holds most of this layer. |
| 3 | `port/models-boundary` | Done 2026-11-27. `llm_client` (11 modules), `embedder`, `cross_encoder`, plus the new `Microsoft.Extensions.AI` adapter | 16 | 2,228 | Sonnet 5.5, high |
| 4 | `port/prompts` | Done 2026-11-27. `prompts` | 14 | 3,052 | Sonnet 5.5, medium |
| 5 | `port/neo4j` | Done 2026-11-27. `driver.neo4j`, `driver.neo4j_driver` | 14 | 3,032 | Sonnet 5.5, high. Opus 5.5 medium for `search_ops`. |
| 6 | `port/search` | Done 2026-11-27. `search` | 7 | 3,456 | Opus 5.5, medium |
| 7 | `port/maintenance` | Done 2026-11-27. `utils.maintenance` | 10 | 4,458 | Opus 5.5, medium |
| 8 | `port/ingestion` | Done 2026-11-28. `ingestion` | 9 | 4,159 | Opus 5.5, medium |
| 9 | `port/graphiti-core`, then `port/public-surface` | In progress. The core is done 2026-11-28: `graphiti` (2,782 lines), the package root, `migrations`, and the flow cases. The public .NET surface is the second pull request. | 3 | 2,787 | Opus 5.5, high |

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
| 5 | Done 2026-11-27. Save and load each node and edge type on Neo4j. Build indexes twice and get the same result. |
| 6 | Done 2026-11-27. Seed a graph, then run each search recipe and compare the ranked ids with the golden result. |
| 8 | Ingest episodes in parallel into one group, and find no lost update. This test moved to layer 9, because it needs the `Graphiti` object. |
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

Not in this pull request: the two skeleton packets for layers 7 to 9. They came after decision D49 (the section at the end of this file).

### Layer 1, the foundations, 2026-11-27

Five packets ported 11 modules. Two Opus 5.5 high reviewers read each packet, one against Python with its own inputs and one against the evidence with seeded defects. One Sonnet 5.5 implementer did each fix round.

| Packet | Modules | Implementer | Review findings |
| --- | --- | --- | --- |
| P1a | `helpers`, `utils.datetime_utils`, `utils.text_utils` | Sonnet 5.5, medium | 5 important, 16 minor. 22,550 calls of one reviewer matched Python. |
| P1b | `graph_queries`, `models.edges.edge_db_queries`, `models.nodes.node_db_queries` | Sonnet 5.5, medium | 5 important, 10 minor. The query text matched Python for each provider. |
| P1c | `tracer` | Sonnet 5.5, high | In the P1e row |
| P1d | `graphiti_types` | Sonnet 5.5, medium | In the P1e row |
| P1e | `namespaces.nodes`, `namespaces.edges` | Sonnet 5.5, medium | 7 important, 17 minor, for P1c, P1d, and P1e together |

What the reviews found, and the result:

| Finding | Result |
| --- | --- |
| 13 query constants had no test that can fail: 7 of 24 seeded defects passed each check. | The recording holds each module constant by name (rule R25). |
| A private helper of the query unit refused `bytes` labels and broke on a dict with other keys. | `Py.Iter` in `PyCompat` (rule R6). |
| The sum of `normalize_l2` was a private copy of numpy's pairwise sum. | `PyNumpy.AddReduce` in `PyCompat`, with 140 recorded numpy results (rule R18). |
| The binding of `parse_db_date` had a guard that hid a wrong result. | The function returns its input unchanged for another type, as Python does (rule R20). One case left open item O4. |
| A wrong `SEMAPHORE_LIMIT` failed no test, because the test compared the port with itself. | The recording holds the constant, and the test compares the largest count of calls that ran at the same time. |
| The telemetry of an exception has .NET names where Python has its own. | Accepted as decision D46. The maintainer decides at gate 4. |
| Four reachable namespace methods had no test. | The recording has 177 cases, from 55. |

Two units left the layer: `decorators` goes to layer 6, and `utils.ontology_utils.entity_types_utils` goes to layer 2 (see "Lead decisions").

Evidence that waits for a later layer:

- `graphiti_types`: no test reaches `complete_prompt`, `generate_prompt_response`, and two routes of `resolve_prompt_messages`. They stop at skeleton members of layers 3 and 4.
- `namespaces`: the recording reaches the entity node namespace. The others wait for the Neo4j operations of layer 5.
- `utils.text_utils`: 16 judge cases need the `EpisodicNode` model, which layer 2 registers.

One Opus 5.5 adversarial pass read the whole layer and gave 7 findings (2 important). The lead fixed each one:

- The binding of `utils.text_utils` did not register `MAX_SUMMARY_CHARS`, so the case `pure.prompts.const.max_summary_chars` stayed listed. It passes now.
- `port/STATE.md` named `Py.NotNone`, which comes with layer 2.
- The queue notes of `helpers` and `utils.datetime_utils` gave a wrong reason for 4 listed cases, and the queue did not show `driver.falkordb` as ported.
- R18 and G17 named a `np.sum` site that upstream does not have, and left out the float32 norm of `ingestion/delta.py:56`.
- `Py.Iter` gives a set in insertion order. R6 says so now.
- `OpenTelemetrySpan` wrote to a span that a sampler does not record, where the `NonRecordingSpan` of Python does nothing. Each member tests `IsAllDataRequested` now, and one new test fails for the seeded defect of the reviewer.

Commands at the tip:

- `scripts/check.sh`: `check: passed`.
- `dotnet test --project tests/Port.Evaluation`: 2,451 tests, 0 failed, 0 skipped.
- The recorders with `--check`: `helpers` 1,926 cases, the query builders 2,804, `namespaces` 177, `tracer` 900, and the numpy values. Each one is current.
- `python3 -B port/judge/run.py --build dotnet`: 1,999 cases, 681 passed, 0 unexpected, 0 bound and wrong. 278 ids left `expected-failures.txt`, and 1,318 stay. The pull request lists the ids.

### Layer 2, the graph types and the driver contract, 2026-11-27

Three packets ported 6 modules, and `utils.ontology_utils.entity_types_utils` came from layer 1. Two Opus 5.5 high reviewers read each packet. One Sonnet 5.5 implementer did each fix round.

| Packet | Modules | Review findings |
| --- | --- | --- |
| P2a | `nodes` | 9 and 14 findings. 9 of the seeded defects of one reviewer passed each test before the fix round. |
| P2b | `edges` | 6 and 12 findings. 13 of 34 seeded defects passed each test before the fix round. |
| P2c | `driver.driver`, `driver.record_parsers`, `driver.operations.graph_utils`, `driver.operations.graph_ops` | 5 and 11 findings. 16 of 38 seeded defects passed each test before the fix round, and 1 made the test run hang. |

What the reviews found, and the result:

| Finding | Result |
| --- | --- |
| After a length error of `zip(strict=True)`, the port assigned no embedding. Python assigns each pair first. | `Py.ZipStrict` in `PyCompat` (Part 3 of the rulebook). |
| A bad label together with another field error gave one pydantic error, not two. | The validator of an abstract base model has a static form that the field reader calls (rule R7). |
| `EpisodeType.from_str` and `parse_db_date` threw for a record value of another type, where Python gives a result. | Both take `object?` (rules R20 and R22). Four O4 cases pass now. |
| A method call on a record value of a wrong type threw `TypeError`, where Python raises `AttributeError`. | `Py.Receiver<T>` in `PyCompat` (rule R4). GAPS F24 names what stays different. |
| The `else` branch of `try`/`except`/`else` used the result as its flag, so an interface that returns `None` ran the query. | A filtered catch, `catch (A) when (!returned)` (Part 3). GAPS F25 names what stays different. |
| `GraphDriver.transaction` closed its session with a canceled token, so a canceled body left the session open. | `CancellationToken.None` in the `finally` (rule R8), with a test that fails for the old form. |
| A fake that completes at once cannot show a missing `await`, and no test passed a canceled token. | Each fake yields first, and each unit has a canceled-token theory (rule R25). |
| A wrong tie order in label propagation made the test run hang, because of upstream defect U11. | Each case has a time limit of 5 seconds. |
| The recorders left out defaults, a limit of 0, an empty group id, equality, hashing, and the order of log lines and calls. | The recordings have 1,155 (`nodes`), 1,140 (`edges`), and 1,128 (driver contract) cases. |
| Two pilot tests read each JSON integer as a float. | Fixed. A seeded reader that rejects an int now fails 4 tests. |

Differences that the lead accepted by name under D39: GAPS F24, GAPS F25, the R7 row for `load_fact_embedding`, and `model_copy(update=...)` on a `validate_assignment` model. No upstream site reaches them with Neo4j.

The adversarial pass over the layer (Opus 5.5, high effort) gave 3 important findings and 3 small ones. The lead fixed each one:

- `get_entity_node_from_record` read `labels` before it ran `group_id.replace`. The port now keeps the order of Python, and the recording holds three records with two wrong fields.
- `Py.Int` accepted bytes with a byte that is whitespace only in text, such as `0xA0`. It now reads bytes as Python does and gives the bytes repr in the error.
- The text of GAPS F24 named fewer differences than the tests exempt. It now names each one: a list as `attributes`, a value that is not a list as `labels`, and a dict with keys that are not text.
- The gate log, an O4 row, a count in `port/STATE.md`, and a sentence of rule R8 were stale. One fake did not yield.

Evidence that waits for a later layer:

- `driver.driver`: no test reads the four index names with their environment variables set. The port has no seam for them.
- `utils.ontology_utils.entity_types_utils` and `helpers.validate_excluded_entity_types`: 12 cases that pass a built-in model class (`ExtractedEntities` or `EntityNode`) as a custom type stay listed under open item O4.
- `Py.TimeSeconds` reads the system clock, so no test compares the time text of the debug line of `generate_embedding`.
- The Neo4j branch of each `save` and `get` method runs on a database first in layer 5.

Commands at the tip:

- `scripts/check.sh`: `check: passed`.
- `dotnet test --project tests/Port.Evaluation`: 5,889 tests, 0 failed, 0 skipped.
- The recorders with `--check`: each of the eight is current.
- `python3 -B port/judge/run.py --build dotnet`: 1,999 cases, 957 passed, 0 unexpected, 0 bound and wrong. 276 ids left `expected-failures.txt`, and 1,042 stay. The pull request lists the ids.

Found beside the plan: `scripts/mutate.sh` gave no result on 2026-11-26. Two tests, `PyAstTests.LiteralEval_matches_Python` and `PyAstTests.LiteralEval_of_long_and_deeply_nested_texts_matches_Python`, stop the test host in a mutated build. Stryker then runs them against each mutant, and each mutant gets the status `RuntimeError`. A probe on `PyHash.cs` with 56 mutants took 159 seconds and gave 56 errors. With the two tests skipped it took 8 seconds and killed 56. The probable cause is a stack overflow in the mutated parser. The lead fixes the two tests before the mutation run of layer 5.

### Layer 4, the prompts, 2026-11-27

Four packets ported 10 modules. Two Opus 5.5 high reviewers read each packet group. One Sonnet 5.5 implementer did each fix round.

| Packet | Modules | Review |
| --- | --- | --- |
| P4a | `prompts.extract_nodes`, `prompts.snippets` | One review pair for P4a, P4b, and P4c. The lead made 13 decisions from it. |
| P4b | `prompts.extract_edges`, `prompts.dedupe_edges`, `prompts.extract_nodes_and_edges` | The same pair. |
| P4c | `prompts.eval`, `prompts.summarize_nodes`, `prompts.summarize_sagas`, `prompts.names` | The same pair. |
| P4d | `prompts.lib` | 4 and 12 findings. 15 of 29 seeded defects passed each test before the fix round, and each one fails a test now. |

What the reviews changed:

| Finding | Result |
| --- | --- |
| A binding registered only the paths that its own cases named. | Rule R22: the unit of the module that defines a name registers each path of that module that a judge case names. |
| Five models had a `ResponseModel` member that upstream never passes as a response model. | Removed. |
| The two section builders of `extract_nodes_and_edges` had typed parameters and a private body for open data. | Each one takes `object?` (rule R20). |
| No recording called a prompt function by its Python name, and none built a model from its required fields only. | Each recorder does both now. |
| `prompts.lib`: a null override, an override that returns null, two faults in one call, and object identity had no case. | The recording has 1,000 cases, with each of them. |
| `prompts.lib`: each read of a legacy prompt function gave a new delegate. | One delegate for each wrapper (rule R17). |
| `prompts.lib`: a specs entry that is null threw `NullReferenceException`. | It gives the `ValueError` text of Python. |

The lead wrote `tests/Graphiti.Net.JudgeHost/Units/Types.cs`, the decoder for `types:SimpleNamespace`.

Differences that the lead accepted by name under D39: a typed override cannot return a list or a text, and a delegate of another type counts as not callable (rule R15).

The adversarial pass over the layer (Opus 5.5, high effort) compared 1,200 prompt texts with Python and found no difference in behavior. It gave 2 important findings and 4 small ones, each about a record or a name, and the lead fixed each one: a stale queue note, an O4 row that listed three closed cases as open, a wrong count, a change log row with no third cell, a parameter name, and a stale sentence about O12.

Evidence that waits for a later layer:

- `jev_prompt_overrides` and `openai_decisions_prompt_overrides` read this library first in layer 3.
- The tests of `LLMRuntime` and of `prompt_config` that need the prompt library come with layer 3.

Commands at the tip:

- `scripts/check.sh`: `check: passed`.
- `dotnet test --project tests/Port.Evaluation`: 10,233 tests, 0 failed, 0 skipped.
- The four prompt recorders with `--check`: 1,031, 1,532, 734, and 1,000 cases. Each one is current.
- `python3 -B port/judge/run.py --build dotnet`: 1,999 cases, 1,150 passed, 0 unexpected, 0 bound and wrong. 193 ids left `expected-failures.txt`, and 849 stay. The pull request lists the ids.

### Layer 3, the model boundary, 2026-11-27

Seven packets ported 13 modules and wrote the `Microsoft.Extensions.AI` adapters. Two Opus 5.5 high reviewers read each packet group. One Sonnet 5.5 implementer did each fix round.

| Packet | Modules | Review |
| --- | --- | --- |
| P3a | `llm_client.config`, `llm_client.errors`, `llm_client.utils`, `llm_client.token_tracker` | One review pair for P3a, P3b, and P3e: 7 and 18 findings. 23 of 58 seeded defects passed each test before the fix round. |
| P3b | `llm_client.client`, `embedder.client`, `cross_encoder.client` | The same pair. |
| P3e | `llm_client.cache`, `cross_encoder.openai_reranker_client` | The same pair. |
| P3c | `llm_client.prompt_config`, `llm_client.llm_runtime` | One review pair for P3c and P3d: 13 and 11 findings. 18 of 68 seeded defects passed each test before the fix round. |
| P3d | `llm_client.decision_prompts`, `llm_client.openai_decisions_client` | The same pair. |
| P3f | The adapters `ChatClientLlmClient`, `EmbeddingGeneratorEmbedder`, `CrossEncoderBridge`, and `ChatClientLogProbabilityChat` (Opus 5.5, medium effort) | No review pair. The adversarial pass reads them. |
| P3g | The evidence of `prompt_config` and `llm_runtime` that needs the prompt library of layer 4 | No review pair. 15 of 16 seeded defects fail a test. |

What the reviews changed:

| Finding | Result |
| --- | --- |
| The default HTTP client of the decisions client followed a redirect and had a timeout of 100 seconds. httpx does neither. | `new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })` with no timeout of its own (rule R19). |
| The cache had no busy timeout, so two processes on one cache file failed at once. | A busy timeout of 5 seconds, as the `sqlite3` module of Python has. |
| The path of `generate_response` with the cache on had no test. | The recording has three scenarios of the cache path, with 10 calls. |
| A cancel during a retried failure left as the provider error. | It leaves as `OperationCanceledException`, and a test holds it. |
| A reply that is not UTF-8, a byte order mark, and a charset header were read in another way. | `response.json()` reads the bytes as strict UTF-8. GAPS F34 names what stays different. |
| `int(x)` of open data, a method call on a value of another type, and `x in c` on open data threw other exceptions. | `Py.Int`, `Py.Receiver`, and four private members of `DecisionPrompts` (rule R6). |
| `parse_list` returned a typed list, so JSON text that is not a list could not pass. | It returns `object?` (rule R20). One O4 case is closed. |
| Five tests of the runtime had upstream names and other assertions, and a helper built a runtime with no constructor call. | Each one is a port of its upstream test, and each test calls the constructor. |
| The runtime had no recording. | `port/tools/record_llm_runtime.py` holds 553 cases, with 60 sets of calls that run at the same time. |

Decision D48: two outputs above debug level keep the text of a provider exception, as upstream does.

The adapters follow [the public surface design](../../design-docs/public-surface-design.md). The design left five points open, and the lead chose one reading for each. The maintainer reads them at gate 4:

- The adapter sends a temperature only if the option `Llm.Temperature` has a value. It is a `double?` parameter of the adapter, apart from `LLMConfig.Temperature`.
- The classifier of the host runs first. If it is absent or returns `None`, the default classifier decides. The chat adapter and the reranker follow the same order.
- The test for a filtered reply comes before the test for an empty reply. A filtered reply with no text gives `RefusalException`.
- The usage of a reply is recorded before the test that the JSON is an object, as upstream does.
- The embedding adapter has one constructor for `Embedding<float>` and one for `Embedding<double>`. `ModelCallInfo.Model` is null for an embed call and for a rerank call, because neither has a route. The reranker model goes to `ChatOptions.ModelId`.

Differences that the lead accepted by name under D39: GAPS F34, F35, and F36, the R4 row for a base URL with no `http` scheme, and one seeded defect of packet P3g that no test can see. With the defect, `LLMRuntime.set_tracer` sets the tracer of a shared client two times, and the second call changes nothing.

The adversarial pass over the layer (Opus 5.5, high effort) said that each of the five readings is a fair reading of the design. It gave 1 important finding and 5 small ones, and the lead fixed each one:

- The reranker used the classifier of the host with no default behind it, so a host classifier that returns `None` for status 429 gave `HttpRequestException` there and `RateLimitException` in the chat adapter. Both paths now use the host classifier first and then the default.
- A rerank call set `ModelCallInfo.Model` to the reranker model, against the design text. It is null now.
- `edge_e2` gave another `TypeError` text than Python for a list, a tuple, or a text plus a value of another type. It gives the Python text now, and eight literal cases from Python hold it.
- The adapters had no test of the token rule, and a dropped token passed each test. Each fake host now records if the cancel of the caller reaches its token.
- One comment named the wrong upstream client, and rule R4 had one sentence two times.

Evidence that waits for a later layer:

- Two tests of `test_jev_client.py` and one of `test_openai_decisions_client.py` wait for the unit `jev_client`, which is out of the MVP.
- `tests/test_graphiti_configurable_prompts.py` waits for the `Graphiti` class of layer 9.
- No test compares the time text of the debug line of `generate_embedding`, because `Py.TimeSeconds` reads the system clock.
- No test reads the usage record of a reply that is not a JSON object through the adapter.

Commands at the tip:

- `scripts/check.sh --test`: `check: passed`, 10,855 tests.
- `dotnet test --project tests/Port.Evaluation`: 10,462 tests, 0 failed, 0 skipped.
- The seven recorders of the layer with `--check`: 189, 931, 159, 1,193, 553, and 1,213 cases, and the 2 fixture texts. Each one is current.
- `python3 -B port/judge/run.py --build dotnet`: 1,999 cases, 1,206 passed, 0 unexpected, 0 bound and wrong. 56 ids left `expected-failures.txt`, and 793 stay. The pull request lists the ids.

### Layer 5, the Neo4j driver and its operations, 2026-11-27

Five packets ported 11 modules, packet P5f wrote the end-to-end test, and two small packets added evidence for earlier units. Two Opus 5.5 high reviewers read each packet group. One Sonnet 5.5 implementer did each fix round.

| Packet | Modules | Review |
| --- | --- | --- |
| P5a | `driver.neo4j_driver` | One review pair for P5a and P5e: 12 and 14 findings. 20 of 29 seeded defects in the driver passed each test before the fix round. |
| P5e | `driver.neo4j.operations.search_ops` | The same pair. 8 of 26 seeded defects passed each test before the fix round. |
| P5b | `episode_node_ops`, `community_node_ops`, `saga_node_ops` | One review pair for P5b, P5c, and P5d: 3 and 7 findings. 9 of 60 seeded defects passed each test before the fix round. |
| P5c | `entity_edge_ops`, `episodic_edge_ops`, `community_edge_ops`, `has_episode_edge_ops`, `next_episode_edge_ops` | The same pair. |
| P5d | `driver.neo4j.operations.graph_ops` | The same pair. |
| P5f | The end-to-end test of the graph types, and `scripts/db_schema.py` | No review pair. The adversarial pass reads it. |
| Evidence | More recorded calls for two earlier units. `entity_node_ops`: 267 cases for each of its 10 methods, where the pilot had 5 methods. `namespaces.nodes` and `namespaces.edges`: 643 cases for each of the 75 public methods of the 9 namespace classes. | No review pair. Each of 11 seeded defects fails a test, and no case showed a difference. |

Each seeded defect that passed fails a test now.

What the reviews changed:

| Finding | Result |
| --- | --- |
| A text parameter with a lone surrogate was stored as U+FFFD. Python raises `UnicodeEncodeError`. | The conversion at the driver boundary runs the strict UTF-8 check on each text, at each depth (rule R9). |
| The close of a session threw the error of a result that nobody read. The Python close drops it. | `CloseAsync` and `DisposeAsync` drop it. GAPS F28 names what stays different. |
| A canceled token did not stop a write through a session or a transaction. | Each driver call checks the token first (rule R8), and a theory passes a canceled token to each method. |
| The index test passed with no index build, because the shared database had the indexes. | A fake of the `Neo4j.Driver` package records each query, and the test compares the list with Python. |
| The driver tests compared the port with itself. | Each expected text is a Python literal, and the life cycle of a transaction is compared call by call. |
| The text of a driver error in a log line was not the `str(e)` of Python. | One private member builds the Python text (rule R4). |
| `get_community_clusters(None)` read group ids from the database into a typed list. | The value stays open data (rule R6). |
| No test saw the concurrency limit of `build_indices_and_constraints`. | The fake counts the calls in flight, and the test compares the largest count, which is 20. |
| A cancel at a later awaited call had no case. | One case for each later call: the fake cancels at call k, and no call follows. |
| The recordings left out a default `saga`, records with two defects, seven inputs of the rerankers, and 20 items with equal scores. | Each recorder has them. |

The end-to-end test of the plan row, `GraphTypesOnNeo4jTests`:

- It saves and loads each node type and each edge type on Neo4j, through the model classes and through the 9 operations classes. Python recorded the expected values on the same database.
- It found no difference between the port and Python in a stored form, a loaded value, a type, an offset, an embedding, or an error code.
- It builds the indexes two times and reads `SHOW INDEXES` after each build. The two lists are equal, and each of the 31 indexes of Python is in the list.
- It does not call four methods of the operations classes: `get_between_nodes`, `get_by_node_uuid`, `get_by_entity_node_uuid`, and `retrieve_episodes`. The recorded calls with a fake cover them.
- It holds upstream defects U1, U2, and U7 as they are: the batched deletes fail, the saga save through the driver fails, and a save with no embedding fails.

`scripts/db_schema.py` writes [db-schema.md](../../generated/db-schema.md) from the ported query definitions.

Differences that the lead accepted by name under D39: GAPS F26, F27, F28, F29, F30, F37, and F38. The maintainer reads F28 at gate 4: a bare `session.run` with a statement that fails at once raises in Python and is silent in the port. Three upstream sites call `session.run`, each with a fixed statement.

Open item O7 is closed. The driver has the first library callback, and a test fails without `Asyncio.OnLoopAsync`.

The adversarial pass over the layer (Opus 5.5, high effort) ran each command of this section and got the same counts. It gave 1 important finding and 8 small ones. The lead resolved each one:

- Inside a transaction or `execute_write`, a statement that fails at once raises at `tx.run` in Python. In the port the rest of the body runs, and the error arrives at the commit. `Neo4j.Driver` has no call that waits for the answer to the statement alone, so the lead accepted the difference as GAPS F37. The stored data is the same. The maintainer reads it at gate 4.
- A cancel at the end of a transaction body: GAPS F38.
- A port that sorts the keys of a map from Neo4j passed each test. One test on Neo4j now compares the key order with Python.
- The schema document left out three stored forms of the save methods of the operations classes. It has a section for them now.
- On a database that already has the indexes, the two index tests on Neo4j pass with no index build. The test with a fake holds the build, and so does a run on an empty database, as in CI.
- Four records were wrong or stale: two queue notes, the count of `session.run` sites, a line of `port/STATE.md`, and a count in rule R3.

After the rebase onto layer 3, the full gate failed two health check tests in each run. The cause: two test classes replaced `Console.Out`, which is one object for the process, and they ran at the same time. Both classes are in one test collection now, so they run one after the other.

Evidence that waits for a later layer:

- No flow case of the judge runs against the .NET build. The 10 units with no pure case keep the status `tests_pass` until layer 9.
- The queries of `search_ops` run on Neo4j first in the end-to-end test of layer 6.
- No test holds the hang of upstream defect U11. A check on a background thread stops a recorded case that does not end.
- No mutation run gave a result. The fix of the two `PyAst` tests is on the branch `test/pyast-large-stack`, and it waits for the maintainer.

Commands at the tip:

- `scripts/check.sh --test`: `check: passed`, 13,492 tests.
- `dotnet test --project tests/Port.Evaluation`: 13,054 tests, 0 failed, 0 skipped.
- `dotnet test --project tests/Graphiti.Net.E2E.Tests` on Neo4j 5.26.2: 46 tests, 0 failed, 0 skipped.
- The eight recorders of the layer with `--check`, each one current: 267 cases (`entity_node_ops`), 587 (the other node operations), 617 (the edge operations), 126 (`graph_ops`), 305 (`search_ops`), 643 (the namespaces), the index queries of the driver, and the end-to-end values of the graph types on Neo4j. `python3 scripts/db_schema.py --check` says that the schema document is current.
- `python3 -B port/judge/run.py --build dotnet`: 1,999 cases, 1,229 passed, 0 unexpected, 0 bound and wrong. 23 ids left `expected-failures.txt`, and 770 stay. The pull request lists the ids.

### Layer 6, search, 2026-11-27

Three packets ported 6 modules, and one packet wrote the end-to-end test. Two Opus 5.5 high reviewers read each packet group. One Sonnet 5.5 implementer did each fix round.

| Packet | Modules | Review |
| --- | --- | --- |
| P6a | `search.search_config`, `search.search_config_recipes`, `search.search_helpers`, `decorators` | One review pair for P6a and P6b: 3 and 7 findings. 10 of 58 seeded defects passed each test before the fix round, and 1 made the test run hang. |
| P6b | `search.search_utils` | The same pair. |
| P6c | `search.search` | 2 and 7 findings. 21 of 34 seeded defects passed each test before the fix round. |
| E6 | The end-to-end test of the search recipes on Neo4j | No review pair. The adversarial pass reads it. |

Reviewer A of each pair found no difference in behavior that needs a code change: no product line of the six modules changed after the reviews. Each seeded defect that passed fails a test now.

What the reviews changed:

| Finding | Result |
| --- | --- |
| The canceled-token theory proved only that some call reads the token. A token that one call dropped passed each test. | Each fake records the token that it gets, and the test compares each one with the token of the caller (rule R25). |
| No test saw a concurrency limit. A port that runs the searches one after the other passed. | The fakes yield first on both sides and count the calls in flight. The recordings have cases with more calls than the limit. |
| A call that blocks the scheduler thread made the whole test run hang, also with a time limit on the test. | A loop guard ends the test host with a message (rule R25). |
| The filters of a reranker had no input on the limit and none below it. Four configuration values had only their default. | The recording of `search` had 204 cases after the fix round, where it had 117. |
| No test read the spans of a canceled search. | The recording held 44 cancel points with the exception and the span list of Python. The adversarial pass replaced them with a full grid (below). |
| The 19 judge cases of `fulltext_query` had no binding for a driver. | The judge host binds a `types:SimpleNamespace` as a driver. Each of the 19 passes. |

The end-to-end test of the plan row, `SearchRecipesOnNeo4jTests`:

- Python seeds one graph on Neo4j and runs the upstream `search` for each of the 16 recipes and 3 queries. The graph has 23 entities, 18 facts, 8 episodes, and 5 communities. The port seeds the same graph in its own group and runs the ported `search`.
- Each ranked list of the port equals the list of Python, in order, and each reranker score agrees within 1e-6. The test found no difference.
- The embedder and the cross-encoder are fakes at the network boundary. Each one is a pure function of its text, so both sides compute the same values.
- A second group holds nodes and edges with the same query words. A result from that group fails the test. This holds the group filter of the similarity queries only. The Lucene group clause still filters a full-text hit when its Cypher filter is absent, and no edge joins the two groups, so a breadth-first search never reaches the second group. The unit recordings hold the Cypher group filter of each full-text search and the origin filter of each breadth-first search: a seed that removes one fails 23 to 28 recorded cases.
- Three seeded defects fail it: a sort direction, the group filter of one query, and a limit factor.
- It does not reach these paths: the Neptune and FalkorDB branches, a search interface of the driver, an empty query, a `query_vector` of the caller, a `reranker_min_score` above 0, and a full-text query that is too long. The recordings with a fake driver cover them.

Differences that the lead accepted by name under D39: GAPS F31, F32, and F33. The maintainer reads F33 at gate 4. A cancel that arrives while a driver call or a cross-encoder call returns is seen at once in Python, and at the next call in the port. The recorder builds a grid of 153 cancel points with a loop: 114 at a driver query, 24 at an embedder call, and 15 at a cross-encoder call. At 73 points the port differs, and the list `LateCancel` of `SearchValuesTests.cs` names each one with its kind. At 51 points the span events have another order. At 18 points the port returns the full result. At 2 points it has more span events, and at 2 points it throws the error that comes first. A listed point that agrees with Python fails the test. Each of the 24 embedder points agrees.

The adversarial pass (Opus 5.5, high) read the layer diff and ran 33 seeds. It found one important gap and five minor ones, and the lead resolved each one:

- Three `min_score` filters of rrf in `search` had no test that reads them: the edge cross-encoder and the two node distance seeds. The recording has six new configurations, with a candidate below the minimum and one on it. Each of the three seeds now fails a test.
- Three test files entered `Asyncio.Run` with no loop guard. `LoopGuard` is its own file, the E2E project links it, and each such call goes through it. With a blocking call in `search`, a run of one test class ends after 6 seconds with the guard message.
- The sentence about the second group of the end-to-end test said more than the test holds. It is corrected above.
- The counts of GAPS F33 came from a list that no file held. The recorder now builds the grid, and the test holds the list of the points that differ.
- Two upstream tests of the cross-encoder shortlist and one of the group id check had no port. They are ported. The two search tests of `tests/test_neptune_search_isolation.py` call the Neptune branches that hold a `TODO(port)` marker (rule R24), and the queue names them.
- The binding of `fulltext_query` set a default provider and raised the `AttributeError` of Python itself. It now sets only the attributes that the namespace has.

Five `TODO(port)` markers stay, and `port/STATE.md` lists them. Four are new: the Neptune branch of each full-text search calls `driver.run_aoss_query`, which only the Neptune driver has (rule R24).

Evidence that waits for a later layer:

- No flow case of the judge runs against the .NET build. `search.search` and `decorators` have no pure case and keep the status `tests_pass` until layer 9.
- No test compares the time text of the debug line of `search`, because `Py.TimeSeconds` reads the system clock.
- `Asyncio.Run` does not cancel the tasks that a failed call leaves, where `asyncio.run` does. The test of a recorded call that fails gives the scheduler 50 turns before it reads the events.
- No judge case passes a driver namespace that lacks an attribute after the group ids pass. A typed driver cannot raise the `AttributeError` of Python for it, and the binding does not raise it for the port.
- Three covering cases stay listed under open item O4: one of `search_config` and two of `rrf`.

Commands at the tip:

- `scripts/check.sh --test`: `check: passed`, 15,775 tests.
- `dotnet test --project tests/Port.Evaluation`: 15,320 tests, 0 failed, 0 skipped.
- `dotnet test --project tests/Graphiti.Net.E2E.Tests` on Neo4j 5.26.2: 63 tests, 0 failed, 0 skipped.
- The five recorders of the layer with `--check`, each one current: 510 cases (the configuration, the recipes, and the helpers), 1,322 (`search_utils`), 432 (the decorator), 172 (`search`) with the 153 points of the cancel grid, and the end-to-end values of the recipes on Neo4j.
- `python3 -B port/judge/run.py --build dotnet`: 1,999 cases, 1,375 passed, 0 unexpected, 0 bound and wrong. 146 ids left `expected-failures.txt`, and 624 stay. The pull request lists the ids.

### Layer 0, the skeleton for layers 7 to 9, 2026-11-27

Decision D49 extended the run through layer 9. Two packets declared the types and the signatures of the modules of layers 7 to 9. One Opus 5.5 high reviewer read each packet and tried to refute it.

| Packet | Modules | Review findings |
| --- | --- | --- |
| S6 | `utils.maintenance` (9 modules) and `utils.bulk_utils` | 6 important, 2 minor |
| S7 | `ingestion` (8 modules) and `graphiti` | 1 important, 8 minor |

What the lead and the reviews changed:

- The two packets typed the same Python unions in two ways. `str | SagaNode | None` is the record `SagaOrName`, and `EpisodicNode | list[EpisodicNode]` is the record `EpisodeOrEpisodes`, at each site. Part 3 of the rulebook now gives the name and the place of such a record.
- Five of the six important findings of S6 had one cause: the typed read of the judge host had no form for five parameter types. About 70 judge cases did not reach their function. The host now reads a `ulong`, a value tuple, a dict with other keys, a read-only collection, and `None` for a nullable list item.
- Two members of `delta` that judge cases call were private. They are internal, and so is `_get_or_create_saga`, which an upstream test calls.
- The keyword dict `commit_kwargs` of the scheduler is three typed parameters.
- `_get_provider_type` has no member. Its one caller is the telemetry that decision D9 leaves out.
- The module class of `combined_extraction` has the suffix `Module`, because the module imports a model with its name.
- Decision D50: where upstream keeps `type(error).__name__` as diagnostic text, the port keeps the .NET type name. The maintainer reads it at gate 4.
- The rulebook has rows of R2, R14, R15, R16, R18, R22, and Part 3 for each construct that had a missing-rule marker. Open item O4 has 2 more cases.

The lead left one finding for the units of layer 7: seven judge cases pass a built-in prompt model as a custom type, and the registry gives a response model for it.

Three package modules of these layers only import names again, and `migrations` is empty. They need no file (rule R1), and the queue has them as `reviewed`. The queue has the target of each other module. 65 units are `reviewed`, 31 are `tests_pass`, and 86 are `todo`.

Commands at the tip, after the rebase on `main`:

- `scripts/check.sh --test`: `check: passed`, 15,776 tests.
- `python3 -B port/judge/run.py --build dotnet`: 1,999 cases, 1,375 passed, 624 expected failures, 0 unexpected, 0 fixed, 0 bound and wrong.
- `scan_port.py src --allow-todo`: 606 `TODO(port)` markers in 144 files. The other 176 findings are `NotImplementedException` sites that rules R4 and R24 give.

### Layer 7, maintenance, 2026-11-27

Seven packets ported 10 modules and built the custom type. Two Opus 5.5 high reviewers read each packet except P7g. One implementer did each fix round: Sonnet 5.5 for five packets, and Opus 5.5 for P7f.

| Packet | Modules | Review |
| --- | --- | --- |
| P7a | `dedup_helpers`, `attribute_utils`, `temporal_edge_utils`, `dangling_endpoints`, `graph_data_operations` | 4 and 9 findings. 10 of 72 seeded defects passed each test before the fix round. |
| P7b | `node_operations` | 3 and 6 findings. 9 of 62 seeded defects passed. |
| P7c | `edge_operations` | 3 and 7 findings. 6 of 73 seeded defects passed. |
| P7d | `combined_extraction`, `community_operations` | 6 and 9 findings. 22 of 78 seeded defects passed. |
| P7e | `utils.bulk_utils` | 5 and 10 findings. 18 of 69 seeded defects passed. |
| P7f | The custom type descriptor and the public `GraphTypeDefinition` (debt T18) | 6 and 5 findings. 14 of 55 seeded defects passed. |
| P7g | The write with a transaction timeout (open item O6) | No review pair. The builder ran 4 seeds, and the adversarial pass reads it. |

Each seeded defect that passed fails a test now, except the ones that a reviewer showed to change nothing.

Differences in the product code that the reviews found, and their fix:

| Finding | Result |
| --- | --- |
| The aggregate cap of `attribute_utils` multiplied in 64 bits, where a Python int has no limit. | The product and its comparison run in `Int128` (rule R4). |
| `filter_existing_duplicate_of_edges` skipped a record value that is a list or a dict, where Python raises `TypeError`. | It throws `InvalidCastException`, as rule R4 says. |
| `write_timeout` of `add_nodes_and_edges_bulk` did not stop the work. The work function kept the token of the caller, and one test asserted that. | The work function takes the token of the limit (rule R8). The recording holds the sequence of Python: a rollback, a close, and `TimeoutError`. |
| `dedupe_nodes_bulk` made three calls before it failed on a short episode list, where Python fails with zero calls. | It reads each episode tuple before the gather (rule R8). |
| `UnionFind.find` had no depth limit. | A stack guard throws before the stack ends. The depth still differs from Python (GAPS F43). |
| The schema of a custom type skipped `inspect.cleandoc`, had no `maxLength`, and gave a wrong title for a field name that is not ASCII or that ends in `_`. | `PyText.Cleandoc`, `PyText.Title`, and a `maxLength` member of the schema builder, each with values recorded from Python. |
| Two `!` operators in `combined_extraction`, which rule R24 forbids. | `Py.NotNone`. `scripts/check.sh` does not find the operator (debt T28). |

What the reviews changed in the evidence:

- Each recording calls each function one time with its defaults. Each value that the code reads from the first item of a list has a case where the items differ. Each numeric limit has an input on it.
- The theory that cancels at call k now compares with Python: the recorder runs the same cancel on Python and records the attempts and the log events. A port that catches a cancel and writes a warning passed each test of four packets before this change.
- A recorder subclass of a model takes the name of its base class. Before that, a recorded pydantic error had the title of the subclass, and the tests left the message out of the comparison.
- `label_propagation` runs on a thread with a time limit in its test. A seeded tie rule made the test run hang.
- 23 more upstream tests are ported: 5 of `remove_communities`, 6 of the configurable prompts of the community units, and 12 that the first version marked for open item O10.

Decisions of the lead under D49, for the maintainer at gate 4:

- D52 closes open item O6. `GraphDriverSession` has `ExecuteWriteAsync(func, double timeout, cancellationToken)`. The rulebook gave the item to the maintainer. The contract is internal, and each commit of an episode on Neo4j needs the member.
- Open item O10 has a reading (Part 3 of the rulebook). A test that a fake at the network boundary can serve is ported. 12 tests of the layer keep a `TODO(port): O10` marker, each with the recorded case that covers the same behavior. `port/queue.json` names each one.
- The public builder has six forms that the design does not give. [The public surface design](../../design-docs/public-surface-design.md) lists them at the end of the section "Custom entity types and edge types".
- `.editorconfig` turns the analyzer rule CA1720 off for `GraphFieldKind.cs` only. The design names the members `String` and `Integer`.

Differences that the lead accepted by name: GAPS F41 to F46, and 80 more cancel points of GAPS F33 (6 in `node_operations`, 1 in `combined_extraction`, 7 in `community_operations`, 55 in `edge_operations`, and 11 in `bulk_utils`). One point of `bulk_utils` makes two more attempts than Python. The review of `community_operations` also found upstream defect U20: a `null` summary of the model becomes the text `None` in the next prompt.

The write with a timeout on the real Neo4j, `WriteTimeoutOnNeo4jTests` (23 tests):

- A write inside the limit commits, and the test reads the node back.
- A write that waits for a lock past its limit fails. The server ends the transaction, so the result does not depend on the speed of the machine. Both drivers give `Neo.ClientError.Transaction.LockClientStopped` after one attempt, and no node of the group stays.
- The Python driver 6.1.0 sends whole milliseconds and rounds half to even. `Neo4j.Driver` 6.5.1 rounds up. `Neo4jSession` converts the limit the Python way, so the value on the wire is the same.

Two `TODO(port)` markers are new, and `port/STATE.md` lists them: on FalkorDB and Neptune the entity-node bulk query is a list (rule R24).

Evidence that waits for a later layer:

- The trace paths of `node_operations`, `edge_operations`, and `bulk_utils` are recorded with `trace=None`. Packet P8b of layer 8 records them with a `ResolutionTrace`.
- `graph_data_operations` has no pure case and keeps the status `tests_pass` until layer 9.
- Four covering cases stay listed under open item O4: two of `lsh_bands` and two wrong-type cases of `bulk_utils`.
- The three built-in models that the judge host registers as custom types have descriptors that are written by hand. No case reads their schema or their validation.
- Inputs of `ModelValidate` that are not JSON values, for example a `datetime` from a stored attribute, have no recorded case.

The adversarial pass (Opus 5.5, high) read the layer diff and ran 2 seeds. It found four important gaps and five minor ones, and the lead resolved each one:

- The cancel theory of `combined_extraction` failed in 8 of 30 runs on unchanged code. Its fake canceled with `CancelAsync`, which runs the callbacks of the token on the thread pool. Each fake now calls `Cancel()`, and the class passed 30 of 30 runs. Rule R25 has the form, and four test files of layers 2, 5, and 7 changed with it.
- The cancel theories of `edge_operations` and `bulk_utils` did not compare with Python. Both recorders now run the same cancel on Python. A seeded port that logs a cancel now fails 13 tests. The comparison found 66 more points of GAPS F33.
- Two upstream tests of `edge_operations` kept an O10 marker and have a form under the reading. They are ported.
- The layer diff changed a file of `tests/Graphiti.Net.JudgeHost/Core/`. Pull request 44 merged that change alone, as `REVIEW.md` says.
- The public documentation of `GraphTypeDefinition` gave the old equality, and one rule still named `Py.Round`. Both texts are corrected.
- The queue did not name the tests that keep an O10 marker. It names each one now, with its recorded case.
- Two sentences of the gate log row were wrong: the count of the builder forms, and the claim that each accepted difference had two reviews. Both are corrected.

Commands at the tip, after the rebase on `main`:

- `scripts/check.sh --test`: `check: passed`, 17,088 tests.
- `dotnet test --project tests/Port.Evaluation`: 16,607 tests, 0 failed. `dotnet test --project tests/Graphiti.Net.E2E.Tests`: 86 tests, 0 failed.
- `python3 -B port/judge/run.py --build dotnet`: 1,999 cases, 1,801 passed, 198 expected failures, 0 unexpected, 0 fixed, 0 bound and wrong.
- `scan_port.py src --allow-todo`: 384 `TODO(port)` markers in 147 files. The other 183 findings are `NotImplementedException` sites that rules R4 and R24 give.

### Layer 8, ingestion, 2026-11-28

Four packets ported the 8 modules of `ingestion`. The ninth unit of the row is the package module, which needs no file. Two Opus 5.5 high reviewers read each packet, and one implementer did each fix round.

| Packet | Modules | Review |
| --- | --- | --- |
| P8a | `change_set`, `state_store`, `sqlite_state_store`, `config` | 6 and 8 findings. 18 of 63 seeded defects passed each test before the fix round. |
| P8b | `delta`, `graph_reader`, and the recording of `ResolutionTrace` | 5 and 10 findings. 17 of 78 seeded defects passed. |
| P8c | `phases` | 4 and 10 findings. 24 of 61 seeded defects passed. |
| P8d | `scheduler` | 3 and 9 findings. 10 of 60 seeded defects passed. |

Each seeded defect that passed fails a test now, with two kinds of exception: a seed that a reviewer showed to change nothing, and the three seeds of `phases` in the list at the end of this section.

Differences in the product code that the reviews found, and their fix:

| Finding | Result |
| --- | --- |
| `merge_edge_attributes` used the identity test, where Python uses a plain `==`. A `nan` value differed. | `PyValueEquality.PlainEqual` (rule R6). |
| `ChangeSet.from_bytes` returned a change set for bytes with a cut check value, where Python raises `zlib.error`. | `PyZlib.Decompress`, an inflate in `PyCompat` (decision D51). |
| `top_matches` used a stable sort. `np.argsort` is not stable for 17 or more items. | `PyNumpy.ArgSort`, the generic sort of numpy 2.4.1 (decision D53, GAPS F47). |
| `asyncio.sleep` has no upper limit, and `Task.Delay` throws above about 49.7 days. | `Asyncio.SleepAsync` at each site, also in `OpenaiDecisionsClient` of layer 3 (rule R8). |
| The apply loop of `phases` overflowed at the largest attempt count. | The loop compares before it adds. |
| The commit span and the report scope got a `double` count, where Python gives an `int`. | Each count is a `long` (rule R18). |
| A cleanup call in `except BaseException` did not take the token of the caller after an error that is not a cancel. | The cleanup token form of rule R8, at seven sites of `phases` and `scheduler`. |
| `SemaphoreSlim` wakes a waiter through the thread pool. Two callers got their sequence numbers in the wrong order in 518 of 2,000 rounds. | `AsyncSemaphore` and `AsyncLock`, ports of the CPython classes (decision D53). The same measurement gives 0 of 2,000. |
| The result of `add_episode` shared its lists with the change set. | Each list is copied (rule R7). No test reaches the site before layer 9. |

What the reviews changed in the evidence:

- A fake cancels with `Cancel()`, never with `CancelAsync()`. The second form ran callbacks on the thread pool, and tests of layers 2, 5, 7, and 8 failed in some runs (rule R25).
- A wrapper of a real object, for example the state store, records the token of each call. Five store calls got no token, and each test passed.
- A test of module state, for example the digest cache of `delta`, runs in one turn of the scheduler.
- The recording of the scheduler has no wait for a recorded state. A wake-up has a fixed place in the order of the loop, so each drain ends when the state is stable.
- The cancel at call k compares the log events after the cancel, and the token of the call that made each store call.

The new semaphore changed three older tests. Seven cancel points of `search`, `edge_operations`, and `combined_extraction` now agree with Python, and they left the `LateCancel` lists. The sections of layers 6 and 7 above give the counts of their day, and GAPS F33 gives the present counts.

Decisions of the lead under D49, for the maintainer at gate 4:

- D51: `PyZlib.Decompress` is an inflate of its own in `PyCompat`.
- D53: `AsyncSemaphore`, `AsyncLock`, and `PyNumpy.ArgSort` are ports of library source in `PyCompat`.
- `AcquireAsync` throws for a token that is canceled at the call, also with a free slot. Python takes the free slot and raises at the next suspension (rule R8).
- The order of work changed. The paths that need the `Graphiti` object, and end-to-end test E8, moved to layer 9.

Differences that the lead accepted by name: GAPS F39, F40, F47, U21, and 18 more cancel points of GAPS F33 (1 in `graph_reader`, 10 in `phases`, and 7 in `scheduler`).

No `TODO(port)` marker is new in a ported unit. Four upstream tests of the scheduler keep a `TODO(port): O10` comment, and the queue names them.

Evidence that waits for layer 9:

- Eight functions and `CommitResult` of `phases` have no recording: `admit_episode`, `admit_bulk`, `analyze_episode`, `analyze_bulk`, `commit_unit`, `enrich_unit`, `_admit_heartbeat`, and `_default_wait`. Each one calls a method of the `Graphiti` object first.
- The paths of `scheduler` after the admit lock have no recording: the commit turn, the order of commits, the apply retry, a successful call, and the error exit.
- Three seeded defects of `phases` pass each test: the type of a span attribute of `commit_unit`, the token of its cleanup call, and the sleep of `DefaultWait`.
- The upstream tests that build `Graphiti` are not ported: `test_commit_repair.py`, `test_concurrent_identity_properties.py`, five tests of `test_scheduler.py`, and parts of three more files. The queue notes name them.
- The five units with no pure case keep the status `tests_pass`. One flow case of the judge, `flow.recorded.concurrent`, runs these paths, and it passes on the layer 9 branch.
- No test runs two processes that write to one SQLite file at one time.
- `tests/ingestion/test_concurrent_ingestion_falkordblite_int.py` needs FalkorDB, which is out of the MVP (decision D10).
- Nobody ran the oracle on x86, where numpy can sort equal scores in another order (GAPS F47).

The mutation run of plan 0003, task 5 did not run after layer 7: the machine stopped under its load. It is still owed.

One more Opus 5.5 high reviewer read the whole layer diff. It found two important gaps and six minor ones, and each one is resolved. A recorder, `record_resolution_trace.py`, could not make its value file again. One seeded defect in `AsyncSemaphore` passed each test, and a second look found one in `AsyncLock`. Three new recorded scripts fail for them.

Commands at the tip, after the rebase on `main`:

- `scripts/check.sh --test`: `check: passed`. 18,080 tests, 0 failed.
- `dotnet test --project tests/Port.Evaluation`: 17,554 tests, 0 failed.
- `dotnet test --project tests/Graphiti.Net.E2E.Tests`: 86 tests, 0 failed.
- The recorders with `--check`: `change_set` 1,365 cases, `state_store` 223, `delta` 373, `graph_reader` 26, `resolution_trace` 17, `phases` 48 and 115 async cases, and `scheduler` 50. Each one is current.
- `python3 -B port/judge/run.py --build dotnet`: 1,999 cases, 1,899 passed, 100 expected failures, 0 unexpected, 0 fixed, 0 bound and wrong. 98 ids left `expected-failures.txt`. The pull request lists the ids.

### Layer 9, the core: graphiti and the flow cases, 2026-11-28

Layer 9 has two pull requests. This section is the first one: the unit `graphiti`, the flow layer of the judge host, and the evidence of `ingestion` that needs the `Graphiti` object. The public .NET surface follows in its own pull request, with end-to-end tests E8 and E9.

| Packet | Work | Review |
| --- | --- | --- |
| P9a | `graphiti` (2,782 Python lines, `Core/Graphiti.cs`) | Two Opus 5.5 high reviewers: 3 and 11 findings. 43 of 102 seeded defects passed each test before the fix round. |
| P9c | The flow layer of the judge host (`tests/Graphiti.Net.JudgeHost/Flow/`) | 5 seeded defects, and each one fails a case. |
| P9e | The time of a driver error, and the model path of `RawEpisode` in the host | 4 seeded defects. One passed, and its code was removed. |
| P9b | Recordings of `phases` and `scheduler` with the real `Graphiti` | 14 seeded defects, and each one fails a test. |
| P9f | Four upstream test files of the `Graphiti` class on Neo4j: 65 cases | 5 seeded defects, and each one fails a test. |
| P9g | The upstream ingestion tests that build `Graphiti`, on a fake at the driver boundary: 65 cases | 5 seeded defects, and each one fails a test. |

Packets P9b, P9c, P9e, P9f, and P9g had no review by two reviewers. The adversarial pass of the pull request read them.

After the fix round of P9a, each seeded defect of its review fails a test, with five exceptions that a reviewer showed to change nothing.

Differences in the product code, and their fix:

| Finding | Result |
| --- | --- |
| A failed `tx.run` raised at the commit, where the Python driver raises it at the next run. `add_nodes_and_edges_bulk_tx` sent one query more than Python (`flow.recorded.custom_types`). | `Neo4jTransaction.RunAsync` and `Neo4jSession.RunAsync` read the keys of the result (rule R9, GAPS F37). The packet measured the Python driver on 11 points first. |
| The commit span and the report scope of `commit_unit` got a `double` count. A C# conditional with a `long` and a `double` branch gives a `double`. | The `long` branch is cast to `object` (rule R18). |
| `SUMMARY_REBUILD_BATCH_SIZE` was a `const`, and two upstream tests replace it. | A `PyPatch` value (rule R17). The two tests set it as upstream does. |
| The reviews of `graphiti` found no difference in the product code. | The fix round changed tests only. |

The flow layer:

- The host runs each of the 34 golden scenarios in a fresh process. The fakes are at the network boundary: an `IChatClient` that gives the recorded replies, an embedding generator, and a recording wrapper of the Neo4j driver.
- 31 cases passed in each run of the first version of the host. `flow.recorded.custom_types` showed the driver difference above. `flow.recorded.bulk` needed a model factory in the host.
- `flow.recorded.lifecycle` depends on the order of rows that a query with no `ORDER BY` returns. Pull request 46 changed the judge: step 5 declares `unordered_params`. Debt row T29 names the limit of that declaration.
- The 34 flow ids left `expected-failures.txt`. 66 pure ids stay.

Which units the flow cases run. The lead measured the lines of each product file that the 34 cases run, with `dotnet-coverage` 18.12.0 from a folder outside the repository:

```bash
dotnet-coverage collect --server-mode --session-id l9flow -f cobertura -o flow.xml &
dotnet-coverage instrument --session-id l9flow tests/Graphiti.Net.JudgeHost/bin/Debug/net10.0/Graphiti.Net.dll
dotnet-coverage instrument --session-id l9flow tests/Graphiti.Net.JudgeHost/bin/Debug/net10.0/Graphiti.Net.Neo4j.dll
NEO4J_URI=bolt://localhost:7688 python3 -B port/judge/run.py --build dotnet --layer flow
dotnet-coverage shutdown l9flow
```

A unit with no pure case moves from `tests_pass` to `reviewed` if the flow cases run a line of its file. The line counts are the lines that the tool instruments, so they differ from the lines of the file.

| Unit | Lines that the flow cases ran | Lines |
| --- | --- | --- |
| `graphiti` | 1,482 | 2,038 |
| `ingestion.phases` | 847 | 1,747 |
| `ingestion.scheduler` | 169 | 306 |
| `decorators` | 34 | 52 |
| `driver.neo4j.operations.community_edge_ops` | 98 | 107 |
| `driver.neo4j.operations.community_node_ops` | 127 | 139 |
| `driver.neo4j.operations.entity_edge_ops` | 189 | 201 |
| `driver.neo4j.operations.entity_node_ops` | 174 | 192 |
| `driver.neo4j.operations.episode_node_ops` | 210 | 225 |
| `driver.neo4j.operations.episodic_edge_ops` | 105 | 117 |
| `driver.neo4j.operations.graph_ops` | 91 | 146 |
| `driver.neo4j.operations.has_episode_edge_ops` | 91 | 112 |
| `driver.neo4j.operations.next_episode_edge_ops` | 103 | 112 |
| `driver.neo4j.operations.saga_node_ops` | 110 | 128 |
| `driver.neo4j_driver` | 226 | 332 |
| `embedder.client` | 2 | 4 |
| `graphiti_types` | 41 | 82 |
| `ingestion.graph_reader` | 61 | 73 |
| `ingestion.state_store` | 195 | 326 |
| `llm_client.client` | 147 | 234 |
| `llm_client.config` | 15 | 15 |
| `llm_client.token_tracker` | 18 | 83 |
| `namespaces.edges` | 179 | 180 |
| `namespaces.nodes` | 164 | 167 |
| `search.search` | 912 | 935 |
| `utils.maintenance.graph_data_operations` | 74 | 121 |

`cross_encoder.client` is an abstract class with no executable line, and the flow cases reach it through `CrossEncoderBridge`. It has the status `reviewed`.

Seven units keep `tests_pass`, because no flow case runs a line of them. Their tests are their only evidence. Four more units keep `tests_pass` for another reason: each one has a pure case that is still listed (`utils.datetime_utils`, `llm_client.prompt_config`, `search.search_config`, and `search.search_utils`).

| Unit | Lines that the flow cases ran | Lines |
| --- | --- | --- |
| `cross_encoder.openai_reranker_client` | 0 | 88 |
| `ingestion.sqlite_state_store` | 0 | 495 |
| `llm_client.cache` | 0 | 74 |
| `llm_client.errors` | 0 | 18 |
| `llm_client.llm_runtime` | 0 | 83 |
| `llm_client.openai_decisions_client` | 0 | 425 |
| `llm_client.utils` | 0 | 15 |

The flow host gives the product an `IChatClient`, so the OpenAI clients, the cache, and the reranker of layer 3 are not on its path. The scenarios use the in-memory state store.

What the packets changed in the evidence:

- The recordings of `phases` and `scheduler` run the real `Graphiti` with the real in-memory state store. They cover 624 of 626 lines of the eight functions of `phases`, and 74 of 74 lines of the scheduler paths after the admit lock.
- The three seeded defects of `phases` that passed each test at the end of layer 8 fail a test now.
- The ingestion tests keep the fakes of the upstream tests, in four places. `HarnessDriver` is a fake driver. `FakeGraph` derives from the product class `GraphReader` and replaces each read, so the queries of `GraphReader` do not run in these tests (the flow cases run 61 of its 73 lines). `LoggingStore` wraps the state store. `HarnessLlm` replaces `generate_response`, which is above the network call. The mocks of `TestGraphitiMock.cs` and the fake model clients of the `graphiti` tests replace `generate_response` too. Part 3 of `port/RULES.md` allows these forms for a port of an upstream test. Rule 4 of `docs/TESTING.md` names only the network boundary (debt row T31). The oracle ran the upstream ingestion tests on the same driver fake: 98 passed.
- Four tests of `TestAddTriplet.cs` assert the result of Python 0.32.0, with a `// PORT:` comment. Their upstream assertion fails on Python itself.
- The lease tests of `TestFailureRules.cs` run on the virtual clock. On the real clock, a stall of 0.3 seconds of the shared scheduler failed them.
- One cache test of layer 3 measured a wait with a clock that started late. It proves an order now (debt row T30).

Decisions of the lead under D49, for the maintainer at gate 4:

- Two pull requests for layer 9.
- The status rule of the measurement above, and the seven units that keep `tests_pass`.
- GAPS F50: a cancel does not stop the wait of a `run` call for its keys.
- Debt row T31: rule 4 of `docs/TESTING.md` has no clause for the fakes that a port of an upstream test keeps.
- Rule R9: a `run` call of the Neo4j adapter reads the keys of its result. It adds one wait and no round trip.
- Seven cancel points have the kind `cleanup`. After a cancel in a cleanup await, the port does not mark the unit failed, and the deadline of the unit releases its position later.

Differences that the lead accepted by name: GAPS F48, F49, U22, and 132 more cancel points of GAPS F33 (48 in `graphiti`, 51 in `phases`, and 33 in `scheduler`).

No `TODO(port)` marker is new in a ported unit. 20 upstream tests keep a `TODO(port): O10` comment: 18 in the ingestion test files and 2 in `TestAddTriplet.cs`. Each one replaces a function of a module with a result, and the queue names the recorded case that covers it.

Commands at the tip of the branch, 2026-11-28, on a Neo4j 5.26.2 with an empty graph:

| Command | Result |
| --- | --- |
| `scripts/check.sh --test` | `check: passed`, 19,268 tests, 0 failed |
| `dotnet test --project tests/Port.Evaluation` | 18,664 tests, 0 failed, 0 skipped |
| `dotnet test --project tests/Graphiti.Net.E2E.Tests` | 164 tests, 0 failed, 0 skipped |
| `python3 -B port/judge/run.py --build dotnet` | 1,999 cases, 1,933 passed, 66 expected failures, 0 unexpected, 0 fixed, 0 bound and wrong, `judge: passed` |
| `record_graphiti.py --check`, `record_phases.py --check`, `record_scheduler.py --check` | Each one is current |

The adversarial pass of the pull request found four minor points and no behavior difference. Each of its nine seeded defects fails a test. The four points are corrected in this section, in the queue, and in GAPS F50 and debt row T31.

What is not checked:

- Two lines of `phases` have no recording: line 820 and line 1810.
- The flow cases run `Neo4jSession.RunAsync` with its branch for a result with no keys. In `Neo4jTransaction.RunAsync` they do not run that branch, because each transaction statement of the scenarios returns rows. Eight end-to-end tests on Neo4j cover both forms.
- A cancel does not stop the new wait of a `run` call. `KeysAsync` and `ConsumeAsync` of Neo4j.Driver take no token, so a statement that returns no rows holds its caller until the server ends it. Python stops that wait at a cancel (GAPS F50).
- After a failed statement, the port throws `TransactionTerminatedException` for the next run, where Python raises `TransactionError` (GAPS F37).
- No test runs two processes that write to one SQLite file at one time.
- The mutation run of plan 0003, task 5 is still owed.
