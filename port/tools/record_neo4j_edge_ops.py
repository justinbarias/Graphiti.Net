"""Records what the five Neo4j edge operations classes do, from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python port/tools/record_neo4j_edge_ops.py
    .upstream/graphiti/.venv/bin/python port/tools/record_neo4j_edge_ops.py --check

It runs each method of these classes in graphiti_core/driver/neo4j/operations/:
Neo4jEntityEdgeOperations (entity_edge_ops.py), Neo4jEpisodicEdgeOperations (episodic_edge_ops.py),
Neo4jCommunityEdgeOperations (community_edge_ops.py), Neo4jHasEpisodeEdgeOperations
(has_episode_edge_ops.py), and Neo4jNextEpisodeEdgeOperations (next_episode_edge_ops.py).
A fake executor and a fake transaction record each call: the query text, the parameter keys in order,
and each value with its type. The tool keeps the calls and the log lines in one ordered list, and it
records the result or the exception of each call. Each method runs with and without a transaction,
with each branch of its code, and with the defaults one time.

It writes tests/Port.Evaluation/values/neo4j_edge_ops.json. --check writes nothing and exits 1 if the
file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream. It
reads the upstream folder from the environment variable GRAPHITI_UPSTREAM, then from
.upstream/graphiti in the repository, then from the main checkout when this is a git worktree. It
changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import json
import logging
import os
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'neo4j_edge_ops.json'


def find_upstream() -> Path:
    given = os.environ.get('GRAPHITI_UPSTREAM')
    if given:
        return Path(given)
    local = ROOT / '.upstream' / 'graphiti'
    if local.exists():
        return local
    # A git worktree has no .upstream of its own. The main checkout holds it.
    return ROOT.parents[2] / '.upstream' / 'graphiti'


UPSTREAM = find_upstream()

MODULES = {
    'EntityEdge': ('entity_edge_ops', 'Neo4jEntityEdgeOperations'),
    'EpisodicEdge': ('episodic_edge_ops', 'Neo4jEpisodicEdgeOperations'),
    'CommunityEdge': ('community_edge_ops', 'Neo4jCommunityEdgeOperations'),
    'HasEpisodeEdge': ('has_episode_edge_ops', 'Neo4jHasEpisodeEdgeOperations'),
    'NextEpisodeEdge': ('next_episode_edge_ops', 'Neo4jNextEpisodeEdgeOperations'),
}
CLASSES = list(MODULES)

UUIDS = ['e-1', 'Zoë 東京 😀']
UUID_LISTS = [[], ['a'], ['a', 'a'], ['Zoë', '東京 😀']]
GROUP_ID_LISTS = [['g'], ['g', 'grüp 東京'], [''], []]
PAGES = [(None, None), (5, None), (None, ''), (3, 'c-1'), (0, ''), (0, 'c-1')]
BATCH_SIZES = [None, 0]

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
    'attributes': {'role': 'eng', 'n': [1, 2.5], 'flag': True, 'none': None},
}
ENTITY_EDGE_VARIANTS = [
    ENTITY_EDGE,
    {**ENTITY_EDGE, 'uuid': 'Zoë 東京 😀', 'group_id': ''},
    {
        **ENTITY_EDGE,
        'fact_embedding': None,
        'episodes': [],
        'attributes': {},
        'valid_at': '2026-01-02T03:04:05+05:30',
        'expired_at': '2026-03-01T00:00:00',
    },
    {**ENTITY_EDGE, 'attributes': {'uuid': 'override', 'name': 'x', 'extra': {'nested': [1, {'deep': None}]}}},
]
BASE_EDGE_VARIANTS = [
    BASE_EDGE,
    {**BASE_EDGE, 'uuid': 'Zoë 東京 😀', 'group_id': '', 'created_at': '2026-01-02T03:04:05.123456+05:30'},
    {**BASE_EDGE, 'created_at': '2026-01-02T03:04:05'},
]


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


def record_sets(cls_name: str) -> dict[str, list[dict]]:
    """Named lists of records that a get method can receive."""
    make = entity_record if cls_name == 'EntityEdge' else base_record
    out = {
        'none': [],
        'one': [make('r-1', 'g')],
        'two': [make('r-1', 'g'), make('r-2', 'other')],
        'naive_date': [{**make('r-1', 'g'), 'created_at': '2026-01-01T00:00:00'}],
        'z_date': [{**make('r-1', 'g'), 'created_at': '2026-01-01T00:00:00Z'}],
        'no_uuid': [{k: v for k, v in make('r-1', 'g').items() if k != 'uuid'}],
        'no_created_at': [{k: v for k, v in make('r-1', 'g').items() if k != 'created_at'}],
        'null_created_at': [{**make('r-1', 'g'), 'created_at': None}],
        'bad_date': [{**make('r-1', 'g'), 'created_at': 'garbage'}],
        # two defects: the parser reads uuid first, so the error is a KeyError and not the error of the date
        'no_uuid_bad_date': [{**{k: v for k, v in make('r-1', 'g').items() if k != 'uuid'}, 'created_at': 'not-a-date'}],
        'int_group_id': [{**make('r-1', 'g'), 'group_id': 5}],
        'second_bad': [make('r-1', 'g'), {**make('r-2', 'g'), 'uuid': None}],
    }
    if cls_name == 'EntityEdge':
        out['no_attributes'] = [{k: v for k, v in entity_record('r-1', 'g').items() if k != 'attributes'}]
        out['null_attributes'] = [{**entity_record('r-1', 'g'), 'attributes': None}]
        out['no_episodes'] = [{k: v for k, v in entity_record('r-1', 'g').items() if k != 'episodes'}]
        out['no_embedding'] = [{k: v for k, v in entity_record('r-1', 'g').items() if k != 'fact_embedding'}]
        out['null_embedding'] = [{**entity_record('r-1', 'g'), 'fact_embedding': None}]
        out['reference_time'] = [{**entity_record('r-1', 'g'), 'reference_time': '2026-01-03T00:00:00+00:00'}]
    return out


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
    if isinstance(value, (list, tuple, set)):
        return {'type': 'list', 'items': [encode(item) for item in value]}
    if isinstance(value, dict):
        return {'type': 'dict', 'items': [[key, encode(item)] for key, item in value.items()]}
    if hasattr(value, 'model_dump') and hasattr(value, 'source_node_uuid'):
        return {'type': 'edge', 'class': type(value).__name__, 'dump': encode(value.model_dump())}
    return {'type': type(value).__name__}


def edge_variants(cls_name: str) -> list[dict]:
    return ENTITY_EDGE_VARIANTS if cls_name == 'EntityEdge' else BASE_EDGE_VARIANTS


def cases_for(cls_name: str) -> list[dict]:
    out = []
    base = {'cls': cls_name}
    sets = record_sets(cls_name)
    for use_tx in (False, True):
        for edge in edge_variants(cls_name):
            out.append({**base, 'method': 'save', 'use_tx': use_tx, 'edge': edge})
        for uuid in UUIDS:
            out.append({**base, 'method': 'delete', 'use_tx': use_tx, 'edge': {**edge_variants(cls_name)[0], 'uuid': uuid}})
        for uuids in UUID_LISTS:
            out.append({**base, 'method': 'delete_by_uuids', 'use_tx': use_tx, 'uuids': uuids})
        edges = [edge_variants(cls_name)[0], {**edge_variants(cls_name)[0], 'uuid': 'e-2', 'group_id': 'other'}]
        if cls_name != 'CommunityEdge':
            for batch_size in BATCH_SIZES:
                for count in (0, 1, 2):
                    case = {**base, 'method': 'save_bulk', 'use_tx': use_tx, 'edges': edges[:count]}
                    if batch_size is not None:
                        case['batch_size'] = batch_size
                    out.append(case)
            if cls_name == 'EntityEdge':
                out.append({**base, 'method': 'save_bulk', 'use_tx': use_tx, 'edges': ENTITY_EDGE_VARIANTS})
            else:
                out.append({**base, 'method': 'save_bulk', 'use_tx': use_tx, 'edges': BASE_EDGE_VARIANTS})
    for name, records in sets.items():
        for uuid in UUIDS if name in ('none', 'one', 'two') else ['e-1']:
            out.append({**base, 'method': 'get_by_uuid', 'uuid': uuid, 'records': records})
        for uuids in UUID_LISTS if name in ('none', 'one', 'two') else [['a']]:
            out.append({**base, 'method': 'get_by_uuids', 'uuids': uuids, 'records': records})
        out.append({**base, 'method': 'get_by_group_ids', 'group_ids': ['g'], 'limit': 2, 'uuid_cursor': 'c', 'records': records})
    for group_ids in GROUP_ID_LISTS:
        for limit, cursor in PAGES:
            for count in (0, 2) if group_ids == ['g'] else (0,):
                out.append(
                    {
                        **base,
                        'method': 'get_by_group_ids',
                        'group_ids': group_ids,
                        'limit': limit,
                        'uuid_cursor': cursor,
                        'records': sets['two'][:count],
                    }
                )
    out.append({**base, 'method': 'get_by_group_ids', 'group_ids': ['g'], 'defaults': True, 'records': sets['two']})
    if cls_name == 'EntityEdge':
        out.extend(entity_only_cases(sets))
    return out


def entity_only_cases(sets: dict[str, list[dict]]) -> list[dict]:
    base = {'cls': 'EntityEdge'}
    out = []
    for name in ('none', 'one', 'two', 'null_attributes', 'no_attributes'):
        for source, target in (('a 😀', 'b'), ('', '')):
            out.append({**base, 'method': 'get_between_nodes', 'source': source, 'target': target, 'records': sets[name]})
        for node_uuid in ('n-1', 'Zoë 東京'):
            out.append({**base, 'method': 'get_by_node_uuid', 'node_uuid': node_uuid, 'records': sets[name]})
    edge = ENTITY_EDGE
    loads = [
        [],
        [{}],
        [{'fact_embedding': None}],
        [{'fact_embedding': []}],
        [{'fact_embedding': [0.5, -2.25]}, {'fact_embedding': [9.0]}],
        [{'fact_embedding': [1e300, -0.0]}],
        [{'fact_embedding': [0.5]}, {}],
    ]
    for uuid in UUIDS:
        for records in loads:
            out.append({**base, 'method': 'load_embeddings', 'edge': {**edge, 'uuid': uuid}, 'records': records})
    uuids = ['e-1', 'Zoë 東京', 'e-1']
    bulk = [
        [],
        [{'uuid': 'Zoë 東京', 'fact_embedding': [2.5, 3.0]}],
        [{'uuid': 'e-1', 'fact_embedding': [1.5]}, {'uuid': 'other', 'fact_embedding': [9.0]}],
        [{'uuid': 'e-1', 'fact_embedding': [1.0]}, {'uuid': 'e-1', 'fact_embedding': [2.0]}],
        [{'uuid': 'e-1', 'fact_embedding': None}],
        [{'uuid': 'e-1', 'fact_embedding': []}],
        [{'uuid': 'e-1'}],
        [{'fact_embedding': [1.0]}],
        [{'uuid': 'e-1', 'fact_embedding': [1.0]}, {'uuid': 'Zoë 東京'}],
        [{'uuid': 'other', 'fact_embedding': [1.0]}, {'uuid': 'other'}],
    ]
    for records in bulk:
        for batch_size in BATCH_SIZES:
            case = {**base, 'method': 'load_embeddings_bulk', 'uuids': uuids, 'edge': edge, 'records': records}
            if batch_size is not None:
                case['batch_size'] = batch_size
            out.append(case)
    out.append({**base, 'method': 'load_embeddings_bulk', 'uuids': [], 'edge': edge, 'records': []})
    out.append({**base, 'method': 'load_embeddings_bulk', 'uuids': [], 'edge': edge, 'records': bulk[1]})
    return out


def cases() -> list[dict]:
    out = []
    for cls_name in CLASSES:
        out.extend(cases_for(cls_name))
    return out


def main() -> int:
    sys.path.insert(0, str(UPSTREAM))
    import importlib

    from graphiti_core import edges as edges_module
    from graphiti_core.driver.query_executor import QueryExecutor, Transaction

    events: list[dict] = []

    class FakeExecutor(QueryExecutor):
        def __init__(self, records):
            self.records = records

        async def execute_query(self, cypher_query_, **kwargs):
            await asyncio.sleep(0)
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
            await asyncio.sleep(0)
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

    capture = Capture()
    operations = {}
    for cls_name, (module_name, class_name) in MODULES.items():
        module = importlib.import_module(f'graphiti_core.driver.neo4j.operations.{module_name}')
        logger = logging.getLogger(module.__name__)
        logger.setLevel(logging.DEBUG)
        logger.propagate = False
        logger.addHandler(capture)
        operations[cls_name] = getattr(module, class_name)()

    def make_edge(cls_name, data):
        return getattr(edges_module, cls_name)(**data)

    async def run(ops, case, state):
        method = case['method']
        executor = FakeExecutor(case.get('records', []))
        tx = FakeTransaction() if case.get('use_tx') else None
        cls_name = case['cls']
        edge = make_edge(cls_name, case['edge']) if 'edge' in case else None
        state['edge'] = edge
        if method == 'save' or method == 'delete':
            return await getattr(ops, method)(executor, edge, tx), edge, None
        if method == 'save_bulk':
            edge_list = [make_edge(cls_name, e) for e in case['edges']]
            if 'batch_size' in case:
                return await ops.save_bulk(executor, edge_list, tx, case['batch_size']), None, None
            return await ops.save_bulk(executor, edge_list, tx), None, None
        if method == 'delete_by_uuids':
            return await ops.delete_by_uuids(executor, case['uuids'], tx), None, None
        if method == 'get_by_uuid':
            return await ops.get_by_uuid(executor, case['uuid']), None, None
        if method == 'get_by_uuids':
            return await ops.get_by_uuids(executor, case['uuids']), None, None
        if method == 'get_by_group_ids':
            if case.get('defaults'):
                return await ops.get_by_group_ids(executor, case['group_ids']), None, None
            return await ops.get_by_group_ids(executor, case['group_ids'], case['limit'], case['uuid_cursor']), None, None
        if method == 'get_between_nodes':
            return await ops.get_between_nodes(executor, case['source'], case['target']), None, None
        if method == 'get_by_node_uuid':
            return await ops.get_by_node_uuid(executor, case['node_uuid']), None, None
        if method == 'load_embeddings':
            return await ops.load_embeddings(executor, edge), edge, None
        if method == 'load_embeddings_bulk':
            edge_list = [make_edge(cls_name, {**case['edge'], 'uuid': u}) for u in case['uuids']]
            state['edge_list'] = edge_list
            if 'batch_size' in case:
                result = await ops.load_embeddings_bulk(executor, edge_list, case['batch_size'])
            else:
                result = await ops.load_embeddings_bulk(executor, edge_list)
            return result, None, edge_list
        raise ValueError(method)

    results = []
    for case in cases():
        events.clear()
        outcome = {}
        state = {'edge': None, 'edge_list': None}
        result = None
        try:
            result = asyncio.run(run(operations[case['cls']], case, state))[0]
            outcome['error'] = None
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
        outcome['events'] = list(events)
        outcome['result'] = encode(result)
        if case['method'] == 'load_embeddings':
            outcome['embeddings'] = [encode(state['edge'].fact_embedding)]
        elif case['method'] == 'load_embeddings_bulk':
            outcome['embeddings'] = [encode(e.fact_embedding) for e in state['edge_list'] or []]
        results.append({'input': case, 'output': outcome})

    head = json.dumps(
        {'python': '.'.join(str(part) for part in sys.version_info[:3]), 'source': 'graphiti_core/driver/neo4j/operations/*_edge_ops.py'},
        ensure_ascii=True,
    )
    # One case on each line keeps the file small and each change readable.
    text = head[:-1] + ', "cases": [\n' + ',\n'.join(json.dumps(r, ensure_ascii=True) for r in results) + '\n]}\n'
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
