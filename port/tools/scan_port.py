#!/usr/bin/env python3
"""Scan ported C# for shortcuts that hide unfinished or unchecked work.

Finds port TODO markers, stubs, constructs that switch off the compiler's
checks, and product code that shells out to Python. Exits 1 when anything
is found, so it can gate a unit or a CI run.

This file is a copy of scripts/scan_port.py of the port-python-to-dotnet skill.
The copy adds one option, --allow.

Usage:
    python3 port/tools/scan_port.py CSHARP_DIR [--exclude PART ...] [--allow-todo] [--allow FILE]

Example:
    python3 port/tools/scan_port.py src --exclude /obj/ --exclude /bin/ --allow port/tools/scan_port_allow.txt

--exclude skips any file whose path contains the given text. Use it for
build output and for the judge's own tests, which are allowed to start
the Python implementation.

--allow-todo still prints every TODO(port) marker but does not fail on
them. Use it while blocked units remain, and check the printed markers
against the blocked list in port/STATE.md.

--allow names a file of accepted findings. Each line has a path, a label,
and a count, with one tab between them. A line that starts with # is a
comment. The findings of one label in one file are accepted only if their
count equals the count of the line. If the count differs, each finding of
that label in that file is reported, so a new site and a removed site both
fail the scan. A line that matches no finding fails the scan too.
--write-allow prints such a file for the present findings.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

CHECKS: list[tuple[str, re.Pattern[str]]] = [
    ("port TODO", re.compile(r"TODO\(port\)")),
    ("stub", re.compile(r"\bNotImplementedException\b")),
    ("dynamic type", re.compile(r"\bdynamic\b(?!\s*\()")),
    ("warning switched off", re.compile(r"#pragma\s+warning\s+disable|\[SuppressMessage")),
    ("null-forgiving operator", re.compile(r"(?<=[\w\)\]])!(?=[\.\;\,\)\[])")),
    ("calls Python", re.compile(r"Process.*[\"']python|[\"']python3?[\"']|FileName\s*=\s*[\"'](uv|python)")),
]

# The TODO marker lives in comments, so it is checked on the raw line.
RAW_LINE_CHECKS = {"port TODO"}
COMMENT = re.compile(r"//.*$")
STRING = re.compile(r'"(?:\\.|[^"\\])*"')


def scan(path: Path) -> list[tuple[int, str, str]]:
    hits = []
    for number, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), start=1):
        code = COMMENT.sub("", line)
        no_strings = STRING.sub('""', code)
        for label, pattern in CHECKS:
            if label in RAW_LINE_CHECKS:
                target = line
            elif label == "calls Python":
                target = code
            else:
                target = no_strings
            if pattern.search(target):
                hits.append((number, label, line.strip()))
    return hits


def read_allow(path: Path) -> dict[tuple[str, str], int]:
    allowed: dict[tuple[str, str], int] = {}
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip() or line.startswith("#"):
            continue
        parts = line.split("	")
        if len(parts) != 3 or not parts[2].isdigit():
            raise SystemExit(f"error: {path}:{number}: expected path, label, and count with tabs between them")
        allowed[(parts[0], parts[1])] = int(parts[2])
    return allowed


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root", type=Path)
    parser.add_argument("--exclude", action="append", default=[], help="skip paths containing this text")
    parser.add_argument("--allow-todo", action="store_true", help="report TODO(port) markers without failing")
    parser.add_argument("--allow", type=Path, help="file of accepted findings: path, label, count")
    parser.add_argument("--write-allow", action="store_true", help="print an allow file for the present findings")
    args = parser.parse_args()

    if not args.root.is_dir():
        print(f"error: {args.root} is not a directory", file=sys.stderr)
        return 2

    allowed = read_allow(args.allow) if args.allow else {}
    found: dict[tuple[str, str], list[tuple[int, str]]] = {}
    files = 0
    for path in sorted(args.root.rglob("*.cs")):
        text = path.as_posix()
        if any(part in text for part in args.exclude):
            continue
        files += 1
        for number, label, line in scan(path):
            found.setdefault((text, label), []).append((number, line))

    if args.write_allow:
        for (text, label), hits in sorted(found.items()):
            print(f"{text}	{label}	{len(hits)}")
        return 0

    total = 0
    todos = 0
    accepted = 0
    for (text, label), hits in sorted(found.items()):
        if allowed.get((text, label)) == len(hits):
            accepted += len(hits)
            continue
        for number, line in hits:
            total += 1
            todos += label == "port TODO"
            print(f"{text}:{number}: {label}: {line}")
        if (text, label) in allowed:
            print(f"{text}: {label}: the allow file accepts {allowed[(text, label)]}, and the scan found {len(hits)}")
    stale = sorted(key for key in allowed if key not in found)
    for text, label in stale:
        print(f"{text}: {label}: the allow file names a finding that the scan did not find")

    print(f"{files} files scanned, {total} findings, {todos} of them port TODO markers, {accepted} accepted by the allow file")
    failing = total - todos if args.allow_todo else total
    return 1 if failing or stale else 0


if __name__ == "__main__":
    sys.exit(main())
