"""Records what Neo4jEntityNodeOperations does, from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_entity_node_ops.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_entity_node_ops.py --check

It runs each of the ten methods of graphiti_core/driver/neo4j/operations/entity_node_ops.py. A fake
executor and a fake transaction record each call: the query text, the parameter keys in order, and each
value with its type. The calls and the log lines of one run are one list in the order of their time. The
tool also records the result or the exception.

A case that has "defaults": true passes only the required arguments. A value of a case that has the shape
{"$dt": "<isoformat>"} is a datetime. The tool's own encoding of a result: {"type": "datetime", ...} for a
datetime, and {"type": "EntityNode", "items": [...]} for a node.

save and save_bulk write the labels with list(set(labels + ['Entity'])). The order of a set of text
depends on the hash seed of the Python run, so two runs would write two orders. The tool gives
entity_node_ops a replacement for the built-in set that keeps Entity first and then the other labels in
their first order, each one time. This is the order that the port uses (R14). It is the one change that
the tool makes to the module.

It writes tests/Port.Evaluation/values/neo4j_entity_node_ops.json. --check writes nothing and exits 1 if
the file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream. The
environment variable GRAPHITI_UPSTREAM names the upstream folder, for a worktree that has no .upstream. It
changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import json
import logging
import os
import sys
from datetime import datetime
from enum import Enum
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM', str(ROOT / '.upstream' / 'graphiti')))
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'neo4j_entity_node_ops.json'

UUIDS = ['u-1', 'Zoë 東京 😀']
GROUP_IDS = ['g-1', 'grüp 東京']
UUID_LISTS = [[], ['a'], ['a', 'a'], ['a', 'b'], ['Zoë', '東京 😀']]
BATCH_SIZES = [100, 0]
GROUP_ID_LISTS = [[], ['g-1'], ['g-1', 'g-2']]
LIMITS = [None, 0, 5]
CURSORS = [None, '', 'c-1']
EMBEDDINGS = [
    None,
    [],
    [0.1, 0.25],
    [1, 2],
    [True, False],
    ['1.5', '2'],
    ['x'],
    [1, 'x', 2.5, 'y'],
    'abc',
    '1.5',
    5,
    {'a': 1},
    [[1.0]],
    [None],
    [1e300, -0.0],
]


def dt(text):
    return {'$dt': text}


def plain(uuid):
    return {'uuid': uuid, 'name': 'n', 'group_id': 'g'}


def entity_node(kind):
    nodes = {
        # each field set
        'full': {
            'uuid': 'Zoë 東京 😀', 'name': 'Zoë ü', 'group_id': 'grüp 東京', 'labels': ['Person', 'Org', 'Place'],
            'created_at': dt('2026-01-02T03:04:05.123456+00:00'), 'name_embedding': [0.1, 0.25], 'summary': 'sum 😀',
            'attributes': {'age': 3, 'nested': {'k': [1, 'x', None], 'm': {'z': 1.5}}, 'flag': True, 'none': None},
        },
        # the defaults of each field but the time
        'defaults': {'uuid': 'e-1', 'name': 'n', 'group_id': 'g', 'created_at': dt('2026-01-02T03:04:05+00:00')},
        'one_label': {
            'uuid': 'e-2', 'name': 'n', 'group_id': 'g', 'labels': ['Person'], 'created_at': dt('2026-01-02T03:04:05'),
            'name_embedding': None, 'summary': '', 'attributes': {},
        },
        # a label that repeats, and Entity in the labels
        'entity_label': {
            'uuid': 'e-3', 'name': '', 'group_id': '', 'labels': ['Entity', 'Foo', 'Foo'],
            'created_at': dt('2026-01-02T03:04:05+00:00'), 'name_embedding': [], 'summary': 'x',
        },
        # attributes that have the key of a field: the attributes win
        'override': {
            'uuid': 'e-4', 'name': 'n', 'group_id': 'g', 'created_at': dt('2026-01-02T03:04:05+00:00'),
            'name_embedding': [1, 2], 'summary': 's', 'attributes': {'summary': 'over', 'extra': [1, 2], 'name': 'other'},
        },
    }
    return nodes[kind]


NODE_KINDS = ['full', 'defaults', 'one_label', 'entity_label', 'override']


def entity_record(**changes):
    record = {
        'uuid': 'e-1', 'name': 'ep', 'group_id': 'g-1', 'created_at': dt('2026-01-02T03:04:05+00:00'),
        'summary': 'sum', 'labels': ['Entity', 'Person'], 'attributes': {'age': 3},
    }
    record.update(changes)
    return {key: value for key, value in record.items() if value is not Ellipsis}


def entity_record_sets():
    good = entity_record()
    return [
        [],
        [good],
        [good, entity_record(uuid='e-2', name='ü', group_id='grüp-東京', labels=['Entity', 'Entity_grüp東京', 'Org'], attributes={})],
        [entity_record(labels=['Entity', 'Entity_g1', 'Entity_g1'], group_id='g-1')],
        [entity_record(created_at='2026-01-02T03:04:05+00:00')],
        [entity_record(created_at='2026-01-02T03:04:05', labels=Ellipsis)],
        [entity_record(created_at='2026-01-02T03:04:05Z')],
        [entity_record(created_at=None)],
        [entity_record(created_at=Ellipsis)],
        [entity_record(created_at=0)],
        [entity_record(uuid=Ellipsis)],
        [entity_record(name=Ellipsis)],
        [entity_record(name=None)],
        [entity_record(summary=Ellipsis)],
        [entity_record(summary=None)],
        [entity_record(attributes=Ellipsis)],
        [entity_record(attributes=None)],
        [entity_record(attributes={'uuid': 'a', 'name': 'a', 'group_id': 'a', 'name_embedding': [1], 'summary': 'a', 'created_at': 'a', 'labels': ['a'], 'keep': {'n': None}})],
        [entity_record(group_id=None)],
        [entity_record(group_id=Ellipsis)],
        [entity_record(labels=None)],
        [entity_record(labels=['Entity', 'bad label!'])],
        [entity_record(name_embedding=[0.5, 1])],
        [entity_record(name_embedding=['x'])],
        [entity_record(extra='ignored')],
        # two defects: the parser reads uuid first, so the error is a KeyError and not the error of the date
        [entity_record(uuid=Ellipsis, created_at='not-a-date')],
        [entity_record(created_at='not-a-date')],
        [good, entity_record(created_at='not-a-date')],
        [good, entity_record(uuid=Ellipsis)],
    ]


def cases():
    out = []

    def add(method, **fields):
        out.append({'method': method, **fields})

    # save: each node through the executor and through a transaction, and with the defaults.
    for kind in NODE_KINDS:
        add('save', defaults=True, node=entity_node(kind))
        for use_tx in (False, True):
            add('save', use_tx=use_tx, node=entity_node(kind))

    # save_bulk: no node, one node, several nodes, and each node alone; with a batch size that splits the list.
    add('save_bulk', defaults=True, nodes=[entity_node('full'), entity_node('defaults')])
    node_lists = [
        [],
        [entity_node('full')],
        [entity_node(kind) for kind in NODE_KINDS],
        [entity_node('one_label'), entity_node('one_label')],
    ] + [[entity_node(kind)] for kind in NODE_KINDS[1:]]
    for nodes in node_lists:
        for batch_size in BATCH_SIZES + ([1, 2] if len(nodes) > 2 else []):
            for use_tx in (False, True):
                add('save_bulk', use_tx=use_tx, batch_size=batch_size, nodes=nodes)

    # delete, delete_by_group_id, delete_by_uuids.
    add('delete', defaults=True, node=plain('u-1'))
    for uuid in UUIDS:
        for use_tx in (False, True):
            add('delete', use_tx=use_tx, node=plain(uuid))
    add('delete_by_group_id', defaults=True, group_id='g-1')
    add('delete_by_uuids', defaults=True, uuids=['a', 'b'])
    for group_id in GROUP_IDS:
        for batch_size in BATCH_SIZES:
            for use_tx in (False, True):
                add('delete_by_group_id', use_tx=use_tx, group_id=group_id, batch_size=batch_size)
    for uuids in UUID_LISTS:
        for batch_size in BATCH_SIZES:
            for use_tx in (False, True):
                add('delete_by_uuids', use_tx=use_tx, uuids=uuids, batch_size=batch_size)

    # get_by_uuid, get_by_uuids, get_by_group_ids: records that make each parser branch reach its result or its error.
    sets = entity_record_sets()
    for records in sets:
        add('get_by_uuid', uuid='u-1', records=records)
    add('get_by_uuid', uuid='Zoë 東京 😀', records=[sets[1][0]])
    for records in sets:
        add('get_by_uuids', uuids=['a', 'Zoë'], records=records)
    add('get_by_uuids', uuids=[], records=[])
    add('get_by_uuids', uuids=[], records=sets[2])
    add('get_by_group_ids', defaults=True, group_ids=['g-1'], records=[sets[1][0]])
    for group_ids in GROUP_ID_LISTS:
        for limit in LIMITS:
            for cursor in CURSORS:
                add('get_by_group_ids', group_ids=group_ids, limit=limit, uuid_cursor=cursor, records=[sets[1][0]])
    for records in sets:
        add('get_by_group_ids', group_ids=['g-1'], limit=2, uuid_cursor='c-1', records=records)

    # load_embeddings and load_embeddings_bulk.
    for uuid in UUIDS:
        add('load_embeddings', node=plain(uuid), records=[])
        add('load_embeddings', node=plain(uuid), records=[{}])
        add('load_embeddings', node=plain(uuid), records=[{'name_embedding': [0.5]}, {'name_embedding': ['x']}])
        for embedding in EMBEDDINGS:
            add('load_embeddings', node=plain(uuid), records=[{'name_embedding': embedding}])
    nodes = [plain('n-1'), plain('Zoë 東京'), plain('n-1')]
    add('load_embeddings_bulk', nodes=[], records=[])
    add('load_embeddings_bulk', nodes=[plain('n-1')], records=[])
    add('load_embeddings_bulk', nodes=nodes, records=[])
    add('load_embeddings_bulk', nodes=nodes, records=[{'uuid': 'Zoë 東京', 'name_embedding': [2, 3]}])
    add('load_embeddings_bulk', nodes=nodes, records=[{'uuid': 'n-1', 'name_embedding': [1.5]}, {'uuid': 'other', 'name_embedding': [9]}])
    add('load_embeddings_bulk', nodes=nodes, records=[{'uuid': 'n-1', 'name_embedding': [1]}, {'uuid': 'n-1', 'name_embedding': [2]}])
    add('load_embeddings_bulk', nodes=nodes, records=[{'uuid': 'n-1', 'name_embedding': None}])
    for embedding in EMBEDDINGS:
        add('load_embeddings_bulk', nodes=nodes, records=[{'uuid': 'Zoë 東京', 'name_embedding': embedding}])
    add('load_embeddings_bulk', nodes=nodes, records=[{'uuid': 'n-1', 'name_embedding': [1]}, {'uuid': 'Zoë 東京', 'name_embedding': ['x']}])
    add('load_embeddings_bulk', nodes=nodes, records=[{'uuid': 'n-1', 'name_embedding': ['x']}, {'uuid': 'Zoë 東京', 'name_embedding': [7]}])
    add('load_embeddings_bulk', nodes=nodes, records=[{'uuid': 'n-1'}])
    add('load_embeddings_bulk', nodes=nodes, records=[{'name_embedding': [1]}])
    return out


def decode(value):
    """A value of a case as Python holds it: {"$dt": text} is a datetime."""
    if isinstance(value, dict):
        if set(value) == {'$dt'}:
            return datetime.fromisoformat(value['$dt'])
        return {key: decode(item) for key, item in value.items()}
    if isinstance(value, list):
        return [decode(item) for item in value]
    return value


def main() -> int:
    sys.path.insert(0, str(UPSTREAM))
    from pydantic import BaseModel

    from graphiti_core.driver.neo4j.operations import entity_node_ops
    from graphiti_core.driver.query_executor import QueryExecutor, Transaction
    from graphiti_core.nodes import EntityNode

    def ordered_set(items):
        # Entity first, then the other items in their first order, each one time (see the docstring).
        rest = [item for item in dict.fromkeys(items) if item != 'Entity']
        return ['Entity', *rest] if 'Entity' in items else rest

    entity_node_ops.set = ordered_set

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
        if isinstance(value, str):
            return {'type': 'str', 'value': value}
        if isinstance(value, datetime):
            return {'type': 'datetime', 'value': value.isoformat()}
        if isinstance(value, Enum):
            return {'type': 'enum', 'value': value.value}
        if isinstance(value, BaseModel):
            return {'type': type(value).__name__, 'items': [[key, encode(item)] for key, item in dict(value).items()]}
        if isinstance(value, (list, tuple, set)):
            return {'type': 'list', 'items': [encode(item) for item in value]}
        if isinstance(value, dict):
            return {'type': 'dict', 'items': [[key, encode(item)] for key, item in value.items()]}
        return {'type': type(value).__name__}

    events = []

    class FakeExecutor(QueryExecutor):
        def __init__(self, records):
            self.records = records

        async def execute_query(self, cypher_query_, **kwargs):
            events.append(
                {
                    'kind': 'call',
                    'via': 'executor',
                    'query': cypher_query_,
                    'keys': list(kwargs),
                    'values': [encode(value) for value in kwargs.values()],
                }
            )
            return self.records, None, None

    class FakeTransaction(Transaction):
        async def run(self, query, **kwargs):
            events.append(
                {
                    'kind': 'call',
                    'via': 'transaction',
                    'query': query,
                    'keys': list(kwargs),
                    'values': [encode(value) for value in kwargs.values()],
                }
            )

    class Capture(logging.Handler):
        def __init__(self):
            super().__init__(logging.DEBUG)

        def emit(self, record):
            events.append({'kind': 'log', 'level': record.levelname, 'message': record.getMessage()})

    # The handler sits on the package logger, so that it also gets the lines of graphiti_core.nodes.
    logger = logging.getLogger('graphiti_core')
    logger.setLevel(logging.DEBUG)
    logger.propagate = False
    logger.addHandler(Capture())
    operations = entity_node_ops.Neo4jEntityNodeOperations()

    async def run(case, executor, tx):
        method = case['method']
        fn = getattr(operations, method)
        optional = not case.get('defaults')
        if method in ('save', 'delete'):
            kwargs = {'tx': tx} if optional else {}
            return await fn(executor, EntityNode(**decode(case['node'])), **kwargs)
        if method == 'save_bulk':
            kwargs = {'tx': tx, 'batch_size': case['batch_size']} if optional else {}
            return await fn(executor, [EntityNode(**decode(n)) for n in case['nodes']], **kwargs)
        if method in ('delete_by_group_id', 'delete_by_uuids'):
            kwargs = {'tx': tx, 'batch_size': case['batch_size']} if optional else {}
            return await fn(executor, case['group_id'] if method == 'delete_by_group_id' else case['uuids'], **kwargs)
        if method == 'get_by_uuid':
            return await fn(executor, case['uuid'])
        if method == 'get_by_uuids':
            return await fn(executor, case['uuids'])
        if method == 'get_by_group_ids':
            kwargs = {'limit': case['limit'], 'uuid_cursor': case['uuid_cursor']} if optional else {}
            return await fn(executor, case['group_ids'], **kwargs)
        if method == 'load_embeddings':
            node = EntityNode(**decode(case['node']))
            await fn(executor, node)
            return node.name_embedding
        nodes = [EntityNode(**decode(n)) for n in case['nodes']]
        await fn(executor, nodes)
        return [n.name_embedding for n in nodes]

    results = []
    for case in cases():
        events.clear()
        executor = FakeExecutor(decode(case.get('records', [])))
        transaction = FakeTransaction() if case.get('use_tx') else None
        outcome = {}
        try:
            result = asyncio.run(run(case, executor, transaction))
            outcome['error'] = None
            if result is not None:
                outcome['result'] = encode(result)
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
        outcome['events'] = list(events)
        results.append({'input': case, 'output': outcome})

    document = {
        'python': '.'.join(str(part) for part in sys.version_info[:3]),
        'source': 'graphiti_core/driver/neo4j/operations/entity_node_ops.py',
    }
    text = '{"python":' + json.dumps(document['python']) + ',"source":' + json.dumps(document['source']) + ',"cases":[\n'
    text += ',\n'.join(json.dumps(result, ensure_ascii=True, separators=(',', ':')) for result in results) + '\n]}\n'
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
