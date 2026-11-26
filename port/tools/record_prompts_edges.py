"""Records the results of graphiti_core/prompts/extract_edges.py, dedupe_edges.py, and extract_nodes_and_edges.py.

The judge has a few cases for these modules. This file gives the evaluation tests about 1,000
generated inputs, with the value or the exception class that Python gives for each one
(port/RULES.md, R25).

    GRAPHITI_UPSTREAM=<upstream checkout> <python 3.12> port/tools/record_prompts_edges.py
    GRAPHITI_UPSTREAM=<upstream checkout> <python 3.12> port/tools/record_prompts_edges.py --check

It writes tests/Port.Evaluation/values/prompts_edges.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The tool needs Python 3.12 and the pinned upstream (the environment
variable GRAPHITI_UPSTREAM, or .upstream/graphiti). It changes nothing there. Nothing here uses the network. The
random seed is fixed. A result longer than 300 characters is stored as zlib and base64 to keep the file small.

A case is left out when its result depends on a documented limit of the port: an integer that a 64-bit long
cannot hold. The text of a KeyError message is not recorded.
"""
from __future__ import annotations

import base64
import datetime as dt
import json
import os
import random
import re
import sys
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'prompts_edges.json'
sys.path.insert(0, str(UPSTREAM))

import pydantic  # noqa: E402

from graphiti_core.prompts import dedupe_edges as de  # noqa: E402
from graphiti_core.prompts import extract_edges as ee  # noqa: E402
from graphiti_core.prompts import extract_nodes_and_edges as ne  # noqa: E402

BIG_INT = re.compile(r'(?<![\d.eE+-])-?\d{19,}(?![\d.eE])')

R = random.Random(20261127)

SPECIAL = [
    "'", '"', '\\', '\n', '\r', '\t', '\r\n', '\x00', '\x01', '\x1f', '\x7f', '\x80', '\x85', '\xa0',
    ' ', ' ', '﻿', '​', '\ud800', '\udc00', '\U0001F600', 'é', 'é',
    '中文', '한국어', '١٢', '{', '}', '{{', '}}', '$', '$$', '"""', "'''",
    '%s', '{0}', '\x0b', '\x0c', '\x1b', '­', '\U0010FFFF', '￿', ' ', '', 'None', 'True',
]


def rstr(maxlen=6):
    n = R.randint(0, maxlen)
    parts = []
    for _ in range(n):
        if R.random() < 0.5:
            parts.append(R.choice(SPECIAL))
        else:
            parts.append(''.join(R.choice('abcXYZ019 _-.:') for _ in range(R.randint(1, 5))))
    return ''.join(parts)


FLOATS = [0.0, -0.0, 1.0, -1.5, 0.1, 0.1 + 0.2, 1e16, 1e-7, 1e22, 123456789.0, 5e-324,
          1.7976931348623157e308, float('nan'), float('inf'), float('-inf'), 2.5, 1e15, 0.5e-4, 3.0e300]
INTS = [0, 1, -1, 42, 2**31, -(2**31), 2**63 - 1, -(2**63)]


def rscalar(allow_exotic=True):
    k = R.random()
    if k < 0.35:
        return rstr()
    if k < 0.5:
        return R.choice(INTS) if R.random() < 0.5 else R.randint(-1000, 1000)
    if k < 0.62:
        return R.choice(FLOATS) if R.random() < 0.6 else R.uniform(-1e6, 1e6)
    if k < 0.72:
        return R.choice([True, False])
    if k < 0.8:
        return None
    if not allow_exotic:
        return rstr()
    if k < 0.85:
        if R.random() < 0.5:
            return dt.datetime(R.randint(1, 9999), R.randint(1, 12), R.randint(1, 28), R.randint(0, 23),
                               R.randint(0, 59), R.randint(0, 59), R.choice([0, 1, 123456]))
        return dt.datetime(2024, 5, 6, 7, 8, 9, R.choice([0, 500]),
                           tzinfo=dt.timezone(dt.timedelta(minutes=R.choice([0, 330, -480]))))
    if k < 0.88:
        return dt.date(R.randint(1, 9999), R.randint(1, 12), R.randint(1, 28))
    if k < 0.9:
        return bytes(R.randint(0, 255) for _ in range(R.randint(0, 4)))
    return rstr()


def rvalue(depth=0, exotic=True):
    k = R.random()
    if depth >= 4 or k < 0.45:
        return rscalar(exotic)
    if k < 0.65:
        return [rvalue(depth + 1, exotic) for _ in range(R.randint(0, 4))]
    if k < 0.88:
        return {rkey(): rvalue(depth + 1, exotic) for _ in range(R.randint(0, 4))}
    if k < 0.94 and exotic:
        return tuple(rvalue(depth + 1, exotic) for _ in range(R.randint(0, 3)))
    if exotic:
        d = {}
        for _ in range(R.randint(1, 3)):
            key = R.choice([1, 2, -5, 1.5, float('nan'), True, False, None, rkey(), (1, 'a'), 0.0])
            d[key] = rvalue(depth + 1, exotic)
        return d
    return rscalar(exotic)


def rkey():
    s = rstr(3)
    return s if s != '$t' else 'x'


# ---- encoding ----

def enc(v):
    if isinstance(v, pydantic.BaseModel):
        raise TypeError('model')
    if isinstance(v, dict):
        if all(type(k) is str for k in v):
            return {k: enc(x) for k, x in v.items()}
        return {'$t': 'pydict', 'v': [[enc(k), enc(x)] for k, x in v.items()]}
    if isinstance(v, list):
        return [enc(x) for x in v]
    if isinstance(v, tuple):
        return {'$t': 'tuple', 'v': [enc(x) for x in v]}
    if isinstance(v, dt.datetime):
        return {'$t': 'dt', 'v': v.isoformat()}
    if isinstance(v, dt.date):
        return {'$t': 'date', 'v': v.isoformat()}
    if isinstance(v, bytes):
        return {'$t': 'bytes', 'v': v.hex()}
    return v


def dec(v):
    if isinstance(v, dict):
        if '$t' in v:
            t = v['$t']
            x = v['v']
            if t == 'pydict':
                return {dec(k): dec(y) for k, y in x}
            if t == 'tuple':
                return tuple(dec(y) for y in x)
            if t == 'dt':
                return dt.datetime.fromisoformat(x)
            if t == 'date':
                return dt.date.fromisoformat(x)
            if t == 'bytes':
                return bytes.fromhex(x)
            raise ValueError(t)
        return {k: dec(y) for k, y in v.items()}
    if isinstance(v, list):
        return [dec(y) for y in v]
    return v


def enc_result(r):
    if isinstance(r, list) and r and hasattr(r[0], 'role'):
        return [[x.role, x.content] for x in r]
    if hasattr(r, 'system') and hasattr(r, 'user'):
        return {'system': r.system.content, 'user': r.user.content, 'srole': r.system.role, 'urole': r.user.role}
    return enc(r)


def pack(text):
    if len(text) > 300:
        return {'z': base64.b64encode(zlib.compress(text.encode('ascii'), 9)).decode('ascii')}
    return {'text': text}


def run(fn, args):
    try:
        r = fn(args)
    except pydantic.ValidationError as e:
        return {'kind': 'exc', 'cls': 'ValidationError', 'title': e.title,
                'errors': [[list(x['loc']), x['type'], x['msg']] for x in e.errors()]}
    except Exception as e:  # noqa: BLE001
        out = {'kind': 'exc', 'cls': type(e).__name__}
        if isinstance(e, ValueError):
            out['msg'] = str(e)
        return out
    return {'kind': 'ok', **pack(json.dumps(enc_result(r), ensure_ascii=True))}


cases = []


def keep(expect):
    # A result with an integer that a 64-bit long cannot hold depends on a documented limit (R4, R6).
    if expect['kind'] != 'ok':
        return True
    text = expect['text'] if 'text' in expect else zlib.decompress(base64.b64decode(expect['z'])).decode('ascii')
    return BIG_INT.search(text) is None


def add(fn_name, raw_args, fn):
    e = enc(raw_args)
    text = json.dumps(e)
    args = dec(json.loads(text))  # the exact value the C# side sees
    expect = run(fn, args)
    if keep(expect):
        cases.append({'id': f'{fn_name}#{len(cases)}', 'fn': fn_name, 'args': text, 'expect': expect})


# ---- generated contexts ----
EDGE_TYPE_LISTS = [
    [{'fact_type_name': 'LIVES_IN', 'fact_type_signature': ['Person', 'Location'], 'fact_type_description': 'Where a person lives'}],
    [{'fact_type_name': 'WORKS_AT', 'fact_type_signature': ['Person', 'Organization'], 'fact_type_description': 'Employment'},
     {'fact_type_name': 'KNOWS', 'fact_type_signature': ['Person', 'Person'], 'fact_type_description': 'Café é 日本語 😀'}],
    [{'fact_type_name': 'X', 'fact_type_signature': ['Entity', 'Entity'], 'fact_type_description': ''}],
    [{}],
    [],
    [{'n': 'Café "quoted" {braces} {{double}} back\\slash\nnew\tline'}],
]


def redge_types():
    c = R.random()
    if c < 0.45:
        return R.choice(EDGE_TYPE_LISTS)
    if c < 0.55:
        return [rvalue(2) for _ in range(R.randint(0, 3))]
    if c < 0.62:
        return None
    if c < 0.7:
        return rstr(4)
    if c < 0.78:
        return {rkey(): rvalue(2) for _ in range(R.randint(0, 2))}
    if c < 0.84:
        return R.choice([0, 1, True, False, 0.0, (), ()])
    return rvalue()


def rstrict():
    return R.choice([True, False, None, 0, 1, '', 'x', 'false', [], [1]])


def rprev():
    c = R.random()
    if c < 0.5:
        return [rvalue(1) for _ in range(R.randint(0, 4))]
    if c < 0.58:
        return rstr(4)
    if c < 0.66:
        return {rkey(): rvalue(2) for _ in range(R.randint(0, 3))}
    if c < 0.72:
        return tuple(rvalue(2) for _ in range(R.randint(0, 3)))
    if c < 0.78:
        return None
    if c < 0.84:
        return R.choice([0, 5, 1.5, True])
    return rvalue()


def rcontext(required, optional=()):
    """required: keys read with context[...]; optional: keys read with context.get(...)."""
    exotic = R.random() < 0.3
    ctx = {}
    keys = list(required) + list(optional)
    for k in keys:
        if k in required and R.random() < 0.04:
            continue  # missing key
        if k in optional and R.random() < 0.3:
            continue
        if k == 'edge_types':
            ctx[k] = redge_types()
        elif k == 'strict_edge_types':
            ctx[k] = rstrict()
        elif k == 'previous_episodes':
            ctx[k] = rprev()
        elif k == 'custom_extraction_instructions':
            ctx[k] = R.choice([None, '', rstr(8), 'Be brief.\nNo emoji 😀', 0, 5, rvalue()])
        else:
            ctx[k] = rvalue(exotic=exotic)
    if R.random() < 0.2:
        ctx[rkey() + '_extra'] = rvalue()
    if R.random() < 0.3:
        R.shuffle(keys)
        ctx = {k: ctx[k] for k in keys if k in ctx} | {k: v for k, v in ctx.items() if k not in keys}
    return ctx


MODULES = [
    # name, function, required keys, optional keys, count
    ('edge', ee.edge, ['previous_episodes', 'episode_content', 'nodes', 'reference_time', 'custom_extraction_instructions'],
     ['edge_types', 'strict_edge_types'], 60),
    ('extract_attributes', ee.extract_attributes, ['fact', 'reference_time', 'existing_attributes'], [], 50),
    ('extract_timestamps', ee.extract_timestamps, ['fact', 'reference_time'], [], 40),
    ('extract_timestamps_batch', ee.extract_timestamps_batch, ['facts'], [], 40),
    ('resolve_edge', de.resolve_edge, ['existing_edges', 'edge_invalidation_candidates', 'new_edge'], [], 50),
    ('extract_message', ne.extract_message, ['entity_types', 'previous_episodes', 'episode_content'],
     ['edge_types', 'strict_edge_types', 'custom_extraction_instructions'], 60),
]
DEFAULTS = {'edge': ee.DefaultExtractEdgesPrompts(), 'extract_attributes': ee.DefaultExtractEdgesPrompts(),
            'extract_timestamps': ee.DefaultExtractEdgesPrompts(), 'extract_timestamps_batch': ee.DefaultExtractEdgesPrompts(),
            'resolve_edge': de.DefaultDedupeEdgesPrompts(), 'extract_message': ne.DefaultExtractNodesAndEdgesPrompts()}
VERSIONS = {'edge': ee.versions, 'extract_attributes': ee.versions, 'extract_timestamps': ee.versions,
            'extract_timestamps_batch': ee.versions, 'resolve_edge': de.versions, 'extract_message': ne.versions}

for name, fn, required, optional, n in MODULES:
    for i in range(n):
        ctx = rcontext(required, optional)
        add(name, ctx, fn)
        if i < 6:
            add('versions.' + name, ctx, lambda c, n=name: VERSIONS[n][n](c))
            add('default.' + name, ctx, lambda c, n=name: getattr(DEFAULTS[n], n)(c))
            add('default_as_messages.' + name, ctx, lambda c, n=name: getattr(DEFAULTS[n], n)(c).as_messages())
            add('default_as_messages_false.' + name, ctx,
                lambda c, n=name: getattr(DEFAULTS[n], n)(c).as_messages(append_unicode_note=False))

# The lookup by name that prompts/lib.py does: getattr(group, name)(context). One well-formed context for each name,
# so that a wrong entry of the map gives a different text. An unknown name raises AttributeError.
GOOD = {
    'previous_episodes': [{'uuid': 'u1', 'content': 'earlier'}], 'episode_content': 'Alice met Bob in Paris.',
    'nodes': [{'id': 0, 'name': 'Alice', 'entity_types': ['Person']}], 'reference_time': '2024-01-01T00:00:00Z',
    'custom_extraction_instructions': 'Be brief', 'edge_types': EDGE_TYPE_LISTS[1], 'strict_edge_types': True,
    'fact': 'Alice lives in Paris', 'existing_attributes': {'a': 1}, 'facts': ['Alice lives in Paris'],
    'existing_edges': [{'idx': 0, 'fact': 'f'}], 'edge_invalidation_candidates': [{'idx': 1, 'fact': 'g'}],
    'new_edge': 'Alice lives in Rome', 'entity_types': [{'entity_type_id': 0, 'entity_type_name': 'Entity'}],
}
for name, fn, required, optional, n in MODULES:
    add('getattr.' + name, {k: GOOD[k] for k in required + optional}, lambda c, n=name: getattr(DEFAULTS[n], n)(c))
    for i in range(3):
        add('getattr.' + name, rcontext(required, optional), lambda c, n=name: getattr(DEFAULTS[n], n)(c))


def getattr_unknown(a, group):
    try:
        getattr(group, a['name'])
    except AttributeError:
        return 'AttributeError'
    raise AssertionError('found')


# Names that are not methods: one that the map must not match by length or by case, and one that is far off.
for group_name, group, names in [('extract_edges', ee.DefaultExtractEdgesPrompts(), ['edge', 'extract_attributes', 'extract_timestamps', 'extract_timestamps_batch']),
                                 ('dedupe_edges', de.DefaultDedupeEdgesPrompts(), ['resolve_edge']),
                                 ('extract_nodes_and_edges', ne.DefaultExtractNodesAndEdgesPrompts(), ['extract_message'])]:
    for name in ['no_such_prompt'] + [v for n in names for v in (n[:-1] + 'x', n.upper())]:
        add('getattr_unknown.' + group_name, {'name': name}, lambda a, g=group: getattr_unknown(a, g))

# fixed contexts that cover each branch of the edge types section
for et in EDGE_TYPE_LISTS:
    for strict in [True, False]:
        base = {'previous_episodes': [], 'episode_content': 'x', 'nodes': [], 'reference_time': 't',
                'custom_extraction_instructions': ''}
        add('edge', {**base, 'edge_types': et, 'strict_edge_types': strict}, ee.edge)
        add('extract_message', {'entity_types': [], 'previous_episodes': [], 'episode_content': 'x',
                                'edge_types': et, 'strict_edge_types': strict,
                                'custom_extraction_instructions': 'Be brief.'}, ne.extract_message)

# ---- the helpers of extract_nodes_and_edges ----
for et in EDGE_TYPE_LISTS + [None]:
    for strict in [True, False]:
        add('_build_edge_types_section', {'edge_types': et, 'strict_edge_types': strict},
            lambda a: ne._build_edge_types_section(a['edge_types'], a['strict_edge_types']))
    add('_build_edge_types_section', {'edge_types': et},
        lambda a: ne._build_edge_types_section(a['edge_types']))
for i in range(30):
    et = R.choice(EDGE_TYPE_LISTS + [None, [{rkey(): rvalue(2)}], [{rkey(): rvalue(2)}, {rkey(): rvalue(2)}]])
    a = {'edge_types': et}
    if R.random() < 0.6:
        a['strict_edge_types'] = R.choice([True, False])
    add('_build_edge_types_section', a, lambda a: ne._build_edge_types_section(**a))
for text in [None, '', 'Be brief.\nNo emoji 😀', 'x', ' ', '{braces} {{double}}', 'é', '\x00']:
    add('_build_custom_instructions_section', {'text': text},
        lambda a: ne._build_custom_instructions_section(a['text']))
for i in range(15):
    add('_build_custom_instructions_section', {'text': R.choice([None, '', rstr(8), rstr(3)])},
        lambda a: ne._build_custom_instructions_section(a['text']))

# ---- models ----
POOL = lambda: R.choice([  # noqa: E731
    rstr(), rstr(), '', R.choice(INTS), R.choice(FLOATS), True, False, None, '1', ' 2 ', '1.0', '1e3', '-1',
    '0x10', '1_000', '١٢', '+3', 1.5, 2.0, [], {}, ['a'], {'a': 1}, b'abc', b'\xff', dt.date(2024, 1, 2),
    [1, 2], ['1', 2.0], [1, 'x'], [True], [None], [1.5],
])

VALID = {
    'source_entity_name': lambda: rstr(5), 'target_entity_name': lambda: rstr(5), 'relation_type': lambda: rstr(5),
    'fact': lambda: rstr(8), 'valid_at': lambda: R.choice([None, rstr(5), '2025-04-30T00:00:00Z']),
    'invalid_at': lambda: R.choice([None, rstr(5)]),
    'episode_indices': lambda: R.choice([[0], [], [1, 2], [R.randint(-3, 9) for _ in range(R.randint(0, 4))], ['1', 2.0, True]]),
    'name': lambda: rstr(5), 'entity_type_id': lambda: R.choice([0, 1, R.randint(-3, 9), '2', 3.0, True]),
    'duplicate_facts': lambda: R.choice([[], [0], [1, 2], ['1'], [2.0]]),
    'contradicted_facts': lambda: R.choice([[], [0], [3], ['1']]),
}


def rfields(names, nested=None):
    d = {}
    for n in names:
        if R.random() < 0.12:
            continue
        if nested and n in nested and R.random() < 0.7:
            d[n] = nested[n]()
        elif R.random() < 0.6:
            d[n] = VALID[n]()
        else:
            d[n] = POOL()
    if R.random() < 0.2:
        d['extra_' + rkey()] = rvalue()
    return d


EDGE_FIELDS = ['source_entity_name', 'target_entity_name', 'relation_type', 'fact', 'valid_at', 'invalid_at', 'episode_indices']
TS_FIELDS = ['valid_at', 'invalid_at']
FACT_FIELDS = ['source_entity_name', 'target_entity_name', 'relation_type', 'fact', 'episode_indices']


def redge():
    return rfields(EDGE_FIELDS) if R.random() < 0.85 else POOL()


def rts():
    return rfields(TS_FIELDS) if R.random() < 0.85 else POOL()


def rentity():
    return rfields(['name', 'entity_type_id']) if R.random() < 0.85 else POOL()


def rfact():
    return rfields(FACT_FIELDS) if R.random() < 0.85 else POOL()


MODELS = [
    ('Edge', ee.Edge, lambda: rfields(EDGE_FIELDS)),
    ('ExtractedEdges', ee.ExtractedEdges, lambda: rfields(['edges'], {'edges': lambda: [redge() for _ in range(R.randint(0, 3))]})),
    ('EdgeTimestamps', ee.EdgeTimestamps, lambda: rfields(TS_FIELDS)),
    ('BatchEdgeTimestamps', ee.BatchEdgeTimestamps,
     lambda: rfields(['timestamps'], {'timestamps': lambda: [rts() for _ in range(R.randint(0, 3))]})),
    ('EdgeDuplicate', de.EdgeDuplicate, lambda: rfields(['duplicate_facts', 'contradicted_facts'])),
    ('CombinedEntity', ne.CombinedEntity, lambda: rfields(['name', 'entity_type_id'])),
    ('CombinedFact', ne.CombinedFact, lambda: rfields(FACT_FIELDS)),
    ('CombinedExtraction', ne.CombinedExtraction,
     lambda: rfields(['extracted_entities', 'edges'],
                     {'extracted_entities': lambda: [rentity() for _ in range(R.randint(0, 3))],
                      'edges': lambda: [rfact() for _ in range(R.randint(0, 3))]})),
]
VALID['edges'] = lambda: []
VALID['timestamps'] = lambda: []
VALID['extracted_entities'] = lambda: []

for name, cls, gen in MODELS:
    for i in range(40):
        a = gen()
        add(name, a, lambda a, c=cls: c(**a).model_dump())
    # a valid value, then each field removed, then each field with a wrong type
    valid = {n: VALID[n]() for n in cls.model_fields}
    add(name, valid, lambda a, c=cls: c(**a).model_dump())
    for n in cls.model_fields:
        add(name, {k: v for k, v in valid.items() if k != n}, lambda a, c=cls: c(**a).model_dump())
        for bad in [None, 1.5, {'a': 1}, [[]], b'x']:
            add(name, {**valid, n: bad}, lambda a, c=cls: c(**a).model_dump())

# A typed construction with the required fields only: the default of each other field comes from the initializer.
REQUIRED_ONLY = [
    ('Edge', ee.Edge, {'source_entity_name': 'S', 'target_entity_name': 'T', 'relation_type': 'REL', 'fact': 'F é'}),
    ('EdgeTimestamps', ee.EdgeTimestamps, {}),
    ('CombinedFact', ne.CombinedFact, {'source_entity_name': 'S', 'target_entity_name': 'T', 'relation_type': 'REL', 'fact': 'F é'}),
]
for name, cls, req in REQUIRED_ONLY:
    assert {k for k, v in cls.model_fields.items() if v.is_required()} == set(req)
    add('new.' + name, req, lambda a, c=cls: c(**a).model_dump())

# ---- schemas, constants, versions ----
for name, cls, _ in MODELS:
    cases.append({'id': f'schema.{name}', 'fn': 'schema', 'args': json.dumps(name),
                  'expect': {'kind': 'ok', **pack(json.dumps(json.dumps(cls.model_json_schema())))}})
cases.append({'id': 'const._SYSTEM_PROMPT', 'fn': 'const', 'args': '"_SYSTEM_PROMPT"',
              'expect': {'kind': 'ok', **pack(json.dumps(ne._SYSTEM_PROMPT))}})
cases.append({'id': 'const._C2_INSTRUCTION_TEXT', 'fn': 'const', 'args': '"_C2_INSTRUCTION_TEXT"',
              'expect': {'kind': 'ok', **pack(json.dumps(ne._C2_INSTRUCTION_TEXT))}})
for mod_name, mod in [('extract_edges', ee), ('dedupe_edges', de), ('extract_nodes_and_edges', ne)]:
    cases.append({'id': f'versions.keys.{mod_name}', 'fn': 'versions_keys', 'args': json.dumps(mod_name),
                  'expect': {'kind': 'ok', **pack(json.dumps(repr(list(mod.versions))))}})


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    python = json.dumps('.'.join(str(p) for p in sys.version_info[:3]))
    source = '"graphiti_core/prompts/{extract_edges,dedupe_edges,extract_nodes_and_edges}.py"'
    lines = ['{"python": ' + python + ', "source": ' + source + ', "cases": [']
    lines.append(',\n'.join(json.dumps(c, ensure_ascii=True) for c in cases))
    lines.append(']}')
    text = '\n'.join(lines) + '\n'
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {len(cases)} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {len(cases)} cases, {len(text)} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
