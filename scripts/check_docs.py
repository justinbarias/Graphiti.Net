#!/usr/bin/env python3
"""Mechanical checks for the agent harness.

Checks: required paths exist, AGENTS.md stays within 100 lines, CLAUDE.md only sources
AGENTS.md, every local markdown link resolves to a file, and every managed document can be
reached from AGENTS.md by following links.

It does not check heading anchors, external links, or whether the text is still true.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
REQUIRED = [
    "AGENTS.md", "CLAUDE.md", "ARCHITECTURE.md", "REVIEW.md",
    "docs/design-docs/index.md", "docs/design-docs/core-beliefs.md",
    "docs/exec-plans/active", "docs/exec-plans/completed", "docs/exec-plans/tech-debt-tracker.md",
    "docs/generated/db-schema.md",
    "docs/product-specs/index.md", "docs/product-specs/spec.md",
    "docs/product-specs/new-user-onboarding.md",
    "docs/references/index.md",
    "docs/DESIGN.md", "docs/FRONTEND.md", "docs/PLANS.md", "docs/PRODUCT_SENSE.md",
    "docs/QUALITY_SCORE.md", "docs/RELIABILITY.md", "docs/SECURITY.md", "docs/TESTING.md",
    "port/STATE.md", "port/DECISIONS.md", "port/queue.json", "port/UPSTREAM_PIN",
]
MANAGED_ROOTS = ["docs", "port"]
MANAGED_TOP = ["ARCHITECTURE.md", "REVIEW.md"]
SKIP_DIRS = {".git", ".upstream", "bin", "obj", "StrykerOutput", "node_modules"}
LINK = re.compile(r"\[[^\]]*\]\(([^)\s]+)\)")
FENCE = re.compile(r"```.*?```", re.S)


def markdown_files() -> list[Path]:
    out = []
    for p in ROOT.rglob("*.md"):
        if not SKIP_DIRS.intersection(p.relative_to(ROOT).parts):
            out.append(p)
    return out


def local_links(md: Path) -> list[tuple[str, Path]]:
    text = FENCE.sub("", md.read_text(encoding="utf-8"))
    links = []
    for target in LINK.findall(text):
        if re.match(r"^[a-z][a-z0-9+.-]*:", target) or target.startswith("#"):
            continue
        path = target.split("#", 1)[0]
        if path:
            links.append((target, (md.parent / path).resolve()))
    return links


def main() -> int:
    errors: list[str] = []

    for rel in REQUIRED:
        if not (ROOT / rel).exists():
            errors.append(f"missing required path: {rel}")

    agents = ROOT / "AGENTS.md"
    if agents.exists():
        count = len(agents.read_text(encoding="utf-8").splitlines())
        if count > 100:
            errors.append(f"AGENTS.md has {count} lines, the limit is 100")

    claude = ROOT / "CLAUDE.md"
    if claude.exists() and claude.read_text(encoding="utf-8") != "@AGENTS.md\n":
        errors.append("CLAUDE.md must contain only '@AGENTS.md' and a final newline")

    graph: dict[Path, list[Path]] = {}
    for md in markdown_files():
        graph[md.resolve()] = []
        for target, resolved in local_links(md):
            if not resolved.exists():
                errors.append(f"{md.relative_to(ROOT)}: broken link {target}")
            else:
                graph[md.resolve()].append(resolved)

    seen: set[Path] = set()
    stack = [agents.resolve()]
    while stack:
        node = stack.pop()
        if node in seen:
            continue
        seen.add(node)
        stack.extend(graph.get(node, []))

    managed = [ROOT / m for m in MANAGED_TOP]
    for root in MANAGED_ROOTS:
        managed.extend(p for p in (ROOT / root).rglob("*.md"))
    for md in managed:
        if md.exists() and md.resolve() not in seen:
            errors.append(f"unreachable from AGENTS.md: {md.relative_to(ROOT)}")

    for e in errors:
        print(f"check_docs: {e}", file=sys.stderr)
    if not errors:
        print(f"check_docs: ok ({len(graph)} markdown files)")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
