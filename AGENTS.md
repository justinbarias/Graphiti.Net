# Graphiti.Net

Graphiti.Net is a .NET 10 port of [Graphiti](https://github.com/getzep/graphiti), the temporal context graph library by Zep.
The port is pinned to upstream 0.32.0 ([port/UPSTREAM_PIN](port/UPSTREAM_PIN)).
Gate 2 closed on 2026-11-25, so the judge in `port/judge/` is read-only. The product projects contain no ported code yet.

## Read first

| Need | Document |
| --- | --- |
| What to build, roadmap, MVP goal | [docs/product-specs/spec.md](docs/product-specs/spec.md) |
| Where the port stands now | [port/STATE.md](port/STATE.md) |
| Port scope and library choices | [port/DECISIONS.md](port/DECISIONS.md) |
| Python constructs with no direct C# form, and upstream defects | [port/GAPS.md](port/GAPS.md) |
| How each Python pattern becomes C#. Only the lead changes it. | [port/RULES.md](port/RULES.md) |
| The judge: commands, protocol, noise rules | [port/judge/README.md](port/judge/README.md) |
| Current work | [docs/PLANS.md](docs/PLANS.md) |
| System layout and boundaries | [ARCHITECTURE.md](ARCHITECTURE.md) |
| Test layers and the mutation rule | [docs/TESTING.md](docs/TESTING.md) |
| What a reviewer must look for | [REVIEW.md](REVIEW.md) |
| Design decisions and principles | [docs/DESIGN.md](docs/DESIGN.md) |
| Product intent | [docs/PRODUCT_SENSE.md](docs/PRODUCT_SENSE.md) |
| Evidence by area | [docs/QUALITY_SCORE.md](docs/QUALITY_SCORE.md) |
| Environments and diagnostics | [docs/RELIABILITY.md](docs/RELIABILITY.md) |
| Trust boundaries and secrets | [docs/SECURITY.md](docs/SECURITY.md) |
| User-facing surfaces | [docs/FRONTEND.md](docs/FRONTEND.md) |
| External references | [docs/references/index.md](docs/references/index.md) |

## Commands

```bash
scripts/setup-env.sh              # install the pinned .NET SDK, restore tools and packages
scripts/fetch-upstream.sh         # clone upstream at the pinned commit into .upstream/
docker compose up -d --wait neo4j # start the graph database
scripts/check.sh                  # docs, build, format
scripts/check.sh --test           # the same, then the tests against Neo4j
scripts/mutate.sh                 # mutation run in observe mode
python3 port/judge/run.py --build dotnet   # the judge against the .NET build
scripts/command_center.sh         # build the status board (docs/FRONTEND.md)
```

## Invariants

1. Read `port/STATE.md` and `port/queue.json` before port work. Update both before you stop.
2. Never edit `port/judge/`, its golden files, or `.upstream/` to make a check pass. If a check looks wrong, stop and tell the maintainer.
3. The commands decide that a unit is done. Your reading of the code is not proof.
4. Keep the structure of the source. One Python module becomes one C# file with the same names in the same order. Idiomatic changes wait until M1 is done. The public surface in [port/DECISIONS.md](port/DECISIONS.md) is the one exception.
5. Write no stubs and no simplified versions. If you cannot port something, mark it `// TODO(port): <reason>`, set the unit to `blocked`, and list it in `port/STATE.md`.
6. If the same mistake shows up twice, change `port/RULES.md` and redo the affected units. Do not patch output by hand.
7. Write no unit tests that restate the implementation, and no mocks between internal classes. Fakes exist only at the network boundary. See [docs/TESTING.md](docs/TESTING.md).
8. Graphiti.Net sends no usage telemetry.
9. A change cannot modify the check that judges it. `REVIEW.md`, `scripts/check.sh`, and `port/judge/` change in their own pull requests.

## Work loop

1. Read the active plan in [docs/exec-plans/active](docs/PLANS.md). Each task names its model and effort.
2. Make the change. Keep one concern in each pull request. If a later layer depends on an earlier one, stack the pull requests with `gh stack`.
3. Run `scripts/check.sh --test`, then use the `verify` skill in `.claude/skills/verify`.
4. If a check still fails after three fix attempts, stop and ask the maintainer.
5. Record commands and results in the plan. Update the documents that the change makes stale.
6. Commit only with the maintainer's approval. Pushes, pull requests, merges, and package publishing each need their own approval.

## Ask first

Stop for a maintainer decision before you change any of these: the graph schema or stored data shapes, public interfaces, a decision in `port/DECISIONS.md`, the judge after gate 2, or a library choice.

## Environments

The same scripts serve local machines, the devcontainer, Claude cloud sessions, and CI.
[docs/RELIABILITY.md](docs/RELIABILITY.md) explains each environment and what was verified in it.
