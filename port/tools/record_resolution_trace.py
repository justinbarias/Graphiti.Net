"""Records the resolution functions of layer 7 with a ResolutionTrace, from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_resolution_trace.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_resolution_trace.py --check

Plan 0004, layer 8, packet P8b. The recorders of layer 7 pass `trace=None` to each call, because ResolutionTrace of
ingestion/change_set.py was a skeleton when layer 7 was recorded. This tool closes that gap. It calls

- resolve_extracted_nodes and resolve_nodes_with_candidates (node_operations.py) with the fakes of
  record_node_operations.py (build_world),
- resolve_extracted_edges (edge_operations.py) with the fakes of record_edge_operations.py (run_case),
- dedupe_nodes_bulk (bulk_utils.py) with the fakes of record_bulk_utils.py (run_case),

each one time with a ResolutionTrace and one time with no trace argument. It records the output of each call in the
form of its recorder, and the full content of the trace after the call: each candidate list, each label list, each
operation in order, the stored node UUIDs, the edges that a candidate ended, and the count of model calls. The tool
asserts that the output with a trace equals the output with no trace, and the C# test asserts it again on its side.

run_case of the edge and bulk recorders passes `trace=None`. The tool binds the names resolve_extracted_edges of
edge_operations and dedupe_nodes_bulk of bulk_utils to a wrapper that passes the trace of the run in progress, and
calls the real function. Nothing else of those recorders changes.

The cases: nodes that resolve by the exact name, by a fuzzy name, by an identity property, and by the answer of the
model, and a node with no match; edges that are an exact duplicate, a duplicate by the answer of the model, a
contradiction that ends a stored edge, a fact that a later stored edge ends, and a new edge.

The fixed values of the three recorders hold here too: each utc_now of graphiti_core gives FIXED_NOW, the sets that
the code iterates iterate in insertion order, a uuid4 of the output is a placeholder, and a time of a log line is
<ms>. The C# test gives the port a clock with FIXED_NOW.

It writes tests/Port.Evaluation/values/resolution_trace.json. --check writes nothing and exits 1 if the file differs
from what the run would write. The tool needs Python 3.12 and the pinned upstream in the folder of the variable
GRAPHITI_UPSTREAM, or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import copy
import json
import logging
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'resolution_trace.json'

sys.path.insert(0, str(Path(__file__).resolve().parent))
import record_bulk_utils as rbu  # noqa: E402
import record_edge_operations as reo  # noqa: E402
import record_node_operations as rno  # noqa: E402

FIXED_NOW = datetime(2026, 3, 4, 5, 6, 7, 891011, tzinfo=timezone.utc)
assert FIXED_NOW == rno.FIXED_NOW == reo.FIXED_NOW == rbu.FIXED_NOW

# The trace of the run in progress, and the event list that the log handler writes to.
CURRENT: dict = {'trace': None, 'mode': None}


def trace_view(trace) -> dict | None:
    if trace is None:
        return None
    return {
        'seen_node_candidates': trace.seen_node_candidates,
        'seen_node_labels': trace.seen_node_labels,
        'seen_duplicate_candidates': trace.seen_duplicate_candidates,
        'seen_invalidation_candidates': trace.seen_invalidation_candidates,
        'operations': [operation.model_dump(mode='json') for operation in trace.operations],
        'stored_node_uuids': sorted(trace.stored_node_uuids),
        'ended_by_candidate': sorted(trace.ended_by_candidate),
        'node_llm_calls': trace.node_llm_calls,
    }


# ---------------------------------------------------------------- node cases, in the form of record_node_operations.py

def nd(uuid, name, labels=None, attributes=None, group_id='g'):
    return {'uuid': uuid, 'name': name, 'group_id': group_id, 'labels': labels or ['Entity'],
            'created_at': '2024-01-01T00:00:00+00:00', 'summary': '', 'attributes': attributes or {}}


def ep(i):
    return {'uuid': f'ep-{i}', 'name': f'episode {i}', 'group_id': 'g', 'source': 'message',
            'source_description': 'chat', 'content': 'Alice met Bob.', 'valid_at': '2024-05-06T07:08:09+00:00',
            'created_at': '2024-05-06T07:08:09+00:00'}


def resolutions(*items):
    return {'dedupe_nodes.nodes': [{'entity_resolutions': [
        {'id': i, 'name': name, 'duplicate_candidate_id': dup} for i, name, dup in items]}]}


def node_cases() -> list[dict]:
    alice = nd('s-alice', 'Alice Johnson', ['Entity', 'Person'])
    joe = nd('s-joe', 'Joe Michaels')
    smith = nd('s-smith', 'Alice Smith', ['Entity', 'Person'], {'email': 'smith@x.io', 'title': 'eng'})
    bob = nd('s-bob', 'Robert Tables', ['Entity', 'Person'])
    other = nd('s-other', 'Unrelated Corp', ['Entity', 'Company'])
    nodes = [nd('x-exact', 'Alice Johnson', ['Entity', 'Person'], {'role': 'lead'}),
             nd('x-fuzzy', 'Joe-Michaels', ['Entity', 'Person']),
             nd('x-id', 'A. Smith', ['Entity', 'Person'], {'email': 'smith@x.io'}),
             nd('x-llm', 'Bobby Tables'),
             nd('x-new', 'Completely New Thing')]
    search = {'Alice Johnson': [alice, joe], 'Joe-Michaels': [joe], 'A. Smith': [], 'Bobby Tables': [bob, other],
              'Completely New Thing': [other]}
    reply = resolutions((0, 'Bobby Tables', 0), (1, 'Completely New Thing', -1))
    base = {'clients': {}, 'exact': [alice], 'identity': {'Person': [smith]}, 'search': search, 'llm': reply}
    cases = [
        {**base, 'fn': 'resolve_extracted_nodes',
         'args': {'nodes': nodes, 'episode': ep(1), 'previous': [], 'identity_properties': {'Person': ['email']}}},
        {**base, 'fn': 'resolve_extracted_nodes', 'clients': {'max_coroutines': 1},
         'args': {'nodes': nodes, 'episode': ep(1)}},
        {**base, 'fn': 'resolve_nodes_with_candidates',
         'args': {'nodes': nodes, 'candidates': [[alice, joe], [joe], [smith], [bob, other], [other]], 'episode': ep(1),
                  'identity_properties': {'Person': ['email']}}},
        # Two extracted nodes see one candidate, and both resolve to it.
        {**base, 'fn': 'resolve_nodes_with_candidates', 'llm': resolutions((0, 'Bob T', 0), (1, 'B. Tables', 0)),
         'args': {'nodes': [nd('x-b1', 'Bob T'), nd('x-b2', 'B. Tables', ['Entity', 'Person'], {'k': 1})],
                  'candidates': [[bob], [bob, other]]}},
        # The scenario of the upstream test: one exact name with a label and an attribute, one node with no candidate.
        {'clients': {}, 'exact': [], 'identity': {}, 'llm': {}, 'fn': 'resolve_extracted_nodes',
         'search': {'Ana Lopez': [nd('stored', 'Ana Lopez'), nd('other', 'Bolt')], 'Acme': []},
         'args': {'nodes': [nd('x1', 'Ana Lopez', ['Entity', 'Person'], {'email': 'ana@example.com'}), nd('x2', 'Acme')],
                  'episode': ep(1), 'previous': []}},
        {**base, 'fn': 'resolve_extracted_nodes', 'args': {'nodes': []}},
    ]
    return cases


def run_node_case(case: dict, m, traced: bool) -> dict:
    run, clients, make_nodes = rno.build_world(case, m)
    CURRENT['mode'] = 'node'
    a = case['args']
    nodes = make_nodes(a.get('nodes', []))
    trace = m.ResolutionTrace() if traced else None
    out = {}

    async def body():
        kwargs = {}
        if 'episode' in a:
            kwargs['episode'] = m.EpisodicNode(**copy.deepcopy(a['episode']))
        if 'previous' in a:
            kwargs['previous_episodes'] = [m.EpisodicNode(**copy.deepcopy(e)) for e in a['previous']]
        if 'identity_properties' in a:
            kwargs['identity_properties'] = a['identity_properties']
        if trace is not None:
            kwargs['trace'] = trace
        if case['fn'] == 'resolve_extracted_nodes':
            resolved, uuid_map, pairs = await m.no.resolve_extracted_nodes(clients, nodes, **kwargs)
        else:
            candidates = [make_nodes(group) for group in a['candidates']]
            resolved, uuid_map, pairs = await m.no.resolve_nodes_with_candidates(clients, nodes, candidates, **kwargs)
        return [[rno.node_view(n) for n in resolved], uuid_map, [[x.uuid, y.uuid] for x, y in pairs]]

    try:
        out['result'] = rno.plain(asyncio.run(body()))
        out['error'] = None
    except Exception as error:  # noqa: BLE001 - the recording keeps each exception
        out['result'] = None
        out['error'] = rno.error_view(error)
    out['events'] = run.events
    out['nodes_after'] = [rno.node_view(n) for n in nodes]
    out['max_in_flight'] = dict(sorted(run.max_in_flight.items()))
    out['trace'] = trace_view(trace)
    rno.CURRENT['events'] = None
    return rno.normalize_uuids(out)


# ---------------------------------------------------------------- edge cases, in the form of record_edge_operations.py

def edge_cases() -> list[dict]:
    e, node, ep1, reply = reo.edge, reo.node, reo.ep, reo.reply
    PAST, REF, FUTURE = reo.PAST, reo.REF, reo.FUTURE
    stored = {'n-ana': node('n-ana', 'Ana', ['Entity', 'Person']), 'n-acme': node('n-acme', 'Acme', ['Entity', 'Company']),
              'n-paris': node('n-paris', 'Paris', ['Entity', 'Place']), 'n-bob': node('n-bob', 'Bob', ['Entity', 'Person']),
              'n-fr': node('n-fr', 'France', ['Entity', 'Place'])}
    work = e('s-work', 'n-ana', 'n-acme', 'Ana works at Acme', valid_at=PAST, episodes=['ep0'])
    live = e('s-live', 'n-ana', 'n-paris', 'Ana lives in Paris', valid_at=PAST, episodes=['ep0'], attributes={'k': 1})
    bob_work = e('s-bobwork', 'n-bob', 'n-acme', 'Bob works at Acme', valid_at=PAST, episodes=['ep0'])
    newer = e('s-newer', 'n-ana', 'n-bob', 'Ana and Bob are friends now', valid_at=FUTURE)
    x_dup = e('x-dup', 'n-ana', 'n-acme', 'Ana works at Acme', episodes=['ep1'])
    x_llm = e('x-llm', 'n-ana', 'n-paris', 'Ana resides in Paris', episodes=['ep1'])
    x_contra = e('x-contra', 'n-bob', 'n-acme', 'Bob left Acme', episodes=['ep1'], valid_at=REF)
    x_old = e('x-old', 'n-ana', 'n-bob', 'Ana and Bob are strangers', episodes=['ep1'], valid_at=PAST)
    x_new = e('x-new', 'n-paris', 'n-fr', 'Paris is in France', episodes=['ep1'])

    def search(fact, pair_bm25=(), pair_cos=(), all_bm25=(), all_cos=()):
        return {fact: {'pair': {'bm25': list(pair_bm25), 'cos': list(pair_cos)},
                       'all': {'bm25': list(all_bm25), 'cos': list(all_cos)}}}

    base = {'fn': 'resolve_extracted_edges', 'episode': ep1('ep1', REF), 'entities': list(stored.values()),
            'stored_nodes': list(stored.values()), 'types': [], 'type_map': [], 'override': None, 'strict': False,
            'invalidated_by': True, 'max_coroutines': None, 'routing': False}
    tables = {**search('Ana works at Acme', pair_bm25=[work], all_bm25=[work, bob_work]),
              **search('Ana resides in Paris', pair_bm25=[live], all_cos=[work]),
              **search('Bob left Acme', all_bm25=[bob_work], all_cos=[work]),
              **search('Ana and Bob are strangers', pair_cos=[newer]),
              **search('Paris is in France')}
    replies = [reply('dedupe_edges.resolve_edge', '<NEW FACT>\nAna resides in Paris\n</NEW FACT>',
                     {'duplicate_facts': [0], 'contradicted_facts': []}),
               reply('dedupe_edges.resolve_edge', '<NEW FACT>\nBob left Acme\n</NEW FACT>',
                     {'duplicate_facts': [], 'contradicted_facts': [0]}),
               reply('dedupe_edges.resolve_edge', '<NEW FACT>\nAna and Bob are strangers\n</NEW FACT>',
                     {'duplicate_facts': [], 'contradicted_facts': [0]}),
               reply('dedupe_edges.resolve_edge', '<NEW FACT>\nAna works at Acme\n</NEW FACT>',
                     {'duplicate_facts': [], 'contradicted_facts': []})]
    edges = [x_dup, x_llm, x_contra, x_old, x_new]
    return [
        {**base, 'edges': edges, 'search': tables, 'replies': replies},
        {**base, 'edges': edges, 'search': tables, 'replies': replies, 'max_coroutines': 1, 'invalidated_by': False},
        # An in-batch duplicate that the fast path drops, and an override candidate of the same pair.
        {**base, 'edges': [x_dup, e('x-dup2', 'n-acme', 'n-ana', ' ana works at ACME', episodes=['ep1']), x_new],
         'override': [work], 'search': tables, 'replies': replies},
        {**base, 'edges': []},
    ]


def run_edge_case(case: dict, mods, traced: bool) -> dict:
    CURRENT['mode'] = 'edge'
    CURRENT['trace'] = mods[-1]() if traced else None
    reo.events.clear()
    reo.state.update(in_flight=0, max_in_flight=0)
    try:
        output = {'ok': asyncio.run(reo.run_case(case, mods[:-1]))}
    except Exception as error:  # noqa: BLE001 - the recording keeps each exception
        output = {'error': {'type': type(error).__name__, 'text': str(error)}}
    output['events'] = list(reo.events)
    output['max_in_flight'] = reo.state['max_in_flight']
    output['trace'] = trace_view(CURRENT['trace'])
    CURRENT['trace'] = None
    return reo.placeholders(output, {})


# ---------------------------------------------------------------- bulk cases, in the form of record_bulk_utils.py

def bulk_cases() -> list[dict]:
    node, ep1, reply = rbu.node, rbu.ep, rbu.reply
    stored_alice = node('s-alice', 'Alice Johnson', ['Entity', 'Person'], summary='Stored Alice.')
    stored_carol = node('s-carol', 'Carol', ['Entity', 'Person'], attributes={'email': 'carol@example.com'})
    eps = [ep1('ep1'), ep1('ep2')]
    base = {'fn': 'dedupe_nodes_bulk', 'ops': True, 'episodes': eps, 'identity_properties': None,
            'entity_types': None, 'search': {}, 'exact': [], 'identity': {}, 'replies': [], 'max_coroutines': None}
    nodes = [
        [node('a1', 'Alice Johnson', ['Entity', 'Person']), node('x1', 'Alicia Johnston'), node('c1', 'Carol C',
                                                                                              ['Entity', 'Person'], attributes={'email': 'carol@example.com'})],
        [node('a2', 'alice  johnson', ['Entity']), node('n2', 'Brand New Thing'), node('a3', 'Ally J')],
    ]
    return [
        {**base, 'nodes': nodes, 'exact': [stored_alice], 'identity': {'Person': [stored_carol]},
         'identity_properties': {'Person': ['email']},
         'search': {'Alicia Johnston': [stored_alice], 'Ally J': [stored_alice], 'Brand New Thing': [stored_carol]},
         'replies': [reply('dedupe_nodes.nodes', 'Alicia Johnston', {'entity_resolutions': [
             {'id': 0, 'name': 'Alicia Johnston', 'duplicate_candidate_id': 0}]}),
             reply('dedupe_nodes.nodes', 'Ally J', {'entity_resolutions': [
                 {'id': 0, 'name': 'Brand New Thing', 'duplicate_candidate_id': -1},
                 {'id': 1, 'name': 'Ally J', 'duplicate_candidate_id': 0}]})]},
        {**base, 'nodes': nodes, 'exact': [stored_alice], 'max_coroutines': 1, 'search': {'Ally J': [stored_alice]}},
        {**base, 'nodes': [], 'episodes': []},
    ]


def run_bulk_case(case: dict, mods, traced: bool) -> dict:
    CURRENT['mode'] = 'bulk'
    CURRENT['trace'] = mods[-1]() if traced else None
    rbu.events.clear()
    rbu.state.update(in_flight=0, max_in_flight=0, count=0)
    try:
        output = {'ok': asyncio.run(rbu.run_case(case, mods[:-1]))}
    except Exception as error:  # noqa: BLE001 - the recording keeps each exception
        output = {'error': {'type': type(error).__name__, 'text': str(error)}}
    output['events'] = list(rbu.events)
    output['max_in_flight'] = rbu.state['max_in_flight']
    output['count'] = rbu.state['count']
    output['trace'] = trace_view(CURRENT['trace'])
    CURRENT['trace'] = None
    return rbu.placeholders(output, {})


# ---------------------------------------------------------------- main

def without_trace(output: dict) -> dict:
    return {k: v for k, v in output.items() if k != 'trace'}


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    os.environ.pop('GRAPHITI_ATTRIBUTE_MAX_LENGTH', None)
    from pydantic import BaseModel, Field, create_model

    import graphiti_core
    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.driver.graph_operations.graph_operations import GraphOperationsInterface
    from graphiti_core.driver.search_interface.search_interface import SearchInterface
    from graphiti_core.edges import EntityEdge, EpisodicEdge
    from graphiti_core.embedder.client import EmbedderClient
    from graphiti_core.graphiti_types import GraphitiClients
    from graphiti_core.ingestion.change_set import ResolutionTrace
    from graphiti_core.llm_client import LLMClient
    from graphiti_core.llm_client.config import LLMConfig, ModelSize
    from graphiti_core.nodes import EntityNode, EpisodicNode
    from graphiti_core.prompts.extract_nodes import ExtractedEntity
    from graphiti_core.prompts.lib import default_chat_prompt_library
    from graphiti_core.utils import bulk_utils
    from graphiti_core.utils.maintenance import attribute_utils, dedup_helpers, edge_operations, node_operations

    # The fixed values of the three recorders (see the module docstring).
    for name, module in list(sys.modules.items()):
        if name.startswith('graphiti_core') and hasattr(module, 'utc_now'):
            module.utc_now = lambda: FIXED_NOW
    del graphiti_core
    bulk_utils.set = rbu.InsertionOrderSet
    dedup_helpers.set = rbu.InsertionOrderSet
    attribute_utils.set = rbu.InsertionOrderSet
    edge_operations.set = rbu.InsertionOrderSet

    class OrderedLabelsEntityNode(EntityNode):
        def __init__(self, **data):
            labels = data.get('labels')
            if labels is not None and 'Entity' in labels:
                data['labels'] = ['Entity'] + [label for label in labels if label != 'Entity']
            super().__init__(**data)

    node_operations.EntityNode = OrderedLabelsEntityNode

    # The wrappers that pass the trace of the run in progress (see the module docstring).
    resolve_extracted_edges = edge_operations.resolve_extracted_edges
    dedupe_nodes_bulk = bulk_utils.dedupe_nodes_bulk

    async def resolve_extracted_edges_with_trace(*args, **kwargs):
        kwargs['trace'] = CURRENT['trace']
        return await resolve_extracted_edges(*args, **kwargs)

    async def dedupe_nodes_bulk_with_trace(*args, **kwargs):
        kwargs['trace'] = CURRENT['trace']
        return await dedupe_nodes_bulk(*args, **kwargs)

    edge_operations.resolve_extracted_edges = resolve_extracted_edges_with_trace
    bulk_utils.dedupe_nodes_bulk = dedupe_nodes_bulk_with_trace

    class Capture(logging.Handler):
        def emit(self, record):
            mode = CURRENT['mode']
            if mode == 'node':
                if rno.CURRENT['events'] is None:
                    return
                entry = {'kind': 'log', 'level': record.levelname, 'message': rno.MS.sub(' in <ms> ms', record.getMessage())}
                extra = {k: getattr(record, k) for k in rno.EXTRA_KEYS if hasattr(record, k)}
                if extra:
                    entry['extra'] = extra
                if record.exc_info:
                    entry['exc'] = record.exc_info[0].__name__
                rno.CURRENT['events'].append(entry)
            elif mode == 'edge':
                reo.events.append({'kind': 'log', 'level': record.levelname, 'message': record.getMessage()})
            elif mode == 'bulk':
                rbu.events.append({'kind': 'log', 'level': record.levelname, 'message': record.getMessage()})

    root = logging.getLogger('graphiti_core')
    root.setLevel(logging.DEBUG)
    root.propagate = False
    root.addHandler(Capture(logging.DEBUG))

    m = rno.Names()
    for name, value in {
        'GraphProvider': GraphProvider, 'GraphOperationsInterface': GraphOperationsInterface,
        'SearchInterface': SearchInterface, 'EntityEdge': EntityEdge, 'EmbedderClient': EmbedderClient,
        'GraphitiClients': GraphitiClients, 'LLMClient': LLMClient, 'LLMConfig': LLMConfig, 'ModelSize': ModelSize,
        'EntityNode': EntityNode, 'EpisodicNode': EpisodicNode, 'ExtractedEntity': ExtractedEntity,
        'no': node_operations, 'ResolutionTrace': ResolutionTrace,
    }.items():
        setattr(m, name, value)

    edge_models = {}
    for name, spec in reo.TYPE_DEFS.items():
        fields = {}
        for field_name, required, max_length in spec['fields']:
            kwargs = {} if max_length is None else {'max_length': max_length}
            fields[field_name] = (str, Field(..., **kwargs)) if required else (str | None, Field(None, **kwargs))
        edge_models[name] = create_model(name, __base__=BaseModel, __doc__=spec['doc'], **fields)
    bulk_models = {}
    for name, spec in rbu.TYPE_DEFS.items():
        fields = {}
        for field_name, required, max_length in spec['fields']:
            kwargs = {} if max_length is None else {'max_length': max_length}
            fields[field_name] = (str, Field(..., **kwargs)) if required else (str | None, Field(None, **kwargs))
        bulk_models[name] = create_model(name, __base__=BaseModel, __doc__=spec['doc'], **fields)
    edge_mods = (edge_operations, EntityEdge, EntityNode, EpisodicNode, GraphitiClients, GraphProvider,
                 default_chat_prompt_library, edge_models, ResolutionTrace)
    bulk_mods = (bulk_utils, EntityEdge, EntityNode, EpisodicNode, EpisodicEdge, GraphitiClients, GraphProvider,
                 default_chat_prompt_library, bulk_models, GraphOperationsInterface, SearchInterface, ResolutionTrace)

    records = []
    for family, cases, run in (('node', node_cases(), lambda c, t: run_node_case(c, m, t)),
                               ('edge', edge_cases(), lambda c, t: run_edge_case(c, edge_mods, t)),
                               ('bulk', bulk_cases(), lambda c, t: run_bulk_case(c, bulk_mods, t))):
        for case in cases:
            traced = run(case, True)
            plain = run(case, False)
            if without_trace(traced) != without_trace(plain) or plain['trace'] is not None:
                raise SystemExit(f'The {family} case {case["fn"]} gives another output with a trace.')
            records.append({'family': family, 'in': case, 'out': traced})

    def dumps(value):
        return json.dumps(value, ensure_ascii=True, allow_nan=False, separators=(',', ':'))

    text = ('{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","fixed_now":"' + FIXED_NOW.isoformat()
            + '","cases":[\n' + ',\n'.join(dumps(r) for r in records) + '\n]}\n')
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
