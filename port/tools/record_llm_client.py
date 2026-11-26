"""Records results of llm_client/client.py, embedder/client.py, and cross_encoder/client.py from real Python.

The judge has no case for these modules, and the upstream test of the first one (tests/llm_client/test_client.py)
checks a few values. This file gives the evaluation tests generated inputs from a fixed seed (port/RULES.md, R23 and
R25), so that the port can be compared with real results:

- constants: DEFAULT_CACHE_DIR, EMBEDDING_DIM, EmbedderConfig().embedding_dim, the text of
  get_extraction_language_instruction for several group ids, and what EmbedderClient.create_batch raises.
- is_server_or_retry_error for each exception type that matters (the retried ones, and those that are not).
- _clean_input for generated text with control characters, zero-width characters, non-ASCII text, and lone surrogates.
- _apply_attribute_extraction_preamble for generated message lists, with the call run two times (it is idempotent).
- _get_cache_key (gap J4) for generated messages and models.
- _get_provider_type for class names, and _get_failed_generation_log for generated roles and outputs.
- generate_response with the cache off: each message list that reaches _generate_response, with its other arguments,
  and the span events (name, attributes, status, exception), for generated inputs and for failing calls.
- generate_response with the cache on (the cache path): a miss, a hit, the key with model=, and a reply that JSON cannot
  hold, with the span events, the result, the call count, the stored rows, and the log lines of each call.
- generate_response with a cancel: the cancel arrives while an attempt fails with a retried error (at attempt 1, 2, 3, and
  4), and a task that raises CancelledError itself. The exception type, the attempts, the log lines, and the span calls.
- the attributes that LLMClient.__init__ sets, for each LLMConfig variant and for no config, and with cache=True.
- _generate_response_with_retry (gap J3): for a call that passes, for each retried error type one to four times, and
  for errors that are not retried: the number of attempts, each wait (the arguments of random.uniform, and the sleeps),
  the log lines, and the result or the exception that leaves.

    GRAPHITI_UPSTREAM=.upstream/graphiti .upstream/graphiti/.venv/bin/python -B port/tools/record_llm_client.py
    GRAPHITI_UPSTREAM=.upstream/graphiti .upstream/graphiti/.venv/bin/python -B port/tools/record_llm_client.py --check

It writes tests/Port.Evaluation/values/llm_client.json. --check writes nothing and exits 1 if the file differs from
what the run would write. The tool needs Python 3.12 and the pinned upstream. The environment variable
GRAPHITI_UPSTREAM names the upstream checkout, and it defaults to .upstream/graphiti in the repository. The tool
changes nothing there. Nothing here uses the network. The cache path runs on a SQLite file in a temporary directory.

The recording replaces asyncio.sleep and random.uniform while it runs the retry. The replacement of random.uniform
returns a + (b - a) * r for a scripted r, which is what the real function returns for that r, and it records a and b.
"""
from __future__ import annotations

import asyncio
import contextlib
import json
import logging
import os
import random
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'llm_client.json'

rng = random.Random(20261127)

LOGGER_NAME = 'graphiti_core.llm_client.client'

CHARS = (list('abcXYZ019 ,.;:-_\'"{}[]') + ['\n', '\r', '\t', '\x00', '\x01', '\x04', '\x08', '\x0b', '\x0c', '\x0e', '\x1f',
         '\x7f', '\x80', '\x9f', '\u00a0', '\u200b', '\u200c', '\u200d', '\ufeff', '\u2060', '\u2028', 'é', 'ü', 'ß', 'İ',
         '日', '本', '\U0001F600', '\U0001F9D1', '\ud800', '\udcde', '\udfff', '\ufffd', '\uffff'])


def random_text(max_len: int = 40) -> str:
    # A lone high surrogate followed by a lone low one is two code points in Python and one code point in UTF-16
    # (R12). A text that holds such a pair cannot be told apart in C#, so the generator leaves it out.
    while True:
        text = ''.join(rng.choice(CHARS) for _ in range(rng.randint(0, max_len)))
        if not re.search('[\ud800-\udbff][\udc00-\udfff]', text):
            return text


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    sys.path.insert(0, str(UPSTREAM))
    os.environ.pop('EMBEDDING_DIM', None)
    logging.getLogger(LOGGER_NAME).addHandler(logging.NullHandler())  # no output from the last-resort handler
    import httpx

    from graphiti_core.cross_encoder import client as cross_encoder_client
    from graphiti_core.embedder import client as embedder_client
    from graphiti_core.llm_client import client as llm_client
    from graphiti_core.llm_client.config import DEFAULT_MAX_TOKENS, LLMConfig, ModelSize
    from graphiti_core.llm_client.errors import EmptyResponseError, RateLimitError, RefusalError
    from graphiti_core.prompts import dedupe_nodes
    from graphiti_core.prompts.models import Message
    from graphiti_core.tracer import Tracer

    # The cases of the base class are run on a subclass of it, as the upstream test does.
    def make_class(name: str):
        async def _generate_response(self, messages, response_model=None):
            return {'content': 'test'}

        return type(name, (llm_client.LLMClient,), {'_generate_response': _generate_response})

    MockLLMClient = make_class('MockLLMClient')

    data = {
        'python': '.'.join(str(p) for p in sys.version_info[:3]),
        'source': 'graphiti_core/{llm_client,embedder,cross_encoder}/client.py',
        'constants': record_constants(llm_client, embedder_client, cross_encoder_client),
        'is_server_or_retry_error': record_retry_predicate(llm_client, httpx, RateLimitError, EmptyResponseError, RefusalError),
        'clean_input': record_clean_input(MockLLMClient, LLMConfig),
        'preamble': record_preamble(MockLLMClient, LLMConfig, Message),
        'cache_key': record_cache_key(make_class('MockLLMClient'), LLMConfig, Message),
        'provider_type': record_provider_type(make_class, LLMConfig),
        'failed_generation_log': record_failed_generation_log(MockLLMClient, LLMConfig, Message),
        'generate_response': record_generate_response(
            llm_client, make_class, LLMConfig, ModelSize, Message, Tracer, dedupe_nodes, DEFAULT_MAX_TOKENS,
            RateLimitError, RefusalError),
        'retry': record_retry(llm_client, httpx, LLMConfig, Message, RateLimitError, EmptyResponseError, RefusalError),
        'cache_path': record_cache_path(make_class, LLMConfig, ModelSize, Message, Tracer),
        'cancel': record_cancel(make_class, LLMConfig, ModelSize, Message, Tracer, RateLimitError),
        'client_init': record_client_init(make_class, LLMConfig),
    }
    # allow_nan keeps inf and nan in the file as the tokens that PyJson.Loads reads.
    text = json.dumps(data, ensure_ascii=True, allow_nan=True, separators=(',', ':')) + '\n'
    count = (sum(len(v) for k, v in data.items() if isinstance(v, list)) + len(data['retry']['cases'])
             + len(data['client_init']['cases']))
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


def record_constants(llm_client, embedder_client, cross_encoder_client) -> dict:
    class Fake(embedder_client.EmbedderClient):
        async def create(self, input_data):
            return [0.5]

    error = None
    try:
        asyncio.run(Fake().create_batch(['a']))
    except NotImplementedError as e:
        error = {'type': type(e).__name__, 'str': str(e)}
    abstract_failures = []
    for cls in (embedder_client.EmbedderClient, cross_encoder_client.CrossEncoderClient):
        try:
            cls()
        except TypeError:
            abstract_failures.append(cls.__name__)
    return {
        'DEFAULT_CACHE_DIR': llm_client.DEFAULT_CACHE_DIR,
        'EMBEDDING_DIM': embedder_client.EMBEDDING_DIM,
        'embedding_dim_of_default_config': embedder_client.EmbedderConfig().embedding_dim,
        'embedder_config_frozen_field': embedder_client.EmbedderConfig.model_fields['embedding_dim'].frozen,
        'create_batch_error': error,
        'abstract_classes_refuse_instances': abstract_failures,
        'language_instruction': [
            [group_id, llm_client.get_extraction_language_instruction(group_id)]
            for group_id in [None, 'g1', '', 'é', 'x' * 50]
        ],
        'language_instruction_default': llm_client.get_extraction_language_instruction(),
    }


def make_error(spec: dict, httpx, classes: dict) -> Exception:
    """The exception that a spec names. `message` is the first argument of the constructor."""
    kind = spec['kind']
    message = spec.get('message', 'm')
    if kind == 'HTTPStatusError':
        request = httpx.Request('POST', 'http://localhost/v1/chat/completions')
        return httpx.HTTPStatusError(message, request=request, response=httpx.Response(spec['status'], request=request))
    if kind == 'JSONDecodeError':
        return json.JSONDecodeError(message, '', 0)
    if kind == 'TransportError':
        return httpx.TransportError(message)
    if kind == 'ReadTimeout':
        return httpx.ReadTimeout(message)
    if kind == 'ConnectError':
        return httpx.ConnectError(message)
    if kind in classes:
        return classes[kind](message)
    return {'ValueError': ValueError, 'KeyError': KeyError, 'TimeoutError': TimeoutError, 'Exception': Exception,
            'RuntimeError': RuntimeError, 'OSError': OSError}[kind](message)


def record_retry_predicate(llm_client, httpx, RateLimitError, EmptyResponseError, RefusalError) -> list:
    classes = {'RateLimitError': RateLimitError, 'EmptyResponseError': EmptyResponseError, 'RefusalError': RefusalError}
    specs = [{'kind': k} for k in ['RateLimitError', 'EmptyResponseError', 'RefusalError', 'JSONDecodeError',
                                   'TransportError', 'ReadTimeout', 'ConnectError', 'ValueError', 'KeyError',
                                   'TimeoutError', 'Exception', 'RuntimeError', 'OSError']]
    specs += [{'kind': 'HTTPStatusError', 'status': s} for s in [100, 200, 301, 400, 401, 404, 408, 429, 499, 500, 501,
                                                                   502, 503, 504, 529, 599, 600, 700]]
    out = []
    for spec in specs:
        spec = {**spec, 'message': 'boom'}
        out.append({'spec': spec, 'result': llm_client.is_server_or_retry_error(make_error(spec, httpx, classes))})
    return out


def record_clean_input(MockLLMClient, LLMConfig) -> list:
    client = MockLLMClient(LLMConfig())
    inputs = [
        'Hello World', 'Hello\x00World', 'Hello\nWorld\tTest\r', 'Hello\udcdeWorld', 'Hello\u200bWorld', 'Test\ufeffWord',
        'Hello\x00\u200b\nWorld\udcde', '', '{"edges":[{"relation_typ...\f\x04Hn\\?"}]}', 'Hello\x0cWorld',
        'Hello\x04World', '{"test": "value\f\x00\x04"}', '\U0001F600\ud83d', '\ud83d\U0001F600', '😀',
        '\x7f\x80\x9f', '\u2028\u2029', '\u2060\u2060', ' ', '\x1f\x20',
    ]
    inputs += [random_text() for _ in range(300)]
    return [{'input': text, 'output': client._clean_input(text)} for text in inputs]


def record_preamble(MockLLMClient, LLMConfig, Message) -> list:
    client = MockLLMClient(LLMConfig())
    contents = ['', 'base', 'You are helpful.', 'hi', 'é日本語', '\n', ' padded ', '<<graphiti.attr_extraction.preamble.v1>>x',
                'a <<graphiti.attr_extraction.preamble.v1>> b', 'ATTRIBUTE EXTRACTION: mentioned', 'line1\nline2']
    cases = []
    for _ in range(80):
        n = rng.choice([0, 1, 1, 2, 3])
        spec = [[rng.choice(['system', 'user', 'assistant', 'tool']), rng.choice(contents)] for _ in range(n)]
        if n and rng.random() < 0.5:
            spec[0][0] = 'system'
        for attribute_extraction in (False, True):
            messages = [Message(role=r, content=c) for r, c in spec]
            client._apply_attribute_extraction_preamble(messages, attribute_extraction=attribute_extraction)
            once = [[m.role, m.content] for m in messages]
            client._apply_attribute_extraction_preamble(messages, attribute_extraction=attribute_extraction)
            twice = [[m.role, m.content] for m in messages]
            cases.append({'messages': spec, 'attribute_extraction': attribute_extraction, 'once': once, 'twice': twice})
    return cases


def record_cache_key(MockLLMClient, LLMConfig, Message) -> list:
    contents = ['', 'hi', 'é日本語', '\U0001F600', 'quote " and \\ and \n', 'x' * 120, '\u2028', 'tab\t']
    models = [None, '', 'gpt-4.1-mini', 'é', '日本', 'x' * 60]
    cases = []
    for _ in range(80):
        config_model = rng.choice(models)
        client = MockLLMClient(LLMConfig(model=config_model))
        spec = [[rng.choice(['system', 'user', 'assistant']), rng.choice(contents)] for _ in range(rng.randint(0, 4))]
        model = rng.choice(models)
        messages = [Message(role=r, content=c) for r, c in spec]
        cases.append({'config_model': config_model, 'messages': spec, 'model': model,
                      'key': client._get_cache_key(messages, model=model)})
    # The call with no model argument.
    client = MockLLMClient(LLMConfig(model='m1'))
    cases.append({'config_model': 'm1', 'messages': [['user', 'hi']], 'model': None,
                  'key': client._get_cache_key([Message(role='user', content='hi')])})
    return cases


def record_provider_type(make_class, LLMConfig) -> list:
    names = ['MockLLMClient', 'OpenAIClient', 'OpenAIGenericClient', 'AzureOpenAILLMClient', 'OpenAIDecisionsClient',
             'AnthropicClient', 'GeminiClient', 'GroqClient', 'GLiNER2Client', 'JevClient', 'LLMClient', 'OPENAI',
             'openai', 'ANTHROPIC', 'Gemini', 'GROQ', 'OpenAIAnthropic', 'AnthropicGemini', 'GeminiGroq', 'GroqOpenAI',
             'Ünïcode', 'İOPENAI', 'ǅroq', 'ChatClient', '']
    out = []
    for name in names:
        client = make_class(name)(LLMConfig())
        out.append({'class_name': name, 'provider': client._get_provider_type()})
    return out


def record_failed_generation_log(MockLLMClient, LLMConfig, Message) -> list:
    client = MockLLMClient(LLMConfig())
    roles = ['system', 'user', 'assistant', "it's", '"q"', 'é', '\U0001F600', 'a\nb', '']
    outputs = [None, '', 'x', 'raw output', 'x' * 499, 'x' * 500, 'x' * 501, 'x' * 1000, 'é' * 500, 'é' * 501,
               '\U0001F600' * 499, '\U0001F600' * 500, '\U0001F600' * 501, 'a\U0001F600' * 300, 'line\nbreak', '\udcde' * 501,
               '日本語' * 200]
    cases = []
    for output in outputs:
        for n in (0, 1, 3):
            spec = [rng.choice(roles) for _ in range(n)]
            messages = [Message(role=r, content='c') for r in spec]
            cases.append({'roles': spec, 'output': output, 'log': client._get_failed_generation_log(messages, output)})
    return cases


def record_generate_response(llm_client, make_class, LLMConfig, ModelSize, Message, Tracer, dedupe_nodes,
                             default_max_tokens, RateLimitError, RefusalError) -> list:
    events: list = []

    class Span:
        def add_attributes(self, attributes):
            events.append(['add_attributes', dict(attributes)])

        def set_status(self, status, description=None):
            events.append(['set_status', status, description])

        def record_exception(self, exception):
            events.append(['record_exception', str(exception)])

    class RecordingTracer(Tracer):
        @contextlib.contextmanager
        def start_span(self, name):
            events.append(['start_span', name])
            yield Span()

    models = {'NodeDuplicate': dedupe_nodes.NodeDuplicate, 'NodeResolutions': dedupe_nodes.NodeResolutions}
    contents = ['', 'You are helpful.', 'hi', 'line1\nline2', 'tab\there', 'ctrl\x00\x04\x0c', 'zw\u200bx\ufeffy', 'é日本語',
                '\U0001F600 emoji', 'lone\udcdesurrogate', ' leading space', '\n\nstarts with newlines',
                '<<graphiti.attr_extraction.preamble.v1>>already', 'x' * 300, 'ünï\x1f', '{"a": 1}']
    class_names = ['MockLLMClient', 'OpenAIClient', 'AnthropicClient', 'GeminiClient', 'GroqClient', 'OpenAIGenericClient',
                   'SomethingElse']
    cases: list = []

    def run(spec: dict, behavior: dict | None = None) -> dict:
        received: list = []
        result_object = {'content': 'ok', 'n': len(spec['messages'])}

        async def _generate_response(self, messages, response_model=None, max_tokens=default_max_tokens,
                                     model_size=ModelSize.medium, **kwargs):
            received.append({
                'messages': [[m.role, m.content] for m in messages],
                'response_model': response_model.__name__ if response_model is not None else None,
                'max_tokens': max_tokens,
                'model_size': model_size.value,
                'kwargs': {k: v for k, v in kwargs.items()},
            })
            if behavior is not None:
                raise make_error(behavior, None, {'RateLimitError': RateLimitError, 'RefusalError': RefusalError})
            return result_object

        client_class = make_class(spec['class_name'])
        client_class._generate_response = _generate_response
        client = client_class(LLMConfig(model=spec['config_model'], max_tokens=spec['config_max_tokens']))
        events.clear()
        client.set_tracer(RecordingTracer())
        messages = [Message(role=r, content=c) for r, c in spec['messages']]
        kwargs: dict = {}
        if spec['response_model'] is not None:
            kwargs['response_model'] = models[spec['response_model']]
        if spec['max_tokens'] is not None:
            kwargs['max_tokens'] = spec['max_tokens']
        if spec['model'] is not None:
            kwargs['model'] = spec['model']
        outcome: dict
        try:
            result = asyncio.run(client.generate_response(
                messages, model_size=ModelSize(spec['model_size']), group_id=spec['group_id'],
                prompt_name=spec['prompt_name'], attribute_extraction=spec['attribute_extraction'], **kwargs))
            outcome = {'result': result, 'is_same_object': result is result_object}
        except Exception as e:  # noqa: BLE001
            outcome = {'error': type(e).__name__, 'message': str(e)}
        after = [[m.role, m.content] for m in messages]
        if received:
            assert after == received[-1]['messages']  # the caller's Message objects are the ones that were changed
        return {**spec, 'behavior': behavior, 'received': received, 'caller_messages_after': after,
                'events': [list(e) for e in events], 'outcome': outcome}

    def random_spec(**overrides) -> dict:
        n = rng.choice([1, 1, 2, 3, 4])
        messages = [[rng.choice(['system', 'user', 'assistant']), rng.choice(contents)] for _ in range(n)]
        if rng.random() < 0.6:
            messages[0][0] = 'system'
        spec = {
            'class_name': rng.choice(class_names),
            'config_model': rng.choice([None, 'gpt-4.1-mini', 'é']),
            'config_max_tokens': rng.choice([default_max_tokens, 100, 4096, 0]),
            'messages': messages,
            'response_model': rng.choice([None, None, 'NodeDuplicate', 'NodeResolutions']),
            'max_tokens': rng.choice([None, None, 0, 1, 500, 100000]),
            'model_size': rng.choice(['small', 'medium']),
            'group_id': rng.choice([None, 'g1', '', 'é-group']),
            'prompt_name': rng.choice([None, '', 'extract_nodes.extract_message', 'p']),
            'attribute_extraction': rng.choice([False, True]),
            'model': rng.choice([None, None, '', 'override-model', '日本']),
        }
        spec.update(overrides)
        return spec

    # Each combination of the small arguments, on a fixed message list.
    base_messages = [['system', 'You are helpful.'], ['user', 'hi']]
    for model_size in ('small', 'medium'):
        for response_model in (None, 'NodeDuplicate', 'NodeResolutions'):
            for group_id in (None, 'g1'):
                for attribute_extraction in (False, True):
                    cases.append(run(random_spec(messages=[list(m) for m in base_messages], model_size=model_size,
                                                 response_model=response_model, group_id=group_id,
                                                 attribute_extraction=attribute_extraction, prompt_name=None,
                                                 max_tokens=None, model=None)))
    for _ in range(120):
        cases.append(run(random_spec()))

    # Empty message lists raise IndexError, with and without a response model.
    for response_model in (None, 'NodeDuplicate'):
        for attribute_extraction in (False, True):
            cases.append(run(random_spec(messages=[], response_model=response_model,
                                         attribute_extraction=attribute_extraction)))

    # A failing call: the error is not retried, so the span gets the status and the exception, and it leaves.
    for behavior in ({'kind': 'ValueError', 'message': 'boom'}, {'kind': 'RefusalError', 'message': 'refused'},
                     {'kind': 'RuntimeError', 'message': ''}):
        cases.append(run(random_spec(), behavior))
    # A call that fails with a retried error four times: the waits are patched, and the last error leaves.
    with patched_retry_environment([], [], 0.5):
        cases.append(run(random_spec(), {'kind': 'RateLimitError', 'message': 'slow down'}))
    return cases


class SpanEvents:
    """A tracer that writes each call of the span to a list, as the recording of generate_response does."""

    def __init__(self, Tracer, events: list) -> None:
        class Span:
            def add_attributes(self, attributes):
                events.append(['add_attributes', dict(attributes)])

            def set_status(self, status, description=None):
                events.append(['set_status', status, description])

            def record_exception(self, exception):
                events.append(['record_exception', str(exception)])

        class RecordingTracer(Tracer):
            @contextlib.contextmanager
            def start_span(self, name):
                events.append(['start_span', name])
                yield Span()

        self.tracer = RecordingTracer()


class LineHandler(logging.Handler):
    def __init__(self) -> None:
        super().__init__(logging.DEBUG)
        self.lines: list = []

    def emit(self, record) -> None:
        self.lines.append([record.levelname, record.getMessage()])


def stored_rows(db_path: str) -> list:
    import sqlite3

    con = sqlite3.connect(db_path)
    try:
        return [list(r) for r in con.execute('SELECT key, value FROM cache ORDER BY key')]
    finally:
        con.close()


def record_cache_path(make_class, LLMConfig, ModelSize, Message, Tracer) -> list:
    """generate_response with cache_enabled and a cache_dir: each scenario is a list of calls on one client."""
    import tempfile

    from graphiti_core.llm_client.cache import LLMCache

    sys_hi = [['system', 'You are helpful.'], ['user', 'hi']]
    unicode_messages = [['system', 'é日本語 \U0001F600'], ['user', '']]
    scenarios = [
        # A miss and then a hit, with prompt_name and the model argument.
        {'name': 'miss_then_hit', 'calls': [
            {'messages': sys_hi, 'prompt_name': 'p', 'model': 'override', 'reply': 'dict'},
            {'messages': sys_hi, 'prompt_name': 'p', 'model': 'override', 'reply': 'dict'}]},
        # A miss, a hit, the key with model=, and a reply that JSON cannot hold.
        {'name': 'five_calls', 'calls': [
            {'messages': sys_hi, 'prompt_name': None, 'model': None, 'reply': 'dict'},
            {'messages': sys_hi, 'prompt_name': None, 'model': None, 'reply': 'dict'},
            {'messages': sys_hi, 'prompt_name': 'p', 'model': 'x', 'reply': 'dict'},
            {'messages': [['user', 'other']], 'prompt_name': 'p', 'model': None, 'reply': 'unserializable'},
            {'messages': [['user', 'other']], 'prompt_name': 'p', 'model': None, 'reply': 'dict'}]},
        {'name': 'unicode_and_empty', 'calls': [
            {'messages': unicode_messages, 'prompt_name': 'é', 'model': '', 'reply': 'dict'},
            {'messages': unicode_messages, 'prompt_name': 'é', 'model': '', 'reply': 'dict'},
            {'messages': unicode_messages, 'prompt_name': 'é', 'model': 'm2', 'reply': 'dict'}]},
    ]
    logger = logging.getLogger('graphiti_core.llm_client')
    logger.setLevel(logging.DEBUG)
    out = []
    for scenario in scenarios:
        with tempfile.TemporaryDirectory() as tmp:
            cache = LLMCache(tmp)
            received: list = []
            state = {'index': 0}

            async def _generate_response(self, messages, response_model=None, max_tokens=16384,
                                         model_size=ModelSize.medium, **kwargs):
                received.append([[m.role, m.content] for m in messages])
                spec = scenario['calls'][state['index']]
                if spec['reply'] == 'unserializable':
                    return {'content': {1, 2}}
                return {'content': f'reply {len(received)}'}

            cls = make_class('MockLLMClient')
            cls._generate_response = _generate_response
            client = cls(LLMConfig(model='m'))
            client.cache_enabled = True
            client.cache_dir = cache
            calls = []
            for index, spec in enumerate(scenario['calls']):
                state['index'] = index
                events: list = []
                client.set_tracer(SpanEvents(Tracer, events).tracer)
                handler = LineHandler()
                logger.addHandler(handler)
                before = len(received)
                kwargs = {'model': spec['model']} if spec['model'] is not None else {}
                messages = [Message(role=r, content=c) for r, c in spec['messages']]
                try:
                    result = asyncio.run(client.generate_response(
                        messages, prompt_name=spec['prompt_name'], **kwargs))
                finally:
                    logger.removeHandler(handler)
                json_ok = True
                try:
                    json.dumps(result)
                except TypeError:
                    json_ok = False
                calls.append({
                    'spec': spec,
                    'called': len(received) - before,
                    'result': json.dumps(result) if json_ok else None,
                    'events': events,
                    'log': handler.lines,
                    'rows': stored_rows(os.path.join(tmp, 'cache.db')),
                })
            cache.close()
            out.append({'name': scenario['name'], 'calls': calls, 'received_total': len(received)})
    return out


def record_cancel(make_class, LLMConfig, ModelSize, Message, Tracer, RateLimitError) -> list:
    """generate_response when the task is canceled. The cancel comes while an attempt fails with a retried error, or the
    attempt raises CancelledError itself. tenacity sleeps after a failed attempt but the last, and the cancel lands there."""
    cases = []
    logger = logging.getLogger(LOGGER_NAME)
    logger.setLevel(logging.DEBUG)
    specs = [{'mode': 'cancel_then_rate_limit', 'attempt': n} for n in (1, 2, 3, 4)]
    specs += [{'mode': 'raise_cancelled', 'attempt': n} for n in (1, 2, 4)]
    for spec in specs:
        calls: list = []

        async def _generate_response(self, messages, response_model=None, max_tokens=16384,
                                     model_size=ModelSize.medium, **kwargs):
            n = len(calls) + 1
            calls.append(n)
            if n < spec['attempt']:
                raise RateLimitError('rate')
            if spec['mode'] == 'cancel_then_rate_limit':
                asyncio.current_task().cancel()
                raise RateLimitError('rate')
            raise asyncio.CancelledError()

        cls = make_class('MockLLMClient')
        cls._generate_response = _generate_response
        client = cls(LLMConfig())
        events: list = []
        client.set_tracer(SpanEvents(Tracer, events).tracer)
        handler = LineHandler()
        logger.addHandler(handler)
        try:
            with patched_retry_environment([], [], 0.5):
                try:
                    asyncio.run(client.generate_response([Message(role='user', content='hi')]))
                    outcome = {'type': None}
                except BaseException as e:  # noqa: BLE001
                    outcome = {'type': type(e).__name__, 'message': str(e)}
        finally:
            logger.removeHandler(handler)
        cases.append({'spec': spec, 'attempts': len(calls), 'outcome': outcome, 'events': events, 'log': handler.lines})
    return cases


def record_client_init(make_class, LLMConfig) -> dict:
    """The attributes that LLMClient.__init__ sets, for an LLMConfig and for no config."""
    import tempfile

    local = random.Random(20261129)
    strings = ['', 'a', 'gpt-6-luna', 'gpt-4.1-mini', 'sk-test', 'é', '日本語', 'x' * 50]
    cls = make_class('MockLLMClient')

    def attrs(client) -> dict:
        return {
            'model': client.model,
            'small_model': client.small_model,
            'temperature': client.temperature,
            'temperature_type': type(client.temperature).__name__,
            'max_tokens': client.max_tokens,
            'cache_enabled': client.cache_enabled,
            'cache_dir': None if client.cache_dir is None else type(client.cache_dir).__name__,
            'tracer': type(client.tracer).__name__,
            'token_tracker': type(client.token_tracker).__name__,
            'config': {
                'base_url': client.config.base_url, 'api_key': client.config.api_key, 'model': client.config.model,
                'small_model': client.config.small_model, 'temperature': client.config.temperature,
                'max_tokens': client.config.max_tokens,
            },
        }

    cases = [{'kwargs': {}, 'attrs': attrs(cls(LLMConfig()))}]
    for _ in range(60):
        kwargs: dict = {}
        for name in ['api_key', 'model', 'base_url', 'small_model']:
            if local.random() < 0.6:
                kwargs[name] = local.choice(strings + [None])
        if local.random() < 0.6:
            kwargs['temperature'] = local.choice([0, 1, 2, 0.0, 0.2, 0.5, 0.7, 1.0, 1.5, -1])
        if local.random() < 0.6:
            kwargs['max_tokens'] = local.choice([0, 1, 100, 4096, 8192, 16384, 32768, -5])
        cases.append({'kwargs': kwargs, 'attrs': attrs(cls(LLMConfig(**kwargs)))})
    no_config = attrs(cls(None))

    # cache=True makes ./llm_cache/cache.db in the working directory.
    previous = os.getcwd()
    with tempfile.TemporaryDirectory() as tmp:
        os.chdir(tmp)
        try:
            client = cls(LLMConfig(model='m'), cache=True)
            with_cache = {'attrs': attrs(client), 'db_exists': os.path.isfile(os.path.join('llm_cache', 'cache.db'))}
            client.cache_dir.close()
        finally:
            os.chdir(previous)
    return {'cases': cases, 'no_config': no_config, 'with_cache': with_cache}


@contextlib.contextmanager
def patched_retry_environment(sleeps: list, uniforms: list, r: float):
    """Replaces asyncio.sleep and random.uniform. The wait is a + (b - a) * r for the scripted r."""
    real_sleep = asyncio.sleep
    real_uniform = random.uniform

    async def fake_sleep(seconds, *args, **kwargs):
        sleeps.append(seconds)
        await real_sleep(0)

    def fake_uniform(a, b):
        uniforms.append([a, b])
        return a + (b - a) * r

    asyncio.sleep = fake_sleep
    random.uniform = fake_uniform
    try:
        yield
    finally:
        asyncio.sleep = real_sleep
        random.uniform = real_uniform


def record_retry(llm_client, httpx, LLMConfig, Message, RateLimitError, EmptyResponseError, RefusalError) -> dict:
    classes = {'RateLimitError': RateLimitError, 'EmptyResponseError': EmptyResponseError, 'RefusalError': RefusalError}

    class Handler(logging.Handler):
        def __init__(self) -> None:
            super().__init__(logging.DEBUG)
            self.lines: list = []

        def emit(self, record) -> None:
            self.lines.append([record.levelname, record.getMessage()])

    logger = logging.getLogger(LOGGER_NAME)
    logger.setLevel(logging.DEBUG)

    retried = [{'kind': 'RateLimitError'}, {'kind': 'EmptyResponseError'}, {'kind': 'JSONDecodeError'},
               {'kind': 'HTTPStatusError', 'status': 500}, {'kind': 'HTTPStatusError', 'status': 503},
               {'kind': 'HTTPStatusError', 'status': 599}]
    not_retried = [{'kind': 'ValueError'}, {'kind': 'RefusalError'}, {'kind': 'HTTPStatusError', 'status': 400},
                   {'kind': 'HTTPStatusError', 'status': 429}, {'kind': 'HTTPStatusError', 'status': 600},
                   {'kind': 'TransportError'}, {'kind': 'ReadTimeout'}, {'kind': 'TimeoutError'}, {'kind': 'KeyError'},
                   {'kind': 'Exception'}]
    # Each scenario is the list of outcomes of the attempts. A string is a return value, and a dict is an error.
    scenarios = [[{'return': 'first'}]]
    for spec in retried:
        for failures in (1, 2, 3, 4):
            scenarios.append([spec] * failures + [{'return': 'after the failures'}])
    scenarios.append([retried[0], retried[1], retried[2], {'return': 'mixed'}])
    scenarios.append([retried[3], retried[5], retried[1], retried[0]])
    scenarios.append([retried[0], not_retried[0], {'return': 'never'}])
    scenarios.append([retried[1], retried[0], not_retried[2], {'return': 'never'}])
    scenarios.append([retried[2], retried[3], retried[4], not_retried[1]])
    for spec in not_retried:
        scenarios.append([spec, {'return': 'never'}])
    rs = [0.0, 0.25, 0.5, 0.999, 1.0]

    cases = []
    for index, scenario in enumerate(scenarios):
        r = rs[index % len(rs)]
        # Each error gets a message of its own, so that the test can tell which attempt's error left.
        script = []
        for attempt, outcome in enumerate(scenario):
            if 'return' in outcome:
                script.append(outcome)
            else:
                script.append({**outcome, 'message': f'error of attempt {attempt + 1}'})
        errors = [make_error(o, httpx, classes) if 'kind' in o else None for o in script]
        calls: list = []

        async def _generate_response(self, messages, response_model=None, max_tokens=llm_client.DEFAULT_MAX_TOKENS,
                                     model_size=llm_client.ModelSize.medium, **kwargs):
            attempt = len(calls)
            calls.append(kwargs)
            if 'return' in script[attempt]:
                return {'content': script[attempt]['return']}
            raise errors[attempt]

        client_class = type('RetryClient', (llm_client.LLMClient,), {'_generate_response': _generate_response})
        client = client_class(LLMConfig())
        sleeps: list = []
        uniforms: list = []
        handler = Handler()
        logger.addHandler(handler)
        try:
            with patched_retry_environment(sleeps, uniforms, r):
                try:
                    result = asyncio.run(client._generate_response_with_retry([Message(role='user', content='hi')]))
                    outcome = {'result': result}
                except Exception as e:  # noqa: BLE001
                    outcome = {'raised_attempt': errors.index(e) + 1, 'type': type(e).__name__}
        finally:
            logger.removeHandler(handler)
        cases.append({'script': script, 'r': r, 'attempts': len(calls), 'sleeps': sleeps, 'uniform_args': uniforms,
                      'log': handler.lines, 'outcome': outcome})

    # The call with the model argument, and with a subclass that has the four-parameter signature.
    seen: list = []

    async def _four_parameters(self, messages, response_model=None, max_tokens=1, model_size=llm_client.ModelSize.medium):
        seen.append('four')
        return {'content': 'four'}

    four = type('FourClient', (llm_client.LLMClient,), {'_generate_response': _four_parameters})(LLMConfig())
    result = asyncio.run(four._generate_response_with_retry([Message(role='user', content='hi')]))
    return {
        'cases': cases,
        'four_parameter_subclass_result': result,
        'attempts_total': 4,
    }


if __name__ == '__main__':
    sys.exit(main())
