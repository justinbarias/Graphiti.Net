"""Records what graphiti_core/nodes.py does, from real Python (port/RULES.md, R7, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_nodes.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_nodes.py --check

Sections of the recording:

- construct: EpisodicNode, EntityNode, CommunityNode, and SagaNode built from keyword arguments
  (M(**data), which the port reads with ModelValidate). The tool changes one field at a time, and
  it also builds random combinations from a fixed seed. Each case keeps the model_dump() or the error.
- records: the four get_*_from_record functions with generated records.
- equality: a == b and hash(a) == hash(b) for pairs of nodes (GAPS J2).
- copies: whether a list or a dict value is the same object after construction, assignment,
  model_copy(), and model_copy(deep=True) (R7).
- methods: each save, delete, get_by_*, load_*, and embedding method of the four node classes, and
  create_entity_node_embeddings. A fake driver records each call to the executor, to a session, and
  to the graph operations interface. Each method runs for each provider and in three modes: no
  graph operations interface, an interface that raises NotImplementedError, and an interface that
  returns a value. A fake embedder stands in for the embedding client. The calls and the log lines
  of one run are one list in the order of their time, so the order of a log line and a driver call
  is part of the recording.

The tool writes tests/Port.Evaluation/values/nodes.json. --check writes nothing and exits 1 if the
file differs from what the run would write. The tool needs Python 3.12 and the pinned upstream in
.upstream/graphiti. It changes nothing there. Nothing here uses the network.

Inputs that the port cannot reproduce by a rule are left out, and the reports of plan 0004 packet P2a
and its fix round name them: CommunityNode.save on Neptune (driver.save_to_aoss), and a graph
operations interface that returns None for get_by_uuids with a group id or with require_all (Python
raises TypeError there). A record value of a wrong type at a method call (attributes, labels, group_id)
is recorded: Py.Receiver gives the AttributeError of Python (R4).
"""
from __future__ import annotations

import asyncio
import copy as copy_module
import json
import os
import logging
import random
import re
import sys
from datetime import datetime, timezone
from enum import Enum
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
# GRAPHITI_UPSTREAM names the pinned upstream checkout when the working tree has no .upstream (a git worktree).
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM', str(ROOT / '.upstream' / 'graphiti')))
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'nodes.json'
SEED = 20261127

T0 = datetime(2026, 1, 1, tzinfo=timezone.utc)
T1 = datetime(2026, 2, 1, 12, 30, 15, 123456, tzinfo=timezone.utc)

STR_VALUES = ['x', '', 'Zoë 東京 😀', 5, 1.5, True, None, ['a']]
DT_VALUES = [T1, '2026-03-01T12:00:00+02:00', '2026-03-01T12:00:00Z', '2026-03-01 12:00:00', 'yesterday', 0, 1.5e9, None, True]
LABEL_VALUES = [[], ['Person'], ['A', 'A'], ['_Entity_2', 'A1'], ['Bad Label'], ['1abc'], ['Abc\n'], [5], 'Person', None]
GOOD_LABELS = [[], ['Person'], ['A', 'A'], ['_Entity_2', 'A1'], ['Abc\n']]
EMB_VALUES = [None, [], [0.1, 2], ['1.5'], ['x'], [True], 'abc', 5, [1e300, -0.0]]
DICT_VALUES = [{}, {'a': 1, 'b': [1, 2], 'c': {'k': None}}, None, [], 'x']
STR_LIST_VALUES = [[], ['e1', 'e2'], [1], 'e1', None]
OPT_STR_VALUES = [None, 'e1', '', 5]
OPT_DT_VALUES = [None, T1, '2026-03-01T12:00:00Z', 'soon']
SOURCE_VALUES = ['text', 'message', 'fact_triple', 'html', 'JSON', None, 5]

PROVIDERS = ['neo4j', 'falkordb', 'neptune', 'relate']
MODES = ['none', 'raise', 'return']


def encode(value):
    """A value with its Python type."""
    from graphiti_core.nodes import Node

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
    if isinstance(value, Enum):
        return {'type': 'enum', 'class': type(value).__name__, 'value': value.value}
    if isinstance(value, Node):
        return {'type': 'node', 'class': type(value).__name__, 'uuid': value.uuid}
    if isinstance(value, type):
        return {'type': 'class', 'value': value.__name__}
    if isinstance(value, (list, tuple)):
        return {'type': 'list', 'items': [encode(item) for item in value]}
    if isinstance(value, dict):
        return {'type': 'dict', 'items': [[key, encode(item)] for key, item in value.items()]}
    return {'type': type(value).__name__}


def encode_kwargs(kwargs):
    return [[key, encode(value)] for key, value in kwargs.items()]


def dump(node, kwargs=None):
    """model_dump() of a node with its class. A random uuid or a created_at from the clock is a marker."""
    data = node.model_dump()
    if kwargs is not None and 'uuid' not in kwargs:
        data['uuid'] = '<random>'
    if kwargs is not None and 'created_at' not in kwargs:
        data['created_at'] = '<now>'
    return {'class': type(node).__name__, 'dump': encode(data)}


def error_of(error):
    return {'type': type(error).__name__, 'text': str(error)}


def base_kwargs(cls_name):
    common = {'uuid': 'u-1', 'name': 'Alice', 'group_id': 'g-1', 'labels': ['Person'], 'created_at': T0}
    if cls_name == 'EpisodicNode':
        return {
            **common,
            'source': 'text',
            'source_description': 'desc',
            'content': 'user: 你好 😀',
            'valid_at': T0,
            'entity_edges': ['e1'],
            'episode_metadata': {'k': 'v'},
        }
    if cls_name == 'EntityNode':
        return {**common, 'name_embedding': [0.5, 1.0], 'summary': 'sum', 'attributes': {'age': 3, 'tags': ['a']}}
    if cls_name == 'CommunityNode':
        return {**common, 'name_embedding': [0.5], 'summary': 'sum'}
    return {
        **common,
        'summary': 'sum',
        'first_episode_uuid': 'e1',
        'last_episode_uuid': 'e2',
        'last_summarized_at': T0,
        'last_summarized_episode_valid_at': T1,
    }


def pools(cls_name):
    common = {
        'uuid': STR_VALUES,
        'name': STR_VALUES,
        'group_id': STR_VALUES,
        'labels': LABEL_VALUES,
        'created_at': DT_VALUES,
    }
    if cls_name == 'EpisodicNode':
        return {
            **common,
            'source': SOURCE_VALUES,
            'source_description': STR_VALUES,
            'content': STR_VALUES,
            'valid_at': DT_VALUES,
            'entity_edges': STR_LIST_VALUES,
            'episode_metadata': DICT_VALUES,
        }
    if cls_name == 'EntityNode':
        return {**common, 'name_embedding': EMB_VALUES, 'summary': STR_VALUES, 'attributes': DICT_VALUES}
    if cls_name == 'CommunityNode':
        return {**common, 'name_embedding': EMB_VALUES, 'summary': STR_VALUES}
    return {
        **common,
        'summary': STR_VALUES,
        'first_episode_uuid': OPT_STR_VALUES,
        'last_episode_uuid': OPT_STR_VALUES,
        'last_summarized_at': OPT_DT_VALUES,
        'last_summarized_episode_valid_at': OPT_DT_VALUES,
    }


CLASS_NAMES = ['EpisodicNode', 'EntityNode', 'CommunityNode', 'SagaNode']
OTHER_BAD = {
    'EpisodicNode': {'source': 'html'},
    'EntityNode': {'summary': None},
    'CommunityNode': {'summary': None},
    'SagaNode': {'last_summarized_at': 'soon'},
}


def construct_cases(rng):
    out = []
    for cls_name in CLASS_NAMES:
        base = base_kwargs(cls_name)
        out.append({'class': cls_name, 'kwargs': dict(base)})
        for field, values in pools(cls_name).items():
            missing = dict(base)
            del missing[field]
            out.append({'class': cls_name, 'kwargs': missing})
            for value in values:
                out.append({'class': cls_name, 'kwargs': {**base, field: value}})
        # A bad label together with another field error: pydantic reports both, in field order.
        for other in [{'name': 5}, {'name': None}, {'created_at': 'x'}, OTHER_BAD[cls_name], {'name': 5, 'created_at': 'x'}]:
            out.append({'class': cls_name, 'kwargs': {**base, 'labels': ['Bad Label'], **other}})
        missing = dict(base)
        del missing['name']
        out.append({'class': cls_name, 'kwargs': {**missing, 'labels': ['Bad Label', 'Worse Label']}})
        for _ in range(25):
            kwargs = {}
            for field, values in pools(cls_name).items():
                if rng.random() < 0.15:
                    continue
                kwargs[field] = rng.choice(GOOD_LABELS if field == 'labels' else values)
            out.append({'class': cls_name, 'kwargs': kwargs})
    return out


def record_cases():
    out = []
    episodic = {
        'uuid': 'ep1',
        'name': 'episode',
        'group_id': 'g',
        'created_at': '2026-01-01T00:00:00+00:00',
        'valid_at': T1,
        'source': 'text',
        'source_description': 'sd',
        'content': 'c',
        'entity_edges': ['e1'],
    }
    episodic_values = {
        'created_at': [None, 'not-a-date', '2026-01-01T00:00:00', T0],
        'valid_at': [None, '2025-12-31 23:00:00+00:00'],
        'source': ['html', 'fact_triple', 'message', None, 5],
        'entity_edges': [None, 'e1', [1]],
        'uuid': [None, 5],
        'name': [None],
        'content': [5],
    }
    out.extend(variants('get_episodic_node_from_record', episodic, episodic_values))
    entity = {
        'uuid': 'n1',
        'name': 'Alice',
        'group_id': 'a-b-c',
        'created_at': '2026-01-01T00:00:00+00:00',
        'summary': 's',
        'labels': ['Entity', 'Person', 'Entity_abc', 'Entity_abc'],
        'name_embedding': [0.1, 2],
        'attributes': {'uuid': 'x', 'name': 'x', 'group_id': 'x', 'name_embedding': 'x', 'summary': 'x', 'created_at': 'x', 'labels': 'x', 'age': 3, 'city': '東京'},
    }
    entity_values = {
        'group_id': [None, '', 'abc', 5, ['a']],
        'labels': [[], ['Entity_a-b-c'], ['Bad Label', 'Entity_abc'], None, 5, 'Entity', 'xEntity_abcx'],
        'name_embedding': [None, ['x'], 'abc'],
        'created_at': [None, 'nope', T1],
        'summary': [None, 5],
        'attributes': [{}, {'k': [1, {'n': None}]}, None, 5, 'x', [1]],
        'uuid': [5],
    }
    out.extend(variants('get_entity_node_from_record', entity, entity_values))
    # A bad label together with another field error (the record helper passes labels only for an entity node).
    for other in [{'summary': None}, {'name': None}, {'created_at': 'nope'}, {'summary': None, 'name_embedding': ['x']}]:
        out.append({'function': 'get_entity_node_from_record', 'record': {**entity, 'labels': ['Bad Label', 'Entity_abc'], **other}})
    community = {
        'uuid': 'c1',
        'name': 'C',
        'group_id': 'g',
        'name_embedding': [1.0],
        'created_at': '2026-01-01T00:00:00+00:00',
        'summary': 's',
    }
    community_values = {
        'name_embedding': [None, ['1.5'], 'x'],
        'created_at': [None, 'bad'],
        'summary': [None],
    }
    out.extend(variants('get_community_node_from_record', community, community_values))
    saga = {
        'uuid': 's1',
        'name': 'saga',
        'group_id': 'g',
        'created_at': '2026-01-01T00:00:00+00:00',
        'summary': 'sum',
        'first_episode_uuid': 'a',
        'last_episode_uuid': 'b',
        'last_summarized_at': '2026-02-01T00:00:00+00:00',
        'last_summarized_episode_valid_at': T1,
    }
    saga_values = {
        'summary': [None, '', 0, 5],
        'first_episode_uuid': [None, 5],
        'last_summarized_at': [None, '', 'nope', 0],
        'last_summarized_episode_valid_at': ['', '2026-01-15T00:00:00'],
        'created_at': [None],
    }
    out.extend(variants('get_saga_node_from_record', saga, saga_values))
    return out


def variants(function, base, values):
    out = [{'function': function, 'record': dict(base)}]
    for key in base:
        missing = dict(base)
        del missing[key]
        out.append({'function': function, 'record': missing})
    for key, options in values.items():
        for option in options:
            out.append({'function': function, 'record': {**base, key: option}})
    return out


def method_cases():
    from graphiti_core.nodes import EpisodeType  # noqa: F401 - the import checks the module

    episodic_record = {
        'uuid': 'ep1',
        'name': 'episode',
        'group_id': 'g-1',
        'created_at': '2026-01-01T00:00:00+00:00',
        'valid_at': '2026-01-01T00:00:00+00:00',
        'source': 'message',
        'source_description': 'sd',
        'content': 'c',
        'entity_edges': ['e1'],
    }
    episodic_other = {**episodic_record, 'uuid': 'ep2', 'group_id': 'g-2'}
    entity_record = {
        'uuid': 'n1',
        'name': 'Alice',
        'group_id': 'g-1',
        'created_at': '2026-01-01T00:00:00+00:00',
        'summary': 's',
        'labels': ['Entity', 'Entity_g1'],
        'name_embedding': [0.5],
        'attributes': {'uuid': 'n1', 'age': 3},
    }
    community_record = {
        'uuid': 'c1',
        'name': 'C',
        'group_id': 'g-1',
        'name_embedding': None,
        'created_at': '2026-01-01T00:00:00+00:00',
        'summary': 's',
    }
    saga_record = {'uuid': 's1', 'name': 'saga', 'group_id': 'g-1', 'created_at': '2026-01-01T00:00:00+00:00'}
    nodes = {
        'EpisodicNode': base_kwargs('EpisodicNode'),
        'EntityNode': {**base_kwargs('EntityNode'), 'attributes': {'age': 3, 'name': 'shadow', 'city': '東京'}},
        'CommunityNode': base_kwargs('CommunityNode'),
        'SagaNode': base_kwargs('SagaNode'),
    }
    ops_nodes = {
        'EpisodicNode': [{**base_kwargs('EpisodicNode'), 'uuid': 'op-1', 'group_id': 'g-1'}, {**base_kwargs('EpisodicNode'), 'uuid': 'op-2', 'group_id': 'g-2'}],
        'EntityNode': [{**base_kwargs('EntityNode'), 'uuid': 'op-1'}],
        'CommunityNode': [{**base_kwargs('CommunityNode'), 'uuid': 'op-1'}],
        'SagaNode': [{**base_kwargs('SagaNode'), 'uuid': 'op-1'}],
    }

    specs = []

    def add(cls_name, method, args, record_sets, instance=False, ops_list=False, ops_returns=True, node=None, only_return=False):
        specs.append(
            {
                'node': node,
                'only_return': only_return,
                'class': cls_name,
                'method': method,
                'args': args,
                'record_sets': record_sets,
                'instance': instance,
                'ops_list': ops_list,
                'ops_returns': ops_returns,
            }
        )

    for cls_name in ['EpisodicNode', 'EntityNode', 'CommunityNode']:
        add(cls_name, 'delete', {}, [[]], instance=True, ops_returns=False)
    add('Node', 'delete_by_group_id', {'group_id': 'g-1', 'batch_size': 100}, [[]], ops_returns=False)
    add('Node', 'delete_by_group_id', {'group_id': 'grüp', 'batch_size': 7}, [[]], ops_returns=False)
    # No batch_size: the default of the parameter is part of the contract.
    add('Node', 'delete_by_group_id', {'group_id': 'g-1'}, [[]], ops_returns=False)
    add('Node', 'delete_by_uuids', {'uuids': ['a']}, [[]], ops_returns=False)
    add('Node', 'delete_by_uuids', {'uuids': ['a', 'b'], 'batch_size': 100}, [[]], ops_returns=False)
    add('Node', 'delete_by_uuids', {'uuids': [], 'batch_size': 3}, [[]], ops_returns=False)

    add('EpisodicNode', 'save', {}, [[]], instance=True, ops_returns=False)
    for group_id in [None, 'g-1', 'g-2']:
        add('EpisodicNode', 'get_by_uuid', {'uuid': 'ep1', 'group_id': group_id}, [[], [episodic_record], [episodic_other, episodic_record]])
    for group_id in [None, 'g-1']:
        for require_all in [False, True]:
            add(
                'EpisodicNode',
                'get_by_uuids',
                {'uuids': ['ep1', 'ep2'], 'group_id': group_id, 'require_all': require_all},
                [[], [episodic_record], [episodic_record, episodic_other]],
                ops_list=True,
            )
    add('EpisodicNode', 'get_by_uuids', {'uuids': [], 'group_id': None, 'require_all': True}, [[]], ops_list=True)
    # A graph operations interface that returns None: the else branch of try/except/else runs, and Python returns the value as it is.
    add('EpisodicNode', 'get_by_uuid', {'uuid': 'ep1', 'group_id': None}, [[]], ops_returns=False, only_return=True)
    add('EpisodicNode', 'get_by_uuid', {'uuid': 'ep1', 'group_id': 'g-1'}, [[]], ops_returns=False, only_return=True)
    add('EpisodicNode', 'get_by_uuids', {'uuids': ['ep1'], 'group_id': None, 'require_all': False}, [[]], ops_returns=False, only_return=True)
    for limit, cursor in [(None, None), (5, 'ep9'), (0, ''), (None, 'ep9')]:
        add('EpisodicNode', 'get_by_group_ids', {'group_ids': ['g-1'], 'limit': limit, 'uuid_cursor': cursor}, [[], [episodic_record]], ops_list=True)
    add('EpisodicNode', 'get_by_entity_node_uuid', {'entity_node_uuid': 'n1'}, [[], [episodic_record, episodic_other]], ops_list=True)

    add('EntityNode', 'generate_name_embedding', {}, [[]], instance=True)
    add('EntityNode', 'generate_name_embedding', {}, [[]], instance=True, node={'name': 'a\nb'})
    add('EntityNode', 'load_name_embedding', {}, [[], [{'name_embedding': [1, 2.5]}], [{'name_embedding': None}], [{'name_embedding': ['x']}], [{}], [{'name_embedding': ['1.5']}, {'name_embedding': [9]}]], instance=True, ops_returns=False)
    add('EntityNode', 'save', {}, [[]], instance=True, ops_returns=False)
    add('EntityNode', 'get_by_uuid', {'uuid': 'n1'}, [[], [entity_record]])
    add('EntityNode', 'get_by_uuids', {'uuids': ['n1'], 'group_id': None}, [[], [entity_record]], ops_list=True)
    add('EntityNode', 'get_by_uuids', {'uuids': ['n1'], 'group_id': 'g-1'}, [[entity_record]], ops_list=True)
    for names, group_ids in [
        (['b', 'a', '', 'b', 'Zoë', 'Z'], ['g-2', 'g-1', 'g-1']),
        ([], ['g-1']),
        (['a'], []),
        ([''], ['g-1']),
        (['a'], ['']),
    ]:
        add('EntityNode', 'get_by_names', {'names': names, 'group_ids': group_ids}, [[entity_record]], ops_list=True)
    for attributes, labels, limit in [
        ({'email': 'a@b', 'employee_id': 42}, ['Person', 'Entity'], 7),
        ({'email': None}, None, 15),
        ({}, None, 15),
        ({'bad name': 1}, None, 15),
        ({'email': 1}, ['Bad Label'], 15),
        ({'email': 1}, [], 3),
        ({'_x1': [1, 2]}, ['Entity'], 15),
        ({'email\n': 1}, None, 15),
    ]:
        add('EntityNode', 'get_by_attribute_values', {'group_id': 'g-1', 'attributes': attributes, 'labels': labels, 'limit': limit}, [[entity_record]], ops_list=True)
    # No limit: the default of the parameter is NODE_DEDUP_CANDIDATE_LIMIT.
    add('EntityNode', 'get_by_attribute_values', {'group_id': 'g-1', 'attributes': {'email': 'a@b'}, 'labels': ['Person']}, [[entity_record]], ops_list=True)
    for limit, cursor, with_embeddings in [(None, None, False), (5, 'n9', True), (None, '', True)]:
        add('EntityNode', 'get_by_group_ids', {'group_ids': ['g-1'], 'limit': limit, 'uuid_cursor': cursor, 'with_embeddings': with_embeddings}, [[], [entity_record]], ops_list=True)

    add('CommunityNode', 'save', {}, [[]], instance=True, ops_returns=False)
    add('CommunityNode', 'generate_name_embedding', {}, [[]], instance=True)
    add('CommunityNode', 'generate_name_embedding', {}, [[]], instance=True, node={'name': 'a\nb'})
    add('CommunityNode', 'load_name_embedding', {}, [[], [{'name_embedding': [3]}], [{'name_embedding': 'x'}]], instance=True, ops_returns=False)
    add('CommunityNode', 'get_by_uuid', {'uuid': 'c1'}, [[], [community_record]])
    add('CommunityNode', 'get_by_uuids', {'uuids': ['c1']}, [[], [community_record]], ops_list=True)
    for limit, cursor in [(None, None), (2, 'c9')]:
        add('CommunityNode', 'get_by_group_ids', {'group_ids': ['g-1', 'g-2'], 'limit': limit, 'uuid_cursor': cursor}, [[community_record]], ops_list=True)

    add('SagaNode', 'save', {}, [[]], instance=True, ops_returns=False)
    add('SagaNode', 'delete', {}, [[]], instance=True, ops_returns=False)
    add('SagaNode', 'get_by_uuid', {'uuid': 's1'}, [[], [saga_record]])
    add('SagaNode', 'get_by_uuids', {'uuids': ['s1']}, [[], [saga_record]], ops_list=True)
    for limit, cursor in [(None, None), (2, 's9')]:
        add('SagaNode', 'get_by_group_ids', {'group_ids': ['g-1'], 'limit': limit, 'uuid_cursor': cursor}, [[saga_record]], ops_list=True)

    out = []
    for spec in specs:
        for provider in PROVIDERS:
            for mode in MODES:
                # The path of the interface does not depend on the provider, so the two modes with an interface run once.
                if mode != 'none' and provider != 'neo4j':
                    continue
                if spec['class'] == 'CommunityNode' and spec['method'] == 'save' and provider == 'neptune' and mode != 'return':
                    continue
                if spec['only_return'] and mode != 'return':
                    continue
                record_sets = spec['record_sets'] if mode != 'return' else [[]]
                for records in record_sets:
                    case = {
                        'class': spec['class'],
                        'method': spec['method'],
                        'provider': provider,
                        'mode': mode,
                        'args': dict(spec['args']),
                        'records': records,
                    }
                    if spec['instance']:
                        case['node'] = {**nodes[spec['class']], **(spec['node'] or {})}
                    if mode == 'return' and spec['ops_returns']:
                        returned = ops_nodes[spec['class']]
                        case['ops_result'] = returned if spec['ops_list'] else returned[:1]
                        case['ops_list'] = spec['ops_list']
                    out.append(case)

    for names, embeddings in [
        (['a', '', 'b\nc'], 'echo'),
        ([], 'echo'),
        (['', ''], 'echo'),
        (['a', 'b'], 'short'),
        (['a', 'b'], 'long'),
    ]:
        out.append({'class': 'module', 'method': 'create_entity_node_embeddings', 'names': names, 'embeddings': embeddings})
    return out


def main() -> int:
    sys.path.insert(0, str(UPSTREAM))
    import graphiti_core.nodes as nodes_module
    from graphiti_core.driver.driver import GraphDriver, GraphDriverSession, GraphProvider
    from graphiti_core.driver.graph_operations.graph_operations import GraphOperationsInterface
    from graphiti_core.embedder import EmbedderClient
    from graphiti_core.nodes import CommunityNode, EntityNode, EpisodicNode, Node, SagaNode

    classes = {'EpisodicNode': EpisodicNode, 'EntityNode': EntityNode, 'CommunityNode': CommunityNode, 'SagaNode': SagaNode, 'Node': Node}

    # The calls and the log lines of one run share this list, so that their order is recorded.
    calls = []

    class Capture(logging.Handler):
        def __init__(self):
            super().__init__(logging.DEBUG)

        def emit(self, record):
            message = record.getMessage()
            message = re.sub(r' in [-0-9.e+]+ ms$', ' in <elapsed> ms', message)
            calls.append({'via': 'log', 'level': record.levelname, 'message': message})

    capture = Capture()
    logger = logging.getLogger(nodes_module.__name__)
    logger.setLevel(logging.DEBUG)
    logger.propagate = False
    logger.addHandler(capture)

    def run(make):
        calls.clear()
        try:
            return {'value': make(), 'error': None}
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            return {'value': None, 'error': error_of(error)}

    # construct
    rng = random.Random(SEED)
    construct = []
    for case in construct_cases(rng):
        cls = classes[case['class']]
        outcome = run(lambda: dump(cls(**case['kwargs']), case['kwargs']))
        construct.append(
            {
                'input': {'class': case['class'], 'kwargs': encode_kwargs(case['kwargs'])},
                'output': {'result': outcome['value'], 'error': outcome['error']},
            }
        )

    # records
    records_out = []
    for case in record_cases():
        function = getattr(nodes_module, case['function'])
        record = case['record']
        encoded = encode_kwargs(record)
        if case['function'] == 'get_entity_node_from_record':
            outcome = run(lambda: dump(function(dict(record), GraphProvider.NEO4J)))
        else:
            outcome = run(lambda: dump(function(dict(record))))
        records_out.append(
            {
                'input': {'function': case['function'], 'record': encoded},
                'output': {'result': outcome['value'], 'error': outcome['error']},
            }
        )

    # equality
    a = EntityNode(uuid='u1', name='A', group_id='g', created_at=T0)
    pairs = [
        ('same_uuid_other_fields', a, EntityNode(uuid='u1', name='B', group_id='h', created_at=T1)),
        ('other_uuid_same_fields', a, EntityNode(uuid='u2', name='A', group_id='g', created_at=T0)),
        ('same_object', a, a),
        ('entity_community', a, CommunityNode(uuid='u1', name='C', group_id='g', created_at=T0)),
        ('entity_episodic', a, EpisodicNode(uuid='u1', name='E', group_id='g', created_at=T0, source='text', source_description='d', content='c', valid_at=T0)),
        ('saga_entity', SagaNode(uuid='u1', name='S', group_id='g', created_at=T0), a),
        ('node_str', a, 'u1'),
        ('node_none', a, None),
        ('unicode_uuid', EntityNode(uuid='Zoë 😀', name='A', group_id='g'), EntityNode(uuid='Zoë 😀', name='B', group_id='g')),
    ]
    equality = []
    for name, left, right in pairs:
        equality.append(
            {
                'name': name,
                'eq': left == right,
                'hash_eq': isinstance(right, Node) and hash(left) == hash(right),
            }
        )

    # copies
    copies = []
    list_fields = {
        'EpisodicNode': ['labels', 'entity_edges', 'episode_metadata'],
        'EntityNode': ['labels', 'name_embedding', 'attributes'],
        'CommunityNode': ['labels', 'name_embedding'],
        'SagaNode': ['labels'],
    }
    for cls_name, fields in list_fields.items():
        cls = classes[cls_name]
        kwargs = base_kwargs(cls_name)
        if cls_name == 'EntityNode':
            kwargs['attributes'] = {'nested': {'k': [1]}, 'n': 1}
        if cls_name == 'EpisodicNode':
            kwargs['episode_metadata'] = {'n': {'k': [1]}}
        for field in fields:
            value = kwargs[field]
            node = cls(**kwargs)
            constructed = getattr(node, field) is value
            replacement = type(value)(value)
            setattr(node, field, replacement)
            assigned = getattr(node, field) is replacement
            copies.append({'class': cls_name, 'field': field, 'op': 'construct', 'same': constructed})
            copies.append({'class': cls_name, 'field': field, 'op': 'assign', 'same': assigned})
        for deep in [False, True]:
            node = cls(**kwargs)
            if cls_name == 'SagaNode':
                node._summary_episodes_fetched = 3
                node._summary_episodes_selected = 2
                node._summary_episodes_skipped = 1
                node._summary_selected_episode_bytes = 99
            copy = node.model_copy(deep=deep)
            op = 'model_copy_deep' if deep else 'model_copy'
            for field in fields:
                copies.append({'class': cls_name, 'field': field, 'op': op, 'same': getattr(copy, field) is getattr(node, field)})
            if cls_name == 'EntityNode':
                copies.append({'class': cls_name, 'field': 'attributes.nested', 'op': op, 'same': copy.attributes['nested'] is node.attributes['nested']})
            if cls_name == 'EpisodicNode':
                copies.append({'class': cls_name, 'field': 'episode_metadata.nested', 'op': op, 'same': copy.episode_metadata['n'] is node.episode_metadata['n']})
            if cls_name == 'SagaNode':
                copies.append(
                    {
                        'class': cls_name,
                        'field': 'private',
                        'op': op,
                        'values': [
                            copy._summary_episodes_fetched,
                            copy._summary_episodes_selected,
                            copy._summary_episodes_skipped,
                            copy._summary_selected_episode_bytes,
                        ],
                    }
                )
            copies.append({'class': cls_name, 'field': 'dump', 'op': op, 'same': copy.model_dump() == node.model_dump()})

    # methods
    class FakeSession(GraphDriverSession):
        async def run(self, query, **kwargs):
            calls.append({'via': 'session', 'query': query, 'keys': list(kwargs), 'values': [encode(v) for v in kwargs.values()]})

        async def close(self):
            calls.append({'via': 'session_close'})

        async def execute_write(self, func, *args, **kwargs):
            raise AssertionError('not used')

        async def __aexit__(self, exc_type, exc, tb):
            calls.append({'via': 'session_exit'})

    class FakeDriver(GraphDriver):
        def __init__(self, provider, records):
            self.provider = provider
            self.records = records
            self.graph_operations_interface = None

        async def execute_query(self, cypher_query_, **kwargs):
            calls.append({'via': 'executor', 'query': cypher_query_, 'keys': list(kwargs), 'values': [encode(v) for v in kwargs.values()]})
            return self.records, None, None

        def session(self, database=None):
            calls.append({'via': 'session_open'})
            return FakeSession()

        def close(self):
            raise AssertionError('not used')

        def delete_all_indexes(self):
            raise AssertionError('not used')

        async def build_indices_and_constraints(self, delete_existing=False):
            raise AssertionError('not used')

    returns = {}

    def op(name):
        async def method(self, *args, **kwargs):
            values = [encode(v) for v in args if not isinstance(v, GraphDriver)] + [encode(v) for v in kwargs.values()]
            calls.append({'via': 'ops', 'name': name, 'args': values})
            return returns.get('value')

        return method

    op_names = [
        'node_delete', 'node_delete_by_group_id', 'node_delete_by_uuids', 'episodic_node_save',
        'episodic_node_get_by_uuid', 'episodic_node_get_by_uuids', 'episodic_node_get_by_group_ids',
        'episodic_node_get_by_entity_node_uuid', 'node_load_embeddings', 'node_save', 'node_get_by_uuid',
        'node_get_by_uuids', 'node_get_by_names', 'node_get_by_attribute_values', 'node_get_by_group_ids',
        'community_node_save', 'community_node_load_name_embedding', 'community_node_get_by_uuid',
        'community_node_get_by_uuids', 'community_node_get_by_group_ids', 'saga_node_save', 'saga_node_delete',
        'saga_node_get_by_uuid', 'saga_node_get_by_uuids', 'saga_node_get_by_group_ids',
    ]
    ReturningOps = type('ReturningOps', (GraphOperationsInterface,), {name: op(name) for name in op_names})

    class RaisingOps(GraphOperationsInterface):
        pass

    class FakeEmbedder(EmbedderClient):
        def __init__(self, mode='echo'):
            self.mode = mode

        async def create(self, input_data):
            calls.append({'via': 'embedder', 'name': 'create', 'args': [encode(input_data)]})
            return [float(len(input_data[0])), 0.5]

        async def create_batch(self, input_data_list):
            calls.append({'via': 'embedder', 'name': 'create_batch', 'args': [encode(input_data_list)]})
            result = [[float(len(text)), 1.0] for text in input_data_list]
            if self.mode == 'short':
                return result[:-1]
            if self.mode == 'long':
                return result + [[9.0]]
            return result

    methods = []
    for case in method_cases():
        calls.clear()
        if case['class'] == 'module':
            built = [EntityNode(uuid=f'n{i}', name=name, group_id='g', created_at=T0) for i, name in enumerate(case['names'])]
            outcome = {}
            try:
                asyncio.run(nodes_module.create_entity_node_embeddings(FakeEmbedder(case['embeddings']), built))
                outcome['error'] = None
            except Exception as error:  # noqa: BLE001
                outcome['error'] = error_of(error)
            outcome['calls'] = list(calls)
            outcome['embeddings'] = [encode(n.name_embedding) for n in built]
            methods.append({'input': {'class': 'module', 'method': case['method'], 'names': case['names'], 'embeddings': case['embeddings']}, 'output': outcome})
            continue

        cls = classes[case['class']]
        driver = FakeDriver(GraphProvider(case['provider']), copy_module.deepcopy(case['records']))
        returns.clear()
        if case['mode'] == 'raise':
            driver.graph_operations_interface = RaisingOps()
        elif case['mode'] == 'return':
            driver.graph_operations_interface = ReturningOps()
            if 'ops_result' in case:
                built = [cls(**kwargs) for kwargs in case['ops_result']]
                returns['value'] = built if case['ops_list'] else built[0]
        node = cls(**case['node']) if 'node' in case else None
        outcome = {}
        try:
            if node is not None:
                if case['method'] == 'generate_name_embedding':
                    result = asyncio.run(node.generate_name_embedding(FakeEmbedder()))
                else:
                    result = asyncio.run(getattr(node, case['method'])(driver))
            else:
                result = asyncio.run(getattr(cls, case['method'])(driver, **case['args']))
            outcome['error'] = None
            if case['method'].startswith('get_') or case['method'] == 'generate_name_embedding':
                if isinstance(result, list) and all(isinstance(r, Node) for r in result):
                    outcome['result'] = [dump(r) for r in result]
                elif isinstance(result, Node):
                    outcome['result'] = dump(result)
                else:
                    outcome['result'] = encode(result)
        except Exception as error:  # noqa: BLE001
            outcome['error'] = error_of(error)
        outcome['calls'] = list(calls)
        if node is not None:
            outcome['node'] = dump(node)
        entry = {
            'class': case['class'],
            'method': case['method'],
            'provider': case['provider'],
            'mode': case['mode'],
            'args': encode_kwargs(case['args']),
            'records': encode(case['records']),
        }
        if 'node' in case:
            entry['node'] = encode_kwargs(case['node'])
        if 'ops_result' in case:
            entry['ops_result'] = [encode_kwargs(kwargs) for kwargs in case['ops_result']]
            entry['ops_list'] = case['ops_list']
        methods.append({'input': entry, 'output': outcome})

    # Each query text is stored one time, and a call names it by its index, to keep the file small.
    queries = []
    for method in methods:
        for call in method['output']['calls']:
            if 'query' in call:
                if call['query'] not in queries:
                    queries.append(call['query'])
                call['query'] = queries.index(call['query'])

    document = {
        'python': '.'.join(str(part) for part in sys.version_info[:3]),
        'source': 'graphiti_core/nodes.py',
        'seed': SEED,
        'queries': queries,
        'construct': construct,
        'records': records_out,
        'equality': equality,
        'copies': copies,
        'methods': methods,
    }
    text = json.dumps(document, ensure_ascii=True, separators=(',', ':')) + '\n'
    total = len(construct) + len(records_out) + len(equality) + len(copies) + len(methods)
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {total} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {total} cases, {len(text) // 1024} KB')
    return 0


if __name__ == '__main__':
    sys.exit(main())
