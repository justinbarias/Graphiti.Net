"""Records the results of graphiti_core/prompts/dedupe_nodes.py, models.py, and prompt_helpers.py, from real Python.

The judge has a few cases for these modules. This file gives the evaluation tests about 1,200
generated inputs, with the value or the exception class that Python gives for each one
(port/RULES.md, R25).

    .upstream/graphiti/.venv/bin/python port/tools/record_prompt_values.py
    .upstream/graphiti/.venv/bin/python port/tools/record_prompt_values.py --check

It writes tests/Port.Evaluation/values/prompts_dedupe_nodes.json. --check writes nothing and exits 1
if the file differs from what the run would write. The tool needs Python 3.12 and the pinned
upstream in .upstream/graphiti. It changes nothing there. Nothing here uses the network. The random
seed is fixed. A result longer than 300 characters is stored as zlib and base64 to keep the file small.

A case is left out when its result depends on a documented limit of the port: an integer that a
64-bit long cannot hold. The text of a KeyError message is not recorded.
"""
from __future__ import annotations

import base64
import datetime as dt
import json
import random
import re
import sys
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = ROOT / '.upstream' / 'graphiti'
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'prompts_dedupe_nodes.json'
sys.path.insert(0, str(UPSTREAM))

import pydantic  # noqa: E402

from graphiti_core.prompts import dedupe_nodes as dn  # noqa: E402
from graphiti_core.prompts import models as m  # noqa: E402
from graphiti_core.prompts import prompt_helpers as ph  # noqa: E402

BIG_INT = re.compile(r'(?<![\d.eE+-])-?\d{19,}(?![\d.eE])')

R = random.Random(20261126)

SPECIAL = [
    "'", '"', '\\', '\n', '\r', '\t', '\r\n', '\x00', '\x01', '\x1f', '\x7f', '\x80', '\x85', '\xa0',
    ' ', ' ', '﻿', '​', '\ud800', '\udc00', '\U0001F600', 'é', 'é',
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
    if isinstance(v, BaseModel_t):
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
    if isinstance(v, type):
        return {'$t': 'class', 'v': v.__name__}
    return v


BaseModel_t = pydantic.BaseModel
CLASSES = {'NodeDuplicate': dn.NodeDuplicate, 'NodeResolutions': dn.NodeResolutions}


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
            if t == 'class':
                return CLASSES[x]
            if t == 'msgs':
                return [m.Message(role=r, content=c) for r, c in x]
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


N = 100

# ---- to_prompt_json ----
INDENTS = [None, 0, 1, 2, 4, -1, -3, 10, -(2**31), -(2**40)]
for data, indent in [([1, [2], {'a': []}], -(2**40)), (5, -(2**40)), ({'a': [1, {'b': 2}]}, 0), ({'a': [1, {'b': 2}], 'c': []}, 2), ([], 2), ({}, 0)]:
    add('to_prompt_json', {'data': data, 'indent': indent}, lambda a: ph.to_prompt_json(**a))
for i in range(2 * N):
    data = rvalue()
    if R.random() < 0.08:
        data = R.choice([[], {}, 5, 'x', None, ()])
    a = {'data': data}
    if R.random() < 0.6:
        a['ensure_ascii'] = R.choice([True, False])
    if R.random() < 0.6:
        a['indent'] = R.choice(INDENTS)
    if a.get('indent') is not None and a['indent'] > 100:
        a['data'] = R.choice([[], {}, 5, 'x', None, ()])
    elif a.get('indent') is not None and a['indent'] < -100 and R.random() < 0.5:
        a['data'] = R.choice([[1, [2]], {'a': 1}, [], 5])
    add('to_prompt_json', a, lambda a: ph.to_prompt_json(**a))

# ---- prompt functions ----
KEYS = {
    'node': ['previous_episodes', 'episode_content', 'extracted_node', 'entity_type_description', 'existing_nodes'],
    'nodes': ['previous_episodes', 'episode_content', 'extracted_nodes', 'existing_nodes'],
    'node_list': ['nodes'],
}


def rcontext(name):
    global EXO
    EXO = R.random() < 0.3
    ctx = {}
    keys = list(KEYS[name])
    for k in keys:
        if R.random() < 0.04:
            continue  # missing key
        if k == 'extracted_nodes':
            c = R.random()
            if c < 0.45:
                ctx[k] = [rvalue(1, exotic=EXO) for _ in range(R.randint(0, 5))]
            elif c < 0.6:
                ctx[k] = rstr(5)
            elif c < 0.72:
                ctx[k] = {rkey(): rvalue(2) for _ in range(R.randint(0, 3))}
            elif c < 0.8:
                ctx[k] = tuple(rvalue(2) for _ in range(R.randint(0, 3)))
            else:
                ctx[k] = rvalue()
        else:
            ctx[k] = rvalue(exotic=EXO)
    if R.random() < 0.2:
        ctx[rkey() + '_extra'] = rvalue()
    if R.random() < 0.3:
        R.shuffle(keys)
        ctx = {k: ctx[k] for k in keys if k in ctx} | {k: v for k, v in ctx.items() if k not in keys}
    return ctx


for name, fn in [('node', dn.node), ('nodes', dn.nodes), ('node_list', dn.node_list)]:
    for i in range(N):
        ctx = rcontext(name)
        add(name, ctx, fn)
        if i < N // 8:
            add('versions.' + name, ctx, lambda c, n=name: dn.versions[n](c))
            prompts = dn.DefaultDedupeNodesPrompts()
            add('default.' + name, ctx, lambda c, n=name: getattr(prompts, n)(c))
            add('default_as_messages.' + name, ctx, lambda c, n=name: getattr(prompts, n)(c).as_messages())
            add('default_as_messages_false.' + name, ctx,
                lambda c, n=name: getattr(prompts, n)(c).as_messages(append_unicode_note=False))

# ---- models ----
POOL = lambda: R.choice([  # noqa: E731
    rstr(), rstr(), 'system', 'user', 'System', 'assistant', ' system', 'system ', '',
    R.choice(INTS), R.choice(FLOATS), True, False, None, '1', ' 2 ', '1.0', '1e3', '-1', '0x10', '1_000',
    '١٢', '+3', '١', 1.5, 2.0, [], {}, ['system'], {'a': 1}, b'abc', b'\xff',
    dt.date(2024, 1, 2),
])


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


VALID = {
    'role': lambda: R.choice(['system', 'user', rstr()]),
    'content': lambda: rstr(8),
    'name': lambda: rstr(8),
    'id': lambda: R.choice([R.randint(-5, 50), R.choice(INTS), 3.0, '7', True]),
    'duplicate_candidate_id': lambda: R.choice([R.randint(-1, 9), -1, R.choice(INTS), '0', 1.0]),
    'dynamic_schema': lambda: R.choice([True, False, 0, 1, 'true', 'False', 'yes', 'off', 'on', 'n', 'T', 1.0]),
    'response_model': lambda: R.choice([None, dn.NodeDuplicate, dn.NodeResolutions]),
    'system': lambda: {'content': rstr()},
    'user': lambda: {'content': rstr()},
    'entity_resolutions': lambda: [],
}


def rmsg_dict(role_default):
    d = {}
    if R.random() < 0.5:
        d['role'] = R.choice([role_default, role_default, 'system', 'user', 'other', 1, None, role_default.upper()])
    if R.random() < 0.9:
        d['content'] = R.choice([rstr(), rstr(), 1, None, b'b', True])
    return d


def rdup():
    return rfields(['id', 'name', 'duplicate_candidate_id'])


for i in range(N // 2):
    a = rfields(['role', 'content'])
    a.pop('bytearray', None)
    add('Message', {k: (bytes(v) if isinstance(v, bytearray) else v) for k, v in a.items()}, lambda a: m.Message(**a).model_dump())
for cls_name, cls, role in [('SystemMessage', m.SystemMessage, 'system'), ('UserMessage', m.UserMessage, 'user')]:
    for i in range(N // 2):
        a = rmsg_dict(role) if R.random() < 0.6 else rfields(['role', 'content'])
        a = {k: (bytes(v) if isinstance(v, bytearray) else v) for k, v in a.items()}
        add(cls_name, a, lambda a, c=cls: c(**a).model_dump())
for i in range(N):
    a = rfields(['system', 'user'], {'system': lambda: rmsg_dict('system'), 'user': lambda: rmsg_dict('user')})
    a = {k: (bytes(v) if isinstance(v, bytearray) else v) for k, v in a.items()}
    add('ChatPrompt', a, lambda a: m.ChatPrompt(**a).model_dump())
for i in range(N):
    a = rfields(['name', 'response_model', 'dynamic_schema'],
                {'response_model': lambda: R.choice([dn.NodeDuplicate, dn.NodeResolutions, None, 'NodeDuplicate'])})
    a = {k: (bytes(v) if isinstance(v, bytearray) else v) for k, v in a.items()}
    add('PromptSpec', a, lambda a: m.PromptSpec(**a).model_dump())
for i in range(N // 2):
    a = rdup()
    a = {k: (bytes(v) if isinstance(v, bytearray) else v) for k, v in a.items()}
    add('NodeDuplicate', a, lambda a: dn.NodeDuplicate(**a).model_dump())
for i in range(N // 2):
    a = rfields(['entity_resolutions'], {'entity_resolutions': lambda: [
        (rdup() if R.random() < 0.8 else POOL()) for _ in range(R.randint(0, 3))]})
    a = {k: (bytes(v) if isinstance(v, bytearray) else v) for k, v in a.items()}
    add('NodeResolutions', a, lambda a: dn.NodeResolutions(**a).model_dump())

# ---- ChatPrompt.from_messages / as_messages ----
ROLES = ['system', 'user', 'assistant', 'System', '', ' user', 'user ']
for i in range(N * 8 // 10):
    msgs = [[R.choice(ROLES) if R.random() < 0.5 else R.choice(['system', 'user']), rstr()] for _ in range(R.randint(0, 4))]
    if R.random() < 0.4:
        msgs = [['system', rstr()], ['user', rstr()]]
    text = json.dumps({'$t': 'msgs', 'v': msgs})
    args = dec(json.loads(text))
    cases.append({'id': f'from_messages#{len(cases)}', 'fn': 'from_messages', 'args': text,
                  'expect': run(m.ChatPrompt.from_messages, args)})
for i in range(N * 6 // 10):
    a = {'system': rstr(8), 'user': rstr(8)}
    if R.random() < 0.66:
        a['flag'] = R.choice([True, False])

    def f(a):
        p = m.ChatPrompt(system=m.SystemMessage(content=a['system']), user=m.UserMessage(content=a['user']))
        return p.as_messages() if 'flag' not in a else p.as_messages(append_unicode_note=a['flag'])

    add('as_messages', a, f)

# ---- schemas and constants ----
for name, cls in [('Message', m.Message), ('SystemMessage', m.SystemMessage), ('UserMessage', m.UserMessage),
                  ('ChatPrompt', m.ChatPrompt), ('NodeDuplicate', dn.NodeDuplicate),
                  ('NodeResolutions', dn.NodeResolutions)]:
    cases.append({'id': f'schema.{name}', 'fn': 'schema', 'args': json.dumps(name),
                  'expect': {'kind': 'ok', **pack(json.dumps(json.dumps(cls.model_json_schema())))}})
cases.append({'id': 'const.DO_NOT_ESCAPE_UNICODE', 'fn': 'const', 'args': '"x"',
              'expect': {'kind': 'ok', **pack(json.dumps(ph.DO_NOT_ESCAPE_UNICODE))}})
cases.append({'id': 'versions.keys', 'fn': 'versions_keys', 'args': '"x"',
              'expect': {'kind': 'ok', **pack(json.dumps(repr(list(dn.versions))))}})


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    python = json.dumps('.'.join(str(p) for p in sys.version_info[:3]))
    source = '"graphiti_core/prompts/{dedupe_nodes,models,prompt_helpers}.py"'
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
