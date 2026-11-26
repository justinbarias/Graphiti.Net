"""Records results of llm_client/cache.py and cross_encoder/openai_reranker_client.py from real Python.

The recording gives the evaluation tests generated inputs from a fixed seed (port/RULES.md, R23 and R25):

- cache: for fixed keys and values, the rows of the table `cache` after each `set` (the key, the bytes of the stored
  value, and the SQLite type of the value), the text of the table definition, what `get` returns, and the log lines
  of the corrupted entry and of the value that is not JSON (gap J7 holds the same idea for the state store).
- reranker: with a scripted fake for `client.chat.completions.create` (no real request), the arguments of each call,
  the messages with them, the result list, and the error with the log line, for generated passages, replies, and
  failures. The fake answers the n-th call with the n-th scripted reply.

    .upstream/graphiti/.venv/bin/python port/tools/record_cache_reranker.py
    .upstream/graphiti/.venv/bin/python port/tools/record_cache_reranker.py --check

It writes tests/Port.Evaluation/values/cache_reranker.json. --check writes nothing and exits 1 if the file differs
from what the run would write. The tool needs Python 3.12 and the pinned upstream. The environment variable
GRAPHITI_UPSTREAM names the upstream checkout, and it defaults to .upstream/graphiti in the repository. The tool
changes nothing there. Nothing here uses the network.
"""
from __future__ import annotations

import asyncio
import json
import logging
import math
import os
import random
import sqlite3
import sys
import tempfile
import types
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'cache_reranker.json'

rng = random.Random(20261128)

KEYS = ['k', 'key1', '', 'a b', 'é', '日本語', '\U0001F600', "it's", '"q"', 'x' * 300, 'd41d8cd98f00b204e9800998ecf8427e',
        'line\nbreak']
VALUES = [
    {},
    {'content': 'hello', 'tokens': 42},
    {'version': 1},
    {'version': 2},
    {'choices': [{'message': {'role': 'assistant', 'content': 'test'}}],
     'usage': {'prompt_tokens': 10, 'completion_tokens': 5},
     'nested': {'a': [1, 2, 3], 'b': None, 'c': True}},
    {'text': 'é 日本語 \U0001F600 \n \t " \\ /'},
    {'f': 0.1, 'g': 1e100, 'h': -0.0, 'i': 1.0, 'j': 123456789012345678901234567890, 'k': 5e-324},
    {'nan': float('nan'), 'inf': float('inf'), 'ninf': float('-inf')},
    {'b': 1, 'a': 2, 'c': {'z': 1, 'y': 2}},
    {'empty_list': [], 'empty_dict': {}, 'empty_str': ''},
    {'1': 'a', '2': 'b'},
]


def number_text(value):
    return json.dumps(value)


def row_of(db_path: str, key: str):
    con = sqlite3.connect(db_path)
    try:
        return con.execute('SELECT key, value, typeof(value) FROM cache WHERE key = ?', (key,)).fetchone()
    finally:
        con.close()


def record_cache(LLMCache, logger_name: str) -> dict:
    class Handler(logging.Handler):
        def __init__(self) -> None:
            super().__init__(logging.DEBUG)
            self.lines: list = []

        def emit(self, record) -> None:
            self.lines.append([record.levelname, record.getMessage()])

    logger = logging.getLogger(logger_name)
    logger.setLevel(logging.DEBUG)
    handler = Handler()
    logger.addHandler(handler)
    out = {'sets': [], 'overwrites': [], 'missing': [], 'bad': [], 'corrupt': []}
    try:
        with tempfile.TemporaryDirectory() as tmp:
            # One cache that receives each pair: the row of the key follows each set.
            cache = LLMCache(os.path.join(tmp, 'all'))
            db_path = os.path.join(tmp, 'all', 'cache.db')
            con = sqlite3.connect(db_path)
            out['schema'] = [r[0] for r in con.execute("SELECT sql FROM sqlite_master WHERE name = 'cache' ORDER BY type")]
            out['table_names'] = [r[0] for r in con.execute("SELECT name FROM sqlite_master ORDER BY name")]
            con.close()
            for i, value in enumerate(VALUES):
                key = KEYS[i % len(KEYS)]
                cache.set(key, value)
                stored = row_of(db_path, key)
                got = cache.get(key)
                out['sets'].append({
                    'key': key, 'value': value,
                    'row': list(stored) if stored else None,
                    'get': json.dumps(got),
                })
            # The same key twice: the last value stays, and there is one row.
            for key, first, second in [('same', {'v': 1}, {'v': 2, 'extra': [1, 2]}), ('é', {'a': 1}, {})]:
                cache.set(key, first)
                before = row_of(db_path, key)
                cache.set(key, second)
                after = row_of(db_path, key)
                con = sqlite3.connect(db_path)
                count = con.execute('SELECT count(*) FROM cache WHERE key = ?', (key,)).fetchone()[0]
                con.close()
                out['overwrites'].append({'key': key, 'first': first, 'second': second, 'before': list(before),
                                          'after': list(after), 'count': count, 'get': json.dumps(cache.get(key))})
            for key in ['nonexistent', '', 'é', 'SAME']:
                handler.lines.clear()
                got = cache.get(key)
                out['missing'].append({'key': key, 'get': json.dumps(got), 'log': list(handler.lines)})
            # A value that is not JSON: nothing is stored, and the call logs one warning.
            bad_cases = [('bad', {'func': 'lambda'}, 'func'), ('bad2', {'s': 'set'}, 'set'), ('bad5', {'b': b'bytes'}, 'bytes'),
                         ('é', {'o': object()}, 'object')]
            for key, value, tag in bad_cases:
                handler.lines.clear()
                before = row_of(db_path, key)
                try:
                    if tag == 'func':
                        value = {'func': lambda x: x}
                    elif tag == 'set':
                        value = {'s': {1, 2}}
                    cache.set(key, value)
                    error = None
                except Exception as e:  # recorded: the port must raise too
                    error = type(e).__name__
                after = row_of(db_path, key)
                out['bad'].append({'key': key, 'tag': tag, 'error': error, 'log': list(handler.lines),
                                   'row_before': list(before) if before else None,
                                   'row_after': list(after) if after else None,
                                   'get': json.dumps(cache.get(key))})
            # Corrupt rows, put in with the SQL of the upstream test.
            raws = ['not valid json{{{', '', '{"a": 1', 'NaN', '{"a": NaN}', '[1, 2]', '5', 'null', '"s"', '{"a": 1} x',
                    "{'a': 1}", '﻿{"a": 1}']
            for n, raw in enumerate(raws):
                key = f'corrupt{n}'
                cache._conn.execute('INSERT OR REPLACE INTO cache (key, value) VALUES (?, ?)', (key, raw))
                cache._conn.commit()
                handler.lines.clear()
                try:
                    got = json.dumps(cache.get(key))
                    error = None
                except Exception as e:
                    got = None
                    error = type(e).__name__
                out['corrupt'].append({'key': key, 'raw': raw, 'get': got, 'error': error, 'log': list(handler.lines)})
            cache._conn.execute('INSERT OR REPLACE INTO cache (key, value) VALUES (?, ?)', ('nullvalue', None))
            cache._conn.commit()
            try:
                cache.get('nullvalue')
                null_error = None
            except Exception as e:
                null_error = type(e).__name__
            out['null_value_error'] = null_error
            cache.close()
            cache.close()  # a second close does not raise
            try:
                cache.get('k')
                out['get_after_close'] = None
            except Exception as e:
                out['get_after_close'] = type(e).__name__
            cache.__del__()
            # A new directory, nested. The files that the constructor makes.
            nested = os.path.join(tmp, 'nested', 'dir', 'cache')
            c = LLMCache(nested)
            out['creates'] = [os.path.isdir(nested), os.path.isfile(os.path.join(nested, 'cache.db'))]
            c.close()
            # Persistence across two instances.
            c1 = LLMCache(os.path.join(tmp, 'persist'))
            c1.set('persist_key', {'data': 'survives'})
            c1.close()
            c2 = LLMCache(os.path.join(tmp, 'persist'))
            out['persist'] = json.dumps(c2.get('persist_key'))
            c2.close()
    finally:
        logger.removeHandler(handler)
    return out


TOKENS = ['True', ' true', 'False', 'false', 'TRUE', 'true ', '  True  ', 'True answer', 'true story', 'truex', 'tru', '',
          ' ', '\ttrue', 'true\nfalse', 'Yes', 'no', 'FALSE', ' true', 'ＴＲＵＥ', ' True', 'True.', 'trUe', 'ſtrue',
          ' true', 'true x', '\U0001F600', 'é']


def reply(top):
    return {'kind': 'tokens', 'top': top}


def record_reranker(module) -> dict:
    import httpx
    import openai

    class Handler(logging.Handler):
        def __init__(self) -> None:
            super().__init__(logging.DEBUG)
            self.lines: list = []

        def emit(self, record) -> None:
            self.lines.append([record.levelname, record.getMessage()])

    def make_response(script):
        kind = script['kind']
        if kind == 'tokens':
            top = [types.SimpleNamespace(token=t, logprob=lp) for t, lp in script['top']]
            content = [types.SimpleNamespace(top_logprobs=top)]
            return types.SimpleNamespace(choices=[types.SimpleNamespace(logprobs=types.SimpleNamespace(content=content))])
        if kind == 'no_logprobs':
            return types.SimpleNamespace(choices=[types.SimpleNamespace(logprobs=None)])
        if kind == 'content_none':
            return types.SimpleNamespace(choices=[types.SimpleNamespace(logprobs=types.SimpleNamespace(content=None))])
        if kind == 'empty_content':
            return types.SimpleNamespace(choices=[types.SimpleNamespace(logprobs=types.SimpleNamespace(content=[]))])
        if kind == 'empty_top':
            content = [types.SimpleNamespace(top_logprobs=[])]
            return types.SimpleNamespace(choices=[types.SimpleNamespace(logprobs=types.SimpleNamespace(content=content))])
        if kind == 'no_choices':
            return types.SimpleNamespace(choices=[])
        raise AssertionError(kind)

    def run(case) -> dict:
        calls: list = []
        scripts = list(case['replies'])

        async def create(**kwargs):
            n = len(calls)
            calls.append({
                'model': kwargs['model'],
                'messages': [[m.role, m.content] for m in kwargs['messages']],
                'temperature': kwargs['temperature'],
                'max_tokens': kwargs['max_tokens'],
                'logit_bias': [[k, v] for k, v in kwargs['logit_bias'].items()],
                'logprobs': kwargs['logprobs'],
                'top_logprobs': kwargs['top_logprobs'],
                'kwargs': sorted(kwargs),
            })
            script = scripts[n]
            if script['kind'] == 'rate_limit':
                response = httpx.Response(429, request=httpx.Request('POST', 'http://localhost/v1/chat/completions'))
                raise openai.RateLimitError('rate limited', response=response, body=None)
            if script['kind'] == 'runtime_error':
                raise RuntimeError(script['message'])
            if script['kind'] == 'cancel':
                raise asyncio.CancelledError()
            return make_response(script)

        client = types.SimpleNamespace(chat=types.SimpleNamespace(completions=types.SimpleNamespace(create=create)))
        config = module.LLMConfig(model=case['model']) if 'model' in case else None
        reranker = module.OpenAIRerankerClient(config=config, client=client)
        handler = Handler()
        logger = logging.getLogger(module.__name__)
        logger.setLevel(logging.DEBUG)
        logger.addHandler(handler)
        try:
            result = asyncio.run(reranker.rank(case['query'], case['passages']))
            error = None
        except BaseException as e:  # noqa: BLE001
            result = None
            error = {'type': type(e).__name__, 'text': str(e), 'cause': type(e.__cause__).__name__ if e.__cause__ else None}
        finally:
            logger.removeHandler(handler)
        return {
            'case': case,
            'calls': calls,
            'result': None if result is None else [[p, s] for p, s in result],
            'result_types': None if result is None else [type(s).__name__ for _, s in result],
            'error': error,
            'log': handler.lines,
        }

    cases: list = []

    def add(**kw):
        cases.append(kw)

    # The score of each first token, one passage each, at a fixed log probability.
    for token in TOKENS:
        add(name='token', query='q', passages=['p'], replies=[reply([[token, -0.25], ['other', -3.0]])], model='m')
    for lp in [0.0, -1e-9, -0.0001, -0.3, -0.6931471805599453, -1.0, -2.5, -10.0, -50.0, -745.0, -800.0, -1e-300]:
        add(name='logprob', query='q', passages=['p', 'r'],
            replies=[reply([['True', lp]]), reply([['False', lp]])], model='m')
    # Order of the result: ties keep the input order, also for reverse=True.
    add(name='ties', query='q', passages=['a', 'b', 'c', 'd', 'e'],
        replies=[reply([['True', -0.5]]), reply([['False', -0.5]]), reply([['True', -0.5]]), reply([['True', -0.1]]),
                 reply([['False', -2.0]])], model='m')
    add(name='all_equal', query='q', passages=['a', 'b', 'c', 'd'], replies=[reply([['True', -1.0]])] * 4, model='m')
    # The model: given, empty, None, and no config.
    add(name='model_given', query='q', passages=['p'], replies=[reply([['True', -1.0]])], model='gpt-x')
    add(name='model_empty', query='q', passages=['p'], replies=[reply([['True', -1.0]])], model='')
    add(name='model_none', query='q', passages=['p'], replies=[reply([['True', -1.0]])], model=None)
    add(name='no_config', query='q', passages=['p'], replies=[reply([['True', -1.0]])])
    # Messages: the text of the user message for odd queries and passages.
    odd = ['', 'plain', 'with {braces} and %s', 'line1\nline2', '  lead and trail  ', 'é 日本語', '\U0001F600', '"quoted"', "it's",
           '</PASSAGE>', 'a\r\nb', '\t', 'x' * 300]
    for i, text in enumerate(odd):
        add(name='messages', query=odd[(i + 3) % len(odd)], passages=[text, odd[(i + 1) % len(odd)]],
            replies=[reply([['True', -0.2]]), reply([['False', -0.4]])], model='m')
    # An empty passage list: no call, an empty result.
    add(name='empty_passages', query='q', passages=[], replies=[], model='m')
    # Missing logprobs and content: a score is skipped, and zip(strict=True) fails at the end.
    for kind in ['no_logprobs', 'content_none', 'empty_top']:
        add(name=f'skip_{kind}', query='q', passages=['a', 'b', 'c'],
            replies=[reply([['True', -0.5]]), {'kind': kind}, reply([['False', -0.5]])], model='m')
        add(name=f'skip_{kind}_all', query='q', passages=['a'], replies=[{'kind': kind}], model='m')
    # Content of length 0 is not None: content[0] raises IndexError before the zip.
    add(name='empty_content', query='q', passages=['a', 'b'], replies=[reply([['True', -0.5]]), {'kind': 'empty_content'}],
        model='m')
    add(name='no_choices', query='q', passages=['a'], replies=[{'kind': 'no_choices'}], model='m')
    # Failures of a call.
    add(name='rate_limit', query='q', passages=['a', 'b'], replies=[reply([['True', -0.5]]), {'kind': 'rate_limit'}], model='m')
    add(name='rate_limit_first', query='q', passages=['a', 'b', 'c'],
        replies=[{'kind': 'rate_limit'}, {'kind': 'runtime_error', 'message': 'later'}, reply([['True', -0.5]])], model='m')
    add(name='runtime_error', query='q', passages=['a', 'b'],
        replies=[reply([['True', -0.5]]), {'kind': 'runtime_error', 'message': 'boom'}], model='m')
    add(name='runtime_error_text', query='q', passages=['a'], replies=[{'kind': 'runtime_error', 'message': 'é {x} %s\nline'}],
        model='m')
    add(name='runtime_error_empty', query='q', passages=['a'], replies=[{'kind': 'runtime_error', 'message': ''}], model='m')
    # Generated batches from the fixed seed.
    for n in range(40):
        count = rng.randint(1, 12)
        passages = [rng.choice(odd) + str(i) for i in range(count)]
        replies = []
        for _ in range(count):
            token = rng.choice(TOKENS)
            lp = rng.choice([0.0, -0.1, -0.5, -0.5, -1.0, -2.0, rng.uniform(-8.0, 0.0), -rng.random() * 1e-3, rng.uniform(-30, 0)])
            top = [[token, lp]] + [[rng.choice(TOKENS), rng.uniform(-10, 0)] for _ in range(rng.randint(0, 1))]
            replies.append(reply(top))
        add(name='random', query=rng.choice(odd), passages=passages, replies=replies, model=rng.choice(['m', None, '', 'gpt-4.1']))

    recorded = []
    for case in cases:
        recorded.append(run(case))
    return {'default_model': module.DEFAULT_MODEL, 'cases': recorded}


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core.cross_encoder import openai_reranker_client
    from graphiti_core.llm_client import cache

    data = {
        'python': '.'.join(str(p) for p in sys.version_info[:3]),
        'sqlite': sqlite3.sqlite_version,
        'source': 'graphiti_core/{llm_client/cache,cross_encoder/openai_reranker_client}.py',
        'cache': record_cache(cache.LLMCache, cache.__name__),
        'reranker': record_reranker(openai_reranker_client),
    }
    text = json.dumps(data, ensure_ascii=True, allow_nan=True, separators=(',', ':')) + '\n'
    count = (len(data['cache']['sets']) + len(data['cache']['bad']) + len(data['cache']['corrupt'])
             + len(data['reranker']['cases']))
    if '--check' in sys.argv[1:]:
        if not OUT.exists() or OUT.read_text(encoding='utf-8') != text:
            print(f'{OUT.relative_to(ROOT)} is not current. Run this tool with no flag.', file=sys.stderr)
            return 1
        print(f'{OUT.relative_to(ROOT)}: current, {count} cases')
        return 0
    OUT.parent.mkdir(parents=True, exist_ok=True)
    OUT.write_text(text, encoding='utf-8')
    print(f'{OUT.relative_to(ROOT)}: {count} cases, {len(text.encode("utf-8"))} bytes')
    return 0


if __name__ == '__main__':
    sys.exit(main())
