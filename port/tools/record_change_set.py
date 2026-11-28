"""Records results of graphiti_core/ingestion/change_set.py and config.py from real Python, for the evaluation tests.

The judge has 67 cases for these two modules. This file gives the evaluation tests calls with generated
inputs from a fixed seed (port/RULES.md, R23 and R25):

- merge_edge_attributes, apply_edge_operation (two times on one edge), and _diff_edge_state,
- ResolutionTrace: a list of steps on a pool of stored edges and nodes, with the state of the trace after each
  step. No step calls a function of utils/maintenance (layer 7),
- ChangeSet: the text of model_dump_json(), the bytes of to_bytes() as hex, patches_by_node(), and
  edge_operations(). The stored form of a change set is a stored data shape. The compressed bytes of .NET differ
  from the bytes of CPython (GAPS F39), so the test compares the text inside the bytes, and it reads the bytes of Python,
- ChangeSet.from_bytes on bytes that are cut or changed: the class of the error and, for three kinds of zlib.error, its
  text,
- model_copy(deep=True) of a change set: which objects the copy shares with the original,
- ConcurrentIngestionConfig(**kwargs): the fields, or the class and the text of the error.

    .upstream/graphiti/.venv/bin/python -B port/tools/record_change_set.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_change_set.py --check

It writes tests/Port.Evaluation/values/change_set.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The environment variable GRAPHITI_UPSTREAM names the upstream
folder, for a worktree that has no .upstream. Nothing here uses the network.

A pydantic message is recorded without ', input_value=..., input_type=...]' and without the link line
(RULES.md R25). Each config case has one bad field at most, because the port reports a constraint error only after
the type errors (PydanticFields.ThrowIfAny), where pydantic reports all of them together (GAPS F40).
"""
from __future__ import annotations

import copy
import json
import logging
import os
import random
import re
import sys
import zlib
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM', str(ROOT / '.upstream' / 'graphiti')))
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'change_set.json'

rng = random.Random(20261127)
records: list[dict] = []

UTC = timezone.utc
PLUS2 = timezone(timedelta(hours=2))
DATES = [
    datetime(2026, 1, 1, tzinfo=UTC),
    datetime(2026, 1, 2, 3, 4, 5, 123456, tzinfo=UTC),
    datetime(2026, 3, 1, tzinfo=UTC),
    datetime(2026, 1, 1, 1, 0, tzinfo=PLUS2),
    datetime(2025, 12, 31, 23, 0),
    datetime(2026, 2, 1),
]
VALUES = [1, 1.0, True, False, 0, 2, 'x', 'new', '', None, [1, 2], [1.0, 2.0], {'x': 1}, {'x': 1.0}, 0.1, -3, 'é']
KEYS = ['a', 'b', 'c', 'role', 'k', 'é']
EPISODES = ['ep0', 'ep1', 'ep2', 'ep3', 'x']
LABELS = ['Entity', 'Person', 'Company', 'X']


def dt_json(value):
    return None if value is None else value.isoformat()


def rand_attrs(max_keys: int = 4) -> dict:
    return {k: rng.choice(VALUES) for k in rng.sample(KEYS, rng.randint(0, max_keys))}


def maybe_dt():
    return rng.choice(DATES) if rng.random() < 0.5 else None


def rand_float():
    return rng.choice([0.0, 1.0, -0.5, 0.1, 1e-7, 1e16, 123456789.123, rng.random(), rng.uniform(-1, 1)])


def make_edge(EntityEdge, uuid: str):
    return EntityEdge(
        uuid=uuid,
        group_id='g',
        source_node_uuid='a',
        target_node_uuid='b',
        name='WORKS_AT',
        fact='Ana works at Acme',
        created_at=DATES[0],
        episodes=rng.sample(EPISODES, rng.randint(0, 3)),
        attributes=rand_attrs(),
        invalid_at=maybe_dt(),
        expired_at=maybe_dt(),
        valid_at=maybe_dt(),
        fact_embedding=[rand_float() for _ in range(rng.randint(0, 3))] if rng.random() < 0.5 else None,
    )


def rand_op(m):
    kind = rng.randrange(3)
    if kind == 0:
        return m.AddEpisodesToEdge(edge_uuid='e1', episode_uuids=[rng.choice(EPISODES) for _ in range(rng.randint(0, 4))])
    if kind == 1:
        return m.EndEdge(
            edge_uuid='e1', invalid_at=maybe_dt(), expired_at=rng.choice(DATES), only_if_open=rng.random() < 0.5
        )
    return m.SetEdgeAttributes(edge_uuid='e1', base_attributes=rand_attrs(), attributes=rand_attrs())


def strip_pydantic(text: str) -> str:
    text = re.sub(r', input_value=.*?, input_type=[^\]]*\]', ']', text)
    return '\n'.join(line for line in text.split('\n') if not line.strip().startswith('For further information'))


def record_functions(m, EntityEdge) -> None:
    for _ in range(400):
        base, new, current = rand_attrs(), rand_attrs(), rand_attrs()
        if rng.random() < 0.4:
            current = dict(base)
        records.append({'f': 'merge_edge_attributes', 'a': [base, new, current], 'r': m.merge_edge_attributes(base, new, current)})

    for _ in range(250):
        edge = make_edge(EntityEdge, 'e1')
        op = rand_op(m)
        before = edge.model_dump(mode='json')
        first = m.apply_edge_operation(edge, op)
        middle = edge.model_dump(mode='json')
        second = m.apply_edge_operation(edge, op)
        records.append({
            'f': 'apply_edge_operation',
            'a': [before, op.model_dump(mode='json')],
            'r': [first, middle, second, edge.model_dump(mode='json') == middle],
        })

    for _ in range(300):
        edge = make_edge(EntityEdge, 'e1')
        state = m._EdgeState(
            edge=edge,
            episodes=rng.sample(EPISODES, rng.randint(0, 3)),
            attributes=rand_attrs() if rng.random() < 0.5 else dict(edge.attributes),
            invalid_at=maybe_dt() if rng.random() < 0.5 else edge.invalid_at,
            expired_at=maybe_dt() if rng.random() < 0.5 else edge.expired_at,
            ended_as_candidate=rng.random() < 0.5,
        )
        records.append({
            'f': '_diff_edge_state',
            'a': [
                edge.model_dump(mode='json'),
                state.episodes,
                state.attributes,
                dt_json(state.invalid_at),
                dt_json(state.expired_at),
                state.ended_as_candidate,
            ],
            'r': [op.model_dump(mode='json') for op in m._diff_edge_state(state)],
        })


def trace_state(trace) -> dict:
    # A copy, because the trace changes its dicts in the later steps.
    return copy.deepcopy({
        'seen_node_candidates': trace.seen_node_candidates,
        'seen_node_labels': trace.seen_node_labels,
        'seen_duplicate_candidates': trace.seen_duplicate_candidates,
        'seen_invalidation_candidates': trace.seen_invalidation_candidates,
        'operations': [op.model_dump(mode='json') for op in trace.operations],
        'stored_node_uuids': sorted(trace.stored_node_uuids),
        'ended_by_candidate': sorted(trace.ended_by_candidate),
        'node_llm_calls': trace.node_llm_calls,
    })


def apply_step(trace, edges, nodes, step):
    """Runs one step on the trace. It returns the result of the step, or None."""
    kind = step['m']
    pick = step.get('nodes', step.get('edges'))
    if kind == 'candidates':
        return trace.record_node_candidates(step['uuid'], [nodes[i] for i in pick])
    if kind == 'resolution':
        return trace.record_node_resolution(nodes[step['node']], step['resolved'])
    if kind == 'edge_candidates':
        return trace.record_edge_candidates(
            step['uuid'], [edges[i] for i in step['duplicates']], [edges[i] for i in step['invalidations']]
        )
    if kind == 'watch':
        return trace.watch_stored_edges([edges[i] for i in pick])
    if kind == 'mutate':
        edge = edges[step['edge']]
        if 'append' in step:
            edge.episodes.append(step['append'])
        if 'attributes' in step:
            edge.attributes = dict(step['attributes'])
        if 'set_key' in step:
            edge.attributes[step['set_key'][0]] = step['set_key'][1]
        if 'invalid_at' in step:
            edge.invalid_at = None if step['invalid_at'] is None else datetime.fromisoformat(step['invalid_at'])
        if 'expired_at' in step:
            edge.expired_at = None if step['expired_at'] is None else datetime.fromisoformat(step['expired_at'])
        return None
    if kind == 'labels':
        # A promotion of the labels of a candidate in memory, after its first read.
        node = nodes[step['node']]
        node.labels = [*node.labels, step['label']]
        return None
    if kind == 'mark':
        return trace.mark_ended_as_candidate([edges[i] for i in pick])
    if kind == 'collect':
        return [op.model_dump(mode='json') for op in trace.collect_edge_operations([edges[i] for i in pick])]
    return [op.model_dump(mode='json') for op in trace.collect_edge_operations()]


def random_step():
    kind = rng.choice(['candidates', 'resolution', 'edge_candidates', 'watch', 'mutate', 'mark', 'collect', 'collect_all', 'labels'])
    pick = sorted(rng.sample(range(4), rng.randint(0, 4)))
    if kind == 'candidates':
        return {'m': kind, 'uuid': rng.choice(['x1', 'x2', 'n0']), 'nodes': pick}
    if kind == 'resolution':
        i = rng.randrange(4)
        return {'m': kind, 'node': i, 'resolved': rng.choice([f'n{i}', 'stored-1', 'stored-2'])}
    if kind == 'edge_candidates':
        other = sorted(rng.sample(range(4), rng.randint(0, 4)))
        return {'m': kind, 'uuid': rng.choice(['x1', 'x2']), 'duplicates': pick, 'invalidations': other}
    if kind == 'watch':
        return {'m': kind, 'edges': pick}
    if kind == 'mutate':
        step = {'m': kind, 'edge': rng.randrange(4)}
        if rng.random() < 0.5:
            step['append'] = rng.choice(EPISODES)
        if rng.random() < 0.4:
            step['attributes'] = rand_attrs()
        if rng.random() < 0.3:
            step['set_key'] = [rng.choice(KEYS), rng.choice(VALUES)]
        if rng.random() < 0.4:
            step['invalid_at'] = dt_json(maybe_dt())
        if rng.random() < 0.4:
            step['expired_at'] = dt_json(maybe_dt())
        return step
    if kind == 'labels':
        return {'m': kind, 'node': rng.randrange(4), 'label': rng.choice(LABELS)}
    if kind == 'mark':
        return {'m': kind, 'edges': pick}
    if kind == 'collect':
        return {'m': kind, 'edges': pick}
    return {'m': kind}


def run_trace(m, edges, nodes, steps):
    start = {
        'edges': [e.model_dump(mode='json') for e in edges],
        'nodes': [n.model_dump(mode='json') for n in nodes],
    }
    trace = m.ResolutionTrace()
    recorded = []
    for step in steps():
        result = apply_step(trace, edges, nodes, step)
        recorded.append({'step': step, 'result': result, 'state': trace_state(trace)})
    records.append({'f': 'ResolutionTrace', 'a': [start], 'r': recorded})


def record_traces(m, EntityEdge, EntityNode) -> None:
    def pool():
        edges = [make_edge(EntityEdge, f'e{i}') for i in range(4)]
        nodes = [
            EntityNode(
                uuid=f'n{i}', name=f'N{i}', group_id='g', created_at=DATES[0],
                labels=rng.sample(LABELS, rng.randint(0, 3)), attributes=rand_attrs(),
            )
            for i in range(4)
        ]
        return edges, nodes

    for _ in range(45):
        edges, nodes = pool()
        run_trace(m, edges, nodes, lambda: (random_step() for _ in range(rng.randint(3, 14))))

    # Two rules that the random traces rarely reach. Each trace is fixed. Its pool is random, and it does not change the
    # random draws of the records that follow.
    state = rng.getstate()
    # The labels of a candidate are the labels of its first read. A later read, after a promotion of the labels in
    # memory, keeps them (setdefault).
    edges, nodes = pool()
    nodes[0].labels = ['Entity']
    run_trace(m, edges, nodes, lambda: iter([
        {'m': 'candidates', 'uuid': 'x1', 'nodes': [0]},
        {'m': 'labels', 'node': 0, 'label': 'Person'},
        {'m': 'candidates', 'uuid': 'x2', 'nodes': [0]},
    ]))
    # A second watch of an edge that is watched keeps the first state: the episode that was added between the two
    # watches is still a change when the watch stops.
    edges, nodes = pool()
    edges[0].episodes = []
    edges[0].attributes = {}
    run_trace(m, edges, nodes, lambda: iter([
        {'m': 'watch', 'edges': [0]},
        {'m': 'mutate', 'edge': 0, 'append': 'ep1'},
        {'m': 'watch', 'edges': [0]},
        {'m': 'collect_all'},
    ]))
    rng.setstate(state)


def make_change_set(m, nodes_module, EntityEdge, EpisodicEdge):
    EpisodeType = nodes_module.EpisodeType
    n_eps = rng.randint(0, 2)
    episodes = [
        nodes_module.EpisodicNode(
            uuid=f'ep{i}', name=f'episode {i}', group_id='g', labels=rng.sample(LABELS, rng.randint(0, 2)),
            created_at=rng.choice(DATES), source=rng.choice(list(EpisodeType)), source_description='test',
            content=rng.choice(['Ana works at Acme', 'é 中 "q" \\ \n', '']), valid_at=rng.choice(DATES),
            entity_edges=rng.sample(['e1', 'e2', 'e3'], rng.randint(0, 2)),
            episode_metadata=rand_attrs() if rng.random() < 0.5 else None,
        )
        for i in range(n_eps)
    ]
    nodes = [
        nodes_module.EntityNode(
            uuid=f'n{i}', name=rng.choice(['Ana', 'Acme', 'São Paulo']), group_id='g',
            labels=rng.sample(LABELS, rng.randint(0, 3)), created_at=rng.choice(DATES),
            name_embedding=[rand_float() for _ in range(rng.randint(0, 4))] if rng.random() < 0.6 else None,
            summary=rng.choice(['', 'summary']), attributes=rand_attrs(),
        )
        for i in range(rng.randint(0, 3))
    ]
    edges = [make_edge(EntityEdge, f'e{i}') for i in range(rng.randint(0, 3))]
    episodic_edges = [
        EpisodicEdge(uuid=f'ee{i}', source_node_uuid='ep0', target_node_uuid=f'n{i}', group_id='g', created_at=rng.choice(DATES))
        for i in range(rng.randint(0, 2))
    ]
    operations = []
    for _ in range(rng.randint(0, 6)):
        if rng.random() < 0.3:
            operations.append(m.PatchNode(node_uuid=rng.choice(['n1', 'n2', 'n3']), labels=rng.sample(LABELS, rng.randint(0, 2)), attributes=rand_attrs()))
        else:
            operations.append(rand_op(m))
    saga_links = [
        m.SagaLink(
            saga_uuid='s1', episode_uuid=f'ep{i}', previous_episode_uuid=rng.choice([None, 'ep9']),
            has_episode_edge_uuid=f'h{i}', next_episode_edge_uuid=rng.choice([None, 'nx']), created_at=rng.choice(DATES),
        )
        for i in range(rng.randint(0, 2))
    ]
    saga = None
    if rng.random() < 0.4:
        saga = nodes_module.SagaNode(
            uuid='s1', name='saga', group_id='g', created_at=rng.choice(DATES), summary='s',
            first_episode_uuid=rng.choice([None, 'ep0']), last_summarized_at=maybe_dt(),
        )
    return m.ChangeSet(
        group_id=rng.choice(['g', 'group-é']), unit_id=f'u{rng.randint(1, 99)}', seq=rng.randint(1, 10**6),
        snapshot_seq=rng.randint(0, 10), analysis_started_at=rng.choice([0.0, 12.5, 1767225600.123456, rng.random() * 1e9]),
        episodes=episodes, create_nodes=nodes, create_edges=edges, episodic_edges=episodic_edges, operations=operations,
        node_uuids=rng.sample(['n1', 'n2', 'n3'], rng.randint(0, 3)),
        node_first_episode={k: 'ep0' for k in rng.sample(['n1', 'n2'], rng.randint(0, 2))},
        seen_node_candidates={k: rng.sample(['s1', 's2'], rng.randint(0, 2)) for k in rng.sample(['n1', 'n2'], rng.randint(0, 2))},
        seen_node_labels={k: rng.sample(LABELS, rng.randint(0, 2)) for k in rng.sample(['s1', 's2'], rng.randint(0, 2))},
        seen_duplicate_candidates={k: ['e9'] for k in rng.sample(['e1', 'e2'], rng.randint(0, 2))},
        seen_invalidation_candidates={k: ['e8'] for k in rng.sample(['e1', 'e2'], rng.randint(0, 2))},
        edges_ended_by_candidate=rng.sample(['e1', 'e2'], rng.randint(0, 2)),
        uuid_map={k: 'n9' for k in rng.sample(['x', 'y'], rng.randint(0, 2))},
        saga_links=saga_links, saga_node=saga, create_saga=rng.random() < 0.5,
    )


def record_change_sets(m, nodes_module, EntityEdge, EpisodicEdge) -> None:
    for _ in range(60):
        change_set = make_change_set(m, nodes_module, EntityEdge, EpisodicEdge)
        payload = change_set.to_bytes()
        restored = m.ChangeSet.from_bytes(payload)
        records.append({
            'f': 'ChangeSet',
            'a': [change_set.model_dump_json()],
            'r': {
                'bytes': payload.hex(),
                'text_of_bytes': zlib.decompress(payload).decode() == change_set.model_dump_json(),
                # The text of the restored change set and of its deep copy equals the input text.
                'restored_equal': restored.model_dump_json() == change_set.model_dump_json(),
                'deep_copy_equal': change_set.model_copy(deep=True).model_dump_json() == change_set.model_dump_json(),
                'patches_by_node': {k: [p.model_dump(mode='json') for p in v] for k, v in change_set.patches_by_node().items()},
                'edge_operations': [op.model_dump(mode='json') for op in change_set.edge_operations()],
            },
        })


def record_config(config_module) -> None:
    from pydantic import ValidationError

    good = {
        'window': [1, 4, '7', 2.0, True],
        'lease_seconds': [60.0, 15, 0.5, '30', 1e9],
        'write_margin_seconds': [0, 5.0, 4.999, 1],
        'poll_interval_seconds': [0.25, 1, '0.5'],
        'index_lag_seconds': [0, 300.0, 1.5],
        'max_apply_attempts': [1, 5, '3'],
    }
    bad = {
        'window': [0, -1, 2.5, 'x', None, [1]],
        'lease_seconds': [0, -1.0, 'x', None],
        'write_margin_seconds': [-1, -0.001, 'x'],
        'poll_interval_seconds': [0, -0.25],
        'index_lag_seconds': [-0.1, 'x'],
        'max_apply_attempts': [0, -2, 1.5],
        'state_store': [1, 'store', {'a': 1}],
        'enabled_for_group': [1, 'x'],
        'before_commit_write': ['x'],
        'before_enrichment_write': [3],
    }
    for _ in range(300):
        kwargs = {}
        for name, values in good.items():
            if name == 'window' or rng.random() < 0.5:
                kwargs[name] = rng.choice(values)
        if rng.random() < 0.15:
            kwargs.pop('window')
        elif rng.random() < 0.5:
            name = rng.choice(list(bad))
            kwargs[name] = rng.choice(bad[name])
        try:
            config = config_module.ConcurrentIngestionConfig(**kwargs)
        except ValidationError as error:
            result = {'err': ['ValidationError', strip_pydantic(str(error))]}
        else:
            result = {'ok': {
                'window': config.window,
                'state_store': config.state_store,
                'lease_seconds': config.lease_seconds,
                'write_margin_seconds': config.write_margin_seconds,
                'poll_interval_seconds': config.poll_interval_seconds,
                'index_lag_seconds': config.index_lag_seconds,
                'max_apply_attempts': config.max_apply_attempts,
            }}
        records.append({'f': 'ConcurrentIngestionConfig', 'a': [kwargs], 'r': result})


def record_merge_nan(m) -> None:
    # `current_value == base.get(key, missing)` is a plain ==, so a nan is not equal to itself, also when it is one
    # object in both maps. In a list, the comparison of the items tests identity first. The test builds the same
    # objects: one nan object where the name says "same object".
    nan = float('nan')
    other = float('nan')
    for name, base, current in [
        ('same object, top level', {'x': nan}, {'x': nan}),
        ('two objects, top level', {'x': other}, {'x': nan}),
        ('same object in a list', {'x': [nan]}, {'x': [nan]}),
        ('two objects in a list', {'x': [other]}, {'x': [nan]}),
    ]:
        records.append({'f': 'merge_edge_attributes_nan', 'a': [name], 'r': m.merge_edge_attributes(base, {'x': 1}, current)})


def mutate_bytes(payload: bytes, spec: list) -> bytes:
    kind = spec[0]
    if kind == 'keep':
        return payload[:spec[1]]
    if kind == 'cut_tail':
        return payload[:len(payload) - spec[1]]
    if kind == 'xor':
        out = bytearray(payload)
        out[spec[1]] ^= spec[2]
        return bytes(out)
    if kind == 'append':
        return payload + bytes.fromhex(spec[1])
    return payload


def bytes_result(m, payload: bytes) -> list:
    # The text of a zlib.error is recorded for the three kinds that an input of this recording gives with every zlib: a
    # cut stream, a wrong header, and a wrong check value. Another kind has a text that depends on the build of zlib.
    try:
        restored = m.ChangeSet.from_bytes(payload)
    except zlib.error as error:
        text = str(error)
        known = ('incomplete or truncated stream', 'incorrect header check', 'incorrect data check')
        return ['zlib.error', text if text.endswith(known) else None]
    except Exception as error:
        return [type(error).__name__, None]
    return ['ok', restored.model_dump_json()]


def record_corrupt_bytes(m, nodes_module, EntityEdge, EpisodicEdge) -> None:
    minimal = m.ChangeSet(group_id='g', unit_id='u', seq=1, snapshot_seq=0, analysis_started_at=1.0)
    sets = [minimal, make_change_set(m, nodes_module, EntityEdge, EpisodicEdge), make_change_set(m, nodes_module, EntityEdge, EpisodicEdge)]
    for change_set in sets:
        payload = change_set.to_bytes()
        n = len(payload)
        specs = [
            ['none'],
            ['keep', 0],
            ['keep', 1],
            ['keep', n // 2],
            ['cut_tail', 1],
            ['cut_tail', 3],
            ['cut_tail', 4],
            ['xor', 0, 0xFF],
            ['xor', 1, 0x01],
            ['xor', n // 2, 0x55],
            ['xor', n - 4, 0x10],
            ['xor', n - 1, 0x01],
            ['append', 'deadbeef'],
            ['append', '00'],
        ]
        if change_set is minimal:
            # Each cut and each changed byte of a short stream.
            specs += [['keep', k] for k in range(n + 1)] + [['xor', i, 0x55] for i in range(n)]
        records.append({
            'f': 'ChangeSetBytes',
            'a': [payload.hex()],
            'r': [[spec, bytes_result(m, mutate_bytes(payload, spec))] for spec in specs],
        })


def record_deep_copy(m, nodes_module, EntityEdge, EpisodicEdge) -> None:
    # model_copy(deep=True) is copy.deepcopy: each list, dict, and model is new, a str or a float is the same object, and
    # an object that the change set holds two times is copied one time.
    node = nodes_module.EntityNode(
        uuid='n1', name='Ana', group_id='g', labels=['Entity', 'Person'], created_at=DATES[0], name_embedding=[0.5, 0.25],
        summary='s', attributes={'tags': ['a', ['b']], 'meta': {'k': [1, 2]}},
    )
    edge = EntityEdge(
        uuid='e1', group_id='g', source_node_uuid='a', target_node_uuid='b', name='WORKS_AT', fact='f', created_at=DATES[0],
        episodes=['ep0'], attributes={'tags': ['x', {'y': [1]}]}, fact_embedding=[1.0, 2.0],
    )
    episode = nodes_module.EpisodicNode(
        uuid='ep0', name='episode', group_id='g', labels=['Episodic'], created_at=DATES[0], source=nodes_module.EpisodeType.text,
        source_description='t', content='c', valid_at=DATES[0], entity_edges=['e1'], episode_metadata={'m': [1]},
    )
    episodic_edge = EpisodicEdge(uuid='ee0', source_node_uuid='ep0', target_node_uuid='n1', group_id='g', created_at=DATES[0])
    add = m.AddEpisodesToEdge(edge_uuid='e1', episode_uuids=['ep1'])
    end = m.EndEdge(edge_uuid='e1', invalid_at=DATES[1], expired_at=DATES[2], only_if_open=False)
    set_op = m.SetEdgeAttributes(edge_uuid='e1', base_attributes={'t': [1]}, attributes={'t': [1, 2], 'u': {'v': [3]}})
    patch = m.PatchNode(node_uuid='n1', labels=['A'], attributes={'t': [1]})
    saga = nodes_module.SagaNode(uuid='s1', name='saga', group_id='g', created_at=DATES[0], summary='s', labels=['Saga'])
    link = m.SagaLink(
        saga_uuid='s1', episode_uuid='ep0', previous_episode_uuid=None, has_episode_edge_uuid='h0', next_episode_edge_uuid=None,
        created_at=DATES[0],
    )
    change_set = m.ChangeSet(
        group_id='g', unit_id='u', seq=1, snapshot_seq=0, analysis_started_at=1.0, episodes=[episode],
        create_nodes=[node, node], create_edges=[edge, edge], episodic_edges=[episodic_edge],
        operations=[add, end, set_op, patch, set_op], node_uuids=['n1', 'n2'], node_first_episode={'n1': 'ep0'},
        seen_node_candidates={'n1': ['s1']}, seen_node_labels={'s1': ['Entity']}, seen_duplicate_candidates={'e1': ['e9']},
        seen_invalidation_candidates={'e1': ['e8']}, edges_ended_by_candidate=['e1'], uuid_map={'x': 'n9'},
        saga_links=[link], saga_node=saga,
    )
    copy_ = change_set.model_copy(deep=True)
    probes = {
        'node_uuids': lambda a, b: b.node_uuids is a.node_uuids,
        'node_uuids_item': lambda a, b: b.node_uuids[0] is a.node_uuids[0],
        'episodes': lambda a, b: b.episodes is a.episodes,
        'episode0': lambda a, b: b.episodes[0] is a.episodes[0],
        'episode0_labels': lambda a, b: b.episodes[0].labels is a.episodes[0].labels,
        'episode0_entity_edges': lambda a, b: b.episodes[0].entity_edges is a.episodes[0].entity_edges,
        'episode0_metadata': lambda a, b: b.episodes[0].episode_metadata is a.episodes[0].episode_metadata,
        'episode0_metadata_list': lambda a, b: b.episodes[0].episode_metadata['m'] is a.episodes[0].episode_metadata['m'],
        'create_nodes': lambda a, b: b.create_nodes is a.create_nodes,
        'node0': lambda a, b: b.create_nodes[0] is a.create_nodes[0],
        'node0_labels': lambda a, b: b.create_nodes[0].labels is a.create_nodes[0].labels,
        'node0_embedding': lambda a, b: b.create_nodes[0].name_embedding is a.create_nodes[0].name_embedding,
        'node0_attributes': lambda a, b: b.create_nodes[0].attributes is a.create_nodes[0].attributes,
        'node0_tags': lambda a, b: b.create_nodes[0].attributes['tags'] is a.create_nodes[0].attributes['tags'],
        'node0_tags_inner': lambda a, b: b.create_nodes[0].attributes['tags'][1] is a.create_nodes[0].attributes['tags'][1],
        'node0_meta_list': lambda a, b: b.create_nodes[0].attributes['meta']['k'] is a.create_nodes[0].attributes['meta']['k'],
        'dup_nodes': lambda a, b: b.create_nodes[0] is b.create_nodes[1],
        'create_edges': lambda a, b: b.create_edges is a.create_edges,
        'edge0': lambda a, b: b.create_edges[0] is a.create_edges[0],
        'edge0_episodes': lambda a, b: b.create_edges[0].episodes is a.create_edges[0].episodes,
        'edge0_embedding': lambda a, b: b.create_edges[0].fact_embedding is a.create_edges[0].fact_embedding,
        'edge0_attributes': lambda a, b: b.create_edges[0].attributes is a.create_edges[0].attributes,
        'edge0_tags': lambda a, b: b.create_edges[0].attributes['tags'] is a.create_edges[0].attributes['tags'],
        'edge0_tags_dict': lambda a, b: b.create_edges[0].attributes['tags'][1] is a.create_edges[0].attributes['tags'][1],
        'edge0_tags_list': lambda a, b: b.create_edges[0].attributes['tags'][1]['y'] is a.create_edges[0].attributes['tags'][1]['y'],
        'dup_edges': lambda a, b: b.create_edges[0] is b.create_edges[1],
        'episodic_edges': lambda a, b: b.episodic_edges is a.episodic_edges,
        'episodic_edge0': lambda a, b: b.episodic_edges[0] is a.episodic_edges[0],
        'operations': lambda a, b: b.operations is a.operations,
        'op_add': lambda a, b: b.operations[0] is a.operations[0],
        'op_add_episode_uuids': lambda a, b: b.operations[0].episode_uuids is a.operations[0].episode_uuids,
        'op_end': lambda a, b: b.operations[1] is a.operations[1],
        'op_set': lambda a, b: b.operations[2] is a.operations[2],
        'op_set_base': lambda a, b: b.operations[2].base_attributes is a.operations[2].base_attributes,
        'op_set_base_list': lambda a, b: b.operations[2].base_attributes['t'] is a.operations[2].base_attributes['t'],
        'op_set_attributes': lambda a, b: b.operations[2].attributes is a.operations[2].attributes,
        'op_set_nested': lambda a, b: b.operations[2].attributes['u']['v'] is a.operations[2].attributes['u']['v'],
        'op_patch': lambda a, b: b.operations[3] is a.operations[3],
        'op_patch_labels': lambda a, b: b.operations[3].labels is a.operations[3].labels,
        'op_patch_attributes': lambda a, b: b.operations[3].attributes is a.operations[3].attributes,
        'dup_ops': lambda a, b: b.operations[2] is b.operations[4],
        'node_first_episode': lambda a, b: b.node_first_episode is a.node_first_episode,
        'seen_node_candidates': lambda a, b: b.seen_node_candidates is a.seen_node_candidates,
        'seen_node_candidates_list': lambda a, b: b.seen_node_candidates['n1'] is a.seen_node_candidates['n1'],
        'seen_node_labels': lambda a, b: b.seen_node_labels is a.seen_node_labels,
        'seen_node_labels_list': lambda a, b: b.seen_node_labels['s1'] is a.seen_node_labels['s1'],
        'seen_duplicate_candidates': lambda a, b: b.seen_duplicate_candidates is a.seen_duplicate_candidates,
        'seen_duplicate_candidates_list': lambda a, b: b.seen_duplicate_candidates['e1'] is a.seen_duplicate_candidates['e1'],
        'seen_invalidation_candidates': lambda a, b: b.seen_invalidation_candidates is a.seen_invalidation_candidates,
        'seen_invalidation_candidates_list': lambda a, b: b.seen_invalidation_candidates['e1'] is a.seen_invalidation_candidates['e1'],
        'edges_ended_by_candidate': lambda a, b: b.edges_ended_by_candidate is a.edges_ended_by_candidate,
        'uuid_map': lambda a, b: b.uuid_map is a.uuid_map,
        'saga_links': lambda a, b: b.saga_links is a.saga_links,
        'saga_link0': lambda a, b: b.saga_links[0] is a.saga_links[0],
        'saga_node': lambda a, b: b.saga_node is a.saga_node,
        'saga_node_labels': lambda a, b: b.saga_node.labels is a.saga_node.labels,
    }
    records.append({
        'f': 'ChangeSetDeepCopy',
        'a': [change_set.model_dump_json()],
        'r': {
            'shared': {name: probe(change_set, copy_) for name, probe in probes.items()},
            'copy_equal': copy_.model_dump_json() == change_set.model_dump_json(),
        },
    })


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.dont_write_bytecode = True
    sys.path.insert(0, str(UPSTREAM))
    logging.disable(logging.CRITICAL)
    from graphiti_core import nodes as nodes_module
    from graphiti_core.edges import EntityEdge, EpisodicEdge
    from graphiti_core.ingestion import change_set as m
    from graphiti_core.ingestion import config as config_module

    record_functions(m, EntityEdge)
    record_traces(m, EntityEdge, nodes_module.EntityNode)
    record_change_sets(m, nodes_module, EntityEdge, EpisodicEdge)
    record_config(config_module)
    record_merge_nan(m)
    record_corrupt_bytes(m, nodes_module, EntityEdge, EpisodicEdge)
    record_deep_copy(m, nodes_module, EntityEdge, EpisodicEdge)
    lines = [json.dumps(r, ensure_ascii=True, allow_nan=True, separators=(',', ':')) for r in records]
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3])
        + '","source":"graphiti_core/ingestion/change_set.py, graphiti_core/ingestion/config.py","cases":[\n'
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
