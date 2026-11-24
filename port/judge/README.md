# The judge

The judge decides whether the .NET build behaves like upstream Python at the pinned commit.
[The design document](../../docs/design-docs/judge-design.md) gives the reasons. This page is the contract.

After gate 2 this folder is read-only. The rule for adding cases after the gate is at the end of this page.

## Commands

```bash
python3 port/judge/run.py --build python            # every case must pass
```

```bash
python3 port/judge/run.py --build dotnet            # the failing set must equal expected-failures.txt
```

```bash
python3 port/judge/run.py --build python --record   # write golden files
```

Useful options: `--layer pure`, `--filter <text in the case id>`, `--verbose`, `--show-missing`, `--list-failures`.
With `--filter`, a recording replaces only the selected cases and keeps the other rows of each golden file.

Requirements:

| Command | Needs |
| --- | --- |
| Any run | `python3`. The runner uses the standard library only. |
| `--build python` | `scripts/fetch-upstream.sh`, then `port/tools/upstream-tests.sh` one time to create the upstream virtual environment. The Neo4j sidecar for flow cases. |
| `--build dotnet` | `dotnet build Graphiti.Net.slnx`. The Neo4j sidecar for flow cases. |

## Files

| Path | Holds |
| --- | --- |
| `run.py` | The runner and the noise rules. |
| `cases/<layer>/<file>.jsonl` | Inputs. One case for each line. A case id starts with `<layer>.<file>.`. |
| `golden/<layer>/<file>.jsonl` | Results recorded from Python, after the noise rules. Never edit these by hand. |
| `replies/<name>.jsonl` | Scripted model replies for flow cases. |
| `add_reply.py` | Adds one hand-written reply to a reply file. |
| `embedder-vectors.json` | Vectors from the Python judge embedder. The .NET host must reproduce them bit for bit. |
| `count.txt` | The lowest allowed case count. |
| `expected-failures.txt` | The .NET cases that still fail. Empty at M1. |
| `host-python/` | The Python host. |

The .NET host is `tests/Graphiti.Net.JudgeHost`. It is outside this folder because each ported unit adds a binding to it.
Its `Core/` folder holds the protocol loop, the codec, and the judge embedder.

## Protocol

The runner starts one host process. It writes each case as one JSON line to the host's standard input.
The host prints one JSON line for each case. Logs go to standard error.

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
- `replies` names a file in `replies/`. Leave it out for a scenario that makes no model call.
- `graphiti` holds extra keyword arguments for the `Graphiti` constructor.
- `call` is an attribute path on the `Graphiti` object, for example `add_episode` or `nodes.entity.get_by_uuid`.

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
| `{"$ref": "module:attr"}` | The Python object at that path: a class, a constant, a function | Yes | No |
| `{"$enum": "module:Class", "value": v}` | An enum member | Yes | No. Results carry the member's value. |
| `{"$model": "module:Class", "data": {}}` | An instance, built as `Class(**data)` | Yes | No |
| `{"$type": "Class", ...fields}` | A pydantic model or a dataclass | No | Yes |
| `{"$tuple": []}` | A tuple | Yes | No. Results carry a list. |
| `{"$set": []}` | A set. Results are sorted. | Yes | Yes |
| `{"$dict": [[key, value]]}` | A dict with keys that are not strings | Yes | Yes |
| `{"$bytes": "hex"}` | Bytes | Yes | Yes |
| `{"$float": "nan"}` | `nan`, `inf`, or `-inf` | Yes | Yes |
| `{"$step": [index, "path"]}` | Flow cases only: a value from the return of an earlier step | Yes | No |
| `{"$class": "Name"}`, `{"$exc": "Name"}` | A class or an exception that a function returned | No | Yes |
| `{"$vec": {"n", "head", "sum"}}` | A list of 64 or more numbers, compacted by the runner | No | Yes |
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
Reply keys are SHA-256 hashes of the response model name and the messages, after UUIDs and this run's clock values are masked.

## Noise rules

The runner applies these rules to the host output before it records or compares. The list is closed at gate 2.

| Noise | Rule |
| --- | --- |
| Clock values | An ISO timestamp between the start and the end of the run, with 2 seconds of margin, becomes `<now>`. Other timestamps stay exact. |
| UUIDs | Each UUID gets a name from its content: `<uuid:` and 10 hex digits. The name is a hash of every place where the UUID appears, with other UUIDs shown as their names, refined until no name splits further. Two UUIDs that no content tells apart get the same name. |
| Order of concurrent work | These lists are sorted by their JSON text after the UUID rule: `llm`, `embed`, `rerank`, `queries`, `nodes`, `edges`, `$set`. Operations inside one session keep their order. |
| Vectors | A list of 64 or more numbers becomes `$vec` with its length, its first 8 values, and its sum. |
| Numbers | Two floats match within 1e-6, relative or absolute. Integers match exactly. |
| Index build in the driver constructor | Not captured. A scenario calls `build_indices_and_constraints` as a step. |

Everything else is compared exactly: prompt text, schema text, Cypher text, parameter names, property names, and result order.

## How to add cases

1. Add lines to a file in `cases/`. Start each id with `<layer>.<file>.`.
2. Record: `python3 port/judge/run.py --build python --record --filter <id prefix>`.
3. Read every `judge: review` line. A recorded error becomes the expected behavior. Run the case with `JUDGE_VERBOSE=1` and read the traceback. If upstream itself raises that error for that input, keep the case. If the case called the function in the wrong way, fix the case.
4. Run `python3 port/judge/run.py --build python` two times. Both runs must pass.
5. Add the new ids to `expected-failures.txt`, and raise `count.txt`.

For a flow case that needs hand-written replies:

1. `python3 port/judge/run.py --build python --filter <id> --show-missing` prints the first request with no reply and its key.
2. `python3 port/judge/add_reply.py <replies name> <key> < reply.json` stores a reply.
3. Repeat until no request is missing. Then record.

## Bindings in the .NET host

`tests/Graphiti.Net.JudgeHost/Bindings.cs` maps each `fn` name to the ported method.
A binding can do three things only: decode the arguments, call the product code, and encode the result.
A case with no binding fails with `JudgeNotBound`, which keeps it in `expected-failures.txt`.

## Cases mined from upstream pytest files

| Upstream pytest file | Case file | Cases |
| --- | --- | --- |
| (filled in by plan 0002, task 6) | | |

## Rule for changes after gate 2

If all of these hold, you can add cases without a new gate:

- No case is changed or removed.
- Golden output is recorded again from the unchanged Python source.
- Every old case keeps the same golden output.
- `count.txt` goes up.

Write what was added and why in `port/DECISIONS.md`. Any other change needs the maintainer.
