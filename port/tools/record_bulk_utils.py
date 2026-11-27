"""Records results of graphiti_core/utils/bulk_utils.py from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_bulk_utils.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_bulk_utils.py --check

Plan 0004, layer 7, packet P7e. The tool has two parts.

The sync part calls _build_directed_uuid_map, compress_uuid_map (and so UnionFind), resolve_edge_pointers, and
missing_ingestion_interface_operations with generated inputs from a fixed seed. Fixed inputs add a chain of three
duplicates and a cycle. It records the result, the edges after the call, and each log line of graphiti_core.

The async part runs retrieve_previous_episodes_bulk, _entity_node_record, _entity_edge_record, update_entity_edges_tx,
update_entity_nodes_tx, add_nodes_and_edges_bulk_tx, add_nodes_and_edges_bulk (and so execute_write_with_timeout),
extract_nodes_and_edges_bulk, dedupe_nodes_bulk, and dedupe_edges_bulk with fakes at the network boundary:

- The LLM client replaces generate_response. It records the prompt name, the response model name, the model size,
  max_tokens, group_id, attribute_extraction, and the role, the SHA-256, and the length of each message. It answers
  with the first reply of the case (then of FALLBACK_REPLIES) for the prompt name whose needle is in the last message.
- The embedder gives the vector of the case table `vectors` for a text, or [i, 1.0] for the i-th distinct text.
- The driver has a session, a transaction, a graph operations interface (when the case says `ops`), and a search
  interface. The session records execute_write and close, and it has `query_timeout_ms` when the case says
  `timeout_session`. The transaction records each query with each parameter, in order. The interface records each
  bulk save and bulk update with its records, and answers retrieve_episodes, node_get_by_names, and
  node_get_by_attribute_values from the case. The search interface answers node_similarity_search from the case by
  the text of the vector.
- Each fake yields once (asyncio.sleep(0)) and then records its call. The calls and the log lines are one list. Each
  case keeps the largest count of fake calls in flight. driver.session() and session.close() are recorded, and they are
  not counted as calls of the cancellation tests: close runs in a finally block after a cancellation too.

Fixed values on both sides: each utc_now of graphiti_core gives FIXED_NOW, and the C# test gives the port a clock with
the same time. A uuid4 that the code makes is random: the test replaces each one, in the order of its first appearance
in the case output, by "<new:N>". A timed log line has its milliseconds replaced by "<ms>".

Python iterates a set in hash order (RULES.md R14). bulk_utils iterates two sets: `list(set(node.labels + ['Entity']))`
and `all_uuids` of compress_uuid_map. The port gives `Entity` first and then the other labels in their first order, and
the UUIDs in the order of the pairs. The tool binds the module name `set` of bulk_utils to a set that iterates in
insertion order and puts `Entity` first, which is the order of the port. It binds the sets of dedup_helpers,
attribute_utils, and edge_operations, and the EntityNode of node_operations, as the recorders of packets P7a, P7b, and
P7c do.

Path that the recording does not reach, because the port throws there (R24):
- a Cypher bulk query of an entity node on FalkorDB or Neptune, which is a list of queries.

Packet P7g (decision D52, open item O6) adds the cases of timeout_cases at the end, so that the index of each earlier
case stays: add_nodes_and_edges_bulk with write_timeout on Neo4j, and execute_write_with_timeout on Neo4j and on another
provider, with a work function that returns and one that raises. The session records `func.timeout`, the attribute
that unit_of_work(timeout=...) of the neo4j package puts on the work function, and None for a plain work function. The
limits are floats (7.5, 2.5, 0.0, -1.0), because the port takes a double: an int limit reaches the fake as an int.

The fix round of packet P7e (the two reviews) adds the cases of review_cases() at the end: a write whose embedder sleeps
past the limit (the work gets the token of the limit, and the session records a rollback with CancelledError), the
query_timeout_ms of the session on each path (the `close` fake records it too), a limit of 1.0019 and a limit below 1 ms,
two edges of one episode, the fallback to the canonical node, an identity match whose second value differs, the
similarity limit (0.6 and 0.55), the group of each episode in the separate extraction, episodes with another content and
another valid_at, an `IndexError` of dedupe_nodes_bulk, and one call of each method with each optional argument left out.

It writes tests/Port.Evaluation/values/bulk_utils.json. --check writes nothing and exits 1 if the file differs from
what the run would write. The tool needs Python 3.12 and the pinned upstream in the folder of the variable
GRAPHITI_UPSTREAM, or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import copy
import hashlib
import json
import logging
import math
import os
import random
import re
import sys
import warnings
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'bulk_utils.json'

SEED = 20261129
FIXED_NOW = datetime(2026, 3, 4, 5, 6, 7, 891011, tzinfo=timezone.utc)
UUID4 = re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}')
MS = re.compile(r'in [0-9.e+-]+ ms')

rng = random.Random(SEED)
events: list[dict] = []
state: dict = {}


class InsertionOrderSet(set):
    """A set that iterates in insertion order and puts `Entity` first, the order of the port (RULES.md R14)."""

    def __init__(self, items=()):
        super().__init__()
        self._order = []
        items = list(items)
        if 'Entity' in items:
            self.add('Entity')
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
        'name_embedding': node.name_embedding,
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


records: list[dict] = []


def record(name: str, fn, inputs: dict, outputs) -> None:
    events.clear()
    after = inputs.pop('_after', lambda: None)
    try:
        out = {'ok': outputs(fn())}
    except Exception as error:  # noqa: BLE001 - the recording keeps each exception
        out = {'error': {'type': type(error).__name__}}
    out['log'] = list(events)
    out['after'] = after()
    records.append({'f': name, 'in': inputs, 'out': out})


# ---------------------------------------------------------------------------------------------------------------------
# Sync part

UUIDS = ['a', 'b', 'c', 'd', 'e', 'f', 'Z', 'é', '\U0001F600']


def pair_inputs() -> list[list[list[str]]]:
    fixed = [
        [],
        [['a', 'b'], ['b', 'c'], ['c', 'd']],  # a chain of three duplicates
        [['c', 'b'], ['b', 'a'], ['d', 'c']],
        [['a', 'b'], ['b', 'c'], ['c', 'a']],  # a cycle
        [['b', 'a'], ['a', 'b']],
        [['a', 'a']],
        [['Z', 'a'], ['é', '\U0001F600'], ['\U0001F600', 'Z']],
        [['\uff21', '\U0001F600']],  # U+FF21 sorts above U+1F600 in UTF-16 order, and below it by code point
    ]
    generated = []
    for _ in range(60):
        generated.append([[rng.choice(UUIDS), rng.choice(UUIDS)] for _ in range(rng.randint(1, 7))])
    return fixed + generated


def gen_edge(uuid: str) -> dict:
    return {'uuid': uuid, 'group_id': 'g', 'source_node_uuid': rng.choice(UUIDS[:5]),
            'target_node_uuid': rng.choice(UUIDS[:5]), 'name': 'KNOWS', 'fact': 'f',
            'created_at': dt(datetime(2024, 1, 1, tzinfo=timezone.utc))}


def record_sync(bu, EntityEdge, EpisodicEdge, GraphOperationsInterface) -> None:
    for pairs in pair_inputs():
        record('_build_directed_uuid_map', lambda pairs=pairs: bu._build_directed_uuid_map([tuple(p) for p in pairs]),
               {'pairs': pairs}, lambda r: [[k, v] for k, v in r.items()])
        record('compress_uuid_map', lambda pairs=pairs: bu.compress_uuid_map([tuple(p) for p in pairs]),
               {'pairs': pairs}, lambda r: [[k, v] for k, v in r.items()])

    for i in range(50):
        edges = [gen_edge(f'e{j}') for j in range(rng.randint(0, 5))]
        uuid_map = {u: rng.choice(UUIDS[:6]) for u in rng.sample(UUIDS[:5], rng.randint(0, 4))}
        episodic = i % 5 == 0
        holder = {}

        def run(edges=edges, uuid_map=uuid_map, episodic=episodic, holder=holder):
            if episodic:
                objs = [EpisodicEdge(**{k: v for k, v in decode(e).items() if k not in ('name', 'fact')}) for e in edges]
            else:
                objs = [EntityEdge(**decode(e)) for e in edges]
            holder['edges'] = objs
            return bu.resolve_edge_pointers(objs, dict(uuid_map))

        record('resolve_edge_pointers', run,
               {'edges': edges, 'uuid_map': uuid_map, 'episodic': episodic,
                '_after': lambda holder=holder: [[e.source_node_uuid, e.target_node_uuid] for e in holder['edges']]},
               lambda r: [e.uuid for e in r])

    class FakeDriver:
        def __init__(self, interface):
            self.graph_operations_interface = interface

    for overridden in (None, [], ['node_save_bulk', 'edge_update_bulk'], ['episodic_node_save_bulk', 'edge_save_bulk',
                                                                            'episodic_edge_save_bulk'],
                       list(bu.INGESTION_INTERFACE_OPERATIONS)):
        if overridden is None:
            driver = FakeDriver(None)
        else:
            async def op(self, *args, **kwargs):
                return None

            cls = type('Ops', (GraphOperationsInterface,), {name: op for name in overridden})
            driver = FakeDriver(cls())
        record('missing_ingestion_interface_operations', lambda driver=driver: bu.missing_ingestion_interface_operations(driver),
               {'overridden': overridden}, lambda r: r)


# ---------------------------------------------------------------------------------------------------------------------
# Async part

REF = datetime(2025, 1, 1, tzinfo=timezone.utc)


def ep(uuid: str, group_id: str = 'g', content: str = 'Alice Johnson works at Acme Corporation.',
       source: str = 'message', valid_at: datetime = REF) -> dict:
    return {'uuid': uuid, 'name': 'episode ' + uuid, 'group_id': group_id, 'labels': [], 'source': source,
            'source_description': 'chat', 'content': content, 'valid_at': dt(valid_at),
            'created_at': dt(datetime(2025, 1, 1, tzinfo=timezone.utc))}


def node(uuid: str, name: str, labels: list[str] | None = None, group_id: str = 'g', **extra) -> dict:
    data = {'uuid': uuid, 'name': name, 'group_id': group_id, 'labels': labels or ['Entity'],
            'created_at': dt(datetime(2024, 1, 1, tzinfo=timezone.utc)), 'summary': ''}
    data.update(extra)
    return data


def edge(uuid: str, source: str, target: str, fact: str, name: str = 'RELATES_TO', group_id: str = 'g', **extra) -> dict:
    data = {'uuid': uuid, 'group_id': group_id, 'source_node_uuid': source, 'target_node_uuid': target,
            'name': name, 'fact': fact, 'created_at': dt(datetime(2024, 1, 1, tzinfo=timezone.utc))}
    for key, value in extra.items():
        data[key] = dt(value) if isinstance(value, datetime) else value
    return data


def episodic_edge(uuid: str, source: str, target: str) -> dict:
    return {'uuid': uuid, 'group_id': 'g', 'source_node_uuid': source, 'target_node_uuid': target,
            'created_at': dt(datetime(2024, 1, 1, tzinfo=timezone.utc))}


def reply(prompt: str, needle: str, value) -> dict:
    return {'prompt': prompt, 'needle': needle, 'reply': value}


def resolve_reply(fact: str, duplicates, contradicted=()) -> dict:
    return reply('dedupe_edges.resolve_edge', f'<NEW FACT>\n{fact}\n</NEW FACT>',
                 {'duplicate_facts': list(duplicates), 'contradicted_facts': list(contradicted)})


FALLBACK_REPLIES = [
    reply('extract_nodes.extract_message', '', {'extracted_entities': []}),
    reply('extract_nodes.extract_text', '', {'extracted_entities': []}),
    reply('extract_nodes.extract_json', '', {'extracted_entities': []}),
    reply('extract_edges.edge', '', {'edges': []}),
    reply('dedupe_nodes.nodes', '', {'entity_resolutions': []}),
    reply('dedupe_edges.resolve_edge', '', {'duplicate_facts': [], 'contradicted_facts': []}),
    reply('extract_edges.extract_attributes', '', {'role': 'engineer', 'x': 1}),
    reply('extract_edges.extract_timestamps', '', {'valid_at': None, 'invalid_at': None}),
]

TYPE_DEFS = {
    'Person': {'doc': 'A human person.', 'fields': [['email', False, None], ['role', False, None]]},
    'Company': {'doc': None, 'fields': [['domain', False, 40]]},
    'WORKS_AT': {'doc': 'Employment relation.', 'fields': [['role', False, 12]]},
}

NODES = [node('n-alice', 'Alice Johnson', ['Entity', 'Person']), node('n-acme', 'Acme Corporation', ['Entity', 'Company']),
         node('n-bob', 'Bob', ['Person', 'Entity'], summary='Bob is a person.')]


def write_cases() -> list[dict]:
    cases = []
    with_emb = node('n-emb', 'Embedded', ['Entity'], name_embedding=[0.5, 0.25],
                    attributes={'uuid': 'stolen', 'labels': ['X'], 'color': 'red', 'n': 3})
    no_emb = node('n-plain', 'Plain Node', ['Company', 'Entity', 'Company'],
                  attributes={'summary': 'stale', 'domain': 'acme.com'})
    e1 = edge('x-1', 'n-emb', 'n-plain', 'Embedded works at Plain', 'WORKS_AT', episodes=['ep1', 'ep2'],
              valid_at=REF, attributes={'reference_time': '2020-01-01T00:00:00Z', 'fact': 'old', 'role': 'dev'})
    e2 = edge('x-2', 'n-plain', 'n-emb', 'Plain employs Embedded', fact_embedding=[1.0, 0.0],
              invalid_at=datetime(2026, 1, 1, tzinfo=timezone.utc), reference_time=REF)
    episodes = [ep('ep1'), ep('ep2', source='json', content='{"a": 1}'), ep('ep3', source='text', content='Text.')]
    ep_edges = [episodic_edge('ee-1', 'ep1', 'n-emb'), episodic_edge('ee-2', 'ep2', 'n-plain')]

    cases.append({'fn': '_entity_node_record', 'node': with_emb})
    cases.append({'fn': '_entity_node_record', 'node': no_emb})
    cases.append({'fn': '_entity_edge_record', 'edge': e1})
    cases.append({'fn': '_entity_edge_record', 'edge': e2})
    for ops in (True, False):
        for provider in (('neo4j', 'relate') if not ops else ('neo4j',)):
            cases.append({'fn': 'update_entity_edges_tx', 'ops': ops, 'provider': provider, 'edges': [e1, e2]})
            cases.append({'fn': 'update_entity_nodes_tx', 'ops': ops, 'provider': provider, 'nodes': [with_emb, no_emb]})
            cases.append({'fn': 'add_nodes_and_edges_bulk_tx', 'ops': ops, 'provider': provider, 'episodes': episodes,
                          'episodic_edges': ep_edges, 'nodes': [no_emb, with_emb], 'edges': [e2, e1]})
    for provider in ('falkordb', 'neptune'):
        cases.append({'fn': 'update_entity_edges_tx', 'ops': False, 'provider': provider, 'edges': [e1]})
    cases.append({'fn': 'update_entity_edges_tx', 'ops': True, 'provider': 'neo4j', 'edges': []})
    cases.append({'fn': 'update_entity_nodes_tx', 'ops': False, 'provider': 'neo4j', 'nodes': []})
    cases.append({'fn': 'add_nodes_and_edges_bulk_tx', 'ops': False, 'provider': 'neo4j', 'episodes': [],
                  'episodic_edges': [], 'nodes': [], 'edges': []})
    # add_nodes_and_edges_bulk: one session with no limit, and execute_write_with_timeout on a provider other than
    # Neo4j, with and without query_timeout_ms on the session, and with a limit of zero.
    base = {'fn': 'add_nodes_and_edges_bulk', 'episodes': episodes[:2], 'episodic_edges': ep_edges, 'nodes': [no_emb],
            'edges': [e1]}
    cases.append({**base, 'ops': False, 'provider': 'neo4j', 'write_timeout': None})
    cases.append({**base, 'ops': True, 'provider': 'neo4j', 'write_timeout': None})
    cases.append({**base, 'ops': False, 'provider': 'relate', 'write_timeout': 2.5, 'timeout_session': True})
    cases.append({**base, 'ops': True, 'provider': 'relate', 'write_timeout': 2.5, 'timeout_session': False})
    cases.append({**base, 'ops': False, 'provider': 'relate', 'write_timeout': 0, 'timeout_session': True})
    cases.append({**base, 'ops': False, 'provider': 'relate', 'write_timeout': -1, 'timeout_session': False})
    return cases


def retrieve_cases() -> list[dict]:
    stored = {'g': [ep('old1', valid_at=datetime(2024, 1, 1, tzinfo=timezone.utc)),
                    ep('old2', valid_at=datetime(2024, 6, 1, tzinfo=timezone.utc))],
              'h': [ep('old3', group_id='h')]}
    episodes = [ep('ep1'), ep('ep2', group_id='h', valid_at=datetime(2025, 6, 1, tzinfo=timezone.utc)),
                ep('ep3', group_id='x'), ep('ep4')]
    return [
        {'fn': 'retrieve_previous_episodes_bulk', 'ops': True, 'episodes': episodes, 'stored': stored, 'max_coroutines': None},
        {'fn': 'retrieve_previous_episodes_bulk', 'ops': True, 'episodes': episodes, 'stored': stored, 'max_coroutines': 2},
        {'fn': 'retrieve_previous_episodes_bulk', 'ops': True, 'episodes': [], 'stored': stored, 'max_coroutines': None},
    ]


def x_entity(name: str, type_id: int = 0) -> dict:
    return {'name': name, 'entity_type_id': type_id}


def x_edge(source, target, relation, fact) -> dict:
    return {'source_entity_name': source, 'target_entity_name': target, 'relation_type': relation, 'fact': fact}


def extract_cases() -> list[dict]:
    episodes = [[ep('ep1'), []], [ep('ep2', content='Bob knows Alice Johnson.'), [ep('ep0', content='Earlier.')]],
                [ep('ep3', source='text', content='Acme hired Carol.'), []]]
    replies = [
        reply('extract_nodes.extract_message', 'Alice Johnson works', {'extracted_entities': [
            x_entity('Alice Johnson', 1), x_entity('Acme Corporation', 2), x_entity('', 0)]}),
        reply('extract_nodes.extract_message', 'Bob knows', {'extracted_entities': [
            x_entity('Bob', 1), x_entity('Alice Johnson', 1)]}),
        reply('extract_nodes.extract_text', 'Acme hired', {'extracted_entities': [x_entity('Acme', 9), x_entity('Carol')]}),
        reply('extract_edges.edge', 'Alice Johnson works', {'edges': [
            x_edge('Alice Johnson', 'Acme Corporation', 'WORKS_AT', 'Alice Johnson works at Acme Corporation'),
            x_edge('Alice Johnson', 'Dana', 'KNOWS', 'Alice Johnson knows Dana')]}),
        reply('extract_edges.edge', 'Bob knows', {'edges': [x_edge('Bob', 'Alice Johnson', 'KNOWS', 'Bob knows Alice')]}),
    ]
    base = {'fn': 'extract_nodes_and_edges_bulk', 'episode_tuples': episodes, 'replies': replies,
            'type_map': [[['Person', 'Company'], ['WORKS_AT']]], 'entity_types': None, 'edge_types': None,
            'excluded': None, 'instructions': None, 'combined': False, 'strict': False, 'max_coroutines': None}
    return [
        {**base},
        {**base, 'entity_types': ['Person', 'Company'], 'edge_types': ['WORKS_AT'], 'excluded': ['Company'],
         'instructions': 'Prefer full names.', 'strict': True, 'max_coroutines': 1},
        {**base, 'episode_tuples': episodes[:1], 'max_coroutines': 2, 'type_map': []},
        {**base, 'episode_tuples': [], 'combined': True},
        {**base, 'episode_tuples': [], 'combined': False},
    ]


def dedupe_node_cases() -> list[dict]:
    stored_alice = node('s-alice', 'Alice Johnson', ['Entity', 'Person'], summary='Stored Alice.')
    eps = [ep('ep1'), ep('ep2'), ep('ep3')]
    base = {'fn': 'dedupe_nodes_bulk', 'ops': True, 'episodes': eps, 'identity_properties': None, 'entity_types': None,
            'search': {}, 'exact': [], 'identity': {}, 'replies': [], 'max_coroutines': None}
    in_batch = [
        [node('a1', 'Alice Johnson', ['Entity', 'Person']), node('c1', 'Acme Corporation', ['Entity', 'Company'])],
        [node('a2', 'alice  johnson', ['Entity']), node('b2', 'Bob'), node('c2', 'Acme Corporation Inc')],
        [node('c3', 'ACME CORPORATION'), node('b3', 'Bob'), node('z3', 'Zoë Durand'), node('b4', ' bob ')],
    ]
    identity = [
        [node('p1', 'Alice Smith', ['Entity', 'Person'], attributes={'email': 'shared@example.com'})],
        [node('p2', 'Bob Jones', ['Entity', 'Person'], attributes={'email': 'shared@example.com', 'title': 'eng'}),
         node('p3', 'Carol', ['Entity', 'Person'], attributes={'email': None})],
        [node('p4', 'alice smith', ['Entity', 'Person'], attributes={'email': 'other@example.com', 'phone': '1'})],
    ]
    to_stored = [
        [node('a1', 'Alice Johnson', ['Entity', 'Person'])],
        [node('a2', 'Alice Johnson', ['Entity']), node('x2', 'Alicia Johnston')],
        [node('a3', 'Ally J')],
    ]
    return [
        {**base, 'nodes': in_batch},
        {**base, 'nodes': in_batch, 'max_coroutines': 1, 'episodes': eps},
        {**base, 'nodes': identity, 'identity_properties': {'Person': ['email']}},
        {**base, 'nodes': identity, 'identity_properties': {'Company': ['domain']}},
        {**base, 'nodes': to_stored, 'exact': [stored_alice],
         'search': {'Alicia Johnston': [stored_alice], 'Ally J': [stored_alice]},
         'replies': [reply('dedupe_nodes.nodes', 'Alicia Johnston', {'entity_resolutions': [
             {'id': 0, 'name': 'Alicia Johnston', 'duplicate_candidate_id': 0}]}),
             reply('dedupe_nodes.nodes', 'Ally J', {'entity_resolutions': [
                 {'id': 0, 'name': 'Ally J', 'duplicate_candidate_id': 7}]})]},
        {**base, 'nodes': [], 'episodes': []},
        {**base, 'nodes': [[node('s1', 'Solo')]], 'episodes': eps[:1]},
    ]


def dedupe_edge_cases() -> list[dict]:
    eps = [ep('ep1'), ep('ep2'), ep('ep3')]
    chain = [
        [edge('e-c', 'n-a', 'n-b', 'Alice works at Acme', episodes=['ep1']),
         edge('e-x', 'n-a', 'n-b', 'Totally different words', episodes=['ep1']),
         edge('e-y', 'n-a', 'n-c', 'Alice works at Acme', episodes=['ep1'])],
        [edge('e-b', 'n-a', 'n-b', 'alice WORKS at acme', episodes=['ep2']),
         edge('e-q', 'n-a', 'n-b', 'Unrelated phrase here', episodes=['ep2'])],
        [edge('e-a', 'n-a', 'n-b', 'Alice is employed at Acme', episodes=['ep3', 'ep1'])],
    ]
    vectors = {'Totally different words': [1.0, 0.0], 'Unrelated phrase here': [0.9, 0.1],
               'Alice works at Acme': [0.0, 1.0], 'alice WORKS at acme': [0.0, 1.0],
               'Alice is employed at Acme': [0.1, 1.0]}
    chain_replies = [
        resolve_reply('Alice is employed at Acme', [0]),
        resolve_reply('alice WORKS at acme', [0]),
        resolve_reply('Alice works at Acme', [1]),
        resolve_reply('Totally different words', [0]),
    ]
    cycle = [
        [edge('e-1', 'n-a', 'n-b', 'Bob met Carol', episodes=['ep1'])],
        [edge('e-2', 'n-a', 'n-b', 'Bob met Carol again', episodes=['ep2'])],
    ]
    cycle_replies = [resolve_reply('Bob met Carol', [0]), resolve_reply('Bob met Carol again', [0])]
    base = {'fn': 'dedupe_edges_bulk', 'episodes': eps, 'vectors': vectors, 'types': [], 'type_map': [],
            'routing': False, 'max_coroutines': None}
    return [
        {**base, 'edges': chain, 'replies': chain_replies},
        {**base, 'edges': chain, 'replies': chain_replies, 'types': ['WORKS_AT'], 'routing': True, 'max_coroutines': 2},
        {**base, 'edges': cycle, 'replies': cycle_replies, 'episodes': eps[:2]},
        {**base, 'edges': [[], []], 'replies': [], 'episodes': eps[:2]},
        {**base, 'edges': [[edge('e-s', 'n-a', 'n-b', 'Solo fact', episodes=['ep1'], fact_embedding=[0.3, 0.4])]],
         'replies': [], 'episodes': eps[:1]},
    ]


def timeout_cases() -> list[dict]:
    episodes = [ep('ep1')]
    nodes = [node('n-plain', 'Plain Node', ['Entity'])]
    base = {'fn': 'add_nodes_and_edges_bulk', 'episodes': episodes, 'episodic_edges': [], 'nodes': nodes, 'edges': []}
    cases = [
        {**base, 'ops': False, 'provider': 'neo4j', 'write_timeout': 7.5},
        {**base, 'ops': True, 'provider': 'neo4j', 'write_timeout': 2.5},
        {**base, 'ops': False, 'provider': 'neo4j', 'write_timeout': 0.0},
        {**base, 'ops': False, 'provider': 'neo4j', 'write_timeout': -1.0},
    ]
    for provider, timeout, raises, timeout_session in (
        ('neo4j', 7.5, False, False), ('neo4j', 2.5, True, False), ('neo4j', 0.0, False, False),
        ('neo4j', -1.0, True, False), ('relate', 2.5, True, True), ('relate', 7.5, False, False),
    ):
        cases.append({'fn': 'execute_write_with_timeout', 'ops': False, 'provider': provider, 'timeout': timeout,
                      'raises': raises, 'timeout_session': timeout_session, 'value': 'x'})
    return cases


def review_cases() -> list[dict]:
    """The cases of the fix round of packet P7e. They come last, so that the index of each earlier case stays."""
    later = lambda month: datetime(2025, month, month, tzinfo=timezone.utc)  # noqa: E731
    varied = [ep('ep1'), ep('ep2', content='Second episode text.', valid_at=later(2)),
              ep('ep3', content='Third episode, with Ally J.', valid_at=later(3))]
    plain = [ep('ep1'), ep('ep2'), ep('ep3')]

    # The write: a limit that the work must see, the value of query_timeout_ms, and a limit below one millisecond.
    write = {'fn': 'add_nodes_and_edges_bulk', 'episodes': [ep('ep1')], 'episodic_edges': [],
             'nodes': [node('n-plain', 'Plain Node', ['Entity'])], 'edges': [], 'ops': False, 'provider': 'relate',
             'timeout_session': True}
    cases = [
        {**write, 'write_timeout': 0.2, 'slow_embed': True},
        {**write, 'write_timeout': 1.0019},
        {**write, 'write_timeout': 0.0005, 'block_write': True},
    ]

    # dedupe_nodes_bulk
    stored_alice = node('s-alice', 'Alice Johnson', ['Entity', 'Person'], summary='Stored Alice.')
    nodes_base = dedupe_node_cases()[0]
    identity = copy.deepcopy(dedupe_node_cases()[2])
    identity['nodes'][1][0]['attributes']['email'] = ' shared@example.com '
    varied_nodes = copy.deepcopy(dedupe_node_cases()[4])
    varied_nodes['episodes'] = varied
    cases += [
        {**nodes_base, 'nodes': [[node('a1', 'Alice Johnson')], []], 'episodes': plain[:1]},
        {**nodes_base, 'nodes': [[node('a1', 'alice johnson')], [node('a1', 'Alice Johnson')]], 'episodes': plain[:2],
         'exact': [stored_alice]},
        identity,
        varied_nodes,
    ]

    # dedupe_edges_bulk
    edges_base = dedupe_edge_cases()[0]
    limit_vectors = {'Red apples': [1.0, 0.0], 'Blue sky': [0.6, 0.8], 'Green leaf': [1.0, 0.0],
                     'Dark night': [0.55, math.sqrt(1 - 0.55 ** 2)]}
    varied_edges = [
        [edge('d-1', 'n-a', 'n-b', 'Alice works at Acme', episodes=['ep1'])],
        [edge('d-2', 'n-a', 'n-b', 'Alice is employed at Acme', episodes=[])],
        [edge('d-3', 'n-a', 'n-b', 'Acme employs Alice', episodes=[])],
    ]
    cases += [
        {**edges_base, 'edges': [[edge('w-1', 'n-a', 'n-b', 'Alice likes tea', episodes=['ep1']),
                                  edge('w-2', 'n-a', 'n-b', 'Alice likes coffee', episodes=['ep1'])]],
         'replies': [], 'vectors': {}, 'episodes': plain[:1]},
        {**edges_base, 'edges': [[edge('l-1', 'n-a', 'n-b', 'Red apples', episodes=['ep1']),
                                  edge('l-2', 'n-a', 'n-b', 'Blue sky', episodes=['ep1']),
                                  edge('l-3', 'n-c', 'n-d', 'Green leaf', episodes=['ep1']),
                                  edge('l-4', 'n-c', 'n-d', 'Dark night', episodes=['ep1'])]],
         'replies': [], 'vectors': limit_vectors, 'episodes': plain[:1]},
        {**edges_base, 'edges': varied_edges, 'episodes': varied, 'vectors': {},
         'replies': [resolve_reply('Alice is employed at Acme', [0]), resolve_reply('Acme employs Alice', [1])]},
    ]

    # The group of each episode in the separate extraction.
    extract = extract_cases()[0]
    combined_replies = [
        reply('extract_nodes_and_edges.extract_message', 'Alice Johnson works', {
            'extracted_entities': [{'name': 'Alice Johnson', 'entity_type_id': 0}, {'name': 'Acme', 'entity_type_id': 0}],
            'edges': [{'source_entity_name': 'Alice Johnson', 'target_entity_name': 'Acme', 'relation_type': 'WORKS_AT',
                       'fact': 'Alice Johnson works at Acme', 'episode_indices': [0]}]}),
        reply('extract_nodes_and_edges.extract_message', '', {'extracted_entities': [], 'edges': []}),
        reply('extract_edges.extract_timestamps_batch', '', {'timestamps': [{'valid_at': None, 'invalid_at': None}]}),
    ]
    groups = [[ep('ep1'), []], [ep('ep2', group_id='h', content='Bob knows Alice Johnson.'), [ep('ep0', content='Earlier.')]]]
    cases += [
        {**extract, 'episode_tuples': groups},
        # One call of each method with each optional argument left out.
        {**retrieve_cases()[0], 'defaults': True},
        {**write, 'provider': 'neo4j', 'timeout_session': False, 'write_timeout': None, 'defaults': True},
        {**extract, 'replies': combined_replies + extract['replies'], 'defaults': True},
        {**extract, 'replies': combined_replies + extract['replies'], 'fn': 'extract_nodes_and_edges_bulk_combined',
         'defaults': True},
        {**extract, 'fn': 'extract_nodes_and_edges_bulk_separate', 'defaults': True},
        {**nodes_base, 'defaults': True},
        # The combined extraction with each argument: the ontology, the exclusion, the strict flag, and the instructions.
        {**extract, 'combined': True, 'episode_tuples': [[ep('ep1'), []]], 'entity_types': ['Person', 'Company'],
         'edge_types': ['WORKS_AT'], 'excluded': ['Company'], 'instructions': 'Prefer full names.', 'strict': True,
         'replies': combined_replies},
    ]
    return cases


async def run_case(case: dict, mods) -> dict:
    (bu, EntityEdge, EntityNode, EpisodicNode, EpisodicEdge, GraphitiClients, GraphProvider, default_library, models,
     GraphOperationsInterface, SearchInterface) = mods
    from graphiti_core.llm_client.config import ModelSize
    replies = case.get('replies', []) + FALLBACK_REPLIES
    texts: list[str] = []
    vectors: list[tuple[list[float], str]] = []

    async def enter(kind, record, counted=True):
        state['in_flight'] += 1
        state['max_in_flight'] = max(state['max_in_flight'], state['in_flight'])
        try:
            await asyncio.sleep(0)
            events.append({'kind': kind, **record})
            if counted:
                state['count'] += 1
        finally:
            state['in_flight'] -= 1

    class FakeLlm:
        # The max_tokens of LLMClient with the default LLMConfig.
        max_tokens = 16384

        async def generate_response(self, messages, response_model=None, max_tokens=None, model_size=ModelSize.medium,
                                    group_id=None, prompt_name=None, attribute_extraction=False, model=None):
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
                    return json.loads(json.dumps(item['reply']))
            raise LookupError(f'no reply for {prompt_name}')

    def vector(text):
        if text in case.get('vectors', {}):
            value = list(case['vectors'][text])
        else:
            if text not in texts:
                texts.append(text)
            value = [float(texts.index(text)), 1.0]
        vectors.append((value, text))
        return value

    class FakeEmbedder:
        async def create(self, input_data):
            if case.get('slow_embed'):
                # A call that sleeps past the limit of the write and gives up when the limit cancels it.
                await enter('embed_start', {'input': list(input_data)})
                await asyncio.sleep(30)
            await enter('embed', {'input': list(input_data)})
            return vector(input_data[0])

        async def create_batch(self, input_data_list):
            await enter('embed_batch', {'input': list(input_data_list)})
            return [vector(t) for t in input_data_list]

    def text_of(search_vector):
        for value, text in reversed(vectors):
            if value == list(search_vector):
                return text
        return None

    def nodes_of(items):
        return [EntityNode(**decode(n)) for n in items]

    class FakeSearch(SearchInterface):
        async def node_similarity_search(self, driver, search_vector, search_filter, group_ids=None, limit=100,
                                         min_score=0.7):
            text = text_of(search_vector)
            await enter('node_similarity_search', {'query': text, 'group_ids': group_ids, 'limit': limit,
                                                   'min_score': min_score})
            return nodes_of(case.get('search', {}).get(text, []))

    class FakeOps(GraphOperationsInterface):
        async def retrieve_episodes(self, driver, reference_time, last_n=3, group_ids=None, source=None, saga=None):
            await enter('retrieve_episodes', {'reference_time': dt(reference_time), 'last_n': last_n,
                                              'group_ids': group_ids, 'source': source, 'saga': saga})
            found = []
            for group_id in group_ids or []:
                found += [EpisodicNode(**decode(e)) for e in case.get('stored', {}).get(group_id, [])]
            return found

        async def node_get_by_names(self, _cls, driver, names, group_ids):
            await enter('node_get_by_names', {'names': list(names), 'group_ids': list(group_ids)})
            return [n for n in nodes_of(case.get('exact', [])) if n.name in names]

        async def node_get_by_attribute_values(self, _cls, driver, group_id, attributes, node_labels=None, limit=100):
            await enter('node_get_by_attribute_values', {'group_id': group_id, 'attributes': encode(attributes),
                                                         'labels': node_labels, 'limit': limit})
            return nodes_of(case.get('identity', {}).get(node_labels[0], []))

        async def _bulk(self, kind, transaction, items, batch_size):
            await enter(kind, {'tx': transaction.name, 'records': encode(items), 'batch_size': batch_size})

        async def episodic_node_save_bulk(self, _cls, driver, transaction, nodes, batch_size=100):
            await self._bulk('episodic_node_save_bulk', transaction, nodes, batch_size)

        async def node_save_bulk(self, _cls, driver, transaction, nodes, batch_size=100):
            await self._bulk('node_save_bulk', transaction, nodes, batch_size)

        async def episodic_edge_save_bulk(self, _cls, driver, transaction, episodic_edges, batch_size=100):
            await self._bulk('episodic_edge_save_bulk', transaction, episodic_edges, batch_size)

        async def edge_save_bulk(self, _cls, driver, transaction, edges, batch_size=100):
            await self._bulk('edge_save_bulk', transaction, edges, batch_size)

        async def node_update_bulk(self, _cls, driver, transaction, nodes, batch_size=100):
            await self._bulk('node_update_bulk', transaction, nodes, batch_size)

        async def edge_update_bulk(self, _cls, driver, transaction, edges, batch_size=100):
            await self._bulk('edge_update_bulk', transaction, edges, batch_size)

    class FakeTx:
        name = 'tx'

        async def run(self, query, **kwargs):
            await enter('tx_run', {'query': query, 'keys': list(kwargs), 'values': encode(list(kwargs.values()))})

    class FakeSession:
        async def execute_write(self, func, *args, **kwargs):
            # `timeout` is the attribute that unit_of_work(timeout=...) puts on the work function (Neo4j branch).
            record = {'query_timeout_ms': getattr(self, 'query_timeout_ms', None),
                      'timeout': getattr(func, 'timeout', None)}
            if case.get('block_write'):
                # The call records at once and then waits for the limit to cancel it, so that a limit below one
                # millisecond cannot race the record.
                events.append({'kind': 'execute_write', **record})
                await asyncio.Event().wait()
            await enter('execute_write', record)
            try:
                return await func(FakeTx(), *args, **kwargs)
            except BaseException as error:
                events.append({'kind': 'rollback', 'error': type(error).__name__})
                raise

        async def close(self):
            await enter('close', {'query_timeout_ms': getattr(self, 'query_timeout_ms', None)}, counted=False)

    class TimeoutSession(FakeSession):
        query_timeout_ms = None

    class FakeDriver:
        provider = GraphProvider(case.get('provider', 'neo4j'))
        search_interface = FakeSearch()
        graph_operations_interface = FakeOps() if case.get('ops', True) else None

        def session(self):
            events.append({'kind': 'session'})
            return TimeoutSession() if case.get('timeout_session') else FakeSession()

        async def execute_query(self, cypher_query_, **kwargs):
            await enter('execute_query', {'query': cypher_query_, 'keys': list(kwargs)})
            return [], None, None

    class FakeCrossEncoder:
        async def rank(self, query, passages):
            raise AssertionError('rank is not used')

    from graphiti_core.tracer import NoOpTracer

    driver = FakeDriver()
    embedder = FakeEmbedder()
    clients = GraphitiClients.model_construct(
        driver=driver, llm_client=FakeLlm(), embedder=embedder, cross_encoder=FakeCrossEncoder(),
        tracer=NoOpTracer(), max_coroutines=case.get('max_coroutines'),
        prompt_library=default_library if case.get('routing') else None)
    fn = case['fn']
    type_map = {tuple(k): list(v) for k, v in case.get('type_map', [])}

    def types_of(names):
        return None if names is None else {n: models[n] for n in names}

    def tuples_of(items):
        return [(EpisodicNode(**decode(e)), [EpisodicNode(**decode(p)) for p in previous]) for e, previous in items]

    if fn == '_entity_node_record':
        n = EntityNode(**decode(case['node']))
        result = await bu._entity_node_record(n, embedder, driver)
        return {'record': encode(result), 'node': node_view(n)}
    if fn == '_entity_edge_record':
        e = EntityEdge(**decode(case['edge']))
        result = await bu._entity_edge_record(e, embedder, driver)
        return {'record': encode(result), 'edge': edge_view(e)}
    if fn == 'update_entity_edges_tx':
        edges = [EntityEdge(**decode(e)) for e in case['edges']]
        await bu.update_entity_edges_tx(FakeTx(), edges, embedder, driver)
        return {'edges': [edge_view(e) for e in edges]}
    if fn == 'update_entity_nodes_tx':
        nodes = nodes_of(case['nodes'])
        await bu.update_entity_nodes_tx(FakeTx(), nodes, embedder, driver)
        return {'nodes': [node_view(n) for n in nodes]}
    if fn in ('add_nodes_and_edges_bulk_tx', 'add_nodes_and_edges_bulk'):
        episodes = [EpisodicNode(**decode(e)) for e in case['episodes']]
        episodic_edges = [EpisodicEdge(**decode(e)) for e in case['episodic_edges']]
        nodes = nodes_of(case['nodes'])
        edges = [EntityEdge(**decode(e)) for e in case['edges']]
        if fn == 'add_nodes_and_edges_bulk_tx':
            await bu.add_nodes_and_edges_bulk_tx(FakeTx(), episodes, episodic_edges, nodes, edges, embedder, driver)
        else:
            if case.get('defaults'):
                await bu.add_nodes_and_edges_bulk(driver, episodes, episodic_edges, nodes, edges, embedder)
            else:
                await bu.add_nodes_and_edges_bulk(driver, episodes, episodic_edges, nodes, edges, embedder,
                                                  write_timeout=case['write_timeout'])
        return {'nodes': [node_view(n) for n in nodes], 'edges': [edge_view(e) for e in edges]}
    if fn == 'execute_write_with_timeout':
        async def work(tx, value):
            await tx.run('RETURN $value', value=value)
            if case['raises']:
                raise RuntimeError('work failed')
            return value

        # The port has no result here: the work function returns no value (R9).
        await bu.execute_write_with_timeout(driver, case['timeout'], work, case['value'])
        return None
    if fn == 'retrieve_previous_episodes_bulk':
        episodes = [EpisodicNode(**decode(e)) for e in case['episodes']]
        if case.get('defaults'):
            result = await bu.retrieve_previous_episodes_bulk(driver, episodes)
        else:
            result = await bu.retrieve_previous_episodes_bulk(driver, episodes, max_coroutines=case['max_coroutines'])
        return {'tuples': [[e.uuid, [p.uuid for p in previous]] for e, previous in result]}
    if fn in ('extract_nodes_and_edges_bulk', 'extract_nodes_and_edges_bulk_combined',
              'extract_nodes_and_edges_bulk_separate'):
        if case.get('defaults'):
            target = {'extract_nodes_and_edges_bulk': bu.extract_nodes_and_edges_bulk,
                      'extract_nodes_and_edges_bulk_combined': bu._extract_nodes_and_edges_bulk_combined,
                      'extract_nodes_and_edges_bulk_separate': bu._extract_nodes_and_edges_bulk_separate}[fn]
            nodes_bulk, edges_bulk = await target(clients, tuples_of(case['episode_tuples']), type_map)
        else:
            nodes_bulk, edges_bulk = await bu.extract_nodes_and_edges_bulk(
                clients, tuples_of(case['episode_tuples']), type_map, entity_types=types_of(case['entity_types']),
                excluded_entity_types=case['excluded'], edge_types=types_of(case['edge_types']),
                custom_extraction_instructions=case['instructions'], use_combined_extraction=case['combined'],
                strict_edge_types=case['strict'])
        return {'nodes': [[node_view(n) for n in nodes] for nodes in nodes_bulk],
                'edges': [[edge_view(e) for e in edges] for edges in edges_bulk]}
    if fn == 'dedupe_nodes_bulk':
        extracted = [nodes_of(items) for items in case['nodes']]
        episode_tuples = [(EpisodicNode(**decode(e)), []) for e in case['episodes']]
        if case.get('defaults'):
            by_episode, uuid_map = await bu.dedupe_nodes_bulk(clients, extracted, episode_tuples)
        else:
            by_episode, uuid_map = await bu.dedupe_nodes_bulk(
                clients, extracted, episode_tuples, entity_types=types_of(case['entity_types']),
                identity_properties=case['identity_properties'], trace=None)
        return {'by_episode': [[k, [node_view(n) for n in v]] for k, v in by_episode.items()],
                'uuid_map': [[k, v] for k, v in uuid_map.items()],
                'extracted': [[node_view(n) for n in items] for items in extracted]}
    if fn == 'dedupe_edges_bulk':
        extracted = [[EntityEdge(**decode(e)) for e in items] for items in case['edges']]
        episode_tuples = [(EpisodicNode(**decode(e)), []) for e in case['episodes']]
        by_episode = await bu.dedupe_edges_bulk(clients, extracted, episode_tuples, [], types_of(case['types']),
                                                type_map)
        return {'by_episode': [[k, [edge_view(e) for e in v]] for k, v in by_episode.items()],
                'extracted': [[edge_view(e) for e in items] for items in extracted]}
    raise AssertionError(fn)


def record_async(mods) -> list[dict]:
    out = []
    for case in (write_cases() + retrieve_cases() + extract_cases() + dedupe_node_cases() + dedupe_edge_cases()
                 + timeout_cases() + review_cases()):
        events.clear()
        state.update(in_flight=0, max_in_flight=0, count=0)
        try:
            output = {'ok': asyncio.run(run_case(case, mods))}
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            text = str(error)
            if type(error).__name__ == 'ValidationError':
                text = text.splitlines()[0]
            output = {'error': {'type': type(error).__name__, 'text': text}}
        output['events'] = list(events)
        output['max_in_flight'] = state['max_in_flight']
        output['count'] = state['count']
        out.append({'in': case, 'out': placeholders(output, {})})
    return out


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    os.environ.pop('GRAPHITI_ATTRIBUTE_MAX_LENGTH', None)
    # dedupe_nodes_bulk builds its first coroutine and then raises IndexError for the second one (the case with more node
    # lists than episode tuples), so the first coroutine is never awaited. Python warns of it, as it does for upstream.
    warnings.filterwarnings('ignore', message=r"coroutine '.*' was never awaited", category=RuntimeWarning)
    from pydantic import BaseModel, Field, create_model

    import graphiti_core
    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.driver.graph_operations.graph_operations import GraphOperationsInterface
    from graphiti_core.driver.search_interface.search_interface import SearchInterface
    from graphiti_core.edges import EntityEdge, EpisodicEdge
    from graphiti_core.graphiti_types import GraphitiClients
    from graphiti_core.nodes import EntityNode, EpisodicNode
    from graphiti_core.prompts.lib import default_chat_prompt_library
    from graphiti_core.utils import bulk_utils as bu
    from graphiti_core.utils.maintenance import attribute_utils, dedup_helpers, edge_operations, node_operations

    class Capture(logging.Handler):
        def emit(self, record):
            events.append({'kind': 'log', 'level': record.levelname, 'message': record.getMessage()})

    root = logging.getLogger('graphiti_core')
    root.setLevel(logging.DEBUG)
    root.propagate = False
    root.addHandler(Capture(logging.DEBUG))

    # The fixed clock: each module of graphiti_core that imported utc_now.
    for name, module in list(sys.modules.items()):
        if name.startswith('graphiti_core') and hasattr(module, 'utc_now'):
            module.utc_now = lambda: FIXED_NOW
    del graphiti_core

    # The set orders of the port (RULES.md R14), as the recorders of packets P7a, P7b, and P7c bind them.
    bu.set = InsertionOrderSet
    dedup_helpers.set = InsertionOrderSet
    attribute_utils.set = InsertionOrderSet
    edge_operations.set = InsertionOrderSet

    class OrderedLabelsEntityNode(EntityNode):
        def __init__(self, **data):
            labels = data.get('labels')
            if labels is not None and 'Entity' in labels:
                data['labels'] = ['Entity'] + [label for label in labels if label != 'Entity']
            super().__init__(**data)

    node_operations.EntityNode = OrderedLabelsEntityNode

    models = {}
    for name, spec in TYPE_DEFS.items():
        fields = {}
        for field_name, required, max_length in spec['fields']:
            kwargs = {} if max_length is None else {'max_length': max_length}
            fields[field_name] = (str, Field(..., **kwargs)) if required else (str | None, Field(None, **kwargs))
        models[name] = create_model(name, __base__=BaseModel, __doc__=spec['doc'], **fields)

    constants = {'CHUNK_SIZE': bu.CHUNK_SIZE, 'INGESTION_INTERFACE_OPERATIONS': list(bu.INGESTION_INTERFACE_OPERATIONS)}
    record_sync(bu, EntityEdge, EpisodicEdge, GraphOperationsInterface)
    async_cases = record_async((bu, EntityEdge, EntityNode, EpisodicNode, EpisodicEdge, GraphitiClients, GraphProvider,
                                default_chat_prompt_library, models, GraphOperationsInterface, SearchInterface))

    dump = lambda r: json.dumps(r, ensure_ascii=True, allow_nan=False, separators=(',', ':'))  # noqa: E731
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","fixed_now":"' + FIXED_NOW.isoformat() + '",'
        + '"constants":' + dump(constants) + ',"types":' + dump(TYPE_DEFS) + ',"cases":[\n'
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
