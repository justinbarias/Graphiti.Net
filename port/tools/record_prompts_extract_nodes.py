"""Records the results of graphiti_core/prompts/extract_nodes.py and snippets.py, from real Python.

The judge has about 50 cases for these modules. This file gives the evaluation tests generated inputs, with the
value or the exception class that Python gives for each one (port/RULES.md, R25): each prompt function with each
context key present, each optional key missing, None, an empty list, non-ASCII text, a custom entity type list,
and a missing required key; each response model with valid data, each field missing, and a wrong type; the schema
text of each model; and the constants of the two modules.

    GRAPHITI_UPSTREAM=.upstream/graphiti .upstream/graphiti/.venv/bin/python port/tools/record_prompts_extract_nodes.py
    GRAPHITI_UPSTREAM=.upstream/graphiti .upstream/graphiti/.venv/bin/python port/tools/record_prompts_extract_nodes.py --check

It writes tests/Port.Evaluation/values/prompts_extract_nodes.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The tool needs Python 3.12 and the pinned upstream (the environment
variable GRAPHITI_UPSTREAM names the checkout). It changes nothing there. Nothing here uses the network. The random
seed is fixed. A result longer than 300 characters is stored as zlib and base64 to keep the file small.

A case is left out when its result depends on a documented limit of the port: an integer that a 64-bit long cannot
hold. The text of a KeyError message is not recorded.
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
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'prompts_extract_nodes.json'
sys.path.insert(0, str(UPSTREAM))

import pydantic  # noqa: E402

from graphiti_core.prompts import extract_nodes as en  # noqa: E402
from graphiti_core.prompts import models as m  # noqa: E402
from graphiti_core.prompts import snippets as sn  # noqa: E402
from graphiti_core.utils import text_utils as tu  # noqa: E402

BIG_INT = re.compile(r'(?<![\d.eE+-])-?\d{19,}(?![\d.eE])')

R = random.Random(20261127)

SPECIAL = [
    "'", '"', '\\', '\n', '\r', '\t', '\r\n', '\x00', '\x01', '\x1f', '\x7f', '\x80', '\x85', '\xa0',
    ' ', ' ', '﻿', '​', '\U0001F600', 'é', 'é',
    '中文', '한국어', '١٢', '{', '}', '{{', '}}', '$', '$$', '"""', "'''",
    '%s', '{0}', '\x0b', '\x0c', '\x1b', '\xad', '\U0010FFFF', '￿', ' ', '', 'None', 'True',
    '\\\n', ' \n', '\n ', '<EXAMPLE>', '</ENTITIES>',
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


def rkey():
    s = rstr(3)
    return s if s != '$t' else 'x'


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
            # No NaN key: Python keeps one NaN key after a JSON round trip (the same object), and PyDict does not.
            key = R.choice([1, 2, -5, 1.5, True, False, None, rkey(), (1, 'a'), 0.0])
            d[key] = rvalue(depth + 1, exotic)
        return d
    return rscalar(exotic)


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
    if isinstance(r, list) and r and isinstance(r[0], m.Message):
        return [[x.role, x.content] for x in r]
    if isinstance(r, m.ChatPrompt):
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
        return {'kind': 'exc', 'cls': type(e).__name__}
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


N = 50

# ---- prompt functions ----
EP = ['previous_episodes', 'episode_content']
KEYS = {
    'extract_message': ['entity_types'] + EP + ['custom_extraction_instructions'],
    'extract_json': ['entity_types', 'source_description', 'episode_content', 'custom_extraction_instructions'],
    'extract_text': ['entity_types', 'episode_content', 'custom_extraction_instructions'],
    'classify_nodes': EP + ['extracted_entities', 'entity_types'],
    'extract_attributes': EP + ['node'],
    'extract_summary': EP + ['node'],
    'extract_summaries_batch': EP + ['entities', 'entity_type_descriptions'],
    'extract_entity_summaries_from_episodes': EP + ['entities', 'entity_type_descriptions'],
    '_entity_type_descriptions_section': ['entity_type_descriptions'],
}
OPTIONAL = {'entity_type_descriptions'}


def entity_types():
    c = R.random()
    if c < 0.5:
        return [{'entity_type_id': i, 'entity_type_name': rstr(3) or 'T',
                 'entity_type_description': rstr(8)} for i in range(R.randint(0, 4))]
    if c < 0.6:
        return []
    if c < 0.7:
        return rstr(5)
    if c < 0.75:
        return None
    return rvalue(2)


def episodes():
    c = R.random()
    if c < 0.5:
        return [{'uuid': rstr(2), 'content': rstr(6), 'reference_time': rscalar()} for _ in range(R.randint(0, 3))]
    if c < 0.6:
        return []
    if c < 0.7:
        return rstr(4)
    if c < 0.78:
        return {rkey(): rvalue(2) for _ in range(R.randint(0, 3))}
    if c < 0.84:
        return tuple(rvalue(2) for _ in range(R.randint(0, 3)))
    if c < 0.9:
        return None
    return rvalue(1)


def descriptions():
    c = R.random()
    if c < 0.35:
        return {rstr(3) or 'Person': rstr(8) for _ in range(R.randint(1, 3))}
    if c < 0.5:
        return {}
    if c < 0.6:
        return []
    if c < 0.65:
        return ''
    if c < 0.7:
        return None
    if c < 0.75:
        return 0
    if c < 0.8:
        return [rstr(4)]
    return rvalue(2)


def rcontext(name):
    exo = R.random() < 0.3
    ctx = {}
    keys = list(KEYS[name])
    for k in keys:
        if k in OPTIONAL:
            if R.random() < 0.3:
                continue
        elif R.random() < 0.04:
            continue  # missing key
        if k == 'entity_types':
            ctx[k] = entity_types()
        elif k == 'previous_episodes':
            ctx[k] = episodes()
        elif k == 'entity_type_descriptions':
            ctx[k] = descriptions()
        elif k in ('entities', 'extracted_entities'):
            ctx[k] = R.choice([
                [{'name': rstr(3), 'summary': rstr(6), 'entity_types': [rstr(2)]} for _ in range(R.randint(0, 3))],
                [], rvalue(2)])
        elif k == 'node':
            ctx[k] = R.choice([{'name': rstr(3), 'summary': rstr(6), 'attributes': rvalue(2, exotic=False)},
                               {}, rvalue(1, exotic=exo)])
        else:
            ctx[k] = rvalue(exotic=exo)
    if R.random() < 0.2:
        ctx[rkey() + '_extra'] = rvalue()
    if R.random() < 0.3:
        R.shuffle(keys)
        ctx = {k: ctx[k] for k in keys if k in ctx} | {k: v for k, v in ctx.items() if k not in keys}
    return ctx


FNS = {
    'extract_message': en.extract_message, 'extract_json': en.extract_json, 'extract_text': en.extract_text,
    'classify_nodes': en.classify_nodes, 'extract_attributes': en.extract_attributes,
    'extract_summary': en.extract_summary, 'extract_summaries_batch': en.extract_summaries_batch,
    'extract_entity_summaries_from_episodes': en.extract_entity_summaries_from_episodes,
    '_entity_type_descriptions_section': en._entity_type_descriptions_section,
}
for name, fn in FNS.items():
    for i in range(N):
        ctx = rcontext(name)
        add(name, ctx, fn)
        if name != '_entity_type_descriptions_section' and i < N // 8:
            add('versions.' + name, ctx, lambda c, n=name: en.versions[n](c))
            prompts = en.DefaultExtractNodesPrompts()
            add('default.' + name, ctx, lambda c, n=name: getattr(prompts, n)(c))
            add('default_as_messages.' + name, ctx, lambda c, n=name: getattr(prompts, n)(c).as_messages())

# ---- models ----
POOL = lambda: R.choice([  # noqa: E731
    rstr(), rstr(), '', R.choice(INTS), R.choice(FLOATS), True, False, None, '1', ' 2 ', '1.0', '1e3', '-1',
    '0x10', '1_000', '١٢', 1.5, 2.0, [], {}, ['a'], {'a': 1}, b'abc', dt.date(2024, 1, 2),
])
VALID = {
    'name': lambda: rstr(8),
    'summary': lambda: rstr(8),
    'entity_type_id': lambda: R.choice([R.randint(-5, 50), R.choice(INTS), 3.0, '7', True]),
    'episode_indices': lambda: R.choice([[], [0], [1, 2], [R.randint(-3, 9) for _ in range(3)], ['1', 2.0], [True],
                                         [1.5], ['x'], None, (1, 2), 'ab']),
    'extracted_entities': lambda: [],
    'summaries': lambda: [],
}


def rfields(names, nested=None):
    d = {}
    for n in names:
        if R.random() < 0.12:
            continue
        if nested and n in nested and R.random() < 0.75:
            d[n] = nested[n]()
        elif R.random() < 0.6:
            d[n] = VALID[n]()
        else:
            d[n] = POOL()
    if R.random() < 0.2:
        d['extra_' + rkey()] = rvalue()
    return d


def rentity():
    return rfields(['name', 'entity_type_id', 'episode_indices'])


def rsummarized():
    return rfields(['name', 'summary'])


for i in range(N // 2):
    add('ExtractedEntity', rentity(), lambda a: en.ExtractedEntity(**a).model_dump())
for i in range(N // 2):
    add('ExtractedEntities', rfields(['extracted_entities'], {'extracted_entities': lambda: [
        (rentity() if R.random() < 0.8 else POOL()) for _ in range(R.randint(0, 3))]}),
        lambda a: en.ExtractedEntities(**a).model_dump())
for i in range(N // 3):
    add('EntitySummary', rfields(['summary']), lambda a: en.EntitySummary(**a).model_dump())
for i in range(N // 3):
    add('SummarizedEntity', rsummarized(), lambda a: en.SummarizedEntity(**a).model_dump())
for i in range(N // 2):
    add('SummarizedEntities', rfields(['summaries'], {'summaries': lambda: [
        (rsummarized() if R.random() < 0.8 else POOL()) for _ in range(R.randint(0, 3))]}),
        lambda a: en.SummarizedEntities(**a).model_dump())

# Valid data with the default and with each value of the default field.
for a in [{'name': 'A', 'entity_type_id': 1}, {'name': 'A', 'entity_type_id': 1, 'episode_indices': [2, 3]},
          {'name': 'A', 'entity_type_id': '4', 'episode_indices': []}, {'name': 'A', 'entity_type_id': 1, 'episode_indices': ['5']},
          {'name': 'A', 'entity_type_id': 1, 'episode_indices': None}, {'name': 'é', 'entity_type_id': 0, 'x': 1}]:
    add('ExtractedEntity', a, lambda a: en.ExtractedEntity(**a).model_dump())
    add('ExtractedEntities', {'extracted_entities': [a, a]}, lambda a: en.ExtractedEntities(**a).model_dump())
add('SummarizedEntities', {'summaries': [{'name': 'n', 'summary': 's'}]}, lambda a: en.SummarizedEntities(**a).model_dump())
add('EntitySummary', {'summary': 's'}, lambda a: en.EntitySummary(**a).model_dump())

# ---- schemas and constants ----
for name in ['ExtractedEntity', 'ExtractedEntities', 'EntitySummary', 'SummarizedEntity', 'SummarizedEntities']:
    cases.append({'id': f'schema.{name}', 'fn': 'schema', 'args': json.dumps(name),
                  'expect': {'kind': 'ok', **pack(json.dumps(json.dumps(getattr(en, name).model_json_schema())))}})
cases.append({'id': 'const.summary_instructions', 'fn': 'const', 'args': '"summary_instructions"',
              'expect': {'kind': 'ok', **pack(json.dumps(sn.summary_instructions, ensure_ascii=True))}})
cases.append({'id': 'const.MAX_SUMMARY_CHARS', 'fn': 'const', 'args': '"MAX_SUMMARY_CHARS"',
              'expect': {'kind': 'ok', **pack(json.dumps(tu.MAX_SUMMARY_CHARS))}})
cases.append({'id': 'const._entity_episode_summary_system_prompt', 'fn': 'const',
              'args': '"_entity_episode_summary_system_prompt"',
              'expect': {'kind': 'ok', **pack(json.dumps(en._entity_episode_summary_system_prompt, ensure_ascii=True))}})
cases.append({'id': 'versions.keys', 'fn': 'versions_keys', 'args': '"x"',
              'expect': {'kind': 'ok', **pack(json.dumps(repr(list(en.versions))))}})


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    python = json.dumps('.'.join(str(p) for p in sys.version_info[:3]))
    source = '"graphiti_core/prompts/{extract_nodes,snippets}.py"'
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
