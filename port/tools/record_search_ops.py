"""Records what Neo4jSearchOperations does, from real Python (port/RULES.md, R23, R25, GAPS F21).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_search_ops.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_search_ops.py --check

It runs each method of graphiti_core/driver/neo4j/operations/search_ops.py and the module function
_build_neo4j_fulltext_query. A fake executor records each call: the query text, the parameter keys in
order, and each value with its type. It returns the records that the case gives for that call, in call
order. The tool also records the result or the exception, and each log line of graphiti_core with its
level, in one ordered list with the calls.

The cases reach each branch: each search method, each filter of SearchFilters, group ids that are None,
empty, one, two, the empty text, and not valid, a limit of 0, a query that is too long, a BFS depth of 0,
the source and target filters with and without group ids (defect U9), a center node in and out of the
list, a score of 0, a min_score that removes each node, and an empty result.

It writes tests/Port.Evaluation/values/neo4j_search_ops.json. --check writes nothing and exits 1 if the
file differs from what the run would write. The environment variable GRAPHITI_UPSTREAM names the upstream
folder, for a worktree that has no .upstream. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import copy
import json
import logging
import os
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ['GRAPHITI_UPSTREAM']) if 'GRAPHITI_UPSTREAM' in os.environ else ROOT / '.upstream' / 'graphiti'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'neo4j_search_ops.json'

D1 = '2025-01-02T03:04:05+00:00'
D2 = '2024-06-30T12:00:00'

FILTERS = {
    'default': {},
    'node_labels': {'node_labels': ['Person', 'Org']},
    'edge_types': {'edge_types': ['KNOWS', 'LIKES']},
    'edge_uuids': {'edge_uuids': ['e-1']},
    'edge_source_node_uuids': {'edge_source_node_uuids': ['s-1', 's-2']},
    'edge_target_node_uuids': {'edge_target_node_uuids': ['t-1']},
    'valid_at': {
        'valid_at': [
            [{'date': D1, 'comparison_operator': '>'}, {'comparison_operator': 'IS NULL'}],
            [{'date': D2, 'comparison_operator': '<='}],
        ]
    },
    'invalid_at': {'invalid_at': [[{'date': D1, 'comparison_operator': '='}]]},
    'created_at': {'created_at': [[{'date': D2, 'comparison_operator': '<'}], [{'comparison_operator': 'IS NOT NULL'}]]},
    'expired_at': {'expired_at': [[{'comparison_operator': 'IS NOT NULL'}]]},
    'property_filters': {'property_filters': [{'property_name': 'age', 'property_value': 3, 'comparison_operator': '>'}]},
}
FILTERS['all'] = {k: v for spec in FILTERS.values() for k, v in spec.items()}

GROUPS = [None, [], ['g-1'], ['g-1', 'g_2'], [''], ['bad"group']]
LONG_QUERY = ' '.join(['w'] * 128)

ENTITY = {
    'uuid': 'n-1',
    'name': 'Alice',
    'group_id': 'g-1',
    'labels': ['Entity', 'Person', 'Entity_g1'],
    'created_at': D1,
    'summary': 'Zoë 東京',
    'name_embedding': [0.5, 1],
    'attributes': {'uuid': 'n-1', 'age': 3, 'name': 'Alice'},
}
ENTITY2 = dict(ENTITY, uuid='n-2', name='Bob', labels=['Entity'], attributes={})
EDGE = {
    'uuid': 'e-1',
    'source_node_uuid': 'n-1',
    'target_node_uuid': 'n-2',
    'fact': 'Alice knows Bob',
    'fact_embedding': None,
    'name': 'KNOWS',
    'group_id': 'g-1',
    'episodes': ['ep-1'],
    'created_at': D1,
    'expired_at': None,
    'valid_at': D2,
    'invalid_at': None,
    'attributes': {'fact': 'x', 'weight': 2.5},
}
EPISODE = {
    'content': 'hello',
    'created_at': D1,
    'valid_at': D2,
    'uuid': 'ep-1',
    'group_id': 'g-1',
    'source': 'text',
    'name': 'episode one',
    'source_description': 'chat',
    'entity_edges': ['e-1'],
}
COMMUNITY = {
    'uuid': 'c-1',
    'name': 'Team',
    'group_id': 'g-1',
    'name_embedding': [0.25],
    'created_at': D1,
    'summary': 'a team',
}


def entity(uuid):
    return dict(ENTITY, uuid=uuid, name='name ' + uuid, labels=['Entity'], attributes={})


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
    return {'type': type(value).__name__}


def encode_item(item):
    out = {'class': type(item).__name__, 'uuid': item.uuid, 'name': item.name, 'group_id': item.group_id}
    if type(item).__name__ == 'EntityNode':
        out['labels'] = list(item.labels)
    return out


def search_cases(method, query_arg, records, filtered=True, depth=None):
    """The branches of one search method."""
    out = []

    def case(**kwargs):
        args = dict(query_arg)
        if depth is not None:
            args['max_depth'] = depth
        args.update(kwargs)
        return {'method': method, 'args': args, 'responses': []}

    base = case()
    out.append(base)
    out.append(dict(case(), responses=[records]))
    if filtered:
        for name in FILTERS:
            out.append(case(filter=name))
    for groups in GROUPS:
        out.append(case(group_ids=groups))
    out.append(case(limit=0))
    out.append(dict(case(group_ids=['g-1', 'g_2'], limit=3, **({'filter': 'all'} if filtered else {})), responses=[records]))
    return out


def cases():
    out = []
    fulltext = {'query': 'alice & bob'}
    vector = {'search_vector': [0.1, 0.25, 1.0]}
    out += search_cases('node_fulltext_search', fulltext, [ENTITY, ENTITY2])
    out += search_cases('node_similarity_search', vector, [ENTITY])
    out += search_cases('node_bfs_search', {'origin_uuids': ['n-1', 'n-2']}, [ENTITY], depth=2)
    out += search_cases('edge_fulltext_search', fulltext, [EDGE])
    out += search_cases('edge_similarity_search', dict(vector, source_node_uuid=None, target_node_uuid=None), [EDGE])
    out += search_cases('edge_bfs_search', {'origin_uuids': ['n-1']}, [EDGE], depth=3)
    out += search_cases('episode_fulltext_search', fulltext, [EPISODE])
    out += search_cases('community_fulltext_search', fulltext, [COMMUNITY], filtered=False)
    out += search_cases('community_similarity_search', vector, [COMMUNITY], filtered=False)

    for method in ('node_fulltext_search', 'edge_fulltext_search', 'episode_fulltext_search', 'community_fulltext_search'):
        out.append({'method': method, 'args': {'query': LONG_QUERY}, 'responses': []})
        out.append({'method': method, 'args': {'query': ''}, 'responses': []})
        out.append({'method': method, 'args': {'query': ' '.join(['w'] * 126), 'group_ids': ['g']}, 'responses': []})
        out.append({'method': method, 'args': {'query': ' '.join(['w'] * 127), 'group_ids': ['g']}, 'responses': []})
    for method in ('node_similarity_search', 'edge_similarity_search', 'community_similarity_search'):
        args = {'search_vector': [], 'min_score': 0.0}
        if method == 'edge_similarity_search':
            args.update(source_node_uuid=None, target_node_uuid=None)
        out.append({'method': method, 'args': args, 'responses': []})
    for source, target in (('s-1', None), (None, 't-1'), ('s-1', 't-1')):
        for groups in (None, ['g-1']):
            out.append(
                {
                    'method': 'edge_similarity_search',
                    'args': {'search_vector': [0.5], 'source_node_uuid': source, 'target_node_uuid': target, 'group_ids': groups},
                    'responses': [],
                }
            )
    for depth in (0, 1, 5):
        out.append({'method': 'node_bfs_search', 'args': {'origin_uuids': ['n-1'], 'max_depth': depth}, 'responses': []})
        out.append({'method': 'edge_bfs_search', 'args': {'origin_uuids': ['n-1'], 'max_depth': depth}, 'responses': []})
    out.append({'method': 'node_bfs_search', 'args': {'origin_uuids': [], 'max_depth': 2}, 'responses': []})
    out.append({'method': 'edge_bfs_search', 'args': {'origin_uuids': [], 'max_depth': 2}, 'responses': []})
    out.append({'method': 'node_bfs_search', 'args': {'origin_uuids': ['n-1'], 'max_depth': -1}, 'responses': []})
    # A record with no attributes key raises KeyError in the parser.
    out.append({'method': 'node_fulltext_search', 'args': {'query': 'x'}, 'responses': [[{'uuid': 'n-1'}]]})
    # An episode with no created_at raises ValueError in the parser.
    out.append({'method': 'episode_fulltext_search', 'args': {'query': 'x'}, 'responses': [[dict(EPISODE, created_at=None)]]})

    nodes = [entity('a'), entity('b'), entity('c'), entity('x')]
    distance = [
        (['a', 'b', 'c'], 'x', 0, [[{'uuid': 'b', 'score': 1}], nodes]),
        (['a', 'x', 'b'], 'x', 0, [[{'uuid': 'a', 'score': 1}], nodes]),
        (['a', 'x', 'b'], 'x', 0.5, [[{'uuid': 'a', 'score': 1}], nodes]),
        (['a', 'x', 'b'], 'x', 20, [[{'uuid': 'a', 'score': 1}], nodes]),
        (['a', 'x', 'b'], 'x', 10, [[{'uuid': 'a', 'score': 1}], nodes]),
        (['x'], 'x', 0, [[], nodes]),
        ([], 'x', 0, [[], nodes]),
        (['a', 'b'], 'x', 0, [[{'uuid': 'b', 'score': 1}, {'uuid': 'zzz', 'score': 1}], [entity('b')]]),
        (['a', 'b'], 'x', 0, [[{'uuid': 'b', 'score': 1}], [entity('b'), entity('b'), entity('q')]]),
        (['a', 'b', 'a'], 'x', 0, [[{'uuid': 'b', 'score': 1}], nodes]),
        (['a', 'b'], 'x', 0.5, [[], nodes]),
        (['a', 'b'], 'x', 0, [[{'uuid': 'a', 'score': 0}], nodes]),
        (['a', 'b'], 'x', 0, [[{'uuid': 'a', 'score': 2.5}, {'uuid': 'b', 'score': 1}], []]),
    ]
    for node_uuids, center, min_score, responses in distance:
        out.append(
            {
                'method': 'node_distance_reranker',
                'args': {'node_uuids': node_uuids, 'center_node_uuid': center, 'min_score': min_score},
                'responses': responses,
            }
        )
    out.append({'method': 'node_distance_reranker', 'args': {'node_uuids': ['a'], 'center_node_uuid': 'x'}, 'responses': [[], nodes]})

    mentions = [
        ([], 0, []),
        (['a', 'b', 'c'], 0, [[{'uuid': 'c', 'score': 3}, {'uuid': 'a', 'score': 1}], nodes]),
        (['a', 'b', 'c'], 2, [[{'uuid': 'c', 'score': 3}, {'uuid': 'a', 'score': 1}], nodes]),
        (['a', 'b', 'c'], 1e9, [[{'uuid': 'c', 'score': 3}, {'uuid': 'a', 'score': 1}], nodes]),
        (['a', 'a', 'b'], 0, [[{'uuid': 'a', 'score': 2}], nodes]),
        (['a', 'b'], 0, [[{'uuid': 'a', 'score': 2}, {'uuid': 'b', 'score': 2}], [entity('b')]]),
        (['a', 'b'], -1, [[{'uuid': 'a', 'score': 0}], []]),
    ]
    for node_uuids, min_score, responses in mentions:
        out.append({'method': 'episode_mentions_reranker', 'args': {'node_uuids': node_uuids, 'min_score': min_score}, 'responses': responses})
    out.append({'method': 'episode_mentions_reranker', 'args': {'node_uuids': ['a', 'b']}, 'responses': [[{'uuid': 'b', 'score': 1}], nodes]})

    for name in FILTERS:
        out.append({'method': 'build_node_search_filters', 'args': {'filter': name}})
        out.append({'method': 'build_edge_search_filters', 'args': {'filter': name}})
    for query in ('alice', '', 'a + b (c)', LONG_QUERY, 'café 😀 漢字'):
        for groups in GROUPS:
            out.append({'method': 'build_fulltext_query', 'args': {'query': query, 'group_ids': groups}})
    out.append({'method': 'build_fulltext_query', 'args': {'query': 'a b c', 'max_query_length': 3}})
    out.append({'method': 'build_fulltext_query', 'args': {'query': 'a b', 'group_ids': ['g'], 'max_query_length': 3}})
    out.append({'method': '_build_neo4j_fulltext_query', 'args': {'query': 'a b'}})
    out.append({'method': '_build_neo4j_fulltext_query', 'args': {'query': LONG_QUERY}})
    out.append({'method': '_build_neo4j_fulltext_query', 'args': {'query': 'a b c', 'group_ids': ['g'], 'max_query_length': 4}})
    out.append({'method': '_build_neo4j_fulltext_query', 'args': {'query': 'a b c', 'group_ids': ['g', 'h'], 'max_query_length': 5}})
    return out


def main() -> int:
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core.driver.neo4j.operations import search_ops
    from graphiti_core.driver.query_executor import QueryExecutor
    from graphiti_core.search.search_filters import SearchFilters

    class FakeExecutor(QueryExecutor):
        def __init__(self, responses, events):
            self.responses = responses
            self.events = events

        async def execute_query(self, cypher_query_, **kwargs):
            index = sum(1 for e in self.events if e['kind'] == 'call')
            self.events.append(
                {
                    'kind': 'call',
                    'query': cypher_query_,
                    'keys': list(kwargs),
                    'values': [encode(value) for value in kwargs.values()],
                }
            )
            records = self.responses[index] if index < len(self.responses) else []
            return records, None, None

    class Capture(logging.Handler):
        def __init__(self):
            super().__init__(logging.DEBUG)
            self.events = []

        def emit(self, record):
            self.events.append({'kind': 'log', 'level': record.levelname, 'message': record.getMessage()})

    capture = Capture()
    logger = logging.getLogger('graphiti_core')
    logger.setLevel(logging.DEBUG)
    logger.propagate = False
    logger.addHandler(capture)
    operations = search_ops.Neo4jSearchOperations()

    results = []
    for case in cases():
        capture.events = []
        executor = FakeExecutor(copy.deepcopy(case.get('responses', [])), capture.events)
        args = dict(case['args'])
        if 'filter' in args:
            args['search_filter' if not case['method'].startswith('build_') else 'search_filters'] = SearchFilters(
                **copy.deepcopy(FILTERS[args.pop('filter')])
            )
        elif case['method'] not in ('community_fulltext_search', 'community_similarity_search', 'node_distance_reranker',
                                    'episode_mentions_reranker', 'build_fulltext_query', '_build_neo4j_fulltext_query'):
            args['search_filter'] = SearchFilters()
        method = case['method']
        outcome = {}
        try:
            if method == '_build_neo4j_fulltext_query':
                value = search_ops._build_neo4j_fulltext_query(**args)
                outcome['result'] = encode(value)
            elif method.startswith('build_'):
                value = getattr(operations, method)(**args)
                outcome['result'] = encode(value)
            else:
                value = asyncio.run(getattr(operations, method)(executor, **args))
                outcome['result'] = [encode_item(item) for item in value]
            outcome['error'] = None
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            outcome['result'] = None
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
        outcome['events'] = list(capture.events)
        results.append({'input': case, 'output': outcome})

    document = {
        'python': '.'.join(str(part) for part in sys.version_info[:3]),
        'source': 'graphiti_core/driver/neo4j/operations/search_ops.py',
        'constants': {'MAX_QUERY_LENGTH': search_ops.MAX_QUERY_LENGTH},
        'filters': FILTERS,
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
