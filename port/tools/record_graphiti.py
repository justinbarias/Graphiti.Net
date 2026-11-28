"""Records results of graphiti_core/graphiti.py from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_graphiti.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_graphiti.py --check

Plan 0004, layer 9, packet P9a. The tool has two parts.

The sync part calls the module functions _validate_saga_group, _saga_summary_messages_utf8_bytes, and
select_saga_summary_episodes, and the members of `Graphiti` that do not await: __init__ (each argument left out and
given, and each branch that raises), token_tracker, _resolve_request_scope, and _concurrent_ingestion_enabled. It also
holds the module constants.

The async part builds a `Graphiti` with fakes at the network boundary and calls each other method with
`concurrent_ingestion` off: close, build_indices_and_constraints, retrieve_episodes, _validate_saga_scope,
_get_or_create_saga, _find_saga, _saga_get_previous_episode_uuid, _saga_get_episode_contents, summarize_saga,
_extract_and_resolve_nodes, add_episode (and so _extract_and_resolve_edges, _merge_materialized_endpoint_nodes, and
_process_episode_data), add_episode_bulk (and so _extract_and_dedupe_nodes_bulk and _resolve_nodes_and_edges_bulk),
_resolve_nodes_and_edges_bulk with hydrate=False, build_communities, search, _search, search_,
get_nodes_and_edges_by_episode, add_triplet, and remove_episode. The fakes:

- The graph driver derives from GraphDriver. execute_query records the query (its SHA-256, its length in code points,
  and its first 80 characters with the white space joined), the parameter keys in order, and each parameter value. It
  answers with the rows of the first entry of the case table `rows` whose needle is in the query and whose parameter
  values (an optional third item) the call has. Each event of the driver, the session, and the transaction holds the
  database of its driver, so a test sees the driver that a call used. The session records
  execute_write (with the `timeout` of unit_of_work), run, and close, and the transaction records each run with its
  parameters. clone records the database and gives a new fake driver for it. The case can give the driver a graph
  operations interface (`ops`) that answers retrieve_episodes, saga_get_previous_episode_uuid, and
  saga_get_episode_contents, or raises NotImplementedError.
- The LLM client replaces generate_response. It records the prompt name, the response model name, the model size,
  max_tokens, group_id, attribute_extraction, and the role, the SHA-256, and the length of each message. It answers
  with the first reply of the case for the prompt name whose needle (if any) is in the last message, or with the
  fallback reply of the prompt name. A reply of `{"$raise": ...}` raises LookupError.
- The embedder gives the vector of the case table `vectors` for a text, or [i, 1.0, 0.0] for the i-th distinct text.
- The cross-encoder records the query and the passages and gives the passages in order with the scores 1.0, 0.9, ...
- The tracer records each span: its name, its attributes (a key with `duration` or `_ms` has the value "<ms>"), its
  status, its exception type, and its end.
- Each fake yields once (asyncio.sleep(0)) and then records its call. The calls, the spans, and the log lines are one
  list. Each case keeps the largest count of fake calls in flight.

Fixed values on both sides: each module of graphiti_core that has `utc_now` gives FIXED_NOW, and the C# test gives the
port a clock with the same time. time.time() is the system clock in Python, so a log line with "in <n> ms" has its
number replaced by "<ms>". A uuid4 that the code makes is random: the test replaces each one, in the order of its first
appearance in the case output, by "<new:N>". The telemetry call of __init__ is not ported (decision D9), so the tool
replaces capture_event with a function that does nothing. add_triplet merges labels with a set: the tool binds the
module name `set` of graphiti.py to a set that iterates `Entity` first and then in insertion order, the order of the
port (R14). The sets of bulk_utils, dedup_helpers, attribute_utils, and edge_operations, and the label lists of
node_operations and combined_extraction, have the orders that the recorders of layer 7 bind (R14). A uuid4 key of a dict
gets its placeholder too. An error text of pydantic loses `input_value`, `input_type`, and the link (R25).

The cancel scenarios: for each case with `cancel`, the recorder runs the case again for each fake call k. The fake that
makes call k records it and then cancels the main task. The recorder keeps the count of attempts, and the counted
calls and the log lines after call k. The last call of a case is a cancel point too. A log line keeps the fields of its
`extra`.

Paths that wait for packet P9b, because they enter ConcurrentIngestor (ingestion/scheduler.py is a skeleton in this
packet): _get_concurrent_ingestor, the call of ConcurrentIngestor.add_episode at the end of add_episode, and the call of
ConcurrentIngestor.add_episode_bulk at the end of add_episode_bulk. The checks before those calls are recorded: the
ValueError of update_communities, the reads of `uuid` and `previous_episode_uuids`, and the saga checks. The four
branches of __init__ that build a default object are not ported (R1, decisions D3 and D28): the cases hold the error
text of the port, and Python is not called for them.

It writes tests/Port.Evaluation/values/graphiti.json. --check writes nothing and exits 1 if the file differs from what
the run would write. The tool needs Python 3.12 and the pinned upstream in the folder of the variable GRAPHITI_UPSTREAM,
or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import copy
import hashlib
import json
import logging
import os
import re
import sys
import warnings
from contextlib import contextmanager
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'graphiti.json'

FIXED_NOW = datetime(2026, 3, 4, 5, 6, 7, 891011, tzinfo=timezone.utc)
T0 = datetime(2026, 1, 1, tzinfo=timezone.utc)
T1 = T0 + timedelta(days=10)
T2 = T0 + timedelta(days=20)
T3 = T0 + timedelta(days=30)
UUID4 = re.compile(r'[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}')
MS = re.compile(r'in [0-9.e+-]+ ms')
VALIDATION_INPUT = re.compile(r', input_value=.*?, input_type=\w+\]')

events: list[dict] = []
state: dict = {}


def dt(value):
    return None if value is None else {'$dt': value.isoformat()}


def encode(value):
    if isinstance(value, datetime):
        return dt(value)
    if isinstance(value, dict):
        return {str(k): encode(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [encode(v) for v in value]
    if hasattr(value, 'value') and type(value).__module__.startswith('graphiti_core'):
        return {'$enum': value.value}
    return value


def decode(value):
    if isinstance(value, dict):
        if set(value) == {'$dt'}:
            return datetime.fromisoformat(value['$dt'])
        return {k: decode(v) for k, v in value.items()}
    if isinstance(value, list):
        return [decode(v) for v in value]
    return value


def placeholders(value, known: dict):
    if isinstance(value, dict):
        # A key can be a uuid4 too (the uuid_map of a result). The key comes before its value.
        out = {}
        for k, v in value.items():
            key = placeholders(k, known)
            out[key] = placeholders(v, known)
        return out
    if isinstance(value, list):
        return [placeholders(v, known) for v in value]
    if isinstance(value, str):
        def repl(m):
            if m.group(0) not in known:
                known[m.group(0)] = f'<new:{len(known)}>'
            return known[m.group(0)]

        return UUID4.sub(repl, value)
    return value


def error_text(error) -> str:
    text = str(error)
    if type(error).__name__ == 'ValidationError':
        # ValidationException of the port does not carry the input value, the input type, and the link (R25).
        text = '\n'.join(line for line in VALIDATION_INPUT.sub(']', text).splitlines()
                         if 'For further information visit' not in line)
    return text


def query_view(query: str) -> dict:
    return {'sha': hashlib.sha256(query.encode('utf-8', 'surrogatepass')).hexdigest(), 'len': len(query),
            'head': ' '.join(query.split())[:80]}


# --- inputs --------------------------------------------------------------------------------------------------------


def node(uuid, name, emb=None, labels=None, group_id='g', summary='', attributes=None):
    return {'uuid': uuid, 'name': name, 'group_id': group_id, 'labels': labels or ['Entity'], 'created_at': dt(T0),
            'name_embedding': emb, 'summary': summary, 'attributes': attributes or {}}


def node_row(uuid, name, emb=None, labels=None, group_id='g', summary='', attributes=None):
    row = node(uuid, name, emb if emb is not None else [0.5, 0.5, 0.0], labels, group_id, summary, attributes)
    return row


def edge(uuid, source, target, fact, emb=None, group_id='g', episodes=None, name='RELATES', valid_at=None,
         invalid_at=None, attributes=None):
    return {'uuid': uuid, 'group_id': group_id, 'source_node_uuid': source, 'target_node_uuid': target,
            'name': name, 'fact': fact, 'created_at': dt(T0), 'fact_embedding': emb, 'episodes': episodes or [],
            'expired_at': None, 'valid_at': dt(valid_at), 'invalid_at': dt(invalid_at), 'attributes': attributes or {}}


def edge_row(uuid, source, target, fact, emb=None, **extra):
    return edge(uuid, source, target, fact, emb if emb is not None else [0.0, 1.0, 0.0], **extra)


def episode(uuid='ep1', valid_at=T0, content='content', group_id='g', entity_edges=None, name=None, source='text'):
    return {'uuid': uuid, 'name': name or uuid, 'group_id': group_id, 'source': source, 'source_description': 'test',
            'content': content, 'valid_at': dt(valid_at), 'created_at': dt(valid_at),
            'entity_edges': entity_edges or []}


def saga(uuid='saga1', group_id='g', first=None, last=None, summary='', last_summarized_at=None,
         last_valid_at=None):
    return {'uuid': uuid, 'name': 'Saga One', 'group_id': group_id, 'created_at': dt(T0), 'summary': summary,
            'first_episode_uuid': first, 'last_episode_uuid': last, 'last_summarized_at': dt(last_summarized_at),
            'last_summarized_episode_valid_at': dt(last_valid_at), 'labels': ['Saga']}


def raw(name, content, ref=T0, uuid=None, source='message'):
    return {'name': name, 'content': content, 'source_description': 'chat', 'source': source,
            'reference_time': dt(ref), 'uuid': uuid}


def entities(*names, type_id=0):
    return {'extracted_entities': [{'name': n, 'entity_type_id': type_id} for n in names]}


def facts(*items):
    return {'edges': [{'source_entity_name': s, 'target_entity_name': t, 'relation_type': r, 'fact': f,
                       'valid_at': None, 'invalid_at': None} for s, t, r, f in items]}


def summaries(*items):
    return {'summaries': [{'name': n, 'summary': s} for n, s in items]}


def resolutions(*items):
    return {'entity_resolutions': [{'id': i, 'name': name, 'duplicate_candidate_id': dup} for i, name, dup in items]}


def edge_reply(duplicates=(), contradicted=()):
    return {'duplicate_facts': list(duplicates), 'contradicted_facts': list(contradicted)}


def combined(names, edges_):
    return {'extracted_entities': [{'name': n, 'entity_type_id': 0} for n in names],
            'edges': [{'source_entity_name': s, 'target_entity_name': t, 'relation_type': r, 'fact': f,
                       'valid_at': None, 'invalid_at': None, 'episode_indices': [0]} for s, t, r, f in edges_]}


TYPES = {
    'Person': {'doc': 'A human being.', 'fields': [['role', False, None], ['email', False, None]]},
    'Company': {'doc': 'A business.', 'fields': [['industry', False, None]]},
    'Works': {'doc': 'Employment.', 'fields': [['since', False, None]]},
}

FALLBACK = {
    'extract_nodes.extract_message': {'extracted_entities': []},
    'extract_nodes.extract_text': {'extracted_entities': []},
    'extract_nodes.extract_json': {'extracted_entities': []},
    'extract_nodes_and_edges.extract_message': {'extracted_entities': [], 'edges': []},
    'extract_edges.edge': {'edges': []},
    'dedupe_nodes.nodes': {'entity_resolutions': []},
    'dedupe_edges.resolve_edge': {'duplicate_facts': [], 'contradicted_facts': []},
    'extract_edges.extract_attributes': {},
    'extract_nodes.extract_attributes': {},
    'extract_edges.extract_timestamps': {'valid_at': None, 'invalid_at': None},
    'extract_edges.extract_timestamps_batch': {'timestamps': []},
    'extract_nodes.extract_summaries_batch': {'summaries': []},
    'extract_nodes.extract_entity_summaries_from_episodes': {'summaries': []},
    'summarize_sagas.summarize_saga': {'summary': 'saga summary'},
    'summarize_nodes.summarize_pair': {'summary': 'pair summary'},
    'summarize_nodes.summary_description': {'description': 'community'},
}

# Needles of the queries that the cases answer.
EPISODES_BEFORE = 'RETURN DISTINCT'
EPISODE_GET = 'MATCH (e:Episodic {uuid: $uuid})'
EPISODES_GET = 'MATCH (e:Episodic) WHERE e.uuid IN $uuids'
SAGA_GET = 'MATCH (s:Saga {uuid: $uuid})'
SAGA_FIND = 'MATCH (s:Saga {name: $name, group_id: $group_id})'
SAGA_PREVIOUS = 'WHERE e.uuid <> $current_episode_uuid'
SAGA_CONTENTS = 'RETURN e.content AS content'
EDGE_SEARCH = 'RELATES_TO'
ENTITY_GET = 'MATCH (n:Entity {uuid: $uuid})'
EDGE_GET = 'MATCH (n:Entity)-[e:RELATES_TO {uuid: $uuid}]->(m:Entity)'
EDGES_GET = 'WHERE e.uuid IN $uuids'
MENTIONS = 'MENTIONS'
EPISODE_COUNT = 'AS episode_count'
NODE_SEARCH = 'vector.similarity.cosine(n.name_embedding'


# --- the sync part -------------------------------------------------------------------------------------------------


def sync_cases() -> list[dict]:
    cases: list[dict] = []
    for saga_arg in (None, {'name': 'S'}, {'node': saga()}, {'node': saga(group_id='h')}):
        cases.append({'fn': '_validate_saga_group', 'saga': saga_arg, 'group_id': 'g'})
    for contents in ([], ['a'], ['abc', 'é', '😀x', ''], ['日本語' * 3, 'plain']):
        cases.append({'fn': '_saga_summary_messages_utf8_bytes', 'contents': contents})
    for data, max_chars in (
        ([['a' * 40, None, T0], ['b' * 40, None, T1], ['c' * 40, None, T2]], 90),
        ([['a' * 40, None, T0], ['b' * 50, None, T1], ['c' * 40, None, T2]], 90),
        ([['a' * 40, None, T0], ['b' * 50, None, T1]], 90),
        ([['a' * 40, None, T0], ['b' * 51, None, T1]], 90),
        ([['é' * 10, None, T0], ['a' * 10, None, T1]], 15),
        ([['é' * 10, T1, T0]], 7),
        ([['😀' * 5, None, T0]], 3),
        ([['abc', None, T0]], 0),
        ([['', None, T0], ['x', T2, T1]], 1),
        ([], 5),
        ([['a' * 5, T0, T1], ['b' * 5, None, T2]], None),
        # Review P9a (decision 7): the default budget, with a text exactly on it.
        ([['a' * 100_000, None, T0]], None),
    ):
        case = {'fn': 'select_saga_summary_episodes', 'data': [[c, dt(v), dt(cr)] for c, v, cr in data]}
        if max_chars is not None:
            case['max_episode_chars'] = max_chars
        cases.append(case)
    # __init__: each argument left out, then each one given.
    cases.append({'fn': '__init__', 'init': {}})
    cases.append({'fn': '__init__', 'init': {'store_raw_episode_content': False, 'max_coroutines': 3,
                                             'trace_span_prefix': 'custom', 'tracer': 'otel',
                                             'prompt_library': True, 'concurrent_ingestion': {'window': 2}}})
    cases.append({'fn': '__init__', 'init': {'llm_runtime': True}})
    cases.append({'fn': '__init__', 'init': {'llm_runtime': True, 'llm_client': True}})
    cases.append({'fn': '__init__', 'init': {'llm_runtime': True, 'prompt_library': True}})
    cases.append({'fn': '__init__', 'init': {'llm_runtime': True, 'llm_client': True, 'prompt_library': True}})
    cases.append({'fn': '__init__', 'init': {'graph_driver': False}})
    cases.append({'fn': '__init__', 'init': {'tracer': 'otel'}})
    # The four branches that build a default object are not ported (R1, D3, D28). The port throws ArgumentException.
    for missing, port_error in (
        ({'graph_driver': False, 'uri': 'bolt://localhost:7687'}, 'graph_driver must be provided'),
        ({'llm_client': False}, 'llm_client must be provided'),
        ({'embedder': False}, 'embedder must be provided'),
        ({'cross_encoder': False}, 'cross_encoder must be provided'),
    ):
        cases.append({'fn': '__init__', 'init': missing, 'port_only': {'type': 'ValueError', 'text': port_error}})
    for group_id, database in ((None, 'default_db'), ('tenant_a', 'tenant_a'), ('tenant_b', 'default_db'),
                               ('', 'default_db'), ('bad group!', 'default_db'), ('ok-group_1', 'default_db')):
        cases.append({'fn': '_resolve_request_scope', 'group_id': group_id, 'database': database})
    cases.append({'fn': '_resolve_request_scope', 'group_id': None, 'database': 'default_db', 'provider': 'falkordb'})
    for config, group_id in ((None, 'g'), ({}, 'g'), ({'enabled_for': ['g']}, 'g'), ({'enabled_for': ['g']}, 'h'),
                             ({'enabled_for': []}, '')):
        cases.append({'fn': '_concurrent_ingestion_enabled', 'config': config, 'group_id': group_id})
    return cases


def run_sync(case: dict, m) -> dict:
    g = m['graphiti_module']
    fn = case['fn']
    if fn == '_validate_saga_group':
        g._validate_saga_group(saga_arg(case['saga'], m), case['group_id'])
        return {}
    if fn == '_saga_summary_messages_utf8_bytes':
        from graphiti_core.prompts.models import Message
        messages = [Message(role='user', content=c) for c in case['contents']]
        return {'bytes': g._saga_summary_messages_utf8_bytes(messages)}
    if fn == 'select_saga_summary_episodes':
        data = [(c, decode(v), decode(cr)) for c, v, cr in case['data']]
        if 'max_episode_chars' in case:
            selected, size, dropped = g.select_saga_summary_episodes(data, max_episode_chars=case['max_episode_chars'])
        else:
            selected, size, dropped = g.select_saga_summary_episodes(data)
        return {'selected': [[c, dt(v), dt(cr)] for c, v, cr in selected], 'bytes': size, 'dropped': dropped}
    if fn == '__init__':
        run = Run(case, m)
        graphiti = run.make_graphiti(case['init'])
        return init_view(graphiti, run)
    if fn == '_resolve_request_scope':
        run = Run({'database': case['database'], 'provider': case.get('provider', 'neo4j')}, m)
        graphiti = run.make_graphiti({})
        group_id, driver, clients = graphiti._resolve_request_scope(case['group_id'])
        return {'group_id': group_id, 'database': driver._database, 'same_driver': driver is graphiti.driver,
                'same_clients': clients is graphiti.clients, 'clients_driver': clients.driver is driver,
                'shared_database': graphiti.driver._database, 'shared_clients_driver': graphiti.clients.driver is
                graphiti.driver, 'same_llm': clients.llm_client is graphiti.llm_client,
                'same_embedder': clients.embedder is graphiti.embedder, 'events': list(events)}
    if fn == '_concurrent_ingestion_enabled':
        run = Run({}, m)
        init = {} if case['config'] is None else {'concurrent_ingestion': case['config']}
        graphiti = run.make_graphiti(init)
        return {'enabled': graphiti._concurrent_ingestion_enabled(case['group_id'])}
    raise AssertionError(fn)


def init_view(graphiti, run) -> dict:
    clients = graphiti.clients
    tracer = graphiti.tracer
    return {
        'store_raw_episode_content': graphiti.store_raw_episode_content,
        'max_coroutines': graphiti.max_coroutines,
        'same_driver': graphiti.driver is run.driver,
        'same_llm': graphiti.llm_client is run.llm,
        'llm_is_runtime_client': graphiti.llm_runtime is not None and graphiti.llm_client is graphiti.llm_runtime.client,
        'has_runtime': graphiti.llm_runtime is not None,
        'prompt_library': None if graphiti.prompt_library is None else (
            'runtime' if graphiti.llm_runtime is not None and graphiti.prompt_library is graphiti.llm_runtime.library
            else 'wrapped'),
        'same_embedder': graphiti.embedder is run.embedder,
        'same_cross_encoder': graphiti.cross_encoder is run.cross_encoder,
        'tracer': type(tracer).__name__,
        'span_prefix': getattr(tracer, '_span_prefix', None),
        'llm_tracer': (graphiti.llm_runtime.client.tracer if graphiti.llm_runtime is not None
                       else graphiti.llm_client.tracer) is tracer,
        'clients': [clients.driver is graphiti.driver, clients.llm_client is graphiti.llm_client,
                    clients.embedder is graphiti.embedder, clients.cross_encoder is graphiti.cross_encoder,
                    clients.tracer is tracer, clients.prompt_library is graphiti.prompt_library,
                    clients.llm_runtime is graphiti.llm_runtime, clients.max_coroutines],
        'concurrent_ingestion': None if graphiti.concurrent_ingestion is None else graphiti.concurrent_ingestion.window,
        'namespaces': [type(graphiti.nodes).__name__, type(graphiti.edges).__name__],
        'token_tracker': graphiti.token_tracker is graphiti.llm_client.token_tracker,
    }


# --- the fakes -----------------------------------------------------------------------------------------------------


async def enter(kind: str, record: dict, counted: bool = True) -> None:
    case = state['case']
    state['attempts'] += 1
    state['in_flight'] += 1
    state['max_in_flight'] = max(state['max_in_flight'], state['in_flight'])
    try:
        await asyncio.sleep(0)
        events.append({'kind': kind, **record})
        if not counted:
            return
        state['count'] += 1
        if state['count'] - 1 == state['cancel_at']:
            state['task'].cancel()
        fails = case.get('fail', {})
        if fails.get(kind, 0) > 0:
            fails[kind] -= 1
            raise RuntimeError(f'{kind} failed')
    finally:
        state['in_flight'] -= 1


def saga_arg(value, m):
    if value is None:
        return None
    if 'name' in value:
        return value['name']
    return m['SagaNode'](**decode(value['node']))


class Run:
    """The fakes of one case."""

    def __init__(self, case: dict, m):
        self.case = case
        self.m = m
        self.texts: list[str] = []
        self.replies = copy.deepcopy(case.get('replies', {}))
        self.driver = self.make_driver(case.get('database', 'default_db'))
        self.llm = self.make_llm()
        self.embedder = self.make_embedder()
        self.cross_encoder = self.make_cross_encoder()

    def vector(self, text):
        if text in self.case.get('vectors', {}):
            return list(self.case['vectors'][text])
        if text not in self.texts:
            self.texts.append(text)
        return [float(self.texts.index(text)), 1.0, 0.0]

    def make_driver(self, database):
        m = self.m
        run = self
        GraphDriver = m['GraphDriver']
        GraphDriverSession = m['GraphDriverSession']
        provider_value = m['GraphProvider'](self.case.get('provider', 'neo4j'))

        class FakeTx:
            def __init__(self, db):
                self._database = db

            async def run(self, query, **kwargs):
                await enter('tx_run', {'database': self._database, 'query': query_view(query),
                                       'keys': list(kwargs), 'values': encode(list(kwargs.values()))})

        class FakeSession(GraphDriverSession):
            provider = provider_value

            def __init__(self, db):
                self._database = db

            async def __aexit__(self, exc_type, exc, tb):
                pass

            async def run(self, query, **kwargs):
                await enter('session_run', {'database': self._database, 'query': query_view(query),
                                            'keys': list(kwargs), 'values': encode(list(kwargs.values()))})

            async def execute_write(self, func, *args, **kwargs):
                await enter('execute_write', {'database': self._database, 'timeout': getattr(func, 'timeout', None)})
                return await func(FakeTx(self._database), *args, **kwargs)

            async def close(self):
                await enter('session_close', {}, counted=False)

        class FakeDriver(GraphDriver):
            provider = provider_value

            def __init__(self, db):
                self._database = db
                self.graph_operations_interface = run.make_ops()

            async def execute_query(self, cypher_query_, **kwargs):
                await enter('query', {'database': self._database, 'query': query_view(cypher_query_),
                                      'keys': list(kwargs), 'values': encode(list(kwargs.values()))})
                # The needle is found in the query with its white space joined.
                normalized = ' '.join(cypher_query_.split())
                # A third item of an entry gives parameter values that the call must have too.
                for entry in run.case.get('rows', []):
                    needle, rows = entry[0], entry[1]
                    wanted = entry[2] if len(entry) > 2 else {}
                    if needle in normalized and all(kwargs.get(k) == v for k, v in wanted.items()):
                        return decode(copy.deepcopy(rows)), None, None
                return [], None, None

            def session(self, database=None):
                events.append({'kind': 'session', 'database': self._database})
                return FakeSession(self._database)

            async def close(self):
                await enter('driver_close', {'database': self._database})

            async def delete_all_indexes(self):
                raise AssertionError('not used')

            async def build_indices_and_constraints(self, delete_existing: bool = False):
                await enter('build_indices', {'delete_existing': delete_existing, 'database': self._database})

            def clone(self, database):
                events.append({'kind': 'clone', 'database': database})
                return FakeDriver(database)

        return FakeDriver(database)

    def make_ops(self):
        spec = self.case.get('ops')
        if spec is None:
            return None
        m = self.m

        class FakeOps(m['GraphOperationsInterface']):
            async def retrieve_episodes(self, driver, reference_time, last_n=3, group_ids=None, source=None,
                                        saga=None):
                await enter('ops:retrieve_episodes', {'reference_time': dt(reference_time), 'last_n': last_n,
                                                      'group_ids': group_ids, 'source': encode(source), 'saga': saga,
                                                      'database': driver._database})
                if 'retrieve_episodes' not in spec:
                    raise NotImplementedError
                return [m['EpisodicNode'](**decode(e)) for e in spec['retrieve_episodes']]

            async def saga_get_previous_episode_uuid(self, driver, saga_uuid, current_episode_uuid):
                await enter('ops:saga_get_previous_episode_uuid', {'saga_uuid': saga_uuid,
                                                                   'current_episode_uuid': current_episode_uuid})
                if 'previous' not in spec:
                    raise NotImplementedError
                return spec['previous']

            async def saga_get_episode_contents(self, driver, saga_uuid, since=None, limit=200):
                await enter('ops:saga_get_episode_contents', {'saga_uuid': saga_uuid, 'since': dt(since),
                                                              'limit': limit})
                if 'contents' not in spec:
                    raise NotImplementedError
                return [(c, decode(v), decode(cr)) for c, v, cr in spec['contents']]

        return FakeOps()

    def make_llm(self):
        from graphiti_core.llm_client.config import ModelSize
        run = self

        class FakeLlm(self.m['LLMClient']):
            def __init__(self):
                super().__init__(None, cache=False)

            async def _generate_response(self, messages, response_model=None, max_tokens=0,
                                         model_size=ModelSize.medium, model=None):
                raise AssertionError('not used')

            async def generate_response(self, messages, response_model=None, max_tokens=None,
                                        model_size=ModelSize.medium, group_id=None, prompt_name=None, *,
                                        attribute_extraction=False, model=None):
                await enter('llm', {
                    'prompt_name': prompt_name,
                    'response_model': None if response_model is None else response_model.__name__,
                    'max_tokens': max_tokens, 'model_size': model_size.value, 'group_id': group_id,
                    'attribute_extraction': attribute_extraction,
                    'messages': [[msg.role, hashlib.sha256(msg.content.encode('utf-8', 'surrogatepass')).hexdigest(),
                                  len(msg.content)] for msg in messages],
                })
                last = messages[-1].content if messages else ''
                queue = run.replies.get(prompt_name) or []
                for i, item in enumerate(queue):
                    if item.get('needle') is None or item['needle'] in last:
                        queue.pop(i)
                        reply = item['reply']
                        if isinstance(reply, dict) and '$raise' in reply:
                            raise LookupError(reply['$raise'])
                        return copy.deepcopy(reply)
                if prompt_name in FALLBACK:
                    return copy.deepcopy(FALLBACK[prompt_name])
                raise LookupError(f'no reply for {prompt_name}')

        return FakeLlm()

    def make_embedder(self):
        run = self

        class FakeEmbedder(self.m['EmbedderClient']):
            async def create(self, input_data):
                await enter('embed', {'input': list(input_data)})
                return run.vector(input_data[0])

            async def create_batch(self, input_data_list):
                await enter('embed_batch', {'input': list(input_data_list)})
                return [run.vector(t) for t in input_data_list]

        return FakeEmbedder()

    def make_cross_encoder(self):
        class FakeCrossEncoder(self.m['CrossEncoderClient']):
            async def rank(self, query, passages):
                await enter('rank', {'query': query, 'passages': list(passages)})
                return [(p, 1.0 - 0.1 * i) for i, p in enumerate(passages)]

        return FakeCrossEncoder()

    def make_graphiti(self, init: dict):
        m = self.m
        kwargs = {}
        if init.get('graph_driver', True):
            kwargs['graph_driver'] = self.driver
        if 'uri' in init:
            kwargs['uri'] = init['uri']
        if init.get('llm_runtime'):
            transport = m['LLMTransport'](self.llm)
            kwargs['llm_runtime'] = m['LLMRuntime'](transport.model('model-1'))
            if init.get('llm_client'):
                kwargs['llm_client'] = self.llm
        elif init.get('llm_client', True):
            kwargs['llm_client'] = self.llm
        if init.get('embedder', True):
            kwargs['embedder'] = self.embedder
        if init.get('cross_encoder', True):
            kwargs['cross_encoder'] = self.cross_encoder
        for key in ('store_raw_episode_content', 'max_coroutines', 'trace_span_prefix'):
            if key in init:
                kwargs[key] = init[key]
        if init.get('tracer') == 'otel':
            from opentelemetry import trace
            kwargs['tracer'] = trace.get_tracer('graphiti-recorder')
        if init.get('prompt_library'):
            kwargs['prompt_library'] = m['create_prompt_library']({})
        if 'concurrent_ingestion' in init:
            config = {'window': 2, **init['concurrent_ingestion']}
            names = config.pop('enabled_for', None)
            if names is not None:
                config['enabled_for_group'] = lambda group_id: group_id in names
            if config.pop('store', False):
                config['state_store'] = RecordingStore(m)
            kwargs['concurrent_ingestion'] = m['ConcurrentIngestionConfig'](**config)
        return m['Graphiti'](**kwargs)


class RecordingStore:
    """An in-memory state store whose close is recorded."""

    def __new__(cls, m):
        class Store(m['InMemoryIngestionStateStore']):
            async def close(self):
                await enter('store_close', {})

        return Store()


class RecSpan:
    def add_attributes(self, attributes):
        events.append({'kind': 'span_attrs', 'attrs': [
            [k, '<ms>' if ('duration' in k or k.endswith('_ms')) else encode(v)] for k, v in attributes.items()]})

    def set_status(self, status, description=None):
        events.append({'kind': 'span_status', 'status': status,
                       'description': None if description is None else error_text_from(description)})

    def record_exception(self, exception):
        events.append({'kind': 'span_exception', 'type': type(exception).__name__})


def error_text_from(text: str) -> str:
    return '\n'.join(line for line in VALIDATION_INPUT.sub(']', text).splitlines()
                     if 'For further information visit' not in line)


def make_tracer(m):
    class RecTracer(m['Tracer']):
        @contextmanager
        def start_span(self, name):
            events.append({'kind': 'span', 'name': name})
            try:
                yield RecSpan()
            finally:
                events.append({'kind': 'span_end', 'name': name})

    return RecTracer()


class EntityFirstSet(set):
    """A set that iterates `Entity` first and then in insertion order, the order of the port (RULES.md R14)."""

    def __init__(self, items=()):
        super().__init__()
        self._order = []
        for item in items:
            self.add(item)

    def add(self, item):
        if item not in self:
            super().add(item)
            self._order.append(item)

    def __or__(self, other):
        result = EntityFirstSet(self._order)
        for item in (other._order if isinstance(other, EntityFirstSet) else other):
            result.add(item)
        return result

    def __iter__(self):
        if 'Entity' in self._order:
            yield 'Entity'
        for item in self._order:
            if item != 'Entity':
                yield item


# --- the async cases -----------------------------------------------------------------------------------------------


def nodes_reply_cases() -> list[dict]:
    """Cases of add_episode."""
    cases = []
    ep_rows = [episode('prev1', T0 - timedelta(days=1), 'earlier talk'), episode('prev2', T0 - timedelta(days=2))]

    def add(name, args=None, **extra):
        a = {'name': 'Episode One', 'episode_body': 'Alice met Bob at Acme.', 'source_description': 'chat',
             'reference_time': dt(T1)}
        a.update(args or {})
        case = {'fn': 'add_episode', 'name': name, 'args': a}
        case.update(extra)
        cases.append(case)

    add('no_entity', cancel=True)
    add('entities_and_edges', {'group_id': 'g'}, rows=[[EPISODES_BEFORE, ep_rows]],
        replies={'extract_nodes.extract_message': [{'reply': entities('Alice', 'Bob')}],
                 'extract_edges.edge': [{'reply': facts(('Alice', 'Bob', 'KNOWS', 'Alice knows Bob'))}],
                 'extract_nodes.extract_summaries_batch': [{'reply': summaries(('Alice', 'Alice met Bob.'))}]},
        cancel=True)
    add('previous_episode_uuids', {'group_id': 'g', 'previous_episode_uuids': ['prev1', 'prev2']},
        rows=[[EPISODES_GET, ep_rows]], replies={'extract_nodes.extract_message': [{'reply': entities('Alice')}]})
    add('previous_episode_uuids_missing', {'group_id': 'g', 'previous_episode_uuids': ['prev1', 'gone']},
        rows=[[EPISODES_GET, ep_rows[:1]]])
    add('saga_name_new', {'group_id': 'g', 'saga': {'name': 'Trip'}, 'source': 'text'})
    add('saga_name_existing', {'group_id': 'g', 'saga': {'name': 'Saga One'}},
        rows=[[SAGA_FIND, [saga(first='old1', last='old2')]], [SAGA_PREVIOUS, [{'uuid': 'old2'}]]])
    add('saga_node', {'group_id': 'g', 'saga': {'node': saga()}},
        rows=[[SAGA_GET, [saga(summary='stored')]]])
    add('saga_node_not_stored', {'group_id': 'g', 'saga': {'node': saga(first='e0')}})
    add('saga_node_other_group', {'group_id': 'g', 'saga': {'node': saga(group_id='h')}})
    add('saga_node_stored_other_group', {'group_id': 'g', 'saga': {'node': saga()}},
        rows=[[SAGA_GET, [saga(group_id='h')]]])
    add('saga_previous_given', {'group_id': 'g', 'saga': {'name': 'Trip'}, 'saga_previous_episode_uuid': 'prev1'},
        rows=[[EPISODE_GET, ep_rows[:1]]])
    add('saga_previous_missing', {'group_id': 'g', 'saga': {'name': 'Trip'}, 'saga_previous_episode_uuid': 'gone'})
    add('custom_types', {'group_id': 'g', 'entity_types': ['Person', 'Company'], 'edge_types': ['Works'],
                         'edge_type_map': [[['Person', 'Company'], ['Works']]],
                         'excluded_entity_types': ['Company'], 'custom_extraction_instructions': 'Be brief.'},
        replies={'extract_nodes.extract_message': [{'reply': {'extracted_entities': [
            {'name': 'Alice', 'entity_type_id': 1}, {'name': 'Acme', 'entity_type_id': 2},
            {'name': 'Bob', 'entity_type_id': 0}]}}],
            'extract_edges.edge': [{'reply': facts(('Alice', 'Bob', 'KNOWS', 'Alice knows Bob'))}],
            'extract_nodes.extract_attributes': [{'reply': {'role': 'engineer', 'email': None}}]})
    add('edge_types_no_map', {'group_id': 'g', 'edge_types': ['Works']},
        replies={'extract_nodes.extract_message': [{'reply': entities('Alice', 'Bob')}],
                 'extract_edges.edge': [{'reply': facts(('Alice', 'Bob', 'Works', 'Alice works with Bob'))}]})
    add('excluded_entity', {'group_id': 'g', 'excluded_entity_types': ['Entity']},
        replies={'extract_nodes.extract_message': [{'reply': entities('Alice')}]})
    add('excluded_unknown', {'group_id': 'g', 'excluded_entity_types': ['Nope']})
    add('entity_type_bad_field', {'group_id': 'g', 'entity_types': ['Bad']})
    add('identity_properties', {'group_id': 'g', 'entity_types': ['Person'],
                                'identity_properties': {'Person': ['email']}},
        replies={'extract_nodes.extract_message': [{'reply': {'extracted_entities': [
            {'name': 'Alice', 'entity_type_id': 1}]}}],
            'extract_nodes.extract_attributes': [{'reply': {'role': None, 'email': 'a@x.org'}},
                                                 {'reply': {'role': 'cto', 'email': 'a@x.org'}}]})
    add('identity_properties_unknown_type', {'group_id': 'g', 'identity_properties': {'Person': ['email']}})
    add('update_communities_one_node', {'group_id': 'g', 'update_communities': True},
        replies={'extract_nodes.extract_message': [{'reply': entities('Alice')}]})
    add('update_communities_two_nodes', {'group_id': 'g', 'update_communities': True},
        replies={'extract_nodes.extract_message': [{'reply': entities('Alice', 'Bob')}]})
    add('update_communities_no_node', {'group_id': 'g', 'update_communities': True})
    add('own_uuid', {'group_id': 'g', 'uuid': 'ep-own'},
        rows=[[EPISODE_GET, [episode('ep-own', T1, 'stored body')]]],
        replies={'extract_nodes.extract_text': [{'reply': entities('Alice')}]})
    add('own_uuid_missing', {'group_id': 'g', 'uuid': 'ep-gone'})
    stored_edge = edge_row('e-old', 'n-alice', 'n-bob', 'Alice works at Acme', valid_at=T0, episodes=['prev1'])
    add('contradiction', {'group_id': 'g'},
        rows=[[ENTITY_GET, []], [NODE_SEARCH, [node_row('n-alice', 'Alice'), node_row('n-bob', 'Bob')]],
              [EDGE_SEARCH, [stored_edge]]],
        replies={'extract_nodes.extract_message': [{'reply': entities('Alice', 'Bob')}],
                 'extract_edges.edge': [{'reply': facts(('Alice', 'Bob', 'WORKS_AT', 'Alice left Acme'))}],
                 'dedupe_edges.resolve_edge': [{'reply': edge_reply(contradicted=[0])}],
                 'extract_edges.extract_timestamps': [{'reply': {'valid_at': '2026-01-11T00:00:00Z',
                                                                 'invalid_at': None}}]},
        cancel=True)
    add('materialized_endpoint', {'group_id': 'g'},
        replies={'extract_nodes.extract_message': [{'reply': entities('Alice')}],
                 'extract_edges.edge': [{'reply': facts(('Alice', 'Carol', 'MET', 'Alice met Carol'),
                                                         ('alice', 'Dave', 'MET', 'Alice met Dave'))}]})
    add('store_raw_off', {'group_id': 'g'}, init={'store_raw_episode_content': False},
        replies={'extract_nodes.extract_message': [{'reply': entities('Alice')}]})
    add('other_database', {'group_id': 'tenant'},
        replies={'extract_nodes.extract_message': [{'reply': entities('Alice')}]})
    add('default_group', {}, provider='neo4j')
    add('bad_group', {'group_id': 'bad group'})
    add('llm_fails', {'group_id': 'g'},
        replies={'extract_nodes.extract_message': [{'reply': {'$raise': 'model down'}}]})
    add('save_fails', {'group_id': 'g'}, fail={'execute_write': 1})
    # Review P9a (decision 6): the valid_at of the new fact is not the reference time, so the old edge gets it as its
    # invalid_at.
    add('contradiction_later_fact', {'group_id': 'g'},
        rows=[[ENTITY_GET, []], [NODE_SEARCH, [node_row('n-alice', 'Alice'), node_row('n-bob', 'Bob')]],
              [EDGE_SEARCH, [stored_edge]]],
        replies={'extract_nodes.extract_message': [{'reply': entities('Alice', 'Bob')}],
                 'extract_edges.edge': [{'reply': facts(('Alice', 'Bob', 'WORKS_AT', 'Alice left Acme'))}],
                 'dedupe_edges.resolve_edge': [{'reply': edge_reply(contradicted=[0])}],
                 'extract_edges.extract_timestamps': [{'reply': {'valid_at': '2026-01-15T00:00:00Z',
                                                                 'invalid_at': None}}]})
    # Review P9a (decision 8): extraction gives Bob, resolution maps Bob to the stored node Robert, and an edge names
    # Robert. The materialized Robert matches the resolved node by its name.
    robert = [[NODE_SEARCH, [node_row('n-robert', 'Robert')]]]
    bob_is_robert = {'reply': resolutions((0, 'Bob', 0))}
    add('materialized_name_of_resolved', {'group_id': 'g'}, rows=robert,
        replies={'extract_nodes.extract_message': [{'reply': entities('Bob')}],
                 'dedupe_nodes.nodes': [bob_is_robert],
                 'extract_edges.edge': [{'reply': facts(('Robert', 'Robert', 'IS', 'Robert is Robert'),
                                                         ('Bob', 'Robert', 'KNOWS', 'Bob knows Robert'))}]})
    # The materialized Bobby resolves to the stored Robert, whose uuid the resolved nodes already hold.
    add('materialized_resolves_to_known', {'group_id': 'g'}, rows=robert,
        replies={'extract_nodes.extract_message': [{'reply': entities('Bob')}],
                 'dedupe_nodes.nodes': [bob_is_robert, {'reply': resolutions((0, 'Bobby', 0))}],
                 'extract_edges.edge': [{'reply': facts(('Bob', 'Bobby', 'KNOWS', 'Bob knows Bobby'))}]})
    # An edge names an endpoint whose name is the uuid of a resolved node. The materialized node is not that node: its
    # uuid is new, so it goes through resolution. A materialized node with a known uuid (graphiti.py:1024) cannot
    # happen, because extract_edges gives each one a new uuid4.
    add('materialized_name_is_known_uuid', {'group_id': 'g'}, rows=robert,
        replies={'extract_nodes.extract_message': [{'reply': entities('Bob')}],
                 'dedupe_nodes.nodes': [bob_is_robert],
                 'extract_edges.edge': [{'reply': facts(('Bob', 'n-robert', 'KNOWS', 'Bob knows n-robert'))}]})
    # Review P9a (decision 1): a group that is not the default database, with a saga.
    add('saga_name_other_database', {'group_id': 'tenant', 'saga': {'name': 'Trip'}},
        rows=[[SAGA_PREVIOUS, [{'uuid': 'old9'}]]],
        replies={'extract_nodes.extract_message': [{'reply': entities('Alice')}]})
    add('saga_node_other_database', {'group_id': 'tenant', 'saga': {'node': saga(group_id='tenant')}},
        rows=[[SAGA_GET, [saga(group_id='tenant')]]])
    add('concurrent_update_communities', {'group_id': 'g', 'update_communities': True},
        init={'concurrent_ingestion': {}})
    add('concurrent_saga_other_group', {'group_id': 'g', 'saga': {'node': saga(group_id='h')}},
        init={'concurrent_ingestion': {}})
    add('concurrent_uuid_missing', {'group_id': 'g', 'uuid': 'gone'}, init={'concurrent_ingestion': {}})
    add('concurrent_previous_missing', {'group_id': 'g', 'previous_episode_uuids': ['gone']},
        init={'concurrent_ingestion': {}})
    add('concurrent_other_group_off', {'group_id': 'h'}, init={'concurrent_ingestion': {'enabled_for': ['g']}})
    return cases


def bulk_cases() -> list[dict]:
    cases = []

    def bulk(name, episodes, args=None, **extra):
        a = {'bulk_episodes': episodes}
        a.update(args or {})
        case = {'fn': 'add_episode_bulk', 'name': name, 'args': a}
        case.update(extra)
        cases.append(case)

    bulk('one', [raw('b1', 'Alice met Bob.')], {'group_id': 'g'},
         replies={'extract_nodes_and_edges.extract_message': [
             {'reply': combined(['Alice', 'Bob'], [('Alice', 'Bob', 'MET', 'Alice met Bob')])}]}, cancel=True)
    bulk('several', [raw('b1', 'Alice met Bob.', T1), raw('b2', 'Bob met Carol.', T0), raw('b3', 'Quiet day.', T2)],
         {'group_id': 'g'}, init={'max_coroutines': 2},
         replies={'extract_nodes_and_edges.extract_message': [
             {'needle': 'Alice met Bob', 'reply': combined(['Alice', 'Bob'], [('Alice', 'Bob', 'MET', 'Alice met Bob')])},
             {'needle': 'Bob met Carol', 'reply': combined(['Bob', 'Carol'], [('Bob', 'Carol', 'MET', 'Bob met Carol')])}]},
         cancel=True)
    bulk('separate_extraction', [raw('b1', 'Alice met Bob.', source='text')],
         {'group_id': 'g', 'use_combined_extraction': False, 'strict_edge_types': True, 'edge_types': ['Works'],
          'custom_extraction_instructions': 'Keep it short.'},
         replies={'extract_nodes.extract_text': [{'reply': entities('Alice', 'Bob')}],
                  'extract_edges.edge': [{'reply': facts(('Alice', 'Bob', 'Works', 'Alice works with Bob'),
                                                          ('Alice', 'Bob', 'MET', 'Alice met Bob'))}]})
    bulk('saga_name', [raw('b1', 'Day one.', T1), raw('b2', 'Day two.', T2)], {'group_id': 'g', 'saga': {'name': 'Trip'}},
         rows=[[SAGA_PREVIOUS, [{'uuid': 'old9'}]]])
    bulk('saga_name_existing', [raw('b1', 'Day one.', T1)], {'group_id': 'g', 'saga': {'name': 'Saga One'}},
         rows=[[SAGA_FIND, [saga(first='old1', last='old2')]]])
    bulk('saga_node', [raw('b1', 'Day one.', T1)], {'group_id': 'g', 'saga': {'node': saga()}},
         rows=[[SAGA_GET, [saga()]]])
    bulk('saga_node_other_group', [raw('b1', 'Day one.', T1)], {'group_id': 'g', 'saga': {'node': saga(group_id='h')}})
    bulk('with_uuid', [raw('b1', 'ignored', T1, uuid='ep-own'), raw('b2', 'New one.', T2)], {'group_id': 'g'},
         rows=[[EPISODE_GET, [episode('ep-own', T1, 'stored body')]]])
    bulk('with_uuid_missing', [raw('b1', 'ignored', T1, uuid='ep-gone')], {'group_id': 'g'})
    # Review P9a (decision 4): each episode has an edge, so the combined extraction keeps its nodes.
    bulk('custom_types_identity', [raw('b1', 'Alice joined Acme.'), raw('b2', 'Alice left Acme.', T1)],
         {'group_id': 'g', 'entity_types': ['Person', 'Company'], 'excluded_entity_types': ['Company'],
          'identity_properties': {'Person': ['email']}, 'edge_type_map': [[['Person', 'Company'], ['Works']]],
          'edge_types': ['Works']},
         replies={'extract_nodes_and_edges.extract_message': [
             {'needle': 'joined', 'reply': {
                 'extracted_entities': [{'name': 'Alice', 'entity_type_id': 1}, {'name': 'Bob', 'entity_type_id': 0}],
                 'edges': [{'source_entity_name': 'Alice', 'target_entity_name': 'Bob', 'relation_type': 'KNOWS',
                            'fact': 'Alice knows Bob', 'valid_at': None, 'invalid_at': None,
                            'episode_indices': [0]}]}},
             {'needle': 'left', 'reply': {
                 'extracted_entities': [{'name': 'Alice', 'entity_type_id': 1}, {'name': 'Bob', 'entity_type_id': 0}],
                 'edges': [{'source_entity_name': 'Alice', 'target_entity_name': 'Bob', 'relation_type': 'MET',
                            'fact': 'Alice met Bob', 'valid_at': None, 'invalid_at': None,
                            'episode_indices': [0]}]}}],
             'extract_nodes.extract_attributes': [{'reply': {'role': None, 'email': 'a@x.org'}},
                                                  {'reply': {'role': None, 'email': 'a@x.org'}},
                                                  {'reply': {'role': 'cto', 'email': 'a@x.org'}}]})
    # Review P9a (decision 9): two episodes resolve to one stored edge. The second episode has two facts that both
    # resolve to it, so the resolved edges hold its uuid two times for that episode, and entity_edges holds it once.
    stored = edge_row('e-old', 'n-alice', 'n-bob', 'Alice knows Bob', episodes=['old-ep'])
    to_stored = {'needle': 'Alice knows Bob', 'reply': edge_reply(duplicates=[0])}
    bulk('two_episodes_one_stored_edge', [raw('b1', 'Alice met Bob.', T1), raw('b2', 'Alice saw Bob.', T2)],
         {'group_id': 'g'},
         rows=[[NODE_SEARCH, [node_row('n-alice', 'Alice'), node_row('n-bob', 'Bob')]], [EDGE_SEARCH, [stored]]],
         replies={'extract_nodes_and_edges.extract_message': [
             {'needle': 'met', 'reply': combined(['Alice', 'Bob'], [('Alice', 'Bob', 'MET', 'Alice met Bob')])},
             {'needle': 'saw', 'reply': combined(['Alice', 'Bob'], [('Alice', 'Bob', 'SAW', 'Alice saw Bob'),
                                                                    ('Alice', 'Bob', 'GREETED', 'Alice greeted Bob')])}],
             'dedupe_edges.resolve_edge': [to_stored, to_stored, to_stored]})
    # Review P9a (decision 7): a contradiction in the bulk path (S48, S74).
    bulk('contradiction', [raw('b1', 'Alice left Acme.', T1)], {'group_id': 'g'},
         rows=[[NODE_SEARCH, [node_row('n-alice', 'Alice'), node_row('n-bob', 'Bob')]],
               [EDGE_SEARCH, [edge_row('e-old', 'n-alice', 'n-bob', 'Alice works at Acme', valid_at=T0,
                                       episodes=['prev1'])]]],
         replies={'extract_nodes_and_edges.extract_message': [
             {'reply': combined(['Alice', 'Bob'], [('Alice', 'Bob', 'WORKS_AT', 'Alice left Acme')])}],
             'dedupe_edges.resolve_edge': [{'reply': edge_reply(contradicted=[0])}],
             'extract_edges.extract_timestamps_batch': [{'reply': {'timestamps': [
                 {'index': 0, 'valid_at': '2026-01-15T00:00:00Z', 'invalid_at': None}]}}]})
    # The saga chain follows valid_at, not the order of the input (S70).
    bulk('saga_out_of_order', [raw('b1', 'Day three.', T3), raw('b2', 'Day one.', T1), raw('b3', 'Day two.', T2)],
         {'group_id': 'g', 'saga': {'name': 'Trip'}})
    # Review P9a (decision 1): a group that is not the default database, with a saga.
    bulk('saga_name_other_database', [raw('b1', 'Day one.', T1)], {'group_id': 'tenant', 'saga': {'name': 'Trip'}},
         rows=[[SAGA_PREVIOUS, [{'uuid': 'old9'}]]])
    bulk('saga_node_other_database', [raw('b1', 'Day one.', T1)],
         {'group_id': 'tenant', 'saga': {'node': saga(group_id='tenant')}},
         rows=[[SAGA_GET, [saga(group_id='tenant')]]])
    bulk('empty', [], {'group_id': 'g'})
    bulk('default_group', [raw('b1', 'Hi.')], {})
    bulk('llm_fails', [raw('b1', 'Hi.')], {'group_id': 'g'},
         replies={'extract_nodes_and_edges.extract_message': [{'reply': {'$raise': 'model down'}}]})
    bulk('concurrent_saga_other_group', [raw('b1', 'Hi.')], {'group_id': 'g', 'saga': {'node': saga(group_id='h')}},
         init={'concurrent_ingestion': {}})
    bulk('concurrent_uuid_missing', [raw('b1', 'Hi.', uuid='gone')], {'group_id': 'g'},
         init={'concurrent_ingestion': {}})
    bulk('concurrent_bad_types', [raw('b1', 'Hi.')], {'group_id': 'g', 'entity_types': ['Bad']},
         init={'concurrent_ingestion': {}})
    bulk('concurrent_off_for_group', [raw('b1', 'Hi.')], {'group_id': 'h'},
         init={'concurrent_ingestion': {'enabled_for': ['g']}})
    return cases


def other_cases() -> list[dict]:
    cases = []
    ep_rows = [episode('prev1', T0, 'earlier talk', entity_edges=['e1', 'e2']),
               episode('prev2', T1, 'later talk', group_id='h', entity_edges=['e3'])]
    cases += [
        {'fn': 'close', 'name': 'close'},
        {'fn': 'close', 'name': 'close_store', 'init': {'concurrent_ingestion': {'store': True}}},
        {'fn': 'close', 'name': 'close_no_store', 'init': {'concurrent_ingestion': {}}},
        {'fn': 'build_indices_and_constraints', 'name': 'build_default', 'args': {}},
        {'fn': 'build_indices_and_constraints', 'name': 'build_delete', 'args': {'delete_existing': True}},
        {'fn': 'retrieve_episodes', 'name': 'retrieve_default', 'args': {'reference_time': dt(T1)},
         'rows': [[EPISODES_BEFORE, ep_rows]]},
        {'fn': 'retrieve_episodes', 'name': 'retrieve_all_args',
         'args': {'reference_time': dt(T1), 'last_n': 5, 'group_ids': ['g'], 'source': 'message', 'saga': 'Trip'}},
        {'fn': 'retrieve_episodes', 'name': 'retrieve_ops', 'args': {'reference_time': dt(T1), 'group_ids': ['g']},
         'ops': {'retrieve_episodes': ep_rows[:1]}},
        {'fn': 'retrieve_episodes', 'name': 'retrieve_ops_not_implemented', 'args': {'reference_time': dt(T1)},
         'ops': {}},
        {'fn': 'retrieve_episodes', 'name': 'retrieve_driver_given', 'args': {'reference_time': dt(T1),
                                                                             'driver': 'other'}},
        {'fn': 'retrieve_episodes', 'name': 'retrieve_falkor_groups', 'provider': 'falkordb',
         'args': {'reference_time': dt(T1), 'group_ids': ['a', 'b']}, 'rows': [[EPISODES_BEFORE, ep_rows[:1]]]},
        {'fn': 'retrieve_episodes', 'name': 'retrieve_falkor_one_group', 'provider': 'falkordb',
         'args': {'reference_time': dt(T1), 'group_ids': ['default_db']}},
        {'fn': '_validate_saga_scope', 'name': 'scope_none', 'args': {'saga': None}},
        {'fn': '_validate_saga_scope', 'name': 'scope_name', 'args': {'saga': {'name': 'Trip'}}},
        {'fn': '_validate_saga_scope', 'name': 'scope_stored', 'args': {'saga': {'node': saga()}},
         'rows': [[SAGA_GET, [saga()]]]},
        {'fn': '_validate_saga_scope', 'name': 'scope_not_stored', 'args': {'saga': {'node': saga()}}},
        {'fn': '_validate_saga_scope', 'name': 'scope_stored_other', 'args': {'saga': {'node': saga()}},
         'rows': [[SAGA_GET, [saga(group_id='h')]]]},
        {'fn': '_validate_saga_scope', 'name': 'scope_object_other', 'args': {'saga': {'node': saga(group_id='h')}}},
        {'fn': '_get_or_create_saga', 'name': 'saga_existing', 'args': {'saga_name': 'my-saga', 'group_id': 'g1'},
         'rows': [[SAGA_FIND, [{'uuid': 'saga-1', 'name': 'my-saga', 'group_id': 'g1',
                                'created_at': '2026-08-13T00:00:00+00:00', 'first_episode_uuid': 'ep-1',
                                'last_episode_uuid': 'ep-2'}]]]},
        {'fn': '_get_or_create_saga', 'name': 'saga_absent', 'args': {'saga_name': 'new-saga', 'group_id': 'g1'}},
        {'fn': '_get_or_create_saga', 'name': 'saga_absent_driver', 'args': {'saga_name': 'new-saga', 'group_id': 'g1',
                                                                            'driver': 'other'}},
        {'fn': '_find_saga', 'name': 'find_bad_row', 'args': {'saga_name': 'x', 'group_id': 'g'},
         'rows': [[SAGA_FIND, [{'uuid': 'saga-1', 'name': None, 'group_id': 'g', 'created_at': None,
                                'first_episode_uuid': None, 'last_episode_uuid': None}]]]},
        {'fn': '_saga_get_previous_episode_uuid', 'name': 'previous_query',
         'args': {'saga_uuid': 's1', 'current_episode_uuid': 'ep2', 'group_id': 'g'},
         'rows': [[SAGA_PREVIOUS, [{'uuid': 'ep1'}]]]},
        {'fn': '_saga_get_previous_episode_uuid', 'name': 'previous_none',
         'args': {'saga_uuid': 's1', 'current_episode_uuid': 'ep2', 'group_id': 'g'}},
        {'fn': '_saga_get_previous_episode_uuid', 'name': 'previous_ops',
         'args': {'saga_uuid': 's1', 'current_episode_uuid': 'ep2', 'group_id': 'g'}, 'ops': {'previous': 'ep1'},
         'rows': [[EPISODE_GET, [episode('ep1')]]]},
        {'fn': '_saga_get_previous_episode_uuid', 'name': 'previous_ops_missing_episode',
         'args': {'saga_uuid': 's1', 'current_episode_uuid': 'ep2', 'group_id': 'g'}, 'ops': {'previous': 'ep1'}},
        {'fn': '_saga_get_previous_episode_uuid', 'name': 'previous_ops_none',
         'args': {'saga_uuid': 's1', 'current_episode_uuid': 'ep2', 'group_id': 'g'}, 'ops': {'previous': None}},
        {'fn': '_saga_get_previous_episode_uuid', 'name': 'previous_ops_not_implemented',
         'args': {'saga_uuid': 's1', 'current_episode_uuid': 'ep2', 'group_id': 'g', 'driver': 'other'}, 'ops': {}},
        {'fn': '_saga_get_episode_contents', 'name': 'contents_no_ops', 'args': {'saga_uuid': 's1'}},
        {'fn': '_saga_get_episode_contents', 'name': 'contents_ops',
         'args': {'saga_uuid': 's1', 'since': dt(T0), 'limit': 3},
         'ops': {'contents': [['a', dt(T0), dt(T1)], ['b', None, dt(T2)]]}},
        {'fn': '_saga_get_episode_contents', 'name': 'contents_ops_not_implemented', 'args': {'saga_uuid': 's1'},
         'ops': {}},
        {'fn': '_extract_and_resolve_nodes', 'name': 'extract_resolve_one',
         'args': {'episode': episode('ep1', content='Alice met Bob.'), 'previous_episodes': [episode('p0', T0)],
                  'entity_types': None, 'excluded_entity_types': None},
         'replies': {'extract_nodes.extract_text': [{'reply': entities('Alice', 'Bob')}]}},
        {'fn': '_extract_and_resolve_nodes', 'name': 'extract_resolve_many_identity',
         'args': {'episode': [episode('ep1', content='Alice met Bob.'), episode('ep2', T1, 'Bob left.')],
                  'previous_episodes': [], 'entity_types': ['Person'], 'excluded_entity_types': None,
                  'identity_properties': {'Person': ['email']}},
         'replies': {'extract_nodes.extract_text': [{'reply': {'extracted_entities': [
             {'name': 'Alice', 'entity_type_id': 1, 'episode_indices': [0, 1]}]}}],
             'extract_nodes.extract_attributes': [{'reply': {'role': None, 'email': 'a@x.org'}}]}},
        {'fn': '_resolve_nodes_and_edges_bulk', 'name': 'resolve_bulk_no_hydrate',
         'args': {'nodes_by_episode': {'ep1': [node('n1', 'Alice')], 'ep2': [node('n1', 'Alice'), node('n2', 'Bob')]},
                  'edges_by_episode': {'ep1': [edge('e1', 'n1', 'n2', 'Alice knows Bob', [0.0, 1.0, 0.0],
                                                    episodes=['ep1'])], 'ep2': []},
                  'episode_context': [[episode('ep1'), []], [episode('ep2', T1), [episode('ep1')]]],
                  'entity_types': ['Person'], 'edge_types': None,
                  'edge_type_map': [[['Entity', 'Entity'], []]], 'episodes': [episode('ep1'), episode('ep2', T1)],
                  'identity_properties': {'Person': ['email']}, 'hydrate': False}},
        {'fn': '_resolve_nodes_and_edges_bulk', 'name': 'resolve_bulk_identity_hydrate',
         'args': {'nodes_by_episode': {'ep1': [node('n1', 'Alice', labels=['Entity', 'Person'],
                                                    attributes={'email': 'a@x.org'})],
                                       'ep2': [node('n1', 'Alice', labels=['Entity', 'Person'],
                                                    attributes={'email': 'a@x.org'}), node('n2', 'Bob')]},
                  'edges_by_episode': {'ep1': [], 'ep2': []},
                  'episode_context': [[episode('ep1', T1), []], [episode('ep2', T0), [episode('p0')]]],
                  'entity_types': ['Person'], 'edge_types': ['Works'],
                  'edge_type_map': [[['Entity', 'Entity'], ['Works']]],
                  'episodes': [episode('ep1', T1), episode('ep2', T0)],
                  'identity_properties': {'Person': ['email']}, 'hydrate': True}},
        {'fn': 'build_communities', 'name': 'communities_none', 'args': {}},
        {'fn': 'build_communities', 'name': 'communities_groups', 'args': {'group_ids': ['g'], 'driver': 'other'}},
        {'fn': 'build_communities', 'name': 'communities_falkor', 'provider': 'falkordb',
         'args': {'group_ids': ['a', 'b']}},
        {'fn': 'search', 'name': 'search_default', 'args': {'query': 'Alice'},
         'rows': [[EDGE_SEARCH, [edge_row('e1', 'n1', 'n2', 'Alice knows Bob')]]], 'cancel': True},
        {'fn': 'search', 'name': 'search_center', 'args': {'query': 'Alice', 'center_node_uuid': 'n1',
                                                          'group_ids': ['g'], 'num_results': 3,
                                                          'search_filter': {'edge_types': ['KNOWS']}},
         'rows': [['UNWIND $node_uuids AS node_uuid', [{'uuid': 'n2', 'score': 1}]],
                  [EDGE_SEARCH, [edge_row('e1', 'n1', 'n2', 'Alice knows Bob'),
                                 edge_row('e2', 'n2', 'n3', 'Bob knows Carol')]]]},
        {'fn': 'search', 'name': 'search_blank', 'args': {'query': '   '}},
        {'fn': 'search', 'name': 'search_bad_group', 'args': {'query': 'Alice', 'group_ids': ['bad group']}},
        {'fn': 'search', 'name': 'search_falkor_groups', 'provider': 'falkordb',
         'args': {'query': 'Alice', 'group_ids': ['a', 'b']}},
        {'fn': '_search', 'name': 'private_search', 'args': {'query': 'Alice', 'config': 'EDGE_HYBRID_SEARCH_RRF',
                                                            'group_ids': ['g']},
         'rows': [[EDGE_SEARCH, [edge_row('e1', 'n1', 'n2', 'Alice knows Bob')]]]},
        {'fn': 'search_', 'name': 'search_default_config', 'args': {'query': 'Alice'},
         'rows': [['queryRelationships', [edge_row('e1', 'n1', 'n2', 'Alice knows Bob')]],
                  ['MATCH (n:Entity)-[e:RELATES_TO]->(m:Entity) WITH DISTINCT', [
                      edge_row('e1', 'n1', 'n2', 'Alice knows Bob'), edge_row('e2', 'n2', 'n3', 'Bob knows Carol')]],
                  [NODE_SEARCH, [node_row('n1', 'Alice')]]]},
        {'fn': 'search_', 'name': 'search_vector', 'args': {'query': 'Alice', 'config': 'NODE_HYBRID_SEARCH_RRF',
                                                           'group_ids': ['g'], 'query_vector': [0.0, 0.0, 1.0],
                                                           'search_filter': {'node_labels': ['Person']}}},
        {'fn': 'get_nodes_and_edges_by_episode', 'name': 'by_episode_no_group',
         'args': {'episode_uuids': ['prev1', 'prev2']},
         'rows': [[EPISODES_GET, ep_rows], [EDGES_GET, [edge_row('e1', 'n1', 'n2', 'f1'),
                                                        edge_row('e3', 'n1', 'n2', 'f3', group_id='h')]],
                  [MENTIONS, [node_row('n1', 'Alice'), node_row('n9', 'Zed', group_id='x')]]]},
        {'fn': 'get_nodes_and_edges_by_episode', 'name': 'by_episode_group', 'init': {'max_coroutines': 1},
         'args': {'episode_uuids': ['prev1', 'prev2', 'prev3'], 'group_id': 'g'},
         'rows': [[EPISODES_GET, [ep_rows[0], episode('prev2', T1, entity_edges=['e3']),
                                  episode('prev3', T2, entity_edges=['e4'])]],
                  [EDGES_GET, [edge_row('e1', 'n1', 'n2', 'f1')]], [MENTIONS, [node_row('n1', 'Alice')]]]},
        {'fn': 'get_nodes_and_edges_by_episode', 'name': 'by_episode_group_missing',
         'args': {'episode_uuids': ['prev1'], 'group_id': 'g'}},
    ]
    # Review P9a (decisions 1 and 7).
    two_nodes = [node_row('n1', 'Alice', summary='Alice works.'), node_row('n2', 'Bob', summary='Bob works.')]
    cases += [
        {'fn': 'retrieve_episodes', 'name': 'retrieve_ops_source',
         'args': {'reference_time': dt(T1), 'group_ids': ['g'], 'source': 'message'},
         'ops': {'retrieve_episodes': ep_rows[:1]}},
        {'fn': '_get_or_create_saga', 'name': 'saga_existing_driver',
         'args': {'saga_name': 'my-saga', 'group_id': 'g1', 'driver': 'other'},
         'rows': [[SAGA_FIND, [{'uuid': 'saga-1', 'name': 'my-saga', 'group_id': 'g1',
                                'created_at': '2026-08-13T00:00:00+00:00', 'first_episode_uuid': 'ep-1',
                                'last_episode_uuid': 'ep-2'}]]]},
        {'fn': '_extract_and_resolve_nodes', 'name': 'extract_resolve_identity_and_plain',
         'args': {'episode': episode('ep1', content='Alice met Bob.'), 'previous_episodes': [],
                  'entity_types': ['Person', 'Company'], 'excluded_entity_types': None,
                  'identity_properties': {'Person': ['email']}},
         'replies': {'extract_nodes.extract_text': [{'reply': {'extracted_entities': [
             {'name': 'Alice', 'entity_type_id': 1}, {'name': 'Acme', 'entity_type_id': 2}]}}],
             'extract_nodes.extract_attributes': [{'reply': {'role': None, 'email': 'a@x.org'}},
                                                  {'reply': {'role': None, 'email': 'b@x.org'}}]}},
        {'fn': '_resolve_nodes_and_edges_bulk', 'name': 'resolve_bulk_no_hydrate_identity',
         'args': {'nodes_by_episode': {'ep1': [node('n1', 'Alice', labels=['Entity', 'Person'],
                                                    attributes={'email': 'a@x.org'})],
                                       'ep2': [node('n1', 'Alice', labels=['Entity', 'Person'],
                                                    attributes={'email': 'a@x.org'}), node('n2', 'Bob')]},
                  'edges_by_episode': {'ep1': [], 'ep2': []},
                  'episode_context': [[episode('ep1'), []], [episode('ep2', T1), [episode('ep1')]]],
                  'entity_types': ['Person'], 'edge_types': None,
                  'edge_type_map': [[['Entity', 'Entity'], []]], 'episodes': [episode('ep1'), episode('ep2', T1)],
                  'identity_properties': {'Person': ['email']}, 'hydrate': False}},
        {'fn': '_resolve_nodes_and_edges_bulk', 'name': 'resolve_bulk_shared_edge',
         'args': {'nodes_by_episode': {'ep1': [node('n1', 'Alice'), node('n2', 'Bob')], 'ep2': [node('n1', 'Alice')]},
                  'edges_by_episode': {
                      'ep1': [edge('e1', 'n1', 'n2', 'Alice knows Bob', [0.0, 1.0, 0.0], episodes=['ep1'])],
                      'ep2': [edge('e1', 'n1', 'n2', 'Alice knows Bob', [0.0, 1.0, 0.0], episodes=['ep2']),
                              edge('e2', 'n1', 'n2', 'Alice likes Bob', [1.0, 1.0, 0.0], episodes=['ep2'])]},
                  'episode_context': [[episode('ep1'), []], [episode('ep2', T1), [episode('ep1')]]],
                  'entity_types': None, 'edge_types': None,
                  'edge_type_map': [[['Entity', 'Entity'], []]], 'episodes': [episode('ep1'), episode('ep2', T1)],
                  'identity_properties': None, 'hydrate': False}},
        {'fn': '_resolve_nodes_and_edges_bulk', 'name': 'resolve_bulk_contradiction',
         'rows': [[EDGE_SEARCH, [edge_row('e-old', 'n1', 'n2', 'Alice works at Acme', valid_at=T0)]]],
         'replies': {'dedupe_edges.resolve_edge': [{'reply': edge_reply(contradicted=[0])}]},
         'args': {'nodes_by_episode': {'ep1': [node('n1', 'Alice'), node('n2', 'Bob')]},
                  'edges_by_episode': {'ep1': [edge('e1', 'n1', 'n2', 'Alice left Acme', [0.0, 1.0, 0.0],
                                                    episodes=['ep1'], valid_at=T1)]},
                  'episode_context': [[episode('ep1', T1), []]],
                  'entity_types': None, 'edge_types': None,
                  'edge_type_map': [[['Entity', 'Entity'], []]], 'episodes': [episode('ep1', T1)],
                  'identity_properties': None, 'hydrate': False}},
        {'fn': 'build_communities', 'name': 'communities_built', 'args': {'group_ids': ['g'], 'driver': 'other'},
         'rows': [['MATCH (n:Entity) WHERE n.group_id IN $group_ids', two_nodes],
                  ['WITH count(e) AS count', [{'uuid': 'n2', 'count': 1}]],
                  ['MATCH (n:Entity) WHERE n.uuid IN $uuids', two_nodes]]},
        {'fn': '_search', 'name': 'private_search_center',
         'args': {'query': 'Alice', 'config': 'EDGE_HYBRID_SEARCH_NODE_DISTANCE', 'group_ids': ['g'],
                  'center_node_uuid': 'n1'},
         'rows': [['UNWIND $node_uuids AS node_uuid', [{'uuid': 'n2', 'score': 1}]],
                  [EDGE_SEARCH, [edge_row('e1', 'n1', 'n2', 'Alice knows Bob'),
                                 edge_row('e2', 'n2', 'n3', 'Bob knows Carol')]]]},
        {'fn': 'get_nodes_and_edges_by_episode', 'name': 'by_episode_third_group',
         'args': {'episode_uuids': ['prev1', 'prev2']},
         'rows': [[EPISODES_GET, ep_rows], [EDGES_GET, [edge_row('e1', 'n1', 'n2', 'f1'),
                                                        edge_row('e9', 'n1', 'n2', 'f9', group_id='x')]],
                  [MENTIONS, [node_row('n1', 'Alice')]]]},
        {'fn': 'get_nodes_and_edges_by_episode', 'name': 'by_episode_other_database',
         'args': {'episode_uuids': ['prev1'], 'group_id': 'tenant'},
         'rows': [[EPISODES_GET, [episode('prev1', T0, 'earlier talk', group_id='tenant', entity_edges=['e1'])]],
                  [EDGES_GET, [edge_row('e1', 'n1', 'n2', 'f1', group_id='tenant')]],
                  [MENTIONS, [node_row('n1', 'Alice', group_id='tenant')]]]},
    ]
    return cases


def triplet_cases() -> list[dict]:
    cases = []

    def triplet(name, source, edge_, target, args=None, **extra):
        a = {'source_node': source, 'edge': edge_, 'target_node': target}
        a.update(args or {})
        case = {'fn': 'add_triplet', 'name': name, 'args': a}
        case.update(extra)
        cases.append(case)

    alice = node('n-alice', 'Alice', summary='Alice summary', attributes={'role': 'cto'}, labels=['Entity', 'Person'])
    bob = node('n-bob', 'Bob')
    fact = edge('e-new', 'n-alice', 'n-bob', 'Alice manages Bob', name='MANAGES')
    triplet('new_nodes', alice, fact, bob, cancel=True)
    triplet('existing_nodes', alice, fact, bob,
            rows=[[ENTITY_GET, [node_row('n-alice', 'Alice', labels=['Entity', 'Engineer'],
                                         attributes={'team': 'core'})]]])
    triplet('source_other_group', node('n-x', 'X', group_id='h'), fact, bob)
    triplet('target_other_group', alice, fact, node('n-y', 'Y', group_id='h'))
    triplet('episode_other_group', alice, fact, bob, {'episode': episode('ep-h', group_id='h')})
    triplet('edge_episodes_missing', alice, edge('e-new', 'n-alice', 'n-bob', 'Alice manages Bob',
                                                 episodes=['ep-gone']), bob)
    triplet('edge_episodes_present', alice, edge('e-new', 'n-alice', 'n-bob', 'Alice manages Bob',
                                                  episodes=['ep1']), bob, {'episode': episode('ep1')},
            rows=[[EPISODES_GET, [episode('ep1')]]])
    triplet('stored_source_other_group', alice, fact, bob,
            rows=[[ENTITY_GET, [node_row('n-alice', 'Alice', group_id='h')]]])
    triplet('existing_edge_other_endpoints', alice, fact, bob,
            rows=[[EDGE_GET, [edge_row('e-new', 'n-carol', 'n-bob', 'Carol manages Bob')]]])
    triplet('existing_edge_other_group', alice, fact, bob,
            rows=[[EDGE_GET, [edge_row('e-new', 'n-alice', 'n-bob', 'Alice manages Bob', group_id='h')]]])
    triplet('existing_edge_same_endpoints', alice, fact, bob,
            rows=[[EDGE_GET, [edge_row('e-new', 'n-alice', 'n-bob', 'Alice manages Bob')]]])
    triplet('contradiction', alice, edge('e-new', 'n-alice', 'n-bob', 'Alice no longer manages Bob', valid_at=T2,
                                         name='MANAGES'), bob,
            rows=[[EDGE_GET, []], [EDGE_SEARCH, [edge_row('e-old', 'n-alice', 'n-bob', 'Alice manages Bob',
                                                          valid_at=T0),
                                                 edge_row('e-other', 'n-alice', 'n-carol', 'Alice manages Carol',
                                                          valid_at=T0)]]],
            replies={'dedupe_edges.resolve_edge': [{'reply': edge_reply(contradicted=[0])}]})
    triplet('identity_properties', node('n-alice', 'Alice', labels=['Entity', 'Person'],
                                        attributes={'email': 'a@x.org'}), fact, bob,
            {'identity_properties': {'Person': ['email']}})
    # Review P9a (decisions 2 and 7). The search gives an edge of another group between the two nodes: Python drops it.
    later = edge('e-new', 'n-alice', 'n-bob', 'Alice no longer manages Bob', valid_at=T2, name='MANAGES')
    triplet('foreign_candidate', node('n-alice', 'Alice'), later, bob,
            rows=[[EDGE_GET, []], [EDGE_SEARCH, [edge_row('e-h', 'n-alice', 'n-bob', 'Alice manages Bob', group_id='h',
                                                          valid_at=T0)]]],
            replies={'dedupe_edges.resolve_edge': [{'reply': edge_reply(contradicted=[0])}]})
    # The stored target node is in another group (graphiti.py:2544). The stored source is in the group.
    triplet('stored_target_other_group', alice, fact, bob,
            rows=[[ENTITY_GET, [node_row('n-alice', 'Alice')], {'uuid': 'n-alice'}],
                  [ENTITY_GET, [node_row('n-bob', 'Bob', group_id='h')], {'uuid': 'n-bob'}]])
    # The stored target gets the attributes and the summary of the input target (graphiti.py:2553, 2559).
    triplet('stored_target_merged', alice, fact,
            node('n-bob', 'Bob', summary='Bob summary', attributes={'team': 'core'}, labels=['Entity', 'Engineer']),
            rows=[[ENTITY_GET, [node_row('n-bob', 'Bob', summary='old', attributes={'level': 'senior'})],
                   {'uuid': 'n-bob'}]])
    # A stored edge with the same source and another target (graphiti.py:2584).
    triplet('existing_edge_other_target', alice, fact, bob,
            rows=[[EDGE_GET, [edge_row('e-new', 'n-alice', 'n-carol', 'Alice manages Carol')]]])
    return cases


def remove_cases() -> list[dict]:
    target = episode('ep-del', T1, 'deleted talk', entity_edges=['e1', 'e2', 'e3'])
    mentioning = 'MATCH (e:Episodic)-[r:MENTIONS]->(n:Entity {uuid: $entity_node_uuid})'
    rows = [
        [EPISODE_GET, [target]],
        [EDGES_GET, [edge_row('e1', 'n1', 'n2', 'only here', episodes=['ep-del']),
                     edge_row('e2', 'n1', 'n3', 'also elsewhere', episodes=['ep-del', 'ep-keep']),
                     edge_row('e3', 'n2', 'n3', 'other group', group_id='h', episodes=['ep-del'])]],
        [EPISODE_COUNT, [{'episode_count': 1}]],
        [MENTIONS, [node_row('n1', 'Alice', summary='old'), node_row('n2', 'Bob'), node_row('n4', 'Dan'),
                    node_row('n3', 'Zed', group_id='h')]],
        ['RETURN e.fact_embedding', [{'fact_embedding': [0.0, 1.0, 0.0]}]],
        ['RETURN n.name_embedding', [{'name_embedding': [1.0, 0.0, 0.0]}]],
        [mentioning, [episode('ep-keep', T0, 'kept talk'), target, episode('ep-h', T2, group_id='h')]],
    ]
    # Review P9a (decision 3): each node has two episodes, so it survives and its summary is rebuilt. The rows of the
    # mentioning episodes come before the rows of MENTIONS, whose needle is in both queries.
    survive = [rows[0], rows[1], [EPISODE_COUNT, [{'episode_count': 2}]], rows[6], rows[3], rows[4], rows[5]]
    many = [episode(f'ep-{i:02d}', T0 + timedelta(hours=i), f'talk {i}') for i in range(11)]
    return [
        {'fn': 'remove_episode', 'name': 'remove_with_group', 'args': {'episode_uuid': 'ep-del', 'group_id': 'g'},
         'rows': rows, 'cancel': True},
        {'fn': 'remove_episode', 'name': 'remove_no_group', 'args': {'episode_uuid': 'ep-del'}, 'rows': rows,
         'init': {'max_coroutines': 1}},
        {'fn': 'remove_episode', 'name': 'remove_survivors', 'args': {'episode_uuid': 'ep-del', 'group_id': 'g'},
         'rows': survive, 'replies': {'extract_nodes.extract_entity_summaries_from_episodes': [
             {'needle': 'Alice', 'reply': summaries(('Alice', 'Alice again.'))}]}},
        {'fn': 'remove_episode', 'name': 'remove_summary_fails', 'args': {'episode_uuid': 'ep-del', 'group_id': 'g'},
         'rows': survive, 'init': {'max_coroutines': 1},
         'replies': {'extract_nodes.extract_entity_summaries_from_episodes': [{'reply': {'$raise': 'model down'}}]}},
        {'fn': 'remove_episode', 'name': 'remove_eleven_episodes', 'args': {'episode_uuid': 'ep-del', 'group_id': 'g'},
         'rows': [rows[0], rows[1], [EPISODE_COUNT, [{'episode_count': 2}]], [mentioning, many + [target]],
                  [MENTIONS, [node_row('n1', 'Alice', summary='old')]], rows[4], rows[5]]},
        {'fn': 'remove_episode', 'name': 'remove_counts_not_one', 'args': {'episode_uuid': 'ep-del'},
         'rows': [rows[0], rows[1], [EPISODE_COUNT, [{'episode_count': 2}, {'episode_count': 1.0}]]] + rows[3:]},
        {'fn': 'remove_episode', 'name': 'remove_other_database',
         'args': {'episode_uuid': 'ep-del', 'group_id': 'tenant'},
         'rows': [[EPISODE_GET, [episode('ep-del', T1, 'deleted talk', group_id='tenant', entity_edges=['e1'])]],
                  [EDGES_GET, [edge_row('e1', 'n1', 'n2', 'only here', group_id='tenant', episodes=['ep-del'])]],
                  [EPISODE_COUNT, [{'episode_count': 1}]],
                  [MENTIONS, [node_row('n1', 'Alice', group_id='tenant')]]]},
        {'fn': 'remove_episode', 'name': 'remove_missing', 'args': {'episode_uuid': 'gone', 'group_id': 'g'}},
    ]


# The prompt bytes of summarize_saga (graphiti.py:834) for the three episodes of the cases (the episodes `three` and an
# empty summary), and for one episode of 400 ASCII characters. check_budgets proves them at each run.
PROMPT_BYTES_THREE = 3979
PROMPT_BYTES_ASCII = 4249


def saga_summary_cases() -> list[dict]:
    cases = []

    def summarize(name, saga_row, contents=None, args=None, rows=None, **extra):
        case = {'fn': 'summarize_saga', 'name': name, 'args': {'saga_id': 'saga1', **(args or {})},
                'rows': [[SAGA_GET, [saga_row]]] + (rows or [])}
        if contents is not None:
            case['ops'] = {'contents': contents}
        case.update(extra)
        cases.append(case)

    three = [['a' * 40, dt(T0), dt(T1)], ['b' * 40, dt(T1), dt(T2)], ['c' * 40, None, dt(T3)]]
    summarize('no_episodes', saga())
    summarize('query_first', saga(summary='Old summary.'), rows=[[SAGA_CONTENTS, [
        {'content': 'first', 'valid_at': '2026-01-01T00:00:00+00:00', 'created_at': '2026-01-02T00:00:00+00:00'},
        {'content': '', 'valid_at': None, 'created_at': '2026-01-03T00:00:00+00:00'},
        {'content': 'no time', 'valid_at': None, 'created_at': None},
        {'content': 'second', 'valid_at': None, 'created_at': '2026-01-04T00:00:00+00:00'}]]], cancel=True)
    summarize('query_since', saga(last_summarized_at=T0, last_valid_at=T3), rows=[[SAGA_CONTENTS, [
        {'content': 'newer', 'valid_at': '2026-01-05T00:00:00+00:00', 'created_at': '2026-01-06T00:00:00+00:00'}]]])
    summarize('ops_three', saga(), three)
    summarize('ops_budget_drop', saga(), three, patch={'SAGA_SUMMARY_MAX_EPISODE_CHARS': 90})
    summarize('ops_budget_exact', saga(), three, patch={'SAGA_SUMMARY_MAX_EPISODE_CHARS': 120})
    summarize('ops_truncate_first', saga(), three, patch={'SAGA_SUMMARY_MAX_EPISODE_CHARS': 10})
    summarize('ops_zero_budget', saga(), three, patch={'SAGA_SUMMARY_MAX_EPISODE_CHARS': 0})
    summarize('ops_prompt_cap', saga(), [['x' * 4000, dt(T0 + timedelta(minutes=i)), dt(T0 + timedelta(minutes=i))]
                                         for i in range(1, 8)],
              patch={'SAGA_SUMMARY_MAX_PROMPT_BYTES': 12000, 'SAGA_SUMMARY_MAX_EPISODE_CHARS': 12000})
    summarize('ops_prompt_cap_one', saga(), [['é' * 6000, dt(T0), dt(T1)]],
              patch={'SAGA_SUMMARY_MAX_PROMPT_BYTES': 9000})
    summarize('ops_prompt_cap_nothing', saga(), [['é' * 100, dt(T0), dt(T1)]],
              patch={'SAGA_SUMMARY_MAX_PROMPT_BYTES': 100})
    summarize('long_summary', saga(last_valid_at=T3), three,
              replies={'summarize_sagas.summarize_saga': [
                  {'reply': {'summary': 'Kept sentence. ' + 'x' * 4050 + ' Dropped.'}}]})
    summarize('watermark_older', saga(last_valid_at=T0), [['late', dt(T0 - timedelta(days=1)), dt(T1)]])
    summarize('order_by_valid_at', saga(), [['regular earlier ingestion', dt(T2), dt(T0)],
                                            ['backfill newer ingestion but older valid_at', dt(T0), dt(T1)],
                                            ['no valid', None, dt(T0)]])
    summarize('with_group', saga(), three, args={'group_id': 'g'})
    summarize('with_other_database', saga(group_id='tenant'), three, args={'group_id': 'tenant'})
    summarize('wrong_group', saga(group_id='h'), three, args={'group_id': 'g'})
    summarize('missing', None)
    summarize('routed_library', saga(), [['x' * 4000, dt(T0), dt(T1)] for _ in range(5)],
              patch={'SAGA_SUMMARY_MAX_PROMPT_BYTES': 25000}, init={'prompt_library': 'padded'})
    summarize('llm_fails', saga(), three, replies={'summarize_sagas.summarize_saga': [{'reply': {'$raise': 'down'}}]})
    # Review P9a (decision 5).
    summarize('ops_since', saga(summary='Old.', last_summarized_at=T0, last_valid_at=T0), three)
    for name, budget in (('ops_prompt_on_budget', PROMPT_BYTES_THREE), ('ops_prompt_over_budget',
                                                                        PROMPT_BYTES_THREE - 1)):
        summarize(name, saga(), three, patch={'SAGA_SUMMARY_MAX_PROMPT_BYTES': budget})
    summarize('ops_prompt_cap_ascii', saga(), [['x' * 400, dt(T0), dt(T1)]],
              patch={'SAGA_SUMMARY_MAX_PROMPT_BYTES': PROMPT_BYTES_ASCII - 5})
    for name, size in (('summary_on_limit', 4000), ('summary_over_limit', 4001)):
        summarize(name, saga(), three, replies={'summarize_sagas.summarize_saga': [{'reply': {'summary': 'y' * size}}]})
    for name, size in (('existing_on_limit', 4000), ('existing_over_limit', 4001)):
        summarize(name, saga(summary='z' * size), three)
    summarize('watermark_advances', saga(last_summarized_at=T0, last_valid_at=T0),
              [['new', dt(T1), dt(T2)]], rows=[])
    summarize('no_valid_at', saga(last_summarized_at=T0, last_valid_at=T1),
              [['first', None, dt(T2)], ['second', None, dt(T3)]])
    for case in cases:
        if case['rows'][0][1] == [None]:
            case['rows'] = case['rows'][1:]
    return cases


# --- the runner ----------------------------------------------------------------------------------------------------


def types_of(names, m):
    if names is None:
        return None
    from pydantic import Field, create_model
    out = {}
    for name in names:
        if name == 'Bad':
            # A field name of EntityNode: validate_entity_types refuses it.
            out[name] = create_model('Bad', summary=(str | None, Field(default=None)))
            continue
        spec = TYPES[name]
        fields = {}
        for field_name, required, max_length in spec['fields']:
            kwargs = {} if max_length is None else {'max_length': max_length}
            fields[field_name] = (str, Field(..., **kwargs)) if required else (str | None, Field(default=None, **kwargs))
        model = create_model(name, **fields)
        model.__doc__ = spec['doc']
        out[name] = model
    return out


def type_map_of(items):
    if items is None:
        return None
    return {tuple(k): list(v) for k, v in items}


def driver_arg(value, run):
    if value == 'other':
        return run.driver.clone('other_db')
    return None


def view_episode(ep) -> dict:
    return {'uuid': ep.uuid, 'name': ep.name, 'group_id': ep.group_id, 'content': ep.content,
            'source': ep.source.value, 'source_description': ep.source_description, 'valid_at': dt(ep.valid_at),
            'created_at': dt(ep.created_at), 'entity_edges': list(ep.entity_edges), 'labels': list(ep.labels)}


def view_node(n) -> dict:
    return {'uuid': n.uuid, 'name': n.name, 'group_id': n.group_id, 'labels': list(n.labels),
            'created_at': dt(n.created_at), 'summary': n.summary, 'attributes': encode(n.attributes),
            'name_embedding': n.name_embedding}


def view_edge(e) -> dict:
    return {'uuid': e.uuid, 'group_id': e.group_id, 'source_node_uuid': e.source_node_uuid,
            'target_node_uuid': e.target_node_uuid, 'name': e.name, 'fact': e.fact,
            'fact_embedding': e.fact_embedding, 'episodes': list(e.episodes), 'expired_at': dt(e.expired_at),
            'valid_at': dt(e.valid_at), 'invalid_at': dt(e.invalid_at), 'attributes': encode(e.attributes),
            'created_at': dt(e.created_at)}


def view_episodic_edge(e) -> dict:
    return {'uuid': e.uuid, 'group_id': e.group_id, 'source_node_uuid': e.source_node_uuid,
            'target_node_uuid': e.target_node_uuid, 'created_at': dt(e.created_at)}


def view_saga(s) -> dict:
    return {'uuid': s.uuid, 'name': s.name, 'group_id': s.group_id, 'summary': s.summary,
            'created_at': dt(s.created_at), 'first_episode_uuid': s.first_episode_uuid,
            'last_episode_uuid': s.last_episode_uuid, 'last_summarized_at': dt(s.last_summarized_at),
            'last_summarized_episode_valid_at': dt(s.last_summarized_episode_valid_at),
            'private': [s._summary_episodes_fetched, s._summary_episodes_selected, s._summary_episodes_skipped,
                        s._summary_selected_episode_bytes]}


def view_search(r) -> dict:
    return {'edges': [view_edge(e) for e in r.edges], 'edge_scores': r.edge_reranker_scores,
            'nodes': [view_node(n) for n in r.nodes], 'node_scores': r.node_reranker_scores,
            'episodes': [e.uuid for e in r.episodes], 'communities': [c.uuid for c in r.communities]}


def recipe(name, m):
    from graphiti_core.search import search_config_recipes
    return getattr(search_config_recipes, name)


async def run_case(case: dict, m):
    run = Run(case, m)
    init = case.get('init', {})
    graphiti = run.make_graphiti({**init, 'prompt_library': False})
    if init.get('prompt_library') == 'padded':
        from graphiti_core.prompts.models import ChatPrompt, SystemMessage, UserMessage

        def padded_prompt(context):
            return ChatPrompt(system=SystemMessage(content='P' * 14_000),
                              user=UserMessage(content='\n---\n'.join(context['episodes'])))

        library = m['create_prompt_library']({'summarize_sagas': {'summarize_saga': padded_prompt}})
        graphiti.prompt_library = library
        graphiti.clients.prompt_library = library
    tracer = make_tracer(m)
    graphiti.tracer = tracer
    graphiti.clients.tracer = tracer
    g = m['graphiti_module']
    a = decode(copy.deepcopy(case.get('args', {})))
    fn = case['fn']
    for name, value in case.get('patch', {}).items():
        setattr(g, name, value)
    # search writes num_results into a shared recipe (upstream defect U8). Each case starts with the limits of the
    # module, as the C# test gives them back after each case.
    from graphiti_core.search import search_config_recipes as recipes
    limits = (recipes.EDGE_HYBRID_SEARCH_RRF.limit, recipes.EDGE_HYBRID_SEARCH_NODE_DISTANCE.limit)
    try:
        return await run_method(fn, a, case, run, graphiti, m)
    finally:
        for name, value in m['constants'].items():
            setattr(g, name, value)
        recipes.EDGE_HYBRID_SEARCH_RRF.limit, recipes.EDGE_HYBRID_SEARCH_NODE_DISTANCE.limit = limits


async def run_method(fn, a, case, run, graphiti, m):
    g = m['graphiti_module']
    if fn == 'close':
        await graphiti.close()
        return {}
    if fn == 'build_indices_and_constraints':
        await graphiti.build_indices_and_constraints(**a)
        return {}
    if fn == 'retrieve_episodes':
        if 'source' in a:
            a['source'] = m['EpisodeType'](a['source'])
        if 'driver' in a:
            a['driver'] = driver_arg(a['driver'], run)
        result = await graphiti.retrieve_episodes(**a)
        return {'episodes': [view_episode(e) for e in result]}
    if fn == '_validate_saga_scope':
        await g._validate_saga_scope(saga_arg(case['args']['saga'], m), 'g', graphiti.driver)
        return {}
    if fn == '_get_or_create_saga':
        driver = driver_arg(a.pop('driver', None), run)
        kwargs = {'driver': driver} if driver is not None else {}
        result = await graphiti._get_or_create_saga(a['saga_name'], a['group_id'], T2, **kwargs)
        return {'saga': view_saga(result)}
    if fn == '_find_saga':
        result = await graphiti._find_saga(a['saga_name'], a['group_id'])
        return {'saga': None if result is None else view_saga(result)}
    if fn == '_saga_get_previous_episode_uuid':
        driver = driver_arg(a.pop('driver', None), run)
        kwargs = {'driver': driver} if driver is not None else {}
        return {'uuid': await graphiti._saga_get_previous_episode_uuid(**a, **kwargs)}
    if fn == '_saga_get_episode_contents':
        result = await graphiti._saga_get_episode_contents(**a)
        return {'contents': None if result is None else [[c, dt(v), dt(cr)] for c, v, cr in result]}
    if fn == 'summarize_saga':
        result = await graphiti.summarize_saga(**a)
        return {'saga': view_saga(result)}
    if fn == '_extract_and_resolve_nodes':
        ep = a['episode']
        episode_value = ([m['EpisodicNode'](**e) for e in ep] if isinstance(ep, list) else m['EpisodicNode'](**ep))
        kwargs = {}
        if 'identity_properties' in a:
            kwargs['identity_properties'] = a['identity_properties']
        nodes, uuid_map, duplicates, index_map = await graphiti._extract_and_resolve_nodes(
            episode_value, [m['EpisodicNode'](**e) for e in a['previous_episodes']], types_of(a['entity_types'], m),
            a['excluded_entity_types'], **kwargs)
        return {'nodes': [view_node(n) for n in nodes], 'uuid_map': uuid_map,
                'duplicates': [[x.uuid, y.uuid] for x, y in duplicates], 'index_map': index_map}
    if fn == '_resolve_nodes_and_edges_bulk':
        EntityNode, EntityEdge, EpisodicNode = m['EntityNode'], m['EntityEdge'], m['EpisodicNode']
        shared = {}

        def node_of(data):
            # The same uuid in two episodes is one object, as dedupe_nodes_bulk gives it.
            if data['uuid'] not in shared:
                shared[data['uuid']] = EntityNode(**data)
            return shared[data['uuid']]

        result = await graphiti._resolve_nodes_and_edges_bulk(
            {k: [node_of(n) for n in v] for k, v in a['nodes_by_episode'].items()},
            {k: [EntityEdge(**e) for e in v] for k, v in a['edges_by_episode'].items()},
            [(EpisodicNode(**e), [EpisodicNode(**p) for p in prev]) for e, prev in a['episode_context']],
            types_of(a['entity_types'], m), types_of(a['edge_types'], m), type_map_of(a['edge_type_map']),
            [EpisodicNode(**e) for e in a['episodes']], identity_properties=a['identity_properties'],
            hydrate=a['hydrate'])
        nodes, resolved, invalidated, uuid_map = result
        return {'nodes': [view_node(n) for n in nodes], 'resolved': [view_edge(e) for e in resolved],
                'invalidated': [view_edge(e) for e in invalidated], 'uuid_map': uuid_map}
    if fn == 'add_episode':
        kwargs = dict(a)
        if 'source' in kwargs:
            kwargs['source'] = m['EpisodeType'](kwargs['source'])
        if 'saga' in kwargs:
            kwargs['saga'] = saga_arg(case['args']['saga'], m)
        for key in ('entity_types', 'edge_types'):
            if key in kwargs:
                kwargs[key] = types_of(kwargs[key], m)
        if 'edge_type_map' in kwargs:
            kwargs['edge_type_map'] = type_map_of(kwargs['edge_type_map'])
        result = await graphiti.add_episode(**kwargs)
        return {'episode': view_episode(result.episode),
                'episodic_edges': [view_episodic_edge(e) for e in result.episodic_edges],
                'nodes': [view_node(n) for n in result.nodes], 'edges': [view_edge(e) for e in result.edges],
                'communities': [c.uuid for c in result.communities],
                'community_edges': [e.uuid for e in result.community_edges]}
    if fn == 'add_episode_bulk':
        kwargs = dict(a)
        kwargs['bulk_episodes'] = [m['RawEpisode'](**{**e, 'source': m['EpisodeType'](e['source'])})
                                   for e in kwargs['bulk_episodes']]
        if 'saga' in kwargs:
            kwargs['saga'] = saga_arg(case['args']['saga'], m)
        for key in ('entity_types', 'edge_types'):
            if key in kwargs:
                kwargs[key] = types_of(kwargs[key], m)
        if 'edge_type_map' in kwargs:
            kwargs['edge_type_map'] = type_map_of(kwargs['edge_type_map'])
        result = await graphiti.add_episode_bulk(**kwargs)
        return {'episodes': [view_episode(e) for e in result.episodes],
                'episodic_edges': [view_episodic_edge(e) for e in result.episodic_edges],
                'nodes': [view_node(n) for n in result.nodes], 'edges': [view_edge(e) for e in result.edges],
                'communities': len(result.communities), 'community_edges': len(result.community_edges)}
    if fn == 'build_communities':
        kwargs = dict(a)
        if 'driver' in kwargs:
            kwargs['driver'] = driver_arg(kwargs['driver'], run)
        nodes, edges = await graphiti.build_communities(**kwargs)
        return {'nodes': [n.uuid for n in nodes], 'edges': [e.uuid for e in edges]}
    if fn in ('search', '_search', 'search_'):
        kwargs = dict(a)
        if 'search_filter' in kwargs:
            kwargs['search_filter'] = m['SearchFilters'](**kwargs['search_filter'])
        if 'config' in kwargs:
            kwargs['config'] = recipe(kwargs['config'], m)
        if fn == 'search':
            result = await graphiti.search(**kwargs)
            return {'edges': [view_edge(e) for e in result]}
        if fn == '_search':
            result = await graphiti._search(**kwargs)
        else:
            result = await graphiti.search_(**kwargs)
        return view_search(result)
    if fn == 'get_nodes_and_edges_by_episode':
        result = await graphiti.get_nodes_and_edges_by_episode(**a)
        return view_search(result)
    if fn == 'add_triplet':
        EntityNode, EntityEdge, EpisodicNode = m['EntityNode'], m['EntityEdge'], m['EpisodicNode']
        source = EntityNode(**a['source_node'])
        target = EntityNode(**a['target_node'])
        e = EntityEdge(**a['edge'])
        kwargs = {}
        if 'episode' in a:
            kwargs['episode'] = EpisodicNode(**a['episode'])
        if 'identity_properties' in a:
            kwargs['identity_properties'] = a['identity_properties']
        result = await graphiti.add_triplet(source, e, target, **kwargs)
        return {'nodes': [view_node(n) for n in result.nodes], 'edges': [view_edge(x) for x in result.edges],
                'source': view_node(source), 'target': view_node(target), 'edge': view_edge(e)}
    if fn == 'remove_episode':
        await graphiti.remove_episode(**a)
        return {}
    raise AssertionError(fn)


def run_one(case: dict, m, cancel_at: int) -> dict:
    events.clear()
    state.update(in_flight=0, max_in_flight=0, count=0, attempts=0, cancel_at=cancel_at, case=copy.deepcopy(case))

    async def main():
        task = asyncio.ensure_future(run_case(state['case'], m))
        state['task'] = task
        return await task

    try:
        output = {'ok': asyncio.run(main())}
    except BaseException as error:  # noqa: BLE001 - the recording keeps each exception, also a cancellation
        if isinstance(error, (KeyboardInterrupt, SystemExit)):
            raise
        text = '' if isinstance(error, asyncio.CancelledError) else error_text(error)
        output = {'error': {'type': type(error).__name__, 'text': text}}
    for item in events:
        if item.get('kind') == 'log':
            item['message'] = MS.sub('in <ms> ms', item['message'])
    output['events'] = list(events)
    output['max_in_flight'] = state['max_in_flight']
    output['count'] = state['count']
    output['attempts'] = state['attempts']
    return output


# The events that are not a counted fake call. The events after call k keep the log lines (R25).
COUNTED_SKIP = ('log', 'session', 'session_close', 'span', 'span_attrs', 'span_status', 'span_exception', 'span_end',
                'clone')
AFTER_SKIP = tuple(kind for kind in COUNTED_SKIP if kind != 'log')


def events_after(items: list[dict], k: int) -> list[dict]:
    """The counted calls and the log lines after counted call k."""
    seen = 0
    after = []
    for item in items:
        if seen > k and item['kind'] not in AFTER_SKIP:
            after.append(item)
        if item['kind'] not in COUNTED_SKIP:
            seen += 1
    return after


def record_async(m) -> list[dict]:
    out = []
    for case in (other_cases() + saga_summary_cases() + nodes_reply_cases() + bulk_cases() + triplet_cases()
                 + remove_cases()):
        output = run_one(case, m, -1)
        if case.get('cancel'):
            cancels = []
            for k in range(output['count']):
                cancelled = run_one(case, m, k)
                cancels.append({'k': k, 'attempts': cancelled['attempts'], 'count': cancelled['count'],
                                'error': cancelled.get('error', {}).get('type'),
                                'after': placeholders(events_after(cancelled['events'], k), {})})
            output['cancel'] = cancels
        out.append({'in': case, 'out': placeholders(output, {})})
    check_budgets({r['in']['name']: r['out'] for r in out if r['in']['fn'] == 'summarize_saga'})
    return out


def check_budgets(outputs: dict) -> None:
    """The prompt of three episodes fits a budget of PROMPT_BYTES_THREE bytes, and not one byte less. The ASCII episode
    loses the 5 bytes that its budget lacks."""
    on, over = outputs['ops_prompt_on_budget']['ok']['saga'], outputs['ops_prompt_over_budget']['ok']['saga']
    ascii_saga = outputs['ops_prompt_cap_ascii']['ok']['saga']
    if on['private'] != [3, 3, 0, 120] or over['private'][1] != 2 or ascii_saga['private'][3] != 395:
        raise SystemExit(f'The prompt budgets are wrong: {on["private"]}, {over["private"]}, {ascii_saga["private"]}')


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    os.environ.pop('GRAPHITI_ATTRIBUTE_MAX_LENGTH', None)
    os.environ.pop('SEMAPHORE_LIMIT', None)
    os.environ['GRAPHITI_TELEMETRY_ENABLED'] = 'false'
    warnings.filterwarnings('ignore', message=r"coroutine '.*' was never awaited", category=RuntimeWarning)

    import graphiti_core
    from graphiti_core import graphiti as graphiti_module
    from graphiti_core.cross_encoder.client import CrossEncoderClient
    from graphiti_core.driver.driver import GraphDriver, GraphDriverSession, GraphProvider
    from graphiti_core.driver.graph_operations.graph_operations import GraphOperationsInterface
    from graphiti_core.edges import EntityEdge, EpisodicEdge
    from graphiti_core.embedder.client import EmbedderClient
    from graphiti_core.ingestion.config import ConcurrentIngestionConfig
    from graphiti_core.ingestion.state_store import InMemoryIngestionStateStore
    from graphiti_core.llm_client import LLMClient, LLMRuntime
    from graphiti_core.llm_client.prompt_config import LLMTransport
    from graphiti_core.nodes import EntityNode, EpisodeType, EpisodicNode, SagaNode
    from graphiti_core.prompts import create_prompt_library
    from graphiti_core.search.search_filters import SearchFilters
    from graphiti_core.tracer import Tracer
    from graphiti_core.utils.bulk_utils import RawEpisode

    # The attributes that each LogRecord has. Each other attribute came from `extra` (R25).
    standard = set(vars(logging.LogRecord('n', logging.INFO, 'p', 1, 'm', None, None))) | {'message', 'asctime'}

    class Capture(logging.Handler):
        def emit(self, record):
            item = {'kind': 'log', 'level': record.levelname, 'message': record.getMessage()}
            extra = {k: encode(v) for k, v in vars(record).items() if k not in standard}
            if extra:
                item['extra'] = extra
            events.append(item)

    root = logging.getLogger('graphiti_core')
    root.setLevel(logging.DEBUG)
    root.propagate = False
    root.addHandler(Capture(logging.DEBUG))

    for name, module in list(sys.modules.items()):
        if name.startswith('graphiti_core') and hasattr(module, 'utc_now'):
            module.utc_now = lambda: FIXED_NOW
    del graphiti_core
    # The telemetry is not ported (decision D9).
    graphiti_module.capture_event = lambda *args, **kwargs: None
    # list(set(a) | set(b)) of add_triplet in the order of the port (R14).
    graphiti_module.set = EntityFirstSet
    # The sets and the label lists of the modules below graphiti.py in the order of the port (R14), as the recorders of
    # packets P7a, P7b, P7c, and P7d bind them.
    from pydantic import ConfigDict

    from graphiti_core.utils import bulk_utils
    from graphiti_core.utils.maintenance import attribute_utils, dedup_helpers, edge_operations, node_operations
    from graphiti_core.utils.maintenance import combined_extraction as ce

    sys.path.insert(0, str(Path(__file__).resolve().parent))
    import record_bulk_utils as rbu

    bulk_utils.set = rbu.InsertionOrderSet
    dedup_helpers.set = rbu.InsertionOrderSet
    attribute_utils.set = rbu.InsertionOrderSet
    edge_operations.set = rbu.InsertionOrderSet

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
    ce.EntityNode = OrderedLabelsEntityNode

    constant_names = ['SAGA_SUMMARY_MAX_EPISODE_CHARS', 'SAGA_SUMMARY_MAX_EPISODE_BYTES', 'SAGA_SUMMARY_MAX_PROMPT_BYTES',
                      'MAX_SAGA_EPISODES_FOR_SUMMARY', 'SUMMARY_REBUILD_BATCH_SIZE', 'SAGA_SUMMARY_MAX_TOKENS',
                      '_SAGA_SUMMARY_SCHEMA_WRAPPER']
    constants = {name: getattr(graphiti_module, name) for name in constant_names}
    m = dict(graphiti_module=graphiti_module, Graphiti=graphiti_module.Graphiti, GraphDriver=GraphDriver,
             GraphDriverSession=GraphDriverSession, GraphProvider=GraphProvider,
             GraphOperationsInterface=GraphOperationsInterface, EntityEdge=EntityEdge, EpisodicEdge=EpisodicEdge,
             EntityNode=EntityNode, EpisodicNode=EpisodicNode, SagaNode=SagaNode, EpisodeType=EpisodeType,
             EmbedderClient=EmbedderClient, CrossEncoderClient=CrossEncoderClient, LLMClient=LLMClient,
             LLMRuntime=LLMRuntime, LLMTransport=LLMTransport, ConcurrentIngestionConfig=ConcurrentIngestionConfig,
             InMemoryIngestionStateStore=InMemoryIngestionStateStore, create_prompt_library=create_prompt_library,
             SearchFilters=SearchFilters, Tracer=Tracer, RawEpisode=RawEpisode, constants=constants)

    sync = []
    for case in sync_cases():
        events.clear()
        state.update(in_flight=0, max_in_flight=0, count=0, attempts=0, cancel_at=-1, case=case)
        if 'port_only' in case:
            sync.append({'in': case, 'out': {'error': case['port_only'], 'events': []}})
            continue
        try:
            out = {'ok': run_sync(case, m)}
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            out = {'error': {'type': type(error).__name__, 'text': error_text(error)}}
        out['events'] = list(events)
        sync.append({'in': case, 'out': out})
    async_cases = record_async(m)

    dump = lambda r: json.dumps(r, ensure_ascii=True, allow_nan=False, separators=(',', ':'))  # noqa: E731
    text = (
        '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","fixed_now":"' + FIXED_NOW.isoformat()
        + '","constants":' + dump(constants) + ',"types":' + dump(TYPES) + ',"fallback":' + dump(FALLBACK)
        + ',"cases":[\n' + ',\n'.join(dump(r) for r in sync)
        + '\n],"async_cases":[\n' + ',\n'.join(dump(r) for r in async_cases) + '\n]}\n'
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
    if '--dry' in sys.argv[1:]:
        print(text[:200000])
        print(f'{size} bytes')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(sync)} cases and {len(async_cases)} async cases, {size} bytes')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
