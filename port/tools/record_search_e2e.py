"""Records what the upstream search returns for each search recipe on a real Neo4j (plan 0004, test E6).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_search_e2e.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_search_e2e.py --check

The tool seeds one graph through the Python models (the save method of each node and edge) and the Python
Neo4jDriver: entity nodes, entity edges, episodes with MENTIONS edges, and communities with HAS_MEMBER edges. Then it
runs graphiti_core.search.search.search with each of the 16 recipes of search_config_recipes.py and each of 3 queries.
It records the ranked results of each kind as logical keys (the name of a node, the fact of an edge, the name of an
episode, the name of a community) and the reranker scores. It records no uuid.

The two network clients are fakes that C# computes to the same bits:

- The embedder takes SHA-256 of the UTF-8 text. Number k (0 to 7) is the big-endian unsigned 16-bit integer at
  bytes 2k and 2k+1 of the digest, minus 32768, divided by 32768.
- The cross-encoder takes SHA-256 of the UTF-8 query, one zero byte, and the UTF-8 passage. The score is the
  big-endian unsigned 32-bit integer at bytes 0 to 3, divided by 2**32. rank() sorts the passages by score,
  highest first.

The result must not depend on other data of the database, because the BM25 score of Neo4j reads the whole index:

- Each query is one invented word. Only the texts of this tool hold it.
- Each text that a full-text index reads has the same count of words in its kind. The text of a kind that holds a
  query word holds it a count of times that no other text of the kind has. So the rank of a full-text hit comes from
  the count of the query word.
- The tool stops with an error for two cosine scores of a kind and a query that are closer than 1e-6, and for a
  cosine score closer than 1e-6 to the threshold of the recipes.
- The tool stops with an error when a breadth-first search returns as many rows as its limit, because those
  queries have no ORDER BY and the order of their rows is the order of the store.
- The tool makes its result two times. The second time, the group e2e_py_search_noise holds nodes and edges with the
  same invented words. A difference between the two runs is an error. A key of the noise group in a result is an
  error.

The scores in the recording are reranker scores: reciprocal rank fusion, maximal marginal relevance, cross-encoder,
node distance, and episode mentions scores. No recorded score is a BM25 score.

Each uuid is built from the group id and an index: the first 8 hex characters of SHA-256 of the group id, then
-0000-4000-8000- and the index as 12 hex characters. The order of the uuids is the order of the indexes in each group.

It writes tests/Graphiti.Net.E2E.Tests/values/search_e2e.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The tool needs a Neo4j at NEO4J_URI (default bolt://localhost:7687) with
NEO4J_USER and NEO4J_PASSWORD (the defaults of Neo4jSidecar.cs), Python 3.12, and the pinned upstream. It reads the
upstream folder from GRAPHITI_UPSTREAM, then from .upstream/graphiti in the repository, then from the main checkout
when this is a git worktree.

The tool uses the groups e2e_py_search_fixture and e2e_py_search_noise. It deletes the data of both groups before it
starts and after it ends. It touches no other data and drops no index.
"""
from __future__ import annotations

import asyncio
import hashlib
import json
import math
import os
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'tests' / 'Graphiti.Net.E2E.Tests' / 'values' / 'search_e2e.json'


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

GROUP = 'e2e_py_search_fixture'
NOISE_GROUP = 'e2e_py_search_noise'

# The invented query words. No other text of the database holds them.
WORDS = ['quorbix7k', 'zandeltu9q', 'vexmorin4w']
FILLER = 'lorem'

RECIPES = [
    'COMBINED_HYBRID_SEARCH_RRF',
    'COMBINED_HYBRID_SEARCH_MMR',
    'COMBINED_HYBRID_SEARCH_CROSS_ENCODER',
    'EDGE_HYBRID_SEARCH_RRF',
    'EDGE_HYBRID_SEARCH_MMR',
    'EDGE_HYBRID_SEARCH_NODE_DISTANCE',
    'EDGE_HYBRID_SEARCH_EPISODE_MENTIONS',
    'EDGE_HYBRID_SEARCH_CROSS_ENCODER',
    'NODE_HYBRID_SEARCH_RRF',
    'NODE_HYBRID_SEARCH_MMR',
    'NODE_HYBRID_SEARCH_NODE_DISTANCE',
    'NODE_HYBRID_SEARCH_EPISODE_MENTIONS',
    'NODE_HYBRID_SEARCH_CROSS_ENCODER',
    'COMMUNITY_HYBRID_SEARCH_RRF',
    'COMMUNITY_HYBRID_SEARCH_MMR',
    'COMMUNITY_HYBRID_SEARCH_CROSS_ENCODER',
]
CENTER_RECIPES = {'EDGE_HYBRID_SEARCH_NODE_DISTANCE', 'NODE_HYBRID_SEARCH_NODE_DISTANCE'}

# The bases of the uuid indexes of each kind.
ENTITY, EDGE, EPISODE, COMMUNITY, MENTION, MEMBER = 0x100, 0x200, 0x300, 0x400, 0x500, 0x600

# The center node of the node distance recipes: entity 0, a hub.
CENTER = ('entity', 0)

SIM_MIN_SCORE = 0.6
TIE_GAP = 1e-6

UTC = timezone.utc
BASE_TIME = datetime(2025, 1, 1, 0, 0, 0, tzinfo=UTC)

# The three queries. Query 1 passes bfs_origin_node_uuids. Query 2 passes a SearchFilters value.
QUERIES = [
    {'text': WORDS[0]},
    {'text': WORDS[1], 'bfs_origins': [['episode', 0], ['entity', 1]]},
    {
        'text': WORDS[2],
        'filter': {
            'node_labels': ['Person'],
            'edge_types': ['KNOWS'],
            'valid_at': [
                [{'date': '2025-09-01T00:00:00+00:00', 'comparison_operator': 'less_than_equal'}],
                [{'date': None, 'comparison_operator': 'is_null'}],
            ],
        },
    },
]


# ---------- the fakes at the network boundary ----------

def embed(text: str) -> list[float]:
    digest = hashlib.sha256(text.encode('utf-8')).digest()
    return [(int.from_bytes(digest[2 * k:2 * k + 2], 'big') - 32768) / 32768 for k in range(8)]


def cross_score(query: str, passage: str) -> float:
    digest = hashlib.sha256(query.encode('utf-8') + b'\x00' + passage.encode('utf-8')).digest()
    return int.from_bytes(digest[:4], 'big') / 2**32


# ---------- the fixture ----------

def uid(group: str, number: int) -> str:
    prefix = hashlib.sha256(group.encode('utf-8')).hexdigest()[:8]
    return f'{prefix}-0000-4000-8000-{number:012x}'


def text(prefix: list[str], counts: list[int], length: int) -> str:
    words = list(prefix)
    for word, count in zip(WORDS, counts):
        words.extend([word] * count)
    if len(words) > length:
        raise RuntimeError(f'a text has {len(words)} words, more than {length}')
    words.extend([FILLER] * (length - len(words)))
    return ' '.join(words)


def counts(i: int, modulus: int) -> list[int]:
    """Counts of the three words for item i. For each word, the counts of the items of a kind differ (modulus prime)."""
    return [((i + 1) * (q + 2) + q) % modulus for q in range(3)]


def iso(value: datetime | None) -> str | None:
    return None if value is None else value.isoformat()


ENTITY_NAMES = [
    'Arden Vale', 'Brisk Harbor', 'Cobalt Mesa', 'Dunmore Reach', 'Ember Quay', 'Fallow Crest', 'Gilded Ford',
    'Hollow Pine', 'Ivory Sound', 'Juniper Gate', 'Kestrel Moor', 'Lantern Rise', 'Marrow Field', 'Nettle Bay',
    'Oaken Spur', 'Pallid Cove', 'Quarry Dell', 'Russet Knoll', 'Sable Wharf', 'Tamarind Hill', 'Umber Glen',
    'Verdant Shoal', 'Willow Bend',
]
HUBS = 3
LABELS = ['Person', 'Place', 'Thing']
EDGE_TYPES = ['KNOWS', 'LIKES', 'OWNS']
EDGE_COUNT = 18
EPISODE_COUNT = 8
COMMUNITY_COUNT = 5


def fixture_spec(group: str) -> dict:
    """The fixture as plain values. The C# test builds the same values from its own code and compares them."""
    entities = []
    for i, name in enumerate(ENTITY_NAMES):
        if i < HUBS:
            c = [0, 0, 0]
            label = 'Place' if i == 2 else 'Person'
        else:
            c = counts(i - HUBS, 23)
            label = LABELS[i % 3]
        entities.append({
            'index': i,
            'name': name,
            'labels': ['Entity', label],
            'summary': text([], c, 70),
            'created_at': iso(BASE_TIME + timedelta(minutes=i)),
            'name_embedding': embed(name),
        })
    edges = []
    for k in range(EDGE_COUNT):
        hub = k % HUBS
        target = HUBS + (k * 7) % (len(ENTITY_NAMES) - HUBS)
        fact = text([f'fact{k:02d}'], counts(k, 19), 60)
        valid_at = None if k % 4 == 0 else datetime(2025, 1 + k % 12, 1, tzinfo=UTC)
        invalid_at = datetime(2026, 1 + k % 12, 2, tzinfo=UTC) if k % 5 == 1 else None
        expired_at = datetime(2026, 6, 1 + k, tzinfo=UTC) if k % 7 == 3 else None
        edges.append({
            'index': k,
            'source': hub,
            'target': target,
            'name': EDGE_TYPES[(k // HUBS) % 3],
            'fact': fact,
            'created_at': iso(BASE_TIME + timedelta(hours=k)),
            'valid_at': iso(valid_at),
            'invalid_at': iso(invalid_at),
            'expired_at': iso(expired_at),
            'episodes': list(range(k % 4)),
            'fact_embedding': embed(fact),
        })
    episodes = []
    for e in range(EPISODE_COUNT):
        episodes.append({
            'index': e,
            'name': f'episode {e:02d}',
            'content': text([f'ep{e:02d}'], counts(e, 11), 40),
            'source': 'message',
            'source_description': 'fixture',
            'created_at': iso(BASE_TIME + timedelta(days=e)),
            'valid_at': iso(BASE_TIME + timedelta(days=e, hours=1)),
        })
    mentions = []
    for e in range(EPISODE_COUNT):
        targets = [HUBS + (e * 3 + t) % (len(ENTITY_NAMES) - HUBS) for t in range(1 + e % 4)]
        if e % 3 == 2:
            targets.append(0)
        for entity in targets:
            mentions.append({'index': len(mentions), 'episode': e, 'entity': entity,
                             'created_at': iso(BASE_TIME + timedelta(days=e, minutes=len(mentions)))})
    communities = []
    for c in range(COMMUNITY_COUNT):
        name = text([f'community{c}'], counts(c, 7), 20)
        communities.append({
            'index': c,
            'name': name,
            'summary': 'fixture community',
            'created_at': iso(BASE_TIME + timedelta(days=30 + c)),
            'name_embedding': embed(name),
        })
    members = []
    for c in range(COMMUNITY_COUNT):
        for t in range(2 + c % 3):
            members.append({'index': len(members), 'community': c,
                            'entity': HUBS + (c * 2 + t) % (len(ENTITY_NAMES) - HUBS),
                            'created_at': iso(BASE_TIME + timedelta(days=40, minutes=len(members)))})
    return {'entities': entities, 'edges': edges, 'episodes': episodes, 'mentions': mentions,
            'communities': communities, 'members': members}


def noise_spec() -> dict:
    """A second group with the same invented words, other counts, and other lengths. Each key starts with noise."""
    entities = []
    for i in range(5):
        name = f'noise entity {i}'
        entities.append({
            'index': i, 'name': name, 'labels': ['Entity', LABELS[i % 3]],
            'summary': text([], [i + 2, 7 - i, 3], 25),
            'created_at': iso(BASE_TIME + timedelta(minutes=i)), 'name_embedding': embed(name),
        })
    edges = []
    for k in range(4):
        fact = text([f'noisefact{k}'], [k + 1, 9 - k, 2 + k], 30)
        edges.append({
            'index': k, 'source': k, 'target': k + 1, 'name': 'KNOWS', 'fact': fact,
            'created_at': iso(BASE_TIME + timedelta(hours=k)), 'valid_at': None, 'invalid_at': None,
            'expired_at': None, 'episodes': [], 'fact_embedding': embed(fact),
        })
    episodes = []
    for e in range(3):
        episodes.append({
            'index': e, 'name': f'noise episode {e}', 'content': text([f'noiseep{e}'], [e + 3, 2, 5 - e], 22),
            'source': 'message', 'source_description': 'noise',
            'created_at': iso(BASE_TIME + timedelta(days=e)), 'valid_at': iso(BASE_TIME + timedelta(days=e)),
        })
    mentions = [{'index': e, 'episode': e, 'entity': e, 'created_at': iso(BASE_TIME)} for e in range(3)]
    communities = []
    for c in range(2):
        name = text([f'noisecommunity{c}'], [c + 4, 1, 3 + c], 12)
        communities.append({
            'index': c, 'name': name, 'summary': 'noise community',
            'created_at': iso(BASE_TIME), 'name_embedding': embed(name),
        })
    members = [{'index': c, 'community': c, 'entity': c, 'created_at': iso(BASE_TIME)} for c in range(2)]
    return {'entities': entities, 'edges': edges, 'episodes': episodes, 'mentions': mentions,
            'communities': communities, 'members': members}


# ---------- seeding ----------

async def seed(driver, group: str, spec: dict):
    from graphiti_core.edges import CommunityEdge, EntityEdge, EpisodicEdge
    from graphiti_core.nodes import CommunityNode, EntityNode, EpisodeType, EpisodicNode

    def when(value):
        return None if value is None else datetime.fromisoformat(value)

    for item in spec['entities']:
        await EntityNode(
            uuid=uid(group, ENTITY + item['index']), name=item['name'], group_id=group,
            labels=[label for label in item['labels'] if label != 'Entity'], created_at=when(item['created_at']),
            name_embedding=item['name_embedding'], summary=item['summary'],
        ).save(driver)
    for item in spec['episodes']:
        await EpisodicNode(
            uuid=uid(group, EPISODE + item['index']), name=item['name'], group_id=group,
            created_at=when(item['created_at']), source=EpisodeType(item['source']),
            source_description=item['source_description'], content=item['content'], valid_at=when(item['valid_at']),
        ).save(driver)
    for item in spec['communities']:
        await CommunityNode(
            uuid=uid(group, COMMUNITY + item['index']), name=item['name'], group_id=group,
            created_at=when(item['created_at']), name_embedding=item['name_embedding'], summary=item['summary'],
        ).save(driver)
    for item in spec['edges']:
        await EntityEdge(
            uuid=uid(group, EDGE + item['index']), group_id=group,
            source_node_uuid=uid(group, ENTITY + item['source']), target_node_uuid=uid(group, ENTITY + item['target']),
            created_at=when(item['created_at']), name=item['name'], fact=item['fact'],
            fact_embedding=item['fact_embedding'], episodes=[uid(group, EPISODE + e) for e in item['episodes']],
            valid_at=when(item['valid_at']), invalid_at=when(item['invalid_at']), expired_at=when(item['expired_at']),
        ).save(driver)
    for item in spec['mentions']:
        await EpisodicEdge(
            uuid=uid(group, MENTION + item['index']), group_id=group,
            source_node_uuid=uid(group, EPISODE + item['episode']), target_node_uuid=uid(group, ENTITY + item['entity']),
            created_at=when(item['created_at']),
        ).save(driver)
    for item in spec['members']:
        await CommunityEdge(
            uuid=uid(group, MEMBER + item['index']), group_id=group,
            source_node_uuid=uid(group, COMMUNITY + item['community']),
            target_node_uuid=uid(group, ENTITY + item['entity']), created_at=when(item['created_at']),
        ).save(driver)


async def cleanup(driver):
    for label in ['Entity', 'Episodic', 'Community']:
        await driver.execute_query(
            f'MATCH (n:{label}) WHERE n.group_id IN $groups DETACH DELETE n', groups=[GROUP, NOISE_GROUP])


# ---------- checks of the fixture ----------

def cosine(a: list[float], b: list[float]) -> float:
    dot = sum(x * y for x, y in zip(a, b))
    return dot / (math.sqrt(sum(x * x for x in a)) * math.sqrt(sum(y * y for y in b)))


def check_cosine_ties(spec: dict):
    """Neo4j orders the similarity hits by (1 + cosine) / 2. Two equal scores would leave the order to the store."""
    kinds = {
        'entities': [item['name_embedding'] for item in spec['entities']],
        'edges': [item['fact_embedding'] for item in spec['edges']],
        'communities': [item['name_embedding'] for item in spec['communities']],
    }
    for query in QUERIES:
        vector = embed(query['text'])
        for kind, embeddings in kinds.items():
            scores = sorted((1 + cosine(vector, e)) / 2 for e in embeddings)
            for a, b in zip(scores, scores[1:]):
                if b - a < TIE_GAP:
                    raise RuntimeError(f'two cosine scores of {kind} for {query["text"]!r} tie: {a} and {b}')
            for s in scores:
                if abs(s - SIM_MIN_SCORE) < TIE_GAP:
                    raise RuntimeError(f'a cosine score of {kind} for {query["text"]!r} is at the threshold: {s}')


# ---------- the fake clients ----------

def make_clients(driver):
    from graphiti_core.cross_encoder.client import CrossEncoderClient
    from graphiti_core.embedder.client import EmbedderClient
    from graphiti_core.graphiti_types import GraphitiClients
    from graphiti_core.tracer import NoOpTracer

    class HashEmbedder(EmbedderClient):
        async def create(self, input_data):
            return embed(input_data[0])

        async def create_batch(self, input_data_list):
            return [embed(item) for item in input_data_list]

    class HashCrossEncoder(CrossEncoderClient):
        async def rank(self, query, passages):
            ranked = [(passage, cross_score(query, passage)) for passage in passages]
            ranked.sort(key=lambda pair: pair[1], reverse=True)
            return ranked

    # The search reads no LLM client, so the model holds none.
    return GraphitiClients.model_construct(
        driver=driver, llm_client=None, embedder=HashEmbedder(), cross_encoder=HashCrossEncoder(),
        tracer=NoOpTracer(), max_coroutines=None)


class TruncationGuard:
    """Fails a run in which a breadth-first search fills its limit. Those queries have no ORDER BY."""

    def __init__(self, driver):
        original = driver.execute_query

        async def guarded(cypher, /, **kwargs):
            # The search passes a parameter named query, so the text of the query is positional only.
            result = await original(cypher, **kwargs)
            if 'bfs_origin_node_uuids' in kwargs and len(result[0]) >= kwargs['limit']:
                raise RuntimeError(f'a breadth-first search returned {len(result[0])} rows for limit {kwargs["limit"]}')
            return result

        driver.execute_query = guarded


def search_filter_of(spec):
    from graphiti_core.search.search_filters import ComparisonOperator, DateFilter, SearchFilters

    if spec is None:
        return SearchFilters()
    return SearchFilters(
        node_labels=spec['node_labels'],
        edge_types=spec['edge_types'],
        valid_at=[
            [DateFilter(date=None if f['date'] is None else datetime.fromisoformat(f['date']),
                        comparison_operator=ComparisonOperator[f['comparison_operator']]) for f in or_list]
            for or_list in spec['valid_at']
        ],
    )


def number(value):
    """A score as JSON: an int stays an int. A numpy float becomes a Python float."""
    if isinstance(value, bool):
        raise TypeError('a score is a bool')
    if isinstance(value, int):
        return value
    return float(value)


async def run_all(clients) -> list:
    from graphiti_core.search import search_config_recipes
    from graphiti_core.search.search import search

    out = []
    for name in RECIPES:
        config = getattr(search_config_recipes, name)
        for index, query in enumerate(QUERIES):
            kind, at = CENTER
            center = uid(GROUP, ENTITY + at) if name in CENTER_RECIPES else None
            origins = None
            if 'bfs_origins' in query:
                bases = {'entity': ENTITY, 'episode': EPISODE}
                origins = [uid(GROUP, bases[k] + i) for k, i in query['bfs_origins']]
            results = await search(
                clients, query['text'], [GROUP], config, search_filter_of(query.get('filter')),
                center_node_uuid=center, bfs_origin_node_uuids=origins)
            out.append({
                'recipe': name,
                'query': index,
                'edges': [edge.fact for edge in results.edges],
                'edge_scores': [number(s) for s in results.edge_reranker_scores],
                'nodes': [node.name for node in results.nodes],
                'node_scores': [number(s) for s in results.node_reranker_scores],
                'episodes': [episode.name for episode in results.episodes],
                'episode_scores': [number(s) for s in results.episode_reranker_scores],
                'communities': [community.name for community in results.communities],
                'community_scores': [number(s) for s in results.community_reranker_scores],
            })
    return out


SCORES_OF = {
    'edges': 'edge_scores', 'nodes': 'node_scores', 'episodes': 'episode_scores', 'communities': 'community_scores',
}


def same_runs(first: list, second: list):
    for a, b in zip(first, second, strict=True):
        where = f'{a["recipe"]} query {a["query"]}'
        for kind, scores in SCORES_OF.items():
            if a[kind] != b[kind]:
                raise RuntimeError(f'{where}: the {kind} differ with the noise group:\n{a[kind]}\n{b[kind]}')
            for key in b[kind]:
                if key.startswith('noise'):
                    raise RuntimeError(f'{where}: a key of the noise group is in the result: {key}')
            sa, sb = a[scores], b[scores]
            if len(sa) != len(sb) or any(abs(x - y) > 1e-9 for x, y in zip(sa, sb)):
                raise RuntimeError(f'{where}: the {kind} scores differ with the noise group:\n{sa}\n{sb}')


async def run():
    sys.path.insert(0, str(UPSTREAM))
    import logging

    from graphiti_core.driver.neo4j_driver import Neo4jDriver

    logging.getLogger('graphiti_core').setLevel(logging.CRITICAL)
    logging.getLogger('neo4j').setLevel(logging.CRITICAL)

    fixture = fixture_spec(GROUP)
    noise = noise_spec()
    check_cosine_ties(fixture)

    driver = Neo4jDriver(URI, USER, PASSWORD)
    try:
        await cleanup(driver)
        await driver.build_indices_and_constraints()
        await driver.execute_query('CALL db.awaitIndexes(120)')
        await seed(driver, GROUP, fixture)
        TruncationGuard(driver)
        clients = make_clients(driver)
        first = await run_all(clients)
        await seed(driver, NOISE_GROUP, noise)
        second = await run_all(clients)
        same_runs(first, second)
    finally:
        await cleanup(driver)
        await driver.close()

    return {
        'meta': {
            'words': WORDS,
            'filler': FILLER,
            'recipes': RECIPES,
            'center_recipes': sorted(CENTER_RECIPES),
            'center': list(CENTER),
            'bases': {'entity': ENTITY, 'edge': EDGE, 'episode': EPISODE, 'community': COMMUNITY,
                      'mention': MENTION, 'member': MEMBER},
            'queries': QUERIES,
        },
        'inputs': {'fixture': fixture, 'noise': noise},
        'runs': first,
    }


def render(data) -> str:
    return json.dumps(data, ensure_ascii=False, indent=1) + '\n'


def main() -> int:
    data = asyncio.run(run())
    text_out = render(data)
    if '--check' in sys.argv[1:]:
        current = OUT.read_text(encoding='utf-8') if OUT.exists() else None
        if current != text_out:
            print(f'{OUT.relative_to(ROOT)} is not current. Run the tool without --check.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)} is current')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text_out, encoding='utf-8')
    print(f'wrote {OUT.relative_to(ROOT)} ({len(text_out)} bytes)')
    return 0


if __name__ == '__main__':
    sys.exit(main())
