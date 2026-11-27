"""Records what graphiti_core/search/search_utils.py does, from real Python (port/RULES.md, R18, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_search_utils.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_search_utils.py --check

Sections of the recording:

- constants: each module-level constant by name.
- pure: calculate_cosine_similarity, fulltext_query, rrf, and maximal_marginal_relevance with generated
  inputs from a fixed seed, and with zero vectors, nan and inf values, ties, empty inputs, two
  lengths, each rank constant that divides by zero, and query texts at the length limit.
- calls: each async function of the module. A fake driver records each execute_query call (the query
  text, the parameter keys in order, and each value with its type) and returns the scripted rows of
  the case, one list for each call. The driver has a search interface and a graph operations
  interface, each one of: none (the attribute is None), raise (the base class, whose methods raise
  NotImplementedError), and return (a subclass that records each call and returns fixed values).
  Each function runs for each provider, with the filters off and on, with no group ids and with two,
  with a limit of 0, with no rows, and with rows that make the scores tie.

The calls to the driver and to an interface, and the log lines, are one list in the order of their
time. The clock of search_utils.py is a constant here and real in the test, so the test removes the time
from the two log lines.

Each fake task method (execute_query and each interface method) yields with asyncio.sleep(0) before it
records, as each C# fake runs Task.Yield() first (R25). Each fake also counts the calls in flight, from
its entry to its return, and the recording keeps the largest count of each case (max_in_flight): the
concurrency limit of semaphore_gather shows in it (R25).

Inputs that the port cannot reproduce by a rule are left out, and the report of plan 0004 packet P6b
names them: the Neptune branch of the four full-text searches (driver.run_aoss_query, which only the
Neptune driver has).

The tool writes tests/Port.Evaluation/values/search_utils.json. --check writes nothing and exits 1 if
the file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream in
.upstream/graphiti, or the path in GRAPHITI_UPSTREAM. It changes nothing there. Nothing here uses the
network.
"""
from __future__ import annotations

import asyncio
import json
import logging
import math
import os
import random
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
# GRAPHITI_UPSTREAM names the pinned upstream checkout when the working tree has no .upstream (a git worktree).
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM', str(ROOT / '.upstream' / 'graphiti')))
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'search_utils.json'
SEED = 20261127

PROVIDERS = ['neo4j', 'falkordb', 'neptune', 'relate']
CONSTANTS = ['RELEVANT_SCHEMA_LIMIT', 'DEFAULT_MIN_SCORE', 'DEFAULT_MMR_LAMBDA', 'MAX_SEARCH_DEPTH', 'MAX_QUERY_LENGTH']

FILTERS = {
    'none': {},
    'labels': {'node_labels': ['Person', 'Org_2'], 'edge_types': ['WORKS_AT']},
    'full': {
        'node_labels': ['Person'],
        'edge_types': ['WORKS_AT', 'KNOWS'],
        'valid_at': [
            [{'date': '2026-01-01T00:00:00+00:00', 'comparison_operator': '>'}],
            [{'comparison_operator': 'IS NULL'}],
        ],
        'invalid_at': [[{'date': '2026-02-01T00:00:00+00:00', 'comparison_operator': '<='}]],
        'created_at': [[{'date': '2026-03-01T00:00:00', 'comparison_operator': '='}]],
        'expired_at': [[{'comparison_operator': 'IS NOT NULL'}]],
        'edge_uuids': ['e-1', 'e-2'],
        'edge_source_node_uuids': ['s-1'],
        'edge_target_node_uuids': ['t-1'],
        'property_filters': [
            {'property_name': 'age', 'property_value': 3, 'comparison_operator': '>='},
            {'property_name': 'role', 'property_value': 'eng', 'comparison_operator': '='},
        ],
    },
}

GROUPS = [None, ['g', 'grp-2']]

# The functions that read the search interface, and whether they catch NotImplementedError.
SEARCH_INTERFACE_FUNCTIONS = [
    'edge_fulltext_search',
    'edge_similarity_search',
    'edge_bfs_search',
    'node_fulltext_search',
    'node_similarity_search',
    'node_bfs_search',
    'episode_fulltext_search',
    'community_fulltext_search',
    'community_similarity_search',
    'node_distance_reranker',
    'episode_mentions_reranker',
    'get_embeddings_for_communities',
]
OPERATIONS_FUNCTIONS = [
    'get_episodes_by_mentions',
    'get_mentioned_nodes',
    'get_communities_by_nodes',
    'get_embeddings_for_nodes',
    'get_embeddings_for_edges',
]
# Neptune reaches driver.run_aoss_query in these, and the port throws there (see the module docstring).
AOSS_FUNCTIONS = ['edge_fulltext_search', 'node_fulltext_search', 'episode_fulltext_search', 'community_fulltext_search']


def node_record(uuid, group_id='g'):
    return {
        'uuid': uuid,
        'name': 'Name ' + uuid,
        'group_id': group_id,
        'labels': ['Entity', 'Person'],
        'created_at': '2026-01-01T00:00:00+00:00',
        'summary': 'summary of ' + uuid,
        'name_embedding': [0.5, 0.25],
        'attributes': {'uuid': 'x', 'name': 'x', 'age': 3},
    }


def edge_record(uuid, group_id='g'):
    return {
        'uuid': uuid,
        'group_id': group_id,
        'source_node_uuid': 'src',
        'target_node_uuid': 'tgt',
        'created_at': '2026-01-01T00:00:00+00:00',
        'name': 'WORKS_AT',
        'fact': 'fact of ' + uuid,
        'fact_embedding': [0.5],
        'episodes': ['ep-1'],
        'expired_at': None,
        'valid_at': '2026-01-02T00:00:00+00:00',
        'invalid_at': None,
        'reference_time': None,
        'attributes': {'uuid': 'x', 'custom': 1},
    }


def episode_record(uuid, group_id='g'):
    return {
        'content': 'content ' + uuid,
        'created_at': '2026-01-01T00:00:00+00:00',
        'valid_at': '2026-01-01T00:00:00+00:00',
        'uuid': uuid,
        'name': 'episode ' + uuid,
        'group_id': group_id,
        'source': 'message',
        'source_description': 'chat',
        'entity_edges': ['e-1'],
    }


def community_record(uuid, group_id='g'):
    return {
        'uuid': uuid,
        'name': 'community ' + uuid,
        'group_id': group_id,
        'name_embedding': [1.0, 0.0],
        'created_at': '2026-01-01T00:00:00+00:00',
        'summary': 'summary ' + uuid,
    }


ENTITY_NODE = {**{k: v for k, v in node_record('n-1').items() if k != 'attributes'}, 'attributes': {'age': 3}}
ENTITY_EDGE = {**edge_record('e-1'), 'fact_embedding': [1.0, 0.0], 'attributes': {'custom': 1}}
EPISODE = episode_record('ep-1')
COMMUNITY = community_record('c-1')

SIMILARITY_ROWS = [
    {'id': 1, 'embedding': '1,0'},
    {'id': 2, 'embedding': '0,1'},
    {'id': 3, 'embedding': ''},
    {'id': 4, 'embedding': None},
    {'id': 5, 'embedding': '0.8, 0.6'},
]


def search_responses(function, provider, rows):
    """The rows of each execute_query call of one function."""
    if rows == 0:
        return [[], []]
    if function in ('edge_fulltext_search', 'edge_bfs_search'):
        return [[edge_record('r-1'), edge_record('r-2', 'grp-2')]]
    if function == 'edge_similarity_search':
        edges = [edge_record('r-1'), edge_record('r-2', 'grp-2')]
        return [SIMILARITY_ROWS, edges] if provider == 'neptune' else [edges]
    if function in ('node_fulltext_search', 'node_bfs_search'):
        return [[node_record('r-1'), node_record('r-2', 'grp-2')]]
    if function == 'node_similarity_search':
        nodes = [node_record('r-1'), node_record('r-2', 'grp-2')]
        return [SIMILARITY_ROWS, nodes] if provider == 'neptune' else [nodes]
    if function == 'episode_fulltext_search':
        return [[episode_record('r-1'), episode_record('r-2', 'grp-2')]]
    if function == 'community_fulltext_search':
        return [[community_record('r-1'), community_record('r-2', 'grp-2')]]
    if function == 'community_similarity_search':
        communities = [community_record('r-1'), community_record('r-2', 'grp-2')]
        return [SIMILARITY_ROWS, communities] if provider == 'neptune' else [communities]
    raise ValueError(function)


def search_cases():
    out = []
    for function in [
        'edge_fulltext_search',
        'edge_similarity_search',
        'edge_bfs_search',
        'node_fulltext_search',
        'node_similarity_search',
        'node_bfs_search',
        'episode_fulltext_search',
        'community_fulltext_search',
        'community_similarity_search',
    ]:
        for provider in PROVIDERS:
            if provider == 'neptune' and function in AOSS_FUNCTIONS:
                continue
            for filters in ('none', 'full'):
                for groups in GROUPS:
                    out.append({'fn': function, 'provider': provider, 'filters': filters, 'group_ids': groups, 'rows': 2})
            out.append({'fn': function, 'provider': provider, 'filters': 'labels', 'group_ids': ['g'], 'rows': 2})
            out.append({'fn': function, 'provider': provider, 'filters': 'none', 'group_ids': ['g'], 'rows': 0, 'limit': 0})
        out.append({'fn': function, 'provider': 'neo4j', 'filters': 'none', 'group_ids': None, 'rows': 2, 'defaults': True})
        for mode in ('raise', 'return'):
            out.append({'fn': function, 'provider': 'neo4j', 'filters': 'none', 'group_ids': ['g'], 'rows': 2, 'search_interface': mode})
    # The text-only paths: an empty full-text query, a query at the length limit, and the BFS guards.
    for function in ('edge_fulltext_search', 'node_fulltext_search', 'episode_fulltext_search', 'community_fulltext_search'):
        out.append({'fn': function, 'provider': 'neo4j', 'filters': 'none', 'group_ids': None, 'rows': 2, 'query': ' '.join(['w'] * 128)})
        out.append({'fn': function, 'provider': 'neo4j', 'filters': 'none', 'group_ids': ['bad"g'], 'rows': 2})
        out.append({'fn': function, 'provider': 'neo4j', 'filters': 'none', 'group_ids': [], 'rows': 2})
    for function in ('edge_bfs_search', 'node_bfs_search'):
        for origins in (None, [], ['o-1', 'o-2']):
            for depth in (0, 1, 3):
                out.append({'fn': function, 'provider': 'neo4j', 'filters': 'none', 'group_ids': ['g'], 'rows': 2, 'origins': origins, 'depth': depth})
    for source, target in ((None, None), ('s-9', None), (None, 't-9'), ('s-9', 't-9')):
        for groups in GROUPS:
            out.append(
                {'fn': 'edge_similarity_search', 'provider': 'neo4j', 'filters': 'none', 'group_ids': groups, 'rows': 2, 'source': source, 'target': target}
            )
    for provider in ('neo4j', 'neptune'):
        for min_score in (-1.0, 0.7, 1.0):
            for function in ('edge_similarity_search', 'node_similarity_search', 'community_similarity_search'):
                out.append({'fn': function, 'provider': provider, 'filters': 'none', 'group_ids': ['g'], 'rows': 2, 'min_score': min_score})
    return out


def lookup_cases():
    out = []
    for function in OPERATIONS_FUNCTIONS:
        for provider in PROVIDERS:
            for mode in ('none', 'raise', 'return'):
                for rows in (0, 2):
                    if mode == 'return' and rows == 0:
                        continue
                    out.append({'fn': function, 'provider': provider, 'operations': mode, 'rows': rows})
    for limit in (0, 1, 2, 10):
        out.append({'fn': 'get_episodes_by_mentions', 'provider': 'neo4j', 'operations': 'none', 'rows': 2, 'limit': limit})
    out.append({'fn': 'get_episodes_by_mentions', 'provider': 'neo4j', 'operations': 'none', 'rows': 2, 'defaults': True})
    for provider in PROVIDERS:
        for mode in ('none', 'raise', 'return'):
            for rows in (0, 2):
                if mode == 'return' and rows == 0:
                    continue
                out.append({'fn': 'get_embeddings_for_communities', 'provider': provider, 'search_interface': mode, 'rows': rows})
    return out


def relevant_cases():
    out = []
    for function in ('get_relevant_nodes', 'get_relevant_edges', 'get_edge_invalidation_candidates'):
        for provider in PROVIDERS:
            for filters in ('none', 'full'):
                for count in (0, 1, 2):
                    out.append({'fn': function, 'provider': provider, 'filters': filters, 'count': count, 'rows': 2})
            out.append({'fn': function, 'provider': provider, 'filters': 'labels', 'count': 2, 'rows': 0, 'limit': 0, 'min_score': 0.0})
        out.append({'fn': function, 'provider': 'neo4j', 'filters': 'none', 'count': 2, 'rows': 2, 'defaults': True})
    # The Neptune score filter is `score > min_score`. The scores of the rows are 1.0, 0.0, and 0.6, so a min_score of 0.6
    # and of 1.0 sits on a score.
    for function in ('get_relevant_edges', 'get_edge_invalidation_candidates'):
        for min_score in (0.6, 1.0):
            out.append({'fn': function, 'provider': 'neptune', 'filters': 'none', 'count': 2, 'rows': 2, 'min_score': min_score})
    return out


def reranker_cases():
    out = []
    for provider in ('neo4j', 'falkordb', 'neptune', 'relate'):
        for min_score in (0, 0.5, 2.0):
            for uuids in (['a', 'b', 'c', 'center'], ['center'], [], ['b', 'a', 'b']):
                case = {'fn': 'node_distance_reranker', 'provider': provider, 'node_uuids': uuids, 'center': 'center', 'min_score': min_score, 'rows': 'distance'}
                if provider == 'falkordb':
                    case['header'] = ['uuid', 'score']
                out.append(case)
    out.append({'fn': 'node_distance_reranker', 'provider': 'falkordb', 'node_uuids': ['b'], 'center': 'center', 'min_score': 0, 'rows': 'distance'})
    out.append({'fn': 'node_distance_reranker', 'provider': 'neo4j', 'node_uuids': ['a', 'b'], 'center': 'center', 'rows': 'distance', 'defaults': True})
    # FalkorDB rows: dict(zip(header, row)) over the keys of a dict row.
    for header, rows in (
        (['uuid', 'score'], [{'b': None, 'c': None}]),
        (['uuid'], [{'b': None, 'c': None}]),
        (['uuid', 'score'], []),
    ):
        out.append(
            {'fn': 'node_distance_reranker', 'provider': 'falkordb', 'node_uuids': ['b'], 'center': 'center', 'min_score': 0, 'rows': rows, 'header': header}
        )
    out.append({'fn': 'node_distance_reranker', 'provider': 'neo4j', 'node_uuids': ['a', 'center'], 'center': 'center', 'min_score': 0, 'rows': [{'uuid': 'center', 'score': 1}, {'uuid': 'a', 'score': 0}]})
    for mode in ('raise', 'return'):
        out.append({'fn': 'node_distance_reranker', 'provider': 'neo4j', 'node_uuids': ['a'], 'center': 'center', 'min_score': 0, 'rows': 'distance', 'search_interface': mode})
    for min_score in (0, 1, 3, 10):
        for lists in ([['a', 'b', 'c'], ['c', 'a']], [], [[]], [['x', 'y'], ['y', 'x']]):
            out.append({'fn': 'episode_mentions_reranker', 'provider': 'neo4j', 'node_uuids': lists, 'min_score': min_score, 'rows': 'mentions'})
    out.append({'fn': 'episode_mentions_reranker', 'provider': 'neo4j', 'node_uuids': [['a', 'b']], 'rows': 'mentions', 'defaults': True})
    for mode in ('raise', 'return'):
        out.append({'fn': 'episode_mentions_reranker', 'provider': 'neo4j', 'node_uuids': [['a']], 'min_score': 0, 'rows': 'mentions', 'search_interface': mode})
    return out


def hybrid_cases():
    out = []
    for queries, embeddings in ((['q1', 'q2'], [[1.0, 0.0]]), ([], []), (['q1'], []), ([], [[0.5, 0.5], [1.0, 0.0]])):
        for groups in GROUPS:
            out.append({'fn': 'hybrid_node_search', 'provider': 'neo4j', 'queries': queries, 'embeddings': embeddings, 'group_ids': groups, 'filters': 'labels', 'limit': 3})
    out.append({'fn': 'hybrid_node_search', 'provider': 'neo4j', 'queries': ['q1'], 'embeddings': [[1.0, 0.0]], 'group_ids': None, 'filters': 'none', 'defaults': True})
    out.append({'fn': 'hybrid_node_search', 'provider': 'neo4j', 'queries': ['q1', 'q2'], 'embeddings': [[1.0, 0.0]], 'group_ids': ['g'], 'filters': 'none', 'limit': 2, 'search_interface': 'return'})
    # More searches than the limit of semaphore_gather (SEMAPHORE_LIMIT, 20): 11 + 11 = 22 searches run, 20 at one time.
    wide_queries = [f'q{i}' for i in range(11)]
    wide_embeddings = [[1.0, round(i / 11, 3)] for i in range(11)]
    out.append({'fn': 'hybrid_node_search', 'provider': 'neo4j', 'queries': wide_queries, 'embeddings': wide_embeddings, 'group_ids': ['g'], 'filters': 'none', 'limit': 3})
    return out


def pure_cases(rng):
    out = []

    def vec(n):
        return [round(rng.uniform(-2, 2), rng.choice([1, 3, 17])) for _ in range(n)]

    cosine = [
        ([], []),
        ([0.0, 0.0], [1.0, 2.0]),
        ([1.0, 2.0], [0.0, 0.0]),
        ([1, 2, 3], [4, 5, 6]),
        ([float('nan'), 1.0], [1.0, 1.0]),
        ([float('inf'), 0.0], [1.0, 0.0]),
        ([1e-200, 1e-200], [1e-200, 0.0]),
        ([1e200, 1e200], [1e200, 1e200]),
        ([1.0, 2.0], [1.0, 2.0, 3.0]),
        ([1.0, 2.0, 3.0], [1.0]),
        ([-0.0], [5.0]),
    ]
    for _ in range(60):
        n = rng.randint(1, 12)
        cosine.append((vec(n), vec(n)))
    for a, b in cosine:
        out.append({'fn': 'calculate_cosine_similarity', 'args': [a, b]})

    words = ['alpha', 'b-c', 'AND', 'x:y', '(p)', '"q"', 'Zoë', '東京', '😀', 'a/b', '~', '*', '!', 'OR', '']
    queries = ['', 'hello', 'hello world', ' '.join(['w'] * 127), ' '.join(['w'] * 128), ' '.join(['w'] * 126)]
    for _ in range(20):
        queries.append(' '.join(rng.choice(words) for _ in range(rng.randint(1, 6))))
    groups = [None, [], ['g'], ['g', 'grp-2'], ['g', 'h', 'i'], ['bad"g'], ['g', ''], ['x'] * 3]
    for provider in PROVIDERS:
        for syntax in ('', '@') if provider == 'neo4j' else ('',):
            for query in queries[:6]:
                for group_ids in groups:
                    out.append({'fn': 'fulltext_query', 'provider': provider, 'syntax': syntax, 'args': [query, group_ids]})
    for query in queries[8:]:
        out.append({'fn': 'fulltext_query', 'provider': 'neo4j', 'syntax': '', 'args': [query, rng.choice(groups)]})

    rrf_inputs = [[], [[]], [['a', 'b', 'c'], ['b', 'c', 'd']], [['a', 'b'], ['b', 'a']], [['x', 'y', 'x']], [['😀', 'Zoë', '']]]
    pool = ['a', 'b', 'c', 'd', 'e', 'f', 'g']
    for _ in range(25):
        rrf_inputs.append([rng.sample(pool, rng.randint(0, 5)) for _ in range(rng.randint(1, 4))])
    for results in rrf_inputs:
        for rank_const in (1, 2, 60, 0, -2):
            for min_score in (0, 0.5, -1.0):
                if rank_const != 1 and min_score != 0:
                    continue
                out.append({'fn': 'rrf', 'args': [results], 'kwargs': {'rank_const': rank_const, 'min_score': min_score}})
        out.append({'fn': 'rrf', 'args': [results], 'kwargs': {}})

    mmr_inputs = [
        ([1.0, 0.0], {}),
        ([1.0, 0.0], {'a': [3.0, 4.0]}),
        ([1.0, 0.0], {'a': [0.0, 0.0]}),
        ([1.0, 0.0], {'a': [1.0, 0.0], 'b': [1.0, 0.0], 'c': [1.0, 0.0]}),
        ([0.0, 1.0], {'c': [1.0, 0.0], 'b': [1.0, 0.0], 'a': [-1.0, 0.0]}),
        ([1.0, 0.0], {'a': [float('nan'), 0.0], 'b': [1.0, 0.0]}),
        ([1.0, 0.0], {'a': [1.0, 0.0], 'b': [float('nan'), 1.0], 'c': [0.5, 0.5]}),
        ([float('nan'), 0.0], {'a': [1.0, 0.0], 'b': [0.0, 1.0]}),
        ([1.0, 0.0], {'a': [float('inf'), 0.0], 'b': [1.0, 1.0]}),
        ([1.0, 0.0], {'a': [1.0, 0.0, 0.0]}),
        ([1.0, 0.0], {'a': [1.0, 0.0], 'b': [1.0, 0.0, 0.0]}),
        ([1, 0], {'a': [2, 0], 'b': [0, 2]}),
    ]
    for _ in range(30):
        n = rng.randint(1, 8)
        mmr_inputs.append((vec(n), {f'u{i}': vec(n) for i in range(rng.randint(0, 6))}))
    for query_vector, candidates in mmr_inputs:
        for kwargs in ({}, {'mmr_lambda': 0.0}, {'mmr_lambda': 1.0, 'min_score': 0.0}, {'mmr_lambda': 0.3, 'min_score': -0.5}):
            out.append({'fn': 'maximal_marginal_relevance', 'args': [query_vector, candidates], 'kwargs': kwargs})
    return out


def encode(value):
    """A value with its Python type."""
    from pydantic import BaseModel

    if value is None:
        return {'type': 'none'}
    if isinstance(value, bool):
        return {'type': 'bool', 'value': value}
    if isinstance(value, int):
        return {'type': 'int', 'value': int(value)}
    if isinstance(value, float):
        if math.isnan(value):
            return {'type': 'float', 'value': 'nan'}
        if math.isinf(value):
            return {'type': 'float', 'value': 'inf' if value > 0 else '-inf'}
        return {'type': 'float', 'value': float(value)}
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
    if isinstance(value, BaseModel):
        return {'type': 'model', 'class': type(value).__name__, 'dump': encode(value.model_dump())}
    if hasattr(value, 'dtype'):
        return encode(value.item())
    return {'type': type(value).__name__}


def number(value):
    """A JSON value for a float input that can be nan or inf."""
    if isinstance(value, float) and (math.isnan(value) or math.isinf(value)):
        return {'$float': repr(value)}
    if isinstance(value, list):
        return [number(item) for item in value]
    if isinstance(value, dict):
        return {key: number(item) for key, item in value.items()}
    return value


def unnumber(value):
    if isinstance(value, dict) and set(value) == {'$float'}:
        return float(value['$float'])
    if isinstance(value, list):
        return [unnumber(item) for item in value]
    if isinstance(value, dict):
        return {key: unnumber(item) for key, item in value.items()}
    return value


def main() -> int:
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.driver.graph_operations.graph_operations import GraphOperationsInterface
    from graphiti_core.driver.search_interface.search_interface import SearchInterface
    from graphiti_core.edges import EntityEdge
    from graphiti_core.nodes import CommunityNode, EntityNode, EpisodicNode
    from graphiti_core.search import search_utils
    from graphiti_core.search.search_filters import SearchFilters

    rng = random.Random(SEED)
    events: list[dict] = []
    # Calls in flight: a call counts from the entry of a fake method to its return.
    flight = {'now': 0, 'max': 0}

    class InFlight:
        def __enter__(self):
            flight['now'] += 1
            flight['max'] = max(flight['max'], flight['now'])

        def __exit__(self, *exc):
            flight['now'] -= 1
    # A constant clock keeps the two timed log lines the same on each run.
    search_utils.time = lambda: 100.0

    class Capture(logging.Handler):
        def __init__(self):
            super().__init__(logging.DEBUG)

        def emit(self, record):
            events.append({'kind': 'log', 'level': record.levelname, 'message': record.getMessage()})

    logger = logging.getLogger(search_utils.__name__)
    logger.setLevel(logging.DEBUG)
    logger.propagate = False
    logger.addHandler(Capture())

    class FakeDriver:
        def __init__(self, provider, syntax, search_interface, operations, responses, header):
            self.provider = provider
            self.fulltext_syntax = syntax
            self.search_interface = search_interface
            self.graph_operations_interface = operations
            self.responses = responses
            self.header = header

        async def execute_query(self, cypher_query_, **kwargs):
            with InFlight():
                await asyncio.sleep(0)
                events.append({'kind': 'query', 'query': cypher_query_, 'keys': list(kwargs), 'values': [encode(v) for v in kwargs.values()]})
                records = self.responses.pop(0) if self.responses else []
                return records, self.header, None

        def build_fulltext_query(self, query, group_ids=None, max_query_length=128):
            events.append({'kind': 'op', 'op': 'build_fulltext_query', 'args': [encode(query), encode(group_ids), encode(max_query_length)]})
            return 'falkor(' + query + ')'

    def note(name, args):
        events.append({'kind': 'op', 'op': name, 'args': [encode(a) for a in args if not isinstance(a, FakeDriver)]})

    def returning_search_interface():
        edge = EntityEdge(**{**ENTITY_EDGE, 'uuid': 'si-1'})
        node = EntityNode(**{**ENTITY_NODE, 'uuid': 'si-1'})
        episode = EpisodicNode(**{**EPISODE, 'uuid': 'si-1'})
        community = CommunityNode(**{**COMMUNITY, 'uuid': 'si-1'})
        values = {
            'edge_fulltext_search': [edge],
            'edge_similarity_search': [edge],
            'edge_bfs_search': [edge],
            'node_fulltext_search': [node],
            'node_similarity_search': [node],
            'node_bfs_search': [node],
            'episode_fulltext_search': [episode],
            'community_fulltext_search': [community],
            'community_similarity_search': [community],
            'get_embeddings_for_communities': {'si-1': [1.0, 0.5]},
            'node_distance_reranker': (['si-1'], [1.0]),
            'episode_mentions_reranker': (['si-1'], [2.0]),
        }

        def make(name):
            async def method(self, *args):
                with InFlight():
                    await asyncio.sleep(0)
                    note(name, args)
                    return values[name]

            return method

        cls = type('ReturningSearchInterface', (SearchInterface,), {name: make(name) for name in values})
        return cls()

    def returning_operations():
        node = EntityNode(**{**ENTITY_NODE, 'uuid': 'ops-1'})
        episode = EpisodicNode(**{**EPISODE, 'uuid': 'ops-1'})
        community = CommunityNode(**{**COMMUNITY, 'uuid': 'ops-1'})
        values = {
            'get_mentioned_nodes': [node],
            'get_communities_by_nodes': [community],
            'node_load_embeddings_bulk': {'ops-1': [0.5, 0.5]},
            'edge_load_embeddings_bulk': {'ops-1': [0.25]},
            'episodic_node_get_by_uuids': [episode],
        }

        def make(name):
            async def method(self, *args):
                with InFlight():
                    await asyncio.sleep(0)
                    note(name, args)
                    return values[name]

            return method

        cls = type('ReturningOperations', (GraphOperationsInterface,), {name: make(name) for name in values})
        return cls()

    def interface(mode, base, returning):
        if mode == 'raise':
            return base()
        if mode == 'return':
            return returning()
        return None

    def rows_for(case):
        fn = case['fn']
        rows = case.get('rows', 0)
        provider = case['provider']
        if isinstance(rows, list):
            return [json.loads(json.dumps(rows))]
        if rows == 'distance':
            return [[{'uuid': 'b', 'score': 1}, {'uuid': 'c', 'score': 1}]]
        if rows == 'mentions':
            return [[{'uuid': 'b', 'score': 3}, {'uuid': 'a', 'score': 3}, {'uuid': 'c', 'score': 1}, {'uuid': 'y', 'score': 2}]]
        if fn in ('hybrid_node_search',):
            return [[node_record('h-1'), node_record('h-2')], [node_record('h-2'), node_record('h-3')], [node_record('h-3')], [node_record('h-1')]]
        if fn == 'get_mentioned_nodes':
            return [[node_record('r-1'), node_record('r-2')][:rows]]
        if fn == 'get_communities_by_nodes':
            return [[community_record('r-1'), community_record('r-2')][:rows]]
        if fn == 'get_episodes_by_mentions':
            return [[episode_record('r-1'), episode_record('r-2')][:rows]]
        if fn in ('get_embeddings_for_nodes', 'get_embeddings_for_communities', 'get_embeddings_for_edges'):
            key = 'fact_embedding' if fn == 'get_embeddings_for_edges' else 'name_embedding'
            records = [
                {'uuid': 'r-1', key: [0.5, 1.0]},
                {'uuid': None, key: [1.0]},
                {'uuid': 'r-2', key: None},
                {key: [2.0]},
                {'uuid': 'r-3', key: []},
                {'uuid': 'r-1', key: [3.0, 4.0]},
            ]
            return [records[: rows * 3]]
        if fn == 'get_relevant_nodes':
            if rows == 0:
                return [[]]
            return [[{'search_node_uuid': 'n-1', 'matches': [node_record('m-1'), node_record('m-2')]}, {'search_node_uuid': 'zz', 'matches': [node_record('m-3')]}]]
        if fn in ('get_relevant_edges', 'get_edge_invalidation_candidates'):
            matches = [{'search_edge_uuid': 'e-1', 'matches': [edge_record('m-1'), edge_record('m-2')]}, {'search_edge_uuid': 'zz', 'matches': []}]
            if rows == 0:
                return [[], []]
            if provider == 'neptune':
                first = [
                    {'id': 1, 'source_embedding': '1,0', 'target_embedding': [1.0, 0.0], 'search_edge_uuid': 'e-1'},
                    {'id': 2, 'source_embedding': '0,1', 'target_embedding': [1.0, 0.0], 'search_edge_uuid': 'e-2'},
                    {'id': 3, 'source_embedding': '0.6,0.8', 'target_embedding': [1, 0], 'search_edge_uuid': 'e-1'},
                ]
                return [first, matches]
            return [matches]
        return search_responses(fn, provider, rows)

    async def run(case, driver):
        fn = case['fn']
        func = getattr(search_utils, fn)
        filters = SearchFilters(**json.loads(json.dumps(FILTERS[case.get('filters', 'none')])))
        groups = case.get('group_ids')
        defaults = case.get('defaults', False)
        limit = case.get('limit', 5)
        min_score = case.get('min_score', 0.5)
        if fn in ('edge_fulltext_search', 'node_fulltext_search', 'episode_fulltext_search'):
            query = case.get('query', 'alpha b-c Zoë')
            if defaults:
                return await func(driver, query, filters)
            return await func(driver, query, filters, groups, limit)
        if fn == 'community_fulltext_search':
            query = case.get('query', 'alpha b-c Zoë')
            if defaults:
                return await func(driver, query)
            return await func(driver, query, groups, limit)
        if fn == 'edge_similarity_search':
            if defaults:
                return await func(driver, [1.0, 0.0], None, None, filters)
            return await func(driver, [1.0, 0.0], case.get('source', 's-1'), case.get('target'), filters, groups, limit, min_score)
        if fn == 'node_similarity_search':
            if defaults:
                return await func(driver, [1.0, 0.0], filters)
            return await func(driver, [1.0, 0.0], filters, groups, limit, min_score)
        if fn == 'community_similarity_search':
            if defaults:
                return await func(driver, [1.0, 0.0])
            return await func(driver, [1.0, 0.0], groups, limit, min_score)
        if fn == 'edge_bfs_search':
            origins = case.get('origins', ['o-1'])
            if defaults:
                return await func(driver, origins, case.get('depth', 2), filters)
            return await func(driver, origins, case.get('depth', 2), filters, groups, limit)
        if fn == 'node_bfs_search':
            origins = case.get('origins', ['o-1'])
            if defaults:
                return await func(driver, origins, filters, case.get('depth', 2))
            return await func(driver, origins, filters, case.get('depth', 2), groups, limit)
        if fn == 'hybrid_node_search':
            if defaults:
                return await func(case['queries'], case['embeddings'], driver, filters)
            return await func(case['queries'], case['embeddings'], driver, filters, groups, limit)
        if fn == 'get_episodes_by_mentions':
            nodes = [EntityNode(**ENTITY_NODE)]
            edges = [EntityEdge(**{**ENTITY_EDGE, 'episodes': ['ep-1', 'ep-2']}), EntityEdge(**{**ENTITY_EDGE, 'uuid': 'e-2', 'episodes': ['ep-3']})]
            if defaults:
                return await func(driver, nodes, edges)
            return await func(driver, nodes, edges, case.get('limit', 10))
        if fn == 'get_mentioned_nodes':
            return await func(driver, [EpisodicNode(**EPISODE), EpisodicNode(**{**EPISODE, 'uuid': 'ep-2'})])
        if fn in ('get_communities_by_nodes', 'get_embeddings_for_nodes'):
            return await func(driver, [EntityNode(**ENTITY_NODE), EntityNode(**{**ENTITY_NODE, 'uuid': 'n-2'})])
        if fn == 'get_embeddings_for_communities':
            return await func(driver, [CommunityNode(**COMMUNITY), CommunityNode(**{**COMMUNITY, 'uuid': 'c-2'})])
        if fn == 'get_embeddings_for_edges':
            return await func(driver, [EntityEdge(**ENTITY_EDGE), EntityEdge(**{**ENTITY_EDGE, 'uuid': 'e-2'})])
        if fn == 'get_relevant_nodes':
            nodes = [EntityNode(**ENTITY_NODE), EntityNode(**{**ENTITY_NODE, 'uuid': 'n-2', 'name': 'Zoë 東京', 'group_id': 'grp-2'})][: case['count']]
            if defaults:
                return await func(driver, nodes, filters)
            return await func(driver, nodes, filters, min_score, limit)
        if fn in ('get_relevant_edges', 'get_edge_invalidation_candidates'):
            edges = [EntityEdge(**ENTITY_EDGE), EntityEdge(**{**ENTITY_EDGE, 'uuid': 'e-2', 'group_id': 'grp-2'})][: case['count']]
            if defaults:
                return await func(driver, edges, filters)
            return await func(driver, edges, filters, min_score, limit)
        if fn == 'node_distance_reranker':
            if defaults:
                return await func(driver, case['node_uuids'], case['center'])
            return await func(driver, case['node_uuids'], case['center'], case['min_score'])
        if fn == 'episode_mentions_reranker':
            if defaults:
                return await func(driver, case['node_uuids'])
            return await func(driver, case['node_uuids'], case['min_score'])
        raise ValueError(fn)

    results = []

    def record(case, call):
        events.clear()
        flight['now'] = 0
        flight['max'] = 0
        outcome = {}
        try:
            value = call()
            outcome['error'] = None
            outcome['result'] = encode(value)
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
            outcome['result'] = None
        outcome['events'] = list(events)
        outcome['max_in_flight'] = flight['max']
        results.append({'input': case, 'output': outcome})

    for case in pure_cases(rng):
        fn = case['fn']
        args = case['args']
        kwargs = case.get('kwargs', {})
        if fn == 'fulltext_query':
            driver = FakeDriver(GraphProvider(case['provider']), case['syntax'], None, None, [], None)
            record(case, lambda: search_utils.fulltext_query(args[0], args[1], driver))
        else:
            record({**case, 'args': number(args)}, lambda: getattr(search_utils, fn)(*args, **kwargs))

    for case in search_cases() + lookup_cases() + relevant_cases() + reranker_cases() + hybrid_cases():
        driver = FakeDriver(
            GraphProvider(case['provider']),
            '',
            interface(case.get('search_interface', 'none'), SearchInterface, returning_search_interface),
            interface(case.get('operations', 'none'), GraphOperationsInterface, returning_operations),
            rows_for(case),
            case.get('header'),
        )
        record(case, lambda: asyncio.run(run(case, driver)))

    document = {
        'python': '.'.join(str(part) for part in sys.version_info[:3]),
        'source': 'graphiti_core/search/search_utils.py',
        'constants': {name: getattr(search_utils, name) for name in CONSTANTS},
        'filters': FILTERS,
        'entity_node': ENTITY_NODE,
        'entity_edge': ENTITY_EDGE,
        'episode': EPISODE,
        'community': COMMUNITY,
        'cases': results,
    }
    text = json.dumps(document, ensure_ascii=True, separators=(',', ':'), allow_nan=False) + '\n'
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
