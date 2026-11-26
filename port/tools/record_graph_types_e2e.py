"""Records what Python does with each node type and edge type on a real Neo4j (plan 0004, layer 5, packet P5f).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_graph_types_e2e.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_graph_types_e2e.py --check

It saves the nine types of graphiti_core (EntityNode, EpisodicNode, CommunityNode, SagaNode, EntityEdge,
EpisodicEdge, CommunityEdge, HasEpisodeEdge, NextEpisodeEdge) to the database in two ways. The first way is the
save, get_by_uuid, get_by_uuids, get_by_group_ids, delete, delete_by_uuids, and delete_by_group_id methods of the
model classes. The second way is the same set of methods of the operations classes of Neo4jDriver
(driver.entity_node_ops and the others), with save_bulk. After each save it reads the stored form from the database
(the labels or the type, and the properties). After each read it records the values that Python loaded.

It also records:

- replay: the query text and the parameters of each save of the first way. The end-to-end test of the port runs
  these queries against its own group. This shows that the port loads what Python saved, in a database where no
  Python run happened (CI). The test replaces the group id and the first eight characters of each uuid.
- indexes: the indexes that build_indices_and_constraints creates, read from SHOW INDEXES after the first call and
  after the second call. The two reads must be equal.
- save_errors: the error of Neo4j for a save with a map as an attribute value, a node with no embedding, and an edge
  with no embedding.

It writes tests/Graphiti.Net.E2E.Tests/values/graph_types_e2e.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The tool needs a Neo4j at NEO4J_URI (default bolt://localhost:7687) with
NEO4J_USER and NEO4J_PASSWORD (the defaults of Neo4jSidecar.cs), Python 3.12, and the pinned upstream. It reads the
upstream folder from the environment variable GRAPHITI_UPSTREAM, then from .upstream/graphiti in the repository,
then from the main checkout when this is a git worktree.

The tool uses the group id e2e_py_fixture_p5f. It deletes the data of that group before it starts and after it ends.
It touches no other data and drops no index. It never calls delete_all_indexes.
"""
from __future__ import annotations

import asyncio
import json
import os
import re
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'tests' / 'Graphiti.Net.E2E.Tests' / 'values' / 'graph_types_e2e.json'


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

URI = os.environ.get('NEO4J_URI', 'bolt://localhost:7687')
USER = os.environ.get('NEO4J_USER', 'neo4j')
PASSWORD = os.environ.get('NEO4J_PASSWORD', 'password')

GROUP = 'e2e_py_fixture_p5f'
OTHER_GROUP = 'e2e_py_nobody_p5f'


def uid(n: int) -> str:
    """A fixed uuid. The test replaces the first eight characters and keeps the order of the uuids."""
    return f'00000000-0000-4000-8000-{n:012x}'


MISSING = uid(0xFFF)

UUIDS = {
    'A': uid(0xA1), 'B': uid(0xA2), 'E1': uid(0xB1), 'E2': uid(0xB2), 'C': uid(0xC1), 'S': uid(0xD1),
    'EE': uid(0xE1), 'EP': uid(0xE2), 'CE': uid(0xE3), 'HE': uid(0xE4), 'NE': uid(0xE5),
}

PLUS_0530 = timezone(timedelta(hours=5, minutes=30))
MINUS_0330 = timezone(timedelta(hours=-3, minutes=-30))
PLUS_1000 = timezone(timedelta(hours=10))

NODE_KINDS = ['EntityNode', 'EpisodicNode', 'CommunityNode', 'SagaNode']
EDGE_KINDS = ['EntityEdge', 'EpisodicEdge', 'CommunityEdge', 'HasEpisodeEdge', 'NextEpisodeEdge']
OPS_NAMES = {
    'EntityNode': 'entity_node_ops', 'EpisodicNode': 'episode_node_ops', 'CommunityNode': 'community_node_ops',
    'SagaNode': 'saga_node_ops', 'EntityEdge': 'entity_edge_ops', 'EpisodicEdge': 'episodic_edge_ops',
    'CommunityEdge': 'community_edge_ops', 'HasEpisodeEdge': 'has_episode_edge_ops',
    'NextEpisodeEdge': 'next_episode_edge_ops',
}


# ---------- encoding ----------

def encode(value):
    """A value as a tagged JSON value. An int stays an int and a float stays a float."""
    from neo4j import time as neo4j_time

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
    if isinstance(value, neo4j_time.DateTime):
        value = value.to_native()
    if isinstance(value, datetime):
        return {'type': 'datetime', 'value': value.isoformat()}
    if hasattr(value, 'value') and type(value).__name__ == 'EpisodeType':
        return {'type': 'enum', 'class': type(value).__name__, 'value': value.value}
    if isinstance(value, (list, tuple)):
        return {'type': 'list', 'items': [encode(item) for item in value]}
    if isinstance(value, dict):
        return {'type': 'dict', 'items': [[key, encode(value[key])] for key in sorted(value)]}
    raise TypeError(f'cannot encode {type(value).__name__}')


def encode_model(model) -> dict:
    """The fields of a model in the order of the class. The labels are sorted: Neo4j gives no order for them."""
    fields = {}
    for name in type(model).model_fields:
        value = getattr(model, name)
        if name == 'labels':
            value = sorted(value)
        fields[name] = encode(value)
    for name in sorted(getattr(model, '__pydantic_private__', None) or {}):
        fields[name] = encode(getattr(model, name))
    return {'class': type(model).__name__, 'fields': fields}


def encode_raw(labels_or_type, props, extra=None) -> dict:
    """The stored form. The bulk save of entity nodes stores a labels property, built from a set, so its order is open."""
    props = {key: (sorted(value) if key == 'labels' and isinstance(value, list) else value) for key, value in props.items()}
    out = {'props': encode(props)}
    out['kind'] = encode(sorted(labels_or_type) if isinstance(labels_or_type, list) else labels_or_type)
    if extra is not None:
        out['ends'] = extra
    return out


def error_of(error) -> dict:
    """A database error is its code. The class names and the texts of drivers differ. Other errors keep type and text."""
    code = getattr(error, 'code', None)
    if code is not None:
        return {'code': code}
    return {'type': type(error).__name__, 'text': str(error)}


# ---------- fixtures ----------

def build_fixtures():
    from graphiti_core.nodes import CommunityNode, EntityNode, EpisodeType, EpisodicNode, SagaNode
    from graphiti_core.edges import (
        CommunityEdge,
        EntityEdge,
        EpisodicEdge,
        HasEpisodeEdge,
        NextEpisodeEdge,
    )

    nodes = {
        'A': EntityNode(
            uuid=UUIDS['A'],
            name='Zoë 東京 😀',
            group_id=GROUP,
            labels=['Person', 'Employee'],
            created_at=datetime(2026, 3, 4, 5, 6, 7, 123456, tzinfo=PLUS_0530),
            name_embedding=[0.1, -0.25, 0.5, 1.0],
            summary='Línea uno\nlínea 二 ☃',
            attributes={
                'age': 42,
                'height': 1.75,
                'active': True,
                'tags': ['a', 'é'],
                'born': datetime(1990, 7, 8, 9, 10, 11, 500000, tzinfo=MINUS_0330),
                'note': 'ünï 😀',
                'missing': None,
                'big': 2**40,
                'ratio': 2.0,
            },
        ),
        'B': EntityNode(
            uuid=UUIDS['B'],
            name='Bob Ünïcode',
            group_id=GROUP,
            labels=['Place'],
            created_at=datetime(2026, 3, 5, 6, 7, 8, 1, tzinfo=PLUS_1000),
            name_embedding=[-0.5, 0.75],
            summary='Segundo',
            attributes={'country': 'Perú', 'visits': 3},
        ),
        'E1': EpisodicNode(
            uuid=UUIDS['E1'],
            name='épisode 1',
            group_id=GROUP,
            labels=['Custom'],
            created_at=datetime(2026, 4, 5, 6, 7, 8, 999999, tzinfo=MINUS_0330),
            source=EpisodeType.json,
            source_description='Zoë source 😀',
            content='{"quoté": "日本", "n": 1}',
            valid_at=datetime(2026, 1, 2, 3, 4, 5, 6, tzinfo=PLUS_0530),
            entity_edges=['edge-1', 'édge-2'],
            episode_metadata={'k': 'v', 'n': 1},
        ),
        'E2': EpisodicNode(
            uuid=UUIDS['E2'],
            name='episode 2',
            group_id=GROUP,
            created_at=datetime(2026, 4, 6, 6, 7, 8, tzinfo=timezone.utc),
            source=EpisodeType.message,
            source_description='chat',
            content='Hola: mundo',
            valid_at=datetime(2026, 1, 3, 3, 4, 5, tzinfo=timezone.utc),
        ),
        'C': CommunityNode(
            uuid=UUIDS['C'],
            name='Comunidad ñ 東',
            group_id=GROUP,
            created_at=datetime(2026, 5, 6, 7, 8, 9, 10, tzinfo=PLUS_1000),
            name_embedding=[0.5, 0.25, -0.75],
            summary='resumen 日本 ✓',
        ),
        'S': SagaNode(
            uuid=UUIDS['S'],
            name='Saga ü',
            group_id=GROUP,
            created_at=datetime(2026, 6, 7, 8, 9, 10, 11, tzinfo=PLUS_0530),
            summary='saga summary é',
            first_episode_uuid=UUIDS['E1'],
            last_episode_uuid=UUIDS['E2'],
            last_summarized_at=datetime(2026, 6, 8, 9, 10, 11, 12, tzinfo=MINUS_0330),
            last_summarized_episode_valid_at=datetime(2026, 6, 9, 10, 11, 12, 13, tzinfo=PLUS_1000),
        ),
    }
    # The four counters are private attributes. Python does not store them.
    nodes['S']._summary_episodes_fetched = 7
    nodes['S']._summary_episodes_selected = 5
    nodes['S']._summary_episodes_skipped = 2
    nodes['S']._summary_selected_episode_bytes = 4096

    edges = {
        'EE': EntityEdge(
            uuid=UUIDS['EE'],
            group_id=GROUP,
            source_node_uuid=UUIDS['A'],
            target_node_uuid=UUIDS['B'],
            created_at=datetime(2026, 7, 8, 9, 10, 11, 123456, tzinfo=PLUS_0530),
            name='WORKS_AT',
            fact='Zoë trabaja en 東京 😀',
            fact_embedding=[0.3, -0.6, 0.9],
            episodes=[UUIDS['E1'], 'ép-2'],
            expired_at=datetime(2026, 8, 9, 10, 11, 12, tzinfo=MINUS_0330),
            valid_at=datetime(2026, 1, 1, 0, 0, 0, tzinfo=timezone.utc),
            invalid_at=datetime(2027, 1, 1, 0, 0, 0, 1, tzinfo=PLUS_1000),
            reference_time=datetime(2026, 2, 3, 4, 5, 6, 7),
            attributes={'weight': 0.5, 'since': 2020, 'tags': ['x', 'ý'], 'flag': False, 'unset': None},
        ),
        'EP': EpisodicEdge(
            uuid=UUIDS['EP'],
            group_id=GROUP,
            source_node_uuid=UUIDS['E1'],
            target_node_uuid=UUIDS['A'],
            created_at=datetime(2026, 7, 9, 9, 10, 11, 1, tzinfo=PLUS_0530),
        ),
        'CE': CommunityEdge(
            uuid=UUIDS['CE'],
            group_id=GROUP,
            source_node_uuid=UUIDS['C'],
            target_node_uuid=UUIDS['A'],
            created_at=datetime(2026, 7, 10, 9, 10, 11, 2, tzinfo=MINUS_0330),
        ),
        'HE': HasEpisodeEdge(
            uuid=UUIDS['HE'],
            group_id=GROUP,
            source_node_uuid=UUIDS['S'],
            target_node_uuid=UUIDS['E1'],
            created_at=datetime(2026, 7, 11, 9, 10, 11, 3, tzinfo=PLUS_1000),
        ),
        'NE': NextEpisodeEdge(
            uuid=UUIDS['NE'],
            group_id=GROUP,
            source_node_uuid=UUIDS['E1'],
            target_node_uuid=UUIDS['E2'],
            created_at=datetime(2026, 7, 12, 9, 10, 11, 4, tzinfo=timezone.utc),
        ),
    }
    return nodes, edges


KIND_OF = {
    'A': 'EntityNode', 'B': 'EntityNode', 'E1': 'EpisodicNode', 'E2': 'EpisodicNode', 'C': 'CommunityNode',
    'S': 'SagaNode', 'EE': 'EntityEdge', 'EP': 'EpisodicEdge', 'CE': 'CommunityEdge', 'HE': 'HasEpisodeEdge',
    'NE': 'NextEpisodeEdge',
}
SAVE_ORDER_NODES = ['A', 'B', 'E1', 'E2', 'C', 'S']
SAVE_ORDER_EDGES = ['EE', 'EP', 'CE', 'HE', 'NE']


def keys_of(kind: str) -> list[str]:
    return [key for key in list(SAVE_ORDER_NODES) + list(SAVE_ORDER_EDGES) if KIND_OF[key] == kind]


# ---------- the recorder ----------

class Recorder:
    def __init__(self, driver, classes):
        self.driver = driver
        self.classes = classes
        self.log: list[dict] = []
        self.step = ''
        self.logging = False
        original = driver.execute_query

        async def logged(query, **kwargs):
            if self.logging:
                self.log.append({
                    'step': self.step,
                    'query': query,
                    'params': [[key, encode(value)] for key, value in kwargs.items()],
                })
            return await original(query, **kwargs)

        driver.execute_query = logged

    # --- reading the stored form ---

    async def raw(self, key: str):
        kind = KIND_OF[key]
        uuid = UUIDS[key]
        if kind in NODE_KINDS:
            records, _, _ = await self.driver.execute_query(
                'MATCH (n {uuid: $u}) RETURN labels(n) AS labels, properties(n) AS props', u=uuid)
            if not records:
                return None
            return encode_raw(list(records[0]['labels']), dict(records[0]['props']))
        records, _, _ = await self.driver.execute_query(
            'MATCH (a)-[e {uuid: $u}]->(b) RETURN type(e) AS t, properties(e) AS props, a.uuid AS s, b.uuid AS d', u=uuid)
        if not records:
            return None
        return encode_raw(records[0]['t'], dict(records[0]['props']), [records[0]['s'], records[0]['d']])

    async def exists(self, keys: list[str]) -> list:
        return [{'key': key, 'exists': (await self.raw(key)) is not None} for key in keys]

    async def raw_all(self, kind: str) -> list:
        return [{'key': key, 'raw': await self.raw(key)} for key in keys_of(kind)]

    # --- saves ---

    async def model_saves(self, models, keys):
        for key in keys:
            self.step = 'save:' + key
            self.logging = True
            await models[key].save(self.driver)
            self.logging = False

    async def model_saves_nolog(self, models, keys):
        for key in keys:
            await models[key].save(self.driver)

    async def ops_saves(self, models, keys):
        for key in keys:
            ops = getattr(self.driver, OPS_NAMES[KIND_OF[key]])
            await ops.save(self.driver, models[key])

    async def bulk(self, models, kinds):
        out = []
        for kind in kinds:
            of_kind = [models[key] for key in keys_of(kind)]
            if not hasattr(getattr(self.driver, OPS_NAMES[kind]), 'save_bulk'):
                # The operations class of CommunityEdge has no save_bulk. The kind then saves one by one.
                for model in of_kind:
                    await getattr(self.driver, OPS_NAMES[kind]).save(self.driver, model)
                out.append({'kind': kind, 'error': None, 'no_save_bulk': True})
                continue
            error = await self.attempt(getattr(self.driver, OPS_NAMES[kind]).save_bulk(self.driver, of_kind))
            out.append({'kind': kind, 'error': error})
        return out

    @staticmethod
    async def attempt(awaitable):
        try:
            await awaitable
        except Exception as error:  # noqa: BLE001 - the recording keeps any error
            return error_of(error)
        return None

    # --- reads ---

    async def read_model(self, kind: str):
        cls = self.classes[kind]
        keys = keys_of(kind)
        uuids = [UUIDS[key] for key in keys]
        out = {'raw': await self.raw_all(kind)}
        out['by_uuid'] = [{'key': key, 'model': encode_model(await cls.get_by_uuid(self.driver, UUIDS[key]))} for key in keys]
        found = await cls.get_by_uuids(self.driver, uuids + [MISSING])
        out['by_uuids'] = [encode_model(m) for m in sorted(found, key=lambda m: m.uuid)]
        grouped = await cls.get_by_group_ids(self.driver, [GROUP, OTHER_GROUP])
        out['by_group_ids'] = [encode_model(m) for m in grouped]
        out['missing'] = await self.missing(cls.get_by_uuid(self.driver, MISSING))
        if kind == 'EntityNode':
            loaded = []
            for key in keys:
                node = await cls.get_by_uuid(self.driver, UUIDS[key])
                await node.load_name_embedding(self.driver)
                loaded.append({'key': key, 'embedding': encode(node.name_embedding)})
            out['embeddings'] = loaded
            with_embeddings = await cls.get_by_group_ids(self.driver, [GROUP], with_embeddings=True)
            out['by_group_ids_with_embeddings'] = [encode_model(m) for m in with_embeddings]
        if kind == 'CommunityNode':
            loaded = []
            for key in keys:
                node = await cls.get_by_uuid(self.driver, UUIDS[key])
                node.name_embedding = None
                await node.load_name_embedding(self.driver)
                loaded.append({'key': key, 'embedding': encode(node.name_embedding)})
            out['embeddings'] = loaded
        if kind == 'EntityEdge':
            loaded = []
            for key in keys:
                edge = await cls.get_by_uuid(self.driver, UUIDS[key])
                await edge.load_fact_embedding(self.driver)
                loaded.append({'key': key, 'embedding': encode(edge.fact_embedding)})
            out['embeddings'] = loaded
        return out

    async def read_ops(self, kind: str):
        ops = getattr(self.driver, OPS_NAMES[kind])
        keys = keys_of(kind)
        uuids = [UUIDS[key] for key in keys]
        out = {'raw': await self.raw_all(kind)}
        out['by_uuid'] = [{'key': key, 'model': encode_model(await ops.get_by_uuid(self.driver, UUIDS[key]))} for key in keys]
        found = await ops.get_by_uuids(self.driver, uuids + [MISSING])
        out['by_uuids'] = [encode_model(m) for m in sorted(found, key=lambda m: m.uuid)]
        grouped = await ops.get_by_group_ids(self.driver, [GROUP, OTHER_GROUP])
        out['by_group_ids'] = [encode_model(m) for m in grouped]
        out['missing'] = await self.missing(ops.get_by_uuid(self.driver, MISSING))
        if kind in ('EntityNode', 'EntityEdge'):
            loaded = []
            for key in keys:
                model = await ops.get_by_uuid(self.driver, UUIDS[key])
                await ops.load_embeddings(self.driver, model)
                value = model.name_embedding if kind == 'EntityNode' else model.fact_embedding
                loaded.append({'key': key, 'embedding': encode(value)})
            out['embeddings'] = loaded
            models = [await ops.get_by_uuid(self.driver, UUIDS[key]) for key in keys]
            await ops.load_embeddings_bulk(self.driver, models)
            out['embeddings_bulk'] = [
                {'key': key, 'embedding': encode(m.name_embedding if kind == 'EntityNode' else m.fact_embedding)}
                for key, m in zip(keys, models)
            ]
        if kind == 'CommunityNode':
            loaded = []
            for key in keys:
                model = await ops.get_by_uuid(self.driver, UUIDS[key])
                model.name_embedding = None
                await ops.load_name_embedding(self.driver, model)
                loaded.append({'key': key, 'embedding': encode(model.name_embedding)})
            out['embeddings'] = loaded
        return out

    @staticmethod
    async def missing(awaitable):
        try:
            await awaitable
        except Exception as error:  # noqa: BLE001 - the recording keeps any error
            return error_of(error)
        return None

    async def cleanup(self):
        for label in ['Entity', 'Episodic', 'Community', 'Saga']:
            await self.driver.execute_query(
                f'MATCH (n:{label}) WHERE n.group_id IN $groups DETACH DELETE n', groups=[GROUP, OTHER_GROUP])


async def record_flow(rec: Recorder, nodes, edges):
    models = {**nodes, **edges}
    out = {'model': {}, 'ops': {}}
    classes = rec.classes
    driver = rec.driver

    def ops_of(key):
        return getattr(driver, OPS_NAMES[KIND_OF[key]])

    def uuids_of(kind):
        return [UUIDS[key] for key in keys_of(kind)]

    async def clean_nodes():
        for key in SAVE_ORDER_NODES:
            await ops_of(key).delete(driver, models[key])

    # ----- the methods of the model classes -----
    model = out['model']
    await rec.model_saves(models, SAVE_ORDER_NODES)
    await rec.model_saves(models, SAVE_ORDER_EDGES)
    model['read'] = {kind: await rec.read_model(kind) for kind in NODE_KINDS + EDGE_KINDS}

    # delete of each edge class
    for key in SAVE_ORDER_EDGES:
        await models[key].delete(driver)
    model['after_edge_delete'] = await rec.exists(SAVE_ORDER_EDGES)

    # delete_by_uuids of the base class. It removes the types MENTIONS, RELATES_TO, and HAS_MEMBER only.
    await rec.model_saves_nolog(models, SAVE_ORDER_EDGES)
    await classes['EntityEdge'].delete_by_uuids(driver, [UUIDS[key] for key in SAVE_ORDER_EDGES])
    model['after_edge_delete_by_uuids'] = await rec.exists(SAVE_ORDER_EDGES)
    for key in ['HE', 'NE']:
        await models[key].delete(driver)

    # delete of each node, and the edges of the node go with it
    await rec.model_saves_nolog(models, SAVE_ORDER_EDGES)
    for key in SAVE_ORDER_NODES:
        await models[key].delete(driver)
    model['after_node_delete'] = await rec.exists(SAVE_ORDER_NODES + SAVE_ORDER_EDGES)

    # delete_by_uuids and delete_by_group_id of the node classes. They remove the labels Entity, Episodic, Community.
    await rec.model_saves_nolog(models, SAVE_ORDER_NODES)
    await classes['EntityNode'].delete_by_uuids(driver, [UUIDS[key] for key in SAVE_ORDER_NODES])
    model['after_node_delete_by_uuids'] = await rec.exists(SAVE_ORDER_NODES)
    await rec.model_saves_nolog(models, SAVE_ORDER_NODES)
    await classes['EntityNode'].delete_by_group_id(driver, GROUP)
    model['after_node_delete_by_group_id'] = await rec.exists(SAVE_ORDER_NODES)
    await models['S'].delete(driver)
    model['after_saga_delete'] = await rec.exists(['S'])

    # ----- the methods of the operations classes -----
    ops = out['ops']
    ops['save_errors'] = []
    for key in SAVE_ORDER_NODES:
        ops['save_errors'].append({'key': key, 'error': await rec.attempt(ops_of(key).save(driver, models[key]))})
    await models['S'].save(driver)  # The saga save of the operations class fails (upstream defect U2).
    for key in SAVE_ORDER_EDGES:
        ops['save_errors'].append({'key': key, 'error': await rec.attempt(ops_of(key).save(driver, models[key]))})
    ops['read'] = {kind: await rec.read_ops(kind) for kind in NODE_KINDS + EDGE_KINDS}

    for key in SAVE_ORDER_EDGES:
        await ops_of(key).delete(driver, models[key])
    ops['after_edge_delete'] = await rec.exists(SAVE_ORDER_EDGES)

    ops['bulk_edge_errors'] = await rec.bulk(models, EDGE_KINDS)
    ops['bulk_edges'] = {kind: await rec.read_ops(kind) for kind in EDGE_KINDS}
    ops['delete_by_uuids_edge_errors'] = [
        {'kind': kind, 'error': await rec.attempt(getattr(driver, OPS_NAMES[kind]).delete_by_uuids(driver, uuids_of(kind)))}
        for kind in EDGE_KINDS
    ]
    ops['after_edge_delete_by_uuids'] = await rec.exists(SAVE_ORDER_EDGES)

    for key in SAVE_ORDER_NODES:
        await ops_of(key).delete(driver, models[key])
    ops['after_node_delete'] = await rec.exists(SAVE_ORDER_NODES)

    ops['bulk_node_errors'] = await rec.bulk(models, NODE_KINDS)
    await models['S'].save(driver)
    ops['bulk_nodes'] = {kind: await rec.read_ops(kind) for kind in NODE_KINDS}
    ops['delete_by_uuids_node_errors'] = [
        {'kind': kind, 'error': await rec.attempt(getattr(driver, OPS_NAMES[kind]).delete_by_uuids(driver, uuids_of(kind)))}
        for kind in NODE_KINDS
    ]
    ops['after_node_delete_by_uuids'] = await rec.exists(SAVE_ORDER_NODES)
    await clean_nodes()

    await rec.bulk(models, NODE_KINDS)
    await models['S'].save(driver)
    ops['delete_by_group_id_node_errors'] = [
        {'kind': kind, 'error': await rec.attempt(getattr(driver, OPS_NAMES[kind]).delete_by_group_id(driver, GROUP))}
        for kind in NODE_KINDS
    ]
    ops['after_node_delete_by_group_id'] = await rec.exists(SAVE_ORDER_NODES)
    await clean_nodes()
    return out


async def record_indexes(driver):
    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.graph_queries import get_fulltext_indices, get_range_indices

    queries = get_range_indices(GraphProvider.NEO4J) + get_fulltext_indices(GraphProvider.NEO4J)
    names = sorted(re.search(r'CREATE (?:FULLTEXT )?INDEX (\w+)', q).group(1) for q in queries)

    async def read():
        await driver.execute_query('CALL db.awaitIndexes(120)')
        records, _, _ = await driver.execute_query(
            'SHOW INDEXES YIELD name, type, entityType, labelsOrTypes, properties, state '
            'WHERE name IN $names RETURN name, type, entityType, labelsOrTypes, properties, state ORDER BY name',
            names=names)
        return [
            {
                'name': r['name'], 'type': r['type'], 'entityType': r['entityType'],
                'labelsOrTypes': list(r['labelsOrTypes']), 'properties': list(r['properties']), 'state': r['state'],
            }
            for r in records
        ]

    await driver.build_indices_and_constraints()
    first = await read()
    await driver.build_indices_and_constraints()
    second = await read()
    if first != second:
        raise RuntimeError('the indexes differ after the second build')
    return {'names': names, 'after_first_build': first, 'after_second_build': second}


async def record_save_errors(driver):
    """Saves that Neo4j refuses. The port must refuse them with the same code (upstream defects U7 and the map value)."""
    from graphiti_core.edges import EntityEdge
    from graphiti_core.nodes import EntityNode

    when = datetime(2026, 1, 1, tzinfo=timezone.utc)
    out = {}
    out['map_attribute'] = await Recorder.attempt(EntityNode(
        uuid=uid(0xF1), name='nested', group_id=GROUP, name_embedding=[0.5], attributes={'inner': {'a': 1}}).save(driver))
    out['node_without_embedding'] = await Recorder.attempt(EntityNode(
        uuid=uid(0xF2), name='plain', group_id=GROUP).save(driver))
    first = EntityNode(uuid=uid(0xF3), name='one', group_id=GROUP, name_embedding=[0.5])
    second = EntityNode(uuid=uid(0xF4), name='two', group_id=GROUP, name_embedding=[0.5])
    await first.save(driver)
    await second.save(driver)
    out['edge_without_embedding'] = await Recorder.attempt(EntityEdge(
        uuid=uid(0xF5), group_id=GROUP, source_node_uuid=first.uuid, target_node_uuid=second.uuid, created_at=when,
        name='R', fact='f').save(driver))
    return out


async def run():
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core.driver.neo4j_driver import Neo4jDriver
    from graphiti_core.edges import (
        CommunityEdge,
        EntityEdge,
        EpisodicEdge,
        HasEpisodeEdge,
        NextEpisodeEdge,
    )
    from graphiti_core.nodes import CommunityNode, EntityNode, EpisodicNode, SagaNode
    import logging

    logging.getLogger('graphiti_core').setLevel(logging.CRITICAL)
    logging.getLogger('neo4j').setLevel(logging.CRITICAL)

    classes = {
        'EntityNode': EntityNode, 'EpisodicNode': EpisodicNode, 'CommunityNode': CommunityNode, 'SagaNode': SagaNode,
        'EntityEdge': EntityEdge, 'EpisodicEdge': EpisodicEdge, 'CommunityEdge': CommunityEdge,
        'HasEpisodeEdge': HasEpisodeEdge, 'NextEpisodeEdge': NextEpisodeEdge,
    }
    driver = Neo4jDriver(URI, USER, PASSWORD)
    rec = Recorder(driver, classes)
    try:
        await rec.cleanup()
        nodes, edges = build_fixtures()
        data = {
            'meta': {
                'group': GROUP,
                'other_group': OTHER_GROUP,
                'missing_uuid': MISSING,
                'uuids': UUIDS,
                'kinds': {key: KIND_OF[key] for key in KIND_OF},
            },
            'inputs': {key: encode_model(model) for key, model in {**nodes, **edges}.items()},
        }
        flow = await record_flow(rec, nodes, edges)
        data['replay'] = rec.log
        data['model'] = flow['model']
        data['ops'] = flow['ops']
        data['save_errors'] = await record_save_errors(driver)
        data['indexes'] = await record_indexes(driver)
    finally:
        await rec.cleanup()
        await driver.close()
    return data


def render(data) -> str:
    return json.dumps(data, ensure_ascii=False, indent=1) + '\n'


def main() -> int:
    data = asyncio.run(run())
    text = render(data)
    if '--check' in sys.argv[1:]:
        current = OUT.read_text(encoding='utf-8') if OUT.exists() else None
        if current != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run the tool without --check.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)} is current')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'wrote {OUT.relative_to(ROOT)} ({len(text)} bytes)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
