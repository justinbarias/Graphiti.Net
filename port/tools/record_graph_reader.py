"""Records results of graphiti_core/ingestion/graph_reader.py from real Python (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_graph_reader.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_graph_reader.py --check

Plan 0004, layer 8, packet P8b. The tool calls each method of GraphReader with each argument at its default and with a
value, with fakes at the network boundary:

- a driver with a graph operations interface (node_get_by_names, node_get_by_attribute_values, node_get_by_uuids,
  episodic_node_get_by_uuids, edge_get_by_uuids) and a search interface (node_similarity_search, edge_fulltext_search,
  edge_similarity_search). Each answer comes from the tables of the case. A case can make a lookup raise
  NodeNotFoundError, EdgeNotFoundError, EdgesNotFoundError, or RuntimeError.
- an embedder that gives the i-th distinct text of the case the vector [1000 + i, 0.5]. A search fake finds the text
  of a vector from it, and a stored fact embedding [k, 0.25] names the fact `vec<k>`.
- an LLM client and a cross-encoder that the reader never calls.

Each fake yields once (asyncio.sleep(0)) and then records its call, so the calls are one ordered list. Each case keeps
the largest count of fake calls in flight, and a case with more searches than its limit of max_coroutines.
graph_reader writes no log line, and the reads below it write none on these paths.

It writes tests/Port.Evaluation/values/graph_reader.json. --check writes nothing and exits 1 if the file differs from
what the run would write. The tool needs Python 3.12 and the pinned upstream in the folder of the variable
GRAPHITI_UPSTREAM, or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import json
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'graph_reader.json'

DT = '2025-01-01T00:00:00+00:00'


def node(uuid, name, labels=None, group_id='g', attributes=None, embedding=None):
    data = {'uuid': uuid, 'name': name, 'group_id': group_id, 'labels': labels or ['Entity'], 'created_at': DT,
            'summary': '', 'attributes': attributes or {}}
    if embedding is not None:
        data['name_embedding'] = embedding
    return data


def edge(uuid, source, target, fact, group_id='g', embedding=None):
    data = {'uuid': uuid, 'group_id': group_id, 'source_node_uuid': source, 'target_node_uuid': target,
            'name': 'RELATES_TO', 'fact': fact, 'created_at': DT}
    if embedding is not None:
        data['fact_embedding'] = embedding
    return data


def episode(uuid, group_id='g'):
    return {'uuid': uuid, 'name': 'episode ' + uuid, 'group_id': group_id, 'source': 'message',
            'source_description': 'chat', 'content': 'Ana works at Acme.', 'valid_at': DT, 'created_at': DT}


def node_view(n) -> dict:
    return {'uuid': n.uuid, 'name': n.name, 'group_id': n.group_id, 'labels': list(n.labels),
            'name_embedding': n.name_embedding, 'attributes': n.attributes}


def edge_view(e) -> dict:
    return {'uuid': e.uuid, 'group_id': e.group_id, 'source_node_uuid': e.source_node_uuid,
            'target_node_uuid': e.target_node_uuid, 'fact': e.fact, 'fact_embedding': e.fact_embedding}


ANA = node('s-ana', 'Ana', ['Entity', 'Person'], attributes={'email': 'ana@x.io'})
ANA2 = node('s-ana2', 'Ana Lopez', ['Entity', 'Person'], attributes={'email': 'ana@x.io'})
ACME = node('s-acme', 'Acme', ['Entity', 'Company'])
WORK = edge('s-work', 'a', 'b', 'Ana works at Acme')
BACK = edge('s-back', 'b', 'a', 'Acme employs Ana')
LOOP = edge('s-loop', 'a', 'a', 'Ana is Ana')
OTHER = edge('s-other', 'a', 'c', 'Ana knows Bob')
HOME = edge('s-home', 'c', 'd', 'Bob lives in Lyon', group_id='h')


def search(fact, pair_bm25=(), pair_cos=(), all_bm25=(), all_cos=()):
    return {fact: {'pair': {'bm25': list(pair_bm25), 'cos': list(pair_cos)},
                   'all': {'bm25': list(all_bm25), 'cos': list(all_cos)}}}


def cases() -> list[dict]:
    out = []
    x_ana = node('x-ana', 'Ana', ['Entity', 'Person'], attributes={'email': 'ana@x.io'}, embedding=[7.0, 0.25])
    x_acme = node('x-acme', 'Acme Corp')
    x_bob = node('x-bob', 'Bob', embedding=[8.0, 0.25])
    base = {'exact': [], 'identity': {}, 'search': {}, 'stored_nodes': [], 'stored_episodes': [], 'stored_edges': [],
            'mode': None, 'max_coroutines': None}

    def add(**kw):
        case = dict(base)
        case.update(kw)
        out.append(case)

    node_search = {'vec7': [ANA2], 'Acme Corp': [ACME, ANA], 'vec8': []}
    add(fn='node_candidates', nodes=[])
    add(fn='node_candidates', nodes=[x_ana, x_acme, x_bob], exact=[ANA, ACME], search=node_search)
    add(fn='node_candidates', nodes=[x_ana, x_acme, x_bob], exact=[ANA], search=node_search,
        identity={'Person': [ANA2]}, identity_properties={'Person': ['email']})
    add(fn='node_candidates', nodes=[x_ana, x_acme], identity_properties=None, search=node_search)
    add(fn='node_candidates', nodes=[x_acme, node('x-acme2', 'Acme Inc')], max_coroutines=1, search=node_search)

    e_work = edge('x-work', 'a', 'b', 'Ana works at Acme', embedding=[1.0, 0.25])
    e_other = edge('x-other', 'a', 'c', 'Ana knows Bob', embedding=[2.0, 0.25])
    e_plain = edge('x-plain', 'b', 'a', 'Acme hired Ana')
    e_home = edge('x-home', 'c', 'd', 'Bob lives in Lyon', group_id='h', embedding=[3.0, 0.25])
    edge_search = {**search('Ana works at Acme', pair_bm25=[WORK, LOOP, OTHER], pair_cos=[BACK, WORK],
                            all_bm25=[OTHER, WORK], all_cos=[HOME]),
                   **search('Acme hired Ana', pair_bm25=[BACK], all_cos=[WORK, BACK]),
                   **search('Bob lives in Lyon', pair_cos=[HOME], all_bm25=[HOME, OTHER])}
    for fn in ('edge_duplicate_candidates', 'edge_invalidation_candidates'):
        add(fn=fn, edges=[])
        add(fn=fn, edges=[e_work, e_other, e_plain, e_home], search=edge_search)
        add(fn=fn, edges=[e_work, e_plain, e_home], search=edge_search, max_coroutines=1)
        add(fn=fn, edges=[e_work, e_other, e_plain], search=edge_search, max_coroutines=2)

    stored_nodes = [ANA, ACME, node('s-h', 'Hidden', group_id='h')]
    add(fn='nodes_by_uuids', uuids=[], group_id='g')
    add(fn='nodes_by_uuids', uuids=['s-ana', 's-h', 'missing'], group_id='g', stored_nodes=stored_nodes)
    add(fn='nodes_by_uuids', uuids=['s-ana'], group_id='h', stored_nodes=stored_nodes)
    add(fn='nodes_by_uuids', uuids=['s-ana'], group_id='g', mode='node_not_found')
    add(fn='nodes_by_uuids', uuids=['s-ana'], group_id='g', mode='runtime')
    stored_episodes = [episode('ep1'), episode('ep2', 'h')]
    add(fn='episodes_by_uuids', uuids=[])
    add(fn='episodes_by_uuids', uuids=['ep2', 'ep1', 'missing'], stored_episodes=stored_episodes)
    add(fn='episodes_by_uuids', uuids=['ep1'], mode='node_not_found')
    stored_edges = [WORK, HOME]
    add(fn='edges_by_uuids', uuids=[])
    add(fn='edges_by_uuids', uuids=['s-home', 's-work', 'missing'], stored_edges=stored_edges)
    add(fn='edges_by_uuids', uuids=['s-work'], mode='edge_not_found')
    add(fn='edges_by_uuids', uuids=['s-work'], mode='edges_not_found')
    add(fn='edges_by_uuids', uuids=['s-work'], mode='runtime')
    return out


async def run_case(case: dict, mods) -> dict:
    (GraphReader, EntityNode, EntityEdge, EpisodicNode, GraphitiClients, GraphProvider, GraphOperationsInterface,
     SearchInterface, NoOpTracer, errors) = mods
    events: list[dict] = []
    state = {'in_flight': 0, 'max_in_flight': 0}
    texts: list[str] = []

    async def enter(kind, record):
        state['in_flight'] += 1
        state['max_in_flight'] = max(state['max_in_flight'], state['in_flight'])
        try:
            await asyncio.sleep(0)
            events.append({'kind': kind, **record})
        finally:
            state['in_flight'] -= 1

    def fail(kind):
        mode = case['mode']
        if mode == 'node_not_found' and kind == 'node':
            raise errors.NodeNotFoundError('s-ana')
        if mode == 'edge_not_found' and kind == 'edge':
            raise errors.EdgeNotFoundError('s-work')
        if mode == 'edges_not_found' and kind == 'edge':
            raise errors.EdgesNotFoundError(['s-work', 's-x'])
        if mode == 'runtime':
            raise RuntimeError('lookup down')

    def text_of(vector):
        key = int(vector[0])
        return texts[key - 1000] if key >= 1000 else f'vec{key}'

    def edge_answer(fact, search_filter, kind):
        scope = 'pair' if search_filter.edge_source_node_uuids is not None else 'all'
        return [EntityEdge(**e) for e in case['search'].get(fact, {}).get(scope, {}).get(kind, [])]

    class FakeOps(GraphOperationsInterface):
        async def node_get_by_names(self, _cls, driver, names, group_ids):
            await enter('node_get_by_names', {'names': list(names), 'group_ids': list(group_ids)})
            return [EntityNode(**n) for n in case['exact'] if n['name'] in names]

        async def node_get_by_attribute_values(self, _cls, driver, group_id, attributes, node_labels=None, limit=100):
            await enter('node_get_by_attribute_values', {'group_id': group_id, 'attributes': attributes,
                                                         'labels': node_labels, 'limit': limit})
            return [EntityNode(**n) for n in case['identity'].get(node_labels[0], [])]

        async def node_get_by_uuids(self, _cls, driver, uuids, group_id=None):
            await enter('node_get_by_uuids', {'uuids': list(uuids), 'group_id': group_id})
            fail('node')
            return [EntityNode(**n) for n in case['stored_nodes']
                    if n['uuid'] in uuids and (group_id is None or n['group_id'] == group_id)]

        async def episodic_node_get_by_uuids(self, _cls, driver, uuids):
            await enter('episodic_node_get_by_uuids', {'uuids': list(uuids)})
            fail('node')
            return [EpisodicNode(**e) for e in case['stored_episodes'] if e['uuid'] in uuids]

        async def edge_get_by_uuids(self, _cls, driver, uuids):
            await enter('edge_get_by_uuids', {'uuids': list(uuids)})
            fail('edge')
            return [EntityEdge(**e) for e in case['stored_edges'] if e['uuid'] in uuids]

    class FakeSearch(SearchInterface):
        async def node_similarity_search(self, driver, search_vector, search_filter, group_ids=None, limit=100,
                                         min_score=0.7):
            text = text_of(search_vector)
            await enter('node_similarity_search', {'query': text, 'group_ids': group_ids, 'limit': limit,
                                                   'min_score': min_score})
            return [EntityNode(**n) for n in case['search'].get(text, [])]

        async def edge_fulltext_search(self, driver, query, search_filter, group_ids=None, limit=100):
            await enter('edge_fulltext_search', {'query': query, 'group_ids': group_ids, 'limit': limit,
                                                 'source': search_filter.edge_source_node_uuids,
                                                 'target': search_filter.edge_target_node_uuids})
            return edge_answer(query, search_filter, 'bm25')

        async def edge_similarity_search(self, driver, search_vector, source_node_uuid, target_node_uuid,
                                         search_filter, group_ids=None, limit=100, min_score=0.7):
            text = text_of(search_vector)
            await enter('edge_similarity_search', {'query': text, 'source_node_uuid': source_node_uuid,
                                                   'target_node_uuid': target_node_uuid, 'group_ids': group_ids,
                                                   'limit': limit, 'min_score': min_score,
                                                   'source': search_filter.edge_source_node_uuids,
                                                   'target': search_filter.edge_target_node_uuids})
            fact = next((e['fact'] for e in case.get('edges', []) if e.get('fact_embedding') == list(search_vector)), text)
            return edge_answer(fact, search_filter, 'cos')

    class FakeEmbedder:
        def vector(self, text):
            if text not in texts:
                texts.append(text)
            return [float(1000 + texts.index(text)), 0.5]

        async def create(self, input_data):
            await enter('embed_create', {'input': list(input_data)})
            return self.vector(input_data[0])

        async def create_batch(self, input_data_list):
            await enter('embed_batch', {'input': list(input_data_list)})
            return [self.vector(t) for t in input_data_list]

    class FakeDriver:
        provider = GraphProvider.NEO4J
        graph_operations_interface = FakeOps()
        search_interface = FakeSearch()

        async def execute_query(self, cypher_query_, **kwargs):
            raise AssertionError('execute_query is not used')

    class Unused:
        async def generate_response(self, *args, **kwargs):
            raise AssertionError('not used')

        async def rank(self, *args, **kwargs):
            raise AssertionError('not used')

    clients = GraphitiClients.model_construct(driver=FakeDriver(), llm_client=Unused(), embedder=FakeEmbedder(),
                                              cross_encoder=Unused(), tracer=NoOpTracer(),
                                              max_coroutines=case['max_coroutines'], prompt_library=None)
    reader = GraphReader(clients)
    fn = case['fn']
    output: dict = {}
    try:
        if fn == 'node_candidates':
            nodes = [EntityNode(**n) for n in case['nodes']]
            if 'identity_properties' in case:
                result = await reader.node_candidates(nodes, case['identity_properties'])
            else:
                result = await reader.node_candidates(nodes)
            output['ok'] = [[node_view(n) for n in group] for group in result]
        elif fn in ('edge_duplicate_candidates', 'edge_invalidation_candidates'):
            result = await getattr(reader, fn)([EntityEdge(**e) for e in case['edges']])
            output['ok'] = [[edge_view(e) for e in group] for group in result]
        elif fn == 'nodes_by_uuids':
            output['ok'] = [node_view(n) for n in await reader.nodes_by_uuids(case['uuids'], case['group_id'])]
        elif fn == 'episodes_by_uuids':
            output['ok'] = [[e.uuid, e.group_id] for e in await reader.episodes_by_uuids(case['uuids'])]
        elif fn == 'edges_by_uuids':
            output['ok'] = [edge_view(e) for e in await reader.edges_by_uuids(case['uuids'])]
        else:
            raise AssertionError(fn)
    except Exception as error:  # noqa: BLE001 - the recording keeps each exception
        output['error'] = {'type': type(error).__name__, 'text': str(error)}
    output['events'] = events
    output['max_in_flight'] = state['max_in_flight']
    return output


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core import errors
    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.driver.graph_operations.graph_operations import GraphOperationsInterface
    from graphiti_core.driver.search_interface.search_interface import SearchInterface
    from graphiti_core.edges import EntityEdge
    from graphiti_core.graphiti_types import GraphitiClients
    from graphiti_core.ingestion.graph_reader import GraphReader
    from graphiti_core.nodes import EntityNode, EpisodicNode
    from graphiti_core.tracer import NoOpTracer

    mods = (GraphReader, EntityNode, EntityEdge, EpisodicNode, GraphitiClients, GraphProvider, GraphOperationsInterface,
            SearchInterface, NoOpTracer, errors)
    records = [{'in': case, 'out': asyncio.run(run_case(case, mods))} for case in cases()]

    def dumps(value):
        return json.dumps(value, ensure_ascii=True, allow_nan=False, separators=(',', ':'))

    text = ('{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","cases":[\n'
            + ',\n'.join(dumps(r) for r in records) + '\n]}\n')
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
