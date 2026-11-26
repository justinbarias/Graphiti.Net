"""Records results of the decisions client from real Python, for the evaluation tests.

The modules are graphiti_core/llm_client/decision_prompts.py and openai_decisions_client.py. The judge has 44 cases
for the first one and none for the second, and the upstream tests check a few values each. This file gives the
evaluation tests generated inputs from a fixed seed (port/RULES.md, R23 and R25), so that the port can be compared
with real results:

- decision_prompts: each module-level constant, and the pure functions tag, parse_list, shrink_state, node_n6,
  _contra_strict, edge_e2, _user_text, _encode_payload, assemble_node_answers, and assemble_edge_answers for
  generated inputs, and the two prompt builders _jev_node_builder and _jev_edge_builder with a default builder
  that returns a fixed system text and a fixed user text.
- openai_decisions_client: _map_answers for generated answer lists, the arguments of the constructor, and a scripted
  exchange for each scenario. A scenario is a payload and the arguments of generate_response, and a script of
  replies: a reply that passes, a retried status or a transport error, an input that is too long, a reply that is
  not valid, and the token usage. The recording keeps each request (method, URL, whether the key is in the
  Authorization header, the content type, and the body text), each reply, the delays of _sleep, the usage that
  reaches the token tracker, the spans, and the result or the error of generate_response. The test of the port
  gives the recorded replies to a fake HttpMessageHandler, one after the other.

    .upstream/graphiti/.venv/bin/python port/tools/record_decisions_client.py
    .upstream/graphiti/.venv/bin/python port/tools/record_decisions_client.py --check

It writes tests/Port.Evaluation/values/llm_client_decisions.json. --check writes nothing and exits 1 if the file
differs from what the run would write. The tool needs Python 3.12 and the pinned upstream. The environment
variable GRAPHITI_UPSTREAM names the upstream checkout, and it defaults to .upstream/graphiti in the
repository. The tool changes nothing there. Nothing here uses the network: the HTTP client has a mock transport,
and the key is the text `test-key`.
"""
from __future__ import annotations

import asyncio
import contextlib
import warnings
import json
import os
import random
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'llm_client_decisions.json'

KEY = 'test-key'
BASE_URL = 'https://decisions.test/v1'

rng = random.Random(20261127)

WORDS = ['Alice', 'Bob', 'Acme Corp', 'NYC', 'New York City', "Marco's car", 'café', 'é', '日本語',
         '\U0001F600', "it's", 'say "hi"', '{braces}', '{{double}}', 'back\\slash', 'tab\tx', 'a\nb', '%s', '{0}',
         '<TAG>', 'x' * 30, '  padded  ', 'résumé', 'a b', 'a\x1cb', 'Java', 'engineer', 'Tuesday']


def word() -> str:
    return rng.choice(WORDS)


def sentence() -> str:
    return ' '.join(word() for _ in range(rng.randint(0, 6)))


def maybe(p: float) -> bool:
    return rng.random() < p


def run(fn, *args):
    """The result of a call as {'ok': value}, or {'err': [class name, text]}."""
    try:
        value = fn(*args)
    except Exception as exc:  # noqa: BLE001
        return {'err': [type(exc).__name__, str(exc)]}
    return {'ok': value}


def typed_list(value) -> bool:
    return isinstance(value, list) and all(isinstance(item, dict) and all(isinstance(k, str) for k in item) for item in value)


def render_list(items: list, style: int) -> str:
    """A list as a prompt writes it: JSON, JSON with no escapes, or the repr of the list."""
    if style == 0:
        return json.dumps(items)
    if style == 1:
        return json.dumps(items, ensure_ascii=False)
    return str(items)


def entity_items() -> list:
    items = []
    for i in range(rng.randint(0, 5)):
        e: dict = {'id': i}
        if maybe(0.9):
            e['name'] = word()
        r = rng.random()
        if r < 0.6:
            e['entity_type'] = rng.sample(['Person', 'Location', 'Organization', 'Entity', '日本'], rng.randint(0, 3))
        elif r < 0.7:
            e['entity_type'] = None
        elif r < 0.75:
            e['entity_type'] = 'Person'
        items.append(e)
    return items


def existing_items() -> list:
    items = []
    for i in range(rng.randint(0, 5)):
        c: dict = {'candidate_id': rng.randint(0, 20) + i}
        if maybe(0.85):
            c['name'] = word()
        if maybe(0.1):
            del c['candidate_id']
        items.append(c)
    return items


def fact_items(start: int = 0) -> list:
    items = []
    for i in range(rng.randint(0, 4)):
        f: dict = {'idx': start + i}
        r = rng.random()
        if r < 0.8:
            f['fact'] = sentence()
        elif r < 0.9:
            f['raw'] = sentence()
        items.append(f)
    return items


def cut(text: str) -> str:
    """The text with a random end cut off."""
    return text[: rng.randint(0, len(text))] if maybe(0.1) else text


def node_user() -> str:
    previous = render_list([{'content': sentence()} for _ in range(rng.randint(0, 3))], rng.randint(0, 2))
    return cut(
        f'\n<PREVIOUS MESSAGES>\n{previous}\n</PREVIOUS MESSAGES>\n\n<CURRENT MESSAGE>\n{sentence()}\n</CURRENT MESSAGE>\n\n'
        f'<ENTITIES>\n{render_list(entity_items(), rng.randint(0, 2))}\n</ENTITIES>\n\n'
        f'<EXISTING ENTITIES>\n{render_list(existing_items(), rng.randint(0, 2))}\n</EXISTING ENTITIES>\n\n'
        f'Each of the above ENTITIES was extracted from the CURRENT MESSAGE.\n' + ('tail ' + sentence() if maybe(0.5) else '')
    )


def edge_user() -> str:
    existing = fact_items()
    candidates = fact_items(10)
    return cut(
        f'\nNEVER mark facts as duplicates.\n\n<EXISTING FACTS>\n{render_list(existing, rng.randint(0, 2))}\n</EXISTING FACTS>\n\n'
        f'<FACT INVALIDATION CANDIDATES>\n{render_list(candidates, rng.randint(0, 2))}\n</FACT INVALIDATION CANDIDATES>\n\n'
        f'<NEW FACT>\n{sentence()}\n</NEW FACT>\n'
    )


def tag_text(name: str) -> str:
    gap = rng.choice(['', ' ', '\n', '  \n', '\x1c', ' '])
    text = f'{sentence()}<{name}>{gap}{sentence()}{gap}</{name}>{sentence()}'
    r = rng.random()
    if r < 0.1:
        text = text.replace(f'</{name}>', '')
    elif r < 0.2:
        text = f'<{name}>1</{name}><{name}>2</{name}>'
    return text


def record_constants(dp) -> dict:
    return {name: getattr(dp, name) for name in ['RULES_HEADER', 'NODE_RULES', 'NODE_NONE', 'DUP_RULES', 'CON_RULES']}


def record_pure(dp) -> list:
    from graphiti_core.prompts.models import Message

    cases: list = []

    def add(name: str, *args, encode=None) -> None:
        result = run(getattr(dp, name), *args)
        if 'ok' in result and encode is not None:
            result = {'ok': encode(result['ok'])}
        if name in ('node_n6', 'edge_e2') and 'ok' in result:
            # The state ends with the rule text, which `constants` holds. The file keeps it once.
            kind = 'node' if name == 'node_n6' else 'edge'
            suffix = dp.RULES_HEADER + dp.NODE_RULES if kind == 'node' else dp.RULES_HEADER + 'DUPLICATE: ' + dp.DUP_RULES + '\nCONTRADICTION: ' + dp.CON_RULES
            state = result['ok'][0]
            assert state.endswith(suffix)
            result = {'ok': [state[: -len(suffix)], *result['ok'][1:]], 'suffix': kind}
        cases.append({'f': name, 'a': [encode_arg(a) for a in args], 'r': result})

    def encode_arg(value):
        if isinstance(value, Message):
            return {'role': value.role, 'content': value.content}
        if isinstance(value, list) and value and isinstance(value[0], Message):
            return [encode_arg(m) for m in value]
        return value

    names = ['A', 'PREVIOUS MESSAGES', 'CURRENT MESSAGE', 'ENTITIES', 'EXISTING ENTITIES', 'a.b', 'café', 'NEW FACT']
    for _ in range(120):
        name = rng.choice(names)
        add('tag', tag_text(name), name)
    add('tag', 'nothing', 'A')

    # parse_list: a result that is not a list of dicts has no place in the C# type, and the test expects
    # InvalidCastException for it.
    texts: list = [None, '', ' \n ', '[]', '{"a": 1}', '[1, 2]', '[{"a": 1}, {"b": [true, null]}]', "[{'a': 1, 'b': True, 'c': None}]",
                   "[{'candidate_id': 0, 'name': 'it's'}, {'candidate_id': 1, 'name': 'x'}]", "[{'id': 1, 'name': 'a'}, {'id': 2, 'na",
                   "[{'idx': 4, 'fact': 'it's odd'}, {'idx': 5, 'fact': 'fine'}]", '[{"id": 1, "name": "a"},', 'garbage {no close',
                   "[{'a': 1}, {'b': 2}]", "[{1: 'a'}]", '[{"id": 12345678901234567890}]', "{'candidate_id': 5}"]
    for _ in range(100):
        r = rng.random()
        if r < 0.3:
            items = entity_items() if maybe(0.5) else existing_items() + fact_items()
            texts.append(render_list(items, rng.randint(0, 2)))
        elif r < 0.45:
            items = fact_items() + existing_items()
            texts.append(cut(render_list(items, 2)).replace('"', "'", rng.randint(0, 3)))
        elif r < 0.7:
            items = existing_items() + fact_items()
            text = render_list(items, 2)
            texts.append(text[: rng.randint(0, len(text))])
        elif r < 0.85:
            texts.append("[" + ", ".join("{'candidate_id': %d, 'name': '%s'}" % (rng.randint(-3, 9), word().replace("'", '')) for _ in range(rng.randint(0, 3))) + "']")
        else:
            texts.append(sentence() + ' {' + sentence() + '} ' + sentence())
    for text in texts:
        result = run(dp.parse_list, text)
        if 'ok' in result and not typed_list(result['ok']):
            result = {'untyped': repr(result['ok'])}
        cases.append({'f': 'parse_list', 'a': [text], 'r': result})

    for _ in range(50):
        r = rng.random()
        prev = render_list([{'content': sentence()}], 0) if maybe(0.7) else '[]'
        cur = ('current ' + word()) * (rng.randint(1, 3) if r < 0.4 else rng.randint(70, 130))
        state = f'<PREVIOUS MESSAGES>{prev}</PREVIOUS MESSAGES>\n<CURRENT MESSAGE>{cur}</CURRENT MESSAGE>'
        if maybe(0.15):
            state = f'<CURRENT MESSAGE>{cur}</CURRENT MESSAGE>'
        if maybe(0.1):
            state = sentence()
        add('shrink_state', state)

    for _ in range(40):
        add('node_n6', node_user())
    add('node_n6', '<ENTITIES>\n[{"name": "x"}]\n</ENTITIES>\n<EXISTING ENTITIES>\n[]\n</EXISTING ENTITIES>')
    for _ in range(40):
        add('edge_e2', edge_user())
    for _ in range(40):
        f: dict = {}
        if maybe(0.9):
            f['idx'] = rng.randint(0, 9)
        r = rng.random()
        if r < 0.5:
            f['fact'] = sentence()
        elif r < 0.8:
            f['raw'] = sentence()
        add('_contra_strict', sentence(), f)

    for _ in range(30):
        messages = [Message(role=rng.choice(['system', 'user', 'assistant']), content=sentence()) for _ in range(rng.randint(0, 4))]
        cases.append({'f': '_user_text', 'a': [[encode_arg(m) for m in messages]], 'r': run(dp._user_text, messages)})

    for _ in range(40):
        questions = {f'q{i}': {'type': rng.choice(['choice', 'noul']), 'instructions': sentence(), 'criteria': {'a': word()}}
                     for i in range(rng.randint(0, 3))}
        add('_encode_payload', sentence(), questions)

    for _ in range(80):
        entities = entity_items()
        answers: dict = {}
        for e in entities:
            r = rng.random()
            key = f'e{e["id"]}'
            if r < 0.3:
                answers[key] = {'probabilities': {rng.choice(['none', '0', '1', '2', '7']): round(rng.random(), 3) for _ in range(rng.randint(1, 3))}}
            elif r < 0.5:
                answers[key] = {'choice': rng.choice(['none', '3', ' 4 ', '１２', 'abc', '3.5', None, ''])}
            elif r < 0.6:
                answers[key] = {}
            elif r < 0.7:
                answers[key] = None
            elif r < 0.8:
                answers[key] = {'probabilities': {}, 'choice': '5'}
            elif r < 0.9:
                answers[key] = {'probabilities': {'1': 0.5, '2': 0.5, 'none': 0.5}}
        add('assemble_node_answers', answers, entities)

    for _ in range(80):
        existing = rng.sample(range(0, 8), rng.randint(0, 4))
        contra = rng.sample(range(0, 8), rng.randint(0, 4))
        answers = {}
        for idx in existing:
            if maybe(0.8):
                answers[f'dup{idx}'] = rng.choice([{'noul': round(rng.random(), 2)}, {'noul': 1}, {}, None, {'noul': 0.7}, {'noul': 0}])
        for idx in contra:
            if maybe(0.8):
                answers[f'con{idx}'] = rng.choice([{'noul': round(rng.random(), 2)}, {'noul': 1}, {}, None, {'noul': 0.8}])
        add('assemble_edge_answers', answers, existing, contra, rng.choice([0.0, 0.5, 0.7, 1.0, 0.8, float('nan'), 2]), rng.choice([0.0, 0.5, 0.8, 1.0, float('nan')]))
    return cases


def record_builders(dp) -> list:
    """_jev_node_builder and _jev_edge_builder with a default builder that gives a fixed system text and user text."""
    from graphiti_core.prompts.models import ChatPrompt, SystemMessage, UserMessage

    def default_builder(context):
        return ChatPrompt(system=SystemMessage(content=context['system']), user=UserMessage(content=context['user']))

    cases = []
    for kind, make in (('node', node_user), ('edge', edge_user)):
        builder = dp._jev_node_builder(default_builder) if kind == 'node' else dp._jev_edge_builder(default_builder)
        for _ in range(10):
            context = {'system': sentence(), 'user': make()}
            result = run(lambda: builder(context))
            if 'ok' in result:
                result = {'ok': {'system': result['ok'].system.content, 'user': result['ok'].user.content}}
            cases.append({'kind': kind, 'context': context, 'r': result})
    return cases


def question_set(prompt: str, count: int) -> dict:
    """The questions of a payload, in the order that the prompt builders write them."""
    questions: dict = {}
    if prompt == 'dedupe_nodes.nodes':
        for i in range(count):
            criteria = {'none': 'No match', str(i + 3): sentence()}
            questions[f'e{i}'] = {'type': 'choice', 'instructions': sentence(), 'criteria': criteria}
        return questions
    for i in range(count):
        questions[f'dup{i}'] = {'type': rng.choice(['noul', 'predicate']), 'instructions': sentence(), 'criteria': {'true': 'yes', 'false': 'no'}}
    for i in range(count):
        if maybe(0.7):
            questions[f'con{i}'] = {'type': rng.choice(['noul', 'predicate']), 'instructions': sentence()}
    return questions


def valid_answers(body_questions: list, mutate: str | None) -> list:
    answers = []
    for q in body_questions:
        if q['type'] == 'predicate':
            answers.append({'type': 'predicate', 'name': q['name'], 'probability': rng.choice([0.0, 0.69, 0.7, 0.79, 0.8, 1.0, round(rng.random(), 3), 1, 0])})
            continue
        values = [c['value'] for c in q['choices']]
        r = rng.random()
        if r < 0.5:
            probabilities = [{'value': v, 'probability': round(rng.random(), 3)} for v in values]
            answers.append({'type': 'choice', 'name': q['name'], 'probabilities': probabilities, 'choice': rng.choice(values), 'confidence': 1.0})
        elif r < 0.8:
            answers.append({'type': 'choice', 'name': q['name'], 'choice': rng.choice(values), 'confidence': rng.choice([0.5, 1.0, 0.25])})
        else:
            answers.append({'type': 'choice', 'name': q['name'], 'probabilities': [], 'choice': rng.choice(values), 'confidence': 0.9})
    if mutate and answers:
        i = rng.randrange(len(answers))
        a = answers[i]
        if mutate == 'unknown_name':
            a['name'] = 'unknown'
        elif mutate == 'duplicate_name' and len(answers) > 1:
            answers[i] = dict(answers[0])
        elif mutate == 'missing':
            del answers[i]
        elif mutate == 'wrong_type':
            a['type'] = 'choice' if a['type'] == 'predicate' else 'predicate'
        elif mutate == 'bad_probability':
            if a['type'] == 'predicate':
                a['probability'] = rng.choice([1.5, -0.1, True, 'x', None, float('nan')])
            else:
                a['probabilities'] = [{'value': a['choice'], 'probability': rng.choice([2, -1, True, 'x'])}]
        elif mutate == 'bad_choice':
            if a['type'] == 'choice':
                a['choice'] = rng.choice(['other', 5, ''])
                a.pop('probabilities', None)
        elif mutate == 'no_confidence':
            if a['type'] == 'choice':
                a.pop('probabilities', None)
                a.pop('confidence', None)
        elif mutate == 'bad_item':
            answers[i] = rng.choice([1, 'x', None, [1]])
        elif mutate == 'bad_probability_item':
            if a['type'] == 'choice':
                a['probabilities'] = [rng.choice([1, 'x', None]) ]
        elif mutate == 'duplicate_value':
            if a['type'] == 'choice':
                a['probabilities'] = [{'value': a['choice'], 'probability': 0.5}, {'value': a['choice'], 'probability': 0.5}]
        elif mutate == 'unknown_value':
            if a['type'] == 'choice':
                a['probabilities'] = [{'value': 'zzz', 'probability': 0.5}]
    return answers


def usage_value() -> object:
    r = rng.random()
    if r < 0.5:
        return {'input_tokens': rng.randint(0, 5000), 'output_tokens': rng.randint(0, 500), 'total_tokens': 1}
    if r < 0.6:
        return None
    if r < 0.7:
        return {}
    if r < 0.8:
        return {'input_tokens': '12', 'output_tokens': 3.9}
    if r < 0.9:
        return {'input_tokens': rng.randint(0, 99)}
    return {'output_tokens': rng.randint(0, 99)}


class RecordingSpan:
    def __init__(self, name: str) -> None:
        self.record = {'name': name, 'attributes': {}, 'status': None, 'exceptions': []}

    def add_attributes(self, attributes: dict) -> None:
        self.record['attributes'].update(attributes)

    def set_status(self, status: str, description: str | None = None) -> None:
        self.record['status'] = [status, description]

    def record_exception(self, exception: Exception) -> None:
        self.record['exceptions'].append([type(exception).__name__, str(exception)])


def make_tracer(spans: list):
    from graphiti_core.tracer import Tracer

    class RecordingTracer(Tracer):
        @contextlib.contextmanager
        def start_span(self, name: str):
            span = RecordingSpan(name)
            spans.append(span.record)
            yield span

    return RecordingTracer()


def too_long_body(kind: int) -> dict:
    if kind == 0:
        return {'error': {'code': 'context_length_exceeded', 'type': 'invalid_request_error'}}
    if kind == 1:
        return {'error': {'code': 'string_above_max_length', 'type': 'invalid_request_error'}}
    if kind == 2:
        return {'error': {'message': 'Decision input exceeds the token limit.', 'type': 'invalid_request_error', 'param': None, 'code': None}}
    if kind == 3:
        return {'error': {'message': 'DECISION INPUT EXCEEDS THE TOKEN LIMIT of 5', 'type': 'invalid_request_error'}}
    if kind == 4:
        return {'error': {'message': 'exceeds the token limit', 'type': 'server_error'}}
    if kind == 5:
        return {'error': {'message': 'The request has an invalid parameter.', 'type': 'invalid_request_error', 'param': None, 'code': None}}
    if kind == 6:
        return {'error': 'exceeds the token limit'}
    if kind == 7:
        return {'error': {'message': ['exceeds the token limit'], 'type': 'invalid_request_error'}}
    if kind == 8:
        return {'message': 'exceeds the token limit'}
    return {'error': {'code': 'other', 'message': 'Café 日本 \U0001F600 exceeds the token limit', 'type': 'invalid_request_error'}}


def long_state(prompt: str, entities: list) -> str:
    previous = render_list([{'content': 'previous ' + word()}], 0)
    current = ('current ' + word() + ' ') * rng.randint(110, 260)
    parts = [f'\n<PREVIOUS MESSAGES>\n{previous}\n</PREVIOUS MESSAGES>\n\n<CURRENT MESSAGE>\n{current}\n</CURRENT MESSAGE>\n']
    if prompt == 'dedupe_nodes.nodes':
        parts.append(f'\n<ENTITIES>\n{render_list(entities, rng.randint(0, 1))}\n</ENTITIES>\n')
        parts.append(f'\n<EXISTING ENTITIES>\n{render_list(existing_items(), 0)}\n</EXISTING ENTITIES>')
    return ''.join(parts)


def make_scenario(index: int) -> dict:
    kind = index % 6
    prompt = rng.choice(['dedupe_nodes.nodes', 'dedupe_edges.resolve_edge'])
    count = rng.randint(1, 4) if kind != 0 else rng.randint(0, 5)
    questions = question_set(prompt, count)
    entities = [{'id': i, 'name': word(), 'entity_type': ['Person']} for i in range(count)]
    state = sentence() if kind != 2 else long_state(prompt, entities)
    if prompt == 'dedupe_nodes.nodes' and kind != 2:
        state = f'<CURRENT MESSAGE>{sentence()}</CURRENT MESSAGE>\n<ENTITIES>\n{render_list(entities, 0)}\n</ENTITIES>'
    instructions = rng.choice(['', 'caller instructions', sentence(), ' \n ', 'with trailing\n\n'])
    sc: dict = {
        'prompt_name': prompt,
        'instructions': instructions,
        'state': state,
        'questions': questions,
        'config': {'max_retries': rng.choice([5, 5, 5, 3, 1, 2, 4, 0]), 'base_url': rng.choice([BASE_URL, BASE_URL + '/', None]),
                   'model': rng.choice([None, 'gpt-6-luna', 'custom-model', '']),
                   'duplicate_threshold': rng.choice([0.7, 0.7, 0.5, 1.0]), 'contradiction_threshold': rng.choice([0.8, 0.8, 0.5, 0.0])},
        'call': {'model': rng.choice([None, None, 'call-model', '']), 'max_tokens': rng.choice([None, None, 100, 0]),
                 'model_size': rng.choice(['medium', 'small'])},
    }
    script: list = []
    if kind == 0:  # a reply that passes, with usage
        script.append({'valid': True, 'usage': usage_value()})
    elif kind == 1:  # retried statuses and transport errors
        for _ in range(rng.randint(0, 4)):
            r = rng.random()
            if r < 0.15:
                script.append({'raise': 'ReadTimeout', 'message': 'request timed out'})
            elif r < 0.3:
                script.append({'raise': 'ConnectError', 'message': 'connection refused'})
            else:
                status = rng.choice([500, 502, 503, 504, 429, 408, 401, 404, 400, 403, 301])
                script.append({'status': status, 'body': rng.choice(['', 'busy', json.dumps({'error': {'message': 'try later'}}), 'x' * 700, 'café \U0001F600'])})
        script.append(rng.choice([{'valid': True, 'usage': usage_value()}, {'valid': True, 'usage': usage_value()}, {'status': 429, 'body': 'limit'}, {'status': 500, 'body': ''}]))
    elif kind == 2:  # an input that is too long
        n = rng.randint(1, 5)
        for _ in range(n):
            script.append({'too_long': rng.randint(0, 9)})
        script.append(rng.choice([{'valid': True, 'usage': usage_value()}, {'too_long': rng.randint(0, 9)}, {'valid': True, 'usage': usage_value()}]))
        sc['script_mode'] = rng.choice(['repeat_last', 'repeat_last', 'split_ok'])
    elif kind == 3:  # a reply that is not valid
        mutate = rng.choice(['unknown_name', 'duplicate_name', 'missing', 'wrong_type', 'bad_probability', 'bad_choice', 'no_confidence',
                             'bad_item', 'bad_probability_item', 'duplicate_value', 'unknown_value'])
        script.append(rng.choice([
            {'valid': True, 'mutate': mutate, 'usage': usage_value()},
            {'valid': True, 'mutate': mutate, 'usage': usage_value()},
            {'status': 200, 'body': rng.choice(['', 'not json', '{}', '{"answers": null}', '{"answers": {}}', '{"answers": "x"}', '{"answers": []}',
                                                  '{"answers": [1]}', '{"usage": {}}', '{"answers": [', '﻿{}', '{"answers": [],}'])},
        ]))
    elif kind == 4:  # a question or a payload that the client refuses
        r = rng.random()
        if r < 0.25:
            sc['prompt_name'] = rng.choice([None, 'extract_nodes.extract_message', 'dedupe_nodes', ''])
        elif r < 0.4:
            sc['questions'] = {'q': {'type': rng.choice(['text', None, 5, 'Choice']), 'instructions': 'x'}}
        elif r < 0.5:
            sc['questions'] = {'q': {'type': 'choice', 'instructions': 'x'}}
        elif r < 0.6:
            sc['questions'] = {'q': {'type': 'choice', 'instructions': 'x', 'criteria': ['a']}}
        elif r < 0.7:
            sc['instructions'] = rng.choice([5, None, ['a']])
        elif r < 0.8:
            sc['state'] = rng.choice([5, None, ['a']])
        elif r < 0.9:
            sc['questions'] = rng.choice([[], 'x', 5, None])
        else:
            sc['no_user'] = True
        script.append({'valid': True, 'usage': usage_value()})
    else:  # usage over split requests, and valid answers for a mix of statuses
        script.append({'too_long': 0})
        script.append({'valid': True, 'usage': usage_value()})
        script.append({'valid': True, 'usage': usage_value()})
        script.append({'status': 500, 'body': ''})
        script.append({'valid': True, 'usage': usage_value()})
    sc['script'] = script
    return sc


async def run_scenario(sc: dict, odc, LLMConfig, Message, httpx) -> dict:
    exchanges: list = []
    delays: list = []
    spans: list = []
    script = sc['script']
    mode = sc.get('script_mode', 'repeat_last')

    def reply_for(entry: dict, body: dict) -> dict:
        if 'raise' in entry:
            return {'raise': entry['raise'], 'message': entry['message']}
        if 'too_long' in entry:
            return {'status': 400, 'body': json.dumps(too_long_body(entry['too_long']), ensure_ascii=False), 'content_type': 'application/json'}
        if entry.get('valid'):
            answers = valid_answers(body['questions'], entry.get('mutate'))
            payload: dict = {'model': body['model'], 'answers': answers}
            if 'usage' in entry:
                payload['usage'] = entry['usage']
            return {'status': 200, 'body': json.dumps(payload, ensure_ascii=False), 'content_type': 'application/json'}
        return {'status': entry['status'], 'body': entry['body'], 'content_type': 'text/plain'}

    def handler(request):
        index = len(exchanges)
        body_text = request.content.decode('utf-8')
        body = json.loads(body_text)
        if index < len(script):
            entry = script[index]
        elif mode == 'split_ok':
            entry = {'valid': True, 'usage': usage_value()}
        else:
            entry = script[-1]
        reply = reply_for(entry, body)
        exchanges.append({
            'request': {
                'method': request.method,
                'url': str(request.url),
                'authorization_ok': request.headers.get('Authorization') == f'Bearer {KEY}',
                'content_type': request.headers.get('Content-Type'),
                'body_text': body_text,
            },
            'reply': reply,
        })
        if 'raise' in reply:
            raise getattr(httpx, reply['raise'])(reply['message'])
        return httpx.Response(reply['status'], content=reply['body'].encode('utf-8'), headers={'Content-Type': reply['content_type']})

    async def no_wait(delay) -> None:
        delays.append(delay)

    cfg = sc['config']
    odc._sleep = no_wait
    http_client = httpx.AsyncClient(transport=httpx.MockTransport(handler))
    try:
        client = odc.OpenAIDecisionsClient(
            config=LLMConfig(api_key=KEY, model=cfg['model']),
            base_url=cfg['base_url'],
            duplicate_threshold=cfg['duplicate_threshold'],
            contradiction_threshold=cfg['contradiction_threshold'],
            max_retries=cfg['max_retries'],
            http_client=http_client,
        )
        client.tracer = make_tracer(spans)
        payload = {'instructions': sc['instructions'], 'state': sc['state'], 'questions': sc['questions']}
        messages = [Message(role='system', content='system text'), Message(role='user', content=json.dumps(payload))]
        if sc.get('no_user'):
            messages = [Message(role='system', content='system text')]
        call = sc['call']
        from graphiti_core.llm_client.config import ModelSize

        kwargs: dict = {'model_size': ModelSize(call['model_size'])}
        if call['model'] is not None:
            kwargs['model'] = call['model']
        if call['max_tokens'] is not None:
            kwargs['max_tokens'] = call['max_tokens']
        try:
            outcome: dict = {'ok': await client.generate_response(messages, prompt_name=sc['prompt_name'], **kwargs)}
        except Exception as exc:  # noqa: BLE001
            outcome = {'err': [type(exc).__name__, str(exc)]}
        usage = client.token_tracker.get_total_usage()
        by_prompt = [[k, v.call_count, v.total_input_tokens, v.total_output_tokens] for k, v in client.token_tracker.get_usage().items()]
    finally:
        await http_client.aclose()
    return {
        'scenario': sc,
        'exchanges': exchanges,
        'delays': delays,
        'spans': spans,
        'usage': {'total': [usage.input_tokens, usage.output_tokens], 'by_prompt': by_prompt},
        'outcome': outcome,
    }


def record_map_answers(odc) -> list:
    cases = []
    for _ in range(150):
        prompt = rng.choice(['dedupe_nodes.nodes', 'dedupe_edges.resolve_edge'])
        questions = question_set(prompt, rng.randint(0, 4))
        wire = []
        for name, q in questions.items():
            if q['type'] == 'choice':
                wire.append({'name': name, 'type': 'choice', 'choices': [{'value': k, 'description': 'd'} for k in q['criteria']]})
            else:
                wire.append({'name': name, 'type': 'predicate'})
        raw = valid_answers(wire, rng.choice([None, None, 'unknown_name', 'duplicate_name', 'missing', 'wrong_type', 'bad_probability', 'bad_choice',
                                              'no_confidence', 'bad_item', 'bad_probability_item', 'duplicate_value', 'unknown_value']))
        if maybe(0.05):
            raw = rng.choice([None, 'x', {}, 5, {'answers': []}])
        if maybe(0.1) and isinstance(raw, list):
            raw = raw + raw[:1]
        cases.append({'questions': questions, 'raw': raw, 'r': run(odc.OpenAIDecisionsClient._map_answers, questions, raw)})
    return cases


def record_constructor(odc, LLMConfig, httpx) -> list:
    cases = []
    for _once in [0]:
        for argument in [None, 'https://argument.test/v1/']:
            for config_url in [None, 'https://config.test/v1/', '']:
                for env_url in [None, 'https://environment.test/v1///', '']:
                    for config_key in [None, '', KEY]:
                        for env_key in [None, '', 'env-key']:
                            for model in [None, '', 'm']:
                                if rng.random() > 0.12:
                                    continue
                                for name, value in (('OPENAI_API_KEY', env_key), ('OPENAI_BASE_URL', env_url)):
                                    if value is None:
                                        os.environ.pop(name, None)
                                    else:
                                        os.environ[name] = value
                                config = LLMConfig(api_key=config_key, model=model, base_url=config_url, temperature=0.5, max_tokens=77, small_model='s')
                                result = run(lambda: odc.OpenAIDecisionsClient(config=config, base_url=argument, http_client=httpx.AsyncClient()))
                                if 'ok' in result:
                                    client = result['ok']
                                    result = {'ok': {
                                        'base_url': client.base_url, 'model': client.model, 'small_model': client.small_model,
                                        'temperature': client.temperature, 'max_tokens': client.max_tokens,
                                        'config_base_url': client.config.base_url,
                                        'key_is_config_key': client.api_key == (config_key or env_key),
                                        'key_in_config': client.config.api_key == (config_key or env_key),
                                        'thresholds': [client.duplicate_threshold, client.contradiction_threshold],
                                        'timeout_seconds': client.timeout_seconds, 'max_retries': client.max_retries,
                                        'cache_enabled': client.cache_enabled,
                                    }}
                                cases.append({
                                    'argument_base_url': argument, 'config_base_url': config_url, 'env_base_url': env_url,
                                    'config_key': config_key, 'env_key': env_key, 'model': model, 'r': result,
                                })
    for name in ('OPENAI_API_KEY', 'OPENAI_BASE_URL'):
        os.environ.pop(name, None)
    return cases


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    warnings.simplefilter('ignore')  # ast.literal_eval warns about a backslash that is no escape
    sys.path.insert(0, str(UPSTREAM))
    import httpx

    from graphiti_core.llm_client import decision_prompts as dp
    from graphiti_core.llm_client import openai_decisions_client as odc
    from graphiti_core.llm_client.config import LLMConfig
    from graphiti_core.prompts.models import Message

    scenarios = []

    async def go() -> None:
        for i in range(126):
            scenarios.append(await run_scenario(make_scenario(i), odc, LLMConfig, Message, httpx))

    # The environment may hold a real key. This process removes the two variables and reads neither of them. The
    # constructor cases set them to fixed texts.
    os.environ.pop('OPENAI_API_KEY', None)
    os.environ.pop('OPENAI_BASE_URL', None)
    if True:
        asyncio.run(go())
        data = {
            'python': '.'.join(str(p) for p in sys.version_info[:3]),
            'source': 'graphiti_core/llm_client/{decision_prompts,openai_decisions_client}.py',
            'key': KEY,
            'constants': record_constants(dp),
            'pure': record_pure(dp),
            'builders': record_builders(dp),
            'map_answers': record_map_answers(odc),
            'constructor': record_constructor(odc, LLMConfig, httpx),
            'scenarios': scenarios,
        }
    # allow_nan keeps inf and nan in the file as the tokens that PyJson.Loads reads.
    text = json.dumps(data, ensure_ascii=True, allow_nan=True, separators=(',', ':')) + '\n'
    count = len(data['pure']) + len(data['builders']) + len(data['map_answers']) + len(data['constructor']) + len(data['scenarios'])
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
