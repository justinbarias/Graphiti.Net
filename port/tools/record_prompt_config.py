"""Records results of graphiti_core/llm_client/prompt_config.py from real Python, for the evaluation tests.

The judge has 13 cases for this module, and each one gives an empty tree or a wrong type. The upstream tests
(tests/llm_client/test_llm_runtime.py) check a few values. This file gives the evaluation tests generated override
and route trees from a fixed seed (port/RULES.md, R23 and R25), so that the port can be compared with real results:

- flatten_overrides: each group, each prompt name, shuffled and mixed trees, and the key order of the result.
  Also a wrong group class in a field and a value that is not callable, with the text of each TypeError.
- flatten_routes: a model for a group, a nested group with `default`, each prompt name, mixed trees, a wrong
  value or a wrong group class in a field, and a nested value that is not an LLMModel.
- _nested_group_class: the class name for each group of both parents, spelling variants, and the AttributeError.
- LLMTransport: models with duplicates, blank ids, and the repr. LLMModel: each check of __post_init__ with the text of
  the error, and the comparison and hash of a set of models (the transport by identity).

    .upstream/graphiti/.venv/bin/python port/tools/record_prompt_config.py
    .upstream/graphiti/.venv/bin/python port/tools/record_prompt_config.py --check

It writes tests/Port.Evaluation/values/prompt_config.json. --check writes nothing and exits 1 if the file differs from
what the run would write. The tool needs Python 3.12 and the pinned upstream. The environment variable GRAPHITI_UPSTREAM
names the upstream checkout, and it defaults to .upstream/graphiti in the repository. The tool changes nothing there.
Nothing here uses the network.

A case that the C# types cannot hold has "cs": false. A wrong group class in a field of LLMPromptOverrides, a value
that is not callable, and a nested value that is not an LLMModel are such cases: the C# fields have the types of the
annotations, so only the fields of PromptRoutes that hold a group (`object?`, R22) take a wrong value. The test counts
these cases and does not run them.
"""
from __future__ import annotations

import json
import os
import random
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
UPSTREAM = Path(os.environ.get('GRAPHITI_UPSTREAM') or ROOT / '.upstream' / 'graphiti')
OUT = ROOT / 'tests' / 'Port.Evaluation' / 'values' / 'prompt_config.json'

sys.path.insert(0, str(UPSTREAM))
from graphiti_core.llm_client.client import LLMClient  # noqa: E402
from graphiti_core.llm_client.config import LLMConfig  # noqa: E402
from graphiti_core.llm_client.prompt_config import (  # noqa: E402
    LLMModel,
    LLMPromptOverrides,
    LLMTransport,
    PromptRoutes,
    _nested_group_class,
    flatten_overrides,
    flatten_routes,
)
from graphiti_core.prompts.lib import PROMPT_GROUPS  # noqa: E402

rng = random.Random(20261128)

GROUPS = list(PROMPT_GROUPS)


class FakeLLM(LLMClient):
    def __init__(self) -> None:
        super().__init__(LLMConfig(model='gpt-4.1'), cache=False)

    async def _generate_response(self, messages, response_model=None, max_tokens=None, model_size=None, *, model=None):
        return {}


class OtherLLM(FakeLLM):
    pass


CLIENT_CLASSES = {'FakeLLM': FakeLLM, 'OtherLLM': OtherLLM}

# The pool of clients, transports, and models that the cases refer to by name.
CLIENTS = [{'name': 'c0', 'class': 'FakeLLM'}, {'name': 'c1', 'class': 'FakeLLM'}, {'name': 'c2', 'class': 'OtherLLM'}]
TRANSPORTS = [
    {'name': 't0', 'client': 'c0', 'models': None},
    {'name': 't1', 'client': 'c1', 'models': ['gpt-4.1', 'gpt-4.1-nano', 'gpt-4.1-mini']},
    {'name': 't2', 'client': 'c2', 'models': None},
    {'name': 't3', 'client': 'c0', 'models': None},  # the client of t0, another transport object
]
MODELS = [
    {'name': 'm0', 'transport': 't0', 'id': 'gpt-4.1'},
    {'name': 'm1', 'transport': 't0', 'id': 'gpt-4.1-nano'},
    {'name': 'm2', 'transport': 't1', 'id': 'gpt-4.1'},
    {'name': 'm3', 'transport': 't1', 'id': 'gpt-4.1-mini'},
    {'name': 'm4', 'transport': 't2', 'id': 'claude'},
    {'name': 'm5', 'transport': 't0', 'id': 'gpt-4.1', 'max_tokens': 100},
    {'name': 'm6', 'transport': 't0', 'id': 'gpt-4.1', 'overrides': {'extract_nodes': {'extract_message': 'b9'}}},
    {'name': 'm7', 'transport': 't3', 'id': 'gpt-4.1'},
    {'name': 'm8', 'transport': 't0', 'id': 'gpt-4.1'},  # a second object that equals m0
    {'name': 'm9', 'transport': 't0', 'id': 'gpt-4.1', 'max_tokens': 100,
     'overrides': {'eval': {'qa_prompt': 'b8'}}},  # equals m5
]
MODEL_NAMES = [m['name'] for m in MODELS]

BUILDERS: dict[str, object] = {}


def builder(marker: str):
    if marker not in BUILDERS:
        def build(context, _marker=marker):
            return _marker
        build.marker = marker  # type: ignore[attr-defined]
        BUILDERS[marker] = build
    return BUILDERS[marker]


CLIENT_POOL, TRANSPORT_POOL, MODEL_POOL = {}, {}, {}


def error_of(exc: BaseException) -> list:
    return [type(exc).__name__, str(exc)]


# ----- the trees -----

def group_class(parent, name: str):
    nested = ''.join(part.capitalize() for part in name.split('_'))
    return getattr(parent, nested)


def python_value(value, models_by_name):
    if isinstance(value, dict):
        if '$value' in value:
            return value['$value']
        if '$model' in value:
            return models_by_name[value['$model']]
        raise ValueError(value)
    return value


def build_overrides(spec: dict) -> LLMPromptOverrides:
    kwargs = {}
    for group, methods in spec.items():
        cls = group_class(LLMPromptOverrides, methods.get('$class', group)) if isinstance(methods, dict) else None
        values = {}
        for name, value in methods.items():
            if name == '$class':
                continue
            values[name] = builder(value) if isinstance(value, str) else python_value(value, MODEL_POOL)
        kwargs[group] = cls(**values)
    return LLMPromptOverrides(**kwargs)


def build_routes(spec: dict) -> PromptRoutes:
    kwargs = {}
    for group, value in spec.items():
        if isinstance(value, str):
            kwargs[group] = MODEL_POOL[value]
        elif isinstance(value, dict) and ('$value' in value or '$model' in value):
            kwargs[group] = python_value(value, MODEL_POOL)
        else:
            cls = group_class(PromptRoutes, value.get('$class', group))
            values = {}
            for name, item in value.items():
                if name == '$class':
                    continue
                values[name] = MODEL_POOL[item] if isinstance(item, str) else python_value(item, MODEL_POOL)
            kwargs[group] = cls(**values)
    return PromptRoutes(**kwargs)


def names_of(group: str) -> list[str]:
    return PROMPT_GROUPS[group]


def rand_overrides_spec() -> dict:
    spec: dict = {}
    for group in rng.sample(GROUPS, rng.randint(0, len(GROUPS))):
        names = rng.sample(names_of(group), rng.randint(0, len(names_of(group))))
        spec[group] = {name: f'b{rng.randint(0, 40)}' for name in names}
    return spec


def rand_routes_spec() -> dict:
    spec: dict = {}
    for group in rng.sample(GROUPS, rng.randint(0, len(GROUPS))):
        if rng.random() < 0.4:
            spec[group] = rng.choice(MODEL_NAMES)
            continue
        names = rng.sample(names_of(group), rng.randint(0, len(names_of(group))))
        nested = {name: rng.choice(MODEL_NAMES) for name in names}
        if rng.random() < 0.5:
            nested['default'] = rng.choice(MODEL_NAMES)
        keys = list(nested)
        rng.shuffle(keys)
        spec[group] = {key: nested[key] for key in keys}
    return spec


def marker_of(function) -> str:
    return function.marker


def model_name_of(model) -> str:
    for name, candidate in MODEL_POOL.items():
        if candidate is model:
            return name
    raise AssertionError('a model outside the pool')


def run_overrides(spec: dict, representable: bool) -> dict:
    case = {'spec': spec, 'cs': representable}
    try:
        flat = flatten_overrides(build_overrides(spec))
        case['r'] = {'ok': [[key, marker_of(function)] for key, function in flat.items()]}
    except Exception as exc:  # noqa: BLE001
        case['r'] = {'err': error_of(exc)}
    return case


def run_routes(spec: dict, representable: bool) -> dict:
    case = {'spec': spec, 'cs': representable}
    try:
        flat = flatten_routes(build_routes(spec))
        case['r'] = {'ok': [[key, model_name_of(model)] for key, model in flat.items()]}
    except Exception as exc:  # noqa: BLE001
        case['r'] = {'err': error_of(exc)}
    return case


def record_overrides() -> list:
    cases = [run_overrides({}, True)]
    # Each group with all its names, each prompt name alone, and the groups in reverse order.
    for group in GROUPS:
        cases.append(run_overrides({group: {name: f'b{i}' for i, name in enumerate(names_of(group))}}, True))
        for name in names_of(group):
            cases.append(run_overrides({group: {name: 'b1'}}, True))
        cases.append(run_overrides({group: {}}, True))
    cases.append(run_overrides({g: {n: f'b{i}' for i, n in enumerate(names_of(g))} for g in reversed(GROUPS)}, True))
    cases.append(run_overrides({g: {n: f'b{i}' for i, n in reversed(list(enumerate(names_of(g))))}
                                for g in GROUPS}, True))
    for _ in range(120):
        cases.append(run_overrides(rand_overrides_spec(), True))
    # A group class that does not belong to the field, and a value that is not callable.
    for group in GROUPS:
        other = rng.choice([g for g in GROUPS if g != group])
        cases.append(run_overrides({group: {'$class': other, **{n: 'b2' for n in names_of(other)[:1]}}}, False))
        cases.append(run_overrides({group: {'$class': other}}, False))
        for name in names_of(group):
            for bad in ({'$value': 'x'}, {'$value': 5}, {'$value': 1.5}, {'$value': True}, {'$model': 'm0'},
                        {'$value': ['a']}, {'$value': {'k': 1}}):
                cases.append(run_overrides({group: {name: bad}}, False))
    # A bad value after a good one in the same group, and in a later group: the error comes from the bad value.
    cases.append(run_overrides({'extract_nodes': {'extract_message': 'b1'},
                                'dedupe_edges': {'resolve_edge': {'$value': 'x'}}}, False))
    cases.append(run_overrides({'extract_nodes': {'extract_message': 'b1', 'extract_json': {'$value': 7}},
                                'eval': {'qa_prompt': 'b3'}}, False))
    return cases


def record_routes() -> list:
    cases = [run_routes({}, True)]
    for group in GROUPS:
        cases.append(run_routes({group: 'm0'}, True))
        cases.append(run_routes({group: {}}, True))
        cases.append(run_routes({group: {'default': 'm1'}}, True))
        for name in names_of(group):
            cases.append(run_routes({group: {name: 'm2'}}, True))
            cases.append(run_routes({group: {'default': 'm3', name: 'm4'}}, True))
        cases.append(run_routes({group: {n: MODEL_NAMES[i % len(MODEL_NAMES)] for i, n in enumerate(names_of(group))}},
                                True))
        cases.append(run_routes({group: {**{n: 'm5' for n in names_of(group)}, 'default': 'm6'}}, True))
    cases.append(run_routes({g: f'm{i % 5}' for i, g in enumerate(reversed(GROUPS))}, True))
    for _ in range(120):
        cases.append(run_routes(rand_routes_spec(), True))
    # A wrong value or a wrong group class in a field (the field is `object?` in C#).
    for group in GROUPS:
        for bad in ({'$value': 'x'}, {'$value': 5}, {'$value': 2.5}, {'$value': True}, {'$value': ['a']},
                    {'$value': {'k': 1}}, {'$model': 'm0'}):
            cases.append(run_routes({group: bad}, True))
        other = rng.choice([g for g in GROUPS if g != group])
        cases.append(run_routes({group: {'$class': other}}, True))
        cases.append(run_routes({group: {'$class': other, 'default': 'm0'}}, True))
        # A model first, then a bad value in a later group.
        cases.append(run_routes({group: 'm1', rng.choice(GROUPS): {'$value': 'x'}}, True))
    # A nested value that is not an LLMModel: C# holds LLMModel? and has no form for it.
    for group in GROUPS:
        cases.append(run_routes({group: {'default': {'$value': 'x'}}}, False))
        cases.append(run_routes({group: {'default': {'$value': 5}}}, False))
        for name in names_of(group):
            cases.append(run_routes({group: {name: {'$value': 'x'}}}, False))
            cases.append(run_routes({group: {name: {'$value': None}}}, True))
    return cases


def record_nested() -> list:
    cases = []
    names = GROUPS + ['EXTRACT_NODES', 'Extract_Nodes', 'extract__nodes', 'extract_nodes_', '_extract_nodes',
                      '', 'x', 'nope_group', 'eval ', 'EVAL', 'eval_', 'dedupe', 'ünï', 'extract_nodes_and_edges_x',
                      'summarize_sagas', 'ǆ_nodes', 'ß', 'i̇_x']
    for parent in (LLMPromptOverrides, PromptRoutes):
        for name in names:
            case = {'parent': parent.__name__, 'group': name}
            try:
                case['r'] = {'ok': _nested_group_class(parent, name).__qualname__}
            except Exception as exc:  # noqa: BLE001
                case['r'] = {'err': error_of(exc)}
            cases.append(case)
    return cases


MODEL_ID_POOL = ['gpt-4.1', 'gpt-4.1-nano', 'x', '', ' ', '  ', '\t', ' ', ' ', '​', '\x1f', 'é',
                 '日本', "it's", 'a"b', 'a\nb', 'claude', 'a', 'b', 'B', '\U0001F600', 'z' * 40, ' pad ']


def record_transports() -> list:
    cases = []
    model_lists = [None, [], [''], [' '], ['\t'], [' '], ['​'], ['a'], ['a', 'a'], ['b', 'a', 'b', 'a'],
                   ['a', ''], ['a', ' '], ["it's", 'a"b'], ['é', 'e', 'E', 'Z', 'a', 'B'], ['\U0001F600', '日本', 'z'],
                   ['퟿', '￿', '\U00010000'], ['x' * 50]]
    for _ in range(60):
        model_lists.append(rng.sample(MODEL_ID_POOL, rng.randint(0, 6)) + (['a'] if rng.random() < 0.2 else []))
    for client in ('c0', 'c2'):
        for models in model_lists:
            case = {'client': client, 'models': models}
            try:
                transport = LLMTransport(CLIENT_POOL[client], models=models)
                case['r'] = {'ok': {
                    'models': sorted(transport.models) if transport.models is not None else None,
                    'repr': repr(transport),
                    'supports': {model_id: transport.supports(model_id) for model_id in MODEL_ID_POOL},
                }}
            except Exception as exc:  # noqa: BLE001
                case['r'] = {'err': error_of(exc)}
            cases.append(case)
    return cases


def record_models() -> list:
    cases = []
    max_tokens_pool = [None, None, 1, 5, 100, 0, -1, -100, 2**40]
    overrides_pool = [None, None, {}, {'extract_nodes': {'extract_message': 'b1'}},
                      {'eval': {'qa_prompt': 'b2', 'eval_prompt': 'b3'}, 'dedupe_edges': {'resolve_edge': 'b4'}}]
    for transport in ('t0', 't1', 't2'):
        for model_id in MODEL_ID_POOL + ['gpt-4.1-mini']:
            for max_tokens in max_tokens_pool:
                if rng.random() < 0.6:
                    continue
                overrides = rng.choice(overrides_pool)
                case = {'transport': transport, 'id': model_id, 'max_tokens': max_tokens, 'overrides': overrides}
                try:
                    model = TRANSPORT_POOL[transport].model(
                        model_id,
                        prompt_overrides=build_overrides(overrides) if overrides is not None else None,
                        max_tokens=max_tokens,
                    )
                    case['r'] = {'ok': {
                        'id': model.id,
                        'max_tokens': model.max_tokens,
                        'flat': [[key, marker_of(function)] for key, function in model.flat_overrides.items()],
                    }}
                except Exception as exc:  # noqa: BLE001
                    case['r'] = {'err': error_of(exc)}
                cases.append(case)
    return cases


def record_equality() -> dict:
    eq = []
    hash_eq = []
    for a in MODEL_NAMES:
        row, hrow = [], []
        for b in MODEL_NAMES:
            row.append(MODEL_POOL[a] == MODEL_POOL[b])
            hrow.append(hash(MODEL_POOL[a]) == hash(MODEL_POOL[b]))
        eq.append(row)
        hash_eq.append(hrow)
    # A hash that is equal for an equal pair, as the test needs. An unequal pair has no recorded hash relation.
    return {'names': MODEL_NAMES, 'eq': eq, 'hash_eq_when_equal': [[h if e else None for e, h in zip(er, hr)]
                                                                    for er, hr in zip(eq, hash_eq)]}


def main() -> int:
    CLIENT_POOL.update({c['name']: CLIENT_CLASSES[c['class']]() for c in CLIENTS})
    TRANSPORT_POOL.update({t['name']: LLMTransport(CLIENT_POOL[t['client']], models=t['models']) for t in TRANSPORTS})
    for m in MODELS:
        overrides = build_overrides(m['overrides']) if 'overrides' in m else None
        MODEL_POOL[m['name']] = TRANSPORT_POOL[m['transport']].model(
            m['id'], prompt_overrides=overrides, max_tokens=m.get('max_tokens')
        )

    data = {
        'python': '.'.join(str(p) for p in sys.version_info[:3]),
        'source': 'graphiti_core/llm_client/prompt_config.py',
        'groups': {group: names for group, names in PROMPT_GROUPS.items()},
        'clients': CLIENTS,
        'transports': TRANSPORTS,
        'models': MODELS,
        'overrides': record_overrides(),
        'routes': record_routes(),
        'nested': record_nested(),
        'transport_cases': record_transports(),
        'model_cases': record_models(),
        'equality': record_equality(),
    }
    text = json.dumps(data, ensure_ascii=True, separators=(',', ':')) + '\n'
    count = sum(len(data[k]) for k in ('overrides', 'routes', 'nested', 'transport_cases', 'model_cases'))
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
