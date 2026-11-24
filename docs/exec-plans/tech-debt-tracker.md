# Debt tracker

Each row has evidence and an exit criterion. After the exit criterion of a row is met, remove the row.

| Id | Gap | Evidence | Exit criterion |
| --- | --- | --- | --- |
| T1 | The devcontainer was never built. | No `devcontainer up` run on 2026-11-24. | A person opens the devcontainer and `scripts/check.sh --test` passes inside it. |
| T2 | The Claude cloud session hook was never run in a cloud session. | `scripts/session-start.sh` ran only with `CLAUDE_CODE_REMOTE` unset, where it exits at once. | A cloud session starts, `dotnet --version` prints 10.x, and `scripts/check.sh --test` passes. |
| T4 | No script lists the tests that kill no unique mutant. | [TESTING.md](../TESTING.md), "Known gaps". | Plan 0003 delivers the script. |
| T5 | Azure Cosmos DB Gremlin support for vector and full-text search is not verified. | The interview answer on 2026-11-24 was a belief, not a test. | The M6 spike report. |
| T7 | Upstream `tests/test_add_triplet.py` fails on Neo4j at the pinned commit (11 of 13). | `port/tools/upstream-tests.sh`, configuration C, 2026-11-24. | Plan 0002, task 6 decides how the file is used. Layer 3 scenarios cover `add_triplet`. |
| T8 | The upstream pytest run writes to the same Neo4j sidecar as the end-to-end tests and leaves data behind. The judge hosts delete their own groups. | One node remained after the baseline run. | `port/tools/upstream-tests.sh` cleans up after itself, or uses a second database. |
| T9 | 10 real-model scenarios are not recorded. | `port/judge/pending/flow-recorded.jsonl`. OpenAI answered `insufficient_quota` on 2026-11-24. | The scenarios are recorded and moved to `port/judge/cases/flow/`. |
| T10 | 13 upstream defects are not reported upstream. | `port/GAPS.md`, U1 to U13. | An issue exists upstream for each one, or the maintainer decides not to report it. |
| T11 | No scenario stores a node with a second label, because the label order is not stable. | `port/GAPS.md`, F13. | The maintainer decides on the `labels` noise rule at gate 2. |
| T6 | The fallback `apt-get install dotnet-sdk-10.0` in `scripts/setup-env.sh` was never run. | The script uses it only on a network that blocks `dot.net`. | T2 passes on a Limited-network cloud environment, or the fallback is removed. |
