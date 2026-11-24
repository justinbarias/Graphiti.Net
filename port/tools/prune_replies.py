"""Removes the stored replies that no flow case requests.

A reply file collects replies over many recording runs. After a case text changes, the replies
to the old text stay in the file. This tool runs every flow case through the Python host, notes
each reply key that the host looks up, and writes each reply file again without the other rows.

    .upstream/graphiti/.venv/bin/python port/tools/prune_replies.py          # report only
    .upstream/graphiti/.venv/bin/python port/tools/prune_replies.py --write

Run it only when every flow case passes. It needs the Neo4j sidecar.
"""
from __future__ import annotations

import asyncio
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
JUDGE = ROOT / 'port' / 'judge'
sys.path.insert(0, str(JUDGE / 'host-python'))
os.chdir(ROOT)

import host  # noqa: E402
import judge_clients  # noqa: E402

used: dict[str, set[str]] = {}
_key = judge_clients.JudgeLLMClient._key


def key(self, messages, response_model):
    found, masked = _key(self, messages, response_model)
    if self.replies_path is not None:
        used.setdefault(self.replies_path.name, set()).add(found)
    return found, masked


judge_clients.JudgeLLMClient._key = key


async def one(case_id: str) -> None:
    """Runs one case and prints the reply keys that it used. One process for each case, like the runner."""
    for path in sorted((JUDGE / 'cases' / 'flow').glob('*.jsonl')):
        for line in path.read_text(encoding='utf-8').split('\n'):
            if line.strip() and json.loads(line)['id'] == case_id:
                result = await host.run_flow(json.loads(line))
                missing = len(result.get('missing_replies', []))
                print('PRUNE ' + json.dumps({'missing': missing, 'used': {k: sorted(v) for k, v in used.items()}}))


def main(write: bool) -> None:
    import subprocess

    for path in sorted((JUDGE / 'cases' / 'flow').glob('*.jsonl')):
        for line in path.read_text(encoding='utf-8').split('\n'):
            if not line.strip() or not json.loads(line).get('replies'):
                continue
            case_id = json.loads(line)['id']
            done = subprocess.run(
                [sys.executable, __file__, '--case', case_id], capture_output=True, text=True, check=False
            )
            rows = [row for row in done.stdout.split('\n') if row.startswith('PRUNE ')]
            if not rows:
                raise SystemExit(f'prune_replies: {case_id} did not run.\n{done.stderr[-2000:]}')
            report = json.loads(rows[-1][6:])
            if report['missing']:
                raise SystemExit(f'prune_replies: {case_id} has missing replies. Fix the case first.')
            for name, keys in report['used'].items():
                used.setdefault(name, set()).update(keys)
    for path in sorted((JUDGE / 'replies').glob('*.jsonl')):
        lines = [line for line in path.read_text(encoding='utf-8').split('\n') if line.strip()]
        keep = [line for line in lines if json.loads(line)['key'] in used.get(path.name, set())]
        print(f'prune_replies: {path.name}: {len(lines)} replies, {len(keep)} used')
        if write and len(keep) != len(lines):
            path.write_text(''.join(line + '\n' for line in keep), encoding='utf-8')


if __name__ == '__main__':
    if '--case' in sys.argv:
        asyncio.run(one(sys.argv[sys.argv.index('--case') + 1]))
    else:
        main('--write' in sys.argv[1:])
