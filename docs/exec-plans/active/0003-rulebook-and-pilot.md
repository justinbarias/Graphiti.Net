# 0003 Rulebook, PyCompat, and pilot

Port phases 4 and 5. Starts after gate 2. Ends at gate 3.

## Tasks

| # | Task | Owner, model, effort | Acceptance evidence |
| --- | --- | --- | --- |
| 1 | Done 2026-11-25. Copy the skill's `references/rules.md` to `port/RULES.md`. Add rules for this repository: naming, file layout, error types, logging, pydantic models and validators, async and cancellation, Cypher strings, prompts, and each answer in `port/GAPS.md`. | Lead, Fable 5.2, high | Every gap in `GAPS.md` maps to a rule. |
| 2 | Done 2026-11-25. Write the public surface design document: `AddGraphiti`, options, the `Microsoft.Extensions.AI` adapter, the cross-encoder over `IChatClient`. This is an ask-first item. | Lead, Fable 5.2, high | The maintainer approves it. |
| 3 | Done 2026-11-25. Create `src/Graphiti.Net.PyCompat` and `tests/Graphiti.Net.Property.Tests`. Build only the helpers that the queue needs. Generate expected values from Python into a file. | Workhorse, Sonnet 5.5, medium | CsCheck property tests pass against the Python values, including negative numbers, zero divisors, empty strings, an emoji, and a combining mark. |
| 4 | Done 2026-11-26. Write `scripts/mutation_survivors.py`. It reads the Stryker JSON report and lists the tests that the pruning rule in TESTING.md deletes. | Workhorse, Sonnet 5.5, medium | Run on the PyCompat report. Closes debt T4. |
| 5 | Done 2026-11-28. Run `scripts/mutate.sh` on PyCompat. Apply the pruning rule. | Lead, Fable 5.2, medium | The score and the list of deleted tests, in this plan. |
| P1 | Done 2026-11-26. Add the `.editorconfig` entry that makes IDE0065 an error for each C# file. Move the `using` directives of the judge host and the end-to-end tests inside the namespace. Closes open item O8 and debt T22. | Lead, Fable 5.2, medium | A file with a directive at the top fails the build. The gate passes. Own pull request. |
| P2 | Done 2026-11-26. Build the value model of `PyCompat`: `PyDict`, `PySet<T>`, `PyTuple`, `Py.Repr`, `Py.Str`, `Py.Truthy`, `Py.Len`, `Py.Get`, `Py.As*`, `Py.EnumValue`. | Workhorse, Opus 5.5, medium | Property tests pass against values that Python 3.12 recorded. |
| P3 | Done 2026-11-26. Build `PyJson` (`Dumps`, `Loads`). Needs P2. | Workhorse, Sonnet 5.5, high | Property tests pass against values that Python 3.12 recorded. |
| P4 | Done 2026-11-26. Build `PyDateTime`. | Workhorse, Sonnet 5.5, high | Property tests pass against values that Python 3.12 recorded. |
| P5 | Done 2026-11-26. Build `PyRegex`, `PyRandom`, and `PyEnv`. | Workhorse, Sonnet 5.5, medium | Property tests pass against values that Python 3.12 recorded. |
| P6 | Done 2026-11-26. Build `Pydantic`. Needs P2 and P4. | Workhorse, Opus 5.5, medium | Property tests pass against values that pydantic recorded at the pinned version. |
| P7 | Done 2026-11-26, except `Fixtures.cs` (debt T18). Judge host: add the decoders for a `$model`, `$enum`, `$ref`, and `$dt` argument. Add the encoder for a `$type` result and the exception rows of open item O2. Write `Fixtures.cs` (R22). Needs P2 and P6. Closes O2 and debt T18. | Lead, Fable 5.2, medium | The gate passes, and the judge case count stays 1999. Own pull request. |
| P8 | Done 2026-11-26. Runner change for `JudgeTypeRejected` (open item O1, decision D20). The lead writes a patch file outside `port/judge/`. The maintainer applies it. | Lead, Fable 5.2, medium | `run.py --build python` passes each case, and the case count stays 1999. Own pull request. |
| P9 | Done 2026-11-26. Write the script in `port/tools/` that records `str(exception)` for the `errors` unit (R23, J1). The project `tests/Port.Evaluation` comes with the `errors` unit of the pilot, because a test project with no test fails the test step. | Lead, Fable 5.2, medium | The script records from `.upstream` with Python 3.12. |
| 6 | Done 2026-11-26. Pilot. Port five units of different kinds using only `port/RULES.md`: `errors` (144 lines), `search.search_filters` (cycle group), `prompts.dedupe_nodes`, `driver.neo4j.operations.entity_node_ops`, `utils.content_chunking` (826 lines). A unit also ports the members of other modules that R24 names. Needs P1 to P9. | Implementers, Sonnet 5.5, medium. Opus 5.5 medium for the cycle group. | Build, format, and the covering pure judge cases pass for each unit. `entity_node_ops` has no pure case: build and format only, and its 3 flow cases wait for plan 0004, layer 9. |
| 7 | Done 2026-11-26. Review the pilot. Two fresh reviewers for each unit. If they disagree, a third reviewer decides. | Reviewers, Opus 5.5, high | Findings with inputs. Each finding that shows a missing rule changes `RULES.md`. |
| 8 | Done 2026-11-26. Decide what to do with the pilot output. If a rule changed in a way that the output does not follow, delete the output. | Lead, Fable 5.2, medium | The decision, in `port/STATE.md`. |

## Gate 3

The maintainer approves `port/RULES.md`.

On 2026-11-26 the lead passed this gate under decision D39, after the five pilot units merged. The gate log of [DECISIONS.md](../../../port/DECISIONS.md) gives the reasons. The maintainer reads the rulebook at the end of the run.

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

### Task 4, 2026-11-26

Workhorse session on Sonnet 5.5. No subagent built the script.

Output: [scripts/mutation_survivors.py](../../../scripts/mutation_survivors.py), Python standard library only. Its docstring and [TESTING.md](../../TESTING.md), "Mutation", state how it applies the rule and the limits. Debt T4 is closed. `stryker-config.json` did not change.

Four choices that the rule does not state, for the lead to confirm in task 5:

- "Broader" in step 3 means one other test whose killed mutants are a strict superset. Several tests that only together cover a test do not count.
- Tests that kill the same mutants, with no broader test, are not listed. The script shows them as a group for a person.
- A timeout mutant with no named killer and one covering test counts as a kill of that test.
- `EnvironmentTests` is exempt by file name, as TESTING.md says that the rule does not apply to it.

Two runs on the same code, each with `COMPOSE_PROJECT_NAME=graphitinet scripts/mutate.sh` and then `python3 scripts/mutation_survivors.py`:

| Value | Run 1 | Run 2 |
| --- | --- | --- |
| Report folder in `StrykerOutput/` (not committed) | `2026-11-26.10-02-46` | `2026-11-26.10-04-25` |
| Score | 93.22% | 93.22% |
| Killed / survived / no coverage / timeout / ignored | 809 / 59 / 3 / 44 / 167 | 808 / 59 / 3 / 45 / 167 |
| Compile error / static | 54 / 3 | 54 / 3 |
| Listed tests, of 56 | 13 | 14 |
| Exempt tests | 1 | 1 |

The 62 surviving and uncovered mutants were the same in both runs: 36 in `Py.Numbers.cs`, 15 in `PyText.cs`, 9 in `Py.Sequences.cs`, and 2 in `Py.Uuid.cs`.

Each listed test is listed by step 3. Apart from the exempt test, each test killed a mutant. Neither run showed an equal group. The 13 tests of run 1:

| Class | Tests |
| --- | --- |
| `NumberTests` | `Comb_obeys_Pascals_rule_and_symmetry`, `Int_of_a_float_cuts_toward_zero`, `TrueDiv_of_ints_is_the_nearest_double_to_the_exact_quotient` |
| `SequenceAndUuidTests` | `Combinations_count_is_n_choose_k`, `Combinations_reads_the_list_when_it_is_called`, `ZipLongest_is_as_long_as_the_longer_input` |
| `StringTests` | `EncodeUtf8_matches_Python`, `Full_slice_with_step_minus_one_reverses_code_points`, `Join_undoes_Split_on_a_separator`, `Slice_of_a_list_matches_Python`, `Slice_of_the_whole_string_is_the_string`, `Strip_leaves_no_whitespace_at_either_end`, `Utf8_round_trips_a_string_without_lone_surrogates` |

Run 2 listed the same 13 and `CompareStrings_is_zero_only_for_equal_strings_and_is_antisymmetric`.

For task 5:

- The list depends on the run, because CsCheck draws random inputs. A test is safe to delete only if two runs list it.
- `EncodeUtf8_matches_Python` is listed because `Uuid5_of_the_url_namespace_matches_Python` kills its one mutant. `Slice_of_a_list_matches_Python` is listed because `Slice_of_a_string_matches_Python` kills the same mutants and more. Each is a direct comparison with Python for its own helper.
- A script over the report of run 1 found 0 killed mutants that lose every killer when all 13 listed tests go, and 0 timeout mutants that only listed tests cover. No run without those tests checked this.

A report that anyone can recompute. Save this as `report.json` in a temporary folder:

```json
{"schemaVersion":"2","thresholds":{"high":80,"low":60},"projectRoot":"/p",
 "files":{"/p/src/Calc.cs":{"language":"cs","source":"","mutants":[
  {"id":"1","mutatorName":"Arithmetic","replacement":"a - b","status":"Killed","location":{"start":{"line":3,"column":5},"end":{"line":3,"column":10}},"coveredBy":["t1"],"killedBy":["t1"]},
  {"id":"2","mutatorName":"Equality","replacement":"a >= b","status":"Killed","location":{"start":{"line":7,"column":5},"end":{"line":7,"column":10}},"coveredBy":["t1","t2"],"killedBy":["t1","t2"]},
  {"id":"3","mutatorName":"Boolean","replacement":"false","status":"Killed","location":{"start":{"line":11,"column":5},"end":{"line":11,"column":9}},"coveredBy":["t3"],"killedBy":["t3"]},
  {"id":"4","mutatorName":"String","replacement":"\"\"","status":"Survived","location":{"start":{"line":15,"column":5},"end":{"line":15,"column":9}},"coveredBy":["t3"]}]}},
 "testFiles":{"/p/tests/CalcTests.cs":{"tests":[{"id":"t1","name":"AddsAndCompares"},{"id":"t2","name":"ComparesOnly"},{"id":"t3","name":"Flags"}]}}}
```

`python3 <repository>/scripts/mutation_survivors.py report.json` printed this and exited 0:

```text
report: report.json
score: 75.0% (killed=3 survived=1 no_coverage=0 timeout=0 ignored=0)
other: compile_error=0 runtime_error=0 pending=0 static=0

tests to delete: 1 of 3
  ComparesOnly (tests/CalcTests.cs)
    rule 3: AddsAndCompares kills all 1 of its mutants and 1 more

surviving and uncovered mutants: 1
  src/Calc.cs:15:5 Survived String: ""
```

Changes to that report, applied in a throwaway script:

| Change | Result |
| --- | --- |
| `t1` and `t2` kill mutants 1 and 2 | No test listed. `AddsAndCompares` and `ComparesOnly` shown as an equal group |
| Only `t1` kills mutant 2 | `ComparesOnly` listed, rule `2` |
| Mutant 1 killed by `t1` and `t3`, mutant 3 by `t2` and `t3`, mutant 2 by `t1` and `t2` | No test listed |
| Mutant 1 has no `killedBy` | No list: "1 killed mutants have no `killedBy`" |
| No `testFiles` | No list: "the report lists no tests" |
| A file that is not JSON, or a missing file | A message on stderr, exit 2 |


### Adversarial review of task 4, 2026-11-26

The maintainer chose the reviewer: one fresh Opus 5.5 subagent at high effort, briefed to refute the diff.
It found each number in the documents equal to the two reports, and the printed output of the hand-built report equal to the plan.
It found no wrong result on the real reports. The findings come from inputs that those reports do not hold.

| # | Finding | Answer |
| --- | --- | --- |
| I1 | The `3-equal` rule listed a test that rule 3 keeps, because an equal test is not broader. | Fixed. Equal tests are shown as a group and are not in the list. |
| I2 | The only test that covers a timeout mutant could be listed as a test that kills nothing. | Fixed. That test gets the credit. With more covering tests, the note stays. |
| I3 | A report made with bail on gives a wrong list, and the script cannot see it. | Not fixed. TESTING.md and the docstring state the limit. |
| M | "First by name" sorted by file. A Windows path broke the exemption. Duplicate test ids were dropped. The project root was cut as a plain prefix. A deeply nested file gave a traceback. The newest report went by file time. | Fixed. The newest report is now the last run folder name. |
| M | Three wordings in the plan, STATE.md, and TESTING.md. | Fixed. |

Not changed: every file named `EnvironmentTests.cs` is exempt, and a readable JSON file with a wrong shape gets the "cannot read" message and exit 2.

After the fixes, both reports gave the same 13 and 14 listed tests, no equal group, and the same score and survivors.
The hand-built report printed the same output. The variants in the table above ran again, and three new inputs (one covering test for a timeout, a duplicate id, a root that is only a text prefix) gave the new behavior.
No second review ran after the fixes.

The gate after the fixes: `COMPOSE_PROJECT_NAME=graphitinet scripts/check.sh --test` printed `check: passed`, with 56 tests passed and the judge at `expected failures=1998 unexpected=0 fixed=0 bound_wrong=0`. `scripts/command_center.sh --check` exited 0. The `verify` skill ran: steps 1, 2, and 5. Steps 3 and 4 do not apply, because this change touches no ported code and no public flow.

### Pre-pilot scan, 2026-11-26

The lead scanned the five pilot units before task 6. The scan read the syntax tree of each module with `ast`, and it counted the pure judge cases and their golden results by module.
It did not read each line of each unit.

| Unit | Pure cases | Golden results | Needs that did not exist |
| --- | --- | --- | --- |
| `errors` | 19 | 19 values | `Py.Str` for a list. The project `tests/Port.Evaluation` and a recording script (R23, J1). |
| `search.search_filters` | 133 | 108 values, 23 `ValidationError`, 2 `ValueError` | `Pydantic`, `PyDateTime`, `PyDict`, `Py.EnumValue`. It calls `helpers.validate_node_labels`. |
| `prompts.dedupe_nodes` | 12 | 9 values, 3 `KeyError` | `Pydantic`, `PyDict`, `PyJson`. Each case calls `to_prompt_json` and `Message`. |
| `driver.neo4j.operations.entity_node_ops` | 0 | 3 flow cases in `flow/direct.jsonl` | The flow layer of the judge host, which plan 0004, layer 9 binds. |
| `utils.content_chunking` | 179 | 163 values, 13 `TypeError`, 2 `AttributeError`, 1 `ValueError` | `PyJson`, `PyRegex`, `PyRandom`, `PyEnv`, `PySet<T>`, `PyTuple`, `Py.As*`, `Py.Truthy`. It reads 4 constants of `helpers` and `EpisodeType`. |

Findings:

- The pilot needs most of the value-model helpers of debt T23. Tasks P2 to P6 build them. It does not need `Py.Format`, `PyAst`, `PyLruCache`, `PyHash`, `PyText.Replace`, `str.count`, or `math.isnan`.
- 19 `content_chunking` cases have `wrong_type` in the id. 14 of them have a golden `TypeError` or `AttributeError` and pass only as `JudgeTypeRejected` (task P8). 5 have a golden value and stay expected failures until gate 4 (open item O4).
- The judge host had no decoder for a `$model`, `$enum`, `$ref`, or `$dt` argument and no encoder for a `$type` result. `Codec.cs` had 48 lines. Task P7 adds them.
- R24 made each name from a module outside the pilot a skeleton member that throws. A case that reaches such a member cannot pass. All 12 `dedupe_nodes` cases reach one.
- The earlier sentence in debt T23 that the pilot needs `str.count` was wrong. R19 ports `search.search_filters` alone, and `entity_node_ops` only builds.

The maintainer decided four points on 2026-11-26. They are decisions D37 and D38. D39 is the instruction to continue through plan 0004, layer 6, with standing approvals:

| Point | Decision |
| --- | --- |
| Members of modules outside the pilot | The pilot ports them for real. R19 and R24 name them. |
| `entity_node_ops` | It stays in the pilot. Its acceptance is build and format only. |
| Open item O5 | First answer: the lead writes a patch file, and the maintainer applies it. Changed on the same day (D38), because the runner fails the gate while a listed case passes. The lead removes the passing ids in the pull request of the unit, after the maintainer narrows the deny rule for that one file. |
| Open item O1 | The lead writes the runner change as a patch file outside `port/judge/`. The maintainer applies it. |

Not checked: the exact `Pydantic` members that the units use, whether `entity_node_ops` needs `Asyncio`, and whether a `generate_covering_chunks` case reaches `random.sample`.

### Task P1, 2026-11-26

`.editorconfig` has `csharp_using_directive_placement = inside_namespace:error` and `dotnet_diagnostic.IDE0065.severity = error` for each C# file.
`dotnet build Graphiti.Net.slnx -warnaserror` then failed with `error IDE0065` for 5 files: 2 in the end-to-end tests and 3 in the judge host.
`dotnet format style Graphiti.Net.slnx --diagnostics IDE0065 --severity error` moved the directives, and the build passed.
A probe file in `src/Graphiti.Net.PyCompat` with `using System.Text;` above the namespace failed the build with `error IDE0065`. The probe file is deleted.
`Program.cs` of the judge host has top-level statements and no namespace, and the rule does not report it.
No adversarial review ran, because the change is configuration and formatter output.

### Task P9, 2026-11-26

`port/tools/record_error_messages.py` writes `tests/Port.Evaluation/values/errors.json`: 173 cases of the 15 classes of `graphiti_core/errors.py`, from Python 3.12.13.
Each case holds the class name, the arguments, `str(exception)`, the `message` attribute, and the base classes.
The arguments include an empty text, non-ASCII text, both quote characters, a line break, an emoji, a combining mark, lists with 0, 1, and 2 items, and the integers 0, 7, -1, and 2^63 - 1.
`--check` reported that the file is current. The run changed nothing under `.upstream/`.
The project `tests/Port.Evaluation` is not created here. The `errors` unit of the pilot creates it with its first test.

### Task P8, 2026-11-26: the patch is written, and the maintainer applies it

`port/patches/judge-type-rejected.patch` changes `port/judge/run.py` and `port/judge/README.md`. The lead wrote it on a copy outside `port/judge/`, because sessions cannot edit that folder.

| Change | Reason |
| --- | --- |
| A `JudgeTypeRejected` answer of the .NET host passes if the golden result is `TypeError` or `AttributeError`. | Decision D20. |
| A case in `expected-failures.txt` can fail with `JudgeTypeRejected`, and not only with `JudgeNotBound`. | Decision D40. The runner fails the gate if a bound unit has a listed case with another failure. The cases of open item O4 give a value in Python, and the C# type cannot hold their argument. |

Checks on the changed copy, with the real case files and golden files:

- A stand-in host answered `JudgeTypeRejected` for each `wrong_type` case of `pure.chunking`, a wrong value for one case, and `JudgeNotBound` for the others. 14 cases passed (golden `TypeError` or `AttributeError`). 5 cases stayed expected failures. The wrong value failed as `bound_wrong`.
- With the real .NET host and all 1999 cases, the result did not change: `expected failures=1998 unexpected=0 fixed=0 bound_wrong=0`, `judge: passed`.
- `git apply --check` passed on `main`.

Not run: the changed runner against the Python build.
The maintainer applies the patch with `git apply port/patches/judge-type-rejected.patch` on a branch. It goes in its own pull request.

### Tasks P2, P4, P5, and the `Asyncio` helpers, 2026-11-26

Each group was built by one subagent in its own worktree, from a brief that pointed at the rule sections. Each group has a generator in `port/tools/` that records values from Python 3.12.13, and tests that compare the helpers with them.
One Opus 5.5 high adversarial review ran on each group before its pull request. The value model had two, because its second half was added after the first one.

| Group | Pull request | Builder | Review findings | Notes |
| --- | --- | --- | --- | --- |
| `PyEnv`, `PyHash`, `PyLruCache`, `PyRandom` (P5) | 11 | Sonnet 5.5, medium | 6, with 3 important | Five members that nothing calls were removed. `PyRandom` is a seeded xoshiro256** source and does not give Python's sequence (GAPS F9). |
| `PyDateTime` (P4) | 13 | Sonnet 5.5, high | 6, with 1 important | `FromIsoFormat` matched Python on 107 extra probes of the reviewer. `StrFTime`, `StrPTime`, and subtraction were removed, because no MVP module calls them. |
| `PyRegex`, `PyText.Replace`, `PyText.Count` (P5) | 14 | Sonnet 5.5, high | 4, with 2 important | `Word` and `Boundary` changed under `IgnoreCase`, because .NET folds case with Unicode 16. Each constant now sits in `(?-i:...)`. All 17 sites of R13 matched Python on 24,080 texts after that. |
| `Asyncio`, `ExclusiveScheduler` (debt T23) | 15 | Opus 5.5, medium | 8, with 2 important | Exclusivity held in 100 stress runs of the reviewer. Two findings are rules and not code: R8 has them now, and open item O9 holds the one that needs the maintainer. |
| The value model and the list helpers (P2) | 16 | Opus 5.5, medium | 9, with 1 important, then 8, with 1 important | Float text matched Python on 1,246,333 doubles. The second review found that `Py.SortInPlace` did not give Python's order for 64 or more items with `nan` keys. It is now a port of the list sort of CPython 3.12. No review ran on that port. Its evidence is 2,816 recorded sorts up to 5,000 items. |

Decisions of the lead during these tasks, under decision D39:

- An enum member carries `[PyEnumMember(name, value)]`. A registry needed a call before first use, and the `<Enum>Values` class of R7 was a second source of the same values. R7 has the new rule.
- A member that no MVP module calls is not built. The rulebook rows for `strftime`, `strptime`, datetime subtraction, `\S`, `\W`, and `lstrip(chars)` say so.
- The test project takes each `python-values*.json` file with one line.
- A pull request merges after the CI job `check` passes. The job `mutation` is in observe mode and never blocks a merge, by the comment in `.github/workflows/ci.yml`. It took 15 min 57 s on pull request 14 (debt T27).

Not done in these tasks: `PyJson`, `PyAst` (P3), and `Pydantic` (P6). The next section holds them. `scripts/mutate.sh` did not run on the new helpers outside CI, and task 5 (the pruning rule) waits for P3 and P6.

### Tasks P3, P6, P7, and P8, 2026-11-26

One subagent built each group in its own worktree. One Opus 5.5 high adversarial review ran on each group before its pull request, and the same builder fixed the findings. No second review ran on a fix round.

| Group | Pull request | Builder | Review findings | Notes |
| --- | --- | --- | --- | --- |
| `PyJson`, `PyAst` (P3) | 18 | Sonnet 5.5, high | 5, with 1 important | The reviewer's generator compared `Dumps` on 677,500 cases and `Loads` on 200,105 texts. A bad `\N` escape threw the wrong exception type. A complex number and `...` throw `ArgumentException`, so that the caller takes its fallback path. `PyStrEnumAttribute` was removed. 318 property tests. |
| `Pydantic` (P6) | 20 | Opus 5.5, medium | 8, with 1 important | No difference in 17,812 random field cases, 1,100 mutated replies, and six schemas that the reviewer wrote by hand. `LoadJson` had no nesting limit, and 3000 nested objects crashed the test host. The text of each of the 49 `schema_text` judge cases matches the golden text. 366 property tests. |
| The judge host codec (P7) | 19 | Opus 5.5, medium | 11, with 3 important | Each JSON int decoded as a `BigInteger`, and a value tuple had no encoding. By the reviewer's count, about 240 and 199 pure cases with a golden value would fail for these two reasons. The Neo4j error table followed driver 5.x and follows 6.1.0 now. The fix round added `InvokeAsync`, which the lead read. 147 tests. |
| The runner change (P8) | 21 | Lead | None | The maintainer applied the patch. The changed runner passed each of the 1999 cases against the Python build, on a copy of the judge folder. The earlier section "Task P8" holds the other checks. |

The maintainer also narrowed the deny list of `.claude/settings.json` with a script (pull request 22, decision D38).

Decisions of the lead during these tasks, under decision D39:

- A binding names the Python parameters and gives the ported method to `InvokeAsync`. The first codec needed one branch for each combination of defaults, or a default value in the binding, and `Bindings.cs` forbids both.
- An exception type with no row in `Codec.PythonExceptionName` gives a `host_error`. The first codec returned the C# type name, which no golden file holds.
- `Fixtures.cs` is not written. It needs the public type `GraphTypeDefinition`, which no merged unit has yet (debt T18).
- The codec review found 23 more wrong-type cases for open item O4. They stay expected failures by decision D40.
- The values file of `PyJson` is 8.5 MB. The review asked for a smaller file, and the builder cut 0.8 MB.

Not done: no review ran on the fix rounds, and no review ran on the runner change.

### Task 6, unit `errors`, 2026-11-26

One Sonnet 5.5 medium implementer ported `graphiti_core/errors.py` to `src/Graphiti.Net/Errors.cs` (15 public classes) from the rulebook alone, with `tests/Port.Evaluation/ErrorsTests.cs`.
The test compares the message and the base class of 172 of the 173 recorded cases. `GraphitiError('a', 'b')` has no C# form.
Two Opus 5.5 high reviewers read the unit with no contact. Each compared its own cases with Python (306 and 30) and found no difference in behavior. Each broke the port, and a test failed each time.

| Finding | Reviewer | Result |
| --- | --- | --- |
| R4 gave no form for a class with no `__init__` that is called with no argument, and no form for a default before `innerException`. | The implementer, and A | The lead chose D41. R4 has the two constructor forms. |
| The reason that R4 gave for the single base class of `NodeLabelValidationException` was false: pydantic catches the class as a `ValueError` in two validators. | A and B | R4 names the two sites and points to R7. The error table of the public surface design has the exception. |
| R1 had no XML comment text for the constructor with `innerException`, and no rule for the licence text. | B, and the implementer | R1 has both. |
| The recording script does not record `group_id`, `unit_id`, and `seq`, and no list item that `repr` must escape. | B | Open. The lead extends the script with the unit's pull request. |
| `port/queue.json` and `port/STATE.md` did not name the unit. | B | The lead updates both with the unit's pull request. |

The unit is not merged. Its 19 judge bindings need the codec, which merged after the port.

### Tasks 6 and 7, the other four units, 2026-11-26

One implementer ported each unit from the rulebook alone. Two Opus 5.5 high reviewers then read the unit with no contact. The same implementer did one fix round.
The lead verified each fix round with the gate, the evaluation tests, and the judge. No second review ran on the fixes.

| Unit | Implementer | Judge cases | Evidence beside the judge | Pull request |
| --- | --- | --- | --- | --- |
| `search.search_filters` | Opus 5.5, medium | 147 of 150 pass. 3 answer `JudgeTypeRejected` (O4). | 4 upstream tests, and 3 error lists compared with pydantic | 25 |
| `prompts.dedupe_nodes`, with `prompts.models` and `prompts.prompt_helpers` | Sonnet 5.5, medium | 46 pass. 1 is not bound: it names a model of `prompts.extract_nodes`. | 1,209 recorded Python results | 28 |
| `utils.content_chunking` | Sonnet 5.5, medium | 190 of 197 pass. 7 answer `JudgeTypeRejected` (O4). | The upstream tests, and 1,505 recorded Python results | 29 |
| `driver.neo4j.operations.entity_node_ops` | Sonnet 5.5, medium | None. Its 3 flow cases wait for plan 0004, layer 9. | 94 recorded calls through a fake executor, for the 5 methods that do not stop at a skeleton member | 30 |

What the reviews found:

| Finding | Unit | Result |
| --- | --- | --- |
| When a bad node label met another bad field, pydantic reported both errors, and the port reported one. Rule R7 ran a field validator after the field reads. | `search_filters` | Fixed before the merge. R7: the validator runs inside the read of its field. |
| `to_prompt_json(5, indent=-2**40)` gave `OverflowException` from a cast that Python does not have. | `dedupe_nodes` | `PyJson.Dumps` takes a `long?` indent. R11 changed. |
| `generate_covering_chunks` with a chunk size of 0 or 1 returns the pair chunks in insertion order, where CPython returns the order of its hash table. | `content_chunking` | Accepted as decision D43. The maintainer decides at gate 4. |
| `Py.Comb` returned a `long` and threw for `comb(100, 20)`. | `content_chunking` | It returns a `BigInteger`. R18 changed. |
| A `long` that left its range wrapped in silence. | `content_chunking` | The product projects compile with `CheckForOverflowUnderflow` (decision D44). R4 changed. |
| The ported upstream tests found none of 13 seeded defects. | `content_chunking` | R25: a unit with logic delivers a recording of Python results. In the fix round the recording found 4 of 4 seeded defects, and the judge found 1 of 4. |
| 10 upstream tests replace a module constant with `monkeypatch.setattr`. | `content_chunking` | `PyPatch<T>` (decision D42, R17). |
| `Neo4j.Driver` 6.5.1 throws `ProtocolException` for a nested `OrderedDictionary`, a `PyDateTime`, a tuple, a `BigInteger`, and an enum. R6 said that the driver accepts the value model. | `entity_node_ops` | R6 and R9: the Neo4j project converts each parameter value at the driver boundary. Layer 5 builds the conversion in `Neo4jDriver`. |
| A bad `name_embedding` value in a record gave an error with no class name and no field name. `Node` has `validate_assignment=True`. | `entity_node_ops` | Fixed. R7: the type check at an assignment of open data. |
| `Transaction.RunAsync` returned the result tuple. None of the 150 upstream `tx.run` sites reads a result. | `entity_node_ops` | It returns `Task`. R9 changed. |
| A skeleton `Node` had reference equality. With it, a later unit passes its tests with the wrong meaning. | `entity_node_ops` | The unit ports the fields, the label validation, `__eq__`, and `__hash__` of `Node`, and `utc_now`. R24 changed. |

The adversarial review of the support pull request (27) had 11 findings, and each one is fixed there.
The largest one: `PyPatch<T>` had an implicit conversion, and with it `X == null`, `X.Equals(v)`, and `var x = X` read the holder in silence. The conversion is removed, and code reads `.Value`.

Rule changes: the history table of [RULES.md](../../../port/RULES.md) has one row for each round. It has three new open items:

- O10: a test that replaces a module-level function. The maintainer decides before layer 7.
- O11: judge host additions. The lead builds them before layer 2.
- O12: two prompt library shapes. The lead decides before layer 4.

### Task 8, 2026-11-26

The lead keeps the output of each pilot unit.

- `errors` and `search_filters` merged before the last rule changes. The support pull request moved the members of the three search filter models to the order of R7. No other rule change touches the two units.
- `dedupe_nodes`, `content_chunking`, and `entity_node_ops` applied each changed rule in their fix rounds, before their merge.
- The rules that changed after the last fix round write down what the output already does.

### Task 5, the mutation runs and the pruning rule, 2026-11-28

Two runs of `scripts/mutate.sh` on `PyCompat`, on the machine of the lead (14 cores, 35 minutes each):

| | Run 1, with other work on the machine | Run 2, with no other test or gate |
| --- | --- | --- |
| Score | 89.87% | 89.87% |
| Killed | 5,924 | 5,899 |
| Survived | 546 | 548 |
| Timeouts | 385 | 412 |
| Tests that the script lists | 224 of 445 | 229 of 447 |

- The timeouts do not come from load: the quiet run has more of them. They belong to the mutants.
- The list is stable. Both runs name the same 71 of 74 test methods, and 218 listed rows match by name. Each run names 3 methods that the other does not.
- In both runs, 9 tests kill no mutant (step 2), for example `Digit_matches_Python_for_each_code_point`, `Word_matches_Python_for_each_code_point`, and `EncodeUtf8_matches_Python`. About 209 rows are listed by step 3, and about 160 of them are rows of 8 data-driven tests, for example `Script_trace_matches_asyncio`.

The list of deleted tests is empty. The maintainer decided on 2026-11-28 to delete no test: each listed test compares the port with a value recorded from Python, and a mutation run cannot see that worth. `docs/TESTING.md` now exempts these tests from steps 2 and 3.

A run on GitHub Actions was started and cancelled on the same day, because a runner with 4 cores takes hours. The manual workflow `mutation` is still not measured (debt T27).
