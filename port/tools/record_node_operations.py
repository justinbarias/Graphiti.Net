"""Records results of graphiti_core/utils/maintenance/node_operations.py from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_node_operations.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_node_operations.py --check

Plan 0004, layer 7, packet P7b. The tool calls each function of the module with generated inputs from a fixed seed.
The sync helpers get plain arguments. The async functions get fakes at the network boundary on both sides:

- an LLM client whose generate_response records the prompt name, each message, the response model, and the keyword
  arguments, and returns a scripted reply for the prompt name (a list that the calls take in order);
- an embedder whose vector holds the index of the text in a run table, so that the search fake can find the text;
- a driver with a graph operations interface (node_get_by_names, node_get_by_attribute_values) and a search
  interface (node_similarity_search), each with a scripted answer or a scripted failure.

Each fake counts an attempt, yields once (asyncio.sleep(0)), and then records its call. It also counts the calls in
flight, and the case keeps the highest count of each kind (R25). The calls and the log lines of graphiti_core are one ordered list. A log
line keeps its level, its text, the keys of `extra` that the module writes, and the type of the exception of
`exc_info`.

For a set of cases (cancel_scenarios) the tool also runs the case again with a cancel: once for k = -1, where the task is
canceled before its first await, and once for each call k except the last, where the fake that makes call k records it
and then cancels the task of the call (the port cancels a token source). The case keeps the count of the attempts of
the run, the count of the recorded calls, and the events after call k. Python lets a CancelledError through each
`except Exception`, so the port must make the same attempts and write the same log lines (R25).

Fixed values on both sides:
- node_operations.utc_now gives FIXED_NOW. The C# test gives the port a clock with the same time.
- The text `in <n> ms` of the extraction log line depends on the clock of the process. Both sides write `in <ms> ms`.
- A uuid4 that the module makes is random. Both sides replace each uuid4 text of the output with `<uuid:N>`, where N
  counts the uuids in the order in which they first appear.
- A message longer than 400 characters is stored as its SHA-256 and its length (R25).

Python iterates a set in hash order. One set of this module is iterated: `list({'Entity', str(entity_type_name)})` in
_create_entity_nodes (RULES.md R14, decision D18). The port gives `Entity` first, which is the insertion order. The
tool binds the module name EntityNode to a subclass that puts `Entity` first in its labels, which is the same order.
node_operations builds an EntityNode at that one site only.

The functions of dedup_helpers and attribute_utils that node_operations calls iterate two sets
(candidate_ids in _resolve_with_similarity, and dropped in cap_string_attributes). The tool binds the name `set` of
both modules to the insertion-order set of record_maintenance_helpers.py, as that recorder does.

A custom entity type is a pydantic model that the tool builds from a spec: a name, a docstring, and fields with a kind,
a list flag, a description, a required flag, and a max_length. The C# test builds a GraphTypeDescriptor from the same
spec. The shape validation `entity_type(**merged)` waits for GraphTypeDescriptor.ModelValidate (lead, debt T18), so
the cases that reach it call extract_attributes_only_from_nodes and _extract_entity_attributes with
validate_shape=False, and extract_attributes_from_nodes only with inputs that do not reach it.

It writes tests/Port.Evaluation/values/node_operations.json. --check writes nothing and exits 1 if the file differs
from what the run would write. The tool needs Python 3.12 and the pinned upstream. It reads the folder of the variable
GRAPHITI_UPSTREAM, or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import copy
import hashlib
import json
import logging
import os
import random
import re
import sys
import warnings
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'node_operations.json'

SEED = 20261128
FIXED_NOW = datetime(2026, 3, 4, 5, 6, 7, 891011, tzinfo=timezone.utc)
UUID4 = re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}')
MS = re.compile(r' in \d+ ms$')
EXTRA_KEYS = ['node_uuid', 'group_id', 'identity_type', 'extracted_node_count', 'group_count']
LONG_TEXT = 400

rng = random.Random(SEED)

# A cancel leaves the coroutines of a gather that never started. Python warns about each one, and that is no result.
warnings.filterwarnings('ignore', message='coroutine .* was never awaited', category=RuntimeWarning)

# The event list of the run in progress. The log handler appends to it.
CURRENT: dict = {'events': None}

NAMES = ['Alice Johnson', 'alice johnson', 'Alice', 'Bob', 'Joe', 'Joseph', 'Java', 'Acme Corporation', 'ACME',
         'Acme Corp', 'Jean-Luc Picard', 'Jean Luc Picard', 'Zoë Durand', '東京大学', 'International Business Machines',
         'Meaningful Name Here', 'Dizzy Gillespie', 'Dizzy', 'Caroline', 'Melanie', 'version 2.0', 'Café Crème',
         'İstanbul', 'ſmith', 'Ｆｕｌｌ', '\U0001F600 Rocket', 'x', '', '  ', 'Line\nBreak', 'Alice  Johnson',
         'Nate', 'Audrey', 'Joe-Michaels', 'Joe Michaels', 'Completely New Thing']
DOCS = [None, '', 'A human person.', 'A business or organization. It has employees. It files taxes. It has a logo.',
        'A location.\n\nGOOD: New York\nBAD: here', '\n\n  Use e.g. a name. Next Sentence here.\nMore text!',
        'Version 2.0 of it. Another One.', 'Fin. École next. 東京 third. Fourth One.', 'No punctuation at all',
        'Really? Yes! Fine. Extra One.']
SENTENCE_PARTS = ['A person.', 'e.g.', 'Dr.', 'Smith', '2.0', 'version', 'Next', 'next', 'Why?', 'Wow!', 'École',
                  '東京', '\U0001F600.', 'İs', 'ſo', 'end.', ' ', '\n', '\n\n', '\t', ' ', 'X.', 'ǅ', 'Σ.']


class InsertionOrderEntityNode:
    """Built in main(): an EntityNode subclass whose labels put `Entity` first (see the module docstring)."""


def h(text: str):
    if len(text) <= LONG_TEXT:
        return text
    return {'sha256': hashlib.sha256(text.encode('utf-8', 'surrogatepass')).hexdigest(), 'len': len(text)}


def rep(text: str, count: int) -> dict:
    """A long input text in the values file: `text` repeated `count` times. expand() gives the text on both sides."""
    return {'$rep': [text, count]}


def expand(value):
    if isinstance(value, dict):
        if set(value) == {'$rep'}:
            return value['$rep'][0] * value['$rep'][1]
        return {k: expand(v) for k, v in value.items()}
    if isinstance(value, list):
        return [expand(v) for v in value]
    return value


def dt(value):
    return None if value is None else value.isoformat()


def node_view(node) -> dict:
    return {
        'uuid': node.uuid,
        'name': node.name,
        'group_id': node.group_id,
        'labels': list(node.labels),
        'created_at': node.created_at.isoformat(),
        'name_embedding': node.name_embedding,
        'summary': h(node.summary),
        'attributes': node.attributes,
    }


def plain(value):
    if isinstance(value, bool) or value is None or isinstance(value, (int, float, str)):
        return value
    if isinstance(value, (list, tuple)):
        return [plain(v) for v in value]
    if isinstance(value, dict):
        return {k: plain(v) for k, v in value.items()}
    if hasattr(value, 'uuid') and hasattr(value, 'labels'):
        return node_view(value)
    if isinstance(value, datetime):
        return value.isoformat()
    raise TypeError(type(value).__name__)


def normalize_uuids(value):
    """Replaces each uuid4 text with <uuid:N>, N in the order of first appearance (depth first, keys in order)."""
    table: dict[str, str] = {}

    def text(s):
        return UUID4.sub(lambda m: table.setdefault(m.group(0), f'<uuid:{len(table)}>'), s)

    def walk(v):
        if isinstance(v, str):
            return text(v)
        if isinstance(v, list):
            return [walk(x) for x in v]
        if isinstance(v, dict):
            return {text(k): walk(x) for k, x in v.items()}
        return v

    return walk(value)


# ---------------------------------------------------------------- data

def node_data(uuid, name=None, group_id='g', labels=None, summary='', attributes=None, embedding=None):
    data = {
        'uuid': uuid,
        'name': rng.choice(NAMES) if name is None else name,
        'group_id': group_id,
        'labels': ['Entity'] if labels is None else labels,
        'created_at': rng.choice(['2024-01-01T00:00:00+00:00', '2023-06-01T12:30:00+00:00', '2025-02-03T04:05:06.789000+00:00']),
        'summary': summary,
        'attributes': {} if attributes is None else attributes,
    }
    if embedding is not None:
        data['name_embedding'] = embedding
    return data


def episode_data(i, source='message', content=None, valid='2024-05-06T07:08:09+00:00'):
    return {
        'uuid': f'ep-{i}',
        'name': f'episode {i}',
        'group_id': 'g',
        'source': source,
        'source_description': rng.choice(['chat', 'email 東京', '']),
        'content': content if content is not None else rng.choice(['Alice met Bob.', 'Acme hired Joe 😀.', '{"a": 1}', '']),
        'valid_at': valid,
        'created_at': '2024-05-06T07:08:09+00:00',
    }


def type_spec(name, doc, fields):
    return {'name': name, 'doc': doc, 'fields': fields}


def field(name, kind='str', is_list=False, description=None, required=False, max_length=None):
    return {'name': name, 'kind': kind, 'list': is_list, 'description': description, 'required': required,
            'max_length': max_length}


TYPES = {
    'Person': type_spec('Person', 'A human person.\n\nGOOD: Alice\nBAD: them', [
        field('email', description='Email address'), field('phone'), field('age', 'int'),
        field('aliases', 'str', True, 'Other names')]),
    'Company': type_spec('Company', 'A business or organization. It has employees. It files taxes. It has a logo.', [
        field('domain', description='Web domain', max_length=40), field('size'), field('company_id')]),
    'Topic': type_spec('Topic', None, []),
    'Place': type_spec('Place', '', [field('city'), field('lat', 'float'), field('open', 'bool')]),
    'Patient': type_spec('Patient', 'A patient.', [field('first_name'), field('last_name'), field('favorite_color')]),
    'Strict': type_spec('Strict', 'Strict type.', [field('email', required=True), field('full_name', required=True)]),
}


def pick_types(k=None):
    names = rng.sample(list(TYPES), rng.randint(0, 4) if k is None else k)
    return {n: TYPES[n] for n in names}


# ---------------------------------------------------------------- fakes and one run

class Run:
    def __init__(self, case):
        self.case = case
        self.events = []
        CURRENT['events'] = self.events
        self.texts = []
        self.in_flight = {}
        self.max_in_flight = {}
        self.llm_index = {}
        self.attempts = 0
        self.count = 0
        self.cancel_at = None
        self.main = None
        self.events_at_cancel = None

    async def enter(self, kind, record):
        self.attempts += 1
        self.in_flight[kind] = self.in_flight.get(kind, 0) + 1
        self.max_in_flight[kind] = max(self.max_in_flight.get(kind, 0), self.in_flight[kind])
        try:
            await asyncio.sleep(0)
            self.events.append(record)
            self.count += 1
            if self.cancel_at is not None and self.count - 1 == self.cancel_at:
                self.events_at_cancel = len(self.events)
                self.main.cancel()
        finally:
            self.in_flight[kind] -= 1


def build_world(case, m):
    """The clients of one case. `m` holds the upstream names."""
    run = Run(case)
    spec = case.get('clients', {})

    def make_nodes(items):
        return [m.EntityNode(**copy.deepcopy(d)) for d in items]

    class FakeLLM(m.LLMClient):
        async def _generate_response(self, *args, **kwargs):
            raise AssertionError('not used')

        async def generate_response(self, messages, response_model=None, max_tokens=None,
                                    model_size=m.ModelSize.medium, group_id=None, prompt_name=None, *,
                                    attribute_extraction=False, model=None):
            await run.enter('llm', {
                'kind': 'llm',
                'prompt_name': prompt_name,
                'messages': [[msg.role, h(msg.content)] for msg in messages],
                'response_model': None if response_model is None else response_model.__name__,
                'max_tokens': max_tokens,
                'model_size': model_size.value,
                'group_id': group_id,
                'attribute_extraction': attribute_extraction,
            })
            replies = case['llm'].get(prompt_name) or [{}]
            index = run.llm_index.get(prompt_name, 0)
            run.llm_index[prompt_name] = index + 1
            reply = replies[min(index, len(replies) - 1)]
            if reply == {'$raise': 'RuntimeError'}:
                raise RuntimeError('llm down')
            return copy.deepcopy(reply)

    class FakeEmbedder(m.EmbedderClient):
        def vector(self, text):
            run.texts.append(text)
            return [float(len(run.texts) - 1), 0.5]

        async def create(self, input_data):
            await run.enter('embed', {'kind': 'embed_create', 'input': list(input_data)})
            return self.vector(input_data[0])

        async def create_batch(self, input_data_list):
            mode = spec.get('embedder', 'batch')
            if mode == 'single':
                raise NotImplementedError
            await run.enter('embed', {'kind': 'embed_batch', 'input': list(input_data_list)})
            vectors = [self.vector(t) for t in input_data_list]
            if mode == 'short':
                vectors = vectors[:-1]
            return vectors

    class FakeOps(m.GraphOperationsInterface):
        async def node_get_by_names(self, _cls, driver, names, group_ids):
            await run.enter('names', {'kind': 'get_by_names', 'names': list(names), 'group_ids': list(group_ids)})
            answer = case.get('exact', [])
            if answer == '$raise':
                raise RuntimeError('names down')
            if answer == '$not_implemented':
                raise NotImplementedError
            return make_nodes(answer)

        async def node_get_by_attribute_values(self, _cls, driver, group_id, attributes, node_labels=None, limit=100):
            await run.enter('identity', {'kind': 'get_by_attribute_values', 'group_id': group_id,
                                         'attributes': plain(attributes), 'labels': node_labels, 'limit': limit})
            mode = case.get('identity_mode', 'returns')
            if mode == 'raise':
                raise RuntimeError('identity down')
            if mode == 'not_implemented':
                raise NotImplementedError
            return make_nodes(case.get('identity', {}).get(node_labels[0], []))

    class FakeSearch(m.SearchInterface):
        async def node_similarity_search(self, driver, search_vector, search_filter, group_ids=None, limit=100,
                                         min_score=0.7):
            key = int(search_vector[0])
            text = run.texts[key] if key < 1000 else f'vec{key}'
            await run.enter('search', {'kind': 'node_similarity_search', 'query': text, 'group_ids': group_ids,
                                       'limit': limit, 'min_score': min_score})
            return make_nodes(case.get('search', {}).get(text, []))

    class FakeDriver:
        def __init__(self):
            self.provider = m.GraphProvider(spec.get('provider', 'neo4j'))
            self.graph_operations_interface = FakeOps()
            self.search_interface = FakeSearch()

        async def execute_query(self, query, **kwargs):
            await run.enter('query', {'kind': 'execute_query', 'keys': list(kwargs)})
            return [], None, None

    llm = FakeLLM(m.LLMConfig(max_tokens=spec.get('max_tokens', 8192)))
    clients = m.GraphitiClients.model_construct(
        driver=FakeDriver(),
        llm_client=llm,
        embedder=FakeEmbedder(),
        cross_encoder=None,
        tracer=None,
        max_coroutines=spec.get('max_coroutines'),
    )
    return run, clients, make_nodes


def make_types(specs, m):
    if specs is None:
        return None
    from pydantic import Field, create_model
    kinds = {'str': str, 'int': int, 'float': float, 'bool': bool}
    out = {}
    for name, spec in specs.items():
        fields = {}
        for f in spec['fields']:
            t = kinds[f['kind']]
            if f['list']:
                t = list[t]
            kwargs = {}
            if f['description'] is not None:
                kwargs['description'] = f['description']
            if f['max_length'] is not None:
                kwargs['max_length'] = f['max_length']
            if f['required']:
                fields[f['name']] = (t, Field(..., **kwargs))
            else:
                fields[f['name']] = (t | None, Field(default=None, **kwargs))
        model = create_model(name, **fields)
        model.__doc__ = spec['doc']
        out[name] = model
    return out


def episode_arg(value, m):
    if value is None:
        return None
    if isinstance(value, list):
        return [m.EpisodicNode(**copy.deepcopy(e)) for e in value]
    return m.EpisodicNode(**copy.deepcopy(value))


def error_view(error):
    return {'type': type(error).__name__, 'text': str(error)}


def call_async(m, case, cancel_at=None):
    case = expand(case)
    run, clients, make_nodes = build_world(case, m)
    run.cancel_at = cancel_at
    a = case['args']
    nodes = make_nodes(a.get('nodes', []))
    fn = case['fn']
    out = {}

    async def body():
        run.main = asyncio.current_task()
        if cancel_at == -1:
            # A task that is canceled before its first await: the first suspension raises CancelledError.
            run.events_at_cancel = 0
            run.main.cancel()
        entity_types = make_types(a.get('entity_types'), m)
        previous = None if a.get('previous') is None else [m.EpisodicNode(**copy.deepcopy(e)) for e in a['previous']]
        if fn == 'extract_nodes':
            kwargs = {k: a[k] for k in ('excluded_entity_types', 'custom_extraction_instructions') if k in a}
            if 'entity_types' in a:
                kwargs['entity_types'] = entity_types
            extracted, index_map = await m.no.extract_nodes(clients, episode_arg(a['episode'], m), previous, **kwargs)
            return [[node_view(n) for n in extracted], index_map]
        if fn in ('resolve_extracted_nodes', 'resolve_nodes_with_candidates'):
            kwargs = {}
            if 'episode' in a:
                kwargs['episode'] = episode_arg(a['episode'], m)
            if 'previous' in a:
                kwargs['previous_episodes'] = previous
            if 'entity_types' in a:
                kwargs['entity_types'] = entity_types
            if 'identity_properties' in a:
                kwargs['identity_properties'] = a['identity_properties']
            if fn == 'resolve_extracted_nodes':
                if 'override' in a:
                    kwargs['existing_nodes_override'] = make_nodes(a['override'])
                resolved, uuid_map, pairs = await m.no.resolve_extracted_nodes(clients, nodes, **kwargs)
            else:
                candidates = [make_nodes(group) for group in a['candidates']]
                resolved, uuid_map, pairs = await m.no.resolve_nodes_with_candidates(clients, nodes, candidates, **kwargs)
            return [[node_view(n) for n in resolved], uuid_map, [[x.uuid, y.uuid] for x, y in pairs]]
        if fn == 'collect_candidate_nodes':
            kwargs = {}
            if 'override' in a:
                kwargs['existing_nodes_override'] = make_nodes(a['override'])
            if 'identity_properties' in a:
                kwargs['identity_properties'] = a['identity_properties']
            if 'reuse_name_embeddings' in a:
                kwargs['reuse_name_embeddings'] = a['reuse_name_embeddings']
            result = await m.no._collect_candidate_nodes(clients, nodes, **kwargs)
            return [[node_view(n) for n in group] for group in result]
        if fn == 'extract_attributes_from_nodes':
            kwargs = {}
            for key in ('skip_fact_appending', 'include_type_descriptions', 'skip_attribute_extraction'):
                if key in a:
                    kwargs[key] = a[key]
            if 'episode' in a:
                kwargs['episode'] = episode_arg(a['episode'], m)
            if 'previous' in a:
                kwargs['previous_episodes'] = previous
            if 'entity_types' in a:
                kwargs['entity_types'] = entity_types
            if 'edges' in a:
                kwargs['edges'] = [m.EntityEdge(**copy.deepcopy(e)) for e in a['edges']]
            if 'summarize' in a:
                allowed = set(a['summarize'])

                async def should_summarize(node):
                    await asyncio.sleep(0)
                    run.events.append({'kind': 'should_summarize', 'name': node.name})
                    return node.name in allowed

                kwargs['should_summarize_node'] = should_summarize
            result = await m.no.extract_attributes_from_nodes(clients, nodes, **kwargs)
            return {'same': result is nodes}
        if fn == 'extract_attributes_only_from_nodes':
            kwargs = {'validate_shape': False}
            if 'episode' in a:
                kwargs['episode'] = episode_arg(a['episode'], m)
            if 'previous' in a:
                kwargs['previous_episodes'] = previous
            if 'entity_types' in a:
                kwargs['entity_types'] = entity_types
            result = await m.no.extract_attributes_only_from_nodes(clients, nodes, **kwargs)
            return {'same': result is nodes}
        raise AssertionError(fn)

    try:
        out['result'] = plain(asyncio.run(body()))
        out['error'] = None
    except asyncio.CancelledError:
        out['result'] = None
        out['error'] = {'type': 'CancelledError', 'text': ''}
    except Exception as error:  # noqa: BLE001 - the recording keeps each exception
        out['result'] = None
        out['error'] = error_view(error)
    if cancel_at is not None:
        # The attempts of the whole run, and the events after call k (the log lines and the later calls).
        out['attempts'] = run.attempts
        out['count'] = run.count
        out['after'] = run.events[run.events_at_cancel:] if run.events_at_cancel is not None else None
    out['events'] = run.events
    out['nodes_after'] = [node_view(n) for n in nodes]
    out['max_in_flight'] = dict(sorted(run.max_in_flight.items()))
    return out


# ---------------------------------------------------------------- case generation

def extracted_entities_reply(n_episodes, type_count):
    entities = []
    for _ in range(rng.randint(0, 6)):
        e = {'name': rng.choice(NAMES), 'entity_type_id': rng.choice([0, 0, 1, 2, 3, type_count, -1, 99])}
        if rng.random() < 0.6:
            e['episode_indices'] = rng.sample([-1, 0, 1, 2, 3, 7], rng.randint(0, 3))
        entities.append(e)
    reply = {'extracted_entities': entities}
    r = rng.random()
    if r < 0.05:
        reply = {'extracted_entities': [{'entity_type_id': 0}]}
    elif r < 0.08:
        reply = {}
    return reply


def extract_cases():
    cases = []
    for i in range(70):
        types = pick_types() if rng.random() < 0.7 else None
        n = rng.choice([1, 1, 1, 3]) if rng.random() < 0.8 else 2
        source = rng.choice(['message', 'text', 'json', 'fact_triple'])
        episodes = [episode_data(j, source) for j in range(n)]
        args = {'episode': episodes if (n > 1 or rng.random() < 0.3) else episodes[0],
                'previous': [episode_data(10 + j) for j in range(rng.randint(0, 2))]}
        if types is not None:
            args['entity_types'] = types
        if rng.random() < 0.4:
            choices = ['Entity'] + list(types or {}) + ['Nope']
            args['excluded_entity_types'] = rng.sample(choices, rng.randint(0, min(3, len(choices))))
        if rng.random() < 0.4:
            args['custom_extraction_instructions'] = rng.choice([None, '', 'Prefer people. 東京'])
        llm = {}
        for name in ('extract_nodes.extract_message', 'extract_nodes.extract_text', 'extract_nodes.extract_json'):
            llm[name] = [extracted_entities_reply(n, len(types or {}) + 1)]
        cases.append({'fn': 'extract_nodes', 'clients': {}, 'llm': llm, 'args': args})
    # A custom type whose name is not a valid label, an excluded Entity, and all types excluded.
    cases.append({'fn': 'extract_nodes', 'clients': {}, 'args': {'episode': episode_data(1), 'previous': [],
                  'entity_types': {'Bad Type': type_spec('Bad Type', 'Bad.', [])}},
                  'llm': {'extract_nodes.extract_message': [{'extracted_entities': [{'name': 'X', 'entity_type_id': 1}]}]}})
    cases.append({'fn': 'extract_nodes', 'clients': {}, 'args': {'episode': episode_data(1), 'previous': [],
                  'excluded_entity_types': ['Entity']}, 'llm': {}})
    cases.append({'fn': 'extract_nodes', 'clients': {}, 'args': {'episode': [], 'previous': []}, 'llm': {}})
    cases.append({'fn': 'extract_nodes', 'clients': {}, 'args': {'episode': episode_data(1), 'previous': [],
                  'entity_types': {'Company': TYPES['Company']}, 'excluded_entity_types': ['Entity']},
                  'llm': {'extract_nodes.extract_message': [{'extracted_entities': [
                      {'name': 'Acme Robotics', 'entity_type_id': 0}, {'name': 'acme  robotics', 'entity_type_id': 0}]}]}})
    cases.append({'fn': 'extract_nodes', 'clients': {}, 'args': {'episode': episode_data(1), 'previous': []},
                  'llm': {'extract_nodes.extract_message': [{'$raise': 'RuntimeError'}]}})
    return cases


def resolution_reply(n):
    resolutions = []
    for i in range(n):
        if rng.random() < 0.85:
            resolutions.append({'id': i, 'name': rng.choice(NAMES), 'duplicate_candidate_id': rng.choice([-1, 0, 1, 2, 5, 999])})
    r = rng.random()
    if r < 0.15:
        resolutions.append({'id': rng.choice([n, n + 3, -1]), 'name': 'x', 'duplicate_candidate_id': 0})
    elif r < 0.3 and resolutions:
        resolutions.append(dict(resolutions[0], duplicate_candidate_id=-1))
    reply = {'entity_resolutions': resolutions}
    if rng.random() < 0.04:
        reply = {'entity_resolutions': [{'id': 0, 'name': 'x'}]}
    return reply


def resolve_cases():
    cases = []
    for i in range(90):
        count = rng.randint(0, 5)
        group_id = rng.choice(['g', 'g', 'h'])
        nodes = []
        for j in range(count):
            labels = ['Entity'] + rng.sample(['Person', 'Company', 'Place'], rng.randint(0, 2))
            attributes = {}
            if rng.random() < 0.5:
                attributes = {'email': rng.choice(['a@x.io', ' a@x.io ', '', None]), 'domain': rng.choice(['acme.io', 7]),
                              'company_id': rng.choice(['C-1', None])}
            embedding = [float(1000 + j), 0.25] if rng.random() < 0.3 else None
            nodes.append(node_data(f'x{i}-{j}', group_id=group_id, labels=labels, attributes=attributes, embedding=embedding))

        def candidate(k, name=None):
            return node_data(f'c{i}-{k}', name=name, group_id=rng.choice([group_id, 'h']),
                             labels=['Entity'] + rng.sample(['Person', 'Company'], rng.randint(0, 1)),
                             summary=rng.choice(['', rep('about ', 30)]),
                             attributes=rng.choice([{}, {'candidate_id': 999, 'genre': 'jazz'}, {'email': 'a@x.io'}]))

        k = 0
        search = {}
        for node in nodes:
            hits = []
            for _ in range(rng.randint(0, 3)):
                hits.append(candidate(k, rng.choice([node['name'], rng.choice(NAMES)])))
                k += 1
            key = node['name'].replace('\n', ' ')
            if 'name_embedding' in node:
                key = f'vec{int(node["name_embedding"][0])}'
            search[key] = hits
        exact = [candidate(100 + j, rng.choice([n['name'] for n in nodes] + NAMES)) for j in range(rng.randint(0, 3))] if nodes else []
        if rng.random() < 0.1:
            exact = '$raise'
        identity = {label: [candidate(200 + j) for j in range(rng.randint(0, 2))] for label in ('Person', 'Company')}
        args = {'nodes': nodes}
        if rng.random() < 0.7:
            args['episode'] = episode_data(1)
        if rng.random() < 0.6:
            args['previous'] = [episode_data(2)]
        if rng.random() < 0.4:
            args['entity_types'] = pick_types()
        if rng.random() < 0.5:
            args['identity_properties'] = rng.choice([{}, {'Person': ['email']}, {'Company': ['domain'], 'Person': ['email']},
                                                      {'Company': ['company_id', 'domain']}])
        if rng.random() < 0.2:
            args['override'] = [candidate(300 + j) for j in range(rng.randint(0, 2))]
        fn = 'resolve_extracted_nodes'
        if rng.random() < 0.15:
            fn = 'collect_candidate_nodes'
            if rng.random() < 0.6:
                args['reuse_name_embeddings'] = True
        cases.append({
            'fn': fn,
            'clients': {'provider': rng.choice(['neo4j', 'neo4j', 'neptune']),
                        'embedder': rng.choice(['batch', 'batch', 'batch', 'single', 'short']),
                        'max_tokens': rng.choice([0, 4096, 16384, 32768]),
                        'max_coroutines': rng.choice([None, 1, 2])},
            'llm': {'dedupe_nodes.nodes': [resolution_reply(len(nodes))]},
            'exact': exact,
            'identity': identity,
            'identity_mode': rng.choice(['returns', 'returns', 'raise', 'not_implemented']),
            'search': search,
            'args': args,
        })
    # Exact name, fuzzy name, and the LLM answer for one node each, and a batch of more than 20 unresolved nodes.
    base = node_data('cand-1', 'Joe Michaels', labels=['Entity'])
    for name in ('Joe Michaels', 'Joe-Michaels', 'Joe'):
        cases.append({'fn': 'resolve_extracted_nodes', 'clients': {}, 'exact': [], 'identity': {}, 'search': {name: [base]},
                      'llm': {'dedupe_nodes.nodes': [{'entity_resolutions': [{'id': 0, 'name': name, 'duplicate_candidate_id': 0}]}]},
                      'args': {'nodes': [node_data('ex-1', name, labels=['Entity', 'Person'])], 'episode': episode_data(1),
                               'previous': []}})
    many = [node_data(f'm-{j}', f'Jo{j % 3}', labels=['Entity']) for j in range(24)]
    cases.append({'fn': 'resolve_extracted_nodes', 'clients': {'max_coroutines': 3}, 'exact': [], 'identity': {},
                  'search': {n['name']: [node_data(f'mc-{j}', n['name'] + 'x')] for j, n in enumerate(many)},
                  'llm': {'dedupe_nodes.nodes': [resolution_reply(24)]},
                  'args': {'nodes': many, 'episode': episode_data(1), 'previous': []}})
    # resolve_nodes_with_candidates with given candidates.
    for i in range(10):
        nodes = [node_data(f'w{i}-{j}', labels=['Entity', 'Person'], attributes={'email': 'a@x.io'}) for j in range(rng.randint(1, 4))]
        candidates = [[node_data(f'wc{i}-{j}-{k}', labels=['Entity', 'Person'], attributes={'email': rng.choice(['a@x.io', 'b@x.io'])})
                       for k in range(rng.randint(0, 3))] for j in range(len(nodes))]
        cases.append({'fn': 'resolve_nodes_with_candidates', 'clients': {}, 'exact': [], 'identity': {}, 'search': {},
                      'llm': {'dedupe_nodes.nodes': [resolution_reply(len(nodes))]},
                      'args': {'nodes': nodes, 'candidates': candidates, 'identity_properties': rng.choice([None, {'Person': ['email']}]),
                               'episode': episode_data(1)}})
    return cases


def summary_reply(names):
    summaries = [{'name': rng.choice([n, n.upper(), 'Unknown entity with a long name of more than thirty']),
                  'summary': rng.choice(['Short.', rep('S. ', 600), 'A summary 東京 😀.'])} for n in names if rng.random() < 0.8]
    return {'summaries': summaries}


def attribute_reply(spec):
    reply = {}
    for f in spec['fields']:
        if rng.random() < 0.7:
            reply[f['name']] = rng.choice([None, 'x', 'a' * 50, 3, ['a', 'b'], 'new@x.io'])
    if rng.random() < 0.2:
        reply['unknown'] = 'u'
    return reply


def attribute_cases():
    cases = []
    for i in range(60):
        count = rng.randint(0, 6) if rng.random() < 0.9 else 33
        nodes = []
        for j in range(count):
            labels = ['Entity'] + rng.sample(['Person', 'Company', 'Topic', 'Place', 'Patient'], rng.randint(0, 2))
            summary = rng.choice(['', 'Old summary.', rep('X ', 1100), rep('A summary near the limit. ', 70)])
            nodes.append(node_data(f'n{i}-{j}', name=rng.choice(['Alice', 'alice', 'Bob', 'Topic', '', 'Zoë', 'Node' + str(j)]),
                                   labels=labels, summary=summary,
                                   attributes=rng.choice([{}, {'email': 'old@x.io', 'age': 30}, {'city': 'Paris'}])))
        edges = []
        for j in range(rng.randint(0, 4)):
            if nodes:
                edges.append({'uuid': f'e{i}-{j}', 'group_id': 'g', 'source_node_uuid': rng.choice(nodes)['uuid'],
                              'target_node_uuid': rng.choice(nodes + [{'uuid': 'other'}])['uuid'], 'name': 'REL',
                              'fact': rng.choice(['', 'Alice works with Bob.', rep('f', 1500)]),
                              'created_at': '2024-01-01T00:00:00+00:00'})
        fn = rng.choice(['extract_attributes_from_nodes', 'extract_attributes_from_nodes', 'extract_attributes_only_from_nodes'])
        args = {'nodes': nodes}
        types = pick_types()
        if fn == 'extract_attributes_from_nodes' and any(n for n in nodes):
            # The shape validation of a matching type waits for GraphTypeDescriptor.ModelValidate (see the docstring).
            if rng.random() < 0.5:
                args['skip_attribute_extraction'] = True
            else:
                types = {k: v for k, v in types.items() if not v['fields'] or all(k not in n['labels'] for n in nodes)}
        if rng.random() < 0.85:
            args['entity_types'] = types
        if rng.random() < 0.8:
            args['episode'] = rng.choice([episode_data(1), [episode_data(1), episode_data(2)], [episode_data(3)]])
        if rng.random() < 0.6:
            args['previous'] = [episode_data(5 + j) for j in range(rng.randint(0, 2))]
        if fn == 'extract_attributes_from_nodes':
            if edges or rng.random() < 0.2:
                args['edges'] = edges
            for key in ('skip_fact_appending', 'include_type_descriptions'):
                if rng.random() < 0.4:
                    args[key] = True
            if rng.random() < 0.3:
                args['summarize'] = rng.sample([n['name'] for n in nodes], rng.randint(0, len(nodes)))
        llm = {
            'extract_nodes.extract_summaries_batch': [summary_reply([n['name'] for n in nodes]) for _ in range(3)],
            'extract_nodes.extract_entity_summaries_from_episodes': [summary_reply([n['name'] for n in nodes]) for _ in range(3)],
            'extract_nodes.extract_attributes': [attribute_reply(TYPES[t]) for t in rng.choices(list(TYPES), k=8)],
        }
        cases.append({'fn': fn, 'clients': {'embedder': rng.choice(['batch', 'batch', 'batch', 'single']),
                                              'max_coroutines': rng.choice([None, 1, 2])},
                      'llm': llm, 'args': args})
    return cases


CALL_KINDS = {'llm', 'embed_create', 'embed_batch', 'get_by_names', 'get_by_attribute_values', 'node_similarity_search',
              'execute_query'}


def call_count(output) -> int:
    return sum(1 for e in output['events'] if e['kind'] in CALL_KINDS)


def cancel_scenarios(async_cases):
    """The cases that the tool runs again with a cancel: for each function the first two cases that end with no error and
    make from 1 to 12 calls, and each case with the key `cancel` (three identity lookups at the same time)."""
    chosen: dict[str, list[int]] = {}
    flagged = [i for i, case in enumerate(async_cases) if case['input'].get('cancel')]
    for index, case in enumerate(async_cases):
        output = case['output']
        if case['input'].get('cancel'):
            continue
        if output['error'] is None and 1 <= call_count(output) <= 12:
            chosen.setdefault(case['input']['fn'], [])
            if len(chosen[case['input']['fn']]) < 2:
                chosen[case['input']['fn']].append(index)
    return sorted(flagged + [i for indexes in chosen.values() for i in indexes])


def summary_case(nodes, replies, extra_args=None, **clients):
    """One extract_attributes_from_nodes case that runs only the summary batch, with the replies of the batch prompt."""
    args = {'nodes': nodes, 'episode': episode_data(1), 'previous': [], 'skip_attribute_extraction': True}
    args.update(extra_args or {})
    return {'fn': 'extract_attributes_from_nodes', 'clients': dict(clients),
            'llm': {'extract_nodes.extract_summaries_batch': [{'summaries': replies}]}, 'args': args}


def limit_cases():
    """Inputs on a limit (R25): each value that a comparison of the module uses, on both sides of the limit."""
    cases = []
    # A summary of exactly 2 * MAX_SUMMARY_CHARS code points (some of them outside the BMP) stays as it is, and one more
    # code point goes to the LLM.
    exact = rep('a\U0001F600', 1000)
    cases.append(summary_case(
        [node_data('lim-0', 'Exact', summary=exact), node_data('lim-1', 'Over', summary=rep('b', 2001))],
        [{'name': 'Over', 'summary': 'Shortened.'}]))
    # A reply summary of exactly MAX_SUMMARY_CHARS characters with no sentence end, and one more or one less.
    cases.append(summary_case(
        [node_data('lim-2', 'Alpha'), node_data('lim-3', 'Beta'), node_data('lim-4', 'Gamma'), node_data('lim-5', 'Delta')],
        [{'name': 'Alpha', 'summary': rep('a', 1000)}, {'name': 'Beta', 'summary': rep('b', 1001)},
         {'name': 'Gamma', 'summary': rep('c', 999)}, {'name': 'Delta', 'summary': rep('One sentence. ', 71)}]))
    # Exactly 20 unresolved nodes go to the LLM, and the debug line lists the first 10 ids and the last 10.
    nodes = [node_data(f'twenty-{j}', f'Quux {j}', labels=['Entity']) for j in range(20)]
    cases.append({'fn': 'resolve_extracted_nodes', 'clients': {}, 'exact': [], 'identity': {},
                  'search': {n['name']: [node_data(f'twenty-c-{j}', n['name'] + 'x')] for j, n in enumerate(nodes)},
                  'llm': {'dedupe_nodes.nodes': [resolution_reply(20)]},
                  'args': {'nodes': nodes, 'episode': episode_data(1), 'previous': []}})
    return cases


def group_cases():
    """A summary flight whose nodes are in two groups: the LLM call gets the group of the first node."""
    cases = []
    for order in (('h', 'g'), ('g', 'h')):
        nodes = [node_data(f'grp-{order[0]}-{j}', f'Node {g}', group_id=g) for j, g in enumerate(order)]
        cases.append(summary_case(nodes, [{'name': f'Node {g}', 'summary': f'Summary of {g}.'} for g in order]))
    return cases


def lower_cases():
    """Names that change under full case mapping: the reply name matches the node name after str.lower()."""
    nodes = [node_data('low-0', '\u0130stanbul'), node_data('low-1', '\u039f\u0394\u039f\u03a3'),
             node_data('low-2', '\u1e9e'), node_data('low-3', '\u01c5x')]
    replies = [{'name': 'i\u0307stanbul', 'summary': 'A city.'}, {'name': '\u03bf\u03b4\u03bf\u03c2', 'summary': 'A road.'},
               {'name': '\u00df', 'summary': 'A letter.'}, {'name': '\u01c6x', 'summary': 'Titlecase.'}]
    return [summary_case(nodes, replies)]


def concurrency_cases():
    """Three identity lookups that run at the same time, and an exact-name lookup that is not implemented."""
    cases = []
    people = [node_data(f'idc-{j}', f'Person {j}', labels=['Entity', 'Person'], attributes={'email': f'p{j}@x.io'}) for j in range(3)]
    found = node_data('idc-found', 'Person 0 found', labels=['Entity', 'Person'], attributes={'email': 'p0@x.io'})
    cases.append({'fn': 'resolve_extracted_nodes', 'cancel': True, 'clients': {'max_coroutines': None}, 'exact': [],
                  'identity': {'Person': [found]}, 'identity_mode': 'returns', 'search': {},
                  'llm': {'dedupe_nodes.nodes': [{'entity_resolutions': [{'id': 0, 'name': 'Person 0', 'duplicate_candidate_id': -1}]}]},
                  'args': {'nodes': people, 'episode': episode_data(1), 'previous': [], 'identity_properties': {'Person': ['email']}}})
    # The execute_query path of EntityNode.get_by_names, for each provider that the module reaches.
    for provider in ('neo4j', 'neptune'):
        nodes = [node_data(f'ni-{provider}-{j}', f'Name {j}', labels=['Entity']) for j in range(2)]
        for fn in ('resolve_extracted_nodes', 'collect_candidate_nodes'):
            cases.append({'fn': fn, 'clients': {'provider': provider}, 'exact': '$not_implemented', 'identity': {}, 'search': {},
                          'llm': {'dedupe_nodes.nodes': [resolution_reply(2)]},
                          'args': {'nodes': nodes, 'episode': episode_data(1), 'previous': []}})
    return cases


def sync_cases(m):
    no = m.no
    records = []

    def call(name, fn, args, view=plain):
        try:
            result = {'ok': view(fn())}
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            result = {'err': [type(error).__name__, str(error)]}
        records.append({'f': name, 'a': args, 'r': result})

    for _ in range(120):
        text = ''.join(rng.choice(SENTENCE_PARTS) + rng.choice(['', ' ', ' ', '  ']) for _ in range(rng.randint(0, 9)))
        call('_find_sentence_end', lambda: no._find_sentence_end(text), [text])
        call('_truncate_type_description', lambda: no._truncate_type_description(text), [text])
    for doc in DOCS:
        if doc is not None:
            call('_truncate_type_description', lambda: no._truncate_type_description(doc), [doc])
    for _ in range(30):
        types = pick_types()
        call('_build_entity_types_context', lambda: no._build_entity_types_context(make_types(types, m)), [types])
        ctx = no._build_entity_types_context(make_types(types, m))
        if rng.random() < 0.2:
            ctx.append({'entity_type_id': 9})
        excluded = rng.choice([None, [], rng.sample(['Entity', 'Person', 'Company', 'Nope'], rng.randint(1, 3))])
        call('_filter_entity_types_context', lambda: no._filter_entity_types_context(ctx, excluded), [ctx, excluded])
        labels = rng.sample(['Entity', 'Person', 'Company', 'Topic', 'Place', 'Nope'], rng.randint(0, 3))
        call('_get_entity_type_description', lambda: no._get_entity_type_description(labels, make_types(types, m)), [labels, types])
        call('_entity_type_for_node',
             lambda: (lambda t: None if t is None else t.__name__)(no._entity_type_for_node(m.EntityNode(**node_data('n', labels=labels or ['Entity'])), make_types(types, m))),
             [labels or ['Entity'], types])
    for i in range(40):
        ctx = no._filter_entity_types_context(no._build_entity_types_context(make_types(pick_types(), m)),
                                              rng.choice([None, ['Entity'], ['Person']]))
        entities = [{'name': rng.choice(NAMES), 'entity_type_id': rng.choice([-1, 0, 1, 2, 3, 9]),
                     'episode_indices': rng.sample([-2, 0, 1, 2, 5], rng.randint(0, 3))} for _ in range(rng.randint(0, 5))]
        excluded = rng.choice([None, [], ['Entity'], ['Person', 'Company']])
        episodes = [episode_data(j) for j in range(rng.randint(0, 3))]
        call('_create_entity_nodes',
             lambda: no._create_entity_nodes([m.ExtractedEntity(**e) for e in entities], ctx, excluded,
                                             [m.EpisodicNode(**e) for e in episodes]),
             [entities, ctx, excluded, episodes])
    for i in range(40):
        nodes = [node_data(f'd{i}-{j}', name=rng.choice(['Alice', 'alice', ' ALICE ', 'Bob', 'bob', 'Al', 'al ', 'a   l', 'Café', 'CAFÉ']),
                           labels=['Entity'] + rng.sample(['Person', 'Place'], rng.randint(0, 2))) for j in range(rng.randint(0, 6))]
        index_map = None
        if rng.random() < 0.7:
            index_map = {n['uuid']: rng.sample([0, 1, 2, 3], rng.randint(0, 3)) for n in nodes if rng.random() < 0.9}

        def collapse(nodes=nodes, index_map=index_map):
            mapping = copy.deepcopy(index_map)
            result = no._collapse_exact_duplicate_extracted_nodes([m.EntityNode(**d) for d in nodes], mapping)
            return [[n.uuid for n in result], mapping]

        call('_collapse_exact_duplicate_extracted_nodes', collapse, [nodes, index_map])
    for i in range(20):
        first = [node_data(f'u{rng.randint(0, 5)}') for _ in range(rng.randint(0, 5))]
        override = rng.choice([None, [], [node_data(f'u{rng.randint(0, 7)}') for _ in range(rng.randint(1, 3))]])
        call('_merge_candidate_nodes',
             lambda: [n.uuid + '/' + n.name for n in no._merge_candidate_nodes(
                 [m.EntityNode(**d) for d in first], None if override is None else [m.EntityNode(**d) for d in override])],
             [first, override])
    for i in range(15):
        edges = [{'uuid': f'e{j}', 'group_id': 'g', 'source_node_uuid': rng.choice(['a', 'b', 'c']),
                  'target_node_uuid': rng.choice(['a', 'b', 'd']), 'name': 'R', 'fact': 'f',
                  'created_at': '2024-01-01T00:00:00+00:00'} for j in range(rng.randint(0, 5))]
        edges_arg = None if rng.random() < 0.2 else edges
        call('_build_edges_by_node',
             lambda: {k: [e.uuid for e in v] for k, v in no._build_edges_by_node(
                 None if edges_arg is None else [m.EntityEdge(**e) for e in edges_arg]).items()},
             [edges_arg])
    for i in range(15):
        episode = rng.choice([None, episode_data(1), [], [episode_data(1)], [episode_data(1), episode_data(2)]])
        previous = rng.choice([None, [], [episode_data(3)]])
        node = {'name': 'n', 'entity_types': ['Entity'], 'attributes': {'a': 1}}
        call('_build_episode_context',
             lambda: no._build_episode_context(node, episode_arg(episode, m),
                                               None if previous is None else [m.EpisodicNode(**e) for e in previous]),
             [node, episode, previous])
    return records


# ---------------------------------------------------------------- main

class Names:
    pass


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    os.environ.pop('GRAPHITI_ATTRIBUTE_MAX_LENGTH', None)
    from pydantic import ConfigDict

    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.driver.graph_operations.graph_operations import GraphOperationsInterface
    from graphiti_core.driver.search_interface.search_interface import SearchInterface
    from graphiti_core.edges import EntityEdge
    from graphiti_core.embedder.client import EmbedderClient
    from graphiti_core.graphiti_types import GraphitiClients
    from graphiti_core.llm_client import LLMClient
    from graphiti_core.llm_client.config import LLMConfig, ModelSize
    from graphiti_core.nodes import EntityNode, EpisodicNode
    from graphiti_core.prompts.extract_nodes import ExtractedEntity
    from graphiti_core.utils.maintenance import attribute_utils, dedup_helpers, node_operations

    sys.path.insert(0, str(Path(__file__).resolve().parent))
    from record_maintenance_helpers import InsertionOrderSet

    # The two sets that dedup_helpers and attribute_utils iterate, in insertion order (RULES.md R14), as the recorder
    # of packet P7a binds them.
    dedup_helpers.set = InsertionOrderSet
    attribute_utils.set = InsertionOrderSet

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
    node_operations.EntityNode = OrderedLabelsEntityNode
    node_operations.utc_now = lambda: FIXED_NOW

    m = Names()
    for name, value in {
        'GraphProvider': GraphProvider, 'GraphOperationsInterface': GraphOperationsInterface,
        'SearchInterface': SearchInterface, 'EntityEdge': EntityEdge, 'EmbedderClient': EmbedderClient,
        'GraphitiClients': GraphitiClients, 'LLMClient': LLMClient, 'LLMConfig': LLMConfig, 'ModelSize': ModelSize,
        'EntityNode': EntityNode, 'EpisodicNode': EpisodicNode, 'ExtractedEntity': ExtractedEntity,
        'no': node_operations,
    }.items():
        setattr(m, name, value)

    class Capture(logging.Handler):
        def emit(self, record):
            if CURRENT['events'] is None:
                return
            entry = {'kind': 'log', 'level': record.levelname, 'message': MS.sub(' in <ms> ms', record.getMessage())}
            extra = {k: getattr(record, k) for k in EXTRA_KEYS if hasattr(record, k)}
            if extra:
                entry['extra'] = extra
            if record.exc_info:
                entry['exc'] = record.exc_info[0].__name__
            CURRENT['events'].append(entry)

    root = logging.getLogger('graphiti_core')
    root.setLevel(logging.DEBUG)
    root.propagate = False
    root.addHandler(Capture(logging.DEBUG))

    constants = {'MAX_NODES': node_operations.MAX_NODES,
                 'NODE_DEDUP_COSINE_MIN_SCORE': node_operations.NODE_DEDUP_COSINE_MIN_SCORE}

    sync = sync_cases(m)
    async_inputs = extract_cases() + resolve_cases() + attribute_cases()
    # The cases of the review round come last, so that the index of each earlier case stays.
    async_inputs += limit_cases() + group_cases() + lower_cases() + concurrency_cases()
    async_cases = []
    for case in async_inputs:
        output = call_async(m, case)
        CURRENT['events'] = None
        async_cases.append({'input': case, 'output': normalize_uuids(output)})
    for index in cancel_scenarios(async_cases):
        case = async_cases[index]
        runs = []
        for k in range(-1, call_count(case['output']) - 1):
            output = call_async(m, async_inputs[index], cancel_at=k)
            CURRENT['events'] = None
            runs.append(normalize_uuids({'k': k, 'error': output['error']['type'], 'attempts': output['attempts'],
                                         'count': output['count'], 'after': output['after']}))
        case['cancel'] = runs
    sync = [dict(r, r=normalize_uuids(r['r'])) for r in sync]

    def dumps(value):
        return json.dumps(value, ensure_ascii=True, allow_nan=False, separators=(',', ':'))

    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","fixed_now":"' + FIXED_NOW.isoformat() + '",'
        + '"constants":' + dumps(constants) + ',"types":' + dumps(TYPES) + ',"cases":[\n'
        + ',\n'.join(dumps(r) for r in sync) + '\n],"async_cases":[\n' + ',\n'.join(dumps(r) for r in async_cases) + '\n]}\n'
    )
    size = len(text.encode('utf-8'))
    if size > 1_572_864:
        raise SystemExit(f'The values file would have {size} bytes, above the limit of 1,572,864 (R25).')
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(sync)} cases and {len(async_cases)} async cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(sync)} cases and {len(async_cases)} async cases, {size} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
