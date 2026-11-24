# Quality score

Evidence by area. A cell says what was run and when. "Not verified" means that no evidence exists.

| Area | Check | Last result | Date |
| --- | --- | --- | --- |
| Build | `dotnet build Graphiti.Net.slnx -warnaserror` | 0 warnings, 0 errors | 2026-11-24, macOS arm64, SDK 10.0.203 |
| Format | `dotnet format Graphiti.Net.slnx --verify-no-changes` | Exit 0 | 2026-11-24 |
| End-to-end | `dotnet test --solution Graphiti.Net.slnx` against Neo4j 5.26.2 | 1 passed, 0 failed | 2026-11-24 |
| Failure message | The same test with `NEO4J_URI=bolt://localhost:7999` | Failed with the "Neo4j is not usable" message and the start command | 2026-11-24 |
| Documents | `python3 scripts/check_docs.py` | See [plan 0001](exec-plans/completed/0001-bootstrap.md) | 2026-11-24 |
| Differential judge | None | Not verified. The judge does not exist. | |
| Property tests | None | Not verified. The project does not exist. | |
| Mutation score | `dotnet stryker --config-file stryker-config.json` | The configuration loads with the `mtp` runner. Stryker found 0 mutants, so no score exists. | 2026-11-24 |
| Devcontainer | None | Not verified. Debt T1. | |
| Claude cloud session | None | Not verified. Debt T2. | |
| CI | None | Not verified. Debt T3. | |
| Product code | None | No ported code. 0 of 114 MVP units done. | |

Open gaps are in the [debt tracker](exec-plans/tech-debt-tracker.md).
