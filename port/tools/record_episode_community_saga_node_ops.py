"""Records what the episode, community, and saga node operations do, from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_episode_community_saga_node_ops.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_episode_community_saga_node_ops.py --check

It runs each method of Neo4jEpisodeNodeOperations, Neo4jCommunityNodeOperations, and
Neo4jSagaNodeOperations (graphiti_core/driver/neo4j/operations/episode_node_ops.py,
community_node_ops.py, and saga_node_ops.py). A fake executor and a fake transaction record each
call: the query text, the parameter keys in order, and each value with its type. The calls and the
log lines of one run are one list in the order of their time. The tool also records the result or
the exception.

Each method runs with its defaults one time (the case has "defaults": true and passes only the
required arguments), and with each branch of the method: with and without a transaction, a batch
size of 0, a group id that is the empty text, an empty list, each optional filter on and off, and a
limit of 0. The get methods run with generated records that make each parser branch reach its
result or its error.

A value of a case that has the shape {"$dt": "<isoformat>"} is a datetime. The value of the tool's
own encoding: {"type": "datetime", "value": <isoformat>} for a datetime, {"type": "enum", ...} for an
enum member, and {"type": "<class name>", "items": [...]} for a model.

It writes tests/Port.Evaluation/values/neo4j_episode_community_saga_node_ops.json. --check writes
nothing and exits 1 if the file differs from what the run would write. The tool needs Python 3.12 and
the pinned upstream. The environment variable GRAPHITI_UPSTREAM names the upstream folder, for a
worktree that has no .upstream. It changes nothing there. Nothing here uses the network.
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
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'neo4j_episode_community_saga_node_ops.json'

UUIDS = ['u-1', 'Zoë 東京 😀']
GROUP_IDS = ['g-1', 'grüp 東京', '']
UUID_LISTS = [[], ['a'], ['a', 'a'], ['Zoë', '東京 😀']]
BATCH_SIZES = [100, 0]
GROUP_ID_LISTS = [[], ['g-1'], ['g-1', 'g-2'], ['']]
LIMITS = [None, 0, 5]
CURSORS = [None, '', 'c-1']
REF_TIME = {'$dt': '2026-03-01T12:30:00+00:00'}
REF_TIME_NAIVE = {'$dt': '2026-03-01T12:30:00.123456'}
EMBEDDINGS = [None, [], [0.1, 0.25], [1, 2], ['1.5'], ['x'], 'abc', 5, [[1.0]], [None], [True]]


def dt(text):
    return {'$dt': text}


def episode_node(kind):
    nodes = {
        'plain': {
            'uuid': 'e-1', 'name': 'ep', 'group_id': 'g-1', 'source': 'message', 'source_description': 'desc',
            'content': 'hello', 'valid_at': dt('2026-01-01T00:00:00+00:00'),
            'created_at': dt('2026-01-02T03:04:05.123456+00:00'), 'entity_edges': [],
        },
        'rich': {
            'uuid': 'Zoë 東京 😀', 'name': 'ep ü', 'group_id': 'grüp 東京', 'source': 'json',
            'source_description': 'd 😀', 'content': '{"k": 1}', 'valid_at': dt('2026-01-01T00:00:00'),
            'created_at': dt('2026-01-02T03:04:05'), 'entity_edges': ['a', 'b'], 'labels': ['Foo'],
            'episode_metadata': {'k': 1, 'n': None, 'l': [1, 'x']},
        },
        'empty': {
            'uuid': 'e-3', 'name': '', 'group_id': '', 'source': 'text', 'source_description': '',
            'content': '', 'valid_at': dt('2026-01-01T00:00:00+00:00'),
            'created_at': dt('2026-01-01T00:00:00+00:00'), 'entity_edges': ['a', 'a'], 'episode_metadata': {},
        },
    }
    return nodes[kind]


def community_node(kind):
    nodes = {
        'none': {'uuid': 'c-1', 'name': 'comm', 'group_id': 'g-1', 'created_at': dt('2026-01-02T03:04:05+00:00')},
        'vec': {
            'uuid': 'c-2', 'name': 'comm', 'group_id': 'g-1', 'summary': 'sum',
            'name_embedding': [0.1, 0.25], 'created_at': dt('2026-01-02T03:04:05.5+00:00'),
        },
        'rich': {
            'uuid': 'Zoë 東京 😀', 'name': 'c ü', 'group_id': '', 'summary': 'x 😀', 'name_embedding': [],
            'created_at': dt('2026-01-02T03:04:05'),
        },
    }
    return nodes[kind]


def saga_node(kind):
    nodes = {
        'plain': {'uuid': 's-1', 'name': 'saga', 'group_id': 'g-1', 'created_at': dt('2026-01-02T03:04:05+00:00')},
        'rich': {
            'uuid': 'Zoë 東京 😀', 'name': 's ü', 'group_id': '', 'summary': 'x', 'first_episode_uuid': 'a',
            'last_episode_uuid': 'b', 'last_summarized_at': dt('2026-02-01T00:00:00+00:00'),
            'last_summarized_episode_valid_at': dt('2026-01-15T00:00:00'), 'created_at': dt('2026-01-02T03:04:05'),
        },
    }
    return nodes[kind]


def episode_record(**changes):
    record = {
        'uuid': 'e-1', 'name': 'ep', 'group_id': 'g-1', 'created_at': dt('2026-01-02T03:04:05+00:00'),
        'source': 'message', 'source_description': 'desc', 'content': 'hello',
        'valid_at': dt('2026-01-01T00:00:00+00:00'), 'entity_edges': ['x'],
    }
    record.update(changes)
    return {key: value for key, value in record.items() if value is not Ellipsis}


def community_record(**changes):
    record = {
        'uuid': 'c-1', 'name': 'comm', 'group_id': 'g-1', 'name_embedding': [0.5, 1],
        'created_at': dt('2026-01-02T03:04:05+00:00'), 'summary': 'sum',
    }
    record.update(changes)
    return {key: value for key, value in record.items() if value is not Ellipsis}


def saga_record(**changes):
    record = {'uuid': 's-1', 'name': 'saga', 'group_id': 'g-1', 'created_at': dt('2026-01-02T03:04:05+00:00')}
    record.update(changes)
    return {key: value for key, value in record.items() if value is not Ellipsis}


def episode_record_sets():
    good = episode_record()
    return [
        [],
        [good],
        [good, episode_record(uuid='e-2', name='ü', source='json', entity_edges=[])],
        [episode_record(created_at='2026-01-02T03:04:05+00:00', valid_at='2026-01-01T00:00:00Z')],
        [episode_record(created_at='2026-01-02T03:04:05', valid_at='2026-01-01T00:00:00.123456+05:30')],
        [episode_record(created_at=None)],
        [episode_record(valid_at=None)],
        [episode_record(created_at=None, uuid=Ellipsis)],
        [episode_record(source='bogus')],
        [episode_record(content=Ellipsis)],
        [episode_record(entity_edges=None)],
        [episode_record(entity_edges=['a', 1])],
        [episode_record(name=None)],
        [episode_record(created_at=0)],
        [episode_record(created_at='not-a-date')],
        [episode_record(uuid=Ellipsis, created_at='not-a-date')],
        [episode_record(extra='ignored', source='text')],
        [good, episode_record(valid_at=None)],
    ]


def community_record_sets():
    good = community_record()
    return [
        [],
        [good],
        [good, community_record(uuid='c-2', name_embedding=None, summary='')],
        [community_record(created_at='2026-01-02T03:04:05+00:00')],
        [community_record(created_at='2026-01-02T03:04:05')],
        [community_record(created_at=None)],
        [community_record(created_at=Ellipsis)],
        [community_record(name_embedding=['x'])],
        [community_record(name_embedding='abc')],
        [community_record(summary=None)],
        [community_record(summary=Ellipsis)],
        [community_record(name=None)],
        [community_record(created_at='not-a-date')],
        [community_record(uuid=Ellipsis, created_at='not-a-date')],
        [community_record(extra='ignored')],
    ]


def saga_record_sets():
    good = saga_record()
    return [
        [],
        [good],
        [good, saga_record(uuid='s-2', name='ü')],
        [saga_record(created_at='2026-01-02T03:04:05+00:00')],
        [saga_record(created_at='2026-01-02T03:04:05')],
        [saga_record(created_at=None)],
        [saga_record(created_at=Ellipsis)],
        [saga_record(uuid=Ellipsis)],
        [saga_record(name=None)],
        [saga_record(created_at='not-a-date')],
        # two defects: the parser reads uuid first, so the error is a KeyError and not the error of the date
        [saga_record(uuid=Ellipsis, created_at='not-a-date')],
        [saga_record(created_at=0)],
        [saga_record(extra='ignored')],
    ]


def cases():
    out = []

    def add(module, method, **fields):
        out.append({'module': module, 'method': method, **fields})

    node_kinds = {'episode': ['plain', 'rich', 'empty'], 'community': ['none', 'vec', 'rich'], 'saga': ['plain', 'rich']}
    node_of = {'episode': episode_node, 'community': community_node, 'saga': saga_node}
    record_sets = {'episode': episode_record_sets(), 'community': community_record_sets(), 'saga': saga_record_sets()}

    for module in ('episode', 'community', 'saga'):
        kinds = node_kinds[module]
        make = node_of[module]
        sets = record_sets[module]

        # save and delete: the executor or the transaction, and the defaults.
        for kind in kinds:
            add(module, 'save', defaults=True, node=make(kind))
            for use_tx in (False, True):
                add(module, 'save', use_tx=use_tx, node=make(kind))
                add(module, 'delete', use_tx=use_tx, node=make(kind))
        add(module, 'delete', defaults=True, node=make(kinds[0]))

        # save_bulk: the empty list, one node, and each node.
        add(module, 'save_bulk', defaults=True, nodes=[make(kinds[0]), make(kinds[1])])
        for nodes in ([], [make(kinds[0])], [make(kind) for kind in kinds]):
            for batch_size in BATCH_SIZES:
                for use_tx in (False, True):
                    add(module, 'save_bulk', use_tx=use_tx, batch_size=batch_size, nodes=nodes)

        # delete_by_group_id and delete_by_uuids.
        add(module, 'delete_by_group_id', defaults=True, group_id='g-1')
        add(module, 'delete_by_uuids', defaults=True, uuids=['a', 'b'])
        for group_id in GROUP_IDS:
            for batch_size in BATCH_SIZES:
                for use_tx in (False, True):
                    add(module, 'delete_by_group_id', use_tx=use_tx, group_id=group_id, batch_size=batch_size)
        for uuids in UUID_LISTS:
            for batch_size in BATCH_SIZES:
                for use_tx in (False, True):
                    add(module, 'delete_by_uuids', use_tx=use_tx, uuids=uuids, batch_size=batch_size)

        # get_by_uuid: each record set. get_by_uuids: four sets and two lists.
        for records in sets:
            add(module, 'get_by_uuid', uuid='u-1', records=records)
        add(module, 'get_by_uuid', uuid='Zoë 東京 😀', records=[sets[1][0]])
        for records in (sets[0], sets[1], sets[2], sets[5], sets[-1]):
            for uuids in ([], ['a', 'Zoë']):
                add(module, 'get_by_uuids', uuids=uuids, records=records)

        # get_by_group_ids: each filter, one record; and each record set once.
        add(module, 'get_by_group_ids', defaults=True, group_ids=['g-1'], records=[sets[1][0]])
        for group_ids in GROUP_ID_LISTS:
            for limit in LIMITS:
                for cursor in CURSORS:
                    add(module, 'get_by_group_ids', group_ids=group_ids, limit=limit, uuid_cursor=cursor, records=[sets[1][0]])
        for records in sets:
            add(module, 'get_by_group_ids', group_ids=['g-1'], limit=2, uuid_cursor='c-1', records=records)

    # get_by_entity_node_uuid and retrieve_episodes are methods of the episode class only.
    records = episode_record_sets()
    for uuid in UUIDS:
        add('episode', 'get_by_entity_node_uuid', uuid=uuid, records=records[1])
    for rec in records:
        add('episode', 'get_by_entity_node_uuid', uuid='n-1', records=rec)
    add('episode', 'retrieve_episodes', defaults=True, reference_time=REF_TIME, records=[records[1][0]])
    add('episode', 'retrieve_episodes', defaults=True, reference_time=REF_TIME_NAIVE, records=[])
    # group_ids and no saga argument: the default of saga decides the branch
    add('episode', 'retrieve_episodes', group_ids_only=['g-1'], reference_time=REF_TIME, records=[records[1][0]])
    for last_n in (5, 0):
        for group_ids in (None, [], ['g-1'], ['g-1', 'g-2'], ['']):
            for source in (None, '', 'message'):
                for saga in (None, '', 'saga-1'):
                    add(
                        'episode', 'retrieve_episodes', reference_time=REF_TIME, last_n=last_n, group_ids=group_ids,
                        source=source, saga=saga, records=[records[1][0]],
                    )
    add('episode', 'retrieve_episodes', reference_time=REF_TIME_NAIVE, last_n=3, group_ids=['g-1'], source='json', saga='s', records=[])
    add('episode', 'retrieve_episodes', reference_time=REF_TIME_NAIVE, last_n=3, group_ids=None, source=None, saga=None, records=[])
    for rec in records:
        add('episode', 'retrieve_episodes', reference_time=REF_TIME, last_n=3, group_ids=['g-1'], source=None, saga=None, records=rec)
        add('episode', 'retrieve_episodes', reference_time=REF_TIME, last_n=3, group_ids=['g-1'], source=None, saga='s', records=rec)

    # load_name_embedding is a method of the community class only.
    for uuid in UUIDS:
        add('community', 'load_name_embedding', node=community_node('none') | {'uuid': uuid}, records=[])
        add('community', 'load_name_embedding', node=community_node('none') | {'uuid': uuid}, records=[{}])
        add(
            'community', 'load_name_embedding', node=community_node('none') | {'uuid': uuid},
            records=[{'name_embedding': [0.5]}, {'name_embedding': ['x']}],
        )
        for embedding in EMBEDDINGS:
            add('community', 'load_name_embedding', node=community_node('none') | {'uuid': uuid}, records=[{'name_embedding': embedding}])
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

    from graphiti_core.driver.neo4j.operations import community_node_ops, episode_node_ops, saga_node_ops
    from graphiti_core.driver.query_executor import QueryExecutor, Transaction
    from graphiti_core.nodes import CommunityNode, EpisodicNode, SagaNode

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

    operations = {
        'episode': episode_node_ops.Neo4jEpisodeNodeOperations(),
        'community': community_node_ops.Neo4jCommunityNodeOperations(),
        'saga': saga_node_ops.Neo4jSagaNodeOperations(),
    }
    node_class = {'episode': EpisodicNode, 'community': CommunityNode, 'saga': SagaNode}

    async def run(case, executor, tx):
        module = case['module']
        method = case['method']
        ops = operations[module]
        cls = node_class[module]
        fn = getattr(ops, method)
        optional = not case.get('defaults') and 'group_ids_only' not in case
        if method in ('save', 'delete'):
            kwargs = {'tx': tx} if optional else {}
            return await fn(executor, cls(**decode(case['node'])), **kwargs)
        if method == 'save_bulk':
            kwargs = {'tx': tx, 'batch_size': case['batch_size']} if optional else {}
            return await fn(executor, [cls(**decode(n)) for n in case['nodes']], **kwargs)
        if method in ('delete_by_group_id', 'delete_by_uuids'):
            kwargs = {'tx': tx, 'batch_size': case['batch_size']} if optional else {}
            return await fn(executor, case['group_id'] if method == 'delete_by_group_id' else case['uuids'], **kwargs)
        if method in ('get_by_uuid', 'get_by_entity_node_uuid'):
            return await fn(executor, case['uuid'])
        if method == 'get_by_uuids':
            return await fn(executor, case['uuids'])
        if method == 'get_by_group_ids':
            kwargs = {'limit': case['limit'], 'uuid_cursor': case['uuid_cursor']} if optional else {}
            return await fn(executor, case['group_ids'], **kwargs)
        if method == 'retrieve_episodes':
            kwargs = (
                {'last_n': case['last_n'], 'group_ids': case['group_ids'], 'source': case['source'], 'saga': case['saga']}
                if optional
                else {}
            )
            if 'group_ids_only' in case:
                kwargs = {'group_ids': case['group_ids_only']}
            return await fn(executor, decode(case['reference_time']), **kwargs)
        node = cls(**decode(case['node']))
        await fn(executor, node)
        return node

    results = []
    for case in cases():
        events.clear()
        executor = FakeExecutor(decode(case.get('records', [])))
        transaction = FakeTransaction() if case.get('use_tx') else None
        outcome = {}
        try:
            result = asyncio.run(run(case, executor, transaction))
            outcome['error'] = None
            if case['method'] == 'load_name_embedding':
                outcome['result'] = encode(result.name_embedding)
            elif result is not None:
                outcome['result'] = encode(result)
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
        outcome['events'] = list(events)
        results.append({'input': case, 'output': outcome})

    document = {
        'python': '.'.join(str(part) for part in sys.version_info[:3]),
        'sources': [
            'graphiti_core/driver/neo4j/operations/episode_node_ops.py',
            'graphiti_core/driver/neo4j/operations/community_node_ops.py',
            'graphiti_core/driver/neo4j/operations/saga_node_ops.py',
        ],
        'cases': results,
    }
    text = '{"python":' + json.dumps(document['python']) + ',"sources":' + json.dumps(document['sources']) + ',"cases":[\n'
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
