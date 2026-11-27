"""Records results of graphiti_core/search/search_config.py, search_config_recipes.py, and search_helpers.py from real
Python, for the evaluation tests.

The judge has 61 cases for these modules. This file adds the module constants, each enum member, each of the 16
recipes field by field, and calls with generated inputs from a fixed seed (port/RULES.md, R23 and R25):

- the five config models and SearchResults built from keyword data in lax mode, with valid and wrong values;
- SearchResults.merge over generated lists of results;
- format_edge_date_range and search_results_to_context_string over generated edges, nodes, episodes, and
  communities, with aware, naive, and offset datetimes, None, and text with quotes, line breaks, and non-ASCII.

    .upstream/graphiti/.venv/bin/python -B port/tools/record_search_config_values.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_search_config_values.py --check

It writes tests/Port.Evaluation/values/search_config.json. --check writes nothing and exits 1 if the file differs
from what the run would write. The tool needs Python 3.12 and the pinned upstream in .upstream/graphiti, or in the
checkout that GRAPHITI_UPSTREAM names. It changes nothing there. Nothing here uses the network.

A datetime in an input is written as its isoformat() text under a key that ends in `_at`. A ValidationError is
recorded as its text without the input value, the input type, and the URL line, which is the text of the port's
ValidationException.
"""
from __future__ import annotations

import json
import logging
import os
import random
import re
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
# GRAPHITI_UPSTREAM names another checkout of the pinned upstream, for a worktree that has no .upstream.
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'search_config.json'

RECIPES = [
    'COMBINED_HYBRID_SEARCH_RRF', 'COMBINED_HYBRID_SEARCH_MMR', 'COMBINED_HYBRID_SEARCH_CROSS_ENCODER',
    'EDGE_HYBRID_SEARCH_RRF', 'EDGE_HYBRID_SEARCH_MMR', 'EDGE_HYBRID_SEARCH_NODE_DISTANCE',
    'EDGE_HYBRID_SEARCH_EPISODE_MENTIONS', 'EDGE_HYBRID_SEARCH_CROSS_ENCODER', 'NODE_HYBRID_SEARCH_RRF',
    'NODE_HYBRID_SEARCH_MMR', 'NODE_HYBRID_SEARCH_NODE_DISTANCE', 'NODE_HYBRID_SEARCH_EPISODE_MENTIONS',
    'NODE_HYBRID_SEARCH_CROSS_ENCODER', 'COMMUNITY_HYBRID_SEARCH_RRF', 'COMMUNITY_HYBRID_SEARCH_MMR',
    'COMMUNITY_HYBRID_SEARCH_CROSS_ENCODER',
]
ENUMS = ['EdgeSearchMethod', 'NodeSearchMethod', 'EpisodeSearchMethod', 'CommunitySearchMethod', 'EdgeReranker',
         'NodeReranker', 'EpisodeReranker', 'CommunityReranker']
CONFIGS = {
    'EdgeSearchConfig': ('EdgeSearchMethod', 'EdgeReranker'),
    'NodeSearchConfig': ('NodeSearchMethod', 'NodeReranker'),
    'EpisodeSearchConfig': ('EpisodeSearchMethod', 'EpisodeReranker'),
    'CommunitySearchConfig': ('CommunitySearchMethod', 'CommunityReranker'),
}
ALL_VALUES = ['cosine_similarity', 'bm25', 'breadth_first_search', 'reciprocal_rank_fusion', 'node_distance',
              'episode_mentions', 'mmr', 'cross_encoder', 'bfs', 'rrf', 'BM25', '', 'unknown']
FLOATS = [0.25, 1, 0, -3, 0.5, '0.5', ' 2 ', '1e3', 'abc', '', True, False, None, 'nan', 'inf', '1_000.5', [1]]
INTS = [2, 0, -1, '3', ' 4 ', 2.0, 2.5, True, False, 'x', None, '1_0', '2.00', [3]]
TEXTS = ['Ana works at Acme', 'He said "hi"\nthen\\left', 'line1\nline2\ttab', '東京 \U0001F600 café', 'Émile',
         '', "it's", '<tag> & </tag>', '\x00\x1f', 'a' * 40]

rng = random.Random(20261127)


def text_of(e) -> str:
    """str(e) without the input value, the input type, and the URL line."""
    lines = []
    for line in str(e).split('\n'):
        if line.startswith('    For further information visit '):
            continue
        lines.append(re.sub(r', input_value=.*, input_type=[A-Za-z_]+\]$', ']', line))
    return '\n'.join(lines)


def plain(value):
    """model_dump() with each enum member as its value. model_dump(mode='json') is not used: it keeps nan and inf,
    where the port's Pydantic.JsonValue gives None (a finding for the lead)."""
    from enum import Enum

    if isinstance(value, Enum):
        return value.value
    if isinstance(value, dict):
        return {k: plain(v) for k, v in value.items()}
    if isinstance(value, list):
        return [plain(v) for v in value]
    return value


def pick(pool):
    return rng.choice(pool)


def rand_dt():
    r = rng.random()
    if r < 0.2:
        return None
    base = datetime(2026, 1, 1) + timedelta(seconds=rng.randint(0, 10**8), microseconds=rng.choice([0, 0, 1, 250000, 999999]))
    if r < 0.35:
        return base
    tz = rng.choice([timezone.utc, timezone.utc, timezone(timedelta(hours=5, minutes=30)), timezone(timedelta(hours=-8))])
    return base.replace(tzinfo=tz)


def iso(dt):
    return None if dt is None else dt.isoformat()


def config_data(name):
    methods_enum, _ = CONFIGS[name]
    data = {}
    r = rng.random()
    if r < 0.85:
        data['search_methods'] = [pick(ALL_VALUES) if rng.random() < 0.3 else None for _ in range(rng.randint(0, 3))]
    elif r < 0.92:
        data['search_methods'] = pick(['bm25', 5, None, {'a': 1}])
    for key, pool in [('reranker', ALL_VALUES + [3, None]), ('sim_min_score', FLOATS), ('mmr_lambda', FLOATS),
                      ('bfs_max_depth', INTS)]:
        if rng.random() < 0.45:
            data[key] = pick(pool)
    return data, methods_enum


def fill_methods(data, members):
    if isinstance(data.get('search_methods'), list):
        data['search_methods'] = [v if v is not None else pick(members) for v in data['search_methods']]
    if 'reranker' in data and rng.random() < 0.6:
        data['reranker'] = pick(data['_rerankers'])
    data.pop('_rerankers', None)


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    logging.disable(logging.CRITICAL)
    from pydantic import ValidationError

    from graphiti_core.edges import EntityEdge
    from graphiti_core.nodes import CommunityNode, EntityNode, EpisodeType, EpisodicNode
    from graphiti_core.search import search_config as sc
    from graphiti_core.search import search_config_recipes as recipes
    from graphiti_core.search.search_helpers import format_edge_date_range, search_results_to_context_string

    out: dict = {'python': '.'.join(str(p) for p in sys.version_info[:3]),
                 'source': 'graphiti_core/search/search_config.py, search_config_recipes.py, search_helpers.py'}
    out['constants'] = {'DEFAULT_SEARCH_LIMIT': sc.DEFAULT_SEARCH_LIMIT}
    out['enums'] = {name: [[m.name, m.value] for m in getattr(sc, name)] for name in ENUMS}
    out['recipes'] = {name: plain(getattr(recipes, name).model_dump()) for name in RECIPES}
    # Pydantic keeps the int default 0 of reranker_min_score. The port holds a float (Pydantic.DumpFields).
    out['recipe_types'] = {name: {'limit': type(getattr(recipes, name).limit).__name__,
                                  'reranker_min_score': type(getattr(recipes, name).reranker_min_score).__name__}
                           for name in RECIPES}

    def validate(model, data):
        try:
            return {'ok': plain(model(**data).model_dump())}
        except ValidationError as e:
            return {'error': 'ValidationError', 'text': text_of(e)}

    validations = []
    for name in CONFIGS:
        members = [m.value for m in getattr(sc, CONFIGS[name][0])]
        rerankers = [m.value for m in getattr(sc, CONFIGS[name][1])]
        for _ in range(60):
            data, _ = config_data(name)
            data['_rerankers'] = rerankers
            fill_methods(data, members)
            validations.append({'model': name, 'data': data, **validate(getattr(sc, name), data)})
    for _ in range(80):
        data = {}
        for key, cfg in [('edge_config', 'EdgeSearchConfig'), ('node_config', 'NodeSearchConfig'),
                         ('episode_config', 'EpisodeSearchConfig'), ('community_config', 'CommunitySearchConfig')]:
            r = rng.random()
            if r < 0.4:
                members = [m.value for m in getattr(sc, CONFIGS[cfg][0])]
                d, _ = config_data(cfg)
                d['_rerankers'] = [m.value for m in getattr(sc, CONFIGS[cfg][1])]
                fill_methods(d, members)
                data[key] = d
            elif r < 0.5:
                data[key] = pick([None, 'x', 3, []])
        if rng.random() < 0.5:
            data['limit'] = pick(INTS)
        if rng.random() < 0.5:
            data['reranker_min_score'] = pick(FLOATS)
        validations.append({'model': 'SearchConfig', 'data': data, **validate(sc.SearchConfig, data)})
    for _ in range(30):
        data = {}
        for key in ['edge_reranker_scores', 'node_reranker_scores', 'episode_reranker_scores', 'community_reranker_scores']:
            if rng.random() < 0.5:
                data[key] = [pick(FLOATS[:12]) for _ in range(rng.randint(0, 3))] if rng.random() < 0.9 else pick(['x', 1])
        if rng.random() < 0.2:
            data['edges'] = pick(['x', [1], [{}]])
        validations.append({'model': 'SearchResults', 'data': data, **validate(sc.SearchResults, data)})
    out['validate'] = validations

    def edge_data(i):
        return {'uuid': f'e{i}', 'group_id': 'g', 'source_node_uuid': 'a', 'target_node_uuid': 'b', 'name': 'REL',
                'fact': pick(TEXTS), 'created_at': iso(datetime(2026, 1, 1, tzinfo=timezone.utc)),
                'valid_at': iso(rand_dt()), 'invalid_at': iso(rand_dt())}

    def node_data(i):
        return {'uuid': f'n{i}', 'name': pick(TEXTS), 'group_id': 'g',
                'created_at': iso(datetime(2026, 1, 1, tzinfo=timezone.utc)), 'summary': pick(TEXTS)}

    def episode_data(i):
        return {'uuid': f'ep{i}', 'name': 'ep', 'group_id': 'g', 'source': 'text', 'source_description': pick(TEXTS),
                'content': pick(TEXTS), 'valid_at': iso(datetime(2026, 3, 1, tzinfo=timezone.utc)),
                'created_at': iso(datetime(2026, 3, 1, tzinfo=timezone.utc))}

    def community_data(i):
        return {'uuid': f'c{i}', 'name': pick(TEXTS), 'group_id': 'g',
                'created_at': iso(datetime(2026, 3, 1, tzinfo=timezone.utc)), 'summary': pick(TEXTS)}

    def parse(d):
        return {k: (datetime.fromisoformat(v) if k.endswith('_at') and v is not None else v) for k, v in d.items()}

    def build_edge(d):
        return EntityEdge(**parse(d))

    def results_data(n):
        return {
            'edges': [edge_data(i) for i in range(rng.randint(0, n))],
            'edge_reranker_scores': [round(rng.random(), 3) for _ in range(rng.randint(0, n))],
            'nodes': [node_data(i) for i in range(rng.randint(0, n))],
            'node_reranker_scores': [round(rng.random(), 3) for _ in range(rng.randint(0, n))],
            'episodes': [episode_data(i) for i in range(rng.randint(0, n))],
            'episode_reranker_scores': [round(rng.random(), 3) for _ in range(rng.randint(0, n))],
            'communities': [community_data(i) for i in range(rng.randint(0, n))],
            'community_reranker_scores': [round(rng.random(), 3) for _ in range(rng.randint(0, n))],
        }

    def build_results(d):
        return sc.SearchResults(
            edges=[build_edge(e) for e in d['edges']],
            edge_reranker_scores=d['edge_reranker_scores'],
            nodes=[EntityNode(**parse(x)) for x in d['nodes']],
            node_reranker_scores=d['node_reranker_scores'],
            episodes=[EpisodicNode(**{**parse(x), 'source': EpisodeType(x['source'])}) for x in d['episodes']],
            episode_reranker_scores=d['episode_reranker_scores'],
            communities=[CommunityNode(**parse(x)) for x in d['communities']],
            community_reranker_scores=d['community_reranker_scores'],
        )

    def summary(r):
        return {'edges': [e.uuid for e in r.edges], 'edge_reranker_scores': r.edge_reranker_scores,
                'nodes': [n.uuid for n in r.nodes], 'node_reranker_scores': r.node_reranker_scores,
                'episodes': [n.uuid for n in r.episodes], 'episode_reranker_scores': r.episode_reranker_scores,
                'communities': [n.uuid for n in r.communities],
                'community_reranker_scores': r.community_reranker_scores}

    merges = []
    for _ in range(40):
        inputs = [results_data(3) for _ in range(rng.randint(0, 4))]
        merged = sc.SearchResults.merge([build_results(d) for d in inputs])
        merges.append({'inputs': inputs, 'result': summary(merged)})
    out['merge'] = merges

    out['format_edge_date_range'] = []
    for i in range(80):
        d = edge_data(i)
        out['format_edge_date_range'].append({'edge': d, 'result': format_edge_date_range(build_edge(d))})

    out['context_string'] = []
    for _ in range(40):
        d = results_data(3)
        out['context_string'].append({'results': d, 'result': search_results_to_context_string(build_results(d))})

    text = json.dumps(out, ensure_ascii=True, indent=1) + '\n'
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(validations) + len(merges) + 120} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(validations) + len(merges) + 120} cases, {len(text.encode("utf-8"))} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
