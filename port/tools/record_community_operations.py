"""Records results of graphiti_core/utils/maintenance/community_operations.py from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_community_operations.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_community_operations.py --check

Plan 0004, layer 7, packet P7d. The tool has two parts.

The sync part calls label_propagation with generated projections from a fixed seed, and builds Neighbor from generated
data. Upstream defect U11: label_propagation does not end for some inputs. The tool runs a copy of the loop with a limit
of rounds first, and records only a projection whose loop ends within the limit.

The async part runs get_community_clusters, summarize_pair, generate_summary_description, build_community,
build_communities, remove_communities, determine_entity_community, and update_community with fakes:

- The LLM client replaces generate_response. It records the prompt name, the response model name, the model size,
  max_tokens, group_id, attribute_extraction, and the role, the SHA-256, and the length of each message. It answers with
  the first reply of the case for the prompt name whose needle is in the last message and whose count of uses is not
  spent. A reply {"$raise": text} raises RuntimeError. With no such reply, summarize_pair gets the summary
  "P<8 hex digits of the SHA-256 of the last message>." and summary_description gets the description "D<8 hex digits>",
  so that each answer depends on its input.
- The embedder gives the vector [i, 1.0] to the i-th distinct text of the case.
- The driver answers execute_query with the records of the first answer of the case whose needle is in the query text
  and whose uuid, if it has one, is the value of the parameter uuid or entity_uuid. Its graph operations interface
  answers node_get_by_group_ids and node_get_by_uuids from the stored nodes of the case, in their order. It implements
  get_community_clusters, remove_communities, and determine_entity_community only for a case that names them in "ops".
  A case with "graph_ops" has a GraphMaintenanceOperations with the default remove_communities.
- Each fake counts an attempt, yields once (asyncio.sleep(0)), and then records its call. The calls and the log lines
  are one list. Each case keeps the largest count of fake calls in flight.
- A case with "defaults" calls the function with each optional argument left out (R25).

For the cases of CANCEL_CASES the tool also runs the case again with a cancel (the form of record_node_operations.py): once
for k = -1, where the task is canceled before its first await, and once for each call k except the last, where the fake
that makes call k records it and then cancels the task of the call (the port cancels a token source). The case keeps the
count of the attempts of the run, the count of the recorded calls, and the events after call k, sorted as the text above
says. Python lets a CancelledError through each `except Exception`, so the port must make the same attempts and write
the same log lines (R25). A point where the port makes one more attempt is the late cancel of GAPS F33, and the test
names it.

A community summary comes from pairs of summaries in rounds, and the calls of one round run at the same time. Their
order is not a contract (GAPS G19). So both sides sort each run of consecutive summarize_pair calls in the event list by
the JSON text of the call. The order of the rounds stays.

Fixed values on both sides: community_operations.utc_now gives FIXED_NOW, and the C# test gives the port a clock with
the same time. A uuid4 that the code makes is random: the test replaces each one, in the order of its first appearance
in the case output, by "<new:N>". A timed log line has its milliseconds replaced by "<ms>".

It writes tests/Port.Evaluation/values/community_operations.json. --check writes nothing and exits 1 if the file differs
from what the run would write. The tool needs Python 3.12 and the pinned upstream in the folder of the variable
GRAPHITI_UPSTREAM, or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import hashlib
import json
import logging
import os
import random
import re
import sys
import warnings
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'community_operations.json'

SEED = 20261129
FIXED_NOW = datetime(2026, 3, 4, 5, 6, 7, 891011, tzinfo=timezone.utc)
UUID4 = re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}')
MS = re.compile(r'in [0-9.e+-]+ ms')
PAIR = 'summarize_nodes.summarize_pair'
DESCRIPTION = 'summarize_nodes.summary_description'

rng = random.Random(SEED)
events: list[dict] = []
state: dict = {}

# A cancel leaves the coroutines of a gather that never started. Python warns about each one, and that is no result.
warnings.filterwarnings('ignore', message='coroutine .* was never awaited', category=RuntimeWarning)

# The cases that the tool runs again with a cancel. The C# test names the same cases.
CANCEL_CASES = [0, 1, 7, 12, 19, 20, 25, 28, 32, 35, 42]


def dt(value):
    return None if value is None else {'$dt': value.isoformat()}


def encode(value):
    if isinstance(value, datetime):
        return dt(value)
    if isinstance(value, dict):
        return {str(k): encode(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [encode(v) for v in value]
    return value


def decode(value):
    if isinstance(value, dict):
        if set(value) == {'$dt'}:
            return datetime.fromisoformat(value['$dt'])
        return {k: decode(v) for k, v in value.items()}
    if isinstance(value, list):
        return [decode(v) for v in value]
    return value


def node_view(node) -> dict:
    return {
        'uuid': node.uuid, 'name': node.name, 'group_id': node.group_id, 'labels': list(node.labels),
        'created_at': dt(node.created_at), 'summary': node.summary,
    }


def community_view(node) -> dict:
    return {
        'uuid': node.uuid, 'name': node.name, 'group_id': node.group_id, 'labels': list(node.labels),
        'created_at': dt(node.created_at), 'summary': node.summary, 'name_embedding': node.name_embedding,
    }


def edge_view(edge) -> dict:
    return {
        'uuid': edge.uuid, 'group_id': edge.group_id, 'source_node_uuid': edge.source_node_uuid,
        'target_node_uuid': edge.target_node_uuid, 'created_at': dt(edge.created_at),
    }


def placeholders(value, known: dict):
    """Replaces each uuid4 that the case input does not hold by <new:N>, in the order of first appearance."""
    if isinstance(value, dict):
        return {k: placeholders(v, known) for k, v in value.items()}
    if isinstance(value, list):
        return [placeholders(v, known) for v in value]
    if isinstance(value, str):
        def repl(m):
            if m.group(0) not in known:
                known[m.group(0)] = f'<new:{len(known)}>'
            return known[m.group(0)]

        return MS.sub('in <ms> ms', UUID4.sub(repl, value))
    return value


def sort_pair_runs(items: list[dict]) -> list[dict]:
    """Sorts each run of consecutive summarize_pair calls by its JSON text (see the module docstring)."""
    out: list[dict] = []
    run: list[dict] = []
    for item in items + [None]:
        if item is not None and item.get('kind') == 'llm' and item.get('prompt_name') == PAIR:
            run.append(item)
            continue
        out.extend(sorted(run, key=json.dumps))
        run = []
        if item is not None:
            out.append(item)
    return out


# ---------------------------------------------------------------------------------------------------------------------
# Sync part

records: list[dict] = []


def ends(projection: dict, limit: int = 200) -> bool:
    """The loop of label_propagation with a limit of rounds: True if it ends within the limit."""
    community_map = {uuid: i for i, uuid in enumerate(projection.keys())}
    for _ in range(limit):
        no_change = True
        new_map = {}
        for uuid, neighbors in projection.items():
            curr = community_map[uuid]
            candidates = defaultdict(int)
            for neighbor in neighbors:
                candidates[community_map[neighbor['node_uuid']]] += neighbor['edge_count']
            lst = sorted(((c, k) for k, c in candidates.items()), reverse=True)
            rank, cand = lst[0] if lst else (0, -1)
            new = cand if cand != -1 and rank > 1 else max(cand, curr)
            new_map[uuid] = new
            if new != curr:
                no_change = False
        if no_change:
            return True
        community_map = new_map
    return False


def record_sync(co) -> None:
    count = 0
    while count < 150:
        size = rng.randint(0, 7)
        names = [f'n{i}' for i in range(size)]
        rng.shuffle(names)
        projection = {}
        for name in names:
            neighbors = []
            for _ in range(rng.randint(0, 4)):
                if not names:
                    break
                neighbors.append({'node_uuid': rng.choice(names), 'edge_count': rng.choice([0, 1, 1, 2, 3, -1])})
            projection[name] = neighbors
        if not ends(projection):
            continue
        count += 1
        result = co.label_propagation({k: [co.Neighbor(**n) for n in v] for k, v in projection.items()})
        records.append({'f': 'label_propagation', 'in': {'projection': projection}, 'out': {'ok': result}})
    # A neighbor that is not a key of the projection
    projection = {'a': [{'node_uuid': 'zzz', 'edge_count': 1}]}
    try:
        co.label_propagation({k: [co.Neighbor(**n) for n in v] for k, v in projection.items()})
        out = {'ok': None}
    except KeyError as error:
        out = {'error': {'type': 'KeyError', 'text': str(error)}}
    records.append({'f': 'label_propagation', 'in': {'projection': projection}, 'out': out})
    for data in [{'node_uuid': 'a', 'edge_count': 2}, {'node_uuid': 'a', 'edge_count': '3'},
                 {'node_uuid': 'a', 'edge_count': 2.0}, {'node_uuid': 'a', 'edge_count': 'x'}, {'node_uuid': 'a'},
                 {'node_uuid': 5, 'edge_count': 1}, {'node_uuid': 'a', 'edge_count': 1, 'extra': True}]:
        try:
            out = {'ok': co.Neighbor(**data).model_dump()}
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            out = {'error': {'type': type(error).__name__, 'text': str(error).splitlines()[0]}}
        records.append({'f': 'Neighbor', 'in': {'data': data}, 'out': out})


# ---------------------------------------------------------------------------------------------------------------------
# Async scenarios

T0 = datetime(2024, 1, 1, tzinfo=timezone.utc)


def node(uuid: str, summary: str, group_id: str = 'g') -> dict:
    return {'uuid': uuid, 'name': 'N' + uuid, 'group_id': group_id, 'labels': ['Entity'], 'created_at': dt(T0),
            'summary': summary}


def community(uuid: str, summary: str = 'Old summary.', group_id: str = 'g', embedding=None) -> dict:
    return {'uuid': uuid, 'name': 'C' + uuid, 'group_id': group_id, 'created_at': dt(T0), 'name_embedding': embedding,
            'summary': summary}


def answer(needle: str, rows: list, uuid: str | None = None) -> dict:
    item = {'needle': needle, 'records': rows}
    if uuid is not None:
        item['uuid'] = uuid
    return item


def reply(prompt: str, needle: str, value, times=None) -> dict:
    item = {'prompt': prompt, 'needle': needle, 'reply': value}
    if times is not None:
        item['times'] = times
    return item


NODES = [node('n1', 'Ana is an engineer.'), node('n2', 'Bob runs a bakery.'), node('n3', 'Acme makes robots.'),
         node('n4', 'Paris is a city.'), node('n5', 'Zed is a cat.'), node('m1', 'Mia plays chess.', 'h'),
         node('m2', 'Max sings.', 'h')]
GROUPS = answer('collect(DISTINCT n.group_id) AS group_ids', [{'group_ids': ['g', 'h']}])
PROJECTION = [
    answer('RELATES_TO', [{'uuid': 'n2', 'count': 2}, {'uuid': 'n3', 'count': 1}], 'n1'),
    answer('RELATES_TO', [{'uuid': 'n1', 'count': 1}], 'n2'),
    answer('RELATES_TO', [{'uuid': 'n1', 'count': 1}], 'n3'),
    answer('RELATES_TO', [{'uuid': 'n5', 'count': 3}], 'n4'),
    answer('RELATES_TO', [{'uuid': 'n4', 'count': 1}], 'n5'),
    answer('RELATES_TO', [{'uuid': 'm2', 'count': 1}], 'm1'),
]
MEMBER = 'MATCH (c:Community)-[:HAS_MEMBER]->(n:Entity'
NEIGHBOR = 'MATCH (c:Community)-[:HAS_MEMBER]->(m:Entity)'


def cases() -> list[dict]:
    out = []

    def add(fn, **kw):
        case = {'fn': fn, 'stored_nodes': NODES, 'answers': [], 'replies': [], 'ops': [], 'graph_ops': False,
                'max_coroutines': None, 'routing': False}
        case.update(kw)
        out.append(case)

    # get_community_clusters
    add('get_community_clusters', group_ids=None, answers=[GROUPS] + PROJECTION)
    add('get_community_clusters', group_ids=['g'], answers=PROJECTION, max_coroutines=1)
    add('get_community_clusters', group_ids=['h', 'zz'], answers=PROJECTION)
    add('get_community_clusters', group_ids=None, answers=[answer('collect(DISTINCT', [])])
    add('get_community_clusters', group_ids=[])
    add('get_community_clusters', group_ids=['g'], ops=['get_community_clusters'], clusters=[['n1', 'n2'], ['n5']])
    add('get_community_clusters', group_ids=['g'], answers=[answer('RELATES_TO', [{'uuid': 'n2', 'count': 'x'}], 'n1')])
    # summarize_pair and generate_summary_description
    add('summarize_pair', pair=['Ana is an engineer.', 'Bob runs a bakery.'])
    add('summarize_pair', pair=['A', 'B'], routing=True)
    add('summarize_pair', pair=['A', 'B'], replies=[reply(PAIR, '', {'other': 1})])
    add('summarize_pair', pair=['A', 'B'], replies=[reply(PAIR, '', {'summary': 'First sentence. ' * 70})])
    add('summarize_pair', pair=['A', 'B'], replies=[reply(PAIR, '', {'summary': 'x' * 1200})])
    add('generate_summary_description', summary='A summary of Ana.')
    add('generate_summary_description', summary='A summary of Ana.', replies=[reply(DESCRIPTION, '', {})])
    add('generate_summary_description', summary='A summary.', replies=[reply(DESCRIPTION, '', {'$raise': 'llm down'})])
    # build_community: clusters of each size, a concurrency limit, routing, and failures
    for size in (1, 2, 3, 5, 8):
        add('build_community', cluster=[node(f'c{i}', f'Summary {i}.') for i in range(size)])
    add('build_community', cluster=[node(f'c{i}', f'Summary {i}.') for i in range(6)], max_coroutines=1)
    add('build_community', cluster=[node(f'c{i}', f'Summary {i}.') for i in range(4)], routing=True)
    add('build_community', cluster=[node(f'c{i}', f'Summary {i}.') for i in range(3)],
        replies=[reply(PAIR, 'Summary 0.', {'summary': 'Long. ' * 300}), reply(DESCRIPTION, '', {'description': ''})])
    add('build_community', cluster=[node(f'c{i}', f'Summary {i}.') for i in range(4)],
        replies=[reply(PAIR, 'Summary 1.', {'$raise': 'pair failed'})])
    add('build_community', cluster=[])
    # build_communities
    add('build_communities', group_ids=None, answers=[GROUPS] + PROJECTION)
    add('build_communities', group_ids=['g'], answers=PROJECTION, max_coroutines=2)
    add('build_communities', group_ids=['zz'])
    # remove_communities
    add('remove_communities', group_ids=['g', 'h'])
    add('remove_communities', group_ids=None)
    add('remove_communities', group_ids=[])
    add('remove_communities', group_ids=['g'], ops=['remove_communities'])
    add('remove_communities', group_ids=['g'], graph_ops=True)
    add('remove_communities', group_ids=None, graph_ops=True)
    # determine_entity_community
    entity = node('n1', 'Ana is an engineer.')
    add('determine_entity_community', entity=entity, answers=[answer(MEMBER, [community('k1'), community('k2')], 'n1')])
    add('determine_entity_community', entity=entity, answers=[
        answer(NEIGHBOR, [community('k1'), community('k2'), community('k2', 'Other copy.'), community('k3')], 'n1')])
    add('determine_entity_community', entity=entity, answers=[
        answer(NEIGHBOR, [community('k1'), community('k2'), community('k1', 'Second.')], 'n1')])
    add('determine_entity_community', entity=entity)
    add('determine_entity_community', entity=entity, ops=['determine_entity_community'], community=community('k9'),
        is_new=True)
    add('determine_entity_community', entity=entity, ops=['determine_entity_community'], community=None, is_new=False)
    # update_community
    add('update_community', entity=entity)
    add('update_community', entity=entity, answers=[answer(MEMBER, [community('k1', embedding=[0.5, 0.5])], 'n1')])
    add('update_community', entity=entity, routing=True, answers=[answer(NEIGHBOR, [community('k2')], 'n1')])
    add('update_community', entity=entity, answers=[answer(NEIGHBOR, [community('k2')], 'n1')],
        replies=[reply(PAIR, '', {'summary': 'Merged.'}), reply(DESCRIPTION, 'Merged.', {'description': 'Merged\nname'})])
    # The cases of the review round come last, so that the index of each earlier case stays (CANCEL_CASES).
    # A tie in determine_entity_community: the first community that reaches the highest count wins
    add('determine_entity_community', entity=entity, answers=[answer(NEIGHBOR, [community('k1'), community('k2')], 'n1')])
    add('determine_entity_community', entity=entity, answers=[
        answer(NEIGHBOR, [community('k2'), community('k1'), community('k2', 'Again.'), community('k1', 'Once more.')], 'n1')])
    # A cluster of one node with a summary of 2000 characters, and a cluster of nodes of two groups
    add('build_community', cluster=[node('c0', 'x' * 2000)])
    add('build_community', cluster=[node('c0', 'Summary 0.', 'g'), node('c1', 'Summary 1.', 'h')])
    # The limit of build_communities: 12 clusters with max_coroutines None and 0, and one cluster of 6 with a limit of 2
    twelve = [node(f'b{i}', f'Summary b{i}.') for i in range(24)]
    add('build_communities', group_ids=['g'], stored_nodes=twelve, ops=['get_community_clusters'],
        clusters=[[f'b{2 * i}', f'b{2 * i + 1}'] for i in range(12)], max_coroutines=None)
    add('build_communities', group_ids=['g'], stored_nodes=twelve, ops=['get_community_clusters'],
        clusters=[[f'b{2 * i}', f'b{2 * i + 1}'] for i in range(12)], max_coroutines=0)
    add('build_communities', group_ids=['g'], stored_nodes=twelve, ops=['get_community_clusters'],
        clusters=[[f'b{i}' for i in range(6)]], max_coroutines=2)
    # Each optional argument left out: the cluster of 8 pairs in a round of 4, and two clusters of one group
    add('get_community_clusters', defaults=True, group_ids=['g'], answers=PROJECTION)
    add('summarize_pair', defaults=True, pair=['Ana is an engineer.', 'Bob runs a bakery.'])
    add('generate_summary_description', defaults=True, summary='A summary of Ana.')
    add('build_community', defaults=True, cluster=[node(f'c{i}', f'Summary {i}.') for i in range(8)])
    add('build_communities', defaults=True, group_ids=['g'], answers=PROJECTION)
    add('remove_communities', defaults=True)
    add('update_community', defaults=True, entity=entity, answers=[answer(NEIGHBOR, [community('k2')], 'n1')])
    # The adversarial pass of layer 7: build_communities with a routed prompt library whose summarize_pair prompt is a
    # marker (a `routing` text), the case that covers the upstream test
    # test_build_communities_uses_configured_prompt_library: each build_community gets the clients, so each pair call
    # sends the marker. The case with "defaults" above has the same input with no routing.
    add('build_communities', group_ids=['g'], answers=PROJECTION, routing='CUSTOM_BUILD_COMMUNITY')
    return out


async def run_case(case: dict, mods, cancel_at=None) -> dict:
    co, EntityNode, CommunityNode, GraphitiClients, GraphProvider, GraphOperationsInterface, GraphMaintenanceOperations, \
        default_library = mods
    from graphiti_core.llm_client.config import ModelSize
    replies = [dict(r) for r in case.get('replies', [])]
    texts: list[str] = []
    state['main'] = asyncio.current_task()
    if cancel_at == -1:
        # A task that is canceled before its first await: the first suspension raises CancelledError.
        state['events_at_cancel'] = 0
        state['main'].cancel()

    async def enter(kind, record):
        state['attempts'] += 1
        state['in_flight'] += 1
        state['max_in_flight'] = max(state['max_in_flight'], state['in_flight'])
        try:
            await asyncio.sleep(0)
            events.append({'kind': kind, **record})
            state['count'] += 1
            if cancel_at is not None and state['count'] - 1 == cancel_at:
                state['events_at_cancel'] = len(events)
                state['main'].cancel()
        finally:
            state['in_flight'] -= 1

    class FakeLlm:
        # The defaults of LLMClient.generate_response.
        async def generate_response(self, messages, response_model=None, max_tokens=None, model_size=ModelSize.medium,
                                    group_id=None, prompt_name=None, attribute_extraction=False):
            await enter('llm', {
                'prompt_name': prompt_name,
                'response_model': None if response_model is None else response_model.__name__,
                'max_tokens': max_tokens, 'model_size': model_size.value,
                'group_id': group_id, 'attribute_extraction': attribute_extraction,
                'messages': [[m.role, hashlib.sha256(m.content.encode('utf-8', 'surrogatepass')).hexdigest(),
                              len(m.content)] for m in messages],
            })
            last = messages[-1].content
            for item in replies:
                if item['prompt'] == prompt_name and item['needle'] in last and item.get('times', 1) > 0:
                    if 'times' in item:
                        item['times'] -= 1
                    value = item['reply']
                    if isinstance(value, dict) and '$raise' in value:
                        raise RuntimeError(value['$raise'])
                    return json.loads(json.dumps(value))
            digest = hashlib.sha256(last.encode('utf-8', 'surrogatepass')).hexdigest()[:8]
            if prompt_name == PAIR:
                return {'summary': f'P{digest}.'}
            if prompt_name == DESCRIPTION:
                return {'description': f'D{digest}'}
            raise LookupError(f'no reply for {prompt_name}')

    class FakeEmbedder:
        async def create(self, input_data):
            await enter('embed', {'input': list(input_data)})
            text = input_data[0]
            if text not in texts:
                texts.append(text)
            return [float(texts.index(text)), 1.0]

    def stored():
        return [EntityNode(**decode(n)) for n in case['stored_nodes']]

    class FakeOps(GraphOperationsInterface):
        async def node_get_by_group_ids(self, _cls, driver, group_ids, limit=None, uuid_cursor=None):
            await enter('get_by_group_ids', {'group_ids': list(group_ids), 'limit': limit, 'uuid_cursor': uuid_cursor})
            return [n for n in stored() if n.group_id in group_ids]

        async def node_get_by_uuids(self, _cls, driver, uuids, group_id=None):
            await enter('get_by_uuids', {'uuids': list(uuids), 'group_id': group_id})
            return [n for n in stored() if n.uuid in uuids]

        async def get_community_clusters(self, driver, group_ids):
            if 'get_community_clusters' not in case['ops']:
                raise NotImplementedError
            await enter('ops_get_community_clusters', {'group_ids': group_ids})
            by_uuid = {n.uuid: n for n in stored()}
            return [[by_uuid[u] for u in cluster] for cluster in case['clusters']]

        async def remove_communities(self, driver, group_ids=None):
            if 'remove_communities' not in case['ops']:
                raise NotImplementedError
            await enter('ops_remove_communities', {'group_ids': group_ids})

        async def determine_entity_community(self, driver, entity):
            if 'determine_entity_community' not in case['ops']:
                raise NotImplementedError
            await enter('ops_determine_entity_community', {'entity': entity.uuid})
            found = case['community']
            return (None if found is None else CommunityNode(**decode(found))), case['is_new']

    class FakeMaintenance(GraphMaintenanceOperations):
        async def clear_data(self, executor, group_ids=None):
            raise AssertionError('not used')

        async def build_indices_and_constraints(self, executor, delete_existing=False):
            raise AssertionError('not used')

        async def delete_all_indexes(self, executor):
            raise AssertionError('not used')

        async def get_community_clusters(self, executor, group_ids=None):
            raise AssertionError('not used')

        async def determine_entity_community(self, executor, entity):
            raise AssertionError('not used')

        async def get_mentioned_nodes(self, executor, episodes):
            raise AssertionError('not used')

        async def get_communities_by_nodes(self, executor, nodes):
            raise AssertionError('not used')

    class FakeDriver:
        provider = GraphProvider.NEO4J
        graph_operations_interface = FakeOps()
        graph_ops = FakeMaintenance() if case['graph_ops'] else None

        async def execute_query(self, cypher_query_, **kwargs):
            await enter('query', {'query': cypher_query_, 'keys': list(kwargs), 'values': encode(list(kwargs.values()))})
            key = kwargs.get('uuid', kwargs.get('entity_uuid'))
            for item in case['answers']:
                if item['needle'] in cypher_query_ and ('uuid' not in item or item['uuid'] == key):
                    return [decode(r) for r in item['records']], None, None
            return [], None, None

    from graphiti_core.tracer import NoOpTracer

    llm = FakeLlm()
    embedder = FakeEmbedder()
    driver = FakeDriver()
    clients = GraphitiClients.model_construct(
        driver=driver, llm_client=llm, embedder=embedder, cross_encoder=None, tracer=NoOpTracer(),
        max_coroutines=case['max_coroutines'], prompt_library=routed_library(case['routing'], default_library))
    route = clients if case['routing'] else None
    fn = case['fn']
    defaults = case.get('defaults')
    if fn == 'get_community_clusters':
        if defaults:
            clusters = await co.get_community_clusters(driver, case['group_ids'])
        else:
            clusters = await co.get_community_clusters(driver, case['group_ids'], max_coroutines=case['max_coroutines'])
        return {'clusters': [[n.uuid for n in cluster] for cluster in clusters]}
    if fn == 'summarize_pair':
        if defaults:
            return {'summary': await co.summarize_pair(llm, tuple(case['pair']))}
        return {'summary': await co.summarize_pair(llm, tuple(case['pair']), clients=route)}
    if fn == 'generate_summary_description':
        if defaults:
            return {'description': await co.generate_summary_description(llm, case['summary'])}
        return {'description': await co.generate_summary_description(llm, case['summary'], clients=route)}
    if fn == 'build_community':
        cluster = [EntityNode(**decode(n)) for n in case['cluster']]
        if defaults:
            result = await co.build_community(llm, cluster)
        else:
            result = await co.build_community(llm, cluster, clients=route, max_coroutines=case['max_coroutines'])
        return {'community': community_view(result[0]), 'edges': [edge_view(e) for e in result[1]]}
    if fn == 'build_communities':
        if defaults:
            nodes, edges = await co.build_communities(driver, llm, case['group_ids'])
        else:
            nodes, edges = await co.build_communities(driver, llm, case['group_ids'], clients=route,
                                                      max_coroutines=case['max_coroutines'])
        return {'communities': [community_view(n) for n in nodes], 'edges': [edge_view(e) for e in edges]}
    if fn == 'remove_communities':
        if defaults:
            return {'result': await co.remove_communities(driver)}
        return {'result': await co.remove_communities(driver, case['group_ids'])}
    if fn == 'determine_entity_community':
        found, is_new = await co.determine_entity_community(driver, EntityNode(**decode(case['entity'])))
        return {'community': None if found is None else community_view(found), 'is_new': is_new}
    if fn == 'update_community':
        entity = EntityNode(**decode(case['entity']))
        if defaults:
            nodes, edges = await co.update_community(driver, llm, embedder, entity)
        else:
            nodes, edges = await co.update_community(driver, llm, embedder, entity, clients=route)
        return {'communities': [community_view(n) for n in nodes], 'edges': [edge_view(e) for e in edges]}
    raise AssertionError(fn)


def routed_library(routing, default_library):
    """The prompt library of a case: none, the default library (True), or a library whose summarize_pair prompt sends
    the marker text of `routing`, as _marker_prompt of tests/utils/maintenance/test_configurable_prompts.py."""
    if routing is True:
        return default_library
    if not routing:
        return None
    from graphiti_core.prompts import create_prompt_library
    from graphiti_core.prompts.models import ChatPrompt, SystemMessage, UserMessage

    def marker_prompt(context: dict) -> ChatPrompt:
        return ChatPrompt(system=SystemMessage(content=routing), user=UserMessage(content=routing))

    return create_prompt_library({'summarize_nodes': {'summarize_pair': marker_prompt}})


def reset() -> None:
    events.clear()
    state.update(in_flight=0, max_in_flight=0, attempts=0, count=0, main=None, events_at_cancel=None)


def call_count(output: dict) -> int:
    return sum(1 for e in output['events'] if e['kind'] != 'log')


def record_cancel(case: dict, mods, k: int) -> dict:
    """The case again with a cancel at call k (k = -1: before the first await)."""
    reset()
    try:
        asyncio.run(run_case(case, mods, k))
        error = None
    except asyncio.CancelledError:
        error = 'CancelledError'
    except Exception as exc:  # noqa: BLE001 - the recording keeps each exception
        error = type(exc).__name__
    if error != 'CancelledError':
        raise SystemExit(f'The cancel at call {k} gave {error} and not CancelledError')
    after = sort_pair_runs(list(events[state['events_at_cancel']:]))
    return placeholders({'k': k, 'error': error, 'attempts': state['attempts'], 'count': state['count'],
                         'after': after}, {})


def record_async(mods) -> list[dict]:
    out = []
    for index, case in enumerate(cases()):
        reset()
        try:
            output = {'ok': asyncio.run(run_case(case, mods))}
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            text = str(error)
            if type(error).__name__ == 'ValidationError':
                text = text.splitlines()[0]
            output = {'error': {'type': type(error).__name__, 'text': text}}
        output['events'] = sort_pair_runs(list(events))
        output['max_in_flight'] = state['max_in_flight']
        item = {'in': case, 'out': placeholders(output, {})}
        if index in CANCEL_CASES:
            item['cancel'] = [record_cancel(case, mods, k) for k in range(-1, call_count(output) - 1)]
        out.append(item)
    return out


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))

    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.driver.graph_operations.graph_operations import GraphOperationsInterface
    from graphiti_core.driver.operations.graph_ops import GraphMaintenanceOperations
    from graphiti_core.graphiti_types import GraphitiClients
    from graphiti_core.nodes import CommunityNode, EntityNode
    from graphiti_core.prompts.lib import default_chat_prompt_library
    from graphiti_core.utils.maintenance import community_operations as co

    class Capture(logging.Handler):
        def emit(self, record):
            entry = {'kind': 'log', 'level': record.levelname, 'message': record.getMessage()}
            if record.exc_info:
                entry['exc'] = True
            events.append(entry)

    root = logging.getLogger('graphiti_core')
    root.setLevel(logging.DEBUG)
    root.propagate = False
    root.addHandler(Capture(logging.DEBUG))

    co.utc_now = lambda: FIXED_NOW

    constants = {'MAX_COMMUNITY_BUILD_CONCURRENCY': co.MAX_COMMUNITY_BUILD_CONCURRENCY}
    record_sync(co)
    async_cases = record_async((co, EntityNode, CommunityNode, GraphitiClients, GraphProvider, GraphOperationsInterface,
                                GraphMaintenanceOperations, default_chat_prompt_library))

    dump = lambda r: json.dumps(r, ensure_ascii=True, allow_nan=False, separators=(',', ':'))  # noqa: E731
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","fixed_now":"' + FIXED_NOW.isoformat() + '",'
        + '"constants":' + dump(constants) + ',"cases":[\n'
        + ',\n'.join(dump(r) for r in records) + '\n],"async_cases":[\n' + ',\n'.join(dump(r) for r in async_cases) + '\n]}\n'
    )
    size = len(text.encode('utf-8'))
    if size > 1_572_864:
        raise SystemExit(f'The values file would have {size} bytes, above the limit of 1,572,864 (R25).')
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(records)} cases and {len(async_cases)} async cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(records)} cases and {len(async_cases)} async cases, {size} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
