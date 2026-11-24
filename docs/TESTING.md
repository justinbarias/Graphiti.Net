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
| Property | Does a statement hold for thousands of generated inputs? | `tests/Graphiti.Net.Property.Tests` | Not created. Port phase 4. |
| Mutation | Does each test catch a bug that no other test catches? | `scripts/mutate.sh`, `stryker-config.json` | Configured in observe mode. No product code to mutate yet. |

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
- Equivalence: `PyCompat` helpers match values that Python computed and stored in a file.

### Mutation

Stryker.NET changes the product code in small ways and runs the tests. A mutant that no test kills shows a gap.
`disable-bail` is on, so the report lists every test that kills each mutant.

The pruning rule:

1. Run `scripts/mutate.sh`. Open `StrykerOutput/<run>/reports/mutation-report.json`.
2. If a test kills no mutant, delete the test.
3. If every mutant that a test kills is also killed by a broader test, delete the narrower test.
4. If a mutant survives in code that matters, add an end-to-end or property test. Do not add a unit test.

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

## Known gaps

- No tool finds the tests that the pruning rule deletes. A person reads the JSON report. Plan 0003 adds a script.
- Stryker ran one time on 2026-11-24 and found 0 mutants, because the product projects are empty. No mutation score exists yet.
