# Reliability

How each environment starts, how to diagnose it, and what was verified.

## Environments

All four environments run `scripts/setup-env.sh` and use `docker-compose.yml` for Neo4j at `bolt://localhost:7687`.

| Environment | How setup runs | Verified |
| --- | --- | --- |
| Local machine | Run `scripts/setup-env.sh` by hand. | Yes, macOS arm64, 2026-11-24. The SDK install step was skipped because SDK 10.0.203 was present. |
| Devcontainer | `postCreateCommand` runs `scripts/setup-env.sh --python`. The `dev` container shares the network namespace of the `neo4j` service. | No. Debt T1. |
| Claude cloud session | The SessionStart hook in `.claude/settings.json` runs `scripts/session-start.sh`. | No. Debt T2. |
| CI | `.github/workflows/ci.yml` runs `scripts/setup-env.sh`, then `scripts/check.sh --test`. | Yes, 2026-11-25, on a push to `main`. |

A second workflow, `.github/workflows/upstream-release.yml`, is not one of the four environments. It runs `port/tools/upstream_diff.py --list` one time each week, with no setup script and no database.
[port/UPSTREAM.md](../port/UPSTREAM.md) gives its purpose. It did not run on GitHub: its shell steps ran on a local machine on 2026-11-28.

## Claude cloud sessions

Facts from the [cloud environment documentation](https://code.claude.com/docs/en/cloud-environments), read 2026-11-24:

- A cloud session runs in an Ubuntu 24.04 x86_64 VM. The base image cannot be replaced, so a cloud session cannot run from the devcontainer image.
- The .NET SDK is not pre-installed. Docker and `docker compose` are.
- A cloud session with one repository runs the hooks in the repository's `.claude/settings.json`. A cloud thread inside a project does not.
- A setup script in the cloud environment dialog runs before Claude starts. If the script ends within about five minutes, its result is cached.

What this repository does:

- If `CLAUDE_CODE_REMOTE` is not `true`, `scripts/session-start.sh` exits at once. In a cloud session it installs the SDK into `~/.dotnet`, writes `PATH` to `CLAUDE_ENV_FILE`, and starts Neo4j in the background.
- The devcontainer uses the Ubuntu 24.04 base image to stay close to the cloud VM. A local Apple silicon machine builds an arm64 container. The cloud VM is x86_64.

To make cloud sessions start faster, paste this line into the environment's setup script. The SDK and the Neo4j image then come from the environment cache.

```bash
scripts/setup-env.sh && docker compose pull neo4j
```

For a project's cloud threads, that setup script is required, because threads do not run repository hooks.
If the network level blocks `dot.net`, `scripts/setup-env.sh` falls back to the Ubuntu package `dotnet-sdk-10.0`. That path is not verified (debt T6).

## Diagnostics

| Symptom | Check |
| --- | --- |
| A test fails with "Neo4j is not usable at ..." | Run `docker compose up -d --wait neo4j`, then `docker compose logs neo4j`. |
| `dotnet` is not found in a cloud session | Run `bash scripts/setup-env.sh`, then `export PATH="$HOME/.dotnet:$PATH"`. Read `/tmp/neo4j-up.log` for the sidecar. |
| The SDK version is refused | `global.json` needs SDK 10.0.x. Run `dotnet --list-sdks`. |
| A commit is refused | The pre-commit hook runs `scripts/check.sh`. Read its output. |

## Runtime behavior of the library

Not applicable yet. After the port, this section records retry policy, concurrency limits (`SEMAPHORE_LIMIT` upstream), and LLM rate-limit behavior.
