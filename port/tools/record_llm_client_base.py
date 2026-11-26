"""Records results of the base modules of the model client from real Python, for the evaluation tests.

The modules are graphiti_core/llm_client/config.py, errors.py, utils.py, and token_tracker.py. The judge has no
case for them, and the upstream tests check a few values each. This file gives the evaluation tests generated
inputs from a fixed seed (port/RULES.md, R23 and R25), so that the port can be compared with real results:

- config: each module-level constant, the members of ModelSize, and the six attributes of LLMConfig for
  generated arguments.
- errors: message and str() of the three exception classes for generated messages (gap J1).
- token_tracker: the properties of the two dataclasses, and for generated series of record() calls the usage,
  the totals, the text that print_summary writes for each sort key, and the state after reset() (gap J8).
- utils: what generate_embedding passes to the embedder, what it returns, and its debug message.

llm_client/cache.py has no recording: the port has no SQLite package yet (the unit keeps its TODO(port) marker).

    .upstream/graphiti/.venv/bin/python port/tools/record_llm_client_base.py
    .upstream/graphiti/.venv/bin/python port/tools/record_llm_client_base.py --check

It writes tests/Port.Evaluation/values/llm_client_base.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The tool needs Python 3.12 and the pinned upstream. The environment
variable GRAPHITI_UPSTREAM names the upstream checkout, and it defaults to .upstream/graphiti in the
repository. The tool changes nothing there. Nothing here uses the network.

The recording leaves out the elapsed time in the debug message of generate_embedding, which changes at each
run. It keeps the text around it, and the port test checks that the time is a float text.
"""
from __future__ import annotations

import asyncio
import contextlib
import io
import json
import logging
import os
import random
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'llm_client_base.json'

STRINGS = ['', 'a', 'gpt-6-luna', 'gpt-4.1-mini', 'sk-test', 'https://api.openai.com', 'http://localhost:8080/v1',
           'é', '日本語', 'two words', 'line\nbreak', "it's", '"quoted"', 'tab\there', '\U0001F600', 'x' * 200]
MESSAGES = ['', 'x', 'The LLM refused to respond to this prompt.', 'The LLM returned an empty response.',
            'Rate limit exceeded. Please try again later.', 'é', '日本語のメッセージ', 'a\nb', "it's", '"q"', '\U0001F600',
            '{braces} and %s and {0}', 'x' * 500, ' ', ' ']
PROMPTS = ['extract_nodes.extract_message', 'dedupe_nodes', 'extract_edges', 'unknown', 'a', 'b', 'B', 'é', '日本',
           'x' * 44, 'x' * 45, 'x' * 46, 'x' * 80, '', None, 'p with space', '\U0001F600 emoji', 'Z', 'z',
           'summarize_nodes.summarize_pair']
SORT_KEYS = ['total_tokens', 'input_tokens', 'output_tokens', 'call_count', 'prompt_name', 'bogus', '', 'TOTAL_TOKENS',
             'avg_input_tokens']

rng = random.Random(20261127)


def number(rng_: random.Random) -> int:
    r = rng_.random()
    if r < 0.15:
        return 0
    if r < 0.5:
        return rng_.randint(1, 999)
    if r < 0.75:
        return rng_.randint(1000, 1_000_000)
    if r < 0.9:
        return rng_.randint(10**6, 10**11)
    return rng_.randint(10**11, 10**15)


def record_config(config) -> dict:
    out = {
        'constants': {
            'DEFAULT_MAX_TOKENS': config.DEFAULT_MAX_TOKENS,
            'DEFAULT_TEMPERATURE': config.DEFAULT_TEMPERATURE,
        },
        'model_size': [[m.name, m.value] for m in config.ModelSize],
        'cases': [],
    }

    def attrs(c) -> dict:
        return {
            'base_url': c.base_url,
            'api_key': c.api_key,
            'model': c.model,
            'small_model': c.small_model,
            'temperature': c.temperature,
            'temperature_type': type(c.temperature).__name__,
            'max_tokens': c.max_tokens,
            'max_tokens_type': type(c.max_tokens).__name__,
        }

    out['cases'].append({'kwargs': {}, 'attrs': attrs(config.LLMConfig())})
    for _ in range(60):
        kwargs: dict = {}
        for name in ['api_key', 'model', 'base_url', 'small_model']:
            if rng.random() < 0.6:
                kwargs[name] = rng.choice(STRINGS + [None])
        if rng.random() < 0.6:
            kwargs['temperature'] = rng.choice([0, 1, 2, 0.0, 0.2, 0.5, 0.7, 1.0, 1.5, -1, 1e-7, 1e22, 0.1 + 0.2])
        if rng.random() < 0.6:
            kwargs['max_tokens'] = rng.choice([0, 1, 100, 4096, 8192, 16384, 32768, 2**31, 2**40, -5])
        out['cases'].append({'kwargs': kwargs, 'attrs': attrs(config.LLMConfig(**kwargs))})
    return out


def record_errors(errors) -> list:
    cases = []
    for name in ['RateLimitError', 'RefusalError', 'EmptyResponseError']:
        cls = getattr(errors, name)
        variants: list[list] = [[m] for m in MESSAGES]
        if name == 'RateLimitError':
            variants.insert(0, [])
        for args in variants:
            e = cls(*args)
            cases.append({
                'class': name,
                'args': args,
                'message': e.message,
                'str': str(e),
                'is_exception': isinstance(e, Exception),
                'base': [b.__name__ for b in cls.__mro__[1:-1]],
            })
        if name != 'RateLimitError':
            try:
                cls()
            except TypeError as exc:
                cases.append({'class': name, 'args': None, 'type_error': str(exc)})
    return cases


def record_token_tracker(tt) -> dict:
    out: dict = {'dataclasses': [], 'series': []}
    for _ in range(40):
        calls = rng.choice([0, 1, 2, 3, 7, 1000])
        i, o = number(rng), number(rng)
        p = tt.PromptTokenUsage(prompt_name='p', call_count=calls, total_input_tokens=i, total_output_tokens=o)
        u = tt.TokenUsage(input_tokens=i, output_tokens=o)
        out['dataclasses'].append({
            'call_count': calls, 'input': i, 'output': o,
            'prompt_total': p.total_tokens, 'avg_in': p.avg_input_tokens, 'avg_out': p.avg_output_tokens,
            'avg_in_type': type(p.avg_input_tokens).__name__, 'avg_out_type': type(p.avg_output_tokens).__name__,
            'usage_total': u.total_tokens,
        })

    def prompt_usage_row(k, v) -> list:
        return [k, v.prompt_name, v.call_count, v.total_input_tokens, v.total_output_tokens, v.total_tokens,
                v.avg_input_tokens, v.avg_output_tokens]

    def state(tracker, all_keys: bool = False) -> dict:
        usage = tracker.get_usage()
        total = tracker.get_total_usage()
        summaries = {}
        for key in SORT_KEYS if all_keys else []:
            buf = io.StringIO()
            with contextlib.redirect_stdout(buf):
                tracker.print_summary(key)
            summaries[key] = buf.getvalue()
        buf = io.StringIO()
        with contextlib.redirect_stdout(buf):
            tracker.print_summary()
        return {
            'usage': [prompt_usage_row(k, v) for k, v in usage.items()],
            'total': [total.input_tokens, total.output_tokens, total.total_tokens],
            'summaries': summaries,
            'default_summary': buf.getvalue(),
        }

    for n in [0, 1, 2, 3, 5, 8, 13, 21, 40, 80, 150] * 2:
        tracker = tt.TokenUsageTracker()
        calls = []
        pool = rng.sample(PROMPTS, rng.randint(1, len(PROMPTS)))
        for _ in range(n):
            name = rng.choice(pool)
            i, o = number(rng), number(rng)
            if rng.random() < 0.2:
                i, o = rng.choice([0, 100]), rng.choice([0, 50])  # equal counts give ties in the sort
            calls.append([name, i, o])
            tracker.record(name, i, o)
        case = {'calls': calls, 'after_record': state(tracker, all_keys=True)}
        copy = tracker.get_usage()
        for v in copy.values():
            v.total_input_tokens = 9999  # a change of the copy must not reach the tracker
            v.call_count = 0
        case['after_copy_edit'] = state(tracker)
        tracker.reset()
        case['after_reset'] = state(tracker)
        tracker.record('after_reset', 3, 4)
        case['after_reset_and_record'] = state(tracker)
        out['series'].append(case)
    return out


def record_utils(utils, EmbedderClient) -> list:
    class Handler(logging.Handler):
        def __init__(self) -> None:
            super().__init__(logging.DEBUG)
            self.lines: list[tuple[str, str]] = []

        def emit(self, record) -> None:
            self.lines.append((record.levelname, record.getMessage()))

    class Fake(EmbedderClient):
        def __init__(self) -> None:
            self.calls: list = []

        async def create(self, input_data):
            self.calls.append(input_data)
            return [float(len(input_data[0])), 0.5, -1.25]

    texts = ['', 'hello', 'line1\nline2', '\n', '\n\n\n', 'a\r\nb', '\r', 'é\nü', '日本\n語', '\U0001F600\n\U0001F600',
             'x' * 1000, ' \n ', 'tab\t\nx', 'already clean', 'trailing\n', '\nleading']
    cases = []
    logger = logging.getLogger('graphiti_core.llm_client.utils')
    logger.setLevel(logging.DEBUG)
    for text in texts:
        embedder = Fake()
        handler = Handler()
        logger.addHandler(handler)
        try:
            result = asyncio.run(utils.generate_embedding(embedder, text))
        finally:
            logger.removeHandler(handler)
        assert len(handler.lines) == 1
        level, message = handler.lines[0]
        marker = ' in '
        head, _, tail = message.rpartition(marker)
        assert tail.endswith(' ms')
        float(tail[:-3])  # the elapsed time is a float text
        cases.append({
            'text': text,
            'input_data': embedder.calls,
            'result': result,
            'log_level': level,
            'log_head': head,
            'log_tail': ' ms',
        })
    return cases


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    from graphiti_core.embedder.client import EmbedderClient
    from graphiti_core.llm_client import config, errors, token_tracker, utils

    data = {
        'python': '.'.join(str(p) for p in sys.version_info[:3]),
        'source': 'graphiti_core/llm_client/{config,errors,token_tracker,utils}.py',
        'config': record_config(config),
        'errors': record_errors(errors),
        'token_tracker': record_token_tracker(token_tracker),
        'utils': record_utils(utils, EmbedderClient),
    }
    # allow_nan keeps inf and nan in the file as the tokens that PyJson.Loads reads.
    text = json.dumps(data, ensure_ascii=True, allow_nan=True, separators=(',', ':')) + '\n'
    count = (len(data['config']['cases']) + len(data['errors']) + len(data['token_tracker']['series'])
             + len(data['token_tracker']['dataclasses']) + len(data['utils']))
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
