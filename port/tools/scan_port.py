#!/usr/bin/env python3
"""Scan ported C# for shortcuts that hide unfinished or unchecked work.

Finds port TODO markers, stubs, constructs that switch off the compiler's
checks, and product code that shells out to Python. Exits 1 when anything
is found, so it can gate a unit or a CI run.

This file is a copy of scripts/scan_port.py of the port-python-to-dotnet skill.
The copy adds two options, --allow and --write-allow. It also finds the
null-forgiving operator in each place after a name, a closing parenthesis,
or a closing square bracket. The skill's scanner finds it only before five
characters, and it missed one use before a closing square bracket.

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
a count, the name of the member that holds the line, and the text of the
line, with one tab between them. A line that starts with # is a comment.
The file accepts a finding only in its member and with its text: the count
is the number of such lines. If a count differs, each such line is reported.
A line of the file that matches no finding fails the scan too, and so does
a second line for one key.
--write-allow prints such a file for the present findings.

The member is the nearest declaration with a modifier at or above the line,
found by its text and not by a parser. What the allow file cannot see: a
line that moves inside its member, and a new line with the same text as a
line that is removed from the same member.
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
    ("null-forgiving operator", re.compile(r"(?<=[\w\)\]])!(?!=)")),
    ("calls Python", re.compile(r"Process.*[\"']python|[\"']python3?[\"']|FileName\s*=\s*[\"'](uv|python)")),
]

# The TODO marker lives in comments, so it is checked on the raw line.
RAW_LINE_CHECKS = {"port TODO"}
COMMENT = re.compile(r"//.*$")
MEMBER = re.compile(
    r"^\s*(?:(?:public|internal|private|protected|static|virtual|override|abstract|async|sealed|new|partial|unsafe|extern)\s+)+"
    r"[^=;(]*?\b([A-Za-z_]\w*)\s*(?:<[^>()]*>)?\s*\("
)
STRING = re.compile(r'"(?:\\.|[^"\\])*"')


def member_of(lines: list[str], index: int) -> str:
    for line in reversed(lines[: index + 1]):
        match = MEMBER.match(line)
        if match:
            return match.group(1)
    return "-"


def scan(path: Path) -> list[tuple[int, str, str, str]]:
    hits = []
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    for number, line in enumerate(lines, start=1):
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
                hits.append((number, label, line.strip(), member_of(lines, number - 1)))
    return hits


def key_text(line: str) -> str:
    return " ".join(line.split())


def read_allow(path: Path) -> dict[tuple[str, str, str, str], int]:
    allowed: dict[tuple[str, str, str, str], int] = {}
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip() or line.startswith("#"):
            continue
        parts = line.split("\t")
        if len(parts) != 5 or not parts[2].isdigit() or int(parts[2]) < 1:
            raise SystemExit(f"error: {path}:{number}: expected path, label, count, member, and text with tabs between them")
        key = (parts[0], parts[1], parts[3], key_text(parts[4]))
        if key in allowed:
            raise SystemExit(f"error: {path}:{number}: a second line for {parts[0]}, {parts[1]}, {parts[3]}, and this text")
        allowed[key] = int(parts[2])
    return allowed


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root", type=Path)
    parser.add_argument("--exclude", action="append", default=[], help="skip paths containing this text")
    parser.add_argument("--allow-todo", action="store_true", help="report TODO(port) markers without failing")
    parser.add_argument("--allow", type=Path, help="file of accepted findings: path, label, count, member, text")
    parser.add_argument("--write-allow", action="store_true", help="print an allow file for the present findings")
    args = parser.parse_args()

    if not args.root.is_dir():
        print(f"error: {args.root} is not a directory", file=sys.stderr)
        return 2

    allowed = read_allow(args.allow) if args.allow else {}
    found: dict[tuple[str, str, str, str], list[tuple[int, str]]] = {}
    files = 0
    for path in sorted(args.root.rglob("*.cs")):
        text = path.as_posix()
        if any(part in text for part in args.exclude):
            continue
        files += 1
        for number, label, line, member in scan(path):
            found.setdefault((text, label, member, key_text(line)), []).append((number, line))

    if args.write_allow:
        for (text, label, member, line), hits in sorted(found.items(), key=lambda item: (item[0][0], item[1][0][0])):
            print(f"{text}\t{label}\t{len(hits)}\t{member}\t{line}")
        return 0

    total = 0
    todos = 0
    accepted = 0
    for (text, label, member, line), hits in sorted(found.items(), key=lambda item: (item[0][0], item[1][0][0])):
        if allowed.get((text, label, member, line)) == len(hits):
            accepted += len(hits)
            continue
        for number, raw in hits:
            total += 1
            todos += label == "port TODO"
            print(f"{text}:{number}: {label}: {raw}")
        if (text, label, member, line) in allowed:
            print(f"{text}: {label}: the allow file accepts {allowed[(text, label, member, line)]} of this line in {member}, and the scan found {len(hits)}")
    stale = sorted(key for key in allowed if key not in found)
    for text, label, member, line in stale:
        print(f"{text}: {label}: the allow file names a line in {member} that the scan did not find: {line}")

    print(f"{files} files scanned, {total} findings, {todos} of them port TODO markers, {accepted} accepted by the allow file")
    failing = total - todos if args.allow_todo else total
    return 1 if failing or stale else 0


if __name__ == "__main__":
    sys.exit(main())
