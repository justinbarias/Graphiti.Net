#!/usr/bin/env python3
"""Adds one hand-written reply to a reply file.

    python3 port/judge/run.py --build python --filter <case id> --show-missing
    python3 port/judge/add_reply.py <replies name> <key> < reply.json

Hand-written replies are for guard scenarios. They are inputs to the judge, like cases.
The expected output still comes only from the Python build.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

JUDGE = Path(__file__).resolve().parent


def main() -> int:
    if len(sys.argv) != 3:
        print(__doc__, file=sys.stderr)
        return 2
    name, key = sys.argv[1], sys.argv[2]
    reply = json.load(sys.stdin)
    path = JUDGE / "replies" / f"{name}.jsonl"
    rows = [json.loads(l) for l in path.read_text(encoding="utf-8").split("\n") if l.strip()] if path.exists() else []
    if any(row["key"] == key for row in rows):
        print(f"add_reply: {name} already has a reply for {key[:12]}", file=sys.stderr)
        return 1
    rows.append({"key": key, "reply": reply, "recorded_with": "hand"})
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text("".join(json.dumps(r, ensure_ascii=True) + "\n" for r in rows), encoding="utf-8")
    print(f"add_reply: {name} now has {len(rows)} replies")
    return 0


if __name__ == "__main__":
    sys.exit(main())
