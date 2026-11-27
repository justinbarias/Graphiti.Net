"""Records results of graphiti_core/llm_client/llm_runtime.py from real Python, for the evaluation tests.

The judge has no case for this module: its constructor takes a transport that holds a model client, which the value
model cannot hold. The upstream tests (tests/llm_client/test_llm_runtime.py) check a few routes. This file runs the
upstream class with fake model clients at the network boundary, with generated route trees from a fixed seed
(port/RULES.md, R23 and R25), so that the port can be compared with real results:

- the constructor: the order and the identity of the transports (models that share a transport, the default model in
  each place), `client`, the flat routes, and the flat overrides.
- set_tracer: the clients that get the tracer, and how many times each one gets it (two transports that share one
  client give one call).
- resolve_model and resolve_builder for each prompt name and for odd names, with the library of markers, and with the
  models that have overrides of their own.
- complete: what the client receives (the model, the prompt name, the response model, max_tokens, the model size, the
  group id, the flag of attribute extraction, and the messages), the reply, and each error with its text. Also calls
  that run at the same time (gather), with the calls and the log lines in one ordered list.
- _wrap_builder.

    GRAPHITI_UPSTREAM=.upstream/graphiti .upstream/graphiti/.venv/bin/python -B port/tools/record_llm_runtime.py
    GRAPHITI_UPSTREAM=.upstream/graphiti .upstream/graphiti/.venv/bin/python -B port/tools/record_llm_runtime.py --check

It writes tests/Port.Evaluation/values/llm_runtime.json. --check writes nothing and exits 1 if the file differs from
what the run would write. The tool needs Python 3.12 and the pinned upstream. The environment variable
GRAPHITI_UPSTREAM names the upstream checkout, and it defaults to .upstream/graphiti in the repository. The tool
changes nothing there. Nothing here uses the network: the clients are fakes.

Each fake method that returns a task yields first (`await asyncio.sleep(0)`), and then records its call. One ordered
list holds the calls of the clients and the log lines of the run. A `cs: false` case has no C# form: the C# types hold
the right classes, so a builder that returns a list has no form there.
"""
from __future__ import annotations

import asyncio
import json
import logging
import os
import random
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'llm_runtime.json'

sys.path.insert(0, str(UPSTREAM))

from pydantic import BaseModel  # noqa: E402

from graphiti_core.llm_client.client import LLMClient  # noqa: E402
from graphiti_core.llm_client.config import LLMConfig, ModelSize  # noqa: E402
from graphiti_core.llm_client.llm_runtime import LLMRuntime, _wrap_builder  # noqa: E402
from graphiti_core.llm_client.prompt_config import (  # noqa: E402
    LLMPromptOverrides,
    LLMTransport,
    PromptRoutes,
)
from graphiti_core.prompts.lib import BUILTIN_PROMPT_SPECS, PROMPT_GROUPS, create_prompt_library  # noqa: E402
from graphiti_core.prompts.models import ChatPrompt, SystemMessage, UserMessage  # noqa: E402

rng = random.Random(20261129)

GROUPS = list(PROMPT_GROUPS)
NAMES = [f'{group}.{name}' for group, names in PROMPT_GROUPS.items() for name in names]
DYNAMIC = [name for name in NAMES if BUILTIN_PROMPT_SPECS[name].dynamic_schema]
FIXED = [name for name in NAMES if not BUILTIN_PROMPT_SPECS[name].dynamic_schema]
ODD_NAMES = ['extract_nodes', 'eval', 'dedupe_edges', 'unknown.x', '', 'a.b.c', '.x', 'x.', 'extract_nodes.',
             'extract_nodes.nope', 'Extract_Nodes.extract_message', 'extract_nodes.extract_message.extra',
             'eval.eval_prompt ', 'ǆ.x']


class OtherModel(BaseModel):
    x: int = 1


class PersonAttrs(BaseModel):
    role: str = ''


CUSTOM_MODELS = {'OtherModel': OtherModel, 'PersonAttrs': PersonAttrs}

# The default library gets a real context for these two names (the contexts of the upstream tests).
DEFAULT_CONTEXTS = {
    'extract_nodes.extract_message': {
        'episode_content': 'hi',
        'previous_episodes': [],
        'custom_extraction_instructions': '',
        'entity_types': [],
        'source_description': 't',
    },
    'dedupe_edges.resolve_edge': {
        'existing_edges': [],
        'edge_invalidation_candidates': [],
        'new_edge': 'x',
    },
}

LOG_LINES: list = []


class LogHandler(logging.Handler):
    def emit(self, record: logging.LogRecord) -> None:
        LOG_LINES.append(['log', record.levelname, record.getMessage()])


class FakeLLM(LLMClient):
    """A model client at the network boundary. It yields first, and then records the call into the shared list."""

    def __init__(self, name: str, events: list) -> None:
        super().__init__(LLMConfig(model='gpt-4.1'), cache=False)
        self.name = name
        self.events = events

    async def _generate_response(self, messages, response_model=None, max_tokens=None, model_size=None, *, model=None):
        return {}

    async def generate_response(self, messages, **kwargs):
        await asyncio.sleep(0)
        response_model = kwargs.get('response_model')
        self.events.append(['call', self.name, {
            'keys': sorted(kwargs),
            'model': kwargs.get('model'),
            'prompt_name': kwargs.get('prompt_name'),
            'response_model': response_model.__name__ if response_model is not None else None,
            'max_tokens': kwargs.get('max_tokens'),
            'model_size': kwargs['model_size'].value,
            'group_id': kwargs.get('group_id'),
            'attribute_extraction': kwargs.get('attribute_extraction'),
            'messages': [[m.role, m.content] for m in messages],
        }])
        if kwargs.get('model') == 'fail':
            raise ValueError('fake client failed: fail')
        if kwargs.get('model') == 'failrt':
            raise RuntimeError('fake client failed: failrt')
        return {'client': self.name, 'model': kwargs.get('model')}

    def set_tracer(self, tracer) -> None:
        self.events.append(['set_tracer', self.name])
        self.tracer_seen = tracer


# The pool that the cases refer to by name. A transport has a name, a client, and optional model ids.
CLIENTS = ['c0', 'c1', 'c2']
TRANSPORTS = [
    {'name': 't0', 'client': 'c0'},
    {'name': 't1', 'client': 'c1'},
    {'name': 't2', 'client': 'c2'},
    {'name': 't3', 'client': 'c0'},  # the client of t0, another transport object
]
MODELS = [
    {'name': 'm0', 'transport': 't0', 'id': 'a'},
    {'name': 'm1', 'transport': 't0', 'id': 'b'},
    {'name': 'm2', 'transport': 't1', 'id': 'c'},
    {'name': 'm3', 'transport': 't2', 'id': 'd'},
    {'name': 'm4', 'transport': 't3', 'id': 'e'},
    {'name': 'm5', 'transport': 't0', 'id': 'a', 'max_tokens': 77},
    {'name': 'm6', 'transport': 't1', 'id': 'c', 'overrides': {'extract_nodes': {'extract_message': 'k1'}}},
    {'name': 'm7', 'transport': 't3', 'id': 'f', 'max_tokens': 5,
     'overrides': {'dedupe_edges': {'resolve_edge': 'k2'}, 'eval': {'qa_prompt': 'RAISE'}}},
    {'name': 'm8', 'transport': 't2', 'id': 'd',
     'overrides': {'extract_nodes': {'extract_message': 'k3', 'extract_attributes': 'k4'}}},
    {'name': 'm9', 'transport': 't2', 'id': 'fail'},
    {'name': 'm10', 'transport': 't0', 'id': 'failrt'},
]
MODEL_NAMES = [m['name'] for m in MODELS]
OVERRIDE_MARKERS = ['o0', 'o1', 'o2', 'o3', 'o4', 'o5', 'RAISE']


def context_text(context) -> str:
    return str(context.get('k'))


def marker_builder(marker: str):
    def build(context, _marker=marker):
        if _marker == 'RAISE':
            raise RuntimeError('builder raised')
        return ChatPrompt(
            system=SystemMessage(content=f'OVR {_marker} {context_text(context)}'),
            user=UserMessage(content=f'OVRU {_marker}'),
        )
    build.marker = marker  # type: ignore[attr-defined]
    return build


def library_builder(name: str):
    def build(context, _name=name):
        return ChatPrompt(
            system=SystemMessage(content=f'LIB {_name} {context_text(context)}'),
            user=UserMessage(content=f'LIBU {_name}'),
        )
    return build


def marker_library():
    return create_prompt_library({
        group: {name: library_builder(f'{group}.{name}') for name in names} for group, names in PROMPT_GROUPS.items()
    })


class Pool:
    def __init__(self) -> None:
        self.events: list = []
        self.clients = {name: FakeLLM(name, self.events) for name in CLIENTS}
        self.transports = {t['name']: LLMTransport(self.clients[t['client']]) for t in TRANSPORTS}
        self.models = {}
        self.markers: dict[str, object] = {}
        for m in MODELS:
            overrides = build_overrides(m['overrides'], self) if 'overrides' in m else None
            self.models[m['name']] = self.transports[m['transport']].model(
                m['id'], prompt_overrides=overrides, max_tokens=m.get('max_tokens'))
        self.model_names = {id(model): name for name, model in self.models.items()}
        self.transport_names = {id(t): name for name, t in self.transports.items()}

    def builder(self, marker: str):
        if marker not in self.markers:
            self.markers[marker] = marker_builder(marker)
        return self.markers[marker]


def group_class(parent, name: str):
    return getattr(parent, ''.join(part.capitalize() for part in name.split('_')))


def build_overrides(spec: dict, pool: Pool) -> LLMPromptOverrides:
    kwargs = {}
    for group, methods in spec.items():
        kwargs[group] = group_class(LLMPromptOverrides, group)(**{n: pool.builder(v) for n, v in methods.items()})
    return LLMPromptOverrides(**kwargs)


def build_routes(spec: dict, pool: Pool) -> PromptRoutes:
    kwargs = {}
    for group, value in spec.items():
        if isinstance(value, str):
            kwargs[group] = pool.models[value]
        else:
            kwargs[group] = group_class(PromptRoutes, group)(**{n: pool.models[v] for n, v in value.items()})
    return PromptRoutes(**kwargs)


def build_runtime(spec: dict, pool: Pool) -> LLMRuntime:
    kwargs = {}
    if 'routes' in spec:
        kwargs['routes'] = build_routes(spec['routes'], pool)
    if 'overrides' in spec:
        kwargs['prompt_overrides'] = build_overrides(spec['overrides'], pool)
    if spec['library'] == 'marker':
        kwargs['library'] = marker_library()
    elif spec['library'] == 'default_passed':
        kwargs['library'] = create_prompt_library()
    return LLMRuntime(pool.models[spec['main']], **kwargs)


def rand_routes_spec() -> dict:
    spec: dict = {}
    for group in rng.sample(GROUPS, rng.randint(0, len(GROUPS))):
        if rng.random() < 0.4:
            spec[group] = rng.choice(MODEL_NAMES)
            continue
        names = rng.sample(PROMPT_GROUPS[group], rng.randint(0, len(PROMPT_GROUPS[group])))
        nested = {name: rng.choice(MODEL_NAMES) for name in names}
        if rng.random() < 0.5:
            nested['default'] = rng.choice(MODEL_NAMES)
        keys = list(nested)
        rng.shuffle(keys)
        spec[group] = {key: nested[key] for key in keys}
    return spec


def rand_overrides_spec() -> dict:
    spec: dict = {}
    for group in rng.sample(GROUPS, rng.randint(0, len(GROUPS))):
        names = rng.sample(PROMPT_GROUPS[group], rng.randint(0, len(PROMPT_GROUPS[group])))
        spec[group] = {name: rng.choice(OVERRIDE_MARKERS) for name in names}
    return spec


def rand_spec() -> dict:
    spec: dict = {'main': rng.choice(MODEL_NAMES), 'library': rng.choice(['marker', 'marker', 'default', 'default_passed'])}
    if rng.random() < 0.85:
        spec['routes'] = rand_routes_spec()
    if rng.random() < 0.7:
        spec['overrides'] = rand_overrides_spec()
    return spec


def error_of(exc: BaseException) -> list:
    return [type(exc).__name__, str(exc)]


def describe_chat(chat) -> str:
    return f'{chat.system.content}|{chat.user.content}'


def attempt(function) -> dict:
    try:
        return {'ok': function()}
    except Exception as exc:  # noqa: BLE001
        return {'err': error_of(exc)}


def structure(runtime: LLMRuntime, pool: Pool) -> dict:
    return {
        'transports': [pool.transport_names[id(t)] for t in runtime.transports],
        'model': pool.model_names[id(runtime.model)],
        'client': runtime.client.name,
        'routes': [[key, pool.model_names[id(model)]] for key, model in runtime.routes.items()],
        'overrides': [[key, function.marker] for key, function in runtime.prompt_overrides.items()],
    }


def record_constructor() -> list:
    specs = [{'main': name, 'library': 'marker'} for name in MODEL_NAMES]
    specs.append({'main': 'm0', 'library': 'default', 'routes': {}, 'overrides': {}})
    # The default model on a transport that a route also uses, in each place of the routes.
    specs += [
        {'main': 'm2', 'library': 'marker', 'routes': {'extract_nodes': 'm0', 'dedupe_edges': 'm2', 'eval': 'm3'}},
        {'main': 'm0', 'library': 'marker', 'routes': {'extract_nodes': 'm4', 'dedupe_edges': 'm0'}},
        {'main': 'm0', 'library': 'marker', 'routes': {'eval': {'default': 'm4', 'qa_prompt': 'm0'}}},
        {'main': 'm3', 'library': 'marker', 'routes': {'eval': {'default': 'm4'}, 'extract_edges': {'edge': 'm2'}}},
        {'main': 'm4', 'library': 'marker', 'routes': {'extract_nodes': 'm0', 'dedupe_edges': 'm2', 'eval': 'm3'}},
    ]
    for _ in range(100):
        specs.append(rand_spec())
    cases = []
    for spec in specs:
        pool = Pool()
        runtime = build_runtime(spec, pool)
        case = {'spec': spec, 'r': structure(runtime, pool)}
        # set_tracer: the order of the calls, with the events of one run.
        tracer = object()
        mark = len(pool.events)
        runtime.set_tracer(tracer)
        calls = [event[1] for event in pool.events[mark:]]
        case['set_tracer'] = {
            'calls': calls,
            'reached': {name: getattr(client, 'tracer_seen', None) is tracer for name, client in pool.clients.items()},
        }
        cases.append(case)
    return cases


def record_resolve() -> list:
    cases = []
    specs = [rand_spec() for _ in range(22)]
    specs = [dict(spec, library='marker') for spec in specs]
    specs.append({'main': 'm0', 'library': 'marker', 'routes': {'extract_nodes': 'm5', 'eval': {'default': 'm4'}},
                  'overrides': {'extract_nodes': {'extract_message': 'o1'}, 'eval': {'qa_prompt': 'o2'}}})
    for spec in specs:
        pool = Pool()
        runtime = build_runtime(spec, pool)
        rows = []
        for name in NAMES + ODD_NAMES:
            model = runtime.resolve_model(name)
            row = {'name': name, 'model': pool.model_names[id(model)], 'builders': {}}
            for label, candidate in [('own', model), ('m6', pool.models['m6']),
                                     ('m7', pool.models['m7']), ('m8', pool.models['m8'])]:
                row['builders'][label] = attempt(
                    lambda candidate=candidate, name=name: describe_chat(runtime.resolve_builder(name, candidate)({'k': 'v'})))
            rows.append(row)
        cases.append({'spec': spec, 'rows': rows})
    return cases


def random_text() -> str:
    pool = list('abc XYZ019-_,.:') + ['é', '日本', '\U0001F600', 'ß', '\n', '"', "'"]
    return ''.join(rng.choice(pool) for _ in range(rng.randint(0, 12)))


def rand_call(library: str) -> dict:
    """One call of complete: a prompt name, a context, and the keyword arguments."""
    if library == 'default':
        name = rng.choice(list(DEFAULT_CONTEXTS))
        context = json.loads(json.dumps(DEFAULT_CONTEXTS[name]))
        if name == 'extract_nodes.extract_message':
            context['episode_content'] = random_text()
        else:
            context['new_edge'] = random_text()
    else:
        name = rng.choice(NAMES + (['bogus.name', 'extract_nodes', ''] if rng.random() < 0.08 else []))
        context = {'k': random_text()}
    call: dict = {'name': name, 'context': context}
    spec = BUILTIN_PROMPT_SPECS.get(name)
    roll = rng.random()
    if spec is not None and spec.dynamic_schema:
        call['response_model'] = 'PersonAttrs' if roll < 0.7 else ('OtherModel' if roll < 0.85 else None)
    elif spec is not None:
        call['response_model'] = 'same' if roll < 0.3 else ('OtherModel' if roll < 0.4 else None)
    if rng.random() < 0.5:
        call['attribute_extraction'] = rng.random() < 0.5
    if rng.random() < 0.5:
        call['group_id'] = rng.choice([None, 'g1', 'é', ''])
    if rng.random() < 0.5:
        call['max_tokens'] = rng.choice([None, 1, 500, 100000])
    if rng.random() < 0.5:
        call['model_size'] = rng.choice(['medium', 'small'])
    return call


def run_call(runtime: LLMRuntime, call: dict):
    kwargs = {}
    name = call['name']
    if 'response_model' in call:
        value = call['response_model']
        if value == 'same':
            kwargs['response_model'] = BUILTIN_PROMPT_SPECS[name].response_model
        elif value is not None:
            kwargs['response_model'] = CUSTOM_MODELS[value]
    for key in ('attribute_extraction', 'group_id', 'max_tokens'):
        if key in call:
            kwargs[key] = call[key]
    if 'model_size' in call:
        kwargs['model_size'] = ModelSize(call['model_size'])
    return runtime.complete(name, call['context'], **kwargs)


async def outcome(runtime: LLMRuntime, call: dict) -> dict:
    try:
        return {'ok': await run_call(runtime, call)}
    except Exception as exc:  # noqa: BLE001
        return {'err': error_of(exc)}


def record_complete() -> list:
    cases = []
    # Each prompt name and each odd name with the defaults of every argument, and a dynamic name with a call-site model.
    for name in NAMES + ODD_NAMES:
        for extra in ({}, {'response_model': 'PersonAttrs'}):
            spec = {'main': 'm0', 'library': 'marker'}
            pool = Pool()
            LOG_LINES.clear()
            runtime = build_runtime(spec, pool)
            call = {'name': name, 'context': {'k': 'ctx'}, **extra}
            result = asyncio.run(outcome(runtime, call))
            cases.append({'spec': spec, 'call': call, 'events': pool.events + LOG_LINES, 'r': result})
    for index in range(260):
        spec = rand_spec()
        spec['library'] = 'default' if index % 8 == 7 else rng.choice(['marker', 'marker', 'default_passed'])
        pool = Pool()
        LOG_LINES.clear()
        runtime = build_runtime(spec, pool)
        call = rand_call('marker' if spec['library'] == 'marker' else 'default')
        result = asyncio.run(outcome(runtime, call))
        cases.append({'spec': spec, 'call': call, 'events': pool.events + LOG_LINES, 'r': result})
    return cases


def record_concurrent() -> list:
    cases = []
    for _ in range(60):
        spec = rand_spec()
        spec['library'] = 'marker'
        pool = Pool()
        LOG_LINES.clear()
        runtime = build_runtime(spec, pool)
        calls = [rand_call('marker') for _ in range(rng.randint(2, 4))]

        async def run():
            return await asyncio.gather(*[outcome(runtime, call) for call in calls])

        results = asyncio.run(run())
        cases.append({'spec': spec, 'calls': calls, 'events': pool.events + LOG_LINES, 'r': results})
    return cases


def record_wrap_builder() -> list:
    cases = []

    def chat(text):
        return ChatPrompt(system=SystemMessage(content=text), user=UserMessage(content='u' + text))

    seen: list = []

    def good(context):
        seen.append(dict(context))
        return chat('a' + str(context.get('k')))

    for name in ['extract_nodes.extract_message', 'x', '', 'é.y']:
        seen.clear()
        wrapped = _wrap_builder(good, name)
        out = wrapped({'k': 'v1'})
        cases.append({'kind': 'good', 'name': name, 'context': {'k': 'v1'}, 'seen': list(seen),
                      'r': {'ok': describe_chat(out)}, 'cs': True})
    for kind, make in [('list', lambda ctx: []), ('str', lambda ctx: 'text'), ('none', lambda ctx: None),
                       ('dict', lambda ctx: {}), ('int', lambda ctx: 3)]:
        for name in ['extract_nodes.extract_message', 'q']:
            case = {'kind': kind, 'name': name, 'cs': False}
            case['r'] = attempt(lambda make=make, name=name: _wrap_builder(make, name)({'k': 'v'}))
            cases.append(case)
    case = {'kind': 'raises', 'name': 'x', 'cs': True}

    def boom(context):
        raise RuntimeError('inner')

    case['r'] = attempt(lambda: _wrap_builder(boom, 'x')({'k': 'v'}))
    cases.append(case)
    return cases


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    logger = logging.getLogger('graphiti_core')
    logger.setLevel(logging.DEBUG)
    logger.addHandler(LogHandler())
    logger.propagate = False

    data = {
        'python': '.'.join(str(p) for p in sys.version_info[:3]),
        'source': 'graphiti_core/llm_client/llm_runtime.py',
        'names': NAMES,
        'odd_names': ODD_NAMES,
        'clients': CLIENTS,
        'transports': TRANSPORTS,
        'models': MODELS,
        'constructor': record_constructor(),
        'resolve': record_resolve(),
        'complete': record_complete(),
        'concurrent': record_concurrent(),
        'wrap_builder': record_wrap_builder(),
    }
    text = json.dumps(data, ensure_ascii=True, separators=(',', ':')) + '\n'
    count = sum(len(data[k]) for k in ('constructor', 'resolve', 'complete', 'concurrent', 'wrap_builder'))
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
