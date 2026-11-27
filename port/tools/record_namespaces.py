"""Records what NodeNamespace and EdgeNamespace do, from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python port/tools/record_namespaces.py
    .upstream/graphiti/.venv/bin/python port/tools/record_namespaces.py --check

It runs graphiti_core/namespaces/nodes.py and edges.py with a fake driver. The fake driver is the
network boundary: it implements execute_query and records each query. Its operations are the real
Neo4j operations classes. The tool records two things:

- the construction of NodeNamespace and EdgeNamespace for each set of operations that a driver can
  give (each of the 16 sets of node operations and each of the 32 sets of edge operations): which
  sub-namespaces exist, and the error that a missing one raises
- the calls of each public method of the nine namespace classes: EntityNodeNamespace, EpisodeNodeNamespace,
  CommunityNodeNamespace, SagaNodeNamespace, EntityEdgeNamespace, EpisodicEdgeNamespace, CommunityEdgeNamespace,
  HasEpisodeEdgeNamespace, and NextEpisodeEdgeNamespace. This includes save, which calls the embedder for the
  entity node, the community node, and the entity edge. Each call records the queries that reach the executor
  and the transaction, the arguments that the namespace gives to the operations class (the transaction,
  batch_size, limit, uuid_cursor, and the others), the result or the error with its text, and the texts that
  reached the embedder. The operations class is the real Neo4j class behind a wrapper that notes each call and
  then delegates. The Neo4j class ignores batch_size in load_embeddings_bulk, so only the wrapper shows it.
  The record parsers run on the records that a case gives the fake executor.

Each method runs with each argument at its default (an omitted argument), with each optional argument given,
and with and without a transaction where the method has a tx parameter. The fake embedder is a pure function of
its text. The cases of an entity node that were recorded first carry no "ns" key, and the other cases carry one.
Every datetime of an input is fixed, so the recording is the same in each run.

The labels of a node stay empty or the one label Entity. Python builds the labels of the save query from set(), and
the order of a set of 2 texts changes with PYTHONHASHSEED, so a node with 2 labels has no stable recording.

Each path of the namespaces is recorded. No path stops at a skeleton member.

It writes tests/Port.Evaluation/values/namespaces.json. --check writes nothing and exits 1 if the
file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream in
.upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import datetime as dt
import inspect
import itertools
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM', ROOT / '.upstream' / 'graphiti'))
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'namespaces.json'

NODE_NAMES = ['entity', 'episode', 'community', 'saga']
EDGE_NAMES = ['entity', 'episodic', 'community', 'has_episode', 'next_episode']


def subsets(names):
    """Each subset of the names, in a fixed order: by size, then by position."""
    return [list(combo) for size in range(len(names) + 1) for combo in itertools.combinations(names, size)]


NODE_KINDS = {'+'.join(present) or 'none': present for present in subsets(NODE_NAMES)}
EDGE_KINDS = {'+'.join(present) or 'none': present for present in subsets(EDGE_NAMES)}
DRIVER_NAMES = ['FakeDriver', 'Zoë東京Driver']

UUIDS = ['u-1', 'Zoë 東京 😀']
GROUP_IDS = ['g-1', 'grüp 東京']
UUID_LISTS = [[], ['a'], ['a', 'b'], ['Zoë', '東京 😀']]
BATCH_SIZES = [100, 0]
EMBEDDINGS = [None, [0.1, 0.25], [1, 2], ['x']]
GROUP_ID_LISTS = [['g'], ['g-1', 'grüp 東京']]
LIMITS_AND_CURSORS = [(None, None), (5, None), (None, 'c-1'), (5, 'c-1'), (0, ''), (None, '')]


def encode(value):
    """A value with its Python type."""
    if value is None:
        return {'type': 'none'}
    if isinstance(value, bool):
        return {'type': 'bool', 'value': value}
    if isinstance(value, int):
        return {'type': 'int', 'value': value}
    if isinstance(value, float):
        return {'type': 'float', 'value': value}
    if isinstance(value, dt.datetime):
        return {'type': 'datetime', 'value': value.isoformat()}
    if isinstance(value, str):
        return {'type': 'str', 'value': value}
    if isinstance(value, (list, tuple, set)):
        return {'type': 'list', 'items': [encode(item) for item in value]}
    if isinstance(value, dict):
        return {'type': 'dict', 'items': [[key, encode(item)] for key, item in value.items()]}
    return {'type': type(value).__name__}


def call_cases():
    out = []
    for uuid in UUIDS:
        for use_tx in (False, True):
            out.append({'method': 'delete', 'use_tx': use_tx, 'uuid': uuid})
    for group_id in GROUP_IDS:
        for batch_size in BATCH_SIZES:
            for use_tx in (False, True):
                out.append({'method': 'delete_by_group_id', 'use_tx': use_tx, 'group_id': group_id, 'batch_size': batch_size})
    for uuids in UUID_LISTS:
        for batch_size in BATCH_SIZES:
            for use_tx in (False, True):
                out.append({'method': 'delete_by_uuids', 'use_tx': use_tx, 'uuids': uuids, 'batch_size': batch_size})
    for uuid in UUIDS:
        for embedding in EMBEDDINGS:
            out.append({'method': 'load_embeddings', 'uuid': uuid, 'records': [{'name_embedding': embedding}]})
    nodes = ['n-1', 'Zoë 東京']
    for batch_size in (None, 7, 0):
        extra = {} if batch_size is None else {'batch_size': batch_size}
        out.append({'method': 'load_embeddings_bulk', 'uuids': [], 'records': [], **extra})
        out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'Zoë 東京', 'name_embedding': [2, 3]}], **extra})
        out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'n-1', 'name_embedding': [1.5]}, {'uuid': 'other', 'name_embedding': [9]}], **extra})
    for uuids in UUID_LISTS:
        for batch_size in (None, 7):
            for use_tx in (False, True):
                extra = {} if batch_size is None else {'batch_size': batch_size}
                out.append({'method': 'save_bulk', 'use_tx': use_tx, 'uuids': uuids, **extra})
    for uuid in UUIDS:
        out.append({'method': 'get_by_uuid', 'uuid': uuid})
    for uuids in UUID_LISTS:
        out.append({'method': 'get_by_uuids', 'uuids': uuids})
    for group_ids in GROUP_ID_LISTS:
        for limit, cursor in LIMITS_AND_CURSORS:
            out.append({'method': 'get_by_group_ids', 'group_ids': group_ids, 'limit': limit, 'uuid_cursor': cursor})
        out.append({'method': 'get_by_group_ids', 'group_ids': group_ids})
    return out


T1 = '2024-01-02T03:04:05+00:00'
T2 = '2024-02-03T04:05:06.123456+00:00'
T3 = '2024-03-04T05:06:07+00:00'

NODE_COMMON = ['save', 'save_bulk', 'delete', 'delete_by_group_id', 'delete_by_uuids', 'get_by_uuid', 'get_by_uuids', 'get_by_group_ids']
EDGE_COMMON = ['save', 'save_bulk', 'delete', 'delete_by_uuids', 'get_by_uuid', 'get_by_uuids', 'get_by_group_ids']

# namespace key: (group, attribute, kind, methods)
NAMESPACES = {
    'node.entity': ('node', 'entity', 'entity_node', NODE_COMMON + ['load_embeddings', 'load_embeddings_bulk']),
    'node.episode': ('node', 'episode', 'episode_node', NODE_COMMON + ['get_by_entity_node_uuid', 'retrieve_episodes']),
    'node.community': ('node', 'community', 'community_node', NODE_COMMON + ['load_name_embedding']),
    'node.saga': ('node', 'saga', 'saga_node', NODE_COMMON),
    'edge.entity': ('edge', 'entity', 'entity_edge', EDGE_COMMON + ['get_between_nodes', 'get_by_node_uuid', 'load_embeddings', 'load_embeddings_bulk']),
    'edge.episodic': ('edge', 'episodic', 'episodic_edge', EDGE_COMMON),
    'edge.community': ('edge', 'community', 'community_edge', [m for m in EDGE_COMMON if m != 'save_bulk']),
    'edge.has_episode': ('edge', 'has_episode', 'has_episode_edge', EDGE_COMMON),
    'edge.next_episode': ('edge', 'next_episode', 'next_episode_edge', EDGE_COMMON),
}

# method: (required arguments, optional arguments). A required argument is the key of the case with the same name.
SIGNATURES = {
    'save': (['item'], ['tx']),
    'save_bulk': (['items'], ['tx', 'batch_size']),
    'delete': (['item'], ['tx']),
    'delete_by_group_id': (['group_id'], ['tx', 'batch_size']),
    'delete_by_uuids': (['uuids'], ['tx', 'batch_size']),
    'get_by_uuid': (['uuid'], []),
    'get_by_uuids': (['uuids'], []),
    'get_by_group_ids': (['group_ids'], ['limit', 'uuid_cursor']),
    'load_embeddings': (['item'], []),
    'load_embeddings_bulk': (['items'], ['batch_size']),
    'load_name_embedding': (['item'], []),
    'get_by_entity_node_uuid': (['entity_node_uuid'], []),
    'retrieve_episodes': (['reference_time'], ['last_n', 'group_ids', 'source', 'saga']),
    'get_between_nodes': (['source_node_uuid', 'target_node_uuid'], []),
    'get_by_node_uuid': (['node_uuid'], []),
}


def signature(group, method):
    if method == 'delete_by_uuids' and group == 'edge':
        return (['uuids'], ['tx'])
    return SIGNATURES[method]


DEFAULTS = {
    'entity_node': {'name': 'n', 'group_id': 'g'},
    'episode_node': {'name': 'n', 'group_id': 'g', 'source': 'text', 'source_description': 'sd', 'content': 'c', 'valid_at': T1},
    'community_node': {'name': 'n', 'group_id': 'g'},
    'saga_node': {'name': 'n', 'group_id': 'g'},
    'entity_edge': {'group_id': 'g', 'source_node_uuid': 's', 'target_node_uuid': 't', 'name': 'RELATES_TO', 'fact': 'a fact'},
    'episodic_edge': {'group_id': 'g', 'source_node_uuid': 's', 'target_node_uuid': 't'},
    'community_edge': {'group_id': 'g', 'source_node_uuid': 's', 'target_node_uuid': 't'},
    'has_episode_edge': {'group_id': 'g', 'source_node_uuid': 's', 'target_node_uuid': 't'},
    'next_episode_edge': {'group_id': 'g', 'source_node_uuid': 's', 'target_node_uuid': 't'},
}
DATE_KEYS = ('created_at', 'valid_at', 'expired_at', 'invalid_at')

EDGE_RICH = {'uuid': 'ed-1', 'group_id': 'g-1', 'source_node_uuid': 's-1', 'target_node_uuid': 'Zoë 東京', 'created_at': T2}
SPECS = {
    'entity_node': {
        'plain': {'uuid': 'i-1'},
        'rich': {
            'uuid': 'Zoë 東京',
            'name': 'line\nbreak Zoë',
            'group_id': 'g-1',
            'summary': 'sum 😀',
            'labels': ['Entity'],
            'attributes': {'age': 3, 'tags': ['a', 'b'], 'nested': {'k': None}},
            'name_embedding': [0.5, 1],
            'created_at': T2,
        },
    },
    'episode_node': {
        'plain': {'uuid': 'i-1'},
        'rich': {
            'uuid': 'e-1',
            'name': 'ep',
            'group_id': 'g-1',
            'source': 'json',
            'source_description': 'sd Zoë',
            'content': '{"a": 1}',
            'valid_at': T2,
            'created_at': T1,
            'entity_edges': ['x', 'y'],
        },
    },
    'community_node': {
        'plain': {'uuid': 'i-1'},
        'rich': {'uuid': 'c-1', 'name': 'com\nmunity', 'group_id': 'g-1', 'summary': 's', 'name_embedding': [2], 'created_at': T2},
    },
    'saga_node': {'plain': {'uuid': 'i-1'}, 'rich': {'uuid': 'sa-1', 'name': 'saga 東京', 'group_id': 'g-1', 'summary': 'x', 'created_at': T2}},
    'entity_edge': {
        'plain': {'uuid': 'i-1'},
        'rich': {
            'uuid': 'ee-1',
            'source_node_uuid': 's-1',
            'target_node_uuid': 't-1',
            'name': 'LIKES',
            'fact': 'a\nfact Zoë',
            'fact_embedding': [1.5],
            'group_id': 'g-1',
            'episodes': ['e1', 'e2'],
            'created_at': T1,
            'expired_at': T2,
            'valid_at': T1,
            'invalid_at': T2,
            'attributes': {'w': 0.5, 'n': 2},
        },
    },
    'episodic_edge': {'plain': {'uuid': 'i-1'}, 'rich': EDGE_RICH},
    'community_edge': {'plain': {'uuid': 'i-1'}, 'rich': EDGE_RICH},
    'has_episode_edge': {'plain': {'uuid': 'i-1'}, 'rich': EDGE_RICH},
    'next_episode_edge': {'plain': {'uuid': 'i-1'}, 'rich': EDGE_RICH},
}


def record_for(kind, uuid, **over):
    """A record as the Neo4j driver returns it for the return query of the kind."""
    if kind == 'entity_node':
        base = {
            'uuid': uuid,
            'name': 'n ' + uuid,
            'group_id': 'g-1',
            'name_embedding': [0.5, 2],
            'labels': ['Entity', 'Entity_g1', 'Person'],
            'created_at': T1,
            'summary': 'sum',
            'attributes': {'uuid': 'x', 'name': 'dup', 'age': 3, 'nested': {'a': [1, 2.5]}},
        }
    elif kind == 'episode_node':
        base = {
            'uuid': uuid,
            'name': 'ep',
            'group_id': 'g-1',
            'source': 'message',
            'source_description': 'sd',
            'content': 'hello Zoë',
            'created_at': T1,
            'valid_at': T2,
            'entity_edges': ['x', 'y'],
        }
    elif kind == 'community_node':
        base = {'uuid': uuid, 'name': 'com', 'group_id': 'g-1', 'name_embedding': [1.5], 'created_at': T1, 'summary': 's'}
    elif kind == 'saga_node':
        base = {'uuid': uuid, 'name': 'saga', 'group_id': 'g-1', 'created_at': T2}
    elif kind == 'entity_edge':
        base = {
            'uuid': uuid,
            'source_node_uuid': 's-1',
            'target_node_uuid': 't-1',
            'fact': 'a fact',
            'fact_embedding': [0.25],
            'name': 'LIKES',
            'group_id': 'g-1',
            'episodes': ['e1'],
            'created_at': T1,
            'expired_at': None,
            'valid_at': T2,
            'invalid_at': None,
            'reference_time': T3,
            'attributes': {'uuid': 'x', 'fact': 'dup', 'w': 0.5, 'tags': ['a']},
        }
    else:
        base = {'uuid': uuid, 'group_id': 'g-1', 'source_node_uuid': 's-1', 'target_node_uuid': 't-1', 'created_at': T2}
    base.update(over)
    return base


def broken_records(kind, uuid):
    """Records that make the parser raise, with the key that each one lacks or spoils."""
    out = []
    lacking = 'name' if kind.endswith('_node') else 'group_id'
    record = record_for(kind, uuid)
    del record[lacking]
    out.append([record])
    if kind == 'episode_node':
        out.append([record_for(kind, uuid, created_at=None)])
        out.append([record_for(kind, uuid, valid_at=None)])
        out.append([record_for(kind, uuid, source='bogus')])
    return out


TX_MODES = {'omit': {'omit_tx': True}, 'none': {'use_tx': False}, 'tx': {'use_tx': True}}


def new_cases():
    out = []
    for ns, (group, _attr, kind, methods) in NAMESPACES.items():
        plain = SPECS[kind]['plain']
        rich = SPECS[kind]['rich']
        embedding_key = 'fact_embedding' if kind == 'entity_edge' else 'name_embedding'

        def case(method, **fields):
            out.append({'ns': ns, 'method': method, **fields})

        for method in methods:
            if method == 'save':
                for tx in ('omit', 'none', 'tx'):
                    case('save', items=[plain], **TX_MODES[tx])
                for tx in ('none', 'tx'):
                    case('save', items=[rich], **TX_MODES[tx])
            elif method == 'save_bulk':
                for items in ([], [plain, rich]):
                    for batch in (None, 7):
                        for tx in ('none', 'tx'):
                            extra = {} if batch is None else {'batch_size': batch}
                            case('save_bulk', items=items, **extra, **TX_MODES[tx])
                case('save_bulk', items=[plain], **TX_MODES['omit'])
            elif method == 'delete':
                for tx in ('omit', 'none', 'tx'):
                    case('delete', items=[plain], **TX_MODES[tx])
                case('delete', items=[rich], **TX_MODES['tx'])
            elif method == 'delete_by_group_id':
                for group_id, batch, tx in [('g-1', None, 'omit'), ('g-1', 7, 'omit'), ('g-1', None, 'tx'), ('grüp 東京', 0, 'tx'), ('grüp 東京', None, 'none')]:
                    extra = {} if batch is None else {'batch_size': batch}
                    case('delete_by_group_id', group_id=group_id, **extra, **TX_MODES[tx])
            elif method == 'delete_by_uuids':
                combos = [(None, 'omit'), (7, 'tx'), (None, 'none')] if group == 'node' else [(None, 'omit'), (None, 'tx'), (None, 'none')]
                for uuids in ([], ['a', 'b'], ['Zoë', '東京 😀']):
                    for batch, tx in combos:
                        extra = {} if batch is None else {'batch_size': batch}
                        case('delete_by_uuids', uuids=uuids, **extra, **TX_MODES[tx])
            elif method == 'get_by_uuid':
                case('get_by_uuid', uuid='u-1', records=[])
                case('get_by_uuid', uuid='u-1', records=[record_for(kind, 'u-1')])
                case('get_by_uuid', uuid='Zoë 東京', records=[record_for(kind, 'Zoë 東京'), record_for(kind, 'later')])
                for records in broken_records(kind, 'u-1'):
                    case('get_by_uuid', uuid='u-1', records=records)
            elif method == 'get_by_uuids':
                case('get_by_uuids', uuids=[], records=[])
                case('get_by_uuids', uuids=['a'], records=[])
                case('get_by_uuids', uuids=['a', 'b'], records=[record_for(kind, 'a'), record_for(kind, 'b')])
                case('get_by_uuids', uuids=['Zoë'], records=[record_for(kind, 'Zoë')])
                case('get_by_uuids', uuids=['a'], records=broken_records(kind, 'a')[0])
            elif method == 'get_by_group_ids':
                for extra in ({}, {'limit': 5}, {'uuid_cursor': 'c-1'}, {'limit': 5, 'uuid_cursor': 'c-1'}, {'limit': 0, 'uuid_cursor': ''}, {'limit': None, 'uuid_cursor': None}):
                    case('get_by_group_ids', group_ids=['g'], records=[], **extra)
                case('get_by_group_ids', group_ids=['g-1', 'grüp 東京'], records=[record_for(kind, 'r-1'), record_for(kind, 'r-2')], limit=2, uuid_cursor='r-9')
                case('get_by_group_ids', group_ids=[], records=[])
            elif method in ('load_embeddings', 'load_name_embedding'):
                # EntityEdge has no validate_assignment in Python, so it keeps an int or a text that a record gives. The C# list holds doubles.
                embeddings = [None, [0.1, 0.25], [1.5, 2.5]] if kind == 'entity_edge' else EMBEDDINGS
                for uuid in UUIDS:
                    for embedding in embeddings:
                        case(method, items=[{'uuid': uuid}], records=[{embedding_key: embedding}])
                case(method, items=[{'uuid': 'missing'}], records=[])
            elif method == 'load_embeddings_bulk':
                items = [{'uuid': 'n-1'}, {'uuid': 'Zoë 東京'}]
                wide = [2.5, 3.5] if kind == 'entity_edge' else [2, 3]
                for batch in (None, 7):
                    extra = {} if batch is None else {'batch_size': batch}
                    case(method, items=[], records=[], **extra)
                    case(method, items=items, records=[{'uuid': 'Zoë 東京', embedding_key: wide}], **extra)
                    case(method, items=items, records=[{'uuid': 'n-1', embedding_key: [1.5]}, {'uuid': 'other', embedding_key: [9]}], **extra)
            elif method == 'get_by_entity_node_uuid':
                case(method, entity_node_uuid='en-1', records=[])
                case(method, entity_node_uuid='Zoë 東京', records=[record_for(kind, 'e-1'), record_for(kind, 'e-2')])
            elif method == 'retrieve_episodes':
                base = {'reference_time': T3}
                forms = [
                    {},
                    {'last_n': 5},
                    {'group_ids': ['g']},
                    {'group_ids': ['g-1', 'g-2'], 'source': 'message'},
                    {'group_ids': [], 'source': ''},
                    {'source': 'json'},
                    {'saga': 'sa'},
                    {'saga': 'sa', 'group_ids': []},
                    {'saga': 'sa', 'group_ids': ['g-1', 'g-2']},
                    {'saga': 'sa', 'group_ids': ['g-1'], 'source': 'text', 'last_n': 2},
                    {'saga': '', 'group_ids': ['g-1'], 'source': '', 'last_n': 0},
                ]
                for form in forms:
                    case(method, **base, **form, records=[])
                case(method, **base, group_ids=['g'], records=[record_for(kind, 'e-1')])
                case(method, **base, saga='sa', group_ids=['g'], records=[record_for(kind, 'e-1'), record_for(kind, 'e-2')])
            elif method == 'get_between_nodes':
                case(method, source_node_uuid='s-1', target_node_uuid='t-1', records=[])
                case(method, source_node_uuid='Zoë', target_node_uuid='東京', records=[record_for(kind, 'r-1')])
            elif method == 'get_by_node_uuid':
                case(method, node_uuid='n-1', records=[])
                case(method, node_uuid='Zoë 東京', records=[record_for(kind, 'r-1'), record_for(kind, 'r-2')])
            else:
                raise ValueError(method)
    return out


def item_specs(case):
    """The specs of the models that a case gives to the call, as the first cases of an entity node gave them."""
    if 'items' in case:
        return case['items']
    method = case['method']
    if method in ('save_bulk', 'load_embeddings_bulk'):
        return [{'uuid': uuid} for uuid in case.get('uuids', [])]
    if method in ('delete', 'load_embeddings', 'save') and 'uuid' in case:
        return [{'uuid': case['uuid']}]
    return []


MODEL_FIELDS = {
    'EntityNode': ['uuid', 'name', 'group_id', 'labels', 'created_at', 'name_embedding', 'summary', 'attributes'],
    'EpisodicNode': ['uuid', 'name', 'group_id', 'labels', 'created_at', 'source', 'source_description', 'content', 'valid_at', 'entity_edges'],
    'CommunityNode': ['uuid', 'name', 'group_id', 'labels', 'created_at', 'name_embedding', 'summary'],
    'SagaNode': ['uuid', 'name', 'group_id', 'labels', 'created_at', 'summary'],
    'EntityEdge': [
        'uuid', 'group_id', 'source_node_uuid', 'target_node_uuid', 'created_at', 'name', 'fact', 'fact_embedding', 'episodes',
        'expired_at', 'valid_at', 'invalid_at', 'reference_time', 'attributes',
    ],
    'EpisodicEdge': ['uuid', 'group_id', 'source_node_uuid', 'target_node_uuid', 'created_at'],
    'CommunityEdge': ['uuid', 'group_id', 'source_node_uuid', 'target_node_uuid', 'created_at'],
    'HasEpisodeEdge': ['uuid', 'group_id', 'source_node_uuid', 'target_node_uuid', 'created_at'],
    'NextEpisodeEdge': ['uuid', 'group_id', 'source_node_uuid', 'target_node_uuid', 'created_at'],
}


def encode_result(value):
    """What a method returns: a model as its fields in a fixed order, a list of models, or a plain value."""
    if isinstance(value, list):
        return {'type': 'list', 'items': [encode_result(item) for item in value]}
    name = type(value).__name__
    if name in MODEL_FIELDS:
        fields = []
        for field in MODEL_FIELDS[name]:
            item = getattr(value, field)
            if field == 'source':
                item = item.value
            fields.append([field, encode(item)])
        return {'type': name, 'fields': fields}
    return encode(value)


def main() -> int:
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core.driver.driver import GraphDriver
    from graphiti_core.driver.neo4j.operations.community_edge_ops import Neo4jCommunityEdgeOperations
    from graphiti_core.driver.neo4j.operations.community_node_ops import Neo4jCommunityNodeOperations
    from graphiti_core.driver.neo4j.operations.entity_edge_ops import Neo4jEntityEdgeOperations
    from graphiti_core.driver.neo4j.operations.entity_node_ops import Neo4jEntityNodeOperations
    from graphiti_core.driver.neo4j.operations.episode_node_ops import Neo4jEpisodeNodeOperations
    from graphiti_core.driver.neo4j.operations.episodic_edge_ops import Neo4jEpisodicEdgeOperations
    from graphiti_core.driver.neo4j.operations.has_episode_edge_ops import Neo4jHasEpisodeEdgeOperations
    from graphiti_core.driver.neo4j.operations.next_episode_edge_ops import Neo4jNextEpisodeEdgeOperations
    from graphiti_core.driver.neo4j.operations.saga_node_ops import Neo4jSagaNodeOperations
    from graphiti_core.driver.query_executor import Transaction
    from graphiti_core.embedder import EmbedderClient
    from graphiti_core.namespaces import EdgeNamespace, NodeNamespace
    from graphiti_core.edges import Edge, EntityEdge, EpisodicEdge, CommunityEdge, HasEpisodeEdge, NextEpisodeEdge
    from graphiti_core.nodes import CommunityNode, EntityNode, EpisodeType, EpisodicNode, Node, SagaNode

    node_ops = {
        'entity': Neo4jEntityNodeOperations,
        'episode': Neo4jEpisodeNodeOperations,
        'community': Neo4jCommunityNodeOperations,
        'saga': Neo4jSagaNodeOperations,
    }
    edge_ops = {
        'entity': Neo4jEntityEdgeOperations,
        'episodic': Neo4jEpisodicEdgeOperations,
        'community': Neo4jCommunityEdgeOperations,
        'has_episode': Neo4jHasEpisodeEdgeOperations,
        'next_episode': Neo4jNextEpisodeEdgeOperations,
    }

    def encode_arg(value):
        """An argument that the namespace gives to an operations class."""
        if isinstance(value, Transaction):
            return {'type': 'transaction'}
        if isinstance(value, (Node, Edge)):
            return {'type': type(value).__name__, 'uuid': value.uuid}
        if isinstance(value, list):
            return {'type': 'list', 'items': [encode_arg(item) for item in value]}
        return encode(value)

    class RecordingOperations:
        """The real operations class behind a wrapper that notes each call, and then delegates."""

        def __init__(self, inner, seen):
            self.inner = inner
            self.seen = seen

        def __getattr__(self, name):
            method = getattr(self.inner, name)
            signature = inspect.signature(method)

            async def call(*args, **kwargs):
                await asyncio.sleep(0)
                bound = signature.bind(*args, **kwargs)
                bound.apply_defaults()
                arguments = list(bound.arguments.items())[1:]  # the first one is the executor
                self.seen.append({'method': name, 'args': [[key, encode_arg(value)] for key, value in arguments]})
                return await method(*args, **kwargs)

            return call

    class FakeEmbedder(EmbedderClient):
        """A pure function of its text: the length in code points, and the sum of the code points modulo 997."""

        def __init__(self):
            self.calls = []

        async def create(self, input_data):
            await asyncio.sleep(0)
            self.calls.append(encode(input_data))
            text = input_data[0]
            return [float(len(text)), float(sum(ord(c) for c in text) % 997)]

    class Recorder:
        def __init__(self, records):
            self.calls = []
            self.records = records

        def note(self, via, query, kwargs):
            self.calls.append(
                {
                    'via': via,
                    'query': query,
                    'keys': list(kwargs),
                    'values': [encode(value) for value in kwargs.values()],
                }
            )

    class FakeTransaction(Recorder, Transaction):
        async def run(self, query, **kwargs):
            await asyncio.sleep(0)
            self.note('transaction', query, kwargs)

    def make_driver(name, node_kinds, edge_kinds, records):
        class Driver(Recorder, GraphDriver):
            def __init__(self):
                Recorder.__init__(self, records)
                self.ops_seen = []

            async def execute_query(self, cypher_query_, **kwargs):
                await asyncio.sleep(0)
                self.note('executor', cypher_query_, kwargs)
                return self.records, None, None

            def session(self, database=None):
                raise NotImplementedError

            def close(self):
                raise NotImplementedError

            async def delete_all_indexes(self):
                raise NotImplementedError

            async def build_indices_and_constraints(self, delete_existing=False):
                raise NotImplementedError

            def wrapped(self, ops_class, present):
                return RecordingOperations(ops_class(), self.ops_seen) if present else None

            @property
            def entity_node_ops(self):
                return self.wrapped(node_ops['entity'], 'entity' in node_kinds)

            @property
            def episode_node_ops(self):
                return self.wrapped(node_ops['episode'], 'episode' in node_kinds)

            @property
            def community_node_ops(self):
                return self.wrapped(node_ops['community'], 'community' in node_kinds)

            @property
            def saga_node_ops(self):
                return self.wrapped(node_ops['saga'], 'saga' in node_kinds)

            @property
            def entity_edge_ops(self):
                return self.wrapped(edge_ops['entity'], 'entity' in edge_kinds)

            @property
            def episodic_edge_ops(self):
                return self.wrapped(edge_ops['episodic'], 'episodic' in edge_kinds)

            @property
            def community_edge_ops(self):
                return self.wrapped(edge_ops['community'], 'community' in edge_kinds)

            @property
            def has_episode_edge_ops(self):
                return self.wrapped(edge_ops['has_episode'], 'has_episode' in edge_kinds)

            @property
            def next_episode_edge_ops(self):
                return self.wrapped(edge_ops['next_episode'], 'next_episode' in edge_kinds)

        Driver.__name__ = name
        Driver.__qualname__ = name
        return Driver()

    results = []

    for driver_name in DRIVER_NAMES:
        for kind, present in NODE_KINDS.items():
            namespace = NodeNamespace(make_driver(driver_name, present, [], []), FakeEmbedder())
            outcome = {}
            for name in NODE_NAMES:
                try:
                    sub = getattr(namespace, name)
                    outcome[name] = {'present': True, 'type': type(sub).__name__}
                except NotImplementedError as error:
                    outcome[name] = {'present': False, 'error': type(error).__name__, 'text': str(error)}
            results.append({'input': {'what': 'node_namespace', 'driver': driver_name, 'kind': kind, 'present': present}, 'output': outcome})
        for kind, present in EDGE_KINDS.items():
            namespace = EdgeNamespace(make_driver(driver_name, [], present, []), FakeEmbedder())
            outcome = {}
            for name in EDGE_NAMES:
                try:
                    sub = getattr(namespace, name)
                    outcome[name] = {'present': True, 'type': type(sub).__name__}
                except NotImplementedError as error:
                    outcome[name] = {'present': False, 'error': type(error).__name__, 'text': str(error)}
            results.append({'input': {'what': 'edge_namespace', 'driver': driver_name, 'kind': kind, 'present': present}, 'output': outcome})

    classes = {
        'entity_node': EntityNode,
        'episode_node': EpisodicNode,
        'community_node': CommunityNode,
        'saga_node': SagaNode,
        'entity_edge': EntityEdge,
        'episodic_edge': EpisodicEdge,
        'community_edge': CommunityEdge,
        'has_episode_edge': HasEpisodeEdge,
        'next_episode_edge': NextEpisodeEdge,
    }

    def build_item(kind, spec):
        kwargs = {**DEFAULTS[kind], 'created_at': T1, **spec}
        for key in DATE_KEYS:
            if isinstance(kwargs.get(key), str):
                kwargs[key] = dt.datetime.fromisoformat(kwargs[key])
        if kind == 'episode_node':
            kwargs['source'] = EpisodeType(kwargs['source'])
        return classes[kind](**kwargs)

    def argument(name, case, items):
        if name == 'item':
            return items[0]
        if name == 'items':
            return items
        if name == 'reference_time':
            return dt.datetime.fromisoformat(case['reference_time'])
        return case[name]

    for case in call_cases() + new_cases():
        ns_key = case.get('ns', 'node.entity')
        group, attribute, kind, _methods = NAMESPACES[ns_key]
        driver = make_driver('FakeDriver', NODE_NAMES, EDGE_NAMES, case.get('records', []))
        embedder = FakeEmbedder()
        namespace = (NodeNamespace if group == 'node' else EdgeNamespace)(driver, embedder)
        sub = getattr(namespace, attribute)
        transaction = FakeTransaction([]) if case.get('use_tx') else None
        method = case['method']
        items = [build_item(kind, spec) for spec in item_specs(case)]
        required, optional = signature(group, method)
        args = [argument(name, case, items) for name in required]
        kwargs = {}
        for name in optional:
            if name == 'tx':
                if not case.get('omit_tx'):
                    kwargs['tx'] = transaction
            elif name in case:
                kwargs[name] = case[name]
        outcome = {}
        result = None
        try:
            result = asyncio.run(getattr(sub, method)(*args, **kwargs))
            outcome['error'] = None
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
        outcome['calls'] = driver.calls + (transaction.calls if transaction else [])
        outcome['ops'] = driver.ops_seen
        outcome['result'] = encode_result(result)
        if method == 'save':
            outcome['same'] = result is items[0]
        outcome['embedder'] = embedder.calls
        if method.startswith('load_'):
            field = 'fact_embedding' if kind == 'entity_edge' else 'name_embedding'
            outcome['embeddings'] = [encode(getattr(item, field)) for item in items]
        results.append({'input': case, 'output': outcome})

    document = {
        'python': '.'.join(str(part) for part in sys.version_info[:3]),
        'source': 'graphiti_core/namespaces/nodes.py and edges.py',
        'cases': results,
    }
    text = json.dumps(document, ensure_ascii=True, indent=1) + '\n'
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(results)} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(results)} cases, {len(text) // 1024} KB')
    return 0


if __name__ == '__main__':
    sys.exit(main())
