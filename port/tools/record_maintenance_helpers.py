"""Records results of five maintenance modules from real Python, for the evaluation tests (port/RULES.md, R23, R25).

    .upstream/graphiti/.venv/bin/python -B port/tools/record_maintenance_helpers.py
    .upstream/graphiti/.venv/bin/python -B port/tools/record_maintenance_helpers.py --check

The modules are in graphiti_core/utils/maintenance/: dedup_helpers.py, attribute_utils.py, temporal_edge_utils.py,
dangling_endpoints.py, and graph_data_operations.py (plan 0004, layer 7, packet P7a). The tool calls each function
with generated inputs from a fixed seed. It records the arguments, the result or the exception, and each log line
of graphiti_core with its level, in the order of the calls.

graph_data_operations.py is async and calls the graph driver. The tool gives it a fake driver, a fake session, a
fake transaction, and a fake graph operations interface. Each fake method yields once (asyncio.sleep(0)) and then
records its call: the query text, the parameter keys in order, and each value with its type. The calls and the log
lines are one ordered list.

Two values are fixed on both sides. dangling_endpoints.utc_now gives FIXED_NOW, and the C# test gives the port a
clock with the same time. A new node gets a random uuid4, so the test compares its shape.

The recording leaves out inputs that differ by a documented limit: an int above 64 bits in an environment value or in a
cap (RULES.md R4) and a high surrogate next to a low one (RULES.md, Part 3). It holds each cap that a long holds, up to
9223372036854775807, because Python has no limit on the product `max_len * LIST_TOTAL_LENGTH_MULTIPLIER` and the port
computes it in Int128. Each invalid environment value starts with 'p7a', so that the set of warned values
(_warned_invalid_env) holds no value of another test. The C# test replaces that set, as the upstream test does.

Real Python can give another result than this file for some inputs. A Python set of text iterates in an order that
changes with the hash seed of the process. The port iterates such a set in insertion order (RULES.md R14, decision D43),
so the tool binds the name `set` of dedup_helpers and attribute_utils to a class that iterates in insertion order
(InsertionOrderSet). The order decides the result in two places: a tie between two fuzzy candidates in
_resolve_with_similarity, and the order of the restored fields in apply_capped_attributes (merge_mode='replace'). The tool
runs each such case a second time with the iteration order reversed, and it marks the case with "set_order": true if the
result or a log line differs. For a marked case, real Python gives this recorded result for some hash seeds and the
other result for the others. The port gives the recorded result, which is the choice of R14.

It writes tests/Port.Evaluation/values/maintenance_helpers.json and maintenance_helpers_review.json (the fixed cases that
came with the reviews, so that each file stays under the limit of R25). --check writes nothing and exits 1 if the file
differs from what the run would write. The tool needs Python 3.12 and the pinned upstream. It reads the folder of the
variable GRAPHITI_UPSTREAM, or .upstream/graphiti. It changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import copy
import json
import logging
import math
import os
import random
import re
import sys
from datetime import datetime, timedelta, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'maintenance_helpers.json'
OUT_REVIEW = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'maintenance_helpers_review.json'

SEED = 20261127
FIXED_NOW = datetime(2026, 3, 4, 5, 6, 7, 891011, tzinfo=timezone.utc)

WORDS = ['Alice', 'alice', 'ALICE', 'Johnson', 'Acme', 'Corporation', 'Corp.', 'Dr.', 'Who?!', "O'Brien", 'Smith-Jones',
         'Bob', 'Area', '51', '(Nevada)', 'Hello___World', 'Café', 'Crème', 'École', 'İstanbul', 'İzmir', 'STRAßE',
         'ΣΑΣ', 'ǅungla', 'Ｆｕｌｌ', '東京', '大学', 'こんにちは', '한국어', 'école', 'It’s', "Ross'", 'x', 'a',
         'ab', 'aaaa', 'meaningful', 'name', 'version', '2.0', 'v1.2.3', 'ſmith', 'ıvan', 'KKelvin', 'IBM', 'ibm',
         '\U0001F600', '\U0001F680', '\U0001D400bc', 'z\ud800z', 'a\udfff', '​', '﻿ab', '\x1c', ' \x1f ', '\x85']
SEPS = [' ', ' ', ' ', '  ', '\t', '\n', ' ', '　', '\x1c', '\x1f', ' ', '\x85', '', '-', '_', '.', ',']

LABELS = ['Person', 'Company', 'Party', 'Org', 'Place']
ATTR_KEYS = ['company_id', 'party_id', 'email', 'a', 'b']
CREATED = [
    datetime(2024, 1, 1, tzinfo=timezone.utc),
    datetime(2024, 1, 1, tzinfo=timezone.utc),
    datetime(2023, 6, 1, 12, 30, tzinfo=timezone.utc),
    datetime(2025, 2, 3, 4, 5, 6, 789000, tzinfo=timezone.utc),
]

rng = random.Random(SEED)
records: list[dict] = []
review_records: list[dict] = []
sink = [records]
events: list[dict] = []
UUID4 = re.compile(r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$')


class InsertionOrderSet(set):
    """A set that iterates in insertion order.

    A Python set of text iterates in an order that changes with the hash seed of each process. The port iterates
    such a set in insertion order, which is one of the orders that Python can give (RULES.md R14). The tool binds the
    name `set` of dedup_helpers and attribute_utils to this class, so that both sides use the same order:
    candidate_ids in _resolve_with_similarity and dropped in cap_string_attributes.

    `reverse` is a flag of the class. While it is true, the set iterates in the reverse of the insertion order.
    """

    reverse = False

    def __init__(self, items=()):
        super().__init__()
        self._order = []
        self.update(items)

    def add(self, item):
        if item not in self:
            super().add(item)
            self._order.append(item)

    def update(self, *iterables):
        for iterable in iterables:
            for item in iterable:
                self.add(item)

    def __iter__(self):
        return iter(reversed(self._order) if InsertionOrderSet.reverse else self._order)


def rand_name(max_words: int = 4) -> str:
    parts = []
    for i in range(rng.randint(0, max_words)):
        if i:
            parts.append(rng.choice(SEPS))
        parts.append(rng.choice(WORDS))
    if rng.random() < 0.2:
        parts.insert(0, rng.choice(SEPS))
    if rng.random() < 0.2:
        parts.append(rng.choice(SEPS))
    text = ''.join(parts)
    # A high surrogate is never next to a low one.
    while any('\ud800' <= a <= '\udbff' and '\udc00' <= b <= '\udfff' for a, b in zip(text, text[1:])):
        text = text.replace('\ud800', 'q', 1)
    return text


def perturb(name: str) -> str:
    r = rng.random()
    if r < 0.25:
        return name.upper()
    if r < 0.45:
        return '  ' + name.replace(' ', '   ') + ' '
    if r < 0.6:
        return name.replace(' ', '-')
    if r < 0.75 and len(name) > 8:
        i = rng.randrange(len(name))
        return name[:i] + 'x' + name[i + 1:]
    if r < 0.85:
        return name + '!'
    return name


def dt(value: datetime | None):
    return None if value is None else {'$dt': value.isoformat()}


def node_data(uuid: str, name: str) -> dict:
    labels = ['Entity'] + rng.sample(LABELS, rng.randint(0, 3))
    if rng.random() < 0.15:
        labels = labels[1:]
    if rng.random() < 0.1:
        labels = labels + labels[:1]
    attributes = {}
    for key in rng.sample(ATTR_KEYS, rng.randint(0, 4)):
        attributes[key] = rand_attr_value()
    return {
        'uuid': uuid,
        'name': name,
        'group_id': rng.choice(['g', 'g-1', 'grüp 東京']),
        'labels': labels,
        'created_at': rng.choice(CREATED).isoformat(),
        'summary': rng.choice(['', 'about']),
        'attributes': attributes,
    }


def rand_attr_value():
    return rng.choice([None, '', ' ', ' \t', 'C-1', ' C-1 ', 'c-1', 'x', ' ', 7, 0, -3, 7.0, 2.5, -0.0,
                       float('nan'), float('inf'), True, False, [1], ['C-1'], {'a': 1}, 10 ** 30, 'é東',
                       '\x1c', ' \x1f ', '\x85'])


def node_view(node) -> dict:
    return {
        'uuid': node.uuid,
        'name': node.name,
        'group_id': node.group_id,
        'labels': list(node.labels),
        'created_at': node.created_at.isoformat(),
        'summary': node.summary,
        'attributes': node.attributes,
    }


def plain(value):
    """A value as JSON: a tuple is a list, a set is a sorted list, and an int of 64 bits or more is text."""
    if isinstance(value, bool) or value is None or isinstance(value, (float, str)):
        return value
    if isinstance(value, int):
        return value
    if isinstance(value, tuple):
        return [plain(v) for v in value]
    if isinstance(value, (set, frozenset)):
        return sorted(plain(v) for v in value)
    if isinstance(value, list):
        return [plain(v) for v in value]
    if isinstance(value, dict):
        return {k: plain(v) for k, v in value.items()}
    raise TypeError(type(value).__name__)


def hashes(values) -> list[str]:
    return [str(v) for v in values]


def run_call(fn, args: list, view) -> tuple[dict, list]:
    events.clear()
    try:
        result = {'ok': view(fn(*args))}
    except Exception as error:  # noqa: BLE001 - the recording keeps each exception
        result = {'err': [type(error).__name__, str(error)]}
    return result, list(events)


def call(name: str, fn, args: list, encoded: list, view=plain, ordered: bool = False) -> None:
    # An ordered case runs a second time with the iteration order of the sets reversed. The second run gets a copy of
    # the arguments that the first run has not changed.
    second_args = copy.deepcopy(args) if ordered else None
    result, logs = run_call(fn, args, view)
    record = {'f': name, 'a': encoded, 'r': result, 'logs': logs}
    if ordered:
        InsertionOrderSet.reverse = True
        try:
            other, other_logs = run_call(fn, second_args, view)
        finally:
            InsertionOrderSet.reverse = False
        dump = lambda value: json.dumps(value, ensure_ascii=True, allow_nan=True, separators=(',', ':'))  # noqa: E731
        if dump([other, other_logs]) != dump([result, logs]):
            record['set_order'] = True
    sink[0].append(record)


def record_dedup(dh, EntityNode) -> None:
    names = [rand_name() for _ in range(260)]
    names += ['', ' ', 'ab', 'abc', 'abcdef', 'a b', 'ab cd', 'aabc', 'john smith', '\U0001F600' * 6, '東京大学中央',
              'ſhall', 'İ', 'ı', 'Σ', '\x1c\x1d\x1e\x1f', 'a b c', "''''", "it's", 'ǅ']
    for name in names:
        exact = dh._normalize_string_exact(name)
        fuzzy = dh._normalize_name_for_fuzzy(name)
        call('_normalize_string_exact', dh._normalize_string_exact, [name], [name])
        call('_normalize_name_for_fuzzy', dh._normalize_name_for_fuzzy, [name], [name])
        for text in dict.fromkeys([exact, fuzzy, name]):
            call('_name_entropy', dh._name_entropy, [text], [text])
            call('_has_high_entropy', dh._has_high_entropy, [text], [text])
        call('_shingles', dh._shingles, [fuzzy], [fuzzy])
        call('_cached_shingles', dh._cached_shingles, [fuzzy], [fuzzy])
        shingles = dh._shingles(fuzzy)
        call('_minhash_signature', dh._minhash_signature, [shingles], [sorted(shingles)], hashes)
    for _ in range(80):
        shingle = rand_name(1)[:3]
        seed = rng.choice([0, 1, 31, 32, -1, -(2 ** 63), 2 ** 63 - 1, rng.randint(-10 ** 9, 10 ** 9)])
        call('_hash_shingle', dh._hash_shingle, [shingle, seed], [shingle, seed], str)
    for _ in range(40):
        signature = [rng.choice([0, 1, 2 ** 63, 2 ** 64 - 1, rng.getrandbits(64)]) for _ in range(rng.randint(0, 40))]
        call('_lsh_bands', dh._lsh_bands, [signature], [hashes(signature)], lambda bands: [hashes(b) for b in bands])
    for _ in range(60):
        pool = ['abc', 'bcd', 'cde', 'xyz', '東京大', '\U0001F600ab', 'a', '']
        a = set(rng.sample(pool, rng.randint(0, 5)))
        b = set(rng.sample(pool, rng.randint(0, 5)))
        call('_jaccard_similarity', dh._jaccard_similarity, [a, b], [sorted(a), sorted(b)])

    for _ in range(60):
        value = rand_attr_value()
        call('_normalize_identity_value', dh._normalize_identity_value, [value], [value])

    def rand_identity_properties():
        r = rng.random()
        if r < 0.1:
            return None
        if r < 0.2:
            return {}
        props = {}
        for label in rng.sample(LABELS + ['Entity'], rng.randint(1, 3)):
            props[label] = rng.sample(ATTR_KEYS, rng.randint(0, 2))
        return props

    for i in range(70):
        data = node_data(f'n{i}', rand_name(2))
        props = rand_identity_properties()
        call('_identity_type_for_node', lambda d, p: dh._identity_type_for_node(EntityNode(**d), p), [data, props], [data, props])
        call('_identity_key_for_node', lambda d, p: dh._identity_key_for_node(EntityNode(**d), p), [data, props], [data, props])

    for i in range(50):
        extracted = node_data(f'x{i}', 'A')
        resolved = node_data(f'r{i}', 'A')
        call('_promote_resolved_node', lambda e, r: dh._promote_resolved_node(EntityNode(**e), EntityNode(**r)),
             [extracted, resolved], [extracted, resolved], node_view)

    def indexes_view(indexes):
        return {
            'existing_nodes': [n.uuid for n in indexes.existing_nodes],
            'nodes_by_uuid': {k: v.uuid for k, v in indexes.nodes_by_uuid.items()},
            'normalized_existing': {k: [n.uuid for n in v] for k, v in indexes.normalized_existing.items()},
            'shingles_by_candidate': {k: sorted(v) for k, v in indexes.shingles_by_candidate.items()},
            'lsh_buckets': [[key[0], hashes(key[1]), value] for key, value in indexes.lsh_buckets.items()],
        }

    for i in range(25):
        existing = [node_data(f'e{i}-{j}', rand_name(3)) for j in range(rng.randint(0, 5))]
        call('_build_candidate_indexes', lambda e: dh._build_candidate_indexes([EntityNode(**d) for d in e]),
             [existing], [existing], indexes_view)

    def resolve(existing, extracted, pre, props):
        existing_nodes = [EntityNode(**d) for d in existing]
        extracted_nodes = [EntityNode(**d) for d in extracted]
        indexes = dh._build_candidate_indexes(existing_nodes)
        state = dh.DedupResolutionState(
            resolved_nodes=[existing_nodes[p] if p is not None else None for p in pre],
            uuid_map={},
            unresolved_indices=[],
        )
        dh._resolve_with_identity_properties(extracted_nodes, indexes, state, props)
        after_identity = {
            'resolved_nodes': [None if n is None else n.uuid for n in state.resolved_nodes],
            'uuid_map': dict(state.uuid_map),
            'duplicate_pairs': [[a.uuid, b.uuid] for a, b in state.duplicate_pairs],
        }
        dh._resolve_with_similarity(extracted_nodes, indexes, state)
        return {
            'after_identity': after_identity,
            'resolved_nodes': [None if n is None else node_view(n) for n in state.resolved_nodes],
            'uuid_map': state.uuid_map,
            'unresolved_indices': state.unresolved_indices,
            'duplicate_pairs': [[a.uuid, b.uuid] for a, b in state.duplicate_pairs],
            'existing_after': [node_view(n) for n in existing_nodes],
        }

    base_names = ['Alice Johnson', 'Acme Corporation', 'International Business Machines', 'Bob', 'Zoë Durand',
                  '東京大学', 'Alice Johnson', 'Meaningful Name Here', 'version 2.0', 'Jean-Luc Picard']
    for i in range(70):
        existing = [node_data(f'e{i}-{j}', rng.choice(base_names) if rng.random() < 0.8 else rand_name(3))
                    for j in range(rng.randint(0, 6))]
        extracted = []
        for j in range(rng.randint(1, 5)):
            if existing and rng.random() < 0.7:
                source = rng.choice(existing)
                name = perturb(source['name'])
                uuid = source['uuid'] if rng.random() < 0.1 else f'x{i}-{j}'
            else:
                name = rand_name(3)
                uuid = f'x{i}-{j}'
            data = node_data(uuid, name)
            if existing and rng.random() < 0.3:
                data['attributes'] = dict(rng.choice(existing)['attributes'])
                data['labels'] = list(rng.choice(existing)['labels'])
            extracted.append(data)
        pre = [rng.randrange(len(existing)) if existing and rng.random() < 0.15 else None for _ in extracted]
        props = rand_identity_properties()
        call('resolve', resolve, [existing, extracted, pre, props], [existing, extracted, pre, props], ordered=True)

    def call_resolve(*a):
        call('resolve', resolve, list(a), list(a), ordered=True)

    sink[0] = review_records
    record_fixed_dedup(dh, call_resolve)
    sink[0] = records


def mk(uuid, name, created='2024-01-01T00:00:00+00:00', labels=('Entity', 'Person'), attributes=None, group_id='g'):
    """A node with no random part, for the fixed cases."""
    return {'uuid': uuid, 'name': name, 'group_id': group_id, 'labels': list(labels), 'created_at': created,
            'summary': '', 'attributes': dict(attributes or {})}


def record_fixed_dedup(dh, call_resolve) -> None:
    """Fixed inputs for the identity match, the fuzzy threshold, and the strip of attribute values."""
    email = {'Person': ['email']}
    pid = {'Person': ['pid']}

    def with_email(uuid, created, value='x@y', name=None):
        return mk(uuid, name or f'Person {uuid}', created, attributes={'email': value})

    # The identity matches are not in the order of (created_at, uuid), so the sort decides.
    for existing in ([('a', '2024-01-01T00:00:00+00:00'), ('b', '2023-01-01T00:00:00+00:00')],
                     [('b', '2023-01-01T00:00:00+00:00'), ('a', '2024-01-01T00:00:00+00:00')],
                     [('c', '2025-02-03T00:00:00+00:00'), ('a', '2024-01-01T00:00:00+00:00'), ('b', '2023-01-01T00:00:00+00:00')],
                     [('a', '2023-01-01T00:00:00+00:00'), ('c', '2025-02-03T00:00:00+00:00'), ('b', '2024-01-01T00:00:00+00:00')],
                     [('z', '2023-06-01T12:30:00+00:00'), ('y', '2023-06-01T12:30:00.000001+00:00'), ('x', '2023-06-01T12:29:59+00:00')]):
        nodes = [with_email(u, c) for u, c in existing]
        call_resolve(nodes, [with_email('x', '2024-06-01T00:00:00+00:00')], [None], email)

    # Two matches have one created_at, so the uuid decides. Some pairs have the same instant at two offsets.
    for first, second in ((('u-b', '2024-01-01T00:00:00+00:00'), ('u-a', '2024-01-01T00:00:00+00:00')),
                          (('u-a', '2024-01-01T00:00:00+00:00'), ('u-b', '2024-01-01T00:00:00+00:00')),
                          (('u-b', '2024-01-01T05:00:00+05:00'), ('u-a', '2024-01-01T00:00:00+00:00')),
                          (('u-a', '2024-01-01T00:00:00+00:00'), ('u-b', '2024-01-01T05:00:00+05:00')),
                          (('u-b', '2024-01-01T00:00:00+00:00'), ('u-a', '2023-12-31T19:00:00-05:00'))):
        nodes = [with_email(*first), with_email(*second)]
        call_resolve(nodes, [with_email('x', '2024-06-01T00:00:00+00:00')], [None], email)
    for value, other in ((True, 1), (1, 1.0), (1.0, True), (0, False), (7, 7.0)):
        nodes = [mk('u-b', 'B', '2024-01-01T05:00:00+05:00', attributes={'pid': other}),
                 mk('u-a', 'A', '2024-01-01T00:00:00+00:00', attributes={'pid': other})]
        call_resolve(nodes, [mk('u-x', 'X', attributes={'pid': value})], [None], pid)

    # An extracted node has the uuid of an existing node with the same identity key.
    call_resolve([with_email('s', '2020-01-01T00:00:00+00:00', 'z'), with_email('t', '2024-01-01T00:00:00+00:00', 'z')],
                 [with_email('s', '2025-01-01T00:00:00+00:00', 'z')], [None], email)
    call_resolve([with_email('t', '2024-01-01T00:00:00+00:00', 'z'), with_email('s', '2020-01-01T00:00:00+00:00', 'z')],
                 [with_email('t', '2025-01-01T00:00:00+00:00', 'z')], [None], email)
    call_resolve([with_email('r', '2019-01-01T00:00:00+00:00', 'z'), with_email('s', '2020-01-01T00:00:00+00:00', 'z'),
                  with_email('t', '2024-01-01T00:00:00+00:00', 'z')],
                 [with_email('s', '2025-01-01T00:00:00+00:00', 'z')], [None], email)
    call_resolve([with_email('s', '2020-01-01T00:00:00+00:00', 'z', name='Same Name'), with_email('t', '2024-01-01T00:00:00+00:00', 'q')],
                 [with_email('s', '2025-01-01T00:00:00+00:00', 'z', name='Same Name')], [None], email)
    call_resolve([with_email('s', '2020-01-01T00:00:00+00:00', 'z'), with_email('t', '2024-01-01T00:00:00+00:00', 'z')],
                 [with_email('s', '2025-01-01T00:00:00+00:00', 'z'), with_email('t', '2025-01-01T00:00:00+00:00', 'z')],
                 [None, None], email)

    # The fuzzy score of 0.9 resolves, and the score of 8/9 does not.
    letters = 'abcdefghijklmnopqrstuvwxyz0123456789'
    for long_length, short_length in ((12, 11), (11, 10), (10, 9), (13, 12), (14, 13)):
        longer, shorter = letters[:long_length], letters[:short_length]
        call_resolve([mk('e', longer, labels=('Entity',))], [mk('n', shorter, labels=('Entity',))], [None], None)
        call_resolve([mk('e', shorter, labels=('Entity',))], [mk('n', longer, labels=('Entity',))], [None], None)

    # A prior match with empty text after a strip does not replace an attribute (_merge_non_empty_attributes).
    for text in ('\x1c', ' \x1f ', '\x85', '\x1c\x1d\x1e\x1f', ' \u00a0 ', '\u200b', '\x1c7\x1f'):
        call_resolve([mk('e', 'Alice Johnson', attributes={'a': 'old', 'b': 'old'})],
                     [mk('n', 'Alice Johnson', attributes={'a': text, 'b': 'new'})], [None], None)
        call_resolve([mk('e', 'Alice Johnson', attributes={'email': 'old'})],
                     [mk('n', 'Alice Johnson', attributes={'email': text})], [None], email)
        call_resolve([mk('e', 'Alice Johnson', attributes={'email': text})],
                     [mk('n', 'Bob Other', attributes={'email': text})], [None], email)

    for text in ('\x1c', '\x1f', ' \x1f ', '\x85', '\x1c\x1f', '\x1c7\x1f', '\u00a0', '\u200b', '\ufeff', ' x '):
        call('_normalize_identity_value', dh._normalize_identity_value, [text], [text])


def record_attributes(au) -> None:
    from pydantic import BaseModel, Field

    class Person(BaseModel):
        phones: str | None = Field(default=None, description='Phone numbers')
        industry: str | None = Field(default=None, description='Industry sector')
        description: str | None = Field(default=None, max_length=2000, description='Long-form description')
        aliases: list[str] | None = Field(default=None, description='Alternate names')

    class Strict(BaseModel):
        full_name: str = Field(..., description='Full legal name')
        short: str = Field(..., max_length=3)
        tags: list[str] = Field(default_factory=list, max_length=2)
        zero: str | None = Field(default=None, max_length=0)
        note: str = ''

    class Mixed(BaseModel):
        amount: float | None = None
        notes: str | None = None
        count: int = 0

    models = {'Person': Person, 'Strict': Strict, 'Mixed': Mixed}
    descriptors = {
        name: [[field, au._field_is_required(model, field), au._field_max_length(model, field)] for field in model.model_fields]
        for name, model in models.items()
    }

    def rand_value():
        r = rng.random()
        if r < 0.3:
            return rng.choice(['x', 'abc', 'a' * 249, 'a' * 250, 'a' * 251, '\U0001F600' * 251, 'é' * 130,
                               '東' * 300, 'b' * 2001, 'c' * 49, 'c' * 51, ''])
        if r < 0.55:
            items = [rng.choice(['x' * rng.randint(0, 260), 5, None, '\U0001F600' * 3, ['n']]) for _ in range(rng.randint(0, 12))]
            return items
        if r < 0.65:
            return {'$tuple': ['x' * 300]}
        return rng.choice([None, 0, 1.5, True, {'a': 'x' * 300}, 10 ** 6])

    def decode(value):
        if isinstance(value, dict) and set(value) == {'$tuple'}:
            return tuple(value['$tuple'])
        return value

    for _ in range(80):
        value = rand_value()
        cap = rng.choice([0, -1, 1, 3, 5, 40, 250, 2000])
        call('_check_value_against_cap', au._check_value_against_cap, [decode(value), cap], [value, cap])

    envs = [None, '', '   ', '50', ' 7 ', '1_000', '١٢', '+12', 'p7a-bad', 'p7a-bad', 'p7a-0x10', '-7', '-00',
            '+0', 'p7a-1.5', 'p7a-bad', '2000000000000000000', '9223372036854775807', '1152921504606846975',
            '1152921504606846976', '\x1c', ' \x1f ', '\x85', '\x1c7\x1f']

    def run_with_env(env, fn):
        if env is None:
            os.environ.pop(au._ENV_VAR, None)
        else:
            os.environ[au._ENV_VAR] = env
        try:
            return fn()
        finally:
            os.environ.pop(au._ENV_VAR, None)

    for env in envs:
        for default in (250, 40, 0, -5):
            call('_resolve_default_max_length', lambda e, d: run_with_env(e, lambda: au._resolve_default_max_length(d)),
                 [env, default], [env, default])

    def rand_response(model_name):
        keys = list(models[model_name].model_fields) + ['unknown', 'name']
        return {k: rand_value() for k in rng.sample(keys, rng.randint(0, len(keys)))}

    def rand_kwargs():
        kwargs = {}
        if rng.random() < 0.4:
            kwargs['default_max_length'] = rng.choice([0, 3, 50, 250, -1])
        if rng.random() < 0.5:
            kwargs['prompt_name'] = 'extract_nodes.extract_attributes'
        if rng.random() < 0.5:
            kwargs['entity_uuid'] = rng.choice(['', 'abc-123'])
        if rng.random() < 0.5:
            kwargs['group_id'] = rng.choice(['', 'tenant-x'])
        return kwargs

    def view(result):
        kept, dropped = result
        return [kept, sorted(dropped), list(kept)]

    for _ in range(90):
        model_name = rng.choice(list(models))
        response = rand_response(model_name)
        kwargs = rand_kwargs()
        env = rng.choice([None, None, None, '50', 'p7a-bad', '-7'])
        call('cap_string_attributes',
             lambda e, m, r, k: run_with_env(e, lambda: au.cap_string_attributes({x: decode(y) for x, y in r.items()}, models[m], **k)),
             [env, model_name, response, kwargs], [env, model_name, response, kwargs], view)

    def dropped_order_view(result):
        merged, dropped = result
        return [merged, sorted(dropped), list(merged)]

    for _ in range(90):
        model_name = rng.choice(list(models))
        response = rand_response(model_name)
        prior = {k: rng.choice([None, 'old', 'x' * 300, 3]) for k in rng.sample(list(models[model_name].model_fields) + ['z', 'unknown'], rng.randint(0, 4))}
        kwargs = rand_kwargs()
        kwargs['merge_mode'] = rng.choice(['overlay', 'overlay', 'replace', 'replace', 'merge'])
        call('apply_capped_attributes',
             lambda m, r, p, k: au.apply_capped_attributes({x: decode(y) for x, y in r.items()}, models[m], p, **k),
             [model_name, response, prior, kwargs], [model_name, response, prior, kwargs], dropped_order_view, ordered=True)

    sink[0] = review_records
    # Fixed inputs: a cap above long.MaxValue / 8, an aggregate breach, and a replace that restores two fields or more.
    # They use no random value, so that they do not move the cases above.
    big_caps = [2000000000000000000, 9223372036854775807, 1152921504606846975, 1152921504606846976, 1152921504606846977]
    for cap in big_caps:
        for value in (['a', 'b'], ['x' * 300, 'y'], 'abc', [], ['a', 5, None]):
            call('_check_value_against_cap', au._check_value_against_cap, [value, cap], [value, cap])
    for value, cap in ((['x' * 40] * 9, 40), (['x' * 40] * 8, 40), (['x' * 40] * 8 + ['y'], 40), (['a'] * 9, 1), (['a'] * 8, 1),
                       (['\U0001F600' * 5] * 9, 5), (['x' * 5, 7, None] * 20, 5), ([['a'] * 100] * 3 + ['aa'] * 9, 2),
                       (['x' * 249] * 10, 250), (['x' * 250] * 8, 250), (['x' * 250] * 9, 250), (['x'] * 9, 0), (['x'] * 9, -1)):
        call('_check_value_against_cap', au._check_value_against_cap, [value, cap], [value, cap])

    for env in ('2000000000000000000', '9223372036854775807', '1152921504606846976', None, '50'):
        for model_name in ('Person', 'Strict'):
            for response in ({'aliases': ['x' * 300, 'y'], 'tags': ['a', 'b'], 'phones': 'z' * 400},
                             {'aliases': ['ok', 'fine'], 'tags': ['a'] * 3, 'phones': 'ok', 'short': 'abc'},
                             {'aliases': ['x' * 49] * 9, 'tags': ['ab'] * 9, 'phones': 'z' * 51, 'full_name': 'n' * 300},
                             {'aliases': ['x' * 249] * 10, 'tags': [], 'industry': 'i' * 300}):
                kwargs = {'prompt_name': 'extract_nodes.extract_attributes', 'entity_uuid': 'abc-123', 'group_id': 'tenant-x'}
                call('cap_string_attributes',
                     lambda e, m, r, k: run_with_env(e, lambda: au.cap_string_attributes(dict(r), models[m], **k)),
                     [env, model_name, response, kwargs], [env, model_name, response, kwargs], view)
    for default in (2000000000000000000, 9223372036854775807):
        response = {'aliases': ['x' * 300, 'y'], 'phones': 'z' * 400}
        kwargs = {'default_max_length': default}
        call('cap_string_attributes', lambda e, m, r, k: run_with_env(e, lambda: au.cap_string_attributes(dict(r), models[m], **k)),
             [None, 'Person', response, kwargs], [None, 'Person', response, kwargs], view)

    long_text = 'x' * 300
    replace_cases = [
        ('Person', {'phones': long_text, 'industry': 'y' * 300, 'aliases': ['ok']}, {'phones': 'a', 'industry': 'b'}),
        ('Person', {'aliases': ['ok'], 'industry': 'y' * 300, 'phones': long_text}, {'phones': 'a', 'industry': 'b'}),
        ('Person', {'industry': 'y' * 300, 'phones': long_text, 'aliases': ['x' * 249] * 10}, {'phones': 'a', 'industry': 'b', 'aliases': ['p']}),
        ('Person', {'phones': long_text, 'industry': 'y' * 300, 'aliases': ['x' * 249] * 10}, {'aliases': ['p'], 'industry': 'b'}),
        ('Person', {'phones': long_text, 'industry': 'y' * 300, 'aliases': ['x' * 249] * 10, 'description': 'd' * 2500},
         {'description': 'd0', 'phones': 'a', 'industry': 'b', 'aliases': ['p'], 'other': 1}),
        ('Person', {'phones': None, 'industry': long_text, 'aliases': ['x' * 249] * 10}, {'phones': 'a', 'aliases': None, 'industry': 'b'}),
        ('Strict', {'tags': ['ab'] * 9, 'zero': 'z', 'note': 'n', 'full_name': 'f', 'short': 'abc'}, {'zero': 'old', 'tags': ['t'], 'note': 'o'}),
        ('Strict', {'zero': 'z', 'tags': ['ab'] * 9, 'short': 'abc', 'full_name': 'f', 'note': 'n'}, {'tags': ['t'], 'zero': 'old', 'note': 'o'}),
        ('Mixed', {'notes': long_text, 'amount': 2.5, 'count': 3}, {'notes': 'n', 'amount': 1.0}),
    ]
    for model_name, response, prior in replace_cases:
        for mode in ('replace', 'overlay'):
            kwargs = {'merge_mode': mode, 'entity_uuid': 'abc-123', 'group_id': 'tenant-x'}
            call('apply_capped_attributes',
                 lambda m, r, p, k: au.apply_capped_attributes(dict(r), models[m], p, **k),
                 [model_name, response, prior, kwargs], [model_name, response, prior, kwargs], dropped_order_view, ordered=True)

    sink[0] = records
    records.append({'f': 'descriptors', 'a': [], 'r': {'ok': descriptors}, 'logs': []})


def record_temporal(te, EntityEdge) -> None:
    subjects = ['Bob', 'BOB', 'Zoë', '東京', 'We', 'She']
    futures = ['will', 'shall', 'is going to', 'are going to', 'going to', 'plans to', 'plan to', 'planned to',
               'planning to', 'intends to', 'intend to', 'intended to', 'expects to', 'expect to', 'expected to',
               'promises to', 'promise to', 'promised to', 'scheduled to', 'is scheduled to', 'are scheduled to',
               'set to', 'aims to', 'aim to', 'aimed to', 'committed to', 'WILL', 'Will', 'wİll', 'wıll', 'ſhall',
               'ıs going to', 'promıſes to', 'willing to', 'goingto', 'launched', 'has', 'is', 'shallow']
    contractions = ["she'll", "We'll", "ſhe'll", "İt'll", "ıt'll", "x'll", "'ll", "you'llx", "É'll", "Kelvin'll"]
    tails = ['move', 'finish the work', 'leave', 'stay']
    deadlines = ['by Friday', 'before noon', 'no later than May', 'deadline is June', 'due by June', 'due on May 1',
                 'BY JUNE', 'Before', 'deadlİne set', 'nearby', 'bypass', '']
    ends = ['until 2025', 'through June', 'thru June', 'expires', 'expire', 'expired', 'ends', 'end', 'ended',
            'ending', 'stops', 'stop', 'stopped', 'ceases', 'cease', 'ceased', 'no longer', 'UNTİL', 'expıres',
            'ſtops', 'endless', 'untill', '']
    reference = [None, datetime(2020, 1, 1, tzinfo=timezone.utc), datetime(2020, 1, 1),
                 datetime(2020, 1, 1, 5, 30, tzinfo=timezone(timedelta(hours=5, minutes=30)))]
    dates = [None, datetime(2019, 1, 1, tzinfo=timezone.utc), datetime(2020, 1, 1, tzinfo=timezone.utc),
             datetime(2021, 6, 1, tzinfo=timezone.utc), datetime(2021, 6, 1), datetime(2019, 12, 31, 23, 0),
             datetime(2020, 1, 1, 4, 0, tzinfo=timezone(timedelta(hours=-3)))]

    def fact():
        r = rng.random()
        if r < 0.15:
            words = [rng.choice(contractions), rng.choice(tails)]
        else:
            words = [rng.choice(subjects), rng.choice(futures), rng.choice(tails)]
        if rng.random() < 0.6:
            words.append(rng.choice(deadlines))
        if rng.random() < 0.5:
            words.append(rng.choice(ends))
        return ' '.join(w for w in words if w)

    for _ in range(220):
        f = fact()
        ref, valid, invalid = rng.choice(reference), rng.choice(dates), rng.choice(dates)
        call('normalize_future_temporal_bounds', te.normalize_future_temporal_bounds, [f, ref, valid, invalid],
             [f, dt(ref), dt(valid), dt(invalid)], lambda pair: [dt(pair[0]), dt(pair[1])])

    texts = [None, '', '2024-05-06T07:08:09Z', '2024-05-06T07:08:09.123456Z', '2024-05-06T07:08:09+02:00',
             '2024-05-06', '2024-05-06T07:08:09', 'yesterday', '2024-13-01T00:00:00Z', 'Z', '2024-05-06T07:08:09ZZ',
             '2021-06-01T00:00:00Z', '2019-01-01T00:00:00+00:00', '\U0001F600']

    def apply(f, valid_text, invalid_text, ref, valid0, invalid0):
        edge = EntityEdge(group_id='g', source_node_uuid='s', target_node_uuid='t', created_at=FIXED_NOW, name='R',
                          fact=f, valid_at=valid0, invalid_at=invalid0)
        result = te.apply_extracted_timestamps(edge, valid_text, invalid_text, ref)
        return [result, dt(edge.valid_at), dt(edge.invalid_at)]

    for _ in range(120):
        f = fact()
        args = [f, rng.choice(texts), rng.choice(texts), rng.choice(reference), rng.choice(dates), rng.choice(dates)]
        call('apply_extracted_timestamps', apply, args, [args[0], args[1], args[2], dt(args[3]), dt(args[4]), dt(args[5])], lambda x: x)

    sink[0] = review_records
    # Fixed inputs that use no random value. A letter that R13 adds to a pattern, a mark next to a keyword, a date text
    # whose only form is `Z`, and a word that contains a keyword.
    utc = timezone.utc
    ref = datetime(2020, 1, 1, tzinfo=utc)
    later = datetime(2021, 6, 1, tzinfo=utc)

    def fixed_bounds(f, valid=None, invalid=None):
        call('normalize_future_temporal_bounds', te.normalize_future_temporal_bounds, [f, ref, valid, invalid],
             [f, dt(ref), dt(valid), dt(invalid)], lambda pair: [dt(pair[0]), dt(pair[1])])

    # The contraction has no boundary before it, so each of these needs the letters that R13 adds to [a-z].
    for letter in ('ſ', 'İ', 'ı', 'ſı', 'K', 'ıſ', 'a', 'Z', '1', 'é', ''):
        fixed_bounds(f"{letter}'ll go", later)
        fixed_bounds(f"Bob said {letter}'ll go", later)
        fixed_bounds(f"{letter}'ll go by Friday", None, later)

    marks = ['́', '‿', 'ͅ', '̀́']
    # A mark is not a word character in Python, so a keyword with a mark next to it still has its boundaries.
    for keyword in ('will', 'shall', 'is going to', 'plans to', 'set to', 'committed to', "she'll"):
        for mark in marks:
            for text in (f'Bob {keyword}{mark} stay', f'Bob {mark}{keyword} stay', f'Bob {keyword}{mark}{mark} stay',
                         f'Bob{mark} {keyword} stay', f'Bob {keyword} {mark}stay'):
                fixed_bounds(text, later)
    for keyword in ('by', 'before', 'no later than', 'deadline', 'due by', 'due on'):
        for mark in marks:
            for text in (f'Bob will stay {keyword}{mark} Friday', f'Bob will stay {mark}{keyword} Friday',
                         f'Bob will stay {keyword} {mark}Friday'):
                fixed_bounds(text, None, later)
    for keyword in ('until', 'through', 'thru', 'expires', 'expired', 'ended', 'ending', 'stopped', 'ceases', 'no longer'):
        for mark in marks:
            for text in (f'Bob will stay by Friday {keyword}{mark} 2025', f'Bob will stay by Friday {mark}{keyword} 2025',
                         f'Bob will stay by Friday {keyword} {mark}2025'):
                fixed_bounds(text, None, later)

    # A word that contains a keyword does not end a state or give a deadline, and a bare keyword does.
    for text in ('Bob will stay endless by Friday', 'Bob will stay by Friday endless', 'Bob will stay by Friday untill 2025',
                 'Bob will stay by Friday bypass', 'Bob will stay nearby', 'Bob will stay bypass', 'Bob will stay standby',
                 'Bob will stay thruway by Friday', 'Bob will stay by Friday through', 'Bob will stay by Friday ends',
                 'Bob will stay by Friday ending', 'Bob will stay by Friday expiresx', 'Bob will stay by Friday xexpires',
                 'Bob will stay by Friday no longer', 'Bob will stay by Friday nolonger', 'Bob will stay by Friday no  longer',
                 'Bob will stay by Friday stops', 'Bob will stay by Friday _stops', 'Bob will stay by Friday 9stops',
                 'Bob will stay by Friday éstops', 'Bob will stay by Friday ſtops', 'Bob will stay by Friday UNTİL',
                 'Bob will stay bye', 'Bob will stay by_Friday', 'Bob will stay by9', 'Bob will stay Standby by Friday'):
        fixed_bounds(text, None, later)

    # `Z` alone is a date text only after the replacement, and a text with `Z` inside is not.
    for text in ('2024-05-06Z', '2024-05-06z', '2024-05-06T07:08Z', '2024-05-06T07Z', 'Z2024-05-06', '2024Z', '2024-05-06 Z',
                 '2024-05-06TZ', '2024-W19-1Z', '2024-126Z', '20240506Z', '2024-05-06T07:08:09.5Z', '2024-05-06T07:08:09+00:00Z'):
        for fact, valid0, invalid0 in (('Bob will go', None, None), ('Bob will go by Friday', None, None),
                                       ('Bob went', datetime(2019, 1, 1), later)):
            args = [fact, text, text, ref, valid0, invalid0]
            call('apply_extracted_timestamps', apply, args, [args[0], args[1], args[2], dt(args[3]), dt(args[4]), dt(args[5])], lambda x: x)
        args = ['Bob will go by Friday', None, text, ref, None, None]
        call('apply_extracted_timestamps', apply, args, [args[0], args[1], args[2], dt(args[3]), dt(args[4]), dt(args[5])], lambda x: x)
    sink[0] = records


def record_dangling(de, EntityNode) -> None:
    de.utc_now = lambda: FIXED_NOW

    def materialize(name, group_id, mapping, nodes):
        name_to_node = {k: EntityNode(**v) for k, v in mapping.items()}
        node_list = [EntityNode(**v) for v in nodes]
        before = {k: id(v) for k, v in name_to_node.items()}
        result = de.materialize_dangling_endpoint_node(name, group_id=group_id, name_to_node=name_to_node, nodes=node_list)
        existing = next((k for k, v in before.items() if v == id(result)), None)

        def view(node):
            data = node_view(node)
            # A new node has a random uuid4, so the recording keeps its shape (R25).
            if node is result and existing is None:
                assert UUID4.match(data['uuid'])
                data['uuid'] = '<uuid4>'
            return data

        return {
            'existing_key': existing,
            'result': view(result),
            'nodes': [view(n) for n in node_list],
            'appended': len(node_list) > 0 and node_list[-1] is result,
            'name_to_node': {k: view(v)['uuid'] for k, v in name_to_node.items()},
            'new_key_is_result': [k for k, v in name_to_node.items() if v is result],
        }

    for i in range(50):
        name = rand_name(3)
        mapping = {}
        for j in range(rng.randint(0, 3)):
            key = rng.choice([rng.choice(['version 2.0', 'alice', '']), rand_name(2)])
            mapping[key] = node_data(f'm{i}-{j}', key or 'empty')
        if mapping and rng.random() < 0.4:
            name = perturb(rng.choice(list(mapping)))
        nodes = [node_data(f'n{i}-{j}', rand_name(2)) for j in range(rng.randint(0, 3))]
        group_id = rng.choice(['g', '', 'grüp 東京'])
        call('materialize_dangling_endpoint_node', materialize, [name, group_id, mapping, nodes], [name, group_id, mapping, nodes])

    def prune(nodes, initial, referenced):
        node_list = [EntityNode(**v) for v in nodes]
        same = node_list
        result = de.prune_unreferenced_materialized_nodes(node_list, initial_uuids=set(initial), referenced_uuids=set(referenced))
        return {'result': [n.uuid for n in result], 'nodes': [n.uuid for n in same]}

    for i in range(40):
        uuids = [f'u{j}' for j in range(rng.randint(0, 6))]
        if uuids and rng.random() < 0.2:
            uuids.append(uuids[0])
        nodes = [node_data(u, 'N') for u in uuids]
        initial = rng.sample(uuids, rng.randint(0, len(uuids)))
        referenced = rng.sample(uuids + ['zz'], rng.randint(0, len(uuids) + 1))
        call('prune_unreferenced_materialized_nodes', prune, [nodes, initial, referenced], [nodes, sorted(set(initial)), sorted(set(referenced))])


def encode(value):
    """A parameter value with its Python type."""
    if value is None:
        return {'type': 'none'}
    if isinstance(value, bool):
        return {'type': 'bool', 'value': value}
    if isinstance(value, int):
        return {'type': 'int', 'value': value}
    if isinstance(value, str):
        return {'type': 'str', 'value': value}
    if isinstance(value, datetime):
        return {'type': 'datetime', 'value': value.isoformat()}
    if isinstance(value, list):
        return {'type': 'list', 'items': [encode(v) for v in value]}
    return {'type': type(value).__name__}


def record_graph_data(gdo, GraphProvider, EpisodeType, GraphOperationsInterface) -> list[dict]:
    from graphiti_core.nodes import EpisodicNode

    out = []

    def episode_record(i, source='message', created='2024-05-06T07:08:09+00:00', valid='2024-05-06T07:08:09Z'):
        return {
            'uuid': f'ep-{i}',
            'name': f'episode {i}',
            'group_id': 'g',
            'created_at': created,
            'source': source,
            'source_description': 'chat',
            'content': f'content {i} 東京',
            'valid_at': valid,
            'entity_edges': ['e1', 'e2'][: i % 3],
        }

    class FakeTx:
        def __init__(self, case):
            self.case = case

        async def run(self, query, **kwargs):
            await asyncio.sleep(0)
            self.case['log'].append({'kind': 'tx_run', 'query': query, 'keys': list(kwargs), 'values': [encode(v) for v in kwargs.values()]})
            fail(self.case)

    class FakeSession:
        def __init__(self, case):
            self.case = case

        async def __aenter__(self):
            return self

        async def __aexit__(self, exc_type, exc, tb):
            await asyncio.sleep(0)
            self.case['log'].append({'kind': 'session_close'})

        async def execute_write(self, func, *args, **kwargs):
            await asyncio.sleep(0)
            self.case['log'].append({'kind': 'execute_write'})
            fail(self.case)
            return await func(FakeTx(self.case), *args, **kwargs)

    def fail(case):
        case['count'] += 1
        if case['input'].get('fail_at') == case['count'] - 1:
            raise RuntimeError('boom')

    class FakeOps(GraphOperationsInterface):
        async def clear_data(self, driver, group_ids=None):
            await asyncio.sleep(0)
            CURRENT['log'].append({'kind': 'ops_clear_data', 'values': [encode(group_ids)]})
            fail(CURRENT)
            if CURRENT['input']['ops'] == 'not_implemented':
                raise NotImplementedError
            return None

        async def retrieve_episodes(self, driver, reference_time, last_n=3, group_ids=None, source=None, saga=None):
            await asyncio.sleep(0)
            CURRENT['log'].append({'kind': 'ops_retrieve_episodes', 'values': [
                encode(reference_time), encode(last_n), encode(group_ids), encode(None if source is None else source.name), encode(saga)]})
            fail(CURRENT)
            if CURRENT['input']['ops'] == 'not_implemented':
                raise NotImplementedError
            return [EpisodicNode(**{**episode_record(9), 'source': EpisodeType.text, 'created_at': FIXED_NOW, 'valid_at': FIXED_NOW})]

    class FakeDriver:
        def __init__(self, case):
            self.case = case
            self.provider = GraphProvider(case['input']['provider'])
            self.graph_operations_interface = None if case['input']['ops'] == 'none' else FakeOps()

        def session(self, database=None):
            self.case['log'].append({'kind': 'session_open'})
            return FakeSession(self.case)

        async def execute_query(self, cypher_query_, **kwargs):
            await asyncio.sleep(0)
            self.case['log'].append({'kind': 'execute_query', 'query': cypher_query_, 'keys': list(kwargs), 'values': [encode(v) for v in kwargs.values()]})
            fail(self.case)
            return list(self.case['input']['records']), None, None

    CURRENT = {}

    class Capture(logging.Handler):
        def emit(self, record):
            if CURRENT:
                CURRENT['log'].append({'kind': 'log', 'level': record.levelname, 'message': record.getMessage()})

    handler = Capture(logging.DEBUG)
    logger = logging.getLogger('graphiti_core')
    logger.addHandler(handler)

    inputs = []
    providers = ['neo4j', 'falkordb', 'neptune', 'relate']
    for ops in ['none', 'not_implemented', 'returns']:
        for group_ids in [None, [], ['g'], ['g', 'grüp 東京'], ['']]:
            inputs.append({'method': 'clear_data', 'provider': 'neo4j', 'ops': ops, 'group_ids': group_ids})
        inputs.append({'method': 'clear_data', 'provider': 'neo4j', 'ops': ops, 'default': True})
    for fail_at in range(6):
        inputs.append({'method': 'clear_data', 'provider': 'neo4j', 'ops': 'not_implemented', 'group_ids': ['g', 'h'], 'fail_at': fail_at})
    records_pool = [[], [episode_record(1)], [episode_record(3), episode_record(1, 'json'), episode_record(2, 'fact_triple', valid='2023-01-01T00:00:00')]]
    reference_times = [datetime(2024, 6, 1, tzinfo=timezone.utc), datetime(2024, 6, 1, 12, 30, 15, 123456), datetime(2024, 6, 1, 3, tzinfo=timezone(timedelta(hours=5, minutes=30)))]
    for ops in ['none', 'not_implemented', 'returns']:
        for provider in providers:
            for _ in range(5):
                case = {
                    'method': 'retrieve_episodes',
                    'provider': provider,
                    'ops': ops,
                    'reference_time': dt(rng.choice(reference_times)),
                    'records': rng.choice(records_pool),
                }
                if rng.random() < 0.7:
                    case['last_n'] = rng.choice([0, 1, 3, 10, -1])
                if rng.random() < 0.7:
                    case['group_ids'] = rng.choice([None, [], ['g'], ['g', 'h'], ['']])
                if rng.random() < 0.6:
                    case['source'] = rng.choice([None, 'message', 'json', 'text', 'fact_triple'])
                if rng.random() < 0.5:
                    case['saga'] = rng.choice([None, 'Saga 東京', ''])
                inputs.append(case)
    for fail_at in range(3):
        inputs.append({'method': 'retrieve_episodes', 'provider': 'neo4j', 'ops': 'not_implemented', 'reference_time': dt(reference_times[0]),
                       'records': records_pool[1], 'saga': 's', 'fail_at': fail_at})
    bad = [{**episode_record(1), 'source': 'bogus'}]
    inputs.append({'method': 'retrieve_episodes', 'provider': 'neo4j', 'ops': 'none', 'reference_time': dt(reference_times[0]), 'records': bad})
    inputs.append({'method': 'retrieve_episodes', 'provider': 'neo4j', 'ops': 'none', 'reference_time': dt(reference_times[0]), 'records': [{**episode_record(1), 'valid_at': None}]})
    inputs.append({'method': 'retrieve_episodes', 'provider': 'neo4j', 'ops': 'none', 'reference_time': dt(reference_times[0]), 'records': [{'uuid': 'x'}]})

    for item in inputs:
        CURRENT.clear()
        CURRENT.update({'input': item, 'log': [], 'count': 0})
        driver = FakeDriver(CURRENT)
        output = {}
        try:
            if item['method'] == 'clear_data':
                if item.get('default'):
                    result = asyncio.run(gdo.clear_data(driver))
                else:
                    result = asyncio.run(gdo.clear_data(driver, item['group_ids']))
                output['result'] = result
            else:
                kwargs = {}
                for key in ('last_n', 'group_ids', 'saga'):
                    if key in item:
                        kwargs[key] = item[key]
                if 'source' in item:
                    kwargs['source'] = None if item['source'] is None else EpisodeType(item['source'])
                reference_time = datetime.fromisoformat(item['reference_time']['$dt'])
                episodes = asyncio.run(gdo.retrieve_episodes(driver, reference_time, **kwargs))
                output['result'] = [e.model_dump(mode='json') for e in episodes]
            output['error'] = None
        except Exception as error:  # noqa: BLE001 - the recording keeps each exception
            output['error'] = {'type': type(error).__name__, 'text': str(error)}
        output['events'] = list(CURRENT['log'])
        out.append({'input': item, 'output': output})
    CURRENT.clear()
    logger.removeHandler(handler)
    return out


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    os.environ.pop('GRAPHITI_ATTRIBUTE_MAX_LENGTH', None)
    from graphiti_core.driver.driver import GraphProvider
    from graphiti_core.driver.graph_operations.graph_operations import GraphOperationsInterface
    from graphiti_core.edges import EntityEdge
    from graphiti_core.nodes import EntityNode, EpisodeType
    from graphiti_core.utils.maintenance import (
        attribute_utils,
        dangling_endpoints,
        dedup_helpers,
        graph_data_operations,
        temporal_edge_utils,
    )

    class Capture(logging.Handler):
        def emit(self, record):
            events.append({'level': record.levelname, 'message': record.getMessage()})

    root = logging.getLogger('graphiti_core')
    root.setLevel(logging.DEBUG)
    root.propagate = False
    root.addHandler(Capture(logging.DEBUG))

    constants = {
        'dedup_helpers': {name: getattr(dedup_helpers, name) for name in [
            '_NAME_ENTROPY_THRESHOLD', '_MIN_NAME_LENGTH', '_MIN_TOKEN_COUNT', '_FUZZY_JACCARD_THRESHOLD',
            '_MINHASH_PERMUTATIONS', '_MINHASH_BAND_SIZE', 'ENTITY_BASE_LABEL']},
        'attribute_utils': {name: getattr(attribute_utils, name) for name in [
            'DEFAULT_ATTRIBUTE_MAX_LENGTH', 'LIST_TOTAL_LENGTH_MULTIPLIER', '_ENV_VAR']},
        'graph_data_operations': {'EPISODE_WINDOW_LEN': graph_data_operations.EPISODE_WINDOW_LEN},
        'lru_cache_maxsize': dedup_helpers._cached_shingles.cache_info().maxsize,
    }

    dedup_helpers.set = InsertionOrderSet
    attribute_utils.set = InsertionOrderSet

    record_dedup(dedup_helpers, EntityNode)
    record_attributes(attribute_utils)
    record_temporal(temporal_edge_utils, EntityEdge)
    record_dangling(dangling_endpoints, EntityNode)
    async_cases = record_graph_data(graph_data_operations, GraphProvider, EpisodeType, GraphOperationsInterface)

    def dump(items):
        return [json.dumps(r, ensure_ascii=True, allow_nan=True, separators=(',', ':')) for r in items]

    top = '{"python":"' + '.'.join(str(p) for p in sys.version_info[:3]) + '","fixed_now":"' + FIXED_NOW.isoformat() + '",'
    text = (
        top + '"constants":' + json.dumps(constants, ensure_ascii=True, separators=(',', ':')) + ',"cases":[\n'
        + ',\n'.join(dump(records)) + '\n],"async_cases":[\n' + ',\n'.join(dump(async_cases)) + '\n]}\n'
    )
    # The second file holds the fixed cases that came with the reviews. It has no constants and no async case.
    review_text = top + '"cases":[\n' + ',\n'.join(dump(review_records)) + '\n]}\n'
    outputs = [(OUT, text), (OUT_REVIEW, review_text)]
    for path, body in outputs:
        size = len(body.encode('utf-8'))
        if size > 1_572_864:
            raise SystemExit(f'{path.name} would have {size} bytes, above the limit of 1,572,864 (R25).')
    counts = f'{len(records)} and {len(review_records)} cases and {len(async_cases)} async cases'
    if '--check' in sys.argv[1:]:
        stale = [path for path, body in outputs if not path.exists() or path.read_text(encoding='utf-8') != body]
        if stale:
            print(f'{", ".join(p.name for p in stale)} not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.name} and {OUT_REVIEW.name}: current, {counts}')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    for path, body in outputs:
        path.write_text(body, encoding='utf-8')
    sizes = [len(body.encode('utf-8')) for _, body in outputs]
    print(f'{OUT.name} and {OUT_REVIEW.name}: {counts}, {sizes} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
