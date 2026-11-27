"""Records results of graphiti_core/ingestion/change_set.py and config.py from real Python, for the evaluation tests.

The judge has 67 cases for these two modules. This file gives the evaluation tests calls with generated
inputs from a fixed seed (port/RULES.md, R23 and R25):

- merge_edge_attributes, apply_edge_operation (two times on one edge), and _diff_edge_state,
- ResolutionTrace: a list of steps on a pool of stored edges and nodes, with the state of the trace after each
  step. No step calls a function of utils/maintenance (layer 7),
- ChangeSet: the text of model_dump_json(), the bytes of to_bytes() as hex, patches_by_node(), and
  edge_operations(). The stored form of a change set is a stored data shape, so the test compares the bytes,
- ConcurrentIngestionConfig(**kwargs): the fields, or the class and the text of the error.

    .upstream/graphiti/.venv/bin/python -B port/tools/record_change_set.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_change_set.py --check

It writes tests/Port.Evaluation/values/change_set.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The environment variable GRAPHITI_UPSTREAM names the upstream
folder, for a worktree that has no .upstream. Nothing here uses the network.

A pydantic message is recorded without ', input_value=..., input_type=...]' and without the link line
(RULES.md R25). A config case has at most one field that is not valid, because the port reports a
constraint error only after the type errors (PydanticFields.ThrowIfAny).
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


def record_traces(m, EntityEdge, EntityNode) -> None:
    for _ in range(45):
        edges = [make_edge(EntityEdge, f'e{i}') for i in range(4)]
        nodes = [
            EntityNode(
                uuid=f'n{i}', name=f'N{i}', group_id='g', created_at=DATES[0],
                labels=rng.sample(LABELS, rng.randint(0, 3)), attributes=rand_attrs(),
            )
            for i in range(4)
        ]
        start = {
            'edges': [e.model_dump(mode='json') for e in edges],
            'nodes': [n.model_dump(mode='json') for n in nodes],
        }
        trace = m.ResolutionTrace()
        steps = []
        for _ in range(rng.randint(3, 14)):
            kind = rng.choice(['candidates', 'resolution', 'edge_candidates', 'watch', 'mutate', 'mark', 'collect', 'collect_all', 'labels'])
            pick = sorted(rng.sample(range(4), rng.randint(0, 4)))
            if kind == 'candidates':
                uuid = rng.choice(['x1', 'x2', 'n0'])
                step = {'m': kind, 'uuid': uuid, 'nodes': pick}
                result = trace.record_node_candidates(uuid, [nodes[i] for i in pick])
            elif kind == 'resolution':
                i = rng.randrange(4)
                resolved = rng.choice([nodes[i].uuid, 'stored-1', 'stored-2'])
                step = {'m': kind, 'node': i, 'resolved': resolved}
                result = trace.record_node_resolution(nodes[i], resolved)
            elif kind == 'edge_candidates':
                other = sorted(rng.sample(range(4), rng.randint(0, 4)))
                uuid = rng.choice(['x1', 'x2'])
                step = {'m': kind, 'uuid': uuid, 'duplicates': pick, 'invalidations': other}
                result = trace.record_edge_candidates(uuid, [edges[i] for i in pick], [edges[i] for i in other])
            elif kind == 'watch':
                step = {'m': kind, 'edges': pick}
                result = trace.watch_stored_edges([edges[i] for i in pick])
            elif kind == 'mutate':
                i = rng.randrange(4)
                edge = edges[i]
                step = {'m': kind, 'edge': i}
                if rng.random() < 0.5:
                    step['append'] = rng.choice(EPISODES)
                    edge.episodes.append(step['append'])
                if rng.random() < 0.4:
                    step['attributes'] = rand_attrs()
                    edge.attributes = dict(step['attributes'])
                if rng.random() < 0.3:
                    step['set_key'] = [rng.choice(KEYS), rng.choice(VALUES)]
                    edge.attributes[step['set_key'][0]] = step['set_key'][1]
                if rng.random() < 0.4:
                    step['invalid_at'] = dt_json(maybe_dt())
                    edge.invalid_at = None if step['invalid_at'] is None else datetime.fromisoformat(step['invalid_at'])
                if rng.random() < 0.4:
                    step['expired_at'] = dt_json(maybe_dt())
                    edge.expired_at = None if step['expired_at'] is None else datetime.fromisoformat(step['expired_at'])
                result = None
            elif kind == 'labels':
                # A promotion of the labels of a candidate in memory, after its first read.
                i = rng.randrange(4)
                label = rng.choice(LABELS)
                step = {'m': kind, 'node': i, 'label': label}
                nodes[i].labels = [*nodes[i].labels, label]
                result = None
            elif kind == 'mark':
                step = {'m': kind, 'edges': pick}
                result = trace.mark_ended_as_candidate([edges[i] for i in pick])
            elif kind == 'collect':
                step = {'m': kind, 'edges': pick}
                result = [op.model_dump(mode='json') for op in trace.collect_edge_operations([edges[i] for i in pick])]
            else:
                step = {'m': kind}
                result = [op.model_dump(mode='json') for op in trace.collect_edge_operations()]
            steps.append({'step': step, 'result': result, 'state': trace_state(trace)})
        records.append({'f': 'ResolutionTrace', 'a': [start], 'r': steps})


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
