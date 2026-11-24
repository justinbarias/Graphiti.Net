# 0001 Bootstrap

Status: completed 2026-11-24. Nothing is committed yet. The maintainer approves the first commit.

## Scope

Repository setup, agent guidance, development environments, test tooling, the product specification, and port phase 1.

## Acceptance criteria and results

| Criterion | Command | Result |
| --- | --- | --- |
| The solution builds with warnings as errors | `dotnet build Graphiti.Net.slnx -warnaserror` | 0 warnings, 0 errors |
| Formatting is clean | `dotnet format Graphiti.Net.slnx --verify-no-changes` | Exit 0 |
| The sidecar test passes on a real Neo4j | `dotnet test --solution Graphiti.Net.slnx` | 1 passed |
| The sidecar test fails with a useful message | The same command with `NEO4J_URI=bolt://localhost:7999` | Failed, and the message gave the start command |
| The documents are linked and within limits | `python3 scripts/check_docs.py` | Passed. A broken link and an unreachable file each made it fail. |
| The full gate passes | `scripts/check.sh --test` | Passed |
| The mutation configuration loads | `dotnet stryker --config-file stryker-config.json` | Loaded with the `mtp` runner. 0 mutants, because the product projects are empty. |
| The port queue exists | `build_queue.py .upstream/graphiti graphiti_core` | 182 modules, 47,677 lines, 27 modules in cycles |

## Decisions

The bootstrap interview produced decisions D1 to D15 in [port/DECISIONS.md](../../../port/DECISIONS.md).

## Not done

See the [debt tracker](../tech-debt-tracker.md): the devcontainer, the cloud session hook, and CI were written and not run.
