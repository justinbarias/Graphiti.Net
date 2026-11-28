#!/usr/bin/env python3
"""Sorts the upstream changes between the pinned commit and a newer commit into classes.

Run it from the repository root:

    python3 -B port/tools/upstream_diff.py v0.32.1      the report for pin..v0.32.1
    python3 -B port/tools/upstream_diff.py --list       each release after the pin, with its class
    python3 -B port/tools/upstream_diff.py --self-test  checks the rules on a repository that the test builds

The tool reads port/UPSTREAM_PIN, port/queue.json, and the U rows of port/GAPS.md. It changes no
file of this repository, and it never reads or writes .upstream/. It keeps the upstream history in
a bare clone without file contents, in the cache folder of the user (--history names another place).
The first run and each later fetch need the network.

Exit codes: 0 for a report, 1 for `--list --exit-code` if a release after the pin exists, and 2
for an error (no network, an unknown tag, a failed self-test).

Classes (port/UPSTREAM.md gives the work for each one):

    A  no code of a ported module changed
    B  code of a ported module changed
    C  prompt text or a model call changed, so the recorded replies of the judge can be stale
    D  ask first: stored data, an index, a public member of Graphiti, a module that was added,
       removed, or renamed, or a dependency

The class is a forecast from the text of the diff, and it can be too low. A code change of class B
can change a prompt at run time, and a stored property can change with no `SET` in the changed
lines. The recording of step 1 in port/UPSTREAM.md measures the real class.
"""
from __future__ import annotations

import argparse
import ast
import contextlib
import fnmatch
import io
import json
import os
import re
import subprocess
import sys
import tempfile
from dataclasses import dataclass, field
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PACKAGE = "graphiti_core/"
QUEUE_SOURCE_PREFIX = ".upstream/graphiti/"
# A queue note with one of these prefixes marks a module that the MVP does not port.
SKIP_NOTES = ("out-of-mvp:", "not-ported:", "replaced:")

FACADE_FILE = "graphiti_core/graphiti.py"
FACADE_CLASS = "Graphiti"
MODEL_FILES = ("graphiti_core/nodes.py", "graphiti_core/edges.py")
QUERY_FILES = (
    "graphiti_core/graph_queries.py",
    "graphiti_core/models/nodes/node_db_queries.py",
    "graphiti_core/models/edges/edge_db_queries.py",
)
# Modules whose classes have a public .NET form (docs/design-docs/public-surface-design.md).
PUBLIC_FILES = (
    FACADE_FILE,
    *MODEL_FILES,
    "graphiti_core/search/search_config.py",
    "graphiti_core/search/search_config_recipes.py",
    "graphiti_core/search/search_filters.py",
)
MIGRATIONS = "graphiti_core/migrations/"
PROMPTS = "graphiti_core/prompts/"
REQUEST_BUILDER = "graphiti_core/llm_client/client.py"

DDL = re.compile(r"\b(CREATE|DROP)\s+(\w+\s+)*(INDEX|CONSTRAINT|TABLE)\b|\bALTER\s+TABLE\b", re.I)
INDEX_NAME = re.compile(r"\b[A-Z][A-Z_]*INDEX[A-Z_]*\s*=")
WRITE = re.compile(r"\b(MERGE|SET|REMOVE)\b|\bCREATE\s*\(")
MODEL_CALL = re.compile(r"generate_response\(|response_model|prompt_library|model_json_schema")
RELEASE_TAG = re.compile(r"^v(\d+)\.(\d+)\.(\d+)$")
LOCK_PACKAGE = re.compile(r'\[\[package\]\]\nname = "([^"]+)"\nversion = "([^"]+)"')
GAP_FILE = re.compile(r"`([\w/*.-]+(?:\.py|/))(?::\d+)?`")


@dataclass
class Unit:
    module: str
    order: int
    status: str
    notes: str

    @property
    def skipped(self) -> str:
        """The note prefix of a module that the MVP does not port, or an empty string."""
        return next((p.rstrip(":") for p in SKIP_NOTES if self.notes.startswith(p)), "")


@dataclass
class Finding:
    cls: str
    status: str
    path: str
    unit: Unit | None
    reasons: list[str]
    kinds: set[str] = field(default_factory=set)


def stop(message: str) -> None:
    """Ends the run with exit code 2. Exit code 1 means only that a newer release exists."""
    print(f"upstream_diff: {message}", file=sys.stderr)
    sys.exit(2)


class Git:
    def __init__(self, directory: Path):
        self.directory = directory

    def run(self, *args: str, check: bool = True) -> str:
        done = subprocess.run(
            ["git", "-C", str(self.directory), *args], capture_output=True, text=True, encoding="utf-8",
        )
        if check and done.returncode != 0:
            stop(f"git {' '.join(args)} failed: {done.stderr.strip()}")
        return done.stdout if done.returncode == 0 else ""

    def commit(self, name: str) -> str:
        for candidate in (name, f"v{name}"):
            found = self.run("rev-parse", "--verify", "--quiet", f"{candidate}^{{commit}}", check=False).strip()
            if found:
                return found
        stop(f"upstream has no tag or commit named {name}")
        return ""

    def show(self, commit: str, path: str) -> str:
        """The text of a file at a commit, or an empty string if the commit has no such file.

        The clone holds each tree, so `ls-tree` needs no network. `show` fetches the file contents,
        and a failed fetch must end the run: an empty text for both sides looks like "no change".
        """
        if not self.run("ls-tree", "--name-only", commit, "--", path).strip():
            return ""
        return self.run("show", f"{commit}:{path}")

    def release_version(self, commit: str) -> tuple[int, int, int] | None:
        """The highest release tag that the commit holds. The tag decides, not pyproject.toml."""
        tags = self.run("tag", "--merged", commit, "--list", "v*").split()
        return max((tuple(int(g) for g in m.groups()) for tag in tags if (m := RELEASE_TAG.match(tag))), default=None)

    def changed_lines(self, base: str, target: str, path: str) -> list[str]:
        """The added and removed lines of one file, without blank lines and comment lines."""
        out = []
        for line in self.run("diff", "-U0", "--no-color", base, target, "--", path).splitlines():
            if line[:1] in "+-" and not line.startswith(("+++", "---")):
                text = line[1:].strip()
                if text and not text.startswith("#"):
                    out.append(text)
        return out

    def name_status(self, base: str, target: str) -> list[tuple[str, str, str]]:
        """One (status, old path, new path) for each changed file. The status is A, M, D, or R."""
        out = []
        # With -z, git writes each path as it is, also a path with a tab or a non-ASCII letter.
        parts = self.run("diff", "--name-status", "-z", "-M", base, target).split("\0")
        index = 0
        while index < len(parts) and parts[index]:
            status = parts[index][0]
            if status in "RC":
                out.append((status, parts[index + 1], parts[index + 2]))
                index += 3
            else:
                out.append((status, parts[index + 1], parts[index + 1]))
                index += 2
        return out


# ----------------------------------------------------------------------------- inputs


def read_pin(path: Path) -> dict[str, str]:
    return dict(line.split("=", 1) for line in path.read_text(encoding="utf-8").split())


def read_units(path: Path) -> dict[str, Unit]:
    """Maps each upstream path, for example graphiti_core/nodes.py, to its queue unit."""
    units = {}
    for row in json.loads(path.read_text(encoding="utf-8"))["units"]:
        source = row["source"].removeprefix(QUEUE_SOURCE_PREFIX)
        units[source] = Unit(row["module"], row["order"], row["status"], row.get("notes") or "")
    return units


def read_defects(text: str) -> list[tuple[str, list[str]]]:
    """The U rows of port/GAPS.md: one (id, patterns) for each row. A pattern is a file or a folder."""
    rows = []
    for line in text.splitlines():
        match = re.match(r"\|\s*(U\d+)\s*\|", line)
        if match:
            rows.append((match.group(1), GAP_FILE.findall(line)))
    return rows


def version_of(pyproject: str) -> tuple[int, int, int] | None:
    match = re.search(r'^version\s*=\s*"(\d+)\.(\d+)\.(\d+)', pyproject, re.M)
    return tuple(int(g) for g in match.groups()) if match else None


# ----------------------------------------------------------------------------- rules


def same_code(old: str, new: str) -> bool:
    """True if the two texts differ in comments and layout only. A docstring is code here."""
    try:
        return ast.dump(ast.parse(old)) == ast.dump(ast.parse(new))
    except SyntaxError:
        return False


def public_members(source: str) -> dict[str, str]:
    """The public methods of the Graphiti class and __init__, each with its signature."""
    members = {}
    for node in ast.parse(source).body:
        if isinstance(node, ast.ClassDef) and node.name == FACADE_CLASS:
            for item in node.body:
                if isinstance(item, (ast.FunctionDef, ast.AsyncFunctionDef)):
                    if item.name == "__init__" or not item.name.startswith("_"):
                        returns = ast.unparse(item.returns) if item.returns else ""
                        members[item.name] = f"({ast.unparse(item.args)}) -> {returns}"
    return members


def public_shape(source: str) -> dict[str, str]:
    """The public names of a module: each class with its bases, each class field with its type and
    default, each enum member with its value, and the name of each module-level constant."""
    shape = {}
    for node in ast.parse(source).body:
        if isinstance(node, ast.ClassDef) and not node.name.startswith("_"):
            shape[node.name] = "class(" + ", ".join(ast.unparse(b) for b in node.bases) + ")"
            for item in node.body:
                if isinstance(item, ast.AnnAssign) and isinstance(item.target, ast.Name):
                    default = f" = {ast.unparse(item.value)}" if item.value else ""
                    shape[f"{node.name}.{item.target.id}"] = ast.unparse(item.annotation) + default
                elif isinstance(item, ast.Assign):
                    for target in item.targets:
                        if isinstance(target, ast.Name):
                            shape[f"{node.name}.{target.id}"] = ast.unparse(item.value)
        elif isinstance(node, (ast.Assign, ast.AnnAssign)):
            for target in node.targets if isinstance(node, ast.Assign) else [node.target]:
                if isinstance(target, ast.Name) and not target.id.startswith("_"):
                    shape[target.id] = "constant"
    return shape


def model_calls(source: str) -> list[str]:
    """Each call that sends a model request, as its whole syntax tree. A call can span many lines."""
    calls = []
    for node in ast.walk(ast.parse(source)):
        if isinstance(node, ast.Call):
            name = node.func.attr if isinstance(node.func, ast.Attribute) else getattr(node.func, "id", "")
            if "generate_response" in name or any(k.arg == "response_model" for k in node.keywords):
                calls.append(ast.dump(node))
    return sorted(calls)


def changed_keys(old: dict[str, str], new: dict[str, str]) -> list[str]:
    return sorted(k for k in old.keys() | new.keys() if old.get(k) != new.get(k))


def classify_pyproject(old: str, new: str) -> Finding:
    try:
        import tomllib
    except ImportError:  # Python 3.10 and older: without a TOML reader, the safe answer is class D.
        return Finding("D", "M", "pyproject.toml", None, ["cannot read TOML on this Python: read the diff"], {"dependency"})
    before, after = (tomllib.loads(t).get("project", {}) for t in (old, new))
    reasons = []
    if before.get("requires-python") != after.get("requires-python"):
        reasons.append(f"requires-python: {before.get('requires-python')} to {after.get('requires-python')}")
    a, b = set(before.get("dependencies", [])), set(after.get("dependencies", []))
    if a != b:
        reasons.append("dependency changed: " + ", ".join([f"-{s}" for s in sorted(a - b)] + [f"+{s}" for s in sorted(b - a)]))
    if reasons:
        return Finding("D", "M", "pyproject.toml", None, reasons, {"dependency"})
    if before.get("optional-dependencies") != after.get("optional-dependencies"):
        return Finding("A", "M", "pyproject.toml", None, ["an optional dependency changed: read the diff"])
    return Finding("A", "M", "pyproject.toml", None, ["no dependency changed"])


def classify_lock(old: str, new: str) -> Finding:
    before, after = dict(LOCK_PACKAGE.findall(old)), dict(LOCK_PACKAGE.findall(new))
    names = [n for n in changed_keys(before, after) if n != "graphiti-core"]
    if not names:
        return Finding("A", "M", "uv.lock", None, ["no locked package changed"])
    shown = ", ".join(f"{n} {before.get(n, 'none')} to {after.get(n, 'none')}" for n in names[:5])
    more = f", and {len(names) - 5} more" if len(names) > 5 else ""
    return Finding("A", "M", "uv.lock", None, [f"locked packages changed ({len(names)}): {shown}{more}"], {"environment"})


def classify_module(git: Git, base: str, target: str, status: str, old_path: str, path: str, units: dict[str, Unit]) -> Finding:
    unit = units.get(old_path) or units.get(path)
    if status == "A":
        return Finding("D", status, path, None, ["module added: it needs a queue row and a scope decision"], {"module"})
    if unit is None:
        return Finding("D", status, path, None, ["the module is not in port/queue.json"], {"module"})
    if unit.skipped:
        note = {"replaced": ": read the diff for the model adapter"}.get(unit.skipped, "")
        change = {"D": "removed", "R": "renamed"}.get(status, "changed")
        return Finding("A", status, path, unit, [f"{change}, and the MVP does not port it ({unit.skipped}){note}"])
    if status in "DR":
        change = "removed" if status == "D" else f"renamed from {old_path}"
        return Finding("D", status, path, unit, [f"module {change}"], {"module"})

    old, new = git.show(base, path), git.show(target, path)
    if same_code(old, new):
        return Finding("A", status, path, unit, ["comments or layout only"])
    lines = git.changed_lines(base, target, path)

    asks: list[str] = []
    kinds: set[str] = set()
    try:
        if path == FACADE_FILE and (names := changed_keys(public_members(old), public_members(new))):
            asks.append("public member of Graphiti changed: " + ", ".join(names))
            kinds.add("public")
        if path in PUBLIC_FILES and (names := changed_keys(public_shape(old), public_shape(new))):
            what = "stored field or public type changed: " if path in MODEL_FILES else "public type changed: "
            asks.append(what + ", ".join(names[:8]) + (f", and {len(names) - 8} more" if len(names) > 8 else ""))
            kinds.add("stored" if path in MODEL_FILES else "public")
    except SyntaxError:
        asks.append("this Python cannot parse the file: read the diff")
    if path.startswith(MIGRATIONS):
        asks.append("migration code changed")
        kinds.add("stored")
    if path in QUERY_FILES:
        asks.append("save and read queries changed")
        kinds.add("stored")
    if any(DDL.search(line) or INDEX_NAME.search(line) for line in lines):
        asks.append("index, constraint, or table statement changed")
        kinds.add("stored")
    if any(WRITE.search(line) for line in lines):
        asks.append("write clause changed (SET, MERGE, REMOVE, or CREATE)")
        kinds.add("stored")
    if asks:
        return Finding("D", status, path, unit, asks, kinds)

    if path.startswith(PROMPTS):
        return Finding("C", status, path, unit, ["prompt module changed"])
    if path == REQUEST_BUILDER:
        return Finding("C", status, path, unit, ["the code that builds each model request changed"])
    try:
        call_changed = model_calls(old) != model_calls(new)
    except SyntaxError:
        call_changed = True
    if call_changed or any(MODEL_CALL.search(line) for line in lines):
        return Finding("C", status, path, unit, ["a model call or a response model changed"])
    return Finding("B", status, path, unit, ["code changed"])


def is_module(path: str) -> bool:
    return path.startswith(PACKAGE) and path.endswith(".py")


def classify(git: Git, base: str, target: str, units: dict[str, Unit]) -> list[Finding]:
    findings = []
    for status, old_path, path in git.name_status(base, target):
        if path == "pyproject.toml":
            findings.append(classify_pyproject(git.show(base, path), git.show(target, path)))
        elif path == "uv.lock":
            findings.append(classify_lock(git.show(base, path), git.show(target, path)))
        elif is_module(path) or is_module(old_path):
            # A module that moved out of the package, or lost its .py suffix, is still a module change.
            findings.append(classify_module(git, base, target, status, old_path, path, units))
        else:
            reason = "upstream test" if path.startswith("tests/") else "outside graphiti_core"
            findings.append(Finding("A", status, path, None, [reason], {"test"} if path.startswith("tests/") else set()))
    return findings


def diff_class(findings: list[Finding]) -> str:
    return max((f.cls for f in findings), default="A")


def version_kind(old: tuple[int, int, int] | None, new: tuple[int, int, int] | None) -> str:
    if old is None or new is None or old == new:
        return "none"
    return "major" if old[0] != new[0] else "minor" if old[1] != new[1] else "patch"


def sync_path(kind: str, cls: str) -> str:
    """The path of port/UPSTREAM.md: the version number sets it, and class C or D raises a patch."""
    path = kind if kind in ("minor", "major") else "patch"
    return "minor" if path == "patch" and cls in "CD" else path


def defects_to_check(findings: list[Finding], defects: list[tuple[str, list[str]]]) -> list[tuple[str, list[str]]]:
    """Each U row that names a changed file of a ported module or a changed upstream test."""
    changed = [f.path for f in findings if f.path.startswith("tests/") or (f.unit is not None and not f.unit.skipped)]
    def names(path: str, pattern: str) -> bool:
        if pattern.endswith("/"):
            return f"/{pattern}" in f"/{path}"
        return path == pattern or fnmatch.fnmatch(path, f"*/{pattern}")

    out = []
    for row, patterns in defects:
        hits = sorted({p for p in changed for pattern in patterns if names(p, pattern)})
        if hits:
            out.append((row, hits))
    return out


def pick_target(steps: list[tuple[str, str]]) -> tuple[str, str] | None:
    """The release that the next sync targets: the latest one, or the first one with a class D step."""
    if not steps:
        return None
    for tag, cls in steps:
        if cls == "D":
            return tag, "the first release with a class D step" if tag != steps[-1][0] else "the latest release"
    return steps[-1][0], "the latest release"


# ----------------------------------------------------------------------------- output


def report(git: Git, base: str, target: str, label: str, units: dict[str, Unit], defects: list[tuple[str, list[str]]]) -> str:
    findings = classify(git, base, target, units)
    old_version, new_version = (git.release_version(c) or version_of(git.show(c, "pyproject.toml")) for c in (base, target))
    kind, cls = version_kind(old_version, new_version), diff_class(findings)
    text = lambda v: ".".join(map(str, v)) if v else "unknown"
    lines = [f"upstream diff: {base[:7]} ({text(old_version)}) to {label} {target[:7]} ({text(new_version)})"]
    if git.run("merge-base", base, target, check=False).strip() != base:
        lines.append("warning: the pinned commit is not an ancestor of the target")
    counts = ", ".join(f"{c} {sum(f.cls == c for f in findings)}" for c in "ABCD")
    lines.append(f"version change: {kind}")
    lines.append(f"files changed: {len(findings)} ({counts})")
    lines.append("")
    order = sorted(findings, key=lambda f: (-"ABCD".index(f.cls), f.unit.order if f.unit else 0, f.path))
    for f in order:
        unit = f"{f.unit.module.removeprefix('graphiti_core.')} [{f.unit.order}, {f.unit.status}]" if f.unit else "-"
        lines.append(f"{f.cls}  {f.status}  {f.path}  unit: {unit}  reason: {'. '.join(f.reasons)}")
    stale = sorted({(f.unit.order, f.unit.module, f.unit.status) for f in findings if f.unit and f.cls in "BCD" and not f.unit.skipped})
    lines.append("")
    lines.append("units to port again, in queue order: " + (", ".join(f"{m.removeprefix('graphiti_core.')} [{o}, {s}]" for o, m, s in stale) or "none"))
    rows = defects_to_check(findings, defects)
    lines.append("U rows to check again: " + ("; ".join(f"{r} ({', '.join(h)})" for r, h in rows) or "none"))
    blind = [row for row, patterns in defects if not patterns]
    if blind:
        lines.append(f"U rows that name no file, so the tool cannot list them: {', '.join(blind)}")
    tests = sum("test" in f.kinds for f in findings)
    if tests:
        lines.append(f"upstream tests changed: {tests}. Compare them with tests/Port.Evaluation and the mined judge cases.")
    if any("environment" in f.kinds for f in findings):
        lines.append("the Python environment changed: build it again before the recording.")
    lines.append(f"diff class: {cls}")
    lines.append(f"path: {sync_path(kind, cls)}")
    if any(f.kinds & {"public", "stored"} for f in findings):
        lines.append("major path candidate: a public member or stored data changed. The maintainer decides.")
    return "\n".join(lines)


def releases_after(git: Git, base: str, pinned: tuple[int, int, int]) -> list[tuple[tuple[int, int, int], str]]:
    """Each release tag with a higher version than the pin, that the pinned commit does not hold."""
    held = set(git.run("tag", "--merged", base, "--list", "v*").split())
    found = []
    for tag in git.run("tag", "--list", "v*").split():
        match = RELEASE_TAG.match(tag)
        if match and tag not in held and (version := tuple(int(g) for g in match.groups())) > pinned:
            found.append((version, tag))
    return sorted(found)


def release_list(git: Git, base: str, pinned: tuple[int, int, int], units: dict[str, Unit], branch: str) -> tuple[str, bool]:
    lines = [f"pin: {base[:7]} ({'.'.join(map(str, pinned))})", "", "release  date  version change  step class  class from the pin  files from the pin"]
    previous, previous_version, steps = base, pinned, []
    for version, tag in releases_after(git, base, pinned):
        commit = git.commit(tag)
        step = diff_class(classify(git, previous, commit, units))
        whole = classify(git, base, commit, units)
        date = git.run("log", "-1", "--format=%cs", commit).strip()
        lines.append(f"{tag}  {date}  {version_kind(previous_version, version)}  {step}  {diff_class(whole)}  {len(whole)}")
        steps.append((tag, step))
        previous, previous_version = commit, version
    if not steps:
        lines.append("none")
    target = pick_target(steps)
    lines.append("")
    lines.append(f"target of the next sync: {target[0]} ({target[1]})" if target else "target of the next sync: none. The pin is at the latest release.")
    head = git.run("rev-parse", "--verify", "--quiet", f"{branch}^{{commit}}", check=False).strip()
    if head and head != previous:
        ahead = git.run("rev-list", "--count", f"{previous}..{head}").strip()
        after = steps[-1][0] if steps else "the pin"
        lines.append(f"not released: {branch} is {ahead} commits after {after}, class {diff_class(classify(git, previous, head, units))}")
    return "\n".join(lines), bool(steps)


def open_history(directory: Path, repo: str, fetch: bool) -> Git:
    git = Git(directory)
    if not (directory / "HEAD").exists():
        directory.parent.mkdir(parents=True, exist_ok=True)
        done = subprocess.run(["git", "clone", "-q", "--bare", "--filter=blob:none", repo, str(directory)], capture_output=True, text=True)
        if done.returncode != 0:
            stop(f"cannot clone {repo}: {done.stderr.strip()}")
    elif fetch:
        git.run("fetch", "-q", "--force", "origin", "+refs/heads/*:refs/heads/*", "+refs/tags/*:refs/tags/*")
    return git


# ----------------------------------------------------------------------------- self-test

FIXTURE_FILES = {
    "README.md": "readme\n",
    "pyproject.toml": '[project]\nname = "graphiti-core"\nversion = "0.1.0"\nrequires-python = ">=3.10"\ndependencies = [\n    "pydantic>=2.11",\n]\n',
    "uv.lock": '[[package]]\nname = "graphiti-core"\nversion = "0.1.0"\n\n[[package]]\nname = "pydantic"\nversion = "2.11.0"\n',
    "tests/test_helpers.py": "def test_one():\n    assert True\n",
    "graphiti_core/helpers.py": "# helpers\ndef lucene_sanitize(query):\n    return query.strip()\n",
    "graphiti_core/nodes.py": "class EntityNode:\n    name: str\n    summary: str = ''\n\n    def label(self):\n        return self.name\n",
    "graphiti_core/graph_queries.py": "def get_range_indices():\n    return ['CREATE INDEX entity_uuid IF NOT EXISTS FOR (n:Entity) ON (n.uuid)']\n",
    "graphiti_core/graphiti.py": "class Graphiti:\n    def __init__(self, uri=None):\n        self.uri = uri\n\n    async def add_episode(self, name: str) -> None:\n        return self._clean(name)\n\n    def _clean(self, name):\n        return name\n",
    "graphiti_core/prompts/extract_nodes.py": "def extract_text(context):\n    return 'Extract the entities.'\n",
    "graphiti_core/search/search_utils.py": "def fulltext_query(query, group_ids):\n    return ' OR '.join(group_ids) + ' AND ' + query\n",
    "graphiti_core/utils/node_operations.py": "async def extract(llm_client, messages):\n    reply = await llm_client.generate_response(\n        messages,\n        max_tokens=5,\n    )\n    return reply\n",
    "graphiti_core/driver/neo4j/entity_node_ops.py": "def save_query():\n    return 'MATCH (n:Entity {uuid: $uuid}) RETURN n'\n",
    "graphiti_core/driver/falkordb_driver.py": "def connect():\n    return 1\n",
    "graphiti_core/llm_client/openai_client.py": "MODEL = 'small'\n",
    "graphiti_core/migrations/group_ids.py": "def migrate():\n    return 1\n",
    "graphiti_core/llm_client/client.py": "def clean(text):\n    return text\n",
}
FIXTURE_UNITS = {
    "graphiti_core/helpers.py": "",
    "graphiti_core/nodes.py": "",
    "graphiti_core/graph_queries.py": "",
    "graphiti_core/graphiti.py": "",
    "graphiti_core/prompts/extract_nodes.py": "",
    "graphiti_core/search/search_utils.py": "",
    "graphiti_core/utils/node_operations.py": "",
    "graphiti_core/driver/neo4j/entity_node_ops.py": "",
    "graphiti_core/driver/falkordb_driver.py": "out-of-mvp: FalkorDB driver",
    "graphiti_core/llm_client/openai_client.py": "replaced: the adapter",
    "graphiti_core/migrations/group_ids.py": "",
    "graphiti_core/llm_client/client.py": "",
}
FIXTURE_GAPS = "| U4 | A full-text defect | `search/search_utils.py` (`fulltext_query`), `search_ops.py` | a case |\n| U14 | Another defect | `graphiti.py:1901` | a case |\n| U6 | Queries with no order | `driver/neo4j/` | a case |\n| U99 | A defect with no file | Upstream tests | a case |\n"
# Each row: name, files to write (None removes a file), the expected class of each named file, the expected diff class.
FIXTURE_CASES: list[tuple[str, dict[str, str | None], dict[str, str], str]] = [
    ("docs, a test, and the version number", {
        "README.md": "readme 2\n", "tests/test_helpers.py": "def test_one():\n    assert 1\n",
        "pyproject.toml": FIXTURE_FILES["pyproject.toml"].replace("0.1.0", "0.1.1"),
        "uv.lock": FIXTURE_FILES["uv.lock"].replace('"0.1.0"', '"0.1.1"'),
    }, {"README.md": "A", "tests/test_helpers.py": "A", "pyproject.toml": "A", "uv.lock": "A"}, "A"),
    ("a comment in a ported module", {"graphiti_core/helpers.py": "# helpers, new comment\ndef lucene_sanitize(query):\n\n    return query.strip()\n"}, {"graphiti_core/helpers.py": "A"}, "A"),
    ("modules that the MVP does not port", {"graphiti_core/driver/falkordb_driver.py": "def connect():\n    return 2\n", "graphiti_core/llm_client/openai_client.py": "MODEL = 'large'\n"}, {"graphiti_core/driver/falkordb_driver.py": "A", "graphiti_core/llm_client/openai_client.py": "A"}, "A"),
    ("a locked package", {"uv.lock": FIXTURE_FILES["uv.lock"].replace("2.11.0", "2.12.0")}, {"uv.lock": "A"}, "A"),
    ("the body of a ported function", {"graphiti_core/helpers.py": "# helpers\ndef lucene_sanitize(query):\n    return query.strip().lower()\n"}, {"graphiti_core/helpers.py": "B"}, "B"),
    ("a private method of Graphiti", {"graphiti_core/graphiti.py": FIXTURE_FILES["graphiti_core/graphiti.py"].replace("return name\n", "return name.strip()\n")}, {"graphiti_core/graphiti.py": "B"}, "B"),
    ("a method of a node class", {"graphiti_core/nodes.py": FIXTURE_FILES["graphiti_core/nodes.py"].replace("return self.name", "return self.name.upper()")}, {"graphiti_core/nodes.py": "B"}, "B"),
    ("a read query", {"graphiti_core/driver/neo4j/entity_node_ops.py": "def save_query():\n    return 'MATCH (n:Entity {uuid: $uuid}) RETURN n.name'\n"}, {"graphiti_core/driver/neo4j/entity_node_ops.py": "B"}, "B"),
    ("prompt text", {"graphiti_core/prompts/extract_nodes.py": "def extract_text(context):\n    return 'Extract each entity.'\n"}, {"graphiti_core/prompts/extract_nodes.py": "C"}, "C"),
    ("one keyword line of a model call", {"graphiti_core/utils/node_operations.py": FIXTURE_FILES["graphiti_core/utils/node_operations.py"].replace("max_tokens=5", "max_tokens=10")}, {"graphiti_core/utils/node_operations.py": "C"}, "C"),
    ("the code that builds each request", {"graphiti_core/llm_client/client.py": "def clean(text):\n    return text.strip()\n"}, {"graphiti_core/llm_client/client.py": "C"}, "C"),
    ("a new module", {"graphiti_core/new_module.py": "VALUE = 1\n"}, {"graphiti_core/new_module.py": "D"}, "D"),
    ("a removed module", {"graphiti_core/helpers.py": None}, {"graphiti_core/helpers.py": "D"}, "D"),
    ("a removed module that the MVP does not port", {"graphiti_core/driver/falkordb_driver.py": None}, {"graphiti_core/driver/falkordb_driver.py": "A"}, "A"),
    ("a public member of Graphiti", {"graphiti_core/graphiti.py": FIXTURE_FILES["graphiti_core/graphiti.py"].replace("name: str)", "name: str, group_id: str = '')")}, {"graphiti_core/graphiti.py": "D"}, "D"),
    ("a stored field", {"graphiti_core/nodes.py": FIXTURE_FILES["graphiti_core/nodes.py"].replace("summary: str = ''", "summary: str = ''\n    rank: int = 0")}, {"graphiti_core/nodes.py": "D"}, "D"),
    ("an index", {"graphiti_core/graph_queries.py": FIXTURE_FILES["graphiti_core/graph_queries.py"].replace("entity_uuid", "entity_id")}, {"graphiti_core/graph_queries.py": "D"}, "D"),
    ("a write clause", {"graphiti_core/driver/neo4j/entity_node_ops.py": "def save_query():\n    return 'MERGE (n:Entity {uuid: $uuid}) SET n.rank = $rank RETURN n'\n"}, {"graphiti_core/driver/neo4j/entity_node_ops.py": "D"}, "D"),
    ("a dependency", {"pyproject.toml": FIXTURE_FILES["pyproject.toml"].replace('"pydantic>=2.11",', '"pydantic>=2.11",\n    "numpy>=2",')}, {"pyproject.toml": "D"}, "D"),
    ("a renamed module", {"graphiti_core/helpers.py": None, "graphiti_core/text_helpers.py": FIXTURE_FILES["graphiti_core/helpers.py"]}, {"graphiti_core/text_helpers.py": "D"}, "D"),
    ("a module that leaves the package", {"graphiti_core/helpers.py": None, "src/helpers.py": FIXTURE_FILES["graphiti_core/helpers.py"]}, {"src/helpers.py": "D"}, "D"),
    ("a module with a name that git quotes", {"graphiti_core/caf\u00e9 list.py": "VALUE = 1\n"}, {"graphiti_core/caf\u00e9 list.py": "D"}, "D"),
    ("migration code", {"graphiti_core/migrations/group_ids.py": "def migrate():\n    return 2\n"}, {"graphiti_core/migrations/group_ids.py": "D"}, "D"),
    ("a dropped index", {"graphiti_core/driver/neo4j/entity_node_ops.py": "def save_query():\n    return 'DROP INDEX entity_uuid IF EXISTS'\n"}, {"graphiti_core/driver/neo4j/entity_node_ops.py": "D"}, "D"),
    ("a label write", {"graphiti_core/driver/neo4j/entity_node_ops.py": "def save_query():\n    return 'MATCH (n:Entity {uuid: $uuid}) SET n:Person RETURN n'\n"}, {"graphiti_core/driver/neo4j/entity_node_ops.py": "D"}, "D"),
    ("an index name", {"graphiti_core/helpers.py": FIXTURE_FILES["graphiti_core/helpers.py"] + "ENTITY_INDEX_NAME = 'entities'\n"}, {"graphiti_core/helpers.py": "D"}, "D"),
    ("the Python version", {"pyproject.toml": FIXTURE_FILES["pyproject.toml"].replace(">=3.10", ">=3.11")}, {"pyproject.toml": "D"}, "D"),
    ("a new public class beside Graphiti", {"graphiti_core/graphiti.py": FIXTURE_FILES["graphiti_core/graphiti.py"] + "\n\nclass AddEpisodeResults:\n    episode: str\n"}, {"graphiti_core/graphiti.py": "D"}, "D"),
    ("the default of a stored field", {"graphiti_core/nodes.py": FIXTURE_FILES["graphiti_core/nodes.py"].replace("summary: str = ''", "summary: str = 'none'")}, {"graphiti_core/nodes.py": "D"}, "D"),
    ("a file with a U row, with prompt text", {
        "graphiti_core/search/search_utils.py": "def fulltext_query(query, group_ids):\n    return '(' + ' OR '.join(group_ids) + ') AND ' + query\n",
        "graphiti_core/prompts/extract_nodes.py": "def extract_text(context):\n    return 'Extract all entities.'\n",
    }, {"graphiti_core/search/search_utils.py": "B", "graphiti_core/prompts/extract_nodes.py": "C"}, "C"),
]


def self_test() -> int:
    failures: list[str] = []
    checks = 0

    def expect(what: str, actual: object, wanted: object) -> None:
        nonlocal checks
        checks += 1
        if actual != wanted:
            failures.append(f"{what}: got {actual!r}, expected {wanted!r}")

    with tempfile.TemporaryDirectory() as folder:
        work = Path(folder)
        git = Git(work)
        identity = ["-c", "user.name=self-test", "-c", "user.email=self-test@example.invalid", "-c", "commit.gpgsign=false"]

        def commit(files: dict[str, str | None], message: str) -> str:
            for name, text in files.items():
                target = work / name
                if text is None:
                    target.unlink()
                else:
                    target.parent.mkdir(parents=True, exist_ok=True)
                    target.write_text(text, encoding="utf-8")
            git.run("add", "-A")
            git.run(*identity, "commit", "-q", "--no-verify", "-m", message)
            return git.run("rev-parse", "HEAD").strip()

        git.run("init", "-q", "-b", "main")
        base = commit(FIXTURE_FILES, "base")
        units = {path: Unit("graphiti_core." + path[len(PACKAGE):-3].replace("/", "."), n, "reviewed", note) for n, (path, note) in enumerate(FIXTURE_UNITS.items(), 1)}
        defects = read_defects(FIXTURE_GAPS)
        expect("U rows read", defects, [("U4", ["search/search_utils.py", "search_ops.py"]), ("U14", ["graphiti.py"]), ("U6", ["driver/neo4j/"]), ("U99", [])])
        expect("a missing file reads as empty", git.show(base, "no/such/file.py"), "")
        commits: dict[str, str] = {}

        last = base
        for name, files, wanted, wanted_class in FIXTURE_CASES:
            git.run("checkout", "-q", "--detach", base)
            last = commit(files, name)
            commits[name] = last
            findings = classify(git, base, last, units)
            by_path = {f.path: f.cls for f in findings}
            for path, cls in wanted.items():
                expect(f"{name}: {path}", by_path.get(path), cls)
            expect(f"{name}: diff class", diff_class(findings), wanted_class)

        findings = classify(git, base, last, units)
        expect("U rows to check", defects_to_check(findings, defects), [("U4", ["graphiti_core/search/search_utils.py"])])
        text = report(git, base, last, "target", units, defects)
        expect("report names the unit", "units to port again, in queue order: prompts.extract_nodes [5, reviewed], search.search_utils [6, reviewed]" in text, True)
        expect("report gives the class", "diff class: C" in text, True)
        expect("report names the U row with no file", "U rows that name no file, so the tool cannot list them: U99" in text, True)
        expect("a U row that names a folder", defects_to_check(classify(git, base, commits["a read query"], units), defects), [("U6", ["graphiti_core/driver/neo4j/entity_node_ops.py"])])
        expect("report gives the path", text.splitlines()[-1], "path: minor")

        # Releases: 0.1.1 has class A, 0.2.0 adds a module (class D), and 0.2.1 has class B.
        git.run("checkout", "-q", "main")
        # Upstream tagged v0.31.0 on a commit whose pyproject.toml said 0.32.0. The tag must decide.
        tagged = commit({"pyproject.toml": FIXTURE_FILES["pyproject.toml"].replace("0.1.0", "0.2.0")}, "0.1.1")
        git.run("tag", "v0.1.1")
        expect("the tag gives the version", git.release_version(tagged), (0, 1, 1))
        expect("a commit with no tag has no version", git.release_version(base), None)
        git.run("tag", "v0.1.2pre1")
        commit({"pyproject.toml": FIXTURE_FILES["pyproject.toml"].replace("0.1.0", "0.2.0"), "graphiti_core/new_module.py": "VALUE = 1\n"}, "0.2.0")
        git.run("tag", "v0.2.0")
        commit({"pyproject.toml": FIXTURE_FILES["pyproject.toml"].replace("0.1.0", "0.2.1"), "graphiti_core/helpers.py": "def lucene_sanitize(query):\n    return query\n"}, "0.2.1")
        git.run("tag", "v0.2.1")
        git.run("tag", "mcp-v9.9.9")
        commit({"README.md": "after the release\n"}, "not released")
        listing, newer = release_list(git, base, (0, 1, 0), units, "main")
        rows = [line.split("  ") for line in listing.splitlines() if line.startswith("v")]
        expect("release rows", [(r[0], r[2], r[3], r[4]) for r in rows], [("v0.1.1", "patch", "A", "A"), ("v0.2.0", "minor", "D", "D"), ("v0.2.1", "patch", "B", "D")])
        expect("a newer release exists", newer, True)
        expect("sync target", "target of the next sync: v0.2.0 (the first release with a class D step)" in listing, True)
        expect("unreleased line", "not released: main is 1 commits after v0.2.1, class A" in listing, True)

    expect("path of a patch with class B", sync_path("patch", "B"), "patch")
    expect("path of a patch with class C", sync_path("patch", "C"), "minor")
    expect("path of a minor with class A", sync_path("minor", "A"), "minor")
    expect("path of a major", sync_path("major", "D"), "major")
    expect("target with no class D", pick_target([("v1", "A"), ("v2", "C")]), ("v2", "the latest release"))
    expect("target with no release", pick_target([]), None)
    try:
        with contextlib.redirect_stderr(io.StringIO()):
            stop("a test of the exit code")
    except SystemExit as error:
        expect("an error ends with exit code 2", error.code, 2)

    for line in failures:
        print(f"FAIL {line}", file=sys.stderr)
    print(f"self-test: {checks - len(failures)} of {checks} checks passed")
    return 2 if failures else 0


# ----------------------------------------------------------------------------- main


def main() -> int:
    parser = argparse.ArgumentParser(description="Sorts the upstream changes after the pin into classes A to D.")
    parser.add_argument("target", nargs="?", help="an upstream tag, version, branch, or commit, for example v0.32.1")
    parser.add_argument("--list", action="store_true", help="print each release after the pin with its class")
    parser.add_argument("--self-test", action="store_true", help="check the rules on a repository that the test builds")
    parser.add_argument("--base", help="compare from this commit in place of the pinned commit")
    parser.add_argument("--history", type=Path, help="the folder of the bare clone of upstream")
    parser.add_argument("--no-fetch", action="store_true", help="do not fetch upstream before the comparison")
    parser.add_argument("--exit-code", action="store_true", help="with --list: exit with 1 if a release after the pin exists")
    args = parser.parse_args()

    if args.self_test:
        return self_test()
    if not args.list and not args.target:
        parser.error("give a target, --list, or --self-test")

    pin = read_pin(ROOT / "port/UPSTREAM_PIN")
    units = read_units(ROOT / "port/queue.json")
    defects = read_defects((ROOT / "port/GAPS.md").read_text(encoding="utf-8"))
    cache = Path(os.environ.get("XDG_CACHE_HOME") or Path.home() / ".cache")
    git = open_history(args.history or cache / "graphiti-net" / "upstream-history.git", pin["repo"], not args.no_fetch)
    base = git.commit(args.base or pin["commit"])

    if args.list:
        written = tuple(int(p) for p in pin["version"].split("."))
        pinned = git.release_version(base) or written
        if not args.base and pinned != written:
            print(f"upstream_diff: warning: port/UPSTREAM_PIN says {pin['version']}, and the tags of the pinned commit say {'.'.join(map(str, pinned))}", file=sys.stderr)
        branch = git.run("symbolic-ref", "--short", "HEAD", check=False).strip() or "main"
        listing, newer = release_list(git, base, pinned, units, branch)
        print(listing)
        return 1 if args.exit_code and newer else 0

    print(report(git, base, git.commit(args.target), args.target, units, defects))
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as error:  # An error must never look like exit code 1, which means a newer release.
        print(f"upstream_diff: {type(error).__name__}: {error}", file=sys.stderr)
        sys.exit(2)
