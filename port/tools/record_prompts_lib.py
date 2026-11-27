"""Records the results of graphiti_core/prompts/lib.py.

The judge has 58 cases for this module. This file gives the evaluation tests the call of each prompt function of
each group through both libraries (`prompt_library` and `default_chat_prompt_library`), the unknown group names and
function names, `create_prompt_library`, `ensure_prompt_library_wrapped`, the two proxy classes, each branch of
`validate_prompt_library` with its error text, `resolve_response_model` for each prompt name, and each constant
(port/RULES.md, R25).

    GRAPHITI_UPSTREAM=<upstream checkout> <python 3.12> port/tools/record_prompts_lib.py
    GRAPHITI_UPSTREAM=<upstream checkout> <python 3.12> port/tools/record_prompts_lib.py --check

It writes tests/Port.Evaluation/values/prompts_lib.json. --check writes nothing and exits 1 if the file differs
from what the run would write. The tool needs Python 3.12 and the pinned upstream (the environment variable
GRAPHITI_UPSTREAM, or .upstream/graphiti). It changes nothing there. Nothing here uses the network. The random
seed is fixed. A payload longer than 300 characters is stored as zlib and base64 to keep the file small.

Each case has `fn`, `args` (a JSON value), and `expect`: a payload, a JSON value that the test builds again from
the result of the port. An exception inside a payload is {"exc": <class name>} and, for a ValueError or a
TypeError, also {"msg": <text>}; for an AttributeError, {"name": <the attribute name>}. A case is left out when the
port cannot hold its input by type: an override that is a list or a text, an override that returns a list or a text,
and a specs value that is not a mapping or has an entry that is not a PromptSpec. A null override and an override that
returns null the port can hold, so the cases have them (the test compares the exception type and the full text). The
judge accepts the first three by name (R22).
"""
from __future__ import annotations

import base64
import json
import os
import random
import sys
import zlib
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'prompts_lib.json'
sys.path.insert(0, str(UPSTREAM))

import pydantic  # noqa: E402

from graphiti_core.prompts import dedupe_edges as _de  # noqa: E402
from graphiti_core.prompts import dedupe_nodes as _dn  # noqa: E402
from graphiti_core.prompts import eval as _ev  # noqa: E402
from graphiti_core.prompts import extract_edges as _ee  # noqa: E402
from graphiti_core.prompts import extract_nodes as _en  # noqa: E402
from graphiti_core.prompts import extract_nodes_and_edges as _ene  # noqa: E402
from graphiti_core.prompts import lib as L  # noqa: E402
from graphiti_core.prompts import summarize_nodes as _sn  # noqa: E402
from graphiti_core.prompts import summarize_sagas as _ss  # noqa: E402
from graphiti_core.prompts.prompt_helpers import DO_NOT_ESCAPE_UNICODE  # noqa: E402
from graphiti_core.prompts.models import (  # noqa: E402
    ChatPrompt,
    Message,
    PromptSpec,
    SystemMessage,
    UserMessage,
)

R = random.Random(20261127)

# The context of tests/prompts/test_legacy_prompt_compatibility.py. It fits each of the 25 prompt functions.
PROMPT_CONTEXT = {
    'answer': 'Alice knows Bob.',
    'attributes': {'role': 'project lead'},
    'baseline': 'Bob is known by Alice.',
    'candidate': 'Alice knows Bob.',
    'custom_extraction_instructions': 'Use only facts stated in the episode.',
    'edge_invalidation_candidates': '[]',
    'edge_types': [{'name': 'KNOWS', 'description': 'One person knows another.'}],
    'entities': [{'name': 'Alice', 'type': 'Person'}],
    'entity_summaries': [{'name': 'Alice', 'summary': 'Project lead.'}],
    'entity_type_description': {'Person': 'A person mentioned in an episode.'},
    'entity_type_descriptions': {'Person': 'A person mentioned in an episode.'},
    'entity_types': 'Person: A person mentioned in an episode.',
    'episode_content': 'Alice met Bob at the office on January 2.',
    'episodes': ['Alice met Bob on January 2.'],
    'existing_attributes': {'since': '2024'},
    'existing_edges': '[{"fact": "Alice knows Bob."}]',
    'existing_nodes': [{'name': 'Alice', 'summary': 'Project lead.'}],
    'existing_summary': 'Alice is preparing a project launch.',
    'extracted_entities': '[{"name": "Alice", "type": "Person"}]',
    'extracted_node': {'name': 'Alice', 'type': 'Person'},
    'extracted_nodes': [
        {'name': 'Alice', 'type': 'Person'},
        {'name': 'Bob', 'type': 'Person'},
    ],
    'fact': 'Alice knows Bob.',
    'facts': [{'fact': 'Alice knows Bob.'}],
    'message': 'Alice knows Bob.',
    'new_edge': '{"fact": "Alice knows Bob."}',
    'node': 'Alice',
    'node_name': 'Alice',
    'node_summaries': [{'name': 'Alice', 'summary': 'Project lead.'}],
    'node_summary': 'Alice leads the project.',
    'nodes': [{'name': 'Alice', 'summary': 'Project lead.'}],
    'previous_episodes': [{'content': 'Alice met Bob.', 'reference_time': '2025-01-02T00:00:00Z'}],
    'previous_messages': 'User: Who knows Bob?',
    'query': 'Who knows Bob?',
    'reference_time': '2025-01-03T00:00:00Z',
    'response': 'Alice knows Bob.',
    'saga_name': "Alice's project launch",
    'source_description': 'A chat message',
    'summary': 'Alice leads the project.',
}
UNICODE_CONTEXT = {**PROMPT_CONTEXT, 'episode_content': 'Café é 日本語 😀 "quoted" {braces}', 'x': 'ctx-x ☃'}
CTX_X = {**PROMPT_CONTEXT, 'x': 'ctx-x ☃'}

MODELS = {}
for _mod in (_en, _dn, _ee, _ene, _de, _sn, _ss, _ev):
    for _name, _obj in vars(_mod).items():
        if isinstance(_obj, type) and issubclass(_obj, pydantic.BaseModel) and _obj.__module__ == _mod.__name__:
            assert _name not in MODELS, _name
            MODELS[_name] = _obj

SPECIAL = ['', ' ', '\n', '\t', '"', "'", '{', '}', '\\', 'é', '日本語', '😀', '\x00', ' ', '☃']


def rstr(maxlen=4):
    return ''.join(R.choice(SPECIAL + list('abcXYZ019_.')) for _ in range(R.randint(0, maxlen)))


# ---- encoding ----

def enc_result(r):
    if isinstance(r, list) and all(isinstance(x, Message) for x in r):
        return [[x.role, x.content] for x in r]
    if isinstance(r, ChatPrompt):
        return {'system': r.system.content, 'user': r.user.content, 'srole': r.system.role, 'urole': r.user.role}
    return r


def exc_payload(e):
    out = {'exc': type(e).__name__}
    if isinstance(e, (ValueError, TypeError)):
        out['msg'] = str(e)
    if isinstance(e, AttributeError):
        out['name'] = e.name
    return out


def attempt(fn):
    try:
        return enc_result(fn())
    except Exception as e:  # noqa: BLE001
        return exc_payload(e)


def pack(payload):
    text = json.dumps(payload, ensure_ascii=True)
    if len(text) > 300:
        return {'z': base64.b64encode(zlib.compress(text.encode('ascii'), 9)).decode('ascii')}
    return {'text': text}


cases = []


def add(fn, args, payload):
    cases.append({'id': f'{fn}#{len(cases)}', 'fn': fn, 'args': args, 'expect': pack(payload)})


def probe(lib, group, function, ctx):
    try:
        grp = getattr(lib, group)
    except AttributeError:
        return {'missing': 'group'}
    try:
        fn = getattr(grp, function)
    except AttributeError:
        return {'missing': 'function'}
    if not callable(fn):
        return {'missing': 'callable'}
    return attempt(lambda: fn(ctx))


ALL_FUNCTIONS = [(g, f) for g, fns in L.PROMPT_GROUPS.items() for f in fns]

# ---- the call of each prompt function through the two libraries ----
for lib_name, lib in [('wrapper', L.prompt_library), ('default', L.default_chat_prompt_library)]:
    for g, f in ALL_FUNCTIONS:
        for ctx_name, ctx in [('prompt', PROMPT_CONTEXT), ('empty', {})]:
            add('lib_call', {'lib': lib_name, 'group': g, 'function': f, 'ctx': ctx}, probe(lib, g, f, ctx))
        if (g, f) in [('extract_nodes', 'extract_message'), ('dedupe_nodes', 'nodes'), ('eval', 'qa_prompt')]:
            add('lib_call', {'lib': lib_name, 'group': g, 'function': f, 'ctx': UNICODE_CONTEXT},
                probe(lib, g, f, UNICODE_CONTEXT))

# unknown names go through the default library only: the wrapper has no lookup by name (R15)
for group, function in [
    ('nope', 'extract_message'), ('', 'extract_message'), ('Extract_Nodes', 'extract_message'),
    ('extract_nodes', 'nope'), ('extract_nodes', ''), ('extract_nodes', 'node'), ('dedupe_nodes', 'extract_message'),
    ('eval', 'edge'), ('summarize_sagas', 'summarize_pair'), ('extract_edges', 'extract_json'),
]:
    add('lib_call', {'lib': 'default', 'group': group, 'function': function, 'ctx': PROMPT_CONTEXT},
        probe(L.default_chat_prompt_library, group, function, PROMPT_CONTEXT))

# a wrapper that holds some of the versions: the others are missing attributes
PARTIALS = [
    {'dedupe_nodes': ['node']},
    {'dedupe_nodes': ['node', 'nodes'], 'extract_nodes': ['extract_message']},
    {'eval': ['qa_prompt', 'eval_prompt']},
    {'extract_edges': ['edge', 'extract_attributes'], 'extract_nodes': ['extract_attributes']},
    {},
]
for partial in PARTIALS:
    impl = {g: {f: L.PROMPT_LIBRARY_IMPL[g][f] for f in fns} for g, fns in partial.items()}
    wrapper = L.PromptLibraryWrapper(impl)
    probes = [[g, f] for g, f in ALL_FUNCTIONS]
    add('wrapper_partial', {'versions': partial, 'ctx': PROMPT_CONTEXT},
        [probe(wrapper, g, f, PROMPT_CONTEXT) for g, f in probes])

# ---- VersionWrapper ----
ROLES = ['system', 'user', 'assistant', 'system', 'tool', '', 'System', 'SYSTEM ']
for i in range(24):
    messages = [[R.choice(ROLES), rstr(6)] for _ in range(R.randint(0, 4))]
    x = rstr(3)

    def func(ctx, messages=messages):
        return [Message(role=r, content=c + ctx['x']) for r, c in messages]

    add('version_wrapper', {'messages': messages, 'ctx': {'x': x}}, attempt(lambda: L.VersionWrapper(func)({'x': x})))
add('version_wrapper', {'messages': [['system', 'a'], ['user', 'b']], 'ctx': {}},
    attempt(lambda: L.VersionWrapper(lambda ctx: [Message(role='system', content='a' + ctx['x'])])({})))


# ---- create_prompt_library ----
def make_override(spec):
    if spec is None:
        return None
    if spec.get('returns_none'):
        return lambda ctx: None

    def override(ctx):
        return ChatPrompt(system=SystemMessage(content=spec['system']),
                          user=UserMessage(content=spec['user_prefix'] + ctx['x']))
    return override


RETURNS_NONE = {'returns_none': True}


def build_overrides(spec):
    return {g: {f: make_override(o) for f, o in fns.items()} for g, fns in spec.items()}


def ov(system=None, user_prefix=None):
    return {'system': rstr(5) if system is None else system, 'user_prefix': rstr(3) if user_prefix is None else user_prefix}


CREATE_CASES = [
    None,
    {},
    {'extract_nodes': {'extract_message': ov('custom system', 'custom user ')}},
    {'extract_nodes': {'extract_message': ov('Café é 日本語 😀', 'ü ')}},
    {'dedupe_nodes': {'node': ov(), 'nodes': ov()}, 'eval': {'qa_prompt': ov()}},
    {'extract_nodes': {}, 'eval': {'eval_prompt': ov()}},
    {'extract_nodes': {}},
    {'summarize_sagas': {'summarize_saga': ov()}},
    {'extract_nodes_and_edges': {'extract_message': ov()}, 'dedupe_edges': {'resolve_edge': ov()},
     'summarize_nodes': {'summarize_pair': ov(), 'summarize_context': ov(), 'summary_description': ov()}},
    {'extract_edges': {'edge': ov(), 'extract_attributes': ov(), 'extract_timestamps': ov(),
                       'extract_timestamps_batch': ov()}},
    {'nope': {'extract_message': ov()}},
    {'extract_nodes': {'nope': ov()}},
    {'extract_nodes': {'node': ov()}},
    {'eval': {'edge': ov()}},
    {'extract_nodes': {'extract_message': ov()}, 'nope': {}},
    {'nope': {}},
    {'': {'': ov()}},
    {'extract_nodes': {'extract_message': ov()}, 'dedupe_nodes': {'nope': ov()}},
    {'nope': {'extract_message': ov()}, 'extract_nodes': {'nope': ov()}},
    # the order of the checks decides the error (the unknown function comes before the unknown group)
    {'extract_nodes': {'nope': ov()}, 'nope': {'x': ov()}},
    # an override that is None, and one that returns None
    {'extract_nodes': {'extract_message': None}},
    {'extract_nodes': {'nope': ov(), 'extract_message': None}},
    {'extract_nodes': {'extract_message': None, 'nope': ov()}},
    {'dedupe_nodes': {'node': ov()}, 'eval': {'qa_prompt': None}},
    {'extract_nodes': {'extract_message': RETURNS_NONE}},
    {'extract_nodes': {'extract_message': RETURNS_NONE, 'extract_text': ov()}, 'eval': {'qa_prompt': RETURNS_NONE}},
]
for overrides in CREATE_CASES:
    probes = [['extract_nodes', 'extract_message'], ['extract_nodes', 'extract_text'], ['extract_edges', 'edge'],
              ['dedupe_nodes', 'node'], ['dedupe_nodes', 'nodes'], ['eval', 'qa_prompt'], ['eval', 'eval_prompt'],
              ['summarize_sagas', 'summarize_saga'], ['summarize_nodes', 'summarize_pair'],
              ['extract_nodes', 'nope'], ['nope', 'extract_message']]
    try:
        library = L.create_prompt_library(None if overrides is None else build_overrides(overrides))
    except Exception as e:  # noqa: BLE001
        add('create', {'overrides': overrides, 'ctx': CTX_X}, exc_payload(e))
        continue
    add('create', {'overrides': overrides, 'ctx': CTX_X}, {
        'type': type(library).__name__,
        'specs_is_builtin': library.specs is L.BUILTIN_PROMPT_SPECS,
        'probes': [probe(library, g, f, CTX_X) for g, f in probes],
    })


# ---- a hand-built library, for validate_prompt_library, ensure_prompt_library_wrapped, get_prompt_builder ----
RAW = ChatPrompt(system=SystemMessage(content='hand-built system'), user=UserMessage(content='hand-built user'))


def raw(_context):
    return RAW


def raw_none(_context):
    return None


def build_specs(mods):
    d = {n: PromptSpec(name=n, response_model=s.response_model, dynamic_schema=s.dynamic_schema)
         for n, s in L.BUILTIN_PROMPT_SPECS.items()}
    for mod in mods:
        name = mod['name']
        old = d.get(name)
        if mod['mode'] == 'drop':
            del d[name]
        elif mod['mode'] == 'dyn_flip':
            d[name] = PromptSpec(name=name, response_model=old.response_model, dynamic_schema=not old.dynamic_schema)
        elif mod['mode'] == 'none':
            d[name] = None
        elif mod['mode'] == 'model_swap':
            d[name] = PromptSpec(name=name, response_model=MODELS[mod['to']] if mod['to'] else None,
                                 dynamic_schema=old.dynamic_schema)
        else:
            raise ValueError(mod['mode'])
    return d


def build_fake(spec):
    ns = {}
    for g, fns in L.PROMPT_GROUPS.items():
        if g in spec.get('absent', []):
            continue
        if g in spec.get('notgroup', []):
            ns[g] = 'not a group'
            continue
        members = {}
        for f in fns:
            if [g, f] in spec.get('drop', []):
                continue
            members[f] = 'not callable' if [g, f] in spec.get('notcallable', []) else (
                raw_none if spec.get('returns_none') else raw)
        ns[g] = SimpleNamespace(**members)
    if spec.get('specs') is not None:
        ns['specs'] = build_specs(spec['specs'])
    return SimpleNamespace(**ns)


def flip(name):
    return {'name': name, 'mode': 'dyn_flip'}


def swap(name, to):
    return {'name': name, 'mode': 'model_swap', 'to': to}


def drop(name):
    return {'name': name, 'mode': 'drop'}


def none(name):
    return {'name': name, 'mode': 'none'}


FAKES = [
    {},
    {'specs': []},
    {'specs': [drop('extract_nodes.extract_message')]},
    {'specs': [drop('eval.eval_add_episode_results')]},
    {'specs': [drop('dedupe_nodes.node_list'), drop('eval.qa_prompt')]},
    {'specs': [flip('extract_nodes.extract_attributes')]},
    {'specs': [flip('extract_nodes.extract_message')]},
    {'specs': [flip('eval.eval_prompt'), flip('extract_edges.extract_attributes')]},
    {'specs': [swap('extract_nodes.extract_message', 'Summary')]},
    {'specs': [swap('extract_nodes.extract_message', None)]},
    {'specs': [swap('dedupe_nodes.node_list', 'NodeDuplicate')]},
    {'specs': [swap('extract_nodes.extract_attributes', 'Summary')]},
    {'specs': [swap('summarize_sagas.summarize_saga', 'Summary'), flip('eval.qa_prompt')]},
    {'specs': [flip('eval.qa_prompt'), swap('summarize_sagas.summarize_saga', 'Summary')]},
    {'specs': [drop('eval.qa_prompt'), flip('extract_nodes.extract_message')]},
    {'absent': ['extract_nodes']},
    {'absent': ['eval']},
    {'absent': ['summarize_sagas', 'dedupe_nodes']},
    {'absent': ['dedupe_nodes'], 'drop': [['extract_nodes', 'extract_json']]},
    {'notgroup': ['extract_edges']},
    {'notgroup': ['eval'], 'absent': ['dedupe_nodes']},
    {'drop': [['extract_nodes', 'extract_summary']]},
    {'drop': [['eval', 'eval_add_episode_results']]},
    {'drop': [['summarize_nodes', 'summary_description'], ['extract_nodes', 'extract_json']]},
    {'notcallable': [['extract_nodes', 'extract_message']]},
    {'notcallable': [['dedupe_edges', 'resolve_edge']]},
    {'notcallable': [['summarize_sagas', 'summarize_saga']], 'drop': [['extract_nodes', 'extract_json']]},
    {'notcallable': [['dedupe_nodes', 'nodes']], 'specs': [drop('eval.qa_prompt')]},
    {'drop': [['eval', 'qa_prompt']], 'specs': [drop('extract_nodes.extract_message')]},
    {'absent': ['extract_nodes'], 'notcallable': [['dedupe_nodes', 'node']]},
    # the order of the checks decides the error: one name with both faults, and two faults in one group
    {'specs': [flip('eval.qa_prompt'), swap('eval.qa_prompt', None)]},
    {'specs': [swap('summarize_sagas.summarize_saga', 'Summary'), flip('summarize_sagas.summarize_saga')]},
    {'notcallable': [['extract_nodes', 'extract_message']], 'drop': [['extract_nodes', 'extract_json']]},
    {'drop': [['extract_nodes', 'extract_message']], 'notcallable': [['extract_nodes', 'extract_json']]},
    # an entry that is None, and a library whose functions return None
    {'specs': [none('eval.qa_prompt')]},
    {'specs': [none('extract_nodes.extract_message'), drop('eval.qa_prompt')]},
    {'specs': [drop('extract_nodes.extract_message'), none('eval.qa_prompt')]},
    {'returns_none': True},
]
for fake_spec in FAKES:
    fake = build_fake(fake_spec)
    add('validate', {'fake': fake_spec}, attempt(lambda: L.validate_prompt_library(fake)))
    try:
        wrapped = L.ensure_prompt_library_wrapped(fake)
    except Exception as e:  # noqa: BLE001
        add('ensure_wrapped', {'fake': fake_spec, 'ctx': {}}, exc_payload(e))
    else:
        add('ensure_wrapped', {'fake': fake_spec, 'ctx': {}}, {
            'type': type(wrapped).__name__,
            'same': wrapped is fake,
            'specs_is_builtin': wrapped.specs is L.BUILTIN_PROMPT_SPECS,
            'specs_keys': list(wrapped.specs),
            'probes': [probe(wrapped, g, f, {}) for g, f in [('extract_nodes', 'extract_message'), ('eval', 'qa_prompt'),
                                                             ('nope', 'x'), ('eval', 'nope')]],
        })
    for name in ['extract_nodes.extract_message', 'eval.eval_add_episode_results', 'summarize_sagas.summarize_saga',
                 'nope.x', 'nodot', 'extract_nodes.nope']:
        add('get_builder', {'lib': {'fake': fake_spec}, 'name': name, 'ctx': {}},
            attempt(lambda: L.get_prompt_builder(fake, name)({})))

for lib_name, lib in [('default', L.default_chat_prompt_library),
                      ('created', L.create_prompt_library({'extract_nodes': {'extract_message': make_override(ov('S', 'U '))}})),
                      ('created_empty', L.create_prompt_library({'eval': {}}))]:
    add('validate', {'lib': lib_name}, attempt(lambda: L.validate_prompt_library(lib)))
    wrapped = L.ensure_prompt_library_wrapped(lib)
    add('ensure_wrapped', {'lib': lib_name, 'ctx': CTX_X}, {
        'type': type(wrapped).__name__,
        'same': wrapped is lib,
        'specs_is_builtin': wrapped.specs is L.BUILTIN_PROMPT_SPECS,
        'specs_keys': list(wrapped.specs),
        'probes': [probe(wrapped, g, f, CTX_X) for g, f in [('extract_nodes', 'extract_message'), ('eval', 'qa_prompt')]],
    })

# ---- get_prompt_builder on the default library and on a created library ----
BUILDER_NAMES = (
    [f'{g}.{f}' for g, f in ALL_FUNCTIONS]
    + ['nodot', '', '.', '.x', 'x.', 'nope.nope', 'extract_nodes.', 'extract_nodes.extract_message.extra', 'extract_nodes..',
       ' extract_nodes.extract_message', 'extract_nodes.extract_message ', 'EXTRACT_NODES.extract_message',
       'extract_nodes.node', 'eval.edge', 'extract_nodes', 'extract_nodes_and_edges.extract_message',
       'extract_nodes_and_edges.extract_json', 'é.é', 'a.b.c']
)
CREATED = L.create_prompt_library({'extract_nodes': {'extract_message': make_override(ov('S2', 'U2 '))},
                                   'eval': {'qa_prompt': make_override(ov('S3', 'U3 '))}})
CREATED_NONE = L.create_prompt_library({'extract_nodes': {'extract_message': make_override(RETURNS_NONE)}})
for lib_name, lib in [('default', L.default_chat_prompt_library), ('created', CREATED), ('created_none', CREATED_NONE)]:
    for name in BUILDER_NAMES:
        add('get_builder', {'lib': lib_name, 'name': name, 'ctx': CTX_X}, attempt(lambda: L.get_prompt_builder(lib, name)(CTX_X)))

# ---- ensure_chat_prompt ----
CP = ChatPrompt(system=SystemMessage(content='sys'), user=UserMessage(content='usr'))
VALUES = {
    'chat_prompt': lambda: CP,
    'list_empty': lambda: [],
    'list_messages': lambda: [Message(role='user', content='x')],
    'list_text': lambda: ['a', 1],
    'str': lambda: 'x',
    'str_empty': lambda: '',
    'none': lambda: None,
    'int': lambda: 5,
    'float': lambda: 1.5,
    'bool': lambda: True,
    'dict': lambda: {'system': 'a'},
    'bytes': lambda: b'ab',
    'message': lambda: Message(role='user', content='x'),
    'system_message': lambda: SystemMessage(content='x'),
}
LABELS = ['lbl', '', 'extract_nodes.extract_message', 'override.node', 'é 日本語 😀', 'a b\n{c}']
for private in [False, True]:
    for kind, make in VALUES.items():
        for label in LABELS if kind in ('list_empty', 'str', 'none', 'chat_prompt') else LABELS[:2]:
            fn = L._ensure_chat_prompt if private else L.ensure_chat_prompt
            value = make()
            add('ensure_chat_prompt', {'private': private, 'kind': kind, 'label': label},
                attempt(lambda: fn(value, label)))

# ---- resolve_response_model ----
SPEC_NAMES = list(L.BUILTIN_PROMPT_SPECS)
for name in SPEC_NAMES + ['', 'nope', 'extract_nodes', 'extract_nodes.', 'EXTRACT_NODES.extract_message',
                          'extract_nodes.extract_message ', 'eval.nope', 'é']:
    add('resolve', {'name': name, 'model': None},
        attempt(lambda: (lambda m: m.__name__ if m else None)(L.resolve_response_model(name))))
    for model_name in ['Summary', 'ExtractedEntities', 'ExtractedEntity']:
        add('resolve', {'name': name, 'model': model_name},
            attempt(lambda: (lambda m: m.__name__ if m else None)(L.resolve_response_model(name, MODELS[model_name]))))
    spec = L.BUILTIN_PROMPT_SPECS.get(name)
    if spec is not None and spec.response_model is not None:
        add('resolve', {'name': name, 'model': spec.response_model.__name__},
            attempt(lambda: (lambda m: m.__name__ if m else None)(L.resolve_response_model(name, spec.response_model))))
        # a new class with the name of the expected model: the test is by identity, not by name
        clone = type(spec.response_model.__name__, (pydantic.BaseModel,), {})
        add('resolve', {'name': name, 'model': spec.response_model.__name__, 'clone': True},
            attempt(lambda: (lambda m: m.__name__ if m else None)(L.resolve_response_model(name, clone))))
for model_name in MODELS:
    for name in ['extract_nodes.extract_attributes', 'extract_edges.extract_attributes', 'dedupe_nodes.node_list',
                 'eval.qa_prompt']:
        add('resolve', {'name': name, 'model': model_name},
            attempt(lambda: (lambda m: m.__name__ if m else None)(L.resolve_response_model(name, MODELS[model_name]))))

# ---- _OverrideGroup ----
for names in [[], ['extract_message'], ['extract_message', 'extract_text'], ['classify_nodes', 'extract_summary']]:
    overrides = {n: make_override(ov()) for n in names}
    override_spec = {n: {'system': 'S-' + n, 'user_prefix': 'U-' + n + ' '} for n in names}
    overrides = {n: make_override(o) for n, o in override_spec.items()}
    group = L._OverrideGroup(L.default_chat_prompt_library.extract_nodes, overrides)
    probe_names = ['extract_message', 'extract_text', 'extract_json', 'classify_nodes', 'extract_attributes', 'nope', '',
                   'node']
    out = []
    for n in probe_names:
        try:
            fn = getattr(group, n)
        except AttributeError:
            out.append({'missing': True})
            continue
        out.append(attempt(lambda: fn(CTX_X)))
    add('override_group', {'overrides': override_spec, 'probes': probe_names, 'ctx': CTX_X}, out)

# ---- _ComposedPromptLibrary ----
for overrides_spec in [{}, {'extract_nodes': {}}, {'extract_nodes': {'extract_message': ov('A', 'B ')}},
                       {'eval': {'qa_prompt': ov('C', 'D ')}, 'dedupe_edges': {}},
                       {'nope': {'x': ov()}}]:
    base = L.DefaultChatPromptLibrary()
    composed = L._ComposedPromptLibrary(base, build_overrides(overrides_spec))
    probe_names = list(L.PROMPT_GROUPS) + ['specs', 'nope', '', 'extract_nodes_']
    out = []
    for n in probe_names:
        try:
            value = getattr(composed, n)
        except AttributeError:
            out.append({'missing': True})
            continue
        if n == 'specs':
            out.append({'specs_is_base': value is base.specs, 'keys': list(value)})
        else:
            out.append({'type': type(value).__name__, 'same_as_base': value is getattr(base, n)})
    add('composed', {'overrides': overrides_spec, 'probes': probe_names}, out)

# ---- _LibraryWithBuiltinSpecs ----
for fake_spec in [{}, {'specs': []}, {'absent': ['eval']}, {'notgroup': ['eval']}]:
    fake = build_fake(fake_spec)
    wrapper = L._LibraryWithBuiltinSpecs(fake)
    out = []
    for n in list(L.PROMPT_GROUPS) + ['specs', 'nope']:
        try:
            value = getattr(wrapper, n)
        except AttributeError:
            out.append({'missing': True})
            continue
        if n == 'specs':
            out.append({'specs_is_builtin': value is L.BUILTIN_PROMPT_SPECS})
        else:
            out.append({'same_as_inner': value is getattr(fake, n)})
    add('with_builtin_specs', {'fake': fake_spec}, out)

# ---- a base group that is None (a ChatPromptLibrary subclass whose group property returns None) ----
def null_group_library(group):
    return type('NullGroupLibrary', (L.DefaultChatPromptLibrary,), {group: property(lambda self: None)})()


for group in ['extract_nodes', 'eval', 'dedupe_edges']:
    fns = L.PROMPT_GROUPS[group]
    for with_override in [False, True]:
        override_spec = {group: {fns[0]: ov('NS', 'NU ')}} if with_override else {}
        library = null_group_library(group)
        payload = {
            'validate': attempt(lambda: L.validate_prompt_library(library)),
            'builder': attempt(lambda: L.get_prompt_builder(library, f'{group}.{fns[0]}')(CTX_X)),
        }
        try:
            composed = L._ComposedPromptLibrary(library, build_overrides(override_spec))
        except Exception as e:  # noqa: BLE001
            payload['composed'] = exc_payload(e)
        else:
            payload['composed'] = {
                'group_is_none': getattr(composed, group) is None,
                'probes': [probe(composed, group, f, CTX_X) for f in fns],
                'validate': attempt(lambda: L.validate_prompt_library(composed)),
                'builder': attempt(lambda: L.get_prompt_builder(composed, f'{group}.{fns[0]}')(CTX_X)),
            }
        add('null_group', {'group': group, 'overrides': override_spec, 'ctx': CTX_X}, payload)


# ---- object identity ----
class OtherSpecsLibrary(L.DefaultChatPromptLibrary):
    @property
    def specs(self):
        return OTHER_SPECS


OTHER_SPECS = dict(L.BUILTIN_PROMPT_SPECS)
_one = L.create_prompt_library({'extract_nodes': {'extract_message': make_override(ov('I', 'J '))}})
_base = OtherSpecsLibrary()
_composed = L._ComposedPromptLibrary(_base, {})
_wrapped = [Message(role='system', content='a'), Message(role='user', content='b')]
_wrapper_result = L.VersionWrapper(lambda ctx: _wrapped)({})
IDENTITY = {
    'create_is_default_library': L.create_prompt_library() is L.default_chat_prompt_library,
    'create_twice': L.create_prompt_library() is L.create_prompt_library(),
    'create_empty_is_default_library': L.create_prompt_library({}) is L.default_chat_prompt_library,
    'composed_group_is_default_group': _one.eval is L.default_chat_prompt_library.eval,
    'composed_overridden_group_is_default_group': _one.extract_nodes is L.default_chat_prompt_library.extract_nodes,
    'composed_specs_is_base_specs': _composed.specs is _base.specs,
    'composed_specs_is_builtin': _composed.specs is L.BUILTIN_PROMPT_SPECS,
    'composed_group_is_base_group': _composed.eval is _base.eval,
    'version_wrapper_same_list': _wrapper_result is _wrapped,
    'version_wrapper_changed_content': [m.content for m in _wrapped],
    'prompt_library_group_twice': L.prompt_library.eval is L.prompt_library.eval,
    'prompt_library_function_twice': L.prompt_library.eval.qa_prompt is L.prompt_library.eval.qa_prompt,
    'prompt_library_other_function': L.prompt_library.eval.qa_prompt is L.prompt_library.eval.eval_prompt,
}
for fact, value in IDENTITY.items():
    add('identity', {'fact': fact}, value)


# ---- the module constants (R25) ----
add('const', {'name': 'PROMPT_GROUPS'}, {g: list(fns) for g, fns in L.PROMPT_GROUPS.items()})
add('const', {'name': 'BUILTIN_PROMPT_SPECS'},
    [[n, s.name, s.response_model.__name__ if s.response_model else None, s.dynamic_schema]
     for n, s in L.BUILTIN_PROMPT_SPECS.items()])
add('const', {'name': 'CHAT_PROMPT_MIGRATION_NOTE'}, L.CHAT_PROMPT_MIGRATION_NOTE)
add('const', {'name': 'PROMPT_LIBRARY_IMPL'}, {g: list(v) for g, v in L.PROMPT_LIBRARY_IMPL.items()})
def _setitem():
    L.BUILTIN_PROMPT_SPECS['x'] = None


add('const', {'name': 'BUILTIN_PROMPT_SPECS_setitem'}, attempt(_setitem))
add('const', {'name': 'DO_NOT_ESCAPE_UNICODE_suffix'},
    [m.content.endswith(DO_NOT_ESCAPE_UNICODE) for m in L.prompt_library.dedupe_nodes.node(PROMPT_CONTEXT)])


def main() -> int:
    if sys.version_info[:2] != (3, 12):
        raise SystemExit(f'Python 3.12 is needed, and this is {sys.version.split()[0]}')
    python = json.dumps('.'.join(str(p) for p in sys.version_info[:3]))
    lines = ['{"python": ' + python + ', "source": "graphiti_core/prompts/lib.py", "cases": [']
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
