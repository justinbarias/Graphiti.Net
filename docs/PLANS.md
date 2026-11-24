# Plans

An execution plan is a file in `exec-plans/`. It holds scope, tasks, acceptance criteria, decisions, and results.

## Lifecycle

1. A plan starts in `exec-plans/active/` with a number and a short name.
2. Each task names its model and effort, its acceptance evidence, and its expected values.
3. The person or agent that does a task records the commands and the results in the plan.
4. If every acceptance criterion passes, move the plan to `exec-plans/completed/` and update this index.
5. Work that a plan leaves open goes to the [debt tracker](exec-plans/tech-debt-tracker.md) with an exit criterion.

## Active

| Plan | Port phases | Depends on |
| --- | --- | --- |
| [0002 Judge and source preparation](exec-plans/active/0002-judge-and-source-prep.md) | 2, 3 | Gate 1 |
| [0003 Rulebook, PyCompat, and pilot](exec-plans/active/0003-rulebook-and-pilot.md) | 4, 5 | 0002 |
| [0004 Translate the core](exec-plans/active/0004-translate-core.md) | 6 | 0003 |
| [0005 Parity, cutover, and release](exec-plans/active/0005-parity-and-release.md) | 7, 8 | 0004 |

## Completed

| Plan | Result |
| --- | --- |
| [0001 Bootstrap](exec-plans/completed/0001-bootstrap.md) | Repository, guidance, environments, tooling, port phase 1 |
