"""Records what the driver contract of graphiti_core does, from real Python (port/RULES.md, R7, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_driver_contract.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_driver_contract.py --check

Modules: driver/driver.py, driver/record_parsers.py, driver/operations/graph_utils.py, and
driver/operations/graph_ops.py (remove_communities).

Sections of the recording:

- parsers: the four *_from_record functions with generated records. Each field is missing, then it
  takes each value of a pool (None, a wrong type, a date as text, as a datetime, as an int), then
  random combinations from a fixed seed, and a bad label together with another field error. The test
  compares each case with Python in full.
- neighbor: Neighbor(**data) with each field missing and with values of a wrong type.
- label_propagation: generated graphs. The function does not end for some inputs (upstream defect U11).
  The tool stops such a call after 0.02 seconds and counts it in "nonterminating". The test runs only
  the calls that ended.
- driver: with_database, clone, build_fulltext_query, transaction, _SessionTransaction.run, and the
  defaults of GraphDriver, run with a fake driver at the network boundary.
- remove_communities: GraphMaintenanceOperations.remove_communities with a fake executor.
- constants: DEFAULT_SIZE and the four index names with no environment value.

The tool writes tests/Port.Evaluation/values/driver_contract.json. --check writes nothing and exits 1
if the file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream
(GRAPHITI_UPSTREAM, or .upstream/graphiti). It changes nothing there. Nothing here uses the network.

Inputs that the port cannot reproduce by a rule are left out: a record value that is not None and
not of the type that the code calls a method on (attributes, labels, and group_id of an entity
record, and attributes of an edge record), and an edge_count that a 64-bit integer cannot hold (R7).
"""
from __future__ import annotations

import asyncio
import json
import os
import random
import signal
import sys
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM', str(ROOT / '.upstream' / 'graphiti')))
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'driver_contract.json'
SEED = 20261128

T0 = datetime(2026, 1, 1, tzinfo=timezone.utc)
T1 = datetime(2026, 2, 1, 12, 30, 15, 123456, tzinfo=timezone.utc)
NAIVE = datetime(2026, 3, 1, 9, 8, 7)

DATE_VALUES = [
    None, '2026-01-02T03:04:05+00:00', '2026-01-02T03:04:05Z', '2026-01-02 03:04:05', '2026-01-02',
    '2026-01-02T03:04:05.123456+10:00', 'garbage', '', T1, NAIVE, 0, 1234567890, 1.5e9, True, [], {},
]
OTHER_VALUES = [None, 5, 1.5, True, 'x', '', 'Zoë 東京 😀', [], ['a'], {}]
EMB_VALUES = [None, [], [0.1, 2], ['1.5'], ['x'], 'abc', 5]
SOURCE_VALUES = ['text', 'message', 'json', 'fact_triple', 'html', 'JSON', '', None, 5]
LABEL_VALUES = [None, [], ['Entity'], ['Entity', 'Person', 'Entity_abc', 'Entity_abc'], ['Bad Label'], ['Entity_a-b-c']]
GROUP_VALUES = [None, '', 'abc', 'a-b-c', 'g-1', 'Zoë-東京']
ATTR_VALUES = [None, {}, {'k': 1}, {'k': [1, {'n': None}]}, {'uuid': 'x', 'extra': 'é'}]
EDGE_LIST_VALUES = [None, [], ['e1'], ['e1', 'e2'], 'e1', [1]]

PROVIDERS = ['neo4j', 'falkordb', 'neptune', 'relate']


def encode(value):
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
        return {'type': 'enum', 'class': type(value).__name__, 'value': value.value}
    if isinstance(value, (list, tuple)):
        return {'type': 'list', 'items': [encode(item) for item in value]}
    if isinstance(value, dict):
        return {'type': 'dict', 'items': [[key, encode(item)] for key, item in value.items()]}
    return {'type': type(value).__name__}


def encode_kwargs(kwargs):
    return [[key, encode(value)] for key, value in kwargs.items()]


def error_of(error):
    return {'type': type(error).__name__, 'text': str(error)}


def copy_record(record):
    import copy

    return copy.deepcopy(record)


# ---------- parsers ----------

def parser_specs():
    entity = {
        'uuid': 'n1', 'name': 'Alice', 'group_id': 'a-b-c', 'created_at': '2026-01-01T00:00:00+00:00', 'summary': 's',
        'labels': ['Entity', 'Person', 'Entity_abc', 'Entity_abc'], 'name_embedding': [0.1, 2],
        'attributes': {'uuid': 'x', 'name': 'x', 'group_id': 'x', 'name_embedding': 'x', 'summary': 'x', 'created_at': 'x', 'labels': 'x', 'age': 3, 'city': '東京'},
    }
    edge = {
        'uuid': 'e1', 'source_node_uuid': 's1', 'target_node_uuid': 't1', 'fact': 'Ana knows Bo', 'fact_embedding': [0.5], 'name': 'KNOWS',
        'group_id': 'g', 'episodes': ['ep1', 'ep2'], 'created_at': '2026-01-02T03:04:05+00:00', 'expired_at': None,
        'valid_at': '2026-01-01T00:00:00+00:00', 'invalid_at': None, 'reference_time': '2026-01-15T10:00:00+00:00',
        'attributes': {'uuid': 'x', 'source_node_uuid': 'x', 'target_node_uuid': 'x', 'fact': 'x', 'fact_embedding': 'x', 'name': 'x',
                       'group_id': 'x', 'episodes': 'x', 'created_at': 'x', 'expired_at': 'x', 'valid_at': 'x', 'invalid_at': 'x',
                       'reference_time': 'x', 'weight': 0.5, 'tags': ['a']},
    }
    episodic = {
        'uuid': 'ep1', 'name': 'episode', 'group_id': 'g', 'created_at': '2026-01-02T03:04:05+00:00', 'valid_at': '2026-01-01T00:00:00+00:00',
        'source': 'message', 'source_description': 'sd', 'content': 'hello', 'entity_edges': ['e1'],
    }
    community = {
        'uuid': 'c1', 'name': 'C', 'group_id': 'g', 'name_embedding': [1.0], 'created_at': '2026-01-01T00:00:00+00:00', 'summary': 's',
    }
    return {
        'entity_node_from_record': (entity, {
            'uuid': OTHER_VALUES, 'name': OTHER_VALUES, 'group_id': GROUP_VALUES, 'created_at': DATE_VALUES,
            'summary': OTHER_VALUES, 'labels': LABEL_VALUES, 'name_embedding': EMB_VALUES, 'attributes': ATTR_VALUES,
        }),
        'entity_edge_from_record': (edge, {
            'uuid': OTHER_VALUES, 'source_node_uuid': OTHER_VALUES, 'target_node_uuid': OTHER_VALUES, 'fact': OTHER_VALUES,
            'fact_embedding': EMB_VALUES, 'name': OTHER_VALUES, 'group_id': OTHER_VALUES, 'episodes': EDGE_LIST_VALUES,
            'created_at': DATE_VALUES, 'expired_at': DATE_VALUES, 'valid_at': DATE_VALUES, 'invalid_at': DATE_VALUES,
            'reference_time': DATE_VALUES, 'attributes': ATTR_VALUES,
        }),
        'episodic_node_from_record': (episodic, {
            'uuid': OTHER_VALUES, 'name': OTHER_VALUES, 'group_id': OTHER_VALUES, 'created_at': DATE_VALUES, 'valid_at': DATE_VALUES,
            'source': SOURCE_VALUES, 'source_description': OTHER_VALUES, 'content': OTHER_VALUES, 'entity_edges': EDGE_LIST_VALUES,
        }),
        'community_node_from_record': (community, {
            'uuid': OTHER_VALUES, 'name': OTHER_VALUES, 'group_id': OTHER_VALUES, 'name_embedding': EMB_VALUES,
            'created_at': DATE_VALUES, 'summary': OTHER_VALUES,
        }),
    }


def parser_cases(rng):
    cases = []
    for function, (base, pools) in parser_specs().items():
        cases.append({'function': function, 'record': copy_record(base)})
        for field in base:
            missing = copy_record(base)
            del missing[field]
            cases.append({'function': function, 'record': missing})
        for field, values in pools.items():
            for value in values:
                cases.append({'function': function, 'record': {**copy_record(base), field: value}})
        if function == 'entity_node_from_record':
            # A bad label together with another field error: pydantic reports both, in field order.
            for other in [{'summary': None}, {'name': None}, {'created_at': 'nope'}, {'summary': None, 'name_embedding': ['x']}, {'uuid': 5}]:
                cases.append({'function': function, 'record': {**copy_record(base), 'labels': ['Bad Label', 'Entity_abc'], **other}})
            missing = copy_record(base)
            del missing['name']
            cases.append({'function': function, 'record': {**missing, 'labels': ['Bad Label']}})
        for _ in range(40):
            record = copy_record(base)
            for field, values in pools.items():
                roll = rng.random()
                if roll < 0.12:
                    del record[field]
                elif roll < 0.5:
                    record[field] = rng.choice(values)
            cases.append({'function': function, 'record': record})
    return cases


def run_parser(case):
    from graphiti_core.driver import record_parsers

    record = copy_record(case['record'])
    function = case['function']
    entry = {'function': function, 'record': encode_kwargs(case['record'])}
    try:
        node = getattr(record_parsers, function)(record)
        entry['error'] = None
        entry['result'] = {'class': type(node).__name__, 'dump': encode(node.model_dump())}
    except Exception as error:  # noqa: BLE001
        entry['error'] = error_of(error)
    return entry


# ---------- neighbor ----------

def neighbor_cases():
    from graphiti_core.driver.operations.graph_utils import Neighbor

    base = {'node_uuid': 'n1', 'edge_count': 2}
    datas = [dict(base)]
    for key in base:
        missing = dict(base)
        del missing[key]
        datas.append(missing)
    for value in [None, 5, 1.5, True, 'x', '', '3', ['a'], {}]:
        datas.append({**base, 'node_uuid': value})
    for value in [None, 'many', '3', '', 1.5, 2.0, True, False, [], {}, -4, 2**62]:
        datas.append({**base, 'edge_count': value})
    datas.append({**base, 'extra': 1})
    out = []
    for data in datas:
        entry = {'data': encode_kwargs(data)}
        try:
            entry['error'] = None
            entry['result'] = encode(Neighbor(**data).model_dump())
        except Exception as error:  # noqa: BLE001
            entry['error'] = error_of(error)
        out.append(entry)
    return out


# ---------- label_propagation ----------

class Timeout(Exception):
    pass


def _alarm(signum, frame):
    raise Timeout()


def graph_inputs(rng):
    graphs = [
        [], [('a', [])], [('a', []), ('b', [])], [('a', [('b', 1)]), ('b', [('a', 1)])],
        [('a', [('b', 2), ('c', 2)]), ('b', [('a', 2), ('c', 2)]), ('c', [('a', 2), ('b', 2)])],
        [('a', [('zzz', 1)])], [('a', [('a', 3)])], [('a', [('b', 0)]), ('b', [('a', 0)])], [('a', [('b', -3)]), ('b', [('a', -3)])],
        [('a', [('b', 1)]), ('b', [('a', 1), ('c', 1)]), ('c', [('b', 1)])],
        [('é', [('東', 5)]), ('東', [('é', 5)])],
    ]
    for _ in range(1000):
        n = rng.randint(1, 7)
        names = [f'n{i}' for i in range(n)]
        adjacency = {name: [] for name in names}
        for i in range(n):
            for j in range(i + 1, n):
                if rng.random() < 0.35:
                    count = rng.choice([1, 1, 2, 3, 5])
                    adjacency[names[i]].append((names[j], count))
                    if rng.random() < 0.92:
                        adjacency[names[j]].append((names[i], count))
        order = names[:]
        rng.shuffle(order)
        graphs.append([(name, adjacency[name]) for name in order])
    return graphs


def label_propagation_cases(rng):
    from graphiti_core.driver.operations.graph_utils import Neighbor, label_propagation

    cases = []
    nonterminating = 0
    signal.signal(signal.SIGALRM, _alarm)
    for graph in graph_inputs(rng):
        projection = {uuid: [Neighbor(node_uuid=u, edge_count=c) for u, c in neighbors] for uuid, neighbors in graph}
        entry = {'projection': [[uuid, [[u, c] for u, c in neighbors]] for uuid, neighbors in graph]}
        signal.setitimer(signal.ITIMER_REAL, 0.02)
        try:
            entry['error'] = None
            entry['result'] = label_propagation(projection)
        except Timeout:
            nonterminating += 1
            continue
        except Exception as error:  # noqa: BLE001
            entry['error'] = error_of(error)
        finally:
            signal.setitimer(signal.ITIMER_REAL, 0)
        cases.append(entry)
    return cases, nonterminating


# ---------- driver ----------

def driver_cases():
    from graphiti_core.driver.driver import GraphDriver, GraphDriverSession, GraphProvider

    calls = []

    class FakeSession(GraphDriverSession):
        async def run(self, query, **kwargs):
            calls.append({'via': 'run', 'query': query, 'keys': list(kwargs), 'values': [encode(v) for v in kwargs.values()]})
            return 'run-result'

        async def close(self):
            calls.append({'via': 'close'})

        async def execute_write(self, func, *args, **kwargs):
            raise AssertionError('not used')

        async def __aexit__(self, exc_type, exc, tb):
            calls.append({'via': 'exit'})

    class FakeDriver(GraphDriver):
        def __init__(self, provider, database):
            self.provider = provider
            self._database = database
            self.connection = object()
            self.default_group_id = 'dg'

        async def execute_query(self, cypher_query_, **kwargs):
            raise AssertionError('not used')

        def session(self, database=None):
            calls.append({'via': 'session', 'database': database})
            return FakeSession()

        def close(self):
            raise AssertionError('not used')

        def delete_all_indexes(self):
            raise AssertionError('not used')

        async def build_indices_and_constraints(self, delete_existing=False):
            raise AssertionError('not used')

    out = {'with_database': [], 'clone': [], 'build_fulltext_query': [], 'transaction': [], 'defaults': None}
    for provider in PROVIDERS:
        for database in ['other', '', 'Zoë-東京']:
            driver = FakeDriver(GraphProvider(provider), 'first')
            cloned = driver.with_database(database)
            out['with_database'].append({
                'provider': provider,
                'database': database,
                'same_object': cloned is driver,
                'type': type(cloned).__name__,
                'database_of_clone': cloned._database,
                'database_of_original': driver._database,
                'provider_of_clone': cloned.provider.value,
                'default_group_id': cloned.default_group_id,
                'same_connection': cloned.connection is driver.connection,
            })
            again = cloned.with_database('third')
            out['with_database'][-1]['chain'] = [cloned._database, again._database, again is cloned]
            out['clone'].append({'provider': provider, 'database': database, 'same_object': driver.clone(database) is driver, 'database_after': driver._database})
        driver = FakeDriver(GraphProvider(provider), 'first')
        for args in [('hello',), ('hello', ['g1', 'g2']), ('', None, 5)]:
            entry = {'provider': provider, 'args': encode(list(args))}
            try:
                entry['error'] = None
                entry['result'] = driver.build_fulltext_query(*args)
            except Exception as error:  # noqa: BLE001
                entry['error'] = error_of(error)
            out['build_fulltext_query'].append(entry)

    async def transaction_case(provider, body):
        del calls[:]
        driver = FakeDriver(GraphProvider(provider), 'first')
        entry = {'provider': provider, 'body': body}
        try:
            async with driver.transaction() as tx:
                entry['tx_type'] = type(tx).__name__
                if body == 'run':
                    entry['run_result'] = encode(await tx.run('MATCH (n) WHERE n.uuid = $uuid RETURN n', uuid='u-1', names=['a', 'é'], flag=None))
                elif body == 'two_runs':
                    await tx.run('A', x=1)
                    await tx.run('B')
                elif body == 'raise':
                    raise ValueError('boom')
            entry['error'] = None
        except Exception as error:  # noqa: BLE001
            entry['error'] = error_of(error)
        entry['calls'] = list(calls)
        return entry

    for provider in PROVIDERS:
        for body in ['empty', 'run', 'two_runs', 'raise']:
            out['transaction'].append(asyncio.run(transaction_case(provider, body)))

    driver = FakeDriver(GraphProvider.NEO4J, 'first')
    out['defaults'] = {
        'fulltext_syntax': GraphDriver.fulltext_syntax,
        'default_group_id': GraphDriver.default_group_id,
        'search_interface': encode(GraphDriver.search_interface),
        'graph_operations_interface': encode(GraphDriver.graph_operations_interface),
        'RELATE': GraphDriver.RELATE.value,
        'properties': {
            name: encode(getattr(driver, name))
            for name in [
                'entity_node_ops', 'episode_node_ops', 'community_node_ops', 'saga_node_ops', 'entity_edge_ops', 'episodic_edge_ops',
                'community_edge_ops', 'has_episode_edge_ops', 'next_episode_edge_ops', 'search_ops', 'graph_ops',
            ]
        },
    }
    return out


# ---------- remove_communities ----------

def remove_communities_cases():
    from graphiti_core.driver.operations.graph_ops import GraphMaintenanceOperations

    class Ops(GraphMaintenanceOperations):
        async def clear_data(self, executor, group_ids=None):
            raise AssertionError('not used')

        async def build_indices_and_constraints(self, executor, delete_existing=False):
            raise AssertionError('not used')

        async def delete_all_indexes(self, executor):
            raise AssertionError('not used')

        async def get_community_clusters(self, executor, group_ids=None):
            raise AssertionError('not used')

        async def determine_entity_community(self, executor, entity):
            raise AssertionError('not used')

        async def get_mentioned_nodes(self, executor, episodes):
            raise AssertionError('not used')

        async def get_communities_by_nodes(self, executor, nodes):
            raise AssertionError('not used')

    calls = []

    class Executor:
        async def execute_query(self, cypher_query_, **kwargs):
            calls.append({'query': cypher_query_, 'keys': list(kwargs), 'values': [encode(v) for v in kwargs.values()]})
            return [], None, None

    out = []
    for group_ids in [None, [], ['g1'], ['g1', 'Zoë-東京'], ['']]:
        del calls[:]
        asyncio.run(Ops().remove_communities(Executor(), group_ids))
        out.append({'group_ids': encode(group_ids), 'calls': list(calls)})
    del calls[:]
    asyncio.run(Ops().remove_communities(Executor()))
    out.append({'group_ids': 'default', 'calls': list(calls)})
    return out


def constants():
    for name in ['ENTITY_INDEX_NAME', 'EPISODE_INDEX_NAME', 'COMMUNITY_INDEX_NAME', 'ENTITY_EDGE_INDEX_NAME']:
        os.environ.pop(name, None)
    from graphiti_core.driver import driver

    return {
        'DEFAULT_SIZE': driver.DEFAULT_SIZE,
        'ENTITY_INDEX_NAME': driver.ENTITY_INDEX_NAME,
        'EPISODE_INDEX_NAME': driver.EPISODE_INDEX_NAME,
        'COMMUNITY_INDEX_NAME': driver.COMMUNITY_INDEX_NAME,
        'ENTITY_EDGE_INDEX_NAME': driver.ENTITY_EDGE_INDEX_NAME,
    }


def main():
    sys.path.insert(0, str(UPSTREAM))
    import logging

    logging.disable(logging.CRITICAL)
    rng = random.Random(SEED)
    consts = constants()
    parsers = [run_parser(case) for case in parser_cases(rng)]
    label_cases, nonterminating = label_propagation_cases(rng)
    document = {
        'python': '.'.join(str(part) for part in sys.version_info[:3]),
        'source': 'graphiti_core/driver',
        'seed': SEED,
        'constants': consts,
        'parsers': parsers,
        'neighbor': neighbor_cases(),
        'label_propagation': label_cases,
        'nonterminating': nonterminating,
        'driver': driver_cases(),
        'remove_communities': remove_communities_cases(),
    }
    text = json.dumps(document, ensure_ascii=True, separators=(',', ':')) + '\n'
    total = len(parsers) + len(document['neighbor']) + len(label_cases) + len(document['remove_communities']) + sum(
        len(v) for k, v in document['driver'].items() if isinstance(v, list)
    )
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {total} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {total} cases, {len(text) // 1024} KB, {nonterminating} nonterminating graphs left out')
    return 0


if __name__ == '__main__':
    sys.exit(main())
