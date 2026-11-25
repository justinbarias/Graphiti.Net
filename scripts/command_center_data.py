#!/usr/bin/env python3
"""Writes the data file that the command center reads for values that change with the port.

The command center generator of the agent-harness skill reads the plan files itself.
This script adds what only this repository knows:

    port/DECISIONS.md                              the date on which each gate passed
    docs/exec-plans/*/0004-translate-core.md       one task for each layer and each end-to-end test
    port/queue.json                                the status of each layer, from its units, after gate 3
    port/judge/count.txt, expected-failures.txt    judge parity

    python3 scripts/command_center_data.py    # writes docs/generated/command-center.data.json

scripts/command_center.sh runs this script and then the generator. docs/FRONTEND.md describes the page.

A row of a plan 0004 table has no Task cell. A status mark at the start of one of its cells sets the status:
"Done 2026-12-05.", "Blocked.", or "In progress.". The mark is not part of the title.
"""
from __future__ import annotations

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
OUTPUT = ROOT / "docs" / "generated" / "command-center.data.json"
PLAN = "0004"
OUT_OF_SCOPE = ("replaced", "out-of-mvp", "not-ported")
LAYER_FEATURES = {"0": "Core skeleton", "1": "Foundations", "2": "Graph types", "3": "Model boundary", "4": "Prompts",
                  "5": "Neo4j driver", "6": "Search", "7": "Maintenance", "8": "Ingestion", "9": "Facade"}
GATES = ("1", "2", "3", "4")
PLAN_GATE = "3"  # plan 0004 starts after this gate. Before it, the queue records only the pilot of plan 0003.
DAY = r"\d{4}-\d{2}-\d{2}"
MARK = re.compile(r"^\s*(?:(Done),? (?:on )?(" + DAY + r")|(Blocked)|(In progress))\b[.:]?\s*")


def cells(line: str) -> list[str]:
    """Splits one Markdown table row. A pipe inside a code span stays in its cell."""
    parts, current, in_code = [], "", False
    for char in line.strip().strip("|"):
        if char == "`":
            in_code = not in_code
        if char == "|" and not in_code:
            parts.append(current.strip())
            current = ""
        else:
            current += char
    parts.append(current.strip())
    return parts


def table(text: str, *columns: str) -> list[dict[str, str]]:
    """Returns the rows of the first table that has each given column, as a map from column name to cell."""
    lines = text.split("\n")
    for index, line in enumerate(lines):
        header = cells(line) if line.startswith("|") else []
        if all(column in header for column in columns):
            rows = []
            for row in lines[index + 2 :]:
                if not row.startswith("|"):
                    break
                rows.append(dict(zip(header, cells(row))))
            return rows
    return []


def take_mark(row: dict[str, str]) -> tuple[str, str]:
    """Removes a status mark from the start of a cell. Returns the status and the done date."""
    for column, cell in row.items():
        match = MARK.match(cell)
        if match:
            row[column] = cell[match.end():]
            return ("done" if match.group(1) else "blocked" if match.group(3) else "active"), match.group(2) or ""
    return "", ""


def read_gates() -> dict[str, str]:
    """Returns the date of each gate that the gate log of port/DECISIONS.md records as approved."""
    passed = {}
    for line in (ROOT / "port" / "DECISIONS.md").read_text(encoding="utf-8").split("\n"):
        row = cells(line) if line.startswith("|") else []
        gate = row[0].split(".")[0] if row else ""
        if len(row) < 3 or gate not in GATES or not re.fullmatch(DAY, row[1]):
            continue
        if row[2].startswith("Approved"):
            passed[gate] = row[1]
        else:
            print(f"command_center_data: gate {gate} has a date, but its result does not start with \"Approved\". The gate stays open.", file=sys.stderr)
    return passed


def layer_of(module: str) -> str:
    name = module.removeprefix("graphiti_core").lstrip(".")
    top = name.split(".")[0]
    if name in ("", "graphiti") or top == "migrations":
        return "9"
    if top == "ingestion":
        return "8"
    if name.startswith("utils.maintenance"):
        return "7"
    if top == "search":
        return "6"
    if name.startswith("driver.neo4j"):
        return "5"
    if top == "prompts":
        return "4"
    if top in ("llm_client", "embedder", "cross_encoder"):
        return "3"
    if top in ("nodes", "edges", "driver"):
        return "2"
    return "1"


def build() -> dict:
    queue = json.loads((ROOT / "port" / "queue.json").read_text(encoding="utf-8"))
    in_scope = [unit for unit in queue["units"] if unit["notes"].split(":")[0] not in OUT_OF_SCOPE]
    by_layer: dict[str, list[dict]] = {}
    for unit in sorted(in_scope, key=lambda u: u["order"]):
        by_layer.setdefault(layer_of(unit["module"]), []).append(unit)

    passed = read_gates()
    started = PLAN_GATE in passed
    plan_file = next((ROOT / "docs" / "exec-plans").glob(f"*/{PLAN}-*.md"), None)
    plan_done = plan_file is not None and plan_file.parent.name == "completed"
    text = plan_file.read_text(encoding="utf-8") if plan_file else ""
    tasks = []
    for row in table(text, "Layer", "Branch", "Upstream packages"):
        recorded, when = take_mark(row)
        layer = row["Layer"]
        units = [] if layer == "0" else by_layer.get(layer, [])
        states = {unit["status"] for unit in units} if started else set()  # before the gate, unit work is the pilot
        if plan_done or recorded == "done" or (units and states == {"reviewed"}):
            status = "done"
        elif recorded == "blocked" or "blocked" in states:
            status = "blocked"
        elif recorded == "active" or states - {"todo"}:
            status = "active"
        else:
            status = "todo"
        count = len(in_scope) if layer == "0" else len(units)
        owner = row.get("Implementer", "")
        tasks.append({
            "plan": PLAN, "num": f"L{layer}", "status": status, "done_date": when, "feature": LAYER_FEATURES.get(layer, ""),
            "title": f"Layer {layer}: {row['Branch'].strip('`').removeprefix('port/')}",
            "text": f"Port layer {layer} on branch {row['Branch']}: {count} units. Upstream packages: {row['Upstream packages']}",
            "owner": f"Implementer: {owner}" if owner else "",
            "acceptance": "Each unit of the layer reaches `reviewed` in `port/queue.json`: build, format, evaluation tests, judge cases, and two reviewers.",
            "items": [{"name": unit["module"].removeprefix("graphiti_core.") or "graphiti_core", "status": unit["status"],
                       "note": f"{unit['lines']} lines" + (", cycle group" if unit["cycle"] else "")} for unit in units],
        })
    for row in table(text, "After layer", "Flow"):
        recorded, when = take_mark(row)
        layer = row["After layer"]
        tasks.append({
            "plan": PLAN, "num": f"E{layer}", "status": "done" if plan_done else recorded or "todo", "done_date": when,
            "feature": LAYER_FEATURES.get(layer, ""), "title": f"End-to-end test after layer {layer}", "text": row["Flow"],
            "acceptance": "The test runs against the Neo4j sidecar and passes in `scripts/check.sh --test`.",
        })

    # An open gate has no row here, so a date in docs/command-center.toml stays in force.
    gates = [{"id": gate, "passed": passed[gate]} for gate in GATES if gate in passed]

    judge = ROOT / "port" / "judge"
    cases = int((judge / "count.txt").read_text().strip())
    lines = (judge / "expected-failures.txt").read_text().split("\n")
    expected = len([line for line in lines if line.strip() and not line.lstrip().startswith("#")])
    reviewed = sum(1 for unit in in_scope if unit["status"] == "reviewed")
    in_work = sum(1 for unit in in_scope if unit["status"] != "todo")
    note = f"{in_work} started" + ("" if started or not in_work else ", all in the pilot of plan 0003")
    counters = [
        {"label": "Port units reviewed", "value": reviewed, "total": len(in_scope), "note": note, "source": "port/queue.json"},
        {"label": "Judge cases that pass on .NET", "value": cases - expected, "total": cases, "source": "port/judge/expected-failures.txt"},
    ]
    return {"tasks": tasks, "gates": gates, "counters": counters}


def main() -> int:
    data = build()
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(data, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"command_center_data: {len(data['tasks'])} tasks, {len(data['gates'])} gates, {len(data['counters'])} counters -> {OUTPUT.relative_to(ROOT)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
