"""Records results of graphiti_core/ingestion/state_store.py and sqlite_state_store.py from real Python, for the
evaluation tests.

The judge has no case for these two modules. This file gives the evaluation tests (port/RULES.md, R23 and R25, gap J7):

- each module-level constant, also the schema text and the SQL text of the terminal statuses,
- is_released and is_stale_heartbeat for each status, with deadlines before, at, and after the current time,
- sequences of store calls from a fixed seed, with a fake clock that moves before and after each deadline. Most calls
  follow the life of a unit, so that the sequences reach CommitTurn, CommitWait, CommitRecover, and a non-empty
  enrichment. Fixed sequences follow them, with the clock exactly at a deadline, before it, and after it. Each
  sequence runs on InMemoryIngestionStateStore, and on SQLiteIngestionStateStore with a temp file and with
  ':memory:', as the upstream test runs its conformance class with two store fixtures. The record holds the
  result or the exception of each call, and the state at the end: the `_groups` of the in-memory store and the
  rows of each table of the SQLite file,
- one SQLite file that Python wrote, as hex text, with the rows of each table and the `sql` of sqlite_master,
- the busy timeout and the journal mode of the connection of a store, on a file and on ':memory:'.

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
    """One call. A seq or a fence comes mostly from an earlier result, so that the calls reach each state.

    Most calls follow the life of a unit that the earlier results show (admit, analyze, commit, enrich), so that the
    sequences reach CommitTurn, CommitWait, CommitRecover, and a non-empty enrichment. The other calls are random.
    """
    group = rng.choice(['g1', 'g1', 'g1', 'g2'])
    if rng.random() < 0.65:
        step = lifecycle_step(known)
        if step is not None:
            return step
    seqs = known['seqs'].get(group, [])
    seq = rng.choice(seqs) if seqs and rng.random() < 0.85 else rng.randint(1, 7)
    fences = known['fences'].get((group, seq), [])
    fence = fences[-1] if fences and rng.random() < 0.7 else (rng.choice(fences) if fences and rng.random() < 0.5 else rng.randint(0, 3))
    r = rng.random()
    if r < 0.09:
        return {'m': 'advance', 'seconds': rng.choice([0.5, 1.0, 1.0, 1.0, 5.0, 5.0, 30.0, 30.0, TTL, TTL + 1, 301.0])}
    if r < 0.24:
        return {'m': 'begin_admit', 'group_id': group, 'unit_id': rng.choice(['u1', 'u2', 'u3', 'u4', 'u5']), 'ttl': rng.choice([TTL, 600.0, 600.0, 600.0, 1.0])}
    if r < 0.34:
        return {'m': 'end_admit', 'group_id': group, 'seq': seq}
    if r < 0.40:
        return {'m': 'start_analysis', 'group_id': group, 'seq': seq}
    if r < 0.47:
        return {'m': 'save_change_set', 'group_id': group, 'seq': seq, 'payload': rng.choice(['', 'a1', '00ff'])}
    if r < 0.53:
        return {'m': 'heartbeat', 'group_id': group, 'seq': seq, 'ttl': rng.choice([TTL, 600.0]), 'fence': rng.choice([None, fence])}
    if r < 0.65:
        return {'m': 'begin_commit', 'group_id': group, 'seq': seq, 'ttl': rng.choice([TTL, 600.0, 600.0, 1.0])}
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


def lifecycle_step(known: dict):
    """The next call of a unit that has not finished, by the phase that the earlier results show."""
    live = [(g, s, p) for (g, s), p in known['phase'].items() if p != 'done']
    if not live:
        return None
    group, seq, phase = rng.choice(live)
    ttl = rng.choice([TTL, TTL, 600.0])
    fences = known['fences'].get((group, seq), [])
    fence = fences[-1] if fences else 1
    if phase == 'admitting':
        return {'m': 'end_admit', 'group_id': group, 'seq': seq}
    if phase == 'admitted':
        return rng.choice([
            {'m': 'start_analysis', 'group_id': group, 'seq': seq},
            {'m': 'save_change_set', 'group_id': group, 'seq': seq, 'payload': rng.choice(['', 'a1', '00ff'])},
        ])
    if phase == 'analyzed':
        return rng.choice([
            {'m': 'begin_commit', 'group_id': group, 'seq': seq, 'ttl': ttl},
            {'m': 'begin_commit', 'group_id': group, 'seq': seq, 'ttl': ttl},
            {'m': 'begin_commit', 'group_id': group, 'seq': seq, 'ttl': ttl},
            {'m': 'heartbeat', 'group_id': group, 'seq': seq, 'ttl': ttl, 'fence': None},
            {'m': 'advance', 'seconds': rng.choice([1.0, 5.0, 30.0, TTL])},
        ])
    if phase == 'turn':
        return rng.choice([
            {'m': 'save_final', 'group_id': group, 'seq': seq, 'fence': fence, 'payload': rng.choice(['f1', 'f2', ''])},
            {'m': 'save_final', 'group_id': group, 'seq': seq, 'fence': fence, 'payload': 'f1'},
            {'m': 'heartbeat', 'group_id': group, 'seq': seq, 'ttl': ttl, 'fence': fence},
            {'m': 'advance', 'seconds': rng.choice([1.0, 5.0, 30.0, TTL])},
        ])
    if phase == 'final':
        return rng.choice([
            {
                'm': 'finish_commit', 'group_id': group, 'seq': seq, 'fence': fence,
                'node_uuids': [rng.choice(['a', 'b', 'c']) for _ in range(rng.randint(0, 3))],
                'retain_seconds': rng.choice([0.0, 30.0, 1000.0]),
            },
            {'m': 'fail', 'group_id': group, 'seq': seq, 'reason': 'apply_failed', 'fence': fence},
            {'m': 'advance', 'seconds': rng.choice([1.0, 30.0, TTL, TTL + 1])},
            {'m': 'advance', 'seconds': TTL + 1},
            {'m': 'advance', 'seconds': TTL + 1},
        ])
    if phase == 'enrich':
        return rng.choice([
            {'m': 'ready_enrich', 'group_id': group, 'seq': seq},
            {'m': 'ready_enrich', 'group_id': group, 'seq': seq},
            {'m': 'ready_enrich', 'group_id': group, 'seq': seq},
            {'m': 'finish_enrich', 'group_id': group, 'seq': seq, 'node_uuids': rng.sample(['a', 'b', 'c'], rng.randint(0, 3))},
            {'m': 'load_delta', 'group_id': group, 'after_seq': rng.randint(0, 3), 'before_seq': rng.randint(2, 10), 'since': rng.choice([0.0, 1_000.0])},
            {'m': 'advance', 'seconds': rng.choice([1.0, 5.0, 30.0])},
        ])
    return None


def learn(known: dict, step: dict, result) -> None:
    if result is None or 'ok' not in result:
        return
    ok = result['ok']
    group = step.get('group_id')
    phase = known['phase']
    m = step['m']
    if isinstance(ok, dict):
        if ok.get('$type') == 'Admission':
            seqs = known['seqs'].setdefault(group, [])
            if ok['seq'] not in seqs:
                seqs.append(ok['seq'])
            if not ok['existing']:
                phase[(group, ok['seq'])] = 'admitting'
        elif ok.get('$type') == 'CommitTurn':
            known['fences'].setdefault((group, step['seq']), []).append(ok['fence'])
            phase[(group, step['seq'])] = 'final' if ok['final_payload'] is not None else 'turn'
        elif ok.get('$type') == 'CommitRecover':
            known['fences'].setdefault((group, ok['seq']), []).append(ok['fence'])
            phase[(group, ok['seq'])] = 'final'
        return
    if m == 'end_admit':
        phase[(group, step['seq'])] = 'admitted'
    elif m == 'save_change_set':
        phase[(group, step['seq'])] = 'analyzed'
    elif m == 'save_final':
        phase[(group, step['seq'])] = 'final'
    elif m == 'finish_commit':
        phase[(group, step['seq'])] = 'enrich'
    elif m in ('fail', 'finish_enrich'):
        phase[(group, step['seq'])] = 'done'


async def make_sequence(store, clock) -> tuple[list[dict], list]:
    """The calls and the results of the in-memory store. The next call reads the earlier results."""
    known: dict = {'seqs': {}, 'fences': {}, 'phase': {}}
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


def step(m: str, **kwargs) -> dict:
    return {'m': m, **kwargs}


def admit(seq: int, unit: str, ttl: float = TTL) -> list[dict]:
    return [step('begin_admit', group_id='g1', unit_id=unit, ttl=ttl), step('end_admit', group_id='g1', seq=seq)]


def commit(seq: int, nodes: list[str], retain: float, ttl: float = TTL) -> list[dict]:
    return [
        step('begin_commit', group_id='g1', seq=seq, ttl=ttl),
        step('finish_commit', group_id='g1', seq=seq, fence=1, node_uuids=nodes, retain_seconds=retain),
    ]


def fixed_sequences() -> list[tuple[str, list[dict]]]:
    """Sequences that put the clock exactly on a deadline, before it, and after it, and two more states that the random
    sequences rarely reach. A random sequence moves the clock by the steps of the generator, so it seldom stops at a
    deadline."""
    sequences: list[tuple[str, list[dict]]] = []
    for delta in (-1.0, 0.0, 1.0):
        # begin_commit while an earlier unit holds the turn: its deadline is 1060, and the clock is 1059, 1060, or 1061.
        sequences.append((f'earlier unit deadline {delta:+}', [
            *admit(1, 'u1'), *admit(2, 'u2'),
            step('advance', seconds=30.0),
            step('heartbeat', group_id='g1', seq=2, ttl=TTL, fence=None),
            step('advance', seconds=30.0 + delta),
            step('begin_commit', group_id='g1', seq=2, ttl=TTL),
        ]))
        # begin_commit of a unit that is in `applying` with its own deadline at the clock.
        sequences.append((f'own unit applying deadline {delta:+}', [
            *admit(1, 'u1'),
            step('begin_commit', group_id='g1', seq=1, ttl=TTL),
            step('save_final', group_id='g1', seq=1, fence=1, payload='f1'),
            step('advance', seconds=60.0 + delta),
            step('begin_commit', group_id='g1', seq=1, ttl=TTL),
        ]))
        # ready_enrich: the deadline of the earlier unit that has an entry is at the clock.
        sequences.append((f'ready_enrich deadline {delta:+}', [
            *admit(1, 'u1'), *commit(1, ['a'], 1000.0),
            step('advance', seconds=30.0),
            *admit(2, 'u2'), *commit(2, ['a'], 1000.0),
            step('advance', seconds=30.0 + delta),
            step('ready_enrich', group_id='g1', seq=2),
        ]))
    # _delete_old_units: the unit 3 has a snapshot of 3 and keeps the old units 1 and 2 (the limit is its snapshot).
    sequences.append(('old units and the snapshot limit', [
        *admit(1, 'u1', 600.0), *admit(2, 'u2', 600.0), *admit(3, 'u3', 600.0),
        step('save_change_set', group_id='g1', seq=1, payload='a1'),
        step('save_change_set', group_id='g1', seq=2, payload='a2'),
        step('start_analysis', group_id='g1', seq=3),
        *commit(1, [], 0.0, 600.0),
        step('advance', seconds=1.0),
        *commit(2, [], 0.0, 600.0),
        step('load_delta', group_id='g1', after_seq=0, before_seq=10, since=0.0),
    ]))
    # begin_commit with two earlier units: unit 1 expired in `applying` with its final change set (it is recovered), and
    # the live unit 2 is also earlier than unit 3. The order of the earlier units decides the result.
    sequences.append(('two earlier units, the first one is recovered', [
        *admit(1, 'u1', TTL), *admit(2, 'u2', 600.0), *admit(3, 'u3', 600.0),
        step('begin_commit', group_id='g1', seq=1, ttl=TTL),
        step('save_final', group_id='g1', seq=1, fence=1, payload='f1'),
        step('advance', seconds=61.0),
        step('begin_commit', group_id='g1', seq=3, ttl=600.0),
    ]))
    return sequences


async def record_one(records: list, folder: str, index: int, steps: list[dict] | None, name: str | None) -> None:
    from graphiti_core.ingestion.sqlite_state_store import SQLiteIngestionStateStore
    from graphiti_core.ingestion.state_store import InMemoryIngestionStateStore

    clock = FakeClock()
    memory = InMemoryIngestionStateStore(clock=clock)
    if steps is None:
        steps, memory_results = await make_sequence(memory, clock)
    else:
        memory_results = await run(memory, clock, steps)
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
    if memory_results != sqlite_results:
        raise SystemExit(f'sequence {index}: the in-memory store and the SQLite store give other results')
    record = {
        'f': 'sequence',
        'a': [steps],
        'r': {'memory': memory_results, 'memory_state': state, 'sqlite': sqlite_results, 'sqlite_rows': table_rows(path)},
    }
    if name is not None:
        record['n'] = name
    records.append(record)


async def record_sequences(records: list) -> None:
    with tempfile.TemporaryDirectory() as folder:
        for index in range(90):
            await record_one(records, folder, index, None, None)
        for index, (name, steps) in enumerate(fixed_sequences(), start=90):
            await record_one(records, folder, index, steps, name)


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


async def record_pragmas(records: list) -> None:
    """The busy timeout and the journal mode of the connection of the store, on a file and on ':memory:'."""
    from graphiti_core.ingestion.sqlite_state_store import SQLiteIngestionStateStore

    with tempfile.TemporaryDirectory() as folder:
        for name, path in (('file', os.path.join(folder, 'pragmas.db')), ('memory', ':memory:')):
            store = SQLiteIngestionStateStore(path, clock=FakeClock())
            result = {
                'busy_timeout': store._conn.execute('PRAGMA busy_timeout').fetchone()[0],
                'journal_mode': store._conn.execute('PRAGMA journal_mode').fetchone()[0],
            }
            await store.close()
            records.append({'f': 'pragmas', 'a': [name], 'r': result})


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
    asyncio.run(record_pragmas(records))
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
