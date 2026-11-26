"""Records what the edge classes of graphiti_core/edges.py do, from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python port/tools/record_edges.py
    .upstream/graphiti/.venv/bin/python port/tools/record_edges.py --check

It runs each method of the five edge classes that reaches the driver or the embedder, and
create_entity_edge_embeddings and get_entity_edge_from_record. A fake driver records each
execute_query call: the query text, the parameter keys in order, and each value with its type. The
graph operations interface of the driver is one of three:

- none: the attribute is None, so the method runs its own query.
- raise: the base GraphOperationsInterface, whose methods raise NotImplementedError, so the method
  falls back to its own query.
- null: a subclass whose edge_get_by_uuid and edge_get_by_uuids return None.
- return: a subclass that records each call (the operation name and its arguments after the driver)
  and returns fixed edges.

The tool also records the result or the exception, each log line with its level, the state of the
edge after the call, and each embedder call. The clock of edges.py is a constant, so the time in the
log line of generate_embedding is 0.0 ms. The test replaces the time in both texts.

It writes tests/Port.Evaluation/values/edges.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The tool needs Python 3.12 and the pinned upstream in
.upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import json
import logging
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = ROOT / '.upstream' / 'graphiti'
if not UPSTREAM.exists():
    # A git worktree has no .upstream of its own. The main checkout holds it.
    UPSTREAM = ROOT.parents[2] / '.upstream' / 'graphiti'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'edges.json'

PROVIDERS = ['neo4j', 'falkordb', 'neptune', 'relate']
MODES = ['none', 'raise', 'return']
UUIDS = ['e-1', 'Zoë 東京 😀']
UUID_LISTS = [[], ['e-1'], ['e-1', 'Zoë 東京 😀']]
GROUP_ID_LISTS = [['g'], ['g', 'grüp 東京']]
PAGES = [(None, None), (5, None), (None, ''), (3, 'c-1'), (0, ''), (0, 'c-1')]
ENTITY_PAGES = [(None, None), (0, ''), (0, 'c-1')]
CLASSES = ['EpisodicEdge', 'EntityEdge', 'CommunityEdge', 'HasEpisodeEdge', 'NextEpisodeEdge']

BASE_EDGE = {
    'uuid': 'e-1',
    'group_id': 'g',
    'source_node_uuid': 'src',
    'target_node_uuid': 'tgt',
    'created_at': '2026-01-01T00:00:00+00:00',
}
ENTITY_EDGE = {
    **BASE_EDGE,
    'name': 'WORKS_AT',
    'fact': 'Ana works\nat Acme 😀',
    'fact_embedding': [0.25, -1.5],
    'episodes': ['ep-1', 'ep-2'],
    'expired_at': None,
    'valid_at': '2026-01-02T03:04:05.123456+00:00',
    'invalid_at': None,
    'reference_time': '2026-01-01T00:00:00',
    'attributes': {'uuid': 'kept-out', 'role': 'eng', 'n': [1, 2.5], 'flag': True, 'none': None},
}


def base_record(uuid: str, group_id: str) -> dict:
    return {
        'uuid': uuid,
        'group_id': group_id,
        'source_node_uuid': 'src',
        'target_node_uuid': 'tgt',
        'created_at': '2026-01-01T00:00:00+00:00',
    }


def entity_record(uuid: str, group_id: str) -> dict:
    return {
        **base_record(uuid, group_id),
        'name': 'R',
        'fact': 'f',
        'fact_embedding': [0.5],
        'episodes': ['ep-1'],
        'expired_at': None,
        'valid_at': '2026-01-01T00:00:00+00:00',
        'invalid_at': None,
        'reference_time': None,
        'attributes': {'uuid': 'x', 'fact': 'x', 'created_at': 'x', 'custom': 1},
    }


def records_for(cls_name: str, count: int) -> list[dict]:
    make = entity_record if cls_name == 'EntityEdge' else base_record
    return [make('r-1', 'g'), make('r-2', 'other')][:count]


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
    if isinstance(value, (list, tuple)):
        return {'type': 'list', 'items': [encode(item) for item in value]}
    if isinstance(value, dict):
        return {'type': 'dict', 'items': [[key, encode(item)] for key, item in value.items()]}
    if isinstance(value, type):
        return {'type': 'class', 'value': value.__name__}
    if hasattr(value, 'model_dump') and hasattr(value, 'source_node_uuid'):
        return {'type': 'edge', 'class': type(value).__name__, 'dump': encode(value.model_dump())}
    return {'type': type(value).__name__}


def instance_cases(cls_name: str) -> list[dict]:
    edge = ENTITY_EDGE if cls_name == 'EntityEdge' else BASE_EDGE
    out = []
    for mode in MODES:
        for uuid in UUIDS:
            out.append({'cls': cls_name, 'method': 'save', 'mode': mode, 'provider': 'neo4j', 'edge': {**edge, 'uuid': uuid}})
            out.append({'cls': cls_name, 'method': 'delete', 'mode': mode, 'provider': 'neo4j', 'edge': {**edge, 'uuid': uuid}})
    for provider in PROVIDERS:
        out.append({'cls': cls_name, 'method': 'save', 'mode': 'none', 'provider': provider, 'edge': edge})
    return out


def class_cases(cls_name: str) -> list[dict]:
    out = []
    for mode in MODES:
        # The records do not reach a method whose operation returns, so that mode needs one count.
        for count in (0, 1, 2) if mode != 'return' else (0,):
            for uuid in UUIDS:
                out.append({'cls': cls_name, 'method': 'get_by_uuid', 'mode': mode, 'provider': 'neo4j', 'uuid': uuid, 'records': count})
            for uuids in UUID_LISTS:
                out.append({'cls': cls_name, 'method': 'get_by_uuids', 'mode': mode, 'provider': 'neo4j', 'uuids': uuids, 'records': count})
            for group_ids in GROUP_ID_LISTS:
                for limit, cursor in PAGES:
                    out.append(
                        {
                            'cls': cls_name,
                            'method': 'get_by_group_ids',
                            'mode': mode,
                            'provider': 'neo4j',
                            'group_ids': group_ids,
                            'limit': limit,
                            'uuid_cursor': cursor,
                            'records': count,
                        }
                    )
    return out


def entity_cases() -> list[dict]:
    out = []
    for mode in MODES:
        for provider in PROVIDERS:
            for count in (0, 2) if mode != 'return' else (0,):
                for group_id in (None, 'g', 'other', ''):
                    out.append({'cls': 'EntityEdge', 'method': 'get_by_uuid', 'mode': mode, 'provider': provider, 'uuid': 'e-1', 'group_id': group_id, 'records': count})
                    for uuids in UUID_LISTS:
                        out.append({'cls': 'EntityEdge', 'method': 'get_by_uuids', 'mode': mode, 'provider': provider, 'uuids': uuids, 'group_id': group_id, 'records': count})
                out.append({'cls': 'EntityEdge', 'method': 'get_between_nodes', 'mode': mode, 'provider': provider, 'source': 'a 😀', 'target': 'b', 'records': count})
                out.append({'cls': 'EntityEdge', 'method': 'get_by_node_uuid', 'mode': mode, 'provider': provider, 'node_uuid': 'n-1', 'records': count})
                for with_embeddings in (False, True):
                    out.append(
                        {
                            'cls': 'EntityEdge',
                            'method': 'get_by_group_ids',
                            'mode': mode,
                            'provider': provider,
                            'group_ids': ['g'],
                            'limit': 2,
                            'uuid_cursor': 'c',
                            'with_embeddings': with_embeddings,
                            'records': count,
                        }
                    )
                for group_ids in (['g'], ['']):
                    for limit, cursor in ENTITY_PAGES if count == 0 and mode == 'none' else []:
                        for with_embeddings in (False, True):
                            out.append(
                                {
                                    'cls': 'EntityEdge',
                                    'method': 'get_by_group_ids',
                                    'mode': mode,
                                    'provider': provider,
                                    'group_ids': group_ids,
                                    'limit': limit,
                                    'uuid_cursor': cursor,
                                    'with_embeddings': with_embeddings,
                                    'records': count,
                                }
                            )
                if mode == 'none' and count == 2:
                    # One call with no with_embeddings: the default of the method.
                    out.append(
                        {
                            'cls': 'EntityEdge',
                            'method': 'get_by_group_ids',
                            'mode': mode,
                            'provider': provider,
                            'group_ids': ['g'],
                            'limit': 2,
                            'uuid_cursor': 'c',
                            'with_embeddings': 'default',
                            'records': count,
                        }
                    )
            out.append({'cls': 'EntityEdge', 'method': 'save', 'mode': mode, 'provider': provider, 'edge': ENTITY_EDGE})
            for load in ([], [{}], [{'fact_embedding': None}], [{'fact_embedding': [0.5, -2.25]}, {'fact_embedding': [9.0]}]):
                out.append({'cls': 'EntityEdge', 'method': 'load_fact_embedding', 'mode': mode, 'provider': provider, 'edge': ENTITY_EDGE, 'load': load})
    # An operation that returns None is not NotImplementedError, so the method returns, or fails on the None.
    out.append({'cls': 'EntityEdge', 'method': 'get_by_uuid', 'mode': 'null', 'provider': 'neo4j', 'uuid': 'e-1', 'group_id': 'g', 'records': 2})
    out.append({'cls': 'EntityEdge', 'method': 'get_by_uuids', 'mode': 'null', 'provider': 'neo4j', 'uuids': ['e-1'], 'group_id': 'g', 'records': 2})
    for fact in ('Ana works\nat Acme 😀', '', '\n\n', 'plain'):
        out.append({'cls': 'EntityEdge', 'method': 'generate_embedding', 'mode': 'none', 'provider': 'neo4j', 'edge': {**ENTITY_EDGE, 'fact': fact}})
    return out


def community_save_cases() -> list[dict]:
    return [{'cls': 'CommunityEdge', 'method': 'save', 'mode': 'none', 'provider': p, 'edge': BASE_EDGE} for p in PROVIDERS]


def module_cases() -> list[dict]:
    out = []
    for uuids in UUID_LISTS:
        for mode in MODES:
            out.append({'cls': 'Edge', 'method': 'delete_by_uuids', 'mode': mode, 'provider': 'neo4j', 'uuids': uuids})
    fact_lists = [[], [''], ['a'], ['a', '', 'b 😀'], ['', ''], [' '], ['\t'], ['a', ' ', '\t', '']]
    for facts in fact_lists:
        for delta in (0, -1, 1):
            out.append({'cls': 'Edge', 'method': 'create_entity_edge_embeddings', 'mode': 'none', 'provider': 'neo4j', 'facts': facts, 'delta': delta})
    reserved = {
        'uuid': 'x',
        'source_node_uuid': 'x',
        'target_node_uuid': 'x',
        'fact': 'x',
        'fact_embedding': 'x',
        'name': 'x',
        'group_id': 'x',
        'episodes': 'x',
        'created_at': 'x',
        'expired_at': 'x',
        'valid_at': 'x',
        'invalid_at': 'x',
        'reference_time': 'x',
        'custom': 1,
        'other': [1, 'a'],
    }
    for attributes in ({}, reserved, {'custom': {'nested': True}}):
        out.append({'cls': 'Edge', 'method': 'get_entity_edge_from_record', 'mode': 'none', 'provider': 'neo4j', 'record': {**entity_record('r-1', 'g'), 'attributes': attributes}})
    return out


def value_cases(cls_name: str) -> list[dict]:
    """Equality, hashing, the JSON dump, the copies, and the defaults of one class."""
    edge = ENTITY_EDGE if cls_name == 'EntityEdge' else BASE_EDGE
    other_fields = {**edge, 'group_id': 'h', 'source_node_uuid': 's2', 'target_node_uuid': 't2'}
    if cls_name == 'EntityEdge':
        other_fields = {**other_fields, 'fact': 'another fact', 'name': 'KNOWS'}
    base = {'cls': cls_name, 'mode': 'none', 'provider': 'neo4j'}
    out = [
        {
            **base,
            'method': 'equality',
            'edge': edge,
            'same_uuid_edge': other_fields,
            'other_uuid_edge': {**edge, 'uuid': 'E-1'},
        },
        {**base, 'method': 'model_copy', 'edge': edge},
    ]
    offset = {**edge, 'created_at': '2026-01-02T03:04:05.123456+05:30', 'uuid': 'Zoë 東京 😀'}
    for variant in (edge, offset):
        out.append({**base, 'method': 'model_dump_json', 'edge': variant})
    if cls_name == 'EntityEdge':
        out.append({**base, 'method': 'model_validate', 'edge': edge})
        bare = {k: v for k, v in edge.items() if k not in ('episodes', 'attributes', 'fact_embedding')}
        out.append({**base, 'method': 'defaults', 'bare': bare})
    return out


def cases() -> list[dict]:
    out = []
    for cls_name in CLASSES:
        out.extend(value_cases(cls_name))
    for cls_name in CLASSES:
        out.extend(instance_cases(cls_name))
        if cls_name != 'EntityEdge':
            out.extend(class_cases(cls_name))
    out.extend(entity_cases())
    out.extend(community_save_cases())
    out.extend(module_cases())
    return out


OPERATIONS = [
    'edge_save',
    'edge_delete',
    'edge_delete_by_uuids',
    'edge_get_by_uuid',
    'edge_get_by_uuids',
    'edge_get_by_group_ids',
    'edge_load_embeddings',
    'edge_get_between_nodes',
    'edge_get_by_node_uuid',
    'episodic_edge_save',
    'episodic_edge_get_by_uuid',
    'episodic_edge_get_by_uuids',
    'episodic_edge_get_by_group_ids',
    'community_edge_save',
    'community_edge_get_by_uuid',
    'community_edge_get_by_uuids',
    'community_edge_get_by_group_ids',
    'has_episode_edge_save',
    'has_episode_edge_delete',
    'has_episode_edge_get_by_uuid',
    'has_episode_edge_get_by_uuids',
    'has_episode_edge_get_by_group_ids',
    'next_episode_edge_save',
    'next_episode_edge_delete',
    'next_episode_edge_get_by_uuid',
    'next_episode_edge_get_by_uuids',
    'next_episode_edge_get_by_group_ids',
]


def main() -> int:
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core import edges as edges_module
    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.driver.graph_operations.graph_operations import GraphOperationsInterface
    from graphiti_core.embedder import EmbedderClient

    classes = {name: getattr(edges_module, name) for name in CLASSES}
    edges_module.time = lambda: 100.0

    class FakeDriver:
        def __init__(self, provider, operations, records):
            self.provider = provider
            self.graph_operations_interface = operations
            self.records = records
            self.calls = []

        async def execute_query(self, cypher_query_, **kwargs):
            self.calls.append({'query': cypher_query_, 'keys': list(kwargs), 'values': [encode(v) for v in kwargs.values()]})
            return self.records, None, None

    def returning_operations(calls, cls_name):
        cls = classes.get(cls_name, classes['EntityEdge'])
        data = ENTITY_EDGE if cls is classes['EntityEdge'] else BASE_EDGE
        one = cls(**{**data, 'uuid': 'ops-1'})
        two = cls(**{**data, 'uuid': 'ops-2', 'group_id': 'other'})

        def make(name):
            async def operation(self, *args):
                calls.append({'op': name, 'args': [encode(a) for a in args if not isinstance(a, FakeDriver)]})
                if name.endswith('_get_by_uuid'):
                    return one
                if '_get_' in name:
                    return [one, two]
                return None

            return operation

        namespace = {name: make(name) for name in OPERATIONS}
        return type('ReturningOperations', (GraphOperationsInterface,), namespace)()

    def null_operations():
        async def operation(self, *args):
            return None

        return type('NullOperations', (GraphOperationsInterface,), {'edge_get_by_uuid': operation, 'edge_get_by_uuids': operation})()

    class FakeEmbedder(EmbedderClient):
        def __init__(self, delta=0):
            self.calls = []
            self.delta = delta

        async def create(self, input_data):
            self.calls.append({'op': 'create', 'input': list(input_data)})
            return [float(len(input_data[0])), 0.5]

        async def create_batch(self, input_data_list):
            self.calls.append({'op': 'create_batch', 'input': list(input_data_list)})
            out = [[float(i), float(len(text))] for i, text in enumerate(input_data_list)]
            if self.delta < 0:
                out = out[: len(out) + self.delta]
            out.extend([[9.0]] * max(self.delta, 0))
            return out

    class Capture(logging.Handler):
        def __init__(self):
            super().__init__(logging.DEBUG)
            self.lines = []

        def emit(self, record):
            self.lines.append({'level': record.levelname, 'message': record.getMessage()})

    capture = Capture()
    logger = logging.getLogger(edges_module.__name__)
    logger.setLevel(logging.DEBUG)
    logger.propagate = False
    logger.addHandler(capture)

    results = []
    for case in cases():
        capture.lines.clear()
        op_calls = []
        mode = case['mode']
        operations = None
        if mode == 'raise':
            operations = GraphOperationsInterface()
        elif mode == 'return':
            operations = returning_operations(op_calls, case['cls'])
        elif mode == 'null':
            operations = null_operations()
        if 'load' in case:
            records = json.loads(json.dumps(case['load']))
        else:
            records = records_for(case['cls'], case.get('records', 0))
        driver = FakeDriver(GraphProvider(case['provider']), operations, records)
        embedder = FakeEmbedder(case.get('delta', 0))
        method = case['method']
        cls = classes.get(case['cls'])
        edge = cls(**json.loads(json.dumps(case['edge']))) if 'edge' in case else None
        entity_edges = [classes['EntityEdge'](**{**ENTITY_EDGE, 'uuid': f'f-{i}', 'fact': fact}) for i, fact in enumerate(case.get('facts', []))]
        record = json.loads(json.dumps(case['record'])) if 'record' in case else None
        outcome = {}
        try:
            if method == 'save':
                value = asyncio.run(edge.save(driver))
            elif method == 'delete':
                value = asyncio.run(edge.delete(driver))
            elif method == 'delete_by_uuids':
                value = asyncio.run(edges_module.Edge.delete_by_uuids(driver, case['uuids']))
            elif method == 'get_by_uuid' and case['cls'] == 'EntityEdge':
                value = asyncio.run(cls.get_by_uuid(driver, case['uuid'], group_id=case['group_id']))
            elif method == 'get_by_uuid':
                value = asyncio.run(cls.get_by_uuid(driver, case['uuid']))
            elif method == 'get_by_uuids' and case['cls'] == 'EntityEdge':
                value = asyncio.run(cls.get_by_uuids(driver, case['uuids'], group_id=case['group_id']))
            elif method == 'get_by_uuids':
                value = asyncio.run(cls.get_by_uuids(driver, case['uuids']))
            elif method == 'get_by_group_ids' and case['cls'] == 'EntityEdge':
                if case['with_embeddings'] == 'default':
                    value = asyncio.run(cls.get_by_group_ids(driver, case['group_ids'], case['limit'], case['uuid_cursor']))
                else:
                    value = asyncio.run(
                        cls.get_by_group_ids(driver, case['group_ids'], case['limit'], case['uuid_cursor'], case['with_embeddings'])
                    )
            elif method == 'get_by_group_ids':
                value = asyncio.run(cls.get_by_group_ids(driver, case['group_ids'], case['limit'], case['uuid_cursor']))
            elif method == 'get_between_nodes':
                value = asyncio.run(cls.get_between_nodes(driver, case['source'], case['target']))
            elif method == 'get_by_node_uuid':
                value = asyncio.run(cls.get_by_node_uuid(driver, case['node_uuid']))
            elif method == 'load_fact_embedding':
                value = asyncio.run(edge.load_fact_embedding(driver))
            elif method == 'generate_embedding':
                value = asyncio.run(edge.generate_embedding(embedder))
            elif method == 'equality':
                same = cls(**json.loads(json.dumps(case['same_uuid_edge'])))
                other = cls(**json.loads(json.dumps(case['other_uuid_edge'])))
                value = {
                    'same_object_eq': edge == edge,
                    'same_object_hash': hash(edge) == hash(edge),
                    'same_uuid_eq': edge == same,
                    'same_uuid_hash': hash(edge) == hash(same),
                    'other_uuid_eq': edge == other,
                    'other_uuid_hash': hash(edge) == hash(other),
                    'str_eq': edge == edge.uuid,
                    'int_eq': edge == 5,
                    'none_eq': edge == None,  # noqa: E711
                    'hash_is_uuid_hash': hash(edge) == hash(edge.uuid),
                }
            elif method == 'model_dump_json':
                value = edge.model_dump(mode='json')
            elif method == 'model_copy':
                value = {}
                for label, copy in (('shallow', edge.model_copy()), ('deep', edge.model_copy(deep=True))):
                    value[label + '_new_object'] = copy is not edge
                    value[label + '_dump'] = copy.model_dump()
                    if case['cls'] == 'EntityEdge':
                        value[label + '_episodes_shared'] = copy.episodes is edge.episodes
                        value[label + '_embedding_shared'] = copy.fact_embedding is edge.fact_embedding
                        value[label + '_attributes_shared'] = copy.attributes is edge.attributes
                        value[label + '_nested_shared'] = copy.attributes['n'] is edge.attributes['n']
            elif method == 'model_validate':
                data = json.loads(json.dumps(case['edge']))
                made = cls(**data)
                value = {
                    'attributes_same_object': made.attributes is data['attributes'],
                    'attributes_equal': made.attributes == data['attributes'],
                    'nested_same_object': made.attributes['n'] is data['attributes']['n'],
                    'episodes_same_object': made.episodes is data['episodes'],
                }
            elif method == 'defaults':
                first = cls(**case['bare'])
                second = cls(**case['bare'])
                value = {
                    'episodes_empty': first.episodes == [],
                    'episodes_shared': first.episodes is second.episodes,
                    'attributes_empty': first.attributes == {},
                    'attributes_shared': first.attributes is second.attributes,
                    'embedding_none': first.fact_embedding is None,
                }
            elif method == 'create_entity_edge_embeddings':
                value = asyncio.run(edges_module.create_entity_edge_embeddings(embedder, entity_edges))
            else:
                value = edges_module.get_entity_edge_from_record(record, driver.provider)
            outcome['error'] = None
            outcome['result'] = encode(value)
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
            outcome['result'] = None
        outcome['calls'] = driver.calls
        outcome['operations'] = op_calls
        outcome['embedder'] = embedder.calls
        outcome['logs'] = list(capture.lines)
        if edge is not None:
            outcome['edge'] = encode(edge.model_dump())
        if entity_edges:
            outcome['edges'] = [encode(e.fact_embedding) for e in entity_edges]
        if record is not None:
            outcome['record_attributes'] = encode(record['attributes'])
        results.append({'input': case, 'output': outcome})

    document = {
        'python': '.'.join(str(part) for part in sys.version_info[:3]),
        'source': 'graphiti_core/edges.py',
        'base_edge': BASE_EDGE,
        'entity_edge': ENTITY_EDGE,
        'cases': results,
    }
    text = json.dumps(document, ensure_ascii=True, separators=(',', ':')) + '\n'
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
