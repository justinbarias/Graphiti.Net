"""Records what NodeNamespace and EdgeNamespace do, from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python port/tools/record_namespaces.py
    .upstream/graphiti/.venv/bin/python port/tools/record_namespaces.py --check

It runs graphiti_core/namespaces/nodes.py and edges.py with a fake driver. The fake driver is the
network boundary: it implements execute_query and records each query. Its operations are the real
Neo4j operations classes. The tool records two things:

- the construction of NodeNamespace and EdgeNamespace for each set of operations that a driver can
  give: which sub-namespaces exist, and the error that a missing one raises
- the calls of EntityNodeNamespace that reach the executor and stop at no function of a module that
  is not ported yet: delete, delete_by_group_id, delete_by_uuids, load_embeddings, and
  load_embeddings_bulk, with and without a transaction

save, get_by_uuid and the other calls need a skeleton function today (generate_name_embedding, the
record parsers, the queries of node_db_queries.py, or an operations class of another layer), so they
are not recorded.

It writes tests/Port.Evaluation/values/namespaces.json. --check writes nothing and exits 1 if the
file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream in
.upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM', ROOT / '.upstream' / 'graphiti'))
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'namespaces.json'

NODE_KINDS = {
    'none': [],
    'entity_only': ['entity'],
    'episode_saga': ['episode', 'saga'],
    'all': ['entity', 'episode', 'community', 'saga'],
}
NODE_NAMES = ['entity', 'episode', 'community', 'saga']
EDGE_KINDS = {
    'none': [],
    'episodic_only': ['episodic'],
    'entity_next': ['entity', 'next_episode'],
    'all': ['entity', 'episodic', 'community', 'has_episode', 'next_episode'],
}
EDGE_NAMES = ['entity', 'episodic', 'community', 'has_episode', 'next_episode']
DRIVER_NAMES = ['FakeDriver', 'Zoë東京Driver']

UUIDS = ['u-1', 'Zoë 東京 😀']
GROUP_IDS = ['g-1', 'grüp 東京']
UUID_LISTS = [[], ['a'], ['a', 'b'], ['Zoë', '東京 😀']]
BATCH_SIZES = [100, 0]
EMBEDDINGS = [None, [0.1, 0.25], [1, 2], ['x']]


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
    out.append({'method': 'load_embeddings_bulk', 'uuids': [], 'records': []})
    out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'Zoë 東京', 'name_embedding': [2, 3]}]})
    out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'n-1', 'name_embedding': [1.5]}, {'uuid': 'other', 'name_embedding': [9]}]})
    return out


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
    from graphiti_core.nodes import EntityNode

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

    class FakeEmbedder(EmbedderClient):
        async def create(self, input_data):
            return [0.0]

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
            self.note('transaction', query, kwargs)

    def make_driver(name, node_kinds, edge_kinds, records):
        class Driver(Recorder, GraphDriver):
            def __init__(self):
                Recorder.__init__(self, records)

            async def execute_query(self, cypher_query_, **kwargs):
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

            @property
            def entity_node_ops(self):
                return node_ops['entity']() if 'entity' in node_kinds else None

            @property
            def episode_node_ops(self):
                return node_ops['episode']() if 'episode' in node_kinds else None

            @property
            def community_node_ops(self):
                return node_ops['community']() if 'community' in node_kinds else None

            @property
            def saga_node_ops(self):
                return node_ops['saga']() if 'saga' in node_kinds else None

            @property
            def entity_edge_ops(self):
                return edge_ops['entity']() if 'entity' in edge_kinds else None

            @property
            def episodic_edge_ops(self):
                return edge_ops['episodic']() if 'episodic' in edge_kinds else None

            @property
            def community_edge_ops(self):
                return edge_ops['community']() if 'community' in edge_kinds else None

            @property
            def has_episode_edge_ops(self):
                return edge_ops['has_episode']() if 'has_episode' in edge_kinds else None

            @property
            def next_episode_edge_ops(self):
                return edge_ops['next_episode']() if 'next_episode' in edge_kinds else None

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
            results.append({'input': {'what': 'node_namespace', 'driver': driver_name, 'kind': kind}, 'output': outcome})
        for kind, present in EDGE_KINDS.items():
            namespace = EdgeNamespace(make_driver(driver_name, [], present, []), FakeEmbedder())
            outcome = {}
            for name in EDGE_NAMES:
                try:
                    sub = getattr(namespace, name)
                    outcome[name] = {'present': True, 'type': type(sub).__name__}
                except NotImplementedError as error:
                    outcome[name] = {'present': False, 'error': type(error).__name__, 'text': str(error)}
            results.append({'input': {'what': 'edge_namespace', 'driver': driver_name, 'kind': kind}, 'output': outcome})

    for case in call_cases():
        driver = make_driver('FakeDriver', ['entity'], [], case.get('records', []))
        namespace = NodeNamespace(driver, FakeEmbedder())
        entity = namespace.entity
        transaction = FakeTransaction([]) if case.get('use_tx') else None
        method = case['method']
        nodes = [EntityNode(uuid=uuid, name='n', group_id='g') for uuid in case.get('uuids', [])]
        node = EntityNode(uuid=case['uuid'], name='n', group_id='g') if 'uuid' in case and method != 'delete_by_uuids' else None
        outcome = {}
        try:
            if method == 'delete':
                asyncio.run(entity.delete(node, transaction))
            elif method == 'delete_by_group_id':
                asyncio.run(entity.delete_by_group_id(case['group_id'], transaction, case['batch_size']))
            elif method == 'delete_by_uuids':
                asyncio.run(entity.delete_by_uuids(case['uuids'], transaction, case['batch_size']))
            elif method == 'load_embeddings':
                asyncio.run(entity.load_embeddings(node))
            else:
                asyncio.run(entity.load_embeddings_bulk(nodes))
            outcome['error'] = None
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
        outcome['calls'] = driver.calls + (transaction.calls if transaction else [])
        if method == 'load_embeddings':
            outcome['embeddings'] = [encode(node.name_embedding)]
        elif method == 'load_embeddings_bulk':
            outcome['embeddings'] = [encode(n.name_embedding) for n in nodes]
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
