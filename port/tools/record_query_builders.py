"""Records results of the query builders of three modules from real Python, for the evaluation tests.

The modules are graphiti_core/graph_queries.py, graphiti_core/models/edges/edge_db_queries.py, and
graphiti_core/models/nodes/node_db_queries.py. The judge has one case or two for most functions, and the
upstream tests look for a few words in the text. Neither finds a changed space, a wrong branch for one
provider, or a label that enters the text in the wrong form. This file gives the evaluation tests calls with
each provider value, each flag, and labels that are safe, unsafe, empty, and not ASCII, so that the port can
be compared with them character for character (port/RULES.md, R25).

    .upstream/graphiti/.venv/bin/python port/tools/record_query_builders.py
    .upstream/graphiti/.venv/bin/python port/tools/record_query_builders.py --check

It writes tests/Port.Evaluation/values/query_builders.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The tool needs Python 3.12 and the pinned upstream in
.upstream/graphiti. It changes nothing there. Nothing here uses the network.

Each case holds the module, the function name, the arguments, and the result: the value, or the exception
class and its message. A tuple is {"$tuple": [...]}, bytes are {"$bytes": [ints]}, and a dict with a key that is
not a str is {"$pydict": [[key, value], ...]}. A case whose function name starts with "const:" holds the value of
the module constant of that name. A call with fewer arguments than the function has uses its defaults. The seed
is fixed.

The environment variable GRAPHITI_UPSTREAM names the upstream folder, for a worktree that has no .upstream.
"""
from __future__ import annotations

import json
import os
import random
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ['GRAPHITI_UPSTREAM']) if 'GRAPHITI_UPSTREAM' in os.environ else ROOT / '.upstream' / 'graphiti'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'query_builders.json'

rng = random.Random(20261126)
records: list[dict] = []

PROVIDERS = ['NEO4J', 'FALKORDB', 'NEPTUNE', 'RELATE']
LABEL_STRINGS = [
    'Entity', 'Entity:Person', 'Entity:Person:Organization', '_Private1', '::Entity::Person:', '', ':::', ' ',
    '1Bad', 'Has Space', 'my-label', 'Entity:Entity`) WITH n MATCH (x) DETACH DELETE x //', 'Ent"ity', "Ent'ity",
    'A{b}', 'A;MATCH', 'A|B', 'Café', 'Café', 'Ent\U0001F600', '人物', 'A\nB', 'A\\B', 'A.B',
    'Person\n', 'Entity:Per son', 'Αlpha', 'x' * 300,
]
LABEL_VALUES = [
    ['Entity', 'Person'], ['Entity'], [], [''], ['', 'Entity', ''], ['Entity', 'a b'], ['Entity', 'Entity`) //'],
    ['Café'], ['Café', 'Entity'], ['人物'], ['_x', 'Y1', 'z'], ['Entity:Person'], ['0'],
    [0, 'Entity'], [1], [None, 'Entity'], 'Entity:Person', 'Entity', '', ':', 'a b', None, 5, 'Café',
    {'Entity': 1, 'Person': 2}, ['Entity', 'Person', 'Organization', 'Place', 'Thing'],
    # Values of the model that are not a list or a str: a tuple, bytes, a dict with str keys, a dict with other
    # keys, and scalars. A node dict is open data, so each one can reach the builders.
    ('Entity', 'Person'), ('Entity',), (), b'', b'AB', {}, {1: 'a'}, {0: 'a', 'Entity': 1}, {'Entity': 1},
    True, False, 0, 1.5, ['Entity', 'Entity'], [False, 'Entity'], [0.0, 'Entity'], [[], 'Entity'], [{}, 'Entity'],
    [True], [1.5], [b'A'], ['Entity', 'bad label', 5], ['bad label', None], [()], [b''],
]


def enc(value):
    if isinstance(value, tuple):
        return {'$tuple': [enc(v) for v in value]}
    if isinstance(value, bytes):
        return {'$bytes': list(value)}
    if isinstance(value, list):
        return [enc(v) for v in value]
    if isinstance(value, dict):
        if all(isinstance(k, str) for k in value):
            return {k: enc(v) for k, v in value.items()}
        return {'$pydict': [[enc(k), enc(v)] for k, v in value.items()]}
    return value


def call(module: str, name: str, fn, args: list) -> None:
    try:
        result = {'ok': enc(fn(*args))}
    except BaseException as e:  # noqa: BLE001 - the tool records each exception
        result = {'err': [type(e).__name__, str(e)]}
    records.append({'m': module, 'f': name, 'a': enc(args), 'r': result})


CONSTANTS = {
    'graph_queries': ['NEO4J_TO_FALKORDB_MAPPING'],
    'edge_db_queries': [
        'EPISODIC_EDGE_SAVE', 'EPISODIC_EDGE_RETURN', 'COMMUNITY_EDGE_RETURN', 'HAS_EPISODE_EDGE_SAVE',
        'HAS_EPISODE_EDGE_RETURN', 'NEXT_EPISODE_EDGE_SAVE', 'NEXT_EPISODE_EDGE_RETURN',
    ],
    'node_db_queries': [
        'EPISODIC_NODE_RETURN', 'EPISODIC_NODE_RETURN_NEPTUNE', 'COMMUNITY_NODE_RETURN',
        'COMMUNITY_NODE_RETURN_NEPTUNE', 'SAGA_NODE_RETURN', 'SAGA_NODE_RETURN_NEPTUNE',
    ],
}


def node(i: int, labels) -> dict:
    return {
        'uuid': f'n{i}', 'name': f'Name {i}', 'group_id': 'g', 'summary': 's', 'created_at': '2024-01-01T00:00:00Z',
        'name_embedding': [0.1, 0.2], 'labels': labels,
    }


def record(gp, gq, eq, nq) -> None:
    def prov(name):
        return getattr(gp, name)

    modules = {'graph_queries': gq, 'edge_db_queries': eq, 'node_db_queries': nq}
    for module, names in CONSTANTS.items():
        for name in names:
            call(module, 'const:' + name, lambda m=module, n=name: enc(getattr(modules[m], n)), [])

    for p in PROVIDERS:
        call('graph_queries', 'get_range_indices', lambda x: gq.get_range_indices(prov(x)), [p])
        call('graph_queries', 'get_fulltext_indices', lambda x: gq.get_fulltext_indices(prov(x)), [p])
        for name in ['node_name_and_summary', 'community_name', 'episode_content', 'edge_name_and_fact', 'zzz', '',
                     'x"y', "a`b'c", 'café \U0001F600 é', '漢字']:
            for query in ['$query', '"abc"', '']:
                limit = rng.choice([0, 1, 10, -5, 1000])
                call('graph_queries', 'get_nodes_query', lambda n, q, l, x: gq.get_nodes_query(n, q, l, prov(x)),
                     [name, query, limit, p])
            call('graph_queries', 'get_relationships_query',
                 lambda n, l, x: gq.get_relationships_query(n, l, prov(x)), [name, rng.choice([0, 5, -1]), p])
        for a, b in [('n.name_embedding', '$search_vector'), ('', ''), ('é\U0001F600', '漢'), ('a', 'b')]:
            call('graph_queries', 'get_vector_cosine_func_query',
                 lambda a, b, x: gq.get_vector_cosine_func_query(a, b, prov(x)), [a, b, p])

        for aoss in [False, True]:
            for fn in ['get_entity_edge_save_query', 'get_entity_edge_save_bulk_query',
                       'get_entity_edge_update_bulk_query']:
                call('edge_db_queries', fn, lambda x, a, fn=fn: getattr(eq, fn)(prov(x), a), [p, aoss])
        for fn in ['get_entity_edge_save_query', 'get_entity_edge_save_bulk_query',
                   'get_entity_edge_update_bulk_query']:
            call('edge_db_queries', fn, lambda x, fn=fn: getattr(eq, fn)(prov(x)), [p])
        for fn in ['get_episodic_edge_save_bulk_query', 'get_entity_edge_return_query',
                   'get_community_edge_save_query']:
            call('edge_db_queries', fn, lambda x, fn=fn: getattr(eq, fn)(prov(x)), [p])

        for fn in ['get_episode_node_save_query', 'get_episode_node_save_bulk_query', 'get_entity_node_return_query',
                   'get_community_node_save_query', 'get_saga_node_save_query']:
            call('node_db_queries', fn, lambda x, fn=fn: getattr(nq, fn)(prov(x)), [p])
        for labels in LABEL_STRINGS:
            for aoss in [False, True]:
                call('node_db_queries', 'get_entity_node_save_query',
                     lambda x, l, a: nq.get_entity_node_save_query(prov(x), l, a), [p, labels, aoss])
            call('node_db_queries', 'get_entity_node_save_query',
                 lambda x, l: nq.get_entity_node_save_query(prov(x), l), [p, labels])

        node_sets = [[], [node(1, ['Entity', 'Person'])], [node(1, ['Entity']), node(2, ['Entity', 'Place'])],
                     [{'uuid': 'n1'}], [{'uuid': 'n1', 'name': 'Ana', 'extra': {'k': [1, 2]}}]]
        for labels in LABEL_VALUES:
            node_sets.append([node(1, labels)])
            node_sets.append([node(1, ['Entity']), node(2, labels)])
        for _ in range(3):
            node_sets.append([node(i, rng.choice(LABEL_VALUES)) for i in range(rng.randint(1, 4))])
        for nodes in node_sets:
            for aoss in [False, True]:
                for fn in ['get_entity_node_save_bulk_query', 'get_entity_node_update_bulk_query']:
                    call('node_db_queries', fn, lambda x, ns, a, fn=fn: getattr(nq, fn)(prov(x), ns, a),
                         [p, nodes, aoss])
            if len(nodes) <= 1:
                for fn in ['get_entity_node_save_bulk_query', 'get_entity_node_update_bulk_query']:
                    call('node_db_queries', fn, lambda x, ns, fn=fn: getattr(nq, fn)(prov(x), ns), [p, nodes])


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core import graph_queries
    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.models.edges import edge_db_queries
    from graphiti_core.models.nodes import node_db_queries

    record(GraphProvider, graph_queries, edge_db_queries, node_db_queries)
    lines = [json.dumps(r, ensure_ascii=True, allow_nan=True, separators=(',', ':')) for r in records]
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3])
        + '","source":["graphiti_core/graph_queries.py","graphiti_core/models/edges/edge_db_queries.py",'
        + '"graphiti_core/models/nodes/node_db_queries.py"],"cases":[\n'
        + ',\n'.join(lines) + '\n]}\n'
    )
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(records)} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(records)} cases, {len(text.encode("utf-8"))} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
