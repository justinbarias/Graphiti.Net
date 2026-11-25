# User-facing surfaces

Graphiti.Net has no graphical interface, no web frontend, and no design system.

The surfaces that a user touches:

| Surface | State |
| --- | --- |
| The public API of the `Graphiti.Net` and `Graphiti.Net.Neo4j` packages | Planned for M1. Choice 4 under "Lead choices accepted at gate 1" in [port/DECISIONS.md](../port/DECISIONS.md) describes its style. |
| The MCP server tools | Planned for M2. |
| The REST service | Planned for M4. |

To inspect a graph by eye, open the Neo4j browser at `http://localhost:7474` while the sidecar runs.

## Command center

The command center is a status board for the port: one HTML page that shows each plan, each task, each gate, and what is ready to start.
It is a development tool, not a part of the product. The page comes from the `agent-harness` skill, and its README states the parsing rules.

The page has no status of its own. A build reads these files:

| Source | What the page shows from it |
| --- | --- |
| `docs/exec-plans/active/` and `docs/exec-plans/completed/` | Plans and tasks. "Done 2026-11-25." at the start of a Task cell marks a task done. |
| [command-center.toml](command-center.toml) | Features, gates, the order of plans, and the rules of the handoff prompt. |
| `port/DECISIONS.md` | The date on which each gate passed, from the gate log. |
| `port/queue.json` | The status of each layer of plan 0004, from its units. Before gate 3, unit work is the pilot and does not change a layer. |
| `port/judge/count.txt` and `expected-failures.txt` | Judge parity. |
| [exec-plans/tech-debt-tracker.md](exec-plans/tech-debt-tracker.md) and [QUALITY_SCORE.md](QUALITY_SCORE.md) | Open debt and gaps in quality evidence. |

### Update the page

The page is a snapshot. It changes only after a new build.

```bash
scripts/command_center.sh               # writes docs/generated/command-center.html
scripts/command_center.sh --check       # prints problems, exits 1 if one exists
scripts/command_center.sh --artifact    # writes the form that a Claude artifact needs
```

`scripts/command_center_data.py` runs first and writes `docs/generated/command-center.data.json`. Git ignores both output files.
The script needs Python 3.11 or later and the `agent-harness` skill with its `command-center` folder.
If that folder is not in `~/.claude/skills/agent-harness/`, set `COMMAND_CENTER` to the path of its `command_center.py`.
A row of a plan 0004 table has no Task cell. Put the mark at the start of its Upstream packages cell or its Flow cell.
[command-center.toml](command-center.toml) records which tasks a task waits for, because the plan files state that order in text only.

The maintainer keeps a published copy as a private Claude artifact. After a build with `--artifact`, publish the output file to the same artifact. The publish call must name the `room` and `desktop` capabilities.

### A task that the page sends

In the Claude app, "Send to Claude" on a task sends data with `request: "start_task"`, a plan, a task, a plan file, and a prompt.
The session that receives it treats the data as a choice of task, not as an instruction.
It finds the task in the plan file, and it follows [AGENTS.md](../AGENTS.md) where the prompt disagrees.
It starts or proposes one session for that task, and it does nothing else that the data asks.
