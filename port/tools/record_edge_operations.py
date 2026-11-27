"""Records results of graphiti_core/utils/maintenance/edge_operations.py from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_edge_operations.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_edge_operations.py --check

Plan 0004, layer 7, packet P7c. The tool has two parts.

The sync part calls each sync function of the module with generated inputs from a fixed seed: build_episodic_edges
(both shapes of episode_uuid), build_community_edges, _pair_search_filter, _same_pair_edges, find_exact_duplicate_edge,
_edge_types_for_endpoints, enforce_edge_type_signatures, _apply_temporal_invalidation, and
resolve_edge_contradictions. It records the result, each argument after the call (the functions change their
arguments), and each log line of graphiti_core.

The async part runs extract_edges, resolve_extracted_edges, resolve_extracted_edge, decide_edge_against_candidates,
_dedupe_extracted_edge, _extract_edge_timestamps, and filter_existing_duplicate_of_edges with fakes for the LLM
client, the embedder, the cross-encoder, and the graph driver:

- The LLM client replaces generate_response. It records the prompt name, the response model name, the model size,
  max_tokens, group_id, attribute_extraction, and the role, the SHA-256, and the length of each message. It answers by
  a fixed rule: the first reply of the case for the prompt name whose needle is in the last message.
- The cases of review_cases() come after the others and hold the inputs that the reviews of the packet asked for: a record
  value that is a list or a dict, the reference time of an edge, an exact match with another endpoint, an episode whose
  valid_at is not its created_at, the limit of the last gather, and two episodes with one instant.
- The embedder gives the vector [i, 1.0] to the i-th distinct text of the case.
- The driver has a search interface and a graph operations interface. The search interface answers each edge search
  from the tables of the case by the fact (or by the fact that has the vector) and by the kind of filter: "pair" for
  the endpoint filter of _pair_search_filter, and "all" for SearchFilters(). The graph operations interface answers
  node_get_by_uuids from the stored nodes of the case. execute_query answers filter_existing_duplicate_of_edges.
- Each fake yields once (asyncio.sleep(0)) and then records its call. The calls and the log lines are one list. Each
  case keeps the largest count of fake calls in flight.

For the cases of CANCEL_CASES the tool also runs the case again with a cancel (the form of record_node_operations.py): once
for k = -1, where the task is canceled before its first await, and once for each call k except the last, where the fake
that makes call k records it and then cancels the task of the call (the port cancels a token source). The case keeps the
count of the attempts (each fake counts an attempt before its yield), the count of the recorded calls, and the events
after call k. Python lets a CancelledError through each `except Exception`, so the port must make the same attempts and
write the same log lines (R25). A point where the port makes one more attempt is the late cancel of GAPS F33, and the
test names it.

Fixed values on both sides: edge_operations.utc_now and dangling_endpoints.utc_now give FIXED_NOW, and the C# test
gives the port a clock with the same time. A uuid4 that the code makes is random: the test replaces each one, in the
order of its first appearance in the case output, by "<new:N>". A timed log line has its milliseconds replaced by
"<ms>". edge_operations iterates the set referenced_node_uuids, so the tool binds the module name `set` to a set that
iterates in insertion order, the order of the port (RULES.md R14).

It writes tests/Port.Evaluation/values/edge_operations.json. --check writes nothing and exits 1 if the file differs
from what the run would write. The tool needs Python 3.12 and the pinned upstream in the folder of the variable
GRAPHITI_UPSTREAM, or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import hashlib
import json
import logging
import os
import random
import re
import sys
import warnings
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'edge_operations.json'

SEED = 20261128
FIXED_NOW = datetime(2026, 3, 4, 5, 6, 7, 891011, tzinfo=timezone.utc)
UUID4 = re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}')
MS = re.compile(r'in [0-9.e+-]+ ms')

rng = random.Random(SEED)
events: list[dict] = []
state: dict = {}

# A cancel leaves the coroutines of a gather that never started. Python warns about each one, and that is no result.
warnings.filterwarnings('ignore', message='coroutine .* was never awaited', category=RuntimeWarning)

# The cases that the tool runs again with a cancel: one case of each async function. The C# test names the same cases.
CANCEL_CASES = [0, 13, 14, 15, 16, 17, 21, 26, 29, 31, 36]


class InsertionOrderSet(set):
    """A set that iterates in insertion order, the order of the port (RULES.md R14)."""

    def __init__(self, items=()):
        super().__init__()
        self._order = []
        self.update(items)

    def add(self, item):
        if item not in self:
            super().add(item)
            self._order.append(item)

    def update(self, *iterables):
        for iterable in iterables:
            for item in iterable:
                self.add(item)

    def __iter__(self):
        return iter(self._order)


def dt(value):
    return None if value is None else {'$dt': value.isoformat()}


def encode(value):
    if isinstance(value, datetime):
        return dt(value)
    if isinstance(value, dict):
        return {str(k): encode(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [encode(v) for v in value]
    return value


def edge_view(edge) -> dict:
    return {
        'uuid': edge.uuid, 'group_id': edge.group_id, 'source_node_uuid': edge.source_node_uuid,
        'target_node_uuid': edge.target_node_uuid, 'created_at': dt(edge.created_at), 'name': edge.name,
        'fact': edge.fact, 'fact_embedding': edge.fact_embedding, 'episodes': list(edge.episodes),
        'expired_at': dt(edge.expired_at), 'valid_at': dt(edge.valid_at), 'invalid_at': dt(edge.invalid_at),
        'reference_time': dt(edge.reference_time), 'attributes': encode(edge.attributes),
    }


def node_view(node) -> dict:
    return {
        'uuid': node.uuid, 'name': node.name, 'group_id': node.group_id, 'labels': list(node.labels),
        'created_at': dt(node.created_at), 'summary': node.summary, 'attributes': encode(node.attributes),
    }


def simple_edge_view(edge) -> dict:
    return {
        'type': type(edge).__name__, 'uuid': edge.uuid, 'group_id': edge.group_id,
        'source_node_uuid': edge.source_node_uuid, 'target_node_uuid': edge.target_node_uuid,
        'created_at': dt(edge.created_at),
    }


def placeholders(value, known: dict):
    """Replaces each uuid4 that the case input does not hold by <new:N>, in the order of first appearance."""
    if isinstance(value, dict):
        return {k: placeholders(v, known) for k, v in value.items()}
    if isinstance(value, list):
        return [placeholders(v, known) for v in value]
    if isinstance(value, str):
        def repl(m):
            if m.group(0) not in known:
                known[m.group(0)] = f'<new:{len(known)}>'
            return known[m.group(0)]

        return MS.sub('in <ms> ms', UUID4.sub(repl, value))
    return value


# ---------------------------------------------------------------------------------------------------------------------
# Generated data

DATES = [
    None,
    datetime(2024, 1, 1, tzinfo=timezone.utc),
    datetime(2024, 6, 1, 12, 0, tzinfo=timezone.utc),
    datetime(2025, 1, 1, tzinfo=timezone.utc),
    datetime(2026, 1, 1, tzinfo=timezone.utc),
    datetime(2027, 5, 5, tzinfo=timezone.utc),
    datetime(2024, 6, 1, 12, 0),  # naive: ensure_utc reads it as UTC
    datetime(2025, 1, 1, 2, 0, tzinfo=timezone(timedelta(hours=2))),
]
NODE_UUIDS = ['a', 'b', 'c', 'd']
FACTS = ['Ana works at Acme', '  ANA  works at   acme ', 'Ana lives in Paris', 'CAFÉ 東京', 'café 東京', 'x', 'Bob knows Ana']
NAMES = ['WORKS_AT', 'LIVES_IN', 'KNOWS', 'RELATES_TO', 'MENTIONS', 'FREE']


def gen_edge_data(uuid: str) -> dict:
    data = {
        'uuid': uuid, 'group_id': rng.choice(['g', 'g', 'h']),
        'source_node_uuid': rng.choice(NODE_UUIDS), 'target_node_uuid': rng.choice(NODE_UUIDS),
        'name': rng.choice(NAMES), 'fact': rng.choice(FACTS), 'created_at': dt(datetime(2024, 1, 1, tzinfo=timezone.utc)),
    }
    for key in ('valid_at', 'invalid_at', 'expired_at'):
        value = rng.choice(DATES)
        if value is not None and (key != 'expired_at' or rng.random() < 0.3):
            data[key] = dt(value)
    if rng.random() < 0.3:
        data['attributes'] = {'k': rng.randint(0, 3)}
    if rng.random() < 0.3:
        data['episodes'] = rng.sample(['ep1', 'ep2', 'ep3'], rng.randint(1, 2))
    return data


def decode(value):
    if isinstance(value, dict):
        if set(value) == {'$dt'}:
            return datetime.fromisoformat(value['$dt'])
        return {k: decode(v) for k, v in value.items()}
    if isinstance(value, list):
        return [decode(v) for v in value]
    return value


LABELS = ['Person', 'Company', 'Place']


def gen_node_data(uuid: str) -> dict:
    labels = ['Entity'] + rng.sample(LABELS, rng.randint(0, 2))
    if rng.random() < 0.2:
        labels = labels[1:]
    return {'uuid': uuid, 'name': 'N' + uuid, 'group_id': 'g', 'labels': labels,
            'created_at': dt(datetime(2024, 1, 1, tzinfo=timezone.utc))}


# The custom edge types. Each one has a docstring or not, and fields with or without a max_length.
TYPE_DEFS = {
    'WORKS_AT': {'doc': 'Employment relation.', 'fields': [['role', False, 12], ['since', False, None]]},
    'LIVES_IN': {'doc': None, 'fields': [['city', False, None]]},
    'MENTIONS': {'doc': 'A mention with no field.', 'fields': []},
    'KNOWS': {'doc': 'Two people know each other.', 'fields': [['how', True, None]]},
}
TYPE_MAPS = [
    [],
    [[['Person', 'Company'], ['WORKS_AT']], [['Person', 'Place'], ['LIVES_IN']]],
    [[['Person', 'Company'], ['WORKS_AT', 'MENTIONS']], [['Entity', 'Entity'], ['KNOWS']],
     [['Company', 'Person'], ['WORKS_AT']]],
    [[['Entity', 'Entity'], ['MENTIONS', 'UNKNOWN_TYPE']], [['Person', 'Person'], ['KNOWS', 'LIVES_IN']]],
]


def record(name: str, fn, inputs: dict, outputs) -> None:
    events.clear()
    try:
        result = fn()
        out = {'ok': outputs(result)}
    except Exception as error:  # noqa: BLE001 - the recording keeps each exception
        out = {'error': {'type': type(error).__name__, 'text': str(error)}}
    out['log'] = list(events)
    out['after'] = inputs.get('_after', lambda: None)()
    inputs = {k: v for k, v in inputs.items() if k != '_after'}
    records.append({'f': name, 'in': inputs, 'out': placeholders(out, {})})


records: list[dict] = []


def record_sync(eo, EntityEdge, EntityNode, CommunityNode, models) -> None:
    # build_episodic_edges, with both shapes of episode_uuid
    for i in range(60):
        nodes = [gen_node_data(u) for u in rng.sample(NODE_UUIDS, rng.randint(0, 3))]
        if rng.random() < 0.3:
            episode_uuid = rng.choice(['ep1', 'ep2'])
        else:
            episode_uuid = rng.sample(['ep1', 'ep2', 'ep3'], rng.randint(0, 3))
        index_map = None
        r = rng.random()
        if r < 0.5:
            index_map = {n['uuid']: [rng.randint(-2, 4) for _ in range(rng.randint(0, 3))] for n in nodes if rng.random() < 0.7}
        elif r < 0.6:
            index_map = {}
        created = rng.choice(DATES[1:])
        inputs = {'nodes': nodes, 'episode_uuid': episode_uuid, 'created_at': dt(created), 'map': index_map}
        record('build_episodic_edges', lambda: eo.build_episodic_edges(
            [EntityNode(**decode(n)) for n in nodes], episode_uuid, created, index_map),
            inputs, lambda r: [simple_edge_view(e) for e in r])

    # build_community_edges
    for i in range(6):
        nodes = [gen_node_data(u) for u in rng.sample(NODE_UUIDS, rng.randint(0, 3))]
        community = {'uuid': 'c1', 'name': 'Comm', 'group_id': rng.choice(['gc', 'g']),
                     'created_at': dt(datetime(2024, 1, 1, tzinfo=timezone.utc))}
        inputs = {'nodes': nodes, 'community': community, 'created_at': dt(DATES[3])}
        record('build_community_edges', lambda: eo.build_community_edges(
            [EntityNode(**decode(n)) for n in nodes], CommunityNode(**decode(community)), DATES[3]),
            inputs, lambda r: [simple_edge_view(e) for e in r])

    # _pair_search_filter and _same_pair_edges
    for i in range(20):
        edge = gen_edge_data('x')
        candidates = [gen_edge_data(f'c{j}') for j in range(rng.randint(0, 6))]
        inputs = {'edge': edge, 'candidates': candidates}
        record('_pair_search_filter', lambda: eo._pair_search_filter(EntityEdge(**decode(edge))), inputs,
               lambda r: {'edge_source_node_uuids': r.edge_source_node_uuids,
                          'edge_target_node_uuids': r.edge_target_node_uuids,
                          'dump': encode(r.model_dump(mode='json'))})
        record('_same_pair_edges', lambda: eo._same_pair_edges(
            EntityEdge(**decode(edge)), [EntityEdge(**decode(c)) for c in candidates]), inputs,
            lambda r: [e.uuid for e in r])

    # find_exact_duplicate_edge: a sequence of edges through one `seen` dict
    for i in range(40):
        edges = [gen_edge_data(f'e{j}') for j in range(rng.randint(1, 7))]
        inputs = {'edges': edges}

        def run(edges=edges):
            seen = {}
            out = []
            for data in edges:
                found = eo.find_exact_duplicate_edge(EntityEdge(**decode(data)), seen)
                out.append(None if found is None else found.uuid)
            return out, len(seen)

        record('find_exact_duplicate_edge', run, inputs, lambda r: {'found': r[0], 'seen': r[1]})

    # _edge_types_for_endpoints and enforce_edge_type_signatures
    for i in range(80):
        type_map = rng.choice(TYPE_MAPS)
        type_names = rng.sample(sorted(TYPE_DEFS), rng.randint(0, 4))
        node_map = {u: gen_node_data(u) for u in rng.sample(NODE_UUIDS, rng.randint(0, 4))}
        for n in node_map.values():
            n['labels'] = n['labels'] + rng.sample(['Person', 'Company', 'Place'], rng.randint(0, 2))
        edges = [gen_edge_data(f'e{j}') for j in range(rng.randint(0, 4))]
        for e in edges:
            e['name'] = rng.choice(list(TYPE_DEFS) + ['UNKNOWN_TYPE', 'RELATES_TO', 'OTHER'])
        strict = rng.random() < 0.5
        inputs = {'edges': edges, 'type_map': type_map, 'type_names': type_names, 'nodes': node_map, 'strict': strict}

        def run(edges=edges, type_map=type_map, type_names=type_names, node_map=node_map, strict=strict):
            edge_type_map = {tuple(k): list(v) for k, v in type_map}
            edge_types = {n: models[n] for n in type_names}
            uuid_entity_map = {u: EntityNode(**decode(n)) for u, n in node_map.items()}
            objs = [EntityEdge(**decode(e)) for e in edges]
            lst = [eo._edge_types_for_endpoints(e, uuid_entity_map, edge_types, edge_type_map) for e in objs]
            matched = [list(m) for m in lst]
            kept, kept_types = eo.enforce_edge_type_signatures(objs, lst, edge_type_map, strict)
            return matched, [edge_view(e) for e in kept], [list(m) for m in kept_types]

        record('edge_types', run, inputs, lambda r: {'matched': r[0], 'kept': r[1], 'kept_types': r[2]})

    # enforce_edge_type_signatures with lists of two lengths
    edges = [gen_edge_data('e0')]
    record('enforce_length_mismatch', lambda: eo.enforce_edge_type_signatures(
        [EntityEdge(**decode(edges[0]))], [], {('Person', 'Company'): ['WORKS_AT']}, True),
        {'edges': edges}, lambda r: None)

    # _apply_temporal_invalidation and resolve_edge_contradictions
    for name in ('_apply_temporal_invalidation', 'resolve_edge_contradictions'):
        for i in range(150):
            resolved = gen_edge_data('r')
            candidates = [gen_edge_data(f'c{j}') for j in range(rng.randint(0, 5))]
            inputs = {'resolved': resolved, 'candidates': candidates}
            holder = {}

            def run(resolved=resolved, candidates=candidates, holder=holder, name=name):
                r = EntityEdge(**decode(resolved))
                cs = [EntityEdge(**decode(c)) for c in candidates]
                holder['r'], holder['cs'] = r, cs
                return getattr(eo, name)(r, cs)

            inputs['_after'] = lambda holder=holder: {
                'resolved': edge_view(holder['r']), 'candidates': [edge_view(c) for c in holder['cs']]}
            record(name, run, inputs, lambda r: [e.uuid for e in r])


# ---------------------------------------------------------------------------------------------------------------------
# Async scenarios


def ep(uuid: str, valid_at: datetime | None = None, group_id: str = 'g', content: str = 'Ana works at Acme.',
       created_at: datetime | None = None) -> dict:
    return {'uuid': uuid, 'name': 'episode ' + uuid, 'group_id': group_id, 'labels': [], 'source': 'message',
            'source_description': 'chat', 'content': content,
            'valid_at': dt(valid_at or datetime(2025, 1, 1, tzinfo=timezone.utc)),
            'created_at': dt(created_at or datetime(2025, 1, 1, tzinfo=timezone.utc))}


def node(uuid: str, name: str, labels: list[str] | None = None, group_id: str = 'g') -> dict:
    return {'uuid': uuid, 'name': name, 'group_id': group_id, 'labels': labels or ['Entity'],
            'created_at': dt(datetime(2024, 1, 1, tzinfo=timezone.utc)), 'summary': ''}


def edge(uuid: str, source: str, target: str, fact: str, name: str = 'RELATES_TO', group_id: str = 'g', **extra) -> dict:
    data = {'uuid': uuid, 'group_id': group_id, 'source_node_uuid': source, 'target_node_uuid': target,
            'name': name, 'fact': fact, 'created_at': dt(datetime(2024, 1, 1, tzinfo=timezone.utc))}
    for key, value in extra.items():
        data[key] = dt(value) if isinstance(value, datetime) else value
    return data


def x_edge(source, target, relation, fact, valid_at=None, invalid_at=None, indices=None) -> dict:
    data = {'source_entity_name': source, 'target_entity_name': target, 'relation_type': relation, 'fact': fact}
    if valid_at is not None:
        data['valid_at'] = valid_at
    if invalid_at is not None:
        data['invalid_at'] = invalid_at
    if indices is not None:
        data['episode_indices'] = indices
    return data


def reply(prompt: str, needle: str, value) -> dict:
    return {'prompt': prompt, 'needle': needle, 'reply': value}


def resolve_reply(fact: str, duplicates, contradicted) -> dict:
    return reply('dedupe_edges.resolve_edge', f'<NEW FACT>\n{fact}\n</NEW FACT>',
                 {'duplicate_facts': duplicates, 'contradicted_facts': contradicted})


def ts_reply(fact: str, valid_at=None, invalid_at=None) -> dict:
    return reply('extract_edges.extract_timestamps', fact, {'valid_at': valid_at, 'invalid_at': invalid_at})


PAST = datetime(2023, 3, 1, tzinfo=timezone.utc)
REF = datetime(2025, 1, 1, tzinfo=timezone.utc)
FUTURE = datetime(2027, 1, 1, tzinfo=timezone.utc)

NODES = [node('n-ana', 'Ana', ['Entity', 'Person']), node('n-acme', 'Acme', ['Entity', 'Company']),
         node('n-paris', 'Paris', ['Entity', 'Place']), node('n-bob', 'Bob', ['Entity', 'Person'])]
TYPES_ALL = ['WORKS_AT', 'LIVES_IN', 'MENTIONS', 'KNOWS']
MAP_A = [[['Person', 'Company'], ['WORKS_AT', 'MENTIONS']], [['Person', 'Place'], ['LIVES_IN']]]


def extract_cases() -> list[dict]:
    base = {'fn': 'extract_edges', 'episode': ep('ep1'), 'nodes': NODES, 'previous': [ep('ep0', PAST, content='Earlier.')],
            'type_map': MAP_A, 'group_id': '', 'types': None, 'strict': False, 'instructions': None}
    cases = []

    def add(**kw):
        case = dict(base)
        case.update(kw)
        cases.append(case)

    reply_edges = [
        x_edge('Ana', 'Acme', 'WORKS_AT', 'Ana works at Acme', valid_at='2024-01-01T00:00:00Z'),
        x_edge(' ana ', 'Paris', 'LIVES_IN', 'Ana lives in Paris', invalid_at='not a date'),
        x_edge('Ana', 'Zed Corp', 'WORKS_AT', 'Ana will work at Zed Corp', valid_at='2027-02-01T00:00:00+00:00'),
        x_edge('', 'Acme', 'X', 'empty source'),
        x_edge('Ana', 'Acme', 'X', '   '),
        x_edge('Bob', 'Bob', 'KNOWS', 'Bob knows himself', valid_at='bad', invalid_at='2026-01-01T00:00:00'),
    ]
    add(replies=[reply('extract_edges.edge', '', {'edges': reply_edges})])
    add(replies=[reply('extract_edges.edge', '', {'edges': reply_edges})], types=TYPES_ALL, group_id='gx',
        instructions='Prefer short facts.')
    add(replies=[reply('extract_edges.edge', '', {'edges': reply_edges})], types=['WORKS_AT', 'MENTIONS'], strict=True)
    add(replies=[], types=None, strict=True)
    add(replies=[], types=[], strict=True)
    add(replies=[reply('extract_edges.edge', '', {'edges': []})])
    add(replies=[reply('extract_edges.edge', '', {'edges': [x_edge('Ghost', 'Acme', 'X', '  ')]})])
    add(replies=[reply('extract_edges.edge', '', {'edges': [x_edge('Ghost', 'Spirit', 'HAUNTS', 'Ghost haunts Spirit')]})],
        types=['WORKS_AT'], strict=True)
    add(replies=[reply('extract_edges.edge', '', {'edges': [x_edge('Ghost', 'Spirit', 'WORKS_AT', 'Ghost works at Spirit')]})],
        types=['WORKS_AT'], strict=True, nodes=[])
    add(replies=[reply('extract_edges.edge', '', {'edges': [{'source_entity_name': 'Ana'}]})])
    add(replies=[reply('extract_edges.edge', '', {'edges': [x_edge('Ana', 'Acme', 'WORKS_AT', 'f', indices=[5])]})])
    # Two episodes: attribution text, the indices, and the reference time of each fact
    two = [ep('ep1', REF), ep('ep2', datetime(2025, 6, 1, tzinfo=timezone.utc), content='Ana moved to Paris.'),
           ep('ep3', datetime(2024, 6, 1, tzinfo=timezone.utc), content='Bob knows Ana.')]
    add(episode=two, replies=[reply('extract_edges.edge', '', {'edges': [
        x_edge('Ana', 'Paris', 'LIVES_IN', 'Ana is going to live in Paris', indices=[1]),
        x_edge('Bob', 'Ana', 'KNOWS', 'Bob knows Ana', indices=[2, 0]),
        x_edge('Ana', 'Acme', 'WORKS_AT', 'Ana works at Acme', indices=[]),
        x_edge('Ana', 'Acme', 'WORKS_AT', 'Ana plans to stay at Acme', indices=[7, -1]),
        x_edge('Ana', 'Acme', 'WORKS_AT', 'Ana will lead Acme', valid_at='2026-06-01T00:00:00Z', indices=[-1, 0]),
    ]})])
    add(episode=[ep('ep9', REF, group_id='h')], previous=[], replies=[reply('extract_edges.edge', '', {'edges': [
        x_edge('Ana', 'New Co', 'WORKS_AT', 'Ana works at New Co'), x_edge('new co', 'Ana', 'EMPLOYS', 'New Co employs Ana')]})])
    return cases


def resolve_cases() -> list[dict]:
    cases = []
    stored = {
        'n-ana': node('n-ana', 'Ana', ['Entity', 'Person']), 'n-acme': node('n-acme', 'Acme', ['Entity', 'Company']),
        'n-paris': node('n-paris', 'Paris', ['Entity', 'Place']), 'n-zed': node('n-zed', 'Zed', ['Entity', 'Company'], 'h'),
    }
    old_work = edge('s-work', 'n-ana', 'n-acme', 'Ana works at Acme', 'WORKS_AT', valid_at=PAST, episodes=['ep0'])
    old_live = edge('s-live', 'n-ana', 'n-paris', 'Ana lives in Lyon', 'LIVES_IN', valid_at=PAST)
    newer = edge('s-newer', 'n-ana', 'n-paris', 'Ana lives in Nice', 'LIVES_IN', valid_at=FUTURE)
    ended = edge('s-ended', 'n-ana', 'n-paris', 'Ana lived in Rome', 'LIVES_IN', valid_at=PAST,
                 invalid_at=datetime(2023, 6, 1, tzinfo=timezone.utc))
    loop = edge('s-loop', 'n-ana', 'n-ana', 'Ana is Ana', valid_at=PAST)
    other = edge('s-other', 'n-bob', 'n-acme', 'Bob works at Acme', 'WORKS_AT', valid_at=PAST)

    def search(fact, pair_bm25=(), pair_cos=(), all_bm25=(), all_cos=()):
        return {fact: {'pair': {'bm25': list(pair_bm25), 'cos': list(pair_cos)},
                       'all': {'bm25': list(all_bm25), 'cos': list(all_cos)}}}

    base = {'fn': 'resolve_extracted_edges', 'episode': ep('ep1', REF), 'entities': list(stored.values())[:2],
            'stored_nodes': list(stored.values()), 'types': TYPES_ALL, 'type_map': MAP_A, 'override': None,
            'strict': False, 'invalidated_by': True, 'max_coroutines': None, 'routing': False}

    def add(**kw):
        case = dict(base)
        case.update(kw)
        cases.append(case)

    e_work = edge('x-work', 'n-ana', 'n-acme', 'Ana works at Acme', 'WORKS_AT', episodes=['ep1'])
    e_work2 = edge('x-work2', 'n-acme', 'n-ana', ' ana works at ACME', 'WORKS_AT', episodes=['ep1'])
    e_live = edge('x-live', 'n-ana', 'n-paris', 'Ana lives in Paris', 'LIVES_IN', episodes=['ep1'], valid_at=REF)
    e_mention = edge('x-mention', 'n-ana', 'n-paris', 'Ana mentions Paris', 'MENTIONS', episodes=['ep1'],
                     attributes={'stale': 1})
    e_free = edge('x-free', 'n-ana', 'n-acme', 'Ana joined Acme', 'WORKS_AT', episodes=['ep1'], attributes={'role': 'x'})
    e_zed = edge('x-zed', 'n-zed', 'n-acme', 'Zed partners with Acme', 'KNOWS', group_id='h')

    # A duplicate by exact fact, an in-batch duplicate in reverse direction, a contradiction, a signature mismatch
    add(edges=[e_work, e_work2, e_live, e_mention],
        search={**search('Ana works at Acme', pair_bm25=[old_work, loop, other], pair_cos=[old_work]),
                **search('Ana lives in Paris', pair_bm25=[old_live, ended], pair_cos=[newer], all_bm25=[old_live, other],
                         all_cos=[other]),
                **search('Ana mentions Paris', all_bm25=[old_work])},
        replies=[resolve_reply('Ana lives in Paris', [0, 9, 0], [1, 2, 3, 3, 9, -1]),
                 resolve_reply('Ana mentions Paris', [], [0]),
                 ts_reply('Ana mentions Paris', '2024-01-01T00:00:00Z')])
    # Strict: the mismatched edge is dropped; the missing node is fetched for the edge group only
    add(edges=[e_mention, e_zed, e_free], strict=True, entities=[],
        search={**search('Ana joined Acme', pair_bm25=[old_work], all_cos=[other])},
        replies=[resolve_reply('Ana joined Acme', [0], []),
                 reply('extract_edges.extract_attributes', 'Ana joined Acme', {'role': 'an engineer for life', 'since': None}),
                 ts_reply('Zed partners with Acme', None, None),
                 reply('extract_edges.extract_attributes', 'Zed partners with Acme', {'how': 'business'})])
    # No candidates: attributes and timestamps; a failed timestamp call is logged
    add(edges=[e_free, e_zed, e_live], types=TYPES_ALL, type_map=MAP_A + [[['Entity', 'Entity'], ['KNOWS']]],
        replies=[reply('extract_edges.extract_attributes', 'Ana joined Acme', {'role': 'lead', 'since': '2020'}),
                 reply('extract_edges.extract_timestamps', 'Ana joined Acme', {'$raise': 'fake failure'}),
                 reply('extract_edges.extract_attributes', 'Zed partners with Acme', {'how': None}),
                 ts_reply('Zed partners with Acme', '2030-01-01T00:00:00Z', 'garbage')])
    # The override list adds candidates of the same pair and group; prompt routing passes the clients
    add(edges=[e_work, e_live], override=[old_work, old_live, other, edge('s-g2', 'n-ana', 'n-acme', 'Ana at Acme', group_id='h')],
        routing=True, invalidated_by=False,
        search={**search('Ana works at Acme', all_bm25=[old_work, old_live])},
        replies=[resolve_reply('Ana works at Acme', [], [1]), resolve_reply('Ana lives in Paris', [0], [0]),
                 ts_reply('Ana works at Acme', '2026-01-01T00:00:00Z')])
    # More edges than the concurrency limit
    many = [edge(f'x-m{i}', 'n-ana', 'n-acme', f'Ana fact {i}', 'MENTIONS', episodes=['ep1']) for i in range(4)]
    add(edges=many, max_coroutines=2,
        search={**search('Ana fact 1', pair_bm25=[old_work]), **search('Ana fact 2', all_cos=[old_work])},
        replies=[resolve_reply('Ana fact 1', [0], []), resolve_reply('Ana fact 2', [], [0])]
        + [ts_reply(f'Ana fact {i}') for i in range(4)])
    add(edges=many[:3], max_coroutines=1, types=[], type_map=[],
        search={**search('Ana fact 0', pair_bm25=[old_work])},
        replies=[resolve_reply('Ana fact 0', [], [])] + [ts_reply(f'Ana fact {i}') for i in range(3)])
    # A reply with a missing field
    add(edges=[e_live], search={**search('Ana lives in Paris', pair_bm25=[old_live])},
        replies=[reply('dedupe_edges.resolve_edge', 'Ana lives in Paris', {'duplicate_facts': [0]})])
    # No edge
    add(edges=[])

    # resolve_extracted_edge and decide_edge_against_candidates on their own
    single = {'fn': 'resolve_extracted_edge', 'episode': ep('ep1', REF), 'routing': False}
    cases.append({**single, 'edge': e_live, 'related': [old_live], 'existing': [newer, ended],
                  'candidates': TYPES_ALL, 'replies': [resolve_reply('Ana lives in Paris', [], [0, 1, 2])]})
    cases.append({**single, 'edge': e_work, 'related': [old_work], 'existing': [], 'candidates': None, 'replies': []})
    cases.append({**single, 'edge': e_free, 'related': [], 'existing': [], 'candidates': None,
                  'replies': [ts_reply('Ana joined Acme', '2019-01-01T00:00:00Z', '2018-01-01T00:00:00Z')]})
    cases.append({**single, 'edge': e_mention, 'related': [old_live], 'existing': [old_work],
                  'candidates': ['MENTIONS'], 'routing': True,
                  'replies': [resolve_reply('Ana mentions Paris', [0], [5, 1]), ts_reply('Ana mentions Paris')]})
    decide = {'fn': 'decide_edge_against_candidates', 'routing': False}
    cases.append({**decide, 'edge': e_work, 'related': [old_live, edge('s-same', 'n-ana', 'n-acme', 'ANA works at acme')],
                  'existing': [], 'replies': []})
    cases.append({**decide, 'edge': e_work2, 'related': [], 'existing': [],
                  'replies': [resolve_reply(' ana works at ACME', [0], [0])]})
    cases.append({**decide, 'edge': e_live, 'related': [old_live], 'existing': [other, newer],
                  'replies': [resolve_reply('Ana lives in Paris', [-1, 1, 0], [0, 1, 2, 3])]})
    cases.append({**decide, 'edge': e_live, 'related': [], 'existing': [other],
                  'replies': [resolve_reply('Ana lives in Paris', [0], [0, 1])]})
    dedupe = {'fn': '_dedupe_extracted_edge', 'episode': ep('ep1', REF), 'routing': False}
    cases.append({**dedupe, 'edge': e_mention, 'related': [old_live], 'existing': [], 'candidates': {},
                  'replies': [resolve_reply('Ana mentions Paris', [], [])]})
    cases.append({**dedupe, 'edge': e_live, 'related': [], 'existing': [old_work], 'candidates': ['LIVES_IN'],
                  'replies': [resolve_reply('Ana lives in Paris', [], [0]),
                              reply('extract_edges.extract_attributes', 'Ana lives in Paris', {'city': 'Paris', 'zip': '75'})]})
    timestamps = {'fn': '_extract_edge_timestamps', 'routing': False}
    cases.append({**timestamps, 'edge': e_work, 'episode': ep('ep1', REF),
                  'replies': [ts_reply('Ana works at Acme', '2020-05-05T00:00:00', '2030-01-01T00:00:00Z')]})
    cases.append({**timestamps, 'edge': e_live, 'episode': ep('ep1', REF), 'replies': []})
    cases.append({**timestamps, 'edge': e_work, 'episode': None, 'replies': []})
    cases.append({**timestamps, 'edge': edge('x-ref', 'n-ana', 'n-acme', 'Ana will join Acme',
                                              reference_time=datetime(2025, 1, 1, 9, 0, tzinfo=timezone(timedelta(hours=-5)))),
                  'episode': None, 'routing': True,
                  'replies': [ts_reply('Ana will join Acme', '2026-01-01T00:00:00Z')]})
    cases.append({**timestamps, 'edge': e_work, 'episode': ep('ep1', REF),
                  'replies': [ts_reply('Ana works at Acme', 5)]})
    filters = {'fn': 'filter_existing_duplicate_of_edges'}
    pairs = [['n-ana', 'n-acme'], ['n-acme', 'n-ana'], ['n-ana', 'n-paris'], ['n-ana', 'n-acme']]
    for provider in ('neo4j', 'neptune', 'falkordb'):
        cases.append({**filters, 'provider': provider, 'pairs': pairs, 'stored_nodes': list(stored.values()),
                      'records': [{'source_uuid': 'n-ana', 'target_uuid': 'n-acme'},
                                  {'source_uuid': 'n-paris', 'target_uuid': 'n-ana'}, {'source_uuid': None}]})
    cases.append({**filters, 'provider': 'neo4j', 'pairs': [], 'stored_nodes': [], 'records': []})
    return cases


def review_cases() -> list[dict]:
    """The cases that the reviews of the packet added. They come after the other cases, so that the index of each earlier
    case stays the same."""
    cases = []
    stored = [node('n-ana', 'Ana', ['Entity', 'Person']), node('n-acme', 'Acme', ['Entity', 'Company']),
              node('n-paris', 'Paris', ['Entity', 'Place'])]
    old_work = edge('s-work', 'n-ana', 'n-acme', 'Ana works at Acme', 'WORKS_AT', valid_at=PAST, episodes=['ep0'])
    old_live = edge('s-live', 'n-ana', 'n-paris', 'Ana lives in Lyon', 'LIVES_IN', valid_at=PAST)
    e_live = edge('x-live', 'n-ana', 'n-paris', 'Ana lives in Paris', 'LIVES_IN', episodes=['ep1'], valid_at=REF)
    e_work = edge('x-work', 'n-ana', 'n-acme', 'Ana works at Acme', 'WORKS_AT', episodes=['ep1'])
    e_free = edge('x-free', 'n-ana', 'n-acme', 'Ana joined Acme', 'WORKS_AT', episodes=['ep1'], attributes={'role': 'x'})

    # filter_existing_duplicate_of_edges: a record value that is a list or a dict has no hash (TypeError in Python);
    # the source value is hashed before the target value; an int is hashable and finds no pair
    filters = {'fn': 'filter_existing_duplicate_of_edges', 'provider': 'neo4j', 'pairs': [['n-ana', 'n-acme']],
               'stored_nodes': stored}
    for records in (
        [{'source_uuid': ['n-ana'], 'target_uuid': 'n-acme'}],
        [{'source_uuid': {'a': 1}, 'target_uuid': 'n-acme'}],
        [{'source_uuid': 'n-ana', 'target_uuid': ['x']}],
        [{'source_uuid': 'n-ana', 'target_uuid': {'b': 2}}],
        [{'source_uuid': {'a': 1}, 'target_uuid': ['x']}],
        [{'source_uuid': 5, 'target_uuid': 'n-acme'}, {'source_uuid': 'n-ana', 'target_uuid': 'n-acme'}],
    ):
        cases.append({**filters, 'records': records})

    # _extract_edge_timestamps: the reference time of the edge comes before the valid_at of the episode
    timestamps = {'fn': '_extract_edge_timestamps', 'routing': False}
    cases.append({**timestamps, 'episode': ep('ep1', REF),
                  'edge': edge('x-ref', 'n-ana', 'n-acme', 'Ana will join Acme',
                               reference_time=datetime(2020, 5, 1, 12, 0, tzinfo=timezone(timedelta(hours=1)))),
                  'replies': [ts_reply('Ana will join Acme', '2026-01-01T00:00:00Z')]})
    # the case of test_extract_edge_timestamps_prefers_edge_reference_time
    cases.append({**timestamps, 'episode': ep('episode_uuid', datetime(2024, 2, 1, tzinfo=timezone.utc)),
                  'edge': edge('x-ref2', 'source_uuid', 'target_uuid', 'Alice knows Bob.', 'KNOWS',
                               reference_time=datetime(2024, 1, 1, tzinfo=timezone.utc)),
                  'replies': [ts_reply('Alice knows Bob.', '2024-01-02T00:00:00Z')]})

    # the exact match of resolve_extracted_edge keeps the dates of the stored edge, and ends no stored edge
    exact = edge('s-exact', 'n-ana', 'n-acme', 'ana works at  acme', 'WORKS_AT', valid_at=PAST,
                 invalid_at=datetime(2024, 1, 1, tzinfo=timezone.utc), episodes=['ep0'])
    single = {'fn': 'resolve_extracted_edge', 'episode': ep('ep1', REF), 'routing': False}
    cases.append({**single, 'edge': e_work, 'related': [exact], 'existing': [], 'candidates': None, 'replies': []})
    cases.append({**single, 'edge': e_work, 'related': [exact], 'existing': [old_live], 'candidates': TYPES_ALL,
                  'replies': []})

    # the exact match needs the same source and the same target
    decide = {'fn': 'decide_edge_against_candidates', 'routing': False}
    cases.append({**decide, 'edge': e_work, 'existing': [],
                  'related': [edge('s-ac', 'n-ana', 'n-paris', 'Ana works at Acme')],
                  'replies': [resolve_reply('Ana works at Acme', [], [])]})
    cases.append({**decide, 'edge': e_work, 'existing': [],
                  'related': [edge('s-bc', 'n-bob', 'n-acme', 'Ana works at Acme')],
                  'replies': [resolve_reply('Ana works at Acme', [], [])]})

    # an episode whose valid_at is not its created_at; the attribute prompt holds the valid_at and the attributes of the
    # edge that is resolved
    ep_late = ep('ep1', datetime(2025, 3, 3, tzinfo=timezone.utc), created_at=datetime(2025, 3, 9, tzinfo=timezone.utc))
    attributes = reply('extract_edges.extract_attributes', 'Ana joined Acme', {'role': 'lead', 'since': '2020'})
    single_late = {**single, 'episode': ep_late}
    cases.append({**single_late, 'edge': e_free, 'related': [], 'existing': [old_work], 'candidates': ['WORKS_AT'],
                  'replies': [resolve_reply('Ana joined Acme', [], []), attributes]})
    cases.append({**single_late, 'edge': e_free, 'related': [], 'existing': [], 'candidates': ['WORKS_AT'],
                  'replies': [attributes]})
    cases.append({**single_late, 'edge': e_free, 'existing': [], 'candidates': ['WORKS_AT'],
                  'related': [edge('s-work2', 'n-ana', 'n-acme', 'Ana at Acme', 'WORKS_AT', attributes={'role': 'dev'})],
                  'replies': [resolve_reply('Ana joined Acme', [0], []),
                              reply('extract_edges.extract_attributes', 'Ana at Acme', {'role': 'lead', 'since': '2020'})]})

    # the limit of the last gather: the resolved edge and the invalidated edge are both embedded, with the limit 1
    for limit in (1, None):
        cases.append({
            'fn': 'resolve_extracted_edges', 'episode': ep('ep1', REF), 'entities': stored[:2], 'stored_nodes': stored,
            'types': TYPES_ALL, 'type_map': MAP_A, 'override': None, 'strict': False, 'invalidated_by': True,
            'max_coroutines': limit, 'routing': False, 'edges': [e_live],
            'search': {'Ana lives in Paris': {'pair': {'bm25': [old_live], 'cos': []}, 'all': {'bm25': [], 'cos': []}}},
            'replies': [resolve_reply('Ana lives in Paris', [], [0])]})

    # extract_edges: max() keeps the first of two episodes with the same instant and other offsets
    plus = datetime(2025, 1, 1, 10, 0, tzinfo=timezone(timedelta(hours=1)))
    utc = datetime(2025, 1, 1, 9, 0, tzinfo=timezone.utc)
    for first, second in ((plus, utc), (utc, plus)):
        cases.append({'fn': 'extract_edges', 'episode': [ep('ep1', first), ep('ep2', second, content='Ana moved.')],
                      'nodes': NODES, 'previous': [], 'type_map': MAP_A, 'group_id': '', 'types': None, 'strict': False,
                      'instructions': None, 'replies': [reply('extract_edges.edge', '', {'edges': []})]})
    return cases


# The replies after the replies of each case: an attribute reply with known, unknown, and over-long values, and a
# timestamp reply with no dates.
FALLBACK_REPLIES = [
    reply('extract_edges.extract_attributes', '', {'role': 'developer of tools', 'city': 'Lyon', 'how': 'school', 'x': 1}),
    reply('extract_edges.extract_timestamps', '', {'valid_at': None, 'invalid_at': None}),
]


async def run_case(case: dict, mods, cancel_at=None) -> dict:
    eo, EntityEdge, EntityNode, EpisodicNode, GraphitiClients, GraphProvider, default_library, models = mods
    from graphiti_core.llm_client.config import ModelSize
    replies = case.get('replies', []) + FALLBACK_REPLIES
    texts: list[str] = []
    state['main'] = asyncio.current_task()
    if cancel_at == -1:
        # A task that is canceled before its first await: the first suspension raises CancelledError.
        state['events_at_cancel'] = 0
        state['main'].cancel()

    async def enter(kind, record):
        state['attempts'] += 1
        state['in_flight'] += 1
        state['max_in_flight'] = max(state['max_in_flight'], state['in_flight'])
        try:
            await asyncio.sleep(0)
            events.append({'kind': kind, **record})
            state['count'] += 1
            if cancel_at is not None and state['count'] - 1 == cancel_at:
                state['events_at_cancel'] = len(events)
                state['main'].cancel()
        finally:
            state['in_flight'] -= 1

    class FakeLlm:
        # The defaults of LLMClient.generate_response.
        async def generate_response(self, messages, response_model=None, max_tokens=None, model_size=ModelSize.medium,
                                    group_id=None, prompt_name=None, attribute_extraction=False):
            await enter('llm', {
                'prompt_name': prompt_name,
                'response_model': None if response_model is None else response_model.__name__,
                'max_tokens': max_tokens, 'model_size': model_size.value,
                'group_id': group_id, 'attribute_extraction': attribute_extraction,
                'messages': [[m.role, hashlib.sha256(m.content.encode('utf-8', 'surrogatepass')).hexdigest(),
                              len(m.content)] for m in messages],
            })
            last = messages[-1].content
            for item in replies:
                if item['prompt'] == prompt_name and item['needle'] in last:
                    value = item['reply']
                    if isinstance(value, dict) and '$raise' in value:
                        raise RuntimeError(value['$raise'])
                    return json.loads(json.dumps(value))
            raise LookupError(f'no reply for {prompt_name}')

    class FakeEmbedder:
        async def create_batch(self, input_data):
            await enter('embed', {'input': list(input_data)})
            out = []
            for text in input_data:
                if text not in texts:
                    texts.append(text)
                out.append([float(texts.index(text)), 1.0])
            return out

        async def create(self, input_data):
            raise AssertionError('create is not used')

    def answer(fact, search_filter, kind):
        scope = 'pair' if search_filter.edge_source_node_uuids is not None else 'all'
        table = case.get('search', {}).get(fact, {}).get(scope, {}).get(kind, [])
        return [EntityEdge(**decode(e)) for e in table]

    class FakeSearch:
        async def edge_fulltext_search(self, driver, query, search_filter, group_ids, limit):
            await enter('fulltext', {'query': query, 'group_ids': group_ids, 'limit': limit,
                                     'source': search_filter.edge_source_node_uuids,
                                     'target': search_filter.edge_target_node_uuids})
            return answer(query, search_filter, 'bm25')

        async def edge_similarity_search(self, driver, search_vector, source_node_uuid, target_node_uuid,
                                         search_filter, group_ids, limit, min_score):
            await enter('similarity', {'vector': search_vector, 'source_node_uuid': source_node_uuid,
                                       'target_node_uuid': target_node_uuid, 'group_ids': group_ids, 'limit': limit,
                                       'min_score': min_score, 'source': search_filter.edge_source_node_uuids,
                                       'target': search_filter.edge_target_node_uuids})
            return answer(texts[int(search_vector[0])], search_filter, 'cos')

    class FakeGraphOps:
        async def node_get_by_uuids(self, _cls, driver, uuids, group_id=None):
            await enter('get_nodes', {'uuids': list(uuids), 'group_id': group_id})
            return [EntityNode(**decode(n)) for n in case.get('stored_nodes', [])
                    if n['uuid'] in uuids and (group_id is None or n['group_id'] == group_id)]

    class FakeDriver:
        provider = GraphProvider(case.get('provider', 'neo4j'))
        search_interface = FakeSearch()
        graph_operations_interface = FakeGraphOps()

        async def execute_query(self, cypher_query_, **kwargs):
            await enter('query', {'query': cypher_query_, 'keys': list(kwargs), 'values': encode(list(kwargs.values()))})
            return [dict(r) for r in case.get('records', [])], None, None

    class FakeCrossEncoder:
        async def rank(self, query, passages):
            raise AssertionError('rank is not used')

    from graphiti_core.tracer import NoOpTracer

    llm = FakeLlm()
    clients = GraphitiClients.model_construct(
        driver=FakeDriver(), llm_client=llm, embedder=FakeEmbedder(), cross_encoder=FakeCrossEncoder(),
        tracer=NoOpTracer(), max_coroutines=case.get('max_coroutines'),
        prompt_library=default_library if case.get('routing') else None)
    route = clients if case.get('routing') else None
    fn = case['fn']
    type_map = {tuple(k): list(v) for k, v in case.get('type_map', [])}

    def types_of(names):
        return None if names is None else {n: models[n] for n in names}

    if fn == 'extract_edges':
        episode = case['episode']
        episode_arg = [EpisodicNode(**decode(e)) for e in episode] if isinstance(episode, list) else EpisodicNode(**decode(episode))
        nodes = [EntityNode(**decode(n)) for n in case['nodes']]
        edges, materialized = await eo.extract_edges(
            clients, episode_arg, nodes, [EpisodicNode(**decode(e)) for e in case['previous']], type_map,
            group_id=case['group_id'], edge_types=types_of(case['types']), strict_edge_types=case['strict'],
            custom_extraction_instructions=case['instructions'])
        return {'edges': [edge_view(e) for e in edges], 'materialized': [node_view(n) for n in materialized],
                'nodes': [node_view(n) for n in nodes]}
    if fn == 'resolve_extracted_edges':
        extracted = [EntityEdge(**decode(e)) for e in case['edges']]
        invalidated_by = {} if case['invalidated_by'] else None
        override = None if case['override'] is None else [EntityEdge(**decode(e)) for e in case['override']]
        resolved, invalidated, new = await eo.resolve_extracted_edges(
            clients, extracted, EpisodicNode(**decode(case['episode'])), [EntityNode(**decode(n)) for n in case['entities']],
            types_of(case['types']), type_map, existing_edges_override=override, strict_edge_types=case['strict'],
            invalidated_by=invalidated_by, trace=None)
        return {'resolved': [edge_view(e) for e in resolved], 'invalidated': [edge_view(e) for e in invalidated],
                'new': [e.uuid for e in new], 'invalidated_by': invalidated_by,
                'extracted': [edge_view(e) for e in extracted]}
    if fn in ('resolve_extracted_edge', '_dedupe_extracted_edge'):
        extracted = EntityEdge(**decode(case['edge']))
        related = [EntityEdge(**decode(e)) for e in case['related']]
        existing = [EntityEdge(**decode(e)) for e in case['existing']]
        kwargs = {'clients': route} if route is not None else {}
        result = await getattr(eo, fn)(llm, extracted, related, existing, EpisodicNode(**decode(case['episode'])),
                                       types_of(case['candidates']), **kwargs)
        return {'result': [edge_view(result[0]), [edge_view(e) for e in result[1]],
                           None if result[2] is None else [edge_view(e) for e in result[2]]],
                'extracted': edge_view(extracted), 'related': [edge_view(e) for e in related],
                'existing': [edge_view(e) for e in existing]}
    if fn == 'decide_edge_against_candidates':
        extracted = EntityEdge(**decode(case['edge']))
        related = [EntityEdge(**decode(e)) for e in case['related']]
        existing = [EntityEdge(**decode(e)) for e in case['existing']]
        decision = await eo.decide_edge_against_candidates(llm, extracted, related, existing, clients=route)
        return {'exact_match': None if decision.exact_match is None else decision.exact_match.uuid,
                'duplicates': [e.uuid for e in decision.duplicates],
                'contradicted': [e.uuid for e in decision.contradicted]}
    if fn == '_extract_edge_timestamps':
        extracted = EntityEdge(**decode(case['edge']))
        episode = None if case['episode'] is None else EpisodicNode(**decode(case['episode']))
        await eo._extract_edge_timestamps(llm, extracted, episode, clients=route)
        return {'edge': edge_view(extracted)}
    if fn == 'filter_existing_duplicate_of_edges':
        stored = {n['uuid']: EntityNode(**decode(n)) for n in case['stored_nodes']}
        pairs = [(stored[s], stored[t]) for s, t in case['pairs']]
        result = await eo.filter_existing_duplicate_of_edges(clients.driver, pairs)
        return {'pairs': [[s.uuid, t.uuid] for s, t in result]}
    raise AssertionError(fn)


def reset() -> None:
    events.clear()
    state.update(in_flight=0, max_in_flight=0, attempts=0, count=0, main=None, events_at_cancel=None)


def call_count(output: dict) -> int:
    return sum(1 for e in output['events'] if e['kind'] != 'log')


def record_cancel(case: dict, mods, k: int) -> dict:
    """The case again with a cancel at call k (k = -1: before the first await)."""
    reset()
    try:
        asyncio.run(run_case(case, mods, k))
        error = None
    except asyncio.CancelledError:
        error = 'CancelledError'
    except Exception as exc:  # noqa: BLE001 - the recording keeps each exception
        error = type(exc).__name__
    if error != 'CancelledError':
        raise SystemExit(f'The cancel at call {k} gave {error} and not CancelledError')
    after = events[state['events_at_cancel']:]
    return placeholders({'k': k, 'error': error, 'attempts': state['attempts'], 'count': state['count'],
                         'after': list(after)}, {})


def record_async(mods) -> list[dict]:
    out = []
    for index, case in enumerate(extract_cases() + resolve_cases() + review_cases()):
        reset()
        try:
            output = {'ok': asyncio.run(run_case(case, mods))}
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            text = str(error)
            if type(error).__name__ == 'ValidationError':
                text = text.splitlines()[0]
            output = {'error': {'type': type(error).__name__, 'text': text}}
        output['events'] = list(events)
        output['max_in_flight'] = state['max_in_flight']
        item = {'in': case, 'out': placeholders(output, {})}
        if index in CANCEL_CASES:
            item['cancel'] = [record_cancel(case, mods, k) for k in range(-1, call_count(output) - 1)]
        out.append(item)
    return out


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    os.environ.pop('GRAPHITI_ATTRIBUTE_MAX_LENGTH', None)
    from pydantic import BaseModel, Field, create_model

    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.edges import EntityEdge
    from graphiti_core.graphiti_types import GraphitiClients
    from graphiti_core.nodes import CommunityNode, EntityNode, EpisodicNode
    from graphiti_core.prompts.lib import default_chat_prompt_library
    from graphiti_core.utils.maintenance import dangling_endpoints
    from graphiti_core.utils.maintenance import edge_operations as eo

    class Capture(logging.Handler):
        def emit(self, record):
            events.append({'kind': 'log', 'level': record.levelname, 'message': record.getMessage()})

    root = logging.getLogger('graphiti_core')
    root.setLevel(logging.DEBUG)
    root.propagate = False
    root.addHandler(Capture(logging.DEBUG))

    eo.utc_now = lambda: FIXED_NOW
    dangling_endpoints.utc_now = lambda: FIXED_NOW
    eo.set = InsertionOrderSet

    models = {}
    for name, spec in TYPE_DEFS.items():
        fields = {}
        for field_name, required, max_length in spec['fields']:
            kwargs = {} if max_length is None else {'max_length': max_length}
            fields[field_name] = (str, Field(..., **kwargs)) if required else (str | None, Field(None, **kwargs))
        model = create_model(name, __base__=BaseModel, __doc__=spec['doc'], **fields)
        models[name] = model
    type_defs = {
        name: {'doc': model.__doc__, 'fields': [[f, info.is_required(), TYPE_DEFS[name]['fields'][i][2]]
                                                for i, (f, info) in enumerate(model.model_fields.items())]}
        for name, model in models.items()
    }

    constants = {'DEFAULT_EDGE_NAME': eo.DEFAULT_EDGE_NAME}
    record_sync(eo, EntityEdge, EntityNode, CommunityNode, models)
    async_cases = record_async((eo, EntityEdge, EntityNode, EpisodicNode, GraphitiClients, GraphProvider,
                                default_chat_prompt_library, models))

    dump = lambda r: json.dumps(r, ensure_ascii=True, allow_nan=False, separators=(',', ':'))  # noqa: E731
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","fixed_now":"' + FIXED_NOW.isoformat() + '",'
        + '"constants":' + dump(constants) + ',"types":' + dump(type_defs) + ',"cases":[\n'
        + ',\n'.join(dump(r) for r in records) + '\n],"async_cases":[\n' + ',\n'.join(dump(r) for r in async_cases) + '\n]}\n'
    )
    size = len(text.encode('utf-8'))
    if size > 1_572_864:
        raise SystemExit(f'The values file would have {size} bytes, above the limit of 1,572,864 (R25).')
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(records)} cases and {len(async_cases)} async cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(records)} cases and {len(async_cases)} async cases, {size} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
