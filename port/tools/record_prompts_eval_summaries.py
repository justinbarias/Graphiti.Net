"""Records the results of graphiti_core/prompts/{eval,summarize_nodes,summarize_sagas,names}.py, from real Python.

The judge has a few cases for these modules. This file gives the evaluation tests generated
inputs, with the value or the exception class that Python gives for each one (port/RULES.md, R25).

    GRAPHITI_UPSTREAM=.upstream/graphiti .upstream/graphiti/.venv/bin/python port/tools/record_prompts_eval_summaries.py
    GRAPHITI_UPSTREAM=.upstream/graphiti .upstream/graphiti/.venv/bin/python port/tools/record_prompts_eval_summaries.py --check

It writes tests/Port.Evaluation/values/prompts_eval_summaries.json. --check writes nothing and exits 1
if the file differs from what the run would write. The tool needs Python 3.12 and the pinned
upstream (the environment variable GRAPHITI_UPSTREAM, default .upstream/graphiti). It changes nothing
there. Nothing here uses the network. The random seed is fixed. A result longer than 300 characters is
stored as zlib and base64 to keep the file small.

A case is left out when its result depends on a documented limit of the port: an integer that a
64-bit long cannot hold. The text of a KeyError message is not recorded.
"""
from __future__ import annotations

import base64
import datetime as dt
import json
import os
import random
import re
import sys
import typing
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'prompts_eval_summaries.json'
sys.path.insert(0, str(UPSTREAM))

import pydantic  # noqa: E402

from graphiti_core.prompts import eval as ev  # noqa: E402
from graphiti_core.prompts import models as m  # noqa: E402
from graphiti_core.prompts import names as nm  # noqa: E402
from graphiti_core.prompts import summarize_nodes as sn  # noqa: E402
from graphiti_core.prompts import summarize_sagas as ss  # noqa: E402

BIG_INT = re.compile(r'(?<![\d.eE+-])-?\d{19,}(?![\d.eE])')

R = random.Random(20261127)

SPECIAL = [
    "'", '"', '\\', '\n', '\r', '\t', '\r\n', '\x00', '\x01', '\x1f', '\x7f', '\x80', '\x85', '\xa0',
    ' ', ' ', '﻿', '​', '\ud800', '\udc00', '\U0001F600', 'é', 'é',
    '中文', '한국어', '١٢', '{', '}', '{{', '}}', '$', '$$', '"""', "'''",
    '%s', '{0}', '\x0b', '\x0c', '\x1b', '­', '\U0010FFFF', '￿', ' ', '', 'None', 'True',
    '\n---\n', '---',
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

# ---- prompt functions ----
# (module, function name, context keys, keys that a function reads with a default)
FUNCS = [
    (ev, 'query_expansion', ['query']),
    (ev, 'qa_prompt', ['query', 'entity_summaries', 'facts']),
    (ev, 'eval_prompt', ['query', 'answer', 'response']),
    (ev, 'eval_add_episode_results', ['previous_messages', 'message', 'baseline', 'candidate']),
    (sn, 'summarize_pair', ['node_summaries']),
    (sn, 'summarize_context', ['previous_episodes', 'episode_content', 'node_name', 'node_summary', 'attributes']),
    (sn, 'summary_description', ['summary']),
    (ss, 'summarize_saga', ['saga_name', 'existing_summary', 'episodes']),
]
DEFAULTED = {'summarize_saga'}


def rcontext(name, keys):
    exo = R.random() < 0.3
    ctx = {}
    keys = list(keys)
    miss = 0.5 if name in DEFAULTED else 0.04
    for k in keys:
        if R.random() < miss:
            continue  # missing key
        if name == 'summarize_saga':
            if k == 'episodes':
                c = R.random()
                if c < 0.5:
                    ctx[k] = [rstr(6) for _ in range(R.randint(0, 4))]
                elif c < 0.6:
                    ctx[k] = None
                elif c < 0.7:
                    ctx[k] = rstr(5)
                elif c < 0.78:
                    ctx[k] = tuple(rstr(4) for _ in range(R.randint(0, 3)))
                elif c < 0.86:
                    ctx[k] = {rstr(3): 1 for _ in range(R.randint(0, 3))}
                else:
                    ctx[k] = rvalue(2, exotic=exo)
            else:
                c = R.random()
                ctx[k] = rstr(8) if c < 0.7 else (None if c < 0.8 else rvalue(2, exotic=exo))
        else:
            ctx[k] = rvalue(exotic=exo)
    if R.random() < 0.2:
        ctx[rkey() + '_extra'] = rvalue()
    if R.random() < 0.3:
        R.shuffle(keys)
        ctx = {k: ctx[k] for k in keys if k in ctx} | {k: v for k, v in ctx.items() if k not in keys}
    return ctx


PROMPTS = {
    ev: ev.DefaultEvalPrompts(),
    sn: sn.DefaultSummarizeNodesPrompts(),
    ss: ss.DefaultSummarizeSagasPrompts(),
}

for mod, name, keys in FUNCS:
    fn = getattr(mod, name)
    prompts = PROMPTS[mod]
    for i in range(N):
        ctx = rcontext(name, keys)
        add(name, ctx, fn)
        if i < N // 8:
            add('versions.' + name, ctx, lambda c, mo=mod, n=name: mo.versions[n](c))
            add('default.' + name, ctx, lambda c, p=prompts, n=name: getattr(p, n)(c))
            add('default_as_messages.' + name, ctx, lambda c, p=prompts, n=name: getattr(p, n)(c).as_messages())
            add('default_as_messages_false.' + name, ctx,
                lambda c, p=prompts, n=name: getattr(p, n)(c).as_messages(append_unicode_note=False))

# The lookup by name that prompts/lib.py does: getattr(group, name)(context). One well-formed context for each name,
# so that a wrong entry of the map gives a different text. An unknown name raises AttributeError.
GOOD = {
    'query': 'Who lives in Paris?', 'entity_summaries': [{'name': 'Alice', 'summary': 's'}], 'facts': ['Alice lives in Paris'],
    'answer': 'Alice', 'response': 'Alice lives in Paris', 'previous_messages': ['hello'], 'message': 'hi there',
    'baseline': 'base', 'candidate': 'cand', 'node_summaries': ['one', 'two'], 'previous_episodes': ['earlier'],
    'episode_content': 'Alice met Bob.', 'node_name': 'Alice', 'node_summary': 'A person', 'attributes': {'age': 3},
    'summary': 'A summary', 'saga_name': 'Saga', 'existing_summary': 'Old', 'episodes': ['e1', 'e2'],
}
for mod, name, keys in FUNCS:
    prompts = PROMPTS[mod]
    add('getattr.' + name, {k: GOOD[k] for k in keys}, lambda c, p=prompts, n=name: getattr(p, n)(c))
    for i in range(3):
        add('getattr.' + name, rcontext(name, keys), lambda c, p=prompts, n=name: getattr(p, n)(c))


def getattr_unknown(a, group):
    try:
        getattr(group, a['name'])
    except AttributeError:
        return 'AttributeError'
    raise AssertionError('found')


# Names that are not methods: one that the map must not match by length or by case, and one that is far off.
for group_name, mod in [('eval', ev), ('summarize_nodes', sn), ('summarize_sagas', ss)]:
    names = [n for m_, n, _ in FUNCS if m_ is mod]
    for name in ['no_such_prompt'] + [v for n in names for v in (n[:-1] + 'x', n.upper())]:
        add('getattr_unknown.' + group_name, {'name': name}, lambda a, g=PROMPTS[mod]: getattr_unknown(a, g))

# ---- the response models ----
POOL = lambda: R.choice([  # noqa: E731
    rstr(), rstr(), '', R.choice(INTS), R.choice(FLOATS), True, False, None, '1', ' 2 ', '1.0', 'true', 'False',
    'yes', 'off', 'on', 'n', 'T', 0, 1, [], {}, ['x'], {'a': 1}, b'abc', b'\xff', dt.date(2024, 1, 2),
])
VALID = {
    'query': lambda: rstr(8),
    'ANSWER': lambda: rstr(8),
    'is_correct': lambda: R.choice([True, False, 0, 1, 'true', 'False', 'yes', 'off', 'on', 'n', 'T', 1.0]),
    'reasoning': lambda: rstr(8),
    'candidate_is_worse': lambda: R.choice([True, False, 0, 1, 'true', 'False', 'yes', 'off', 'on', 'n', 'T', 1.0]),
    'summary': lambda: rstr(8),
    'description': lambda: rstr(8),
}
MODELS = [
    ('QueryExpansion', ev.QueryExpansion, ['query']),
    ('QAResponse', ev.QAResponse, ['ANSWER']),
    ('EvalResponse', ev.EvalResponse, ['is_correct', 'reasoning']),
    ('EvalAddEpisodeResults', ev.EvalAddEpisodeResults, ['candidate_is_worse', 'reasoning']),
    ('Summary', sn.Summary, ['summary']),
    ('SummaryDescription', sn.SummaryDescription, ['description']),
    ('SagaSummary', ss.SagaSummary, ['summary']),
]


def rfields(names):
    d = {}
    for n in names:
        if R.random() < 0.15:
            continue
        d[n] = VALID[n]() if R.random() < 0.6 else POOL()
    if R.random() < 0.2:
        d['extra_' + rkey()] = rvalue()
    return d


for name, cls, names in MODELS:
    # Each field missing in turn, and the valid data.
    valid = {n: ('ok' if n in ('query', 'ANSWER', 'reasoning', 'summary', 'description') else True) for n in names}
    add(name, valid, lambda a, c=cls: c(**a).model_dump())
    for n in names:
        add(name, {k: v for k, v in valid.items() if k != n}, lambda a, c=cls: c(**a).model_dump())
        add(name, {**valid, n: [1]}, lambda a, c=cls: c(**a).model_dump())
        add(name, {**valid, n: None}, lambda a, c=cls: c(**a).model_dump())
    add(name, {}, lambda a, c=cls: c(**a).model_dump())
    for i in range(N // 2):
        add(name, rfields(names), lambda a, c=cls: c(**a).model_dump())
    cases.append({'id': f'schema.{name}', 'fn': 'schema', 'args': json.dumps(name),
                  'expect': {'kind': 'ok', **pack(json.dumps(json.dumps(cls.model_json_schema())))}})

# ---- names ----
cases.append({'id': 'names.PromptGroup', 'fn': 'names', 'args': json.dumps('PromptGroup'),
              'expect': {'kind': 'ok', **pack(json.dumps(list(typing.get_args(nm.PromptGroup))))}})
cases.append({'id': 'names.PromptName', 'fn': 'names', 'args': json.dumps('PromptName'),
              'expect': {'kind': 'ok', **pack(json.dumps(list(typing.get_args(nm.PromptName))))}})
for mod, label in [(ev, 'eval'), (sn, 'summarize_nodes'), (ss, 'summarize_sagas')]:
    cases.append({'id': f'versions_keys.{label}', 'fn': 'versions_keys', 'args': json.dumps(label),
                  'expect': {'kind': 'ok', **pack(json.dumps(repr(list(mod.versions))))}})


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    python = json.dumps('.'.join(str(p) for p in sys.version_info[:3]))
    source = '"graphiti_core/prompts/{eval,summarize_nodes,summarize_sagas,names}.py"'
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
