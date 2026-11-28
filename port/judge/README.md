# The judge

The judge decides whether the .NET build behaves like upstream Python at the pinned commit.
[The design document](../../docs/design-docs/judge-design.md) gives the reasons. This page is the contract.

After gate 2 this folder is read-only. The rule for adding cases after the gate is at the end of this page.

## Commands

```bash
python3 port/judge/run.py --build python            # every case must pass
```

```bash
python3 port/judge/run.py --build dotnet            # the failing set must equal expected-failures.txt and stale-after-pin.txt together
```

```bash
python3 port/judge/run.py --build python --record   # write golden files
```

Useful options: `--layer pure`, `--filter <text in the case id>`, `--verbose`, `--show-missing`, `--list-failures`.
With `--filter`, a recording replaces only the selected cases and keeps the other rows of each golden file.
If a filter selects no case, the runner stops with an error.

Requirements:

| Command | Needs |
| --- | --- |
| Any run | `python3`. The runner uses the standard library only. |
| `--build python` | `scripts/fetch-upstream.sh`, then `port/tools/upstream-tests.sh` one time to create the upstream virtual environment. The Neo4j sidecar for flow cases. The flow layer needs the database for itself: do not run another test or tool on it at the same time. |
| `--build dotnet` | `dotnet build Graphiti.Net.slnx`. The Neo4j sidecar for flow cases. |

## Files

| Path | Holds |
| --- | --- |
| `run.py` | The runner and the noise rules. |
| `cases/<layer>/<file>.jsonl` | Inputs. One case for each line. A case id starts with `<layer>.<file>.`. |
| `golden/<layer>/<file>.jsonl` | Results recorded from Python, after the noise rules. Never edit these by hand. |
| `replies/<name>.jsonl` | Scripted model replies for flow cases. `handwritten` holds replies that a person wrote. `recorded` holds replies from `gpt-4.1-mini`, and `recorded_strong` holds replies from `gpt-4.1`. |
| `add_reply.py` | Adds one hand-written reply to a reply file. |
| `embedder-vectors.json` | Vectors from the Python judge embedder. The .NET host must reproduce them bit for bit. |
| `count.txt` | The lowest allowed case count. |
| `expected-failures.txt` | The .NET cases that still fail. Empty since gate 4. |
| `accepted-differences.txt` | The cases that the maintainer accepted by name at gate 4 (decision D55). Each one must answer `JudgeTypeRejected`. |
| `stale-after-pin.txt` | The .NET cases that a pin move made wrong. Empty outside a sync. Each line is `<case id> <unit> <target version>`. The runner reads the first word only. |
| `host-python/` | The Python host. |

The .NET host is `tests/Graphiti.Net.JudgeHost`. It is outside this folder because each ported unit adds a binding to it.
Its `Core/` folder holds the protocol loop, the codec, and the judge embedder.

## Protocol

The runner starts one host process. It writes each case as one JSON line to the host's standard input.
The host prints one JSON line for each case. Logs go to standard error.
Every line on this channel and every line of a golden file is ASCII-escaped JSON. A Unicode line separator or a lone surrogate inside a string then cannot break a line.

### Pure case

```json
{"id": "pure.text.lucene_sanitize.plus", "fn": "graphiti_core.helpers:lucene_sanitize", "args": ["a + b"], "kwargs": {}}
```

`fn` is `module:attribute.path`. The result is `{"ok": <value>}` or `{"error": "<Python exception class name>"}`.
The Python host awaits a coroutine and turns a generator into a list.
The Python host calls the function with exactly the given arguments. It adds no defaults and no type checks.

Two judge functions exist:

| `fn` | Returns |
| --- | --- |
| `judge:schema_text` with one argument, a `module:Class` path | `json.dumps(Class.model_json_schema())`, the text that upstream puts into prompts |
| `judge:embed` with one text argument | The judge embedding of the text |

### Flow case

```json
{"id": "flow.hand.example", "group": "judge_hand_example", "replies": "handwritten",
 "graphiti": {}, "steps": [{"call": "add_episode", "args": [], "kwargs": {}}]}
```

- `group` is the group id of the scenario. It must start with `judge_` and be unique among cases. The host deletes that group before and after the run.
- Before the first step, the host prepares the database. It deletes the groups of the case and makes sure that no other node exists. Then it drops the full-text indexes, builds all indexes again, and waits until they are online. If another node exists, the case fails with `JudgeDatabaseNotEmpty`. The preparation is not captured.
- `extra_groups` lists more group ids that the scenario writes to. The host deletes them too, and the graph dump includes them.
- `replies` names a file in `replies/`. Leave it out for a scenario that makes no model call.
- A step can hold `parallel`, a list of calls that start together, in place of `call`.
- `graphiti` holds extra keyword arguments for the `Graphiti` constructor.
- `call` is an attribute path on the `Graphiti` object, for example `add_episode` or `nodes.entity.get_by_uuid`.
- A step can hold `unordered`, a list of dotted paths into its return value, for example `["nodes", "edges"]`. The runner compares the list at each path without its order. Use it only for a call whose upstream query has no `ORDER BY`.
- A step can hold `unordered_params`, a list of parameter names, for example `["uuids"]`. In each query of the step, the runner compares a list parameter with one of these names without its order. Use it only where upstream builds the list from the rows of a query with no `ORDER BY`.

The result has one entry for each step, then the graph of the group:

```json
{"steps": [{"call": "...", "ret": "<value>", "llm": [], "embed": [], "rerank": [], "queries": []}],
 "graph": {"nodes": [], "edges": []}}
```

A step that raises has `"error": "<class name>"` in place of `ret`.
A request with no stored reply raises `JudgeMissingReply`, and the result lists it under `missing_replies`.

### Value tags

Arguments and results are JSON. These tags carry the values that JSON lacks.

| Tag | Meaning | In arguments | In results |
| --- | --- | --- | --- |
| `{"$dt": "2026-03-01T00:00:00+00:00"}` | A datetime, written as Python `isoformat()` writes it | Yes | Yes |
| `{"$date": "2026-03-01"}` | A date with no time | No | Yes |
| `{"$ref": "module:attr"}` | The Python object at that path: a class, a constant, a function | Yes | No |
| `{"$enum": "module:Class", "value": v}` | An enum member | Yes | No. Results carry the member's value. |
| `{"$model": "module:name", "data": {}}` | The result of a call to the object at that path, with `data` as keyword arguments. For a class, that is an instance. | Yes | No |
| `{"$type": "Class", ...fields}` | A pydantic model or a dataclass | No | Yes |
| `{"$tuple": []}` | A tuple | Yes | No. Results carry a list. |
| `{"$set": []}` | A set. Results are sorted. | Yes | Yes |
| `{"$dict": [[key, value]]}` | A dict with keys that are not strings | Yes | Yes |
| `{"$bytes": "hex"}` | Bytes | Yes | Yes |
| `{"$float": "nan"}` | `nan`, `inf`, or `-inf` | Yes | Yes |
| `{"$step": [index, "path"]}` | Flow cases only: a value from the return of an earlier step | Yes | No |
| `{"$graphiti": "path"}` | Flow cases only: an object on the `Graphiti` instance, for example `driver` | Yes | No |
| `{"$embed": "text"}` | Flow cases only: the judge embedding of the text | Yes | No |
| `{"$class": "Name"}`, `{"$exc": "Name"}` | A class or an exception that a function returned | No | Yes |
| `{"$vec": {"n", "head", "sum", "wsum"}}` | A list of 64 or more numbers, compacted by the runner | No | Yes |
| `{"$bag": []}` | A list whose order is not a contract. The runner adds this tag and sorts the list. A host never writes it. | No | Golden files only |
| `{"$unknown": "TypeName"}` | The codec does not know this type. Treat it as a defect in the case or in the codec. | No | Yes |

## Capture seams for flow cases

| Capture | Python | .NET |
| --- | --- | --- |
| `llm` | `JudgeLLMClient._generate_response` | A judge `IChatClient` under the product adapter |
| `embed` | `JudgeEmbedder.create` and `create_batch` | A judge `IEmbeddingGenerator` |
| `rerank` | `JudgeCrossEncoder.rank` | A judge cross-encoder |
| `queries` | `RecordingNeo4jClient` around the `neo4j` driver object | A recording decorator around `IDriver` |

One entry in `queries` is one `execute_query` call, or one session with its operations in order.

The judge embedder is a bag of words. Each token gets a vector from SHA-256, the vectors are added, and the sum is scaled to unit length.
The judge cross-encoder scores a passage as `(cosine + 1) / 2` of the judge embeddings, and breaks ties by input position.
Reply keys are SHA-256 hashes of the response model name and the messages. Before the hash, UUIDs and this run's clock values are masked, and each label list is sorted (see the noise rules).
A reply that a person wrote with `add_reply.py` stores the key and the reply, but not the messages. The golden `llm` capture of the scenario holds the full request.

## Noise rules

The runner applies these rules to the host output before it records or compares. The list is closed at gate 2.

| Noise | Rule |
| --- | --- |
| Clock values | An ISO timestamp between the start and the end of the run, with 2 seconds of margin, is a clock value. Its instant is noise. Its format and its relation to the clock values next to it are not. Other timestamps stay exact. |
| Clock values as fields | Clock values that are side by side in one object or one list become `<now#rank:shape>`. The rank is 1 for the earliest instant, and equal instants get the same rank. A field that copies another clock field in place of a new clock reading then shows as a difference. |
| Clock values inside text | A clock value inside a longer text, for example a prompt, becomes `<now:shape>`. |
| Clock shape | `separator\|fraction\|zone`. The separator is `T` or a space. The fraction is `us` for no digits or 6 digits, which is what Python writes, and `<n>digits` for any other count. The zone is the zone text, or `naive`. |
| UUIDs | Each UUID gets a name from its content: `<uuid:` and 10 hex digits. The name is a hash of every place where the UUID appears, with other UUIDs shown as their names, refined until no name splits further. Two UUIDs that no content tells apart get the same name. |
| Order of concurrent work | Four kinds of list are compared without their order. The first kind is the capture lists of a step: `llm`, `embed`, `rerank`, and `queries`. The second kind is the `nodes` and `edges` lists of the graph dump. The third kind is each `$set`. The fourth kind is each list that a step declares in `unordered`, and each list parameter of a query that a step declares in `unordered_params`. The runner wraps each one in `$bag` and sorts it by its JSON text after the UUID rule. Operations inside one session keep their order. A `nodes` or `edges` list inside a return value keeps its order. |
| Order of node labels | Flow results only. Upstream builds the labels of a node from a set, so their order differs between processes. A label list is a list of two or more strings that holds `Entity`. As a value, it is compared without its order. Inside a text, the runner sorts it: a JSON or `repr` array in a prompt, and a chain such as `:Entity:Person` in a Cypher query. Pure cases keep label order exact. |
| Vectors | A list of 64 or more numbers becomes `$vec` with its length, its first 8 values, its sum, and its sum weighted by position. If two values swap places, the weighted sum changes. |
| Numbers | If the golden value is an integer, the other value must have the same integral value. Above 2^53 it must also be written as an integer, because a double cannot hold it. If the golden value is a float, the other value must match within 1e-6, relative or absolute. |
| Index build in the driver constructor | Not captured. A scenario calls `build_indices_and_constraints` as a step. |
| State of the full-text index | Not a rule of the runner, but a duty of each host. A full-text score depends on every entry of the index, also on entries of deleted nodes that Neo4j did not remove yet. Each host thus gives each flow case an empty database with new full-text indexes. Without that step, the order of search rows changes between runs. |

Everything else is compared exactly: prompt text, schema text, Cypher text, parameter names, property names, and result order.

## What a flow case must avoid

The judge can compare only what upstream does the same way in each run. Upstream leaves these three things open, so a case must stay away from them.

| Source | Why it is not stable | What to do |
| --- | --- | --- |
| Two search rows with the same full-text score | Upstream sorts by score only. Neo4j returns equal scores in an order that can change. The order reaches results and prompts. | Run `port/tools/find_score_ties.py` on the case. Change the text until it prints no `TIE` line. |
| Two facts in one `add_episode_bulk` call that upstream merges as duplicates | Upstream keeps the fact with the smallest UUID, and UUIDs are random. | Give each pair of entities one fact in a bulk call. |
| `build_communities` on nodes with different summaries | Upstream pairs the summaries in an order that comes from a query with no `ORDER BY`. The prompts then differ between runs. | Give all nodes of one community the same summary, as `flow.hand.communities` does. |
| A list that upstream builds from the rows of a query with no `ORDER BY`, and then passes to a later query (`remove_episode` with two or more edges to delete) | Neo4j returns the rows in the order of their UUIDs, and UUIDs are random. The order reaches the parameter of the later query. | Declare the parameter in `unordered_params` of the step, as `flow.recorded.lifecycle` does. |
| Calls in one `parallel` step that write to the same group | The result depends on which call commits first. | Use a different group for each call in a `parallel` step. Test same-group ingestion with calls in sequence. |

## How to add cases

1. Add lines to a file in `cases/`. Start each id with `<layer>.<file>.`.
2. Record: `python3 port/judge/run.py --build python --record --filter <id prefix>`.
3. Read every `judge: review` line. A recorded error becomes the expected behavior. Run the case with `JUDGE_VERBOSE=1` and read the traceback. If upstream itself raises that error for that input, keep the case. If the case called the function in the wrong way, fix the case.
4. Run `python3 port/judge/run.py --build python` two times. Both runs must pass.
5. Add the new ids to `expected-failures.txt`, and raise `count.txt`.

For a flow case that needs replies from a real model:

1. Put `OPENAI_API_KEY` into `.env`. The host reads it and never prints it.
2. `python3 port/judge/run.py --build python --record --record-replies --filter <id>` asks the model for each request with no stored reply, and stores the reply. `JUDGE_RECORD_MODEL` selects the model. The default is `gpt-4.1-mini`.
3. Run the same command again until the reply file does not grow. A replay can ask for a request that the first run did not make.
4. Run `port/tools/find_score_ties.py` on the case. If it prints a `TIE` line, change the case text and go back to step 2.
5. After a case text changes, run `port/tools/prune_replies.py --write` to remove the replies that no case requests.

For a flow case that needs hand-written replies:

1. `python3 port/judge/run.py --build python --filter <id> --show-missing` prints the first request with no reply and its key.
2. `python3 port/judge/add_reply.py <replies name> <key> < reply.json` stores a reply.
3. Repeat until no request is missing. Then record.

## Bindings in the .NET host

`tests/Graphiti.Net.JudgeHost/Bindings.cs` maps each `fn` name to the ported method.
A binding can do three things only: decode the arguments, call the product code, and encode the result.
A case with no binding fails with `JudgeNotBound`, which keeps it in `expected-failures.txt`.
A case in `expected-failures.txt` can fail only with `JudgeNotBound` or `JudgeTypeRejected`. If a bound case runs and gives a wrong result, the runner reports it as `bound_wrong` and fails, even though the case is in the list.

The .NET host answers `JudgeTypeRejected` if a C# parameter cannot hold an argument of the case (decision D20).
If the golden result is `TypeError` or `AttributeError`, the case passes, because Python refused the same argument for its type.
If the golden result is another error or a value, the case fails, unless its id is in `accepted-differences.txt`.
The maintainer accepts such a difference by name, and only then does the id enter that file (decision D55).
A case in that file fails if the host gives any answer other than `JudgeTypeRejected`, and the runner also fails for an id there that no case has, and for an id that is in both lists.
A line of the file that starts with `#`, with or without spaces before it, is a comment.

A case in `stale-after-pin.txt` can be bound and give a wrong result, from a pin move until its unit is ported again (port/UPSTREAM.md).
If such a case passes, the runner fails until its line is removed. The runner also fails for an id there that no case has, and for an id that is also in one of the other two lists.
A case there cannot answer `JudgeTypeRejected`: a port cannot change that answer, so the maintainer decides the case by name (decision D55).

## Cases mined from upstream pytest files

| Upstream pytest file | Case file | Cases |
| --- | --- | --- |
| `tests/test_text_utils.py` | `pure/text.jsonl` | 11 |
| `tests/utils/test_concatenate_episodes.py` | `pure/text.jsonl` | 9 |
| `tests/helpers_test.py` | `pure/text.jsonl` | 2 |
| `tests/utils/test_content_chunking.py` | `pure/chunking.jsonl` | About 35 |
| `tests/test_edge_db_queries.py` | `pure/queries.jsonl` | 24 |
| `tests/test_node_label_security.py` | `pure/queries.jsonl` | 30 |
| `tests/utils/search/test_search_security.py` | `pure/filters.jsonl`, `pure/queries.jsonl` | 62 |
| `tests/utils/search/test_edge_bfs_query_shape.py` | `pure/filters.jsonl` | 2 |
| `tests/ingestion/test_update_queries.py` | `pure/queries.jsonl` | 13 |
| `tests/prompts/test_legacy_prompt_compatibility.py` | `pure/prompts.jsonl` | 32 |
| `tests/prompts/test_complete_prompt.py` | `pure/prompts.jsonl` | 4 |
| `tests/prompts/test_configurable_prompt_library.py` | `pure/prompts.jsonl` | 5 |
| `tests/prompts/test_prompt_exports.py` | `pure/prompts.jsonl` | 1 |
| `tests/prompts/test_prompt_names.py` | `pure/prompts.jsonl` | 3 |
| `tests/test_graphiti_configurable_prompts.py` | `pure/prompts.jsonl` | 3 |

The case writers reported these counts. Some are approximate, because one case can serve two pytest files.
Pytest files that need a mocked driver or a mocked model gave no pure case. The flow layer covers their behavior through real calls.

## Rule for changes after gate 2

If all of these hold, you can add cases without a new gate:

- No case is changed or removed.
- Golden output is recorded again from the unchanged Python source.
- Every old case keeps the same golden output.
- `count.txt` goes up.

Write what was added and why in `port/DECISIONS.md`. Any other change needs the maintainer.

A pin move is the one case where golden output changes for an old case. In a pin pull request, the lead records all golden files again from the new pinned source (port/UPSTREAM.md, step 1). A case or `count.txt` changes in that pull request only through a patch that the maintainer applied on its branch.
