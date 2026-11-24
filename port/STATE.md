# Port state

Read this file and [queue.json](queue.json) before any port work. Update both before you stop.
The porting skill is `port-python-to-dotnet`. Its standing rules apply in every phase.

## Now

| Item | Value |
| --- | --- |
| Phase | 2. Build the judge |
| Gate | Gate 1 passed on 2026-11-24. Gate 2 is next: the maintainer reviews the judge and the break tests. |
| Last action | 2026-11-24: the maintainer approved the judge design. Built the upstream Python environment and recorded the test baseline (plan 0002, tasks 1 and 2). |
| Next action | [Plan 0002](../docs/exec-plans/active/0002-judge-and-source-prep.md), task 3. The maintainer approved the split: the lead builds the hosts and the runner, and Sonnet 5.5 agents write cases. |

## Queue counts

182 units. All have status `todo`. 114 are in MVP scope. See the scope table in [DECISIONS.md](DECISIONS.md).

## Judge

The runner, both hosts, and the noise rules work. [The judge README](judge/README.md) is the contract, and [judge-design.md](../docs/design-docs/judge-design.md) gives the reasons.
The case set is not complete, and the break tests have not run.

## Upstream test baseline

`port/tools/upstream-tests.sh`, 2026-11-24: 1026 passed and 14 skipped without a database, 40 passed on Neo4j, and `tests/test_add_triplet.py` has 11 failures on Neo4j that upstream CI does not see.

## Blocked units

None.

## Rule changes

None. `port/RULES.md` does not exist yet. Phase 4 writes it.
[GAPS.md](GAPS.md) holds 22 language gaps and 11 surfaces that the judge cannot reach. [TYPE_HINTS.md](TYPE_HINTS.md) holds the 127 signatures that upstream leaves without hints.

## Deviations from the porting skill

| Skill text | What this port does | Why |
| --- | --- | --- |
| Scaffold in phase 4 | The solution, the Neo4j sidecar test, and the tooling exist from the bootstrap. `PyCompat` still waits for phase 4. | The maintainer asked for the development tooling first. |
| Port the unit tests with each module | pytest ports live in `tests/Port.Evaluation` and are temporary. | Decision D6. |
| Lead model is Opus | The lead session ran on Fable 5.2. | The maintainer's global rules name Fable as orchestrator. |
| Phase 3 completes type hints in the Python source | The source is not changed. `port/tools/infer_type_hints.py` writes the signatures that pyright infers into `TYPE_HINTS.md`. | The oracle stays identical to the pinned commit. |
| The .NET host is in `port/judge/` | It is `tests/Graphiti.Net.JudgeHost`. | Each ported unit adds a binding, and `port/judge/` is read-only after gate 2. |

## Open questions

None.

## Checks not run

- No judge run, because the judge does not exist.
- No mutation score. Stryker loaded its configuration and found 0 mutants, because the product projects contain no code yet.
- The devcontainer and the Claude cloud session hook were not started. See [docs/RELIABILITY.md](../docs/RELIABILITY.md).
