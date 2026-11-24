# Gaps

Port phase 3. This file lists each place where upstream Python does something that C# cannot say directly, with the chosen answer.
It also lists the public surface that the judge cannot reach.
The lead owns this file. Reviewers must challenge it.

The counts cover the 114 MVP units at the pinned commit. They come from `grep` over `.upstream/graphiti/graphiti_core`.

## Language gaps

| # | Python construct | Where | Count | Answer |
| --- | --- | --- | --- | --- |
| G1 | A Python `repr` of a list or dict goes into prompt text through an f-string, for example `[{'idx': 0, 'fact': '...'}]` | `prompts/*.py` | 39 f-string sites read `context[...]` | `PyCompat.Repr` must write Python `repr` for strings, numbers, booleans, `None`, lists, dicts, and tuples. That includes the choice between single and double quotes. Judge layer 2 pins every prompt. |
| G2 | `json.dumps` text goes into prompts, with Python's default separators `, ` and `: ` and ASCII escapes | `prompts/prompt_helpers.py` (`to_prompt_json`, 41 calls), 17 direct `json.dumps` calls | 58 | One `PyCompat.JsonDumps` helper with the options that each call site uses: `ensure_ascii`, `indent`, `sort_keys`, `default`. Never call `JsonSerializer` directly for prompt text. |
| G3 | The pydantic JSON schema text of the response model is appended to the prompt | `llm_client/client.py:227`, `graphiti.py:830` | 5 `model_json_schema` calls | A schema writer that reproduces pydantic's output: key order, `title` values, `$defs`, `anyOf` for optional fields, `default`. Judge layer 2 pins the schema text of every response model. |
| G4 | `str(datetime)` and `datetime.isoformat()` reach prompts, queries, and stored values | 18 sites | 18 | `PyCompat.IsoFormat` and `PyCompat.Str(DateTimeOffset)`. If the microseconds are zero, Python leaves them out. Python writes `+00:00`, never `Z`. Store 6 fractional digits at most. |
| G5 | pydantic models with validators and coercion | `nodes.py`, `edges.py`, `search/`, `prompts/models.py`, `ingestion/` | 3 validator functions, plus field coercion everywhere | Records with an explicit `Validate()` that the constructor path calls. LLM replies are parsed with the same coercion rules that pydantic applies in lax mode: a numeric string becomes an integer, and a missing optional field gets its default. Judge layer 3 guard scenarios pin the common cases. |
| G6 | Custom entity and edge types are pydantic classes that the user defines. The class docstring and the field descriptions reach prompts. | `add_episode(entity_types=...)` | | Ask-first item for the public surface design: how a .NET user declares a type, its description, and its field descriptions. The judge fixtures in `port/judge/host-python/fixtures.py` fix what the prompt text must be. |
| G7 | Duck typing | 60 `getattr` or `hasattr` calls, 94 `isinstance` calls | 154 | An interface for each role. The rulebook lists each `getattr` site with its interface. No `dynamic` in product code. |
| G8 | `typing.Protocol` classes | `driver/`, `ingestion/` | 10 | C# interfaces with the same member names in PascalCase. |
| G9 | Decorators | `@handle_multiple_group_ids` (4 uses), `@retry` from tenacity (1 use), 2 `@asynccontextmanager` | 7 | `handle_multiple_group_ids` becomes an explicit wrapper call in each of the 4 methods. `@retry` becomes a Polly pipeline with the same numbers: 4 attempts, random exponential wait with multiplier 10, minimum 5 seconds, maximum 120 seconds. Context managers become `IAsyncDisposable`. |
| G10 | Module-level configuration from environment variables, read at import | `helpers.py`, `embedder/client.py` | 7 | Static readonly fields with the same names and defaults, read one time. The public surface offers options that override them. The judge runs with the defaults. |
| G11 | Import cycles | 21 MVP modules in cycle groups ([queue.json](queue.json), `"cycle": true`) | 21 | C# has no import order, so a cycle is legal inside one assembly. Port each cycle group in one unit of work. The driver contract cycle must not pull `Neo4j.Driver` into the core assembly. |
| G12 | The built-in `hash()` of a string is salted for each process | `ingestion/delta.py:163`, `nodes.py:150`, `edges.py:97` | 3 | `__hash__` on nodes and edges becomes `GetHashCode` over the UUID. For `delta.py`, the hash is a cache key inside one process. Use any stable hash, and make sure that no stored value depends on it. A reviewer must challenge this reading of `delta.py`. |
| G13 | Sorting relies on Python's stable sort and on tuple comparison | 42 `sorted` or `.sort` calls | 42 | `OrderBy` and `ThenBy` are stable. `List.Sort` and `Array.Sort` are not stable and are banned for ported sorts. A sort key that is a tuple becomes `OrderBy` then `ThenBy` in the same order. |
| G14 | Dict and set iteration order reaches prompts, queries, and results | Throughout | | A Python dict keeps insertion order. Use `Dictionary` only where no order is read, and `OrderedDictionary<,>` or a list of pairs elsewhere. A Python set has no stable order: where upstream iterates a set into output, the judge noise rules already sort that output, and the rulebook must name each site. |
| G15 | String length, slicing, and case mapping count code points | 11 `lower`, `upper`, or `title` calls, plus slicing in `content_chunking.py` and `dedup_helpers.py` | | `PyCompat.Len`, `Slice`, `Lower`, and the character class helpers. Judge layer 2 has cases with an emoji and a combining mark for each text function. |
| G16 | Regular expressions | 19 `re` calls | 19 | Translate each pattern by hand with the regex rules of the rulebook. `\w`, `\d`, and `\s` match Unicode in Python 3, so use the matching `RegexOptions` and never `ECMAScript`. |
| G17 | `numpy` vector math | 25 calls: L2 norm, dot product, MMR | 25 | `System.Numerics.Tensors.TensorPrimitives` on `double`. Sum in the same order as numpy only where the judge shows a difference above 1e-6. |
| G18 | Generators | 9 `yield` sites, none with `send()` | 9 | `IEnumerable` or `IAsyncEnumerable` with `yield return`. The 2 context managers are in G9. |
| G19 | Concurrency through `semaphore_gather` (47 call sites) and `asyncio.gather` (5) | `helpers.py:132` | 52 | One `PyCompat.SemaphoreGather` that keeps the result order of the inputs and applies the same limit. The order of side effects between concurrent calls is not a contract. |
| G20 | The `Neo4jDriver` constructor starts an index build as a background task | `driver/neo4j_driver.py:92` | 1 | Not ported as constructor behavior. The .NET driver builds indexes only through an explicit call. The judge does not capture the constructor's index build. This is a deliberate deviation. |
| G21 | Exceptions | `errors.py`, `llm_client/errors.py` | | Each class keeps its name with the `Exception` suffix that CA1710 needs. `Codec.PythonExceptionName` in the judge host maps each one back to the Python name. Python built-ins map as the rulebook says: `ValueError` to `ArgumentException`, `KeyError` to `KeyNotFoundException`, `TypeError` to `ArgumentException` or `InvalidCastException`. |
| G22 | Functions without type hints | 127 of 1,178 functions | 127 | [TYPE_HINTS.md](TYPE_HINTS.md) gives the signature that pyright infers for each one. The upstream source is not changed. |

## What the judge cannot reach

Each item needs expected values from Python, recorded by a script in `port/tools/`, and a test in the temporary evaluation tests. Plan 0003 writes those scripts with the units that need them.

| # | Surface | Why the judge misses it | Answer |
| --- | --- | --- | --- |
| J1 | Exception messages | The judge compares exception class names only. | Record `str(exception)` for each class in `errors.py` with representative arguments. |
| J2 | Equality and hashing of nodes and edges | Results carry no equality information. | Record `a == b` for pairs: the same UUID with different fields, and different UUIDs with the same fields. |
| J3 | Retry and backoff of the LLM client | The judge scripts replies at `_generate_response`. A scripted error waits 5 to 120 seconds before each retry. | A test with a fake clock: 4 attempts, the retried exception types, the wait bounds. |
| J4 | The LLM response cache | Off in the judge. | Record the cache key (`md5` of the model name and the sorted message JSON) for fixed inputs. |
| J5 | The provider wire format | The judge captures above the provider package. | The end-to-end layer runs the adapter with the official `OpenAI` package against an HTTP stub. The M1 quickstart runs against a real model. |
| J6 | Tracing spans and their attributes | Not captured. | Record span names and attribute keys from a run with an in-memory exporter. |
| J7 | The SQLite state store file | Only its effects are visible. | Record the table definitions and one row of each table after a fixed scenario. |
| J8 | Token usage tracking | The judge LLM client reports no usage. | Record tracker totals for scripted usage values. |
| J9 | Malformed JSON from a provider, empty responses, and refusals | They occur below the capture seam. | Covered with J5 in the end-to-end layer. |
| J10 | Rate limit behavior and `SEMAPHORE_LIMIT` under load | The judge compares results, not timing. | One end-to-end test that counts the highest number of calls in flight. |
| J11 | Data written by Python and read by .NET, and the reverse | Each judge run uses one build. | One end-to-end test that loads a graph dump recorded from Python and searches it with .NET. |

## Findings from the case writers

Plan 0002, tasks 5 to 7. Filled in after the agents report.
