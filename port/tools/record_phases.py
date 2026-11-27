"""Records results of graphiti_core/ingestion/phases.py from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_phases.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_phases.py --check

Plan 0004, layer 8, packet P8c. The tool has two parts.

The sync part calls new_report, _default_edge_type_map, _written_episode, _episode_uuid, _unique_by_uuid,
_unique_episodic_edges, _replace_entity_edges, _episode_context, _remap_edges, _move_end_date_to_earliest_contradiction,
_check_driver, and the sync members of UnitLease (renewed, write_budget). It also holds REPORT_KEYS.

The async part runs each async function that the port can reach with no `Graphiti` method: UnitLease.heartbeat,
_begin_admit, _fail_unit, _fail_admission, _written_episodes, _episodes_with_stored_links, rebase_change_set (and so
_repair_nodes, _remap_edges, _repair_edges), apply_change_set (and so _write_saga_links), _write_saga_links,
_apply_with_retries, _mark_apply_failed, _recover_unit, _build_saga_links (the paths that call no `Graphiti` method),
and _hydrate_nodes. The fakes are at the network boundary:

- The LLM client replaces generate_response. It records the prompt name, the response model name, the model size,
  max_tokens, group_id, attribute_extraction, and the role, the SHA-256, and the length of each message. It answers
  with the next reply of the case for the prompt name, or raises LookupError when the case has none left.
- The embedder gives the vector of the case table `vectors` for a text, or [i, 1.0, 0.0] for the i-th distinct text.
- The driver has a session and a transaction. The session records execute_write with the `timeout` attribute that
  unit_of_work(timeout=...) puts on the work function, and close. The transaction records each query with each
  parameter, in order. execute_query records the query and its parameters, and answers with the rows of the first
  entry of the case table `rows` whose needle is in the query. `ops` gives the driver a graph operations interface for
  _check_driver.
- The reader derives from GraphReader, as FakeGraph of tests/ingestion/ingestion_fixtures.py replaces the reader. It
  records each read and answers from the case: each call returns new objects.
- The state store is the real InMemoryIngestionStateStore with a fake clock, inside a wrapper that records each call
  (with no yield, as LoggingStore of the fixtures). A case can move the clock at a named fake call (`advance`), and a
  fake call can fail (`fail`).
- Each fake yields once (asyncio.sleep(0)) and then records its call. The calls, the store calls, and the log lines are
  one list. Each case keeps the largest count of fake calls in flight.

Values that the recording leaves out of the comparison, by name:
- The `*_ms` keys of a commit report: phases.py reads time.time() (the system clock) for them.
- The failure reason that _fail_unit stores, and the type name in the log line of _apply_with_retries: Python writes
  type(error).__name__ and the port the .NET type name (decision D50, R15). The recording writes `<type>`.
- Each uuid4 that the code makes (the edge UUIDs of a saga link): the test replaces each one, in the order of its
  first appearance, by "<new:N>".

The cancel scenarios: for each case with `cancel`, the recorder runs the case again for each fake call k. The fake that
makes call k records it and then cancels the main task. The recorder keeps the count of attempts (fake entries, also
an entry that the cancellation stops) and the events after call k.

Paths that wait for layer 9, because they call a method of `Graphiti` (graphiti._resolve_request_scope and the others)
and GraphitiCore is a skeleton: admit_episode, admit_bulk, analyze_episode, analyze_bulk, commit_unit, enrich_unit,
_save_analysis, and the paths of _build_saga_links that call graphiti._find_saga or
graphiti._saga_get_previous_episode_uuid. _admit_heartbeat is not recorded: its only effect is a heartbeat after
lease_seconds / 3 of real time. An apply on FalkorDB is not recorded: the bulk save of FalkorDB is a list of queries, and
the port of utils/bulk_utils.py throws NotSupportedException there (R24).

It writes tests/Port.Evaluation/values/phases.json. --check writes nothing and exits 1 if the file differs from what
the run would write. The tool needs Python 3.12 and the pinned upstream in the folder of the variable GRAPHITI_UPSTREAM,
or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import copy
import hashlib
import inspect
import json
import logging
import os
import re
import sys
import warnings
from datetime import datetime, timedelta, timezone
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'phases.json'

FIXED_NOW = datetime(2026, 3, 4, 5, 6, 7, 891011, tzinfo=timezone.utc)
T0 = datetime(2026, 1, 1, tzinfo=timezone.utc)
T1 = T0 + timedelta(days=10)
T2 = T0 + timedelta(days=20)
T3 = T0 + timedelta(days=30)
UUID4 = re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}')
TYPE_NAME = re.compile(r'failed: [A-Za-z_]+$')
VALIDATION_INPUT = re.compile(r', input_value=.*?, input_type=\w+\]')

X = [1.0, 0.0, 0.0]
Y = [0.0, 1.0, 0.0]
Z = [0.0, 0.0, 1.0]
NEAR_X = [0.9, 0.1, 0.0]

events: list[dict] = []
state: dict = {}


def dt(value):
    return None if value is None else {'$dt': value.isoformat()}


def encode(value):
    if isinstance(value, datetime):
        return dt(value)
    if isinstance(value, dict):
        return {str(k): encode(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [encode(v) for v in value]
    if hasattr(value, 'value') and type(value).__module__.startswith('graphiti_core'):
        return {'$enum': value.value}
    return value


def decode(value):
    if isinstance(value, dict):
        if set(value) == {'$dt'}:
            return datetime.fromisoformat(value['$dt'])
        return {k: decode(v) for k, v in value.items()}
    if isinstance(value, list):
        return [decode(v) for v in value]
    return value


def placeholders(value, known: dict):
    if isinstance(value, dict):
        return {k: placeholders(v, known) for k, v in value.items()}
    if isinstance(value, list):
        return [placeholders(v, known) for v in value]
    if isinstance(value, str):
        def repl(m):
            if m.group(0) not in known:
                known[m.group(0)] = f'<new:{len(known)}>'
            return known[m.group(0)]

        return UUID4.sub(repl, value)
    return value


# --- inputs --------------------------------------------------------------------------------------------------------


def node(uuid, name, emb=None, labels=None, group_id='g', **extra):
    return {'uuid': uuid, 'name': name, 'group_id': group_id, 'labels': labels or ['Entity'], 'created_at': dt(T0),
            'name_embedding': emb if emb is not None else X, **extra}


def edge(uuid, source, target, fact, emb=None, group_id='g', episodes=None, **extra):
    return {'uuid': uuid, 'group_id': group_id, 'source_node_uuid': source, 'target_node_uuid': target,
            'name': extra.pop('name', 'RELATES'), 'fact': fact, 'created_at': dt(T0),
            'fact_embedding': emb if emb is not None else X, 'episodes': episodes or [], **extra}


def episode(uuid='ep1', valid_at=T0, content='content', group_id='g', entity_edges=None):
    return {'uuid': uuid, 'name': uuid, 'group_id': group_id, 'source': 'text', 'source_description': 'test',
            'content': content, 'valid_at': dt(valid_at), 'created_at': dt(valid_at),
            'entity_edges': entity_edges or []}


def episodic(source, target, uuid=None):
    return {'uuid': uuid or f'ee-{source}-{target}', 'group_id': 'g', 'source_node_uuid': source,
            'target_node_uuid': target, 'created_at': dt(T0)}


def unit(seq=1, episodes=None, previous=None, bulk=False):
    episodes = episodes or [episode()]
    return {'group_id': 'g', 'unit_id': f'u{seq}', 'seq': seq, 'created_at': dt(T0), 'bulk': bulk,
            'episodes': episodes, 'previous_episodes': previous if previous is not None else [[] for _ in episodes]}


CS_MODULES: dict = {}


def change_set(seq=1, **fields):
    """A change set as the JSON of model_dump(mode='json'), which each side reads with model_validate."""
    ChangeSet = CS_MODULES['ChangeSet']
    EntityNode = CS_MODULES['EntityNode']
    EntityEdge = CS_MODULES['EntityEdge']
    EpisodicNode = CS_MODULES['EpisodicNode']
    EpisodicEdge = CS_MODULES['EpisodicEdge']
    SagaNode = CS_MODULES['SagaNode']
    values = dict(fields)
    values.setdefault('episodes', [episode()])
    values['episodes'] = [EpisodicNode(**decode(e)) for e in values['episodes']]
    values['create_nodes'] = [EntityNode(**decode(n)) for n in values.get('create_nodes', [])]
    values['create_edges'] = [EntityEdge(**decode(e)) for e in values.get('create_edges', [])]
    values['episodic_edges'] = [EpisodicEdge(**decode(e)) for e in values.get('episodic_edges', [])]
    if values.get('saga_node') is not None:
        values['saga_node'] = SagaNode(**decode(values['saga_node']))
    values.setdefault('node_uuids', [n.uuid for n in values['create_nodes']])
    values['operations'] = decode(values.get('operations', []))
    values['saga_links'] = decode(values.get('saga_links', []))
    cs = ChangeSet(group_id='g', unit_id=f'u{seq}', seq=seq, snapshot_seq=values.pop('snapshot_seq', 0),
                   analysis_started_at=1000.0, **values)
    return cs.model_dump(mode='json')


def end_edge(edge_uuid, invalid_at, expired_at, only_if_open=False):
    return {'op': 'end_edge', 'edge_uuid': edge_uuid, 'invalid_at': dt(invalid_at), 'expired_at': dt(expired_at),
            'only_if_open': only_if_open}


def add_episodes(edge_uuid, uuids):
    return {'op': 'add_episodes_to_edge', 'edge_uuid': edge_uuid, 'episode_uuids': uuids}


def set_attributes(edge_uuid, base, attributes):
    return {'op': 'set_edge_attributes', 'edge_uuid': edge_uuid, 'base_attributes': base, 'attributes': attributes}


def patch(node_uuid, labels=None, attributes=None):
    return {'op': 'patch_node', 'node_uuid': node_uuid, 'labels': labels or [], 'attributes': attributes or {}}


def resolutions(*items):
    return {'entity_resolutions': [{'id': i, 'name': name, 'duplicate_candidate_id': dup} for i, name, dup in items]}


def edge_reply(duplicates=(), contradicted=()):
    return {'duplicate_facts': list(duplicates), 'contradicted_facts': list(contradicted)}


SAGA_GET = 'MATCH (s:Saga {uuid: $uuid})'
FACT_EMBEDDING = 'RETURN e.fact_embedding AS fact_embedding'
EPISODES_GET = 'MATCH (e:Episodic)'


def saga_row(uuid='saga1', group_id='g', first=None, last=None):
    return {'uuid': uuid, 'name': 'Saga One', 'group_id': group_id, 'created_at': dt(T0), 'summary': 'stored summary',
            'first_episode_uuid': first, 'last_episode_uuid': last}


def saga_node(uuid='saga1', group_id='g', first=None, last=None):
    return {'uuid': uuid, 'name': 'Saga One', 'group_id': group_id, 'created_at': dt(T0), 'summary': 'object summary',
            'first_episode_uuid': first, 'last_episode_uuid': last}


def link(episode_uuid, previous=None, has='has-1', nxt=None, saga='saga1'):
    return {'saga_uuid': saga, 'episode_uuid': episode_uuid, 'previous_episode_uuid': previous,
            'has_episode_edge_uuid': has, 'next_episode_edge_uuid': nxt, 'created_at': dt(T0)}


# --- sync cases ----------------------------------------------------------------------------------------------------


def sync_cases() -> list[dict]:
    cases: list[dict] = [{'fn': 'new_report'}]
    for types in (None, {}, {'Works': None, 'Knows': None}):
        cases.append({'fn': '_default_edge_type_map', 'edge_types': None if types is None else list(types)})
    for raw in (True, False):
        cases.append({'fn': '_written_episode', 'store_raw': raw, 'episode': episode(entity_edges=['old']),
                      'entity_edges': ['e2', 'e1', 'e2', 'e3', 'e1']})
    cases.append({'fn': '_written_episode', 'store_raw': True, 'episode': episode(), 'entity_edges': []})
    for args in (['g', 'u1'], ['g', 'u1', 0], ['g', 'u1', 3], ['é', '😀', 1], ['', '', 0], ['g:x', 'u', -1],
                 ['g', 'u1', 12345678901]):
        cases.append({'fn': '_episode_uuid', 'args': args})
    cases.append({'fn': '_unique_by_uuid_nodes', 'records': [node('a', 'A1'), node('b', 'B'), node('a', 'A2'),
                                                             node('c', 'C'), node('b', 'B2')]})
    cases.append({'fn': '_unique_by_uuid_nodes', 'records': []})
    cases.append({'fn': '_unique_by_uuid_edges', 'records': [edge('e1', 'a', 'b', 'f1'), edge('e1', 'a', 'c', 'f2'),
                                                             edge('e2', 'a', 'b', 'f3')]})
    cases.append({'fn': '_unique_episodic_edges', 'edges': [episodic('ep1', 'a', 'x1'), episodic('ep1', 'b', 'x2'),
                                                            episodic('ep1', 'a', 'x3'), episodic('a', 'ep1', 'x4')]})
    eps = [episode('ep1', entity_edges=['e1', 'e2', 'e3', 'e2']), episode('ep2', T1, entity_edges=['e3', 'e4'])]
    for replaced, dropped, added in (
        ({'e1': 'x1'}, None, None),
        ({'e1': 'e2'}, ['e3'], None),
        ({}, [], ['e9', 'e1', 'e9']),
        ({'e3': 'x3', 'e4': 'x3'}, ['e1'], ['x3', 'n1']),
        ({}, None, []),
    ):
        case = {'fn': '_replace_entity_edges', 'episodes': eps, 'replaced': replaced}
        if dropped is not None:
            case['dropped'] = dropped
        if added is not None:
            case['added'] = added
        cases.append(case)
    cases.append({'fn': '_episode_context', 'unit': unit(1, [episode('ep1'), episode('ep2', T1, 'other')],
                                                         [[episode('p1')], []])})
    cases.append({'fn': '_episode_context', 'unit': unit(1, [episode('ep1'), episode('ep1', T1, 'second')],
                                                         [[episode('p1')], [episode('p2')]])})
    cases.append({'fn': '_episode_context', 'unit': unit(1, [episode('ep1'), episode('ep2', T1)], [[]])})
    cases.append({'fn': '_episode_context', 'unit': unit(1, [episode('ep1')], [[], []])})
    # _remap_edges: no map, a self edge, an exact duplicate with episodes, two directions with the same name.
    cases.append({'fn': '_remap_edges', 'change_set': change_set(1, create_edges=[edge('e1', 'a', 'b', 'f')])})
    cases.append({'fn': '_remap_edges', 'change_set': change_set(
        1, uuid_map={'a': 'b', 'c': 'd'},
        episodes=[episode('ep1', entity_edges=['e1', 'e2', 'e3', 'e4', 'e5'])],
        create_edges=[edge('e1', 'a', 'b', 'self'), edge('e2', 'c', 'x', 'Fact One', episodes=['ep1']),
                      edge('e3', 'd', 'x', 'fact one', episodes=['ep2', 'ep1', 'ep3']),
                      edge('e4', 'x', 'd', 'fact one'), edge('e5', 'x', 'd', 'fact one', name='OTHER')],
        episodic_edges=[episodic('ep1', 'a', 'x1'), episodic('ep1', 'b', 'x2'), episodic('ep1', 'c', 'x3')])})
    for valid, invalid, candidates in (
        (T0, T3, [T1, T2]),
        (T0, T3, [T2, T1]),
        (T0, T3, [T0, T3]),
        (T0, None, [T1]),
        (None, T3, [T1]),
        (T0, T3, [None, T1]),
        (T0, T3, []),
        (datetime(2026, 1, 1), datetime(2026, 1, 31), [datetime(2026, 1, 11)]),
    ):
        cases.append({'fn': '_move_end_date_to_earliest_contradiction',
                      'edge': edge('e1', 'a', 'b', 'f', valid_at=dt(valid), invalid_at=dt(invalid)),
                      'contradicted': [edge(f'c{i}', 'a', 'c', 'g', valid_at=dt(v)) for i, v in enumerate(candidates)]})
    for ops in ('none', 'partial', 'all', 'one_missing'):
        cases.append({'fn': '_check_driver', 'ops': ops})
    # UnitLease, sync members: write_budget with no deadline, on the limit, above it, and below it.
    for steps in (
        [['write_budget']],
        [['renewed', 1000.0], ['write_budget']],
        [['renewed', 1000.0], ['clock', 1055.0], ['write_budget']],
        [['renewed', 1000.0], ['clock', 1054.5], ['write_budget']],
        [['renewed', 1000.0], ['clock', 1056.0], ['write_budget']],
        [['renewed', 990.0], ['renewed', 1010.0], ['clock', 1060.0], ['write_budget']],
    ):
        cases.append({'fn': 'UnitLease', 'steps': steps})
    # UnitLease with the default clock (time.monotonic): no deadline, and a deadline long in the past.
    cases.append({'fn': 'UnitLease', 'steps': [['write_budget'], ['renewed', -1e12], ['write_budget']],
                  'default_clock': True})
    return cases


def run_sync(case: dict, mods) -> dict:
    ph = mods['phases']
    EntityNode, EntityEdge, EpisodicNode, EpisodicEdge = (mods['EntityNode'], mods['EntityEdge'], mods['EpisodicNode'],
                                                          mods['EpisodicEdge'])
    fn = case['fn']
    if fn == 'new_report':
        report = ph.new_report()
        return {'report': [[k, v, type(v).__name__] for k, v in report.items()]}
    if fn == '_default_edge_type_map':
        types = None if case['edge_types'] is None else {name: object() for name in case['edge_types']}
        result = ph._default_edge_type_map(types)
        return {'map': [[list(k), v] for k, v in result.items()]}
    if fn == '_written_episode':
        graphiti = SimpleNamespace(store_raw_episode_content=case['store_raw'])
        ep = EpisodicNode(**decode(case['episode']))
        written = ph._written_episode(graphiti, ep, case['entity_edges'])
        return {'written': episode_view(written), 'original': episode_view(ep), 'same': written is ep}
    if fn == '_episode_uuid':
        return {'uuid': ph._episode_uuid(*case['args'])}
    if fn in ('_unique_by_uuid_nodes', '_unique_by_uuid_edges'):
        cls = EntityNode if fn.endswith('nodes') else EntityEdge
        records = [cls(**decode(r)) for r in case['records']]
        result = ph._unique_by_uuid(records)
        return {'result': [[records.index(r), r.uuid] for r in result]}
    if fn == '_unique_episodic_edges':
        edges = [EpisodicEdge(**decode(e)) for e in case['edges']]
        return {'result': [e.uuid for e in ph._unique_episodic_edges(edges)]}
    if fn == '_replace_entity_edges':
        cs = mods['ChangeSet'].model_validate(change_set(1, episodes=case['episodes']))
        args = {}
        if 'dropped' in case:
            args['dropped'] = set(case['dropped'])
        if 'added' in case:
            args['added'] = list(case['added'])
        ph._replace_entity_edges(cs, dict(case['replaced']), **args)
        return {'episodes': [e.entity_edges for e in cs.episodes]}
    if fn == '_episode_context':
        u = admitted(case['unit'], mods)
        context = ph._episode_context(u)
        return {'context': [[k, ep.uuid, ep.content, [p.uuid for p in previous]]
                            for k, (ep, previous) in context.items()]}
    if fn == '_remap_edges':
        cs = mods['ChangeSet'].model_validate(case['change_set'])
        report = ph.new_report()
        ph._remap_edges(cs, report)
        return {'change_set': cs.model_dump(mode='json'), 'report': report_view(report)}
    if fn == '_move_end_date_to_earliest_contradiction':
        e = EntityEdge(**decode(case['edge']))
        contradicted = [EntityEdge(**decode(c)) for c in case['contradicted']]
        ph._move_end_date_to_earliest_contradiction(e, contradicted)
        return {'invalid_at': dt(e.invalid_at)}
    if fn == '_check_driver':
        ph._check_driver(fake_driver({'ops': case['ops']}, mods))
        return {}
    if fn == 'UnitLease':
        clock = FakeClock()
        store = mods['InMemoryIngestionStateStore'](clock=clock)
        config = mods['ConcurrentIngestionConfig'](window=4)
        lease = ph.UnitLease(store, config, 'g', 7) if case.get('default_clock') else ph.UnitLease(store, config, 'g', 7,
                                                                                                   clock)
        out = [{'fence': lease.fence, 'group_id': lease.group_id, 'seq': lease.seq}]
        for step in case['steps']:
            if step[0] == 'renewed':
                lease.renewed(step[1])
                out.append({'renewed': step[1]})
            elif step[0] == 'clock':
                clock.now = step[1]
            else:
                try:
                    out.append({'budget': lease.write_budget()})
                except Exception as error:  # noqa: BLE001 - the recording keeps each exception
                    out.append({'error': {'type': type(error).__name__, 'text': str(error)}})
        return {'steps': out}
    raise AssertionError(fn)


# --- shared views --------------------------------------------------------------------------------------------------


def episode_view(ep) -> dict:
    return {'uuid': ep.uuid, 'name': ep.name, 'group_id': ep.group_id, 'content': ep.content,
            'valid_at': dt(ep.valid_at), 'created_at': dt(ep.created_at), 'entity_edges': list(ep.entity_edges),
            'source': ep.source.value}


def node_view(n) -> dict:
    return {'uuid': n.uuid, 'name': n.name, 'group_id': n.group_id, 'labels': list(n.labels),
            'created_at': dt(n.created_at), 'summary': n.summary, 'attributes': encode(n.attributes),
            'name_embedding': n.name_embedding}


def edge_view(e) -> dict:
    return {'uuid': e.uuid, 'group_id': e.group_id, 'source_node_uuid': e.source_node_uuid,
            'target_node_uuid': e.target_node_uuid, 'name': e.name, 'fact': e.fact,
            'fact_embedding': e.fact_embedding, 'episodes': list(e.episodes), 'expired_at': dt(e.expired_at),
            'valid_at': dt(e.valid_at), 'invalid_at': dt(e.invalid_at), 'attributes': encode(e.attributes)}


def report_view(report: dict) -> list:
    # The *_ms keys read the system clock (time.time()), so the recording leaves them out.
    return [[k, float(v)] for k, v in report.items() if not k.endswith('_ms')]


def store_view(store) -> list:
    out = []
    for group_id, group in store._groups.items():
        units = []
        for seq, u in group.units.items():
            reason = u.fail_reason
            if reason is not None and reason not in ('expired', 'apply_failed'):
                reason = '<type>'
            units.append([seq, u.unit_id, u.status, u.deadline, u.snapshot_seq, u.started_at, u.ended_at, u.fence,
                          reason, u.payload is not None])
        out.append({'group_id': group_id, 'next_seq': group.next_seq, 'watermark': group.watermark,
                    'admit_seq': group.admit_seq, 'units': units, 'unit_ids': [[k, v] for k, v in group.unit_ids.items()],
                    'enrich': [[k, v] for k, v in group.enrich.items()]})
    return out


def admitted(data: dict, mods):
    EpisodicNode = mods['EpisodicNode']
    return mods['phases'].AdmittedUnit(
        group_id=data['group_id'], unit_id=data['unit_id'], seq=data['seq'], created_at=decode(data['created_at']),
        bulk=data['bulk'], episodes=[EpisodicNode(**decode(e)) for e in data['episodes']],
        previous_episodes=[[EpisodicNode(**decode(p)) for p in previous] for previous in data['previous_episodes']])


class FakeClock:
    def __init__(self, now: float = 1000.0):
        self.now = now

    def __call__(self) -> float:
        return self.now


# --- fakes ---------------------------------------------------------------------------------------------------------


async def enter(kind: str, record: dict, counted: bool = True) -> None:
    case = state['case']
    state['attempts'] += 1
    state['in_flight'] += 1
    state['max_in_flight'] = max(state['max_in_flight'], state['in_flight'])
    try:
        await asyncio.sleep(0)
        events.append({'kind': kind, **record})
        if not counted:
            return
        state['count'] += 1
        for item in case.get('advance', []):
            if item[0] == kind and item[1] > 0:
                item[1] -= 1
                state['clock'].now += item[2]
        if state['count'] - 1 == state['cancel_at']:
            state['task'].cancel()
        fails = case.get('fail', {})
        if fails.get(kind, 0) > 0:
            fails[kind] -= 1
            raise RuntimeError(f'{kind} failed')
    finally:
        state['in_flight'] -= 1


def fake_driver(case: dict, mods):
    GraphProvider = mods['GraphProvider']
    GraphOperationsInterface = mods['GraphOperationsInterface']

    class PartialOps(GraphOperationsInterface):
        async def node_save_bulk(self, _cls, driver, transaction, nodes, batch_size=100):
            raise AssertionError('not used')

        async def edge_update_bulk(self, _cls, driver, transaction, edges, batch_size=100):
            raise AssertionError('not used')

    class AllOps(PartialOps):
        async def episodic_node_save_bulk(self, _cls, driver, transaction, nodes, batch_size=100):
            raise AssertionError('not used')

        async def episodic_edge_save_bulk(self, _cls, driver, transaction, episodic_edges, batch_size=100):
            raise AssertionError('not used')

        async def edge_save_bulk(self, _cls, driver, transaction, edges, batch_size=100):
            raise AssertionError('not used')

        async def node_update_bulk(self, _cls, driver, transaction, nodes, batch_size=100):
            raise AssertionError('not used')

    class OneMissing(AllOps):
        pass

    OneMissing.node_update_bulk = GraphOperationsInterface.node_update_bulk

    class FakeTx:
        async def run(self, query, **kwargs):
            await enter('tx_run', {'query': query, 'keys': list(kwargs), 'values': encode(list(kwargs.values()))})

    class FakeSession:
        async def execute_write(self, func, *args, **kwargs):
            await enter('execute_write', {'timeout': getattr(func, 'timeout', None)})
            try:
                return await func(FakeTx(), *args, **kwargs)
            except BaseException as error:
                events.append({'kind': 'rollback', 'error': type(error).__name__})
                raise

        async def close(self):
            await enter('close', {}, counted=False)

    class FakeDriver:
        provider = GraphProvider(case.get('provider', 'neo4j'))
        search_interface = None
        graph_operations_interface = {'partial': PartialOps, 'all': AllOps,
                                      'one_missing': OneMissing}.get(case.get('ops', 'none'), lambda: None)()

        def session(self):
            events.append({'kind': 'session'})
            return FakeSession()

        async def execute_query(self, cypher_query_, **kwargs):
            await enter('query', {'query': cypher_query_, 'keys': list(kwargs), 'values': encode(list(kwargs.values()))})
            for needle, rows in case.get('rows', []):
                if needle in cypher_query_:
                    return decode(copy.deepcopy(rows)), None, None
            return [], None, None

    return FakeDriver()


def run_env(case: dict, mods):
    """The clients, the reader, the store, and the config of one case."""
    ph = mods['phases']
    from graphiti_core.llm_client.config import ModelSize
    from graphiti_core.tracer import NoOpTracer

    EntityNode, EntityEdge, EpisodicNode = mods['EntityNode'], mods['EntityEdge'], mods['EpisodicNode']
    replies = copy.deepcopy(case.get('replies', {}))
    texts: list[str] = []

    class FakeLlm:
        max_tokens = 16384

        async def generate_response(self, messages, response_model=None, max_tokens=None, model_size=ModelSize.medium,
                                    group_id=None, prompt_name=None, *, attribute_extraction=False, model=None):
            await enter('llm', {
                'prompt_name': prompt_name,
                'response_model': None if response_model is None else response_model.__name__,
                'max_tokens': max_tokens, 'model_size': model_size.value, 'group_id': group_id,
                'attribute_extraction': attribute_extraction,
                'messages': [[m.role, hashlib.sha256(m.content.encode('utf-8', 'surrogatepass')).hexdigest(),
                              len(m.content)] for m in messages],
            })
            queue = replies.get(prompt_name) or []
            if not queue:
                raise LookupError(f'no reply for {prompt_name}')
            return queue.pop(0)

    def vector(text):
        if text in case.get('vectors', {}):
            return list(case['vectors'][text])
        if text not in texts:
            texts.append(text)
        return [float(texts.index(text)), 1.0, 0.0]

    class FakeEmbedder:
        async def create(self, input_data):
            await enter('embed', {'input': list(input_data)})
            return vector(input_data[0])

        async def create_batch(self, input_data_list):
            await enter('embed_batch', {'input': list(input_data_list)})
            return [vector(t) for t in input_data_list]

    class FakeCrossEncoder:
        async def rank(self, query, passages):
            raise AssertionError('rank is not used')

    driver = fake_driver(case, mods)
    clients = mods['GraphitiClients'].model_construct(
        driver=driver, llm_client=FakeLlm(), embedder=FakeEmbedder(), cross_encoder=FakeCrossEncoder(),
        tracer=NoOpTracer(), max_coroutines=case.get('max_coroutines'), prompt_library=None)
    spec = case.get('reader', {})

    class FakeReader(mods['GraphReader']):
        async def node_candidates(self, nodes, identity_properties=None):
            await enter('read:node_candidates', {'nodes': [n.uuid for n in nodes],
                                                 'identity_properties': identity_properties})
            table = spec.get('node_candidates', {})
            return [[EntityNode(**decode(c)) for c in table.get(n.uuid, [])] for n in nodes]

        async def edge_duplicate_candidates(self, edges):
            await enter('read:edge_duplicate_candidates', {'edges': [e.uuid for e in edges]})
            table = spec.get('duplicates', {})
            return [[EntityEdge(**decode(c)) for c in table.get(e.uuid, [])] for e in edges]

        async def edge_invalidation_candidates(self, edges):
            await enter('read:edge_invalidation_candidates', {'edges': [e.uuid for e in edges]})
            table = spec.get('invalidations', {})
            return [[EntityEdge(**decode(c)) for c in table.get(e.uuid, [])] for e in edges]

        async def nodes_by_uuids(self, uuids, group_id):
            await enter('read:nodes_by_uuids', {'uuids': list(uuids), 'group_id': group_id})
            stored = {n['uuid']: n for n in spec.get('nodes', [])}
            return [EntityNode(**decode(stored[u])) for u in uuids if u in stored]

        async def episodes_by_uuids(self, uuids):
            await enter('read:episodes_by_uuids', {'uuids': list(uuids)})
            stored = {e['uuid']: e for e in spec.get('episodes', [])}
            return [EpisodicNode(**decode(stored[u])) for u in uuids if u in stored]

        async def edges_by_uuids(self, uuids):
            await enter('read:edges_by_uuids', {'uuids': list(uuids)})
            stored = {e['uuid']: e for e in spec.get('edges', [])}
            return [EntityEdge(**decode(stored[u])) for u in uuids if u in stored]

    clock = FakeClock(case.get('clock', 1000.0))
    state['clock'] = clock
    inner = mods['InMemoryIngestionStateStore'](clock=clock)
    config = mods['ConcurrentIngestionConfig'](**{'window': 4, **case.get('config', {})})

    def logged(name):
        async def call(self, *args):
            # Each parameter of the method, also one that the caller leaves at its default, so that the record does not
            # depend on the form of the call. A failure reason that Python writes with type(error).__name__ is `<type>`.
            bound = inspect.signature(getattr(inner, name)).bind(*args)
            bound.apply_defaults()
            shown = [a for a in bound.arguments.values() if not isinstance(a, bytes)]
            if name == 'fail' and shown[2] not in ('expired', 'apply_failed'):
                shown[2] = '<type>'
            events.append({'kind': 'store', 'method': name, 'args': encode(shown)})
            fails = state['case'].get('store_fail', {})
            if fails.get(name, 0) > 0:
                fails[name] -= 1
                raise RuntimeError(f'store {name} failed')
            return await getattr(inner, name)(*args)

        return call

    # LoggingStore of the fixtures: each call is recorded with no yield, and the inner store runs it. A payload (bytes)
    # is left out of the record: the compressed bytes differ between the two sides (GAPS F39).
    LoggingStore = type('LoggingStore', (mods['IngestionStateStore'],), {
        name: logged(name) for name in ('begin_admit', 'end_admit', 'start_analysis', 'save_change_set', 'heartbeat',
                                        'begin_commit', 'load_delta', 'save_final', 'finish_commit', 'fail',
                                        'ready_enrich', 'finish_enrich', 'close')})
    store = LoggingStore()
    return clients, FakeReader(clients), store, inner, config, clock


async def setup_store(store, inner, case: dict, config, clock, mods) -> list:
    """Runs the setup steps on the inner store, with no record. Each begin_commit result is kept."""
    results = []
    for step in case.get('setup', []):
        op = step[0]
        if op == 'admit':
            admission = await inner.begin_admit('g', step[1], config.lease_seconds)
            await inner.end_admit('g', admission.seq)
        elif op == 'admitting':
            await inner.begin_admit('g', step[1], config.lease_seconds)
        elif op == 'analyze':
            await inner.start_analysis('g', step[1])
            await inner.save_change_set('g', step[1], mods['ChangeSet'].model_validate(step[2]).to_bytes())
        elif op == 'begin_commit':
            results.append(await inner.begin_commit('g', step[1], config.lease_seconds))
        elif op == 'save_final':
            await inner.save_final('g', step[1], step[2], mods['ChangeSet'].model_validate(step[3]).to_bytes())
        elif op == 'finish_commit':
            await inner.finish_commit('g', step[1], step[2], step[3], config.index_lag_seconds)
        elif op == 'fail':
            await inner.fail('g', step[1], step[2], step[3])
        elif op == 'advance':
            clock.now += step[1]
        else:
            raise AssertionError(op)
    return results


def commit_view(result) -> dict:
    name = type(result).__name__
    if name == 'CommitTurn':
        return {'type': name, 'fence': result.fence, 'final': result.final_payload is not None}
    if name == 'CommitRecover':
        return {'type': name, 'seq': result.seq, 'fence': result.fence}
    return {'type': name}


# --- async cases ---------------------------------------------------------------------------------------------------


def lease_cases() -> list[dict]:
    cases = []
    applying = [['admit', 'u1'], ['analyze', 1, change_set(1)], ['begin_commit', 1], ['save_final', 1, 1,
                                                                                      change_set(1)]]
    for setup, fence, steps in (
        ([['admit', 'u1']], None, ['heartbeat', 'write_budget']),
        (applying, 1, ['heartbeat', 'write_budget']),
        (applying, 3, ['heartbeat', 'write_budget']),
        (applying, None, ['heartbeat', 'write_budget']),
        ([['admit', 'u1'], ['advance', 61.0]], None, ['heartbeat']),
        ([['admit', 'u1'], ['fail', 1, 'expired', None]], None, ['heartbeat']),
        ([['admit', 'u1'], ['advance', 60.0]], None, ['heartbeat']),
        ([['admit', 'u1'], ['advance', 59.0]], None, ['heartbeat', 'write_budget']),
    ):
        cases.append({'fn': 'UnitLease.heartbeat', 'setup': setup, 'fence': fence, 'steps': steps})
    return cases


def admit_cases() -> list[dict]:
    return [
        {'fn': '_begin_admit', 'unit_id': 'u-new'},
        {'fn': '_begin_admit', 'unit_id': 'u1', 'setup': [['admit', 'u1']]},
        {'fn': '_begin_admit', 'unit_id': 'u1', 'setup': [['admit', 'u1'], ['advance', 61.0]]},
        {'fn': '_begin_admit', 'unit_id': 'u1', 'setup': [['admit', 'u1'], ['fail', 1, 'expired', None]]},
        # A different unit holds the admit lease. The fake wait moves the clock past its deadline.
        {'fn': '_begin_admit', 'unit_id': 'u2', 'setup': [['admitting', 'u1']], 'wait_advance': 30.0},
        # A cancellation while the store call runs: the call completes and the unit is released.
        {'fn': '_begin_admit', 'unit_id': 'u2', 'cancel_in_shield': True},
        {'fn': '_begin_admit', 'unit_id': 'u1', 'setup': [['admit', 'u1']], 'cancel_in_shield': True},
        {'fn': '_fail_unit', 'seq': 1, 'setup': [['admit', 'u1']]},
        {'fn': '_fail_unit', 'seq': 1, 'setup': [['admit', 'u1'], ['analyze', 1, change_set(1)],
                                                 ['begin_commit', 1], ['save_final', 1, 1, change_set(1)]]},
        {'fn': '_fail_unit', 'seq': 9},
        {'fn': '_fail_admission', 'seq': 1, 'existing': False, 'setup': [['admitting', 'u1']]},
        {'fn': '_fail_admission', 'seq': 1, 'existing': True, 'setup': [['admitting', 'u1']]},
        {'fn': '_written_episodes', 'uuids': []},
        {'fn': '_written_episodes', 'uuids': ['ep1', 'ep2'], 'group_id': 'g',
         'rows': [[EPISODES_GET, [episode('ep2', T1, 'stored', entity_edges=['e1']), episode('ep1')]]]},
        {'fn': '_written_episodes', 'uuids': ['ep1'], 'group_id': 'h', 'rows': []},
        {'fn': '_episodes_with_stored_links',
         'episodes': [episode('ep1', entity_edges=['n1', 'n2']), episode('ep2', T1, entity_edges=['n3']),
                      episode('ep3', T2, entity_edges=['n4'])],
         'reader': {'episodes': [episode('ep1', entity_edges=['s1', 'n2', 's2']), episode('ep2', T1)]}},
        {'fn': '_episodes_with_stored_links', 'episodes': []},
    ]


def rebase_cases() -> list[dict]:
    ep1 = episode('ep1')
    cases = []

    def rebase(name, cs, reader=None, delta=(), replies=None, u=None, identity=None, **extra):
        case = {'fn': 'rebase_change_set', 'name': name, 'change_set': cs, 'reader': reader or {},
                'delta': [list(d) for d in delta], 'unit': u or unit(1), 'identity_properties': identity}
        if replies:
            case['replies'] = replies
        case.update(extra)
        cases.append(case)

    rebase('empty', change_set(1), defaults=True)
    # C3: an exact name match of the graph merges with no model call.
    rebase('c3_exact', change_set(
        1, create_nodes=[node('n-new', 'Alice'), node('n-b', 'Bob', Y)],
        create_edges=[edge('e1', 'n-new', 'n-b', 'Alice knows Bob', episodes=['ep1'])],
        episodes=[episode('ep1', entity_edges=['e1'])], episodic_edges=[episodic('ep1', 'n-new'), episodic('ep1', 'n-b')],
        node_first_episode={'n-new': 'ep1', 'n-b': 'ep1'}),
        reader={'node_candidates': {'n-new': [node('n-old', 'Alice')]}}, cancel=True)
    # C3: the rules do not resolve, so the model decides: a duplicate, then no duplicate.
    rebase('c3_model_merge', change_set(1, create_nodes=[node('n-new', 'Alicia Smith', NEAR_X)]),
           reader={'node_candidates': {'n-new': [node('n-old', 'Alice S.', X)]}},
           replies={'dedupe_nodes.nodes': [resolutions((0, 'Alicia Smith', 0))]}, cancel=True)
    rebase('c3_model_keep', change_set(1, create_nodes=[node('n-new', 'Alicia Smith', NEAR_X)]),
           reader={'node_candidates': {'n-new': [node('n-old', 'Alice S.', X)]}},
           replies={'dedupe_nodes.nodes': [resolutions((0, 'Alicia Smith', -1))]})
    rebase('c3_model_fails', change_set(1, create_nodes=[node('n-new', 'Alicia Smith', NEAR_X)]),
           reader={'node_candidates': {'n-new': [node('n-old', 'Alice S.', X)]}})
    # A reply that is not a NodeResolutions: the pydantic error leaves the commit.
    rebase('c3_model_bad_reply', change_set(1, create_nodes=[node('n-new', 'Alicia Smith', NEAR_X)]),
           reader={'node_candidates': {'n-new': [node('n-old', 'Alice S.', X)]}},
           replies={'dedupe_nodes.nodes': [{'entity_resolutions': [{'id': 'zero', 'name': 'Alicia Smith'}]}]})
    delta_bob = (2, 'committed', change_set(2, create_nodes=[node('n-old', 'Bob', Y)], episodes=[episode('ep2', T1)]))
    # C3: a delta node that the search does not return is a candidate when it is in the graph.
    rebase('c3_delta_node', change_set(3, create_nodes=[node('n-new', 'Bob', Y)]),
           reader={'nodes': [node('n-old', 'Bob', Y)]}, delta=[delta_bob], u=unit(3))
    rebase('c3_delta_node_missing', change_set(3, create_nodes=[node('n-new', 'Bob', Y)]), delta=[delta_bob],
           u=unit(3))
    # The delta of the unit itself is not a delta.
    rebase('c3_own_delta', change_set(2, create_nodes=[node('n-new', 'Bob', Y)]),
           reader={'nodes': [node('n-old', 'Bob', Y)]}, delta=[delta_bob], u=unit(2))
    # C3: a candidate that the analysis saw is not decided again.
    rebase('c3_seen', change_set(1, create_nodes=[node('n-new', 'Alice')], seen_node_candidates={'n-new': ['n-old']}),
           reader={'node_candidates': {'n-new': [node('n-old', 'Alice')]}})
    patch_delta = (2, 'committed', change_set(2, operations=[patch('n-old', ['Entity'], {'role': 'chef'})],
                                              episodes=[episode('ep2', T1)]))
    rebase('c3_seen_patched_attributes', change_set(
        3, create_nodes=[node('n-new', 'Alice')], seen_node_candidates={'n-new': ['n-old']}),
        reader={'node_candidates': {'n-new': [node('n-old', 'Alice')]}}, delta=[patch_delta], u=unit(3))
    rebase('c3_seen_patch_no_data', change_set(
        3, create_nodes=[node('n-new', 'Alice')], seen_node_candidates={'n-new': ['n-old']}),
        reader={'node_candidates': {'n-new': [node('n-old', 'Alice')]}},
        delta=[(2, 'committed', change_set(2, operations=[patch('n-old', ['Entity'])]))], u=unit(3))
    rebase('c3_seen_patch_label', change_set(
        3, create_nodes=[node('n-new', 'Alice')], seen_node_candidates={'n-new': ['n-old']},
        seen_node_labels={'n-old': ['Entity']}),
        reader={'node_candidates': {'n-new': [node('n-old', 'Alice')]}},
        delta=[(2, 'committed', change_set(2, operations=[patch('n-old', ['Entity', 'Person'])]))], u=unit(3))
    rebase('c3_seen_label_written', change_set(
        3, create_nodes=[node('n-new', 'Alice')], seen_node_candidates={'n-new': ['n-old']},
        seen_node_labels={'n-old': ['Entity', 'Person']}),
        reader={'node_candidates': {'n-new': [node('n-old', 'Alice', labels=['Entity', 'Person'])]}},
        delta=[(2, 'committed', change_set(2, operations=[patch('n-old', ['Entity', 'Person'])]))], u=unit(3))
    rebase('c3_patched_seen_not_in_search', change_set(
        3, create_nodes=[node('n-new', 'Alice')], seen_node_candidates={'n-new': ['n-old', 'n-gone']}),
        reader={'nodes': [node('n-old', 'Alice')]}, delta=[patch_delta], u=unit(3))
    # C3 with identity properties: a delta patch gives the stored node the identity key.
    rebase('c3_identity_patch', change_set(
        3, create_nodes=[node('n-new', 'A. Lee', Z, labels=['Entity', 'Person'], attributes={'email': 'a@x.org'})],
        seen_node_candidates={'n-new': ['n-old']}),
        reader={'nodes': [node('n-old', 'Alice Lee', Y, labels=['Entity', 'Person'])]},
        delta=[(2, 'apply_failed', change_set(2, operations=[patch('n-old', ['Entity', 'Person'],
                                                                   {'email': 'a@x.org'})]))],
        u=unit(3), identity={'Person': ['email']},
        replies={'dedupe_nodes.nodes': [resolutions((0, 'A. Lee', 0))]})
    # A bulk unit resolves the nodes of each episode with the context of that episode.
    ep2 = episode('ep2', T1, 'second content')
    rebase('c3_bulk_groups', change_set(
        1, create_nodes=[node('n1', 'Ann One', NEAR_X), node('n2', 'Bea Two', [0.1, 0.9, 0.0]),
                         node('n3', 'Cy Three', [0.0, 0.1, 0.9])],
        node_first_episode={'n1': 'ep2', 'n2': 'ep1', 'n3': 'ep-gone'}, episodes=[ep1, ep2],
        node_uuids=['n1', 'n2', 'n3', 'n1']),
        reader={'node_candidates': {'n1': [node('s1', 'Ann', X)], 'n2': [node('s2', 'Bea', Y)],
                                    'n3': [node('s3', 'Cy', Z)]}},
        u=unit(1, [ep1, ep2], [[episode('p1')], [episode('p2', T1, 'p2 content')]], bulk=True),
        replies={'dedupe_nodes.nodes': [resolutions((0, 'Ann One', 0)), resolutions((0, 'Bea Two', 0), (1, 'Cy Three', -1))]},
        max_coroutines=1)
    # C4: a node merge makes a self edge, which the remap drops. C4a: two created edges become one.
    rebase('c4_self_edge_and_duplicate', change_set(
        1, create_nodes=[node('n-new', 'Alice'), node('n-c', 'Carl', Z)],
        create_edges=[edge('e-self', 'n-new', 'n-old', 'Alice is herself', Z, episodes=['ep1']),
                      edge('e1', 'n-new', 'n-c', 'Alice met Carl', Y, episodes=['ep1']),
                      edge('e2', 'n-old', 'n-c', 'alice met carl', Y, episodes=['ep1', 'ep0'])],
        episodes=[episode('ep1', entity_edges=['e-self', 'e1', 'e2'])],
        episodic_edges=[episodic('ep1', 'n-new'), episodic('ep1', 'n-old'), episodic('ep1', 'n-c')]),
        reader={'node_candidates': {'n-new': [node('n-old', 'Alice')]}})
    # C5: an exact fact of an earlier unit is a duplicate with no model call.
    rebase('c5_exact', change_set(
        1, create_edges=[edge('e-new', 'a', 'b', 'Alice works at Acme', episodes=['ep1'])],
        episodes=[episode('ep1', entity_edges=['e-new'])]),
        reader={'duplicates': {'e-new': [edge('e-old', 'a', 'b', 'alice works at acme', episodes=['ep0'])]}},
        cancel=True)
    rebase('c5_model', change_set(
        1, create_edges=[edge('e-new', 'a', 'b', 'Alice is employed by Acme', episodes=['ep1'])],
        episodes=[episode('ep1', entity_edges=['e-new'])]),
        reader={'duplicates': {'e-new': [edge('e-old', 'a', 'b', 'Alice works at Acme', episodes=['ep0'])]}},
        replies={'dedupe_edges.resolve_edge': [edge_reply([0])]})
    # C6: a new fact ends a contradicted fact of an earlier unit. The unit is not bulk, so the ended edge is a link.
    for bulk in (False, True):
        rebase(f'c6_ends_old_bulk_{bulk}', change_set(
            1, create_edges=[edge('e-new', 'a', 'b', 'Alice works at Beta', valid_at=dt(T2), episodes=['ep1'])],
            episodes=[episode('ep1', entity_edges=['e-new'])]),
            reader={'invalidations': {'e-new': [edge('e-old', 'a', 'c', 'Alice works at Acme', valid_at=dt(T0))]}},
            replies={'dedupe_edges.resolve_edge': [edge_reply([], [0])]}, u=unit(1, bulk=bulk))
    # C7: a candidate that starts later ends the new fact.
    rebase('c7_later_candidate', change_set(
        1, create_edges=[edge('e-new', 'a', 'b', 'Alice works at Acme', valid_at=dt(T0), episodes=['ep1'])],
        episodes=[episode('ep1', entity_edges=['e-new'])]),
        reader={'invalidations': {'e-new': [edge('e-old', 'a', 'c', 'Alice works at Beta', valid_at=dt(T2))]}},
        replies={'dedupe_edges.resolve_edge': [edge_reply([], [0])]})
    # C7: an earlier contradiction of the delta moves the end date of the new fact.
    rebase('c7_move_end_date', change_set(
        3, create_edges=[edge('e-new', 'a', 'b', 'Alice works at Acme', valid_at=dt(T0), invalid_at=dt(T3),
                              expired_at=dt(FIXED_NOW), episodes=['ep1'])],
        episodes=[episode('ep1', entity_edges=['e-new'])], edges_ended_by_candidate=['e-new'],
        seen_invalidation_candidates={'e-new': ['e-seen']}),
        reader={'edges': [edge('e-delta', 'a', 'c', 'Alice works at Gamma', valid_at=dt(T1))]},
        delta=[(2, 'committed', change_set(2, create_edges=[edge('e-delta', 'a', 'c', 'Alice works at Gamma',
                                                                 valid_at=dt(T1))]))],
        replies={'dedupe_edges.resolve_edge': [edge_reply([], [0])]}, u=unit(3))
    # C6: a delta edge that the search does not return is a candidate when it is in the graph, and not when it is not.
    for stored in (True, False):
        rebase(f'c6_delta_edge_stored_{stored}', change_set(
            3, create_edges=[edge('e-new', 'a', 'b', 'Alice lives in Rome', NEAR_X, valid_at=dt(T2), episodes=['ep1'])],
            episodes=[episode('ep1', entity_edges=['e-new'])]),
            reader={'edges': [edge('e-delta', 'a', 'd', 'Alice lives in Oslo', X, valid_at=dt(T0))] if stored else []},
            delta=[(2, 'committed', change_set(2, create_edges=[edge('e-delta', 'a', 'd', 'Alice lives in Oslo', X,
                                                                     valid_at=dt(T0))]))],
            replies={'dedupe_edges.resolve_edge': [edge_reply([], [0])]}, u=unit(3))
    # C7: each candidate was seen, so the edge makes no model call.
    rebase('c7_no_new_candidate', change_set(
        1, create_edges=[edge('e-new', 'a', 'b', 'Alice works at Acme', episodes=['ep1'])],
        seen_duplicate_candidates={'e-new': ['e-dup']}, seen_invalidation_candidates={'e-new': ['e-inv']}),
        reader={'duplicates': {'e-new': [edge('e-dup', 'a', 'b', 'Alice is at Acme')]},
                'invalidations': {'e-new': [edge('e-inv', 'a', 'c', 'Alice left Acme'),
                                            edge('e-dup', 'a', 'b', 'Alice is at Acme')]}})
    # Edge decisions with more calls than the limit, and a failed decision.
    many = [edge(f'e{i}', 'a', f'b{i}', f'Fact number {i}', episodes=['ep1']) for i in range(4)]
    rebase('c5_concurrency', change_set(1, create_edges=many, episodes=[episode('ep1', entity_edges=[e['uuid'] for e in many])]),
           reader={'invalidations': {e['uuid']: [edge('e-old', 'a', 'z', 'Old fact', valid_at=dt(T0))] for e in many}},
           replies={'dedupe_edges.resolve_edge': [edge_reply() for _ in many]}, max_coroutines=2)
    rebase('c5_model_fails', change_set(1, create_edges=many[:2]),
           reader={'invalidations': {e['uuid']: [edge('e-old', 'a', 'z', 'Old fact')] for e in many[:2]}},
           replies={'dedupe_edges.resolve_edge': [edge_reply()]})
    # The whole path: a node merge, a remapped edge that is an exact duplicate of a stored edge, and a report.
    rebase('c3_to_c5', change_set(
        1, create_nodes=[node('n-new', 'Alice')],
        create_edges=[edge('e-new', 'n-new', 'n-b', 'Alice knows Bob', episodes=['ep1'])],
        episodes=[episode('ep1', entity_edges=['e-new'])], node_uuids=['n-new', 'n-b'],
        node_first_episode={'n-new': 'ep1', 'n-b': 'ep1'}),
        reader={'node_candidates': {'n-new': [node('n-old', 'Alice')]},
                'duplicates': {'e-new': [edge('e-old', 'n-old', 'n-b', 'alice knows bob', episodes=['ep0'])]}},
        report=True)
    return cases


def apply_cases() -> list[dict]:
    cases = []
    stored = [edge('e1', 'a', 'b', 'Alice works at Acme', episodes=['ep0']),
              edge('e2', 'a', 'c', 'Alice lives in Rome', expired_at=dt(T1), invalid_at=dt(T1)),
              edge('e3', 'a', 'd', 'Alice likes tea', attributes={'level': 1}),
              edge('e4', 'a', 'e', 'Alice owns a car', fact_embedding=None)]
    ops = [add_episodes('e1', ['ep1', 'ep0', 'ep1']), end_edge('e2', T2, FIXED_NOW, only_if_open=True),
           set_attributes('e3', {'level': 1}, {'level': 2}), add_episodes('e-gone', ['ep1']),
           add_episodes('e4', ['ep1']), end_edge('e1', None, FIXED_NOW)]
    full = change_set(
        1, create_nodes=[node('n1', 'Ann', summary='s', attributes={'age': 3})],
        create_edges=[edge('e-new', 'n1', 'a', 'Ann knows Alice', valid_at=dt(T0), episodes=['ep1'])],
        episodic_edges=[episodic('ep1', 'n1')], operations=ops,
        episodes=[episode('ep1', entity_edges=['e-new', 'e1'])])
    reader = {'edges': stored, 'episodes': [episode('ep1', entity_edges=['s1', 'e1'])]}
    rows = [[FACT_EMBEDDING, [{'fact_embedding': [0.5, 0.5, 0.0]}]]]

    def apply(name, cs, budgets=(20.0,), turns=None, **extra):
        case = {'fn': 'apply_change_set', 'name': name, 'change_set': cs, 'budgets': list(budgets), 'reader': reader,
                'rows': rows}
        if turns is not None:
            case['turns'] = turns
        case.update(extra)
        cases.append(case)

    apply('empty', change_set(1), defaults=True)
    apply('operations', full, cancel=True)
    apply('operations_turn_ok', full, turns=['ok', 'ok', 'ok'], budgets=(10.0, 9.0))
    apply('turn_no_answer', full, turns=['error', 'ok'], budgets=(10.0, 9.0))
    apply('turn_stale', full, turns=['stale'])
    apply('turn_expired', full, turns=['expired'])
    apply('budget_expired_first', full, budgets=('expired',))
    apply('budget_expired_update', full, budgets=(10.0, 'expired'))
    apply('budget_zero', full, budgets=(0.0, 0.0))
    apply('bulk_write_fails', full, fail={'execute_write': 1})
    apply('update_fails', full, fail={'tx_run': 4})
    saga_cs = dict(saga_node=saga_node(), saga_links=[link('ep1', 'ep0', 'has-1', 'next-1')], create_saga=True)
    apply('saga_new', change_set(1, **saga_cs), budgets=(5.0, 4.0, 3.0, 2.0, 1.0))
    apply('saga_new_not_created', change_set(1, **{**saga_cs, 'create_saga': False}))
    apply('saga_stored', change_set(1, saga_node=saga_node(first='ep-a', last='ep-b'),
                                    saga_links=[link('ep1', None, 'has-1'), link('ep2', 'ep1', 'has-2', 'next-2')]),
          extra_rows=[[SAGA_GET, [saga_row(first='ep-a', last='ep-b')]]])
    apply('saga_other_group', change_set(1, **saga_cs), extra_rows=[[SAGA_GET, [saga_row(group_id='h')]]])
    apply('saga_no_links', change_set(1, saga_node=saga_node(), create_saga=True))
    apply('saga_budget_expired', change_set(1, **saga_cs), budgets=(5.0, 'expired'))
    for case in cases:
        case['rows'] = case.pop('extra_rows', []) + case['rows']
    # _write_saga_links alone, with an async budget.
    cases.append({'fn': '_write_saga_links', 'change_set': change_set(1, **saga_cs), 'budgets': [1.5, 2.5, 3.5],
                  'rows': []})
    cases.append({'fn': '_write_saga_links', 'change_set': change_set(1, **saga_cs), 'budgets': [1.5, 'expired'],
                  'rows': []})
    cases.append({'fn': '_write_saga_links', 'change_set': change_set(1), 'budgets': [1.5], 'rows': []})
    # _apply_with_retries.
    for name, fail, attempts, budgets, turns in (
        ('first_attempt_fails', {'execute_write': 1}, 3, [9.0], None),
        ('each_attempt_fails', {'execute_write': 5}, 2, [9.0], None),
        ('one_attempt', {}, 1, [9.0], None),
        ('expired', {}, 3, ['expired'], None),
        ('stale', {}, 3, [9.0], ['stale']),
        ('partial_then_retry', {'tx_run': 2}, 3, [9.0], None),
    ):
        cases.append({'fn': '_apply_with_retries', 'name': name, 'change_set': full, 'fail': fail,
                      'max_attempts': attempts, 'budgets': budgets, 'turns': turns, 'reader': reader, 'rows': rows,
                      'cancel': name == 'first_attempt_fails'})
    return cases


def store_cases() -> list[dict]:
    final1 = change_set(1, create_nodes=[node('n1', 'Ann')], operations=[add_episodes('e1', ['ep1'])])
    stopped = [['admit', 'u1'], ['analyze', 1, change_set(1)], ['begin_commit', 1], ['save_final', 1, 1, final1],
               ['advance', 61.0], ['admit', 'u2'], ['begin_commit', 2]]
    reader = {'edges': [edge('e1', 'a', 'b', 'Alice works at Acme')]}
    cases = [
        {'fn': '_mark_apply_failed', 'seq': 1, 'fence': 1, 'setup': stopped[:4]},
        {'fn': '_mark_apply_failed', 'seq': 1, 'fence': 7, 'setup': stopped[:4]},
        {'fn': '_mark_apply_failed', 'seq': 1, 'fence': 1, 'setup': stopped[:4] + [['advance', 61.0]]},
        {'fn': '_recover_unit', 'name': 'recovered', 'setup': stopped, 'reader': reader, 'cancel': True},
        {'fn': '_recover_unit', 'name': 'each_apply_fails', 'setup': stopped, 'reader': reader,
         'fail': {'execute_write': 9}, 'config': {'max_apply_attempts': 2}},
        # A recovery with the fence of an earlier turn: the heartbeat before the first write is stale.
        {'fn': '_recover_unit', 'name': 'stale', 'setup': stopped, 'reader': reader, 'fence': 1},
        # The store gives no answer to the heartbeat, and the clock passes the local deadline: the turn expires.
        {'fn': '_recover_unit', 'name': 'expired_turn', 'setup': stopped, 'reader': reader,
         'advance': [['read:edges_by_uuids', 1, 100.0]], 'store_fail': {'heartbeat': 1}, 'turns': {'1': 1}},
        {'fn': '_recover_unit', 'name': 'expired_last_turn', 'setup': stopped, 'reader': reader,
         'advance': [['read:edges_by_uuids', 1, 100.0]], 'store_fail': {'heartbeat': 1}, 'turns': {'1': 4}},
        {'fn': '_recover_unit', 'name': 'expired_on_limit', 'setup': stopped, 'reader': reader,
         'advance': [['read:edges_by_uuids', 1, 55.0]], 'store_fail': {'heartbeat': 1}},
        {'fn': '_recover_unit', 'name': 'below_limit', 'setup': stopped, 'reader': reader,
         'advance': [['read:edges_by_uuids', 1, 54.5]], 'store_fail': {'heartbeat': 1}},
        {'fn': '_recover_unit', 'name': 'bad_payload', 'setup': stopped, 'reader': reader, 'bad_payload': True},
    ]
    return cases


def saga_cases() -> list[dict]:
    two = [episode('ep1', T2, 'one'), episode('ep2', T1, 'two')]
    return [
        {'fn': '_build_saga_links', 'saga': None, 'unit': unit(1)},
        {'fn': '_build_saga_links', 'saga': saga_node(), 'previous': 'ep0', 'unit': unit(1, two), 'rows': []},
        {'fn': '_build_saga_links', 'saga': saga_node(), 'previous': 'ep0', 'unit': unit(1, two),
         'rows': [[SAGA_GET, [saga_row()]]]},
        {'fn': '_build_saga_links', 'saga': saga_node(), 'previous': 'ep0', 'unit': unit(1),
         'rows': [[SAGA_GET, [saga_row(group_id='h')]]]},
        {'fn': '_build_saga_links', 'saga': saga_node(group_id='h'), 'previous': 'ep0', 'unit': unit(1)},
    ]


def hydrate_cases() -> list[dict]:
    ep2 = episode('ep2', T1, 'second content')
    ep3 = episode('ep3', T2, 'third content')
    nodes = [node('n1', 'Ann', summary='old'), node('n2', 'Bea', Y), node('n3', 'Cy', Z), node('n4', 'Dan', NEAR_X)]
    cs = change_set(1, node_first_episode={'n1': 'ep2', 'n2': 'ep1', 'n3': 'ep3', 'n4': 'ep-gone'},
                    create_edges=[edge('e1', 'n1', 'n2', 'Ann knows Bea')], episodes=[episode(), ep2, ep3])
    summaries = lambda *names: {'summaries': [{'name': n, 'summary': f'{n} summary'} for n in names]}  # noqa: E731
    every = summaries('Ann', 'Bea', 'Cy', 'Dan')
    replies = {'extract_nodes.extract_summaries_batch': [every, every, every]}
    cases = []
    for bulk in (False, True):
        cases.append({'fn': '_hydrate_nodes', 'name': f'groups_bulk_{bulk}', 'change_set': cs, 'nodes': nodes,
                      'unit': unit(1, [episode(), ep2, ep3], [[], [episode('p2')], []], bulk=bulk),
                      'replies': replies, 'max_coroutines': 2, 'cancel': not bulk})
    cases.append({'fn': '_hydrate_nodes', 'name': 'failure', 'change_set': cs, 'nodes': nodes,
                  'unit': unit(1, [episode(), ep2, ep3]), 'replies': {}})
    cases.append({'fn': '_hydrate_nodes', 'name': 'no_nodes', 'change_set': cs, 'nodes': [], 'unit': unit(1)})
    return cases


async def run_case(case: dict, mods) -> dict:
    ph = mods['phases']
    clients, reader, store, inner, config, clock = run_env(case, mods)
    results = await setup_store(store, inner, case, config, clock, mods)
    ChangeSet = mods['ChangeSet']
    fn = case['fn']
    if fn == 'UnitLease.heartbeat':
        lease = ph.UnitLease(store, config, 'g', 1, clock)
        lease.fence = case['fence']
        out = []
        for step in case['steps']:
            try:
                if step == 'heartbeat':
                    await lease.heartbeat()
                    out.append({'heartbeat': True})
                else:
                    out.append({'budget': lease.write_budget()})
            except Exception as error:  # noqa: BLE001 - the recording keeps each exception
                out.append({'error': {'type': type(error).__name__, 'text': str(error)}})
        return {'steps': out, 'store': store_view(inner)}
    if fn == '_begin_admit':
        async def wait():
            await enter('wait', {})
            clock.now += case.get('wait_advance', 0.0)

        if case.get('cancel_in_shield'):
            task = asyncio.ensure_future(ph._begin_admit(store, config, 'g', case['unit_id'], wait))
            await asyncio.sleep(0)
            task.cancel()
            try:
                await task
            except asyncio.CancelledError:
                events.append({'kind': 'cancelled'})
            return {'store': store_view(inner)}
        admission = await ph._begin_admit(store, config, 'g', case['unit_id'], wait)
        return {'admission': [admission.seq, admission.existing, admission.previous_seq], 'store': store_view(inner)}
    if fn == '_fail_unit':
        await ph._fail_unit(store, 'g', case['seq'], RuntimeError('model'))
        return {'store': store_view(inner)}
    if fn == '_fail_admission':
        Admission = mods['Admission']
        await ph._fail_admission(store, 'g', Admission(seq=case['seq'], existing=case['existing']), KeyError('x'))
        return {'store': store_view(inner)}
    if fn == '_written_episodes':
        args = [clients.driver, case['uuids'], case.get('group_id', 'g')]
        result = await ph._written_episodes(*args)
        return {'episodes': [[k, episode_view(v)] for k, v in result.items()]}
    if fn == '_episodes_with_stored_links':
        eps = [mods['EpisodicNode'](**decode(e)) for e in case['episodes']]
        result = await ph._episodes_with_stored_links(reader, eps)
        return {'episodes': [[episode_view(e), any(e is x for x in eps)] for e in result],
                'inputs': [episode_view(e) for e in eps]}
    if fn == 'rebase_change_set':
        cs = ChangeSet.model_validate(case['change_set'])
        DeltaUnit = mods['DeltaUnit']
        delta = [DeltaUnit(seq=seq, status=status, payload=ChangeSet.model_validate(d).to_bytes())
                 for seq, status, d in case['delta']]
        u = admitted(case['unit'], mods)
        if case.get('defaults'):
            final = await ph.rebase_change_set(clients, cs, delta, unit=u)
            report = None
        else:
            report = ph.new_report() if case.get('report') else None
            given = report
            final = await ph.rebase_change_set(clients, cs, delta, unit=u, reader=reader, entity_types=None,
                                               identity_properties=case['identity_properties'], report=report)
            report = given
        return {'final': final.model_dump(mode='json'), 'input': cs.model_dump(mode='json') == case['change_set'],
                'report': None if report is None else report_view(report)}
    if fn in ('apply_change_set', '_apply_with_retries', '_write_saga_links'):
        budgets = list(case['budgets'])
        turns = list(case.get('turns') or [])
        UnitExpiredError, StaleCommitError = mods['UnitExpiredError'], mods['StaleCommitError']

        def write_budget():
            value = budgets.pop(0) if len(budgets) > 1 else budgets[0]
            events.append({'kind': 'budget', 'value': value})
            if value == 'expired':
                raise UnitExpiredError('g', 1)
            return value

        async def check_turn():
            outcome = turns.pop(0) if turns else 'ok'
            await enter('check_turn', {'outcome': outcome})
            if outcome == 'stale':
                raise StaleCommitError('g', 1)
            if outcome == 'expired':
                raise UnitExpiredError('g', 1)
            if outcome == 'error':
                raise RuntimeError('no answer')

        cs = ChangeSet.model_validate(case['change_set'])
        if fn == '_write_saga_links':
            async def async_budget():
                return write_budget()

            await ph._write_saga_links(clients, cs, async_budget)
            return {'change_set': cs.model_dump(mode='json')}
        if fn == '_apply_with_retries':
            report = ph.new_report()
            changed = await ph._apply_with_retries(clients, cs, write_budget, case['max_attempts'], report, reader,
                                                   check_turn)
            return {'changed': [edge_view(e) for e in changed], 'report': report_view(report)}
        if case.get('defaults'):
            result = await ph.apply_change_set(clients, cs, write_budget=write_budget)
        else:
            result = await ph.apply_change_set(clients, cs, write_budget=write_budget, reader=reader,
                                               check_turn=check_turn if case.get('turns') is not None else None)
        return {'changed': [edge_view(e) for e in result.changed_edges], 'applied': result.applied,
                'dropped': result.dropped, 'change_set': cs.model_dump(mode='json')}
    if fn == '_mark_apply_failed':
        await ph._mark_apply_failed(store, 'g', case['seq'], case['fence'])
        return {'store': store_view(inner)}
    if fn == '_recover_unit':
        recover = results[-1]
        if case.get('bad_payload'):
            recover = mods['CommitRecover'](seq=recover.seq, fence=recover.fence, final_payload=b'not zlib')
        if case.get('fence') is not None:
            recover = mods['CommitRecover'](seq=recover.seq, fence=case['fence'], final_payload=recover.final_payload)
        turns = {int(k): v for k, v in case.get('turns', {}).items()}
        report = ph.new_report()
        await ph._recover_unit(clients, store, config, 'g', recover, clock(), clock, turns, report, reader)
        return {'recover': commit_view(recover), 'turns': [[k, v] for k, v in turns.items()],
                'report': report_view(report), 'store': store_view(inner)}
    if fn == '_build_saga_links':
        def no_graphiti(*args, **kwargs):
            raise AssertionError('graphiti is not used on this path')

        graphiti = SimpleNamespace(_find_saga=no_graphiti, _saga_get_previous_episode_uuid=no_graphiti)
        saga = None if case['saga'] is None else mods['SagaNode'](**decode(case['saga']))
        u = admitted(case['unit'], mods)
        links, saga_node_result, create = await ph._build_saga_links(graphiti, clients, u, saga, case.get('previous'))
        return {'links': [link_view.model_dump(mode='json') for link_view in links],
                'saga': None if saga_node_result is None else saga_node_result.model_dump(mode='json'),
                'same': saga_node_result is saga, 'create': create}
    if fn == '_hydrate_nodes':
        cs = ChangeSet.model_validate(case['change_set'])
        nodes = [mods['EntityNode'](**decode(n)) for n in case['nodes']]
        u = admitted(case['unit'], mods)
        failed = await ph._hydrate_nodes(clients, u, cs, nodes, None)
        return {'failed': failed, 'nodes': [node_view(n) for n in nodes]}
    raise AssertionError(fn)


def run_one(case: dict, mods, cancel_at: int) -> dict:
    events.clear()
    state.update(in_flight=0, max_in_flight=0, count=0, attempts=0, cancel_at=cancel_at, case=copy.deepcopy(case))

    async def main():
        task = asyncio.ensure_future(run_case(state['case'], mods))
        state['task'] = task
        return await task

    try:
        output = {'ok': asyncio.run(main())}
    except BaseException as error:  # noqa: BLE001 - the recording keeps each exception, also a cancellation
        if isinstance(error, (KeyboardInterrupt, SystemExit)):
            raise
        text = '' if isinstance(error, asyncio.CancelledError) else str(error)
        if type(error).__name__ == 'ValidationError':
            # ValidationException of the port does not carry the input value, the input type, and the link (R25).
            text = '\n'.join(line for line in VALIDATION_INPUT.sub(']', text).splitlines()
                              if 'For further information visit' not in line)
        output = {'error': {'type': type(error).__name__, 'text': text}}
    for item in events:
        if item.get('kind') == 'log':
            item['message'] = TYPE_NAME.sub('failed: <type>', item['message'])
    output['events'] = list(events)
    output['max_in_flight'] = state['max_in_flight']
    output['count'] = state['count']
    output['attempts'] = state['attempts']
    return output


def record_async(mods) -> list[dict]:
    out = []
    for case in lease_cases() + admit_cases() + rebase_cases() + apply_cases() + store_cases() + saga_cases() \
            + hydrate_cases():
        output = run_one(case, mods, -1)
        if case.get('cancel'):
            cancels = []
            for k in range(output['count'] - 1):
                cancelled = run_one(case, mods, k)
                counted = [e for e in cancelled['events'] if e['kind'] not in ('store', 'log', 'session', 'close',
                                                                                'rollback', 'budget', 'cancelled')]
                cancels.append({'k': k, 'attempts': cancelled['attempts'], 'count': cancelled['count'],
                                'error': cancelled.get('error', {}).get('type'),
                                'after': placeholders(counted[k + 1:], {})})
            output['cancel'] = cancels
        out.append({'in': case, 'out': placeholders(output, {})})
    return out


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    os.environ.pop('GRAPHITI_ATTRIBUTE_MAX_LENGTH', None)
    warnings.filterwarnings('ignore', message=r"coroutine '.*' was never awaited", category=RuntimeWarning)

    import graphiti_core
    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.driver.graph_operations.graph_operations import GraphOperationsInterface
    from graphiti_core.edges import EntityEdge, EpisodicEdge
    from graphiti_core.errors import StaleCommitError, UnitExpiredError
    from graphiti_core.graphiti_types import GraphitiClients
    from graphiti_core.ingestion import phases
    from graphiti_core.ingestion.change_set import ChangeSet
    from graphiti_core.ingestion.config import ConcurrentIngestionConfig
    from graphiti_core.ingestion.graph_reader import GraphReader
    from graphiti_core.ingestion.state_store import (
        Admission,
        CommitRecover,
        DeltaUnit,
        IngestionStateStore,
        InMemoryIngestionStateStore,
    )
    from graphiti_core.nodes import EntityNode, EpisodicNode, SagaNode

    class Capture(logging.Handler):
        def emit(self, record):
            events.append({'kind': 'log', 'level': record.levelname, 'message': record.getMessage()})

    root = logging.getLogger('graphiti_core')
    root.setLevel(logging.DEBUG)
    root.propagate = False
    root.addHandler(Capture(logging.DEBUG))

    for name, module in list(sys.modules.items()):
        if name.startswith('graphiti_core') and hasattr(module, 'utc_now'):
            module.utc_now = lambda: FIXED_NOW
    del graphiti_core

    mods = dict(phases=phases, ChangeSet=ChangeSet, EntityNode=EntityNode, EntityEdge=EntityEdge,
                EpisodicNode=EpisodicNode, EpisodicEdge=EpisodicEdge, SagaNode=SagaNode, GraphProvider=GraphProvider,
                GraphOperationsInterface=GraphOperationsInterface, GraphitiClients=GraphitiClients,
                GraphReader=GraphReader, InMemoryIngestionStateStore=InMemoryIngestionStateStore,
                IngestionStateStore=IngestionStateStore, ConcurrentIngestionConfig=ConcurrentIngestionConfig,
                DeltaUnit=DeltaUnit, Admission=Admission, CommitRecover=CommitRecover,
                UnitExpiredError=UnitExpiredError, StaleCommitError=StaleCommitError)
    CS_MODULES.update(mods)

    sync = []
    for case in sync_cases():
        events.clear()
        try:
            out = {'ok': run_sync(case, mods)}
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            out = {'error': {'type': type(error).__name__, 'text': str(error)}}
        out['events'] = list(events)
        sync.append({'in': case, 'out': out})
    async_cases = record_async(mods)

    constants = {'REPORT_KEYS': list(phases.REPORT_KEYS)}
    dump = lambda r: json.dumps(r, ensure_ascii=True, allow_nan=False, separators=(',', ':'))  # noqa: E731
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","fixed_now":"' + FIXED_NOW.isoformat()
        + '","constants":' + dump(constants) + ',"cases":[\n' + ',\n'.join(dump(r) for r in sync)
        + '\n],"async_cases":[\n' + ',\n'.join(dump(r) for r in async_cases) + '\n]}\n'
    )
    size = len(text.encode('utf-8'))
    if size > 1_572_864:
        raise SystemExit(f'The values file would have {size} bytes, above the limit of 1,572,864 (R25).')
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(sync)} cases and {len(async_cases)} async cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(sync)} cases and {len(async_cases)} async cases, {size} bytes')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
