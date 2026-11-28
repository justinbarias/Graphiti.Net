#!/usr/bin/env python3
"""Scan ported C# for shortcuts that hide unfinished or unchecked work.

Finds port TODO markers, stubs, constructs that switch off the compiler's
checks, and product code that shells out to Python. Exits 1 when anything
is found, so it can gate a unit or a CI run.

This file started as a copy of scripts/scan_port.py of the
port-python-to-dotnet skill. It keeps the six checks and the options of the
skill's scanner, and it differs in three ways:

- It has two more options, --allow and --write-allow.
- It reads each file with its state: a block comment, a raw string of more
  than one line, the depth of the braces, and the type and the member that
  hold a line. The skill's scanner reads each line alone.
- It finds the null-forgiving operator after a name, a closing parenthesis,
  or a closing square bracket, when no = follows, and inside the holes of
  an interpolated string. The skill's scanner finds it only before five
  characters.

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
a count, the member that holds the line, and the text of the line, with
one tab between them. A line that starts with # is a comment. The member
is the name of the type, a colon, and the first line of the declaration
of the member. The file accepts a finding only in its member and with its
text: the count is the number of such lines. If a count differs, each such
line is reported. A line of the file that matches no finding fails the
scan too, and so does a second line for one key. The file can hold only
the labels "stub" and "port TODO". Each other label is an error, because
no port of upstream code needs it.
--write-allow prints such a file for the present findings of those two
labels.

Limits. The scanner reads text and not syntax. It does not see a line
that moves inside its member, or a new line with the same text as a line
that leaves the same member. It does not see the operator after a space
unless a dot and a name follow. A verbatim string of more than one line
is read as code.
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
    ("null-forgiving operator", re.compile(r"(?<=[\w\)\]])!(?!=)|[\w\)\]]\s+!\.(?=[A-Za-z_])")),
    ("calls Python", re.compile(r"Process.*[\"']python|[\"']python3?[\"']|FileName\s*=\s*[\"'](uv|python)")),
]

# The TODO marker lives in comments, so it is checked on the raw line.
RAW_LINE_CHECKS = {"port TODO"}
# The labels that a port of upstream code can need. Only these can be in an allow file.
ALLOWABLE = {"stub", "port TODO"}

CHAR = re.compile(r"'(?:\\.|[^'\\])'")
VERBATIM = re.compile(r'\$?@\$?"(?:""|[^"])*"')
STRING = re.compile(r'(\$?)"((?:\\.|[^"\\])*)"')
HOLE = re.compile(r"\{([^{}]+)\}")
RAW_QUOTES = re.compile(r'"{3,}')
TYPE = re.compile(
    r"^\s*(?:(?:public|internal|private|protected|static|sealed|abstract|partial|readonly|ref|unsafe|file|new)\s+)*"
    r"(?:class|record(?:\s+(?:class|struct))?|struct|interface)\s+([A-Za-z_]\w*)"
)


def key_text(line: str) -> str:
    return " ".join(line.split())


def _string(match: re.Match[str]) -> str:
    # The holes of an interpolated string are code, so they stay.
    if not match.group(1):
        return '""'
    return '"" ' + " ".join(HOLE.findall(match.group(2).replace("{{", "").replace("}}", "")))


class Reader:
    """Gives the code of each line with no comment and no string text, and the member that holds the line."""

    def __init__(self) -> None:
        self.in_block = False
        self.in_raw = False
        self.depth = 0
        self.types: list[tuple[str, int]] = []
        self.pending: tuple[str, int] | None = None
        self.header = ""
        self.boundary = True

    def code(self, line: str) -> tuple[str, str]:
        """Returns the line with its line comment and the line with no comment and no string text."""
        if self.in_raw:
            if RAW_QUOTES.search(line):
                self.in_raw = False
            return "", ""
        text = line
        if self.in_block:
            end = text.find("*/")
            if end < 0:
                return "", ""
            text = " " * (end + 2) + text[end + 2:]
            self.in_block = False
        if len(RAW_QUOTES.findall(text)) % 2 == 1:
            self.in_raw = True
            text = text[: RAW_QUOTES.search(text).start()] + '""'
        text = CHAR.sub("' '", text)
        text = VERBATIM.sub('""', text)
        bare = STRING.sub(_string, text)
        with_strings = text
        while True:
            start = bare.find("/*")
            if start < 0:
                break
            end = bare.find("*/", start + 2)
            if end < 0:
                self.in_block = True
                bare = bare[:start]
                break
            bare = bare[:start] + " " + bare[end + 2:]
        comment = bare.find("//")
        if comment >= 0:
            bare = bare[:comment]
        # For the check that looks at string text, cut the line comment only if it starts outside each string.
        marker = STRING.sub(lambda m: "#" * len(m.group(0)), text).find("//")
        if marker >= 0:
            with_strings = with_strings[:marker]
        return with_strings, bare

    def member(self, bare: str) -> str:
        """Moves the state over one line of code and returns the member that holds the line."""
        before = self.depth
        body = self.types[-1][1] if self.types else None
        significant = bare.strip() and not bare.strip().startswith("[")
        declared = TYPE.match(bare)
        if body is not None and before == body and significant and self.boundary and not declared:
            self.header = key_text(bare)
            self.boundary = False
        if declared and not (bare.rstrip().endswith(";") and "{" not in bare):
            self.pending = (declared.group(1), before)
        for char in bare:
            if char == "{":
                if self.pending is not None and self.depth == self.pending[1]:
                    self.types.append((self.pending[0], self.depth + 1))
                    self.pending = None
                    self.boundary = True
                    self.header = ""
                self.depth += 1
            elif char == "}":
                self.depth -= 1
                if self.types and self.depth < self.types[-1][1]:
                    self.types.pop()
                    self.boundary = True
                    self.header = ""
        found = f"{self.types[-1][0]}: {self.header}" if self.types and before >= self.types[-1][1] and self.header else "-"
        if body is not None and self.types and self.types[-1][1] == body and self.depth == body and bare.rstrip().endswith((";", "}")):
            self.boundary = True
        return found


def scan(path: Path) -> list[tuple[int, str, str, str]]:
    hits = []
    reader = Reader()
    for number, line in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), start=1):
        with_strings, bare = reader.code(line)
        member = reader.member(bare)
        for label, pattern in CHECKS:
            if label in RAW_LINE_CHECKS:
                target = line
            elif label == "calls Python":
                target = with_strings
            else:
                target = bare
            if pattern.search(target):
                hits.append((number, label, line.strip(), member))
    return hits


def read_allow(path: Path) -> dict[tuple[str, str, str, str], int]:
    allowed: dict[tuple[str, str, str, str], int] = {}
    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip() or line.startswith("#"):
            continue
        parts = line.split("\t")
        if len(parts) != 5 or not parts[2].isdigit() or int(parts[2]) < 1:
            raise SystemExit(f"error: {path}:{number}: expected path, label, count, member, and text with tabs between them")
        if parts[1] not in ALLOWABLE:
            raise SystemExit(f"error: {path}:{number}: the label {parts[1]!r} cannot be in an allow file")
        key = (parts[0], parts[1], key_text(parts[3]), key_text(parts[4]))
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
    parser.add_argument("--write-allow", action="store_true", help="print an allow file for the present stub and port TODO findings")
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
            found.setdefault((text, label, key_text(member), key_text(line)), []).append((number, line))

    ordered = sorted(found.items(), key=lambda item: (item[0][0], item[1][0][0]))
    if args.write_allow:
        for (text, label, member, line), hits in ordered:
            if label in ALLOWABLE:
                print(f"{text}\t{label}\t{len(hits)}\t{member}\t{line}")
        return 0

    total = 0
    todos = 0
    accepted = 0
    for (text, label, member, line), hits in ordered:
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
