"""Records results of graphiti_core/utils/maintenance/combined_extraction.py from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_combined_extraction.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_combined_extraction.py --check

Plan 0004, layer 7, packet P7d. The tool has two parts.

The sync part calls _episode_uuids_for_fact with generated inputs from a fixed seed.

The async part runs extract_nodes_and_edges with a fake LLM client. The other clients are fakes that fail when the code
calls them, because the function calls only the LLM client:

- The LLM client replaces generate_response. It records the prompt name, the response model name, the model size,
  max_tokens, group_id, attribute_extraction, and the role, the SHA-256, and the length of each message. It answers by
  a fixed rule: the first reply of the case for the prompt name whose needle is in the last message and whose count of
  uses is not spent. A reply {"$raise": text} raises RuntimeError. With no such reply, a batch timestamp prompt gets one
  empty row for each `"reference_time"` text of the last message, and another prompt raises LookupError.
- Each fake counts an attempt, yields once (asyncio.sleep(0)), and then records its call. The calls and the log lines
  are one list. Each case keeps the largest count of fake calls in flight.

For the cases of CANCEL_CASES the tool also runs the case again with a cancel (the form of record_node_operations.py): once
for k = -1, where the task is canceled before its first await, and once for each call k except the last, where the fake
that makes call k records it and then cancels the task of the call (the port cancels a token source). The case keeps the
count of the attempts of the run, the count of the recorded calls, and the events after call k. Python lets a
CancelledError through each `except Exception`, so the port must make the same attempts and write the same log lines
(R25). A point where the port makes one more attempt is the late cancel of GAPS F33, and the test names it.

Fixed values on both sides: combined_extraction.utc_now and dangling_endpoints.utc_now give FIXED_NOW, and the C# test
gives the port a clock with the same time. A uuid4 that the code makes is random: the test replaces each one, in the
order of its first appearance in the case output, by "<new:N>". A timed log line has its milliseconds replaced by
"<ms>".

Python iterates one set of this module: `list({'Entity', str(entity_type_name)})` (RULES.md R14, decision D18). The port
gives `Entity` first. The tool binds the module name EntityNode to a subclass that puts `Entity` first in its labels,
which is the same order, as record_node_operations.py does.

It writes tests/Port.Evaluation/values/combined_extraction.json. --check writes nothing and exits 1 if the file differs
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
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'combined_extraction.json'

SEED = 20261127
FIXED_NOW = datetime(2026, 3, 4, 5, 6, 7, 891011, tzinfo=timezone.utc)
UUID4 = re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}')
MS = re.compile(r'in [0-9.e+-]+ ms')

rng = random.Random(SEED)
events: list[dict] = []
state: dict = {}

# A cancel leaves the coroutines of a gather that never started. Python warns about each one, and that is no result.
warnings.filterwarnings('ignore', message='coroutine .* was never awaited', category=RuntimeWarning)

# The cases that the tool runs again with a cancel. The C# test names the same cases.
CANCEL_CASES = [0, 14, 15]


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


def edge_view(edge) -> dict:
    return {
        'uuid': edge.uuid, 'group_id': edge.group_id, 'source_node_uuid': edge.source_node_uuid,
        'target_node_uuid': edge.target_node_uuid, 'created_at': dt(edge.created_at), 'name': edge.name,
        'fact': edge.fact, 'fact_embedding': edge.fact_embedding, 'episodes': list(edge.episodes),
        'expired_at': dt(edge.expired_at), 'valid_at': dt(edge.valid_at), 'invalid_at': dt(edge.invalid_at),
        'reference_time': dt(edge.reference_time), 'attributes': encode(edge.attributes),
    }


def node_view(node) -> dict:
    return {
        'uuid': node.uuid, 'name': node.name, 'group_id': node.group_id, 'labels': list(node.labels),
        'created_at': dt(node.created_at), 'summary': node.summary, 'attributes': encode(node.attributes),
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


# ---------------------------------------------------------------------------------------------------------------------
# Generated data


def ep(uuid: str, valid_at: datetime | None = None, group_id: str = 'g', content: str = 'Ana works at Acme.',
       source: str = 'message') -> dict:
    return {'uuid': uuid, 'name': 'episode ' + uuid, 'group_id': group_id, 'labels': [], 'source': source,
            'source_description': 'chat', 'content': content,
            'valid_at': dt(valid_at or datetime(2025, 1, 1, tzinfo=timezone.utc)),
            'created_at': dt(datetime(2025, 1, 1, tzinfo=timezone.utc))}


records: list[dict] = []


def record_sync(ce, CombinedFact, EpisodicNode) -> None:
    for i in range(80):
        count = rng.randint(0, 4)
        episodes = [ep(f'e{j}') for j in range(count)]
        data = {'source_entity_name': 'a', 'target_entity_name': 'b', 'relation_type': 'R', 'fact': 'f'}
        if rng.random() < 0.85:
            data['episode_indices'] = [rng.randint(-2, 5) for _ in range(rng.randint(0, 4))]
        events.clear()
        try:
            result = {'ok': ce._episode_uuids_for_fact(
                CombinedFact(**data), [EpisodicNode(**decode(e)) for e in episodes])}
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            result = {'error': {'type': type(error).__name__, 'text': str(error)}}
        records.append({'f': '_episode_uuids_for_fact', 'in': {'fact': data, 'episodes': episodes}, 'out': result})


# ---------------------------------------------------------------------------------------------------------------------
# Async scenarios

REF = datetime(2025, 1, 1, tzinfo=timezone.utc)

# The custom entity and edge types: a name and a docstring.
TYPE_DEFS = {
    'Person': 'A human being.',
    'Company': 'A named business or organization.',
    'Place': None,
    'Bad Type': 'A type with a name that is not a Cypher identifier.',
    'WORKS_AT': 'Employment relation.',
    'LIVES_IN': None,
    'KNOWS': 'Two people know each other.',
}


def ent(name, type_id=0) -> dict:
    return {'name': name, 'entity_type_id': type_id}


def fact(source, target, relation, text, indices=None) -> dict:
    data = {'source_entity_name': source, 'target_entity_name': target, 'relation_type': relation, 'fact': text}
    if indices is not None:
        data['episode_indices'] = indices
    return data


def reply(prompt: str, needle: str, value, times=None) -> dict:
    item = {'prompt': prompt, 'needle': needle, 'reply': value}
    if times is not None:
        item['times'] = times
    return item


def main_reply(entities, edges) -> dict:
    return reply('extract_nodes_and_edges.extract_message', '', {'extracted_entities': entities, 'edges': edges})


def ts(valid_at=None, invalid_at=None) -> dict:
    return {'valid_at': valid_at, 'invalid_at': invalid_at}


TS = 'extract_edges.extract_timestamps_batch'


def cases() -> list[dict]:
    base = {'episode': ep('ep1', REF), 'previous': [ep('ep0', datetime(2024, 3, 1, tzinfo=timezone.utc), content='Earlier.')],
            'entity_types': None, 'excluded': None, 'type_map': None, 'edge_types': None, 'strict': False,
            'instructions': None, 'timeline': None, 'max_coroutines': None, 'routing': False}
    out = []

    def add(**kw):
        case = dict(base)
        case.update(kw)
        out.append(case)

    # Nodes and edges: an empty name, a duplicate name, a dangling target and source, an empty fact, an empty
    # endpoint name, an orphan node, an out-of-range type id, and timestamps of each kind
    entities = [ent('Ana', 1), ent('  '), ent('acme', 0), ent('Acme', 2), ent('Paris', 9), ent('Orphan'), ent('ana ', 0)]
    edges = [
        fact('Ana', 'Acme', 'WORKS_AT', 'Ana works at Acme'),
        fact(' ANA ', 'Lyon', 'LIVES_IN', 'Ana lives in Lyon', [0]),
        fact('Zed', 'Paris', 'VISITED', 'Zed visited Paris', [3]),
        fact('Ana', 'Acme', 'X', '   '),
        fact('', 'Acme', 'X', 'empty source'),
        fact('Ana', 'lyon', 'LIKES', 'Ana likes lyon', []),
    ]
    add(entity_types=['Person', 'Company'], replies=[
        main_reply(entities, edges),
        reply(TS, '', {'timestamps': [ts('2024-01-01T00:00:00Z'), ts(None, '2026-01-01T00:00:00'),
                                      ts('not a date', 'bad'), ts('2027-02-01T00:00:00+00:00')]}),
    ])
    # The same with the default type only, prompt routing, and custom instructions
    add(routing=True, instructions='Prefer short facts.', replies=[main_reply(entities, edges)])
    # Excluded types: the default type is excluded, and an excluded entity is an edge endpoint
    add(entity_types=['Person', 'Company'], excluded=['Entity', 'Company'], replies=[
        main_reply([ent('Alice', 0), ent('Acme', 1), ent('Bob', 5)],
                   [fact('Alice', 'Acme', 'WORKS_AT', 'Alice works at Acme'),
                    fact('Alice', 'Bob', 'KNOWS', 'Alice knows Bob'),
                    fact('acme', 'Bob', 'KNOWS', 'Acme knows Bob')]),
    ])
    # Each type excluded: no LLM call
    add(entity_types=['Person'], excluded=['Entity', 'Person'], replies=[])
    add(excluded=['Entity'], replies=[])
    # Strict edge types: the edge types context, a missing signature, and the filter
    add(entity_types=['Person', 'Company', 'Place'], edge_types=['WORKS_AT', 'LIVES_IN', 'KNOWS'],
        type_map=[[['Person', 'Company'], ['WORKS_AT']], [['Person', 'Place'], ['LIVES_IN', 'WORKS_AT']]],
        strict=True, replies=[
            main_reply([ent('Ana', 1), ent('Acme', 2), ent('Paris', 3)],
                       [fact('Ana', 'Acme', 'WORKS_AT', 'Ana works at Acme'),
                        fact('Ana', 'Paris', 'LIVES_IN', 'Ana lives in Paris'),
                        fact('Ana', 'Paris', 'VISITED', 'Ana visited Paris'),
                        fact('Ana', 'Bob', 'KNOWS', 'Ana knows Bob')]),
            reply(TS, 'Ana knows Bob', {'timestamps': [ts('2020-01-01T00:00:00Z')]}),
        ])
    # Strict with no edge types, and with edge types but no map
    add(strict=True, edge_types=['WORKS_AT'], replies=[])
    add(strict=True, type_map=[[['Person', 'Company'], ['WORKS_AT']]], replies=[])
    add(edge_types=['WORKS_AT'], replies=[main_reply([ent('Ana')], [fact('Ana', 'Acme', 'WORKS_AT', 'Ana works at Acme')])])
    # Non-strict with a map: the context, and an edge of another type is kept
    add(entity_types=['Person'], edge_types=['KNOWS', 'WORKS_AT'], type_map=[[['Person', 'Person'], ['KNOWS']]],
        replies=[main_reply([ent('Ana', 1), ent('Bob', 1)], [fact('Ana', 'Bob', 'LIKES', 'Ana likes Bob')])])
    # Several episodes: indices, the reference time of each fact, and a timeline with a context turn
    three = [ep('ep1', REF), ep('ep2', datetime(2025, 6, 1, tzinfo=timezone.utc), content='Ana moved to Paris.'),
             ep('ep3', datetime(2024, 6, 1, 9, 30, tzinfo=timezone(timedelta(hours=2))), content='Bob knows Ana.')]
    many_edges = [
        fact('Ana', 'Paris', 'LIVES_IN', 'Ana lives in Paris', [1]),
        fact('Bob', 'Ana', 'KNOWS', 'Bob knows Ana', [2, 0, 2]),
        fact('Ana', 'Acme', 'WORKS_AT', 'Ana works at Acme', []),
        fact('Ana', 'Acme', 'WORKS_AT', 'Ana stays at Acme', [7, -1]),
        fact('Ana', 'Bob', 'KNOWS', 'Ana met Bob', [-1, 1]),
    ]
    add(episode=three, replies=[main_reply([ent('Ana'), ent('Paris'), ent('Bob'), ent('Acme')], many_edges)])
    add(episode=[three[0], three[2]], timeline=[three[0], ep('ctx', REF, content='An assistant turn.'), three[2]],
        previous=[], replies=[main_reply([ent('Ana'), ent('Bob')], many_edges[1:2] + many_edges[4:])])
    add(episode=[ep('ep9', REF, group_id='h', source='text')], previous=[], timeline=[],
        replies=[main_reply([], [fact('Ana', 'New Co', 'WORKS_AT', 'Ana works at New Co'),
                                 fact('new co', 'Ana', 'EMPLOYS', 'New Co employs Ana')])])
    # No edge: each node is an orphan, and no timestamp call
    add(replies=[main_reply([ent('Ana'), ent('Bob')], [])])
    # Timestamp groups: more facts than one group, a failure then a success, two failures, and row counts that differ
    big = [fact(f'E{i}', f'E{i + 1}', 'LINKS_TO', f'E{i} links to E{i + 1} now') for i in range(45)]
    add(max_coroutines=2, replies=[
        main_reply([ent(f'E{i}') for i in range(46)], big),
        reply(TS, 'E0 links', {'$raise': 'first attempt fails'}, times=1),
        reply(TS, 'E20 links', {'$raise': 'fails'}),
        reply(TS, 'E40 links', {'timestamps': [ts('2024-05-05T00:00:00Z')] * 7}),
    ])
    add(max_coroutines=1, replies=[
        main_reply([ent(f'E{i}') for i in range(23)], big[:22]),
        reply(TS, 'E20 links', {'timestamps': [ts('2024-05-05T00:00:00Z')]}),
        reply(TS, 'E0 links', {'timestamps': 'not a list'}, times=1),
        reply(TS, 'E0 links', {'missing': True}),
    ])
    # A reply with a missing field, and one with a wrong type
    add(replies=[reply('extract_nodes_and_edges.extract_message', '', {'extracted_entities': [ent('Ana')]})])
    add(replies=[main_reply([{'name': 'Ana', 'entity_type_id': 'x'}], [])])
    # The main call fails
    add(replies=[reply('extract_nodes_and_edges.extract_message', '', {'$raise': 'llm down'})])
    # The cases of the review round come last, so that the index of each earlier case stays (CANCEL_CASES).
    # An excluded source alone. The exclusion of the default type renumbers the types, so Person is id 0, and id 1 is out of
    # range: Acme has the default type and is excluded, and Alice is a Person.
    add(entity_types=['Person'], excluded=['Entity'], replies=[
        main_reply([ent('Acme', 1), ent('Alice', 0)], [fact('Acme', 'Alice', 'KNOWS', 'Acme knows Alice')]),
    ])
    # The indices [1, 2] and [2, 1]: the reference time is the time of the first index in range
    add(episode=three, replies=[main_reply([ent('Ana'), ent('Paris'), ent('Bob')], [
        fact('Ana', 'Paris', 'LIVES_IN', 'Ana lives in Paris', [1, 2]),
        fact('Bob', 'Paris', 'VISITED', 'Bob visited Paris', [2, 1]),
    ])])
    # An empty edge type map with edge types, strict on and off
    add(entity_types=['Person'], edge_types=['WORKS_AT'], type_map=[], strict=True, replies=[])
    add(entity_types=['Person'], edge_types=['WORKS_AT'], type_map=[], strict=False, replies=[
        main_reply([ent('Ana', 1), ent('Acme')], [fact('Ana', 'Acme', 'WORKS_AT', 'Ana works at Acme')]),
    ])
    # A name and a fact of Python whitespace only: str.strip removes U+001C, and the .NET Trim does not
    add(replies=[main_reply([ent('\x1c'), ent('Ana'), ent('Bob')], [
        fact('Ana', 'Bob', 'KNOWS', '\x1c'),
        fact('\x1c', 'Bob', 'KNOWS', 'a fact with an empty source'),
        fact('Ana', 'Bob', 'KNOWS', 'Ana knows Bob'),
    ])])
    # Episodes of the groups g and h: the group of the first episode is the group of the call and of each result
    add(episode=[ep('ga', REF, group_id='g'), ep('hb', REF, group_id='h', content='Bob knows Ana.')], previous=[], replies=[
        main_reply([ent('Ana'), ent('Bob')], [fact('Ana', 'Bob', 'KNOWS', 'Ana knows Bob', [1])]),
    ])
    # The entity type id -1 is out of range, so the type is Entity
    add(entity_types=['Person'], replies=[main_reply([ent('Ana', -1), ent('Bob', 1)], [fact('Ana', 'Bob', 'KNOWS', 'Ana knows Bob')])])
    # Each optional argument left out
    add(defaults=True, replies=[main_reply([ent('Ana'), ent('Acme')], [fact('Ana', 'Acme', 'WORKS_AT', 'Ana works at Acme')])])
    # An entity type name that is not a Cypher identifier: the error of the node validator names EntityNode
    add(entity_types=['Bad Type'], replies=[main_reply([ent('Ana', 1)], [fact('Ana', 'Ana', 'SELF', 'Ana is Ana')])])
    return out


async def run_case(case: dict, mods, cancel_at=None) -> dict:
    ce, EntityNode, EpisodicNode, GraphitiClients, default_library, models = mods
    from graphiti_core.llm_client.config import ModelSize
    replies = [dict(r) for r in case.get('replies', [])]
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
            if prompt_name == TS:
                return {'timestamps': [{'valid_at': None, 'invalid_at': None}] * last.count('"reference_time"')}
            raise LookupError(f'no reply for {prompt_name}')

    class Unused:
        def __getattr__(self, name):
            raise AssertionError(f'{name} is not used')

    from graphiti_core.tracer import NoOpTracer

    clients = GraphitiClients.model_construct(
        driver=Unused(), llm_client=FakeLlm(), embedder=Unused(), cross_encoder=Unused(),
        tracer=NoOpTracer(), max_coroutines=case.get('max_coroutines'),
        prompt_library=default_library if case.get('routing') else None)

    def types_of(names):
        return None if names is None else {n: models[n] for n in names}

    episode = case['episode']
    episode_arg = [EpisodicNode(**decode(e)) for e in episode] if isinstance(episode, list) else EpisodicNode(**decode(episode))
    timeline = None if case['timeline'] is None else [EpisodicNode(**decode(e)) for e in case['timeline']]
    type_map = None if case['type_map'] is None else {tuple(k): list(v) for k, v in case['type_map']}
    previous = [EpisodicNode(**decode(e)) for e in case['previous']]
    if case.get('defaults'):
        nodes, edges, index_map = await ce.extract_nodes_and_edges(clients, episode_arg, previous)
    else:
        nodes, edges, index_map = await ce.extract_nodes_and_edges(
            clients, episode_arg, previous,
            entity_types=types_of(case['entity_types']), excluded_entity_types=case['excluded'], edge_type_map=type_map,
            edge_types=types_of(case['edge_types']), strict_edge_types=case['strict'],
            custom_extraction_instructions=case['instructions'], timeline=timeline)
    return {'nodes': [node_view(n) for n in nodes], 'edges': [edge_view(e) for e in edges], 'map': [[k, v] for k, v in index_map.items()]}


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
    after = events[state['events_at_cancel']:]
    return placeholders({'k': k, 'error': error, 'attempts': state['attempts'], 'count': state['count'],
                         'after': list(after)}, {})


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
        output['events'] = list(events)
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
    from pydantic import BaseModel, ConfigDict, create_model

    from graphiti_core.graphiti_types import GraphitiClients
    from graphiti_core.nodes import EntityNode, EpisodicNode
    from graphiti_core.prompts.extract_nodes_and_edges import CombinedFact
    from graphiti_core.prompts.lib import default_chat_prompt_library
    from graphiti_core.utils.maintenance import combined_extraction as ce
    from graphiti_core.utils.maintenance import dangling_endpoints

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

    class OrderedLabelsEntityNode(EntityNode):
        model_config = ConfigDict(title='EntityNode')

        def __init__(self, **data):
            labels = data.get('labels')
            if labels is not None and 'Entity' in labels:
                data['labels'] = ['Entity'] + [label for label in labels if label != 'Entity']
            super().__init__(**data)

    # The title of a pydantic error is the title of the model config, and the class takes the name of upstream too.
    OrderedLabelsEntityNode.__name__ = 'EntityNode'
    OrderedLabelsEntityNode.__qualname__ = 'EntityNode'
    ce.EntityNode = OrderedLabelsEntityNode
    ce.utc_now = lambda: FIXED_NOW
    dangling_endpoints.utc_now = lambda: FIXED_NOW

    models = {name: create_model(name, __base__=BaseModel, __doc__=doc) for name, doc in TYPE_DEFS.items()}
    type_defs = {name: model.__doc__ for name, model in models.items()}

    constants = {'TIMESTAMP_BATCH_SIZE': ce.TIMESTAMP_BATCH_SIZE, 'TIMESTAMP_BATCH_ATTEMPTS': ce.TIMESTAMP_BATCH_ATTEMPTS}
    record_sync(ce, CombinedFact, EpisodicNode)
    async_cases = record_async((ce, EntityNode, EpisodicNode, GraphitiClients, default_chat_prompt_library, models))

    dump = lambda r: json.dumps(r, ensure_ascii=True, allow_nan=False, separators=(',', ':'))  # noqa: E731
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","fixed_now":"' + FIXED_NOW.isoformat() + '",'
        + '"constants":' + dump(constants) + ',"types":' + dump(type_defs) + ',"cases":[\n'
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
