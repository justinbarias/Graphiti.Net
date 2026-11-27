# Testing

This is the canonical home of the test strategy. It follows
[How the Claude Code team thinks about verification](https://claude.dev/blog/how-the-claude-code-team-thinks-about-verification/) (read 2026-11-24).

## Principles

1. Verification means that you ran the change. Use the `verify` skill in `.claude/skills/verify`.
2. A test must be hard to fool. If the feature is broken, the test must fail.
3. Write no test that restates the implementation. Write no test that asserts on a mock's call list.
4. Fakes exist only at the network boundary: the LLM and embedding HTTP endpoints. The graph database is always a real Neo4j.
5. A flaky test gets a root cause or gets deleted. Never skip it, add a sleep, or retry it. Write the reason for a deletion in [the debt tracker](exec-plans/tech-debt-tracker.md).
6. A change cannot modify the check that judges it.
7. Run cheap checks first. `scripts/check.sh` runs docs, build, and format before any test.

## The four layers

| Layer | Question it answers | Location | State |
| --- | --- | --- | --- |
| Differential judge | Does .NET behave like Python at the pinned commit? | `port/judge/`, `tests/Graphiti.Net.JudgeHost` | 1,999 cases: 1,965 pure and 34 flow. 11 flow scenarios use replies from a real model. |
| End-to-end | Does a real user flow work against a real graph? | `tests/Graphiti.Net.E2E.Tests` | One environment test. |
| Property | Does a statement hold for thousands of generated inputs? | `tests/Graphiti.Net.Property.Tests` | 55 tests for `PyCompat`: each helper against the values that Python recorded, and laws that hold for any input. |
| Mutation | Does each test catch a bug that no other test catches? | `scripts/mutate.sh`, `stryker-config.json` | Configured in observe mode. Two runs on `PyCompat` on 2026-11-26 each scored 93.22%. The pruning rule is not applied yet (plan 0003, task 5). |

### Differential judge

Python is the oracle. A recorder script runs upstream at the pinned commit and saves golden files.
The golden files are committed, so CI and cloud sessions need no Python.

The judge compares three things for the same input:

- The requests that each build sends to the LLM and the embedder: messages, response schema, and model options.
- The Cypher queries and parameters that each build sends to the database.
- The result that each build returns after both receive the same scripted model replies.

The judge prints its case count. If the count is lower than `port/judge/count.txt`, the judge fails.
`port/judge/expected-failures.txt` starts with every case and must be empty at M1.
After gate 2, `port/judge/` is read-only, and `.claude/settings.json` denies edits to it from that point.
The [judge design document](design-docs/judge-design.md) gives the reasons, and [the judge README](../port/judge/README.md) is the contract.

### End-to-end

Each test drives a public flow, for example `AddEpisodeAsync` then `SearchAsync`, and then reads the graph back from Neo4j.
All tests share the `Neo4jSidecar` fixture. Each test creates a unique group id with `Neo4jSidecar.NewGroupId()` and touches only that group.
A small HTTP stub supplies LLM and embedding replies to the official `OpenAI` package. The stub replays the judge's reply files and arrives in plan 0004.

`EnvironmentTests` guards the environment itself. It proves that the sidecar supports full-text and vector search.
It has no product code to mutate, so the mutation rule does not apply to it.

### Property

Use CsCheck. Good properties for this code base:

- Round trip: a node or edge that is saved and loaded is equal to the original.
- Idempotency: the dedupe step applied twice gives the same graph as the step applied once.
- Invariant: a fact's `invalid_at` is never earlier than its `valid_at`. An invalidated fact is never deleted.
- Equivalence: `PyCompat` helpers match values that Python computed and stored in a file. `port/tools/generate_pycompat_values.py` writes `tests/Graphiti.Net.Property.Tests/python-values.json` and `src/Graphiti.Net.PyCompat/PyUnicodeData.g.cs` from the Python 3.12 of the upstream virtual environment. Run it with `--check` to see whether the files are stale. A case holds a result or a Python error name, and the test asserts the C# exception type from [RULES.md](../port/RULES.md) R4.

### Mutation

Stryker.NET changes the product code in small ways and runs the tests. A mutant that no test kills shows a gap.
`disable-bail` is on, so the report lists every test that kills each mutant.

The pruning rule:

1. Run `scripts/mutate.sh`. Then run `python3 scripts/mutation_survivors.py`, which reads the newest `StrykerOutput/<run>/reports/mutation-report.json`.
2. If a test kills no mutant, delete the test.
3. If every mutant that a test kills is also killed by a broader test, delete the narrower test.
4. If a mutant survives in code that matters, add an end-to-end or property test. Do not add a unit test.

The script lists the tests of steps 2 and 3 and the mutants of step 4. It deletes nothing.
It reads "broader" as one other test that kills every mutant of the narrower test, and more.
Tests that kill the same mutants, with no broader test, are not in the list. The script shows each such group, and a person decides.
It exempts `EnvironmentTests`. `--json` prints the same result for a program.

Limits of the list:

- Property tests use random inputs, so the list can differ between two runs of the same code. Compare two runs before you delete a test.
- The rule compares killed mutants only. A test that compares values with Python can be listed because another test runs through the same code.
- With no per-test data, for example a killed mutant with no `killedBy`, the script prints no list. Keep `disable-bail` on, because the script cannot see that a report is incomplete.
- A timeout mutant names no killer in the reports of 2026-11-26. If one test covers it, that test gets the credit. If more tests cover it, none gets credit, and a listed one has a note.
- A static mutant has an empty `coveredBy` in the reports of 2026-11-26, so its `killedBy` can be incomplete. The script counts these mutants.
- The result holds only for the code that the run mutated.
- A test that stops the test host during the coverage capture gives no result for any mutant. Stryker cannot read the coverage of such a test, so it runs the test against each mutant, and each mutant gets the status `RuntimeError`. The instrumented build uses more stack, so the two deep tests of `PyAstTests.cs` run their body on a thread with a large stack (`OnLargeStack`). One other test there holds the default stack, and it does not run in an instrumented build. If a run gives only errors, look for `Failed to capture per-test coverage` in its log. A test that stops the host only for one mutant gives `RuntimeError` for that mutant only.

The gate is in observe mode: `thresholds.break` is 0 and the CI job cannot block a merge.
After M1 gives a baseline score, the maintainer sets an enforced threshold (M3 in the [spec](product-specs/spec.md)).

## Temporary port evaluation tests

Upstream has 990 pytest tests. During the port they are ported to xUnit under `tests/Port.Evaluation`.
They judge the port and nothing else. They are not part of the permanent suite, and M3 deletes them.
Upstream tests that mock internal classes are ported as they are, because their only job is to compare behavior with Python.

## Commands

```bash
scripts/check.sh --test
```

```bash
scripts/mutate.sh
```

```bash
python3 scripts/mutation_survivors.py
```

## Known gaps

- Nobody applied the pruning rule yet. Plan 0003, task 5 does it.
- Only `PyCompat` has product code, so no mutation score exists for ported code.
