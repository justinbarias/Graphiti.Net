---
name: verify
description: Run Graphiti.Net end to end against a real Neo4j and show the evidence. Use before every commit, and when asked to verify, smoke test, or prove that a change works.
---

# Verify

A change is verified when you ran it and can show the output. Reading the code is not evidence.

## Steps

1. Start the graph database. The command waits until Neo4j is healthy.

   ```bash
   docker compose up -d --wait neo4j
   ```

2. Run the gate. It runs the cheap checks first (docs, build, format), then the tests.

   ```bash
   scripts/check.sh --test
   ```

3. If the change touches ported code, run the judge cases that cover the unit. `port/judge/README.md` gives the command. The judge does not exist before port phase 2.

4. If the change touches a public flow (`AddEpisodeAsync`, `SearchAsync`), drive that flow through an end-to-end test in `tests/Graphiti.Net.E2E.Tests`, and read the graph back from Neo4j to make sure that the data is there.

5. Report the commands you ran and the last lines of their output. Name every check you did not run.

## Rules

- If a check still fails after three fix attempts, stop and ask a human.
- Never skip a flaky test, add a sleep, or retry until it passes. Find the cause. If you cannot find it, delete the test and write the reason in `docs/exec-plans/tech-debt-tracker.md`.
- Never edit `port/judge/`, its golden files, or `.upstream/` to make a check pass.
- If Neo4j is not reachable, the test output says so and gives the command to start it.
