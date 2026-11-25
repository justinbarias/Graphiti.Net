# 0003 Rulebook, PyCompat, and pilot

Port phases 4 and 5. Starts after gate 2. Ends at gate 3.

## Tasks

| # | Task | Owner, model, effort | Acceptance evidence |
| --- | --- | --- | --- |
| 1 | Done 2026-11-25. Copy the skill's `references/rules.md` to `port/RULES.md`. Add rules for this repository: naming, file layout, error types, logging, pydantic models and validators, async and cancellation, Cypher strings, prompts, and each answer in `port/GAPS.md`. | Lead, Fable 5.2, high | Every gap in `GAPS.md` maps to a rule. |
| 2 | Done 2026-11-25. Write the public surface design document: `AddGraphiti`, options, the `Microsoft.Extensions.AI` adapter, the cross-encoder over `IChatClient`. This is an ask-first item. | Lead, Fable 5.2, high | The maintainer approves it. |
| 3 | Done 2026-11-25. Create `src/Graphiti.Net.PyCompat` and `tests/Graphiti.Net.Property.Tests`. Build only the helpers that the queue needs. Generate expected values from Python into a file. | Workhorse, Sonnet 5.5, medium | CsCheck property tests pass against the Python values, including negative numbers, zero divisors, empty strings, an emoji, and a combining mark. |
| 4 | Write `scripts/mutation_survivors.py`. It reads the Stryker JSON report and lists the tests that the pruning rule in TESTING.md deletes. | Workhorse, Sonnet 5.5, medium | Run on the PyCompat report. Closes debt T4. |
| 5 | Run `scripts/mutate.sh` on PyCompat. Apply the pruning rule. | Lead, Fable 5.2, medium | The score and the list of deleted tests, in this plan. |
| 6 | Pilot. Port five units of different kinds using only `port/RULES.md`: `errors` (144 lines), `search.search_filters` (cycle group), `prompts.dedupe_nodes`, `driver.neo4j.operations.entity_node_ops`, `utils.content_chunking` (826 lines). | Implementers, Sonnet 5.5, medium. Opus 5.5 medium for the cycle group. | Build, format, and the covering judge cases pass for each unit. |
| 7 | Review the pilot. Two fresh reviewers for each unit. If they disagree, a third reviewer decides. | Reviewers, Opus 5.5, high | Findings with inputs. Each finding that shows a missing rule changes `RULES.md`. |
| 8 | Decide what to do with the pilot output. If a rule changed in a way that the output does not follow, delete the output. | Lead, Fable 5.2, medium | The decision, in `port/STATE.md`. |

## Gate 3

The maintainer approves `port/RULES.md`.

## Results

### Task 1, 2026-11-25

Lead session on Fable 5.2. No subagent.

Output:

- [port/RULES.md](../../../port/RULES.md): 25 repository rules (R1 to R25), the `PyCompat` helper list, the general rules of the skill with each replaced row pointing to its repository rule, the gap map, and 5 open items.
- [The rulebook design](../../design-docs/rulebook-design.md): the options and the reasons for decisions D20 to D23 and for six lead choices.
- The maintainer decided four items while the rulebook was written, each with a question that listed the options: D20 (wrong-type cases), D21 (the Python value model), D22 (the exclusive scheduler), D23 (the ambient service holder). [port/DECISIONS.md](../../../port/DECISIONS.md) records them.

Evidence for "every gap maps to a rule":

| Command | Result |
| --- | --- |
| A Python script reads each id in the tables of `port/GAPS.md` (`G`, `J`, `U`, `F`) and its row in Part 4 of `port/RULES.md` | 78 gaps in GAPS.md, 78 mapped, 0 missing, 0 extra. Each row names a rule that exists. 25 rules defined, 0 references to a rule that does not exist. |
| `grep` over the 114 MVP modules of `.upstream/` for the site lists | 17 `re` calls, 3 validators, 14 environment reads, 34 module loggers and 130 log calls, 71 `except NotImplementedError` clauses, 52 lines with `getattr` or `hasattr`, 20 exception classes |
| A script over the pure golden files for error class names | 107 `ValidationError`, 53 `ValueError`, 52 `KeyError`, 47 `TypeError`, 21 `AttributeError`, 13 `NotImplementedError`, 4 `ZeroDivisionError`, 3 `OverflowError`. 68 case ids hold `wrong_type`: 45 with a golden `TypeError` or `AttributeError`, 23 with another result. |

The gate:

| Command | Result |
| --- | --- |
| `scripts/check.sh --test`, first run | Failed in the docs step: a regular expression in a table cell of RULES.md had the shape of a markdown link. The cell was rewritten. |
| `scripts/check.sh --test`, second run | Docs, build, and format passed. The Neo4j step failed: `Bind for 127.0.0.1:7474 failed: port is already allocated`. This session runs in a git worktree, so Compose used a new project name while the sidecar of the main checkout held the ports. Debt T20. |
| `COMPOSE_PROJECT_NAME=graphitinet scripts/check.sh --test` | `check: passed`. `check_docs: ok (35 markdown files)`. Build: 0 warnings, 0 errors. Format: exit 0. Compose adopted the running sidecar and did not restart it (the configuration hash was equal). Tests: 1 passed, 0 failed. Judge: `cases=1999 passed=1 failed=1998`, `expected failures=1998 unexpected=0 fixed=0 bound_wrong=0`, `judge: passed`. |

The `verify` skill: steps 1, 2, and 5 ran as above. Steps 3 and 4 do not apply, because this change holds documents only and touches no ported code and no public flow.

### Adversarial review of task 1, 2026-11-25

The first results above said that a branch with documents only gets no adversarial pass. The maintainer asked for one, because the rulebook governs each later unit, and chose the reviewer.
One fresh Opus 5.5 subagent at high effort, briefed to refute the diff. It edited nothing. It ran 95 tool calls: `grep` over the 114 MVP modules, Python in the upstream virtual environment, and throwaway .NET 10 console projects.

Result: 16 important findings and 14 smaller ones. The lead confirmed the main ones against the golden files and the upstream source, and fixed each one in `port/RULES.md`.

| # | Finding | Evidence from the reviewer | Answer |
| --- | --- | --- | --- |
| I1 | The exclusive scheduler (D22) does not hold inside a library callback. | A second Polly attempt and a `Neo4j.Driver` work function ran on the thread pool. Upstream runs ported logic inside `session.execute_write`. | R8: each lambda that a library calls starts with `Asyncio.OnLoopAsync`. `GatherAsync` throws off the scheduler. The design document states the weak point. Open item O7. |
| I2 | `Node` and `Edge` equality cannot be a record, and the reference test was wrong. | `error CS0111` for `Equals(object)` in a record. Golden: a node equals an edge with the same UUID. In Python `e == e` is `False` for an edge. | R7 and R18: classes with the Python `__eq__` only. Containers use `Py.Contains` and the like, which test identity first. |
| I3 | A bad label in a model gives `ValidationError`, not `NodeLabelValidationError`. | 16 golden cases. Pydantic wraps a `ValueError` from a validator. | R7: `Pydantic.Wrap` around each validator. |
| I4 | `GraphProvider` and `EpisodeType` are plain `Enum` classes, so value text does not equal a member. | `get_default_group_id('falkordb')` gives `''` in Python, and the member gives `'_'`. | R22: such a case is a type rejection. The five cases moved to the open group of O4. |
| I5 | Rune iteration and `Encoding.UTF8` replace a lone surrogate. | Golden `pure.seed.lucene_sanitize.lone_surrogate` keeps it. | R12: `PyText.CodePoints` and `PyText.EncodeUtf8`. |
| I6 | Python escapes and line joins in prompt literals had no rule. | 7 prompt literals with a backslash at a line end. | R9: apply Python's string processing first. |
| I7 | Inner lines with trailing spaces are lost to editors. | 9 lines in 3 files. `.editorconfig` trims. | R9: such a line goes in an ordinary string. |
| I8 | No C# type for a typed `datetime`. A naive value must stay naive. | Golden `created_at_naive_kept_naive`. | R6 and R11: the `PyDateTime` struct for each `datetime`. |
| I9 | `Split(sep, n)` and `find` differ from Python. | `"a.b.c".Split('.', 1)` gives one part. Positions are code points. | R12: `PyText.Split`, `RSplit`, `Find`, `RFind`, `Index`. |
| I10 | `re.IGNORECASE` matches U+017F, U+0130, and U+0131 in Python only. | Tested in both runtimes. | R13: write the three letters out. |
| I11 | One iterated set was not listed. | `attribute_utils.py:256`. | R14: new row. |
| I12 | The retry log does not run after attempt 1. | `llm_client/client.py:123`. | R16: the exact condition. |
| I13 | `except Exception` as "not `OperationCanceledException`" lets an `HttpClient` timeout through. | `TaskCanceledException` derives from it. | R4: `Asyncio.IsCancellation(e, cancellationToken)`. |
| I14 | Constructs in MVP modules with no rule. | `uuid5`, `to_thread`, `unit_of_work`, provider errors, a discriminated union, `model_dump_json`, namespace `__getattr__`, `OrderedDict`, `combinations`, `zip_longest`, and others. | Rules added in R4, R5, R7, R8, R14, R15, R18. Two stay open: provider errors (O3) and `unit_of_work` in core code, which needs a driver contract member and is an ask-first item (O6). |
| I15 | Three environment reads run at call time, not at import. | `attribute_utils.py:63`, `openai_decisions_client.py:68`, `:84`. | R17: row split. |
| I16 | The pilot cannot follow the cycle rule or the skeleton rule. | `search.search_filters` is in cycle group 10. The skeleton comes after gate 3. | R19 and R24: one pilot exception with skeleton members. |

Smaller findings, all fixed: `\d` above U+FFFF, the wording on raw string literals, `list.sort` in place, tuple sorts with `reverse=True`, `defaultdict(int)`, module names equal to a class, `InternalsVisibleTo`, the order of rows in `Codec`, the exception type of each helper, the `Z` of pydantic's JSON mode, the scope of `using var span`, `MaxBy` with `nan`, and the counts that differ from GAPS.md.
Not fixed, only recorded: the reviewer's count of 23 more wrong-type cases with other ids was not counted again by the lead (debt T17).

The reviewer found no defect in the site counts, the 78-row gap map, the invariants of AGENTS.md, or these .NET claims: `OrderedDictionary` keeps a key's position on overwrite, `$` matches before a final line break, `OrderByDescending` is stable, and `MinBy` and `MaxBy` return the first of equal items.
The reviewer did not check pydantic lax coercion field by field, the tenacity wait formula, or the full Unicode tables.

No second review ran after the fixes.

The gate after the fixes: `COMPOSE_PROJECT_NAME=graphitinet scripts/check.sh --test` printed `check: passed`, with `check_docs: ok (35 markdown files)`, 0 build warnings, 1 test passed, and the judge at `expected failures=1998 unexpected=0 fixed=0 bound_wrong=0`. The gap map script printed 78 gaps, 78 mapped, and no reference to a rule that does not exist. The `verify` skill ran: steps 1, 2, and 5. Steps 3 and 4 do not apply to a change with documents only.

Not done and not checked:

- No C# code follows the rulebook yet. The pilot (task 6) is its first test.
- The rules that the review added (`OnLoopAsync`, `PyDateTime`, `Pydantic.Wrap`, identity-first containers) came from one review and have no code behind them yet.
- The site lists come from `grep` patterns. A site that the patterns missed is not listed. The rulebook tells an implementer to stop there.
- Open items O1 to O7 of the rulebook: debt T17, T18, T19, plan task 2, the driver contract member of O6, and the scheduler test of O7.
- Plan 0004 changed in one line: a skeleton member throws `NotSupportedException`, not `NotImplementedException` (rule R24).

### Task 2, 2026-11-25

Lead session on Fable 5.2. No subagent.

Output:

- [The public surface design](../../design-docs/public-surface-design.md): the `AddGraphiti` registration, the options, the facade `IGraphiti`, 59 public types, the chat adapter, the embedding adapter, the cross-encoder, prompt overrides and routing, the namespaces, and the rules for host code. After the review it lists 13 decisions, 13 lead choices, 21 checked facts, and 12 open questions.
- [port/DECISIONS.md](../../../port/DECISIONS.md): decisions D24 to D36, two library rows, and amendments to lead choices 2 and 3.
- [port/RULES.md](../../../port/RULES.md): the rule changes that the design needs, in R1, R2, R3, R4, R8, R15, R17, R19, Part 2, and Part 3. Open item O3 is closed. Open item O8 is new. The maintainer chose to make these changes in the same branch.
- [port/GAPS.md](../../../port/GAPS.md) (G6, G10) and [ARCHITECTURE.md](../../../ARCHITECTURE.md) point to the design.

The maintainer answered 16 questions in five rounds, each with the options, and approved the page.
The maintainer took the recommendation in 12 of them. The other four: all four extra surfaces for M1, a question back about a native OpenAI SDK, the approval itself, and the choice to change the rulebook in this branch and not in a separate change.
For the question back, the lead found the experimental `DecisionsClient` of `OpenAI` 2.15.0 and asked again with that fact (D32).
`port/queue.json` did not change, because no unit changed its status.

Findings that changed earlier records:

| Finding | Evidence | Effect |
| --- | --- | --- |
| `Microsoft.Extensions.AI.Abstractions` has no member for token log probabilities, and the OpenAI adapter copies none. | The Learn page of `ChatOptions`. `OpenAIChatClient.cs` on `main` of `dotnet/extensions` holds no line with `LogProb`. | D30: a public hook. Lead choice 2 amended. |
| The decisions client posts to `{base_url}/decisions` with `httpx`. | `llm_client/openai_decisions_client.py:191` | D32. Lead choice 3 replaced. |
| A public record and a ported class with one name cannot share the namespace `Graphiti`. | Rule R1 and decision D25 | D35: ported code moves to `Graphiti.Core`. |
| C# searches the parent namespace before a `using` directive at the top of a file. | A test project with the .NET 10 SDK: `error CS1503` with the directive at the top, a clean build with the directive inside the namespace. | Rule R1 states the placement. Open item O8 asks for a build check. |

Three numbers in the questions to the maintainer were wrong, and the page has the right ones: the namespaces have 75 calls, not about 60, the decisions client has 14 pytest tests, not 9, and ten settings stay environment variables, not nine. No number was the reason for an option.

The gate, before the adversarial review:

| Command | Result |
| --- | --- |
| A Python script over the gap ids of `port/GAPS.md` and Part 4 of `port/RULES.md` | 78 gaps, 78 mapped, 0 missing, 0 extra. 25 rules, 0 references to a rule that does not exist. 0 rows that still say "blocked until" the design. |
| `COMPOSE_PROJECT_NAME=graphitinet scripts/check.sh --test` | `check: passed`. `check_docs: ok (36 markdown files)`. Build: 0 warnings, 0 errors. Format: exit 0. Tests: 1 passed, 0 failed. Judge: `cases=1999 passed=1 failed=1998`, `expected failures=1998 unexpected=0 fixed=0 bound_wrong=0`, `judge: passed`. |

The `verify` skill: steps 1, 2, and 5 ran as above. Steps 3 and 4 do not apply, because this change holds documents only and touches no ported code and no public flow.

Not done and not checked:

- No code follows the design. Plan 0004 writes the facade, the records, and the adapters.
- One adversarial review ran. The section below gives its results. No second review ran after the fixes.
- The Learn pages of `Microsoft.Extensions.AI.Abstractions` show version 10.11.0. The review found each member in the 10.8.3 assemblies. Nobody inspected the 11.0.1 assembly (debt T21).
- Nobody tested what a provider adapter other than OpenAI's does with `ChatOptions.AdditionalProperties`. The design does not depend on it (lead choice L6).
- The scheduler hop of D33 and the hook of D30 have no code and no test yet.
- The 12 open questions of the design page. Three need the maintainer: the packaging of `PyCompat`, the library row for `IHttpClientFactory`, and the code fence step of the chat adapter.

### Adversarial review of task 2, 2026-11-25 and 2026-11-26

The maintainer chose the reviewer: one fresh Opus 5.5 subagent at high effort, briefed to refute the design page and the rule changes. It edited nothing.
It ran 101 tool calls: `grep` and `ast` reads of upstream, a walk over the golden flow files, reflection on the local package cache, and throwaway .NET 10 projects.

Result: 14 important findings and 18 smaller ones. The lead confirmed I1, I3, I12, I13, and S6 against the source, and took the measured results of the others from the report.

| # | Finding | Evidence from the reviewer | Answer |
| --- | --- | --- | --- |
| I1 | A judge embedding generator cannot tell `create` from `create_batch`. | Each of the 6 MVP `create` calls passes a list with one string. The golden flow files hold 197 batch entries and 57 one-item lists. | The ambient call value has an `Operation`: `embed` or `embed_batch`. The ported `create` parameter is `List<string>`. |
| I2 | The reranker was both a queue unit and lead code. | `port/queue.json`, order 57. The module imports `OpenAIClient` from a replaced module. | The unit stays ported. Its client is the lead seam `LogProbabilityChat`. Rule R19 gives the constructor and the call. |
| I3 | No rule for the default-object branches of `Graphiti.__init__`. | `graphiti.py:372`, `:385`, `:389`, `:393` | R1: each such branch throws `ArgumentException`. The facade always passes the four objects. |
| I4 | `_generate_response` does not receive the prompt name, so usage and the call value had no source for it. | `llm_client/client.py:152`. Only provider overrides record usage. | The chat adapter also overrides `generate_response`, sets the prompt name, and calls the base method. |
| I5 | Nothing made a classified server error retry. | `is_server_or_retry_error` knows a server error only as an HTTP status error. | The adapter throws `HttpRequestException` with status 500 and the provider exception inside. |
| I6 | R19 and R4 disagreed on the timeout of the decisions client. The default `HttpClient` timeout is 100 seconds, and upstream's is 120. | `openai_decisions_client.py:263` | R4 catches `TimeoutException` and the timeout form of `OperationCanceledException`. R19 sets the timeout of the default client. |
| I7 | `using Neo4j.Driver;` inside `namespace Graphiti.Neo4j.<Sub>;` fails. | `error CS0234` in a test project | R1: `using global::Neo4j.Driver;`. |
| I8 | The placement rule missed the judge host and the tests, and a wrong placement does not always fail the build. | `typeof(SearchConfig)` and `value is SearchFilters` compiled and bound to the public record. | R1 covers each project. Open item O8 and debt T22 ask for one build check for all C# files. |
| I9 | Public exception members with no docstring break the build. | `error CS1591` on `UnitInProgressException` | R1: two fixed XML comment texts for the two error files. |
| I10 | `IResponseModel` was not defined well enough. | The return types of `ModelValidate` conflicted. `prompts/lib.py:355` compares with `is`. | R7 holds the exact interface, one class, and one static object for each built-in model. |
| I11 | The judge cannot recover the `model` field from `ChatOptions.ModelId`. | All 187 golden `llm` entries have `"model": null`. | `ModelCallInfo` has `Model`, the override argument. |
| I12 | A default prompt library moves each call to another code path. | `graphiti_types.py:95`, `prompts/lib.py:545` | With no override and no route, the facade passes no library. |
| I13 | Routes with the default options fail, and routed usage is not visible. | `prompt_config.py:188` rejects an empty model id. | `Llm.Model` is required with routes. Lead choice L13: one shared tracker. The maintainer accepted it. |
| I14 | A read and a save of a naive date changes the stored value shape. | Python stores a naive date as a local value. | An ask-first item. The maintainer chose D36: read as UTC, save as a zoned value, and say so in the documents. |

Smaller findings, fixed: the count of environment variables (ten, not nine), the count of calls with a `driver` parameter (four, not five), the line references of the custom type table, the `title` and docstring rules of the schema text, the reranker details (default model, the limit of the gather, the token rule, 32-bit log probabilities, a cache in front of the client), the body bytes and headers of the decisions client, the dispose form of the singleton, the host parts that run in line, the thread contract of a public call, the order of `edge_type_map`, the inner-exception constructor, `Pydantic.Wrap` for a core exception type, the missing rows of the error table, judge fixtures as descriptors, and the property shapes that the page leaves to the mapping rules.
The maintainer also changed D24 on 2026-11-26: `IGraphiti` has no dispose member, and the container closes the implementation.

Not fixed, only recorded as open questions of the design page: plan 0004 gives the adapter to a workhorse (question 10), the attribute values that the Neo4j project can return (question 11), and class names that two ported namespaces share (question 12).

The reviewer found no defect in these claims: the upstream counts (13 public members, 75 namespace calls, 25 prompt names, 11 and 3 environment reads, 14 pytest tests, 16 recipes), the retry test, the reranker request, the options of concurrent ingestion, and each `Microsoft.Extensions.AI` member that the page names, in version 10.8.3.
It measured that the ambient call value reaches the thread pool and that the caller resumes on an exclusive scheduler.
The reviewer did not check the 11.0.1 assemblies, the `DecisionsClient` of `OpenAI` 2.15.0, any Microsoft Learn page, or whether IDE0065 fails a build for a file-scoped namespace.

No second review ran after the fixes.

The gate after the fixes, on 2026-11-26: `COMPOSE_PROJECT_NAME=graphitinet scripts/check.sh --test` printed `check: passed`, with `check_docs: ok (36 markdown files)`, 0 build warnings, 1 test passed, and the judge at `expected failures=1998 unexpected=0 fixed=0 bound_wrong=0`. The gap map script printed 78 gaps, 78 mapped, and no reference to a rule that does not exist.
The change holds documents only, so steps 3 and 4 of the `verify` skill do not apply.

### Task 3, 2026-11-25

Workhorse session on Sonnet 5.5. No subagent.

Scope. The task says to build only the helpers that the queue needs. A Python `ast` pass over the 114 MVP modules found the scalar and text operations that they use. The pass lists `//` and `%`, `/`, `int()`, `float()`, `len`, slices, `lower`, `capitalize`, `isupper`, `strip`, `lstrip`, `rstrip`, `split`, `splitlines`, `find`, `rfind`, `ord`, `encode`, `decode`, `combinations`, `zip_longest`, `uuid4`, and `uuid5`. These helpers are built. The first pass missed a call that names an imported function (`from math import comb`) and a call on a module (`time.time()`). The integrated review found both. The second pass reads each call form and found `comb` (1 call, in `content_chunking`), `time()` (26 calls in 9 modules), and `time.time()` (18 calls in 1 module). `Py.Comb` and `Py.TimeSeconds` are built. The Part 2 members that no module uses (`Round`, `Title`, `Upper`, `IsAlnum`, `RSplit`, `Index`, `Chr`) are not built. The second pass also found two operations that Part 2 does not name: `math.isnan` (1 call, `dedup_helpers.py`) and `str.count` with a substring (2 calls, the two `*_db_queries.py` files). Nobody built them, and the maintainer decides.

Built, all `internal` in `src/Graphiti.Net.PyCompat`, namespace `Graphiti.PyCompat`:

| Class | Members |
| --- | --- |
| `Py` | `TrueDiv` (long and double), `FloorDiv`, `Mod`, `Int` (double and text), `Float` (text), `ReprStr`, `Len`, `At`, `Slice` (string and list), `CompareStrings`, `StringComparer`, `Combinations`, `ZipLongest`, `Comb`, `TimeSeconds`, `Uuid4`, `Uuid4Hex`, `Uuid5`, `NamespaceUrl` |
| `PyText` | `Lower`, `Capitalize`, `IsUpper`, `Strip`, `LStrip`, `RStrip`, `Split` (whitespace and separator), `SplitLines`, `Find`, `RFind`, `CodePoints`, `Ord`, `EncodeUtf8`, `DecodeUtf8`, `DropLoneSurrogates`, and the internal `ToCodePoints`, `FromCodePoints`, `IsSpaceCodePoint` |
| `PyUnicodeData` (generated) | The Python 3.12 (Unicode 15.0) tables for case ranges, case mapping, and decimal digits |

Decisions that a reader must know:

- `Py.TrueDiv(long, long)` rounds the exact quotient once, as Python does. It matters above 2^53.
- A Python int outside the range of a `long` has no C# form here. `FloorDiv(long.MinValue, -1)`, `Int` of a float or text that is too large, throw `OverflowException`. The values file keeps Python's real result for each such case (field `big`, a decimal string), and the test asserts the exception. RULES.md R6 says `PyJson.Loads` gives `BigInteger`, and the helpers here do not.
- `int()` and `float()` remove the C whitespace below U+007F and the `str` whitespace above it. The separators U+001C to U+001F are not removed, which the first run of the tests showed.
- The Unicode tables come from Python, not from .NET, because .NET holds Unicode 16 and Python 3.12 holds Unicode 15. `lower()` follows CPython's final sigma rule.
- `Uuid5` uses SHA-1, as RFC 4122 requires. Analyzer CA5350 is off for this one project in its `.csproj`, with a comment. RULES.md R24 forbids `#pragma` and `[SuppressMessage]` and names no project setting. The maintainer can accept this or ask for another form.
- No Python string in the cases holds a high surrogate directly before a low surrogate, because a .NET string would join them.

Python values: `port/tools/generate_pycompat_values.py` runs Python 3.12.13 and writes `tests/Graphiti.Net.Property.Tests/python-values.json` (56,349 cases of 36 helpers over 134 strings, 4.7 MB, with the text of each Python error) and `src/Graphiti.Net.PyCompat/PyUnicodeData.g.cs`. The inputs include negative numbers, zero divisors, empty strings, an emoji (U+1F600), a combining mark (U+0301 after `e`), a family emoji joined with zero width joiners, flags, lone surrogates, a final sigma in six contexts, and the letters that changed in Unicode 16 (U+10D50). `--check` found both files current.

Tests: `tests/Graphiti.Net.Property.Tests` has 55 tests. 36 compare a helper with the recorded values. Each runs every case, then lets CsCheck draw 200 cases from the list. The test also compares the message of each error, except the encode errors. 19 are CsCheck laws that do not use Python, for example `a == FloorDiv(a, b) * b + Mod(a, b)`, `Join(Split(s, sep)) == s`, and the UTF-8 round trip.

Evidence:

| Command | Result |
| --- | --- |
| `COMPOSE_PROJECT_NAME=graphitinet scripts/check.sh --test` | `check: passed`. `check_docs: ok (36 markdown files)`. Build: 0 warnings, 0 errors. Format: exit 0. Tests: 56 passed (55 property, 1 end-to-end), 0 failed. This is the run after the integrated review. Judge: `selfcheck: 9 vectors, 0 differ`, `cases=1999 passed=1 failed=1998`, `expected failures=1998 unexpected=0 fixed=0 bound_wrong=0`. |
| Four hand-made mutants, each tested and then reverted | `q--` to `q++` in `FloorDiv`: caught by 2 tests. Final sigma test replaced by `true`: caught by `Lower` and `Capitalize`. `CompareStrings` length tie-break replaced by 0: caught by 2 tests. The skip length of a bad UTF-8 sequence set to 1: not caught, and the result is the same, because a continuation byte is always a bad start byte. |
| One failing test run | The first run failed on `int('\x1c5\x1f')`: Python raises `ValueError` and the helper returned 5. The fix is in `Py.Numbers.cs`. |

Adversarial review, Codex gpt-6-astra at high effort, read-only, 3 minutes 20 seconds. It reported 9 important findings and reproduced each with Python 3.12.13 and the .NET build.

| # | Finding | Answer |
| --- | --- | --- |
| 1 | The generator recorded `OverflowError` for Python ints that a long cannot hold, which changed the oracle to fit the code. | Fixed. The file keeps Python's real value in `big`. The test asserts `OverflowException`. The limit stays and is debt T25. |
| 2 | The one-ULP property rejected correct division (`1232334214084484197 / 5233191937471430191`), and two laws restated the implementation. | Fixed. The law now checks in exact integer arithmetic that the result is the nearest double. The `Int` law checks truncation properties and not the formula. |
| 3 | `"abc"[1::2**63-1]` overflowed the index. | Fixed with `Int128` and the slice length. Big steps and bounds are recorded cases now. |
| 4 | `Split` with a lone surrogate as separator cut an emoji in half. | Fixed. `Split` works on code points. Two surrogate separators are recorded cases. |
| 5 | A reverse slice of two lone surrogates (low, then high) joins them. | Not fixable in a .NET string. Documented in `PyText`, and recorded as debt T24 for the maintainer. |
| 6 | `ZipLongest<int,int>` gives 0 for a missing item. | Documented. The caller passes nullable items for a value type. Upstream's only use has reference types. |
| 7 | `Combinations` read the list lazily. | Fixed. It copies the list when called. A test pins it. |
| 8 | `int("0" * 4301)` is a `ValueError` in Python 3.12 (4,300 digit limit). | Fixed, with the Python message. Six recorded cases. |
| 9 | Error messages used a simplified `repr`. | Fixed. `Py.ReprStr` follows `repr(str)`, with a table of non-printable code points from Python. The tests compare each message. The rerun also found that `%` by zero has its own message, which is fixed. |

No second review ran after the fixes.

Not done and not checked:

- The helpers that need the value model are not built (debt T23). The pilot units need several of them, so task 6 cannot start before the maintainer decides the order.
- No mutation score for `PyCompat`. Tasks 4 and 5 run it.
- Nothing is committed, pushed, or in a pull request.
- `scripts/check.sh` does not run `generate_pycompat_values.py --check`. A change to `scripts/check.sh` needs its own pull request (invariant 9).
- No project references `Graphiti.Net.PyCompat` yet except the test project. The product projects get the reference in task 6.
- `port/queue.json` is unchanged: `PyCompat` is not a queue unit, and no unit changed state.

### Integrated review, 2026-11-26

One Opus 5.5 pass at high effort reviewed `git diff main...claude/plan-0003-pycompat-882eaf` on 2026-11-26. It found 4 important and 6 minor findings. The maintainer chose to fix all of them. The helpers matched Python 3.12 on 180,000 random inputs. The findings are around the helpers.

| # | Finding | Answer |
| --- | --- | --- |
| 1 | The plan said that no MVP module uses `Comb` or `TimeSeconds`. | Wrong. The first `ast` pass missed two call forms. The second pass found `comb` and `time` (see the scope paragraph of task 3). `Py.Comb` and `Py.TimeSeconds` are built. `Comb` has 279 recorded cases, with k = 0, k = n, k above n, negative arguments with their messages, and 6 results beyond a long (recorded as `big`). `TimeSeconds` has a law test. The pass also found `math.isnan` and `str.count`, which Part 2 does not name. They are not built. |
| 2 | Debt ids pointed to the wrong rows. | Fixed. `ARCHITECTURE.md` and the plan now point to T23 and T21 as the rows say. Debt T25 records that the helpers that return a `long` throw `OverflowException` where Python returns a larger int. The `grep` over `T21` to `T25` found no wrong reference. |
| 3 | Four mutants passed all 52 tests. | Fixed with recorded values. The generator adds `Aǅ` and similar strings for `IsUpper`, astral and surrogate number text, the floats 2^63 and -2^63 with their neighbors, and a probe for each range boundary of each table and each key of the case maps. Each code point also records `isupper` of `"A" + c`, `isprintable`, and `int(c)`. Each mutant now fails a test, see the next table. |
| 4 | The `int()` error message did not cut the repr at 200 characters. | Fixed. The repr is cut after 200 code points, as `%.200R` does. The values include texts whose repr has 199, 200, 201, and 302 characters, a `\x00` escape across the cut, and an astral letter on each side of it. The longest message has 240 characters. `Py.Float` has no cut and is unchanged. |
| 5 | The strict `DecodeUtf8` message always said "invalid utf-8". | Fixed. The message has Python's reason (`invalid start byte`, `invalid continuation byte`, `unexpected end of data`) and its byte or position range. The tests compare the message for each of 1,346 byte strings, hand-picked and random. They match on the first attempt. |
| 6 | The doc comment of `Combinations` said that a negative `k` throws at the first read. | Fixed. The call throws, as in Python. |
| 7 | The `using` directives stood above the namespace. | Moved inside the file-scoped namespace in each hand-written C# file of both projects. The format step accepts it. |
| 8 | Three lines were stale (`docs/TESTING.md`, `port/STATE.md` twice). | Each line is true now. The lead authorized one edit to `port/RULES.md`: the sentence in R3 about `InternalsVisibleTo`. |

Mutants, each applied alone and then reverted (`git diff` shows none left):

| Mutant | Failing tests |
| --- | --- |
| `IsUpper` without the titlecase check | `IsUpper_matches_Python`, `Single_code_point_tables_match_Python` |
| `NormalizeNumberText` without the astral and surrogate branch | `Int_of_text_matches_Python`, `Float_of_text_matches_Python` |
| `Py.Int(double)` with `t >= 2^63` changed to `>` | `Int_of_float_matches_Python` |
| `PyUnicodeData.Lower`: the range 0x61 to 0x7A cut to 0x79 | `Single_code_point_tables_match_Python` |
| `PyUnicodeData.DecimalDigitStarts`: 0x660 changed to 0x661 | `Int_of_text_matches_Python`, `Float_of_text_matches_Python`, `Single_code_point_tables_match_Python` |

R12, R1, R4, Part 2, the design's Errors table, and the dates of the rule-change tables are fixed in the task 2 layer.
