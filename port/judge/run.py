#!/usr/bin/env python3
"""The judge runner. Python standard library only.

    python3 port/judge/run.py --build python            every case must pass
    python3 port/judge/run.py --build python --record   write golden files (needs the upstream venv)
    python3 port/judge/run.py --build dotnet            the failing set must equal expected-failures.txt

Options: --layer pure|flow, --filter TEXT (case id substring), --verbose.

The runner starts one host process, sends each case as one JSON line on standard input, and
reads one JSON line for each case from standard output. It applies the noise rules from
README.md to the result and compares it with the golden file.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
import re
import subprocess
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

JUDGE = Path(__file__).resolve().parent
ROOT = JUDGE.parent.parent
CASES = JUDGE / "cases"
GOLDEN = JUDGE / "golden"
COUNT_FILE = JUDGE / "count.txt"
EXPECTED_FAILURES = JUDGE / "expected-failures.txt"

HOSTS = {
    "python": [str(ROOT / ".upstream/graphiti/.venv/bin/python"), str(JUDGE / "host-python/host.py")],
    "dotnet": ["dotnet", str(ROOT / "tests/Graphiti.Net.JudgeHost/bin/Debug/net10.0/Graphiti.Net.JudgeHost.dll")],
}

FLOAT_TOLERANCE = 1e-6
VECTOR_MIN_LENGTH = 64
UUID_RE = re.compile(r"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}|(?<![0-9a-f])[0-9a-f]{32}(?![0-9a-f])")
ISO_RE = re.compile(r"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})?")
# Keys whose list values are unordered. The runner sorts them before it compares.
UNORDERED_KEYS = {"llm", "embed", "rerank", "queries", "nodes", "edges", "$set"}


# --------------------------------------------------------------------------- cases


def load_cases(layer: str | None, text_filter: str | None) -> list[dict]:
    """Returns every case as {"id", "layer", "file", "case"} in a stable order."""
    out: list[dict] = []
    for layer_dir in sorted(p for p in CASES.iterdir() if p.is_dir()):
        if layer and layer_dir.name != layer:
            continue
        for path in sorted(layer_dir.glob("*.jsonl")):
            for line_no, line in enumerate(path.read_text(encoding="utf-8").splitlines(), 1):
                if not line.strip():
                    continue
                case = json.loads(line)
                if "id" not in case:
                    raise SystemExit(f"{path}:{line_no}: case has no id")
                prefix = f"{layer_dir.name}.{path.stem}."
                if not case["id"].startswith(prefix):
                    raise SystemExit(f"{path}:{line_no}: case id must start with {prefix}")
                out.append({"id": case["id"], "layer": layer_dir.name, "file": path.stem, "case": case})
    ids = [c["id"] for c in out]
    duplicates = sorted({i for i in ids if ids.count(i) > 1})
    if duplicates:
        raise SystemExit(f"duplicate case ids: {duplicates[:5]}")
    if text_filter:
        out = [c for c in out if text_filter in c["id"]]
    return out


def load_golden() -> dict[str, object]:
    golden: dict[str, object] = {}
    if not GOLDEN.exists():
        return golden
    for path in sorted(GOLDEN.glob("*/*.jsonl")):
        for line in path.read_text(encoding="utf-8").splitlines():
            if line.strip():
                row = json.loads(line)
                golden[row["id"]] = row["result"]
    return golden


# --------------------------------------------------------------------------- noise rules


def _parse_iso(text: str) -> datetime | None:
    try:
        value = datetime.fromisoformat(text.replace("Z", "+00:00").replace(" ", "T"))
    except ValueError:
        return None
    if value.tzinfo is None:
        value = value.replace(tzinfo=timezone.utc)
    return value


def _mask_clock(text: str, window: tuple[datetime, datetime] | None) -> str:
    if window is None:
        return text

    def swap(match: re.Match) -> str:
        value = _parse_iso(match.group(0))
        if value is not None and window[0] <= value <= window[1]:
            return "<now>"
        return match.group(0)

    return ISO_RE.sub(swap, text)


def _compact_vector(values: list) -> dict:
    floats = [float(v) for v in values]
    return {"$vec": {"n": len(floats), "head": floats[:8], "sum": math.fsum(floats)}}


def _is_vector(value: object) -> bool:
    return (
        isinstance(value, list)
        and len(value) >= VECTOR_MIN_LENGTH
        and all(isinstance(v, (int, float)) and not isinstance(v, bool) for v in value)
    )


def _walk(value: object, on_string) -> object:
    """Copies a JSON value. Compacts vectors and passes every string through on_string."""
    if isinstance(value, str):
        return on_string(value)
    if _is_vector(value):
        return _compact_vector(value)  # type: ignore[arg-type]
    if isinstance(value, list):
        return [_walk(v, on_string) for v in value]
    if isinstance(value, dict):
        return {on_string(k): _walk(v, on_string) for k, v in value.items()}
    return value


def _label_text(value: object, labels: dict[str, str]) -> str:
    """JSON text of a value for UUID labelling: UUIDs become their current labels, floats are dropped."""

    def swap(match: re.Match) -> str:
        return labels.get(match.group(0), "<uuid>")

    def strip(item: object) -> object:
        if isinstance(item, str):
            return UUID_RE.sub(swap, item)
        if isinstance(item, float):
            return "<float>"
        if isinstance(item, list):
            parts = [strip(v) for v in item]
            return parts
        if isinstance(item, dict):
            out = {}
            for key in item:
                part = strip(item[key])
                if key in UNORDERED_KEYS and isinstance(part, list):
                    part = sorted(part, key=lambda v: json.dumps(v, sort_keys=True, ensure_ascii=False))
                out[UUID_RE.sub(swap, key)] = part
            return out
        return item

    return json.dumps(strip(value), sort_keys=True, ensure_ascii=False)


def _uuid_contexts(value: object, owner: object, out: dict[str, list[object]]) -> None:
    """For each UUID, collects the innermost dicts (or bare strings) in which it appears."""
    if isinstance(value, str):
        for found in set(UUID_RE.findall(value)):
            out.setdefault(found, []).append(owner if owner is not None else value)
    elif isinstance(value, list):
        for item in value:
            _uuid_contexts(item, owner, out)
    elif isinstance(value, dict):
        for key, item in value.items():
            _uuid_contexts(key, value, out)
            _uuid_contexts(item, value, out)


def _uuid_names(value: object) -> dict[str, str]:
    """Names each UUID by its content, so that the name does not depend on any order.

    A UUID starts with one shared label. Each round gives it a new label from the text of every
    place where it appears, with the other UUIDs shown as their labels. Rounds repeat until no
    label splits further. Two UUIDs that no content tells apart end with the same name.
    """
    contexts: dict[str, list[object]] = {}
    _uuid_contexts(value, None, contexts)
    labels = {found: "<uuid>" for found in contexts}
    distinct = 1
    for _ in range(12):
        cache: dict[int, str] = {}

        def text(item: object) -> str:
            if id(item) not in cache:
                cache[id(item)] = _label_text(item, labels)
            return cache[id(item)]

        fresh = {}
        for found, places in contexts.items():
            digest = hashlib.sha256()
            digest.update(labels[found].encode())
            for part in sorted(text(place) for place in places):
                digest.update(part.encode("utf-8"))
            fresh[found] = "<uuid:" + digest.hexdigest()[:10] + ">"
        labels = fresh
        count = len(set(labels.values()))
        if count == distinct:
            break
        distinct = count
    return labels


def _sort_unordered(value: object) -> object:
    if isinstance(value, list):
        return [_sort_unordered(v) for v in value]
    if isinstance(value, dict):
        out = {}
        for key, item in value.items():
            item = _sort_unordered(item)
            if key in UNORDERED_KEYS and isinstance(item, list):
                item = sorted(item, key=lambda v: json.dumps(v, sort_keys=True, ensure_ascii=False))
            out[key] = item
        return out
    return value


def normalize(result: object) -> object:
    """Applies the noise rules. The same function runs on golden recording and on comparison."""
    window = None
    if isinstance(result, dict) and isinstance(result.get("meta"), dict):
        meta = result["meta"]
        start, end = _parse_iso(str(meta.get("start", ""))), _parse_iso(str(meta.get("end", "")))
        if start and end:
            window = (start - timedelta(seconds=2), end + timedelta(seconds=2))
        result = {k: v for k, v in result.items() if k != "meta"}

    masked = _walk(result, lambda s: _mask_clock(s, window))
    names = _uuid_names(masked)

    def rename(text: str) -> str:
        return UUID_RE.sub(lambda match: names.get(match.group(0), match.group(0)), text)

    def rename_all(value: object) -> object:
        if isinstance(value, str):
            return rename(value)
        if isinstance(value, list):
            return [rename_all(v) for v in value]
        if isinstance(value, dict):
            return {rename(k): rename_all(v) for k, v in value.items()}
        return value

    # Sort after the rename, so that the order of an unordered list depends on content only.
    return _sort_unordered(rename_all(masked))


def differences(expected: object, actual: object, path: str = "$") -> list[str]:
    """Deep comparison. Numbers match within FLOAT_TOLERANCE. Everything else is exact."""
    if isinstance(expected, bool) or isinstance(actual, bool):
        return [] if expected is actual else [f"{path}: expected {expected!r}, got {actual!r}"]
    if isinstance(expected, (int, float)) and isinstance(actual, (int, float)):
        if isinstance(expected, int) and isinstance(actual, int):
            return [] if expected == actual else [f"{path}: expected {expected}, got {actual}"]
        if math.isclose(expected, actual, rel_tol=FLOAT_TOLERANCE, abs_tol=FLOAT_TOLERANCE):
            return []
        return [f"{path}: expected {expected}, got {actual}"]
    if type(expected) is not type(actual):
        return [f"{path}: expected {type(expected).__name__} {_short(expected)}, got {type(actual).__name__} {_short(actual)}"]
    if isinstance(expected, dict):
        out: list[str] = []
        for key in sorted(set(expected) | set(actual)):  # type: ignore[arg-type]
            if key not in actual:  # type: ignore[operator]
                out.append(f"{path}.{key}: missing")
            elif key not in expected:
                out.append(f"{path}.{key}: not expected")
            else:
                out.extend(differences(expected[key], actual[key], f"{path}.{key}"))  # type: ignore[index]
        return out
    if isinstance(expected, list):
        out = []
        if len(expected) != len(actual):  # type: ignore[arg-type]
            out.append(f"{path}: expected {len(expected)} items, got {len(actual)}")  # type: ignore[arg-type]
        for index, (left, right) in enumerate(zip(expected, actual)):  # type: ignore[arg-type]
            out.extend(differences(left, right, f"{path}[{index}]"))
        return out
    return [] if expected == actual else [f"{path}: expected {_short(expected)}, got {_short(actual)}"]


def _errors_in(result: object) -> list[str]:
    """Names the errors inside a recorded result, so that a person reviews each one."""
    found: list[str] = []
    if isinstance(result, dict):
        for key in ("error", "host_error"):
            if key in result:
                found.append(f"{key}={result[key]}")
        if result.get("missing_replies"):
            found.append(f"missing_replies={len(result['missing_replies'])}")
        for index, step in enumerate(result.get("steps", [])):
            if isinstance(step, dict) and "error" in step:
                found.append(f"step {index} {step.get('call')}: {step['error']}")
    return found


def _short(value: object) -> str:
    text = json.dumps(value, ensure_ascii=False)
    return text if len(text) <= 160 else text[:157] + "..."


# --------------------------------------------------------------------------- host


class Host:
    def __init__(self, build: str, record_replies: bool):
        command = HOSTS[build]
        if not Path(command[-1]).exists():
            raise SystemExit(f"host not found: {command[-1]}. Build it first (see port/judge/README.md).")
        env = dict(os.environ)
        env["PYTHONUNBUFFERED"] = "1"
        env["GRAPHITI_TELEMETRY_ENABLED"] = "false"
        if record_replies:
            env["JUDGE_RECORD_REPLIES"] = "1"
        self.process = subprocess.Popen(
            command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, text=True, encoding="utf-8", env=env, cwd=ROOT
        )

    def ask(self, case: dict) -> object:
        assert self.process.stdin and self.process.stdout
        self.process.stdin.write(json.dumps(case, ensure_ascii=False) + "\n")
        self.process.stdin.flush()
        line = self.process.stdout.readline()
        if not line:
            raise SystemExit(f"host exited while it ran case {case['id']} (exit code {self.process.poll()})")
        return json.loads(line)

    def close(self) -> None:
        if self.process.stdin:
            self.process.stdin.close()
        self.process.wait(timeout=60)


# --------------------------------------------------------------------------- main


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--build", choices=sorted(HOSTS), required=True)
    parser.add_argument("--record", action="store_true", help="write golden files from the Python build")
    parser.add_argument("--record-replies", action="store_true", help="call a real model for requests with no stored reply")
    parser.add_argument("--layer")
    parser.add_argument("--filter")
    parser.add_argument("--verbose", action="store_true")
    parser.add_argument("--list-failures", action="store_true", help="print the failing case ids, one for each line")
    parser.add_argument("--show-missing", action="store_true", help="print each request that has no stored reply")
    args = parser.parse_args()

    if (args.record or args.record_replies) and args.build != "python":
        raise SystemExit("only the Python build can record")

    cases = load_cases(args.layer, args.filter)
    partial = bool(args.layer or args.filter)
    golden = load_golden()
    host = Host(args.build, args.record_replies)

    results: dict[str, object] = {}
    failed: dict[str, list[str]] = {}
    try:
        for entry in cases:
            raw = host.ask(entry["case"])
            if args.show_missing and isinstance(raw, dict):
                for item in raw.get("missing_replies", []):
                    print(f"--- missing reply in {entry['id']}: key={item['key']} response_model={item['response_model']}")
                    for role, content in item["messages"]:
                        print(f"[{role}]\n{content}")
            actual = normalize(raw)
            results[entry["id"]] = actual
            if args.record:
                continue
            if entry["id"] not in golden:
                failed[entry["id"]] = ["no golden result. Record it from the Python build."]
                continue
            found = differences(golden[entry["id"]], actual)
            if found:
                failed[entry["id"]] = found
    finally:
        host.close()

    if args.record:
        by_file: dict[tuple[str, str], list[dict]] = {}
        for entry in cases:
            by_file.setdefault((entry["layer"], entry["file"]), []).append(entry)
        for (layer, name), entries in by_file.items():
            path = GOLDEN / layer / f"{name}.jsonl"
            path.parent.mkdir(parents=True, exist_ok=True)
            # Keep the rows of cases that this run did not select, in case-file order.
            rows = dict(results)
            if path.exists():
                for line in path.read_text(encoding="utf-8").splitlines():
                    if line.strip():
                        row = json.loads(line)
                        rows.setdefault(row["id"], row["result"])
            order = [c["id"] for c in load_cases(layer, None) if c["file"] == name and c["id"] in rows]
            lines = [json.dumps({"id": i, "result": rows[i]}, ensure_ascii=False, sort_keys=True) for i in order]
            path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        print(f"judge: recorded {len(cases)} cases into {len(by_file)} golden files")
        # A recorded error becomes the expected behavior. A person must read this list.
        for entry in cases:
            found = _errors_in(results[entry["id"]])
            if found:
                print(f"judge: review {entry['id']}: {', '.join(found)}")
        return 0

    if args.list_failures:
        print("\n".join(sorted(failed)))
        return 0

    minimum = int(COUNT_FILE.read_text().strip()) if COUNT_FILE.exists() else 0
    print(f"judge: build={args.build} cases={len(cases)} passed={len(cases) - len(failed)} failed={len(failed)}")
    status = 0

    if not partial and len(cases) < minimum:
        print(f"judge: FAIL the case count {len(cases)} is under the minimum {minimum} in count.txt", file=sys.stderr)
        status = 1

    if args.build == "python":
        for case_id, found in failed.items():
            print(f"FAIL {case_id}", file=sys.stderr)
            for line in found[: 20 if args.verbose else 3]:
                print(f"     {line}", file=sys.stderr)
        if failed:
            status = 1
    else:
        expected = set()
        if EXPECTED_FAILURES.exists():
            expected = {l.strip() for l in EXPECTED_FAILURES.read_text().splitlines() if l.strip() and not l.startswith("#")}
        if partial:
            expected &= {c["id"] for c in cases}
        surprise = sorted(set(failed) - expected)
        fixed = sorted(expected - set(failed))
        for case_id in surprise:
            print(f"FAIL {case_id} (not in expected-failures.txt)", file=sys.stderr)
            for line in failed[case_id][: 20 if args.verbose else 3]:
                print(f"     {line}", file=sys.stderr)
        for case_id in fixed:
            print(f"FAIL {case_id} passes now. Remove it from expected-failures.txt.", file=sys.stderr)
        if surprise or fixed:
            status = 1
        print(f"judge: expected failures={len(expected)} unexpected={len(surprise)} fixed={len(fixed)}")

    print("judge: passed" if status == 0 else "judge: FAILED")
    return status


if __name__ == "__main__":
    sys.exit(main())
