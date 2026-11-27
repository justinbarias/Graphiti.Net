"""Records what graphiti_core/search/search.py does, from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_search.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_search.py --check

Each case runs `search`, or one of the four scope functions, with fakes for the graph driver, the embedder, the
cross-encoder, and the tracer:

- The driver is a Neo4j driver with no search interface and no graph operations interface, so the real functions of
  search_utils.py build each query. execute_query records the query text, the parameter keys in order, and each value
  with its type. It answers from the tables of the recording by a fixed rule (see `respond`), because the searches run
  concurrently, and an answer by call order would depend on the schedule.
- The embedder returns [1.0, 0.0] and records its input. The cross-encoder returns the passages in reverse order with
  the scores 1.0, 0.75, 0.5, and so on, and records its input.
- The tracer records the start of each span, and at its end the name, the attributes, the status, and the class of the
  recorded exception.

Each fake first yields to the loop (`await asyncio.sleep(0)`), then records the call (R25). The calls, the spans, and
the log lines of the `graphiti_core` loggers are one list in the order of their time. A call is in flight from its entry
to its return, and each case keeps the largest count of calls in flight over the three fakes (`max_in_flight`). The clock is a constant here and real in the
test, so the test removes the time from a log line. A vector of more than 16 items is recorded as its length and its sum.

The cases run each of the 16 recipes with no center node and with one, and again with a limit of 2 and a
reranker_min_score of 0.5. Configs given as JSON reach each reranker of each scope with all search methods, a limit of
0, a config with no scope, and an episode-only config, which takes the zero vector. The other cases hold an empty
query, a query with line breaks, the group ids None, [], [''], and an invalid id, a given query vector, a
max_coroutines of 1, and direct calls of the four scope functions with no config and with no tracer.
Other cases give the cross-encoder of the community and episode scope a reranker_min_score at and above a score, give
the edge and node scopes a sim_min_score and a bfs_max_depth that differ from the defaults, give an MMR reranker with no
cosine method, an empty list of breadth-first origins, and a max_coroutines of 1 and 2 for a hybrid config of each scope.
Two more groups of cases give each of the three reranker_min_score filters of rrf (the edge cross-encoder, the edge node
distance, and the node node distance) a candidate below the minimum and a candidate at the minimum, with no limit that
removes it first.

The cancel cases are in a second file. The grid has 8 recipes, a max_coroutines of None, 1, and 2, and each k from 1 to
the count of calls of one kind that the search makes with no cancel. The kinds are a query of the driver, a call of the
embedder, and a call of the cross-encoder. The caller cancels the task of the search while the k-th call of the kind
returns. The search then ends with CancelledError, which is a BaseException, so the spans keep no status. The loop
builds the grid, and no list holds a point.

The tool writes tests/Port.Evaluation/values/search.json and tests/Port.Evaluation/values/search_cancel.json. --check
writes nothing and exits 1 if a file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream in .upstream/graphiti, or the path in
GRAPHITI_UPSTREAM. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import json
import logging
import math
import os
import sys
import warnings
from contextlib import contextmanager
from datetime import datetime
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
# GRAPHITI_UPSTREAM names the pinned upstream checkout when the working tree has no .upstream (a git worktree).
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM', str(ROOT / '.upstream' / 'graphiti')))
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'search.json'
OUT_CANCEL = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'search_cancel.json'

RECIPES = [
    'COMBINED_HYBRID_SEARCH_RRF',
    'COMBINED_HYBRID_SEARCH_MMR',
    'COMBINED_HYBRID_SEARCH_CROSS_ENCODER',
    'EDGE_HYBRID_SEARCH_RRF',
    'EDGE_HYBRID_SEARCH_MMR',
    'EDGE_HYBRID_SEARCH_NODE_DISTANCE',
    'EDGE_HYBRID_SEARCH_EPISODE_MENTIONS',
    'EDGE_HYBRID_SEARCH_CROSS_ENCODER',
    'NODE_HYBRID_SEARCH_RRF',
    'NODE_HYBRID_SEARCH_MMR',
    'NODE_HYBRID_SEARCH_NODE_DISTANCE',
    'NODE_HYBRID_SEARCH_EPISODE_MENTIONS',
    'NODE_HYBRID_SEARCH_CROSS_ENCODER',
    'COMMUNITY_HYBRID_SEARCH_RRF',
    'COMMUNITY_HYBRID_SEARCH_MMR',
    'COMMUNITY_HYBRID_SEARCH_CROSS_ENCODER',
]


def edge(uuid, source, fact, episodes):
    return {
        'uuid': uuid,
        'source_node_uuid': source,
        'target_node_uuid': 'n-9',
        'group_id': 'g',
        'created_at': '2026-01-01T00:00:00+00:00',
        'name': 'WORKS_AT',
        'fact': fact,
        'episodes': episodes,
        'expired_at': None,
        'valid_at': None,
        'invalid_at': None,
        'reference_time': None,
        'attributes': {'uuid': uuid},
    }


def node(uuid, name):
    return {
        'uuid': uuid,
        'name': name,
        'group_id': 'g',
        'created_at': '2026-01-01T00:00:00+00:00',
        'summary': 'summary of ' + uuid,
        'labels': ['Entity'],
        'attributes': {'uuid': uuid},
    }


def episode(uuid, content):
    return {
        'uuid': uuid,
        'name': 'episode ' + uuid,
        'group_id': 'g',
        'created_at': '2026-01-01T00:00:00+00:00',
        'source': 'message',
        'source_description': 'chat',
        'content': content,
        'valid_at': '2026-01-01T00:00:00+00:00',
        'entity_edges': [],
    }


def community(uuid, name):
    return {
        'uuid': uuid,
        'name': name,
        'group_id': 'g',
        'created_at': '2026-01-01T00:00:00+00:00',
        'name_embedding': None,
        'summary': 'summary of ' + uuid,
    }


# The rows of each search query. Two results share a fact, a name, or a content, so that a map from the text to the
# uuid keeps the last one.
TABLES = {
    'edge_fulltext': [
        edge('e-1', 'n-1', 'fact one', ['ep-1']),
        edge('e-2', 'n-2', 'fact two', ['ep-1', 'ep-2']),
        edge('e-3', 'n-1', 'fact three', ['ep-3', 'ep-1', 'ep-2']),
    ],
    'edge_similarity': [
        edge('e-3', 'n-1', 'fact three', ['ep-3', 'ep-1', 'ep-2']),
        edge('e-4', 'n-3', 'fact one', ['ep-2', 'ep-4']),
        edge('e-1', 'n-1', 'fact one', ['ep-1']),
    ],
    'edge_bfs': [
        edge('e-5', 'n-2', 'fact five', []),
        edge('e-2', 'n-2', 'fact two', ['ep-1', 'ep-2']),
    ],
    'node_fulltext': [node('n-1', 'Alice'), node('n-2', 'Bob'), node('n-3', 'Carol')],
    'node_similarity': [node('n-3', 'Carol'), node('n-4', 'Alice'), node('n-1', 'Alice')],
    'node_bfs': [node('n-5', 'Eve'), node('n-2', 'Bob')],
    'episode_fulltext': [
        episode('ep-1', 'c1'),
        episode('ep-2', 'c2'),
        episode('ep-3', 'c1'),
        episode('ep-4', 'c3'),
        episode('ep-5', 'c4'),
        episode('ep-6', 'c5'),
    ],
    'community_fulltext': [community('c-1', 'Comm A'), community('c-2', 'Comm B')],
    'community_similarity': [
        community('c-2', 'Comm B'),
        community('c-3', 'Comm A'),
        community('c-4', 'Comm C'),
        community('c-5', 'Comm D'),
    ],
}
EMBEDDINGS = {
    'e-1': [1.0, 0.0],
    'e-2': [0.6, 0.8],
    'e-3': [0.0, 1.0],
    'e-4': [0.8, 0.6],
    'e-5': [-1.0, 0.0],
    'n-1': [0.0, 1.0],
    'n-2': [1.0, 0.0],
    'n-3': [0.6, 0.8],
    'n-4': [0.8, -0.6],
    'n-5': [0.5, 0.5],
    'c-1': [1.0, 0.0],
    'c-2': [0.0, 1.0],
    'c-3': [0.6, 0.8],
    'c-4': [0.8, 0.6],
    'c-5': [0.0, -1.0],
}
# node_distance_reranker: the nodes next to the center node.
CONNECTED = ['n-2', 'n-3', 'n-5']
# episode_mentions_reranker: the count of mentions of each node.
MENTIONS = {'n-1': 1, 'n-2': 3, 'n-3': 1, 'n-4': 2}

ALL_EDGE = ['bm25', 'cosine_similarity', 'breadth_first_search']
CUSTOM = {
    'edge_rrf_all': {'edge_config': {'search_methods': ALL_EDGE, 'reranker': 'reciprocal_rank_fusion'}},
    'edge_mentions_all': {'edge_config': {'search_methods': ALL_EDGE, 'reranker': 'episode_mentions'}},
    'edge_distance_all': {'edge_config': {'search_methods': ALL_EDGE, 'reranker': 'node_distance'}},
    'edge_mmr_all': {'edge_config': {'search_methods': ALL_EDGE, 'reranker': 'mmr', 'mmr_lambda': 0.3}},
    'edge_cross_all': {'edge_config': {'search_methods': ALL_EDGE, 'reranker': 'cross_encoder'}, 'limit': 2},
    'edge_bfs_only': {'edge_config': {'search_methods': ['breadth_first_search'], 'reranker': 'reciprocal_rank_fusion', 'bfs_max_depth': 1}},
    'edge_no_methods': {'edge_config': {'search_methods': [], 'reranker': 'reciprocal_rank_fusion'}},
    'node_rrf_all': {'node_config': {'search_methods': ALL_EDGE, 'reranker': 'reciprocal_rank_fusion'}},
    'node_mentions_all': {'node_config': {'search_methods': ALL_EDGE, 'reranker': 'episode_mentions'}},
    'node_distance_all': {'node_config': {'search_methods': ALL_EDGE, 'reranker': 'node_distance'}},
    'node_mmr_all': {'node_config': {'search_methods': ALL_EDGE, 'reranker': 'mmr', 'mmr_lambda': 0.7}},
    'node_cross_all': {'node_config': {'search_methods': ALL_EDGE, 'reranker': 'cross_encoder'}},
    'node_bfs_only': {'node_config': {'search_methods': ['breadth_first_search'], 'reranker': 'reciprocal_rank_fusion', 'bfs_max_depth': 3}},
    'episode_rrf': {'episode_config': {'search_methods': ['bm25'], 'reranker': 'reciprocal_rank_fusion'}},
    'episode_cross': {'episode_config': {'search_methods': ['bm25'], 'reranker': 'cross_encoder'}, 'limit': 2},
    'episode_cross_min': {'episode_config': {'search_methods': ['bm25'], 'reranker': 'cross_encoder'}, 'reranker_min_score': 0.6},
    'community_rrf': {'community_config': {'search_methods': ['bm25'], 'reranker': 'reciprocal_rank_fusion'}},
    'community_mmr': {'community_config': {'search_methods': ['bm25'], 'reranker': 'mmr'}},
    'community_cross': {'community_config': {'search_methods': ['bm25', 'cosine_similarity'], 'reranker': 'cross_encoder', 'sim_min_score': 0.2}},
    'all_scopes_limit_0': {
        'edge_config': {'search_methods': ALL_EDGE, 'reranker': 'reciprocal_rank_fusion'},
        'node_config': {'search_methods': ALL_EDGE, 'reranker': 'reciprocal_rank_fusion'},
        'episode_config': {'search_methods': ['bm25'], 'reranker': 'reciprocal_rank_fusion'},
        'community_config': {'search_methods': ['bm25'], 'reranker': 'reciprocal_rank_fusion'},
        'limit': 0,
    },
    'all_scopes_min_score': {
        'edge_config': {'search_methods': ALL_EDGE, 'reranker': 'cross_encoder'},
        'node_config': {'search_methods': ALL_EDGE, 'reranker': 'cross_encoder'},
        'episode_config': {'search_methods': ['bm25'], 'reranker': 'cross_encoder'},
        'community_config': {'search_methods': ['bm25'], 'reranker': 'cross_encoder'},
        'limit': 3,
        'reranker_min_score': 0.5,
    },
    'no_scope': {},
    # B1: the cross-encoder gives the scores 1.0, 0.75, 0.5, 0.25, so 0.75 is a boundary and 0.8 drops one more.
    'community_cross_min_075': {'community_config': {'search_methods': ['bm25', 'cosine_similarity'], 'reranker': 'cross_encoder'}, 'reranker_min_score': 0.75},
    'community_cross_min_08': {'community_config': {'search_methods': ['bm25', 'cosine_similarity'], 'reranker': 'cross_encoder'}, 'reranker_min_score': 0.8},
    'episode_cross_min_075': {'episode_config': {'search_methods': ['bm25'], 'reranker': 'cross_encoder'}, 'reranker_min_score': 0.75},
    # The episode cross-encoder reads the reciprocal rank fusion with the same minimum first. It scores 1, 0.5, 0.33,
    # 0.25, 0.2, and 0.17 for ep-1 to ep-6. A minimum of 0.25 keeps four episodes and holds the cross-encoder score
    # 0.25 at its boundary. A minimum of 0.16 keeps all six, which are five contents, and drops the score 0.0.
    'episode_cross_min_025': {'episode_config': {'search_methods': ['bm25'], 'reranker': 'cross_encoder'}, 'reranker_min_score': 0.25},
    'episode_cross_min_016': {'episode_config': {'search_methods': ['bm25'], 'reranker': 'cross_encoder'}, 'reranker_min_score': 0.16},
    'episode_cross_min_08': {'episode_config': {'search_methods': ['bm25'], 'reranker': 'cross_encoder'}, 'reranker_min_score': 0.8},
    # B2: values that differ from the defaults (sim_min_score 0.6, bfs_max_depth 3).
    'edge_sim_03': {'edge_config': {'search_methods': ALL_EDGE, 'reranker': 'reciprocal_rank_fusion', 'sim_min_score': 0.3}},
    'node_sim_03': {'node_config': {'search_methods': ALL_EDGE, 'reranker': 'reciprocal_rank_fusion', 'sim_min_score': 0.3}},
    'node_bfs_only_depth_1': {'node_config': {'search_methods': ['breadth_first_search'], 'reranker': 'reciprocal_rank_fusion', 'bfs_max_depth': 1}},
    'node_bfs_only_depth_2': {'node_config': {'search_methods': ['breadth_first_search'], 'reranker': 'reciprocal_rank_fusion', 'bfs_max_depth': 2}},
    'edge_bfs_only_depth_2': {'edge_config': {'search_methods': ['breadth_first_search'], 'reranker': 'reciprocal_rank_fusion', 'bfs_max_depth': 2}},
    # With no origins given, the search expands the breadth-first search from the nodes that it found, to the same depth.
    'edge_all_depth_2': {'edge_config': {'search_methods': ALL_EDGE, 'reranker': 'reciprocal_rank_fusion', 'bfs_max_depth': 2}},
    'node_all_depth_2': {'node_config': {'search_methods': ALL_EDGE, 'reranker': 'reciprocal_rank_fusion', 'bfs_max_depth': 2}},
    # B3: an MMR reranker with no cosine method. Python embeds the query for it.
    'edge_mmr_bm25': {'edge_config': {'search_methods': ['bm25'], 'reranker': 'mmr'}},
    'node_mmr_bm25': {'node_config': {'search_methods': ['bm25'], 'reranker': 'mmr'}},
    # The three rrf filters that read reranker_min_score: the edge cross-encoder, the edge node distance, and the node node
    # distance. The methods are bm25 and cosine_similarity, so that rrf gives the score 1 / (i + 1) for each list. An
    # item that only one list holds at position 2 has the score 0.5: e-2 and e-4 for the edges, and n-2 and n-4 for the
    # nodes. All four are next to the center node n-1 or sit in the cross-encoder passages, so a minimum of 0.6 that the
    # rrf filter does not apply would let them through. The minimum 0.5 holds them at the boundary. The limit is the
    # default 10, so the slice of 2 * limit removes no candidate.
    'edge_cross_min_06': {'edge_config': {'search_methods': ['bm25', 'cosine_similarity'], 'reranker': 'cross_encoder'}, 'reranker_min_score': 0.6},
    'edge_cross_min_05': {'edge_config': {'search_methods': ['bm25', 'cosine_similarity'], 'reranker': 'cross_encoder'}, 'reranker_min_score': 0.5},
    'edge_distance_min_06': {'edge_config': {'search_methods': ['bm25', 'cosine_similarity'], 'reranker': 'node_distance'}, 'reranker_min_score': 0.6},
    'edge_distance_min_05': {'edge_config': {'search_methods': ['bm25', 'cosine_similarity'], 'reranker': 'node_distance'}, 'reranker_min_score': 0.5},
    'node_distance_min_06': {'node_config': {'search_methods': ['bm25', 'cosine_similarity'], 'reranker': 'node_distance'}, 'reranker_min_score': 0.6},
    'node_distance_min_05': {'node_config': {'search_methods': ['bm25', 'cosine_similarity'], 'reranker': 'node_distance'}, 'reranker_min_score': 0.5},
    # B5: a hybrid config for each scope, to see the limit of the inner gather.
    'community_rrf_hybrid': {'community_config': {'search_methods': ['bm25', 'cosine_similarity'], 'reranker': 'reciprocal_rank_fusion'}},
}


# The cancel grid. The caller cancels the search while the k-th call of one kind returns. The port sees a cancel only
# where code passes its token, and Python delivers it at the next await of each task. So a cancel that arrives while a
# task waits to resume after a finished call can end the search later in the port, with more spans or a full result:
# late cancel, see GAPS. The recorder keeps each point of the grid, and the test names the points where the port differs.
CANCEL_RECIPES = [
    'COMBINED_HYBRID_SEARCH_RRF',
    'COMBINED_HYBRID_SEARCH_MMR',
    'COMBINED_HYBRID_SEARCH_CROSS_ENCODER',
    'NODE_HYBRID_SEARCH_CROSS_ENCODER',
    'COMMUNITY_HYBRID_SEARCH_MMR',
    'EDGE_HYBRID_SEARCH_RRF',
    'NODE_HYBRID_SEARCH_RRF',
    'EDGE_HYBRID_SEARCH_NODE_DISTANCE',
]
CANCEL_COROUTINES = [None, 1, 2]
# The key of the input for each kind of call, and the key of the count in `state`.
CANCEL_KINDS = [('query', 'queries'), ('embed', 'embeds'), ('rank', 'ranks')]


def cancel_base(recipe, max_coroutines):
    case = {'fn': 'search', 'recipe': recipe}
    if max_coroutines is not None:
        case['max_coroutines'] = max_coroutines
    return case


def cases():
    out = []
    for recipe in RECIPES:
        out.append({'fn': 'search', 'recipe': recipe})
        out.append({'fn': 'search', 'recipe': recipe, 'center': 'n-1', 'bfs': ['n-1', 'n-2'], 'group_ids': ['g', 'grp-2']})
        out.append({'fn': 'search', 'recipe': recipe, 'overrides': {'limit': 2, 'reranker_min_score': 0.5}, 'center': 'n-4'})
    for name in CUSTOM:
        out.append({'fn': 'search', 'config': name})
        out.append({'fn': 'search', 'config': name, 'center': 'n-1', 'bfs': ['n-2']})
    out.append({'fn': 'search', 'recipe': 'COMBINED_HYBRID_SEARCH_RRF', 'query': '   \n\t'})
    out.append({'fn': 'search', 'recipe': 'COMBINED_HYBRID_SEARCH_RRF', 'query': ''})
    out.append({'fn': 'search', 'recipe': 'COMBINED_HYBRID_SEARCH_RRF', 'query': 'line one\nline two\r\nZoë 東京 😀'})
    for groups in ([], [''], ['', 'g'], ['g'], ['bad"g']):
        out.append({'fn': 'search', 'recipe': 'COMBINED_HYBRID_SEARCH_MMR', 'group_ids': groups})
    out.append({'fn': 'search', 'recipe': 'COMBINED_HYBRID_SEARCH_MMR', 'query_vector': [0.6, 0.8]})
    out.append({'fn': 'search', 'recipe': 'EDGE_HYBRID_SEARCH_RRF', 'query_vector': []})
    out.append({'fn': 'search', 'config': 'episode_rrf', 'query_vector': [0.5]})
    out.append({'fn': 'search', 'recipe': 'COMBINED_HYBRID_SEARCH_CROSS_ENCODER', 'max_coroutines': 1, 'center': 'n-1'})
    out.append({'fn': 'search', 'recipe': 'COMBINED_HYBRID_SEARCH_CROSS_ENCODER', 'no_tracer': True})
    # B2: an empty list of origins is not None, so the search does not expand the breadth-first search.
    for name in ('edge_rrf_all', 'node_rrf_all', 'edge_bfs_only', 'node_bfs_only'):
        out.append({'fn': 'search', 'config': name, 'bfs': []})
    out.append({'fn': 'search', 'recipe': 'COMBINED_HYBRID_SEARCH_RRF', 'bfs': []})
    # B5: the limit of the inner gather of each scope, and of the gather of the scopes.
    for name in ('edge_rrf_all', 'node_rrf_all', 'community_rrf_hybrid'):
        for limit in (1, 2):
            out.append({'fn': 'search', 'config': name, 'max_coroutines': limit})
    for fn in ('edge_search', 'node_search', 'episode_search', 'community_search'):
        out.append({'fn': fn, 'config': None})
        out.append({'fn': fn, 'config': 'all_scopes_min_score', 'no_tracer': True})
        out.append({'fn': fn, 'config': 'all_scopes_min_score', 'defaults': True})
    return out


def compact(value):
    """A long vector becomes its length and its sum."""
    if isinstance(value, list) and len(value) > 16 and all(isinstance(x, float) for x in value):
        return {'$vector': len(value), 'sum': math.fsum(value)}
    return value


def encode(value):
    """A value with its Python type."""
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
    if isinstance(value, dict) and '$vector' in value:
        return {'type': 'vector', 'len': value['$vector'], 'sum': value['sum']}
    if isinstance(value, (list, tuple)):
        return {'type': 'list', 'items': [encode(item) for item in value]}
    if isinstance(value, dict):
        return {'type': 'dict', 'items': [[key, encode(item)] for key, item in value.items()]}
    return {'type': type(value).__name__}


def respond(query, params):
    """The rows for one query: the rule of the C# test is the same."""
    if 'queryRelationships("edge_name_and_fact"' in query:
        return TABLES['edge_fulltext']
    if 'cosine(e.fact_embedding' in query:
        return TABLES['edge_similarity']
    if 'queryNodes("node_name_and_summary"' in query:
        return TABLES['node_fulltext']
    if 'cosine(n.name_embedding' in query:
        return TABLES['node_similarity']
    if 'queryNodes("episode_content"' in query:
        return TABLES['episode_fulltext']
    if 'queryNodes("community_name"' in query:
        return TABLES['community_fulltext']
    if 'cosine(c.name_embedding' in query:
        return TABLES['community_similarity']
    if 'bfs_origin_node_uuids' in params:
        return TABLES['edge_bfs'] if 'e.fact AS fact' in query else TABLES['node_bfs']
    if 'edge_uuids' in params:
        return [{'uuid': u, 'fact_embedding': EMBEDDINGS[u]} for u in params['edge_uuids'] if u in EMBEDDINGS]
    if 'community_uuids' in params:
        return [{'uuid': u, 'name_embedding': EMBEDDINGS[u]} for u in params['community_uuids'] if u in EMBEDDINGS]
    if 'center_uuid' in params:
        return [{'score': 1, 'uuid': u} for u in params['node_uuids'] if u in CONNECTED]
    if 'n.name_embedding AS name_embedding' in query:
        return [{'uuid': u, 'name_embedding': EMBEDDINGS[u]} for u in params['node_uuids'] if u in EMBEDDINGS]
    if 'count(*) AS score' in query:
        return [{'score': MENTIONS[u], 'uuid': u} for u in params['node_uuids'] if u in MENTIONS]
    raise ValueError('no rows for the query: ' + query)


def main() -> int:
    # A canceled search leaves coroutines that never ran, and Python warns about each one. The warning is no result.
    warnings.filterwarnings('ignore', message='coroutine .* was never awaited', category=RuntimeWarning)
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.search import search as search_module
    from graphiti_core.search import search_config_recipes
    from graphiti_core.search.search_config import SearchConfig
    from graphiti_core.search.search_filters import SearchFilters
    from graphiti_core.tracer import NoOpTracer

    from graphiti_core.search import search_utils

    events: list[dict] = []
    # A constant clock keeps the timed log lines the same on each run.
    search_module.time = lambda: 100.0
    search_utils.time = lambda: 100.0

    class Capture(logging.Handler):
        def __init__(self):
            super().__init__(logging.DEBUG)

        def emit(self, record):
            events.append({'kind': 'log', 'level': record.levelname, 'message': record.getMessage()})

    logger = logging.getLogger('graphiti_core')
    logger.setLevel(logging.DEBUG)
    logger.propagate = False
    logger.addHandler(Capture())

    # The count of calls that run at the same time, over the driver, the embedder, and the cross-encoder. A call runs
    # from its entry to its return. `main` is the task that runs the case, which a cancel at a query ends.
    state = {
        'in_flight': 0,
        'max_in_flight': 0,
        'queries': 0,
        'embeds': 0,
        'ranks': 0,
        'cancel_at_query': None,
        'cancel_at_embed': None,
        'cancel_at_rank': None,
        'main': None,
    }

    class FakeDriver:
        provider = GraphProvider.NEO4J
        fulltext_syntax = ''
        search_interface = None
        graph_operations_interface = None

        async def execute_query(self, cypher_query_, **kwargs):
            state['in_flight'] += 1
            state['max_in_flight'] = max(state['max_in_flight'], state['in_flight'])
            try:
                await asyncio.sleep(0)
                events.append(
                    {'kind': 'query', 'query': cypher_query_, 'keys': list(kwargs), 'values': [encode(compact(v)) for v in kwargs.values()]}
                )
                state['queries'] += 1
                if state['queries'] == state['cancel_at_query']:
                    state['main'].cancel()
                return json.loads(json.dumps(respond(cypher_query_, kwargs))), None, None
            finally:
                state['in_flight'] -= 1

    class FakeEmbedder:
        async def create(self, input_data):
            state['in_flight'] += 1
            state['max_in_flight'] = max(state['max_in_flight'], state['in_flight'])
            try:
                await asyncio.sleep(0)
                events.append({'kind': 'embed', 'input': encode(input_data)})
                state['embeds'] += 1
                if state['embeds'] == state['cancel_at_embed']:
                    state['main'].cancel()
                return [1.0, 0.0]
            finally:
                state['in_flight'] -= 1

    class FakeCrossEncoder:
        async def rank(self, query, passages):
            state['in_flight'] += 1
            state['max_in_flight'] = max(state['max_in_flight'], state['in_flight'])
            try:
                await asyncio.sleep(0)
                events.append({'kind': 'rank', 'query': query, 'passages': list(passages)})
                state['ranks'] += 1
                if state['ranks'] == state['cancel_at_rank']:
                    state['main'].cancel()
                return [(passage, 1.0 - 0.25 * i) for i, passage in enumerate(reversed(passages))]
            finally:
                state['in_flight'] -= 1

    class RecordingSpan:
        def __init__(self):
            self.attributes = {}
            self.status = None
            self.exception = None

        def add_attributes(self, attributes):
            self.attributes.update(attributes)

        def set_status(self, status, description=None):
            self.status = [status, description]

        def record_exception(self, exception):
            self.exception = type(exception).__name__

    class RecordingTracer:
        @contextmanager
        def start_span(self, name):
            span = RecordingSpan()
            events.append({'kind': 'span', 'name': name})
            try:
                yield span
            finally:
                events.append(
                    {'kind': 'end', 'name': name, 'attributes': encode(span.attributes), 'status': span.status, 'exception': span.exception}
                )

    def config_for(case):
        if 'recipe' in case:
            recipe = getattr(search_config_recipes, case['recipe'])
            if 'overrides' in case:
                return SearchConfig(**{**recipe.model_dump(mode='json'), **case['overrides']})
            return recipe
        return SearchConfig(**json.loads(json.dumps(CUSTOM[case['config']])))

    def dump(results):
        return {
            'edges': [e.uuid for e in results.edges],
            'edge_reranker_scores': encode(results.edge_reranker_scores),
            'nodes': [n.uuid for n in results.nodes],
            'node_reranker_scores': encode(results.node_reranker_scores),
            'episodes': [e.uuid for e in results.episodes],
            'episode_reranker_scores': encode(results.episode_reranker_scores),
            'communities': [c.uuid for c in results.communities],
            'community_reranker_scores': encode(results.community_reranker_scores),
        }

    async def run(case):
        driver = FakeDriver()
        tracer = NoOpTracer() if case.get('no_tracer') else RecordingTracer()
        fn = case['fn']
        state['main'] = asyncio.current_task()
        if fn == 'search':
            clients = SimpleNamespace(
                driver=driver,
                embedder=FakeEmbedder(),
                cross_encoder=FakeCrossEncoder(),
                tracer=tracer,
                max_coroutines=case.get('max_coroutines'),
            )
            results = await search_module.search(
                clients,
                case.get('query', 'who works where'),
                case.get('group_ids'),
                config_for(case),
                SearchFilters(),
                center_node_uuid=case.get('center'),
                bfs_origin_node_uuids=case.get('bfs'),
                query_vector=case.get('query_vector'),
            )
            return dump(results)
        config = None if case['config'] is None else config_for(case)
        scope = {
            'edge_search': 'edge_config',
            'node_search': 'node_config',
            'episode_search': 'episode_config',
            'community_search': 'community_config',
        }[fn]
        scope_config = None if config is None else getattr(config, scope)
        func = getattr(search_module, fn)
        query_vector = [1.0, 0.0]
        common = (driver, FakeCrossEncoder(), 'who works where', query_vector, ['g'], scope_config)
        tracer_arg = None if case.get('no_tracer') else tracer
        if case.get('defaults'):
            if fn in ('edge_search', 'node_search', 'episode_search'):
                items, scores = await func(*common, SearchFilters())
            else:
                items, scores = await func(*common)
        elif fn in ('edge_search', 'node_search'):
            items, scores = await func(*common, SearchFilters(), 'n-1', None, 3, 0.5, tracer_arg, max_coroutines=2)
        elif fn == 'episode_search':
            items, scores = await func(*common, SearchFilters(), 3, 0.5, tracer_arg, max_coroutines=2)
        else:
            items, scores = await func(*common, 3, 0.5, tracer_arg, max_coroutines=2)
        return {'items': [item.uuid for item in items], 'scores': encode(scores)}

    def execute(case):
        events.clear()
        state.update(
            in_flight=0,
            max_in_flight=0,
            queries=0,
            embeds=0,
            ranks=0,
            cancel_at_query=case.get('cancel_at_query'),
            cancel_at_embed=case.get('cancel_at_embed'),
            cancel_at_rank=case.get('cancel_at_rank'),
            main=None,
        )
        outcome = {}
        try:
            outcome['result'] = asyncio.run(run(case))
            outcome['error'] = None
        except asyncio.CancelledError as error:
            # CancelledError is a BaseException, so `except Exception` in the library does not see it.
            outcome['result'] = None
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            outcome['result'] = None
            outcome['error'] = {'type': type(error).__name__, 'text': str(error)}
        outcome['events'] = list(events)
        outcome['max_in_flight'] = state['max_in_flight']
        return outcome, {kind: state[key] for kind, key in CANCEL_KINDS}

    results = []
    for case in cases():
        outcome, _ = execute(case)
        results.append({'input': case, 'output': outcome})

    # The cancel grid: each k from 1 to the count of calls that the search makes with no cancel.
    cancel_results = []
    for recipe in CANCEL_RECIPES:
        for max_coroutines in CANCEL_COROUTINES:
            base = cancel_base(recipe, max_coroutines)
            _, counts = execute(base)
            for kind, _key in CANCEL_KINDS:
                for k in range(1, counts[kind] + 1):
                    case = {**base, f'cancel_at_{kind}': k}
                    outcome, _ = execute(case)
                    cancel_results.append({'input': case, 'output': outcome})

    python = '.'.join(str(part) for part in sys.version_info[:3])
    document = {
        'python': python,
        'source': 'graphiti_core/search/search.py',
        'tables': TABLES,
        'embeddings': EMBEDDINGS,
        'connected': CONNECTED,
        'mentions': MENTIONS,
        'configs': CUSTOM,
        'recipes': {name: getattr(search_config_recipes, name).model_dump(mode='json') for name in RECIPES},
        'cases': results,
    }
    # The cancel file holds the fixture of the first file, so that the test can see that the two files agree.
    cancel_document = {
        'python': python,
        'source': 'graphiti_core/search/search.py',
        'tables': TABLES,
        'embeddings': EMBEDDINGS,
        'connected': CONNECTED,
        'mentions': MENTIONS,
        'cases': cancel_results,
    }
    files = [
        (OUT, json.dumps(document, ensure_ascii=True, separators=(',', ':'), allow_nan=False) + '\n', len(results)),
        (OUT_CANCEL, json.dumps(cancel_document, ensure_ascii=True, separators=(',', ':'), allow_nan=False) + '\n', len(cancel_results)),
    ]
    if '--check' in sys.argv[1:]:
        for path, text, count in files:
            if not path.exists() or path.read_text(encoding='utf-8') != text:
                print(f'{path.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
                return 1
            print(f'{path.relative_to(ROOT)}: current, {count} cases')
        return 0
    for path, text, count in files:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8')
        print(f'{path.relative_to(ROOT)}: {count} cases, {len(text) // 1024} KB')
    return 0


if __name__ == '__main__':
    sys.exit(main())
