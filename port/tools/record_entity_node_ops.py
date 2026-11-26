"""Records what Neo4jEntityNodeOperations does, from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python port/tools/record_entity_node_ops.py
    .upstream/graphiti/.venv/bin/python port/tools/record_entity_node_ops.py --check

It runs the methods of graphiti_core/driver/neo4j/operations/entity_node_ops.py that reach the
executor and stop at no function of node_db_queries.py or record_parsers.py: delete,
delete_by_group_id, delete_by_uuids, load_embeddings, and load_embeddings_bulk. A fake executor and a
fake transaction record each call: the query text, the parameter keys in order, and each value with
its type. The tool also records the result or the exception, and each log line with its level.
save, save_bulk, get_by_uuid, get_by_uuids, and get_by_group_ids call a skeleton function today, so
they are not recorded.

It writes tests/Port.Evaluation/values/neo4j_entity_node_ops.json. --check writes nothing and exits 1
if the file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream
in .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import json
import logging
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = ROOT / '.upstream' / 'graphiti'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'neo4j_entity_node_ops.json'

UUIDS = ['u-1', 'Zoë 東京 😀']
GROUP_IDS = ['g-1', 'grüp 東京']
UUID_LISTS = [[], ['a'], ['a', 'a'], ['a', 'b'], ['Zoë', '東京 😀']]
BATCH_SIZES = [100, 0]
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


def cases():
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
        out.append({'method': 'load_embeddings', 'uuid': uuid, 'records': []})
        out.append({'method': 'load_embeddings', 'uuid': uuid, 'records': [{}]})
        out.append({'method': 'load_embeddings', 'uuid': uuid, 'records': [{'name_embedding': [0.5]}, {'name_embedding': ['x']}]})
        for embedding in EMBEDDINGS:
            out.append({'method': 'load_embeddings', 'uuid': uuid, 'records': [{'name_embedding': embedding}]})
    nodes = ['n-1', 'Zoë 東京', 'n-1']
    out.append({'method': 'load_embeddings_bulk', 'uuids': [], 'records': []})
    out.append({'method': 'load_embeddings_bulk', 'uuids': ['n-1'], 'records': []})
    out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': []})
    out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'Zoë 東京', 'name_embedding': [2, 3]}]})
    out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'n-1', 'name_embedding': [1.5]}, {'uuid': 'other', 'name_embedding': [9]}]})
    out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'n-1', 'name_embedding': [1]}, {'uuid': 'n-1', 'name_embedding': [2]}]})
    out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'n-1', 'name_embedding': None}]})
    for embedding in EMBEDDINGS:
        out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'Zoë 東京', 'name_embedding': embedding}]})
    out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'n-1', 'name_embedding': [1]}, {'uuid': 'Zoë 東京', 'name_embedding': ['x']}]})
    out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'n-1', 'name_embedding': ['x']}, {'uuid': 'Zoë 東京', 'name_embedding': [7]}]})
    out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'uuid': 'n-1'}]})
    out.append({'method': 'load_embeddings_bulk', 'uuids': nodes, 'records': [{'name_embedding': [1]}]})
    return out


def main() -> int:
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core.driver.neo4j.operations import entity_node_ops
    from graphiti_core.driver.query_executor import QueryExecutor, Transaction
    from graphiti_core.nodes import EntityNode

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

    class FakeExecutor(Recorder, QueryExecutor):
        async def execute_query(self, cypher_query_, **kwargs):
            self.note('executor', cypher_query_, kwargs)
            return self.records, None, None

    class FakeTransaction(Recorder, Transaction):
        async def run(self, query, **kwargs):
            self.note('transaction', query, kwargs)

    class Capture(logging.Handler):
        def __init__(self):
            super().__init__(logging.DEBUG)
            self.lines = []

        def emit(self, record):
            self.lines.append({'level': record.levelname, 'message': record.getMessage()})

    capture = Capture()
    logger = logging.getLogger(entity_node_ops.__name__)
    logger.setLevel(logging.DEBUG)
    logger.propagate = False
    logger.addHandler(capture)
    operations = entity_node_ops.Neo4jEntityNodeOperations()

    results = []
    for case in cases():
        capture.lines.clear()
        executor = FakeExecutor(case.get('records', []))
        transaction = FakeTransaction([]) if case.get('use_tx') else None
        method = case['method']
        nodes = [EntityNode(uuid=uuid, name='n', group_id='g') for uuid in case.get('uuids', [])]
        node = EntityNode(uuid=case['uuid'], name='n', group_id='g') if 'uuid' in case and method != 'delete_by_uuids' else None
        outcome = {}
        try:
            if method == 'delete':
                asyncio.run(operations.delete(executor, node, transaction))
            elif method == 'delete_by_group_id':
                asyncio.run(operations.delete_by_group_id(executor, case['group_id'], transaction, case['batch_size']))
            elif method == 'delete_by_uuids':
                asyncio.run(operations.delete_by_uuids(executor, case['uuids'], transaction, case['batch_size']))
            elif method == 'load_embeddings':
                asyncio.run(operations.load_embeddings(executor, node))
            else:
                asyncio.run(operations.load_embeddings_bulk(executor, nodes))
            outcome['error'] = None
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
        outcome['calls'] = executor.calls + (transaction.calls if transaction else [])
        outcome['logs'] = list(capture.lines)
        if method == 'load_embeddings':
            outcome['embeddings'] = [encode(node.name_embedding)]
        elif method == 'load_embeddings_bulk':
            outcome['embeddings'] = [encode(n.name_embedding) for n in nodes]
        results.append({'input': case, 'output': outcome})

    document = {
        'python': '.'.join(str(part) for part in sys.version_info[:3]),
        'source': 'graphiti_core/driver/neo4j/operations/entity_node_ops.py',
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
