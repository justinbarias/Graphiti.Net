#!/usr/bin/env python3
"""Lists the tests that the pruning rule of docs/TESTING.md deletes, from one Stryker JSON report.

    python3 scripts/mutation_survivors.py                  # the newest StrykerOutput/*/reports/mutation-report.json
    python3 scripts/mutation_survivors.py <report> --json  # one report, as JSON

It deletes nothing. It exits 0 when it can read the report, also when the list is not empty. It exits 2 when it cannot.

The rule, as the script applies it:

    Rule 2   A test that kills no mutant is listed.
    Rule 3   A test is listed when one other test kills every mutant that it kills, and more.
             Several tests that only together kill those mutants do not count.
    Equal    Tests that kill the same mutants, with no broader test, are not in the list, because neither
             is broader. The script shows each such group, and a person decides.
    Exempt   Tests in EnvironmentTests.cs. docs/TESTING.md says that the rule does not apply to them.

Rule 4 needs a person: the script lists each mutant that survived or has no coverage, with its file and line.

Limits:

- A report with no per-test data gets no list. This is a report with no test list, no detected mutant,
  or a killed mutant that has no `killedBy`. Rules 2 and 3 need every killer of every mutant, so keep
  `disable-bail` on. With bail on, the report holds data, but it is incomplete and the script cannot see that.
- "Broader" means a larger set of killed mutants. The script does not know the test layer.
- A timeout mutant counts as detected. If the report names no test for it and one test covers it,
  that test gets the credit. If more tests cover it, none gets credit, and a listed one has a note.
- A static mutant runs one time for the whole process (a static field, for example), so its per-test data
  is less exact. The script counts these mutants and uses their `killedBy` as it is.
- CompileError, RuntimeError, and Ignored mutants say nothing about a test.
- The result holds only for the code that the run mutated. A test of other code kills no mutant.
- A test is one entry of the report. A test method with many cases inside is one test.
"""
from __future__ import annotations

import argparse
import json
import sys
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
EXEMPT_FILES = ("EnvironmentTests.cs",)
DETECTED = ("Killed", "Timeout")
SURVIVORS = ("Survived", "NoCoverage")


def newest_report() -> Path | None:
    # Stryker names each run folder with its start time, so the name gives the order. A file time can change.
    return max(ROOT.glob("StrykerOutput/*/reports/mutation-report.json"), key=lambda p: p.parts[-3], default=None)


def short(path: str, root: str) -> str:
    base = root.rstrip("/\\")
    if base and path.startswith(base) and path[len(base):][:1] in ("/", "\\"):
        return path[len(base) + 1:]
    return path


def analyze(report: dict) -> dict:
    root = report.get("projectRoot", "")
    mutants = [(short(path, root), mutant) for path, entry in report["files"].items() for mutant in entry["mutants"]]
    counts = Counter(mutant["status"] for _, mutant in mutants)
    detected = counts["Killed"] + counts["Timeout"]
    valid = detected + counts["Survived"] + counts["NoCoverage"]
    result = {
        "score": {
            "killed": counts["Killed"], "survived": counts["Survived"], "no_coverage": counts["NoCoverage"],
            "timeout": counts["Timeout"], "ignored": counts["Ignored"], "compile_error": counts["CompileError"],
            "runtime_error": counts["RuntimeError"], "pending": counts["Pending"],
            "static": sum(1 for _, mutant in mutants if mutant.get("static")),
            "percent": round(100 * detected / valid, 2) if valid else None,
        },
        "survivors": sorted(
            ({"file": path, "line": m["location"]["start"]["line"], "column": m["location"]["start"]["column"],
              "status": m["status"], "mutator": m.get("mutatorName", ""), "replacement": m.get("replacement", ""),
              "static": bool(m.get("static"))}
             for path, m in mutants if m["status"] in SURVIVORS),
            key=lambda s: (s["file"], s["line"], s["column"])),
        "tests": 0, "delete": None, "equal": [], "exempt": [], "no_list_reason": None,
    }

    entries = [(test["id"], test["name"], short(path, root))
               for path, entry in report.get("testFiles", {}).items() for test in entry["tests"]]
    tests = {test: (name, path) for test, name, path in entries}
    result["tests"] = len(tests)
    kills: dict[str, set[int]] = {test: set() for test in tests}
    timeouts: Counter[str] = Counter()  # timeout mutants with no named killer, for each covering test
    unattributed = unknown = 0
    for index, (_, mutant) in enumerate(mutants):
        if mutant["status"] not in DETECTED:
            continue
        killers = mutant.get("killedBy") or []
        if not killers and mutant["status"] == "Killed":
            unattributed += 1
        elif not killers and len(mutant.get("coveredBy") or []) == 1:
            killers = mutant["coveredBy"]  # only this test ran the mutant, so it made the detection
        elif not killers:
            timeouts.update(mutant.get("coveredBy") or [])
        for test in killers:
            if test in kills:
                kills[test].add(index)
            else:
                unknown += 1

    if not tests:
        result["no_list_reason"] = "the report lists no tests"
    elif len(tests) < len(entries):
        result["no_list_reason"] = "two tests in the report have the same id"
    elif not detected:
        result["no_list_reason"] = "the report holds no killed or timeout mutant"
    elif unattributed:
        result["no_list_reason"] = f"{unattributed} killed mutants have no `killedBy`"
    elif unknown:
        result["no_list_reason"] = f"{unknown} `killedBy` entries name a test that the report does not list"
    if result["no_list_reason"]:
        return result

    order = sorted(tests, key=lambda t: (tests[t][1], tests[t][0], t))
    judged = []
    for test in order:
        if tests[test][1].replace("\\", "/").rsplit("/", 1)[-1] in EXEMPT_FILES:
            result["exempt"].append({"name": tests[test][0], "file": tests[test][1]})
        else:
            judged.append(test)

    # An exempt test is never the reason to delete another test.
    result["delete"] = []
    equal: dict[frozenset[int], list[str]] = {}
    for test in judged:
        mine = kills[test]
        broader = [other for other in judged if mine < kills[other]]
        keeper = None
        if not mine:
            rule, reason = "2", "kills no mutant"
        elif broader:
            keeper = max(broader, key=lambda other: len(kills[other]))
            rule, reason = "3", f"{tests[keeper][0]} kills all {len(mine)} of its mutants and {len(kills[keeper]) - len(mine)} more"
        else:
            equal.setdefault(frozenset(mine), []).append(tests[test][0])
            continue
        result["delete"].append({
            "name": tests[test][0], "file": tests[test][1], "id": test, "rule": rule, "reason": reason,
            "kills": len(mine), "covered_by": tests[keeper][0] if keeper else None, "unnamed_timeouts": timeouts[test],
        })
    result["equal"] = [{"kills": len(mutants_), "tests": names} for mutants_, names in equal.items() if len(names) > 1]
    return result


def render(path: Path, result: dict) -> str:
    score = result["score"]
    percent = "none" if score["percent"] is None else f"{score['percent']}%"
    lines = [
        f"report: {path}",
        f"score: {percent} (killed={score['killed']} survived={score['survived']} no_coverage={score['no_coverage']} "
        f"timeout={score['timeout']} ignored={score['ignored']})",
        f"other: compile_error={score['compile_error']} runtime_error={score['runtime_error']} "
        f"pending={score['pending']} static={score['static']}",
        "",
    ]
    if result["delete"] is None:
        lines.append(f"tests to delete: no list, because {result['no_list_reason']}")
    else:
        lines.append(f"tests to delete: {len(result['delete'])} of {result['tests']}")
        for test in result["delete"]:
            note = (f" [note: covers {test['unnamed_timeouts']} timeout mutants with no named killer]"
                    if test["unnamed_timeouts"] else "")
            lines.append(f"  {test['name']} ({test['file']})\n    rule {test['rule']}: {test['reason']}{note}")
        for group in result["equal"]:
            lines.append(f"equal, a person decides: these tests kill the same {group['kills']} mutants: {', '.join(group['tests'])}")
        lines += [f"exempt: {test['name']} ({test['file']})" for test in result["exempt"]]
    lines += ["", f"surviving and uncovered mutants: {len(result['survivors'])}"]
    for s in result["survivors"]:
        replacement = " ".join(s["replacement"].split())
        static = " static" if s["static"] else ""
        lines.append(f"  {s['file']}:{s['line']}:{s['column']} {s['status']}{static} {s['mutator']}: {replacement[:80]}")
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("report", nargs="?", type=Path, help="a mutation-report.json (default: the newest one)")
    parser.add_argument("--json", action="store_true", help="print JSON")
    args = parser.parse_args()
    path = args.report or newest_report()
    if path is None:
        print("mutation_survivors: no StrykerOutput/*/reports/mutation-report.json. Run scripts/mutate.sh.", file=sys.stderr)
        return 2
    try:
        result = analyze(json.loads(path.read_text(encoding="utf-8-sig")))
    except (OSError, ValueError, KeyError, TypeError, AttributeError, RecursionError) as error:
        print(f"mutation_survivors: cannot read the report {path}: {type(error).__name__}: {error}", file=sys.stderr)
        return 2
    print(json.dumps({"report": str(path), **result}, indent=1) if args.json else render(path, result))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
