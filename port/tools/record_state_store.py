"""Records results of graphiti_core/ingestion/state_store.py and sqlite_state_store.py from real Python, for the
evaluation tests.

The judge has no case for these two modules. This file gives the evaluation tests (port/RULES.md, R23 and R25, gap J7):

- each module-level constant, also the schema text and the SQL text of the terminal statuses,
- is_released and is_stale_heartbeat for each status, with deadlines before, at, and after the current time,
- sequences of store calls from a fixed seed, with a fake clock that moves before and after each deadline. Each
  sequence runs on InMemoryIngestionStateStore, and on SQLiteIngestionStateStore with a temp file and with
  ':memory:', as the upstream test runs its conformance class with two store fixtures. The record holds the
  result or the exception of each call, and the state at the end: the `_groups` of the in-memory store and the
  rows of each table of the SQLite file,
- one SQLite file that Python wrote, as hex text, with the rows of each table and the `sql` of sqlite_master.

    .upstream/graphiti/.venv/bin/python -B port/tools/record_state_store.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_state_store.py --check

It writes tests/Port.Evaluation/values/state_store.json. --check writes nothing and exits 1 if the file differs
from what the run would write. The environment variable GRAPHITI_UPSTREAM names the upstream folder, for a
worktree that has no .upstream. Nothing here uses the network. The temp files are removed at the end.

Each store gets the fake clock. The default clock of both stores is time.time, which the recording does not call.
"""
from __future__ import annotations

import asyncio
import json
import logging
import os
import random
import sqlite3
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM', str(ROOT / '.upstream' / 'graphiti')))
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'state_store.json'

TTL = 60.0
rng = random.Random(20261128)


class FakeClock:
    def __init__(self) -> None:
        self.now = 1_000.0

    def __call__(self) -> float:
        return self.now


def encode(value):
    from graphiti_core.ingestion import state_store as s

    if value is None or isinstance(value, (bool, int, float, str)):
        return value
    if isinstance(value, bytes):
        return {'$bytes': value.hex()}
    if isinstance(value, list):
        return [encode(v) for v in value]
    if isinstance(value, s.Admission):
        return {'$type': 'Admission', 'seq': value.seq, 'existing': value.existing, 'previous_seq': value.previous_seq}
    if isinstance(value, s.AnalysisStart):
        return {'$type': 'AnalysisStart', 'snapshot_seq': value.snapshot_seq, 'started_at': value.started_at}
    if isinstance(value, s.CommitTurn):
        return {'$type': 'CommitTurn', 'fence': value.fence, 'final_payload': encode(value.final_payload)}
    if isinstance(value, s.CommitWait):
        return {'$type': 'CommitWait'}
    if isinstance(value, s.CommitRecover):
        return {'$type': 'CommitRecover', 'seq': value.seq, 'fence': value.fence, 'final_payload': encode(value.final_payload)}
    if isinstance(value, s.DeltaUnit):
        return {'$type': 'DeltaUnit', 'seq': value.seq, 'status': value.status, 'payload': encode(value.payload)}
    raise TypeError(type(value))


def next_step(known: dict) -> dict:
    """One call. A seq or a fence comes mostly from an earlier result, so that the calls reach each state."""
    group = rng.choice(['g1', 'g1', 'g1', 'g2'])
    seqs = known['seqs'].get(group, [])
    seq = rng.choice(seqs) if seqs and rng.random() < 0.85 else rng.randint(1, 7)
    fences = known['fences'].get((group, seq), [])
    fence = fences[-1] if fences and rng.random() < 0.7 else (rng.choice(fences) if fences and rng.random() < 0.5 else rng.randint(0, 3))
    r = rng.random()
    if r < 0.07:
        return {'m': 'advance', 'seconds': rng.choice([0.5, 1.0, 1.0, 1.0, 30.0, TTL - 1, TTL, TTL + 1, 301.0])}
    if r < 0.24:
        return {'m': 'begin_admit', 'group_id': group, 'unit_id': rng.choice(['u1', 'u2', 'u3', 'u4', 'u5']), 'ttl': rng.choice([TTL, TTL, TTL, 1.0, 600.0])}
    if r < 0.34:
        return {'m': 'end_admit', 'group_id': group, 'seq': seq}
    if r < 0.40:
        return {'m': 'start_analysis', 'group_id': group, 'seq': seq}
    if r < 0.47:
        return {'m': 'save_change_set', 'group_id': group, 'seq': seq, 'payload': rng.choice(['', 'a1', '00ff'])}
    if r < 0.53:
        return {'m': 'heartbeat', 'group_id': group, 'seq': seq, 'ttl': rng.choice([TTL, 600.0]), 'fence': rng.choice([None, fence])}
    if r < 0.65:
        return {'m': 'begin_commit', 'group_id': group, 'seq': seq, 'ttl': rng.choice([TTL, TTL, TTL, 1.0])}
    if r < 0.72:
        return {'m': 'save_final', 'group_id': group, 'seq': seq, 'fence': fence, 'payload': rng.choice(['f1', 'f2', ''])}
    if r < 0.80:
        return {
            'm': 'finish_commit', 'group_id': group, 'seq': seq, 'fence': fence,
            'node_uuids': [rng.choice(['a', 'b', 'c']) for _ in range(rng.randint(0, 3))],
            'retain_seconds': rng.choice([0.0, 30.0, 1000.0]),
        }
    if r < 0.86:
        return {'m': 'fail', 'group_id': group, 'seq': seq, 'reason': rng.choice(['error', 'apply_failed', 'apply_failed', 'expired']), 'fence': rng.choice([None, fence, fence])}
    if r < 0.90:
        return {'m': 'ready_enrich', 'group_id': group, 'seq': seq}
    if r < 0.93:
        return {'m': 'finish_enrich', 'group_id': group, 'seq': seq, 'node_uuids': rng.sample(['a', 'b', 'c'], rng.randint(0, 3))}
    return {
        'm': 'load_delta', 'group_id': group, 'after_seq': rng.randint(0, 3), 'before_seq': rng.randint(2, 10),
        'since': rng.choice([0.0, 1_000.0, 1_100.0, 5_000.0]),
    }


def learn(known: dict, step: dict, result) -> None:
    if result is None or 'ok' not in result or not isinstance(result['ok'], dict):
        return
    ok = result['ok']
    group = step.get('group_id')
    if ok.get('$type') == 'Admission':
        seqs = known['seqs'].setdefault(group, [])
        if ok['seq'] not in seqs:
            seqs.append(ok['seq'])
    elif ok.get('$type') == 'CommitTurn':
        known['fences'].setdefault((group, step['seq']), []).append(ok['fence'])
    elif ok.get('$type') == 'CommitRecover':
        known['fences'].setdefault((group, ok['seq']), []).append(ok['fence'])


async def make_sequence(store, clock) -> tuple[list[dict], list]:
    """The calls and the results of the in-memory store. The next call reads the earlier results."""
    known: dict = {'seqs': {}, 'fences': {}}
    steps: list[dict] = []
    results: list = []
    for _ in range(rng.randint(15, 60)):
        step = next_step(known)
        result = (await run(store, clock, [step]))[0]
        learn(known, step, result)
        steps.append(step)
        results.append(result)
    return steps, results


async def run(store, clock: FakeClock, steps: list[dict]) -> list:
    from graphiti_core.errors import StaleCommitError, UnitExpiredError

    results = []
    for step in steps:
        m = step['m']
        if m == 'advance':
            clock.now += step['seconds']
            results.append(None)
            continue
        args = {k: v for k, v in step.items() if k != 'm'}
        if 'payload' in args:
            args['payload'] = bytes.fromhex(args['payload'])
        try:
            result = {'ok': encode(await getattr(store, m)(**args))}
        except (StaleCommitError, UnitExpiredError, ValueError) as error:
            result = {'err': [type(error).__name__, str(error)]}
        results.append(result)
    return results


def memory_state(store) -> dict:
    groups = {}
    for group_id, group in sorted(store._groups.items()):
        groups[group_id] = {
            'next_seq': group.next_seq,
            'watermark': group.watermark,
            'admit_seq': group.admit_seq,
            'units': [
                {
                    'seq': u.seq, 'unit_id': u.unit_id, 'status': u.status, 'deadline': u.deadline,
                    'snapshot_seq': u.snapshot_seq, 'started_at': u.started_at, 'ended_at': u.ended_at,
                    'fence': u.fence, 'fail_reason': u.fail_reason, 'payload': encode(u.payload),
                }
                for u in group.units.values()
            ],
            'unit_ids': dict(sorted(group.unit_ids.items())),
            'enrich': [[seq, uuids] for seq, uuids in group.enrich.items()],
        }
    return groups


def table_rows(path: str) -> dict:
    conn = sqlite3.connect(path)
    try:
        tables = {}
        for table, order in (
            ('graphiti_ingest_group', 'group_id'),
            ('graphiti_ingest_unit', 'group_id, seq'),
            ('graphiti_ingest_enrich', 'rowid'),
        ):
            cursor = conn.execute(f'SELECT * FROM {table} ORDER BY {order}')
            names = [d[0] for d in cursor.description]
            tables[table] = [dict(zip(names, (encode(v) for v in row), strict=True)) for row in cursor.fetchall()]
        tables['sqlite_master'] = [list(row) for row in conn.execute('SELECT type, name, tbl_name, sql FROM sqlite_master ORDER BY name')]
        return tables
    finally:
        conn.close()


async def record_sequences(records: list) -> None:
    from graphiti_core.ingestion.sqlite_state_store import SQLiteIngestionStateStore
    from graphiti_core.ingestion.state_store import InMemoryIngestionStateStore

    with tempfile.TemporaryDirectory() as folder:
        for index in range(90):
            clock = FakeClock()
            memory = InMemoryIngestionStateStore(clock=clock)
            steps, memory_results = await make_sequence(memory, clock)
            state = memory_state(memory)
            await memory.close()

            path = os.path.join(folder, f'run{index}.db')
            clock = FakeClock()
            store = SQLiteIngestionStateStore(path, clock=clock)
            sqlite_results = await run(store, clock, steps)
            await store.close()

            clock = FakeClock()
            in_memory_db = SQLiteIngestionStateStore(':memory:', clock=clock)
            if await run(in_memory_db, clock, steps) != sqlite_results:
                raise SystemExit(f'sequence {index}: the SQLite store on :memory: and on a file give other results')
            await in_memory_db.close()
            records.append({
                'f': 'sequence',
                'a': [steps],
                'r': {'memory': memory_results, 'memory_state': state, 'sqlite': sqlite_results, 'sqlite_rows': table_rows(path)},
            })


async def record_python_file(records: list) -> None:
    """A file that Python wrote, with the rows that Python reads back from it."""
    from graphiti_core.ingestion.sqlite_state_store import SQLiteIngestionStateStore

    with tempfile.TemporaryDirectory() as folder:
        path = os.path.join(folder, 'python.db')
        clock = FakeClock()
        store = SQLiteIngestionStateStore(path, clock=clock)
        steps = [
            {'m': 'begin_admit', 'group_id': 'g1', 'unit_id': 'u1', 'ttl': TTL},
            {'m': 'end_admit', 'group_id': 'g1', 'seq': 1},
            {'m': 'begin_admit', 'group_id': 'g1', 'unit_id': 'u2', 'ttl': TTL},
            {'m': 'end_admit', 'group_id': 'g1', 'seq': 2},
            {'m': 'start_analysis', 'group_id': 'g1', 'seq': 2},
            {'m': 'save_change_set', 'group_id': 'g1', 'seq': 2, 'payload': 'a1b2'},
            {'m': 'begin_commit', 'group_id': 'g1', 'seq': 1, 'ttl': TTL},
            {'m': 'save_final', 'group_id': 'g1', 'seq': 1, 'fence': 1, 'payload': 'f1'},
            {'m': 'finish_commit', 'group_id': 'g1', 'seq': 1, 'fence': 1, 'node_uuids': ['a', 'b', 'a'], 'retain_seconds': 1000.0},
            {'m': 'advance', 'seconds': 0.5},
            {'m': 'begin_admit', 'group_id': 'g-é', 'unit_id': 'u1', 'ttl': TTL},
            {'m': 'fail', 'group_id': 'g-é', 'seq': 1, 'reason': 'error'},
        ]
        results = await run(store, clock, steps)
        await store.close()
        records.append({
            'f': 'python_file',
            'a': [steps],
            'r': {'results': results, 'file': Path(path).read_bytes().hex(), 'rows': table_rows(path)},
        })


def record_functions(records: list) -> None:
    from graphiti_core.ingestion import state_store as s

    statuses = [s.STATUS_ADMITTING, s.STATUS_ADMITTED, s.STATUS_ANALYZED, s.STATUS_APPLYING, s.STATUS_COMMITTED, s.STATUS_FAILED, s.STATUS_APPLY_FAILED]
    for status in statuses:
        for deadline in (999.0, 1000.0, 1001.0):
            records.append({'f': 'is_released', 'a': [status, deadline, 1000.0], 'r': s.is_released(status, deadline, 1000.0)})
        for unit_fence in (0, 1, 2):
            for fence in (None, 0, 1, 2):
                records.append({'f': 'is_stale_heartbeat', 'a': [status, unit_fence, fence], 'r': s.is_stale_heartbeat(status, unit_fence, fence)})


def record_constants(records: list) -> None:
    from graphiti_core.ingestion import sqlite_state_store as q
    from graphiti_core.ingestion import state_store as s

    for name in ('STATUS_ADMITTING', 'STATUS_ADMITTED', 'STATUS_ANALYZED', 'STATUS_APPLYING', 'STATUS_COMMITTED',
                 'STATUS_FAILED', 'STATUS_APPLY_FAILED', 'REASON_APPLY_FAILED', 'REASON_EXPIRED'):
        records.append({'f': 'constant', 'a': ['state_store', name], 'r': getattr(s, name)})
    for name in ('TERMINAL_STATUSES', 'FAILED_STATUSES'):
        records.append({'f': 'constant', 'a': ['state_store', name], 'r': sorted(getattr(s, name))})
    for name in ('BUSY_TIMEOUT_SECONDS', '_SCHEMA', '_TERMINAL_SQL'):
        records.append({'f': 'constant', 'a': ['sqlite_state_store', name], 'r': getattr(q, name)})


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.dont_write_bytecode = True
    sys.path.insert(0, str(UPSTREAM))
    logging.disable(logging.CRITICAL)
    records: list = []
    record_constants(records)
    record_functions(records)
    asyncio.run(record_sequences(records))
    asyncio.run(record_python_file(records))
    lines = [json.dumps(r, ensure_ascii=True, allow_nan=False, separators=(',', ':')) for r in records]
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","sqlite":"' + sqlite3.sqlite_version
        + '","source":"graphiti_core/ingestion/state_store.py, graphiti_core/ingestion/sqlite_state_store.py","cases":[\n'
        + ',\n'.join(lines) + '\n]}\n'
    )
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(records)} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(records)} cases, {len(text.encode("utf-8"))} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
