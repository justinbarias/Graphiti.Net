# 0003 Rulebook, PyCompat, and pilot

Port phases 4 and 5. Starts after gate 2. Ends at gate 3.

## Tasks

| # | Task | Owner, model, effort | Acceptance evidence |
| --- | --- | --- | --- |
| 1 | Done 2026-11-25. Copy the skill's `references/rules.md` to `port/RULES.md`. Add rules for this repository: naming, file layout, error types, logging, pydantic models and validators, async and cancellation, Cypher strings, prompts, and each answer in `port/GAPS.md`. | Lead, Fable 5.2, high | Every gap in `GAPS.md` maps to a rule. |
| 2 | Write the public surface design document: `AddGraphiti`, options, the `Microsoft.Extensions.AI` adapter, the cross-encoder over `IChatClient`. This is an ask-first item. | Lead, Fable 5.2, high | The maintainer approves it. |
| 3 | Create `src/Graphiti.Net.PyCompat` and `tests/Graphiti.Net.Property.Tests`. Build only the helpers that the queue needs. Generate expected values from Python into a file. | Workhorse, Sonnet 5.5, medium | CsCheck property tests pass against the Python values, including negative numbers, zero divisors, empty strings, an emoji, and a combining mark. |
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
