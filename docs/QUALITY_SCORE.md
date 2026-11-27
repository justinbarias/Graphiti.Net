# Quality score

Evidence by area. A cell says what was run and when. "Not verified" means that no evidence exists.

| Area | Check | Last result | Date |
| --- | --- | --- | --- |
| Build | `dotnet build Graphiti.Net.slnx -warnaserror` | 0 warnings, 0 errors | 2026-11-24, macOS arm64, SDK 10.0.203 |
| Format | `dotnet format Graphiti.Net.slnx --verify-no-changes` | Exit 0 | 2026-11-24 |
| End-to-end | `dotnet test --project tests/Graphiti.Net.E2E.Tests` against Neo4j 5.26.2 | 46 passed, 0 failed. The tests save and load each node and edge type and compare the result with Python (plan 0004, layer 5). | 2026-11-27 |
| Failure message | The same test with `NEO4J_URI=bolt://localhost:7999` | Failed with the "Neo4j is not usable" message and the start command | 2026-11-24 |
| Documents | `python3 scripts/check_docs.py` | See [plan 0001](exec-plans/completed/0001-bootstrap.md) | 2026-11-24 |
| Differential judge, Python build | `python3 port/judge/run.py --build python` | 1,999 cases, 1,999 passed, in two full runs. The flow layer passed 10 more runs. Each run had a different hash seed. | 2026-11-25 |
| Differential judge, .NET build | `python3 port/judge/run.py --build dotnet` | 1,229 passed, 770 expected failures, 0 unexpected, 0 bound and wrong | 2026-11-27 |
| Break tests of the judge | `python3 port/tools/break_tests.py` | Control passed. 20 of 20 breaks caught. | 2026-11-25 |
| Score ties in flow cases | `port/tools/find_score_ties.py - port/judge/cases/flow/*.jsonl` | 0 ties in 34 cases | 2026-11-25 |
| Adversarial review of the judge | One Opus 5.5 subagent, high effort | 6 important findings: 4 fixed, 1 decided at gate 2 (D17), 1 closed in part (debt T12 and T16). See [port/STATE.md](../port/STATE.md). | 2026-11-25 |
| Upstream test baseline | `port/tools/upstream-tests.sh` | 1026 passed without a database, 40 passed on Neo4j | 2026-11-24 |
| Rulebook gap map | A script reads each gap id in `port/GAPS.md` and its row in Part 4 of `port/RULES.md` | 78 gaps, 78 mapped, each to a rule that exists. See [plan 0003](exec-plans/active/0003-rulebook-and-pilot.md). | 2026-11-25 |
| Adversarial review of the rulebook | One Opus 5.5 subagent, high effort | 16 important findings and 14 smaller ones. All fixed in `port/RULES.md`, with two left as open items (O3, O6). No second review. See [plan 0003](exec-plans/active/0003-rulebook-and-pilot.md). | 2026-11-25 |
| Public surface design | The maintainer's approval, after 16 questions with options | Approved, with decisions D24 to D36. No code follows it yet. See [plan 0003](exec-plans/active/0003-rulebook-and-pilot.md). | 2026-11-26 |
| Adversarial review of the public surface design | One Opus 5.5 subagent, high effort | 14 important findings and 18 smaller ones. All 14 fixed or decided, 3 smaller ones left as open questions. No second review. See [plan 0003](exec-plans/active/0003-rulebook-and-pilot.md). | 2026-11-26 |
| Property tests | `scripts/check.sh --test` | 55 tests pass for the PyCompat scalar and text helpers, against 4.7 MB of values that Python 3.12.13 recorded. See [plan 0003](exec-plans/active/0003-rulebook-and-pilot.md). | 2026-11-25 |
| Mutation score | `scripts/mutate.sh`, then `python3 scripts/mutation_survivors.py` | `PyCompat`, two runs: 93.22% each. 59 mutants survived and 3 had no coverage. The script listed 13 and then 14 of 56 tests. No test is deleted yet. See [plan 0003](exec-plans/active/0003-rulebook-and-pilot.md). | 2026-11-26 |
| Devcontainer | None | Not verified. Debt T1. | |
| Claude cloud session | None | Not verified. Debt T2. | |
| CI | `.github/workflows/ci.yml` on GitHub, push to `main` | `check` and `mutation` jobs passed | 2026-11-25 |
| Product code | None | No ported code. 0 of 114 MVP units done. | |

Open gaps are in the [debt tracker](exec-plans/tech-debt-tracker.md).
