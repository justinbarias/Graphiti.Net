#!/usr/bin/env python3
"""Proves that the judge catches breakage.

For each break, the script copies upstream `graphiti_core` to a temporary folder, changes one
piece of text, and runs the judge against that copy. The judge must fail. The pinned checkout
in `.upstream/` is never changed. A control run on an unchanged copy must pass.

    python3 port/tools/break_tests.py

Prints a Markdown table for port/STATE.md. Exit code 0 means that the control passed and the
judge caught every break.
"""
from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent.parent
UP = ROOT / ".upstream/graphiti"

# (layer, file under graphiti_core, text to find, replacement, what the break means)
BREAKS = [
    ("pure", "prompts/extract_nodes.py", "entity extraction specialist for unstructured text", "entity extraction expert for unstructured text", "One word changed in the text extraction prompt"),
    ("pure", "search/search_utils.py", "scores[uuid] += 1 / (i + rank_const)", "scores[uuid] += 1 / (i + rank_const + 1)", "RRF rank constant off by one"),
    ("pure", "helpers.py", "'~': r'\\~',", "", "The `~` escape removed from the full-text sanitizer"),
    ("pure", "utils/maintenance/dedup_helpers.py", "_FUZZY_JACCARD_THRESHOLD = 0.9", "_FUZZY_JACCARD_THRESHOLD = 0.5", "Fuzzy dedupe threshold lowered from 0.9 to 0.5"),
    ("pure", "models/nodes/node_db_queries.py", "n.summary AS summary,", "n.name AS summary,", "A node query returns the name in place of the summary"),
    ("pure", "utils/datetime_utils.py", "tzinfo=timezone.utc", "tzinfo=None", "A naive datetime is no longer set to UTC"),
    ("flow", "driver/neo4j/operations/search_ops.py", "filter_queries.append('e.group_id IN $group_ids')", "pass", "The group filter dropped from one edge search query"),
    ("flow", "utils/maintenance/edge_operations.py", "edge.invalid_at = resolved_edge.valid_at", "edge.invalid_at = edge.invalid_at", "A contradicted fact keeps an open validity window"),
    ("flow", "search/search_utils.py", "DEFAULT_MIN_SCORE = 0.6", "DEFAULT_MIN_SCORE = 0.99", "Similarity search needs a score of 0.99"),
    ("flow", "utils/maintenance/edge_operations.py", "resolved_edge.expired_at = now", "resolved_edge.expired_at = None", "An invalidated fact gets no expiry time"),
    ("flow", "search/search_utils.py", "filter_queries.append('e.group_id IN $group_ids')", "pass", "The group filter dropped from the inline edge full-text search"),
    ("flow", "nodes.py", "class EpisodeType(Enum):", "class EpisodeType(Enum):\n    pass\n\n\nclass _Unused(Enum):", "Control for the script: an import error must also fail the judge"),
]


def run_judge(upstream_dir: Path | None) -> tuple[int, dict[str, int], str]:
    env = dict(os.environ)
    if upstream_dir is not None:
        env["JUDGE_UPSTREAM_DIR"] = str(upstream_dir)
    run = subprocess.run(
        [sys.executable, str(ROOT / "port/judge/run.py"), "--build", "python"], capture_output=True, text=True, env=env
    )
    failed = re.findall(r"^FAIL (\S+)", run.stderr, flags=re.M)
    by_layer: dict[str, int] = {}
    for case_id in failed:
        by_layer[case_id.split(".", 1)[0]] = by_layer.get(case_id.split(".", 1)[0], 0) + 1
    first = failed[0] if failed else ("host exited" if "host exited" in run.stdout + run.stderr else "")
    return run.returncode, by_layer, first


def main() -> int:
    ok = True
    rows = []
    with tempfile.TemporaryDirectory() as scratch:
        copy = Path(scratch) / "graphiti"
        shutil.copytree(UP / "graphiti_core", copy / "graphiti_core", ignore=shutil.ignore_patterns("__pycache__"))
        code, by_layer, _ = run_judge(copy)
        control = code == 0
        ok &= control
        rows.append(f"| Control | An unchanged copy of upstream | {'Passed' if control else 'FAILED: ' + str(by_layer)} | |")

        for index, (layer, rel, old, new, meaning) in enumerate(BREAKS, 1):
            path = copy / "graphiti_core" / rel
            original = path.read_text(encoding="utf-8")
            if old not in original:
                rows.append(f"| {index} | {meaning} | SCRIPT ERROR: text not found in `{rel}` | |")
                ok = False
                continue
            path.write_text(original.replace(old, new, 1), encoding="utf-8")
            for cache in (copy / "graphiti_core").rglob("__pycache__"):
                shutil.rmtree(cache, ignore_errors=True)
            code, by_layer, first = run_judge(copy)
            path.write_text(original, encoding="utf-8")
            caught = code != 0
            ok &= caught
            counts = ", ".join(f"{count} {name}" for name, count in sorted(by_layer.items())) or "the host did not start"
            rows.append(
                f"| {index} | {meaning} (`{rel}`) | {'Caught' if caught else 'MISSED'}: {counts} | `{first}` |"
            )

    print("| # | Break | Judge result | First failing case |")
    print("| --- | --- | --- | --- |")
    print("\n".join(rows))
    print(f"\nbreak_tests: {'passed' if ok else 'FAILED'}", file=sys.stderr)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
