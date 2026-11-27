---
name: verify
description: Prove a Graphiti.Net change by running it, and report the evidence and the gaps. Use right before each commit, and when asked to verify, smoke test, or prove that a change works.
---

# Verify

A change is verified when you ran it and can show the output. Reading the code is not evidence.
Start each shell with `export COMPOSE_PROJECT_NAME=graphitinet PATH="$HOME/.dotnet:$PATH"`.

## Select the steps

| The change touches | Run the steps |
| --- | --- |
| Documentation only | 1 |
| A ported unit under `src/` | 1, 2, 3, 4 |
| The judge host, `tests/Graphiti.Net.JudgeHost` | 1, 2, 3 with no filter |
| A driver or a query | 1, 2, 3, 4, 5 |
| A public flow | 1, 2, 3, 5, and read "Not built yet" |
| A script under `scripts/` or `port/tools/` | 1, 2, then run the changed script |

## Steps, from cheap to costly

1. Docs, build, and format, with no database. The last line is `check: passed`.
   `scripts/check.sh`
2. All test projects and the whole judge on Neo4j. The last lines are `judge: passed` and `check: passed`.
   `docker compose up -d --wait neo4j`, then `scripts/check.sh --test`
3. The judge cases of your unit. The judge runs the built host, so build before each judge run.
   `dotnet build Graphiti.Net.slnx --nologo -v quiet`, then
   `python3 -B port/judge/run.py --build dotnet --filter <case id prefix>`
   A case id prefix is a layer and a case file, for example `pure.ranking`. Find your unit's cases by its `fn` in `port/judge/cases/`.
   The pass lines are `judge: expected failures=N unexpected=0 fixed=0 bound_wrong=0` and `judge: passed`.
   To see the failing ids, add `--list-failures`. That run prints only the ids, not the pass lines.
   If `fixed` is above 0, remove those ids from `port/judge/expected-failures.txt` (decision D38) and list them in the pull request.
4. The recorders of your unit. Each one runs the pinned upstream and compares its values file with Python.
   `$GRAPHITI_UPSTREAM/.venv/bin/python -B port/tools/record_<unit>.py --check`
   The pass line is `<values file>: current, N cases`. A worktree has no `.upstream`, so set `GRAPHITI_UPSTREAM` to the one of the main checkout.
   If the change touches a query or an index, also run `python3 -B scripts/db_schema.py --check`. The pass line ends with `is current`.
5. The end-to-end tests on Neo4j, after a build. The last lines are `failed: 0` and `skipped: 0`.
   `dotnet test --project tests/Graphiti.Net.E2E.Tests --no-build`

The detail is in [TESTING.md](../../../docs/TESTING.md), [the judge README](../../../port/judge/README.md), and [RELIABILITY.md](../../../docs/RELIABILITY.md).

## Not built yet

Name each gap that applies to your change in the report. Never claim it as verified.
The pull request of the layer that builds a feature also updates this table and the steps.

| Feature | Why verify cannot cover it now | Built by | Step that the skill gains |
| --- | --- | --- | --- |
| Ingestion | Not ported. No test ingests episodes in parallel. | Plan 0004, layer 8 | Step 5 gains the test that finds no lost update. |
| The `Graphiti` class, `AddEpisodeAsync`, `SearchAsync` | Not ported. All 34 flow cases of the judge fail as not bound. | Plan 0004, layer 9 | Step 3 covers the flow cases. Step 5 gains `AddEpisodeAsync`, then `SearchAsync`, then a read of the graph. |
| The quickstart in `examples/Quickstart` | The folder does not exist. It needs a real LLM key. | Plan 0005, task 4 | A step that runs the quickstart against Neo4j. |
| The mutation gate | `scripts/mutate.sh` runs in observe mode and cannot fail. | Plan 0003, task 5, then M3 of the spec | A step that runs `scripts/mutate.sh` and `python3 scripts/mutation_survivors.py`. |

## Rules

- If a check still fails after three fix attempts, stop and ask a human.
- Never skip a flaky test, add a sleep, or retry until it passes. Find the cause. If you cannot find it, delete the test and write the reason in `docs/exec-plans/tech-debt-tracker.md`.
- Never edit `port/judge/`, its golden files, or `.upstream/` to make a check pass. D38 is the one exception.
- Other sessions share the Neo4j of `graphitinet`. Never run `docker compose down`, and never delete data that is not yours.
- If Neo4j is not reachable, the test output says so and gives the command to start it.

## Report

- Each command that you ran, with its last lines.
- Each step of the table that you did not run, with the reason.
- Each row of "Not built yet" that applies to the change.
