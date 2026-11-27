"""Records results of graphiti_core/ingestion/delta.py from real Python (port/RULES.md, R18, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_delta.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_delta.py --check

Plan 0004, layer 8, packet P8b. The module has no network call. The tool calls each function and each method with
generated inputs from a fixed seed, and records the result or the type and the text of the exception:

- _unit_matrix, with rows of each width from 0 to 300 (the float32 norm sums in pairs above 8 and 128 values), rows of
  zeros, an empty list, rows of different lengths, and values that float32 cannot hold;
- _fuzzy_query and _fuzzy_match, with names of high and low entropy and near duplicates;
- _stack and _stack_uuids, with empty matrices and matrices of other widths;
- _top_matches, with equal scores, a score exactly on DEFAULT_MIN_SCORE (0.6) and on NODE_DEDUP_COSINE_MIN_SCORE, 0, 1,
  and more matches than the limit, a limit of 0 and a negative limit, and query vectors of another width;
- patch_as_node;
- load_delta_digests with the final change sets of units in any order, and each method of Delta on the result.

A float32 value is recorded exactly. The C# test compares each matrix value exactly and each list of UUIDs exactly.
np.argsort sorts 17 or more scores in an order that is not stable (PyNumpy.ArgSort ports it). The cases of the reviews
of packet P8b have 17, 20, and 22 candidates with equal scores, a row of nan with a limit below the count, a row of
width 255 that the pairwise split rounds in another way than a plain loop, a fuzzy score of exactly 0.9, two equal
best fuzzy scores, two patches of one node, and more similar edges and cosine node matches than the limits.

The digest cache: a load of the same units gives the same digest objects, and the load of DIGEST_CACHE_SIZE + 1 other
units drops the first one. The case records `is` of each digest against the digest of the first load. Three sequences
test the limits: A and then DIGEST_CACHE_SIZE - 1 other units keep A; A, B, A again, and DIGEST_CACHE_SIZE - 1 other
units keep A and drop B; two payloads with the same group, seq, status, and length give two digests.

Python iterates a set in hash order. delta.py iterates one set, `candidate_ids` in _fuzzy_match (RULES.md R14). The
tool binds the name `set` of delta and dedup_helpers to the insertion-order set of record_maintenance_helpers.py.

`created_at` of patch_as_node is the clock of the call, and the tool leaves it out. A payload is the bytes that Python
compressed, as hex, so the port reads bytes that Python wrote (GAPS F39).

It writes tests/Port.Evaluation/values/delta.json. --check writes nothing and exits 1 if the file differs from what
the run would write. The tool needs Python 3.12 and the pinned upstream in the folder of the variable
GRAPHITI_UPSTREAM, or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import json
import math
import os
import random
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'delta.json'

SEED = 20261130
rng = random.Random(SEED)

NAMES = ['Alice Johnson', 'alice  johnson', 'Alice Jonson', 'Alice Johnsen', 'ALICE JOHNSON!', 'Bob', 'Acme Corporation',
         'Acme Corporatoin', 'acme corporation', 'Massachusetts Institute of Technology',
         'Massachusetts Institute of Technolgy', 'aaaaaa', '東京', 'Jean-Luc Picard', 'Jean Luc Picard', 'Zoë Durand',
         'Zoe Durand', 'International Business Machines', 'International Business Machine', 'x', '']
LABELS = ['Person', 'Company', 'Place']
DT = '2026-01-01T00:00:00+00:00'


def num(x: float):
    return x if math.isfinite(x) else {'$f': repr(x)}


def matrix_view(m) -> dict:
    return {'shape': list(m.shape), 'rows': [[num(float(v)) for v in row] for row in m.tolist()]}


def error_view(error) -> list:
    return [type(error).__name__, str(error)]


def vector(dim: int, scale: float = 1.0) -> list[float]:
    return [round(rng.uniform(-1, 1) * scale, rng.choice([1, 2, 6, 17])) for _ in range(dim)]


def node_data(uuid: str, name: str | None = None, labels=None, attributes=None, embedding=None, summary='') -> dict:
    data = {'uuid': uuid, 'name': rng.choice(NAMES) if name is None else name, 'group_id': 'g',
            'labels': ['Entity'] + (rng.sample(LABELS, rng.randint(0, 2)) if labels is None else labels),
            'created_at': DT, 'summary': summary, 'attributes': attributes or {}}
    if embedding is not None:
        data['name_embedding'] = embedding
    return data


def edge_data(uuid: str, source: str, target: str, embedding=None) -> dict:
    data = {'uuid': uuid, 'group_id': 'g', 'source_node_uuid': source, 'target_node_uuid': target, 'name': 'REL',
            'fact': 'fact ' + uuid, 'created_at': DT}
    if embedding is not None:
        data['fact_embedding'] = embedding
    return data


def node_view(node) -> dict:
    return {'uuid': node.uuid, 'name': node.name, 'group_id': node.group_id, 'labels': list(node.labels),
            'created_at': node.created_at.isoformat(), 'name_embedding': node.name_embedding, 'summary': node.summary,
            'attributes': node.attributes}


def digest_view(digest) -> dict:
    indexes = digest.node_indexes
    return {
        'seq': digest.seq, 'status': digest.status, 'nodes': [node_view(n) for n in digest.nodes],
        'normalized_existing': {k: [n.uuid for n in v] for k, v in indexes.normalized_existing.items()},
        'nodes_by_uuid': list(indexes.nodes_by_uuid),
        'shingles_by_candidate': {k: sorted(v) for k, v in indexes.shingles_by_candidate.items()},
        'lsh_bucket_count': len(indexes.lsh_buckets),
        'node_vector_uuids': digest.node_vector_uuids, 'node_vectors': matrix_view(digest.node_vectors),
        'edges': [list(e) for e in digest.edges], 'edge_vector_uuids': digest.edge_vector_uuids,
        'edge_vectors': matrix_view(digest.edge_vectors),
        'patches': [p.model_dump(mode='json') for p in digest.patches],
    }


def delta_view(delta) -> dict:
    return {'unit_count': delta.unit_count, 'digests': [digest_view(d) for d in delta.digests],
            'patches': {k: [p.model_dump(mode='json') for p in v] for k, v in delta.patches.items()},
            'created_node_uuids': sorted(delta.created_node_uuids)}


records: list[dict] = []


def call(name: str, args, fn, view=lambda r: r) -> None:
    try:
        result = {'ok': view(fn())}
    except Exception as error:  # noqa: BLE001 - the recording keeps each exception
        result = {'err': error_view(error)}
    records.append({'f': name, 'a': args, 'r': result})


def unit_matrix_cases(d) -> None:
    for dim in [0, 1, 2, 3, 7, 8, 9, 15, 16, 17, 64, 127, 128, 129, 130, 255, 256, 300]:
        rows = [vector(dim, rng.choice([1.0, 1e-3, 40.0])) for _ in range(rng.randint(1, 3))]
        if rng.random() < 0.3 and dim:
            rows.append([0.0] * dim)
        call('_unit_matrix', [rows], lambda rows=rows: d._unit_matrix(rows), matrix_view)
    for rows in ([], [[]], [[], []], [[3.0, 4.0], [0.0, 0.0], [1.0, 1.0]], [[1.0, 2.0], [3.0]], [[], [1.0]],
                 [[1e39, 1.0]], [[1e-46, 0.0]], [[-0.0, 0.0]], [[1e20, 1e20, 1e20]], [[3e-23, 4e-23]]):
        call('_unit_matrix', [rows], lambda rows=rows: d._unit_matrix(rows), matrix_view)
    # A row of width 255 whose float32 norm differs if the pairwise split does not round half down to a multiple of 8
    # (review B, seed D03): the 27th draw of random.Random(7).
    rv = random.Random(7)
    for _ in range(27):
        row = [rv.uniform(-1, 1) for _ in range(rv.choice([255, 300, 1536]))]
    call('_unit_matrix', [[row]], lambda: d._unit_matrix([row]), matrix_view)


def fuzzy_cases(d, dh) -> None:
    for name in NAMES + ['Ünïcödé Nämé', 'ab', 'abc def', '12345 67890', '  spaced   out  name  ']:
        call('_fuzzy_query', [name], lambda name=name: d._fuzzy_query(name),
             lambda r: None if r is None else [sorted(r[0]), [[str(v) for v in band] for band in r[1]]])
    from graphiti_core.nodes import EntityNode
    for _ in range(40):
        nodes = [node_data(f'c{j}') for j in range(rng.randint(0, 6))]
        name = rng.choice(NAMES)
        call('_fuzzy_match', [name, nodes],
             lambda name=name, nodes=nodes: d._fuzzy_match(
                 d._fuzzy_query(name), dh._build_candidate_indexes([EntityNode(**n) for n in nodes])))
    # A Jaccard score of exactly 0.9 (review B, seed D23), and two candidates with one best score (seed D24).
    for name, nodes in (('Quantum Biola', [node_data('c0', name='Quantum Biol')]),
                        ('Acme Corporation', [node_data('c0', name='Acme Corporation'), node_data('c1', name='Acme Corporation')]),
                        ('Quantum Biola', [node_data('c0', name='Bob'), node_data('c1', name='Quantum Biol'),
                                           node_data('c2', name='Quantum Biol')])):
        call('_fuzzy_match', [name, nodes],
             lambda name=name, nodes=nodes: d._fuzzy_match(
                 d._fuzzy_query(name), dh._build_candidate_indexes([EntityNode(**n) for n in nodes])))


def stack_cases(d) -> None:
    for _ in range(30):
        mats = []
        for _ in range(rng.randint(0, 4)):
            width = rng.choice([0, 2, 3, 3])
            rows = [] if rng.random() < 0.2 else [vector(width) for _ in range(rng.randint(1, 3))]
            mats.append(rows)
        uuids = [[f'u{i}-{j}' for j in range(len(rows))] for i, rows in enumerate(mats)]
        if rng.random() < 0.1:
            uuids = uuids[:-1]
        call('_stack', [mats], lambda mats=mats: d._stack([d._unit_matrix(m) for m in mats]), matrix_view)
        call('_stack_uuids', [uuids, mats],
             lambda mats=mats, uuids=uuids: d._stack_uuids(uuids, [d._unit_matrix(m) for m in mats]))


def top_matches_cases(d) -> None:
    import numpy as np

    def top(vectors, uuids, rows, min_score, limit, raw=True):
        def run():
            matrix = np.asarray(rows, dtype=np.float32) if raw else d._unit_matrix(rows)
            if matrix.ndim != 2:
                matrix = np.zeros((0, 0), dtype=np.float32)
            return d._top_matches(vectors, uuids, matrix, min_score, limit), top_loop(vectors, uuids, matrix, min_score, limit)
        try:
            result, (loop, check) = run()
            record = {'f': '_top_matches', 'a': [vectors, uuids, rows, min_score, limit, raw], 'r': {'ok': result}}
            if loop != result:
                # GAPS G17: BLAS sums the scores in another order than the plain loop of the port. The ranks may differ
                # only where the loop scores are equal.
                check()
                record['r_loop'] = {'ok': loop}
            records.append(record)
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            records.append({'f': '_top_matches', 'a': [vectors, uuids, rows, min_score, limit, raw], 'r': {'err': error_view(error)}})

    def top_loop(vectors, uuids, matrix, min_score, limit):
        # _top_matches with the scores of a plain loop in index order with a float32 sum (RULES.md R18). It checks that
        # each score differs from the BLAS score by less than 1e-6, and gives the ranks of the port. The check that it
        # returns stops the tool if the BLAS ranks and the loop ranks differ in more than the order of equal loop scores,
        # or if a score of the first `limit` ranks is on the other side of `min_score`.
        results = [[] for _ in vectors]
        rows = [index for index, vector in enumerate(vectors) if vector]
        if not rows or not uuids:
            return results, lambda: None
        queries = d._unit_matrix([vectors[index] or [] for index in rows])
        if queries.shape[1] != matrix.shape[1]:
            return results, lambda: None
        blas = queries @ matrix.T
        scores = np.zeros(blas.shape, dtype=np.float32)
        for r in range(queries.shape[0]):
            for c in range(matrix.shape[0]):
                total = np.float32(0)
                for k in range(queries.shape[1]):
                    total = np.float32(total + np.float32(queries[r, k] * matrix[c, k]))
                scores[r, c] = total
                if np.isfinite(blas[r, c]) and abs(float(total) - float(blas[r, c])) >= 1e-6:
                    raise SystemExit(f'A BLAS score differs from the loop by 1e-6 or more: {total} {blas[r, c]}')
        for row, index in enumerate(rows):
            order = np.argsort(-scores[row])
            results[index] = [uuids[column] for column in order[:limit] if scores[row, column] >= min_score]

        def check():
            for row in range(len(rows)):
                blas_order = np.argsort(-blas[row])
                loop_order = np.argsort(-scores[row])
                for a, b in zip(blas_order, loop_order, strict=True):
                    x, y = scores[row, a], scores[row, b]
                    if not (x == y or (np.isnan(x) and np.isnan(y))):
                        raise SystemExit(f'BLAS and the loop rank other scores at one place: {x} {y} (GAPS G17)')
                for column in list(blas_order[:limit]) + list(loop_order[:limit]):
                    if (blas[row, column] >= min_score) != (scores[row, column] >= min_score):
                        raise SystemExit(f'BLAS and the loop put a score on two sides of {min_score} (GAPS G17)')
        return results, check

    # A score exactly on the limit (0.6 in float32), a limit above the score that float32 rounds to the score (NumPy 2
    # compares in float32, NEP 50), equal scores, and 0, 1, and more matches than the limit.
    on_limit = [[1.0, 0.0], [0.6, 0.8], [0.0, 1.0], [-1.0, 0.0], [0.6, 0.8]]
    for min_score in (0.6, 0.6000000238418579, 0.60000003, 0.5999999, 0.0, -1.0, 1.0, 1.0000001):
        for limit in (0, 1, 2, 3, 10, -1, -3):
            top([[1.0, 0.0]], ['c1', 'c2', 'c3', 'c4', 'c5'], on_limit, min_score, limit)
    top([[1.0, 0.0], None, [], [0.0, 2.0]], ['c1', 'c2', 'c3', 'c4'], [[1.0, 0.0], [0.6, 0.8], [0.0, 1.0], [-1.0, 0.0]], 0.5, 2)
    top([[1.0, 0.0]], ['c1', 'c2'], [[1.0, 0.0], [1.0, 0.0]], 0.0, 5)
    top([[1.0, 0.0]], [f'c{i}' for i in range(16)], [[1.0, 0.0]] * 16, 0.6, 15)
    top([[1.0, 0.0]], [], [[1.0, 0.0]], 0.5, 5)
    top([[1.0, 0.0]], ['c1'], [], 0.5, 5)
    top([[1.0, 0.0, 0.0]], ['c1'], [[1.0, 0.0]], 0.5, 5)
    top([[1.0, 0.0], [1.0]], ['c1'], [[1.0, 0.0]], 0.5, 5)
    top([None, []], ['c1'], [[1.0, 0.0]], 0.5, 5)
    top([[0.0, 0.0]], ['c1', 'c2'], [[1.0, 0.0], [0.0, 0.0]], 0.0, 5)
    top([[1e39, 1.0]], ['c1', 'c2'], [[1.0, 0.0], [0.0, 1.0]], -1.0, 5)
    top([[3.0, 4.0]], ['c1', 'c2', 'c3'], [[3.0, 4.0], [0.6, 0.8], [4.0, 3.0]], 0.96, 5, raw=False)
    for _ in range(60):
        dim = rng.choice([2, 3, 5, 8, 9])
        count = rng.randint(0, 16)
        rows = [vector(dim) for _ in range(count)]
        for i in range(count):
            if rng.random() < 0.2 and i:
                rows[i] = list(rows[rng.randrange(i)])
        vectors = [rng.choice([None, vector(dim), vector(dim), list(rows[0]) if rows else vector(dim)]) for _ in range(rng.randint(1, 4))]
        top(vectors, [f'c{i}' for i in range(count)], rows, rng.choice([0.6, 0.0, 0.3, 0.9, -0.5]),
            rng.choice([1, 3, 10, 15, 0]), raw=rng.random() < 0.5)

    # The reviews of packet P8b. numpy's quicksort orders equal scores of 17 or more candidates in another order than
    # their columns (review A, finding 1, and review B, finding 1).
    rv = random.Random(SEED + 1)
    top([[1.0, 0.0]], [f'c{i}' for i in range(20)], [[1.0, 0.0]] * 20, 0.6, 10)
    top([[1.0, 0.0]], [f'c{i}' for i in range(17)], [[1.0, 0.0]] * 17, 0.6, 10)
    top([[1.0, 0.0]], [f'c{i}' for i in range(20)], [[1.0, 0.0]] * 20, 0.6, 30)
    v = [round(rv.gauss(0, 0.03), 5) for _ in range(1536)]
    q = [round(x + rv.gauss(0, 0.01), 5) for x in v]
    others = [[round(x + rv.gauss(0, 0.02), 5) for x in v] for _ in range(4)]
    top([q], [f'c{i}' for i in range(20)], [v] * 20, 0.6, 10, raw=False)
    top([q], [f'c{i}' for i in range(22)], [v] * 9 + others + [v] * 9, 0.6, 10, raw=False)
    # Two or more equal rows among 17 to 40 rows.
    for _ in range(40):
        dim = rv.choice([2, 3, 9, 64])
        count = rv.randint(17, 40)
        rows = [[round(rv.uniform(-1, 1), 6) for _ in range(dim)] for _ in range(count)]
        for i in range(count):
            if rv.random() < 0.3 and i:
                rows[i] = list(rows[rv.randrange(i)])
        query = [round(rv.uniform(-1, 1), 6) for _ in range(dim)]
        top([rv.choice([query, list(rows[0])]), rv.choice([None, list(reversed(query))])], [f'c{i}' for i in range(count)], rows,
            rv.choice([0.6, 0.0, -1.0]), rv.choice([1, 10, 15, 40]), raw=rv.random() < 0.5)
    # A candidate row with nan and a limit below the count: the nan column sorts last (review A, finding 3, and review B,
    # seed D16).
    # float32 makes 1e39 inf, and the unit row of [inf, 0] is [nan, 0].
    top([[1.0, 1.0]], ['c1', 'c2', 'c3'], [[1e39, 0.0], [0.0, 1.0], [1.0, 0.0]], -1.0, 2, raw=False)
    top([[1.0, 0.0]], ['c1', 'c2'], [[1e39, 1.0], [1.0, 0.0]], 0.6, 1, raw=False)


def patch_cases(d) -> None:
    from graphiti_core.ingestion.change_set import PatchNode
    from graphiti_core.nodes import EntityNode
    for _ in range(10):
        n = node_data('n1', attributes={'a': 1})
        p = {'node_uuid': 'n1', 'labels': rng.sample(['Entity', 'Person', 'Company'], rng.randint(0, 3)),
             'attributes': rng.choice([{}, {'email': 'a@x.io'}, {'n': [1, 2], 'b': None}])}

        def view(node):
            v = node_view(node)
            del v['created_at']
            return v

        call('patch_as_node', [n, p], lambda n=n, p=p: d.patch_as_node(EntityNode(**n), PatchNode(**p)), view)


def change_set(group_id: str, seq: int, nodes, edges, patches) -> dict:
    return {'group_id': group_id, 'unit_id': f'u{seq}', 'seq': seq, 'snapshot_seq': 0, 'analysis_started_at': 1.5,
            'create_nodes': nodes, 'create_edges': edges,
            'operations': [dict(op='patch_node', **p) for p in patches]}


def delta_cases(d) -> None:
    from graphiti_core.edges import EntityEdge
    from graphiti_core.ingestion.change_set import ChangeSet
    from graphiti_core.ingestion.state_store import DeltaUnit
    from graphiti_core.nodes import EntityNode

    def run(group_id, units, queries, edge_queries, identity):
        def body():
            delta = d.load_delta_digests(group_id, [DeltaUnit(u['seq'], u['status'], bytes.fromhex(u['payload'])) for u in units])
            nodes = [EntityNode(**q) for q in queries]
            edges = [EntityEdge(**e) for e in edge_queries]
            out = {'delta': delta_view(delta), 'match_nodes': delta.match_nodes(nodes)}
            for key, value in (('match_nodes_identity', lambda: delta.match_nodes(nodes, identity)),
                               ('patched_identity_matches', lambda: delta.patched_identity_matches(nodes, identity)),
                               ('patched_identity_matches_none', lambda: delta.patched_identity_matches(nodes, None)),
                               ('edges_between', lambda: delta.edges_between(edges)),
                               ('similar_edges', lambda: delta.similar_edges(edges))):
                try:
                    out[key] = {'ok': value()}
                except Exception as error:  # noqa: BLE001 - the recording keeps each exception
                    out[key] = {'err': error_view(error)}
            return out
        call('delta', [group_id, units, queries, edge_queries, identity], body)

    def unit(seq, status, cs):
        return {'seq': seq, 'status': status, 'payload': ChangeSet(**cs).to_bytes().hex()}

    identity = {'Person': ['email'], 'Company': ['domain']}
    for i in range(25):
        group_id = f'g{i}'
        dim = rng.choice([2, 3, 3, 4])
        units = []
        uuids = []
        for seq in rng.sample(range(1, 9), rng.randint(0, 4)):
            nodes = []
            for j in range(rng.randint(0, 4)):
                uuid = f'd{seq}-{j}'
                uuids.append(uuid)
                labels = rng.sample(LABELS, rng.randint(0, 1))
                attrs = rng.choice([{}, {'email': rng.choice(['a@x.io', ' A@X.io ', 'b@x.io'])}, {'domain': 'acme.io'}])
                emb = rng.choice([None, vector(dim), vector(dim), vector(rng.choice([dim, 5])), []])
                nodes.append(node_data(uuid, labels=labels, attributes=attrs, embedding=emb,
                                       summary=rng.choice(['', 'Summary.'])))
            edges = []
            for j in range(rng.randint(0, 3)):
                ends = uuids or ['s1', 's2']
                edges.append(edge_data(f'e{seq}-{j}', rng.choice(ends + ['s1']), rng.choice(ends + ['s2']),
                                       rng.choice([None, vector(dim), vector(dim), []])))
            patches = []
            for _ in range(rng.randint(0, 3)):
                patches.append({'node_uuid': rng.choice(uuids + ['s1', 's2', 's3'] if uuids else ['s1', 's2']),
                                'labels': ['Entity'] + rng.sample(LABELS, rng.randint(0, 1)),
                                'attributes': rng.choice([{}, {'email': 'a@x.io'}, {'domain': 'acme.io'}, {'email': 'c@x.io'}])})
            units.append(unit(seq, rng.choice(['committed', 'applying', 'committed']), change_set(group_id, seq, nodes, edges, patches)))
        queries = []
        for j in range(rng.randint(0, 4)):
            queries.append(node_data(rng.choice([f'q{j}', f'q{j}'] + uuids[:1]), labels=rng.sample(LABELS, rng.randint(0, 1)),
                                     attributes=rng.choice([{}, {'email': 'a@x.io'}, {'domain': 'acme.io'}, {'email': 'c@x.io'}]),
                                     embedding=rng.choice([None, vector(dim), []])))
        edge_queries = [edge_data(f'x{j}', rng.choice(uuids + ['s1', 's2']), rng.choice(uuids + ['s1', 's2']),
                                  rng.choice([None, vector(dim), vector(dim + 1)])) for j in range(rng.randint(0, 3))]
        run(group_id, units, queries, edge_queries, rng.choice([identity, {}, {'Person': ['email']}]))

    # A query node that matches each rule: the exact name, the identity key of a patched delta node, a fuzzy name,
    # and the cosine of the name embedding, with more matches than NODE_DEDUP_CANDIDATE_LIMIT.
    many = [node_data(f'm{j}', name='Alice Johnson', labels=['Person'], embedding=[1.0, 0.0]) for j in range(20)]
    rules = [node_data('r-exact', name='Acme Corporation'), node_data('r-fuzzy', name='The Massachusetts Institute of Technology'),
             node_data('r-id', name='Zed', labels=['Person']), node_data('r-cos', name='Q', embedding=[0.6, 0.8])]
    units = [unit(2, 'committed', change_set('rules', 2, many[:10], [], [{'node_uuid': 'r-id', 'labels': ['Entity', 'Person'],
                                                                     'attributes': {'email': 'z@x.io'}},
                                                                    {'node_uuid': 's9', 'labels': ['Entity', 'Person'],
                                                                     'attributes': {'email': 'z@x.io'}}])),
             unit(1, 'committed', change_set('rules', 1, many[10:] + rules, [edge_data('re1', 'm1', 'm2', [1.0, 0.0]),
                                                                           edge_data('re2', 'm2', 'm1', [0.6, 0.8])], []))]
    queries = [node_data('q1', name='alice  johnson', labels=['Person'], embedding=[1.0, 0.0]),
               node_data('q2', name='ACME CORPORATION', embedding=[0.0, 1.0]),
               node_data('q3', name='Other', labels=['Person'], attributes={'email': 'z@x.io'}),
               node_data('q4', name='Massachusetts Institute of Technology'),
               node_data('m1', name='Alice Johnson', embedding=[1.0, 0.0])]
    run('rules', units, queries, [edge_data('xq', 'm2', 'm1', [1.0, 0.0]), edge_data('xr', 'm1', 'm1', [0.6, 0.8])],
        {'Person': ['email']})
    run('empty', [], queries[:1], [edge_data('xq', 'm2', 'm1', [1.0, 0.0])], {'Person': ['email']})
    # A payload that is not a change set.
    run('bad', [{'seq': 1, 'status': 'committed', 'payload': ChangeSet(group_id='bad', unit_id='u', seq=1, snapshot_seq=0,
                                                                         analysis_started_at=0.0).to_bytes().hex()[:-4]}],
        [], [], {})
    # Two patches of one node with other identity values, applied in order (review B, seed D31).
    patches = [{'node_uuid': 's1', 'labels': ['Entity', 'Person'], 'attributes': {'email': 'a@x.io'}},
               {'node_uuid': 's1', 'labels': ['Entity', 'Person'], 'attributes': {'email': 'c@x.io'}}]
    run('patches', [unit(1, 'committed', change_set('patches', 1, [], [], patches))],
        [node_data('qa', name='Qa', labels=['Person'], attributes={'email': 'a@x.io'}),
         node_data('qc', name='Qc', labels=['Person'], attributes={'email': 'c@x.io'})], [], {'Person': ['email']})
    # More delta edges with the same fact embedding than DEFAULT_SEARCH_LIMIT (seed D39), and more delta nodes with
    # the same name embedding and no rule match than NODE_DEDUP_CANDIDATE_LIMIT (seed D40). The scores are equal.
    words = ['Harbor', 'Kestrel', 'Juniper', 'Zephyr', 'Northwind', 'Silverline', 'Blue', 'Quantum', 'Fieldwork', 'Delta']
    cosine = [node_data(f'k{j}', name=f'{words[j % 10]} {words[(j * 3 + 1) % 10]} {j}', labels=[], embedding=[1.0, 0.0])
              for j in range(20)]
    similar = [edge_data(f'se{j}', 'k1', 'k2', [1.0, 0.0]) for j in range(12)]
    run('limits', [unit(1, 'committed', change_set('limits', 1, cosine, similar, []))],
        [node_data('q1', name='Q', labels=[], embedding=[1.0, 0.0])],
        [edge_data('xs', 'k3', 'k4', [1.0, 0.0])], {})


def cache_case(d) -> dict:
    """Loads units, loads them again, then loads DIGEST_CACHE_SIZE + 1 other units, then the first units again."""
    from graphiti_core.ingestion.change_set import ChangeSet
    from graphiti_core.ingestion.state_store import DeltaUnit

    def payload(seq, name):
        return ChangeSet(**change_set('cache', seq, [node_data(f'n{seq}', name=name)], [], [])).to_bytes()

    first = [DeltaUnit(1, 'committed', payload(1, 'Alice')), DeltaUnit(2, 'committed', payload(2, 'Bob'))]
    one = d.load_delta_digests('cache', first).digests
    two = d.load_delta_digests('cache', list(reversed(first))).digests
    other_status = d.load_delta_digests('cache', [DeltaUnit(1, 'applying', first[0].payload)]).digests
    other_group = d.load_delta_digests('cache2', first[:1]).digests
    filler = [DeltaUnit(1000 + i, 'committed', payload(1000 + i, 'Filler')) for i in range(d.DIGEST_CACHE_SIZE + 1)]
    d.load_delta_digests('cache', filler)
    three = d.load_delta_digests('cache', first).digests
    # Review B of packet P8b. Each sequence uses its own group, so the result does not depend on the entries of the
    # cache before it. Seed D32: A, then DIGEST_CACHE_SIZE - 1 other units, then A.
    others = [DeltaUnit(5000 + i, 'committed', payload(5000 + i, 'Other')) for i in range(d.DIGEST_CACHE_SIZE - 1)]
    a = d.load_delta_digests('limit', first[:1]).digests[0]
    d.load_delta_digests('limit', others)
    limit_kept = d.load_delta_digests('limit', first[:1]).digests[0] is a
    # Seed D33: A, B, A again (a hit moves A to the end), DIGEST_CACHE_SIZE - 1 other units, then A and B.
    a = d.load_delta_digests('move', first[:1]).digests[0]
    b = d.load_delta_digests('move', first[1:]).digests[0]
    d.load_delta_digests('move', first[:1])
    d.load_delta_digests('move', others)
    move = [d.load_delta_digests('move', first[:1]).digests[0] is a, d.load_delta_digests('move', first[1:]).digests[0] is b]
    # Seed D34: two payloads with the same group, seq, status, and length.
    # The payload is compressed, so the tool takes the first name of a fixed list that gives the length of 'Alice'.
    twins = [payload(7, 'Alice')]
    twins.append(next(p for p in (payload(7, name) for name in ('Alicg', 'Alicf', 'Alicd', 'Alica', 'Aline', 'Alize', 'Elice'))
                      if len(p) == len(twins[0])))
    x = d.load_delta_digests('twins', [DeltaUnit(7, 'committed', twins[0])]).digests[0]
    y = d.load_delta_digests('twins', [DeltaUnit(7, 'committed', twins[1])]).digests[0]
    return {'again': [a is b for a, b in zip(one, two, strict=True)], 'other_status': other_status[0] is one[0],
            'other_group': other_group[0] is one[0], 'after_eviction': [a is b for a, b in zip(one, three, strict=True)],
            'payloads': [u.payload.hex() for u in first], 'filler_count': len(filler),
            'other_count': len(others), 'limit_kept': limit_kept, 'move': move,
            'twins': [t.hex() for t in twins], 'twins_same': x is y, 'twins_names': [x.nodes[0].name, y.nodes[0].name]}


class Names:
    pass


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core.ingestion import delta
    from graphiti_core.search.search_config import DEFAULT_SEARCH_LIMIT
    from graphiti_core.search.search_utils import DEFAULT_MIN_SCORE
    from graphiti_core.utils.maintenance import dedup_helpers
    from graphiti_core.utils.maintenance.node_operations import NODE_DEDUP_CANDIDATE_LIMIT, NODE_DEDUP_COSINE_MIN_SCORE

    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from record_maintenance_helpers import InsertionOrderSet

    # The set that _fuzzy_match iterates, in insertion order (RULES.md R14).
    delta.set = InsertionOrderSet
    dedup_helpers.set = InsertionOrderSet

    constants = {'DIGEST_CACHE_SIZE': delta.DIGEST_CACHE_SIZE, 'DEFAULT_MIN_SCORE': DEFAULT_MIN_SCORE,
                 'DEFAULT_SEARCH_LIMIT': DEFAULT_SEARCH_LIMIT, 'NODE_DEDUP_CANDIDATE_LIMIT': NODE_DEDUP_CANDIDATE_LIMIT,
                 'NODE_DEDUP_COSINE_MIN_SCORE': NODE_DEDUP_COSINE_MIN_SCORE}
    unit_matrix_cases(delta)
    fuzzy_cases(delta, dedup_helpers)
    stack_cases(delta)
    top_matches_cases(delta)
    patch_cases(delta)
    delta_cases(delta)
    cache = cache_case(delta)

    def dumps(value):
        return json.dumps(value, ensure_ascii=True, allow_nan=False, separators=(',', ':'))

    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","constants":' + dumps(constants)
        + ',"cache":' + dumps(cache) + ',"cases":[\n' + ',\n'.join(dumps(r) for r in records) + '\n]}\n'
    )
    size = len(text.encode('utf-8'))
    if size > 1_572_864:
        raise SystemExit(f'The values file would have {size} bytes, above the limit of 1,572,864 (R25).')
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(records)} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(records)} cases, {size} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
