"""Finds full-text searches in flow cases whose returned rows hold a score tie.

Upstream orders full-text results by score only. Neo4j returns two rows with the same score in
an order that can change between runs, so a case with such a tie gives an unstable golden result.
Run this tool on new flow cases before you record them. Change the case text until it prints no
TIE line.

    .upstream/graphiti/.venv/bin/python port/tools/find_score_ties.py - port/judge/cases/flow/*.jsonl
    .upstream/graphiti/.venv/bin/python port/tools/find_score_ties.py <text in the case id> <case file>

The tool runs each case through the Python host. Before each full-text query reaches Neo4j, the
tool runs the same search with its scores and reports each tie inside the row limit.
It needs the Neo4j sidecar and stored replies. It changes no file.
"""
from __future__ import annotations

import asyncio
import json
import os
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'port' / 'judge' / 'host-python'))
os.chdir(ROOT)

import host  # noqa: E402
import judge_clients  # noqa: E402

PROC = re.compile(r'db\.index\.fulltext\.(queryNodes|queryRelationships)\("([^"]+)"')
found: list[str] = []
current = {'case': ''}


async def _report(client, query: str, params: dict) -> None:
    match = PROC.search(str(query))
    if match is None:
        return
    var = 'node' if match.group(1) == 'queryNodes' else 'relationship'
    group_ids = params.get('group_ids')
    where = f'WHERE {var}.group_id IN $gids' if group_ids else ''
    try:
        rows, _, _ = await client.execute_query(
            f'CALL db.index.fulltext.{match.group(1)}("{match.group(2)}", $query, {{limit: 1000}}) '
            f'YIELD {var}, score {where} '
            f'RETURN coalesce({var}.name, "") + " | " + coalesce({var}.fact, "") AS label, '
            f'{var}.uuid AS uuid, score ORDER BY score DESC',
            query=params.get('query'),
            gids=group_ids,
        )
    except Exception:  # a case can send a query that Lucene rejects
        return
    limit = params.get('limit') or 10
    for index, (left, right) in enumerate(zip(rows, rows[1:])):
        if index < limit and left['score'] == right['score'] and left['uuid'] != right['uuid']:
            line = (
                f"TIE {current['case']} index={match.group(2)} limit={limit} score={left['score']:.6f}\n"
                f"      {left['label'][:70]!r}\n      {right['label'][:70]!r}\n"
                f"      query={str(params.get('query'))[:120]!r}"
            )
            if line not in found:
                found.append(line)
                print(line, flush=True)


_execute_query = judge_clients.RecordingNeo4jClient.execute_query
_tx_run = judge_clients._RecordingTx.run
_session_run = judge_clients._RecordingSession.run


async def execute_query(self, query, /, parameters_=None, **kwargs):
    params = dict(parameters_ or {})
    params.update({k: v for k, v in kwargs.items() if not k.endswith('_')})
    await _report(self._client, query, params)
    return await _execute_query(self, query, parameters_, **kwargs)


judge_clients.RecordingNeo4jClient.execute_query = execute_query


async def one(case_id: str, path: str) -> None:
    for line in Path(path).read_text(encoding='utf-8').split('\n'):
        if line.strip() and json.loads(line)['id'] == case_id:
            current['case'] = case_id
            result = await host.run_flow(json.loads(line))
            missing = len(result.get('missing_replies', []))
            print(f'done {case_id}' + (f' (missing replies: {missing})' if missing else ''), flush=True)


def main(only: str | None, files: list[str]) -> None:
    """Runs each case in its own process, like the runner, because upstream keeps state in a process."""
    import subprocess

    ties = 0
    for path in files:
        for line in Path(path).read_text(encoding='utf-8').split('\n'):
            if not line.strip():
                continue
            case_id = json.loads(line)['id']
            if only and only not in case_id:
                continue
            done = subprocess.run(
                [sys.executable, __file__, '--case', case_id, path], capture_output=True, text=True, check=False
            )
            if f'done {case_id}' not in done.stdout:
                raise SystemExit(f'find_score_ties: {case_id} did not run.\n{done.stderr[-2000:]}')
            ties += done.stdout.count('TIE ')
            print(done.stdout, end='', flush=True)
    print(f'find_score_ties: {ties} ties')


if __name__ == '__main__':
    if sys.argv[1] == '--case':
        asyncio.run(one(sys.argv[2], sys.argv[3]))
    else:
        main(None if sys.argv[1] == '-' else sys.argv[1], sys.argv[2:])
