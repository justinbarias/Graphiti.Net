"""Records what Neo4jGraphMaintenanceOperations does, from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python port/tools/record_graph_ops.py
    .upstream/graphiti/.venv/bin/python port/tools/record_graph_ops.py --check

It runs each method of graphiti_core/driver/neo4j/operations/graph_ops.py: clear_data,
build_indices_and_constraints, delete_all_indexes, get_community_clusters, determine_entity_community,
get_mentioned_nodes, and get_communities_by_nodes. A fake executor records each call: the query text, the
parameter keys in order, each value with its type, and the records that it gave back. The tool also records
the result or the exception, and each log line with its level. The calls and the log lines of a case are in
one ordered list.

get_community_clusters runs label_propagation, which does not end for some graphs (upstream defect U11, GAPS
F6). The tool tries each graph first with a time limit, and it leaves out each graph for which Python does not
end, as record_driver_contract.py does. The count of the graphs that it left out is in the file.

It writes tests/Port.Evaluation/values/neo4j_graph_ops.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The tool needs Python 3.12 and the pinned upstream. It reads the folder
of the variable GRAPHITI_UPSTREAM, or .upstream/graphiti. It changes nothing there. Nothing here uses the
network.
"""
from __future__ import annotations

import asyncio
import copy
import json
import logging
import os
import random
import signal
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'neo4j_graph_ops.json'

SEED = 20261127
TIME_LIMIT = 0.5
CREATED = ['2024-05-06T07:08:09.123456Z', '2024-05-06T07:08:09+00:00', '2023-01-02T03:04:05', '2024-12-31T23:59:59.999999Z']
NAMES = ['Alice', 'Zoë', '東京', 'Bob 😀', 'a b']
GROUP_IDS = ['g-1', 'grüp 東京']


class Timeout(Exception):
    pass


def _alarm(signum, frame):
    raise Timeout()


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
    if isinstance(value, (list, tuple, set)):
        return {'type': 'list', 'items': [encode(item) for item in value]}
    if isinstance(value, dict):
        return {'type': 'dict', 'items': [[key, encode(item)] for key, item in value.items()]}
    return {'type': type(value).__name__}


def node_record(uuid, name, group_id, labels=None, attributes=None, summary='about', created=CREATED[0]):
    """A record as the entity node return query gives it."""
    if labels is None:
        labels = ['Entity', 'Entity_' + group_id.replace('-', '')]
    properties = {
        'uuid': uuid,
        'name': name,
        'group_id': group_id,
        'summary': summary,
        'created_at': created,
        'labels': list(labels),
    }
    properties.update(attributes or {})
    return {
        'uuid': uuid,
        'name': name,
        'group_id': group_id,
        'created_at': created,
        'summary': summary,
        'labels': list(labels),
        'attributes': properties,
    }


def community_record(uuid, name, group_id, created=CREATED[0], embedding=None, summary='members'):
    return {
        'uuid': uuid,
        'name': name,
        'group_id': group_id,
        'created_at': created,
        'name_embedding': embedding,
        'summary': summary,
    }


def random_graph(rng, groups):
    """A graph of nodes and their neighbors. The edges are mostly symmetric, as RELATES_TO edges are."""
    nodes = {}
    edges = {}
    for group_id in groups:
        count = rng.randint(1, 6)
        uuids = [f'{group_id}:n{i}' for i in range(count)]
        order = uuids[:]
        rng.shuffle(order)
        for uuid in order:
            nodes.setdefault(group_id, []).append(
                node_record(
                    uuid,
                    rng.choice(NAMES),
                    group_id,
                    attributes=rng.choice([{}, {'age': 7}, {'tags': ['x', 'y'], 'name_embedding': [0.5, 1]}]),
                    created=rng.choice(CREATED),
                )
            )
            edges[uuid] = []
        for i in range(count):
            for j in range(i + 1, count):
                if rng.random() < 0.4:
                    weight = rng.choice([1, 1, 2, 3, 5])
                    edges[uuids[i]].append({'uuid': uuids[j], 'count': weight})
                    if rng.random() < 0.92:
                        edges[uuids[j]].append({'uuid': uuids[i], 'count': weight})
    return {'group_records': [], 'nodes': nodes, 'edges': edges}


def cluster_cases(rng):
    out = []

    def case(group_ids, graph, **extra):
        out.append({'method': 'get_community_clusters', 'group_ids': group_ids, 'graph': graph, **extra})

    empty = {'group_records': [], 'nodes': {}, 'edges': {}}
    # no group ids given, with each answer of the query for the group ids
    case(None, empty)
    case(None, {**empty, 'group_records': [{'group_ids': []}]})
    case(None, {**empty, 'group_records': [{'group_ids': None}]})
    one = {'group_records': [{'group_ids': ['g-1']}], 'nodes': {'g-1': [node_record('a', 'Alice', 'g-1')]}, 'edges': {'a': []}}
    case(None, one)
    case(None, {**one, 'group_records': [{'group_ids': ['g-1', 'g-1']}]})
    case(None, {**one, 'group_records': [{'group_ids': []}, {'group_ids': ['g-1']}]})
    # a value of the record that is not a list of text: Python binds group_ids again to open data (rule R6)
    group_nodes = {'a': [node_record('a1', 'Ann', 'a')], 'b': [node_record('b1', 'Ben', 'b')], '5': [], '': []}
    group_edges = {'a1': [], 'b1': []}
    for value in ('ab', '', [5], 0, [None]):
        case(None, {'group_records': [{'group_ids': value}], 'nodes': group_nodes, 'edges': group_edges})
    # group ids given
    case([], one)
    case(['g-1'], empty)
    case([''], {'group_records': [], 'nodes': {'': [node_record('e1', 'Empty', '')]}, 'edges': {'e1': []}})
    case(['g-1'], one)
    case(['g-1', 'g-1'], one)
    two = {
        'group_records': [],
        'nodes': {
            'g-1': [node_record('a', 'Alice', 'g-1'), node_record('b', 'Zoë', 'g-1')],
            'grüp 東京': [node_record('東', '東京', 'grüp 東京')],
        },
        'edges': {'a': [{'uuid': 'b', 'count': 1}], 'b': [{'uuid': 'a', 'count': 1}], '東': []},
    }
    case(['g-1', 'grüp 東京'], two)
    case(['grüp 東京', 'g-1'], two)
    case(['g-1'], {**two, 'group_records': []})
    triangle = {
        'group_records': [],
        'nodes': {'g-1': [node_record(u, u, 'g-1') for u in ['a', 'b', 'c']]},
        'edges': {
            'a': [{'uuid': 'b', 'count': 2}, {'uuid': 'c', 'count': 2}],
            'b': [{'uuid': 'a', 'count': 2}, {'uuid': 'c', 'count': 2}],
            'c': [{'uuid': 'a', 'count': 2}, {'uuid': 'b', 'count': 2}],
        },
    }
    case(['g-1'], triangle)
    # a neighbor that is not in the projection
    case(['g-1'], {**one, 'edges': {'a': [{'uuid': 'zzz', 'count': 1}]}})
    # a record that is not valid, or that holds an odd value
    case(['g-1'], {**one, 'edges': {'a': [{'count': 1}]}})
    case(['g-1'], {**one, 'edges': {'a': [{'uuid': 'a'}]}})
    case(['g-1'], {**one, 'edges': {'a': [{'uuid': 'a', 'count': 'x'}]}})
    case(['g-1'], {**one, 'edges': {'a': [{'uuid': 'a', 'count': 1.5}]}})
    case(['g-1'], {**one, 'edges': {'a': [{'uuid': 'a', 'count': None}]}})
    case(['g-1'], {**one, 'edges': {'a': [{'uuid': 'a', 'count': 2.0}]}})
    case(['g-1'], {**one, 'edges': {'a': [{'uuid': 'a', 'count': '3'}]}})
    case(['g-1'], {**one, 'edges': {'a': [{'uuid': 7, 'count': 3}]}})
    bad = node_record('a', 'Alice', 'g-1')
    for label, mutate in [
        ('no attributes', lambda r: r.pop('attributes')),
        ('attributes none', lambda r: r.__setitem__('attributes', None)),
        ('group none', lambda r: r.__setitem__('group_id', None)),
        ('name int', lambda r: r.__setitem__('name', 5)),
        ('no uuid', lambda r: r.pop('uuid')),
        ('no labels', lambda r: r.pop('labels')),
        ('labels none', lambda r: r.__setitem__('labels', None)),
        ('no summary', lambda r: r.pop('summary')),
        ('created none', lambda r: r.__setitem__('created_at', None)),
    ]:
        record = copy.deepcopy(bad)
        mutate(record)
        case(['g-1'], {'group_records': [], 'nodes': {'g-1': [record]}, 'edges': {'a': []}}, note=label)
    # the executor fails at a call
    for index in (0, 1, 2):
        case(['g-1'], two, fail_at=index)
    # random graphs
    for _ in range(70):
        groups = rng.choice([['g-1'], ['g-1'], ['g-1', 'grüp 東京'], ['g-1', 'g-2', 'g-3']])
        graph = random_graph(rng, groups)
        graph['group_records'] = [{'group_ids': groups}] if rng.random() < 0.3 else []
        case(None if graph['group_records'] else groups, graph)
    return out


def cases(rng):
    out = []
    group_lists = [None, [], ['g-1'], ['g-1', 'grüp 東京'], [''], ['a', 'a']]
    for group_ids in group_lists:
        out.append({'method': 'clear_data', 'group_ids': group_ids})
    out.append({'method': 'clear_data'})
    out.append({'method': 'clear_data', 'group_ids': ['g-1', 'g-2'], 'fail_at': 0})
    out.append({'method': 'clear_data', 'group_ids': ['g-1', 'g-2'], 'fail_at': 2})
    out.append({'method': 'clear_data', 'group_ids': None, 'fail_at': 0})
    out.append({'method': 'build_indices_and_constraints'})
    out.append({'method': 'build_indices_and_constraints', 'delete_existing': False})
    out.append({'method': 'build_indices_and_constraints', 'delete_existing': True})
    out.append({'method': 'delete_all_indexes'})
    out.append({'method': 'delete_all_indexes', 'fail_at': 0})
    out.extend(cluster_cases(rng))

    first = [community_record('c1', 'Community', 'g-1')]
    second = [community_record('c2', '東京', 'g-1', embedding=[0.5, 1]), community_record('c3', 'Zoë', 'g-1')]
    for uuid in ['u-1', 'Zoë 東京', '']:
        out.append({'method': 'determine_entity_community', 'uuid': uuid, 'responses': []})
        out.append({'method': 'determine_entity_community', 'uuid': uuid, 'responses': [first]})
        out.append({'method': 'determine_entity_community', 'uuid': uuid, 'responses': [first, second]})
        out.append({'method': 'determine_entity_community', 'uuid': uuid, 'responses': [[], second]})
        out.append({'method': 'determine_entity_community', 'uuid': uuid, 'responses': [[], []]})
    out.append({'method': 'determine_entity_community', 'uuid': 'u-1', 'responses': [], 'fail_at': 0})
    out.append({'method': 'determine_entity_community', 'uuid': 'u-1', 'responses': [], 'fail_at': 1})

    entity_records = [
        node_record('a', 'Alice', 'g-1', attributes={'age': 3}),
        node_record('東', '東京', 'grüp 東京', labels=['Entity', 'Person', 'Entity_grüp 東京']),
        node_record('c', 'Bob 😀', 'g-1', labels=['Entity', 'Entity_g1', 'Place'], created=CREATED[2]),
    ]
    episode_lists = [[], ['e-1'], ['e-1', 'e-1'], ['e-1', 'Zoë 東京', '']]
    for episodes in episode_lists:
        for records in ([], entity_records[:1], entity_records):
            out.append({'method': 'get_mentioned_nodes', 'episodes': episodes, 'responses': [records]})
    out.append({'method': 'get_mentioned_nodes', 'episodes': ['e-1'], 'responses': [[entity_records[0], entity_records[0]]]})
    out.append({'method': 'get_mentioned_nodes', 'episodes': ['e-1'], 'responses': [[{'uuid': 'x'}]]})
    out.append({'method': 'get_mentioned_nodes', 'episodes': ['e-1'], 'responses': [[entity_records[0], {**entity_records[1], 'name': 5}]]})
    out.append({'method': 'get_mentioned_nodes', 'episodes': ['e-1'], 'responses': [[]], 'fail_at': 0})
    out.append({'method': 'get_mentioned_nodes', 'episodes': ['e-1'], 'responses': [[{**entity_records[0], 'labels': ['Entity', 'Bad Label']}]]})

    community_records = [
        community_record('c1', 'Community', 'g-1'),
        community_record('c2', '東京', 'grüp 東京', embedding=[0.5, 1], created=CREATED[1]),
        community_record('c3', 'Zoë', 'g-1', created=CREATED[2], summary=''),
    ]
    node_lists = [[], ['a'], ['a', 'a'], ['a', 'Zoë 東京', '']]
    for nodes in node_lists:
        for records in ([], community_records[:1], community_records):
            out.append({'method': 'get_communities_by_nodes', 'nodes': nodes, 'responses': [records]})
    out.append({'method': 'get_communities_by_nodes', 'nodes': ['a'], 'responses': [[community_records[0], community_records[0]]]})
    out.append({'method': 'get_communities_by_nodes', 'nodes': ['a'], 'responses': [[{'uuid': 'x'}]]})
    out.append({'method': 'get_communities_by_nodes', 'nodes': ['a'], 'responses': [[{**community_records[0], 'name': 5}]]})
    out.append({'method': 'get_communities_by_nodes', 'nodes': ['a'], 'responses': [[]], 'fail_at': 0})
    return out


def main() -> int:
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core.driver.neo4j.operations import graph_ops
    from graphiti_core.driver.operations.graph_utils import Neighbor, label_propagation
    from graphiti_core.driver.query_executor import QueryExecutor
    from graphiti_core.nodes import EntityNode, EpisodeType, EpisodicNode

    class FakeExecutor(QueryExecutor):
        def __init__(self, case, events):
            self.case = case
            self.events = events
            self.count = 0
            self.in_flight = 0
            self.max_in_flight = 0

        def respond(self, index, query, kwargs):
            graph = self.case.get('graph')
            if graph is None:
                responses = self.case.get('responses', [])
                return responses[index] if index < len(responses) else []
            if 'collect(DISTINCT n.group_id)' in query:
                return graph['group_records']
            if 'RELATES_TO' in query:
                return graph['edges'].get(kwargs['uuid'], [])
            if 'n.uuid IN $uuids' in query:
                return [r for records in graph['nodes'].values() for r in records if r['uuid'] in kwargs['uuids']]
            if 'n.group_id IN $group_ids' in query:
                return graph['nodes'].get(kwargs['group_ids'][0], [])
            raise AssertionError(query)

        async def execute_query(self, cypher_query_, **kwargs):
            self.in_flight += 1
            self.max_in_flight = max(self.max_in_flight, self.in_flight)
            try:
                await asyncio.sleep(0)
                return self.run_call(cypher_query_, kwargs)
            finally:
                self.in_flight -= 1

        def run_call(self, cypher_query_, kwargs):
            index = self.count
            self.count += 1
            records = copy.deepcopy(self.respond(index, cypher_query_, kwargs))
            self.events.append(
                {
                    'kind': 'call',
                    'query': cypher_query_,
                    'keys': list(kwargs),
                    'values': [encode(value) for value in kwargs.values()],
                    'response': copy.deepcopy(records),
                }
            )
            if self.case.get('fail_at') == index:
                raise RuntimeError('boom')
            return records, None, None

    class Capture(logging.Handler):
        def __init__(self, events):
            super().__init__(logging.DEBUG)
            self.events = events

        def emit(self, record):
            self.events.append({'kind': 'log', 'level': record.levelname, 'message': record.getMessage()})

    def terminates(case):
        """False if label_propagation does not end for a graph of the case, within the time limit."""
        graph = case['graph']
        groups = case.get('group_ids')
        if groups is None:
            groups = (graph['group_records'][0]['group_ids'] if graph['group_records'] else []) or []
        for group_id in groups:
            try:
                projection = {
                    r['uuid']: [Neighbor(node_uuid=e['uuid'], edge_count=e['count']) for e in graph['edges'].get(r['uuid'], [])]
                    for r in graph['nodes'].get(group_id, [])
                }
            except Exception:  # noqa: BLE001 - an invalid record ends the call before label_propagation
                return True
            signal.setitimer(signal.ITIMER_REAL, TIME_LIMIT)
            try:
                label_propagation(projection)
            except Timeout:
                return False
            except Exception:  # noqa: BLE001 - an error is a result that the recording keeps
                pass
            finally:
                signal.setitimer(signal.ITIMER_REAL, 0)
        return True

    events = []
    capture = Capture(events)
    logger = logging.getLogger(graph_ops.__name__)
    logger.setLevel(logging.DEBUG)
    logger.propagate = False
    logger.addHandler(capture)
    operations = graph_ops.Neo4jGraphMaintenanceOperations()
    signal.signal(signal.SIGALRM, _alarm)

    def dump(node):
        return encode(node.model_dump(mode='json'))

    results = []
    nonterminating = 0
    for case in cases(random.Random(SEED)):
        if 'graph' in case and not terminates(case):
            nonterminating += 1
            continue
        events.clear()
        executor = FakeExecutor(case, events)
        method = case['method']
        outcome = {}
        try:
            if method == 'clear_data':
                if 'group_ids' in case:
                    result = asyncio.run(operations.clear_data(executor, case['group_ids']))
                else:
                    result = asyncio.run(operations.clear_data(executor))
            elif method == 'build_indices_and_constraints':
                if 'delete_existing' in case:
                    result = asyncio.run(operations.build_indices_and_constraints(executor, case['delete_existing']))
                else:
                    result = asyncio.run(operations.build_indices_and_constraints(executor))
            elif method == 'delete_all_indexes':
                result = asyncio.run(operations.delete_all_indexes(executor))
            elif method == 'get_community_clusters':
                result = asyncio.run(operations.get_community_clusters(executor, case['group_ids']))
                result = [[dump(node) for node in cluster] for cluster in result]
            elif method == 'determine_entity_community':
                entity = EntityNode(uuid=case['uuid'], name='n', group_id='g')
                result = asyncio.run(operations.determine_entity_community(executor, entity))
            elif method == 'get_mentioned_nodes':
                episodes = [
                    EpisodicNode(
                        uuid=u,
                        name='e',
                        group_id='g',
                        source=EpisodeType.message,
                        source_description='d',
                        content='c',
                        valid_at=datetime(2024, 1, 1, tzinfo=timezone.utc),
                    )
                    for u in case['episodes']
                ]
                result = [dump(node) for node in asyncio.run(operations.get_mentioned_nodes(executor, episodes))]
            else:
                nodes = [EntityNode(uuid=u, name='n', group_id='g') for u in case['nodes']]
                result = [dump(node) for node in asyncio.run(operations.get_communities_by_nodes(executor, nodes))]
            outcome['error'] = None
            outcome['result'] = result
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
        outcome['events'] = list(events)
        outcome['max_in_flight'] = executor.max_in_flight
        results.append({'input': case, 'output': outcome})

    document = {
        'python': '.'.join(str(part) for part in sys.version_info[:3]),
        'source': 'graphiti_core/driver/neo4j/operations/graph_ops.py',
        'nonterminating_graphs_left_out': nonterminating,
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
    print(f'{OUT.relative_to(ROOT)}: {len(results)} cases, {len(text) // 1024} KB, {nonterminating} graphs left out')
    return 0


if __name__ == '__main__':
    sys.exit(main())
