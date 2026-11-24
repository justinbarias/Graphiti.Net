# Judge design

Status: approved by the maintainer on 2026-11-24 (plan 0002, task 1). The judge is built, and [the judge README](../../port/judge/README.md) is the contract.
The maintainer accepted the recommendation for each of the six choices under "Decisions for the maintainer".
The section "Changes after the adversarial review" lists what changed on 2026-11-25. The maintainer accepted them and closed gate 2 on 2026-11-25.

The judge decides whether the .NET build behaves like upstream Python at the pinned commit.
It works from outside both builds, and it is built before any C# is ported.

## Evidence from upstream

These facts come from the checkout in `.upstream/graphiti`. They shape the design.

| Fact | Source | Effect on the judge |
| --- | --- | --- |
| The base `LLMClient.generate_response` does the shared work: the attribute preamble, the schema text, the language instruction, input cleaning, and the cache. A provider that keeps the base method implements only `_generate_response(messages, response_model, max_tokens, model_size, model)`. | `llm_client/client.py`, lines 152 and 207 | `_generate_response` is the capture seam. It is the same place where the `Microsoft.Extensions.AI` adapter sits in .NET. |
| Seven provider clients replace `generate_response`: Anthropic, Gemini, Jev, GLiNER2, the OpenAI base client, the generic OpenAI client, and the decisions client. The default `OpenAIClient` sends the schema through `responses.parse` and adds no schema text to the prompt. The first version of this table said that every provider implements only `_generate_response`. That was wrong. | `llm_client/openai_base_client.py`, `llm_client/openai_client.py`, line 105 | The judge pins the base path. The prompt that the default client sends differs from it. The maintainer decides at gate 2 which path the .NET adapter follows. |
| The base client appends `json.dumps(response_model.model_json_schema())` to the last message. | `llm_client/client.py`, line 227 | The pydantic schema text is part of the prompt. The .NET build must produce the same text. |
| All Neo4j traffic goes through `execute_query`, a session `run`, or a transaction `run`. | `driver/neo4j_driver.py`, `driver/neo4j/operations/` | The Neo4j library boundary is the capture seam for queries. |
| 47 call sites use `semaphore_gather` and 5 use `asyncio.gather`. | `helpers.py`, line 132 | The order of concurrent LLM calls is not a contract. |
| The code calls `uuid4()` and `utc_now()` during ingestion. | `ingestion/phases.py` and 12 other modules | Identifiers and clock values differ between runs and need a noise rule. |
| The default embedding size is 1024. | `embedder/client.py`, line 23 | The judge embedder returns 1024 values. |

## Parts

```text
port/judge/
  README.md               commands, the noise list, the pytest-to-case table
  run.py                  the runner. Python standard library only
  count.txt               the lowest allowed case count
  expected-failures.txt   .NET cases that still fail. Empty at M1
  cases/<layer>/*.jsonl   inputs
  golden/<layer>/*.jsonl  outputs recorded from Python
  replies/*.jsonl         scripted model replies
  embedder-vectors.json   shared check values for the judge embedder
  host-python/            the Python host. Only the recorder needs it

tests/Graphiti.Net.JudgeHost/   the .NET host. Outside port/judge, because each ported unit adds a binding
```

A host is a thin command. It reads cases as JSON lines on standard input and prints one JSON result for each case.
The runner starts one host process, sends every case, and compares each result with its golden file.

```bash
python3 port/judge/run.py --build python --record   # write golden files. Needs the upstream uv environment
python3 port/judge/run.py --build python            # every case must pass
python3 port/judge/run.py --build dotnet            # the failing set must equal expected-failures.txt
```

The runner itself needs only `python3`, like `scripts/check_docs.py`. CI and cloud sessions never install upstream Python packages.

## Layer 2: pure functions

A case names an upstream function and gives its arguments.

```json
{"fn": "graphiti_core.utils.content_chunking.chunk_text", "args": ["..."], "kwargs": {}}
```

The result is `{"ok": <value>}` or `{"error": "<exception class name>"}`.

The Python host decodes the case, calls the real function with exactly those arguments, and prints the result.
It adds no defaults, no type checks, and no error handling.
The .NET host maps each `fn` name to the ported method through one explicit table.

Targets:

- Content chunking, text utilities, and datetime helpers.
- Dedupe helpers.
- Search filters and the query fragments that they build.
- The node and edge query builders, and the record parsers.
- Prompt rendering for every prompt in the library.
- RRF and MMR ranking.
- Group id and label checks.
- The pydantic schema text of every response model.

## Layer 3: flows

A case is a scenario: a list of public calls on one `Graphiti` instance, against a real Neo4j, with scripted model replies.
A step can also hold a `parallel` list of calls that start together. The concurrent ingestion scenario uses it.

```json
{"group": "g1", "replies": "quickstart", "steps": [
  {"call": "build_indices_and_constraints"},
  {"call": "add_episode", "args": {"name": "e1", "episode_body": "...", "reference_time": "2026-03-01T00:00:00Z"}},
  {"call": "search", "args": {"query": "..."}}
]}
```

For each step the host captures four things:

| Capture | Seam in Python | Seam in .NET |
| --- | --- | --- |
| LLM requests | A judge subclass of `LLMClient` that implements `_generate_response` | A judge `IChatClient` under the product adapter |
| Embedding requests | A judge subclass of `EmbedderClient` | A judge `IEmbeddingGenerator` |
| Queries | A recording proxy around the `neo4j` driver object | A recording decorator around `IDriver`, its sessions, and its transactions |
| Return value | The value that the public call returns | The same |

After the last step, the host also dumps the whole graph of the scenario's group from Neo4j: every node and edge with all properties.
The graph dump is the strongest check, because it shows the final effect of every write.

Calls in scope:

- `build_indices_and_constraints`
- `add_episode`, `add_episode_bulk`, `add_triplet`
- `search`, and `search_` with each recipe
- `retrieve_episodes`, `get_nodes_and_edges_by_episode`, `remove_episode`
- `build_communities`, `summarize_saga`
- Concurrent ingestion through the scheduler

### Scripted replies

The judge LLM client looks up each request in a reply file. The key is a hash of the response model name and the message text after the noise rules are applied.
A request with no matching key fails the case, and the report shows the closest stored request with a text diff.

The judge embedder is a pure function from text to 1024 values, seeded by a SHA-256 hash of the text and normalized to unit length.
It exists once in Python and once in C#. Both must reproduce `embedder-vectors.json`.

The judge cross-encoder computes each score from the judge embeddings, so it needs no reply file.

### Scenario sources

1. The upstream quickstart episodes and the first part of the podcast transcript in `examples/`.
2. Inputs mined from the upstream pytest files. The README table maps each pytest file to its cases.
3. Custom entity and edge types, excluded entity types, sagas, configurable prompts, and each search recipe and filter.
4. Guard scenarios for inputs: an empty episode, an episode with only an emoji, a combining mark in an entity name, and a very long episode that forces chunking.
5. Guard scenarios for replies: malformed JSON, an empty list, an unknown entity id, and one entity repeated.

## Noise rules

The runner applies these rules to both sides before it compares. The list is closed after gate 2.

| Noise | Rule |
| --- | --- |
| UUIDs | Each UUID gets a name from its content, refined until no name splits further. The first rule gave names by order of first appearance. It gave different results between runs, because two edges that differ only in their UUIDs have no stable order. Changed on 2026-11-24 during task 3. |
| Clock values | The instant of a timestamp that falls between the start and the end of the run is noise. Its format, and its order among the clock values next to it, are compared. A timestamp that comes from the input stays exact. Changed on 2026-11-25. |
| Order of concurrent requests | The capture lists of one step and the node and edge lists of the graph dump are compared as sorted multisets. Queries inside one transaction keep their order. Lists inside a return value keep their order, unless the case declares one as `unordered`. Changed on 2026-11-25. |
| Numbers | A golden float matches within 1e-6. A golden integer needs the same integral value. Changed on 2026-11-25. |
| Vectors | A list of 64 or more numbers is compared by its length, its first 8 values, its sum, and its sum weighted by position. |

Everything else is compared exactly. That includes prompt text, schema text, Cypher text, parameter names, property names, and the order of search results.

## What the judge cannot reach

Plan 0002, task 11 lists these in `port/GAPS.md`. Scripts in `port/tools/` record their expected values.

- Exception types and their messages.
- Equality and hashing of node and edge types.
- The retry and backoff behavior of the LLM client.
- The LLM response cache.
- Tracing spans.
- The SQLite state store file format.

The judge also cannot show that real models give good answers through the .NET adapter. The M1 quickstart run with a real LLM covers that.

## Break tests

For each layer, break the Python code in at least three places, one at a time, and make sure that the judge fails each time. Planned breaks:

| Layer | Break |
| --- | --- |
| 2 | Change one word in a prompt. Change the RRF constant. Remove one stop word from the full-text query builder. |
| 3 | Drop the `group_id` filter from one search query. Skip edge invalidation. Change a dedupe threshold. Stop writing one node property. |

## Relation to the end-to-end layer

The judge captures at the contract seams, so it does not exercise a real provider package.
The end-to-end layer covers that gap. It runs the product adapter with the official `OpenAI` package against a small HTTP stub that replays the same reply files.
The stub is a C# project next to the end-to-end tests. Only the end-to-end tests use it.

## Decisions for the maintainer

| # | Choice | Accepted answer | Rejected alternative |
| --- | --- | --- | --- |
| 1 | Where the judge captures LLM requests | At `_generate_response`, the provider contract. Both builds own that seam. | At the HTTP wire. The .NET wire format comes from a provider package, so wire differences are not port bugs. |
| 2 | How to compare the pydantic schema text inside prompts | Exactly, character for character. The .NET build needs a schema writer that follows pydantic's output and `json.dumps` spacing. | Parse the schema out of the prompt and compare it as data. Easier, but the model then sees different prompt text than upstream sends. |
| 3 | Where scripted replies come from | Record them one time from a real model through the Python build. The maintainer supplies a key for that run. Guard replies are written by hand. | Write every reply by hand. No key needed, but the replies are less realistic and exercise fewer dedupe and invalidation paths. |
| 4 | Clock values | Normalize them to `<now>`. No change to upstream at run time. | Patch `utc_now` in the recorder to a fixed time. Exact comparison, but the recorder then changes the oracle. |
| 5 | Order of concurrent requests | Compare as a multiset. | Force `SEMAPHORE_LIMIT=1` in both builds and compare in order. Stricter, but it tests a configuration that users do not run. |
| 6 | Cypher text | Compare exactly. | Normalize whitespace. Not needed for a strict port. |

## Changes after the adversarial review

An Opus 5.5 subagent at high effort reviewed the judge on 2026-11-25, with a brief to refute it. It reported 6 important findings and 14 smaller ones.

| Finding | What was wrong | Answer |
| --- | --- | --- |
| I1 | The runner sorted every list named `nodes` or `edges`, also inside return values. A port that returned search results in the wrong order passed. | Only the capture lists, the graph dump, `$set`, and declared `unordered` paths are sorted. Break tests 12 and 13 prove it. |
| I2 | Every clock value became `<now>`. A port that wrote a naive timestamp, or copied `created_at` into `expired_at`, passed. | Clock tokens carry a rank and a shape. Break tests 14 and 15 prove it. |
| I3 | An integer and a float matched within the float tolerance. | A golden integer needs the same integral value. |
| I4 | A case in `expected-failures.txt` was free to fail for any reason. A bound but wrong case stayed hidden. | A listed case can fail only with `JudgeNotBound`. |
| I5 | The evidence table said that every provider implements only `_generate_response`. | Corrected above. Decision D17 keeps the base path. |
| I6 | No recorded scenario reached `add_episode_bulk`, `build_communities`, `add_triplet`, `remove_episode`, `summarize_saga`, the scheduler, the decisions client, or the tracer. | The real-model scenarios now reach the first six. `flow.hand.guard_invisible_chars` covers input cleaning. The decisions client, the tracer, a custom prompt library, and the attribute merge of `add_triplet` stay open as J13 in `port/GAPS.md`. |

## Changes at gate 2

The maintainer decided these points on 2026-11-25. `port/DECISIONS.md` holds them as D17 to D19.

| Point | Result |
| --- | --- |
| LLM contract | The base client path stays the contract. |
| Node labels | Their order is noise. The rule covers label values, label lists inside prompt text, label chains inside Cypher text, and reply keys. A rule for the values alone is not enough, because a reply key is a hash of the prompt text. |
| Real-model scenarios | 11 scenarios are recorded: 10 with `gpt-4.1-mini` and 1 with `gpt-4.1`. |

The recording showed the limits of a differential judge on this code. Upstream leaves four behaviors open. The first is the order of search rows with equal scores. The second is the pair order in `build_communities`. The third is the commit order of episodes that run at the same time. The fourth is the fact that stays after a merge in a bulk call. The judge cannot compare an open behavior, so the cases stay away from each one. `port/judge/README.md` gives the rule for each. `port/GAPS.md` lists them as U15 to U18 and J14.

The recording also showed that a full-text score depends on the history of the index. Each host now gives each flow case an empty database with new full-text indexes.

Six pure cases were removed, because each one recorded an error of the case itself and not upstream behavior: five `edge_label_*` cases and `pure.queries.fulltext_query.no_driver_attr`.

## Changes to plan 0002

- Task 3 builds the two judge hosts and the reply lookup. The HTTP stub moves to the end-to-end work in plan 0004.
- The bootstrap added deny rules for `port/judge/` to `.claude/settings.json` too early. The maintainer approved their removal on 2026-11-24 for phase 2. Task 12 put them back at gate 2, on 2026-11-25.
